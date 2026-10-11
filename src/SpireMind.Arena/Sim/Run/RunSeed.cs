using System.Text;

namespace SpireMind.Arena;

/// <summary>
/// The seed of a run: the text a player sees and can type ("KMEHXZW0R5"), and the number the
/// game makes of it. Every stream of a run is seeded from that number, so two runs with the
/// same text are the same run.
/// </summary>
public sealed class RunSeed
{
    /// <summary>The 34 characters the game builds a seed from: the digits and the capital letters without I and O.</summary>
    public const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>The length of a seed the game makes itself.</summary>
    public const int DefaultLength = 10;

    /// <summary>The seed as the game keeps and shows it.</summary>
    public string Text { get; }

    /// <summary>The game's hash of <see cref="Text"/>: the seed number of the run streams.</summary>
    public uint Hash { get; }

    /// <summary>
    /// A seed from what a player types. The game makes typed seeds uniform before it uses them:
    /// capitals, the letter O read as zero, the letter I as one, no spaces around it.
    /// </summary>
    public RunSeed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = Canonical(text);
        Hash = unchecked((uint)RngSet.NameHash(Text));
    }

    /// <summary>The game's rule for a typed seed, in its order: upper case, O to 0, I to 1, then trimmed.</summary>
    public static string Canonical(string typed) =>
        typed.ToUpperInvariant().Replace('O', '0').Replace('I', '1').Trim();

    /// <summary>
    /// A seed of <paramref name="length"/> characters as the game makes one: one pick from
    /// <see cref="Alphabet"/> per character. The game draws these from the clock and throws a
    /// seed away that spells a word of its bad-word list; that list is not in the arena, so a
    /// seed made here can be one the game would have drawn again. It is still a seed a player
    /// can type, which is all a run needs.
    /// </summary>
    public static RunSeed Random(GameRng rng, int length = DefaultLength)
    {
        var text = new StringBuilder(length);
        for (var i = 0; i < length; i++) text.Append(Alphabet[rng.NextInt(0, Alphabet.Length)]);
        return new RunSeed(text.ToString());
    }

    public override string ToString() => Text;
}

/// <summary>The two ways the game turns a class or enum name into text. Stream names and model ids are hashed, so they must be exact.</summary>
public static class GameNames
{
    /// <summary>
    /// "MonsterAi" to "monster_ai": the name of a stream of the enum member that names it.
    /// An underscore goes before every capital that follows a letter or a digit, and before
    /// every further capital of a run of capitals; then everything is lower case.
    /// </summary>
    public static string SnakeCase(string name) => WithUnderscores(name.Trim()).ToLowerInvariant();

    /// <summary>
    /// "TheKinBoss" to "THE_KIN_BOSS": the entry of a model id, which is what a save writes
    /// ("ENCOUNTER.THE_KIN_BOSS") and what the game hashes for an encounter's or an event's own
    /// stream. Underscores as in <see cref="SnakeCase"/>, upper case, spaces to underscores,
    /// and every character that is not a capital, a digit or an underscore dropped.
    /// </summary>
    public static string Slug(string className)
    {
        var upper = WithUnderscores(className.Trim()).ToUpperInvariant();
        var slug = new StringBuilder(upper.Length);
        var inSpace = false;
        foreach (var ch in upper)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!inSpace) slug.Append('_');
                inSpace = true;
                continue;
            }
            inSpace = false;
            if (ch is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_') slug.Append(ch);
        }
        return slug.ToString();
    }

    /// <summary>The class name behind a save's id: "ENCOUNTER.THE_KIN_BOSS" or "THE_KIN_BOSS" to "TheKinBoss".
    /// Only for names the arena's lists hold: the slug of each is compared, nothing is guessed.</summary>
    public static string? ClassNameOf(string id, IEnumerable<string> classNames)
    {
        var entry = id[(id.IndexOf('.') + 1)..];
        return classNames.FirstOrDefault(n => Slug(n) == entry);
    }

    // The game's pattern takes, from left to right and never overlapping, either one letter or
    // digit and the capital behind it, or (right where the last match ended, but not at the
    // very start) a capital alone, and puts an underscore before that capital.
    private static string WithUnderscores(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        var i = 0;
        var lastMatchEnd = -1;
        while (i < text.Length)
        {
            var ch = text[i];
            if (IsAlphaNumeric(ch) && i + 1 < text.Length && IsCapital(text[i + 1]))
            {
                result.Append(ch).Append('_').Append(text[i + 1]);
                i += 2;
                lastMatchEnd = i;
            }
            else if (i == lastMatchEnd && i != 0 && IsCapital(ch))
            {
                result.Append('_').Append(ch);
                i++;
                lastMatchEnd = i;
            }
            else
            {
                result.Append(ch);
                i++;
            }
        }
        return result.ToString();
    }

    private static bool IsCapital(char ch) => ch is >= 'A' and <= 'Z';
    private static bool IsAlphaNumeric(char ch) => ch is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9');
}
