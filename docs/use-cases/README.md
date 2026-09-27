# Debugging use cases

Each guide starts from the repository root and uses a target that ships with the repository. The targets are deliberately small so the debugger result has an unambiguous interpretation. Run `dotnet build DotDbg.slnx` first, or let `file <project.csproj> -c Debug` build a target on demand.

| Investigation | Target | What it exercises |
| --- | --- | --- |
| [Find a wrong checkout total](wrong-checkout-total.md) | [CheckoutApp](../../samples/DbgTargetCheckoutApp/README.md) | Input file, target working directory, environment variable, target argument, breakpoint, compact frame context, console diagnostics |
| [Follow a running app's messages](follow-ticker-output.md) | [TickerApp](../../samples/DbgTargetTickerApp/README.md) | stdout, stderr, `Debug.WriteLine`, event cursors, paging, exit event |
| [Inspect a thrown exception](inspect-exception.md) | [ExceptionApp](../../samples/DbgTargetExceptionApp/README.md) | `catch`, first exception stop, `info exception`, `run --wait`, exit status |
| [Attach to a running process](attach-to-ticker.md) | [TickerApp](../../samples/DbgTargetTickerApp/README.md) | PID attach, source breakpoint, detach without terminating the target |

Other focused targets remain available: [AgentApp](../../samples/DbgTargetAgentApp/README.md) covers arguments and environment variables, [FileApp](../../samples/DbgTargetFileApp/README.md) covers a file-based app and tracepoints, and [ShapeApp](../../samples/DbgTargetShapeApp/README.md) covers object expressions.

Run `dotnet test DotDbg.slnx` from the repository root to execute the target workloads on Windows or Linux. See [Agent workflow](../agent-workflow.md) for the general command and JSON conventions.
