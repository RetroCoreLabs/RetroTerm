# M1 — ANSI and VT100, the core

**Full path:** `docs\manual-tests\M1-ANSI-AND-VT100.md`
**Index:** `docs\manual-tests\INDEX.md`
**Overview row:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` §M1
**Backs:** `docs\FINISH-PLAN-2026-08-11.md` P0

**This is the best-evidenced area in the program, and that changes what the pass is for.** 76
xterm.js screens captured from a real xterm all match exactly, and 393 libvterm assertions run
green. Repeating those checks by hand would find nothing.

So this pass asks the questions the corpora cannot: does it *behave* as well as it *matches*, and
does it look right while doing so.

---

## M1.1 — The TestServer's Standard Tests, on VT100 and then ANSI

**Needs:** the TestServer only. Main menu **1**. Run the whole menu twice, once per terminal type.

| Menu | Test | Pass |
|---|---|---|
| 1 | Basic Colors | Eight foreground and eight background colours, all distinct, none bleeding into the next cell |
| 2 | Cursor Movement | The cursor lands where the text says, including all four corners |
| 3 | Character Attributes | Bold, underline, blink, reverse and their combinations, each clearly different from plain |
| 4 | Scrolling | Smooth, with no torn or duplicated row at either end |
| 5 | Line Drawing | A box with unbroken corners and joins — a gap means the DEC graphics set is mapped wrong |
| 6 | Screen Clear | Nothing left behind, and the scrollback touched only where the test says |
| 7 | 256 Colours | A smooth ramp with no banding that looks like duplicate entries |
| 8 | Tab Stops | Columns line up at 8, custom stops honoured, a cleared stop skipped |
| 9 | Character Sets G0–G3 | Switching changes the glyphs and switching back restores them |
| A | Scrolling Region | Only the region scrolls; text above and below stays still |
| B | Cursor Save/Restore | The cursor returns to the saved spot **with its attributes** |
| D | VT100 Key Decoder | Every key prints the sequence the VT100 manual gives |

**Machine cover, and it is heavy.** The xterm.js corpus covers scrolling, editing, tabs and the
right-hand edge; libvterm covers state and callbacks; `attribute-bold.png`, `attribute-reverse-
video.png`, `attribute-hidden.png`, `blink-*.png`, `dsize-*.png`, `scroll-region-before/after.png`
and `erase-before/after.png` are rendered every run. What none of them can answer is **B** with
attributes, and whether the colours look distinct on your monitor.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M1.2 — Blink, which is time and cannot be tested

**Needs:** the app.
**Do:** run Standard Tests item 3 and watch the blinking text for half a minute.
**Pass:** it blinks at a steady rate, the slow and fast rates are visibly different, and text stops
blinking cleanly when the attribute is turned off — no cell left permanently invisible.
**Machine cover:** `BlinkAttributeRenderingTests` drives the blink phase directly rather than
sleeping, and `CursorBlinkLifetimeTests` proves a replaced renderer's timer really stops. Neither
can tell you whether the rate looks right.
**Judge:** eye. **Never let a test sleep its way to a pass here** — drive the callback, as those two
do.
**Result:** ______  **Date:** ______  **By:** ______

---

## M1.3 — vttest menus 1 to 3

**Assumption: vttest is not installed on this machine and has not been checked for.**

**Do:** run menus 1 (cursor movement), 2 (screen features) and 3 (character sets) over telnet or
SSH.
**Pass:** every screen matches vttest's own printed description — it tells you on screen what you
should be seeing, which is the whole reason it is worth the setup.
**Why it is still worth running with a clean corpus:** vttest drives the terminal the way a program
does, in sequences nobody chose for a test file. The corpus proves we match recorded screens; this
asks whether we survive being used.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M1.4 — The keyboard decoder

**Needs:** the TestServer, Standard Tests item **D**.
**Do:** press every key, including the arrows with and without application cursor mode, the keypad
in both modes, and the function keys.
**Pass:** each prints the sequence the VT100 manual gives.
**Machine cover:** the keyboard mapper tests, and `UserDefinedKeyPressTests` for DECUDK.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **Whether the colours look right on your monitor.** Pixels can be asserted; taste cannot.
- **Blink rate and smoothness** — M1.2.
- **Anything vttest would find**, until vttest is available.
