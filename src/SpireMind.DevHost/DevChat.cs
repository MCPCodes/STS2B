using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.DevHost;

/// <summary>The chat every dev host mode uses.</summary>
public static class DevChat
{
    /// <summary>How each character talks, a stand-in for the persona Identity reads from the game.</summary>
    private static readonly Dictionary<string, string> Tones = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ironclad"] = "A terse, grim soldier.",
        ["Silent"] = "An almost wordless huntress who speaks in ellipses.",
        ["Defect"] = "A chirping, beeping automaton that refers to itself as \"it\".",
        ["Regent"] = "A loud, pompous royal who refers to himself as \"his Majesty\".",
        ["Necrobinder"] = "A vengeful but darkly funny young lich, with Osty, her animated hand.",
    };

    /// <summary>A hub whose bot is <paramref name="botName"/>, with a small stand-in for the
    /// persona Identity reads from the game so get_persona works here too. Every line goes to
    /// <paramref name="output"/>; a line relayed from another copy says so.</summary>
    public static ChatHub Create(TextWriter output, string botName = "Ironclad")
    {
        var name = string.IsNullOrWhiteSpace(botName) ? "Ironclad" : botName.Trim();
        var persona = new JsonObject { ["name"] = name, ["title"] = $"The {name}", ["id"] = name.ToUpperInvariant() };
        if (Tones.TryGetValue(name, out var tone))
            persona["tone"] = tone;

        var chat = new ChatHub { BotName = name, Persona = persona };
        chat.LineAdded += line => output.WriteLine(Format(line));
        return chat;
    }

    /// <summary>How a chat line is printed: "chat Mark: hello", "chat: Silent paused.", and
    /// "chat Silent (relayed): ..." for a line that came from another copy.</summary>
    public static string Format(ChatLine line)
    {
        var relayed = line.Remote ? " (relayed)" : "";
        return line.From.Length > 0 ? $"chat {line.From}{relayed}: {line.Text}" : $"chat{relayed}: {line.Text}";
    }
}
