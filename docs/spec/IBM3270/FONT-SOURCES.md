# IBM 3270 font sources

Research only, 22 November 2026. Nothing was built and no ROM file was downloaded.
Anything marked NOT VERIFIED was not read or checked by hand.

## What a real 3270 cell looks like

From GA33-3056-0 (Color and Programmed Symbols, 3278/3279/3287), in `IBM-manuals/`:

- 3279: characters sit in a 9 x 12 dot cell; capitals use 7 x 9 of it.
- Programmed symbols (loadable glyphs) are 9 x 12 or 9 x 16 dots, 18 bytes each.

## 1. Real IBM ROM dumps

### IBM 3270 PC (5271) character ROM - the only real dump found

- **Where it is listed:** MAME source, `src/mame/pc/ibmpc.cpp` (lines about 216-222),
  https://github.com/mamedev/mame/blob/master/src/mame/pc/ibmpc.cpp
  - `1504161.bin` - pixel columns 0-7, 8 KB, CRC d9246cf5, SHA1 2eaed495893a4e6649b04d10dada7b5ef4abd140
  - `1504162.bin` - pixel column 8 (the 9th column), 8 KB, CRC 59e1dc32, SHA1 337b5cced203345a5acfb02532d6b5f526902ee7
- **In MAME it is only a comment.** No driver loads it and MAME does not ship it. MAME only
  records that the dump exists.
- **Where the files can be downloaded: NOT VERIFIED.** Not searched for.
- **Cell:** 9 x 14 (same as the IBM MDA), from https://www.seasip.info/VintagePC/5271.html.
  That page shows the font as pictures only (no download). It says the order is the 3270
  "CG" order, close to x3270's, but the last 64 characters differ.
- **APL and box drawing: NOT VERIFIED.**
- **Licence:** none. This is IBM's code, IBM copyright. Putting the ROM bytes, or a font
  converted one to one from them, in a public repository is copying IBM's work without
  permission. MAME's BSD-3 licence covers MAME's source, not the ROM. It can be used only
  as a file the user supplies himself (the way MAME and RetroCore treat ROMs), never shipped.
- **Note:** this is the 3270 PC, a PC display that imitates a 3278/3279. It is not the
  font of a 3278/3279 terminal itself.

### Terminals with no dump found

3277, 3278, 3279, 3178, 3179, 3180, 3191, 3192, 3194, 3290: no public dump found.
This comes from searching only (MAME source, web search, VCF/classiccmp/vogons search
results), so it does not prove none exists.

- bitsavers `bits/IBM/` and `pdf/ibm/3270/` could not be listed (403 / empty answer).
  NOT VERIFIED.
- lowobservable on GitHub (`coax`, `oec`, `pytn3270`): code search for "character
  generator" and "font" found no glyph table. The `oec` sources were not read by hand.
  NOT VERIFIED.
- MAME has 3270 clones, not IBM: Informer 207/376 (`src/mame/informer/informer_207_376.cpp`)
  and Beehive DM3270 (`src/mame/beehive/microb.cpp`), and the Ericsson SAD 8852 3270
  adapter (`src/devices/bus/isa/eis_sad8852.cpp`). Their character ROMs were not read.
  NOT VERIFIED. They are not IBM fonts in any case, and their ROMs carry the same
  "not ours to ship" problem.

## 2. Hand-made copies (open licence)

### x3270 bitmap fonts (BDF)

- **Where:** the x3270 source tree, folder `x3270/` (local copy kept beside the Hercules
  sources). Upstream: https://x3270.miraheze.org/ and https://github.com/pmattes/x3270
  (upstream URL NOT VERIFIED in this session).
- **Origin:** Georgia Tech's 3270tool (1989), later Jeff Sparkes (1990), then Paul Mattes.
  The rbanffy README says it was "hand-copied from a 3270 series terminal". Which terminal:
  NOT VERIFIED.
- **Licence:** BSD-3 style (Mattes, Sparkes, GTRC), in each BDF header and in `LICENSE.md`.
  `3270-12.bdf` has an older MIT-style permission notice. Both allow shipping with the
  copyright notice kept.
- **Coverage, from the BDF headers:** page 0 = EBCDIC US-International set, page 1 =
  APL/APL2 set, page 2 = first 16 DEC line-drawing characters (box drawing). Order is
  "3270 CG" order, not EBCDIC and not Unicode, so a mapping table is needed.

| File | Cell (w x h) | Glyphs | Note |
|---|---|---|---|
| `3270.bdf` / `3270b.bdf` | 9 x 14 | 431 | full APL set; b = bold |
| `3270h.bdf` | 9 x 14 | 256 | |
| `3270-12.bdf` / `3270-12b.bdf` | 7 x 12 | 314 | APL marked incomplete |
| `3270-20.bdf` / `3270-20b.bdf` | not checked | not checked | NOT VERIFIED |
| `3270gt8.bdf` | 5 x 8 | 312 | |
| `3270gt12.bdf` / b | 7 x 12 | 312 | |
| `3270gt16.bdf` / b | 9 x 16 | 312 | APL marked incomplete |
| `3270gt24.bdf` / b | 14 x 24 | 312 | |
| `3270gt32.bdf` / b | 18 x 32 | 312 | |
| `3270gr.bdf` | 10 x 20 | 312 | |

### rbanffy/3270font (TrueType remake)

- **Where:** https://github.com/rbanffy/3270font. A copy of the TTFs also sits in the local
  x3270 tree under `wc3270/` (`3270-Regular.ttf`, `3270Condensed-Regular.ttf`,
  `3270SemiCondensed-Regular.ttf`).
- **Origin:** made from the x3270 font (see above). Outline font, source `3270_HQ.sfd`.
- **Licence:** `LICENSE.txt` is BSD-3 style (Banffy, Mattes, Russell, Altenbern, Sparkes,
  Georgia Tech). GitHub shows "NOASSERTION". Shipping allowed with the notice kept.
- **Cell size and APL coverage:** NOT VERIFIED.
- An outline font, so it does not give the exact dots of a terminal.

## 3. Drawing our own from the IBM manual

GA27-2837-9 (3270 Character Set Reference, April 1987), in `IBM-manuals/`, 340 pages.
Two pages were rendered at 150 dpi and looked at: page 224 (Figure 9-2, Austrian/German
code chart) and page 241 (Figure 9-19, Data Analysis APL 2-byte code chart).

- **The charts are typeset, not dot grids.** Each character is printed in a normal
  print typeface inside the code table. They show WHICH character sits at each code
  and roughly its shape (including the APL symbols, underscored letters, super/subscripts
  and the box-drawing corners), but not the dots.
- The scans are 400 dpi, 1-bit, and clear enough to read every character.
- **So:** usable as the map of code to character, and as a shape guide for hand drawing.
  Not usable to copy dot patterns from. A text search for "dot"/"matrix" in this manual
  found no dot-cell drawings; only the 9 x 12 / 9 x 16 sizes in GA33-3056-0 above.
- Copying IBM's printed code tables into a mapping table is copying facts (which
  character is at which code), which is what every 3270 emulator does. A glyph we draw
  ourselves on a 9 x 14 or 9 x 16 grid is our own work.

## PROPOSAL

1. **Draw our own bitmap font**, 9 x 16 (matches the IBM programmed-symbol cell) and
   optionally 9 x 14 (the 3270 PC cell), using the GA27-2837-9 charts as the list of
   characters and the shape guide. This is the only route that gives "our own" font with
   no licence question at all.
2. **Until that is done, or as a check,** use the x3270 BDF fonts (`3270.bdf` 9 x 14 with
   full APL; `3270gt16.bdf` 9 x 16), with the BSD-3 notice kept. They are a hand copy of a
   real terminal, open licence, and include APL and line drawing.
3. **Never commit the 3270 PC ROM** (`1504161.bin`/`1504162.bin`) or a font converted from
   it. If wanted, RetroTerm could load it as an optional user-supplied file to show the
   real 3270 PC dots.

## Open questions

- Is a font drawn by us wanted, or is the x3270 BDF (open licence, not ours) good enough?
- Which cell: 9 x 14 (3270 PC), 9 x 16 (3278/3279 programmed symbols) or both?
- Should RetroTerm offer loading a user-supplied 3270 PC ROM at all?
- Where can the 5271 ROM files be found, and do they include APL and box drawing?
  NOT VERIFIED.
- Which terminal did the Georgia Tech 3270tool font come from? NOT VERIFIED.
