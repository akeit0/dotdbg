# dotdbg

`dotdbg` is a command-line debugger for managed .NET applications. It keeps a debug session alive between short CLI calls, which makes it useful for AI agents and terminal automation.

Install with the .NET 10 SDK:

```shell
dotnet tool install --global DotDbg
dotdbg --help
```

Load a project, set a breakpoint, and inspect a stop:

```shell
dotdbg file MyApp.csproj -c Debug
dotdbg break Program.cs:20
dotdbg run --wait -- --my-app-option
dotdbg context
dotdbg print myVariable
dotdbg quit
```

Use `--json` for structured results, `source --last -c 'command; command'` to batch known steps, `help all` for command syntax, and `schema` for machine-readable command discovery. The target needs managed .NET runtime support and symbols for source debugging. Windows and Linux are tested.

License: MIT. The debugger engine includes SharpDbg, licensed separately under MIT.
