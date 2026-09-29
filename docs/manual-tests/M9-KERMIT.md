# M9 — Kermit file transfer

**Full path:** `docs\manual-tests\M9-KERMIT.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Machine cover:** `tests\RetroTerm.Core.Protocols.Kermit.Tests` — 123 tests over encoding, the
three block-check types, Send-Init negotiation, 7-bit channels, nd-kermit patterns and the engine
state machine. The transfer settings live in **Preferences, Kermit tab**; there is no separate
transfer settings window, and Send and Receive read the preferences as they are when clicked.

The protocol engine is `src\RetroTerm.Core.Protocols.Kermit\KermitEngine.cs`. It negotiates the
block check type in the Send-Init exchange (the lower of the two sides, falling back to type 1) and
then computes and verifies type 1, 2 or 3 on every packet; `KermitFileTransfer.cs` runs the timeout
timer and the file-collision handling. So nothing in this document depends on engine work that is
still to come.

---

## nd-kermit SET commands and where each lives here

| nd-kermit SET | Preferences, Kermit tab | KermitOptions Property | Wired To |
|---------------|-------------------------|----------------------|----------|
| SET DELAY | Send delay | `.Delay` | `KermitFileTransfer.StartSendAsync()` — `Task.Delay()` |
| SET FILE-WARNING | If file exists | N/A (on `KermitFileTransfer`) | `OpenFileForWrite()` collision logic |
| SET RECEIVE TIMEOUT | Timeout | `.Timeout` | Send-Init TIME field + timeout timer → `NotifyTimeout()` |
| SET SEND PACKET-LENGTH | Max packet size | `.MaxReceivePacketSize` | Send-Init MAXL field, negotiated |
| SET USE-8-BIT-QUOTE | Force 8-bit quoting | `.Force8BitQuoting` | Send-Init QBIN field = '&', negotiated |
| SET BLOCK-CHECK | Block check | `.BlockCheckType` | Send-Init CHKT field, negotiated, then used on every packet |

---

## Prerequisites

- RetroTerm built and running (`.\scripts\publish.ps1`, then `publish\current\RetroTerm.Desktop.exe`)
- A Telnet/SSH host available for connection testing (or TestServer)
- A host running Kermit (nd-kermit, C-Kermit, or G-Kermit) for end-to-end tests
- A few test files of varying sizes (e.g., small.txt 100B, medium.bin 50KB, large.dat 5MB)
- A binary file with all 256 byte values for 8-bit quoting verification

---

## M9.1 — Menu visibility and placement

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 1.1 | Launch RetroTerm, inspect menu bar | "Transfer" menu appears between "Connection" and "Keyboard" | |
| 1.2 | Click "Transfer" menu | Three items: "Send File(s)...", "Receive File(s)...", separator, "Cancel Transfer". Settings are NOT here; they are in Preferences, Kermit tab | |

---

## M9.2 — Menu state, disconnected

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 2.1 | With no connection, open Transfer menu | "Send File(s)..." is disabled (greyed out) | |
| 2.2 | | "Receive File(s)..." is disabled | |
| 2.3 | | "Cancel Transfer" is disabled | |

---

## M9.3 — Menu state, connected, no transfer active

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 3.1 | Connect to a host, open Transfer menu | "Send File(s)..." is enabled | |
| 3.2 | | "Receive File(s)..." is enabled | |
| 3.3 | | "Cancel Transfer" is disabled | |

---

## M9.4 — Menu state, after disconnect

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 4.1 | Connect, then disconnect, open Transfer menu | "Send File(s)..." returns to disabled | |
| 4.2 | | "Receive File(s)..." returns to disabled | |

---

## M9.5 — Status bar, XFR indicator

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 5.1 | Disconnected state | "XFR" indicator is NOT visible in status bar | |
| 5.2 | Connected, no transfer | "XFR" indicator is NOT visible | |
| 5.3 | Transfer active | "XFR" indicator appears in blue, left of REC indicator area | |
| 5.4 | Transfer completes or is cancelled | "XFR" indicator disappears | |

---

## M9.6 — Preferences, Kermit tab: layout

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 6.1 | Open Preferences, pick the Kermit tab | Two groups: "File Transfer" and "Kermit Settings" | |
| 6.2 | Channel settings | Parity dropdown (None/Even/Odd/Mark/Space) and "Force 8-bit quoting" checkbox | |
| 6.3 | Timing settings | Send delay (0-30 s), Timeout (1-60 s), Max retries (0-99) spinners | |
| 6.4 | File handling | "If file exists" dropdown: Rename/Overwrite/Skip | |
| 6.5 | Advanced | Block check dropdown (1 / 2 / 3 (CRC)), Max packet size (20-94) spinner | |

---

## M9.7 — Preferences, Kermit tab: defaults

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 7.1 | Open the tab on a fresh preferences file | Parity = None | |
| 7.2 | | Force 8-bit quoting = unchecked | |
| 7.3 | | Send delay = 0 | |
| 7.4 | | Timeout = 8 | |
| 7.5 | | Max retries = 10 | |
| 7.6 | | If file exists = Rename | |
| 7.7 | | Block check = 1 | |
| 7.8 | | Max packet size = 80 | |

---

## M9.8 — Preferences, Kermit tab: persistence

The values are written to the preferences file as `kermit-*` lines the moment they change, so they
survive a restart, not just the session.

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 8.1 | Change settings, close Preferences, reopen | All changed values are preserved | |
| 8.2 | Change settings, close the app, start it again, open Preferences | Values still preserved | |
| 8.3 | Change Block check to 3, then Send a file to a host that supports CRC | The transfer negotiates CRC — the new value was used, no dialog in between | |

---

## M9.9 — Send and Receive start straight away

There is no settings dialog before a transfer. Send opens the file picker at once, Receive opens the
folder picker at once, and both use whatever the Kermit tab holds at that moment.

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 9.1 | Click Send File(s)... while connected | The file picker opens directly | |
| 9.2 | Click Receive File(s)... while connected | The folder picker opens directly | |

---

## M9.10 — Send File(s): file picker

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 10.1 | File picker opens | Title says "Select Files to Send", multi-select is enabled | |
| 10.2 | Cancel the file picker without selecting | No progress window opens, no error, terminal resumes normally | |
| 10.3 | Select one file and confirm | Progress window opens, transfer begins | |
| 10.4 | Select multiple files and confirm | Progress window opens with batch transfer | |

---

## M9.11 — Receive File(s): folder picker

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 11.1 | Folder picker opens | Title says "Select Save Directory" | |
| 11.2 | Cancel the folder picker without selecting | No progress window opens, no error | |
| 11.3 | Select a folder and confirm | Progress window opens, receiver enters waiting state | |

---

## M9.12 — Progress window: layout and initial state

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

## M9.13 — Progress window: during transfer

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

## M9.14 — Progress window: completion states

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

## M9.15 — Cancel transfer

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 15.1 | Click Cancel button in progress window during transfer | Transfer aborts, state changes to Cancelled | |
| 15.2 | Click Transfer > Cancel Transfer menu item during transfer | Same as 15.1 | |
| 15.3 | Close progress window (X button) during active transfer | Transfer is cancelled automatically | |
| 15.4 | After cancellation | Terminal resumes normal operation (incoming data goes to emulator) | |

---

## M9.16 — Transfer mode: data routing

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 16.1 | During active transfer, host sends terminal data | Data is NOT displayed on terminal (routed to Kermit handler) | |
| 16.2 | After transfer completes | Terminal displays incoming data normally again | |
| 16.3 | After transfer is cancelled | Terminal displays incoming data normally again | |

---

## M9.17 — Tab switching during transfer

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 17.1 | Start transfer on Tab 1, switch to Tab 2 | Tab 2 operates normally, transfer continues on Tab 1 | |
| 17.2 | Switch back to Tab 1 during transfer | XFR indicator reflects Tab 1's transfer state | |
| 17.3 | Transfer menu reflects active tab | Send/Receive disabled on Tab 1 (transferring), enabled on Tab 2 (connected, not transferring) | |

---

## M9.18 — 8-bit quoting: channel settings

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 18.1 | Settings: Parity=None, Force 8-bit quoting=OFF. Send ASCII text file | Transfer succeeds, file matches original | |
| 18.2 | Settings: Parity=None, Force 8-bit quoting=OFF. Send binary file (all 256 bytes) | Transfer succeeds on 8-bit clean link, file matches | |
| 18.3 | Settings: Parity=None, Force 8-bit quoting=ON. Send binary file | Transfer succeeds, 8-bit quoting negotiated (QBIN='&') | |
| 18.4 | Settings: Parity=Even, Force 8-bit quoting=OFF. Send binary file | 8-bit quoting auto-enabled due to parity, binary file transfers correctly | |
| 18.5 | Remote is nd-kermit with SET USE-8-BIT-QUOTE. Our side: Force 8-bit quoting=ON | Both advertise '&', negotiation succeeds, binary data survives 7-bit path | |
| 18.6 | Remote does NOT support 8-bit quoting (sends N/space in QBIN). Our side: Force=ON | Negotiation falls back to no quoting. ASCII transfers work, binary high bytes lost | |

---

## M9.19 — Sender delay

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 19.1 | Settings: Send delay=0. Start Send | Transfer begins immediately, no visible pause | |
| 19.2 | Settings: Send delay=5. Start Send | Progress window shows "Initializing", ~5 second pause before first packet | |
| 19.3 | Settings: Send delay=5. Cancel during delay | Transfer cancels cleanly, no packet sent | |

---

## M9.20 — File collision (receive)

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 20.1 | Receive file "test.txt" when it does NOT exist | File created normally | |
| 20.2 | Settings: If file exists=Rename. Receive "test.txt" when it already exists | New file saved as "test_1.txt" (or _2, _3, etc.) | |
| 20.3 | Settings: If file exists=Overwrite. Receive "test.txt" when it already exists | Existing file overwritten with new content | |
| 20.4 | Settings: If file exists=Skip. Receive "test.txt" when it already exists | File skipped, transfer continues to next file (or completes) | |
| 20.5 | Receive file with remote path separators (e.g., "PACK-1:MYFILE.DAT") | Path stripped, saved as "MYFILE.DAT" in chosen directory | |
| 20.6 | Receive file with invalid characters in name | Invalid chars replaced with underscore | |

---

## M9.21 — Timeout and retries

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 21.1 | Settings: Timeout=8, Max retries=10. Normal transfer | Transfer completes without retries | |
| 21.2 | Remote stops responding mid-transfer | After timeout × max retries, transfer fails with "Too many retries" | |
| 21.3 | Settings: Max retries=0 (unlimited). Remote temporarily unresponsive | Transfer retries indefinitely until remote recovers or user cancels | |
| 21.4 | Timeout value sent in Send-Init TIME field | Verify with protocol trace: TIME field matches configured timeout | |

---

## M9.22 — Block check type

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 22.1 | Settings: Block check=1. Transfer file | Type 1 (6-bit) checksum used, transfer succeeds | |
| 22.2 | Settings: Block check=3 (CRC). Remote supports CRC | CRC-16 negotiated, transfer succeeds | |
| 22.3 | Settings: Block check=3 (CRC). Remote only supports type 1 | Graceful fallback to type 1, transfer succeeds | |
| 22.4 | Introduce bit errors on link with type 1 | Errors occasionally missed (6-bit checksum is weak) | |
| 22.5 | Introduce bit errors on link with type 3 (CRC) | Errors reliably detected, packets retransmitted | |

---

## M9.23 — Packet size

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 23.1 | Settings: Max packet size=80 (default). Transfer 5MB file | Transfer works, ~77 data bytes per packet | |
| 23.2 | Settings: Max packet size=94 (maximum short). Transfer 5MB file | Transfer works, slightly fewer packets than 23.1 | |
| 23.3 | Settings: Max packet size=20 (minimum). Transfer file | Transfer works but slowly (many small packets) | |
| 23.4 | Remote advertises smaller max than our setting | Engine uses the smaller of the two (negotiated down) | |

---

## M9.24 — End to end: RetroTerm and nd-kermit

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 24.1 | RetroTerm sends ASCII text file to nd-kermit RECEIVE | File arrives intact, content matches | |
| 24.2 | nd-kermit SEND file, RetroTerm receives | File arrives intact in chosen directory | |
| 24.3 | Binary file transfer with Force 8-bit quoting=ON, nd-kermit SET USE-8-BIT-QUOTE | Binary file transfers correctly over 7-bit path | |
| 24.4 | SINTRAN filename with colons (e.g., "PACK-1:MYFILE.DAT") | Path stripped on receive, saved as "MYFILE.DAT" | |
| 24.5 | Large file (>64KB) | Multi-packet transfer completes, all data intact | |
| 24.6 | Transfer in both directions sequentially | First transfer completes, second starts cleanly | |

---

## M9.25 — End to end: RetroTerm and C-Kermit / G-Kermit

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 25.1 | Send text file to C-Kermit | Transfer succeeds | |
| 25.2 | Receive file from C-Kermit | Transfer succeeds | |
| 25.3 | C-Kermit with SET BLOCK-CHECK 3. Our side: Block check=3 | CRC negotiated, transfer succeeds | |
| 25.4 | C-Kermit with SET PARITY EVEN. Our side: Parity=Even | Parity applied, 8-bit quoting auto-enabled, binary file OK | |

---

## M9.26 — Edge cases

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

## M9.27 — Byte formatting in progress window

| # | Test | Expected | Pass? |
|---|------|----------|-------|
| 27.1 | < 1 KB transferred | Shows bytes (e.g., "512 B") | |
| 27.2 | 1 KB – 1 MB | Shows KB (e.g., "125.4 KB") | |
| 27.3 | 1 MB – 1 GB | Shows MB (e.g., "3.2 MB") | |
| 27.4 | > 1 GB | Shows GB (e.g., "1.50 GB") | |

---

## Notes

- For nd-kermit interop testing: the ND-100 serial line uses 7-bit even parity at the hardware level. When connecting through a TCP-to-serial gateway, set `Force 8-bit quoting = ON` on our side (not `Parity = Even`, because the gateway handles parity). On the nd-kermit side: `SET USE-8-BIT-QUOTE`.
- File collision only affects receive operations. The setting is visible but irrelevant when sending.
