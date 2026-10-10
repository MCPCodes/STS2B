using System.Text.Json.Nodes;
using SpireMind.Core;

namespace SpireMind.DevHost;

/// <summary>The chat both dev host modes use.</summary>
public static class DevChat
{
    /// <summary>A hub whose bot is the Ironclad, with a small stand-in for the persona Identity
    /// reads from the game so get_persona works here too. Every line goes to <paramref name="output"/>.</summary>
    public static ChatHub Create(TextWriter output)
    {
        var chat = new ChatHub
        {
            BotName = "Ironclad",
            Persona = new JsonObject { ["name"] = "Ironclad", ["title"] = "The Ironclad", ["id"] = "IRONCLAD", ["tone"] = "A terse, grim soldier." },
        };
        chat.LineAdded += line => output.WriteLine(line.From.Length > 0 ? $"chat {line.From}: {line.Text}" : $"chat: {line.Text}");
        return chat;
    }
}
