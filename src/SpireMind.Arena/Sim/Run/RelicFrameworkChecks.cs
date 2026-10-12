using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The relic framework: relics obtained, kept and replaced in a run, who hears a hook outside
/// and inside a fight and in which order, "after room entered" in a fight's set-up, the gold,
/// heal, max HP and damage commands outside a fight with the hooks they send, and pulling
/// relics from the two bags.
/// <para>
/// The relics here are the check's own, never registered. Those named after a relic of the game
/// carry that relic's rule as its class in the game states it (the file is named beside each),
/// and the expected numbers are worked out by hand from that rule and the checked content's
/// numbers (Strike 6, Burning Blood 6, Fairy in a Bottle 30%). What the real game did on seed
/// 1BUSLN0FY5 (the relic of its event, the bags after it, the relics of its first shop) comes
/// from Mark's saves and the game's own record of that run; nothing is read off a run of the
/// code that is checked.
/// </para>
/// </summary>
public sealed class RelicFrameworkChecks : IArenaChecks
{
    public void Run(Check c)
    {
        ThreeRelicsOverARun(c);
        OrderOutsideAFight(c);
        OrderInsideAFight(c);
        RoomEnteredInAFight(c);
        Pickup(c);
        Gold(c);
        HpOutsideAFight(c);
        DeathOutsideAFight(c);
        RealRunPulls(c);
        LeftOutRelics(c);
        BagRules(c);
        HookTable(c);
        HookCallSites(c);
        DeckListeningGuard(c);
    }

    // ---------- helpers ----------

    private const string PlayedSeed = "1BUSLN0FY5";

    private static void Do(Arena.Run run, params string[] ids)
    {
        foreach (var id in ids)
        {
            var result = run.Execute(id);
            if (!result.Ok)
                throw new InvalidOperationException($"'{id}' was refused: {result.Error} Legal now: {string.Join(", ", run.LegalActions().Select(a => a.Id))}");
        }
    }

    /// <summary>Plays the first card of the hand (in these fights always a Strike) at the one enemy.</summary>
    private static void Strike(Arena.Run run) =>
        Do(run, run.LegalActions().First(a => a.Id.StartsWith("play:0", StringComparison.Ordinal)).Id);

    private static FightRequest Dummy(int hp, RoomType room = RoomType.Monster, Action<Fight, Creature>? onAdded = null) => new()
    {
        Monsters = () => [new PlacedMonster(new CheckDummy { Hp = hp, Steps = [new DummyStep("HIT", Damage: 5)], OnAdded = onAdded })],
        RoomType = room,
        ShuffleDeck = false,
    };

    private static bool Throws<T>(Action body) where T : Exception
    {
        try
        {
            body();
        }
        catch (T)
        {
            return true;
        }
        return false;
    }

    /// <summary>A run that is a list of steps: each does something to the run and may name a fight to start;
    /// the next step follows the fight (or follows at once). Cleared after the last.</summary>
    private sealed class Script(params Func<Arena.Run, FightRequest?>[] steps) : RunDirector
    {
        private int _next;

        public override void Begin(Arena.Run run) => Next(run);

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => Next(run);

        private void Next(Arena.Run run)
        {
            while (_next < steps.Length)
            {
                var fight = steps[_next++](run);
                if (run.IsOver) return;
                if (fight is null) continue;
                run.State.ActFloor++;
                run.State.TotalFloor++;
                run.StartFight(fight);
                return;
            }
            run.End(RunResult.Cleared);
        }
    }

    /// <summary>A run that stands in a room and waits, for commands a check gives it directly.</summary>
    private sealed class Standing(bool world = false, RunUnlocks? unlocks = null) : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            if (world) RunWorld.Begin(run.State, unlocks);
            run.Push(new Waiting());
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => run.Push(new Waiting());
    }

    private sealed class Waiting : RunScreen
    {
        public override string Name => "event";

        public override GameSnapshot Snapshot(Arena.Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() },
            Actions = [new LegalAction("option:0", "Wait")],
        };

        public override ActionResult Execute(Arena.Run run, string actionId) =>
            actionId == "option:0" ? ActionResult.Success : ActionResult.Fail($"Unknown action '{actionId}'.");

        public override RunScreen Clone(CloneMap map) => new Waiting();
    }

    private static Arena.Run Stand(RunSetup? setup = null, bool world = false, RunUnlocks? unlocks = null) =>
        new(setup ?? new RunSetup(), new Standing(world, unlocks));

    /// <summary>Notes that listeners of every kind write into, kept in the run so that copies carry them.</summary>
    private sealed class Notes : RunPart
    {
        public string Text = "";

        public override RunPart Clone(CloneMap map) => new Notes { Text = Text };

        public override JsonNode? Dump() => Text;
    }

    // ---------- relics of the game, by their rule ----------

    /// <summary>Strawberry.cs: at pickup, 7 max HP.</summary>
    private sealed class Strawberry : Relic
    {
        public override string Name => "Strawberry";
        public override string Describe() => "At pickup: gain 7 Max HP.";
        public override void AfterObtained(RunContext c) => c.GainMaxHp(7);
    }

    /// <summary>Vajra.cs: after a fight's room is entered, 1 Strength, applied by the player.</summary>
    private sealed class Vajra : Relic
    {
        public override string Name => "Vajra";
        public override string Describe() => "At the start of each fight, gain 1 Strength.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<StrengthPower>(f.Player, 1, f.Player);
        }
    }

    /// <summary>SwordOfStone.cs: counts won elite fights; at the fifth it is replaced by Sword of Jade.</summary>
    private sealed class SwordOfStone : Relic
    {
        public int ElitesDefeated;
        public override string Name => "Sword of Stone";
        public override string Describe() => $"Becomes the Sword of Jade after 5 won elite fights ({ElitesDefeated} so far).";

        public override void AfterCombatVictory(Fight f)
        {
            if (f.RoomType != RoomType.Elite) return;
            ElitesDefeated++;
            if (ElitesDefeated >= 5) f.ReplaceRelic(this, new SwordOfJade());
        }
    }

    /// <summary>SwordOfJade.cs: after a fight's room is entered, 3 Strength, with no applier.</summary>
    private sealed class SwordOfJade : Relic
    {
        public override string Name => "Sword of Jade";
        public override string Describe() => "At the start of each fight, gain 3 Strength.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (c.Fight is { } f && room.IsCombat()) f.ApplyPower<StrengthPower>(f.Player, 3, null);
        }
    }

    /// <summary>MealTicket.cs: after a shop is entered, heal 15 (when alive).</summary>
    private sealed class MealTicket : Relic
    {
        public override string Name => "Meal Ticket";
        public override string Describe() => "Whenever you enter a shop, heal 15 HP.";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            if (!c.IsDead && room == RoomKind.Shop) c.Heal(15);
        }
    }

    /// <summary>BowlerHat.cs: gold gained times 1.25.</summary>
    private sealed class BowlerHat : Relic
    {
        public decimal Told = -1m;
        public int TimesTold;
        public override string Name => "Bowler Hat";
        public override string Describe() => "Gain 25% more gold.";
        public override decimal ModifyGoldGained(RunContext c, decimal amount) => amount * 1.25m;

        public override void AfterModifyingGoldGained(RunContext c, decimal amount)
        {
            Told = amount;
            TimesTold++;
        }
    }

    /// <summary>DragonFruit.cs: whenever gold is gained, 1 max HP.</summary>
    private sealed class DragonFruit : Relic
    {
        public override string Name => "Dragon Fruit";
        public override string Describe() => "Whenever you gain gold, gain 1 Max HP.";
        public override void AfterGoldGained(RunContext c) => c.GainMaxHp(1);
    }

    /// <summary>TungstenRod.cs: HP the player is about to lose, less 1 (never below 0); it flashes when that changed the number.</summary>
    private sealed class TungstenRod : Relic
    {
        public int Flashes;
        public override string Name => "Tungsten Rod";
        public override string Describe() => "Whenever you would lose HP, lose 1 less.";

        public override decimal ModifyHpLostAfterOsty(Fight f, Creature target, decimal amount, ValueProp props, Creature? dealer, CardInstance? cardSource) =>
            target.IsPlayer ? Math.Max(0m, amount - 1m) : amount;

        public override void AfterModifyingHpLostAfterOsty(Fight f) => Flashes++;
    }

    /// <summary>LizardTail.cs: once, the player does not die and is healed 50% of max HP (at least 1) instead.</summary>
    private sealed class LizardTail : Relic
    {
        public bool WasUsed;
        public override string Name => "Lizard Tail";
        public override string Describe() => WasUsed ? "Used up." : "When you would die, heal to 50% of your Max HP instead. Once.";
        public override bool ShouldDieLate(Fight f, Creature creature) => !creature.IsPlayer || WasUsed;

        public override void AfterPreventingDeath(Fight f, Creature creature)
        {
            WasUsed = true;
            f.Heal(creature, Math.Max(1m, creature.MaxHp * 0.5m));
        }
    }

    /// <summary>JuzuBracelet.cs: an Unknown map point never becomes a hallway fight.</summary>
    private sealed class JuzuBracelet : Relic
    {
        public override string Name => "Juzu Bracelet";
        public override string Describe() => "Unknown rooms never hold a regular fight.";

        public override IReadOnlySet<RolledRoom> ModifyUnknownMapPointRoomTypes(RunContext c, IReadOnlySet<RolledRoom> roomTypes) =>
            roomTypes.Where(room => room != RolledRoom.Monster).ToHashSet();
    }

    /// <summary>Stands for a relic that does nothing the checks look at; its class name is a bag relic's.</summary>
    private sealed class StoneCracker : Relic
    {
        public override string Name => "Stone Cracker";
        public override string Describe() => "(a check's stand-in)";
    }

    /// <summary>
    /// ArcaneScroll.cs with CardFactory.CreateForReward: at pickup one Rare card of the character's
    /// pool joins the deck, picked with ONE number of the player's rewards stream (uniform odds
    /// make no rarity roll, and the relic asks for no upgrade roll).
    /// </summary>
    private sealed class ArcaneScroll : Relic
    {
        public override string Name => "Arcane Scroll";
        public override string Describe() => "At pickup: add a random Rare card to your deck.";

        public override void AfterObtained(RunContext c)
        {
            var run = c.NeedRun("Arcane Scroll's card");
            var rares = Content.Registry.IroncladPool.Where(card => card.Rarity == CardRarity.Rare).ToList();
            var rng = run.State.Part<RunWorld>().Streams.Draw(RunStream.Rewards, Purpose.Relic(ClassName));
            run.State.AddCard(rng.NextItem(rares)!);
        }
    }

    // ---------- 1. the three sample relics over a scripted run ----------

    private static void ThreeRelicsOverARun(Check c)
    {
        c.Group("a pickup relic, a room-entered relic and a counting relic over a run of eight fights", () =>
        {
            // The run: 50 of 80 HP, the starter deck in deck order (so every first hand is five
            // Strikes), Burning Blood. Strawberry and Vajra are picked up before fight 1, the Sword
            // of Stone and then a Meal Ticket (which does nothing in a fight) after it. Fights 2, 3, 5, 6, 7 are elites, 1, 4 and 8 hallway fights; every
            // enemy dies on turn 1, so no HP is lost and Burning Blood heals 6 after each.
            var setup = new RunSetup { Hp = 50 };
            var director = new Script(
                run =>
                {
                    run.ObtainRelic(new Strawberry());
                    run.ObtainRelic(new Vajra());
                    return Dummy(14);
                },
                run =>
                {
                    run.ObtainRelic(new SwordOfStone());
                    run.ObtainRelic(new MealTicket());
                    return Dummy(7, RoomType.Elite);
                },
                _ => Dummy(7, RoomType.Elite),
                _ => Dummy(7),
                _ => Dummy(7, RoomType.Elite),
                _ => Dummy(7, RoomType.Elite),
                _ => Dummy(7, RoomType.Elite),
                _ => Dummy(30));
            var run = new Arena.Run(setup, director);

            // The pickups, before any fight: Strawberry gives 7 max HP and heals by what the maximum rose.
            c.Equal(87, run.State.MaxHp, "max HP after Strawberry: 80 + 7");
            c.Equal(57, run.State.Hp, "HP after Strawberry: 50 + 7");
            c.Sequence(["BurningBlood", "Strawberry", "Vajra"], run.State.Relics.Select(r => r.ClassName), "relics in pickup order");

            // Fight 1: Vajra's Strength is there on turn 1, so a Strike deals 7 and two of them kill 14.
            c.Equal(1, run.Fight!.Player.PowerAmount<StrengthPower>(), "fight 1: Strength on turn 1");
            c.Equal(1, run.Fight.Turn, "fight 1: it is turn 1");
            Strike(run);
            c.Equal(7, run.Fight!.Enemies[0].Hp, "fight 1: a Strike with 1 Strength deals 7 of 14");
            Strike(run);

            // Fight 2 has begun: the run's state is the player as fight 1 left it.
            c.Equal(63, run.State.Hp, "after fight 1: 57 + 6 from Burning Blood");
            c.Sequence(["BurningBlood", "Strawberry", "Vajra", "SwordOfStone", "MealTicket"], run.State.Relics.Select(r => r.ClassName), "the Sword of Stone and the Meal Ticket were picked up after fight 1");
            c.Equal(RoomType.Elite, run.Fight!.RoomType, "fight 2 is an elite");
            c.Equal(1, run.Fight.Player.PowerAmount<StrengthPower>(), "fight 2: Strength starts again at Vajra's 1");

            int[] hpAfter = [69, 75, 81, 87, 87];
            int[] elites = [1, 2, 2, 3, 4];
            for (var fight = 2; fight <= 6; fight++)
            {
                Strike(run);
                c.Equal(hpAfter[fight - 2], run.State.Hp, $"after fight {fight}: HP (6 from Burning Blood, never above 87)");
                c.Equal(elites[fight - 2], run.State.Relics.OfType<SwordOfStone>().Single().ElitesDefeated, $"after fight {fight}: elites the Sword of Stone has counted (fight 4 is no elite)");
            }
            c.That(run.DescribeDeck()["relics"]!.AsArray().Any(r => (string?)r!["text"] == "Becomes the Sword of Jade after 5 won elite fights (4 so far)."),
                "get_deck shows the count, read off the fight's own copy of the relic");

            // Fight 7 is the fifth elite: in its victory hook the Sword of Stone is replaced, in place.
            var before = run.Clone();
            Strike(run);
            c.Sequence(["BurningBlood", "Strawberry", "Vajra", "SwordOfJade", "MealTicket"], run.State.Relics.Select(r => r.ClassName), "after the fifth elite the Sword of Jade stands where the Sword of Stone stood, not at the end");
            c.Equal(87, run.State.Hp, "HP after fight 7");
            c.Equal(87, run.State.MaxHp, "max HP is still 87");

            // Fight 8: Vajra's 1 and the Sword of Jade's 3, in pickup order: a Strike deals 10.
            c.Equal(4, run.Fight!.Player.PowerAmount<StrengthPower>(), "fight 8: Strength 1 + 3 on turn 1");
            Strike(run);
            c.Equal(20, run.Fight!.Enemies[0].Hp, "fight 8: a Strike with 4 Strength deals 10 of 30");
            Strike(run);
            Strike(run);
            c.Equal(RunResult.Cleared, run.Result, "the run is cleared after the eighth fight");
            c.Equal(8, run.State.TotalFloor, "eight floors");
            c.Sequence(["Burning Blood", "Strawberry", "Vajra", "Sword of Jade", "Meal Ticket"], run.PlayerJson()["relics"]!.AsArray().Select(name => (string?)name ?? ""), "the player block lists the relics by title");

            // The same run again from its save, and a copy taken before the fifth elite's last blow.
            var again = Arena.Run.Replay(setup, director, run.Actions);
            c.Equal(run.Dump(), again.Dump(), "a replay of the save ends in the same state, relic fields included");
            foreach (var id in run.Actions.Skip(before.Actions.Count)) Do(before, id);
            c.Equal(run.Dump(), before.Dump(), "a copy taken before the replacement goes on to the same state");
            c.That(before.State.Relics.All(relic => !run.State.Relics.Contains(relic)), "and shares no relic object with the run it was copied from");
        });

        c.Group("after room entered outside a fight: Meal Ticket heals 15 in a shop and nowhere else", () =>
        {
            var run = Stand(new RunSetup { Hp = 40, ExtraRelics = [() => new MealTicket(), () => new Vajra()] });
            run.AfterRoomEntered(RoomKind.RestSite);
            c.Equal(40, run.State.Hp, "a rest site: nothing");
            c.Equal(RoomKind.RestSite, run.Room, "the run knows the room it is in");
            run.AfterRoomEntered(RoomKind.Shop);
            c.Equal(55, run.State.Hp, "a shop: 40 + 15");
            run.AfterRoomEntered(RoomKind.Shop);
            run.AfterRoomEntered(RoomKind.Shop);
            c.Equal(80, run.State.Hp, "never above max HP: 55, 70, 80");
            c.That(Throws<ArgumentException>(() => run.AfterRoomEntered(RoomKind.Elite)), "a fight's room is not announced by the run: the fight does it in its set-up");
        });
    }

    // ---------- 2 and 3. who hears a hook, and in which order ----------

    /// <summary>The first relic of the order checks: holds what the listeners noted, one line per hook.</summary>
    private sealed class Clock : Relic
    {
        public string Entered = "", Ended = "";
        public override string Name => "Clock";
        public override string Describe() => "(a check's log)";
        public static Clock Of(IEnumerable<Relic> relics) => relics.OfType<Clock>().First();
    }

    private sealed class Stamp : Relic
    {
        public string Tag { get; init; } = "?";

        /// <summary>The tag of a relic this one takes away when a room is entered, once.</summary>
        public string? Removes;

        public override string Name => "Stamp " + Tag;
        public override string Describe() => "(a check's listener)";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            Clock.Of(c.Relics).Entered += Tag + ",";
            if (Removes is null) return;
            var target = c.Relics.OfType<Stamp>().First(s => s.Tag == Removes);
            Removes = null;
            c.RemoveRelic(target);
        }

        public override void AfterRemoved(RunContext c) => Clock.Of(c.Relics).Entered += Tag + "-removed,";
        public override void AfterCombatEnd(Fight f) => Clock.Of(f.Relics).Ended += Tag + ",";
    }

    /// <summary>The hook class of a card that listens as a deck card. It says where its card is when it hears a hook.</summary>
    private sealed class DeckVoice : CardBehaviour, IListensInDeck
    {
        private string Where() => Card.Pile == PileType.Deck ? $"deck#{Card.Id}," : $"pile<{Card.DeckCardId},";
        public override void AfterRoomEntered(RunContext c, RoomKind room) => Clock.Of(c.Relics).Entered += Where();
        public override void AfterCombatEnd(Fight f) => Clock.Of(f.Relics).Ended += Where();
    }

    private static readonly CardDef ListeningCard = new("CheckListeningCard", "Listening Card", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
    {
        Text = _ => "Does nothing.",
        Behaviour = () => new DeckVoice(),
    };

    private sealed class PotionVoice : Listener
    {
        public override void AfterRoomEntered(RunContext c, RoomKind room) => Clock.Of(c.Relics).Entered += "potion,";
        public override void AfterCombatEnd(Fight f) => Clock.Of(f.Relics).Ended += "potion,";
    }

    private static readonly PotionDef ListeningPotion = new("CheckListeningPotion", "Listening Potion", PotionRarity.Common, TargetType.Self)
    {
        Text = _ => "Does nothing.",
        Use = _ => { },
        Hooks = new PotionVoice(),
    };

    private static RunSetup OrderSetup() => new()
    {
        // The starter's ten cards have ids 1 to 10; the two listening cards get 11 and 12.
        ExtraCards = [(ListeningCard, false), (ListeningCard, false)],
        ExtraRelics = [() => new Clock(), () => new Stamp { Tag = "A" }, () => new Stamp { Tag = "B" }],
    };

    private static void OrderOutsideAFight(Check c)
    {
        c.Group("outside a fight: listening deck cards in deck order, relics in pickup order, potions in slot order", () =>
        {
            var run = Stand(OrderSetup());
            run.State.Belt.Add(ListeningPotion, 0);
            run.State.Belt.Add(ListeningPotion, 2);
            run.AfterRoomEntered(RoomKind.Treasure);
            c.Equal("deck#11,deck#12,A,B,potion,potion,", Clock.Of(run.State.Relics).Entered, "the order (a potion's hook object is heard once per slot that holds it)");
        });

        c.Group("outside a fight: a relic that another one's hook took away is not called, and hears that it was removed", () =>
        {
            var run = Stand(OrderSetup());
            run.State.Relics.OfType<Stamp>().First(s => s.Tag == "A").Removes = "B";
            run.AfterRoomEntered(RoomKind.Event);
            c.Equal("deck#11,deck#12,A,B-removed,", Clock.Of(run.State.Relics).Entered, "B was in the list when the dispatch began and is gone when its turn comes");
            c.Sequence(["BurningBlood", "Clock", "Stamp"], run.State.Relics.Select(r => r.ClassName), "the relics that are left");

            var deckCard = run.State.Deck.First(card => card.Id == 11);
            run.State.RemoveCard(deckCard);
            Clock.Of(run.State.Relics).Entered = "";
            run.AfterRoomEntered(RoomKind.Event);
            c.Equal("deck#12,A,", Clock.Of(run.State.Relics).Entered, "a card that left the deck is no listener");
        });

        c.Group("a card whose hook class does not listen in the deck hears nothing as a deck card", () =>
        {
            var plain = new CardDef("CheckPlainCard", "Plain Card", CardType.Skill, CardRarity.Common, 1, TargetType.Self) { Text = _ => "Does nothing.", Behaviour = () => new PileVoice() };
            var run = Stand(new RunSetup { ExtraCards = [(plain, false)], ExtraRelics = [() => new Clock()] });
            run.AfterRoomEntered(RoomKind.Event);
            c.Equal("", Clock.Of(run.State.Relics).Entered, "outside a fight");
            run.StartFight(Dummy(6));
            c.Equal("", Clock.Of(run.Fight!.Relics).Entered, "in a fight's room entry: neither the deck card nor its copy in the draw pile hears the run's hook");
            Strike(run);
            c.Equal("pile<11,", Clock.Of(run.State.Relics).Ended, "its copy in a pile hears the fight's hooks, once");
        });
    }

    private sealed class PileVoice : CardBehaviour
    {
        public override void AfterRoomEntered(RunContext c, RoomKind room) => Clock.Of(c.Relics).Entered += "plain,";
        public override void AfterCombatEnd(Fight f) => Clock.Of(f.Relics).Ended += Card.Pile == PileType.Deck ? "deck," : $"pile<{Card.DeckCardId},";
    }

    private static void OrderInsideAFight(Check c)
    {
        c.Group("inside a fight: the run's hook reaches deck cards, relics, potions; the fight's hook the deck cards first and the pile cards last", () =>
        {
            var run = new Arena.Run(OrderSetup(), new Script(
                r =>
                {
                    r.State.Belt.Add(ListeningPotion, 0);
                    r.State.Belt.Add(ListeningPotion, 2);
                    return Dummy(6);
                }));
            // "After room entered" was sent during the set-up: the run's listeners alone. The combat
            // copies of cards 11 and 12 sit in the draw pile and are not asked.
            c.Equal("deck#11,deck#12,A,B,potion,potion,", Clock.Of(run.Fight!.Relics).Entered, "after room entered, in a fight");

            Strike(run);
            // "After combat end" belongs to run and fight: deck cards, then the fight's own order
            // (relics, potions, the cards in the piles: the two copies are in the draw pile).
            c.Equal("deck#11,deck#12,A,B,potion,potion,pile<11,pile<12,", Clock.Of(run.State.Relics).Ended, "after combat end");
            c.Equal("deck#11,deck#12,A,B,potion,potion,", Clock.Of(run.State.Relics).Entered, "nothing more was noted for the room");
        });

        c.Group("inside a fight: a relic replaced in a hook is not called again in that dispatch, its successor from the next one", () =>
        {
            // A takes B away while the room is entered: B is in the snapshot of that dispatch.
            var run = new Arena.Run(OrderSetup(), new Script(
                r =>
                {
                    r.State.Relics.OfType<Stamp>().First(s => s.Tag == "A").Removes = "B";
                    return Dummy(6);
                }));
            c.Equal("deck#11,deck#12,A,B-removed,", Clock.Of(run.Fight!.Relics).Entered, "B is gone when its turn comes");
            Strike(run);
            c.Equal("deck#11,deck#12,A,pile<11,pile<12,", Clock.Of(run.State.Relics).Ended, "and hears nothing for the rest of the fight");
            c.Sequence(["BurningBlood", "Clock", "Stamp"], run.State.Relics.Select(r => r.ClassName), "the run takes the relics back as the fight left them");
        });
    }

    // ---------- 4. "after room entered" in a fight's set-up ----------

    private sealed class Probe : Relic
    {
        public string AtEntry = "", AtStart = "";
        public override string Name => "Probe";
        public override string Describe() => "(a check's probe)";

        public override void AfterRoomEntered(RunContext c, RoomKind room)
        {
            var f = c.Fight;
            AtEntry += f is null
                ? $"{room}|no fight;"
                : $"{room}|starting={f.IsStarting}|inProgress={f.InProgress}|combat={f.IsCombat}|enemies={f.Enemies.Count}|enemyPowers={f.Enemies.Sum(e => e.Powers.Count)}|draw={f.DrawPile.Count}|hand={f.Hand.Count}|run={(c.Run is null ? "none" : "there")};";
        }

        public override void BeforeCombatStart(Fight f) =>
            AtStart += $"starting={f.IsStarting}|inProgress={f.InProgress}|enemyPowers={f.Enemies.Sum(e => e.Powers.Count)}|hand={f.Hand.Count}|strength={f.Player.PowerAmount<StrengthPower>()};";
    }

    private static void RoomEnteredInAFight(Check c)
    {
        c.Group("after room entered comes between the shuffle and the monsters' starting powers", () =>
        {
            // CombatRoom.StartCombat: the monsters are created, CombatManager.SetUpCombat fills and
            // shuffles the draw pile, then "after room entered", then every creature gets its
            // starting powers and first move, then the fight is in progress and "before combat start".
            var run = new Arena.Run(new RunSetup { ExtraRelics = [() => new Probe(), () => new Vajra()] }, new FixedFights(
                Dummy(40, RoomType.Elite, (f, me) => f.ApplyPower<StrengthPower>(me, 2, me))));
            var probe = run.Fight!.Relics.OfType<Probe>().Single();
            c.Equal("Elite|starting=True|inProgress=False|combat=True|enemies=1|enemyPowers=0|draw=10|hand=0|run=none;", probe.AtEntry, "what a relic sees when the room is entered");
            c.Equal("starting=False|inProgress=True|enemyPowers=1|hand=0|strength=1;", probe.AtStart, "what it sees at 'before combat start': the monster has its power, Vajra's Strength is on");
            c.Equal(5, run.Fight.Hand.Count, "then the first hand is drawn");
        });

        c.Group("a single fight enters its room too", () =>
        {
            var probe = new Probe();
            var f = new FightBuilder().DrawOrder("StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad")
                .Enemy(new CheckDummy { Hp = 20 }).Relic(probe).Relic(new Vajra()).Start();
            c.Equal("Monster|starting=True|inProgress=False|combat=True|enemies=1|enemyPowers=0|draw=6|hand=0|run=none;", probe.AtEntry, "the hook, once, as a hallway fight");
            c.Equal(1, f.Player.PowerAmount<StrengthPower>(), "Vajra works in a single fight");
            f.Execute(f.LegalActions().First(a => a.Id.StartsWith("play:0", StringComparison.Ordinal)).Id);
            c.Equal(13, f.Enemies[0].Hp, "a Strike deals 7 of 20");
        });
    }

    // ---------- 5. picking a relic up ----------

    /// <summary>Named after a Common relic of the shared pool so that the bags hold its name; it notes what it finds at pickup.</summary>
    private sealed class Anchor : Relic
    {
        public override string Name => "Anchor";
        public override string Describe() => "(a check's probe)";

        public override void AfterObtained(RunContext c)
        {
            var run = c.NeedRun("the probe's pickup");
            var world = run.State.Part<RunWorld>();
            run.State.Part<Notes>().Text += $"pickup:at={run.State.Relics.ToList().IndexOf(this)}:inPlayerBag={world.PlayerBag.Contains(ClassName)}:inSharedBag={world.SharedBag.Contains(ClassName)};";
        }

        public override void AfterRemoved(RunContext c) => c.NeedRun("the probe's removal").State.Part<Notes>().Text += "removed;";
    }

    private sealed class Hearing(string tag) : RunScreen, IHearsRelicObtained
    {
        public override string Name => "rewards";

        public override GameSnapshot Snapshot(Arena.Run run) => new() { Screen = Name, AwaitingDecision = true, Actions = [new LegalAction("proceed", "Proceed")] };

        public override ActionResult Execute(Arena.Run run, string actionId) => ActionResult.Success;

        public override RunScreen Clone(CloneMap map) => new Hearing(tag);

        public void OnRelicObtained(Arena.Run run, Relic relic) =>
            run.State.Part<Notes>().Text += $"{tag}:{relic.ClassName}:inList={run.State.Relics.Contains(relic)}:inPlayerBag={run.State.Part<RunWorld>().PlayerBag.Contains(relic.ClassName)};";
    }

    private static void Pickup(Check c)
    {
        c.Group("obtaining a relic: into the list, then the open reward screens hear of it, then out of both bags, then its pickup effect", () =>
        {
            // RelicCmd.Obtain: Player.AddRelicInternal (the list, and the RelicObtained event a pending card
            // reward listens to), then the removal from the player's bag and the shared bag, then AfterObtained.
            var run = Stand(world: true);
            var world = run.State.Part<RunWorld>();
            c.That(world.PlayerBag.Contains("Anchor") && world.SharedBag.Contains("Anchor"), "before: both bags hold Anchor");
            run.Push(new Hearing("first"));
            run.Push(new Hearing("second"));
            var relic = run.ObtainRelic(new Anchor());
            c.Equal("first:Anchor:inList=True:inPlayerBag=True;second:Anchor:inList=True:inPlayerBag=True;pickup:at=1:inPlayerBag=False:inSharedBag=False;",
                run.State.Part<Notes>().Text, "the order, and what each step finds");
            c.That(Throws<InvalidOperationException>(() => run.ObtainRelic(relic)), "the same relic object cannot be obtained twice");
            c.That(Throws<KeyNotFoundException>(() => run.ObtainRelic("NoSuchRelic")), "a relic the arena does not hold cannot be obtained by name");
        });

        c.Group("obtaining at a place, removing, replacing", () =>
        {
            var run = Stand(new RunSetup { Hp = 50 }, world: true);
            run.ObtainRelic(new Vajra());
            run.ObtainRelic(new Strawberry(), 0);
            c.Sequence(["Strawberry", "BurningBlood", "Vajra"], run.State.Relics.Select(r => r.ClassName), "a relic obtained at index 0 stands first");
            c.Equal(87, run.State.MaxHp, "and its pickup effect ran");

            // RelicCmd.Replace: the index of the original, Remove (AfterRemoved), then Obtain at that index.
            var anchor = new Anchor();
            run.ReplaceRelic(run.State.Relics[1], anchor);
            c.Sequence(["Strawberry", "Anchor", "Vajra"], run.State.Relics.Select(r => r.ClassName), "the replacement stands where the original stood");
            c.Equal("pickup:at=1:inPlayerBag=False:inSharedBag=False;", run.State.Part<Notes>().Text, "it was obtained like any relic");
            run.RemoveRelic(anchor);
            c.Sequence(["Strawberry", "Vajra"], run.State.Relics.Select(r => r.ClassName), "removed");
            c.Equal("pickup:at=1:inPlayerBag=False:inSharedBag=False;removed;", run.State.Part<Notes>().Text, "and told so");
            c.That(Throws<InvalidOperationException>(() => run.RemoveRelic(anchor)), "a relic the player does not hold cannot be removed");
            c.Equal(87, run.State.MaxHp, "losing Burning Blood changed no number");
        });

        c.Group("a relic obtained inside a fight leaves the bags when the run next looks at one", () =>
        {
            // The fight cannot reach the bags. Strawberry is given in a victory hook here; its pickup
            // effect runs in the fight (7 max HP on the fight's player), and the run takes it back.
            var run = new Arena.Run(new RunSetup { Hp = 50, ExtraRelics = [() => new Giver()] }, new WorldThenFight(Dummy(6)));
            Strike(run);
            c.Sequence(["BurningBlood", "Giver", "Strawberry"], run.State.Relics.Select(r => r.ClassName), "the relic came back with the fight");
            c.Equal(87, run.State.MaxHp, "max HP 80 + 7");
            c.Equal(63, run.State.Hp, "HP 50 + 6 (Burning Blood is asked before the Giver) + 7");
            var world = run.State.Part<RunWorld>();
            c.That(world.PlayerBag.Contains("Strawberry"), "until a bag is looked at, the name is still in it");
            var pulled = new List<string>();
            for (var i = 0; i < 26; i++) pulled.Add(RunRelics.PullFromFront(run.State, RelicRarity.Common, isBuilt: _ => true).Relic!);
            c.That(!pulled.Contains("Strawberry"), "no pull gives a relic the player holds");
            c.Equal(25, pulled.Count(r => GameLists.BagRelic(r).Rarity == RelicRarity.Common), "the 26 Commons of the bag, less Strawberry, come first");
            c.That(!world.PlayerBag.Contains("Strawberry") && !world.SharedBag.Contains("Strawberry"), "and it has left both bags");
        });
    }

    private sealed class Giver : Relic
    {
        public override string Name => "Giver";
        public override string Describe() => "(a check's relic)";
        public override void AfterCombatVictory(Fight f) => f.ObtainRelic(new Strawberry());
    }

    private sealed class WorldThenFight(FightRequest fight) : RunDirector
    {
        public override void Begin(Arena.Run run)
        {
            RunWorld.Begin(run.State);
            run.State.TotalFloor++;
            run.StartFight(fight with { Seed = 1 });
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome won) => run.Push(new Waiting());
    }

    // ---------- 6. gold ----------

    private sealed class Payday : Relic
    {
        public int Paid = -1;
        public override string Name => "Payday";
        public override string Describe() => "(a check's relic)";
        public override void BeforeCombatStart(Fight f) => Paid = f.GainGold(10);
    }

    private sealed class Tithe : CardBehaviour
    {
        public override decimal ModifyGoldGained(RunContext c, decimal amount) => amount + 1m;
    }

    private static void Gold(Check c)
    {
        c.Group("gaining gold: modified, cut to a whole number when paid, then 'gold gained'", () =>
        {
            // PlayerCmd.GainGold: ModifyGoldGained through every listener; the ones whose step changed the
            // whole number hear AfterModifyingGoldGained with the final amount; nothing more at 0 or
            // less; else Gold += (int)amount and AfterGoldGained.
            var run = Stand(new RunSetup { Hp = 50, ExtraRelics = [() => new BowlerHat(), () => new DragonFruit()] });
            var hat = run.State.Relics.OfType<BowlerHat>().Single();
            c.Equal(12, run.GainGold(10), "10 gold with Bowler Hat: 12.5, paid 12");
            c.Equal(111, run.State.Gold, "99 + 12");
            c.Equal(12.5m, hat.Told, "the hat is told the final amount, not yet cut");
            c.Equal(81, run.State.MaxHp, "Dragon Fruit: 1 max HP");
            c.Equal(51, run.State.Hp, "and 1 HP with it");

            c.Equal(1, run.GainGold(1), "1 gold: 1.25, paid 1");
            c.Equal(1, hat.TimesTold, "the hat changed no whole number this time and is not told");
            c.Equal(82, run.State.MaxHp, "Dragon Fruit again");

            c.Equal(0, run.GainGold(0), "0 gold: nothing is paid");
            c.Equal(82, run.State.MaxHp, "and nobody hears 'gold gained'");
            c.Equal(0, run.GainGold(-5), "a negative amount: nothing");
            c.Equal(112, run.State.Gold, "gold is still 99 + 12 + 1");

            // 0.5 becomes 0.625: more than 0, so the game goes on, pays the whole-number part (0) and sends the hook.
            c.Equal(0, run.GainGold(0.5m), "half a gold: 0 is paid");
            c.Equal(83, run.State.MaxHp, "but 'gold gained' is heard, as in the game");

            run.LoseGold(5.9m);
            c.Equal(107, run.State.Gold, "losing 5.9 gold takes 5");
            run.LoseGold(1000);
            c.Equal(0, run.State.Gold, "gold never goes below 0");
            c.Equal(83, run.State.MaxHp, "losing gold is heard by nobody");
        });

        c.Group("gold inside a fight: the fight's listeners may change the amount, the run's hear 'gold gained'", () =>
        {
            // The Tithe card's hook class does not listen in the deck: outside a fight it is not asked,
            // inside one its copy in the draw pile is, after the relics (10 x 1.25 = 12.5, + 1 = 13.5).
            var tithe = new CardDef("CheckTithe", "Tithe", CardType.Skill, CardRarity.Common, 1, TargetType.Self) { Text = _ => "Does nothing.", Behaviour = () => new Tithe() };
            var setup = new RunSetup { Hp = 50, ExtraCards = [(tithe, false)], ExtraRelics = [() => new BowlerHat(), () => new DragonFruit(), () => new Payday()] };

            var outside = Stand(setup);
            c.Equal(12, outside.GainGold(10), "outside a fight the deck card is not asked: 12");

            var run = new Arena.Run(setup, new FixedFights(Dummy(6)));
            c.Equal(13, run.Fight!.Relics.OfType<Payday>().Single().Paid, "inside: 13");
            c.Equal(112, run.Gold, "the live gold: 99 + 13");
            c.Equal(99, run.State.Gold, "the run's own state is the player as the fight began");
            c.Equal(13.5m, run.Fight.Relics.OfType<BowlerHat>().Single().Told, "the hat is told 13.5");
            c.Equal(81, run.MaxHp, "Dragon Fruit raised the fight's player");
            c.Equal(51, run.Hp, "by 1 HP too");
            c.That(Throws<InvalidOperationException>(() => run.GainGold(1)), "the run's own command is refused while a fight is on");
            c.That(Throws<InvalidOperationException>(() => run.Heal(1)), "and so is a heal");
            c.That(Throws<InvalidOperationException>(() => run.ObtainRelic(new Vajra())), "and a pickup");
            Strike(run);
            c.Equal(112, run.State.Gold, "the gold comes back with the fight");
            c.Equal(81, run.State.MaxHp, "and the max HP");
            c.Equal(57, run.State.Hp, "HP 51 + 6 from Burning Blood");
        });
    }

    // ---------- 7. HP outside a fight ----------

    private sealed class HpWatch : Relic
    {
        public int Calls;
        public decimal LastDelta;
        public string Seen = "";
        public override string Name => "Watch";
        public override string Describe() => "(a check's probe)";

        public override void AfterCurrentHpChanged(Fight f, Creature creature, decimal delta)
        {
            Calls++;
            LastDelta = delta;
            Seen = $"combat={f.IsCombat}|inProgress={f.InProgress}|player={creature.IsPlayer}|enemies={f.Enemies.Count}";
        }
    }

    private static void HpOutsideAFight(Check c)
    {
        c.Group("heal and max HP outside a fight", () =>
        {
            // CreatureCmd.Heal: up to max HP; "HP changed" is sent only for a creature in a combat.
            var run = Stand(new RunSetup { Hp = 50, ExtraRelics = [() => new HpWatch()] });
            var watch = run.State.Relics.OfType<HpWatch>().Single();
            run.Heal(12);
            c.Equal(62, run.State.Hp, "50 + 12");
            run.Heal(100);
            c.Equal(80, run.State.Hp, "never above max HP");
            c.Equal(0, watch.Calls, "no hook hears a heal outside a fight");

            run.State.Hp = 50;
            run.GainMaxHp(7);
            c.Equal(87, run.State.MaxHp, "max HP 80 + 7");
            c.Equal(57, run.State.Hp, "HP rises by what the maximum rose");
            c.That(Throws<ArgumentException>(() => run.GainMaxHp(-1)), "a negative gain is refused, as in the game");
            c.Equal(0, watch.Calls, "still no hook");
        });

        c.Group("damage outside a fight goes through the damage hooks", () =>
        {
            var run = Stand(new RunSetup { Hp = 50, ExtraRelics = [() => new HpWatch()] });
            var watch = run.State.Relics.OfType<HpWatch>().Single();
            c.Equal(6, run.Damage(6), "an event's 'lose 6 HP': 6 lost");
            c.Equal(44, run.State.Hp, "50 - 6");
            c.Equal(1, watch.Calls, "'HP changed' is heard once");
            c.Equal(-6m, watch.LastDelta, "with the loss");
            c.Equal("combat=False|inProgress=False|player=True|enemies=0", watch.Seen, "by a relic that is told no fight is on");

            // TungstenRod.ModifyHpLostAfterOsty: the player's loss less 1, also with no fight on.
            var rodded = Stand(new RunSetup { Hp = 50, ExtraRelics = [() => new TungstenRod()] });
            var rod = rodded.State.Relics.OfType<TungstenRod>().Single();
            c.Equal(5, rodded.Damage(6), "with Tungsten Rod: 5 lost");
            c.Equal(45, rodded.State.Hp, "50 - 5");
            c.Equal(1, rod.Flashes, "the rod is told it changed the number");
            c.Equal(0, rodded.Damage(1), "a loss of 1 becomes 0");
            c.Equal(45, rodded.State.Hp, "HP unchanged");
            c.Equal(2, rod.Flashes, "and the rod is told again");
        });

        c.Group("losing max HP outside a fight", () =>
        {
            // CreatureCmd.LoseMaxHp: HP above the new maximum is lost as damage that ignores Block and is
            // not modified, through the hooks; then the maximum is set (never below 1), which cuts HP to it.
            var full = Stand(new RunSetup { ExtraRelics = [() => new HpWatch()] });
            full.LoseMaxHp(10);
            c.Equal(70, full.State.MaxHp, "80 - 10");
            c.Equal(70, full.State.Hp, "HP 80 loses the 10 above the new maximum");
            c.Equal(-10m, full.State.Relics.OfType<HpWatch>().Single().LastDelta, "as damage of 10");

            var low = Stand(new RunSetup { Hp = 40, ExtraRelics = [() => new HpWatch()] });
            low.LoseMaxHp(10);
            c.Equal(70, low.State.MaxHp, "max HP 70");
            c.Equal(40, low.State.Hp, "HP 40 is below it and stays");
            c.Equal(0, low.State.Relics.OfType<HpWatch>().Single().Calls, "no damage, no hook");

            // With Tungsten Rod the damage is 9, HP 71, and the new maximum cuts it to 70.
            var rodded = Stand(new RunSetup { ExtraRelics = [() => new TungstenRod(), () => new HpWatch()] });
            rodded.LoseMaxHp(10);
            c.Equal(70, rodded.State.Hp, "with Tungsten Rod: HP 70 all the same");
            c.Equal(70, rodded.State.MaxHp, "max HP 70");
            c.Equal(-9m, rodded.State.Relics.OfType<HpWatch>().Single().LastDelta, "but the damage was 9");
            c.Equal(1, rodded.State.Relics.OfType<TungstenRod>().Single().Flashes, "and the rod flashed");
            c.That(Throws<ArgumentException>(() => rodded.LoseMaxHp(-1)), "a negative loss is refused");
        });
    }

    private static void DeathOutsideAFight(Check c)
    {
        c.Group("a death outside a fight can be prevented by a relic, once", () =>
        {
            // LizardTail: ShouldDieLate says no once; AfterPreventingDeath heals 50% of max HP.
            var run = Stand(new RunSetup { Hp = 30, ExtraRelics = [() => new LizardTail()] });
            run.AfterRoomEntered(RoomKind.Event);
            c.Equal(30, run.Damage(999), "all 30 HP are lost");
            c.Equal(40, run.State.Hp, "Lizard Tail: back to 50% of 80");
            c.That(!run.IsOver, "the run goes on");
            c.That(run.State.Relics.OfType<LizardTail>().Single().WasUsed, "the tail is used up");

            c.Equal(40, run.Damage(999), "the second time nothing saves the player");
            c.Equal(0, run.State.Hp, "HP 0");
            c.Equal(RunResult.Died, run.Result, "the run is over: died");
            var over = run.Snapshot();
            c.Equal("game_over", over.Screen, "the game over screen first, as after a lost fight");
            c.Equal("defeat", (string?)over.State["result"], "its result");
            c.Equal("Event", (string?)over.State["room"], "its room: the kind of room the run was in");
            Do(run, "continue");
            c.Equal(Arena.Run.OverScreen, run.Snapshot().Screen, "then the end snapshot");
            c.Equal("died", (string?)run.Snapshot().State["result"], "with the result");

            run.Heal(10);
            c.Equal(0, run.GainGold(10), "gold after the end: none");
            c.Equal(0, run.State.Hp, "a command after the end changes nothing");
            c.Equal(99, run.State.Gold, "not the gold either");
        });

        c.Group("Fairy in a Bottle prevents a death outside a fight and is used up", () =>
        {
            // The potion's hooks are the registry's: ShouldDie says no for the player; the potion is used, healing 30% of max HP.
            var run = Stand(new RunSetup { Hp = 30, Potions = [null, "FairyInABottle"] });
            c.Equal(30, run.Damage(999), "all 30 HP are lost");
            c.Equal(24, run.State.Hp, "healed to 30% of 80");
            c.That(!run.IsOver, "the run goes on");
            c.That(run.State.Belt.Slots.All(p => p is null), "the potion has left the belt");
            run.Damage(999);
            c.Equal(RunResult.Died, run.Result, "a second death is one");
            c.Equal("Unassigned", (string?)run.Snapshot().State["room"], "no room was entered in this run");
        });
    }

    // ---------- 8. pulling relics ----------

    private static Arena.Run PlayedRun() => new(new RunSetup { Seed = PlayedSeed }, new Standing(world: true));

    private static IReadOnlyList<string> Saved(GoldenSave save, bool shared, RelicRarity rarity) =>
        (shared ? save.SharedBag : save.PlayerBag).Single(list => list.Rarity == rarity).Relics;

    private static IEnumerable<string> Slugs(IEnumerable<string> classNames) => classNames.Select(GameNames.Slug);

    private static void RealRunPulls(Check c)
    {
        c.Group("the real run on seed 1BUSLN0FY5: Arcane Scroll's card, the event's relic, the bags afterwards, the first shop's relics", () =>
        {
            var run = PlayedRun();
            var state = run.State;
            var world = state.Part<RunWorld>();

            // Neow: Arcane Scroll was taken. The save written after the pick shows the player's rewards
            // stream at 1 and Barricade in the deck: one pick among the Ironclad's Rare cards.
            run.ObtainRelic(new ArcaneScroll());
            c.Equal(1, world.Streams.Counter(RunStream.Rewards), "Arcane Scroll's card takes one number of rewards");
            c.Equal("Barricade", state.Deck[^1].Def.ClassName, "and it is the card the real run got");
            c.Equal(11, state.Deck.Count, "the deck has 11 cards");

            // Two fights later the rewards stream stood at 25. The event This or That then gave a relic:
            // one number for the rarity, which was Uncommon, and the front of the player's Uncommon list.
            world.Streams.FastForward(RunStream.Rewards, 25);
            var pull = RunRelics.RollAndPullFromFront(state, world.Streams.Draw(RunStream.Rewards, Purpose.RelicRarity()), strict: true, isBuilt: _ => true);
            c.Equal("StoneCracker", pull.Relic, "the relic the event gave");
            c.Equal(RelicRarity.Uncommon, pull.Asked, "rolled Uncommon");
            c.That(pull.IsTheGames, "nothing passed over, nothing rolled again");
            c.Equal(26, world.Streams.Counter(RunStream.Rewards), "the rewards counter the save then shows");
            c.That(!world.PlayerBag.Contains("StoneCracker") && !world.SharedBag.Contains("StoneCracker"), "a relic pulled from the player's bag has left the shared bag too");
            run.ObtainRelic(new StoneCracker());

            // The save the game wrote after that event: its relics and both its bags.
            var later = GoldenSave.Parse(RunGoldenData.Saves.Single(s => s.File.StartsWith("later-", StringComparison.Ordinal)).Json);
            c.Sequence(later.Root.GetProperty("players")[0].GetProperty("relics").EnumerateArray().Select(r => r.GetProperty("id").GetString()![6..]),
                Slugs(state.Relics.Select(r => r.ClassName)), "the player's relics, as the save lists them");
            foreach (var rarity in RelicBag.PlayerRarities)
            {
                c.Sequence(Saved(later, shared: false, rarity), Slugs(world.PlayerBag.Relics(rarity)), $"the player's {rarity} list, as saved");
                c.Sequence(Saved(later, shared: true, rarity), Slugs(world.SharedBag.Relics(rarity)), $"the shared {rarity} list, as saved");
            }

            // The run's first shop showed Paper Phrog, Pendulum and Ringing Triangle (the game's record of
            // the run): the backs of the Uncommon, Common and Shop lists. Bowler Hat and Lucky Fysh
            // stand behind Paper Phrog and are never sold.
            c.Sequence(["PaperPhrog", "LuckyFysh", "BowlerHat"], world.PlayerBag.Relics(RelicRarity.Uncommon).TakeLast(3), "the back of the Uncommon list");
            var first = RunRelics.PullFromBack(state, RelicRarity.Uncommon, isBuilt: _ => true);
            var second = RunRelics.PullFromBack(state, RelicRarity.Common, isBuilt: _ => true);
            var third = RunRelics.PullFromBack(state, RelicRarity.Shop, isBuilt: _ => true);
            c.Sequence(["PaperPhrog", "Pendulum", "RingingTriangle"], new[] { first.Relic ?? "", second.Relic ?? "", third.Relic ?? "" }, "the shop's three relics");
            c.That(first.IsTheGames && second.IsTheGames && third.IsTheGames, "each is the game's own pull");
            c.Sequence(["LuckyFysh", "BowlerHat"], world.PlayerBag.Relics(RelicRarity.Uncommon).TakeLast(2), "the two a shop never sells are still in the bag");
            c.That(new[] { "PaperPhrog", "Pendulum", "RingingTriangle" }.All(r => !world.PlayerBag.Contains(r) && !world.SharedBag.Contains(r)), "a relic put on a shop's shelf has left both bags");
            // An elite would still get what a shop passes by.
            c.Equal("BowlerHat", world.PlayerBag.Relics(RelicRarity.Uncommon)[^1], "Bowler Hat stays for a pull from the front");
        });

        c.Group("the rarity roll: Common below 0.5, Uncommon below 0.83, else Rare, one number", () =>
        {
            // RelicFactory.RollRarity. The numbers are read off a second copy of each stream.
            var seen = new HashSet<RelicRarity>();
            var wrong = 0;
            for (var seed = 1UL; seed <= 300; seed++)
            {
                var number = new GameRng(seed).NextFloat();
                var expected = number < 0.5f ? RelicRarity.Common : number < 0.83f ? RelicRarity.Uncommon : RelicRarity.Rare;
                var rng = new GameRng(seed);
                var rolled = RunRelics.RollRarity(rng);
                seen.Add(rolled);
                if (rolled != expected || rng.Counter != 1) wrong++;
            }
            c.Equal(0, wrong, "rolls that differ from the thresholds or take more than one number");
            c.That(seen.SetEquals([RelicRarity.Common, RelicRarity.Uncommon, RelicRarity.Rare]), "all three rarities come up, and never Shop");
        });
    }

    private static void LeftOutRelics(Check c)
    {
        // The bag lists of the seed as the game saved them on Neow's screen.
        var saved = GoldenSave.Parse(RunGoldenData.Saves.Single(s => s.File.StartsWith("run-164514", StringComparison.Ordinal)).Json);
        string Name(RelicRarity rarity, int index) => GameLists.SharedRelics.Select(r => r.ClassName).Concat(GameLists.CharacterRelics["Ironclad"].Select(r => r.ClassName))
            .Single(n => GameNames.Slug(n) == Saved(saved, shared: false, rarity)[index]);

        c.Group("a relic the arena has not built: strict pulls nothing, otherwise it is passed over and stays in the bag", () =>
        {
            var run = PlayedRun();
            var state = run.State;
            var world = state.Part<RunWorld>();
            var commons = Enumerable.Range(0, 6).Select(i => Name(RelicRarity.Common, i)).ToList();
            var rares = Enumerable.Range(0, 2).Select(i => Name(RelicRarity.Rare, i)).ToList();
            var built = new HashSet<string> { commons[2], commons[5], rares[1] };
            c.Sequence(commons, world.PlayerBag.Relics(RelicRarity.Common).Take(6), "the front of the Common list is the saved one");

            var strict = RunRelics.PullFromFront(state, RelicRarity.Common, strict: true, isBuilt: built.Contains);
            c.That(!strict.Found, "strict: nothing is pulled");
            c.Equal(commons[0], strict.GameRelic, "the pull names the relic the game gives");
            c.That(strict.Reason is not null && strict.Reason.Contains(commons[0], StringComparison.Ordinal), "and its reason names it");
            c.Sequence(commons, world.PlayerBag.Relics(RelicRarity.Common).Take(6), "the bag is untouched");

            var sharedBefore = world.SharedBag.Relics(RelicRarity.Common).ToList();
            var first = RunRelics.PullFromFront(state, RelicRarity.Common, isBuilt: built.Contains);
            c.Equal(commons[2], first.Relic, "draw again: the first built Common");
            c.Sequence([commons[0], commons[1]], first.PassedOver, "the two before it were passed over");
            c.Equal(commons[0], first.GameRelic, "the game would have given the first");
            c.That(!first.IsTheGames, "so this is not the game's own pull");
            c.Sequence([commons[0], commons[1], commons[3], commons[4], commons[5]], world.PlayerBag.Relics(RelicRarity.Common).Take(5), "they stay in the bag, in place");
            c.Sequence(sharedBefore.Where(r => r != commons[2]), world.SharedBag.Relics(RelicRarity.Common), "only the pulled relic left the shared bag");

            var second = RunRelics.PullFromFront(state, RelicRarity.Common, isBuilt: built.Contains);
            c.Equal(commons[5], second.Relic, "the next built Common");
            c.Sequence([commons[0], commons[1], commons[3], commons[4]], second.PassedOver, "four passed over");

            // No built Common is left. A rarity that was rolled is rolled again on the same stream until
            // it names a list with a built relic: here only Rare has one. The count is worked out on a
            // second copy of the stream.
            var copy = new GameRng(12345);
            var expectedRolls = 0;
            float number;
            do
            {
                number = copy.NextFloat();
                expectedRolls++;
            }
            while (number < 0.83f);
            var rng = new GameRng(12345);
            var third = RunRelics.PullFromFront(state, RelicRarity.Common, rerollWith: rng, isBuilt: built.Contains);
            c.Equal(rares[1], third.Relic, "rolled again until Rare: the first built Rare");
            c.Equal(expectedRolls, third.Rerolls, "as many rolls as it takes to roll Rare");
            c.Equal(expectedRolls, rng.Counter, "each one number of the same stream");
            c.Sequence([rares[0]], third.PassedOver, "the unbuilt Rare before it was passed over");
            c.Equal(RelicRarity.Common, third.Asked, "the pull remembers what was first asked");

            // Now nothing built is left anywhere.
            var counter = rng.Counter;
            var none = RunRelics.PullFromFront(state, RelicRarity.Common, rerollWith: rng, isBuilt: built.Contains);
            c.That(!none.Found && none.Reason is not null, "nothing left to land on: nothing is pulled, with a reason");
            c.Equal(counter, rng.Counter, "and no number is wasted on rolls that cannot help");

            var shop = RunRelics.PullFromBack(state, RelicRarity.Shop, isBuilt: built.Contains);
            c.That(!shop.Found && shop.Reason!.Contains("not rolled", StringComparison.Ordinal), "a rarity that was not rolled (a shop's third slot) is not rolled again");
        });

        c.Group("a chest pulls from the shared bag, and the relic leaves the player's bag only when it is obtained", () =>
        {
            var run = PlayedRun();
            var world = run.State.Part<RunWorld>();
            var front = world.SharedBag.Relics(RelicRarity.Rare)[0];
            c.Equal(Saved(saved, shared: true, RelicRarity.Rare)[0], GameNames.Slug(front), "the front of the shared Rare list is the saved one");
            var pull = RunRelics.PullFromShared(run.State, RelicRarity.Rare, isBuilt: _ => true);
            c.Equal(front, pull.Relic, "the chest's relic");
            c.That(!world.SharedBag.Contains(front), "it has left the shared bag");
            c.That(world.PlayerBag.Contains(front), "and is still in the player's (TreasureRoomRelicSynchronizer pulls from the shared bag alone)");
            RunRelics.TakeOutOfBags(run.State, front);
            c.That(!world.PlayerBag.Contains(front), "obtaining it takes it out of both");
        });
    }

    private static void BagRules(Check c)
    {
        c.Group("an empty rarity gives the next one's: Shop, then Common", () =>
        {
            // RelicGrabBag.GetAvailableDeque. The Ironclad's bag has 26 Shop relics.
            var run = PlayedRun();
            var world = run.State.Part<RunWorld>();
            var lastSellableCommon = world.PlayerBag.Relics(RelicRarity.Common).Last(r => !RunRelics.NeverInShops.Contains(r));
            for (var i = 0; i < 26; i++)
                c.Equal(RelicRarity.Shop, RunRelics.PullFromBack(run.State, RelicRarity.Shop, isBuilt: _ => true).Rarity, $"Shop pull {i + 1} gives a Shop relic");
            c.Equal(0, world.PlayerBag.Relics(RelicRarity.Shop).Count, "the Shop list is empty");
            var next = RunRelics.PullFromBack(run.State, RelicRarity.Shop, isBuilt: _ => true);
            c.Equal(lastSellableCommon, next.Relic, "the next Shop pull takes the back of the Common list (the last one a shop may sell)");
            c.That(next.IsTheGames, "and that is the game's own rule, not the arena's");
        });

        c.Group("relics that only come up before the last chest are dropped from the bag from floor 41 on", () =>
        {
            // RelicModel.IsBeforeAct3TreasureChest: total floor below 41 in single player. The bag drops what
            // is not allowed at every pull, for good.
            var run = PlayedRun();
            var world = run.State.Part<RunWorld>();
            c.Equal(17, RunRelics.OnlyBeforeTheLastChest.Count, "seventeen such relics");
            c.That(RunRelics.OnlyBeforeTheLastChest.All(world.PlayerBag.Contains), "all of them are in a fresh bag");
            run.State.TotalFloor = 40;
            var onFloor40 = RunRelics.PullFromFront(run.State, RelicRarity.Shop, isBuilt: _ => true).Relic!;
            c.Equal(RunRelics.OnlyBeforeTheLastChest.Contains(onFloor40) ? 16 : 17, RunRelics.OnlyBeforeTheLastChest.Count(world.PlayerBag.Contains), "on floor 40 they stay (less the one pulled, if it was one of them)");
            run.State.TotalFloor = 41;
            RunRelics.PullFromFront(run.State, RelicRarity.Shop, isBuilt: _ => true);
            c.Equal(0, RunRelics.OnlyBeforeTheLastChest.Count(world.PlayerBag.Contains), "on floor 41 a pull drops them all");
            c.That(world.PlayerBag.Contains("Anchor"), "and nothing else");
        });

        c.Group("the relics a shop never sells", () =>
        {
            c.That(RunRelics.NeverInShops.SetEquals(["AmethystAubergine", "BowlerHat", "LuckyFysh", "OldCoin", "TheCourier"]), "the five whose class says so");
            c.That(RunRelics.NeverInShops.All(GameLists.IsBagRelic), "all five are bag relics");
            c.That(RunRelics.OnlyBeforeTheLastChest.All(GameLists.IsBagRelic), "and so are the seventeen");
            c.That(!RunRelics.IsAllowed("LastingCandy", new RunState(new RunSetup()), new RunUnlocks([], [], 0, [])), "Lasting Candy does not come up in a profile's first run");
            c.That(RunRelics.IsAllowed("LastingCandy", new RunState(new RunSetup()), RunUnlocks.All), "and does afterwards");
            c.That(RunRelics.IsBuilt("BurningBlood") && !RunRelics.IsBuilt("NoSuchRelic"), "a built relic is one the registry holds");
        });
    }

    // ---------- 9. the hooks ----------

    private static void HookTable(Check c)
    {
        c.Group("the 27 hooks outside a fight that the reachable relics use", () =>
        {
            var hooks = RunHooks.Hooks;
            c.Equal(27, hooks.Count, "hooks listed");
            c.Equal(27, hooks.Select(h => h.Name).Distinct(StringComparer.Ordinal).Count(), "each once");
            // relics.json: how many of the 157 reachable relics override each.
            c.Equal(105, hooks.Sum(h => h.Users), "uses in all: 38 + 23 + 7 + 4 + 3 x 3 + 4 x 2 + 16 x 1");
            c.Equal(24, hooks.Count(h => h.IsDeclared), "declared now: eighteen with the relic framework, six with the rewards");
            c.Sequence(["ModifyMerchantPrice", "ShouldRefillMerchantEntry", "TryModifyRestSiteOptions"],
                hooks.Where(h => !h.IsDeclared).Select(h => h.Name).OrderBy(n => n, StringComparer.Ordinal),
                "waiting for the types of later parts: the shop's two and the rest site's one");
            var onRewards = new[]
            {
                "ModifyCardRewardCreationOptions", "ModifyMerchantCardCreationResults", "TryModifyCardRewardOptions",
                "TryModifyCardRewardOptionsLate", "TryModifyRestSiteHealRewards", "TryModifyRewards",
            };
            foreach (var hook in hooks)
            {
                var onListener = typeof(Relic).GetMethods().Any(m => m.Name == hook.Name && m.IsVirtual && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(RunContext));
                var onInterface = typeof(IRewardHooks).GetMethods().Any(m => m.Name == hook.Name && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(RunContext));
                c.That(!(onListener && onInterface), $"{hook.Name} is declared in one place only");
                if (hook.IsDeclared)
                {
                    var withRewards = onRewards.Contains(hook.Name);
                    c.That(withRewards ? onInterface : onListener,
                        $"{hook.Name} is a hook a relic can {(withRewards ? "implement (IRewardHooks)" : "override")}, handed a RunContext");
                    c.That(hook.SentBy.Length > 0, $"{hook.Name} names who sends it");
                    c.Equal(withRewards, hook.SentBy.StartsWith("RewardHooks.", StringComparison.Ordinal), $"{hook.Name}: the rewards send their own hooks and no others");
                    // Every call site the list names is there.
                    foreach (var named in hook.SentBy.Split(", "))
                    {
                        var site = named.Split(' ')[0].Split('.');
                        var owner = typeof(RunHooks).Assembly.GetType("SpireMind.Arena." + site[0]);
                        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
                        c.That(site.Length == 2 && owner is not null && owner.GetMethods(any).Any(m => m.Name == site[1]), $"{hook.Name}: the call site {named.Split(' ')[0]} exists");
                    }
                }
                else
                {
                    c.That(!onListener && !onInterface, $"{hook.Name} is listed as waiting; once it is declared the list must say who sends it");
                    c.That(!string.IsNullOrEmpty(hook.Shape), $"{hook.Name} says how the game sends it");
                }
            }

            // One declaration per hook across the three places a hook of the run can be declared.
            static IEnumerable<string> Names(Type type) => type.GetMethods().Where(m => m.GetParameters().FirstOrDefault()?.ParameterType == typeof(RunContext)).Select(m => m.Name);
            var twice = Names(typeof(Relic)).Distinct().Concat(Names(typeof(IRewardHooks))).Concat(Names(typeof(IDeckListener)))
                .GroupBy(n => n, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            c.Equal("", string.Join(", ", twice), "no hook of the run is declared both on Listener and on an interface of a part");
        });
    }

    /// <summary>A relic that answers the run's simple hooks as it is told to and notes that it was asked.</summary>
    private sealed class Voice : Relic
    {
        public string Tag { get; init; } = "?";
        public bool Travel { get; init; }
        public bool Treasure { get; init; } = true;
        public bool DisableRest { get; init; } = true;
        public decimal HealPlus { get; init; }
        public decimal HealTimes { get; init; } = 1m;
        public string Heard = "";

        public override string Name => "Voice " + Tag;
        public override string Describe() => "(a check's listener)";

        public override bool ShouldAllowFreeTravel(RunContext c)
        {
            Heard += "travel,";
            return Travel;
        }

        public override bool ShouldGenerateTreasure(RunContext c)
        {
            Heard += "treasure,";
            return Treasure;
        }

        public override bool ShouldDisableRemainingRestSiteOptions(RunContext c)
        {
            Heard += "rest,";
            return DisableRest;
        }

        public override bool ShouldForcePotionReward(RunContext c, RoomKind room)
        {
            Heard += "potion,";
            return room == RoomKind.Elite && Travel;
        }

        public override decimal ModifyRestSiteHealAmount(RunContext c, decimal amount) => (amount + HealPlus) * HealTimes;
        public override void AfterRestSiteHeal(RunContext c, bool isMimicked) => Heard += isMimicked ? "healed-mimicked," : "healed,";
        public override IReadOnlyList<string> ModifyExtraRestSiteHealText(RunContext c, IReadOnlyList<string> extraText) => [.. extraText, Tag];
        public override void AfterModifyingRewards(RunContext c) => c.NeedRun("a note").State.Part<Notes>().Text += Tag + ",";
        public override void AfterModifyingCardRewardOptions(RunContext c) => c.NeedRun("a note").State.Part<Notes>().Text += Tag + "!,";
        public override void AfterPotionProcured(RunContext c, PotionDef potion) => Heard += "got " + potion.ClassName + ",";
        public override void AfterPotionDiscarded(RunContext c, PotionDef potion) => Heard += "lost " + potion.ClassName + ",";
    }

    /// <summary>MoltenEgg.cs, the part that matters when a card joins the deck: an Attack that can be upgraded joins upgraded.</summary>
    private sealed class MoltenEgg : Relic
    {
        public override string Name => "Molten Egg";
        public override string Describe() => "Attacks you add to your deck are upgraded.";

        public override bool TryModifyCardBeingAddedToDeck(RunContext c, CardInstance card, out CardInstance? newCard)
        {
            newCard = null;
            if (card.Type != CardType.Attack || !card.IsUpgradable) return false;
            newCard = c.NeedRun("an upgraded copy").State.NewCard(card.Def, upgraded: true);
            return true;
        }
    }

    private sealed class LateSwap : Relic
    {
        public override string Name => "Late Swap";
        public override string Describe() => "(a check's relic)";

        public override bool TryModifyCardBeingAddedToDeckLate(RunContext c, CardInstance card, out CardInstance? newCard)
        {
            newCard = card.IsUpgraded ? c.NeedRun("a card").State.NewCard(Content.Card("Bash")) : null;
            return newCard is not null;
        }
    }

    private static void HookCallSites(Check c)
    {
        c.Group("hooks that ask yes or no stop at the answer that decides", () =>
        {
            Func<Relic>[] three = [() => new Voice { Tag = "1" }, () => new Voice { Tag = "2", Travel = true, Treasure = false, DisableRest = false }, () => new Voice { Tag = "3" }];
            var run = Stand(new RunSetup { ExtraRelics = three });
            var voices = run.State.Relics.OfType<Voice>().ToList();

            // Hook.ShouldAllowFreeTravel: flag = flag || ..., so nobody is asked after the first yes.
            c.That(RunHooks.ShouldAllowFreeTravel(run.Context), "free travel: the second relic says yes");
            c.Sequence(["travel,", "travel,", ""], voices.Select(v => v.Heard), "the third is not asked");
            // Hook.ShouldGenerateTreasure and ShouldDisableRemainingRestSiteOptions: false at the first no.
            c.That(!RunHooks.ShouldGenerateTreasure(run.Context), "treasure: the second says no");
            c.That(!RunHooks.ShouldDisableRemainingRestSiteOptions(run.Context), "rest site options stay: the second says no");
            c.Sequence(["travel,treasure,rest,", "travel,treasure,rest,", ""], voices.Select(v => v.Heard), "the third is never asked");
            c.That(RunHooks.ShouldForcePotionReward(run.Context, RoomKind.Elite), "a potion is forced after an elite");
            c.That(!RunHooks.ShouldForcePotionReward(run.Context, RoomKind.Monster), "not after a hallway fight");

            var plain = Stand();
            c.That(!RunHooks.ShouldAllowFreeTravel(plain.Context) && RunHooks.ShouldGenerateTreasure(plain.Context)
                && RunHooks.ShouldDisableRemainingRestSiteOptions(plain.Context) && !RunHooks.ShouldForcePotionReward(plain.Context, RoomKind.Boss),
                "with Burning Blood alone every answer is the game's default");
        });

        c.Group("hooks that pass a value on go through the relics in pickup order", () =>
        {
            Func<Relic>[] two = [() => new Voice { Tag = "plus", HealPlus = 5 }, () => new Voice { Tag = "double", HealTimes = 2 }, () => new JuzuBracelet()];
            var run = Stand(new RunSetup { ExtraRelics = two });
            c.Equal(58m, RunHooks.ModifyRestSiteHealAmount(run.Context, 24), "(24 + 5) x 2, not 24 x 2 + 5");
            c.Sequence(["first", "plus", "double"], RunHooks.ModifyExtraRestSiteHealText(run.Context, ["first"]), "lines are added in order");
            RunHooks.AfterRestSiteHeal(run.Context, isMimicked: false);
            RunHooks.AfterRestSiteHeal(run.Context, isMimicked: true);
            c.Equal("healed,healed-mimicked,", run.State.Relics.OfType<Voice>().First().Heard, "'after rest site heal' is told whether it was mimicked");

            var allowed = UnknownRoomOdds.Allowed(shopBarred: false);
            var left = RunHooks.ModifyUnknownMapPointRoomTypes(run.Context, allowed);
            c.That(!left.Contains(RolledRoom.Monster) && left.Contains(RolledRoom.Event) && left.Contains(RolledRoom.Shop) && left.Contains(RolledRoom.Treasure), "Juzu Bracelet takes the hallway fight out of an Unknown room");
            c.That(allowed.Contains(RolledRoom.Monster), "the set that was passed in is not changed");

            RunHooks.AfterPotionProcured(run.Context, Content.Potion("FirePotion"));
            RunHooks.AfterPotionDiscarded(run.Context, Content.Potion("FirePotion"));
            c.Equal("healed,healed-mimicked,got FirePotion,lost FirePotion,", run.State.Relics.OfType<Voice>().First().Heard, "potion gained and thrown away");

            var voices = run.State.Relics.OfType<Voice>().ToList();
            RunHooks.AfterModifyingRewards(run.Context, [voices[1], voices[0]]);
            RunHooks.AfterModifyingCardRewardOptions(run.Context, [voices[1]]);
            RunHooks.AfterModifyingRewards(run.Context, []);
            c.Equal("plus,double,double!,", run.State.Part<Notes>().Text, "the listeners that changed something are told, in listener order, and only they");
        });

        c.Group("a card being added to the deck can be swapped: first pass, then the late pass", () =>
        {
            var run = Stand(new RunSetup { ExtraRelics = [() => new LateSwap(), () => new MoltenEgg()] });
            var strike = run.State.NewCard(Content.Card("StrikeIronclad"));
            var added = RunHooks.ModifyCardBeingAddedToDeck(run.Context, strike, out var modifiers);
            // Molten Egg hands back Strike+ in the first pass; the late relic, first in pickup order, sees it only then.
            c.Equal("Bash", added.Def.ClassName, "the late pass sees the card as the first pass left it");
            c.Sequence(["MoltenEgg", "LateSwap"], modifiers.Cast<Relic>().Select(r => r.ClassName), "who changed it, in the order they did");

            var defend = run.State.NewCard(Content.Card("DefendIronclad"));
            c.That(ReferenceEquals(defend, RunHooks.ModifyCardBeingAddedToDeck(run.Context, defend, out var none)) && none.Count == 0, "a Skill is left as it is");
        });
    }

    // ---------- 10. the guard on listening deck cards ----------

    private sealed class LoudDeckVoice : CardBehaviour, IListensInDeck
    {
        public override void AfterCombatEnd(Fight f) { }
        public override void AfterGoldGained(RunContext c) { }
        public override void AfterCardPlayed(Fight f, CardPlay play) { }
        public override void AfterPlayerTurnStart(Fight f) { }
    }

    private static void DeckListeningGuard(Check c)
    {
        c.Group("a card that listens in the deck may override only hooks the game sends to deck cards", () =>
        {
            c.Sequence(Array.Empty<string>(), DeckListening.ForbiddenOverrides(typeof(DeckVoice)), "the run's hooks and 'after combat end' are heard by deck cards");
            c.Sequence(["AfterCardPlayed", "AfterPlayerTurnStart"], DeckListening.ForbiddenOverrides(typeof(LoudDeckVoice)), "a card played and a turn starting are the fight's alone");
            c.Sequence(Array.Empty<string>(), DeckListening.Problems(), "no registered card breaks the rule");
            c.Equal(27, DeckListening.HeardByDeckCards.Count, "the fight hooks the game also sends to the deck cards");
            c.That(DeckListening.HeardByDeckCards.All(name => typeof(Listener).GetMethod(name) is { IsVirtual: true }), "each is a hook of Listener");
        });
    }
}
