# Working in dotdbg

This file is for agents editing this repository. [README.md](README.md) explains the tool to users; [docs/agent-workflow.md](docs/agent-workflow.md) documents how to operate the debugger. Keep command behavior and examples consistent across the CLI help, README, and workflow docs.

## Repository boundaries

- `src/DotDbg` contains the CLI, daemon, IPC, and debug session. `test/DotDbg.Tests` covers commands; `test/DotDbg.WorkloadTests` exercises live debugging against `samples`.
- `sharpdbg` is a separate Git submodule with its own formatting conventions. Check its status before changing it; keep root and submodule commits distinct.

## Build and validation

```shell
dotnet build DotDbg.slnx
dotnet test DotDbg.slnx
```

The repository pins CSharpier as a local .NET tool. Format and check the root project's C# and project files with:

```shell
dotnet tool restore
dotnet csharpier format src test samples
dotnet csharpier check src test samples
```

Do not apply the root formatting command to `sharpdbg`. The [cross-platform checks](.github/workflows/cross-platform-checks.yml) workflow validates formatting, unit tests, and debugger workloads on Windows and Ubuntu.

## Debugger changes

- Keep `--json` responses suitable for short agent interactions. Preserve `success`, `message`, and command-specific `data` semantics when extending commands.
- Prefer a live workload test when behavior depends on stepping, exceptions, output capture, or process lifecycle. Use command tests for parsing and response shapes.
- Run the relevant tests after edits. For a change to debugger behavior or command syntax, also check the user-facing help and [agent workflow](docs/agent-workflow.md).
- When testing an interactive session, use a unique `--session-id`, reuse it for every call, and finish with `quit`. Detach first if an attached target must keep running.
- For package versions, tags, and NuGet publishing, follow [docs/releasing.md](docs/releasing.md). A tag push starts the publishing workflow.
