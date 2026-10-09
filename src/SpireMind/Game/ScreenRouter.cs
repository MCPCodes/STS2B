namespace SpireMind.Game;

/// <summary>
/// Decides which screen is up and whether the game is in a state where reading it is
/// meaningful. Mirrors the game's own layering (docs/game-api.md section f): busy gates
/// first, then screens a human opened and modals, then the map, then the overlay stack, then
/// the room.
/// </summary>
internal static class ScreenRouter
{
    /// <summary>Last screen name we routed to, used to label busy snapshots.</summary>
    public static string LastScreenName { get; private set; } = "unknown";

    private static RunState? _lastRun;

    /// <summary>Overlay types we have no reader for, logged once each instead of on every poll.</summary>
    private static readonly HashSet<string> UnhandledLogged = new(StringComparer.Ordinal);

    public static ActiveScreen? Resolve(GameContext ctx, out GameSnapshot? busy)
    {
        busy = null;

        if (!ReferenceEquals(_lastRun, ctx.Run))
        {
            _lastRun = ctx.Run;
            Latches.ResetForNewRun();
        }

        if (BusyReason(ctx) is { } why)
        {
            busy = GameSnapshot.Busy(LastScreenName, why);
            return null;
        }

        var active = Route(ctx, out var unrouted);
        if (active is null)
        {
            busy = GameSnapshot.Busy("unknown", unrouted);
            return null;
        }

        LastScreenName = active.Value.Screen.Name;
        return active;
    }

    /// <summary>Step 0 and 1 of the screen detection recipe: not ready, transitioning, loading.</summary>
    private static string? BusyReason(GameContext ctx)
    {
        var game = NGame.Instance;
        if (game is null || (game.MainMenu is null && game.CurrentRunNode is null))
            return "game starting up";

        if (game.Transition?.InTransition == true)
            return "screen transition";

        var rm = RunManager.Instance;
        if (rm is not null)
        {
            if (rm.IsCleaningUp) return "run cleaning up";
            if (rm.IsInProgress && rm.NetService?.IsGameLoading == true) return "loading";
            // Only while the run scene exists: a run set up from the main menu (or left behind by
            // a failed Continue) counts as in progress with no NRun, and Route sends that to the menu.
            if (rm.IsInProgress && NRun.Instance is { GlobalUi: null }) return "run loading";
        }

        if (NMapScreen.Instance?.IsTraveling == true)
            return "travelling on the map";

        if (TimeoutOverlayShown())
            return "waiting on the network";

        return null;
    }

    private static ActiveScreen? Route(GameContext ctx, out string why)
    {
        why = "no recognised screen yet";
        object? current = null;
        try { current = ActiveScreenContext.Instance?.GetCurrentScreen(); }
        catch (Exception ex) { ModEntry.LogError($"GetCurrentScreen: {ex.Message}"); }

        // The feedback screen outranks modals and the inspect screens outrank everything below
        // them; a human opened all three, so let them close them.
        if (current is NSendFeedbackScreen or NInspectCardScreen or NInspectRelicScreen)
        {
            why = "an inspect or feedback screen is open; close it to continue";
            return null;
        }

        // Modals sit above everything else (FTUE tips, error popups, the mod consent prompt).
        if (NModalContainer.Instance?.OpenModal is Node modal && GodotObject.IsInstanceValid(modal))
            return new ActiveScreen(ModalReader.Instance, modal);

        if (current is ICapstoneScreen)
        {
            why = "a deck view or the pause menu is open; close it to continue";
            return null; // a human opened it, let them close it.
        }

        // The main menu wins over RunManager, as in GetCurrentScreen: a run being set up from it
        // already counts as in progress.
        if (!ctx.InRun || NGame.Instance?.MainMenu is not null)
        {
            // The run can stop counting as in progress while its game over screen is still up.
            if (NOverlayStack.Instance?.Peek() is NGameOverScreen over && GodotObject.IsInstanceValid(over))
                return new ActiveScreen(GameOverReader.Instance, over);
            return new ActiveScreen(MenuReader.Instance, current as Node);
        }

        // The map draws over terminal reward screens that stay on the overlay stack until the
        // room changes, so it wins when open.
        if (NMapScreen.Instance is { IsOpen: true } map)
            return new ActiveScreen(MapReader.Instance, map);

        // Only a fight's rewards screen is terminal and stays on the stack, under the map (checked
        // above), as does the Crystal Sphere after its proceed. Every other rewards screen
        // (treasure, shop relics like Orrery, events) removes itself on proceed or once emptied.
        if (NOverlayStack.Instance?.Peek() is Node top && GodotObject.IsInstanceValid(top))
        {
            IScreen? reader = top switch
            {
                NGameOverScreen => GameOverReader.Instance,
                NRewardsScreen => RewardsReader.Instance,
                NCardRewardSelectionScreen => CardRewardReader.Instance,
                NCrystalSphereScreen => CrystalSphereReader.Instance,
                NDeckCardSelectScreen or NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen
                    or NSimpleCardSelectScreen or NCombatPileCardSelectScreen
                    or NChooseACardSelectionScreen or NChooseABundleSelectionScreen => CardSelectReader.Instance,
                _ => null,
            };
            if (reader is not null)
                return new ActiveScreen(reader, top);

            var type = top.GetType().Name;
            if (UnhandledLogged.Add(type)) ModEntry.Log($"unhandled overlay {type}");
            why = $"a screen the bot can't read is open ({type})";
            return null;
        }

        // A fight comes first whatever the screen context reports: one an event starts runs
        // inside the event's room. Hand selection (the "hand_select" snapshot) lives in the
        // combat reader too. IsInProgress turns false when the fight ends, so an event page
        // shown again afterwards routes to the event.
        if (ctx.InCombat)
            return new ActiveScreen(CombatReader.Instance, current as NCombatRoom ?? NRun.Instance?.CombatRoom);

        // Room layer. Prefer the concrete node the screen context reports; fall back to the
        // logical room type when the context is something we don't special-case (the merchant's
        // inventory, a plain screen context rather than an overlay, reports itself while open).
        switch (current)
        {
            case NCombatRoom combatRoom:
                return new ActiveScreen(CombatReader.Instance, combatRoom);
            case NEventRoom eventRoom:
                return new ActiveScreen(EventReader.Instance, eventRoom);
            case NTreasureRoom treasure:
                return new ActiveScreen(TreasureReader.Instance, treasure);
            case NRestSiteRoom rest:
                return new ActiveScreen(RestSiteReader.Instance, rest);
            case NMerchantRoom shop:
                return new ActiveScreen(ShopReader.Instance, shop);
        }

        var roomType = ctx.Run?.CurrentRoom?.RoomType;
        return roomType switch
        {
            RoomType.Event => new ActiveScreen(EventReader.Instance, NRun.Instance?.EventRoom),
            RoomType.Treasure => new ActiveScreen(TreasureReader.Instance, NRun.Instance?.TreasureRoom),
            RoomType.RestSite => new ActiveScreen(RestSiteReader.Instance, NRestSiteRoom.Instance),
            RoomType.Shop => new ActiveScreen(ShopReader.Instance, NMerchantRoom.Instance),
            // Map room (act start), a fight's room after the fight, or between rooms with the map
            // closed: nothing to do yet.
            _ => NoRoom(roomType, out why),
        };
    }

    private static ActiveScreen? NoRoom(RoomType? roomType, out string why)
    {
        why = roomType is null or RoomType.Unassigned ? "between rooms" : $"nothing to do in this {roomType} room yet";
        return null;
    }

    /// <summary>The co-op "waiting for player" overlay: one game-level (lobby, loading) and one per run.</summary>
    private static bool TimeoutOverlayShown() =>
        NGame.Instance?.TimeoutOverlay?.IsShown == true || NRun.Instance?.GlobalUi?.TimeoutOverlay?.IsShown == true;
}
