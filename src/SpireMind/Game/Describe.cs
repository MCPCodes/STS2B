using System.Text.Json.Nodes;

namespace SpireMind.Game;

/// <summary>
/// Shared, compact JSON for game objects so every screen describes cards, creatures,
/// relics and potions the same way. Keep these small: they go into the model's context on
/// every decision.
/// </summary>
internal static class Describe
{
    /// <summary>Localized text from whatever the game hands us (string, LocString, or anything with a sensible ToString).</summary>
    public static string Text(object? value) => value switch
    {
        null => "",
        string s => TextCleaner.Clean(s),
        LocString loc => TextCleaner.Clean(loc.GetFormattedText()),
        _ => TextCleaner.Clean(value.ToString()),
    };

    // ---------- cards ----------

    /// <summary>
    /// A card as the player sees it. <paramref name="pile"/> picks which description variant
    /// the game renders (hand text shows live numbers including Strength, Vulnerable...).
    /// </summary>
    public static JsonObject Card(CardModel card, PileType pile, Creature? target = null)
    {
        var json = new JsonObject
        {
            // CardModel.Title already ends in "+" (or "+N" for multi-upgrade cards) when upgraded.
            ["name"] = Text(card.Title),
            ["cost"] = Cost(card),
            ["type"] = card.Type.ToString(),
        };

        var text = SafeText(() => card.GetDescriptionForPile(pile, target));
        if (text.Length > 0) json["text"] = text;

        if (card.Enchantment is { } ench)
            json["enchant"] = Text(ench.Title);

        return json;
    }

    /// <summary>A card's name for action labels. CardModel.Title already ends in "+" (or "+N") when upgraded.</summary>
    public static string CardName(CardModel card) => Text(card.Title);

    /// <summary>Card for reward/shop/deck lists, where rarity helps judge value.</summary>
    public static JsonObject CardOffer(CardModel card)
    {
        var json = Card(card, PileType.Deck);
        json["rarity"] = card.Rarity.ToString();
        return json;
    }

    /// <summary>"1", "0", "X", "-" (unplayable), plus "+2*" when it also costs stars.</summary>
    public static string Cost(CardModel card)
    {
        string energy;
        try
        {
            var cost = card.EnergyCost;
            energy = cost.CostsX ? "X" : cost.GetWithModifiers(CostModifiers.All) is var n && n >= 0 ? n.ToString() : "-";
        }
        catch
        {
            energy = "?";
        }

        try
        {
            if (card.HasStarCostX) return energy + "+X*";
            var stars = card.GetStarCostWithModifiers();
            if (stars > 0) return $"{energy}+{stars}*";
        }
        catch
        {
            // Not every card type supports star costs.
        }
        return energy;
    }

    /// <summary>Deck summary that stays small: name counts, e.g. {"Strike":4,"Defend":4,"Bash+":1}.</summary>
    public static JsonObject DeckCounts(IEnumerable<CardModel> cards)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in cards)
        {
            var name = Text(c.Title);
            counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        }
        var json = new JsonObject();
        foreach (var (name, n) in counts) json[name] = n;
        return json;
    }

    // ---------- creatures ----------

    public static JsonObject Creature(Creature c)
    {
        var json = new JsonObject
        {
            ["name"] = Text(c.Name),
            ["hp"] = c.CurrentHp,
            ["max_hp"] = c.MaxHp,
        };
        if (c.Block > 0) json["block"] = c.Block;
        var powers = Powers(c);
        if (powers.Count > 0) json["powers"] = powers;
        return json;
    }

    public static JsonArray Powers(Creature c)
    {
        var arr = new JsonArray();
        foreach (var p in c.Powers)
        {
            if (!p.IsVisible) continue;
            var entry = new JsonObject { ["id"] = p.Id.Entry };
            if (p.DisplayAmount is var amount && amount != 0) entry["amount"] = amount;
            var kind = p.TypeForCurrentAmount.ToString();
            if (kind.Length > 0 && kind != "None") entry["kind"] = kind;
            arr.Add(entry);
        }
        return arr;
    }

    /// <summary>
    /// Enemy intents against the local player: attacks with per-hit damage and hit count
    /// (already including Strength/Weak/Vulnerable), plus other intent kinds by name.
    /// </summary>
    public static JsonArray Intents(Creature enemy, ICombatState combat)
    {
        var arr = new JsonArray();
        var monster = enemy.Monster;
        if (monster is null) return arr;

        var move = monster.NextMove;
        if (move is null) return arr;
        if (move.Id == "STUNNED")
        {
            arr.Add("stunned");
            return arr;
        }

        var targets = combat.Players.Select(p => p.Creature).ToList();
        foreach (var intent in move.Intents)
        {
            if (intent is AttackIntent attack)
            {
                var perHit = attack.GetSingleDamage(targets, enemy);
                var hits = Math.Max(1, attack.Repeats);
                arr.Add(hits > 1 ? $"attack {perHit}x{hits}" : $"attack {perHit}");
            }
            else
            {
                var label = SafeText(() => intent.GetIntentLabel(targets, enemy).GetFormattedText());
                var kind = intent.IntentType.ToString().ToLowerInvariant();
                arr.Add(label.Length > 0 && !label.Equals(kind, StringComparison.OrdinalIgnoreCase) ? $"{kind}: {label}" : kind);
            }
        }
        return arr;
    }

    // ---------- items ----------

    public static JsonObject Relic(RelicModel relic) => new()
    {
        ["name"] = Text(relic.Title),
        ["text"] = SafeText(() => relic.DynamicDescription),
    };

    public static JsonObject Potion(PotionModel potion) => new()
    {
        ["name"] = Text(potion.Title),
        ["text"] = SafeText(() => potion.DynamicDescription),
        ["target"] = potion.TargetType.ToString(),
    };

    /// <summary>The player's run-level status: HP, gold, relics, potions and deck summary.</summary>
    public static JsonObject PlayerRun(Player me, bool includeDeck)
    {
        var json = new JsonObject
        {
            ["character"] = Text(me.Character?.Title),
            ["hp"] = me.Creature.CurrentHp,
            ["max_hp"] = me.Creature.MaxHp,
            ["gold"] = me.Gold,
        };

        var relics = new JsonArray();
        foreach (var r in me.Relics) relics.Add(Text(r.Title));
        json["relics"] = relics;

        var potions = new JsonArray();
        for (var i = 0; i < me.MaxPotionCount; i++)
        {
            var p = me.GetPotionAtSlotIndex(i);
            potions.Add(p is null ? null : Text(p.Title));
        }
        json["potions"] = potions;

        if (includeDeck)
        {
            json["deck_size"] = me.Deck.Cards.Count;
            json["deck"] = DeckCounts(me.Deck.Cards);
        }
        return json;
    }

    /// <summary>Act, floor and ascension for orientation.</summary>
    public static JsonObject RunInfo(RunState run) => new()
    {
        ["act"] = run.CurrentActIndex + 1,
        ["floor"] = run.TotalFloor,
        ["ascension"] = run.AscensionLevel,
    };

    /// <summary>Co-op teammates as short lines ("Silent 41/70 hp") for off-combat choices their HP bears on.</summary>
    public static JsonArray TeamHp(IEnumerable<Player> teammates)
    {
        var team = new JsonArray();
        foreach (var p in teammates)
            team.Add($"{Text(p.Character?.Title)} {p.Creature.CurrentHp}/{p.Creature.MaxHp} hp");
        return team;
    }

    // ---------- json helpers ----------

    /// <summary>
    /// A new object with <paramref name="key"/> first, then <paramref name="body"/>'s properties
    /// moved over (body is left empty), so ids read first in the model's context.
    /// </summary>
    public static JsonObject Lead(string key, JsonNode? value, JsonObject body)
    {
        var json = new JsonObject { [key] = value };
        foreach (var name in body.Select(kv => kv.Key).ToList())
        {
            var node = body[name];
            body.Remove(name);
            json[name] = node;
        }
        return json;
    }

    /// <summary>Text from a getter that may throw (some LocStrings need variables we can't supply); "" then.</summary>
    public static string SafeText(Func<object?> get)
    {
        try { return Text(get()); }
        catch { return ""; }
    }
}
