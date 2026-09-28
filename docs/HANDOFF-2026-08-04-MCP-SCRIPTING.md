# HANDOFF 2026-08-04 — MCP + Scripting in RetroTerm

Full path: `docs\HANDOFF-2026-08-04-MCP-SCRIPTING.md`
State at handoff: **build clean (warnings=errors), 3052 tests passing (2929 + 123 Kermit), 0 failed.**
Committed up to `3d8d4ab`; everything after (variables/IF, connection+Kermit+control commands,
hooks, MCP log, console, editor rebuild) is **UNCOMMITTED in the working tree**.

## What exists now

RetroTerm can be driven by an LLM (MCP) and by scripts. One extensibility seam powers it all:
`ISessionCommand` + `CommandRegistry` (`src\RetroTerm.Core\Commands\`) — **one command class,
registered once = a script verb + a console command + an MCP tool + generated help.**
Registration REJECTS commands with missing docs.

- **Threading rule (never violate):** network thread produces → `SessionPump`
  (`Core\Session\SessionPump.cs`, bounded channel, single consumer task) is the ONLY code that
  mutates emulator/buffer → UI/MCP consume events/snapshots. All reads via
  `TerminalSession.RunOnSessionThreadAsync` / `ReadScreenAsync`; `FlushAsync` is the
  deterministic test barrier (never `Task.Delay`).
- **Screen truth:** `WaitForScreenAsync` (`TerminalSessionWait.cs`) matches the RENDERED screen
  (tail/anywhere/scrollback, literal/regex, timeout+idle). Timeout still returns the screen and
  the elapsed time. Disconnect ends the wait and says so.
- **MCP server:** `src\RetroTerm.Mcp\` (ModelContextProtocol SDK 1.1.0; 2.0.0 exists upstream).
  Hosted in the desktop app (`MainWindow.Mcp.cs`), localhost only, `/mcp`, port from
  Preferences → MCP tab (default 5715), and nothing else — the `RETROTERM_MCP_PORT` override was
  removed on 27 August 2026. Every `terminal_open`
  is a visible tab. Claude Code: `claude mcp add --transport http retroterm http://127.0.0.1:5715/mcp`.
  Registered project-locally in `%USERPROFILE%\.claude.json` already.
- **Script language (.rts)** (`Core\Scripting\`): verbs from the registry + language level:
  `LABEL/GOTO/GOSUB/RETURN`, `SET name value`, `$var`/`${var}`/`$$`, `IF/ELSE/ENDIF`
  (`== != CONTAINS MATCHES`), `into=var` capture (WAITFOR → regex group 1), `ontimeout=label`,
  `optional=true`, `#` comments. Parser validates EVERYTHING at edit time with line numbers
  (labels, IF nesting, params). Runner: pc-loop, GOSUB stack, 100k-step runaway cap, stops on
  failing step and hands the LIVE session back, aborts on ConnectionLost naming the step.
- **Command set:** SEND, SENDRAW (ESC), SENDKEY (named TDV keys via TDV2200KeyRegistry,
  mode-aware; HJELP → `ESC[46_` verified), WAITFOR, WAITIDLE, READSCREEN, READNEW (per-session
  scrollback cursor), STATUS (time-since-last-byte), SLEEP (discouraged), RESET, CLEAR, ECHO,
  SNAPSHOT, LOGSTART/LOGSTOP, HELP, CONNECT (stored name or ad-hoc), DISCONNECT,
  CONNLIST/CONNSHOW/CONNSAVE/CONNDEL (same store as Quick Connect), SENDFILE/RECEIVEFILE
  (Kermit, desktop wires Preferences → Kermit options).
- **Connection hooks:** `HostConfiguration.OnConnectScript` / `OnDisconnectScript` (set via
  `CONNSAVE ... onconnect=name ondisconnect=name`). Desktop fires on-connect after
  `ConnectToHost` success; on-disconnect on `ConnectionLost` (remote drops ONLY — deliberate
  disconnects never fire it). One subscription per tab reading current params at fire time.
- **UI:** View → Script Editor (rebuilt IDE-style: toolbar New/Rename/Delete|Save/Run/Stop,
  line-number gutter, collapsible PROBLEMS panel from `ScriptParseError` objects, RUN TRANSCRIPT
  panel, status strip, dirty tracking + Ctrl+S/F5, theme-brush bound via `GetResourceObservable`);
  View → Script Console (interactive DSL, HELP prints here, RUN/SCRIPTS/VARS/CLS, history);
  View → MCP Log (live tool-call/result tail from `McpTrafficLog`, passwords masked).
  The blocking "Connection Lost" modal and connect-failure modals are GONE — status bar +
  on-screen notice only.
- **Docs:** `docs\MCP-AND-SCRIPTING.md` (user guide, kept current), `PLAN-MCP-SCRIPTING.md`
  (original plan, all 21 tasks done), `HANDOVER-MCP-TERMINAL-CONTROL.md` (the requirements).

## Remaining work (task #29, in progress)

Updated 2026-08-05 — items 1 and 2 are now DONE:
1. **Console + MCP Log theme quick wins** — DONE. Both windows use `BindTheme`/
   `GetResourceObservable`. MCP Log: header with endpoint + entry count, tail toggle,
   word-wrap toggle, Clear as a secondary button (ctor now takes the server URL).
   Console: Ctrl+L = CLS, target tab in the window title, history and SET variables
   are static (survive window close/reopen, reset on app restart).
2. **Syntax highlighting overlay** — DONE. `ScriptEditorWindow`: the TextBox's own
   Foreground is transparent (local value beats pseudo-class styles; caret bound to
   PrimaryTextBrush), a hit-test-invisible TextBlock with colored Runs sits ON TOP
   (so the selection rectangle below stays visible), scroll-synced both axes via the
   editor's inner ScrollViewer. Re-lexed on EVERY keystroke (not the 300 ms debounce —
   transparent text + delay = invisible typing). Colors are cached from resource
   observers (`Avalonia.Reactive.AnonymousObserver`, verified present in Avalonia
   11.3.11) so live theme switches re-color. Lexer: verbs (registry + language words),
   "strings", $vars, key= names, # comments.
3. **Not GUI-verified** — STILL OPEN, now covers the highlight overlay too. Launch the
   app and check: gutter sync while scrolling, glyph alignment of the colored overlay
   vs caret/selection, splitters, theme switch live-restyling, dirty-confirm flow,
   Stop button mid-WAITFOR, MCP Log tail toggle, console Ctrl+L.

### Semantics changes 2026-08-05 (Ronny's decisions — do not revert)

- **SEND sends EXACTLY the text.** The auto-CR and the `cr=` parameter are GONE.
  A line terminator is always explicit: `SEND "LIST-FILES\r"`. Same for terminal_send
  (the LLM puts the `\r` in the JSON string).
- **String escapes in the DSL:** `\" \\ \r \n \t \e (ESC) \xHH (hex) \NNN (octal ≤ \377)` —
  decoded at parse time (ScriptParser.TryReadValue), same dialect as
  EscapeSequenceFormatter. Unknown/bad escapes are parse ERRORS with line numbers.
- **HELP includes the language level.** `ScriptLanguageHelp` (Core\Scripting) is the
  single table for LABEL/GOTO/GOSUB/RETURN/SET/IF/ELSE/ENDIF/#/escapes/optional=/
  ontimeout=/into= — CommandHelpGenerator appends it to the overview and resolves
  single names (forgiving: "ontimeout" finds "ontimeout="); the editor panel reads
  the same table (its private copy was deleted).
- **"Run on" target picker** (`Views\SessionTargetSelector.cs`, a ComboBox): editor
  toolbar + console top strip choose which open tab commands run on. "Active tab"
  follows focus; picking a tab PINS it; a closed pinned tab falls back to active.
  Items refresh on dropdown-open; MainWindow feeds it via GetOpenSessions().

### Flaky test — root-caused and fixed (2026-08-05)

The one-off full-suite failure is `ScriptRunnerTests.Run_ConnectionLostMidWait_AbortsAndSaysSo`.
Root cause was a REAL race, not a test problem: `WaitForScreenAsync` observes the drop via
the connection's `StatusChanged` while `ScriptRunner` observes `session.ConnectionLost` —
two different events, so WAITFOR could fail with "Connection lost while waiting" before the
runner's flag was set, and the run reported a generic step failure instead of ConnectionLost.
Fix: `CommandResult.ConnectionLost` (set by WAITFOR/WAITIDLE on disconnect), and the runner
checks `flag || result.ConnectionLost`. 5 consecutive green full-suite runs since. NOTE: one
intermediate run showed a single failure whose name was not captured — if a flake reappears,
capture the test name with `-v n | Select-String "\[FAIL\]"` before anything else.

## Gotchas that cost time (do not rediscover)

- `SendBytesAsync` METHOD on TerminalSession collides with the `Transfer.SendBytesAsync`
  DELEGATE — qualify the delegate (`Transfer.SendBytesAsync`) at use sites.
- `RunAsync<T>` + `() => tcs.TrySetResult(x)`: the lambda returns bool and binds to the generic
  overload again → stack overflow. Statement-bodied lambda required.
- MCP SDK 1.1.0: `CallToolRequestParams.Arguments` is `IDictionary` (not IReadOnly);
  `WithHttpTransport` needs `using Microsoft.AspNetCore.Hosting` for `UseUrls`.
- Tokenizer: key=value splitting must require an IDENTIFIER before '=' or `!=`/`==` in IF
  conditions get eaten (`IsIdentifier` in ScriptParser).
- `Theme(...)` as a method name on a Window hides `StyledElement.Theme` — use `BindTheme`.
- The comment-separator lines (─ chars) in MainWindow.axaml.cs don't round-trip through
  Edit-tool matching reliably — anchor edits on code, not on those banners.
- `dotnet test` from repo root; never `cd`. One flaky full-suite failure was observed once
  (2026-08-04, name not captured) and never reproduced in 5+ runs.

## Verify-first notes

- MCP endpoint smoke test: POST initialize to `http://127.0.0.1:5715/mcp` with
  `Accept: application/json, text/event-stream` → SSE event with serverInfo "RetroTerm".
- E2E test exists: `tests\RetroTerm.Tests\Mcp\McpEndToEndTests.cs` (real HTTP client → real
  Kestrel → real telnet → real TestServer).
