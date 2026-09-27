using DotDbg.Util;

namespace DotDbg.Engine;

internal static class IlBreakpointLocation
{
    internal static bool TryParse(
        string location,
        out string method,
        out int ilOffset,
        out string error
    )
    {
        method = string.Empty;
        ilOffset = 0;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(location))
        {
            error = "missing IL location";
            return false;
        }

        var rest = location;
        var bangIndex = rest.IndexOf('!');
        if (bangIndex >= 0)
        {
            rest = rest.Substring(bangIndex + 1).Trim();
        }

        var colonIndex = rest.LastIndexOf(':');
        if (colonIndex <= 0)
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        var methodSpec = rest.Substring(0, colonIndex).Trim();
        var offsetSpec = rest.Substring(colonIndex + 1).Trim();

        if (string.IsNullOrWhiteSpace(methodSpec) || string.IsNullOrWhiteSpace(offsetSpec))
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        if (!IlOffsetParser.TryParse(offsetSpec, out ilOffset))
        {
            error = "invalid IL offset";
            return false;
        }

        if (!methodSpec.Contains('.'))
        {
            error = "invalid IL location. Use Type.Method:IL_0000 or Type.Method:0x1f";
            return false;
        }

        method = methodSpec;
        return true;
    }

    internal static string BuildMethodKey(string? moduleName, string method) =>
        string.IsNullOrWhiteSpace(moduleName) ? method : $"{moduleName}!{method}";

    internal static (string? ModuleName, string MethodName) SplitMethodKey(string methodKey)
    {
        var bangIndex = methodKey.IndexOf('!');
        if (bangIndex < 0)
            return (null, methodKey);

        return (
            methodKey.Substring(0, bangIndex).Trim(),
            methodKey.Substring(bangIndex + 1).Trim()
        );
    }
}
