using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SpireMind.Game.Coop;

/// <summary>
/// The Steam-less profile a bot copy plays from: <c>&lt;user data&gt;/default/&lt;client id&gt;/</c>.
/// A copy without a settings.save there starts fullscreen, shows the mod-consent popup (whose
/// Yes quits the game) and loads no mods, so no bot. This class makes the two files a first
/// start needs, with the field names of v0.107.1 (sts2-notes/coop-api-v0.107.1/spawn.md,
/// section 2, and scripts/coop-bot.sh, which seeds the same way):
///   settings.save: a small muted window, mods agreed to, the early-access notice seen, and
///     the human's own choices for the local mods, so both copies load the same ones;
///   progress.save: tutorials off (no tutorial question at ready-up), both ascension notices
///     seen, the top multiplayer ascension cap (the lobby takes the lowest cap among its
///     players, so a new profile's 0 would pull the human's lobby down to 0), and the human's
///     unlocks: every epoch he has revealed on his Timeline, so the bot can pick any character
///     he has unlocked and its rewards come from the pools he is used to.
///
/// What is taken from the human is read from his running game, never from his files, and is
/// only this: his language, his on/off choices for local mods, and the ids and dates of his
/// revealed epochs. No statistics, discoveries, achievements, run saves, key maps or other
/// settings go into a bot's profile, and the bot gets a progress id of its own.
///
/// A file that already exists is never replaced: an existing profile is only inspected, and
/// the running copy corrects its own window and volume in memory (<see cref="BotWindow.Start"/>).
/// Nothing is ever written outside <c>default/</c>, and never through a link.
///
/// <see cref="Capture"/> runs on the main thread; the rest is plain file work for a worker thread.
/// </summary>
internal static class BotProfile
{
    private const string SettingsFile = "settings.save";
    private const string ProgressFile = "progress.save";
    private const string ProfileFile = "profile.save";

    /// <summary>How the game writes ModSource.ModsDirectory in settings.save.</summary>
    private const string LocalSource = "mods_directory";

    /// <summary>How the game writes EpochState.Revealed in progress.save (lower snake case).</summary>
    private const string RevealedState = "revealed";

    /// <summary>The top of the multiplayer ascension range (the game clamps a saved value to it).
    /// The lobby's cap is the lowest value among its players, so this never raises the human's cap.</summary>
    private const int MaxMultiplayerAscension = 10;

    /// <summary>The two notices that ignore the tutorials switch (lobby.md R0, spawn.md 2.4).</summary>
    private static readonly string[] SeenNotices = ["ascension_multiplayer_ftue", "ascension_singleplayer_ftue"];

    /// <summary>The first epoch of each character that has to be unlocked (UnlockState.Characters); the Ironclad needs none.</summary>
    private static readonly (string Epoch, string Character)[] CharacterEpochs =
    [
        ("SILENT1_EPOCH", "Silent"), ("REGENT1_EPOCH", "Regent"), ("NECROBINDER1_EPOCH", "Necrobinder"), ("DEFECT1_EPOCH", "Defect"),
    ];

    private const int MaxWalkDepth = 12;
    private const int MaxWalkEntries = 20000;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>One line of a settings.save mod list: a mod id, whether the entry is for the
    /// local mods folder (the only source a copy without Steam has) and whether it is switched on.</summary>
    public sealed record ModChoice(string Id, bool Local, bool Enabled);

    /// <summary>An epoch the human has revealed: its id and when he obtained it (unix seconds).</summary>
    public sealed record Epoch(string Id, long ObtainDate);

    /// <summary>What a new profile takes from the human's running game. Read only.</summary>
    /// <param name="ProgressLoaded">False while his profile isn't loaded yet; <paramref name="Unlocks"/> is then empty.</param>
    public sealed record HostSettings(
        bool ModsEnabled, IReadOnlyList<ModChoice> Mods, string? Language, int SettingsSchema, int ProgressSchema,
        bool ProgressLoaded, IReadOnlyList<Epoch> Unlocks);

    /// <summary>
    /// A profile as it is, or as it would be made. <see cref="ModsEnabled"/> and <see cref="Mods"/>
    /// are the mod settings the copy will start with, for the check that both copies load the same mods.
    /// </summary>
    public sealed record Plan(
        ulong ClientId, string Folder, string SettingsPath, bool SettingsExist, JsonObject? Seed,
        bool ModsEnabled, IReadOnlyList<ModChoice> Mods, string? Problem, IReadOnlyList<string> Notes);

    /// <summary>The profile folder of a client id. The game names it after the number it parses from --clientId.</summary>
    public static string FolderFor(string userDataDir, ulong clientId) =>
        Path.Combine(userDataDir, "default", clientId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>The characters a profile with these revealed epochs can pick, the Ironclad first.</summary>
    public static IReadOnlyList<string> CharactersFor(IEnumerable<string> revealedEpochIds)
    {
        var revealed = revealedEpochIds.ToHashSet(StringComparer.Ordinal);
        var names = new List<string> { "Ironclad" };
        names.AddRange(CharacterEpochs.Where(c => revealed.Contains(c.Epoch)).Select(c => c.Character));
        return names;
    }

    /// <summary>
    /// Reads what a new profile needs from the human's game as it holds it in memory: his mod
    /// choices and language from the settings, the ids of his revealed epochs from his progress,
    /// and the save versions this build writes. Main thread only; changes nothing.
    /// </summary>
    public static HostSettings Capture()
    {
        var saves = SaveManager.Instance;
        var settings = saves.SettingsSave;
        var mods = settings.ModSettings;
        var list = new List<ModChoice>();
        if (mods?.ModList is { } entries)
            foreach (var entry in entries)
                if (!string.IsNullOrEmpty(entry.Id))
                    list.Add(new ModChoice(entry.Id, entry.Source == ModSource.ModsDirectory, entry.IsEnabled));

        // Before the profile is loaded, Progress is a throw-away default object: nothing to copy.
        var loaded = saves.IsProfileInitialized;
        var unlocks = new List<Epoch>();
        if (loaded)
            foreach (var epoch in saves.Progress.Epochs)
                if (epoch.State == EpochState.Revealed && BotArgs.IsIdShaped(epoch.Id ?? ""))
                    unlocks.Add(new Epoch(epoch.Id!, epoch.ObtainDate));

        return new HostSettings(mods?.PlayerAgreedToModLoading == true, list, settings.Language,
            saves.GetLatestSchemaVersion<SettingsSave>(), saves.GetLatestSchemaVersion<SerializableProgress>(), loaded, unlocks);
    }

    /// <summary>
    /// Looks at the profile of <paramref name="clientId"/> without changing anything: an
    /// existing settings.save is read and judged, a missing one is planned from
    /// <paramref name="host"/>. <paramref name="botModId"/> is this mod's id, always switched on.
    /// </summary>
    public static Plan Look(string userDataDir, ulong clientId, HostSettings host, string botModId)
    {
        var folder = FolderFor(userDataDir, clientId);
        var path = Path.Combine(folder, SettingsFile);
        var notes = new List<string>();

        // The copy writes its saves into this folder as it plays: only a plain folder of its own will do.
        if (Refusal(userDataDir, folder) is { } refusal)
            return new Plan(clientId, folder, path, File.Exists(path), null, false, [], refusal, notes);

        if (!File.Exists(path))
        {
            var seed = Seed(clientId, host, botModId);
            var mods = SeededMods(host, botModId);
            return new Plan(clientId, folder, path, false, seed, true, mods, null, notes);
        }

        // An existing profile is left as it is. Only what would keep the bot from loading stops the start.
        JsonObject? existing;
        try { existing = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            existing = null;
        }
        if (existing is null)
            return new Plan(clientId, folder, path, true, null, false, [], Unreadable(path), notes);

        var modSettings = existing["mod_settings"] as JsonObject;
        var enabled = Flag(modSettings?["mods_enabled"]) == true;
        var list = new List<ModChoice>();
        if (modSettings?["mod_list"] is JsonArray array)
            foreach (var node in array)
                if (node is JsonObject entry && Text(entry["id"]) is { Length: > 0 } id)
                    list.Add(new ModChoice(id, Text(entry["source"]) == LocalSource, Flag(entry["is_enabled"]) != false));

        if (!enabled)
            return new Plan(clientId, folder, path, true, null, false, list,
                $"the copy's profile has not agreed to load mods, so its game would start without the bot and show the mod popup. Delete {path} and ask again; it is made afresh from your settings.",
                notes);

        // The running copy corrects these in memory at start; worth a line in the log all the same.
        if (Flag(existing["fullscreen"]) != false) notes.Add($"{path} says fullscreen; the copy switches itself to a small window");
        if (Flag(existing["seen_ea_disclaimer"]) != true) notes.Add($"{path} has not seen the early-access notice; the copy marks it seen at start");
        if (Number(existing["schema_version"]) is { } version && version != host.SettingsSchema)
            notes.Add($"{path} has schema_version {version}; this game writes {host.SettingsSchema}");
        return new Plan(clientId, folder, path, true, null, true, list, null, notes);
    }

    /// <summary>
    /// Makes what <paramref name="plan"/> found missing: settings.save from the seed, and a
    /// progress.save with tutorials off and the human's unlocks. Existing files are left alone.
    /// Returns null, or what went wrong in words for the chat. On failure nothing half-made is
    /// left behind.
    /// </summary>
    public static string? Write(Plan plan, HostSettings host, string userDataDir)
    {
        if (plan.Problem is not null) return plan.Problem;
        // Again, right before writing: the folder may have changed since the look.
        if (Refusal(userDataDir, plan.Folder) is { } refusal) return refusal;

        var made = new List<string>();
        try
        {
            if (!plan.SettingsExist && plan.Seed is { } seed)
            {
                var wrote = WriteNew(plan.SettingsPath, seed.ToJsonString(Indented) + "\n", made,
                    check: text => JsonNode.Parse(text) is JsonObject read && Flag((read["mod_settings"] as JsonObject)?["mods_enabled"]) == true);
                ModEntry.Log(wrote
                    ? $"coop: profile: wrote {plan.SettingsPath} (your settings were only read): a {BotWindow.Width}x{BotWindow.Height} window, muted, " +
                      $"mods agreed to, mods: {ListMods(plan.Mods)}"
                    : $"coop: profile: {plan.SettingsPath} appeared meanwhile; left as it is");
            }
            else
            {
                ModEntry.Log($"coop: profile: {plan.SettingsPath} exists and is left as it is (mods: {ListMods(plan.Mods)})");
            }

            // The copy runs modded, so its profile folders sit under modded/. The profile number
            // is the one the game would pick: profile.save's last_profile_id, 1 for a new profile.
            var progress = Path.Combine(plan.Folder, "modded", $"profile{ProfileNumber(plan.Folder)}", "saves", ProgressFile);
            if (File.Exists(progress))
                InspectProgress(progress, host);
            else
                WriteProgress(progress, plan.ClientId, host, made);
            return null;
        }
        catch (Exception ex)
        {
            // No half-made profile: what was made just now goes again, newest first.
            for (var i = made.Count - 1; i >= 0; i--)
            {
                try
                {
                    if (File.Exists(made[i])) File.Delete(made[i]);
                    else if (Directory.Exists(made[i])) Directory.Delete(made[i], recursive: false);
                }
                catch (Exception) { /* best effort */ }
            }
            ModEntry.LogError($"coop: profile: preparing {plan.Folder} failed: {ex}");
            return $"the copy's profile folder could not be prepared ({ex.Message}). Check that {plan.Folder} can be written, then ask again.";
        }
    }

    /// <summary>The settings.save of a new profile: exactly the fields the notes list (P1), with the human's language and local mod choices.</summary>
    private static JsonObject Seed(ulong clientId, HostSettings host, string botModId)
    {
        var (size, position) = BotWindow.TileFor(clientId);
        var json = new JsonObject
        {
            ["schema_version"] = host.SettingsSchema,
            ["fullscreen"] = false,
            ["window_size"] = new JsonObject { ["X"] = size.X, ["Y"] = size.Y },
            ["window_position"] = new JsonObject { ["X"] = position.X, ["Y"] = position.Y },
            ["target_display"] = 0,
            ["fps_limit"] = BotWindow.Fps,
            // On, a copy that loses focus in the menu or the lobby stays capped at 30 fps for the whole session.
            ["limit_fps_in_background"] = false,
            ["volume_master"] = 0,
            ["volume_bgm"] = 0,
            ["volume_sfx"] = 0,
            ["volume_ambience"] = 0,
            ["skip_intro_logo"] = true,
            ["seen_ea_disclaimer"] = true,
        };
        // The same language as the human's game, so card text reads the same in both. Null means "from the OS", which is the same too.
        if (!string.IsNullOrEmpty(host.Language)) json["language"] = host.Language;

        var list = new JsonArray();
        foreach (var mod in SeededMods(host, botModId))
            list.Add(new JsonObject { ["id"] = mod.Id, ["is_enabled"] = mod.Enabled, ["source"] = LocalSource });
        json["mod_settings"] = new JsonObject { ["mod_list"] = list, ["mods_enabled"] = true };
        return json;
    }

    /// <summary>The human's choices for local mods carry over, in his order. Workshop entries mean
    /// nothing without Steam and are left out. This mod is always on, or there is no bot.</summary>
    private static List<ModChoice> SeededMods(HostSettings host, string botModId)
    {
        var mods = new List<ModChoice>();
        foreach (var mod in host.Mods)
            if (mod.Local && mod.Id != botModId && mods.All(m => m.Id != mod.Id))
                mods.Add(mod);
        mods.Add(new ModChoice(botModId, true, true));
        return mods;
    }

    /// <summary>
    /// The progress.save of a new profile (spawn.md P2): the minimal file, plus one "revealed"
    /// entry for each epoch the human has revealed. On load the game adds the Timeline slots
    /// those epochs open by itself. Nothing else of his progress is in it.
    /// </summary>
    private static void WriteProgress(string path, ulong clientId, HostSettings host, List<string> made)
    {
        var epochs = new JsonArray();
        foreach (var epoch in host.Unlocks)
            epochs.Add(new JsonObject { ["id"] = epoch.Id, ["state"] = RevealedState, ["obtain_date"] = epoch.ObtainDate });
        var json = new JsonObject
        {
            ["schema_version"] = host.ProgressSchema,
            // Seven characters from A-Z and 0-9, as the game makes them; without the key the id loads as null.
            ["unique_id"] = "BOT" + (clientId % 10000).ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            ["enable_ftues"] = false,
            ["ftue_completed"] = new JsonArray(SeenNotices.Select(id => (JsonNode)id).ToArray()),
            ["max_multiplayer_ascension"] = MaxMultiplayerAscension,
            ["epochs"] = epochs,
        };
        var wrote = WriteNew(path, json.ToJsonString(Indented) + "\n", made,
            check: text => JsonNode.Parse(text) is JsonObject read && Flag(read["enable_ftues"]) == false);
        if (!wrote)
        {
            ModEntry.Log($"coop: profile: {path} appeared meanwhile; left as it is");
            return;
        }

        var characters = string.Join(", ", CharactersFor(host.Unlocks.Select(e => e.Id)));
        ModEntry.Log($"coop: profile: wrote {path}: tutorials off, multiplayer ascension cap {MaxMultiplayerAscension}, " +
                     (host.ProgressLoaded
                         ? $"{host.Unlocks.Count} revealed epoch(s) taken from your running game (ids and dates only), so it can pick: {characters}"
                         : "no unlocks (your profile was not loaded yet), so it can pick the Ironclad only"));
    }

    /// <summary>An existing progress.save is never touched. One log line says which of the human's unlocks it lacks.</summary>
    private static void InspectProgress(string path, HostSettings host)
    {
        try
        {
            var has = new HashSet<string>(StringComparer.Ordinal);
            if (JsonNode.Parse(File.ReadAllText(path)) is JsonObject json && json["epochs"] is JsonArray epochs)
                foreach (var node in epochs)
                    if (node is JsonObject epoch && Text(epoch["state"]) == RevealedState && Text(epoch["id"]) is { } id)
                        has.Add(id);
            var missing = host.Unlocks.Select(e => e.Id).Where(id => !has.Contains(id)).ToList();
            ModEntry.Log($"coop: profile: {path} exists and is left as it is; it can pick: {string.Join(", ", CharactersFor(has))}" +
                         (missing.Count == 0
                             ? ""
                             : $"; it lacks {missing.Count} of your unlocks ({string.Join(", ", missing.Take(8))}{(missing.Count > 8 ? ", ..." : "")}). " +
                               "To give it yours, delete that file while the copy is not running; it is made afresh at the next start."));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            ModEntry.LogError($"coop: profile: {path} exists but can't be read ({ex.Message}); the copy's game decides what to do with it");
        }
    }

    /// <summary>
    /// Why <paramref name="folder"/> must not be used, or null. Only ever a plain folder directly
    /// inside &lt;user data&gt;/default, and no link in its place or anywhere inside it: the copy
    /// reads and writes its saves there as it plays, and a link would send those writes somewhere
    /// else, into the human's own saves under steam/ for one (scripts/coop-bot.sh, "profile").
    /// </summary>
    private static string? Refusal(string userDataDir, string folder)
    {
        if (string.IsNullOrWhiteSpace(userDataDir)) return "the game's data folder is unknown, so no profile was made.";
        try
        {
            var root = Path.Combine(Path.GetFullPath(userDataDir), "default");
            var full = Path.GetFullPath(folder);
            if (Path.GetDirectoryName(full) != root)
                return $"refusing to use {full} as a copy's profile: it is not directly inside {root}.";

            foreach (var path in new[] { root, full })
            {
                if (IsLink(path))
                    return $"the copy's profile folder is a link, or lies behind one ({path}). Its game would read and write its saves wherever that leads. Remove the link, then ask again.";
                if (File.Exists(path))
                    return $"{path} is a file, and the copy's profile needs a folder there. Move it away, then ask again.";
            }

            var seen = 0;
            return Directory.Exists(full) && LinkInside(full, 0, ref seen) is { } link
                ? $"the copy's profile holds a link ({link}). Its game would read and write wherever that leads. Remove the link, then ask again."
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return $"the copy's profile folder {folder} can't be looked at ({ex.Message}).";
        }
    }

    private static bool IsLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        // A dangling link exists for neither test; LinkTarget still names it.
        return info.LinkTarget is not null;
    }

    /// <summary>The first link under <paramref name="folder"/>, without ever following one; null when there is none.</summary>
    private static string? LinkInside(string folder, int depth, ref int seen)
    {
        if (depth > MaxWalkDepth) return null;
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            if (++seen > MaxWalkEntries) return null;
            if (entry.LinkTarget is not null) return entry.FullName;
            if (entry is DirectoryInfo && LinkInside(entry.FullName, depth + 1, ref seen) is { } deeper) return deeper;
        }
        return null;
    }

    /// <summary>
    /// Writes a file that must not exist yet: to a temporary file beside it, read back and
    /// checked, then moved into place without replacing anything. False when the file turned
    /// up in the meantime (it is then left alone). Folders it had to make are added to
    /// <paramref name="made"/>, so a later failure can take them away again.
    /// </summary>
    private static bool WriteNew(string path, string text, List<string> made, Func<string, bool> check)
    {
        var folder = Path.GetDirectoryName(path)!;
        var missing = new List<string>();
        for (var dir = folder; !string.IsNullOrEmpty(dir) && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
            missing.Add(dir);
        for (var i = missing.Count - 1; i >= 0; i--)
        {
            Directory.CreateDirectory(missing[i]);
            made.Add(missing[i]);
        }

        // The game deletes leftover *.tmp files in a profile at start, should one ever stay behind.
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, System.IO.FileAccess.Write, FileShare.None))
            {
                stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            if (!check(File.ReadAllText(temp)))
                throw new IOException($"{Path.GetFileName(path)} did not read back as written");
            try
            {
                File.Move(temp, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                return false; // someone else made it in the meantime: theirs stays
            }
            made.Add(path);
            return true;
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>The profile the game would use in this folder: profile.save's last_profile_id, else 1.</summary>
    private static int ProfileNumber(string folder)
    {
        try
        {
            var path = Path.Combine(folder, ProfileFile);
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
                && Number(json["last_profile_id"]) is >= 1 and <= 99 and var id)
                return id;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable: the game falls back to its default too.
        }
        return 1;
    }

    private static string Unreadable(string path) =>
        $"the copy's settings file can't be read, and its game would show an invalid-save popup. Move {path} away and ask again; it is made afresh from your settings.";

    private static string ListMods(IReadOnlyList<ModChoice> mods) =>
        mods.Count == 0 ? "none listed" : string.Join(", ", mods.Select(m => $"{m.Id} {(m.Enabled ? "on" : "off")}{(m.Local ? "" : " (Workshop)")}"));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool? Flag(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static int? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;
}
