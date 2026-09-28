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

# TDV Terminal Comprehensive Reference

## Document Version
Created: 2025-11-19
**Purpose**: Complete reference for all TDV terminal implementations in RetroTerm

---

## Table of Contents
1. [Implemented TDV Models](#implemented-tdv-models)
2. [Common TDV Features (All Models)](#common-tdv-features-all-models)
3. [TDV1200 Specifications](#tdv1200-specifications)
4. [TDV2215 Specifications](#tdv2215-specifications)
5. [TDV2200 Specifications](#tdv2200-specifications)
6. [TDV2115 Compatibility Mode](#tdv2115-compatibility-mode)
7. [Keyboard Mappings](#keyboard-mappings)
8. [Testing Matrix](#testing-matrix)

---

## Implemented TDV Models

| Model | Implementation | Scrollback | Special Features |
|-------|---------------|------------|------------------|
| **TDV1200** | ✅ TDV1200Emulator.cs | 1,500 lines | 2115 compatibility mode |
| **TDV2115** | ✅ Compatibility Mode | N/A | Mode in TDV1200/2215/2200 |
| **TDV2215** | ✅ TDV2215Emulator.cs | 10,000 lines | Extended mode, transparent mode, 2115 compat |
| **TDV2200** | ✅ TDV2200Emulator.cs | 1,000 lines | Full feature set, 2115 compat, DCS |

**Implementation Status**: All 4 TDV terminals implemented
- TDV2115 is implemented as a **compatibility mode** switchable in all emulators

---

## Common TDV Features (All Models)

### Architecture
All TDV emulators extend `TDVEmulatorBase` which provides:
- VT100 compatibility layer
- ISO 6429 (ECMA-48) compliance
- Protected areas (SPA/EPA)
- Work areas (NDDWA)
- Message LEDs
- Rectangle operations
- Character set management (G0-G3)

### C0 Control Codes (0x00-0x1F)

**Standard ISO 6429 C0 Codes** - All Models:

| Code | Hex | Mnem | Function | Auto Repeat |
|------|-----|------|----------|-------------|
| 0x00 | 00 | NUL | Null | No |
| 0x05 | 05 | ENQ | Enquiry (LED 1 ON) | No |
| 0x06 | 06 | ACK | Acknowledge (LED 2 ON) | No |
| 0x07 | 07 | BEL | Bell | No |
| 0x08 | 08 | BS | Backspace | Yes |
| 0x09 | 09 | HT | Horizontal Tab | No |
| 0x0A | 0A | LF | Line Feed | Yes |
| 0x0B | 0B | VT | Vertical Tab (Cursor Down) | Yes |
| 0x0C | 0C | FF | Form Feed (Roll Up) | Yes |
| 0x0D | 0D | CR | Carriage Return | No |
| 0x0E | 0E | SO | Shift Out (G1 active) — **in NATIVE mode only**; inside 2115 compatibility mode (ND private mode 66) this byte sets UNDERLINE instead. See TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md | No |
| 0x0F | 0F | SI | Shift In (G0 active) — **in NATIVE mode only**; inside 2115 compatibility mode this byte CLEARS underline | No |
| 0x10 | 10 | DLE | Direct Line Entry (cursor load) | No |
| 0x15 | 15 | NAK | Negative Ack (LED 3 ON) | No |
| 0x16 | 16 | SYN | Clear Lamps | No |
| 0x17 | 17 | ETB | Roll Down | Yes |
| 0x18 | 18 | CAN | Cursor Right | Yes |
| 0x19 | 19 | EM | Erase Page | No |
| 0x1B | 1B | ESC | Escape | No |
| 0x1C | 1C | FS | Cursor Up | Yes |
| 0x1D | 1D | GS | Cursor Home | No |

**TDV-Specific C0 Usage:**
- **Cursor Movement**: Uses C0 codes (0x08, 0x0B, 0x18, 0x1C, 0x1D), NOT ESC sequences
- **Scroll Operations**: 0x0C (Roll Up), 0x17 (Roll Down)
- **LED Control**: 0x05 (ENQ), 0x06 (ACK), 0x15 (NAK), 0x16 (SYN)

### ESC Sequences (ESC + single character)

**Standard ISO 6429:**
| Sequence | Hex | Function | All Models |
|----------|-----|----------|------------|
| ESC D | 1B 44 | IND - Index (move down) | ✅ |
| ESC E | 1B 45 | NEL - Next Line | ✅ |
| ESC H | 1B 48 | HTS - Horizontal Tab Set | ✅ |
| ESC M | 1B 4D | RI - Reverse Index | ✅ |
| ESC N | 1B 4E | SS2 - Single Shift 2 | ✅ |
| ESC O | 1B 4F | SS3 - Single Shift 3 | ✅ |
| ESC P | 1B 50 | DCS - Device Control String | ✅ |
| ESC \ | 1B 5C | ST - String Terminator | ✅ |
| ESC c | 1B 63 | RIS - Reset to Initial State | ✅ |

**Character Set Designation (ISO 2022):**
| Sequence | Function | All Models |
|----------|----------|------------|
| ESC ( X | Designate G0 character set | ✅ |
| ESC ) X | Designate G1 character set | ✅ |
| ESC * X | Designate G2 character set | ✅ |
| ESC + X | Designate G3 character set | ✅ |

**TDV-Specific ESC Sequences:**
| Sequence | Function | Models |
|----------|----------|--------|
| ESC # 3 | Double-height line (top half) | All |
| ESC # 4 | Double-height line (bottom half) | All |
| ESC # 5 | Single-width line | All |
| ESC # 6 | Double-width line | All |
| ESC 1-9 | NDSS (ND Single Shift) - char set 1-9 | All |
| ESC n | Invoke G2 as GL | All |
| ESC o | Invoke G3 as GL | All |
| ESC N | Invoke G2 as GL (single shift) | All |
| ESC O | Invoke G3 as GL (single shift) | All |

### CSI Sequences (ESC [ params letter)

**Standard ISO 6429 CSI - All Models:**

| Sequence | Function | Notes |
|----------|----------|-------|
| CSI n A | CUU - Cursor Up | n=1 default |
| CSI n B | CUD - Cursor Down | n=1 default |
| CSI n C | CUF - Cursor Forward | n=1 default |
| CSI n D | CUB - Cursor Back | n=1 default |
| CSI n E | CNL - Cursor Next Line | n=1 default |
| CSI n F | CPL - Cursor Previous Line | n=1 default |
| CSI n G | CHA - Cursor Horizontal Absolute | n=1 default |
| CSI r;c H | CUP - Cursor Position | r=1, c=1 default |
| CSI n J | ED - Erase in Display | n=0: below, 1: above, 2: all |
| CSI n K | EL - Erase in Line | n=0: right, 1: left, 2: all |
| CSI n L | IL - Insert Lines | n=1 default |
| CSI n M | DL - Delete Lines | n=1 default |
| CSI n P | DCH - Delete Characters | n=1 default |
| CSI n @ | ICH - Insert Characters | n=1 default |
| CSI n X | ECH - Erase Characters | n=1 default |
| CSI n S | SU - Scroll Up | n=1 default |
| CSI n T | SD - Scroll Down | n=1 default |
| CSI t;b r | DECSTBM - Set Top/Bottom Margins | Full screen default |
| CSI ps m | SGR - Select Graphic Rendition | Multiple params |

**SGR (Select Graphic Rendition) Parameters:**
- 0: Reset all attributes
- 1: Bold
- 2: Dim
- 4: Underline
- 5: Blink (slow)
- 7: Reverse video
- 8: Concealed
- 22: Normal intensity
- 24: Not underlined
- 25: Not blinking
- 27: Not reversed
- 30-37: Foreground colors
- 40-47: Background colors
- 38;5;n: 256-color foreground
- 48;5;n: 256-color background

**Mode Setting (SM/RM):**
| Sequence | Function |
|----------|----------|
| CSI n h | SM - Set Mode |
| CSI n l | RM - Reset Mode |
| CSI ? n h | DECSET - Set Private Mode |
| CSI ? n l | DECRST - Reset Private Mode |

**Common Modes:**
- 4: IRM - Insert/Replace Mode
- 20: LNM - Line Feed/New Line Mode
- ?1: DECCKM - Application Cursor Keys
- ?3: DECCOLM - 132 Column Mode
- ?6: DECOM - Origin Mode
- ?7: DECAWM - Auto Wrap Mode
- ?25: DECTCEM - Cursor Visibility
- ?40: TDV2115 Compatibility Mode (TDV-specific)

### Query/Response Sequences

**Device Attributes (DA) - All Models:**
| Query (Host→Term) | Response (Term→Host) | Description |
|-------------------|----------------------|-------------|
| CSI c | CSI ? 1 ; 2 c | Primary DA (VT100 compatible) |
| CSI 0 c | CSI ? 1 ; 2 c | Primary DA (explicit) |
| CSI > c | CSI > 0 ; 115 ; 0 c | Secondary DA (firmware ID 115) |
| CSI > 0 c | CSI > 0 ; 115 ; 0 c | Secondary DA (explicit) |

**Device Status Report (DSR) - All Models:**
| Query (Host→Term) | Response (Term→Host) | Description |
|-------------------|----------------------|-------------|
| CSI 5 n | CSI 0 n | Device OK |
| CSI 6 n | CSI r ; c R | Cursor Position Report |

**TDV-Specific Queries:**
| Query | Response | Description | Models |
|-------|----------|-------------|--------|
| CSI ? 15 n | CSI ? 15 ; w ; h n | Screen size | 2200, 2215 |
| CSI ? 25 n | CSI ? 25 ; vis ; 0 ; 0 n | Cursor visibility | 2200, 2215 |

### ND Private CSI Sequences (TDV-Specific)

**Protected Areas:**
| Sequence | Function | Hex Range |
|----------|----------|-----------|
| CSI p | SPA - Start Protected Area | - |
| CSI q | EPA - End Protected Area | - |

**Work Areas (NDDWA):**
| Sequence | Function |
|----------|----------|
| CSI t;l;b;r x | Define Work Area |

**Message LEDs:**
| Sequence | Function |
|----------|----------|
| CSI n y | NDCLED - Clear LED |
| CSI n z | NDSLED - Set LED |
| CSI n { | NDBLED - Blink LED |

**Rectangle Operations:**
| Sequence | Function |
|----------|----------|
| CSI t;l;b;r | | NDILWA - Insert Lines in Work Area |
| CSI t;l;b;r } | NDDLWA - Delete Lines in Work Area |
| CSI n ~ | NDICHE - Insert Characters (horizontal) |
| CSI n DEL | NDDCHE - Delete Characters (horizontal) |

**Character Set Control:**
| Sequence | Function |
|----------|----------|
| CSI 0 ` | Select character set 0 (US ASCII) |
| CSI 1-9 ` | Select ND character set 1-9 |

---

## TDV1200 Specifications

### Model Info
- **File**: `TDV1200Emulator.cs`
- **Base Class**: `TDVEmulatorBase`
- **Scrollback**: 1,500 lines
- **Default Size**: 80x24

### Unique Features
- Built-in TDV 2115 compatibility mode
- Basic VT100 + ND-specific support
- Component-based architecture (reuses TDV2200 components)

### Character Sets (Default)
- G0: Graphics I
- G1: Graphics I
- G2: Graphics II
- G3: Math

### Supported CSI Sequences
✅ All standard ISO 6429 CSI sequences
✅ All ND private CSI sequences (protected areas, work areas, LEDs, rectangles)
✅ TDV 2115 compatibility mode (CSI ? 40 h/l)

### Query/Response Support
✅ Primary DA: `CSI ? 1 ; 2 c`
✅ Secondary DA: `CSI > 0 ; 115 ; 0 c`
✅ DSR Status: `CSI 0 n`
✅ DSR Cursor Position: `CSI r ; c R`

### Keyboard
- Arrow keys: C0 control codes (0x1C, 0x0B, 0x18, 0x08, 0x1D)
- F1-F12: VT220 sequences (ESC[11~-24~)
- ALT+letter: Application key shortcuts (see keyboard section)

### Modes
✅ Insert/Replace Mode (IRM)
✅ Auto Wrap Mode (DECAWM)
✅ Origin Mode (DECOM)
✅ Application Cursor Keys (DECCKM)
✅ TDV 2115 Compatibility Mode (CSI ? 40 h/l)

---

## TDV2215 Specifications

### Model Info
- **File**: `TDV2215Emulator.cs`
- **Base Class**: `TDVEmulatorBase`
- **Scrollback**: 10,000 lines (largest)
- **Default Size**: 80x24

### Unique Features
- **Extended Mode**: Advanced control sequences
- **Transparent Mode**: Passthrough mode for special applications
- Built-in TDV 2115 compatibility mode
- Three-character ESC sequences
- Enhanced DCS support

### Character Sets (Default)
- G0: Graphics I
- G1: Graphics I
- G2: Graphics II
- G3: Math

### Supported CSI Sequences
✅ All standard ISO 6429 CSI sequences
✅ All ND private CSI sequences
✅ Extended mode CSI sequences (TDV2215-specific)
✅ TDV 2115 compatibility mode (CSI ? 40 h/l)

### Query/Response Support
✅ Primary DA: `CSI ? 1 ; 2 c`
✅ Secondary DA: `CSI > 0 ; 115 ; 0 c`
✅ DSR Status: `CSI 0 n`
✅ DSR Cursor Position: `CSI r ; c R`
✅ Screen Size Query: `CSI ? 15 ; w ; h n`
✅ Cursor Visibility Query: `CSI ? 25 ; vis ; 0 ; 0 n`

### Keyboard
- Arrow keys: C0 control codes (0x1C, 0x0B, 0x18, 0x08, 0x1D)
- F1-F12: VT220 sequences (ESC[11~-24~)
- F13-F20: Extended VT220 sequences (ESC[25~-34~)
- ALT+letter: Application key shortcuts
- Modifiers: Shift, Ctrl, Alt on navigation keys

### Modes
✅ All TDV1200 modes
✅ Extended Mode (TDV2215-specific)
✅ Transparent Mode (TDV2215-specific)

---

## TDV2200 Specifications

### Model Info
- **File**: `TDV2200Emulator.cs`
- **Base Class**: `TDVEmulatorBase`
- **Scrollback**: 1,000 lines
- **Default Size**: 80x24

### Unique Features
- **Most complete TDV implementation**
- Full DCS sequence support
- User-defined characters (UDC)
- PUSH key programming (P1-P8)
- Soft key support
- Built-in TDV 2115 compatibility mode
- Graphics extension support (optional)
- Tektronix mode support (optional)

### Character Sets (Default)
- G0: US ASCII
- G1: US ASCII
- G2: Graphics I
- G3: Graphics II

### Supported CSI Sequences
✅ All standard ISO 6429 CSI sequences
✅ All ND private CSI sequences
✅ TDV 2115 compatibility mode (CSI ? 40 h/l)
✅ Extended attribute sequences
✅ Rectangle operations

### DCS Sequences (Device Control Strings)
| DCS Sequence | Function |
|--------------|----------|
| ESC P @ ... ESC \ | Define soft key label |
| ESC P A ... ESC \ | Define soft key output sequence |
| ESC P L ... ESC \ | Load user-defined characters |
| ESC P N n ESC \ | Activate PUSH key n |

### Query/Response Support
✅ Primary DA: `CSI ? 1 ; 2 c`
✅ Secondary DA: `CSI > 0 ; 115 ; 0 c`
✅ DSR Status: `CSI 0 n`
✅ DSR Cursor Position: `CSI r ; c R`
✅ Screen Size Query: `CSI ? 15 ; w ; h n`
✅ Cursor Visibility Query: `CSI ? 25 ; vis ; 0 ; 0 n`

### Keyboard
- Arrow keys: C0 control codes (0x1C, 0x0B, 0x18, 0x08, 0x1D) **by default**
- Arrow keys: ESC sequences (ESC[A-D) **when `tdvArrowsIsEscCode=true`**
- F1-F12: VT220 sequences (ESC[11~-24~)
- F13-F20: Extended VT220 sequences (ESC[25~-34~)
- P1-P8: Programmable PUSH keys (user-defined)
- ALT+letter: Application key shortcuts
- Modifiers: Shift, Ctrl, Alt on navigation keys

### Modes
✅ All TDV1200 modes
✅ TDV 2115 Compatibility Mode (CSI ? 40 h/l)
✅ Graphics Extension Mode (optional)
✅ Tektronix Mode (optional)

---

## TDV2115 Compatibility Mode

### Implementation
TDV2115 is **NOT a separate emulator** - it's a **compatibility mode** available in:
- TDV1200 (via `TDV2115CompatibilityHandler`)
- TDV2215 (via `TDV2115CompatibilityHandler`)
- TDV2200 (via `TDV2115CompatibilityHandler`)

### Activation
**Enable**: `CSI ? 40 h` (DECSET 40)
**Disable**: `CSI ? 40 l` (DECRST 40)

### When Active
- Cursor report format: TDV2115 format (1-based)
- Function keys: Send shorter/different escape strings
- Character sets: Use TDV2115 default font mappings
- Soft keys: Emulate fixed-key layout from 2115
- Reduced feature set (basic terminal operations only)

### Component
**File**: `TDV2115CompatibilityHandler.cs`, `TDV2115CompatibilityMode.cs`

### Character Sets in 2115 Mode
Different defaults than modern TDV terminals - consult TDV2115 spec for details.

---

## Keyboard Mappings

### Arrow Keys (TDV-Specific C0 Codes)

**Default Behavior** (All TDV Models):
| Key | Transmits | Hex | Mnemonic |
|-----|-----------|-----|----------|
| Up | 0x1C | 1C | FS (File Separator) |
| Down | 0x0B | 0B | VT (Vertical Tab) |
| Right | 0x18 | 18 | CAN (Cancel) |
| Left | 0x08 | 08 | BS (Backspace) |
| Home | 0x1D | 1D | GS (Group Separator) |

**Fallback Mode** (TDV2200 only, when `tdvArrowsIsEscCode=true`):
| Key | Transmits | Notes |
|-----|-----------|-------|
| Up | ESC[A | VT100-style |
| Down | ESC[B | VT100-style |
| Right | ESC[C | VT100-style |
| Left | ESC[D | VT100-style |
| Home | ESC[H | VT100-style |

### Arrow Keys with Modifiers (VT220-Style)

**When ESC mode active OR explicit modifier pressed:**
| Key | Shift | Ctrl | Alt |
|-----|-------|------|-----|
| Up | ESC[1;2A | ESC[1;5A | ESC[1;3A |
| Down | ESC[1;2B | ESC[1;5B | ESC[1;3B |
| Right | ESC[1;2C | ESC[1;5C | ESC[1;3C |
| Left | ESC[1;2D | ESC[1;5D | ESC[1;3D |

**Modifier Encoding:**
- `;2` = Shift
- `;3` = Alt (Meta)
- `;4` = Shift+Alt
- `;5` = Ctrl
- `;6` = Shift+Ctrl
- `;7` = Alt+Ctrl
- `;8` = Shift+Alt+Ctrl

### Function Keys (VT220-Style)

**F1-F12** (All Models):
| Key | Base Sequence | Shift | Ctrl | Alt |
|-----|---------------|-------|------|-----|
| F1 | ESC[11~ | ESC[11;2~ | ESC[11;5~ | ESC[11;3~ |
| F2 | ESC[12~ | ESC[12;2~ | ESC[12;5~ | ESC[12;3~ |
| F3 | ESC[13~ | ESC[13;2~ | ESC[13;5~ | ESC[13;3~ |
| F4 | ESC[14~ | ESC[14;2~ | ESC[14;5~ | ESC[14;3~ |
| F5 | ESC[15~ | ESC[15;2~ | ESC[15;5~ | ESC[15;3~ |
| F6 | ESC[17~ | ESC[17;2~ | ESC[17;5~ | ESC[17;3~ |
| F7 | ESC[18~ | ESC[18;2~ | ESC[18;5~ | ESC[18;3~ |
| F8 | ESC[19~ | ESC[19;2~ | ESC[19;5~ | ESC[19;3~ |
| F9 | ESC[20~ | ESC[20;2~ | ESC[20;5~ | ESC[20;3~ |
| F10 | ESC[21~ | ESC[21;2~ | ESC[21;5~ | ESC[21;3~ |
| F11 | ESC[23~ | ESC[23;2~ | ESC[23;5~ | ESC[23;3~ |
| F12 | ESC[24~ | ESC[24;2~ | ESC[24;5~ | ESC[24;3~ |

**F13-F20** (TDV2215, TDV2200):
| Key | Base Sequence |
|-----|---------------|
| F13 | ESC[25~ |
| F14 | ESC[26~ |
| F15 | ESC[28~ |
| F16 | ESC[29~ |
| F17 | ESC[31~ |
| F18 | ESC[32~ |
| F19 | ESC[33~ |
| F20 | ESC[34~ |

**Note**: F-key modifiers (Shift/Ctrl/Alt+F13-F20) are **NOT currently implemented**.

### ALT+Letter Shortcuts (Access Special Keys)

**Application Control Keys:**
| Shortcut | Sends | Description |
|----------|-------|-------------|
| ALT+H | ESC[28~ | HELP (HJÄLP) |
| ALT+D | ESC[29~ | DO (Execute) |
| ALT+U | ESC[@ | FUNC (FUNK) |
| ALT+P | ESC[A | PRINT (SKRIV) |
| ALT+X | ESC[C | EXIT (SLUT) |
| ALT+C | ESC[27~ | CANCEL |
| ALT+M | ESC[26~ | COMMAND |
| ALT+F | ESC[1;2R | FIND |
| ALT+S | ESC[4;2~ | SELECT |

**TDV1200 Editing Keys:**
| Shortcut | Sends | Description |
|----------|-------|-------------|
| ALT+K | ESC[M | COPY (KOPI) |
| ALT+V | ESC[N | MOVE (FLYTT) |
| ALT+J | ESC[ H | JUST (Justify) |
| ALT+A | ESC[X | MARK |
| ALT+L | ESC[Y | FIELD |
| ALT+R | ESC[Z | PARA (Paragraph) |
| ALT+E | ESC[[ | SENT (Sentence) |
| ALT+W | ESC[\ | WORD |
| ALT+I | ESC[2;2~ | INSERT HERE |

**PUSH Keys:**
| Shortcut | Sends | Description |
|----------|-------|-------------|
| ALT+1 | ESC P N 1 ESC \ | PUSH Key 1 |
| ALT+2 | ESC P N 2 ESC \ | PUSH Key 2 |
| ALT+3 | ESC P N 3 ESC \ | PUSH Key 3 |
| ALT+4 | ESC P N 4 ESC \ | PUSH Key 4 |
| ALT+5 | ESC P N 5 ESC \ | PUSH Key 5 |
| ALT+6 | ESC P N 6 ESC \ | PUSH Key 6 |
| ALT+7 | ESC P N 7 ESC \ | PUSH Key 7 |
| ALT+8 | ESC P N 8 ESC \ | PUSH Key 8 |

**Navigation Keys:**
| Shortcut | Sends | Description |
|----------|-------|-------------|
| ALT+Delete | ESC[3;2~ | REMOVE |
| ALT+PageUp | ESC[5;2~ | PREV |
| ALT+PageDown | ESC[6;2~ | NEXT |

### Navigation Keys (Standard)

| Key | Base | Shift | Ctrl | Alt |
|-----|------|-------|------|-----|
| Insert | ESC[2~ | ESC[2;2~ | ESC[2;5~ | ESC[2;3~ |
| Delete | 0x7F (DEL) | ESC[3;2~ | ESC[3;5~ | ESC[3;3~ |
| Home | 0x1D (GS) | ESC[1;2~ | ESC[1;5~ | ESC[1;3~ |
| End | ESC[F | ESC[1;2F | ESC[1;5F | ESC[1;3F |
| PageUp | ESC[5~ | ESC[5;2~ | ESC[5;5~ | ESC[5;3~ |
| PageDown | ESC[6~ | ESC[6;2~ | ESC[6;5~ | ESC[6;3~ |

---

## Testing Matrix

### Feature Support Matrix

| Feature | TDV1200 | TDV2215 | TDV2200 | TDV2115 Mode |
|---------|---------|---------|---------|--------------|
| **C0 Control Codes** | ✅ | ✅ | ✅ | ✅ |
| **Standard ESC Sequences** | ✅ | ✅ | ✅ | ✅ |
| **Standard CSI Sequences** | ✅ | ✅ | ✅ | ✅ |
| **ND Private CSI** | ✅ | ✅ | ✅ | Limited |
| **Protected Areas (SPA/EPA)** | ✅ | ✅ | ✅ | ✅ |
| **Work Areas (NDDWA)** | ✅ | ✅ | ✅ | Limited |
| **Message LEDs** | ✅ | ✅ | ✅ | ✅ |
| **Rectangle Operations** | ✅ | ✅ | ✅ | Limited |
| **Character Sets (G0-G3)** | ✅ | ✅ | ✅ | ✅ |
| **ISO 646 Variants** | ✅ | ✅ | ✅ | ✅ |
| **DCS Sequences** | Basic | Enhanced | Full | No |
| **PUSH Keys (P1-P8)** | ✅ | ✅ | ✅ Programmable | No |
| **Soft Keys** | Basic | Enhanced | Full | No |
| **User-Defined Characters** | No | Limited | ✅ | No |
| **2115 Compatibility Mode** | ✅ | ✅ | ✅ | Native |
| **Extended Mode** | No | ✅ | No | No |
| **Transparent Mode** | No | ✅ | No | No |
| **Graphics Extension** | No | No | ✅ Optional | No |
| **Tektronix Mode** | No | No | ✅ Optional | No |

### Query/Response Support Matrix

| Query | TDV1200 | TDV2215 | TDV2200 | TDV2115 Mode |
|-------|---------|---------|---------|--------------|
| Primary DA (CSI c) | ✅ | ✅ | ✅ | ✅ |
| Secondary DA (CSI > c) | ✅ | ✅ | ✅ | ✅ |
| DSR Status (CSI 5 n) | ✅ | ✅ | ✅ | ✅ |
| DSR Cursor (CSI 6 n) | ✅ | ✅ | ✅ | ✅ (1-based) |
| Screen Size (CSI ? 15 n) | ❌ | ✅ | ✅ | ❌ |
| Cursor Visibility (CSI ? 25 n) | ❌ | ✅ | ✅ | ❌ |

### Keyboard Support Matrix

| Feature | TDV1200 | TDV2215 | TDV2200 |
|---------|---------|---------|---------|
| **Arrow Keys (C0 codes)** | ✅ | ✅ | ✅ |
| **Arrow Keys (ESC fallback)** | ❌ | ❌ | ✅ Optional |
| **Arrow Modifiers (Shift/Ctrl/Alt)** | ✅ | ✅ | ✅ |
| **F1-F12** | ✅ | ✅ | ✅ |
| **F13-F20** | ❌ | ✅ | ✅ |
| **F-key Modifiers** | ❌ | ❌ | ❌ |
| **ALT+Letter Shortcuts** | ✅ | ✅ | ✅ |
| **PUSH Keys (P1-P8)** | ✅ Fixed | ✅ Fixed | ✅ Programmable |
| **Navigation Key Modifiers** | ✅ | ✅ | ✅ |

---

## Validation Checklist

### C0 Control Code Tests
- [ ] 0x08 (BS) - Backspace moves cursor left
- [ ] 0x0B (VT) - Cursor down (not line feed)
- [ ] 0x0C (FF) - Roll up (smooth scroll)
- [ ] 0x0D (CR) - Carriage return
- [ ] 0x17 (ETB) - Roll down
- [ ] 0x18 (CAN) - Cursor right
- [ ] 0x1C (FS) - Cursor up
- [ ] 0x1D (GS) - Cursor home
- [ ] 0x19 (EM) - Erase page

### ESC Sequence Tests
- [ ] ESC D - Index (cursor down with scroll)
- [ ] ESC M - Reverse index (cursor up with scroll)
- [ ] ESC c - Reset to initial state
- [ ] ESC # 3/4 - Double-height lines
- [ ] ESC # 5/6 - Single/double width lines
- [ ] ESC 1-9 - ND character set selection

### CSI Sequence Tests
- [ ] CSI H - Cursor position (home)
- [ ] CSI r;c H - Cursor position (specific)
- [ ] CSI n J - Erase in display (0/1/2)
- [ ] CSI n K - Erase in line (0/1/2)
- [ ] CSI n m - SGR attributes
- [ ] CSI t;b r - Set scroll region
- [ ] CSI ? 40 h/l - 2115 compatibility mode

### Query/Response Tests
- [ ] CSI c → CSI ? 1 ; 2 c
- [ ] CSI > c → CSI > 0 ; 115 ; 0 c
- [ ] CSI 5 n → CSI 0 n
- [ ] CSI 6 n → CSI r ; c R

### Keyboard Tests
- [ ] Right arrow sends 0x18 (not ESC[C)
- [ ] Up arrow sends 0x1C (not ESC[A)
- [ ] Down arrow sends 0x0B (not ESC[B)
- [ ] Left arrow sends 0x08 (not ESC[D)
- [ ] Home sends 0x1D (not ESC[H)
- [ ] ALT+H sends ESC[28~ (HELP)
- [ ] ALT+1 sends PUSH key 1 DCS
- [ ] Shift+Up sends ESC[1;2A
- [ ] Ctrl+Up sends ESC[1;5A
- [ ] Alt+Up sends ESC[1;3A

### TDV-Specific Tests
- [ ] Protected areas (SPA/EPA) prevent modification
- [ ] Work area boundaries respected
- [ ] LED control (NDCLED/NDSLED/NDBLED)
- [ ] Rectangle operations work correctly
- [ ] Character set switching (ESC 1-9)
- [ ] PUSH key programming (TDV2200)

---

## Document Maintenance

**Last Updated**: 2025-11-19
**Test Coverage**: Based on implemented code, needs validation
**Known Gaps**:
- F-key modifier support not implemented
- Some TDV2215 extended mode sequences need documentation
- Graphics extension and Tektronix mode details not included

**Files Referenced:**
- `TDVEmulatorBase.cs` - Base TDV functionality
- `TDV1200Emulator.cs` - TDV1200 implementation
- `TDV2215Emulator.cs` - TDV2215 implementation
- `TDV2200Emulator.cs` - TDV2200 implementation
- `TDV2115CompatibilityHandler.cs` - 2115 compatibility
- `TDVKeyboardMapper.cs` - Keyboard mapping
- `KeyboardMapper.cs` - ALT+letter shortcuts
- `TerminalEmulatorBase.cs` - Base CSI sequences

---

**END OF DOCUMENT**
