# RetroTerm

RetroTerm is a terminal emulator for the Norsk Data world and the DEC world: the Tandberg
TDV 1200, TDV 2215 and TDV 2200 (the ND-246 NOTIS terminal), and the VT52, VT100, VT102,
VT220, VT240, VT320, VT340, VT420, xterm and Tektronix 4014. It talks Telnet, SSH and serial,
and it can be driven by a program instead of a person, through an MCP server that is built
into the application.

This folder is a distribution. Everything in it is described below.

## What is in this folder

| File | What it is |
|---|---|
| `RetroTerm.Desktop.exe` | The whole application. Self-contained, single file, Windows x64. Nothing to install and no .NET runtime needed. |
| `README.md` | This file. |
| `.mcp.json` | A ready-made Claude Code project configuration that points at RetroTerm's MCP server. Copy it into the folder you run Claude Code from. |
| `add-mcp-to-claude-code.ps1` | The same thing as a one-line script: registers the server with Claude Code for your user. |

There are no other files to ship. The MCP server, the terminal fonts, the themes and the
help text are all inside the exe. Native libraries are unpacked by the exe itself on first
start.

## Running it

Double-click `RetroTerm.Desktop.exe`, or start it from a prompt. There are no command-line
arguments.

On first start it creates a settings folder at `%APPDATA%\RetroTerm` and writes to it as you
work:

| File | Holds |
|---|---|
| `preferences.txt` | Theme, font, MCP on/off and port, and the other Preferences settings |
| `host-configurations.json` | Your saved connections (Manage Connections) |
| `tdv-key-bindings.json` | Key bindings you have made on the virtual keyboard |
| `push-keys.json` | The strings programmed on the TDV PUSH keys |
| `gateway-settings.json`, `gateway-disk-settings.json` | ND-100 gateway settings |
| `logs\` | Session logs, when you start one |
| `crash.log` | Written only if the application crashes |

Delete the folder to start from scratch. The exe itself never writes next to itself.

## Using it as a person

- **Connect.** Connection, Quick Connect for a one-off host. Connection, Manage Connections to
  save a connection with its terminal type, screen size, keyboard settings and colours, then
  connect it from the list or from Connection, Favorites.
- **Terminal type.** Pick it when you save the connection, or change it on an open tab from
  Connection, Emulation. A TDV is 80 by 25, the VT family 80 by 24; the size follows the type.
- **The virtual keyboard.** View, Virtual Keyboard shows the real TDV keyboard. Click a key to
  send it. Right-click a key to bind a PC key combination to it. Escape closes that popup.
- **Keyboard reference.** Keyboard, Key Reference lists which PC key sends which TDV key and
  what bytes go on the wire. It is generated from the same table the keyboard uses.
- **Backspace and Delete.** Manage Connections, Keyboard tab. A TDV sends DEL for Backspace by
  default, which is what SINTRAN wants; a VT sends BS. The connection can override either way.
- **ND-100 gateway.** Connection, Gateway Settings, for a RetroCore ND-100 emulator's terminal
  ports.
- **File transfer.** Transfer, Send File(s) and Receive File(s), over Kermit.

## Using it from a program: MCP

Every running RetroTerm hosts an MCP server (Model Context Protocol, streamable HTTP):

```
http://127.0.0.1:5715/mcp
```

- It is **on by default**. Preferences, MCP tab, has the on/off switch and the port. A change
  there takes effect the next time RetroTerm starts. Nothing else sets the port: no
  environment variable, no command-line flag.
- It listens on **127.0.0.1 only**. The program that drives RetroTerm has to run on the same
  computer. That is deliberate: the tools type into live machines, and a server reachable from
  the network would let anybody on it do that. To drive it from another computer, open an SSH
  tunnel to port 5715 rather than changing the bind address.
- RetroTerm must be running, with its window open, while the tools are used. Every session a
  program opens is a **visible tab** in the window, so you can watch it type.
- If the port is already taken, the status bar says so and RetroTerm runs without the server.

### Pointing Claude Code at it

Either of these. Both do the same thing.

**Per user, once:**

```powershell
claude mcp add --transport http retroterm http://127.0.0.1:5715/mcp
```

`add-mcp-to-claude-code.ps1` in this folder runs exactly that line.

**Per project:** copy `.mcp.json` from this folder into the folder you start Claude Code in.
It contains:

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

If you changed the port in Preferences, change it in the URL too. Then start Claude Code, run
`/mcp` to see that `retroterm` is connected, and the tools appear as `terminal_open`,
`terminal_send` and so on.

### Any other MCP client

Anything that speaks MCP over streamable HTTP can use the same URL. There is no
authentication and no API key, because the server only answers on the local machine.

### The tools

Run `terminal_help` first: it lists every tool with its parameters, and it is generated from
the same command table the script language uses, so it is always current for the build you
have. The set, grouped:

- **Sessions:** `terminal_open` (returns a `sessionId`), `terminal_close`, `terminal_list`,
  `terminal_status`.
- **Typing:** `terminal_send` sends exactly the text given, so put the carriage return in it
  (`"LIST-FILES\r"`). `terminal_sendraw` sends bytes as hex pairs or the word `ESC`.
  `terminal_sendkey` sends a named terminal key such as a TDV function key.
  `terminal_localkey` presses a PC key through the real keyboard path. `terminal_paste`.
- **Reading:** `terminal_waitfor` waits until a pattern is on the rendered screen and is the
  tool to sequence on. `terminal_readscreen`, `terminal_readnew`, `terminal_snapshot`,
  `terminal_screenshot`, `terminal_waitidle`.
- **The terminal:** `terminal_emulation`, `terminal_zoom`, `terminal_clear`, `terminal_reset`,
  `terminal_echo`, `terminal_keybind`, `terminal_unhandled`.
- **Stored connections:** `terminal_connect`, `terminal_disconnect`, `terminal_connlist`,
  `terminal_connshow`, `terminal_connsave`, `terminal_conndel`.
- **Files:** `terminal_sendfile`, `terminal_receivefile` (Kermit).
- **Diagnostics:** `terminal_tracestart`, `terminal_tracestop`, `terminal_traceread`,
  `terminal_traceclear` for the byte-level wire trace; `terminal_logstart`, `terminal_logstop`
  for a transcript.
- **Scripts:** `terminal_run_script`, `terminal_scripts`, `terminal_sleep`.
- **Machines:** `terminal_gateway`, `terminal_opcom` for the ND-100 gateway and the OPCOM
  console.

### How to drive a machine well

These are the rules that came out of real use against ND-100 and ND-500 machines:

1. **Open once, keep the session.** `terminal_open` returns a `sessionId` that stays valid
   across calls. Never open a session per command. Reconnecting in the middle of a program
   can hang the line on these machines.
2. **Wait on the screen, never on a clock.** After every send, `terminal_waitfor` the prompt
   or text you expect, then read. Replies arrive in several small writes, and a fixed delay
   is either too short or wasted. The `timeout` is in milliseconds. A timeout still returns
   the screen and the time it waited.
3. **A fresh SINTRAN line wants ESC first.** Send `terminal_sendraw` with `ESC` before you
   expect a prompt.
4. **Log in with one send.** `terminal_send` with `"SYSTEM\r\r"`: the name, a return, and a
   second return for the blank password. Split into separate sends it bounces back to a fresh
   ENTER prompt.
5. **Watch the MCP Log.** View, MCP Log in RetroTerm shows every tool call and its answer as
   it happens, which is the fastest way to see what the program actually sent.

A short example, as the calls an LLM would make:

```
terminal_open      host=localhost port=9010 emulator=TDV2200      -> sessionId
terminal_sendraw   sessionId=... bytes=ESC
terminal_waitfor   sessionId=... pattern="ENTER" timeout=5000
terminal_send      sessionId=... text="SYSTEM\r\r"
terminal_waitfor   sessionId=... pattern="@" timeout=10000
terminal_send      sessionId=... text="LIST-FILES\r"
terminal_waitfor   sessionId=... pattern="@" timeout=10000
terminal_readscreen sessionId=...
```

## Scripts without an LLM

The same commands are available as a small script language stored in RetroTerm, with
`WAITFOR`, variables, loops and subroutines, and a script can run automatically when a saved
connection connects or disconnects. View, Script Editor in the application, and the built-in
help there.

## Version

Help, About shows the version and the build date of the exe you are running. The MCP server
reports the same in its greeting when a client connects.
