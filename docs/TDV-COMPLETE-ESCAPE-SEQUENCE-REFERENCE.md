> # WARNING - THE MODE NUMBERS AND THE MODE QUERY IN THIS DOCUMENT ARE WRONG
>
> Checked against the real Tandberg and ND manuals on 11 September 2026. This document cites no
> source, and every mode number in it names a different switch in the manuals that do exist. It
> also describes a mode query, DECRQM, that appears in no TDV manual at all.
>
> **Read `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md` instead.** That document quotes the
> manual and the section for every statement, and its last part lists exactly what this one got
> wrong and why.
>
> This file is kept, rather than deleted, because code and tests were written from it and a reader
> who finds an old reference to it needs to be able to see what happened.

# TDV Complete Escape Sequence Reference

## TDV Model Overview

### TDV 2115 (Base Model)
- **Firmware ID**: 115
- **Features**: C0 control codes only, DLE cursor positioning, simple attributes
- **Compatibility**: Pure TDV 2115 mode via CSI ? 40 h

### TDV 2215 (Extended TDV 2115)
- **Firmware ID**: 115
- **Features**: TDV 2115 + extended features via EC switch
- **EC Switch**: ESC Q enables extended operation

### TDV 1200 (ND Version)
- **Firmware ID**: 120
- **Features**: VT100 + ND extensions, full CSI support
- **Compatibility**: Similar to TDV 2200 but different behavior details

### TDV 2200/9S (Advanced Model)
- **Firmware ID**: 220
- **Features**: Full CSI support, extended features, ND private codes
- **Compatibility**: Complete TDV feature set

## TDV 2115 Base Features (Foundation)

### TDV 2115 C0 Control Codes

| Code | Hex | Name | Function |
|------|-----|------|----------|
| STX  | 0x02 | Video off | Turns video display off |
| ETX  | 0x03 | Video on | Turns video display on |
| EOT  | 0x04 | Erase line | Erases current line |
| ENQ  | 0x05 | Light 1 on | Turns LED 1 on |
| ACK  | 0x06 | Light 2 on | Turns LED 2 on |
| BEL  | 0x07 | Bell | Standard bell |
| BS   | 0x08 | Backspace | Cursor left |
| HT   | 0x09 | Tab | Horizontal tab |
| LF   | 0x0A | Line feed | Cursor down |
| VT   | 0x0B | Cursor down | Cursor down |
| FF   | 0x0C | Roll up | Page up/scroll up |
| CR   | 0x0D | Cursor return | Cursor to start of line |
| SO   | 0x0E | Underline | Set underline attribute — **ONLY IN 2115 COMPATIBILITY MODE** (ND private mode 66). In native TDV mode 0x0E is Shift Out, G1 invocation. See TDV-COMPREHENSIVE-REFERENCE.md and TDV2115CompatibilityHandler.cs |
| SI   | 0x0F | Normal | Clear underline attribute — **ONLY IN 2115 COMPATIBILITY MODE** (ND private mode 66). In native TDV mode 0x0F is Shift In, G0 invocation |
| DLE  | 0x10 | Cursor load | Start binary cursor positioning |
| SYN  | 0x16 | Keyboard lights off | All LEDs off |
| ETB  | 0x17 | Roll down | Page down/scroll down |
| CAN  | 0x18 | Cursor right | Cursor right |
| EM   | 0x19 | Erase page | Erase entire screen |
| FS   | 0x1C | Cursor up | Cursor up |
| GS   | 0x1D | Cursor home | Cursor to home position |

### TDV 2115 Compatibility Mode

- **Activated by**: CSI ? 40 h
- **Deactivated by**: CSI ? 40 l
- **Special**: ESC Q (turns EC switch ON, enables extended operation)
- **Behavior**: In 2115 mode, only C0 codes accepted, ESC sequences ignored (except ESC Q)

### DLE (Direct Line Entry) Cursor Positioning

DLE uses a two-byte binary positioning system:
1. First byte: Line number (0-24) with 5-bit mask (0b11111)
2. Second byte: Column number (0-79) with 5-bit mask (0b11111)

Example: DLE 0x10 0x15 positions cursor at line 16, column 21

## TDV 2215/2200 Extended Features

### ND Private Character Set Switching

| Sequence | Function |
|----------|----------|
| ESC 1 | NDSS1 - Graphics I |
| ESC 2 | NDSS2 - Graphics II |
| ESC 3 | NDSS3 - Math |
| ESC 4 | NDSS4 - Greek |
| ESC 5 | NDSS5 - Diacritics |
| ESC 6 | NDSS6 - Box |
| ESC 7 | NDSS7 - NIX |
| ESC 8 | NDSS8 - T |
| ESC 9 | NDSS9 - ND private |

### Single Shift Sequences

| Sequence | Function |
|----------|----------|
| ESC N | SS2 - Single shift to G2 |
| ESC O | SS3 - Single shift to G3 |

### Locking Shift Sequences

| Sequence | Function |
|----------|----------|
| ESC n | LS2 - Locking shift to G2 |
| ESC o | LS3 - Locking shift to G3 |

### Character Set Designation

| Sequence | Function |
|----------|----------|
| ESC ( A | Designate G0 as US ASCII |
| ESC ( B | Designate G0 as UK ASCII |
| ESC ( 0 | Designate G0 as Graphics I |
| ESC ( 1 | Designate G0 as Graphics II |
| ESC ( 2 | Designate G0 as Math |
| ESC ( 3 | Designate G0 as Greek |
| ESC ( 4 | Designate G0 as Diacritics |
| ESC ( 5 | Designate G0 as Box |
| ESC ( 6 | Designate G0 as NIX |
| ESC ( 7 | Designate G0 as T |
| ESC ( 8 | Designate G0 as ND private |
| ESC ) A | Designate G1 as US ASCII |
| ESC ) B | Designate G1 as UK ASCII |
| ESC ) 0 | Designate G1 as Graphics I |
| ESC ) 1 | Designate G1 as Graphics II |
| ESC ) 2 | Designate G1 as Math |
| ESC ) 3 | Designate G1 as Greek |
| ESC ) 4 | Designate G1 as Diacritics |
| ESC ) 5 | Designate G1 as Box |
| ESC ) 6 | Designate G1 as NIX |
| ESC ) 7 | Designate G1 as T |
| ESC ) 8 | Designate G1 as ND private |
| ESC * A | Designate G2 as US ASCII |
| ESC * B | Designate G2 as UK ASCII |
| ESC * 0 | Designate G2 as Graphics I |
| ESC * 1 | Designate G2 as Graphics II |
| ESC * 2 | Designate G2 as Math |
| ESC * 3 | Designate G2 as Greek |
| ESC * 4 | Designate G2 as Diacritics |
| ESC * 5 | Designate G2 as Box |
| ESC * 6 | Designate G2 as NIX |
| ESC * 7 | Designate G2 as T |
| ESC * 8 | Designate G2 as ND private |
| ESC + A | Designate G3 as US ASCII |
| ESC + B | Designate G3 as UK ASCII |
| ESC + 0 | Designate G3 as Graphics I |
| ESC + 1 | Designate G3 as Graphics II |
| ESC + 2 | Designate G3 as Math |
| ESC + 3 | Designate G3 as Greek |
| ESC + 4 | Designate G3 as Diacritics |
| ESC + 5 | Designate G3 as Box |
| ESC + 6 | Designate G3 as NIX |
| ESC + 7 | Designate G3 as T |
| ESC + 8 | Designate G3 as ND private |

### Three-Character Escape Sequences

| Sequence | Function |
|----------|----------|
| ESC # 3 | Double-height line, top half |
| ESC # 4 | Double-height line, bottom half |
| ESC # 5 | Single-width line |
| ESC # 6 | Double-width line |

### TDV Private Control Codes

| Code | Hex | Function |
|------|-----|----------|
| ND1  | 0xB1 | ND control sequence 1 |
| ND2  | 0xB2 | ND control sequence 2 |
| ND3  | 0xB3 | ND control sequence 3 |
| ND4  | 0xB4 | ND control sequence 4 |
| ND8  | 0xB8 | ND control sequence 8 |
| CE   | 0xCE | Parameter parsing |
| CF   | 0xCF | Parameter dispatch |
| FC   | 0xFC | Function control |

### TDV Private CSI Sequences

#### Work Area Operations

| Sequence | Function |
|----------|----------|
| CSI ? 70 h | Set work area 1 |
| CSI ? 71 h | Set work area 2 |
| CSI ? 72 h | Set work area 3 |
| CSI ? 73 h | Set work area 4 |
| CSI ? 74 h | Set work area 5 |

#### Rectangle Operations

| Sequence | Function |
|----------|----------|
| CSI ? 75 h | Rectangle operations enable |
| CSI ? 76 h | Rectangle operations disable |
| CSI ? 7A h | Rectangle fill |
| CSI ? 7B h | Rectangle copy |
| CSI ? 7C h | Rectangle move |
| CSI ? 7D h | Rectangle delete |

#### System Configuration

| Sequence | Function |
|----------|----------|
| CSI ? 77 h | System configuration 1 |
| CSI ? 78 h | System configuration 2 |
| CSI ? 79 h | System configuration 3 |
| CSI ? 7E h | System configuration 4 |
| CSI ? 7F h | System configuration 5 |

#### LED Control

| Sequence | Function |
|----------|----------|
| CSI ? A | LED 1 on |
| CSI ? B | LED 2 on |
| CSI ? C | LED 3 on |

## DCS (Device Control String) Sequences

### DCS Introduction and Termination

| Sequence | Function |
|----------|----------|
| ESC P | DCS - Start device control string |
| ESC \ | ST - String terminator |

### TDV DCS Sequences

| Sequence | Function |
|----------|----------|
| DCS 0 q | Query terminal capabilities |
| DCS 1 q | Set terminal mode |
| DCS 2 q | Reset terminal mode |
| DCS 3 q | Set cursor type |
| DCS 4 q | Set character attributes |
| DCS 5 q | Set display mode |
| DCS 6 q | Set keyboard mode |
| DCS 7 q | Set communication mode |
| DCS 8 q | Set printer mode |
| DCS 9 q | Set terminal configuration |

## Function Key Mappings

### TDV 2215 Function Keys

| Key | Normal | Shift | Ctrl |
|-----|--------|-------|------|
| F1  | 11~    | 23~   | 35~  |
| F2  | 12~    | 24~   | 36~  |
| F3  | 13~    | 25~   | 37~  |
| F4  | 14~    | 26~   | 38~  |
| F5  | 15~    | 27~   | 39~  |
| F6  | 16~    | 28~   | 40~  |
| F7  | 17~    | 29~   | 41~  |
| F8  | 18~    | 30~   | 42~  |
| F9  | 19~    | 31~   | 43~  |
| F10 | 20~    | 32~   | 44~  |
| F11 | 21~    | 33~   | 45~  |
| F12 | 22~    | 34~   | 46~  |

### TDV 2200/9S Function Keys

| Key | Normal | Shift | Ctrl |
|-----|--------|-------|------|
| F1  | 11~    | 23~   | 35~  |
| F2  | 12~    | 24~   | 36~  |
| F3  | 13~    | 25~   | 37~  |
| F4  | 14~    | 26~   | 38~  |
| F5  | 15~    | 27~   | 39~  |
| F6  | 16~    | 28~   | 40~  |
| F7  | 17~    | 29~   | 41~  |
| F8  | 18~    | 30~   | 42~  |
| F9  | 19~    | 31~   | 43~  |
| F10 | 20~    | 32~   | 44~  |
| F11 | 21~    | 33~   | 45~  |
| F12 | 22~    | 34~   | 46~  |

## Query/Response Sequences

### Device Attributes

| Query | Response | Function |
|-------|----------|----------|
| CSI 0 c | CSI ? 1 ; 0 c | Primary device attributes |
| CSI > 0 c | CSI > 1 ; 0 c | Secondary device attributes |

### Cursor Position Report

| Query | Response | Function |
|-------|----------|----------|
| CSI 6 n | CSI row ; col R | Report cursor position |

### Device Status Report

| Query | Response | Function |
|-------|----------|----------|
| CSI 5 n | CSI 0 n | Device OK |
| CSI 6 n | CSI 1 n | Device busy |

### Terminal Identification

| Query | Response | Function |
|-------|----------|----------|
| CSI 0 c | CSI ? 115 ; 0 c | TDV 2115/2215 |
| CSI 0 c | CSI ? 220 ; 0 c | TDV 2200 |
| CSI 0 c | CSI ? 120 ; 0 c | TDV 1200 |

### Mode Queries

| Query | Response | Function |
|-------|----------|----------|
| CSI ? 1 h | CSI ? 1 h | Cursor keys mode |
| CSI ? 2 h | CSI ? 2 h | ANSI/VT52 mode |
| CSI ? 3 h | CSI ? 3 h | Column mode |
| CSI ? 4 h | CSI ? 4 h | Scrolling mode |
| CSI ? 5 h | CSI ? 5 h | Screen mode |
| CSI ? 6 h | CSI ? 6 h | Origin mode |
| CSI ? 7 h | CSI ? 7 h | Wrap mode |
| CSI ? 8 h | CSI ? 8 h | Auto-repeat mode |
| CSI ? 9 h | CSI ? 9 h | Interlace mode |
| CSI ? 25 h | CSI ? 25 h | Cursor visibility |
| CSI ? 40 h | CSI ? 40 h | TDV 2115 compatibility mode |

## Implementation Status

### Completed Features
- [ ] TDV 2115 C0 control codes
- [ ] DLE cursor positioning
- [ ] Basic character set switching
- [ ] Query/response sequences
- [ ] Function key mapping
- [ ] Rectangle operations
- [ ] Work area operations
- [ ] DCS sequences
- [ ] TDV configuration system
- [ ] Bitmap font system
- [ ] Test server enhancements

### Pending Features
- [ ] Complete character set implementation
- [ ] Advanced DCS features
- [ ] Full rectangle operations
- [ ] Work area management
- [ ] TDV-specific keyboard mapping
- [ ] Performance optimization
- [ ] Comprehensive testing

## Notes

1. **TDV 2115 Compatibility**: All TDV terminals can operate in TDV 2115 compatibility mode via CSI ? 40 h
2. **EC Switch**: TDV 2215 uses ESC Q to enable extended control features
3. **Character Sets**: TDV supports 9 character sets with proper designation and locking
4. **Function Keys**: All TDV terminals use the same function key mapping scheme
5. **DCS Sequences**: Device control strings provide advanced terminal configuration
6. **Rectangle Operations**: TDV terminals support advanced rectangle manipulation
7. **Work Areas**: Multiple work areas can be defined and managed
8. **Query/Response**: Comprehensive terminal identification and status reporting
