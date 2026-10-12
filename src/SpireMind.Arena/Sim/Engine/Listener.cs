namespace SpireMind.Arena;

/// <summary>
/// Everything that can hear a hook: powers, monsters, relics and card behaviours. Each method
/// is one of the game's hooks under the game's name, with the fight passed in first. The
/// default does nothing. Who is asked first, and which hooks still reach listeners while the
/// fight is ending, is decided in Fight.Hooks.cs (rules-turns.md R2 and R3).
/// <para>
/// A listener must keep all of its state in its own fields: plain values are copied when the
/// fight is cloned; fields that hold a <see cref="Creature"/>, <see cref="CardInstance"/> or
/// <see cref="Power"/> are pointed at the clone's objects; anything else that can change
/// (a list, a set) must be copied in <see cref="OnCloned"/>.
/// </para>
/// </summary>
public abstract class Listener
{
    /// <summary>Called on the copy after a fight is cloned. Copy lists and other mutable
    /// reference fields here; creature, card and power fields are already remapped.</summary>
    public virtual void OnCloned(CloneMap map) { }

    // ---------- start and end of the fight ----------

    /// <summary>After every monster has its powers and first intent, the fight just marked in progress (R5 step 7).</summary>
    public virtual void BeforeCombatStart(Fight f) { }
    public virtual void BeforeCombatStartLate(Fight f) { }

    /// <summary>On a win, first thing after the fight is marked over (R48 step 2).</summary>
    public virtual void AfterCombatEnd(Fight f) { }

    /// <summary>On a win, after the player's powers, piles and Block are gone (R48 step 4).</summary>
    public virtual void AfterCombatVictoryEarly(Fight f) { }
    public virtual void AfterCombatVictory(Fight f) { }

    // ---------- start of a side's turn, in this order (R11, R38) ----------

    /// <summary>First step of a side's turn: before monsters choose moves and before Block is cleared.
    /// Block gained here by the side that is starting is wiped.</summary>
    public virtual void BeforeSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) { }

    /// <summary>False keeps <paramref name="creature"/>'s Block at the start of its side's turn (Barricade).</summary>
    public virtual bool ShouldClearBlock(Fight f, Creature creature) => true;

    /// <summary>Once per creature of the side, right after the Block clear (also on turn 1, where nothing was cleared).</summary>
    public virtual void AfterBlockCleared(Fight f, Creature creature) { }

    /// <summary>False adds the maximum to what is left instead of setting energy to the maximum.</summary>
    public virtual bool ShouldResetEnergy(Fight f) => true;

    /// <summary>Player turn only, after energy was reset and before the hand draw.</summary>
    public virtual void AfterEnergyReset(Fight f) { }
    public virtual void AfterEnergyResetLate(Fight f) { }

    public virtual void BeforeHandDraw(Fight f) { }
    public virtual void BeforeHandDrawLate(Fight f) { }

    /// <summary>The number of cards the turn-start draw takes (base 5). Return the new count.</summary>
    public virtual decimal ModifyHandDraw(Fight f, decimal count) => count;
    public virtual decimal ModifyHandDrawLate(Fight f, decimal count) => count;

    /// <summary>Player turn only, right after the hand draw (Inferno and Crimson Mantle hurt here).</summary>
    public virtual void AfterPlayerTurnStartEarly(Fight f) { }
    public virtual void AfterPlayerTurnStart(Fight f) { }
    public virtual void AfterPlayerTurnStartLate(Fight f) { }

    /// <summary>After the draw on the player side; after the Block clear and before any monster acts
    /// on the enemy side (Demon Form, Plating's countdown, Slow's reset, Poison).</summary>
    public virtual void AfterSideTurnStart(Fight f, Side side, IReadOnlyList<Creature> participants) { }
    public virtual void AfterSideTurnStartLate(Fight f, Side side, IReadOnlyList<Creature> participants) { }

    /// <summary>Last step before the player may act.</summary>
    public virtual void AfterAutoPrePlayPhaseEnteredEarly(Fight f) { }
    public virtual void AfterAutoPrePlayPhaseEntered(Fight f) { }
    public virtual void AfterAutoPrePlayPhaseEnteredLate(Fight f) { }

    // ---------- end of a side's turn, in this order (R32, R38) ----------

    /// <summary>First step after the player ends the turn (Stampede, Howl from Beyond).</summary>
    public virtual void AfterAutoPostPlayPhaseEntered(Fight f) { }

    /// <summary>Three passes before the hand is touched; Plating gives its Block in the Early one.</summary>
    public virtual void BeforeSideTurnEndVeryEarly(Fight f, Side side, IReadOnlyList<Creature> participants) { }
    public virtual void BeforeSideTurnEndEarly(Fight f, Side side, IReadOnlyList<Creature> participants) { }
    public virtual void BeforeSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants) { }

    /// <summary>False leaves an Ethereal card in hand at end of turn.</summary>
    public virtual bool ShouldEtherealTrigger(Fight f, CardInstance card) => true;

    /// <summary>After Ethereal cards and end-of-turn-in-hand cards resolved, before the hand is discarded.</summary>
    public virtual void BeforeFlush(Fight f) { }
    public virtual void BeforeFlushLate(Fight f) { }

    /// <summary>False keeps the whole hand at end of turn.</summary>
    public virtual bool ShouldFlush(Fight f) => true;

    public virtual void AfterFlush(Fight f, IReadOnlyList<CardInstance> flushed, IReadOnlyList<CardInstance> retained) { }

    /// <summary>
    /// The last hook of a side's turn. For the player side it runs after the hand was discarded
    /// (temporary Strength, Rage, No Draw, Constrict end here). For the enemy side it runs after
    /// every monster acted: Vulnerable, Weak and Frail tick here for everyone.
    /// </summary>
    public virtual void AfterSideTurnEnd(Fight f, Side side, IReadOnlyList<Creature> participants) { }
    public virtual void AfterSideTurnEndLate(Fight f, Side side, IReadOnlyList<Creature> participants) { }

    // ---------- cards ----------

    /// <summary>False forbids the play (hand cards read "BlockedByHook"; an auto-played card moves on unplayed).</summary>
    public virtual bool ShouldPlay(Fight f, CardInstance card, AutoPlayType autoPlay) => true;

    /// <summary>The energy cost of a card, after its own modifiers. Return the new cost (never called for X or unplayable costs).</summary>
    public virtual decimal ModifyEnergyCost(Fight f, CardInstance card, decimal cost) => cost;
    public virtual decimal ModifyEnergyCostLate(Fight f, CardInstance card, decimal cost) => cost;

    /// <summary>Right after a card's cost was paid, before it leaves the hand.</summary>
    public virtual void AfterEnergySpent(Fight f, CardInstance card, int amount) { }

    /// <summary>The X of an X-cost card. Return the new value.</summary>
    public virtual int ModifyXValue(Fight f, CardInstance card, int x) => x;

    /// <summary>Where a played card goes afterwards, decided before its effect runs.</summary>
    public virtual (PileType Pile, PilePosition Position) ModifyCardPlayResultPile(
        Fight f, CardInstance card, bool isAutoPlay, PileType pile, PilePosition position) => (pile, position);

    /// <summary>How many times the card's effect runs. Return the new count.</summary>
    public virtual int ModifyCardPlayCount(Fight f, CardInstance card, Creature? target, int playCount) => playCount;

    /// <summary>Called on a listener whose <see cref="ModifyCardPlayCount"/> changed the count (spend the charge here).</summary>
    public virtual void AfterModifyingCardPlayCount(Fight f, CardInstance card) { }

    /// <summary>An effect is about to play <paramref name="card"/> for free.</summary>
    public virtual void BeforeCardAutoPlayed(Fight f, CardInstance card, Creature? target, AutoPlayType type) { }

    /// <summary>Once per run of a card's effect, before it (the cost is already paid, the card is in the Play pile).</summary>
    public virtual void BeforeCardPlayed(Fight f, CardPlay play) { }

    /// <summary>Once per run of a card's effect, after it. Still called after the killing card,
    /// when most commands no longer do anything.</summary>
    public virtual void AfterCardPlayed(Fight f, CardPlay play) { }
    public virtual void AfterCardPlayedLate(Fight f, CardPlay play) { }

    /// <summary>The hand became empty after a card or potion finished.</summary>
    public virtual void AfterHandEmptied(Fight f) { }

    /// <summary>False blocks a whole draw request (No Draw; never the turn-start draw there).</summary>
    public virtual bool ShouldDraw(Fight f, bool fromHandDraw) => true;

    /// <summary>After each single card drawn, before the next one of the same draw.</summary>
    public virtual void AfterCardDrawnEarly(Fight f, CardInstance card, bool fromHandDraw) { }
    public virtual void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw) { }

    /// <summary>After the discard pile was shuffled into the draw pile.</summary>
    public virtual void AfterShuffle(Fight f) { }

    public virtual void AfterCardExhausted(Fight f, CardInstance card, bool causedByEthereal) { }

    /// <summary>A real discard (an effect discarded the card). The end-of-turn flush is not one.</summary>
    public virtual void AfterCardDiscarded(Fight f, CardInstance card) { }

    /// <summary>A card was created during the fight and put in a pile.</summary>
    public virtual void AfterCardGeneratedForCombat(Fight f, CardInstance card, Creature? creator) { }

    /// <summary>A card that was in no pile entered a combat pile (created or transformed in).</summary>
    public virtual void AfterCardEnteredCombat(Fight f, CardInstance card) { }

    /// <summary>Any move of a card between piles (also the flush and reshuffles). Reaches listeners while the fight is ending.</summary>
    public virtual void AfterCardChangedPiles(Fight f, CardInstance card, PileType oldPile) { }
    public virtual void AfterCardChangedPilesLate(Fight f, CardInstance card, PileType oldPile) { }

    // ---------- energy ----------

    /// <summary>An energy gain from a card, potion or power. Return the new amount.</summary>
    public virtual decimal ModifyEnergyGain(Fight f, decimal amount) => amount;

    /// <summary>The player's maximum energy (base 3). Return the new amount.</summary>
    public virtual decimal ModifyMaxEnergy(Fight f, decimal amount) => amount;

    // ---------- damage (rules-damage.md R3, R4); these reach listeners while the fight is ending ----------

    /// <summary>An amount to add to one hit. Return 0 for none. <paramref name="target"/> is null in card previews.</summary>
    public virtual decimal ModifyDamageAdditive(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => 0m;

    /// <summary>A factor for one hit. Return 1 for none.</summary>
    public virtual decimal ModifyDamageMultiplicative(Fight f, Creature? target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => 1m;

    /// <summary>The most one hit may do. Return decimal.MaxValue for no cap.</summary>
    public virtual decimal ModifyDamageCap(Fight f, Creature? target, ValueProp props, Creature? dealer, CardInstance? cardSource) => decimal.MaxValue;

    /// <summary>Before Block absorbs a hit (Thorns answers here; the hit still lands).</summary>
    public virtual void BeforeDamageReceived(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) { }

    /// <summary>The HP a creature is about to lose after Block, in four passes. Return the new amount.</summary>
    public virtual decimal ModifyHpLostBeforeOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => amount;
    public virtual decimal ModifyHpLostBeforeOstyLate(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => amount;
    public virtual decimal ModifyHpLostAfterOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => amount;
    public virtual decimal ModifyHpLostAfterOstyLate(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) => amount;

    /// <summary>Called on a listener whose HP-loss modifier changed the whole-number result.</summary>
    public virtual void AfterModifyingHpLostBeforeOsty(Fight f) { }
    public virtual void AfterModifyingHpLostAfterOsty(Fight f) { }

    /// <summary>A hit took a creature's Block to 0.</summary>
    public virtual void AfterBlockBroken(Fight f, Creature creature) { }

    /// <summary>HP went down (negative delta) or up (heal).</summary>
    public virtual void AfterCurrentHpChanged(Fight f, Creature creature, decimal delta) { }

    /// <summary>After every damage instance, also for the one that killed.</summary>
    public virtual void AfterDamageGiven(Fight f, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardInstance? cardSource) { }

    /// <summary>After every damage instance the target survived, also fully blocked hits and hits of 0.
    /// Not called for the hit that kills.</summary>
    public virtual void AfterDamageReceived(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource) { }
    public virtual void AfterDamageReceivedLate(Fight f, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardInstance? cardSource) { }

    /// <summary>Once per attack command, before its first hit and after its last.</summary>
    public virtual void BeforeAttack(Fight f, AttackCommand attack) { }
    public virtual void AfterAttack(Fight f, AttackCommand attack) { }

    // ---------- block (rules-damage.md R8) ----------

    /// <summary>An amount to add to a Block gain. Return 0 for none.</summary>
    public virtual decimal ModifyBlockAdditive(Fight f, Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 0m;

    /// <summary>A factor for a Block gain. Return 1 for none.</summary>
    public virtual decimal ModifyBlockMultiplicative(Fight f, Creature target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay) => 1m;

    /// <summary>After every Block gain, with the unrounded amount (which may be above 0 and still add nothing).</summary>
    public virtual void AfterBlockGained(Fight f, Creature creature, decimal amount, ValueProp props, CardInstance? cardSource) { }

    // ---------- powers (R51) ----------

    public virtual void BeforePowerAmountChanged(Fight f, Power power, decimal amount, Creature target, Creature? applier, CardInstance? cardSource) { }

    public virtual decimal ModifyPowerAmountGivenAdditive(Fight f, Power power, Creature giver, decimal amount, Creature? target, CardInstance? cardSource) => 0m;
    public virtual decimal ModifyPowerAmountGivenMultiplicative(Fight f, Power power, Creature giver, decimal amount, Creature? target, CardInstance? cardSource) => 1m;

    /// <summary>Return true and set <paramref name="modified"/> to change what <paramref name="target"/> receives (Artifact sets 0).</summary>
    public virtual bool TryModifyPowerAmountReceived(Fight f, Power power, Creature target, decimal amount, Creature? applier, out decimal modified)
    {
        modified = amount;
        return false;
    }

    /// <summary>Called on a listener whose <see cref="TryModifyPowerAmountReceived"/> returned true.</summary>
    public virtual void AfterModifyingPowerAmountReceived(Fight f, Power power) { }

    /// <summary>After a power landed or changed by a non-zero amount (not on ticks to nothing).</summary>
    public virtual void AfterPowerAmountChanged(Fight f, Power power, decimal amount, Creature? applier, CardInstance? cardSource) { }

    // ---------- death and the list of creatures; these reach listeners while the fight is ending ----------

    public virtual void BeforeDeath(Fight f, Creature creature) { }

    /// <summary>False prevents the death.</summary>
    public virtual bool ShouldDie(Fight f, Creature creature) => true;
    public virtual bool ShouldDieLate(Fight f, Creature creature) => true;

    public virtual void AfterDeath(Fight f, Creature creature, bool wasRemovalPrevented) { }

    /// <summary>Called on the one listener whose <see cref="ShouldDie"/> or <see cref="ShouldDieLate"/> said no.
    /// If the creature is still at 0 HP afterwards, the death is tried again.</summary>
    public virtual void AfterPreventingDeath(Fight f, Creature creature) { }

    /// <summary>False keeps a dead monster in the enemy list (Illusion).</summary>
    public virtual bool ShouldCreatureBeRemovedFromCombatAfterDeath(Fight f, Creature creature) => true;

    /// <summary>False keeps <paramref name="power"/> on its dead owner.</summary>
    public virtual bool ShouldPowerBeRemovedOnDeath(Fight f, Power power) => true;

    /// <summary>True keeps the fight going although no primary enemy is alive (Infested).</summary>
    public virtual bool ShouldStopCombatFromEnding(Fight f) => false;

    /// <summary>False makes <paramref name="creature"/> un-hittable and unable to receive powers (a reviving Illusion owner).</summary>
    public virtual bool ShouldAllowHitting(Fight f, Creature creature) => true;

    /// <summary>False makes <paramref name="creature"/> untargetable by cards and potions.</summary>
    public virtual bool ShouldAllowTargeting(Fight f, Creature creature) => true;

    public virtual void AfterCreatureAddedToCombat(Fight f, Creature creature) { }

    // ---------- potions; these reach listeners while the fight is ending ----------

    public virtual void BeforePotionUsed(Fight f, PotionDef potion, Creature? target) { }
    public virtual void AfterPotionUsed(Fight f, PotionDef potion, Creature? target) { }

    // ---------- the run (run mode) ----------
    //
    // Hooks of the run rather than of a fight. They are handed a RunContext instead of a fight,
    // because most of them are heard outside fights and some in both places. RunHooks
    // (Sim/Run/RunHooks.cs) sends each of them, in the game's order: listening deck cards
    // (IListensInDeck) in deck order, relics in pickup order, potions' hook objects in slot order.
    // Where a line says "with the fight's listeners", a dispatch inside a fight goes on to the
    // player's powers, the cards in the piles and the enemies, as the fight's own hooks do.
    //
    // The pickup itself is a relic's own: Relic.AfterObtained and Relic.AfterRemoved (Models.cs).
    // The hooks whose arguments are things later parts of run mode define (rewards, card offers,
    // shop entries, rest site options) are declared with those parts; RunHooks.Hooks lists all 27.

    /// <summary>
    /// A room was entered and is set up. In a fight's room this comes after the monsters stand
    /// and the draw pile is shuffled, before the monsters get their starting powers and first
    /// intents and before <see cref="BeforeCombatStart"/> (<c>c.Fight</c> is the fight; powers
    /// can be applied). In every other room <c>c.Run</c> is the run. Never with the fight's listeners.
    /// </summary>
    public virtual void AfterRoomEntered(RunContext c, RoomKind room) { }

    /// <summary>An amount of gold the player is about to gain. Return the new amount. With the fight's listeners.</summary>
    public virtual decimal ModifyGoldGained(RunContext c, decimal amount) => amount;

    /// <summary>Called on a listener whose <see cref="ModifyGoldGained"/> changed the whole-number amount, with the final amount. With the fight's listeners.</summary>
    public virtual void AfterModifyingGoldGained(RunContext c, decimal amount) { }

    /// <summary>After gold was gained (not when the final amount was 0 or less).</summary>
    public virtual void AfterGoldGained(RunContext c) { }

    /// <summary>
    /// A card is about to be added to the deck. Return true and set <paramref name="newCard"/> to
    /// put another card in its place (the Eggs hand back an upgraded copy). Two passes; every
    /// listener of the first is asked before any of the second.
    /// </summary>
    public virtual bool TryModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        return false;
    }

    public virtual bool TryModifyCardBeingAddedToDeckLate(RunContext c, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        return false;
    }

    /// <summary>A potion went into the belt. With the fight's listeners.</summary>
    public virtual void AfterPotionProcured(RunContext c, PotionDef potion) { }

    /// <summary>A potion was thrown away. With the fight's listeners.</summary>
    public virtual void AfterPotionDiscarded(RunContext c, PotionDef potion) { }

    /// <summary>What an Unknown map point may become. Return the set to go on with (Juzu Bracelet takes Monster out).</summary>
    public virtual IReadOnlySet<RolledRoom> ModifyUnknownMapPointRoomTypes(RunContext c, IReadOnlySet<RolledRoom> roomTypes) => roomTypes;

    /// <summary>True makes the fight of this kind of room drop a potion whatever the roll says. Listeners after the first yes are not asked.</summary>
    public virtual bool ShouldForcePotionReward(RunContext c, RoomKind room) => false;

    /// <summary>True lets the player travel to any point of the next map row. Listeners after the first yes are not asked.</summary>
    public virtual bool ShouldAllowFreeTravel(RunContext c) => false;

    /// <summary>False leaves a treasure room without its relic. Listeners after the first no are not asked.</summary>
    public virtual bool ShouldGenerateTreasure(RunContext c) => true;

    /// <summary>False keeps the other rest site options after one was used. Listeners after the first no are not asked.</summary>
    public virtual bool ShouldDisableRemainingRestSiteOptions(RunContext c) => true;

    /// <summary>What resting heals. Return the new amount.</summary>
    public virtual decimal ModifyRestSiteHealAmount(RunContext c, decimal amount) => amount;

    /// <summary>After the heal of a rest (also one a relic or event mimics).</summary>
    public virtual void AfterRestSiteHeal(RunContext c, bool isMimicked) { }

    /// <summary>Lines added to what the Rest option says it does. Return the list to go on with.</summary>
    public virtual IReadOnlyList<string> ModifyExtraRestSiteHealText(RunContext c, IReadOnlyList<string> extraText) => extraText;

    /// <summary>Called on a listener that changed a set of rewards.</summary>
    public virtual void AfterModifyingRewards(RunContext c) { }

    /// <summary>Called on a listener that changed the cards of a card reward.</summary>
    public virtual void AfterModifyingCardRewardOptions(RunContext c) { }
}
