# dotdbg

`dotdbg` is a command-line debugger for managed .NET programs, designed for AI agents and terminal users. Each command is a short-lived process; a background daemon keeps the target and debugging state alive between calls. The debugger engine is the [`sharpdbg`](sharpdbg/README.md) submodule.

It supports source and IL breakpoints, stepping, expression evaluation, watches, exception stops, stack inspection, and captured target output. Commands have familiar names such as `file`, `break`, `run`, `next`, and `backtrace`. `--json` returns structured responses, and `schema` describes the command surface.

## Requirements

- .NET 10 SDK to build and run dotdbg.
- A managed .NET target with symbols for source breakpoints and stepping.

The debugger workload tests run on Windows and Linux. macOS has not been tested. Windows is not required.

## Install

```shell
dotnet tool install --global DotDbg --version 0.1.0
dotdbg --help
```

## Build

From the repository root, initialize the debugger engine, then build and run the tests:

```shell
git submodule update --init --recursive
dotnet build DotDbg.slnx
dotnet test DotDbg.slnx
dotnet src/DotDbg/bin/Debug/net10.0/DotDbg.dll --help
```

The build restores `Microsoft.Diagnostics.DbgShim`. The test suite includes live debugger workloads that launch sample apps and attach to a running process. The [cross-platform checks](.github/workflows/cross-platform-checks.yml) workflow runs unit tests on Windows and the full suite on Ubuntu. Run the full suite locally on Windows for live debugger verification; GitHub-hosted Windows runners intermittently fail to attach to target processes.

To package and install the current checkout as a local .NET tool:

```shell
dotnet pack src/DotDbg/DotDbg.csproj -c Release -p:Version=0.1.0 -o artifacts/packages
dotnet tool install DotDbg --tool-path artifacts/tools --add-source artifacts/packages --version 0.1.0
```

Run `./artifacts/tools/dotdbg.exe --help` on Windows or `./artifacts/tools/dotdbg --help` on Linux. The package ID is `DotDbg`; the installed command is `dotdbg`.

## First session

These examples use an installed `dotdbg`. During development, invoke `dotnet src/DotDbg/bin/Debug/net10.0/DotDbg.dll` instead.

```shell
dotdbg file samples/DbgTargetSampleApp/DbgTargetSampleApp.csproj -c Debug
dotdbg break samples/DbgTargetSampleApp/Program.cs:33
dotdbg run --wait
dotdbg context
dotdbg print n
dotdbg continue --wait
dotdbg quit
```

`file` also accepts a built `.dll`, `.exe`, an executable Unix apphost, or a .NET 10 file-based `.cs` app. It builds projects and file-based apps before launch. `run --wait` returns the next stop or exit; `context` shows the selected frame, nearby source, and compact variable values. `quit` ends the daemon and any launched target. Use `detach` first to leave an attached process running.

For structured output and fewer process launches, known setup steps can be batched:

```shell
dotdbg --json source --last -c 'file samples/DbgTargetSampleApp/DbgTargetSampleApp.csproj -c Debug; break samples/DbgTargetSampleApp/Program.cs:33; run --wait'
```

The result includes the first stop and its `reason` (`breakpoint`, `exception`, or `exited`). A session defaults to a key derived from the working directory; use the same directory for each call or supply a stable `--session-id`.

## Explore

| Task | Commands |
| --- | --- |
| Target and session | `file`, `run`, `attach`, `detach`, `kill`, `quit`, `info status` |
| Stop points | `break`, `breakpoints`, `condition`, `ignore`, `trace`, `catch` |
| Execution | `continue`, `wait`, `interrupt`, `next`, `step`, `finish`, `until` |
| Inspection | `context`, `backtrace`, `print`, `list`, `info`, `output`, `events`, `decompile` |
| Automation and discovery | `source`, `--json-input`, `help`, `schema` |

Use `dotdbg help all` for command syntax in one response, `dotdbg help <command>` for examples, and `dotdbg schema` for machine-readable syntax. Put global options before the command name (`dotdbg --json run`). Use `--` before target arguments that might be parsed as debugger options.

For a full session guide, including JSON responses, batching, expressions, and shell quoting, see [Agent workflow](docs/agent-workflow.md). [Debugging use cases](docs/use-cases/README.md) show expected observations on runnable targets.

## Repository layout

- [`src/DotDbg`](src/DotDbg): CLI, daemon, IPC, and debug session.
- [`sharpdbg`](sharpdbg): managed debugger engine, maintained as a separate Git submodule.
- [`test/DotDbg.Tests`](test/DotDbg.Tests) and [`test/DotDbg.WorkloadTests`](test/DotDbg.WorkloadTests): command and live workload tests.
- [`samples`](samples): targets for debugger examples and tests.
- [`docs/use-cases`](docs/use-cases/README.md): runnable investigations.

Repository working instructions for coding agents are in [AGENTS.md](AGENTS.md). See [`sharpdbg`'s license](sharpdbg/LICENSE.txt) for the underlying engine.

For package publishing and version tags, see [Releasing dotdbg](docs/releasing.md). Dotdbg's own code is licensed under [MIT](LICENSE).
