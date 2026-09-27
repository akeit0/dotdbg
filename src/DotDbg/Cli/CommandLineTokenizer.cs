using System.Text;

namespace DotDbg.Cli;

/// <summary>
/// Tokenizes command lines and splits inline command scripts (semicolon-separated)
/// while respecting quotes and escape sequences.
/// </summary>
public static class CommandLineTokenizer
{
    /// <summary>
    /// Tokenizes a single command line into whitespace-separated tokens, respecting
    /// double and single quotes and "\" escapes. Empty quoted arguments are preserved.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string line)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        var tokenStarted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '\\' && i + 1 < line.Length && IsEscapable(line[i + 1]))
            {
                tokenStarted = true;
                current.Append(line[i + 1]);
                i++;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                tokenStarted = true;
                if (quote == c)
                {
                    quote = null;
                    continue;
                }

                if (quote is null)
                {
                    quote = c;
                    continue;
                }
            }

            if (char.IsWhiteSpace(c) && quote is null)
            {
                if (tokenStarted)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
                continue;
            }

            tokenStarted = true;
            current.Append(c);
        }

        if (quote is not null)
            throw new CommandParseException($"Unclosed quote: {quote}");

        if (tokenStarted)
            parts.Add(current.ToString());

        return parts;
    }

    /// <summary>
    /// Splits an inline command script into individual commands separated by semicolons.
    /// Quotes and escaped semicolons are respected. Escaped quotes are kept as literal
    /// characters and do not open or close quoting for splitting purposes.
    /// </summary>
    public static IReadOnlyList<string> SplitScript(string script)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char? quote = null;

        for (var i = 0; i < script.Length; i++)
        {
            var c = script[i];

            if (c == '"' || c == '\'')
            {
                if (!IsEscaped(script, i))
                {
                    if (quote == c)
                    {
                        quote = null;
                    }
                    else if (quote is null)
                    {
                        quote = c;
                    }
                }

                current.Append(c);
                continue;
            }

            if (c == ';')
            {
                if (!IsEscaped(script, i) && quote is null)
                {
                    var segment = current.ToString().Trim();
                    if (segment.Length > 0)
                        parts.Add(segment);
                    current.Clear();
                    quote = null;
                    continue;
                }

                if (IsEscaped(script, i))
                {
                    // The escaping backslash is consumed; the semicolon is literal.
                    if (current.Length > 0 && current[current.Length - 1] == '\\')
                        current.Length--;
                }

                current.Append(';');
                continue;
            }

            current.Append(c);
        }

        if (quote is not null)
            throw new CommandParseException($"Unclosed quote in script: {quote}");

        var last = current.ToString().Trim();
        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }

    private static bool IsEscapable(char c) => c == '"' || c == '\'' || c == '\\';

    /// <summary>
    /// Returns true if the character at <paramref name="index"/> is escaped by an
    /// immediately preceding odd number of backslash characters.
    /// </summary>
    private static bool IsEscaped(string s, int index)
    {
        var count = 0;
        for (var i = index - 1; i >= 0 && s[i] == '\\'; i--)
            count++;
        return (count & 1) == 1;
    }
}
