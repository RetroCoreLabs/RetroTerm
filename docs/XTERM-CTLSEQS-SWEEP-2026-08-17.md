# Walking xterm's ctlseqs list — 17 August 2026

**Full path:** `docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`

This is the P1.4 deliverable: every sequence in `spec\DEC\xterm-ctlseqs.txt` marked **implemented**,
**deliberately not**, or **missing**. 224 sequences were pulled out of the document — 117 CSI, 93
ESC, 14 DCS/OSC. **All three sections are walked**: the CSI, ESC and DCS/OSC lists below each
carry an implemented, deliberately-not and missing list.

Two things it found are already fixed and are recorded in
`docs\EMULATOR-VALIDATION-STATUS-2026-08-11.md`:

- **DECSTR (`CSI ! p`) did not exist**, hidden by a parser defect that recorded `!` as a private
  marker when 0x21 is an intermediate.
- **DECSCUSR (`CSI Ps SP q`) did not exist.** Every modern editor sends it, so the cursor never
  changed shape — and the style enum and the renderer that reads it were both already there.
- **`CSI Ps ^`**, the ECMA-48 5th-edition spelling of SD, was missing. One line.

## The CSI list

### Implemented

| Sequence | Name | Note |
|---|---|---|
| `CSI Ps @` | ICH | Disarms the pending wrap — xterm.js t0050. |
| `CSI Ps SP @` / `CSI Ps SP A` | SL / SR | ECMA-48, on every terminal here. |
| `CSI Ps A` … `CSI Ps F` | CUU CUD CUF CUB CNL CPL | Bounded by the margins. |
| `CSI Ps G` | CHA | Deliberately *not* put through the column origin — see below. |
| `CSI Ps ; Ps H`, `CSI Ps ; Ps f` | CUP, HVP | Both coordinates go through the addressing origin. |
| `CSI Ps I` | CHT | Never cancels a pending wrap. |
| `CSI Ps J`, `CSI ? Ps J` | ED, DECSED | |
| `CSI Ps K`, `CSI ? Ps K` | EL, DECSEL | EL disarms the pending wrap. |
| `CSI Ps L`, `CSI Ps M` | IL, DL | Move to the line home position; no effect outside the region. |
| `CSI Ps P` | DCH | Honours the left and right margins. |
| `CSI Ps S`, `CSI Ps T`, `CSI Ps ^` | SU, SD, SD | The caret form was added on 2026-08-17. |
| `CSI Ps ; … ; Ps T` | XTHIMOUSE | Recognised as five-parameter, so it is not read as SD. |
| `CSI Ps X` | ECH | Ignores the margins, as chapter 8 says it must. |
| `CSI Ps Z` | CBT | Never cancels a pending wrap, even though it moves. |
| `CSI Ps \`` , `CSI Ps a`, `CSI Ps j` | HPA, HPR, HPB | |
| `CSI Ps b` | REP | Dispatched before anything touches the cursor. |
| `CSI Ps c`, `CSI > Ps c` | Primary and Secondary DA | Per profile; every reply checked against a manual. |
| `CSI Ps d`, `CSI Ps e` | VPA, VPR | VPR is **not** CUD — the region does not stop it. |
| `CSI Ps g` | TBC | |
| `CSI Pm h` / `l`, `CSI ? Pm h` / `l` | SM / RM, DECSET / DECRST | |
| `CSI Pm m` | SGR | Including 256-colour and direct RGB. |
| `CSI Ps n`, `CSI ? Ps n` | DSR, DSR DEC-specific | The private form was added on 2026-08-17. |
| `CSI ! p` | DECSTR | Added 2026-08-17, from VT420 table 13-1. |
| `CSI Pl ; Pc " p` | DECSCL | |
| `CSI Ps $ p`, `CSI ? Ps $ p` | DECRQM | |
| `CSI Ps SP q` | DECSCUSR | Added 2026-08-17. |
| `CSI Ps " q` | DECSCA | |
| `CSI Ps ; Ps r` | DECSTBM | Homes to the host's column 1, line 1. |
| `CSI … $ r`, `CSI … $ t` | DECCARA, DECRARA | DECSACE decides rectangle or stream. |
| `CSI s`, `CSI Ps ; Ps s` | SCOSC, DECSLRM | Told apart by DECLRMM. |
| `CSI u` | SCORC | |
| `CSI Ps ; … t` | XTWINOPS / DECSLPP | Which one depends on the page-memory feature. |
| `CSI … $ v` | DECCRA | |
| `CSI Ps x` | DECREQTPARM | |
| `CSI Ps * x` | DECSACE | |
| `CSI … $ x` | DECFRA | |
| `CSI … * y` | DECRQCRA | Added 2026-08-17. |
| `CSI … $ z`, `CSI … $ {` | DECERA, DECSERA | |
| `CSI Ps ' }`, `CSI Ps ' ~` | DECIC, DECDC | |
| `CSI Ps $ }`, `CSI Ps $ ~` | DECSASD, DECSSDT | Added 2026-08-17. |

### Deliberately not implemented

| Sequence | Name | Why |
|---|---|---|
| `CSI Ps i`, `CSI ? Ps i` | MC — media copy | There is no printer at all. This is P4.1, and it is a whole feature rather than a sequence. |
| `CSI Ps q` | DECLL — load LEDs | No keyboard LEDs exist here. A real TDV has message LEDs and those *are* implemented, on their own sequences. |
| `CSI Ps SP t`, `CSI Ps SP u` | DECSWBV, DECSMBV | Warning-bell and margin-bell volume. Whether a beep sounds right is a question only Ronny can answer, so the volume of one is not worth guessing at. |
| `CSI Ps ; Ps ' z`, `CSI Ps ' {`, `CSI Ps ' \|`, `CSI Ps ; … ' w` | DECELR, DECSLE, DECRQLP, DECEFR | The DEC locator. Not built — and the DSR replies say so truthfully, "no locator" and "cannot identify". Answering these while reporting no locator would contradict ourselves. |
| `CSI Ps ; Ps , \|`, `CSI Ps ; Ps , }` | DECAC, DECATC | VT525 only. No VT525 is emulated here. |
| `CSI ) Ps {` | DECSTGLT | VT525 only. |
| `CSI > Ps T`, `CSI > Ps t`, `CSI > Ps f`, `CSI ? Ps g`, `CSI > Ps m`, `CSI ? Ps m`, `CSI > Ps n`, `CSI > Ps p`, `CSI > Ps s` | XTRMTITLE, XTSMTITLE, XTFMTKEYS, XTQFMTKEYS, XTMODKEYS, XTQMODKEYS, XTSHIFTESCAPE, XTSMPOINTER | xterm's own resource and key-modifier controls. They configure *xterm*, not a terminal — and this emulator's keyboard is configured through its own binding system, which the user can see and edit. |

### Missing — a real gap, with nothing standing in for it

| Sequence | Name | Weight |
|---|---|---|
| ~~`CSI ? 5 W`~~ | ~~DECST8C~~ | **Done 2026-08-17.** `ResetTabStops` already made stops at every eighth column, so only the routing was missing. |
| `CSI # P` / `# Q` / `# R` | XTPUSHCOLORS, XTPOPCOLORS, XTREPORTCOLORS | A palette stack. Nothing here uses it, but it is a real xterm feature and the palette is real. |
| `CSI # p` / `# q` / `# {` / `# }` | XTPUSHSGR, XTPOPSGR | An SGR stack. Same shape as above and rather more used in the wild. |
| `CSI Ps # y` | XTCHECKSUM | Would let a host choose the checksum rules DECRQCRA uses. Its default is what we already implement, so this is the knob rather than the function. |
| `CSI Ps ; Ps ; Ps # \|` | XTREPORTSGR | Reports the rendition over an area. |
| `CSI Ps $ \|`, `CSI Ps * \|` | DECSCPP, DECSNLS | Columns per page and lines per screen. Both are recorded in `DescribeSetting` as settings this terminal cannot report *because it cannot set them*. |
| `CSI & u` | DECRQUPSS | Asks which user-preferred supplemental set is assigned. There is none. |
| `CSI Ps " v` | DECRQDE | Request displayed extent. |
| `CSI Ps $ w` | DECRQPSR | Presentation state report — the big one, DECTABSR and DECCIR. |
| ~~`CSI ? Ps r`, `CSI ? Ps s`~~ | ~~XTRESTORE, XTSAVE~~ | **Done 2026-08-17**, and it was the worst of the set: `CSI ? 7 s` TURNED AUTOWRAP OFF. See below. |
| `CSI = c` | Tertiary DA | Answers a unit ID. |
| `CSI > Ps q` | XTVERSION | "Report xterm name and version." A modern host uses this to decide what the terminal can do. |
| `CSI ? Ps ; Ps ; Ps S` | XTSMGRAPHICS | Query or set graphics geometry and colour registers. A Sixel-aware host asks this before sending an image. **Worth doing, next to P2.** |

## The ESC list

The Tektronix and VT52 sections of the document are counted here but are covered by their own
emulators (`TEK4014` and VT52 mode), which have their own tests and their own manuals; they are not
repeated below.

### Implemented

| Sequence | Name | Note |
|---|---|---|
| `ESC D`, `ESC E`, `ESC M` | IND, NEL, RI | |
| `ESC H` | HTS | |
| `ESC 7`, `ESC 8` | DECSC, DECRC | Each screen has its own slot. |
| `ESC =`, `ESC >` | DECKPAM, DECKPNM | |
| `ESC c` | RIS | |
| `ESC # 3` … `ESC # 6` | DECDHL, DECSWL, DECDWL | |
| `ESC # 8` | DECALN | |
| `ESC ( C`, `ESC ) C`, `ESC * C`, `ESC + C` | Designate G0–G3 | Three of the manual's fifteen final characters — see the validation status document. |
| `ESC N`, `ESC O` | SS2, SS3 | **Added 2026-08-17.** |
| `ESC n`, `ESC o` | LS2, LS3 | **Added 2026-08-17.** |
| `ESC Z` | DECID | **Added 2026-08-17.** |
| `ESC SP F`, `ESC SP G` | S7C1T, S8C1T | **Added 2026-08-17.** The state was already there. |
| `ESC [`, `ESC P`, `ESC ]`, `ESC \`, `ESC X`, `ESC ^`, `ESC _` | CSI, DCS, OSC, ST, SOS, PM, APC | Introducers; the parser owns them. |

### Deliberately not implemented

| Sequence | Name | Why |
|---|---|---|
| `ESC F` | Cursor to lower left corner | An HP compatibility bug xterm keeps behind a resource that is **off by default**. Implementing a bug nobody switched on would be strange. |
| `ESC l`, `ESC m` | Memory lock / unlock | HP terminals. Nothing here has the concept, and no HP manual is held. |
| `ESC SP L`, `ESC SP M`, `ESC SP N` | ANSI conformance levels 1–3 | ECMA-43 levels, distinct from DECSCL. No document held here describes what each level changes. |
| `ESC V`, `ESC W` | SPA, EPA | The guarded-area pair. The TDV emulators implement protected areas on their own sequences, from their own manual; the ECMA-48 spelling would need the VT selective-erase model extended to match, and nothing sends it. |

### Missing — a real gap

| Sequence | Name | Weight |
|---|---|---|
| `ESC ~`, `ESC }`, `ESC \|` | LS1R, LS2R, LS3R | Invoke a set as **GR** rather than GL. **There is no GR here**: a cell carries one character-set number and the high half of the code table is not mapped separately. Adding the sequences without the concept would be a terminal that accepts the command and ignores it. This is the honest blocker, not laziness. |
| `ESC - C`, `ESC . C`, `ESC / C` | Designate G1–G3, 96-character | Same shape as the 94-character designators that exist, for the 96-character sets. Small, once there is a 96-character set worth designating. |
| `ESC % @`, `ESC % G` | Select default / UTF-8 character set | How a host switches the terminal in and out of UTF-8. Worth checking against how this emulator decodes today before adding — it may already be unconditional. |
| ~~`ESC 6`, `ESC 9`~~ | ~~DECBI, DECFI~~ | **Done 2026-08-17**, and the sideways scroll really was already there as SL and SR — which turned out to matter, see below. |

## The DCS and OSC lists

### DCS

| Sequence | Name | Verdict |
|---|---|---|
| `DCS Ps ; Ps \| Pt ST` | DECUDK | **Implemented**, lock and all. |
| `DCS Ps ; … { Dscs … ST` | DECDLD | **Implemented** — loaded and stored, but *never drawn*; see the validation status document. |
| `DCS $ q Pt ST` | DECRQSS | **Implemented**, seven of table 12-4's fourteen settings. |
| `DCS Pa ; Pb ; Ph q … ST` | Sixel | **Implemented.** |
| `DCS Pm p … ST` | ReGIS | **Implemented**, the drawing commands; the rest are counted. |
| `DCS Ps ! u Pt ST` | DECAUPSS | **Missing.** Assigns the user-preferred supplemental set; there is no such set here, and DECRQUPSS correctly reports none. |
| `DCS Ps $ t Pt ST` | DECRSPS | **Missing.** Restores a presentation state — the other half of DECRQPSR, which is also missing. |
| `DCS + q Pt ST` | XTGETTCAP | **Missing.** A host asking for a termcap string. Some programs use it to discover key sequences. |
| `DCS + p Pt ST`, `DCS + Q Pt ST` | XTSETTCAP, XTGETXRES | **Deliberately not.** These set and read *xterm's own resources*. This emulator's settings are not xterm's, and answering would be inventing a resource database. |

### OSC

Handled: **0** and **2** (window title), **1** (icon name), **4** (palette entry, set and query),
**7** (working directory), **10**, **11** and **12** (text, background and cursor colour, query
form).

Everything else is silent, and the code says why. **OSC 52 is the one to know about**: it hands the
clipboard to the host, and answering it would let anything on the far end of a connection read what
the user last copied. Silence there is a decision, not an omission.

## What comes next

The ones marked **worth doing** were those a real host actually notices: DECST8C, XTSAVE and
XTRESTORE, XTSMGRAPHICS, and DECBI/DECFI. **All four are now done.** The rest are recorded so that
"missing" never has to be re-derived.

**XTSMGRAPHICS was the one to do next, and it is done** (17 August 2026). It is not really a text
sequence at all: a Sixel-aware host asks it for the graphics geometry and the number of colour
registers *before* sending an image, so without it a terminal that draws Sixel perfectly can still
be sent nothing. Reading is answered; setting and resetting are refused with status 3, exactly as
xterm refuses them, because the plane is the VT340's fixed graphics space.

**The whole document is now walked** — CSI, ESC, DCS and OSC.

**All three "worth doing" ones are now done, 2026-08-17.** What is left of the list above is the
part a real host does not notice.

### They were not merely absent — two of them did the wrong thing

`CSI ? Ps W`, `CSI ? Ps s` and `CSI ? Ps r` all carry the `?` marker, and the private-MODE handler
reads any final byte other than `h` as a RESET. So:

 - **`CSI ? 7 s` turned AUTOWRAP OFF.** "Remember whether autowrap is on" did the opposite of
   remembering. xterm's own documentation gives that sequence as the termcap idiom for vi: "this
   can be used in termcap for vi(1), for example, to turn off saving of lines, but restore whatever
   the original state was on exit."
 - **`CSI ? Ps W` reset private mode Ps.** With the documented parameter of 5 that was harmless
   here by accident, because mode 5 is DECSCNM and this emulator does not implement it. With any
   other parameter it was real: `CSI ? 7 W` also turned autowrap off.

That makes **four** final bytes now found sitting in the same hole — `n` (DSR) and `i` (media copy)
were the first two, each a question or a command being read as a mode change. The pattern is worth
stating plainly: **a `?`-marked sequence is not automatically a mode.**

### And DECFI showed why the primitive matters

The first draft used `DeleteColumns(1)` for DECFI at the right margin. That acts at the CURSOR, so
it would have blanked one column and left the rest of the row where it was — the opposite of the
manual's "all screen data within the margins moves one column to the left". `ShiftRegionLeft(1)`,
which starts at the left margin, is the right primitive. DECBI's `ShiftRegionRight(1)` is the same
choice, though there the cursor-based version happens to agree.

### One more gap this turned up: DECSCNM, private mode 5 — **done 2026-08-17**

`ReverseVideoMode` was a field on `TerminalEmulatorBase` that nothing wrote and nothing read. It is
now the real mode, and the manual decided where it lives: "Screen mode only affects how the data
appears on the screen. DECSCNM does not change the data in page memory." So the flag rides out on
`ScreenFrame` and the renderer swaps at draw time; the buffer keeps what the host wrote. It
composes with SGR reverse by exclusive-or, so a cell already marked reverse comes out looking
normal on a reversed screen — which is what keeps a highlighted menu bar standing out.

**Looking at the PNG found two more defects, neither visible to any assertion:**

1. **A dark grid over every cell.** The per-cell background rectangles land on fractional
   coordinates and do not tile exactly, so a three-pixel seam at every boundary showed whatever was
   underneath. Normally that is the same colour as the cells and invisible; inverted, the ground was
   still dark and the screen came out covered in a grid. Both places that paint the ground — the
   page fill and the row cache's band fill — now follow the mode.
2. **A cell that should have been normal was invisible.** The renderer skips the background fill
   when the cell's background IS the default, relying on the page underneath. Under DECSCNM the
   page is the foreground colour, so a cell whose two swaps cancelled back to the default
   background had to be painted after all. Comparing against `_defaultBackground` unconditionally
   is what hid it.

**And the row cache had to learn about it.** DECSCNM changes every pixel while changing no cell, so
`RowCellsChanged` sees nothing and every cached row would have been reused the wrong way round.

## One the sweep missed — DECSDM, private mode 80

Added 2026-08-17, after the vt340test corpus found it.

The sweep walked all 224 sequences and marked the private modes it found, but **mode 80 (DECSDM,
Sixel Display Mode) was not among them** — neither as implemented, nor as deliberately-not, nor as
missing. It is listed in `ctlseqs.txt` under both `CSI ? Pm h` and `CSI ? Pm l`, so it was there to
be read and was skipped.

Worth writing down rather than quietly fixing, because it says something about the method: walking a
long list by eye misses entries, and it took a corpus fixture rendering visibly wrong to notice.
`comment.six` builds one picture out of fourteen DCS strings and only three of its sixteen colours
were drawn, for months, with a passing test.

It is now implemented, and the two sources disagree about which way round it goes:

 - The Graphics Programming manual: "When sixel display mode is set, the Sixel Scrolling feature is
   enabled."
 - xterm's `ctlseqs.txt`: mode 80 RESET "Turns on Sixel Scrolling" — the opposite.

hackerb9's errata for that manual settles it against real hardware: "DECSDM reversed regarding sixel
scrolling. On hackerb9's vt340: when DECSDM is set, sixel scrolling is disabled; when DECSDM is
reset, sixel scrolling is enabled." xterm and the machine agree, the manual carries several other
confirmed errata, and a machine beats a sentence. **Set means scrolling off.**
