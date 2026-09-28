# RetroTerm Architecture Review — Multi-Terminal Emulation and Rendering

**Date:** 2026-08-08
**Scope:** Whole emulation stack — parser, emulators, screen model, cells, character sets, colors, scrolling, renderer, input, host responses, modes, testing, performance, graphics readiness.
**Method:** Direct source inspection. Every claim carries a `file:line` reference. The critical claims (parser C1 handling, DCS handling, the `DataToSend` wiring) were verified twice — once during the sweep and once by hand. Nothing in this document is guessed; where something could not be verified it is marked **[NOT VERIFIED]**.

**The main question answered up front:** the current architecture is NOT the right long-term foundation for 10–15 terminal profiles **as it stands**, but it does **not** need a rewrite. The outer layering (Core → Protocols → UI, the session pump, the headless test approach) is correct and worth keeping. What breaks at scale is the *inside* of each layer: a parser with real defects that block every graphics protocol, a base emulator class that secretly *is* the VT100, mode state as loose bools, screen operations duplicated per emulator, a renderer with no caching and TDV type-checks inside it, and two host-response channels of which the VT one is dead. Section C proposes the target architecture; Section F shows how to get there incrementally.

---

## A. Current Architecture — what RetroTerm actually does today

### A.1 The actual data flow (verified, not the idealized picture)

```text
IConnection (Telnet/SSH/Serial/Memory)          src\RetroTerm.Core.Protocols.Net\
        │ DataReceived (network thread)
        ▼
TerminalSession.OnConnectionDataReceived        TerminalSession.cs:436
        │ _pump.PostData (ArrayPool copy)
        ▼
SessionPump (single mutator thread)             SessionPump.cs:19-30
        │ ProcessReceivedData                   TerminalSession.cs:451-498
        │  ├─ DataLogger / MCP observers / OPCOM / file transfer / Kermit scan
        ▼
Emulator.ProcessData
        │  VT path: straight to parser          TerminalEmulatorBase.cs:175
        │  TDV path: TDVInputProcessor FIRST    TDVInputProcessor.cs:31-81
        │    (strips DLE coords, ESC % variant,
        │     2115 C0 codes — BEFORE the parser,
        │     then feeds parser ONE BYTE AT A TIME :79)
        ▼
EscapeSequenceParser (hand-written switch)      EscapeSequenceParser.cs
        │ events: OnCharacter/OnExecute/OnEscapeDispatch/
        │         OnCsiDispatch/OnOscDispatch/(OnDcsHook — dead)
        ▼
TerminalEmulatorBase.HandleCsiSequence          TerminalEmulatorBase.cs:456
        │ virtual-override chain: leaf → TDVEmulatorBase → base,
        │ each a switch(final)
        ▼
TerminalBuffer (TerminalCell[,])                TerminalBuffer.cs
        │ Invalidated event (full screen, no payload)
        ▼
TerminalCanvas → Dispatcher.Post(InvalidateVisual)   TerminalCanvas.axaml.cs:302-306
        ▼
TerminalRenderer.Render — FULL redraw, every cell,   TerminalRenderer.cs:165-299
immediate mode into Avalonia DrawingContext, reading
the live buffer from the UI thread (data race — see B.9)
```

Host responses go the other way through **two different channels** (see A.7).

### A.2 Component inventory

| Component | File | Lines | Terminal-specific content? |
|---|---|---:|---|
| Parser | `src\RetroTerm.Core\Terminal\Parsing\EscapeSequenceParser.cs` | 569 | Almost none — one TDV hack (final bytes `< = >` accepted, :461, :407) |
| Parser state enum | `Parsing\ParserState.cs` | 92 | 18 states declared, only ~9 reachable |
| `ParserAction.cs` | `Parsing\ParserAction.cs` | 77 | **Entirely unreferenced** — a table-driven design that was never built |
| Base emulator | `Emulators\TerminalEmulatorBase.cs` | 1082 | Yes — it IS the VT100 implementation |
| VT100Emulator | `Emulators\VT100Emulator.cs` | **20** | Overrides only `ToString()` |
| TDV base | `Emulators\TDV\TDVEmulatorBase.cs` | 1326 | TDV/ND CSI, charsets, LEDs, rectangles |
| TDV1200 / 2215 / 2200 | `Emulators\TDV\` | 467 / 748 / 1311 | Per-model dispatch + heavy duplication |
| TDV components | `Emulators\TDV\Components\` + support | ~4800 total | Mixed: some clean state-only, some emulator-back-reference |
| Buffer | `Buffer\TerminalBuffer.cs` | — | Generic; `TerminalCell[,]` grid, jagged scrollback list |
| Cell | `Buffer\TerminalCell.cs` | — | Generic; 20 bytes (doc comment says 16 — wrong, :9) |
| Cursor | `Terminal\Cursor.cs` | — | Generic but knows nothing about scroll margins |
| Session | `Session\TerminalSession.cs` + `SessionPump.cs` | — | Clean coordinator; downcasts to `TDVEmulatorBase` at :199 |
| Renderer | `Desktop\Rendering\TerminalRenderer.cs` | — | **Contains TDV type-checks** (:332-349) despite comments claiming otherwise (:42-43, :56) |
| Font renderers | `Desktop\Rendering\SystemFontRenderer.cs`, `BitmapFontRenderer.cs` | — | Bitmap renderer contains a duplicate ISO646 remap table (:50-80) |
| Keyboard mappers | `Core\Terminal\Input\KeyboardMapper.cs` | 432 | VT100/VT220/TDV mappers + string-keyed factory |
| Fonts | `Core\Fonts\FontTDV2200.cs` | 18514 | Glyph ROM tables; national remap inside the ROM |

### A.3 The parser (syntax layer)

One generic hand-written state machine. Genuinely zero-alloc where it counts: fixed `int[32]` params, `byte[2]` intermediates, spans exposed over them (`EscapeSequenceParser.cs:115,120`), OSC via `ArrayPool` (:503-513). The parser knows almost nothing about specific terminals. **But it has five structural defects that block nearly every terminal on the roadmap** — see B.1.

Terminal-specific *byte-level* handling does not live in the parser — it lives **in front of it**: `TDVInputProcessor.cs:31-81` strips DLE coordinate bytes and 2115 C0 codes before the parser ever sees them. So today "syntax vs semantics" is split three ways: pre-parser byte filters (TDV), the parser (generic), and the emulators (semantics).

### A.4 The emulator layer (semantics)

`TerminalEmulatorBase` implements the ECMA-48/VT100 core: CSI finals `A B C D E F G H f J K L M P X d m n r s u` (`TerminalEmulatorBase.cs:456-576`), DEC private modes 1/6/7/12/25/47/1047/1048/1049 (:578-627 — 47/1047/1049 are **TODO no-ops**), SGR incl. 256-color and RGB (:629-782), DECSTBM (:549), DECSC/DECRC (:390-396).

`VT100Emulator` is 20 lines. The factory maps `"VT220"` to a `VT100Emulator` instance (`Configuration\EmulatorFactory.cs:35`). So today there is exactly **one** VT implementation, and it lives in the base class every other terminal must inherit from.

Modes are seven loose `protected bool` fields (:42-53) plus more bools scattered across `TDVEmulatorBase.cs:26-28`, `TDV2200Emulator.cs:44-47`, components, and two duplicated onto `Cursor` (`Cursor.cs:41,47`). There is no central mode object and no capability description anywhere.

TDV uses two different composition styles at once:
- **Clean:** state-only components owned by `TDVEmulatorBase` (:39-51) that receive the buffer per call — `TDVRectangleOperations.SetAttributeInRectangle(Buffer, ...)` (`TDVRectangleOperations.cs:22`). Reusable shape.
- **Messy:** `Components\` classes owned by each **leaf** emulator, holding a back-reference and mutating `_emulator.Cursor` / `_emulator.Buffer` directly (`TDV2115CompatibilityHandler.cs:17,198,216`). The wiring is copy-pasted into all three leaves, along with `CharacterSetVariant`, `GetISO646LanguageCode`, `KeyboardLights` and `ProcessInput` (byte-identical in `TDV1200Emulator.cs:329-464`, `TDV2215Emulator.cs:436-745`, `TDV2200Emulator.cs:519-1132`).

### A.5 Screen model

`TerminalBuffer` owns: clear/clear-line/clear-partial (:134-198), region scroll up/down (:213-279), insert/delete line (:284-307 — with a region bug, B.5), resize (:320-360), scrollback (`List<TerminalCell[]>`, viewport read :371-414), alternate screen (:430-469 — fully implemented, **unreachable from any escape sequence**).

Missing entirely: line objects (no wrap flag, no per-line double-width/height), tab stops (`HandleTab` is hardcoded mod-8, `TerminalEmulatorBase.cs:883-888`; HTS is a TODO :386-388; TBC absent), dirty tracking (grep "Dirty" in src → only a settings form), margin awareness in `Cursor` (clamps to physical screen only, `Cursor.cs:18-31`; margin logic re-derived in three places that disagree — B.5).

`TerminalCell` is 18 payload bytes padded to 20: `uint Codepoint` + `ushort CharacterAttributes` + `byte CharacterSet` + `byte _flags` + two 5-byte `TerminalColor` (`TerminalCell.cs:11-49`, `TerminalColor.cs:13-45`). `TerminalColor` is already a proper Default/Indexed/RGB union — 256-color and 24-bit are live through SGR 38/48 (`TerminalEmulatorBase.cs:751-782`). Double-width/height is stored **twice** (cell flags bits 0-1 AND attribute bits 9-11) and rendered **zero** times.

### A.6 Character sets — three pipelines, none complete

- **VT path:** bytes mapped to Unicode inside `HandleCharacter` via `MapDecSpecialGraphics` (`TerminalEmulatorBase.cs:267-313`) — only `ESC ( ) * +` with `A`/`B`/`0`, SO/SI locking shifts, **no SS2/SS3, no NRCS**.
- **TDV path:** deliberately bypasses that (`TDVEmulatorBase.cs:514-519` passes codepoints through), writes the raw byte plus a `FontNumber` side-channel into the cell (`TDV2200Emulator.cs:336-356`), and defers byte→glyph resolution to the **Desktop renderer's font ROM** (`BitmapFontRenderer.cs:48`, `FontTDV2200.cs:18416-18454`). National ISO646 variants are remapped inside the ROM, driven by a renderer-side type-check (`TerminalRenderer.SyncFontVariant :332-349`).
- **Dead path:** the nine glyph dictionaries in `TDVCharacterSets.cs:81-437` have no live consumer — `TDVInputProcessor.cs:58-70` explicitly stopped calling them (double-mapping produced blank cells).

And underneath all of it, the parser does **unconditional UTF-8 decoding** of every byte ≥0x80 (`EscapeSequenceParser.cs:178`) on what is, for TDV/ND, an 8-bit national-charset stream. The TDV path only survives because the pre-parser filter strips the binary bytes first.

**There is no single `incoming byte → active charset → glyph → display` stage anywhere.**

### A.7 Host responses — two channels, one dead

- **Channel A (VT, dead):** `TerminalEmulatorBase.SendResponse` → `DataToSend` event (:1020-1023, :133). **No subscriber exists anywhere in src** (verified by grep — the only `DataToSend` subscriptions in the repo belong to the unrelated Kermit engine). Consequence: a VT100 session generates DSR/CPR replies (:864-877) and drops them on the floor. The base also **never answers Primary DA at all** — `c` is not in its CSI switch.
- **Channel B (TDV, works):** `TDVEmulatorBase.SendResponse(string)` → `OnResponseReady` (:1258-1266) → `TerminalSession.OnTDVResponseReady` (:218-248) → connection. String-typed, UTF-8 encoded at the session (`TerminalSession.cs:229`) — fine for ASCII responses, wrong the day a response carries a byte >0x7F.

### A.8 Input

Flow: Avalonia KeyDown → `AvaloniaKeyHelper` VK conversion → `IKeyboardMapper.MapKey(vk, modifiers, TerminalModes)` → `InputReceived` string → session → connection. Plain printable characters bypass the mapper entirely via TextInput (`TerminalCanvas.axaml.cs:683-699`) — deliberate and correct for international keyboards.

Defects: the mapper factory is a closed string switch on the emulator type name that silently falls back to VT100 (`KeyboardMapper.cs:412-432`); DECCKM/DECKPAM never reach the mapper because `GetTerminalModes()` only sets TDV bits (`TerminalCanvas.axaml.cs:653-681`) and the emulator's `ApplicationCursorKeys` field has no public accessor; a C0 fallback table is hardcoded in the UI **twice** (`TerminalCanvas.axaml.cs:451-530` and duplicated verbatim in `VirtualKeyboardWindow.axaml.cs:102-178`); ISO646 output conversion lives in Desktop, twice (:626-650 and `VirtualKeyboardWindow.axaml.cs:200-218`). Mouse reporting to host: **does not exist at all** (no DECSET 1000-family handling, no state, nothing).

### A.9 Renderer

Immediate-mode full-screen redraw on every invalidation, no dirty regions, no glyph cache. `SystemFontRenderer` builds a `Typeface` + `FormattedText` + geometry **per glyph per frame** (`SystemFontRenderer.cs:54-66`). `BitmapFontRenderer` issues one `FillRectangle` **per lit pixel** (`BitmapFontRenderer.cs:100-118`) — up to ~215k draw calls/frame on a full TDV2200 screen, doubled for bold (`TerminalRenderer.cs:359-366`). Blink is stored but never rendered. DW/DH stored but never rendered. Cursor blink timer lives in the renderer, ticks on a threadpool thread, **never triggers a repaint**, and is leaked on every emulator swap because `Dispose` is called from nowhere (`TerminalRenderer.cs:33,65-67,499-503`; `TerminalCanvas.SetEmulator:240`).

Color: green phosphor `#00FF88`/`#001911` is baked into the renderer constructor (:120-122) and into the canvas letterbox + XAML background (`TerminalCanvas.axaml.cs:42-43`), UI presets only swap the two default brushes (`TerminalRenderer.SetDefaultColors:129-133`). There are **three** disagreeing 256-color tables: renderer palette (:73-123, cube `*40+55`), `TerminalColor.IndexToRgb` (`TerminalColor.cs:114-159`, cube `*51` — different results for indices 16-231), and the entirely dead `ColorScheme.cs` system (`Buffer\ColorScheme.cs` — nothing references it).

There is **no resize path in use**: `TerminalEmulatorBase.Resize` (:1028-1042) has zero callers in src; the picture is scaled/letterboxed instead (`TerminalCanvas.Render:343-352`).

### A.10 Graphics

Nothing exists but flags: `_isTektronixMode` / `_hasGraphicsExtension` bools on TDV2200 (:46-47), toggled by `ESC[n>` (:913-931), surfaced only as "+TEKTRONIX"/"+GRAPHICS" strings in DA replies (:1170-1193). Four empty TODO methods (:1139-1159). No plane, no pixel, no vector state anywhere in the buffer/renderer/cell model. Good primary-source documentation exists: `spec\Tektronix\nd-graphic-terminal-analysis.md` (762 lines, ND protocol reverse-engineered) and `spec\Tektronix\retroterm-nd-graphics-plan.md` (296 lines, phase-1 plan — including the confirmed `ESC "` mis-parse diagnosis at :62-64).

### A.11 Testing (the strongest part of the codebase)

~1,900 declared tests; only 32 (≈1.7%) touch Avalonia, all serialized in one collection. The dominant pattern is exactly right: construct emulator → feed bytes → assert buffer/cursor/response (`ScrollingRegionBufferValidationTests.cs:22-55`, `TDVQueryResponseTests.cs:17-31`). The parser is tested in isolation (`Terminal\Parsing\EscapeSequenceParserTests.cs`). TDV2200 validation runs against a parsed hardware-manual spec database (`TDV2200ValidationTestBase.cs:24-71`). Gaps: the real `TerminalRenderer.Render` path is never pixel-tested (screenshot tests re-implement its logic, `TDV2200ScreenshotTests.cs:57-60`), session tests use `Thread.Sleep(200)` (race-prone; source of most of the 41 skips), and the whole test csproj is pinned `net9.0-windows` even though only the Desktop tests need Windows.

---

## B. Problems

Classification: **Critical** = blocks the roadmap or is a live correctness bug. **Important** = will multiply cost per new terminal. **Minor** = cleanup. **Already good** = keep.

### B.1 — CRITICAL — Parser defects (`EscapeSequenceParser.cs`)

| # | Defect | Evidence | Which future terminals it blocks |
|---|---|---|---|
| 1 | Global ESC pre-emption: any 0x1B in any state resets to Escape (:155-167). **No string sequence can be terminated by `ESC \` (ST)** and no DCS/OSC payload may contain ESC. Hosts overwhelmingly terminate DCS with `ESC \`. | Verified by hand | VT240/VT340 (ReGIS, Sixel), xterm OSC, any DCS use |
| 2 | 8-bit C1 controls dead: `b >= 0x80` → UTF-8 (:178) is checked **before** `b == 0x9B` (:190), so the C1-CSI branch is unreachable; 0x84/0x85/0x8D/0x90/0x9C/0x9D become U+FFFD. | Verified by hand | VT220+ (8-bit mode is a VT220 feature), any 8-bit host |
| 3 | DCS is a stub: parameters are discarded (digit branch :527-531 never touches `_params`), there is no case for `DcsParam` → the parser **wedges** on `DCS 0;1q`, payload bytes are dropped (:546 is a comment), `OnDcsPut` is never raised (:79-81), `OnDcsHook/Unhook` have zero subscribers in the repo. | Verified by hand | Sixel, ReGIS, VT220 DECUDK, TDV PUSH/PROGRAM download |
| 4 | No sub-parameters: `:` (0x3A) kills the sequence (:479 default → Ground). | Agent report | xterm `SGR 38:2::R:G:B`, modern SGR forms |
| 5 | No alternate ground / byte-mode concept: VT52 `ESC Y <r><c>` binary params, Tektronix vector bytes after GS, ND DLE coordinates — none can be tokenized. TDV solved this by filtering bytes **before** the parser (`TDVInputProcessor.cs:41-52`), which is why the parser can't see modes and modes can't reach the parser. | Agent report + spec doc | VT52, Tek 4010/4014, ND graphics |
| 6 | `ESC "` mis-parse: `"` collected as intermediate, first digit becomes the final (:317-332). Already diagnosed in `spec\Tektronix\retroterm-nd-graphics-plan.md:62-64`. | Spec doc + parser code | ND/TDV graphics — the user's #1 target |

Also: the TDV `< = >` final-byte hack (:461, :407) leaks one terminal's syntax into the shared parser — exactly the pattern the review was asked to prevent.

### B.2 — CRITICAL — VT host responses never sent (`TerminalEmulatorBase.cs:1020`, `TerminalSession.cs`)

`DataToSend` has no subscriber (verified). Primary DA is not implemented in the base at all. Any host that probes a VT100/"VT220" RetroTerm session (which is most full-screen software) gets silence. Every future VT/xterm profile needs this channel; it must be wired once in `TerminalSession` beside `WireTDVQueryResponse` (:197).

### B.3 — CRITICAL — One class is both engine and terminal (`TerminalEmulatorBase.cs`, `VT100Emulator.cs`, `EmulatorFactory.cs:35`)

The base class is the VT100; VT100Emulator is empty; "VT220" is a VT100. With 10-15 profiles this forces every terminal to inherit VT100 behavior and override it away piecemeal — the TDV tree already shows the end state: triplicated wiring, forked `HandleCharacter` with **divergent scroll behavior at the region bottom** (`TDV2215Emulator.cs:612` vs `TerminalEmulatorBase.cs:254` — 2215's condition can never be true, so 2215 loses region-bottom scrolling; live bug), and per-leaf switch chains. The fix is engine + profile composition (Section C), not more subclasses.

### B.4 — CRITICAL — No graphics plane, and the cell grid cannot express one (A.10)

Not a defect today, but every graphics target (ND, Tek, ReGIS, Sixel) needs a plane/compositor model that does not exist, and bolting pixels into `TerminalCell[,]` would be wrong. Must be designed before, not after, the first graphics emulator.

### B.5 — IMPORTANT — Screen-operation duplication and margin logic in three places

- `InsertLines`/`DeleteLines` ignore the scroll-region bottom (`TerminalBuffer.cs:284-307` always uses `Height-1`) — TDV2200 re-implemented them against the work area instead (`TDV2200Emulator.cs:806-843`).
- NDICHE/NDDCHE re-implement `ShiftCharactersRight`/`DeleteCharacters` line for line (`TDV2200Emulator.cs:850-899` vs `TerminalEmulatorBase.cs:986-1010`), with a guard difference that is a live off-by-one risk.
- A whole second rectangle-operation family inside `TDVEmulatorBase` (~200 lines, :802-1008) is **unreachable** and uses the opposite coordinate convention (1-indexed) from the live one (0-indexed, :253-320).
- Margin/origin logic is derived independently in `HandleLineFeed` (:890), `HandleReverseLineFeed` (:904), the wrap fixup (:249-258), and origin translation is applied to CUP only, without a bottom clamp (:506-509) — VPA and CUU/CUD ignore origin mode.
- Cursor save/restore stores row/col/style only (`Cursor.cs:231-244`) — DECSC requires attributes, charset, origin mode.

Every new DEC terminal (VT220's DECSEL/DECSED, VT420's rectangle ops) multiplies this.

### B.6 — IMPORTANT — No central mode/capability representation (A.4)

Loose bools across five classes, an inconsistent numeric mode-query map (`GetModeState` overridden three times, mode 1 means DECCKM on 2200 but "extended mode" on 2215 — `TDV2200Emulator.cs:1027-1038` vs `TDV2215Emulator.cs:696-704`), missing SM/RM entirely (IRM/LNM cannot be set by the host), DECCOLM absent, DECSET 47/1047/1049 TODO. xterm alone adds dozens of private modes; without a mode table this becomes unmanageable.

### B.7 — IMPORTANT — Character-set pipeline split across three layers (A.6)

VT maps in the emulator; TDV maps in the Desktop font ROM driven by renderer type-checks; parser force-decodes UTF-8 underneath both. VT220 NRCS, G2/G3, SS2/SS3 (implemented twice, incompatibly — `TDVCharacterSetManager` vs 2215's private fields; TDV1200 not at all), and downloadable character sets all need the one pipeline that is missing: `byte → active charset → glyph id → display mapping`, in Core.

### B.8 — IMPORTANT — Renderer performance and coupling (A.9)

Per-glyph FormattedText, per-pixel FillRectangle, per-frame HashSet + brush allocations inside the cell loop (`TerminalRenderer.cs:179,255-267,432-466`), no dirty tracking, no glyph cache, invalidation amplification (per-TDV-command `Invalidated`, per-character on the 2215 path — `TDV2215Emulator.cs:618`). Plus TDV type-checks inside the renderer (:332-349) and the ISO646 table duplicated in the font renderer (`BitmapFontRenderer.cs:50-80`). Graphics planes will multiply frame cost; this must be fixed before them.

### B.9 — IMPORTANT — Renderer reads the live buffer from the UI thread

The buffer's contract is single-writer-on-the-pump (`ScreenReader.cs:11-14`), and MCP/scripts honor it via `RunOnSessionThreadAsync`. The renderer does not: `TerminalCanvas.Render` walks the mutable `TerminalCell[,]` while the pump mutates it. 20-byte structs tear; a concurrent `Resize` array swap can throw from the indexer (`TerminalBuffer.cs:357` + `TerminalRenderer.cs:217`). Needs a snapshot/double-buffer handoff.

### B.10 — IMPORTANT — Input gaps (A.8)

Closed string-keyed mapper factory with silent VT100 fallback; DECCKM/DECKPAM not plumbed (`ApplicationKeypad` mapper branch is an empty stub, `KeyboardMapper.cs:195-199`); C0 fallback tables duplicated in two UI classes; ISO646 conversion in Desktop, twice; no mouse reporting infrastructure (xterm needs it; GIN needs pointer→host too).

### B.11 — MINOR — Dead code and stale artifacts (delete list)

- Dead second escape parser inside TDV2200 (~250 lines: `TDV2200Emulator.cs:95-457` family, `_escapeSequence`/`_isCsiMode` :42-43) + `TDVDCSHandler.cs` (160 lines, reachable only from it).
- `TDVDCSHandlerFeature.cs` (172 lines) — caller chain starts at a method nothing calls (`TDV2215Emulator.cs:227-233`).
- Unreachable rectangle family (`TDVEmulatorBase.cs:802-1008`) and never-called `HandleRectangleOperation` (:223-247).
- `ParserAction.cs`, `_paramBuffer` (never read, `EscapeSequenceParser.cs:91`), unreachable `ParserState` members.
- Dead color-scheme system (`Buffer\ColorScheme.cs`, ~256 lines), dead `ScrollingEngine.cs`, dead `Desktop\Fonts\BitmapFont.cs`/`SystemFont.cs`, four `.phase4.backup` files, duplicate root-level `EscapeSequenceParserTests.cs`.
- Inert protected-area machinery: `CharacterAttributes.Protected` never set/tested; `TDVProtectedAreas` has no production writers and goes stale on resize (`TDVProtectedAreas.cs:16-21`).
- `TDVEmulatorBase.MaxScrollback => 2000` (:63) doesn't do anything — ctor still passes 10000.
- TDV1200 2115-mode swallows cursor movement and erase (`TDV2115CompatibilityMode.cs:124-150` returns true, does nothing) — live bug, fix or remove.
- Unguarded per-escape logging allocation (`TDVEmulatorBase.cs:389-391` and :355-357, :371-373, :396-404) — violates the project's own logging rule (`ApplicationLogger.cs:22-26`).

### B.12 — Already good (keep, build on)

- **Layering:** Core (netstandard2.1, no UI deps) / Protocols / Desktop, `IConnection` abstraction, `ConnectionFactory`.
- **SessionPump** single-mutator threading model with ArrayPool buffers and backpressure (`SessionPump.cs`).
- **Parser skeleton:** zero-alloc param/intermediate storage, span-based event surface — worth fixing, not replacing.
- **`TerminalColor`** union (Default/Indexed/RGB) — already covers mono → 24-bit; the *logical vs presentation* split the review asked for half-exists: cells store logical color, the renderer owns brushes. It just needs one canonical palette and themed presentation.
- **Headless test pattern** (bytes in → buffer/cursor/response out) and the TDV spec-database validation suite.
- **`ref TerminalCell` indexer** + struct cells — the right hot-path shape.
- The **state-only TDV component style** (`TDVRectangleOperations` receiving the buffer per call) — the seed of the module pattern Section C proposes.
- `EscapeSequenceFormatter` / `ScriptStringEscapes` — one escape notation everywhere.

---

## C. Proposed Target Architecture

Derived from what exists; every box maps to code that is either already there or has one clear landing site.

```text
                    ┌─────────────────────┐
                    │   IConnection       │  (unchanged)
                    └─────────┬───────────┘
                              │ bytes (network thread)
                              ▼
                    ┌─────────────────────┐
                    │   SessionPump       │  (unchanged — single mutator)
                    └─────────┬───────────┘
                              ▼
              ┌───────────────────────────────────┐
              │  ByteStreamParser (fixed, still    │
              │  ONE class, still zero-alloc)      │
              │  - C1 8-bit fixed, sub-params,     │
              │    DCS hook/put/unhook streaming,  │
              │    ESC \ ST                        │
              │  - PLUGGABLE GROUND MODES:         │
              │    Ansi | Vt52Param | TekVector |  │
              │    DleCoordinate | NdGraphicsParam │
              │    (profile switches them; replaces │
              │    the pre-parser TDVInputProcessor │
              │    filtering)                      │
              └───────────────┬───────────────────┘
                              │ tokenized sequences (spans, no strings)
                              ▼
              ┌───────────────────────────────────┐
              │  TerminalProfile (per terminal)    │
              │  = capability record               │
              │    (DA strings, conformance level, │
              │     geometry, charset repertoire)  │
              │  + ordered HANDLER MODULES:        │
              │    Ecma48Core | DecPrivateModes |  │
              │    DecCharsets | XtermOsc |        │
              │    TdvNdText | TdvLeds | Sixel |   │
              │    Regis | TekPlot | NdGraphics    │
              │  (dispatch = table lookup, not     │
              │   virtual-override switch chains)  │
              └───────┬───────────┬───────────┬───┘
             semantic ops     graphics ops   response bytes
                      ▼           ▼               ▼
        ┌──────────────────┐ ┌──────────────┐ ┌─────────────────┐
        │ TextScreen        │ │ Graphics     │ │ HostReply (ONE  │
        │ (buffer+cursor+   │ │ planes:      │ │ byte channel;   │
        │  margins+tabs+    │ │ TekVectors / │ │ session wires it │
        │  charset state +  │ │ SixelRaster /│ │ once)            │
        │  line metadata +  │ │ RegisState / │ └─────────────────┘
        │  dirty rows)      │ │ NdGraphics   │
        └────────┬─────────┘ │  → draw into  │
                 │           │ IGraphicsSurface │
                 │           └──────┬───────┘
                 │    ┌─────────────┘
                 ▼    ▼
        ┌──────────────────────┐
        │ Compositor            │  text plane + graphics plane(s) + GIN overlay
        │ (owns the coordinate  │  logical coords → viewport transform → surface
        │  transform)           │
        └──────────┬───────────┘
                   ▼
        ┌──────────────────────┐
        │ Renderer (Desktop)    │  glyph atlas cache, dirty-row redraw,
        │                       │  theme/phosphor presentation, NO terminal
        │                       │  type-checks
        └──────────────────────┘
```

Key decisions and why the source justifies them:

**C.1 One parser, fixed, with pluggable ground modes — not per-terminal parsers.** The existing parser is 95% generic and zero-alloc; its defects are local and enumerable (B.1). The one thing it lacks structurally is a swappable ground state. TDV already proved the need by building `TDVInputProcessor` as a pre-parser filter; VT52 params, Tek vectors and ND DLE coordinates are the same shape. The rule: **the parser recognizes structure (which bytes belong to which sequence/mode); it never interprets.** `CSI ? 25 h` is tokenized identically for VT220, VT340 and xterm; `ESC "5d` is tokenized as an ND-graphics sequence only when the profile has enabled that ground mode, and its meaning lives in the ND module. The TDV `< = >` final-byte hack moves out of the shared table and into the TDV profile's dispatch.

**C.2 Engine + profile + handler modules — not an emulator class per terminal.** The evidence is already in the repo from both directions: `VT100Emulator` being 20 lines shows the subclass model collapses upward into the base; the TDV triplication shows it collapses sideways into copy-paste. A profile is data plus a module list:

```text
VT100  = Ecma48Core + DecPrivateModes(vt100 set) + Charsets(B,0,A) + VtKeyboard(vt100)
VT220  = VT100 modules + Charsets(G0-G3, NRCS, SS2/SS3) + DecPrivateModes(vt220) + Protected(DECSCA) + VtKeyboard(vt220) + 8-bit C1
xterm  = VT220 modules + XtermOsc + XtermPrivateModes + MouseReporting + ExtendedSgr
TDV2200= Ecma48Core(subset) + TdvNdText + TdvCharsets + TdvLeds + TdvKeyboard
VT340  = VT220 modules + Regis + Sixel
TDV2200G = TDV2200 modules + NdGraphics + Gin
Tek4014 = TekPlot + TekKeyboard (its own small profile — NOT merged with ND)
"ANSI" = Ecma48Core + ExtendedSgr (a thin generic profile; ECMA-48 itself is
         a MODULE shared by everyone, not an emulator — answering §Main/ANSI)
```

Dispatch becomes a table the profile assembles at construction (final byte + private marker + intermediate → handler), replacing the four-deep virtual switch chains (`TDV2200Emulator.HandleCsiSequence` → `TDVEmulatorBase` → base). Modules use the **clean** TDV component style that already exists: state-only, operating on `TextScreen` passed in, no back-references.

**C.3 TextScreen as the one semantic surface.** Promote what `TerminalBuffer` + the base's helpers already half-provide into one API the modules call: `MoveCursor / Print(glyph) / ScrollRegion / InsertLines(count, respecting margins) / EraseDisplay(mode) / SetMargins / SetTabStop / SwitchScreen(alt)` — with margins, origin clamping and tab stops implemented **once** (fixing B.5). Add line metadata (wrap flag + line attributes for DW/DH — fixes selection/copy and makes DECDWL/DECDHL renderable), dirty-row bits, ring-buffer scrollback, `Array.Copy` row moves, and a packed cell (target 12-16 bytes: 4-byte color, drop the duplicated DW/DH bits).

**C.4 One charset pipeline in Core.** `byte → CharsetState(G0-G3, shifts, single-shift) → GlyphRef { glyph id + font id }` resolved **before** the cell is written. VT profiles resolve to Unicode; TDV profiles resolve to (ROM index, font number) — the cell's existing `Codepoint + FontNumber` pair already expresses this, it just needs the mapping moved out of `BitmapFontRenderer`/the font ROM and into the TDV charset module. The parser stops force-decoding UTF-8: UTF-8 decoding becomes a property of the profile's transport encoding (xterm: yes; TDV/ND 8-bit: no).

**C.5 One host-reply channel.** `SendResponse(ReadOnlySpan<byte>)` on the engine; session subscribes once (the wiring point already exists beside `WireTDVQueryResponse`). The TDV string channel becomes a thin wrapper and is eventually removed. Fixes B.2 structurally.

**C.6 One mode table + capability record.** A small `TerminalMode` id → bool/value store on the engine with change notifications (the keyboard encoder and renderer both need to observe modes — today's DECCKM plumbing gap, B.10). Generic ECMA/DEC modes get shared ids; terminal-specific modes live in the profile's own id range. The capability record (answers DA/DSR content, geometry, feature flags) replaces `GetTerminalType/GetCapabilities` string concatenation (`TDV2200Emulator.cs:1170-1193`).

**C.7 Graphics: planes + `IGraphicsSurface`, no universal GraphicsCommand.** The four protocols share almost no command semantics — do not pretend otherwise. What they genuinely share:
- a **surface**: `Clear / SetPixel / Line / FillRect / (optional) DrawGlyph`, implemented over an in-memory bitmap in Core (testable headless) and blitted by the Desktop renderer; Skia never appears in Core.
- a **plane model**: text plane + zero or more graphics planes + a GIN/crosshair overlay, composed by a compositor.
- a **coordinate transform with one owner** (the compositor/viewport): ND/Tek bottom-left 0..4095-style logical space → top-left surface pixels, scaling, aspect, clipping. Neither the protocol module nor the renderer does its own math.
- **GIN-style input routing**: pointer events → active graphics module → encoded report bytes → HostReply (the reverse of the keyboard path, same shape).

Per-protocol state stays per-module: Tek storage-display beam state, Sixel palette/raster placement, ReGIS command state, ND graphics memory/visibility. Sixel/ReGIS consume the parser's fixed DCS streaming (hook → put chunks → unhook); Tek and ND consume ground-mode switching.

**C.8 Renderer: consumes state, never protocol.** Renderer reads: a snapshot (or double-buffered handoff — fixes the B.9 race), dirty rows, composited planes, cursor state, and a **presentation theme** (palette + phosphor + letterbox color — one canonical 256-color table, logical color stays in the cell). Glyph atlas: render each (glyph id, font id, attrs) once to a cached bitmap, then blit — replaces both the per-glyph FormattedText path and the per-pixel FillRectangle path. The `SyncFontVariant` type-check chain disappears because the charset module resolved variants before the cell was written.

---

## D. Reusable Building Blocks (common, implement once)

| Block | Seeded by (existing code) |
|---|---|
| ByteStreamParser with ground modes | `EscapeSequenceParser.cs` (fix, don't replace) |
| Ecma48Core handler module | extracted from `TerminalEmulatorBase.cs:456-1018` |
| DecPrivateModes module (parameterized by supported set) | `TerminalEmulatorBase.cs:578-627` |
| TextScreen (buffer + cursor + margins + tabs + line metadata + dirty) | `TerminalBuffer.cs` + base helpers |
| Packed TerminalCell + TerminalColor(4B) | `TerminalCell.cs`, `TerminalColor.cs` |
| CharsetState pipeline (G0-G3, shifts, NRCS, downloadable) | base :424-454 + `TDVCharacterSetManager` (merge the two SS2/SS3 impls) |
| Mode table + capability record | replaces the loose bools |
| HostReply channel | `SendResponse` + session wiring |
| KeyboardEncoder registry (profile-keyed, mode-aware) | `KeyboardMapper.cs` + `TDV2200KeyRegistry` binding model |
| PointerEncoder (mouse/GIN) | new; routes like the keyboard, reverse direction |
| IGraphicsSurface + in-memory impl + plane compositor + coordinate transform | new (Core); Skia blit in Desktop |
| Glyph atlas cache + dirty-row renderer | replaces both font renderers' inner loops |
| Presentation theme (palette + phosphor) | consolidates the three color tables |
| Headless test kit (byte-feed harness, spec-database validation, surface assertions) | existing test pattern + `TDV2200ValidationTestBase` |

## E. Terminal-Specific Components (stay isolated)

- **Per profile:** capability record (DA/ID strings, geometry, conformance), dispatch table contents, keyboard encoding tables, charset repertoire, mode-id extensions.
- **DEC family:** NRCS tables, DECUDK, ReGIS engine, Sixel decoder, VT420 rectangle/left-right-margin ops.
- **TDV/ND:** ND text extensions (work areas, LEDs, rectangle attrs, push keys), TDV font ROMs, ISO646 variants, ND graphics module + GIN, DLE coordinate ground mode, 2115 compatibility.
- **Tektronix:** Tek coordinate decoder, storage-display semantics, its own GIN encoding. **Kept separate from ND graphics** — they share only the surface/plane/transform blocks; the spec docs show related coordinate ideas but different command sets, and nothing in the source justifies merging them.
- **xterm:** OSC handlers, private-mode table, mouse encodings, modifier-key encodings.

## F. Migration Plan (incremental; each phase ships green)

**Phase 0 — Delete and repair (days).** Remove the B.11 dead-code list (≈1,500+ lines). Fix the four live bugs that need no redesign: C1 branch ordering (:178/:190), wire `DataToSend` in `TerminalSession` + implement Primary DA in the base, the unguarded logging allocations, the TDV2215 region-bottom scroll condition. Justified by: every later phase gets cheaper and the diffs get honest.

**Phase 1 — Parser hardening (1-2 weeks).** ST via `ESC \` (narrow the :155 pre-emption), DCS param collection + `OnDcsPut` streaming, sub-parameter support, ground-mode plug point (fold `TDVInputProcessor`'s DLE filtering into a parser mode; add `ESC "` tokenization gated on a profile flag — the spec plan already sketches this). The isolated parser test suite exists to lock every change.

**Phase 2 — TextScreen consolidation (2-3 weeks).** Margins/origin/tabs implemented once; `InsertLines/DeleteLines` region-aware; alternate screen wired to 47/1047/1049; DECSC full state; line metadata (wrap, DW/DH); ring scrollback; `Array.Copy` moves; packed cell; dirty rows; snapshot handoff for the renderer (fixes B.9). The scrolling-region test suites lock behavior.

**Phase 3 — Dispatch tables + profiles (2-3 weeks).** Extract Ecma48Core and DecPrivateModes into modules; introduce the profile object + mode table + capability record; make VT100 and "ANSI" the first real profiles; convert the TDV chain leaf by leaf (the clean component half converts almost mechanically; the back-reference components get the TextScreen API instead). Keyboard factory becomes profile-keyed; DECCKM plumbed; UI C0 fallbacks move into the Core encoders.

**Phase 4 — Charset pipeline (1-2 weeks).** One byte→glyph stage in Core; merge the two SS2/SS3 implementations; move ISO646/national mapping out of the font ROM and renderer; profile-controlled transport encoding (UTF-8 optional).

**Phase 5 — Renderer (2 weeks).** Glyph atlas, dirty-row redraw, cached brushes, canonical palette + presentation themes, honor DW/DH + blink, fix the timer leak. Independent of phases 3-4; can run in parallel after Phase 2.

**Phase 6 — Graphics foundation (2-3 weeks).** `IGraphicsSurface` + in-memory impl + compositor + coordinate transform + GIN routing + headless surface tests. Then the first graphics module rides on it.

Only after Phase 3 does adding a terminal become "write a profile + its specific modules." Phases are ordered by dependency, not preference; 0-1-2 are prerequisites for everything else.

## G. Recommended Emulator Addition Order

1. **ANSI/ECMA-48 improvements** — this *is* Phases 2-3 (SM/RM, ICH, SU/SD, tab stops, DECCOLM, alt screen, DA). Everything else reuses it. Ships the "ANSI" profile almost for free.
2. **VT220** — forces the charset pipeline (G0-G3, NRCS, SS2/SS3), 8-bit C1, DECSCA protected attributes, real DA/DECID plumbing, DECUDK over fixed DCS. After it, every later DEC terminal is mostly a capability delta.
3. **xterm** (then **xterm-256color**, which is nearly free — `TerminalColor` already carries 256/RGB) — forces OSC breadth, the private-mode table at scale, mouse reporting, modifier encodings. Highest day-to-day utility for modern hosts.
4. **TDV 2200 graphics (ND)** — first consumer of Phase 6 + the `ESC "` ground mode + GIN. Put it before Tek because it is the user's primary target, the protocol analysis and phase plan already exist in `spec\Tektronix\`, and it exercises every graphics building block.
5. **Tektronix 4010/4014** — reuses surface/planes/transform/GIN wholesale; adds only the Tek coordinate decoder and storage-display semantics. Cheap right after ND graphics, and a good independence test that the shared blocks are genuinely protocol-neutral.
6. **VT240/VT241** — first ReGIS consumer (DCS streaming from Phase 1 + surface from Phase 6).
7. **VT320** — text-only capability delta over VT220; validates that a pure-profile addition is now small.
8. **VT340** — ReGIS + Sixel together; Sixel is a raster module over the same surface.
9. **VT420** — DECLRMM left/right margins + rectangle ops (which by then share the TextScreen rectangle primitives the TDV attribute rectangles use).
10. **VT52 / VT102** — small profiles; VT52 needs only the ground mode from Phase 1 plus a tiny module (also required as VT100's ANSI/VT52 sub-mode, so much of it arrives with #1 anyway).
11. **TDV 1200 family repair** — last not because it's hard but because it already exists; it needs the Phase 3 conversion plus fixing its 2115-mode swallowing bug rather than new infrastructure.

---

## Appendix 0: Phase 0 execution notes (2026-08-08)

Phase 0 was carried out immediately after this review. Three corrections to the review's
own findings emerged while doing it — recorded here because the review text above was
written before them:

1. **`Desktop\Fonts\SystemFont.cs` / `BitmapFont.cs` are NOT dead.** §B.11 listed them for
   deletion. They are referenced by `FontManager` and covered by `Fonts\SystemFontTests.cs`,
   `Fonts\BitmapFontTests.cs` and `Fonts\FontManagerTests.cs`. They were kept.
2. **The two `EscapeSequenceParserTests.cs` files are NOT duplicates.** §B.11 called the
   root-level one a duplicate of `Terminal\Parsing\`. They differ (381 vs 264 lines, and
   different test bodies); deleting either would lose coverage. Both were kept.
3. **The dead TDV2200 escape-sequence region contained one LIVE member.**
   `protected override void HandleCharacter(uint codepoint)` — which sets `cell.FontNumber`
   from `TDVCharacterSetManager` — sat inside the otherwise-dead `#region`. Deleting the
   whole region broke 28 character-set/locking-shift tests. It was restored.
   **Lesson for later phases: audit the whole region's member list (`git diff` filtered on
   declarations), not a grep of the method names you expect to find.**

Also deliberately KEPT despite being unreachable today, because they are the intended
landing pads for Phase 1 rather than superseded duplicates:
`TerminalEmulatorBase.HandleDCSSequence` + its 2215 override + `TDVDCSHandlerFeature`
(DCS wiring), the unreachable `ParserState` members (now documented as unreachable in the
enum's XML comment), `ScrollingEngine.cs` (has tests; smooth-scroll feature),
the alternate-buffer implementation, and `TDVProtectedAreas`.

**2115 mode (resolved after the review, 2026-08-08).** §B.11 parked the TDV1200 2115-mode
handler as "needs a spec decision". The answer is that **2115 mode is the same on every TDV
model**, which makes the TDV1200 behaviour simply a bug: it alone routed CSI through
`TDV2115CompatibilityMode`, a stub returning `true` for `A B C D H J K` (and `BEL BS HT LF
CR`, and `ESC D/E/M`) while doing nothing, so entering 2115 mode disabled all cursor
movement and erase on that model. The stub is deleted; mode entry, the `?40`/`?66` numbers
and `ESC Q` are resolved once in `TDVEmulatorBase`; `TDV2115ModeParityTests` runs identical
byte streams against all three models. Note the doc conflict found on the way:
`TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md:342` and `TDV-COMPREHENSIVE-REFERENCE.md` say
`CSI ? 40 h`, while `TDV-QUERY-COMMANDS.md` and the (superseded) `TDV-IMPLEMENTATION-
REFERENCE.md` say `?66`. Both numbers are accepted; ?40 is treated as canonical.

**Closing the renderer-testability gap (§A.11, §B.8).** The review recorded that the real
`TerminalRenderer.Render` path was never pixel-tested, because the existing screenshot
tests re-implement rendering in SkiaSharp — their own colour maths, bold and reverse
handling — so a renderer regression left the pictures perfect and the suite green, despite
`TDV2200ScreenshotTests.cs:14-15` claiming it "Uses the ACTUAL TerminalRenderer".

`tests\RetroTerm.Tests\Avalonia\RenderedScreenshot.cs` now renders through the production
chain and nothing else: `TerminalCanvas.Render(DrawingContext)` → `TerminalRenderer.Render`
→ `IFontRenderer.DrawCharacter`, captured with `RenderTargetBitmap` on the headless Skia
platform (`UseHeadlessDrawing = false`, already configured). The canvas is arranged at its
exact natural size so the renderer's scale is 1.0 and one cell maps to a whole number of
pixels, which is what makes per-cell pixel assertions valid.

Each test does two things at once: it writes a magnified PNG (nearest-neighbour, so no
invented colours) into `tests\RetroTerm.Tests\Avalonia\images\rendered\` for a human to
look at, and it asserts on the true-size pixels so a regression fails the build unattended.
That folder is gitignored — the images are regenerated every run, not committed.
`RenderedOutputValidationTests` covers glyph placement, the cursor (drawn and DECTCEM-
hidden), reverse video, bold, conceal, ANSI/256/24-bit colour, erase, scroll regions, the
TDV2200 bitmap-font path and its graphics character set, and the default phosphor colour.
The older SkiaSharp-reimplementing screenshot tests are left in place for now; they should
be migrated onto this harness rather than trusted.

## Appendix 0b: Phase 1 execution notes (2026-08-09) — COMPLETE

Phase 1 (§F) is done. All four items landed, full suite green at 3088 passing.

**DCS is now usable** (`EscapeSequenceParser.HandleDcsState`). Was a stub that made Sixel,
ReGIS and DECUDK impossible: introducer parameters discarded, no `DcsParam` case at all (so
`DCS 0;1;0 q` wedged the parser and swallowed everything after it), payload dropped, and
`OnDcsPut` never raised. The introducer now collects parameters, sub-parameters, private
markers and intermediates through the *same helpers CSI uses*, so the two cannot drift.
Payload streams to `OnDcsPut` in 4 KB chunks — a full-screen Sixel image is neither buffered
whole nor charged one callback per byte.

**The DCS events are wired.** `TerminalEmulatorBase` now subscribes `OnDcsHook`/`OnDcsPut`/
`OnDcsUnhook` and exposes them as `HandleDCSSequence` / `HandleDCSData` / `HandleDCSEnd`.
Previously nothing subscribed, so those methods and the TDV2215 override were unreachable.
`TDVDCSHandlerFeature` no longer scans for its own terminator — that is the parser's job.

**ST works.** The unconditional "ESC resets everything" rule ate the first half of `ESC \`,
the terminator hosts actually send, so string sequences could only end with a bare 0x9C.
String states now take one byte of lookahead: `ESC \` completes, anything else abandons the
string and is reprocessed (keeping the old recovery behaviour). An abandoned DCS still
raises the unhook so a consumer is never left waiting.

**Sub-parameters parse.** `:` matched nothing and silently killed the sequence, so
`SGR 38:2::R:G:B` (the T.416 form modern terminals emit) was dropped. Values come through
`Parameters` flat, with `SubParameterFlags` marking which followed a colon.

**The ground-mode seam exists** — this is the piece VT52, Tektronix and ND graphics all
needed, and it is deliberately generic rather than per-terminal parser states:
- `ExpectRawBytes(count, handler)` — hands over the next N bytes verbatim, ahead of even ESC
  handling, for sequences with BINARY parameters (VT52 `ESC Y <row><col>`, ND `DLE <row><col>`).
  Those bytes may legitimately be 0x1B or a control code, so nothing may interpret them.
- `GroundFilter` — first refusal on every Ground byte, for modes where a plain byte is not
  text (Tektronix vectors after GS, DLE addressing). It replaces the current approach of
  filtering bytes *upstream* of the parser in `TDVInputProcessor`, which is precisely why the
  parser cannot see terminal modes today.

Two properties worth keeping: the filter is consulted **only** in Ground, so it can never
corrupt a partly parsed sequence; and it never receives the ESC, so a mode can always be
escaped — a filter that could swallow ESC would be able to wedge the terminal in itself
permanently. `Reset()` abandons raw requests but deliberately does **not** clear
`GroundFilter`, since a RIS must not switch off a mode behind the emulator's back.

Still open from the review and NOT part of Phase 1: `ESC "` (ND graphics) tokenization is now
expressible via these seams but no terminal uses them yet; the `TDVInputProcessor` upstream
filtering has not been migrated onto `GroundFilter`; APC/PM/SOS still dispatch as ordinary
escape sequences with their payload printed as text.

## Appendix 0c: Phase 2a execution notes (2026-08-09) — screen model, part 1

Phase 2 in section F is a large change to the layer everything else sits on. It was split so the
straightforward correctness work lands first, held by tests, before the invasive representation
work (packed cell, ring scrollback, snapshot handoff) touches the same code. This is part 1.

Five defects fixed, all verified in the source before being changed:

1. **IL/DL ignored the scrolling region.** `TerminalBuffer.InsertLines/DeleteLines` hardcoded
   `Height - 1` as the bottom row, so `CSI L`/`CSI M` inside a `DECSTBM` region pushed content
   past the region's bottom and moved rows the region does not own — a full-screen program that
   reserves a status line smeared it. Both now take an explicit `bottomRow`, and
   `TerminalEmulatorBase` passes `ScrollBottom`. A cursor outside the region makes them no-ops.
   The old two-argument overloads remain, defaulting to the full screen.

2. **DL pushed deleted lines into scrollback.** `DeleteLines` called `ScrollUp(row, …)`, which
   saves the departing line whenever `topRow == 0`. Lines an application *deleted* never scrolled
   off and are not history; putting them in scrollback corrupts what the user scrolls back
   through. `ScrollUp` gained an explicit `toScrollback` flag; DL passes false. The distinction
   is the point — an index at the bottom of the screen still saves, and a region that does not
   start at row 0 still discards.

3. **Tab stops did not exist.** HTS was `// TODO: Implement tab stops` and `HandleTab` was
   hardcoded to the next multiple of 8, so a host that moved its stops — which forms do — landed
   in the wrong columns and had no way to notice. There is now a per-column stop array with
   HTS (`ESC H`), TBC (`CSI g`, modes 0 and 3), CHT (`CSI I`) and CBT (`CSI Z`). Stops reset to
   every-8 on RIS and on resize, since the array is per column.

4. **DECSC saved only position.** `Cursor.Save()` stores row, column and style. The standard's
   cursor state also covers graphic rendition, the G0–G3 character sets, origin mode and
   auto-wrap, so "DECSC, change colour, DECRC" kept the changed colour. `SaveTerminalState` /
   `RestoreTerminalState` in the base now carry the whole set. A DECRC with nothing saved goes
   home with default rendition, per the standard. `CSI s`/`CSI u` (SCO) are deliberately left
   position-only — that is what they are.

5. **The alternate screen was never wired, and resize could crash.** `TerminalBuffer` implemented
   the alternate buffer, but DECSET `47`/`1047`/`1049` were all `// TODO`, so no full-screen
   program could use it. All three are wired now with their real differences: `47` does not clear,
   `1047` clears on the way out, `1049` saves state, clears on the way in and restores on the way
   out. Separately, `Resize` left the *inactive* buffer at the old size while `Width`/`Height`
   moved on — `Clear()` then indexed past the end of it, a hard crash on any resize taller than
   the old screen, and switching back handed out a wrongly sized primary. Both grids are resized.

Also from the Phase 2 list: row moves in `ScrollUp`/`ScrollDown` are now a single `Array.Copy` of
the whole block instead of a cell-by-cell double loop. The screen is a rectangular array, so it is
row-major and the block move is exact; `Array.Copy` handles the overlap as if it had gone via a
temporary.

Tests: `tests\RetroTerm.Tests\Terminal\Phase2ScreenModelTests.cs` (25 tests). Suite 3088 → 3113
passing, 41 skipped, no regressions.

Two notes for whoever does part 2. First, the C# `\x` escape is variable length, so `"\x1b7"` is
the single character U+01B7 and not ESC + '7' — that cost four failing tests here and had already
cost time once in `CtrlSpaceNulTests`. Use the four-digit backslash-u-0-0-1-b form whenever the next
character is a hex digit. Second, `Resize` preserves content by top-left corner only; no reflow.
That is unchanged behaviour, not something Phase 2a introduced, and it belongs with the line
metadata work (a wrapped line cannot be reflowed until the buffer records that it wrapped).

Still to do in Phase 2b: line metadata (wrap flag, double-width/double-height), ring scrollback
instead of `List` with `RemoveAt(0)`, packed cell representation, dirty-row tracking, and the
snapshot handoff to the renderer that closes the UI-thread data race (problem B.9).

## Appendix 0d: Phase 2b execution notes, part 1 (2026-08-09) — scrollback ring + line metadata

**Scrollback is now a fixed-capacity ring.** It was a `List<TerminalCell[]>` that `Add`ed a freshly
allocated row and then `RemoveAt(0)`'d once over the limit. Both halves were wrong on the hot path:
`RemoveAt(0)` shifts every remaining entry, so at the default 10000 lines a single screenful of
scrolling moved roughly a quarter of a million references to no purpose, and every scrolled line
allocated a row array the GC then had to take back. The ring overwrites the oldest slot and
**reuses its row array**, so steady-state scrolling allocates nothing. Only the slot array is
allocated up front — one reference per line, 80 KB at the default. `ClearScrollback` resets the
window but deliberately keeps the row arrays, since they are the right size and will be reused.

**Per-line metadata: the wrap flag.** The grid cannot answer a question that matters: a line that
filled up and continued onto the next one looks exactly like a line that ended with a newline.
`TerminalBuffer` now carries a per-row flag, set by the emulator when auto-wrap carries the cursor
over, and it moves with the line — through `ScrollUp`/`ScrollDown` (an `Array.Copy` alongside the
cell block), into scrollback, across an alternate-screen swap, and through a resize. `ClearLine`
and `Clear` clear it. This is the prerequisite for reflow-on-resize, which is impossible in
principle without it, and it is immediately useful to screen-to-text: `ScreenReader.GetScreenText`
takes a `joinWrappedLines` flag that puts a wrapped line back together. It is **off by default** —
existing callers (MCP reads, prompt matching, the test suite) want the screen-shaped form, and
changing that silently would have been a behaviour change nobody asked for.

Not done, deliberately: double-width/double-height line attributes (DECDWL/DECDHL). The metadata
slot is the right home for them, but they are only meaningful once the renderer honours them, so
they belong with the rendering work rather than as unused state.

### A divergence found while testing, NOT introduced here

`Cursor.Advance` (`Cursor.cs:192-210`) wraps **eagerly**: it moves to the next line the moment the
last column is filled. A real VT **defers** the wrap — after writing the last column the cursor
stays there in a "pending wrap" state and only a further character moves it on. That is why
printing exactly 80 characters on an 80-column VT does not scroll the screen, and it is a visible
difference for any host that draws a full-width bottom line.

The consequence for this work is that filling a line exactly is indistinguishable from a line that
really continued, so the wrap flag follows the eager behaviour. The test
`ExactlyFillingALine_MarksItWrapped_BecauseThisEmulatorWrapsEagerly` documents that explicitly
rather than asserting behaviour the code does not have. Fixing it means adding pending-wrap state
to the cursor, which touches every wrap path (`HandleCharacter`, the region-bottom scroll, the
TDV2215 override, CUF/CUB clamping) and deserves its own change with its own tests.

Tests: `tests\RetroTerm.Tests\Terminal\Phase2ScrollbackAndLineMetadataTests.cs` (23 tests).
Suite 3113 → 3136 passing, 41 skipped, no regressions.

## Appendix 0e: Phase 2b part 2 (2026-08-09) — the frame handoff, B.9 CLOSED

The renderer no longer reads the live buffer. `ScreenFrame`
(`src\RetroTerm.Core\Terminal\Rendering\ScreenFrame.cs`) is one finished picture of the screen; the
pump fills one and publishes it, and `TerminalRenderer` only ever reads a published frame. Nothing
mutates a frame after publication, so the fix is structural — there are no locks and no retries.

What this actually closes: `TerminalCell` is a 20-byte struct, so the old UI-thread walk could
return a character wearing another cell's colours; and a concurrent `Resize` swaps the whole array,
so the indexer could throw in the middle of a paint. Both are gone by construction.

Four decisions worth recording, because each had a wrong-looking alternative:

- **The frame stores the viewport, already resolved through the scroll offset.** The obvious design
  captures the live screen and lets the renderer resolve scrollback itself. That would have left
  the renderer reading the scrollback ring — which, since part 1, *recycles its rows in place*, so
  it is exactly as racy as the grid was. Resolving on the pump is what lets the renderer stop
  touching history at all.
- **The scroll offset travels to the pump, not the frame to the UI.** `ViewScrollOffset` is written
  by the UI and read at capture time. Simply invalidating on a scroll would redraw the frame we
  already had (the old position), and on an idle terminal with no incoming data nothing would ever
  produce a new one. `FrameRequested` → `TerminalSession` → `_pump.RunAsync` is how a scroll gets a
  frame without the UI touching the buffer.
- **Three frames in the pool, not two.** The renderer holds its reference for the whole draw, so
  the pump must not write into that one. With two, the single spare *is* the frame the renderer may
  still be reading. The pool also skips whichever frame is currently published, and a test drives
  25 consecutive publishes to catch a rotation bug. This is ordinary triple buffering: the pump
  would have to get two full publishes ahead of a single draw to catch the reader.
- **Publish happens *before* `Invalidated` is raised, and everything routes through
  `OnInvalidated()`.** A notification that arrived ahead of its frame would make the renderer draw
  the previous one. The four direct `Invalidated?.Invoke()` sites were folded into the existing
  helper so there is one place this can be got wrong.

The canvas now also takes its natural size and its scrollback count from the frame rather than the
buffer. Sizing from the live buffer let the two disagree for one paint after a resize — content
scaled as if it were the new size while still being the old one.

Tests: `tests\RetroTerm.Tests\Terminal\ScreenFrameHandoffTests.cs` (16 tests), centred on the
property everything rests on: a frame handed out does not change when the screen does. Suite
3136 → 3152 passing, 41 skipped, no regressions.

Worth noting as evidence rather than assertion: the 18 screenshot tests added earlier drive the
real `TerminalRenderer` through `RenderedScreenshot` and assert on pixels. They pass unchanged
against the frame path, so the rewrite is not merely compiling — it is rendering the same pixels.

## Appendix 0f: Phase 2b part 3 (2026-08-09) — margins and origin mode, implemented once

The last of the Phase 2 "implemented once" list (problem B.5). These rules were re-derived at each
call site and disagreed with each other. Six defects, all verified in the source, all reachable
from ordinary host output:

1. **VPA ignored origin mode.** `CSI d` set the row absolutely while `CSI H` applied the top
   margin, so the same row number meant two different rows depending on which sequence a host used.
2. **CUP in origin mode never clamped to the bottom margin.** It added `ScrollTop` and stopped
   there, so a row past the region escaped it — origin mode did not actually confine anything.
3. **CUU/CUD clamped to the screen, not the margins.** Cursor-up walked straight out of the
   scrolling region.
4. **CNL/CPL had the same problem**, since they went through the same unclamped moves.
5. **DECOM homed to the absolute corner.** Turning origin mode *on* put the cursor outside the
   region it had just confined it to.
6. **CPR reported absolute rows.** A host that sent `CUP 2;5` in origin mode and then asked where
   the cursor was got a different number back from the one it had sent.

Everything vertical now goes through four helpers on the base — `RowAddressingOrigin`,
`SetCursorRowFromHost`, `MoveCursorUpWithinMargins`, `MoveCursorDownWithinMargins` — so the rules
exist in one place and cannot drift apart again.

The margin rule for CUU/CUD has a subtle half worth keeping: the margin only limits a cursor that
started **inside** the region. A cursor above the top margin must not be dragged down into the
region by a cursor-up, and one below the bottom margin keeps the whole screen to move in. Both
halves are tested.

Tests: `tests\RetroTerm.Tests\Terminal\MarginsAndOriginModeTests.cs` (18 tests). Several are
written as agreement properties rather than fixed expectations — CUP and VPA must land on the same
row in both modes, and a position must survive a round trip out through CPR — because that is the
form the defects actually took. Suite 3152 → 3170 passing, 41 skipped, no regressions; the
existing scrolling-region suites pass unchanged.

### Dirty-row tracking: examined and deliberately deferred

It was next on the Phase 2 list and the frame is the natural place to carry a changed-row set, but
it buys nothing on its own. `TerminalRenderer.Render` is immediate-mode: it re-emits every drawing
command into a fresh `DrawingContext` each paint, so knowing that only two rows changed does not
let it skip anything — there is nothing retained to skip. Dirty rows only start paying once the
renderer draws into a cached bitmap and blits, which is the glyph-atlas work in C.8. Doing them
first would add bookkeeping to the hot path in exchange for nothing measurable.

### Still open in Phase 2b

- ~~The snapshot handoff (B.9).~~ **Done — see appendix 0e.**
- Packed cell representation (perf; `TerminalCell` is 20 bytes and every frame copies a screenful).
- Dirty-row tracking — deferred on purpose, see above; it needs the cached-bitmap renderer first.
- Deferred wrap, per the divergence above.

## Appendix 0g: Phase 3 part 1 (2026-08-09) — the terminal profile, and DECRQM

The first slice of Phase 3, attacking B.3: `TerminalEmulatorBase` secretly *was* the VT100. The DA
reply was a VT100 reply hardcoded in the base, `VT100Emulator` was twenty lines overriding
`ToString`, `GetTerminalType()` returned the placeholder `"Terminal"` for anything that did not
override it, and the factory answered a request for a VT220 with a VT100.

`TerminalProfile` (`src\RetroTerm.Core\Terminal\Profiles\TerminalProfile.cs`) now carries a
terminal's identity: its name, both Device Attributes replies, the DEC private modes it recognises,
and a `TerminalFeatures` flag set. `Profile` is **abstract** on the base, not defaulted — a default
would have meant "everything is a VT100 unless it says otherwise", which is the confusion being
removed. Five production classes and three test doubles now each declare what they are.

`Ecma48Emulator` makes the claim real rather than aspirational: a terminal that adds no behaviour,
only a different identity and capability set, needs no class at all. The factory's new `"ANSI"`
entry is exactly that — a profile and nothing else. Terminals that genuinely add behaviour (the TDV
family) still subclass.

**DECRQM (`CSI ? Ps $ p`)** is implemented, answering DECRPM. This is how a host discovers what a
terminal can do without guessing from the DA string, and it is why the profile carries a list of
recognised modes: *"I know that mode and it is off"* and *"I have never heard of it"* are different
answers, and collapsing them into "reset" tells a host it may safely use a mode this terminal will
silently ignore. It shares the `?` marker with DECSET/DECRST and differs only by the `$`
intermediate, so a dispatch that ignored intermediates would have **set** the mode instead of
reporting it — there is a test for precisely that.

### Two things the tests found, both pre-existing

- **TDV replies do not travel on `DataToSend`.** They go through `TDVEmulatorBase.OnResponseReady`,
  a string channel of its own above the generic byte one (the A.7 split). A test subscribing to the
  wrong channel observed *nothing at all* and looked like a broken emulator rather than a broken
  test. Worth keeping in mind: the split is silent in exactly this way. Unifying the two channels
  belongs in a later Phase 3 slice.
- **`TDV2200Emulator.GetTerminalType()` appends its active options** — `"TDV2200+ISO646_International"`
  rather than `"TDV2200"`. Existing behaviour, so the profile test matches the model name as a
  prefix. That the type string is simultaneously an identity and a capability list is part of why
  the capability record exists.

### Stated, not hidden: "VT220" still returns a VT100

The factory entry is kept, because removing it would break existing configurations, but it now says
so in the code and in a test. The VT220-only features (soft character sets, DECUDK, the wider
national set support) are not implemented, so the emulator reports itself through the VT100 profile
instead of claiming a VT220 identity it could not live up to — a host that probed DA and then sent
VT220 sequences would be worse off than one told the truth. `AskingForAVt220StillGetsAVt100_AndSaysSo`
is the test that should fail, and be updated, when VT220 is really built.

Tests: `tests\RetroTerm.Tests\Terminal\TerminalProfileTests.cs` (20 tests). Suite 3170 → 3190
passing, 41 skipped, no regressions.

## Appendix 0h: Phase 3 part 2 (2026-08-09) — one reply channel, not two

Closes the A.7 split. There were two ways for a terminal to answer its host: the generic
`TerminalEmulatorBase.DataToSend`, carrying bytes, and `TDVEmulatorBase.OnResponseReady`, carrying
a string that `TerminalSession` encoded as **UTF-8**. That was wrong twice over.

**The encoding.** A reply is a byte string. UTF-8 turns any byte above 0x7F into two bytes on the
wire — 0x9C (8-bit ST) would have gone out as `C2 9C`. This was **latent, not live**: a sweep of
the TDV reply builders found no reply containing such a byte today. But nothing prevented one, and
the symptom would have been a host inexplicably rejecting a valid answer.

**The split itself.** Two channels meant two near-identical send handlers in the session — same
logging, same tracing, same byte counting, different encoding — and a caller watching the wrong one
saw *silence* rather than an error. That is not hypothetical: it happened one appendix ago, writing
`TerminalProfileTests`, where subscribing to `DataToSend` on a TDV emulator produced an empty
collection that read like a broken emulator rather than a broken test.

`TDVEmulatorBase.SendResponse(string)` now converts char-to-byte one for one and sends through the
base byte channel. `OnResponseReady` still fires and is documented as what it now is — an
**observation point**, not a wire. `TerminalSession.OnTDVResponseReady` is gone;
`WireTDVQueryResponse()` is kept, because callers ask for it by name, and delegates to
`WireEmulatorResponse()`.

### A test that was pinned to the wiring rather than the behaviour

Ten session integration tests failed on the first run, all through one helper: `VerifyEventWiring`
used reflection to assert that `OnResponseReady` had subscribers. That checks *which handler
happens to be attached*, not whether replies can reach the host — so it broke while replies were
still flowing perfectly. Its nine siblings in the same class, which assert that actual bytes arrive
at the connection, passed throughout, and they are the reason this change can be called verified
rather than merely compiled. The helper now checks `DataToSend`, which is the wire.

Tests: `tests\RetroTerm.Tests\TDV\TdvReplyChannelUnificationTests.cs` (8 tests), including a sweep
of every byte 0x80–0xFF through the reply path. Suite 3190 → 3198 passing, 41 skipped.

One note for anyone editing that file: the high-byte test builds its string from explicit `char`
values rather than embedding a literal U+009C. The first draft did embed one — the Write went
through as UTF-8 `C2 9C` and the test passed for the right reason, but it would have depended on
the file staying UTF-8 for its meaning, and a re-encode could have changed what it tested without
failing.

## Appendix 0i: Phase 3 part 3 (2026-08-09) — the mode table

Three defects, all host-visible, all found by looking at what could set the mode fields rather than
at what read them.

**1. The ANSI modes were not handled at all.** `CSI Ps h` / `CSI Ps l` — SM/RM, the forms with no
`?` marker — had no case in the CSI switch. The consequence was not merely that the sequences were
ignored. `InsertMode` and `NewLineMode` had fields, reset code, and **read sites** in
`TerminalEmulatorBase.HandleCharacter` and `TDV2215Emulator`, and nothing anywhere could set either
of them to `true`. The insert-mode path was fully written and permanently unreachable, so a host
using IRM to insert text silently got overwrite instead. Grepping for *writers* of a field, not
readers, is what surfaced this.

**2. A mode sequence applied only its first parameter.** `HandleDecPrivateMode` read
`parameters[0]` and stopped. `CSI ? 1 ; 25 h` sets DECCKM *and* DECTCEM, and hosts do bundle modes;
every mode after the first was dropped. Both the private and ANSI forms now loop, and a bundle
containing one mode this terminal does not implement still applies the rest — there is a test for
that, because the natural way to write the loop is to bail out on the unknown one.

**3. DECRQM covered private modes only**, so a host had no way to ask about an ANSI mode. `CSI Ps
$ p` now answers alongside the `?` form.

Modes are now applied one at a time through `SetDecPrivateMode` / `SetAnsiMode`, and read through
`GetPrivateModeState` / `GetAnsiModeState`. That pairing is the mode table the plan asked for: a
terminal adding a mode overrides two small methods instead of editing a switch that also has to
stay in step with the reporting path.

### A test that passed, and why I checked it anyway

`ResetTurnsInsertModeOffAgain` passed on the first run — but I had written `"c"` where RIS needs
`ESC c`, which should have left insert mode on and failed the assertion. Reading the file back
showed the Write had turned `\x1bc` into a real ESC byte, so RIS did happen and the test was sound.
Worth recording for two reasons. The escape was **interpreted at write time**, exactly as the
U+009C literal in appendix 0h was, so both now build their strings from explicit `char` values
rather than depending on what ends up in the file. And the test asserted only a screen shape, which
would have passed just as well if RIS had cleared the screen while leaving the mode set; it now
queries the mode through DECRQM as well.

Tests: `tests\RetroTerm.Tests\Terminal\ModeHandlingTests.cs` (15 tests). Suite 3198 → 3213 passing,
41 skipped, no regressions.

## Appendix 0j: Phase 3 part 4 (2026-08-09) — DECCKM actually reaches the keyboard

Same question as appendix 0i, asked the other way round: not *who writes this field* but **who reads
it**. `ApplicationCursorKeys` was written by DECCKM and read by nothing that mattered.

The keyboard mapper had the branch — `ApplyTerminalModeModifications` turns `ESC [ A` into
`ESC O A` when `TerminalModes.ApplicationCursorKeys` is set, and it is called from both mapping
paths. But what *called* the mapper was the UI, and the UI built its mode set from a chain of
emulator **type checks** that only ever produced the TDV model flags. Nothing anywhere set
`ApplicationCursorKeys` on that flag set, so the branch was unreachable.

The visible effect: vi, less and most full-screen programs enable DECCKM and expect `ESC O A` from
the up arrow. They got `ESC [ A`, always.

That type-check chain was also **duplicated** in `TerminalCanvas` and `MainWindow`, and it was
business logic in a view — which the project's own rule forbids. Both are now one call to
`TerminalEmulatorBase.GetActiveModes()`, which the TDV emulators override to add their model flag.
The emulator owns the state, so the emulator answers the question, and a new terminal with new
modes does not need the UI edited.

Not done: `TerminalModes.ApplicationKeypad` now reaches the mapper, but the mapper's keypad branch
is still an empty stub — DECKPAM is reported and carried correctly, and then nothing is done with
it. Implementing it needs the keypad key codes, which is its own piece of work; it is listed under
B.10 and stays open.

### A mode-number collision on the TDV2215, recorded rather than guessed at

A test asserting that a TDV2215 reports DECCKM failed. The cause is real:
`TDV2215Emulator.HandleCsiSequence` claims private mode **1** for "enable extended mode" and
returns before the base handler sees it. On that model, DECCKM is unreachable by its standard
number — the sequence a VT100 uses to switch its arrow keys means something else entirely.

**Whether that is correct depends on the TDV2215 specification, which I have not verified.**
`docs\TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md` is where to settle it. The test
`OnTheTdv2215PrivateModeOneIsExtendedMode_NotDeccKm` documents the behaviour as observed and says
plainly that it does not endorse it; if a real 2215 uses mode 1 for DECCKM, that test should fail
and the emulator should change. It exists so the collision is visible rather than a surprise to
whoever next wonders why arrow keys behave differently on a 2215.

Tests: `tests\RetroTerm.Tests\Terminal\ActiveModeReportingTests.cs` (17 tests), ending with an
end-to-end check that a host turning DECCKM on changes the bytes the terminal sends for each arrow
key. Suite 3213 → 3230 passing, 41 skipped, no regressions.

## Appendix 0k: Phase 3 part 5 (2026-08-09) — the keypad under DECKPAM

The mode reached the mapper as of appendix 0j, and then the mapper ignored it: the keypad branch of
`ApplyTerminalModeModifications` was an empty stub carrying the comment *"this would need specific
keypad key handling"*.

**The stub could not have been filled in where it stood.** `ApplyTerminalModeModifications` receives
the *finished sequence* a key produced. Application keypad mode depends on **which key was
pressed**, not on what it produced — the whole point is telling the keypad `4` from the
main-keyboard `4`, and by that stage both look identical. That is why the branch sat empty while the
cursor-key branch beside it worked: DECCKM *can* be done as a sequence rewrite (`ESC [ A` →
`ESC O A`), and DECKPAM cannot. The handling now happens in `MapKey`, ahead of the tables.

Keypad `0`–`9` send `ESC O p` … `ESC O y`, and the operator keys `* + , - . /` send `ESC O j` … `o`.
Two things are deliberately left alone: modified presses (DEC defines no modified form, and a user
may have bound Ctrl+keypad to something) and the main-keyboard digits.

### The double-delivery trap

Two Desktop changes were needed, and the second is the interesting one. `AvaloniaKeyHelper` gained
the numeric-keypad VK codes, which simply did not exist. Then the keypad keys had to be routed to
the mapper at all — but **only while the mode is on**.

`IsKeypadKey` is deliberately separate from `IsSpecialKey` for that reason. A special key always
goes to the mapper; a keypad key is only special while DECKPAM is on. In numeric mode it is an
ordinary character that must keep travelling the normal text path, and routing it to the mapper as
well would deliver every keypad digit **twice**. `WithoutTheModeAKeypadKeyIsNotClaimedByTheMapper`
pins the Core half of that.

Tests: `tests\RetroTerm.Tests\Terminal\ApplicationKeypadTests.cs` (24 tests). One is worth
mentioning: `TheDigitsAreDistinct` checks the ten sequences are all different, because a
transcription slip in a ten-entry table is easy to make and invisible to a per-digit test that
happens to be wrong the same way.

Suite 3230 → 3254 passing, 41 skipped, no regressions.

## Appendix 0l: Phase 3 part 6 (2026-08-09) — Ctrl+key decided once

The review recorded "C0 fallback tables duplicated in two UI classes". It was worse than two.

`Ctrl+letter → C0` existed in **three** places: `TerminalCanvas`, `VirtualKeyboardWindow`, and
label-based inside `VirtualKeyboardPanel`. `Ctrl+Space → NUL` existed in four (those two views, plus
`BaseKeyboardMapper` and `TDVKeyboardMapper` separately). `Ctrl+Backspace → NUL` existed in two.
What Ctrl+A means is a property of the terminal, not of whichever control has focus.

`MapUniversalControl` now holds all three rules, and **both** mapper families call it.

### The regression this nearly caused

`TDVKeyboardMapper` overrides `MapKey` completely. Deleting the UI fallback while adding the rule
only to `BaseKeyboardMapper` would have left TDV sessions with **no Ctrl+letter at all** — Ctrl+C
would have stopped reaching an ND host. The shared helper is called from both overrides for exactly
that reason. This is not the "no VT220 fallback in TDV mode" rule being bent: a C0 code is not a
VT220 escape sequence, it is what CTRL on a TDV keyboard has always produced.

### The on-screen keyboard keeps the label as its authority

`VirtualKeyboardPanel` resolves Ctrl+letter from the key's **label**, not its VK code, and that is
right: the label is what the user clicked, and it is what changes with the national layout while the
VK on the layout entry stays put. The new mapper rule would have answered first and taken that over,
so the panel now skips the mapper for Ctrl on an alphanumeric key. The mapper's rule serves the
physical-keyboard paths, which have no label to consult.

That was caught by five existing tests failing, not by inspection.

### Two things I had wrong, recorded because the pattern repeats

- **I introduced the very duplication this appendix removes.** Appendix 0k added a numeric-keypad
  VK table to `AvaloniaKeyHelper.ToVKCodeForMapper` — thirteen entries that
  `TDVKeyBindingConfiguration.AvaloniaKeyToVK` already had, one call away, with identical values.
  Removed. `ToVKCodeForMapper` also re-implemented the letter and digit ranges before delegating to
  the same table, so it is now a straight delegation and the two cannot drift.
- **A test asserted a guess.** `BareBackspaceIsStillBackspace` expected DEL (0x7F); this mapper
  sends BS (0x08). Both are in use in the wild — a real VT100 sends DEL, xterm is configurable — so
  the test now records the choice this codebase already made instead of my assumption about it.

### A hazard worth naming

`VirtualKeyboardOnKeyPressedTests.SimulateOnKeyPressed` is a **hand-written copy of
VirtualKeyboardPanel's resolution order**, not a call into it. It therefore diverged the moment the
panel changed, and its five failures were reporting a difference from the panel rather than a defect
in it. A test that re-implements the thing it tests can pass while production is broken, and fail
while production is fine. It is updated to match, but it is the same class of problem as the
screenshot tests that used to re-implement the renderer.

Tests: `tests\RetroTerm.Tests\Terminal\ControlKeyEncodingTests.cs` (19 tests), including a sweep of
all 26 letters and an assertion that the VT and TDV mappers agree on every one — the property the
whole change exists for. Suite 3254 → 3273 passing, 41 skipped.

## Appendix 0m: Phase 3 part 7 (2026-08-09) — the keyboard comes from the profile

`KeyboardMapperFactory.CreateMapper(emulator.GetType().Name)` — the keyboard was chosen by
**class name**, with a silent VT100 fallback for anything unrecognised. Two consequences, neither
of them anyone's decision: renaming an emulator class silently downgraded that terminal's keyboard
to VT100, and a terminal whose class name was not in the list got VT100 keys without anyone
choosing so.

`Ecma48Emulator` — added three appendices ago for the "ANSI" profile — was exactly that second
case. It worked, by luck, through the fallback. `TerminalProfile` now carries a `KeyboardLayout`,
the ANSI profile states `"VT100"`, and `CreateMapper(profile)` is what the canvas calls.

`KeyboardLayout` is separate from `Name` on purpose: several terminals can share one keyboard while
differing in everything else, and a terminal's identity should not have to be its keyboard's
identity. It defaults to the profile's own name, which is right for a terminal with a keyboard of
its own.

**An unknown layout now throws** rather than quietly substituting VT100, and the message names both
the profile and the layout it asked for. "This terminal types on a VT100 keyboard" is a decision for
a profile to state, not something a lookup should assume when it recognises nothing. The old
string-keyed overload keeps its fallback so existing callers behave as before, but it is no longer
how any emulator gets its keyboard.

### A mapper production cannot reach

`VT220KeyboardMapper` is implemented, and reachable by layout name — but there is no `VT220Emulator`
class, and the factory's `"VT220"` entry builds a `VT100Emulator` carrying the VT100 profile. So
nothing in the running application ever selects it. This is the keyboard half of the same limitation
appendix 0g recorded for DA replies. It is **not dead code to delete**; it is code waiting for the
VT220 profile to exist. `TheVt220KeyboardExistsButNoShippedProfileAsksForIt` records it and says
what should change when VT220 is really built.

Tests: `tests\RetroTerm.Tests\Terminal\KeyboardLayoutSelectionTests.cs` (14 tests), including one
that walks every emulator type the factory can build and asserts each resolves to its own mapper —
the guard that would catch a new profile with a typo in its layout name. Suite 3273 → 3287 passing,
41 skipped.

### A stale memory note, corrected

My own project memory claimed "there is NO `TDV2200KeyboardMapper` type — an earlier memory entry
claiming that name was wrong (verified 2026-07-30)". The source disagrees: `TDV2200KeyboardMapper`
exists at `KeyboardMapper.cs:417`, is VK-keyed, and is what the factory builds;
`TDV1200KeyboardMapper` and `TDV2215KeyboardMapper` are empty subclasses of it. The note had
conflated it with the *name*-keyed `TDVKeyboardMapper` in `Emulators\TDV\`. Both classes exist and
do different jobs. Memory corrected.

## Appendix 0n: Phase 3 part 8 (2026-08-09) — the TDV leaves stop repeating each other

TDV1200, TDV2215 and TDV2200 each declared the same four components, built them with
**byte-identical** constructor code, and implemented the same five members on top of them:
`CharacterSetVariant`, `GetAvailableCharacterSetVariants`, `GetISO646LanguageCode`,
`KeyboardLights` and `ProcessInput`.

Checked line by line before touching anything: the three copies had **not** diverged — only their
doc comments differed. Worth stating, because it makes this a safe move rather than a bug fix. (A
first crude whitespace-normalised diff said they differed; it was counting the comments. Reading
the three side by side was what settled it.)

`TDVEmulatorBase` now owns the four components and all five members. Constructing the components
in the base constructor is safe because each one only stores the emulator reference — verified in
`Components\`, not assumed.

### What the build told me that I had not noticed

The first build failed with "already contains a definition". The base **already declared all five
members as virtual** — as do-nothing stubs whose own comments read *"derived classes override
this"*. So the shape was not "three leaves inventing the same thing"; it was "a base that declared
the contract, then declined to implement it, three times over". Those stubs are now the real
implementations, which is a smaller and more honest change than the one I started making.

Result: the three leaves lose 173 lines and keep only what actually differs between the models.

Tests: `tests\RetroTerm.Tests\TDV\TdvSharedBehaviourTests.cs` (14 tests). They assert the models
**agree** rather than re-asserting each model separately — per-model tests would have passed just as
happily with three implementations, which is how the triplication survived this long. One test
deliberately guards the other way, checking the models still differ on identity and scrollback
depth, so that pulling members up cannot quietly flatten them into each other.

Suite 3287 → 3301 passing, 41 skipped.

### The one Phase 3 item I am not doing, and why

"Extract Ecma48Core and DecPrivateModes into modules" is the last item, and I do not think it earns
its churn right now. The brief for this review said explicitly: *do not redesign code just for
architectural elegance.*

Everything Phase 3 has delivered so far was driven by a **concrete defect or a measurable
duplication** — unreachable insert mode, DECCKM that never reached the keyboard, four copies of
Ctrl+letter, a keyboard chosen by class name, 173 lines triplicated across the TDV leaves. Splitting
`TerminalEmulatorBase` into an `Ecma48Core` module has no defect behind it and no duplication to
remove; it would move working code between files and risk regressions in the one class every
terminal depends on, in exchange for a structure that is not yet under strain.

It becomes worth doing when there is a second real user for the extracted module — a terminal that
needs ECMA-48 handling *without* the VT100 assumptions baked in beside it. VT52 or Tektronix would
be that user. Doing it before then means guessing at the seam, and a seam guessed wrong is worse
than no seam. **Recommendation: defer to whenever the first non-VT-family profile lands, and let
that terminal's real needs decide where the cut goes.**

### Still open in Phase 3

Extracting Ecma48Core and DecPrivateModes into modules — **deliberately deferred, see appendix 0n**
for the reasoning; it should wait for the first non-VT-family profile to decide where the seam goes.
~~Converting the TDV chain leaf by leaf~~ — **done, see appendix 0n.**

**Phase 3 is otherwise complete.**
~~The profile-keyed keyboard factory~~ — **done, see appendix 0m.**
~~Moving the UI C0 fallbacks into Core encoders~~ — **done, see appendix 0l.**
~~The DECKPAM keypad mapping~~ — **done, see appendix 0k.**
~~DECCKM plumbing~~ — **done, see appendix 0j.**
~~The mode table~~ — **done, see appendix 0i.**
~~Unifying the two host-reply channels~~ — **done, see appendix 0h.**

## Appendix 0o: Phase 4 part 1 (2026-08-09) — one ISO 646 wire conversion, not three

A TDV renders bytes through its active national replacement set, so typing `ø` has to put the ASCII
position byte `|` on the wire — the terminal draws ø where it sees `|`. Send a Unicode `ø` and the
terminal shows whatever its font holds at that codepoint, which is not ø.

The review recorded "ISO646 conversion in Desktop, twice". The *table* was never duplicated — it has
always been one shared map in Core. What was duplicated is the **loop around it**, three times:
`TerminalCanvas`, `VirtualKeyboardWindow` and `VirtualKeyboardPanel`.

### A live divergence, not just repetition

The three copies had already drifted. The canvas converted a string of **any length**; the virtual
keyboard window tested `text.Length == 1` and converted only single characters. So a multi-character
paste or IME commit through that window went out **unconverted**, while the same text typed into the
terminal canvas converted correctly. Same user, same terminal, different answer depending on which
window had focus — the exact shape of the Ctrl+key problem in appendix 0l.

`TDVCharacterSets.ConvertToWireBytes(text, languageCode)` is now the single implementation. It
returns the input unchanged when the variant is International or when nothing in the text needs
substituting, so the common case allocates nothing.

### Two language sources, checked and left alone

The canvas takes the variant from the **emulator** (`GetISO646LanguageCode`); the virtual keyboard
takes it from its **selected layout** (`KeyboardLayout.GetLanguageCode`). Those are different things
and could in principle disagree.

They do not in practice: `MainWindow.ApplyLanguageVariant` sets the emulator's variant from the
keyboard/connection language, so the two are kept in step. Verified rather than assumed, and **left
alone** — collapsing them would be a behaviour change with no defect behind it, and the emulator is
not obviously the right authority for a keyboard the user picked independently.

Tests: `tests\RetroTerm.Tests\TDV\Iso646WireConversionTests.cs` (16 tests). Two are the point:
`MultiCharacterTextConvertsEveryCharacter` covers the regression, and
`ASingleCharacterAndTheSameCharacterInAStringConvertIdentically` states the property the three
copies violated — length must not change the answer. There is also a stability check, since the
call sites are layered and text can pass through conversion twice.

Suite 3301 → 3317 passing, 41 skipped.

## Appendix 0p: Phase 4 part 2 (2026-08-09) — the SS2/SS3 "merge" should not happen

The plan lists "merge the two SS2/SS3 implementations". There **are** two —
`TDVCharacterSetManager`'s SS2/SS3 flags, used by TDV2200, and TDV2215's own
`_pendingSingleShift` byte. Reading them side by side shows they are **not the same behaviour
written twice**:

- **TDV2200** picks its font from the *designated set type* (GraphicsI → 2, GraphicsII → 3);
- **TDV2215** picks it from a *locked set number*, and additionally suppresses control-code handling
  for codepoints below 0x20, because its charsets 3 and 4 hold real glyphs there.

`TDV2215Emulator`'s own class comment says as much: *"Character Sets (TDV2215/TDV2115 — different
from TDV2200!)"*. Merging them would flatten a documented model difference with no defect behind it.
**Recommendation: don't.** The plan item was written from the outside, before the two were compared.

### What the investigation did turn up

**The shared component is deliberately not driven from the input path.** `TDVInputProcessor` has a
long comment explaining that it used to call `HandleSingleShift` and write an already-mapped
character, which double-mapped: the bitmap renderer picks a glyph from the *raw* character plus the
cell's `FontNumber`, so a pre-mapped codepoint produced a cell with the right font and no drawable
glyph. Both models now inherit that component from the base (appendix 0n), so there is now a test
pinning that a single shift still takes effect exactly once.

**A real asymmetry on raw SI/SO.** TDV2200 overrides `HandleExecute` to intercept the raw C0 bytes
0x0E/0x0F and route them through `InvokeCharacterSet`, commented as necessary to keep `FontNumber`
correct. TDV2215 does not — those bytes reach `TerminalEmulatorBase.HandleExecute`, which sets the
VT-style `ActiveCharacterSet`, a *different variable* from the `_lockedCharacterSet` that 2215's
`HandleCharacter` actually reads. The ESC forms (`ESC n` / `ESC o`) do reach 2215's state; the raw
byte cannot.

Whether a real TDV2215 responds to raw SI/SO is a question for
`docs\TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md`, which I have not verified — so this is **recorded,
not fixed**.

### A test of mine that looked like proof and was not

The first draft split that finding into two tests, one per model — and both asserted `font == 0`.
They passed, and demonstrated **nothing**: the models happen to agree on the *observable* value
because G1 on a 2200 is US ASCII, so the font legitimately stays 0 either way. A reader would have
taken the pair as evidence of an asymmetry that the tests never touched.

They are now one honestly-labelled characterisation test that says what it cannot distinguish and
why. Making it conclusive would need G1 designated as a graphics set, which needs a TDV designation
sequence I have not verified — and guessing one to make a test look conclusive is worse than saying
plainly that it is not.

Tests: `tests\RetroTerm.Tests\TDV\SingleShiftAndLockingShiftTests.cs` (9 tests) pinning that a
single shift lasts exactly one character on both models, that a locking shift persists, that a
single shift overrides a lock for one character, and that reset clears a pending shift.

---

## Appendix 0q: Phase 4 part 3 (2026-08-09) — the bitmap renderer's national table was wrong, not just duplicated

The plan records *"Bitmap renderer contains a duplicate ISO646 remap table (:50-80)"*. It does. The
expectation going in was a copy that had drifted a little. What the table actually contained was
**four outright errors**, and the check that found them was one glance: two entries mapped to the
same position.

`BitmapFontRenderer.DrawCharacter` used to fall back, when the font had no glyph for a codepoint
above 127, to an inline `switch` from Unicode to an ASCII position. Against the ISO 646 tables in
`TDVCharacterSets` — and against `FontTDV2215`'s own header comment describing its ROM, which agrees
with them:

| character | renderer said | the ROM says |
|---|---|---|
| Æ | `^` (0x5E) | `[` (0x5B) |
| Ø | `` ` `` (0x60) | `\` (0x5C) |
| æ | `~` (0x7E) | `{` (0x7B) |
| ø | `` ` `` (0x60) | `|` (0x7C) |
| ß | `{` (0x7B) | `~` (0x7E) |

**Ø and ø both resolved to 0x60.** No character generator puts an upper and a lower case letter at
one position, so one of the two could never draw. That single collision is enough to condemn the
table without needing to check any other row.

It was also **variant-blind**: it applied Ä→`[` whether the terminal was Swedish, Norwegian or
International. Under Norwegian, Ä lives at `@`, not `[`. Under International there are no national
positions at all, so the old code would draw a `[` where a Ä appeared instead of drawing nothing.

### The fix

The mapping now lives in Core beside the tables it has to agree with:
`TDVCharacterSets.TryMapUnicodeToRomPosition(char, variant, out char)`, built from the same reverse
map `ConvertToWireBytes` uses (appendix 0o). Returns false for plain ASCII, for International, and
for characters the active variant has no position for — all cases where the caller must leave the
character alone.

`FontBase` now declares `CharacterSetVariant` (default 0); `FontTDV2200` and `FontTDV2215` override
their existing properties. `TerminalRenderer.SyncFontVariant` already pushes the emulator's variant
onto the font, so the renderer can read the live variant back through the base without type-testing
each font class. `BitmapFontRenderer` is down to *resolve, then draw bits*.

`TDVEmulatorBase.GetISO646LanguageCode` now delegates to
`TDVCharacterSets.GetLanguageCodeForVariant` — the same variant→language switch had been written
twice.

### Recorded, not fixed

`FontTDV2215.GetFontBits` returns null for anything ≥ 0x80, which is what makes the fallback matter
there. **`FontTDV2200` has no such guard** — a high value indexes straight into its ROM and returns
*some* glyph (0x00C4 lands in the character-set-2 region), so the fallback never runs on a 2200 and a
stray Unicode character silently draws an unrelated glyph rather than nothing. That is a separate
defect from the mapping table and is **not fixed here**; it is held by a test that says so.

Core's Norwegian table and `FontTDV2215`'s header comment also disagree at three positions — `@`,
`^`, `~` (Core: Ä, Ü, ü; the 2215 comment: unchanged, unchanged, ¦). They agree on every character
that matters here. **Not resolved** — it needs the TDV spec, which I have not verified.

Tests: `tests\RetroTerm.Tests\TDV\NationalGlyphPositionTests.cs` (12 tests), including a round trip
that asserts every entry of the forward ISO 646 table resolves back to its own position — which is
what stops the two drifting apart again. Suite 3326 → 3341 passing, 41 skipped.

Suite 3317 → 3326 passing, 41 skipped.

---

## Appendix 0r: Phase 4 part 4 (2026-08-09) — the terminal decides how text becomes bytes

`TerminalSession.SendInputAsync` called `Encoding.UTF8.GetBytes` for **every** session, whatever
terminal was attached. Right for a modern host behind an ANSI/VT session; wrong for an ND host
behind a TDV one, where the line is 8-bit and one typed character has to leave as one byte. Under
UTF-8 anything above 0x7F went out as **two** bytes and the host read two garbage characters.

The ISO 646 wire conversion (appendix 0o) hid this for the characters people actually type on a
Norwegian keyboard — Æ Ø Å are replaced with ASCII positions *before* they reach the send path — so
only everything else showed the symptom. That is why it survived this long.

`TerminalProfile` now carries a `TransportEncoding` (`EightBit` / `Utf8`) and an
`EncodeForTransport(string)`. TDV profiles are 8-bit; the VT family keeps UTF-8. **That second half
is a product decision, not a hardware fact** — a real VT100 was 7-bit ASCII, but these sessions
reach modern hosts where typing é into an ssh session has to work. Stated in a test so it is a
choice on the record rather than an accident. A new profile that says nothing defaults to UTF-8.

Characters above 0xFF become `'?'` on an 8-bit line — the substitution `Encoding.ASCII` makes. A
surrogate pair costs two. Visible beats silently vanished.

### The receive half, which was worse

Symmetry made me look at the incoming direction, and the same assumption was baked into the parser:
every byte at 0xA0 or above was treated as a **UTF-8 lead byte**. On an ND line that corrupted host
output two ways — a lone 0xC5 became U+FFFD and the byte was **lost**, and 0xC5 followed by anything
in 0x80-0xBF was folded into **one** wrong codepoint, so two characters of host output collapsed
into one glyph.

`EscapeSequenceParser.DecodeUtf8` now gates that branch, set from the profile.
**Scope, deliberately narrow:** only 0xA0 and above. The 0x80-0x9F range keeps being read as 8-bit
C1 controls on both settings — whether a TDV line should read those as data is a separate question I
have not verified against the TDV spec.

### A mistake worth keeping

The first draft set the flag in `TerminalEmulatorBase.ProcessData` and the TDV tests still failed:
`TDVEmulatorBase` **overrides `ProcessData` to bypass the base entirely**. A one-time hook in one
path would have left exactly the terminals this change is for on the wrong setting — and the send
half would have looked fine on its own, so the bug would have shipped behind a green suite. The
assignment now sits in a shared `ApplyTransportEncodingToParser()` called from both entry points.

Tests: `tests\RetroTerm.Tests\Terminal\TransportEncodingTests.cs` (16 tests), including a sweep
asserting the two encodings are byte-identical across all of 7-bit ASCII — switching a session's
terminal type must not change what ordinary typing puts on the wire. Suite 3341 → 3357 passing,
41 skipped.

### Still open in Phase 4

Nothing. ~~National mapping out of the font ROM and bitmap renderer~~ — done, appendix 0q.
~~Profile-controlled transport encoding~~ — done, this appendix. ~~Merging the two SS2/SS3
implementations~~ — assessed and **declined**, appendix 0p.

Carried forward as a separate defect, recorded in 0q: `FontTDV2200` has no guard for codepoints
above 0x7F, so a stray Unicode character indexes into its ROM and draws an unrelated glyph.

---

## Appendix 0s: Phase 5 part 1 (2026-08-09) — one palette, and a test that could not fail

Verified live bug 10: two 256-colour cubes that disagree.

- `TerminalRenderer` built its brushes from the xterm ramp — **0, 95, 135, 175, 215, 255**.
- `TerminalColor.IndexToRgb` computed `(idx / 36) * 51` — an even six-way split, **0, 51, 102, 153,
  204, 255**.

Both look like reasonable arithmetic. Only the first is what xterm does: the first step is
deliberately wider so dark colours stay distinguishable from black. The two agree on the 16 base
colours, agree on the grey ramp, and disagree on **214 of the 216** cube colours. The screen showed
one colour and Core answered a different one to anything that asked.

### The part worth remembering

The existing test, `Color_Cube_Should_Convert_Correctly`, checked index 16 and index 231 — cube
(0,0,0) and (5,5,5). Those are **exactly the two points where the two formulas agree**. It passed
for as long as the bug existed, and it was not a lazy test to look at: it named the right feature,
it checked both ends of the range, and it was wrong about the only thing that mattered.

The lesson is narrower than "test more". Both ends of a ramp are where interpolation schemes
*converge*; the discriminating samples are in the middle. The new tests sweep all 256 indices and
pin the ramp level by level.

### The fix

`TerminalPalette` in Core is now the only table. `TerminalColor.IndexToRgb` delegates to it, and
`TerminalRenderer` builds its 256 brushes from it in a loop instead of carrying a second copy of the
numbers.

### FluentAssertions removed while in there

The file holding that test was one of five still using FluentAssertions, which CLAUDE.md forbids
outright. All 149 `.Should().Be(...)` and its relatives across those five files are now xUnit
`Assert.*`, and the package reference is gone from `RetroTerm.Tests.csproj`. Same test count either
side of the change (3370), so nothing was silently dropped.

Tests: `tests\RetroTerm.Tests\Rendering\CanonicalPaletteTests.cs` (13 tests). Suite 3357 → 3370
passing, 41 skipped.

---

## Appendix 0t: Phase 5 part 2 (2026-08-09) — the blink timer that leaked and never blinked

Verified live bug 8, which is two defects with one cause: **nothing owned the renderer's lifetime.**

**The leak.** `TerminalCanvas.SetEmulator` builds a new `TerminalRenderer` on every terminal-type
change and used to simply overwrite the field. The old renderer's 500 ms `System.Timers.Timer` kept
running for the life of the process, holding that renderer — and through it the emulator, the
256-brush palette and the selection manager — alive. `TerminalRenderer.Dispose()` **existed the
whole time and had no callers**. A Dispose nobody calls is not a resource policy.

**The blink.** The timer flipped a bool and stopped. Nothing asked for a repaint, so a blinking
cursor only changed state when something *else* redrew the screen — never, on an idle session, which
is exactly when someone looks at the cursor. The renderer now raises `BlinkStateChanged` and the
canvas marshals a repaint, the same way it already did for `Invalidated`.

**A third one found next door.** The same re-entrancy that leaked the renderer also stacked
subscriptions: `_emulator.Invalidated += …` with no matching `-=`, so calling `SetEmulator` twice for
the same emulator meant two repaints per change. The `Bell` subscription three lines below already
guarded against precisely this and said so in a comment — the guard just never got applied to its
neighbour.

**Cost control.** A repaint twice a second per tab is not free, so `BlinkStateChanged` is raised only
when the last drawn frame actually used a blinking cursor style. The flag keeps flipping either way,
so switching *to* a blinking style needs no special handling: the repaint that draws it sets the
flag, and the next tick starts blinking. `_cursorVisible` is now read and written through
`Volatile` — it is written on the timer thread and read on the UI thread.

### Verified red before green

The leak test was run against the code with the `Dispose()` call removed and **failed**
(`TheReplacedRenderersTimerIsStopped`), then passed with it restored. Worth doing rather than
assuming: this session has already produced two tests that passed while demonstrating nothing.

### Recorded, not fixed

`TerminalCanvas` still has **no end-of-life cleanup at all** — no `OnDetachedFromVisualTree`, no
`Dispose`. The renderer is now released when it is *replaced*, but not when the control itself goes
away. I did not add a detach hook because Avalonia detaches and reattaches controls during ordinary
tab switching, and killing the renderer there would break the app in a way this change is not
allowed to risk. It needs someone who knows how the tabs are hosted to say where teardown belongs.

Tests: `tests\RetroTerm.Tests\Avalonia\CursorBlinkLifetimeTests.cs` (8 tests). Suite 3370 → 3378
passing, 41 skipped.

## Appendix 0u: Phase 5 part 3 (2026-08-09) — the render hot path

"Cached brushes" from the Phase 5 list, which on inspection was three separate problems in
the same loop rather than one.

**What was there.** `TerminalRenderer.Render` built three collections on every frame before
drawing a single cell:

- `_selectionManager.GetSelectedCells().ToHashSet()` — an iterator yielding one tuple per
  selected cell (1,920 of them for a full-screen selection on 80x24), boxed into a HashSet.
  Worse, the character-selection path calls `FindLastNonWhitespaceColumn(row)` per row, so
  building the set also **rescanned the buffer** every frame.
- two more HashSets of `(row, col)` for search matches, rebuilt whether or not the matches
  had changed.
- `new ImmutableSolidColorBrush(...)` for the selection, current-match and other-match
  highlights — **inside the per-cell loop**, so a brush per highlighted cell per frame, for
  three colours that never change.

It also used `.ToHashSet()` and `foreach` on the hot path, both forbidden by this project.

**What it is now.** Selection and search are answered **per row**, not per cell.
`SelectionManager.TryGetSelectedColumnRange(row, out start, out end)` is new Core API — the
question belongs with the selection, not with the renderer, which had no business
rebuilding a set to answer it. Search matches are scanned linearly per row; a search
produces a handful of ranges, and scanning them beats building a set of every matched cell.
The three brushes are `static readonly`. Per frame the allocation count for this work goes
from "a HashSet per collection plus a brush per highlighted cell" to zero.

**The test is an AGREEMENT test, and the first version of it proved nothing.**
`SelectedColumnRangeTests` sweeps eleven selection shapes (single cell, full width, whole
screen, spans across blank rows, backwards drags) in both character and rectangular mode,
and for each walks **every cell of the buffer** demanding the new range query and the old
`GetSelectedCells` agree. Walking every cell rather than only the selected ones is
deliberate: a range that is too WIDE is as wrong as one too narrow.

The red-before-green check caught a flaw in the check itself, recorded because it is a
shape that keeps recurring. Removing the trailing-whitespace trim to watch the test fail —
and it still passed, 36 green. The reason: the line removed appears in BOTH methods, so the
edit broke them identically and they still agreed. **An agreement test can only be verified
by breaking ONE side.** Targeting the new method alone turns it red at once, naming the
exact cell (`row 1 col 13: GetSelectedCells says False, TryGetSelectedColumnRange says
True`). 3680 passing, 41 skipped.

**Still open in Phase 5.** Glyph atlas; dirty-row redraw (needs a cached-bitmap renderer
first); presentation themes over the canonical palette; honouring the DW/DH and SGR blink
CELL attributes — verified absent, not assumed: `CharacterAttributes` carries `Blink`,
`RapidBlink`, `DoubleWidth`, `DoubleHeightTop`, `DoubleHeightBottom`, and
`TerminalRenderer` reads none of them. Cursor blink is handled (appendix 0t); the SGR blink
attribute on a cell still renders steady.

## Appendix 0v: Phase 5 part 4 (2026-08-09) — blink, which was missing in three places

"Honour blink" from the Phase 5 list turned out to be three independent gaps that each hid
the others.

1. **SGR 6 had no case at all.** `TerminalEmulatorBase`'s SGR switch went 5, 7, 8 — no 6. So
   `CharacterAttributes.RapidBlink`, which the flags enum has carried from the start, could
   never be set by anything in the codebase. A host asking for rapid blink got no blink.
2. **SGR 25 cleared only `Blink`.** ECMA-48 SGR 25 is "steady (not blinking)" and ends both
   rates. This mattered little while nothing set `RapidBlink`; fixing (1) without (2) would
   have shipped a rapid blink with no way to switch it off.
3. **`TerminalRenderer` read neither flag.** Cells the emulator correctly marked — via SGR 5
   or via `TDVRectangleOperations`, which sets `Blink` on rectangles — rendered permanently
   steady. Verified by grep before any code was written: both attributes had writers and no
   readers.

**Rates.** One timer drives everything at 500 ms. Rapid flips every tick, slow every second
tick: 120 and 60 flashes per minute. ECMA-48 puts the boundary at 150/minute (slow "less
than", rapid "at least"), so the rapid rate here sits at the slow end of what SGR 6 permits
— chosen because a faster flash on a modern display is unpleasant rather than authentic.

**Text blink does NOT share the cursor's phase.** Separate flags on the same timer. A cursor
flashing in lockstep with the text under it is how no real terminal behaved.

**Blink removes the glyph, not the cell.** The background is already painted when the blink
test runs, so a blinked-off cell keeps its colour and loses only its character — otherwise a
reverse-video blinking run would flash holes in the line. Pinned by
`ABlinkedOffCellKeepsItsBackground`.

**Repaint economy follows the cursor rule from appendix 0t.** `Render` recomputes
`_frameHasBlinkingText` from scratch each frame — a cell that stopped blinking must stop
costing repaints, and only the completed sweep knows that — and the timer stays silent when
neither the cursor nor any cell blinks.

**Tests are pixel-level, through the production path.** `BlinkAttributeRenderingTests` (11)
asserts on real rendered pixels via `RenderedScreenshot.CellHasInk`, because "did the glyph
disappear" is a question about what was drawn, not about what a flag says. `Capture` gained
an optional `configureRenderer` hook for it: blink phase is time-based state that cannot be
reached through the emulator. The hook uses reflection **inside the test helper** rather
than adding a test-only accessor to `TerminalCanvas` — the principle `RenderedScreenshot`
already states about not bending production controls for tests.

Red-before-green: with the renderer's blink check removed, `BlinkingText_DisappearsInThe
OffPhase` and `TheTwoRatesAreIndependent` fail and the other nine still pass — the two that
can only pass because of the fix. 3691 passing, 41 skipped, 0 warnings.

**Still open in Phase 5.** Glyph atlas; dirty-row redraw (needs a cached-bitmap renderer
first); presentation themes over the canonical palette; **DW/DH**, which is now the last
attribute gap and a genuinely bigger job than blink — double width means drawing a glyph at
2x and consuming two cells, double height means drawing the top and bottom halves of one
glyph across two rows, so it changes geometry rather than visibility. Also worth noting:
only the TDV path sets `DoubleWidth`/`DoubleHeight*` today. The VT DECDWL/DECDHL sequences
(`ESC # 3/4/5/6`) appear to be unimplemented in the emulator — recorded as observed from
the writer list, not verified against the sequence handler.

## Appendix 0w: Phase 5 part 5 (2026-08-09) — DW/DH, the model half

Double-width/double-height is the last attribute gap, and it is two jobs: getting the MODEL
right, and drawing it. This is the model half. The rendering half changes glyph geometry —
2x draw consuming two cells, top and bottom halves of one glyph across two rows — and is
better done against a settled model than alongside one.

**Three findings, all verified before any code was written.**

1. **Only the TDV models honoured ESC #.** `HandleDoubleWidthHeight`, `SetLineHeight` and
   `SetLineWidth` lived in `TDVEmulatorBase`, so a VT100 or ANSI session ignored
   DECDHL/DECDWL/DECSWL entirely — even though `CharacterAttributes` has carried
   `DoubleWidth`, `DoubleHeightTop` and `DoubleHeightBottom` from the start. Nothing about
   the behaviour is TDV-specific; the old implementation's own comment cited the VT220 spec.
   Outside the TDV tree, `DECDWL`/`DECDHL` appeared only in `EscapeSequenceDecoder`, which
   names them for the protocol monitor and does not act on them.

2. **THREE copies of that one behaviour.** The TDV base, an override in `TDV2200Emulator`,
   and an override in `TDV1200Emulator` fanning out to four one-line private helpers. They
   had not diverged, with one exception: TDV2200 called `SetLineWidth(Single)` after
   `SetLineHeight(Single)` on ESC # 5, clearing `DoubleWidth` a second time —
   `SetLineHeight` already clears all three double-size attributes. Same shape as commit
   `3d48957` ("Stop the three TDV models repeating each other").

3. **Double size was recorded TWICE per cell.** Bits 0-1 of `TerminalCell._flags`
   (`DoubleWidth` / `DoubleHeight` properties) *and* the `CharacterAttributes` flags. One
   method wrote both; **nothing in production read the bits** — only tests did — so the two
   could drift apart without a single test noticing.

**What it is now.** `TerminalEmulatorBase.HandleLineSize` owns ESC # 3/4/5/6 for every
terminal, with `SetLineHeight`/`SetLineWidth` beside it; `LineHeight` and `LineWidth` moved
out of the TDV namespace, where they had been described as "line height modes for TDV
terminals". The TDV override of `HandleEscapeWithIntermediates` now delegates `#` to the
base. The cell's `DoubleWidth`/`DoubleHeight` are **derived** from the attributes, joined by
new `DoubleHeightTop`/`DoubleHeightBottom` — the attributes win because a bit cannot say
which half of a double-height line a cell shows, and the renderer must know. Two `_flags`
bits are now free.

**Tests.** `LineSizeSequenceTests` — 60 cases, ten behaviours across all six emulator types
the factory builds, so a new terminal is covered the moment it is registered. Including the
negative: ESC # 8 is DECALN, shares the `#` intermediate, and must not be swallowed as an
unknown line size. Also that `FontNumber` survives a line-size change, since the freed bits
sit in the same packed byte and a stray write there would select glyphs from the wrong ROM
set.

Red-before-green: with the base's `#` branch removed, 36 of the 60 fail. 3751 passing.

**A method-cut mistake worth recording.** Removing the duplicated members by line range
swallowed the *signature* of the next method, leaving its body orphaned — the compiler
caught it, but the recovery was `git show HEAD:<file>` for the exact original rather than
retyping it from memory. Same family as the appendix 0 lesson about auditing a whole
region's member list: a line range does not know where a member ends.

**Still open in Phase 5.** Glyph atlas; dirty-row redraw (needs a cached-bitmap renderer
first); presentation themes over the canonical palette; and the DW/DH RENDERING half, now
the only attribute left that the renderer ignores.

## Appendix: verified live bugs found during this review (independent of any refactor)

1. 8-bit C1 controls unreachable — `EscapeSequenceParser.cs:178` vs `:190` (verified by hand).
2. VT DSR/CPR responses generated and dropped; Primary DA absent — `TerminalEmulatorBase.cs:1020-1023` + no `DataToSend` subscriber (verified by hand).
3. DCS wedges on any parameterized DCS; payload discarded; `ESC \` never unhooks — `EscapeSequenceParser.cs:518-548` + `:155` (verified by hand).
4. TDV2215 loses scroll-at-region-bottom — impossible wrap condition `TDV2215Emulator.cs:612` (agent-verified against `TerminalEmulatorBase.cs:254`).
5. TDV1200 in 2115 mode silently swallows cursor movement/erase — `TDV2115CompatibilityMode.cs:124-150`.
6. Unconditional allocation storm per escape sequence in TDV logging — `TDVEmulatorBase.cs:389-391`.
7. Renderer/UI-thread data race on the live buffer — `TerminalCanvas.Render` vs pump contract (`ScreenReader.cs:11-14`).
8. ~~Leaked blink timer per emulator swap; blink never repaints~~ — **FIXED**, appendix 0t (plus a stacked `Invalidated` subscription found alongside). Canvas end-of-life teardown still absent.
9. `TerminalCell` doc says 16 bytes, actual 20 — `TerminalCell.cs:9`.
10. ~~Two 256-color cubes disagree (`*40+55` vs `*51`)~~ — **FIXED**, appendix 0s: one `TerminalPalette` in Core, both callers read it.
