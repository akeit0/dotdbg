namespace DotDbg.Cli;

/// <summary>
/// Parses source locations of the form file:line or file:line:column.
/// Handles Windows drive-letter paths like C:\file.cs:10.
/// </summary>
public static class LocationParser
{
    public static bool TryParse(
        string location,
        out string? filePath,
        out int line,
        out int? column
    )
    {
        filePath = null;
        line = 0;
        column = null;

        if (string.IsNullOrWhiteSpace(location))
            return false;

        var parts = location.Split(':');
        if (parts.Length < 2)
            return false;

        // Handle Windows drive-letter paths like C:\file.cs:10
        var driveIndex = 0;
        if (
            parts[0].Length == 1
            && char.IsLetter(parts[0][0])
            && parts[1].Length > 0
            && (parts[1][0] == '\\' || parts[1][0] == '/')
        )
        {
            driveIndex = 1;
        }

        var numericParts = parts.Length - driveIndex - 1;

        if (numericParts == 1 && int.TryParse(parts[^1], out var lineOnly) && lineOnly > 0)
        {
            filePath = string.Join(':', parts, 0, parts.Length - 1);
            line = lineOnly;
            return true;
        }

        if (
            numericParts == 2
            && int.TryParse(parts[^1], out var col)
            && col > 0
            && int.TryParse(parts[^2], out var line2)
            && line2 > 0
        )
        {
            filePath = string.Join(':', parts, 0, parts.Length - 2);
            line = line2;
            column = col;
            return true;
        }

        return false;
    }
}
