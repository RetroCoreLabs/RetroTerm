# Manual test documents — index and rules

**Full path:** `docs\manual-tests\INDEX.md`
**Started:** 2026-08-17
**Parent:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` — the one-page overview.
**Grandparent:** `docs\FINISH-PLAN-2026-08-11.md` — why each area matters.

The overview says *what* to test in a table. These documents say *how*: the exact keystrokes, the
exact bytes, what a pass looks like, and — for every case — whether a machine already checks it or
whether it needs Ronny's eyes, ears or hardware.

---

## The documents

| Doc | Covers | Needs | Full path |
|---|---|---|---|
| M1 | ANSI and VT100 — the core | the TestServer | `docs\manual-tests\M1-ANSI-AND-VT100.md` |
| M2 | xterm — real programs, and two open decisions | a real host over SSH | `docs\manual-tests\M2-XTERM.md` |
| M3 | VT220 / 320 / 340 / 420, and what all fourteen answer | the TestServer | `docs\manual-tests\M3-DEC-TERMINALS.md` |
| M4 | TDV1200 / 2215 / 2200 — the keyboard above all | a real TDV keyboard | `docs\manual-tests\M4-TDV.md` |
| M5 | Tektronix 4010 / 4014 — patterns, sizes, two-column writing | nothing, then gnuplot | `docs\manual-tests\M5-TEKTRONIX.md` |
| M6 | Sixel and ReGIS — the hardware comparison sheets, and graphics input by hand | nothing | `docs\manual-tests\M6-SIXEL-AND-REGIS.md` |
| M7 | Norsk Data `ESC "` graphics — produces a work list | a real ND machine | `docs\manual-tests\M7-NORSK-DATA.md` |
| M8 | The terminal as a whole — resize, zoom, scrollback, copy/paste, bell, changing the emulation | nothing | `docs\manual-tests\M8-WHOLE-TERMINAL.md` |

**The set is complete.** There is no M0 document: the overview's setup table is short enough to
stay where it is, and no section for the printer beyond M6.6 or for IBM 3270, because one has no
manual pass of its own and the other has no code.

## Running a pass with Ronny — start here

`docs\manual-tests\RUN-SHEET.md` puts the open cases in the order that shares
setup, says which machine each needs, and splits the work: I drive, Ronny judges. It exists because
the documents below say *how* to run a case and never said *when*, which is why the list stopped
moving. It also records what is genuinely reachable on this machine — a real SSH host and real
SINTRAN both are, which the plan had wrong.

**It holds outstanding work only.** What the sessions already run found — the ND work list, the
132-column answer, the PED keyboard evidence, and two corrections to my own findings — is in
`docs\manual-tests\FINDINGS-2026-08-20.md`.

## If you only have an hour

In this order, because this is where the unknowns are:

1. **M6.1a** — page through the eleven comparison sheets. Nothing else in the program is checked
   against real hardware.
2. **M2.6** — one multi-line paste. It closes the cheapest open item in the finish plan.
3. **M5.1a** — one look at the five dash patterns, which are a reading of five words and nothing
   more.
4. **M8.3b** and **M8.4a** — copy a wrapped line, and listen to the bell.
5. **M8.7b** — zoom to 300% and type. Three faults hid behind sixteen green tests here, and only an
   eye found them. The measurable half is now automated, so what is left is whether it FEELS right.
6. **M6.5b** — steer the graphics input crosshair with the arrow keys. Every part of it is pinned by
   a test; whether one pixel per press is usable at all is a question no test can ask.

## What the suite writes for these documents

Artefacts live beside the rendered PNGs in
`tests\RetroTerm.Tests\Avalonia\images\rendered\`. They are produced by the
test run, so they are always current:

 - `compare-*.png` — our Sixel beside the same stream drawn by real VT340 hardware, eleven pairs.
 - `tek-line-patterns.png` — the five vector patterns, at one plane pixel per image pixel.
 - `tek-character-sizes.png` — the four aligned sizes, normalised to one screen width.
 - `tek-two-column-writing.png` — what a storage tube does instead of scrolling.
 - `tdv2200-key-sheet.md` — every TDV2200 key and the bytes it sends, with a box to tick.
 - `bell.wav` — the bell, to listen to without running the app.
 - `print-*.pdf` — the printed pages.

---

## How a case is written

Every case has a **stable ID** (`M8.3b`). The ID never changes and never gets reused, because it is
what the automated test names itself after and what a recorded result refers to.

```
### M8.3b — <one line saying what is being checked>

**Needs:**        what host, program or hardware — "nothing but the app" when that is true
**Setup:**        numbered, exact
**Do:**           numbered, exact — keystrokes and bytes, not descriptions of keystrokes and bytes
**Pass:**         one sentence a person can answer yes or no to
**Machine cover:** the test that already checks part of this, and WHAT IT CANNOT SEE
**Judge:**        machine / eye / ear / hardware
**Result:**       ______  **Date:** ______  **By:** ______
```

**"Machine cover" is the important line.** It is not there to reassure; it is there to say what the
automated test is blind to, so the by-hand pass spends its time on the part that needs a person. A
case whose machine cover is complete should not be in a manual document at all.

---

## The two-way link with the test suite

A case that can be checked headlessly gets an `[AvaloniaFact]` whose method name carries the case ID:

```csharp
/// <summary>
/// M8.2a - scrolling back shows rows that have left the screen.
/// </summary>
[AvaloniaFact]
public void M8_2a_ScrollingBackShowsRowsThatHaveLeftTheScreen()
```

Live in `tests\RetroTerm.Tests\Avalonia\ManualPlan\`, one file per section.

So the link runs both ways: the document names the test, the test names the case. Rename one and the
other is found by searching for the ID.

**These tests do not replace the manual pass.** They pin the part that has a right answer in code,
which is exactly the part a person should not have to check twice. Everything left over — whether a
beep sounds like a beep, whether a colour looks right, whether a real ND host is happy — is what the
manual pass is *for*, and no amount of test writing will shorten it.

---

## Rules for running a pass

1. **Turn recording on first.** `terminal_logstart` for a transcript and `terminal_tracestart` for
   the bytes. A screen that looks wrong is worth little without the bytes that made it.
2. **Write the result in the case.** An unrecorded pass gets re-run from scratch next month.
3. **When something fails, capture the bytes** (`terminal_traceread`) as well as the screen
   (`terminal_snapshot`). Only the bytes can be turned into a test.
4. **Anything a document settles becomes an automated test the same day.** The manual pass exists to
   FIND those, not to stand in for them.
5. **Anything no document settles** goes in `docs\NEEDS-A-REAL-HOST-2026-08-11.md`
   with the reason.

---

## What these documents will not do

They will not describe behaviour the program does not have. Writing a case is a check in itself:
M8.1 was going to say "the host is told the new size", because that is what the overview says, and
the source says otherwise — `TelnetConnection.UpdateWindowSizeAsync` has no caller anywhere in the
repository. The case now describes what actually happens and the gap is recorded as a gap.
