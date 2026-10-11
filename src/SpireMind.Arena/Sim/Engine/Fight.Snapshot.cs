using System.Globalization;
using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.Arena;

// What the model sees. Keys, their order, the wording of labels and what is left out follow
// the mod's readers (src/SpireMind/Game/Screens/CombatReader.cs, CardSelectReader.cs,
// PotionActions.cs and Describe.cs), because the model must meet the same snapshots here as in
// the game. Card, power, potion and relic texts are our own wording.
public sealed partial class Fight
{
    /// <summary>The snapshot of the fight as it stands: "combat", "hand_select", "card_select" or, after the end, "arena_over".</summary>
    public GameSnapshot Snapshot()
    {
        if (_s.Over) return OverSnapshot();
        if (_pending is { IsHand: true }) return HandSelectSnapshot();
        if (_pending is not null) return CardSelectSnapshot();

        var actions = new List<LegalAction>();
        var hand = new JsonArray();
        for (var i = 0; i < _s.Hand.Count; i++)
        {
            var options = PlayOptionsFor(i, _s.Hand[i], out var unplayable);
            foreach (var o in options) actions.Add(new LegalAction(o.Id, o.Label));

            var entry = Lead("i", i, HandCardJson(_s.Hand[i]));
            entry["playable"] = options.Count > 0;
            if (options.Count == 0 && unplayable is not null) entry["unplayable"] = unplayable;
            hand.Add(entry);
        }
        foreach (var o in PotionOptions()) actions.Add(new LegalAction(o.Id, o.Label));
        actions.Add(new LegalAction("end_turn", "End turn"));

        return new GameSnapshot { Screen = "combat", AwaitingDecision = true, State = BoardState(hand, mayAct: true), Actions = actions };
    }

    /// <summary>What the mod shows while an action resolves or the enemies act: four keys, no hand, no actions.</summary>
    public JsonObject WaitingState() => new()
    {
        ["turn"] = _s.Turn,
        ["round"] = _s.Round,
        ["hp"] = _s.Player.Hp,
        ["block"] = _s.Player.Block,
    };

    private GameSnapshot OverSnapshot()
    {
        var state = new JsonObject
        {
            ["result"] = _s.Won ? "won" : "lost",
            ["turn"] = _s.Turn,
            ["me"] = new JsonObject { ["name"] = _s.Player.Name, ["hp"] = _s.Player.Hp, ["max_hp"] = _s.Player.MaxHp },
        };
        if (_s.TimedOut) state["reason"] = string.Create(CultureInfo.InvariantCulture, $"not won within {_info.MaxTurns} turns");
        // A decision with nothing left to do, not a busy snapshot: told to wait, a model would wait on a finished fight for ever.
        return new GameSnapshot
        {
            Screen = OverScreen, AwaitingDecision = true, State = state,
            Actions = [new LegalAction(DoneAction, OverLabel())],
        };
    }

    // ---------- the board (CombatReader.BoardState) ----------

    private JsonObject BoardState(JsonArray hand, bool mayAct)
    {
        var state = new JsonObject
        {
            ["run"] = RunInfo(room: true),
            ["turn"] = _s.Turn,
            ["round"] = _s.Round,
            ["energy"] = _s.Energy,
            ["max_energy"] = MaxEnergy,
            ["me"] = CreatureJson(_s.Player),
            ["enemies"] = EnemiesJson(),
            ["incoming_damage"] = IncomingDamage(),
            ["hand"] = hand,
            // Counts by name only: the order of a pile is hidden, as it is from a human player.
            ["draw_pile"] = PileJson(_s.Draw),
            ["discard_pile"] = PileJson(_s.Discard),
            ["exhaust_count"] = _s.Exhaust.Count,
            ["potions"] = PotionsJson(mayAct),
        };
        var relics = new JsonArray();
        foreach (var relic in _s.Relics) relics.Add(relic.Name);
        state["relics"] = relics;
        return state;
    }

    private JsonObject RunInfo(bool room)
    {
        var run = new JsonObject { ["act"] = 1, ["floor"] = _info.Floor, ["ascension"] = 0 };
        if (room) run["room"] = _info.RoomType.ToString();
        return run;
    }

    private JsonObject CreatureJson(Creature c)
    {
        var json = new JsonObject { ["name"] = c.Name, ["hp"] = c.Hp, ["max_hp"] = c.MaxHp };
        if (c.Block > 0) json["block"] = c.Block;
        var powers = new JsonArray();
        foreach (var p in c.Powers)
        {
            if (!p.IsVisible) continue;
            var entry = new JsonObject { ["name"] = p.Name };
            if (p.DisplayAmount != 0) entry["amount"] = p.DisplayAmount;
            var kind = p.TypeForCurrentAmount;
            if (kind != PowerType.None) entry["kind"] = kind.ToString();
            var text = p.Describe();
            if (text.Length > 0) entry["text"] = text;
            powers.Add(entry);
        }
        if (powers.Count > 0) json["powers"] = powers;
        return json;
    }

    private JsonArray EnemiesJson()
    {
        var arr = new JsonArray();
        var living = _s.Roster.Where(InFight).ToList();
        // "primary" only says something when some living enemies are and some aren't.
        var mixed = living.Any(e => e.IsPrimaryEnemy) && living.Any(e => !e.IsPrimaryEnemy);
        for (var j = 0; j < _s.Roster.Count; j++)
        {
            var enemy = _s.Roster[j];
            if (!InFight(enemy)) continue;
            var json = Lead("i", j, CreatureJson(enemy));
            json["intents"] = IntentsJson(enemy);
            if (mixed && enemy.IsPrimaryEnemy) json["primary"] = true;
            if (!IsHittable(enemy)) json["untargetable"] = true;
            arr.Add(json);
        }
        return arr;
    }

    /// <summary>One attack intent's number per hit, as the game computes it: the full damage chain
    /// against the player, truncated, before Block (R-damage R22).</summary>
    public int IntentDamage(Creature enemy, Intent intent) =>
        (int)ModifyDamage(_s.Player, enemy, intent.Damage!(), Props.MonsterMove, null);

    /// <summary>The intents of the monster's next move, in the mod's words (R-damage R23).</summary>
    public IReadOnlyList<string> IntentTexts(Creature enemy)
    {
        var texts = new List<string>();
        var monster = enemy.Monster;
        if (monster?.NextMove is not { } move) return texts;
        if (monster.IsStunned)
        {
            texts.Add("stunned");
            return texts;
        }
        foreach (var intent in move.Intents)
        {
            if (intent.IsAttack)
            {
                var perHit = IntentDamage(enemy, intent);
                var hits = Math.Max(1, intent.Repeats!());
                texts.Add(hits > 1
                    ? string.Create(CultureInfo.InvariantCulture, $"attack {perHit}x{hits}")
                    : string.Create(CultureInfo.InvariantCulture, $"attack {perHit}"));
            }
            else
            {
                texts.Add(IntentWord(intent));
            }
        }
        return texts;
    }

    /// <summary>An intent that is not an attack, in the mod's words: its kind, and the game's label
    /// where that says more. Only the status intent has a label in the game: the bare card count
    /// ("statuscard: 2").</summary>
    internal static string IntentWord(Intent intent)
    {
        var kind = intent.Type.ToString().ToLowerInvariant();
        return intent.Type == IntentType.StatusCard ? string.Create(CultureInfo.InvariantCulture, $"{kind}: {intent.StatusCount}") : kind;
    }

    private JsonArray IntentsJson(Creature enemy)
    {
        var arr = new JsonArray();
        foreach (var text in IntentTexts(enemy)) arr.Add(text);
        return arr;
    }

    /// <summary>The sum of the attack intents of every living, un-stunned enemy: per hit times hits, before Block.</summary>
    public int IncomingDamage()
    {
        var total = 0;
        foreach (var enemy in _s.Roster)
        {
            if (!InFight(enemy) || enemy.Monster!.NextMove is not { } move || enemy.Monster.IsStunned) continue;
            foreach (var intent in move.Intents)
                if (intent.IsAttack) total += IntentDamage(enemy, intent) * intent.Repeats!();
        }
        return total;
    }

    private static JsonObject PileJson(List<CardInstance> pile)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in pile)
            counts[card.Title] = counts.TryGetValue(card.Title, out var n) ? n + 1 : 1;
        var cards = new JsonObject();
        foreach (var (name, n) in counts) cards[name] = n;
        return new JsonObject { ["count"] = pile.Count, ["cards"] = cards };
    }

    private JsonArray PotionsJson(bool mayAct)
    {
        var usable = mayAct ? PotionOptions().Where(o => !o.Discard).Select(o => o.Slot).ToHashSet() : new HashSet<int>();
        var slots = new JsonArray();
        for (var slot = 0; slot < _s.Potions.Length; slot++)
        {
            if (_s.Potions[slot] is not { } potion)
            {
                slots.Add((JsonNode?)null);
                continue;
            }
            slots.Add(new JsonObject
            {
                ["slot"] = slot, ["name"] = potion.Name, ["text"] = potion.Text(this), ["target"] = potion.Target.ToString(), ["usable"] = usable.Contains(slot),
            });
        }
        return slots;
    }

    // ---------- cards (Describe.Card) ----------

    /// <summary>A card as the mod describes it: name, cost, type and, when there is any, text.</summary>
    private JsonObject CardJson(CardInstance card, PileType pile) => DescribeCard(this, card, pile);

    /// <summary>
    /// A hand card as the mod's Describe.HandCard writes it: the card with its text at rest (the
    /// player's own modifiers, no target), plus "vs": for a card aimed at one enemy or at all of
    /// them, its text previewed against each enemy that can be targeted, by "e&lt;j&gt;" id, kept only
    /// where it differs from the text at rest (a Vulnerable enemy, for one). The preview stops
    /// where the game's does: no enemy Block, no HP-loss modifiers such as Slippery.
    /// </summary>
    private JsonObject HandCardJson(CardInstance card)
    {
        var json = CardJson(card, PileType.Hand);
        if (card.Target is not (TargetType.AnyEnemy or TargetType.AllEnemies)) return json;

        var atRest = (string?)json["text"] ?? "";
        JsonObject? vs = null;
        for (var j = 0; j < _s.Roster.Count; j++)
        {
            var enemy = _s.Roster[j];
            if (!IsTargetableEnemy(enemy)) continue;
            var text = CardText(this, card, PileType.Hand, enemy);
            if (text.Length > 0 && text != atRest) (vs ??= new JsonObject())[string.Create(CultureInfo.InvariantCulture, $"e{j}")] = text;
        }
        if (vs is not null) json["vs"] = vs;
        return json;
    }

    internal static JsonObject DescribeCard(Fight? f, CardInstance card, PileType pile)
    {
        var json = new JsonObject
        {
            ["name"] = card.Title,
            ["cost"] = f is not null ? f.CostText(card) : card.CostsX ? "X" : card.BaseCost >= 0 ? card.BaseCost.ToString(CultureInfo.InvariantCulture) : "-",
            ["type"] = card.Type.ToString(),
        };
        var text = CardText(f, card, pile);
        if (text.Length > 0) json["text"] = text;
        return json;
    }

    /// <summary>
    /// The card's text as the game composes it: the keywords that go first (in this order), the
    /// rules text, an affliction's line, a replay line, then Exhaust. Lines are joined with " / ",
    /// which is what the mod turns line breaks into.
    /// </summary>
    public static string CardText(Fight? f, CardInstance card, PileType pile) => CardText(f, card, pile, null);

    /// <summary>The same with the card's numbers previewed against <paramref name="target"/> (null: at rest),
    /// as the game shows them while the card is aimed at that enemy (rules-damage.md R24).</summary>
    public static string CardText(Fight? f, CardInstance card, PileType pile, Creature? target)
    {
        var parts = new List<string>(4);
        if (card.Has(CardKeywords.Unplayable)) parts.Add("Unplayable.");
        if (card.Has(CardKeywords.Innate)) parts.Add("Innate.");
        if (card.Has(CardKeywords.Retain) || card.RetainThisTurn) parts.Add("Retain.");
        if (card.Has(CardKeywords.Ethereal)) parts.Add("Ethereal.");

        var body = card.Def.Text?.Invoke(new TextContext(f, card, pile, target));
        if (!string.IsNullOrWhiteSpace(body)) parts.Add(body.Trim());

        if (card.Affliction is { } affliction && Content.Registry.Affliction(affliction).ExtraCardText is { } extra)
            parts.Add(extra(card.AfflictionAmount));
        if (card.ReplayCount > 0)
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"Played {card.ReplayCount} extra {(card.ReplayCount == 1 ? "time" : "times")}."));

        if (card.Has(CardKeywords.Exhaust)) parts.Add("Exhaust.");
        if (card.Has(CardKeywords.Eternal)) parts.Add("Eternal.");
        return string.Join(" / ", parts);
    }

    /// <summary>A new object with <paramref name="key"/> first, then the body's properties, so ids read first.</summary>
    private static JsonObject Lead(string key, JsonNode? value, JsonObject body)
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

    // ---------- the hand selection (CombatReader.CaptureHandSelect) ----------

    private GameSnapshot HandSelectSnapshot()
    {
        var p = _pending!;
        var actions = ChoiceActions();
        var selectable = actions.Where(a => a.Id.StartsWith("select:", StringComparison.Ordinal)).Select(a => a.Id).ToHashSet();

        var hand = new JsonArray();
        for (var i = 0; i < _s.Hand.Count; i++)
        {
            var card = _s.Hand[i];
            var entry = Lead("i", i, CardJson(card, PileType.Hand));
            var option = p.Options.IndexOf(card);
            if (option >= 0 && p.Selected.Contains(option)) entry["selected"] = true;
            else entry["selectable"] = selectable.Contains($"select:{i}");
            hand.Add(entry);
        }

        // Potions wait for the same gate as cards, and that gate is shut while a selection is open.
        var state = BoardState(hand, mayAct: false);
        var selection = new JsonObject { ["mode"] = p.Kind == ChoiceKind.HandUpgrade ? "upgrade" : "select" };
        if (p.Prompt is { Length: > 0 } prompt) selection["prompt"] = prompt;
        selection["min"] = p.Min;
        selection["max"] = p.Max;
        var names = new JsonArray();
        foreach (var i in p.Selected) names.Add(p.Options[i].Title);
        selection["selected"] = names;
        state["selection"] = selection;

        return new GameSnapshot { Screen = "hand_select", AwaitingDecision = true, State = state, Actions = actions };
    }

    // ---------- the card selection overlays (CardSelectReader) ----------

    private GameSnapshot CardSelectSnapshot()
    {
        var p = _pending!;
        var state = new JsonObject
        {
            ["kind"] = p.Kind switch
            {
                ChoiceKind.Choose => "choose",
                ChoiceKind.CombatPile => "combat_pile_select",
                _ => "select",
            },
        };
        if (p.Prompt is { Length: > 0 } prompt) state["prompt"] = prompt;
        state["min"] = p.Min;
        state["max"] = p.Max;

        var cards = new JsonArray();
        if (p.Kind == ChoiceKind.Choose)
        {
            for (var n = 0; n < p.Options.Count; n++) cards.Add(Lead("i", n, CardJson(p.Options[n], PileOrDeck(p.Options[n]))));
            state["cards"] = cards;
            state["can_skip"] = p.CanSkip;
        }
        else
        {
            var piles = p.Options.Select(c => c.Pile).Distinct().ToList();
            if (piles is [var only] && only != PileType.None) state["pile"] = only.ToString();
            if (p.Selected.Count > 0)
            {
                var names = new JsonArray();
                foreach (var i in p.Selected) names.Add(p.Options[i].Title);
                state["selected"] = names;
            }

            // Unselected identical copies are listed once, under the first copy's index, with "copies".
            var groups = new Dictionary<string, (JsonObject Entry, int Count)>(StringComparer.Ordinal);
            for (var n = 0; n < p.Options.Count; n++)
            {
                var card = p.Options[n];
                var desc = CardJson(card, PileOrDeck(card));
                if (piles.Count > 1 && card.Pile != PileType.None) desc["pile"] = card.Pile.ToString();
                if (p.Selected.Contains(n))
                {
                    var chosen = Lead("i", n, desc);
                    chosen["selected"] = true;
                    cards.Add(chosen);
                    continue;
                }
                var key = desc.ToJsonString();
                if (groups.TryGetValue(key, out var group))
                {
                    groups[key] = (group.Entry, group.Count + 1);
                    continue;
                }
                var entry = Lead("i", n, desc);
                groups[key] = (entry, 1);
                cards.Add(entry);
            }
            foreach (var (entry, count) in groups.Values)
                if (count > 1) entry["copies"] = count;
            state["cards"] = cards;
        }

        state["run"] = RunInfo(room: false);
        state["combat"] = CombatSummary();
        return new GameSnapshot { Screen = "card_select", AwaitingDecision = true, State = state, Actions = ChoiceActions() };
    }

    private static PileType PileOrDeck(CardInstance card) => card.Pile == PileType.None ? PileType.Deck : card.Pile;

    /// <summary>
    /// The small picture of the fight the card selection screens carry (CardSelectReader.CombatSummary):
    /// hand cards with their text but no "i" (no action here names a hand card), and the combat
    /// board's own enemy entries and incoming damage, so each enemy keeps its e&lt;j&gt; index.
    /// </summary>
    private JsonObject CombatSummary()
    {
        var hand = new JsonArray();
        foreach (var card in _s.Hand) hand.Add(CardJson(card, PileType.Hand));
        return new JsonObject
        {
            ["turn"] = _s.Turn,
            ["energy"] = _s.Energy,
            ["max_energy"] = MaxEnergy,
            ["me"] = CreatureJson(_s.Player),
            ["hand"] = hand,
            ["draw_count"] = _s.Draw.Count,
            ["discard_count"] = _s.Discard.Count,
            ["exhaust_count"] = _s.Exhaust.Count,
            ["enemies"] = EnemiesJson(),
            ["incoming_damage"] = IncomingDamage(),
        };
    }

    // ---------- get_deck (Describe.Deck) ----------

    /// <summary>The deck the fight started with, card by card in deck order with its text, plus relics and potions.</summary>
    public JsonObject DescribeDeck()
    {
        var deck = new JsonArray();
        foreach (var entry in _info.Deck)
        {
            var def = entry.Def;
            var card = new CardInstance(0, def);
            if (entry.Upgraded && card.IsUpgradable) card.ApplyUpgrade();
            var json = DescribeCard(null, card, PileType.Deck);
            json["rarity"] = def.Rarity.ToString();
            if (card.IsUpgraded) json["upgraded"] = true;
            deck.Add(json);
        }

        var relics = new JsonArray();
        foreach (var relic in _s.Relics) relics.Add(new JsonObject { ["name"] = relic.Name, ["text"] = relic.Describe() });

        // Filled slots only; the snapshots say which slot each potion is in.
        var potions = new JsonArray();
        foreach (var potion in _s.Potions)
            if (potion is not null)
                potions.Add(new JsonObject { ["name"] = potion.Name, ["text"] = potion.Text(this), ["target"] = potion.Target.ToString() });

        return new JsonObject
        {
            ["character"] = _s.Player.Name,
            ["hp"] = _s.Player.Hp,
            ["max_hp"] = _s.Player.MaxHp,
            ["gold"] = 0,
            ["run"] = RunInfo(room: false),
            ["deck_size"] = _info.Deck.Count,
            ["deck"] = deck,
            ["relics"] = relics,
            ["potions"] = potions,
        };
    }
}
