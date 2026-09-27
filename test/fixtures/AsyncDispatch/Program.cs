using AsyncDispatch.Cli;
using AsyncDispatch.Data;
using AsyncDispatch.Gateway;
using AsyncDispatch.Processing;
using AsyncDispatch.Reporting;

if (RunOptions.WantsHelp(args))
{
    Console.WriteLine(RunOptions.HelpText);
    return;
}

try
{
    var options = RunOptions.Parse(args);
    var jobs = JobCsvReader.Read(options.InputPath);
    var gateway = new SimulatedGateway(Environment.GetEnvironmentVariable("DISPATCH_FAIL_ONCE"));
    var coordinator = new DispatchCoordinator(new RetryPolicy(gateway));
    var results = await coordinator.RunAsync(jobs, options);
    var report = new ConsoleReport(options.Explain);
    Environment.ExitCode = report.Write(results);
}
catch (Exception error) when (error is ArgumentException or FormatException or IOException)
{
    Console.Error.WriteLine($"input error: {error.Message}");
    Environment.ExitCode = 2;
}
