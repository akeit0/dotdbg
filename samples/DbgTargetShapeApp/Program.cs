using System.Diagnostics;

namespace DbgTargetShapeApp;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine($"Shape app started, PID: {Process.GetCurrentProcess().Id}");

        bool loop = args.Length > 0 && args[0].Equals("loop", StringComparison.OrdinalIgnoreCase);
        var service = new ShapeService();
        service.ShapeAdded += OnShapeAdded;

        try
        {
            var shapes = BuildShapes();
            foreach (var shape in shapes)
            {
                service.Add(shape);
            }

            var iteration = 0;
            while (loop || iteration < 3)
            {
                iteration++;
                Console.WriteLine($"--- iteration {iteration} ---");

                var totalArea = service.TotalArea();
                var averagePerimeter = service.AveragePerimeter();
                var largest = service.FindLargest();

                Console.WriteLine($"Total area: {totalArea:F2}");
                Console.WriteLine($"Average perimeter: {averagePerimeter:F2}");
                Console.WriteLine($"Largest shape: {largest?.Describe() ?? "none"}");

                var metrics = await ComputeMetricsAsync(service).ConfigureAwait(false);
                foreach (var metric in metrics)
                {
                    PrintMetric(metric);
                }

                if (iteration % 2 == 0)
                {
                    service.ScaleAll(1.1);
                }

                if (iteration == 3 && !loop)
                {
                    TryDivideByZero(iteration);
                }

                if (loop)
                    await Task.Delay(100).ConfigureAwait(false);
            }
        }
        catch (InvalidShapeException ex)
        {
            Console.WriteLine($"Invalid shape: {ex.Message}, value={ex.Value}");
        }
        catch (DivideByZeroException ex)
        {
            Console.WriteLine($"Divide by zero: {ex.Message}");
        }

        Console.WriteLine("Shape app finished");
    }

    static List<Shape> BuildShapes()
    {
        var shapes = new List<Shape>
        {
            new Circle(5.0),
            new Rectangle(3.0, 4.0),
            new Circle(2.5),
            new Rectangle(6.0, 6.0),
        };
        return shapes;
    }

    static async Task<IReadOnlyList<ShapeMetrics>> ComputeMetricsAsync(ShapeService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        var metrics = await service.ComputeMetricsAsync().ConfigureAwait(false);
        var sorted = metrics.OrderByDescending(m => m.Area).ToList().AsReadOnly();
        return sorted;
    }

    static void PrintMetric(ShapeMetrics metric)
    {
        var ratio = metric.Perimeter > 0 ? metric.Area / metric.Perimeter : 0.0;
        Console.WriteLine(
            $"{metric.Name}: area={metric.Area:F2}, perimeter={metric.Perimeter:F2}, ratio={ratio:F4}"
        );
    }

    static void OnShapeAdded(object? sender, ShapeEventArgs e)
    {
        Console.WriteLine($"Added: {e.Shape}");
    }

    static void TryDivideByZero(int iteration)
    {
        var numerator = 42;
        var denominator = 0;
        var result = numerator / denominator;
        Console.WriteLine($"Iteration {iteration} result: {result}");
    }
}
