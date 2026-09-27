#:property PublishAot=false
#:property TargetFramework=net10.0

using System.Diagnostics;

var loop = args.Length > 0 && args[0].Equals("loop", StringComparison.OrdinalIgnoreCase);

Console.WriteLine($"File-based sample started, PID: {Process.GetCurrentProcess().Id}");

var count = 0;
while (loop || count < 3)
{
    count++;
    Greet(count);
    var result = Calculate(count, count + 1);
    Console.WriteLine($"Result: {result}");

    if (loop)
        Thread.Sleep(100);
}

Console.WriteLine("File-based sample finished");

static void Greet(int n)
{
    var message = $"Hello {n}";
    Console.WriteLine(message);
}

static int Calculate(int a, int b)
{
    var product = a * b;
    return product + 1;
}
