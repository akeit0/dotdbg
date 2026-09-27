# DbgTargetFileApp

A .NET 10 [file-based app](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) (single `.cs` file, no `.csproj`) for exercising `dotdbg file <path>.cs`.

## Run directly

```bash
dotnet run samples/DbgTargetFileApp/hello.cs
```

Add `loop` to keep it running for attach scenarios:

```bash
dotnet run samples/DbgTargetFileApp/hello.cs -- loop
```

## Debug with dotdbg

`file` builds the `.cs` app and sets the output assembly as the debug target:

```bash
dotdbg file samples/DbgTargetFileApp/hello.cs
dotdbg break samples/DbgTargetFileApp/hello.cs:27
dotdbg run
dotdbg wait
dotdbg backtrace
dotdbg list
dotdbg print n
dotdbg quit
```

Release configuration and MSBuild properties are supported:

```bash
dotdbg file samples/DbgTargetFileApp/hello.cs -c Release
dotdbg file samples/DbgTargetFileApp/hello.cs -c Debug -p:DefineConstants=TRACE
```

Or use the included JSON batch script:

```bash
dotdbg --json-input samples/DbgTargetFileApp/verify.json
```
