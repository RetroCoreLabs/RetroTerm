# TDV 2200 Printer / Auxiliary-Port Support - Design

**Status:** design proposal (no code written yet)
**Author context:** enables RetroTerm to act as the printer-capable Tandberg TDV
2200 terminal that SINTRAN III drives when a printer is "connected to a Tandberg
terminal" (the auxiliary printer port). See the companion analysis in NDInsight:
`SINTRAN\Print\09-PRINTING-VIA-TERMINAL-AND-RETROTERM.md`.

All file paths are relative to the repository root. Coding rules
observed: no LINQ, no `foreach` (use `for`), no FluentAssertions, `Span` /
`ArrayPool` on hot paths, netstandard2.1 constraints, comments kept.

---

## 1. What we are implementing (verified protocol)

The host (ND-100 / SINTRAN, or any VT100-class host) drives a terminal-attached
printer through **ECMA-48 Media Copy (MC)** plus a few local behaviours. Every
byte sequence below was extracted from the reverse-engineered TDV ROM spec under
`spec\TDV2200\` and cross-checked against `spec\ECMA\ECMA-48_5th_edition_june_1991.md`.

| Feature | Sequence / trigger | Hex | Semantics | Spec source |
|---------|--------------------|-----|-----------|-------------|
| Printer controller ON (transparent pass-through) | `CSI 5 i` | `1B 5B 35 69` | Received chars go to the printer **only**, not the screen, until OFF. | `spec\TDV2200\Testing2215\tdv2215_escape_sequences.md:165,170` + ECMA-48:3301-3321 |
| Printer controller OFF | `CSI 4 i` | `1B 5B 34 69` | Stop relay; resume normal screen handling. | same |
| Print screen (one-shot) | `CSI 0 i` or `CSI i` | `1B 5B 30 69` / `1B 5B 69` | Copy current screen to printer once. | `tdv2215_escape_sequences.md:165,168`; ECMA-48:3312 |
| Auto-print / line-log ON | `CSI ? 5 i` | `1B 5B 3F 35 69` | Chars shown on screen **and** each completed line printed. | VT100/ECMA-48 private MC; matches "Printer Mode = Log" firmware |
| Auto-print OFF | `CSI ? 4 i` | `1B 5B 3F 34 69` | Stop line logging. | same |
| Print cursor line | `CSI ? 1 i` | `1B 5B 3F 31 69` | Print the current line once. | VT100 private MC |
| Local screen dump | key **CTRL+START PRINT (G52)** | n/a (local) | User dumps screen locally, no host involvement. | `spec\TDV2200\OCR\TDV-2200_9-User-s_Guide-ND_combined.md:808-809` |
| Abort printout | key **CTRL+STOP PRINT (G51)** | n/a (local) | Stop an in-progress local printout. | same `:805-806` |
| Set printer mode remotely | `DCS 8 q` | - | Host sets the Printer-Mode switch; payload not enumerated in spec. | `docs\TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md:231` |

**Historical accuracy note (must be preserved in comments):** MC (`CSI ... i`)
is present in the **TDV 2215 / VT100-compatible** reconstruction and in ECMA-48,
but is **absent from the native 2200/9S ND escape table** - on a real 2200/9 the
printer was driven by the local CTRL+PRINT key and the "Printer Mode = Log"
NVRAM switch, not by a host escape. RetroTerm targets interoperability, so we
implement MC for all TDV models but gate it behind an explicit
`PrinterMode`/`PrinterEnabled` config (default OFF), and document the deviation.

**Flow control (verified):** the printer link is **XON/XOFF only**; there is no
hardware busy line and **the terminal never reports printer status back to the
host** (`spec\TDV2200\OCR\TDV-392894-0-EN_combined.md:1516-1526`). So our sink
must never need to signal the host; it simply consumes bytes. We do NOT invent a
host-facing printer-status reply.

**NVRAM printer parameters (for the config model):** Printer Mode
(Local/Rem, Remote, Log), Printer Form Feed (None/Before/After/Both), Printer
Handshake (Off / XON-XOFF), Printer Code Format (7even/7odd/8none/8even/8odd),
Printer Speed. Source `spec\TDV2200\Testing2215\tdv2115_control_sequences.md:84-105`.

---

## 2. Runtime state model

A single `PrinterController` (in `RetroTerm.Core`) owns all printer state. The
emulator holds one instance and tees characters into it.

```
enum PrinterMode        // mirrors the NVRAM "Printer Mode" switch
{
    Off,                // printer disabled; ignore all MC + local keys
    LocalAndRemote,     // honour MC commands AND local CTRL+PRINT (NVRAM "Local/Rem.")
    Remote,             // honour MC commands only (no local key dump)
    Log                 // force line-by-line auto-print always on (NVRAM "Log")
}

enum PrinterCodeFormat  // NVRAM "Printer Code Format"; parity is a serial concept,
{                       // so for a file/host sink we only apply the 7-vs-8-bit mask.
    SevenBit,           // mask 0x7F   (7even / 7odd)
    EightBit            // pass low byte (8none / 8even / 8odd)
}

enum PrinterFormFeed { None, Before, After, Both }
```

Two transient runtime flags, set/cleared by MC sequences (independent of the
static `PrinterMode`):

- `ControllerActive` - set by `CSI 5 i`, cleared by `CSI 4 i`. While true,
  received printable chars and CR/LF/FF are routed to the printer and **suppressed
  on screen** (transparent controller mode).
- `AutoPrintActive` - set by `CSI ? 5 i`, cleared by `CSI ? 4 i`; also forced
  true whenever `Mode == Log`. While true, characters render on screen normally
  **and** each completed line is also copied to the printer.

Effective gating:

| Mode | MC honoured? | ControllerActive allowed | AutoPrint | Local CTRL+PRINT |
|------|--------------|--------------------------|-----------|------------------|
| Off | no | no | no | no |
| LocalAndRemote | yes | yes | via `CSI ? 5 i` | yes |
| Remote | yes | yes | via `CSI ? 5 i` | no |
| Log | yes | yes | always on | no |

`bool Active => Mode != Off && (ControllerActive || AutoPrintActive);`

---

## 3. New components

### 3.1 `IPrinterSink` - the output target (dumb byte consumer)

`src\RetroTerm.Core\Terminal\Printing\IPrinterSink.cs` **[NEW]**

Modeled on `Session\ISessionDataLogger.cs`. The sink knows nothing about modes;
it just receives already-translated printer bytes.

```csharp
public interface IPrinterSink : IDisposable
{
    bool IsOpen { get; }
    // Write pre-translated printer bytes (code-format + FF already applied).
    void Write(ReadOnlySpan<byte> data);
    // Called at natural job boundaries (print-screen done, controller OFF) so a
    // file sink can flush; a spooling sink could close the job. Never blocks the host.
    void Flush();
}
```

Implementations **[NEW]** in `...\Terminal\Printing\`:

- `FilePrinterSink` - append bytes to a file (or a rolling file per job).
  Modeled exactly on `Session\FileSessionDataLogger.cs`: `lock` for thread
  safety, `ArrayPool<byte>` staging, and the netstandard2.1 constraint that
  `FileStream.Write` has **no `Span` overload** (copy pooled `byte[]` then
  `Write(buffer, 0, count)`). Ctor `FilePrinterSink(string path, bool newFilePerJob)`.
- `NullPrinterSink` - no-op singleton (default when no printer configured).
- `CapturePrinterSink` (test only, in the test project) - collects bytes into a
  `List<byte>` / exposes `string AsText()` for assertions.

Design choice: the sink is intentionally decoupled from `IConnection`. A future
"print back to the host over a second channel" or "spool to the OS printer" is
just another `IPrinterSink`.

### 3.2 `PrinterController` - the brain (mode logic, translation, buffering)

`src\RetroTerm.Core\Terminal\Printing\PrinterController.cs` **[NEW]**

Owned by the emulator. Holds the state from section 2, the `IPrinterSink`, a
pooled line buffer, and does code-format translation + form-feed injection.

```csharp
public sealed class PrinterController
{
    public PrinterMode Mode { get; set; } = PrinterMode.Off;
    public PrinterCodeFormat CodeFormat { get; set; } = PrinterCodeFormat.SevenBit;
    public PrinterFormFeed FormFeed { get; set; } = PrinterFormFeed.None;

    public IPrinterSink Sink { get; set; } = NullPrinterSink.Instance;

    public bool ControllerActive { get; private set; }
    public bool AutoPrintActive  { get; private set; }
    public bool Active => Mode != PrinterMode.Off && (ControllerActive || AutoPrintActive);

    // ---- MC command entry points (called by the CSI dispatcher) ----
    public void StartController();          // CSI 5 i   (no-op if Mode==Off)
    public void StopController();           // CSI 4 i
    public void SetAutoPrint(bool on);      // CSI ? 5 i / ? 4 i (ignored if Mode==Log: stays on)
    public void PrintScreen(ITerminalBufferReader buffer, int width, int height); // CSI 0 i / CSI i
    public void PrintLine(ITerminalBufferReader buffer, int row, int width);      // CSI ? 1 i

    // ---- character tee (called from HandleCharacter / HandleExecute) ----
    // Returns true if the char was consumed for the printer and must NOT be
    // written to the screen (transparent controller mode). Returns false when
    // the char should render normally (auto-print tees a copy but returns false).
    public bool OnDisplayChar(uint codepoint);
    public bool OnControl(byte control);    // CR/LF/FF while active

    // ---- lifecycle ----
    public void OnModeChanged();            // recompute AutoPrintActive for Log
    public void Reset();                    // on terminal reset (RIS): clear flags, flush
}
```

Key internal behaviour:

- **Controller mode (transparent):** `OnDisplayChar`/`OnControl` translate the
  byte (code format) and `Sink.Write` it, then return `true` so the caller skips
  the screen. On `StopController` -> optional trailing FF -> `Sink.Flush()`.
- **Auto-print / Log:** `OnDisplayChar` appends the translated byte to a pooled
  line buffer and returns `false` (screen still renders). `OnControl(LF)` flushes
  the line buffer (plus CR/LF and optional FF policy) to the sink. This matches
  the firmware "LOG (line-by-line data transfer)" note
  (`spec\TDV2200\Export2215Code\TDV2215_Memory.txt:1163`).
- **Code-format translation:** `SevenBit` -> `b & 0x7F`; `EightBit` -> `(byte)b`.
  Parity (even/odd) is a serial-wire property with no meaning for a file/host
  sink; we deliberately do **not** synthesize parity bits and say so in a comment.
- **Form feed:** `Before`/`After`/`Both` inject `0x0C` around a print-screen job
  and (optionally) around controller sessions.
- **Line buffer:** rented from `ArrayPool<byte>.Shared`, grown by re-rent + copy,
  returned on `Reset`. No per-char allocation.

`ITerminalBufferReader` **[NEW, tiny]** - a read-only view (`int Width`,
`int Height`, `uint CodepointAt(int row, int col)`) so `PrinterController` can
render a screen dump without depending on the concrete `TerminalBuffer`.
`TerminalBuffer` (`Terminal\Buffer\TerminalBuffer.cs`) implements it via its
existing `GetCell`/indexer and `TerminalCell.Codepoint`. Screen-dump loops use
`for` (no LINQ), trimming trailing spaces per row, emitting CR/LF between rows.

### 3.3 Emulator changes

`src\RetroTerm.Core\Terminal\Emulators\TerminalEmulatorBase.cs`

- Add `public PrinterController Printer { get; } = new PrinterController();`
  (single shared choke point; VT100 and TDV all inherit it).
- In `HandleCharacter(uint codepoint)` (line 215), **before** writing the cell:
  ```csharp
  // Tee to the terminal-attached printer. In transparent controller mode the
  // char is consumed by the printer and must not reach the screen buffer.
  if (Printer.Active && Printer.OnDisplayChar(codepoint))
      return;
  ```
  The TDV override (`TDV2200Emulator.cs:350`) calls `base.HandleCharacter`, so
  this covers **both** the live `ProcessData` path and the `ProcessInput` path
  (the two-path caveat below).
- In `HandleExecute(byte control)` (line 305), for CR/LF/FF: mirror to the
  printer, and in controller mode suppress the screen action:
  ```csharp
  if (Printer.Active && Printer.OnControl(control))
      return; // consumed transparently (controller mode)
  ```
- In `Reset()`/RIS handling: call `Printer.Reset()`.

`src\RetroTerm.Core\Terminal\Emulators\TDV\TDV2200Emulator.cs`

- In `HandleTDV2200Sequence(char final, ...)` (line 691) add:
  ```csharp
  case 'i':                       // ECMA-48 Media Copy (printer control)
      HandleMediaCopy(parser.PrivateMarker, parameters);
      return true;
  ```
- New `private void HandleMediaCopy(byte privateMarker, ReadOnlySpan<int> ps)`:
  ```csharp
  int p = ps.Length > 0 ? ps[0] : 0;
  bool priv = privateMarker == (byte)'?';
  if (!priv)
      switch (p) {
          case 0: Printer.PrintScreen(Buffer, Width, Height); break; // CSI 0 i / CSI i
          case 4: Printer.StopController();  break;                  // CSI 4 i
          case 5: Printer.StartController(); break;                  // CSI 5 i
      }
  else
      switch (p) {
          case 1: Printer.PrintLine(Buffer, Cursor.Row, Width); break; // CSI ? 1 i
          case 4: Printer.SetAutoPrint(false); break;                  // CSI ? 4 i
          case 5: Printer.SetAutoPrint(true);  break;                  // CSI ? 5 i
      }
  ```
  Note: `CSI ? 4/5 i` is dispatched here as a private-marker MC (final byte `i`),
  which is cleaner than routing through `HandleDecPrivateMode` (that path keys on
  `h`/`l`, not `i`). We keep all MC in one method.

Optional (faithful): connect the existing **G52 "PRINT"** key
(`TDV2200KeyRegistry.cs:510,725-735`) so that, in `Mode == LocalAndRemote`, it
triggers `Printer.PrintScreen(...)` locally instead of sending bytes to the host;
in `Remote`/`Log` it emits the MC `CSI i` to the host (matching
`tdv2215_escape_sequences.md:251` "PRINT key ... may send MC sequence").

### 3.4 Configuration

`src\RetroTerm.Core\Configuration\TDVConfiguration.cs`

Add printer fields with defaults in `SetDefaults()`/`Reset()`:
```csharp
public bool PrinterEnabled { get; set; }               // false
public PrinterMode PrinterMode { get; set; }           // Off
public PrinterCodeFormat PrinterCodeFormat { get; set; } // SevenBit
public PrinterFormFeed PrinterFormFeed { get; set; }   // None
public string? PrinterTargetPath { get; set; }         // null
```

**Wiring caveat (verified):** `TDVConfiguration` is currently a standalone
options object and is **not referenced by `TDV2200Emulator`** state. So adding
fields here is not sufficient - we must push them into the emulator's
`PrinterController`. Do this in one explicit place:

`src\RetroTerm.Core\Configuration\EmulatorFactory.cs` `CreateEmulator(HostConfiguration)`
(line 27): after constructing the emulator, if printing is enabled, set
`emulator.Printer.Mode/CodeFormat/FormFeed` and
`emulator.Printer.Sink = new FilePrinterSink(path, ...)`. Persist per-host
settings on `HostConfiguration` (`Configuration\HostConfiguration.cs`) - either
explicit `Printer*` properties or via the existing
`Dictionary<string,object> EmulatorSettings` (line 72), serialized by
`ConfigurationManager`.

### 3.5 Session wiring (sink ownership + lifecycle)

`src\RetroTerm.Core\Session\TerminalSession.cs`

The emulator owns the `PrinterController`, but the **`IPrinterSink` lifetime**
(open file on connect, flush/close on disconnect) belongs with the session,
mirroring how `TerminalSession` already owns `ISessionDataLogger`. On session
start, if the host config enables printing, create the `FilePrinterSink` and
assign it to `Emulator.Printer.Sink`; on stop, `Flush()` + `Dispose()` and reset
to `NullPrinterSink.Instance`. No new per-char event is needed because the tee
lives inside the emulator - this avoids the per-character event overhead and
sidesteps the `ProcessData` vs `ProcessInput` divergence entirely.

---

## 4. Control-flow per feature

1. **Aux pass-through print (`CSI 5 i` ... data ... `CSI 4 i`):**
   parser dispatches `case 'i'` -> `Printer.StartController()`; subsequent bytes
   reach `HandleCharacter`/`HandleExecute`, `Printer.Active` is true,
   `OnDisplayChar`/`OnControl` translate + `Sink.Write` and return `true` so the
   screen is untouched; `CSI 4 i` -> `StopController()` -> optional FF + `Flush`.
2. **Print screen (`CSI 0 i`):** `Printer.PrintScreen(Buffer, W, H)` iterates the
   buffer (`for` loops), trims trailing blanks, writes CR/LF per row with FF
   policy, `Flush`.
3. **Auto-print / Log (`CSI ? 5 i` or `Mode == Log`):** normal rendering
   continues; each printable char is copied into the line buffer; `HandleExecute(LF)`
   flushes the line to the sink. `CSI ? 4 i` stops it (unless `Mode == Log`).
4. **Local CTRL+PRINT (G52):** desktop key handler calls
   `Emulator.Printer.PrintScreen(...)` when `Mode == LocalAndRemote`.

---

## 5. Edge cases and deviations (documented honestly)

- **Transparent controller mode vs escape parsing:** a fully faithful printer
  controller mode passes even ESC sequences to the printer except the `CSI 4 i`
  exit. We take the pragmatic path: the parser keeps interpreting escape
  sequences (so `CSI 4 i` always exits), while printable + CR/LF/FF are routed to
  the printer. This deviation is noted in code comments; it is safe for the
  SINTRAN spooling use case (raw text streams).
- **Codepoint == received byte:** because TDV is a byte protocol (not UTF-8),
  the codepoint reaching `HandleCharacter` equals the received byte for the
  printable/national range, so teeing there yields faithful printer bytes.
  Comment this assumption; if a UTF-8 path is ever added, tee raw bytes instead.
- **No host-facing status:** the sink never blocks and never emits to
  `DataToSend`; XON/XOFF from the printer is a terminal-local concern we simply
  do not model (there is no real serial printer downstream).
- **Parity not synthesized:** code-format parity bits are meaningless to a file
  sink; we only apply the 7-bit mask and say so.
- **Two data paths:** live connection uses base `ProcessData` -> parser directly
  (bypassing `TDVInputProcessor`); tests use `ProcessInput`. Teeing in
  `HandleCharacter`/`HandleExecute` (shared by both) is the reason the design
  works on the live path - do NOT tee in `TDVInputProcessor`.

---

## 6. Test plan

Project `tests\RetroTerm.Tests\TDV\`, xUnit, `Assert.*`, pattern from
`TDV2200EmulatorTests.cs` (`new TDV2200Emulator(80,24,10000)`,
`StringToBytes`, assert public state). New file
`TDV2200PrinterTests.cs` **[NEW]**:

1. `CSI 5 i` sets `Printer.ControllerActive`; `CSI 4 i` clears it.
2. In controller mode, text bytes are captured by a `CapturePrinterSink` and are
   **absent** from the screen buffer (assert both).
3. `CSI 0 i` with a known screen dumps the expected trimmed text to the sink.
4. `CSI ? 5 i` (auto-print): a line renders on screen AND appears in the sink
   only after the LF; `CSI ? 4 i` stops it.
5. `Mode = Log` forces auto-print with no MC.
6. Code-format `SevenBit` masks a 0xE5 byte to 0x65; FormFeed policy injects 0x0C.
7. **Live-path guard:** drive the same `CSI 5 i` + data via
   `Emulator.ProcessData(...)` (and once through `TerminalSession` +
   `InMemoryConnection`, `tests\RetroTerm.Tests\TDV\InMemoryConnection.cs`) to
   prove the tee works on the real connection path, not just `ProcessInput`.
8. `Mode = Off` ignores all MC (no sink writes, screen unaffected).

---

## 7. File change summary

| File | Change |
|------|--------|
| `src\RetroTerm.Core\Terminal\Printing\IPrinterSink.cs` | **new** interface |
| `...\Printing\FilePrinterSink.cs` | **new** (model on `FileSessionDataLogger`) |
| `...\Printing\NullPrinterSink.cs` | **new** no-op |
| `...\Printing\PrinterController.cs` | **new** brain (modes, translation, buffer, screen dump) |
| `...\Printing\ITerminalBufferReader.cs` | **new** tiny read view |
| `...\Terminal\Buffer\TerminalBuffer.cs` | implement `ITerminalBufferReader` |
| `...\Emulators\TerminalEmulatorBase.cs` | add `Printer`; tee in `HandleCharacter`, `HandleExecute`; `Reset` |
| `...\Emulators\TDV\TDV2200Emulator.cs` | `case 'i'` -> `HandleMediaCopy` |
| `...\Configuration\TDVConfiguration.cs` | printer fields + enums |
| `...\Configuration\HostConfiguration.cs` | persisted printer settings |
| `...\Configuration\EmulatorFactory.cs` | build sink, push config into `Printer` |
| `...\Session\TerminalSession.cs` | own sink lifetime (open/flush/dispose) |
| `...\Desktop\...` (optional) | G52 PRINT key -> local screen dump |
| `tests\RetroTerm.Tests\TDV\TDV2200PrinterTests.cs` | **new** tests + `CapturePrinterSink` |

---

## 8. Suggested phasing

1. **Core plumbing:** `IPrinterSink`/`NullPrinterSink`/`PrinterController` (modes
   + tee + translation) + emulator tee + `case 'i'` dispatch + tests 1-6,8. No
   file I/O yet (use `CapturePrinterSink`). This alone makes SINTRAN's `CSI i`
   pass-through work end to end in tests.
2. **File sink + config:** `FilePrinterSink`, `TDVConfiguration`/`HostConfiguration`
   fields, `EmulatorFactory`/`TerminalSession` wiring + live-path test 7.
3. **Polish:** local CTRL+PRINT (G52), form-feed policy, `DCS 8 q` remote
   mode-set (once its payload is captured from a real session - not yet in spec).
