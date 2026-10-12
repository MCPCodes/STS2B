namespace SpireMind.Arena;

/// <summary>One of the hooks outside a fight that a relic an Ironclad can meet in act 1 uses.</summary>
/// <param name="Name">The game's name of the hook (a method of its AbstractModel, or of RelicModel for the pickup).</param>
/// <param name="Users">How many of the 157 reachable relics override it (relics.json).</param>
/// <param name="SentBy">The call site that sends it; empty while the hook waits for its arguments' types. A hook sent by
/// <see cref="RewardHooks"/> is declared on <see cref="IRewardHooks"/>, every other one on <see cref="Listener"/> or <see cref="Relic"/>.</param>
/// <param name="Waits">What the hook's arguments are made of when the arena does not hold that yet, and the chunk that brings it; null for a hook that is declared.</param>
/// <param name="Shape">For a waiting hook: how the game sends it, in terms of <see cref="RunHooks"/>'s own helpers.</param>
public sealed record RunHookInfo(string Name, int Users, string SentBy, string? Waits = null, string? Shape = null)
{
    public bool IsDeclared => Waits is null;
}

/// <summary>
/// The call sites of the run's hooks: one method per hook, which asks everyone who hears it in
/// the game's order and puts the answers together the way the game's Hook class does. Whoever
/// reaches the moment in the game's flow calls the method here; nobody walks the relics himself.
/// <code>
/// if (RunHooks.ShouldAllowFreeTravel(run.Context)) ...            // the map, building its next points
/// allowed = RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, allowed);
/// </code>
/// Every method takes a <see cref="RunContext"/>: <c>run.Context</c> outside a fight, and inside
/// one the fight's (<see cref="Fight.GainGold"/> is such a caller). The order is
/// <see cref="RunContext.Each"/>'s: listening deck cards, relics, potions; a hook that also
/// belongs to the fight goes on to the fight's own listeners when it is sent inside one.
/// <para>
/// <see cref="Hooks"/> lists the 27 hooks outside a fight that the relics of an act 1 Ironclad
/// use. All are declared and sent: twenty-one on <see cref="Listener"/> (and
/// <see cref="Relic"/> for the pickup), sent from here, from the command named in the list,
/// from <see cref="RestSite.GenerateOptions"/> (the options of a rest site) or from
/// <see cref="ShopHooks"/> (a shop entry's price and refill), and the six whose arguments are
/// rewards and card offers on <see cref="IRewardHooks"/>, sent from <see cref="RewardHooks"/>
/// through the helpers here. The content that first uses one brings its checks. A hook that a
/// later part needs and that is not among the 27 is declared by that part on
/// <see cref="Listener"/> and sent with <see cref="Modifiers"/>, <see cref="Fold{T}"/>,
/// <see cref="Any"/>, <see cref="All"/> or <see cref="Each"/> from one named call site.
/// </para>
/// <para>
/// Hooks outside a fight that no reachable relic's count is listed for live with their part:
/// the other ten of <see cref="IRewardHooks"/>, and the deck's <see cref="IDeckListener"/>
/// (sent by <see cref="DeckHooks"/>).
/// </para>
/// </summary>
public static class RunHooks
{
    /// <summary>The 27, in the order of how many reachable relics use each.</summary>
    public static readonly IReadOnlyList<RunHookInfo> Hooks =
    [
        new("AfterObtained", 38, "Run.ObtainRelic, Fight.ObtainRelic"),
        new("AfterRoomEntered", 23, "Run.AfterRoomEntered, Fight.StartCombat"),
        new("TryModifyCardRewardOptionsLate", 7, "RewardHooks.ModifyCardRewardOptions (the second pass), CardReward.OnRelicObtained"),
        new("TryModifyRewards", 4, "RewardHooks.ModifyRewards"),
        new("ModifyMerchantCardCreationResults", 3, "RewardHooks.ModifyMerchantCardCreationResults"),
        new("TryModifyCardBeingAddedToDeck", 3, "RunHooks.ModifyCardBeingAddedToDeck (called by DeckEdits)"),
        new("ModifyExtraRestSiteHealText", 3, "RunHooks.ModifyExtraRestSiteHealText"),
        new("TryModifyRestSiteOptions", 2, "RestSite.GenerateOptions"),
        new("ModifyMerchantPrice", 2, "ShopHooks.ModifyMerchantPrice"),
        new("AfterRestSiteHeal", 2, "RunHooks.AfterRestSiteHeal"),
        new("AfterModifyingCardRewardOptions", 2, "RunHooks.AfterModifyingCardRewardOptions"),
        new("AfterModifyingRewards", 1, "RunHooks.AfterModifyingRewards"),
        new("AfterPotionProcured", 1, "RunHooks.AfterPotionProcured"),
        new("AfterPotionDiscarded", 1, "RunHooks.AfterPotionDiscarded"),
        new("ModifyGoldGained", 1, "RunHooks.GainGold"),
        new("AfterModifyingGoldGained", 1, "RunHooks.GainGold"),
        new("ModifyCardRewardCreationOptions", 1, "RewardHooks.ModifyCardRewardCreationOptions"),
        new("AfterGoldGained", 1, "RunHooks.GainGold"),
        new("ModifyUnknownMapPointRoomTypes", 1, "RunHooks.ModifyUnknownMapPointRoomTypes"),
        new("TryModifyCardRewardOptions", 1, "RewardHooks.ModifyCardRewardOptions, CardReward.OnRelicObtained"),
        new("ShouldDisableRemainingRestSiteOptions", 1, "RunHooks.ShouldDisableRemainingRestSiteOptions"),
        new("ModifyRestSiteHealAmount", 1, "RunHooks.ModifyRestSiteHealAmount"),
        new("ShouldGenerateTreasure", 1, "RunHooks.ShouldGenerateTreasure"),
        new("ShouldRefillMerchantEntry", 1, "ShopHooks.ShouldRefillMerchantEntry"),
        new("TryModifyRestSiteHealRewards", 1, "RewardHooks.ModifyRestSiteHealRewards"),
        new("ShouldForcePotionReward", 1, "RunHooks.ShouldForcePotionReward"),
        new("ShouldAllowFreeTravel", 1, "RunHooks.ShouldAllowFreeTravel"),
    ];

    // ---------- the helpers every hook is sent with ----------

    /// <summary>Calls <paramref name="call"/> on the run's listeners in order (a hook that tells and asks nothing back).</summary>
    public static void Each(RunContext c, Action<Listener> call) => c.Each(call, withFight: false);

    /// <summary>Runs a value through the run's listeners in order: each gets what the one before returned.</summary>
    public static T Fold<T>(RunContext c, T value, Func<Listener, T, T> step)
    {
        c.Each(l => value = step(l, value), withFight: false);
        return value;
    }

    /// <summary>
    /// The listeners that answered yes, in order: the game's "try to modify" pattern, where every
    /// listener is asked and those that changed something are told afterwards
    /// (<see cref="AfterModifyingRewards"/>, <see cref="AfterModifyingCardRewardOptions"/>).
    /// </summary>
    public static List<Listener> Modifiers(RunContext c, Func<Listener, bool> tryModify)
    {
        var modifiers = new List<Listener>();
        c.Each(l =>
        {
            if (tryModify(l)) modifiers.Add(l);
        }, withFight: false);
        return modifiers;
    }

    /// <summary>True when some listener says yes. Listeners after the first yes are not asked.</summary>
    public static bool Any(RunContext c, Func<Listener, bool> ask)
    {
        var any = false;
        c.Each(l => any = any || ask(l), withFight: false);
        return any;
    }

    /// <summary>True when every listener says yes. Listeners after the first no are not asked.</summary>
    public static bool All(RunContext c, Func<Listener, bool> ask)
    {
        var all = true;
        c.Each(l => all = all && ask(l), withFight: false);
        return all;
    }

    /// <summary>Calls <paramref name="call"/> on those of <paramref name="modifiers"/> that still listen, in listener order.</summary>
    private static void EachOf(RunContext c, IReadOnlyCollection<Listener> modifiers, Action<Listener> call, bool withFight)
    {
        if (modifiers.Count == 0) return;
        c.Each(l =>
        {
            if (modifiers.Contains(l)) call(l);
        }, withFight);
    }

    // ---------- rooms ----------

    /// <summary>
    /// "After room entered" (Hook.AfterRoomEntered): to the run's listeners alone, also in a
    /// fight's room. A fight sends it itself during its set-up (<see cref="Fight"/>'s
    /// StartCombat); every other room goes through <see cref="Run.AfterRoomEntered"/>.
    /// </summary>
    public static void AfterRoomEntered(RunContext c, RoomKind room) => Each(c, l => l.AfterRoomEntered(c, room));

    // ---------- gold ----------

    /// <summary>
    /// PlayerCmd.GainGold, in a fight or outside one: every listener may change the amount, in
    /// the fight's order when one is on; those whose step changed the whole-number amount are
    /// told the final amount; nothing more happens when that is 0 or less; else its whole-number
    /// part is paid and the run's listeners hear "gold gained". Returns what was paid.
    /// </summary>
    public static int GainGold(RunContext c, decimal amount)
    {
        List<Listener>? changed = null;
        c.Each(l =>
        {
            var before = amount;
            amount = l.ModifyGoldGained(c, amount);
            if ((int)before != (int)amount) (changed ??= new()).Add(l);
        }, withFight: true);
        if (changed is not null) EachOf(c, changed, l => l.AfterModifyingGoldGained(c, amount), withFight: true);
        if (!(amount > 0m)) return 0;

        var paid = (int)amount;
        c.SetGold(c.Gold + paid);
        Each(c, l => l.AfterGoldGained(c));
        return paid;
    }

    // ---------- the deck ----------

    /// <summary>
    /// Hook.ModifyCardBeingAddedToDeck: every listener may hand back another card to add in the
    /// card's place (first pass, then the Late pass; each is shown the card as the one before
    /// left it). Returns the card to add; <paramref name="modifiers"/> are the listeners that
    /// changed it, for the caller's flash and log.
    /// </summary>
    public static CardInstance ModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out List<Listener> modifiers)
    {
        var changed = new List<Listener>();
        Each(c, l =>
        {
            if (!l.TryModifyCardBeingAddedToDeck(c, card, out var replaced) || replaced is null) return;
            changed.Add(l);
            card = replaced;
        });
        Each(c, l =>
        {
            if (!l.TryModifyCardBeingAddedToDeckLate(c, card, out var replaced) || replaced is null) return;
            changed.Add(l);
            card = replaced;
        });
        modifiers = changed;
        return card;
    }

    // ---------- potions ----------

    /// <summary>Hook.AfterPotionProcured: a potion went into the belt. With the fight's listeners inside a fight.</summary>
    public static void AfterPotionProcured(RunContext c, PotionDef potion) => c.Each(l => l.AfterPotionProcured(c, potion), withFight: true);

    /// <summary>Hook.AfterPotionDiscarded: a potion was thrown away. With the fight's listeners inside a fight.</summary>
    public static void AfterPotionDiscarded(RunContext c, PotionDef potion) => c.Each(l => l.AfterPotionDiscarded(c, potion), withFight: true);

    /// <summary>Hook.ShouldForcePotionReward: whether the fight of a room of this kind drops a potion whatever the roll says.</summary>
    public static bool ShouldForcePotionReward(RunContext c, RoomKind room) => Any(c, l => l.ShouldForcePotionReward(c, room));

    // ---------- the map ----------

    /// <summary>Hook.ModifyUnknownMapPointRoomTypes: what an Unknown map point may become, after every listener took kinds away.</summary>
    public static IReadOnlySet<RolledRoom> ModifyUnknownMapPointRoomTypes(RunContext c, IReadOnlySet<RolledRoom> roomTypes) =>
        Fold(c, (IReadOnlySet<RolledRoom>)new HashSet<RolledRoom>(roomTypes), (l, set) => l.ModifyUnknownMapPointRoomTypes(c, set));

    /// <summary>Hook.ShouldAllowFreeTravel: whether any point of the next map row may be travelled to.</summary>
    public static bool ShouldAllowFreeTravel(RunContext c) => Any(c, l => l.ShouldAllowFreeTravel(c));

    // ---------- treasure ----------

    /// <summary>Hook.ShouldGenerateTreasure: false leaves the treasure room without a relic (and, in the room's own code, without its gold).</summary>
    public static bool ShouldGenerateTreasure(RunContext c) => All(c, l => l.ShouldGenerateTreasure(c));

    // ---------- the rest site ----------

    /// <summary>Hook.ShouldDisableRemainingRestSiteOptions: false keeps the other options after one was used.</summary>
    public static bool ShouldDisableRemainingRestSiteOptions(RunContext c) => All(c, l => l.ShouldDisableRemainingRestSiteOptions(c));

    /// <summary>Hook.ModifyRestSiteHealAmount: what resting heals, after every listener.</summary>
    public static decimal ModifyRestSiteHealAmount(RunContext c, decimal amount) => Fold(c, amount, (l, a) => l.ModifyRestSiteHealAmount(c, a));

    /// <summary>Hook.AfterRestSiteHeal: after the heal of a rest, real or mimicked.</summary>
    public static void AfterRestSiteHeal(RunContext c, bool isMimicked) => Each(c, l => l.AfterRestSiteHeal(c, isMimicked));

    /// <summary>Hook.ModifyExtraRestSiteHealText: the lines relics add to what the Rest option says it does.</summary>
    public static IReadOnlyList<string> ModifyExtraRestSiteHealText(RunContext c, IReadOnlyList<string> extraText) =>
        Fold(c, extraText, (l, text) => l.ModifyExtraRestSiteHealText(c, text));

    // ---------- rewards ----------

    /// <summary>Hook.AfterModifyingRewards: tells the listeners that changed a set of rewards, in listener order.</summary>
    public static void AfterModifyingRewards(RunContext c, IReadOnlyCollection<Listener> modifiers) =>
        EachOf(c, modifiers, l => l.AfterModifyingRewards(c), withFight: false);

    /// <summary>Hook.AfterModifyingCardRewardOptions: tells the listeners that changed the cards of a card reward, in listener order.</summary>
    public static void AfterModifyingCardRewardOptions(RunContext c, IReadOnlyCollection<Listener> modifiers) =>
        EachOf(c, modifiers, l => l.AfterModifyingCardRewardOptions(c), withFight: false);
}
