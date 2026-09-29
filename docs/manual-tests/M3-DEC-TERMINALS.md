# M3 — VT220 / VT320 / VT340 / VT420, and the identity of all fourteen

**Full path:** `docs\manual-tests\M3-DEC-TERMINALS.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Machine cover:** `tests\RetroTerm.Tests\Avalonia\ManualPlan\M3DecTerminalTests.cs`
**Generated artefact:** `tests\RetroTerm.Tests\Avalonia\images\rendered\terminal-answers.md`

P1 walked these terminals against the real manuals and closed every gap it found. So this pass is
not about whether a sequence matches a page. It is about the two things a page cannot show: whether
a real program is happy, and whether what the terminal SAYS about itself is true.

---

## M3.1 — What each terminal answers

A host asks who it is talking to before it decides what to send, and a wrong answer does not make
the screen wrong — it makes it **empty**. A Sixel host told the terminal cannot draw sends no
picture at all. That is not hypothetical: the VT240 was refusing Sixel it could draw perfectly,
because its device attributes claimed only ReGIS.

Those answers are spread over fourteen terminals and several manuals, so the suite writes them all
into one sheet:

> `tests\RetroTerm.Tests\Avalonia\images\rendered\terminal-answers.md`

Every terminal, eight questions each — DA1, DA2, DA3, DSR status, DSR cursor, and three DECRQSS
requests — with the reply as readable text and as hex. The questions are really asked of live
emulators and the replies really captured, so the sheet cannot drift from what a host receives.

### M3.1a — Read the sheet against the manuals

**Needs:** the sheet, and the manuals in `spec\DEC\`.
**Do:** for each terminal you care about, compare its DA1 line with the manual for that model.
**Pass:** each reply names the right model and lists only capabilities the terminal really has.
**Look hardest at the blanks.** A blank is a terminal that stayed silent, and silence is two things
at once: the correct answer to a question a model does not support, and exactly what a swallowed
sequence looks like. DA3 is blank for the older models and should be — it is a VT420 feature.
**Machine cover:** `M3_1b` asserts every terminal answers DA1 at all, with two exemptions that are
themselves tested: a **4014** has no ANSI device attributes and answers `ESC ENQ` instead, and a
**VT52** predates ANSI entirely and answers `ESC Z`. `M3_1c` sends a real one-character Sixel image
to every terminal whose DA1 claims capability 4 and asserts something is drawn.
**Judge:** eye, once per model.
**Result:** ______  **Date:** ______  **By:** ______

### M3.1b — Ask a real host what it thinks we are

**Needs:** a real host over SSH.
**Do:** `echo $TERM`, then `infocmp | head`, then run something that probes — `tput colors`.
**Pass:** the host's idea of the terminal matches the one you picked in the dropdown.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M3.2 — The TestServer's DEC branch

**Needs:** the TestServer, main menu **3**. Nothing outside.

| Key | Test | Pass |
|---|---|---|
| 1 | Reports and Identity | Every reply printed as text **and hex** beside its expected shape |
| 2 | Page Memory | DECSLPP, then NP, PP, PPA, PPR, PPB across three written pages |
| 3 | Rectangle Operations | DECFRA, DECCRA including an **overlapping** copy, DECERA, DECSERA, DECRQCRA |
| 4 | Margins and Column Editing | DECLRMM, DECSLRM, DECIC/DECDC, SL/SR, origin mode against the left margin |
| 5 | Soft Font and User Keys | DECDLD in the 96- **and** 94-character forms, then DECUDK on F1 and F2 |
| 6 | Status Line | DECSASD, DECSSDT |
| 7 | National Character Sets | Ten NRCS sets, one per row, Norwegian and Danish included |
| 8 | Conformance Levels | DECSCL 61–65, each re-queried with DA |
| 9 | VT52 Mode | Enter, `ESC Y` addressing, graphics mode, `ESC Z` identity, and back |
| A | Selective Erase | DECSCA, then DECSEL and DECSED sparing what is protected |

**Two of these probe things that are deliberately not built**, and the menu says so on screen:
the 25th-line geometry and the indicator line of the status line. Nothing happening there is the
known state, not a defect you have just found.

**On rectangles, look for smearing.** A copy whose source and destination overlap is the case that
separates a correct implementation from one that copies cell by cell in the wrong order.

**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M3.3 — Soft fonts you can actually read

**Needs:** the app, on a VT340; the TestServer's DEC menu item 5.
**Do:** download a soft font, then print text in it. Open `softfont-block.png`,
`softfont-back-to-ascii.png`, `softfont-not-selected.png` and `softfont-undefined.png` beside the
screen.
**Pass:** the downloaded glyphs appear where the manual says, and they are **legible at your font
size** — which is the part no test can answer.
**Corrected 2026-08-18 — this case used to say the glyphs were never drawn. That was wrong.**
An earlier version of this document warned that `TryGetGlyph` had no caller and that downloaded
glyphs would not appear at all. It does have one — `SystemFontRenderer.GetSoftGlyph` — and the
renderer is handed the font in `TerminalEmulatorFontRendererExtensions`. Verified by rendering:
`softfont-block.png` shows a downloaded solid block drawn in a cell where a SPACE would otherwise
draw nothing. **So if your downloaded glyphs do not appear, that IS a new defect — report it.**

**Machine cover:** `SoftFontRenderingTests` — five tests through the real render chain, and the
DECDLD parser tests, including the 94-character form whose size parameter was being ignored (every
glyph of a 96-character download used to land one position to the left).
`TheDownloadedShapeIsDrawnTheRightWayUp` downloads a shape lit in its top half only, because the
solid block the other four use is symmetric and would look identical drawn upside down.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M3.4 — vttest, where it is available

**Assumption: `vttest` is not installed on this machine and has not been checked for.** It is the
standard DEC exerciser, packaged for Debian and Ubuntu as `vttest`.

| Menu | Covers | Pass |
|---|---|---|
| 1 | Cursor movement | Every screen matches vttest's own printed description |
| 2 | Screen features | Same |
| 3 | Character sets | Same |
| 6 | Reports | The replies match what the sheet in M3.1 gives |
| 7 | VT52 mode | Short sequences work, and `ESC <` gets you back |
| 9 | VT220 and up | DECUDK, user-defined keys |
| 11 | VT420 rectangles and margins | Rectangles land on the manual's coordinates; an overlapping copy is not smeared |

vttest prints what you should be seeing on the screen itself, which is why it is worth the setup.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M3.5 — Conformance levels

**Needs:** the TestServer, DEC menu item 8.
**Do:** set DECSCL to each of 61 through 65 and re-query with DA after each.
**Pass:** the DA reply changes to match the level, and sequences above the level stop working.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **The VT52 is the weakest row in the program** and is written up as such in the finish plan: no
  manual held here, no corpus entry, and a small surface. What can be checked is that it answers
  `ESC Z`, and that is asserted.
- **Whether downloaded glyphs are LEGIBLE at your font size** (M3.3). That they are drawn at all is
  settled — five rendered-pixel tests, one of which pins their orientation. Whether a 12-row matrix
  squeezed into your cell can still be read is taste, and only you can say.
- **The status line's 25th-line geometry and indicator line** are deliberately absent.
