# Inspect a thrown exception

The [exception target](../../samples/DbgTargetExceptionApp/README.md) rejects order A100. It can handle the exception itself or let it terminate the app. The default `catch user` stops at user-code throws and unhandled exceptions. `catch all` stops on every throw, `catch unhandled` stops only before termination, and `catch none` lets exceptions run.

Run from the repository root in PowerShell:

```powershell
$dbg = (Resolve-Path src/DotDbg/bin/Debug/net10.0/DotDbg.dll).Path
$session = "exception-investigation"

dotnet $dbg -s $session --json file samples/DbgTargetExceptionApp/DbgTargetExceptionApp.csproj -c Debug
dotnet $dbg -s $session --json catch all
dotnet $dbg -s $session --json run --wait --timeout 10 -- handled
dotnet $dbg -s $session --json info exception
dotnet $dbg -s $session --json context
```

The single `run --wait` call launches the target and returns its first stop: `reason: "exception"`. `info exception` reports `InvalidOperationException` and `order A100 was rejected`; `context` shows `orderId=A100` at the throw. Continue to let the app's handler run:

```powershell
dotnet $dbg -s $session --json continue
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json wait --timeout 1
dotnet $dbg -s $session --json info status
```

Both `wait` calls report `reason: "exited"` with `exitCode: 0`. Repeating `wait` after exit returns the known terminal result immediately. `info status` also has `exitCode: 0` and `hasProcess: false`.

To let a handled exception run without stopping, change the setting and launch again:

```powershell
dotnet $dbg -s $session --json catch none
dotnet $dbg -s $session --json run --wait --timeout 10 -- handled
dotnet $dbg -s $session --json output --channel stdout
```

The last run reaches exit directly and stdout includes `handled: order A100 was rejected`. `catch` without a mode queries the current setting. With `run --wait`, the default wait timeout is 30 seconds; on timeout the command fails but the target keeps running. Use `wait` again or `interrupt` to inspect it.

Now try the unhandled path:

```powershell
dotnet $dbg -s $session --json run --wait --timeout 10 -- unhandled
dotnet $dbg -s $session --json events --kind stop
dotnet $dbg -s $session --json output --channel stderr
```

The exit response and stop event include `unhandledException: true`, which comes from the CLR exception callback. The target prints a stack trace to stderr. This sample has reported OS `exitCode: 0` while debugged, so inspect `unhandledException` as well as `exitCode` when determining whether an investigation found a failure. The handled runs report `unhandledException: false`.

To stop only for the failing throw, switch modes and compare both target paths:

```powershell
dotnet $dbg -s $session --json catch unhandled
dotnet $dbg -s $session --json run --wait --timeout 10 -- handled
dotnet $dbg -s $session --json run --wait --timeout 10 -- unhandled
dotnet $dbg -s $session --json info exception
dotnet $dbg -s $session --json context
dotnet $dbg -s $session --json continue
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json quit
```

The handled run exits without an exception stop. The unhandled run stops at the throw with `unhandledException: true`; `info exception` reports the exception type and message while the frame is still available. Continue to receive the final exit result.
