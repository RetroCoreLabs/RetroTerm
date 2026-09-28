# RetroTerm — ND Graphic Terminal Implementation Plan

## Phase 1: Host Request/Response (Detection)

### Goal
Make the graphic-term TPE test program detect RetroTerm as a valid graphic terminal,
so it proceeds past "WARNING: Selected device IS NOT found to be a graphic terminal."

### What the Host Sends (sequence from binary analysis)

```
Byte  Hex    Meaning
────  ────   ──────────────────────────────────
0x1F         US — terminal reset to alpha mode
0x1B 0x22    ESC " — ND graphics CSI
  31 33 3B   "13;"
  31 30 6C   "10l" — reset modes 13 and 10 (final='l')
0x1B 0x22    ESC "
  35 64      "5d" — device query mode 5 (final='d')
0x1B 0x05    ESC ENQ — terminal identification request
```

### What the Terminal Must Respond (7+ bytes, Tek 4014 GIN encoding)

```c
// Byte 0: Status byte
//   Bit 7: 0 (always)
//   Bit 6: 1 (always)
//   Bit 5: 1 = no hardcopy unit
//   Bit 4: 0 = no vector (alpha mode)
//   Bit 3: 1 = not in graph mode
//   Bit 2: 0 = margin 1
//   Bit 1: 0 = no auxiliary unit
// = 0x68 (0110_1000) for alpha mode, no peripherals

byte status = 0x68;

// Bytes 1-4: Cursor position in Tek 4014 5-bit encoding
// Home position: X=0, Y=767 (top-left)
int x = 0;
int y = 767;

byte hiY = (byte)(0x20 | ((y >> 5) & 0x1F));  // Hi Y: tag=001, data=Y[9:5]
byte loY = (byte)(0x60 | (y & 0x1F));          // Lo Y: tag=011, data=Y[4:0]
byte hiX = (byte)(0x20 | ((x >> 5) & 0x1F));  // Hi X: tag=001, data=X[9:5]
byte loX = (byte)(0x40 | (x & 0x1F));          // Lo X: tag=010, data=X[4:0]

// Bytes 5-6: ND extension bytes (7-bit, AND 0x7F)
// The detection code just checks count >= 7, doesn't validate these
byte ext1 = 0x40;  // Placeholder (bit 6 set, valid 7-bit)
byte ext2 = 0x40;  // Placeholder

// Complete response: 7 bytes
byte[] response = { status, hiY, loY, hiX, loX, ext1, ext2 };
// = { 0x68, 0x37, 0x7F, 0x20, 0x40, 0x40, 0x40 }
```

### Parser Changes Required

**File**: `src/RetroTerm.Core/Terminal/Parsing/EscapeSequenceParser.cs`

**Problem**: `ESC "` (0x1B 0x22) is mis-parsed. The parser treats `"` (0x22) as an
intermediate byte (range 0x20-0x2F), then the FIRST digit becomes the final byte.
So `ESC "5d` becomes `OnEscapeDispatch(intermediate='"', final='5')` — wrong.

**Solution**: Add a new parser state for ND graphics sequences.

```
ParserState.cs — add:
    NdGraphicsParam    // Accumulating ESC " params (digits, semicolons)

EscapeSequenceParser.cs — in Escape state handler:
    case 0x22:  // " character
        if (TektronixEnabled) {
            _state = ParserState.NdGraphicsParam;
            _ndParamBuffer.Clear();
        } else {
            // Standard behavior: treat as intermediate
            ...
        }

NdGraphicsParam state handler:
    0x30-0x39 (digits):  accumulate into param buffer
    0x3B (semicolon):    accumulate (parameter separator)
    0x2E (period):       accumulate (used as final for mode 18)
    0x23 (#):            accumulate (used in ESC * sequences)
    0x40-0x7E (letters): final byte → dispatch OnNdGraphicsDispatch()
    0x00-0x1F (controls): execute control, stay in state (like CSI)
```

**New event**:
```csharp
void OnNdGraphicsDispatch(ReadOnlySpan<byte> parameters, byte finalByte);
// parameters = "5" for ESC "5d, "13;10" for ESC "13;10l, etc.
// finalByte = 'd', 'l', 'h', 'n', 'r', 'a', 'C', '.'
```

### Emulator Changes Required

**File**: `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200Emulator.cs`

**1. Handle ENQ (0x05) in OnExecute:**
```csharp
case 0x05: // ENQ
    if (_isTektronixMode) {
        SendGinResponse();  // Send 7-byte Tek 4014 GIN identification
    }
    break;
```

**2. Handle US (0x1F) in OnExecute:**
```csharp
case 0x1F: // US — Unit Separator
    if (_isTektronixMode) {
        ExitGraphMode();    // Return to alpha mode
    }
    break;
```

**3. Handle ESC "5d (device query):**
```csharp
void HandleNdGraphicsDispatch(ReadOnlySpan<byte> parameters, byte finalByte) {
    if (finalByte == 'd') {
        // Query — mode 5 = device attributes
        // Response same as ENQ
        SendGinResponse();
    }
    else if (finalByte == 'h') {
        // Set mode — parse mode number from parameters
        HandleNdSetMode(parameters);
    }
    else if (finalByte == 'l') {
        // Reset mode
        HandleNdResetMode(parameters);
    }
    // ... other final bytes
}
```

**4. Send GIN response:**
```csharp
void SendGinResponse() {
    Span<byte> response = stackalloc byte[7];
    response[0] = 0x68;  // Status: alpha mode, no peripherals
    response[1] = (byte)(0x20 | ((_cursorY >> 5) & 0x1F));  // Hi Y
    response[2] = (byte)(0x60 | (_cursorY & 0x1F));          // Lo Y
    response[3] = (byte)(0x20 | ((_cursorX >> 5) & 0x1F));  // Hi X
    response[4] = (byte)(0x40 | (_cursorX & 0x1F));          // Lo X
    response[5] = 0x40;  // ND extension 1
    response[6] = 0x40;  // ND extension 2
    _connection.Send(response);
}
```

### Testing

1. Start nd100x emulator with TPE-MON
2. Connect RetroTerm to nd100x via telnet
3. In TPE-MON: `load graphic`
4. Expected: "GRAPHIC-TERM - Version: B00 - 1986-10-17" WITHOUT the warning
5. Then: `help` should show all 20 graphic commands

### Minimal Change List

| File | Change | Lines |
|------|--------|-------|
| `ParserState.cs` | Add `NdGraphicsParam` state | +1 |
| `EscapeSequenceParser.cs` | Add NdGraphicsParam state handler | +30 |
| `IParserEventHandler.cs` | Add `OnNdGraphicsDispatch` event | +1 |
| `TDV2200Emulator.cs` | Handle ENQ, US, ESC "Xd responses | +40 |
| `TDV2200Emulator.cs` | Add `SendGinResponse()` method | +15 |

**Total**: ~87 lines of code for Phase 1.

---

## Display Architecture

### Two-Plane Model (from binary analysis)

The ND graphic terminal has two independent display planes composited for output:

```
┌──────────────────────────────┐
│  Alpha Plane (text terminal) │  80×24 or 96×32 character cells
│  - Standard TDV 2200 text    │  - US (0x1F) = enter alpha mode
│  - Alpha cursor visible      │  - CR, LF, BS, etc.
├──────────────────────────────┤
│  Graphics Plane (bitmap)     │  1024 × 780 pixel bitmap
│  - Tek 4014 vector coords    │  - GS (0x1D) = enter graph mode
│  - ESC "9h = clear           │  - ESC "11;{0|1}h = show/hide
│  - ESC "8;...h = rectangles  │  - ESC "30h = execute draw
├──────────────────────────────┤
│  Crosshair Overlay           │  Full-screen crosshair cursor
│  - ESC SUB = show            │  - Positioned by thumbwheels/mouse
│  - ESC "6h = enable          │  - ESC "10;{0|1}h = visibility
└──────────────────────────────┘
         ↓ composite
┌──────────────────────────────┐
│  Display Output              │  All planes overlaid
└──────────────────────────────┘
```

### Graphics Bitmap Size

From binary data and Tek 4014 spec:
- **1024 × 780** addressable pixel grid (standard Tek 4014)
- Coordinates: X = 0-1023 (left to right), Y = 0-779 (bottom to top)
- Note: Y origin is BOTTOM-LEFT (not top-left like text terminals)
- Binary hint: 0x03E0 = 992 pixels (usable area, slightly less than 1024)

### Clear Operations

| Command | Alpha Plane | Graphics Plane |
|---------|------------|----------------|
| `cmd_clear_screen` (ESC FF) | Cleared + home | Cleared |
| `cmd_clear_graphic` (ESC "9h) | Unchanged | Cleared |
| PAGE key | Cleared + home | Unchanged |
| ESC "11;0h | Unchanged | Hidden (not cleared) |
| ESC "11;1h | Unchanged | Shown |

## GIN Mode — Crosshair Input

### Entry
- **ESC SUB** (0x1B 0x1A) — display crosshair, enter GIN mode
- **ESC "6h** — enable crosshair (ND extension)

### Keyboard Input → Terminal Sends to Host
When user presses ANY key while crosshair is displayed:
```
Byte 0: key character pressed
Byte 1: 0x20 | (Y >> 5)     Hi Y (5 high bits + tag)
Byte 2: 0x60 | (Y & 0x1F)   Lo Y (5 low bits + tag)
Byte 3: 0x20 | (X >> 5)     Hi X (5 high bits + tag)
Byte 4: 0x40 | (X & 0x1F)   Lo X (5 low bits + tag)
Byte 5: 0x0D               CR (if strap option)
```

### Host Request → Terminal Response
When host sends ESC ENQ while crosshair displayed:
```
Byte 0: status byte (bit6=1, bit7=0)
Byte 1-4: crosshair position (same Tek encoding as above)
Byte 5: CR (if strap option)
Byte 6: EOT (if strap option)
```

ND extension: 7 bytes total (5 standard + 2 extra 7-bit bytes)

### Crosshair Movement
- **Thumbwheels** (real hardware) → mouse in emulator
- **Arrow keys** → incremental movement (for inking mode)
- **ESC "16;1h** = enable inking (draw while moving crosshair)

### RetroTerm Implementation

**Mouse-driven (primary):**
- Mouse movement over graphics area → update crosshair X,Y
- Mouse click → encode position, send `click_char + HiY + LoY + HiX + LoX + CR`
- Right-click or ESC → exit GIN mode

**Keyboard-driven (authentic):**
- Arrow keys → move crosshair ±1 pixel (or configurable step)
- Any printable key → send `key + position` to host
- Matches original terminal behavior

**Both coexist** — mouse positions, keyboard triggers send.

## Phase 2: Basic Graphics Rendering (future)

After detection works, the next step would be:
1. `ESC "9h` — clear graphic memory (needs a bitmap/canvas)
2. `ESC "8;x1;y1;x2;y2h` — rectangle fill
3. GS + coordinates — vector drawing
4. `ESC "30h` — execute draw

This requires the graphics engine (`IGraphicsEngine` + SkiaSharp) planned for v2.0.

## Phase 3: Full Protocol (future)

- All 30 ESC " modes
- Font download (ESC /)
- GIN mode with crosshair cursor
- Polygon/circle/arc primitives
- Graphic memory read/write
- Window copy operations

---

## Reference Files

- ND escape analysis: `nd-graphic-terminal-analysis.md` (in this folder)
- Tektronix 4014 manual: `4014-um.pdf` (in this folder)
- RetroTerm source: this repository, `src\`
- Graphic test binary: `D:\ND\S\testprog\x\graphic-term-b00.test`
- TPE format spec: `docs\test-file-format.md` in the NDGen repository
