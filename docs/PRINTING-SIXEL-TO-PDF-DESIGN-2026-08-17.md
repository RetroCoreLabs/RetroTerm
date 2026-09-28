# Printing: media copy, and sixel to PDF

**Full path:** `docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md`
**Date:** 2026-08-17
**Status:** **BUILT 2026-08-17.** Media copy, printer controller mode, autoprint, DECPFF and DECPEX;
the sixel dump generator with DECGEPM and DECGRPM; `ESC ETB` in Tektronix mode; and the PDF writer,
with page geometry in real-world units. A print job now goes from a host sequence to a PDF file
with the right page size on it.

**What is left is not code.** Whether the printed page LOOKS right is a P5 question for Ronny —
see "How it gets judged" at the end. Also still open: the ReGIS hardcopy command, which waits on
ReGIS `S(H)`, and a real printer port, which was always out of scope. Built in **P4** of
`docs\FINISH-PLAN-2026-08-11.md`.

## What started this

`sixeltests/vaxrgl-lntest.six` in hackerb9's corpus is a 972 by 1548 sixel — a **DEC engineering
drawing sheet**, title block `TOP_DOCUMENT=TEST`, `ENG=E FONTANA`,
`LAST_MODIFIED=Mon 3-Mar-87 16:03:04`. It is a plotter page, not a screen image, and on an 800 by
480 graphics plane only its top-left corner is visible. The question it raised: can RetroTerm take
a print stream and produce a PDF.

Yes — and there are TWO features here, not one. Nothing has to be detected in either.

## How this actually worked, in the 1980s

Source: **DEC STD 070, section 7.8, Graphics Printing** — the Video Systems Reference Manual,
marked "COMPANY CONF - DEC Internal Use Only, 28-Apr-1987", extracted to Markdown by hackerb9 at
`mediacopy/sixel-printer-port.md` in vt340test. Quoted rather than remembered.

**The printer hung off the TERMINAL, not the host.** A VT had a printer port. A graphics print was
started either by the host — the ReGIS hardcopy command, or media copy — or by the user pressing
the Print key under local control. The terminal then re-encoded its OWN graphics bitmap as sixel
and sent it out that port. The documented VT240 screen dump is:

    ST CR DCS 1 q  ...sixel data...  ST

with a guideline that the CR is sent only when the user started it: "If the Sixel dump is initiated
by the host, the CR is not sent so the host can initialize the starting sixel position."

**Printers were classified by sixel LEVEL.**
 - Level 1, the factory default, for older printers: no Set Raster Attribute, no Background Select,
   no Horizontal Grid Size, no Macro Parameter. Aspect fixed at 2:1, horizontal grid about 0.0075
   inch, so 800 pixels take 6 inches. The control string is always the bare 7-bit
   `ESC P 1 q ... ESC \`.
 - Level 2, which a VT340 speaks: all of those supported, and the full form
   `ESC P Ps1;Ps2;Pn3 q " Pn4;Pn5;Pn6;Pn7 ... ESC \`.

**The mode numbers, from the manual itself.** `spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf`,
chapter "Printing Graphics", gives each one with its character-cell codes, so these are read rather
than inferred:
 - **DECGEPM, private mode 43.** `CSI ? 43 h` selects an expanded image of about 300 by 200 mm
   (12 by 8 in). `CSI ? 43 l` selects a compressed one of about 150 by 75 mm (6 by 3 in), which is
   the default.
 - **DECGPCM, private mode 44.** `CSI ? 44 h` sends a colour image to the printer, `CSI ? 44 l`
   black and white. Default black and white, and the manual says only use colour on a VT340.
 - **DECGRPM, private mode 47.** `CSI ? 47 h` selects an image of about 200 by 300 mm (8 by 12 in),
   rotated 90 degrees. `CSI ? 47 l` goes back to compressed. Default compressed.
 - **Graphics to printer**, `CSI ? i` or `CSI ? 0 i`, and **graphics to host**, `CSI ? 2 i`. These
   choose where a ReGIS hardcopy command sends the screen.

**Modes 43 and 44 are implemented; 47 is deliberately NOT.** `CSI ? 43 h/l` and `CSI ? 44 h/l` write
`GraphicsPrintOptions`, and both answer DECRQM. Mode 47 is left alone for the reason below, and the
rotated print is reachable through `GraphicsPrintOptions.Rotated` instead. A test pins that 47 is
still the alternate screen buffer, so changing it has to be a decision rather than someone noticing
the mode was missing.

**DECIDED 2026-08-17 by Ronny: keep the xterm meaning everywhere.** Mode 47 is the Alternate Screen
Buffer on every profile, including the DEC graphics ones, and a host can never reach the rotated
print. Rotated printing stays available from RetroTerm's own settings through
`GraphicsPrintOptions.Rotated`. The reason: modern software sends 1047 and 1049, so the DEC reading
would buy almost nothing, while a mis-read 47 would silently swap a user's screen in the middle of
a session. This is settled — the paragraph below is kept because it explains WHY there was a
question, not because the question is still open.

**A collision that had to be decided, not fixed blind.** DEC's **DECGRPM is private mode 47**, and
xterm's private mode 47 is the **Alternate Screen Buffer** - which is what this emulator implements
today, at `TerminalEmulatorBase.cs:2326`. On a real VT340 `CSI ? 47 h` rotates the next print; here
it swaps to the alternate screen. Both readings are correct for their own terminal, so this is a
per-profile question and not a bug to be corrected in place. In practice modern software sends 1047
and 1049 rather than 47, so the risk is small - but a host driving a VT340's printer would swap our
screen instead of rotating its page, and nothing would report that it had.

**Three print options, and two of them are DEC-private modes.**
 - Compressed: a 1:1 terminal ORs each pair of vertically adjacent pixels into one, so the print
   comes out the same size and aspect as a VT240's.
 - Expanded, mode **DECGEPM**: each sixel sent twice in horizontal succession. Small images fit
   8.5 inch paper, expanded ones need 13 inch in portrait.
 - Rotated, mode **DECGRPM**: rotates the image 90 degrees so an expanded image fits one 8.5 inch
   page. Rotated images are always expanded, and the bitmap is sent as vertical strips six pixels
   wide, top to bottom and right to left.

**Rotation was real, and it was deliberate.** The guideline: "the VT240 rotates the image counter
clockwise, so that the left side of the paper (as it comes out of a typical dot matrix printer)
corresponds to the top of the image on the terminal screen. This scanning order was chosen to allow
punching holes for a looseleaf notebook on the left side of the page as it comes out of the
printer." This is why `vaxrgl-lntest.six` is a landscape drawing on a portrait sheet, and it
corrects an earlier note in this repository that said nothing on a VT340 rotated. Nothing on the
SCREEN rotates; PRINTING rotates, on a mode.

**Colour:** monochrome sixels are a logical OR of every plane. Colour printing sends the colour map
at the head of the dump. With PRINT BACKGROUND selected, pixels with no bits set in any plane are
printed in the colour of index 0.

## The two features

**1. Print out.** The host sends media copy or ReGIS hardcopy, or the user presses Print, and
RetroTerm generates the sixel dump from its own graphics plane — honouring the level, DECGEPM and
DECGRPM — and hands it to a sink. Instead of a serial cable the sink writes a PDF. Nothing is
detected; it is commanded.

**2. Print through.** A host streams a page THROUGH the terminal to the attached printer with
printer controller mode. That is the `vaxrgl-lntest.six` case: a page that was never meant for the
screen. Again nothing is detected — `CSI 5 i` says so.

The way in to both is **media copy**.

## What exists today

**Built 2026-08-17.** `src\RetroTerm.Core\Terminal\Printing\` holds `IPrintSink` (the port),
`MemoryPrintSink` (the printer that is always available) and `PrinterControllerFilter` (the scanner
that watches for `CSI 4 i`). `TerminalEmulatorBase` has `PrintSink`, `PrinterControllerMode`,
`AutoPrintMode`, `PrintFormFeedMode` and `PrintExtentFullScreen`, and answers `CSI ? 15 n` with
"printer ready" when a sink is attached and "no printer" when it is not. 21 tests in
`tests\RetroTerm.Tests\Terminal\Printing\MediaCopyTests.cs`.

**Two defects this found in the existing code, both of the same shape as ones already fixed:**

1. **`CSI ? Ps i` fell into the private-MODE handler**, which reads any final byte other than `h`
   as a reset. So `CSI ? 5 i`, "turn autoprint on", would have RESET PRIVATE MODE 5 — reverse
   video. That is exactly the trap already documented beside `CSI ? Ps n` in the same method, which
   had turned origin mode off and hidden the cursor before it was found. A third final byte was
   sitting in the same hole.

2. **A mode change could not take effect mid-chunk.** `CSI 5 i` means every byte AFTER it goes to
   the printer, but host data arrives in whatever sizes the network hands over, so the sequence and
   the print job land in one chunk. The parser ran to the end of the span regardless, which put the
   whole print job on the screen. `EscapeSequenceParser.ProcessBytes` now returns how many bytes it
   used and a handler can set `StopRequested` to stop it; `ProcessData` loops. Removing that one
   line turns 8 of the 21 tests red.

The DA replies still deliberately omit extension 2, the printer port: that goes in when a print job
can actually produce a page, not when the bytes merely reach a sink.

## The sequences

From `spec\DEC\xterm-ctlseqs.txt` lines 1047 to 1061, quoted rather than remembered:

`CSI Ps i` — Media Copy (MC):
 - `Ps = 0` — print screen (default).
 - `Ps = 4` — turn OFF printer controller mode.
 - `Ps = 5` — turn ON printer controller mode.
 - `Ps = 10`, `11` — xterm's HTML and SVG screen dumps. Not DEC; out of scope.

`CSI ? Ps i` — Media Copy, DEC-specific:
 - `Ps = 1` — print the line containing the cursor.
 - `Ps = 4` — turn OFF autoprint mode.
 - `Ps = 5` — turn ON autoprint mode.
 - `Ps = 10` — print composed display, ignores DECPEX.
 - `Ps = 11` — print all pages.

Two private modes govern what a print means, same document, lines 936 and 1085:
 - `DECPFF`, mode 18 — send a form feed after printing, or not.
 - `DECPEX`, mode 19 — set the print extent to the full screen, or limit it to the scrolling
   region.

**Note the marker.** `CSI 5 i` — no `?` — is printer CONTROLLER mode, the one that matters here:
every byte from then until `CSI 4 i` goes to the printer instead of the screen. `CSI ? 5 i` is
AUTOPRINT, which prints each line as it scrolls off. They are different features and the private
marker is the whole difference, which is the same trap that made `CSI ? 1 ; 1 S` get read as
scroll-up.

## The shape of the work

### 1. A print sink, in Core — **DONE 2026-08-17**

`TerminalEmulatorBase` has printer controller mode: while it is on, **RAW** bytes are routed to an
`IPrintSink` rather than to the parser. Raw, not decoded — that was the one correction the building
made to this design. In printer controller mode the terminal is a wire: bytes are not characters,
escape sequences are not executed and the C1 controls mean nothing, because a sixel passing through
must reach the printer byte for byte or the picture is corrupt. Only `CSI 4 i` is looked for, and
the filter matches it across chunk boundaries so a terminator split one byte per read still works.

The sink is an interface so that Core, which is netstandard2.1 and knows nothing about drawing
libraries, does not grow a PDF dependency. It receives:
 - bytes, for a text or pass-through print job — **done**,
 - a page break, from a form feed or from `DECPFF` — **done**,
 - a completed graphics surface per page, for a sixel or ReGIS print job — **not yet**. Deliberately
   left off the interface until something calls it, rather than shipping a method nothing invokes.

With **no** sink attached the print commands are still obeyed and the job is swallowed. That is what
a terminal with an empty printer port did, and it is the better failure: putting the job on the
screen instead means the host believes it printed a page while the user watches garbage arrive.

### 1a. The sixel dump generator — **DONE 2026-08-17**

`SixelEncoder` re-encodes a surface as sixel — the other direction from `SixelDecoder`, and a
separate class because the two share no code and no state: one walks a byte stream and paints, the
other walks pixels and writes bytes. What they share is the FORMAT, so every rule in the encoder is
quoted from chapter 5 of the Level 2 Sixel Programming Reference, which settled several things:

 - **Ps1 is a MACRO parameter**, selecting horizontal grid size and pixel aspect ratio together.
   Table 5-1: 0 and 1 both mean aspect 200:100, the 2:1 a level 1 printer is fixed at — which is
   exactly the `ESC P 1 q` DEC STD 070 gives for the VT240 screen dump.
 - **Ps2 "selects a background color. The device ignores this parameter."** So the background-select
   worry was misplaced: on a printer it means nothing at all.
 - **Bit 0 is the TOP pixel and bit 5 the bottom** (§5.5.1). Getting that backwards flips every band
   and the picture comes out striped.
 - The control codes (table 5-3) are `"` DECGRA, `!` DECGRI, `$` DECGCR, `-` DECGNL, `#` DECGCI, and
   "monochrome devices ignore DECGCI", so a black-and-white dump selects no colour at all.

**Rotation happens on the way IN.** A rotated print reads the same encoder through a transposed
accessor rather than through a second emit path — one encoder, one set of run-length rules, one
place for a bug to live. Counter-clockwise sends source `(sx, sy)` to page `(sy, W-1-sx)`.

**The oracle is a round trip.** Nothing outside this repository can say whether the bytes we emit
are right, so the test encodes a picture and decodes it with the decoder that IS checked against
sixteen streams from real VT340 hardware, then compares pixels.

**And then look at it, because the round trip cannot see orientation.** A rotation that went the
wrong way preserves every pixel. `print-sixel-upright.png` and `print-sixel-rotated.png` draw a red
bar along the top edge and a blue bar down the right; after rotation the red runs down the LEFT of
the page and the blue ACROSS ITS TOP. Looking also caught a mistake in the test itself: the first
version drew in the top-LEFT and the rotated page came out blank, because a counter-clockwise turn
sends the source's left edge to the bottom of the page and off a screen-sized plane entirely.

**The disc comes out as an ellipse and that is correct** — "rotated images are always expanded", so
every column prints twice. It looks like a defect in the PNG. There is an assertion pinning it so
that anyone who "fixes" it finds out at once.

### 2. No second decoder

A sixel arriving in printer mode is decoded by **the same `SixelDecoder`** onto **the same
`InMemoryGraphicsSurface`** as one arriving on screen. The only difference is the surface's size —
a page rather than the 800 by 480 plane — and where it goes afterwards. Writing a separate
"printer sixel decoder" would be the duplication trap this repository has already been bitten by
twice; the fix would then land on one path and not the other.

That means one change in the decoder's neighbours, not in the decoder: the surface must be
allocated from the image's own declared size instead of always being the screen plane.

### 3. Page size in real-world units, in Core

This is what makes PDF the right target rather than PNG. vt340test's `physicalsixels.md` documents
the mechanism:
 - the default unit in sixels is the **decipoint**, 1/720 inch,
 - `SSU`, Select Size Unit, chooses the unit,
 - `Pn3`, the third parameter of the DCS protocol selector, sets the horizontal grid size in that
   unit,
 - the vertical grid is the horizontal grid times the aspect ratio, which comes from the raster
   attributes `Pan/Pad` — the same numbers this repository started honouring on 2026-08-17.

So a page has a true size in inches and the dots have a true spacing, and both are computable
rather than guessed.

**Settled 2026-08-17, from the bytes and from ECMA-48.** The fixture's opening sequence is
`1B 5B 37 20 49` — `CSI 7 SP I`, **with the 02/00 intermediate**. That matters: without the
intermediate, `CSI Ps I` is CHT, cursor forward tabulation, and the sequence would mean something
else entirely. With it, ECMA-48 5th edition §8.3.139 gives `CSI Ps 02/00 04/09` as **SSU, Select
Size Unit**, and its parameter table gives:

    0  CHARACTER (device-dependent)      5  BASIC MEASURING UNIT, 1/1200 in
    1  MILLIMETRE                        6  MICROMETRE
    2  COMPUTER DECIPOINT, 1/720 in      7  PIXEL
    3  DECIDIDOT                         8  DECIPOINT
    4  MIL, 1/1000 in

So **`CSI 7 SP I` selects PIXEL** — the file addresses in device dots, which is what a plotter page
dumped at native resolution would do. It follows that this file does NOT declare a physical size
through SSU; the size comes from the sixel grid (`Pn3`) and the device.

**Still not identified:** the fixture's second sequence, `CSI ? 20 SP J`. It is not in the Level 2
Sixel Programming Reference and it carries a DEC private marker, so it is not ECMA-48 either. I do
not know what it does. Recorded rather than guessed at.

Also worth knowing: the Level 2 manual says "Level 2 devices support only the character cell model.
Point addressing is not supported", so the decipoint machinery below belongs to the level 3
printers, not to the level 2 ones a VT340 talks to.

**The reading is now held.** Fetched 2026-08-17 into `spec\DEC\` — both are DEC's own manuals, from
hackerb9's collection:
 - `EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf` — "Digital ANSI-Compliant Printing
   Protocol", 234 pages. The printer-side sixel authority, and where the SSU table should be.
 - `EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf` — "VT330/VT340 Programmer Reference
   Manual; Volume 2: Graphics Programming, 2nd Edition", 305 pages. The ReGIS and sixel authority.
   `docs\REGIS-GAP-2026-08-17.md` had to take its ReGIS command table second-hand from
   `regis/regis-commands.txt` for want of this; that gap is now closed and the ReGIS work should be
   re-read against it.

### 4. PDF, in Desktop — **DONE 2026-08-17**

`src\RetroTerm.Desktop\Printing\PdfPrintSink.cs`. `SKDocument.CreatePdf` needs **no new
dependency** — but the version note in the first draft of this document was wrong and worth
correcting: it said "verified present in SkiaSharp 3.119.1", which is the version the TEST project
references. Desktop resolves **2.88.9** transitively through Avalonia 11.3. Both were checked by
reflection over the actual assemblies on disk, and `CreatePdf`,
`DrawText(string, float, float, SKFont, SKPaint)` and `DrawBitmap(SKBitmap, float, float, SKPaint)`
are in both. That matters because both assemblies really do get loaded in the test run, so an
overload present in only one would compile and then throw.

**The bitmap is embedded at its native pixel size under a scale transform**, never resized into a
smaller bitmap first. No resampling adds information; it only turns crisp dots into grey smears.
The printer's own resolution does the final sharpening.

**Page geometry, all four numbers quoted rather than chosen:**
 - Sixel grid, table 5-1 at Ps1 = 0: 50 centipoints per pixel = **0.5 point**.
 - Text pitch, DECSHORP Ps = 0: "720 centipoints, 10 characters/inch" = **7.2 points**.
 - Line pitch, DECVERP Ps = 0: "1200 centipoints, 6 lines/inch" = **12 points**.
 - The sheet is Letter and **grows** rather than cropping, because a plotter page is bigger than
   Letter and losing its edges is worse than an unusual page size.

**A defect found by opening the PDF.** The vertical grid was first written as 100 centipoints — the
obvious reading of table 5-1, where the vertical grid is the horizontal times the aspect ratio. The
first page it produced turned a circle into an ellipse and a square into a rectangle. Section
5.4.1.3 is the answer: the aspect "may be defined by Ps1 **or the Set Raster Attributes sixel
control code**", and DECGRA wins — and `SixelDecoder` already honours it by painting Pan/Pad rows
per bit, so the aspect is baked into the bitmap before the PDF writer ever sees it. Applying it
again squares the distortion. The page now uses **square pixels** and the decoder owns the aspect,
which is also the only way the two halves cannot drift apart. `APictureKeepsItsShapeOnThePage`
pins it.

**The artefact to look at:** `tests\RetroTerm.Tests\Avalonia\images\rendered\print-job.pdf`, kept
rather than deleted for the same reason the screenshots are kept. Two pages — a screen print, so
the text path and its column alignment can be judged, and a graphics hard copy.

### 5. Both command surfaces, declared once

Starting and stopping a print job, and choosing where the PDF goes, are commands. They must be
declared on the command metadata the way `CommandParameter.DecodeEscapes` is, so the script DSL and
`RetroTermToolProvider` both inherit the behaviour. `SEND` swallowing a carriage return was fixed
in the script parser and shipped broken over MCP for weeks; the equivalence test at
`tests\RetroTerm.Tests\Commands\CommandSurfaceEquivalenceTests.cs` exists to stop it happening
again and will catch this one too.

### 6. What the DA reply must not say

Extension 2, the printer port, goes into the DA reply **only once printing actually works** — the
rule this emulator already follows everywhere else. A DA reply is a promise, and an unkept one
sends a host down a path the terminal cannot follow.

## How it gets judged

Assertable here, and **the first four are now asserted**:
 - printer controller mode routes bytes away from the screen buffer and back again — **done**,
 - a sixel passes through byte for byte and none of it reaches the screen — **done**,
 - the exit sequence is found even when split one byte per chunk — **done**,
 - autoprint prints each line as it is finished while the screen keeps working — **done**,
 - a page break produces a new page,
 - the sixel that arrives in printer mode decodes to the same pixels as the same sixel on screen,
 - the PDF has the expected page count and page size in points — **done**,
 - a picture keeps its SHAPE on the page — **done**, and it caught a real defect,
 - a print out with DECGRPM on comes out rotated 90 degrees counter-clockwise from the same print
   with it off, and DECGEPM doubles the width. Both are pixel comparisons against the unrotated
   render, so neither needs a photograph,
 - a Level 1 dump carries no raster attributes and a Level 2 dump does. That is a byte-level check
   on what we EMIT, which is the half of printing that has no oracle otherwise.

**Not** assertable here, and therefore a P5 question for Ronny: **whether the printed page looks
right.** Everything above says the numbers are what the manuals give; none of it says the page is
nice to hold. Two things to open:

 - `tests\RetroTerm.Tests\Avalonia\images\rendered\print-job.pdf` — two pages, one text and one
   graphics, produced by the real path and kept on purpose.
 - `vaxrgl-lntest.six` streamed through printer controller mode, which is the case that started
   all this: a real DEC engineering drawing sheet, 972 by 1548, that was never meant for a screen.

Print them. Whether the dots are crisp, whether the page is the right size in the hand, and whether
the text pitch reads well are all things only a person and a printer can answer.

## What is deliberately out of scope

 - xterm's `CSI 10 i` and `CSI 11 i` HTML and SVG dumps. Not DEC, and not what this is for.
 - A real printer port. The sink writes files; talking to a physical printer is a separate job.
 - ReGIS printing. It falls out of the same sink once ReGIS itself is further along — see
   `docs\REGIS-GAP-2026-08-17.md`.
