# Should text live in the graphics bitmap, like a real VT340?

**Full path:** `docs\TEXT-BITMAP-STUDY.md`
**Plan item:** `docs\PLAN.md`, Phase 1.1
**Written:** 19 August 2026. **Status: a study. No code has been written.**

Ronny asked for this before anything is built. It says what the change would buy, what it would
break, roughly what it would cost, and which of the three ways forward I would take.

---

## 1. The question in one paragraph

On a real VT340 there is **one** picture memory, four bits deep. Characters and ReGIS drawings and
Sixel images all land in the same 800 by 480 grid of 4-bit codes, and a colour map turns each code
into a colour as the beam sweeps. Here, text is a grid of `TerminalCell` structs that the Avalonia
renderer draws with real fonts, and graphics are separate planes composited over the top. The
question is whether to move text into the bitmap.

---

## 2. What we do today — verified, with the lines

| Piece | Where | What it does |
|---|---|---|
| Text | `src\RetroTerm.Core\Terminal\Buffer\TerminalBuffer.cs` | 2-D array of `TerminalCell` — character, attributes, colours |
| Graphics | `src\RetroTerm.Core\Terminal\Graphics\GraphicsCompositor.cs` | Flattens the visible planes into one ARGB surface |
| The join | `TerminalRenderer.cs:606` `DrawGraphicsPlanes` | Text is drawn first; the composite is **stretched** over the whole terminal area |
| The reason | `GraphicsCompositor.cs:9-14` | Core has no glyph rasteriser. Fonts, shaping and the glyph cache are all in the Desktop layer |

The plane is **800 by 480** (`RegisDecoder.DefaultWidth` / `DefaultHeight`, lines 39 and 44), and
`TerminalEmulatorBase.cs:6218` already derives a cell-to-pixel mapping from it:

```
GraphicsCellWidth  = GraphicsPlaneWidth  / Width      // 800 / 80 = 10
GraphicsCellHeight = GraphicsPlaneHeight / Height     // 480 / 24 = 20
```

**On the standard 80 by 24 that mapping is exact, and it is exactly the VT340's 10 by 20 cell.**
That matters for option B below. On any other geometry it is a division with a remainder, and the
existing comment says so plainly.

The indexed machinery a bitmap design needs **already exists**, built this month for the ReGIS write
controls: `InMemoryGraphicsSurface` carries a parallel `_codes` plane of 4-bit values, an
`IndexedMap`, a `PlaneMask`, and the complement/erase/replace writing modes that operate on the code
rather than the colour.

---

## 3. What the change would buy

This is the honest list. It is shorter than I expected when I started.

1. **The bitplane recolour trick (M6.4h).** hackerb9's `faketextcolor` stream clears bitplanes across
   a run of characters and gets genuinely multicoloured text out of a terminal that cannot otherwise
   do it. We paint a black bar over the letters instead. This is the only case in any of the three
   outside corpora that we get visibly wrong for this reason.
2. **A colour-map change would repaint text too.** Today `M(a)` repaints what is already drawn on the
   planes; text ignores it, because text colours come from the theme and the cell attributes.
3. **A graphics hardcopy would include the text**, if a VT340 really prints its whole bitmap.
   **I have not verified that it does** — I have no page of the manual saying so. Marked as an
   assumption, not a benefit I am claiming.

That is it. Everything else ReGIS, Sixel and Tektronix do is already right: all four vt340test
hardware photographs agree with what we draw, three of them above 99%.

---

## 4. What it would cost

### 4.1 The subsystems that stop working as written

| Subsystem | Why the bitmap breaks it |
|---|---|
| **Selection and copy** | `SelectionManager.GetSelectedText()` walks cells and returns a string. Pixels have no characters. The cell buffer would have to survive **anyway**, purely to answer "what does this say" |
| **Search** | Same reason. `SearchHighlightTests` (7 tests) matches text in the buffer and highlights cell ranges |
| **Scrollback** | Scrollback is a list of cell rows. A bitmap scrollback is 800×480×4 bits ≈ 192 KB **per screen**; the current default holds thousands of lines of cells |
| **Fonts** | Two renderers, `SystemFontRenderer` and `BitmapFontRenderer`, with glyph-outline caches. A bitmap needs a rasteriser **inside Core**, which `GraphicsCompositor.cs:9` was written specifically to refuse |
| **Sharpness** | The renderer caches at **device** pixels on purpose (`TerminalRenderer.cs:447-449`) so glyphs are rasterised at their final size. Text rasterised once into an 800×480 bitmap and then stretched to the window is a blurred copy — the one defect only an eye catches |
| **Double width and height** | `TerminalRenderer.cs:1020` and `1214` scale the glyph geometry. In a fixed bitmap, double-height text is a 2× pixel stretch, which is what the real hardware did and looks worse than what we have now |
| **Soft fonts (DECDLD)** | Already work through the glyph cache. Would need re-plumbing into the Core rasteriser |
| **Phosphor themes** | Amber/green/white currently recolour text instantly by changing brushes. In a bitmap, text pixels carry a code, so a theme change becomes a colour-map change — this one actually gets *simpler* |
| **The dirty-row cache** | The whole design compares cells row by row (`RowAppearance`, `RowCellsChanged`). With text in a bitmap there are no rows to compare. `DirtyRowCacheTests` exists because six of eight cases showed stale pixels without it |
| **Speed** | The cache comment records a **full screen repaint costing about forty times a cached blit**. Every text write would become a rasterise-into-bitmap. `RenderCostBenchmarkTests` is the measurement that would have to be re-run |
| **Resize and reflow (Phase 1.2)** | Direct conflict — see below |

### 4.2 The direct conflict with the work you already approved

Phase 1.2 says the window drives the terminal size and wrapped paragraphs reflow. That is a
**cell-grid** idea: it only means anything if text is characters in rows that can be re-broken at a
new width. A fixed 800×480 bitmap has one geometry. Doing both means keeping the cell buffer as the
truth and treating the bitmap as a second copy — which is option C's real shape, and the reason it
is expensive.

### 4.3 The tests

Counted, not guessed:

- **309** `[AvaloniaFact]`/`[AvaloniaTheory]` in `tests\RetroTerm.Tests\Avalonia\`.
- **91** of them are renderer-internal — dirty-row cache, glyph cache, render cost, scaling, search
  highlight, double size, blink, soft fonts, reverse screen, themes, cursor blink, rendered-output
  validation. **These are the ones that would need rewriting**, because they assert on how the
  renderer draws text, not on what the terminal decided.
- **145** assertions across **11** Avalonia test files read the cell buffer directly (`GetCell(`,
  `frame[`). Most survive if the cell buffer survives.
- **64** test files across the whole suite read cells. If the cell buffer stayed, they all keep
  working; if it did not, they would all have to be rewritten against pixels, which would be a bad
  trade — a pixel assertion cannot tell you the terminal put an `A` there.

`TerminalRenderer.cs` is **1404** lines. The bulk of it — the cache, the row diff, the glyph
drawing, the cursor — is about drawing text from cells.

---

## 5. Three ways forward

### Option A — Do nothing, keep the limit written down

M6.4h already explains it, shows both artefacts, and tells the reader **not** to file it as a
defect. Cost: nothing. Gives up the one wrong picture.

### Option B — Give text a colour code, and let plane operations touch it

Keep the cell buffer and the renderer exactly as they are. Add a 4-bit **colour code** to the cell
(text is `0111`, as on the hardware). When ReGIS writes with a plane mask in a mode that only clears
or complements planes, map the affected rectangle to cells through the mapping that already exists at
`TerminalEmulatorBase.cs:6218`, and apply the same plane operation to the cell's code instead of
painting over it. The renderer resolves the code through `IndexedMap` when drawing the glyph.

This reproduces the trick — multicoloured text on the real geometry — without a rasteriser, without
touching selection, scrollback, fonts, sharpness, reflow or the row cache. Themes get simpler.

**What it does not give you:** anything below cell resolution. A plane clear across half a character
recolours the whole character. On 80×24 the hardware trick is cell-aligned anyway, so the picture
matches; on other geometries it is an approximation, and I would say so in the manual case rather
than pretend otherwise.

**Rough size:** one field on `TerminalCell`, a rectangle-to-cells helper, a branch in the ReGIS write
path, a colour lookup in `DrawRow`, plus tests. **Two to four sessions.** No existing test should
need rewriting; a handful of new ones, and the M6.4h comparison becomes a pass instead of a recorded
limit.

### Option C — Text really lives in the bitmap

A glyph rasteriser in Core, text written as codes into the shared plane, the renderer reduced to a
blit, the cell buffer kept anyway for selection, search, scrollback and reflow — so **two**
representations to keep in step, which is the trap this repo already has a rule about.

**Rough size:** the 91 renderer-internal tests rewritten, `TerminalRenderer` largely rebuilt,
`RenderCostBenchmarkTests` re-measured, and a sharpness regression to argue about. **Weeks, not
sessions.** It buys item 1 of section 3, which option B also buys, plus item 2, which nothing else
demands.

---

## 6. Recommendation

**Option B.** It gets the one picture we render wrong, at a fraction of the cost, and it does not
fight the resize-and-reflow work you have already approved. Option C spends weeks to buy what B buys
in days, and pays for it with two copies of the same truth, blurrier text, and a rewrite of the
cache that makes the terminal fast.

If B's cell-resolution limit is not good enough for you, the honest answer is C, not a middle. I
would want to hear that from you before starting either.

**Second choice: A.** Doing nothing is a perfectly defensible answer here — the limit is documented,
every ReGIS command in that stream executes correctly, and no host anyone actually uses relies on
the trick.

---

## 7. What I could not verify

Said plainly rather than hedged:

- **Whether a VT340 graphics hardcopy includes text.** No page in the manuals here says either way.
- **Whether any real host uses the bitplane trick.** The only example anywhere in the three corpora
  is hackerb9's deliberate demonstration of it.
- **What a VT340 does when a plane operation covers part of a character.** Physically it recolours
  those pixels only. Whether any real stream ever does it, I do not know — which is why option B's
  limit may cost nothing in practice.
