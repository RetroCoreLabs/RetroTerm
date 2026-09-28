# RetroTerm - Project Status & Roadmap

**Date**: 2026-02-15
**Tests**: 2069 passing, 41 skipped
**Recent focus**: Virtual keyboard UI polish (last ~30 commits)
**Deferred to v2.0**: Blazor, IBM 3270, Graphics (ReGIS/Sixel/Tektronix)

---

## Current State - Phase Status

### Phase 1: Core Foundation - ~90% Done
- Terminal buffer, cell, cursor, attributes, color system (8/16/256/RGB)
- Escape sequence parser (state machine, CSI/OSC/DCS)
- TerminalEmulatorBase with ECMA-48/ISO 6429 support
- Scrolling engine (STEP + SMOOTH), tab stops
- Remaining: Fast-path parser / zero-allocation parsing (perf optimization), G0-G3 char set designation in base class (partial)

### Phase 2: Character-Mode Terminals - ~70% Done
**TDV (done):** TDVEmulatorBase, TDV1200, TDV2215, TDV2200, all utilities, 268 keyboard mappings, virtual keyboard (120+ keys, 12 national layouts), key binding system, TelnetServer library
**VT (not done):** VT52, VT220 (proper), xterm, ANSI emulators. VT100 is partial.
**TDV gaps:** ~10-15 failing comprehensive tests (char set designation, rectangle ops, double height/width, graphics/Tektronix flags)

### Phase 3: Block-Mode & Graphics - 0% (Deferred v2.0)

### Phase 4: Desktop UI - ~55% Done
**Done:** Main window, menu, status bar, renderer, connection dialog, font architecture, virtual keyboard, keyboard reference, log viewer, push keys dialog, SSH host key dialog, dark theme
**Not done:** Settings window, tab management, extended menus, toolbar, saved profiles, terminal type selector, glyph cache, dirty region tracking

### Phase 5: Protocols - ~55% Done
**Done:** Telnet, SSH, TelnetServer library, IConnection/ConnectionFactory/TerminalSession
**Not done:** TN3270 (deferred), Serial, WebSocket (stub), HDLC/LAPB/XOT/SINTRAN (v1.5)

### Phase 6: Advanced Features - ~60% Done
**Done:** Text selection, clipboard, search, key binding system
**Not done:** File transfer, Unicode enhancements, hyperlinks, session logging

---

## Roadmap - Ordered by Priority

### Step 1: Documentation Cleanup (IN PROGRESS)
Update all stale docs to reflect reality.

### Step 2: TDV Emulators to 100%
Make TDV2200, TDV2215, and TDV1200 pass ALL tests. No exceptions.

Key tasks:
1. Identify ALL failing TDV tests
2. Fix character set designation (ESC ( x sequences)
3. Fix rectangle operations (NDSAR, NDFC, NDSREC, NDRREC)
4. Fix double height/width edge cases
5. Fix TAB handling, graphics/Tektronix mode tests
6. Review skipped TDV tests

### Step 3: Desktop UI - Perfect & Bug-Free
Make the desktop app polished, user-friendly, and bug-free.

Sub-tasks:
- 3a. Settings Window (tabbed: General, Terminal, Scroll, CharSets, KeyBindings, Advanced)
- 3b. Tab Management (TabControl, per-tab session)
- 3c. Extended Menu System (Edit, View, Connection, Tools)
- 3d. Saved Connection Profiles (JSON, recent connections)
- 3e. Bug Fixes & Polish
- 3f. Performance (glyph cache, dirty region tracking)

### Step 4: VT220 / xterm Emulators
After UI is solid, add proper VT-series support.

1. Complete VT100 (DEC private modes, special graphics charset, 80/132 col)
2. VT220Emulator (8-bit controls, additional CSI, user-defined keys)
3. XTermEmulator (mouse tracking, OSC, 24-bit color, bracketed paste)
4. Unit tests for each
5. vttest compliance validation

---

## Out of Scope for v1.0

- Blazor Web UI
- WebSocket proxy
- IBM 3270
- ReGIS/Sixel/Tektronix graphics
- SINTRAN/XOT/HDLC/LAPB
- File transfer protocols
- CRT effects / terminal skins
- Split panes
- Serial port support

---

## Key Architecture Decisions Made

| Decision | Choice | Rationale |
|----------|--------|-----------|
| TDV base class | TDVEmulatorBase with composition | Shared logic via components |
| 2115 compat mode | State within emulators | Simple flag, no extra hierarchy |
| Character sets | Per-emulator + TDVCharacterSetManager | TDV sets fundamentally different from VT |
| ISO 646 scope | Configurable per connection | Different hosts need different variants |
| Font rendering | Strategy pattern (IFontRenderer) | Bitmap for TDV2200, system for others |
| IBM 3270 | Deferred v2.0 | Block-mode too different, delays v1.0 |
| Blazor | Deferred v2.0 | Desktop first |
| Keyboard mapper | Unified TDV2200KeyboardMapper | Type aliases for 1200/2215 |
| Key bindings | TDVKeyBindingConfiguration singleton | Any key combo → TDV grid position |

---

## Verification

After each step:
- Run `dotnet test` - all tests must pass (zero regressions)
- Run `dotnet build RetroTerm.sln` - clean build
- For UI changes: manual smoke test (connect to TestServer, verify rendering)
- For emulator changes: verify specific test categories pass
