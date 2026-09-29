# Run sheet — the by-hand pass, you and me together

**Full path:** `docs\manual-tests\RUN-SHEET.md`
**Updated:** 29 September 2026
**This sheet holds outstanding work only.** What earlier sessions found is in
`docs\manual-tests\FINDINGS-2026-08-20.md`.
**The cases themselves:** `docs\manual-tests\INDEX.md` and the M1–M8 documents
beside it. This sheet says **what order, which machine, and who does what**.

**Two items left, and both need the real D100:** the rest of the TDV key sheet (11) and s3-config
(12). Session 1 and Session 2 are both closed, and the one decision this sheet carried (13, Sixel and
ReGIS scrolling) was made on 9 September 2026 and built.

---

## How this works

**I drive, you judge.** Every case here survives because a test cannot answer it.

| Me | You |
|---|---|
| Open the session, log in, send the bytes | Look at the screen, listen, press the keys |
| Turn recording on before anything else | Say pass or fail, in your own words |
| Capture the trace when something looks wrong | Decide whether "wrong" is worth fixing |
| Write the result into the case, same day | — |

**Say fail freely.** A case that fails is worth more than one that passes: it is the only thing here
that turns into a test. If something merely looks *odd*, say so — half of what was found this
fortnight came from "that looks wrong" rather than from an assertion.

**Recording goes on FIRST.** `terminal_logstart` for the transcript, `terminal_tracestart` for the
bytes. A screen that looks wrong is worth little without the bytes that made it. If I forget, stop me.

**The app no longer has to be closed.** `.\scripts\publish.ps1` publishes to
`publish\versions\<version>-<n>\` and repoints the `publish\current` junction while the old build is
still running. The paragraph that used to sit here asking you to close RetroTerm by hand is gone.

---

## What is reachable — checked 28 August 2026, not assumed

| Name | Target | State |
|---|---|---|
| `D100` | `localhost:9010` | **UP.** ND-100 under RetroCore, real SINTRAN III VSX/500 |
| `D102` | `localhost:9102` | Up |
| `ubuntu18` | `ubuntu18lts.hackercorp.no:22` | Up, VT100 — has no mouse capability in terminfo, use ubuntu18-xterm for mouse work |
| `ubuntu18-xterm` | `ubuntu18lts.hackercorp.no:22` | Up, XTERM — created 31 August 2026, same host as ubuntu18 with a mouse-capable profile |
| `5000 CPU`, `OPCOM`, `WASM ND100x`, serial | — | Not needed by anything here |

---

## Session 1 — CLOSED, 1 September 2026

Nothing left in it. **M6.1a is PASS** — all fifteen Sixel and ReGIS sheets judged and approved,
which was the last item this session held. It found one real defect on the way: `extremeratio` was
missing the line of text along the top, traced to the renderer painting the graphics plane over the
text unconditionally, and fixed. `registest-bitplane`'s apparently missing text was NOT a defect —
its reference is a photograph of hackerb9's whole screen, and that sheet now says so itself.

M6.5a, M6.5b, M8.3b, M8.7b and M6.5d are all PASS as well. M6.5b's run is what surfaced the
click-to-place decision — see `PLAN.md`'s standing judgement calls. Evidence in
`M6-SIXEL-AND-REGIS.md` and `M8-WHOLE-TERMINAL.md`; the sheets themselves are at
`the "Phosphor Proof" artifact (DELETED 2 September 2026 after judging; regenerate from the compare-*.png sheets if it is needed again)`.

---

## Session 2 — CLOSED, 31 August 2026

M2.6, M2.2 and M2.3 are all PASS, driven live over the real ubuntu18 host. See `PLAN.md` for the
summary and `M2-XTERM.md` for the evidence, including the VT100-vs-XTERM finding on M2.2.

**The wheel's own arbitration is now measured** (`MouseWheelWiringTests`, 28 Aug): Ctrl+wheel zooms
even while the host tracks, a tracking host gets the wheel, Shift forces our scrollback. What vim
makes of the bytes is now measured too.

---

## Session 3 — telnet to D100 as TDV2200. About an hour

Setup is known to work and takes me under a minute:

```
open localhost:9010 as TDV2200, 80 x 25          <- 25 ROWS. Not 24.
TRACESTART, LOGSTART
SENDRAW ESC
SEND "SYSTEM\r\r"                                <- one send; splitting it bounces to a fresh prompt
SEND "set-term-type,,93\r"
```

### 3a — the TDV keyboard

**Setup verified working 1-2 September 2026**, and the D100 takes several lines at once, so this
session can run alongside whatever else is connected to 9010.

**Already finished and OFF this sheet**, so they are not asked twice: the four arrows, F1, and HOME
in extended mode all PASS, measured 1-2 September 2026 on the live D100. Evidence and the byte
values are in `M4-TDV.md`; chasing F1 also turned up 30 wrong sequences in the keyboard reference,
now corrected.

**The simple ASCII half is finished too**, 2 September 2026: HOME and all four arrows sent exactly
the same bytes in 2115 mode as in extended mode.

**One sentence of that was withdrawn on 11 September 2026.** It said the mode was "confirmed
active by a DECRQM round trip". That round trip went into RetroTerm's own receive path - RetroTerm
is the terminal here and the D100 is the host - so it measured this program answering a sequence no
TDV has. It said nothing about hardware. The key bytes are a genuine measurement and stand. See
`docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`.

**Still to do:**

| # | Case | What you are judging |
|---|---|---|
| 11 | **M4.x** | The rest of the sheet. `tdv2200-key-sheet.md` has a box per key and is generated from the registry every test run |

**Measured 28 August, and it narrows M4.2d:** neither of HOME's two candidate bytes is safe for a
host to echo. DLE `0x10` eats the next two characters; GS `0x1D` is worse — the graphics side takes
it as Tektronix enter-graph-mode, so every following character becomes a vector coordinate. So the
case cannot be settled by "pick the harmless one". **If SINTRAN accepts both bytes the case stays
open and I will say so** rather than record a pass.

---

### 3b — s3-config, and you drive this one

| # | Case | What we are after |
|---|---|---|
| 12 | **s3-config graphics** | The only ND program we know of that draws. Whatever it sends is the ND graphics work list |

**Your words: "s3-config has graphics and is for configuring the system, so be careful."** So the
split here is the opposite of everywhere else on this sheet, and you chose it:

- **You type.** Every keystroke into s3-config is yours. I send nothing.
- **I watch the trace** and read `UNHANDLED` after each screen.
- **Nothing gets saved or applied.** Back out the way you came in.

**What I need from you first:** is there a screen in it that draws something and can be reached
without changing anything? If the answer is no we skip it and say so — an unrun case is better than
a reconfigured machine.

### 3c — the watch item: CAUGHT and FIXED, 2 September 2026

**It reproduced, and the raw block settled it.** Pressing RIGHT showed in the decoded pane as
`TX 1B 18  ESC.  Unrecognised sequence` — an ESC glued to the cursor-right byte. The RAW block for
the same press reads `TX 18  BLOCK  1 bytes`.

**One byte on the wire. The keyboard is right and nothing wrong reaches the host.** `WireScanner`
attached a stale pending ESC to the next byte in the DECODED view only, so this is a
trace-rendering defect rather than a terminal one — which is exactly why the raw block had to be
read before anything was written down.

**Fixed the same day.** The one-second abandon rule was not the hole: the two keystrokes were a
fraction of a second apart, inside the second it waits. On the TX side one send is always one
complete byte string, so a sequence still pending when a send ends can never be finished by the
next one, and it is now flushed at the end of every TX feed. RX keeps the carry-over, because a
host's sequence really does arrive split across two reads. Pinned by
`WireScannerGluedKeystrokeTests`; the full account is `docs\PLAN.md` Phase 1. Nothing to watch for
here any more.

---

## Decisions — no machine, no session

None open. The one this sheet carried — **13, do Sixel and ReGIS pictures scroll or clear with the
text?** — Ronny decided on 9 September 2026: faithfully, as a real VT340 does with its one shared
bitmap. A whole-screen scroll moves the picture and `ED` mode 2 wipes it; a scroll REGION leaves the
planes alone, and `ED` modes 0, 1 and 3 do not clear them. Built and pinned by
`GraphicsScrollAndClearTests`; the reasoning is in `docs\PLAN.md` under the standing judgement calls.

---

## Never, unless something turns up

Listed so they stop looking like tasks:

| Item | What it would take |
|---|---|
| **IBM 3270** | A 3270 data-stream manual. Building it from recollection would invent EBCDIC orders and AID codes |
| **VT52** | Any manual at all. Its identity is checked and everything else is inferred |
| **ReGIS glyph shapes** | DEC's own font. The TDV2200 ROM stands in; 8 wide is right, 14 rows where ReGIS stores 10 is not. A one-method change if the shapes appear |
| **ND model identification bytes** | A machine that may never exist |
| **What a physical TDV keyboard sent** | An actual TDV. Session 3a gets the half that matters — what SINTRAN accepts — and cannot get this half |
| **Colour map register 7** | A VT340. The manual says 53% grey, hackerb9's photograph measures 46%, and six of the other seven registers agree within 8 — so this is a real disagreement, not a measurement error |
| **What mode 62 does** | The TDV2200 Programmer Reference Manual. Two sources show a TDV being sent it; neither says what it means. Counted, not implemented |
| **ReGIS grid labels four half-cells too high** | A manual that explains what clears accumulated PV spacing. The obvious fix was tried and REFUTED — chapter 7's own worked example needs the value to persist |

---

## Results

Filled in as we go. The detail goes in the case; this is the overview. **A finished row leaves this
sheet** — the ones below are still open.

| # | Case | Session | Result | Date |
|---|---|---|---|---|
| 10 | M4.2d + M4.2b, the simple ASCII half | 3a | **PASS** - HOME and all four arrows identical in 2115 and extended mode. The "confirmed by DECRQM" part was withdrawn 11 Sept, see above | 2026-09-02 |
| 11 | M4.x, the rest of the key sheet | 3a | PART - M4.2b, M4.2c, HOME all PASS in extended mode | 2026-09-01/02 |
| 12 | s3-config graphics | 3b | ______ | ______ |

**Closed since this sheet was last written, and off it:** decision 13, Sixel and ReGIS scrolling —
decided 9 September 2026 and built, `GraphicsScrollAndClearTests`. The 3c watch item, the glued ESC
in the trace — fixed 2 September 2026, `WireScannerGluedKeystrokeTests`. M6.1a, the fifteen hardware sheets - PASS,
1 September 2026, and it found the extremeratio renderer defect on the way. M7.1, the ND UNHANDLED sweep — run
28 August against nine SINTRAN programs, produced a six-key list, and turned up a real defect in our
own mode handling. M6.5c and M6.5e, both PASS. M8.4a, the bell, PASS. M5.1a, the five dash masks,
PASS. The 132-column question. The page-mode half of M4.2d.

---

## After each session, same day

Not optional, and short:

1. **Every failure becomes a test**, from the captured bytes rather than from the description.
2. **Every pass gets written into its case** with the date.
3. **Anything neither** — an odd-looking thing we decided not to chase — goes in
   `docs\PLAN.md` with the reason, so it is a recorded decision rather than something forgotten.
   (The separate needs-a-real-host list it used to go in was retired on 29 September 2026.)
4. **This sheet's results table gets the one-line version, and the finished row leaves the sheet.**

The rule the whole pass exists for: **the manual pass is for finding things that should have been
tests, not for standing in for tests.** Anything it settles is automated the same day.
