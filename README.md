<p align="center">
  <img src="assets/RetroTerm-DarkCRT-256.png" width="160" alt="RetroTerm logo: a dark CRT screen with a green cursor.">
</p>

# RetroTerm

[![build](https://github.com/RetroCoreLabs/RetroTerm/actions/workflows/build.yml/badge.svg)](https://github.com/RetroCoreLabs/RetroTerm/actions/workflows/build.yml)
[![release](https://img.shields.io/github/v/release/RetroCoreLabs/RetroTerm)](https://github.com/RetroCoreLabs/RetroTerm/releases)
[![licence](https://img.shields.io/github/license/RetroCoreLabs/RetroTerm)](LICENSE)
[![downloads](https://img.shields.io/github/downloads/RetroCoreLabs/RetroTerm/total)](https://github.com/RetroCoreLabs/RetroTerm/releases)

**RetroTerm is a terminal emulator that puts a 1980s Tandberg TDV or DEC VT screen on a modern
desktop, so you can talk to a real Norsk Data machine - or an emulated one - the way its users
did, and let a script or an LLM do the typing when you would rather not.**

[What it does](#what-you-can-use-it-for) · [Quick start](#quick-start) · [Status](#status) · [Building](#building-from-source) · [About the terminals](#about-the-tdv-and-vt-terminals)

<!-- A 5-15 second recording: RetroTerm opening a connection to a SINTRAN host, the TDV2200
     screen drawing, a command typed. Under 10 MB. Uncomment when the capture exists.
![RetroTerm connected to a SINTRAN III system over telnet, the TDV2200 screen drawing a directory listing.](docs/images/readme/demo.gif)
-->

## Why it exists

A Norsk Data ND-100 or ND-500 does not talk to a PC. It talks to a **Tandberg TDV terminal** -
a TDV2200, a TDV2215, a TDV1200 - and SINTRAN III's screen handling, its function keys, its
protected fields and its national character sets all assume one is on the other end of the
line. Point an ordinary terminal emulator at one and you get a screen that is almost right, with
the cursor in the wrong place and the keys that matter doing nothing.

RetroTerm is the TDV, done from the manuals. The escape sequences, the 120-key keyboard with
its twelve national layouts, the smooth-scroll modes, the message LEDs. Beside it sit the DEC
VT family and the Tektronix 4014, built the same way - from the specification, with the test
that proves each claim, and without claiming the features a real one did not have.

It is also built to be **driven**. Every running RetroTerm hosts an MCP server, so an LLM can
open a session, type, and wait on the **rendered screen** instead of guessing at a byte stream;
and the same commands are a script language, a console, and a generated help page, because
each one is written once.

## What you can use it for

- **Log in to a real Norsk Data machine** over Telnet, SSH or a serial line, with the
  terminal SINTRAN expects - TDV2200, TDV2215 or TDV1200 - and a keyboard that sends what the
  TDV keys sent.
- **Use it as an ordinary VT** against anything else: VT52, VT100, VT102, VT220, VT240,
  VT320, VT340, VT420, xterm and xterm-256color, selectable per connection.
- **Draw**: Tektronix 4014 storage-tube graphics, ReGIS, Sixel, and the Norsk Data TDV2200
  graphics planes.
- **Automate a session**: `.rts` scripts with SEND, WAITFOR on the rendered screen, variables,
  branches and retries - run from the IDE-style editor, the console, or on connect.
- **Let an LLM run the terminal**: the MCP server on `http://127.0.0.1:5715/mcp` exposes
  every command as a tool, including Kermit transfers, the protocol trace, the OPCOM debugger
  and the ND-100 gateway.
- **Transfer files with Kermit**, trace the protocol with unique log ids, and debug an
  ND-100 through OPCOM - registers, memory, CPU control, boot.

### The terminals

Selectable today, in Connection → Manage Connections → Terminal: **VT52, VT100, VT102, VT220,
VT240, VT320, VT340, VT420, xterm, xterm-256color, TDV1200, TDV2215, TDV2200, TEK4014**.

That list is read from `EmulatorFactory.AvailableEmulators` rather than typed into the dialog. It
had been typed in, and said VT100, TDV2200 and TDV2215 only - so ten of these terminals were built,
tested and described here as shipped while nobody could actually pick one for a connection.
`TerminalTypeIsSelectableTests` now fails if the dialog and the factory ever disagree again.

The table is the planned set against what actually exists, in the order
`docs/ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md` recommends adding them - ordered by
dependency, not preference, so each one reuses what the ones above it forced into place.

| # | Terminal | State |
|---|---|---|
| 1 | ANSI / ECMA-48 | Core is solid - an outside conformance corpus went from 97 disagreements to 8, of which 7 are deliberate representation differences. No selectable profile entry yet. |
| 2 | DEC VT100 | **Shipped** |
| 3 | DEC VT220 | **Shipped** - identifies as VT200-family and its DA reply names the four extensions it really has: 132 columns, selective erase, downloadable character sets, and user-defined keys. The printer port and the national replacement sets are absent, so it does not claim them. |
| 4 | xterm / xterm-256color | **Shipped** - selectable as `XTERM` and `XTERM-256COLOR`. OSC colour queries, bracketed paste, focus reporting, mouse tracking (9/1000/1002/1003) with the SGR encoding, host-commanded resize. It identifies as VT100-class rather than claiming the VT420 features it does not have. |
| 5 | Tandberg TDV2200/9 (text) | **Shipped** |
| 6 | TDV2200 graphics (Norsk Data) | **Working end to end** - `ESC "` ground mode, graphics planes, detection reply, Tektronix vector drawing. Six `ESC "` modes are implemented (6, 8, 9, 10, 17, 24); the rest are counted rather than guessed at, because the spec names them without their parameter meanings. |
| 7 | Tektronix 4010/4014 | **Shipped** - selectable as `TEK4014` at the real 74×35 geometry. Storage-tube behaviour, `ESC FF` erase, `ESC ENQ` five-byte report, `ESC SUB` crosshair. Character sizes and the alternate-column text wrap are not implemented. |
| 8 | Tandberg TDV2215 | **Shipped** |
| 9 | Tandberg TDV1200 | **Shipped** - the C0 control codes a TDV moves its cursor with now work outside 2115 mode too, per the 2215 manual section 8.4. |
| 10 | DEC VT240/VT241 | **Shipped** - selectable as `VT240`: a VT220 with ReGIS. Pair it with a single-phosphor theme and the drawing collapses to the phosphor with the text, as a one-gun screen had to. Sixel is not claimed - no source here confirms a VT240 had it. |
| 11 | DEC VT320 | **Shipped** - selectable as `VT320`. Answers as service class 63, the VT300 family, from DEC's own table in the VT330/VT340 Programmer Reference. It claims 132 columns, selective erase, the downloadable character set and user-defined keys, and each of those four is tested. The printer port and national replacement sets are not built, so they are not claimed; ReGIS and Sixel are what separate the VT330/VT340 from this machine. Page memory included. |
| 12 | DEC VT340 | **Sixel and ReGIS shipped** - selectable as `VT340`. Sixel images land at the cursor; ReGIS draws vectors, circles and colour on a plane of its own. The ReGIS commands that draw are implemented and the rest are counted, readable through the emulator. |
| 13 | DEC VT420 | **Margins shipped** - selectable as `VT420`. DECLRMM and DECSLRM confine printing, wrapping AND editing to a column range: insert/delete character stop at the margins, and insert/delete line move only the columns inside the region. `CSI s` correctly stays SCOSC when the mode is off. The whole rectangle family is in: DECFRA, DECERA, DECSERA, DECCRA, DECCARA, DECRARA and DECSACE - whose default extent is a STREAM, not a rectangle. |
| 14 | DEC VT52 / VT102 | **Both shipped.** `VT52`: single-letter escapes, `ESC Y` direct addressing through the parser's raw-byte path, `ESC / Z` identification - and a VT-family terminal can drop into it with DECANM. `VT102`: answers `CSI ? 6 c`, so a host knows it may use the editing sequences instead of redrawing whole lines. |
| 15 | IBM 3270 | Not started, and deliberately not begun blind. There is no 3270 manual or data-stream reference anywhere in this repository - building one from recollection would invent EBCDIC orders, AID codes and structured fields that may not match any real host. The scaffolding that exists (a `TN3270` protocol that throws, a cell field-attribute slot) is honest about that. Needs a spec before it needs code. |

### Keyboard, connections, and the rest

- **Virtual keyboard**: 120+ key TDV keyboard with 12 national layouts (ND-246), and a key
  binding system that maps any PC key combination to any TDV key. Keyboard → Key Reference
  lists what every PC key sends, generated from the same table the keyboard uses.
- **Backspace**: a TDV sends DEL, which SINTRAN expects; a VT sends BS. Each connection can
  override it either way. A TDV is 80 by 25 and a VT 80 by 24, wherever the type is picked.
- **Connections**: Telnet (RFC 854, with NAWS and TERMINAL-TYPE negotiation), SSH (password
  authentication, host key verification on first use), and a serial port.
- **Text**: selection by character, word, line or rectangle; scrollback search with regular
  expressions (F3 / Shift+F3); copy and paste with control characters stripped.
- **Smooth scrolling**: the TDV STEP and SMOOTH modes (NDSSM).

### Automation: MCP and scripting

- **MCP server**: every running RetroTerm hosts one on `http://127.0.0.1:5715/mcp`. An LLM
  such as Claude Code can open sessions as visible tabs, type commands, and wait for prompts on
  the **rendered screen** instead of guessing at the raw byte stream.
- **Script language** (`.rts`): SEND (verbatim; `\r \n \t \e \xHH \NNN` escapes), SENDRAW,
  SENDKEY, WAITFOR / WAITIDLE (rendered-screen matching, all times in **milliseconds**),
  variables with IF/ELSE, LABEL/GOTO/GOSUB, `ontimeout=` retry loops, `into=` capture - and a
  stop-on-failure runner that hands the live session back for interactive debugging.
- **One command seam**: each command is a script verb **and** a console command **and** an MCP
  tool **and** its own generated help. Add a class once; it appears everywhere.
- **Beyond the terminal**, MCP and scripts also drive stored connections (CONNECT, CONNSAVE),
  Kermit transfers (SENDFILE, RECEIVEFILE), the protocol monitor with unique incremental log
  ids (TRACESTART, TRACEREAD `sinceid=`), the OPCOM debugger (registers, memory, CPU control,
  boot), TDV keyboard bindings (KEYBIND) and the ND-100 gateway (GATEWAY).
- **Tools**: View → Script Editor (syntax highlighting, live validation, generated help panel,
  per-tab run target), Script Console, MCP Log. Scripts can auto-run on connect and disconnect
  per stored connection.
- The setup, the full tool list and the language reference are in
  **[MCP and Scripting](docs/MCP-AND-SCRIPTING.md)**.

## Status

Version `1.0.26.2`. No tagged GitHub release yet, so the release and download badges above stay
empty until the first `v*` tag.

- **The whole solution builds in Release with no errors**, and the test suites pass:
  **7,029 passed, 1 skipped, 0 failed** on 2026-09-28 (6,906 in `RetroTerm.Tests`, 123 in the
  Kermit suite; the main suite takes about two minutes in Release).
- **Fourteen terminal types are selectable and tested**; the table above says exactly what each
  one claims and what it deliberately does not.
- **Telnet, SSH and serial connections work.** IBM 3270 is not started, for the reason the table
  gives.
- **Not yet settled**: the things only a real host or a human eye can - mouse reporting driven by
  a program that actually tracks the pointer, bracketed paste in a real shell, and the flagged
  assumptions in the ND graphics protocol. Each is listed with what would settle it in
  [`docs/NEEDS-A-REAL-HOST-2026-08-11.md`](docs/NEEDS-A-REAL-HOST-2026-08-11.md).

## Prerequisites

| # | Requirement | Needed for | How to check |
|---|---|---|---|
| P1 | The .NET SDK. The projects target .NET 9; the build here runs on SDK 10.0.401 | Everything | `dotnet --info` |
| P2 | A Windows desktop session | The application is Avalonia; the UI tests target `net9.0-windows` | Not usable over a plain SSH session |
| P3 | Something to connect to | Anything useful | `tests/RetroTerm.TestServer` is included for exactly this |

## Quick start

```powershell
# from the repository root
dotnet build RetroTerm.sln -nodeReuse:false
dotnet run --project src\RetroTerm.Desktop\RetroTerm.Desktop.csproj
```

Then Connection → Manage Connections, pick a terminal type, and connect. If you have no host
to hand, start the included test server first and connect to it:

```powershell
dotnet run --project tests\RetroTerm.TestServer\RetroTerm.TestServer.csproj
```

Once `build-release.bat` has produced `publish\`, `start-retroterm-with-testserver.bat` starts
both published executables in one step - the test server on port 8080, then the client.
`scripts\publish.ps1` publishes the client alone into a versioned folder under
`publish\versions\` and repoints `publish\current` at it, which works while an older build is
still running. What ships with the exe, and how to point an MCP client at it, is in
[`docs/DISTRIBUTION-README.md`](docs/DISTRIBUTION-README.md).

## Building from source

```powershell
# build, then run every test
dotnet build RetroTerm.sln -nodeReuse:false
dotnet test  RetroTerm.sln -nodeReuse:false

# one test category
dotnet test --filter "FullyQualifiedName~TDV2200"

# a self-contained Windows x64 build of the client and the test server, into publish\
build-release.bat
```

> [!IMPORTANT]
> Pass `-nodeReuse:false` on every `dotnet build` and `dotnet test`. Node reuse is on by
> default and leaves MSBuild worker processes behind that hold a lock on the output DLL; the
> next build then quietly keeps the old binary, and **the tests report green against stale
> code**.

### Architecture

```
+-------------------------------------------+
|  Desktop UI (Avalonia)                    |  src\RetroTerm.Desktop (net9.0)
+-------------------------------------------+
|  Protocol Layer (Telnet, SSH, serial,     |  src\RetroTerm.Core.Protocols.* (netstandard2.1 / net9.0)
|  Kermit, WebSocket, telnet server)        |
+-------------------------------------------+
|  Core Emulation Layer                     |  src\RetroTerm.Core (netstandard2.1)
|  (Buffer, Parser, Emulators)              |
+-------------------------------------------+
```

1. **Zero-allocation parsing**: `ReadOnlySpan<byte>` for escape sequences, no LINQ, no `foreach`.
2. **Layered**: Core → Protocols → UI, strict dependency direction.
3. **Strategy** for font rendering: `BitmapFontRenderer` for the TDV2200, `SystemFontRenderer` for the rest.
4. **Composition**: the TDV emulators are built from component classes - `ProtectedAreas`, `WorkAreas`, `MessageLEDs` and so on.

## Current limitations

- **IBM 3270 is not implemented**, and will not be until a data-stream reference is in the
  repository - see the table.
- **The VT220 and VT320 do not claim a printer port or national replacement character sets**,
  because they are not built. The DA replies say so.
- **Tektronix 4014 character sizes and the alternate-column text wrap** are not implemented.
- **TDV2200 graphics**: six `ESC "` modes are implemented; the rest are counted, not guessed at.
- **Windows only** for now. The core is `netstandard2.1` and the UI is Avalonia, so Linux and
  macOS are possible; they have not been built or tested.

## About the TDV and VT terminals

If none of the names above mean anything, this is what they are.

A **terminal** is a screen and a keyboard with no computer of its own: it sends each keystroke
down a line to a machine somewhere else, and draws whatever comes back. From the 1970s to the
early 1990s that was how almost everyone used a computer, and every manufacturer's terminal had
its own set of control codes - the escape sequences that move the cursor, clear a field, ring
the bell, switch character set.

**Tandberg Data**, in Norway, made the **TDV** series for **Norsk Data**, whose ND-100 and
ND-500 minicomputers ran universities, hospitals, banks and defence across Scandinavia. The
TDV2200 is the one SINTRAN III - Norsk Data's operating system - was written for: protected
fields, a 120-key keyboard with national layouts, message LEDs, and a graphics mode. A real ND
machine still expects one on the other end of the line, which is why this project exists.

**DEC's VT100** (1978) became the standard everyone else imitated; the VT220, VT320, VT420 and
the graphics VT240 and VT340 followed, and **xterm** is the modern program that still speaks
their language. The **Tektronix 4014** was a storage-tube graphics terminal: it drew vectors
onto a phosphor that held the image until erased.

For the TDV, the manuals are in `spec/` and the escape sequences are written up in
[`docs/TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md`](docs/TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md).

## Roadmap

**v1.0**: documentation cleanup; TDV emulators to a 100% test pass rate; desktop UI polish
(settings, tabs, profiles); and the items in `docs/NEEDS-A-REAL-HOST-2026-08-11.md`.

**v2.0**: IBM 3270 block-mode emulators; a Blazor web UI; file transfer over SFTP/SCP and
Zmodem; SINTRAN/XOT protocols.

## Documentation

- **[MCP and Scripting](docs/MCP-AND-SCRIPTING.md)** - drive RetroTerm from an LLM or with stored scripts
- **[Running the published exe](docs/DISTRIBUTION-README.md)** - the README that ships beside the exe: what it writes where, and MCP setup for Claude Code or any client
- **[User manual](USER-MANUAL.md)** and **[Quick start](QUICK-START.md)**
- **[Feature status](FEATURE-STATUS.md)** - what is implemented, component by component
- **[ND-100 gateway](docs/ND100-GATEWAY.md)** and **[OPCOM command reference](docs/OPCOM-COMMAND-REFERENCE.md)**
- **[TDV escape sequence reference](docs/TDV-COMPLETE-ESCAPE-SEQUENCE-REFERENCE.md)** and **[TDV keyboard reference](docs/TDV-KEYBOARD-COMPLETE-REFERENCE.md)**
- **[Open questions](OPEN-QUESTIONS.md)** - design decisions still open
- **[Documentation guide](DOCUMENTATION-GUIDE.md)** - how the documents in this repository fit together

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to get a build running, what the code is
expected to look like, and what will get a change sent back. A security problem goes through
[SECURITY.md](SECURITY.md), never a public issue.

## Acknowledgements

- **Tandberg Data and Norsk Data** - the TDV terminal documentation this is built from
- **Digital Equipment Corporation** - the VT terminal specifications
- **Thomas Dickey** - the xterm documentation and `vttest`

## Licence

MIT. See [LICENSE](LICENSE).
