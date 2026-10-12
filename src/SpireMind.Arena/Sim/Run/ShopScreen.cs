using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The shop screen (the game's merchant room as the mod's ShopReader shows it): one screen in
/// two modes. A visit starts with the inventory closed: the stock is listed, and the actions
/// are <c>open</c> (only when something on the shelf can be paid for) and <c>proceed</c>. With
/// the inventory open the actions are the buys and <c>close</c>; never a buy and
/// <c>proceed</c> in one snapshot. Keys, their order, labels and refusals are those of
/// docs/screens.md, "Shop".
/// <para>
/// An entry is numbered by its place in the game's list of its kind, sold-out ones included:
/// after card 2 was bought the cards are 0, 1, 3, 4. A buy is offered for an entry that is
/// stocked and can be paid for, a potion only with a free slot in the belt
/// (<c>potion_slots_full</c> says so otherwise).
/// </para>
/// <para>
/// <c>buy:remove</c> asks which card through the deck's removal screen, as a question of the
/// action (<see cref="Run.Ask"/>). Closing that screen without choosing ends the purchase with
/// nothing paid, and the shop then carries <c>last_purchase_failed: "a card removal"</c>
/// until the next shop action that succeeds; potion actions do not clear it. A relic whose
/// pickup opens a screen of its own (a card choice, a rewards list) puts that screen over the
/// shop; the shop shows again when it is done.
/// </para>
/// <para>
/// <c>proceed</c> leaves the room: the screen is taken away and the director is told
/// (<see cref="Run.RoomFinished"/>).
/// </para>
/// <para>
/// Not imitated, because a profile that has seen the tutorials never meets them: the
/// merchant's tutorial popup and tips drawn in the room (<c>dismiss</c> is refused as the
/// reader refuses it with no tip up), the waits while the panel slides, and Lord's Parasol,
/// which belongs to an Ancient of act 3.
/// </para>
/// </summary>
public sealed class ShopScreen : RunScreen
{
    public ShopScreen(ShopInventory inventory) => Inventory = inventory;

    /// <summary>What the shop sells, as it stands.</summary>
    public ShopInventory Inventory { get; }

    /// <summary>The stock panel is up: buying is offered, leaving is not.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>What the last purchase was, when it did not go through; cleared by the next shop action that succeeds.</summary>
    public string? LastPurchaseFailed { get; private set; }

    public override string Name => "shop";

    // ---------- the stocked entries, as the reader lists them ----------

    private enum ItemKind { Card, Colorless, Relic, Potion, Removal }

    /// <summary>One stocked entry as it is shown and offered right now.</summary>
    private sealed record Item(ItemKind Kind, int N, string Name, int Price, bool Affordable, bool OnSale, JsonObject Json, ShopEntry Entry)
    {
        public string Id => Kind switch
        {
            ItemKind.Card => string.Create(CultureInfo.InvariantCulture, $"buy:card{N}"),
            ItemKind.Colorless => string.Create(CultureInfo.InvariantCulture, $"buy:colorless{N}"),
            ItemKind.Relic => string.Create(CultureInfo.InvariantCulture, $"buy:relic{N}"),
            ItemKind.Potion => string.Create(CultureInfo.InvariantCulture, $"buy:potion{N}"),
            _ => "buy:remove",
        };

        public string What => Kind == ItemKind.Removal ? "a card removal" : Name;

        public string Label => Kind switch
        {
            ItemKind.Card => string.Create(CultureInfo.InvariantCulture, $"Buy {Name} for {Price} gold{(OnSale ? " (on sale)" : "")}"),
            ItemKind.Colorless => string.Create(CultureInfo.InvariantCulture, $"Buy colorless {Name} for {Price} gold"),
            ItemKind.Relic => string.Create(CultureInfo.InvariantCulture, $"Buy relic {Name} for {Price} gold"),
            ItemKind.Potion => string.Create(CultureInfo.InvariantCulture, $"Buy potion {Name} for {Price} gold"),
            _ => string.Create(CultureInfo.InvariantCulture, $"Pay {Price} gold to remove a card from your deck"),
        };
    }

    /// <summary>The stocked entries in the reader's order: cards, colorless cards, relics, potions, the removal.</summary>
    private List<Item> Items(Run run)
    {
        var items = new List<Item>();
        void Add(ItemKind kind, int n, string name, JsonObject body, ShopEntry entry, bool onSale = false)
        {
            // n leads; the entry's own keys follow; then the price as it stands with the price hooks, and whether the gold is there.
            var json = kind == ItemKind.Removal ? body : new JsonObject { ["n"] = n };
            if (kind != ItemKind.Removal)
                foreach (var (key, value) in body) json[key] = value?.DeepClone();
            var price = entry.Cost(run);
            var affordable = entry.EnoughGold(run);
            json["price"] = price;
            if (onSale) json["on_sale"] = true;
            json["affordable"] = affordable;
            items.Add(new Item(kind, n, name, price, affordable, onSale, json, entry));
        }

        for (var n = 0; n < Inventory.CharacterCards.Count; n++)
            if (Inventory.CharacterCards[n] is { Card: { } card } entry) Add(ItemKind.Card, n, card.Title, RunDescribe.CardOffer(card), entry, entry.IsOnSale);
        for (var n = 0; n < Inventory.ColorlessCards.Count; n++)
            if (Inventory.ColorlessCards[n] is { Card: { } card } entry) Add(ItemKind.Colorless, n, card.Title, RunDescribe.CardOffer(card), entry);
        for (var n = 0; n < Inventory.Relics.Count; n++)
            if (Inventory.Relics[n] is { Relic: { } relic } entry) Add(ItemKind.Relic, n, relic.Name, RunDescribe.Relic(relic), entry);
        for (var n = 0; n < Inventory.Potions.Count; n++)
            if (Inventory.Potions[n] is { Potion: { } potion } entry) Add(ItemKind.Potion, n, potion.Name, RunDescribe.Potion(potion), entry);
        if (Inventory.Removal is { IsStocked: true } removal) Add(ItemKind.Removal, 0, "card removal", new JsonObject(), removal);
        return items;
    }

    // ---------- the snapshot ----------

    public override GameSnapshot Snapshot(Run run)
    {
        var items = Items(run);
        var beltFull = !run.State.Belt.HasOpenSlot;

        var stock = new JsonObject();
        void AddList(string key, ItemKind kind)
        {
            var list = new JsonArray();
            foreach (var item in items)
                if (item.Kind == kind) list.Add(item.Json);
            if (list.Count > 0) stock[key] = list;
        }
        AddList("cards", ItemKind.Card);
        AddList("colorless", ItemKind.Colorless);
        AddList("relics", ItemKind.Relic);
        AddList("potions", ItemKind.Potion);
        if (items.FirstOrDefault(item => item.Kind == ItemKind.Removal) is { } removal) stock["removal"] = removal.Json;

        var state = new JsonObject { ["run"] = run.RunJson() };
        MapState.AddBoss(state, run);
        state["gold"] = run.Gold;
        state["inventory_open"] = IsOpen;
        state["player"] = run.PlayerJson(PlayerKeys.Hp | PlayerKeys.Deck);
        state["potions"] = RunPotions.Describe(run);
        state["stock"] = stock;
        if (beltFull && items.Any(item => item.Kind == ItemKind.Potion)) state["potion_slots_full"] = true;
        if (LastPurchaseFailed is { } failed) state["last_purchase_failed"] = failed;

        var actions = new List<LegalAction>();
        if (IsOpen)
        {
            foreach (var item in items)
                if (item.Affordable && (item.Kind != ItemKind.Potion || !beltFull)) actions.Add(new LegalAction(item.Id, item.Label));
            actions.Add(new LegalAction("close", "Close the shop inventory"));
        }
        else
        {
            // Worth opening only when something can be paid for; the stock is in the state either way.
            if (CanOpen(run, items)) actions.Add(new LegalAction("open", "Open the shop inventory to buy"));
            actions.Add(new LegalAction("proceed", "Leave the shop"));
        }
        // Potion actions ride along with the shop's own, which are never none.
        actions.AddRange(RunPotions.Legal(run));
        return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = state, Actions = actions };
    }

    private static bool CanOpen(Run run, List<Item> items) => items.Any(item => item.Affordable) && !run.State.IsDead;

    // ---------- the actions ----------

    public override ActionResult Execute(Run run, string actionId)
    {
        if (RunPotions.TryExecute(run, actionId, out var potion)) return potion;

        return actionId switch
        {
            "dismiss" => ActionResult.Fail("No tip is showing."),
            "open" => Open(run),
            "close" => Close(),
            "proceed" => Leave(run),
            _ when actionId.StartsWith("buy:", StringComparison.Ordinal) => Buy(run, actionId),
            _ => ActionResult.Fail($"Unknown shop action '{actionId}'."),
        };
    }

    private ActionResult Open(Run run)
    {
        if (IsOpen) return ActionResult.Fail("The shop inventory is already open.");
        if (run.State.IsDead) return ActionResult.Fail("The merchant won't trade with a dead player.");
        // The reader has no refusal of its own for this: with nothing to pay for it does not offer the id, and the mod's
        // bridge turns down an id the fresh snapshot does not offer in these words.
        if (!CanOpen(run, Items(run))) return ActionResult.Fail($"'open' is no longer available on the {Name} screen.");
        IsOpen = true;
        LastPurchaseFailed = null;
        return ActionResult.Success;
    }

    private ActionResult Close()
    {
        if (!IsOpen) return ActionResult.Fail("The shop inventory is not open.");
        IsOpen = false;
        LastPurchaseFailed = null;
        return ActionResult.Success;
    }

    private ActionResult Leave(Run run)
    {
        if (IsOpen) return ActionResult.Fail("Close the shop inventory first.");
        LastPurchaseFailed = null;
        TakeOffTheStack(run);
        run.RoomFinished();
        return ActionResult.Success;
    }

    private ActionResult Buy(Run run, string actionId)
    {
        if (!IsOpen) return ActionResult.Fail("Open the shop inventory first.");
        var item = Items(run).FirstOrDefault(each => each.Id == actionId);
        if (item is null) return ActionResult.Fail($"'{actionId}' is not in stock.");
        if (!item.Affordable) return ActionResult.Fail(string.Create(CultureInfo.InvariantCulture, $"Not enough gold for {item.What} ({item.Price})."));
        if (item.Kind == ItemKind.Potion && !run.State.Belt.HasOpenSlot) return ActionResult.Fail("Potion slots are full; use or discard a potion first.");

        // The buy itself is accepted from here on. A purchase the game turns down after it started (a removal that was
        // backed out of, a potion a hook keeps out of the belt) is not a refusal: the next snapshot says what failed.
        LastPurchaseFailed = null;
        if (!Inventory.TryPurchase(run, item.Entry)) LastPurchaseFailed = item.What;
        return ActionResult.Success;
    }

    /// <summary>Takes this screen off the run's screens; anything a purchase put over it stays, in its order.</summary>
    private void TakeOffTheStack(Run run)
    {
        var above = new Stack<RunScreen>();
        while (!ReferenceEquals(run.Top, this))
        {
            if (run.Top is null) throw new InvalidOperationException("The shop screen is gone.");
            above.Push(run.Pop());
        }
        run.Pop();
        while (above.Count > 0) run.Push(above.Pop());
    }

    public override RunScreen Clone(CloneMap map) => new ShopScreen(Inventory.Clone(map)) { IsOpen = IsOpen, LastPurchaseFailed = LastPurchaseFailed };
}
