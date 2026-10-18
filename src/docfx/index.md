# RetroTerm

A terminal emulator for the machines of 1975 to 1995 - Norsk Data TDV2200, TDV2215 and
TDV1200; DEC VT100 to VT420 with Sixel and ReGIS graphics; Tektronix 4010 and 4014 - over
telnet, SSH, a serial port or a WebSocket, with Kermit file transfer, an MCP server and a
scripting language so a program can drive a session the way a person would.

The source, the releases and the build instructions are on
[GitHub](https://github.com/RetroCoreLabs/RetroTerm).

## Where to look

- **[Documentation](../../docs/MCP-AND-SCRIPTING.md)** - how to use RetroTerm: what ships with the
  exe, MCP and scripting, the ND-100 gateway protocol, the OPCOM command references, and the
  TDV keyboard and mode references written from the manuals.
- **[Manuals and source material](../../docs/spec/DEC_VT_Validation.md)** - the transcribed terminal
  manuals the emulators were written from: Tandberg and ND for the TDV family, DEC for the VT
  family, ECMA-48, Tektronix. The scanned PDFs themselves are on GitHub, linked from the pages.
- **[API reference](api/index.md)** - every namespace, type and member of the eight projects,
  generated from the XML documentation comments: `RetroTerm.Core` (emulators, parser, graphics,
  session), `RetroTerm.Core.Protocols` and its `.Net`, `.WebSocket`, `.TelnetServer` and
  `.Kermit` siblings (connections and transfer), `RetroTerm.Mcp` (the MCP server) and
  `RetroTerm.Desktop` (the Avalonia application).

The tree on the left lists every page.
