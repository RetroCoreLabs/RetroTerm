# TDV 2215 Complete NVRAM Memory Map

## Overview
The TDV 2215 uses an ER3400 series non-volatile RAM (NVRAM) to store configuration data that persists across power cycles. This document provides a complete mapping of the NVRAM usage based on firmware analysis and the official specification.

## NVRAM Architecture
- **Chip**: ER3400 series NVRAM
- **Addressing**: CPU addresses mapped via `map_to_nvram_address()` function
- **Control**: A10 bit controls ER3400 mode (read/write vs erase/accept_address)
- **Access**: /S1 signal from R/W controls C0 (read vs write operations)

## Complete NVRAM Memory Layout

### Soft-Switch Configuration Data (0x50-0x5A) - 11 bytes
Packed bit-field storage for all 37 soft-switches, extracted via template system:

| NVRAM Address | Content | Template Byte | Switches Stored |
|---------------|---------|---------------|-----------------|
| 0x50 | Config Byte 0 | 0x0AA0 (0xE8) | Convenience: Cursor Type, Key Click, Margin Bell |
| 0x51 | Config Byte 1 | 0x0AA1 (0xE8) | Convenience: Auto Repeat, Key Rollover, Keyboard CAPS |
| 0x52 | Config Byte 2 | 0x0AA2 (0xFF) | Function: Time-out, Bell, Graphic Rendition Mode |
| 0x53 | Config Byte 3 | 0x0AA3 (0xF8) | Function: Underline Rep, Cursor Return, BOL Wrap |
| 0x54 | Config Byte 4 | 0x0AA4 (0xAF) | Function: EOL Wrap, Roll/Page Mode, Roll Type |
| 0x55 | Config Byte 5 | 0x0AA5 (0x80) | Function: Clear Lamps, PUSH-key Prog, Extended Control |
| 0x56 | Config Byte 6 | 0x0AA6 (0x80) | Function: Vertical Edit, Printer Mode, Printer FF |
| 0x57 | Config Byte 7 | 0x0AA7 (0x80) | Communication: SRM, Echo, Line/Local Status |
| 0x58 | Config Byte 8 | 0x0AA8 (0x80) | Communication: Online, Comm Clock, Handshake |
| 0x59 | Config Byte 9 | 0x0AA9 (0x80) | Communication: Modem, Code Length, Parity |
| 0x5A | Config Byte 10 | 0x0AAA (0x80) | Communication: Stop Bits, Speed, Delay, Mode, Printer settings |

### PUSH-Key Sequence Storage (16 sequences, 12 bytes each)
8 physical keys × 2 modes (normal/shifted) = 16 programmable sequences:

| Key | Normal Mode | Shifted Mode | Description |
|-----|-------------|--------------|-------------|
| PUSH-Key 1 | 0x60-0x6B | 0x6C-0x77 | 12 bytes each |
| PUSH-Key 2 | 0x78-0x83 | 0x84-0x8F | 12 bytes each |
| PUSH-Key 3 | 0x90-0x9B | 0x9C-0xA7 | 12 bytes each |
| PUSH-Key 4 | 0xA8-0xB3 | 0xB4-0xBF | 12 bytes each |
| PUSH-Key 5 | 0xC0-0xCB | 0xCC-0xD7 | 12 bytes each |
| PUSH-Key 6 | 0xD8-0xE3 | 0xE0-0xEB | 12 bytes each |
| PUSH-Key 7 | 0x100-0x10B | 0x120-0x12B | 12 bytes each |
| PUSH-Key 8 | 0x140-0x14B | 0x170-0x17B | 12 bytes each |

**Total PUSH-Key Storage**: 192 bytes (16 × 12 bytes)

### Tabulation Rack Storage
Location and size determined by configuration menu system:
- Horizontal tab stop positions
- Permanent/temporary tab settings
- Menu-controlled save/restore ("Make Tabulation Rack Permanent")

## Configuration Processing

### Template System
1. **Template Table**: Located at 0x0AA0-0x0AAA in ROM (11 bytes)
2. **Bit Extraction**: Each template byte defines bit-field extraction pattern
3. **Output**: 37 individual switch values stored at RAM 0x5F00-0x5F24
4. **Process**: `load_config_from_nvram_template()` function

### Switch Categories and Default Values

#### Convenience Switches (6 switches) - Section 4.1
1. **Cursor Type**: LINE (default), BLOCK
2. **Key Click**: ON (default), OFF  
3. **Margin Bell**: ON (default), OFF
4. **Auto Repeat**: ON (default), OFF
5. **Key Rollover**: ENABLED (default), DISABLED
6. **Keyboard CAPS on Power Up**: OFF (default), ON

#### Function Switches (15 switches) - Section 4.2
7. **Time-out**: ON (default), OFF
8. **Bell**: ON (default), OFF
9. **Graphic Rendition Mode**: ATTR (default), UNDERLINE, SGR
10. **Underline Representation**: UNDERLINE (default), multiple options
11. **Cursor Return**: CR (default), CRLF
12. **Beginning of Line Wrap**: STOP (default), WRAP
13. **End of Line Wrap**: STOP (default), WRAP
14. **Roll/Page Mode**: ROLL (default), PAGE
15. **Roll Type**: STEP (default), SMOOTH
16. **Clear Lamps**: BOTH (default), KEY, SYN
17. **PUSH-key Programming**: ALLOWED (default), PROHIBITED
18. **Extended Control**: OFF (default), ON - **CRITICAL COMPATIBILITY SWITCH**
19. **Vertical Editing Mode**: FOLLOWING (default)
20. **Printer Mode**: LOCAL/REM (default), REMOTE, LOG
21. **Printer Form Feed**: NONE (default), BEFORE, AFTER, BOTH

#### Communication Switches (16 switches) - Section 4.3
22. **Send Receive Mode**: SIMULTANEOUS (default), TRANSPARENT
23. **Echo**: ON (default), OFF
24. **Line/Local Status**: LINE (default), LOCAL
25. **Online**: ONLINE (default), TOGGLE
26. **Communication Clock**: ASY (default)
27. **Communication Handshake**: OFF (default), XON/XOFF, DTR
28. **Modem**: INHIBIT (default), LEASED, DIALLED
29. **Transmission Code Length**: 7BIT (default), 8BIT
30. **Transmission Code Parity**: NONE (default), EVEN, ODD
31. **Transmission Code Stop Bits**: 1BIT (default), 2BIT
32. **Transmission Speed**: 9600 baud (default), 50-19200 range
33. **Transmission Delay**: NONE (default), 20ms, 40ms, 60ms
34. **Communication Mode**: V.24 (default), V.11
35. **Printer Handshake**: OFF (default), XON/XOFF
36. **Printer Code Format**: 7EVEN (default), 7ODD, 8NONE, 8EVEN, 8ODD
37. **Printer Speed**: 1200 baud (default), 50-19200 range

## NVRAM Management

### Power-Up Sequence
1. Hardware power-on test includes NVRAM validation
2. Load permanent switches from NVRAM → temporary switches in RAM
3. Apply default values if NVRAM uninitialized or corrupted
4. Initialize terminal based on loaded configuration

### Configuration Menu System (Section 5)
- **Access**: MODE key twice while SHIFT pressed
- **Categories**: Convenience, Function, Communication switches + PUSH-keys + Tabulation
- **Permanent/Temporary**: Changes affect RAM copies, menu controls NVRAM write-back
- **Factory Reset**: Restore all defaults and save to NVRAM

### Remote Configuration
- **Host Control**: Most switches controllable via SM/RM commands (except communication format switches)
- **PUSH-Key Programming**: DCS (Device Control String) sequences from host
- **Limitations**: Host cannot access setup menus or make settings permanent

## Technical Notes

### NVRAM Reliability
- ER3400 series provides data retention without power
- Memory test functions validate NVRAM integrity during power-up
- Error codes displayed if NVRAM failures detected

### Compatibility
- **TDV 2115 Mode**: Extended Control = OFF, limited to C0 control codes
- **Extended Mode**: Extended Control = ON, full TDV 2215 capabilities
- **Activation**: ESC Q sequence enables extended mode from TDV 2115 mode

### Programming Interface
- **Local**: Setup menus (if PUSH-key Programming = ALLOWED)
- **Remote**: DCS sequences (always available when Extended Control = ON)
- **Format**: Text strings and/or control character sequences
- **Length**: Maximum 12 bytes per PUSH-key sequence

This comprehensive NVRAM map ensures proper understanding of the TDV 2215's non-volatile storage system and configuration management.