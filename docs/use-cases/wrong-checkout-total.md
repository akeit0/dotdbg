# Find a wrong checkout total

The [checkout target](../../samples/DbgTargetCheckoutApp/README.md) reads `orders.csv`. VIP orders should receive one discount on the subtotal, then add shipping. It reports a mismatch on stderr and exits with code 1. The investigation is to find where the extra subtraction occurs.

Run these commands in PowerShell from the repository root. A stable session ID keeps separate CLI calls in the same debugger session. `file` builds the project; `--cwd` lets the target find its input file; `--env` and the arguments after `--` belong to the target.

```powershell
$dbg = (Resolve-Path src/DotDbg/bin/Debug/net10.0/DotDbg.dll).Path
$session = "checkout-investigation"
$line = (Select-String -LiteralPath samples/DbgTargetCheckoutApp/Program.cs -Pattern 'return total;').LineNumber

dotnet $dbg -s $session --json file samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj -c Debug
dotnet $dbg -s $session --json break "samples/DbgTargetCheckoutApp/Program.cs:$line"
dotnet $dbg -s $session --json run --env DOTDBG_DISCOUNT=0.10 --cwd samples/DbgTargetCheckoutApp -- --tier vip
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json context
```

The first stop is at `return total` for order A100. `context` should show `rate=0.1`, `subtotal=60`, `discount=6`, `discounted=54`, and `total=53`. The expected amount is `60 - 6 + 5 = 59`. The [source line](../../samples/DbgTargetCheckoutApp/Program.cs) computes `discounted - discount + shipping`, subtracting the same discount twice. `context` is enough to see the cause; use `print total` or `info locals` if you need a focused value or all locals.

```powershell
dotnet $dbg -s $session --json continue
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json print total
dotnet $dbg -s $session --json continue
dotnet $dbg -s $session --json wait --timeout 10
dotnet $dbg -s $session --json output --channel stderr
dotnet $dbg -s $session --json events --kind stop
dotnet $dbg -s $session --json quit
```

The second order stops with `total=32`, while its expected amount is `30 - 3 + 8 = 35`. The final `wait` reports `reason: "exited"`. `output --channel stderr` shows the two mismatch messages. `events --kind stop` shows both breakpoint stops and the exit, without mixing in the app's messages. When inspecting a real bug, fix the expression to `discounted + order.Shipping`, then repeat the run to confirm both totals.
