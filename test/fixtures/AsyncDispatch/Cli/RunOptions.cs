namespace AsyncDispatch.Cli;

internal sealed record RunOptions(string InputPath, int RetryCount, bool Explain)
{
    public const string HelpText = """
        AsyncDispatch: send jobs through an asynchronous gateway.

        Usage: dotnet run -- [--input PATH] [--retries 0..3] [--explain]

        --input PATH  Job CSV file (default: jobs.csv)
        --retries N   Additional attempts after the first call (default: 0)
        --explain     Show the number of gateway calls for each job
        --help, -h    Show this help

        Environment: DISPATCH_FAIL_ONCE=JOB_ID makes the gateway reject that job's
        first call with a transient BUSY response. A retry should recover it.
        """;

    public static bool WantsHelp(string[] args) =>
        args.Contains("--help", StringComparer.Ordinal)
        || args.Contains("-h", StringComparer.Ordinal);

    public static RunOptions Parse(string[] args)
    {
        var input = "jobs.csv";
        var retries = 0;
        var explain = false;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--input":
                    input = ValueAfter(args, ref index);
                    break;
                case "--retries":
                    if (
                        !int.TryParse(ValueAfter(args, ref index), out retries)
                        || retries is < 0 or > 3
                    )
                        throw new ArgumentException("--retries must be an integer from 0 to 3");
                    break;
                case "--explain":
                    explain = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[index]}");
            }
        }

        return new RunOptions(input, retries, explain);
    }

    private static string ValueAfter(string[] args, ref int index)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Missing value after {args[index - 1]}");
        return args[index];
    }
}
