> # WARNING - THE 2115 MODE SEQUENCE IN THIS DOCUMENT IS WRONG
>
> Checked against the Tandberg and ND manuals on 11 September 2026. This document says 2115
> compatibility mode is entered with `CSI ? 40 h`. Mode 40 is PCF, the printer code format
> (TDV 2215 Functional Specifications section 8.7.1); the 2115 switch is mode 66, it carries no
> private marker, and **RESET** is the side that enters 2115 mode - `CSI 66 l`. The way out is
> `ESC Q`.
>
> **Read `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`**, which quotes the manual and section
> for every statement. The key tables below HAVE been re-checked against the registry (see the next
> note); the mode-switching claims have not.

# TDV1200/TDV2200/TDV2215 Keyboard Complete Reference

> **SOURCE OF TRUTH: `src\RetroTerm.Core\Terminal\Emulators\TDV\TDV2200KeyRegistry.cs`.**
> Corrected 27 August 2026 after the nd-120 Verilog session nearly built an FPGA terminal from the
> tables below. They had been written in VT220 shape - arrows as `ESC[A/B/C/D`, function keys as
> `ESC[nn~`, PUSH keys as `ESC[?n~`, plus Page Up/Down, Insert, Delete and End that a TDV does not
> have - and every one of those was wrong. HOME had its two modes the wrong way round.
>
> A TDV is not a VT220 with extras. Where this document and the registry disagree, THE REGISTRY IS
> RIGHT and this document should be fixed.
>
> **Corrected again 2 September 2026, after measuring F1 on the live D100.** The 27 August sweep
> missed two rows and they contradicted this document's own summary line, which gives HJELP as
> `ESC[46_`:
>
> - **G53 HJELP** was listed as VK **112** sending **`ESC[28~`** - the same VT220 `ESC[nn~` shape
>   that sweep was supposed to remove. The registry has it at VK **0**, sending `ESC[46_`, and VK 0
>   is right: no PC key reaches HJELP, which is why it is bound rather than pressed.
> - **F51** was listed as "PUSH SI", VK 0. The registry has it as the terminal's **own F1 key**,
>   VK **112**, sending `ESC[50_` - and that is what a live D100 received when the F1 key was
>   pressed through the real key path, so it is measured rather than inferred.
>
> **Corrected a third time on 29 September 2026, and this time every row is checked by a machine.**
> Three whole tables were removed because they contradicted the registry: a VT220-shaped
> "F1-F20" table (`ESC[11~` to `ESC[34~`), a PUSH-key table giving `ESC[?1~` to `ESC[?8~`, and an
> "Alt+key" table from a key scheme that no longer exists. Some thirty other rows had the wrong VK
> code, the wrong key on a grid position, or a VT sequence next to the real C0 byte.
> `TdvDocumentFunctionKeyRowsTests` now reads EVERY table row that names a grid position and checks
> its VK code and every `ESC[nn_` and `0xNN` it states against the registry.
> `TdvKeyboardDocumentMatchesTheRegistryTests` guards the arrows and HOME by their text.

This document describes all keyboard keys, their integer values (VK codes), and expected escape
sequences for TDV1200, TDV2200, and TDV2215 terminals.

## Table of Contents

1. [Terminal Comparison](#terminal-comparison)
2. [Key Modes Overview](#key-modes-overview)
3. [Navigation Keys](#navigation-keys)
4. [Editing and Navigation Function Keys](#editing-and-navigation-function-keys)
5. [Function Keys F1-F8](#function-keys-f1-f8)
6. [PUSH Keys P1-P8](#push-keys-p1-p8)
7. [Soft Keys S1-S8](#soft-keys-s1-s8)
8. [Application Keys](#application-keys)
9. [System Keys](#system-keys)
10. [Control Keys](#control-keys)
11. [Numeric Keypad](#numeric-keypad)
12. [Modifier Keys](#modifier-keys)
13. [Complete Grid Position Reference](#complete-grid-position-reference)

---

## Terminal Comparison

### TDV1200 vs TDV2200 vs TDV2215 Overview

| Feature | TDV1200 | TDV2200 | TDV2215 |
|---------|---------|---------|---------|
| **Base Class** | TDVEmulatorBase | TDVEmulatorBase | TDVEmulatorBase |
| **Keyboard Mapper** | TDV1200KeyboardMapper | TDV2200KeyboardMapper | TDV2215KeyboardMapper |
| **2115 Compat Mode** | Yes (`CSI 66 h/l`) | Yes (`CSI ? 40 h/l`) | Yes (`CSI ? 40 h/l`) |
| **Firmware ID (2DA)** | 120 | 220 | 115 |
| **Primary DA** | `ESC[?1;2c` | `ESC[?220;0c` | `ESC[?1;2c` |
| **2115 Mode DA** | `ESC[?1;0c` | `ESC[?115;0c` | `ESC[?1;0c` |
| **Extended Mode** | Implicit | Implicit | Explicit (`CSI?1h/l`) |
| **Transparent Mode** | No | No | Yes (`CSI?2h/l`) |
| **Max Scrollback** | 1500 | 1000 | 10000 |
| **Era** | ~1985 | ~1988 | 1983 |

### Keyboard Sequences: IDENTICAL Across All Three

**All TDV terminals use the SAME keyboard escape sequences.** Note the terminator is an UNDERSCORE (`_`, 0x5F), which is outside the ANSI final-byte range — an ECMA-48 parser will not terminate on it. Shifted is the unshifted number PLUS ONE throughout.

| Key Type | Sequence Format | Example |
|----------|-----------------|---------|
| Function F1-F8 | `ESC [ nn _` | F1 = `ESC[50_`, shifted `ESC[51_` |
| ND function keys | `ESC [ nn _` | HJELP = `ESC[46_`, SLUTT = `ESC[48_` |
| PUSH P1-P8 | **nothing** | Programmable; no fixed sequence exists (`IsProgrammable`, every sequence null) |
| Arrow keys, ALL modes | C0 control code | Up = `0x1C` (FS) — never an escape sequence |
| Application keys | `ESC [ nn _` | MERK = `ESC[00_`, shifted `ESC[01_` |
| Numeric pad in pad function mode | `ESC [ nn _` | KP0 = `ESC[68_`, pad ENTER = `ESC[81_` |

### Character Set Defaults

| Terminal | G0/G1 Default | G2 | G3 |
|----------|---------------|-----|-----|
| TDV1200 | GraphicsI | GraphicsII | Math |
| TDV2200 | USASCII | GraphicsI | GraphicsII |
| TDV2215 | GraphicsI | GraphicsII | Math |

### Device Identification Responses (VERIFIED FROM CODE)

| Query | TDV1200 | TDV2200 | TDV2215 |
|-------|---------|---------|---------|
| Primary DA (`CSI c`) | `ESC[?1;2c` | `ESC[?220;0c` | `ESC[?1;2c` |
| Secondary DA (`CSI > c`) | `ESC[>120;0;0c` | `ESC[>220;0;0c` | `ESC[>115;0;0c` |
| In 2115 mode (Primary) | `ESC[?1;0c` | `ESC[?115;0c` | `ESC[?1;0c` |
| In 2115 mode (Secondary) | `ESC[>115;0;0c` | `ESC[>115;0;0c` | `ESC[>115;0;0c` |

### Font Rendering

| Terminal | Font Class | Cell Size | Rendering |
|----------|------------|-----------|-----------|
| TDV1200 | SystemFontRenderer | System | Vector/System font |
| TDV2200 | FontTDV2200 | 8x14 | Authentic bitmap |
| TDV2215 | FontTDV2215 | 9x14 | Authentic bitmap |

### Key Takeaway

**For keyboard validation: One test suite works for ALL THREE terminals.** The only differences are in device identification responses and font rendering, not in keyboard input sequences.

---

## Key Modes Overview

### TDV Extended Mode (Default for TDV1200/TDV2200/TDV2215)
- Uses `ESC [ nn _` — CSI, two decimal digits, UNDERSCORE terminator
- NOT VT220 compatible. There is no VT100/VT220 fallback anywhere: a key with no TDV
  equivalent returns null and sends nothing
- **Arrow keys and HOME do NOT send escape sequences in any mode** — see Navigation Keys
- All three terminals behave identically in this mode

### TDV2115 Compatibility Mode
- TDV1200: Enabled by `CSI 66 h`, disabled by `CSI 66 l`
- TDV2200/TDV2215: Enabled by `CSI ? 40 h`, disabled by `CSI ? 40 l`
- Uses C0 control codes for navigation keys
- More limited functionality, matches original TDV2115 hardware

### Application Cursor Mode (DECCKM)
- Enabled by CSI ? 1 h, disabled by CSI ? 1 l
- **Has no effect on a TDV.** DECCKM rewrites `ESC [ A` into `ESC O A`, and a TDV never
  sends either — its arrows are the C0 bytes below in every mode. This applies to the
  VT100/VT220 mappers, which are different classes.

---

## Navigation Keys

The VK column is the Windows virtual-key code that reaches the key from a PC keyboard. **0 means
no PC key reaches it** - the key is available on the virtual keyboard and through a user binding.

| Grid Pos | Key Name | VK Code | Extended | 2115 Mode | Notes |
|----------|----------|---------|----------|-----------|-------|
| C48 | Up Arrow | 38 (VK_UP) | `0x1C` (FS) | `0x1C` | `AlwaysSameCode` |
| A48 | Down Arrow | 40 (VK_DOWN) | `0x0B` (VT) | `0x0B` | `AlwaysSameCode` |
| B47 | Left Arrow | 37 (VK_LEFT) | `0x08` (BS) | `0x08` | `AlwaysSameCode`; note this is backspace |
| B49 | Right Arrow | 39 (VK_RIGHT) | `0x18` (CAN) | `0x18` | `AlwaysSameCode` |
| B48 | HOME | 36 (VK_HOME) | `0x1D` (GS) | `0x1D` | `AlwaysSameCode`. Corrected 31 August 2026 - this row previously gave DLE for 2115 mode with no citation; `spec\Keyboards\keyboard-spec.md` §6.8.3, which cites the TDV-2200/9 User's Guide (ND-30.003.04 EN) directly, shows that was an OCR error in an early pass superseded by a fresh OCR marking HOME "is always" GS |
| D47 | ROLLUP | 33 (VK_PRIOR) | `ESC[28_` / shift `ESC[29_` | `0x06` (ACK) | Page Up |
| D49 | ROLLDN | 34 (VK_NEXT) | `ESC[32_` / shift `ESC[33_` | `0x05` (ENQ) | Page Down |
| C47 | FIELDLEFT | 0 | `ESC[34_` / shift `ESC[35_` | `0x0C` (FF) | |
| C49 | FIELDRIGHT | 0 | `ESC[36_` / shift `ESC[37_` | `0x17` (ETB) | Name alias INSERT in the registry |
| A47 | TABLEFT | 0 | `ESC[38_` / shift `ESC[39_` | `0x15` (NAK) | |
| A49 | TABRIGHT | 0 | `ESC[40_` / shift `ESC[41_` | `0x09` (HT) | |
| G47 | STRYK | 46 (VK_DELETE) | `ESC[10_` / shift `ESC[11_` | `0x04` (EOT) | 2115 byte added to the registry 27 September 2026 |
| E14 | DEL | 0 | `0x7F` | `0x7F` | `AlwaysSameCode` |
| C13 | RETURN | 13 (VK_RETURN) | `0x0D` | `0x0D` | `AlwaysSameCode` |
| G54 | SLUTT | 35 (VK_END) | `ESC[48_` / shift `ESC[49_` | — | The PC End key reaches SLUTT (EXIT). There is no separate END key on a TDV |

### Arrow Keys with Modifiers (xterm-style)

**These rows describe the xterm and VT mappers, not a TDV.** A TDV arrow is `AlwaysSameCode`: it
sends the same C0 byte with or without Shift, Ctrl or Alt, and the registry ignores the modifier.

| Key + Modifier | VK + Modifier | Sequence |
|----------------|---------------|----------|
| Shift+Up | 38 + Shift | `ESC [ 1 ; 2 A` |
| Ctrl+Up | 38 + Ctrl | `ESC [ 1 ; 5 A` |
| Alt+Up | 38 + Alt | `ESC [ 1 ; 3 A` |
| Shift+Down | 40 + Shift | `ESC [ 1 ; 2 B` |
| Ctrl+Down | 40 + Ctrl | `ESC [ 1 ; 5 B` |
| Alt+Down | 40 + Alt | `ESC [ 1 ; 3 B` |
| Shift+Right | 39 + Shift | `ESC [ 1 ; 2 C` |
| Ctrl+Right | 39 + Ctrl | `ESC [ 1 ; 5 C` |
| Alt+Right | 39 + Alt | `ESC [ 1 ; 3 C` |
| Shift+Left | 37 + Shift | `ESC [ 1 ; 2 D` |
| Ctrl+Left | 37 + Ctrl | `ESC [ 1 ; 5 D` |
| Alt+Left | 37 + Alt | `ESC [ 1 ; 3 D` |

### Modifier Parameter Encoding (xterm)
| Modifier | Parameter |
|----------|-----------|
| Shift | 2 |
| Alt | 3 |
| Shift+Alt | 4 |
| Ctrl | 5 |
| Shift+Ctrl | 6 |
| Alt+Ctrl | 7 |
| Shift+Alt+Ctrl | 8 |

---

## Editing and Navigation Function Keys

The orange and brown keys around the main block that are not arrows. Every one sends `ESC [ nn _`
in extended mode, shifted is nn plus one, and the 2115-mode column is the single C0 byte from
`spec\Keyboards\keyboard-spec.md` section 6.8.3.

| Grid Pos | Key Name | VK Code | Extended | Shifted | 2115 Mode |
|----------|----------|---------|----------|---------|-----------|
| F47 | TAB | 9 (VK_TAB) | `ESC[16_` | `ESC[17_` | `0x09` (HT) |
| F48 | SEARCH | 0 | `ESC[18_` | `ESC[19_` | `0x11` (DC1) |
| F49 | REPLACE | 0 | `ESC[20_` | `ESC[21_` | `0x14` (DC4) |
| E13 | NEWPARA | 192 | `ESC[86_` | `ESC[87_` | `0x08` (BS) |
| E47 | GUILLEMETS | 0 | `ESC[22_` | `ESC[23_` | `0x1A` (SUB) |
| E48 | JUST | 0 | `ESC[24_` | `ESC[25_` | `0x12` (DC2) |
| E49 | SINGLEGUILLEMETS | 0 | `ESC[26_` | `ESC[27_` | `0x13` (DC3) |
| D99 | INNS | 45 (VK_INSERT) | `ESC[82_` | `ESC[83_` | `0x07` (BEL) |
| D13 | LF | 10 | `0x0A` | `0x0A` | `0x0A`, `AlwaysSameCode` |
| D47 | ROLLUP | 33 (VK_PRIOR) | `ESC[28_` | `ESC[29_` | `0x06` (ACK) |
| D48 | ANGRE | 0 | `ESC[30_` | `ESC[31_` | `0x15` (NAK) |
| D49 | ROLLDN | 34 (VK_NEXT) | `ESC[32_` | `ESC[33_` | `0x05` (ENQ) |
| C99 | MODE | 0 | `ESC[84_` | `ESC[85_` | `0x05` (ENQ) |
| C47 | FIELDLEFT | 0 | `ESC[34_` | `ESC[35_` | `0x0C` (FF) |
| C49 | FIELDRIGHT | 0 | `ESC[36_` | `ESC[37_` | `0x17` (ETB) |
| A47 | TABLEFT | 0 | `ESC[38_` | `ESC[39_` | `0x15` (NAK) |
| A49 | TABRIGHT | 0 | `ESC[40_` | `ESC[41_` | `0x09` (HT) |

---

## Function Keys F1-F8

A TDV2200 has eight function keys, F1 to F4 on the F row and F5 to F8 on the E row. F1 is the
terminal's OWN F1 (`ESC[50_`), measured on a live D100 on 1 September 2026. It is not HJELP, which
is a separate key (G53). F2 and F3 are the only keys with a Ctrl variant. In 2115 mode F5 to F8
send the plain characters shown rather than a control byte.

| Grid Pos | Key Name | VK Code | Extended | Shifted | Ctrl | 2115 Mode |
|----------|----------|---------|----------|---------|------|-----------|
| F51 | F1 | 112 (VK_F1) | `ESC[50_` | `ESC[51_` | — | `0x1E` (RS) |
| F52 | F2 | 113 (VK_F2) | `ESC[52_` | `ESC[53_` | `ESC[54_` | `0x1F` (US) |
| F53 | F3 | 114 (VK_F3) | `ESC[55_` | `ESC[56_` | `ESC[57_` | `0x18` (CAN) |
| F54 | F4 | 115 (VK_F4) | `ESC[58_` | `ESC[59_` | — | — |
| E51 | F5 | 116 (VK_F5) | `ESC[60_` | `ESC[61_` | — | `000` |
| E52 | F6 | 117 (VK_F6) | `ESC[62_` | `ESC[63_` | — | `00` |
| E53 | F7 | 118 (VK_F7) | `ESC[64_` | `ESC[65_` | — | `0` |
| E54 | F8 | 119 (VK_F8) | `ESC[66_` | `ESC[67_` | — | `+` |

PC keys F9 to F12 have no TDV equivalent and send nothing in TDV mode.

---

## PUSH Keys P1-P8

Grid positions G1 to G8, the brown keys at the left of the top row. They are **programmable**: the
registry flags them `IsProgrammable` and gives them no sequence at all, so `GetSequence` answers
null for every one of them in every mode. What a PUSH key sends is whatever the user has stored in
it - Keyboard menu, "Configure PUSH Keys...", saved in `tdv-key-bindings.json`. No PC key reaches
them directly (VK 0).

An earlier version of this document gave them `ESC[?1~` to `ESC[?8~` and a DCS form for the shifted
keys. No manual held here says that and the registry never did; the table is gone.

---

## Soft Keys S1-S8

**Not in the registry, and not checked by anything.** The only place these appear in the source is
`src\RetroTerm.Core\Terminal\Emulators\TDV\TDVKeyboardMapper.cs`, a name-keyed mapper that nothing
calls, and no manual in `spec\` has been found to cite them. Treat the table as a claim.

| Key Name | VK Code | Sequence | Hex |
|----------|---------|----------|-----|
| SOFT1 | 0 | `ESC [ ? A` | `1B 5B 3F 41` |
| SOFT2 | 0 | `ESC [ ? B` | `1B 5B 3F 42` |
| SOFT3 | 0 | `ESC [ ? C` | `1B 5B 3F 43` |
| SOFT4 | 0 | `ESC [ ? D` | `1B 5B 3F 44` |
| SOFT5 | 0 | `ESC [ ? E` | `1B 5B 3F 45` |
| SOFT6 | 0 | `ESC [ ? F` | `1B 5B 3F 46` |
| SOFT7 | 0 | `ESC [ ? G` | `1B 5B 3F 47` |
| SOFT8 | 0 | `ESC [ ? H` | `1B 5B 3F 48` |

---

## Application Keys

The orange keys on the top row: text editing on the left, file and program control on the right.
Shifted is the number plus one.

| Grid Pos | NO Label | EN Label | VK Code | Extended | Shifted | 2115 Mode |
|----------|----------|----------|---------|----------|---------|-----------|
| G9 | MERK | MARK | 0 | `ESC[00_` | `ESC[01_` | — |
| G10 | FELT | FIELD | 0 | `ESC[02_` | `ESC[03_` | `0x02` (STX) |
| G11 | AVSN | PARA | 0 | `ESC[04_` | `ESC[05_` | `0x01` (SOH) |
| G12 | SETN | SENT | 0 | `ESC[06_` | `ESC[07_` | `0x03` (ETX) |
| G13 | ORD | WORD | 0 | `ESC[08_` | `ESC[09_` | — |
| G14 | LOKAL | LOCAL | 0 | (local mode - not transmitted) | | |
| G47 | STRYK | DELETE | 46 (VK_DELETE) | `ESC[10_` | `ESC[11_` | `0x04` (EOT) |
| G48 | KOPI | COPY | 0 | `ESC[12_` | `ESC[13_` | `0x10` (DLE) |
| G49 | FLYTT | MOVE | 0 | `ESC[14_` | `ESC[15_` | `0x19` (EM) |
| G51 | FUNK | FUNC | 0 | `ESC[42_` | `ESC[43_` | `0x16` (SYN) |
| G52 | SKRIV | PRINT | 44 (VK_SNAPSHOT) | `ESC[44_` | `ESC[45_` | — |
| G53 | HJELP | HELP | 0 | `ESC [ 46 _` | `ESC[47_` | — |
| G54 | SLUTT | EXIT | 35 (VK_END) | `ESC[48_` | `ESC[49_` | — |

G11 was registered as "AVSH" until 27 September 2026; the keycap photo `spec\Keyboards\keys\G11.png`
reads AVSN (short for "avsnitt", paragraph).

---

## System Keys

| Grid Pos | Key Name | VK Code | Sequence |
|----------|----------|---------|----------|
| G0 | ESC | 27 (VK_ESCAPE) | `0x1B` |
| D13 | LF | 10 | `0x0A` (LF), `AlwaysSameCode` |
| E14 | DEL | 0 | `0x7F` (DEL), `AlwaysSameCode` |
| C13 | RETURN | 13 (VK_RETURN) | `0x0D` (CR) |
| B54 | ENTER (numeric pad) | 13 (VK_RETURN) | `0x0D` (CR); `ESC[81_` in pad function mode |
| A5 | SPACE | 32 (VK_SPACE) | the space character; the registry holds no sequence for it |
| D0 | CTRL | 162 (VK_LCONTROL) | (modifier only) |
| B99 | SHIFT (L) | 160 (VK_LSHIFT) | (modifier only) |
| B11 | SHIFT (R) | 161 (VK_RSHIFT) | (modifier only) |

The PC Backspace key (VK 8) is not on the TDV grid. Grid E13, which sits where a PC has Backspace,
is NEWPARA and sends `ESC[86_` - see Editing and Navigation Function Keys.

---

## Control Keys

TDV2115 mode control codes (C0 character set). These are the codes a HOST sends to the terminal;
they are listed here because in 2115 mode several keys send the same bytes back.

| Key Name | TDV2115 Code | Hex | Description |
|----------|--------------|-----|-------------|
| VIDEO_OFF | STX | 0x02 | Turn video off |
| VIDEO_ON | ETX | 0x03 | Turn video on |
| ERASE_LINE | EOT | 0x04 | Erase to end of line |
| LIGHT1_ON | ENQ | 0x05 | Turn on LED 1 |
| LIGHT2_ON | ACK | 0x06 | Turn on LED 2 |
| BELL | BEL | 0x07 | Terminal bell |
| CURSOR_LEFT | BS | 0x08 | Move cursor left |
| TAB | HT | 0x09 | Horizontal tab |
| LINE_FEED | LF | 0x0A | Line feed |
| CURSOR_DOWN | VT | 0x0B | Move cursor down |
| ROLL_UP | FF | 0x0C | Roll screen up |
| CURSOR_RETURN | CR | 0x0D | Carriage return |
| UNDERLINE | SO | 0x0E | Enable underline |
| NORMAL | SI | 0x0F | Disable underline |
| CURSOR_LOAD | DLE | 0x10 | Start binary positioning |
| LIGHT3_ON | NAK | 0x15 | Turn on LED 3 |
| LIGHTS_OFF | SYN | 0x16 | Turn off all LEDs |
| ROLL_DOWN | ETB | 0x17 | Roll screen down |
| CURSOR_RIGHT | CAN | 0x18 | Move cursor right |
| ERASE_PAGE | EM | 0x19 | Erase page |
| CURSOR_UP | FS | 0x1C | Move cursor up |
| CURSOR_HOME | GS | 0x1D | Move cursor home |

---

## Numeric Keypad

The pad keys send their character. In **numeric pad function mode** (spec section 6.8.4) each pad
key sends `ESC [ nn _` instead, including ENTER. The pad has a SPACE key (D54) and a minus (C54);
it has no plus.

| Grid Pos | Key Label | VK Code | Sequence | Pad function mode |
|----------|-----------|---------|----------|-------------------|
| A51 | 0 | 96 (VK_NUMPAD0) | `0` | `ESC[68_` |
| B51 | 1 | 97 (VK_NUMPAD1) | `1` | `ESC[69_` |
| B52 | 2 | 98 (VK_NUMPAD2) | `2` | `ESC[70_` |
| B53 | 3 | 99 (VK_NUMPAD3) | `3` | `ESC[71_` |
| C51 | 4 | 100 (VK_NUMPAD4) | `4` | `ESC[72_` |
| C52 | 5 | 101 (VK_NUMPAD5) | `5` | `ESC[73_` |
| C53 | 6 | 102 (VK_NUMPAD6) | `6` | `ESC[74_` |
| D51 | 7 | 103 (VK_NUMPAD7) | `7` | `ESC[75_` |
| D52 | 8 | 104 (VK_NUMPAD8) | `8` | `ESC[76_` |
| D53 | 9 | 105 (VK_NUMPAD9) | `9` | `ESC[77_` |
| A53 | . | 110 (VK_DECIMAL) | `.` | `ESC[78_` |
| C54 | - | 109 (VK_SUBTRACT) | `-` | `ESC[79_` |
| D54 | SPACE | 0 | space | `ESC[80_` |
| B54 | ENTER | 13 (VK_RETURN) | `0x0D` (CR) | `ESC[81_` |

---

## Modifier Keys

| Grid Pos | Key Name | VK Code | Purpose |
|----------|----------|---------|---------|
| B99 | SHIFT (Left) | 160 (VK_LSHIFT) | Modify key output |
| B11 | SHIFT (Right) | 161 (VK_RSHIFT) | Modify key output |
| D0 | CTRL | 162 (VK_LCONTROL) | Control key modifier |
| E0 | CAPS | 20 (VK_CAPITAL) | Caps Lock toggle |
| C0 | LOCK | 20 (VK_CAPITAL) | Lock mode toggle |
| C99 | MODE | 0 | Terminal mode switch; sends `ESC[84_` |
| D99 | INNS | 45 (VK_INSERT) | Insert/Expand mode; sends `ESC[82_` |

---

## Complete Grid Position Reference

Every key in the registry, by row. The Sequence column is the extended-mode sequence, or the C0
byte for the `AlwaysSameCode` keys, or the pad-function sequence for the numeric pad.

### Row G (Top Row) - Y=0

| Position | VK | Type | NO Label | EN Label | Sequence |
|----------|-----|------|----------|----------|----------|
| G0 | 27 | System | ESC | ESC | `0x1B` |
| G1 | 0 | PushKey | P1 | P1 | programmable, no fixed sequence |
| G2 | 0 | PushKey | P2 | P2 | programmable, no fixed sequence |
| G3 | 0 | PushKey | P3 | P3 | programmable, no fixed sequence |
| G4 | 0 | PushKey | P4 | P4 | programmable, no fixed sequence |
| G5 | 0 | PushKey | P5 | P5 | programmable, no fixed sequence |
| G6 | 0 | PushKey | P6 | P6 | programmable, no fixed sequence |
| G7 | 0 | PushKey | P7 | P7 | programmable, no fixed sequence |
| G8 | 0 | PushKey | P8 | P8 | programmable, no fixed sequence |
| G9 | 0 | Function | MERK | MARK | `ESC[00_` |
| G10 | 0 | Function | FELT | FIELD | `ESC[02_` |
| G11 | 0 | Function | AVSN | PARA | `ESC[04_` |
| G12 | 0 | Function | SETN | SENT | `ESC[06_` |
| G13 | 0 | Function | ORD | WORD | `ESC[08_` |
| G14 | 0 | Function | LOKAL | LOCAL | (local) |
| G47 | 46 | Special | STRYK | DELETE | `ESC[10_` |
| G48 | 0 | Special | KOPI | COPY | `ESC[12_` |
| G49 | 0 | Special | FLYTT | MOVE | `ESC[14_` |
| G51 | 0 | Special | FUNK | FUNC | `ESC[42_` |
| G52 | 44 | Special | SKRIV | PRINT | `ESC[44_` |
| G53 | 0 | Special | HJELP | HELP | `ESC[46_` |
| G54 | 35 | Special | SLUTT | EXIT | `ESC[48_` |

### Row F - Y=70

| Position | VK | Type | Label | Sequence |
|----------|-----|------|-------|----------|
| F47 | 9 | Function | TAB | `ESC[16_` |
| F48 | 0 | Function | SEARCH | `ESC[18_` |
| F49 | 0 | Function | REPLACE | `ESC[20_` |
| F51 | 112 | Function | F1 | `ESC[50_` |
| F52 | 113 | Function | F2 | `ESC[52_` |
| F53 | 114 | Function | F3 | `ESC[55_` |
| F54 | 115 | Function | F4 | `ESC[58_` |

### Row E (Number Row) - Y=140

| Position | VK | Type | Primary | Shifted | Sequence |
|----------|-----|------|---------|---------|----------|
| E0 | 20 | Special | CAPS | - | (toggle) |
| E1 | 49 | Normal | 1 | ! | `1` / `!` |
| E2 | 50 | Normal | 2 | " (@US) | `2` / `"` |
| E3 | 51 | Normal | 3 | # | `3` / `#` |
| E4 | 52 | Normal | 4 | ¤ ($US) | `4` / `¤` |
| E5 | 53 | Normal | 5 | % | `5` / `%` |
| E6 | 54 | Normal | 6 | & (^US) | `6` / `&` |
| E7 | 55 | Normal | 7 | / (&US) | `7` / `/` |
| E8 | 56 | Normal | 8 | ( (*US) | `8` / `(` |
| E9 | 57 | Normal | 9 | ) | `9` / `)` |
| E10 | 48 | Normal | 0 | = ()US) | `0` / `=` |
| E11 | 189 | Normal | + | ? (-US) | `+` / `?` |
| E12 | 187 | Normal | \| | ` (=US) | `\|` / `` ` `` |
| E13 | 192 | Function | NEWPARA | - | `ESC[86_` |
| E14 | 0 | Special | DEL | - | `0x7F` |
| E47 | 0 | Function | GUILLEMETS | - | `ESC[22_` |
| E48 | 0 | Function | JUST | - | `ESC[24_` |
| E49 | 0 | Function | SINGLEGUILLEMETS | - | `ESC[26_` |
| E51 | 116 | Function | F5 | - | `ESC[60_` |
| E52 | 117 | Function | F6 | - | `ESC[62_` |
| E53 | 118 | Function | F7 | - | `ESC[64_` |
| E54 | 119 | Function | F8 | - | `ESC[66_` |

E11 and B10 share VK 189 (VK_OEM_MINUS); Ronny decided on 2 September 2026 to leave that pair alone.
The reasoning is in the registry beside E11.

### Row D (QWERTY Row) - Y=210

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| D99 | 45 | Special | INNS/EXPS | `ESC[82_` |
| D0 | 162 | Modifier | CTRL | (modifier) |
| D1 | 81 | Normal | Q | `Q`/`q` |
| D2 | 87 | Normal | W | `W`/`w` |
| D3 | 69 | Normal | E | `E`/`e` |
| D4 | 82 | Normal | R | `R`/`r` |
| D5 | 84 | Normal | T | `T`/`t` |
| D6 | 89 | Normal | Y | `Y`/`y` |
| D7 | 85 | Normal | U | `U`/`u` |
| D8 | 73 | Normal | I | `I`/`i` |
| D9 | 79 | Normal | O | `O`/`o` |
| D10 | 80 | Normal | P | `P`/`p` |
| D11 | 219 | Normal | Å (NO) / [ (US) | national |
| D12 | 221 | Normal | ¨ (NO) / ] (US) | national |
| D13 | 10 | System | LF | `0x0A` |
| D47 | 33 | Navigation | ROLLUP (Page Up) | `ESC[28_` |
| D48 | 0 | Special | ANGRE | `ESC[30_` |
| D49 | 34 | Navigation | ROLLDN (Page Down) | `ESC[32_` |
| D51 | 103 | NumericPad | 7 | `7`; pad function `ESC[75_` |
| D52 | 104 | NumericPad | 8 | `8`; pad function `ESC[76_` |
| D53 | 105 | NumericPad | 9 | `9`; pad function `ESC[77_` |
| D54 | 0 | NumericPad | SPACE | space; pad function `ESC[80_` |

### Row C (ASDF Row) - Y=280

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| C99 | 0 | Special | MODE | `ESC[84_` |
| C0 | 20 | Special | LOCK | (toggle) |
| C1 | 65 | Normal | A | `A`/`a` |
| C2 | 83 | Normal | S | `S`/`s` |
| C3 | 68 | Normal | D | `D`/`d` |
| C4 | 70 | Normal | F | `F`/`f` |
| C5 | 71 | Normal | G | `G`/`g` |
| C6 | 72 | Normal | H | `H`/`h` |
| C7 | 74 | Normal | J | `J`/`j` |
| C8 | 75 | Normal | K | `K`/`k` |
| C9 | 76 | Normal | L | `L`/`l` |
| C10 | 186 | Normal | Ø (NO) / ; (US) | national |
| C11 | 222 | Normal | Æ (NO) / ' (US) | national |
| C12 | 220 | Normal | ' (NO) / \ (US) | national |
| C13 | 13 | System | RETURN | `0x0D` |
| C47 | 0 | Navigation | FIELDLEFT | `ESC[34_` |
| C48 | 38 | Navigation | ↑ | `0x1C` |
| C49 | 0 | Navigation | FIELDRIGHT | `ESC[36_` |
| C51 | 100 | NumericPad | 4 | `4`; pad function `ESC[72_` |
| C52 | 101 | NumericPad | 5 | `5`; pad function `ESC[73_` |
| C53 | 102 | NumericPad | 6 | `6`; pad function `ESC[74_` |
| C54 | 109 | NumericPad | - | `-`; pad function `ESC[79_` |

### Row B (ZXCV Row) - Y=350

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| B99 | 160 | Modifier | SHIFT | (modifier) |
| B0 | 226 | Normal | < > (ISO key left of Z) | chars |
| B1 | 90 | Normal | Z | `Z`/`z` |
| B2 | 88 | Normal | X | `X`/`x` |
| B3 | 67 | Normal | C | `C`/`c` |
| B4 | 86 | Normal | V | `V`/`v` |
| B5 | 66 | Normal | B | `B`/`b` |
| B6 | 78 | Normal | N | `N`/`n` |
| B7 | 77 | Normal | M | `M`/`m` |
| B8 | 188 | Normal | , | `,` |
| B9 | 190 | Normal | . | `.` |
| B10 | 189 | Normal | - / _ | `-`/`_` |
| B11 | 161 | Modifier | SHIFT | (modifier) |
| B47 | 37 | Navigation | ← | `0x08` |
| B48 | 36 | Navigation | HOME | `0x1D` |
| B49 | 39 | Navigation | → | `0x18` |
| B51 | 97 | NumericPad | 1 | `1`; pad function `ESC[69_` |
| B52 | 98 | NumericPad | 2 | `2`; pad function `ESC[70_` |
| B53 | 99 | NumericPad | 3 | `3`; pad function `ESC[71_` |
| B54 | 13 | NumericPad | ENTER (tall) | `0x0D`; pad function `ESC[81_` |

### Row A (Bottom Row) - Y=420

| Position | VK | Type | Label | Sequence |
|----------|-----|------|-------|----------|
| A5 | 32 | System | SPACE | space |
| A47 | 0 | Navigation | TABLEFT | `ESC[38_` |
| A48 | 40 | Navigation | ↓ | `0x0B` |
| A49 | 0 | Navigation | TABRIGHT | `ESC[40_` |
| A51 | 96 | NumericPad | 0 (wide) | `0`; pad function `ESC[68_` |
| A53 | 110 | NumericPad | . | `.`; pad function `ESC[78_` |

---

## Sequence Notation Legend

| Notation | Meaning | Hex |
|----------|---------|-----|
| `ESC` | Escape character | 0x1B |
| `[` | CSI introducer (follows ESC) | 0x5B |
| `_` | Terminator of every TDV key sequence | 0x5F |

---

## Source Files Reference

- Key Registry (THE source of truth): `src\RetroTerm.Core\Terminal\Emulators\TDV\TDV2200KeyRegistry.cs`
- Keyboard Layout: `src\RetroTerm.Desktop\Models\TDV2200KeyLayout.cs`
- VK Mapper: `src\RetroTerm.Core\Terminal\Input\KeyboardMapper.cs`
- Name-keyed TDV Mapper (no caller): `src\RetroTerm.Core\Terminal\Emulators\TDV\TDVKeyboardMapper.cs`
- Virtual Keyboard: `src\RetroTerm.Desktop\Controls\VirtualKeyboardPanel.axaml.cs`
- Guards on this document: `tests\RetroTerm.Tests\Terminal\TdvDocumentFunctionKeyRowsTests.cs` and
  `tests\RetroTerm.Tests\Terminal\TdvKeyboardDocumentMatchesTheRegistryTests.cs`

---

## Specifications Reference

- keyboard-spec.md: `spec\Keyboards\keyboard-spec.md` (sections 6.3, 6.8.3, 6.8.4 give the bytes above)
- TDV-Keys.md: `spec\TDV2200\Term and keyboard info\TDV-Keys.md`
- TDV2215.md: `spec\TDV2215\TDV2215.md` (Section 3.1 TDV 2115 Compatible Mode)
- keyboard_documentation.md: `spec\TDV2200\DOC TDV2200\keyboard_documentation.md`

---

*Document generated: 2026-02-06. Key tables re-checked against the registry: 2026-09-29*
*Full path: docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md*
