# TDV and ND terminal modes and reports - what the manuals actually say

**Full path:** `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`
**Written:** 11 September 2026, from the transcribed manuals in `spec\`.

**Why this document exists.** Two documents in this folder described TDV mode setting and mode
reporting, neither cited a source, and both are wrong on every mode number. They were the only
authority behind `TDVEmulatorBase.GetModeState`, three test classes and one switched-off test.
Everything below carries the manual and the section it came from, so the next reader can check it
rather than trusting it.

The two documents that were wrong are named at the bottom, with what each got wrong, so that
nothing here is mistaken for a rewrite of them.

---

## The manuals, and which model each governs

| Model | Manual | File |
|---|---|---|
| TDV 2215, and TDV 2115 by way of its compatibility mode | *TDV 2215 Functional Specifications*, part no. 385604, publ. no. 5176, firmware revision level 11, February 1983, Tandberg Data A/S | `spec\TDV2215\TDV2215.md` |
| TDV 2200/9 S | *TDV 2200/9 S User's Guide (ND)*, Tandberg Data | `spec\TDV2200\OCR\TDV-2200_9-User-s_Guide-ND_combined.md` |
| ND Display Terminal 1200 | *ND Display Terminal 1200 Functional Specifications*, ND-12054-1 EN | `spec\TDV1200\ND-12054-1-EN_combined.md` |

`spec\TDV2115\TDV2115.md` is a byte-identical duplicate of the 2215 file. It is named for the 2115
but its contents are the 2215 specification; do not read it as a separate source.

**The 2200 is not a separate programming manual.** Its User's Guide section 11.1 says so in as many
words: "Information needed by the application programmer ... is found in the *TDV 2215 S Functional
Specifications*. The differences between the TDV 2215 S and the TDV 2200/9 S are described in this
manual." Section 11.2 is that difference list, and it is short. So for programming purposes the
2200 is the 2215 plus eleven sequences.

**What has NOT been read.** The scanned PDFs beside the OCR files in `spec\TDV2200\` are images
with no text layer. Absence of a statement in the transcribed material is not proof the hardware
lacked the feature - only that nothing held here documents it.

---

## 1. No TDV manual documents DECRQM

`CSI ? Ps $ p` and its reply `CSI ? Ps ; Pv $ y` appear nowhere in any of the three manuals. They
appear in this repository's `spec\` only in `spec\DEC\xterm-ctlseqs.txt`, which is DEC and xterm.

Three separate statements in the 2215 specification say the same thing from different directions:

- **Section 8.7, Accepted CSI Sequences**, is a complete table - CBT, DCH, DL, DSR, ECH, ED, EL,
  ICH, IL, MC, RM, SGR, SM, TBC. There is no `$` intermediate anywhere in it.
- **Section 8.3.2, Generated CSI Sequences**, lists everything the terminal ever sends, and it is
  one sequence: CPR, the cursor position report.
- **Section 7.1, Status Report**, says the report function is the cursor position, and section 8.7's
  DSR row adds "Note that for all parameter values other than 6, the sequence will be ignored."

The 2200 delta in section 11.2 adds no query of any kind. Its eleven extra sequences are lamp
control, national character generator select, cursor positioning and movement, and one mode pair:

```
CSI 80 h    Numeric pad to "Function" mode
CSI 80 l    Numeric pad to "Numeric" mode
```

Note there is **no `?` marker** on that pair, so it does not collide with DEC private mode 80
(DECSDM, sixel display mode), which this program implements. `CSI 80 h` / `CSI 80 l` is **not
implemented here**.

---

## 2. The ND way of asking is NDRQ

ND-1200 section 5.48, *NDRQ - Request and Report Terminal Parameters*:

```
Request   CSI Ps x                      (hex: CSI Ps 78)
Report    CSI Ps ; n1 ; ... ; nn x      (hex: CSI Ps 3B n1 3B ... nn 78)
```

Direction is both ways. Default is report type 1. The first parameter is the report type:

| Type | Request | Report |
|---|---|---|
| 0 | Ignored | "Requested report type not available" |
| 1 | Emulator level | emulator type, 3 digits, range 050-100 |
| 2 | Mode switches | three parameters p1, p2, p3 - see below |
| 3 | Convenience options | two parameters |
| 4 | Graphic switches | "To be defined later" |
| 5 | Error conditions | one parameter |
| 6 | Auxiliary devices | one parameter |

**Report type 2 is the one that answers "which modes are on".** It does not report one mode at a
time the way DECRQM does; it returns three bitmasks, one per mode family, and each family matches
the SM/RM tables in section 3 below.

p1, the ISO modes:

| Bit | Mode |
|---|---|
| 0 | Keyboard action mode |
| 1 | Insert/replace mode |
| 2 | Vertical editing mode |
| 3 | Horizontal editing mode |
| 4 | Send/receive mode |
| 5 | Line feed - new line |
| 6 | SGR combination mode |
| 7 | - |

p2, the DEC-compatible modes:

| Bit | Mode |
|---|---|
| 0 | Cursor key mode |
| 1 | Smooth scroll mode |
| 2 | Screen background mode |
| 3 | Origin mode |
| 4 | Autowrap mode |
| 5 | Auto repeat mode, 1 |
| 6 | Auto repeat mode, 2 |
| 7 | - |

p3, the ND private modes:

| Bit | Mode |
|---|---|
| 0 | Beginning of line wrap mode |
| 1 | Roll/page mode |
| 2 | Key klick mode |
| 3 | PUSH-key label mode |
| 4 | PROGRAM-key label mode |
| 5 | Decimal separator mode, 1 |
| 6 | Decimal separator mode, 2 |
| 7 | Numeric pad mode |

Report type 5 carries framing error, parity error, line buffer overflow, cursor addressing error
and CSI parameter error. Report type 6 carries magnetic card reader, graphics, mouse and printer.

**NDRQ is not implemented in this program.** The TDV2200 firmware analysis in
`spec\TDV2200\Testing2200_9S\tdv2200_escape_sequence_table.md` lists it as `ESC [ 120 x`, "NDREQ -
Request terminal parameters", "Not found" - that "not found" is about the firmware disassembly, not
about our code.

---

## 3. The mode numbers, per manual

### 3.1 TDV 2215, section 8.7.1 - SM/RM parameter values

These are ANSI modes: `CSI p1;p2;...;pn h` to set, `CSI ... l` to reset. **No `?` marker.** For
switches with more than two settings, each SM increments the setting until it reaches its highest
value, where it stays until RM.

| Value | Switch | RM | 1.SM | 2.SM |
|---|---|---|---|---|
| 7 | VEM, Vertical Editing Mode | FOL | PRE | |
| 31 | BOL, Beginning of Line Wrap | STOP | WRAP | |
| 32 | CR, Cursor Return | CR | CRLF | |
| 36 | EOL, End of Line Wrap | STOP | WRAP | |
| 40 | PCF, Printer Code Format | 7EVEN | 7ODD | 8NONE, then 8EVEN, 8ODD |
| 42 | PS, Printer Speed | 50 | 75 | 110, 134.5, 200, 300, 600, 1200, 2400, 4800, 9600, 19200 |
| 43 | PH, Printer Handshake | OFF | XON/XOFF | |
| 47 | RPM, Roll/Page Mode | ROLL | PAGE | |
| 53 | KC, Key Click | ON | OFF | |
| 54 | MB, Margin Bell | ON | OFF | |
| 55 | AR, Auto Repeat | ON | OFF | |
| 56 | UR, Underline Representation | UNDERLINE | NORMAL | INV, LOW, INV/LOW, INV/UND, UND/LOW, ... |
| 60 | RT, Roll Type | STEP | SMOOTH | |
| 61 | CLL, Clear Lamps | BOTH | KEY | SYN |
| 62 | GRM, Graphic Rendition Mode | ATTR | UND | SGR |
| 66 | **EC, Extended Control** | **OFF** | **ON** | |
| 67 | HAN, Handshake | OFF | XON/XOFF | |
| 68 | CT, Cursor Type | LINE | BLOCK | |
| 69 | PM, Printer Mode | LOC/REM | REM | LOG |

**Mode 66 is the 2115 switch, and its polarity is the opposite of what this program assumed.**
Section 3.1: "Whether the TDV 2215 should be limited to the TDV 2115 compatible mode, or take
advantage of its full capability, is controlled by a Soft-switch called the Extended Control switch
(4.2.12). When this switch is set to OFF, the terminal works like a TDV 2115 from the host
computer's point of view."

So **EC off means 2115-compatible** and EC on means extended. `CSI 66 l` enters 2115 mode;
`CSI 66 h` leaves it.

**Smooth scroll on the 2215 is mode 60, RT Roll Type, STEP or SMOOTH.** Not 67, which is the
XON/XOFF handshake.

**Leaving 2115 mode when you cannot send CSI.** In 2115-compatible operation the terminal accepts
only C0 codes and discards the ESC of any control sequence (section 3.1), so `CSI 66 h` cannot get
in. The manual names two ways out and both are single escapes: **`ESC Q`** (sections 3.1, 7.9.2,
and the note under 8.7.1), and **`ESC 0`** (section 7.9.1: "When the Extended Control switch is
off, a character following <1B> is not considered a parameter except 0. ESC 0 will turn the
Extended Control switch on.")

### 3.2 ND Display Terminal 1200, section 5.64 - RM/SM

Three families, distinguished by the intermediate character.

**ANSI, no marker** - `CSI n1 ; ... ; nn h` / `l`:

| Value | Mode | Reset | Set |
|---|---|---|---|
| 2 | Keyboard action mode | DISABLED | ENABLED |
| 4 | Insert/replace mode | REPLACE | INSERT |
| 7 | Vertical editing mode | FOLLOWING | PRECEDING |
| 10 | Horizontal editing mode | FOLLOWING | PRECEDING |
| 12 | Send/receive mode | MONITOR | SIMULTANEOUS |
| 20 | Line feed - new line | NEL | LF |
| 21 | SGR combination mode | REPLACING | CUMULATIVE |
| 62 | Graphic Rendition mode | ATTRIBUTE | UNDERLINE |
| 66 | **2115 mode** | **2115 codes** | **N/A** |

The manual's own note: "The 2115 mode and graphic rendition mode are included for historical
reasons to enable the terminal to run 2115 compatible software. To exit from 2115 mode, the
sequence 1B 51 must be sent." 1B 51 is `ESC Q`.

**So on the ND-1200, 2115 mode is entered by RESET of an ANSI mode - `CSI 66 l` - and there is no
set form at all.** Section 8.1 states it directly: "Enter 2115 MODE: `CSI` 66 l. Exit 2115 MODE:
`ESC Q`." This agrees with the 2215's EC switch: reset enters 2115 mode on both.

**DEC compatible private, `?` marker** - `CSI ? n1 ; ... ; nn h` / `l`:

| Value | Mode | Reset | Set |
|---|---|---|---|
| 1 | Cursor key mode | CURSOR | APPLICATION |
| 2, 3, 9 | Ignored | | |
| 4 | **Smooth scroll mode** | STEP | SMOOTH |
| 5 | Screen background mode | NEGATIVE | POSITIVE |
| 6 | Origin mode | OFF | ON |
| 7 | Autowrap mode | STOP | WRAP |
| 8 | Auto repeat mode | OFF | REPEAT |

These are DEC's own numbers, so `CSI ? 4 h` is DECSCLM and means exactly what it means on a VT100.

**ND private, `>` marker** - `CSI > n1 ; ... ; nn h` / `l`:

| Value | Mode | Reset | Set |
|---|---|---|---|
| 1 | Beginning of line wrap mode | STOP | WRAP |
| 2 | End of line wrap mode | STOP | WRAP |
| 3 | Roll/page mode | ROLL | PAGE |
| 4 | Key click mode | DISABLE | ENABLE |
| 5 | PUSH-key label mode | ON | OFF |
| 6 | PROGRAM-key label mode | ON | OFF |
| 7 | Decimal separator mode | PERIOD | COMMA, then SEQUENCE |

**NDBLWM and NDELWM are line wrap, not blink.** ND-1200 section 4.10, "NDBLWM - Beginning of Line
Wrap Mode: SET - Cursor will wrap around to preceding line when left margin is reached. RESET -
Cursor will stop at left margin." Section 4.11 is the End of Line equivalent. The mode list in
section 4 names eighteen modes and not one of them is a blink mode.

---

## 4. What this program had, and what each thing really is

`TDVEmulatorBase.GetModeState` and `HandleNDPrivateSequence` used five private-marker mode numbers.
Every one of them names a different switch in the manuals:

| Ours | We called it | 2215 section 8.7.1 | ND-1200 section 5.64 |
|---|---|---|---|
| `CSI ? 40 h` | 2115 compatibility, "the canonical number" | 40 = PCF, Printer Code Format | no mode 40 |
| `CSI ? 66 h` | 2115 compatibility, on | 66 = EC, and **reset** is the 2115 side | 66 = 2115 mode, **ANSI marker**, and **reset** enters it |
| `CSI ? 67 h` | NDSSM, smooth scroll | 67 = HAN, Handshake | smooth scroll is `CSI ? 4 h` |
| `CSI ? 68 h` | NDBLWM, "Blink mode" | 68 = CT, Cursor Type | NDBLWM = Beginning of Line Wrap, `CSI > 1 h` |
| `CSI ? 69 h` | NDELWM, "Enhanced blink mode" | 69 = PM, Printer Mode | NDELWM = End of Line Wrap, `CSI > 2 h` |

Mode 66 was the only one with the right number, and it was inverted and carried the wrong marker.

---

## 5. The two documents that were wrong

Neither cites a source. Both are kept, with a warning at the top pointing here, rather than deleted,
because code and tests were written from them and a reader who finds an old reference to one needs
to be able to see what happened.

**`docs\TDV-QUERY-COMMANDS.md`**

- Invents DECRQM for the TDV, which no manual has.
- Assigns `Ps = 66` to 2115 compatibility, 67 to smooth scroll, 68 to blink, 69 to enhanced blink,
  1 to extended mode, 2 to transparent mode, 38 to Tektronix mode. See the table above.
- Its state values - "0 not set, 1 set, 2 permanently set, 3 permanently not set" - are a garbled
  copy of DEC's DECRPM table, which is 0 not recognised, 1 set, 2 reset, 3 permanently set,
  4 permanently reset.
- Lists response shapes as though they were queries: `ESC [ ? 1 ; 1 $ y` is given as the "Work Area
  Query" command, but `$ y` is a reply final. Four such "custom queries" are listed and none of
  them is in any manual.

**`docs\TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md`**

- "Pure TDV 2115 mode via CSI ? 40 h", and "Activated by CSI ? 40 h / Deactivated by CSI ? 40 l".
  Mode 40 is the printer code format on the 2215 and does not exist on the ND-1200. The private
  marker is wrong, and the polarity is inverted.
- It does get `ESC Q` right.

**And one measurement that reads like evidence and is not.** `docs\manual-tests\M4-TDV.md` M4.2b
records "the mode was CONFIRMED active by a real DECRQM round trip rather than assumed:
`ESC[?40$p` came back `ESC[?40;1$y`". That went into RetroTerm's own receive path - RetroTerm is
the terminal in that setup and the D100 is the host - so it measured this program answering its own
invented sequence. It says nothing about a Tandberg. The rest of that case, the arrow key bytes, is
a genuine measurement and stands.

---

## 6. A second pass, 11 September 2026 - the 2215's "extended" and "transparent" modes

The mode work above corrected the sequences this program SENT. Driving the TestServer afterwards
turned up the same class of fault in what the 2215 emulator OBEYED.

### 6.1 Three DEC private numbers were being stolen

`TDV2215Emulator.HandleCsiSequence` claimed private modes 1, 2 and 40 for this model:

| Ours | We called it | What it really is |
|---|---|---|
| `CSI ? 1 h` | enable extended mode | **DECCKM**, application cursor keys |
| `CSI ? 2 h` | enable transparent mode | ND-1200 section 5.64 lists 2 among the numbers the terminal **ignores** |
| `CSI ? 40 h` | 2115 compatibility | 40 is PCF, the printer code format; the real switch is ANSI mode 66 |

So a host asking a TDV2215 for application cursor keys got "extended mode" instead, and the arrow
keys did not change. That block is gone. All three fall through to the ordinary DEC private path.

`tests\RetroTerm.Tests\Terminal\ActiveModeReportingTests.cs` had predicted this exactly: a test
named `OnTheTdv2215PrivateModeOneIsExtendedMode_NotDeccKm` documented the collision, said it did not
claim to be correct, and said that if a real 2215 has DECCKM on mode 1 "this test should fail and
the emulator should change". It does, and it did.

### 6.2 Extended mode IS the Extended Control switch

One switch, so now one flag: `IsExtendedMode` reads `!Is2115CompatibilityMode`. Section 3.1 makes
them the same thing - EC off "works like a TDV 2115 from the host computer's point of view" - and
section 8.7.1 gives the host mode 66 for it. A terminal that obeys CSI at power-up has EC on, so a
fresh emulator is extended, and the model string names the departure: plain `TDV2215` is extended
operation, `TDV2215+2115` is compatibility mode.

### 6.3 Transparent mode cannot be set by a host at all

- **TDV 2215 section 4.3.1** - the Send-Receive Mode soft-switch, SIMULTANEOUS or TRANSPARENT, set
  from the keyboard. "The only exit possible from this mode is obtained by depressing the MODE key
  twice, which gives access to the Soft-switch menu." Section 8.7.1 lists every host-settable mode
  and SRM is not among them.
- **TDV 2200/9 S User's Guide**, switch table - "Send Receive Mode: Simultaneous / Transparent".
- **ND 1200**, chapter 3 of the set-up functions - "Transparent mode: Disabled / Enabled".

`TDVSequenceBuilder.BuildTransparentModeEnable` and `BuildTransparentModeDisable` are deleted, and
a test fails if any method with "Transparent" in its name comes back.

### 6.4 NDRAR and NDFC were swapped everywhere

| Mnemonic | Final | Hex |
|---|---|---|
| NDAAR - Add attribute in rectangle | `{` | 7B |
| **NDRAR - Remove attribute in rectangle** | **`|`** | **7C** |
| **NDFC - Fill character(s) in rectangle** | **`}`** | **7D** |
| NDDWA - Define work area | `~` | 7E |

Two independent tables agree: ND Display Terminal 1200 section 2.8, and
`spec\TDV2200\Testing2200_9S\nd_csi_sequences.md`. This program had NDRAR on `}` and NDFC on `|`
in the emulators, in `TDVSequenceBuilder`, and in the trace decoder - so a host filling a rectangle
removed attributes instead, and the trace named the wrong sequence while doing it. Corrected in all
three places, and the three rectangle handlers moved up into `TDVEmulatorBase` so the 2215 has them
too. The 2215 never had NDFC at all: `|` was being eaten by the invented "extended mode specific"
handler.

### 6.5 Still open, and honestly unsettled

**NDVIDEO.** The manuals give its final as hex 7F - ND-1200 section 5.59, "HEX SEQUENCE: 7F",
"ASCII SEQUENCE: `<CSI> n/p`" - and this program uses `<`, hex 3C. 7F is DEL and lies outside the
ECMA-48 final range 40-7E, so the manual's own entry may be an OCR artefact or may describe
something this parser cannot represent. Left alone rather than changed on a guess.

### 6.6 The TestServer was sending invented sequences at real terminals

Found by driving it over a socket rather than by reading it:

- The TDV2215 suite sent `ESC ? 1 h` and `ESC ? 2 h` - not CSI sequences at all, and in no manual.
- Its general 2215 block SENT `CSI ? 1 h` (DECCKM) labelled "extended mode" and never put it back.
- Its transparent line was missing an escape, so it PRINTED `1b[?45h` on the screen, and the
  matching `CSI ? 45 l` turned off xterm reverse-wraparound on anything that understood it.
- `TDVCapabilityChecker` said the TDV1200 and TDV2200 had neither extended nor transparent mode.
  Both manuals list both switches for both models.
- One `TestServerApp` instance serves every client, so a second connection came up inside the menu
  the first had left, on the terminal type the first had chosen. The fields are reset per client
  now; two clients AT ONCE would still share them.

---

## 7. Character sets - what the manuals say, and why nothing was changed

Added 11 September 2026, after driving the TDV menus. This program designates character sets with
`ESC ( 0-9` (G0) and `ESC ) 0-9` (G1), mapping the digit onto `TDVCharacterSets.TDVCharacterSetType`
- US ASCII, Graphics I, Graphics II, Math, Greek, Norwegian, Swedish, Danish, Finnish, German. Three
manuals bear on that, and they do not agree with each other or with us.

**ND Display Terminal 1200, section 2.2** lists `ESC (` as "Designate G0 character set" and `ESC )`
as "Designate G1 character set" - so the SHAPE is right - but its table leaves the final character
unspecified, so the digit mapping is ours.

**ND-1200 sections 2.3 and 5.54** give nine alternate sets reached by SINGLE SHIFT: `ESC 1` to
`ESC 6`, `ESC 9`, `ESC :`, `ESC ;`, each affecting one following character in the range 21 to 7E.
None of these is implemented.

**TDV 2215, section 7.2** has FOUR sets, not ten: 1 standard, 2 semigraphic (95 line-drawing,
histogram and plotting characters), 3 subscript/superscript (32 characters), 4 the control-code
glyphs used in transparent operation. Sets 2 and 3 are reached with SS2 and SS3, which this program
does implement.

**TDV 2200/9 S, section 10.2** has FIFTEEN sets, reached by ESC-letter lead-ins: `ESC A` set 2,
`ESC 0` set 3, `ESC B` set 5, `ESC C` set 6 NORTEXT, `ESC D` set 7, and on to `ESC L`. `FontTDV2200`
in this repository already carries that reading in a comment - "Character set 2 (ESC A)",
"Character set 3 (ESC 0)" - while the code selects sets by a different sequence entirely.

**And section 10.2 contradicts section 10.1 in the same manual.** 10.1's table reads `ESC A Set 0`,
`ESC B Set 1`, `ESC C Set 2`, `ESC D Set 3`; 10.2 lists `ESC A` twice, for set 2 and again for set 4.
One of the two is an OCR casualty and nothing here says which. So the lead-ins are NOT implemented -
a guess here would put the wrong glyph on the screen for every national variant that uses set 4.

What it would take to settle it: a cleaner scan of section 10 of the 2200/9 S guide, or a real
terminal to type at. Recorded in `docs\PLAN.md` under Phase 4.

One more thing found on the way, and it is a clash to respect rather than a defect: 2215 section
7.9.1 says that with the Extended Control switch OFF, `ESC 0` turns EC back ON. So `ESC 0` means
"leave 2115 operation" in one mode and "character set 3" in the other, exactly like the `ESC c`
clash `TDV2200Emulator` already documents for graph mode.

---

## 8. The numpad: four keyboards, two ROMs, and one damaged table

Added 11 September 2026. I reported that two numpad keys were named a position out, on the strength
of one table. Four photographs say otherwise and the report was wrong - this section is the
correction and the evidence.

**What the photographs show.** `spec\Keyboards\ND246-Keyboard.jpg`, `ND322-Keyboard.jpg`,
`nd-967003-1.jpg` (TDV 1200 keyboard) and `nd-965133-1.jpg` (TDV 2200/S) all carry the same numeric
pad, and the cropped keycaps in `spec\Keyboards\keys\` show it close up:

| Row | Keys |
|---|---|
| D | 7, 8, 9, **space** (D54, the open-topped rectangle) |
| C | 4, 5, 6, **minus** (C54) |
| B | 1, 2, 3, ENTER (B54, double height) |
| A | 0, **period** (A53) |

`TDV2200KeyRegistry` matches that exactly - `KPSPACE` on D54, `KPMINUS` on C54, `KPDOT` on A53 -
and so does `keyboard-spec.md`, which is built from two keyboard ROM layouts (965313-0 for the
2200/9 S Norwegian, 961292-2 for the 2215 ASCII) as well as the photographs. Both ROMs give D54 SP
and C54 minus.

**The one source that disagrees, and why it loses.** Section 9.2 of the TDV 2200/9 S User's Guide
lists D54 as "-" and C54 as "+". That table is already known to be OCR-damaged in the column next
door: its first three rows give the sequence terminator as `3F` where every other source, and a
fresh OCR, give `5F` - recorded and verified in `keyboard-spec.md` in February 2026. A label column
in a table whose neighbouring column is proven wrong does not outweigh four photographs and two ROM
dumps.

**Where the "+" actually is.** On the TDV 2200/S keyboard (`nd-965133-1.jpg`) there are `=` and `+`
keys in the block ABOVE the pad, not in it. That is the likeliest thing section 9.2's row was
reaching for.

**And where the comma comes from - it is a MODE, not a keycap.** ND-1200 section 5.64 gives ND
private mode 7, the decimal-separator mode: `CSI > 7 h` steps PERIOD to COMMA to SEQUENCE and
`CSI > 7 l` goes back to PERIOD. NDRQ report type 2 carries it in parameter 3, bits 5 and 6. So the
same physical decimal key reads as a point or a comma depending on a switch the host can set, which
is exactly what a Nordic machine would want. This program implements neither the mode nor those two
report bits.

**What IS missing from the registry**, and it is one row: B54 ENTER has no Function-mode code.
Section 9.2 and `keyboard-spec.md` both give `CSI 81 _`; the registry sends carriage return in every
mode. The other thirteen numpad keys all have theirs and all thirteen match.

---

## 9. The 2215's nineteen ANSI modes, read the other way round

Added 11 September 2026. Sections 3 to 6 worked from the sequences this program SENDS towards the
manual. This is the reverse pass: take every number in TDV 2215 section 8.7.1 and ask what this
emulator does with it.

| # | 2215 switch | What happens here |
|---|---|---|
| 7 | VEM, vertical editing | counted as unhandled |
| 31 | BOL, beginning of line wrap | handled - reverse wrap |
| 32 | CR, cursor return | handled - LNM, DEC's mode 20, one state |
| 36 | EOL, end of line wrap | handled - autowrap |
| 40 | PCF, printer code format | counted - no printer modelled |
| 42 | PS, printer speed | counted - no printer modelled |
| 43 | PH, printer handshake | counted - no printer modelled |
| 47 | RPM, roll/page | handled - held and reported, no behaviour yet |
| 53 | KC, key click | handled - held and reported, inverted against ND mode 4 |
| 54 | MB, margin bell | counted |
| 55 | AR, auto repeat | handled - the DECARM flag, inverted against DEC private mode 8 |
| 56 | UR, underline representation | counted - a multi-position rendering switch |
| 60 | RT, roll type | handled - smooth scroll |
| 61 | CLL, clear lamps | counted - three positions, and it interacts with the message lamps |
| 62 | GRM, graphic rendition | counted - three positions, and PED really sends it |
| 66 | EC, extended control | handled - the 2115 switch |
| 67 | HAN, handshake | counted - flow control belongs to the connection |
| 68 | CT, cursor type | handled - the cursor shape, blink left alone |
| 69 | PM, printer mode | counted - no printer modelled |

**Three of the nineteen were the find.** 32, 55 and 68 each name a switch this program already
keeps under DEC's number - LNM, DECARM and the cursor shape - and each was ignored on the 2215's
own number. A host driving a TDV by its own manual got nothing. They are wired now, and 55 carries
the same inversion trap as the key click: the 2215 lists the ordinary state first, so SET means OFF,
while DEC's private mode 8 has SET meaning repeat.

**Everything else is either done or honestly counted.** A mode this terminal cannot act on reaches
`CountUnrecognisedSequence` WITH ITS NUMBER, so a real host's use of one shows up in UNHANDLED
rather than vanishing - that is what makes the printer and handshake rows harmless.

**The two worth doing next, when there is a reason to:** 62 GRM, because a real PED sends it, and
61 CLL, because the message lamps now exist for it to act on. Both are three-position switches, so
both need the stepping shape the decimal separator uses.
