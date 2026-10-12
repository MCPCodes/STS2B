using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The card choices of a run outside a fight, kind by kind, each driven through its whole flow
/// by a script: remove, upgrade, transform and select on the deck screens (pick, preview,
/// confirm, back, close), select on the plain grid, and choose. Every expected snapshot is
/// written out by hand from docs/screens.md ("Card selection": key order, labels, refusals,
/// the examples there) and the mod's CardSelectReader, with the arena's own card texts
/// (Strike 6, Defend 5, Bash 8 and 2 Vulnerable; upgraded 9, 8, 10 and 3). Which cards a
/// screen lists, when nothing is asked, and the order the picks come back in are the game's
/// (CardSelectCmd and the four grid screens).
/// </summary>
public sealed class CardSelectChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Removal(c);
        SeveralRemovals(c);
        PickOrder(c);
        WhatARemovalLists(c);
        Upgrades(c);
        SeveralUpgrades(c);
        Transforms(c);
        OptionalTransform(c);
        OptionalRemoval(c);
        DeckSelect(c);
        PlainGrid(c);
        PlainGridWithConfirm(c);
        Choose(c);
        NotAsked(c);
        Refusals(c);
        CopiesAndSaves(c);
        TwoQuestions(c);
        TheSet(c);
        RandomWalks(c);
    }

    // ---------- the cards as the screens write them ----------

    /// <summary>JSON written with single quotes for double ones, to keep the expected texts readable.</summary>
    private static string Q(string json) => json.Replace('\'', '"');

    private static readonly string Strike = Q("'name':'Strike','cost':'1','type':'Attack','text':'Deal 6 damage.','rarity':'Basic'");
    private static readonly string StrikeUp = Q("'name':'Strike+','cost':'1','type':'Attack','text':'Deal 9 damage.','rarity':'Basic'");
    private static readonly string Defend = Q("'name':'Defend','cost':'1','type':'Skill','text':'Gain 5 Block.','rarity':'Basic'");
    private static readonly string DefendUp = Q("'name':'Defend+','cost':'1','type':'Skill','text':'Gain 8 Block.','rarity':'Basic'");
    private static readonly string Bash = Q("'name':'Bash','cost':'2','type':'Attack','text':'Deal 8 damage. Apply 2 Vulnerable.','rarity':'Basic'");
    private static readonly string BashUp = Q("'name':'Bash+','cost':'2','type':'Attack','text':'Deal 10 damage. Apply 3 Vulnerable.','rarity':'Basic'");
    private static readonly string Pommel = Q("'name':'Pommel Strike','cost':'1','type':'Attack','text':'Deal 9 damage. Draw 1 card.','rarity':'Common'");
    private static readonly string Anger = Q("'name':'Anger','cost':'0','type':'Attack','text':'Deal 6 damage. Add a copy of this card to your discard pile.','rarity':'Common'");
    private static readonly string Shrug = Q("'name':'Shrug It Off','cost':'1','type':'Skill','text':'Gain 8 Block. Draw 1 card.','rarity':'Common'");
    private static readonly string Inflame = Q("'name':'Inflame','cost':'1','type':'Power','text':'Gain 2 Strength.','rarity':'Uncommon'");
    private static readonly string Barricade = Q("'name':'Barricade','cost':'3','type':'Power','text':'Your Block is no longer removed at the start of your turn.','rarity':'Rare'");

    private static readonly string Hint = Q("'hint':'pick a card, then confirm it on the preview'");
    private static readonly string Floor0 = Q("'run':{'act':1,'floor':0,'ascension':0}");

    /// <summary>The player of a new run on a grid that lists the whole deck: the deck's name counts are left out.</summary>
    private static readonly string StarterNoDeck = Q("'player':{'character':'The Ironclad','hp':80,'max_hp':80,'gold':99,'relics':['Burning Blood'],'potions':[null,null,null],'deck_size':10}");

    /// <summary>The same in the full form.</summary>
    private static readonly string StarterFull = Q("'player':{'character':'The Ironclad','hp':80,'max_hp':80,'gold':99,'relics':['Burning Blood'],'potions':[null,null,null],'deck_size':10,'deck':{'Bash':1,'Defend':4,'Strike':5}}");

    /// <summary>The starter deck as an untouched grid lists it: five Strikes under index 0, four Defends under 5, Bash at 9.</summary>
    private static readonly string StarterCards = $$"""
        "cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}}}]
        """.Trim();

    // ---------- the script ----------

    private static void Note(Desk desk, string what, IEnumerable<CardInstance> cards)
    {
        var names = string.Join(", ", cards.Select(card => card.Title));
        desk.Notes.Add(names.Length > 0 ? $"{what}: {names}" : $"{what}: nothing");
    }

    private static CardDef Def(string className) => Content.Registry.Card(className);

    private static List<CardInstance> Make(Run run, params string[] classNames) => classNames.Select(name => run.State.NewCard(Def(name))).ToList();

    private static readonly Dictionary<string, Action<Run, Desk>> Script = new()
    {
        // Precise Scissors and Precarious Shears: one card, two cards; no way back.
        ["scissors"] = static (run, desk) => Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1))),
        ["shears"] = static (run, desk) => Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(2))),
        ["three"] = static (run, desk) => Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(3))),
        // The shop's removal: one card, and the screen can be closed. 5 gold is paid before the screen opens, to
        // show what a question's screen sees.
        ["shop removal"] = static (run, desk) =>
        {
            run.State.Gold -= 5;
            Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1) with { Cancelable = true, RequireManualConfirmation = true }));
        },
        ["reward removal"] = static (run, desk) => Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.RemoveForReward())),
        // Smith: one card, and the screen can be closed. Pomander: one card, no way back.
        ["smith"] = static (run, desk) => Upgraded(run, desk, CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(1) with { Cancelable = true, RequireManualConfirmation = true })),
        ["pomander"] = static (run, desk) => Upgraded(run, desk, CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(1))),
        ["two upgrades"] = static (run, desk) => Upgraded(run, desk, CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(2))),
        // New Leaf: one card. Morphic Grove: two.
        ["new leaf"] = static (run, desk) => Note(desk, "to transform", CardSelect.FromDeckForTransformation(run, CardSelectPrefs.Transform(1))),
        ["grove"] = static (run, desk) => Note(desk, "to transform", CardSelect.FromDeckForTransformation(run, CardSelectPrefs.Transform(2))),
        ["up to two"] = static (run, desk) => Note(desk, "to transform", CardSelect.FromDeckForTransformation(run, new CardSelectPrefs("Choose up to 2 cards to transform.", 0, 2))),
        // A removal of up to two cards that can be closed: a deck screen with a confirm of its own and a close button.
        ["maybe remove"] = static (run, desk) => Removed(run, desk,
            CardSelect.FromDeckForRemoval(run, new CardSelectPrefs("Choose up to 2 cards to remove.", 0, 2) { Cancelable = true, IsRemovalPrompt = true })),
        // Dolly's Mirror: any card but a Quest card, on the generic deck screen with a prompt of its own.
        ["mirror"] = static (run, desk) => Note(desk, "to copy", CardSelect.FromDeckGeneric(run, new CardSelectPrefs("Choose a card to copy.", 1), card => card.Type != CardType.Quest)),
        // Room Full of Cheese: generated cards in a plain grid, two of them.
        ["cheese"] = static (run, desk) =>
        {
            var taken = CardSelect.FromSimpleGrid(run, Make(run, "Anger", "Anger", "ShrugItOff", "PommelStrike"), new CardSelectPrefs("Choose 2 cards to add to your deck.", 2));
            foreach (var card in taken) DeckEdits.Add(run, card);
            Note(desk, "took", taken);
        },
        // Sea Glass: any number of the offered cards, with a confirm button.
        ["glass"] = static (run, desk) => Note(desk, "took",
            CardSelect.FromSimpleGrid(run, Make(run, "Anger", "ShrugItOff", "Inflame"), new CardSelectPrefs("Choose any number of cards.", 0, 2))),
        // Hefty Tablet: one of three, or none; then the card and an Injury go into the deck together.
        ["tablet"] = static (run, desk) =>
        {
            var chosen = CardSelect.FromChooseACardScreen(run, Make(run, "Barricade", "Inflame", "Anger"), canSkip: true);
            var cards = new List<CardInstance> { run.State.NewCard(Curse("Injury", "Injury")) };
            if (chosen is not null) cards.Insert(0, chosen);
            DeckEdits.Add(run, cards);
            desk.Notes.Add(chosen is null ? "took: nothing" : $"took: {chosen.Title}");
        },
        ["toolbox"] = static (run, desk) => desk.Notes.Add($"took: {CardSelect.FromChooseACardScreen(run, Make(run, "Anger", "Inflame"))?.Title ?? "nothing"}"),
        // Two choices in one action: remove a card, then upgrade one.
        ["both"] = static (run, desk) =>
        {
            Removed(run, desk, CardSelect.FromDeckForRemoval(run, CardSelectPrefs.Remove(1)));
            Upgraded(run, desk, CardSelect.FromDeckForUpgrade(run, CardSelectPrefs.Upgrade(1)));
        },
        ["empty offers"] = static (run, desk) =>
        {
            desk.Notes.Add($"choose: {CardSelect.FromChooseACardScreen(run, [])?.Title ?? "nothing"}");
            Note(desk, "grid", CardSelect.FromSimpleGrid(run, [], new CardSelectPrefs("Choose a card.", 1)));
            Note(desk, "all of them", CardSelect.FromSimpleGrid(run, Make(run, "Anger", "Inflame"), new CardSelectPrefs("Choose 2 cards.", 2)));
        },
    };

    private static void Removed(Run run, Desk desk, IReadOnlyList<CardInstance> cards)
    {
        DeckEdits.Remove(run, cards);
        Note(desk, "removed", cards);
    }

    private static void Upgraded(Run run, Desk desk, IReadOnlyList<CardInstance> cards)
    {
        DeckEdits.Upgrade(run, cards);
        Note(desk, "upgraded", cards);
    }

    private static Run New(RunSetup? setup = null) => new(setup ?? new RunSetup(), new DeskDirector(Script));

    private static string State(Run run) => Text(run.Snapshot().State);

    private static List<string> DeskNotes(Run run) => ((Desk)run.Top!).Notes;

    // ---------- remove ----------

    private static void Removal(Check c)
    {
        c.Group("remove, with a close button: the shop's removal as docs/screens.md shows it", () =>
        {
            // The example of docs/screens.md: floor 6, 58 of 80 HP, a Fire Potion, an upgraded Defend and a Pommel
            // Strike in the deck. 46 gold, of which the script pays 5 before it asks.
            var run = New(new RunSetup
            {
                Hp = 58, Gold = 46, Potions = ["FirePotion"],
                Deck =
                [
                    new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"),
                    new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad", Upgraded: true), new("Bash"), new("PommelStrike"),
                ],
            });
            run.State.TotalFloor = 6;
            Do(run, "shop removal");

            var grid = run.Snapshot();
            c.Equal("card_select", grid.Screen, "the screen");
            c.That(grid.AwaitingDecision, "a decision");
            c.That(run.Question is CardSelectScreen { Kind: "remove", Layout: CardSelectScreen.GridLayout.Deck, PreviewOpen: false } && run.Top is Desk, "the question is open over the screen that asked");
            var player = Q("'player':{'character':'The Ironclad','hp':58,'max_hp':80,'gold':41,'relics':['Burning Blood'],'potions':['Fire Potion',null,null],'deck_size':11}");
            var run6 = Q("'run':{'act':1,'floor':6,'ascension':0}");
            var cards = $$"""{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":3},{"i":8,{{DefendUp}}},""";
            var open = $$"""{"kind":"remove","prompt":"Choose a card to remove.","min":1,"max":1,{{Hint}},"cards":[{{cards}}{"i":9,{{Bash}}},{"i":10,{{Pommel}}}],{{run6}},{{player}}}""";
            c.Equal(open, Text(grid.State), "the grid: kind, prompt, min, max, hint, cards (copies under the first index), run, player without the deck's counts");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:8=Pick Defend+ to remove | card:9=Pick Bash to remove | card:10=Pick Pommel Strike to remove | cancel=Close without removing a card",
                Labels(grid), "its actions: the picks, then the close button");

            Do(run, "card:9");
            var preview = run.Snapshot();
            c.Equal($$"""{"kind":"remove","prompt":"Choose a card to remove.","min":1,"max":1,"selected":["Bash"],"preview":[{{{Bash}}}],"cards":[{{cards}}{"i":9,{{Bash}},"selected":true},{"i":10,{{Pommel}}}],{{run6}},{{player}}}""",
                Text(preview.State), "after the pick: selected, the preview in place of the hint, and the picked entry marked");
            c.Equal("confirm=Confirm: remove Bash | cancel=Back to the cards (pick a different one)", Labels(preview), "the preview's two actions");
            c.Equal(11, run.State.Deck.Count, "nothing is removed yet");

            Do(run, "cancel");
            c.Equal(open, State(run), "back to the cards: the pick is dropped and the grid is as it was");
            c.Equal(Labels(grid), Labels(run.Snapshot()), "with the same actions, the close button included");

            Do(run, "card:10", "confirm");
            c.Equal("event", run.Snapshot().Screen, "the screen is closed");
            c.Equal(null, run.Question, "no question is open");
            c.Sequence(["removed: Pommel Strike"], DeskNotes(run), "the action went on with the card");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad+ Bash", DeckNames(run), "the Pommel Strike is gone");
            c.Equal(41, run.State.Gold, "the 5 gold was paid once");
            c.Sequence(["shop removal", "card:9", "cancel", "card:10", "confirm"], run.Actions, "every step is in the save");

            // Closing the screen: the action sees that nothing was chosen.
            Do(run, "shop removal", "cancel");
            c.Sequence(["removed: Pommel Strike", "removed: nothing"], DeskNotes(run), "closed without removing a card");
            c.Equal(10, run.State.Deck.Count, "the deck is as it was");
            c.Equal(36, run.State.Gold, "what the action did before it asked still counts");

            Do(run, "reward removal");
            c.That(run.Question is CardSelectScreen { Kind: "remove", Prefs: { Cancelable: true, Min: 1, Max: 1 } }, "a card removal reward asks the same way");
            c.That(Labels(run.Snapshot()).EndsWith("cancel=Close without removing a card", StringComparison.Ordinal), "and can be closed");
        });

        c.Group("remove without a close button (Precise Scissors)", () =>
        {
            var run = New();
            Do(run, "scissors");
            c.Equal($$"""{"kind":"remove","prompt":"Choose a card to remove.","min":1,"max":1,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "the grid over the whole starter deck");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove", Labels(run.Snapshot()), "no cancel: the game allows no backing out");
            Do(run, "card:5", "confirm");
            c.Sequence(["removed: Defend"], DeskNotes(run), "the first Defend is removed");
            c.Equal(9, run.State.Deck.Count, "nine cards");
        });
    }

    private static void SeveralRemovals(Check c)
    {
        c.Group("remove two cards (Precarious Shears): picks toggle, the second one opens the preview", () =>
        {
            var run = New();
            Do(run, "shears");
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "the grid");

            Do(run, "card:0");
            // docs/screens.md: "The picked copy leaves its group."
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,"selected":["Strike"],{{Hint}},"cards":[{"i":0,{{Strike}},"selected":true},{"i":1,{{Strike}},"copies":4},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}}}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "one picked: still the grid, with the hint");
            c.Equal("card:0=Deselect Strike | card:1=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove", Labels(run.Snapshot()), "the picked card can be put back");

            Do(run, "card:0");
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "deselected: the grid as at the start");

            Do(run, "card:0", "card:5");
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,"selected":["Strike","Defend"],"preview":[{{{Strike}}},{{{Defend}}}],"cards":[{"i":0,{{Strike}},"selected":true},{"i":1,{{Strike}},"copies":4},{"i":5,{{Defend}},"selected":true},{"i":6,{{Defend}},"copies":3},{"i":9,{{Bash}}}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "the second pick opens the preview of both");
            c.Equal("confirm=Confirm: remove Strike, Defend | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");

            Do(run, "cancel");
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "back drops both picks");

            Do(run, "card:9", "card:1", "confirm");
            c.Sequence(["removed: Bash, Strike"], DeskNotes(run), "both are removed, in the order picked");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad", DeckNames(run), "eight cards are left");
        });

        c.Group("a later copy can be picked by its own index", () =>
        {
            var run = New();
            Do(run, "shears", "card:3");
            c.Equal($$"""{"kind":"remove","prompt":"Choose 2 cards to remove.","min":2,"max":2,"selected":["Strike"],{{Hint}},"cards":[{"i":0,{{Strike}},"copies":4},{"i":3,{{Strike}},"selected":true},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}}}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "the fourth Strike is picked: the other four stay one entry under index 0");
            var fourth = run.State.Deck[3];
            Do(run, "card:9", "confirm");
            c.That(run.State.Deck.All(card => card.Id != fourth.Id), "and it is that card that is removed");
        });
    }

    private static void PickOrder(Check c)
    {
        c.Group("the picks are kept, shown and handed back in the order of the game's set", () =>
        {
            // The game keeps the picks in a .NET HashSet: a new pick takes the place the last deselected one left.
            var run = New();
            Do(run, "three", "card:0", "card:5", "card:0");
            c.Equal("""["Defend"]""", Text(run.Snapshot().State["selected"]), "Strike picked and put back: Defend alone, in the second place");
            Do(run, "card:9");
            c.Equal("""["Bash","Defend"]""", Text(run.Snapshot().State["selected"]), "Bash takes the place the Strike left: it comes first");
            Do(run, "card:1");
            c.Equal("""["Bash","Defend","Strike"]""", Text(run.Snapshot().State["selected"]), "the third goes to the end");
            c.Equal($$"""[{{{Bash}}},{{{Defend}}},{{{Strike}}}]""", Text(run.Snapshot().State["preview"]), "the preview shows them in that order");
            c.Equal("confirm=Confirm: remove Bash, Defend, Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the label names them in that order");
            Do(run, "confirm");
            c.Sequence(["removed: Bash, Defend, Strike"], DeskNotes(run), "and the action gets them in that order");
        });
    }

    private static void WhatARemovalLists(Check c)
    {
        c.Group("a removal lists the curses first and never an Eternal card", () =>
        {
            var run = New(new RunSetup { ExtraCards = [(Curse("Greed", "Greed", CardKeywords.Eternal), false), (Curse("Clumsy", "Clumsy"), false)] });
            Do(run, "scissors");
            var clumsy = Q("'name':'Clumsy','cost':'-','type':'Curse','text':'Unplayable. / A curse of the checks.','rarity':'Curse'");
            var player = Q("'player':{'character':'The Ironclad','hp':80,'max_hp':80,'gold':99,'relics':['Burning Blood'],'potions':[null,null,null],'deck_size':12,'deck':{'Bash':1,'Clumsy':1,'Defend':4,'Greed':1,'Strike':5}}");
            c.Equal($$"""{"kind":"remove","prompt":"Choose a card to remove.","min":1,"max":1,{{Hint}},"cards":[{"i":0,{{clumsy}}},{"i":1,{{Strike}},"copies":5},{"i":6,{{Defend}},"copies":4},{"i":10,{{Bash}}}],{{Floor0}},{{player}}}""",
                State(run), "Clumsy (last in the deck) is listed first, Greed not at all; the grid is not the whole deck, so the deck's counts are there");
            Do(run, "card:0", "confirm");
            c.Sequence(["removed: Clumsy"], DeskNotes(run), "card:0 is the curse");
        });
    }

    // ---------- upgrade ----------

    private static void Upgrades(Check c)
    {
        c.Group("upgrade with a close button (Smith): the preview shows the card and the upgraded card", () =>
        {
            var run = New();
            Do(run, "smith");
            var open = $$"""{"kind":"upgrade","prompt":"Choose a card to upgrade.","min":1,"max":1,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""";
            c.Equal(open, State(run), "the grid: every card of the starter deck can be upgraded, so it is the whole deck");
            c.Equal("card:0=Pick Strike to upgrade | card:5=Pick Defend to upgrade | card:9=Pick Bash to upgrade | cancel=Close without choosing", Labels(run.Snapshot()), "its actions");

            Do(run, "card:9");
            c.Equal($$"""{"kind":"upgrade","prompt":"Choose a card to upgrade.","min":1,"max":1,"selected":["Bash"],"preview":[{{{Bash}}},{{{BashUp}}}],"cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}},"selected":true}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "the preview: the card as it is, then as it will be");
            c.Equal("confirm=Confirm: upgrade Bash | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");
            c.That(!run.State.Deck[9].IsUpgraded, "the preview upgrades nothing");

            Do(run, "cancel");
            c.Equal(open, State(run), "back to the cards");
            Do(run, "card:9", "confirm");
            c.Sequence(["upgraded: Bash+"], DeskNotes(run), "confirmed: Bash is upgraded");
            c.That(run.State.Deck[9] is { Title: "Bash+", UpgradeLevel: 1 }, "in the deck");

            // The upgraded card is not offered again, so the grid is no longer the whole deck.
            Do(run, "smith");
            var player = Q("'player':{'character':'The Ironclad','hp':80,'max_hp':80,'gold':99,'relics':['Burning Blood'],'potions':[null,null,null],'deck_size':10,'deck':{'Bash+':1,'Defend':4,'Strike':5}}");
            c.Equal($$"""{"kind":"upgrade","prompt":"Choose a card to upgrade.","min":1,"max":1,{{Hint}},"cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":4}],{{Floor0}},{{player}}}""",
                State(run), "the second visit lists the nine cards left, and the player block has the deck's counts");
            Do(run, "cancel");
            c.Sequence(["upgraded: Bash+", "upgraded: nothing"], DeskNotes(run), "closing gives the action nothing");
            c.Equal("event", run.Snapshot().Screen, "and the screen that asked is back");
        });
    }

    private static void SeveralUpgrades(Check c)
    {
        c.Group("upgrade two cards: no confirm on the grid, and the preview shows only the upgraded cards", () =>
        {
            var run = New();
            Do(run, "two upgrades", "card:0");
            c.Equal($$"""{"kind":"upgrade","prompt":"Choose 2 cards to upgrade.","min":2,"max":2,"selected":["Strike"],{{Hint}},"cards":[{"i":0,{{Strike}},"selected":true},{"i":1,{{Strike}},"copies":4},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}}}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "one picked: the grid stays");
            c.Equal("card:0=Deselect Strike | card:1=Pick Strike to upgrade | card:5=Pick Defend to upgrade | card:9=Pick Bash to upgrade", Labels(run.Snapshot()), "no confirm and no cancel on this grid");
            Do(run, "card:9");
            c.Equal($$"""[{{{StrikeUp}}},{{{BashUp}}}]""", Text(run.Snapshot().State["preview"]), "the preview of several: the upgraded cards only");
            c.Equal("confirm=Confirm: upgrade Strike, Bash | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");
            Do(run, "confirm");
            c.Sequence(["upgraded: Strike+, Bash+"], DeskNotes(run), "both are upgraded");
            c.Equal("StrikeIronclad+ StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+", DeckNames(run), "in the deck");
        });
    }

    // ---------- transform ----------

    private static void Transforms(Check c)
    {
        c.Group("transform (New Leaf): the preview shows the card and never what it becomes", () =>
        {
            var run = New();
            Do(run, "new leaf");
            var open = $$"""{"kind":"transform","prompt":"Choose a card to transform.","min":1,"max":1,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""";
            c.Equal(open, State(run), "the grid");
            c.Equal("card:0=Pick Strike to transform | card:5=Pick Defend to transform | card:9=Pick Bash to transform", Labels(run.Snapshot()), "its actions");
            Do(run, "card:5");
            c.Equal($$"""{"kind":"transform","prompt":"Choose a card to transform.","min":1,"max":1,"selected":["Defend"],"preview":[{{{Defend}}}],"cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"selected":true},{"i":6,{{Defend}},"copies":3},{"i":9,{{Bash}}}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "the preview: the picked card alone");
            c.Equal("confirm=Confirm: transform Defend | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");
            Do(run, "cancel");
            c.Equal(open, State(run), "back to the cards");
            Do(run, "card:9", "confirm");
            c.Sequence(["to transform: Bash"], DeskNotes(run), "confirmed");
        });

        c.Group("transform two cards (Morphic Grove)", () =>
        {
            var run = New();
            Do(run, "grove", "card:5", "card:0");
            c.Equal("""["Defend","Strike"]""", Text(run.Snapshot().State["selected"]), "two picked");
            c.Equal($$"""[{{{Defend}}},{{{Strike}}}]""", Text(run.Snapshot().State["preview"]), "the preview shows both, in the order picked");
            c.Equal("confirm=Confirm: transform Defend, Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");
            Do(run, "confirm");
            c.Sequence(["to transform: Defend, Strike"], DeskNotes(run), "the action gets both, in the order picked");
        });

        c.Group("a transform lists neither Quest cards nor Eternal cards", () =>
        {
            var quest = new CardDef("SpoilsMap", "Spoils Map", CardType.Quest, CardRarity.Quest, CardDef.NoCost, TargetType.Self) { Keywords = CardKeywords.Unplayable, MaxUpgradeLevel = 0 };
            var run = New(new RunSetup { ExtraCards = [(quest, false), (Curse("Greed", "Greed", CardKeywords.Eternal), false), (Curse("Clumsy", "Clumsy"), false)] });
            Do(run, "new leaf");
            var listed = ((CardSelectScreen)run.Question!).Cards.Select(card => card.Title).ToList();
            c.Sequence(["Strike", "Strike", "Strike", "Strike", "Strike", "Defend", "Defend", "Defend", "Defend", "Bash", "Clumsy"], listed, "the deck in deck order without Spoils Map and Greed; a curse stays where it is");
            c.Equal(13, (int)run.Snapshot().State["player"]!["deck_size"]!, "of thirteen cards");
            c.That(run.Snapshot().State["player"]!["deck"] is not null, "so the deck's counts are shown");
        });
    }

    private static void OptionalTransform(Check c)
    {
        c.Group("a choice with a minimum below its maximum: a confirm on the grid opens the preview early", () =>
        {
            var run = New();
            Do(run, "up to two");
            c.Equal($$"""{"kind":"transform","prompt":"Choose up to 2 cards to transform.","min":0,"max":2,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "the grid");
            c.Equal("card:0=Pick Strike to transform | card:5=Pick Defend to transform | card:9=Pick Bash to transform | confirm=Confirm without choosing a card to transform",
                Labels(run.Snapshot()), "the grid's confirm is there from the start: nothing has to be picked");

            // Confirming with nothing picked opens a preview of nothing, which can only be confirmed.
            Do(run, "confirm");
            c.Equal($$"""{"kind":"transform","prompt":"Choose up to 2 cards to transform.","min":0,"max":2,"preview":"open",{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "the preview of nothing is the text \"open\"");
            c.Equal("confirm=Confirm without choosing a card to transform", Labels(run.Snapshot()), "only confirm: there is no pick to go back from");
            Do(run, "confirm");
            c.Sequence(["to transform: nothing"], DeskNotes(run), "nothing was chosen");

            // One picked, then the grid's confirm: the preview of that one.
            Do(run, "up to two", "card:9");
            c.Equal("card:0=Pick Strike to transform | card:5=Pick Defend to transform | card:9=Deselect Bash | confirm=Confirm: transform Bash", Labels(run.Snapshot()), "one picked: the grid's confirm names it");
            c.That(run.Snapshot().State["hint"] is not null && run.Snapshot().State["preview"] is null, "still the grid");
            Do(run, "confirm");
            c.Equal($$"""[{{{Bash}}}]""", Text(run.Snapshot().State["preview"]), "the preview opens early");
            c.Equal("confirm=Confirm: transform Bash | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "and is confirmed a second time");
            Do(run, "confirm");
            c.Equal("to transform: Bash", DeskNotes(run)[^1], "one card chosen");

            // Two picked: the maximum opens the preview by itself.
            Do(run, "up to two", "card:0", "card:5");
            c.Equal($$"""[{{{Strike}}},{{{Defend}}}]""", Text(run.Snapshot().State["preview"]), "the second pick reaches the maximum and opens the preview");
            Do(run, "confirm");
            c.Equal("to transform: Strike, Defend", DeskNotes(run)[^1], "two cards chosen");
        });
    }

    private static void OptionalRemoval(Check c)
    {
        c.Group("a deck screen with a confirm of its own and a close button: picks, then confirm, then cancel", () =>
        {
            var run = New();
            Do(run, "maybe remove");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove | confirm=Confirm without choosing a card to remove | cancel=Close without removing a card",
                Labels(run.Snapshot()), "the grid's actions in the reader's order");

            // The preview of nothing: the close button is off while a preview is up, and there is no pick to go back from.
            Do(run, "confirm");
            c.Equal(Q("'open'"), Text(run.Snapshot().State["preview"]), "confirm with nothing picked opens the preview of nothing");
            c.Equal("confirm=Confirm without choosing a card to remove", Labels(run.Snapshot()), "which offers confirm alone");
            var refused = run.Execute("cancel");
            c.That(!refused.Ok && refused.Error == "There is no enabled way to back out of this screen.", $"cancel is refused there ({refused.Error})");
            c.Equal("card_select", run.Snapshot().Screen, "and the screen stays");
            Do(run, "confirm");
            c.Sequence(["removed: nothing"], DeskNotes(run), "confirmed: nothing is removed");

            // One pick, the grid's confirm, back to the cards, and the close button is there again.
            Do(run, "maybe remove", "card:0", "confirm");
            c.Equal("confirm=Confirm: remove Strike | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "the preview of one pick");
            Do(run, "cancel");
            c.Equal("card:0=Pick Strike to remove | card:5=Pick Defend to remove | card:9=Pick Bash to remove | confirm=Confirm without choosing a card to remove | cancel=Close without removing a card",
                Labels(run.Snapshot()), "back on the grid with nothing picked and the close button on");
            Do(run, "cancel");
            c.Equal("event", run.Snapshot().Screen, "closed");
            c.Equal(10, run.State.Deck.Count, "nothing was removed");
        });
    }

    // ---------- select on the deck screen ----------

    private static void DeckSelect(Check c)
    {
        c.Group("select on the deck screen (Dolly's Mirror): a deck pick that is not a removal", () =>
        {
            var run = New();
            Do(run, "mirror");
            c.Equal($$"""{"kind":"select","prompt":"Choose a card to copy.","min":1,"max":1,{{Hint}},{{StarterCards}},{{Floor0}},{{StarterNoDeck}}}""", State(run), "the grid: kind select, with the hint of a deck screen");
            c.Equal("card:0=Select Strike | card:5=Select Defend | card:9=Select Bash", Labels(run.Snapshot()), "the labels of a selection");
            Do(run, "card:9");
            c.Equal($$"""{"kind":"select","prompt":"Choose a card to copy.","min":1,"max":1,"selected":["Bash"],"preview":[{{{Bash}}}],"cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":4},{"i":9,{{Bash}},"selected":true}],{{Floor0}},{{StarterNoDeck}}}""",
                State(run), "the preview");
            c.Equal("confirm=Confirm selection: Bash | cancel=Back to the cards (pick a different one)", Labels(run.Snapshot()), "its actions");
            Do(run, "confirm");
            c.Sequence(["to copy: Bash"], DeskNotes(run), "confirmed");
        });
    }

    // ---------- the plain grid ----------

    private static void PlainGrid(Check c)
    {
        c.Group("select on the plain grid (Room Full of Cheese): no preview, the last pick closes it", () =>
        {
            var run = New();
            Do(run, "cheese");
            c.That(run.Question is CardSelectScreen { Kind: "select", Layout: CardSelectScreen.GridLayout.Simple }, "the plain grid");
            c.Equal($$"""{"kind":"select","prompt":"Choose 2 cards to add to your deck.","min":2,"max":2,"cards":[{"i":0,{{Anger}},"copies":2},{"i":2,{{Shrug}}},{"i":3,{{Pommel}}}],{{Floor0}},{{StarterFull}}}""",
                State(run), "kind, prompt, min, max, cards, run, the full player block; no hint");
            c.Equal("card:0=Select Anger | card:2=Select Shrug It Off | card:3=Select Pommel Strike", Labels(run.Snapshot()), "its actions: no confirm, no cancel");

            Do(run, "card:0");
            c.Equal($$"""{"kind":"select","prompt":"Choose 2 cards to add to your deck.","min":2,"max":2,"selected":["Anger"],"cards":[{"i":0,{{Anger}},"selected":true},{"i":1,{{Anger}}},{"i":2,{{Shrug}}},{"i":3,{{Pommel}}}],{{Floor0}},{{StarterFull}}}""",
                State(run), "one picked");
            c.Equal("card:0=Deselect Anger | card:1=Select Anger | card:2=Select Shrug It Off | card:3=Select Pommel Strike", Labels(run.Snapshot()), "it can be put back");

            Do(run, "card:3");
            c.Equal("event", run.Snapshot().Screen, "the second pick closes the grid by itself");
            c.Sequence(["took: Anger, Pommel Strike"], DeskNotes(run), "the action gets both");
            c.Equal("Bash Anger PommelStrike", string.Join(" ", run.State.Deck.Skip(9).Select(card => card.Def.ClassName)), "and they are in the deck");
            c.Sequence(["cheese", "card:0", "card:3"], run.Actions, "three steps in the save");
        });
    }

    private static void PlainGridWithConfirm(Check c)
    {
        c.Group("the plain grid with a confirm button (any number of cards)", () =>
        {
            var run = New();
            Do(run, "glass");
            c.Equal($$"""{"kind":"select","prompt":"Choose any number of cards.","min":0,"max":2,"cards":[{"i":0,{{Anger}}},{"i":1,{{Shrug}}},{"i":2,{{Inflame}}}],{{Floor0}},{{StarterFull}}}""", State(run), "the grid");
            c.Equal("card:0=Select Anger | card:1=Select Shrug It Off | card:2=Select Inflame | confirm=Confirm with nothing selected", Labels(run.Snapshot()), "confirm is there with nothing picked");
            Do(run, "card:1");
            c.Equal("card:0=Select Anger | card:1=Deselect Shrug It Off | card:2=Select Inflame | confirm=Confirm selection: Shrug It Off", Labels(run.Snapshot()), "one picked");
            Do(run, "card:2");
            c.Equal("card:1=Deselect Shrug It Off | card:2=Deselect Inflame | confirm=Confirm selection: Shrug It Off, Inflame", Labels(run.Snapshot()),
                "at the maximum the grid stays open: only the picked cards and confirm are offered");
            c.Equal("card_select", run.Snapshot().Screen, "it does not close by itself");
            Do(run, "card:1", "confirm");
            c.Sequence(["took: Inflame"], DeskNotes(run), "confirm is final");

            Do(run, "glass", "confirm");
            c.Equal("took: nothing", DeskNotes(run)[^1], "confirming with nothing picked takes nothing");
        });
    }

    // ---------- choose ----------

    private static void Choose(Check c)
    {
        c.Group("choose (Hefty Tablet): one action takes a card, or skip", () =>
        {
            var run = New();
            Do(run, "tablet");
            c.That(run.Question is ChooseCardScreen { CanSkip: true, Cards.Count: 3 }, "the choose screen");
            c.Equal($$"""{"kind":"choose","min":0,"max":1,"cards":[{"i":0,{{Barricade}}},{"i":1,{{Inflame}}},{"i":2,{{Anger}}}],"can_skip":true,{{Floor0}},{{StarterFull}}}""",
                State(run), "kind, min, max, cards, can_skip, run, the full player block; no prompt");
            c.Equal("card:0=Take Barricade | card:1=Take Inflame | card:2=Take Anger | skip=Skip", Labels(run.Snapshot()), "its actions");
            var copy = run.Clone();

            Do(run, "card:1");
            c.Equal("event", run.Snapshot().Screen, "one action and the screen is closed");
            c.Sequence(["took: Inflame"], DeskNotes(run), "the card is taken");
            c.Equal("Bash Inflame Injury", string.Join(" ", run.State.Deck.Skip(9).Select(card => card.Def.ClassName)), "and is in the deck, before the Injury");

            Do(copy, "skip");
            c.Sequence(["took: nothing"], DeskNotes(copy), "a copy skips: nothing is taken");
            c.Equal("Bash Injury", string.Join(" ", copy.State.Deck.Skip(9).Select(card => card.Def.ClassName)), "only the Injury is added");
        });

        c.Group("a choice that cannot be skipped", () =>
        {
            var run = New();
            Do(run, "toolbox");
            c.Equal($$"""{"kind":"choose","min":1,"max":1,"cards":[{"i":0,{{Anger}}},{"i":1,{{Inflame}}}],"can_skip":false,{{Floor0}},{{StarterFull}}}""", State(run), "min 1 and can_skip false");
            c.Equal("card:0=Take Anger | card:1=Take Inflame", Labels(run.Snapshot()), "no skip");
            var refused = run.Execute("skip");
            c.That(!refused.Ok, "skip is refused");
            c.Equal("This choice cannot be skipped now.", refused.Error, "in the reader's words");
            Do(run, "card:0");
            c.Sequence(["took: Anger"], DeskNotes(run), "a card has to be taken");
        });
    }

    // ---------- when nothing is asked ----------

    private static void NotAsked(Check c)
    {
        c.Group("no more candidates than the minimum: they are taken without a question, unless the choice can be backed out of", () =>
        {
            // Only Bash can still be upgraded.
            RunSetup OneLeft() => new() { Deck = [new("StrikeIronclad", Upgraded: true), new("Bash")] };
            var run = New(OneLeft());
            Do(run, "pomander");
            c.Equal("event", run.Snapshot().Screen, "Pomander with one candidate asks nothing");
            c.Sequence(["upgraded: Bash+"], DeskNotes(run), "and upgrades it");
            c.Sequence(["pomander"], run.Actions, "one action");

            var smith = New(OneLeft());
            Do(smith, "smith");
            c.Equal("card_select", smith.Snapshot().Screen, "Smith with one candidate still asks: the visit can be backed out of");
            c.Equal("card:0=Pick Bash to upgrade | cancel=Close without choosing", Labels(smith.Snapshot()), "the one card, under its index among the cards the screen lists");

            Do(run, "pomander");
            c.Equal("upgraded: nothing", DeskNotes(run)[^1], "with nothing left to upgrade nothing is asked and nothing happens");
            Do(run, "smith");
            c.Equal("event", run.Snapshot().Screen, "Smith neither");

            // The same on the generic deck screen: the shop's removal asks even for the only card; Precise Scissors takes it.
            var shop = New(new RunSetup { Deck = [new("Bash")] });
            Do(shop, "shop removal");
            c.Equal("card:0=Pick Bash to remove | cancel=Close without removing a card", Labels(shop.Snapshot()), "the shop's removal with one card in the deck still asks");
            var scissors = New(new RunSetup { Deck = [new("Bash")] });
            Do(scissors, "scissors");
            c.Sequence(["removed: Bash"], DeskNotes(scissors), "Precise Scissors with one card in the deck removes it without a question");

            var two = New(new RunSetup { Deck = [new("StrikeIronclad"), new("Bash")] });
            Do(two, "shears");
            c.Sequence(["removed: Strike, Bash"], DeskNotes(two), "two removals from a deck of two: both go, in deck order, without a question");
            Do(two, "scissors");
            c.Equal("removed: nothing", DeskNotes(two)[^1], "a removal from an empty deck removes nothing");
            Do(two, "new leaf");
            c.Equal("to transform: nothing", DeskNotes(two)[^1], "and a transform transforms nothing");

            var dead = New();
            dead.State.Hp = 0;
            Do(dead, "scissors", "mirror");
            c.Sequence(["removed: nothing", "to copy: nothing"], DeskNotes(dead), "a dead player is not asked for a deck pick");

            var offers = New();
            Do(offers, "empty offers");
            c.Sequence(["choose: nothing", "grid: nothing", "all of them: Anger, Inflame"], DeskNotes(offers), "an empty offer asks nothing; a grid of two with two to take gives both");
        });
    }

    // ---------- refusals ----------

    private static void Refusals(Check c)
    {
        c.Group("what is not on offer is refused in the reader's words and changes nothing", () =>
        {
            void Refuse(Run run, string id, string error)
            {
                var before = run.Dump();
                var result = run.Execute(id);
                c.That(!result.Ok, $"'{id}' is refused");
                c.Equal(error, result.Error, $"'{id}' in the reader's words");
                c.Equal(before, run.Dump(), $"'{id}' changes nothing");
            }

            var run = New();
            Do(run, "scissors");
            Refuse(run, "skip", "Unknown card selection action 'skip'.");
            Refuse(run, "card:x", "Unknown card selection action 'card:x'.");
            Refuse(run, "card:10", "There is no card 10.");
            Refuse(run, "card:-1", "There is no card -1.");
            Refuse(run, "confirm", "The confirm button is not enabled.");
            Refuse(run, "cancel", "There is no enabled way to back out of this screen.");
            Do(run, "card:0");
            Refuse(run, "card:5", "A preview is open; confirm or cancel it first.");
            Refuse(run, "card:0", "A preview is open; confirm or cancel it first.");
            c.Sequence(["scissors", "card:0"], run.Actions, "a refused step is not in the save");

            var smith = New();
            Do(smith, "smith", "card:0");
            c.Equal("confirm=Confirm: upgrade Strike | cancel=Back to the cards (pick a different one)", Labels(smith.Snapshot()), "in a preview the close button is off: cancel goes back, it does not close");
            Do(smith, "cancel");
            c.Equal("card_select", smith.Snapshot().Screen, "back on the grid");

            var grid = New();
            Do(grid, "glass", "card:0", "card:1");
            Refuse(grid, "card:2", "The maximum is already selected; deselect a card first.");
            Refuse(grid, "cancel", "There is no enabled way to back out of this screen.");

            var two = New();
            Do(two, "two upgrades", "card:0");
            Refuse(two, "confirm", "The confirm button is not enabled.");

            // A preview of nothing: no pick to go back from, and the close button is off while a preview is up.
            var none = New();
            Do(none, "up to two", "confirm");
            Refuse(none, "cancel", "There is no enabled way to back out of this screen.");
            Refuse(none, "card:0", "A preview is open; confirm or cancel it first.");

            var choose = New();
            Do(choose, "tablet");
            Refuse(choose, "confirm", "Unknown card choice action 'confirm'.");
            Refuse(choose, "cancel", "Unknown card choice action 'cancel'.");
            Refuse(choose, "card:3", "There is no card 3.");
        });
    }

    // ---------- copies and the save ----------

    private static void CopiesAndSaves(Check c)
    {
        c.Group("a run is copied and saved in the middle of a card choice", () =>
        {
            var run = New();
            Do(run, "shears", "card:0", "card:9");
            c.That(run.Question is CardSelectScreen { PreviewOpen: true }, "a preview is open");
            var copy = run.Clone();
            c.Equal(run.Dump(), copy.Dump(), "the copy shows the same");
            c.That(!ReferenceEquals(((CardSelectScreen)copy.Question!).Cards[0], ((CardSelectScreen)run.Question!).Cards[0]), "but its screen lists the copy's own deck cards");
            c.That(ReferenceEquals(((CardSelectScreen)copy.Question!).Cards[0], copy.State.Deck[0]), "which are the cards of the copy's deck");

            Do(run, "confirm");
            c.Sequence(["removed: Strike, Bash"], DeskNotes(run), "the run confirms");
            c.Equal("card_select", copy.Snapshot().Screen, "the copy is still in its preview");
            Do(copy, "cancel", "card:5", "card:6", "confirm");
            c.Sequence(["removed: Defend, Defend"], DeskNotes(copy), "and goes its own way");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad", DeckNames(run), "the run lost a Strike and Bash");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad Bash", DeckNames(copy), "the copy two Defends");

            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup(), new DeskDirector(Script), run.Actions).Dump(), "the run's save replays");
            c.Equal(copy.Dump(), Arena.Run.Replay(new RunSetup(), new DeskDirector(Script), copy.Actions).Dump(), "and so does the copy's");
            c.Sequence(["shears", "card:0", "card:9", "cancel", "card:5", "card:6", "confirm"], copy.Actions, "the copy's save has every step");

            // A copy taken on the grid keeps its own picks.
            var picking = New();
            Do(picking, "shears", "card:0");
            var twin = picking.Clone();
            Do(picking, "card:0");
            c.That(picking.Snapshot().State["selected"] is null, "the run put its pick back");
            c.Equal(Q("['Strike']"), Text(twin.Snapshot().State["selected"]), "the copy still has it");
            Do(twin, "card:5");
            c.That(((CardSelectScreen)twin.Question!).PreviewOpen && !((CardSelectScreen)picking.Question!).PreviewOpen, "the copy's second pick opens the copy's preview only");

            // A longer script through every kind, replayed.
            var all = New();
            Do(all, "scissors", "card:0", "confirm", "smith", "card:8", "confirm", "new leaf", "card:4", "confirm", "mirror", "card:0", "cancel", "card:1", "confirm",
                "cheese", "card:2", "card:0", "tablet", "skip", "glass", "card:2", "confirm", "smith", "cancel", "up to two", "confirm", "confirm");
            c.Sequence(["removed: Strike", "upgraded: Bash+", "to transform: Defend", "to copy: Strike", "took: Shrug It Off, Anger", "took: nothing", "took: Inflame", "upgraded: nothing", "to transform: nothing"],
                DeskNotes(all), "every kind in one run");
            c.Equal(all.Dump(), Arena.Run.Replay(new RunSetup(), new DeskDirector(Script), all.Actions).Dump(), "and its save replays to the same state");
        });
    }

    private static void TwoQuestions(Check c)
    {
        c.Group("two choices in one action: the second screen shows the deck after the first", () =>
        {
            var run = New();
            Do(run, "both", "card:9", "confirm");
            c.Equal("card_select", run.Snapshot().Screen, "the second question is up");
            var player = Q("'player':{'character':'The Ironclad','hp':80,'max_hp':80,'gold':99,'relics':['Burning Blood'],'potions':[null,null,null],'deck_size':9}");
            c.Equal($$"""{"kind":"upgrade","prompt":"Choose a card to upgrade.","min":1,"max":1,{{Hint}},"cards":[{"i":0,{{Strike}},"copies":5},{"i":5,{{Defend}},"copies":4}],{{Floor0}},{{player}}}""",
                State(run), "Bash is gone from the grid and from the deck's size");
            c.Sequence(["removed: Bash"], DeskNotes(run), "the state is as it was when the second question came up: the first part of the action is done");
            Do(run, "card:5", "confirm");
            c.Sequence(["removed: Bash", "upgraded: Defend+"], DeskNotes(run), "both happened, once each");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad+ DefendIronclad DefendIronclad DefendIronclad", DeckNames(run), "the deck");
            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup(), new DeskDirector(Script), run.Actions).Dump(), "the save replays");
        });
    }

    // ---------- every offered action works ----------

    private static void RandomWalks(Check c)
    {
        c.Group("random walks through the choices: what is offered is accepted, the keys keep their order, and the save replays", () =>
        {
            var steps = 0;
            var questions = 0;
            var problems = new List<string>();
            for (ulong seed = 1; seed <= 120; seed++)
            {
                var rng = new GameRng(seed);
                var run = New();
                for (var n = 0; n < 60; n++)
                {
                    var snapshot = run.Snapshot();
                    if (snapshot.Screen == "card_select")
                    {
                        questions++;
                        var keys = Keys(snapshot.State);
                        if (!keys.StartsWith("kind,", StringComparison.Ordinal) || !keys.EndsWith(",run,player", StringComparison.Ordinal))
                            problems.Add($"seed {seed} step {n}: keys {keys}");
                        if (snapshot.Actions.Count == 0) problems.Add($"seed {seed} step {n}: a card choice with nothing to do");
                        if (snapshot.Actions.Select(a => a.Id).Distinct().Count() != snapshot.Actions.Count) problems.Add($"seed {seed} step {n}: an id twice");
                        if (snapshot.State["preview"] is not null && snapshot.State["hint"] is not null) problems.Add($"seed {seed} step {n}: preview and hint together");
                    }
                    var action = snapshot.Actions[rng.NextInt(snapshot.Actions.Count)];
                    var result = run.Execute(action.Id);
                    if (!result.Ok) problems.Add($"seed {seed} step {n}: '{action.Id}' ({action.Label}) was offered and refused: {result.Error}");
                    steps++;
                }
                if (seed % 10 != 0) continue;
                var replay = Arena.Run.Replay(new RunSetup(), new DeskDirector(Script), run.Actions);
                if (replay.Dump() != run.Dump()) problems.Add($"seed {seed}: the save does not replay to the same state");
                var copy = run.Clone();
                if (copy.Dump() != run.Dump()) problems.Add($"seed {seed}: a copy differs");
            }
            c.Sequence([], problems.Take(5), "no problem on any walk");
            c.That(steps == 7200 && questions > 2000, $"7200 steps, {questions} of them on a card choice");
        });
    }

    // ---------- the set ----------

    private static void TheSet(Check c)
    {
        c.Group("the selection keeps the order of a .NET HashSet", () =>
        {
            // The same adds and removes on a real HashSet, which is what the game's screens use.
            var set = new SelectionSet();
            var real = new HashSet<int>();
            var steps = new (bool Add, int Item)[]
            {
                (true, 4), (true, 7), (true, 1), (false, 7), (true, 9), (false, 4), (false, 1), (true, 2), (true, 3), (true, 8), (false, 9), (true, 7), (true, 7), (false, 5),
            };
            foreach (var (add, item) in steps)
            {
                c.Equal(add ? real.Add(item) : real.Remove(item), add ? set.Add(item) : set.Remove(item), $"{(add ? "add" : "remove")} {item}: the same answer");
                c.Sequence(real, set.InOrder(), $"after {(add ? "adding" : "removing")} {item}: the same order");
                c.Equal(real.Count, set.Count, $"after {(add ? "adding" : "removing")} {item}: the same count");
            }
            // By hand: 4 7 1; 7 out; 9 takes its place: 4 9 1; 4 and 1 out; 2 takes 1's place (freed last), 3 takes 4's: 3 9 2.
            var small = new SelectionSet();
            foreach (var item in new[] { 4, 7, 1 }) small.Add(item);
            small.Remove(7);
            small.Add(9);
            c.Sequence([4, 9, 1], small.InOrder(), "a new item takes the freed place");
            small.Remove(4);
            small.Remove(1);
            small.Add(2);
            small.Add(3);
            c.Sequence([3, 9, 2], small.InOrder(), "the place freed last is taken first");
            var copy = small.Clone();
            small.Clear();
            small.Add(5);
            c.Sequence([5], small.InOrder(), "an emptied set starts from the first place again");
            c.Sequence([3, 9, 2], copy.InOrder(), "a copy is its own set");
            c.That(copy.Contains(9) && !copy.Contains(4), "and knows what it holds");
        });
    }
}
