# Agent launch sample

This target exposes a command-line argument, an environment variable, and the working directory as locals for debugger checks. The loop provides repeated breakpoint hits and conditional-breakpoint coverage.

```powershell
dotdbg file samples/DbgTargetAgentApp/DbgTargetAgentApp.csproj -c Debug
dotdbg break samples/DbgTargetAgentApp/Program.cs:15 if index == 1
dotdbg run --env "DOTDBG_MARKER=hello world" --cwd samples/DbgTargetAgentApp upper
dotdbg wait --timeout 10
dotdbg info args
dotdbg info locals
dotdbg print result
dotdbg quit
```

Use `info args` to check `mode`, `marker`, and `index`; `info locals` shows `prefix` and `result`.
