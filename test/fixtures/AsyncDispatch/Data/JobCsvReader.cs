using System.Globalization;
using AsyncDispatch.Domain;

namespace AsyncDispatch.Data;

internal static class JobCsvReader
{
    public static IReadOnlyList<DispatchJob> Read(string path)
    {
        var jobs = new List<DispatchJob>();
        foreach (var line in File.ReadLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(',');
            if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]))
                throw new FormatException($"Invalid job row: {line}");

            var amount = decimal.Parse(fields[1], CultureInfo.InvariantCulture);
            var gatewayDelayMs = int.Parse(fields[2], CultureInfo.InvariantCulture);
            if (amount < 0 || gatewayDelayMs < 0)
                throw new FormatException($"Negative amount or delay: {line}");
            jobs.Add(new DispatchJob(fields[0], amount, gatewayDelayMs));
        }

        if (jobs.Count == 0)
            throw new FormatException("No jobs found");
        return jobs;
    }
}
