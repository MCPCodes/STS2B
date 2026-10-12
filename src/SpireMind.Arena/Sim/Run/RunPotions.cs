using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>How an attempt to gain a potion ended (the game's PotionProcureResult).</summary>
public enum PotionProcureResult
{
    /// <summary>The potion is in the belt.</summary>
    Success,

    /// <summary>No slot was free (or the slot named was taken): nothing changed.</summary>
    TooFull,

    /// <summary>A hook kept the potion out: nothing changed.</summary>
    NotAllowed,
}

/// <summary>
/// Potions outside a fight, shared by every screen that offers them (the mod's PotionActions
/// with inCombat false, and the game's PotionCmd): the detailed <c>potions</c> block, the
/// action ids <c>potion:&lt;slot&gt;</c> and <c>discard_potion:&lt;slot&gt;</c>, using a potion,
/// throwing one away, and gaining one.
/// <para>
/// A screen writes <see cref="Describe"/> into its state, adds <see cref="Legal"/> behind its
/// own actions (the mod's rule: only beside at least one action of the screen itself, except
/// on rewards when a potion reward waits for a free slot), and hands every id to
/// <see cref="TryExecute"/> first.
/// </para>
/// <para>
/// Outside a fight a potion can be used when the game lets it be used at any time (Blood
/// Potion, Fruit Juice) and it needs no enemy; every held potion can be thrown away, also the
/// one that works by itself (Fairy in a Bottle). A potion's effect outside a fight is its one
/// effect, run on the player as it stands between rooms (<see cref="Run.OutOfCombat"/>).
/// </para>
/// </summary>
public static class RunPotions
{
    /// <summary>The mod's note on a potion reward that cannot be taken because the belt is full.</summary>
    public const string SlotsFullNote = "potion slots full: use or discard a potion to take it";

    private const string UsePrefix = "potion:";
    private const string DiscardPrefix = "discard_potion:";

    private static RunState Outside(Run run) =>
        run.Fight is null ? run.State : throw new InvalidOperationException("The potion actions of a run are for outside a fight; a fight has its own.");

    // ---------- what a screen shows and offers ----------

    /// <summary>The detailed <c>potions</c> block of a screen outside a fight (the mod's PotionActions.Describe): one entry per
    /// slot, null for an empty one; <c>usable</c> says whether the potion's use is offered now.</summary>
    public static JsonArray Describe(Run run)
    {
        var offered = CanAct(run);
        return run.PotionsJson((_, potion) => offered && Usable(potion));
    }

    /// <summary>
    /// The potion actions of a screen outside a fight, slot by slot, a slot's use before its
    /// discard (the mod's PotionActions.Legal). None at all for a dead player, and while an
    /// event holds the belt (<see cref="RewardsState.CanRemovePotions"/>).
    /// </summary>
    public static IReadOnlyList<LegalAction> Legal(Run run)
    {
        var actions = new List<LegalAction>();
        if (!CanAct(run)) return actions;
        var belt = run.State.Belt;
        for (var slot = 0; slot < belt.Count; slot++)
        {
            if (belt[slot] is not { } potion) continue;
            if (Usable(potion)) actions.Add(new LegalAction(string.Create(CultureInfo.InvariantCulture, $"{UsePrefix}{slot}"), $"Use {potion.Name}"));
            actions.Add(new LegalAction(string.Create(CultureInfo.InvariantCulture, $"{DiscardPrefix}{slot}"), $"Discard {potion.Name}"));
        }
        return actions;
    }

    /// <summary>
    /// Runs a potion action (the mod's PotionActions.TryExecute). False when the id is not a
    /// potion id at all: the screen then looks at it itself. True otherwise, with the result:
    /// done, or refused with the mod's wording when the action is not offered now.
    /// </summary>
    public static bool TryExecute(Run run, string actionId, out ActionResult result)
    {
        var discard = actionId.StartsWith(DiscardPrefix, StringComparison.Ordinal);
        if (!discard && !actionId.StartsWith(UsePrefix, StringComparison.Ordinal))
        {
            result = ActionResult.Fail($"'{actionId}' is not a potion action.");
            return false;
        }

        // The id is looked up among the actions as they stand, as the mod does: the slot and the potion are then current.
        if (!Legal(run).Any(action => action.Id == actionId))
        {
            result = ActionResult.Fail($"'{actionId}' is not available now.");
            return true;
        }
        var slot = int.Parse(actionId.AsSpan((discard ? DiscardPrefix : UsePrefix).Length), NumberStyles.None, CultureInfo.InvariantCulture);
        if (discard) Discard(run, slot);
        else Use(run, slot);
        result = ActionResult.Success;
        return true;
    }

    private static bool CanAct(Run run)
    {
        var state = Outside(run);
        return !state.IsDead && (state.FindPart<RewardsState>()?.CanRemovePotions ?? true);
    }

    /// <summary>
    /// Whether a potion's use is offered outside a fight (the mod's rules, which are the game's
    /// potion popup's): the game lets it be used at any time, and it is not aimed at an enemy or
    /// at another player. A potion for any player goes to the player himself.
    /// </summary>
    /// <exception cref="NotSupportedException">The potion has a use condition of its own, which is written for a fight.</exception>
    public static bool Usable(PotionDef potion)
    {
        if (potion.Usage != PotionUsage.AnyTime) return false;
        if (potion.Target is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy or TargetType.AnyAlly) return false;
        if (potion.CanUse is not null)
            throw new NotSupportedException($"{potion.ClassName} can be used at any time and has a use condition of its own, which the arena can only ask inside a fight.");
        return true;
    }

    // ---------- the commands ----------

    /// <summary>
    /// Gains a potion (PotionCmd.TryToProcure): unless a hook keeps it out, it goes into the slot
    /// named or the first free one, and the listeners hear of it. With no free slot nothing
    /// happens: the game never pushes a potion out.
    /// </summary>
    public static PotionProcureResult TryToProcure(Run run, PotionDef potion, int slot = -1)
    {
        var state = Outside(run);
        if (!RewardHooks.ShouldProcurePotion(run, potion)) return PotionProcureResult.NotAllowed;
        if (!state.Belt.Add(potion, slot)) return PotionProcureResult.TooFull;
        RunHooks.AfterPotionProcured(run.Context, potion);
        return PotionProcureResult.Success;
    }

    /// <summary>Throws the potion of a slot away (PotionCmd.Discard): it leaves the belt, and the listeners hear of it.</summary>
    public static void Discard(Run run, int slot)
    {
        var state = Outside(run);
        var potion = state.Belt.Remove(slot) ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Potion slot {slot} is empty."));
        RunHooks.AfterPotionDiscarded(run.Context, potion);
    }

    /// <summary>
    /// Uses the potion of a slot outside a fight (PotionModel.OnUseWrapper with no combat): it
    /// leaves the belt first; the listeners hear "before potion used"; its effect, on the player;
    /// "after potion used". The effect is the potion's own (<see cref="PotionDef.Use"/>), run on
    /// the player as it stands outside a fight; the listeners are the run's (deck cards that
    /// listen, relics, potions), heard through the fight's two potion hooks.
    /// </summary>
    /// <exception cref="NotSupportedException">The effect drew a random number from a fight's streams, which no fight owns here.</exception>
    public static void Use(Run run, int slot)
    {
        var state = Outside(run);
        if (state.Belt[slot] is null) throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Potion slot {slot} is empty."));
        run.OutOfCombat(f => f.UsePotionFromHook(slot, null));
    }
}
