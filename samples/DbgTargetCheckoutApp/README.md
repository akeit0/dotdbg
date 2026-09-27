# Checkout target

This console app reads `orders.csv` from its working directory. `--tier vip` applies the rate from `DOTDBG_DISCOUNT` (default `0.10`). The price calculation intentionally subtracts the discount twice, so the app writes mismatches to stderr and exits with code 1. Standard tier uses no discount and should succeed.

From the repository root:

```powershell
Push-Location samples/DbgTargetCheckoutApp
$previousDiscount = $env:DOTDBG_DISCOUNT
try {
    $env:DOTDBG_DISCOUNT = "0.10"
    dotnet run --project . -- --tier vip
} finally {
    if ($null -eq $previousDiscount) {
        Remove-Item Env:DOTDBG_DISCOUNT -ErrorAction SilentlyContinue
    } else {
        $env:DOTDBG_DISCOUNT = $previousDiscount
    }
    Pop-Location
}
```

Expected results: A100 `actual=53.00 expected=59.00`; B200 `actual=32.00 expected=35.00`. The sample has `BREAK_POINT` and `TRACE_POINT` comments to locate useful source lines without relying on fixed line numbers. Follow the [wrong total use case](../../docs/use-cases/wrong-checkout-total.md) for the debugger investigation.
