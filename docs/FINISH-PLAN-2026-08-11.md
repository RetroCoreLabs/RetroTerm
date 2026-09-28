# Plan to finish the terminal emulators — 11 August 2026

> **SUPERSEDED.** The living plan is `docs\PLAN.md`, which holds only
> outstanding work. This file is kept for its record of what was decided and why, not as a
> to-do list.


**Full path:** `docs\FINISH-PLAN-2026-08-11.md`

**SUPERSEDED on 2026-08-18 by `docs\PLAN-2026-08-18.md`.** P0 to P3 here are
finished; P4 to P7 are carried forward there with what the vt340test ReGIS corpus has since
shown. This document is kept because every row of it records how something was found.

This is the ordered list of what is left, why each item is where it is, and what "done" means for
it. Priorities are **P0 highest**. Everything below P4 is blocked on something outside the code.

**Every phase whose code is validated as working also carries a by-hand pass**, written up in
`docs\MANUAL-TEST-PLAN-2026-08-17.md` — which host, app or program exercises
it, the steps, and what a pass looks like. Each phase below links to its section. Phases with no code
yet (P4.1 printer, P7 IBM 3270) have no manual section, because there is nothing to exercise.

**State this plan starts from**

| | |
|---|---|
| Suite | 5675 + 123 passing, 41 skipped, 0 warnings (2026-08-17) |
| xterm.js screen corpus | 76 fixtures, **all clean** (2026-08-17) |
| libvterm corpus | 43 scripts, **4 still disagree, 9 assertions** |
| Tektronix corpus | 3 real gnuplot streams, all clean |
| Terminals built | VT52, VT100, VT102, VT220, VT240, VT320, VT340, VT420, ANSI, XTERM, XTERM-256COLOR, TEK4014, TDV1200, TDV2215, TDV2200 |
| Terminals not started | IBM 3270 (deliberately — see P7) |

Neither corpus is committed. `tools\fetch-conformance-corpora.ps1` fetches both on demand, and the
tests pass with them absent.

---

## P0 — Finish the xterm.js screen corpus — COMPLETE

**Why first.** It is the only oracle here whose expected output was captured from a *real xterm*
rather than written by anyone's reading of a spec, and every row it still disagrees on is a real
screen a real program could produce. It is also nearly finished — 879 rows became 46.

Each entry below is one tick of work: diagnose against `spec\DEC\xterm-ctlseqs.txt` and the VT420
manual, fix red-before-green, lower the number in `XtermJsScreenTests.ExpectedFailures`, run the
suite, commit.

**State: DONE as of 2026-08-17.** All 76 fixtures match xterm exactly and `ExpectedFailures` is
empty. 879 rows became 0. Keep it that way: a name appearing in that table again is a regression,
and the runner fails just as loudly on a count that is too high as on one that is too low.

| # | Fixture | Rows | What is known about it |
|---|---|---|---|
| ~~P0.1~~ | ~~`t0084-CBT`~~ | ~~14~~ | **Done 2026-08-17, and it finished the corpus.** It was not a different fault from HT and CHT after all — it was the same rule one step further: **tabulation never cancels a pending wrap, not even when it moves.** The fixture proves both halves by itself, which is why no outside document was needed. Its `at end:` rows send `CSI Z` with the wrap armed and let the `!` wrap onto the next row; its `at end with clipping:` rows put `ESC M ESC D` in between, which *does* cancel the wrap, and the `!` then lands on the exact column CBT had moved back to. That second half is what separates "CBT keeps the flag" from "CBT does nothing at all". Forward tabulation cannot show this, because the flag is only ever armed on the last column and there is no stop beyond it. See `TabulateToColumn` and `TabStopTests`. |
| ~~P0.2~~ | ~~`t0040-REP`~~ | ~~7~~ | **Done 2026-08-17.** REP was not implemented at all. Two rules the fixture settled: a control OR a control sequence ends the run, so a repeat after a cursor move prints nothing; and an explicit `CSI 0 b` still repeats once. Fixing it also emptied `t0100-IRM`, which used REP to build its screen. See `RepeatCharacterTests`. |
| ~~P0.3~~ | ~~`t0014-CAN` / `t0015-SUB`~~ | ~~4 + 4~~ | **Done 2026-08-17**, and it was two defects. Neither control was recognised anywhere, so inside a CSI they ran as ordinary controls and the sequence carried on (`ESC [ CAN D` finished as CUB). The eighth line of each fixture needed a second fix: a ruined CSI now falls into `CsiIgnore` instead of dropping to Ground and printing its own remains. A real xterm shows **nothing** for SUB — the note about a replacement character was wrong. Watch out: `ESC SUB` is a real Tektronix sequence, so the control is delivered before the sequence is abandoned. |
| ~~P0.4~~ | ~~`t0092-alt_screen_DECSC`~~ | ~~4~~ | **Done 2026-08-17.** Each screen owns its own DECSC slot, and there was only one, so the alternate screen's save destroyed the main screen's and both restores landed on the same spot. The slots are exchanged when the buffer really changes — twice for the same switch would be as wrong as never. See `SavedCursorPerScreenTests`. |
| ~~P0.5~~ | ~~`t0100-IRM`~~ | ~~3~~ | **Done 2026-08-17**, as a side effect of REP. |
| ~~P0.6~~ | ~~`t0050-ICH`, `t0051-IL`, `t0052-DL`, `t0055-EL`, `t0079-DECSTBM_VPR`~~ | ~~2 each~~ | **Done 2026-08-17.** Five fixtures, three rules, all of them at the right-hand edge. (1) **ICH and EL disarm the pending wrap** — each fills all 80 columns, edits the last one, then prints; xterm prints on that row, we printed on the next. The `NOTES` guess that t0050 was an xterm bug was wrong. (2) **IL and DL move the cursor to the line home position** — libvterm's script asserts they do not and its author's note says xterm does not either, but the capture of xterm disagrees in all four rows, so `13state_edit` now carries one recorded disagreement. (3) **VPR is not CUD** — `t0075-DECSTBM_CUU_CUD` is the same stream with `CSI 25 B` in place of `CSI 25 e`, and from the same row above the same region xterm answers row 19 for one and row 25 for the other. Applying the rule to CUD instead broke t0075 and t0078, which is what separated the two. See `LastColumnEditTests`. |

**Done means:** `ExpectedFailures` is empty, or every remaining entry carries a written reason that
names the document (or the absence of one) behind it.

**Manual test:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M1** — the TestServer's whole *Standard
Tests* menu on VT100 and ANSI, then `vttest` menus 1 to 3 over a real host.

---

## P1 — Walk each DEC terminal against the real documents — COMPLETE

**Why second.** This is the item that changes the validation table from *inferred* to *documented*,
and it is the one Ronny asked for directly. Three real references are now in the repo or on disk:

- `spec\DEC\VT420-Programmer-Reference-EK-VT420-RM-002.pdf`
- `spec\DEC\xterm-ctlseqs.txt`
- `d:\OCR\ai\EK-VT3XX-TP-001_VT330_VT340_Text_Programming_Mar87\..._combined.md` (Ronny's OCR)

Order within P1, cheapest evidence first:

| # | Terminal | Task | Done means |
|---|---|---|---|
| P1.1 | VT420 | Walk margins, the rectangle family (DECCRA/DECFRA/DECERA/DECSERA), DECRQSS and page memory against the PDF, chapter by chapter. All of it was written before the PDF arrived. **Chapter 12, Reports, is done (2026-08-17)** — it found that `CSI ? Ps n` was being read as a mode RESET, so DECXCPR turned origin mode off and the UDK-lock question hid the cursor; DECRQCRA did not exist; and the rest of the private DSRs answered with silence. All fixed, see `ChecksumAndPrivateReportTests`. **Chapter 6, the page format, is done too** — the column turned out to have an addressing origin the same way the row does ("the starting point for lines *and columns* depends on DECOM"), so CUP addressed columns absolutely under origin mode and both DECSLRM and DECSTBM homed to the wrong corner. See `LeftRightMarginTests`. **Chapter 8 and the DECRQSS list are done too** — chapter 8 was almost right already (ICH and DCH honour the margins, the erases correctly ignore them); its one find was that IL and DL had no effect *below* the scrolling region but did work *above* it. DECRQSS now answers five of table 12-4's fourteen, with a written reason beside each of the nine it will not answer. **P1.1 is complete.** | Each implemented sequence has a test quoting the manual's own wording, as `PanningTests` and `ScrollRegionEdgeCaseTests` already do. |
| P1.2 | VT340 / VT330 | Walk against the OCR. Character sets, soft fonts, status line, page memory, the graphics-adjacent text behaviour. **The status line (chapter 11) is done 2026-08-17** — DECSASD and DECSSDT did not exist and now do, and walking them uncovered a defect with nothing to do with them: `DCS $ q` (DECRQSS) was being swallowed by the Sixel branch, so **every DECRQSS on a VT340 was answered with silence**. The 25th-line geometry and the indicator line are deliberately not built and are written down as such. **Soft fonts (chapter 5) are done too** — Pcss changes what Pcn means and that was ignored, so every character of a 96-character download landed one position to the left; and `TryGetGlyph` turns out to have **no caller in `src\`**, so DECDLD is accepted and stored but never DRAWN. **The character-set designators and every DA reply are done too** — the VT240 turned out to be REFUSING SIXEL IT CAN DRAW, because its DA claimed only ReGIS and the test pinning that said outright that no source confirmed otherwise. The manual's alias-reply table confirms it now. The other DA replies were checked and are honest subsets of the manual. Remaining: page memory, which P1.3 also needs. | Same. |
| P1.3 | VT320 | Page memory is the named gap. The VT420 manual documents it and P1.1 will already have covered it. | DA reply, page memory and DECUDK/DECDLD all backed by a quotation. |
| P1.4 | xterm / xterm-256color | Walk `ctlseqs.txt` end to end for the sequences we claim. Only modes 1004, 1006 and 2004 have been checked so far. **Started 2026-08-17**: 224 sequences extracted (117 CSI, 93 ESC, 14 DCS/OSC). The first pass found the biggest gap yet - **DECSTR (`CSI ! p`) was not implemented at all**, hidden by a parser defect that recorded `!` as a private marker when 0x21 is an intermediate. Every curses program sends DECSTR on the way out. Built from VT420 table 13-1, with four tests pinning what it must NOT do. The CSI list is now walked in full and written up at `docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`, every entry marked implemented / deliberately not / missing. Two more gaps came out of it and are fixed: **DECSCUSR (`CSI Ps SP q`, the cursor shape) did not exist** - every modern editor sends it, and the style enum and renderer were already there - and `CSI Ps ^`, ECMA-48s 5th-edition spelling of SD. The ESC list is walked too, and it found that DESIGNATING a character set was implemented while INVOKING one was not: SS2, SS3, LS2 and LS3 were all missing, so G2 and G3 could be loaded and never used. Those, DECID and S7C1T/S8C1T are added. The DCS and OSC lists are walked too, so **P1.4 is complete**: the whole document is written up at docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md. The last thing it added is **XTSMGRAPHICS**, which a Sixel host asks BEFORE sending an image - without it a terminal that draws Sixel perfectly can be sent nothing at all. That one belongs to P2 as much as to P1. | A written list of every sequence in ctlseqs marked implemented / deliberately not / missing. |
| ~~P1.5~~ | ~~VT52, VT100, VT102, VT220, VT240~~ | **Done 2026-08-17, and it was both.** Their IDENTITY turned out to be documented after all: the VT330/VT340 manual lists the alias DA replies a VT300 sends when identifying as an earlier terminal, and ours match it - VT100 and VT102 exactly, VT220 and VT240 as honest subsets. Pinned in `DeviceAttributesAgainstTheManualTests`. Their BEHAVIOUR rests on the two corpora, which is stronger than "inferred" sounds: 76 of 76 xterm.js screens captured from a real xterm, and 393 libvterm assertions. **The VT52 is the one weak row and is written up as such** - no document, no corpus, and a small surface. **P1 is complete.** | Either a document reference or an honest "inferred" note per terminal. |

**Done means:** the table in `docs\EMULATOR-VALIDATION-STATUS-2026-08-11.md` has no *Inferred* row
left that could have been a *Document* row.

**Manual test:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M3** for the DEC terminals and **§M2** for
xterm. The TestServer now carries a **DEC Terminal Tests** branch (main menu 3) covering reports,
page memory, the rectangle family, margins, soft fonts, NRCS, DECSCL, VT52 and selective erase, and
an **xterm** branch (main menu 4). Run those first; §M2's SSH pass with vim, tmux, htop and less
still matters afterwards, because only a real program can show what a real program needs.

---

## P2 — The graphics oracle: vt340test

**Why here.** RetroTerm has a Sixel decoder, a ReGIS decoder and a Tektronix plotter, and **none of
them is checked against anything a real machine produced** except the three gnuplot Tektronix
streams. vt340test is CC0 — no licence question at all — and its `.six` files come paired with PNGs
photographed from real VT340 and VT240 hardware.

| # | Task |
|---|---|
| P2.1 | ~~Add vt340test's `sixeltests\*.six` to `tools\fetch-conformance-corpora.ps1`~~ **Done 2026-08-17.** The `.six` streams AND the reference `.png` captures are fetched, plus `encoding.md` and `sixelcomments.md`. Gitignored, `-text` in `.gitattributes`, CC0 so there is no licence question. 16 `.six` files. |
| P2.2 | ~~Feed each `.six`, render, write the PNG, then look at them.~~ **Done 2026-08-17, and looking found two real defects — see below.** `tests\RetroTerm.Tests\Avalonia\Vt340SixelCorpusRenderingTests.cs`; PNGs land as `vt340-sixel-NAME.png`. |
| P2.3 | ~~Pin what looks right with spot assertions.~~ **Done.** `map8.six` writes its eight colours as plain numbers in the stream, so the band order can be asserted by pixel without a photograph; the other fifteen assert only that the stream decoded to something. Five decoder-level tests pin the two fixes below. |
| P2.4 | ~~Read the `regis\` directory and write down the gap.~~ **Done 2026-08-17:** `docs\REGIS-GAP-2026-08-17.md`, then **rewritten the same day against the manual itself** once `spec\DEC\EK-VT3XX-GP-002_...pdf` was held. That re-read found a real defect and closed it: **macrographs (`@`) are now implemented.** `@` has no brackets, so the skip-and-count rule could not carry it — a definition's body was drawn the moment it was defined and every invocation drew nothing, both backwards. Storing and replaying is the smaller job, since a macrograph body is made of the S/P/V/C/W commands already implemented. 11 tests; 4 go red with the `@` case removed. The doc also cut `F` (Polygon Fill) down to size: chapter 11 says it re-uses V, C, P and W entirely — and **`F` is now implemented too**, for exactly that reason. One new surface method (an even-odd scanline `FillPolygon`) and no new parsing: the F body runs through the ordinary decoder with a flag set, so V and C collect vertices instead of drawing. 21 tests, and the rendered PNG `regis-polygon-fill.png` draws the manual's own Figures 11-1 and 11-2 so the border alignment can be judged by eye. **ReGIS is now 7 of its 10 commands**; `T`, `L` and `R` are what remain. |

**What looking at the pictures found.** Both were shipped, both were invisible to every assertion
that existed, and both took one glance at a rendered PNG beside its reference:

1. **The raster aspect ratio was read and thrown away.** `"Pan;Pad` is a vertical pixel
   REPLICATION count, not a hint: a VT340 draws each sixel pixel Pan/Pad screen rows tall. The
   corpus proves it twice — `multisize.six` changes the ratio three times inside one image to build
   a flag out of three differently scaled bands, and `extremeratio.six` sends 80:1 with the note
   that a real VT340 renders each sixel 480 screen pixels high. Ours drew both as a twelve-pixel
   sliver along the top edge, and drew every 2:1 image (which is what DEC's own encoders emit) at
   half its height.
2. **Whitespace inside a colour definition's parameters ended the definition.** Sixel data runs
   from `?` to `~`, so a space can never be data and a hand-written stream is entitled to lay its
   numbers out readably — `#1;2; 0;13;28`. Stopping at the space left that register at its
   power-on default, so the navy canton of the flag came out bright blue and the grey stripes came
   out black.

**One fixture looks wrong and is not.** `vaxrgl-lntest.six` renders rotated, and the rotation is in
the file: decoded at its natural size it is 972 by 1548 — a portrait sheet with landscape content
drawn onto it sideways. It is the only fixture with no reference capture, it is more than three
times taller than a VT340 screen, it opens with SSU (the real-world dot size for printing) and a
66-line region, and it declares no raster attributes. It is a printed page. **No rotation setting
was added on the connection**: nothing on the SCREEN rotates, and the two fixtures with hardware
captures — `multisize.six` and `extremeratio.six` — come out in the same orientation as those
captures.

But rotation itself is real, and this is where it belongs. DEC STD 070 §7.8.2.3 defines a **rotated
print mode, `DECGRPM`**, turning the image 90° counter-clockwise so an expanded image fits one 8½ in
page — chosen so the printout could be hole-punched down the left edge for a ring binder. It is a
**print** mode, not a connection setting, and it is designed in
`docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md` rather than invented as a knob.

**Closed 2026-08-17.** `cat-original.six` sets its whole palette through ReGIS
`S(M`_n_`(H`_h_`L`_l_`S`_s_`))` and then sends a Sixel image that defines no colours of its own.
Both halves are done: `GraphicsColorMap` is now the single map both decoders hold — a VT340 has one
— and ReGIS `S(M...)` is implemented. The default map is **table 2-3 from the manual**, which
replaced two private palettes that were both wrong, differently: the Sixel one made entries 9–14
*brighter* than 1–6 where the footnote says they are less saturated, and the ReGIS one used fully
saturated primaries. `docs\REGIS-GAP-2026-08-17.md` is updated.

**Printing, step 1 done 2026-08-17.** Media copy, printer controller mode, autoprint, DECPFF and
DECPEX are implemented, with `IPrintSink` as the printer port and `MemoryPrintSink` as the printer
that is always available. It found two defects of a shape this codebase has been bitten by before:
`CSI ? Ps i` fell into the private-MODE handler, so "turn autoprint on" would have reset reverse
video; and a mode change could not take effect mid-chunk, so `CSI 5 i` followed by a print job in
one packet put the whole job on the screen. `EscapeSequenceParser.ProcessBytes` now returns what it
consumed and a handler can stop it.

**Printing, step 2 done 2026-08-17.** `SixelEncoder` re-encodes the graphics plane as sixel, with
DECGEPM (mode 43) and DECGRPM. The oracle is a round trip — encode, then decode with the decoder
already checked against real VT340 hardware, and compare pixels — plus two rendered PNGs, because
a rotation that went the wrong way preserves every pixel and only a picture can tell. **Tektronix
`ESC ETB` is now implemented too**: it was held back on purpose until there was a sink for all
three hardcopy routes to reach. Mode 47 is still the alternate screen buffer and there is a test
pinning that, and Ronny settled it the same day: xterm's reading wins everywhere.

**Printing is BUILT, 2026-08-17.** `PdfPrintSink` in Desktop turns a print job into a PDF, with
page geometry in real-world units — all four numbers quoted from the manuals (sixel grid 0.5 point
per pixel, text 7.2 points per column at 10 cpi, 12 points per line at 6 lpi, Letter sheet that
grows rather than crops). No new dependency: `SKDocument.CreatePdf` is in both the SkiaSharp
Desktop resolves (2.88.9 via Avalonia) and the one the tests reference (3.119.1), checked by
reflection rather than assumed — the design doc's original claim about the version was wrong.

**Opening the PDF found a defect at once**: the vertical grid was written as 100 centipoints, the
obvious reading of table 5-1, and every circle came out an ellipse. The aspect is already applied
by the decoder, so the page must use square pixels. `print-job.pdf` is kept beside the rendered
PNGs — two pages, text and graphics — because whether a printed page looks RIGHT is a P5 question.

**Manual test:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M6** — `img2sixel` over SSH on a
**three-colour** image. A two-colour image hides a red/blue channel swap, which is how the last one
survived for months.

---

## P3 — Tektronix 4014, from vttest

`vttest`'s `tek4014.c` is the only Tektronix 4014 test material found anywhere (survey entry 4), and
three of its five tests need no input at all. It is X11/BSD licensed.

| # | Task |
|---|---|
| P3.1 | ~~Read `tek4014.c` and hand-write its byte streams as xUnit data.~~ **Superseded 2026-08-17 — do NOT read vttest for this.** The premise ("the only Tektronix 4014 test material found anywhere") stopped being true when the VT330/VT340 Graphics Programming manual arrived in `spec\DEC`: it has a whole **4010/4014 Mode chapter**, every sequence printed beside its character-cell codes. A manual says what a sequence MEANS rather than what one program happens to exercise, and it removes the licence question entirely. Gap written up at `docs\TEKTRONIX-4014-GAP-2026-08-17.md`. |
| P3.2 | Work the gap list in the order that document gives. **`ESC CAN` bypass, all four character sizes and the vector patterns are done** (2026-08-17). Next: `ESC ETB` hard copy **together with** the printing work so all three hardcopy routes reach one sink. What is left is the raster writing modes and the two-margin text wrap, neither of which is 4010/4014 protocol. |
| P3.3 | Render, look, then assert. Same rule as P2. |

**Manual test:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M5** — real `gnuplot` with
`set term tek40xx`. Watch the axis labels (they cascaded diagonally once), the cursor position after
leaving graph mode, and the phosphor: vectors must turn amber with the text.

---

## P4 — Gaps that have no test because they have no code

These are known-missing rather than known-wrong. Each is small on its own.

| # | Gap | Note |
|---|---|---|
| P4.1 | **Media copy / printer** (`CSI i`, `CSI ? i`) | Nothing in `src\` matches media copy at all. `docs\TDV-PRINTER-SUPPORT-DESIGN.md` designs the TDV side and has zero code behind it. |
| P4.2 | Remaining ND `ESC "` graphics modes | 6 of 30 implemented. The other 24 are **counted, not guessed** — `NorskDataGraphicsModule.UnhandledSequences`. Blocked on P5.3. |
| P4.3 | DECDLD 94-character sets | The size parameter is read and not acted on. Symptom would be every soft glyph one position out. |
| ~~P4.4~~ | ~~Left/right margins under origin mode~~ | **The cursor half is done 2026-08-17** — the column has an addressing origin, and CUP, DECOM, DECSLRM and DECSTBM all now use it. `15state_mode.test` still shows 1, but for a different reason, now written into its baseline entry: the corpus runs as **ANSI**, whose profile does not claim `LeftRightMargins`, so `CSI ? 69 h` is refused and there are no margins to home into. Widening the ANSI profile would change all 43 scripts at once, so it is recorded rather than quietly done. |
| P4.5 | Reflow on resize | `63screen_resize` (5) — libvterm re-flows wrapped paragraphs, we truncate. Not a spec violation; DEC terminals could not resize. A decision, not a bug. |

**Manual test:** nothing for P4.1 — media copy has no code to exercise. P4.2 is exercised by
`docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M7**, and P4.5 by **§M8.1** (resize while a full-screen
program runs).

---

## P5 — Needs Ronny, or needs hardware

Nothing in the code can settle these. They are listed in full in
`docs\NEEDS-A-REAL-HOST-2026-08-11.md`; the three that actually block work are repeated here.

| # | Item | Cost to settle |
|---|---|---|
| P5.1 | Bracketed paste in a real shell | one paste, ten seconds |
| P5.2 | Mouse reporting in `vim` over SSH — the largest untested surface, and it contains a decision (Shift forces local selection) | minutes |
| P5.3 | **Point a session at a real ND host and read `UnhandledSequences`** | one session — and it converts P4.2 from guesswork into a list |
| P5.4 | TDV keyboard against real hardware; the two ND model identification bytes | needs a machine that may never appear |

**Manual test:** this phase IS manual. P5.1 is `docs\MANUAL-TEST-PLAN-2026-08-17.md` **§M2.7**, P5.2
is **§M2.2** and **§M2.3**, P5.3 is **§M7**, P5.4 is **§M4 Part B**. The TDV emulators themselves —
already validated against Tandberg documents — have their full by-hand pass in **§M4**, and the
whole-program checks that belong to no single phase are in **§M8**.

---

## P6 — Optional extra oracle

Alacritty's `alacritty.recording` files (Apache-2.0): real captured sessions of vim, tmux+htop,
tmux+git-log, fish and zsh. Use the **recordings only**, with RetroTerm's own snapshot as the
baseline — the same pattern as the libvterm counts. Ignore their `grid.json`; translating
Alacritty's internal cell struct costs more than it buys.

---

## P7 — IBM 3270, last and deliberately

No 3270 manual or data-stream reference is held in this repository. Building one from recollection
would invent EBCDIC orders, AID codes and structured fields — exactly the kind of confident wrongness
this project has been avoiding. **Blocked on a document.** Per Ronny's instruction it waits until
everything above is implemented *and* validated.

---

## Rules that apply to every item above

- **Clean-room only.** xterm, xterm.js, libvterm and vt340test are used as *specifications and
  oracles*. No line of their code is ported into this repository. Their data files are fetched on
  demand and never committed.
- **Red before green.** Break the fix once, watch the new test fail, restore it.
- **Look at the pixels.** Every rendered test writes a PNG. Open it. Three defects that no assertion
  could see were caught this way.
- **Finish with** `dotnet format` → `dotnet build` (0 warnings) → `dotnet test` (quote the number) →
  `dotnet build-server shutdown`, then check for leftover hosts and check their command lines before
  touching any of them.
