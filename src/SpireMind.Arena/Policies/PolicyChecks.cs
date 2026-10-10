using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// The scripted players, checked as the yardsticks they are meant to be: each one finishes
/// any fight the arena can build without an exception, a stall or a potion it should not
/// use; Greedy follows its rules; Lookahead finds lines whose best answer is known; and
/// neither can see what a model cannot (the draw order, the random numbers to come).
/// </summary>
public sealed class PolicyChecks : IArenaChecks
{
    // ---------- cards and potions only these checks use (never registered) ----------

    private static readonly CardDef Drill = new("CheckDrill", "Drill", CardType.Power, CardRarity.Uncommon, 1, TargetType.Self)
    {
        Text = _ => "Gain 1 Strength.",
        Play = c => c.ApplyToSelf<StrengthPower>(1),
    };

    private static readonly CardDef Scout = new("CheckScout", "Scout", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Draw 1 card.",
        Play = c => c.Draw(1),
    };

    private static readonly CardDef Bleed = new("CheckBleed", "Bleed", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
    {
        Text = _ => "Lose 3 HP. Gain 2 energy.",
        Play = c =>
        {
            c.LoseHp(3);
            c.GainEnergy(2);
        },
    };

    private static readonly CardDef Purge = new("CheckPurge", "Purge", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Exhaust 1 card.",
        Play = c =>
        {
            if (c.F.ChooseHandCardToExhaust() is { } card) c.F.Exhaust(card);
        },
    };

    private static readonly CardDef Fetch = new("CheckFetch", "Fetch", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Put a card from your discard pile into your hand.",
        Play = c =>
        {
            if (c.F.ChooseFromPile(PileType.Discard, "Choose a card to put into your hand.", 1, 1).FirstOrDefault() is { } card)
                c.F.MoveCard(card, PileType.Hand);
        },
    };

    private static readonly CardDef Offer = new("CheckOffer", "Offer", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Add 1 of 3 cards to your hand.",
        Play = c =>
        {
            var offered = new[] { c.F.CreateCard("DefendIronclad"), c.F.CreateCard("Bash"), c.F.CreateCard("Wound") };
            if (c.F.ChooseOneOf(offered, canSkip: true) is { } card) c.F.AddGeneratedCard(card, PileType.Hand, PilePosition.Bottom, c.Owner);
        },
    };

    private static readonly CardDef Cull = new("CheckCull", "Cull", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Exhaust any number of cards.",
        Play = c =>
        {
            foreach (var card in c.F.ChooseFromHand("Choose cards to Exhaust.", 0, 10)) c.F.Exhaust(card);
        },
    };

    private static readonly CardDef Sift = new("CheckSift", "Sift", CardType.Skill, CardRarity.Common, 0, TargetType.Self)
    {
        Text = _ => "Put up to 2 cards from your draw pile into your hand.",
        Play = c =>
        {
            foreach (var card in c.F.ChooseFromGrid(c.F.DrawPile.ToList(), "Choose up to 2 cards to put into your hand.", 0, 2))
                c.F.MoveCard(card, PileType.Hand);
        },
    };

    private static readonly CardDef Gamble = new("CheckGamble", "Gamble", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
    {
        Text = _ => "Draw 1 card.",
        Play = c => c.Draw(1),
    };

    private static readonly CardDef Finisher = new("CheckFinisher", "Finisher", CardType.Attack, CardRarity.Rare, 0, TargetType.AnyEnemy)
    {
        Vars = { ["Damage"] = 60 },
        Text = t => $"Deal {t.Damage()} damage.",
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    private static readonly CardDef Scatter = new("CheckScatter", "Scatter", CardType.Attack, CardRarity.Common, 1, TargetType.RandomEnemy)
    {
        Vars = { ["Damage"] = 10 },
        Text = t => $"Deal {t.Damage()} damage to a random enemy.",
        Play = c => c.Attack(c.V("Damage")).Execute(),
    };

    private static readonly PotionDef Fire = new("CheckPolicyFire", "Fire Flask", PotionRarity.Common, TargetType.AnyEnemy)
    {
        Text = _ => "Deal 20 damage.",
        Use = p => p.F.Damage(p.Target!, 20, Props.NonCardUnpowered, p.Owner),
    };

    private static readonly PotionDef Guard = new("CheckPolicyGuard", "Guard Flask", PotionRarity.Common, TargetType.Self)
    {
        Text = _ => "Gain 12 Block.",
        Use = p => p.F.GainBlock(p.Owner, 12, Props.NonCardUnpowered),
    };

    private static readonly PotionDef Mend = new("CheckPolicyMend", "Mend Flask", PotionRarity.Common, TargetType.Self)
    {
        Usage = PotionUsage.AnyTime,
        Text = _ => "Heal 10 HP.",
        Use = p => p.F.Heal(p.Owner, 10),
    };

    private static readonly PotionDef Swig = new("CheckPolicySwig", "Swig Flask", PotionRarity.Common, TargetType.Self)
    {
        Text = _ => "Draw 2 cards.",
        Use = p => p.F.Draw(2),
    };

    private static readonly PotionDef Sampler = new("CheckPolicySampler", "Sampler Flask", PotionRarity.Uncommon, TargetType.Self)
    {
        Text = _ => "Add 1 of 3 cards to your hand, or none.",
        Use = p =>
        {
            var offered = new[] { p.F.CreateCard("DefendIronclad", upgraded: true), p.F.CreateCard("Bash"), p.F.CreateCard("Wound") };
            if (p.F.ChooseOneOf(offered, canSkip: true) is { } card) p.F.AddGeneratedCard(card, PileType.Hand, PilePosition.Bottom, p.Owner);
        },
    };

    private static readonly GreedyPolicy Greedy = new();
    private static readonly LookaheadPolicy Lookahead = new();

    public void Run(Check c)
    {
        Listed(c);
        GreedyRules(c);
        GreedyChoices(c);
        LookaheadLines(c);
        LookaheadPotions(c);
        LookaheadPotionsThatDraw(c);
        NothingHidden(c);
        WholeFights(c);
        EverythingRegistered(c);
    }

    // ---------- driving a player ----------

    private static string Pick(IPolicy player, GameSnapshot snapshot, Fight f) =>
        player is IEnginePolicy searching ? searching.Choose(snapshot, f) : player.Choose(snapshot);

    /// <summary>Lets <paramref name="player"/> play until it ends the turn or the fight is over,
    /// and returns the labels of what it did, in order.</summary>
    private static List<string> Turn(Fight f, IPolicy player, int most = 40)
    {
        var labels = new List<string>();
        for (var n = 0; n < most && !f.IsOver; n++)
        {
            var snapshot = f.Snapshot();
            var id = Pick(player, snapshot, f);
            var action = snapshot.Actions.FirstOrDefault(a => a.Id == id)
                         ?? throw new InvalidOperationException($"{player.Name} chose '{id}', which is not offered");
            labels.Add(action.Label);
            f.Do(id);
            if (id == "end_turn") break;
        }
        return labels;
    }

    /// <summary>How a whole fight went: every action id, and what stopped it if it did not end.</summary>
    private sealed record Played(List<string> Ids, string? Problem);

    /// <summary>Plays the fight to its end. A refused action, an exception, a turn of more than
    /// <paramref name="mostPerTurn"/> actions or a fight that does not end is the problem reported.</summary>
    private static Played PlayOut(Fight f, IPolicy player, int mostPerTurn = 150)
    {
        var ids = new List<string>();
        var inTurn = 0;
        for (var n = 0; n < 6000 && !f.IsOver; n++)
        {
            string id;
            try
            {
                var snapshot = f.Snapshot();
                id = Pick(player, snapshot, f);
                if (snapshot.Actions.All(a => a.Id != id)) return new Played(ids, $"chose '{id}', which is not offered on turn {f.Turn}");
            }
            catch (Exception ex)
            {
                return new Played(ids, $"threw {ex.GetType().Name} while choosing on turn {f.Turn}: {ex.Message}");
            }

            ids.Add(id);
            try
            {
                var result = f.Execute(id);
                if (!result.Ok) return new Played(ids, $"'{id}' was refused on turn {f.Turn}: {result.Error}");
            }
            catch (Exception ex)
            {
                return new Played(ids, $"the fight threw {ex.GetType().Name} on '{id}' (turn {f.Turn}, a content fault, not the player's): {ex.Message}");
            }

            inTurn = id == "end_turn" ? 0 : inTurn + 1;
            if (inTurn > mostPerTurn) return new Played(ids, $"took more than {mostPerTurn} actions in turn {f.Turn} without ending it");
        }
        return new Played(ids, f.IsOver ? null : "the fight did not end");
    }

    /// <summary>A fight against dummies with the starter deck in the kit's order, and then exactly this hand.</summary>
    private static Fight With(object[] hand, params CheckDummy[] dummies)
    {
        var f = CheckKit.VsDummies(dummies);
        f.Clear(PileType.Hand);
        foreach (var card in hand)
        {
            if (card is CardDef def) f.Put(def, PileType.Hand);
            else f.Put((string)card, PileType.Hand);
        }
        return f;
    }

    private static CheckDummy Hitter(int hp, int damage, string name = "Dummy") =>
        new() { Hp = hp, DisplayName = name, Steps = [new DummyStep("HIT", Damage: damage)] };

    private static CheckDummy Idle(int hp, string name = "Dummy") => new() { Hp = hp, DisplayName = name };

    // ---------- the list ----------

    private static void Listed(Check c)
    {
        c.Group("the players are listed weakest first", () =>
        {
            c.Sequence(["Random", "Greedy", "Lookahead"], Policies.All.Take(3).Select(p => p.Name), "Policies.All");
            c.That(Policies.All.First(p => p.Name == "Greedy") is not IEnginePolicy, "Greedy gets the snapshot only");
            c.That(Policies.All.First(p => p.Name == "Lookahead") is IEnginePolicy, "Lookahead gets the fight to copy");
        });
    }

    // ---------- Greedy ----------

    private static void GreedyRules(Check c)
    {
        c.Group("Greedy: a kill comes before Block", () =>
        {
            // Hand: Strike, Strike, Defend, Defend, Bash. Small has 6 HP and is about to hit for 20.
            var f = CheckKit.VsDummies(Idle(30, "Big"), Hitter(6, 20, "Small"));
            c.Sequence(["Play Strike on Small", "Play Strike on Big", "End turn"], Turn(f, Greedy), "the turn");
            c.Equal(80, f.Player.Hp, "nothing hit");
        });

        c.Group("Greedy: a kill over several cards counts the target's Vulnerable", () =>
        {
            var f = CheckKit.VsDummy(Hitter(18, 20));
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 2, f.Player);
            // Two Strikes at 9 each are exactly 18.
            c.Sequence(["Play Strike on Dummy", "Play Strike on Dummy"], Turn(f, Greedy), "the turn");
            c.That(f.Won, "the fight is won on turn 1");
        });

        c.Group("Greedy: blocks while more is coming than is blocked, then hits", () =>
        {
            var f = CheckKit.VsDummy(Hitter(100, 11));
            c.Sequence(["Play Defend", "Play Defend", "Play Strike on Dummy", "End turn"], Turn(f, Greedy), "the turn");
            c.Equal(79, f.Player.Hp, "10 of 11 blocked");
            c.Equal(94, f.Enemy(0).Hp, "one Strike landed");
        });

        c.Group("Greedy: the most damage per energy at the weakest enemy, and no Block nobody asked for", () =>
        {
            var f = CheckKit.VsDummies(Idle(50, "Big"), Idle(40, "Small"));
            // Strike is 6 per energy, Bash 4; with 1 energy left Bash cannot be paid and Defend has no use.
            c.Sequence(["Play Strike on Small", "Play Strike on Small", "End turn"], Turn(f, Greedy), "the turn");
        });

        c.Group("Greedy: what is left goes into powers, then free cards; never a card that costs HP", () =>
        {
            var f = With(["DefendIronclad", Scout, Bleed, Drill, "StrikeIronclad"], Idle(100));
            var turn = Turn(f, Greedy);
            // Scout draws the Strike on top of the draw pile, which the last energy pays for (7 with 1 Strength).
            c.Sequence(["Play Strike on Dummy", "Play Drill", "Play Scout", "Play Strike on Dummy", "End turn"], turn, "the turn");
            c.Equal(87, f.Enemy(0).Hp, "6 and 7 damage");
            c.Equal(80, f.Player.Hp, "no HP spent");
        });

        c.Group("Greedy: reads an attack that hits several times, all enemies, or a random one", () =>
        {
            var twice = new CardDef("CheckTwice2", "Pair", CardType.Attack, CardRarity.Common, 1, TargetType.AnyEnemy)
            {
                Vars = { ["Damage"] = 4 }, Text = t => $"Deal {t.Damage()} damage twice.", Play = p => p.Attack(p.V("Damage")).Hits(2).Execute(),
            };
            var sweep = new CardDef("CheckSweep", "Sweep", CardType.Attack, CardRarity.Common, 1, TargetType.AllEnemies)
            {
                Vars = { ["Damage"] = 7 }, Text = t => $"Deal {t.Damage()} damage to ALL enemies.", Play = p => p.Attack(p.V("Damage")).Execute(),
            };
            // Pair is 8 for 1 energy, Sweep 7, Strike 6: all three kill the 21 HP enemy between them.
            var f = With(["StrikeIronclad", twice, sweep, "DefendIronclad"], Hitter(21, 30, "Near"), Idle(60, "Far"));
            c.Sequence(["Play Pair on Near", "Play Sweep", "Play Strike on Near", "End turn"], Turn(f, Greedy), "the kill, best card first");
            c.That(f.Enemy(0).IsDead, "Near is dead");

            // Scatter may hit either enemy, so it is no part of a kill, and is played only when nothing can be aimed.
            f = With([Scatter, "StrikeIronclad"], Idle(10, "Near"), Idle(60, "Far"));
            c.Sequence(["Play Strike on Near", "Play Scatter", "End turn"], Turn(f, Greedy), "a random target is the last resort");
        });

        c.Group("Greedy: takes a card's damage against an enemy from the snapshot's preview where there is one", () =>
        {
            // One Strike, one energy. A has 9 HP and is Vulnerable, B has 8 HP.
            GameSnapshot Shown(JsonObject? vs)
            {
                var strike = new JsonObject { ["i"] = 0, ["name"] = "Strike", ["cost"] = "1", ["type"] = "Attack", ["text"] = "Deal 6 damage.", ["playable"] = true };
                if (vs is not null) strike["vs"] = vs;
                return new GameSnapshot
                {
                    Screen = "combat", AwaitingDecision = true,
                    Actions = [new("play:0>e0", "Play Strike on A"), new("play:0>e1", "Play Strike on B"), new("end_turn", "End turn")],
                    State = new JsonObject
                    {
                        ["energy"] = 1, ["me"] = new JsonObject { ["hp"] = 50 }, ["incoming_damage"] = 0,
                        ["enemies"] = new JsonArray(
                            new JsonObject { ["i"] = 0, ["hp"] = 9, ["powers"] = new JsonArray(new JsonObject { ["name"] = "Vulnerable", ["amount"] = 1 }) },
                            new JsonObject { ["i"] = 1, ["hp"] = 8 }),
                        ["hand"] = new JsonArray(strike),
                    },
                };
            }

            c.Equal("play:0>e0", Greedy.Choose(Shown(new JsonObject { ["e0"] = "Deal 9 damage." })), "the preview says 9 against A: a kill");
            c.Equal("play:0>e0", Greedy.Choose(Shown(null)), "no preview at all: Vulnerable is worked out, 9 again");
            c.Equal("play:0>e1", Greedy.Choose(Shown(new JsonObject { ["e1"] = "Deal 4 damage." })), "previews, but none against A: 6 as at rest, no kill, so the weakest enemy");
        });

        c.Group("Greedy: potions wait in an ordinary fight until it is hurt or about to die, and are never thrown away", () =>
        {
            var f = CheckKit.VsDummy(Hitter(300, 30));
            f.SetPotion(0, Fire);
            f.SetPotion(1, Guard);
            var first = Turn(f, Greedy);
            c.That(first.All(label => !label.Contains("Flask", StringComparison.Ordinal)), $"turn 1, at full HP with 30 coming: no potion ({string.Join("; ", first)})");
            c.That(f.Potions[0] is not null && f.Potions[1] is not null, "both potions are still in the belt");
            var played = PlayOut(f, Greedy);
            c.Equal(null, played.Problem, "the fight ran to its end");
            c.That(f.IsOver && !f.Won, "the fight is lost all the same");
            c.That(f.Potions[0] is null && f.Potions[1] is null, "both potions were used on the way down");
            c.That(played.Ids.All(id => !id.StartsWith("discard_potion:", StringComparison.Ordinal)), "none was thrown away");

            // At half HP or less it uses them at once, the one that needs an enemy on the toughest.
            var hurt = CheckKit.VsDummies(Idle(20, "Small"), Idle(90, "Big"));
            hurt.SetHp(hurt.Player, 40);
            hurt.SetPotion(0, Fire);
            hurt.SetPotion(2, Guard);
            var turn = Turn(hurt, Greedy);
            c.Sequence(["Use Fire Flask on Big", "Use Guard Flask"], turn.Take(2), "at 40 of 80 HP: both potions first, the Fire Flask at the big one");

            // What is coming would kill it: the same, whatever its HP is of its maximum.
            var deadly = CheckKit.VsDummy(Hitter(300, 90));
            deadly.SetPotion(1, Guard);
            c.Equal("Use Guard Flask", Turn(deadly, Greedy)[0], "90 coming at 80 HP: the potion first");
        });

        c.Group("Greedy: in an elite or boss fight every potion goes at once, except a healing one at good HP", () =>
        {
            Fight Make(RoomType room, int hp)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(Hitter(300, 6)).Room(room).Start();
                f.SetHp(f.Player, hp);
                f.SetPotion(0, Mend);
                f.SetPotion(1, Fire);
                f.SetPotion(2, Guard);
                return f;
            }

            foreach (var room in new[] { RoomType.Elite, RoomType.Boss })
            {
                var f = Make(room, 80);
                var turn = Turn(f, Greedy);
                c.Sequence(["Use Fire Flask on Dummy", "Use Guard Flask"], turn.Take(2), $"{room}: the two potions open the turn");
                c.That(f.Potions[0] is not null, $"{room}: the healing potion is kept at full HP");
            }
            var ordinary = Make(RoomType.Monster, 80);
            c.That(Turn(ordinary, Greedy).All(label => !label.Contains("Flask", StringComparison.Ordinal)), "an ordinary fight at full HP: none is used");
            var low = Make(RoomType.Elite, 40);
            c.Equal("Use Mend Flask", Turn(low, Greedy)[0], "at half HP the healing potion goes first");
        });

        c.Group("Greedy: any screen gets a legal answer", () =>
        {
            LegalAction[] odd = [new("potion:0", "Use something"), new("card:0", "Deselect Bash"), new("card:1", "Select Strike")];
            c.Equal("card:1", Greedy.Choose(new GameSnapshot { Screen = "unknown", AwaitingDecision = true, Actions = odd }), "an unknown screen: no potion, nothing taken back");
            c.Equal("end_turn", Greedy.Choose(new GameSnapshot { Screen = "combat", AwaitingDecision = false }), "no actions at all");
            var broken = new GameSnapshot
            {
                Screen = "combat", AwaitingDecision = true, Actions = [new("play:0", "Play ?"), new("end_turn", "End turn")],
                State = new JsonObject { ["hand"] = "not a list", ["enemies"] = new JsonArray(1, "x"), ["energy"] = "three" },
            };
            c.Equal("end_turn", Greedy.Choose(broken), "a snapshot it cannot read");
            var over = CheckKit.VsDummy(Hitter(100, 200));
            over.EndTurn();
            c.Equal(Fight.DoneAction, Greedy.Choose(over.Snapshot()), "after the fight");
            c.Equal(Fight.DoneAction, Lookahead.Choose(over.Snapshot(), over), "Lookahead after the fight");
        });
    }

    private static void GreedyChoices(Check c)
    {
        c.Group("Greedy: an upgrade goes to the most valuable card", () =>
        {
            var f = With(["Armaments", "StrikeIronclad", "Bash", "DefendIronclad"], Hitter(100, 10));
            var turn = Turn(f, Greedy);
            c.Sequence(["Play Armaments", "Select Bash", "Confirm selection (1 chosen)"], turn.Take(3), "the choice");
            c.That(f.AllCards.Any(card => card.Title == "Bash+"), "Bash is upgraded");
            c.Equal("End turn", turn[^1], "the turn ends");
        });

        c.Group("Greedy: what is given up is the least valuable card", () =>
        {
            var f = With([Purge, "StrikeIronclad", "Wound", "DefendIronclad"], Idle(100));
            var turn = Turn(f, Greedy);
            c.Sequence(["Play Strike on Dummy", "Play Purge", "Select Wound", "Confirm selection (1 chosen)", "End turn"], turn, "the turn");
            c.Sequence(["Wound"], f.ExhaustPile.Select(card => card.Title), "the exhaust pile");
        });

        c.Group("Greedy: from a pile it takes the most valuable card", () =>
        {
            var f = With([Fetch], Idle(100));
            f.Put("Wound", PileType.Discard);
            f.Put("Bash", PileType.Discard);
            f.Put("DefendIronclad", PileType.Discard);
            var turn = Turn(f, Greedy);
            c.Sequence(["Play Fetch", "Select Bash", "Play Bash on Dummy", "End turn"], turn, "the turn");
        });

        c.Group("Greedy: of three offered cards it takes the most valuable and does not skip", () =>
        {
            var f = With([Offer], Idle(100));
            c.Sequence(["Play Offer", "Take Bash", "Play Bash on Dummy", "End turn"], Turn(f, Greedy), "the turn");
        });

        c.Group("Greedy: asked for any number to give up, it gives up the junk and nothing else", () =>
        {
            var f = With([Cull, "Wound", "DefendIronclad", "Burn", "DefendIronclad"], Idle(100));
            var turn = Turn(f, Greedy);
            c.Equal("End turn", turn[^1], "the turn ends");
            c.Sequence(["Burn", "Wound"], f.ExhaustPile.Select(card => card.Title).Order(StringComparer.Ordinal), "the exhaust pile");
            c.Equal(2, f.DiscardPile.Count(card => card.Title == "Defend"), "both Defends were kept (and flushed at the end of the turn)");
        });

        c.Group("Greedy: on a grid with a confirm button it picks up to the maximum and never takes a pick back", () =>
        {
            // Draw pile: Strike, Strike, Strike, Defend, Defend. Two Strikes are the best two.
            var f = With([Sift], Idle(100));
            var turn = Turn(f, Greedy);
            c.That(turn.All(label => !label.StartsWith("Deselect", StringComparison.Ordinal)), "nothing was deselected");
            c.Sequence(["Play Sift", "Select Strike", "Select Strike", "Confirm selection: Strike, Strike"], turn.Take(4), "the choice");
            c.Equal("End turn", turn[^1], "the turn ends");
        });

        c.Group("Lookahead: gets through every kind of card choice", () =>
        {
            foreach (var card in new[] { Purge, Fetch, Offer, Cull, Sift })
            {
                var f = With([card, "StrikeIronclad", "Wound", "DefendIronclad", "Armaments", "Bash"], Hitter(100, 9));
                f.Put("Bash", PileType.Discard);
                f.Put("Wound", PileType.Discard);
                var first = Turn(f, Lookahead, most: 60);
                // Each of them is free and has something to offer here (a Wound to lose, a Bash to fetch), so it is played.
                c.That(first.Contains("Play " + card.Name), $"{card.Name} is played: {string.Join("; ", first)}");
                c.That(first.All(label => !label.StartsWith("Deselect", StringComparison.Ordinal)), $"{card.Name}: nothing was deselected");
                c.Equal("End turn", first[^1], $"{card.Name}: the first turn ends");
                c.Equal("End turn", Turn(f, Lookahead, most: 60)[^1], $"{card.Name}: the second turn ends");
            }
        });
    }

    // ---------- Lookahead ----------

    private static void LookaheadLines(Check c)
    {
        c.Group("Lookahead: the lethal line that needs its cards in the right order", () =>
        {
            // 17 HP: only Bash (8, then Vulnerable) followed by Strike (9) gets there with 3 energy.
            var f = With(["DefendIronclad", "StrikeIronclad", "DefendIronclad", "Bash", "DefendIronclad"], Hitter(17, 25));
            c.Sequence(["Play Bash on Dummy", "Play Strike on Dummy"], Turn(f, Lookahead), "the turn");
            c.That(f.Won, "the fight is won on turn 1");
        });

        c.Group("Lookahead: the lethal line that runs through a card choice", () =>
        {
            // 9 HP, 2 energy, and 40 coming at 30 HP: Armaments must upgrade the Strike, not the Defend.
            var f = With(["DefendIronclad", "Armaments", "StrikeIronclad"], Hitter(9, 40));
            f.SetEnergy(2);
            f.SetHp(f.Player, 30);
            c.Sequence(["Play Armaments", "Select Strike", "Confirm selection (1 chosen)", "Play Strike+ on Dummy"], Turn(f, Lookahead), "the turn");
            c.That(f.Won, "the fight is won");
        });

        c.Group("Lookahead: the best Block: all that is coming and not a point more", () =>
        {
            var f = With(["StrikeIronclad", "DefendIronclad", "DefendIronclad", "StrikeIronclad", "DefendIronclad"], Hitter(100, 10));
            f.SetHp(f.Player, 12);
            var turn = Turn(f, Lookahead);
            c.Sequence(["End turn", "Play Defend", "Play Defend", "Play Strike on Dummy"], turn.Order(StringComparer.Ordinal), "two Defends and a Strike");
            c.Equal(12, f.Player.Hp, "nothing got through");
            c.Equal(94, f.Enemy(0).Hp, "the third energy went into a Strike");
        });

        c.Group("Lookahead: blocks what it must to stay alive", () =>
        {
            var f = With(["StrikeIronclad", "DefendIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"], Hitter(100, 14));
            f.SetHp(f.Player, 9);
            Turn(f, Lookahead);
            c.That(!f.IsOver, "it lives");
            c.Equal(5, f.Player.Hp, "10 of 14 blocked");
            c.Equal(94, f.Enemy(0).Hp, "and a Strike with the energy left");
        });

        c.Group("Lookahead: with nothing coming it puts everything into damage, Vulnerable first", () =>
        {
            // Hand: Strike, Strike, Defend, Defend, Bash. Bash then Strike is 8 + 9; three energy allow no more.
            var f = CheckKit.VsDummy(Idle(100));
            c.Sequence(["Play Bash on Dummy", "Play Strike on Dummy", "End turn"], Turn(f, Lookahead), "the turn");
        });

        c.Group("Lookahead: kills the enemy whose blow it would otherwise take", () =>
        {
            var f = CheckKit.VsDummies(Idle(60, "Wall"), Hitter(12, 20, "Brute"));
            var turn = Turn(f, Lookahead);
            c.That(f.Enemy(1).IsDead, "Brute is dead");
            c.Equal(80, f.Player.Hp, "nothing hit");
            c.Equal(2, turn.Count(label => label == "Play Strike on Brute"), "two Strikes did it");
        });

        c.Group("Lookahead: leaves the running fight alone, and answers the same twice", () =>
        {
            var f = With([Gamble, "StrikeIronclad", "DefendIronclad", "Bash"], Hitter(60, 9, "Left"), Hitter(50, 7, "Right"));
            var seen = f.Seen();
            var rolls = f.Rng.Shuffle.Counter + f.Rng.CombatTargets.Counter + f.Rng.MonsterAi.Counter;
            var order = f.DrawPile.Select(card => card.Id).ToList();
            var first = Lookahead.Choose(f.Snapshot(), f);
            c.Equal(seen, f.Seen(), "the snapshot is unchanged");
            c.Equal(rolls, f.Rng.Shuffle.Counter + f.Rng.CombatTargets.Counter + f.Rng.MonsterAi.Counter, "no random number was used");
            c.Sequence(order, f.DrawPile.Select(card => card.Id), "the draw pile is as it was");
            c.Equal(first, Lookahead.Choose(f.Snapshot(), f), "the same answer again");
            c.Equal(first, new LookaheadPolicy().Choose(f.Snapshot(), f), "and from another instance");
        });

        c.Group("Lookahead: a tiny budget still ends every turn", () =>
        {
            var cramped = new LookaheadPolicy(seed: 1, budget: 1, worlds: 1);
            var f = With([Purge, "Armaments", "StrikeIronclad", "Bash", "DefendIronclad", Scout], Hitter(100, 12));
            c.Equal("End turn", Turn(f, cramped, most: 60)[^1], "turn 1 ends");
            c.Equal(null, PlayOut(f, cramped).Problem, "and the fight runs to its end");
        });
    }

    private static void LookaheadPotions(Check c)
    {
        c.Group("Lookahead: keeps its potions in an ordinary fight where they neither save it nor win", () =>
        {
            var f = CheckKit.VsDummy(Hitter(100, 40));
            f.SetHp(f.Player, 60);
            f.SetPotion(0, Fire);
            f.SetPotion(1, Guard);
            var turn = Turn(f, Lookahead);
            c.That(turn.All(label => !label.Contains("Flask", StringComparison.Ordinal)), "no potion in the turn");
            c.That(f.Potions[0] is not null && f.Potions[1] is not null, "both potions are still in the belt");
            c.That(!f.IsOver, "and it is alive without them");
        });

        c.Group("Lookahead: uses a potion that wins the fight this turn", () =>
        {
            // 37 HP: the flask's 20, Bash's 8 and a Strike's 9 on the Vulnerable enemy are exactly enough.
            var f = CheckKit.VsDummy(Hitter(37, 10));
            f.SetPotion(0, Fire);
            f.SetPotion(1, Guard);
            Turn(f, Lookahead);
            c.That(f.Won, "the fight is won on turn 1");
            c.That(f.Potions[0] is null, "the Fire Flask was used");
            c.That(f.Potions[1] is not null, "the Guard Flask was not");
        });

        c.Group("Lookahead: uses a potion that keeps it alive", () =>
        {
            var f = With(["StrikeIronclad", "DefendIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad"], Hitter(100, 22));
            f.SetHp(f.Player, 10);
            f.SetPotion(1, Guard);
            Turn(f, Lookahead);
            c.That(!f.IsOver, "it lives");
            c.Equal(10, f.Player.Hp, "12 and two Defends blocked all 22");
            c.That(f.Potions[1] is null, "the Guard Flask was used");
        });

        c.Group("Lookahead: in an elite fight a potion goes where it saves a lot of HP, and only there", () =>
        {
            Fight Elite(int damage)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(Hitter(100, damage)).Room(RoomType.Elite).Start();
                f.SetHp(f.Player, 60);
                f.SetPotion(0, Guard);
                return f;
            }

            var hard = Elite(40);
            Turn(hard, Lookahead);
            c.That(hard.Potions[0] is null, "40 coming: the Guard Flask was used");
            c.That(hard.Player.Hp >= 32, $"its 12 Block counted ({hard.Player.Hp} HP left of 60)");

            var soft = Elite(6);
            Turn(soft, Lookahead);
            c.That(soft.Potions[0] is not null, "6 coming: the Guard Flask was kept");
        });
    }

    private static void LookaheadPotionsThatDraw(Check c)
    {
        c.Group("Lookahead: a potion that draws or asks for a card is weighed like any other", () =>
        {
            Fight Make(RoomType room, int damage)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(Hitter(100, damage)).Room(room).Start();
                f.SetHp(f.Player, 50);
                f.SetPotion(0, Swig);
                f.SetPotion(1, Sampler);
                return f;
            }

            // An ordinary fight that is neither won nor lost this turn: both stay in the belt.
            var ordinary = Make(RoomType.Monster, 12);
            var turn = Turn(ordinary, Lookahead);
            c.Equal("End turn", turn[^1], "the ordinary turn ends");
            c.That(ordinary.Potions[0] is not null && ordinary.Potions[1] is not null, "ordinary fight: both potions are kept");

            // An elite hitting for 45 at 50 HP: whatever it does with them, it does the same twice and ends the turn.
            var first = Turn(Make(RoomType.Elite, 45), Lookahead, most: 60);
            c.Equal("End turn", first[^1], "the elite turn ends");
            c.Sequence(first, Turn(Make(RoomType.Elite, 45), Lookahead, most: 60), "the elite turn, played again");
            c.That(first.All(label => !label.StartsWith("Discard", StringComparison.Ordinal)), "no potion is thrown away");

            // 24 coming at 10 HP: two Defends are 10, and only the Defend+ (8) out of the Sampler with them keeps it alive.
            var deadly = Make(RoomType.Monster, 24);
            deadly.SetHp(deadly.Player, 10);
            deadly.SetPotion(0, (string?)null);
            var saved = Turn(deadly, Lookahead, most: 60);
            c.That(!deadly.IsOver, $"it lives through 24 at 10 HP ({string.Join("; ", saved)})");
            c.Equal(4, deadly.Player.Hp, "18 of 24 blocked");
            c.That(saved.Contains("Use Sampler Flask") && saved.Contains("Take Defend+"), "by taking the Defend+ out of the Sampler Flask");
        });
    }

    // ---------- what the players cannot see ----------

    private static void NothingHidden(Check c)
    {
        c.Group("Lookahead: the real draw order does not reach its choice", () =>
        {
            // One energy: Gamble (draw 1) or Defend. In fight A the card on top really is the
            // Finisher, which wins at once; in fight B it is at the bottom. Both show the same.
            var a = new FightBuilder()
                .DrawOrder("DefendIronclad", "DefendIronclad", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound", "Wound")
                .Enemy(Hitter(50, 12)).Start();
            a.Put(Gamble, PileType.Hand);
            a.Put(Finisher, PileType.Draw, position: PilePosition.Top);
            a.SetEnergy(1);
            a.SetHp(a.Player, 30);
            var b = a.Clone();
            b.ReverseDrawPile();

            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            c.That(a.Clone().Play("Gamble").Play("Finisher", 0).Won, "in A, Gamble really draws the Finisher and wins");
            c.That(b.Clone().Play("Gamble").HandTitles().All(title => title != "Finisher"), "in B, Gamble really draws a Wound");

            var gambles = 0;
            for (var seed = 1; seed <= 16; seed++)
            {
                var player = new LookaheadPolicy(seed);
                var inA = player.Choose(a.Snapshot(), a);
                c.Equal(inA, player.Choose(b.Snapshot(), b), $"reshuffle seed {seed}: the same choice in both fights");
                if (a.Snapshot().Actions.First(x => x.Id == inA).Label == "Play Gamble") gambles++;
            }
            // One who saw the order would gamble every time in A. Its own reshuffles put the Finisher on top now and then.
            c.That(gambles < 16, $"in A it does not gamble every time ({gambles} of 16)");
        });

        c.Group("Lookahead: the random numbers to come do not reach its choice", () =>
        {
            // Scatter hits one of two enemies at random; only a hit on the Killer (10 HP) stops its 12 damage.
            Fight Make(int seed)
            {
                var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(Hitter(10, 12, "Killer")).Enemy(Idle(100, "Bystander")).Seed(seed).Start();
                f.Clear(PileType.Hand);
                f.Put(Scatter, PileType.Hand);
                f.Put("DefendIronclad", PileType.Hand);
                f.Put("StrikeIronclad", PileType.Hand);
                f.SetEnergy(1);
                f.SetHp(f.Player, 30);
                return f;
            }
            bool Kills(int seed) => Make(seed).Play("Scatter").Enemy(0).IsDead;

            var hit = Enumerable.Range(1, 40).FirstOrDefault(Kills);
            var miss = Enumerable.Range(1, 40).FirstOrDefault(seed => !Kills(seed));
            c.That(hit > 0 && miss > 0, "one seed's Scatter really kills the Killer and another's really misses it");
            if (hit == 0 || miss == 0) return;

            var a = Make(hit);
            var b = Make(miss);
            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            for (var seed = 1; seed <= 8; seed++)
            {
                var player = new LookaheadPolicy(seed);
                c.Equal(player.Choose(a.Snapshot(), a), player.Choose(b.Snapshot(), b), $"reshuffle seed {seed}: the same choice in both fights");
            }
        });

        c.Group("Greedy: the same snapshot, the same answer", () =>
        {
            var a = new FightBuilder().Deck(CheckKit.StarterInOrder).Encounter("NibbitsNormal").Seed(5).Start();
            var shown = a.Snapshot();
            var copy = new GameSnapshot
            {
                Screen = shown.Screen, AwaitingDecision = true, Actions = shown.Actions.ToList(),
                State = (JsonObject)JsonNode.Parse(shown.State.ToJsonString())!,
            };
            c.Equal(Greedy.Choose(shown), Greedy.Choose(copy), "from the snapshot's JSON alone");
            c.Equal(Greedy.Choose(shown), new LookaheadPolicy().Choose(copy), "Lookahead without a fight decides as Greedy");
        });
    }

    // ---------- whole fights ----------

    private static void WholeFights(Check c)
    {
        c.Group("generated scenarios: both players finish them, and the same way twice", () =>
        {
            foreach (var scenario in Scenarios.Generate(4, 20261010))
            {
                foreach (var player in new IPolicy[] { Greedy, Lookahead })
                {
                    var row = Baseline.Play(scenario, player);
                    c.Equal(row, Baseline.Play(scenario, player), $"{player.Name} on {scenario.Id}: the replay");
                    c.That(row.Turns is >= 1 and <= Tuning.MaxTurns, $"{player.Name} on {scenario.Id}: {row.Turns} turns");
                }
            }
        });

        c.Group("two Nibbits with the starter deck: Lookahead keeps more HP than Greedy, Greedy more than Random", () =>
        {
            var hp = new Dictionary<string, int>();
            foreach (var player in new IPolicy[] { new RandomPolicy(), Greedy, Lookahead })
            {
                var total = 0;
                for (var seed = 1; seed <= 12; seed++)
                {
                    var f = new FightBuilder().Encounter("NibbitsNormal").Seed(seed).Start();
                    var played = PlayOut(f, player);
                    c.Equal(null, played.Problem, $"{player.Name}, seed {seed}");
                    if (f.Won) total += f.Player.Hp;
                }
                hp[player.Name] = total;
            }
            c.That(hp["Lookahead"] > hp["Greedy"], $"Lookahead {hp["Lookahead"]} HP over 12 fights, Greedy {hp["Greedy"]}");
            c.That(hp["Greedy"] > hp["Random"], $"Greedy {hp["Greedy"]} HP over 12 fights, Random {hp["Random"]}");
        });
    }

    /// <summary>
    /// Whatever content is registered when this runs: every card is in the deck of some fight,
    /// every encounter is fought, every potion sits in a belt. Greedy plays all of them twice;
    /// Lookahead, with a small budget, every third.
    /// </summary>
    private static void EverythingRegistered(Check c)
    {
        var registry = Content.Registry;
        var cards = registry.Cards.Keys.Order(StringComparer.Ordinal).ToList();
        var encounters = registry.EncounterNames.Order(StringComparer.Ordinal).ToList();
        var potions = registry.Potions.Keys.Order(StringComparer.Ordinal).ToList();
        const int extra = 8;
        var fights = Math.Max(encounters.Count, (cards.Count + extra - 1) / extra);
        var quick = new LookaheadPolicy(seed: 1, budget: 30, worlds: 2);

        Fight Build(int i)
        {
            var builder = new FightBuilder().Encounter(encounters[i % encounters.Count]).Seed(1000 + i);
            for (var k = 0; k < extra; k++) builder.Add(cards[(i * extra + k) % cards.Count]);
            if (potions.Count > 0)
                builder.Potions(potions[i * 3 % potions.Count], potions[(i * 3 + 1) % potions.Count], potions[(i * 3 + 2) % potions.Count]);
            return builder.Start();
        }

        c.Group($"every registered card, encounter and potion ({cards.Count}, {encounters.Count}, {potions.Count}) in {fights} fights", () =>
        {
            c.That(encounters.Count > 0 && cards.Count > 0, "there is content to play");
            for (var i = 0; i < fights; i++)
            {
                var what = $"fight {i} ({encounters[i % encounters.Count]} with {string.Join(", ", Enumerable.Range(0, extra).Select(k => cards[(i * extra + k) % cards.Count]))})";
                Fight f;
                try
                {
                    f = Build(i);
                }
                catch (Exception ex)
                {
                    c.That(false, $"{what} could not be started: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                var greedy = PlayOut(f, Greedy);
                c.Equal(null, greedy.Problem, $"Greedy, {what}");
                c.That(greedy.Ids.All(id => !id.StartsWith("discard_potion:", StringComparison.Ordinal)), $"Greedy, {what}: no potion thrown away");
                c.Sequence(greedy.Ids, PlayOut(Build(i), Greedy).Ids, $"Greedy, {what}: the replay");

                if (i % 3 != 0) continue;
                var searched = PlayOut(Build(i), quick);
                c.Equal(null, searched.Problem, $"Lookahead, {what}");
                c.That(searched.Ids.All(id => !id.StartsWith("discard_potion:", StringComparison.Ordinal)), $"Lookahead, {what}: no potion thrown away");
            }
        });
    }
}
