> # WARNING - THE 2115 MODE SEQUENCE IN THIS DOCUMENT IS WRONG
>
> Checked against the Tandberg and ND manuals on 11 September 2026. This document says 2115
> compatibility mode is entered with `CSI ? 40 h`. Mode 40 is PCF, the printer code format
> (TDV 2215 Functional Specifications section 8.7.1); the 2115 switch is mode 66, it carries no
> private marker, and **RESET** is the side that enters 2115 mode - `CSI 66 l`. The way out is
> `ESC Q`.
>
> **Read `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`**, which quotes the manual and section
> for every statement. Nothing else in this file has been re-checked.

# TDV1200/TDV2200/TDV2215 Keyboard Complete Reference

> **SOURCE OF TRUTH: `src\RetroTerm.Core\Terminal\Emulators\TDV\TDV2200KeyRegistry.cs`.**
> Corrected 27 August 2026 after the nd-120 Verilog session nearly built an FPGA terminal from the
> tables below. They had been written in VT220 shape - arrows as `ESC[A/B/C/D`, function keys as
> `ESC[nn~`, PUSH keys as `ESC[?n~`, plus Page Up/Down, Insert, Delete and End that a TDV does not
> have - and every one of those was wrong. HOME had its two modes the wrong way round.
>
> A TDV is not a VT220 with extras. Where this document and the registry disagree, THE REGISTRY IS
> RIGHT and this document should be fixed. Anything here not yet checked against it is a claim.
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
>   pressed through the real key path, so it is measured rather than inferred. If PUSH SI is a real
>   TDV concept it belongs on some other grid position; nothing here cites a manual for it.
>
> `TdvKeyboardDocumentMatchesTheRegistryTests` guards this document against the registry, but only
> for the arrows and HOME - which is exactly why these two rows survived. Extending it to the
> function keys would have caught them.

This document describes all keyboard keys, their integer values (VK codes), and expected escape sequences for TDV1200, TDV2200, and TDV2215 terminals.

## Table of Contents

1. [Terminal Comparison](#terminal-comparison)
2. [Key Modes Overview](#key-modes-overview)
3. [Navigation Keys](#navigation-keys)
4. [Function Keys F1-F20](#function-keys-f1-f20)
5. [PUSH Keys P1-P8](#push-keys-p1-p8)
6. [Soft Keys S1-S8](#soft-keys-s1-s8)
7. [Application Keys](#application-keys)
8. [System Keys](#system-keys)
9. [Control Keys](#control-keys)
10. [Numeric Keypad](#numeric-keypad)
11. [Modifier Keys](#modifier-keys)
12. [Complete Grid Position Reference](#complete-grid-position-reference)

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
| Function F1-F4 | `ESC [ nn _` | F1 = `ESC[50_`, shifted `ESC[51_` |
| ND function keys | `ESC [ nn _` | HJELP = `ESC[46_`, SLUTT = `ESC[48_` |
| PUSH P1-P8 | **nothing** | Programmable; no fixed sequence exists (`IsProgrammable`, every sequence null) |
| Arrow keys, ALL modes | C0 control code | Up = `0x1C` (FS) — never an escape sequence |
| Application keys | `ESC [ nn _` | MERK = `ESC[00_`, shifted `ESC[01_` |

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

| Grid Pos | Key Name | VK Code | Extended | 2115 Mode | Notes |
|----------|----------|---------|----------|-----------|-------|
| (implied) | Up Arrow | 38 (VK_UP) | `0x1C` (FS) | `0x1C` | `AlwaysSameCode` |
| A48 | Down Arrow | 40 (VK_DOWN) | `0x0B` (VT) | `0x0B` | `AlwaysSameCode` |
| B47 | Left Arrow | 37 (VK_LEFT) | `0x08` (BS) | `0x08` | `AlwaysSameCode`; note this is backspace |
| B49 | Right Arrow | 39 (VK_RIGHT) | `0x18` (CAN) | `0x18` | `AlwaysSameCode` |
| B48 | HOME | 36 (VK_HOME) | `0x1D` (GS) | `0x1D` | `AlwaysSameCode`. Corrected 31 August 2026 - this row previously said `0x10` (DLE) for 2115 mode with no citation; `spec\Keyboards\keyboard-spec.md` §6.8.3, which cites the TDV-2200/9 User's Guide (ND-30.003.04 EN) directly, shows that was an OCR error in an early pass superseded by a fresh OCR marking HOME "is always" GS |
| D47 | ROLLUP | 33 (VK_PRIOR) | `ESC[28_` / shift `ESC[29_` | `0x06` | `ESC[28_` |
| D49 | ROLLDN | 34 (VK_NEXT) | `ESC[32_` / shift `ESC[33_` | `0x05` | `ESC[32_` |
| C49 | FIELDRIGHT | — | `ESC[36_` / shift `ESC[37_` | `0x17` | `ESC[36_` |
| G47 | STRYK | 46 (VK_DELETE) | `ESC[10_` / shift `ESC[11_` | — | `ESC[10_` |
| E14 | DEL | — | `0x7F` | `0x7F` | `AlwaysSameCode` |
| C13 | RETURN | 13 | `0x0D` | `0x0D` | `AlwaysSameCode` |
| — | END | 35 (VK_END) | **nothing** | **nothing** | No END key exists on a TDV. Returns null; no fallback |

### Arrow Keys with Modifiers (xterm-style)

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

## Function Keys F1-F20

| Grid Pos | Key Name | VK Code | TDV Sequence | VT100 Style |
|----------|----------|---------|--------------|-------------|
| (F1) | F1 | 112 (VK_F1) | `ESC [ 11 ~` | `ESC O P` |
| (F2) | F2 | 113 (VK_F2) | `ESC [ 12 ~` | `ESC O Q` |
| (F3) | F3 | 114 (VK_F3) | `ESC [ 13 ~` | `ESC O R` |
| (F4) | F4 | 115 (VK_F4) | `ESC [ 14 ~` | `ESC O S` |
| (F5) | F5 | 116 (VK_F5) | `ESC [ 15 ~` | `ESC [ 15 ~` |
| (F6) | F6 | 117 (VK_F6) | `ESC [ 17 ~` | `ESC [ 17 ~` |
| (F7) | F7 | 118 (VK_F7) | `ESC [ 18 ~` | `ESC [ 18 ~` |
| (F8) | F8 | 119 (VK_F8) | `ESC [ 19 ~` | `ESC [ 19 ~` |
| (F9) | F9 | 120 (VK_F9) | `ESC [ 20 ~` | `ESC [ 20 ~` |
| (F10) | F10 | 121 (VK_F10) | `ESC [ 21 ~` | `ESC [ 21 ~` |
| (F11) | F11 | 122 (VK_F11) | `ESC [ 23 ~` | `ESC [ 23 ~` |
| (F12) | F12 | 123 (VK_F12) | `ESC [ 24 ~` | `ESC [ 24 ~` |
| (F13) | F13 | 124 (VK_F13) | `ESC [ 25 ~` | `ESC [ 25 ~` |
| (F14) | F14 | 125 (VK_F14) | `ESC [ 26 ~` | `ESC [ 26 ~` |
| (F15) | F15 | 126 (VK_F15) | `ESC [ 28 ~` | `ESC [ 28 ~` |
| (F16) | F16 | 127 (VK_F16) | `ESC [ 29 ~` | `ESC [ 29 ~` |
| (F17) | F17 | 128 (VK_F17) | `ESC [ 31 ~` | `ESC [ 31 ~` |
| (F18) | F18 | 129 (VK_F18) | `ESC [ 32 ~` | `ESC [ 32 ~` |
| (F19) | F19 | 130 (VK_F19) | `ESC [ 33 ~` | `ESC [ 33 ~` |
| (F20) | F20 | 131 (VK_F20) | `ESC [ 34 ~` | `ESC [ 34 ~` |

### Function Keys with Modifiers

Format: `ESC [ <num> ; <modifier> ~`

| Key + Modifier | Sequence |
|----------------|----------|
| Shift+F1 | `ESC [ 11 ; 2 ~` |
| Ctrl+F1 | `ESC [ 11 ; 5 ~` |
| Alt+F1 | `ESC [ 11 ; 3 ~` |
| (same pattern for F2-F20) | |

---

## PUSH Keys P1-P8

TDV-specific programmable function keys. These send CSI ? sequences.

| Grid Pos | Key Name | VK Code | Normal Sequence | Shift Sequence |
|----------|----------|---------|-----------------|----------------|
| G1 | P1 (PUSH1) | 0 | `ESC [ ? 1 ~` | `ESC P S 1 ESC \` |
| G2 | P2 (PUSH2) | 0 | `ESC [ ? 2 ~` | `ESC P S 2 ESC \` |
| G3 | P3 (PUSH3) | 0 | `ESC [ ? 3 ~` | `ESC P S 3 ESC \` |
| G4 | P4 (PUSH4) | 0 | `ESC [ ? 4 ~` | `ESC P S 4 ESC \` |
| G5 | P5 (PUSH5) | 0 | `ESC [ ? 5 ~` | `ESC P S 5 ESC \` |
| G6 | P6 (PUSH6) | 0 | `ESC [ ? 6 ~` | `ESC P S 6 ESC \` |
| G7 | P7 (PUSH7) | 0 | `ESC [ ? 7 ~` | `ESC P S 7 ESC \` |
| G8 | P8 (PUSH8) | 0 | `ESC [ ? 8 ~` | `ESC P S 8 ESC \` |

### DCS Sequence Format for Shifted PUSH Keys
- `ESC P` = DCS introducer (0x1B 0x50)
- `S` = Shifted flag
- `<num>` = PUSH key number (1-8)
- `ESC \` = ST (String Terminator, 0x1B 0x5C)

---

## Soft Keys S1-S8

TDV-specific soft function keys. These send CSI ? sequences with letter suffix.

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

### Soft Key Sequence Format
- `ESC [` = CSI introducer
- `?` = Private parameter flag
- `A-H` = Soft key identifier (A=1, B=2, ... H=8)

---

## Application Keys

TDV-specific application control and editing keys.

| Grid Pos | NO Label | EN Label | VK Code | Sequence |
|----------|----------|----------|---------|----------|
| G9 | MERK | MARK | 0 | `ESC[00_` |
| G10 | FELT | FIELD | 0 | `ESC[02_` |
| G11 | AVSH | PARA | 0 | `ESC[04_` |
| G12 | SETN | SENT | 0 | `ESC[06_` |
| G13 | ORD | WORD | 0 | `ESC[08_` |
| G14 | LOKAL | LOCAL | 0 | (local mode - not transmitted) |
| G47 | STRYK | DELETE | 46 | `ESC[10_` |
| G48 | KOPI | COPY | 67 | `ESC[12_` |
| G49 | FLYTT | MOVE | 88 | `ESC[14_` |
| G51 | FUNK | FUNC | 0 | `ESC[42_` |
| G52 | SKRIV | PRINT | 44 | `ESC[44_` |
| G53 | HJELP | HELP | 0 | `ESC [ 46 _` |
| G54 | SLUTT | EXIT | 0 | `ESC[48_` |

### Additional Application Keys (via Alt+Key)

| Alt+Key | Name | Sequence |
|---------|------|----------|
| Alt+H | HELP | `ESC [ 28 ~` |
| Alt+D | DO | `ESC [ 29 ~` |
| Alt+U | FUNC | `ESC [ @` |
| Alt+P | PRINT | `ESC [ A` |
| Alt+X | EXIT | `ESC [ C` |
| Alt+C | CANCEL | `ESC [ 27 ~` |
| Alt+M | COMMAND | `ESC [ 26 ~` |
| Alt+F | FIND | `ESC [ 1 ; 2 R` |
| Alt+S | SELECT | `ESC [ 4 ; 2 ~` |
| Alt+K | COPY | `ESC [ M` |
| Alt+V | MOVE | `ESC [ N` |
| Alt+J | JUST | `ESC [   H` (space before H) |
| Alt+A | MARK | `ESC [ X` |
| Alt+L | FIELD | `ESC [ Y` |
| Alt+R | PARA | `ESC [ Z` |
| Alt+E | SENT | `ESC [ [` |
| Alt+W | WORD | `ESC [ \` |
| Alt+I | INSERT_HERE | `ESC [ 2 ; 2 ~` |
| Alt+Delete | REMOVE | `ESC [ 3 ; 2 ~` |
| Alt+PageUp | PREV | `ESC [ 5 ; 2 ~` |
| Alt+PageDown | NEXT | `ESC [ 6 ; 2 ~` |

---

## System Keys

| Grid Pos | Key Name | VK Code | Sequence |
|----------|----------|---------|----------|
| G0 | ESC | 27 (VK_ESCAPE) | `0x1B` |
| D13 | DEL | 8 (VK_BACK) | `0x08` (BS) or `0x7F` (DEL) |
| C13 | RETURN | 13 (VK_RETURN) | `0x0D` (CR) |
| B54 | ENTER | 13 (VK_RETURN) | `0x0D` (CR) |
| A49 | ENTER (→\|) | 13 (VK_RETURN) | `0x0D` (CR) |
| A5 | SPACE | 32 (VK_SPACE) | `0x20` |
| D0 | CTRL | 162 (VK_LCONTROL) | (modifier only) |
| B99 | SHIFT (L) | 160 (VK_LSHIFT) | (modifier only) |
| B11 | SHIFT (R) | 161 (VK_RSHIFT) | (modifier only) |

---

## Control Keys

TDV2115 mode control codes (C0 character set).

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

| Grid Pos | Key Label | VK Code | Sequence |
|----------|-----------|---------|----------|
| A47 | 0 | 96 (VK_NUMPAD0) | `0` or app keypad mode |
| A51-52 | 0 (wide) | 96 (VK_NUMPAD0) | `0` |
| A53 | . | 110 (VK_DECIMAL) | `.` |
| B51 | 1 | 97 (VK_NUMPAD1) | `1` |
| B52 | 2 | 98 (VK_NUMPAD2) | `2` |
| B53 | 3 | 99 (VK_NUMPAD3) | `3` |
| C51 | 4 | 100 (VK_NUMPAD4) | `4` |
| C52 | 5 | 101 (VK_NUMPAD5) | `5` |
| C53 | 6 | 102 (VK_NUMPAD6) | `6` |
| C54 | + | 107 (VK_ADD) | `+` |
| D51 | 7 | 103 (VK_NUMPAD7) | `7` |
| D52 | 8 | 104 (VK_NUMPAD8) | `8` |
| D53 | 9 | 105 (VK_NUMPAD9) | `9` |
| D54 | - | 109 (VK_SUBTRACT) | `-` |

---

## Special Function Row Keys

| Grid Pos | Key Name | VK Code | Description |
|----------|----------|---------|-------------|
| F51 | F1 | 112 | `ESC[50_` |
| F52 | HEX SO | 0 | Shift Out for HEX input |
| F53 | CLEAR | 12 | Clear screen |

---

## Modifier Keys

| Grid Pos | Key Name | VK Code | Purpose |
|----------|----------|---------|---------|
| B99 | SHIFT (Left) | 160 (VK_LSHIFT) | Modify key output |
| B11 | SHIFT (Right) | 161 (VK_RSHIFT) | Modify key output |
| D0 | CTRL | 162 (VK_LCONTROL) | Control key modifier |
| E0 | CAPS | 20 (VK_CAPITAL) | Caps Lock toggle |
| C0 | LOCK | 20 (VK_CAPITAL) | Lock mode toggle |
| C99 | MODE | 0 | Terminal mode switch |
| D99 | INNS/EXPS | 45 (VK_INSERT) | Insert/Expand mode |

---

## Complete Grid Position Reference

### Row G (Top Row) - Y=0

| Position | VK | Type | NO Label | EN Label | Sequence |
|----------|-----|------|----------|----------|----------|
| G0 | 27 | System | ESC | ESC | `0x1B` |
| G1 | 0 | PushKey | P1 | P1 | `ESC[?1~` |
| G2 | 0 | PushKey | P2 | P2 | `ESC[?2~` |
| G3 | 0 | PushKey | P3 | P3 | `ESC[?3~` |
| G4 | 0 | PushKey | P4 | P4 | `ESC[?4~` |
| G5 | 0 | PushKey | P5 | P5 | `ESC[?5~` |
| G6 | 0 | PushKey | P6 | P6 | `ESC[?6~` |
| G7 | 0 | PushKey | P7 | P7 | `ESC[?7~` |
| G8 | 0 | PushKey | P8 | P8 | `ESC[?8~` |
| G9 | 0 | Function | MERK | MARK | `ESC[00_` |
| G10 | 0 | Function | FELT | FIELD | `ESC[02_` |
| G11 | 0 | Function | AVSH | PARA | `ESC[04_` |
| G12 | 0 | Function | SETN | SENT | `ESC[06_` |
| G13 | 0 | Function | ORD | WORD | `ESC[08_` |
| G14 | 0 | Function | LOKAL | LOCAL | (local) |
| G47 | 46 | Special | STRYK | DELETE | `ESC[10_` |
| G48 | 67 | Special | KOPI | COPY | `ESC[12_` |
| G49 | 88 | Special | FLYTT | MOVE | `ESC[14_` |
| G51 | 0 | Special | FUNK | FUNC | `ESC[42_` |
| G52 | 44 | Special | SKRIV | PRINT | `ESC[44_` |
| G53 | 0 | Special | HJELP | HELP | `ESC[46_` |
| G54 | 0 | Special | SLUTT | EXIT | `ESC[48_` |

### Row F - Y=70

| Position | VK | Type | Label | Sequence |
|----------|-----|------|-------|----------|
| F47 | 0 | Normal | (empty) | - |
| F48 | 0 | Normal | (empty) | - |
| F49 | 0 | Normal | (empty) | - |
| F51 | 112 | Function | F1 | - | `ESC[50_` |
| F52 | 0 | Special | HEX SO | - |
| F53 | 12 | Special | CLEAR | - |
| F54 | 0 | Normal | (empty) | - |

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
| E13 | 192 | Normal | ´ | \ | `´` / `\` |
| E14 | 50 | Special | @ | - | `@` |

### Row D (QWERTY Row) - Y=210

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| D99 | 45 | Special | INNS/EXPS | - |
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
| D13 | 8 | System | DEL | `0x08` |
| D47 | 33 | Navigation | PG UP (ROLLUP) | `ESC[28_` |
| D48 | 90 | Special | ANGRE | (undo) |
| D49 | 34 | Navigation | PG DN (ROLLDN) | `ESC[32_` |
| D51-D54 | - | NumericPad | 7 8 9 - | digits |

### Row C (ASDF Row) - Y=280

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| C99 | 0 | Special | MODE | - |
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
| C47 | 46 | Special | ERASE PAGE | `0x19` (EM) |
| C48 | 46 | Special | ERASE LINE | `0x04` (EOT) |
| C49 | 45 | Navigation | INSERT | `ESC[36_` |
| C51-C54 | - | NumericPad | 4 5 6 + | digits |

### Row B (ZXCV Row) - Y=350

| Position | VK | Type | Primary | Sequence |
|----------|-----|------|---------|----------|
| B99 | 160 | Modifier | SHIFT | (modifier) |
| B0-B9 | varies | Normal | Z X C V B N M , . / | chars |
| B10 | 189 | Normal | - / _ | `-`/`_` |
| B11 | 161 | Modifier | SHIFT | (modifier) |
| B47 | 37 | Navigation | ← | `ESC[D` / `0x08` |
| B48 | 36 | Navigation | HOME | `ESC[H` / `0x1D` |
| B49 | 39 | Navigation | → | `ESC[C` / `0x18` |
| B51-B54 | - | NumericPad | 1 2 3 ENTER | digits |

### Row A (Bottom Row) - Y=420

| Position | VK | Type | Label | Sequence |
|----------|-----|------|-------|----------|
| A5 | 32 | System | SPACE | `0x20` |
| A47 | 96 | NumericPad | 0 | `0` |
| A48 | 40 | Navigation | ↓ | `ESC[B` / `0x0B` |
| A49 | 13 | System | →\| ENTER | `0x0D` |
| A51 | 96 | NumericPad | 0 (wide) | `0` |
| A53 | 110 | NumericPad | . | `.` |
| B54 | 13 | System | ENTER (tall) | `0x0D` |

---

## Sequence Notation Legend

| Notation | Meaning | Hex |
|----------|---------|-----|
| `ESC` | Escape character | 0x1B |
| `[` | CSI introducer (follows ESC) | 0x5B |
| `O` | SS3 introducer (follows ESC) | 0x4F |
| `~` | Sequence terminator for CSI | 0x7E |
| `P` | DCS introducer (follows ESC) | 0x50 |
| `\` | ST (String Terminator) | 0x5C |

---

## Source Files Reference

- Keyboard Layout: `src\RetroTerm.Desktop\Models\TDV2200KeyLayout.cs`
- VK Mapper: `src\RetroTerm.Core\Terminal\Input\KeyboardMapper.cs`
- TDV Mapper: `src\RetroTerm.Core\Terminal\Emulators\TDV\TDVKeyboardMapper.cs`
- Virtual Keyboard: `src\RetroTerm.Desktop\Controls\VirtualKeyboardPanel.axaml.cs`
- Key Definitions: `src\RetroTerm.Desktop\Models\KeyDefinition.cs`

---

## Specifications Reference

- TDV-Keys.md: `spec\TDV2200\Term and keyboard info\TDV-Keys.md`
- TDV2215.md: `spec\TDV2215\TDV2215.md` (Section 3.1 TDV 2115 Compatible Mode)
- keyboard_documentation.md: `spec\TDV2200\DOC TDV2200\keyboard_documentation.md`

---

*Document generated: 2026-02-06*
*Full path: docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md*
