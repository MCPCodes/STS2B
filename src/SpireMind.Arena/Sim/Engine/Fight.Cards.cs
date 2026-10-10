using System.Text;

namespace SpireMind.Arena;

// Piles, drawing, shuffling, costs, and playing cards and potions (rules-turns.md sections 3 and 4).
public sealed partial class Fight
{
    // ---------- creating cards ----------

    private CardInstance NewCard(CardDef def, bool upgraded)
    {
        var card = new CardInstance(_s.NextCardId++, def);
        if (upgraded && card.IsUpgradable) card.ApplyUpgrade();
        return card;
    }

    /// <summary>A new card in no pile yet. Put it somewhere with <see cref="AddGeneratedCard"/>.</summary>
    public CardInstance CreateCard(string className, bool upgraded = false) => NewCard(Content.Registry.Card(className), upgraded);

    public CardInstance CreateCard(CardDef def, bool upgraded = false) => NewCard(def, upgraded);

    /// <summary>A new card that copies <paramref name="original"/> as it is now (upgrade, numbers, cost changes), in no pile yet.</summary>
    public CardInstance CloneCard(CardInstance original) => original.Duplicate(_s.NextCardId++);

    private List<CardInstance> PileList(PileType pile) => pile switch
    {
        PileType.Hand => _s.Hand,
        PileType.Draw => _s.Draw,
        PileType.Discard => _s.Discard,
        PileType.Exhaust => _s.Exhaust,
        PileType.Play => _s.Play,
        _ => throw new ArgumentOutOfRangeException(nameof(pile), pile, "Not a combat pile."),
    };

    // ---------- moving cards (R19, R21) ----------

    /// <summary>
    /// A plain move of a card into a pile: no discard or exhaust hooks, only "card changed
    /// piles". Bottom appends, Top inserts at index 0, Random picks an index from the Shuffle
    /// stream. A card headed for a full hand goes to the discard pile instead. Nothing moves
    /// while the fight is ending, before it is in progress, or when the player is dead.
    /// Returns whether the card moved.
    /// </summary>
    public bool MoveCard(CardInstance card, PileType to, PilePosition position = PilePosition.Bottom) =>
        MoveCards([card], to, position) == 1;

    /// <summary>Moves a batch; the "changed piles" hooks run after the whole batch has moved. Returns how many moved.</summary>
    public int MoveCards(IReadOnlyList<CardInstance> cards, PileType to, PilePosition position = PilePosition.Bottom)
    {
        if (cards.Count == 0 || IsEnding || _s.Player.IsDead || !_s.InProgress) return 0;

        var moved = new List<(CardInstance Card, PileType Old)>(cards.Count);
        foreach (var card in cards.ToList())
        {
            var old = card.Pile;
            var target = to == PileType.Hand && _s.Hand.Count >= HandLimit ? PileType.Discard : to;
            if (old != PileType.None) PileList(old).Remove(card);

            var list = PileList(target);
            var index = position switch
            {
                PilePosition.Top => 0,
                PilePosition.Random => _s.Rng.Shuffle.NextInt(list.Count + 1),
                _ => list.Count,
            };
            list.Insert(index, card);
            card.Pile = target;
            card.KnownPosition = target == PileType.Draw && position == PilePosition.Top;

            if (old == PileType.None) Each(l => l.AfterCardEnteredCombat(this, card));
            moved.Add((card, old));
        }

        foreach (var (card, old) in moved)
        {
            Each(l => l.AfterCardChangedPiles(this, card, old), guarded: false);
            Each(l => l.AfterCardChangedPilesLate(this, card, old), guarded: false);
        }
        return moved.Count;
    }

    /// <summary>Takes a card out of the fight altogether (a played Power card): it is in no pile and hears no
    /// hooks. Everyone else is told it changed piles, as for any other move.</summary>
    public void RemoveFromCombat(CardInstance card)
    {
        if (card.Pile == PileType.None) return;
        var old = card.Pile;
        PileList(old).Remove(card);
        card.Pile = PileType.None;
        Each(l => l.AfterCardChangedPiles(this, card, old), guarded: false);
        Each(l => l.AfterCardChangedPilesLate(this, card, old), guarded: false);
    }

    /// <summary>Exhausts a card: to the exhaust pile, then the exhaust hooks. Nothing while the fight is over or ending.</summary>
    public void Exhaust(CardInstance card, bool causedByEthereal = false)
    {
        if (IsOverOrEnding) return;
        MoveCard(card, PileType.Exhaust);
        _s.History.CardExhausted(this, card, causedByEthereal);
        Each(l => l.AfterCardExhausted(this, card, causedByEthereal));
    }

    /// <summary>A real discard by an effect: to the discard pile, then the discard hooks. (The
    /// end-of-turn flush is a plain move, not this.)</summary>
    public void Discard(CardInstance card)
    {
        if (IsOverOrEnding) return;
        MoveCard(card, PileType.Discard);
        _s.History.CardDiscarded(this, card);
        Each(l => l.AfterCardDiscarded(this, card));
    }

    /// <summary>
    /// Discards the cards one after the other, each with its discard hooks, then draws
    /// <paramref name="drawCount"/> cards (the game's DiscardAndDraw; Gambler's Brew). Whether the
    /// fight is ending is asked once, before the first card; with no card to discard nothing is drawn either.
    /// </summary>
    public void DiscardAndDraw(IReadOnlyList<CardInstance> cards, int drawCount)
    {
        if (IsOverOrEnding || cards.Count == 0) return;
        foreach (var card in cards.ToList())
        {
            MoveCard(card, PileType.Discard);
            _s.History.CardDiscarded(this, card);
            Each(l => l.AfterCardDiscarded(this, card));
        }
        if (drawCount > 0) Draw(drawCount);
    }

    /// <summary>
    /// Puts a card that was created during the fight into a pile and fires "card generated".
    /// Into the draw pile the default is the bottom, as in the game; say Random for "shuffled in".
    /// </summary>
    public bool AddGeneratedCard(CardInstance card, PileType pile, PilePosition position = PilePosition.Bottom, Creature? creator = null)
    {
        if (!_s.InProgress) return false;
        if (card.Pile != PileType.None) throw new InvalidOperationException($"{card} already is in the {card.Pile} pile.");
        _s.History.CardGenerated(this, card, creator);
        var moved = MoveCard(card, pile, position);
        Each(l => l.AfterCardGeneratedForCombat(this, card, creator));
        return moved;
    }

    /// <summary>Creates <paramref name="count"/> cards one by one straight into a pile (monsters' status cards).</summary>
    public void AddStatusCards(string className, int count, PileType pile, PilePosition position = PilePosition.Bottom, Creature? creator = null)
    {
        if (_s.Player.IsDead) return;
        for (var i = 0; i < count; i++)
            AddGeneratedCard(CreateCard(className), pile, position, creator);
    }

    /// <summary>Upgrades a card for the rest of the fight, if it can still be upgraded.</summary>
    public void Upgrade(CardInstance card)
    {
        if (IsEnding || !card.IsUpgradable) return;
        card.ApplyUpgrade();
    }

    /// <summary>
    /// Replaces a card in its pile, at its position, by a new card (Primal Force). The original
    /// leaves the fight without being exhausted or discarded.
    /// </summary>
    public CardInstance? Transform(CardInstance original, CardDef replacement, bool upgraded = false)
    {
        if (IsEnding || original.Pile == PileType.None) return null;
        var pile = original.Pile;
        var list = PileList(pile);
        var index = list.IndexOf(original);
        list.RemoveAt(index);
        original.Pile = PileType.None;

        var card = NewCard(replacement, upgraded);
        list.Insert(index, card);
        card.Pile = pile;
        _s.History.CardGenerated(this, card, _s.Player);
        Each(l => l.AfterCardEnteredCombat(this, card));
        Each(l => l.AfterCardChangedPiles(this, card, pile), guarded: false);
        Each(l => l.AfterCardChangedPilesLate(this, card, pile), guarded: false);
        // The game ends a transformation in a combat pile with "card generated", the card's owner as creator.
        Each(l => l.AfterCardGeneratedForCombat(this, card, _s.Player));
        return card;
    }

    /// <summary>
    /// Puts an affliction on a card. A card that already carries one keeps it as it is: another
    /// kind is refused, and so is the same kind again unless the affliction is stackable, in
    /// which case the amount is added. False when nothing changed.
    /// </summary>
    public bool Afflict(CardInstance card, string afflictionClassName, int amount = 1)
    {
        if (IsOverOrEnding && card.Pile != PileType.None) return false;
        var def = Content.Registry.Affliction(afflictionClassName);
        if (card.Affliction is null)
        {
            card.Affliction = afflictionClassName;
            card.AfflictionAmount = amount;
            return true;
        }
        if (card.Affliction != afflictionClassName || !def.IsStackable) return false;
        card.AfflictionAmount += amount;
        return true;
    }

    public void ClearAffliction(CardInstance card)
    {
        card.Affliction = null;
        card.AfflictionAmount = 0;
    }

    // ---------- drawing and shuffling (R17 to R20) ----------

    /// <summary>
    /// Draws up to <paramref name="count"/> cards (a fraction rounds up): asks "should draw" once,
    /// stops at 10 cards in hand, shuffles the discard pile in when the draw pile is empty, and
    /// runs the "card drawn" hooks after every single card. Returns the cards drawn.
    /// </summary>
    public IReadOnlyList<CardInstance> Draw(decimal count, bool fromHandDraw = false)
    {
        var drawn = new List<CardInstance>();
        if (IsOverOrEnding) return drawn;
        if (!AllListeners(l => l.ShouldDraw(this, fromHandDraw), guarded: true)) return drawn;

        var requested = count > 0m ? (int)Math.Ceiling(count) : 0;
        for (var i = 0; i < requested; i++)
        {
            if (_s.Hand.Count >= HandLimit || IsOverOrEnding) break;
            if (_s.Draw.Count + _s.Discard.Count == 0) break;
            ShuffleIfNecessary();
            if (_s.Draw.Count == 0 || _s.Hand.Count >= HandLimit) break;

            var card = _s.Draw[0];
            drawn.Add(card);
            MoveCard(card, PileType.Hand);
            _s.History.CardDrawn(this, card, fromHandDraw);
            Each(l => l.AfterCardDrawnEarly(this, card, fromHandDraw));
            Each(l => l.AfterCardDrawn(this, card, fromHandDraw));
        }
        return drawn;
    }

    /// <summary>R18: only when a card is about to be taken from an empty draw pile and the discard pile has cards.</summary>
    public void ShuffleIfNecessary()
    {
        if (_s.Draw.Count == 0 && _s.Discard.Count > 0) Shuffle();
    }

    /// <summary>
    /// Shuffles the discard pile (and whatever is still in the draw pile) into a new draw pile:
    /// sorted into a fixed order first, so the result does not depend on discard order, then the
    /// game's shuffle on the Shuffle stream. Stops in the middle if the fight starts ending.
    /// </summary>
    public void Shuffle()
    {
        if (IsOverOrEnding) return;
        var fromDraw = new HashSet<CardInstance>(_s.Draw, ReferenceEqualityComparer.Instance);

        // The game sorts with the runtime's own list sort, which is not order-keeping for every
        // size (three cards, or more than sixteen, can swap two identical copies). The same call
        // on the same input order gives the same order here, so identical cards that differ in
        // hidden state (a cost change, a number they raised) land where the game puts them.
        var keyed = _s.Discard.Concat(_s.Draw).Select(c => new ShuffleKey(SortKey(c.Def), c.UpgradeLevel, c)).ToList();
        keyed.Sort();
        var all = keyed.Select(k => k.Card).ToList();
        _s.Rng.Shuffle.Shuffle(all);

        _s.Draw.Clear();
        foreach (var card in all)
        {
            card.KnownPosition = false;
            if (fromDraw.Contains(card))
            {
                _s.Draw.Add(card);
                continue;
            }
            MoveCard(card, PileType.Draw);
            if (IsOverOrEnding) return;
        }
        Each(l => l.AfterShuffle(this));
    }

    /// <summary>A card as the pre-shuffle sort compares it: by model id (character codes), then upgrade level.</summary>
    private sealed class ShuffleKey : IComparable<ShuffleKey>
    {
        private readonly string _entry;
        private readonly int _upgradeLevel;

        public ShuffleKey(string entry, int upgradeLevel, CardInstance card)
        {
            _entry = entry;
            _upgradeLevel = upgradeLevel;
            Card = card;
        }

        public CardInstance Card { get; }

        public int CompareTo(ShuffleKey? other)
        {
            if (ReferenceEquals(this, other)) return 0;
            if (other is null) return 1;
            var byId = string.CompareOrdinal(_entry, other._entry);
            return byId != 0 ? byId : _upgradeLevel.CompareTo(other._upgradeLevel);
        }
    }

    /// <summary>
    /// The entry of the game's model id for a card: the class name in upper snake case. An
    /// underscore goes before every capital that follows a letter or a digit, also between two
    /// capitals in a row ("ExpectAFight" is EXPECT_A_FIGHT). Ids are compared by character code.
    /// </summary>
    internal static string SortKey(CardDef def)
    {
        var name = def.ClassName;
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsAsciiLetterUpper(name[i]) && char.IsAsciiLetterOrDigit(name[i - 1])) sb.Append('_');
            sb.Append(char.ToUpperInvariant(name[i]));
        }
        return sb.ToString();
    }

    // ---------- energy (R13) ----------

    /// <summary>Gains energy through the "energy gain" modifiers; not capped at the maximum.</summary>
    public void GainEnergy(decimal amount)
    {
        if (amount <= 0m || IsEnding) return;
        var final = Fold(amount, (l, n) => l.ModifyEnergyGain(this, n));
        if (final > 0m) _s.Energy = (int)Math.Clamp(_s.Energy + final, 0m, Cap);
    }

    /// <summary>Loses energy, not below 0.</summary>
    public void LoseEnergy(decimal amount)
    {
        if (amount <= 0m || IsEnding) return;
        _s.Energy = (int)Math.Clamp(_s.Energy - amount, 0m, Cap);
    }

    // ---------- cost and play conditions (R22, R23) ----------

    /// <summary>
    /// What the card costs now: its own cost changes, then the cost hooks of powers (normal pass,
    /// late pass), never below 0. -1 for a card with no cost; 0 for an X card (it takes all energy).
    /// </summary>
    public int CostOf(CardInstance card)
    {
        if (card.BaseCost < 0 || card.CostsX) return card.BaseCost;
        decimal cost = card.LocalCostValue();
        if (cost >= 0m)
        {
            cost = Fold(cost, (l, c) => l.ModifyEnergyCost(this, card, c));
            cost = Fold(cost, (l, c) => l.ModifyEnergyCostLate(this, card, c));
        }
        return Math.Max(0, (int)cost);
    }

    /// <summary>
    /// The card's cost after its own changes (an upgrade, "costs 0 this turn") and before the cost
    /// hooks of powers, never below 0; a card without a cost keeps its -1 and an X card reads 0.
    /// Touch of Insanity asks for this next to <see cref="CostOf"/>.
    /// </summary>
    public int LocalCostOf(CardInstance card) =>
        card.BaseCost < 0 || card.CostsX ? card.BaseCost : Math.Max(0, card.LocalCostValue());

    /// <summary>"1", "0", "X", or "-" for a card without a cost, as the mod prints it.</summary>
    public string CostText(CardInstance card)
    {
        if (card.CostsX) return "X";
        var cost = CostOf(card);
        return cost >= 0 ? cost.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>The X an X-cost card's effect uses: the energy captured for this play, through the X modifiers.</summary>
    public int ResolveX(CardInstance card) => (int)Fold(card.CapturedX, (l, x) => l.ModifyXValue(this, card, (int)x));

    /// <summary>Whether the card could be played from the hand now, and if not, why (every reason that holds).</summary>
    public bool CanPlay(CardInstance card, out UnplayableReason reason)
    {
        reason = UnplayableReason.None;
        if (card.Has(CardKeywords.Unplayable)) reason |= UnplayableReason.HasUnplayableKeyword;
        if (Math.Max(0, CostOf(card)) > _s.Energy) reason |= UnplayableReason.EnergyCostTooHigh;
        if (card.Target == TargetType.AnyAlly) reason |= UnplayableReason.NoLivingAllies; // one player: never an ally
        if (!AllListeners(l => l.ShouldPlay(this, card, AutoPlayType.None), guarded: true)) reason |= UnplayableReason.BlockedByHook;
        if (card.Def.IsPlayable is { } ok && !ok(this, card)) reason |= UnplayableReason.BlockedByCardLogic;
        return reason == UnplayableReason.None;
    }

    private static bool IsValidTarget(CardInstance card, Creature? target)
    {
        if (target is null) return card.Target is not (TargetType.AnyEnemy or TargetType.AnyAlly);
        if (!target.IsAlive) return false;
        return card.Target == TargetType.AnyEnemy && target.Side == Side.Enemy;
    }

    // ---------- playing a card (R23 to R28) ----------

    private void PlayFromHand(CardInstance card, Creature? target)
    {
        // R23: the cost is paid first, while the card is still in the hand. X takes everything.
        var toSpend = card.CostsX ? _s.Energy : Math.Max(0, CostOf(card));
        if (card.CostsX) card.CapturedX = toSpend;
        if (toSpend > 0)
        {
            _s.History.EnergySpent(this, toSpend);
            _s.Energy = Math.Max(0, _s.Energy - toSpend);
        }
        Each(l => l.AfterEnergySpent(this, card, toSpend));

        _s.CardsPlayed++;
        RunCardPlay(card, target, isAutoPlay: false, energySpent: toSpend, energyValue: toSpend);
    }

    private PileType DefaultResultPile(CardInstance card)
    {
        // R25: decided before the effect runs, so a keyword gained during the effect changes nothing.
        if (card.IsDupe || card.Type == CardType.Power) return PileType.None;
        if (card.ExhaustOnNextPlay || card.Has(CardKeywords.Exhaust))
        {
            card.ExhaustOnNextPlay = false;
            return PileType.Exhaust;
        }
        return PileType.Discard;
    }

    /// <summary>R24: one play, from the Play pile to the destination, with every run of the effect in between.</summary>
    private void RunCardPlay(CardInstance card, Creature? target, bool isAutoPlay, int energySpent, int energyValue)
    {
        // R24.2: to the Play pile. A manual play moves even past the usual guards (it was legal a moment ago).
        if (isAutoPlay)
        {
            MoveCard(card, PileType.Play);
        }
        else
        {
            if (IsOverOrEnding) return;
            var old = card.Pile;
            if (old != PileType.None) PileList(old).Remove(card);
            _s.Play.Add(card);
            card.Pile = PileType.Play;
            Each(l => l.AfterCardChangedPiles(this, card, old), guarded: false);
            Each(l => l.AfterCardChangedPilesLate(this, card, old), guarded: false);
        }

        // R24.3: where it will go.
        var resultPile = DefaultResultPile(card);
        var resultPosition = PilePosition.Bottom;
        if (_s.Starting || !IsOverOrEnding)
        {
            var list = RentListeners();
            foreach (var l in list)
                if (StillListening(l)) (resultPile, resultPosition) = l.ModifyCardPlayResultPile(this, card, isAutoPlay, resultPile, resultPosition);
            ReturnListeners(list);
        }

        // R24.4, R28: how many runs.
        var playCount = GeneratePlayCount(card, target);
        if (_s.Player.IsDead) return;

        // R24.5
        _s.EffectDepth++;
        for (var i = 0; i < playCount; i++)
        {
            var play = new CardPlay
            {
                Card = card, Target = target, ResultPile = resultPile, IsAutoPlay = isAutoPlay, PlayIndex = i, PlayCount = playCount,
                EnergySpent = energySpent, EnergyValue = energyValue, PlayId = _s.NextPlayId++,
            };
            Each(l => l.BeforeCardPlayed(this, play));
            _s.History.CardPlayStarted(this, play);
            card.Def.Play?.Invoke(new PlayContext(this, play));
            if (_s.Player.IsDead)
            {
                _s.EffectDepth--;
                return;
            }
            _s.History.CardPlayFinished(this, play);
            if (_s.InProgress)
            {
                // Not guarded: after the killing card these still run, and what they try is stopped by the commands.
                Each(l => l.AfterCardPlayed(this, play), guarded: false);
                Each(l => l.AfterCardPlayedLate(this, play), guarded: false);
                if (_s.Player.IsDead)
                {
                    _s.EffectDepth--;
                    return;
                }
            }
        }
        _s.EffectDepth--;

        // R24.6
        if (card.Pile == PileType.Play)
        {
            switch (resultPile)
            {
                case PileType.None:
                    RemoveFromCombat(card);
                    break;
                case PileType.Exhaust:
                    Exhaust(card);
                    break;
                default:
                    MoveCard(card, resultPile, resultPosition);
                    break;
            }
        }

        // R24.7, R24.8
        CheckForEmptyHand();
        card.AfterPlayedCleanup();
    }

    /// <summary>
    /// How many times a play of the card runs its effect: 1, plus the card's own replay count,
    /// through the play-count hooks. A listener that changed the count is told at once (that is
    /// where a one-shot doubling power spends its charge), so only ask when a play, or an effect
    /// that counts as one (Drum of Battle's exhaust trigger), is really happening.
    /// </summary>
    public int GeneratePlayCount(CardInstance card, Creature? target)
    {
        var playCount = card.ReplayCount + 1;
        if (!_s.Starting && IsOverOrEnding) return playCount;

        var list = RentListeners();
        List<Listener>? changed = null;
        foreach (var l in list)
        {
            if (!StillListening(l)) continue;
            var before = playCount;
            playCount = l.ModifyCardPlayCount(this, card, target, playCount);
            if (playCount != before) (changed ??= new()).Add(l);
        }
        ReturnListeners(list);
        if (changed is not null && (_s.Starting || !IsOverOrEnding))
            foreach (var l in changed) l.AfterModifyingCardPlayCount(this, card);
        return playCount;
    }

    private void CheckForEmptyHand()
    {
        if (_s.InProgress && _s.EffectDepth == 0 && _s.Hand.Count == 0) Each(l => l.AfterHandEmptied(this));
    }

    /// <summary>
    /// R29: an effect plays a card for free. Nothing is paid and energy is not checked; an
    /// Unplayable or forbidden card just moves to its result pile; a missing "any enemy" target is
    /// rolled among hittable enemies (CombatTargets stream, one draw even with one enemy); an X
    /// card uses the current energy as X without spending it.
    /// </summary>
    public void AutoPlay(CardInstance card, Creature? target = null, AutoPlayType type = AutoPlayType.Default)
    {
        if (IsOverOrEnding || _s.Player.IsDead) return;

        if (card.Has(CardKeywords.Unplayable) || !AllListeners(l => l.ShouldPlay(this, card, type), guarded: true))
        {
            MoveToResultPileWithoutPlaying(card);
            return;
        }
        if (card.Target == TargetType.AnyEnemy)
        {
            target ??= _s.Rng.CombatTargets.NextItem(HittableEnemies);
            if (target is null)
            {
                MoveToResultPileWithoutPlaying(card);
                return;
            }
        }
        else if (card.Target == TargetType.AnyAlly)
        {
            MoveToResultPileWithoutPlaying(card); // one player: never an ally
            return;
        }

        if (card.CostsX) card.CapturedX = _s.Energy;
        if (card.Pile == PileType.None) MoveCard(card, PileType.Play);
        Each(l => l.BeforeCardAutoPlayed(this, card, target, type));
        var value = card.CostsX ? _s.Energy : Math.Max(0, CostOf(card));
        RunCardPlay(card, target, isAutoPlay: true, energySpent: 0, energyValue: value);
    }

    private void MoveToResultPileWithoutPlaying(CardInstance card)
    {
        MoveCard(card, PileType.Play);
        if (card.Pile != PileType.Play) return;
        if (card.IsDupe) RemoveFromCombat(card);
        else if (card.ExhaustOnNextPlay || card.Has(CardKeywords.Exhaust)) Exhaust(card);
        else MoveCard(card, PileType.Discard);
    }

    /// <summary>
    /// Plays cards off the draw pile (Havoc, Cascade): all of them are moved to the Play pile
    /// first, shuffling the discard pile in when the draw pile runs dry, and only then played in
    /// the order taken.
    /// </summary>
    public void AutoPlayFromDrawPile(int count, PilePosition position = PilePosition.Top, bool forceExhaust = false)
    {
        if (IsOverOrEnding) return;
        var cards = new List<CardInstance>(count);
        for (var i = 0; i < count; i++)
        {
            ShuffleIfNecessary();
            var card = position switch
            {
                PilePosition.Bottom => _s.Draw.LastOrDefault(),
                PilePosition.Top => _s.Draw.FirstOrDefault(),
                _ => _s.Rng.CombatCardSelection.NextItem(_s.Draw),
            };
            if (card is null) break;
            cards.Add(card);
            MoveCard(card, PileType.Play);
        }
        foreach (var card in cards)
        {
            if (_s.Player.IsDead) break;
            card.ExhaustOnNextPlay = forceExhaust;
            AutoPlay(card);
        }
    }

    // ---------- potions (R31) ----------

    private void UsePotion(int slot, Creature? target)
    {
        var potion = _s.Potions[slot]!;
        // The potion leaves the belt before its effect runs.
        _s.Potions[slot] = null;
        if (target is null && potion.Target is TargetType.AnyPlayer or TargetType.Self) target = _s.Player;

        Each(l => l.BeforePotionUsed(this, potion, target), guarded: false);
        _s.EffectDepth++;
        potion.Use(new PotionContext(this, potion, target));
        _s.EffectDepth--;
        if (_s.InProgress) _s.History.PotionUsed(this, potion, target);
        Each(l => l.AfterPotionUsed(this, potion, target), guarded: false);
        CheckForEmptyHand();
    }

    /// <summary>
    /// Uses the potion in <paramref name="slot"/> from inside a hook (Fairy in a Bottle when its
    /// owner would die): the same steps as a use by hand. It leaves the belt, the potion hooks
    /// run around its effect, and the use is written to the history. Nothing happens for an empty slot.
    /// </summary>
    public void UsePotionFromHook(int slot, Creature? target)
    {
        if (slot < 0 || slot >= _s.Potions.Length || _s.Potions[slot] is null) return;
        UsePotion(slot, target);
    }

    /// <summary>Takes a potion out of its belt slot without using it (a discard).</summary>
    public void RemovePotion(int slot) => _s.Potions[slot] = null;

    /// <summary>The first belt slot that holds this potion, or -1.</summary>
    public int PotionSlot(PotionDef potion) => Array.IndexOf(_s.Potions, potion);

    /// <summary>Puts a potion into the first empty belt slot. False when the belt is full.</summary>
    public bool GainPotion(PotionDef potion)
    {
        for (var i = 0; i < _s.Potions.Length; i++)
        {
            if (_s.Potions[i] is not null) continue;
            _s.Potions[i] = potion;
            return true;
        }
        return false;
    }
}
