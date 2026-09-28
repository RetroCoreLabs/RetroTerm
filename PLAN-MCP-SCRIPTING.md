# Plan: MCP + Scripting support for RetroTerm

**Written:** 2026-08-04
**Input spec:** `HANDOVER-MCP-TERMINAL-CONTROL.md`
**Decisions made (Ronny, 2026-08-04):**
- MCP server lives **inside RetroTerm.Desktop** (localhost TCP/HTTP, like the Unity model). All logic in Core so a headless host can be added later.
- Script library format: **simple text DSL** (line-based SEND / WAITFOR / etc.), not JSON.

**Success test for the whole plan:** an LLM (via MCP) can connect to a live ND machine,
log in with a stored script (ESC → wait `ENTER` → user → wait `PASSWORD:` → password),
run a command, wait for the `@` prompt on the RENDERED screen, read the screen back —
and if the connection drops mid-way, no dialog blocks the app; the MCP client is told,
the script stops, and the tab status shows disconnected.

---

## Phase 1 — Core foundation: threading architecture, read the screen, wait for the screen

Everything else depends on this. All code in `RetroTerm.Core` (netstandard2.1, headless already — verified).

**Threading requirement (Ronny, 2026-08-04):** all communication and emulation runs OFF the
UI thread, the UI↔communication boundary is safe by design, and today's hacks are replaced
with a real architecture — not patched.

| # | Task | Notes |
|---|------|-------|
| 1.0 | **Session threading architecture — single-writer pump** | Target model: each `TerminalSession` has ONE processing context that is the only thing allowed to mutate the emulator/buffer. Connection receive thread does nothing but hand bytes to a `System.Threading.Channels` channel (bounded, backpressure); a per-session pump task drains it and calls `ProcessData`. Script/MCP wait+read run on that same serialized context (or read a snapshot it publishes), so buffer access needs no locks in the hot path. UI never touches the buffer directly from events — it gets `DisplayInvalidated` and reads via a snapshot/`Dispatcher.UIThread` as it does today, but the contract is now explicit. Audit and remove today's hacks: `async void` event handlers in `TerminalSession`, UI reading the live buffer while the network thread writes it, `Dispatcher.UIThread.Post` sprinkled as the only safety. Document the rule: **network thread produces, pump thread owns the emulator, UI and MCP consume snapshots/events.** |
| 1.1 | **Thread-safe screen reads on top of 1.0** | With the single-writer pump, reads are either (a) executed as a small job on the pump context, or (b) taken from an immutable snapshot the pump publishes after each batch (`GetSnapshot()` exists but allocates a `TerminalCell[,]` — measure; may need a pooled/double-buffered snapshot to respect the zero-allocation rule). Decide a+measure, then implement. |
| 1.2 | **ScreenReader** | Buffer → text: visible screen as string rows + cursor row/col, `stripTrailingBlanks` option, scrollback line access, "screen tail" helper. For-loops over `TerminalCell`, no LINQ. Lives in `Core\Terminal\Buffer\` or `Core\Session\`. |
| 1.3 | **TerminalSession additions** | (a) raw-bytes send (`SendBytesAsync(ReadOnlyMemory<byte>)`) so ESC is first-class; (b) re-exposed `DataReceived` event (raw incoming bytes) for MCP forwarding — subscribers survive reconnects because they subscribe on the session, not the connection; (c) counters: bytes in/out, timestamp of last received byte (for `terminal_status` and `idleMs`). |
| 1.4 | **WaitForScreenAsync primitive** | Params: pattern (literal/regex), where (screen-tail / anywhere-on-screen / scrollback), timeoutMs, idleMs ("screen unchanged for N ms"). Triggered by `DisplayInvalidated`. Returns: matched?, what matched, screen snapshot at that moment, elapsed ms. **On timeout still returns the screen** (handover rule 4) and the elapsed time (rule 5). |
| 1.5 | **Unit tests** for 1.1–1.4 with `InMemoryConnection` | Including: prompt arrives split across chunks; prompt appears mid-listing but not at screen tail (must NOT match with `screen-tail`); timeout returns partial screen; idleMs fires. |

**Done when:** tests pass 100%, `dotnet build RetroTerm.sln` clean (warnings = errors).

## Phase 2 — Remove the blocking disconnect dialog

Small, independent, immediately useful — do it early.

| # | Task | Notes |
|---|------|-------|
| 2.1 | Remove the two `await ShowDisconnectionDialog(...)` calls | `MainWindow.axaml.cs:566` (remote close) and `:600` (connection error). Keep: status dot update, `StopSessionLogging`, `UpdateStatus("Disconnected")`, title refresh. Show the reason in the status bar / write a line to the terminal instead of a modal. |
| 2.2 | Add a disconnect notification seam | Session/tab-level event the MCP layer and script runner subscribe to: "connection dropped (reason)". Wired in phase 3/4; the event exists now. |
| 2.3 | Decide fate of `ShowDisconnectionDialog` + `SuppressDisconnectDialog` | If nothing calls the dialog any more, delete it; the suppress flag may still gate the SSH host-key path — verify before removing. |

**Done when:** pulling the network cable / killing the server shows status "Disconnected" with no modal window; build clean.

## Phase 2.5 — Command registry: one seam for all functionality

**Extensibility requirement (Ronny, 2026-08-04):** adding a new command or capability must be
cheap — one new class, registered once, and it is available everywhere.

| # | Task | Notes |
|---|------|-------|
| 2.5 | **`ISessionCommand` + registry in Core** | Every operation (connect, disconnect, send, send-raw, wait-for, read-screen, status, ...) is one self-contained command class: name, parameter definition, `ExecuteAsync(TerminalSession, args)` running on the session's pump context, typed result. A `CommandRegistry` holds them (explicit registration or reflection discovery — `ConnectionFactory` already uses reflection, same style). **All three fronts dispatch through the registry:** MCP tool calls map tool name → command; script DSL verbs map verb → command; UI actions may call the same commands. Adding functionality later = write one command class + register it — it appears in MCP, in the DSL, and is unit-testable in isolation with `InMemoryConnection`. No LINQ, netstandard2.1. |
| 2.6 | **Self-describing commands + dynamic help generator** | Command metadata carries everything help needs: one-line summary, per-parameter name/type/required/default/description, and at least one usage example in DSL syntax. A `CommandHelpGenerator` in Core walks the registry and produces help text — used by (a) a `HELP [verb]` script verb / console command, (b) an MCP `terminal_help` tool so an LLM can discover the verb set at runtime, and (c) the script editor's help panel. **Help is always generated from the live registry, never hand-written** — a new command brings its own docs or fails registration (metadata validated: no empty description, params documented). |

**Done when:** phase-1 primitives are wrapped as the first command set, a unit test proves a
dummy command registered in one line is callable through the generic dispatch path, and the
generated help for that dummy command shows its description, params and example without any
help-specific code being written for it.

## Phase 3 — Script engine + library (text DSL)

In `RetroTerm.Core\Scripting\` — headless, no UI dependency.

| # | Task | Notes |
|---|------|-------|
| 3.1 | **DSL definition + parser** | Line-based, hand-written parser (no LINQ). Draft verbs: `SEND "text"`, `SENDRAW 1B` (hex bytes / `ESC` keyword), `WAITFOR "pattern" [REGEX] [TAIL|SCREEN|SCROLLBACK] [TIMEOUT 30s]`, `WAITIDLE 500ms`, `SLEEP 2s` (the discouraged exception), `LABEL`/comments (`#`). Parser errors report line number + text. Verb set to be confirmed against `ndterm.ps1` usage before freezing. **Verbs resolve through the phase-2.5 command registry** — a new registered command is automatically a new script verb; the parser stays generic (verb + args), it does not hard-code the command list. |
| 3.2 | **Script runner** | Runs steps sequentially against a `TerminalSession` using the phase-1 primitives. **Stops on the failing step and leaves the session live** (handover: "a script that stops on the failing step and hands the live session back is the ideal"). Per-step transcript: step text, elapsed ms, matched text, screen on failure. Cancellable. Subscribes to the phase-2 disconnect event → aborts with "connection dropped at step N" + screen (rule 6). |
| 3.3 | **Script library** | Store as `.rts` (RetroTerm script) text files in `%AppData%\RetroTerm\scripts\` (same pattern as key bindings). List / load / save. Name = filename. |
| 3.4 | **Unit tests** | Parser (good + broken scripts), runner against `InMemoryConnection` (happy path, failing step returns partial transcript + live session, disconnect mid-script). |
| 3.5 | **Script editor window (Desktop)** | Avalonia window for the script library: list/create/rename/delete `.rts` scripts, text editor pane, **live syntax validation** using the SAME Core parser (errors listed with line numbers, click jumps to the line — no second validation implementation in the UI), and a help panel fed by the phase-2.6 `CommandHelpGenerator` (verb list from the live registry; selecting a verb shows params + example, double-click inserts a template line). Optional stretch: run the open script against the active tab's session with the per-step transcript shown. UI layer stays thin — parsing, validation and help all come from Core. Remember the Avalonia DataGrid `StyleInclude` rule if a grid is used. |

**Done when:** the 5-step SINTRAN login from the handover runs as a stored script against the TestServer or an InMemory fake; tests 100%.

## Phase 4 — MCP server inside RetroTerm.Desktop

Thin host over Core; no business logic in the UI layer (project rule).

| # | Task | Notes |
|---|------|-------|
| 4.1 | **Session manager seam** | MCP needs to address tabs/sessions by id across calls. `TabSession.Id` (Guid) exists; expose open/list/close through a manager the MCP layer calls, marshalled to the UI thread where needed (tab creation is UI work). |
| 4.2 | **MCP transport** | Localhost-only listener inside the desktop app (Unity-model, e.g. `127.0.0.1:<port>`). MCP over HTTP (streamable) so Claude Code can connect directly; if that fights the SDK, a stdio shim proxying to the TCP port. Port in settings. **NOT VERIFIED yet:** which .NET MCP server package fits netstandard/net9 here — must be checked at the start of this phase. |
| 4.3 | **Tool surface v1** | `terminal_open`, `terminal_close`, `terminal_list`, `terminal_send` (text + `sendEscape`/hex bytes), `terminal_wait_for`, `terminal_read_screen`, `terminal_status` (incl. time-since-last-byte). Thin wrappers: tool name + params → phase-2.5 command registry dispatch. New commands registered in Core surface as MCP tools without new plumbing. |
| 4.4 | **Tool surface v2** | `terminal_read_new` (scrollback since last read — needs a per-client read cursor), `terminal_run_script` (name from library or inline DSL text; returns per-step transcript, failing step, screen). |
| 4.5 | **Disconnect → MCP notify** | Wire the phase-2 event: active connection drops → running script aborted, session status updated, next MCP call (or a notification, if the transport supports it) reports "disconnected at ...". |
| 4.6 | **Integration test** | Against `RetroTerm.TestServer`: open telnet session via MCP tools, log in via script, run menu commands with wait_for, read screen. |

**Done when:** the handover author's three "priority" tools (open/send/close + wait_for-on-screen + read_screen) work end-to-end against a real machine, and the dropped-connection path informs instead of blocking.

## Phase 5 — Polish / later

- Human + LLM sharing one visible session (who owns the keyboard) — handover open question 3.
- Attribute-aware screen read (colors/protected fields) — "bonus" in the spec.
- Headless console MCP host (`src\RetroTerm.Mcp`) reusing the same Core code.
- Transcript logging via `ISessionDataLogger` for MCP-driven sessions.
- Serial/SSH transports through MCP (Telnet first; factory already supports the rest).

---

## Order and why

1. **Phase 1 first** — everything (scripts, MCP wait_for) sits on ScreenReader + WaitFor; and the thread-safety fix must exist before any non-UI thread reads the buffer.
2. **Phase 2 second** — independent, small, removes the daily annoyance now.
3. **Phase 3 before 4** — the runner is testable headless with unit tests; MCP then only wraps it.
4. **Phase 4** — the only phase with unverified externals (MCP server library choice), so it gets the verified core underneath it first.
