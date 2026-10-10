using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// The parts of the engine that are about the player rather than the rules: card choices
/// through action ids (Armaments as the worked example), potions, the shape of the snapshots,
/// determinism, and copies of a running fight.
/// </summary>
public sealed class CoreChoiceChecks : IArenaChecks
{
    /// <summary>Exhaust a chosen hand card, then draw 2 (the shape of the game's Burning Pact).</summary>
    private static readonly CardDef Pact = new("CheckPact", "Pact", CardType.Skill, CardRarity.Uncommon, 0, TargetType.Self)
    {
        Play = c =>
        {
            if (c.F.ChooseHandCardToExhaust() is { } card) c.F.Exhaust(card);
            c.Draw(2);
        },
    };

    /// <summary>Hit, then put a chosen discard-pile card on top of the draw pile (the shape of Headbutt).</summary>
    private static readonly CardDef Recall = new("CheckRecall", "Recall", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Play = c =>
        {
            c.Attack(3).Execute();
            var picked = c.F.ChooseFromPile(PileType.Discard, "Choose a card to put on top of your draw pile.", 1, 1).FirstOrDefault();
            if (picked is not null) c.F.MoveCard(picked, PileType.Draw, PilePosition.Top);
        },
    };

    /// <summary>Two questions in one card: exhaust one, then exhaust another.</summary>
    private static readonly CardDef Twice = new("CheckTwice", "Twice", CardType.Skill, CardRarity.Rare, 0, TargetType.Self)
    {
        Play = c =>
        {
            if (c.F.ChooseHandCardToExhaust() is { } first) c.F.Exhaust(first);
            c.Draw(1);
            if (c.F.ChooseHandCardToExhaust() is { } second) c.F.Exhaust(second);
        },
    };

    private static readonly CardDef Broken = new("CheckBroken", "Broken", CardType.Attack, CardRarity.Common, 0, TargetType.AnyEnemy)
    {
        Play = c =>
        {
            c.Attack(10).Execute();
            throw new InvalidOperationException("a card with a bug");
        },
    };

    private static readonly PotionDef Fire = new("CheckFire", "Check Fire", PotionRarity.Common, TargetType.AnyEnemy)
    {
        Text = _ => "Deal 20 damage.",
        Use = p => p.F.Damage(p.Target!, 20, Props.NonCardUnpowered, p.Owner),
    };

    private static readonly PotionDef Guard = new("CheckGuard", "Check Guard", PotionRarity.Common, TargetType.AnyPlayer)
    {
        Text = _ => "Gain 12 Block.",
        Use = p => p.F.GainBlock(p.Target!, 12, Props.NonCardUnpowered),
    };

    private static readonly PotionDef Fairy = new("CheckFairy", "Check Fairy", PotionRarity.Rare, TargetType.Self)
    {
        Usage = PotionUsage.Automatic,
        Text = _ => "Cannot be used by hand.",
        Use = _ => { },
    };

    /// <summary>Prevents its holder's death once, heals 10 and uses itself up (the shape of Fairy in a Bottle).</summary>
    private sealed class SecondChanceHooks : Listener
    {
        public override bool ShouldDie(Fight f, Creature creature) => !creature.IsPlayer;

        public override void AfterPreventingDeath(Fight f, Creature creature)
        {
            f.Heal(creature, 10);
            f.RemovePotion(f.PotionSlot(SecondChance));
        }
    }

    private static readonly PotionDef SecondChance = new("CheckSecondChance", "Second Chance", PotionRarity.Rare, TargetType.Self)
    {
        Usage = PotionUsage.Automatic,
        Text = _ => "When you would die, heal 10 HP instead.",
        Use = _ => { },
        Hooks = new SecondChanceHooks(),
    };

    private sealed class Leaky : Power
    {
        public List<int> Numbers = new();
        public override string Name => "Leaky";
        public override PowerType Type => PowerType.Buff;
        public override string Describe() => "";
    }

    public void Run(Check c)
    {
        Armaments(c);
        OtherChoices(c);
        PotionsAndSnapshots(c);
        DeterminismAndCopies(c);
    }

    private static void Armaments(Check c)
    {
        c.Group("Armaments: the hand choice through action ids", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put("Armaments", PileType.Hand);
            // Hand: Strike, Strike, Defend, Defend, Bash, Armaments.
            f.Play("Armaments");
            var s = f.Snapshot();
            c.Equal("hand_select", s.Screen, "the screen of a hand choice");
            c.That(s.AwaitingDecision, "it awaits a decision");
            c.Equal(5, f.Player.Block, "the Block came before the question");
            c.Equal(2, f.Energy, "the cost was paid");
            c.Equal(5, f.Hand.Count, "Armaments itself is in the Play pile, not in the hand");
            c.Sequence(["select:0", "select:1", "select:2", "select:3", "select:4"], s.Actions.Select(a => a.Id), "every upgradable hand card can be selected; no confirm yet");
            c.Equal("Select Bash", s.Actions[4].Label, "labels as the reader words them");
            var selection = s.State["selection"]!.AsObject();
            c.Equal("upgrade", selection["mode"]!.GetValue<string>(), "upgrade mode");
            c.Equal(1, selection["min"]!.GetValue<int>(), "min 1");
            c.Equal(1, selection["max"]!.GetValue<int>(), "max 1");
            c.That(s.State["hand"]!.AsArray().All(x => x!["selectable"]!.GetValue<bool>() && x["playable"] is null), "hand entries say selectable, not playable");
            c.Sequence(
                ["run", "turn", "round", "energy", "max_energy", "me", "enemies", "incoming_damage", "hand", "draw_pile", "discard_pile", "exhaust_count", "potions", "relics", "selection"],
                s.State.Select(kv => kv.Key), "the hand selection carries the whole board plus the selection");

            c.That(!f.Execute("end_turn").Ok && !f.Execute("play:0>e0").Ok && !f.Execute("confirm").Ok && !f.Execute("select:9").Ok, "nothing else is accepted while the choice is open");

            f.Do("select:4");
            s = f.Snapshot();
            c.Sequence(["confirm"], s.Actions.Select(a => a.Id), "at the maximum only confirm is left");
            c.Equal("Confirm selection (1 chosen)", s.Actions[0].Label, "the confirm label");
            c.Equal(true, s.State["hand"]![4]!["selected"]?.GetValue<bool>(), "the picked card is marked");
            c.Equal("Bash", s.State["selection"]!["selected"]![0]!.GetValue<string>(), "and named in the selection");
            c.That(!f.Execute("select:0").Ok, "a second card cannot be selected");

            f.Do("confirm");
            s = f.Snapshot();
            c.Equal("combat", s.Screen, "back in the fight");
            var bash = f.Hand[f.HandIndex("Bash+")];
            c.Equal("Deal 10 damage. Apply 3 Vulnerable.", Fight.CardText(f, bash, PileType.Hand), "Bash is upgraded for the rest of the fight");
            c.Equal(5, f.Player.Block, "the Block was gained once, not again when the card resumed");
            c.Equal(2, f.Energy, "the cost was paid once");
            c.That(f.DiscardPile.Count == 1 && f.DiscardPile[0].Title == "Armaments", "Armaments went to the discard pile");
            c.Equal(1, f.CardsPlayed, "one card played");
            f.Play("Bash+", 0);
            c.Equal(90, f.Enemy(0).Hp, "10 damage");
            c.Equal(3, f.Enemy(0).PowerAmount<VulnerablePower>(), "3 Vulnerable");
        });

        c.Group("Armaments: no question with one candidate; the upgraded card upgrades the whole hand", () =>
        {
            var f = CheckKit.VsDummy();
            f.Clear(PileType.Hand);
            f.Put("Armaments", PileType.Hand);
            f.Put("Wound", PileType.Hand);
            var strike = f.Put("StrikeIronclad", PileType.Hand);
            f.Play("Armaments");
            c.Equal("combat", f.Snapshot().Screen, "one candidate is taken without asking");
            c.That(strike.IsUpgraded, "and upgraded");
            c.Equal("Deal 9 damage.", Fight.CardText(f, strike, PileType.Hand), "Strike+ deals 9");

            var none = CheckKit.VsDummy();
            none.Clear(PileType.Hand);
            none.Put("Armaments", PileType.Hand);
            none.Put("Wound", PileType.Hand);
            none.Play("Armaments");
            c.Equal("combat", none.Snapshot().Screen, "no candidate: nothing happens");

            var g = CheckKit.VsDummy();
            var plus = g.Put("Armaments+", PileType.Hand);
            g.Put("Dazed", PileType.Hand);
            c.Equal("Gain 5 Block. Upgrade all cards in your hand for the rest of combat.", Fight.CardText(g, plus, PileType.Hand), "the upgraded text; the Block stays 5");
            g.Play("Armaments+");
            c.Equal("combat", g.Snapshot().Screen, "the upgraded card asks nothing");
            c.Sequence(["Strike+", "Strike+", "Defend+", "Defend+", "Bash+", "Dazed"], g.HandTitles(), "every upgradable card in hand is upgraded; a status is not");
            c.That(!g.DiscardPile[0].Title.EndsWith("++", StringComparison.Ordinal), "and it does not upgrade itself");
        });
    }

    private static void OtherChoices(Check c)
    {
        c.Group("a choice in the middle of a card: what happened before stays, what comes after happens once", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Pact, PileType.Hand);
            var nextTwo = f.DrawPile.Take(2).ToList();
            f.Play("Pact");
            var s = f.Snapshot();
            c.Equal("hand_select", s.Screen, "the exhaust choice is a hand selection");
            c.Equal("select", s.State["selection"]!["mode"]!.GetValue<string>(), "select mode");
            c.Equal("Choose a card to Exhaust.", s.State["selection"]!["prompt"]!.GetValue<string>(), "our own prompt");
            c.Equal(5, f.DrawPile.Count, "nothing is drawn before the answer");
            f.Do("select:0", "confirm");
            c.Equal(1, f.ExhaustPile.Count, "the chosen card was exhausted");
            c.Equal("Strike", f.ExhaustPile[0].Title, "it was the first Strike");
            c.Equal(6, f.Hand.Count, "then two cards were drawn");
            c.That(f.Hand[^2].Id == nextTwo[0].Id && f.Hand[^1].Id == nextTwo[1].Id, "the same two cards that were on top before the question");
        });

        c.Group("two choices in one card", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Twice, PileType.Hand);
            f.Play("Twice");
            f.Do("select:4", "confirm");
            c.Equal("hand_select", f.Snapshot().Screen, "the second question follows");
            c.Sequence(["Strike", "Strike", "Defend", "Defend", "Strike"], f.HandTitles(), "Bash is gone and a card was drawn in between");
            c.Equal(1, f.ExhaustPile.Count, "one card exhausted so far");
            f.Do("select:2", "confirm");
            c.Equal("combat", f.Snapshot().Screen, "done");
            c.Sequence(["Bash", "Defend"], f.ExhaustPile.Select(x => x.Title), "both choices took effect, in order");
            c.Sequence(["Strike", "Strike", "Defend", "Strike"], f.HandTitles(), "the hand afterwards");
            c.Equal(4, f.DrawPile.Count, "exactly one card was drawn");
        });

        c.Group("choosing from a pile: the card selection screen", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put("StrikeIronclad", PileType.Discard);
            f.Put("StrikeIronclad", PileType.Discard);
            f.Put("Bash+", PileType.Discard);
            f.Put(Recall, PileType.Hand);
            f.Play("Recall", 0);
            var s = f.Snapshot();
            c.Equal("card_select", s.Screen, "a pile choice is a card selection");
            c.Sequence(["kind", "prompt", "min", "max", "pile", "cards", "run", "combat"], s.State.Select(kv => kv.Key), "its keys");
            c.Equal("combat_pile_select", s.State["kind"]!.GetValue<string>(), "kind");
            c.Equal("Discard", s.State["pile"]!.GetValue<string>(), "pile");
            var cards = s.State["cards"]!.AsArray();
            c.Equal(2, cards.Count, "identical copies are listed once");
            c.Equal(2, cards[0]!["copies"]!.GetValue<int>(), "with their number");
            c.Equal("Deal 10 damage. Apply 3 Vulnerable.", cards[1]!["text"]!.GetValue<string>(), "a card in the discard pile shows its plain numbers");
            c.Sequence(["card:0", "card:2"], s.Actions.Select(a => a.Id), "one action per listed card, under the first copy's index; no confirm, no cancel");
            c.Equal("Select Bash+", s.Actions[1].Label, "the label");
            c.Equal(97, f.Enemy(0).Hp, "the hit came before the question");
            c.Sequence(["turn", "energy", "max_energy", "me", "hand", "draw_count", "discard_count", "enemies"], s.State["combat"]!.AsObject().Select(kv => kv.Key), "the small picture of the fight");
            f.Do("card:2");
            c.Equal("combat", f.Snapshot().Screen, "picking the one card finishes the choice");
            c.Equal("Bash+", f.DrawPile[0].Title, "the chosen card is on top of the draw pile");
            c.Equal(97, f.Enemy(0).Hp, "the hit happened once");

            // One card in the discard pile: taken without asking. Recall itself is in the Play pile.
            var g = CheckKit.VsDummy();
            g.Put("DefendIronclad", PileType.Discard);
            g.Put(Recall, PileType.Hand);
            g.Play("Recall", 0);
            c.Equal("combat", g.Snapshot().Screen, "no question for a single candidate");
            c.Equal("Defend", g.DrawPile[0].Title, "it went on top");
            var h = CheckKit.VsDummy();
            h.Put(Recall, PileType.Hand);
            h.Play("Recall", 0);
            c.Equal("combat", h.Snapshot().Screen, "an empty pile asks nothing");

            // The draw pile is listed in an order that tells nothing; a choice with a range has a confirm.
            var d = CheckKit.VsDummy();
            d.RunAsAction(x => x.ChooseFromPile(PileType.Draw, "Choose cards.", 0, 2));
            var listed = d.Snapshot().State["cards"]!.AsArray().Select(x => x!["name"]!.GetValue<string>()).ToList();
            c.Sequence(["Defend", "Strike"], listed, "draw-pile cards are sorted by how they look and grouped");
            c.Sequence(["card:0", "card:2", "confirm"], d.Snapshot().Actions.Select(a => a.Id), "with a range, confirm is offered from the minimum on");
            c.Equal("Confirm with nothing selected", d.Snapshot().Actions[^1].Label, "the empty confirm label");
            d.Do("card:0");
            c.Sequence(["card:0", "card:1", "card:2", "confirm"], d.Snapshot().Actions.Select(a => a.Id), "the picked card can be deselected; its twin is now listed by itself");
            c.Equal("Deselect Defend", d.Snapshot().Actions[0].Label, "deselect label");
            d.Do("card:2", "confirm");
            c.Equal("combat", d.Snapshot().Screen, "confirmed");
        });

        c.Group("choosing one of three offered cards, or skipping", () =>
        {
            CardInstance? taken = null;
            var f = CheckKit.VsDummy();
            void Offer(Fight x)
            {
                var offered = new[] { x.CreateCard("Bash"), x.CreateCard("Slimed"), x.CreateCard("StrikeIronclad", upgraded: true) };
                var pick = x.ChooseOneOf(offered, canSkip: true);
                if (pick is not null) x.AddGeneratedCard(pick, PileType.Hand);
                if (ReferenceEquals(x, f)) taken = pick;
            }
            f.RunAsAction(Offer);
            var s = f.Snapshot();
            c.Equal("card_select", s.Screen, "the choose-a-card screen");
            c.Sequence(["kind", "min", "max", "cards", "can_skip", "run", "combat"], s.State.Select(kv => kv.Key), "its keys");
            c.Equal("choose", s.State["kind"]!.GetValue<string>(), "kind");
            c.Equal(0, s.State["min"]!.GetValue<int>(), "skippable: min 0");
            c.Sequence(["card:0", "card:1", "card:2", "skip"], s.Actions.Select(a => a.Id), "three cards and skip");
            c.Equal("Take Strike+", s.Actions[2].Label, "the label");
            f.Do("card:2");
            c.Equal("Strike+", f.Hand[^1].Title, "the taken card is in the hand");
            c.That(taken is not null && ReferenceEquals(taken, f.Hand[^1]), "and it is the offered card");

            var g = CheckKit.VsDummy();
            g.RunAsAction(Offer);
            g.Do("skip");
            c.Equal(5, g.Hand.Count, "a skip takes nothing");
        });

        c.Group("a multi-card hand choice with a range", () =>
        {
            var f = CheckKit.VsDummy();
            f.RunAsAction(x =>
            {
                foreach (var card in x.ChooseFromHand("Choose any number of cards to Exhaust.", 0, 99)) x.Exhaust(card);
            });
            var s = f.Snapshot();
            c.Equal(6, s.Actions.Count, "five cards and confirm");
            c.Equal("Confirm selection (0 chosen)", s.Actions[^1].Label, "confirm is there from the start");
            f.Do("select:1", "select:3");
            c.Sequence(["select:0", "select:2", "select:4", "confirm"], f.Snapshot().Actions.Select(a => a.Id), "selected cards are no longer offered");
            f.Do("confirm");
            c.Sequence(["Strike", "Defend"], f.ExhaustPile.Select(x => x.Title), "exhausted in the order picked");
            c.Sequence(["Strike", "Defend", "Bash"], f.HandTitles(), "the rest stays");
        });
    }

    private static void PotionsAndSnapshots(Check c)
    {
        c.Group("potions as the mod exposes them", () =>
        {
            var f = CheckKit.VsDummies(new CheckDummy { Hp = 30, DisplayName = "Left" }, new CheckDummy { Hp = 15, DisplayName = "Right" });
            f.SetPotion(0, Fire);
            f.SetPotion(2, Fairy);
            f.SetPotion(1, Guard);
            var s = f.Snapshot();
            var potions = s.State["potions"]!.AsArray();
            c.Equal("""{"slot":0,"name":"Check Fire","text":"Deal 20 damage.","target":"AnyEnemy","usable":true}""", potions[0]!.ToJsonString(), "a potion slot");
            c.Equal("AnyPlayer", potions[1]!["target"]!.GetValue<string>(), "target kinds by the game's names");
            c.Equal(false, potions[2]!["usable"]!.GetValue<bool>(), "an automatic potion is not usable by hand");
            var ids = s.Actions.Select(a => a.Id).ToList();
            c.Sequence(["potion:0>e0", "potion:0>e1", "discard_potion:0", "potion:1", "discard_potion:1", "discard_potion:2", "end_turn"], ids.Skip(ids.Count - 7), "potion actions follow the plays, slot by slot, then end_turn");
            c.Equal("Use Check Fire on Right", s.Actions.Single(a => a.Id == "potion:0>e1").Label, "the use label");
            c.Equal("Discard Check Guard", s.Actions.Single(a => a.Id == "discard_potion:1").Label, "the discard label");

            f.SetBlock(f.Enemy(0), 5);
            f.ApplyPower<VulnerablePower>(f.Enemy(0), 1, f.Player);
            f.ApplyPower<StrengthPower>(f.Player, 3, f.Player);
            f.Do("potion:0>e0");
            c.Equal(15, f.Enemy(0).Hp, "potion damage is not modified, and Block absorbs it: 20 - 5");
            c.That(f.Potions[0] is null, "the slot is empty");
            c.Equal(3, f.Energy, "a potion costs no energy");
            f.ApplyPower<FrailPower>(f.Player, 1, f.Enemy(0));
            f.Do("potion:1");
            c.Equal(12, f.Player.Block, "potion Block ignores Frail");
            f.Do("discard_potion:2");
            c.That(f.Potions.All(p => p is null), "discarded");
            c.That(f.Snapshot().Actions.All(a => !a.Id.Contains("potion", StringComparison.Ordinal)), "no potion actions with an empty belt");
            c.Equal("[null,null,null]", f.Snapshot().State["potions"]!.ToJsonString(), "three empty slots");

            // A potion can end the fight; the win check runs after it.
            var g = CheckKit.VsDummy(new CheckDummy { Hp = 20 });
            g.SetPotion(1, Fire);
            g.Do("potion:1>e0");
            c.That(g.IsOver && g.Won, "won by a potion");

            // While a hand choice is open the potions are shown but cannot be used.
            var h = CheckKit.VsDummy();
            h.SetPotion(0, Guard);
            h.Put("Armaments", PileType.Hand);
            h.Play("Armaments");
            c.Equal(false, h.Snapshot().State["potions"]![0]!["usable"]!.GetValue<bool>(), "not usable during a selection");
            c.That(!h.Execute("potion:0").Ok, "and refused");
        });

        c.Group("a potion in the belt hears hooks; a prevented death is not a loss", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(5).Enemy(new CheckDummy { Steps = [new DummyStep("HIT", Damage: 12)] }).Start();
            f.SetPotion(1, SecondChance);
            f.EndTurn();
            c.That(!f.IsOver, "the fight goes on");
            c.Equal(10, f.Player.Hp, "healed from 0");
            c.That(f.Potions[1] is null, "the potion used itself up");
            f.EndTurn();
            c.That(f.IsOver && !f.Won, "the second time nothing prevents it");
        });

        c.Group("the card pool for random generation", () =>
        {
            var pool = Content.Registry.IroncladPool;
            c.That(pool.Any(x => x.ClassName == "Bash") && pool.Any(x => x.ClassName == "Armaments"), "pool cards are in it");
            c.That(pool.All(x => x.Type != CardType.Status), "status cards are not");
            c.That(pool.Select(x => x.ClassName).SequenceEqual(pool.Select(x => x.ClassName).Order(StringComparer.Ordinal)), "in pool order (by class name)");
            c.That(!Content.Registry.Card("Bash").IsGeneratableInCombat && Content.Registry.Card("Armaments").IsGeneratableInCombat, "Basic cards are never generated in a fight");
            var f = CheckKit.VsDummy();
            var strike = f.Hand[0];
            strike.ReplayCount = 1;
            c.Equal("Deal 6 damage. / Played 1 extra time.", Fight.CardText(f, strike, PileType.Hand), "a replay count shows on the card");
            f.Play("Strike", 0);
            c.Equal(88, f.Enemy(0).Hp, "and the card runs twice");
            var copy = f.CloneCard(f.Hand[0]);
            c.That(copy.IsClone && copy.Pile == PileType.None && copy.Id != f.Hand[0].Id, "CloneCard makes a new card in no pile, flagged as a clone");
        });

        c.Group("the combat snapshot, key by key", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Hp(61).Floor(7)
                .Enemy(new CheckDummy { Hp = 30, Steps = [new DummyStep("MIX", Damage: 4, Hits: 2, Block: 3, Then: (x, me) => x.AddStatusCards("Slimed", 2, PileType.Discard, PilePosition.Bottom, me))] }).Start();
            f.ApplyPower<StrengthPower>(f.Player, -1, f.Player);
            f.SetBlock(f.Player, 3);
            var s = f.Snapshot();
            c.Equal("""{"act":1,"floor":7,"ascension":0,"room":"Monster"}""", s.State["run"]!.ToJsonString(), "run");
            c.Equal("""{"name":"The Ironclad","hp":61,"max_hp":80,"block":3,"powers":[{"name":"Strength","amount":-1,"kind":"Debuff","text":"Attacks deal 1 less damage per hit."}]}""",
                s.State["me"]!.ToJsonString(), "me: Block only when above 0, powers with amount, kind and text");
            c.Equal("""[{"i":0,"name":"Dummy","hp":30,"max_hp":30,"intents":["attack 4x2","defend","debuff"]}]""", s.State["enemies"]!.ToJsonString(), "enemies: no block key at 0, no powers key when empty");
            c.Equal(8, s.State["incoming_damage"]!.GetValue<int>(), "incoming");
            c.Equal("""{"i":0,"name":"Strike","cost":"1","type":"Attack","text":"Deal 5 damage.","playable":true}""", s.State["hand"]![0]!.ToJsonString(), "a hand card: the text has the player's Strength");
            c.Equal("""{"count":5,"cards":{"Defend":2,"Strike":3}}""", s.State["draw_pile"]!.ToJsonString(), "piles by name, sorted, order hidden");
            c.Equal("""["Burning Blood"]""", s.State["relics"]!.ToJsonString(), "relics by name");
            c.Equal("Play Strike on Dummy", s.Actions[0].Label, "a single enemy is named without its id");
            c.Equal("""{"turn":1,"round":1,"hp":61,"block":3}""", f.WaitingState().ToJsonString(), "the waiting state");

            f.EndTurn();
            s = f.Snapshot();
            c.Equal("""{"count":7,"cards":{"Bash":1,"Defend":2,"Slimed":2,"Strike":2}}""", s.State["discard_pile"]!.ToJsonString(), "status cards from a monster land in the discard pile");
            c.Equal(3, s.State["enemies"]![0]!["block"]!.GetValue<int>(), "an enemy's Block shows while it has some");

            var deck = f.DescribeDeck();
            c.Sequence(["character", "hp", "max_hp", "gold", "run", "deck_size", "deck", "relics", "potions"], deck.Select(kv => kv.Key), "get_deck's keys");
            c.Equal(10, deck["deck"]!.AsArray().Count, "one entry per card, in deck order");
            c.Equal("""{"name":"Bash","cost":"2","type":"Attack","text":"Deal 8 damage. Apply 2 Vulnerable.","rarity":"Basic"}""", deck["deck"]![4]!.ToJsonString(), "a deck card: plain numbers, with its rarity");
            c.Equal("""[{"name":"Burning Blood","text":"At the end of combat, heal 6 HP."}]""", deck["relics"]!.ToJsonString(), "relics with text");
        });

        c.Group("twins are told apart by their id in labels", () =>
        {
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Encounter("NibbitsNormal").Start();
            c.Equal("Play Strike on Nibbit (e0)", f.Snapshot().Actions[0].Label, "two living enemies of one name");
        });
    }

    private static void DeterminismAndCopies(Check c)
    {
        c.Group("determinism: a seed decides the whole fight", () =>
        {
            for (var seed = 1; seed <= 5; seed++)
            {
                var a = CheckKit.PlayOut(new FightBuilder().Encounter("NibbitsNormal").Seed(seed).Start());
                var b = CheckKit.PlayOut(new FightBuilder().Encounter("NibbitsNormal").Seed(seed).Start());
                c.That(a == b, $"seed {seed}: two runs differ");
            }
            var one = CheckKit.PlayOut(new FightBuilder().Encounter("NibbitsNormal").Seed(1).Start());
            var two = CheckKit.PlayOut(new FightBuilder().Encounter("NibbitsNormal").Seed(2).Start());
            c.That(one != two, "different seeds give different fights");
        });

        c.Group("Clone: an exact, independent copy", () =>
        {
            var f = new FightBuilder().Encounter("NibbitsNormal").Seed(9).Start();
            f.Play("Strike", 0);
            f.EndTurn();
            var seen = f.Seen();
            var copy = f.Clone();
            c.Equal(seen, copy.Seen(), "the copy shows the same");
            var played = CheckKit.PlayOut(copy);
            c.Equal(seen, f.Seen(), "playing the copy to the end leaves the original untouched");
            c.Equal(played, CheckKit.PlayOut(f.Clone()), "a second copy plays out identically");
            c.Equal(played, CheckKit.PlayOut(f), "and so does the original");

            // Powers with extra state, monsters in the middle of their machine, cost changes and upgrades are all carried.
            var g = CheckKit.VsDummy(new CheckDummy { Steps = [new DummyStep("A", Damage: 2), new DummyStep("B", Block: 3)] });
            g.ApplyPower<PlatingPower>(g.Player, 3, g.Player);
            g.Hand[0].SetCostThisCombat(0);
            g.Upgrade(g.Hand[4]);
            g.EndTurn();
            var twin = g.Clone();
            c.Equal(g.Seen(), twin.Seen(), "same after a clone");
            c.That(!ReferenceEquals(g.Player, twin.Player) && !ReferenceEquals(g.Player.Powers[0], twin.Player.Powers[0]), "creatures and powers are copies");
            c.That(ReferenceEquals(twin.Player.Powers[0].Owner, twin.Player), "a copied power belongs to the copied creature");
            c.That(ReferenceEquals(twin.Enemy(0).Monster!.Creature, twin.Enemy(0)), "a copied monster belongs to the copied creature");
            twin.EndTurn();
            g.EndTurn();
            c.Equal(g.Seen(), twin.Seen(), "and after both play on");
        });

        c.Group("Clone in the middle of a card choice", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Pact, PileType.Hand);
            f.Play("Pact");
            var copy = f.Clone();
            c.Equal(f.Seen(), copy.Seen(), "the copy is at the same question");
            copy.Do("select:4", "confirm");
            c.Equal("hand_select", f.Snapshot().Screen, "answering the copy leaves the original waiting");
            c.Equal("Bash", copy.ExhaustPile[0].Title, "the copy exhausted Bash");
            f.Do("select:0", "confirm");
            c.Equal("Strike", f.ExhaustPile[0].Title, "the original exhausted the Strike");
            c.Sequence(f.Hand.Skip(4).Select(x => x.Title), copy.Hand.Skip(4).Select(x => x.Title), "both drew the same two cards");
        });

        c.Group("CloneDeterminized: what the player cannot know is replaced, what they can see is not", () =>
        {
            var f = new FightBuilder().Seed(4).Enemy(new CheckDummy()).Start();
            var known = f.DrawPile[^1];
            f.MoveCard(known, PileType.Draw, PilePosition.Top);
            var seen = f.Seen();
            var order = f.DrawPile.Select(x => x.Id).ToList();
            var different = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var d = f.CloneDeterminized(seed);
                c.That(d.Seen() == seen, $"seed {seed}: the determinized copy shows something else");
                c.That(d.DrawPile[0].Id == known.Id, $"seed {seed}: a card the player put on top stays on top");
                c.That(d.DrawPile.Select(x => x.Id).Order().SequenceEqual(order.Order()), $"seed {seed}: the draw pile holds other cards");
                if (!d.DrawPile.Select(x => x.Id).SequenceEqual(order)) different++;
            }
            c.That(different >= 15, $"the unknown part of the draw pile is reshuffled ({different} of 20 orders differ)");
            c.Equal(seen, f.Seen(), "the original is untouched");

            // Future random numbers differ too: the same action gives different results on different copies.
            var r = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Enemy(new CheckDummy { Hp = 50 }).Enemy(new CheckDummy { Hp = 50 }).Enemy(new CheckDummy { Hp = 50 }).Start();
            var randomHit = new CardDef("CheckRandomHit", "Random Hit", CardType.Attack, CardRarity.Common, 0, TargetType.RandomEnemy)
            {
                Play = x => x.Attack(7).Execute(),
            };
            r.Put(randomHit, PileType.Hand);
            var targets = new HashSet<int>();
            for (var seed = 1; seed <= 30; seed++)
            {
                var d = r.CloneDeterminized(seed);
                d.Play("Random Hit");
                targets.Add(d.Roster.First(e => e.Hp < 50).RosterIndex);
            }
            c.Equal(3, targets.Count, "over 30 determinized copies the random target varies");

            // In the middle of a choice: what already happened is replayed as it was; the future after the answer is new.
            var p = CheckKit.VsDummy();
            p.Put(Pact, PileType.Hand);
            p.Play("Pact");
            var draws = new HashSet<string>();
            for (var seed = 1; seed <= 30; seed++)
            {
                var d = p.CloneDeterminized(seed);
                c.That(d.Seen() == p.Seen(), $"seed {seed}: a determinized copy of an open choice shows something else");
                d.Do("select:4", "confirm");
                c.That(d.ExhaustPile.Count == 1 && d.ExhaustPile[0].Title == "Bash" && d.Hand.Count == 6, $"seed {seed}: the choice did not resolve as asked");
                draws.Add(string.Join(",", d.Hand.Skip(4).Select(x => x.Title)));
            }
            c.That(draws.Count > 1, "the cards drawn after the answer depend on the copy's seed");
        });

        c.Group("copies stay in step: every action on a fight and on its copy shows the same", () =>
        {
            string[] deck =
            [
                "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "StrikeIronclad", "DefendIronclad", "DefendIronclad", "DefendIronclad", "Bash",
                "Armaments", "Armaments", "Armaments+", "Slimed", "Void", "Dazed", "Burn", "Wound", "Toxic", "Beckon", "FranticEscape", "Infection",
            ];
            for (var seed = 1; seed <= 12; seed++)
            {
                var f = new FightBuilder().Deck(deck).Hp(60).Encounter("NibbitsNormal").Seed(seed).Start();
                f.SetPotion(0, Fire);
                f.SetPotion(1, Guard);
                var ids = new List<string>();
                for (var step = 0; step < 400 && !f.IsOver; step++)
                {
                    var actions = f.LegalActions();
                    // A cheap, fixed way of picking: mostly plays, sometimes the turn's end.
                    var pick = actions[(seed * 31 + step * 7) % actions.Count].Id;
                    var twin = f.Clone();
                    f.Do(pick);
                    twin.Do(pick);
                    ids.Add(pick);
                    if (f.Seen() == twin.Seen()) continue;
                    c.That(false, $"seed {seed}: after {string.Join(" ", ids)} the copy shows something else");
                    break;
                }
                c.That(f.IsOver, $"seed {seed}: the fight did not end in 400 actions");

                // And the same ids on a new fight give the same end.
                var again = new FightBuilder().Deck(deck).Hp(60).Encounter("NibbitsNormal").Seed(seed).Start();
                again.SetPotion(0, Fire);
                again.SetPotion(1, Guard);
                foreach (var id in ids) again.Do(id);
                c.That(again.Seen() == f.Seen(), $"seed {seed}: replaying the ids ends differently");
            }
        });

        c.Group("a rule that throws leaves the fight as it was", () =>
        {
            var f = CheckKit.VsDummy();
            f.Put(Broken, PileType.Hand);
            var seen = f.Seen();
            var threw = false;
            try
            {
                f.Play("Broken", 0);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            c.That(threw, "the exception reaches the caller");
            c.Equal(seen, f.Seen(), "and the fight is back to before the action");
            f.Play("Strike", 0);
            c.Equal(94, f.Enemy(0).Hp, "and still playable");
        });

        c.Group("the clone guard finds fields a copy would share", () =>
        {
            c.That(ListenerFields.UnsafeFields(typeof(Leaky)).Any(), "a power with a List and no OnCloned is reported");
            c.That(!ListenerFields.UnsafeFields(typeof(StrengthPower)).Any(), "a plain power is not");
            c.That(!ListenerFields.UnsafeFields(typeof(Nibbit)).Any(), "nor is a monster with flags");
        });
    }
}
