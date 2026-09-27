# Exception target

Run with `handled` to throw and catch an `InvalidOperationException`, then exit normally. Run with `unhandled` (the default) to let the same exception terminate the app. The `THROW_POINT` comment locates the source line for debugger experiments.

```powershell
dotnet run --project samples/DbgTargetExceptionApp -- handled
```

See the [exception investigation](../../docs/use-cases/inspect-exception.md) for `catch all`, `catch unhandled`, `catch none`, and exit outcomes.
