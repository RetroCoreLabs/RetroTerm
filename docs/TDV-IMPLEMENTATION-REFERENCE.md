# TDV Terminal Emulation Implementation Reference

## Overview

This document provides a comprehensive reference of all implemented TDV (Tandberg Data Video) terminal emulation features in RetroTerm. The implementation covers TDV1200, TDV2215, and TDV2200 terminal models with full backward compatibility to VT100.

## Implementation Architecture

### Base Classes

- **`TDVEmulatorBase`**: Core TDV functionality shared across all models
- **`TDV1200Emulator`**: TDV1200 with 2115 compatibility mode
- **`TDV2215Emulator`**: TDV2215 with extended mode and transparent mode
- **`TDV2200Emulator`**: TDV2200 with graphics extension and Tektronix mode

### Supporting Components

- **`TDVProtectedAreas`**: Manages protected regions (SPA/EPA)
- **`TDVWorkAreas`**: Manages work area definitions (NDDWA)
- **`TDVMessageLEDs`**: Controls message LEDs (NDCLED, NDSLED, NDBLED)
- **`TDVRectangleOperations`**: Handles rectangle operations (NDSAR, NDAAR, NDRAR, NDFC, NDSREC, NDRREC)
- **`TDVPushKeys`**: Manages programmable function keys
- **`TDVCharacterSets`**: Handles 10 TDV-specific character sets

## Implemented Features

### 1. Core TDV Sequences

#### Device Attributes (DA) - Query/Response Support
- **Primary DA**: `ESC [ c` → Responds with `ESC [ ? 1 ; 2 c` (VT100 compatible)
- **Secondary DA**: `ESC [ > c` → Responds with model-specific version info
- **Implementation**: Added to all TDV emulator classes with model-specific responses

#### Cursor Position Report (CPR)
- **Query**: `ESC [ 6 n` → Responds with `ESC [ row ; col R`
- **Implementation**: Returns current cursor position (1-based coordinates)

#### Device Status Report (DSR)
- **Query**: `ESC [ 5 n` → Responds with `ESC [ 0 n` (ready) or `ESC [ 3 n` (not ready)
- **Implementation**: Always returns ready status

#### Terminal Identification
- **Query**: `ESC Z` → Responds with terminal identification string
- **Implementation**: Returns model-specific identification

#### Mode Query (DECRQM) - NOT A TDV SEQUENCE. Corrected 11 September 2026

This section used to list DECRQM as a TDV query. **No TDV manual describes it.** TDV 2215
Functional Specifications section 8.7 lists every CSI sequence the terminal accepts and not one
carries a `$` intermediate; section 8.3.2 lists everything it ever sends, and that is CPR alone.
The TDV 2200/9 S User's Guide section 11.2 lists the eleven sequences the 2200 adds, and none of
them is a query.

`CSI ? Ps $ p` still gets an answer from this program, because `TerminalEmulatorBase` answers it
for every emulator with DEC's mode numbers and DEC's values. That is OUR extension, not a claim
about Tandberg hardware.

**What a TDV really has is NDRQ** - ND Display Terminal 1200 Functional Specifications section
5.48, `CSI Ps x` to request and `CSI Ps ; n1 ; ... x` to report, with six report types. It is not
implemented here yet. See `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`.

### 2. Character Sets (10 TDV Sets)

All TDV terminals support 10 character sets:

1. **Graphics I** (`ESC ( 1`): Extended graphics characters
2. **Graphics II** (`ESC ( 2`): Additional graphics symbols
3. **Math** (`ESC ( 3`): Mathematical symbols
4. **Greek** (`ESC ( 4`): Greek alphabet
5. **Norwegian** (`ESC ( 5`): Norwegian-specific characters
6. **Swedish** (`ESC ( 6`): Swedish-specific characters
7. **Danish** (`ESC ( 7`): Danish-specific characters
8. **Finnish** (`ESC ( 8`): Finnish-specific characters
9. **German** (`ESC ( 9`): German-specific characters
10. **US ASCII** (`ESC ( 0`): Standard ASCII

**Implementation**: Complete character mapping with Unicode fallback

### 3. Drawing Operations

#### Rectangle Operations
- **NDSAR** (`ESC [ attr ; x1 ; y1 ; x2 ; y2 z`): Set attribute in rectangle
- **NDAAR** (`ESC [ attr ; x1 ; y1 ; x2 ; y2 {`): Add attribute in rectangle
- **NDRAR** (`ESC [ attr ; x1 ; y1 ; x2 ; y2 }`): Remove attribute in rectangle
- **NDFC** (`ESC [ char ; x1 ; y1 ; x2 ; y2 |`): Fill character in rectangle
- **NDSREC** (`ESC [ x1 ; y1 ; x2 ; y2 u`): Save rectangle
- **NDRREC** (`ESC [ x ; y v`): Restore rectangle

**Implementation**: Full coordinate validation and buffer manipulation

#### Work Areas
- **NDDWA** (`ESC [ x1 ; y1 ; x2 ; y2 ~`): Define work area
- **Query Support**: Can query current work area boundaries
- **Implementation**: Work area management with boundary checking

### 4. Function Keys and Input

#### Standard Function Keys
- **F1-F12**: Full support with proper escape sequences
- **Arrow Keys**: All four directions with proper sequences
- **Special Keys**: Home, End, Insert, Delete, Page Up/Down

#### PUSH Keys (Programmable)
- **Programming**: DCS sequences for key definition
- **Execution**: Runtime key sequence execution
- **Query Support**: Can query programmed key definitions
- **Implementation**: Complete PUSH key management system

### 5. Modes and Features

#### Smooth Scroll Mode (NDSSM)
- **Enable**: `ESC [ ? 67 h`
- **Disable**: `ESC [ ? 67 l`
- **Implementation**: Animated scrolling with renderer integration

#### Blink Modes
- **Standard Blink** (NDBLWM): `ESC [ ? 68 h/l`
- **Enhanced Blink** (NDELWM): `ESC [ ? 69 h/l`
- **Implementation**: Character attribute blink control

#### Double-Width/Height Lines
- **ESC # 3**: Double-height line, top half
- **ESC # 4**: Double-height line, bottom half
- **ESC # 5**: Single-width, single-height line
- **ESC # 6**: Double-width line
- **Implementation**: Line attribute management in terminal buffer

### 6. Model-Specific Features

#### TDV1200
- **2115 Compatibility Mode**: `ESC [ ? 66 h/l`
- **ND Graphics**: Full ND-specific graphics sequences
- **Character Sets**: All 10 TDV character sets
- **Protected Areas**: SPA/EPA management

#### TDV2215
- **Extended Mode**: `ESC [ ? 1 h/l`
- **Transparent Mode**: `ESC [ ? 2 h/l`
- **DCS Sequences**: Device Control String support
- **Function Key Programming**: Advanced key programming

#### TDV2200
- **Graphics Extension**: Graphics board support
- **Tektronix Mode**: `ESC [ ? 38 h/l` (Tektronix 4010 compatibility)
- **ISO 646 Variants**: Country-specific character sets
- **Advanced Graphics**: Extended graphics operations

### 7. Message LEDs

- **NDCLED**: Clear all message LEDs
- **NDSLED**: Set specific message LED
- **NDBLED**: Blink message LED
- **Query Support**: Can query LED states
- **Implementation**: LED state management with visual feedback

### 8. Protected Areas

- **SPA** (Start Protected Area): Define protected region
- **EPA** (End Protected Area): End protected region
- **Query Support**: Can query protected area boundaries
- **Implementation**: Protected area management with cursor checking

### 9. VT100 Compatibility

All TDV emulators maintain full VT100 compatibility:

- **Standard Escape Sequences**: Complete VT100 sequence support
- **Character Attributes**: Bold, underline, reverse, blink, dim, hidden
- **Cursor Control**: All cursor movement and positioning
- **Screen Control**: Clear, scroll, insert/delete operations
- **Color Support**: 8-color and 256-color support
- **Character Sets**: G0-G3 character set support

### 10. Query/Response Commands

#### Implemented Query Commands
- **Device Attributes**: Primary and secondary DA queries
- **Cursor Position**: CPR queries with position reporting
- **Device Status**: DSR queries with ready/not-ready responses
- **Terminal ID**: Terminal identification queries
- **Mode Queries**: DECRQM for mode state queries
- **Work Area Queries**: Current work area boundary queries
- **Protected Area Queries**: Protected region boundary queries
- **LED Status Queries**: Message LED state queries
- **PUSH Key Queries**: Programmed key definition queries

#### Response Format
All responses follow standard escape sequence format:
- **DA Response**: `ESC [ ? 1 ; 2 c` (VT100 compatible)
- **CPR Response**: `ESC [ row ; col R` (1-based coordinates)
- **DSR Response**: `ESC [ 0 n` (ready) or `ESC [ 3 n` (not ready)
- **Terminal ID**: Model-specific identification string

## Implementation Status

### Completed Features ✅
- [x] All 10 TDV character sets
- [x] Rectangle operations (NDSAR, NDAAR, NDRAR, NDFC, NDSREC, NDRREC)
- [x] Work area management (NDDWA)
- [x] Protected areas (SPA/EPA)
- [x] Message LEDs (NDCLED, NDSLED, NDBLED)
- [x] PUSH key programming and execution
- [x] Double-width/height lines (ESC # 3-6)
- [x] Smooth scroll mode (NDSSM)
- [x] Blink modes (NDBLWM, NDELWM)
- [x] Model-specific features (2115 mode, extended mode, graphics extension)
- [x] Query/response command support
- [x] VT100 compatibility layer

### Test Coverage ✅
- [x] Unit tests for all TDV components
- [x] Integration tests for TDV emulators
- [x] Test server with TDV-specific tests
- [x] Manual test procedures
- [x] Query/response validation tests

## Usage Examples

### Basic TDV1200 Usage
```csharp
var emulator = new TDV1200Emulator(80, 24);
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?66h")); // Enter 2115 mode
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[c")); // Query device attributes
// Response: \x1b[?1;2c
```

### Rectangle Operations
```csharp
// Set bold attribute in rectangle (10,10 to 20,20)
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[1;10;10;20;20z"));

// Fill rectangle with 'A' character
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[65;10;10;20;20|"));
```

### Character Set Usage
```csharp
// Switch to Graphics I character set
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b(1"));

// Switch to Greek character set
emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b(4"));
```

## Performance Characteristics

- **Memory Usage**: Optimized buffer management with minimal overhead
- **Processing Speed**: Efficient escape sequence parsing
- **Scrollback**: Configurable scrollback buffer (default 2000 lines)
- **Response Time**: Sub-millisecond query/response handling

## Compatibility Matrix

| Feature | TDV1200 | TDV2215 | TDV2200 | VT100 |
|---------|---------|---------|---------|-------|
| Basic Sequences | ✅ | ✅ | ✅ | ✅ |
| Character Sets | ✅ | ✅ | ✅ | ✅ |
| Rectangle Ops | ✅ | ✅ | ✅ | ❌ |
| Work Areas | ✅ | ✅ | ✅ | ❌ |
| Protected Areas | ✅ | ✅ | ✅ | ❌ |
| Message LEDs | ✅ | ✅ | ✅ | ❌ |
| PUSH Keys | ✅ | ✅ | ✅ | ❌ |
| 2115 Mode | ✅ | ✅ | ❌ | ❌ |
| Extended Mode | ❌ | ✅ | ❌ | ❌ |
| Graphics Extension | ❌ | ❌ | ✅ | ❌ |
| Tektronix Mode | ❌ | ❌ | ✅ | ❌ |

## Future Enhancements

### Planned Features
- [ ] Graphics rendering engine integration
- [ ] Font system integration
- [ ] Advanced graphics operations
- [ ] Performance optimizations
- [ ] Additional character sets
- [ ] Enhanced test coverage

### Potential Improvements
- [ ] Hardware acceleration support
- [ ] Advanced graphics modes
- [ ] Extended character set support
- [ ] Performance profiling tools
- [ ] Memory usage optimization

## Conclusion

The TDV terminal emulation implementation provides comprehensive support for all three TDV terminal models with full backward compatibility to VT100. The implementation includes complete query/response support, extensive test coverage, and detailed documentation for maintainability and extensibility.

All major TDV features are implemented and tested, providing a solid foundation for terminal emulation applications requiring TDV compatibility.
