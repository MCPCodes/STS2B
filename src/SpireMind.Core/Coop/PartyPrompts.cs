using System.Globalization;

namespace SpireMind.Core.Coop;

/// <summary>
/// Every piece of text the bots' model or the human sees about the party's planning sessions
/// (<see cref="PartyConductor"/>, <see cref="PartyBot"/>). Placeholders in braces are filled by
/// <see cref="Fill"/>.
/// </summary>
public static class PartyPrompts
{
    // ---- What the model is asked on its turn (party.ask) ----

    public const string ActStartRound1 =
        "Planning session for act {act}, round 1 of {rounds}, your turn ({seconds} s). Your Ancient pick waits until the plan is set. " +
        "Look at your deck, relics, the Ancient's offers and the team, then call party_pitch once: the archetype you want, your role, " +
        "the cards and relics you will take, what you need from teammates and what you will give them. If you need Mark's view, put " +
        "one short question in say and in question. Keep say under 150 characters.";

    public const string ActStartRound2 =
        "Planning session for act {act}, final round, your turn ({seconds} s). Everyone's first pitch, the finals before yours and " +
        "Mark's lines are below. Call party_pitch once more with your final answer: keep or adjust your lane so the team covers damage, " +
        "block and debuffs without two decks chasing the same cards, and add party calls (who gets damage relics, Mend targets, route) " +
        "when you see one. {answer} Keep say under 150 characters.";

    /// <summary>{answer} in <see cref="ActStartRound2"/> when a bot asked and the human answered.</summary>
    public const string Answered = "Mark answered; his lines are below.";

    /// <summary>{answer} in <see cref="ActStartRound2"/> when a bot asked and the human didn't answer.</summary>
    public const string NotAnswered = "Mark didn't answer in time: go with your best guess and say what you guessed.";

    public const string Mid =
        "Mid-act check for act {act}, your turn ({seconds} s). The party plan is below. Call party_pitch once: keep your lane or adjust " +
        "it to what you were actually offered, say what you still need before the boss ({boss}), and add a call if the team should " +
        "change course. Keep say under 150 characters.";

    public const string Boss =
        "Boss plan for {boss}, your turn ({seconds} s). Call party_pitch once with calls for the fight: what you cover (debuffs, block " +
        "for others, the big hits), the potions you save for it, and who should be Mended or Smith at the rest site. Keep say under " +
        "150 characters.";

    /// <summary>The first round of a session the human started with /plan, which may come at any
    /// screen off combat, so it doesn't speak of the Ancient.</summary>
    public const string ManualRound1 =
        "Planning session (Mark asked for it), round 1 of {rounds}, your turn ({seconds} s). Your card, relic and shop picks wait " +
        "until the plan is set. Look at your deck, relics and the team, then call party_pitch once: the archetype you want, your " +
        "role, the cards and relics you will take, what you need from teammates and what you will give them. If you need Mark's " +
        "view, put one short question in say and in question. Keep say under 150 characters.";

    // ---- What the model is told after a session ----

    public const string Closed =
        "The party plan is set (below). Check your section and the party calls before every card, relic and shop pick; Mark's orders " +
        "still come first. If the Build plan in runs/current.md doesn't match your section, update it now with notes_write. Your held " +
        "pick is free.";

    public const string ClosedEarly =
        "Planning ended early ({reason}). Use the plan below as it stands, and your own Build plan where it says nothing. Your held pick is free.";

    /// <summary>What act and act_sequence say on a screen held for a session.</summary>
    public const string Held =
        "Held for the party's planning session ({key}, up to {n} s more): pick after the plan is set. Call wait_for_decision; it " +
        "returns at your turn to speak or when the plan is out.";

    /// <summary>The waiting text of a held snapshot.</summary>
    public const string HeldWaiting = "held for the party's planning session";

    /// <summary>party_pitch outside the bot's turn.</summary>
    public const string NotYourTurn = "Not your turn in the planning session; call wait_for_decision.";

    // ---- The human's chat ----

    public const string OpeningActStart =
        "Planning for act {act} (up to {cap}; the bots hold their Ancient picks). {name}tell them what you're building, or /plan skip.";

    public const string OpeningManual =
        "Planning now (up to {cap}; the bots hold their card, relic and shop picks). {name}tell them what you're building, or /plan skip.";

    public const string OpeningMid = "Mid-act check (up to {cap}). /plan skip ends it.";

    public const string OpeningBoss = "Boss plan for {boss} (up to {cap}). /plan skip ends it.";

    public const string TurnTimedOut = "{bot} didn't answer in time; going on.";

    public const string Skipped = "Planning skipped.";

    public const string SkippedReason = "Mark skipped it";

    public const string NothingToSkip = "No planning session is running.";

    public const string InFight = "Planning works off combat; try again on the map.";

    public const string AlreadyRunning = "A planning session is already running. /plan skip ends it.";

    public const string NoBots = "No bot can plan right now: none is playing in the run.";

    public const string AutoOn = "Automatic planning sessions are on: at each act start, mid-act and before the boss.";

    public const string AutoOff = "Automatic planning sessions are off. /plan still starts one.";

    public const string NoPlan = "No party plan yet. /plan starts a session.";

    public const string Usage = "Use /plan to start a planning session, /plan skip to end one, /plan show for the plan, /plan on or /plan off for the automatic ones.";

    /// <summary>The text with each {name} replaced by its value; placeholders without a value stay.</summary>
    public static string Fill(string text, params (string Name, object? Value)[] values)
    {
        foreach (var (name, value) in values)
            text = text.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", StringComparison.Ordinal);
        return text;
    }

    /// <summary>"2.5 minutes", "75 s", "2 minutes".</summary>
    public static string Span(TimeSpan span)
    {
        var seconds = Math.Max(1, (int)Math.Round(span.TotalSeconds));
        if (seconds < 120)
            return seconds.ToString(CultureInfo.InvariantCulture) + " s";
        var minutes = seconds / 60.0;
        return (minutes % 1 == 0 ? ((int)minutes).ToString(CultureInfo.InvariantCulture) : minutes.ToString("0.#", CultureInfo.InvariantCulture)) + " minutes";
    }
}
