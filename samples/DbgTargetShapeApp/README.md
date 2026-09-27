# DbgTargetShapeApp

A richer .NET console target for exercising `dotdbg` with classes, interfaces, generics, async/await, records, events, and exception handling.

## Files

- `Shape.cs` — abstract `Shape` base class and `IScalable` interface.
- `Circle.cs` / `Rectangle.cs` — concrete shapes with properties, virtual methods, and `Scale`.
- `InvalidShapeException.cs` — custom exception carrying the offending value.
- `ShapeService.cs` — repository, events, generic collection, async metrics, and `record struct`.
- `Program.cs` — async `Main` loop that scales shapes and finally throws a `DivideByZeroException`.

## Build & run

```bash
dotnet run --project samples/DbgTargetShapeApp
```

Add `loop` to keep it running for attach scenarios:

```bash
dotnet run --project samples/DbgTargetShapeApp -- loop
```

## Verify with dotdbg

A JSON batch script is included:

```bash
dotdbg --json-input samples/DbgTargetShapeApp/verify.json
```

This loads the target, sets a conditional breakpoint, a tracepoint, and a watch, then runs the program and verifies `backtrace`, `frame`, `list`, `print`, `info locals`, `info args`, `info threads`, `info modules`, `info files`, `watches`, `unwatch`, `trace-log`, and `info exception`.
