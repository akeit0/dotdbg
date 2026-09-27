namespace DotDbg.Util;

internal static class EnvironmentNameComparer
{
    internal static StringComparer Instance =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
