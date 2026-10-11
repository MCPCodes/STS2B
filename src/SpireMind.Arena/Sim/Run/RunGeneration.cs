namespace SpireMind.Arena;

/// <summary>
/// The rooms one act of a run will hand out, drawn when the run starts: the event list, the
/// hallway fights (the weak ones first), fifteen elites, the boss and the Ancient, all by class
/// name. The k-th Monster room of the act takes hallway entry k and the k-th Elite room elite
/// entry k; both wrap around at the end of their list. The counters say how far each list is used.
/// </summary>
public sealed class ActRooms
{
    public required string Act { get; init; }

    /// <summary>The act's own events and the shared ones in one shuffle. An Event room takes the next
    /// entry from <see cref="EventsVisited"/> on that is allowed now and was not met in this run;
    /// every entry it steps over moves the counter too, for good (the walk itself belongs to the
    /// event room). The Ancient's room at the start of the act counts as an event room
    /// (<see cref="MarkEventVisited"/>), so the first Event room of an act starts its walk at entry 1.</summary>
    public required List<string> Events { get; init; }

    public required List<string> NormalEncounters { get; init; }
    public required List<string> EliteEncounters { get; init; }
    public required string Boss { get; set; }

    /// <summary>Null only when the profile has not unlocked any Ancient for the act.</summary>
    public required string? Ancient { get; init; }

    public int EventsVisited { get; set; }
    public int NormalEncountersVisited { get; set; }
    public int EliteEncountersVisited { get; set; }
    public int BossEncountersVisited { get; set; }

    public string NextNormalEncounter => NormalEncounters[NormalEncountersVisited % NormalEncounters.Count];
    public string NextEliteEncounter => EliteEncounters[EliteEncountersVisited % EliteEncounters.Count];

    /// <summary>The event list's entry at the counter, wrapping; whether it may be used is for the caller to ask.</summary>
    public string NextEvent => Events[EventsVisited % Events.Count];

    /// <summary>A room of the type was entered as a map node's own room: its list moves on by one.
    /// A fight an event starts does not call this.</summary>
    public void MarkVisited(RoomType room)
    {
        switch (room)
        {
            case RoomType.Monster: NormalEncountersVisited++; break;
            case RoomType.Elite: EliteEncountersVisited++; break;
            case RoomType.Boss: BossEncountersVisited++; break;
        }
    }

    /// <summary>
    /// An event room was entered as a map node's own room: an Event room, an Unknown room that
    /// turned out to be one, and also the Ancient's room on the act's start node (Neow). The game
    /// counts every room of type Event here once it is entered, whichever event it holds: a save
    /// taken after Neow's pick shows the counter at 1 before any Event room was met.
    /// </summary>
    public void MarkEventVisited() => EventsVisited++;

    public ActRooms Clone() => new()
    {
        Act = Act,
        Events = Events.ToList(),
        NormalEncounters = NormalEncounters.ToList(),
        EliteEncounters = EliteEncounters.ToList(),
        Boss = Boss,
        Ancient = Ancient,
        EventsVisited = EventsVisited,
        NormalEncountersVisited = NormalEncountersVisited,
        EliteEncountersVisited = EliteEncountersVisited,
        BossEncountersVisited = BossEncountersVisited,
    };
}

/// <summary>What a run holds before its first room that the seed and the profile decide: its acts,
/// both relic bags and every act's rooms.</summary>
public sealed class RunStart
{
    public required RunSeed Seed { get; init; }

    /// <summary>The acts by class name, first act first.</summary>
    public required IReadOnlyList<string> Acts { get; init; }

    /// <summary>The whole shared pool. Chests pull from it; a relic the player gets by any way leaves it too.</summary>
    public required RelicBag SharedBag { get; init; }

    /// <summary>Shared pool plus the character's own relics, the four bag rarities. Elites, events and shops pull from it.</summary>
    public required RelicBag PlayerBag { get; init; }

    /// <summary>One entry per act, in act order.</summary>
    public required IReadOnlyList<ActRooms> Rooms { get; init; }

    /// <summary>How many numbers the up_front stream gave for all of this: what a fresh save writes under <c>rng.counters.up_front</c>.</summary>
    public required int UpFrontDraws { get; init; }
}

/// <summary>
/// The start of a run, in the game's order and on the game's streams (v0.107.1, single
/// player, ascension 0, standard mode): the act roll on <c>act_selection</c>, then on
/// <c>up_front</c> the shared relic bag, the player's bag, the shared Ancients and each act's
/// rooms. Five real saves are rebuilt from their seed by exactly this (RunStreamsChecks).
/// <para>
/// Left out: more than one player (one room less per act, a subset roll per player), the
/// second boss of ascension 10, run modifiers, and the rest of a profile's very first run
/// (the list order of that run is here; its missing Neow and scripted rewards are not).
/// </para>
/// </summary>
public static class RunGeneration
{
    /// <summary>The length of every act's elite list.</summary>
    public const int ElitesPerAct = 15;

    /// <summary>
    /// The acts of a run, first act first. One pick per act index on the <c>act_selection</c>
    /// stream among the acts of that index the profile has unlocked: also when there is only
    /// one, as for the second and third act. An unlocked act that is not a default one and
    /// that the profile has never entered is taken without a pick, so the first run after
    /// unlocking Underdocks is always Underdocks. With everything unlocked the first act is
    /// Overgrowth or Underdocks, half and half.
    /// </summary>
    public static List<string> RollActs(RunSeed seed, RunUnlocks unlocks)
    {
        var rng = RunStreams.ActSelection(seed);
        var acts = new List<string>();
        foreach (var options in GameLists.ActsByIndex)
        {
            GameAct? forced = null;
            var unlocked = new List<GameAct>();
            foreach (var act in options)
            {
                if (!unlocks.IsUnlocked(act)) continue;
                if (!act.IsDefault && !unlocks.DiscoveredActs.Contains(act.ClassName))
                {
                    forced = act;
                    break;
                }
                unlocked.Add(act);
            }
            var chosen = forced ?? rng.NextItem(unlocked)
                ?? throw new InvalidOperationException($"The profile has no unlocked act for act {acts.Count + 1}.");
            acts.Add(chosen.ClassName);
        }
        return acts;
    }

    /// <summary>A whole start from a seed: the act roll, then <see cref="Generate"/> on the streams' up_front stream.</summary>
    public static RunStart Start(RunStreams streams, RunUnlocks unlocks, string character = "Ironclad") =>
        Generate(streams, RollActs(streams.Seed, unlocks), unlocks, character);

    /// <summary>
    /// Everything the up_front stream makes, for acts already chosen. The stream must be
    /// untouched: this is the first thing that draws from it.
    /// </summary>
    public static RunStart Generate(RunStreams streams, IReadOnlyList<string> acts, RunUnlocks unlocks, string character = "Ironclad")
    {
        var rng = streams.Long(RunStream.UpFront);
        if (rng.Counter != 0) throw new InvalidOperationException("The start of a run must be the first thing drawn from up_front.");

        // 1. The shared bag: the whole unlocked shared pool, Event and Ancient rarity included.
        var sharedBag = new RelicBag(refreshAllowed: true);
        sharedBag.Populate(unlocks.Relics(GameLists.SharedRelics), rng);

        // 2. The player's bag.
        var playerBag = new RelicBag();
        playerBag.Populate(RelicBag.PlayerPool(unlocks, character), rng);

        // 3. The Ancients that belong to no act are shuffled, and each act after the first takes
        // a number of them off the front: one draw per such act, also with nothing left to take.
        var sharedAncients = GameLists.SharedAncients.Where(a => unlocks.IsRevealed(a.Epoch)).Select(a => a.ClassName).ToList();
        rng.Shuffle(sharedAncients);
        var dealt = new Dictionary<int, List<string>>();
        for (var i = 1; i < acts.Count; i++)
        {
            var count = rng.NextInt(sharedAncients.Count + 1);
            dealt[i] = sharedAncients.Take(count).ToList();
            sharedAncients = sharedAncients.Skip(count).ToList();
        }

        // 4. Every act's rooms, in act order.
        var rooms = new List<ActRooms>();
        for (var i = 0; i < acts.Count; i++)
        {
            var act = GameLists.Act(acts[i]);
            var actRooms = GenerateRooms(act, rng, unlocks, dealt.GetValueOrDefault(i) ?? []);
            ApplyDiscoveryOrder(act, actRooms, unlocks);
            rooms.Add(actRooms);
        }

        return new RunStart { Seed = streams.Seed, Acts = acts.ToList(), SharedBag = sharedBag, PlayerBag = playerBag, Rooms = rooms, UpFrontDraws = rng.Counter };
    }

    /// <summary>
    /// One act's rooms, in the game's order of draws: the events (the act's own, then the shared
    /// ones, without those whose epoch is not revealed) in one shuffle; the weak hallway
    /// fights; the regular ones up to the act's room count; fifteen elites; the boss, any of the
    /// act's with equal chance; the Ancient, any of the act's unlocked ones and of the shared
    /// ones dealt to this act.
    /// </summary>
    public static ActRooms GenerateRooms(GameAct act, GameRng rng, RunUnlocks unlocks, IReadOnlyList<string> sharedAncients)
    {
        var events = act.Events.Concat(GameLists.SharedEvents)
            .Where(e => !GameLists.EventEpochs.TryGetValue(e, out var epoch) || unlocks.IsRevealed(epoch)).ToList();
        rng.Shuffle(events);

        var normal = new List<GameEncounter>();
        Fill(normal, act.Weak.ToList(), act.WeakEncounters, rng);
        Fill(normal, act.Regular.ToList(), act.Rooms - act.WeakEncounters, rng);

        var elites = new List<GameEncounter>();
        Fill(elites, act.Elites.ToList(), ElitesPerAct, rng);

        var boss = rng.NextItem(act.Bosses.ToList()) ?? throw new InvalidOperationException($"{act.ClassName} has no boss.");
        var ancient = rng.NextItem(act.Ancients.Where(a => unlocks.IsRevealed(a.Epoch)).Select(a => a.ClassName).Concat(sharedAncients).ToList());

        return new ActRooms
        {
            Act = act.ClassName,
            Events = events,
            NormalEncounters = normal.Select(e => e.ClassName).ToList(),
            EliteEncounters = elites.Select(e => e.ClassName).ToList(),
            Boss = boss.ClassName,
            Ancient = ancient,
        };
    }

    // The game's grab bag: the pool's encounters with equal weights; each grab takes one out; the
    // bag is filled again, in the pool's order, only when it is empty. The bag is a new one for
    // every call, so the regular fights start from a full bag after the weak ones.
    private static void Fill(List<GameEncounter> list, IReadOnlyList<GameEncounter> pool, int count, GameRng rng)
    {
        var bag = new List<GameEncounter>();
        for (var i = 0; i < count; i++)
        {
            if (bag.Count == 0) bag.AddRange(pool);
            GrabWithoutRepeatingTags(list, bag, rng);
        }
    }

    // One grab. An entry fits when it is not the list's last entry and shares no tag with it
    // (the regular fights look at the last weak one). When some entry of the bag fits, numbers
    // are drawn until one lands on an entry that fits; when none fits, one number takes any.
    private static void GrabWithoutRepeatingTags(List<GameEncounter> list, List<GameEncounter> bag, GameRng rng)
    {
        if (bag.Count == 0) return;
        var last = list.Count > 0 ? list[^1] : null;
        bool Fits(GameEncounter e) => !e.SharesTagsWith(last) && !ReferenceEquals(e, last);

        int index;
        if (bag.Any(Fits))
        {
            do index = GrabIndex(bag.Count, rng);
            while (!Fits(bag[index]));
        }
        else
        {
            index = GrabIndex(bag.Count, rng);
        }
        list.Add(bag[index]);
        bag.RemoveAt(index);
    }

    // The bag's weights are all 1: the entry is the whole part of one number times the bag's size.
    private static int GrabIndex(int count, GameRng rng) => (int)(rng.NextDouble() * count);

    /// <summary>
    /// What a standard-mode run changes after the draws, without drawing: the boss is the first
    /// of the act's discovery order the profile has never met, when there is one; and on a
    /// profile's very first run Overgrowth's lists start in a fixed order.
    /// </summary>
    public static void ApplyDiscoveryOrder(GameAct act, ActRooms rooms, RunUnlocks unlocks)
    {
        foreach (var boss in act.BossDiscoveryOrder)
        {
            if (unlocks.HasSeen(boss)) continue;
            rooms.Boss = boss;
            break;
        }

        if (act.ClassName != GameLists.Overgrowth || unlocks.NumberOfRuns != 0) return;
        SwapToOrCreateAt(rooms.NormalEncounters, "NibbitsWeak", 0);
        SwapToOrCreateAt(rooms.NormalEncounters, "SlimesWeak", 1);
        SwapToOrCreateAt(rooms.NormalEncounters, "ShrinkerBeetleWeak", 2);
        SwapToOrCreateAt(rooms.NormalEncounters, "InkletsNormal", 3);
        SwapToOrCreateAt(rooms.NormalEncounters, "MawlerNormal", 4);
        SwapToOrCreateAt(rooms.NormalEncounters, "RubyRaidersNormal", 5);
        SwapToOrCreateAt(rooms.NormalEncounters, "NibbitsNormal", 6);
        SwapToOrCreateAt(rooms.Events, "ByrdonisNest", 0);
        SwapToOrCreateAt(rooms.Events, "SapphireSeed", 1);
        SwapToOrCreateAt(rooms.EliteEncounters, "ByrdonisElite", 0);
        SwapToOrCreateAt(rooms.EliteEncounters, "PhrogParasiteElite", 1);
    }

    // The first entry of that name changes places with the entry at the index; when the list
    // has no such entry, the entry at the index is replaced.
    private static void SwapToOrCreateAt(List<string> list, string name, int index)
    {
        var found = list.IndexOf(name);
        if (found >= 0) (list[index], list[found]) = (list[found], list[index]);
        else list[index] = name;
    }
}
