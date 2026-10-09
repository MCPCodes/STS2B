using System.Reflection;
using HarmonyLib;

namespace SpireMind.Patches;

/// <summary>
/// What the game is asking for when it pauses an action for a card choice ("Choose a card
/// to Exhaust", min 1, max 1). The selection screens don't expose this cleanly, so we
/// observe the static CardSelectCmd.From* factories. Read-only: the prefix never changes
/// arguments or skips the original.
/// </summary>
internal static class ChoicePrompt
{
    public sealed record Info(string Source, string? Prompt, int? Min, int? Max, DateTime At);

    public static Info? Latest { get; set; }

    /// <summary>The latest prompt if it is recent enough to belong to the screen now showing.</summary>
    public static Info? Recent(TimeSpan maxAge) =>
        Latest is { } i && DateTime.UtcNow - i.At <= maxAge ? i : null;
}

[HarmonyPatch]
internal static class ChoicePromptPatch
{
    private static readonly string[] Factories =
    [
        "FromHand", "FromHandForUpgrade", "FromSimpleGrid", "FromSimpleGridForRewards", "FromCombatPile", "FromChooseACardScreen",
    ];

    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(CardSelectCmd)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => Factories.Contains(m.Name) && !m.IsGenericMethodDefinition);

    private static void Prefix(MethodBase __originalMethod, object[] __args)
    {
        try
        {
            // Only our own choices matter; in co-op the teammate's prompts run on their machine.
            var player = __args.OfType<Player>().FirstOrDefault();
            if (player is not null && !LocalContext.IsMe(player)) return;

            string? prompt = null;
            int? min = null, max = null;
            foreach (var arg in __args)
            {
                if (arg is null || arg is Player) continue;
                prompt ??= arg switch
                {
                    LocString loc => loc.GetFormattedText(),
                    string s => s,
                    _ => ReadString(arg, "Prompt", "Title", "Header", "Description"),
                };
                min ??= ReadInt(arg, "MinSelect", "MinCount", "Min");
                max ??= ReadInt(arg, "MaxSelect", "MaxCount", "Max");
            }

            ChoicePrompt.Latest = new ChoicePrompt.Info(
                __originalMethod.Name, prompt is null ? null : TextCleaner.Clean(prompt), min, max, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"choice prompt capture: {ex.Message}");
        }
    }

    private static string? ReadString(object obj, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Member(obj, name);
            switch (value)
            {
                case LocString loc: return loc.GetFormattedText();
                case string s when s.Length > 0: return s;
            }
        }
        return null;
    }

    private static int? ReadInt(object obj, params string[] names)
    {
        foreach (var name in names)
        {
            if (Member(obj, name) is int i) return i;
        }
        return null;
    }

    private static object? Member(object obj, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var t = obj.GetType();
        if (t.IsPrimitive || t.IsEnum) return null;
        return t.GetProperty(name, flags)?.GetValue(obj) ?? t.GetField(name, flags)?.GetValue(obj);
    }
}
