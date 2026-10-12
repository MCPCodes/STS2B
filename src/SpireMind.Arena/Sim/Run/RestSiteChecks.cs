using System.Text.Json.Nodes;
using SpireMind.Core;
using static SpireMind.Arena.DeckCheckKit;

namespace SpireMind.Arena;

/// <summary>
/// The rest site: its two options, what a rest heals, Smith with its card choice and its way
/// back, the hooks that change any of it, and the screen as the mod shows it. Every expected
/// number is worked out by hand from the game's code (30% of max HP, cut to whole HP as HP is;
/// HealRestSiteOption, RestSiteSynchronizer) or is the real game's: the three rest sites of
/// the first act of Mark's winning run on seed 75F277YN2G, as the game's own record of it has
/// them (sts2-notes/golden-saves/history/1791592972.run, floors 8, 12 and 16). The relics of
/// the checks are the game's classes as far as a rest site asks (Regal Pillow, Stone
/// Humidifier, Tiny Mailbox, Miniature Tent, Girya, Shovel's shape), written here because none
/// of them is built yet; they are the checks' own and are never registered.
/// </summary>
public sealed class RestSiteChecks : IArenaChecks
{
    public void Run(Check c)
    {
        Arriving(c);
        Resting(c);
        RealRests(c);
        Smithing(c);
        SmithDisabled(c);
        HealHooks(c);
        HealRewards(c);
        MimickedRest(c);
        KeptOptions(c);
        ChangedOptions(c);
        ShortForm(c);
        Refusals(c);
        PotionsRideAlong(c);
        OptionList(c);
        ReplayAndCopy(c);
    }

    // ---------- the directors and screens of the checks ----------

    /// <summary>A run that stands at a rest site from its first screen.</summary>
    private sealed class AtRestSite : RunDirector
    {
        /// <summary>Begin the run's world first (the boss key, the streams of rewards).</summary>
        public bool World { get; init; }

        public int Floor { get; init; } = 8;

        /// <summary>How often a room was finished.</summary>
        public int RoomsLeft;

        public override void Begin(Arena.Run run)
        {
            if (World) RunWorld.Begin(run.State);
            run.State.TotalFloor = Floor;
            RestSite.Enter(run);
        }

        public override void AfterFightWon(Arena.Run run, FightOutcome fight) => throw new NotSupportedException("No fight is part of these checks.");

        public override void AfterRoom(Arena.Run run)
        {
            RoomsLeft++;
            run.CloseAll();
            run.Push(new Outside());
        }
    }

    /// <summary>What a finished room leads to in these checks: a screen with nothing on it.</summary>
    private sealed class Outside : RunScreen
    {
        public override string Name => "map";

        public override GameSnapshot Snapshot(Arena.Run run) => new()
        {
            Screen = Name, AwaitingDecision = true, State = new JsonObject { ["run"] = run.RunJson() }, Actions = [new LegalAction("stay", "Stay")],
        };

        public override ActionResult Execute(Arena.Run run, string actionId) =>
            actionId == "stay" ? ActionResult.Success : ActionResult.Fail($"Unknown map action '{actionId}'.");

        public override RunScreen Clone(CloneMap map) => new Outside();
    }

    private const string RealSeed = "1BUSLN0FY5";

    private static Arena.Run Rest(RunSetup? setup = null, bool world = false, int floor = 8) =>
        new(setup ?? new RunSetup(), new AtRestSite { World = world, Floor = floor });

    private static RunSetup Hurt(int hp, int maxHp = 80, params Func<Relic>[] relics) => new() { Hp = hp, MaxHp = maxHp, ExtraRelics = relics };

    private static string Options(GameSnapshot s) => Text(s.State["options"]);

    private static string Chosen(GameSnapshot s) => s.State["chosen"] is { } chosen ? Text(chosen) : "(none)";

    private static bool Offers(GameSnapshot s, string id) => s.Actions.Any(a => a.Id == id);

    // ---------- the relics of the checks: the game's classes, as far as a rest site asks ----------

    /// <summary>RegalPillow.cs: a rest heals 15 more, and the Rest option says so in a line of its own.</summary>
    private sealed class RegalPillow : Relic
    {
        public override string Name => "Regal Pillow";
        public override string Describe() => "Resting heals 15 more HP.";
        public override decimal ModifyRestSiteHealAmount(RunContext c, decimal amount) => amount + 15m;
        public override IReadOnlyList<string> ModifyExtraRestSiteHealText(RunContext c, IReadOnlyList<string> extraText) => [.. extraText, "Regal Pillow: 15 more HP."];
    }

    /// <summary>A relic of the checks alone that doubles what a rest heals: with Regal Pillow it shows the order the amount passes through the relics.</summary>
    private sealed class Doubler : Relic
    {
        public override string Name => "Doubler";
        public override string Describe() => "(a check's relic) Resting heals twice as much.";
        public override decimal ModifyRestSiteHealAmount(RunContext c, decimal amount) => amount * 2m;
    }

    /// <summary>StoneHumidifier.cs: after a rest's heal, gain 5 Max HP; a line of the Rest option says so.</summary>
    private sealed class StoneHumidifier : Relic
    {
        public string Heard = "";
        public override string Name => "Stone Humidifier";
        public override string Describe() => "Whenever you rest, gain 5 Max HP.";

        public override void AfterRestSiteHeal(RunContext c, bool isMimicked)
        {
            Heard += isMimicked ? $"mimicked at {c.Hp}," : $"rested at {c.Hp},";
            c.GainMaxHp(5m);
        }

        public override IReadOnlyList<string> ModifyExtraRestSiteHealText(RunContext c, IReadOnlyList<string> extraText) => [.. extraText, "Stone Humidifier: gain 5 Max HP."];
    }

    /// <summary>TinyMailbox.cs: a rest also gives two potion rewards. It counts whether it is told that it changed rewards,
    /// which the game does not do for a rest's rewards.</summary>
    private sealed class TinyMailbox : Relic, IRewardHooks
    {
        public string Heard = "";
        public override string Name => "Tiny Mailbox";
        public override string Describe() => "Whenever you rest, find 2 random potions.";

        public bool TryModifyRestSiteHealRewards(RunContext c, List<Reward> rewards, bool isMimicked)
        {
            Heard += isMimicked ? "rewards-mimicked," : "rewards,";
            rewards.Add(new PotionReward());
            rewards.Add(new PotionReward());
            return true;
        }

        public override void AfterModifyingRewards(RunContext c) => Heard += "told,";
    }

    /// <summary>A relic of the checks alone whose rest reward is a fixed 7 gold: a reward whose every number can be worked out.</summary>
    private sealed class Tip : Relic, IRewardHooks
    {
        public override string Name => "Tip";
        public override string Describe() => "(a check's relic) Whenever you rest, find 7 gold.";

        public bool TryModifyRestSiteHealRewards(RunContext c, List<Reward> rewards, bool isMimicked)
        {
            rewards.Add(new GoldReward(7));
            return true;
        }
    }

    /// <summary>MiniatureTent.cs: the other options stay after one was used. It counts how often it is asked.</summary>
    private sealed class MiniatureTent : Relic
    {
        public int Asked;
        public override string Name => "Miniature Tent";
        public override string Describe() => "You may use any number of options at a rest site.";

        public override bool ShouldDisableRemainingRestSiteOptions(RunContext c)
        {
            Asked++;
            return false;
        }
    }

    /// <summary>A relic that keeps the game's default and notes when it hears the rest site's hooks.</summary>
    private sealed class Witness : Relic
    {
        public string Heard = "";
        public override string Name => "Witness";
        public override string Describe() => "(a check's listener)";

        public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)
        {
            Heard += $"options {string.Join("+", options.Select(o => o.OptionId))},";
            return false;
        }

        public override void AfterRoomEntered(RunContext c, RoomKind room) => Heard += $"entered {room},";

        public override bool ShouldDisableRemainingRestSiteOptions(RunContext c)
        {
            Heard += "disable?,";
            return true;
        }

        public override void AfterRestSiteHeal(RunContext c, bool isMimicked) => Heard += "healed,";
    }

    /// <summary>Girya.cs, the rest site's part: it adds Lift while it was lifted fewer than three times.</summary>
    private sealed class Girya : Relic
    {
        public int TimesLifted;
        public override string Name => "Girya";
        public override string Describe() => "You can Lift at rest sites, up to 3 times, for 1 Strength at the start of every combat each.";

        public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)
        {
            if (TimesLifted >= 3) return false;
            options.Add(new LiftRestSiteOption());
            return true;
        }
    }

    /// <summary>LiftRestSiteOption.cs: one more lift; it always succeeds.</summary>
    private sealed class LiftRestSiteOption : RestSiteOption
    {
        public override string OptionId => "LIFT";
        public override string Title => "Lift";

        public override string Describe(Arena.Run run) =>
            $"Gain 1 Strength at the start of every combat. {3 - run.State.FindRelic<Girya>()!.TimesLifted} lifts left.";

        public override bool OnSelect(Arena.Run run)
        {
            run.State.FindRelic<Girya>()!.TimesLifted++;
            return true;
        }
    }

    /// <summary>The modifier Midas, as far as a rest site asks: it takes Smith off the list.</summary>
    private sealed class NoSmith : Relic
    {
        public override string Name => "No Smith";
        public override string Describe() => "(a check's relic) Rest sites have no Smith.";

        public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)
        {
            foreach (var smith in options.OfType<SmithRestSiteOption>().ToList()) options.Remove(smith);
            return true;
        }
    }

    /// <summary>A relic of the checks alone that empties the list.</summary>
    private sealed class NoOptions : Relic
    {
        public override string Name => "No Options";
        public override string Describe() => "(a check's relic) Rest sites offer nothing.";

        public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)
        {
            options.Clear();
            return true;
        }
    }

    /// <summary>An option of the checks alone with no text, which always succeeds.</summary>
    private sealed class HatchRestSiteOption : RestSiteOption
    {
        public override string OptionId => "HATCH";
        public override string Title => "Hatch";
        public override string Describe(Arena.Run run) => "";
        public override bool OnSelect(Arena.Run run) => true;
    }

    /// <summary>ByrdonisEgg.cs, the rest site's part: the DECK card adds Hatch.</summary>
    private sealed class EggVoice : CardBehaviour, IListensInDeck
    {
        public override bool TryModifyRestSiteOptions(RunContext c, ICollection<RestSiteOption> options)
        {
            options.Add(new HatchRestSiteOption());
            return true;
        }
    }

    private static readonly CardDef Egg = new("CheckEgg", "Egg", CardType.Quest, CardRarity.Quest, CardDef.NoCost, TargetType.None)
    {
        Keywords = CardKeywords.Unplayable, MaxUpgradeLevel = 0, Text = _ => "A quest card of the checks.", Behaviour = () => new EggVoice(),
    };

    // ---------- 1. arriving ----------

    private static void Arriving(Check c)
    {
        c.Group("arriving at a rest site with 41 of 80 HP: the snapshot the mod gives (docs/screens.md, Rest site)", () =>
        {
            var setup = new RunSetup
            {
                Seed = RealSeed, Hp = 41, Gold = 131, Potions = ["BloodPotion"],
                Deck = [.. RunSetup.IroncladDeck, new("PommelStrike"), new("Thunderclap"), new("ShrugItOff")],
                ExtraRelics = [() => new Witness()],
            };
            var run = Rest(setup, world: true);
            var s = run.Snapshot();

            c.Equal("rest", s.Screen, "the screen");
            c.That(s.AwaitingDecision, "a decision");
            c.Equal("run,hp,max_hp,boss,player,potions,options", Keys(s.State), "the state's keys in the mod's order; no chosen yet");
            c.Equal("""{"act":1,"floor":8,"ascension":0}""", Text(s.State["run"]), "the run block");
            c.Equal(41, s.State["hp"]!.GetValue<int>(), "hp at top level");
            c.Equal(80, s.State["max_hp"]!.GetValue<int>(), "max_hp at top level");
            c.Equal("Vantom", s.State["boss"]!.GetValue<string>(), "the act's boss, as the real save of this seed names it");
            c.Equal("""{"character":"The Ironclad","gold":131,"relics":["Burning Blood","Witness"],"deck_size":13,"deck":{"Bash":1,"Defend":4,"Pommel Strike":1,"Shrug It Off":1,"Strike":5,"Thunderclap":1}}""",
                Text(s.State["player"]), "the player block: gold instead of hp and max_hp, no potions");
            c.Equal("""[{"slot":0,"name":"Blood Potion","text":"Heal 20% of your Max HP.","target":"AnyPlayer","usable":true},null,null]""",
                Text(s.State["potions"]), "the detailed potions");
            // 30% of 80 is 24.
            c.Equal("""[{"n":0,"id":"HEAL","name":"Rest","text":"Heal 24 HP."},{"n":1,"id":"SMITH","name":"Smith","text":"Upgrade a card in your deck."}]""",
                Options(s), "the two options in the game's order");
            c.Equal("option:0=Rest (heal 24) | option:1=Smith (upgrade a card) | potion:0=Use Blood Potion | discard_potion:0=Discard Blood Potion",
                Labels(s), "the options, then the potion ids; no proceed before an option succeeded");

            c.Equal("options HEAL+SMITH,entered RestSite,", run.State.FindRelic<Witness>()!.Heard,
                "the options are made, and the listeners asked about them, before 'after room entered' is sent");
            c.Equal(RoomKind.RestSite, run.Room, "the room the hooks were told about");
            c.Equal(0, ((AtRestSite)run.Director).RoomsLeft, "the room is not finished");
        });

        c.Group("a rest site without a world has no boss to name", () =>
        {
            var s = Rest(Hurt(41)).Snapshot();
            c.Equal("run,hp,max_hp,player,potions,options", Keys(s.State), "the boss key is left out");
            c.Equal("option:0 option:1", Ids(s), "no potion is held: the options alone");
        });
    }

    // ---------- 2. Rest ----------

    private static void Resting(Check c)
    {
        c.Group("Rest heals 30% of max HP, uses the visit up and turns proceed on", () =>
        {
            var run = Rest(Hurt(41, 80, () => new Witness()));
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal(65, run.Hp, "41 + 24");
            c.Equal(65, s.State["hp"]!.GetValue<int>(), "the snapshot's hp");
            c.Equal("[]", Options(s), "one option, then the rest vanish");
            c.Equal("""["Rest"]""", Chosen(s), "chosen names the option used");
            c.Equal("run,hp,max_hp,player,potions,options,chosen", Keys(s.State), "chosen comes last");
            c.Equal("proceed=Proceed", Labels(s), "proceed is the one action left");
            c.Sequence(["HEAL"], ((RestSiteScreen)run.Top!).ChosenIds, "the game's own record of the choice");
            c.Equal("options HEAL+SMITH,entered RestSite,healed,disable?,", run.State.FindRelic<Witness>()!.Heard,
                "the heal's hook, then the question whether the other options go");

            Do(run, "proceed");
            c.Equal("map", run.Snapshot().Screen, "proceed finishes the room");
            c.Equal(1, ((AtRestSite)run.Director).RoomsLeft, "once");
            c.Sequence(["option:0", "proceed"], run.Actions, "a normal visit is two actions");
        });

        c.Group("the heal is cut to whole HP as HP is, and never goes above max HP", () =>
        {
            // 0.3 x 87 = 26.1: the option says 26, and 50 + 26.1 = 76.1 becomes 76.
            var run = Rest(Hurt(50, 87));
            c.Equal("option:0=Rest (heal 26) | option:1=Smith (upgrade a card)", Labels(run.Snapshot()), "26.1 is shown as 26");
            c.Equal("Heal 26 HP.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "in the text too");
            Do(run, "option:0");
            c.Equal(76, run.Hp, "50 + 26.1, cut");

            // 0.3 x 75 = 22.5: the option says 22, and 10 + 22.5 = 32.5 becomes 32.
            run = Rest(Hurt(10, 75));
            c.That(Offers(run.Snapshot(), "option:0") && run.Snapshot().Actions[0].Label == "Rest (heal 22)", "22.5 is shown as 22");
            Do(run, "option:0");
            c.Equal(32, run.Hp, "10 + 22.5, cut (no rounding up)");

            run = Rest(Hurt(70));
            Do(run, "option:0");
            c.Equal(80, run.Hp, "70 + 24 stops at 80");

            run = Rest(Hurt(80));
            c.That(Offers(run.Snapshot(), "option:0"), "Rest is offered at full HP too: the game never greys it out");
            Do(run, "option:0");
            c.Equal(80, run.Hp, "and heals nothing");
            c.Equal("""["Rest"]""", Chosen(run.Snapshot()), "but uses the visit up");

            c.Equal(24m, RestSite.BaseHealAmount(Rest(Hurt(1))), "the base amount at 80 max HP");
            c.Equal(26.1m, RestSite.BaseHealAmount(Rest(Hurt(1, 87))), "and at 87, not rounded");
        });
    }

    // ---------- 3. the real game's rest sites ----------

    private static void RealRests(Check c)
    {
        c.Group("the rest sites of a real run's first act (seed 75F277YN2G, the game's own record)", () =>
        {
            // Floor 12: 35 of 80 HP on arriving (the fight before left 35), HEAL chosen, hp_healed 24, 59 afterwards.
            var run = Rest(Hurt(35), floor: 12);
            Do(run, "option:0");
            c.Equal(59, run.Hp, "floor 12: 35 + 24 = 59, as the record has it");

            // Floor 16: 52 of 80 HP, HEAL chosen, hp_healed 24, 76 afterwards.
            run = Rest(Hurt(52), floor: 16);
            c.Equal("""{"act":1,"floor":16,"ascension":0}""", Text(run.Snapshot().State["run"]), "the last rest site of an act is on floor 16");
            Do(run, "option:0");
            c.Equal(76, run.Hp, "floor 16: 52 + 24 = 76, as the record has it");

            // Floor 8: 49 of 80 HP and 100 gold, SMITH chosen, upgraded_cards [INFLAME], HP and gold unchanged. The deck by
            // then: Bash upgraded by Pomander at Neow, one Strike removed in the shop, and Twin Strike, Anger, Bloodletting,
            // Twin Strike and Inflame added in that order.
            var setup = new RunSetup
            {
                Hp = 49, Gold = 100,
                Deck =
                [
                    new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"), new("StrikeIronclad"),
                    new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad"), new("DefendIronclad"), new("Bash", true),
                    new("TwinStrike"), new("Anger"), new("Bloodletting"), new("TwinStrike"), new("Inflame"),
                ],
            };
            run = Rest(setup);
            Do(run, "option:1");
            var s = run.Snapshot();
            c.Equal("card_select", s.Screen, "Smith opens the game's upgrade grid");
            c.Equal("upgrade", s.State["kind"]!.GetValue<string>(), "kind upgrade");
            c.Equal((1, 1), (s.State["min"]!.GetValue<int>(), s.State["max"]!.GetValue<int>()), "one card");
            // Bash+ cannot be upgraded again and is not listed; the two Twin Strikes are one entry under the first one's index.
            c.Equal("card:0 card:4 card:8 card:9 card:10 card:12 cancel", Ids(s), "every card that can still be upgraded, identical copies once, and the close button");
            Do(run, "card:12");
            s = run.Snapshot();
            c.Sequence(["Inflame", "Inflame+"], s.State["preview"]!.AsArray().Select(card => card!["name"]!.GetValue<string>()), "the preview shows the card and its upgrade");
            c.Equal("confirm=Confirm: upgrade Inflame | cancel=Back to the cards (pick a different one)", Labels(s), "confirm or go back");
            Do(run, "confirm");
            s = run.Snapshot();
            c.Equal("rest", s.Screen, "back at the rest site");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+ TwinStrike Anger Bloodletting TwinStrike Inflame+",
                DeckNames(run), "Inflame is upgraded where it stands; nothing else changed");
            c.Equal((49, 100), (run.Hp, run.Gold), "HP and gold as before, as the record has them");
            c.Equal("""["Smith"]""", Chosen(s), "chosen");
            c.Sequence(["SMITH"], ((RestSiteScreen)run.Top!).ChosenIds, "the record's rest_site_choices");
            c.Equal("proceed=Proceed", Labels(s), "Smith used the visit up");
        });
    }

    // ---------- 4. Smith ----------

    private static void Smithing(Check c)
    {
        c.Group("Smith upgrades the chosen card; with a fresh deck the grid is the whole deck", () =>
        {
            var run = Rest(Hurt(41, 80, () => new Witness()));
            Do(run, "option:1");
            var s = run.Snapshot();
            c.Equal("card:0 card:5 card:9 cancel", Ids(s), "Strike, Defend and Bash once each, and the close button");
            c.Equal("character,hp,max_hp,gold,relics,potions,deck_size", Keys(s.State["player"]), "the grid lists every card of the deck: the deck's counts are left out");
            Do(run, "card:9", "confirm");
            c.Equal("StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad StrikeIronclad DefendIronclad DefendIronclad DefendIronclad DefendIronclad Bash+", DeckNames(run), "Bash is upgraded");
            c.Equal(41, run.Hp, "Smith heals nothing");
            s = run.Snapshot();
            c.Equal("[]", Options(s), "the other option is gone");
            c.Equal("""["Smith"]""", Chosen(s), "chosen");
            c.Equal("proceed=Proceed", Labels(s), "proceed is on");
            c.Equal("options HEAL+SMITH,entered RestSite,disable?,", run.State.FindRelic<Witness>()!.Heard, "no heal hook for Smith");
            c.Sequence(["option:1", "card:9", "confirm"], run.Actions, "the save: the option, then the steps of its card choice");
        });

        c.Group("backing out of Smith's card choice keeps the visit: the options are back and proceed stays off", () =>
        {
            var run = Rest(Hurt(41, 80, () => new Witness()));
            var before = run.Snapshot().ToJson().ToJsonString();
            Do(run, "option:1", "cancel");
            var s = run.Snapshot();
            c.Equal("rest", s.Screen, "closing the grid brings the rest site back");
            c.Equal(before, s.ToJson().ToJsonString(), "exactly as on arrival: both options, no chosen, no proceed");
            c.Equal("options HEAL+SMITH,entered RestSite,", run.State.FindRelic<Witness>()!.Heard, "nobody is asked whether the other options go");

            // Out of the preview first (back to the cards), then out of the grid.
            Do(run, "option:1", "card:0", "cancel", "cancel");
            c.Equal(before, run.Snapshot().ToJson().ToJsonString(), "the same after a pick that was taken back");
            c.Equal("StrikeIronclad", run.State.Deck[0].Def.ClassName + (run.State.Deck[0].IsUpgraded ? "+" : ""), "nothing was upgraded");

            Do(run, "option:0");
            c.Equal(65, run.Hp, "and Rest can still be chosen");
            c.Sequence(["option:1", "cancel", "option:1", "card:0", "cancel", "cancel", "option:0"], run.Actions, "every step is in the save");
        });

        c.Group("Smith asks even when one card alone can be upgraded", () =>
        {
            var setup = new RunSetup { Deck = [new("StrikeIronclad", true), new("DefendIronclad", true), new("Bash")] };
            var run = Rest(setup);
            Do(run, "option:1");
            var s = run.Snapshot();
            c.Equal("card_select", s.Screen, "the grid opens for the one card: Smith requires the player's own confirmation");
            c.Equal("card:0 cancel", Ids(s), "Bash, the one card of the grid, and the close button");
        });

        c.Group("Smith's card choice: the first card is index 0 of the cards that can be upgraded, not of the deck", () =>
        {
            var setup = new RunSetup { Deck = [new("StrikeIronclad", true), new("DefendIronclad"), new("Bash")] };
            var run = Rest(setup);
            Do(run, "option:1");
            c.Equal("card:0 card:1 cancel", Ids(run.Snapshot()), "Defend and Bash");
            Do(run, "card:0", "confirm");
            c.Equal("StrikeIronclad+ DefendIronclad+ Bash", DeckNames(run), "Defend was upgraded");
        });
    }

    private static void SmithDisabled(Check c)
    {
        c.Group("Smith is greyed out when no card of the deck can be upgraded", () =>
        {
            var setup = new RunSetup { Hp = 41, Deck = [new("StrikeIronclad", true), new("DefendIronclad", true), new("Bash", true)] };
            var run = Rest(setup);
            var s = run.Snapshot();
            c.Equal("""[{"n":0,"id":"HEAL","name":"Rest","text":"Heal 24 HP."},{"n":1,"id":"SMITH","name":"Smith","text":"No card in your deck can be upgraded.","available":false}]""",
                Options(s), "listed with available false");
            c.Equal("option:0", Ids(s), "and not offered");
            var refused = run.Execute("option:1");
            c.That(!refused.Ok, "choosing it is refused");
            c.Equal("Smith is not available right now.", refused.Error, "with the mod's words");
            Do(run, "option:0");
            c.Equal(65, run.Hp, "Rest works as ever");
        });
    }

    // ---------- 5. the hooks of the heal ----------

    private static void HealHooks(Check c)
    {
        c.Group("Regal Pillow: a rest heals 15 more, and the option says so", () =>
        {
            var run = Rest(Hurt(20, 80, () => new RegalPillow()));
            var s = run.Snapshot();
            // 24 + 15 = 39.
            c.Equal("Rest (heal 39)", s.Actions[0].Label, "the label names the heal after the relic");
            c.Equal("Heal 39 HP. / Regal Pillow: 15 more HP.", s.State["options"]![0]!["text"]!.GetValue<string>(), "the relic's line is joined on as the mod joins lines");
            Do(run, "option:0");
            c.Equal(59, run.Hp, "20 + 39");
        });

        c.Group("the amount passes through the relics in pickup order", () =>
        {
            // (24 + 15) x 2 = 78 with the pillow first; 24 x 2 + 15 = 63 with the doubler first.
            var run = Rest(Hurt(1, 80, () => new RegalPillow(), () => new Doubler()));
            c.Equal(78m, RestSite.HealAmount(run), "pillow, then doubler");
            Do(run, "option:0");
            c.Equal(79, run.Hp, "1 + 78");

            run = Rest(Hurt(1, 80, () => new Doubler(), () => new RegalPillow()));
            c.Equal(63m, RestSite.HealAmount(run), "doubler, then pillow");
            Do(run, "option:0");
            c.Equal(64, run.Hp, "1 + 63");
        });

        c.Group("Stone Humidifier: after the heal, 5 Max HP", () =>
        {
            var run = Rest(Hurt(41, 80, () => new StoneHumidifier()));
            c.Equal("Heal 24 HP. / Stone Humidifier: gain 5 Max HP.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "the option's text");
            Do(run, "option:0");
            // The heal first: 41 + 24 = 65. Then max HP 85, which heals by the 5 it rose: 70.
            c.Equal("rested at 65,", run.State.FindRelic<StoneHumidifier>()!.Heard, "the hook comes after the heal and is told the rest is the rest site's own");
            c.Equal((70, 85), (run.Hp, run.MaxHp), "65 of 80, then 70 of 85");
            var s = run.Snapshot();
            c.Equal((70, 85), (s.State["hp"]!.GetValue<int>(), s.State["max_hp"]!.GetValue<int>()), "as the snapshot shows it");
        });

        c.Group("several relics add their lines in pickup order", () =>
        {
            var run = Rest(Hurt(41, 80, () => new StoneHumidifier(), () => new RegalPillow()));
            c.Equal("Heal 39 HP. / Stone Humidifier: gain 5 Max HP. / Regal Pillow: 15 more HP.", run.Snapshot().State["options"]![0]!["text"]!.GetValue<string>(), "two lines");
        });
    }

    private static void HealRewards(Check c)
    {
        c.Group("a rest that brings rewards: they are offered before the option counts as used", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 41, ExtraRelics = [() => new Tip(), () => new Witness()] };
            var run = Rest(setup, world: true);
            int Rewards() => run.State.Part<RunWorld>().Streams.Counter(RunStream.Rewards);
            c.Equal(0, Rewards(), "nothing drawn yet");
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the rewards screen lies over the rest site");
            c.Equal("""[{"n":0,"kind":"gold","amount":7}]""", Text(s.State["rewards"]), "the relic's 7 gold");
            c.Equal("reward:0=Take 7 gold | proceed=Skip (leave without the unclaimed rewards)", Labels(s), "a screen that is no room's last: its proceed is Skip");
            c.Equal(65, run.Hp, "the heal came first");
            // The row was in the list the set was made with: filling it draws its one number, fixed amount or not.
            c.Equal(1, Rewards(), "one number of rewards for the gold row");
            c.Equal("options HEAL+SMITH,entered RestSite,healed,", run.State.FindRelic<Witness>()!.Heard, "nobody was asked yet whether the other options go");

            Do(run, "reward:0");
            s = run.Snapshot();
            c.Equal("rest", s.Screen, "the last row taken closes the rewards and shows the rest site again");
            c.Equal(106, run.Gold, "99 + 7");
            c.Equal("options HEAL+SMITH,entered RestSite,healed,disable?,", run.State.FindRelic<Witness>()!.Heard, "now the option has run to its end");
            c.Equal("""["Rest"]""", Chosen(s), "chosen");
            c.Equal("[]", Options(s), "the options are gone");
            c.Equal("proceed=Proceed", Labels(s), "proceed is on");
            Do(run, "proceed");
            c.Equal(1, ((AtRestSite)run.Director).RoomsLeft, "and leaves the room");
        });

        c.Group("skipping a rest's rewards ends the option just the same", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 41, ExtraRelics = [() => new Tip()] };
            var run = Rest(setup, world: true);
            Do(run, "option:0", "proceed");
            var s = run.Snapshot();
            c.Equal("rest", s.Screen, "Skip closes the rewards");
            c.Equal(99, run.Gold, "the gold was left behind");
            c.Equal("proceed=Proceed", Labels(s), "the visit is used up");
        });

        c.Group("Tiny Mailbox: two potions after a rest; the relic is not told that it changed rewards", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 41, ExtraRelics = [() => new TinyMailbox()] };
            var run = Rest(setup, world: true);
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal("rewards", s.Screen, "the rewards screen");
            c.Sequence(["potion", "potion"], s.State["rewards"]!.AsArray().Select(row => row!["kind"]!.GetValue<string>()), "two potion rows");
            // A random potion is two numbers, its rarity and the pick, and two more for every pick that landed on a potion the arena leaves out.
            var passedOver = run.State.FindPart<LeftOutLog>()?.Entries.Count ?? 0;
            c.Equal(4 + 2 * passedOver, run.State.Part<RunWorld>().Streams.Counter(RunStream.Rewards), "two numbers of rewards for each potion");
            c.Equal("rewards,", run.State.FindRelic<TinyMailbox>()!.Heard, "asked once, for the rest site's own rest; the game drops the list of who added rewards");
            Do(run, "reward:0", "reward:0");
            c.Equal("rest", run.Snapshot().Screen, "both taken: back at the rest site");
            c.Equal(2, run.State.Belt.Slots.Count(potion => potion is not null), "two potions in the belt");
            c.Equal("""["Rest"]""", Chosen(run.Snapshot()), "the visit is used up");
        });
    }

    private static void MimickedRest(Check c)
    {
        c.Group("a rest that an event mimics heals the same and tells the listeners it is mimicked", () =>
        {
            var script = new Dictionary<string, Action<Arena.Run, Desk>>
            {
                ["rest"] = static (run, desk) => desk.Notes.Add(RestSite.ExecuteHeal(run, isMimicked: true) is null ? "no rewards" : "rewards"),
            };
            var run = new Arena.Run(Hurt(30, 80, () => new StoneHumidifier(), () => new RegalPillow()), new DeskDirector(script));
            Do(run, "rest");
            // 30 + (24 + 15) = 69; then max HP 85 and 5 more HP.
            c.Equal("mimicked at 69,", run.State.FindRelic<StoneHumidifier>()!.Heard, "after the heal, with the mimicked mark");
            c.Equal((74, 85), (run.Hp, run.MaxHp), "the same heal as at a rest site");
            c.Sequence(["no rewards"], Notes(run), "no relic added rewards: no screen");
        });
    }

    // ---------- 6. the options ----------

    private static void KeptOptions(Check c)
    {
        c.Group("Miniature Tent: the other options stay, renumbered, and proceed is on beside them", () =>
        {
            var run = Rest(Hurt(41, 80, () => new MiniatureTent()));
            Do(run, "option:0");
            var s = run.Snapshot();
            c.Equal("""[{"n":0,"id":"SMITH","name":"Smith","text":"Upgrade a card in your deck."}]""", Options(s), "Rest is gone and Smith is option 0 now");
            c.Equal("""["Rest"]""", Chosen(s), "chosen");
            c.Equal("option:0=Smith (upgrade a card) | proceed=Proceed (leave the remaining options unused)", Labels(s), "the option, and proceed with the mod's warning");
            c.Equal(1, run.State.FindRelic<MiniatureTent>()!.Asked, "asked once, after the option succeeded");

            Do(run, "option:0", "cancel");
            c.Equal("option:0=Smith (upgrade a card) | proceed=Proceed (leave the remaining options unused)", Labels(run.Snapshot()), "backing out of Smith changes nothing");
            c.Equal("""["Rest"]""", Chosen(run.Snapshot()), "and Smith is not among the chosen");
            c.Equal(1, run.State.FindRelic<MiniatureTent>()!.Asked, "nor is the relic asked for an option that did not succeed");

            Do(run, "option:0", "card:9", "confirm");
            s = run.Snapshot();
            c.Equal("[]", Options(s), "no option is left");
            c.Equal("""["Rest","Smith"]""", Chosen(s), "both, in the order used");
            c.Sequence(["HEAL", "SMITH"], ((RestSiteScreen)run.Top!).ChosenIds, "as the game records them");
            c.Equal("proceed=Proceed", Labels(s), "plain proceed");
            c.Equal(2, run.State.FindRelic<MiniatureTent>()!.Asked, "asked again for the second option");
            c.Equal((65, "Bash+"), (run.Hp, run.State.Deck[9].Title), "healed and upgraded");
        });

        c.Group("Miniature Tent: leaving with an option unused is allowed", () =>
        {
            var run = Rest(Hurt(41, 80, () => new MiniatureTent()));
            Do(run, "option:0", "proceed");
            c.Equal(1, ((AtRestSite)run.Director).RoomsLeft, "the room is finished");
        });
    }

    private static void ChangedOptions(Check c)
    {
        c.Group("Girya adds Lift; an option a relic adds goes behind Rest and Smith", () =>
        {
            var run = Rest(Hurt(41, 80, () => new Girya()));
            var s = run.Snapshot();
            c.Equal("""{"n":2,"id":"LIFT","name":"Lift","text":"Gain 1 Strength at the start of every combat. 3 lifts left."}""", Text(s.State["options"]![2]), "the third option");
            c.Equal("option:0=Rest (heal 24) | option:1=Smith (upgrade a card) | option:2=Lift (Gain 1 Strength at the start of every combat)", Labels(s),
                "another option's label is its title and the first sentence of its text");
            Do(run, "option:2");
            c.Equal(1, run.State.FindRelic<Girya>()!.TimesLifted, "lifted once");
            s = run.Snapshot();
            c.Equal("""["Lift"]""", Chosen(s), "chosen");
            c.Equal("proceed=Proceed", Labels(s), "it used the visit up like any option");
            c.Equal(41, run.Hp, "and healed nothing");
        });

        c.Group("Girya lifted three times adds nothing", () =>
        {
            var run = Rest(Hurt(41, 80, () => new Girya { TimesLifted = 3 }));
            c.Equal("option:0 option:1", Ids(run.Snapshot()), "Rest and Smith alone");
        });

        c.Group("a deck card is asked before the relics (Byrdonis Egg adds Hatch)", () =>
        {
            var setup = new RunSetup { ExtraCards = [(Egg, false)], ExtraRelics = [() => new Witness(), () => new Girya()] };
            var run = Rest(setup);
            var s = run.Snapshot();
            c.Sequence(["HEAL", "SMITH", "HATCH", "LIFT"], s.State["options"]!.AsArray().Select(o => o!["id"]!.GetValue<string>()), "the card's option, then the relic's");
            c.Equal("options HEAL+SMITH+HATCH,entered RestSite,", run.State.FindRelic<Witness>()!.Heard, "the relic between them saw the card's option and not yet the later relic's");
            c.Equal("""{"n":2,"id":"HATCH","name":"Hatch"}""", Text(s.State["options"]![2]), "an option without a text has no text key");
            c.Equal("Hatch", s.Actions[2].Label, "and its label is its title alone");
        });

        c.Group("a listener can take an option away (the modifier Midas takes Smith)", () =>
        {
            var run = Rest(Hurt(41, 80, () => new NoSmith()));
            c.Equal("""[{"n":0,"id":"HEAL","name":"Rest","text":"Heal 24 HP."}]""", Options(run.Snapshot()), "Rest alone");
            c.Equal("option:0", Ids(run.Snapshot()), "one action");
        });

        c.Group("with no option left to offer, proceed is on at once", () =>
        {
            var run = Rest(Hurt(41, 80, () => new NoOptions()));
            var s = run.Snapshot();
            c.Equal("[]", Options(s), "nothing on offer");
            c.Equal("(none)", Chosen(s), "nothing chosen");
            c.Equal("proceed=Proceed", Labels(s), "the game turns proceed on when the list is empty");
            Do(run, "proceed");
            c.Equal(1, ((AtRestSite)run.Director).RoomsLeft, "and the room can be left");
        });

        c.Group("two options are the same option when their ids are, as in the game", () =>
        {
            c.That(new SmithRestSiteOption().Equals(new SmithRestSiteOption()), "Smith is Smith");
            c.That(!new SmithRestSiteOption().Equals(new HealRestSiteOption()), "and is not Rest");
            ICollection<RestSiteOption> options = new List<RestSiteOption> { new HealRestSiteOption(), new SmithRestSiteOption() };
            c.That(options.Contains(new HealRestSiteOption()) && options.Remove(new SmithRestSiteOption()) && options.Count == 1, "a hook can look an option up and take it away by a new object of its kind");
        });
    }

    // ---------- 7. labels and refusals ----------

    private static void ShortForm(Check c)
    {
        c.Group("the short form of an option's text in its label (RestSiteReader.Short)", () =>
        {
            c.Equal("Obtain a random relic", RestSiteScreen.Short("Obtain a random relic."), "one sentence: its full stop goes");
            c.Equal("Gain 1 Strength", RestSiteScreen.Short("Gain 1 Strength. 3 lifts left."), "the first sentence alone");
            var seventy = new string('a', 70);
            c.Equal(seventy, RestSiteScreen.Short(seventy + "."), "70 characters are kept whole");
            var long71 = new string('b', 71);
            c.Equal(new string('b', 67) + "...", RestSiteScreen.Short(long71), "71 are cut to 67 and three dots");
            c.Equal("", RestSiteScreen.Short(""), "nothing stays nothing");
        });
    }

    private static void Refusals(Check c)
    {
        c.Group("what the rest site refuses, in the mod's words; a refusal changes nothing", () =>
        {
            var run = Rest(Hurt(41));
            var before = run.Dump();
            void Refused(string id, string error)
            {
                var result = run.Execute(id);
                c.That(!result.Ok, $"'{id}' is refused");
                c.Equal(error, result.Error, $"the reason for '{id}'");
            }

            Refused("proceed", "The proceed button is not enabled yet.");
            Refused("option:2", "There is no rest site option 2.");
            Refused("option:-1", "There is no rest site option -1.");
            Refused("option:", "Unknown rest site action 'option:'.");
            Refused("option:0>p1", "Unknown rest site action 'option:0>p1'.");
            Refused("open", "Unknown rest site action 'open'.");
            Refused("potion:0", "'potion:0' is not available now.");
            Refused("discard_potion:1", "'discard_potion:1' is not available now.");
            c.Equal(before, run.Dump(), "the run is as it was");
            c.Equal(0, run.Actions.Count, "and nothing went into the save");

            Do(run, "option:0");
            Refused("option:0", "There is no rest site option 0.");
            Refused("option:1", "There is no rest site option 1.");
        });
    }

    private static void PotionsRideAlong(Check c)
    {
        c.Group("potions are used and thrown away from the rest site's own screen", () =>
        {
            var setup = new RunSetup { Hp = 41, Potions = ["BloodPotion", "FirePotion"] };
            var run = Rest(setup);
            c.Equal("option:0 option:1 potion:0 discard_potion:0 discard_potion:1", Ids(run.Snapshot()), "the screen's actions first; a potion that needs an enemy can only be thrown away");
            Do(run, "potion:0");
            // Blood Potion: 20% of 80 is 16.
            c.Equal(57, run.Hp, "41 + 16");
            var s = run.Snapshot();
            c.Equal("rest", s.Screen, "still at the rest site");
            c.Equal("(none)", Chosen(s), "a potion is no rest site option");
            c.Equal("option:0 option:1 discard_potion:1", Ids(s), "both options are still there");
            Do(run, "discard_potion:1", "option:0");
            c.Equal(80, run.Hp, "57 + 24 stops at 80");
            c.Equal("proceed", Ids(run.Snapshot()), "an empty belt adds nothing");
        });
    }

    private static void OptionList(Check c)
    {
        c.Group("every rest site option of the game is built or left out by name", () =>
        {
            // The game's classes under Entities.RestSite, by their OptionId.
            string[] game = ["CLONE", "COOK", "DIG", "HATCH", "HEAL", "KINDLE", "LIFT", "MEND", "SMITH"];
            var named = RestSite.Built.Concat(RestSite.LeftOut.Select(o => o.OptionId)).Order(StringComparer.Ordinal).ToList();
            c.Sequence(game, named, "the nine options, each once");
            c.That(RestSite.LeftOut.All(o => o.Reason.Length > 0), "each one left out has its reason");
            c.Sequence(RestSite.Built, RestSite.GenerateOptions(Rest()).Select(o => o.OptionId), "the built ones are what a rest site offers by itself, in the game's order");
        });
    }

    // ---------- 8. replay and copies ----------

    private static void ReplayAndCopy(Check c)
    {
        c.Group("a visit replays from its save and a copy goes on the same way", () =>
        {
            var setup = new RunSetup { Seed = RealSeed, Hp = 41, Potions = ["BloodPotion"], ExtraRelics = [() => new MiniatureTent(), () => new Tip(), () => new StoneHumidifier()] };
            var director = new AtRestSite { World = true };
            var run = new Arena.Run(setup, director);
            Do(run, "option:1", "card:5", "cancel", "card:9");

            // In the middle of Smith's card choice: a copy answers the same way.
            var copy = run.Clone();
            c.Equal(run.Dump(), copy.Dump(), "a copy in the middle of the card choice is in the same state");
            Do(run, "confirm", "potion:0", "option:0", "reward:0", "proceed");
            Do(copy, "confirm", "potion:0", "option:0", "reward:0", "proceed");
            c.Equal(run.Dump(), copy.Dump(), "and ends in the same state");
            c.Equal(run.Dump(), Arena.Run.Replay(setup, director, run.Actions).Dump(), "the whole visit replays from its action list");

            // Smith first (Bash+), the potion (41 + 16 = 57), Rest (57 + 24 = 81, cut to 80; then 85 max and 5 more HP), the 7 gold.
            c.Equal((85, 85, 106), (run.Hp, run.MaxHp, run.Gold), "the numbers of the visit");
            c.Equal("map", run.Snapshot().Screen, "and the room is left");
        });
    }
}
