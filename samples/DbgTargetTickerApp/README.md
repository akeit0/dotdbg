# Ticker target

This finite console app emits `tick N` on stdout, even tick warnings on stderr, and `checkpoint N` through `Debug.WriteLine`. Its `--ticks` and `--delay-ms` options accept positive integers. Defaults are six ticks and 150 ms between ticks.

From the repository root:

```powershell
dotnet run --project samples/DbgTargetTickerApp -- --ticks 4 --delay-ms 100
```

The `BREAK_POINT` comment identifies the line to use for repeated breakpoint or tracepoint experiments. Follow the [output use case](../../docs/use-cases/follow-ticker-output.md) to capture the three message channels and page through session events.
The [attach use case](../../docs/use-cases/attach-to-ticker.md) uses the same target as an external process.
