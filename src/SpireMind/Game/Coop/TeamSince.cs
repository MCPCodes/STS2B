using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;

namespace SpireMind.Game.Coop;

/// <summary>
/// "team_since" and "team_changed" of a co-op fight snapshot: what the other players did since
/// the last action the model chose for this copy's player, one short line each, newest last.
///
/// The source is the fight's own record, CombatManager.Instance.History. Every copy runs every
/// player's actions, so a client's record holds the others' card plays, potions, damage, Block
/// and powers. Only reads, main thread only; a throw is logged once and the two fields are left
/// out for the rest of that fight.
///
/// What the record gives, and how each entry is tied to a player:
/// <list type="bullet">
/// <item>A card play is two entries, started and finished, with the card, its owner and its
/// target. Damage names the card that dealt it, Block the card play that gave it, a power the
/// creature that applied it. Those go on the card's line.</item>
/// <item>A potion is one entry written after its effects. Its line takes what the record shows
/// its user dealing or applying since the entry before (a card started or finished, or another
/// potion), to its target when it has one, and Block that landed on its target there.</item>
/// <item>Anything else with a teammate on record as dealer or applier is that teammate's
/// "also" line. An effect with nobody on record is listed with no cause, and only where this
/// copy's own card or potion cannot be behind it.</item>
/// <item>What a turn's setup does (start-of-turn relics, powers, autoplays) is not told: it
/// comes before the turn's first card or potion and before the first capture with nothing
/// running.</item>
/// </list>
/// The record has no entry for an end of turn, a heal, Block taken away or energy given. Turn
/// ends come from the ready flags. The others come from comparing this player's HP, Block and
/// energy and each enemy's HP and Block between two quiet captures with no action of our own
/// between them, less what the record explains; they are worded without a cause.
///
/// The reader cannot tell a capture the model gets from one of the many it never sees (the
/// wait loop polls, every action is checked against a fresh capture, the brain peeks). So
/// lines stay until this copy's player acts (<see cref="Acted"/>), and until the turn ends.
/// An action the model chose runs on a snapshot the tools matched against the model's own
/// (BotTools refuses on another version), so what that snapshot showed has been read. A
/// later step of an act_sequence runs on a snapshot the model never saw: it is told apart by
/// how soon it follows that snapshot (<see cref="ModelAnswerMs"/>) and clears nothing, so
/// the lines are still there in the snapshot the sequence returns.
/// </summary>
internal static class TeamSince
{
    /// <summary>Most lines one snapshot carries.</summary>
    public const int MaxLines = 8;

    /// <summary>Most creatures one line names before it says "and N more".</summary>
    private const int MaxGroups = 4;

    /// <summary>
    /// An own action this soon after the snapshot it acts on first read as it does is a later
    /// step of an act_sequence, not an answer of the model. The tools run a sequence's next
    /// step as soon as the snapshot has read the same for 300 ms, polled every 150 ms
    /// (DecisionWaiter.SettleTime and PollInterval; BotTools.StepAsync), so a step comes 300 to
    /// about 500 ms after it. The model's answer to a snapshot takes seconds. Too low, and a
    /// step delayed by a slow frame clears lines nobody read; too high, and a model that
    /// answers faster than this sees its lines once more after its action.
    /// </summary>
    private const long ModelAnswerMs = 700;

    private enum Kind { Damage, Block, Power, Hp, Energy, Card, Drew }

    /// <summary>One thing that happened to one creature.</summary>
    private sealed class Effect(Kind kind, Creature on)
    {
        public Kind Kind { get; } = kind;
        public Creature On { get; } = on;
        public int Amount;
        public int Blocked;
        public bool Killed;
        public string? Name;

        /// <summary>The teammate on record as dealer, applier or creator, while it waits in <see cref="Run"/>.</summary>
        public Player? By;

        /// <summary>The capture that first showed it; 0 before that.</summary>
        public int Gen;
    }

    private sealed class Line(string head)
    {
        public string Head { get; } = head;

        /// <summary>" on Nibbit (e0)", or empty.</summary>
        public string On { get; init; } = "";

        /// <summary>The head once the first part has been handed over while the card was still running.</summary>
        public string? Later { get; init; }

        public Player? Actor { get; init; }
        public Creature? Target { get; init; }
        public CardModel? Card { get; init; }
        public List<Effect> Effects { get; } = new();

        /// <summary>A card play that has not finished: more effects can follow.</summary>
        public bool Open;
        public bool Continued;
        public bool Logged;
        public int Times = 1;
        public int Gen;
    }

    /// <summary>A card play that has started and not finished. <see cref="Line"/> is null for our own and for start-of-turn autoplays.</summary>
    private sealed record Play(CardPlay CardPlay, Player Owner, Line? Line, bool Quiet);

    private struct Sum
    {
        public int HpLost;
        public int Gained;
        public int Blocked;
    }

    // ---------- state, all of it per fight ----------

    private static ICombatState? _fight;
    private static ICombatState? _broken;

    /// <summary>The cursor: entries of the record already read.</summary>
    private static int _scanned;
    private static (int Round, int Turn) _turn;

    private static readonly List<Line> Lines = new();
    private static readonly List<Play> Open = new();

    /// <summary>Effects since the last card or potion entry that no card play claimed yet.</summary>
    private static readonly List<Effect> Run = new();

    /// <summary>Stands in <see cref="_left"/> until the turn's first card or potion entry: what came before is turn setup.</summary>
    private static readonly object Setup = new();

    /// <summary>Whose card or potion entry came last: a Player, <see cref="Setup"/>, or null once setup is known to be over.</summary>
    private static object? _left = Setup;

    private static readonly Dictionary<ulong, bool> Ended = new();

    // What the record cannot explain: the last quiet capture's numbers, and the record's sums since.
    private static Dictionary<Creature, (int Hp, int Block)>? _base;
    private static int _baseEnergy;
    private static readonly Dictionary<Creature, Sum> Sums = new();
    private static bool _own;

    // Handing over.
    private static int _gen;
    private static GameContext? _genCtx;
    private static string? _board;
    private static int _ackGen;
    private static string? _ackBoard;
    private static bool _dirty;

    // Since when the snapshot has looked as it does, while it carries lines (Environment.TickCount64).
    // The look before is kept too, so one capture of a passing state does not restart the clock.
    private static string? _look;
    private static long _lookSince;
    private static string? _lookEarlier;
    private static long _lookEarlierSince;

    /// <summary><see cref="_lookSince"/> as the capture before the last one left it; 0 when that one carried no lines.</summary>
    private static long _lookSinceBefore;

    private sealed record Scene(ICombatState Cs, Player Me, IReadOnlyList<Creature> Enemies, IReadOnlyList<Teammates.Mate> Mates)
    {
        public bool IsMe(Player? p) => p is not null && p.NetId == Me.NetId;

        public string Name(Player p)
        {
            foreach (var mate in Mates)
                if (mate.NetId == p.NetId && mate.Name.Length > 0) return mate.Name;
            return $"player {p.NetId}";
        }

        /// <summary>This copy's player, their pet, or an enemy: what "team_changed" is about.</summary>
        public bool Touches(Creature c) =>
            ReferenceEquals(c, Me.Creature) || IsMe(c.PetOwner) || c.Side == CombatSide.Enemy;

        /// <summary>"you", "Nibbit (e0)", "Sam (p2)", the acting player's bare name, "Mark's Osty".</summary>
        public string Label(Creature c, Player? actor)
        {
            if (ReferenceEquals(c, Me.Creature)) return "you";
            if (c.Player is { } p)
            {
                if (actor is not null && actor.NetId == p.NetId) return Name(p);
                var seat = IndexOf(Cs.Players, p);
                return seat >= 0 ? $"{Name(p)} (p{seat})" : Name(p);
            }
            var name = Describe.SafeText(() => c.Name);
            if (c.PetOwner is { } owner) return IsMe(owner) ? $"your {name}" : $"{Name(owner)}'s {name}";
            var j = IndexOf(Enemies, c);
            return j >= 0 ? $"{name} (e{j})" : name;
        }

        private static int IndexOf<T>(IReadOnlyList<T> list, T item) where T : class
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], item)) return i;
            return -1;
        }
    }

    // ---------- the two calls from CombatReader ----------

    /// <summary>
    /// Reads the record up to now and adds "team_since" (absent when there is nothing to tell)
    /// and "team_changed": true (absent otherwise) to a fight snapshot's state. Call it last,
    /// once the rest of the state is in: the rest is what tells an action that ran from one
    /// the game ignored.
    /// </summary>
    public static void Add(JsonObject state, GameContext ctx, ICombatState cs, Player me, IReadOnlyList<Creature> enemies)
    {
        if (ReferenceEquals(cs, _broken)) return;
        try
        {
            if (CombatManager.Instance is not { } cm) return;
            var entries = cm.History.Entries as IReadOnlyList<CombatHistoryEntry> ?? cm.History.Entries.ToList();
            if (!ReferenceEquals(cs, _fight) || entries.Count < _scanned)
            {
                Reset();
                _fight = cs;
                ModEntry.Log($"coop: team_since: following this fight's record ({entries.Count} entries so far)");
            }

            var turn = (cs.RoundNumber, me.PlayerCombatState?.TurnNumber ?? 0);
            if (turn != _turn)
            {
                NewTurn();
                _turn = turn;
            }

            _gen++;
            _genCtx = ctx;
            var s = new Scene(cs, me, enemies, Teammates.All(ctx));

            // A play whose owner died mid-card never gets its finished entry.
            for (var i = Open.Count - 1; i >= 0; i--)
                if (Open[i].Owner.Creature.IsDead) Close(i);

            for (; _scanned < entries.Count; _scanned++)
                if (entries[_scanned].HappenedThisTurn(cs)) Read(entries[_scanned], s);

            var run = RunManager.Instance;
            var quiet = run?.ActionExecutor is { IsRunning: false };
            var playPhase = run?.ActionQueueSynchronizer?.CombatState == ActionSynchronizerCombatState.PlayPhase;
            if (!playPhase)
            {
                // Only a choice from the hand gets here outside the play phase: the turn is being
                // set up or wound up. What lands then (start-of-turn and end-of-turn relics and
                // powers, ours among them) is the turn's doing, not a teammate's action.
                Run.Clear();
            }
            else if (quiet && ReferenceEquals(_left, Setup))
            {
                // No card or potion yet this turn and nothing running: all so far was turn setup.
                Run.Clear();
                _left = null;
            }
            else if (quiet)
            {
                // Nothing is running, so no potion entry is still to come for these. And with no
                // card open either, whatever acted last is done to its last trigger: what comes
                // next is not its doing.
                EndRun(s, null, trailing: true);
                if (Open.Count == 0) _left = null;
            }
            else if (s.IsMe(_left as Player) && Open.Count == 0 && !CombatReader.HandSelectOpen()
                     && run?.ActionExecutor?.CurrentlyRunningAction is { } running && running.OwnerId != me.NetId)
            {
                // A teammate's action is running and actions run one at a time, so ours is done
                // to its last trigger. What waits here could still be ours; what comes next is not.
                EndRun(s, me);
                _left = null;
            }

            TurnEnds(s, cm);
            if (quiet && playPhase) Unrecorded(s);

            _board = Lines.Count > 0 || _ackGen > 0 ? state.ToJsonString() : null;
            if (_ackGen > 0 && (_dirty || !string.Equals(_board, _ackBoard, StringComparison.Ordinal)))
            {
                HandOver(_ackGen);
                _ackGen = 0;
                _ackBoard = null;
            }
            _dirty = false;

            Lines.RemoveAll(l => l.Continued && !l.Open && l.Effects.Count == 0);
            var texts = new List<string>();
            var changed = false;
            foreach (var line in Lines)
            {
                if (line.Continued && line.Effects.Count == 0) continue;
                if (line.Gen == 0) line.Gen = _gen;
                foreach (var effect in line.Effects)
                    if (effect.Gen == 0) effect.Gen = _gen;

                var text = Text(line, s);
                texts.Add(text);
                changed |= (!line.Continued && line.Target is { } target && s.Touches(target)) || line.Effects.Any(e => s.Touches(e.On));
                if (!line.Logged && !line.Open)
                {
                    line.Logged = true;
                    ModEntry.Log($"coop: team_since: {text}");
                }
            }
            Look(texts.Count == 0 || _board is null ? null : $"{_board}\n{string.Join("\n", texts)}");
            if (texts.Count == 0) return;

            if (texts.Count > MaxLines)
            {
                var hidden = texts.Count - (MaxLines - 1);
                texts = texts.Skip(hidden).Prepend($"({hidden} earlier lines left out)").ToList();
            }
            state["team_since"] = new JsonArray(texts.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray());
            if (changed) state["team_changed"] = true;
        }
        catch (Exception ex)
        {
            _broken = cs;
            Reset();
            ModEntry.LogError($"coop: team_since failed, so it is left out for the rest of this fight: {ex}");
        }
    }

    /// <summary>
    /// This copy's player acted. When the model chose the action, what the snapshots showed
    /// before this call's own capture has been handed over: those lines go once the board has
    /// changed too, so an action the game ignored still leaves the snapshot as it was. A later
    /// step of an act_sequence (<see cref="ModelAnswerMs"/>) hands nothing over. Returns
    /// <paramref name="result"/>, whatever happens here.
    /// </summary>
    public static ActionResult Acted(GameContext ctx, ActionResult result)
    {
        try
        {
            // Only in the fight the lines are of: never in a solo fight, nor after a failed read.
            if (!result.Ok || _fight is null || !ReferenceEquals(ctx.Combat, _fight)) return result;
            _own = true;
            if (Lines.Count == 0) return result;

            // GameBridge captures and executes with one context: what only that capture showed is not handed over yet.
            var check = ReferenceEquals(ctx, _genCtx);
            var gen = check ? _gen - 1 : _gen;
            var since = check ? _lookSinceBefore : _lookSince;
            var age = System.Environment.TickCount64 - since;
            if (since != 0 && age < ModelAnswerMs)
            {
                var kept = Lines.Count(l => l.Gen != 0 && l.Gen <= gen);
                if (kept > 0)
                    ModEntry.Log($"coop: team_since: {kept} line(s) kept: this action came {age} ms after its snapshot, so it is a later step of a sequence");
                return result;
            }

            _ackGen = gen;
            _ackBoard = _board;
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"coop: team_since: the hand-over failed: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Keeps the clock of <see cref="ModelAnswerMs"/>: since when the board and the lines have
    /// read as they do now. <paramref name="look"/> is null when the snapshot carries no lines.
    /// </summary>
    private static void Look(string? look)
    {
        _lookSinceBefore = _look is null ? 0 : _lookSince;
        if (string.Equals(look, _look, StringComparison.Ordinal)) return;

        if (look is not null && string.Equals(look, _lookEarlier, StringComparison.Ordinal))
        {
            // Back to the look before a passing one: it keeps its own time.
            (_look, _lookSince, _lookEarlier, _lookEarlierSince) = (_lookEarlier, _lookEarlierSince, _look, _lookSince);
            return;
        }

        (_lookEarlier, _lookEarlierSince) = (_look, _lookSince);
        (_look, _lookSince) = (look, System.Environment.TickCount64);
    }

    // ---------- reading the record ----------

    private static void Read(CombatHistoryEntry entry, Scene s)
    {
        switch (entry)
        {
            case CardPlayStartedEntry started:
            {
                // The entry's own actor: a card can lose its owner once it has left the fight.
                var play = started.CardPlay;
                if (started.Actor?.Player is not { } owner) break;
                if (play.IsAutoPlay && ReferenceEquals(_left, Setup))
                {
                    // Played by a start-of-turn effect: part of turn setup, not an action.
                    Open.Add(new Play(play, owner, null, Quiet: true));
                    break;
                }

                EndRun(s, owner);
                _left = owner;
                Line? line = null;
                if (s.IsMe(owner))
                {
                    _own = true;
                }
                else if (play.PlayIndex > 0 && Lines.LastOrDefault(l => ReferenceEquals(l.Card, play.Card)) is { } first)
                {
                    // A replay of the same card: one line, counted.
                    line = first;
                    line.Times++;
                    line.Open = true;
                    _dirty = true;
                }
                else
                {
                    var name = s.Name(owner);
                    var card = CardTitle(play.Card);
                    var target = ReferenceEquals(play.Target, owner.Creature) ? null : play.Target;
                    line = Keep(new Line($"{name} played {card}{(play.IsAutoPlay ? " (auto)" : "")}")
                    {
                        On = target is null ? "" : $" on {s.Label(target, owner)}",
                        Later = $"{name}'s {card} went on",
                        Actor = owner,
                        Target = target,
                        Card = play.Card,
                        Open = true,
                    });
                }
                Open.Add(new Play(play, owner, line, Quiet: false));
                break;
            }

            case CardPlayFinishedEntry finished:
            {
                var at = Open.FindLastIndex(p => ReferenceEquals(p.CardPlay, finished.CardPlay));
                if (at >= 0 && Open[at].Quiet)
                {
                    Close(at);
                    break;
                }

                if (finished.Actor?.Player is not { } owner) break;
                EndRun(s, owner);
                _left = owner;
                if (s.IsMe(owner)) _own = true;
                if (at >= 0) Close(at);
                break;
            }

            case PotionUsedEntry used:
            {
                if (used.Actor?.Player is not { } owner) break;
                if (s.IsMe(owner))
                {
                    _own = true;
                    EndRun(s, owner);
                    _left = owner;
                    break;
                }

                var target = ReferenceEquals(used.Target, owner.Creature) ? null : used.Target;
                var potion = Describe.SafeText(() => used.Potion.Title);
                var line = new Line($"{s.Name(owner)} used {(potion.Length > 0 ? potion : used.Potion.Id.Entry)}")
                {
                    On = target is null ? "" : $" on {s.Label(target, owner)}",
                    Actor = owner,
                    Target = target,
                };
                if (!ReferenceEquals(_left, Setup))
                {
                    // Block lands with nobody on record. Ours could be our own card's doing when that came just before.
                    var mine = s.IsMe(_left as Player) || Open.Any(p => s.IsMe(p.Owner));
                    for (var i = 0; i < Run.Count; i++)
                    {
                        var effect = Run[i];
                        // An aimed potion acts on its target (every stock potion aimed at one creature does only
                        // that), so what its user did to anyone else here stays an "also": a relic or power of theirs.
                        var theirs = effect.By is { } by && by.NetId == owner.NetId
                                     && (used.Target is not { } aimed || ReferenceEquals(effect.On, aimed));
                        var block = effect.By is null && effect.Kind == Kind.Block
                            && (used.Target is { } aim ? ReferenceEquals(effect.On, aim) : effect.On.Side == CombatSide.Player)
                            && !(mine && ReferenceEquals(effect.On, s.Me.Creature));
                        if (!theirs && !block) continue;
                        line.Effects.Add(effect);
                        Run.RemoveAt(i--);
                    }
                }
                EndRun(s, owner);
                _left = owner;
                Keep(line);
                break;
            }

            case DamageReceivedEntry damage:
            {
                var result = damage.Result;
                if (damage.Receiver is not { } receiver || result is null) break;
                Count(receiver, hpLost: result.UnblockedDamage);
                // A pet's hits are blocked with its owner's Block.
                Count(receiver.PetOwner?.Creature ?? receiver, blocked: result.BlockedDamage);

                var total = result.BlockedDamage + result.UnblockedDamage;
                if (total == 0 && !result.WasTargetKilled) break;
                var effect = new Effect(Kind.Damage, receiver) { Amount = total, Blocked = result.BlockedDamage, Killed = result.WasTargetKilled };
                var play = damage.CardSource is { } card ? Open.FindLast(p => ReferenceEquals(p.CardPlay.Card, card)) : null;
                Place(s, effect, play, PlayerOf(damage.Dealer));
                break;
            }

            case BlockGainedEntry gained:
            {
                if (gained.Receiver is not { } receiver) break;
                Count(receiver, gained: gained.Amount);
                var effect = new Effect(Kind.Block, receiver) { Amount = gained.Amount };
                if (gained.CardPlay is { } cardPlay)
                    Place(s, effect, Open.FindLast(p => ReferenceEquals(p.CardPlay, cardPlay)), OwnerOf(cardPlay.Card));
                else
                    Place(s, effect, null, null);
                break;
            }

            case PowerReceivedEntry power:
            {
                var amount = (int)power.Amount;
                if (amount == 0 || power.Actor is null || power.Power is not { IsVisible: true }) break;
                var title = Describe.SafeText(() => power.Power.Title);
                var effect = new Effect(Kind.Power, power.Actor) { Amount = amount, Name = title.Length > 0 ? title : power.Power.Id.Entry };
                Place(s, effect, null, PlayerOf(power.Applier));
                break;
            }

            case CardGeneratedEntry made when ReferenceEquals(made.Actor, s.Me.Creature) && made.Creator is { } creator && !s.IsMe(creator):
                // A card a teammate put into one of our piles.
                Place(s, new Effect(Kind.Card, s.Me.Creature) { Name = CardTitle(made.Card) }, null, creator);
                break;

            case CardDrawnEntry { FromHandDraw: false } drawn when ReferenceEquals(drawn.Actor, s.Me.Creature):
                Place(s, new Effect(Kind.Drew, s.Me.Creature) { Amount = 1 }, null, null);
                break;

            case EnergySpentEntry spent when ReferenceEquals(spent.Actor, s.Me.Creature):
                _own = true;
                break;
        }
    }

    private static Player? PlayerOf(Creature? c) => c?.Player ?? c?.PetOwner;

    /// <summary>A card's shown name, or its id when the name cannot be read: one odd card must not end the whole read.</summary>
    private static string CardTitle(CardModel card)
    {
        var title = Describe.SafeText(() => card.Title);
        return title.Length > 0 ? title : Describe.SafeText(() => card.Id.Entry);
    }

    private static Player? OwnerOf(CardModel card)
    {
        try { return card.Owner; }
        catch { return null; }
    }

    /// <summary>
    /// Puts an effect where the record ties it: the card play that is named for it, else the
    /// running card of the teammate on record as dealer or applier, else the waiting list.
    /// Our own are dropped: they are not news.
    /// </summary>
    private static void Place(Scene s, Effect effect, Play? linked, Player? by)
    {
        if (linked is null && by is not null)
        {
            if (s.IsMe(by)) return;
            linked = Open.FindLast(p => p.Owner.NetId == by.NetId);
        }

        if (linked is not null)
        {
            if (linked.Line is not { } line) return;
            line.Effects.Add(effect);
            _dirty = true;
            return;
        }

        effect.By = by;
        Run.Add(effect);
    }

    /// <summary>
    /// The waiting effects end at a card or potion entry of <paramref name="right"/> (null: at
    /// the end of the record with nothing running). Those with a teammate on record that touch
    /// us or an enemy become that teammate's "also" line. The rest go on a teammate's running
    /// card when nothing but that card lies around them, and are otherwise listed with no
    /// cause, unless a card or potion of ours lies next to them. Before the turn's first card
    /// or potion they are turn setup and dropped.
    /// </summary>
    private static void EndRun(Scene s, Player? right, bool trailing = false)
    {
        if (Run.Count == 0) return;
        if (ReferenceEquals(_left, Setup))
        {
            Run.Clear();
            return;
        }

        var left = _left as Player;
        var rest = new List<Effect>();
        var also = new Dictionary<ulong, Line>();
        foreach (var effect in Run)
        {
            if (effect.By is not { } by)
            {
                rest.Add(effect);
                continue;
            }
            // A trigger of theirs that only touches their own side is not worth a line.
            if (!s.Touches(effect.On)) continue;
            if (!also.TryGetValue(by.NetId, out var line))
                also[by.NetId] = line = Keep(new Line($"{s.Name(by)} also") { Actor = by });
            line.Effects.Add(effect);
        }
        Run.Clear();
        if (rest.Count == 0) return;

        var within = left is not null && !s.IsMe(left) && (trailing || right?.NetId == left.NetId)
                     && Open.Count > 0 && Open.All(p => p.Owner.NetId == left.NetId);
        if (within && Open[^1].Line is { } running)
        {
            running.Effects.AddRange(rest);
            _dirty = true;
            return;
        }

        if (within || s.IsMe(left) || s.IsMe(right) || Open.Any(p => s.IsMe(p.Owner))) return;
        rest.RemoveAll(e => !s.Touches(e.On));
        if (rest.Count > 0) Keep(new Line("No recorded cause")).Effects.AddRange(rest);
    }

    private static void Close(int at)
    {
        var line = Open[at].Line;
        Open.RemoveAt(at);
        if (line is not null) line.Open = Open.Any(p => ReferenceEquals(p.Line, line));
    }

    private static Line Keep(Line line)
    {
        Lines.Add(line);
        _dirty = true;
        return line;
    }

    private static void Count(Creature c, int hpLost = 0, int gained = 0, int blocked = 0)
    {
        Sums.TryGetValue(c, out var sum);
        sum.HpLost += hpLost;
        sum.Gained += gained;
        sum.Blocked += blocked;
        Sums[c] = sum;
    }

    // ---------- what the record does not hold ----------

    /// <summary>
    /// "Mark ended their turn", from the ready flags, which the game clears at each turn start.
    /// The game itself sets dead players and those sitting out an extra turn ready: not told.
    /// </summary>
    private static void TurnEnds(Scene s, CombatManager cm)
    {
        foreach (var mate in s.Mates)
        {
            if (mate.IsMe || !mate.Player.Creature.IsAlive || !cm.IsPartOfPlayerTurn(mate.Player)) continue;
            var ended = cm.IsPlayerReadyToEndTurn(mate.Player);
            if (ended == Ended.GetValueOrDefault(mate.NetId)) continue;
            Ended[mate.NetId] = ended;
            Keep(new Line(ended ? $"{mate.Name} ended their turn" : $"{mate.Name} took back their end of turn") { Actor = mate.Player });
        }
    }

    /// <summary>
    /// Heals, Block taken away and energy given leave no entry. With nothing running, compare
    /// our HP, Block and energy and each enemy's HP and Block with the last such capture, less
    /// the damage and Block the record holds for the time between. Skipped when a card or
    /// potion of our own lies between: the difference could be its doing.
    /// </summary>
    private static void Unrecorded(Scene s)
    {
        var now = new Dictionary<Creature, (int Hp, int Block)>();
        if (s.Me.Creature.IsAlive) now[s.Me.Creature] = (s.Me.Creature.CurrentHp, s.Me.Creature.Block);
        foreach (var enemy in s.Enemies)
            if (CombatReader.InFight(s.Cs, enemy)) now[enemy] = (enemy.CurrentHp, enemy.Block);
        var energy = s.Me.PlayerCombatState?.Energy ?? 0;

        if (_base is not null && !_own && !Open.Any(p => s.IsMe(p.Owner)))
        {
            var effects = new List<Effect>();
            foreach (var (creature, (hp, block)) in now)
            {
                if (!_base.TryGetValue(creature, out var was)) continue;
                Sums.TryGetValue(creature, out var sum);
                var hpOff = hp - was.Hp + sum.HpLost;
                var blockOff = block - was.Block - sum.Gained + sum.Blocked;
                if (hpOff != 0) effects.Add(new Effect(Kind.Hp, creature) { Amount = hpOff });
                if (blockOff != 0) effects.Add(new Effect(Kind.Block, creature) { Amount = blockOff });
            }
            if (energy != _baseEnergy && now.ContainsKey(s.Me.Creature))
                effects.Add(new Effect(Kind.Energy, s.Me.Creature) { Amount = energy - _baseEnergy });
            if (effects.Count > 0) Keep(new Line("Not in the fight record")).Effects.AddRange(effects);
        }

        _base = now;
        _baseEnergy = energy;
        Sums.Clear();
        _own = false;
    }

    // ---------- handing over, and starting again ----------

    /// <summary>Drops what was shown up to capture <paramref name="gen"/>. A card still running keeps a line for what follows.</summary>
    private static void HandOver(int gen)
    {
        var gone = 0;
        for (var i = Lines.Count - 1; i >= 0; i--)
        {
            var line = Lines[i];
            if (line.Gen == 0 || line.Gen > gen) continue;
            line.Effects.RemoveAll(e => e.Gen != 0 && e.Gen <= gen);
            if (!line.Continued) gone++;
            if (line.Open || line.Effects.Count > 0) line.Continued = true;
            else Lines.RemoveAt(i);
        }
        if (gone > 0) ModEntry.Log($"coop: team_since: {gone} line(s) handed over with this copy's own action");
    }

    private static void NewTurn()
    {
        Lines.Clear();
        Open.Clear();
        Run.Clear();
        Ended.Clear();
        Sums.Clear();
        _left = Setup;
        _base = null;
        _own = false;
        _ackGen = 0;
        _ackBoard = null;
        _board = null;
        _dirty = false;
        _look = null;
        _lookEarlier = null;
        _lookSince = 0;
        _lookEarlierSince = 0;
        _lookSinceBefore = 0;
    }

    private static void Reset()
    {
        NewTurn();
        _fight = null;
        _scanned = 0;
        _turn = default;
        _genCtx = null;
    }

    // ---------- wording ----------

    private static string Text(Line line, Scene s)
    {
        var head = line.Continued && line.Later is { } later
            ? later
            : $"{line.Head}{(line.Times > 1 ? $" {line.Times} times" : "")}{line.On}";

        // The card's or potion's own target first, and unnamed: the head names it.
        var order = new List<Creature>();
        foreach (var effect in line.Effects)
            if (!order.Any(c => ReferenceEquals(c, effect.On))) order.Add(effect.On);
        var target = line.Continued ? null : line.Target;
        if (target is not null && order.RemoveAll(c => ReferenceEquals(c, target)) > 0) order.Insert(0, target);

        var parts = new List<string>();
        foreach (var on in order)
        {
            var text = Fragments(line.Effects.Where(e => ReferenceEquals(e.On, on)).ToList());
            if (text.Length == 0) continue;
            parts.Add(ReferenceEquals(on, target) ? text : $"{s.Label(on, line.Actor)} {text}");
        }
        if (parts.Count > MaxGroups)
        {
            var more = parts.Count - MaxGroups;
            parts = parts.Take(MaxGroups).Append($"and {more} more").ToList();
        }
        return parts.Count == 0 ? head : $"{head}: {string.Join("; ", parts)}";
    }

    /// <summary>"8 damage (5 blocked, killed), +8 Block, 2 Vulnerable": one creature's effects, summed.</summary>
    private static string Fragments(List<Effect> effects)
    {
        var parts = new List<string>();

        var hits = effects.Where(e => e.Kind == Kind.Damage).ToList();
        if (hits.Count > 0)
        {
            var notes = new List<string>();
            if (hits.Sum(e => e.Blocked) is > 0 and var blocked) notes.Add($"{blocked} blocked");
            if (hits.Any(e => e.Killed)) notes.Add("killed");
            parts.Add($"{hits.Sum(e => e.Amount)} damage{(notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "")}");
        }

        Signed(Kind.Block, "Block");
        foreach (var power in effects.Where(e => e.Kind == Kind.Power).GroupBy(e => e.Name))
            if (power.Sum(e => e.Amount) is not 0 and var amount) parts.Add($"{amount} {power.Key}");
        Signed(Kind.Hp, "HP");
        Signed(Kind.Energy, "energy");
        foreach (var card in effects.Where(e => e.Kind == Kind.Card).GroupBy(e => e.Name))
            parts.Add(card.Count() > 1 ? $"got {card.Key} x{card.Count()}" : $"got {card.Key}");
        if (effects.Count(e => e.Kind == Kind.Drew) is > 0 and var drew)
            parts.Add(drew == 1 ? "drew 1 card" : $"drew {drew} cards");
        return string.Join(", ", parts);

        void Signed(Kind kind, string what)
        {
            var of = effects.Where(e => e.Kind == kind).ToList();
            if (of.Count > 0 && of.Sum(e => e.Amount) is not 0 and var sum) parts.Add($"{sum:+0;-0} {what}");
        }
    }
}
