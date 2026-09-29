# M6 — Sixel and ReGIS graphics

**Full path:** `docs\manual-tests\M6-SIXEL-AND-REGIS.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Machine cover:** `tests\RetroTerm.Tests\Avalonia\ManualPlan\M6SixelAndRegisTests.cs`

Graphics is the area where **looking is the test**. Three defects shipped here that every assertion
in the suite was blind to — a thrown-away aspect ratio, red and blue swapped in every image, and
plots drawn in the wrong phosphor — and each took one glance at a picture.

So most of this section is: open a file, look at it, answer a question. The files are already
written by the suite; you do not need a host for M6.1, M6.4 or M6.5.

---

## Before you start

Run the suite once, so every artefact below is current:

    dotnet test src\RetroTerm.slnx

Everything lands in `tests\RetroTerm.Tests\Avalonia\images\rendered\`.

**The corpus must be fetched** or the comparison cases have nothing to compare against:

    powershell -File tools\fetch-conformance-corpora.ps1

It is CC0 and deliberately **not** committed. With it absent every test here still passes and simply
produces nothing — so an empty images folder means the fetch, not the decoder.

---

## M6.1 — Our Sixel against real VT340 hardware

hackerb9's vt340test is the only oracle in this program that was **photographed off real glass**.
Eleven of the sixteen streams come with a capture of what a real VT340 or VT240 made of them.

### M6.1a — The eleven pairs, side by side

**Needs:** nothing but the fetched corpus and one test run.
**Do:** open every
`tests\RetroTerm.Tests\Avalonia\images\rendered\compare-*.png`.

Each sheet is one fixture, stacked:

    top    — what RetroTerm drew
    bottom — what the real hardware drew

**Pass, per sheet:** the same picture, in the same colours, the same way up, at the same proportions.

**What is NOT a failure, and will look like one:**
- The bottom half is a **photograph of a curved screen**. It is softer, the geometry bends at the
  corners, and the colour temperature is warm. Judge the shapes and the colour *relationships*, not
  the sharpness.
- Sizes differ. Ours is drawn at the screen size of an 80x24 window; the captures were taken at
  whatever the camera or grabber gave.

**Machine cover:** `M6_1a_EveryHardwarePairIsBuiltIntoOneSheetToJudge` builds the sheets and checks
both halves carry ink, so a fixture that silently decoded to nothing cannot present a blank top half
as a pass. It cannot judge whether the two halves are the same picture — that is the case.
**Judge:** eye.
**Result:** **PASS — all fifteen sheets judged and approved by Ronny, 1 September 2026.** Two real
findings came out of that sitting, and they were different things:

 - **`extremeratio` was missing the line of text along the top. REAL, and fixed.** The emulator was
   never at fault - the file ends with plain text after its Sixel terminator, the image leaves the
   cursor at row 0 column 0 exactly as the fixture's own note says a VT340 does, and the text landed
   in row 0 of the buffer. It went missing in the RENDERER, which blitted the text and then painted
   the graphics plane over it unconditionally, whatever arrived last. A real VT340 has one picture
   memory for both, so the later write wins per pixel - which `DrawSixelImage`'s own comment already
   predicted. Cells now carry `TextIsNewerThanGraphics` and the plane is punched transparent over
   them. Pinned by `tests\RetroTerm.Tests\Terminal\Graphics\TextAfterASixelImageTests.cs`.
   **The two halves still read different words** - the capture says "This text should be at the top
   line of the screen." while the fixture sends "* left-arrow A VT340 leaves the text cursor at the
   top of the screen." Judge placement and the unbroken black bar, not the wording.
 - **`registest-bitplane` appeared to show no text. NOT a defect.** That fixture is 411 bytes of
   circles and colour-map writes and contains no text command at all, so there is nothing for us to
   draw. Its reference is a photograph of hackerb9's WHOLE SCREEN - emacs holding `registest.sh`, a
   modeline, a shell prompt, job-control output - with the circles drawn over it. The sheet now
   carries that warning in its own hardware label so it cannot mislead the next reader.

**What the fix also improved, found by measuring every fixture rather than the one being fixed:**
`textcursor` exists precisely to ask where text overlaps an image - its captions read "Overlaps a
little", "Always overlaps", "Overlaps badly" - and it had been hiding its own "overlap?" labels
underneath the squares. They now appear over them exactly as its hardware capture shows.

**The sheets, on one page:** `the "Phosphor Proof" artifact (DELETED 2 September 2026 after judging; regenerate from the compare-*.png sheets if it is needed again)`

**Superseded detail from the earlier partial pass, kept because the withdrawal is the useful part:**
**background defect FIXED. The "colour skew" underneath it WAS NOT REAL and is withdrawn (27 Aug 2026).** It was a capture artefact: the sheets compared our 616x393 render against an 800x480 photograph, and a black coat beside a white stripe, resampled, reads as red on green. Captured at the hardware's own 10x20 cell our half went from 1,940 distinct colours to 15 against the photograph's 21, and cat-original and cat-vt340 now agree with the hardware - hat and coat black in both. Remaining, measured and NOT claimed as defects: background green (36,143,107) vs (36,138,102), white 252 vs 255, and cat-vt240 differing only where that fixture leaves registers at the machine default while we render it with VT340 defaults. Still yours: the eleven sheets by eye.  **Date:** 21 Aug 2026, revised 2026-08-27  **By:** Ronny + measured

**`cat-original.png` had a black background where the hardware has dark teal-green.** Measured, not
eyeballed — the ink in each half counted by colour:

```
                 before        after      reference
cat-original   green=487    green=234722   green=357302
```

Ours is the smaller picture, so the fraction is what matters: **97% green against the reference's
94.5%.** The other fourteen sheets are unchanged to the pixel.

**Fixed.** A ReGIS write to output map location 0 now sets the page background —
`RegisDecoder.BackgroundMapLocationChanged`, turned into `ImageTextBackground` in `DrawRegis`.
Pinned by `tests\RetroTerm.Tests\Terminal\Graphics\RegisPageBackgroundTests.cs`.

**What is different about that one fixture.** `cat-original.six` opens with four ReGIS commands —
`S(M0(H280L35S60))`, `S(M1(H0L0S0))`, `S(M2(H120L50S100))`, `S(M3(H0L99S0))` — and then sends a Sixel
image that defines no colours of its own. Registers 2 and 3 reach our render (red and white are both
present); register 0 does not, so everywhere the hardware paints dark teal-green we paint black. The
cat's face is drawn in register 0 too, not just the background.

**The standing hypothesis, and it fits all four:** a ReGIS `S(M0(...))` sets the graphics page
BACKGROUND; a Sixel `#0` definition only defines a drawing colour and does not. All four fixtures set
register 0 to the same teal-green, but only `cat-original` does it through ReGIS — and only
`cat-original` has a reference background that follows register 0.

**A wrong turn worth not repeating.** The first diagnosis was the Sixel introducer's P2 parameter,
which we do not read at all. It looked strong: `cat-original` introduces with a bare `ESC P q` and
`cat-vt340` with `ESC P 0;1 q`. Filling unpainted pixels from register 0 raised `cat-original` from
487 green pixels to 193,793 — and turned `cat-vt240` green where the hardware is black, because that
file is *also* `P2` of 0 with a green register 0. **Measuring every fixture instead of the one being
fixed is what caught it**, and the change was backed out whole. P2 may still be worth reading one
day; it is not the cause of this.

**A "the reds should be blacks" finding was raised here and then WITHDRAWN.** It is written up
because the withdrawal is the useful part.

With the green background in place, our cat's hat and coat looked red where the hardware's looked
black. Three explanations were tested and all three were wrong:

 - **The HLS conversion.** `S(M1(H0L0S0))` is black — `FromHls` takes the grey path at saturation 0
   and returns `0,0,0` for lightness 0. Read, not assumed.
 - **A ReGIS-only fault.** `cat-vt340` takes its colours from the Sixel stream and showed the same
   apparent skew, so it could not be about ReGIS.
 - **Register selection.** Pinned by `SixelRegisterSelectionTests` — selecting a register defined
   black draws black, by the Sixel route AND by the ReGIS route. All three pass.

**Then the measurement refused to support the claim.** Sampling the hat region in each half found
726 red pixels inside the reference's supposedly black hat, and our cat renders at a different size
and position, so the two rectangles are not the same part of the picture. Across the whole half our
red-to-black proportion does run higher (31/37 against the reference's 21/44) — but at a different
scale, which changes thin-line ink ratios on its own.

**So there may be nothing here.** It came from comparing two differently-scaled pictures by eye. If
it is real it needs a comparison at matched scale, which is a change to how the sheets are built,
not a change to the decoder. Do that before believing it again.

### M6.1b — The four cats must agree with each other

**Needs:** the same sheets.
**Do:** open `compare-cat-original.png`, `compare-cat-vt340.png`, `compare-cat-vt240.png` and
`compare-cat-libsixel.png` together.
**Pass:** four encodings of **the same photograph**, so the four top halves should show the same cat
in the same colours. Differences between the four are the point of the fixture set — one uses a
ReGIS-loaded palette, one is what a VT240 produced, one is libsixel's output — but a cat that is
recognisably a *different colour* in one of them is a defect in that encoding path.
**Why this case exists:** `cat-original.six` sets its whole palette through ReGIS and then sends a
Sixel image that defines no colours of its own. That only works because a VT340 has ONE colour map
that both languages write to, which is now how it is built. Before that it came out in the wrong
colours and nothing said so.
**Machine cover:** `M6_1b_TheCatsFourColoursConvertAsTheHardwareDrewThem` pins the colour maths (see
below). Four pictures being the same picture is not a thing an assertion can state without a
reference to compare against, and the references are photographs.
**Judge:** eye.
**Result:** **OPEN — three of the four agree; cat-vt240 is measured and explained below**  **Date:** 26 Aug 2026  **By:** Claude

#### 26 August 2026 — cat-vt240's register 1, root-caused as far as it goes

**Three of the four cats match the hardware. The fourth does not, and it is the only colour
difference anywhere in the set.** Measured rather than eyeballed, because the last finding here was
withdrawn for being an eyeball comparison of two differently-scaled pictures:

```
                 ours          hardware capture
register 1     51,51,204        36,138,102
```

**Our blue is not a setting, and not a connection.** That was the first question asked and the
answer is no: 51,51,204 is `20,20,80` percent, entry 1 of DEC's Table 2-3 default colour map,
transcribed in `GraphicsColorMap.DefaultMapPercent` from
`spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf`. It is the power-on colour,
showing because the fixture never successfully redefines register 1.

**Why the fixture fails to redefine it, from the bytes.** `cat-vt240.six` opens with four colour
definitions, and the second one is malformed:

```
#0;1;280;35;60   #1;10;0;0   #2;1;120;50;100   #3;1;0;99;0
```

Four numbers where the others have five, and its coordinate-system selector is **10**. The Level 2
Sixel Programming Reference's table of Pu values ends: **"Other — Ignore sequence."** So DEC's own
document says to do exactly what we do.

**What it was meant to say.** `cat-original.six` sends the same palette through ReGIS as
`S(M1(H0L0S0))`, which is black — and black is what the other three cats draw in both halves. So the
intended colour is black, and **neither we nor the photograph produce it**: we give blue, the
hardware gives register 0's green.

**The hardware's green is NOT explained.** Register 1 ends up holding exactly the colour register 0
was given one definition earlier. No mechanism read out of either manual produces that. It is not
copied, and no theory is claimed. One piece of outside evidence bears on it: hackerb9's own
`sixelcomments.md` records that the ReGIS-palette version of this picture exists because *"early
VT240 firmware did not handle sixel color palettes correctly"* — so the capture may be a photograph
of that firmware being wrong. **Unproven.**

**What the investigation did turn up, and it is a real defect.** Chasing this found that we treated
**any** introducer with fewer than five numbers as a selection. Both DEC manuals say a short
definition is still a definition with the missing coordinates taken as zero. A host sending the
perfectly legal `#2;1;120;50` had its register left at the power-on colour and its picture drawn in
the wrong one, silently. Fixed 26 August; pinned by
`tests\RetroTerm.Tests\Terminal\Graphics\SixelShortColourIntroducerTests.cs`. Every Sixel stream in
the corpus was scanned first: there is exactly one short introducer in the whole set and it is the
malformed one above, so no fixture moved — verified by histogramming all eleven `plane-*.png` before
and after.

**A second suspicion was raised here and WITHDRAWN the same hour — and the withdrawal was itself
wrong. REOPENED 27 August 2026.** Our `extremeratio` render puts the text after the image near the
BOTTOM where the hardware capture has it at the top. It was withdrawn on two grounds and neither
holds:

 - "`TerminalEmulatorBase` already implements both halves of DECSDM and quotes the manual." True,
   and irrelevant. DECSDM governs whether sixel output SCROLLS; it says nothing about where the
   text cursor is left when the string ends, and this fixture never sets it.
 - "the capture contains different text from the one the `.six` file prints, so the two were never
   the same picture." The TEXT differs — the capture was taken from an earlier revision of the
   fixture — but BOTH strings assert the same thing, so the position claim was always comparable.
   Dismissing a difference because a label beside it changed is not a measurement.

What settles the comparison is that the fixture makes the claim ITSELF, needing no capture at all.
`extremeratio.six` ends with, after the ST:

    * <- A VT340 leaves the text cursor at the top of the screen.

The `*` marks the cursor. So the fixture author states the expected position in the file, and we
put it at the bottom left. That is now clean evidence rather than a resampling artefact, because
both halves of the sheet are rendered at 800x480 with nothing scaled.

**SETTLED 27 August 2026, from the manual this repository already quotes. IT IS A REAL DEFECT.**

`TerminalEmulatorBase` carries the rule in its own DECSDM remarks, from Graphics Programming
chapter 14, for scrolling ENABLED — which is the default and what this fixture uses:

> "the sixel active position begins at the upper-left corner of the ANSI text active position"
> ... and "when sixel mode is exited, **the text cursor is set to the current sixel cursor
> position**".

The text cursor must therefore be put WHERE THE SIXEL CURSOR ENDED UP. What the code does instead
is count the image's pixel height and line-feed that many text rows:

    int rows = (_sixelDecoder.Height + GraphicsCellHeight - 1) / GraphicsCellHeight;
    for (int i = 0; i < rows; i++) HandleLineFeed();

Those two rules agree for an ordinary image and disagree completely here, because the sixel cursor
only moves DOWN on `-`, the graphics new line — never because pixels were drawn.

**The measurement that settles it.** `extremeratio.six` contains **zero `-` characters** and two
`$` (graphics carriage return, which returns to the left margin on the SAME band). So all three
colour passes paint one single band and the sixel cursor never advances a row at all. It is still
at the top when the string ends, so a real VT340 leaves the text cursor at the top — which is what
the fixture says in its own last line and what the hardware capture shows. We line-feed 480/20 = 24
rows and land at the bottom. For contrast, `map8.six` has two `-` and no `$`.

**Why it hid for so long.** The two rules coincide whenever a band advance accompanies each six
pixels drawn, which is every fixture at 1:1 aspect. This one sets `"80;1;800;480` — Pan 80, so one
band is 480 screen pixels — precisely to pull them apart. That is what the fixture is FOR.

**FIXED 28 August 2026**, on Ronny's decision, in commit `fe13d2c`. `SixelDecoder` now reports
`SixelCursorTop`, moved only by `-`, and the emulator sets the text cursor from it. It sits behind
`TerminalFeatures.SixelExitCursorFollowsTheSixelCursor`, carried by the two profiles that do Sixel:
VT340, where the behaviour is measured, and VT240, where it is derived from the same DEC
specification and labelled as derived.

Three existing tests had pinned the old behaviour, all three written from our code rather than from
hardware, and all three now assert the manual's rule. Red before green: restoring the old line
drives `extremeratio` from row 0 to row 23 again.

**A consequence worth knowing.** An image that does NOT end with a `-` now leaves the text cursor
on its last band rather than below it, so the next text printed overlaps the picture. That is what
the hardware does. It also means two small images land on top of each other unless the first steps
the sixel cursor past a whole text cell - 20 plane pixels on the VT340's own geometry.

#### What building these sheets already showed — one confirmation and one open question

**Confirmed, and it is a good one.** `cat-vt340.six` defines exactly four colours, all in HLS, and
the last of them is the green it puts behind everything: `#0;1;280;35;60`. The background pixels of
the real VT340 capture measure **36,138,102**. Our conversion answers **36,143,107** — within five
counts per channel of a photograph of a screen. That is the first time the HLS conversion has been
checked against real hardware rather than against the manual's own table, and it settles the part
that is easiest to get wrong: DEC's hue is offset so 0° is BLUE, and getting that wrong rotates
every colour by a third of the wheel — a plausible picture in the wrong colours, which looks like
art rather than like a fault. Pinned by the test named above.

**Settled, and one of the two halves was my own mistake.** This document first recorded an open
question here: the hardware appeared to draw the cat's hat and gown **black** where we drew them
**red**. That was wrong, and the way it was wrong is worth keeping:

- The comparison sheet shows the SCREEN — the plane stretched over the text area — while the
  capture is close to the plane's own size, and the capture's cat sits lower because the image
  includes two shell prompt lines. So the two were sampled at different content, and a whole-image
  count of "reddish pixels" is dominated by the hardware's green background.
- Magnified, both hats are the same thing: a **one-pixel dither of red and black columns**, which
  reads as dark red at native size and as red at any scale that blends it.

The lesson is the Tektronix one again — **when the thing being judged is smaller than the scaling,
scale nothing**. So `plane-*.png` is now written beside each sheet: the graphics plane at one image
pixel per plane pixel, which is what can be compared with a capture pixel for pixel.

**The one real difference was the background, and it is now implemented.** The hardware's whole
screen is green and ours was not. The fixture's own comment says why: a VT340 takes its text
foreground from the **sixth colour defined** and its text background from the **sixteenth**,
whatever register each was given — "the vt340's peculiar ordering scheme". `cat-vt340.six` defines
exactly sixteen colours in an order unrelated to their registers, and the sixteenth is the green.

That is now a profile feature, `ImageColoursSetTextColours`, claimed by the VT340 and by nothing
else — xterm and libsixel draw Sixel and do not do this. Pinned by
`M6_1f_TheCatSetsTheScreenBackgroundTheWayTheHardwareShows`, which checks the colour that arrives
against the **36,138,102 measured off the hardware capture**, with two guards: an image defining
three colours changes nothing, and an xterm fed the same file keeps its own colours.

**What is still worth your eye:** at screen scale our hat reads redder than the hardware's, because
the dither's red and black columns blend differently at the two scales. The pixels match; the
impression does not. Compare `plane-cat-vt340.png` with the capture rather than the sheet if you
want to judge that properly.

### M6.1c — The aspect ratio fixtures

**Needs:** the same sheets.
**Do:** open `compare-multisize.png` and `compare-extremeratio.png`.
**Pass:**
- `multisize` is a **flag built from three bands at three different scales**, changing the ratio
  three times inside one image. All three bands must be there, and the flag must fill the picture.
- `extremeratio` sends 80:1 — a real VT340 draws each sixel **480 screen pixels high**. If it comes
  out as a thin sliver along the top edge, the raster attributes are being read and thrown away,
  which is exactly the defect these two fixtures caught.
**Machine cover:** the decoder-level tests in `SixelRenderingTests` pin the replication maths. They
cannot tell you the flag looks like a flag.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.1d — The eight map colours

**Machine, in full.** `Vt340SixelCorpusRenderingTests.TheEightMapColoursComeOutInTheRightOrder`
samples the bands of `map8.six` by pixel and pins red, green and **cyan** — cyan being the one that
catches red and blue swapped, the defect that survived months because the only thing drawing was a
single green.

Nothing for a person to do here. It is listed so nobody spends eyes on it.

---

## M6.2 — Sixel and text on one screen

**Needs:** the TestServer — main menu **5**, Graphics.
**Do:** run the graphics menu on a **VT340** connection and watch each item.
**Pass:**
1. Three-colour bars: red on the left, blue on the right. A swap here is the channel order.
2. Grey ramp with a square: the square is **square**, not an ellipse or a rectangle.
3. Sixel mixed with text: neither erases the other, and text after the image starts **below** it.
4. A tall image scrolls with the text rather than staying pinned.
**Machine cover:** `SixelRenderingTests`, `TestServerNewSuiteTests` (decodes the three-bar image and
asserts red left, blue right), and the rendered `sixel-with-text.png`. What no test covers is the
scrolling interaction, because scrollback rendering is not reachable from the capture helper.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M6.3 — A real image from a real host

**Needs:** a host with `img2sixel` (libsixel) or `lsix`. **Not verified to be installed anywhere —
assumption.**
**Do:** `img2sixel photo.png` over SSH, on a **three-colour or full-colour** image.
**Pass:** the photograph looks like the photograph.
**Use three colours at least.** A two-colour image hides a red/blue swap, which is how the last one
survived for months: the only colour drawing was `(0,255,136)`, which comes out `(136,255,0)` when
the channels swap, and both look green.
**Machine cover:** none — this is a real encoder we do not control, which is the point of running it.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M6.4 — ReGIS

**All ten of ReGIS's commands are implemented**: S (screen), W (write controls), P (position),
V (vector), C (curve), F (polygon fill), `@` (macrographs), T (text) and L (load) since 2026-08-18,
and **R (report)**, which answers instead of drawing. R is the one command with no picture to look
at, so its case (M6.4g) needs a live host that asks; note there that `R(E)` always answers `"0,0"`
because parse errors are not tracked.

Within T, three options are deliberately not built and are counted rather than pretended: PV spacing
(subscripts and superscripts), temporary text control, and temporary write control.

### M6.4a — The drawings the suite produces

**Needs:** one test run.
**Do:** open, in this order:

| File | What it is | Pass |
|---|---|---|
| `regis-box-diagonal-circle.png` | The three primitives | A closed box, a straight diagonal, a round circle — **round**, not an ellipse |
| `regis-polygon-fill.png` | The manual's own Figures 11-1 and 11-2 | The fill reaches the border exactly: no white seam inside the outline, no colour leaking past it |
| `regis-three-colours.png` | Three colour registers | Three distinct colours, and red is red |
| `regis-single-phosphor.png` | The same drawing on a one-gun screen | One colour at different brightnesses, never a hue |
| `regis-phosphor-intensity.png` | Intensity mapping | Brighter where the colour was brighter |
| `regis-text-sample.png` | **The text command** — see M6.4c below | Legible text at five sizes, italics that slant, tilted strings, and a loaded checkerboard |

**Machine cover:** `RegisRenderingTests`, `RegisPolygonFillTests`, `RegisMacrographTests`,
`MonochromeGraphicsTests`. They pin geometry and colour presence. Whether a circle looks round to a
person is the bit that has caught real defects twice — a squashed circle is how the doubled aspect
ratio was found.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4c — Is the text legible?

**Needs:** one test run.
**Do:** open `regis-text-sample.png`. It carries sizes S0 to S4, the height multiplier, italics, a
string running down the screen and one running up it, and three characters from a **loaded**
character set drawn as checkerboards.

**Pass:** every line reads as the words it says. The checkerboards are three squares of alternating
blocks, which is exactly what the hex pairs `AA,55,AA,55...` describe.

**Judge the S0 line separately, and know why it is worse.** The sizes come from the manual's table
7-3, but the manual prints no font — so the glyphs are the TDV2200 character ROM used as a
stand-in. That ROM's cell is fourteen rows and ReGIS's stored cell is ten, so at S0 the squeeze
loses detail a real VT340's own 8x10 glyphs would have kept. At S1 and above there is room and the
text is clean.

**This case exists because assertions could not see it.** Twenty-one tests passed while the first
sample sheet read "Thc quick brown fox" and "S1zcs from tablc 7-3" — every `e` drawn as a `c` and
every `i` as a `1`, because the rows that tell them apart were being dropped before scaling. One
look at the picture found it.

**Machine cover:** `RegisTextAndLoadTests` pins the sizes against table 7-3, the character
positioning, the control characters, the doubled-quote rule, and — exactly, because a host defines
the pixels — what a loaded cell draws.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4e — The registest grid against real hardware

**Needs:** the fetched corpus and one test run. Git bash must be on the machine, or the fetch script
cannot run `registest.sh` and this case has nothing to show.

**Do:** open `compare-registest-grid.png`, and then the other three `compare-registest-*.png`.

These are the **only ReGIS pictures anywhere with a hardware reference**. Everything else in this
section compares Sixel. The grid is the interesting one: it is the only fixture that uses the text
command, and it uses the temporary text control on every label.

**Pass:** the grid lines, their spacing and the diagonal labels match the capture below.

**Measured agreement with the hardware**, as of 2026-08-18, sampling every second pixel:

| Drawing | Agrees |
|---|---|
| checkerboard | 100.00% |
| raf | 99.85% |
| grid | 99.24% |
| bitplane | 73.99% |

The bitplane number is not a defect. Its capture is a photograph of hackerb9's whole screen with an
emacs buffer of the script over the graphics; our sheet shows the graphics plane alone. Every colour
sampled inside the picture matches.

**One difference is still open — do not report it as new:**

- **The two labels along the bottom edge are missing from ours.** The hardware draws them ABOVE the
  cursor, in rows 460 to 478; we draw below the cursor as chapter 7 says to, so they fall off the
  plane. Whether a VT340 clamps text that will not fit is not something the manual says.

**Fixed since this case was written — these should all look right now:**

1. **Line patterns** (2026-08-18). The rules were solid where the hardware's are dashed. Both
   patterned rules now light 401 and 400 pixels of 800, the same counts as the capture.
2. **Plane select, shading, negation and the colour map** (2026-08-18) — see M6.4f.
3. Labels used to be half height, because the default cell was S0 and table 7-4 says it is S1.
4. Labels written as `T22'...'` used to vanish, because a number between `T` and its string made the
   parser abandon the command.

**Machine cover:** `M6_4e_EachRegistestDrawingSitsBesideItsHardwareCapture` builds the sheets, checks
both halves carry ink, and — for the grid alone — asserts that both patterned rules light exactly as
many pixels as the photograph does. That is the only assertion in the program whose expected value
is a piece of hardware rather than a document. It still cannot judge whether the pictures LOOK the
same.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4f — The write controls, one panel each

**Needs:** one test run. The sheet is
`tests\RetroTerm.Tests\Avalonia\images\rendered\regis-write-controls.png`.

**Do:** open it. Six panels, labelled below, each drawn at one pixel per plane pixel — nothing is
scaled, so what you see is what the decoder put down.

| Panel | Shows | What right looks like |
|---|---|---|
| 1 Planes | Three bars, in planes 0, 1 and 2, crossing | Where two bars cross, a THIRD colour appears — not the last one drawn. That is the four-bit code, not a colour, being written |
| 2 Shading | A circle with shading on | A solid disc. Nobody computed a disc: the top and bottom arcs each shade to the same row and meet in the middle |
| 3 Vertical | The same circle shaded to a COLUMN | A solid disc again. `W(S(X)[x])` shades sideways |
| 4 Negate | The same dashed line twice, `W(N0)` then `W(N1)` | The two lines are photographic negatives of each other — where the top has ink the bottom has none |
| 5 Replace | A solid bar, then a dashed line over it in overlay and in replace | Overlay lets the bar show through the gaps. Replace ERASES them. This is the only writing style that can take ink away |
| 6 Remap | A drawing, then the same drawing after `S(M...)` moved two map locations | The colours change although nothing was redrawn. On the hardware the colour was never stored — pixel memory holds a code and the map is read at scan-out |

**Pass:** each panel matches its description. Panel 1 must show three distinct crossing colours and
panel 5 must show a visible difference between its two halves; if either looks uniform, something
has regressed to writing colours instead of codes.

**Machine cover:** `M6_4f_TheWriteControlsEachDrawTheirOwnPanel` draws the sheet and asserts the
distinguishing fact of each panel — the crossing colour, the filled centre, the inverted pattern, the
erased gap, and the changed colour. Those assertions cannot tell you the sheet is legible.

**Panel 5, measured 2026-08-18** when replace writing was built: the pattern's 0 bits write the
background instead of skipping, so a dashed line in replace mode ERASES its own gaps. On this sheet
the overlay gaps read `(204,204,204)`, the bar underneath showing through; the replace gaps read
`(0,0,0)`. The test samples both at x=140, a gap column, because sampling a dash compares two
identical pixels and says nothing.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4g — The report command, which answers instead of drawing

**Needs:** a host that asks. There is nothing to look at — this is the one ReGIS command with no
picture, so the by-hand part is only worth doing against something that actually reports.

**Do:** from a host, send each of these inside a ReGIS string and read what comes back.

| Send | Should come back | Meaning |
|---|---|---|
| `R(P)` | `[x,y]` then CR | where the drawing cursor is |
| `R(M(A))` | `@=A` … `@;` then CR | macrograph A's contents, empty body if undefined |
| `R(M(=))` | `"available,10000"` then CR | macrograph space free, then total |
| `R(L)` | `A'name'` then CR | the set selected for load operations |
| `R(E)` | `"0,0"` then CR | last parse error — see the warning below |

**Pass:** each reply arrives, in that shape, ending with a carriage return.

**Two things to know before you judge:**

1. **`R(E)` always answers `"0,0"`.** Nothing in this decoder flags parse errors yet, so that is a
   truthful report of what it knows rather than a real error history. It is still sent, because a
   host that asks and hears nothing waits forever. Table 10-1's error codes are not implemented.
2. **Turn host echo off first.** The manual is explicit: "make sure the system does not display the
   information on the screen. The data could affect your graphic images. There is no ReGIS control
   to prevent this action." A report echoed back into the terminal is fed to the ReGIS parser and
   will corrupt your picture. That happens on real hardware too — it is not our defect.

**Machine cover:** `RegisReportTests` — fifteen tests pinning every byte of every format against
chapter 10, including `TheReportReachesTheHostThroughTheEmulator`, which drives a real VT340
emulator with a device control string and reads what it puts on the wire.
**Judge:** a host.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4h — Coloured text through ReGIS: it works, and it matches the hardware

**Needs:** one test run. Open, side by side:
`tests\RetroTerm.Tests\Conformance\vt340regis\faketextcolor.png` (real hardware),
`tests\RetroTerm.Tests\Avalonia\images\rendered\regis-faketextcolor-ours.png` (ours).

**What the hardware does.** A VT340 cannot show multicoloured text — every text pixel holds the same
value, `0111`, so recolouring one recolours all of them. hackerb9's trick gets round it by writing
index 0 through a **plane mask**: a text pixel becomes `old AND NOT mask`, while the background is
already `0000` and does not move. Sixteen boxes through sixteen masks give sixteen values.

**What we do.** The same operation, per character cell. It lands in the right place because the
geometry agrees: the plane is 800×480, a cell on an 80×24 screen is exactly 10×20, and each box is
20 wide, so one box covers exactly two characters.

**Pass — check all four against the photograph:**

1. The plain row shows **eight colours repeating twice**. Plain text is `0111`, which has no bit 3,
   so mask *i* and mask *i+8* leave it at the same value.
2. On the plain row, **two pairs of letters are invisible** — `O P` and `4 5`. Those are masks 7 and
   15, both of which take `0111` to `0000`, the background colour.
3. On the bold row, **only the last pair is invisible**. Bold is `1111`, so only mask 15 clears it.
4. The colours in order are grey, yellow, cyan, magenta, green, red, blue.

**Measured, not judged by eye.** Sampling the photograph pair by pair gives codes 7,6,5,4,3,2,1 as
(117,117,117), (201,201,51), (51,201,201), (201,51,201), (51,201,51), (201,33,33), (51,51,201). Six
of those seven agree with our colour map within 8. **Register 7 does not** — the manual's own default
table calls it "gray 50%" and gives 53 percent, which is 135; the photograph shows 117, which is 46
percent. Nothing here can say which is right, so our value stays the manual's and the disagreement is
written down rather than tuned away. It needs a VT340 to settle.

**The one place we are not the hardware.** Boxes touch at a seam: box *i*'s right edge lands on the
first pixel column of the next box's left-hand character. On the hardware that is one pixel column of
one character and nobody would ever see it; here a cell is the smallest thing that can hold a colour,
so that character takes both masks. An operation has to cover **half a cell or more** to change it,
which is what keeps the one-pixel `V0` separator lines from repainting every letter they cross.

**Machine cover:** `M6_4h_TheFakeTextColourTrickRecoloursTheLettersItPassesOver` plays the fixture's
own sequence, checks every plane value against `7 AND NOT mask` and `15 AND NOT mask`, checks the
colour map against the photograph, and asserts that **nothing at all** is painted onto the graphics
plane — on the hardware these boxes are invisible.
**Judge:** eye, against the photograph.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4i — Colourised text that moves

**Needs:** M6.4h run first, so there is coloured text on screen.
**Do:** scroll it down (reverse index at the top), scroll it back up (index at the bottom), then
delete a few characters from the front of its line.

**Pass:** the colours survive both scrolls, and vanish from the whole line on the delete. That split
is hackerb9's measurement, not a guess — a vertical scroll moves the bitmap with the text, while a
sideways shift redraws the characters where they now belong, and drawing a character writes the
ordinary text value over whatever was underneath.

**Machine cover:** `M6_4i_ColouredTextSurvivesScrollingAndIsLostWhenTheLineShiftsSideways`.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4j — Line attributes erase the graphics on their row

**Needs:** the corpus fetched. Open
`tests\RetroTerm.Tests\Conformance\vt340regis\regis-decdwl.png` (real hardware) and read
`regis-decdwl.sh` beside it.

**What the hardware does.** Changing a line's attribute — DECDWL, DECDHL or DECSWL — **clears any
ReGIS graphics on that row**. Setting the attribute a line already has does nothing at all.

hackerb9's script asks exactly this. It draws **one** flag across six rows that differ only in the
order of three things — line **A**ttributes, **T**ext and **G**raphics — and prints the ordering
beside each row:

| Row | Order | Attribute set | Flag on the photograph |
|---|---|---|---|
| 1 | ATG | before the graphics | **intact** |
| 2 | GAT | after | **erased** |
| 3 | TAG | before | **intact** |
| 4 | GTA | after | **erased** |
| 5 | AGT | before | **intact** |
| 6 | TGA | after | **erased** |

**Pass:** the flag is banded exactly like the photograph — three stripes of it present, three gone.

**Why the no-change half matters.** It is what makes case 5 useful: a program can update the text on
a double-width line without erasing the picture behind it, because setting the attribute the line
already has costs nothing. Without that rule the two could never coexist.

**Deliberately NOT done: the text is untouched.** The fixture is explicit that unlike Sixel, ReGIS
"does not reset the line attribute flags to single width nor does it clear the underlying text
buffer" — and calls that a good thing, for the same reason.

**Machine cover:** `M6RegisLineAttributeTests` — six cases covering erase-on-change, survive-when-set-
first, nothing-on-a-repeat, only-the-row-that-changed, DECSWL erasing just as much as DECDWL, and the
text surviving. Four of them were watched failing with the clear disabled; the two that check nothing
is erased stayed green, which is the split that proves they are testing the right thing.
**Judge:** eye, against the photograph.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4k — A Sixel image puts every line back to single width

**Needs:** the corpus fetched. Open, in this order:
`tests\RetroTerm.Tests\Conformance\vt340test\decdwl-quirk2.png` then `decdwl-quirk1.png`. Both
captures carry their own explanation printed underneath the picture, which is why no script is
needed to read them.

**What the hardware does.** Quirk 2 states it plainly: *"The VT340 resets all line attributes to
single-width when a sixel image is received. This leads to a quirk where an image indented by a
double-width line suddenly reverts to single-width indentation."* The picture shows exactly that —
an image whose top sits at double-width indentation and whose remainder steps left.

Quirk 1 says the same and adds the consequence: *"Line 5 has text overwriting the image because the
double-width attribute that makes columns twice as wide was reset."*

**Do:** put a line into double width, print something on it, then send any Sixel image. **Pass:** the
line comes back to single width.

**Deliberately NOT done — and this one is a judgement call worth understanding.** The same captures
say the VT340 *"clears the underlying text buffer when a sixel image is received. Existing text is
retained only in the bitmap buffer and cannot be edited."*

On the hardware that costs nothing to look at: text and graphics are one bitmap, so the characters
stay on screen as pixels even though the terminal has forgotten them. Here text is a cell buffer, so
clearing it would make those characters **vanish** — further from the photograph than leaving them
alone. So the text is left. It is the same architectural split as M6.4h, and it is the rare case
where copying the hardware exactly would make the picture *worse*.

**Claimed by the VT340 and by nothing else.** `TerminalFeatures.ImageResetsLineAttributes`, following
`ImageColoursSetTextColours`: no DEC manual held here states it, and xterm and libsixel draw Sixel
without doing it. There is a test that an xterm is left alone.

**Machine cover:** `M6RegisLineAttributeTests` — the reset, the xterm exemption, and the text
surviving.
**Judge:** eye, against the two captures.
**Result:** ______  **Date:** ______  **By:** ______

### M6.4b — ReGIS from a host

**Needs:** the TestServer's graphics menu (item 5), which includes a ReGIS drawing.
**Do:** run it on a VT340.
**Pass:** the drawing appears and matches `regis-box-diagonal-circle.png` in kind.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M6.7 — Phosphor: graphics must follow the theme

<!--
    Renumbered from M6.5 on 27 August 2026. There were TWO cases called M6.5 in this file - this
    one and the graphics-input case below - and the run sheet, the plan and FINDINGS all mean the
    graphics-input one when they say M6.5. This one had no references anywhere, so it moved.
-->


**Needs:** one test run for the files, and the app for the live check.
**Do:**
1. Open `theme-mono-amber.png` and `regis-single-phosphor.png`.
2. In the app, switch the theme to amber with a picture on screen.
**Pass:** the vectors and the sixel go amber **with the text**. A real single-phosphor machine cannot
draw a green line on an amber screen; the defect that shipped was exactly that, because the draw
colour was a constant that happened to match the default green.
**Machine cover:** `GraphicsFollowTheThemeTests` and `MonochromeGraphicsTests` assert the hue follows
the theme. They run on the fixed set of themes in the suite; a theme you add yourself is not covered.
**Judge:** machine, plus one live switch by eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M6.6 — The printed versions

Graphics reaches paper by three routes — media copy, the ReGIS hardcopy command, and Tektronix
`ESC ETB` — and all three end in the same sink. The PDFs are the artefact:

| File | What to look for |
|---|---|
| `print-sixel-catalogue.pdf` | 16 pages, one per corpus fixture. Same judgement as M6.1, on paper |
| `print-plotter-sheet.pdf` | The whole plotter page |
| `print-colourwheel.pdf` | One picture, large |
| `print-job.pdf` | Text and graphics in one job — column alignment on the text page |
| `print-tektronix.pdf` | The three gnuplot streams as hard copies |

All in the rendered images folder. **Pass:** circles are round (the aspect is applied once, not
twice — an ellipse here is that defect returning), nothing is clipped at a page edge, and the text
pages line up in columns.
**Machine cover:** `PdfPrintSinkTests` checks page counts, page geometry in points, and that a
picture survives the round trip. Page *appearance* is this case.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M6.5 — ReGIS graphics input mode

Send a drawing followed by `R(I0)R(P(I))` — for example a box and then
`P[400,240]R(I0)R(P(I))` — and drive it by hand.

**a. The crosshair is visible enough to aim with.** Two lines cross the whole screen and meet at the
drawing point. **Pass:** you can see both arms against the drawing without hunting for them, on your
monitor at your brightness. The colour is white and is OURS — the manual gives the crosshair's shape
and never its colour, and register 7, the obvious pick, turned out to be 53% grey and nearly
invisible.
**Machine cover:** `RegisGraphicsInputPixelTests` renders it over a box and checks both arms are on
the screen and unbroken. Whether it is *comfortable to aim with* is this case.
**Judge:** eye.
**Result:** the pixels PASS — both arms cross the whole screen, meet at the drawing point, are white, and stay unbroken over the box and the circle. `m65-run2-round1.png`, driven over MCP 27 August 2026. **JUDGED BY RONNY, 31 August 2026: PASS.** He picked out both arms against the box, the circle and the diagonals without hunting for them, on his own monitor at his own brightness. The white crosshair stands as it is. M6.5a is CLOSED.  **Date:** 2026-08-27  **By:** measured

**b. The arrow keys steer it, and shift makes it move ten.** **Pass:** one press moves one pixel,
shift-press moves ten, and running off any edge wraps to the far side rather than sticking.
**Machine cover:** `RegisGraphicsInputKeyTests`, through the real canvas.
**Judge:** eye — specifically whether one pixel per press is usable at all, or whether crossing the
screen takes so long that the shift step is the only one anybody would use.
**Result:** the steps are EXACT, measured through the host's own report. One Right and one Up gave `X[401,239]` from 400,240. Nineteen shifted presses (nine right, ten up) gave `Z[490,140]` — every one landed. **JUDGED BY RONNY, 31 August 2026: PASS.** Driving it himself through all four rounds he navigated with the arrow keys and with shift, and reported every cursor style working. One pixel per press is usable; the shifted step is a convenience, not the only usable step. M6.5b is CLOSED.

**What the run also produced: a decision to ADD the mouse.** Aiming across the 800-pixel plane is 800 unshifted presses or 80 shifted, and a click is one action. The 2026-08-20 keyboard-only decision was made because the ReGIS manual describes only the arrow keys and nothing said who wins when a program is also asking for xterm mouse reports. That objection does not reach this case: one-shot graphics input SUSPENDS the host, so no program is listening for mouse reports in the window where the mouse would be used, and nothing new is arriving to select as text. Ronny decided on 31 August 2026 that a click places the cursor and the arrows still refine it, because the plane is scaled into the text area and a click can only round to the nearest ReGIS pixel. NOT verified: whether a real VT330/VT340 accepted a mouse or tablet as a ReGIS locator - nothing in `spec\` covers it.  **Date:** 2026-08-27  **By:** measured

**c. The session really does stop, and says why.** While one-shot mode is running the host's output
is held. **Pass:** the status line reads "Graphics input: arrow keys move the crosshair…", nothing
new appears on the screen, and pressing Esc puts everything that was held back on the screen at once
in the right order.
**Machine cover:** `RegisGraphicsInputEmulatorTests` proves the bytes are held and replayed. That the
NOTE is legible and lands before a reader concludes the session has died is this case.
**Judge:** eye.
**Result:** PASS — the green "sent while the cursor was up" line stayed off the screen for the whole of every round and arrived only after the round was answered, four rounds running.  **Date:** 2026-08-27  **By:** measured

**d. The four cursor shapes.** Select each with `S(C(I<n>))` **before** `R(I0)` — 0 or 2 crosshair,
1 diamond, 3 rubber band line, 4 rubber band rectangle. The two rubber band shapes stretch from the
drawing point to the cursor, so move the cursor and watch. **Pass:** all four are usable, and in
particular the diamond — 21 pixels on an 800-wide screen — can still be found on a busy drawing.
**Why before:** one-shot mode buffers everything the host sends, so a style selected afterwards does
not arrive until the mode ends. That is the manual's behaviour, not a defect.
**Machine cover:** `RegisCursorStyleTests` and two rendered PNGs. Whether the diamond is big enough
to aim with is this case.
**Judge:** eye.
**Result:** all four drawn and photographed — crosshair `m65-run2-round1.png`, diamond `m65-r2.png` (and moving, `m65-r2-moved.png`), rubber band line `m65-round3-rubberline.png`, rubber band rectangle `m65-round4-rectangle.png`.

**The busy-drawing half is now MEASURED, 28 August 2026.** `RegisDiamondOnABusyDrawingTests` puts
the diamond in the middle of a screen carrying a ruled grid, a fan of diagonals, twenty-five nested
circles and a run of text — including register 7, the pale grey that was once tried for the cursor
itself. `regis-diamond-on-a-busy-drawing.png` is the sheet.

It renders the same drawing twice, with the cursor up and without, and compares the two. That
answers three things a single picture cannot:

 - the diamond marks its own point and changes **nothing else on the screen**.
 - its changed region spans the full 21 pixels each way, so the shape is not clipped by the drawing.
 - every pixel it touches comes out **brighter** than what it covered, so it is a mark on the
   picture rather than a hole in it.

**Why the difference and not the colour.** The obvious test — the cursor is full white, so count
white pixels — does not work, and the first draft failed on a screen where the PNG plainly shows
the diamond. The 800 by 480 ReGIS plane is scaled DOWN into the text area, so a one-pixel white
line lands on less than one screen pixel and is averaged with what is behind it, arriving as a
light grey no threshold can tell from register 7. Comparing two frames is immune to that.

**JUDGED BY RONNY, 29 August 2026: PASS — he found the diamond easily on the busy sheet.** The
manual's 21 pixels stand; nothing changes. What is left of M6.5 is only M6.5a and M6.5b, the
crosshair's aiming comfort and whether one pixel per arrow press is usable.  **Date:** 2026-08-29  **By:** Ronny

**e. Answering ends it.** Press any ordinary key. **Pass:** the crosshair vanishes, the drawing is
untouched, the held output arrives, and the host receives that character followed by `[X,Y]` — check
with a trace running.
**Judge:** eye plus a trace.
**Result:** PASS — answering ended each round, the drawing survived, the held output arrived, and the host received the key followed by the position every time (`X[401,239]`, `Y[400,240]`, `Z[490,140]`).  **Date:** 2026-08-27  **By:** measured

---


### Two things found while driving M6.5 over MCP, 27 August 2026

**SCREENSHOT can save a frame that is one change out of date.** It renders the control, and the
control draws the last PUBLISHED graphics composite - so a picture taken before the app has
repainted shows the previous one. Twice this looked like a defect and was not:

 - On a brand new tab the first ReGIS picture came out EMPTY - no box, no circle, no crosshair -
   because that tab had never published a composite. The same round photographed after one repaint
   is complete. This is almost certainly what the 27 August live run saw when it reported no
   crosshair.
 - Sixteen arrow presses sent in one script left the rubber band line apparently unmoved. The
   host's report afterwards read Z[490,140], which is every one of those presses landed exactly.
   Only the repaint was behind.

**FIXED THE SAME DAY.** SCREENSHOT now waits `settle=` milliseconds for the screen to go quiet
(250 by default) and publishes the graphics on the session thread before it draws, so no caller
has to remember a rule. `settle=0` takes the frame as it stands. Proved against the live app: the
same brand new tab, same first round, now draws in full - `fresh-tab-round1-after-fix.png`.

**The test server prints each round's report under the NEXT round's heading.** Round 1's
"Host received:" line came out empty, round 2's carried round 1's answer, and so on for all four
rounds, in two separate runs. The reports themselves are correct - only where they are PRINTED is
wrong. Which side is late, our send or GraphicsTestSuite.CollectGraphicsInputReportAsync giving up
early, is not established here and no cause is claimed.

## What this section cannot settle

- **Nothing here has been checked against a real VT340 in this room.** The corpus captures are the
  nearest thing, and they are photographs taken by somebody else.
- **ReGIS text (`T`) is built, and its glyph shapes are a stand-in** — the TDV2200 ROM, because the
  manual prints no font. `hersheydemo.regis` in the ReGIS corpus would exercise it hard; it is not
  fetched today, and fetching it is the next thing worth doing for this section.
- **The ReGIS report command (`R`) is built in full**, including graphics input mode — see M6.5. It
  answers a host rather than drawing, so a picture cannot judge it and a live host still would.
- **The user-defined input cursor is not built** — `S(C(I[+5,+10]"XO"))`, a 16 by 24 shape built from
  two characters of the loaded set. The four standard shapes are; see M6.5d.
- **No graphics OUTPUT cursor is drawn.** `S(C0)`, `S(C1)` and `S(C(H n))` are read and remembered so
  a host is answered honestly, but a real VT340's "waiting for the next command" mark has no
  equivalent here.


---



## The comparison-sheet review of 28 August 2026 - all fifteen sheets, case M6.1a



Every sheet in `tests\RetroTerm.Tests\Avalonia\images
endered\compare-*.png` was opened and

compared against its hardware photograph. Ours on top, hackerb9's VT340 capture below, both at the

hardware's own 800 x 480.



### One defect found and FIXED - the DCS pixel aspect ratio



**Commit `e871c63`.** A sixel image states its aspect twice: in the DCS parameter Pa, and again in

the raster attributes. We read only the raster attributes, so every image written before raster

attributes existed rendered at **half its proper height** - silently, because a squashed picture

still looks like a picture.



`cat-original.six` and `cat-vt240.six` are the only two fixtures with no raster attributes, and the

photographs show both standing twice as tall as we drew them. Measured across all fifteen fixtures

before changing anything: only those two move. Everything else declares raster attributes or asks

for 1:1.



Verified, not recalled: `DCS Pa ; Pb ; Ph q` and `Pa -> pixel aspect ratio` are in the xterm control

sequences document held in `spec\DEC\`; Table 5-1 of the Level 2 Sixel Programming Reference gives

Pa 0 and Pa 1 as aspect 200:100 and an omitted parameter as 0. The rest of that table is not held

here, so Pa 2 to 8 stay at 1:1 and say so in the code.



### One claim raised here and WITHDRAWN on 28 August 2026 - the out-of-range colour introducer

**I reported that our cat-vt240 is blue where the hardware is green because we keep the register
SELECTION from an introducer whose colour system is out of range. The reasoning does not survive
reading the fixture, and a change made on the strength of it was reverted.**

`cat-vt240.six` defines `#0;1;280;35;60` - a green - then sends `#1;10;0;0`, whose colour system 10
is neither HLS nor RGB. Our rule ignores the definition but keeps the selection, so register 1 stays
at its power-on blue. I proposed ignoring the whole sequence, which would leave the pen on register
0's green and match the photograph.

**Two things kill it.**

First, the fixture RE-SELECTS register 1 with a bare `#1` twenty-eight times, once before every
drawing line. Whether the ignored introducer also selects is therefore irrelevant - register 1 is
explicitly current when the cat is drawn either way. The change cannot alter that picture at all,
which the suite confirmed: nothing moved.

Second, there is already an explanation on record, and it was sitting in
`SixelShortColourIntroducerTests` where I did not read it. hackerb9's own `sixelcomments.md` says
this picture exists in a ReGIS-palette version because "early VT240 firmware did not handle sixel
color palettes correctly". The green cat may be a record of that firmware defect rather than of
correct behaviour.

The cat is blue because register 1 is never DEFINED. That is the whole of it, and the standing rule
about keeping the selection is untouched by any of this.

### One claim raised here and WITHDRAWN the same night

**I reported ink painted beyond the declared image width. It is not a defect and the claim was
wrong.** Recorded rather than deleted, because a withdrawn finding is evidence about how the
finding was made.

`colorwheel.six` and `colorwheel+dither.six` both declare `"1;1;480;480`, and ours paints a solid
band across the remaining 320 pixels of every row - yellow on one sheet, cyan on the other. A
different colour on each looked like a register bleeding past the width.

**What the bytes actually say.** Walking the payload column by column, the widest column either
image reaches is exactly **480**. Neither paints a single pixel past its declared width, so the
band cannot be coming from the image data.

It is the VT340's colour-ordering scheme, which this program implements on purpose: the sixth
colour an image DEFINES becomes the text foreground and the sixteenth becomes the text background.
The sixteenth definition in `colorwheel.six` is RGB `94;88;0`, which is that yellow; in
`colorwheel+dither.six` it is RGB `0;76;76`, which is that cyan. The band is the terminal's text
background, correctly painted, in exactly the colour the rule predicts.

The same rule is what `cat-vt340.six` demonstrates on hardware - its photograph shows the whole
screen in the sixteenth colour defined. And the two colorwheel photographs are cropped to 480 x 480,
so they never said anything about that region at all.

**What went wrong in the looking.** Two halves of a sheet at different sizes were read as though
they were the same view, and a difference was called a defect without checking whether the corpus
image could speak to it. The measurement that settled it took two minutes.

### The four remaining differences, all now decoded

Each was measured with PIL rather than read by eye, after eye-reading two halves at different crops
produced two wrong claims earlier the same night. **Three turned out to be real defects and one
turned out to be nothing.** None has been fixed: each touches behaviour well beyond its own sheet.

 - **`cat-original` four rows low - EXPLAINED 28 August 2026, and it is NOT a defect. The
   photograph scrolled; our render did not.**

   Measured: our ink begins on plane row 150, the hardware's on row 70 - a difference of exactly 80
   pixels, which is four text rows of 20. The hardware's "Drawn by Brian Guinn." signature occupies
   rows 412-458; add the same 80 and it would sit at 492-538, off the bottom of a 480-row plane.
   That is precisely why ours is missing, and it falls straight out of the same 80 pixels rather
   than being a second thing to explain.

   The hardware half carries a shell command line along its bottom edge, so text was printed AFTER
   the image and scrolled the screen up by those four lines. `Play` in `M6SixelAndRegisTests` feeds
   only `ESC [ ? 2 5 l` and then the fixture, so nothing on our side moves the cursor at all. Ours
   is the state before the scroll; the photograph is the state after it.

   Nothing to fix. Worth keeping written down because the missing signature looks like clipping and
   is not.

 - **`textcursor` shifted right by sixteen columns - EXPLAINED 28 August 2026, and it is a real
   defect in the CSI dispatch, not in the graphics.** Decoding the fixture shows the whole file is
   ONE sixel image of 16,580 bytes; the labels a reader sees are painted pixels, not terminal text.
   The only other thing in the file is `ESC [ 2 SP I` before the image.

   `CSI Ps I` is CHT, cursor forward tabulation. This one carries a SPACE intermediate, which under
   ECMA-48 makes it a different control function - an intermediate is part of a control's identity,
   not decoration. `TerminalEmulatorBase` handles space-intermediate sequences for `@`, `A` and `q`,
   and for `P`, `R` and `Q` when the profile has page memory. Everything else falls through to the
   "Standard CSI sequences" switch, **which dispatches on the final byte alone**. So this executes
   as CHT.

   The arithmetic matches the picture exactly: two tab stops from column 0 is column 16, the cell is
   800 / 80 = 10 pixels wide, and 16 x 10 = **160 pixels** - the measured shift. Real hardware does
   not move, so the fifth column stays on screen there and is clipped here.

   **The defect is general, not about this one sequence.** Any CSI arriving with an intermediate
   that is not explicitly handled is executed as though it had none. The fix is for the main switch
   to run only when there is no intermediate, with anything else counted through
   `CountUnrecognisedSequence` instead of obeyed.

   **FIXED 28 August 2026.** The standard switch now runs only when there is no intermediate;
   anything else is counted. DECRQM (`CSI Ps $ p`) had been testing its own intermediate from
   inside that switch and had to move up beside the other claimed intermediates - five
   ModeHandlingTests caught that immediately. Red before green: with the guard disabled the
   cursor lands on column 16. Pinned by `CsiIntermediateIdentityTests`.

   **Blast radius, measured across all 114 files of the conformance corpora** rather than guessed:
   only four distinct CSI-with-intermediate shapes appear anywhere.
    - `CSI Pn ' }` and `CSI Pn ' ~` - DECIC and DECDC, in t602 and t603. Already handled by the
      apostrophe branch, so these are correct today.
    - `CSI 2 SP I` in `textcursor.six` and `CSI 7 SP I` in `vaxrgl-lntest.six`. Both run as CHT.
      The first is the 160-pixel shift above; the second would be seven tab stops, 560 pixels.
    - `CSI ? 20 SP J` in `vaxrgl-lntest.six`. This one carries a private marker as well, so it may
      go down the private-mode path rather than to ED - NOT checked, and not claimed either way.
      Worth checking first if this is ever fixed, because ED on the wrong path erases the screen.


 - **`registest-grid` labels sit above their grid line - EXPLAINED 28 August 2026, and it is a real
   defect: ReGIS PV spacing leaks from one text command into the next.**

   Measured off the sheet with PIL rather than by eye, because eye-reading two halves at different
   crops produced two wrong claims earlier the same night. The horizontal grid line is on plane row
   100 in BOTH halves - that part is right. The `100` label:
    - ours occupies rows **62-74**, which is ABOVE the line;
    - the hardware's occupies rows **104-114**, which is below it.
   The rotated label beside it shows the same shape: ours rows 111-133, the hardware's 131-155.

   `DrawGlyph` computes `y = _y + targetY` with targetY running 0 upward, so a glyph is anchored at
   its TOP and grows downward - which would put it below the line, as the hardware has it. Something
   is lifting the pen, and it is PV spacing.

   `_textPvRight` and `_textPvDown` in `RegisDecoder` are only ever ACCUMULATED with `+=`. Nothing
   clears them. `registest.sh` sets `T22` for the bottom-edge labels at y=479 - two PV digits, each
   half a display cell, which is exactly what keeps those labels on screen - and then `T4` for the
   right-edge label. Every later grid label is written with a plain `T(B S1)` carrying no PV digits
   at all, and inherits the accumulated lift.

   **The photograph settles which behaviour is right.** On the hardware the bottom-edge labels ARE
   lifted and the grid labels are NOT, from the same stream. So PV spacing applies to the text
   command that carries it and does not persist into later ones. Ours persists, and every label
   after the first `T22` is displaced for the rest of the picture.

   **A FIX WAS ATTEMPTED ON 28 AUGUST 2026 AND REVERTED, because the manual refutes it.** Resetting
   PV at the start of each text command turned two tests red that were written from chapter 7's own
   words. The decisive one is the manual's worked example: "if you selected superscripting (PV = 2),
   use subscripting (PV = 6)" to return to the baseline - and `T2` followed by `T6` is TWO text
   commands, so the value MUST survive from one to the next.

   So the accumulation is right, and something else in that stream returns the value to zero on real
   hardware - most likely the `T(E)` closing each label's temporary text control, though nothing held
   here says so. The divergence stands unexplained rather than papered over.

 - **`extremeratio` olive side bands - EXPLAINED 28 August 2026: we never read the DCS background
   colour option, Pb.**

   Measured with PIL, and the first measurement RULED OUT the obvious suspect. The checkerboard
   bands are identical in both halves - 80 screen rows each, at exactly rows 0-79, 80-159, 160-239,
   240-319, 320-399 and 400-479. Our vertical scaling for an 80:1 aspect is therefore exactly right,
   which leaves only the side columns to explain.

   Down the left edge at x=20: ours is the image's olive for rows 0-79 and then our phosphor
   background `(0,25,17)` for rows 80-479. The hardware is olive `(51,69,18)` for rows 20-479, the
   whole way down. The right edge is the same shape - ours olive only at rows 400-479, the hardware
   olive throughout. Those two of ours are a faithful decode of `#2 !80@ !640? !80_`: `@` is sixel
   bit 0, `_` is bit 5, and at 80 rows per bit that is rows 0-79 and rows 400-479.

   So the pixels we DRAW are right. What is missing is the fill behind them. `extremeratio`'s
   picture DCS is `ESC P 9;0;0 q`, and the middle parameter is Pb, the background colour option -
   "Pb -> background color option" in the xterm control sequences document held in `spec\DEC\`.
   `TerminalEmulatorBase` reads `Parameters[0]` for the aspect and **nothing else**, so every image
   is treated as though its zero bits were transparent.

   Consistent with the other sheets: `cat-vt240.six` sends `ESC P ; 1 q`, Pb=1, the transparent
   case - and that background does look right.

   **FIXED 28 August 2026, and the manual answered the colour question.** The VT330/VT340 Graphics
   Programming manual in `spec\DEC` gives the P2 table outright: 0 or 2, the default, means "pixel
   positions specified as 0 are set to the current background color"; 1 means they "remain at their
   current color". The area is the raster one - "The VT300 uses Ph and Pv to erase the background
   when P2 is set to 0 or 2".

   So the fill is the terminal's CURRENT BACKGROUND COLOUR, not a sixel register. My guess that it
   was the last defined register was wrong; the olive in the photograph was hackerb9's screen
   background at capture time, not a rule.

   **Which also means this does not explain the extremeratio side bands after all.** With the
   background at its terminal default, leaving those pixels transparent shows the terminal's own
   background - which IS the current background colour. Our sides look different from the
   photograph's because the two machines' backgrounds differ, the same theme difference already
   noted below. The conformance gap was real and is now closed, but it was never the cause of what
   I saw.

   Pinned by `SixelBackgroundOptionTests`. Reading the manual also produced the full aspect table -
   2 is 5:1, 3 and 4 are 3:1, 5 and 6 are 2:1 - which had been left at 1:1 rather than guessed.


One difference IS explained and is not a fault: where an image paints nothing, ours shows the dark
green phosphor theme and the hardware shows black. That is the theme, not the decoder.

### Sheets that match with nothing to report



`cat-vt340`, `cat-libsixel`, `map8`, `steiner`, `multisize`, `registest-raf`,

`registest-checkerboard`, `registest-bitplane` - including the colour mixing of three overlapping

circles and the label cascade that used to be a defect.

