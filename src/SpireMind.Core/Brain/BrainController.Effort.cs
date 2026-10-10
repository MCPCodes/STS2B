namespace SpireMind.Core.Brain;

// /effort elite and /effort boss: the effort of elite and boss rooms (BrainSettings.EffortFor).
public sealed partial class BrainController
{
    /// <summary>The argument hint /help shows for /effort.</summary>
    public static string EffortUsage { get; } =
        $"[{string.Join("|", BrainSettings.Efforts)}] or elite|boss [level|{BrainSettings.SameEffort}|{BrainSettings.DefaultWord}]";

    private static string EffortOptions =>
        $"Options: {string.Join(", ", BrainSettings.Efforts)}. /effort elite or /effort boss with a level, " +
        $"{BrainSettings.SameEffort} (the normal effort) or {BrainSettings.DefaultWord} ({BrainSettings.DefaultFightEffort}, or the normal effort when higher) sets a fight's.";

    // The reply to /effort without arguments: the normal effort and both fight efforts.
    private static string EffortSummary(BrainSettings settings) =>
        $"The effort is {settings.Effort}; {FightEffort(settings, BrainSettings.EliteRoom)} in elite rooms and " +
        $"{FightEffort(settings, BrainSettings.BossRoom)} in boss rooms. {EffortOptions}";

    // Added to the model and effort in /budget when a fight room differs from the normal effort.
    private static string FightEffortNote(BrainSettings settings)
    {
        var elite = settings.EffortFor(BrainSettings.EliteRoom);
        var boss = settings.EffortFor(BrainSettings.BossRoom);
        if (elite == settings.Effort && boss == settings.Effort)
            return "";
        return elite == boss ? $", {elite} in elite and boss rooms" : $", {elite} in elite rooms and {boss} in boss rooms";
    }

    // "high", "medium (the normal effort)" or "high (the default)".
    private static string FightEffort(BrainSettings settings, string room)
    {
        var chosen = room == BrainSettings.EliteRoom ? settings.EliteEffort : settings.BossEffort;
        var effort = settings.EffortFor(room);
        return chosen switch
        {
            null => $"{effort} (the default)",
            BrainSettings.SameEffort => $"{effort} (the normal effort)",
            _ => effort,
        };
    }

    // /effort elite ... and /effort boss ...; null for any other /effort.
    private string? FightEffortCommand(string from, string args)
    {
        var words = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return null;
        var room = words[0].ToLowerInvariant() switch
        {
            "elite" => BrainSettings.EliteRoom,
            "boss" => BrainSettings.BossRoom,
            _ => null,
        };
        if (room is null)
            return null;

        var current = Settings;
        var name = room.ToLowerInvariant();
        if (words.Length == 1)
            return $"The effort in {name} rooms is {FightEffort(current, room)}. {EffortOptions}";
        if (!BrainSettings.TryParseFightEffort(words[1], out var value))
            return $"\"{words[1]}\" isn't an effort level. {EffortOptions}";
        var was = room == BrainSettings.EliteRoom ? current.EliteEffort : current.BossEffort;
        if (value == was)
            return $"The effort in {name} rooms is {FightEffort(current, room)} already.";

        var reloaded = Reload(current, keepAutostart: true);
        var settings = room == BrainSettings.EliteRoom ? reloaded with { EliteEffort = value } : reloaded with { BossEffort = value };
        var saved = Save(settings);
        bool subscription;
        lock (_gate)
        {
            _settings = settings;
            subscription = _kind == BrainKind.Subscription;
        }
        Log($"brain: {name} effort set to {value ?? BrainSettings.DefaultWord} by {from}");
        var line = $"The effort in {name} rooms is now {FightEffort(settings, room)}.";
        // Claude Code isn't told an effort, so only the API brain uses it.
        return subscription
            ? $"{line} Only the API brain uses it, so Claude Code playing now isn't affected.{saved}"
            : $"{line} It applies from the next request in {(room == BrainSettings.EliteRoom ? "an" : "a")} {name} room.{saved}";
    }
}
