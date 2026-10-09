using System.Text.Json.Nodes;
// The Describe method below hides the Describe class inside this type.
using Describer = SpireMind.Game.Describe;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// Using and discarding potions, shared by every screen that offers them: combat passes
/// inCombat=true, map, rewards, shop and the like pass false. Ids are "potion:&lt;slot&gt;",
/// "potion:&lt;slot&gt;&gt;e&lt;j&gt;" or "&gt;p&lt;j&gt;" (j is the "i" of an enemy or player
/// as the combat snapshot lists them) and "discard_potion:&lt;slot&gt;".
///
/// Uses go through PotionModel.EnqueueManualUse and discards through a
/// DiscardPotionGameAction, both on the synced action queue for the local player. Neither
/// validates anything, so the gates from docs/game-api.md section c are checked here.
/// </summary>
internal static class PotionActions
{
    private static readonly TimeSpan UseTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DiscardTimeout = TimeSpan.FromSeconds(4);

    /// <summary>Potions whose read already failed once, so a broken one doesn't log on every poll.</summary>
    private static readonly HashSet<string> Unreadable = new(StringComparer.Ordinal);

    private sealed record Option(string Id, string Label, int Slot, PotionModel Potion, bool IsDiscard, Creature? Target);

    /// <summary>Potion slots as JSON: [{"slot":0,"name":..,"text":..,"target":..,"usable":true}, null, ...].</summary>
    public static JsonArray Describe(GameContext ctx, bool inCombat)
    {
        var slots = new JsonArray();
        if (ctx.Me is not { } me) return slots;

        var usable = Options(ctx, inCombat).Where(o => !o.IsDiscard).Select(o => o.Slot).ToHashSet();
        for (var slot = 0; slot < me.MaxPotionCount; slot++)
        {
            var potion = me.GetPotionAtSlotIndex(slot);
            if (potion is null)
            {
                slots.Add((JsonNode?)null);
                continue;
            }
            var json = Describer.Lead("slot", slot, Describer.Potion(potion));
            json["usable"] = usable.Contains(slot);
            slots.Add(json);
        }
        return slots;
    }

    /// <summary>
    /// Legal potion actions right now. Out of combat only AnyTime potions that need no enemy,
    /// plus discards. In combat everything waits for the same may-act gate as cards.
    /// </summary>
    public static IEnumerable<LegalAction> Legal(GameContext ctx, bool inCombat) =>
        Options(ctx, inCombat).Select(o => new LegalAction(o.Id, o.Label)).ToList();

    /// <summary>Handles ids starting with "potion:" or "discard_potion:"; returns false for any other id.</summary>
    public static bool TryExecute(GameContext ctx, string actionId, bool inCombat, out ActionResult result)
    {
        if (!actionId.StartsWith("potion:", StringComparison.Ordinal) && !actionId.StartsWith("discard_potion:", StringComparison.Ordinal))
        {
            result = ActionResult.Fail($"'{actionId}' is not a potion action.");
            return false;
        }

        // Re-derive the options from the live game so the slot, potion and target are current.
        var option = Options(ctx, inCombat).FirstOrDefault(o => o.Id == actionId);
        result = option is null
            ? ActionResult.Fail($"'{actionId}' is not available now.")
            : option.IsDiscard ? Discard(ctx, option) : Use(ctx, option);
        return true;
    }

    private static List<Option> Options(GameContext ctx, bool inCombat)
    {
        var options = new List<Option>();
        if (ctx.Me is not { } me || !me.Creature.IsAlive || !me.CanRemovePotions) return options;
        if (ActionTracker.Pending is not null) return options;

        // Mid-fight, potions wait for the same gate as cards, whichever screen is asking.
        if (ctx.InCombat && !CombatReader.MayAct(ctx, out _)) return options;

        var combat = inCombat && ctx.InCombat ? ctx.Combat : null;
        var enemies = combat is null ? new List<Creature>() : CombatReader.EnemyRoster(combat);
        var players = combat?.Players.ToList() ?? new List<Player>();

        for (var slot = 0; slot < me.MaxPotionCount; slot++)
        {
            var potion = me.GetPotionAtSlotIndex(slot);
            if (potion is null || potion.IsQueued) continue;

            var name = Describer.Text(potion.Title);
            try
            {
                AddUses(options, slot, potion, name, me, combat, enemies, players);
            }
            catch (Exception ex)
            {
                // One odd potion must not hide the others or the discard option.
                if (Unreadable.Add(name)) ModEntry.LogError($"reading potion {name}: {ex.Message}");
            }
            options.Add(new Option($"discard_potion:{slot}", $"Discard {name}", slot, potion, IsDiscard: true, Target: null));
        }
        return options;
    }

    private static void AddUses(List<Option> options, int slot, PotionModel potion, string name, Player me, ICombatState? combat, List<Creature> enemies, List<Player> players)
    {
        // Same rules as the potion popup's use button. None is never used by a real potion.
        if (potion.Usage is PotionUsage.Automatic or PotionUsage.None) return;
        if (potion.Usage == PotionUsage.CombatOnly && combat is null) return;
        if (!potion.PassesCustomUsabilityCheck) return;

        var id = $"potion:{slot}";
        // Read at decision time: FoulPotion is TargetedNoCreature (the merchant) out of combat and AllEnemies in it.
        switch (potion.TargetType)
        {
            case TargetType.AnyEnemy:
                if (combat is null) return;
                for (var j = 0; j < enemies.Count; j++)
                {
                    var enemy = enemies[j];
                    if (CombatReader.IsTargetableEnemy(combat, enemy) && potion.IsValidTarget(enemy))
                        options.Add(new Option($"{id}>e{j}", $"Use {name} on {CombatReader.EnemyLabel(enemies, j)}", slot, potion, false, enemy));
                }
                return;

            case TargetType.AllEnemies or TargetType.RandomEnemy:
                if (combat is null) return;
                break;

            case TargetType.AnyPlayer when combat is not null && potion.CanThrowAtAlly():
                for (var j = 0; j < players.Count; j++)
                {
                    var target = players[j].Creature;
                    if (!target.IsAlive || !potion.IsValidTarget(target)) continue;
                    var who = LocalContext.IsMe(players[j]) ? "yourself" : CombatReader.PlayerLabel(players, j);
                    options.Add(new Option($"{id}>p{j}", $"Use {name} on {who}", slot, potion, false, target));
                }
                return;

            case TargetType.AnyPlayer when combat is null && CanThrow(potion):
                // CanThrowAtAlly is only true mid-fight; a non-combat screen asking then
                // indexes no players, so it goes to us.
                options.Add(new Option(id, $"Use {name}", slot, potion, false, me.Creature));
                return;
        }

        // No target: EnqueueManualUse swaps in our own creature when that is valid (Self, AnyPlayer
        // without a throw); AllEnemies, RandomEnemy and TargetedNoCreature take none. Anything
        // else (no potion is AnyAlly in this build) would only be cancelled when it runs.
        if (potion.IsValidTarget(null) || potion.IsValidTarget(me.Creature))
            options.Add(new Option(id, $"Use {name}", slot, potion, false, null));
    }

    /// <summary>CanThrowAtAlly out of combat; if it can't answer there, fall back to the null target.</summary>
    private static bool CanThrow(PotionModel potion)
    {
        try { return potion.CanThrowAtAlly(); }
        catch { return false; }
    }

    private static ActionResult Use(GameContext ctx, Option o)
    {
        var me = ctx.Me!;
        var potion = o.Potion;
        var slot = o.Slot;
        var name = Describer.Text(potion.Title);
        var fight = ctx.InCombat;
        var overlayBefore = Nodes.TopOverlay();

        potion.EnqueueManualUse(o.Target);

        ActionTracker.Begin(name, _ =>
        {
            if (fight && CombatReader.FightOver()) return true;
            // Some potions open a choice (pick a card, discard from hand); that is the next decision.
            if (CombatReader.HandSelectOpen() || !ReferenceEquals(Nodes.TopOverlay(), overlayBefore)) return true;
            var resolved = !ReferenceEquals(me.GetPotionAtSlotIndex(slot), potion) || !potion.IsQueued;
            return resolved && ActionTracker.GameLooksIdle();
        }, UseTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Discard(GameContext ctx, Option o)
    {
        if (RunManager.Instance?.ActionQueueSynchronizer is not { } sync)
            return ActionResult.Fail("The action queue is not ready.");

        var me = ctx.Me!;
        var potion = o.Potion;
        var slot = o.Slot;

        // The game action wants the real combat flag, whichever screen asked.
        sync.RequestEnqueue(new DiscardPotionGameAction(me, (uint)slot, ctx.InCombat));

        var fight = ctx.InCombat;
        ActionTracker.Begin($"discarding {Describer.Text(potion.Title)}",
            _ => (fight && CombatReader.FightOver()) || (!ReferenceEquals(me.GetPotionAtSlotIndex(slot), potion) && ActionTracker.GameLooksIdle()),
            DiscardTimeout);
        return ActionResult.Success;
    }
}
