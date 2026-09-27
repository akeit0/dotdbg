# Follow a running app's messages

The [ticker target](../../samples/DbgTargetTickerApp/README.md) runs for a fixed number of ticks. It writes ticks to stdout, even tick warnings to stderr, and checkpoints through `Debug.WriteLine`. This makes it possible to check that target messages stay separate from command JSON and to poll them with a cursor.

Run from the repository root in PowerShell:

```powershell
$dbg = (Resolve-Path src/DotDbg/bin/Debug/net10.0/DotDbg.dll).Path
$session = "ticker-investigation"

dotnet $dbg -s $session --json file samples/DbgTargetTickerApp/DbgTargetTickerApp.csproj -c Debug
dotnet $dbg -s $session --json run -- --ticks 8 --delay-ms 250
dotnet $dbg -s $session --json output --limit 5
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json output --channel stderr
dotnet $dbg -s $session --json events --kind stop
```

The first `output` can return any prefix of the messages because the app is still running. After `wait` reports `reason: "exited"`, the filtered output contains `warning tick 2`, `4`, `6`, and `8`. `events --kind stop` contains the exit event. `Debug.WriteLine` checkpoints appear on the `debug` channel:

```powershell
dotnet $dbg -s $session --json output --channel debug
```

For incremental reading, pass `--since 0` to start with the oldest retained event and use each response's `data.nextSeq` for the next page. `data.hasMore` tells you whether another page is already available. This example pages through the combined event stream in groups of three:

```powershell
$cursor = 0
do {
    $page = dotnet $dbg -s $session --json events --since $cursor --limit 3 | ConvertFrom-Json
    $page.data.events | Select-Object seq, kind, channel, text, reason
    $cursor = $page.data.nextSeq
} while ($page.data.hasMore)

dotnet $dbg -s $session --json events --since $cursor
dotnet $dbg -s $session --json quit
```

The last `events` call returns no events until something new occurs. For a long-running target, repeat that call later with the same cursor. A filter such as `output --channel stderr` is useful for a quick diagnostic, but use unfiltered `events` when one cursor must cover messages, tracepoints, and stops. The daemon keeps the latest 1,024 events; `data.truncated` signals that an older range has been lost.
