# Logging & Protocol Monitor Redesign — Success Plan

## Problem (observed in the shipped log sample)

| # | Defect | Evidence |
|---|--------|----------|
| 1 | Same payload hex-dumped **4×** | `TelnetConnection: Read 95 bytes`, `TelnetConnection: Firing DataReceived with 95 bytes`, `TerminalSession: Received 95 bytes`, `TDV2200Emulator: ProcessData called with 95 bytes` — identical hex, four lines |
| 2 | Escape sequences not named | `Parser.OnCsiDispatch fired! Final=0x6D, Params=0` — the reader must decode `0x6D`=SGR by hand |
| 3 | Zero-information lines | `Waiting for data from stream...`, `DataReceived event fired (subscribers: 1)`, `ProcessData: Parser.ProcessBytes completed` |
| 4 | No structure | `LogEntry` is `{Timestamp, Message}`. No level, no category ⇒ the viewer cannot filter |
| 5 | 200-entry cap, memory only | The interesting part scrolls away in ~2 seconds of traffic |
| 6 | Causality reads backwards | CSI dispatch lines are emitted *before* the `ProcessData` line for the same packet |
| 7 | Copy is all-or-nothing | `ItemsControl` has no selection; only "Copy to Clipboard" (everything) |

## Decisions (confirmed with user)

- **Scope**: full — structured logger + escape-sequence decoder + rebuilt Log Viewer + a **dedicated two-pane Protocol Monitor window**.
- **Raw bytes**: inline per decoded sequence, collapsible column; printable runs collapse to a quoted string with a count.
- **File output**: opt-in session file, `%AppData%\RetroTerm\logs\session-<timestamp>.log`, unbounded.
- **Unknown sequences**: logged at **Warn** with raw bytes, so gaps in TDV coverage are obvious.
- **Clipboard**: copy **all** *or* **selected lines**, in both the Log Viewer and the Protocol Monitor.

## Target output format

```
14:54:09.893  RX  1B 5B 30 6D          ESC[0m       SGR    Reset attributes
14:54:09.893  RX  20 20 ... (81)       "          " 81 printable chars
14:54:09.893  RX  1B 5B 37 3B 33 48    ESC[7;3H     CUP    row=7 col=3
14:54:09.801  TX  0D                   CR           CR     Carriage Return
```

## Definition of done

1. `LogEntry` carries `Timestamp`, `Level`, `Category`, `Source`, `Message`, optional raw bytes.
2. `ApplicationLogger` supports `MinimumLevel`, `EnabledCategories`, `IsEnabled(...)` guard (so disabled logging allocates nothing), ring buffer ≥ 5000, and opt-in file output.
3. `EscapeSequenceDecoder` names every CSI/ESC/C0 sequence the emulators implement (ECMA-48 core + TDV `z { } | ~ u v <` + DEC private) and returns `IsKnown=false` for anything else.
4. The 4× duplication is gone: raw bytes are logged **once**, at the network layer, at Trace.
5. The zero-information lines from defect 3 are deleted or demoted to Trace.
6. `ProtocolMonitorWindow` shows a raw byte pane + a decoded sequence list, RX/TX coloured, with filters and both copy modes.
7. `LogViewerWindow` gains category/level filters, text search, and both copy modes.
8. No LINQ, no `foreach`, no FluentAssertions. `dotnet build` clean, `dotnet test` green.

## Phases

1. Core logging infra — `LogLevel`, `LogCategory`, `ApplicationLogger` rewrite, file sink.
2. `EscapeSequenceDecoder` + unit tests.
3. `ProtocolTracer` + rewire `TelnetConnection` / `TerminalSession` / `TerminalEmulatorBase`; delete duplicate dumps.
4. `ProtocolMonitorWindow` + `LogViewerWindow` rebuild (multi-select, copy all / copy selected).
5. Build + full test run.

---

## Result — all phases complete

### Files added

| File | Purpose |
|------|---------|
| `src\RetroTerm.Core\Logging\LogLevel.cs` | Trace / Debug / Info / Warn / Error |
| `src\RetroTerm.Core\Logging\LogCategory.cs` | `[Flags]` subsystem mask (net, telnet, session, parser, emul, kbd, ui, general) |
| `src\RetroTerm.Core\Logging\LogEntry.cs` | Structured record + fixed-column `Format()` |
| `src\RetroTerm.Core\Logging\DecodedSequence.cs` | Mnemonic / Name / Arguments / Rendered / IsKnown |
| `src\RetroTerm.Core\Logging\EscapeSequenceDecoder.cs` | ECMA-48 + DEC private + TDV finals → names |
| `src\RetroTerm.Core\Logging\ProtocolTraceEntry.cs` | One traced item (direction, kind, bytes, decode) |
| `src\RetroTerm.Core\Logging\WireScanner.cs` | Stateful stream splitter, survives split packets |
| `src\RetroTerm.Core\Logging\ProtocolTracer.cs` | Ring buffer + single feed point per direction |
| `src\RetroTerm.Desktop\Views\LogLineRow.cs` | Row view model (top-level for `x:DataType`) |
| `src\RetroTerm.Desktop\Views\ProtocolMonitorWindow.axaml(.cs)` | Two-pane protocol trace |
| `tests\RetroTerm.Tests\Logging\EscapeSequenceDecoderTests.cs` | 29 tests |
| `tests\RetroTerm.Tests\Logging\WireScannerTests.cs` | 13 tests |

### Files rewritten / rewired

- `src\RetroTerm.Core\Logging\ApplicationLogger.cs` — ring buffer, levels, categories, `IsEnabled` guard, file sink, console echo now OFF by default.
- `src\RetroTerm.Core\Session\TerminalSession.cs` — the single RX and TX feed points; ~40 lines of triple-logged `Debug.WriteLine` + `ApplicationLogger.Log` + `Console.WriteLine` collapsed to one line each.
- `src\RetroTerm.Core.Protocols.Net\TelnetConnection.cs` — 2 duplicate hex dumps and 2 zero-information lines removed; remaining raw dump demoted to Trace.
- `src\RetroTerm.Core\Terminal\Emulators\TerminalEmulatorBase.cs` — `ProcessData` hex dump and "ProcessBytes completed" deleted; CSI dispatch line now decoded and demoted to Trace.
- `src\RetroTerm.Desktop\Views\LogViewerWindow.axaml(.cs)` — filters, chronological order, multi-select, Copy All / Copy Selected / Ctrl+C / Ctrl+A, file-log toggle.
- `src\RetroTerm.Desktop\MainWindow.axaml(.cs)` — View ▸ Protocol Monitor.

### Verification

- `dotnet build RetroTerm.sln` → **0 errors**.
- `dotnet test RetroTerm.sln` → **2876 passed, 0 failed, 41 skipped** (the 41 skips are pre-existing).
- 42 of those passing tests are new, covering the decoder and the scanner.

### Behaviour change worth knowing

`ApplicationLogger.EchoToConsole` now defaults to **false**, and `TerminalSession` no longer
writes its own `Console.WriteLine` copies. If the TestServer workflow relied on those lines
appearing on stdout, set `ApplicationLogger.EchoToConsole = true` at startup.
