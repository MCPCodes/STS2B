namespace SpireMind.Arena;

/// <summary>
/// The Ironclad's three basic cards, and Armaments as the worked example of a card that asks
/// the player for a card from the hand. Numbers from starting-deck.json and cards-1.json.
/// </summary>
internal static class CoreCards
{
    public static void Register(Registry r)
    {
        // Strike: 6 damage (9 upgraded) to the chosen enemy. Carries the Strike tag.
        r.Card(new CardDef("StrikeIronclad", "Strike", CardType.Attack, CardRarity.Basic, 1, TargetType.AnyEnemy)
        {
            Tags = CardTags.Strike,
            Vars = { ["Damage"] = 6 },
            Upgrade = c => c.Add("Damage", 3),
            Text = t => $"Deal {t.Damage()} damage.",
            Play = c => c.Attack(c.V("Damage")).Execute(),
        });

        // Defend: 5 Block (8 upgraded). Carries the Defend tag.
        r.Card(new CardDef("DefendIronclad", "Defend", CardType.Skill, CardRarity.Basic, 1, TargetType.Self)
        {
            Tags = CardTags.Defend,
            Vars = { ["Block"] = 5 },
            Upgrade = c => c.Add("Block", 3),
            Text = t => $"Gain {t.Block()} Block.",
            Play = c => c.GainBlock(c.V("Block")),
        });

        // Bash: 8 damage (10), then 2 Vulnerable (3). The Vulnerable lands after the hit, so it does not raise it.
        r.Card(new CardDef("Bash", "Bash", CardType.Attack, CardRarity.Basic, 2, TargetType.AnyEnemy)
        {
            Vars = { ["Damage"] = 8, ["Vulnerable"] = 2 },
            Upgrade = c =>
            {
                c.Add("Damage", 2);
                c.Add("Vulnerable", 1);
            },
            Text = t => $"Deal {t.Damage()} damage. Apply {t.N("Vulnerable")} Vulnerable.",
            Play = c =>
            {
                c.Attack(c.V("Damage")).Execute();
                c.Apply<VulnerablePower>(c.Target, c.V("Vulnerable"));
            },
        });

        // Armaments: 5 Block, then upgrade one upgradable card in hand for the rest of the fight
        // (the player picks; with none or one candidate nothing is asked). Upgraded: every
        // upgradable card in hand, no choice; the Block stays 5. While it resolves the card sits
        // in the Play pile, so it never upgrades itself.
        r.Card(new CardDef("Armaments", "Armaments", CardType.Skill, CardRarity.Common, 1, TargetType.Self)
        {
            Vars = { ["Block"] = 5 },
            Text = t => t.Upgraded
                ? $"Gain {t.Block()} Block. Upgrade all cards in your hand for the rest of combat."
                : $"Gain {t.Block()} Block. Upgrade a card in your hand for the rest of combat.",
            Play = c =>
            {
                c.GainBlock(c.V("Block"));
                if (c.Upgraded)
                {
                    foreach (var card in c.F.Hand.Where(h => h.IsUpgradable).ToList()) c.F.Upgrade(card);
                    return;
                }
                if (c.F.ChooseHandCardToUpgrade() is { } chosen) c.F.Upgrade(chosen);
            },
        });
    }
}
