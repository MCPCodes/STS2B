using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace SpireMind.Arena;

/// <summary>
/// <c>arena run-baseline</c>: the scripted run players play whole first acts, from Neow's room
/// to the proceed of the boss's rewards, which costs nothing. It is what <c>baseline</c> is for
/// single fights: it shows what the acts are and how hard, and it is the place a refused action,
/// a run that hangs or a failure of the simulator shows first. In strict mode it measures the
/// share of real runs the build can represent (plan 5.1 and 5.3).
/// <para>
/// It reads its own options, as <c>values</c> does. Nothing is written unless <c>--out</c>
/// names a folder: then every run's save (the action list) and one result line per run with the
/// floor log. A save that is already there is carried on, so the same command goes on after a stop.
/// </para>
/// </summary>
public static class RunBaselineCommand
{
    public const string Usage =
        "  scripts/arena.sh run-baseline [--runs N=20] [--seed S=1] [--seeds A,B,...] [--mode draw-again|screened|strict]\n" +
        "                       [--screen default|act|full|neow,events=N,bags=N,shop] [--streams game|keyed]\n" +
        "                       [--players Walker,Plain] [--threads K=1] [--out DIR] [--floors] [--look] [--tries T=200000]\n" +
        "      Let the scripted run players play N whole first acts (Neow to the boss's proceed) and print how each\n" +
        "      run ended and the totals by player. Costs nothing. The players: Walker (every choice outside a fight\n" +
        "      at random, Greedy in fights) and Plain (the first path, every reward, the first card, rest below half\n" +
        "      HP, smith otherwise, buy nothing; Lookahead in fights, about ten seconds an act); a fight player can be\n" +
        "      named after a colon (Walker:random, Plain:greedy, Walker:lookahead).\n" +
        "      --mode says what happens when a roll lands on something the arena has not built. draw-again (the\n" +
        "      default): it is drawn again and logged; seeds whose first act is Overgrowth. screened: the same, on\n" +
        "      seeds that pass --screen, so that a run starts as the game's own run for its seed. strict: nothing is\n" +
        "      drawn again and such a run ends as unrepresentable; seeds as they come, half of them Underdocks: the\n" +
        "      share of runs that end cleared or dead is the share of real runs the build can represent.\n" +
        "      --seed makes the seeds (the same number, the same seeds); --seeds names them instead (1BUSLN0FY5).\n" +
        "      --streams keyed gives every roll a stream of its own place and purpose (draw-again only): the house\n" +
        "      rule for comparing players. --threads plays K runs at once (at most 8). --floors prints every run's\n" +
        "      floor log. --look plays nothing: it prints what each seed fixes (act, Neow's offer, the first events,\n" +
        "      the ends of the relic bags, the first shop's colorless cards) and whether it passes --screen.\n" +
        "      --out DIR keeps every run's save (DIR/actions/<player>/<seed>.txt, carried on when it is there) and\n" +
        "      writes DIR/runs.jsonl, one line per run with its floor log. --tries is how many seeds are looked at\n" +
        "      for a screen before giving up. Ends with 1 when an action was refused, a run hung, the simulator\n" +
        "      failed or no seed was found.";

    public const int DefaultRuns = 20;

    /// <summary>How many seeds are looked at for the ones a mode can use before the search gives up.</summary>
    public const int DefaultTries = 200_000;
    public const int MostRuns = 100_000;
    public const int MostThreads = 8;

    /// <summary>What the command line asked for.</summary>
    public sealed record Options
    {
        public int Runs { get; init; } = DefaultRuns;
        public int Seed { get; init; } = 1;
        public IReadOnlyList<string> Seeds { get; init; } = [];
        public RunMode Mode { get; init; } = RunMode.DrawAgain;
        public SeedScreen Screen { get; init; } = SeedScreen.Default;
        public StreamMode Streams { get; init; } = StreamMode.Game;
        public IReadOnlyList<string> Players { get; init; } = RunPlayers.Default;
        public int Threads { get; init; } = 1;
        public string? Out { get; init; }
        public bool Floors { get; init; }
        public bool Look { get; init; }
        public int Tries { get; init; } = DefaultTries;

        /// <summary>Reads the arguments after the command's name.</summary>
        /// <exception cref="UsageException">An unknown option, a missing or bad value, or options that do not go together.</exception>
        public static Options Parse(IReadOnlyList<string> args)
        {
            var o = new Options();
            var given = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < args.Count; i++)
            {
                var name = args[i];
                given.Add(name);
                string Value() => i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : throw new UsageException($"{name} needs a value.");
                int Whole(int min, int max)
                {
                    var text = Value();
                    return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
                        ? n
                        : throw new UsageException(max == int.MaxValue ? $"{name} takes a whole number from {min} up, not '{text}'." : $"{name} takes a whole number from {min} to {max}, not '{text}'.");
                }
                switch (name)
                {
                    case "--runs": o = o with { Runs = Whole(1, MostRuns) }; break;
                    case "--seed": o = o with { Seed = Whole(0, int.MaxValue) }; break;
                    case "--threads": o = o with { Threads = Whole(1, MostThreads) }; break;
                    case "--out": o = o with { Out = Value() }; break;
                    case "--floors": o = o with { Floors = true }; break;
                    case "--look": o = o with { Look = true }; break;
                    case "--tries": o = o with { Tries = Whole(1, 100_000_000) }; break;
                    case "--seeds":
                        var seeds = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(RunSeed.Canonical).Distinct(StringComparer.Ordinal).ToList();
                        if (seeds.Count == 0) throw new UsageException("--seeds takes run seeds separated by commas, e.g. --seeds 1BUSLN0FY5,KMEHXZW0R5.");
                        if (seeds.FirstOrDefault(seed => seed.Any(c => !RunSeed.Alphabet.Contains(c))) is { } odd)
                            throw new UsageException($"'{odd}' is no run seed: a seed is letters and digits, as the game shows it.");
                        o = o with { Seeds = seeds };
                        break;
                    case "--mode":
                        var mode = Value();
                        o = o with
                        {
                            Mode = mode switch
                            {
                                "draw-again" => RunMode.DrawAgain,
                                "screened" => RunMode.Screened,
                                "strict" => RunMode.Strict,
                                _ => throw new UsageException($"--mode takes draw-again, screened or strict, not '{mode}'."),
                            },
                        };
                        break;
                    case "--streams":
                        var streams = Value();
                        o = o with
                        {
                            Streams = streams switch
                            {
                                "game" => StreamMode.Game,
                                "keyed" => StreamMode.Keyed,
                                _ => throw new UsageException($"--streams takes game or keyed, not '{streams}'."),
                            },
                        };
                        break;
                    case "--screen":
                        try { o = o with { Screen = SeedScreen.Parse(Value()) }; }
                        catch (FormatException ex) { throw new UsageException($"--screen: {ex.Message}"); }
                        break;
                    case "--players":
                        var players = Value().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                        if (players.Count == 0) throw new UsageException("--players takes run player names separated by commas.");
                        if (players.FirstOrDefault(player => RunPlayers.ByName(player) is null) is { } unknown)
                            throw new UsageException($"There's no run player called '{unknown}': use {string.Join(", ", RunPlayers.Names)} (a fight player after the colon: random, greedy, lookahead).");
                        if (players.GroupBy(player => RunPlayers.ByName(player)!.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1) is { } twice)
                            throw new UsageException($"--players names {twice.Key} twice.");
                        o = o with { Players = players };
                        break;
                    default:
                        throw new UsageException(name.StartsWith('-') ? $"run-baseline has no {name} option." : $"'{name}' has no option name in front of it.");
                }
            }

            if (given.Contains("--seeds") && (given.Contains("--runs") || given.Contains("--seed")))
                throw new UsageException("--seeds names the seeds itself: leave out --runs and --seed.");
            if (o.Streams == StreamMode.Keyed && o.Mode != RunMode.DrawAgain)
                throw new UsageException("--streams keyed goes with --mode draw-again only: a screened or strict run is the game's own run and uses the game's streams.");
            if (given.Contains("--screen") && o.Mode != RunMode.Screened && !o.Look)
                throw new UsageException("--screen applies to --mode screened (and to --look).");
            return o;
        }
    }

    /// <summary>One run of the batch as it ended.</summary>
    public sealed record Played(ActResult Result, IReadOnlyList<FloorLine> Floors, JsonObject Line, string? Note);

    /// <summary>Parses, plays and prints. 0 when every run came to an end with no action refused, 1 otherwise, 2 for bad arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usage:\n" + Usage);
            return 2;
        }
        return Run(options, output);
    }

    public static int Run(Options options, TextWriter output) => Run(options, output, null);

    /// <summary>The same with the players made by the caller, for checks: a function from a name of <see cref="Options.Players"/> to its player.</summary>
    internal static int Run(Options options, TextWriter output, Func<string, IRunPlayer>? playerFor)
    {
        var inv = CultureInfo.InvariantCulture;
        playerFor ??= name => RunPlayers.ByName(name, options.Seed)!;

        // ---------- the seeds ----------
        List<string> seeds;
        string seedNote;
        if (options.Seeds.Count > 0)
        {
            seeds = options.Seeds.ToList();
            seedNote = "the seeds named";
        }
        else
        {
            seeds = ActRun.Seeds(options.Runs, options.Seed, options.Look ? RunMode.Strict : options.Mode, out var tried, options.Screen, mostTried: options.Tries);
            seedNote = options.Look || options.Mode == RunMode.Strict
                ? string.Create(inv, $"seeds as they come from seed {options.Seed}")
                : options.Mode == RunMode.Screened
                    ? string.Create(inv, $"from seed {options.Seed}: {seeds.Count} of {tried} seeds pass the screen ({options.Screen}: {options.Screen.Describe()})")
                    : string.Create(inv, $"from seed {options.Seed}: {seeds.Count} of {tried} seeds have Overgrowth as their first act");
            if (seeds.Count < options.Runs)
                output.WriteLine(string.Create(inv, $"Only {seeds.Count} of the {options.Runs} seeds asked for were found among {tried} (--tries): the screen ({options.Screen}) is deeper than what is built allows on so few seeds."));
            if (seeds.Count == 0) return 1;
        }

        if (options.Look) return Look(options, seeds, output);

        // ---------- the runs ----------
        var players = options.Players.ToList();
        var jobs = (from seed in seeds from player in players select (Seed: seed, Player: player)).ToList();
        var played = new Played[jobs.Count];
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Threads) }, j =>
        {
            played[j] = PlayOne(jobs[j].Seed, playerFor(jobs[j].Player), options);
        });

        var names = players.Select(player => playerFor(player).Name).ToList();
        output.WriteLine(string.Create(inv,
            $"Arena run baseline: {seeds.Count} {(seeds.Count == 1 ? "run" : "runs")} of the first act ({seedNote}), mode {ModeName(options.Mode)}, {(options.Streams == StreamMode.Keyed ? "keyed" : "the game's")} streams, played by {string.Join(", ", names)}."));
        output.WriteLine();

        // Run by run, while that is a page; a big batch prints its totals only.
        if (seeds.Count <= 60 || options.Floors)
        {
            var table = new TextTable(["seed", .. names]);
            for (var s = 0; s < seeds.Count; s++)
                table.Add([seeds[s], .. Enumerable.Range(0, players.Count).Select(p => Cell(played[s * players.Count + p].Result))]);
            table.Write(output);
            output.WriteLine("cleared with the HP left; died or unrepresentable (unrep.) with the floor the run stood on.");
            output.WriteLine();
        }

        var totals = new TextTable(["player", "runs", "cleared", "died", "unrepresentable", "mean floor", "floor of deaths", "HP left on clears", "fights won", "the game's own", "refused", "seconds a run"]);
        for (var p = 0; p < players.Count; p++)
        {
            var rows = Enumerable.Range(0, seeds.Count).Select(s => played[s * players.Count + p].Result).ToList();
            totals.Add(
            [
                names[p],
                rows.Count.ToString(inv),
                rows.Count(r => r.Cleared).ToString(inv),
                rows.Count(r => r.Died).ToString(inv),
                rows.Count(r => r.Unrepresentable).ToString(inv),
                Stats.Mean(rows.Select(r => (double)r.Floor)),
                Stats.Mean(rows.Where(r => r.Died).Select(r => (double)r.Floor)),
                Stats.Mean(rows.Where(r => r.Cleared).Select(r => (double)r.Hp)),
                Stats.Mean(rows.Select(r => (double)r.Fights)),
                rows.Count(r => r.Finished && r.FirstRedrawFloor is null).ToString(inv),
                rows.Sum(r => r.Refused).ToString(inv),
                rows.Average(r => r.Seconds).ToString("0.00", inv),
            ]);
        }
        totals.Write(output);
        output.WriteLine("\"the game's own\": runs that ended cleared or dead with no roll drawn again, so the game gives the same run for that seed and those actions.");

        // ---------- what was not built ----------
        var missing = played.Where(run => run.Result.Unrepresentable).ToList();
        if (missing.Count > 0)
        {
            output.WriteLine();
            output.WriteLine(string.Create(inv, $"Unrepresentable runs by what was missing, all players together: {Tally(missing.Select(run => ActRun.MissingKind(run.Result.Reason)))}."));
            foreach (var group in missing.GroupBy(run => ActRun.MissingKind(run.Result.Reason)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
                output.WriteLine(string.Create(inv, $"  {group.Key} ({group.Count()}), for example {group.First().Result.Seed}: {group.First().Result.Reason}"));
        }

        if (options.Mode == RunMode.Strict)
        {
            output.WriteLine();
            for (var p = 0; p < players.Count; p++)
            {
                var rows = Enumerable.Range(0, seeds.Count).Select(s => played[s * players.Count + p].Result).ToList();
                output.WriteLine(RealRunShare.Of(rows).Describe(names[p]));
            }
            output.WriteLine("A strict run that ends cleared or dead met nothing the arena has not built: it is the run the game gives for that seed and those actions.");
            output.WriteLine("\"As whole acts\" takes the deaths out: floor by floor, of the runs still alive and still the game's own, the share that met nothing unbuilt; the number in brackets is how many runs a floor's figure rests on.");
        }

        if (options.Floors)
        {
            foreach (var run in played)
            {
                output.WriteLine();
                output.WriteLine($"{run.Result.Seed}, {run.Result.Player}: {run.Line["summary"]}");
                foreach (var floor in run.Floors) output.WriteLine("  " + floor);
            }
        }

        // ---------- what should never happen ----------
        var bad = 0;
        foreach (var run in played)
        {
            var r = run.Result;
            if (run.Note is not null) output.WriteLine($"NOTE: {r.Player} on {r.Seed}: {run.Note}");
            if (r.Refused > 0)
            {
                bad++;
                output.WriteLine(string.Create(inv, $"REFUSED: {r.Player} on {r.Seed} had {r.Refused} actions turned down; the first: {r.FirstRefusal}"));
            }
            if (r.Stalled)
            {
                bad++;
                output.WriteLine(string.Create(inv, $"HUNG: {r.Player} on {r.Seed} did not come to the end of the run (floor {r.Floor}, {r.Actions} actions){(r.FirstRefusal is null ? "" : ": " + r.FirstRefusal)}"));
            }
            if (r.Result == RunResult.Error)
            {
                bad++;
                output.WriteLine($"ERROR: {r.Player} on {r.Seed}: {r.Reason}");
            }
        }

        if (options.Out is { } folder)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "runs.jsonl");
            File.WriteAllLines(path, played.Select(run => run.Line.ToJsonString()), new UTF8Encoding(false));
            output.WriteLine();
            output.WriteLine($"Result lines with the floor logs: {path}; the saves: {Path.Combine(folder, "actions")}");
        }
        return bad > 0 ? 1 : 0;
    }

    /// <summary>One run of one player, from its save when <c>--out</c> has one.</summary>
    private static Played PlayOne(string seed, IRunPlayer player, Options options)
    {
        string? note = null;
        RunGame? game = null;
        if (options.Out is { } folder)
        {
            var path = Path.Combine(folder, "actions", player.Name.Replace(':', '-'), seed + ".txt");
            if (File.Exists(path))
            {
                try
                {
                    game = ActRun.Resume(seed, options.Mode, path, options.Streams, options.Screen, TimeSpan.Zero);
                    note = string.Create(CultureInfo.InvariantCulture, $"carried on from {path} ({game.Actions.Count} actions)");
                }
                catch (InvalidOperationException ex)
                {
                    // A save of another build or another mode: it is of no use, and the run costs nothing to play again.
                    File.Delete(path);
                    note = $"the save {path} did not fit and was played again from the start ({ex.Message})";
                }
            }
            game ??= ActRun.StartGame(seed, options.Mode, options.Streams, options.Screen, path, TimeSpan.Zero);
        }
        game ??= ActRun.StartGame(seed, options.Mode, options.Streams, options.Screen, actionDelay: TimeSpan.Zero);

        var result = RunPlayers.Play(game, player);
        var line = result.ToJson(game.Run);
        line["mode"] = ModeName(options.Mode);
        line["streams"] = options.Streams == StreamMode.Keyed ? "keyed" : "game";
        if (options.Mode == RunMode.Screened) line["screen"] = options.Screen.ToString();
        if (game.ActionLogError is { } error) note = (note is null ? "" : note + "; ") + "the save could not be written: " + error;
        return new Played(result, FloorLog.Of(game.Run), line, note);
    }

    /// <summary>The mode as the command line names it.</summary>
    public static string ModeName(RunMode mode) => mode switch
    {
        RunMode.Strict => "strict",
        RunMode.Screened => "screened",
        _ => "draw-again",
    };

    /// <summary>A run in a table cell: "cleared 44", "died 9", "unrep. 4", "ERROR".</summary>
    public static string Cell(ActResult result) => result.Result switch
    {
        RunResult.Cleared => string.Create(CultureInfo.InvariantCulture, $"cleared {result.Hp}"),
        RunResult.Died => string.Create(CultureInfo.InvariantCulture, $"died {result.Floor}"),
        RunResult.Unrepresentable => string.Create(CultureInfo.InvariantCulture, $"unrep. {result.Floor}"),
        RunResult.Error => "ERROR",
        _ => "HUNG",
    };

    private static string Tally(IEnumerable<string> kinds) =>
        string.Join(", ", kinds.GroupBy(kind => kind, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} {g.Count()}")));

    // ---------- --look ----------

    /// <summary>What each seed fixes, and whether it passes the screen. Plays nothing.</summary>
    private static int Look(Options options, IReadOnlyList<string> seeds, TextWriter output)
    {
        var inv = CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(inv, $"Seed screen ({options.Screen}): {options.Screen.Describe()}. A name in brackets is not built."));
        output.WriteLine();
        var passed = 0;
        foreach (var seed in seeds)
        {
            var look = SeedLook.Of(seed);
            var why = options.Screen.WhyNot(look);
            if (why is null) passed++;
            static string Mark(string name, bool built) => built ? name : $"[{name}]";
            output.WriteLine($"{look.Seed}  {look.Act}  {(why is null ? "passes" : "does not pass: " + why)}");
            if (!look.IsOvergrowth) continue;
            output.WriteLine($"  Neow: {string.Join(", ", look.Neow.Select(relic => Mark(relic, RunRelics.IsBuilt(relic))))}");
            output.WriteLine(string.Create(inv, $"  events ({look.EventsBuilt()} built at the front): {string.Join(", ", look.Events.Take(8).Select(name => Mark(name, EventList.IsBuilt(name))))}, ..."));
            foreach (var end in look.BagEnds)
                output.WriteLine($"  {end.Bag} bag, {end.Rarity} {end.End}: {string.Join(", ", end.Relics.Take(3).Select(relic => Mark(relic, RunRelics.IsBuilt(relic))))}, ...");
            output.WriteLine($"  first shop's colorless cards: {string.Join(", ", look.ShopColorless.Select(card => Mark(card, CardPools.IsBuilt(card))))}");
        }
        output.WriteLine();
        output.WriteLine(string.Create(inv, $"{passed} of {seeds.Count} seeds pass."));
        return 0;
    }
}
