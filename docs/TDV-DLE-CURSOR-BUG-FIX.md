# TDV DLE Cursor Addressing — Bug Fix Report

Responds to `RETROTERM-TDV2200-DLE-CURSOR-BUG.md` (external, confirmed from live RX capture
of SINTRAN terminal type 53).

## Verdict: bug confirmed, but the root cause was deeper than reported

The report assumed "the parser has no DLE state — add one". In fact **a complete, correct DLE
decoder already existed** (`TDV2115CompatibilityHandler.HandleDLEByte`). It was unreachable for
two separate reasons, stacked:

### Cause 1 — `ProcessData` never reached the TDV input pipeline (the big one)

`TerminalSession` calls `Emulator.ProcessData`. Nothing overrode it, so
`TerminalEmulatorBase.ProcessData` fed `Parser.ProcessBytes` **directly**.
`TerminalEmulatorBase.ProcessInput` — and with it the whole `TDVInputProcessor` chain
(ISO 646 variant selection, single shifts, DLE, 2115 control codes) — was only invoked from the
DCS feature and a few internal helpers.

**The entire TDV-specific input path was dead code on the live network path.** DLE was one
visible symptom; every other TDV-native C0 behaviour routed through that processor was equally
inert.

Fix: `TDVEmulatorBase.ProcessData` now routes to `ProcessInput`.

### Cause 2 — DLE was gated behind 2115 compatibility mode

`ProcessTDV2115ControlCharacter` returns early when `!_is2115CompatibilityMode`, so
`case 0x10:` (which starts DLE mode) was never reached. Terminal type 53 runs in native mode
with 2115 mode OFF.

Fix: new ungated `TDV2115CompatibilityHandler.BeginDLE()`, called from `TDVInputProcessor`.
Justified by `docs/TDV-COMPREHENSIVE-REFERENCE.md`, whose C0 table — headed **"Standard ISO 6429
C0 Codes – All Models"** — lists `0x10 DLE Direct Line Entry (cursor load)`. DLE is native TDV,
not a 2115-only feature.

## Encoding: no arithmetic change was needed

The report gives `row = row_byte - 0x7F`, `col = col_byte - 0x7F` (1-based). The existing code
masks instead (`b & 0b11111` row, `b & 0b1111111` col, 0-based). **These agree across the entire
valid range**, and masking additionally tolerates the unbiased encoding asserted by the
pre-existing 2115 test (`DLE 0x05 0x0A` = row 5, col 10). Both encodings now decode correctly
with one code path.

| Raw        | Report says   | Masked result (0-based) | Agrees |
|------------|---------------|-------------------------|--------|
| `10 80 80` | row 1, col 1  | row 0, col 0            | yes    |
| `10 83 82` | row 4, col 3  | row 3, col 2            | yes    |
| `10 98 CF` | row 25, col 80| row 24, col 79          | yes    |

**Doc defect found:** `docs/TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md` states a *5-bit* mask for
the COLUMN. That cannot express the documented column range 0–79. The 7-bit column mask in code
is correct and was kept; the discrepancy is recorded in the method's XML docs.

## Regression exposed and fixed: SS2 double-mapping

Activating the dead path immediately broke `Screenshot_SS2_GraphicsI`. `TDVInputProcessor`
intercepted SS2/SS3 and wrote the **already-mapped** Unicode character via
`ProcessCharacterWithFont`. The TDV2200 bitmap renderer selects a glyph from the **raw**
character plus the cell's `FontNumber`, so this produced a cell with the correct `FontNumber`
but no drawable glyph — a blank.

The emulator's parser path already handles `ESC N` / `ESC O` correctly, `FontNumber` included.
The interception was redundant *and* wrong, and has been removed along with the now-unused
`ProcessCharacterWithFont`.

## Protocol Monitor

`WireScanner` gained a `DleCoordinates` state: `0x10` now consumes the next two bytes and is
reported as one item — `DLE 83 82   DLE   TDV Cursor Address - row=4 col=3` — with 1-based
coordinates so it lines up directly against the `ESC[4;3H` the same program emits in ANSI mode.
Previously the monitor showed `UNKNOWN control 0x10` followed by two bogus TEXT bytes, which is
exactly what obscured the underlying defect.

## Secondary codes — what the TDV documentation actually says

The report guessed at these. The in-repo docs contradict it in two places.

| Bytes | Report's guess | **Documentation says** | Status |
|-------|----------------|------------------------|--------|
| `05` ENQ | "identify request — may need answerback" | **LED 1 ON** (`TDV-COMPREHENSIVE-REFERENCE.md`, `TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md`, `TDV-COMPLETE-KEY-REFERENCE.md`) — listed under "All Models" | NOT changed — see below |
| `16` SYN | "TDV control — swallow" | **Clear Lamps / all LEDs off**, "All Models" | NOT changed |
| `1B 51` ESC Q | "swallow if unmodelled" | **EC switch** — "turns EC switch ON, enables extended operation"; the one ESC sequence honoured even in 2115 mode | NOT changed |
| `ESC[30;7;80l` / `ESC[62;62h` | "mode set/reset" | **Undocumented.** `TDV2200 TERMCAP REFERENCE.md` marks these private mode numbers "Function: Unknown", "No evidence currently available", and asks for the TDV2200 Programmer Reference Manual | NOT changed — no guessing |

These are the termcap init string: `docs/TermCap.txt:130` is literally
`is2=\EQ\E[30;7;80l\E[62;62h\E[0m`.

Nothing here was implemented, because ENQ/SYN/ESC Q all have **documented, non-trivial**
behaviour that the report's "swallow it" advice would have silently discarded, and the mode
numbers have no evidence at all.

## Follow-up: indicator lamps wired to the virtual keyboard

The virtual keyboard already had a full lamp panel (`L1 EXP`, `L2 APP`, `L3 BSY`, `L4 MSG`,
`LINE`, `CAR`, `WAIT`, `ERROR`, `ON`) with a `SetLEDState` API — **nothing drove it**. Now wired:

| Lamp | Driven by |
|------|-----------|
| L1 / L2 / L3 | Host C0 codes ENQ / ACK / NAK; SYN clears all three |
| L4 MSG | `TDVMessageLEDs` (NDCLED/NDSLED/NDBLED). Blink animates at ~1.5 Hz |
| LINE / CAR | Connection status = Connected |
| WAIT | Connection status = Connecting |
| ERROR | Connection status = Error |
| ON | Power lamp, always green |

Mechanism: `TDVEmulatorBase.LedStateChanged` (no payload — the subscriber re-reads
`KeyboardLights` and `MessageLEDState`, so a multi-lamp change is one repaint).
Events fire **only on an actual state change**, so a host re-asserting lamps every refresh
does not flood the UI thread. `VirtualKeyboardWindow` marshals to the UI thread and
unsubscribes on close.

`ProcessData` now reaching the TDV pipeline is what made this possible at all — before that fix
the lamp codes could never have arrived.

### Partially actioned: the 2115 C0 gate

Requested: ungate the whole documented "All Models" C0 set. **Only the four lamp codes
(ENQ/ACK/NAK/SYN) were ungated.** The rest was blocked by hard conflicts:

| Code | Why NOT ungated |
|------|-----------------|
| `0x0E` SO / `0x0F` SI | This handler implements the 2115 meaning (Underline / Normal). The "All Models" table defines them as Shift Out / Shift In — G1/G0 charset invocation — which `TDV2200Emulator.HandleExecute` already implements correctly. Ungating would silently break character-set switching. |
| `0x0B` VT, `0x18` CAN, `0x1C` FS, `0x1D` GS | **Test conflict.** `TDV2115KeyboardModeTests.TDV2200_NormalMode_IgnoresC0CodesForCursorMovement` explicitly asserts `0x18` must NOT move the cursor when 2115 mode is off. Enabling these failed that test. Docs and test suite disagree. |
| `0x0C` FF, `0x17` ETB, `0x19` EM | Roll/erase. No test covers them either way; enabling them alongside the base emulator's own FF handling risks double-handling. Not changed without evidence. |
| `0x02` STX, `0x03` ETX, `0x04` EOT | Video off/on and erase-line are **not in the "All Models" table at all** — genuinely 2115-only. Enabling them would be invention. |

**Open question for you:** the cursor-code row above is a real contradiction between
`docs/TDV-COMPREHENSIVE-REFERENCE.md` and an existing deliberate test. Note that project memory
records the TDV *keyboard* sending these same codes (UP=0x1C, DOWN=0x0B, LEFT=0x08, RIGHT=0x18,
HOME=0x1D) in **all** modes, which argues the terminal should honour them in all modes too — i.e.
the docs may be right and the test may encode a wrong assumption. Resolving it means either
changing that test or accepting reduced fidelity. Not decided here.

## Verification

- `dotnet build RetroTerm.sln` → 0 errors.
- `dotnet test RetroTerm.sln` → **2910 passed, 0 failed, 41 skipped** (skips pre-existing).
- 34 new tests: 11 in `tests\RetroTerm.Tests\TDV\TDVDleCursorAddressingTests.cs` (the four
  captured positions, ANSI-equivalence, no-garbage-in-buffer, packet-split, both encodings,
  bounds), 8 in `tests\RetroTerm.Tests\Logging\WireScannerDleTests.cs`, and 15 in
  `tests\RetroTerm.Tests\TDV\TDVIndicatorLampTests.cs`.
- NOT verified: a live type-53 session against the ND-500 emulator. The repro in the report
  should be re-run to confirm the linker form positions correctly on screen.
- NOT verified: the lamps rendering in the running app. The wiring is covered by unit tests at
  the emulator boundary, but `AttachLedSource` / `UpdateConnectionLamps` / the blink timer have
  only been compile-checked, not seen on screen.

## Files changed

| File | Change |
|------|--------|
| `src\RetroTerm.Core\Terminal\Emulators\TDV\TDVEmulatorBase.cs` | `ProcessData` override routing to `ProcessInput` |
| `src\RetroTerm.Core\Terminal\Emulators\TDV\Components\TDV2115CompatibilityHandler.cs` | `BeginDLE()`; encoding docs |
| `src\RetroTerm.Core\Terminal\Emulators\TDV\Components\TDVInputProcessor.cs` | DLE consumed first; SS2/SS3 interception removed |
| `src\RetroTerm.Core\Logging\WireScanner.cs` | `DleCoordinates` state |
| `src\RetroTerm.Core\Logging\EscapeSequenceDecoder.cs` | `DecodeControl(0x10)` named |
