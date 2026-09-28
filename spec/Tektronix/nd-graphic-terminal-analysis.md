# Norsk Data Graphic Terminal Protocol Analysis

Analysis of the escape sequences and protocol used by `graphic-term-b00.test`
(TPE hardware diagnostic test program, version B00, 1986-10-17).

**Source**: Reverse-engineered from `D:\ND\S\testprog\x\graphic-term-b00.test` via
Ghidra disassembly (76 functions identified) and nd100x emulator traces.

**Cross-reference**: Tektronix 4014 User's Manual (070-1647-00, 1974/1982)

---

## Terminal Identification

- **TPE test name**: GRAPHIC-TERM
- **Device ID at load**: "646D" (hex for device number)
- **Version**: B00 (1986-10-17)
- **Dependencies**: TP-CON-1-A01, TP-LIB-1-A01 (oldest runtime version)
- **Font file**: GRAPHIC-TERM.FONT (downloadable fonts)
- **Hardcopy**: Epson MX100/MX80/FX80/RX80 printers

## Protocol Overview

The terminal uses a **hybrid protocol**:
- **Coordinate encoding**: Standard Tektronix 4014 (5-bit Hi/Lo pairs, 10-bit resolution)
- **Escape sequences**: Norsk Data proprietary `ESC "` (0x1B 0x22) format, NOT standard
  ANSI `ESC [` or Tektronix control characters
- **GIN mode response**: Tektronix 4014 compatible (status byte + 4 address bytes)
- **Extensions**: Rectangle fill, polygons, circles, arcs, fonts, graphic memory,
  copy window — features NOT present in standard Tektronix 4014

## Escape Sequence Format

All extended commands use `ESC "` (0x1B 0x22) as the Control Sequence Introducer (CSI),
followed by a mode number, optional semicolon-separated parameters, and a final letter:

```
ESC " <mode> ; <param1> ; <param2> ... <final_letter>
```

**Final letters**:
- `h` = Set mode (most common)
- `l` = Reset mode (counterpart to `h`)
- `d` = Device query/action
- `n` = Device status report
- `r` = Report/restore
- `a` = Character position
- `C` = Cursor/graphics position (capital C)

## Terminal Detection (Host → Terminal → Host)

### Query Sequence (sent at program load)

```
0x1F                    — Unit Separator (terminal reset)
ESC "13;10l             — Reset modes 13 and 10
ESC "5d                 — Device query (mode 5, action 'd')
ESC ENQ (0x1B 0x05)     — Terminal identification request (standard Tek 4014)
```

### Complete Detection Wire Protocol (from Ghidra disassembly of ram:c69b)

**Phase 1 — Host sends init + query:**
```
Hex                              ASCII/Meaning
─────────────────────────────    ──────────────────────────────
1F                               US — reset terminal to alpha mode
1B 22 31 33 3B 31 30 6C         ESC "13;10l — reset modes 13 and 10
1B 22 35 64                     ESC "5d — device query (mode 5, action 'd')
1B 05                           ESC ENQ — terminal identification request
```

**Phase 2 — Terminal must respond (7 bytes minimum):**
```
Byte  Encoding                  Content
────  ────────────────────────  ──────────────────────────────
0     raw (full byte)           Status byte (see below)
1     0x20 | (Y >> 5)           Hi Y — 5 high bits + tag 0x20
2     0x60 | (Y & 0x1F)        Lo Y — 5 low bits + tag 0x60
3     0x20 | (X >> 5)           Hi X — 5 high bits + tag 0x20
4     0x40 | (X & 0x1F)        Lo X — 5 low bits + tag 0x40
5     7-bit (AND 0x7F)          ND extension byte 1
6     7-bit (AND 0x7F)          ND extension byte 2
```

**If fewer than 7 bytes received** (checked at ram:c6fe-c701): detection FAILS →
"WARNING: Selected device IS NOT found to be a graphic terminal"

**Phase 3 — Host sends confirmation pattern (after detection passes):**
```
Hex                              ASCII/Meaning
─────────────────────────────    ──────────────────────────────
0D                               CR
2A 2A 2A 2A 2A 2A 2A 2A 2A 2A  "**********" (10 asterisks)
0D                               CR
5C 5C 5C 5C 5C 5C 5C 5C 5C 5C  "\\\\\\\\\\\\" (10 backslashes)
0D                               CR
41 31 31                         "A11" — terminal type identifier
1C                               FS — enters Point Plot mode (Tek 4014)
```

### Detection Validation Logic (ram:c6c3-c701)

The host reads bytes from the terminal in a loop (max 10 iterations):
1. Call `read_single_char` (0x6942) with timeout 150 cycles
2. If timeout → compare received timestamp against deadline, retry or exit
3. If character received → AND with 0x007F (strip to 7-bit ASCII)
4. Store in response buffer at ram:c57d via SBYT instruction
5. Increment byte counter at ram:c57b
6. If counter reaches 9 → exit loop
7. After loop: check `counter < 7` → FAIL

### Response Byte Parsing (ram:c751-c775)

After detection passes, individual bytes are extracted from the response buffer:

```c
// Extract bytes using SAX (set index) + LBYT (load byte)
byte status  = buffer[0];                           // → [B-0x7A]
int  y_coord = (buffer[1] & 0x03E0)                // Hi Y: bits 9-5
             | (buffer[2] & 0x001F);                // Lo Y: bits 4-0
                                                     // → [B-0x79]
int  x_coord = (buffer[3] & 0x03E0)                // Hi X: bits 9-5
             | (buffer[4] & 0x001F);                // Lo X: bits 4-0
                                                     // → [B-0x78]
byte nd_ext1 = buffer[5] & 0x007F;                  // → [B-0x77]
byte nd_ext2 = buffer[6] & 0x007F;                  // → [B-0x76]
```

### Tektronix 4014 Status Byte (from manual page 3-30)

| Bit | Value | Meaning |
|-----|-------|---------|
| 8 | varies | Arbitrary (strap option dependent) |
| 7 | **0** | Always 0 |
| 6 | **1** | Always 1 |
| 5 | 0/1 | Hard Copy Unit (0 = available, 1 = not available) |
| 4 | 0/1 | Vector bit (1 = Graph Mode vector exists) |
| 3 | 0/1 | Graph Mode bit (0 = Graph Mode exists) |
| 2 | 0/1 | Margin 2 exists |
| 1 | 0/1 | Auxiliary Unit Sensing (0 = aux device connected) |

**Example valid status byte**: 0x68 = `0110_1000`
- Bit 7=0 ✓, Bit 6=1 ✓, Bit 5=1 (no hardcopy), Bit 4=0, Bit 3=1 (not in graph), Bit 2=0, Bit 1=0

### Coordinate Encoding (Tektronix 4014 standard)

Each 10-bit coordinate (0-1023) is split into two 5-bit values with tag bits:

```
Hi byte: 0 0 1 D9 D8 D7 D6 D5    (tag = 001, data = bits 9-5)
Lo byte: T1 T0 1 D4 D3 D2 D1 D0  (tag varies, data = bits 4-0)

Y Lo tag: 11 (0x60 base)
X Hi tag: 01 (0x20 base, same as Y Hi)
X Lo tag: 10 (0x40 base)
```

**Encoding example** (cursor at X=512, Y=390):
```
Y = 390 = 0b0110000110
  Hi Y: 0x20 | (390 >> 5) = 0x20 | 12 = 0x2C
  Lo Y: 0x60 | (390 & 0x1F) = 0x60 | 6 = 0x66
X = 512 = 0b1000000000
  Hi X: 0x20 | (512 >> 5) = 0x20 | 16 = 0x30
  Lo X: 0x40 | (512 & 0x1F) = 0x40 | 0 = 0x40

Response: 0x68 0x2C 0x66 0x30 0x40 0x40 0x40
```

### Function Pointer Table (ram:c718-c783)

Complete data reference for `detect_graphic_terminal` (ram:c69b):

| Address | Value | Purpose |
|---------|-------|---------|
| c718 | 0x6902 | csav (PLANC frame setup) |
| c719 | 0x8DAD | Terminal device handler descriptor |
| c71a | 0x6932 | `terminal_raw_send` — send bytes to terminal |
| c71b | 0x6904 | `check_error` — verify I/O completion |
| c71c | 0xC785 | → test pattern data (10 asterisks + 10 backslashes + "A11") |
| c71d | 0xC69A | → ESC ENQ bytes (0x1B 0x05) |
| c71e | 0xD6FB | → output string function |
| c71f | 0x6930 | `read_with_timeout` — read with 150-cycle timeout |
| c721 | 0x0096 | Timeout value = 150 |
| c722 | 0xC57D | Response buffer base address |
| c723 | 0xC57B | Byte counter variable |
| c724 | 0xC57A | Byte position accumulator |
| c725 | 0x6942 | `read_single_char` — read one byte from terminal |
| c726 | 0xBCDA | `esc_read_response` — read and validate response |
| c727 | 0x007F | 7-bit ASCII mask for response parsing |
| c729 | 0x0032 | Scaling factor (50 decimal) |
| c72a | 0x6938 | `send_esc_prefix` — send ESC sequence to terminal |
| c72b | 0xC786 | → asterisk pattern data pointer |
| c72c | 0xC787 | → continuation pointer |
| c77e | 0x03E0 | Mask for Hi coordinate (bits 9-5) |
| c77f | 0x001F | Mask for Lo coordinate (bits 4-0) |
| c780 | 0x03E0 | Mask for Hi coordinate (bits 9-5) |
| c781 | 0x001F | Mask for Lo coordinate (bits 4-0) |
| c782 | 0x007F | Mask for ND extension byte 1 |
| c783 | 0x007F | Mask for ND extension byte 2 |

### Test Pattern Data (ram:c785-c794)

```
c785: 41 00              MIN 0 (data separator)
c786: 41 00              MIN 0
c787: 2A 2A 2A 2A 2A     "*****"
c78a: 2A 2A 2A 2A 2A     "*****"     (10 asterisks total)
c78c: 41 00              MIN 0 (data separator)
c78d: 5C 5C 5C 5C 5C     "\\\\\"
c790: 5C 5C 5C 5C 5C     "\\\\\"     (10 backslashes total)
c792: 41 31              "A1"
c793: 31 00              "1\0"       ("A11" null-terminated)
c794: 1C 00              FS + NUL    (enter Point Plot mode)
```

### ESC ENQ Data (ram:c69a)

```
c69a: 1B 05              ESC ENQ (terminal identification request)
```

### ND Extension Bytes 5-6 — Terminal Model Identification

The two extension bytes (masked to 7-bit each) identify the specific **Norsk Data
terminal model**. The test program uses these to distinguish between:

- **ND-324 / Notis** — Norsk Data graphics workstation
- **ND-325 / Net** — Network graphics terminal
- **ND-246** — Compact graphics terminal
- **ND-285** — Mid-range graphics terminal
- **ND-320** — Entry-level graphics terminal
- **ND-322** — Enhanced graphics terminal

The detection function at ram:c83f performs an additional **feature probe** after
the initial GIN identification:

### Feature Probe Sequence (ram:c83f)

After the initial 7-byte GIN response passes validation, the host probes individual
terminal features by sending:

```
HOST → TERMINAL (for each feature):
  1B 61          ESC 'a' — probe feature 'a'
  1B 62          ESC 'b' — probe feature 'b'
  1B 63          ESC 'c' — probe feature 'c'
  ...
  1B 7A          ESC 'z' — probe feature 'z'
  1B 60          ESC '`' — probe feature '`' (backtick)

TERMINAL → HOST (for each probe):
  XX             Single response byte per feature
                 Presence/absence of response indicates support
```

This probes 27 individual features (a-z plus backtick). Each `ESC + letter` asks
"do you support feature X?" and the terminal responds with a byte if it does, or
times out if it doesn't. The probe results determine which test commands are
available for the specific terminal model.

### Additional Control Sequences Found in Detection Area

| Address | Bytes | Sequence | Purpose |
|---------|-------|----------|---------|
| c61c | 1B 0C | ESC FF | Clear screen (both planes) + home cursor |
| c636 | 1B 17 | ESC ETB | End transmission block |
| c69a | 1B 05 | ESC ENQ | Terminal identification request |
| c7c8 | 1B 1A | ESC SUB | Enter GIN mode (display crosshair) |
| c8ec | 1B 27 | ESC ' | Group separator |
| ce47 | 1B 2A | ESC * | Set graphic memory address |

## Graphic Memory Protocol (from cmd_graphic_memory analysis)

The graphic memory read/write test uses special control characters:

### Memory Write Protocol

```
HOST → TERMINAL:
  19                     EM — cursor home / position reset
  16                     SYN — enter graphic memory WRITE mode
  <addr params>          Address parameters (coordinates)
  <bitmap data>          Pattern data bytes
  18                     CAN — exit write mode
```

### Memory Read / Readback Protocol

```
HOST → TERMINAL:
  19                     EM — cursor home
  16                     SYN — enter graphic memory mode
  18                     CAN — enter READBACK mode
  <addr params>          Read address parameters

TERMINAL → HOST:
  <data bytes>           Memory content bytes (readback)

HOST → TERMINAL:
  04                     EOT — end readback transmission
  10                     DLE — data link escape (cleanup)
```

### Memory Data Patterns

The test writes 3 different patterns and reads them back for verification:
- Pattern 1: fixed bit sequence
- Pattern 2: different fixed sequence
- Pattern 3: third pattern
Each pattern is 80 bytes (SAT 0x4F = 79 iterations + 1)

### Key Control Characters for Memory Operations

| Hex | ASCII | Purpose |
|-----|-------|---------|
| 0x04 | EOT | End readback transmission |
| 0x10 | DLE | Data link escape (memory protocol) |
| 0x16 | SYN | Enter graphic memory write mode |
| 0x18 | CAN | Cancel / switch between write and readback |
| 0x19 | EM | Cursor home / position reset |

## GIN Mode — Crosshair Input Protocol (from Ghidra disassembly)

### Entering GIN Mode

**Host → Terminal:**
```
1B 1A                    ESC SUB — display crosshair cursor, enter GIN mode
```
The crosshair cursor appears on screen. The terminal waits for user input.

Additionally, the test program sends these setup commands before entering GIN:
```
ESC "10;1h              Set cursor visibility ON
ESC "12;Vh              Set cursor style
ESC /f                  Select font
```

### User Interaction → Terminal Response

**When user presses ANY key while crosshair is displayed:**

The terminal automatically sends to the host:
```
TERMINAL → HOST (5 bytes + optional CR):
  Byte 0: key character   — the ASCII code of the key pressed
  Byte 1: 0x20 | (Y >> 5) — Hi Y coordinate of crosshair
  Byte 2: 0x60 | (Y & 0x1F) — Lo Y coordinate
  Byte 3: 0x20 | (X >> 5) — Hi X coordinate
  Byte 4: 0x40 | (X & 0x1F) — Lo X coordinate
  Byte 5: 0x0D           — CR (if strap option selected)
```

**When host sends ESC ENQ while crosshair is displayed:**

Same response but with status byte instead of key character:
```
TERMINAL → HOST (5-7 bytes):
  Byte 0: status byte     — terminal status (bit6=1, bit7=0)
  Bytes 1-4: crosshair position (same Tek 4014 encoding)
  Byte 5: CR (optional)
  Byte 6: EOT (optional)
```

### GIN Response Handler (ram:bb3a)

The test program reads the GIN response in this sequence:
```
Step 1: call 0x6920 (READ)  — read key character (param=0)
Step 2: call 0x6904         — check for errors
Step 3: call 0x6920 (READ)  — read coordinate bytes (param=2)
Step 4: call 0x6904         — check for errors
Step 5: call 0x696A         — decode coordinates from Tek 4014 encoding
Step 6: call 0x6904         — check for errors
Step 7: call 0x6910         — send acknowledgement
```

### Runtime Library I/O Functions

| Address | Function | Direction | Purpose |
|---------|----------|-----------|---------|
| 0x6920 | `terminal_read` | Terminal → Host | Read bytes from terminal |
| 0x6926 | `terminal_write` | Host → Terminal | Write/send bytes to terminal |
| 0x6930 | `read_with_timeout` | Terminal → Host | Read with timeout (detection) |
| 0x6932 | `terminal_raw_send` | Host → Terminal | Raw byte send (no params) |
| 0x6938 | `send_esc_prefix` | Host → Terminal | Send ESC sequence header |
| 0x6910 | `send_esc_params` | Host → Terminal | Send ESC sequence parameters |
| 0x6942 | `read_single_char` | Terminal → Host | Read exactly 1 byte |
| 0x696A | `decode_gin_data` | Internal | Parse Tek 4014 coordinate bytes |
| 0x690E | `show_description` | Host → Terminal | Display test description text |
| 0x6904 | `check_error` | Internal | Verify I/O completion |

### Inking Mode (cmd_inking, ram:a471)

Inking mode extends GIN mode with continuous drawing:

**Host → Terminal:**
```
ESC "16;1h              Enable inking mode
ESC SUB (1B 1A)         Enter GIN mode with crosshair
```

**User interaction:**
- Arrow keys move the crosshair
- While inking is enabled, moving the crosshair **draws lines** on the graphics plane
- Each arrow keypress sends the key code + new position back to host
- The terminal draws a vector from old position to new position

**Terminal → Host (on each arrow key):**
```
  Byte 0: arrow key code  — direction character
  Bytes 1-4: new crosshair position (Tek 4014 encoding)
  Byte 5: CR (optional)
```

**Host → Terminal (to stop inking):**
```
ESC "16;0h              Disable inking mode
```

### Crosshair Position Coordinate System

```
        Y=779 (top)
          │
          │     Crosshair (+)
          │        │
          │        ▼
   X=0 ───┼─────────────── X=1023
          │
          │
          │
        Y=0 (bottom)

Origin: bottom-left (0,0)
Tek 4014 standard coordinate system
Y increases upward (opposite of screen coordinates)
```

### For RetroTerm Implementation

**Mouse → GIN coordinates:**
```csharp
// Convert screen pixel to Tek 4014 coordinates
int tekX = (mouseX * 1024) / screenWidth;
int tekY = 779 - (mouseY * 780) / screenHeight;  // Y inverted!

// Encode for transmission
byte hiY = (byte)(0x20 | ((tekY >> 5) & 0x1F));
byte loY = (byte)(0x60 | (tekY & 0x1F));
byte hiX = (byte)(0x20 | ((tekX >> 5) & 0x1F));
byte loX = (byte)(0x40 | (tekX & 0x1F));
```

**Arrow keys → incremental movement:**
```
Up arrow:    Y += 1 (or configurable step)
Down arrow:  Y -= 1
Right arrow: X += 1
Left arrow:  X -= 1
```

**Keypress in GIN mode → send to host:**
```csharp
// User pressed 'A' at crosshair position (512, 390)
byte[] response = { 0x41, hiY, loY, hiX, loX, 0x0D };
connection.Send(response);
```

## Escape Sequence Reference (from binary)

### Complete ESC " Mode Reference (30 modes, from 308-call Ghidra analysis)

**Action letters**: `h` = set mode, `l` = reset mode, `d` = query, `n` = define,
`r` = report, `a` = address, `C` = cursor, `.` = font params

| Mode | Format | Params | Meaning | Used By |
|------|--------|--------|---------|---------|
| 1 | `ESC "1;Vh` | device | **Select display device** | cmd_fonts |
| 2 | `ESC "2;Vh` | mode | **Set write mode** (replace/OR/XOR/complement) | cmd_fonts, cmd_coordinates |
| 3 | `ESC "3;V;Vh` | style,scale | **Set line style** (0=solid,1=dotted,2=dash-dot,3=short-dash,4=long-dash,5=user) | cmd_lines |
| 5 | `ESC "5;Vh` / `ESC "5d` | mode | **Set/query graphic mode** | detection, cmd_copy_window, cmd_polygons, cmd_circles, cmd_arcs |
| 6 | `ESC "6h` | none | **Enable crosshair cursor** | cmd_fonts |
| 7 | `ESC "7;Vh` | attr | **Set video attribute** (normal/reverse/blink) | cmd_graphic_video |
| 8 | `ESC "8;x1;y1;x2;y2h` | 4 coords | **Rectangle fill** (define + fill rectangle) | cmd_rectangle_fill, cmd_chess_board, cmd_clear_graphic, cmd_graphic_video |
| 9 | `ESC "9h` | none | **Clear graphic memory** (erase bitmap) | cmd_clear_graphic |
| 10 | `ESC "10;Vh` | vis | **Set cursor visibility** (0=off, 1=on) | cmd_flashing_crosshair, cmd_inking |
| 11 | `ESC "11;Vh` | font | **Select alpha font** (0=normal, 1=downloaded) | cmd_fonts |
| 12 | `ESC "12;Vh` | style | **Set cursor style** | cmd_flashing_crosshair, cmd_inking |
| 13 | `ESC "13;0h` | 0 | **Set coordinate system** (reset to default) | detection init |
| 15 | `ESC "15;Vh` | rate | **Set blinking rate** | (via toggle helper) |
| 16 | `ESC "16;Vh` | mode | **Set inking mode** (0=off, 1=draw while moving) | cmd_inking |
| 17 | `ESC "17h` | none | **Enable graphic display** (show graphics plane) | cmd_select_device |
| 18 | `ESC "18;p1;p2;p3;p4.` | 4 vals | **Font download parameters** (final=`.`) | cmd_fonts |
| 19 | `ESC "19;Vh` | mode | **Set polygon/shape drawing mode** | cmd_copy_window, cmd_polygons, cmd_circles, cmd_arcs |
| 20 | `ESC "20;Vh` | mode | **Set circle drawing mode** | cmd_circles |
| 21 | `ESC "21;Vh` | mode | **Set arc drawing mode** | cmd_arcs |
| 22 | `ESC "22;Vh` | mode | **Set window/copy mode** | cmd_copy_window |
| 23 | `ESC "23;Vh` | dir | **Set copy direction** | cmd_copy_window |
| 24 | `ESC "24;cx;cy;rh` | 3 vals | **Define circle** (center X, Y, radius) | cmd_circles |
| 25 | `ESC "25;cx;cy;r;sa;ea;dirh` | 6 vals | **Define arc** (center, radius, start/end angles, direction) | cmd_arcs |
| 26 | `ESC "26;x1;y1;x2;y2;oph` | 5 vals | **Define copy window** (source rect + operation) | cmd_copy_window |
| 27 | `ESC "27;mode;...h` | complex | **Define polygon** (multi-vertex, uses `.` and `#` sub-delimiters) | cmd_polygons |
| 28 | `ESC "28;Vh` | page | **Set memory page** (graphic memory bank) | cmd_graphic_memory |
| 29 | `ESC "29;V;Vh` | attr,rate | **Set flashing attributes** | (via helper) |
| 30 | `ESC "30h` | none | **Execute draw** (renders pending polygon/shape) | cmd_copy_window, cmd_polygons |
| — | `ESC "V;Vl` | varies | **Reset mode** (counterpart to `h`) | cmd_select_device, cmd_graphic_memory |
| — | `ESC "Vd` | mode | **Query mode** (terminal responds) | detection, cmd_select_device, cmd_set_parameters |
| — | `ESC "V;V;V;Vn` | 4 vals | **Define coordinates** | cmd_graphic_memory |
| — | `ESC "V;Va` | 2 vals | **Set memory address** | cmd_graphic_memory |

### Query/Action Commands (non-'h' final letters)

| Sequence | Final | Inferred Meaning |
|----------|-------|-----------------|
| `ESC "Vd` | `d` | Device query/position (used for detection: `ESC "5d`) |
| `ESC "V;Vl` | `l` | Reset mode (counterpart to `h`) |
| `ESC "V;V;V;Vn` | `n` | Device status report / query |
| `ESC "V;{0|1}r` | `r` | Report/restore |
| `ESC "V;Va` | `a` | Character position absolute |
| `ESC "V;V;VC` | `C` | Cursor/graphics position command |

### Font Protocol (`ESC /` = 0x1B 0x2F)

Three font sub-commands identified from binary analysis:

| Sub-command | Sequence | Purpose |
|-------------|----------|---------|
| Font Query | `ESC / <slot> d` | Query font slot (1-3), final='d' |
| Font Download | `ESC / <slot> <data...> {a\|b\|c}` | Download bitmap to slot, data terminated by a/b/c |
| Font Select | `ESC / f` | Activate downloaded font |

**Font Download Wire Protocol:**
```
HOST → TERMINAL:
  1B 2F                    ESC / — font download introducer
  3X                       Font slot number ('1', '2', or '3')
  XX XX XX XX ...          Font bitmap data (byte by byte, words byte-swapped)
  {61|62|63}               Final character 'a', 'b', or 'c' — terminates download
```

The font data is loaded from the file `GRAPHIC-TERM.FONT` on disk (referenced at
ram:967b as `(250:FLOPPY-USER)GRAPHIC-TERM`). The test program:
1. Opens the font file via SINTRAN MON calls
2. Reads the bitmap data into a memory buffer
3. Sends `ESC /` + slot number
4. Sends each word from the buffer, byte-swapped (SAD SHR 16)
5. Sends the terminating letter (a, b, or c)

**Font rendering test** (`cmd_fonts` at ram:96ea):
After downloading, the test displays:
```
" THIS IS NORMAL IN UPPERCASE"
"THIS IS ITALICS IN UPPERCASE"
" THIS IS NORMAL IN LOWERCASE"
```
Using ESC "11;0h (font 0 = normal) and ESC "11;1h (font 1 = downloaded/italic).

**Font file format**: Unknown. The file is read as raw bytes and sent to the terminal.
The terminal's firmware interprets the bitmap format. Likely a simple bitmap where
each character is defined as an N×M pixel grid.

### Other Escape Sequences

| Sequence | Hex | Meaning |
|----------|-----|---------|
| `ESC /` | 0x1B 0x2F | Font download/query/select (see above) |
| `ESC ENQ` | 0x1B 0x05 | Terminal identification request (standard Tek 4014) |
| `ESC SUB` | 0x1B 0x1A | Enter GIN mode — display crosshair (standard Tek 4014) |
| `ESC FF` | 0x1B 0x0C | Erase screen + home cursor (standard Tek 4014) |
| `ESC *` | 0x1B 0x2A | Set graphic memory address (`ESC * p1;p2;p3#`) |

## Complete Per-Command Protocol (all 20 commands)

### Bidirectional Commands (8 of 19 — terminal sends data BACK to host)

| Command | What Host Sends | What Terminal Responds | Read Via |
|---------|----------------|----------------------|----------|
| **Detection** | US + ESC "13;10l + ESC "5d + ESC ENQ | 7 bytes: status + HiY LoY HiX LoX + 2 ext | Direct (6942) |
| **FLASHING-CROSSHAIR** | ESC SUB (enter GIN) | Key char + HiY LoY HiX LoX + CR (on keypress) |
| **INKING** | ESC "16;1h + ESC SUB | Arrow key + HiY LoY HiX LoX + CR (continuous) |
| **GRAPHIC-MEMORY** | ESC "Va (set addr) + read | Memory content bytes (readback verification) |
| **SELECT-DEVICE** | ESC "Vd (query) | Device status response |
| **SET-PARAMETERS** | ESC "Vd (query) | Current parameter values |

### Unidirectional Commands (host sends only)

| Command | ESC Sequences Sent | Tek 4014 Controls |
|---------|-------------------|-------------------|
| **CLEAR-SCREEN** | ESC FF (erase+home) | US (alpha) |
| **CLEAR-GRAPHIC** | ESC "9h (clear gfx mem), ESC "8;x;y;x;yh (rect) | US, CAN |
| **RECTANGLE-FILL** | ESC "8;x1;y1;x2;y2h (define+fill rectangle) | US |
| **CHESS-BOARD** | ESC "8;...h repeated (grid of rectangles) | GS (graph mode), US |
| **LINES** | ESC "3;Vh (line style), ESC "1;Vh (char set) | FS (point plot), US, CAN |
| **COORDINATES** | ESC "2;Vh (write mode) | GS (graph mode), RS (incremental), US |
| **FONTS** | ESC "5h, ESC "1;Vh, ESC "6h, ESC "2;Vh, ESC "11;0h/1h, ESC / (font download) | GS, RS, US |
| **MOVE-CURSOR** | (no ESC " sequences) | BS, HT, LF, CR, US |
| **PLOT-POINTS-LINES** | (no ESC " sequences) | FS (point plot), RS (incremental), US |
| **GRAPHIC-VIDEO** | ESC "7;Vh (video attr), ESC "8;...h (rect) | US, CAN |
| **RUN-ALL-TESTS** | Calls all other commands in sequence | N/A |
| **COPY-WINDOW** | ESC "5h, ESC "19;Vh, ESC "22h, ESC "23h, ESC "26;...h, ESC "30h | GS, US, CAN |
| **POLYGONS** | ESC "5h, ESC "19;Vh, ESC "27;...h, ESC "30h | GS, US |
| **CIRCLES** | ESC "5h, ESC "19;Vh, ESC "24;cx;cy;rh | US |
| **ARCS** | ESC "5h, ESC "19;Vh, ESC "25;...h | US |
| **GRAPHIC-HARDCOPY** | Epson printer escape codes | N/A |

## Command → ESC Sequence Mapping

Based on the 20 test commands registered by the program. Each command's ESC sequences
determined from Ghidra disassembly (pointer table analysis + inline data extraction):

| TPE Command | Handler | ESC Sequences Used | Standard Tek 4014 |
|-------------|---------|-------------------|-------------------|
| CLEAR-SCREEN | ram:9024 | `ESC "6h` (erase mode) | + ESC FF (erase+home) |
| CLEAR-GRAPHIC | ram:90b3 | `ESC "9h` (graphic mode) | + ESC FF |
| RECTANGLE-FILL | ram:9178 | `ESC "8;X1;Y1;X2;Y2h` (define rectangle) | GS + coords |
| CHESS-BOARD | ram:9220 | `ESC "8;...h` repeated (rectangle fill grid) | GS + coords |
| LINES | ram:9307 | `ESC "3;Vh` (line style 0-5), `ESC "1;Vh` (char set) | ESC ` a b c d (line types) |
| COORDINATES | ram:950a | `ESC "2;Vh` (drawing mode) | GS + vector coords |
| FONTS | ram:96ea | `ESC /` (font download from GRAPHIC-TERM.FONT) | N/A (ND extension) |
| MOVE-CURSOR | ram:9a78 | Cursor movement chars (SP, BS, LF, CR) | Standard ASCII |
| PLOT-POINTS-LINES | ram:9c05 | Point Plot + Incremental Plot modes | FS (0x1C), RS (0x1E) |
| GRAPHIC-MEMORY | ram:9da8 | `ESC "Vn` (query), `ESC "Vd` (action) | N/A (ND extension) |
| GRAPHIC-VIDEO | ram:a28a | `ESC "11;0h` / `ESC "11;1h` (video plane on/off) | N/A (ND extension) |
| FLASHING-CROSSHAIR | ram:a370 | `ESC "5d` (enable crosshair) | ESC SUB (GIN mode) |
| INKING | ram:a471 | GIN mode + arrow key input + line drawing | ESC SUB + GS coords |
| RUN-ALL-TESTS | ram:a7c1 | Calls all other commands in sequence | N/A |
| SELECT-DEVICE | ram:a9f3 | Device I/O setup (no ESC to terminal) | N/A |
| SET-PARAMETERS | ram:ac78 | Test config (delay, loops, abort mode) | N/A |
| COPY-WINDOW | ram:add9 | `ESC "18;W;Hh` (window size), `ESC "24;...h` (copy) | N/A (ND extension) |
| POLYGONS | ram:b03b | `ESC "25;V1-V6h` (polygon), `ESC "26;V1-V5h` (fill) | N/A (ND extension) |
| CIRCLES | ram:b1dd | `ESC "25;Cx;Cy;R;...h` (circle def + fill) | N/A (ND extension) |
| ARCS | ram:b34e | `ESC "25;...h` (arc def + fill) | N/A (ND extension) |
| GRAPHIC-HARDCOPY | N/A | Epson printer control codes | N/A |

### Runtime Helper Functions (TP-CON library at 0x69xx)

All cmd_* functions call through these shared runtime I/O functions:

| Address | Name | Purpose | Called by |
|---------|------|---------|----------|
| 0x690E | `show_description` | Display test description, wait for CR | All cmd_* (first call) |
| 0x6926 | `terminal_io_setup` | Init terminal I/O with device handler + params | All cmd_* |
| 0x6904 | `check_error` | Check I/O completion, handle errors | All (after every I/O) |
| 0x6938 | `send_esc_prefix` | Send ESC " + mode number to terminal | ESC helper funcs |
| 0x6910 | `send_esc_params` | Send parameter bytes for ESC sequence | ESC helper funcs |
| 0x6930 | `read_with_timeout` | Read terminal response with timeout | Detection code |
| 0x6932 | `terminal_raw_send` | Low-level terminal write (no params) | Detection + rare |
| 0x6942 | `read_single_char` | Read one character from terminal | Detection code |
| 0x8DAD | `DEVICE_HANDLER` | Terminal device descriptor (data, not function) | All |

### ESC Sequence Helper Functions (in test program code c5xx-d3xx)

These intermediate functions build and send the ESC " sequences:

| Address | Name | ESC Modes Sent |
|---------|------|---------------|
| ram:c583 | `esc_device_setup` | Terminal init |
| ram:c592 | (unnamed) | Common setup helper |
| ram:c5d7 | (unnamed) | Line style setup |
| ram:c61d | `esc_device_reserve` | Reserve device |
| ram:c650 | `esc_init_terminal` | Init terminal mode |
| ram:c65f | `esc_mode_reset_init` | Reset modes at startup |
| ram:c69b | `detect_graphic_terminal` | ESC "5d + ESC ENQ detection |
| ram:c795 | `esc_send_test_pattern` | Send asterisks/backslashes test |
| ram:c8bb | `esc_mode_1_set` | ESC "1;Vh (character set) |
| ram:c95c | `esc_mode_3_line_style` | ESC "3;Vh (line style) |
| ram:cb33 | `esc_mode_toggle` | ESC "Vh (generic toggle) |
| ram:cc16 | `esc_mode_5_attribute` | ESC "5;{0\|1}h |
| ram:cc47 | `esc_mode_7_wrap` | ESC "7;{0\|1}h |
| ram:cf08 | `esc_mode_18_viewport` | ESC "18;V;Vh (window size) |
| ram:cf85 | `esc_mode_20` | ESC "20;Vh |
| ram:cfb7 | `esc_mode_21` | ESC "21;Vh |
| ram:cfe9 | `esc_mode_22` | ESC "22;Vh |
| ram:d01b | `esc_mode_23` | ESC "23;Vh |
| ram:d04a | (unnamed) | ESC "24;V;V;Vh (color) |
| ram:d13b | `esc_mode_25_extended_gfx` | ESC "25;V1-V6h (polygon/circle/arc) |
| ram:d1d0 | `esc_mode_27_complex` | ESC "27;Vh (complex sub-params) |
| ram:d236 | `esc_mode_28_29` | ESC "28;Vh, ESC "29;V;Vh |
| ram:d346 | `esc_mode_30_set` | ESC "30h |

### Low-Level Graphics Utilities

| Address | Name | Purpose |
|---------|------|---------|
| ram:b8ca | `esc_send_sequence` | Build and send complete ESC sequence |
| ram:b94f | `esc_output_char` | Output single character to terminal |
| ram:b9e2 | `esc_output_number` | Convert number to ASCII and send |
| ram:bc13 | `esc_send_coords` | Send Tek 4014 encoded coordinates |
| ram:bcda | `esc_read_response` | Read terminal response bytes |
| ram:bcf4 | `esc_parse_coords` | Parse Tek 4014 coordinate response |
| ram:bd35 | `esc_error_report` | Report test errors |
| ram:d661 | `gfx_draw_line` | Draw line via GS + coordinates |
| ram:d6fb | `gfx_draw_rect` | Draw rectangle via ESC "8;...h |
| ram:d801 | `gfx_fill_pattern` | Set fill pattern |
| ram:da65 | `gfx_send_string` | Send ASCII string to terminal |
| ram:db12 | `gfx_wait_cr` | Wait for CR from user |
| ram:db20 | `gfx_delay` | Timed delay between tests |
| ram:db32 | `gfx_print_number` | Print numeric value |
| ram:dbc8 | `gfx_print_error` | Print error message |

## Tektronix 4014 Compatibility Assessment

### Compatible (standard Tek 4014):
- **Coordinate encoding**: 5-bit Hi/Lo pairs for X,Y — identical to Tek 4014
- **Resolution**: ~1024x780 (standard Tek 4014 grid)
- **GIN mode response**: Status byte + 4 address bytes — same format
- **ESC ENQ**: Terminal identification/status request — standard
- **GS (0x1D)**: Enter Graph Mode — standard Tek 4014
- **US (0x1F)**: Unit Separator / alpha mode reset — standard
- **CR (0x0D)**: Carriage return / mode exit — standard
- **ESC SUB**: Enter GIN mode — standard Tek 4014
- **Line types**: Dotted (ESC a), dot-dashed (ESC b), short-dashed (ESC c),
  long-dashed (ESC d), normal (ESC `) — standard Tek 4014
- **Point Plot mode**: FS (0x1C) — standard Tek 4014 with EGM
- **Incremental Plot**: RS (0x1E) — standard Tek 4014 with EGM

### NOT compatible (Norsk Data proprietary extensions):
- **`ESC "` CSI**: Proprietary control sequence introducer (not ANSI `ESC [`)
- **Modes 1-30**: Extended mode set/reset commands via `ESC "Nh`
- **Rectangle fill**: No equivalent in Tek 4014
- **Polygon fill**: No equivalent in Tek 4014
- **Circle/arc drawing**: No equivalent in Tek 4014
- **Font download**: `ESC /` command — not in Tek 4014
- **Copy window**: Window-to-window copy — not in Tek 4014
- **Graphic memory access**: Read/write graphic plane — not in Tek 4014
- **7-byte GIN response**: Standard Tek 4014 sends 5 bytes (status + 4 addr);
  the ND terminal sends 7 bytes (5 standard + 2 extra ND extension bytes)

### Conclusion

The Norsk Data graphic terminal is a **Tektronix 4014-compatible terminal with
proprietary extensions**. It supports standard Tek 4014 vector drawing, coordinate
encoding, and GIN mode, but adds advanced 2D graphics primitives (rectangles,
polygons, circles, arcs), font management, graphic memory access, and window
operations through the proprietary `ESC "` escape sequence format.

The terminal was likely designed for CAD/engineering workstations in the mid-1980s,
providing backward compatibility with existing Tektronix 4014 software while adding
raster graphics capabilities not available on the original storage-tube Tek 4014.

---

## Resolution Data from Binary

| Address | Value | Meaning |
|---------|-------|---------|
| ram:c695 | 0x03E0 = 992 | Horizontal resolution (pixels) |
| ram:c696 | 0x0020 = 32 | Character cell width? |
| ram:c698 | 0x0040 = 64 | Vertical parameter? |
| ram:c721 | 0x0096 = 150 | Detection timeout (cycles) |
| ram:c727 | 0x007F | 7-bit ASCII mask for response parsing |
| ram:c729 | 0x0032 = 50 | Coordinate scaling factor? |

## Source Files

- Binary: `D:\ND\S\testprog\x\graphic-term-b00.test` (29,184 words)
- Font: `GRAPHIC-TERM.FONT` (on nd100x disk)
- Ghidra project: loaded via TPETestLoader at base VA 0x6B88
- Reference: `4014-um.pdf` (in this folder)
