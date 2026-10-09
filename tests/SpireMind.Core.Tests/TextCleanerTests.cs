using SpireMind.Core;
using Xunit;

namespace SpireMind.Core.Tests;

public class TextCleanerTests
{
    [Theory]
    [InlineData("Deal [blue]6[/blue] damage.", "Deal 6 damage.")]
    [InlineData("Gain [img]res://images/ui/energy_icon.png[/img][img]res://images/ui/energy_icon.png[/img].", "Gain (E)(E).")]
    [InlineData("Gain [img=24x24]res://ui/star.png[/img] 2.", "Gain (Star) 2.")]
    [InlineData("[gold]Exhaust[/gold].\nDraw 2 cards.", "Exhaust. / Draw 2 cards.")]
    [InlineData("  spaced   out  ", "spaced out")]
    [InlineData(null, "")]
    [InlineData("[color=#ff0000]Lose[/color] 3 HP.", "Lose 3 HP.")]
    public void Cleans_bbcode(string? input, string expected) =>
        Assert.Equal(expected, TextCleaner.Clean(input));

    [Fact]
    public void Join_skips_blanks() =>
        Assert.Equal("a, c", TextCleaner.Join("a", null, " ", "c"));
}
