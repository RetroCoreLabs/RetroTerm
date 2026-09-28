# Priority plan — 2026-08-09

Written after a run of conformance work against libvterm's corpus. This is the plan I am
working to; it supersedes nothing, it just says what comes next and why.

## Where things stand

| | |
|---|---|
| Tests | 4137 passing, 41 skipped, 0 warnings |
| Corpus | 97 → **8** disagreements, 395 assertions passing |
| Corpus location | `tests\RetroTerm.Tests\Conformance\libvterm\` (43 files, MIT, Paul Evans) |

The corpus is an **outside** opinion. Every other terminal test in this repo was written by us
from our own reading of the specs, so a misreading we shared with ourselves was invisible. Eight
of its files are DEC's own vttest screens turned into machine-checkable assertions.

### What it found

Fifteen real emulator defects, all shipped:

| Defect | Effect before the fix |
|---|---|
| `Resize` kept a stale cursor bound | Shrinking the window then typing **threw** out of the buffer indexer |
| Eager wrap at the last column | Cursor a line too far; screens scrolled a line early |
| Five ECMA-48 position sequences missing | `HPA HPR HPB VPR VPB` parsed and did nothing |
| EL did not end a line | Screen-to-text joined two unrelated lines |
| ICH missing | A host opening a gap to insert text overwrote instead |
| DECALN missing | Decoded for the logs, did nothing on screen |
| LF/RI scrolled from outside the region | Status lines and pager footers: jumping screen, stuck cursor |
| Line size wiped by typing | A double-width line stopped being double-width on first keypress |
| Double-width line used full width | Text ran off the visible right edge |
| No reflow | Dragging narrower **destroyed** the right-hand side of every wrapped line |
| Cleared cell indistinguishable from a written space | Trailing spaces lost through reflow and screen reads |
| Height resize kept the top and cut the bottom | Shrinking a window threw away the NEWEST output and stranded the cursor |
| Wide characters used one cell | CJK and emoji clipped; every following column one place out |
| Combining marks used a cell | The mark failed to attach AND destroyed the next character |
| DECSCA + selective erase missing | Clearing a form's input fields cleared nothing at all |
| TDV2200 claimed `q` with no intermediate check | Swallowed DECSCA and DECSCUSR, doing a work-area delete instead |

And **five bugs in the conformance runner itself** — worth stating plainly, because a test
harness that lies is worse than no harness. The worst was `$SEQ  2  7:` being aligned with double
spaces: `Split(' ')` turned it into rows 0..2 instead of 2..7, so five assertions never ran and
two ran against the wrong rows while reporting green.

## Phase A — finish the corpus

| # | Item | Wins | State |
|---|---|---|---|
| A1 | Untouched cell ≠ written space | 10 | **DONE** |
| A2 | Wide + combining characters | 5 | **DONE** |
| A3 | Height resize through history | 4 | **DONE** |
| A4 | DECSLRM (left/right margins) | 1 | Todo, low value |
| A5 | DECSCA + selective erase | 1 | **DONE** |

**A2 — wide and combining characters. DONE.** A wide character claims two cells (lead + trail,
using the flag bits freed when double size stopped being stored twice); a combining mark claims
none and composes into its base where Unicode has a precomposed form. Where it does not —
Devanagari, Thai, stacked accents — the mark is dropped, which is written on
`UnicodeComposition` rather than hidden. Deliberately not applied to the TDV models: those are
`TransportEncoding.EightBit` and read a high byte as an ISO 646 national character, not as part
of a UTF-8 sequence.

**A3 — height resize through history. DONE.** It turned out not to be about width at all: a
height change has to slide the screen over the scrollback rather than cut off the bottom. Four of
the five items left in `63screen_resize` are an architectural difference, not a defect — libvterm
keeps its scrollback in the harness, outside the terminal, so its RESET cannot clear it; ours
lives in the buffer and RIS clears it, as xterm does.

**A4 — DECSLRM.** A real feature for one assertion. Do last, or not at all.

**A5 — DECSCA and selective erase. DONE.** `CSI Ps " q` marks a field protected; `CSI ? J` and
`CSI ? K` erase everything else. That pair is how a host draws a form once and afterwards clears
what the user typed without wiping the labels. Neither existed: the private forms fell into the
DEC private-MODE switch, which knows mode numbers rather than `J` and `K`, so clearing a form
cleared nothing at all.

It also turned up a latent bug of its own. TDV2200 claimed the `q` final for NDDLWA (Delete Lines
in Work Area) with **no intermediate check**, so it swallowed `CSI Ps " q` — and would equally
have swallowed DECSCUSR, `CSI Ps SP q`. A 2200 did a work-area delete instead: a wrong action,
not a missing one. TDV-specific finals are now claimed only when no intermediate is present.

### The 8 that remain, exactly

| Count | File(s) | Cause | Ours to fix? |
|---|---|---|---|
| 4 | `63screen_resize` | Scrollback lives inside the terminal for us, outside it for libvterm, so our RIS clears it | No — architectural |
| 3 | `16state_resize`, `63screen_resize` | libvterm parks a pending-wrap cursor past the last column; we keep it at 79 with a flag | No — same screen, ours is what DECXCPR reports |
| 1 | `15state_mode` | DECSLRM not implemented | A4, low value |

**Nothing left in the corpus is a defect.** Seven of the eight are deliberate differences in how
the two terminals represent the same screen, and the eighth is one unimplemented feature. Phase A
is finished apart from A4, which I would leave alone.

## Phase B — the migration plan

| # | Item | State |
|---|---|---|
| B1 | DW/DH rendering — draw double-size glyphs 2×2 | **DONE** |
| B2a | Glyph cache in both font renderers | **DONE** |
| B2b | One frame per chunk, not per escape sequence | **DONE** |
| B2c | Dirty-row redraw | Not started — see below |
| B3 | Presentation themes over the canonical palette | **DONE** (one gap, below) |

**B1 — double-size rendering. DONE.** `TerminalRenderer` read none of the line-size bits, so a
host sending `ESC # 6` (DECDWL) or `ESC # 3`/`ESC # 4` (DECDHL) changed the buffer and produced
nothing visible: the banner it wanted twice the size came out ordinary, and a DECDHL pair came out
printed twice.

Now a double-size row places buffer column *c* at screen column *2c*, gives every cell — background,
selection, search highlight and cursor alike — twice the width, and stops at the halfway column the
model already refuses to move the cursor past. The glyph is scaled by a clip plus a transform
wrapped around the ordinary draw rather than a second copy of the glyph code, so both font
renderers (bitmap TDV and system-font VT) get it without knowing it exists. A DECDHL pair draws two
halves of ONE 2×2 glyph — the bottom row starts its glyph a cell higher and clips back — so the
halves line up with no seam.

Held by `tests\RetroTerm.Tests\Avalonia\DoubleSizeRenderingTests.cs`, on real pixels through the
production canvas. Four of its seven go red with the scaling removed.

Two things that test found, both about measuring rather than about the feature: the cursor is drawn
as a filled block, so text must park the cursor elsewhere or every "did it spill into the next
cell" check measures the cursor; and `InkPixelsInCell` takes each cell's most common colour as the
background, which INVERTS once a double-size glyph covers most of its cell — a bigger character
measured as less ink. The size comparisons read the background once from a known-empty cell instead.

**B2a — glyph cache. DONE.** Both font renderers rebuilt every glyph from scratch for every cell
of every frame:

| Renderer | What it did per cell, per frame |
|---|---|
| `BitmapFontRenderer` | Called `FontBase.GetFontBits`, which **allocates a fresh `ushort[]`** — 1,920 arrays for one 80×24 frame, on a hot path this project's rules say must not allocate — then issued one `FillRectangle` per **lit pixel**, up to 126 per cell |
| `SystemFontRenderer` | Built a `Typeface`, a `FormattedText` and a fresh outline: full text shaping, 1,920 times a frame, for the few dozen distinct characters actually on screen |

Each glyph is now built once and kept as a geometry in cell-local coordinates; the cell's position
is a translation, which is also what lets the double-size transform from B1 compose with it. The
bitmap path additionally merges each row's consecutive lit pixels into **one** rectangle, so even
the first build issues far fewer figures than the old loop did every frame.

Keys: codepoint + font number + ISO 646 variant for the ROM fonts, codepoint + weight + slant for
the system font. Colour is deliberately **not** in either key — the brush is applied at draw time,
so a 256-colour screen still holds one entry per shape. "No glyph here" is cached too; without that
a missing character redoes the whole three-step lookup every frame.

Held by `tests\RetroTerm.Tests\Avalonia\GlyphCacheTests.cs`, which **counts** the builds rather
than trusting the cache works. Eight of its ten go red with the lookup disabled; the two variant
tests go red, and only those two, when the variant is dropped from the key. The other half of the
check is the existing headless UI suite — a cached glyph that drew differently would fail the TDV
screenshot and `GlyphPixelValidator` tests, and none moved.

**Not measured:** how much faster this actually is. There is no benchmark harness in this repo and
adding one to prove a point about allocation counts that can be read straight off the code did not
seem worth it. The claim here is "fewer allocations and fewer draw calls", which is verifiable by
reading `BuildGlyph`; it is NOT a claim about frames per second.

**B2b — one frame per chunk. DONE.** Twenty-nine places call `OnInvalidated`, and each one
published a whole fresh frame — `PublishFrame` copies every cell on screen — and raised
`Invalidated`, which the canvas answers by posting a repaint to the UI thread. One read from a host
routinely carries dozens of escape sequences. A TDV host repainting a form is the worst case: the
2200 raises `Invalidated` from ten different handlers, so one form copied the screen and queued a
repaint dozens of times to show one final picture.

Nothing outside could see those intermediate frames anyway — the frame is read only by the
renderer, on another thread, whenever it happens to draw. So they were pure cost with no observer.

`BeginBatch`/`EndBatch` hold the publish until the outermost batch closes. `ProcessData` opens one;
`TDVEmulatorBase` opens its own, because it overrides `ProcessData` to bypass the base class
entirely — the two-surface trap one layer down, and a fix applied only to the base would have left
the emulators that repaint hardest still doing it per sequence.

Measured, this time, because the test counts it: a twenty-sequence chunk raised **21** invalidations
before and raises **1** after. Held by
`tests\RetroTerm.Tests\Terminal\InvalidationBatchingTests.cs`, which also pins the things batching
must NOT change — each chunk still gets its own repaint, a chunk that only moved the cursor still
republishes, and the one frame shows the chunk's FINAL state rather than a half-drawn screen.

### What a frame actually costs — measured 2026-08-09

`tests\RetroTerm.Tests\Avalonia\RenderCostBenchmarkTests.cs` draws through the production renderer
and prints timings. It asserts **no** wall-clock threshold — that would be a flaky test on a
developer box — and every measurement first proves through `RenderedScreenshot` that real ink
reached real pixels, so a headless backend that skipped rasterising could not report a wonderful
number for doing nothing.

Fastest frame of 200, best of three runs on this machine (which was also running Visual Studio,
Unity and a live emulator):

| Scene | Fastest frame | Share of a 60 Hz frame |
|---|---|---|
| VT100 80×24, every cell text | 15.0 ms | 90% |
| TDV2200 80×24, every cell text | 9.2 ms | 55% |
| VT100 80×24, every cell text + colour | 8.0 ms | 48% |
| VT100 80×24, **one line** of text | **0.29 ms** | 1.7% |
| **Blit a cached 80×24 screen bitmap** | **0.29 ms** | 1.7% |
| `PublishFrame` 80×24 (not a render) | 0.039 ms | 0.2% |

**Read the minimum, not the mean.** The mean was tried first and thrown out: across four repeats of
one identical screen it ranged from 24 ms to 56 ms, which was interference from other processes,
not render cost. The fastest frame is the least polluted sample; the median printed beside it shows
how busy the box was.

**Two things this settles.**

1. **B2c is worth building.** A full screen costs 8–23 ms and a near-empty one costs 0.29 ms — a
   30-to-70× gap that tracks the number of cells drawn. Terminal frames overwhelmingly change one
   or a few rows: a line of output, a keystroke, a cursor blink. Redrawing only those moves the
   common case toward the 0.29 ms floor instead of paying for a whole screen every time.

2. **A correction to what B2b was for.** The commit message and the note above lead with
   `PublishFrame` copying 1,920 cells. Measured, that copy is **0.039 ms — two tenths of one
   percent of a frame**, and it is not on the UI thread anyway. The expensive thing batching
   avoided was the twenty-one *repaint requests*, each of which is a full render at 8–23 ms. Right
   change, wrong headline reason; recorded here so the next optimisation is not chosen by the same
   mistaken model of where the time goes.

**One result I cannot explain.** The plain full-text screen is consistently SLOWER than the
coloured one (15.0 vs 8.0 ms fastest), even though the coloured screen draws the same 1,920 glyphs
*plus* a background rectangle behind each. Same character set, same emulator, same size. I do not
know why, and I am not going to invent a reason. It does not change the conclusion — both are
enormous next to the 0.29 ms floor — but it means the absolute figures should be treated as an
order of magnitude, not as precision measurements.

### B2c — dirty-row redraw: the case for it, and the hole in the case

**What the blit number settles.** Every frame under a cached-bitmap design pays for the blit,
whatever else it saves, so if the blit cost what a render costs the design would be worthless. It
does not: **0.29 ms against 11.2 ms, about 40× cheaper.** A one-row change would cost roughly the
blit plus one row's glyphs — under 1 ms against 11 ms today. `Avalonia.Base.xml` confirms
`RenderTargetBitmap.CreateDrawingContext(false)` leaves existing image data alone, so incremental
row repainting is possible at all. Both were worth one measurement each rather than a day of work.

**THE HOLE, found while writing this up.** The harness renders into a `RenderTargetBitmap` in a
single call, so it times **recording and rasterising together, on one thread**. The running app
splits them: `Render` records a display list on the UI thread and the compositor rasterises on its
own thread, reusing the recorded list when nothing invalidates. So 11.2 ms is an upper bound on the
combined work — it is **not** a measurement of how long the UI thread stalls, and I do not have one.

This does not touch the relative figures: a full screen really is ~40× a blit, and that ratio is
what dirty-row redraw trades on. It does weaken the headline. "A frame costs 11 ms" would be
overclaiming; "recording plus rasterising a full screen costs 11 ms, split unknown" is what was
actually measured.

**Second caveat — resolution.** The canvas scales the terminal to fit its window, so the scale is
almost never exactly 1.0. A cache rendered at natural size and then scaled on the blit would look
softer than today's glyphs, which are rasterised at final size. Done properly the cache has to hold
**device-resolution** pixels, which means the scale moves from the canvas into the renderer — a
real refactor of that seam, not just a bitmap bolted on. Softness is also exactly the kind of thing
only Ronny can judge, so it must not be discovered after the fact.

**B2c — dirty-row redraw. DONE.** Built in two commits so the drawing change and the caching change
stayed separable: first the scale moved from `TerminalCanvas` into `TerminalRenderer` (no pixel
moved), then the cache itself.

`TerminalRenderer` now keeps the screen in a `RenderTargetBitmap` at **device** resolution, diffs
each incoming frame against the cells it last drew, repaints only the rows whose appearance
changed, and blits. Measured on the same harness, before and after:

| Scene | Before | After |
|---|---|---|
| Full screen, nothing changing | 11.2 ms | **0.28 ms** |
| Full screen, **one row changes** (the common case) | 11.2 ms | **0.91 ms** |
| Full screen, whole screen scrolls (worst case) | 11.2 ms | **6.0 ms** |

No case got worse, which was the thing to check.

**Cell content is not enough to decide a row is unchanged.** A row can look different with identical
text: selected, highlighted as a search hit, holding the cursor, or holding blinking characters
currently in their off phase. All of that is captured per row in `RowAppearance` and compared
alongside the cells. Diffing only the cells would have frozen the cursor and stopped blinking text
dead.

**Two real bugs the pixel tests caught**, neither of which would have failed any existing test:

1. **Spill across a row boundary.** A glyph is not guaranteed to stay inside its own row — a
   descender or a tall accent reaches across. When a row changes, the stale spill it left in the
   bands above and below is still there. Dirty rows now expand by one in each direction.
2. **Fractional row heights.** A band expressed in cell-grid coordinates has an antialiased edge,
   and an antialiased edge blends with whatever is *already in the bitmap* — transparent on a fresh
   cache, the previous frame on a reused one. The same screen came out different depending on
   whether the cache had been drawn before, so even a whole-screen scroll differed from a cold draw
   by several hundred pixels along every row boundary. Bands are now computed and clipped in whole
   **device** pixels, which tile exactly and blend with nothing.

Held by `tests\RetroTerm.Tests\Avalonia\DirtyRowCacheTests.cs`. Every test there renders twice
through one renderer, changing something between, and compares against a second renderer that saw
the final state cold — text changing, the cursor moving between rows, the blink phase flipping, a
whole-screen scroll, a double-height pair changing, nothing changing at all, and the same cases at
2× magnification. Six of its eight were red when it was first written; that is what found both bugs
above. The other ~500 UI tests could not have: they each build a fresh renderer, so their cache is
always cold and the incremental path — the entire risk — never runs.

**A benchmark flaw fixed on the way.** The worst-case scene first scrolled in short lines, so after
a few hundred frames the screen was nearly empty and it reported the worst case as *cheaper* than
changing one row of a full screen. It now scrolls full-width lines. A benchmark that quietly empties
its own subject is worse than no benchmark.

**Unexplained, stated rather than guessed at:** the whole-screen-scroll case comes out at 6.0 ms,
below the 11.2 ms an uncached full screen cost. Clipping each band may let Skia reject work cheaply,
or the two screens may not be quite comparable. I have not chased it down, and it is not load-bearing
for any claim above.

**B3 — presentation themes. DONE, with one gap.** Half of this already existed and the plan did not
say so: seven terminal colour presets (Amber, Blue, Cyan, Green, Green Phosphor, Paper White, White)
have long been selectable per tab and per saved connection. What they set is the default foreground
and background and **nothing else** — the sixteen ANSI colours were hard-wired to xterm's values, so
an amber terminal drew a host's `SGR 32` in xterm green on an amber screen. A single-gun CRT could
not do that: it had one phosphor, and sixteen colours arrived as sixteen brightnesses.

`TerminalTheme` (Core) now sits over `TerminalPalette` and decides what is DRAWN, while the palette
keeps saying what an index MEANS. That separation is the point: a screen read back over MCP or a
script still reports the colour the host asked for, not the shade a phosphor theme painted.

A monochrome theme measures each canonical colour's brightness by the BT.601 luma weights and
redraws it at that brightness in the screen's own colour. The weights, not a flat average, because
the eye is far more sensitive to green than to blue and on real hardware blue was the dimmest thing
on the display. Shades blend **from the background** rather than scaling towards black — an amber
screen's background is dark brown, not black, and scaling towards black would put dim text below the
surface it sits on. Only indices 0–15 are themed; the 6×6×6 cube and the grey ramp are addressed by
exact RGB, and remapping those would be a lie about what the host asked for rather than a
presentation choice.

Two new presets, **Amber (single phosphor)** and **Green Phosphor (single phosphor)**. The existing
seven are byte for byte unchanged — picking a colour preset must not silently change how a coloured
host program looks.

Held by `tests\RetroTerm.Tests\Terminal\TerminalThemeTests.cs` (nine, pure logic: a colour theme
declines every index, black lands on the background and white on the phosphor, bright beats dim,
blue is dimmer than green, nothing is drawn darker than the background it sits on) and
`tests\RetroTerm.Tests\Avalonia\TerminalThemeRenderingTests.cs` (four, on real pixels). Six go red
when the mapping is switched off.

The theme change also has to drop the row cache: a theme touches no cell, so the row diff would find
nothing dirty and the screen would keep the old colours until something else happened to change a
row. `ChangingTheThemeRepaintsTheScreen` pins that.

**THE GAP.** A saved connection stores foreground and background as hex, and "Amber" and "Amber
(single phosphor)" have *identical* hex — so the flag cannot be inferred and a saved connection
cannot express it. Persisting it needs a field on the connection profile, which is a schema change
and a deliberate separate step. Today the two new presets are reachable from the tab context menu
and last for that session.

**Not verifiable here:** whether a single-phosphor screen actually looks right. The pixels are
asserted; the taste is Ronny's.

## Phase C — graphics foundation

### Real Tektronix fixtures, and what looking at them found — 2026-08-10

`tests\RetroTerm.Tests\Conformance\tektronix\` holds three streams from **gnuplot's `tek40xx`
driver**, captured through WSL. Third-party bytes: every other graphics test in this repo sends
bytes this codebase also wrote, so a misreading shared by encoder and decoder was invisible. Same
role the libvterm corpus plays for text.

They are **4010-form**, not 4014 — gnuplot's driver is named for the 4010 and its 4014 driver writes
to a terminal rather than a file. A strict subset, so it exercises the decoder honestly but the
fixtures themselves carry no 4014 five-byte address.

That gap is closed by a test built from **Table F-2** instead. A 4014 with the Enhanced Graphics
Module addresses 4096×4096 and sends an Extra Byte between the high and low Y. It shares a tag range
with Low Order Y and is told apart by position — when two arrive in a row, the first is the Extra.
This decoder keeps 10 bits, which is all a 1024×780 surface can show, and reading the Extra as a Low
Order Y that the real one then overwrites lands on exactly the right point, discarding only the two
extra bits. Correct **degradation** rather than a bug: a 4010 program and a 4014 program plot in the
same place. Asserted rather than assumed, because "it happens to work" and "it works" look identical
until someone changes it.

**Two real defects on first contact, neither of which any assertion had caught:**

1. **`ESC FF` lost the first vector of every plot.** Every gnuplot stream opens with it — the Tek
   ERASE SCREEN command, where the C0 *is* the end of the command. A change made earlier that day,
   following ECMA-48's "a mid-sequence C0 lets the sequence continue", meant the `GS` and coordinate
   bytes after it were swallowed as intermediates and a final. It recovered on the next `GS`, so the
   plot still looked right and the corpus tests still passed. **Reverted**; the reasoning is kept in
   `ControlInsideEscapeTests` so it is not re-adopted.
2. **Axis labels landed in a diagonal cascade.** Leaving graph mode must put the text cursor at the
   last plotted point — that is how a Tektronix host writes a label anywhere on screen. Only the
   rendered picture could show this: the corpus tests check labels are printed as text rather than
   punctuation, and they were, just all in the wrong place.

**One thing that is NOT a defect, recorded so it is not chased again.** In `box.tek40xx` the four
parametric lines coincide exactly with the plot border, because gnuplot auto-ranged the axes to the
box. Nothing is missing.

### The 4014 manual answers three things I had listed as unknown — 2026-08-10

`spec\Tektronix\4014-um.pdf` was in the repo the whole time. Reading it turned three guesses into
quotations:

- **The coordinate space.** The glossary: "0Y, 0X being in the bottom left corner and 779Y, 1023X
  being in the top right corner." The 1024×780 bottom-left grid is now quoted, not inferred from
  the ND analysis.
- **Shortened addresses.** "A Graph Mode address of less than four bytes. Can be used when part of
  a new address is the same as part of the one which immediately precedes it." This was implemented
  and explicitly flagged as *derived, not quoted, not confirmed*. It is now confirmed.
- **Incremental plot, Table F-5.** The full encoding: `SP` beam off, `P` beam on, and eight
  directions `D`=N `E`=NE `A`=E `I`=SE `H`=S `J`=SW `B`=W `F`=NW. Plus "the write status does not
  change until a different write command is received", so the beam setting is sticky. **Implemented**,
  replacing the version that swallowed and counted these bytes because the encoding was unknown.

Two more facts noted but not yet used: **Table F-2** gives the 4014's 12-bit "Extra Byte" for 4096
addressing, and **Table F-3** gives the `ESC`+letter vector types — dotted, dot-dashed, short-dash,
long-dash, and the defocused and write-thru beam modes.

**Lesson worth keeping:** three items sat on the "blocked, facts I don't have" list while the
manual that answers them sat in `spec\`. Check what is already in the repo before recording
something as unknowable.

**And one open geometry mismatch.** In that same plot the x-axis labels still cascade. The stream
places all seven at **y=14**, the same row — correct, and our placement honours it. But 780 graphics
units over **24 text rows** puts y=14 on the last row, and the `LF` after each label scrolls the
screen, carrying the earlier ones upward. A real 4014 has **35 alpha lines**, where y=14 sits below
the axis with room to spare. A terminal-geometry mismatch rather than a decoding error, so the fix
is a decision rather than a bug fix: run more rows in Tek mode, or accept that a 4014 plot's labels
crowd on a 24-row terminal. Left as-is.

### Started 2026-08-10

| Block | State |
|---|---|
| `IGraphicsSurface` + in-memory implementation | **DONE** |
| Coordinate transform (`GraphicsViewport`) | **DONE** |
| Plane model + compositor | **DONE** |
| GIN input routing | **DONE — Phase C foundation complete** |

**GIN is the reverse of the keyboard path.** A pointer arrives in surface pixels because that is
where the mouse is; the host wants the terminal's own space. `GinRouter` does not convert — it asks
`GraphicsViewport`, the one owner of that arithmetic. A second copy of the Y flip is exactly how a
crosshair ends up half a screen from where it is pointing.

**It is modal, and that is a guard not a detail.** A terminal is not always reporting: the host arms
it, a crosshair appears, the user acts, one report goes out and the mode ends. A terminal that
reported pointer movement unasked would corrupt every session that never wanted graphics. Moving the
crosshair reports nothing — a real terminal reports when the user ACTS, and streaming a report per
mouse move would flood the line.

**The encoding is pinned to a primary source, not to my reading of one.** The spec carries a worked
example taken from a disassembled ND test program — crosshair at X=512, Y=390, status `0x68`,
response `68 2C 66 30 40 40 40` — and `TheWorkedExampleFromTheSpecEncodesByteForByte` reproduces it
exactly. Eight of the sixteen tests go red when the two low tag bytes are swapped, which is the
mistake that would otherwise put every pick on the diagonal and look almost right.

**A conflict in the spec, recorded rather than papered over.** Its GIN section describes bytes 5–6
of an `ESC ENQ` reply as "CR (optional), EOT (optional)"; its detection section, quoting the
disassembled parsing code at `ram:c751-c775`, reads them as the two ND extension bytes — and its own
worked example ends `40 40`, which is neither CR (`0x0D`) nor EOT (`0x04`). The parsing code and the
example agree, and the detection validator requires **at least 7 bytes**, so the extension-byte
reading is implemented. The "CR/EOT optional" line looks like generic Tek 4014 prose that survived a
copy. **I have not confirmed this against hardware.**

**Planes exist because a terminal's graphics are not one picture.** An ND terminal can hold a
drawing and hide it, draw into a second graphics memory while the first is displayed, and put a
crosshair over the top. That last one is the trap a single shared surface walks straight into: a GIN
cursor scribbled onto the drawing plane is still there after the crosshair moves, and it comes back
when the host reads the plane. Hiding is also not erasing — a hidden plane keeps every pixel, so a
host can show a drawing again unchanged.

**What the compositor deliberately does NOT own is the text.** The architecture review's diagram
feeds the text plane in too, but Core has no glyph rasteriser and is not getting one — fonts,
shaping and the glyph cache all live in the Desktop renderer, and dragging them down to satisfy a
diagram would be the tail wagging the dog. So: this flattens the graphics planes and the GIN
overlay, the renderer draws the text and blits the result on top. The text showing through is what
the transparent pixels are for.

Compositing is source-over alpha. Most terminal graphics are fully on or fully off — both of those
are exact and cheap special cases — but Sixel carries real colours, and getting the general case
right here costs a few lines instead of a special case in every protocol that needs it.

**The logical space is 1024 × 780, not "0..4095".** Verified in
`spec\Tektronix\nd-graphic-terminal-analysis.md` rather than taken from this document's own earlier
wording, which said "0..4095-style" loosely. ND graphics and Tektronix 4010/4014 both put the
**origin at the bottom left with Y increasing upward** — the opposite of every surface, window and
bitmap.

**One owner for the transform.** `GraphicsViewport` converts both ways and nothing else does the
arithmetic. Two copies of a Y flip in two modules is how a crosshair ends up half a screen from the
line it is pointing at, with each module looking correct on its own. It also owns aspect ratio: a
Tek screen is roughly 4:3, and stretching it to fill a wide window turns circles into ellipses, so
the drawing keeps its shape and the spare width becomes margin.

Off-space points map to off-surface coordinates rather than being refused — a protocol is entitled
to draw off the edge and the **surface** clips. Refusing at the transform would push the special
case into every caller and lose the part of a line that does land. The GIN direction clamps instead,
because a crosshair report of "−3" is not something a host can act on.

**The line symmetry bug.** Plain Bresenham is not symmetric: when accumulated error lands on a tie,
the pixel chosen depends on which end the walk started from, so a line drawn B→A sat one pixel off
the line drawn A→B. A shape's outline would shift depending on the order a protocol emitted its
vectors, with nothing in the picture to explain why. Fixed by drawing from a canonical end rather
than by tuning the tie-break. Caught by `ALineIsTheSameDrawnFromEitherEnd`, which was the one
failure out of twenty-two on first run.

### How a host tells an ND terminal from a real Tek 4014

Raised by Ronny 2026-08-10 and confirmed in `spec\Tektronix\nd-graphic-terminal-analysis.md`.
Recorded here because it decides what the ND graphics module must emit, and it is not derivable
from any code in this repo.

The hook is the **standard** Tek identification request, `ESC ENQ` (`0x1B 0x05`). The difference is
the length of the reply:

| Terminal | Reply |
|---|---|
| Real Tek 4014 | **5 bytes** — status + `HiY LoY HiX LoX` |
| ND graphic terminal | **7 bytes** — those 5, plus **2 ND extension bytes**, each masked to 7 bits |

The two extension bytes identify the **model**: ND-324/Notis, ND-325/Net, ND-246, ND-285, ND-320,
ND-322. So a host learns both "this is an ND" and "which ND" from one standard request.

The ND test program's full detection sequence is `US`, `ESC "13;10l`, `ESC "5d`, `ESC ENQ` → the
7-byte reply. Once that validates, it probes features one at a time with `ESC 'a'` … `ESC 'z'`
(function at `ram:c83f`).

**Consequences for us:** the ND module must emit 7 bytes where a Tek module emits 5, and the model
bytes have to be selectable so a host can be told which ND it is talking to. Both belong to the ND
graphics module, not to the shared surface/viewport blocks — which is the point of keeping Tek and
ND as separate modules over shared foundations.

## Phase C — graphics foundation (original entry)

`IGraphicsSurface` + in-memory implementation + compositor + coordinate transform + GIN routing +
headless surface tests. Unchanged from
`docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md`.

## What I cannot verify

Stated so nobody burns turns guessing:

- **Sound.** Whether a beep is audible and instant. Needs a listening test.
- **Real hardware.** ND-100/ND-500, real serial keyboards, real TDV terminals.
- **Appearance.** Whether a colour looks right, whether a layout reads well.
- **Wall-clock timing.** Never slept a test into passing; the blink tick is driven directly.
