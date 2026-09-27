using System.Text.Json.Nodes;

namespace DotDbg.Util;

internal static class ResponsePathPresenter
{
    private static readonly HashSet<string> PathFields =
    [
        "file",
        "filePath",
        "path",
        "source",
        "target",
    ];

    public static void MakePathsRelative(JsonObject response, string workingDirectory)
    {
        var root = Path.GetFullPath(workingDirectory);
        var prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        Visit(response, null, root, prefix, comparison);
    }

    private static void Visit(
        JsonNode node,
        string? parentKey,
        string root,
        string prefix,
        StringComparison comparison
    )
    {
        if (node is JsonObject obj)
        {
            foreach (var (key, child) in obj.ToList())
            {
                if (child is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    if (PathFields.Contains(key))
                        obj[key] = RelativePath(text, root, prefix, comparison);
                    else if (
                        key is "message" or "description" or "stackTrace"
                        || key == "text" && obj["kind"]?.GetValue<string>() == "stop"
                    )
                        obj[key] = ShortenPathsInText(text, prefix, comparison);
                }
                else if (child is not null)
                {
                    Visit(child, key, root, prefix, comparison);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (var i = 0; i < array.Count; i++)
            {
                var child = array[i];
                if (
                    parentKey == "files"
                    && child is JsonValue value
                    && value.TryGetValue<string>(out var path)
                )
                    array[i] = RelativePath(path, root, prefix, comparison);
                else if (child is not null)
                    Visit(child, null, root, prefix, comparison);
            }
        }
    }

    private static string RelativePath(
        string path,
        string root,
        string prefix,
        StringComparison comparison
    )
    {
        if (!Path.IsPathFullyQualified(path))
            return path;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!fullPath.Equals(root, comparison) && !fullPath.StartsWith(prefix, comparison))
                return path;
            return Path.GetRelativePath(root, fullPath);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string ShortenPathsInText(
        string text,
        string prefix,
        StringComparison comparison
    )
    {
        var shortened = text.Replace(prefix, string.Empty, comparison);
        if (OperatingSystem.IsWindows())
            shortened = shortened.Replace(prefix.Replace('\\', '/'), string.Empty, comparison);
        return shortened;
    }
}
