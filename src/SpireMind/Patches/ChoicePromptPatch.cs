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
        "FromHand", "FromHandForDiscard", "FromHandForUpgrade", "FromSimpleGrid", "FromSimpleGridForRewards", "FromCombatPile",
        "FromChooseACardScreen", "FromChooseABundleScreen",
        "FromDeckForUpgrade", "FromDeckForTransformation", "FromDeckForEnchantment", "FromDeckForRemoval", "FromDeckGeneric",
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
            if (__args.OfType<CardSelectorPrefs>().Select(p => (CardSelectorPrefs?)p).FirstOrDefault() is { } prefs)
            {
                prompt = prefs.Prompt?.GetFormattedText();
                min = prefs.MinSelect;
                max = prefs.MaxSelect;
            }
            else
            {
                // The factories without prefs always pick one card. Don't read other arguments
                // for text: the source card or enchantment would pass for the prompt.
                switch (__originalMethod.Name)
                {
                    case "FromHandForUpgrade":
                        prompt = new LocString("gameplay_ui", "CHOOSE_CARD_UPGRADE_HEADER").GetFormattedText();
                        min = max = 1;
                        break;
                    case "FromChooseACardScreen":
                        min = __args.OfType<bool>().FirstOrDefault() ? 0 : 1; // canSkip
                        max = 1;
                        break;
                }
            }

            ChoicePrompt.Latest = new ChoicePrompt.Info(
                __originalMethod.Name, prompt is null ? null : TextCleaner.Clean(prompt), min, max, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            ModEntry.LogError($"choice prompt capture: {ex.Message}");
        }
    }
}
