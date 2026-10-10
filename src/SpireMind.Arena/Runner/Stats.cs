using System.Globalization;

namespace SpireMind.Arena;

/// <summary>The little statistics the report needs, and how it writes numbers.</summary>
public static class Stats
{
    /// <summary>
    /// The 95% Wilson interval for a rate of <paramref name="wins"/> in <paramref name="n"/>
    /// tries: the range the true rate plausibly lies in. It is wide for few tries, which is the
    /// point of showing it.
    /// </summary>
    public static (double Low, double High) Wilson(int wins, int n)
    {
        if (n <= 0)
            return (0, 1);
        const double z = 1.959964;
        var p = (double)wins / n;
        var denominator = 1 + z * z / n;
        var centre = (p + z * z / (2 * n)) / denominator;
        var half = z * Math.Sqrt(p * (1 - p) / n + z * z / (4.0 * n * n)) / denominator;
        return (Math.Max(0, centre - half), Math.Min(1, centre + half));
    }

    /// <summary>
    /// The two-sided exact sign test: how likely a split at least as lopsided as
    /// <paramref name="better"/> against <paramref name="worse"/> is when neither side is really
    /// better (ties left out). 1 when there is nothing to compare.
    /// </summary>
    public static double SignTest(int better, int worse)
    {
        var n = better + worse;
        if (n == 0)
            return 1;
        var k = Math.Min(better, worse);
        double tail = 0, term = Math.Pow(0.5, n); // C(n, 0) / 2^n
        for (var i = 0; i <= k; i++)
        {
            tail += term;
            term = term * (n - i) / (i + 1);
        }
        return Math.Min(1, 2 * tail);
    }

    public static string Percent(double rate) => (rate * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>The mean of <paramref name="values"/> in the given format, or "-" for none.</summary>
    public static string Mean(IEnumerable<double> values, string format = "0.0")
    {
        var list = values.ToList();
        return list.Count == 0 ? "-" : list.Average().ToString(format, CultureInfo.InvariantCulture);
    }
}
