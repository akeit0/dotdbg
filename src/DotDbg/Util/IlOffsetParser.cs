using System.Globalization;

namespace DotDbg.Util;

internal static class IlOffsetParser
{
    // IL_ and 0x are hexadecimal; an unprefixed offset is decimal.
    internal static bool TryParse(string text, out int offset)
    {
        offset = 0;
        var hexadecimal =
            text.StartsWith("IL_", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        var digits =
            text.StartsWith("IL_", StringComparison.OrdinalIgnoreCase) ? text[3..]
            : text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..]
            : text;

        if (digits.Length == 0 || !digits.All(hexadecimal ? Uri.IsHexDigit : IsDecimalDigit))
            return false;

        return int.TryParse(
                digits,
                hexadecimal ? NumberStyles.HexNumber : NumberStyles.None,
                CultureInfo.InvariantCulture,
                out offset
            )
            && offset >= 0;
    }

    private static bool IsDecimalDigit(char c) => c is >= '0' and <= '9';
}
