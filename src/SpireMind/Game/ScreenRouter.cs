namespace SpireMind.Game;

/// <summary>
/// Decides which screen is up and whether the game is in a state where reading it is
/// meaningful. Mirrors the game's own layering (docs/game-api.md section f): busy gates
/// first, then modals, then the map, then the overlay stack, then the room.
/// </summary>
internal static class ScreenRouter
{
    /// <summary>Last screen name we routed to, used to label busy snapshots.</summary>
    public static string LastScreenName { get; private set; } = "unknown";

    private static RunState? _lastRun;

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

        var active = Route(ctx);
        if (active is null)
        {
            busy = GameSnapshot.Busy("unknown", "no recognised screen yet");
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
            if (rm.IsInProgress && NRun.Instance?.GlobalUi is null) return "run loading";
        }

        if (NMapScreen.Instance?.IsTraveling == true)
            return "travelling on the map";

        if (TimeoutOverlayShown())
            return "waiting on the network";

        return null;
    }

    private static ActiveScreen? Route(GameContext ctx)
    {
        object? current = null;
        try { current = ActiveScreenContext.Instance?.GetCurrentScreen(); }
        catch (Exception ex) { ModEntry.LogError($"GetCurrentScreen: {ex.Message}"); }

        // Modals sit above everything (FTUE tips, error popups, the mod consent prompt).
        if (NModalContainer.Instance?.OpenModal is Node modal && GodotObject.IsInstanceValid(modal))
            return new ActiveScreen(ModalReader.Instance, modal);

        if (current is ICapstoneScreen)
            return null; // deck view or pause menu: a human opened it, let them close it.

        if (!ctx.InRun)
            return new ActiveScreen(MenuReader.Instance, current as Node);

        // The map draws over terminal reward screens that stay on the overlay stack until the
        // room changes, so it wins when open.
        if (NMapScreen.Instance is { IsOpen: true } map)
            return new ActiveScreen(MapReader.Instance, map);

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

            ModEntry.Log($"unhandled overlay {top.GetType().Name}");
            return null;
        }

        // Room layer. Prefer the concrete node the screen context reports; fall back to the
        // logical room type when the context is something we don't special-case.
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
            RoomType.Monster or RoomType.Elite or RoomType.Boss when ctx.InCombat
                => new ActiveScreen(CombatReader.Instance, NRun.Instance?.CombatRoom),
            RoomType.Event => new ActiveScreen(EventReader.Instance, NRun.Instance?.EventRoom),
            RoomType.Treasure => new ActiveScreen(TreasureReader.Instance, NRun.Instance?.TreasureRoom),
            RoomType.RestSite => new ActiveScreen(RestSiteReader.Instance, NRestSiteRoom.Instance),
            RoomType.Shop => new ActiveScreen(ShopReader.Instance, NMerchantRoom.Instance),
            // Map room (act start) or between rooms with the map closed: nothing to do yet.
            _ => null,
        };
    }

    private static bool TimeoutOverlayShown()
    {
        // The network timeout overlay has no static accessor in the notes; look for it by type
        // name among the run's global UI children (cheap: this runs on every poll) so a rename
        // only disables this check.
        if (NRun.Instance?.GlobalUi is not Node ui) return false;
        foreach (var child in ui.GetChildren())
        {
            if (child is CanvasItem ci && ci.GetType().Name.Contains("TimeoutOverlay", StringComparison.Ordinal) && ci.IsVisibleInTree())
                return true;
        }
        return false;
    }
}
