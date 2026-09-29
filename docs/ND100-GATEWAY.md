# ND-100 Gateway WebSocket Protocol

RetroTerm can act as a direct terminal client for the ND-100 emulator via the Gateway WebSocket Protocol. Instead of connecting through an external Node.js gateway server, RetroTerm hosts its own WebSocket listener that the ND-100 emulator connects to.

## Architecture

```
ND-100 Emulator ──WebSocket──▸ GatewayListener (singleton, background)
                                    │
                    routes by identCode
                                    │
                  ┌─────────────────┼─────────────────┐
                  ▼                 ▼                  ▼
          GatewayConnection   GatewayConnection   GatewayConnection
          (identCode=43)      (identCode=44)      (identCode=45)
               │                   │                   │
          TerminalSession     TerminalSession     TerminalSession
               │                   │                   │
          TDV2200Emulator     TDV2200Emulator     TDV2200Emulator
```

### Key Classes

- **GatewayListener** (`src\RetroTerm.Core.Protocols.WebSocket\Gateway\GatewayListener.cs`)
  Singleton WebSocket server. Manages the single emulator WebSocket connection, holds the registered terminal list, and routes messages by identCode.

- **GatewayConnection** (`src\RetroTerm.Core.Protocols.WebSocket\Gateway\GatewayConnection.cs`)
  Per-terminal `IConnection` implementation. Each instance represents a connection to one terminal device (identCode), routed through the shared GatewayListener.

- **GatewaySettings** (`src\RetroTerm.Core.Protocols.WebSocket\Gateway\GatewaySettings.cs`)
  Configuration model (`Enabled`, `Port`). Persisted to `%AppData%\RetroTerm\gateway-settings.json`.

- **GatewayTerminalInfo** (`src\RetroTerm.Core.Protocols.WebSocket\Gateway\GatewayTerminalInfo.cs`)
  Data class for registered terminals (`IdentCode`, `Name`, `LogicalDevice`).

## Configuration

1. Open **Connection > Gateway Settings...**, or the **Gateway** tab of Preferences
   (`src\RetroTerm.Desktop\Views\PreferencesWindow.axaml`). The Preferences tab holds the same
   enable switch and port, plus the Ethernet mapping described below.
2. Enable the gateway and set the port (default: 8765)
3. Click Apply — the listener starts immediately

Settings are persisted to `%AppData%\RetroTerm\gateway-settings.json`.

### Ethernet

Besides terminals and disks, the gateway carries the emulated machine's Ethernet (frame types
`0x30`, `0x31` and `0x32`). How a frame leaves the machine is set in Preferences > Gateway: not
mapped, a real host adapter (pcap), join a TCP/RETH segment, host a TCP/RETH segment, or a UDP
multicast group. HDLC frame types (`0x10` to `0x12`) are not implemented. What has and has not
been run against real hardware is listed in `docs\PLAN.md`, section "Gateway Ethernet".

## Connecting

1. Enable the gateway (see above)
2. Start the ND-100 emulator and point it at `ws://localhost:8765`
3. The status bar shows "GW: N terminals" when the emulator connects and registers
4. Open **Connection > Connect...** (or Ctrl+Shift+N)
5. Select **Gateway** protocol
6. Pick a terminal from the list
7. Click Connect

Each terminal (identCode) supports only one connection at a time, like a physical serial port. The connection dialog shows "(In Use)" for terminals that are already connected in another tab.

## WebSocket Protocol

The protocol uses a mix of binary and JSON text WebSocket frames. High-frequency terminal I/O uses binary frames for minimal overhead. Infrequent control messages use JSON text frames.

### Binary Frames (high-frequency terminal I/O)

Format: `[type:1 byte][identCode:1 byte][data:N bytes]`

| Type | Direction | Description |
|------|-----------|-------------|
| `0x01` | RetroTerm → Emulator | **term-input** — keyboard data from terminal to host |
| `0x02` | Emulator → RetroTerm | **term-output** — display data from host to terminal |

Example: sending "ABC" (0x41, 0x42, 0x43) to identCode 43:
```
Binary frame: [0x01] [0x2B] [0x41] [0x42] [0x43]
               type   id=43   'A'    'B'    'C'
```

A 100-byte terminal output = 102 bytes on the wire (2-byte header + raw data). No JSON parsing required.

### JSON Text Frames (control messages)

#### Emulator → RetroTerm

**`register`** — Sent after the emulator connects. Lists all available terminal devices.
```json
{
  "type": "register",
  "terminals": [
    { "identCode": 43, "name": "TERMINAL 12", "logicalDevice": 1 },
    { "identCode": 44, "name": "TERMINAL 13", "logicalDevice": 2 }
  ]
}
```

#### RetroTerm → Emulator

**`client-connected`** — Sent when a RetroTerm tab connects to a terminal (establishes carrier).
```json
{
  "type": "client-connected",
  "identCode": 43
}
```

**`client-disconnected`** — Sent when a RetroTerm tab disconnects from a terminal (drops carrier).
```json
{
  "type": "client-disconnected",
  "identCode": 43
}
```

### Connection Rules

- Only **one emulator** can connect at a time. A second WebSocket connection is rejected with close code 4000.
- Each **identCode** supports exactly one active `GatewayConnection` (exclusive access).
- If the emulator disconnects, all active `GatewayConnection` instances are notified and transition to `Disconnected` state.

## Debugging

Open **Connection > Gateway Debug...** to see real-time WebSocket traffic:

- **TX** (teal) — messages sent to the emulator
- **RX** (yellow) — messages received from the emulator
- **EVT** (purple) — lifecycle events (connect, disconnect, registration)

The debug window shows timestamps, message types, identCodes, data sizes, and hex dumps for term-input/term-output data.

## Implementation Details

### No External Libraries
Uses .NET 10 built-in APIs:
- `TcpListener` for TCP accept
- Manual HTTP upgrade handshake (~20 lines)
- `WebSocket.CreateFromStream()` for WebSocket framing

### Thread Safety
- `ConcurrentDictionary<int, GatewayConnection>` for active connections
- `Channel<T>`-based send queue — single dedicated sender loop, multiple writers from any thread (WebSocket does not support concurrent `SendAsync`)
- `lock` around terminal list and emulator socket access
- All UI events dispatched to Avalonia's `Dispatcher.UIThread`

### Connection Factory Bypass
`GatewayConnection` needs a reference to the live `GatewayListener` singleton, so it cannot be created through the reflection-based `ConnectionFactory`. Instead, `MainWindow.ConnectGatewayAsync` creates the connection directly.

## Status Bar

The status bar shows gateway state in the rightmost column:
- **GW: :8765** — listening but no emulator connected
- **GW: 5 terminals** — emulator connected with 5 registered terminals

## Tests

40 unit tests in `tests\RetroTerm.Tests\Gateway\` (counted 29 September 2026), plus 4 in
`tests\RetroTerm.Tests\Commands\GatewayCommandTests.cs` for the GATEWAY script and MCP command:
- `GatewayListenerTests` — listener lifecycle, register parsing, message routing, exclusive emulator connection
- `GatewayConnectionTests` — IConnection lifecycle, client-connected/disconnected messages, exclusive access, send/receive, emulator disconnect handling
- `GatewayEthernetTests` — the RETH handshake and length prefix, the mapping spec and its backend factory, a hub and client carrying a frame between them
- `GatewaySettingsTests` — default values, data storage

Run with:
```powershell
dotnet test tests\RetroTerm.Tests\RetroTerm.Tests.csproj --filter "FullyQualifiedName~Gateway"
```
