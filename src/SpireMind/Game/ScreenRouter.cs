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
            if (rm.IsInProgress && NRun.Instance?.GlobalUi is null) return "run loading";
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

        // Modals sit above everything (FTUE tips, error popups, the mod consent prompt).
        if (NModalContainer.Instance?.OpenModal is Node modal && GodotObject.IsInstanceValid(modal))
            return new ActiveScreen(ModalReader.Instance, modal);

        if (current is ICapstoneScreen)
        {
            why = "a deck view or the pause menu is open; close it to continue";
            return null; // a human opened it, let them close it.
        }

        if (!ctx.InRun)
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

        // VERIFY: a rewards screen in a treasure room, shop (Orrery, Cauldron) or event leaves the stack once its proceed is pressed; one that stays would hide the room under it
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
        // combat reader too.
        if (ctx.InCombat) // VERIFY: CombatManager.IsInProgress turns false once a fight is over, so an event's page after its fight reads as the event
            return new ActiveScreen(CombatReader.Instance, current as NCombatRoom ?? NRun.Instance?.CombatRoom);

        // Room layer. Prefer the concrete node the screen context reports; fall back to the
        // logical room type when the context is something we don't special-case (the merchant's
        // inventory, for one, reports itself rather than the room).
        // VERIFY: the merchant inventory is not an NOverlayStack screen (else the overlay switch above needs a case for it)
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
