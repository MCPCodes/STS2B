namespace SpireMind.Game;

/// <summary>
/// The chat box in the game's own look (docs/chat-and-persona.md, "The chat box"): Kreon for
/// text and names, cream text and gold highlights. Read here, game-side, and handed to
/// <see cref="ChatOverlay"/> as plain Godot values. Anything that fails to load keeps the
/// overlay's built-in look for that part.
/// </summary>
internal static class ChatTheme
{
    private const string BodyFontPath = "res://themes/kreon_regular_shared.tres";
    private const string NameFontPath = "res://themes/kreon_bold_shared.tres";

    /// <summary>On the main thread, once the game is up (the overlay asks when it first attaches).</summary>
    public static ChatStyle Load(Action<string>? logError = null) => new(
        LoadFont(BodyFontPath, logError),
        LoadFont(NameFontPath, logError),
        Read(() => StsColors.cream, "cream", logError),
        Read(() => StsColors.gold, "gold", logError));

    /// <summary>The font a .tres holds: both of the game's are FontVariations over a Kreon font file.</summary>
    private static Font? LoadFont(string path, Action<string>? logError)
    {
        try
        {
            // Exists first: Load on a missing path prints an engine error of its own.
            var font = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) as Font : null;
            if (font is null) logError?.Invoke($"chat: no font in {path}, keeping the default");
            return font;
        }
        catch (Exception ex)
        {
            logError?.Invoke($"chat: loading {path} failed, keeping the default font: {ex.Message}");
            return null;
        }
    }

    private static Color? Read(Func<Color> color, string name, Action<string>? logError)
    {
        try { return color(); }
        catch (Exception ex)
        {
            logError?.Invoke($"chat: StsColors.{name} unavailable, keeping the default colour: {ex.Message}");
            return null;
        }
    }
}
