# RetroTerm Feature Status

Last updated: 2026-02-15

## Version Scope

- **v1.0 Focus**: TDV emulators + VT-series emulators + Desktop UI polish
- **v2.0 Deferred**: IBM 3270, Graphics (ReGIS/Sixel/Tektronix), Blazor Web UI

---

## Component Status Overview

| Component | Status | Tests | Notes |
|-----------|--------|-------|-------|
| Core Emulation | Implemented | ~200+ | TerminalEmulatorBase, Buffer, Parser |
| TDV Emulators | Implemented | 1400+ | TDV1200, TDV2215, TDV2200, comprehensive validation |
| TDV Keyboard | Implemented | 200+ | 268 key mappings, ND246 layout, unified mapper |
| Virtual Keyboard | Implemented | Manual | 120+ keys, 12 national layouts, key binding UI |
| Key Binding System | Implemented | Yes | TDVKeyBindingConfiguration, JSON persistence |
| TelnetServer Library | Implemented | Yes | Extracted from TestServer, input pump model |
| VT Emulators | Partial | Limited | VT100 only, needs VT220/xterm |
| Protocols | Partial | Yes | Telnet, SSH implemented |
| Desktop UI | Implemented | Manual | Avalonia, needs settings window + tab management |
| IBM 3270 | Not Started | - | Deferred to v2.0 |
| Graphics | Not Started | - | Deferred to v2.0 |
| Web UI | Not Started | - | Deferred to v2.0 |

---

## TDV Emulator Details

### TDV2200 Emulator

| Feature | Status | Tests |
|---------|--------|-------|
| Primary DA Response | Working | Pass |
| Secondary DA Response | Working | Pass |
| CPR (Cursor Position Report) | Working | Pass |
| DSR (Device Status Report) | Working | Pass |
| Terminal ID Query | Working | Pass |
| 2115 Compatibility Mode | Working | Pass |
| SGR Attributes (Bold/Underline/Blink/Reverse) | Working | Pass |
| Cursor Movement | Working | Pass |
| Screen Clear | Working | Pass |
| Protected Areas (SPA/EPA) | Working | Pass |
| Work Areas (NDDWA) | Working | Pass |
| Message LEDs | Working | Pass |
| Push Keys (8 programmable) | Working | Pass |
| Character Set Switching (ESC 0-9) | Working | Pass |
| Rectangle Save/Restore | Working | Pass |
| ISO 646 Variants (12 national) | Working | Pass |
| Graphics Extension Mode (ESC[1>/ESC[0>) | Working | Pass |
| Tektronix Mode (ESC[2>/ESC[3>) | Working | Pass |

### TDV Keyboard System

| Feature | Status | Notes |
|---------|--------|-------|
| Unified TDV2200KeyboardMapper | Working | Single mapper, type aliases for 1200/2215 |
| TDV2200KeyRegistry | Working | 268 entries, single source of truth |
| Function Keys F1-F20 | Working | All modifiers supported |
| Arrow Keys (C0 mode) | Working | UP=0x1C, DOWN=0x0B, LEFT=0x08, RIGHT=0x18 |
| Arrow Keys (ESC mode) | Working | CSI A/B/C/D |
| Modified Arrow Keys | Working | xterm-style ESC[1;2A etc. |
| PUSH Keys 1-8 | Working | Programmable via DCS |
| TDV Special Keys | Working | HELP, DO, FUNC, COPY, MOVE, etc. |
| Control Keys | Working | RETURN, TAB (TDV-native), BACKSPACE, ESCAPE |
| ND246 National Layouts | Working | 12 variants |
| Key Binding System | Working | Any key combo → TDV grid position |
| Key Capture Mode | Working | Binding popup in virtual keyboard |

### Virtual Keyboard

| Feature | Status | Notes |
|---------|--------|-------|
| VirtualKeyboardPanel | Working | 120+ keys rendered |
| Key Symbol Rendering | Working | SVG paths + font-rendered labels |
| 12 National Layouts | Working | NO, SE, DK, DE, FR, etc. |
| Mouse Click → Key Send | Working | Sends correct TDV sequences |
| Key Binding Popup | Working | Key capture mode |
| JSON Persistence | Working | %AppData%\RetroTerm\tdv-key-bindings.json |

### TelnetServer Library

| Feature | Status | Notes |
|---------|--------|-------|
| TelnetServer | Working | TCP listener, extracted from TestServer |
| TelnetSession | Working | Input pump model (StartInputPump/ReadInputAsync) |
| TelnetCodec | Working | IAC handling |
| TelnetNegotiator | Working | Option negotiation |
| ITelnetApp | Working | App interface for TestServer |
| InputParser | Working | ParsedInput, InputType |
| TDVCapabilityChecker | Working | DA/DSR query validation |
| TDVResponseValidator | Working | Response verification |

### Known Issues

1. **Scroll Region Bug** - Cursor positioning outside scroll region causes unexpected behavior (4 tests skipped)
2. **~10-15 Comprehensive Test Failures** - Character set designation, rectangle ops, double height/width edge cases
3. **Line Drawing Cursor Position** - Manual test 5 fails (cursor positioning with line drawing chars)

---

## VT Emulator Details

| Emulator | Status | Notes |
|----------|--------|-------|
| VT52 | Not Started | |
| VT100 | Partial | Basic implementation, needs DEC private modes |
| VT220 | Not Started | Needs proper implementation |
| xterm | Not Started | |
| ANSI | Not Started | |

---

## Protocol Details

| Protocol | Status | Notes |
|----------|--------|-------|
| Telnet | Implemented | RFC 854 compliant, NAWS, TERMINAL-TYPE |
| SSH | Implemented | Password auth, host key verification (TOFU) |
| TN3270 | Not Started | Deferred to v2.0 |
| Serial | Not Started | |
| WebSocket | Not Started | Deferred to v2.0 |
| XOT | Not Started | |
| SINTRAN | Not Started | |

---

## Desktop UI Details

| Feature | Status | Notes |
|---------|--------|-------|
| Main Window | Working | Menu, status bar, terminal canvas |
| Connection Dialog | Working | Telnet + SSH, host/port config |
| Terminal Renderer | Working | Canvas-based, font strategy pattern |
| Virtual Keyboard | Working | 120+ keys, 12 national layouts |
| Keyboard Reference | Working | Key mapping display |
| Log Viewer | Working | Terminal sequence logging |
| Push Keys Dialog | Working | PUSH key configuration |
| SSH Host Key Dialog | Working | TOFU verification |
| Dark Theme | Working | Applied across UI |
| Text Selection | Working | Character, word, line, rectangular |
| Clipboard | Working | Copy/paste with sanitization |
| Search | Working | Scrollback search with regex, F3 navigation |
| Settings Window | Not Started | Needs tabbed settings UI |
| Tab Management | Not Started | Single terminal only |
| Toolbar | Not Started | |
| Saved Profiles | Not Started | Connection profile persistence |
| Glyph Cache | Not Started | Performance optimization |
| Dirty Region Tracking | Not Started | Performance optimization |

---

## Test Summary

### Overall
- **Total Tests**: 2069 passing, 41 skipped
- **Test Framework**: xUnit
- **No FluentAssertions** (xUnit Assert.* only)

### Skipped Tests by Reason

| Reason | Count |
|--------|-------|
| Scroll region emulator bug | 4 |
| Requires Avalonia context | ~6 |
| TCP timing issues | ~8 |
| Other/manual | ~23 |

---

## Files Reference

### TDV Emulator Implementation
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDVEmulatorBase.cs`
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200Emulator.cs`
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2215Emulator.cs`
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV1200Emulator.cs`
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200KeyboardMapper.cs`
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200KeyRegistry.cs`

### Key Binding System
- `src/RetroTerm.Core/Terminal/Emulators/TDV/TDVKeyBindingConfiguration.cs`
- `src/RetroTerm.Desktop/Controls/VirtualKeyboardPanel.axaml.cs`
- `src/RetroTerm.Desktop/Helpers/AvaloniaKeyHelper.cs`

### TelnetServer Library
- `src/RetroTerm.Core.Protocols.TelnetServer/Telnet/TelnetServer.cs`
- `src/RetroTerm.Core.Protocols.TelnetServer/Telnet/TelnetSession.cs`
- `src/RetroTerm.Core.Protocols.TelnetServer/Parsing/InputParser.cs`

### Core Implementation
- `src/RetroTerm.Core/Terminal/Emulators/TerminalEmulatorBase.cs`
- `src/RetroTerm.Core/Terminal/Buffer/TerminalBuffer.cs`
- `src/RetroTerm.Core/Terminal/Parsing/EscapeSequenceParser.cs`
- `src/RetroTerm.Core/Session/TerminalSession.cs`
