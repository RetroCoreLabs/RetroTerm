# M5 — Tektronix 4010 / 4014

**Full path:** `docs\manual-tests\M5-TEKTRONIX.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Backs:** the gap list at `docs\TEKTRONIX-4014-GAP-2026-08-17.md`
**Machine cover:** `tests\RetroTerm.Tests\Avalonia\ManualPlan\M5TektronixTests.cs`

Every item the 4010/4014 chapter of the VT330/VT340 Graphics Programming manual asks for is built.
What is left is judgement, and it splits in two:

- **Things a document settles but only a picture can show** — the dash patterns, the four character
  sizes, two-column writing. Sheets are produced for each; §M5.1.
- **Things only a real plotting program can show** — gnuplot; §M5.2.

---

## Before you start

    dotnet test src\RetroTerm.slnx

Artefacts land in `tests\RetroTerm.Tests\Avalonia\images\rendered\`.

---

## M5.1 — What the suite draws for you

### M5.1a — The five vector patterns

**Needs:** one test run.
**Do:** open `tek-line-patterns.png`. Five lines, top to bottom, in the manual's own order:

    solid       ESC `
    dotted      ESC a
    dot-dash    ESC b
    short dash  ESC c
    long dash   ESC d

**Pass:** five lines that are each clearly what they are called, and clearly different from one
another. Dotted has even single gaps; dot-dash alternates a long mark with a short one; the short
dash is visibly shorter than the long dash.

**This sheet is the graphics PLANE at one pixel to one pixel, not a screenshot** — and it had to
become one. As a screenshot it lied: the renderer stretches the 1024-wide plane over a text area
about 535 pixels across, and a pattern that alternates one pixel on with one off cannot survive
being sampled at half scale. It aliased into runs of several pixels, so the dotted line showed the
*longest* marks of the three and the short dash showed the shortest — the reverse of the truth. The
masks are what this case judges, so the plane is what it shows.

**Judge this one properly — the numbers are ours, not DEC's.** The manual names the five patterns and
**does not print their dash lengths**. The sixteen-bit masks in `LinePatternMask` are this
emulator's reading of those names, written in binary so the shape reads off the source. The tests
pin the *shape* — a dotted line has gaps, a long dash runs longer than a short one, the phase
carries around corners — because pinning exact pixels would only test our choice against itself.
So if a pattern looks wrong to you, it is wrong, and the mask is the thing to change.

**Machine cover:** `TektronixMarginsAndWritingModeTests` and the pattern tests pin the shape rules.
They cannot judge whether "dot-dash" looks like dot-dash.
**Judge:** eye. **This is the case most likely to need your opinion.**
**Result:** **PASS.**  **Date:** 25 August 2026  **By:** Ronny, by eye, on the 1:1 plane sheet.

**What was actually checked, and what it settles.** Two specific questions rather than one general
one, because the general one had already been answered "what am I looking at":

 - **Line 2 (dotted, 1 on / 1 off) against line 4 (short dash, 2 on / 2 off).** These two are only
   one pixel apart in mark length, so the worry was that both would read as the same thin grey
   haze. Ronny's answer: **two clearly different styles.** One reads as dots, the other as short
   dashes. My prediction — that line 2 would smear into solid grey — was **wrong**, and the mask
   stays at 1 on / 1 off.
 - **Line 3 (dot-dash, 4 on / 4 off / 1 on / 7 off).** The lone one-pixel dot is the part most
   likely to vanish. Ronny's answer: **the dash and the dot alternate visibly.** The mask stays.

**So all five masks in `LinePatternMask.For` are confirmed by eye and none of them changed.** That
matters because the numbers are ours, not DEC's — the manual names the five patterns and prints no
lengths. This is now the only evidence that the reading of those five words is right, and it is a
human's, which is the best this case can ever have.

### M5.1b — The four character sizes

**Needs:** one test run.
**Do:** open `tek-character-sizes.png`. The same sentence at all four aligned sizes, stacked:

| Sequence | Grid |
|---|---|
| `ESC 8` | 35 lines of 74 characters — the power-on size |
| `ESC 9` | 38 lines of 81 |
| `ESC :` | 58 lines of 121 |
| `ESC ;` | 64 lines of 133 |

**Pass:** four legible sizes, each smaller than the one above, and the text of each fits its width.

**Every panel is scaled to the same width, on purpose.** A capture is taken at its natural size —
columns times cell width — so a 133-column screen comes out *wider* with glyphs of exactly the same
size, and four such panels stacked show four identical sentences and answer nothing. A real 4014's
glass does not grow: its width is fixed and more columns means smaller characters. The sheet
normalises the width so the text shrinks the way it does on the tube.

**What a real tube does that this cannot.** A storage tube keeps what is already drawn, so changing
the size changes the cell for text written *afterwards* and leaves the glass alone — one screen can
carry two sizes at once. This buffer has one cell size for the whole grid, so existing text is
re-laid out instead of staying put. In practice a host sends the size straight after an erase, so
this rarely shows. It is written down rather than hidden.

**Machine cover:** the size tests pin the grid dimensions. Legibility at each size is this case.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M5.1c — Two-column writing

**Needs:** one test run.
**Do:** open `tek-two-column-writing.png`.

A 4014 cannot scroll — it is a storage tube. When the screen fills, it starts again at the **top**
in a second column down the middle of the screen, overstriking whatever is there, and when that
fills it goes back to the left. That is what it does *instead* of scrolling.

**Pass:** the numbered lines run down the left half, then continue from the top of the **right**
half. The second column starts at the horizontal centre.
**Machine cover:** `TektronixMarginsAndWritingModeTests` pins the switch and the snap to the centre
column, including the case that caught a defect — a character that fills the last column leaves the
cursor there with the wrap pending, so the snap had to happen after the wrap was resolved, not
before.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M5.1d — The three gnuplot streams, drawn

**Needs:** the fetched corpus and one test run.
**Do:** open `tek-corpus-box.png`, `tek-corpus-points.png`, `tek-corpus-sin.png` and
`tek-corpus-sin-74x35.png`.
**Pass:** a curve with axes, a border, and **axis labels sitting at their ticks**.
**Watch the labels.** A diagonal cascade of labels down the screen is a defect that already shipped
once: the curve, axes and border were all correct and every label was in the wrong place, because
leaving graph mode must park the text cursor at the **last plotted point**. Nothing asserted it,
because the corpus tests checked that labels were printed as text — which they were.
**Machine cover:** `TektronixCorpusRenderingTests` and `TektronixCorpusTests`. Label *position* is
pinned now; whether the whole plot reads correctly is this case.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M5.2 — A real plotting program

**Needs:** any host with **gnuplot**. That is the program that exercises this for real, and no test
server can imitate it.

| # | Step | Pass |
|---|---|---|
| M5.2a | `gnuplot`, then `set term tek40xx`, then `plot sin(x)` | A curve with axes, border and labels, all in place |
| M5.2b | Look at the labels specifically | Each sits **at** its tick. A diagonal cascade is the shipped defect above |
| M5.2c | `plot` again without clearing | The old plot is still there underneath — a storage tube does not erase itself |
| M5.2d | Leave graph mode and type | Text starts **at the last plotted point**. Derived, not quoted from a document, and gnuplot depends on it |
| M5.2e | `set term tek40xx; set output` to a file, then `cat` the file to the terminal | Same picture as drawing it live |

**Result:** ______  **Date:** ______  **By:** ______

---

## M5.3 — Tektronix mode from the TDV side

**Needs:** the TestServer, connected as **TDV2200**.
**Do:** main menu 2, then the Tektronix Mode test.
**Pass:** 4010 compatibility draws from the TDV side too — the same vectors, through a different
front door.
**Machine cover:** `TektronixVectorRenderingTests` drives exactly this path (`ESC "17h` to show the
plane, then GS and coordinates) and asserts the coordinate bytes are **not printed as text**, which
is what goes wrong first.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M5.4 — Phosphor

**Needs:** the app.
**Do:** draw a plot, then switch the theme to amber.
**Pass:** **the vectors go amber with the text.** A real single-phosphor machine cannot draw a green
line on an amber screen.
**Why this case exists:** it shipped wrong. Text followed the theme and vectors stayed green, because
the draw colour was a constant that happened to match the default green. Nothing compared the two.
**Machine cover:** `GraphicsFollowTheThemeTests`.
**Judge:** eye, once per release.
**Result:** ______  **Date:** ______  **By:** ______

---

## M5.5 — Hard copy

**Needs:** one test run.
**Do:** open `print-tektronix.pdf` — the three gnuplot streams printed, three pages.
**Pass:** each page carries the plot that the matching PNG shows.
**What this exercises that the PNGs do not:** `ESC ETB` asks the terminal to print a hard copy of its
own bitmap **by re-encoding it as sixel**. So the vectors are drawn, then encoded, then decoded
again by the PDF writer. A fault anywhere in that chain shows here and nowhere else.
**Machine cover:** `PdfPrintSinkTests.TheTektronixStreamsPrintAsHardCopies` — page count and that
each page carries a picture. Appearance is this case.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **No real 4014 exists here**, and none is likely to. The manual and the three gnuplot streams are
  the whole oracle.
- **The dash masks are a reading, not a measurement** (M5.1a). If a real 4014 ever turns up, that is
  the first thing to photograph.
- **A storage tube's persistence is not emulated**, and cannot be: the screen here is a buffer with
  one cell size, so a size change re-lays out text that a real tube would have left burnt on the
  glass.
