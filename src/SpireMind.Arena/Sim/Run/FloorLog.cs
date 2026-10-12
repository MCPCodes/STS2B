using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// One choice of a run: an accepted action outside a fight, or a potion used or thrown away
/// inside one, as the player met it (<see cref="Run.Choices"/>).
/// </summary>
/// <param name="Action">Its place in the run's action list (<see cref="Run.Actions"/>).</param>
/// <param name="Floor">The total floor the player stood on when it was taken. A path choice is taken on the floor it leaves.</param>
/// <param name="Screen">The screen it was taken on ("map", "rewards", "card_reward", "event", "shop", "rest", "treasure", "card_select", "combat", "game_over").</param>
/// <param name="Id">The action id ("map:2", "reward:0", "buy:card0").</param>
/// <param name="Label">The label the snapshot offered the id under ("Go to Monster at (5,1)", "Take Colossus").</param>
public sealed record RunChoice(int Action, int Floor, string Screen, string Id, string Label);

/// <summary>One fight of a floor, as the floor log keeps it.</summary>
/// <param name="Encounter">The encounter's class name.</param>
/// <param name="Room">Monster, Elite or Boss.</param>
/// <param name="Enemies">The monsters' names, joined by " + ".</param>
/// <param name="Turns">The player's turn number when it ended.</param>
/// <param name="HpBefore">HP on entering the fight.</param>
/// <param name="HpAfter">HP when it was over, with what the victory hooks healed.</param>
/// <param name="DamageTaken">All HP lost in the fight.</param>
/// <param name="Won">False for a death and for the arena's turn limit.</param>
/// <param name="TimedOut">Lost to the turn limit.</param>
public sealed record FightLine(string Encounter, string Room, string Enemies, int Turns, int HpBefore, int HpAfter, int DamageTaken, bool Won, bool TimedOut)
{
    internal static FightLine Of(FightOutcome fight) =>
        new(fight.Encounter, fight.RoomType.ToString(), fight.Enemies, fight.Turns, fight.HpBefore, fight.HpAfter, fight.DamageTaken, fight.Won, fight.TimedOut);
}

/// <summary>
/// One floor of a run as the floor log shows it: the room, the player on entering and on
/// leaving, the fights, every choice taken while standing on the floor, and what a roll on it
/// passed over because the arena has not built it.
/// </summary>
/// <param name="Floor">The total floor, from 1 (Neow's room).</param>
/// <param name="At">The map point, "(col,row)".</param>
/// <param name="Point">The kind of map point, in the map screen's words: ancient, monster, elite, unknown, shop, treasure, rest, boss.</param>
/// <param name="Room">The kind of room the point held (the game's room type): Event, Monster, Elite, Boss, Shop, Treasure, RestSite.</param>
/// <param name="What">The encounter's or the event's class name ("ShrinkerBeetleWeak", "Neow", "ThisOrThat"); null for a shop, a rest site and a treasure room.</param>
/// <param name="Title">The event's title, or the fight's enemies; null where <paramref name="What"/> is.</param>
/// <param name="HpIn">HP on entering the room (for Neow's room: before her heal).</param>
/// <param name="Finished">The room was left through its proceed or Leave. False for the room a run ended in, and for the room the player is in.</param>
/// <param name="HpOut">HP on leaving; for a room that was not left, HP as it stands.</param>
/// <param name="Fights">The fights of the floor, in order: the room's own, and any an event started. The last one of a run that ended in a fight is the lost one;
/// the last one of a run that ended as unrepresentable when a won fight's rewards were made is that won fight.</param>
/// <param name="Choices">Every choice taken on this floor, in order; the last one of a finished floor is the path choice that leaves it.</param>
/// <param name="DrawnAgain">What rolls on this floor landed on and passed over, as "kind:ClassName" ("relic:StoneCracker"). Empty while the floor is the game's own.</param>
public sealed record FloorLine(
    int Floor, string At, string Point, string Room, string? What, string? Title,
    int HpIn, int MaxHpIn, int GoldIn, int DeckIn,
    bool Finished, int HpOut, int MaxHpOut, int GoldOut, int DeckOut,
    IReadOnlyList<FightLine> Fights, IReadOnlyList<RunChoice> Choices, IReadOnlyList<string> DrawnAgain)
{
    /// <summary>The turns of the floor's fights together; 0 for a floor without one.</summary>
    public int Turns => Fights.Sum(fight => fight.Turns);

    /// <summary>The line as JSON, for a result line: the keys in the order of the parameters, lists left out when empty.</summary>
    public JsonObject ToJson()
    {
        var json = new JsonObject { ["floor"] = Floor, ["at"] = At, ["point"] = Point, ["room"] = Room };
        if (What is not null) json["what"] = What;
        if (Title is not null) json["title"] = Title;
        json["hp_in"] = HpIn;
        json["max_hp_in"] = MaxHpIn;
        json["gold_in"] = GoldIn;
        json["deck_in"] = DeckIn;
        json["finished"] = Finished;
        json["hp_out"] = HpOut;
        json["max_hp_out"] = MaxHpOut;
        json["gold_out"] = GoldOut;
        json["deck_out"] = DeckOut;
        if (Fights.Count > 0)
        {
            var fights = new JsonArray();
            foreach (var fight in Fights)
            {
                fights.Add(new JsonObject
                {
                    ["encounter"] = fight.Encounter, ["room"] = fight.Room, ["enemies"] = fight.Enemies, ["turns"] = fight.Turns,
                    ["hp_before"] = fight.HpBefore, ["hp_after"] = fight.HpAfter, ["damage_taken"] = fight.DamageTaken,
                    ["won"] = fight.Won, ["timed_out"] = fight.TimedOut,
                });
            }
            json["fights"] = fights;
        }
        if (Choices.Count > 0)
        {
            var choices = new JsonArray();
            foreach (var choice in Choices)
                choices.Add(new JsonObject { ["screen"] = choice.Screen, ["id"] = choice.Id, ["label"] = choice.Label });
            json["choices"] = choices;
        }
        if (DrawnAgain.Count > 0)
        {
            var passed = new JsonArray();
            foreach (var name in DrawnAgain) passed.Add(name);
            json["drawn_again"] = passed;
        }
        return json;
    }

    /// <summary>The line as text: the room, the player in and out, then the choices.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"floor {Floor,2}  {Point} {At}  {Room}");
        if (What is not null) text.Append(' ').Append(What);
        if (Fights.Count > 0)
            text.Append(" [").AppendJoin("; ", Fights.Select(fight => string.Create(CultureInfo.InvariantCulture,
                $"{fight.Enemies}, {fight.Turns} {(fight.Turns == 1 ? "turn" : "turns")}{(fight.Won ? "" : fight.TimedOut ? ", turn limit" : ", lost")}"))).Append(']');
        else if (Title is not null && Title != What) text.Append(" [").Append(Title).Append(']');
        text.Append(CultureInfo.InvariantCulture, $"  HP {HpIn}/{MaxHpIn} > {HpOut}/{MaxHpOut}  gold {GoldIn} > {GoldOut}  deck {DeckIn} > {DeckOut}");
        if (!Finished) text.Append("  (not left)");
        if (DrawnAgain.Count > 0) text.Append("  drawn again past ").AppendJoin(", ", DrawnAgain);
        if (Choices.Count > 0) text.Append("  | ").AppendJoin("; ", Choices.Select(choice => $"{choice.Id} {choice.Label}"));
        return text.ToString();
    }
}

/// <summary>
/// The floor log of a run: one entry per map point entered, written by the act's director
/// (<see cref="ActDirector"/>) as the run goes. It is a part of the run's state, so it is
/// copied with the run, undone with an action that is undone, and built again by a replay.
/// <code>
/// foreach (var line in FloorLog.Of(run)) Console.WriteLine(line);   // one line a floor
/// var json = FloorLog.ToJson(run);                                   // the same as a JSON array, for a result line
/// </code>
/// <para>
/// The part holds what only the director knows at the time: where the room was, what it held,
/// the player on entering and on leaving, and the fights that were won. <see cref="Of"/> joins
/// that with what the run keeps itself: the choices (<see cref="Run.Choices"/>), the fight a
/// run ended in (<see cref="Run.LastFight"/>: a lost one, or a won one whose rewards could not
/// be made), the player as it stands for a room that was not left, and everything a roll
/// passed over (<see cref="LeftOutLog"/>).
/// </para>
/// <para>
/// A run that ends as unrepresentable stands as it stood before the action that met what is
/// missing: its log ends on the floor the player was on, with that action as the last choice,
/// and the reason is the run's (<see cref="Run.ResultReason"/>).
/// </para>
/// </summary>
public sealed class FloorLog : RunPart
{
    /// <summary>One map point entered, as the director wrote it.</summary>
    public sealed class Entry
    {
        public required int Floor { get; init; }
        public required MapCoord Coord { get; init; }
        public required MapPointType Point { get; init; }
        public required RoomKind Room { get; init; }
        public string? What { get; init; }
        public string? Title { get; init; }
        public required int HpIn { get; init; }
        public required int MaxHpIn { get; init; }
        public required int GoldIn { get; init; }
        public required int DeckIn { get; init; }

        /// <summary>The fights won on this floor, in order.</summary>
        public IReadOnlyList<FightLine> Fights => FightList;
        internal List<FightLine> FightList { get; init; } = new();

        /// <summary>The room was left; the four values below are the player at that moment.</summary>
        public bool Finished { get; internal set; }
        public int HpOut { get; internal set; }
        public int MaxHpOut { get; internal set; }
        public int GoldOut { get; internal set; }
        public int DeckOut { get; internal set; }

        internal Entry Clone() => new()
        {
            Floor = Floor, Coord = Coord, Point = Point, Room = Room, What = What, Title = Title,
            HpIn = HpIn, MaxHpIn = MaxHpIn, GoldIn = GoldIn, DeckIn = DeckIn, FightList = FightList.ToList(),
            Finished = Finished, HpOut = HpOut, MaxHpOut = MaxHpOut, GoldOut = GoldOut, DeckOut = DeckOut,
        };
    }

    private List<Entry> _entries = new();

    /// <summary>The map points entered so far, in order.</summary>
    public IReadOnlyList<Entry> Entries => _entries;

    // ---------- written by the director ----------

    /// <summary>
    /// A map point was entered (<see cref="MapState.Enter"/> is done) and its room is about to
    /// be opened: the entry of the floor begins with the player as it stands.
    /// </summary>
    /// <param name="what">The encounter or event the room holds, by class name; null for a room that holds neither.</param>
    /// <param name="title">The event's title; null otherwise.</param>
    public static void Enter(Run run, MapArrival arrival, string? what = null, string? title = null)
    {
        var state = run.State;
        state.Part<FloorLog>()._entries.Add(new Entry
        {
            Floor = state.TotalFloor, Coord = arrival.Coord, Point = arrival.PointType, Room = arrival.Room, What = what, Title = title,
            HpIn = state.Hp, MaxHpIn = state.MaxHp, GoldIn = state.Gold, DeckIn = state.Deck.Count,
        });
    }

    /// <summary>A fight of the floor was won and has handed the player back.</summary>
    public static void FightWon(Run run, FightOutcome fight) => Last(run).FightList.Add(FightLine.Of(fight));

    /// <summary>The room of the floor is left (its proceed, or an event's Leave): the entry is closed with the player as it stands.</summary>
    public static void Leave(Run run)
    {
        var state = run.State;
        var entry = Last(run);
        entry.Finished = true;
        entry.HpOut = state.Hp;
        entry.MaxHpOut = state.MaxHp;
        entry.GoldOut = state.Gold;
        entry.DeckOut = state.Deck.Count;
    }

    private static Entry Last(Run run) =>
        run.State.FindPart<FloorLog>() is { _entries.Count: > 0 } log
            ? log._entries[^1]
            : throw new InvalidOperationException("The floor log has no floor yet: FloorLog.Enter comes first.");

    // ---------- read ----------

    /// <summary>
    /// The log of a run as it stands, one line per floor: what the director wrote, the run's
    /// choices by the floor they were taken on, the lost fight of a run that died in one, and
    /// what was drawn again. Empty for a run that never entered a map point (a seed that was
    /// refused at the start) and for a run of another director.
    /// </summary>
    public static IReadOnlyList<FloorLine> Of(Run run)
    {
        var state = run.State;
        var entries = state.FindPart<FloorLog>()?._entries ?? [];
        var passed = state.FindPart<LeftOutLog>()?.Entries ?? [];
        var lines = new List<FloorLine>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var fights = entry.Fights;
            // The run's last fight, when the director's entry does not hold it: a lost fight never reaches the director, and
            // a won one whose rewards met something unbuilt was taken back out of the entry with the rest of that step.
            if (i == entries.Count - 1 && run.LastFight is { } ended && ended.Floor == entry.Floor)
            {
                var line = FightLine.Of(ended);
                if (fights.Count == 0 || fights[^1] != line) fights = [.. fights, line];
            }
            var title = entry.Title ?? (fights.Count > 0 ? string.Join("; ", fights.Select(fight => fight.Enemies)) : null);
            lines.Add(new FloorLine(
                entry.Floor, entry.Coord.ToString(), MapScreen.TypeWord(entry.Point), entry.Room.ToString(), entry.What, title,
                entry.HpIn, entry.MaxHpIn, entry.GoldIn, entry.DeckIn,
                entry.Finished,
                // A room that was not left: the player as it stands (the fight's own numbers while one is on).
                entry.Finished ? entry.HpOut : run.Hp, entry.Finished ? entry.MaxHpOut : run.MaxHp,
                entry.Finished ? entry.GoldOut : run.Gold, entry.Finished ? entry.DeckOut : run.Deck.Count,
                fights,
                run.Choices.Where(choice => choice.Floor == entry.Floor).ToList(),
                passed.Where(p => p.Floor == entry.Floor).Select(p => $"{p.Kind}:{p.ClassName}").ToList()));
        }
        return lines;
    }

    /// <summary>The log as a JSON array, one object per floor (<see cref="FloorLine.ToJson"/>).</summary>
    public static JsonArray ToJson(Run run)
    {
        var array = new JsonArray();
        foreach (var line in Of(run)) array.Add(line.ToJson());
        return array;
    }

    /// <summary>The log as text, one line per floor.</summary>
    public static string Describe(Run run) => string.Join("\n", Of(run));

    // ---------- copies and dumps ----------

    public override RunPart Clone(CloneMap map) => new FloorLog { _entries = _entries.Select(entry => entry.Clone()).ToList() };

    public override JsonNode Dump()
    {
        var floors = new JsonArray();
        foreach (var e in _entries)
        {
            floors.Add(string.Create(CultureInfo.InvariantCulture,
                $"{e.Floor}:{e.Point}@{e.Coord}:{e.Room}:{e.What}:{e.HpIn}/{e.MaxHpIn},{e.GoldIn},{e.DeckIn}>{(e.Finished ? $"{e.HpOut}/{e.MaxHpOut},{e.GoldOut},{e.DeckOut}" : "-")}:{string.Join("+", e.Fights.Select(f => $"{f.Encounter}#{f.Turns}"))}"));
        }
        return new JsonObject { ["floors"] = floors };
    }
}
