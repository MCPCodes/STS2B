using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SpireMind.Core;

namespace SpireMind.Arena;

/// <summary>
/// A sensible beginner, one card at a time with no plan for the turn: finish an enemy that can
/// be finished, otherwise block while the enemies' intents exceed its Block, otherwise hit the
/// weakest enemy with whatever does the most damage per energy, and spend what is left on
/// powers and free cards. It never sets up (Vulnerable before the big hit) and never spends HP.
/// <para>
/// Potions it uses the way a beginner does, without reading them: all of them as soon as it may
/// in an elite or boss fight, and in any other fight once it is at half its HP or less or the
/// enemies' intents would kill it this turn. One that needs an enemy goes to the toughest. A
/// potion that heals waits until it is at half HP. It never throws a potion away.
/// </para>
/// <para>
/// It knows exactly what a model is shown: the snapshot's numbers, the card texts and the
/// legal actions. What a card does is read from its text ("Deal 6 damage", "Gain 5 Block"),
/// so it plays any card the arena knows, and a card it cannot read is simply a card with no
/// damage and no Block to it.
/// </para>
/// </summary>
public sealed partial class GreedyPolicy : IPolicy
{
    public string Name => "Greedy";

    public string Choose(GameSnapshot snapshot)
    {
        var actions = snapshot.Actions;
        if (actions.Count == 0) return "end_turn";

        var choice = snapshot.Screen switch
        {
            "combat" => Combat(snapshot),
            "hand_select" => HandSelect(snapshot),
            "card_select" => CardSelect(snapshot),
            _ => null,
        };
        return choice is not null && actions.Any(a => a.Id == choice) ? choice : Fallback(actions);
    }

    /// <summary>A screen it has no rule for: whatever closes it, and never a potion or an action that undoes a pick.</summary>
    private static string Fallback(IReadOnlyList<LegalAction> actions)
    {
        foreach (var id in new[] { "end_turn", "confirm", "skip" })
            if (actions.Any(a => a.Id == id)) return id;
        return (actions.FirstOrDefault(a => !IsDeselect(a) && !IsPotion(a)) ?? actions[0]).Id;
    }

    private static bool IsDeselect(LegalAction action) => action.Label.StartsWith("Deselect", StringComparison.Ordinal);

    private static bool IsPotion(LegalAction action) =>
        action.Id.StartsWith("potion:", StringComparison.Ordinal) || action.Id.StartsWith("discard_potion:", StringComparison.Ordinal);

    // ---------- a fight ----------

    private static string Combat(GameSnapshot snapshot)
    {
        var state = snapshot.State;
        var legal = snapshot.Actions.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var energy = Int(state["energy"]);
        var block = Int((state["me"] as JsonObject)?["block"]);
        var incoming = Int(state["incoming_damage"]);

        var foes = new List<Foe>();
        foreach (var node in state["enemies"] as JsonArray ?? [])
            if (node is JsonObject enemy && Int(enemy["hp"]) > 0 && !Flag(enemy["untargetable"]))
                foes.Add(new Foe(Int(enemy["i"]), Int(enemy["hp"]) + Int(enemy["block"]), HasPower(enemy, "Vulnerable")));
        foes = foes.OrderBy(f => f.Toughness).ThenBy(f => f.Index).ToList();

        var hand = new List<HandCard>();
        foreach (var node in state["hand"] as JsonArray ?? [])
        {
            if (node is not JsonObject json || !Flag(json["playable"])) continue;
            var card = HandCard.Read(json);
            // A card that costs HP is never played; an X card with no energy does nothing.
            if (card.Facts.LosesHp || (card.Facts.CostsX && energy == 0)) continue;
            if (legal.Contains(card.Id) || foes.Any(f => legal.Contains(card.IdAt(f)))) hand.Add(card);
        }

        // 0. Potions, when the fight calls for them.
        if (Potion(snapshot, foes, incoming - block) is { } potion) return potion;

        // 1. Kill what can be killed this turn, weakest first: with the cheapest card that does it
        // alone, or else with as many cards as it takes, the best per energy first.
        foreach (var foe in foes)
        {
            var ranked = Ranked(hand, foe, foes.Count, energy, legal);
            var alone = ranked
                .Where(c => c.Facts.CostNow(energy) <= energy && c.DamageTo(foe, energy) >= foe.Toughness)
                .OrderBy(c => c.Facts.CostNow(energy))
                .ThenBy(c => c.DamageTo(foe, energy))
                .ThenBy(c => c.Index)
                .FirstOrDefault();
            if (alone is not null) return alone.ActionAt(foe, legal);

            var left = energy;
            var damage = 0;
            HandCard? first = null;
            foreach (var card in ranked)
            {
                var cost = card.Facts.CostNow(left);
                if (cost > left) continue;
                damage += card.DamageTo(foe, left);
                left -= cost;
                first ??= card;
            }
            if (first is not null && damage >= foe.Toughness) return first.ActionAt(foe, legal);
        }

        // 2. Block while more is coming than is blocked.
        if (incoming > block)
        {
            var guard = hand
                .Where(c => !c.Facts.IsPower && c.Facts.Block > 0)
                .OrderByDescending(c => PerEnergy(c.Facts.Block, c.Facts.CostNow(energy)))
                .ThenBy(c => c.Index)
                .FirstOrDefault();
            if (guard is not null && Play(guard, foes, legal) is { } id) return id;
        }

        // 3. Otherwise the most damage per energy into the weakest enemy it can reach; an attack
        // that picks its own target only when nothing can be aimed.
        foreach (var foe in foes)
            if (Ranked(hand, foe, foes.Count, energy, legal).FirstOrDefault() is { } best)
                return best.ActionAt(foe, legal);
        var unaimed = hand
            .Where(c => !c.Facts.IsPower && c.Facts.DamageNow(energy) > 0)
            .OrderByDescending(c => PerEnergy(c.Facts.DamageNow(energy), c.Facts.CostNow(energy)))
            .ThenBy(c => c.Index)
            .FirstOrDefault();
        if (unaimed is not null && Play(unaimed, foes, legal) is { } hit) return hit;

        // 4. Energy left over goes into powers first, then free cards, then anything else that is
        // neither an attack nor Block nobody asked for.
        var rest = hand
            .Where(c => c.Facts.IsPower || (c.Facts.Block == 0 && c.Facts.Damage == 0))
            .OrderBy(c => c.Facts.IsPower ? 0 : 1)
            .ThenBy(c => c.Facts.CostNow(energy))
            .ThenBy(c => c.Index);
        foreach (var card in rest)
            if (Play(card, foes, legal) is { } id) return id;

        return "end_turn";
    }

    /// <summary>
    /// The potion to use now, or null: the first usable one in belt order when the room is an
    /// elite's or a boss's, when HP is at half or less, or when what is coming through its Block
    /// would kill it. A healing potion is only wanted at half HP or less.
    /// </summary>
    private static string? Potion(GameSnapshot snapshot, List<Foe> foes, int comingThrough)
    {
        var state = snapshot.State;
        var me = state["me"] as JsonObject;
        var hp = Int(me?["hp"]);
        var hurt = hp * 2 <= Int(me?["max_hp"]);
        var room = Str((state["run"] as JsonObject)?["room"]);
        if (room is not ("Elite" or "Boss") && !hurt && comingThrough < hp) return null;

        foreach (var node in state["potions"] as JsonArray ?? [])
        {
            if (node is not JsonObject potion || !Flag(potion["usable"])) continue;
            if ((Str(potion["text"]) ?? "").StartsWith("Heal", StringComparison.Ordinal) && !hurt) continue;
            var id = string.Create(CultureInfo.InvariantCulture, $"potion:{Int(potion["slot"])}");
            if (snapshot.Actions.Any(a => a.Id == id)) return id;
            // One that is thrown at an enemy: the toughest it can be thrown at.
            foreach (var foe in foes.OrderByDescending(f => f.Toughness).ThenBy(f => f.Index))
            {
                var aimed = string.Create(CultureInfo.InvariantCulture, $"{id}>e{foe.Index}");
                if (snapshot.Actions.Any(a => a.Id == aimed)) return aimed;
            }
        }
        return null;
    }

    /// <param name="Toughness">HP plus Block: what an attack has to get through.</param>
    private sealed record Foe(int Index, int Toughness, bool Vulnerable);

    /// <param name="Facts">The card as its text reads at rest: the player's own modifiers, no target.</param>
    /// <param name="Versus">The card as it reads against one enemy, by the enemy's index, where the
    /// snapshot says that differs (its "vs" entries: a Vulnerable enemy, for one). Null when the
    /// snapshot gives none for this card.</param>
    private sealed record HandCard(int Index, CardFacts Facts, Dictionary<int, CardFacts>? Versus)
    {
        public static HandCard Read(JsonObject json)
        {
            Dictionary<int, CardFacts>? versus = null;
            if (json["vs"] is JsonObject vs)
            {
                versus = new Dictionary<int, CardFacts>();
                foreach (var (id, text) in vs)
                    if (GreedyPolicy.Index(id, "e") is { } enemy && Str(text) is { } against)
                        versus[enemy] = CardFacts.Read(json, against);
            }
            return new HandCard(Int(json["i"]), CardFacts.Read(json), versus);
        }

        public string Id => string.Create(CultureInfo.InvariantCulture, $"play:{Index}");

        public string IdAt(Foe foe) => string.Create(CultureInfo.InvariantCulture, $"play:{Index}>e{foe.Index}");

        /// <summary>The card's action aimed at <paramref name="foe"/>, or its untargeted one.</summary>
        public string ActionAt(Foe foe, HashSet<string> legal) => legal.Contains(IdAt(foe)) ? IdAt(foe) : Id;

        /// <summary>
        /// Damage to <paramref name="foe"/> before its Block. Where the snapshot previews the card
        /// against its targets, that is the number (and no entry means "as at rest"); where it
        /// does not, a Vulnerable target takes half as much again per hit, fraction dropped.
        /// </summary>
        public int DamageTo(Foe foe, int energy)
        {
            if (Versus is null) return Facts.DamageNow(energy, foe.Vulnerable);
            return (Versus.TryGetValue(foe.Index, out var against) ? against : Facts).DamageNow(energy);
        }
    }

    /// <summary>The card's action: untargeted if it has one, else aimed at the weakest enemy it can be aimed at.</summary>
    private static string? Play(HandCard card, List<Foe> foes, HashSet<string> legal)
    {
        if (legal.Contains(card.Id)) return card.Id;
        foreach (var foe in foes)
            if (legal.Contains(card.IdAt(foe))) return card.IdAt(foe);
        return null;
    }

    /// <summary>The attacks that can hurt <paramref name="foe"/>, most damage per energy first.</summary>
    private static List<HandCard> Ranked(List<HandCard> hand, Foe foe, int foeCount, int energy, HashSet<string> legal) =>
        hand
            .Where(c => !c.Facts.IsPower && c.DamageTo(foe, energy) > 0 && Reaches(c, foe, foeCount, legal))
            .OrderByDescending(c => PerEnergy(c.DamageTo(foe, energy), c.Facts.CostNow(energy)))
            .ThenBy(c => c.Index)
            .ToList();

    /// <summary>An aimed card reaches the enemy it may be aimed at; an unaimed one reaches every
    /// enemy, unless its text says its target is random and there is more than one.</summary>
    private static bool Reaches(HandCard card, Foe foe, int foeCount, HashSet<string> legal) =>
        legal.Contains(card.IdAt(foe)) || (legal.Contains(card.Id) && (!card.Facts.RandomTarget || foeCount == 1));

    /// <summary>Free cards come first, the bigger the better.</summary>
    private static double PerEnergy(int amount, int cost) => cost <= 0 ? 1000 + amount : (double)amount / cost;

    // ---------- what a card's snapshot entry says ----------

    /// <summary>
    /// A card as its snapshot entry describes it. Damage and Block are the numbers of the card's
    /// own sentences; a sentence about another moment ("At the end of your turn...", "Whenever
    /// you...") is not what playing the card does and is skipped. The hand text already carries
    /// the player's Strength, Weak, Dexterity and Frail.
    /// </summary>
    private sealed partial record CardFacts(string Name, string Type, int Cost, bool CostsX, string Text)
    {
        public int Damage { get; private init; }

        /// <summary>Hits of the attack; -1 for "X times".</summary>
        public int Hits { get; private init; } = 1;
        public int Block { get; private init; }
        public bool LosesHp { get; private init; }
        public bool RandomTarget { get; private init; }

        public bool IsPower => Type == "Power";
        public bool IsJunk => Type is "Status" or "Curse" || Text.Contains("Unplayable.", StringComparison.Ordinal);

        [GeneratedRegex(@"\bdeals? (\d+) damage\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex DamagePattern();

        /// <summary>A worked-out amount given in brackets: "Deal damage equal to your Block (7 right now)".</summary>
        [GeneratedRegex(@"\bdeals? damage\b[^(]*\((\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex WorkedOutDamagePattern();

        [GeneratedRegex(@"\b(\d+|X) times\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex TimesPattern();

        [GeneratedRegex(@"\bgain (\d+) Block\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex BlockPattern();

        [GeneratedRegex(@"\blose (\d+) HP\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HpLossPattern();

        private static readonly string[] OtherMoments = ["At the ", "Whenever ", "When ", "Each time ", "Every time ", "If ", "Next turn", "While "];

        /// <summary>Reads a card entry; <paramref name="textInstead"/> replaces its text (a "vs" preview).</summary>
        public static CardFacts Read(JsonObject card, string? textInstead = null)
        {
            var costText = Str(card["cost"]) ?? "";
            var costsX = costText == "X";
            var cost = int.TryParse(costText, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
            var text = textInstead ?? Str(card["text"]) ?? "";

            int damage = 0, hits = 1, block = 0;
            bool losesHp = false, random = false;
            // The mod joins a card's lines with " / "; sentences end in a full stop.
            foreach (var part in text.Split([" / ", ". "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (OtherMoments.Any(m => part.StartsWith(m, StringComparison.OrdinalIgnoreCase))) continue;
                var hit = DamagePattern().Match(part);
                if (!hit.Success) hit = WorkedOutDamagePattern().Match(part);
                if (damage == 0 && hit.Success)
                {
                    damage = Number(hit.Groups[1].Value);
                    if (part.Contains("twice", StringComparison.OrdinalIgnoreCase)) hits = 2;
                    else if (TimesPattern().Match(part) is { Success: true } times)
                        hits = times.Groups[1].Value is "X" or "x" ? -1 : Math.Max(1, Number(times.Groups[1].Value));
                    random = part.Contains("random", StringComparison.OrdinalIgnoreCase);
                }
                if (block == 0 && BlockPattern().Match(part) is { Success: true } gain) block = Number(gain.Groups[1].Value);
                if (HpLossPattern().IsMatch(part)) losesHp = true;
            }

            return new CardFacts(Str(card["name"]) ?? "", Str(card["type"]) ?? "", cost, costsX, text)
            {
                Damage = damage, Hits = hits, Block = block, LosesHp = losesHp, RandomTarget = random,
            };
        }

        private static int Number(string digits) =>
            int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

        /// <summary>Energy the card takes with <paramref name="energy"/> at hand: its cost, or everything for an X card.</summary>
        public int CostNow(int energy) => CostsX ? energy : Cost;

        /// <summary>All the damage the text promises one enemy, before its Block: per hit, times
        /// the hits (X is the energy at hand); half as much again per hit when asked to count Vulnerable.</summary>
        public int DamageNow(int energy, bool vulnerable = false)
        {
            var perHit = vulnerable ? Damage * 3 / 2 : Damage;
            var hits = Hits >= 0 ? Hits : CostsX ? energy : 1;
            return perHit * hits;
        }

        /// <summary>How much the card is worth keeping, for choices among cards: junk is worth less than nothing.</summary>
        public int Worth
        {
            get
            {
                if (IsJunk) return -100 + Math.Max(0, Cost);
                var use = Math.Max(Damage * Math.Max(1, Hits), Block);
                return 1 + use + 3 * (CostsX ? 2 : Cost) + (IsPower ? 6 : 0);
            }
        }
    }

    // ---------- choices among cards ----------

    /// <summary>Words of a prompt that say the chosen card is given up.</summary>
    private static readonly string[] GivingUp = ["exhaust", "discard", "remove", "lose", "destroy", "sacrifice"];

    /// <summary>One card that can be picked now, and the action that picks it.</summary>
    private sealed record Pick(string ActionId, CardFacts Facts);

    private static string? HandSelect(GameSnapshot snapshot)
    {
        var state = snapshot.State;
        var selection = state["selection"] as JsonObject;
        var cards = new Dictionary<int, CardFacts>();
        foreach (var node in state["hand"] as JsonArray ?? [])
            if (node is JsonObject card) cards[Int(card["i"])] = CardFacts.Read(card);

        var picks = new List<Pick>();
        foreach (var action in snapshot.Actions)
            if (Index(action.Id, "select:") is { } i && cards.TryGetValue(i, out var facts))
                picks.Add(new Pick(action.Id, facts));

        var keep = Str(selection?["mode"]) == "upgrade" || !GivesUp(Str(selection?["prompt"]));
        var chosen = (selection?["selected"] as JsonArray)?.Count ?? 0;
        return Decide(snapshot, picks, chosen, Int(selection?["min"]), Int(selection?["max"]), keep);
    }

    private static string? CardSelect(GameSnapshot snapshot)
    {
        var state = snapshot.State;
        var cards = new Dictionary<int, CardFacts>();
        var chosen = 0;
        foreach (var node in state["cards"] as JsonArray ?? [])
        {
            if (node is not JsonObject card) continue;
            cards[Int(card["i"])] = CardFacts.Read(card);
            if (Flag(card["selected"])) chosen++;
        }

        var picks = new List<Pick>();
        foreach (var action in snapshot.Actions)
            if (!IsDeselect(action) && Index(action.Id, "card:") is { } n && cards.TryGetValue(n, out var facts))
                picks.Add(new Pick(action.Id, facts));

        // One of the offered cards is taken ("choose"); the other screens say what happens to the pick.
        var keep = Str(state["kind"]) == "choose" || !GivesUp(Str(state["prompt"]));
        var min = Str(state["kind"]) == "choose" ? 1 : Int(state["min"]);
        return Decide(snapshot, picks, chosen, min, Math.Max(min, Int(state["max"])), keep);
    }

    private static bool GivesUp(string? prompt) =>
        prompt is not null && GivingUp.Any(word => prompt.Contains(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The next step of a choice. A card that is kept (upgraded, copied, taken into the hand) is
    /// the most valuable one; a card that is given up is the least valuable. Beyond the minimum it
    /// takes more of what it keeps, and gives up more only if it is junk. It never takes a pick
    /// back, so every step brings the choice closer to its end.
    /// </summary>
    private static string? Decide(GameSnapshot snapshot, List<Pick> picks, int chosen, int min, int max, bool keep)
    {
        var next = keep
            ? picks.OrderByDescending(p => p.Facts.Worth).FirstOrDefault()
            : picks.OrderBy(p => p.Facts.Worth).FirstOrDefault();
        var canConfirm = snapshot.Actions.Any(a => a.Id == "confirm");

        if (next is not null && chosen < min) return next.ActionId;
        if (canConfirm)
        {
            var more = next is not null && chosen < max && (keep || next.Facts.IsJunk);
            return more ? next!.ActionId : "confirm";
        }
        if (next is not null) return next.ActionId;
        return snapshot.Actions.Any(a => a.Id == "skip") ? "skip" : null;
    }

    // ---------- reading JSON without ever throwing ----------

    private static int Int(JsonNode? node) => node is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

    private static string? Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Flag(JsonNode? node) => node is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static bool HasPower(JsonObject creature, string name) =>
        (creature["powers"] as JsonArray ?? []).Any(p => p is JsonObject power && Str(power["name"]) == name);

    /// <summary>The number of "select:3", "card:3" or "e3"; null for anything else.</summary>
    private static int? Index(string id, string prefix) =>
        id.StartsWith(prefix, StringComparison.Ordinal)
        && int.TryParse(id.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
}
