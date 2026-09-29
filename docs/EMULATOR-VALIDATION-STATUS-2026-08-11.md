# What is validated and what is only inferred — 11 August 2026

Every emulator in this repository is listed below with **the evidence that backs it**, not with how
finished it feels. The distinction is the point of this document: a test I wrote myself proves the
code does what I believed, which is not the same as proving it does what the real terminal did.

Three classes of evidence are used:

- **Document** — a real vendor manual, spec or ROM dump held in this repository.
- **Outside artifact** — a third-party conformance corpus or a genuine captured data stream.
- **Inferred** — written from model knowledge and pinned with tests written by the same author.
  Self-consistent, unproven.

## The terminals

| Terminal | Evidence | Detail |
|---|---|---|
| ANSI / ECMA-48 core | **Outside artifact** | libvterm's own conformance scripts, vendored into the suite. 97 disagreements down to 8, and 7 of those are deliberate representation differences. |
| TDV2200 / 2215 / 1200 | **Document** | OCR'd Tandberg manuals in `spec\TDV2200\`, the 2115 control-sequence PDFs in `spec\TDV2200\Testing2215\`, a TDV2215 ROM memory dump (`TDV2215_Memory.txt`), and fonts extracted from real bitmaps. The modes, queries and reports, each with its manual page, are in `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`. |
| TDV2200 graphics (ND `ESC "`) | **Document, partial** | `spec\TDV1200\tdv_1200_graphics_library_extended.md` names the modes. Six are implemented (6, 8, 9, 10, 17, 24). The rest are COUNTED, not implemented, because the spec names them without their parameter meanings — see the counters on the emulator. |
| Tektronix 4010 / 4014 | **Document + outside artifact** | `spec\Tektronix\4014-um.pdf`, plus vendored genuine gnuplot Tektronix streams. Those captures disproved one change and identified one non-bug. |
| Sixel graphics | **Outside artifact** | hackerb9's vt340test, fetched on demand — 16 `.six` streams paired with captures of what a REAL VT340 and VT240 made of them. Walked on 2026-08-17. Every stream decodes; looking at the pictures found two shipped defects (the raster aspect ratio thrown away, and whitespace ending a colour definition), both fixed. The by-hand cases are in `docs\manual-tests\M6-SIXEL-AND-REGIS.md`. |
| ReGIS graphics | **Document** | `spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf` — Volume 2, 305 pages, fetched 2026-08-17. All ten commands implemented as of 2026-08-18; the options deliberately left out are counted, not guessed at. The record, read against the manual's own chapters, is `docs\REGIS-GAP-2026-08-17.md`. |
| Sixel printing | **Document** | `spec\DEC\EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf` — "Digital ANSI-Compliant Printing Protocol", 234 pages — plus the Graphics Programming manual's printing chapter and DEC STD 070 §7.8. Built 2026-08-17: `src\RetroTerm.Core\Terminal\Printing\` and `src\RetroTerm.Desktop\Printing\`; the design is `docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md`. |
| xterm / xterm-256color | **Document** | `spec\DEC\xterm-ctlseqs.txt`, the authoritative control-sequence list from invisible-island.net. **Walked end to end on 2026-08-17** — all 224 sequences, written up at `docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`, each marked implemented / deliberately not / missing. |
| VT100, VT102, VT220 | **Document for what they ARE; outside artifact for what they DO** | No manual for these is held — but the VT330/VT340 manual's alias-DA table gives their identities exactly, and all three now match it (see `DeviceAttributesAgainstTheManualTests`). Their *behaviour* rests on the two corpora, which is a stronger backing than the word "inferred" suggests: the xterm.js screens were captured from a real xterm and all 76 now match, and libvterm's 43 scripts pass 393 assertions with 4 recorded disagreements. Both corpora exercise the VT100/VT102 core almost entirely. |
| VT52 | **Inferred** | The weakest row in this table, and it is small: ESC A–K cursor and erase, ESC Y addressing, ESC Z identify, graphics mode, and the way out to ANSI. No document, and no corpus covers VT52 mode. |
| VT420 | **Document** | `spec\DEC\VT420-Programmer-Reference-EK-VT420-RM-002.pdf`, **walked on 2026-08-17**: chapter 6 (the page format), chapter 8 (editing and erasing), chapter 12 (reports) and the DECRQSS list. |
| VT320, VT330, VT340, VT240 | **Document** | An OCR of the VT330/VT340 Text Programming manual (EK-VT3XX-TP-001), held outside this repository; its graphics volume is in the repository as `spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf`. Walked on 2026-08-17: the status line, soft character sets, the character-set designators and every DA reply. Page memory is the one part of it not yet gone through. |
| IBM 3270 | Not started, deliberately | No 3270 manual or data-stream reference is held here. Building one from recollection would invent EBCDIC orders, AID codes and structured fields. Deferred until everything else is implemented AND validated. |

## Where the 17 August 2026 walk-through went

This file used to carry the notes from walking the VT420, VT330/VT340 and xterm documents on
17 August 2026 (DECSTR, DECRQCRA, the private DSRs, the status line, soft character sets, the
page format, the DECRQSS list). Every finding in them is now either fixed and pinned by a named
test, or listed as open work in `docs\PLAN.md`, so the notes were removed in the documentation
clean-up of September 2026 rather than left to go stale. The xterm sequence list is in
`docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`; the ReGIS record is `docs\REGIS-GAP-2026-08-17.md`.

## What no test in this repository can judge

Listed in `docs\PLAN.md`, Phase 4: whether a beep sounds right, whether a colour looks right on a
real monitor, real ND / TDV hardware behaviour, and anything depending on the wall clock.

Rendered PNGs ARE examined by eye, and that practice has caught three defects no assertion saw - a
red/blue channel swap, axis labels in a diagonal cascade, and plots drawn in the wrong phosphor.
