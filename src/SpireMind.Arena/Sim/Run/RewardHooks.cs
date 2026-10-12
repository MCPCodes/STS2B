using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The hooks of the run whose arguments are rewards, card offers and their options: the
/// game's hooks of that name on its model base class (AbstractModel), one method each, with
/// the game's defaults. A relic opts in by implementing the interface and writes only the
/// methods it needs; so can a deck card's hook object and a potion's (the interface carries
/// <see cref="IListensInDeck"/>, so a card's hook class that implements it is heard as the deck
/// card; on a relic or a potion the marker says nothing).
/// <para>
/// Nothing calls these directly: every call goes through <see cref="RewardHooks"/>, which asks
/// the run's listeners in the game's order (<see cref="RunHooks"/>). Each hook is handed the
/// run's <see cref="RunContext"/>, as every hook of the run is; all of these are heard outside
/// a fight, so <c>c.Run</c> is there. In the game the hooks also carry the player they are
/// about; a run has one player, so that argument is gone and a relic's "is this my owner" test
/// with it.
/// </para>
/// <para>
/// The other hooks the rewards send are declared on <see cref="Listener"/> with the rest of the
/// run's hooks, and a relic overrides them there: <see cref="Listener.AfterModifyingRewards"/>,
/// <see cref="Listener.AfterModifyingCardRewardOptions"/>, <see cref="Listener.ShouldForcePotionReward"/>,
/// <see cref="Listener.AfterPotionProcured"/>, <see cref="Listener.AfterPotionDiscarded"/>, and
/// around a potion used outside a fight the fight's own <see cref="Listener.BeforePotionUsed"/>
/// and <see cref="Listener.AfterPotionUsed"/>.
/// </para>
/// </summary>
public interface IRewardHooks : IListensInDeck
{
    // ---------- a set of rewards ----------

    /// <summary>
    /// A set of rewards was made and filled: add to it or change it (Prayer Wheel adds a second
    /// card reward after a hallway fight, Lava Rock two relics after the first boss). Rewards
    /// added here are filled afterwards, in the order added. True when anything was changed; the
    /// listener is then told <see cref="Listener.AfterModifyingRewards"/>.
    /// </summary>
    /// <param name="room">The kind of room the set belongs to; null for rewards an event or a relic offers.</param>
    bool TryModifyRewards(RunContext c, List<Reward> rewards, RoomKind? room) => false;

    /// <summary>The same, asked of every listener after all of them had <see cref="TryModifyRewards"/>.</summary>
    bool TryModifyRewardsLate(RunContext c, List<Reward> rewards, RoomKind? room) => false;

    /// <summary>The rewards a rest gives besides its heal (Dream Catcher adds a card reward): add to the list. True when anything was added.</summary>
    /// <param name="isMimicked">The rest is one an event or a relic mimics, not the rest site's own.</param>
    bool TryModifyRestSiteHealRewards(RunContext c, List<Reward> rewards, bool isMimicked) => false;

    /// <summary>A reward was taken (not skipped, not bounced).</summary>
    void AfterRewardTaken(RunContext c, Reward reward) { }

    /// <summary>False holds the proceed button of a rewards screen back.</summary>
    bool ShouldProceedToNextMapPoint(RunContext c) => true;

    // ---------- the cards of an offer ----------

    /// <summary>
    /// Before each card of an offer is rolled: change where it is rolled from (Dingy Rug adds the
    /// colorless pool to a card reward). The options object belongs to the offer: a change made
    /// to it lasts for the cards after this one too.
    /// </summary>
    CardCreationOptions ModifyCardRewardCreationOptions(RunContext c, CardCreationOptions options) => options;

    /// <summary>The same, asked of every listener after all of them had <see cref="ModifyCardRewardCreationOptions"/>.</summary>
    CardCreationOptions ModifyCardRewardCreationOptionsLate(RunContext c, CardCreationOptions options) => options;

    /// <summary>
    /// The cards of an offer are rolled: add one or change them (Lasting Candy adds a Power).
    /// Change a card through <see cref="CardCreationResult.ModifyCard"/> with a copy; never
    /// change the rolled card itself. True when anything was changed; the listener is then told
    /// <see cref="Listener.AfterModifyingCardRewardOptions"/>.
    /// <para>
    /// A relic is asked this too, alone, the moment it is obtained while a card reward waits
    /// (<see cref="CardReward.OnRelicObtained"/>); what it is told then is
    /// <see cref="Listener.AfterModifyingRewards"/>, as in the game.
    /// </para>
    /// </summary>
    bool TryModifyCardRewardOptions(RunContext c, List<CardCreationResult> cards, CardCreationOptions options) => false;

    /// <summary>The same, asked after every listener had <see cref="TryModifyCardRewardOptions"/> (the Eggs, Lava Lamp, Silver Crucible, Silken Tress, Wing Charm).</summary>
    bool TryModifyCardRewardOptionsLate(RunContext c, List<CardCreationResult> cards, CardCreationOptions options) => false;

    /// <summary>The chance that a rolled reward card comes upgraded; the roll is at or below it.</summary>
    decimal ModifyCardRewardUpgradeOdds(RunContext c, CardInstance card, decimal odds) => odds;

    /// <summary>True lets the player take another card from a card reward after one was taken.</summary>
    bool ShouldAllowSelectingMoreCardRewards(RunContext c, CardReward reward) => false;

    /// <summary>
    /// The buttons of a card reward besides its cards, by the game's option ids ("Skip"): add to
    /// them. The arena builds Skip alone; anything added here is refused by name
    /// (<see cref="CardReward"/>), since Reroll and Sacrifice come from relics of act 2.
    /// </summary>
    bool TryModifyCardRewardAlternatives(RunContext c, CardReward reward, List<string> alternatives) => false;

    // ---------- the cards of a shop ----------

    /// <summary>The cards a shop slot is rolled from.</summary>
    IEnumerable<GameCard> ModifyMerchantCardPool(RunContext c, IEnumerable<GameCard> options) => options;

    /// <summary>The rarity rolled for a shop card.</summary>
    CardRarity ModifyMerchantCardRarity(RunContext c, CardRarity rarity) => rarity;

    /// <summary>A shop card was made (and again whenever its shelf entry is brought up to date): change it through
    /// <see cref="CardCreationResult.ModifyCard"/> (the Eggs upgrade it). The list holds the one card.</summary>
    void ModifyMerchantCardCreationResults(RunContext c, List<CardCreationResult> cards) { }

    // ---------- potions ----------

    /// <summary>False keeps a potion from being gained at all; a potion reward is then used up without giving anything.</summary>
    bool ShouldProcurePotion(RunContext c, PotionDef potion) => true;
}

/// <summary>
/// The named call sites of the rewards' hooks: one method per hook of the game's Hook class,
/// each asking the run's listeners in the game's order through <see cref="RunHooks"/> (the
/// deck cards that listen, the relics in pickup order, the potions in the belt). The rewards,
/// the card factory and the potion actions call these and nothing else; the shop and the rest
/// site call the ones that are theirs to send.
/// <para>
/// In v0.107.1 only relics (and run modifiers, which the arena leaves out) override any of
/// these hooks.
/// </para>
/// </summary>
public static class RewardHooks
{
    // ---------- a set of rewards ----------

    /// <summary>Hook.ModifyRewards: every listener's TryModifyRewards, then every listener's TryModifyRewardsLate. Returns
    /// who changed something, for <see cref="RunHooks.AfterModifyingRewards"/>.</summary>
    public static List<Listener> ModifyRewards(Run run, List<Reward> rewards, RoomKind? room)
    {
        var c = run.Context;
        var modifiers = RunHooks.Modifiers(c, l => l is IRewardHooks hooks && hooks.TryModifyRewards(c, rewards, room));
        modifiers.AddRange(RunHooks.Modifiers(c, l => l is IRewardHooks hooks && hooks.TryModifyRewardsLate(c, rewards, room)));
        return modifiers;
    }

    /// <summary>Hook.ModifyRestSiteHealRewards: for the rest site, which then offers the list as custom rewards. Returns who
    /// added something; the game's rest site does nothing with that.</summary>
    public static List<Listener> ModifyRestSiteHealRewards(Run run, List<Reward> rewards, bool isMimicked)
    {
        var c = run.Context;
        return RunHooks.Modifiers(c, l => l is IRewardHooks hooks && hooks.TryModifyRestSiteHealRewards(c, rewards, isMimicked));
    }

    /// <summary>Hook.AfterRewardTaken.</summary>
    public static void AfterRewardTaken(Run run, Reward reward)
    {
        var c = run.Context;
        RunHooks.Each(c, l => (l as IRewardHooks)?.AfterRewardTaken(c, reward));
    }

    /// <summary>Hook.ShouldProceedToNextMapPoint: the first no decides.</summary>
    public static bool ShouldProceedToNextMapPoint(Run run)
    {
        var c = run.Context;
        return RunHooks.All(c, l => l is not IRewardHooks hooks || hooks.ShouldProceedToNextMapPoint(c));
    }

    // ---------- the cards of an offer ----------

    /// <summary>Hook.ModifyCardRewardCreationOptions: every listener, then every listener's Late form.</summary>
    public static CardCreationOptions ModifyCardRewardCreationOptions(Run run, CardCreationOptions options)
    {
        var c = run.Context;
        options = RunHooks.Fold(c, options, (l, o) => l is IRewardHooks hooks ? hooks.ModifyCardRewardCreationOptions(c, o) : o);
        return RunHooks.Fold(c, options, (l, o) => l is IRewardHooks hooks ? hooks.ModifyCardRewardCreationOptionsLate(c, o) : o);
    }

    /// <summary>Hook.TryModifyCardRewardOptions: every listener, then every listener's Late form. Returns who changed
    /// something, for <see cref="RunHooks.AfterModifyingCardRewardOptions"/>; a listener that did in both passes is in
    /// the list twice, as in the game, and still told once.</summary>
    public static List<Listener> ModifyCardRewardOptions(Run run, List<CardCreationResult> cards, CardCreationOptions options)
    {
        var c = run.Context;
        var modifiers = RunHooks.Modifiers(c, l => l is IRewardHooks hooks && hooks.TryModifyCardRewardOptions(c, cards, options));
        modifiers.AddRange(RunHooks.Modifiers(c, l => l is IRewardHooks hooks && hooks.TryModifyCardRewardOptionsLate(c, cards, options)));
        return modifiers;
    }

    /// <summary>Hook.ModifyCardRewardUpgradeOdds.</summary>
    public static decimal ModifyCardRewardUpgradeOdds(Run run, CardInstance card, decimal odds)
    {
        var c = run.Context;
        return RunHooks.Fold(c, odds, (l, o) => l is IRewardHooks hooks ? hooks.ModifyCardRewardUpgradeOdds(c, card, o) : o);
    }

    /// <summary>Hook.ShouldAllowSelectingMoreCardRewards: the first yes decides.</summary>
    public static bool ShouldAllowSelectingMoreCardRewards(Run run, CardReward reward)
    {
        var c = run.Context;
        return RunHooks.Any(c, l => l is IRewardHooks hooks && hooks.ShouldAllowSelectingMoreCardRewards(c, reward));
    }

    /// <summary>Hook.ModifyCardRewardAlternatives.</summary>
    public static void ModifyCardRewardAlternatives(Run run, CardReward reward, List<string> alternatives)
    {
        var c = run.Context;
        RunHooks.Each(c, l => (l as IRewardHooks)?.TryModifyCardRewardAlternatives(c, reward, alternatives));
    }

    // ---------- the cards of a shop ----------

    /// <summary>Hook.ModifyMerchantCardPool.</summary>
    public static IEnumerable<GameCard> ModifyMerchantCardPool(Run run, IEnumerable<GameCard> options)
    {
        var c = run.Context;
        return RunHooks.Fold(c, options, (l, o) => l is IRewardHooks hooks ? hooks.ModifyMerchantCardPool(c, o) : o);
    }

    /// <summary>Hook.ModifyMerchantCardRarity.</summary>
    public static CardRarity ModifyMerchantCardRarity(Run run, CardRarity rarity)
    {
        var c = run.Context;
        return RunHooks.Fold(c, rarity, (l, r) => l is IRewardHooks hooks ? hooks.ModifyMerchantCardRarity(c, r) : r);
    }

    /// <summary>Hook.ModifyMerchantCardCreationResults: the shop sends it for each card it has made, and again when it
    /// brings a shelf entry up to date.</summary>
    public static void ModifyMerchantCardCreationResults(Run run, List<CardCreationResult> cards)
    {
        var c = run.Context;
        RunHooks.Each(c, l => (l as IRewardHooks)?.ModifyMerchantCardCreationResults(c, cards));
    }

    // ---------- potions ----------

    /// <summary>Hook.ShouldProcurePotion: the first no decides.</summary>
    public static bool ShouldProcurePotion(Run run, PotionDef potion)
    {
        var c = run.Context;
        return RunHooks.All(c, l => l is not IRewardHooks hooks || hooks.ShouldProcurePotion(c, potion));
    }
}

/// <summary>What the rewards keep for the whole run: the order card rewards were made in, and whether potions may be
/// used or thrown away outside a fight right now.</summary>
public sealed class RewardsState : RunPart
{
    /// <summary>How many card rewards the run has made; the next one's <see cref="CardReward.Sequence"/>.</summary>
    public int CardRewardsMade { get; internal set; }

    /// <summary>
    /// False while an event holds the belt (the game's Player.CanRemovePotions: The Future of
    /// Potions sets it while its potions are on the table): no potion can be used or thrown away
    /// outside a fight then.
    /// </summary>
    public bool CanRemovePotions { get; set; } = true;

    public override RunPart Clone(CloneMap map) => new RewardsState { CardRewardsMade = CardRewardsMade, CanRemovePotions = CanRemovePotions };

    public override JsonNode Dump() => new JsonObject { ["card_rewards_made"] = CardRewardsMade, ["can_remove_potions"] = CanRemovePotions };
}
