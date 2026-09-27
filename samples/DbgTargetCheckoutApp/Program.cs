using System.Diagnostics;
using System.Globalization;

var tierIndex = Array.IndexOf(args, "--tier");
var tier = tierIndex >= 0 && tierIndex + 1 < args.Length ? args[tierIndex + 1] : "standard";
var rateText = Environment.GetEnvironmentVariable("DOTDBG_DISCOUNT") ?? "0.10";
var rate = tier.Equals("vip", StringComparison.OrdinalIgnoreCase)
    ? double.Parse(rateText, CultureInfo.InvariantCulture)
    : 0.0;

var mismatchCount = 0;
foreach (var line in File.ReadLines("orders.csv").Skip(1))
{
    var fields = line.Split(',');
    var order = new Order(
        fields[0],
        double.Parse(fields[1], CultureInfo.InvariantCulture),
        double.Parse(fields[2], CultureInfo.InvariantCulture)
    );
    var actual = PriceOrder(order, rate);
    var expected = order.Subtotal * (1 - rate) + order.Shipping;
    // TRACE_POINT: the value returned by PriceOrder is ready for comparison.
    Console.WriteLine($"order {order.Id}: actual={actual:F2} expected={expected:F2}");
    if (Math.Abs(actual - expected) > 0.001)
    {
        Console.Error.WriteLine($"mismatch {order.Id}: expected {expected:F2}, got {actual:F2}");
        mismatchCount++;
    }
}
Environment.ExitCode = mismatchCount == 0 ? 0 : 1;

static double PriceOrder(Order order, double rate)
{
    var subtotal = order.Subtotal;
    var discount = subtotal * rate;
    var discounted = subtotal - discount;
    // Intentional bug: discount is subtracted a second time.
    var total = discounted - discount + order.Shipping;
    Debug.WriteLine($"pricing {order.Id}: subtotal={subtotal:F2} discount={discount:F2}");
    // BREAK_POINT: inspect subtotal, discount, discounted, and total here.
    return total;
}

internal sealed record Order(string Id, double Subtotal, double Shipping);
