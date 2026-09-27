using AsyncDispatch.Domain;

namespace AsyncDispatch.Reporting;

internal sealed class ConsoleReport(bool explain)
{
    public int Write(IReadOnlyList<DispatchResult> results)
    {
        var failed = 0;
        foreach (var result in results)
        {
            if (result.Delivered)
            {
                Console.WriteLine($"{result.Job.Id}: delivered amount={result.Job.Amount:0.00}");
            }
            else
            {
                failed++;
                Console.Error.WriteLine($"{result.Job.Id}: failed code={result.FailureCode}");
            }

            if (explain)
                Console.WriteLine($"  {result.Job.Id}: gateway calls={result.GatewayCalls}");
        }

        Console.WriteLine($"summary: delivered={results.Count - failed} failed={failed}");
        return failed == 0 ? 0 : 1;
    }
}
