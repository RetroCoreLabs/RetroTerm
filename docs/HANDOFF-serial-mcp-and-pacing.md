# Handoff — MCP serial, COM11 release, and transmit pacing

Written 31 August 2026, 02:xx, at the end of a long session. Everything below is committed;
nothing is pushed.

## What was done, in commit order

| Commit | What |
|---|---|
| `1f803ac` | `terminal_open name=<stored connection>`; ad-hoc serial fields on open/connect; `connsave` serial round-trip; port disposal on every path; serial-exclusivity guard shared with MCP |
| `e1db90f` | `PdfPrintSink.DecodePicture` checks the bitmap actually allocated. **Unproven** — see below |
| `c91f39a` | `SerialPort.ParityReplace = 0` — the question marks |
| `db60f68` | Keyboard tab: DEL keys, New-Line receive/transmit, Local Echo |
| `fd17bf4` | **The port-release fix** + Save button visibility + disconnection-notice advice |
| `2ad56fe` | Transmit pacing: msec/char, msec/line |

Published as `1.0.26.2-2` from a clean tree at `2ad56fe`.

## THE NEXT ACTION

Ronny restarts RetroTerm from `publish\current\RetroTerm.Desktop.exe` and does
**connect COM11 → disconnect → connect** again. That is the one thing not yet confirmed in the
real application.

It IS confirmed at the class level: driving the shipped `SerialConnection` on COM11 directly,
the pre-fix code **hung in `DisconnectAsync` and never returned** (killed after five minutes,
port still held), and the fixed code releases on all three teardown paths in ~130 ms. Script:
`%TEMP%\claude\E--Dev-Ronny-RetroTerm\<session>\scratchpad\verify-port-release2.ps1`.

## The bug that mattered, so it is not reintroduced

`DisconnectAsync` used to cancel the token, `await` the receive task, and only then close the
port. **`SerialPort.BaseStream.ReadAsync` on Windows does not honour a cancellation token** — a
pending read stays pending until bytes arrive or the handle is closed. A SINTRAN console is
silent most of the time, so cancelling did nothing, the await never returned, and the close and
dispose beneath it never ran.

Three earlier disposal fixes were made and all of them were useless, because every one sat
BELOW that await. **Close the handle first; it is what makes the pending read fail.** The wait
afterwards is bounded (2 s) so a loop stuck for any other reason cannot hold the port.

## Things a fresh session would not guess

- **`SerialPort.ParityReplace` defaults to 63 = `'?'`.** Any parity setting other than None turns
  on the Windows error character, so a byte failing its parity check is handed over as a question
  mark embedded in the data. Measured; pyserial at the same 7E1 returns clean bytes because it
  never sets that field.
- **The ND-120's UART is 8N1 with no parity logic at all.** Read from
  `nd-120/Verilog/Shared/support/SC2661_UART.v` — no parity state in either state machine, and
  the word "parity" appears nowhere in the file. `HARDWARE.md` described the REAL chip's
  capability, which is what led to a 7E1 setting.
- **`nexys-115200` was changed from 7E1 to 8N1** in `%AppData%\RetroTerm\host-configurations.json`.
  Backup: `host-configurations.json.before-8n1-20260831-001922.bak`.
- **Transmit pacing gaps are minimums.** `Task.Delay` cannot go below the ~15.6 ms Windows timer
  tick, so a requested 10 ms/char measured ~15.8. Fine for "do not overrun the UART", useless for
  a precise character rate.
- **An inline `Background` in axaml beats a style selector.** The Save button could not be
  coloured until the inline brushes were removed.

## Open threads

- **Nothing is pushed.** Six commits on `master` here; `773651f` on branch `mister` in
  the nd-120 repository, a separate checkout, which also holds **19 unstaged files of Ronny's own work** — that
  is why only two files were staged there, by explicit path.
- **The 6-in-42 question is unexplained.** A 7E1/8N1 framing mismatch predicts a parity failure on
  nearly every odd-population character, which would shred the text. Only 6 of 42 bytes came back
  as `?`, clustered around the echo and the prompts. The mismatch is verified from source; the
  rate and clustering are not. Settling it needs a controlled capture with COM11 free.
- **`e1db90f` is not a proven fix.** `PdfPrintSinkTests.TheTektronixStreamsPrintAsHardCopies` threw
  inside `Draw` twice on 30 August and has since passed seven consecutive full runs. Both failures
  coincided with another repository's test suite loading the machine. The bitmap-allocation check
  is correct on its own merits; it was never shown to be the cause.
- **A peer Claude session ("RTC") requested the pacing feature and then vanished** before the reply
  could be delivered. Its two surprises are in `2ad56fe`'s commit message: the setting is
  per-connection rather than a global Preferences tab, and the gaps are minimums.
- **Serial only.** Pacing is not wired for telnet. RTC noted the gap is general and was right.

## Test state

Full suite at `2ad56fe`: **6604 passed, 0 failed, 41 skipped**, plus 123 in the Kermit suite.
