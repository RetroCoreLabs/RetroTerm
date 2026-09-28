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
| TDV2200 / 2215 / 1200 | **Document** | OCR'd Tandberg manuals in `spec\TDV2200\`, the 2115 control-sequence PDFs in `spec\TDV2200\Testing2215\`, a TDV2215 ROM memory dump (`TDV2215_Memory.txt`), and fonts extracted from real bitmaps. |
| TDV2200 graphics (ND `ESC "`) | **Document, partial** | `spec\TDV1200\tdv_1200_graphics_library_extended.md` names the modes. Six are implemented (6, 8, 9, 10, 17, 24). The rest are COUNTED, not implemented, because the spec names them without their parameter meanings — see the counters on the emulator. |
| Tektronix 4010 / 4014 | **Document + outside artifact** | `spec\Tektronix\4014-um.pdf`, plus vendored genuine gnuplot Tektronix streams. Those captures disproved one change and identified one non-bug. |
| Sixel graphics | **Outside artifact** | hackerb9's vt340test, fetched on demand — 16 `.six` streams paired with captures of what a REAL VT340 and VT240 made of them. Walked on 2026-08-17. Every stream decodes; looking at the pictures found two shipped defects (the raster aspect ratio thrown away, and whitespace ending a colour definition), both fixed. See the notes below. |
| ReGIS graphics | **Document** | `spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf` — Volume 2, 305 pages, fetched 2026-08-17. Five of ten commands implemented; the rest counted, not guessed at. Gap written up at `docs\REGIS-GAP-2026-08-17.md`, which was written from a second-hand command table and should be re-read against the manual now that it is held. |
| Sixel printing | **Document** | `spec\DEC\EK-PPLV2-PM.B01_Level_2_Sixel_Programming_Reference.pdf` — "Digital ANSI-Compliant Printing Protocol", 234 pages — plus the Graphics Programming manual's printing chapter and DEC STD 070 §7.8. No code yet; designed at `docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md`. |
| xterm / xterm-256color | **Document** | `spec\DEC\xterm-ctlseqs.txt`, the authoritative control-sequence list from invisible-island.net. **Walked end to end on 2026-08-17** — all 224 sequences, written up at `docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`, each marked implemented / deliberately not / missing. |
| VT100, VT102, VT220 | **Document for what they ARE; outside artifact for what they DO** | No manual for these is held — but the VT330/VT340 manual's alias-DA table gives their identities exactly, and all three now match it (see `DeviceAttributesAgainstTheManualTests`). Their *behaviour* rests on the two corpora, which is a stronger backing than the word "inferred" suggests: the xterm.js screens were captured from a real xterm and all 76 now match, and libvterm's 43 scripts pass 393 assertions with 4 recorded disagreements. Both corpora exercise the VT100/VT102 core almost entirely. |
| VT52 | **Inferred** | The weakest row in this table, and it is small: ESC A–K cursor and erase, ESC Y addressing, ESC Z identify, graphics mode, and the way out to ANSI. No document, and no corpus covers VT52 mode. |
| VT420 | **Document** | `spec\DEC\VT420-Programmer-Reference-EK-VT420-RM-002.pdf`, **walked on 2026-08-17**: chapter 6 (the page format), chapter 8 (editing and erasing), chapter 12 (reports) and the DECRQSS list. See the notes below. |
| VT320, VT330, VT340, VT240 | **Document** | Ronny's OCR of the VT330/VT340 Text Programming manual (EK-VT3XX-TP-001) at `d:\OCR\ai\EK-VT3XX-TP-001_VT330_VT340_Text_Programming_Mar87\`. Walked on 2026-08-17: the status line, soft character sets, the character-set designators and every DA reply — see the notes below. Page memory is the one part of it not yet gone through. |
| IBM 3270 | Not started, deliberately | No 3270 manual or data-stream reference is held here. Building one from recollection would invent EBCDIC orders, AID codes and structured fields. Deferred until everything else is implemented AND validated. |

## VT420 chapter 12 — Reports — walked 17 August 2026

The first chapter of the VT420 manual to be gone through line by line. What it found:

- **`CSI ? Ps n` was not recognised as a question at all.** It fell through to the private-*mode*
  handler, which reads any final byte other than `h` as a reset. So `CSI ? 6 n` (DECXCPR, "where is
  the cursor?") silently **turned origin mode off**, and `CSI ? 25 n` ("are the function keys
  locked?") **hid the cursor**. Both are sequences real software sends. Fixed, with the two defects
  pinned as their own tests.
- **DECRQCRA did not exist.** `CSI Pid;Pp;Pt;Pl;Pb;Pr * y` now answers with the documented
  `DCS Pid ! ~ D..D ST`, over any page, honouring origin mode and the manual's page rules.
- The other private DSRs now answer instead of staying silent: printer, UDK lock, keyboard, locator,
  macro space, macro checksum, data integrity and multiple-session status. Where the hardware is
  simply not here, the manual's own "there is none" reply is what goes back.

**One thing the manual does NOT say, written down rather than guessed:** it defines the checksum
request and reply exactly and says nothing whatever about how the number is computed. The only
description held here is xterm's `ctlseqs.txt` entry for XTCHECKSUM, which states the default by
saying what each bit changes — negate the result, omit blanks, mask each character to 8 bits,
include the video attributes. The first three are followed. **The fourth is not**, because no
document here gives the weight each attribute adds, and inventing them would disagree with xterm
anyway. The sum is over character codes only, which is what a host gets from xterm when it sets
XTCHECKSUM bit 1. See `ReportRectangleChecksum` and `ChecksumAndPrivateReportTests`.

## The terminals with no manual — settled 17 August 2026

P1.5 asked for either a document or an honest note per terminal. Both, as it turns out.

**Their identity is documented after all.** The VT330/VT340 manual's "Alias Primary DA Responses
From the VT300" table lists what a VT300 answers when set up to identify as an earlier terminal —
VT100, VT101, VT102, VT125, VT131, VT220 and VT240. DEC would hardly print an alias that did not
match the real machine, so that table is a document for terminals whose own manuals are not held
here. **Ours match it**: the VT100 exactly, the VT102 exactly, the VT220 and VT240 as honest
subsets whose omissions each name a capability that is genuinely absent. Pinned in
`DeviceAttributesAgainstTheManualTests`, which also pins the *shape* of every reply — a DA answer
is a promise, and a host that gets a malformed one cannot tell it from another report entirely.

**Their behaviour rests on the two corpora, and that is worth more than "inferred" sounds.** The
xterm.js screens were captured from a real xterm and **all 76 now match**; libvterm's 43 scripts
pass 393 assertions with 4 recorded disagreements. Between them they exercise the VT100 and VT102
core almost entirely — cursor movement, erasing, editing, scrolling regions, modes, tabs, wrapping,
character sets. What they do *not* cover is the VT220's own additions beyond that core, and those
rest on the VT420 manual where the two overlap, which is most of them.

**The VT52 is the weakest row in the table, and it is honest to say so.** No document, and no corpus
covers VT52 mode. It is also small: ESC A–K cursor and erase, ESC Y addressing, ESC Z identify,
graphics mode, and the way back to ANSI. If a manual ever turns up it is an afternoon's work to
check.

## xterm ctlseqs — the sweep, started 17 August 2026

224 sequences were pulled out of `spec\DEC\xterm-ctlseqs.txt` — 117 CSI, 93 ESC, 14 DCS/OSC — and
checked against the dispatch. The first pass found the largest single gap in the emulator so far.

**DECSTR — `CSI ! p`, the soft terminal reset — was not implemented at all**, and the reason it was
invisible is a **parser defect** underneath it: `!` was being recorded as a *private marker*. The
private-marker range is 0x3C to 0x3F — `<`, `=`, `>`, `?` — and nothing else. 0x21 is an
*intermediate*, so `CSI ! p` is a final `p` carrying one intermediate. Recorded as a marker, the
sequence arrived with no intermediates at all and matched nothing.

That matters out of all proportion to its size: **every curses program sends DECSTR on the way
out**, and so does `tput`. A session left by vim kept whatever modes vim had set — application
cursor keys, origin mode, a scrolling region, a hidden cursor.

It is now built from table 13-1 of the VT420 manual, and the note beside that table is the half
that matters most: *"DECSTR affects only those functions listed in Table 13-1."* So it resets the
modes and the rendition and **does not** clear the screen, empty the scrollback, forget a downloaded
character set or the user-defined keys, or move the cursor. Four tests pin what it must not do.

**One disagreement, written down rather than split:** xterm's DECSTR clears the left and right
margins. They are not in table 13-1, and the note says the list is the whole of it, so they survive
here. No document held here says otherwise.

The rest of the sweep — the remaining CSI entries, then ESC and DCS/OSC — is still to do.

## VT330/VT340 — character sets and device attributes — walked 17 August 2026

**The VT240 was refusing Sixel it can draw.** Its DA reply claimed ReGIS and not Sixel, and the
test that pinned it said why: *"no source in this repository confirms a VT240 had it, and a DA reply
is not the place to guess."* A source does now — the VT330/VT340 manual lists the alias replies a
VT300 sends when told to identify as an earlier terminal, and its VT240 line is
`CSI ? 62; 1; 2; 3; 4; 6; 7; 8; 9 c`. So a host asking "can you do Sixel?" was told no, and
`img2sixel` would not send an image to a terminal that can draw one. Fixed, and the test rewritten
to the new evidence. **This is exactly the inferred-to-documented move P1 exists for.**

The other DA replies were *checked and found honest*: every one of them is a subset of what the
manual documents, and each omission is a capability this emulator really does not have — 2 (printer
port), 9 (national replacement character sets), 13, 15, 16, 18 and 19.

**Character-set designators: three of the manual's fifteen.** Table 5-2 lists ASCII `B`, DEC
Supplemental `% 5`, ISO Latin-1 `A` in the 96-character position, user-preferred `<`, DEC Special
Graphic `0`, DEC Technical `>`, and twelve national replacement sets. Only ASCII, British and DEC
Special Graphic exist here — and **no profile claims the rest**, so no host is being misled: none of
the DA replies carries 9 or 15. An unknown designator falls back to ASCII deliberately, and the
reason is now written at the call site: a host asking for Norwegian and getting line-drawing glyphs
for every letter would be far worse than getting unaccented ones.

## VT330/VT340 chapter 5 — soft character sets — walked 17 August 2026

**Pcss changes what Pcn means, and that was being ignored.** The manual is explicit — "The value of
Pcss changes the meaning of the Pcn (starting character) parameter above" — and then gives both
readings outright:

- Pcss 0, a 94-character set and the default: Pcn 1 is position 2/1, Pcn 94 is 7/14, and "the
  terminal ignores any attempt to load characters into the 2/0 or 7/15 table positions".
- Pcss 1, a 96-character set: Pcn 0 is position 2/0, Pcn 95 is 7/15.

Both are the same arithmetic on the code table, but the store here counts from the *first character
of the set*, and a 94-character set starts one place further along. The 94-character reading was
being applied to both, so **every character of a 96-character download landed one position to the
left**, and a 94-character set happily accepted positions it does not have.

**The bigger finding is that none of it is drawn.** `SoftFont` is loaded, named and designated, and
`TryGetGlyph` has **no caller anywhere in `src\`** — no renderer reads it. A host that downloads a
character set and selects it gets the ordinary glyphs. So DECDLD is *accepted and stored*, not
*shown*, and the validation table should be read that way until a renderer uses it. Drawing it is
also one of the things only Ronny can judge.

## VT330/VT340 chapter 11 — the status line — walked 17 August 2026

Against Ronny's OCR at `d:\OCR\ai\EK-VT3XX-TP-001_VT330_VT340_Text_Programming_Mar87\`. The status
line did not exist here at all; DECSASD and DECSSDT are now built, with the line's contents, the
rules for leaving it and both DECRQSS answers.

**A defect fell out of it that had nothing to do with the status line.** `DCS $ q` (DECRQSS) and
`DCS q` (Sixel) share a final byte and are told apart by the `$` intermediate — the code even said
so — but the Sixel branch tested only the final byte and came first. So **on any terminal with
Sixel, every DECRQSS was swallowed as the start of an image and answered with silence.** That is
the VT340, which is the terminal this chapter was being walked for. Found because a status-line
DECRQSS test returned nothing.

**Where the three documents disagree, ECMA-48 settles it.** xterm's ctlseqs and the VT420 manual
both give DECSSDT as `CSI Ps $ ~`. The VT330/VT340 manual gives `CSI Ps $ -`, and backs it with the
character codes 2/4 2/13 — a real hyphen, not a bad scan. But 0x2D is in the *intermediate* range
0x20–0x2F, so a control sequence cannot end there: `CSI Ps $ -` would leave the parser still
waiting for a final byte. The tilde is a proper final. Two documents against one, and the standard
agrees with the two.

**What is NOT built, deliberately:** the geometry. A real VT340 shows 24 lines of main display with
the status line as a 25th below them; here it is held separately and the main display never gives
up a row, because this emulator's height comes from the window. **Nothing draws it yet either** —
whether a status line looks right is a question only Ronny can answer, so it waits until there is
something to look at. The *indicator* line is not built at all: it is the terminal talking about
itself, and half of what a VT340 reports there (modem state, dual sessions) has no counterpart
here. Two behaviours are marked in the code as assumptions rather than quotations: what happens to
DECSASD when DECSSDT changes underneath it, and whether the host may select an indicator line it
cannot write to.

## VT420 chapter 8 and the DECRQSS list — walked 17 August 2026

Chapter 8 (inserting, deleting and erasing) turned out to be almost right already: ICH and DCH
honour the left and right margins and refuse outside them, and ED, EL and ECH ignore the margins
entirely, which is what the chapter says ("These control functions can affect data inside or
outside the scrolling region. They are not restricted by margins").

One find: **IL and DL had no effect BELOW the scrolling region but did work above it.** The manual
says "IL has no effect outside the page margins" and "DL has no effect outside the scrolling
margins", and the buffer's own guard only refused a row past the bottom — so IL with the cursor
above a region shifted the rows outside it downwards and dragged them in.

**DECRQSS (table 12-4) lists fourteen settings; this terminal answers five.** DECSCL was added —
with the level number remembered *as the host asked for it*, because 62, 63 and 64 all behave the
same here but the answer has to be an instruction the host actually gave. The other nine are
deliberately unanswered, each for a written reason: the setter is not built (DECSCPP), what is
stored is not the value the setting names (DECSLPP holds a page count, not a page length), or the
hardware does not exist here at all (the status line, local function keys, modifier key reporting).
Answering with a sequence this terminal would then ignore is worse than saying nothing — the whole
point of DECRQSS is that the answer is executable.

## VT420 chapter 6 — the page format — walked 17 August 2026

Margins and origin mode, against the same manual. Two things were wrong, and they are the same
thing seen from opposite sides: **the column has an addressing origin too.**

The manual says it in the description of CUP itself — "The starting point for **lines and columns**
depends on the setting of origin mode (DECOM)" — and again under DECOM: "the home cursor position
is at the upper-left corner of the screen, **within the margins** ... The cursor cannot move outside
of the margins." It is easy to miss because it shows only once a host has set left and right
margins, and until DECSLRM existed here there were none to have.

- **CUP and HVP addressed columns absolutely**, so under origin mode with a left margin, column 1
  meant column 1 of the page rather than the first column of the region, and nothing stopped a host
  addressing past the right margin.
- **DECSLRM homed to the new left margin whatever origin mode said**, and **DECSTBM homed to column
  0 even under origin mode.** Both manuals' notes read "moves the cursor to column 1, line 1 of the
  page", and origin mode is what decides where that is. Two tests had pinned the old behaviour;
  their comments said they were written from "the same shape as DECSTBM" rather than from a
  document.

**Deliberately NOT changed: CHA and HPA.** The manual describes the origin under CUP because it is
describing CUP; CHA and HPA are ECMA-48 and the VT420 manual does not list them at all. No document
here says either way, so they still address the page. Making them consistent by guesswork is the
mistake the VPR-is-not-CUD evidence already punished once.

DECXCPR is also a place where the two documents disagree: DEC replies `CSI Pl;Pc;Pp R`, xterm
replies `CSI ? r ; c R` and notes it "assumes the default page". An unmarked reply cannot be told
from an ordinary CPR, so the marker is taken from xterm and the page from DEC.

## What no test in this repository can judge

Recorded separately in `docs\NEEDS-A-REAL-HOST-2026-08-11.md`: whether a beep sounds right, whether
a colour looks right on a real monitor, real ND / TDV hardware behaviour, and anything depending on
the wall clock.

Rendered PNGs ARE examined by eye, and that practice has caught three defects no assertion saw — a
red/blue channel swap, axis labels in a diagonal cascade, and plots drawn in the wrong phosphor.

## The next step this implies

The two reference documents added today are the first real DEC and xterm material in the repository.
Walking the implemented sequences against them — sequence by sequence, fixing what disagrees — is
what turns the "Inferred" rows above into "Document" rows, and it is now possible without asking
anyone for anything.
