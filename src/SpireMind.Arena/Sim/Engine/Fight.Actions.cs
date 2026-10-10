using System.Globalization;
using SpireMind.Core;

namespace SpireMind.Arena;

// Action ids, as the mod's readers offer and accept them (CLAUDE.md lists them):
// play:<handIndex>[>e<j>], potion:<slot>[>e<j>], discard_potion:<slot>, end_turn in a fight;
// select:<handIndex> and confirm on the hand selection; card:<n>, confirm, skip on the card
// selection; done once the fight is over.
public sealed partial class Fight
{
    public const string OverScreen = "arena_over";
    public const string DoneAction = "done";

    private sealed record Option(string Id, string Label, CardInstance? Card = null, Creature? Target = null, int Slot = -1, bool Discard = false);

    // ---------- what may be done now ----------

    private List<Option> PlayOptionsFor(int i, CardInstance card, out string? unplayable)
    {
        var options = new List<Option>();
        unplayable = null;
        if (!CanPlay(card, out var reason))
        {
            unplayable = (reason & UnplayableReason.EnergyCostTooHigh) != 0 ? "not enough energy" : reason.ToString();
            return options;
        }

        var name = card.Title;
        if (card.Target == TargetType.AnyEnemy)
        {
            for (var j = 0; j < _s.Roster.Count; j++)
            {
                var enemy = _s.Roster[j];
                if (IsTargetableEnemy(enemy) && IsValidTarget(card, enemy))
                    options.Add(new Option($"play:{i}>e{j}", $"Play {name} on {EnemyLabel(j)}", card, enemy));
            }
        }
        else if (card.Target != TargetType.AnyAlly && IsValidTarget(card, null))
        {
            // Self, AllEnemies, RandomEnemy, None: the game picks.
            options.Add(new Option($"play:{i}", $"Play {name}", card));
        }

        if (options.Count == 0) unplayable = "no valid target";
        return options;
    }

    private List<Option> PotionOptions()
    {
        var options = new List<Option>();
        if (_s.Player.IsDead) return options;
        for (var slot = 0; slot < _s.Potions.Length; slot++)
        {
            if (_s.Potions[slot] is not { } potion) continue;
            var id = $"potion:{slot}";
            if (potion.Usage is PotionUsage.CombatOnly or PotionUsage.AnyTime && (potion.CanUse?.Invoke(this) ?? true))
            {
                if (potion.Target == TargetType.AnyEnemy)
                {
                    for (var j = 0; j < _s.Roster.Count; j++)
                    {
                        var enemy = _s.Roster[j];
                        if (IsTargetableEnemy(enemy))
                            options.Add(new Option($"{id}>e{j}", $"Use {potion.Name} on {EnemyLabel(j)}", null, enemy, slot));
                    }
                }
                else if (potion.Target != TargetType.AnyAlly)
                {
                    // AllEnemies, RandomEnemy, Self, and AnyPlayer with nobody else to throw it at.
                    options.Add(new Option(id, $"Use {potion.Name}", null, null, slot));
                }
            }
            options.Add(new Option($"discard_potion:{slot}", $"Discard {potion.Name}", null, null, slot, Discard: true));
        }
        return options;
    }

    /// <summary>The enemy's name, with its id when another living enemy shares the name.</summary>
    private string EnemyLabel(int j)
    {
        var name = _s.Roster[j].Name;
        var twins = _s.Roster.Count(e => e.IsAlive && e.Name == name);
        return twins > 1 ? $"{name} (e{j})" : name;
    }

    private List<Option> CombatOptions()
    {
        var options = new List<Option>();
        for (var i = 0; i < _s.Hand.Count; i++) options.AddRange(PlayOptionsFor(i, _s.Hand[i], out _));
        options.AddRange(PotionOptions());
        options.Add(new Option("end_turn", "End turn"));
        return options;
    }

    /// <summary>Every action the player may take now, in the order the snapshot lists them.</summary>
    public IReadOnlyList<LegalAction> LegalActions()
    {
        if (_s.Over) return [new LegalAction(DoneAction, OverLabel())];
        if (_pending is not null) return ChoiceActions();
        return CombatOptions().Select(o => new LegalAction(o.Id, o.Label)).ToList();
    }

    private string OverLabel() => $"The fight is over: you {(_s.Won ? "won" : "lost")}. Nothing is left to do; stop here.";

    // ---------- doing it ----------

    /// <summary>
    /// Runs one action by id. An id that is not offered now is refused with a reason and changes
    /// nothing. After the fight, only "done" is accepted, and it changes nothing.
    /// </summary>
    public ActionResult Execute(string? actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return ActionResult.Fail("No action id was given.");
        if (_s.Over)
        {
            return actionId == DoneAction
                ? ActionResult.Success
                : ActionResult.Fail($"The fight is over; '{actionId}' cannot run. The only action left is '{DoneAction}'.");
        }
        if (_pending is not null) return ExecuteChoice(actionId);

        var option = CombatOptions().FirstOrDefault(o => o.Id == actionId);
        if (option is null)
        {
            return ActionResult.Fail(actionId.StartsWith("play:", StringComparison.Ordinal)
                ? $"'{actionId}' is not playable now."
                : actionId.StartsWith("potion:", StringComparison.Ordinal) || actionId.StartsWith("discard_potion:", StringComparison.Ordinal)
                    ? $"'{actionId}' is not available now."
                    : $"Unknown combat action '{actionId}'.");
        }

        _checkpoint = _s.Clone(out _);
        _answers = new List<int[]>();
        return Run(actionId);
    }

    /// <summary>Runs the pending action on the live state with the answers known so far.</summary>
    private ActionResult Run(string actionId)
    {
        _pending = null;
        _answerCursor = 0;
        _running = true;
        try
        {
            Perform(actionId);
            _checkpoint = null;
            _pendingAction = null;
            _answers = new List<int[]>();
            _rerandomize = new List<(int, int)>();
        }
        catch (ChoicePendingException)
        {
            // The state stays as it was when the question came up: that is what the selection screen shows.
            _pendingAction = actionId;
        }
        catch
        {
            // A rule threw: put the fight back as it was before the action, so it is still in one piece.
            if (_checkpoint is not null) _s = _checkpoint.Clone(out _);
            _checkpoint = null;
            _pendingAction = null;
            _pending = null;
            _answers = new List<int[]>();
            throw;
        }
        finally
        {
            _running = false;
        }
        return ActionResult.Success;
    }

    private const string ScriptedAction = "(scripted)";
    private Action<Fight>? _scripted;

    private void Perform(string actionId)
    {
        if (actionId == "end_turn")
        {
            EndPlayerTurn();
            return;
        }
        if (actionId == ScriptedAction)
        {
            _scripted!(this);
            CheckWinCondition();
            return;
        }

        // Ids are resolved against the state they were offered from (this also holds in a replay).
        var option = CombatOptions().First(o => o.Id == actionId);
        if (option.Discard) _s.Potions[option.Slot] = null;
        else if (option.Slot >= 0) UsePotion(option.Slot, option.Target);
        else PlayFromHand(option.Card!, option.Target);

        // R3: the win check after every finished player action other than "end turn".
        CheckWinCondition();
    }

    /// <summary>The player answered the open choice: start the action again from before it began, now with the answer.</summary>
    private ActionResult Resume(int[] answer)
    {
        var action = _pendingAction!;
        _answers.Add(answer);
        _s = _checkpoint!.Clone(out _);
        return Run(action);
    }

    // ---------- answering a choice ----------

    private List<LegalAction> ChoiceActions()
    {
        var p = _pending!;
        var actions = new List<LegalAction>();
        if (p.IsHand)
        {
            // As the hand reader: nothing more to select once the maximum is picked; confirm when the count fits.
            if (p.Selected.Count < p.Max)
            {
                for (var i = 0; i < _s.Hand.Count; i++)
                {
                    var option = p.Options.IndexOf(_s.Hand[i]);
                    if (option >= 0 && !p.Selected.Contains(option))
                        actions.Add(new LegalAction($"select:{i}", $"Select {_s.Hand[i].Title}"));
                }
            }
            if (p.Selected.Count >= p.Min && p.Selected.Count <= p.Max)
                actions.Add(new LegalAction("confirm", string.Create(CultureInfo.InvariantCulture, $"Confirm selection ({p.Selected.Count} chosen)")));
            return actions;
        }

        if (p.Kind == ChoiceKind.Choose)
        {
            for (var n = 0; n < p.Options.Count; n++) actions.Add(new LegalAction($"card:{n}", $"Take {p.Options[n].Title}"));
            if (p.CanSkip) actions.Add(new LegalAction("skip", "Skip"));
            return actions;
        }

        // Grid screens: identical unselected copies are offered once, under the first copy's index.
        var atMax = p.Selected.Count >= p.Max;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var n = 0; n < p.Options.Count; n++)
        {
            var selected = p.Selected.Contains(n);
            if (!selected && !seen.Add(CardJson(p.Options[n], p.Options[n].Pile).ToJsonString())) continue;
            if (selected) actions.Add(new LegalAction($"card:{n}", $"Deselect {p.Options[n].Title}"));
            else if (!atMax) actions.Add(new LegalAction($"card:{n}", $"Select {p.Options[n].Title}"));
        }
        if (GridConfirmReady(p))
        {
            var names = string.Join(", ", p.Selected.Select(i => p.Options[i].Title));
            actions.Add(new LegalAction("confirm", names.Length > 0 ? $"Confirm selection: {names}" : "Confirm with nothing selected"));
        }
        return actions;
    }

    private static bool GridConfirmReady(PendingChoice p) =>
        p.ManualConfirm && p.Selected.Count >= Math.Min(p.Min, p.Options.Count);

    private ActionResult ExecuteChoice(string actionId)
    {
        var p = _pending!;
        if (p.IsHand)
        {
            if (actionId == "confirm")
            {
                if (p.Selected.Count < p.Min || p.Selected.Count > p.Max) return ActionResult.Fail("The confirm button is not enabled yet.");
                return Resume(p.Selected.ToArray());
            }
            if (!TryIndex(actionId, "select:", out var i)) return ActionResult.Fail($"'{actionId}' is not a hand selection action.");
            var option = i >= 0 && i < _s.Hand.Count ? p.Options.IndexOf(_s.Hand[i]) : -1;
            if (option < 0 || p.Selected.Contains(option) || p.Selected.Count >= p.Max)
                return ActionResult.Fail($"'{actionId}' cannot be selected now.");
            p.Selected.Add(option);
            return ActionResult.Success;
        }

        if (p.Kind == ChoiceKind.Choose)
        {
            if (actionId == "skip")
                return p.CanSkip ? Resume([]) : ActionResult.Fail("This choice cannot be skipped now.");
            if (!TryIndex(actionId, "card:", out var n)) return ActionResult.Fail($"Unknown card choice action '{actionId}'.");
            if (n < 0 || n >= p.Options.Count) return ActionResult.Fail($"There is no card {n}.");
            return Resume([n]);
        }

        if (actionId == "confirm")
            return GridConfirmReady(p) ? Resume(p.Selected.ToArray()) : ActionResult.Fail("The confirm button is not enabled.");
        if (!TryIndex(actionId, "card:", out var pick)) return ActionResult.Fail($"Unknown card selection action '{actionId}'.");
        if (pick < 0 || pick >= p.Options.Count) return ActionResult.Fail($"There is no card {pick}.");

        if (p.Selected.Remove(pick)) return ActionResult.Success;
        if (p.Selected.Count >= p.Max) return ActionResult.Fail("The maximum is already selected; deselect a card first.");
        p.Selected.Add(pick);
        // Without a confirm button the screen closes by itself once enough is picked.
        if (!p.ManualConfirm && p.Selected.Count >= Math.Min(p.Max, p.Options.Count)) return Resume(p.Selected.ToArray());
        return ActionResult.Success;
    }

    private static bool TryIndex(string actionId, string prefix, out int n)
    {
        n = -1;
        return actionId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(actionId.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out n);
    }

    // ---------- copies ----------

    /// <summary>
    /// A deep copy of the fight as it stands, also in the middle of a card choice. Playing the
    /// copy never changes this fight. Both continue identically from the same actions.
    /// </summary>
    public Fight Clone() => new(this);

    /// <summary>
    /// A copy in which everything the player cannot know is replaced: the order of the draw pile
    /// (a card the player put on top stays there) and every random number still to come. What
    /// the player can see stays: the hand, the piles' contents, enemy HP and shown intents. For
    /// search players, so that they cannot peek at the future.
    /// </summary>
    public Fight CloneDeterminized(int seed)
    {
        var copy = new Fight(this);
        if (copy._pending is null) copy.Rerandomize(seed);
        // In the middle of an action the copy must first replay what already happened, with the
        // real numbers; it forgets the future at the point where the open question is answered.
        else copy._rerandomize.Add((copy._answers.Count, seed));
        return copy;
    }
}
