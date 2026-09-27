using System.Diagnostics;

var ticks = ReadOption(args, "--ticks", 6);
var delayMs = ReadOption(args, "--delay-ms", 150);
Console.WriteLine($"ticker pid={Environment.ProcessId}");

for (var tick = 1; tick <= ticks; tick++)
{
    // BREAK_POINT: this line runs once per tick.
    Console.WriteLine($"tick {tick}");
    if (tick % 2 == 0)
        Console.Error.WriteLine($"warning tick {tick}");
    Debug.WriteLine($"checkpoint {tick}");
    await Task.Delay(delayMs);
}

static int ReadOption(string[] args, string name, int defaultValue)
{
    var index = Array.IndexOf(args, name);
    if (index < 0)
        return defaultValue;
    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var value) || value < 1)
        throw new ArgumentException($"{name} requires a positive integer");
    return value;
}
