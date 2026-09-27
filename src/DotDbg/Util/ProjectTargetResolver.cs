using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotDbg.Util;

/// <summary>
/// Builds a .csproj or file-based app (.cs) and resolves its output assembly (TargetPath).
/// Uses a real <c>dotnet build</c> first, then <c>-getProperty:TargetPath</c>. Evaluating
/// TargetPath alone does not always produce outputs for file-based apps.
/// </summary>
internal static class ProjectTargetResolver
{
    public static bool IsBuildableTarget(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);

    public static async Task<ProjectResolveResult> BuildAndResolveAsync(
        string targetPath,
        string? configuration,
        IReadOnlyList<string> properties,
        CancellationToken cancellationToken
    )
    {
        if (!File.Exists(targetPath))
            return ProjectResolveResult.Fail($"Target not found: {targetPath}");

        var buildArgs = new List<string> { "build", targetPath, "-v:q", "--nologo" };
        AppendBuildOptions(buildArgs, configuration, properties);

        var build = await RunDotnetAsync(buildArgs, cancellationToken).ConfigureAwait(false);
        if (build.ExitCode != 0)
        {
            var detail = CombineOutput(build.StdOut, build.StdErr);
            return ProjectResolveResult.Fail(
                string.IsNullOrWhiteSpace(detail)
                    ? $"dotnet build failed with exit code {build.ExitCode}"
                    : $"dotnet build failed with exit code {build.ExitCode}:{Environment.NewLine}{detail}"
            );
        }

        var getArgs = new List<string>
        {
            "build",
            targetPath,
            "-v:q",
            "--nologo",
            "--no-restore",
            "-getProperty:TargetPath",
        };
        AppendBuildOptions(getArgs, configuration, properties);

        var get = await RunDotnetAsync(getArgs, cancellationToken).ConfigureAwait(false);
        if (get.ExitCode != 0)
        {
            var detail = CombineOutput(get.StdOut, get.StdErr);
            return ProjectResolveResult.Fail(
                string.IsNullOrWhiteSpace(detail)
                    ? $"Failed to resolve TargetPath (exit code {get.ExitCode})"
                    : $"Failed to resolve TargetPath (exit code {get.ExitCode}):{Environment.NewLine}{detail}"
            );
        }

        if (!TryParseTargetPath(get.StdOut, out var outputPath, out var parseError))
            return ProjectResolveResult.Fail(parseError ?? "Failed to parse TargetPath");

        if (string.IsNullOrWhiteSpace(outputPath))
            return ProjectResolveResult.Fail("TargetPath is empty");

        if (!File.Exists(outputPath))
            return ProjectResolveResult.Fail($"Built output not found: {outputPath}");

        return ProjectResolveResult.Ok(outputPath);
    }

    private static void AppendBuildOptions(
        List<string> args,
        string? configuration,
        IReadOnlyList<string> properties
    )
    {
        if (!string.IsNullOrWhiteSpace(configuration))
        {
            args.Add("-c");
            args.Add(configuration);
        }

        foreach (var property in properties)
            args.Add($"-p:{property}");
    }

    private static bool TryParseTargetPath(string stdout, out string? targetPath, out string? error)
    {
        targetPath = null;
        error = null;
        var text = stdout.Trim();
        if (string.IsNullOrEmpty(text))
        {
            error = "dotnet build -getProperty:TargetPath returned no output";
            return false;
        }

        if (text.StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (
                    doc.RootElement.TryGetProperty("Properties", out var props)
                    && props.TryGetProperty("TargetPath", out var pathElement)
                )
                {
                    targetPath = pathElement.GetString();
                    return true;
                }

                error = "TargetPath property missing from build JSON output";
                return false;
            }
            catch (JsonException ex)
            {
                error = $"Invalid build JSON output: {ex.Message}";
                return false;
            }
        }

        var lines = text.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (
                line.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || line.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            )
            {
                var arrow = line.LastIndexOf("->", StringComparison.Ordinal);
                targetPath = arrow >= 0 ? line[(arrow + 2)..].Trim() : line;
                return true;
            }
        }

        var fallback = lines.LastOrDefault();
        if (string.IsNullOrWhiteSpace(fallback))
        {
            error = "dotnet build -getProperty:TargetPath returned empty TargetPath";
            return false;
        }

        targetPath = fallback;
        return true;
    }

    private static async Task<DotnetResult> RunDotnetAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

        if (!process.Start())
            return new DotnetResult(-1, string.Empty, "Failed to start dotnet process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best-effort cleanup.
            }

            throw;
        }

        return new DotnetResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static string CombineOutput(string stdout, string stderr)
    {
        stdout = stdout.Trim();
        stderr = stderr.Trim();
        if (stdout.Length == 0)
            return stderr;
        if (stderr.Length == 0)
            return stdout;
        return stdout + Environment.NewLine + stderr;
    }

    private sealed record DotnetResult(int ExitCode, string StdOut, string StdErr);
}

internal sealed record ProjectResolveResult(bool Success, string? TargetPath, string? Error)
{
    public static ProjectResolveResult Ok(string targetPath) => new(true, targetPath, null);

    public static ProjectResolveResult Fail(string error) => new(false, null, error);
}
