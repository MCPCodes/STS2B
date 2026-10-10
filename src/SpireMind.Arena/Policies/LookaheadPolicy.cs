using System.Globalization;
using System.Text;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The strong baseline: before every action it plays out the rest of the turn in every way the
/// legal actions allow (cards, potions, the answers to card choices), lets the enemies do what
/// their intents show, and takes the first action of the line that leaves the best position:
/// a won fight first, then staying alive, then HP kept, then damage done to the enemies that
/// matter, then what lasts beyond the turn (Vulnerable, Weak, Strength, powers).
/// <para>
/// It searches on copies of the running fight in which everything the player cannot know has
/// been replaced (<see cref="Fight.CloneDeterminized"/>): the order of the draw pile and every
/// random number still to come. A line that draws cards or rolls dice is therefore judged on a
/// few such copies and the results are averaged. What it reads off a position is what a
/// snapshot would show of it (HP, Block, powers, what was exhausted); the hand a copy has drawn
/// for the next turn and the intents it has rolled for it are made up, and are not looked at.
/// </para>
/// <para>
/// Potions are held back: a line that uses one is taken only when it turns a death into
/// survival or wins the fight this turn, or, against an elite or a boss, when it saves a lot.
/// It sees one turn, never throws a potion away, and keeps nothing between calls.
/// </para>
/// </summary>
public sealed class LookaheadPolicy : IEnginePolicy
{
    // ---------- the search ----------

    /// <summary>Positions tried per search of one copy. A hand of the starter deck needs a few
    /// dozen; past the budget a line is only followed to the end of the turn, not branched.
    /// Results stop improving well below this.</summary>
    public const int DefaultBudget = 600;

    /// <summary>Copies a decision is averaged over when its lines touch what the player cannot know.</summary>
    public const int DefaultWorlds = 3;

    /// <summary>Actions deep at which a line stops branching, and at which it is cut off altogether.</summary>
    private const int MaxDepth = 60, HardDepth = 400;

    // ---------- the evaluation; one point is one HP ----------

    private const double Win = 10_000, Loss = -10_000;

    /// <summary>Damage a deck is taken to deal per turn when it does nothing else: how long enemies will live.</summary>
    private const double DamagePerTurn = 20;

    /// <summary>An enemy with fewer HP than this still takes a whole card to finish, so it counts as
    /// having this many: the last blow is worth a card's damage, and chipping at what is left is worth nothing.</summary>
    private const double LastHit = DamagePerTurn / 3;

    /// <summary>What an enemy is taken to deal per turn over the rest of the fight: a base plus a share of its max HP.</summary>
    private const double UsualBase = 4, UsualPerMaxHp = 1 / 12.0;

    /// <summary>How far the attack an enemy shows now stands for its later turns; the rest is the usual figure.</summary>
    private const double ShownShare = 0.3;

    /// <summary>An HP is worth more the fewer are left: below these marks each one counts for this much more.</summary>
    private const double LowHp = 40, LowHpExtra = 0.5, VeryLowHp = 20, VeryLowHpExtra = 1.0;

    /// <summary>A turn of Vulnerable on an enemy adds half the damage dealt to it, a turn of Weak
    /// takes a quarter off its attacks; both are discounted, since the turn may go otherwise.</summary>
    private const double VulnerableTurn = 0.5 * DamagePerTurn * 0.6, WeakTurn = 0.25 * 0.8;

    /// <summary>Attack hits per turn that a point of the player's Strength adds to, and Block cards per turn for a point of Dexterity.</summary>
    private const double HitsPerTurn = 2.2, BlocksPerTurn = 1.5;

    /// <summary>What is still to come is worth less than what is certain.</summary>
    private const double Later = 0.8;

    /// <summary>A lasting power the evaluation has no rule for, per turn the fight is taken to last.</summary>
    private const double OtherBuffPerTurn = 1.2;

    /// <summary>A debuff, on an enemy or on the player, that the evaluation has no rule for.</summary>
    private const double OtherDebuff = 2;

    /// <summary>A status or curse card that is out of the deck for the rest of the fight, and any other card that is.</summary>
    private const double JunkGone = 0.5, CardGone = -0.3;

    /// <summary>What the next turn starts with beyond the usual: a point of Block that was kept
    /// (counted up to a limit), and an energy more than the turn's normal amount.</summary>
    private const double BlockKept = 0.5, MostBlockKept = 30, EnergyAhead = 3;

    /// <summary>The longest the rest of a fight is taken to last, in turns.</summary>
    private const double Horizon = 8;

    /// <summary>Taken off per action, so that of two lines worth the same the shorter is chosen and nothing is played for nothing.</summary>
    private const double PerAction = 0.01;

    /// <summary>Taken off per potion used, so that a potion is never used where the same result comes without it.</summary>
    private const double PerPotion = 1;

    /// <summary>A line with a potion is taken when it is this much better than the best line
    /// without one: in any fight when it turns a death into survival or wins the fight this turn
    /// (in at least half the copies), and against an elite or a boss when it saves this many HP.</summary>
    private const double Decisive = Win / 2, WorthAPotion = 10;

    private readonly int _seed, _budget, _worlds;
    private readonly GreedyPolicy _withoutFight = new();

    public LookaheadPolicy() : this(1) { }

    /// <param name="seed">Seeds the reshuffles: with the same seed, the same snapshot gets the same answer.</param>
    /// <param name="budget">Positions per search of one copy.</param>
    /// <param name="worlds">Copies to average over when a line touches what is hidden.</param>
    public LookaheadPolicy(int seed, int budget = DefaultBudget, int worlds = DefaultWorlds)
    {
        _seed = seed;
        _budget = Math.Max(1, budget);
        _worlds = Math.Max(1, worlds);
    }

    public string Name => "Lookahead";

    /// <summary>Without the fight there is nothing to search on: it decides as Greedy does.</summary>
    public string Choose(GameSnapshot snapshot) => _withoutFight.Choose(snapshot);

    public string Choose(GameSnapshot snapshot, Fight fight)
    {
        var offered = snapshot.Actions;
        if (offered.Count == 0) return "end_turn";
        if (offered.Count == 1 || fight.IsOver) return offered[0].Id;

        var measure = new Measure(fight);
        var shown = Hash(snapshot);
        var hasPotion = fight.Potions.Any(p => p is { Usage: PotionUsage.CombatOnly or PotionUsage.AnyTime });
        var bigFight = fight.RoomType is RoomType.Elite or RoomType.Boss;

        // A line with a potion in it must beat the best line without one by this much to be taken.
        bool PotionPays((string Id, double Value)? with, (string Id, double Value)? without) =>
            with is { } w && w.Value - (without?.Value ?? double.NegativeInfinity) is var gain && (gain >= Decisive || (bigFight && gain >= WorthAPotion));

        var plain = new Tally();
        var withPotions = new Tally();
        var worlds = 1;
        for (var k = 0; k < worlds; k++)
        {
            var search = new Search(measure, _budget, hasPotion);
            search.Rate(Determinize(fight, WorldSeed(shown, k)), plain, withPotions);

            // One copy is the whole truth unless a line drew a card or rolled a die. For lines that
            // only do so after a potion, more copies are worth it only if such a line is in the running.
            if (k == 0 && (search.TouchedHidden || (search.PotionTouchedHidden && PotionPays(withPotions.Best(1), plain.Best(1)))))
                worlds = _worlds;
        }

        var choice = plain.Best(worlds);
        if (hasPotion && withPotions.Best(worlds) is { } potionLine && PotionPays(potionLine, choice)) choice = potionLine;
        return choice is { } best && offered.Any(a => a.Id == best.Id) ? best.Id : _withoutFight.Choose(snapshot);
    }

    // ---------- copies that hide what the player cannot know ----------

    /// <summary>FNV-1a over what the snapshot shows, so that a reshuffle depends on nothing else.</summary>
    private static ulong Hash(GameSnapshot snapshot)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in snapshot.Screen) hash = unchecked((hash ^ c) * 1099511628211UL);
        foreach (var c in snapshot.State.ToJsonString()) hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }

    /// <summary>The seed of the decision's k-th copy: the player's seed, what is shown, and k.</summary>
    private int WorldSeed(ulong shown, int k) =>
        unchecked((int)(new GameRng(shown ^ ((ulong)(uint)_seed << 32) ^ (uint)k).NextU64() >> 33));

    /// <summary>
    /// A copy of <paramref name="live"/> to search on, with everything the player cannot know
    /// replaced. The engine orders the hidden cards by what they look like before it reshuffles
    /// them, so two fights that show the same give the same copy, also in the middle of a card choice.
    /// </summary>
    private static Fight Determinize(Fight live, int seed) => live.CloneDeterminized(seed);

    // ---------- adding up over copies ----------

    /// <summary>The value of every action of the decision, summed over the copies searched.</summary>
    private sealed class Tally
    {
        private readonly List<string> _order = new();
        private readonly Dictionary<string, (double Sum, int Count)> _sums = new(StringComparer.Ordinal);

        public void Add(string id, double value)
        {
            if (!_sums.TryGetValue(id, out var sum)) _order.Add(id);
            _sums[id] = (sum.Sum + value, sum.Count + 1);
        }

        /// <summary>The action with the best average; of equals, the one listed first. An action
        /// that did not run in every copy, or could not run at all, is left out.</summary>
        public (string Id, double Value)? Best(int worlds)
        {
            (string Id, double Value)? best = null;
            foreach (var id in _order)
            {
                var (sum, count) = _sums[id];
                if (count < worlds || double.IsNegativeInfinity(sum)) continue;
                var value = sum / count;
                if (best is null || value > best.Value.Value + 1e-9) best = (id, value);
            }
            return best;
        }
    }

    // ---------- the search of one copy ----------

    /// <summary>What a position, or an action, leads to at best.</summary>
    /// <param name="Plain">Without using a potion from here on.</param>
    /// <param name="Potion">With potions allowed; never less than <paramref name="Plain"/> when both were searched in full.</param>
    /// <param name="Complete">Nothing below was cut short by the budget, so the values can be used again as they are.</param>
    private readonly record struct Rated(double Plain, double Potion, bool Complete);

    private sealed class Search(Measure measure, int budget, bool usePotions)
    {
        private readonly Dictionary<string, Rated> _seen = new(StringComparer.Ordinal);
        private int _spent, _stopAt;

        /// <summary>A line without a potion drew a card, shuffled, or used a random number: other copies may go differently.</summary>
        public bool TouchedHidden { get; private set; }

        /// <summary>The same for a line with a potion in it.</summary>
        public bool PotionTouchedHidden { get; private set; }

        private bool Exhausted => _spent >= _stopAt;

        /// <summary>
        /// Rates every action of the decision <paramref name="root"/> stands at, once for lines
        /// that use no potion and once for lines that may. The budget is shared out evenly, so
        /// that the first action cannot use it all up; what one action leaves over goes to the
        /// ones after it.
        /// </summary>
        public void Rate(Fight root, Tally plain, Tally withPotions)
        {
            var actions = Candidates(root);
            for (var i = 0; i < actions.Count; i++)
            {
                _stopAt = _spent + Math.Max(1, (budget - _spent) / (actions.Count - i));
                var rated = After(root, actions[i], depth: 0, afterPotion: false);
                plain.Add(actions[i].Id, rated.Plain);
                withPotions.Add(actions[i].Id, rated.Potion);
            }
        }

        /// <summary>What <paramref name="action"/> leads to at best, the action's own cost taken off.</summary>
        /// <param name="afterPotion">A potion was used earlier in the line.</param>
        private Rated After(Fight from, LegalAction action, int depth, bool afterPotion)
        {
            var isEndTurn = action.Id == "end_turn";
            var isPotion = action.Id.StartsWith("potion:", StringComparison.Ordinal);
            var rolls = isEndTurn ? 0 : Rolls(from);
            var child = from.Clone();
            bool ok;
            try
            {
                ok = child.Execute(action.Id).Ok;
            }
            catch (Exception)
            {
                // A card that breaks is a card not to play. The copy is dropped; nothing else saw it.
                ok = false;
            }
            _spent++;
            if (!ok) return new Rated(double.NegativeInfinity, double.NegativeInfinity, true);

            // The enemy turn and the next hand are the same guess for every line; only what happens
            // before the turn ends can make copies differ. (A won fight has no piles left to compare.)
            afterPotion |= isPotion;
            if (!isEndTurn && (Rolls(child) != rolls || (!child.IsOver && child.DrawPile.Count < from.DrawPile.Count)))
            {
                if (afterPotion) PotionTouchedHidden = true;
                else TouchedHidden = true;
            }

            Rated below;
            if (measure.IsLeaf(child) || depth >= HardDepth)
            {
                var value = measure.Evaluate(child);
                below = new Rated(value, value, measure.IsLeaf(child));
            }
            else
            {
                below = Explore(child, depth + 1, afterPotion);
            }
            return isPotion
                ? new Rated(double.NegativeInfinity, below.Potion - PerAction - PerPotion, below.Complete)
                : new Rated(below.Plain - PerAction, below.Potion - PerAction, below.Complete);
        }

        /// <summary>The best that can be reached from <paramref name="f"/>, a position that awaits a decision.</summary>
        private Rated Explore(Fight f, int depth, bool afterPotion)
        {
            var key = Key(f);
            if (_seen.TryGetValue(key, out var known) && (known.Complete || Exhausted)) return known;

            var actions = Candidates(f);
            double plain = double.NegativeInfinity, potion = double.NegativeInfinity;
            var complete = true;
            for (var i = 0; i < actions.Count; i++)
            {
                // Out of budget, or too deep: only the first action, which ends the turn or moves the choice on.
                if (i > 0 && (Exhausted || depth >= MaxDepth))
                {
                    complete = false;
                    break;
                }
                var rated = After(f, actions[i], depth, afterPotion);
                complete &= rated.Complete;
                plain = Math.Max(plain, rated.Plain);
                potion = Math.Max(potion, rated.Potion);
            }
            var result = new Rated(plain, potion, complete);
            _seen[key] = result;
            return result;
        }

        /// <summary>
        /// The actions worth telling apart, the one that closes the turn or the choice first. Left
        /// out: throwing a potion away, taking a pick back (which could go round for ever), potions
        /// when this search does without them, and a second copy of a card that reads the same as
        /// one already listed.
        /// </summary>
        private List<LegalAction> Candidates(Fight f)
        {
            var list = new List<LegalAction>();
            var told = new HashSet<string>(StringComparer.Ordinal);
            foreach (var action in f.LegalActions())
            {
                var id = action.Id;
                if (id.StartsWith("discard_potion:", StringComparison.Ordinal)) continue;
                if (action.Label.StartsWith("Deselect", StringComparison.Ordinal)) continue;

                var same = id;
                if (id.StartsWith("potion:", StringComparison.Ordinal))
                {
                    if (!usePotions) continue;
                    if (TrySplit(id, "potion:", out var slot, out var at) && slot < f.Potions.Count && f.Potions[slot] is { } potion)
                        same = "potion:" + potion.ClassName + at;
                }
                else if (TrySplit(id, "play:", out var i, out var target) && i < f.Hand.Count)
                {
                    same = "play:" + Looks(f, f.Hand[i]) + target;
                }
                else if (TrySplit(id, "select:", out var pick, out _) && pick < f.Hand.Count)
                {
                    same = "select:" + Looks(f, f.Hand[pick]);
                }
                if (!told.Add(same)) continue;

                if (id is "end_turn" or "confirm") list.Insert(0, action);
                else list.Add(action);
            }
            return list;
        }

        /// <summary>"play:3>e1" as 3 and ">e1"; the target part is empty when there is none.</summary>
        private static bool TrySplit(string id, string prefix, out int index, out string target)
        {
            index = -1;
            target = "";
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var arrow = id.IndexOf('>');
            if (arrow >= 0) target = id[arrow..];
            var digits = arrow < 0 ? id.AsSpan(prefix.Length) : id.AsSpan(prefix.Length, arrow - prefix.Length);
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out index) && index >= 0;
        }

        /// <summary>A hand card as the player reads it: two cards that read the same are played the same.</summary>
        private static string Looks(Fight f, CardInstance card) =>
            $"{card.Title}|{f.CostText(card)}|{Fight.CardText(f, card, PileType.Hand)}|{(card.ExhaustOnNextPlay ? 'x' : '-')}";

        /// <summary>Random numbers used so far, over all streams.</summary>
        private static int Rolls(Fight f)
        {
            var r = f.Rng;
            return r.Shuffle.Counter + r.MonsterAi.Counter + r.Niche.Counter + r.CombatTargets.Counter + r.CombatCardSelection.Counter
                   + r.CombatCardGeneration.Counter + r.CombatPotionGeneration.Counter + r.CombatEnergyCosts.Counter + r.Encounter.Counter;
        }

        /// <summary>
        /// Positions that differ only in the order they were reached share a key: the creatures,
        /// the hand as a set, the other piles, the potions, and how much has happened. Inside one
        /// copy a card's id stands for the card.
        /// </summary>
        private static string Key(Fight f)
        {
            var key = new StringBuilder(256);
            var inv = CultureInfo.InvariantCulture;
            key.Append(inv, $"{f.Turn},{f.Energy},{f.History.EntryCount},{f.CardsPlayed}");
            Append(key, f.Player);
            foreach (var enemy in f.Roster)
            {
                if (!enemy.InCombat)
                {
                    key.Append("|-");
                    continue;
                }
                Append(key, enemy);
                key.Append(enemy.Monster!.NextMove?.Id).Append(enemy.Monster.IsStunned ? '!' : '.');
            }

            key.Append("|H");
            foreach (var card in f.Hand.OrderBy(c => c.Id))
            {
                key.Append(inv, $"{card.Id}:{card.UpgradeLevel}:{card.LocalCostValue()}:{(int)card.Keywords}:{card.ReplayCount}:{card.AfflictionAmount}");
                key.Append(card.RetainThisTurn ? 'r' : '-').Append(card.ExhaustOnNextPlay ? 'x' : '-');
                foreach (var name in card.Def.Vars.Names) key.Append(inv, $"{card.Get(name)}/");
                key.Append(';');
            }
            key.Append("|D");
            foreach (var card in f.DrawPile) key.Append(inv, $"{card.Id},");
            key.Append("|X");
            foreach (var id in f.DiscardPile.Select(c => c.Id).Order()) key.Append(inv, $"{id},");
            key.Append("|E");
            foreach (var id in f.ExhaustPile.Select(c => c.Id).Order()) key.Append(inv, $"{id},");
            key.Append("|P");
            foreach (var potion in f.Potions) key.Append(potion?.ClassName).Append(',');

            // A choice in progress: which cards are offered and which are picked is only in the snapshot.
            if (f.HasPendingChoice) key.Append("|C").Append(f.Snapshot().State.ToJsonString());
            return key.ToString();
        }

        private static void Append(StringBuilder key, Creature c)
        {
            var inv = CultureInfo.InvariantCulture;
            key.Append(inv, $"|{c.Hp}/{c.MaxHp}/{c.Block}");
            foreach (var power in c.Powers) key.Append(inv, $"{power.ClassName}{power.Amount}/{power.DisplayAmount},");
        }
    }

    // ---------- what a position is worth ----------

    /// <summary>
    /// The yardstick of one decision, taken from the fight as the snapshot shows it now: how
    /// dangerous each enemy looks, and so what a point of damage on it is worth. Every position
    /// the search reaches at the end of the turn is measured against it.
    /// </summary>
    private sealed class Measure
    {
        private readonly int _turn;

        // By the enemy's e-index. HP is -1 for an enemy that was not in the fight at the decision.
        private readonly int[] _hp, _strength, _hits;
        private readonly double[] _threat, _worth, _turns;
        private readonly double _fightLeft, _bestWorth;
        private readonly Dictionary<string, int> _powers = new(StringComparer.Ordinal);
        private readonly Dictionary<(int Enemy, string Power), int> _enemyPowers = new();
        private readonly int _junk, _exhausted, _maxEnergy;

        public Measure(Fight now)
        {
            _turn = now.Turn;
            var n = now.Roster.Count;
            _hp = new int[n];
            _strength = new int[n];
            _hits = new int[n];
            _threat = new double[n];
            _worth = new double[n];
            _turns = new double[n];

            var living = new List<int>();
            for (var j = 0; j < n; j++)
            {
                var enemy = now.Roster[j];
                _hp[j] = -1;
                if (!now.InFight(enemy)) continue;
                _hp[j] = enemy.Hp;
                _strength[j] = enemy.PowerAmount<StrengthPower>();
                foreach (var power in enemy.Powers)
                    if (power.IsVisible) _enemyPowers[(j, power.ClassName)] = power.Amount;
                living.Add(j);

                // The attack it shows: the intent's number per hit, times the hits.
                var shown = 0;
                if (enemy.Monster is { IsStunned: false, NextMove: { } move })
                {
                    foreach (var intent in move.Intents)
                    {
                        if (!intent.IsAttack) continue;
                        var hits = Math.Max(1, intent.Repeats!());
                        shown += now.IntentDamage(enemy, intent) * hits;
                        _hits[j] += hits;
                    }
                }
                _hits[j] = Math.Max(1, _hits[j]);

                // What it shows now says only so much about its later turns; its size is the rest of the guess.
                var usual = UsualBase + enemy.MaxHp * UsualPerMaxHp;
                _threat[j] = shown > 0 ? ShownShare * shown + (1 - ShownShare) * usual : usual;
            }

            // Enemies are best killed in order of threat per HP, and damage on one shortens its
            // life and that of every enemy behind it.
            double PerHp(int j) => _threat[j] / Math.Max(1, _hp[j] + now.Roster[j].Block);
            living.Sort((a, b) => PerHp(b).CompareTo(PerHp(a)) is var byThreat and not 0 ? byThreat : a.CompareTo(b));
            var behind = 0.0;
            for (var k = living.Count - 1; k >= 0; k--)
            {
                behind += _threat[living[k]];
                _worth[living[k]] = behind / DamagePerTurn;
            }

            // How long each enemy, and the fight, is taken to last from here. Fixed for the
            // decision: were they read off the position reached, a line that shortens the fight
            // would lose what its Strength and its debuffs are worth, and look the worse for it.
            var toughness = 0.0;
            foreach (var j in living)
            {
                _turns[j] = Math.Clamp(Math.Ceiling(_hp[j] / DamagePerTurn), 1, Horizon);
                toughness += _hp[j];
                _bestWorth = Math.Max(_bestWorth, _worth[j]);
            }
            _fightLeft = Math.Clamp(Math.Ceiling(toughness / DamagePerTurn), 1, Horizon);

            foreach (var power in now.Player.Powers)
                if (power.IsVisible) _powers[power.ClassName] = power.Amount;
            _junk = now.ExhaustPile.Count(IsJunk);
            _exhausted = now.ExhaustPile.Count;
            _maxEnergy = now.MaxEnergy;
        }

        private static bool IsJunk(CardInstance card) => card.Type is CardType.Status or CardType.Curse;

        /// <summary>The turn the decision belongs to is over (the enemies have acted), or the fight is.</summary>
        public bool IsLeaf(Fight f) => f.IsOver || f.Turn != _turn;

        public double Evaluate(Fight f)
        {
            var damage = 0.0;
            for (var j = 0; j < _hp.Length; j++)
            {
                if (_hp[j] < 0) continue;
                var enemy = f.Roster[j];
                // One that fled took its HP along: nothing was done to it.
                damage += (StillToDo(_hp[j]) - StillToDo(enemy.IsDead ? 0 : enemy.Hp)) * _worth[j];
            }

            // Of two ways to lose, the one that came closer.
            if (f.IsOver) return f.Won ? Win + HpValue(f.Player.Hp) : Loss + damage * 0.01;

            var score = HpValue(f.Player.Hp) + damage;

            // What the enemies still standing are taken to deal per turn, and what is on them.
            var danger = 0.0;
            for (var j = 0; j < _hp.Length; j++)
            {
                var enemy = f.Roster[j];
                if (_hp[j] < 0 || !f.InFight(enemy)) continue;
                danger += _threat[j];

                // Strength it gained or lost for good works on every later hit.
                score -= (enemy.PowerAmount<StrengthPower>() - _strength[j]) * _hits[j] * Math.Min(_turns[j], 5) * Later;
                foreach (var power in enemy.Powers)
                {
                    if (!power.IsVisible) continue;
                    switch (power)
                    {
                        case VulnerablePower:
                            score += Math.Min(power.Amount, _turns[j]) * VulnerableTurn * _worth[j];
                            break;
                        case WeakPower:
                            score += Math.Min(power.Amount, _turns[j]) * WeakTurn * _threat[j];
                            break;
                        case StrengthPower:
                            break;
                        default:
                            // A debuff it got this turn that has no rule of its own.
                            if (power.TypeForCurrentAmount == PowerType.Debuff
                                && Math.Abs(power.Amount) > Math.Abs(_enemyPowers.GetValueOrDefault((j, power.ClassName))))
                                score += OtherDebuff;
                            break;
                    }
                }
            }

            foreach (var power in f.Player.Powers)
            {
                if (!power.IsVisible) continue;
                var before = _powers.GetValueOrDefault(power.ClassName);
                var lasting = Math.Min(power.Amount, _fightLeft);
                switch (power)
                {
                    case StrengthPower:
                        score += power.Amount * HitsPerTurn * _fightLeft * _bestWorth * Later;
                        break;
                    case DexterityPower:
                        score += power.Amount * BlocksPerTurn * _fightLeft * Later;
                        break;
                    case PlatingPower:
                        // Block every turn, one less each time.
                        score += lasting * (2 * power.Amount - lasting + 1) / 2 * Later;
                        break;
                    case VulnerablePower:
                        score -= lasting * 0.5 * danger * Later;
                        break;
                    case WeakPower:
                        score -= lasting * 0.25 * DamagePerTurn * _bestWorth * Later;
                        break;
                    case FrailPower:
                        score -= lasting * 0.25 * BlocksPerTurn * 5 * Later;
                        break;
                    default:
                        // New or grown since the decision: a power card was played, or a debuff was taken.
                        if (power.Amount == before || (before != 0 && Math.Abs(power.Amount) < Math.Abs(before))) break;
                        score += power.TypeForCurrentAmount == PowerType.Debuff ? -OtherDebuff : OtherBuffPerTurn * _fightLeft;
                        break;
                }
            }

            // What the turn used up for good, and what the next one starts with.
            var junk = f.ExhaustPile.Count(IsJunk) - _junk;
            score += junk * JunkGone + (f.ExhaustPile.Count - _exhausted - junk) * CardGone;
            score += Math.Min(f.Player.Block, MostBlockKept) * BlockKept;
            score += Math.Max(0, f.Energy - _maxEnergy) * EnergyAhead;
            return score;
        }

        /// <summary>The damage it still takes to kill an enemy with <paramref name="hp"/> HP.</summary>
        private static double StillToDo(int hp) => hp <= 0 ? 0 : Math.Max(hp, LastHit);

        private static double HpValue(int hp) =>
            hp + LowHpExtra * Math.Min(hp, LowHp) + VeryLowHpExtra * Math.Min(hp, VeryLowHp);
    }
}
