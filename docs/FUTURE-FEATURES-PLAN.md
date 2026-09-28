# Future Features Plan

## Overview

Three features planned for future phases, ordered by complexity and dependency:

| Phase | Feature | Complexity | Dependencies |
|-------|---------|-----------|--------------|
| **4a** | Scrollback with mouse wheel | Medium | None |
| **4b** | Session data logging to disk | Low-Medium | None |
| **4c** | Kermit file transfer protocol | High | Scrollback (nice-to-have), Logging (useful for debug) |

---

## Phase 4a: Scrollback with Mouse Wheel

### Current State

`TerminalBuffer` already stores scrollback data:
- `_scrollback` field: `List<TerminalCell[]>` (line 14 of TerminalBuffer.cs)
- Default max: 10,000 lines (constructor parameter `maxScrollbackLines`)
- `ScrollUp()` saves top line to scrollback when scrolling from row 0 (lines 224-239)
- `GetScrollbackLine(int index)` retrieves stored lines (lines 123-129)
- `ScrollbackLineCount` property returns count (line 36)

**What's missing**: The entire rendering pipeline assumes viewport starts at row 0. There is NO scroll offset anywhere:
- `TerminalCanvas` — no scroll offset field, no mouse wheel handler
- `TerminalRenderer.Render()` — hardcoded `for (row = 0; row < buffer.Height; row++)`
- `PixelToCell()` — no offset compensation for selection
- `MeasureOverride()` — reports only visible buffer size, not total scrollable height

### Architecture

```
Mouse Wheel Event
    │
    ▼
TerminalCanvas._scrollOffset (new field: int, 0 = live view, >0 = lines scrolled back)
    │
    ├──► TerminalRenderer.Render(context, size, scrollOffset)
    │       For each visible row:
    │         if row_index < scrollOffset → read from TerminalBuffer.GetScrollbackLine()
    │         else → read from TerminalBuffer[row - scrollOffset, col]
    │
    ├──► PixelToCell(point) adjusts row by scrollOffset
    │
    └──► Auto-scroll to bottom on new data (when scrollOffset == 0)
         Lock scroll position when scrollOffset > 0 (user is reading history)
```

### Implementation Plan

#### File 1: `src\RetroTerm.Core\Terminal\Buffer\TerminalBuffer.cs`

Add method to read a "virtual row" that spans scrollback + visible buffer:

```csharp
/// Returns the cell at the given virtual row (0 = oldest scrollback line).
/// Virtual row range: [0 .. ScrollbackLineCount + Height - 1]
public bool TryGetVirtualCell(int virtualRow, int col, out TerminalCell cell)
```

Also add:
```csharp
/// Total virtual height: scrollback lines + visible buffer height
public int TotalVirtualHeight => ScrollbackLineCount + Height;
```

#### File 2: `src\RetroTerm.Desktop\Rendering\TerminalRenderer.cs`

Modify `Render()` to accept a scroll offset:

```csharp
public void Render(DrawingContext context, Size availableSize, int scrollOffset = 0)
```

When `scrollOffset > 0`:
- Compute which virtual rows are visible
- Read cells from `TerminalBuffer.TryGetVirtualCell()` instead of direct buffer access
- Cursor rendering suppressed when scrolled back (cursor is at live position)

Modify `CalculateSize()` to optionally report full scrollable height (for ScrollViewer integration).

#### File 3: `src\RetroTerm.Desktop\Controls\TerminalCanvas.axaml.cs`

Add fields:
```csharp
private int _scrollOffset;           // 0 = live view, >0 = lines scrolled back from bottom
private const int ScrollLinesPerTick = 3;
```

Add mouse wheel handler (in constructor, alongside existing Pointer events):
```csharp
PointerWheelChanged += OnPointerWheelChanged;
```

Implement:
```csharp
private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
{
    if (_emulator == null) return;
    var buffer = _emulator.GetBuffer();
    int maxOffset = buffer.ScrollbackLineCount;

    if (e.Delta.Y > 0)
    {
        // Scroll up (into history)
        _scrollOffset = Math.Min(_scrollOffset + ScrollLinesPerTick, maxOffset);
    }
    else
    {
        // Scroll down (toward live)
        _scrollOffset = Math.Max(_scrollOffset - ScrollLinesPerTick, 0);
    }

    InvalidateVisual();
    e.Handled = true;
}
```

Modify `OnTerminalInvalidated()`:
- If `_scrollOffset == 0` (user at live position), invalidate as normal
- If `_scrollOffset > 0`, do NOT auto-scroll — user is reading history

Modify `PixelToCell()`:
- Adjust row by scroll offset for correct selection when scrolled

Add public methods:
```csharp
public void ScrollToBottom() { _scrollOffset = 0; InvalidateVisual(); }
public bool IsScrolledBack => _scrollOffset > 0;
```

Modify `Render()` call:
```csharp
_renderer.Render(context, Bounds.Size, _scrollOffset);
```

#### File 4: `src\RetroTerm.Desktop\Controls\TerminalControl.axaml.cs`

Add pass-through:
```csharp
public void ScrollToBottom() => _canvas?.ScrollToBottom();
public bool IsScrolledBack => _canvas?.IsScrolledBack ?? false;
```

#### File 5: `src\RetroTerm.Desktop\MainWindow.axaml`

Add to View menu:
```xml
<MenuItem Header="Scroll to Bottom" Click="OnScrollToBottomClick" Padding="8,6"/>
```

#### File 6: `src\RetroTerm.Desktop\MainWindow.axaml.cs`

Add handler:
```csharp
private void OnScrollToBottomClick(object? sender, RoutedEventArgs e)
{
    _terminalControl?.ScrollToBottom();
}
```

#### Visual indicator

When scrolled back, show a small overlay or status bar indicator: "↑ Scrolled back (123 lines) — scroll down or press End to return"

### Selection While Scrolled

`PixelToCell()` must map to virtual rows when scrolled. The `SelectionManager` already works with arbitrary row numbers, so selections that span scrollback + visible buffer should work if the row indices are virtual.

**Decision needed**: Should `GetSelectedCells()` return virtual row indices or buffer-relative? Virtual rows are simpler for rendering but complicate `GetSelectedText()` which reads from `TerminalBuffer`.

**Recommendation**: Add `GetVirtualSelectedText(int scrollOffset)` to `SelectionManager` or use `TryGetVirtualCell()` in the text extraction path.

### Tests

- `TerminalBuffer.TryGetVirtualCell()` — unit tests for scrollback + visible range
- `TerminalBuffer.TotalVirtualHeight` — correct after scrollback additions
- Mouse wheel scroll offset clamping (never negative, never beyond scrollback)
- Auto-scroll behavior: new data resets offset to 0 when at live position
- Selection across scrollback boundary

### Files Modified (6)

1. `src\RetroTerm.Core\Terminal\Buffer\TerminalBuffer.cs`
2. `src\RetroTerm.Desktop\Rendering\TerminalRenderer.cs`
3. `src\RetroTerm.Desktop\Controls\TerminalCanvas.axaml.cs`
4. `src\RetroTerm.Desktop\Controls\TerminalControl.axaml.cs`
5. `src\RetroTerm.Desktop\MainWindow.axaml`
6. `src\RetroTerm.Desktop\MainWindow.axaml.cs`

---

## Phase 4b: Session Data Logging to Disk

### Current State

Two logging systems exist but neither logs session data (raw bytes on the wire):

| Logger | Location | Purpose |
|--------|----------|---------|
| `ApplicationLogger` | `Core\Logging\ApplicationLogger.cs` | In-memory (200 entries), events for UI, debug messages |
| `Logger` | `Core\Diagnostics\Logger.cs` | File-based (`%AppData%\RetroTerm\Logs\`), release diagnostics |

Neither captures the actual data stream. `TerminalSession.OnConnectionDataReceived()` (line 261) logs byte hex to `ApplicationLogger` but only for debug — it's ephemeral and truncated.

### Architecture

```
IConnection.DataReceived ──► TerminalSession.OnConnectionDataReceived()
                                    │
                                    ├──► Emulator.ProcessData()    (existing)
                                    │
                                    └──► ISessionDataLogger.LogIncoming(data)  (NEW)

TerminalSession.SendInputAsync()
        │
        ├──► _connection.SendAsync()   (existing)
        │
        └──► ISessionDataLogger.LogOutgoing(data)  (NEW)
```

### Implementation Plan

#### File 1 (new): `src\RetroTerm.Core\Session\ISessionDataLogger.cs`

```csharp
/// <summary>
/// Interface for logging raw session data to persistent storage.
/// </summary>
public interface ISessionDataLogger : IDisposable
{
    bool IsLogging { get; }
    void Start(string filePath, SessionLogFormat format);
    void Stop();
    void LogIncoming(ReadOnlySpan<byte> data);
    void LogOutgoing(ReadOnlySpan<byte> data);
}

public enum SessionLogFormat
{
    /// Raw bytes as-is (binary log, replayable)
    RawBinary,

    /// Hex dump with timestamps and direction markers
    HexDump,

    /// Decoded text only (lossy — control codes stripped or represented)
    DecodedText
}
```

#### File 2 (new): `src\RetroTerm.Core\Session\FileSessionDataLogger.cs`

Implementation using `FileStream` with buffered writes:
- Thread-safe (lock or concurrent queue)
- Timestamps on each write
- Direction markers: `>>>` for outgoing, `<<<` for incoming
- Hex dump format: `[17:45:23.456] <<< 48 65 6C 6C 6F  |Hello|`
- Raw binary: just write bytes with minimal framing
- Decoded text: UTF-8 decode, replace control codes with `<ESC>`, `<CR>`, etc.
- Flush periodically or on stop

**Zero-allocation consideration**: Use `ArrayPool<byte>` for hex formatting buffer. No string allocations in the hot path for raw binary mode.

#### File 3: `src\RetroTerm.Core\Session\TerminalSession.cs`

Add field:
```csharp
private ISessionDataLogger? _dataLogger;
```

Add public property/methods:
```csharp
public ISessionDataLogger? DataLogger { get; set; }
```

Modify `OnConnectionDataReceived()` (line 261):
```csharp
private void OnConnectionDataReceived(ReadOnlyMemory<byte> data)
{
    _dataLogger?.LogIncoming(data.Span);
    Emulator.ProcessData(data.Span);
}
```

Modify `SendInputAsync()` (line 229):
```csharp
public async Task SendInputAsync(string input, CancellationToken cancellationToken = default)
{
    // ... existing validation ...
    var bytes = Encoding.UTF8.GetBytes(input);
    _dataLogger?.LogOutgoing(bytes);
    await _connection.SendAsync(bytes, cancellationToken).ConfigureAwait(false);
}
```

Also add logging in `OnTDVResponseReady()` for TDV query responses.

#### File 4: `src\RetroTerm.Desktop\MainWindow.axaml`

Add to View menu:
```xml
<Separator Background="#3C3C3C" Margin="0,4"/>
<MenuItem Header="Start Session Log..."
          Name="StartSessionLogMenuItem"
          Click="OnStartSessionLogClick"
          Padding="8,6"/>
<MenuItem Header="Stop Session Log"
          Name="StopSessionLogMenuItem"
          Click="OnStopSessionLogClick"
          IsEnabled="False"
          Padding="8,6"/>
```

#### File 5: `src\RetroTerm.Desktop\MainWindow.axaml.cs`

Add handlers:
- `OnStartSessionLogClick` — show file save dialog, pick format, start logger
- `OnStopSessionLogClick` — stop logger, update menu state
- Toggle enable/disable of Start/Stop items

#### File 6: Status bar indicator

Show "REC" or similar indicator in status bar when logging is active.

### Log Format Examples

**Hex dump** (most useful for debugging):
```
[2026-02-15 17:45:23.456] SESSION START — host=nd100.example.com:23 emulator=TDV2200
[2026-02-15 17:45:23.789] <<< 1B 5B 63                              |.[c|
[2026-02-15 17:45:23.790] >>> 1B 5B 3F 36 32 3B 31 3B 32 3B 36 63  |.[?62;1;2;6c|
[2026-02-15 17:45:24.001] <<< 48 65 6C 6C 6F                        |Hello|
```

**Raw binary**: Just the bytes, with a minimal header for direction/timestamp. Useful for replay.

**Decoded text**: Human-readable transcript.

### Tests

- `FileSessionDataLogger` unit tests: write incoming/outgoing, verify file content
- Round-trip: log data, read back, verify matches
- Thread safety: concurrent LogIncoming/LogOutgoing
- Start/Stop lifecycle
- Format tests for hex dump, decoded text
- Integration: wire into TerminalSession, verify logging during data flow

### Files Modified/Created (6)

1. `src\RetroTerm.Core\Session\ISessionDataLogger.cs` (new)
2. `src\RetroTerm.Core\Session\FileSessionDataLogger.cs` (new)
3. `src\RetroTerm.Core\Session\TerminalSession.cs` (modified)
4. `src\RetroTerm.Desktop\MainWindow.axaml` (modified)
5. `src\RetroTerm.Desktop\MainWindow.axaml.cs` (modified)
6. Tests (new test file)

---

## Phase 4c: Kermit File Transfer Protocol

### Critical Architectural Note

**Kermit is NOT a connection type.** It is a file transfer protocol that runs OVER an existing connection (Telnet or SSH). The ND-100 has a Kermit program that sends/receives files through the terminal data stream. The flow is:

```
Normal terminal mode:
  Host ←──bytes──► Telnet/SSH Connection ←──bytes──► TerminalSession ←──► Emulator

Kermit file transfer mode:
  Host ←──bytes──► Telnet/SSH Connection ←──bytes──► TerminalSession
                                                          │
                                                          ├──► Emulator (suppressed or passthrough)
                                                          │
                                                          └──► KermitEngine (intercepts data stream)
                                                                  │
                                                                  ├──► File I/O (read/write local files)
                                                                  └──► Send packets back via connection
```

Kermit packets start with SOH (0x01) followed by length + type + data + checksum. The `TerminalSession` needs a data interception layer that can:
1. Detect when Kermit transfer starts
2. Route Kermit packets to the Kermit engine
3. Route Kermit responses back through the connection
4. Return to normal terminal mode when transfer completes

### Kermit Protocol Summary

| Field | Size | Description |
|-------|------|-------------|
| MARK | 1 byte | SOH (0x01) — packet start |
| LEN | 1 byte | Packet length (char(count + 32)) |
| SEQ | 1 byte | Sequence number (char(n + 32)) mod 64 |
| TYPE | 1 byte | Packet type (S, F, D, Z, B, Y, N, E, etc.) |
| DATA | variable | Payload (printable encoding: byte + 32) |
| CHECK | 1-3 bytes | Checksum (type 1: arithmetic, type 2: 2-byte, type 3: CRC-16) |

Packet types:
- `S` — Send-Init (negotiate parameters)
- `Y` — ACK
- `N` — NAK
- `F` — File Header (filename)
- `D` — Data
- `Z` — End of File
- `B` — Break (end of transaction)
- `E` — Error

### Implementation Plan

#### New Project: `src\RetroTerm.Core.Protocols.Kermit\`

```
RetroTerm.Core.Protocols.Kermit/
├── RetroTerm.Core.Protocols.Kermit.csproj
├── KermitEngine.cs           — Main state machine (Send/Receive/Idle)
├── KermitPacket.cs           — Packet structure and serialization
├── KermitCodec.cs            — Encode/decode with printable-char encoding
├── KermitChecksum.cs         — Checksum types 1, 2, 3 (CRC-16)
├── KermitParameters.cs       — Negotiated parameters (max packet len, timeout, etc.)
├── KermitFileTransfer.cs     — File I/O operations (read/write with progress)
└── IKermitUI.cs              — Interface for progress callbacks
```

Project references: `RetroTerm.Core` only (no UI dependency).

#### File: `KermitPacket.cs`

```csharp
public readonly struct KermitPacket
{
    public byte Sequence { get; }
    public char Type { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public KermitChecksumType ChecksumType { get; }

    // Serialize to wire format (SOH + LEN + SEQ + TYPE + DATA + CHECK)
    public int WriteTo(Span<byte> buffer);

    // Parse from wire data (returns bytes consumed, or -1 if incomplete)
    public static int TryParse(ReadOnlySpan<byte> data, out KermitPacket packet);
}
```

#### File: `KermitEngine.cs`

```csharp
public class KermitEngine
{
    public enum State { Idle, SendInit, SendFile, SendData, SendEof, SendBreak,
                        ReceiveInit, ReceiveFile, ReceiveData, Complete, Error }

    public State CurrentState { get; }

    /// Feed incoming bytes from the connection. Returns bytes consumed.
    /// Unconsumed bytes should go back to the emulator.
    public int ProcessIncoming(ReadOnlySpan<byte> data);

    /// Get outgoing packet to send back to host.
    public bool TryGetResponse(Span<byte> buffer, out int bytesWritten);

    /// Start a file send operation.
    public void StartSend(string[] filePaths);

    /// Start a file receive operation.
    public void StartReceive(string destinationDirectory);

    /// Cancel current transfer.
    public void Cancel();

    /// Progress reporting
    public event Action<KermitProgress>? ProgressChanged;
    public event Action<KermitResult>? TransferCompleted;
}

public struct KermitProgress
{
    public string FileName;
    public long BytesTransferred;
    public long TotalBytes;        // -1 if unknown
    public int PacketsSent;
    public int PacketsReceived;
    public int RetryCount;
}
```

#### File: `src\RetroTerm.Core\Session\TerminalSession.cs` (modified)

Add Kermit integration:

```csharp
private KermitEngine? _kermitEngine;

public void StartKermitSend(string[] filePaths)
{
    _kermitEngine = new KermitEngine();
    _kermitEngine.ProgressChanged += OnKermitProgress;
    _kermitEngine.TransferCompleted += OnKermitCompleted;
    _kermitEngine.StartSend(filePaths);
}

public void StartKermitReceive(string destinationDirectory)
{
    _kermitEngine = new KermitEngine();
    _kermitEngine.ProgressChanged += OnKermitProgress;
    _kermitEngine.TransferCompleted += OnKermitCompleted;
    _kermitEngine.StartReceive(destinationDirectory);
}
```

Modify `OnConnectionDataReceived()`:
```csharp
private void OnConnectionDataReceived(ReadOnlyMemory<byte> data)
{
    _dataLogger?.LogIncoming(data.Span);

    if (_kermitEngine != null && _kermitEngine.CurrentState != KermitEngine.State.Idle)
    {
        int consumed = _kermitEngine.ProcessIncoming(data.Span);

        // Send Kermit response packets
        Span<byte> responseBuffer = stackalloc byte[1024];
        while (_kermitEngine.TryGetResponse(responseBuffer, out int written))
        {
            _connection!.SendAsync(responseBuffer.Slice(0, written).ToArray()).GetAwaiter().GetResult();
        }

        // Pass unconsumed bytes to emulator (interleaved terminal output)
        if (consumed < data.Length)
        {
            Emulator.ProcessData(data.Span.Slice(consumed));
        }

        // Check if transfer completed
        if (_kermitEngine.CurrentState == KermitEngine.State.Complete ||
            _kermitEngine.CurrentState == KermitEngine.State.Error)
        {
            _kermitEngine = null;
        }
        return;
    }

    Emulator.ProcessData(data.Span);
}
```

#### UI: File menu additions

`src\RetroTerm.Desktop\MainWindow.axaml`:
```xml
<!-- In File menu, before Exit -->
<Separator Background="#3C3C3C" Margin="0,4"/>
<MenuItem Header="Send File (Kermit)..."
          Name="KermitSendMenuItem"
          Click="OnKermitSendClick"
          IsEnabled="False"
          Padding="8,6"/>
<MenuItem Header="Receive File (Kermit)..."
          Name="KermitReceiveMenuItem"
          Click="OnKermitReceiveClick"
          IsEnabled="False"
          Padding="8,6"/>
```

Enable only when connected. Disable during active transfer.

#### UI: Progress dialog (new)

`src\RetroTerm.Desktop\Views\KermitTransferDialog.axaml`:
- File name display
- Progress bar (bytes transferred / total)
- Packet count, retry count
- Cancel button
- Auto-close on completion with success/error message

#### File: `src\RetroTerm.Desktop\MainWindow.axaml.cs` (modified)

Add handlers:
```csharp
private async void OnKermitSendClick(object? sender, RoutedEventArgs e)
{
    // Show file picker
    // Start KermitEngine send
    // Show progress dialog
}

private async void OnKermitReceiveClick(object? sender, RoutedEventArgs e)
{
    // Show folder picker
    // Start KermitEngine receive
    // Show progress dialog
}
```

Update `UpdateMenuState()` to enable/disable Kermit items based on connection state.

### Kermit Encoding Details

All data bytes are "printable encoded": each byte is converted to a printable ASCII character by adding 32. Control characters (0x00-0x1F) are prefixed with `#` and XORed with 0x40. 8-bit characters (if enabled) are prefixed with `&`.

The `KermitCodec` class handles:
- `Encode(ReadOnlySpan<byte> raw, Span<byte> encoded)` — raw to printable
- `Decode(ReadOnlySpan<byte> encoded, Span<byte> raw)` — printable to raw
- Repeat-count compression (prefix `~` + count + char)

### Tests

**Unit tests (no network needed):**
- `KermitPacket.TryParse()` — valid packets, truncated packets, invalid checksums
- `KermitPacket.WriteTo()` — serialize and verify wire format
- `KermitCodec.Encode/Decode` — round-trip for all byte values (0x00-0xFF)
- `KermitChecksum` — type 1, 2, 3 against known test vectors
- `KermitEngine` state machine — simulate complete send/receive handshake
- `KermitParameters` — Send-Init negotiation

**Integration tests:**
- Full send: create temp file, run KermitEngine send, capture packets, verify file reconstructed
- Full receive: feed packets to KermitEngine, verify file written correctly
- Error handling: NAK retransmission, timeout recovery, cancel mid-transfer
- Interleaved data: terminal output mixed with Kermit packets

### Files Created/Modified

**New project (7 files):**
1. `src\RetroTerm.Core.Protocols.Kermit\RetroTerm.Core.Protocols.Kermit.csproj`
2. `src\RetroTerm.Core.Protocols.Kermit\KermitEngine.cs`
3. `src\RetroTerm.Core.Protocols.Kermit\KermitPacket.cs`
4. `src\RetroTerm.Core.Protocols.Kermit\KermitCodec.cs`
5. `src\RetroTerm.Core.Protocols.Kermit\KermitChecksum.cs`
6. `src\RetroTerm.Core.Protocols.Kermit\KermitParameters.cs`
7. `src\RetroTerm.Core.Protocols.Kermit\IKermitUI.cs`

**Modified files (4):**
1. `src\RetroTerm.Core\Session\TerminalSession.cs`
2. `src\RetroTerm.Desktop\MainWindow.axaml`
3. `src\RetroTerm.Desktop\MainWindow.axaml.cs`
4. `RetroTerm.sln` (add new project)

**New UI (2):**
1. `src\RetroTerm.Desktop\Views\KermitTransferDialog.axaml`
2. `src\RetroTerm.Desktop\Views\KermitTransferDialog.axaml.cs`

**New tests:**
1. `tests\RetroTerm.Tests\Kermit\KermitPacketTests.cs`
2. `tests\RetroTerm.Tests\Kermit\KermitCodecTests.cs`
3. `tests\RetroTerm.Tests\Kermit\KermitChecksumTests.cs`
4. `tests\RetroTerm.Tests\Kermit\KermitEngineTests.cs`
5. `tests\RetroTerm.Tests\Kermit\KermitIntegrationTests.cs`

---

## Dependency Graph

```
Phase 4a (Scrollback)          Phase 4b (Data Logging)
       │                              │
       │                              │
       └──────────┬───────────────────┘
                  │
                  ▼
           Phase 4c (Kermit)
           - Benefits from scrollback (see transfer output scroll back)
           - Benefits from data logging (debug Kermit packet exchange)
           - But neither is a hard dependency
```

**Recommended order**: 4a → 4b → 4c (or 4b → 4a → 4c if logging is more urgent).

Phase 4a and 4b are independent and could be done in parallel. Phase 4c benefits from both but doesn't require them.
