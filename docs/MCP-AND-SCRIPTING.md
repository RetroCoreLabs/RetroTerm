# MCP and Scripting in RetroTerm

RetroTerm can be driven by a program instead of a human: an LLM over **MCP** (Model
Context Protocol), or a stored **script** in RetroTerm's own script language. Both go
through the same machinery — the same commands, the same rendered-screen waits, the
same help system.

The core idea: **the screen is the truth.** Automation never matches patterns against
the raw byte stream (escape sequences, repaints and split packets make that guesswork).
It waits for text on the *rendered screen*, exactly what a human sees.

---

## 1. The MCP server

Every running RetroTerm hosts an MCP server on **localhost only**:

- URL: `http://127.0.0.1:5715/mcp` (streamable HTTP)
- Enable/disable and port: **Preferences → MCP tab** ("Enable MCP" + "MCP port").
  Changes take effect the next time RetroTerm starts.
- Preferences is the ONLY place the port comes from. The `RETROTERM_MCP_PORT` environment
  variable used to override it and was removed on 27 August 2026: a setting the dialog cannot
  show is a setting nobody can debug. Pinned by `NoEnvironmentVariableConfigurationTests`, which
  fails if any environment variable comes back into the app's configuration. The port is the
  preference if one is set, otherwise 5715.
- Starts with the app, stops with the app. Failure to bind (port in use) is logged and
  shown in the status bar; the app runs fine without it.

### Connect Claude Code

```powershell
claude mcp add --transport http retroterm http://127.0.0.1:5715/mcp
```

RetroTerm must be running when the LLM uses the tools. Every session the LLM opens is
a **visible tab** in the RetroTerm window — you watch it type.

### The tools

Session lifetime:

| Tool | What it does |
|------|--------------|
| `terminal_open` | Opens a PERSISTENT session → returns a `sessionId`. Three ways to say where: `name=<stored connection>` (opens it exactly as saved, serial included — no other parameters needed), `host`+`port` for ad-hoc telnet/ssh, or `protocol=serial` with `port_name` (+ `baud`, `data_bits`, `parity`, `stop_bits`, defaults 9600 8N1) for ad-hoc serial. Sessions survive across calls and turns — never open-and-close per command; reconnecting mid-program can hang the line. |
| `terminal_close` | Closes a session. |

**A serial port is exclusive.** `terminal_open` refuses a COM port a tab or pop-out window already
holds, and says which — because Windows gives the port to one owner, and a second open fails with
"Access to the path 'COM11' is denied", which reads like a device permissions problem and is not one.
The check is shared with the UI connect path (`MainWindow.SerialPortAlreadyInUse`) rather than copied
into each: it lived only in the UI path until 30 August 2026, and the day MCP learned to open serial
at all, one remote call could take the console port out from under a live session.
| `terminal_list` | Lists open sessions, so a later turn can find one opened earlier. |

Talking (one tool per registered command — the same set as the script language):

| Tool | What it does |
|------|--------------|
| `terminal_send` | Send EXACTLY the given text — nothing is appended. Put the carriage return in the text itself (`"LIST-FILES\r"`). |
| `terminal_sendraw` | Send bare bytes: hex pairs (`1B0D`) or the keyword `ESC`. ESC wakes a SINTRAN line and recovers a wedged one. |
| `terminal_waitfor` | **The important one.** Wait until a pattern appears on the RENDERED screen. `where`: `tail` (prompt matching, default), `screen`, `scrollback`. `regex=true` for regular expressions. A timeout still returns the screen and the elapsed time. |
| `terminal_waitidle` | Wait until the screen stops changing for N ms. |
| `terminal_readscreen` | The visible screen as plain text + cursor position. |
| `terminal_readnew` | Scrollback lines added since the last `readnew` + the current screen — for capturing long listings that scrolled past. |
| `terminal_status` | Connected?, emulator, size, bytes in/out, **time since last byte** (tells busy from broken). |
| `terminal_help` | The generated command reference. |
| `terminal_sendkey` | Send a named TDV function key (e.g. HJELP), mode-aware, from the key registry. |

Beyond the terminal — every registered command is also an MCP tool (`terminal_<name>`):

| Tool | What it does |
|------|--------------|
| `terminal_connect` / `terminal_disconnect` | Connect a stored name, ad-hoc host/port, or ad-hoc serial (`protocol=serial port_name=COM11 baud=115200 ...`); deliberate disconnect. |
| `terminal_connlist` / `terminal_connshow` / `terminal_connsave` / `terminal_conndel` | Manage the stored connections (same list as Quick Connect), including the onconnect/ondisconnect hooks. Serial connections use `port_name`/`baud`/`data_bits`/`parity`/`stop_bits`; giving `host`/`port` with `protocol=Serial` is refused by name, never silently dropped. |
| `terminal_sendfile` / `terminal_receivefile` | Kermit send/receive. |
| `terminal_reset` / `terminal_clear` / `terminal_echo` / `terminal_snapshot` / `terminal_logstart` / `terminal_logstop` | Emulator control, notes, screen snapshot, raw traffic log. |
| `terminal_tracestart` / `terminal_tracestop` / `terminal_traceread` / `terminal_traceclear` | The protocol monitor. Each entry has a unique, ever-increasing id; `traceread` with `sinceid=` polls incrementally (`[id]` per line) so a client only ever sees new entries. |
| `terminal_opcom` | The OPCOM debugger: `action=` for memory, registers per level, CPU stop/start/step/breakpoint, IOX, boot (all values octal). |
| `terminal_keybind` | TDV keyboard bindings: `action=` list/bind/unbind/defaults (source `vk=`+`mods=`, target `grid=`). |
| `terminal_gateway` | ND-100 gateway state: listening, port, emulator/disk connected, registered terminals with ident codes and free/in-use flags. |

Scripts:

| Tool | What it does |
|------|--------------|
| `terminal_run_script` | Run a stored script by `name` or inline DSL via `script`. Returns a per-step transcript with timings; stops at the first failing step and **hands the live session back** for single-command poking. |
| `terminal_scripts` | List stored scripts, or save one (`save` + `script`; parse-validated before saving). |

### Security

The server binds to `127.0.0.1` only — hard-coded, not configurable. This tool types
arbitrary commands into machines; it must never listen on an external interface.

---

## 2. The script language (.rts)

Line-based, one command per line. `#` starts a comment. Strings in double quotes,
with escapes: `\"` `\\` `\r` (CR) `\n` (LF) `\t` (TAB) `\e` (ESC) `\xHH` (hex byte)
`\NNN` (octal, up to `\377`). **SEND appends NOTHING** — a line terminator is always
written explicitly: `SEND "LIST-FILES\r"`. Arguments are positional (in the order
the command declares them) or named `key=value`.

The verbs are the SAME commands the MCP tools expose — `HELP` (in a script, or the
`terminal_help` tool, or the script editor's help panel) always shows the current,
generated reference. Adding a command to the registry automatically adds a verb, a
tool and its help entry.

```text
# SINTRAN login — no fixed delays anywhere, every step waits on the screen
SENDRAW ESC                                # wake the line
WAITFOR "ENTER" where=screen timeout=15000
SEND "MY-USER\r"                           # \r explicit — SEND appends nothing
WAITFOR "PASSWORD:" where=screen timeout=15000
SEND "SECRET\r"
WAITFOR "@" timeout=15000                  # tail match: prompt at end of output
```

**Every time value in the language is MILLISECONDS** — `timeout=`, `WAITIDLE`, `SLEEP`,
`ontimeout` budgets, all of them. `timeout=300` is 0.3 seconds; a 15-second wait is
`timeout=15000`.

Verb summary:

| Verb | Example |
|------|---------|
| `SEND` | `SEND "LIST-FILES\r"` — sends EXACTLY the text, nothing appended; escapes: `\r \n \t \e \xHH \NNN` |
| `SENDRAW` | `SENDRAW ESC` or `SENDRAW 1B0D` |
| `WAITFOR` | `WAITFOR "X-C:" timeout=30000` — timeout in **ms** (30000 = 30 s); `regex=true`, `where=tail\|screen\|scrollback`, `tailrows=N` |
| `WAITIDLE` | `WAITIDLE 500` — screen quiet for 500 **ms** |
| `READSCREEN` | `READSCREEN` — checkpoint: captures the screen into the run's transcript at this point |
| `READNEW` | `READNEW` — checkpoint: new scrollback since last READNEW, into the transcript |
| `STATUS` | `STATUS` |
| `SLEEP` | `SLEEP 1000` — 1000 **ms** = 1 s; **discouraged**, fixed delays are how logins race. Use WAITFOR. |
| `HELP` | `HELP WAITFOR` |
| `CONNECT` | `CONNECT "ND-lab"` (stored name), `CONNECT host=10.0.0.1 port=5001` (ad-hoc), or `CONNECT protocol=serial port_name=COM11 baud=115200 data_bits=7 parity=even` (ad-hoc serial, defaults 9600 8N1) |
| `DISCONNECT` | `DISCONNECT` — deliberate, does not trigger connection-lost handling |
| `CONNLIST` / `CONNSHOW` | `CONNLIST`, `CONNSHOW "ND-lab"` — the stored connections (same list as Quick Connect) |
| `CONNSAVE` / `CONNDEL` | `CONNSAVE "ND-lab" host=10.0.0.1 port=5001 emulator=TDV2200` — create/update; serial: `CONNSAVE "nexys" protocol=Serial port_name=COM11 baud=115200 data_bits=7 parity=even` (`host`/`port` with Serial is refused, not silently dropped); `CONNDEL "old"` |
| `SENDFILE` | `SENDFILE "C:\\files\\data.bin"` — Kermit send (start the remote receive first); `;`-separated for several |
| `RECEIVEFILE` | `RECEIVEFILE "C:\\incoming"` — Kermit receive into a directory |
| `SENDKEY` | `SENDKEY HJELP` — named TDV function key, mode-aware sequence from the key registry |
| `EMULATION` | `EMULATION type=TDV2200` — changes the terminal type on a LIVE connection, keeping the screen and the connection. |
| `UNHANDLED` | `UNHANDLED` — the sequences this session was sent and did not act on, the ND graphics modes and ReGIS commands included. The first thing to read when a screen looks wrong and nothing has failed. |
| `RESET` / `CLEAR` | Reset the emulator / clear screen + scrollback |
| `ECHO` | `ECHO "--- backup starts ---"` — local note, never sent to the host |
| `SNAPSHOT` | `SNAPSHOT "C:\\logs\\state.txt"` — save the rendered screen to a file |
| `LOGSTART` / `LOGSTOP` | `LOGSTART "C:\\logs\\session.txt" format=text` — raw traffic log (text/hex/raw) |
| `TRACESTART` / `TRACESTOP` | Start/stop the protocol trace — the same decoded stream the Protocol Monitor window shows |
| `TRACEREAD` | `TRACEREAD sinceid=1234 max=200` — decoded trace entries, each line prefixed `[id]`. Ids are unique and strictly increasing (they survive TRACECLEAR), so an MCP client polls incrementally: read, remember the reported next id, poll with `sinceid=` that id. `into=var` stores the next poll cursor. `raw=true` includes raw blocks, `hex=true` the hex column. |
| `TRACECLEAR` | Empty the trace buffer — ids keep counting, old cursors stay valid |
| `OPCOM` | `OPCOM action=readreg level=0 reg=P` — full OPCOM debug access (all values **octal**): `action=` status, passthrough, readmem/writemem/dumpmem, readreg/writereg (per `level`), readintreg/writeintreg, dumpregs/dumpintregs, ioxread/ioxwrite, stop/start/masterclear/step/breakpoint/escape, boot. `into=var` stores the octal result. Attaches an OPCOM handler if the window never opened one. |
| `KEYBIND` | `KEYBIND action=bind vk=72 mods=alt grid=G53` — TDV keyboard bindings: `action=` list, bind, unbind, defaults. A source key is a Windows virtual-key code (`vk=`) plus `mods=` (shift/ctrl/alt/meta); the target is a grid position (`grid=`, e.g. G53=HJELP), optional `shifted=`. Persists to `tdv-key-bindings.json` unless `save=false`. |
| `GATEWAY` | `GATEWAY` or `GATEWAY action=terminals` — ND-100 gateway state: listening, port, emulator/disk-worker connected, and each registered terminal with its ident code, name, logical device and free/in-use flag. `into=var` stores the terminal count. |
| `terminal_build` (MCP only) | No arguments, no session needed — which BUILD is answering: version, git commit, branch, whether the tree was dirty, and when it was built. The same line is in the MCP handshake. **Check it before trusting a test result:** more than one copy of RetroTerm can run from the same folder and only the first gets the port, so a caller can reach an old binary without noticing. |
| `SCREENSHOT` | `SCREENSHOT "C:\\shots\\after-regis.png"` — saves the screen as a PNG, with Sixel, ReGIS and vector graphics drawn in. `SNAPSHOT` and `READSCREEN` return TEXT, which on a session carrying graphics is a fraction of what is there. Waits `settle=` milliseconds (default 250) for the screen to go quiet and publishes the graphics first, so the picture is never one change out of date; `settle=0` takes the frame as it stands. |
| `LOCALKEY` | `LOCALKEY key=Left shift=true` — presses a key AT THE TERMINAL instead of sending it to the host. A key name (Left, Right, Up, Down, Enter, Escape, F1, Add, Subtract) or a single character to type. This is the only way to reach the ReGIS graphics input cursor and the zoom shortcuts: everything else here ends on the connection, and the terminal decides those for itself. |
| `ZOOM` | `ZOOM percent=300`, `ZOOM step=-1`, or `ZOOM` on its own to report it — the display magnification, 50 to 400 percent. Stepping walks the same ladder the dropdown offers. Giving both `percent` and `step` is refused rather than guessed at. |
| `PASTE` | `PASTE text="LIST-FILES\r"` — pastes the way the Paste menu does, so national characters are converted and bracketed paste is applied; leave `text` out to use the system clipboard. Not the same as `SEND`, which does neither. |

**Windows paths in strings:** `\` starts an escape, so paths need doubled backslashes
(`"C:\\logs\\x.txt"`) — a single `\l` is a parse error the editor flags immediately.

### Variables

```text
SET user "SYSTEM"                 # assign (SET works in the console too, persists there)
SEND "LOGIN $user\r"              # $name or ${name} expands at execution time; $$ = literal $
WAITFOR "VERSION ([A-Z])" regex=true into=ver    # into= captures regex GROUP 1
READSCREEN into=screen            # READ*/STATUS capture their output
IF $ver == "K"                    # operators: == != CONTAINS MATCHES (regex)
SEND "IS-VERSION-K\r"
ELSE
ECHO "unexpected version: $ver"
ENDIF
```

`IF` blocks nest; the parser validates structure (missing ENDIF, double ELSE) at edit
time. In the console, `VARS` lists the variables.

### Connection event hooks

A stored connection can name library scripts to run automatically. Set them in
**Connection → Manage Connections → Scripts tab** (two dropdowns listing the script
library), or from a script/console/MCP:

```text
CONNSAVE "ND-lab" host=10.0.0.1 port=5001 onconnect=nd-login ondisconnect=nd-reconnect
```

- **onconnect** runs right after the connection is established (auto-login).
- **ondisconnect** runs when the connection DROPS — remote close or failure, never a
  deliberate disconnect. A reconnect script can CONNECT again and log back in.

Hook progress and failures are written onto the tab's screen and the status bar.

Any step can take `optional=true` — its failure does not stop the script (for probing
steps like "dismiss a message if present").

### Control flow

Labels and jumps are part of the language (the runner interprets them; they never
touch the session):

| Verb | Meaning |
|------|---------|
| `LABEL name` | Declares a jump target. Executing it does nothing. |
| `GOTO name` | Jump, **no return**. |
| `GOSUB name` | Jump; the next `RETURN` comes back to the step after the GOSUB. Nests. |
| `RETURN` | Back to the step after the most recent GOSUB. `RETURN` without a GOSUB fails the script. |

`WAITFOR` and `WAITIDLE` take `ontimeout=name`: on timeout the script **branches**
to that label instead of failing — a timeout with a target is a planned path.
The parser validates every label at edit time (unknown target, duplicate label).
A tight jump loop with no waits is aborted after 100 000 executed steps.

```text
# wake a SINTRAN line: keep sending ESC until the ENTER prompt shows up
LABEL wake
SENDRAW ESC
WAITFOR "ENTER" where=screen timeout=2000 ontimeout=wake
SEND "MY-USER\r"

# subroutine example
GOSUB login
SEND "LIST-FILES\r"
GOTO end
LABEL login
SEND "MY-USER\r"
WAITFOR "PASSWORD:" where=screen
SEND "SECRET\r"
RETURN
LABEL end
```

### Running scripts

Four ways:

1. **Script Console** (View → Script Console): type single DSL commands — this is
   where `HELP` prints (commands AND the language level) — and `RUN <name>` runs a
   stored script with a live transcript. `SCRIPTS` lists the library, `CLS` (or
   Ctrl+L) clears, arrow keys walk history. The **Run on** picker at the top chooses
   which open tab the commands hit: "Active tab" follows focus, or pin a specific
   connection; the window title names the current target. History and `SET`
   variables survive closing and reopening the console (they reset on app restart).
   When a script stops on a failing step, poke at the same session here.
2. **Script Editor** (View → Script Editor): the **Run** button runs the open text
   (saved or not) and shows the per-step transcript. The toolbar's **Run on** picker
   works like the console's — follow the active tab or pin one; the status strip
   shows the current run target.
3. **MCP**: `terminal_run_script` with a stored `name` or inline `script` text.
4. Any MCP client / the console can save scripts with `terminal_scripts` / the editor.

### Rules the design enforces (learned the hard way)

**The failure that started all of this.** The script this replaced logged in to a SINTRAN
machine with fixed 1500 ms delays: send ESC, wait, send the user name, wait, send the password.
The ND had just restarted XMSG and its banner came late, so the user name went out before the
`ENTER` prompt existed, and from then on the login loop ran one step out of phase - the first
real command answered the `PASSWORD:` prompt and the machine went back to `ENTER`. The 13
configuration commands that followed each waited their full 120-second timeout: 28 minutes,
nothing applied, and a transcript that read as though the ND was broken. Every rule below
comes from that half hour.

1. **One persistent connection per session** — reconnecting mid-program wedges the line.
2. **ESC is a first-class operation** — it wakes and recovers SINTRAN lines.
3. **Never guess a duration** — `WAITFOR` is the normal way to sequence; `SLEEP` is the exception.
4. **A timeout still returns what the machine said** — partial output is the most valuable thing on a failure.
5. **Every wait reports how long it took** — 23 s vs 200 ms is often the whole diagnosis.
6. **Errors are never swallowed** — a dropped connection aborts a script naming the step, with the screen.

### The script library

Scripts are plain text files: `%AppData%\RetroTerm\scripts\<name>.rts`. The folder IS
the library — drop a file in with any text editor and it appears. No index, no metadata.

---

## 2.5 The MCP Log

**View → MCP Log** live-tails the MCP traffic: every tool call an LLM makes (name +
arguments, passwords masked) and every result going back (ok/error, elapsed time,
response text). This is how you watch what the LLM is doing without reading the tabs.
The header shows the server endpoint and the entry count. "Tail" (on by default)
keeps the view jumping to the newest entry — turn it off to read old entries without
the view fighting you; "Word wrap" wraps long lines.

## 3. The script editor

**View → Script Editor** in RetroTerm:

- manage the library (new / delete / save),
- **syntax highlighting** in the theme's code colors: verbs, "strings", `$variables`,
  `key=` argument names and `#` comments each get their own color, live as you type,
- edit with **live syntax validation** — the same parser the runner uses; errors are
  listed with line numbers and clicking one jumps to the line,
- a **help panel generated from the live command registry** — select a verb to see its
  parameters and example; double-click to insert the example into the script.

A script saved with errors is kept (work in progress is fine) but will not run until
the errors are fixed.

---

## 4. How it works / extending it

Threading and architecture are documented in the source:

- `src\RetroTerm.Core\Session\SessionPump.cs` lines 16 to 30 — the single-writer threading
  rule and the reasons for it: the network thread produces, one pump task per session owns
  the emulator and buffer and is the only code allowed to change them, UI and MCP consume
  snapshots and events or run a job on the pump through `RunAsync`. One consumer means no
  locks on the hot path; the bounded channel gives backpressure instead of unbounded growth.
- `src\RetroTerm.Core\Commands\` — `ISessionCommand` + `CommandRegistry`: **one new
  command class + one registration = a new script verb + a new MCP tool + generated
  help.** Registration rejects commands with missing documentation.
- `src\RetroTerm.Core\Scripting\` — parser, runner, library.
- `src\RetroTerm.Mcp\` — the MCP server (official `ModelContextProtocol` C# SDK).
- `src\RetroTerm.Desktop\MainWindow.Mcp.cs` — desktop hosting: tabs as sessions.

The field failure that motivated the design is told above, at the top of "Rules the design
enforces" in section 2.
