using System;
using System.Diagnostics;
using System.Threading;

namespace DbgTargetSampleApp;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine($"Sample app started, PID: {Process.GetCurrentProcess().Id}");

        bool loop = args.Length > 0 && args[0].Equals("loop", StringComparison.OrdinalIgnoreCase);

        int count = 0;
        while (loop || count < 3)
        {
            count++;
            Greet(count);
            var result = Calculate(count, count + 1);
            Console.WriteLine($"Result: {result}");

            if (loop)
                Thread.Sleep(100);
        }

        Console.WriteLine("Sample app finished");
    }

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
}
