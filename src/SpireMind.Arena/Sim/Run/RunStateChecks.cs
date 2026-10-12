using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The run's state, the hand-over to a fight and back, copies, the save and the end screens.
/// Expected numbers are worked out by hand from the rules each line names (the cards', potions'
/// and Burning Blood's numbers are the checked content's: Strike 6, Defend 5, Bash 8 and 2
/// Vulnerable, Vulnerable times 1.5, Feed 10 and 3 max HP, 12 and 4 upgraded, Armaments 5 Block,
/// Anger 6, Fire Potion 20, Block Potion 12, Burning Blood 6); the snapshot shapes are those of
/// docs/screens.md. Nothing here is read off a run of the code it checks.
/// </summary>
public sealed class RunStateChecks : IArenaChecks
{
    public void Run(Check c)
    {
        NewRun(c);
        Belt(c);
        SingleFightsKeepTheirShape(c);
        ThreeFightsCarryThePlayer(c);
        DeckDescription(c);
        PlayerBlocks(c);
        Death(c);
        TurnLimit(c);
        ReplayAndCopies(c);
        Streams(c);
        Questions(c);
        RefusalsAndFailures(c);
        Game(c);
    }

    // ---------- helpers ----------

    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSON as compact text with "+" and quotes written as they are, as the tools send it.</summary>
    private static string Text(JsonNode? node) => node is null ? "null" : node.ToJsonString(Plain);

    private static string Keys(JsonNode? node) => node is JsonObject o ? string.Join(",", o.Select(kv => kv.Key)) : "(not an object)";

    private static string Ids(GameSnapshot snapshot) => string.Join(" ", snapshot.Actions.Select(a => a.Id));

    private static string? Label(GameSnapshot snapshot, string id) => snapshot.Actions.FirstOrDefault(a => a.Id == id)?.Label;

    private static FightRequest Dummy(string name, int hp, int hit = 0, RoomType room = RoomType.Monster) => new()
    {
        Monsters = () => [new PlacedMonster(new CheckDummy { DisplayName = name, Hp = hp, Steps = [hit > 0 ? new DummyStep("HIT", Damage: hit) : new DummyStep("IDLE")] })],
        RoomType = room,
        ShuffleDeck = false,
    };

    private static void Do(Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    /// <summary>A fixed rule that finishes any run of fights: a potion when one can be used, else the first
    /// card, else end the turn; the one action of a screen that has only one.</summary>
    private static string Pick(GameSnapshot snapshot)
    {
        var actions = snapshot.Actions;
        return (actions.FirstOrDefault(a => a.Id.StartsWith("potion:", StringComparison.Ordinal))
                ?? actions.FirstOrDefault(a => a.Id != "end_turn" && !a.Id.StartsWith("discard_potion:", StringComparison.Ordinal))
                ?? actions[^1]).Id;
    }

    // ---------- check-only relics: each keeps something from fight to fight ----------

    /// <summary>Counts the player's turns over the whole run; every third one gives 1 energy.</summary>
    private sealed class TurnCounter : Relic
    {
        public int Turns;
        public override string Name => "Turn Counter";
        public override string Describe() => $"Every third turn, gain 1 energy. {Turns} so far.";

        public override void AfterEnergyReset(Fight f)
        {
            Turns++;
            if (Turns % 3 == 0) f.GainEnergy(1);
        }
    }

    /// <summary>7 gold after every won fight, through the fight's own gold.</summary>
    private sealed class VictoryPurse : Relic
    {
        public override string Name => "Victory Purse";
        public override string Describe() => "After a won fight, gain 7 gold.";
        public override void AfterCombatVictory(Fight f) => f.Gold += 7;
    }

    /// <summary>After the first won fight: the first Defend leaves the deck and an Anger joins it. Once.</summary>
    private sealed class Shedder : Relic
    {
        private bool _done;
        public override string Name => "Shedder";
        public override string Describe() => _done ? "Used up." : "After the next won fight, lose a Defend and gain an Anger.";

        public override void AfterCombatVictory(Fight f)
        {
            if (_done) return;
            _done = true;
            f.DeckList.Remove(f.DeckCards.First(card => card.Def.ClassName == "DefendIronclad"));
            f.DeckList.Add(f.NewDeckCard(Content.Card("Anger")));
        }
    }

    // ---------- a new run ----------

    private static void NewRun(Check c)
    {
        c.Group("a new run is the Ironclad as the game makes the player", () =>
        {
            // Ironclad.cs: StartingHp 80, StartingGold 99, five Strikes, four Defends, Bash, Burning Blood;
            // Player.CreateForNewRun: three potion slots, no potion; the odds start at -0.05 and 0.4.
            var s = new RunState(new RunSetup());
            c.Equal(80, s.Hp, "HP");
            c.Equal(80, s.MaxHp, "max HP");
            c.Equal(99, s.Gold, "gold");
            c.Sequence(["StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Bash"],
                s.Deck.Select(card => card.Def.ClassName), "the deck in deck order");
            c.Sequence(Enumerable.Range(1, 10), s.Deck.Select(card => card.Id), "every deck card has an id of its own");
            c.That(s.Deck.All(card => card.Pile == PileType.Deck && !card.IsUpgraded && card.DeckCardId == 0), "deck cards are in pile Deck, not upgraded");
            c.Sequence(["BurningBlood"], s.Relics.Select(r => r.ClassName), "relics");
            c.Equal(3, s.Belt.Count, "potion slots");
            c.That(s.Belt.Slots.All(p => p is null), "the belt is empty");
            c.Equal(-0.05f, s.CardRarityOffset, "the card rarity offset");
            c.Equal(0.4f, s.PotionChance, "the potion chance");
            c.Equal(0, s.ActIndex, "act index");
            c.Equal(0, s.TotalFloor, "no map point entered yet");
            c.Equal(0, s.Ascension, "ascension");
            c.Equal("The Ironclad", s.Character, "the character's title");
        });

        c.Group("cards and relics added to the state", () =>
        {
            var s = new RunState(new RunSetup());
            var anger = s.AddCard("Anger", upgraded: true);
            c.Equal(11, anger.Id, "the next id");
            c.Equal("Anger+", anger.Title, "upgraded");
            c.Equal(11, s.Deck.Count, "at the end of the deck");
            c.That(ReferenceEquals(s.Deck[^1], anger), "the last card");
            var offer = s.NewCard(Content.Card("Feed"));
            c.Equal(12, offer.Id, "a card on offer takes an id too");
            c.Equal(11, s.Deck.Count, "and is not in the deck");
            s.PutInDeck(offer, 0);
            c.That(ReferenceEquals(s.Deck[0], offer), "put in at an index");
            c.That(s.RemoveCard(anger), "removed");
            c.That(!s.RemoveCard(anger), "not twice");
            c.Equal(null, s.CardById(11), "gone by id");
            c.That(ReferenceEquals(s.CardById(12), offer), "found by id");
            c.Equal(13, s.AddCard("Bash").Id, "an id is never used twice");

            var counter = new TurnCounter();
            s.AddRelic(counter);
            c.That(ReferenceEquals(s.FindRelic<TurnCounter>(), counter), "found by type");
            c.That(s.HasRelic("BurningBlood") && !s.HasRelic("Anchor"), "found by class name");
            s.AddRelic(new VictoryPurse(), 0);
            c.Sequence(["VictoryPurse", "BurningBlood", "TurnCounter"], s.Relics.Select(r => r.ClassName), "a relic can take a place in the row");
        });

        c.Group("a copy of the state shares nothing that changes", () =>
        {
            var s = new RunState(new RunSetup { Potions = ["FirePotion"], ExtraRelics = [() => new TurnCounter { Turns = 4 }] });
            s.Part<NotePart>().Notes.Add("first");
            var before = Text(s.Dump());
            var copy = s.Clone(out var map);
            c.Equal(before, Text(copy.Dump()), "the copy is in the same state");
            c.That(!ReferenceEquals(copy.Deck[0], s.Deck[0]) && ReferenceEquals(map.Get(s.Deck[0]), copy.Deck[0]), "deck cards are copied, and the map knows which is which");
            c.That(ReferenceEquals(map.Get(s.Relics[1]), copy.Relics[1]) && !ReferenceEquals(copy.Relics[1], s.Relics[1]), "so are relics");

            copy.Hp = 1;
            copy.Gold = 0;
            copy.CardRarityOffset = 0.2f;
            copy.PotionChance = 0.1f;
            copy.TotalFloor = 9;
            copy.Belt.Remove(0);
            copy.Belt.Grow(1);
            copy.FindRelic<TurnCounter>()!.Turns = 99;
            copy.RemoveCard(copy.Deck[0]);
            copy.AddCard("Anger");
            copy.Deck[0].Set("Damage", 50);
            copy.Part<NotePart>().Notes.Add("second");
            c.Equal(before, Text(s.Dump()), "nothing done to the copy shows in the original");
            c.That(Text(copy.Dump()) != before, "and the dump tells them apart");
            c.Sequence(["first"], s.Part<NotePart>().Notes, "a part is copied with the state");
        });
    }

    /// <summary>A piece of state another file would own.</summary>
    private sealed class NotePart : RunPart
    {
        public List<string> Notes = new();
        public override RunPart Clone(CloneMap map) => new NotePart { Notes = Notes.ToList() };
        public override JsonNode? Dump() => string.Join("|", Notes);
    }

    // ---------- the belt ----------

    private static void Belt(Check c)
    {
        c.Group("the belt (Player.AddPotionInternal, RemovePotionInternal, AddToMaxPotionCount)", () =>
        {
            var fire = Content.Potion("FirePotion");
            var block = Content.Potion("BlockPotion");
            var belt = new PotionBelt(3);
            c.That(belt.Add(fire), "into an empty belt");
            c.That(belt.Add(block, 2), "into a named slot");
            c.Sequence(["FirePotion", null, "BlockPotion"], belt.Slots.Select(p => p?.ClassName), "the first empty slot, then slot 2");
            c.That(!belt.Add(fire, 2), "a taken slot refuses");
            c.That(!belt.Add(fire, 3), "there is no slot 3");
            c.That(belt.HasOpenSlot, "slot 1 is open");
            c.That(belt.Add(block), "the first empty slot is 1");
            c.That(!belt.HasOpenSlot && !belt.Add(fire), "a full belt refuses, and nothing is pushed out");
            c.Sequence(["FirePotion", "BlockPotion", "BlockPotion"], belt.Slots.Select(p => p?.ClassName), "unchanged by the refusal");
            c.That(ReferenceEquals(belt.Remove(0), fire) && belt[0] is null, "removing empties the slot and leaves the others where they are");
            belt.Grow(2);
            c.Equal(5, belt.Count, "two more slots");
            c.Sequence([null, "BlockPotion", "BlockPotion", null, null], belt.Slots.Select(p => p?.ClassName), "new slots are empty and at the end");
            var threw = false;
            try { belt.Grow(-1); }
            catch (NotSupportedException) { threw = true; }
            c.That(threw, "shrinking is left out by name");
            var copy = belt.Clone();
            copy.Remove(1);
            c.That(belt[1] is not null, "a copy is its own belt");
        });
    }

    // ---------- the single fight is what it was ----------

    private static void SingleFightsKeepTheirShape(Check c)
    {
        c.Group("a single fight has nothing of a run", () =>
        {
            var f = CheckKit.VsDummy();
            c.Equal(0, f.DeckCards.Count, "no deck cards of its own");
            c.Equal(0, f.Gold, "no gold");
            c.Equal(3, f.Potions.Count, "three belt slots");
            c.That(f.AllCards.All(card => card.DeckCardId == 0 && f.DeckVersionOf(card) is null), "no card points at a deck card");
            c.Sequence(["Burning Blood"], f.Relics.Select(r => r.Name), "Burning Blood alone");
            c.Equal(10, f.Deck.Count, "the deck it started with is still listed");
            c.Equal(0, (int)f.DescribeDeck()["gold"]!, "get_deck of a single fight says 0 gold, as before");
            var copy = f.Clone();
            copy.Gold = 5;
            c.Equal(0, f.Gold, "gold is copied with the fight");
        });
    }

    // ---------- three fights in a fixed order ----------

    private static RunSetup CarrySetup() => new()
    {
        Seed = "CARRY",
        // Deck order is draw order here (the fights do not shuffle): turn 1 draws the first five, turn 2 the next five.
        Deck =
        [
            new("Bash"), new("StrikeIronclad"), new("StrikeIronclad"), new("DefendIronclad"), new("DefendIronclad"),
            new("Feed"), new("StrikeIronclad"), new("StrikeIronclad"), new("DefendIronclad"), new("Armaments"),
        ],
        PotionSlots = 4,
        Potions = ["FirePotion", null, "BlockPotion"],
        ExtraRelics = [() => new TurnCounter(), () => new VictoryPurse(), () => new Shedder()],
    };

    private static FixedFights CarryFights() => new(Dummy("Left", 40, hit: 17), Dummy("Right", 30, hit: 10), Dummy("Last", 18));

    private static readonly string[] CarryActions =
    [
        // Fight 1, turn 1 (3 energy): Fire Potion 20 (40 -> 20), Bash 8 (-> 12, 2 Vulnerable), Defend (5 Block);
        // the dummy hits for 17, 5 blocked: 80 -> 68. Vulnerable ticks to 1.
        "potion:0>e0", "play:0>e0", "play:2", "end_turn",
        // Turn 2: Armaments (5 Block) asks which of Feed, Strike, Strike, Defend to upgrade: Feed. Feed+ deals
        // 12 x 1.5 = 18 to the last 12 HP: fatal, so 4 max HP (84) and 4 HP (72). Burning Blood: 78.
        "play:4", "select:0", "confirm", "play:0>e0",
        // Fight 2, turn 1 (the run's third turn: 4 energy): Bash 8 (30 -> 22), Strike 9 (-> 13), Strike 9 (-> 4);
        // the dummy hits for 10: 78 -> 68.
        "play:0>e0", "play:0>e0", "play:0>e0", "end_turn",
        // Turn 2: Block Potion, then Strike 9 kills. Burning Blood: 74.
        "potion:2", "play:0>e0",
        // Fight 3: nothing on turn 1. Turn 2 (the run's sixth: 4 energy): Anger 6 (18 -> 12) and its copy, Strike 6, Strike 6.
        "end_turn", "play:4>e0", "play:0>e0", "play:0>e0",
    ];

    private static void ThreeFightsCarryThePlayer(Check c)
    {
        c.Group("three fights in a fixed order carry HP, max HP, gold, potions, relic state and the deck", () =>
        {
            var run = new Run(CarrySetup(), CarryFights());

            // ----- fight 1, as the run hands it over -----
            var f1 = run.Fight!;
            var first = run.Snapshot();
            c.Equal("combat", first.Screen, "the run starts in its first fight");
            c.Equal("""{"act":1,"floor":1,"ascension":0,"room":"Monster"}""", Text(first.State["run"]), "the run block of fight 1");
            c.Equal("""{"name":"The Ironclad","hp":80,"max_hp":80}""", Text(first.State["me"]), "the player enters at 80/80");
            c.Equal(3, (int)first.State["energy"]!, "turn 1 of the run: 3 energy");
            c.Sequence(["Bash", "Strike", "Strike", "Defend", "Defend"], f1.HandTitles(), "the deck in deck order is the draw order");
            c.Sequence([1, 2, 3, 4, 5], f1.Hand.Select(card => card.DeckCardId), "each combat card points at its deck card");
            c.Sequence([6, 7, 8, 9, 10], f1.DrawPile.Select(card => card.DeckCardId), "the draw pile too");
            c.That(f1.AllCards.All(card => f1.DeckVersionOf(card) is { } deck && deck.Def == card.Def && deck.Pile == PileType.Deck), "the deck version of every card is in the fight's deck");
            c.Equal(10, f1.DeckCards.Count, "the fight has the whole deck");
            c.That(f1.DeckCards.Zip(run.State.Deck).All(pair => !ReferenceEquals(pair.First, pair.Second) && pair.First.Id == pair.Second.Id), "as its own copies, with the run's ids");
            c.Equal(4, f1.Potions.Count, "the belt has the run's four slots");
            c.Equal("""[{"slot":0,"name":"Fire Potion","text":"Deal 20 damage to an enemy. Strength, Weak and Vulnerable do not change it.","target":"AnyEnemy","usable":true},null,{"slot":2,"name":"Block Potion","text":"Gain 12 Block. Dexterity and Frail do not change it.","target":"AnyPlayer","usable":true},null]""",
                Text(first.State["potions"]), "the belt in the snapshot");
            c.Equal("""["Burning Blood","Turn Counter","Victory Purse","Shedder"]""", Text(first.State["relics"]), "the relics in pickup order");
            c.Equal(99, f1.Gold, "the gold goes in");
            c.That(f1.Relics.Zip(run.State.Relics).All(pair => !ReferenceEquals(pair.First, pair.Second)), "the fight works on its own relic objects");

            Do(run, CarryActions[..4]);
            c.Equal(68, run.Hp, "after the dummy's 17 into 5 Block: 68, read live from the fight");
            c.Equal(80, run.State.Hp, "the state is the player as the fight began until the fight hands back");
            c.Equal(68, (int)run.DescribeDeck()["hp"]!, "get_deck shows the live HP");
            c.Equal("""["Block Potion"]""", Text(new JsonArray(run.DescribeDeck()["potions"]!.AsArray().Select(p => (JsonNode?)(string)p!["name"]!).ToArray())), "and the belt without the Fire Potion that was used");

            Do(run, "play:4");
            var choice = run.Snapshot();
            c.Equal("hand_select", choice.Screen, "Armaments asks through the fight's own screen");
            c.Equal("select:0 select:1 select:2 select:3", Ids(choice), "Feed, Strike, Strike, Defend can be upgraded");
            Do(run, "select:0", "confirm");
            c.Equal("Play Feed+ on Left", Label(run.Snapshot(), "play:0>e0"), "Feed is upgraded for this fight");
            c.That(!f1.DeckCards.Any(card => card.IsUpgraded), "the deck card is not");
            c.That(run.Fight is not null && run.LastFight is null, "the fight is still on");
            Do(run, "play:0>e0");

            // ----- between fight 1 and fight 2 -----
            var won = run.LastFight!;
            c.Equal(new FightOutcome(true, false, "Custom", RoomType.Monster, 1, 2, 80, 78, 84, 12, 1, 0, "Left"), won, "how fight 1 ended");
            var f2 = run.Fight!;
            c.That(!ReferenceEquals(f1, f2), "the next fight has begun");
            c.Equal(78, run.State.Hp, "HP: 68, plus 4 from Feed+, plus Burning Blood's 6");
            c.Equal(84, run.State.MaxHp, "max HP: 80 plus Feed+'s 4");
            c.Equal(106, run.State.Gold, "gold: 99 plus the purse's 7");
            c.Sequence([null, null, "BlockPotion", null], run.State.Belt.Slots.Select(p => p?.ClassName), "the belt: the Fire Potion is gone, four slots still");
            c.Equal(2, run.State.FindRelic<TurnCounter>()!.Turns, "the counter comes back with its two turns");
            c.Sequence([1, 2, 3, 5, 6, 7, 8, 9, 10, 11], run.State.Deck.Select(card => card.Id), "the deck: Defend 4 left it, card 11 joined");
            c.Equal("""{"Anger":1,"Armaments":1,"Bash":1,"Defend":2,"Feed":1,"Strike":4}""", Text(RunDescribe.DeckCounts(run.State.Deck)), "by name: no Feed+, the upgrade ended with the fight");

            var second = run.Snapshot();
            c.Equal("""{"act":1,"floor":2,"ascension":0,"room":"Monster"}""", Text(second.State["run"]), "fight 2 is on floor 2");
            c.Equal("""{"name":"The Ironclad","hp":78,"max_hp":84}""", Text(second.State["me"]), "the player enters fight 2 at 78/84 with no Block and no powers");
            c.Equal(4, (int)second.State["energy"]!, "the run's third turn: the counter gives 1 energy");
            c.Equal(3, (int)second.State["max_energy"]!, "of 3");
            c.Sequence(["Bash", "Strike", "Strike", "Defend", "Feed"], f2.HandTitles(), "the new deck order is the draw order");
            c.Sequence([1, 2, 3, 5, 6], f2.Hand.Select(card => card.DeckCardId), "with the deck cards' ids");
            c.Equal("Deal 10 damage. If this kills an enemy that is not a Minion, gain 3 Max HP. / Exhaust.", (string)second.State["hand"]![4]!["text"]!, "Feed is back, not upgraded, though it was exhausted in fight 1");
            c.Equal("""[null,null,{"slot":2,"name":"Block Potion","text":"Gain 12 Block. Dexterity and Frail do not change it.","target":"AnyPlayer","usable":true},null]""",
                Text(second.State["potions"]), "the belt of fight 2");
            c.Equal(106, f2.Gold, "the gold goes into fight 2");
            c.Equal("Every third turn, gain 1 energy. 3 so far.", (string)run.DescribeDeck()["relics"]![1]!["text"]!, "get_deck shows the relic as the fight has it");
            c.That(f1.Seed != f2.Seed, "each fight has streams of its own");

            Do(run, CarryActions[8..12]);
            c.Equal(68, run.Hp, "the dummy's 10: 68");
            Do(run, "potion:2");
            c.Equal(12, run.Fight!.Player.Block, "Block Potion");
            Do(run, "play:0>e0");

            // ----- between fight 2 and fight 3 -----
            c.Equal(new FightOutcome(true, false, "Custom", RoomType.Monster, 2, 2, 78, 74, 84, 10, 1, 0, "Right"), run.LastFight, "how fight 2 ended");
            c.Equal(74, run.State.Hp, "HP: 68 plus Burning Blood's 6");
            c.Equal(113, run.State.Gold, "gold: 7 more");
            c.That(run.State.Belt.Slots.All(p => p is null) && run.State.Belt.Count == 4, "an empty belt of four");
            c.Equal(4, run.State.FindRelic<TurnCounter>()!.Turns, "four turns counted");
            c.Sequence([1, 2, 3, 5, 6, 7, 8, 9, 10, 11], run.State.Deck.Select(card => card.Id), "the deck did not change again: the relic remembers it is used up");

            var f3 = run.Fight!;
            c.Equal("""{"name":"The Ironclad","hp":74,"max_hp":84}""", Text(run.Snapshot().State["me"]), "fight 3: 74/84, and the 12 Block of fight 2 is gone");
            c.Equal(3, f3.Energy, "the run's fifth turn: 3 energy");
            Do(run, "end_turn");
            c.Equal(4, f3.Energy, "the sixth: 4");
            c.Sequence(["Strike", "Strike", "Defend", "Armaments", "Anger"], f3.HandTitles(), "the Anger that joined the deck in fight 1 is drawn");
            c.Equal(11, f3.Hand[4].DeckCardId, "as deck card 11");
            Do(run, "play:4>e0");
            c.Sequence(["Anger", "Anger"], f3.DiscardPile.Skip(5).Select(card => card.Title), "Anger and its copy lie on the discard pile");
            var angers = f3.DiscardPile.Where(card => card.Def.ClassName == "Anger").ToList();
            c.Sequence([0, 11], angers.Select(card => card.DeckCardId).Order(), "the copy has no deck card");
            c.That(angers.Count(card => f3.DeckVersionOf(card) is not null) == 1, "only the original has a deck version");
            Do(run, "play:0>e0", "play:0>e0");

            // ----- the end -----
            c.That(run.IsOver && run.AtEnd && run.Fight is null, "the run is over");
            c.Equal(RunResult.Cleared, run.Result, "cleared");
            c.Equal(80, run.State.Hp, "HP: 74 plus 6");
            c.Equal(84, run.State.MaxHp, "max HP");
            c.Equal(120, run.State.Gold, "gold");
            c.Equal(6, run.State.FindRelic<TurnCounter>()!.Turns, "six turns in all");
            c.Equal(10, run.State.Deck.Count, "ten cards: the copy of Anger was the fight's");
            c.Sequence(CarryActions, run.Actions, "the save is the actions in order");

            var end = run.Snapshot();
            c.Equal("arena_over", end.Screen, "the end snapshot");
            c.That(end.AwaitingDecision && end.Waiting is null, "a decision, not a busy snapshot");
            c.Equal("""{"result":"cleared","run":{"act":1,"floor":3,"ascension":0},"me":{"name":"The Ironclad","hp":80,"max_hp":84},"summary":"Cleared the act on floor 3 with 80/84 HP, 120 gold, 10 cards and 4 relics."}""",
                Text(end.State), "its state");
            c.Equal("done", Ids(end), "one action");
            c.Equal("The run is over: you cleared the act. Nothing is left to do; stop here.", Label(end, "done"), "its label");

            var dump = run.Dump();
            c.That(run.Execute("done").Ok, "done runs");
            c.Equal(dump, run.Dump(), "and changes nothing");
            c.Equal(CarryActions.Length, run.Actions.Count, "it is not part of the save");
            var refused = run.Execute("end_turn");
            c.That(!refused.Ok, "anything else is refused");
            c.Equal("The run is over; 'end_turn' cannot run. The only action left is 'done'.", refused.Error, "with the reason");

            // ----- the same run from its save -----
            var replayed = Arena.Run.Replay(CarrySetup(), CarryFights(), CarryActions);
            c.Equal(dump, replayed.Dump(), "replaying the action list gives the same end state");
        });
    }

    // ---------- get_deck and the shared blocks ----------

    private static void DeckDescription(Check c)
    {
        c.Group("get_deck has the mod's shape (docs/screens.md, get_deck)", () =>
        {
            var setup = new RunSetup { Deck = [new("StrikeIronclad"), new("DefendIronclad"), new("Bash", true)], Potions = [null, "FirePotion"] };
            var run = new Run(setup, new FixedFights(Dummy("Dummy", 100)));
            c.Equal("""{"character":"The Ironclad","hp":80,"max_hp":80,"gold":99,"run":{"act":1,"floor":1,"ascension":0},"deck_size":3,"deck":[{"name":"Strike","cost":"1","type":"Attack","text":"Deal 6 damage.","rarity":"Basic"},{"name":"Defend","cost":"1","type":"Skill","text":"Gain 5 Block.","rarity":"Basic"},{"name":"Bash+","cost":"2","type":"Attack","text":"Deal 10 damage. Apply 3 Vulnerable.","rarity":"Basic","upgraded":true}],"relics":[{"name":"Burning Blood","text":"At the end of combat, heal 6 HP."}],"potions":[{"name":"Fire Potion","text":"Deal 20 damage to an enemy. Strength, Weak and Vulnerable do not change it.","target":"AnyEnemy"}]}""",
                Text(run.DescribeDeck()), "during a fight");
            c.That(!ReferenceEquals(run.DescribeDeck(), run.DescribeDeck()), "a new object every call");
        });
    }

    private static void PlayerBlocks(Check c)
    {
        c.Group("the run and player blocks (docs/screens.md, Shared blocks)", () =>
        {
            var run = new Run(new RunSetup { Potions = ["FirePotion"], Deck = [new("StrikeIronclad"), new("Bash", true), new("StrikeIronclad")] }, new Idle());
            c.Equal("""{"act":1,"floor":0,"ascension":0}""", Text(run.RunJson()), "run: act from 1, the total floor, the ascension");
            run.State.TotalFloor = 7;
            run.State.Gold = 141;
            run.State.Hp = 72;
            c.Equal("""{"act":1,"floor":7,"ascension":0}""", Text(run.RunJson()), "the floor is the state's total floor");
            c.Equal("""{"character":"The Ironclad","hp":72,"max_hp":80,"gold":141,"relics":["Burning Blood"],"potions":["Fire Potion",null,null],"deck_size":3,"deck":{"Bash+":1,"Strike":2}}""",
                Text(run.PlayerJson()), "the full form: card_reward and game_over");
            c.Equal("character,hp,max_hp,gold,relics,deck_size,deck", Keys(run.PlayerJson(PlayerKeys.NoPotions)), "map, rewards, treasure, event: no potions");
            c.Equal("character,hp,max_hp,relics,deck_size,deck", Keys(run.PlayerJson(PlayerKeys.Hp | PlayerKeys.Deck)), "shop: gold is at top level");
            c.Equal("character,gold,relics,deck_size,deck", Keys(run.PlayerJson(PlayerKeys.Gold | PlayerKeys.Deck)), "rest: hp and max_hp are at top level");
            // docs/screens.md, Card selection, Context: "On a grid that lists every card of the deck, deck is left
            // out and only deck_size is added, at the end" (CardSelectReader.AddContext in the mod).
            c.Equal("""{"character":"The Ironclad","hp":72,"max_hp":80,"gold":141,"relics":["Burning Blood"],"potions":["Fire Potion",null,null],"deck_size":3}""",
                Text(run.PlayerJson(PlayerKeys.FullWithoutDeck)), "card_select over the whole deck: the deck is left out, deck_size stays, at the end");
            c.Equal("character,hp,max_hp,gold,relics,potions", Keys(run.PlayerJson(PlayerKeys.Full & ~PlayerKeys.Deck)), "without either deck key the block ends with the potions");
            c.Equal(Text(run.PlayerJson()), Text(run.PlayerJson(PlayerKeys.Full | PlayerKeys.DeckSize)), "deck_size is written once when both deck keys are asked for");
            c.Equal("""[{"slot":0,"name":"Fire Potion","text":"Deal 20 damage to an enemy. Strength, Weak and Vulnerable do not change it.","target":"AnyEnemy","usable":false},null,null]""",
                Text(run.PotionsJson()), "the detailed potions: a fight potion is not usable off a fight");
            c.Equal(true, (bool)run.PotionsJson((slot, potion) => slot == 0 && potion.ClassName == "FirePotion")[0]!["usable"]!, "the screen says which slot can be used");
            c.Equal("""{"name":"Bash+","cost":"2","type":"Attack","text":"Deal 10 damage. Apply 3 Vulnerable.","rarity":"Basic"}""", Text(RunDescribe.CardOffer(run.State.Deck[1])), "a card on offer");
            c.Equal("""{"name":"Burning Blood","text":"At the end of combat, heal 6 HP."}""", Text(RunDescribe.Relic(run.State.Relics[0])), "a relic");
            c.Equal("""{"name":"Fire Potion","text":"Deal 20 damage to an enemy. Strength, Weak and Vulnerable do not change it.","target":"AnyEnemy"}""", Text(RunDescribe.Potion(run.State.Belt[0]!)), "a potion");
        });
    }

    /// <summary>A director that puts up one screen with nothing to do but end the run.</summary>
    private sealed class Idle : RunDirector
    {
        public override void Begin(Run run) => run.Push(new EventLike());
        public override void AfterFightWon(Run run, FightOutcome fight) => run.End(RunResult.Cleared);
    }

    // ---------- death and the turn limit ----------

    private static void Death(Check c)
    {
        c.Group("a death ends the run: the mod's game over screen, then the end snapshot", () =>
        {
            // The starter deck, 80 HP; an elite dummy that hits for 50 a turn: 30 after turn 1, dead in turn 2.
            var setup = new RunSetup { Potions = [null, "BlockPotion"] };
            var fights = new FixedFights(Dummy("Brute", 100, hit: 50, room: RoomType.Elite), Dummy("Never", 1));
            var run = new Run(setup, fights);
            Do(run, "end_turn");
            c.Equal(30, run.Hp, "30 HP after the first hit");
            c.That(!run.IsOver, "still running");
            Do(run, "end_turn");

            c.That(run.IsOver && !run.AtEnd, "decided, with one screen to go");
            c.Equal(RunResult.Died, run.Result, "died");
            c.Equal(null, run.Fight, "no fight is on");
            c.Equal(new FightOutcome(false, false, "Custom", RoomType.Elite, 1, 2, 80, 0, 80, 80, 1, 0, "Brute"), run.LastFight, "how the fight ended");
            var over = run.Snapshot();
            c.Equal("game_over", over.Screen, "the game over screen");
            c.That(over.AwaitingDecision, "a decision");
            c.Equal("""{"result":"defeat","run":{"act":1,"floor":1,"ascension":0},"room":"Elite","player":{"character":"The Ironclad","hp":0,"max_hp":80,"gold":99,"relics":["Burning Blood"],"potions":[null,"Block Potion",null],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}}""",
                Text(over.State), "its state in the reader's key order, without the score");
            c.Equal("continue", Ids(over), "one action");
            c.Equal("Continue to the next page of the game over screen", Label(over, "continue"), "the reader's label for the first page");
            var refused = run.Execute("proceed");
            c.That(!refused.Ok, "another id is refused");
            c.Equal("Unknown game over action 'proceed'.", refused.Error, "in the reader's words");

            Do(run, "continue");
            c.That(run.AtEnd, "the end snapshot is up");
            var end = run.Snapshot();
            c.Equal("arena_over", end.Screen, "arena_over");
            c.Equal("""{"result":"died","run":{"act":1,"floor":1,"ascension":0},"me":{"name":"The Ironclad","hp":0,"max_hp":80},"summary":"Died on floor 1 against Brute (Elite, turn 2)."}""",
                Text(end.State), "its state");
            c.Equal("The run is over: you died. Nothing is left to do; stop here.", Label(end, "done"), "the label of done");
            c.Sequence(["end_turn", "end_turn", "continue"], run.Actions, "the save");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, fights, run.Actions).Dump(), "the replay ends in the same place");
        });
    }

    private static void TurnLimit(Check c)
    {
        c.Group("a fight that runs into the turn limit is lost and ends the run (house rule)", () =>
        {
            var run = new Run(new RunSetup { MaxTurns = 2 }, new FixedFights(Dummy("Wall", 1000)));
            Do(run, "end_turn");
            c.That(!run.IsOver, "one turn is allowed");
            Do(run, "end_turn");
            c.Equal(RunResult.Died, run.Result, "lost");
            c.Equal("a fight was not won within 2 turns", run.ResultReason, "with the reason");
            c.That(run.LastFight is { Won: false, TimedOut: true, HpAfter: 80 }, "by the limit, not by dying");
            c.Equal("game_over", run.Snapshot().Screen, "the game over screen first");
            c.Equal(80, (int)run.Snapshot().State["player"]!["hp"]!, "with the HP that was left");
            Do(run, "continue");
            c.Equal("""{"result":"died","run":{"act":1,"floor":1,"ascension":0},"me":{"name":"The Ironclad","hp":80,"max_hp":80},"summary":"Lost to the turn limit on floor 1 against Wall (Monster, turn 2).","reason":"a fight was not won within 2 turns"}""",
                Text(run.Snapshot().State), "the end snapshot says why");
        });
    }

    // ---------- replay and copies ----------

    private static void ReplayAndCopies(Check c)
    {
        foreach (var seed in new[] { "REPLAY-A", "REPLAY-B", "REPLAY-C" })
        {
            c.Group($"a run of the game's encounters replays from its save and copies exactly ({seed})", () =>
            {
                RunSetup Setup() => new()
                {
                    Seed = seed, Potions = ["FirePotion", "BlockPotion", "FirePotion"],
                    Deck = RunSetup.IroncladDeck.Concat([new DeckCard("Armaments"), new DeckCard("Anger"), new DeckCard("Feed")]).ToList(),
                    ExtraRelics = [() => new TurnCounter(), () => new VictoryPurse(), () => new Shedder()],
                };
                FixedFights Fights() => new(
                    new FightRequest { Encounter = "NibbitsWeak" }, new FightRequest { Encounter = "SlimesWeak" },
                    new FightRequest { Encounter = "ShrinkerBeetleWeak" }, new FightRequest { Encounter = "NibbitsNormal" });

                // The run, with a copy taken before every seventh action.
                var run = new Run(Setup(), Fights());
                var copies = new List<(int At, Run Copy, string Dump)>();
                var seen = new List<string>();
                for (var n = 0; n < 5000 && !run.AtEnd; n++)
                {
                    var snapshot = run.Snapshot();
                    if (n % 7 == 3 || snapshot.Screen != "combat") copies.Add((n, run.Clone(), run.Dump()));
                    seen.Add(Text(snapshot.ToJson()));
                    Do(run, Pick(snapshot));
                }
                seen.Add(Text(run.Snapshot().ToJson()));
                var end = run.Dump();
                c.That(run.AtEnd && run.Result is RunResult.Cleared or RunResult.Died, $"the run ended ({run.Result})");
                c.That(run.Actions.Count >= 20, $"after {run.Actions.Count} actions");
                c.Equal(seen.Count - 1, run.Actions.Count, "every accepted action is in the save");
                c.That(copies.Count >= 3, "copies were taken on the way");
                c.That(copies.Any(copy => copy.Copy.Fight is { HasPendingChoice: true }) || !seen.Any(s => s.Contains("\"hand_select\"", StringComparison.Ordinal)),
                    "one of them in the middle of a card choice, if the run had any");

                // Playing on did not touch the copies; each copy goes the same way as the run did.
                foreach (var (at, copy, dump) in copies)
                {
                    c.Equal(dump, copy.Dump(), $"the copy taken before action {at} is still where it was");
                    c.Sequence(run.Actions.Take(at), copy.Actions, $"with the save up to action {at}");
                    foreach (var id in run.Actions.Skip(at)) Do(copy, id);
                    c.Equal(end, copy.Dump(), $"and played on with the same actions it ends in the same state (from action {at})");
                }

                // The replay shows the same snapshot before every action and ends in the same state.
                var replay = new Run(Setup(), Fights());
                var same = true;
                for (var n = 0; n < run.Actions.Count; n++)
                {
                    same &= Text(replay.Snapshot().ToJson()) == seen[n];
                    Do(replay, run.Actions[n]);
                }
                c.That(same, "the replay shows the same snapshot before every action");
                c.Equal(end, replay.Dump(), "and ends in the same state");
                c.Equal(end, Arena.Run.Replay(Setup(), Fights(), run.Actions).Dump(), "Run.Replay does the same in one call");

                var other = new Run(new RunSetup { Seed = seed + "x", Potions = Setup().Potions, Deck = Setup().Deck, ExtraRelics = Setup().ExtraRelics }, Fights());
                c.That(other.Fight!.Seed != new Run(Setup(), Fights()).Fight!.Seed, "another seed gives other fights");
            });
        }

        c.Group("a save that does not fit is refused", () =>
        {
            var threw = "";
            try { Arena.Run.Replay(new RunSetup(), new FixedFights(Dummy("Dummy", 100)), ["end_turn", "play:9", "end_turn"]); }
            catch (InvalidOperationException ex) { threw = ex.Message; }
            c.That(threw.StartsWith("The save does not fit the run: action 1 ('play:9') was refused:", StringComparison.Ordinal), $"with the action that failed ({threw})");
        });
    }

    // ---------- the fight's random streams ----------

    /// <summary>Two fights on one floor, as an event's fight and the room's own would be.</summary>
    private sealed class TwoOnOneFloor : RunDirector
    {
        private int _fights;

        public override void Begin(Run run)
        {
            run.State.TotalFloor = 3;
            AfterFightWon(run, null!);
        }

        public override void AfterFightWon(Run run, FightOutcome fight)
        {
            if (_fights++ < 2) run.StartFight(new FightRequest { Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 5 })] });
            else run.End(RunResult.Cleared);
        }
    }

    private static void Streams(Check c)
    {
        c.Group("a fight's streams: from a seed, lent by the caller, and handed back", () =>
        {
            // The same streams shuffle the same deck the same way: a single fight with seed 77 is the yardstick.
            var yardstick = new FightBuilder().Seed(77).Enemy(new CheckDummy { Hp = 5 }).Start();
            FightRequest Request(int? seed, RngSet? rng) => new() { Monsters = () => [new PlacedMonster(new CheckDummy { Hp = 5 })], Seed = seed, Rng = rng };

            var seeded = new Run(new RunSetup(), new FixedFights(Request(77, null)));
            c.Sequence(yardstick.HandTitles(), seeded.Fight!.HandTitles(), "a request's seed is the fight's seed");
            c.Equal(77, seeded.Fight!.Seed, "as the fight says");

            var lent = new RngSet(77);
            var run = new Run(new RunSetup(), new FixedFights(Request(null, lent)));
            c.Sequence(yardstick.HandTitles(), run.Fight!.HandTitles(), "lent streams are the fight's streams");
            c.Equal(null, run.LastFightRng, "nothing to hand back before a fight ended");

            // Ten cards: the opening shuffle takes nine numbers. Five cards are drawn, and at most four
            // of them are Defends, so an Attack is in hand; any of them kills the 5 HP dummy on turn 1.
            Do(run, run.LegalActions().First(a => a.Id.EndsWith(">e0", StringComparison.Ordinal)).Id);
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "won on turn 1");
            c.Equal(9, run.LastFightRng!.Shuffle.Counter, "the streams come back as the fight left them: nine numbers of the shuffle stream used");

            var plain = new Run(new RunSetup { Seed = "STREAMS" }, new FixedFights(Request(null, null)));
            var again = new Run(new RunSetup { Seed = "STREAMS" }, new FixedFights(Request(null, null)));
            c.Equal(plain.Fight!.Seed, again.Fight!.Seed, "without either, the seed follows from the run's seed");
        });

        c.Group("two fights on one floor have streams of their own", () =>
        {
            var run = new Run(new RunSetup(), new TwoOnOneFloor());
            var first = run.Fight!.Seed;
            c.Equal(3, run.Fight!.Floor, "the first fight on floor 3");
            Do(run, run.LegalActions().First(a => a.Id.EndsWith(">e0", StringComparison.Ordinal)).Id);
            c.Equal(3, run.Fight!.Floor, "the second fight on floor 3 too");
            c.That(run.Fight!.Seed != first, "with another seed");
        });
    }

    // ---------- questions asked in the middle of an action ----------

    /// <summary>A screen outside a fight whose actions ask the player things, change the state, or fail.</summary>
    private sealed class EventLike : RunScreen
    {
        public int Done;
        public string Note = "";

        public override string Name => "event";

        public override GameSnapshot Snapshot(Run run) => new()
        {
            Screen = Name, AwaitingDecision = true,
            State = new JsonObject { ["gold"] = run.Gold, ["done"] = Done, ["note"] = Note },
            Actions =
            [
                new LegalAction("go", "Go"), new LegalAction("twice", "Twice"), new LegalAction("boom", "Boom"), new LegalAction("late boom", "Late boom"),
                new LegalAction("fight", "Fight"), new LegalAction("finish", "Finish"),
                new LegalAction("missing", "Missing"), new LegalAction("late missing", "Late missing"),
            ],
        };

        public override ActionResult Execute(Run run, string actionId)
        {
            switch (actionId)
            {
                case "go":
                    // What is done before the question must not be done twice when the action runs again.
                    run.State.Gold += 1;
                    if (run.Ask(new PickOne(3)) is not { } answer)
                    {
                        Note = "backed out";
                        return ActionResult.Success;
                    }
                    run.State.Gold += 10 * answer[0];
                    Done++;
                    Note = "went";
                    return ActionResult.Success;
                case "twice":
                    var first = run.Ask(new PickOne(3));
                    var second = run.Ask(new PickOne(2));
                    run.State.Gold += 100 * (first?[0] ?? 0) + 10 * (second?[0] ?? 0);
                    Done++;
                    return ActionResult.Success;
                case "boom":
                    run.State.Gold = 0;
                    Done = 77;
                    throw new InvalidOperationException("boom");
                case "late boom":
                    // Fails only once the question is answered: on the second run of the action.
                    run.State.Gold += 5;
                    run.Ask(new PickOne(2));
                    throw new InvalidOperationException("late boom");
                case "missing":
                    // A roll that can only land on something the arena has not built, after the action changed things.
                    run.State.Gold = 0;
                    Done = 88;
                    throw new RunUnrepresentableException("a check's roll on floor 0 can only land on cards the arena leaves out (NeverBuilt)");
                case "late missing":
                    // The same, met only once the question is answered.
                    run.State.Gold += 5;
                    run.Ask(new PickOne(2));
                    throw new RunUnrepresentableException("a check's pick on floor 0 landed on the card NeverBuilt, which the arena leaves out");
                case "fight":
                    run.StartFight(Dummy("Guard", 5));
                    return ActionResult.Success;
                case "finish":
                    run.End(RunResult.Cleared);
                    return ActionResult.Success;
                default:
                    return ActionResult.Fail($"Unknown event action '{actionId}'.");
            }
        }

        public override RunScreen Clone(CloneMap map) => new EventLike { Done = Done, Note = Note };
    }

    /// <summary>A question with two steps, as a deck card choice has: a pick, then confirm; or back out.</summary>
    private sealed class PickOne(int options) : RunScreen
    {
        private int _picked = -1;

        public override string Name => "card_select";

        public override GameSnapshot Snapshot(Run run)
        {
            var actions = Enumerable.Range(0, options).Select(n => new LegalAction($"card:{n}", $"Pick {n}")).ToList();
            if (_picked >= 0) actions.Add(new LegalAction("confirm", "Confirm"));
            actions.Add(new LegalAction("cancel", "Back"));
            return new GameSnapshot { Screen = Name, AwaitingDecision = true, State = new JsonObject { ["picked"] = _picked, ["gold"] = run.Gold }, Actions = actions };
        }

        public override ActionResult Execute(Run run, string actionId)
        {
            if (actionId == "cancel") return run.Answer(null);
            if (actionId == "confirm") return _picked >= 0 ? run.Answer([_picked]) : ActionResult.Fail("The confirm button is not enabled yet.");
            if (actionId.StartsWith("card:", StringComparison.Ordinal) && int.TryParse(actionId.AsSpan(5), out var n) && n >= 0 && n < options)
            {
                _picked = n;
                return ActionResult.Success;
            }
            return ActionResult.Fail($"Unknown card selection action '{actionId}'.");
        }

        public override RunScreen Clone(CloneMap map) => new PickOne(options) { _picked = _picked };
    }

    /// <summary>After its one fight it asks which of two prizes to take: 100 or 200 gold.</summary>
    private sealed class PrizeAfterFight : RunDirector
    {
        public override void Begin(Run run)
        {
            run.State.TotalFloor = 1;
            run.StartFight(Dummy("Guard", 5, hit: 9));
        }

        public override void AfterFightWon(Run run, FightOutcome fight)
        {
            if (run.Ask(new PickOne(2)) is { } answer) run.State.Gold += 100 * (answer[0] + 1);
            run.End(RunResult.Cleared);
        }
    }

    private static void Questions(Check c)
    {
        c.Group("an action outside a fight asks the player and is run again with the answer", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            c.Equal("event", run.Snapshot().Screen, "the director's first screen");
            c.Equal(0, run.Actions.Count, "nothing in the save yet");

            Do(run, "go");
            c.Equal("card_select", run.Snapshot().Screen, "the question's screen shows");
            c.That(run.Question is PickOne && run.Top is EventLike, "over the screen that asked");
            c.Equal("card:0 card:1 card:2 cancel", Ids(run.Snapshot()), "its actions");
            c.Equal(100, run.Gold, "the state is as it was when the question came up: the 1 gold is paid");
            c.That(!run.Execute("confirm").Ok, "the question refuses what is not on offer");
            Do(run, "card:2");
            c.Equal("card:0 card:1 card:2 confirm cancel", Ids(run.Snapshot()), "a pick is a step of the question, not the answer");
            c.Equal(100, run.Gold, "nothing has run again yet");
            Do(run, "confirm");
            c.Equal("event", run.Snapshot().Screen, "the question is closed");
            c.Equal(null, run.Question, "none is open");
            c.Equal(120, run.Gold, "99 + 1 + 10 x 2: the action ran once from its start, the 1 gold was not paid twice");
            c.Equal("""{"gold":120,"done":1,"note":"went"}""", Text(run.Snapshot().State), "the screen's own state took part");
            c.Sequence(["go", "card:2", "confirm"], run.Actions, "the question's steps are in the save");

            Do(run, "go", "cancel");
            c.Equal("""{"gold":121,"done":1,"note":"backed out"}""", Text(run.Snapshot().State), "backing out is an answer too: the action sees null");

            Do(run, "twice", "card:1", "confirm");
            c.Equal("card_select", run.Snapshot().Screen, "the second question of the same action");
            c.Equal("card:0 card:1 cancel", Ids(run.Snapshot()), "with its own options");
            var copy = run.Clone();
            Do(run, "card:1", "confirm");
            c.Equal(231, run.Gold, "121 + 100 x 1 + 10 x 1");
            c.Equal(121, copy.Gold, "a copy taken in the middle of the question was not touched");
            Do(copy, "card:0", "confirm");
            c.Equal(221, copy.Gold, "and answers for itself: 121 + 100 x 1 + 10 x 0");
            c.Equal("""{"gold":221,"done":2,"note":"backed out"}""", Text(copy.Snapshot().State), "to the end of the action");

            var replay = Arena.Run.Replay(new RunSetup(), new Idle(), run.Actions);
            c.Equal(run.Dump(), replay.Dump(), "the save replays through the questions");
        });

        c.Group("a question outside an action cannot be shown", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            var threw = false;
            try { run.Ask(new PickOne(2)); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "Ask throws");
            threw = false;
            try { run.Answer([0]); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "and so does an answer to no question");
        });

        c.Group("what follows a won fight can ask too", () =>
        {
            // The starter deck unshuffled: five Strikes in the first hand. The guard has 5 HP and hits for 9.
            var run = new Run(new RunSetup(), new PrizeAfterFight());
            Do(run, "end_turn");
            c.Equal(71, run.Hp, "the guard's 9");
            // Turn 2 draws the four Defends and Bash; Bash's 8 is enough.
            Do(run, "play:4>e0");
            c.Equal("card_select", run.Snapshot().Screen, "the fight is won and the question is up");
            c.That(run.Fight is null && !run.IsOver, "the fight is gone, the run is not over");
            c.Equal(77, run.State.Hp, "the player was handed back first: 71 plus Burning Blood's 6");
            c.Sequence(["end_turn", "play:4>e0"], run.Actions, "the action that won the fight is in the save");
            var copy = run.Clone();
            Do(run, "card:1", "confirm");
            c.Equal(299, run.State.Gold, "99 + 200");
            c.Equal(77, run.State.Hp, "the hand-back is not undone by running what follows again");
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "the run ended as the director said");
            Do(copy, "cancel");
            c.Equal(99, copy.State.Gold, "the copy backed out: no prize");
            c.That(copy.AtEnd, "and ended too");
            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup(), new PrizeAfterFight(), run.Actions).Dump(), "the save replays");
        });

        c.Group("a fight started from a screen goes back to the director when it is won", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            run.State.TotalFloor = 4;
            Do(run, "fight");
            c.Equal("combat", run.Snapshot().Screen, "the fight is on top of the screen that started it");
            c.Equal(2, run.Screens.Count, "two screens are open");
            c.Equal(4, (int)run.Snapshot().State["run"]!["floor"]!, "on the run's floor");
            Do(run, "play:0>e0");
            c.That(run.AtEnd && run.Result == RunResult.Cleared, "this director ends the run after a fight");
        });
    }

    // ---------- refusals and failures ----------

    /// <summary>Its rule for what follows a fight is broken.</summary>
    private sealed class Broken : RunDirector
    {
        public override void Begin(Run run)
        {
            run.State.TotalFloor = 6;
            run.StartFight(Dummy("Guard", 5));
        }

        public override void AfterFightWon(Run run, FightOutcome fight) => throw new InvalidOperationException("no rewards here");
    }

    /// <summary>What follows its one fight lands on something the arena has not built.</summary>
    private sealed class NothingToGive : RunDirector
    {
        public override void Begin(Run run)
        {
            run.State.TotalFloor = 6;
            run.StartFight(Dummy("Guard", 5));
        }

        public override void AfterFightWon(Run run, FightOutcome fight)
        {
            run.State.Gold += 50;
            throw new RunUnrepresentableException("a relic reward on floor 6: no relic of the player's bag is built");
        }
    }

    /// <summary>Its first step lands on something the arena has not built.</summary>
    private sealed class NothingToStartWith : RunDirector
    {
        public override void Begin(Run run)
        {
            run.State.Gold = 0;
            throw new RunUnrepresentableException("Neow's offer: none of the three relics is built");
        }

        public override void AfterFightWon(Run run, FightOutcome fight) { }
    }

    private static void RefusalsAndFailures(Check c)
    {
        c.Group("a refused action changes nothing and is not in the save", () =>
        {
            var run = new Run(new RunSetup(), new FixedFights(Dummy("Dummy", 100)));
            var before = run.Dump();
            foreach (var id in new[] { "play:7", "play:0", "potion:0", "map:1", "proceed", "confirm", "done", "" })
            {
                var result = run.Execute(id);
                c.That(!result.Ok && !string.IsNullOrEmpty(result.Error), $"'{id}' is refused in a fight");
            }
            c.Equal("'play:7' is not playable now.", run.Execute("play:7").Error, "in the fight's own words");
            c.Equal("No action id was given.", run.Execute(" ").Error, "a blank id");
            c.Equal(before, run.Dump(), "the run is as it was");
            c.Equal(0, run.Actions.Count, "nothing in the save");

            var other = new Run(new RunSetup(), new Idle());
            before = other.Dump();
            var unknown = other.Execute("option:3");
            c.Equal("Unknown event action 'option:3'.", unknown.Error, "a screen outside a fight refuses in its own words");
            c.Equal(before, other.Dump(), "and the run is as it was");
        });

        c.Group("an action outside a fight that throws leaves the run as it was", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            Do(run, "go", "card:1", "confirm");
            var before = run.Dump();
            var threw = "";
            try { run.Execute("boom"); }
            catch (InvalidOperationException ex) { threw = ex.Message; }
            c.Equal("boom", threw, "the exception comes out");
            c.Equal(before, run.Dump(), "the gold it took and the count it set are put back");
            c.Sequence(["go", "card:1", "confirm"], run.Actions, "and it is not in the save");

            Do(run, "late boom", "card:0");
            c.Sequence(["go", "card:1", "confirm", "late boom", "card:0"], run.Actions, "an action that has asked is in the save, with the question's steps");
            threw = "";
            try { run.Execute("confirm"); }
            catch (InvalidOperationException ex) { threw = ex.Message; }
            c.Equal("late boom", threw, "it throws when it is run again with the answer");
            c.Equal(before, run.Dump(), "the run is as it was before the action, the question closed");
            c.Sequence(["go", "card:1", "confirm"], run.Actions, "and the action and its question are out of the save, which still replays to here");
            c.Equal(before, Arena.Run.Replay(new RunSetup(), new Idle(), run.Actions).Dump(), "as the replay shows");

            Do(run, "finish");
            c.That(run.AtEnd, "the run goes on afterwards");
        });

        c.Group("a run whose next step fails after a fight stops as an error, in one piece", () =>
        {
            var run = new Run(new RunSetup(), new Broken());
            Do(run, "play:0>e0");
            c.That(run.AtEnd, "the end snapshot is up");
            c.Equal(RunResult.Error, run.Result, "as an error");
            c.Equal("after the fight on floor 6: InvalidOperationException: no rewards here", run.ResultReason, "with what failed");
            c.Equal("""{"result":"error","run":{"act":1,"floor":6,"ascension":0},"me":{"name":"The Ironclad","hp":80,"max_hp":80},"summary":"Stopped on floor 6: the arena failed.","reason":"after the fight on floor 6: InvalidOperationException: no rewards here"}""",
                Text(run.Snapshot().State), "the end snapshot says so");
            c.Equal("The run is over: the arena failed. Nothing is left to do; stop here.", Label(run.Snapshot(), "done"), "the label of done");
            c.Sequence(["play:0>e0"], run.Actions, "the action that ended the fight is in the save");
        });

        c.Group("a run can be ended as unrepresentable", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            run.State.TotalFloor = 5;
            run.End(RunResult.Unrepresentable, "the shop's Rare colorless card is not in the arena");
            c.Equal("""{"result":"unrepresentable","run":{"act":1,"floor":5,"ascension":0},"me":{"name":"The Ironclad","hp":80,"max_hp":80},"summary":"Stopped on floor 5: the run met something the arena does not hold.","reason":"the shop's Rare colorless card is not in the arena"}""",
                Text(run.Snapshot().State), "the end snapshot");
            c.Equal("The run is over: it met something the arena does not hold. Nothing is left to do; stop here.", Label(run.Snapshot(), "done"), "the label of done");
        });

        c.Group("an action that meets something the arena has not built ends the run as unrepresentable, as it stood before", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            Do(run, "go", "card:1", "confirm");
            c.Equal(110, run.Gold, "99 + 1 + 10 x 1 before the action");
            Do(run, "missing");
            c.That(run.AtEnd, "the end snapshot is up: nothing was thrown at the caller");
            c.Equal(RunResult.Unrepresentable, run.Result, "as unrepresentable, not as an error");
            c.Equal("a check's roll on floor 0 can only land on cards the arena leaves out (NeverBuilt)", run.ResultReason, "the reason is the exception's own text");
            c.Equal(110, run.State.Gold, "the gold the action took before it met the missing thing is back");
            c.Equal("""{"result":"unrepresentable","run":{"act":1,"floor":0,"ascension":0},"me":{"name":"The Ironclad","hp":80,"max_hp":80},"summary":"Stopped on floor 0: the run met something the arena does not hold.","reason":"a check's roll on floor 0 can only land on cards the arena leaves out (NeverBuilt)"}""",
                Text(run.Snapshot().State), "the end snapshot");
            c.Sequence(["go", "card:1", "confirm", "missing"], run.Actions, "the action is in the save");
            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup(), new Idle(), run.Actions).Dump(), "so the save replays to the same end");
            c.That(!run.Execute("go").Ok, "and the run is over");

            var asked = new Run(new RunSetup(), new Idle());
            Do(asked, "late missing", "card:0");
            c.That(!asked.IsOver && asked.Gold == 104, "an action that asks first: the question is up, the 5 gold is paid");
            Do(asked, "confirm");
            c.That(asked.AtEnd && asked.Result == RunResult.Unrepresentable, "it meets the missing thing when it is run again with the answer, and the run ends");
            c.Equal("a check's pick on floor 0 landed on the card NeverBuilt, which the arena leaves out", asked.ResultReason, "with the reason");
            c.Equal(null, asked.Question, "the question is closed");
            c.Equal(99, asked.State.Gold, "the run stands as before the action: the 5 gold is back");
            c.Sequence(["late missing", "card:0", "confirm"], asked.Actions, "the action and the steps of its question stay in the save");
            c.Equal(asked.Dump(), Arena.Run.Replay(new RunSetup(), new Idle(), asked.Actions).Dump(), "which replays to the same end");
        });

        c.Group("the same after a won fight and at the start of a run", () =>
        {
            var run = new Run(new RunSetup(), new NothingToGive());
            Do(run, "play:0>e0");
            c.That(run.AtEnd && run.Result == RunResult.Unrepresentable, "what follows the fight met something missing: unrepresentable, not an error");
            c.Equal("a relic reward on floor 6: no relic of the player's bag is built", run.ResultReason, "the reason as thrown");
            c.Equal(80, run.State.Hp, "the player was handed back by the fight first");
            c.Equal(99, run.State.Gold, "and the 50 gold that step took before it met the missing thing is back");
            c.Sequence(["play:0>e0"], run.Actions, "the action that ended the fight is the save");
            c.Equal(run.Dump(), Arena.Run.Replay(new RunSetup(), new NothingToGive(), run.Actions).Dump(), "and replays");

            var atStart = new Run(new RunSetup(), new NothingToStartWith());
            c.That(atStart.AtEnd && atStart.Result == RunResult.Unrepresentable, "a run whose first step meets something missing is made, and is over");
            c.Equal("Neow's offer: none of the three relics is built", atStart.ResultReason, "with the reason");
            c.Equal(99, atStart.State.Gold, "as the player started");
            c.Equal(0, atStart.Actions.Count, "with an empty save");
        });

        c.Group("a director without a rule for a finished room says so", () =>
        {
            var run = new Run(new RunSetup(), new Idle());
            var threw = "";
            try { run.RoomFinished(); }
            catch (NotSupportedException ex) { threw = ex.Message; }
            c.Equal("Idle has no rule for what follows a finished room.", threw, "by name");
        });
    }

    // ---------- behind the bridge ----------

    private static void Game(Check c)
    {
        static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
        var none = CancellationToken.None;

        c.Group("the run behind the bridge: busy after an action, refused while busy", () =>
        {
            var game = new RunGame(new Run(new RunSetup(), new FixedFights(Dummy("Dummy", 100, hit: 3)))) { ActionDelay = TimeSpan.FromSeconds(30) };
            var first = Wait(game.CaptureAsync(none));
            c.That(first is { Screen: "combat", AwaitingDecision: true }, "a decision at the start");
            c.That(Wait(game.ExecuteAsync("end_turn", none)).Ok, "an action is accepted");
            var busy = Wait(game.CaptureAsync(none));
            c.That(busy is { Screen: "combat", AwaitingDecision: false, Waiting: "resolving" } && busy.Actions.Count == 0, "then the snapshot is busy, with no actions");
            c.Equal("""{"turn":2,"round":2,"hp":77,"block":0}""", Text(busy.State), "with the fight's small waiting state");
            var refused = Wait(game.ExecuteAsync("end_turn", none));
            c.Equal("Cannot act now (resolving). Call wait_for_decision.", refused.Error, "an action sent meanwhile is refused");
            c.Equal(1, game.RefusedActions, "and counted");
            c.Sequence(["end_turn"], game.Actions, "the save has the one accepted action");
            c.Equal(77, (int)Wait(game.DescribeDeckAsync(none))["hp"]!, "get_deck answers while busy");
        });

        c.Group("the run behind the bridge: to the end without waiting", () =>
        {
            var game = new RunGame(new Run(new RunSetup(), new FixedFights(Dummy("Brute", 100, hit: 50)))) { ActionDelay = TimeSpan.Zero };
            c.That(!Wait(game.ExecuteAsync("play:9", none)).Ok, "an illegal id is refused");
            c.That(Wait(game.ExecuteAsync("end_turn", none)).Ok && Wait(game.ExecuteAsync("end_turn", none)).Ok, "two turns");
            c.That(game.IsOver && !game.AtEnd && game.Result == RunResult.Died, "the player died");
            c.Equal("game_over", Wait(game.CaptureAsync(none)).Screen, "game over");
            c.That(Wait(game.ExecuteAsync("continue", none)).Ok && game.AtEnd, "continue");
            var end = Wait(game.CaptureAsync(none));
            c.That(end is { Screen: "arena_over", AwaitingDecision: true }, "the end snapshot is a decision");
            c.That(Wait(game.ExecuteAsync("done", none)).Ok, "done succeeds");
            c.Equal(Text(end.ToJson()), Text(Wait(game.CaptureAsync(none)).ToJson()), "and the snapshot is the same");
            c.Equal(1, game.RefusedActions, "one refusal in all");
        });

        c.Group("the run behind the bridge: done leaves no busy spell, an action outside a fight does", () =>
        {
            var game = new RunGame(new Run(new RunSetup(), new Idle())) { ActionDelay = TimeSpan.FromSeconds(30) };
            c.That(Wait(game.ExecuteAsync("finish", none)).Ok, "the action that ends the run");
            var busy = Wait(game.CaptureAsync(none));
            c.That(busy is { Screen: "event", AwaitingDecision: false, Waiting: "resolving" }, "busy under the screen the action was taken on");
            c.Equal("{}", Text(busy.State), "with an empty state, as the bridge's busy snapshots have");

            var ended = new Run(new RunSetup(), new Idle());
            ended.End(RunResult.Cleared);
            var over = new RunGame(ended) { ActionDelay = TimeSpan.FromSeconds(30) };
            c.That(Wait(over.ExecuteAsync("done", none)).Ok, "done");
            c.That(Wait(over.CaptureAsync(none)) is { Screen: "arena_over", AwaitingDecision: true }, "is answered by the end snapshot at once");
        });

        c.Group("the save on disk is written as the run goes, and carries a run on", () =>
        {
            var folder = Directory.CreateTempSubdirectory("spiremind-arena-check-").FullName;
            // A folder that is not there yet: the game makes it.
            var path = Path.Combine(folder, "saves", "run.actions");
            try
            {
                var game = new RunGame(new Run(CarrySetup(), CarryFights())) { ActionDelay = TimeSpan.Zero, ActionLogPath = path };
                foreach (var id in CarryActions[..9]) c.That(Wait(game.ExecuteAsync(id, none)).Ok, $"{id} is accepted");
                c.That(!Wait(game.ExecuteAsync("map:0", none)).Ok, "a refused action");
                c.Equal(null, game.ActionLogError, "the file could be written");
                c.Equal("# spiremind arena run, seed CARRY\n" + string.Join("\n", CarryActions[..9]) + "\n", File.ReadAllText(path).Replace("\r\n", "\n"), "the file: the seed, then the accepted ids");
                var (seed, actions) = RunGame.ReadActionLog(path);
                c.Equal("CARRY", seed, "read back: the seed");
                c.Sequence(CarryActions[..9], actions, "and the actions");

                // The process dies here. The same setup and the file give the run back, in fight 2.
                var resumed = RunGame.Resume(CarrySetup(), CarryFights(), path, TimeSpan.Zero);
                c.Equal(game.Run.Dump(), resumed.Run.Dump(), "the run is rebuilt as it was");
                foreach (var id in CarryActions[9..]) c.That(Wait(resumed.ExecuteAsync(id, none)).Ok, $"{id} is accepted after the restart");
                c.That(resumed.AtEnd && resumed.Result == RunResult.Cleared, "and played to its end");
                c.Sequence(CarryActions, RunGame.ReadActionLog(path).Actions, "the file holds the whole run, nothing twice");

                var wrong = "";
                try { RunGame.Resume(new RunSetup { Seed = "OTHER" }, CarryFights(), path); }
                catch (InvalidOperationException ex) { wrong = ex.Message; }
                c.That(wrong.Contains("is of the run with seed CARRY, not OTHER", StringComparison.Ordinal), "a save of another seed is not taken");
            }
            finally
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
        });

        c.Group("eight threads at once break nothing", () =>
        {
            var game = new RunGame(new Run(new RunSetup { Seed = "THREADS" }, new FixedFights(new FightRequest { Encounter = "NibbitsWeak" }, new FightRequest { Encounter = "SlimesWeak" }))) { ActionDelay = TimeSpan.Zero };
            var errors = 0;
            var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
            {
                try
                {
                    for (var n = 0; n < 400 && !game.AtEnd; n++)
                    {
                        var snapshot = Wait(game.CaptureAsync(none));
                        if (snapshot.Actions.Count > 0) Wait(game.ExecuteAsync(Pick(snapshot), none));
                        Wait(game.DescribeDeckAsync(none));
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errors);
                }
            })).ToList();
            threads.ForEach(t => t.Start());
            threads.ForEach(t => t.Join());
            c.Equal(0, errors, "no call threw");
            c.That(game.AtEnd, "the run was played to its end");
            c.Equal(game.Run.Dump(), Arena.Run.Replay(new RunSetup { Seed = "THREADS" }, new FixedFights(new FightRequest { Encounter = "NibbitsWeak" }, new FightRequest { Encounter = "SlimesWeak" }), game.Actions).Dump(),
                "and its save, in the order the actions were accepted, replays to the same state");
        });
    }
}
