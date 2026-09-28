# TDV 2215 Extended Mode - Complete ESC and CSI Sequence Reference

## Overview

When the TDV 2215 Extended Control switch (4.2.12) is ON, the terminal supports a comprehensive set of escape sequences beyond the basic TDV 2115 compatibility mode. These sequences follow ECMA-6, ECMA-35, and ECMA-48 standards where applicable, with TDV 2215-specific extensions.

**Activation**: ESC Q enables extended mode from TDV 2115 compatibility mode.

## Mode Compatibility

### TDV 2115 Compatible Mode (Extended Control = OFF)
- Only C0 control codes (00-1F) accepted
- ESC sequences ignored except **ESC Q** (enables extended mode)
- Sequence behavior: ESC ignored, remaining characters displayed

### Extended Mode (Extended Control = ON)  
- Full C0 control set
- C1 control codes via ESC sequences
- CSI sequences with parameters
- Three-character ESC sequences
- Device control strings (DCS)

---

## C0 Control Characters (00-1F)

### TDV 2115 Compatible Codes (Available in both modes)

| Code | Hex | Mnemonic | TDV 2115 Function | VT100 Compatible |
|------|-----|----------|-------------------|-------------------|
| 06 | 06 | ACK | Light 2 on keyboard comes on | No - TDV specific |
| 07 | 07 | BEL | Bell | Yes |
| 08 | 08 | BS | Backspace | Yes |
| 09 | 09 | HT | Horizontal Tab | Extended mode only |
| 0A | 0A | LF | Line Feed | Yes |
| 0B | 0B | VT | Cursor Down | Different from VT100 |
| 0C | 0C | FF | Roll Up | No - TDV specific |
| 0D | 0D | CR | Cursor Return | Yes |
| 0E | 0E | SO | Underline/Attribute start | Different from VT100 |
| 0F | 0F | SI | Normal/Attribute end | Different from VT100 |
| 10 | 10 | DLE | Cursor Load (direct addressing) | No - TDV specific |
| 15 | 15 | NAK | Light 3 on keyboard comes on | No - TDV specific |
| 16 | 16 | SYN | Keyboard lights are turned off | No - TDV specific |
| 17 | 17 | ETB | Roll Down | No - TDV specific |
| 18 | 18 | CAN | Cursor Right | Different from VT100 |
| 19 | 19 | EM | Erase Page | No - TDV specific |
| 1A | 1A | EOT | Erase Line | Different from VT100 |
| 1C | 1C | FS | Cursor Up | No - TDV specific |  
| 1D | 1D | GS | Cursor Home | No - TDV specific |
| 1E | 1E | STX | Video Off | No - TDV specific |
| 1F | 1F | ETX | Video On | No - TDV specific |

### Extended Mode Additional C0 Codes

| Code | Hex | Mnemonic | Function | VT100 Compatible |
|------|-----|----------|----------|-------------------|
| 09 | 09 | HT | Horizontal Tab | Yes |
| 1B | 1B | ESC | Lead-in for additional controls | Yes |

---

## C1 Control Codes (ESC 40-5F)

Available only in Extended Mode. Represented as ESC + character in range 40-5F.

| Sequence | Hex | C1 Name | Function | VT100 Compatible |
|----------|-----|---------|----------|-------------------|
| ESC [ | 1B 5B | CSI | Control Sequence Introducer | Yes |
| ESC ^ | 1B 5E | PM | Privacy Message | Partial |
| ESC N | 1B 4E | SS2 | Single Shift 2 | No - Character set |
| ESC O | 1B 4F | SS3 | Single Shift 3 | No - Character set |
| ESC P | 1B 50 | DCS | Device Control String (PUSH-key loading) | Partial |
| ESC \ | 1B 5C | ST | String Terminator | Yes |
| ESC H | 1B 48 | HTS | Horizontal Tab Set | Yes |

---

## Three-Character ESC Sequences (ESC 20-2F 30-3F)

Extended mode only. Format: ESC + intermediate + final character.

| Sequence | Function | VT100 Compatible |
|----------|----------|-------------------|
| ESC # 3 | DWL - Double-width line (top half) | Yes |
| ESC # 4 | DWL - Double-width line (bottom half) | Yes |  
| ESC # 5 | SWL - Single-width line | Yes |
| ESC # 6 | DWL - Double-width line (single height) | Yes |

---

## CSI Sequences (ESC [ Pn...Pn final)

Control sequences with numeric parameters. Format: ESC [ [parameters] final_character

Parameters are ASCII numbers separated by semicolons (;). Omitted parameters use default values.

### Cursor Movement

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Pn A | Pn = count | CUU - Cursor Up | 1 | Yes |
| ESC [ Pn B | Pn = count | CUD - Cursor Down | 1 | Yes |
| ESC [ Pn C | Pn = count | CUF - Cursor Forward | 1 | Yes |
| ESC [ Pn D | Pn = count | CUB - Cursor Backward | 1 | Yes |
| ESC [ Pl ; Pc H | Pl=line, Pc=column | CUP - Cursor Position | 1;1 | Yes |
| ESC [ Pl ; Pc f | Pl=line, Pc=column | HVP - Horizontal/Vertical Position | 1;1 | Yes |

### Tabulation

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Pn Z | Pn = count | CBT - Cursor Backward Tabulation | 1 | Yes |
| ESC [ Ps g | Ps = type | TBC - Tabulation Clear | 0 | Yes |

**TBC Parameters:**
- Ps = 0: Clear tab at current position
- Ps = 3: Clear all tabs

### Editing Functions

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Pn @ | Pn = count | ICH - Insert Character | 1 | Yes |
| ESC [ Pn P | Pn = count | DCH - Delete Character | 1 | Yes |
| ESC [ Pn L | Pn = count | IL - Insert Line | 1 | Yes |
| ESC [ Pn M | Pn = count | DL - Delete Line | 1 | Yes |
| ESC [ Pn X | Pn = count | ECH - Erase Character | 1 | Yes |

### Erase Functions

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Ps J | Ps = mode | ED - Erase in Display | 0 | Yes |
| ESC [ Ps K | Ps = mode | EL - Erase Line | 0 | Yes |

**ED Parameters:**
- Ps = 0: Erase from cursor to end of display
- Ps = 1: Erase from start of display to cursor  
- Ps = 2: Erase entire display

**EL Parameters:**
- Ps = 0: Erase from cursor to end of line
- Ps = 1: Erase from start of line to cursor
- Ps = 2: Erase entire line

### Graphic Rendition

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Ps...Ps m | Ps = attributes | SGR - Select Graphic Rendition | 0 | Yes |

**SGR Parameters (can be combined with semicolons):**
- 0: Normal (reset all attributes)
- 1: Bold/High intensity  
- 2: Low intensity
- 4: Underline
- 5: Blink
- 7: Inverse video
- 8: Invisible (concealed)

### Printer Control

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Ps i | Ps = mode | MC - Media Copy | 0 | Partial |

**MC Parameters:**
- Ps = 0: Print screen
- Ps = 4: Turn off printer pass-through  
- Ps = 5: Turn on printer pass-through

### Mode Setting

| Sequence | Parameters | Function | Default | VT100 Compatible |
|----------|------------|----------|---------|-------------------|
| ESC [ Ps...Ps h | Ps = mode numbers | SM - Set Mode | - | Partial |
| ESC [ Ps...Ps l | Ps = mode numbers | RM - Reset Mode | - | Partial |

**Mode Numbers:**
- 20: LNM - Line Feed/New Line Mode

---

## Device Status Report (DSR) - Host Response Sequences

The TDV 2215 supports device status reporting as documented in the specification.

### Status Query

| Sequence | Function | Response Format | VT100 Compatible |
|----------|----------|-----------------|-------------------|
| ESC [ 5 n | DSR - Device Status Report | ESC [ 0 n (OK) or ESC [ 3 n (Error) | Yes |
| ESC [ 6 n | CPR - Cursor Position Report | ESC [ Pl ; Pc R | Yes |

### Response Sequences (Terminal → Host)

| Response | Format | Description | Notes |
|----------|--------|-------------|-------|
| ESC [ 0 n | Device Status | Terminal OK | Standard VT100 response |
| ESC [ 3 n | Device Status | Terminal has malfunction | Standard VT100 response |
| ESC [ Pl ; Pc R | Cursor Position | Current line (Pl) and column (Pc) | Line/column are 1-based |

**Implementation Notes:**
- Cursor position report shows current cursor location
- Line and column numbers start from 1 (not 0)
- Error status reflects hardware self-test results
- Firmware includes "Cursor position:" string handling

---

## DCS Sequences (Device Control String)

Used for PUSH-key programming from the host.

### PUSH-Key Programming

| Sequence | Function | Format |
|----------|----------|--------|
| ESC P Ps ST | Program PUSH-key | ESC P key_number string ESC \ |

**Parameters:**
- Ps = PUSH-key number (0-15: 0-7 normal, 8-15 shifted)
- string = sequence to store (text and/or control characters)
- ST = String Terminator (ESC \)

**Example:**
- `ESC P 0 Hello World ESC \` - Programs PUSH-key 1 (normal) with "Hello World"
- `ESC P 8 ESC [ H ESC \` - Programs PUSH-key 1 (shifted) with cursor home sequence

---

## Keyboard Function Key Sequences

When Extended Control is ON, special keys generate escape sequences. The TDV 2215 firmware includes keyboard character normalization and transmission handling.

### Control and Navigation Keys

| Key | TDV 2115 Mode | Extended Mode | VT100 Compatible |
|-----|---------------|---------------|-------------------|
| Cursor Up | Cursor Up (1C) | ESC [ A | Partial |
| Cursor Down | Cursor Down (0B) | ESC [ B | Partial |  
| Cursor Right | Cursor Right (18) | ESC [ C | Partial |
| Cursor Left | Backspace (08) | ESC [ D | Partial |
| Home | Cursor Home (1D) | ESC [ H | Yes |

### Special Function Keys

| Key | Function | Extended Mode | Notes |
|-----|----------|---------------|-------|
| MODE | Setup access | No sequence | Local function only |
| PRINT | Print control | Depends on printer mode | May send MC sequence |
| LINE | Online control | No sequence | Local modem control |
| CLEAR | Clear lamps | No sequence | Local function |

## Debugging and Diagnostic Features

### Transparent Mode

When Send-Receive Mode switch (4.3.1) = TRANSPARENT, the terminal enters debug mode:

**Character Display Mapping:**
| Code Range | Display Format | Description |
|------------|----------------|-------------|
| 00 | Blank space | Null character |
| 01-1F | Hex values rotated 90° | Control codes visible |
| 20-7F | Standard ASCII | Normal text |
| 80-FF | Underlined characters | High bit set indication |

**Behavior:**
- **Received data**: Displayed directly with above mapping
- **Transmitted data**: Shown in inverse video/low intensity
- **Control decoding**: Disabled (no sequence processing)
- **Exit method**: MODE key twice to access setup menu
- **Use case**: System programmer debugging aid

### Error Reporting and Status

**Hardware Self-Test:**
- Power-on diagnostic sequence
- Error codes displayed on malfunction
- ERROR lamp indication on keyboard
- Status available via ESC [ 5 n query

**Common Error Conditions:**
- NVRAM corruption or failure
- Z80SIO communication errors
- Display controller problems
- Keyboard communication failure

### Character Set and Encoding

**256-Character Support:**
- **Standard ASCII**: 0x20-0x7F (96 characters)
- **Semigraphic Set**: Line drawing, histogram, plot characters
- **Subscript/Superscript**: Numeric formatting characters
- **Control Display**: Visible representation in transparent mode

**Character Generator:**
- **Matrix**: 9×14 dot cell (effective 7×9 character matrix)
- **Anti-glare**: Bonded anti-reflex faceplate
- **Adjustable**: Height, tilt, swivel stand

### Communication Diagnostics

**Transmission Monitoring:**
- Character-by-character transmission tracking
- Flow control status (XON/XOFF states)
- Buffer utilization monitoring
- Error detection and recovery

**Interface Testing:**
- Keyboard to host transmission test: `test_keyboard_to_host_transmission()`
- Keyboard communication test: `test_keyboard_communication()`
- Z80SIO port testing: `test_z80sio_ports()`
- Printer status checking: `check_printer_status()`

### PUSH-Keys (Programmable Function Keys)

| Key | Normal Mode | Shifted Mode | Description |
|-----|-------------|--------------|-------------|
| PUSH 1 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 2 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 3 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 4 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 5 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 6 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 7 | User programmable | User programmable | 12 bytes max sequence |
| PUSH 8 | User programmable | User programmable | 12 bytes max sequence |

**Programming Methods:**
1. **Local**: Via setup menu (MODE key twice + SHIFT) if PKP switch = ALLOWED
2. **Remote**: Via DCS sequences from host (always available in extended mode)

**Storage**: NVRAM addresses 0x60-0x17B (16 sequences × 12 bytes each)

---

## TDV 2215 Specific Extensions

### Non-VT100 Compatible Features

1. **TDV 2115 Control Codes**: Unique functions like cursor load (DLE), video control, keyboard lights
2. **Roll Operations**: Roll up/down with smooth scroll option
3. **Attribute Mode**: SO Y SI sequences for graphic rendition
4. **PUSH-Key Programming**: 16 programmable sequences via DCS
5. **Printer Modes**: Local/remote/log modes with specific control
6. **Double-Width Characters**: Line-by-line basis vs character-by-character

### VT100 Compatibility Notes

- **Basic cursor movement**: Compatible
- **Character attributes**: SGR sequences compatible, but TDV also supports attribute mode
- **Editing functions**: Standard CSI sequences supported
- **Tab handling**: Standard tab set/clear functions
- **Device status**: Standard DSR responses

### Extended Capabilities

- **256 Character Set**: Includes semigraphic characters (line drawing, histogram, subscript, superscript)
- **Soft-Switch Control**: Remote configuration via SM/RM sequences
- **Background Printing**: Optional print buffer with MC control
- **Advanced Flow Control**: XON/XOFF and DTR handshaking

---

## Implementation Notes

### Character Set Support
- **G0**: Primary character set (ASCII + semigraphics)
- **G1**: Alternate character set (semigraphics)
- **SS2/SS3**: Single shift to G2/G3 (limited support)

### Graphic Rendition Modes
1. **ATTR Mode**: TDV 2115 style attributes occupy screen positions
2. **UNDERLINE Mode**: SO/SI control individual characters  
3. **SGR Mode**: Full CSI m sequence support

## Advanced Features and Implementation Details

### Scrolling and Roll Operations

The TDV 2215 implements sophisticated scrolling with both TDV 2115 compatibility and extended features:

| Function | Implementation | VT100 Compatible |
|----------|----------------|-------------------|
| ROLL UP (FF/0C) | `scroll_display_region_up()` | No - TDV specific |
| ROLL DOWN (ETB/17) | `scroll_display_region_down()` | No - TDV specific |
| Smooth Scroll | Hardware line-by-line scrolling | No - TDV specific |
| Step Scroll | Text line-by-line scrolling | Partial |

**Roll Type Control:**
- **STEP**: One text line at a time (stepwise)
- **SMOOTH**: One matrix dot line at a time (continuous)
- Controlled by Roll Type switch (4.2.9)

### Tabulation System

Advanced tab handling with NVRAM storage:

| Function | Implementation | Storage |
|----------|----------------|---------|
| Default tabs | 8-column intervals | Built-in |
| Custom tabs | User-definable positions | NVRAM |
| Tab advance | `cursor_tab_advance()` | Hardware |
| Tab buffer | Circular buffer system | RAM |

**Tabulation Functions:**
- `get_default_tab_stop_count()`: Returns default tab spacing
- `get_next_tab_stop_from_buffer()`: Retrieves custom tab positions
- Supports both permanent and temporary tab rack settings

### Character Attribute System

Three distinct graphic rendition modes:

#### 1. ATTR Mode (TDV 2115 Compatible)
```
SO Y SI - Where Y encodes attributes:
bit 6,5: 00=control, 01=low intensity, 10=inverse, 11=normal
bit 4:   0=primary function, 1=alternate (blink/underline/invisible)
bits 3-0: Additional control/positioning
```

#### 2. UNDERLINE Mode (TDV 2115 Compatible)  
```
SO - Start underline/special rendition
SI - Return to normal rendition
```

#### 3. SGR Mode (Extended - VT100 Compatible)
```
CSI Ps m - Select Graphic Rendition
Multiple parameters supported with semicolon separation
```

**Implementation:**
- `calculate_character_attribute_code()`: Computes display attributes
- `process_character_with_attributes()`: Applies rendering
- `set_attribute_processing_mode()`: Switches between modes

### Control Character Dispatch System

The firmware uses a sophisticated dual dispatch system:

```assembly
; Control Character Dispatcher (0x25A6)
MOV B,A          ; Save character
ANI 0x1F         ; Mask to 5 bits (C0 range)
ADD A            ; Index * 2 (16-bit pointers)
; Jump table lookup and call

; High Character Dispatcher (0x25A0)  
MOV B,A          ; Save character
ANI 0x0F         ; Mask to 4 bits (high chars)
ADD A            ; Index * 2
; Jump table lookup and call
```

**Character Range Handling:**
- **0x00-0x1F**: C0 control characters (32 entries)
- **0xB0-0xBF**: High control characters (16 entries)  
- **Other ranges**: Standard character processing

### Video Control System

Hardware video control with software interface:

| Function | Control Code | Implementation |
|----------|--------------|----------------|
| Video On | ETX (1F) | `enable_video_off_control()` |
| Video Off | STX (1E) | `disable_video_off_control()` |
| Timeout Control | 10 minutes | Automatic hardware control |

### Memory and Buffer Management

**Character Buffers:**
- Keyboard input buffer with atomic operations
- Printer output buffer (optional 2000 characters)
- Circular buffer system for tab stops
- Display memory with attribute separation

**Flow Control:**
- XON/XOFF software handshaking
- DTR/RTS hardware handshaking  
- Character transmission masking
- Buffer overflow protection