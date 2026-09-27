# Agent workflow

For concrete investigations, start with the [use case index](use-cases/README.md).

Inspect each result before choosing a dependent command. Batch steps that are known in advance with `source`; use separate calls when the next step depends on a stop or expression value. Put `--json` before the command name. `-v` and `--verbose` remain aliases.

Use `help all` for the command syntax in one response, `help <command>` for behavior and examples, or `schema` for machine-readable syntax.

## Session lifecycle

The default session key is a hash of the process working directory. If an agent changes directories between calls, it should supply a stable `--session-id` to every call. For an independent investigation in a shared directory, choose a unique `--session-id` and reuse it for every call; otherwise previous breakpoints and watches remain active. The first session command starts a daemon automatically. The daemon keeps the target, breakpoints, watches, selected frame, and trace log state until `quit`. End every investigation with `quit`, even after the target exits. It shuts down the daemon and terminates a live target; use `detach` first if an attached target must keep running.

```shell
dotdbg -s bug-123 --json file samples/DbgTargetSampleApp/DbgTargetSampleApp.csproj -c Debug
dotdbg -s bug-123 --json break samples/DbgTargetSampleApp/Program.cs:33
dotdbg -s bug-123 --json run
dotdbg -s bug-123 --json wait --timeout 30
dotdbg -s bug-123 --json context
dotdbg -s bug-123 --json backtrace
dotdbg -s bug-123 --json info locals
dotdbg -s bug-123 --json print n
dotdbg -s bug-123 --json quit
```

Bare `run` and `continue` return when execution resumes. Call `wait --timeout 30` to receive a stop, trace, or exit result within 30 seconds, or use `run --wait` and `continue --wait` to resume and wait in one command. On timeout, the command returns `success: false` and leaves the session running. Bare `wait` can wait indefinitely. `interrupt` or `quit` can be sent from another process. A tracepoint can return a response with `data.reason: "trace"` and then auto-continue. Inspect `data.reason` before evaluating expressions, since evaluation needs a stopped frame.

`run --wait` and `continue --wait` default to a 30-second wait; `--timeout 10` changes it. `continue --to <breakpoint-id>` waits for that enabled breakpoint or exit and passes other stops, including exception stops. Its response includes `data.targetReached` and `data.skippedStops`; use `events --kind stop` to inspect what it passed. A timeout leaves the target running. Exit responses include `data.exitCode` for launched targets and `data.unhandledException` from the CLR callback. `wait` returns the same terminal result on repeat calls. `info status` also reports these fields and `terminalReason` after exit. An exit code of 0 can accompany an unhandled exception under this engine, so check both fields.

After exit, `continue` returns an error with the exit code and a restart hint. Use `context` for status and suggested next commands, `output` or `events` for the completed run, and `run` to launch again. `wait` can reread the exit result.

## JSON output

A successful stop returns an object with this shape:

```json
{"success":true,"message":"Stopped: breakpoint","data":{"threadId":1234,"reason":"breakpoint","filePath":"Program.cs","line":33,"breakpointId":1,"breakpoints":[{"id":1,"name":"","isTracepoint":false,"file":"Program.cs","line":34,"column":null}],"context":{"frame":{"name":"Program.Main","line":33},"arguments":[],"locals":[]},"recentOutput":[{"seq":12,"channel":"stdout","text":"starting"}],"omittedOutput":0,"outputNextSeq":13}}
```

The compact `context` within a stop or from the `context` command includes up to eight locals. When more exist, it favors locals named on the current source statement and reports the remainder as `omittedLocals`; `print <name>` and `info locals` can inspect others.

Async backtraces can include logical caller frames marked `syntheticAsyncCaller: true`. Select one with `frame <index>`; `context` and `info locals` can show values captured in its state machine. `print` cannot evaluate a C# expression there, so set a breakpoint in the real method when expression evaluation is needed.

The exact `data` fields vary by command. Check `success` before using them. CLI parse and transport failures have this shape:

```json
{"success":false,"phase":"parse","error":{"message":"break: missing location"}}
```

`phase` is `invocation`, `parse`, `batch`, or `transport` for failures before a daemon response. Debugger operation errors return `success: false` with a `message`. The process exit code is nonzero on failure.

CLI responses show paths relative to the caller's working directory when the path is inside it. Paths outside that directory stay absolute. This applies to stops, frames, breakpoints, source listings, status, event locations, and exception stack traces. Target output and evaluated values are left unchanged. Keep the same working directory across calls or use the relative path from the directory of that call. The debugger still stores absolute paths internally.

`dotdbg schema` prints a bare JSON command list without starting a daemon. Direct `help` prints text by default; with `--json`, it returns a response with the text under `data.text`. Run discovery commands separately from a JSON batch when you want their output on its own. The schema describes CLI syntax and aliases; JSON response `data` depends on the command.

## Expressions and shell quoting

`print` evaluates a C# expression in the current stopped frame. `condition`, `watch`, and `trace` register expressions to evaluate when execution reaches a relevant breakpoint or stop. Match the value types in the program: `7.00` is a `double` literal, while `7.00m` is `decimal`. For a decimal local, use an expression such as `dotdbg --json print 'subtotal + 7.00m'`. The evaluator can report a C# type error for an unsupported combination. C# string concatenation adds no separator on its own: `parcel.Id + parcel.BaseShipping` can yield `P3000.00`.

Quote an entire expression containing a C# string literal so the shell passes its double quotes through to dotdbg:

```shell
dotdbg --json condition 1 'parcel.Id == "P200"'
dotdbg --json print 'parcel.Id == "P200"'
dotdbg --json trace Pricing/ShippingPolicy.cs:15 'parcel.Id + ": " + parcel.BaseShipping'
```

Within a single-quoted `source -c` script, write C# string literals normally: `dotdbg --json source -c 'condition 1 parcel.Id == "P200"; continue --wait'`. A standalone `source -c 'print "hello"'` evaluates the string literal when stopped. The script parser preserves C# quotes for `condition`, `break ... if`, `print`, `watch`, and `trace`. Direct CLI arguments use shell quoting; script and JSON command strings contain the C# expression itself. Check the stored expression with `breakpoints` after setting a condition or tracepoint.

If `print` receives a quoted member or comparison such as `"$exception.Code"` or `"attempt < attemptLimit"`, it returns that string literal with a hint. In a `source -c` script, write `print attempt < attemptLimit` without inner quotes to evaluate the comparison.

For a named conditional breakpoint, `--name` may precede or follow `if`: `dotdbg --json source -c 'break Program.cs:20 if parcel.Id == "P200" --name p200'`. In PowerShell, keep the C# double quotes inside the outer single-quoted script without adding backslashes.

`breakpoints` can show `verified: false` before the target starts because the pending breakpoint has not bound to loaded code. Check it again after `run`; a breakpoint that remains unverified may have the wrong file or line. The `line` field remains the requested line; `boundLine` shows the executable line selected by the debugger after binding. For example, a request at line 12 of a multi-line statement may bind to line 11. The text listing shows this as `file:12 -> file:11`. A breakpoint stop also includes `breakpointId` when one user breakpoint matches, and `breakpoints` with the IDs and requested locations of all matches; the stop's top-level `filePath` and `line` remain the actual location. New watches have no last value until the next stop. At a stop, `*` marks a watch value that changed since its previous evaluation (including its first value). `step` can enter a user-defined property getter; `next` steps over the call, while Just My Code filters non-user code. `list file:line` shows five lines by default; use `list file:line --lines 20` for a wider view (up to 100 lines). The window shifts at file boundaries.

A source breakpoint pauses before the current statement executes. A local assigned on that line may still have its default value; use `next` and then `print` to inspect the assigned result.

To target a later hit without repeated `continue --wait` calls, use `ignore <breakpoint-id> <hits-to-skip>` before `run`; for example, `ignore 1 1` stops on the second hit. A breakpoint requested on a continuation line of a multi-line statement may bind to the statement's earlier executable sequence point. Read the actual line in the stop response before stepping or printing a newly assigned local.

For `break --il`, `IL_001A` and `0x1a` both mean hexadecimal offset 26; a bare `26` is decimal. Source breakpoints and tracepoints can share a location when all are unconditional. Co-located source breakpoints with conditions or ignore counts are rejected because a shared engine breakpoint cannot apply their rules independently.

### PowerShell on Windows

PowerShell does not use `\"` to escape a double quote. A direct call such as `condition 2 parcel.Id == "P200"` without quotes around the whole expression passes `parcel.Id == P200` to dotdbg. Single-quote the whole expression as shown above. For expressions without an embedded C# string literal, `"n > 10"` works. Windows `cmd.exe` has different quoting rules. Run `dotdbg help powershell` for the CLI examples.

## App output and events

The app's stdout and stderr are captured when dotdbg launches it. `Debug.WriteLine` messages use the separate `debug` channel. Stop responses include the last four output entries since execution resumed, clipped to 160 characters each, plus `omittedOutput` and `outputNextSeq`. Each command still produces one JSON response. `events` includes stdout, stderr, debug output, stops, evaluated tracepoint results, and debugger diagnostics; one `events` query can retrieve all of them. Breakpoint stop events retain the same breakpoint IDs and requested locations as live stop responses. `output` narrows that feed to target output and can filter by channel. A tracepoint that resumes execution appears as `kind: "trace"`, with no false stop entry. An invalid breakpoint condition appears as `kind: "diagnostic"` and in the next stop or exit response's `recentDiagnostics`, so check that field when an expected breakpoint never hits. Debugger diagnostics are separate from target `Debug.WriteLine` messages.

```shell
dotdbg -s bug-123 --json output --limit 20
dotdbg -s bug-123 --json output --since 42 --channel stderr
dotdbg -s bug-123 --json events --since 42
dotdbg -s bug-123 --json events --kind trace
dotdbg -s bug-123 --json events --kind diagnostic
```

Without `--since`, these commands return the most recent events, including after the target exits; reading stop `recentOutput` does not drain them. Use `output --since 0` for retained history. `data.nextSeq` is a cursor for a later `--since` call, which excludes entries at or before that sequence. When `data.hasMore` is true, repeat with `nextSeq` to page through retained events. `data.truncated` means the requested cursor predates the in-memory buffer. The buffer holds 1,024 events and each event text is capped at 2,048 characters. Stop and trace events include their source location and thread ID when available. Commands and their arguments are not retained. For attached processes, stdout and stderr remain in their original console; debugger messages can still be captured.

## Batch input

Use a batch when the steps are known in advance. `source --last -c` runs a semicolon-separated script in one daemon request and returns the final command's normal JSON response with `batch.executed`: a final `print` has `data.value`, while a final `continue --wait` has `data.reason`. If later commands replace a stop response, `batch.lastStop` retains its command index, location, and compact context, including when a later command fails. `batch.prints[].index` is the 1-based position among all script commands, including non-print commands. A script stops at the first failure and reports the failed command. End setup scripts with `run --wait` to receive the first stop directly, then use separate calls so the next action can depend on the stop reason or expression value. Do not add `context` after `run --wait` unless you need more variables or source; the stop already carries compact context.

Scripts execute commands in order; a later failure does not undo earlier commands. On failure, `--last` returns `data.completed`, `data.failedIndex`, and up to 20 recent responses within 512 KiB. `data.omittedResponses` counts older responses, and a large response may have `dataOmitted: true`. Use `source --tail N` to keep the last 1–20 responses, including intermediate stop context; earlier responses are counted in `data.omittedResponses`. Both bounded modes retain up to 50 print values in `batch.prints`, even when their individual responses are omitted. Omit both options to keep every response. `next`, `step`, and `finish` always wait for their next stop; `--wait` is accepted but optional.

`help` and `schema` also run inside `source` or a JSON batch. Their results appear in `data.responses` (`help` text under `data.text`, schema under `data.schema`); use the default output mode to read them alongside later command results. Direct `help` is text unless `--json` is set; direct `schema` remains bare JSON.

```shell
dotdbg -s bug-123 --json source --last -c 'file samples/DbgTargetCheckoutApp/DbgTargetCheckoutApp.csproj -c Debug; break samples/DbgTargetCheckoutApp/Program.cs:41; run --cwd samples/DbgTargetCheckoutApp --wait'
```

For compact inspections, `dotdbg --json source --last -c 'print merchandise; print shipping; next; print rebate'` returns the final rebate directly and all three print values in `batch.prints` when `next` reaches the expected frame. An exception can interrupt a step and select the throw frame, making the caller's locals unavailable; inspect `data.reason` before batching a dependent print. Older prints are counted in `batch.omittedPrints`; long values are clipped in this summary and marked `truncated`. Use `source --tail 2 -c 'next; backtrace'` to retain both full responses, including the stop context after `next`; a smaller tail drops earlier stop responses. `events` records target output and debugger events, not `print` command responses. For a reusable file-based batch, `--json-input` keeps one CLI process and prints one JSON line per command; each command still makes a daemon request:

```json
{
  "stopOnError": true,
  "commands": [
    "file samples/DbgTargetSampleApp/DbgTargetSampleApp.csproj -c Debug",
    "break samples/DbgTargetSampleApp/Program.cs:33",
    "run",
    "wait --timeout 30",
    "backtrace",
    "quit"
  ]
}
```

Run with `dotdbg -s bug-123 --json-input commands.json`. The output is newline-delimited JSON: one response for each executed command, or a `phase: "batch"` error for invalid batch input. Execution stops at the first failure unless `stopOnError` is `false`. The batch exit code is nonzero if any command fails.

Batch entries may also be raw operation objects with an `op` field. Use command strings unless you need fields that the CLI parser does not expose; raw operation fields are internal and may change. Without `--last`, `source` returns one response containing the individual responses in `data.responses`.

## Choosing commands

- Prefer `file <project.csproj> -c Debug` or `file <app.cs> -c Debug` when the binary has not been built. `file` builds these targets. Use a DLL or EXE path to skip a build.
- Use `run --env NAME=VALUE --cwd PATH -- arg` to set the target environment and working directory. The `--` separator passes option-like arguments to the target.
- The default `catch user` stops at user-code throws and unhandled exceptions while skipping framework async rethrows. Use `catch all` for every throw, `catch unhandled` to stop only before an unhandled exception terminates the app, or `catch none` to let exceptions run. Use `info exception` and `print '$exception.Member'` at an exception stop; bare `catch` reports the current mode.
- Use `context` for a small first look at session status, the selected frame, nearby source, arguments, and locals. It caps variables at eight per group and clips long values. Use `backtrace`, `info locals`, `info args`, and `list` when more detail is needed.
- Use `breakpoints` for numeric breakpoint IDs before `delete`, `condition`, or `ignore`. Use `watches` for watch IDs before `unwatch`.
- Use `break --il` and `decompile --il` when source is unavailable. `--no-jmc` on `run` or `attach` permits stepping into framework code.
- Use `quit` when done, so the daemon and any launched target do not keep running.
