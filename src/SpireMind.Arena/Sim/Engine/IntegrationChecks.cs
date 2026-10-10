using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// Checks for the engine rules that were settled when the content files were put together,
/// each read from the game's code (v0.107.1): an affliction is not put on a card twice, a
/// transformed card counts as generated, a monster's status cards have no creator, discarding
/// several cards and drawing, a potion used from a hook, a card's own cost against its cost
/// with powers, and a copy for search players that gives nothing away about the draw order.
/// </summary>
public sealed class IntegrationChecks : IArenaChecks
{
    /// <summary>A relic that writes down the hooks these checks are about, in the order they come.</summary>
    private sealed class Tap : Relic
    {
        public List<string> Seen = new();

        public override string Name => "Tap";
        public override string Describe() => "Writes down what it hears.";
        public override void OnCloned(CloneMap map) { }

        public override void AfterCardEnteredCombat(Fight f, CardInstance card) => Seen.Add($"entered {card.Title}");
        public override void AfterCardChangedPiles(Fight f, CardInstance card, PileType oldPile) => Seen.Add($"moved {card.Title} from {oldPile} to {card.Pile}");
        public override void AfterCardGeneratedForCombat(Fight f, CardInstance card, Creature? creator) => Seen.Add($"generated {card.Title} by {creator?.Name ?? "nobody"}");
        public override void AfterCardDiscarded(Fight f, CardInstance card) => Seen.Add($"discarded {card.Title}");
        public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw) => Seen.Add($"drew {card.Title}");
        public override void BeforePotionUsed(Fight f, PotionDef potion, Creature? target) => Seen.Add($"before {potion.Name} on {target?.Name ?? "nobody"}");
        public override void AfterPotionUsed(Fight f, PotionDef potion, Creature? target) => Seen.Add($"after {potion.Name}, HP {f.Player.Hp}");
        public override void BeforeAttack(Fight f, AttackCommand attack) => Seen.Add(string.Create(CultureInfo.InvariantCulture, $"attack {attack.Id}"));
    }

    /// <summary>A monster that hands out a status card through the monster helper every turn.</summary>
    private sealed class Slimer : Monster
    {
        public override string Name => "Slimer";
        public override int MinHp => 50;
        public override int MaxHp => 50;

        protected override MonsterState BuildMachine(MachineBuilder m)
        {
            var goop = m.Move("GOOP", f => AddStatusCards(f, "Slimed", 2), Intent.Status(2));
            goop.Then(goop);
            return goop;
        }
    }

    private static Fight WithTap(out Tap tap, params Monster[] monsters)
    {
        tap = new Tap();
        var builder = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap);
        foreach (var monster in monsters.DefaultIfEmpty(new CheckDummy())) builder.Enemy(monster);
        var f = builder.Start();
        tap.Seen.Clear();
        return f;
    }

    public void Run(Check c)
    {
        c.Group("an affliction that is not stackable is not put on a card a second time, and never replaces another", () =>
        {
            var registry = Content.Registry;
            c.That(registry.Afflictions.Values.All(a => !a.IsStackable), "no affliction in the arena is stackable (in the game only Galvanized and Tainted are)");
            var f = CheckKit.VsDummy();
            var card = f.Hand[0];
            c.That(f.Afflict(card, "Entangled", 1), "the first one lands");
            c.That(!f.Afflict(card, "Entangled", 1), "the same one again is refused");
            c.Equal(1, card.AfflictionAmount, "and its amount stays");
            c.That(!f.Afflict(card, "Ringing", 1), "another kind is refused");
            c.Equal("Entangled", card.Affliction, "and the card keeps what it had");
            f.ClearAffliction(card);
            c.That(f.Afflict(card, "Ringing", 1) && card.Affliction == "Ringing", "once cleared, the card can take the other kind");
        });

        c.Group("a transformed card counts as generated: entered, changed piles, then generated with the player as creator", () =>
        {
            var f = WithTap(out var tap);
            var defend = f.Hand[2];
            var rock = f.Transform(defend, Content.Card("GiantRock"));
            c.That(rock is not null && f.Hand[2] == rock, "the new card sits where the old one sat");
            c.Sequence(["entered Giant Rock", "moved Giant Rock from Hand to Hand", "generated Giant Rock by The Ironclad"], tap.Seen, "the hooks, in the game's order");
            c.Equal(PileType.None, defend.Pile, "the old card is in no pile");
        });

        c.Group("status cards a monster hands out through the monster helper have no creator", () =>
        {
            var f = WithTap(out var tap, new Slimer());
            f.EndTurn();
            c.Sequence(["generated Slimed by nobody", "generated Slimed by nobody"], tap.Seen.Where(s => s.StartsWith("generated", StringComparison.Ordinal)), "two Slimed, nobody named");
            c.Equal(2, f.History.Count(e => e.Kind == HistoryKind.CardGenerated && e.OtherId == -1), "and the history has no creator for them either");
            c.Equal(2, f.DiscardPile.Count(card => card.Title == "Slimed"), "they went to the discard pile");
        });

        c.Group("DiscardAndDraw: each card is discarded with its hook, in the order given, and only then are cards drawn", () =>
        {
            var f = WithTap(out var tap);
            f.DiscardAndDraw([f.Hand[4], f.Hand[0]], 2);
            c.Sequence(
                ["moved Bash from Hand to Discard", "discarded Bash", "moved Strike from Hand to Discard", "discarded Strike",
                    "moved Strike from Draw to Hand", "drew Strike", "moved Strike from Draw to Hand", "drew Strike"],
                tap.Seen, "discard, discard, then the two draws");
            c.Sequence(["Bash", "Strike"], f.DiscardPile.Select(card => card.Title), "the discard pile in the order discarded");

            var g = WithTap(out var quiet);
            g.DiscardAndDraw([], 3);
            c.That(quiet.Seen.Count == 0 && g.Hand.Count == 5, "with no card to discard nothing is drawn either");
        });

        c.Group("a card's own cost and its cost with powers are two numbers", () =>
        {
            var f = CheckKit.VsDummy();
            var strike = f.Hand[0];
            var bash = f.Hand[4];
            c.Equal(1, f.LocalCostOf(strike), "Strike: 1");
            bash.SetCostThisTurn(0);
            c.Equal(0, f.LocalCostOf(bash), "Bash set to 0 this turn: 0");
            f.ApplyPower("TangledPower", f.Player, 2, f.Enemy(0));
            c.Equal(1, f.LocalCostOf(strike), "Tangled does not touch the card's own cost");
            c.Equal(3, f.CostOf(strike), "but the cost to pay is 1 + 2");
            c.Equal(2, f.CostOf(bash), "and Bash's 0 + 2");
            var wound = f.Put("Wound", PileType.Hand);
            c.Equal(-1, f.LocalCostOf(wound), "a card without a cost reads -1");
        });

        c.Group("a potion used from a hook is used like any other: out of the belt, the potion hooks around its effect, a line in the history", () =>
        {
            var tap = new Tap();
            var f = new FightBuilder().DrawOrder(CheckKit.StarterInOrder).Relic(tap).Hp(4, 80)
                .Enemy(new CheckDummy { Hp = 300, Steps = [new DummyStep("HIT", Damage: 10)] }).Start();
            f.SetPotion(1, "FairyInABottle");
            tap.Seen.Clear();
            f.EndTurn();
            c.Sequence(["before Fairy in a Bottle on The Ironclad", "after Fairy in a Bottle, HP 24"],
                tap.Seen.Where(s => s.Contains("Fairy", StringComparison.Ordinal)), "both potion hooks, the heal in between");
            c.That(f.Potions[1] is null, "the slot is empty");
            c.Equal(1, f.History.Count(e => e.Kind == HistoryKind.PotionUsed), "one potion use in the history");
            f.UsePotionFromHook(0, f.Player);
            f.UsePotionFromHook(7, f.Player);
            c.Equal(1, f.History.Count(e => e.Kind == HistoryKind.PotionUsed), "an empty slot or one that does not exist does nothing");
        });

        c.Group("every attack has its own number, which a copy of the fight carries on from", () =>
        {
            var f = WithTap(out var tap);
            f.SetEnergy(4);
            f.Play("Strike", 0);
            f.Play("Strike", 0);
            var copy = f.Clone();
            f.Play("Bash", 0);
            c.Sequence(["attack 1", "attack 2", "attack 3"], tap.Seen.Where(s => s.StartsWith("attack", StringComparison.Ordinal)), "1, 2, 3");
            var probe = copy.Attack(copy.Player, 1).Targeting(copy.Enemy(0));
            c.Equal(3, probe.Id, "the copy's next attack is its own number 3");
        });

        c.Group("a copy for search players gives nothing away about the real draw order", () =>
        {
            // Two fights that show the same and hold the draw pile in opposite orders.
            var a = CheckKit.VsDummy();
            var b = a.Clone();
            b.ReverseDrawPile();
            c.Equal(a.Seen(), b.Seen(), "the two fights show the same snapshot");
            c.That(!a.DrawPile.Select(card => card.Title).SequenceEqual(b.DrawPile.Select(card => card.Title)), "and really differ in their draw order");
            for (var seed = 1; seed <= 12; seed++)
            {
                var fromA = a.CloneDeterminized(seed).DrawPile.Select(card => card.Id).ToList();
                var fromB = b.CloneDeterminized(seed).DrawPile.Select(card => card.Id).ToList();
                c.Sequence(fromA, fromB, $"seed {seed}: the same copy from both");
            }
            c.That(Enumerable.Range(1, 12).Select(seed => string.Join(",", a.CloneDeterminized(seed).DrawPile.Select(card => card.Id))).Distinct().Count() > 1,
                "different seeds do give different orders");

            // A card whose place the player knows (it was put on top) stays there.
            var known = CheckKit.VsDummy();
            known.Play("Defend");
            known.MoveCard(known.DiscardPile[0], PileType.Draw, PilePosition.Top);
            for (var seed = 1; seed <= 6; seed++)
                c.Equal("Defend", known.CloneDeterminized(seed).DrawPile[0].Title, $"seed {seed}: the card put on top is still on top");

            // In the middle of a card choice the copy forgets the future when the choice is answered.
            Fight MidChoice(bool reversed)
            {
                var f = CheckKit.VsDummy();
                if (reversed) f.ReverseDrawPile();
                f.Put("BurningPact", PileType.Hand);
                f.Play("Burning Pact");
                return f;
            }
            var x = MidChoice(false);
            var y = MidChoice(true);
            c.That(x.HasPendingChoice && y.HasPendingChoice, "Burning Pact asks which card to exhaust");
            for (var seed = 1; seed <= 6; seed++)
            {
                var fromX = x.CloneDeterminized(seed).Do("select:0", "confirm");
                var fromY = y.CloneDeterminized(seed).Do("select:0", "confirm");
                c.Sequence(fromX.HandTitles(), fromY.HandTitles(), $"seed {seed}: the two cards drawn after the choice are the same in both copies");
            }
        });

        c.Group("the audit hooks are not part of a fight: a copy does not carry them", () =>
        {
            var f = CheckKit.VsDummy();
            var heard = 0;
            f.DamageTraced = _ => heard++;
            f.HistoryTraced = _ => heard++;
            f.Clone().Play("Strike", 0);
            c.Equal(0, heard, "playing the copy tells the original's listeners nothing");
            f.Play("Strike", 0);
            c.That(heard > 0, "playing the original does");
        });
    }
}
