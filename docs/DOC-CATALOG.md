# RetroTerm Documentation Catalog & Open Work

**Generated:** 2026-07-30
**Purpose:** Single index of every surviving `.md` file, plus the list of work that is genuinely still outstanding — verified against the source tree, not taken on faith from the plan documents.

Every claim marked **verified** below was checked against actual files in `src\`. Claims marked
**unverified** are copied from the source document and have NOT been confirmed.

---

## 1. What was retired

116 files were moved to `garbage\` (untracked — `garbage/` is in `.gitignore`):

| Bucket | Count | What it was |
|--------|-------|-------------|
| `garbage\root\` | 8 | Oct-2025 session debris: `START-HERE.md`, `GOOD-MORNING.md`, `OVERNIGHT-CHECKLIST.md`, `SESSION-RESTART-PROMPT.md`, `PHASE1-QUICK-START.md`, `TEST-PLAN.md`, `retroterm_emulator_plan.md`, `TODO-1-4.md` |
| `garbage\docs\` | 7 | Superseded TDV keyboard references (see §4) |
| `garbage\docs-archive\` | 101 | The entire former `docs\archive\` — completed-phase reports, one-off bug analyses, session summaries |

Nothing was deleted. `spec\` (hardware manuals, OCR'd ND/Tandberg documentation) was **not touched** —
it is irreplaceable primary source material.

---

## 2. Surviving root documents (12)

| File | Role | State |
|------|------|-------|
| `README.md` | Project overview | Current |
| `CLAUDE.md` | Agent/coding rules for this repo | Current. **Stale reference:** points at `retroterm_emulator_plan.md`, now retired (guarded by "if exists") |
| `TODO-PLAN.md` | Master 10-phase roadmap | **Partly stale** — see §3 |
| `FEATURE-STATUS.md` | Component/feature status matrix | **Stale** — see §3 |
| `OPEN-QUESTIONS.md` | Unresolved design/spec questions | Live; most entries still `TBD` |
| `USER-MANUAL.md` | End-user manual | Current-ish, predates recent UI windows |
| `QUICK-START.md` | User quick start | Current-ish |
| `CROSS-PLATFORM-GUIDE.md` | Build/run on Windows/Linux/macOS | Current |
| `DOCUMENTATION-GUIDE.md` | House style for docs | Standard |
| `MERMAID-COLOR-STANDARDS.md` | Diagram colour standard | Standard |
| `LOGO-DESIGN-GUIDE.md` | Branding | Standard |
| `RETRO-FAMILY-UI-DESIGN-SYSTEM.md` | UI design system spec | Standard |

---

## 3. FEATURE-STATUS.md is out of date — verified corrections

`FEATURE-STATUS.md` (last touched 2026-02-15) lists several items as *Not Started* that now exist in the
source tree. **Verified** by file inspection:

| FEATURE-STATUS claim | Reality | Evidence |
|----------------------|---------|----------|
| Settings Window — *Not Started* | **Exists** | `src\RetroTerm.Desktop\Views\PreferencesWindow.axaml` |
| Tab Management — *Not Started* | **Exists** | `TabStrip` panel, `MainWindow.axaml:261`; `TerminalPopoutWindow.axaml` |
| Saved Profiles — *Not Started* | **Exists** | `src\RetroTerm.Desktop\Views\ManageConnectionsWindow.axaml` |
| Protocols: only Telnet + SSH | **Also WebSocket gateway + Kermit** | `ND100-GATEWAY.md`, `src\RetroTerm.Core.Protocols.Kermit\` |
| Test count 2069 passing | Not re-run during this cleanup | **unverified** |

Also not reflected anywhere in `FEATURE-STATUS.md`: structured logging + Protocol Monitor
(`src\RetroTerm.Core\Logging\`, `ProtocolMonitorWindow.axaml`), session data logging
(`FileSessionDataLogger.cs`), terminal bell, OPCOM/ND-100 gateway debug windows,
ISO 646 national variants in display + keyboard.

**Recommendation:** rewrite `FEATURE-STATUS.md` from the source tree before trusting it again.

---

## 4. Retired keyboard docs and why

All seven were superseded by `docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md` (2026-02-06) or by
`docs\TDV-KEYBOARD-HARDWARE-VALIDATION.md` (2026-07-09):

| Retired file | Reason |
|--------------|--------|
| `TDV-COMPLETE-KEY-REFERENCE.md` | Its 2115-C0 + key-category tables are reproduced in COMPLETE-REFERENCE |
| `TDV-KEY-MAPPING-REFERENCE.md` | Thinner near-duplicate of the above |
| `TDV-KEY-IMPLEMENTATION-STATUS.md` | **Factually wrong** — headline "Query/Response Not Wired ⚠️ CRITICAL"; that has worked since Nov 2025 |
| `TDV-SPECIAL-KEYS-REFERENCE.md` | Spec extracts now folded into COMPLETE-REFERENCE + HARDWARE-VALIDATION |
| `TDV-APPLICATION-KEYS-REFERENCE.md` | HELP/DO/FUNC/COPY tables live in COMPLETE-REFERENCE |
| `TDV2200-KEYBOARD-MAPPING.md` | **Actively misleading** — documents the Alt-combination strategy that was deleted and replaced by `TDVKeyBindingConfiguration` (arbitrary key combo → grid position) |
| `TDV2200-KEYBOARD-SPEC-ANALYSIS.md` | Single settled question ("what do the manuals say about arrow keys?") |

---

## 5. Surviving `docs\` files (25)

### 5.1 Live reference — keep

| File | Content |
|------|---------|
| `TDV2200 TERMCAP REFERENCE.md` | 165 KB termcap/terminfo reference for TDV2200 |
| `TDV-COMPREHENSIVE-REFERENCE.md` | Whole-terminal reference across TDV1200/2215/2200 |
| `TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md` | Escape-sequence tables per model |
| `TDV-QUERY-COMMANDS.md` | DA/DSR/terminal-ID query reference |
| `TDV-KEYBOARD-COMPLETE-REFERENCE.md` | Consolidated keyboard reference (VK codes + sequences) |
| `KBD-ND-246.md` | ND-246 physical layout + 13 national variants, from the OCR'd User's Guide |
| `ND246-KEYBOARD-IMPLEMENTATION.md` | Implementation notes; `ND246KeyboardMapper.cs` **verified to exist** (thin wrapper over `TDV2200KeyRegistry`) |
| `tdv-246-no-image.md` | Physical keyboard photo analysis; referenced by the above |
| `ND100-GATEWAY.md` | ND-100 emulator WebSocket gateway protocol |
| `ND110-OPCOM-MICROCODE-REFERENCE.md` | ND-110 OPCOM microcode (57 KB) |
| `OPCOM-COMMAND-REFERENCE.md` | OPCOM commands from the SINTRAN manual |
| `KERMIT-MANUAL-TEST-PLAN.md` | Manual test procedure; Kermit itself is **implemented** (`src\RetroTerm.Core.Protocols.Kermit\`) — keep as a reusable test script |
| `TDV-DLE-CURSOR-BUG-FIX.md` | Bug-fix record for the DLE cursor-addressing defect (2026-07-22) |
| `ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md` | Deep architecture review of the whole emulation stack (parser/emulators/buffer/renderer/input/graphics readiness) ahead of the multi-terminal expansion. All findings carry `file:line` refs; includes 10 verified live bugs, target architecture (engine + profiles + handler modules, graphics planes), 7-phase migration plan, emulator addition order |

### 5.2 Open work — keep, action required

| File | Status |
|------|--------|
| `TDV-PRINTER-SUPPORT-DESIGN.md` | **Not implemented.** Verified: no `*Printer*.cs` anywhere in `src\`. Design proposal only. |
| `TDV-KEYBOARD-HARDWARE-VALIDATION.md` | **Open.** No physical ND-246 / TDV-2200/9 available for wire capture; lists byte sequences that must be confirmed before being treated as authoritative. |
| `PROJECT-ANALYSIS-AND-NEXT-STEPS.md` | Roadmap snapshot (2026-02-15); overlaps `TODO-PLAN.md` + `FEATURE-STATUS.md` — candidate for merge |

### 5.3 Completed plans — candidates for retirement (NOT yet moved)

Each was checked against the source tree and found already delivered:

| File | Verification |
|------|--------------|
| `ARCHITECTURE-ANALYSIS.md` | Proposed refactor is **done** — `TDV1200Emulator`, `TDV2215Emulator`, `TDV2200Emulator` all derive from `TDVEmulatorBase` (the doc's whole premise was that 1200/2215 wrongly derived from 2200) |
| `FUTURE-FEATURES-PLAN.md` | All three phases shipped: 4a scrollback wheel (`TerminalCanvas.axaml.cs:97 OnPointerWheelChanged`), 4b session logging (`FileSessionDataLogger.cs`), 4c Kermit (`RetroTerm.Core.Protocols.Kermit`) |
| `LOGGING-REDESIGN-PLAN.md` | Shipped in commit `5a4ee6a` — `Logging\LogEntry.cs`, `ApplicationLogger.cs`, `ProtocolMonitorWindow.axaml` |
| `COMPREHENSIVE-PROJECT-PLAN.md` | Oct-2025 plan, superseded by `TODO-PLAN.md` |
| `DESIGN-SYSTEM-IMPLEMENTATION.md` | Completion report for `RETRO-FAMILY-UI-DESIGN-SYSTEM.md` |
| `TDV-FEATURE-ANALYSIS.md` | Nov-2025 status snapshot, superseded by `FEATURE-STATUS.md` |
| `TDV-IMPLEMENTATION-COMPLETE-SUMMARY.md` | Oct-2025 session completion summary |
| `TDV-IMPLEMENTATION-REFERENCE.md` | Superseded by `TDV-COMPREHENSIVE-REFERENCE.md` (newer, broader) |

---

## 6. Other markdown outside root/docs (not touched)

| Location | Count | Note |
|----------|-------|------|
| `spec\` | ~45 | Primary hardware documentation: ECMA-48, TDV1200/2115/2200/2215 manuals, OCR'd ND guides, keyboard specs, Tektronix analyses. **Do not retire.** |
| `tests\RetroTerm.Tests\Avalonia\images\` | 2 | Screenshot-documentation for TDV2200 / TDV2215 render tests |

---

## 7. Consolidated open work

Compiled from the surviving live documents plus source-tree verification.

### 7.1 Not implemented — verified absent from `src\`

1. **TDV 2200 printer / auxiliary-port support** — design complete, zero code.
   See `docs\TDV-PRINTER-SUPPORT-DESIGN.md`.
2. **VT emulator family** — `FEATURE-STATUS.md` reports VT52, VT220, xterm, ANSI as Not Started;
   VT100 as partial (missing DEC private modes). *(unverified — not re-checked in this pass.)*
3. **Serial protocol** — listed Not Started.
4. **IBM 3270 / TN3270**, **graphics (ReGIS / Sixel / Tektronix)**, **Blazor Web UI** — deliberately
   deferred to v2.0.
5. **Rendering performance work** — glyph cache and dirty-region tracking, both listed Not Started.

### 7.2 Known defects (from `FEATURE-STATUS.md`, *unverified* in this pass)

1. Scroll-region bug — cursor positioning outside the scroll region misbehaves; 4 tests skipped.
2. ~10–15 comprehensive test failures — character-set designation, rectangle ops,
   double-height/width edge cases.
3. Line-drawing cursor position — manual test 5 fails.
4. 41 skipped tests overall (scroll region 4, Avalonia context ~6, TCP timing ~8, other ~23).

### 7.3 Unresolved questions (`OPEN-QUESTIONS.md`)

Still marked TBD / speculative:

- **Q1.1** TDV2200/9 functional spec — no equivalent to TDV1200's ND-12054-1-EN; reverse-engineered
  from ROM dumps.
- **Q1.2** TDV graphics-extension board protocol — no documentation found.
- **Q2.1** SINTRAN packet format — only `0x21 0x13 [protocol]` known; server/PAD ID placement,
  sequence numbers and flow control unknown.
- **Q2.2** X.25 gateway multiplexing — assumed absent, to be made configurable.
- **Q2.3** SINTRAN file-transfer protocol — protocol ID, frame format, commands all unknown.
- **Q2.4** TAD transparent protocol (`0xDD`) — usage and data format unclear.
- **Q2.5** SINTRAN routing protocol (`0xDE`) — routing-table format and commands unclear.

### 7.4 Hardware validation blocked

`docs\TDV-KEYBOARD-HARDWARE-VALIDATION.md` — no physical ND-246 / TDV-2200/9 for wire capture.
Editing keys (E13, E14, G47 STRYK, B47 ←, PC Backspace/Delete) and Ctrl-modifier behaviour have
spec conflicts that cannot be settled without hardware.

### 7.5 Documentation debt created by this cleanup

1. Rewrite `FEATURE-STATUS.md` from the source tree (§3).
2. Decide on the eight completed plans in §5.3.
3. `CLAUDE.md` references `retroterm_emulator_plan.md`, now in `garbage\root\`.
4. Consider merging `PROJECT-ANALYSIS-AND-NEXT-STEPS.md` into `TODO-PLAN.md`.
