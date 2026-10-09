using System.Text.Json.Nodes;

// Same namespace as ScreenRouter, which refers to the readers unqualified.
namespace SpireMind.Game;

/// <summary>
/// The merchant (NMerchantRoom): what is for sale, and buying it. The shop has no action
/// queue (docs/game-api.md section e): the inventory opens with room.OpenInventory(), a
/// purchase is the entry's own OnTryPurchaseWrapper, whose Task we never block on, and leaving
/// is the inventory's %BackButton and then the room's proceed button. Each player shops on
/// their own, so co-op needs no vote until the map.
///
/// Actions: "open", "close", "proceed" (leave the shop), "buy:card&lt;n&gt;", "buy:colorless&lt;n&gt;",
/// "buy:relic&lt;n&gt;", "buy:potion&lt;n&gt;" (n as listed in state.stock), "buy:remove",
/// "dismiss" for a tutorial tip drawn in the room rather than as a modal, and the
/// out-of-combat potion actions. Purchases are offered only while the inventory is open, as
/// for a human; the stock is listed either way so the model can leave without looking. Card
/// removal and the Orrery/Cauldron relics put their own screens over the shop while the
/// purchase is still pending; the router hands those to their readers and this one reports
/// "buying ..." until the purchase settles. The merchant's tutorial (NMerchantFtue) is a modal
/// the first proceed on a profile shows instead of leaving, unless the inventory was opened first.
/// </summary>
internal sealed class ShopReader : IScreen
{
    public static readonly ShopReader Instance = new();

    public string Name => "shop";

    /// <summary>The room fades in on arrival; give its buttons a moment before offering anything.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan LeaveTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan TipTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long the tracker holds snapshots after a purchase; the pending Task keeps the shop waiting after that.</summary>
    private static readonly TimeSpan PurchaseTimeout = TimeSpan.FromSeconds(8);

    /// <summary>
    /// A purchase still pending after this long with the shop itself on screen (nothing over it)
    /// is taken to be stuck, and the shop is offered again rather than waiting forever.
    /// </summary>
    private static readonly TimeSpan PurchaseStall = TimeSpan.FromSeconds(15);

    /// <summary>A gap between shop captures longer than this means another screen was up in between, or nobody polled.</summary>
    private static readonly TimeSpan CaptureGap = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Tutorial (FTUE) nodes are recognised by namespace, so no particular tip class has to be named.
    /// Every FTUE in v0.107.1, NMerchantFtue included, goes to NModalContainer and so to the modal
    /// reader; this only catches one drawn inside the room.
    /// </summary>
    private const string FtueNamespace = "MegaCrit.Sts2.Core.Nodes.Ftue";

    private ShopReader() { }

    private enum ItemKind { Card, Colorless, Relic, Potion, Removal }

    /// <summary>One stocked shop entry as read just now. <see cref="Buy"/> starts the game's purchase on the live entry.</summary>
    private sealed record Item(ItemKind Kind, int N, string Name, int Price, bool Affordable, bool OnSale, JsonObject Json, Func<Task<bool>> Buy)
    {
        public string Id => Kind switch
        {
            ItemKind.Card => $"buy:card{N}",
            ItemKind.Colorless => $"buy:colorless{N}",
            ItemKind.Relic => $"buy:relic{N}",
            ItemKind.Potion => $"buy:potion{N}",
            _ => "buy:remove",
        };

        public string What => Kind == ItemKind.Removal ? "a card removal" : Name;

        public string Label => Kind switch
        {
            ItemKind.Card => $"Buy {Name} for {Price} gold{(OnSale ? " (on sale)" : "")}",
            ItemKind.Colorless => $"Buy colorless {Name} for {Price} gold",
            ItemKind.Relic => $"Buy relic {Name} for {Price} gold",
            ItemKind.Potion => $"Buy potion {Name} for {Price} gold",
            _ => $"Pay {Price} gold to remove a card from your deck",
        };
    }

    /// <summary>
    /// The inventory screen node, whether it is open, and the stocked entries. StockKnown is false
    /// while the inventory node has no stock model yet (Items is then empty).
    /// </summary>
    private sealed record ShopView(NMerchantInventory Screen, bool Open, bool StockKnown, List<Item> Items);

    // ---------- per-room memory (reset when a different merchant room shows up) ----------

    private static ulong _roomId;
    private static DateTime _firstSeen;

    /// <summary>The purchase in flight. Never awaited: polled for completion on later calls.</summary>
    private static Task<bool>? _purchase;
    private static string _purchaseWhat = "";
    private static DateTime _purchaseQuietSince;
    private static DateTime _purchaseLastSeen;

    /// <summary>What the last purchase was, when the game said no or it was cancelled; cleared by the next shop action.</summary>
    private static string? _failedNote;

    /// <summary>We called OpenInventory and have not seen the inventory open since (it opens at once, so only a call that did nothing).</summary>
    private static bool _openPending;

    /// <summary>Our proceed click opened the map (false when the merchant's tutorial took it).</summary>
    private static bool _left;

    /// <summary>Tip buttons we clicked; one still up after the click window didn't close its tip and stops blocking the shop.</summary>
    private static readonly HashSet<ulong> TipsTried = new();

    /// <summary>Entries whose read already failed once in this room, so a broken one doesn't log on every poll.</summary>
    private static readonly HashSet<string> Unreadable = new(StringComparer.Ordinal);

    private static void Track(NMerchantRoom room)
    {
        var id = room.GetInstanceId();
        if (id == _roomId) return;
        _roomId = id;
        _firstSeen = DateTime.UtcNow;
        _purchase = null;
        _purchaseWhat = "";
        _failedNote = null;
        _openPending = false;
        _left = false;
        TipsTried.Clear();
        Unreadable.Clear();
    }

    /// <summary>Collect a finished purchase. Its result only matters when the game said no.</summary>
    private static void SettlePurchase()
    {
        if (_purchase is not { IsCompleted: true } task) return;
        _purchase = null;

        // Completed, so reading the result doesn't block.
        if (task.IsCompletedSuccessfully && task.Result) return;

        if (task.IsFaulted)
            ModEntry.LogError($"buying {_purchaseWhat} failed: {task.Exception?.GetBaseException().Message}");
        _failedNote = _purchaseWhat;
    }

    // ---------- IScreen ----------

    public GameSnapshot Capture(GameContext ctx, Node? node)
    {
        if (Room(node) is not { } room)
            return GameSnapshot.Busy(Name, "shop closing");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return GameSnapshot.Busy(Name, "run loading");

        Track(room);
        SettlePurchase();
        if (Read(room) is not { } shop)
            return GameSnapshot.Busy(Name, "shop loading");
        if (shop.Open) _openPending = false;

        var state = BuildState(ctx, rs, me, shop);
        var actions = new List<LegalAction>();
        var waiting = PurchaseOrArrival();
        if (waiting is null && ParasolBusy(me))
            waiting = "Lord's Parasol is buying out the shop";
        if (waiting is null && TipButton(room) is { } tip)
        {
            // A tip drawn in the room sits over the shop and would swallow the next input.
            if (Clicks.RecentlyClicked(tip))
                waiting = "dismissing the tutorial tip";
            else
                actions.Add(new LegalAction("dismiss", "Dismiss the tutorial tip"));
        }
        else if (waiting is null && (waiting = Transition(room, shop)) is null)
        {
            actions.AddRange(ShopActions(room, me, shop));
            // Potion actions alone are not a shop decision; they ride along with the real ones.
            if (actions.Count > 0)
                actions.AddRange(PotionActions.Legal(ctx, false));
            else if (_left)
                waiting = ctx.IsMultiplayer ? "left the shop; waiting for teammates" : "leaving the shop";
            else
                waiting = shop.Open ? "shop inventory opening" : "shop not ready";
        }

        if (waiting is not null)
            return new GameSnapshot { Screen = Name, AwaitingDecision = false, Waiting = waiting, State = state };

        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    public ActionResult Execute(GameContext ctx, Node? node, string actionId)
    {
        if (PotionActions.TryExecute(ctx, actionId, false, out var potionResult))
            return potionResult;

        if (Room(node) is not { } room)
            return ActionResult.Fail("The shop is gone.");
        if (ctx.Run is not { } rs || ctx.Me is not { } me)
            return ActionResult.Fail("No run in progress.");

        Track(room);
        SettlePurchase();
        if (_purchase is not null)
            return ActionResult.Fail($"Still buying {_purchaseWhat}.");
        if (ParasolBusy(me))
            return ActionResult.Fail("Lord's Parasol is still buying out the shop.");
        if (Read(room) is not { } shop)
            return ActionResult.Fail("The shop inventory is not ready.");

        var result = actionId switch
        {
            "dismiss" => DismissTip(room),
            "open" => Open(room, me, shop),
            "close" => Close(room, shop),
            "proceed" => Leave(rs, room, shop),
            _ when actionId.StartsWith("buy:", StringComparison.Ordinal) => Buy(room, me, shop, actionId),
            _ => ActionResult.Fail($"Unknown shop action '{actionId}'."),
        };
        if (result.Ok) _failedNote = null;
        return result;
    }

    // ---------- acting ----------

    private static ActionResult Buy(NMerchantRoom room, Player me, ShopView shop, string actionId)
    {
        if (!shop.Open)
            return ActionResult.Fail("Open the shop inventory first.");
        var item = shop.Items.FirstOrDefault(i => i.Id == actionId);
        if (item is null)
            return ActionResult.Fail($"'{actionId}' is not in stock.");
        if (!item.Affordable)
            return ActionResult.Fail($"Not enough gold for {item.What} ({item.Price}).");
        if (item.Kind == ItemKind.Potion && !me.HasOpenPotionSlots)
            return ActionResult.Fail("Potion slots are full; use or discard a potion first.");

        Task<bool> task;
        try
        {
            task = item.Buy();
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"buying {item.What}: {ex}");
            return ActionResult.Fail($"The game refused to sell {item.What}: {ex.Message}");
        }

        var now = DateTime.UtcNow;
        _purchase = task;
        _purchaseWhat = item.What;
        _purchaseQuietSince = now;
        _purchaseLastSeen = now;

        // A removal's card choice or the Orrery/Cauldron rewards cover the shop before the Task
        // completes; those screens are the next decision, so stop holding snapshots then.
        ActionTracker.Begin(
            $"buying {item.What}",
            _ => task.IsCompleted || !Nodes.Alive(room) || Nodes.TopOverlay() is not null || Nodes.ModalOpen(),
            PurchaseTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Open(NMerchantRoom room, Player me, ShopView shop)
    {
        if (shop.Open)
            return ActionResult.Fail("The shop inventory is already open.");
        // OpenInventory doesn't check this; the merchant button a human clicks does.
        if (me.Creature.IsDead)
            return ActionResult.Fail("The merchant won't trade with a dead player.");

        var click = Clicks.Once(room, () =>
        {
            _openPending = true;
            room.OpenInventory();
        }, "the shop");
        if (!click.Ok)
            return click;

        var screen = shop.Screen;
        ActionTracker.Begin(
            "opening the shop",
            _ => !Nodes.Alive(room) || !Nodes.Alive(screen) || screen.IsOpen || Nodes.ModalOpen() || TipButton(room) is not null,
            OpenTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Close(NMerchantRoom room, ShopView shop)
    {
        if (!shop.Open)
            return ActionResult.Fail("The shop inventory is not open.");
        var back = BackButton(shop.Screen);
        if (back is null || !Nodes.IsShown(back))
            return ActionResult.Fail("There is no back button on the shop.");
        if (!back.IsEnabled)
            return ActionResult.Fail("The shop's back button is not enabled yet.");

        var click = Clicks.Once(back, () => back.ForceClick(), "the shop's back button");
        if (!click.Ok)
            return click;

        var screen = shop.Screen;
        ActionTracker.Begin(
            "closing the shop",
            _ => !Nodes.Alive(room) || !Nodes.Alive(screen) || !screen.IsOpen || Nodes.ModalOpen(),
            CloseTimeout);
        return ActionResult.Success;
    }

    private static ActionResult Leave(RunState rs, NMerchantRoom room, ShopView shop)
    {
        if (shop.Open)
            return ActionResult.Fail("Close the shop inventory first.");
        // ForceClick skips IsEnabled, and even a disabled proceed opens the map.
        var proceed = room.ProceedButton;
        if (proceed is null || !Nodes.IsShown(proceed))
            return ActionResult.Fail("There is no proceed button.");
        if (!proceed.IsEnabled)
            return ActionResult.Fail("The proceed button is not enabled yet.");

        // The first proceed on a profile only shows the merchant's tutorial modal and stays in the
        // room (opening the inventory also counts as having seen it); proceed is offered again after.
        var leaves = SaveManager.Instance.SeenFtue(NMerchantFtue.id);
        var roomModel = rs.CurrentRoom;
        var click = Clicks.Once(proceed, () =>
        {
            _left = leaves;
            proceed.ForceClick();
        }, "the proceed button");
        if (!click.Ok)
            return click;

        ActionTracker.Begin(
            "leaving the shop",
            c => !Nodes.Alive(room) || NMapScreen.Instance?.IsOpen == true || Nodes.ModalOpen() || !ReferenceEquals(c.Run?.CurrentRoom, roomModel),
            LeaveTimeout);
        return ActionResult.Success;
    }

    private static ActionResult DismissTip(NMerchantRoom room)
    {
        var button = TipButton(room);
        if (button is null)
            return ActionResult.Fail("No tip is showing.");

        var id = button.GetInstanceId();
        var click = Clicks.Once(button, () =>
        {
            TipsTried.Add(id);
            button.ForceClick();
        }, "the tip's button");
        if (!click.Ok)
            return click;

        ActionTracker.Begin("dismissing the tip", _ => !Nodes.Alive(button) || !Nodes.IsShown(button), TipTimeout);
        return ActionResult.Success;
    }

    // ---------- reading ----------

    /// <summary>A purchase still settling, or the room just arrived.</summary>
    private static string? PurchaseOrArrival()
    {
        var now = DateTime.UtcNow;
        if (_purchase is not null)
        {
            // Only time with the shop itself on screen counts: removal and the Orrery/Cauldron
            // rewards cover it for as long as the model takes to choose.
            if (now - _purchaseLastSeen > CaptureGap) _purchaseQuietSince = now;
            _purchaseLastSeen = now;
            if (now - _purchaseQuietSince < PurchaseStall)
                return $"buying {_purchaseWhat}";

            ModEntry.Log($"buying {_purchaseWhat} still pending with the shop on screen; offering the shop again");
            _purchase = null;
        }

        return now - _firstSeen < Settle ? "entering the shop" : null;
    }

    /// <summary>The inventory sliding open or shut after one of our clicks, or a proceed the merchant's tutorial took.</summary>
    private static string? Transition(NMerchantRoom room, ShopView shop)
    {
        if (!shop.Open && _openPending && Clicks.RecentlyClicked(room))
            return "opening the shop";
        if (shop.Open && !shop.StockKnown && Clicks.RecentlyClicked(room))
            return "shop inventory loading";
        if (shop.Open && BackButton(shop.Screen) is { } back && Clicks.RecentlyClicked(back))
            return "closing the shop";
        // Proceed comes back once the click window passes, rather than leaving only "open" on offer.
        if (!shop.Open && !_left && room.ProceedButton is { } proceed && Clicks.RecentlyClicked(proceed))
            return "the merchant's tutorial took the proceed click";
        return null;
    }

    private static List<LegalAction> ShopActions(NMerchantRoom room, Player me, ShopView shop)
    {
        var actions = new List<LegalAction>();
        if (shop.Open)
        {
            foreach (var item in shop.Items)
                if (item.Affordable && (item.Kind != ItemKind.Potion || me.HasOpenPotionSlots))
                    actions.Add(new LegalAction(item.Id, item.Label));

            if (BackButton(shop.Screen) is { } back && Nodes.IsShown(back) && back.IsEnabled && !Clicks.RecentlyClicked(back))
                actions.Add(new LegalAction("close", "Close the shop inventory"));
        }
        else
        {
            // Worth opening only when something is affordable (the stock is in the state either
            // way), or when the stock can't be read until it opens.
            if ((!shop.StockKnown || shop.Items.Any(i => i.Affordable)) && !me.Creature.IsDead && !Clicks.RecentlyClicked(room))
                actions.Add(new LegalAction("open", _openPending
                    ? "Open the shop inventory again (the last try did not open it)"
                    : shop.StockKnown ? "Open the shop inventory to buy" : "Open the shop inventory to see the stock"));

            // Disabled while the inventory or anything else is the current screen.
            var proceed = room.ProceedButton;
            if (proceed is not null && Nodes.IsShown(proceed) && proceed.IsEnabled && !Clicks.RecentlyClicked(proceed))
                actions.Add(new LegalAction("proceed", "Leave the shop"));
        }
        return actions;
    }

    private static ShopView? Read(NMerchantRoom room)
    {
        var screen = room.Inventory;
        if (screen is null || !GodotObject.IsInstanceValid(screen)) return null;
        // The room's _Ready attaches the stock (open or closed). Without it the room can still be
        // opened or left, rather than reporting "loading" for good.
        if (screen.Inventory is not { } inv)
            return new ShopView(screen, screen.IsOpen, false, new List<Item>());

        // Cost includes Hook.ModifyMerchantPrice (discount relics) while the current room is the shop.
        // n is the entry's place in the game's list, sold-out ones included, so ids don't shift after a purchase.
        var items = new List<Item>();

        var n = 0;
        foreach (var entry in inv.CharacterCardEntries)
        {
            var i = n++;
            Add(items, ItemKind.Card, i, () => entry.IsStocked && entry.CreationResult?.Card is CardModel card
                ? Make(ItemKind.Card, i, Describe.CardName(card), Describe.CardOffer(card), entry.Cost, entry.EnoughGold, entry.IsOnSale,
                    () => entry.OnTryPurchaseWrapper(inv))
                : null);
        }

        n = 0;
        foreach (var entry in inv.ColorlessCardEntries)
        {
            var i = n++;
            Add(items, ItemKind.Colorless, i, () => entry.IsStocked && entry.CreationResult?.Card is CardModel card
                ? Make(ItemKind.Colorless, i, Describe.CardName(card), Describe.CardOffer(card), entry.Cost, entry.EnoughGold, false,
                    () => entry.OnTryPurchaseWrapper(inv))
                : null);
        }

        n = 0;
        foreach (var entry in inv.RelicEntries)
        {
            var i = n++;
            Add(items, ItemKind.Relic, i, () => entry.IsStocked && entry.Model is RelicModel relic
                ? Make(ItemKind.Relic, i, Describe.Text(relic.Title), Describe.Relic(relic), entry.Cost, entry.EnoughGold, false,
                    () => entry.OnTryPurchaseWrapper(inv))
                : null);
        }

        n = 0;
        foreach (var entry in inv.PotionEntries)
        {
            var i = n++;
            Add(items, ItemKind.Potion, i, () => entry.IsStocked && entry.Model is PotionModel potion
                ? Make(ItemKind.Potion, i, Describe.Text(potion.Title), Describe.Potion(potion), entry.Cost, entry.EnoughGold, false,
                    () => entry.OnTryPurchaseWrapper(inv))
                : null);
        }

        // Typed as the removal entry so its own purchase (with the cancelable flag) runs rather than the base one.
        Add(items, ItemKind.Removal, 0, () => inv.CardRemovalEntry is MerchantCardRemovalEntry removal && removal.IsStocked
            ? Make(ItemKind.Removal, 0, "card removal", new JsonObject(), removal.Cost, removal.EnoughGold, false,
                () => removal.OnTryPurchaseWrapper(inv, cancelable: true))
            : null);

        return new ShopView(screen, screen.IsOpen, true, items);
    }

    private static void Add(List<Item> items, ItemKind kind, int n, Func<Item?> make)
    {
        try
        {
            if (make() is { } item) items.Add(item);
        }
        catch (Exception ex)
        {
            // One odd entry must not hide the rest of the shop.
            if (Unreadable.Add($"{kind}{n}")) ModEntry.LogError($"reading shop entry {kind} {n}: {ex.Message}");
        }
    }

    private static Item Make(ItemKind kind, int n, string name, JsonObject body, int price, bool affordable, bool onSale, Func<Task<bool>> buy)
    {
        var json = kind == ItemKind.Removal ? body : Describe.Lead("n", n, body);
        json["price"] = price;
        if (onSale) json["on_sale"] = true;
        json["affordable"] = affordable;
        return new Item(kind, n, name, price, affordable, onSale, json, buy);
    }

    private static JsonObject BuildState(GameContext ctx, RunState rs, Player me, ShopView shop)
    {
        var player = Describe.PlayerRun(me, includeDeck: true);
        player.Remove("potions"); // replaced by the detailed slots below
        player.Remove("gold");    // top level below

        var stock = new JsonObject();
        AddList(stock, "cards", shop, ItemKind.Card);
        AddList(stock, "colorless", shop, ItemKind.Colorless);
        AddList(stock, "relics", shop, ItemKind.Relic);
        AddList(stock, "potions", shop, ItemKind.Potion);
        if (shop.Items.FirstOrDefault(i => i.Kind == ItemKind.Removal) is { } removal)
            stock["removal"] = removal.Json;

        var state = new JsonObject
        {
            ["run"] = Describe.RunInfo(rs),
            ["gold"] = me.Gold,
            ["inventory_open"] = shop.Open,
            ["player"] = player,
            ["potions"] = PotionActions.Describe(ctx, false),
            ["stock"] = shop.StockKnown ? stock : (JsonNode)"unknown until the inventory is opened",
        };
        if (!me.HasOpenPotionSlots && shop.Items.Any(i => i.Kind == ItemKind.Potion))
            state["potion_slots_full"] = true;
        if (_failedNote is { } failed)
            state["last_purchase_failed"] = failed;
        return state;
    }

    private static void AddList(JsonObject stock, string key, ShopView shop, ItemKind kind)
    {
        var list = new JsonArray();
        foreach (var item in shop.Items)
            if (item.Kind == kind) list.Add(item.Json);
        if (list.Count > 0) stock[key] = list;
    }

    /// <summary>
    /// The inventory's own back button (the node it wires to Close). It is disabled until the
    /// inventory opens and while Lord's Parasol blocks input.
    /// </summary>
    private static NBackButton? BackButton(NMerchantInventory screen) =>
        Nodes.Get<NBackButton>(screen, "%BackButton");

    /// <summary>
    /// Lord's Parasol buys out the shop on arrival with travel turned off (it opens the inventory
    /// itself and blocks its input), then forces a card removal, and turns travel back on when
    /// done. Acting before then would overlap its purchases, and a proceed would open a map with
    /// travel off. A teammate's parasol only buys from that teammate's own inventory.
    /// </summary>
    private static bool ParasolBusy(Player me) =>
        me.GetRelic<LordsParasol>() is not null && NMapScreen.Instance is { IsTravelEnabled: false };

    /// <summary>
    /// The button of a tutorial tip drawn inside the shop (modal FTUEs go to the modal reader
    /// before this reader is asked). Only shallow levels are searched so the poll stays cheap.
    /// </summary>
    private static NClickableControl? TipButton(NMerchantRoom room)
    {
        var tip = FindTip(room, 3) ?? (NRun.Instance?.GlobalUi is Node ui ? FindTip(ui, 1) : null);
        if (tip is null) return null;

        foreach (var button in Nodes.FindAll<NClickableControl>(tip))
        {
            if (!Nodes.IsShown(button) || !button.IsEnabled) continue;
            if (TipsTried.Contains(button.GetInstanceId()) && !Clicks.RecentlyClicked(button)) continue;
            return button;
        }
        return null;
    }

    private static Node? FindTip(Node root, int depth)
    {
        if (!GodotObject.IsInstanceValid(root)) return null;
        foreach (var child in root.GetChildren())
        {
            // Hidden subtrees can't hold a visible tip.
            if (child is CanvasItem { Visible: false } or CanvasLayer { Visible: false }) continue;
            if (child.GetType().Namespace?.StartsWith(FtueNamespace, StringComparison.Ordinal) == true)
                return child;
            if (depth > 1 && FindTip(child, depth - 1) is { } found)
                return found;
        }
        return null;
    }

    // ---------- helpers ----------

    private static NMerchantRoom? Room(Node? node)
    {
        var room = node as NMerchantRoom ?? NMerchantRoom.Instance;
        return room is not null && Nodes.Alive(room) ? room : null;
    }
}
