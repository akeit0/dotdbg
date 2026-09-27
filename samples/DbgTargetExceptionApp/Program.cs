var mode = args.FirstOrDefault() ?? "unhandled";
if (mode is not ("handled" or "unhandled"))
    throw new ArgumentException("Use handled or unhandled");

Console.WriteLine($"exception mode={mode}");
if (mode == "handled")
{
    try
    {
        RejectOrder();
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"handled: {ex.Message}");
    }
}
else
{
    RejectOrder();
}

static void RejectOrder()
{
    var orderId = "A100";
    // THROW_POINT: inspect the order and current exception here.
    throw new InvalidOperationException($"order {orderId} was rejected");
}
