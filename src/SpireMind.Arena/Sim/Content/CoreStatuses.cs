using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// The 12 status cards of cards-3.json (rules-turns.md R34 has the end-of-turn ones). None can
/// be upgraded. Overgrowth monsters add Slimed, Dazed, Wound and Infection; the others are
/// here because the card pool names them.
/// </summary>
internal static class CoreStatuses
{
    /// <summary>End of turn in hand: the card hurts its holder with the holder as dealer and the
    /// card as source. Blockable and not modified (Burn, Infection, Wither, Toxic).</summary>
    private static void BlockableDamage(Fight f, CardInstance card) =>
        f.Damage(f.Player, card.Get("Damage"), card.VarDef("Damage").Props, f.Player, card);

    public static void Register(Registry r)
    {
        // Beckon: cost 1, playable, does nothing when played and goes to the discard pile.
        // In hand at end of turn: lose 6 HP (ignores Block).
        r.Card(new CardDef("Beckon", "Beckon", CardType.Status, CardRarity.Status, 1, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Vars = { ["HpLoss"] = 6 },
            Text = t => $"At the end of your turn, if this is in your hand, lose {t.N("HpLoss")} HP.",
            TurnEndInHand = (f, card) => f.Damage(f.Player, card.Get("HpLoss"), Props.CardHpLoss, f.Player, card),
        });

        // Burn: unplayable. In hand at end of turn: 2 damage, blockable, not modified.
        r.Card(new CardDef("Burn", "Burn", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Damage"] = Var.Damage(2, Props.CardUnpowered) },
            Text = t => $"At the end of your turn, if this is in your hand, take {t.Damage()} damage.",
            TurnEndInHand = BlockableDamage,
        });

        // Dazed: unplayable, Ethereal. Nothing else.
        r.Card(new CardDef("Dazed", "Dazed", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable | CardKeywords.Ethereal,
        });

        // Debris: cost 1, no effect, Exhaust.
        r.Card(new CardDef("Debris", "Debris", CardType.Status, CardRarity.Status, 1, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Exhaust,
        });

        // Frantic Escape: cost 1; adds 1 to the Sandpit an enemy holds against this player, if any,
        // then costs 1 more for the rest of the fight. No enemy of Overgrowth has Sandpit.
        r.Card(new CardDef("FranticEscape", "Frantic Escape", CardType.Status, CardRarity.Status, 1, TargetType.Self)
        {
            MaxUpgradeLevel = 0,
            CanBeGeneratedInCombat = false,
            Text = _ => "Add 1 to the Sandpit counting down on you, if there is one. Costs 1 more each time it is played.",
            Play = c =>
            {
                var enemy = c.F.Enemies.FirstOrDefault(e => e.HasPower("SandpitPower"));
                var sandpit = enemy?.Powers.FirstOrDefault(p => p.ClassName == "SandpitPower" && p is ITargetedPower t && ReferenceEquals(t.Target, c.Owner));
                if (sandpit is not null) c.F.ModifyPowerAmount(sandpit, 1m, enemy, c.Card);
                c.Card.AddCostThisCombat(1);
            },
        });

        // Infection: unplayable. In hand at end of turn: 3 damage, blockable, not modified.
        r.Card(new CardDef("Infection", "Infection", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Damage"] = Var.Damage(3, Props.CardUnpowered) },
            Text = t => $"At the end of your turn, if this is in your hand, take {t.Damage()} damage.",
            TurnEndInHand = BlockableDamage,
        });

        // Wither: unplayable. In hand at end of turn: 3 damage, plus 3 per fake upgrade its source
        // monster gave it (see FakeUpgradeWither); the title then reads "Wither+N".
        r.Card(new CardDef("Wither", "Wither", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
            Vars = { ["Damage"] = Var.Damage(3, Props.CardUnpowered), ["FakeUpgrades"] = 0 },
            Title = card => card.Get("FakeUpgrades") > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Wither+{(int)card.Get("FakeUpgrades")}")
                : "Wither",
            Text = t => $"At the end of your turn, if this is in your hand, take {t.Damage()} damage.",
            TurnEndInHand = BlockableDamage,
        });

        // Slimed: cost 1, draw 1 card, Exhaust. (Unlike the first game it draws.)
        r.Card(new CardDef("Slimed", "Slimed", CardType.Status, CardRarity.Status, 1, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Cards"] = 1 },
            Text = t => $"Draw {TextContext.Count(t.N("Cards"), "card")}.",
            Play = c => c.Draw(c.V("Cards")),
        });

        // Soot: unplayable. Nothing else.
        r.Card(new CardDef("Soot", "Soot", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            CanBeGeneratedInCombat = false,
            Keywords = CardKeywords.Unplayable,
        });

        // Toxic: cost 1, no effect when played, Exhaust. In hand at end of turn: 5 damage,
        // blockable, not modified; it then goes to the discard pile, so it comes back.
        r.Card(new CardDef("Toxic", "Toxic", CardType.Status, CardRarity.Status, 1, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Exhaust,
            Vars = { ["Damage"] = Var.Damage(5, Props.CardUnpowered) },
            Text = t => $"At the end of your turn, if this is in your hand, take {t.Damage()} damage.",
            TurnEndInHand = BlockableDamage,
        });

        // Void: unplayable, Ethereal. When drawn (any draw, not other ways into the hand): lose 1 energy.
        r.Card(new CardDef("Void", "Void", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable | CardKeywords.Ethereal,
            Vars = { ["Energy"] = 1 },
            Text = t => $"When drawn, lose {t.N("Energy")} energy.",
            Behaviour = () => new VoidBehaviour(),
        });

        // Wound: unplayable. Nothing else.
        r.Card(new CardDef("Wound", "Wound", CardType.Status, CardRarity.Status, CardDef.NoCost, TargetType.None)
        {
            MaxUpgradeLevel = 0,
            Keywords = CardKeywords.Unplayable,
        });
    }

    /// <summary>What the Aeonglass monster does to a Wither: +3 damage and one more "+" in the title.</summary>
    public static void FakeUpgradeWither(CardInstance wither)
    {
        wither.Add("FakeUpgrades", 1);
        wither.Add("Damage", 3);
    }

    private sealed class VoidBehaviour : CardBehaviour
    {
        public override void AfterCardDrawn(Fight f, CardInstance card, bool fromHandDraw)
        {
            if (ReferenceEquals(card, Card)) f.LoseEnergy(Card.Get("Energy"));
        }
    }
}
