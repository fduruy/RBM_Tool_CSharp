using System.Globalization;

namespace RbmTool.Core.Inputs;

/// <summary>
/// Numeric parsing tolerant to European formats (port of parse_numeric_value in utils.py).
/// Supports: -2,329.65 (comma = thousands, dot = decimal),
///           -2.329,65 (dot = thousands, comma = decimal),
///           -2329.65, -2,32965.
/// Always returns a double with English decimal separator.
/// </summary>
public static class NumericParser
{
    public static double Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0.0;
        }

        var s = value.Trim();

        // Both separators present: determine which is which by their last position
        if (s.Contains('.') && s.Contains(','))
        {
            var lastDot = s.LastIndexOf('.');
            var lastComma = s.LastIndexOf(',');
            if (lastDot > lastComma)
            {
                s = s.Replace(",", "");          // -2,329.65
            }
            else
            {
                s = s.Replace(".", "").Replace(',', '.'); // -2.329,65
            }
        }
        else if (s.Contains(',') && !s.Contains('.'))
        {
            // Only comma: decimal if followed by 1-3 digits, otherwise thousands
            var commaPos = s.LastIndexOf(',');
            var after = s[(commaPos + 1)..];
            s = after.Length <= 3
                ? s.Replace(',', '.')
                : s.Replace(",", "");
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0.0;
    }
}
