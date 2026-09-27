var mode = args.Length > 0 ? args[0] : "default";
var marker = Environment.GetEnvironmentVariable("DOTDBG_MARKER") ?? "<unset>";
var workDirectory = Environment.CurrentDirectory;

for (var index = 0; index < 3; index++)
{
    var result = Format(mode, marker, index);
    Console.WriteLine($"{workDirectory}: {result}");
}

static string Format(string mode, string marker, int index)
{
    var prefix = mode == "upper" ? marker.ToUpperInvariant() : marker;
    var result = $"{prefix}:{index}:{mode}";
    Console.Error.WriteLine($"agent stderr {index}");
    System.Diagnostics.Debug.WriteLine($"agent debug {index}");
    return result;
}
