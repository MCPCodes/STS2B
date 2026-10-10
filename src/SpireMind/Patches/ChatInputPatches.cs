using System.Reflection;
using HarmonyLib;
using SpireMind.Game;

namespace SpireMind.Patches;

/// <summary>
/// The chat box's keys (docs/chat-and-persona.md, "The chat box"). The game turns keys into
/// hotkeys in NInputManager._UnhandledKeyInput, after the GUI has had them, so a key that gets
/// there wasn't used by the focused LineEdit. With the box closed, a fresh press of the chat
/// hotkey opens it and isn't typed into it; with the box open, every key that gets there is
/// kept from the game (Cmd or Ctrl plus a digit picks cards whatever has focus, F-keys, key
/// releases). The game turns releases into hotkeys too, so once the box has closed, the release
/// of a key it took is kept back as well. <see cref="ChatOverlay.TakeUnhandledKey"/> decides;
/// this marks the event handled and skips the game's handler.
/// </summary>
[HarmonyPatch]
internal static class ChatHotkeyPatch
{
    private static bool _failed;

    /// <summary>The handler NInputManager declares itself, not Node's empty virtual.</summary>
    internal static MethodBase? Target() =>
        AccessTools.DeclaredMethod(typeof(NInputManager), "_UnhandledKeyInput", [typeof(InputEvent)]);

    private static MethodBase TargetMethod() =>
        Target() ?? throw new MissingMethodException(nameof(NInputManager), "_UnhandledKeyInput");

    /// <summary>True when this Harmony instance's prefix is on the handler, so the overlay can stop polling the hotkey.</summary>
    public static bool IsApplied(string harmonyId) =>
        Target() is { } target && HarmonyLib.Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == harmonyId) == true;

    private static bool Prefix(NInputManager __instance, InputEvent __0)
    {
        try
        {
            if (ModEntry.Overlay?.TakeUnhandledKey(__0) != true) return true;
            __instance.GetViewport()?.SetInputAsHandled();
            return false;
        }
        catch (Exception ex)
        {
            // The game keeps the key: a broken chat box must not cost the game its hotkeys.
            if (!_failed)
            {
                _failed = true;
                ModEntry.LogError($"chat hotkey: {ex.Message}");
            }
            return true;
        }
    }
}

/// <summary>
/// F2 opens the game's feedback screen from NFeedbackScreenOpener._Input, which runs before the
/// GUI, so the chat box can't take the key first. Skipped while typing; the handler does nothing
/// else.
/// </summary>
[HarmonyPatch]
internal static class FeedbackKeyPatch
{
    private static MethodBase TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(NFeedbackScreenOpener), "_Input", [typeof(InputEvent)])
        ?? throw new MissingMethodException(nameof(NFeedbackScreenOpener), "_Input");

    private static bool Prefix() => !ChatOverlay.IsTyping;
}
