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

Every running RetroTerm hosts an MCP server inside the program itself. Read from
`src\RetroTerm.Mcp\McpServerHost.cs` and `src\RetroTerm.Desktop\MainWindow.Mcp.cs`:

| Fact | Value |
|------|-------|
| Transport | MCP **streamable HTTP** (the official `ModelContextProtocol` C# SDK, `WithHttpTransport`). No stdio, no separate process. |
| Address | `http://127.0.0.1:5715/mcp` - the path is always `/mcp` |
| Bind address | `127.0.0.1` only, written into the code, not a setting. The tools type into live machines, so the server must never be reachable from the network. To drive it from another computer, use an SSH tunnel to the port. |
| Default port | **5715** |
| Turn it on or off, change the port | **File → Preferences... → MCP tab**: "Enable MCP" and "MCP port" (1 to 65535). It is on by default. A change takes effect the next time RetroTerm starts. Saved as `mcp-enabled=` and `mcp-port=` in `%APPDATA%\RetroTerm\preferences.txt`. |
| Anything else that sets the port | Nothing. There is no environment variable and no command-line flag. `RETROTERM_MCP_PORT` used to exist and was removed on 27 August 2026, because the dialog could show one port while the server listened on another. `NoEnvironmentVariableConfigurationTests` fails if one comes back. |
| Authentication | None. It answers on the local machine only. |
| Lifetime | Starts with the window, stops when the program closes. RetroTerm must be running, with its window open, while the tools are used. |
| Where sessions appear | Every session a client opens is a **visible tab** in the RetroTerm window, so you watch it type. View → MCP Log shows every call and its answer (section 2.5). |

**Two copies of RetroTerm running.** Only the first one gets the port. The second one cannot
listen on it: the start fails, the reason is written to the log and to the status bar
("MCP server failed to start: ..."), and that copy runs on without an MCP server. A client
therefore always reaches the copy that started first, which may be an older build than the
one you just started.

**Which build is answering.** The server reports its real build, never a fixed "1.0.0":

- in the MCP handshake, the server name `RetroTerm` and the version from `BuildIdentity.Short`;
- in the server instructions (below), the full line from `BuildIdentity.Describe()`:
  version, commit, branch, whether the tree had uncommitted changes, and the build date;
- from the tool `terminal_build`, which needs no session: the same line, then `version:`,
  `commit:`, `branch:`, `built:`, and a NOTE line when the build was made from a tree with
  uncommitted changes.

**The server instructions**, sent to every client at the handshake, word for word from
`McpServerHost.cs` (the build line is filled in at run time):

> RetroTerm terminal control. THIS SERVER IS *build line*. Check that against the build you
> expect before trusting a test result - more than one copy of RetroTerm can be running and
> only the first gets this port. Open a PERSISTENT session with terminal_open, then drive it
> with terminal_send / terminal_waitfor / terminal_readscreen. Sequence steps by waiting on the
> RENDERED screen (terminal_waitfor), never with fixed delays. On a fresh SINTRAN connection
> send ESC first (terminal_sendraw bytes=ESC). terminal_help lists the command set.

---

## 1.1 Connecting a client

Every example uses the default port, 5715. If you changed the port in Preferences, change it
in the URL too. RetroTerm must be running when the client uses the tools.

### Claude Code

One command line. Checked against the Claude Code documentation,
[code.claude.com/docs/en/mcp](https://code.claude.com/docs/en/mcp), on 24 November 2026:

```powershell
# for this project folder only (the default scope, "local"; kept in ~/.claude.json)
claude mcp add --transport http retroterm http://127.0.0.1:5715/mcp

# for every project you open
claude mcp add --transport http --scope user retroterm http://127.0.0.1:5715/mcp

# written into .mcp.json in the project folder, shared with everyone who uses the folder
claude mcp add --transport http --scope project retroterm http://127.0.0.1:5715/mcp
```

The `.mcp.json` form, the same thing written by hand in the project folder
(`docs\distribution-mcp.json` in this repository holds exactly this):

```json
{
  "mcpServers": {
    "retroterm": {
      "type": "http",
      "url": "http://127.0.0.1:5715/mcp"
    }
  }
}
```

Check it: `claude mcp list` in a shell shows `retroterm` with its state, or `/mcp` inside
Claude Code. The tools then appear as `mcp__retroterm__terminal_open` and so on.

`src\tools\add-mcp-to-claude-code.ps1` runs the second command line above (`--scope user`), so it
registers the server for every project. The release carries it, with `.mcp.json` and a README, beside
the program.

### Claude Desktop

Claude Desktop starts its local MCP servers as programs it talks to over standard input and
output (stdio), from its configuration file. RetroTerm's server is HTTP only, so Claude Desktop
needs a small bridge program between the two. The bridge used here is `mcp-remote`
([github.com/geelen/mcp-remote](https://github.com/geelen/mcp-remote)), which needs Node.js.

The configuration file, from
[modelcontextprotocol.io/docs/develop/connect-local-servers](https://modelcontextprotocol.io/docs/develop/connect-local-servers)
(Settings → Developer → Edit Config opens it):

| System | File |
|--------|------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Linux | `~/.config/Claude/claude_desktop_config.json` |

```json
{
  "mcpServers": {
    "retroterm": {
      "command": "npx",
      "args": ["-y", "mcp-remote", "http://127.0.0.1:5715/mcp", "--allow-http", "--transport", "http-only"]
    }
  }
}
```

`--allow-http` lets the bridge use a plain `http://` address, and `--transport http-only`
stops it trying the older SSE transport, which RetroTerm does not offer. Quit Claude Desktop
completely and start it again after editing the file. Its MCP logs are in
`%APPDATA%\Claude\logs` (`mcp*.log`).

I have not verified this Claude Desktop setup against a running RetroTerm; the file location
and the JSON shape come from the two pages above.

### VS Code (GitHub Copilot)

`.vscode/mcp.json` in the workspace, top-level key `servers`, from
[code.visualstudio.com/docs/agents/reference/mcp-configuration](https://code.visualstudio.com/docs/agents/reference/mcp-configuration):

```json
{
  "servers": {
    "retroterm": {
      "type": "http",
      "url": "http://127.0.0.1:5715/mcp"
    }
  }
}
```

For every workspace, the command **MCP: Open User Configuration** opens the user-level file,
which takes the same shape. The same page says newer Copilot set-ups also read
`~/.copilot/mcp-config.json`, with the top-level key `mcpServers` instead of `servers`.

### Cursor

`.cursor/mcp.json` in the project, or `~/.cursor/mcp.json` for every project, from
[cursor.com/docs/context/mcp](https://cursor.com/docs/context/mcp):

```json
{
  "mcpServers": {
    "retroterm": {
      "url": "http://127.0.0.1:5715/mcp"
    }
  }
}
```

### Any other MCP client

Give it the URL `http://127.0.0.1:5715/mcp` and choose the transport called "streamable HTTP"
(some clients just say "HTTP"). No key, no header, no token. A client that can only start
stdio programs needs a bridge such as `mcp-remote`, as for Claude Desktop above. SSE (the
older HTTP transport) is not offered.

---

## 1.2 The tools

**54 tools.** They are generated, not written by hand, so this list was taken from the source
on 24 November 2026 by reading every command class the desktop registers
(`GetOrCreateCommandRegistry` in `MainWindow.Mcp.cs`) and the hand-written list in
`RetroTermToolProvider.cs`:

- **6 hand-written tools**: `terminal_open`, `terminal_close`, `terminal_list`,
  `terminal_build`, `terminal_run_script`, `terminal_scripts`.
- **48 command tools**, one per command in the command registry, named `terminal_` + the
  command name in lower case. These are the same 48 commands the script language (section 2)
  runs, with the same parameters, defaults and checks. A command added to the registry becomes
  a tool with no change to the MCP code.

The aliases `IND$PUT` and `IND$GET` are not tools: an MCP tool name cannot hold a `$`, so they
exist in scripts only, and over MCP the same two commands are `terminal_hostsend` and
`terminal_hostreceive`.

`terminal_help` returns the same reference, generated from the running build - use it when
this page and the program disagree, and tell us, because then this page is wrong.

### How every command tool behaves

- **`sessionId` is required** on every command tool: the id `terminal_open` returned. It is the
  only argument that is not a parameter of the command.
- **The schema comes from the command's own description**: the type (`string`, `integer`,
  `boolean`), whether it is required, the default (added to the description as
  "(default N)"), and for a parameter with a fixed set of words, a JSON Schema `enum`.
  SCREENSHOT's and RECORDSTART's `area`, and CONNECT's and CONNSAVE's `protocol`, are the ones
  with such a list today; the hand-written `terminal_open` declares one for its `protocol` too.
- **The tool description** is the command's one-line summary followed by
  "Script DSL equivalent:" and the script example.
- **A wrong argument name is refused, and nothing runs.** On a command tool the answer is
  `SCREENSHOT has no parameter 'aera' - it takes: file, settle, area (see HELP SCREENSHOT)`,
  word for word the same as the script parser gives. A hand-written tool answers the same way
  from its own list, for example `terminal_open has no parameter 'hots' - it takes: name,
  protocol, ... (see tools/list)`. Before this rule a misspelled name was read as "not given"
  and the default was used without a word (BUGS.md B13, B14).
- **A wrong value is refused by name**: a whole number that is not one, a true/false that is
  not one (`true/false`, `1/0`, `on/off`, `yes/no` are accepted), a word outside the list.
  The hand-written tools check their values by the same rule and with the same words, so
  `terminal_open` with `"protocol": "telnt"` answers `terminal_open: 'protocol' must be one of
  telnet, ssh, tn3270, serial, opcomsimulator, got 'telnt'` and opens nothing.
- **Escapes**: SEND's, ECHO's and PASTE's `text` take the backslash escapes `\r \n \t \e \xHH
  \NNN` over MCP exactly as inside a quoted script string, so `"SYSTEM\r"` sends a carriage
  return, not two characters. WAITFOR's `pattern` is deliberately not decoded, so a regular
  expression like `\d+` arrives unchanged.
- **The result** is text: the error (if any), then the command's output, then
  `[took N ms]`. A failed command comes back with the MCP error flag set and the reason in the
  text; nothing fails silently.
- **All times are in milliseconds.** `timeout=300` is 0.3 seconds.

### The hand-written tools

| Tool | Arguments | What it does and returns |
|------|-----------|--------------------------|
| `terminal_open` | `name` (a stored connection; every other argument is then ignored), `protocol` (telnet, ssh, tn3270, serial, opcomsimulator; telnet when left out; any other word is refused by name. `gateway` is not offered here: a gateway connection is opened from the window, Connection menu), `host`, `port` (23), `emulator` (VT100), `width`, `height` (the terminal's own size: 80 by 24 for a VT, 80 by 25 for a TDV), `username`, `password` (ssh), `port_name`, `baud` (9600), `data_bits` (8), `parity` (none, odd, even, mark, space), `stop_bits` (1, 1.5, 2), `model` (tn3270 only: IBM-3278-2, IBM-3278-3, IBM-3278-4, IBM-3278-5; the model also fixes the screen: 80x24, 80x32, 80x43, 132x27), `codepage` (tn3270 only: the EBCDIC code page by IBM number, one of 37, 273, 277, 278, 280, 284, 285, 297, 500, 871, 1047, 1140-1149; 37 when left out) | Opens a **persistent** session as a new visible tab and returns `sessionId: <id>` and what it connected to. Three ways to say where: `name=`, `host`+`port`, or `protocol=serial` with `port_name`; `protocol=opcomsimulator` (the built-in OPCOM simulator) needs neither. `host`/`port` with serial is refused by name. Refused while a dialog is open in RetroTerm (a tab built under a modal dialog breaks the window), and refused for a COM port another tab already holds, naming that tab - a port has one owner, and the second open would otherwise fail with a misleading "Access to the path 'COM11' is denied". Keep the session across calls and turns; reconnecting in the middle of a program can hang the line. |
| `terminal_close` | `sessionId` (required) | Closes that session's tab, popped-out windows included. Returns `closed <id>`. |
| `terminal_list` | none | One line per open session, popped-out windows included: id, connected or disconnected, terminal type, title. `no open sessions` when there are none. |
| `terminal_build` | none, and no session needed | Which build is answering (see above). |
| `terminal_run_script` | `sessionId` (required), `name` (a stored script) or `script` (inline script text) | Parses the script first (parse errors come back without running anything), runs it, and returns `SCRIPT OK` or `SCRIPT FAILED`, step count and total time, then one line per step with its time; READSCREEN-type steps include their output, and a failure includes the screen at the failing step. The session stays open for single commands afterwards. |
| `terminal_scripts` | `save` (name), `script` (text; required with `save`) | Without arguments, lists the stored scripts. With `save`, parses the script first and saves it only if it has no errors, answering with the step count and the file it wrote. |

### The command tools

Required parameters are in **bold**. Defaults in brackets. Every one also takes `sessionId`.

**Typing and waiting**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_send` | **`text`** | Sends exactly the text, nothing appended. End with `\r` yourself. |
| `terminal_sendraw` | **`bytes`** | Hex byte pairs without separators (`1B0D`), or `ESC`. ESC wakes a SINTRAN line. |
| `terminal_sendkey` | **`key`**, `shift` [false] | Presses a named terminal key - see "Key names for SENDKEY" below. |
| `terminal_waitfor` | **`pattern`**, `regex` [false], `where` [tail] (tail, screen, scrollback), `timeout` [30000], `tailrows` [1] | **The one to sequence on.** Waits until the text is on the rendered screen. `tail` looks at the last non-blank rows, which is how a prompt is matched. A timeout still returns the screen and the time waited. |
| `terminal_waitidle` | **`ms`**, `timeout` [30000] | Waits until the screen has not changed for `ms`. |
| `terminal_sleep` | **`ms`** | Waits a fixed time. Discouraged: fixed delays are how logins race. |
| `terminal_paste` | `text` (left out: the system clipboard) | Pastes the way the Paste menu does: national characters converted, bracketed paste applied. Not the same as SEND, which does neither. |
| `terminal_localkey` | **`key`**, `shift` [false] | Presses a key AT THE TERMINAL, not sent to the host. A single character is typed; a longer word is an Avalonia key name (Left, Right, Up, Down, Enter, Escape, Tab, F1, Add, Subtract, D0). The only way to reach the ReGIS graphics input cursor and the zoom shortcuts. |

**Reading the screen**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_readscreen` | `strip` [true] | The rendered screen as plain text, with the cursor position. |
| `terminal_readnew` | none | Scrollback lines added since the last READNEW, then the current screen - for listings that scrolled past. |
| `terminal_status` | none | Connected or not, terminal type, screen size, bytes in and out, and the time since the last byte (tells busy from broken). |
| `terminal_unhandled` | none | The sequences this session was sent and did not act on, the ND graphics modes and ReGIS commands included. The first thing to read when a screen looks wrong and nothing has failed. |
| `terminal_snapshot` | **`file`** | Writes the rendered screen as text to a file (the folder must exist). |
| `terminal_screenshot` | **`file`**, `settle` [250], `area` [terminal] (terminal, window) | Saves a PNG with Sixel, ReGIS and vector graphics drawn in. Waits until the screen has been quiet for `settle` ms (0 = take it now). `area=terminal` is the terminal area of the tab only; `area=window` is the whole program window showing the tab (menu, tabs, status bar), or the popped-out window. Both are drawn by the program into an offscreen picture, not captured from the screen: the operating system's frame and anything lying on top of the window are not in it. `area=window` is refused when the tab is not the selected one, rather than photographing a different tab. Returns the path and the size in bytes; an empty file is reported as a failure. |
| `terminal_recordstart` | **`file`**, `area` [terminal] (terminal, window), `interval` [100] (20 to 10000) | Starts filming the screen as an animated PNG. The screen is looked at every `interval` ms and a frame is added only when the picture changed; each frame lasts as long as it was shown. The first frame is taken at once, so a recording that cannot draw is refused here. One recording per session; an existing file is replaced; the folder must exist. |
| `terminal_recordstop` | none | Finishes the file and returns its path, picture size, number of pictures and frames, and length. If the recording had already ended by itself (the area could no longer be drawn, or the picture changed size), it says why. Then it makes an MP4 beside the PNG (same name, `.mp4`) when it finds ffmpeg, and the result lists every file written with its kind (`animated PNG`, `MP4 (H.264, yuv420p, WxH)`) and which ffmpeg made the MP4 (`ffmpeg used: <path> (set in Preferences)` or `(found on the PATH)`), or `no MP4:` and why. This text is the tool result, the same as a script gets. Closing the tab also finishes the file, but makes no MP4. |

**IBM 3270**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_statusline` | none | The status line: the 3270's line 25 with the keyboard lock and why; on a VT tab, the line the host writes. |
| `terminal_fields` | none | Every 3270 field, one line each: position, length, protected or input, numeric, hidden, bright, modified, colour and text. A hidden field never shows its text. |
| `terminal_setfield` | **`field`**, **`text`** | Empties the input field with that FIELDS number (from 1) and types the text. Nothing is sent until an attention key such as `IBM.ENTER`. |
| `terminal_cursor` | **`row`**, **`col`** | Moves the 3270 cursor (both counted from 1). |
| `terminal_waitunlock` | `timeout` [30000] | Waits until the host unlocks the keyboard (it has answered). |
| `terminal_codepage` | `number` [0] | Without a number: the EBCDIC code page in use and the ones to choose from. With one (277 for Denmark and Norway): changes it and draws the screen again. Every tab starts on 037. |
| `terminal_hostsend` | **`path`**, **`dataset`**, `mode` [ascii] (ascii, binary), `options` [""], `timeout` [120000] | Sends a PC file to a host data set with IND$FILE PUT. TSO must be at READY (not in ISPF). Without quotes, TSO puts the user id in front of the data set name. `options` adds host options such as `RECFM(F) LRECL(80)`. Script alias `IND$PUT`. |
| `terminal_hostreceive` | **`path`**, **`dataset`**, `mode` [ascii] (ascii, binary), `options` [""], `timeout` [120000] | Receives a host data set into a PC file with IND$FILE GET. The file is replaced only when the host says the transfer is complete. `options` such as `APPEND`. Script alias `IND$GET`. |
| `terminal_submit` | **`path`**, `timeout` [30000] | Sends a JCL text file to the host's card reader, which runs it as a batch job (Hercules' socket card reader; port 3505 on TK5). The reader's port, and its host if it is not the connection's own, are set on the connection (Manage Connections, Protocol tab); with none set it refuses. A line longer than 80 columns, or a byte that is not plain ASCII, is refused with its line number and nothing is sent. Answers with the number of cards and bytes sent. It cannot see the JES2 job number. |

**The terminal itself**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_emulation` | **`type`** | Changes the terminal type on a live connection, keeping screen and connection: VT100, VT220, VT320, VT340, VT420, VT52, VT102, VT240, XTERM, XTERM-256COLOR, TDV1200, TDV2215, TDV2200, TEK4014. |
| `terminal_reset` | none | Resets the terminal (modes, character sets, screen) - recovers a garbled display. |
| `terminal_clear` | none | Clears the screen and the scrollback. |
| `terminal_echo` | **`text`** | Writes a local note on the screen; nothing is sent to the host. |
| `terminal_zoom` | `percent` (50 to 400), `step` | Reads, sets or steps the display zoom. No arguments reports it; giving both is refused. |
| `terminal_keybind` | **`action`** (list, bind, unbind, defaults), `vk`, `mods` [""], `grid`, `shifted` [false], `save` [true] | TDV keyboard bindings: a Windows virtual-key code plus shift/ctrl/alt/meta, bound to a TDV grid position such as G53 (HJELP). |
| `terminal_help` | `command` | The generated command reference, or the full help for one command. |

**Connections**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_connect` | `name`, `host`, `port` [23], `protocol` [telnet] (telnet, ssh, tn3270, serial, opcomsimulator; any other word is refused), `username`, `password`, `port_name`, `baud` [9600], `data_bits` [8], `parity` [none], `stop_bits` [1], `model` (tn3270 only: IBM-3278-2, IBM-3278-3, IBM-3278-4, IBM-3278-5; the model also fixes the screen: 80x24, 80x32, 80x43, 132x27), `codepage` (tn3270 only: the EBCDIC code page by IBM number, one of 37, 273, 277, 278, 280, 284, 285, 297, 500, 871, 1047, 1140-1149; 37 when left out) | Connects this session: a stored name (then the rest is ignored), ad-hoc host and port, or ad-hoc serial. CONNECT keeps the tab's terminal, so a `model` other than the tab's is refused (open a new tab with terminal_open model=...); `codepage` is set before the connect. |
| `terminal_disconnect` | none | Deliberate disconnect; does not run the connection's on-disconnect script. |
| `terminal_connlist` | none | The stored connections (name, target, protocol; `*` = favourite) - the same list as Quick Connect. |
| `terminal_connshow` | **`name`** | The details of one stored connection. |
| `terminal_connsave` | **`name`**, `host`, `port` [23], `protocol` [Telnet] (telnet, ssh, tn3270, serial, gateway, opcomsimulator, any case; any other word is refused), `port_name`, `baud` [9600], `data_bits` [8], `parity` [none], `stop_bits` [1], `emulator` [VT100], `username`, `width`, `height`, `favorite` [false], `onconnect`, `ondisconnect`, `model` (tn3270 only: IBM-3278-2, IBM-3278-3, IBM-3278-4, IBM-3278-5; the model also fixes the screen: 80x24, 80x32, 80x43, 132x27), `codepage` (tn3270 only: the EBCDIC code page by IBM number, one of 37, 273, 277, 278, 280, 284, 285, 297, 500, 871, 1047, 1140-1149; 37 when left out) | Creates a stored connection, or updates one (only the values given change). `onconnect`/`ondisconnect` name library scripts; an empty string clears them. `model` and `codepage` are saved where Manage Connections reads them (the size box and the code page box); CONNSHOW shows the code page. |
| `terminal_conndel` | **`name`** | Deletes a stored connection. |

**Files, logs and traces**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_sendfile` | **`path`** | Kermit send (start the remote receive first); several files separated by `;`. |
| `terminal_receivefile` | **`dir`** | Kermit receive into an existing folder (start the remote send first). |
| `terminal_logstart` | **`file`**, `format` [text] (text, hex, raw) | Logs the raw session traffic, both directions, to a file. |
| `terminal_logstop` | none | Stops the log and closes the file. |
| `terminal_tracestart` | `raw` [true] | Starts the protocol trace, the same decoded stream as the Protocol Monitor window. |
| `terminal_tracestop` | none | Stops it; what was recorded stays readable. |
| `terminal_traceread` | `sinceid` [0], `max` [200], `raw` [false], `hex` [false] | Trace entries, each line starting with its id. Ids are unique, always increasing, and survive TRACECLEAR, so poll with `sinceid=` the last id + 1 and you only ever see new entries. |
| `terminal_traceclear` | none | Empties the trace buffer; ids keep counting. |

**Norsk Data machines**

| Tool | Parameters | What it does |
|------|-----------|--------------|
| `terminal_opcom` | **`action`**, `addr`, `endaddr`, `value`, `reg`, `level` [0], `endlevel`, `count` [1], `pass`, `timeout` [10000] | The ND-100 OPCOM debugger, all values **octal**. `action`: status, passthrough, readmem, writemem, dumpmem, readreg, writereg, readintreg, writeintreg, dumpregs, dumpintregs, ioxread, ioxwrite, stop, start, masterclear, step, breakpoint, escape, boot. |
| `terminal_gateway` | `action` [status] (status, terminals) | The ND-100 gateway: listening or not, port, emulator and disk connected, and the registered terminals. |

### Key names for SENDKEY

A key is `FAMILY.KEY` or a bare `KEY`. The families are `TDV`, `IBM` and `VT`. A bare name is
looked up in the tab's own terminal; a family that does not match the tab is refused and
nothing is sent (`IBM.PF3` on a TDV tab fails). Upper or lower case does not matter.

- **IBM (3270 tabs)**, from `Ibm3270Emulator.BuildKeyNames`: `ENTER`, `CLEAR`, `PA1`, `PA2`,
  `PA3`, `PF1` to `PF24`, `RESET`, `TAB`, `BACKTAB`, `HOME`, `LEFT`, `RIGHT`, `UP`, `DOWN`,
  `NEWLINE`, `BACKSPACE`, `DELETE`, `INSERT`, `END`, `ERASEEOF`, `ERASEINPUT`, `DUP`,
  `FIELDMARK`, `ATTN`, `SYSREQ`, `CURSORSELECT`. ENTER, CLEAR, the PA and the PF keys send the
  screen to the host; the others act on the terminal. `ATTN` in plain TN3270 sends PA1 and then
  a Telnet BREAK; in TN3270E it is refused. `SYSREQ` does nothing in TN3270E.
- **TDV**: the legends printed on the TDV2200 keyboard (`HJELP`, `ANGRE`, `F1` .. `F8`, ...)
  or a grid position such as `G53` or `F51`. A legend and a grid position can name the same
  key: `F1` and `F51` are one key. The sequence sent follows the terminal's mode (extended, or
  2115 compatibility). Programmable PUSH keys have no fixed sequence and are refused.
  `docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md` has the full table.
- **VT**: no key names yet; SENDKEY on a VT tab is refused - use `terminal_sendraw`.

### Security

The server binds to `127.0.0.1` only - written into the code, not configurable. This tool
types arbitrary commands into machines; it must never listen on an external interface.

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

Verb summary. These are the same 48 commands as the MCP command tools in section 1.2, with the
same parameters and defaults; `IND$PUT` and `IND$GET` are script-only aliases of `HOSTSEND` and
`HOSTRECEIVE`.

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
| `SENDKEY` | `SENDKEY HJELP`, `SENDKEY IBM.PF3` — a named terminal key; `FAMILY.KEY` (TDV, IBM, VT) or a bare name looked up in the tab's own terminal. The key names are listed in section 1.2, "Key names for SENDKEY" |
| `STATUSLINE` | `STATUSLINE` — the status line: a 3270's line 25 plus whether the keyboard is locked and why; on a VT tab, the line the host writes |
| `FIELDS` | `FIELDS` — every 3270 field, one line each: row and column of its first character, length, protected or input, numeric, hidden, bright, modified, colour and its text; a hidden field never shows its text |
| `SETFIELD` | `SETFIELD 2 HERC01` — empty a 3270 input field (numbered as FIELDS numbers it) and type the text; nothing is sent until a key such as `SENDKEY IBM.ENTER` |
| `CURSOR` | `CURSOR 10 20` — move the 3270 cursor to row 10, column 20 (counted from 1) |
| `WAITUNLOCK` | `WAITUNLOCK 10000` — wait until the host unlocks the 3270 keyboard (it has answered); fails at once on a lock only Reset clears; `ontimeout=label` works |
| `HOSTSEND` | `HOSTSEND "C:\\temp\\hello.txt" HELLO.TEXT` — send a PC file to an IBM host data set with IND$FILE: types `IND$FILE PUT HELLO.TEXT ASCII CRLF` at TSO's READY (not in ISPF) and waits for the host's `TRANS03` message. `mode=binary` sends the bytes unchanged; `options=` adds host options such as `RECFM(F) LRECL(80)`; `ontimeout=label` works |
| `HOSTRECEIVE` | `HOSTRECEIVE "C:\\temp\\back.txt" HELLO.TEXT` — receive a host data set into a PC file with IND$FILE GET. The file is only replaced when the host says the transfer is complete; a failed transfer leaves it as it was. Same `mode=`, `options=` and `timeout=` as HOSTSEND. In a script, `IND$PUT` and `IND$GET` (IBM's names) are the same two commands; MCP lists them only as HOSTSEND and HOSTRECEIVE, since a tool name cannot hold a `$` |
| `SUBMIT` | `SUBMIT "C:\\temp\\hello.jcl"` — send a JCL text file to the host's card reader, which MVS runs as a batch job (Hercules' socket card reader; on TK5 port 3505). The reader's port, and its host if it is not the connection's own, are set on the connection (Manage Connections, Protocol tab); with none set SUBMIT refuses. A line longer than 80 columns, or a byte that is not plain ASCII, is refused with its line number and nothing is sent. Answers with the number of cards and bytes sent, once the reader has closed the connection after the last card. It cannot see the JES2 job number; `STATUS jobname` at TSO's READY shows it. `timeout=` (default 30000 ms) |
| `CODEPAGE` | `CODEPAGE 277` — change the 3270 tab's EBCDIC code page and draw the screen again with it; it decides what the host's bytes show as and what a typed character is sent as (277 for Denmark and Norway). `CODEPAGE` alone names the page in use and lists the others. Every tab starts on 037 |
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
| `SCREENSHOT` | `SCREENSHOT "C:\\shots\\after-regis.png"` — saves the screen as a PNG, with Sixel, ReGIS and vector graphics drawn in. `SNAPSHOT` and `READSCREEN` return TEXT, which on a session carrying graphics is a fraction of what is there. Waits `settle=` milliseconds (default 250) for the screen to go quiet and publishes the graphics first, so the picture is never one change out of date; `settle=0` takes the frame as it stands. `area=terminal` (the default) saves only the terminal area of the session's tab, at the terminal control's own pixel size; `area=window` saves the whole program window showing that tab (menu, tab strip, terminal, status bar), or the popout window if the tab was popped out. Both are drawn by the program into an offscreen picture - not a capture of the screen: the operating system's frame and title bar are not in it, nor anything lying on top of the window. `area=window` is refused when the session's tab is not the selected tab, rather than photographing another tab; any word other than `terminal` or `window` is refused by name. The folder must already exist. |
| `RECORDSTART` / `RECORDSTOP` | `RECORDSTART "C:\\films\\login.png" area=terminal` ... `RECORDSTOP` — records the screen as a film, an animated PNG (APNG: plays in Chrome, Edge, Firefox and Safari; a viewer without APNG support shows the first frame). `area=` takes the same two words as `SCREENSHOT` and every frame is drawn the same way a `SCREENSHOT` is. The screen is looked at every `interval=` milliseconds (default 100, allowed 20 to 10000), and a frame is added only when the picture changed; each frame lasts as long as it was on the screen, right to within one interval, so a quiet minute is one frame. The first frame is taken by `RECORDSTART` itself, so a recording that cannot draw is refused there. One recording per session: a second `RECORDSTART` is refused naming the file already being written, and `RECORDSTOP` with nothing started is refused with `no recording is running for this session - RECORDSTART first`. `RECORDSTOP` finishes the file and reports its path, size, number of pictures and frames, and length (a still longer than 65.5 seconds is stored as more than one frame). Closing the tab finishes the file too. The recording ends by itself, keeping what it has, when the area can no longer be drawn - `area=window` while another tab is selected, or the picture changing size (an APNG has one size) - and the next `RECORDSTOP` says why. The folder must already exist; an existing file is replaced. MCP: `terminal_recordstart`, `terminal_recordstop`. **The MP4.** After the PNG is closed, `RECORDSTOP` runs ffmpeg as a separate program to make an MP4 beside it (same name, `.mp4`; an existing one is replaced): H.264 in yuv420p, which YouTube and video editors take, with every frame lasting exactly as long as in the PNG (checked to the millisecond with ffprobe). yuv420p needs an even width and height, so an odd one gets one black column or row added. ffmpeg is the one set in Preferences, Recording tab; when that box is empty, the first `ffmpeg` on the system PATH. A path in Preferences that names no file is reported, not replaced by the PATH's one. RetroTerm ships no ffmpeg. The reply lists every file written with its kind and which ffmpeg was used, for example `files written:` / `  <folder>\login.png - animated PNG` / `  <folder>\login.mp4 - MP4 (H.264, yuv420p, 1024x640)` / `ffmpeg used: <the ffmpeg program> (found on the PATH)`. With no ffmpeg it says `no MP4:` and why, and the PNG is the film. If ffmpeg fails, its own error text is reported after `no MP4:`, the PNG is kept, and any empty MP4 it left is deleted; `RECORDSTOP` still succeeds, because the film asked for was written. `into=` stores the PNG's path. Closing the tab finishes the PNG without an MP4. |
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

The commands that can time out - `WAITFOR`, `WAITIDLE`, `WAITUNLOCK`, `HOSTSEND`, `HOSTRECEIVE` and `SUBMIT` - take `ontimeout=name` (on any other command the parser refuses it): on timeout the script **branches**
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

1. **One persistent connection per session** — reconnecting in the middle of a program hangs the line.
2. **ESC is a first-class operation** — it wakes and recovers SINTRAN lines.
3. **Never guess a duration** — `WAITFOR` is the normal way to sequence; `SLEEP` is the exception.
4. **A timeout still returns what the machine said** — partial output is the most valuable thing on a failure.
5. **Every wait reports how long it took** — 23 s vs 200 ms is often the whole diagnosis.
6. **Errors are never swallowed** — a dropped connection aborts a script naming the step, with the screen.
7. **A name the command does not take is refused, on both surfaces, with the same words** —
   `SCREENSHOT aera=window` in a script and `terminal_screenshot` with `"aera": "window"` over MCP
   both answer `SCREENSHOT has no parameter 'aera' - it takes: ... (see HELP SCREENSHOT)` and
   nothing runs. Over MCP the one extra name every command tool takes is `sessionId`; in a script
   the extra names are the step keywords `optional=`, `ontimeout=` and `into=`. The MCP tools that
   are not commands (`terminal_open`, `terminal_close`, `terminal_list`, `terminal_build`,
   `terminal_run_script`, `terminal_scripts`) follow the same rule from their own declared
   arguments, which are also their schema: `terminal_open` with `"hots"` answers
   `terminal_open has no parameter 'hots' - it takes: name, protocol, ... (see tools/list)`.

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
