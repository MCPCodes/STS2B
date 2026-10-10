using System.Globalization;

namespace SpireMind.Arena;

/// <summary>
/// A plain text table for the terminal: columns as wide as their widest cell, two spaces apart.
/// A column whose cells all look like numbers is right-aligned; the last column isn't padded.
/// </summary>
public sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    public int Count => _rows.Count;

    public void Add(params string[] cells)
    {
        var row = new string[headers.Length];
        for (var i = 0; i < row.Length; i++)
            row[i] = i < cells.Length ? cells[i] : "";
        _rows.Add(row);
    }

    public void Write(TextWriter output, string indent = "")
    {
        var widths = new int[headers.Length];
        var right = new bool[headers.Length];
        for (var c = 0; c < headers.Length; c++)
        {
            widths[c] = _rows.Select(r => r[c].Length).Append(headers[c].Length).Max();
            right[c] = _rows.Count > 0 && _rows.All(r => LooksNumeric(r[c]));
        }

        WriteRow(headers);
        foreach (var row in _rows)
            WriteRow(row);

        void WriteRow(string[] cells)
        {
            var line = new System.Text.StringBuilder(indent);
            for (var c = 0; c < cells.Length; c++)
            {
                if (c > 0) line.Append("  ");
                line.Append(right[c] ? cells[c].PadLeft(widths[c]) : c == cells.Length - 1 ? cells[c] : cells[c].PadRight(widths[c]));
            }
            output.WriteLine(line.ToString().TrimEnd());
        }
    }

    private static bool LooksNumeric(string cell) =>
        cell.Length == 0 || cell == "-" || char.IsAsciiDigit(cell[0]) || (cell.Length > 1 && cell[0] is '-' or '+' or '$' && char.IsAsciiDigit(cell[1]));
}
