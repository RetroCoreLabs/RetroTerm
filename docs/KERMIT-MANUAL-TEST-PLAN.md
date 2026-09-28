# Kermit File Transfer — Manual Test Plan

## Implementation Summary

Add Kermit file transfer integration with UI, settings, and protocol engine.

Full in-band file transfer support via the Kermit protocol:

**Core layer:**
- `IFileTransferHandler` interface with `TransferProgress`, `TransferState`,
  `SendBytesAsync` delegate for protocol-agnostic transfer abstraction
- `TerminalSession` transfer mode: data routing intercept during active
  transfer, `StartFileTransferAsync`/`CancelFileTransfer`, progress events

**Kermit protocol engine (`RetroTerm.Core.Protocols.Kermit`):**
- State machine: Send-Init/FileHeader/Data/EOF/Break handshake
- `KermitEncoding`: control char quoting, 8th-bit quoting for 7-bit channels
- `KermitParameters`: Send-Init field negotiation (MAXL, TIME, QBIN, CHKT)
- `KermitChecksum`: type 1 (6-bit), type 2 (12-bit), type 3 (CRC-CCITT)
- Parity support: Even/Odd/Mark/Space with apply/strip on wire bytes
- `KermitFileTransfer` bridge: wires `IFileTransferHandler` to `KermitEngine`,
  implements `IKermitFileHandler` for disk I/O, timeout timer, file collision
  handling (rename/overwrite/skip), sender delay, filename sanitization

**Desktop UI:**
- Transfer menu (Send/Receive/Cancel/Settings) between Connection and Keyboard
- `TransferSettingsWindow`: channel (parity, force 8-bit quoting), timing
  (delay, timeout, retries), file handling (collision mode), advanced
  (block check type, max packet size) — settings persist across transfers
- `FileTransferProgressWindow`: non-modal progress with state, filename,
  progress bar, byte count, file count, error display, cancel/close
- XFR status bar indicator during active transfer
- Menu state management: send/receive enabled when connected, cancel
  enabled when transferring

**Tests:** 106 Kermit protocol tests covering encoding, checksums, parameters,
7-bit channel scenarios, nd-kermit patterns, engine state machine

### Architecture

```
┌─────────────────────────────────────────────┐
│  Desktop UI (Avalonia)                      │
│  TransferSettingsWindow → KermitOptions     │
│  MainWindow (menu, XFR indicator)           │
│  FileTransferProgressWindow                 │
├─────────────────────────────────────────────┤
│  Session Layer                              │
│  TerminalSession.StartFileTransferAsync()   │
│  Data routing: transfer mode ↔ emulator     │
├─────────────────────────────────────────────┤
│  Integration Layer                          │
│  KermitFileTransfer : IFileTransferHandler  │
│  Timeout timer, file collision, delay       │
│  IKermitFileHandler disk I/O                │
├─────────────────────────────────────────────┤
│  Protocol Layer                             │
│  KermitEngine (state machine)               │
│  KermitEncoding, KermitParameters           │
│  KermitChecksum, KermitPacket               │
└─────────────────────────────────────────────┘
```

### Settings Flow

```
UI TransferSettingsWindow
    ↓ populates
KermitOptions (parity, force8BitQuoting, timeout, maxRetries, blockCheckType, delay, maxPacketSize)
    ↓ passed to
KermitFileTransfer (also receives FileCollisionMode)
    ↓ creates
KermitEngine(options, fileHandler)
    ↓ negotiates via
KermitParameters.FromOptions() → Send-Init S/Y packet exchange
```

### nd-kermit SET Command Mapping

| nd-kermit SET | UI Field | KermitOptions Property | Wired To |
|---------------|----------|----------------------|----------|
| SET DELAY | Send delay spinner | `.Delay` | `KermitFileTransfer.StartSendAsync()` — `Task.Delay()` |
| SET FILE-WARNING | If file exists dropdown | N/A (on `KermitFileTransfer`) | `OpenFileForWrite()` collision logic |
| SET RECEIVE TIMEOUT | Timeout spinner | `.Timeout` | Send-Init TIME field + timeout timer → `NotifyTimeout()` |
| SET SEND PACKET-LENGTH | Max packet size spinner | `.MaxReceivePacketSize` | Send-Init MAXL field, negotiated |
| SET USE-8-BIT-QUOTE | Force 8-bit quoting checkbox | `.Force8BitQuoting` | Send-Init QBIN field = '&', negotiated |
| SET BLOCK-CHECK | Block check dropdown | `.BlockCheckType` | Send-Init CHKT field, negotiated |

### Known Gaps (for core Kermit team)

- **Block check type 2/3 in engine**: `BuildPacket()` and `TryExtractPacket()` use `ComputeType1` only.
  The CHKT field is advertised correctly in Send-Init, but the engine doesn't switch checksum
  computation after negotiation. Need to use negotiated type for all packets after S/Y exchange.
- **Padding on send**: Engine doesn't prepend NPAD×PADC before packets (low priority, TCP doesn't need it).
- **Batch send**: Engine handles one file per `BeginSend()`. Multi-file batch requires new engine per file
  or extending the engine to support F→D→Z→F→D→Z→B sequences.

---

## Prerequisites

- RetroTerm built and running
- A Telnet/SSH host available for connection testing (or TestServer)
- A host running Kermit (nd-kermit, C-Kermit, or G-Kermit) for end-to-end tests
- A few test files of varying sizes (e.g., small.txt 100B, medium.bin 50KB, large.dat 5MB)
- A binary file with all 256 byte values for 8-bit quoting verification

---

## 1. Menu Visibility and Placement

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 1.1 | Launch RetroTerm, inspect menu bar | "Transfer" menu appears between "Connection" and "Keyboard" | |
| 1.2 | Click "Transfer" menu | Five items visible: "Send File(s)...", "Receive File(s)...", separator, "Cancel Transfer", separator, "Settings..." | |

---

## 2. Menu State — Disconnected

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 2.1 | With no connection, open Transfer menu | "Send File(s)..." is disabled (greyed out) | |
| 2.2 | | "Receive File(s)..." is disabled | |
| 2.3 | | "Cancel Transfer" is disabled | |
| 2.4 | | "Settings..." is always enabled | |

---

## 3. Menu State — Connected, No Transfer Active

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 3.1 | Connect to a host, open Transfer menu | "Send File(s)..." is enabled | |
| 3.2 | | "Receive File(s)..." is enabled | |
| 3.3 | | "Cancel Transfer" is disabled | |

---

## 4. Menu State — After Disconnect

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 4.1 | Connect, then disconnect, open Transfer menu | "Send File(s)..." returns to disabled | |
| 4.2 | | "Receive File(s)..." returns to disabled | |

---

## 5. Status Bar — XFR Indicator

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 5.1 | Disconnected state | "XFR" indicator is NOT visible in status bar | |
| 5.2 | Connected, no transfer | "XFR" indicator is NOT visible | |
| 5.3 | Transfer active | "XFR" indicator appears in blue, left of REC indicator area | |
| 5.4 | Transfer completes or is cancelled | "XFR" indicator disappears | |

---

## 6. Transfer Settings Dialog — Layout

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 6.1 | Click Transfer > Settings... | Settings dialog opens with title "Transfer Settings" | |
| 6.2 | Channel section visible | Parity dropdown (None/Even/Odd/Mark/Space) and "Force 8-bit quoting" checkbox | |
| 6.3 | Timing section visible | Send delay (0-30), Timeout (1-60), Max retries (0-99) spinners | |
| 6.4 | File Handling section visible | "If file exists" dropdown: Rename/Overwrite/Skip | |
| 6.5 | Advanced section visible | Block check dropdown (1/2/3 CRC), Max packet size (20-94) spinner | |
| 6.6 | Buttons | OK and Cancel at bottom-right | |

---

## 7. Transfer Settings Dialog — Defaults

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 7.1 | Open settings for the first time | Parity = None | |
| 7.2 | | Force 8-bit quoting = unchecked | |
| 7.3 | | Send delay = 0 | |
| 7.4 | | Timeout = 8 | |
| 7.5 | | Max retries = 10 | |
| 7.6 | | If file exists = Rename | |
| 7.7 | | Block check = 1 | |
| 7.8 | | Max packet size = 80 | |

---

## 8. Transfer Settings Dialog — Persistence Within Session

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 8.1 | Change settings, click OK, reopen settings | All changed values are preserved | |
| 8.2 | Change settings, click Cancel, reopen settings | Previous values are preserved (changes discarded) | |
| 8.3 | Change settings via Settings..., then use Send File(s)... | Settings dialog in Send flow shows the last-saved values | |
| 8.4 | Change settings during Send flow, then use Receive | Settings dialog in Receive flow shows values from the Send flow | |

---

## 9. Transfer Settings Dialog — Before Send/Receive

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 9.1 | Click Send File(s)... while connected | Settings dialog appears FIRST, before file picker | |
| 9.2 | Click OK in settings dialog | File picker opens after settings dialog closes | |
| 9.3 | Click Cancel in settings dialog | No file picker appears, transfer does not start | |
| 9.4 | Click Receive File(s)... while connected | Settings dialog appears FIRST, before folder picker | |
| 9.5 | Click OK in settings dialog | Folder picker opens after settings dialog closes | |
| 9.6 | Click Cancel in settings dialog | No folder picker appears, transfer does not start | |

---

## 10. Send File(s) — File Picker

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 10.1 | After settings OK, file picker opens | Title says "Select Files to Send", multi-select is enabled | |
| 10.2 | Cancel the file picker without selecting | No progress window opens, no error, terminal resumes normally | |
| 10.3 | Select one file and confirm | Progress window opens, transfer begins | |
| 10.4 | Select multiple files and confirm | Progress window opens with batch transfer | |

---

## 11. Receive File(s) — Folder Picker

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 11.1 | After settings OK, folder picker opens | Title says "Select Save Directory" | |
| 11.2 | Cancel the folder picker without selecting | No progress window opens, no error | |
| 11.3 | Select a folder and confirm | Progress window opens, receiver enters waiting state | |

---

## 12. Progress Window — Layout and Initial State

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 12.1 | Send initiated | Window title is "File Transfer", direction label says "Sending File(s)" | |
| 12.2 | Receive initiated | Direction label says "Receiving File(s)" | |
| 12.3 | Initial state | State shows "Initializing" in teal/turquoise | |
| 12.4 | | File name shows "-" initially | |
| 12.5 | | Progress bar at 0% | |
| 12.6 | | Cancel button is enabled | |
| 12.7 | | Close button is disabled | |
| 12.8 | Window is non-modal | Can click and interact with main terminal window behind it | |

---

## 13. Progress Window — During Transfer

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 13.1 | Transfer in progress with known file size (send) | Progress bar shows determinate progress (fills proportionally) | |
| 13.2 | | Bytes label shows "X / Y" format (e.g., "12.5 KB / 50.0 KB") | |
| 13.3 | Transfer in progress with unknown file size (receive) | Progress bar is indeterminate (animated) | |
| 13.4 | | Bytes label shows transferred amount only | |
| 13.5 | Batch transfer (multiple files) | "File X of Y" counter appears | |
| 13.6 | Single file transfer | File count row is hidden | |
| 13.7 | State label | Shows "Transferring" during active transfer | |
| 13.8 | File name updates | Shows current file name as each file starts | |

---

## 14. Progress Window — Completion States

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 14.1 | Transfer completes successfully | State shows "Completed" in green | |
| 14.2 | | Progress bar at 100% | |
| 14.3 | | Cancel button becomes disabled | |
| 14.4 | | Close button becomes enabled | |
| 14.5 | Transfer fails (remote error, file open failure) | State shows "Failed" in salmon/red | |
| 14.6 | | Error border appears with red background | |
| 14.7 | | Error message text is displayed | |
| 14.8 | | Cancel disabled, Close enabled | |
| 14.9 | Transfer cancelled by user | State shows "Cancelled" in orange | |
| 14.10 | | Cancel disabled, Close enabled | |

---

## 15. Cancel Transfer

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 15.1 | Click Cancel button in progress window during transfer | Transfer aborts, state changes to Cancelled | |
| 15.2 | Click Transfer > Cancel Transfer menu item during transfer | Same as 15.1 | |
| 15.3 | Close progress window (X button) during active transfer | Transfer is cancelled automatically | |
| 15.4 | After cancellation | Terminal resumes normal operation (incoming data goes to emulator) | |

---

## 16. Transfer Mode — Data Routing

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 16.1 | During active transfer, host sends terminal data | Data is NOT displayed on terminal (routed to Kermit handler) | |
| 16.2 | After transfer completes | Terminal displays incoming data normally again | |
| 16.3 | After transfer is cancelled | Terminal displays incoming data normally again | |

---

## 17. Tab Switching During Transfer

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 17.1 | Start transfer on Tab 1, switch to Tab 2 | Tab 2 operates normally, transfer continues on Tab 1 | |
| 17.2 | Switch back to Tab 1 during transfer | XFR indicator reflects Tab 1's transfer state | |
| 17.3 | Transfer menu reflects active tab | Send/Receive disabled on Tab 1 (transferring), enabled on Tab 2 (connected, not transferring) | |

---

## 18. 8-Bit Quoting — Channel Settings

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 18.1 | Settings: Parity=None, Force 8-bit quoting=OFF. Send ASCII text file | Transfer succeeds, file matches original | |
| 18.2 | Settings: Parity=None, Force 8-bit quoting=OFF. Send binary file (all 256 bytes) | Transfer succeeds on 8-bit clean link, file matches | |
| 18.3 | Settings: Parity=None, Force 8-bit quoting=ON. Send binary file | Transfer succeeds, 8-bit quoting negotiated (QBIN='&') | |
| 18.4 | Settings: Parity=Even, Force 8-bit quoting=OFF. Send binary file | 8-bit quoting auto-enabled due to parity, binary file transfers correctly | |
| 18.5 | Remote is nd-kermit with SET USE-8-BIT-QUOTE. Our side: Force 8-bit quoting=ON | Both advertise '&', negotiation succeeds, binary data survives 7-bit path | |
| 18.6 | Remote does NOT support 8-bit quoting (sends N/space in QBIN). Our side: Force=ON | Negotiation falls back to no quoting. ASCII transfers work, binary high bytes lost | |

---

## 19. Sender Delay

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 19.1 | Settings: Send delay=0. Start Send | Transfer begins immediately, no visible pause | |
| 19.2 | Settings: Send delay=5. Start Send | Progress window shows "Initializing", ~5 second pause before first packet | |
| 19.3 | Settings: Send delay=5. Cancel during delay | Transfer cancels cleanly, no packet sent | |

---

## 20. File Collision (Receive)

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 20.1 | Receive file "test.txt" when it does NOT exist | File created normally | |
| 20.2 | Settings: If file exists=Rename. Receive "test.txt" when it already exists | New file saved as "test_1.txt" (or _2, _3, etc.) | |
| 20.3 | Settings: If file exists=Overwrite. Receive "test.txt" when it already exists | Existing file overwritten with new content | |
| 20.4 | Settings: If file exists=Skip. Receive "test.txt" when it already exists | File skipped, transfer continues to next file (or completes) | |
| 20.5 | Receive file with remote path separators (e.g., "PACK-1:MYFILE.DAT") | Path stripped, saved as "MYFILE.DAT" in chosen directory | |
| 20.6 | Receive file with invalid characters in name | Invalid chars replaced with underscore | |

---

## 21. Timeout and Retries

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 21.1 | Settings: Timeout=8, Max retries=10. Normal transfer | Transfer completes without retries | |
| 21.2 | Remote stops responding mid-transfer | After timeout × max retries, transfer fails with "Too many retries" | |
| 21.3 | Settings: Max retries=0 (unlimited). Remote temporarily unresponsive | Transfer retries indefinitely until remote recovers or user cancels | |
| 21.4 | Timeout value sent in Send-Init TIME field | Verify with protocol trace: TIME field matches configured timeout | |

*Note: Timeout timer implementation is the core Kermit team's responsibility. These tests depend on that being complete.*

---

## 22. Block Check Type

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 22.1 | Settings: Block check=1. Transfer file | Type 1 (6-bit) checksum used, transfer succeeds | |
| 22.2 | Settings: Block check=3 (CRC). Remote supports CRC | CRC-16 negotiated, transfer succeeds | |
| 22.3 | Settings: Block check=3 (CRC). Remote only supports type 1 | Graceful fallback to type 1, transfer succeeds | |
| 22.4 | Introduce bit errors on link with type 1 | Errors occasionally missed (6-bit checksum is weak) | |
| 22.5 | Introduce bit errors on link with type 3 (CRC) | Errors reliably detected, packets retransmitted | |

*Note: Block check type 2/3 negotiation in the engine is the core Kermit team's responsibility. Tests 22.2–22.5 depend on that being complete.*

---

## 23. Packet Size

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 23.1 | Settings: Max packet size=80 (default). Transfer 5MB file | Transfer works, ~77 data bytes per packet | |
| 23.2 | Settings: Max packet size=94 (maximum short). Transfer 5MB file | Transfer works, slightly fewer packets than 23.1 | |
| 23.3 | Settings: Max packet size=20 (minimum). Transfer file | Transfer works but slowly (many small packets) | |
| 23.4 | Remote advertises smaller max than our setting | Engine uses the smaller of the two (negotiated down) | |

---

## 24. End-to-End: RetroTerm ↔ nd-kermit

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 24.1 | RetroTerm sends ASCII text file to nd-kermit RECEIVE | File arrives intact, content matches | |
| 24.2 | nd-kermit SEND file, RetroTerm receives | File arrives intact in chosen directory | |
| 24.3 | Binary file transfer with Force 8-bit quoting=ON, nd-kermit SET USE-8-BIT-QUOTE | Binary file transfers correctly over 7-bit path | |
| 24.4 | SINTRAN filename with colons (e.g., "PACK-1:MYFILE.DAT") | Path stripped on receive, saved as "MYFILE.DAT" | |
| 24.5 | Large file (>64KB) | Multi-packet transfer completes, all data intact | |
| 24.6 | Transfer in both directions sequentially | First transfer completes, second starts cleanly | |

---

## 25. End-to-End: RetroTerm ↔ C-Kermit / G-Kermit

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 25.1 | Send text file to C-Kermit | Transfer succeeds | |
| 25.2 | Receive file from C-Kermit | Transfer succeeds | |
| 25.3 | C-Kermit with SET BLOCK-CHECK 3. Our side: Block check=3 | CRC negotiated, transfer succeeds | |
| 25.4 | C-Kermit with SET PARITY EVEN. Our side: Parity=Even | Parity applied, 8-bit quoting auto-enabled, binary file OK | |

---

## 26. Edge Cases

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 26.1 | Disconnect during active transfer | Transfer is cleaned up, no crash, XFR indicator hidden | |
| 26.2 | Close tab during active transfer | Transfer cancelled, tab closes cleanly | |
| 26.3 | Close application during active transfer | Clean shutdown, no hang | |
| 26.4 | Start transfer when already transferring | "A file transfer is already in progress" error, not a crash | |
| 26.5 | Send a 0-byte empty file | Transfer completes (S→F→Z→B), empty file created on remote | |
| 26.6 | Receive into a read-only directory | Transfer fails with error message, no crash | |
| 26.7 | Send a file that doesn't exist (race condition: deleted after picker) | Transfer fails with "Cannot open file" error, no crash | |
| 26.8 | Remote sends Error (E) packet mid-transfer | Transfer fails, error message from remote displayed | |

---

## 27. Byte Formatting in Progress Window

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 27.1 | < 1 KB transferred | Shows bytes (e.g., "512 B") | |
| 27.2 | 1 KB – 1 MB | Shows KB (e.g., "125.4 KB") | |
| 27.3 | 1 MB – 1 GB | Shows MB (e.g., "3.2 MB") | |
| 27.4 | > 1 GB | Shows GB (e.g., "1.50 GB") | |

---

## Notes

- Sections 21 (timeout timer) and 22 (block check 2/3) depend on core Kermit engine work that is the engine team's responsibility.
- The settings dialog appears before every Send/Receive to give the user a chance to adjust channel settings. Settings persist within the session (remembered between transfers).
- For nd-kermit interop testing: the ND-100 serial line uses 7-bit even parity at the hardware level. When connecting through a TCP-to-serial gateway, set `Force 8-bit quoting = ON` on our side (not `Parity = Even`, because the gateway handles parity). On the nd-kermit side: `SET USE-8-BIT-QUOTE`.
- File collision only affects receive operations. The dropdown is visible but irrelevant when sending.
