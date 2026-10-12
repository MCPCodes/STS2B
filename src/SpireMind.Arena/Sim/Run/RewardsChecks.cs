using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The rewards of a run: what a fight gives and in which order the numbers are drawn, the card
/// factory, random potions, the rewards and card reward screens, potions outside fights, and
/// the call site of every rewards and card-offer hook.
/// <para>
/// Where the expected values come from. The real game's own record of the run on seed
/// 1BUSLN0FY5 (v0.107.1; sts2-notes/golden-saves, history/1791755111.run and the later saves):
/// the card Arcane Scroll gave, and the gold, the potion and the three cards of its first two
/// fights, with the stream counter and the two odds after each. A second, straight-line
/// reading of the game's draw order in this file (<see cref="Trace"/>), which never calls the
/// rewards' own code: it takes the numbers off a copy of the stream and applies the game's
/// thresholds and lists one after the other (which relic an elite's rarity roll gives is
/// asked of the relic framework's pull, on a copy). And numbers worked out by hand from the
/// rule each line names, at places of the stream chosen so that the rule decides the outcome.
/// Snapshot shapes, labels and refusals are those of docs/screens.md.
/// </para>
/// </summary>
public sealed class RewardsChecks : IArenaChecks
{
    public void Run(Check c)
    {
        RealRun(c);
        RealRunThroughTheScreens(c);
        DrawOrder(c);
        PotionChance(c);
        Bosses(c);
        Elites(c);
        Rows(c);
        PotionRows(c);
        ScreensThatAreNotARoomsLast(c);
        PotionsOutsideFights(c);
        Hooks(c);
        CardOfferHooks(c);
        Options(c);
        Factory(c);
        Merchant(c);
        RandomPotions(c);
        GoldShare(c);
        Sorting(c);
        CopiesAndReplay(c);
        Keyed(c);
    }

    // ---------- helpers ----------

    private const string PlayedSeed = "1BUSLN0FY5";
    private static readonly string[] Overgrowth = ["Overgrowth", "Hive", "Glory"];

    private static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>JSON as compact text with "+" and quotes written as they are, as the tools send it.</summary>
    private static string Text(JsonNode? node) => node is null ? "null" : node.ToJsonString(Plain);

    private static string Keys(JsonNode? node) => node is JsonObject o ? string.Join(",", o.Select(kv => kv.Key)) : "(not an object)";

    private static string Ids(GameSnapshot snapshot) => string.Join(" ", snapshot.Actions.Select(a => a.Id));

    private static string? Label(GameSnapshot snapshot, string id) => snapshot.Actions.FirstOrDefault(a => a.Id == id)?.Label;

    private static void Do(Arena.Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    /// <summary>A screen to stand on between rooms: one action, which finishes the "room".</summary>
    private sealed class Hall : RunScreen, IHearsRewardsClosed
    {
        public int RewardsClosed;
        public int RowsLeftWhenClosed = -1;
        public int StillHearingRelics = -1;

        public override string Name => "event";

        public override GameSnapshot Snapshot(Arena.Run run) => new()
        {
            Screen = Name, AwaitingDecision = true,
            State = new JsonObject { ["rewards_closed"] = RewardsClosed },
            Actions = [new LegalAction("leave", "Leave")],
        };

        public override ActionResult Execute(Arena.Run run, string actionId)
        {
            if (actionId != "leave") return ActionResult.Fail($"Unknown action '{actionId}'.");
            run.RoomFinished();
            return ActionResult.Success;
        }

        public void AfterRewardsClosed(Arena.Run run, RewardsSet rewards)
        {
            RewardsClosed++;
            RowsLeftWhenClosed = rewards.Rewards.Count(reward => !reward.SuccessfullySelected);
            StillHearingRelics = rewards.Rewards.OfType<CardReward>().Count(reward => reward.HearsRelics);
        }

        public override RunScreen Clone(CloneMap map) => new Hall { RewardsClosed = RewardsClosed, RowsLeftWhenClosed = RowsLeftWhenClosed, StillHearingRelics = StillHearingRelics };
    }

    /// <summary>
    /// A run for checks: the world of a seed (the acts forced to start in Overgrowth), then either
    /// scripted fights one floor after the other, each followed by its rewards as the act will do
    /// it, or a hall to stand in. A run that cannot go on as the game would ends as unrepresentable.
    /// </summary>
    private sealed class Rooms : RunDirector
    {
        public StreamMode Mode { get; init; } = StreamMode.Game;
        public bool Strict { get; init; }

        /// <summary>Numbers the rewards stream has given before the first room.</summary>
        public int RewardsDrawn { get; init; }

        /// <summary>The fights' rooms, in order; none for a run that stands in a hall.</summary>
        public IReadOnlyList<RoomType> Fights { get; init; } = [];

        /// <summary>The floor of the first fight.</summary>
        public int FirstFloor { get; init; } = 2;

        /// <summary>The profile; everything unlocked when left out.</summary>
        public RunUnlocks? Unlocks { get; init; }

        /// <summary>A fight whose rewards cannot be made as the game would is followed by the next fight (and
        /// <see cref="LastProblem"/> says why) instead of ending the run: for counting over many fights.</summary>
        public bool KeepGoing { get; init; }

        public int Started;
        public int RoomsFinished;

        /// <summary>The set of the last fight, also when filling it failed half way.</summary>
        public RewardsSet? LastSet;
        public string? LastProblem;

        public override void Begin(Arena.Run run)
        {
            var world = RunWorld.Begin(run.State, Unlocks, Mode, acts: Overgrowth);
            world.Streams.FastForward(RunStream.Rewards, RewardsDrawn);
            if (Strict) run.State.Part<LeftOutLog>().Strict = true;
            run.State.TotalFloor = FirstFloor - 1;
            world.Streams.MoveTo(0, run.State.TotalFloor, 0);
            if (Fights.Count == 0) run.Push(new Hall());
            else NextFight(run);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight)
        {
            LastProblem = null;
            if (!KeepGoing)
            {
                // As the act will do it.
                try
                {
                    LastSet = RunRewards.OfferForRoomEnd(run, fight)?.Set;
                }
                catch (RunUnrepresentableException ex)
                {
                    LastProblem = ex.Message;
                    run.End(RunResult.Unrepresentable, ex.Message);
                }
                return;
            }

            // The same in its two steps, so that a set that could not be filled can still be looked at.
            LastSet = new RewardsSet().WithRewardsFromRoom(run, RewardRoom.Of(fight));
            try
            {
                LastSet.Offer(run);
            }
            catch (RunUnrepresentableException ex)
            {
                LastProblem = ex.Message;
                AfterRoom(run);
            }
        }

        public override void AfterRoom(Arena.Run run)
        {
            RoomsFinished++;
            if (Fights.Count == 0) return;
            if (Started >= Fights.Count) run.End(RunResult.Cleared);
            else NextFight(run);
        }

        private void NextFight(Arena.Run run)
        {
            var room = Fights[Started++];
            run.State.TotalFloor++;
            run.State.ActFloor = run.State.TotalFloor;
            run.State.Part<RunWorld>().Streams.MoveTo(0, run.State.TotalFloor - 1, 0);
            run.StartFight(new FightRequest
            {
                Monsters = () => [new PlacedMonster(new CheckDummy { DisplayName = "Dummy", Hp = 5 })],
                RoomType = room,
                ShuffleDeck = false,
            });
        }
    }

    /// <summary>Wins the scripted fight that is on: the starter's first five cards are Strikes, and one kills the 5 HP dummy.</summary>
    private static void WinFight(Arena.Run run) => Do(run, run.LegalActions().First(a => a.Id.EndsWith(">e0", StringComparison.Ordinal)).Id);

    private static Arena.Run Standing(string seed = PlayedSeed, RunSetup? setup = null, Rooms? rooms = null) =>
        new(setup ?? new RunSetup { Seed = seed }, rooms ?? new Rooms());

    private static RunWorld World(Arena.Run run) => run.State.Part<RunWorld>();

    private static int RewardsCounter(Arena.Run run) => World(run).Streams.Counter(RunStream.Rewards);

    private static List<string> ClassNames(IEnumerable<CardInstance> cards) => cards.Select(card => card.Def.ClassName).ToList();

    // ---------- the real run ----------

    private static void RealRun(Check c)
    {
        c.Group("the real run on seed 1BUSLN0FY5: Arcane Scroll's card", () =>
        {
            // ArcaneScroll.cs: one card for a reward from the character's pool, filtered to Rare, uniform odds, no
            // upgrade roll. So one number of rewards: an index into the pool's Rares. The game's record: Barricade,
            // and the save written right after shows rewards at 1.
            var run = Standing();
            var options = new CardCreationOptions([CardPools.Character], CardCreationSource.Other, CardOddsType.Uniform, card => card.Rarity == CardRarity.Rare)
                .WithFlags(CardCreationFlags.NoUpgradeRoll);
            var cards = CardFactory.CreateForReward(run, 1, options);
            c.Sequence(["Barricade"], ClassNames(cards.Select(r => r.Card)), "the card");
            c.Equal(1, RewardsCounter(run), "one number of rewards");
            c.Equal(-0.05f, run.State.CardRarityOffset, "the pity offset is untouched: uniform odds roll no rarity");
        });

        c.Group("the real run on seed 1BUSLN0FY5: the rewards of its first two fights", () =>
        {
            // The game's record (history/1791755111.run, later-*.save): rewards stood at 1 before the first fight.
            // Fight 1: 11 gold, no potion, the cards Colossus (taken), Unrelenting, Havoc; rewards 12, pity -0.02, potion chance 0.5.
            // Fight 2: 10 gold, Vulnerable Potion, the cards Fight Me! (taken), Stone Armor, Setup Strike; rewards 25, pity 0.01, chance 0.4.
            // The record lists the taken card first and the others in the order they were offered.
            var run = Standing(rooms: new Rooms { RewardsDrawn = 1 });

            var first = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            c.Sequence(["gold", "card"], first.Rewards.Select(r => r.Kind), "fight 1: gold and a card reward, no potion");
            c.Equal(11, ((GoldReward)first.Rewards[0]).Amount, "fight 1: gold");
            var offer1 = ClassNames(((CardReward)first.Rewards[1]).Cards);
            c.Sequence(["Colossus", "Havoc", "Unrelenting"], offer1.Order(StringComparer.Ordinal), "fight 1: the three cards");
            c.That(offer1.IndexOf("Unrelenting") < offer1.IndexOf("Havoc"), $"fight 1: Unrelenting was offered before Havoc (offered: {string.Join(", ", offer1)})");
            c.Equal(12, RewardsCounter(run), "fight 1: rewards after it (1 + potion roll + gold + 3 x 3)");
            c.Equal(-0.02f, run.State.CardRarityOffset, "fight 1: the pity offset after three cards that were not Rare");
            c.Equal(0.5f, run.State.PotionChance, "fight 1: the potion chance after no drop");

            var second = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            c.Sequence(["gold", "potion", "card"], second.Rewards.Select(r => r.Kind), "fight 2: gold, a potion, a card reward");
            c.Equal(10, ((GoldReward)second.Rewards[0]).Amount, "fight 2: gold");
            c.Equal("VulnerablePotion", ((PotionReward)second.Rewards[1]).Potion?.ClassName, "fight 2: the potion");
            var offer2 = ClassNames(((CardReward)second.Rewards[2]).Cards);
            c.Sequence(["FightMe", "SetupStrike", "StoneArmor"], offer2.Order(StringComparer.Ordinal), "fight 2: the three cards");
            c.That(offer2.IndexOf("StoneArmor") < offer2.IndexOf("SetupStrike"), $"fight 2: Stone Armor was offered before Setup Strike (offered: {string.Join(", ", offer2)})");
            c.Equal(25, RewardsCounter(run), "fight 2: rewards after it (12 + potion roll + gold + 2 for the potion + 3 x 3)");
            c.Equal(0.01f, run.State.CardRarityOffset, "fight 2: the pity offset");
            c.Equal(0.4f, run.State.PotionChance, "fight 2: the potion chance after a drop");
            c.That(((CardReward)first.Rewards[1]).Cards.Concat(((CardReward)second.Rewards[2]).Cards).All(card => !card.IsUpgraded), "no reward card of act 1 came upgraded");
        });
    }

    // ---------- the second reading of the game's draw order ----------

    /// <summary>What <see cref="Trace"/> expects of one fight's rewards.</summary>
    private sealed record Traced(bool PotionDropped, int? Gold, string? Potion, List<string> Cards, List<bool> Upgraded, RelicRarity? RelicRarity, string? Relic, bool NoRelicLeft, List<string> PassedOver)
    {
        public override string ToString() =>
            $"gold {Gold?.ToString(CultureInfo.InvariantCulture) ?? "-"} | potion {(PotionDropped ? Potion : "-")} | cards {string.Join(",", Cards.Zip(Upgraded, (card, up) => up ? card + "+" : card))}"
            + $" | relic {(RelicRarity is null ? "-" : NoRelicLeft ? "none left" : Relic)} | passed over {string.Join(",", PassedOver)}";
    }

    /// <summary>What the rewards themselves made, in the same words.</summary>
    private static string Made(RewardsSet set, string? problem, IEnumerable<string> passedOver)
    {
        // A set that could not be filled is still in the order it was made in; a filled one is sorted. Kinds are looked up, not counted.
        var gold = set.Rewards.OfType<GoldReward>().SingleOrDefault();
        var potion = set.Rewards.OfType<PotionReward>().SingleOrDefault();
        var cards = set.Rewards.OfType<CardReward>().Single().Cards;
        var relic = set.Rewards.OfType<RelicReward>().SingleOrDefault();
        return $"gold {gold?.Amount.ToString(CultureInfo.InvariantCulture) ?? "-"} | potion {potion?.Potion?.ClassName ?? "-"} | cards {string.Join(",", cards.Select(card => card.IsUpgraded ? card.Def.ClassName + "+" : card.Def.ClassName))}"
            + $" | relic {(relic is null ? "-" : problem is not null ? "none left" : relic.Relic?.ClassName)} | passed over {string.Join(",", passedOver)}";
    }

    /// <summary>
    /// One fight's rewards read straight off the game's code, number by number, from a copy of
    /// the rewards stream as it stood before the fight. It calls nothing of the rewards: the
    /// thresholds, the tables and the order are written out here, each with the line of the
    /// game's code it stands for; the lists are the game's (the card pool, the potion list).
    /// Which relic an elite's rarity roll gives is the relic framework's rule (RunRelics, with
    /// its own checks): the trace asks it, on a copy of the run's state, at the place in the
    /// order where the game rolls it.
    /// </summary>
    /// <param name="relics">A copy of the run's state to pull the elite's relic from; null rolls the rarity alone.</param>
    private static Traced Trace(GameRng rng, RoomType room, ref float potionChance, ref float pity, int actIndex, RunState? relics, (int Min, int Max)? gold = null, bool goldRow = true)
    {
        var registry = Content.Registry;
        var passedOver = new List<string>();

        // RewardsSet.RollForPotionAndAddTo, PotionRewardOdds.Roll: one number against the chance (an elite adds half of 0.25);
        // the chance goes down by 0.1 after a drop and up by 0.1 after none.
        var potionRoll = rng.NextFloat();
        var needed = potionChance + (room == RoomType.Elite ? 0.25f : 0f) * 0.5f;
        var dropped = potionRoll < needed;
        if (dropped) potionChance -= 0.1f;
        else potionChance += 0.1f;

        // GoldReward.Populate: NextInt(min, max + 1). EncounterModel: 10 to 20, 35 to 45, 100.
        int? amount = null;
        if (goldRow)
        {
            var (min, max) = gold ?? room switch { RoomType.Monster => (10, 20), RoomType.Elite => (35, 45), _ => (100, 100) };
            amount = min + (int)(rng.NextDouble() * (max + 1 - min));
        }

        // PotionFactory.CreateRandomPotion: a rarity (Rare at or below 0.1, Uncommon at or below 0.35), then one of that rarity.
        // The arena's rule for a potion it has not built: both numbers again.
        string? potion = null;
        while (dropped && potion is null)
        {
            var rarityRoll = rng.NextFloat();
            var rarity = rarityRoll <= 0.1f ? PotionRarity.Rare : rarityRoll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
            var ofRarity = Scenarios.PotionPool.Where(p => p.Rarity == rarity).ToList();
            var landed = ofRarity[(int)(rng.NextDouble() * ofRarity.Count)].ClassName;
            if (registry.Has(ContentKind.Potion, landed)) potion = landed;
            else passedOver.Add(landed);
        }

        // CardFactory.CreateForReward, three times: CardRarityOdds.Roll, GetNextAllowedRarity, NextItem, RollForUpgrade.
        var pool = CardPools.Ironclad.All.Where(card => !card.MultiplayerOnly).ToList();
        var cards = new List<string>();
        var upgraded = new List<bool>();
        for (var i = 0; i < 3; i++)
        {
            var roll = rng.NextFloat();
            var rareOdds = room switch { RoomType.Monster => 0.03f, RoomType.Elite => 0.1f, _ => 1f };
            var uncommonOdds = room switch { RoomType.Monster => 0.37f, RoomType.Elite => 0.4f, _ => 0f };
            var rareBelow = rareOdds + (room == RoomType.Boss ? 0f : pity);
            var rolled = roll < rareBelow ? CardRarity.Rare : roll < uncommonOdds + rareBelow ? CardRarity.Uncommon : CardRarity.Common;
            pity = rolled == CardRarity.Rare ? -0.05f : Math.Min(pity + 0.01f, 0.4f);

            var left = pool.Where(card => !cards.Contains(card.ClassName)).ToList();
            var rarity = rolled;
            while (!left.Any(card => card.Rarity == rarity))
            {
                rarity = rarity switch { CardRarity.Common => CardRarity.Uncommon, CardRarity.Uncommon => CardRarity.Rare, _ => CardRarity.Common };
                if (rarity == rolled) throw new InvalidOperationException("no rarity is left");
            }
            var ofRarity = left.Where(card => card.Rarity == rarity).ToList();
            var card = ofRarity[(int)(rng.NextDouble() * ofRarity.Count)];
            cards.Add(card.ClassName);

            var upgradeRoll = (decimal)rng.NextFloat();
            var odds = card.Rarity == CardRarity.Rare ? 0m : actIndex * 0.25m;
            upgraded.Add(registry.Card(card.ClassName).MaxUpgradeLevel > 0 && upgradeRoll <= odds);
        }

        // RelicFactory.RollRarity after an elite, the last number: Common below 0.5, Uncommon below 0.83, else Rare;
        // then the front of the player's bag.
        RelicRarity? relicRarity = null;
        string? relic = null;
        var noRelicLeft = false;
        if (room == RoomType.Elite && relics is null)
        {
            var roll = rng.NextFloat();
            relicRarity = roll < 0.5f ? RelicRarity.Common : roll < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare;
        }
        else if (room == RoomType.Elite)
        {
            var pull = RunRelics.RollAndPullFromFront(relics!, rng);
            relicRarity = pull.Asked;
            relic = pull.Relic;
            noRelicLeft = !pull.Found;
            passedOver.AddRange(pull.PassedOver);
        }
        return new Traced(dropped, amount, potion, cards, upgraded, relicRarity, relic, noRelicLeft, passedOver);
    }

    // ---------- the real run, through the screens ----------

    private static void RealRunThroughTheScreens(Check c)
    {
        c.Group("the real run on seed 1BUSLN0FY5, played through the screens: two fights, their rewards taken as the run took them", () =>
        {
            var setup = new RunSetup { Seed = PlayedSeed };
            var rooms = new Rooms { RewardsDrawn = 1, Fights = [RoomType.Monster, RoomType.Monster] };
            var run = new Arena.Run(setup, rooms);
            c.Equal("combat", run.Snapshot().Screen, "the first fight is on");
            WinFight(run);

            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "a won fight is followed by its rewards");
            c.That(s.AwaitingDecision, "a decision");
            c.Equal("run,boss,player,potions,rewards", Keys(s.State), "the state's keys in the reader's order");
            c.Equal("""{"act":1,"floor":2,"ascension":0}""", Text(s.State["run"]), "the run block");
            c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the act's boss, as the save's README names it for this seed");
            c.Equal("""{"character":"The Ironclad","hp":80,"max_hp":80,"gold":99,"relics":["Burning Blood"],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}""",
                Text(s.State["player"]), "the player block without potions");
            c.Equal("[null,null,null]", Text(s.State["potions"]), "the detailed potions, one entry per slot");
            c.Equal("""[{"n":0,"kind":"gold","amount":11},{"n":1,"kind":"card","count":3}]""", Text(s.State["rewards"]), "the rows: 11 gold and a card reward that was not opened yet");
            c.Equal("reward:0 reward:1 proceed", Ids(s), "the actions: the rows, then proceed");
            c.Equal("Take 11 gold", Label(s, "reward:0"), "the gold's label");
            c.Equal("Open card reward (3 cards)", Label(s, "reward:1"), "the card reward's label");
            c.Equal("Skip (leave without the unclaimed rewards)", Label(s, "proceed"), "proceed reads Skip while rows are left");

            Do(run, "reward:0");
            c.Equal(110, run.Gold, "99 + 11: the game's record has 110 after this floor");
            s = run.Snapshot();
            c.Equal("""[{"n":0,"kind":"card","count":3}]""", Text(s.State["rewards"]), "the card reward moved up to 0");
            c.Equal("reward:0 proceed", Ids(s), "and so did its id");

            Do(run, "reward:0");
            s = run.Snapshot();
            c.Equal("card_reward", s.Screen, "the card reward's own screen");
            c.Equal("run,boss,player,cards", Keys(s.State), "its keys");
            c.Equal("""{"character":"The Ironclad","hp":80,"max_hp":80,"gold":110,"relics":["Burning Blood"],"potions":[null,null,null],"deck_size":10,"deck":{"Bash":1,"Defend":4,"Strike":5}}""",
                Text(s.State["player"]), "the full player block, potions by name");
            var cards = s.State["cards"]!.AsArray();
            c.Equal(3, cards.Count, "three cards");
            c.Sequence(["Colossus", "Havoc", "Unrelenting"], cards.Select(card => card!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal), "the run's three");
            for (var n = 0; n < cards.Count; n++)
            {
                var def = Content.Registry.Cards.Values.Single(card => card.Name == cards[n]!["name"]!.GetValue<string>());
                c.Equal("n,name,cost,type,text,rarity", Keys(cards[n]), $"card {n}: n first, then the offer's keys");
                c.Equal(n, cards[n]!["n"]!.GetValue<int>(), $"card {n}: its number");
                c.Equal(def.Cost.ToString(CultureInfo.InvariantCulture), cards[n]!["cost"]!.GetValue<string>(), $"card {n}: the cost is a string");
                c.Equal(def.Type.ToString(), cards[n]!["type"]!.GetValue<string>(), $"card {n}: the type");
                c.Equal(def.Rarity.ToString(), cards[n]!["rarity"]!.GetValue<string>(), $"card {n}: the rarity");
                c.Equal($"Take {def.Name}", Label(s, $"card:{n}"), $"card {n}: its label");
            }
            c.Equal("card:0 card:1 card:2 skip", Ids(s), "a card each, then skip; no potion ids here");
            c.Equal("Skip this card reward", Label(s, "skip"), "skip's label");

            var colossus = cards.Select(card => card!["name"]!.GetValue<string>()).ToList().IndexOf("Colossus");
            Do(run, $"card:{colossus}");
            s = run.Snapshot();
            c.Equal("rewards", s.Screen, "taking a card closes the card reward");
            c.Equal("[]", Text(s.State["rewards"]), "and its row is gone: nothing is left");
            c.Equal("proceed", Ids(s), "only proceed");
            c.Equal("Proceed", Label(s, "proceed"), "which reads Proceed once every reward was taken");
            c.Sequence(["Bash", "Colossus"], run.State.Deck.TakeLast(2).Select(card => card.Def.ClassName), "Colossus is the deck's last card");
            c.Equal(11, run.State.Deck[^1].Id, "with the next id of the run");

            Do(run, "proceed");
            c.Equal("combat", run.Snapshot().Screen, "proceed finished the room: the director started the next fight");
            c.Equal(3, run.State.TotalFloor, "on the next floor");
            WinFight(run);

            s = run.Snapshot();
            var potion = Content.Potion("VulnerablePotion");
            c.Equal($$"""[{"n":0,"kind":"gold","amount":10},{"n":1,"kind":"potion","name":"Vulnerable Potion","text":{{JsonSerializer.Serialize(potion.Text(null), Plain)}},"target":"AnyEnemy"},{"n":2,"kind":"card","count":3}]""",
                Text(s.State["rewards"]), "fight 2: the rows in the screen's order, gold, potion, card");
            c.Equal("reward:0 reward:1 reward:2 proceed", Ids(s), "their ids");
            c.Equal("Take potion Vulnerable Potion", Label(s, "reward:1"), "the potion's label");
            Do(run, "reward:0", "reward:0");
            c.Equal(120, run.Gold, "110 + 10: the record has 120");
            c.Sequence(["VulnerablePotion", null, null], run.State.Belt.Slots.Select(p => p?.ClassName), "the potion went into the first slot, as in the record");
            s = run.Snapshot();
            c.Equal($$"""[{"slot":0,"name":"Vulnerable Potion","text":{{JsonSerializer.Serialize(potion.Text(null), Plain)}},"target":"AnyEnemy","usable":false},null,null]""",
                Text(s.State["potions"]), "the detailed potions now show it; it needs an enemy, so it is not usable here");
            c.Equal("reward:0 proceed discard_potion:0", Ids(s), "the potion ids come after proceed; only the discard is offered");
            c.Equal("Discard Vulnerable Potion", Label(s, "discard_potion:0"), "the discard's label");

            Do(run, "reward:0");
            cards = run.Snapshot().State["cards"]!.AsArray();
            c.Sequence(["Fight Me!", "Setup Strike", "Stone Armor"], cards.Select(card => card!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal), "fight 2: the run's three, by their titles");
            c.Equal("""["Vulnerable Potion",null,null]""", Text(run.Snapshot().State["player"]!["potions"]), "the card reward names the potions in its player block");
            var fightMe = cards.Select(card => card!["name"]!.GetValue<string>()).ToList().IndexOf("Fight Me!");
            Do(run, $"card:{fightMe}", "proceed");

            c.Equal(RunResult.Cleared, run.Result, "the check's two rooms are done");
            c.Sequence(["Bash", "Colossus", "FightMe"], run.State.Deck.TakeLast(3).Select(card => card.Def.ClassName), "the deck as the record has it after floor 3");
            c.Equal(25, RewardsCounter(run), "rewards stands at 25, as the save after floor 3 has it");
            c.Equal(2, ((Rooms)run.Director).RoomsFinished, "each proceed told the director that its room was finished");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, rooms, run.Actions).Dump(), "the replay of the action list ends in the same state");
        });
    }

    // ---------- the draw order over many fights ----------

    private static void DrawOrder(Check c)
    {
        c.Group("1,000 scripted fights: every reward equals a second reading of the game's draw order", () =>
        {
            // Hallway fights with an elite every seventh and a boss every twenty-third; the player takes rewards
            // now and then, so that the belt fills and the deck grows on the way.
            var fights = Enumerable.Range(0, 1000).Select(i => i % 23 == 22 ? RoomType.Boss : i % 7 == 6 ? RoomType.Elite : RoomType.Monster).ToList();
            var run = new Arena.Run(new RunSetup { Seed = "TRACE1000" }, new Rooms { Fights = fights, KeepGoing = true });
            var wrong = new List<string>();
            int potions = 0, redrawn = 0, rares = 0, upgradedCards = 0, logged = 0;
            for (var i = 0; i < fights.Count; i++)
            {
                var world = World(run);
                var probe = world.Streams.Long(RunStream.Rewards).Clone();
                var before = probe.Counter;
                var chance = run.State.PotionChance;
                var pity = run.State.CardRarityOffset;
                var stateBefore = run.State.Clone();
                WinFight(run);

                var expected = Trace(probe, fights[i], ref chance, ref pity, actIndex: 0, stateBefore);
                var director = (Rooms)run.Director;
                var log = run.State.FindPart<LeftOutLog>()?.Entries ?? [];
                var made = Made(director.LastSet!, director.LastProblem, log.Skip(logged).Select(entry => entry.ClassName));
                logged = log.Count;
                if (made != expected.ToString()) wrong.Add($"fight {i} ({fights[i]}): expected [{expected}], got [{made}]");
                if (RewardsCounter(run) != probe.Counter) wrong.Add($"fight {i}: the rewards stream gave {RewardsCounter(run) - before} numbers, the game's order takes {probe.Counter - before}");
                if (run.State.PotionChance != chance || run.State.CardRarityOffset != pity) wrong.Add($"fight {i}: the odds afterwards are {run.State.PotionChance} and {run.State.CardRarityOffset}, expected {chance} and {pity}");
                if (expected.PotionDropped) potions++;
                redrawn += expected.PassedOver.Count;
                rares += expected.Cards.Count(card => Content.Card(card).Rarity == CardRarity.Rare);
                upgradedCards += expected.Upgraded.Count(up => up);

                if (run.Top is not RewardsScreen) continue;
                // Every third fight the gold and the potion are taken when they can be; every eleventh the first card too.
                // A relic is never taken: what it does would change the fights and rewards that follow, and the reading
                // above knows nothing of relics.
                if (i % 3 == 0)
                    while (run.LegalActions().FirstOrDefault(a => a.Label.StartsWith("Take ", StringComparison.Ordinal) && !a.Label.StartsWith("Take relic ", StringComparison.Ordinal)) is { } take) Do(run, take.Id);
                if (i % 11 == 0 && run.LegalActions().FirstOrDefault(a => a.Label.StartsWith("Open card reward", StringComparison.Ordinal)) is { } open) Do(run, open.Id, "card:0");
                if (i % 50 == 49 && run.LegalActions().FirstOrDefault(a => a.Id.StartsWith("discard_potion:", StringComparison.Ordinal)) is { } discard) Do(run, discard.Id);
                Do(run, "proceed");
            }
            c.Equal(0, wrong.Count, $"fights whose rewards differ from the second reading: {string.Join("; ", wrong.Take(3))}");
            c.Equal(RunResult.Cleared, run.Result, "all 1,000 were fought");
            c.That(potions is > 380 and < 620, $"about half the fights dropped a potion, as the moving chance is built to give ({potions})");
            c.That(rares > 3 * 43 && rares < 600, $"every boss gave three Rares and the hallways and elites some more ({rares} Rare cards)");
            c.Equal(0, upgradedCards, "no reward card of act 1 came upgraded");
            // Only while the arena leaves a potion of the list out; once all 48 are built nothing is drawn again here.
            if (Scenarios.PotionPool.Any(p => !Content.Registry.Has(ContentKind.Potion, p.ClassName)))
                c.That(redrawn > 0, $"some rolls landed on something the arena has not built and were drawn again ({redrawn})");
        });

        c.Group("how many numbers a fight's rewards take", () =>
        {
            // One for the potion roll, one for the gold, two for a potion that dropped, three per card, one for an elite's relic rarity.
            // A potion chance of 2 makes the drop certain, a chance of -1 impossible (the roll is a number from 0 up to 1).
            (RoomType Room, float Chance, int Numbers)[] cases =
            [
                (RoomType.Monster, -1f, 11), (RoomType.Monster, 2f, 13), (RoomType.Boss, -1f, 11), (RoomType.Boss, 2f, 13),
            ];
            foreach (var (room, chance, numbers) in cases)
            {
                var run = Standing("COUNTING");
                run.State.PotionChance = chance;
                var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(room));
                c.Equal(numbers, RewardsCounter(run), $"{room}, {(chance > 1f ? "a potion" : "no potion")}: numbers drawn");
                c.Equal(chance > 1f, set.Rewards.Any(r => r is PotionReward), $"{room}: a potion row only when one dropped");
                c.Equal(chance > 1f ? chance - 0.1f : chance + 0.1f, run.State.PotionChance, $"{room}: the chance moved by 0.1");
            }
        });
    }

    private static void PotionChance(Check c)
    {
        c.Group("the potion roll: an elite adds 0.125 to the chance, a boss nothing", () =>
        {
            // PotionRewardOdds.Roll: the number is below the chance plus half of 0.25 in an Elite room. At a place where
            // the first number is from 0.4 up to 0.525, with the starting chance of 0.4: only the elite drops a potion.
            const string seed = "ELITEBONUS";
            var place = Place(seed, rng => rng.NextFloat() is >= 0.4f and < 0.525f);
            foreach (var (room, drops) in new[] { (RoomType.Monster, false), (RoomType.Elite, true), (RoomType.Boss, false) })
            {
                var run = At(seed, place);
                var set = new RewardsSet().WithRewardsFromRoom(run, RewardRoom.Combat(room));
                c.Equal(drops, set.Rewards.Any(r => r is PotionReward), $"{room}: {(drops ? "a potion" : "no potion")}");
                c.Equal(drops ? 0.3f : 0.5f, run.State.PotionChance, $"{room}: the chance afterwards");
                c.Equal(place + 1, RewardsCounter(run), $"{room}: making the set drew the potion roll and nothing else");
            }
        });
    }

    private static void Bosses(Check c)
    {
        c.Group("a boss's rewards: 100 gold, three Rares, the pity offset back at its start, and proceed ends the act", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "BOSSROOM" }, new BossAtTheEndOfTheMap());
            c.Equal(17, run.State.TotalFloor, "the boss stands on the act's seventeenth floor");
            run.State.CardRarityOffset = 0.23f;
            WinFight(run);
            var s = run.Snapshot();
            var set = ((RewardsScreen)run.Top!).Set;
            c.Equal(100, set.Rewards.OfType<GoldReward>().Single().Amount, "100 gold (EncounterModel: 100 to 100)");
            var cards = set.Rewards.OfType<CardReward>().Single().Cards;
            c.That(cards.Count == 3 && cards.All(card => card.Def.Rarity == CardRarity.Rare), $"three Rare cards ({string.Join(", ", cards.Select(card => card.Title))})");
            c.Equal(3, cards.Select(card => card.Def.ClassName).Distinct().Count(), "all different");
            c.Equal(-0.05f, run.State.CardRarityOffset, "each Rare put the pity offset back to -0.05");

            c.That(s.State["boss"] is null, "the boss is not named on its own rewards");
            c.Equal("run,player,potions,rewards,proceed_ends_act", Keys(s.State), "the keys: no boss, and proceed_ends_act at the end");
            c.Equal("true", Text(s.State["proceed_ends_act"]), "proceed_ends_act is true");
            c.Equal("Go on to the next act (button reads Skip: unclaimed rewards are left behind)", Label(s, "proceed"), "the proceed label while rows are left");

            while (run.LegalActions().FirstOrDefault(a => a.Label.StartsWith("Take ", StringComparison.Ordinal)) is { } take) Do(run, take.Id);
            Do(run, run.LegalActions().First(a => a.Label.StartsWith("Open card reward", StringComparison.Ordinal)).Id);
            c.Equal("run,player,cards", Keys(run.Snapshot().State), "nor on its card reward");
            Do(run, "card:0");
            c.Equal("Go on to the next act", Label(run.Snapshot(), "proceed"), "the proceed label once everything was taken");
            Do(run, "proceed");
            c.Equal(RunResult.Cleared, run.Result, "proceed handed the room to the director, which ends the act");
        });

        c.Group("the boss of the run's last act gives nothing", () =>
        {
            // RewardsSet.WithRewardsFromRoom: a Boss room in the last act returns the empty set, before any roll.
            var run = Standing("LASTBOSS");
            run.State.ActIndex = 2;
            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Boss));
            c.Equal(0, set.Rewards.Count, "no rewards");
            c.Equal(0, RewardsCounter(run), "and no number drawn, not even the potion roll");
            c.Equal(0.4f, run.State.PotionChance, "the potion chance did not move");
            var screen = set.Offer(run);
            c.That(screen is { IsTerminal: true }, "its screen is still shown: a fight's room ends with one");
            c.That(screen!.Rows.Count == 0 && screen.CanProceed(run) && !screen.ProceedIsSkip, "with no row and a proceed button that reads Proceed");
        });
    }

    /// <summary>The first act's map walked from its start to the boss's point, then the boss's fight: the player stands
    /// where the game has him when a boss's rewards come up.</summary>
    private sealed class BossAtTheEndOfTheMap : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State, acts: Overgrowth);
            var map = MapState.BeginAct(run, 0);
            while (map.Current != map.Map.Boss.Coord) map.Enter(run, map.Next(run)[0].Coord);
            run.StartFight(new FightRequest
            {
                Monsters = () => [new PlacedMonster(new CheckDummy { DisplayName = "Dummy", Hp = 5 })],
                RoomType = RoomType.Boss,
                ShuffleDeck = false,
            });
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => RunRewards.OfferForRoomEnd(run, fight);

        public override void AfterRoom(Arena.Run run) => run.End(RunResult.Cleared);
    }

    private static void Elites(Check c)
    {
        c.Group("an elite's rewards: 35 to 45 gold, the elite's odds, and the relic's rarity is the last number drawn", () =>
        {
            // In strict mode a relic the arena has not built stops the run by name, and that name is the front of the
            // player's bag for the rarity the last number gives; nothing is taken from the bags then. With the relic
            // built, its row shows it and it has left both bags.
            string[] seeds = ["1BUSLN0FY5", "KMEHXZW0R5", "EEXRY5WA1R", "E9U400ERJ4", "ELITE5", "ELITE6", "ELITE7", "ELITE8", "ELITE9", "ELITE10"];
            foreach (var seed in seeds)
            {
                var run = Standing(seed, rooms: new Rooms { Strict = true });
                var world = World(run);
                var probe = world.Streams.Long(RunStream.Rewards).Clone();
                var chance = run.State.PotionChance;
                var pity = run.State.CardRarityOffset;

                // By hand: what the last number says, and the front of that list in the bag as the seed made it.
                var expected = Trace(probe, RoomType.Elite, ref chance, ref pity, 0, relics: null);
                var front = world.PlayerBag.Clone().PullFromFront(expected.RelicRarity!.Value)!;
                var inSharedBag = world.SharedBag.Contains(front);

                var set = new RewardsSet().WithRewardsFromRoom(run, RewardRoom.Combat(RoomType.Elite));
                string? problem = null;
                try { set.GenerateWithoutOffering(run); }
                catch (RunUnrepresentableException ex) { problem = ex.Message; }

                if (expected.PassedOver.Count > 0)
                {
                    // The potion itself landed on one the arena has not built: strict mode stops there, before the cards.
                    c.That(problem is not null && problem.Contains($"the potion {expected.PassedOver[0]},", StringComparison.Ordinal), $"{seed}: strict mode stops at the potion {expected.PassedOver[0]} ({problem})");
                    continue;
                }

                var built = Content.Registry.Has(ContentKind.Relic, front);
                if (built)
                {
                    c.Equal(front, set.Rewards.OfType<RelicReward>().Single().Relic?.ClassName, $"{seed}: the relic row holds the front of the {expected.RelicRarity} list");
                    c.Sequence(expected.PotionDropped ? ["gold", "potion", "relic", "card"] : ["gold", "relic", "card"], set.Rewards.Select(r => r.Kind), $"{seed}: the rows, the relic before the cards");
                }
                else
                {
                    c.That(problem is not null && problem.Contains($"the relic {front} here", StringComparison.Ordinal), $"{seed}: strict mode stops at {front}, the front of the {expected.RelicRarity} list ({problem})");
                }
                c.Equal(probe.Counter, RewardsCounter(run), $"{seed}: numbers drawn ({(expected.PotionDropped ? 14 : 12)}: the relic's rarity after the cards)");
                var gold = set.Rewards.OfType<GoldReward>().Single().Amount;
                c.Equal(expected.Gold, gold, $"{seed}: the gold");
                c.That(gold is >= 35 and <= 45, $"{seed}: 35 to 45 ({gold})");
                c.Sequence(expected.Cards, ClassNames(set.Rewards.OfType<CardReward>().Single().Cards), $"{seed}: the cards, rolled on the elite's odds");
                c.Equal(pity, run.State.CardRarityOffset, $"{seed}: the pity offset afterwards");
                c.Equal(!built, world.PlayerBag.Contains(front), $"{seed}: {front} {(built ? "left the player's bag" : "is still in the player's bag: nothing was pulled")}");
                c.Equal(inSharedBag && !built, world.SharedBag.Contains(front), $"{seed}: and the shared bag likewise");
            }
        });

        c.Group("a relic reward of a named rarity rolls nothing and takes the front of that list", () =>
        {
            var run = Standing(PlayedSeed, rooms: new Rooms { Strict = true });
            var world = World(run);
            // The real run got Stone Cracker this way round: the front of the player's Uncommon list for this seed.
            c.Equal("StoneCracker", world.PlayerBag.Relics(RelicRarity.Uncommon)[0], "the front of the Uncommon list (the relic the real run's event gave)");
            var reward = new RelicReward(RelicRarity.Uncommon);
            string? problem = null;
            try { RunRewards.GenerateCustom(run, [reward]); }
            catch (RunUnrepresentableException ex) { problem = ex.Message; }
            var built = Content.Registry.Has(ContentKind.Relic, "StoneCracker");
            if (built) c.Equal("StoneCracker", reward.Relic?.ClassName, "the reward holds it");
            else c.That(problem is not null && problem.Contains("the relic StoneCracker here", StringComparison.Ordinal), $"strict mode stops at it by name ({problem})");
            c.Equal(0, RewardsCounter(run), "no number is drawn for a named rarity");
            c.Equal(!built, world.PlayerBag.Contains("StoneCracker"), built ? "it left the player's bag" : "nothing was pulled: it is still in the bag");
        });

        c.Group("an elite's relic the arena has not built: what the relic framework's pull gives is the row's, or the run cannot go on", () =>
        {
            var run = Standing("ELITEBAG");
            var world = World(run);
            var probe = world.Streams.Long(RunStream.Rewards).Clone();
            var chance = run.State.PotionChance;
            var pity = run.State.CardRarityOffset;
            var expected = Trace(probe, RoomType.Elite, ref chance, ref pity, 0, run.State.Clone());

            var set = new RewardsSet().WithRewardsFromRoom(run, RewardRoom.Combat(RoomType.Elite));
            string? problem = null;
            try { set.GenerateWithoutOffering(run); }
            catch (RunUnrepresentableException ex) { problem = ex.Message; }

            var log = run.State.FindPart<LeftOutLog>()?.Entries ?? [];
            c.Sequence(expected.PassedOver, log.Select(entry => entry.ClassName), "every potion and relic passed over is in the run's log, in order");
            c.Equal(probe.Counter, RewardsCounter(run), "the same numbers were drawn, a rarity rolled again included");
            if (expected.NoRelicLeft)
                c.That(problem is not null && problem.StartsWith("a relic reward on floor ", StringComparison.Ordinal), $"no built relic could be given: the run cannot go on, and the reason says where ({problem})");
            else
                c.Equal(expected.Relic, set.Rewards.OfType<RelicReward>().Single().Relic?.ClassName, "the relic the pull gave is the row's");
        });
    }

    // ---------- check-only relics and screens ----------

    /// <summary>A relic that does nothing: something for a relic row to hold.</summary>
    private sealed class Trinket : Relic
    {
        public override string Name => "Trinket";
        public override string Describe() => "Does nothing.";
    }

    /// <summary>Writes down every rewards and potion hook it hears, in order.</summary>
    private sealed class Witness : Relic, IRewardHooks
    {
        public string Heard = "";
        public override string Name => "Witness";
        public override string Describe() => "Watches.";

        private void Note(string what) => Heard = Heard.Length == 0 ? what : Heard + " " + what;

        public void AfterRewardTaken(RunContext c, Reward reward) => Note("taken:" + reward.Kind);
        public override void AfterPotionProcured(RunContext c, PotionDef potion) => Note("procured:" + potion.ClassName);
        public override void AfterPotionDiscarded(RunContext c, PotionDef potion) => Note("discarded:" + potion.ClassName);

        // A potion used outside a fight is heard through the fight's two potion hooks, on the player as it stands between rooms.
        public override void BeforePotionUsed(Fight f, PotionDef potion, Creature? target) =>
            Note(string.Create(CultureInfo.InvariantCulture, $"before:{potion.ClassName}@{f.Player.Hp}{(f.IsCombat ? " in a fight" : "")}"));

        public override void AfterPotionUsed(Fight f, PotionDef potion, Creature? target) =>
            Note(string.Create(CultureInfo.InvariantCulture, $"after:{potion.ClassName}@{f.Player.Hp}"));
    }

    // ---------- the rows ----------

    private static CardInstance NewCard(Arena.Run run, string className, bool upgraded = false) => run.State.NewCard(Content.Card(className), upgraded);

    private static void Rows(Check c)
    {
        c.Group("the six kinds of row stand in the game's order whatever order they were made in", () =>
        {
            // The game's RewardsSetIndex: gold 1, potion 2, relic 3, special card 4, card 5, removal 7.
            var run = Standing("ROWS");
            var screen = RunRewards.OfferCustom(run,
            [
                new CardRemovalReward(),
                new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3),
                new SpecialCardReward(NewCard(run, "Anger")),
                new RelicReward(new Trinket()),
                new PotionReward(Content.Potion("BlockPotion")),
                new GoldReward(50),
            ])!;
            c.That(ReferenceEquals(run.Top, screen) && !screen.IsTerminal, "the screen is up, over the hall; it is not a room's last screen");
            // One number for the gold (a fixed amount draws one too), none for the named potion, relic and card, two per rolled card (no upgrade roll).
            c.Equal(7, RewardsCounter(run), "numbers drawn: 1 + 3 x 2");
            var s = run.Snapshot();
            var rows = s.State["rewards"]!.AsArray();
            c.Sequence(["gold", "potion", "relic", "special_card", "card", "card_removal"], rows.Select(row => row!["kind"]!.GetValue<string>()), "the kinds in the screen's order");
            c.Sequence([0, 1, 2, 3, 4, 5], rows.Select(row => row!["n"]!.GetValue<int>()), "numbered from 0");
            c.Equal("""{"n":0,"kind":"gold","amount":50}""", Text(rows[0]), "the gold row");
            var block = Content.Potion("BlockPotion");
            c.Equal($$"""{"n":1,"kind":"potion","name":"Block Potion","text":{{JsonSerializer.Serialize(block.Text(null), Plain)}},"target":"AnyPlayer"}""", Text(rows[1]), "the potion row: name, text, target, no slot and no usable");
            c.Equal("""{"n":2,"kind":"relic","name":"Trinket","text":"Does nothing."}""", Text(rows[2]), "the relic row: name and text");
            c.Equal("n,kind,name,cost,type,text,rarity", Keys(rows[3]), "the special card row: the card's offer keys");
            c.Equal("Anger", rows[3]!["name"]!.GetValue<string>(), "the special card");
            c.Equal("""{"n":4,"kind":"card","count":3}""", Text(rows[4]), "the card row");
            c.Equal("""{"n":5,"kind":"card_removal"}""", Text(rows[5]), "the removal row has no keys of its own");
            c.Equal("reward:0 reward:1 reward:2 reward:3 reward:4 reward:5 proceed", Ids(s), "one action per row, then proceed");
            c.Sequence(["Take 50 gold", "Take potion Block Potion", "Take relic Trinket", "Take card Anger", "Open card reward (3 cards)", "Remove a card", "Skip (leave without the unclaimed rewards)"],
                s.Actions.Select(a => a.Label), "the labels");
            c.That(s.State["boss"] is not null && s.State["proceed_ends_act"] is null, "the boss is named; proceed does not end the act");

            // Taking from the middle: the relic. The rows behind it move up.
            Do(run, "reward:2");
            c.Sequence(["BurningBlood", "Trinket"], run.State.Relics.Select(r => r.ClassName), "the relic joined the relics");
            s = run.Snapshot();
            c.Sequence(["gold", "potion", "special_card", "card", "card_removal"], s.State["rewards"]!.AsArray().Select(row => row!["kind"]!.GetValue<string>()), "its row is gone");
            c.Sequence([0, 1, 2, 3, 4], s.State["rewards"]!.AsArray().Select(row => row!["n"]!.GetValue<int>()), "and the rest is numbered again");
            c.Equal("""["Burning Blood","Trinket"]""", Text(s.State["player"]!["relics"]), "the player block names it");

            Do(run, "reward:2");
            c.Equal("Anger", run.State.Deck[^1].Def.ClassName, "the special card went into the deck");
            c.Equal(11, run.State.Deck.Count, "11 cards");

            // The removal: backing out keeps the reward, a pick uses it up.
            Do(run, "reward:3");
            c.Equal("card_select", run.Snapshot().Screen, "the removal reward opens the removal choice");
            Do(run, "cancel");
            c.Equal("rewards", run.Snapshot().Screen, "backing out returns to the rewards");
            c.That(run.Snapshot().State["rewards"]!.AsArray().Any(row => row!["kind"]!.GetValue<string>() == "card_removal"), "with the removal still on offer");
            c.Equal(11, run.State.Deck.Count, "and the deck as it was");
            Do(run, "reward:3", "card:0");
            while (run.Snapshot().Screen == "card_select") Do(run, "confirm");
            c.Equal(10, run.State.Deck.Count, "after a pick a card is gone");
            c.That(!run.Snapshot().State["rewards"]!.AsArray().Any(row => row!["kind"]!.GetValue<string>() == "card_removal"), "and so is the reward");

            Do(run, "reward:0", "reward:0");
            c.Equal(149, run.Gold, "99 + 50");
            c.Equal("BlockPotion", run.State.Belt[0]?.ClassName, "the potion is in the belt");
            c.Equal("rewards", run.Snapshot().Screen, "the card reward is still there");
            Do(run, "reward:0", "card:0");
            c.Equal("event", run.Snapshot().Screen, "the last row taken closes a screen that is not a room's last");
            c.Equal(1, ((Hall)run.Top!).RewardsClosed, "and the screen under it is told");
            c.Equal(0, ((Hall)run.Top!).RowsLeftWhenClosed, "with nothing left");
        });

        c.Group("a card reward that was opened and skipped stays and lists its cards from then on", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "SKIPPED" }, new Rooms { Fights = [RoomType.Monster] });
            run.State.PotionChance = -1f;
            WinFight(run);
            var set = ((RewardsScreen)run.Top!).Set;
            var offered = set.Rewards.OfType<CardReward>().Single().Cards.ToList();
            Do(run, "reward:1", "skip");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "skip returns to the rewards");
            var row = s.State["rewards"]!.AsArray()[1]!;
            c.Equal("n,kind,count,cards", Keys(row), "the row now has its cards");
            c.Equal(3, row["count"]!.GetValue<int>(), "count stays");
            var listed = row["cards"]!.AsArray();
            c.Sequence(offered.Select(card => card.Title), listed.Select(card => card!["name"]!.GetValue<string>()), "the cards in offer order");
            c.That(listed.All(card => Keys(card) == "name,cost,type,text,rarity"), "each as an offer, without n");
            c.Equal("reward:0 reward:1 proceed", Ids(s), "it can be opened again");
            c.Equal(10, run.State.Deck.Count, "nothing was taken");
            Do(run, "reward:1", "card:2");
            c.Equal(offered[2].Def.ClassName, run.State.Deck[^1].Def.ClassName, "the second time a card is taken");
            c.Sequence(["gold"], run.Snapshot().State["rewards"]!.AsArray().Select(r => r!["kind"]!.GetValue<string>()), "and the row is gone");
        });

        c.Group("a card reward of named cards, and one that cannot be skipped", () =>
        {
            // CardReward's second constructor: the cards are the ones given, nothing is rolled; hooks may still upgrade
            // them when the reward is filled, but a relic obtained later does not change them.
            var run = StandingWith("NAMED", () => new AttackEgg());
            var named = new CardReward(run, [NewCard(run, "Anger"), NewCard(run, "ShrugItOff")], CardCreationSource.Other) { CanSkip = false };
            RunRewards.OfferCustom(run, [named, new RelicReward(new AttackEgg())]);
            c.Equal(0, RewardsCounter(run), "no number is drawn for named cards");
            c.Sequence(["Anger+", "Shrug It Off"], named.Cards.Select(card => card.Title), "the relic the player holds upgraded the Attack when the reward was filled");
            c.That(!named.HearsRelics && RunRewards.PendingCardRewards(run).Count == 0, "such a reward does not wait for relics");
            c.Equal(1, run.State.FindRelic<AttackEgg>()!.OffersChanged, "the relic was told in the card-offer form");
            c.Equal("""{"n":1,"kind":"card","count":2}""", Text(run.Snapshot().State["rewards"]!.AsArray()[1]), "its row counts two cards");
            c.Equal("Open card reward (2 cards)", Label(run.Snapshot(), "reward:1"), "and says so in its label");

            Do(run, "reward:1");
            var s = run.Snapshot();
            c.Equal("card:0 card:1", Ids(s), "without a Skip button a card has to be taken");
            c.Equal("Unknown card reward action 'skip'.", run.Execute("skip").Error, "skip is refused: the button does not exist");
            Do(run, "card:1");
            c.Equal("ShrugItOff", run.State.Deck[^1].Def.ClassName, "the card taken");
        });

        c.Group("what the rewards screen refuses, in the reader's words", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "REFUSALS" }, new Rooms { Fights = [RoomType.Monster] });
            run.State.PotionChance = -1f;
            WinFight(run);
            var before = run.Dump();
            c.Equal("There is no reward 7.", run.Execute("reward:7").Error, "a row that is not there");
            c.Equal("Unknown rewards action 'dance'.", run.Execute("dance").Error, "an id of another screen");
            c.Equal("Unknown rewards action 'reward:x'.", run.Execute("reward:x").Error, "an id that is not a number");
            c.Equal("'potion:0' is not available now.", run.Execute("potion:0").Error, "a potion id with an empty belt");
            c.Equal("'discard_potion:5' is not available now.", run.Execute("discard_potion:5").Error, "a slot that does not exist");
            c.Equal(before, run.Dump(), "a refused action changes nothing");

            Do(run, "reward:1");
            c.Equal("There is no card 3.", run.Execute("card:3").Error, "card reward: a card that is not there");
            c.Equal("Unknown card reward action 'proceed'.", run.Execute("proceed").Error, "card reward: an id of another screen");
            c.Equal("Unknown card reward action 'potion:0'.", run.Execute("potion:0").Error, "card reward: no potion actions here");
            c.Equal("reward:1", run.Actions[^1], "the save's last entry is the last action that was accepted");
            c.That(run.Actions.All(a => a is not ("dance" or "reward:7" or "card:3")), "a refused id is not in the save");
        });
    }

    private static void PotionRows(Check c)
    {
        c.Group("a potion reward with a full belt is listed but cannot be taken until a slot is free", () =>
        {
            var setup = new RunSetup { Seed = "FULLBELT", Potions = ["FirePotion", "BloodPotion", "FairyInABottle"], Hp = 50 };
            var run = new Arena.Run(setup, new Rooms { Fights = [RoomType.Monster] });
            run.State.PotionChance = 2f;
            // The dummy never hits, the three potions stay: the fight is won with the first Strike.
            WinFight(run);
            var s = run.Snapshot();
            var row = s.State["rewards"]!.AsArray()[1]!;
            c.Equal("potion", row["kind"]!.GetValue<string>(), "the potion row");
            c.Equal("n,kind,name,text,target,available,note", Keys(row), "ends with available and note");
            c.Equal("false", Text(row["available"]), "available: false");
            c.Equal("potion slots full: use or discard a potion to take it", row["note"]!.GetValue<string>(), "the reader's note");
            c.Equal("reward:0 reward:2 proceed discard_potion:0 potion:1 discard_potion:1 discard_potion:2", Ids(s), "its id is not offered; the potion ids follow proceed, a slot's use before its discard");
            c.Sequence([false, true, false], s.State["potions"]!.AsArray().Select(p => p!["usable"]!.GetValue<bool>()), "usable: only the potion that can be used at any time");
            var name = row["name"]!.GetValue<string>();
            c.Equal($"Cannot take the {name} reward: potion slots full: use or discard a potion to take it.", run.Execute("reward:1").Error, "the refusal");

            Do(run, "discard_potion:2");
            s = run.Snapshot();
            c.Equal("n,kind,name,text,target", Keys(s.State["rewards"]!.AsArray()[1]), "with a free slot the note is gone");
            c.Equal("reward:0 reward:1 reward:2 proceed discard_potion:0 potion:1 discard_potion:1", Ids(s), "and the potion can be taken");
            Do(run, "reward:1");
            c.Equal(run.State.Belt[2]?.Name, name, "it went into the freed slot");
            c.Sequence(["gold", "card"], run.Snapshot().State["rewards"]!.AsArray().Select(r => r!["kind"]!.GetValue<string>()), "its row is gone");

            // Using a potion makes room too: Blood Potion heals 20% of 80 max HP. Burning Blood had healed 6 after the fight.
            Do(run, "potion:1");
            c.Equal(72, run.Hp, "50 + 6 + 16");
            c.Equal(null, run.State.Belt[1], "the slot is empty");
        });

        c.Group("when the potion ids are all there is, they are the decision only if they can make room for a potion reward", () =>
        {
            // A screen that is not a room's last, every reward to be taken (no proceed), one potion, a full belt.
            var run = Standing("ONLYPOTIONS", new RunSetup { Seed = "ONLYPOTIONS", Potions = ["FirePotion", "FirePotion", "FirePotion"] });
            new RewardsSet().WithCustomRewards([new PotionReward(Content.Potion("BlockPotion"))]).WithSkippingDisallowed().Offer(run);
            var s = run.Snapshot();
            c.That(s.AwaitingDecision, "a decision");
            c.Equal("discard_potion:0 discard_potion:1 discard_potion:2", Ids(s), "the potion ids alone");
            c.Equal("The proceed button is not enabled yet.", run.Execute("proceed").Error, "no proceed while every reward has to be taken");
            Do(run, "discard_potion:1");
            c.Equal("reward:0 discard_potion:0 discard_potion:2", Ids(run.Snapshot()), "with room the reward is offered, still without proceed");
            Do(run, "reward:0");
            c.Equal("event", run.Snapshot().Screen, "taking the last reward closes the screen");
            c.Sequence(["FirePotion", "BlockPotion", "FirePotion"], run.State.Belt.Slots.Select(p => p?.ClassName), "the potion took the freed slot");
        });

        c.Group("a belt that grew holds a fourth potion", () =>
        {
            var run = Standing("BIGBELT", new RunSetup { Seed = "BIGBELT", Potions = ["FirePotion", "FirePotion", "FirePotion"] });
            run.State.Belt.Grow(1);
            RunRewards.OfferCustom(run, [new PotionReward(Content.Potion("BlockPotion"))]);
            var s = run.Snapshot();
            c.Equal(4, s.State["potions"]!.AsArray().Count, "four slots in the potions block");
            c.Equal("null", Text(s.State["potions"]!.AsArray()[3]), "the new one empty");
            c.That(s.State["rewards"]!.AsArray()[0]!["note"] is null, "the potion reward is not blocked");
            Do(run, "reward:0");
            c.Equal("BlockPotion", run.State.Belt[3]?.ClassName, "it went into the fourth slot");
        });
    }

    private static void ScreensThatAreNotARoomsLast(Check c)
    {
        c.Group("rewards an event or a relic offers: Skip closes the screen, what is left is lost", () =>
        {
            var run = Standing("CUSTOM");
            var card = new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3);
            RunRewards.OfferCustom(run, [card, new GoldReward(20)]);
            c.That(card.HearsRelics, "the card reward waits");
            var s = run.Snapshot();
            c.Equal("Skip (leave without the unclaimed rewards)", Label(s, "proceed"), "the proceed button of such a screen always reads Skip");
            Do(run, "proceed");
            c.Equal("event", run.Snapshot().Screen, "the screen is closed and the one under it shows");
            var hall = (Hall)run.Top!;
            c.Equal(1, hall.RewardsClosed, "which is told");
            c.Equal(2, hall.RowsLeftWhenClosed, "with what was left");
            c.Equal(0, hall.StillHearingRelics, "the card reward that was left behind no longer waits for relics");
            c.Equal(99, run.Gold, "nothing was gained");
            c.Equal(0, ((Rooms)run.Director).RoomsFinished, "and the room is not finished by it");
        });

        c.Group("no rewards, no screen", () =>
        {
            var run = Standing("EMPTY");
            c.Equal(null, RunRewards.OfferCustom(run, []), "an empty custom set shows nothing");
            c.Equal(null, RunRewards.OfferForRoomEnd(run, RewardRoom.Treasure), "a treasure room has no rewards of its own");
            c.Equal(null, RunRewards.OfferForRoomEnd(run, RewardRoom.Combat(RoomType.Monster) with { ShouldGiveRewards = false }), "an encounter that gives no rewards has no rewards screen");
            c.Equal(0, RewardsCounter(run), "and none of the three drew a number");
            c.That(run.Top is Hall, "nothing was put up");

            var threw = false;
            try { RunRewards.GenerateForRoomEnd(run, new RewardRoom { Kind = RoomKind.Event }); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a room that is neither a fight's nor a treasure room has no rewards to generate: the game refuses it too");

            run.State.Hp = 0;
            c.Equal(null, RunRewards.OfferCustom(run, [new GoldReward(5)]), "a dead player is offered nothing");
        });

        c.Group("rewards that must all be taken have no proceed button", () =>
        {
            var run = Standing("NOSKIP");
            new RewardsSet().WithCustomRewards([new RelicReward(new Trinket()), new GoldReward(7)]).WithSkippingDisallowed().Offer(run);
            c.Equal("reward:0 reward:1", Ids(run.Snapshot()), "the rows alone");
            Do(run, "reward:0", "reward:0");
            c.Equal("event", run.Snapshot().Screen, "closed once both were taken");
            c.Equal(106, run.Gold, "99 + 7");
            c.That(run.State.HasRelic("Trinket"), "and the relic");
        });
    }

    // ---------- potions outside fights ----------

    private static void PotionsOutsideFights(Check c)
    {
        c.Group("potions outside a fight: what is offered, in which order, under which labels", () =>
        {
            var run = Standing("BELT", new RunSetup { Seed = "BELT", Hp = 50, Potions = ["FirePotion", "BloodPotion", "FruitJuice"] });
            var block = RunPotions.Describe(run);
            c.Equal(3, block.Count, "one entry per slot");
            c.Equal("slot,name,text,target,usable", Keys(block[1]), "the entry's keys");
            c.Sequence([false, true, true], block.Select(p => p!["usable"]!.GetValue<bool>()), "usable: the two potions the game lets be used at any time");
            c.Sequence(["AnyEnemy", "AnyPlayer", "AnyPlayer"], block.Select(p => p!["target"]!.GetValue<string>()), "the targets");
            var legal = RunPotions.Legal(run);
            c.Sequence(["discard_potion:0", "potion:1", "discard_potion:1", "potion:2", "discard_potion:2"], legal.Select(a => a.Id), "slot by slot, a use before its discard");
            c.Sequence(["Discard Fire Potion", "Use Blood Potion", "Discard Blood Potion", "Use Fruit Juice", "Discard Fruit Juice"], legal.Select(a => a.Label), "the labels");

            c.That(!RunPotions.TryExecute(run, "proceed", out _), "an id that is no potion id is left to the screen");
            c.That(RunPotions.TryExecute(run, "potion:0", out var refused) && !refused.Ok, "a potion id that is not offered is answered here");
            c.Equal("'potion:0' is not available now.", refused.Error, "in the reader's words");
        });

        c.Group("using a potion outside a fight: it leaves the belt, then its effect, with the hooks around it", () =>
        {
            var witness = new Witness();
            var run = Standing("DRINK", new RunSetup { Seed = "DRINK", Hp = 50, MaxHp = 84, Potions = ["FirePotion", "BloodPotion", "FruitJuice"], ExtraRelics = [() => new Witness()] });
            // Blood Potion: 20% of 84 max HP is 16.8; 50 + 16.8 = 66.8, cut to 66.
            c.That(RunPotions.TryExecute(run, "potion:1", out var result) && result.Ok, "used");
            c.Equal(66, run.State.Hp, "50 + 16.8, cut to a whole number");
            c.Equal(null, run.State.Belt[1], "the slot is empty");
            // Fruit Juice: 5 max HP and the 5 HP that come with it.
            RunPotions.TryExecute(run, "potion:2", out _);
            c.Equal(89, run.State.MaxHp, "84 + 5 max HP");
            c.Equal(71, run.State.Hp, "66 + 5 HP");
            RunPotions.TryExecute(run, "discard_potion:0", out _);
            c.Sequence([null, null, null], run.State.Belt.Slots.Select(p => p?.ClassName), "the belt is empty");
            c.Equal("before:BloodPotion@50 after:BloodPotion@66 before:FruitJuice@66 after:FruitJuice@71 discarded:FirePotion", run.State.FindRelic<Witness>()!.Heard,
                "the hooks: before a use (the effect has not run), after it, and after a discard");
            c.Equal("", witness.Heard, "(the run made its own relic object)");

            // A heal never goes above max HP.
            var full = Standing("FULL", new RunSetup { Seed = "FULL", Hp = 78, Potions = ["BloodPotion"] });
            RunPotions.Use(full, 0);
            c.Equal(80, full.State.Hp, "78 + 16 stops at 80");
        });

        c.Group("gaining a potion: the first free slot, a slot by number, a full belt, a hook that keeps it out", () =>
        {
            var run = Standing("GAIN", new RunSetup { Seed = "GAIN", Potions = [null, "FirePotion"], ExtraRelics = [() => new Witness()] });
            c.Equal(PotionProcureResult.Success, RunPotions.TryToProcure(run, Content.Potion("BlockPotion")), "into the first free slot");
            c.Equal(PotionProcureResult.TooFull, RunPotions.TryToProcure(run, Content.Potion("WeakPotion"), slot: 1), "a slot that is taken refuses");
            c.Equal(PotionProcureResult.Success, RunPotions.TryToProcure(run, Content.Potion("WeakPotion"), slot: 2), "a free slot by number");
            c.Equal(PotionProcureResult.TooFull, RunPotions.TryToProcure(run, Content.Potion("SwiftPotion")), "a full belt refuses and nothing is pushed out");
            c.Sequence(["BlockPotion", "FirePotion", "WeakPotion"], run.State.Belt.Slots.Select(p => p?.ClassName), "the belt");
            c.Equal("procured:BlockPotion procured:WeakPotion", run.State.FindRelic<Witness>()!.Heard, "the listeners heard of the two that went in");
        });

        c.Group("no potion actions for a dead player, while an event holds the belt, or during a fight", () =>
        {
            var run = Standing("HELD", new RunSetup { Seed = "HELD", Potions = ["BloodPotion"], Hp = 40 });
            run.State.Part<RewardsState>().CanRemovePotions = false;
            c.Equal(0, RunPotions.Legal(run).Count, "none while the belt is held");
            c.Equal("false", Text(RunPotions.Describe(run)[0]!["usable"]), "and the potion reads as not usable");
            c.That(RunPotions.TryExecute(run, "potion:0", out var held) && !held.Ok, "its id is refused");
            run.State.Part<RewardsState>().CanRemovePotions = true;
            c.Equal(2, RunPotions.Legal(run).Count, "back when the belt is free");
            run.State.Hp = 0;
            c.Equal(0, RunPotions.Legal(run).Count, "none for a dead player");

            var fighting = new Arena.Run(new RunSetup { Seed = "INFIGHT", Potions = ["BloodPotion"] }, new Rooms { Fights = [RoomType.Monster] });
            var threw = false;
            try { RunPotions.Legal(fighting); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "during a fight the fight's own potion actions are the ones to use");
        });

        c.Group("every potion the game lets be used at any time works outside a fight", () =>
        {
            // The game has four: Blood Potion and Fruit Juice (built), Entropic Brew (left out), Foul Potion (an event's, not in the Ironclad's list).
            var anyTime = Content.Registry.Potions.Values.Where(p => p.Usage == PotionUsage.AnyTime).ToList();
            c.That(anyTime.Select(p => p.ClassName).Contains("BloodPotion") && anyTime.Select(p => p.ClassName).Contains("FruitJuice"), "Blood Potion and Fruit Juice are among them");
            foreach (var potion in anyTime)
            {
                var run = Standing("ANYTIME", new RunSetup { Seed = "ANYTIME", Hp = 40, Potions = [potion.ClassName] });
                var before = run.Dump();
                var threw = "";
                try { RunPotions.Use(run, 0); }
                catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }
                c.Equal("", threw, $"{potion.ClassName}: its effect runs with no fight on");
                c.That(run.State.Belt[0] is null && run.Dump() != before, $"{potion.ClassName}: it left the belt and did something");
            }
            c.That(anyTime.All(p => p.CanUse is null), "none of them has a use condition written for a fight");
            c.That(!RunPotions.Usable(Content.Potion("FairyInABottle")) && !RunPotions.Usable(Content.Potion("FirePotion")) && !RunPotions.Usable(Content.Potion("BlockPotion")),
                "a potion that works by itself, one for an enemy and one for fights only cannot be used outside a fight");
        });
    }

    // ---------- the hooks ----------

    /// <summary>Prayer Wheel's shape: a second card reward after a hallway fight.</summary>
    private sealed class SecondOffer : Relic, IRewardHooks
    {
        public int Told;
        public override string Name => "Second Offer";
        public override string Describe() => "Hallway fights give a second card reward.";

        public bool TryModifyRewards(RunContext c, List<Reward> rewards, RoomKind? room)
        {
            if (room != RoomKind.Monster) return false;
            rewards.Add(new CardReward(c.Run!, CardCreationOptions.ForRoom(RoomKind.Monster), 3));
            return true;
        }

        public override void AfterModifyingRewards(RunContext c) => Told++;
    }

    /// <summary>Amethyst Aubergine's shape: 15 gold more after every fight, as a row of its own; and a gold row in a treasure room.</summary>
    private sealed class Tip : Relic, IRewardHooks
    {
        public int Told;
        public override string Name => "Tip";
        public override string Describe() => "Fights give 15 gold more.";

        public bool TryModifyRewardsLate(RunContext c, List<Reward> rewards, RoomKind? room)
        {
            if (room is null || !(room.Value.IsCombat() || room == RoomKind.Treasure)) return false;
            rewards.Add(new GoldReward(15));
            return true;
        }

        public override void AfterModifyingRewards(RunContext c) => Told++;
    }

    /// <summary>White Beast Statue's shape.</summary>
    private sealed class SurePotion : Relic
    {
        public override string Name => "Sure Potion";
        public override string Describe() => "Every fight drops a potion.";
        public override bool ShouldForcePotionReward(RunContext c, RoomKind room) => room.IsCombat();
    }

    /// <summary>Holds the proceed button back, keeps potions out.</summary>
    private sealed class Warden : Relic, IRewardHooks
    {
        public override string Name => "Warden";
        public override string Describe() => "No way on, no potions.";
        public bool ShouldProceedToNextMapPoint(RunContext c) => false;
        public bool ShouldProcurePotion(RunContext c, PotionDef potion) => false;
    }

    /// <summary>The Eggs' shape: an offered Attack that can be upgraded is offered upgraded, through a copy.</summary>
    private sealed class AttackEgg : Relic, IRewardHooks
    {
        public int RewardsChanged;
        public int OffersChanged;
        public override string Name => "Attack Egg";
        public override string Describe() => "Offered Attacks are upgraded.";

        public bool TryModifyCardRewardOptionsLate(RunContext c, List<CardCreationResult> cards, CardCreationOptions options)
        {
            if (options.Flags.HasFlag(CardCreationFlags.NoHookUpgrades)) return false;
            foreach (var result in cards)
            {
                var card = result.Card;
                if (card.Type != CardType.Attack || !card.IsUpgradable) continue;
                result.ModifyCard(c.Run!.State.NewCard(card.Def, upgraded: true), this);
            }
            return true;
        }

        public override void AfterModifyingRewards(RunContext c) => RewardsChanged++;
        public override void AfterModifyingCardRewardOptions(RunContext c) => OffersChanged++;
    }

    /// <summary>Dingy Rug's shape: changes the offer's own options object, here to Attacks only.</summary>
    private sealed class AttacksOnly : Relic, IRewardHooks
    {
        public int Asked;
        public int Told;
        public override string Name => "Attacks Only";
        public override string Describe() => "Card rewards hold Attacks only.";

        public CardCreationOptions ModifyCardRewardCreationOptions(RunContext c, CardCreationOptions options)
        {
            Asked++;
            if (options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications) || !options.Flags.HasFlag(CardCreationFlags.IsCardReward)) return options;
            if (options.CardPoolFilter is not null || options.CustomCardPool is not null) return options;
            return options.WithCardPools(options.CardPools.ToList(), card => card.Type == CardType.Attack);
        }

        public override void AfterModifyingCardRewardOptions(RunContext c) => Told++;
        public override void AfterModifyingRewards(RunContext c) => Told++;
    }

    /// <summary>Every reward card that can be upgraded comes upgraded; a second card may be taken; a button the arena leaves out.</summary>
    private sealed class Generous : Relic, IRewardHooks
    {
        public bool AddsReroll;
        public override string Name => "Generous";
        public override string Describe() => "Upgraded reward cards, and two of them.";
        public decimal ModifyCardRewardUpgradeOdds(RunContext c, CardInstance card, decimal odds) => 1m;
        public bool ShouldAllowSelectingMoreCardRewards(RunContext c, CardReward reward) => true;

        public bool TryModifyCardRewardAlternatives(RunContext c, CardReward reward, List<string> alternatives)
        {
            if (!AddsReroll) return false;
            alternatives.Add("REROLL");
            return true;
        }
    }

    private static Arena.Run StandingWith(string seed, params Func<Relic>[] relics) =>
        Standing(seed, new RunSetup { Seed = seed, ExtraRelics = relics });

    private static void Hooks(Check c)
    {
        c.Group("TryModifyRewards: a reward a relic adds is filled after the room's own, and the rows are sorted afterwards", () =>
        {
            var run = StandingWith("SECOND", () => new SecondOffer());
            run.State.PotionChance = -1f;
            var plain = Standing("SECOND");
            plain.State.PotionChance = -1f;

            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            var without = RunRewards.GenerateForRoomEnd(plain, RewardRoom.Combat(RoomType.Monster));
            c.Sequence(["gold", "card", "card"], set.Rewards.Select(r => r.Kind), "gold and two card rewards");
            // 1 + 1 + 9 for the room's own, then 9 more for the added reward.
            c.Equal(20, RewardsCounter(run), "numbers drawn: 11 and 9");
            var rewards = set.Rewards.OfType<CardReward>().ToList();
            c.Sequence(ClassNames(without.Rewards.OfType<CardReward>().Single().Cards), ClassNames(rewards[0].Cards), "the room's own card reward is the same as without the relic: it was rolled first");
            c.That(rewards[0].Sequence < rewards[1].Sequence, "and the added one was made after it");
            c.Equal(1, run.State.FindRelic<SecondOffer>()!.Told, "the relic was told once that it changed the rewards");

            var boss = StandingWith("SECOND", () => new SecondOffer());
            var bossSet = RunRewards.GenerateForRoomEnd(boss, RewardRoom.Combat(RoomType.Boss));
            c.Equal(1, bossSet.Rewards.Count(r => r is CardReward), "the hook is told the kind of room: nothing is added after a boss");
            c.Equal(0, boss.State.FindRelic<SecondOffer>()!.Told, "and a relic that changed nothing is not told");
        });

        c.Group("a fixed amount of gold: a row a hook adds draws nothing, a row the set was made with draws its number", () =>
        {
            // RewardsSet.GenerateWithoutOffering fills every reward of the set, also a gold row whose amount is known
            // (GoldReward.Populate always rolls); a reward a hook adds is only filled when it is not filled yet.
            var run = StandingWith("TIP", () => new Tip());
            run.State.PotionChance = -1f;
            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            c.Sequence(["gold", "gold", "card"], set.Rewards.Select(r => r.Kind), "the tip stands with the gold");
            c.Equal(15, set.Rewards.OfType<GoldReward>().Last().Amount, "15 gold, behind the room's own gold");
            c.Equal(11, RewardsCounter(run), "the same 11 numbers as without it");
            c.Equal(1, run.State.FindRelic<Tip>()!.Told, "the late pass is told too");

            var custom = Standing("TIP");
            RunRewards.GenerateCustom(custom, [new GoldReward(15)]);
            c.Equal(1, RewardsCounter(custom), "a custom set's fixed gold takes one number");

            // A treasure room: nothing of its own, but the hooks are asked, and what they add is shown on a screen that is not the room's last.
            var chest = StandingWith("TIP", () => new Tip());
            var screen = RunRewards.OfferForRoomEnd(chest, RewardRoom.Treasure);
            c.That(screen is { IsTerminal: false }, "a treasure room's extra rewards get a screen that closes by itself");
            c.Equal("""[{"n":0,"kind":"gold","amount":15}]""", Text(chest.Snapshot().State["rewards"]), "with the row the relic added");
            c.Equal(0, RewardsCounter(chest), "and no number drawn");
        });

        c.Group("ShouldForcePotionReward: the drop is certain, the number is drawn all the same, the chance goes down", () =>
        {
            var run = StandingWith("SURE", () => new SurePotion());
            run.State.PotionChance = -1f;
            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            c.That(set.Rewards.Any(r => r is PotionReward), "a potion although the chance was below 0");
            c.Equal(13, RewardsCounter(run), "13 numbers: the roll, the gold, the potion's two, the cards");
            c.Equal(-1.1f, run.State.PotionChance, "the chance went down by 0.1 as after any drop");
        });

        c.Group("AfterRewardTaken: told for a reward that was taken, not for one that was skipped or bounced", () =>
        {
            var run = Standing("TAKEN", new RunSetup { Seed = "TAKEN", Potions = ["FirePotion", "FirePotion", "FirePotion"], ExtraRelics = [() => new Witness()] });
            RunRewards.OfferCustom(run,
            [
                new GoldReward(3), new PotionReward(Content.Potion("BlockPotion")),
                new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3),
            ]);
            Do(run, "reward:0");
            Do(run, "reward:1", "skip");
            c.Equal("taken:gold", run.State.FindRelic<Witness>()!.Heard, "the gold was taken; the skipped card reward was not");
            Do(run, "discard_potion:0", "reward:0", "reward:0", "card:1");
            c.Equal("taken:gold discarded:FirePotion procured:BlockPotion taken:potion taken:card", run.State.FindRelic<Witness>()!.Heard, "then the potion and, the second time, the card reward");
        });

        c.Group("ShouldProceedToNextMapPoint and ShouldProcurePotion", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "WARDEN", ExtraRelics = [() => new Warden()] }, new Rooms { Fights = [RoomType.Monster] });
            run.State.PotionChance = 2f;
            WinFight(run);
            var s = run.Snapshot();
            c.Equal("reward:0 reward:1 reward:2", Ids(s), "no proceed while a listener holds it back");
            c.Equal("The proceed button is not enabled yet.", run.Execute("proceed").Error, "and the id is refused");
            // PotionReward.OnSelect: a potion a hook keeps out is not a full belt, so the reward counts as taken.
            Do(run, "reward:1");
            c.Sequence([null, null, null], run.State.Belt.Slots.Select(p => p?.ClassName), "the potion was kept out of the belt");
            c.Sequence(["gold", "card"], run.Snapshot().State["rewards"]!.AsArray().Select(r => r!["kind"]!.GetValue<string>()), "and its row is used up all the same");
        });
    }

    private static void CardOfferHooks(Check c)
    {
        c.Group("TryModifyCardRewardOptionsLate: offered Attacks come upgraded, as copies; the rolled cards stay as they were", () =>
        {
            var run = StandingWith("EGG", () => new AttackEgg());
            var plain = Standing("EGG");
            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            var without = RunRewards.GenerateForRoomEnd(plain, RewardRoom.Combat(RoomType.Monster));
            var results = set.Rewards.OfType<CardReward>().Single().Results;
            var rolled = without.Rewards.OfType<CardReward>().Single().Cards;
            c.Sequence(ClassNames(rolled), ClassNames(results.Select(r => r.OriginalCard)), "the same three cards are rolled with the relic and without it");
            c.Equal(RewardsCounter(plain), RewardsCounter(run), "from the same numbers");
            foreach (var result in results)
            {
                var attack = result.OriginalCard.Type == CardType.Attack;
                c.Equal(attack, result.HasBeenModified, $"{result.OriginalCard.Title}: {(attack ? "an Attack, changed" : "no Attack, left alone")}");
                c.Equal(attack, result.Card.IsUpgraded, $"{result.OriginalCard.Title}: the card on offer is {(attack ? "upgraded" : "not upgraded")}");
                c.That(!result.OriginalCard.IsUpgraded, $"{result.OriginalCard.Title}: the rolled card itself was not touched");
                if (attack) c.Sequence(["AttackEgg"], result.ModifyingRelics, "and it says which relic changed it");
            }
            var egg = run.State.FindRelic<AttackEgg>()!;
            c.Equal(1, egg.OffersChanged, "told once that it changed the cards of an offer");
            c.Equal(0, egg.RewardsChanged, "not that it changed rewards");
        });

        c.Group("a relic obtained while a card reward waits changes that reward, until the reward was opened once", () =>
        {
            // CardReward.cs:117,167-181,293: the reward hears the player's relics from the moment it is made; the relic is
            // asked in both passes and told AfterModifyingRewards (not the card-offer form); listening stops when the
            // card reward's screen closes, with a card or without.
            var run = Standing("EGGROW");
            var options = CardCreationOptions.ForNonCombatWithUniformOdds([CardPools.Character], card => card.Type == CardType.Attack && card.Rarity == CardRarity.Common);
            var first = new CardReward(run, options, 3);
            var second = new CardReward(run, CardCreationOptions.ForNonCombatWithUniformOdds([CardPools.Character], card => card.Type == CardType.Attack && card.Rarity == CardRarity.Common), 3);
            RunRewards.OfferCustom(run, [new RelicReward(new AttackEgg()), first, second, new RelicReward(new AttackEgg())]);
            var screen = (RewardsScreen)run.Top!;
            c.Sequence([first.Sequence, second.Sequence], RunRewards.PendingCardRewards(run).Select(r => r.Sequence), "both card rewards wait");
            c.That(screen.Set.Rewards.OfType<CardReward>().SelectMany(r => r.Cards).All(card => !card.IsUpgraded), "six plain Attacks to begin with");

            // Open and skip the first card reward: it stops listening.
            Do(run, "reward:2", "skip");
            c.Equal(1, RunRewards.PendingCardRewards(run).Count, "after a skip only the other one still waits");

            Do(run, "reward:0");
            var cardRewards = ((RewardsScreen)run.Top!).Set.Rewards.OfType<CardReward>().OrderBy(r => r.Sequence).ToList();
            c.That(cardRewards[0].Cards.All(card => !card.IsUpgraded), "the reward that was opened and skipped keeps its plain cards");
            c.That(cardRewards[1].Cards.All(card => card.IsUpgraded), "the reward that still waited now offers its three Attacks upgraded");
            var egg = run.State.FindRelic<AttackEgg>()!;
            c.Equal(1, egg.RewardsChanged, "the relic was told that it changed rewards, once: for the one reward that listened");
            c.Equal(0, egg.OffersChanged, "and not in the card-offer form: the game calls the other one here");

            // Leaving the screen ends the waiting too.
            Do(run, "proceed");
            c.Equal(0, RunRewards.PendingCardRewards(run).Count, "no reward waits once the screen is left");
        });

        c.Group("ModifyCardRewardCreationOptions: asked before every card, and a change to the options lasts for the cards after it", () =>
        {
            var run = StandingWith("ATTACKS", () => new AttacksOnly());
            var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            var reward = set.Rewards.OfType<CardReward>().Single();
            c.That(reward.Cards.All(card => card.Type == CardType.Attack), $"three Attacks ({string.Join(", ", reward.Cards.Select(card => card.Title))})");
            c.Equal(3, run.State.FindRelic<AttacksOnly>()!.Asked, "asked once per card");
            c.Equal(0, run.State.FindRelic<AttacksOnly>()!.Told, "it changed where the cards come from, not the rolled cards or the rewards: it is told neither");
            c.That(reward.Options.CardPoolFilter is not null, "the reward's own options carry the change");

            // An offer that is no card reward is left alone by this relic (it tests the flag, as Dingy Rug does).
            var other = StandingWith("ATTACKS", () => new AttacksOnly());
            var options = CardCreationOptions.ForNonCombatWithUniformOdds([CardPools.Character], card => card.ClassName == "ShrugItOff");
            c.Sequence(["ShrugItOff"], ClassNames(CardFactory.CreateForReward(other, 1, options).Select(r => r.Card)), "an offer without the card-reward flag keeps its own filter");
        });

        c.Group("ModifyCardRewardUpgradeOdds, ShouldAllowSelectingMoreCardRewards, and a button the arena leaves out", () =>
        {
            var run = new Arena.Run(new RunSetup { Seed = "GENEROUS", ExtraRelics = [() => new Generous()] }, new Rooms { Fights = [RoomType.Monster] });
            run.State.PotionChance = -1f;
            WinFight(run);
            var reward = ((RewardsScreen)run.Top!).Set.Rewards.OfType<CardReward>().Single();
            // RollForUpgrade: the roll is at or below odds of 1 whatever it is.
            c.That(reward.Cards.All(card => card.IsUpgraded), $"all three come upgraded ({string.Join(", ", reward.Cards.Select(card => card.Title))})");
            var titles = reward.Cards.Select(card => card.Title).ToList();

            Do(run, "reward:1", "card:0");
            var s = run.Snapshot();
            c.Equal("card_reward", s.Screen, "after a card was taken the card reward stays open for another");
            c.Sequence([titles[1], titles[2]], s.State["cards"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "with the two cards left");
            c.Equal("card:0 card:1 skip", Ids(s), "numbered again");
            c.Equal(11, s.State["player"]!["deck_size"]!.GetValue<int>(), "the first card is in the deck already");
            Do(run, "card:1");
            c.Sequence([titles[0], titles[2]], run.State.Deck.TakeLast(2).Select(card => card.Title), "two cards were taken");
            // The third pick would be allowed too; skipping instead leaves the reward open, as the game's loop has it.
            c.Equal("card_reward", run.Snapshot().Screen, "still open, with one card");
            Do(run, "skip");
            s = run.Snapshot();
            c.Sequence(["gold", "card"], s.State["rewards"]!.AsArray().Select(r => r!["kind"]!.GetValue<string>()), "a skip after two picks leaves the reward on the screen");
            c.Equal(1, s.State["rewards"]!.AsArray()[1]!["count"]!.GetValue<int>(), "with the one card left");

            var withReroll = StandingWith("REROLL", () => new Generous { AddsReroll = true });
            var threw = false;
            try { RunRewards.GenerateForRoomEnd(withReroll, RewardRoom.Combat(RoomType.Monster)); }
            catch (NotSupportedException ex) { threw = ex.Message.Contains("REROLL", StringComparison.Ordinal); }
            c.That(threw, "a Reroll button is left out by name: making a card reward with it is refused");
        });
    }

    // ---------- the card factory ----------

    /// <summary>How many numbers of a seed's rewards stream to pass so that the numbers after them fit the test.</summary>
    private static int Place(string seed, Func<GameRng, bool> fits)
    {
        var stream = new RunStreams(seed).Long(RunStream.Rewards);
        for (var skip = 0; skip < 200_000; skip++)
        {
            if (fits(stream.Clone())) return skip;
            stream.NextU64();
        }
        throw new InvalidOperationException("The stream has no such place.");
    }

    /// <summary>The first seed number from 1 whose stream fits the test.</summary>
    private static ulong SeedWhere(Func<GameRng, bool> fits)
    {
        for (ulong seed = 1; seed < 100_000; seed++)
            if (fits(new GameRng(seed))) return seed;
        throw new InvalidOperationException("No seed fits.");
    }

    /// <summary>The numbers a stream gives next, as fractions.</summary>
    private static float[] Next(GameRng rng, int count)
    {
        var numbers = new float[count];
        for (var i = 0; i < count; i++) numbers[i] = rng.NextFloat();
        return numbers;
    }

    private static Arena.Run At(string seed, int drawn, bool strict = false, params Func<Relic>[] relics) =>
        Standing(seed, new RunSetup { Seed = seed, ExtraRelics = relics }, new Rooms { RewardsDrawn = drawn, Strict = strict });

    private static readonly GameCard AngerCard = new("Anger", CardType.Attack, CardRarity.Common);
    private static readonly GameCard ColossusCard = new("Colossus", CardType.Skill, CardRarity.Uncommon);
    private static readonly GameCard BarricadeCard = new("Barricade", CardType.Power, CardRarity.Rare);
    private static readonly GameCard[] OneOfEach = [AngerCard, ColossusCard, BarricadeCard];

    private static void Options(Check c)
    {
        c.Group("the options of a room's own cards, and of an event's or a relic's", () =>
        {
            // CardCreationOptions.ForRoom and the two ForNonCombat forms.
            var monster = CardCreationOptions.ForRoom(RoomKind.Monster);
            c.That(monster.Source == CardCreationSource.Encounter && monster.RarityOdds == CardOddsType.Regular && monster.Flags == CardCreationFlags.None, "a hallway fight: a fight's reward on the regular table, no flags");
            c.That(ReferenceEquals(monster.CardPools.Single(), CardPools.Character), "from the character's pool");
            c.Equal(CardOddsType.Elite, CardCreationOptions.ForRoom(RoomKind.Elite).RarityOdds, "an elite: the elite table");
            c.Equal(CardOddsType.Boss, CardCreationOptions.ForRoom(RoomKind.Boss).RarityOdds, "a boss: the boss table");
            var shop = CardCreationOptions.ForRoom(RoomKind.Shop);
            c.That(shop.Source == CardCreationSource.Shop && shop.RarityOdds == CardOddsType.Shop, "a shop: its own source and table");
            var chest = CardCreationOptions.ForRoom(RoomKind.Treasure);
            c.That(chest.Source == CardCreationSource.Other && chest.RarityOdds == CardOddsType.Regular, "any other room: no fight's reward, the regular table");
            var threw = false;
            try { CardCreationOptions.ForRoom(RoomKind.Event); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "an event room is refused, as in the game");

            var relic = CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]);
            c.That(relic.Source == CardCreationSource.Other && relic.RarityOdds == CardOddsType.Regular && relic.Flags == CardCreationFlags.NoUpgradeRoll, "default odds outside a fight: the regular table and no upgrade roll");
            var uniform = CardCreationOptions.ForNonCombatWithUniformOdds([CardPools.Character]);
            c.That(uniform.RarityOdds == CardOddsType.Uniform && uniform.Flags == CardCreationFlags.NoUpgradeRoll, "uniform odds outside a fight");

            threw = false;
            try { _ = new CardCreationOptions([AngerCard, new GameCard("IronWave", CardType.Attack, CardRarity.Common)], CardCreationSource.Other, CardOddsType.Regular); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a list of one rarity with rolled rarities is refused: the roll could miss the list");
            c.That(new CardCreationOptions([AngerCard], CardCreationSource.Other, CardOddsType.Uniform).CustomCardPool!.Count == 1, "with uniform odds it is fine");

            var changed = CardCreationOptions.ForRoom(RoomKind.Monster);
            var same = changed.WithFlags(CardCreationFlags.IsCardReward).WithCardPools([CardPools.Character, CardPools.Colorless]);
            c.That(ReferenceEquals(changed, same) && changed.CardPools.Count == 2 && changed.Flags == CardCreationFlags.IsCardReward, "With... changes the object itself and hands it back");
            var copy = changed.Clone();
            copy.WithFlags(CardCreationFlags.NoUpgradeRoll).WithCustomPool(OneOfEach);
            c.That(changed.CardPools.Count == 2 && changed.CustomCardPool is null && changed.Flags == CardCreationFlags.IsCardReward, "a copy is an object of its own");

            var run = Standing("POOL");
            var possible = CardCreationOptions.ForRoom(RoomKind.Monster).GetPossibleCards(run).ToList();
            c.Equal(85, possible.Count, "a single-player run meets 85 of the Ironclad's 87 cards (two are for more players)");
            c.That(possible[0].ClassName == "Aggression" && possible[^1].ClassName == "Whirlwind", "in the pool's order");
        });

        c.Group("a rolled rarity that has no card gives way to the next: Common, Uncommon, Rare, and round to Common", () =>
        {
            // CardFactory.GetNextAllowedRarity with CardRarityExtensions.GetNextHighestRarityWithWrapping.
            static Func<CardRarity, bool> Only(params CardRarity[] allowed) => allowed.Contains;
            c.Equal(CardRarity.Common, CardFactory.GetNextAllowedRarity(CardRarity.Common, Only(CardRarity.Common, CardRarity.Rare)), "an allowed rarity stays");
            c.Equal(CardRarity.Uncommon, CardFactory.GetNextAllowedRarity(CardRarity.Common, Only(CardRarity.Uncommon, CardRarity.Rare)), "Common gives way to Uncommon");
            c.Equal(CardRarity.Rare, CardFactory.GetNextAllowedRarity(CardRarity.Common, Only(CardRarity.Rare)), "and on to Rare");
            c.Equal(CardRarity.Rare, CardFactory.GetNextAllowedRarity(CardRarity.Uncommon, Only(CardRarity.Common, CardRarity.Rare)), "Uncommon gives way to Rare, not back to Common");
            c.Equal(CardRarity.Common, CardFactory.GetNextAllowedRarity(CardRarity.Rare, Only(CardRarity.Common, CardRarity.Uncommon)), "Rare wraps round to Common");
            c.Equal(CardRarity.Uncommon, CardFactory.GetNextAllowedRarity(CardRarity.Rare, Only(CardRarity.Uncommon)), "and on to Uncommon");
            c.Equal(CardRarity.None, CardFactory.GetNextAllowedRarity(CardRarity.Uncommon, Only()), "back at the rolled rarity there is none");
            c.Equal(CardRarity.None, CardFactory.GetNextAllowedRarity(CardRarity.Curse, Only(CardRarity.Common)), "a rarity no roll gives leads nowhere");
        });
    }

    private static void Factory(Check c)
    {
        c.Group("three cards of one offer never repeat: the rolled rarity gives way as the offer empties it", () =>
        {
            // One card of each rarity, an event's or relic's source on the regular table, upgrade rolls on: three numbers
            // per card (rarity, pick, upgrade). Base odds: Rare below 0.03, Uncommon below 0.37, else Common.
            // At a place where the three rarity rolls are all Common: card 1 is the Common one; card 2 finds no Common
            // left and takes the Uncommon one; card 3 finds neither and takes the Rare one.
            const string seed = "FACTORY";
            var place = Place(seed, rng => { var n = Next(rng, 7); return n[0] >= 0.37f && n[3] >= 0.37f && n[6] >= 0.37f; });
            var run = At(seed, place);
            var cards = CardFactory.CreateForReward(run, 3, new CardCreationOptions(OneOfEach, CardCreationSource.Other, CardOddsType.Regular));
            c.Sequence(["Anger", "Colossus", "Barricade"], ClassNames(cards.Select(r => r.Card)), "Common, then Uncommon, then Rare");
            c.Equal(place + 9, RewardsCounter(run), "nine numbers");
            c.Equal(-0.05f, run.State.CardRarityOffset, "an event's or relic's cards do not move the pity offset");

            var four = At(seed, place);
            var threw = false;
            try { CardFactory.CreateForReward(four, 4, new CardCreationOptions(OneOfEach, CardCreationSource.Other, CardOddsType.Regular)); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a fourth card has no rarity left: the game fails here too");
        });

        c.Group("the two rarity rolls: with the pity offset for a fight's reward, on the table alone for everything else", () =>
        {
            // CardRarityOdds: with the offset, Rare below 0.03 + offset and Uncommon below 0.37 more. On the table alone
            // (RollWithBaseOdds) Rare below 0.03 and Uncommon below 0.37, not below 0.40: the game tests Uncommon
            // against its own odds there. So a roll from 0.37 up to 0.40 tells the two apart.
            const string seed = "ODDS";
            var place = Place(seed, rng => rng.NextFloat() is >= 0.37f and < 0.40f);
            CardRarity Roll(CardCreationSource source, CardOddsType table, float pity, bool force, out float pityAfter)
            {
                var run = At(seed, place);
                run.State.CardRarityOffset = pity;
                var options = new CardCreationOptions(OneOfEach, source, table);
                if (force) options.WithFlags(CardCreationFlags.ForceRarityOddsChange);
                var card = CardFactory.CreateForReward(run, 1, options)[0].Card;
                pityAfter = run.State.CardRarityOffset;
                return card.Def.Rarity;
            }

            c.Equal(CardRarity.Common, Roll(CardCreationSource.Other, CardOddsType.Regular, 0f, false, out var after), "an event's card: Common (the roll is not below 0.37)");
            c.Equal(0f, after, "and the pity offset stays");
            c.Equal(CardRarity.Uncommon, Roll(CardCreationSource.Encounter, CardOddsType.Regular, 0f, false, out after), "a fight's card at offset 0: Uncommon (below 0.03 + 0.37)");
            c.Equal(0.01f, after, "and the offset grows by 0.01");
            c.Equal(CardRarity.Rare, Roll(CardCreationSource.Encounter, CardOddsType.Regular, 0.4f, false, out after), "a fight's card at the highest offset: Rare (below 0.03 + 0.4)");
            c.Equal(-0.05f, after, "and the offset falls back to its start");
            c.Equal(CardRarity.Uncommon, Roll(CardCreationSource.Other, CardOddsType.Regular, 0f, true, out after), "an event's card that forces the pity roll: as a fight's");
            c.Equal(0.01f, after, "the offset moves");
            c.Equal(CardRarity.Common, Roll(CardCreationSource.Encounter, CardOddsType.Shop, 0.4f, false, out after), "a fight's source on the shop table is not one of the three pity tables: the table alone");
            c.Equal(0.4f, after, "the offset stays");
        });

        c.Group("uniform odds: no rarity is rolled, and Basic and Ancient cards are never given", () =>
        {
            // At a place where the pick's number is below one third: it would take the first entry of a list of two or three.
            var first = Place("UNIFORM", rng => rng.NextFloat() < 0.33f);
            GameCard[] pool = [new("Bash", CardType.Attack, CardRarity.Basic), new("Break", CardType.Attack, CardRarity.Ancient), AngerCard];
            GameCard[] ancientFirst = [new("Break", CardType.Attack, CardRarity.Ancient), new("Bash", CardType.Attack, CardRarity.Basic), AngerCard];
            var run = At("UNIFORM", first);
            var cards = CardFactory.CreateForReward(run, 1, new CardCreationOptions(pool, CardCreationSource.Other, CardOddsType.Uniform));
            c.Sequence(["Anger"], ClassNames(cards.Select(r => r.Card)), "the one card that is neither Basic nor Ancient");
            c.Sequence(["Anger"], ClassNames(CardFactory.CreateForReward(At("UNIFORM", first), 1, new CardCreationOptions(ancientFirst, CardCreationSource.Other, CardOddsType.Uniform)).Select(r => r.Card)), "also with the Ancient one listed first");
            c.Equal(first + 2, RewardsCounter(run), "two numbers: the pick and the upgrade roll");
            c.Equal(-0.05f, run.State.CardRarityOffset, "the pity offset is untouched");

            var noUpgradeRoll = Standing("UNIFORM");
            CardFactory.CreateForReward(noUpgradeRoll, 1, new CardCreationOptions(pool, CardCreationSource.Other, CardOddsType.Uniform).WithFlags(CardCreationFlags.NoUpgradeRoll));
            c.Equal(1, RewardsCounter(noUpgradeRoll), "one number when the options say no upgrade roll");

            // A card listed twice counts once, and a card for more players is never in the offer. The list then has two
            // cards, Anger and Colossus: a pick from one half up to two thirds takes the second, Colossus. (Counted
            // twice, Anger would fill two thirds of the list and be picked.)
            var upperHalf = Place("UNIFORM", rng => rng.NextFloat() is >= 0.5f and < 0.66f);
            var twice = At("UNIFORM", upperHalf);
            GameCard[] listed = [AngerCard, AngerCard, new("Tank", CardType.Attack, CardRarity.Uncommon, MultiplayerOnly: true), ColossusCard];
            var two = CardFactory.CreateForReward(twice, 2, new CardCreationOptions(listed, CardCreationSource.Other, CardOddsType.Uniform).WithFlags(CardCreationFlags.NoUpgradeRoll));
            c.Sequence(["Colossus", "Anger"], ClassNames(two.Select(r => r.Card)), "two cards of a list that names Anger twice and a card for more players: Colossus by the pick, then Anger, the only one left");
            var threw = false;
            try { CardFactory.CreateForReward(Standing("UNIFORM"), 3, new CardCreationOptions(listed, CardCreationSource.Other, CardOddsType.Uniform)); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a third card does not exist: the game fails here too");
        });

        c.Group("the upgrade roll: the chance is 0.25 per act cleared, and never above 0 for a Rare", () =>
        {
            // CardFactory.RollForUpgrade. A uniform offer of one card takes two numbers: the pick, the upgrade roll.
            const string seed = "UPGRADE";
            bool Upgraded(GameCard card, int act, int place)
            {
                var run = At(seed, place);
                run.State.ActIndex = act;
                return CardFactory.CreateForReward(run, 1, new CardCreationOptions([card], CardCreationSource.Other, CardOddsType.Uniform))[0].Card.IsUpgraded;
            }

            var low = Place(seed, rng => Next(rng, 2)[1] is > 0f and <= 0.25f);
            var middle = Place(seed, rng => Next(rng, 2)[1] is > 0.25f and <= 0.5f);
            c.That(!Upgraded(AngerCard, 0, low), "act 1: a roll of up to 0.25 does not upgrade (the chance is 0)");
            c.That(Upgraded(AngerCard, 1, low), "act 2: it does (the chance is 0.25)");
            c.That(!Upgraded(AngerCard, 1, middle), "act 2: a roll above 0.25 does not");
            c.That(Upgraded(AngerCard, 2, middle), "act 3: a roll of up to 0.5 does (the chance is 0.5)");
            c.That(!Upgraded(BarricadeCard, 2, low), "a Rare card's chance stays 0 in every act");
        });

        c.Group("NoModifyHooks keeps the relics away from an offer", () =>
        {
            var options = new CardCreationOptions([AngerCard], CardCreationSource.Other, CardOddsType.Uniform);
            var asked = StandingWith("NOHOOKS", () => new AttackEgg());
            c.That(CardFactory.CreateForReward(asked, 1, options.Clone())[0].Card.IsUpgraded, "asked: the relic upgrades the Attack");
            var notAsked = StandingWith("NOHOOKS", () => new AttackEgg());
            c.That(!CardFactory.CreateForReward(notAsked, 1, options.Clone().WithFlags(CardCreationFlags.NoModifyHooks))[0].Card.IsUpgraded, "not asked: the card stays as rolled");
        });

        c.Group("a pick that lands on a card the arena has not built is drawn again; in strict mode the run cannot go on", () =>
        {
            // A list of two, the first not built. The pick is one number: below 0.5 it lands on the first.
            const string seed = "LEFTOUT";
            GameCard[] pool = [new("NoSuchCardYet", CardType.Skill, CardRarity.Uncommon), AngerCard];
            var options = new CardCreationOptions(pool, CardCreationSource.Other, CardOddsType.Uniform).WithFlags(CardCreationFlags.NoUpgradeRoll);
            var place = Place(seed, rng => { var n = Next(rng, 2); return n[0] < 0.5f && n[1] >= 0.5f; });

            var run = At(seed, place);
            c.Sequence(["Anger"], ClassNames(CardFactory.CreateForReward(run, 1, options.Clone()).Select(r => r.Card)), "the second pick lands on the built card");
            c.Equal(place + 2, RewardsCounter(run), "two numbers from the same stream");
            var log = run.State.Part<LeftOutLog>().Entries;
            c.That(log.Count == 1 && log[0].Kind == "card" && log[0].ClassName == "NoSuchCardYet", "the run's log has the card that was passed over");

            var strict = At(seed, place, strict: true);
            var message = "";
            try { CardFactory.CreateForReward(strict, 1, options.Clone()); }
            catch (RunUnrepresentableException ex) { message = ex.Message; }
            c.That(message.Contains("NoSuchCardYet", StringComparison.Ordinal), $"strict mode stops at it by name ({message})");

            var nothing = At(seed, place);
            var stopped = false;
            try { CardFactory.CreateForReward(nothing, 1, new CardCreationOptions([pool[0]], CardCreationSource.Other, CardOddsType.Uniform)); }
            catch (RunUnrepresentableException) { stopped = true; }
            c.That(stopped, "a pick that can only land on cards that are not built cannot be drawn again: the run cannot go on");
            c.Equal(place, RewardsCounter(nothing), "and no number is drawn for it");
        });
    }

    /// <summary>A relic with a say in a shop's cards.</summary>
    private sealed class ShopMeddler : Relic, IRewardHooks
    {
        public bool TakesMangleOut;
        public override string Name => "Shop Meddler";
        public override string Describe() => "Shop cards are Rare.";
        public IEnumerable<GameCard> ModifyMerchantCardPool(RunContext c, IEnumerable<GameCard> options) => TakesMangleOut ? options.Where(card => card.ClassName != "Mangle") : options;
        public CardRarity ModifyMerchantCardRarity(RunContext c, CardRarity rarity) => CardRarity.Rare;
    }

    private static void Merchant(Check c)
    {
        c.Group("a shop's card of a type: the rarity and the dead upgrade roll on rewards, the pick on shops", () =>
        {
            // CardFactory.CreateForMerchant(type): RollWithoutChangingFutureOdds on the shop table, NextItem on the shops
            // stream, RollForUpgrade with a chance of -999999999.
            var run = Standing("SHOPCARD");
            run.State.CardRarityOffset = 0.2f;
            run.State.ActIndex = 2;
            var world = World(run);
            var pool = CardPools.Character.Unlocked(RunUnlocks.All);
            var card = CardFactory.CreateForMerchant(run, pool, CardType.Power, Purpose.ShopCard(4)).Card;
            c.Equal(CardType.Power, card.Type, "a card of the type asked for");
            c.That(card.Def.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare, "of a rolled rarity");
            c.That(!card.IsUpgraded, "never upgraded, in whatever act");
            c.Equal(2, world.Streams.Counter(RunStream.Rewards), "two numbers of rewards: the rarity, the upgrade roll");
            c.Equal(1, world.Streams.Counter(RunStream.Shops), "one number of shops: the pick");
            c.Equal(0.2f, run.State.CardRarityOffset, "the pity offset is read and left as it is");

            // At a place where the upgrade roll is at most 0.25, which upgrades a reward card of act 3 (chance 0.5): a shop's card stays plain.
            var lowRoll = Place("SHOPCARD", rng => Next(rng, 2)[1] is > 0f and <= 0.25f);
            var late = At("SHOPCARD", lowRoll);
            late.State.ActIndex = 2;
            c.That(!CardFactory.CreateForMerchant(late, [AngerCard, ColossusCard], CardType.Attack, Purpose.ShopCard(0)).Card.IsUpgraded, "an upgrade roll that would upgrade a reward card does nothing to a shop's card");
            var reward = At("SHOPCARD", lowRoll);
            reward.State.ActIndex = 2;
            c.That(CardFactory.CreateForReward(reward, 1, new CardCreationOptions([AngerCard], CardCreationSource.Other, CardOddsType.Uniform))[0].Card.IsUpgraded, "(the same roll does upgrade a reward card there)");

            // By hand: only one Power in the list, and it is Rare. Whatever rarity is rolled gives way until Rare.
            GameCard[] two = [AngerCard, BarricadeCard];
            c.Equal("Barricade", CardFactory.CreateForMerchant(Standing("SHOPCARD"), two, CardType.Power, Purpose.ShopCard(0)).Card.Def.ClassName, "the one Power of a list, whatever the roll");
            var threw = false;
            try { CardFactory.CreateForMerchant(Standing("SHOPCARD"), two, CardType.Skill, Purpose.ShopCard(0)); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "a type the list does not have at all: the game fails here too");
            GameCard[] withBasic = [new("Bash", CardType.Attack, CardRarity.Basic), new("DefendIronclad", CardType.Skill, CardRarity.Basic), AngerCard];
            c.Equal("Anger", CardFactory.CreateForMerchant(Standing("SHOPCARD"), withBasic, CardType.Attack, Purpose.ShopCard(0)).Card.Def.ClassName, "a Basic card is never sold");
            threw = false;
            try { CardFactory.CreateForMerchant(Standing("SHOPCARD"), withBasic, CardRarity.Basic, Purpose.ShopCard(0)); }
            catch (InvalidOperationException) { threw = true; }
            c.That(threw, "not even when the slot asks for that rarity: the Basic cards are taken out of the list first");
        });

        c.Group("a shop's card of a rarity (the colorless slots): no rarity roll, one number of each stream", () =>
        {
            var run = Standing("SHOPRARITY");
            var card = CardFactory.CreateForMerchant(run, CardPools.Character.Unlocked(RunUnlocks.All), CardRarity.Uncommon, Purpose.ShopCard(5)).Card;
            c.Equal(CardRarity.Uncommon, card.Def.Rarity, "a card of the rarity named");
            c.That(!card.IsUpgraded, "not upgraded");
            c.Equal(1, World(run).Streams.Counter(RunStream.Rewards), "one number of rewards: the upgrade roll");
            c.Equal(1, World(run).Streams.Counter(RunStream.Shops), "one number of shops: the pick");
        });

        c.Group("ModifyMerchantCardRarity and ModifyMerchantCardPool", () =>
        {
            GameCard[] attacks = [AngerCard, new("Mangle", CardType.Attack, CardRarity.Rare)];
            var rare = StandingWith("MEDDLER", () => new ShopMeddler());
            c.Equal("Mangle", CardFactory.CreateForMerchant(rare, attacks, CardType.Attack, Purpose.ShopCard(0)).Card.Def.ClassName, "the relic makes the rolled rarity Rare: the Rare Attack");
            var without = StandingWith("MEDDLER", () => new ShopMeddler { TakesMangleOut = true });
            c.Equal("Anger", CardFactory.CreateForMerchant(without, attacks, CardType.Attack, Purpose.ShopCard(0)).Card.Def.ClassName, "with the Rare Attack taken out of the pool, Rare gives way round to Common");
        });
    }

    // ---------- random potions ----------

    private static void RandomPotions(Check c)
    {
        c.Group("the potions a random potion can be: the Ironclad's three, then the 45 shared ones", () =>
        {
            var run = Standing("POTIONS");
            var options = PotionFactory.GetPotionOptions(run);
            c.Equal(48, options.Count, "48 potions");
            c.Sequence(["BloodPotion", "SoldiersStew", "Ashwater", "AttackPotion"], options.Take(4).Select(p => p.ClassName), "the character's own first (Ironclad4Epoch), then the shared pool from its start");
            c.Equal("WeakPotion", options[^1].ClassName, "to the shared pool's end");
            c.Sequence([16, 16, 16], new[] { PotionRarity.Common, PotionRarity.Uncommon, PotionRarity.Rare }.Select(rarity => options.Count(p => p.Rarity == rarity)), "16 of each rarity");
            c.Equal(46, PotionFactory.GetPotionOptions(run, ["FirePotion", "BloodPotion"]).Count, "a blacklist takes potions out");

            // A profile that has not revealed the potion epochs meets fewer (IroncladPotionPool and SharedPotionPool.GetUnlockedPotions).
            var epochs = GameLists.Epochs.Where(e => e is not ("IRONCLAD4_EPOCH" or "POTION2_EPOCH")).ToList();
            var young = Standing("POTIONS", rooms: new Rooms { Unlocks = new RunUnlocks(epochs, RunUnlocks.All.EncountersSeen, 5, RunUnlocks.All.DiscoveredActs) });
            var fewer = PotionFactory.GetPotionOptions(young).Select(p => p.ClassName).ToList();
            c.Equal(42, fewer.Count, "without the Ironclad's fourth epoch and the second potion epoch: 48 - 3 - 3");
            c.That(!fewer.Contains("BloodPotion") && !fewer.Contains("TouchOfInsanity") && fewer.Contains("BeetleJuice"), "the first potion epoch's three are still there");
        });

        c.Group("a random potion: a rarity at or below 0.1 and 0.35, then one of that rarity", () =>
        {
            c.Equal(PotionRarity.Rare, PotionFactory.RarityOf(0f), "0: Rare");
            c.Equal(PotionRarity.Rare, PotionFactory.RarityOf(0.1f), "0.1 itself is still Rare (at or below)");
            c.Equal(PotionRarity.Uncommon, PotionFactory.RarityOf(0.10000001f), "just above 0.1: Uncommon");
            c.Equal(PotionRarity.Uncommon, PotionFactory.RarityOf(0.35f), "0.35 itself is still Uncommon");
            c.Equal(PotionRarity.Common, PotionFactory.RarityOf(0.35000002f), "just above: Common");

            // 60 potions in a row, each read by hand from a copy of the stream: the rarity, the index into that rarity's 16
            // (the first three of the list are the Ironclad's own), a potion the arena has not built rolled again.
            var run = Standing("POTIONS");
            var stream = World(run).Streams.Long(RunStream.Rewards);
            var registry = Content.Registry;
            var wrong = new List<string>();
            for (var i = 0; i < 60; i++)
            {
                var probe = stream.Clone();
                string expected;
                while (true)
                {
                    var roll = probe.NextFloat();
                    var rarity = roll <= 0.1f ? PotionRarity.Rare : roll <= 0.35f ? PotionRarity.Uncommon : PotionRarity.Common;
                    var ofRarity = Scenarios.PotionPool.Where(p => p.Rarity == rarity).ToList();
                    expected = ofRarity[(int)(probe.NextDouble() * ofRarity.Count)].ClassName;
                    if (registry.Has(ContentKind.Potion, expected)) break;
                }
                var made = PotionFactory.CreateRandomPotionOutOfCombat(run, World(run).Streams.Draw(RunStream.Rewards, Purpose.Potion(i))).ClassName;
                if (made != expected || stream.Counter != probe.Counter) wrong.Add($"potion {i}: expected {expected} after {probe.Counter} numbers, got {made} after {stream.Counter}");
            }
            c.Equal(0, wrong.Count, $"potions that differ: {string.Join("; ", wrong.Take(3))}");

            // By hand: a list of two Common potions (everything else on the blacklist), at a place where both rarity rolls
            // are Common and both picks are below one half. The first pick takes the list's first, Block Potion; the
            // second finds only Fire Potion left.
            var twoCommons = Place("SHELF", rng => { var n = Next(rng, 4); return n[0] > 0.35f && n[1] < 0.5f && n[2] > 0.35f && n[3] < 0.5f; });
            var pair = At("SHELF", twoCommons);
            var others = Scenarios.PotionPool.Select(p => p.ClassName).Where(name => name is not ("BlockPotion" or "FirePotion")).ToList();
            c.Sequence(["BlockPotion", "FirePotion"],
                PotionFactory.CreateRandomPotionsOutOfCombat(pair, 2, World(pair).Streams.Draw(RunStream.Rewards, Purpose.Potion()), others).Select(p => p.ClassName),
                "two potions at once: the second pick runs over what the first left");

            var three = Standing("SHELF");
            var shelf = PotionFactory.CreateRandomPotionsOutOfCombat(three, 3, World(three).Streams.Draw(RunStream.Shops, Purpose.ShopPotion(0)));
            c.Equal(3, shelf.Select(p => p.ClassName).Distinct().Count(), "three at once are three different potions");
            c.That(World(three).Streams.Counter(RunStream.Shops) >= 6 && World(three).Streams.Counter(RunStream.Rewards) == 0, "from the stream the caller named, two numbers each");
        });

        c.Group("a random potion that lands on one the arena has not built is rolled again, rarity and pick", () =>
        {
            var leftOut = Scenarios.PotionPool.Where(p => !Content.Registry.Has(ContentKind.Potion, p.ClassName)).Select(p => p.ClassName).ToList();
            if (leftOut.Count == 0)
            {
                c.That(true, "every potion of the list is built: nothing is rolled again");
                return;
            }
            // A place where the first two numbers land on a potion that is not built.
            const string seed = "POTIONLEFTOUT";
            string Landed(GameRng rng)
            {
                var rarity = PotionFactory.RarityOf(rng.NextFloat());
                var ofRarity = Scenarios.PotionPool.Where(p => p.Rarity == rarity).ToList();
                return ofRarity[(int)(rng.NextDouble() * ofRarity.Count)].ClassName;
            }
            var place = Place(seed, rng => leftOut.Contains(Landed(rng)) && !leftOut.Contains(Landed(rng)));
            var probe = new RunStreams(seed).Long(RunStream.Rewards);
            for (var i = 0; i < place; i++) probe.NextU64();
            var first = Landed(probe);
            var second = Landed(probe);

            var run = At(seed, place);
            var potion = PotionFactory.CreateRandomPotionOutOfCombat(run, World(run).Streams.Draw(RunStream.Rewards, Purpose.Potion()));
            c.Equal(second, potion.ClassName, "the second roll's potion");
            c.Equal(place + 4, RewardsCounter(run), "four numbers");
            var log = run.State.Part<LeftOutLog>().Entries;
            c.That(log.Count == 1 && log[0].Kind == "potion" && log[0].ClassName == first, $"the log names {first}");

            var strict = At(seed, place, strict: true);
            var message = "";
            try { PotionFactory.CreateRandomPotionOutOfCombat(strict, World(strict).Streams.Draw(RunStream.Rewards, Purpose.Potion())); }
            catch (RunUnrepresentableException ex) { message = ex.Message; }
            c.That(message.Contains(first, StringComparison.Ordinal), $"strict mode stops at it by name ({message})");
        });
    }

    // ---------- the gold of a fight monsters fled from ----------

    private static void GoldShare(Check c)
    {
        c.Group("the share of its gold a hallway fight pays: 1 minus the escaped over the kinds of monster", () =>
        {
            // EncounterModel.CalculateGoldProportion; RewardsSet rounds each bound of 10 to 20 to the nearest even.
            c.Equal(0.5f, RewardRoom.GoldShare(1, 2), "one of two kinds fled: half");
            c.Equal(0f, RewardRoom.GoldShare(1, 1), "three monsters of one kind, one fled: nothing, since the kinds are counted");
            c.Equal(-1f, RewardRoom.GoldShare(2, 1), "two of them fled: below nothing");

            (float Share, int Min, int Max)[] cases = [(1f, 10, 20), (0.5f, 5, 10), (1f - 1f / 3f, 7, 13), (0.25f, 2, 5), (0.75f, 8, 15)];
            foreach (var (share, min, max) in cases)
            {
                var run = Standing("SHARE");
                run.State.PotionChance = -1f;
                var probe = World(run).Streams.Long(RunStream.Rewards).Clone();
                probe.NextU64();
                var expected = min + (int)(probe.NextDouble() * (max + 1 - min));
                var set = RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster) with { GoldProportion = share });
                var gold = set.Rewards.OfType<GoldReward>().Single();
                c.Equal((min, max), gold.Range, FormattableString.Invariant($"a share of {share}: gold from {min} to {max} (10 and 20 times the share, each rounded to the nearest, a half to the even one)"));
                c.Equal(expected, gold.Amount, FormattableString.Invariant($"a share of {share}: the amount, by the second number of the stream"));
            }

            var none = Standing("SHARE");
            none.State.PotionChance = -1f;
            var without = RunRewards.GenerateForRoomEnd(none, RewardRoom.Combat(RoomType.Monster) with { GoldProportion = 0f });
            c.Sequence(["card"], without.Rewards.Select(r => r.Kind), "a share of 0: no gold row at all");
            c.Equal(10, RewardsCounter(none), "and no number for it: the potion roll and the nine of the cards");

            var elite = Standing("SHARE", rooms: new Rooms { Strict = true });
            var eliteSet = new RewardsSet().WithRewardsFromRoom(elite, RewardRoom.Combat(RoomType.Elite) with { GoldProportion = 0.5f });
            try { eliteSet.GenerateWithoutOffering(elite); }
            catch (RunUnrepresentableException) { }
            c.That(eliteSet.Rewards.OfType<GoldReward>().Single().Amount >= 35, "an elite pays in full whatever fled");

            var own = Standing("SHARE");
            own.State.PotionChance = -1f;
            var fixedGold = RunRewards.GenerateForRoomEnd(own, RewardRoom.Combat(RoomType.Monster) with { Gold = (300, 300) });
            c.Equal(300, fixedGold.Rewards.OfType<GoldReward>().Single().Amount, "an encounter that names its own gold pays that");
        });

        c.Group("a fight a monster fled from, through the run: the outcome's names give the kinds", () =>
        {
            var fled = new FightOutcome(true, false, "Custom", RoomType.Monster, 3, 2, 80, 80, 80, 0, 3, 1, "Runner + Stayer + Stayer");
            c.Equal(0.5f, RewardRoom.Of(fled).GoldProportion, "three monsters of two kinds, one fled: half");
            c.Equal(1f, RewardRoom.Of(fled with { Escaped = 0 }).GoldProportion, "nobody fled: all of it");
            c.Equal(RoomKind.Elite, RewardRoom.Of(fled with { RoomType = RoomType.Elite }).Kind, "the room's kind is the fight's");

            // The names stand for the classes: no two monster classes of the arena share a title.
            var titles = Content.Registry.MonsterNames.Select(name => Content.Registry.NewMonster(name).Name).ToList();
            c.Equal(titles.Count, titles.Distinct(StringComparer.Ordinal).Count(), "every registered monster class has a title of its own");

            // And a real fight: a dummy that flees in the enemies' first turn, another that is then killed.
            var run = new Arena.Run(new RunSetup { Seed = "FLED" }, new FleeingFight());
            run.State.PotionChance = -1f;
            Do(run, "end_turn");
            var probe = World(run).Streams.Long(RunStream.Rewards).Clone();
            probe.NextU64();
            var expected = 5 + (int)(probe.NextDouble() * 6);
            // The Runner was e0; the Stayer is e1 and keeps that number.
            Do(run, run.LegalActions().First(a => a.Id.EndsWith(">e1", StringComparison.Ordinal)).Id);
            c.Equal(new FightOutcome(true, false, "Custom", RoomType.Monster, 2, 2, 80, 80, 80, 0, 2, 1, "Runner + Stayer"), run.LastFight, "the fight: won in turn 2, one of two monsters fled");
            c.Equal(expected, ((RewardsScreen)run.Top!).Set.Rewards.OfType<GoldReward>().Single().Amount, "its gold is rolled from 5 to 10");
        });
    }

    private sealed class FleeingFight : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State, acts: Overgrowth);
            run.State.TotalFloor = 2;
            run.StartFight(new FightRequest
            {
                Monsters = () =>
                [
                    new PlacedMonster(new CheckDummy { DisplayName = "Runner", Hp = 50, Steps = [new DummyStep("FLEE", Then: (f, me) => f.Escape(me))] }),
                    new PlacedMonster(new CheckDummy { DisplayName = "Stayer", Hp = 5 }),
                ],
                ShuffleDeck = false,
            });
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => RunRewards.OfferForRoomEnd(run, fight);
    }

    // ---------- the game's sort ----------

    private static void Sorting(Check c)
    {
        c.Group("the game's sort of a short list: three items are settled by fixed swaps, which can turn two equal ones round", () =>
        {
            static string Sorted(params (int Key, string Name)[] items)
            {
                var list = items.ToList();
                SmallSort.Sort(list, (x, y) => x.Key.CompareTo(y.Key));
                return string.Join(" ", list.Select(item => item.Name));
            }
            // By hand, the swaps (0,1), (0,2), (1,2), each only when the first is greater:
            // a5 b5 c1: (0,1) equal, stays; (0,2) a5 over c1, swapped: c1 b5 a5; (1,2) equal, stays.
            c.Equal("c b a", Sorted((5, "a"), (5, "b"), (1, "c")), "5 5 1: the two fives change places");
            // a5 b1 c5: (0,1) swapped: b1 a5 c5; (0,2) stays; (1,2) equal, stays.
            c.Equal("b a c", Sorted((5, "a"), (1, "b"), (5, "c")), "5 1 5: they keep their order");
            c.Equal("b c a", Sorted((5, "a"), (1, "b"), (3, "c")), "5 1 3: sorted");
            c.Equal("b a", Sorted((5, "a"), (1, "b")), "two items: swapped when out of order");
            c.Equal("a b", Sorted((5, "a"), (5, "b")), "two equal items stay");
            // Four and more: by insertion, which never moves an item past an equal one.
            c.Equal("d a b c", Sorted((5, "a"), (5, "b"), (5, "c"), (1, "d")), "5 5 5 1: the fives keep their order");
            c.Equal("c e a d b", Sorted((5, "a"), (7, "b"), (1, "c"), (5, "d"), (1, "e")), "five items, equal ones in order");

            // The same as the runtime's own sort, for every arrangement of up to six keys out of three.
            var differs = 0;
            for (var n = 2; n <= 6; n++)
            {
                var total = (int)Math.Pow(3, n);
                for (var code = 0; code < total; code++)
                {
                    var items = new List<(int Key, int Index)>();
                    for (int i = 0, rest = code; i < n; i++, rest /= 3) items.Add((rest % 3, i));
                    var mine = items.ToList();
                    var runtime = items.ToList();
                    SmallSort.Sort(mine, (x, y) => x.Key.CompareTo(y.Key));
                    runtime.Sort((x, y) => x.Key.CompareTo(y.Key));
                    if (!mine.SequenceEqual(runtime)) differs++;
                }
            }
            c.Equal(0, differs, "arrangements that the runtime's List.Sort orders differently");

            var threw = false;
            try { SmallSort.Sort(Enumerable.Range(0, 17).ToList(), (x, y) => x.CompareTo(y)); }
            catch (NotSupportedException) { threw = true; }
            c.That(threw, "more than sixteen items are left out by name");
        });

        c.Group("three rows of a rewards screen: two card rewards made before a gold row change places", () =>
        {
            var run = Standing("THREEROWS");
            var first = new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3);
            var second = new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3);
            var set = RunRewards.GenerateCustom(run, [first, second, new GoldReward(9)]);
            c.That(set.Rewards[0] is GoldReward && ReferenceEquals(set.Rewards[1], second) && ReferenceEquals(set.Rewards[2], first), "the screen shows gold, then the card reward that was made second, then the first");
            // They were still filled in the order made: the first one's cards come from the first six numbers.
            var again = Standing("THREEROWS");
            var alone = RunRewards.GenerateCustom(again, [new CardReward(again, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3)]);
            c.Sequence(ClassNames(((CardReward)alone.Rewards[0]).Cards), ClassNames(first.Cards), "the first-made reward holds the cards of the stream's start");
        });
    }

    // ---------- copies, the save, keyed streams ----------

    private static void CopiesAndReplay(Check c)
    {
        c.Group("a run with rewards is copied whole, also in the middle of a card reward, and replays from its save", () =>
        {
            var setup = new RunSetup { Seed = "COPIES", Potions = ["BloodPotion"], Hp = 40 };
            var rooms = new Rooms { Fights = [RoomType.Monster, RoomType.Monster, RoomType.Boss] };
            var run = new Arena.Run(setup, rooms);
            WinFight(run);
            Do(run, "reward:0");
            Do(run, run.LegalActions().First(a => a.Label.StartsWith("Open card reward", StringComparison.Ordinal)).Id);
            c.Equal("card_reward", run.Snapshot().Screen, "the card reward is open");

            var copy = run.Clone();
            c.Equal(run.Dump(), copy.Dump(), "the copy is in the same state");
            c.That(!ReferenceEquals(((CardRewardScreen)run.Question!).Cards[0], ((CardRewardScreen)copy.Question!).Cards[0]), "with cards of its own on its card reward screen");
            Do(copy, "card:2");
            c.Equal("card_reward", run.Snapshot().Screen, "taking a card in the copy leaves the original's card reward open");
            c.Equal(10, run.State.Deck.Count, "and its deck as it was");
            c.Equal(11, copy.State.Deck.Count, "the copy's deck has the card");

            // Both go on the same way and end in the same place.
            string[] rest = ["proceed"];
            Do(run, "card:2");
            c.Equal(run.Dump(), copy.Dump(), "the original catches up: the same state again");
            foreach (var game in new[] { run, copy })
            {
                Do(game, rest);
                WinFight(game);
                while (game.LegalActions().FirstOrDefault(a => a.Label.StartsWith("Take ", StringComparison.Ordinal)) is { } take) Do(game, take.Id);
                Do(game, game.LegalActions().First(a => a.Label.StartsWith("Open card reward", StringComparison.Ordinal)).Id, "skip", "potion:0", "proceed");
                WinFight(game);
                Do(game, "proceed");
            }
            c.Equal(RunResult.Cleared, run.Result, "three rooms done");
            c.Equal(run.Dump(), copy.Dump(), "the two runs ended in the same state");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, rooms, run.Actions).Dump(), "and so does a replay of the save");
            c.That(run.Actions.Contains("skip") && run.Actions.Contains("card:2") && run.Actions.Contains("potion:0"), "the save holds the card reward's own steps and the potion");
        });

        c.Group("a copy of a rewards screen shares nothing with the original", () =>
        {
            var run = Standing("SHARED");
            RunRewards.OfferCustom(run, [new GoldReward(5), new RelicReward(new Trinket()), new SpecialCardReward(NewCard(run, "Anger")),
                new CardReward(run, CardCreationOptions.ForNonCombatWithDefaultOdds([CardPools.Character]), 3)]);
            var copy = run.Clone();
            var mine = ((RewardsScreen)run.Top!).Set;
            var theirs = ((RewardsScreen)copy.Top!).Set;
            c.That(!ReferenceEquals(mine, theirs) && mine.Rewards.Zip(theirs.Rewards).All(pair => !ReferenceEquals(pair.First, pair.Second)), "the set and every reward are objects of their own");
            c.That(!ReferenceEquals(mine.Rewards.OfType<RelicReward>().Single().Relic, theirs.Rewards.OfType<RelicReward>().Single().Relic), "so is the relic on offer");
            c.That(mine.Rewards.OfType<CardReward>().Single().Cards.Zip(theirs.Rewards.OfType<CardReward>().Single().Cards).All(pair => !ReferenceEquals(pair.First, pair.Second) && pair.First.Id == pair.Second.Id),
                "and the cards on offer, which keep their ids");
            c.That(!ReferenceEquals(mine.Rewards.OfType<CardReward>().Single().Options, theirs.Rewards.OfType<CardReward>().Single().Options), "and the card reward's options");
            Do(copy, "reward:0", "reward:0", "reward:0");
            c.Equal(4, ((RewardsScreen)run.Top!).Rows.Count, "taking three rewards in the copy leaves the original's four rows");
            c.That(run.Gold == 99 && !run.State.HasRelic("Trinket") && run.State.Deck.Count == 10, "and its player");
        });
    }

    private static void Keyed(Check c)
    {
        c.Group("keyed streams: two players on the same node meet the same gold and cards whether or not a potion dropped", () =>
        {
            // Plan 5.4: in game mode a potion that drops takes two numbers before the cards, so the cards behind it shift.
            // In keyed mode every roll has a stream named by what it decides.
            RewardsSet Rewards(StreamMode mode, float potionChance)
            {
                var run = Standing("KEYED", rooms: new Rooms { Mode = mode });
                World(run).Streams.MoveTo(0, 4, 2);
                run.State.PotionChance = potionChance;
                return RunRewards.GenerateForRoomEnd(run, RewardRoom.Combat(RoomType.Monster));
            }
            static List<string> Cards(RewardsSet set) => ClassNames(set.Rewards.OfType<CardReward>().Single().Cards);

            var dry = Rewards(StreamMode.Keyed, -1f);
            var wet = Rewards(StreamMode.Keyed, 2f);
            c.That(!dry.Rewards.Any(r => r is PotionReward) && wet.Rewards.Any(r => r is PotionReward), "one player gets a potion, the other does not");
            c.Equal(dry.Rewards.OfType<GoldReward>().Single().Amount, wet.Rewards.OfType<GoldReward>().Single().Amount, "the same gold");
            c.Sequence(Cards(dry), Cards(wet), "the same three cards");

            var elsewhere = Standing("KEYED", rooms: new Rooms { Mode = StreamMode.Keyed });
            World(elsewhere).Streams.MoveTo(0, 5, 2);
            elsewhere.State.PotionChance = -1f;
            var other = RunRewards.GenerateForRoomEnd(elsewhere, RewardRoom.Combat(RoomType.Monster));
            c.That(!Cards(other).SequenceEqual(Cards(dry)) || other.Rewards.OfType<GoldReward>().Single().Amount != dry.Rewards.OfType<GoldReward>().Single().Amount, "another node has rolls of its own");

            var keyedRun = Standing("KEYED", rooms: new Rooms { Mode = StreamMode.Keyed });
            RunRewards.GenerateForRoomEnd(keyedRun, RewardRoom.Combat(RoomType.Monster));
            c.Equal(0, RewardsCounter(keyedRun), "the game's long rewards stream is not touched in keyed mode");
        });
    }
}
