# RetroTerm - Open Questions & Decisions Needed

This document tracks outstanding questions and unknowns that need to be resolved during development.

## 1. TDV Specifications & ROM Dumps

### Q1.1: TDV2200/9 Functional Specifications
**Status**: ⚠️ Partially documented  
**Issue**: ROM dumps and pictures available, but no functional specification document like TDV1200 (ND-12054-1-EN)

**Options**:
- a) Proceed with reverse engineering from ROM dumps
- b) Defer full implementation to v1.5
- c) Skip TDV2200/9 until specs are found

**Decision**: TBD  
**Impact**: Medium - affects v1.0 scope

### Q1.2: TDV Graphics Extension Details
**Status**: ⏳ No specifications  
**Issue**: TDV 2200 series "graphic extension" boards mentioned, but no protocol details

**Options**:
- a) Defer all TDV graphics to v1.5+ (placeholder only)
- b) Research now if critical for v1.0
- c) Focus on finding documentation first

**Decision**: TBD  
**Impact**: Low for v1.0 (can be deferred)

---

## 2. SINTRAN Protocol Details

### Q2.1: SINTRAN Packet Format
**Status**: 🔍 Speculative  
**Issue**: Exact packet format not fully documented
- Known: `0x21 0x13 [protocol]`
- Unknown: Server ID placement, PAD ID placement, sequence numbers, flow control

**Research Needed**:
- Analyze X25Emulator project when stable
- Review any available SINTRAN/ND documentation
- Network captures from real systems (if available)

**Decision**: Use stubs for v1.0, implement in v1.5  
**Impact**: High for SINTRAN support

### Q2.2: X.25 Routing Gateway Multiplexing
**Status**: ❓ Unknown  
**Issue**: Does X.25 routing gateway support multiplexing?

**Options**:
- a) Assume no multiplexing (safe default)
- b) Make configurable
- c) Auto-detect based on gateway response

**Decision**: Assume no for v1.0, make configurable  
**Impact**: Medium - affects connection management

### Q2.3: SINTRAN File Transfer Protocol
**Status**: 📋 No details  
**Issue**: Protocol ID unknown, frame format unknown, commands unknown

**Action**: Document protocol when available, stub in v1.0  
**Decision**: v1.5 implementation  
**Impact**: Low for v1.0

### Q2.4: TAD (Transparent) Protocol Details
**Status**: 📋 Minimal details  
**Issue**: Protocol ID known (0xDD), but usage and data format unclear

**Action**: Research during X25Emulator analysis  
**Decision**: Stub for v1.0  
**Impact**: Medium

### Q2.5: SINTRAN Routing Protocol Details
**Status**: 📋 Minimal details  
**Issue**: Protocol ID known (0xDE), but routing table format and commands unclear

**Action**: Research during X25Emulator analysis  
**Decision**: Stub for v1.0  
**Impact**: Low for v1.0

### Q2.6: X25Emulator Architecture Stability
**Status**: 🔧 Under development  
**Issue**: X25Emulator project still being debugged, architecture not finalized

**Question**: When will architecture be stable enough to integrate learnings?  
**Impact**: High for v1.5 SINTRAN implementation

---

## 3. Architecture & Design

### Q3.1: TDV Emulator Base Class Hierarchy
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Option (b) - TDVEmulatorBase with composition pattern
**Impact**: Implemented successfully

### Q3.2: TDV 2115 Compatibility Mode
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Option (b) - State within emulators
**Impact**: Implemented successfully

### Q3.3: Character Set Architecture
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Per-emulator + TDVCharacterSetManager
**Impact**: Implemented successfully

### Q3.4: ISO 646-NO Configuration Scope
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Option (a) - Configurable per connection
**Impact**: Implemented successfully

### Q3.5: Graphics Rendering Backend
**Status**: 🤔 Design decision needed  
**Issue**: Need graphics for ReGIS, Sixel, Tektronix 4010, (maybe TDV)

**Options**:
- a) SkiaSharp (works in Avalonia and Blazor)
- b) Avalonia's drawing API (desktop only)
- c) Separate backends for each platform

**Decision**: TBD - recommend option (a)  
**Impact**: High - affects graphics support

---

## 4. WebSocket Proxy

### Q4.1: Proxy Protocol Format
**Status**: 🤔 Design decision needed  
**Issue**: WebSocket message format not defined

**Options**:
- a) JSON text messages: `{"type":"connect","protocol":"telnet","host":"...","port":23}`
- b) Binary protocol: `[MsgType:1][Length:4][Payload:N]`
- c) Existing standard (Socket.IO, etc.)

**Decision**: TBD - recommend option (a) for simplicity  
**Impact**: Medium

### Q4.2: Proxy Authentication for v1.0
**Status**: 🤔 Design decision needed  
**Issue**: Proxy authentication/authorization

**Options**:
- a) None (delegated to RetroHub in future)
- b) Simple token-based
- c) Certificate-based

**Decision**: TBD - recommend option (a)  
**Impact**: Low for v1.0 (will be replaced by RetroHub)

---

## 5. Testing Strategy

### Q5.1: Testing Priority for v1.0
**Status**: 🤔 Prioritization needed  
**Issue**: 10+ emulators, 7+ protocols = massive testing scope

**Options**:
- a) Focus on VT100/VT220 (vttest) + 1-2 TDV emulators
- b) Basic smoke tests for all emulators
- c) Full test suite for all (extends timeline)

**Decision**: TBD - recommend option (a)  
**Impact**: High - affects timeline

### Q5.2: Mock Server Strategy
**Status**: 🤔 Implementation approach needed  
**Issue**: Need mock servers for testing

**Options**:
- a) Build all mock servers from scratch
- b) Use vttest + simple telnet echo server
- c) Hybrid: vttest + custom mocks for TDV sequences

**Decision**: TBD - recommend option (c)  
**Impact**: Medium

---

## 6. Font System

### Q6.1: Font Extraction from TDV ROMs
**Status**: ⏳ Pending clarification  
**Issue**: How to get font bitmaps from ROM dumps

**Options**:
- a) User will provide extracted font bitmaps
- b) Build extraction tool to parse ROMs
- c) Use placeholder fonts for v1.0, extract later

**Question**: Can user provide extracted bitmaps, or should we build extraction tool?  
**Impact**: Medium

### Q6.2: Font Rendering Library
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Strategy pattern (IFontRenderer → SystemFontRenderer + BitmapFontRenderer)
**Impact**: Implemented successfully

---

## 7. Scope & Prioritization

### Q7.1: IBM 3270 Priority
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Deferred to v2.0
**Impact**: No 3270 work in v1.0

### Q7.2: Smooth Scrolling Priority
**Status**: 🤔 Prioritization needed  
**Issue**: Smooth scrolling feasible but adds complexity

**Options**:
- a) Implement for v1.0 (Avalonia only, TDV NDSSM mode)
- b) Defer to v1.5 (not critical)

**Decision**: TBD  
**Impact**: Low

### Q7.3: Blazor WASM Timeline
**Status**: ✅ RESOLVED - see Decision Log
**Decision**: Deferred to v2.0
**Impact**: v1.0 is desktop-only

### Q7.4: Top 5 Critical Features for v1.0
**Status**: 🤔 Prioritization needed  
**Issue**: 40+ features identified, which are most critical?

**Candidates**:
- Text selection & copy/paste
- Search in scrollback
- Unicode support
- URL detection
- Session logging
- Color schemes
- SFTP/SCP file transfer
- Zmodem
- Command palette
- Serial port support
- Accessibility
- Tabs/splits
- Font ligatures
- Shell integration
- Triggers

**Question**: Which 5 features are MUST-HAVE for v1.0?  
**Impact**: High - affects scope and timeline

### Q7.5: Blazor Performance Expectations
**Status**: 🤔 Requirements clarification  
**Issue**: Blazor WASM will likely be slower than native

**Options**:
- a) Acceptable to be slower
- b) Must be near-native (requires heavy optimization)
- c) Target only specific emulators (VT100, not graphics)

**Decision**: TBD  
**Impact**: Medium

---

## 8. Configuration & UI

### Q8.1: Configuration UI Complexity
**Status**: 🤔 Design decision needed  
**Issue**: Many complex settings (Server IDs, LAPB, SSH keys, character sets, etc.)

**Options**:
- a) Full UI for all settings from v1.0
- b) Basic connection dialog + manual JSON editing for advanced
- c) Wizard-style connection setup for common scenarios

**Decision**: TBD - recommend option (b) for v1.0, (c) for v1.5  
**Impact**: Medium

---

## 9. Platform & Licensing

### Q9.1: Target Platforms for v1.0
**Status**: 🤔 Clarification needed  
**Issue**: Which platforms to target

**Options**:
- a) Windows, Linux, macOS (all via Avalonia)
- b) Windows only for v1.0
- c) Windows + Linux for v1.0, macOS later

**Question**: User is on Windows - other platforms needed for v1.0?  
**Impact**: Low

### Q9.2: Licensing Constraints
**Status**: 🤔 Clarification needed  
**Issue**: Any licensing restrictions?

**Options**:
- a) MIT/Apache 2.0 only (permissive)
- b) Allow LGPL (e.g., some font/graphics libraries)
- c) Any restrictions?

**Decision**: TBD  
**Impact**: Low (affects library choices)

---

## 10. Timeline & Development Approach

### Q10.1: Timeline Expectations
**Status**: ℹ️ For reference  
**Estimate**:
- v1.0 (full scope): 24+ weeks (~6 months)
- v1.5 (SINTRAN): +5-6 weeks
- v2.0+: +10+ weeks
- **Total**: ~10 months

**Question**: Is this timeline acceptable?  
**Impact**: Planning only

### Q10.2: Development Approach
**Status**: ℹ️ For reference  
**Options**:
- a) Manual implementation
- b) AI-assisted (faster, needs review)
- c) Hybrid (critical parts manual, boilerplate AI-assisted)

**Assumption**: Hybrid approach with user review  
**Impact**: Timeline and quality

---

## Decision Log

### Q3.1: TDV Emulator Base Class Hierarchy
**Decision**: Option (b) - Create TDVEmulatorBase with shared TDV logic
**Rationale**: TDV emulators share protected areas, work areas, LEDs, character sets, rectangle ops. Composition pattern used for features (TDVProtectedAreas, TDVWorkAreas, TDVMessageLEDs, etc.)
**Impact**: Clean code structure, no duplication across TDV1200/2215/2200

### Q3.2: TDV 2115 Compatibility Mode
**Decision**: Option (b) - State within emulators
**Rationale**: 2115 mode is a state flag within TDV1200/TDV2215; toggled via CSI 66 h / ESC Q
**Impact**: Simple implementation, no extra class hierarchy

### Q3.3: Character Set Architecture
**Decision**: Per-emulator + TDVCharacterSetManager
**Rationale**: TDV character sets are fundamentally different from VT. TDVCharacterSetManager handles all 10 TDV sets, with TDVISO646VariantHandler for national variants
**Impact**: Clean separation, each emulator family manages its own character sets

### Q3.4: ISO 646-NO Configuration Scope
**Decision**: Option (a) - Configurable per connection
**Rationale**: Different connections may need different national variants
**Impact**: ISO646 variant passed via connection settings

### Q6.2: Font Rendering Library
**Decision**: Strategy pattern - IFontRenderer with SystemFontRenderer + BitmapFontRenderer
**Rationale**: TDV2200 needs bitmap fonts (ROM-based), VT/TDV1200/2215 use system fonts. Strategy pattern allows each to work independently
**Impact**: TerminalEmulatorFontRendererExtensions maps emulator type to renderer

### Q7.1: IBM 3270 Priority
**Decision**: Deferred to v2.0
**Rationale**: Block-mode architecture is fundamentally different, would delay v1.0 significantly
**Impact**: No 3270 in v1.0

### Q7.3: Blazor WASM Timeline
**Decision**: Deferred to v2.0
**Rationale**: Desktop app needs to be solid first. Blazor adds significant complexity
**Impact**: v1.0 is desktop-only (Avalonia)

### Q5.2: Mock Server Strategy
**Decision**: Option (c) - Hybrid with custom TestServer
**Rationale**: Built TelnetServer library extracted from TestServer, used for both manual validation and automated tests
**Impact**: Comprehensive test infrastructure with 2069 tests

### Q7.2: Smooth Scrolling Priority
**Decision**: Implemented in v1.0
**Rationale**: ScrollingEngine supports both STEP and SMOOTH modes, TDV NDSSM mode works
**Impact**: Done, no timeline impact

---

## Still Open Questions

### Q1.2: TDV Graphics Extension Details
**Status**: Deferred to v2.0 - no specs available

### Q2.x: SINTRAN Protocol Details (Q2.1-Q2.6)
**Status**: All deferred to v1.5+ - stubs only in v1.0

### Q3.5: Graphics Rendering Backend
**Status**: Deferred to v2.0 with graphics support

### Q4.x: WebSocket Proxy (Q4.1-Q4.2)
**Status**: Deferred to v2.0 with Blazor

---

**Last Updated**: 2026-02-15
**Status**: Most architecture questions resolved through implementation

