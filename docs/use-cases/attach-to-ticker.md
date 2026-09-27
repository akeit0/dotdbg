# Attach to a running .NET process

The [ticker target](../../samples/DbgTargetTickerApp/README.md) runs long enough to attach and set a source breakpoint. Start it separately, then attach by PID to avoid ambiguity when several `dotnet` processes exist.

From the repository root in PowerShell:

```powershell
$dbg = (Resolve-Path src/DotDbg/bin/Debug/net10.0/DotDbg.dll).Path
$target = (Resolve-Path samples/DbgTargetTickerApp/bin/Debug/net10.0/DbgTargetTickerApp.dll).Path
$session = "attach-investigation"
$stdoutPath = Join-Path $env:TEMP "dotdbg-ticker-$([guid]::NewGuid().ToString('N')).out"
$stderrPath = Join-Path $env:TEMP "dotdbg-ticker-$([guid]::NewGuid().ToString('N')).err"
$ticker = Start-Process -FilePath dotnet -ArgumentList @("`"$target`"", "--ticks", "80", "--delay-ms", "200") -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath
$line = (Select-String -LiteralPath samples/DbgTargetTickerApp/Program.cs -Pattern 'BREAK_POINT').LineNumber + 1

dotnet $dbg -s $session --json attach $ticker.Id
dotnet $dbg -s $session --json break "samples/DbgTargetTickerApp/Program.cs:$line"
dotnet $dbg -s $session --json wait --timeout 5
dotnet $dbg -s $session --json context
dotnet $dbg -s $session --json detach
$ticker.Refresh()
$ticker.HasExited
dotnet $dbg -s $session --json quit
```

The breakpoint stop shows a `tick` local in `context`. After `detach`, `$ticker.HasExited` should be `False`; `quit` then closes only the debugger session. The externally launched process keeps its original stdout and stderr streams, so inspect `$stdoutPath` and `$stderrPath` for console messages. `output` can still capture debugger messages while attached.

Clean up the finite sample when finished:

```powershell
$ticker.Refresh()
if (-not $ticker.HasExited) { Stop-Process -Id $ticker.Id -Force }
Remove-Item -LiteralPath $stdoutPath, $stderrPath -ErrorAction SilentlyContinue
```
