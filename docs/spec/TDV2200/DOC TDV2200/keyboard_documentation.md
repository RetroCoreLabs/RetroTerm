# Tandberg Terminal Keyboard Documentation

## Overview

This document describes the keyboard layouts for two Tandberg terminal models:
- **TDV-2200/9S** (ND320 Norsk Data) - Layout 531300
- **TDV-2215** (General Purpose ASCII) - Layout 129903

## Keyboard Layout Structure

Each keyboard uses a multi-layer scan code system with the following structure:
- **0x000-0x07F**: Key flags (control bits)
- **0x080-0x0FF**: Normal key values
- **0x100-0x17F**: Shift held key values  
- **0x180-0x1FF**: Ctrl held key values

## Flag System

The `Flags` byte controls how each key is processed:

```
Flags: xxxxxxxx
       ||||||||
       |||||||+-- Bit 0: Use key with Normal (1=enabled, 0=disabled)
       ||||||+--- Bit 1: Reserved
       |||||+---- Bit 2: Reserved  
       ||||+----- Bit 3: Reserved
       |||+------ Bit 4: Prefix CTRL-key with 0xFF (1=yes, 0=no)
       ||+------- Bit 5: Treat Caps as Normal (1=yes, 0=no)
       |+-------- Bit 6: Reserved
       +--------- Bit 7: Disable key-repeat (1=disabled, 0=enabled)
```

Common flag values:
- **0x0F** (00001111): Standard alphanumeric keys
- **0x1F** (00011111): Special keys requiring FF prefix for Ctrl
- **0x37** (00110111): Function keys with special behavior

## Complete Key Mappings

### TDV-2215 Layout (129903) - Recommended for Implementation

| Position | Key Name | Normal | Shift | Ctrl | Flags | SDL Mapping |
|----------|----------|--------|-------|------|-------|-------------|
| **Letters A-Z** |
| C1 | A | 0x61 | 0x41 | 0x01 | 0x0F | SDLK_a |
| B5 | B | 0x62 | 0x42 | 0x02 | 0x0F | SDLK_b |
| B3 | C | 0x63 | 0x43 | 0x03 | 0x0F | SDLK_c |
| C3 | D | 0x64 | 0x44 | 0x04 | 0x0F | SDLK_d |
| D3 | E | 0x65 | 0x45 | 0x05 | 0x0F | SDLK_e |
| C4 | F | 0x66 | 0x46 | 0x06 | 0x0F | SDLK_f |
| C5 | G | 0x67 | 0x47 | 0x07 | 0x0F | SDLK_g |
| C6 | H | 0x68 | 0x48 | 0x08 | 0x0F | SDLK_h |
| D8 | I | 0x69 | 0x49 | 0x09 | 0x0F | SDLK_i |
| C7 | J | 0x6A | 0x4A | 0x0A | 0x0F | SDLK_j |
| C8 | K | 0x6B | 0x4B | 0x0B | 0x0F | SDLK_k |
| C9 | L | 0x6C | 0x4C | 0x0C | 0x0F | SDLK_l |
| B7 | M | 0x6D | 0x4D | 0x0D | 0x0F | SDLK_m |
| B6 | N | 0x6E | 0x4E | 0x0E | 0x0F | SDLK_n |
| D9 | O | 0x6F | 0x4F | 0x0F | 0x0F | SDLK_o |
| D10 | P | 0x70 | 0x50 | 0x10 | 0x0F | SDLK_p |
| D1 | Q | 0x71 | 0x51 | 0x11 | 0x0F | SDLK_q |
| D4 | R | 0x72 | 0x52 | 0x12 | 0x0F | SDLK_r |
| C2 | S | 0x73 | 0x53 | 0x13 | 0x0F | SDLK_s |
| D5 | T | 0x74 | 0x54 | 0x14 | 0x0F | SDLK_t |
| D7 | U | 0x75 | 0x55 | 0x15 | 0x0F | SDLK_u |
| B4 | V | 0x76 | 0x56 | 0x16 | 0x0F | SDLK_v |
| D2 | W | 0x77 | 0x57 | 0x17 | 0x0F | SDLK_w |
| B2 | X | 0x78 | 0x58 | 0x18 | 0x0F | SDLK_x |
| D6 | Y | 0x79 | 0x59 | 0x19 | 0x0F | SDLK_y |
| B1 | Z | 0x7A | 0x5A | 0x1A | 0x0F | SDLK_z |
| **Numbers 0-9** |
| E10 | 0 = | 0x30 | 0x3D | 0x00 | 0x17 | SDLK_0 |
| E1 | 1 ! | 0x31 | 0x21 | 0x00 | 0x17 | SDLK_1 |
| E2 | 2 " | 0x32 | 0x22 | 0x00 | 0x17 | SDLK_2 |
| E3 | 3 # | 0x33 | 0x23 | 0x00 | 0x17 | SDLK_3 |
| E4 | 4 $ | 0x34 | 0x24 | 0x00 | 0x17 | SDLK_4 |
| E5 | 5 % | 0x35 | 0x25 | 0x00 | 0x17 | SDLK_5 |
| E6 | 6 & | 0x36 | 0x26 | 0x00 | 0x17 | SDLK_6 |
| E7 | 7 / | 0x37 | 0x2F | 0x00 | 0x17 | SDLK_7 |
| E8 | 8 ( | 0x38 | 0x28 | 0x00 | 0x17 | SDLK_8 |
| E9 | 9 ) | 0x39 | 0x29 | 0x00 | 0x17 | SDLK_9 |
| **Navigation & Control** |
| C48 | Up | 0xB1 | — | 0x00 | 0x17 | SDLK_UP |
| A48 | Down | 0xB2 | — | 0x00 | 0x17 | SDLK_DOWN |
| B49 | Right | 0xB3 | — | 0x00 | 0x17 | SDLK_RIGHT |
| B47 | Left | 0xB4 | — | 0x00 | 0x17 | SDLK_LEFT |
| B48 | Home | 0xB8 | — | 0x00 | 0x37 | SDLK_HOME |
| **Special Keys** |
| A5 | Space | 0x20 | — | 0x00 | 0x1F | SDLK_SPACE |
| E13 | Backspace | 0x08 | — | 0x00 | 0x17 | SDLK_BACKSPACE |
| A49 | Tab Right | 0x09 | 0x99 | — | 0x3F | SDLK_TAB |
| C13 | CR | 0xCD | — | — | 0x37 | SDLK_RETURN |
| F53 | ESC | 0xDB | — | — | 0x37 | SDLK_ESCAPE |
| D13 | LF | 0x9A | — | — | 0x17 | SDLK_RETURN2 |
| E14 | DEL | 0x7F | — | — | 0x37 | SDLK_DELETE |
| **Function Keys P1-P8** |
| G1 | P1 | 0xA0 | 0xA8 | 0x81 | 0x3F | SDLK_F1 |
| G2 | P2 | 0xA1 | 0xA9 | 0x82 | 0x3F | SDLK_F2 |
| G3 | P3 | 0xA2 | 0xAA | 0x83 | 0x3F | SDLK_F3 |
| G4 | P4 | 0xA3 | 0xAB | 0x84 | 0x3F | SDLK_F4 |
| G5 | P5 | 0xA4 | 0xAC | 0x85 | 0x3F | SDLK_F5 |
| G6 | P6 | 0xA5 | 0xAD | 0x86 | 0x3F | SDLK_F6 |
| G7 | P7 | 0xA6 | 0xAE | 0x87 | 0x3F | SDLK_F7 |
| G8 | P8 | 0xA7 | 0xAF | 0x88 | 0x3F | SDLK_F8 |
| **Numeric Keypad** |
| A51 | 0 (np) | 0xE0 | — | — | 0x17 | SDLK_KP_0 |
| B51 | 1 (np) | 0xE1 | — | — | 0x17 | SDLK_KP_1 |
| B52 | 2 (np) | 0xE2 | — | — | 0x17 | SDLK_KP_2 |
| B53 | 3 (np) | 0xE3 | — | — | 0x17 | SDLK_KP_3 |
| C51 | 4 (np) | 0xE4 | — | — | 0x17 | SDLK_KP_4 |
| C52 | 5 (np) | 0xE5 | — | — | 0x17 | SDLK_KP_5 |
| C53 | 6 (np) | 0xE6 | — | — | 0x17 | SDLK_KP_6 |
| D51 | 7 (np) | 0xE7 | — | — | 0x17 | SDLK_KP_7 |
| D52 | 8 (np) | 0xE8 | — | — | 0x17 | SDLK_KP_8 |
| D53 | 9 (np) | 0xE9 | — | — | 0x17 | SDLK_KP_9 |
| C54 | - (np) | 0xEA | — | — | 0x17 | SDLK_KP_MINUS |
| A53 | . (np) | 0xEB | — | — | 0x17 | SDLK_KP_PERIOD |
| B54 | ENTER (np) | 0xED | — | — | 0x17 | SDLK_KP_ENTER |
| **System Keys** |
| G53 | MODE | 0x00 | 0xF8 | 0x00 | 0x34 | SDLK_INSERT |
| G54 | BREAK | 0xFE | 0xF9 | 0xF4 | 0x3D | SDLK_PAUSE |
| G52 | PRINT | 0x00 | 0xFC | 0xFD | 0x3C | SDLK_PRINTSCREEN |

## Special Key Combinations

### Configuration Menu Navigation
- **Enter Config**: MODE key (0xF8)
- **Cursor Up**: Up arrow (0xB1)
- **Cursor Down**: Down arrow (0xB2)
- **Cursor Left**: Left arrow (0xB4)  
- **Cursor Right**: Right arrow (0xB3)
- **Enter/Select**: Numeric keypad ENTER (0xED)
- **Exit**: ESC key (0xDB)

### Key Transmission Rules

1. **Normal Keys**: Transmit the Normal value directly
2. **Shift Keys**: Transmit the Shift value directly
3. **Ctrl Keys**: 
   - If Flags bit 4 is set: Send 0xFF prefix + Ctrl value
   - Otherwise: Send Ctrl value directly
4. **Special Flags**: Check bit 0 to determine if key is enabled for Normal mode

## Layout Differences

### TDV-2200/9S (531300) vs TDV-2215 (129903)

The main differences:
- **2200/9S**: Norwegian layout with Æ, Ø, Å characters
- **2215**: ASCII layout with standard punctuation
- **Function Keys**: Different P-key mappings
- **Special Keys**: Different system key assignments

## Implementation Notes

- Use TDV-2215 layout as base for broad compatibility
- Handle modifier keys (Shift, Ctrl) properly
- Implement FF prefix for special Ctrl combinations
- Support both regular and numeric keypad ENTER keys
- Map SDL scancodes to physical key positions

---
*Based on official Tandberg keyboard layout specifications 531300 and 129903*