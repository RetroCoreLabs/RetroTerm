# RetroTerm - Comprehensive TODO Plan

This document provides a detailed, phase-by-phase implementation roadmap for RetroTerm with full scope.

## Timeline Overview

| Phase | Duration | Deliverable |
|-------|----------|-------------|
| **Phase 1**: Core Foundation | Weeks 1-2 | Terminal buffer, parser, base classes |
| **Phase 2**: Character-Mode Terminals | Weeks 3-6 | VT, TDV, ANSI emulators |
| **Phase 3**: Block-Mode & Graphics | Weeks 7-9 | IBM 3270, ReGIS, Sixel, Tektronix |
| **Phase 4**: Desktop UI (Avalonia) | Weeks 10-12 | Full desktop application |
| **Phase 5**: Protocol Layer | Weeks 13-14 | Telnet, SSH, TN3270, Serial |
| **Phase 6**: Advanced Features | Weeks 15-17 | SFTP/SCP, Zmodem, Search, Selection |
| **Phase 7**: Blazor Web UI | Weeks 18-20 | Web frontend |
| **Phase 8**: WebSocket Proxy | Week 21 | Azure-ready proxy |
| **Phase 9**: SINTRAN/XOT (v1.5) | Weeks 22-27 | SINTRAN protocol suite |
| **Phase 10**: Testing & Polish | Weeks 28-30 | Comprehensive testing |

**Total Estimated Time**: 30 weeks (~7.5 months)

---

## ⚠️ MANDATORY PHASE COMPLETION CHECKLIST

**CRITICAL**: Before marking any phase as COMPLETE, run this checklist:

### Rule 1: NO Business Logic in UI Layer
```
☐ Review all UI files (*.xaml.cs, *View.cs, *Control.cs, *Window.cs)
☐ Verify NO connection creation/management in UI
☐ Verify NO data processing in UI (all in Core/Service layers)
☐ Verify NO protocol logic in UI
☐ Verify NO state management in UI (beyond pure UI state)
☐ All business logic is in: Core classes, Services, Managers, Emulators
```

### Rule 2: NO Code Duplication
```
☐ Search for duplicated code patterns
☐ Run: git diff HEAD~10 --stat (look for similar implementations)
☐ Verify no copy-pasted code blocks
☐ Check that similar functionality uses shared base classes/helpers
☐ Review for repeated validation, error handling, or initialization logic
```

### Architecture Review
```
☐ All business logic in Core/Service/Protocol layers
☐ UI only CALLS business logic, never IMPLEMENTS it
☐ Single Responsibility Principle maintained
☐ No "God classes" (>500 lines is a red flag)
☐ Proper separation of concerns
```

### Rule 3: Test Plan for Validation
```
☐ Create comprehensive test plan document for the phase
☐ Test plan must cover ALL new functionality added in this phase
☐ Test plan must be written for a tester (clear, step-by-step instructions)
☐ Include expected results for each test case
☐ Include both positive and negative test scenarios
☐ Document test prerequisites (test servers, accounts, etc.)
☐ Test plan document created in docs/ folder
☐ Test plan reviewed and approved before phase marked complete
```

### If Violations Found
```
1. STOP immediately
2. Refactor violations into proper layers
3. Commit fixes with descriptive messages
4. Re-run checklist
5. ONLY THEN mark phase as complete
```

**Remember**: These rules are NON-NEGOTIABLE. Clean architecture now prevents technical debt later.

---

## Phase 1: Core Foundation (Weeks 1-2)

### Week 1: Terminal Buffer & Core Data Structures

#### Day 1-2: Project Setup
- [x] ✅ Create solution structure (`RetroTerm.sln`)
- [x] ✅ Create `RetroTerm.Core` project (netstandard2.1)
- [x] ✅ Create `RetroTerm.Core.Protocols` project (netstandard2.1)
- [x] ✅ Create `RetroTerm.Core.Protocols.Net` project (net9.0)
- [x] ✅ Create `RetroTerm.Core.Protocols.WebSocket` project (netstandard2.1)
- [x] ✅ Create `RetroTerm.Desktop` project (net9.0, Avalonia)
- [x] ✅ Create `RetroTerm.Tests` project (net9.0, xUnit)
- [x] ✅ Set up .gitignore, EditorConfig, Directory.Build.props
- [x] ✅ Install NuGet packages (see dependencies list)

#### Day 3-4: Terminal Buffer
- [x] ✅ `TerminalCell.cs` - Cell with char, attributes, colors
  - [x] ✅ Implement packed struct for memory efficiency
  - [x] ✅ Foreground/background color (24-bit + palette index)
  - [x] ✅ Character attributes (bold, dim, underline, blink, reverse, hidden)
  - [x] ✅ Character set designation (G0-G3)
  - [x] ✅ Double-width/height flags
  - [x] ✅ Field attributes (for 3270 compatibility)
- [x] ✅ `TerminalBuffer.cs` - 2D scrolling buffer
  - [x] ✅ Primary buffer (visible screen)
  - [x] ✅ Scrollback buffer (history)
  - [x] ⚠️ Alternate screen buffer (for full-screen apps) - Basic support
  - [x] ✅ Efficient row-based storage with `Memory<TerminalCell>`
  - [x] ✅ Scrolling operations (up/down/left/right)
  - [x] ✅ Region operations (insert/delete lines/characters)
  - [x] ✅ Clear operations (screen, line, region)
- [x] ✅ Unit tests for TerminalBuffer

#### Day 5: Character Attributes & Colors
- [x] ✅ `CharacterAttributes.cs` - Attribute flags enum
- [x] ⚠️ `ColorPalette.cs` - Color management (in TerminalColor.cs)
  - [x] ✅ 16-color ANSI palette
  - [x] ✅ 256-color extended palette
  - [x] ✅ 24-bit true color support
  - [x] ✅ Named color schemes
  - [x] ✅ Dark/light theme support
- [x] ✅ `CursorState.cs` - Cursor position, visibility, style (Cursor.cs, CursorStyle.cs)
- [x] ✅ Unit tests for color and attributes

### Week 2: Escape Sequence Parser & Base Classes

#### Day 6-7: Hybrid Escape Sequence Parser
- [x] ⚠️ `EscapeSequenceParser.cs` - Main parser coordinator
  - [ ] ❌ Fast path: Hash lookup for common sequences (DEFERRED to Phase 6 - performance optimization)
  - [x] ✅ Slow path: State machine for complex/long sequences
  - [ ] ❌ Zero-allocation design with `Span<T>` and `stackalloc` (DEFERRED to Phase 6 - optimization)
  - [x] ✅ Streaming support (incomplete sequences)
- [ ] ❌ `FastPathSequenceHandler.cs` - Hash-based lookup (DEFERRED to Phase 6 - performance optimization)
  - [ ] ❌ Pre-computed hash table for common CSI sequences
  - [ ] ❌ Direct dispatch to handlers
- [ ] ❌ `EscapeSequenceStateMachine.cs` - ECMA-48 state machine (embedded in parser, no separate class needed)
  - [x] ✅ States: Ground, Escape, CSI Entry, CSI Param, CSI Intermediate, CSI Final
  - [x] ✅ DCS, OSC, APC, PM, SOS handling
  - [x] ✅ C0/C1 control handling
- [ ] ❌ `SequenceLookupTables.cs` - Pre-computed lookup tables (DEFERRED to Phase 6 - performance optimization)
- [x] ⚠️ Unit tests for parser (has tests, comprehensive coverage deferred)

#### Day 8-9: TerminalEmulatorBase (Concrete Character-Mode Base)
- [x] ✅ `ITerminalEmulator.cs` - Interface for all emulators (COMPLETED 2025-10-18)
- [x] ⚠️ `TerminalEmulatorBase.cs` - Concrete base class (implements ITerminalEmulator)
  - [x] ⚠️ ECMA-48/ISO 6429 complete implementation (basic VT100, complete VT220 in Phase 2)
  - [x] ⚠️ CSI sequences: CUU, CUD, CUF, CUB, CHA, CUP, ED, EL, IL, DL, DCH, ICH, TBC, etc. (VT100 complete)
  - [x] ⚠️ C0 controls: BEL, BS, HT, LF, VT, FF, CR, SO, SI (all VT100 implemented)
  - [x] ⚠️ C1 controls: IND, NEL, HTS, RI, SS2, SS3, DCS, CSI, ST, OSC (VT100 subset implemented)
  - [x] ✅ SGR (Select Graphic Rendition) - all attributes and colors
  - [x] ✅ Scrolling region management (DECSTBM) (COMPLETED 2025-10-19)
  - [x] ⚠️ Alternate screen buffer (DECSC/DECRC) - Basic support
  - [x] ✅ Cursor save/restore (DECSC ESC 7 / DECRC ESC 8) (COMPLETED 2025-10-19)
  - [x] ✅ Tab stop management (HTS, TBC, default stops) (COMPLETED 2025-10-18)
  - [x] ⚠️ Character set designation (G0-G3, ISO 2022) - Variables exist, full implementation in Phase 2
  - [x] ⚠️ Template methods for derived classes to override
- [x] ⚠️ Unit tests for TerminalEmulatorBase (integration tests exist, comprehensive unit tests deferred)

#### Day 10: Scrolling Engine
- [x] ✅ `ScrollingEngine.cs` - STEP and SMOOTH scrolling
  - [x] ✅ Instant scroll (STEP mode)
  - [x] ✅ Animated scroll (SMOOTH mode) with interpolation
  - [x] ✅ Configurable animation speed
  - [x] ✅ Smooth scroll state tracking (pixel offset)
- [x] ✅ Unit tests for ScrollingEngine

---

## Phase 2: Character-Mode Terminals (Weeks 3-6)

### Week 3: VT Series Emulators

#### Day 11-12: VT52 Emulator
- [ ] ❌ `VT/VT52Emulator.cs`
  - [ ] ❌ VT52 escape sequences (cursor movement, erase, etc.)
  - [ ] ❌ Graphics mode (line drawing)
  - [ ] ❌ ANSI mode switch (ESC <)
- [ ] ❌ Unit tests for VT52

#### Day 13-15: VT100 Emulator
- [x] ⚠️ `VT/VT100Emulator.cs` (thin wrapper around TerminalEmulatorBase)
  - [x] ⚠️ All VT100 CSI sequences (many in base class)
  - [ ] ❌ DEC Private Mode sequences (DECSET/DECRST) - Not fully implemented
  - [ ] ❌ Character sets: US ASCII, UK, DEC Special Graphics
  - [ ] ❌ G0-G3 designation and invocation
  - [ ] ❌ 80/132 column mode
  - [ ] ❌ Smooth scroll (DECSCLM)
  - [ ] ❌ Reverse video (DECSCNM)
  - [x] ✅ Origin mode (DECOM) - Variable exists
  - [x] ✅ Auto wrap (DECAWM) - Variable exists
  - [x] ✅ Keypad modes (DECPAM/DECPNM) - Variables exist
- [ ] ❌ `VT/VT100Modes.cs` - Mode flags and state (in base class)
- [ ] ❌ Unit tests for VT100 (comprehensive)

### Week 4: VT220, xterm, ANSI

#### Day 16-17: VT220 Emulator
- [ ] ❌ `VT/VT220Emulator.cs` (extends VT100)
  - [ ] ❌ 8-bit controls (C1 set)
  - [ ] ❌ Additional CSI sequences (ECH, CBT, etc.)
  - [ ] ❌ User-defined keys (DECUDK)
  - [ ] ❌ Soft character sets (DECDLD)
  - [ ] ❌ Status line
  - [ ] ❌ VT220 specific modes
- [ ] ❌ Unit tests for VT220

#### Day 18: xterm Emulator
- [ ] ❌ `VT/XTermEmulator.cs` (extends VT220)
  - [ ] ❌ 256-color support (exists in TerminalColor but no xterm emulator)
  - [ ] ❌ 24-bit true color (ISO-8613-3)
  - [ ] ❌ Mouse tracking (X10, Normal, Button, Any, SGR modes)
  - [ ] ❌ Alternate screen buffer
  - [ ] ❌ Window title (OSC sequences)
  - [ ] ❌ xterm-specific modes
- [ ] ❌ Unit tests for xterm

#### Day 19: ANSI Emulator
- [ ] ❌ `ANSI/ANSIEmulator.cs`
  - [ ] ❌ ANSI X3.64 sequences
  - [ ] ❌ PC-style extended characters
  - [ ] ❌ DOS/Windows console compatibility
- [ ] ❌ Unit tests for ANSI

#### Day 20: Character Set System
- [ ] ❌ `CharacterSets/ICharacterSetProvider.cs` - Interface
- [ ] ❌ `CharacterSets/ISO2022Manager.cs` - G0-G3 mechanism
  - [ ] ❌ Character set designation (ESC ( ), ESC ) ), etc.)
  - [ ] ❌ Single shift (SS2, SS3)
  - [ ] ❌ Locking shift (LS2, LS3)
- [ ] ❌ `CharacterSets/VT100CharacterSets.cs`
  - [ ] ❌ US ASCII
  - [ ] ❌ UK
  - [ ] ❌ DEC Special Graphics (line drawing)
  - [ ] ❌ DEC Supplemental
- [ ] ❌ Unit tests for character sets

### Week 5-6: TDV Emulators

#### Day 21-22: TDVEmulatorBase
- [x] ✅ `TDV/TDVEmulatorBase.cs` - Shared TDV logic
  - [x] ✅ Inherit from TerminalEmulatorBase
  - [x] ✅ VT100 compatibility layer
  - [x] ✅ ND-specific CSI sequences
  - [x] ✅ Protected areas (SPA/EPA)
  - [x] ✅ Work areas (NDDWA)
  - [x] ✅ Rectangle operations (NDILWA, NDDLWA, NDICHE, NDDCHE)
  - [x] ✅ Message LEDs (NDCLED, NDSLED, NDBLED)
  - [x] ✅ Smooth scroll mode (NDSSM)
  - [x] ✅ Blink modes (NDBLWM, NDELWM)
  - [x] ✅ 10 character sets (Graphics I/II, Math, Greek, Diacritics, Box, NIX, T)
- [x] ✅ `TDVCharacterSetManager.cs` - All TDV character sets
- [x] ✅ Unit tests for TDVEmulatorBase

#### Day 23-24: TDV1200 Emulator
- [x] ✅ `TDV/TDV1200Emulator.cs`
  - [x] ✅ ISO 646/2022/6429 compliant implementation
  - [x] ✅ 2115 compatibility mode (enter: CSI 66 h, exit: ESC Q)
  - [x] ✅ All ND-specific sequences from ND-12054-1-EN spec
  - [x] ✅ Double-width/height lines (ESC # 3-6)
  - [x] ✅ Function key sequences (native and 2115 mode)
- [x] ✅ 2115 mode state (within emulators)
- [x] ✅ Unit tests for TDV1200

#### Day 25-26: TDV2215 Emulator
- [x] ✅ `TDV/TDV2215Emulator.cs`
  - [x] ✅ Dual-mode: 2115 compatibility + extended mode
  - [x] ✅ Extended mode C0/C1 controls
  - [x] ✅ Three-character ESC sequences
  - [x] ✅ Extended CSI sequences
  - [x] ✅ Transparent mode support
  - [x] ✅ DCS sequences (PUSH-key, PROGRAM-key loading)
  - [x] ✅ Function key programming
- [x] ✅ Unit tests for TDV2215

#### Day 27-28: TDV2200 Emulator
- [x] ✅ `TDV/TDV2200Emulator.cs`
  - [x] ✅ TDV 2200/9 specific features
  - [x] ✅ Graphics extension board support (flags/mode switching)
  - [x] ✅ ISO 646-NO character mapping (configurable per connection)
- [x] ✅ `TDVISO646VariantHandler.cs` - Norwegian, Swedish, Danish, etc.
- [x] ✅ Unit tests for TDV2200

#### Day 29-30: TDV Utilities
- [x] ✅ `TDV/TDVProtectedAreas.cs` - SPA/EPA management
- [x] ✅ `TDV/TDVPushKeys.cs` - Programmable PUSH keys
- [x] ✅ `TDV/TDVRectangleOperations.cs` - Rectangle manipulation
- [x] ✅ Integration tests for all TDV emulators

---

## Phase 3: Block-Mode & Graphics (Weeks 7-9)

### Week 7: IBM 3270 Architecture

#### Day 31-33: IBM3270EmulatorBase
- [ ] ❌ `IBM3270/IBM3270EmulatorBase.cs` - Block-mode base
  - [ ] ❌ Structured field buffer (not streaming character mode)
  - [ ] ❌ AID keys (Enter, PF1-PF24, PA1-PA3, Clear)
  - [ ] ❌ Field attributes (protected/unprotected, numeric/alpha, intensified, hidden)
  - [ ] ❌ Modified Data Tag (MDT) tracking
  - [ ] ❌ Field navigation (Tab, Backtab)
  - [ ] ❌ 3270 data stream parser
  - [ ] ❌ Read Buffer, Read Modified, Read Modified All
  - [ ] ❌ Write, Erase/Write, Erase/Write Alternate
- [ ] ❌ `IBM3270/IBM3270Buffer.cs` - Structured field buffer
- [ ] ❌ `IBM3270/IBM3270Parser.cs` - Data stream parser
  - [ ] ❌ WCC (Write Control Character)
  - [ ] ❌ Orders: SF, SFE, SA, MF, IC, PT, RA, EUA, GE
  - [ ] ❌ Structured fields
- [ ] ❌ `IBM3270/IBM3270FieldManager.cs` - Field management
- [ ] ❌ Unit tests for IBM3270EmulatorBase

#### Day 34-35: IBM 3270 Model Emulators
- [ ] ❌ `IBM3270/IBM3270Model2Emulator.cs` - 24×80
- [ ] ❌ `IBM3270/IBM3270Model3Emulator.cs` - 32×80
- [ ] ❌ `IBM3270/IBM3270Model4Emulator.cs` - 43×80
- [ ] ❌ `IBM3270/IBM3270Model5Emulator.cs` - 27×132
- [ ] ❌ Unit tests for all models

### Week 8: Graphics Support - ReGIS & Sixel

#### Day 36-38: VT340 & ReGIS
- [ ] ❌ `VT/VT340Emulator.cs` (extends VT220)
  - [ ] ❌ ReGIS mode entry/exit
  - [ ] ❌ Sixel mode entry/exit
  - [ ] ❌ Color palette management (16 colors, configurable)
- [ ] ❌ `Graphics/IGraphicsEngine.cs` - Graphics abstraction
- [ ] ❌ `Graphics/ReGISParser.cs` - ReGIS instruction parser
  - [ ] ❌ Commands: P[x,y] (position), V[dx,dy] (vector)
  - [ ] ❌ T"text" (text), C(color), S(screen), W(write controls)
  - [ ] ❌ F(filled) patterns, A(arc) commands
- [ ] ❌ `Graphics/ReGISRenderer.cs` - ReGIS to bitmap renderer
  - [ ] ❌ Vector rendering (lines, arcs, curves)
  - [ ] ❌ Text rendering
  - [ ] ❌ Fill patterns
  - [ ] ❌ SkiaSharp-based implementation
- [ ] ❌ Unit tests for ReGIS

#### Day 39-40: Sixel Graphics
- [ ] ❌ `Graphics/SixelParser.cs` - Sixel sequence parser
  - [ ] ❌ DCS q ... ST format
  - [ ] ❌ Raster attributes
  - [ ] ❌ Color definition (#)
  - [ ] ❌ Repeat sequences (!)
  - [ ] ❌ Sixel data decoding
- [ ] ❌ `Graphics/SixelRenderer.cs` - Sixel to bitmap renderer
- [ ] ❌ Unit tests for Sixel

### Week 9: Tektronix 4010 & TDV Graphics

#### Day 41-42: Tektronix 4010
- [ ] ❌ `Graphics/Tektronix4010Emulator.cs`
  - [ ] ❌ Vector graphics mode
  - [ ] ❌ Alpha mode
  - [ ] ❌ Plot/draw commands
  - [ ] ❌ GIN mode (cursor input)
- [ ] ❌ Unit tests for Tektronix 4010

#### Day 43-45: TDV Graphics Extensions
- [ ] ❌ `Graphics/TDVGraphicsParser.cs` - TDV proprietary graphics
  - [ ] ❌ Placeholder for now (specs TBD)
  - [ ] ❌ Pixel addressing
  - [ ] ❌ Line drawing
  - [ ] ❌ Tektronix 4010 compatibility mode
- [ ] ❌ `Graphics/TDVGraphicsRenderer.cs` - TDV graphics renderer
  - [ ] ❌ Placeholder implementation
- [ ] ❌ Document graphics extension board details needed

---

## Phase 4: Desktop UI (Avalonia) (Weeks 10-12)

### Week 10: Avalonia Project Setup & Basic UI

#### Day 46-47: Project Setup
- [x] ✅ Create `RetroTerm.Desktop` project
- [x] ✅ Install Avalonia packages (11.0+)
- [ ] ❌ Set up MVVM architecture (all code-behind, no ViewModels)
- [ ] ❌ Analyze RetroCommander StyleGuide.md
- [ ] ❌ Create `Styles/RetroStyle.axaml` based on StyleGuide
  - [ ] ❌ Colors, shapes, effects from StyleGuide
  - [ ] ❌ Dark theme support
  - [ ] ❌ Button, TextBox, ComboBox, Menu styles
- [ ] ❌ Create `Styles/Colors.axaml` - Color resources

#### Day 48-50: Main Window
- [x] ⚠️ `Views/MainWindow.axaml/.cs` - MDI container
  - [x] ⚠️ Menu bar (File, Help only, missing Edit, View, Connection, Tools)
  - [ ] ❌ Toolbar with quick actions
  - [ ] ❌ TabControl for terminal tabs (when DisplayMode = Tabbed)
  - [x] ✅ Status bar with connection info
  - [ ] ❌ Window chrome customization
- [ ] ❌ `ViewModels/MainWindowViewModel.cs` (NO VIEWMODELS)
  - [ ] ❌ Session management integration
  - [ ] ❌ Command bindings
  - [ ] ❌ Tab management
- [x] ⚠️ Menu system implementation
  - [x] ⚠️ File: New Connection (as Connect), Exit (missing Close Tab)
  - [ ] ❌ Edit: Copy, Paste, Select All, Find
  - [ ] ❌ View: Tabs/Windows toggle, Keyboard, Zoom
  - [ ] ❌ Connection: Connect, Disconnect, Settings
  - [ ] ❌ Tools: Settings, Themes, Fonts
  - [x] ⚠️ Help: About (handler exists, implementation unknown), Documentation missing

### Week 11: Terminal Rendering & Views

#### Day 51-53: Terminal Renderer
- [x] ⚠️ `Rendering/TerminalRenderer.cs` - Avalonia renderer
  - [x] ✅ Custom Avalonia control
  - [x] ⚠️ Glyph rendering with SkiaSharp (uses DrawText, not glyph-level)
  - [ ] ❌ Dirty region tracking (renders entire buffer)
  - [ ] ❌ Double buffering (relies on Avalonia)
  - [x] ⚠️ Hardware acceleration where available (Avalonia default)
- [ ] ❌ `Rendering/TextGlyphCache.cs` - Glyph performance cache
  - [ ] ❌ Pre-rendered glyph atlas
  - [ ] ❌ Font-specific caching
  - [ ] ❌ Cache invalidation on font change
- [ ] ❌ `Rendering/SmoothScrollAnimator.cs` - 60fps smooth scroll
  - [ ] ❌ Avalonia animation system integration
  - [ ] ❌ Easing functions
  - [ ] ❌ Pixel offset tracking
- [ ] ❌ `Rendering/GraphicsOverlay.cs` - ReGIS/TDV graphics layer
  - [ ] ❌ Separate layer for graphics
  - [ ] ❌ Compositing with text layer

#### Day 54-55: Terminal Views
- [ ] ❌ `Views/TerminalTabView.axaml/.cs` - Tab mode terminal
  - [ ] ❌ TerminalRenderer integration
  - [ ] ❌ Input handling (keyboard, mouse, paste)
  - [ ] ❌ Selection handling
  - [ ] ❌ Context menu
- [ ] ❌ `Views/TerminalWindow.axaml/.cs` - Windowed mode terminal
  - [ ] ❌ Standalone window for single terminal
  - [ ] ❌ Same rendering as tab mode
- [ ] ❌ `ViewModels/TerminalViewModel.cs` (NO VIEWMODELS)
  - [ ] ❌ Terminal emulator binding
  - [ ] ❌ Connection state
  - [ ] ❌ Input/output handling
- [ ] ❌ `ViewModels/SessionTabViewModel.cs` - Per-tab VM (NO VIEWMODELS)

### Week 12: Dialogs & Advanced UI

#### Day 56-58: Connection Dialog
- [x] ⚠️ `Views/ConnectionDialog.cs` (code-only, no XAML)
  - [x] ⚠️ Protocol selection (Telnet, SSH only - missing TN3270, Serial, XOT, SINTRAN)
  - [x] ✅ Host/port configuration
  - [x] ⚠️ SSH authentication (password only, no key file)
  - [ ] ❌ Terminal type selection
  - [ ] ❌ Advanced settings (expandable)
  - [ ] ❌ Saved connections list
  - [ ] ❌ Test connection button
- [ ] ❌ `ViewModels/ConnectionDialogViewModel.cs` (NO VIEWMODELS)
  - [ ] ❌ Form validation
  - [ ] ❌ Connection profiles management
  - [ ] ❌ Test connection logic

#### Day 59-60: Settings Window
- [ ] ❌ `Views/SettingsWindow.axaml/.cs`
  - [ ] ❌ General: Tab/window mode, theme, language
  - [ ] ❌ Terminal: Font, size, colors, scrollback
  - [ ] ❌ Scroll: STEP/SMOOTH mode, animation speed
  - [ ] ❌ Character sets: ISO 646 variant selection
  - [ ] ❌ Keyboard: Key bindings, paste confirmation
  - [ ] ❌ Advanced: Buffer size, performance options
- [ ] ❌ `ViewModels/SettingsViewModel.cs` (NO VIEWMODELS)
  - [ ] ❌ Settings binding
  - [ ] ❌ Apply/Cancel logic
  - [ ] ❌ Live preview

---

## Phase 5: Protocol Layer (Weeks 13-14)

### Week 13: Core Protocols

#### Day 61-63: Protocol Interfaces & Core
- [ ] `RetroTerm.Core.Protocols/IConnection.cs` - Connection abstraction
  - [ ] Connect/Disconnect async methods
  - [ ] Send/Receive async methods
  - [ ] DataReceived event
  - [ ] Connection state
- [ ] `RetroTerm.Core.Protocols/IFileTransferProtocol.cs` - File transfer abstraction
  - [ ] Upload/Download methods
  - [ ] Progress reporting
  - [ ] Directory listing
  - [ ] Protocol capabilities
- [ ] `RetroTerm.Core.Protocols/Mock/MockConnection.cs` - In-memory connection
  - [ ] Queue-based send/receive
  - [ ] For unit testing
- [ ] Unit tests for mock connection

#### Day 64-66: Telnet Implementation
- [ ] `RetroTerm.Core.Protocols.Net/TelnetClient.cs`
  - [ ] RFC 854 Telnet protocol
  - [ ] IAC command handling
  - [ ] Option negotiation (WILL, WON'T, DO, DON'T)
  - [ ] Subnegotiation (NAWS, TERMINAL-TYPE, etc.)
  - [ ] NAWS (Negotiate About Window Size)
  - [ ] TERMINAL-TYPE announcement
  - [ ] Binary mode
  - [ ] Echo handling
- [ ] Unit tests for Telnet

#### Day 67-70: SSH Implementation
- [ ] `RetroTerm.Core.Protocols.Net/SSHClient.cs`
  - [ ] SSH.NET library integration
  - [ ] Password authentication
  - [ ] Key-based authentication
  - [ ] Host key verification
  - [ ] Shell channel
  - [ ] Terminal size negotiation
- [ ] `RetroTerm.Core.Protocols.Net/SSHHostKeyManager.cs`
  - [ ] known_hosts.json management
  - [ ] Host key fingerprint verification
  - [ ] User prompt for unknown hosts
  - [ ] Key format: MD5, SHA256
- [ ] Unit tests for SSH (with mock server)

### Week 14: Advanced Protocols

#### Day 71-73: TN3270 Implementation
- [ ] `RetroTerm.Core.Protocols.Net/TN3270Client.cs`
  - [ ] TN3270 protocol (RFC 1576)
  - [ ] TN3270E extensions (RFC 2355)
  - [ ] 3270 data stream handling
  - [ ] Device name negotiation
  - [ ] EOR (End of Record) handling
- [ ] Unit tests for TN3270

#### Day 74-75: Serial Port Support
- [ ] `RetroTerm.Core.Protocols.Net/SerialConnection.cs`
  - [ ] System.IO.Ports integration
  - [ ] Baud rate, parity, stop bits configuration
  - [ ] Flow control (RTS/CTS, XON/XOFF)
  - [ ] COM port enumeration
- [ ] Unit tests for Serial (with mock serial port)

#### Day 76-77: HDLC & LAPB (Core Protocols)
- [ ] `RetroTerm.Core.Protocols/HDLC/HdlcFramer.cs`
  - [ ] ITU-T Q.921 framing
  - [ ] Flag bytes (0x7E)
  - [ ] Byte stuffing (0x7D escape)
  - [ ] FCS calculation (CRC-16)
- [ ] `RetroTerm.Core.Protocols/LAPB/LAPBStateMachine.cs`
  - [ ] ISO 7776 LAPB protocol
  - [ ] States: Disconnected, AwaitingConnection, Connected, etc.
  - [ ] Frame types: I, S (RR, RNR, REJ), U (SABM, DISC, UA, DM, FRMR)
  - [ ] Sequence numbering (modulo 8/128)
  - [ ] Window management
  - [ ] Timer management (T1, T2, T3)
  - [ ] Retransmission logic
- [ ] `RetroTerm.Core.Protocols/LAPB/LAPBConfiguration.cs`
  - [ ] Local/Remote Server ID (16-bit)
  - [ ] DTE/DCE mode
  - [ ] Window size, timeouts
- [ ] Unit tests for HDLC and LAPB

#### Day 78-80: XOT & SINTRAN Stubs (v1.0)
- [ ] `RetroTerm.Core.Protocols.Net/XOTConnection.cs` - XOT (RFC 1613) stub
  - [ ] TCP connection wrapper
  - [ ] XOT header handling
  - [ ] HDLC frame transport over TCP
  - [ ] Stub for v1.0, full implementation in v1.5
- [ ] `RetroTerm.Core.Protocols.Net/SINTRAN/` - SINTRAN stubs
  - [ ] `SintranPhysicalConnection.cs` - Physical connection (XOT or Serial)
  - [ ] `SintranLogicalSession.cs` - Logical session
  - [ ] `SintranPacketLayer.cs` - Packet layer stub
  - [ ] `SintranPADProtocol.cs` - PAD protocol stub (0xDA)
  - [ ] `SintranTADProtocol.cs` - TAD protocol stub (0xDD)
  - [ ] `SintranRoutingProtocol.cs` - Routing stub (0xDE)
  - [ ] `SintranFileTransferProtocol.cs` - File transfer stub
  - [ ] All stubs throw `NotImplementedException` with v1.5 message
- [ ] Document SINTRAN implementation requirements for v1.5

---

## Phase 6: Advanced Features (Weeks 15-17)

### Week 15: File Transfer Protocols

#### Day 81-83: SFTP/SCP Implementation
- [ ] `RetroTerm.Core.Protocols.Net/FileTransfer/SFTPService.cs`
  - [ ] SSH.NET SFTP client integration
  - [ ] Upload/download files
  - [ ] Directory listing
  - [ ] File permissions
  - [ ] Progress reporting
- [ ] `RetroTerm.Core.Protocols.Net/FileTransfer/SCPService.cs`
  - [ ] SSH.NET SCP client integration
  - [ ] Upload/download files
  - [ ] Recursive directory transfer
  - [ ] Progress reporting
- [ ] Unit tests for SFTP/SCP

#### Day 84-85: SFTP/SCP UI - Integrated Panel
- [ ] `Views/FileTransferPanel.axaml/.cs`
  - [ ] Integrated into terminal tab/window
  - [ ] Toggle visibility (Ctrl+T)
  - [ ] Local file browser (left pane)
  - [ ] Remote file browser (right pane)
  - [ ] Drag & drop support
  - [ ] Upload/download buttons
  - [ ] Progress bars
  - [ ] Queue management
- [ ] `ViewModels/FileTransferViewModel.cs`

#### Day 86-87: SFTP/SCP UI - Standalone Window
- [ ] `Views/FileTransferWindow.axaml/.cs`
  - [ ] Standalone window
  - [ ] Connection selector (saved SSH connections)
  - [ ] Dual-pane file browser
  - [ ] Drag & drop between panes
  - [ ] Queue management
  - [ ] Batch operations
- [ ] `ViewModels/FileTransferWindowViewModel.cs`

#### Day 88-90: Zmodem/Kermit/Xmodem Protocols
- [ ] `RetroTerm.Core.Protocols.Net/FileTransfer/ZmodemProtocol.cs`
  - [ ] Zmodem send/receive
  - [ ] Auto-detection (rz/sz commands)
  - [ ] CRC-16/CRC-32
  - [ ] Resume support
- [ ] `RetroTerm.Core.Protocols.Net/FileTransfer/KermitProtocol.cs`
  - [ ] Kermit send/receive
  - [ ] Packet format
  - [ ] Windowing
- [ ] `RetroTerm.Core.Protocols.Net/FileTransfer/XmodemProtocol.cs`
  - [ ] Xmodem/Ymodem send/receive
  - [ ] 128/1024 byte blocks
  - [ ] CRC/Checksum
- [ ] Unit tests for all protocols

### Week 16: Selection, Clipboard, Search

#### Day 91-93: Text Selection
- [ ] `Selection/ISelectionManager.cs` - Selection interface
- [ ] `Selection/TextSelection.cs` - Selection state
  - [ ] Start/end coordinates
  - [ ] Selection mode (character, word, line, rectangular)
- [ ] `Selection/SelectionMode.cs` - Mode enum
- [ ] `Selection/SelectionRenderer.cs` - Highlight rendering
  - [ ] Render selection overlay
  - [ ] Handle double-width characters
  - [ ] Rectangular selection rendering
- [ ] Mouse selection handling in TerminalRenderer
  - [ ] Click and drag
  - [ ] Double-click (word selection)
  - [ ] Triple-click (line selection)
  - [ ] Alt+drag (rectangular selection)
- [ ] Unit tests for selection

#### Day 94-95: Clipboard Integration
- [ ] `Clipboard/IClipboardService.cs` - Clipboard abstraction
- [ ] `Clipboard/ClipboardManager.cs`
  - [ ] Copy selected text
  - [ ] Paste text
  - [ ] HTML/RTF export (with colors/formatting)
- [ ] `Clipboard/ClipboardFormatter.cs` - Format converters
- [ ] `Clipboard/PasteConfirmationDialog.cs` - Security dialog
  - [ ] Show paste content preview
  - [ ] Warn for large pastes
  - [ ] Filter control characters
- [ ] Desktop: Avalonia clipboard integration
- [ ] Keyboard shortcuts (Ctrl+C, Ctrl+V, Ctrl+Shift+C/V)
- [ ] Unit tests for clipboard

#### Day 96-98: Search in Scrollback
- [ ] `Search/ISearchProvider.cs` - Search interface
- [ ] `Search/ScrollbackSearch.cs` - Search implementation
  - [ ] Plain text search
  - [ ] Case sensitive/insensitive
  - [ ] Whole word matching
  - [ ] Search direction (up/down)
- [ ] `Search/RegexSearchEngine.cs` - Regex support
- [ ] `Search/SearchHighlighter.cs` - Highlight matches
  - [ ] Render match highlights
  - [ ] Navigate between matches (F3, Shift+F3)
- [ ] `Views/SearchBar.axaml/.cs` - Search UI
  - [ ] Search box in terminal view
  - [ ] Match counter
  - [ ] Previous/next buttons
  - [ ] Options (case, regex, whole word)
- [ ] Unit tests for search

### Week 17: Unicode, Hyperlinks, Session Logging

#### Day 99-100: Unicode Support
- [ ] `Unicode/UTF8Decoder.cs` - Streaming UTF-8 decoder
  - [ ] Handle partial sequences across buffer boundaries
  - [ ] Invalid sequence handling
- [ ] `Unicode/EmojiRenderer.cs` - Emoji support
  - [ ] Detect emoji sequences
  - [ ] Double-width rendering
  - [ ] Color emoji rendering
- [ ] `Unicode/CombiningCharacterHandler.cs` - Diacritics
  - [ ] Combine base + combining characters
  - [ ] Render correctly
- [ ] `Unicode/DoubleWidthCharacterHandler.cs` - CJK support
  - [ ] Detect East Asian Wide characters
  - [ ] Double-width cell allocation
- [ ] `Unicode/BidirectionalTextHandler.cs` - RTL support
  - [ ] Basic RTL rendering (Arabic, Hebrew)
  - [ ] Bidirectional algorithm (simplified)
- [ ] Unit tests for Unicode

#### Day 101-102: Hyperlinks
- [ ] `Hyperlinks/URLDetector.cs` - Auto-detect URLs
  - [ ] Regex-based URL detection
  - [ ] Support http, https, ftp, file, etc.
- [ ] `Hyperlinks/OSC8HyperlinkHandler.cs` - Semantic links
  - [ ] OSC 8 sequence parsing (ESC ]8;;url\e\\text\e\\)
  - [ ] Link metadata
- [ ] `Hyperlinks/HyperlinkRenderer.cs` - Clickable links
  - [ ] Underline on hover
  - [ ] Click to open in browser
  - [ ] Ctrl+Click
  - [ ] Context menu (copy link, open)
- [ ] Unit tests for hyperlinks

#### Day 103-105: Session Logging
- [ ] `Session/SessionLogger.cs` - Log session to file
  - [ ] Plain text log
  - [ ] HTML log (with colors)
  - [ ] Timestamp options
  - [ ] Auto-log on connect
- [ ] `Session/SessionRecorder.cs` - Record/replay
  - [ ] Timing-based recording
  - [ ] Playback with timing
  - [ ] Export to asciicast format (asciinema)
- [ ] `Views/SessionLogDialog.axaml/.cs` - Log settings UI
- [ ] Unit tests for logging

---

## Phase 7: Blazor Web UI (Weeks 18-20)

### Week 18: Blazor Project Setup

#### Day 106-108: Blazor WASM Project
- [ ] Create `RetroTerm.Web` project (Blazor WASM)
- [ ] Configure for .NET 9 with AOT compilation
- [ ] Install NuGet packages
  - [ ] Microsoft.AspNetCore.Components.WebAssembly
  - [ ] Blazor.Extensions.Canvas
- [ ] Set up wwwroot structure
- [ ] Create `wwwroot/index.html`
- [ ] Create `wwwroot/css/retroterm.css` - Responsive styles
- [ ] Create `App.razor` - Root component
- [ ] Create `Program.cs` - WASM entry point
- [ ] Configure services (DI)

#### Day 109-110: Browser Storage
- [ ] `Services/BrowserConfigStorage.cs` - LocalStorage/IndexedDB
  - [ ] IConfigurationStorage implementation
  - [ ] Store connection profiles
  - [ ] Store settings
  - [ ] Store SSH host keys
- [ ] `Services/BrowserHostKeyStorage.cs` - Host key storage
- [ ] JavaScript interop for storage APIs

#### Day 111-112: WebSocket Connection
- [ ] `RetroTerm.Core.Protocols.WebSocket/WebSocketConnection.cs`
  - [ ] IConnection implementation
  - [ ] WebSocket client
  - [ ] JSON message protocol
  - [ ] Connect to proxy server
  - [ ] Handle connection, data, disconnect messages
- [ ] `RetroTerm.Core.Protocols.WebSocket/WebSocketProtocol.cs`
  - [ ] Message format definitions
  - [ ] Serialization/deserialization
- [ ] Unit tests for WebSocket connection

### Week 19: Blazor Terminal UI

#### Day 113-115: Canvas Rendering
- [ ] `wwwroot/js/terminal-canvas.js` - Canvas rendering JS
  - [ ] Initialize canvas
  - [ ] Draw text with font
  - [ ] Draw selection overlay
  - [ ] Draw cursor
  - [ ] Efficient dirty region updates
- [ ] `Rendering/CanvasTerminalRenderer.cs` - Blazor renderer
  - [ ] IJSRuntime integration
  - [ ] Batch buffer serialization
  - [ ] Single JS interop call per frame
  - [ ] Delta compression
- [ ] `Components/TerminalCanvas.razor` - Canvas component
  - [ ] Canvas element
  - [ ] Input handling (keyboard, mouse, paste)
  - [ ] Resize handling

#### Day 116-117: Terminal Pages & Components
- [ ] `Pages/Index.razor` - Landing page
  - [ ] Welcome screen
  - [ ] Quick connect
  - [ ] Recent connections
  - [ ] About
- [ ] `Pages/Terminal.razor` - Main terminal page
  - [ ] Terminal canvas
  - [ ] Connection status
  - [ ] Menu bar
- [ ] `Pages/Settings.razor` - Settings page
  - [ ] Terminal settings
  - [ ] Theme selection
  - [ ] Font configuration

#### Day 118-120: Multi-Session Tabs
- [ ] `Components/TerminalTabs.razor` - Tab component
  - [ ] Tab strip
  - [ ] Add/close tabs
  - [ ] Tab switching
  - [ ] Tab titles
- [ ] Session manager integration
- [ ] Tab state persistence (browser storage)

### Week 20: WASM Optimizations & Advanced Features

#### Day 121-123: Performance Optimizations
- [ ] Async input processing with `Task.Yield()`
  - [ ] Chunk processing (10KB per chunk)
  - [ ] Prevent UI blocking
- [ ] Memory management
  - [ ] Lower scrollback limits (1000 vs 10000)
  - [ ] Max 5 sessions
  - [ ] Memory monitoring
  - [ ] Trim on pressure
- [ ] Rendering optimization
  - [ ] Batch buffer updates
  - [ ] Delta compression
  - [ ] RequestAnimationFrame throttling
- [ ] IL trimming configuration
  - [ ] Remove unused emulators
  - [ ] Remove unused protocols
- [ ] Lazy loading
  - [ ] Load emulators on-demand
  - [ ] Code splitting

#### Day 124-125: WASM-to-WASM Interop
- [ ] `wwwroot/js/wasm-interop.js` - WASM bridge JS
  - [ ] Connect to other WASM modules in same page
  - [ ] Message passing
  - [ ] Shared memory (if available)
- [ ] `Services/WasmInteropService.cs` - Interop service
  - [ ] IConnection implementation for WASM-to-WASM
  - [ ] Connect to ND-100 emulator (or other)
- [ ] Configuration for WASM targets

#### Day 126-128: Blazor Visual Keyboard
- [ ] `Components/VisualKeyboard.razor`
  - [ ] SVG or Canvas-based keyboard
  - [ ] TDV keyboard layouts
  - [ ] Click handling
  - [ ] LED indicators (CSS-based)
  - [ ] Responsive design
- [ ] Mobile touch optimization

#### Day 129-130: PWA Support
- [ ] Service worker configuration
  - [ ] Offline caching
  - [ ] Update notifications
- [ ] PWA manifest
- [ ] Install prompt
- [ ] Offline mode UI

---

## Phase 8: WebSocket Proxy Server (Week 21)

### Week 21: Azure-Ready Proxy

#### Day 131-133: Proxy Core
- [ ] Create `RetroTerm.Proxy` project (ASP.NET Core)
- [ ] `Program.cs` - Application entry point
- [ ] `Startup.cs` - Service configuration
  - [ ] SignalR hub configuration
  - [ ] CORS configuration
  - [ ] Logging configuration
- [ ] `Hubs/TerminalProxyHub.cs` - SignalR hub
  - [ ] Connect/disconnect handling
  - [ ] Message routing
  - [ ] Protocol type handling (Telnet, SSH, TN3270)
- [ ] `Hubs/ProxyConnectionManager.cs` - Connection management
  - [ ] Track active connections
  - [ ] Connection limits enforcement
  - [ ] Cleanup on disconnect

#### Day 134-135: Protocol Proxies
- [ ] `Services/TelnetProxyService.cs` - WebSocket → Telnet
  - [ ] Create TCP connection to target
  - [ ] Bi-directional data relay
  - [ ] Connection lifecycle management
- [ ] `Services/SSHProxyService.cs` - WebSocket → SSH
  - [ ] SSH.NET integration
  - [ ] Credential handling (from client)
  - [ ] Bi-directional data relay
- [ ] `Services/TN3270ProxyService.cs` - WebSocket → TN3270
  - [ ] TN3270 connection
  - [ ] 3270 data stream relay

#### Day 136-137: Configuration & Middleware
- [ ] `Configuration/ProxySettings.cs` - Settings model
  - [ ] Connection limits (per client, total)
  - [ ] Allowed hosts whitelist
  - [ ] Port restrictions
- [ ] `Middleware/ConnectionLimitMiddleware.cs`
  - [ ] Rate limiting
  - [ ] Per-client connection count
  - [ ] Global connection limit
- [ ] `Middleware/HostWhitelistMiddleware.cs`
  - [ ] Validate destination hosts
  - [ ] Block disallowed targets
- [ ] `appsettings.json` - Default configuration
- [ ] `appsettings.Development.json` - Dev settings
- [ ] `appsettings.Production.json` - Production settings

#### Day 138-140: Azure Deployment
- [ ] `Azure/deployment.bicep` - Azure Bicep IaC
  - [ ] App Service plan
  - [ ] App Service
  - [ ] Application settings
  - [ ] Connection strings (if needed)
- [ ] `Azure/app-service-config.json` - App Service settings
- [ ] `Azure/README-Azure.md` - Deployment guide
  - [ ] Prerequisites
  - [ ] Deployment steps
  - [ ] Configuration
  - [ ] Monitoring
- [ ] Health check endpoints
- [ ] Logging integration (ready for App Insights)
- [ ] Test deployment to Azure
- [ ] Integration tests (Blazor → Proxy → Telnet/SSH)

---

## Phase 9: SINTRAN/XOT Implementation (v1.5) (Weeks 22-27)

### Week 22-23: SINTRAN Core Protocols

#### Day 141-145: Refine LAPB/HDLC Implementation
- [ ] Enhance `LAPBStateMachine.cs` with Server ID integration
  - [ ] Server ID in SABM frames
  - [ ] Server ID validation in UA frames
  - [ ] Connection establishment with ID exchange
- [ ] Enhance `HdlcFramer.cs`
  - [ ] Performance optimization
  - [ ] Streaming support
- [ ] Comprehensive LAPB unit tests
  - [ ] State transitions
  - [ ] Retransmission
  - [ ] Timer handling
  - [ ] Error recovery

#### Day 146-150: SINTRAN Packet Layer
- [ ] Research SINTRAN packet format from X25Emulator project
- [ ] Document packet structure
  - [ ] 0x21 0x13 markers
  - [ ] Protocol ID
  - [ ] Server IDs (source/destination)
  - [ ] PAD/TAD IDs (if multiplexing)
  - [ ] Sequence numbers (if applicable)
- [ ] Implement `SintranPacketLayer.cs`
  - [ ] Packet encoding/decoding
  - [ ] Multiplexing/demultiplexing
  - [ ] Routing based on Server ID
- [ ] Unit tests for packet layer

### Week 24: SINTRAN PAD/TAD Protocols

#### Day 151-153: PAD Protocol (0xDA)
- [ ] `SINTRAN/SintranPADProtocol.cs` - Full implementation
  - [ ] Terminal data transport
  - [ ] Flow control
  - [ ] Error handling
  - [ ] X.29-like functionality (if applicable)
- [ ] Connection dialog integration
  - [ ] SINTRAN PAD connection type
  - [ ] Server ID configuration
  - [ ] PAD ID configuration
- [ ] Unit tests for PAD protocol

#### Day 154-155: TAD Protocol (0xDD)
- [ ] `SINTRAN/SintranTADProtocol.cs` - Full implementation
  - [ ] Transparent data transport
  - [ ] Binary data handling
  - [ ] Use cases documentation
- [ ] Unit tests for TAD protocol

### Week 25: SINTRAN Routing & XOT

#### Day 156-158: Routing Protocol (0xDE)
- [ ] `SINTRAN/SintranRoutingProtocol.cs` - Full implementation
  - [ ] Routing table management
  - [ ] Machine ID routing
  - [ ] Multi-hop routing (if applicable)
- [ ] Unit tests for routing protocol

#### Day 159-160: XOT Implementation
- [ ] `XOTConnection.cs` - Full implementation (replace stub)
  - [ ] RFC 1613 XOT protocol
  - [ ] XOT header handling
  - [ ] HDLC frame transport over TCP
  - [ ] Connection establishment
- [ ] Gateway type configuration
  - [ ] DirectHDLC gateway
  - [ ] X25Routing gateway
- [ ] Unit tests for XOT

### Week 26: SINTRAN Physical & Logical Sessions

#### Day 161-163: Physical Connection
- [ ] `SINTRAN/SintranPhysicalConnection.cs` - Full implementation
  - [ ] XOT or Serial transport
  - [ ] LAPB state machine integration
  - [ ] Server ID management
  - [ ] Multiplexing support (if gateway supports)
  - [ ] Packet routing to logical sessions
- [ ] Connection pooling
  - [ ] Reuse physical connections
  - [ ] Session management

#### Day 164-165: Logical Session
- [ ] `SINTRAN/SintranLogicalSession.cs` - Full implementation
  - [ ] IConnection implementation
  - [ ] Session over physical connection
  - [ ] PAD/TAD ID management
  - [ ] Data queue management
- [ ] Session manager integration
  - [ ] Multiple sessions per physical connection
  - [ ] Session lifecycle

### Week 27: SINTRAN File Transfer & Testing

#### Day 166-168: File Transfer Protocol
- [ ] Research SINTRAN file transfer protocol
- [ ] Document protocol specification
  - [ ] Protocol ID (0x??)
  - [ ] File open/close commands
  - [ ] Data transfer format
  - [ ] Directory listing
  - [ ] Error handling
- [ ] `SINTRAN/SintranFileTransferProtocol.cs` - Full implementation
  - [ ] IFileTransferProtocol implementation
  - [ ] Upload/download
  - [ ] Directory operations
  - [ ] Progress reporting
- [ ] File transfer UI integration
  - [ ] SINTRAN FT option in protocol selector
  - [ ] Works when connected via XOT/Serial

#### Day 169-170: SINTRAN Testing
- [ ] Mock SINTRAN gateway
- [ ] Integration tests
  - [ ] XOT connection
  - [ ] Serial connection
  - [ ] PAD protocol
  - [ ] TAD protocol
  - [ ] File transfer
  - [ ] Multiple sessions
- [ ] Real ND-100 system testing (if available)
- [ ] Documentation
  - [ ] SINTRAN connection guide
  - [ ] Troubleshooting
  - [ ] Protocol details

---

## Phase 10: Testing, Polish & Documentation (Weeks 28-30)

### Week 28: Comprehensive Testing

#### Day 171-173: Unit Test Completion
- [ ] Achieve 80%+ code coverage for:
  - [ ] Core emulation classes
  - [ ] Protocol implementations
  - [ ] Parsers and renderers
  - [ ] State machines
- [ ] Edge case testing
  - [ ] Incomplete sequences
  - [ ] Invalid input
  - [ ] Buffer overflows
  - [ ] Memory pressure

#### Day 174-175: Integration Testing
- [ ] End-to-end connection tests
  - [ ] Telnet to real servers
  - [ ] SSH to real servers
  - [ ] TN3270 to mainframe emulators
  - [ ] Serial loopback tests
- [ ] Multi-session testing
  - [ ] Multiple tabs simultaneously
  - [ ] Session switching
  - [ ] Resource cleanup
- [ ] File transfer testing
  - [ ] SFTP/SCP large files
  - [ ] Zmodem transfers
  - [ ] Progress reporting accuracy

#### Day 176-177: vttest Integration
- [ ] Set up local vttest server
- [ ] Automated vttest execution
  - [ ] VT52 tests
  - [ ] VT100 tests
  - [ ] VT220 tests
  - [ ] Color tests
  - [ ] Character set tests
  - [ ] Cursor movement tests
  - [ ] Scrolling tests
- [ ] Document test results
- [ ] Fix failures

#### Day 178-180: Mock Server Testing
- [ ] Complete MockTerminalServer implementation
- [ ] Test sequence generators for:
  - [ ] All VT escape sequences
  - [ ] All TDV sequences
  - [ ] IBM 3270 data streams
  - [ ] Graphics commands (ReGIS, Sixel)
- [ ] Rendering accuracy validation
  - [ ] Pixel-perfect comparison tests
  - [ ] Color accuracy
  - [ ] Font rendering
- [ ] Performance benchmarks
  - [ ] Input processing speed
  - [ ] Rendering FPS
  - [ ] Memory usage
  - [ ] Startup time

### Week 29: Polish & UX Improvements

#### Day 181-183: Visual Keyboard - ✅ COMPLETE
- [x] ✅ Desktop: `Controls/VirtualKeyboardPanel.axaml/.cs`
  - [x] ✅ Avalonia implementation (120+ keys)
  - [x] ✅ TDV2200 layout (ND-246 keyboard)
  - [x] ✅ 12 national keyboard variants
  - [x] ✅ Key symbol rendering (SVG paths + font labels)
  - [x] ✅ Mouse click support (sends key sequences)
  - [x] ✅ Key capture mode for binding popup
  - [ ] ❌ IBM 3270 layout (deferred to v2.0)
  - [ ] ❌ LED indicators (deferred)
- [x] ✅ Key binding system (TDVKeyBindingConfiguration)
  - [x] ✅ ANY key combo → TDV grid position mapping
  - [x] ✅ JSON persistence (%AppData%\RetroTerm\tdv-key-bindings.json)
  - [x] ✅ Auto-migration from old format
- [ ] ❌ Blazor: Deferred to v2.0

#### Day 184-185: Terminal Skinning
- [ ] `Views/SkinManager.cs` - Skin system
- [ ] Create terminal skins in `assets/skins/`
  - [ ] TDV 1200 bezel and theme
  - [ ] TDV 2200 bezel and theme
  - [ ] TDV 2215 bezel and theme
  - [ ] VT100 bezel and theme
  - [ ] VT340 bezel and theme
  - [ ] IBM 3270 bezel and theme
- [ ] Skin selector in settings
- [ ] Custom chrome with bezel images
- [ ] CRT effects (optional)
  - [ ] `Rendering/CRTEffectShader.cs`
  - [ ] Scanlines
  - [ ] Glow
  - [ ] Curvature
  - [ ] Configurable intensity

#### Day 186-188: Font System Finalization
- [ ] `Fonts/FontManager.cs` - Font system
- [ ] `Fonts/TDVFontExtractor.cs` - Extract fonts from ROM dumps
  - [ ] Parse TDV 1200 ROMs (U5, U15)
  - [ ] Parse TDV 2200 ROMs (U32, U33, U63)
  - [ ] Generate bitmap fonts
  - [ ] Store in assets/fonts/
- [ ] `Fonts/BitmapFont.cs` - ROM-based fonts
- [ ] `Fonts/SystemFont.cs` - TrueType/OpenType fonts
  - [ ] System font enumeration
  - [ ] Font fallback
- [ ] Font selection UI in settings
- [ ] Font ligature support (via HarfBuzz)
  - [ ] Install Harfbuzz.Sharp
  - [ ] Text shaping integration
- [ ] Test all fonts with all emulators

#### Day 189-190: Accessibility & Keyboard
- [ ] Screen reader support
  - [ ] Accessible buffer content
  - [ ] Announce terminal output
  - [ ] Navigation hints
- [ ] High contrast mode
  - [ ] Detect OS high contrast setting
  - [ ] Override color scheme
- [ ] Keyboard shortcuts documentation
- [ ] Configurable key bindings
  - [ ] Key binding editor in settings
  - [ ] Import/export bindings
- [ ] Keyboard accessibility
  - [ ] Tab navigation
  - [ ] Focus indicators
  - [ ] Keyboard-only operation

### Week 30: Documentation & Release Preparation

#### Day 191-193: User Documentation
- [ ] `README.md` - Project overview (already created)
- [ ] `docs/user-guide.md` - Comprehensive user guide
  - [ ] Installation
  - [ ] Quick start
  - [ ] Connection types
  - [ ] Terminal types
  - [ ] File transfer
  - [ ] Settings and customization
  - [ ] Keyboard shortcuts
  - [ ] Troubleshooting
- [ ] `docs/terminal-specifications.md` - Terminal reference
  - [ ] VT52/VT100/VT220/VT340 escape sequences
  - [ ] TDV 1200/2200/2215 sequences
  - [ ] IBM 3270 data stream
  - [ ] ANSI/xterm extensions
- [ ] `docs/connection-guide.md` - Connection setup
  - [ ] Telnet configuration
  - [ ] SSH authentication
  - [ ] TN3270 setup
  - [ ] Serial port configuration
  - [ ] SINTRAN/XOT configuration
- [ ] Screenshot/screencast creation for documentation

#### Day 194-195: Developer Documentation
- [ ] `docs/architecture.md` - Architecture overview
- [ ] `docs/api-reference.md` - API documentation
  - [ ] ITerminalEmulator interface
  - [ ] IConnection interface
  - [ ] IFileTransferProtocol interface
  - [ ] Extension points
- [ ] `docs/contributing.md` - Contribution guide
  - [ ] Code style
  - [ ] Testing requirements
  - [ ] Pull request process
- [ ] `docs/building.md` - Build instructions
  - [ ] Prerequisites
  - [ ] Build steps
  - [ ] Platform-specific notes
- [ ] API XML comments completion
- [ ] Generate API documentation (DocFX or similar)

#### Day 196-197: Final Testing & Bug Fixes
- [ ] Regression testing
  - [ ] All features on all platforms
  - [ ] All terminal types
  - [ ] All connection types
- [ ] Performance profiling
  - [ ] Memory leaks
  - [ ] CPU usage
  - [ ] Rendering performance
- [ ] Bug bash
  - [ ] Known issues triage
  - [ ] Critical bug fixes
  - [ ] UX polish
- [ ] Stability testing
  - [ ] Long-running sessions
  - [ ] Large data transfers
  - [ ] Multiple simultaneous connections

#### Day 198-200: Release Preparation
- [ ] Version numbering (v1.0.0)
- [ ] Release notes
  - [ ] Features list
  - [ ] Known limitations
  - [ ] Breaking changes (N/A for v1.0)
- [ ] Packaging
  - [ ] Desktop: Windows installer (MSI/MSIX)
  - [ ] Desktop: Linux packages (deb, rpm, AppImage)
  - [ ] Desktop: macOS app bundle
  - [ ] Web: Deploy to hosting
  - [ ] Proxy: Azure App Service deployment
- [ ] License file (MIT or other)
- [ ] Create GitHub release
  - [ ] Tag v1.0.0
  - [ ] Upload binaries
  - [ ] Publish release notes
- [ ] Announcement
  - [ ] Blog post
  - [ ] Social media
  - [ ] Forums/communities

---

## Post-v1.0: Future Enhancements

### v1.1 - Minor Improvements (Optional)
- [ ] Additional color schemes
- [ ] More terminal skins
- [ ] Command palette (Ctrl+Shift+P)
- [ ] Split panes (horizontal/vertical)
- [ ] Session groups/workspaces
- [ ] Triggers and automation
- [ ] Shell integration (tmux, command markers)
- [ ] Input method editor (IME) support

### v1.5 - SINTRAN Complete (Weeks 22-27, covered above)
- [ ] Full SINTRAN protocol suite
- [ ] XOT complete implementation
- [ ] SINTRAN file transfer
- [ ] X.25 PAD standard support

### v2.0 - Advanced Features
- [ ] GPU acceleration (compute shaders)
- [ ] Terminal multiplexer (like tmux/screen)
- [ ] SSH agent forwarding
- [ ] Port forwarding UI
- [ ] Macro recording/playback
- [ ] Scripting support (Lua/Python)
- [ ] Plugin system
- [ ] Terminal sharing (collaborative)
- [ ] Cloud sync (settings, history)

---

## Critical Path Items

These items are dependencies for multiple phases:

1. **TerminalEmulatorBase** (Phase 1) - Everything depends on this
2. **EscapeSequenceParser** (Phase 1) - All emulators need this
3. **IConnection** (Phase 1) - All protocols need this
4. **TerminalRenderer** (Phase 4) - UI depends on this
5. **WebSocketConnection** (Phase 7) - Blazor depends on this
6. **Proxy Server** (Phase 8) - Blazor testing depends on this

---

## Risk Mitigation

### High-Risk Items

1. **Performance of Blazor WASM**
   - Mitigation: Aggressive optimization, chunking, delta updates
   - Fallback: Reduce feature set for Blazor version

2. **SINTRAN Protocol Unknowns**
   - Mitigation: Stub-first approach, defer to v1.5
   - Fallback: Wait for X25Emulator completion

3. **TDV2200 Specification Gap**
   - Mitigation: ROM dump reverse engineering
   - Fallback: Defer TDV2200 to v1.1

4. **Graphics Rendering Complexity**
   - Mitigation: Use SkiaSharp, incremental implementation
   - Fallback: Defer complex graphics to v1.1

5. **Timeline Overrun**
   - Mitigation: Phase-based delivery, MVP at Phase 5
   - Fallback: Ship desktop-only as v1.0, Blazor as v1.1

---

## Success Metrics

### v1.0 Completion Criteria

- [ ] All 10+ terminal emulators functional
- [ ] All 7+ protocols working (Telnet, SSH, TN3270, Serial, XOT stub, SINTRAN stub, WebSocket)
- [ ] Desktop UI (Avalonia) complete and polished
- [ ] Blazor UI functional (basic feature set)
- [ ] WebSocket proxy deployed to Azure
- [ ] File transfer (SFTP/SCP/Zmodem) working
- [ ] Text selection, copy/paste, search functional
- [ ] Unit test coverage >80%
- [ ] vttest passing for VT100/VT220
- [ ] Documentation complete
- [ ] Zero critical bugs
- [ ] Performance: <100ms input latency, >30fps rendering

---

## Daily Standup Template

```
Date: [YYYY-MM-DD]
Phase: [Phase X - Description]
Day: [Day X of Phase]

Completed Yesterday:
- [ ] Task 1
- [ ] Task 2

Today's Goals:
- [ ] Task 1
- [ ] Task 2

Blockers:
- None / [Describe blocker]

Open Questions:
- [Reference OPEN-QUESTIONS.md if needed]
```

---

**Last Updated**: 2026-02-15
**Status**: Phase 2 TDV COMPLETE ✅ | Virtual Keyboard COMPLETE ✅ | Key Binding System COMPLETE ✅ | TelnetServer Library COMPLETE ✅
**Tests**: 2069 passing, 41 skipped
**Next Steps**: Step 1 Documentation cleanup → Step 2 TDV 100% tests → Step 3 Desktop UI polish → Step 4 VT220/xterm

## Recent Achievements (2026-02-15)

### TDV Emulators - Complete
- [x] ✅ TDVEmulatorBase with composition pattern (components: ProtectedAreas, WorkAreas, MessageLEDs, RectangleOps, CharacterSets, PushKeys)
- [x] ✅ TDV1200, TDV2215, TDV2200 all implemented with comprehensive tests
- [x] ✅ ISO646 variant selection, graphics extension mode, Tektronix mode
- [x] ✅ TDVCharacterSetManager, TDVISO646VariantHandler, TDVDCSHandlerFeature

### Virtual Keyboard - Complete
- [x] ✅ VirtualKeyboardPanel with 120+ keys rendered via custom Avalonia controls
- [x] ✅ 12 national keyboard layouts (Norwegian, Swedish, Danish, German, etc.)
- [x] ✅ Key symbol rendering (SVG paths, font-rendered labels)
- [x] ✅ Key capture mode for key binding popup

### Key Binding System - Complete (replaced AltKeyConfiguration)
- [x] ✅ TDVKeyBindingConfiguration singleton: maps ANY key combo to TDV grid position
- [x] ✅ KeyBindingSource (VKCode + KeyModifiers) → KeyBindingTarget (GridPosition + Shifted)
- [x] ✅ JSON storage: %AppData%\RetroTerm\tdv-key-bindings.json (version 2 format)
- [x] ✅ Auto-migration from old alt-key-config.json

### Keyboard Architecture - Unified
- [x] ✅ Single TDV2200KeyboardMapper (type aliases for 1200/2215)
- [x] ✅ TDV2200KeyRegistry: 268 key mappings, single source of truth
- [x] ✅ 4-step MapKey resolution: user bindings → 2115 C0 → registry → Backspace fallback
- [x] ✅ NO VT220 fallback in TDV mode

### TelnetServer Library - Extracted
- [x] ✅ src\RetroTerm.Core.Protocols.TelnetServer\ (new project)
- [x] ✅ TelnetServer, TelnetSession (input pump model), TelnetCodec, TelnetNegotiator, ITelnetApp
- [x] ✅ InputParser, TDVCapabilityChecker, TDVResponseValidator, TDVSequenceBuilder

### Test Infrastructure
- [x] ✅ 2069 tests passing (up from 142 at TDV2200 milestone)
- [x] ✅ 41 skipped (scroll region bug, Avalonia context, TCP timing)
- [x] ✅ Comprehensive visual rendering tests with screenshot comparison

