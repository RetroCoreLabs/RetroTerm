# IND$FILE file transfer - specification for the RetroTerm 3270 emulation

**What this is:** the rules RetroTerm's coming IBM 3270 emulation must follow to move files to
and from a mainframe with the host program IND$FILE. It is written from the IBM documents and
the TK5 test results listed below. Two other emulators were read to see how they read the IBM
text. Their code was NOT copied, and where they agree with each other that is still not proof
(see "Sources").

**Status:** specification only. Nothing here has been built or tested in RetroTerm.

Words used here:

- **Host** - the mainframe (MVS/TSO, VM/CMS or CICS). **PC** - the terminal side, that is RetroTerm.
- **HFTP** - "Host File Transfer Program", IBM's name for IND$FILE in the datastream document.
- **Upload** - PC to host (the host command is `PUT`). **Download** - host to PC (`GET`).
- **Outbound** - host to PC. **Inbound** - PC to host. (3270 terms.)
- **SF** - structured field: a block that starts with a 2-byte length and an ID.
- **WSF** - the Write Structured Field command the host uses to send SFs.
- **AID** - the first byte of every inbound message: which key was pressed. `X'88'` means
  "structured fields follow".
- **DFT** and **CUT** - the two ways of carrying the file. DFT sends it in structured fields;
  CUT paints it onto the screen buffer. Section 1.

Citations: **[IND]** = IBM SS-HCS12-1372-00 "IND$FILE Datastream Description" (1995), page
numbers as printed in the document (it runs from page 11 to page 36). **[DS]** = IBM
GA23-0059-4 "3270 Data Stream Programmer's Reference" (Dec 1988). **[FTTERM]** = IBM "PC/HOST
File Transfer and Terminal Emulator Program User's Guide and Reference" (Sep 1986). **[x3270]**
= x3270 source, file named. **[tnz]** = IBM tnz source, file named. **[TK5]** = the Hercules
TK5 guide chapter "08 - Moving files between the PC and MVS" and its test results (kept outside
this repository, beside the Hercules install).

---

## 1. The two transfer modes, and which one TK5 uses

### 1.1 DFT mode (structured fields)

The file travels inside structured fields with ID `X'D0'`, a subset of IBM's Distributed Data
Management (DDM) architecture [IND p.23, p.26 section 6.3.2]. No checksum, no screen encoding;
the only change to the data is ASCII/EBCDIC translation, and **that is done on the host** when
the `ASCII` option is given [IND p.23, Figure 26 and the note under it]. Only errors that end
the transfer exist in DFT, because there is no checksum to fail [IND p.25 section 6.3.1.2].

### 1.2 CUT mode (screen frames)

The host paints a "frame" with ordinary 3270 orders onto a 1920-position (model 2) screen.
Binary data is turned into displayable characters first, the PC reads it out of the screen
buffer, and answers with AID keys (Enter, PF1, PF2...) [IND p.11 section 6.2.1, p.15 section
6.2.3]. Each frame carries a sequence number and a checksum [IND p.14 "Data errors"].
"Asynchronous" mode is CUT with a 32-bit CRC instead of the 6-bit checksum and `X'F0'` instead
of `X'7C'` as the last field attribute [IND p.23 section 6.2.6].

### 1.3 How the host picks the mode

The host sends a Read Partition Query and looks at the Query Reply. A reply with code `X'95'`
(DDM, IBM calls it the "TCA attachment") or `X'93'` (PC3270 attachment) means DFT; with
neither, the host uses CUT or Asynchronous [IND p.22 section 6.2.4.11]. In the host command,
the verbs `GET`/`PUT` versus `GTP`/`PTP` tell Asynchronous and CUT apart [same place]. Which
verb means which is not stated there: NOT VERIFIED.

So the mode is the **host's** choice, made from what the terminal said in its Query Reply. The
terminal does not ask for a mode.

### 1.4 TK5 uses DFT - measured

The TK5 test (ws3270 `Transfer()` action, user HERC03, run 22 Nov 2026) asserts that both the
upload and the download report `Transfer complete, N bytes transferred ... in DFT mode`, and
both results are recorded as **PASS** [TK5 guide section 1; TK5 test file
`08-moving-files-between-pc-and-mvs.Tests.ps1` lines 50-61; results `01-...` and `03-...`].
x3270 prints `CUT` or `DFT` in that message from a flag that is only set to CUT when CUT frames
arrive [x3270 `Common/ft.c` around lines 554-586]. ws3270 sends the DDM Query Reply [x3270
`Common/sf.c` `do_qr_ddm`], so TK5's IND$FILE saw DDM and chose DFT.

Facts about the TK5 program itself [TK5 guide section 1]:

- `SYS2.CMDLIB` holds `IND$F205` with the alias `IND$FILE` (found live).
- The transfer must be started from the plain TSO `READY` prompt, not from ISPF. In ISPF it
  "does nothing / times out".
- An upload with no record options made a data set `RECFM VB LRECL 255`.

**What RetroTerm must do first:** DFT. CUT is only needed for a host that does not answer DDM,
and TK5 does. Whether TK5's IND$FILE can still run CUT when the Query Reply has no DDM was not
tested: NOT VERIFIED.

---

## 2. The command the user (or RetroTerm) types

The transfer starts when the user types the IND$FILE command at the host's command line and
presses Enter. Everything after that is the protocol. The IBM datastream document only names
the command as `IND$FILE PUT (PTP) fn ft fm` and `IND$FILE GET ...` [IND p.13 Figures 5-6,
p.24-25 Figures 27-28]; the option lists come from [FTTERM], from what the oracles type, and
from TK5.

### 2.1 MVS/TSO

```
IND$FILE PUT data.set.name ASCII CRLF [APPEND] [RECFM(F|V|U)] [LRECL(n)] [BLKSIZE(n)]
         [SPACE(n1[,n2]) TRACKS|CYLINDERS|AVBLOCK(n)]
IND$FILE GET data.set.name [ASCII] [CRLF] [APPEND]
```

- Options follow the name with **no** opening bracket on TSO [x3270 `Common/ft.c` lines 685-688:
  the `(` is only added for non-TSO hosts]. tnz's own example is the same:
  `IND$FILE PUT CLEANUP.PROC ASCII CRLF LRECL(80) RECFM(F)` [tnz `docs/transfer.md`].
- A name without quotes gets the user's prefix: `HELLO.TEXT` became `HERC01.HELLO.TEXT` [TK5
  section 1].
- SEND (PUT) options for MVS/TSO: `APPEND`, `ASCII`, `BLKSIZE(n)`, `CRLF`, `LRECL(n)`,
  `RECFM(F|V|U)`, `SPACE(n1,n2) AVBLOCK(n)`, `SPACE(n1,n2) CYLINDERS`, `SPACE(n1,n2) TRACKS`.
  Binary is the default [FTTERM p.4-10 Figure 4-3].
- RECEIVE (GET) options for MVS/TSO: `APPEND`, `ASCII`, `CRLF` [FTTERM p.4-25 Figure 4-6].
- For PDS members only `ASCII` and `CRLF` are valid [FTTERM p.4-10 Figure 4-3].
- `BLKSIZE(n)` - block size of a new data set; default equals the record length [FTTERM "Option Definitions", BLKSIZE].
- `LRECL(n)` - "n can be from 1 to 132" for MVS/TSO [FTTERM "Option Definitions", LRECL]. **This conflicts with
  TK5**, which made `LRECL 255` by default [TK5 section 1]. TK5's IND$F205 is evidently not
  limited the way the 1986 manual says. See Open questions.
- `RECFM(F)` "is the default record format for MVS/TSO" [FTTERM "Option Definitions", RECFM]. **Also conflicts with
  TK5** (default was VB). See Open questions.
- `SPACE(n1,n2) units` with `TRACKS`, `CYLINDERS` or `AVBLOCK(value)`; without SPACE "TSO
  allocates one block" [FTTERM "Option Definitions", SPACE].
- x3270 writes only one of `TRACKS` / `CYLINDERS` / `AVBLOCK(n)` after `SPACE(...)` and only for
  PUT [x3270 `Common/ft.c` lines 728-746]. The host message TRANS33 confirms only one is
  allowed [IND p.36].

### 2.2 VM/CMS

```
IND$FILE PUT fn ft fm (ASCII CRLF [APPEND] [RECFM F|V] [LRECL n]
IND$FILE GET fn ft fm (ASCII CRLF [APPEND]
```

- Options after an opening `(`, no closing bracket needed [x3270 `Common/ft.c` lines 685-688;
  FTTERM examples such as `SEND B:SALES.DOC SALES SCRIPT A1 (ASCII CRLF APPEND`, chapter 4 SEND examples].
- `RECFM F` / `RECFM V`, space separated, no brackets; `LRECL n`, n from 1 to 256; VM default is
  `RECFM V` [FTTERM "Option Definitions", LRECL and RECFM; x3270 `Common/ft.c` lines 748-766].

### 2.3 CICS

```
IND$FILE PUT name (ASCII|BINARY CRLF|NOCRLF
IND$FILE GET name (ASCII|BINARY CRLF|NOCRLF
```

- On CICS, ASCII and CRLF are the defaults, and one implies the other unless overridden;
  BINARY implies NOCRLF [FTTERM "Option Definitions", ASCII, BINARY, CRLF]. APPEND is not a SEND
  option on CICS [FTTERM "Option Definitions", APPEND].
- x3270 always writes `BINARY` or `ASCII` and `CRLF` or `NOCRLF` explicitly for CICS [x3270
  `Common/ft.c` lines 689-697].

### 2.4 What ASCII and CRLF mean

- `ASCII`: the data is text; translate ASCII to EBCDIC on PUT, EBCDIC to ASCII on GET [FTTERM
  "Option Definitions", ASCII]. In DFT the host does it [IND p.23].
- `CRLF` on PUT: CR LF ends a host record and is not written to the host file. On GET: CR LF is
  added at the end of each record, and trailing blanks are removed [FTTERM "Option Definitions", CRLF].
- Without `ASCII`, data is binary and passes unchanged [FTTERM "Option Definitions", BINARY].

### 2.5 Typing the command

x3270 clears the input field and types the whole command, then Enter. If the input field is
too short for the command, or the keyboard is locked, or the screen has no input field, it
refuses to start [x3270 `Common/ft.c` lines 683-800]. RetroTerm needs the same check: a cut-off
command sent to TSO would run with the wrong meaning.

---

## 3. DFT mode in detail

### 3.1 Framing on the wire

- **Outbound** (host to PC): the SFs below arrive inside a Write Structured Field command, next
  to any other SFs. x3270 hands every SF whose ID byte is `X'D0'` to its transfer code [x3270
  `Common/sf.c` around line 175; ID `SF_TRANSFER_DATA 0xd0` in `include/3270ds.h` line 133].
- **Inbound** (PC to host): every reply is one message of AID `X'88'` followed by one SF
  [x3270 `Common/ft_dft.c` every reply starts `AID_SF`; tnz `tnz/tnz.py` `rec = b"\x88"`].
- In every SF the first two bytes are the length **including** those two bytes, big-endian.
- The `D0` SF is not in the structured field tables of [DS] chapter 5 that were read: NOT
  VERIFIED in [DS]. The source for the formats below is [IND].

The byte tables below give offsets from the first byte of the SF (the length), not counting
the AID or the WSF command.

### 3.2 Request codes at a glance

From [IND] Figures 31-43 (p.27-32), and the same values in [x3270 `include/ft_dft_ds.h` lines
36-59]:

| Bytes 2-4 | Direction | Meaning |
|---|---|---|
| `D0 00 12` | host -> PC | Open request |
| `D0 00 09` | PC -> host | Open acknowledgement |
| `D0 41 12` | host -> PC | Close request |
| `D0 41 09` | PC -> host | Close acknowledgement |
| `D0 45 11` | host -> PC | Set Cursor request (upload) |
| `D0 46 11` | host -> PC | Get request (upload) |
| `D0 46 05` | PC -> host | Data for Get (upload data) |
| `D0 47 11` | host -> PC | Insert request (download) |
| `D0 47 04` | host -> PC | Data to Insert (download data) |
| `D0 47 05` | PC -> host | Insert normal reply (data acknowledgement) |
| `D0 tt 08` | PC -> host | Error reply; `tt` = the request it answers |

Inner headers: `63 06` record number header, `69 04` error code header, `C0 80` data not
compressed, `61` begin data [IND Figures 38-40, 43; x3270 `include/ft_dft_ds.h` lines 52-55].

### 3.3 Open request (host -> PC)

Four forms, all based on the DDM Open [IND p.27 note under Figure 31]. Short form, length
`X'0023'` = 35 bytes [IND p.27 Figure 31, p.28 Figure 33, p.29 Figure 35]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 23` | SF length |
| 2-4 | `D0 00 12` | Open request |
| 5-10 | `01 06 01 01 04 03` | "Fixed parameter, changes file, etc." |
| 11-20 | see below | "Functions required" |
| 21-25 | `50 05 52 03 F0` | Data not compressed |
| 26-27 | `03 09` | File name header with length |
| 28-34 | 7 characters | File name |

Bytes 11-20, "Functions required":

- Download (host will Insert): `0A 0A 00 00 00 00 11 01 01 00` ("Sequential Insert") [IND p.27
  Figure 31].
- Upload (host will Get): `0A 0A 00 01 00 00 00 00 01 00` ("Sequential Get") [IND p.28 Figure 33].
- Message: same as download [IND p.29 Figure 35].

So the only byte that tells upload from download is offset 14: `01` = upload, `00` = download.
tnz reads exactly that byte [tnz `tnz/tnz.py` `_process_ddm_0xd00012`, `b_str[start+14] == 1`].

Long form "with record size", length `X'0029'` = 41 bytes. Used when the DDM Query Reply's limit
in that direction is greater than 2048, or the user gave a size option over 2048 [IND p.26
section 6.3.2.2, p.28 section 6.3.2.4]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-25 | as above, but length `00 29` | |
| 26-29 | `08 06 27 04` | Record size header with length |
| 30-31 | `ll ll` | Size, "(LIMIN - 17)" |
| 32-33 | `03 09` | File name header with length |
| 34-40 | 7 characters | File name |

[IND p.28 Figures 32 and 34]. The figures write "LIMIN - 17" for both directions; whether the
download form uses LIMOUT instead is not stated: NOT VERIFIED.

**The file name** is `FT:DATA` for file data. For a message, IBM prints `'FT:     '` - "FT:"
then blanks [IND p.29 section 6.3.2.5 and Figure 35]. x3270 instead treats the name `FT:MSG` as
the message open [x3270 `Common/ft_dft.c` line 53 and line 171]; tnz treats any name other
than `FT:DATA` as a message [tnz `tnz/tnz.py` `self.__ddmdata = (ft_str == "FT:DATA")`]. RetroTerm
should follow tnz's rule (anything not `FT:DATA` is a message), which covers both. What TK5
actually sends was not captured: NOT VERIFIED.

How the PC tells the forms apart: by the SF length, `0x23` or `0x29`; anything else is refused
[x3270 `Common/ft_dft.c` lines 146-157]. tnz instead looks at offset 26 (`03` = no size, `08` =
size) [tnz `tnz/tnz.py` `_process_ddm_0xd00012`].

In abort cases the host sends the message open without closing the data file first [IND p.29
section 6.3.2.5].

### 3.4 Open acknowledgement (PC -> host)

`88` then `00 05 D0 00 09` [IND p.29 Figure 36; x3270 `Common/ft_dft.c` lines 183-189]. After
it, record numbering starts at 1 [x3270 `Common/ft_dft.c` line 178].

### 3.5 Download: Insert request and data (host -> PC)

One outbound message carries an Insert request and a data SF [IND p.30 Figure 39]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 0A` | SF length |
| 2-4 | `D0 47 11` | Insert request |
| 5-9 | `01 05 00 80 00` | Feedback requested |

then:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `ll ll` | SF length |
| 2-4 | `D0 47 04` | Data to insert |
| 5-6 | `C0 80` | Data not compressed |
| 7 | `61` | Begin data |
| 8-9 | `dd dd` | Data length **plus 5** |
| 10... | data | |

Note there is no record number in the host's data SF. x3270 does nothing on the Insert request
itself and reads the data length from bytes 8-9 minus 5 [x3270 `Common/ft_dft.c` lines 192-198,
lines 231-237].

**Message SFs** have exactly this format; they count as messages because they follow an Open
for messages [IND p.30 section 6.3.2.10].

### 3.6 Data acknowledgement (PC -> host)

`88` then [IND p.30 Figure 40]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 0B` | SF length |
| 2-4 | `D0 47 05` | Insert normal reply |
| 5-6 | `63 06` | Record number header |
| 7-10 | `nn nn nn nn` | Record number, big-endian |

x3270 sends the current number and then adds one; it starts at 1 after each Open [x3270
`Common/ft_dft.c` lines 201-216, line 178]. tnz adds one **before** sending [tnz `tnz/tnz.py`
just before `D04705`]. Both send 1 for the first data SF only if their starting values are as
read; tnz's starting value was not checked: NOT VERIFIED. [IND] does not say what number the
first acknowledgement carries: NOT VERIFIED.

A message SF is acknowledged the same way [IND p.24-25 Figures 27-28, last two flows].

### 3.7 Upload: Set Cursor and Get (host -> PC)

One outbound message [IND p.29 Figure 37]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 0F` | SF length |
| 2-4 | `D0 45 11` | Set Cursor request |
| 5-9 | `01 05 00 06 00` | Fixed parameter "Rel-Pos." |
| 10-14 | `09 05 01 03 00` | Rel-Pos. parameter "Next" |

then:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 09` | SF length |
| 2-4 | `D0 46 11` | Get request |
| 5-8 | `01 04 00 80` | Feedback requested |

x3270 does nothing on Set Cursor and sends one buffer of file data on Get [x3270
`Common/ft_dft.c` lines 413-419, lines 565-669].

### 3.8 Upload data buffer (PC -> host)

`88` then [IND p.30 Figure 38]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `ll ll` | SF length, including the length field |
| 2-4 | `D0 46 05` | Data for Get |
| 5-6 | `63 06` | Record number header |
| 7-10 | `nn nn nn nn` | Record number |
| 11-12 | `C0 80` | Data not compressed |
| 13 | `61` | Begin data |
| 14-15 | `dd dd` | Data length **plus 5** |
| 16... | data | |

The header is 16 bytes. x3270 fills at most (buffer size - 27) data bytes per SF; its comment
says it reads "5 bytes less than we're allowed" [x3270 `Common/ft_dft.c` lines 582-585]. tnz
fills at most LIMIN - 12 - 2 - 2 bytes [tnz `tnz/tnz.py` `maxlen = self._limin - len(isf) - 2 - 2`].
How close to LIMIN the host really accepts is not stated in [IND]: NOT VERIFIED. RetroTerm
should keep the whole inbound message (AID + SF) at or under LIMIN, since [DS] p.6-37 says the
inbound DDM bytes "must be equal to or less than the LIMIN value".

**Resend on Read Modified.** x3270 keeps a copy of the last upload SF and sends it again if the
host issues a Read Modified while upload data is pending [x3270 `Common/ft_dft.c` lines 657-664
and 713-725; called from `Common/ctlr.c` lines 881 and 1107]. [IND] does not describe this.

**TK5 needs it - measured 24 November 2026** (`Hercules\Tk5TransferBufferSizeTests` with s3270,
`Hercules\HostStreamRecorderTests` cases `file-transfer-large` and `file-transfer-binary` with
RetroTerm, all through `Tk5WireTap`). After every FULL upload block TK5 sends a Read Modified
command, the one byte `F6`, and then expects the same block a second time; only after the second
copy does the next Set Cursor + Get come. Seen after inbound records of 16374, 16384, 32629 and
32757 bytes; never after the last, short block of a file (940, 1821, 1900, 2205 and 3650 bytes) or
the 49-byte file of `file-transfer`. Where between 3650 and 16374 bytes it starts was not measured.

```
host   F3 ... 000F D04511 ... 0009 D04611 01040080      Set Cursor + Get
client 88 3FFF D04605 6306 00000001 C080 61 3FF4 ...    block 1, 16384-byte record
host   F6                                               Read Modified
client 88 3FFF D04605 6306 00000001 C080 61 3FF4 ...    block 1 again, the same bytes
host   F3 ... 000F D04511 ... 0009 D04611 01040080      the next Get
```

A terminal that answers that `F6` with an ordinary Read Modified loses the block **with no error on
either side**: RetroTerm did, before the fix, on a 20,000-byte binary file. It answered with an ordinary
Read Modified (that record was not decoded), the host took the 3,633 data bytes of block 2 as the whole file, said
`TRANS03 File transfer complete`, and the GET gave back those 3,633 bytes. This is the retry of [DS]
p. 3-14 to 3-18 (a Read Modified command in a retry state repeats the last inbound operation; any
outbound transmission other than a read command acknowledges it, p. 3-23), applied to the upload
data reply, which [DS] does not name as a retry case. RetroIO keeps the upload block for exactly
that (`Display3270.SendStructuredFieldReply(field, retry: true)`, used by `IndFileTransfer` for Data
for Get), and the RetroIO tests `AReadModifiedAfterUploadDataSendsTheSameBlockAgain` and
`AReadModifiedAfterTheAcknowledgmentIsAnOrdinaryOne` hold it.

**Block sizes measured on TK5, 24 November 2026** (100,025-byte text file, PUT then GET, ASCII CRLF):

| LIMIN = LIMOUT | Client | Upload field (record) | Host download field (record) | File back intact |
|---|---|---|---|---|
| 16384 | RetroTerm | 16383 (16384), 16367 data | 16367 (16384) | yes (50,000-byte file) |
| 16384 | s3270 | 16373 (16374), 16357 data | 16367 (16384) | yes |
| 32639 | s3270 | 32628 (32629), 32612 data | 32622 (32639) | yes |
| 32767 | s3270 | 32756 (32757), 32740 data | 32683 (32700) | **NO** |

So the host fills each download WSF to exactly LIMOUT, the `F3`, a 6-byte field before the D0
fields and the Insert request included, except at 32767, where it stopped at a 32700-byte record.
x3270 puts (buffer size - 27) data bytes in an upload block, RetroTerm LIMIN - 17; both keep the
structured field bytes after the AID at or under LIMIN, which is what [DS] p. 6-37 asks.

### 3.9 End of upload: "Get past end of file" (PC -> host)

When the PC has no more data and the host sends another Get, the PC answers with an error
reply [IND p.24 Figure 27 "Get Past End of File Error"; p.32 Figure 43]:

`88` then `00 09 D0 46 08 69 04 22 00`

(length 9, `D0 46 08` = error reply to Get, `69 04` = error code header, `22 00` = Get past end
of file) [x3270 `Common/ft_dft.c` lines 643-650 build exactly this]. The host then sends Close.

### 3.10 Close request and acknowledgement

Host -> PC: `00 05 D0 41 12` [IND p.31 Figure 41]. PC -> host: `88` then `00 05 D0 41 09`
[IND p.31 Figure 42; x3270 `Common/ft_dft.c` lines 671-688].

### 3.11 Error reply (PC -> host)

`88` then [IND p.32 "Error Response" and Figure 43]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 09` | SF length |
| 2-4 | `D0 tt 08` | Error reply to request `tt` |
| 5-6 | `69 04` | Error code header |
| 7-8 | `nn nn` | Error code |

| `tt` | Request | Allowed error codes |
|---|---|---|
| `00` | Open | `0100` Open failed exception, `0200` Arrival sequence not allowed, `6000` Command syntax error |
| `47` | Insert | `0200`, `3E00` Operation not authorized, `6000` |
| `45` | Set Cursor | `0200`, `6000` |
| `46` | Get | `0200`, `2200` Get past end of file, `6000` |
| `41` | Close | `6000` |

x3270 uses `0100` for every abort it starts itself (user cancel, write error, read error), with
`tt` = the request it was answering [x3270 `Common/ft_dft.c` lines 690-711]. That is outside the
table above for Insert and Get; whether IND$FILE minds was not tested: NOT VERIFIED. RetroTerm
should pick codes from the table: `3E00` for a local write failure on download, and on upload
either `6000` or the x3270 behaviour after testing.

### 3.12 Whole sequences

**Download** [IND p.25 Figure 28]:

1. PC types `IND$FILE GET ...` + Enter.
2. Host: Read Partition Query. PC: Query Reply including DDM (section 5).
3. Host: Open (`FT:DATA`, download functions). PC: Open ack.
4. Repeat: host Insert + Data to Insert; PC: Data ack.
5. Host: Close. PC: Close ack.
6. Host: Open for messages. PC: Open ack.
7. Host: message `TRANS03 File transfer complete` (as a Data to Insert SF). PC: Data ack.

**Upload** [IND p.24 Figure 27]:

1. PC types `IND$FILE PUT ...` + Enter.
2. Read Partition Query / Query Reply.
3. Host: Open (`FT:DATA`, upload functions). PC: Open ack.
4. Repeat: host Set Cursor + Get; PC: upload data buffer.
5. Host Get once more; PC: Get-past-end-of-file error.
6. Host: Close. PC: Close ack.
7. Host: Open for messages; PC: Open ack; host message; PC: Data ack.

The Query happens on every transfer in IBM's figures. Whether TK5's IND$FILE queries every time
or uses an earlier answer was not captured: NOT VERIFIED.

### 3.13 Errors and how a DFT transfer ends

- **Every DFT transfer, good or bad, ends with a host message** in a message file: Open for
  messages, one Data to Insert, Data ack [IND p.24-26 Figures 27-30].
- **PC finds an error:** PC sends an error reply; host answers with Open for messages and a
  `TRANSxx` message [IND p.25 Figure 29].
- **Host finds an error:** host sends Open for messages and a `TRANSxx` message, possibly
  without closing the data file first [IND p.26 Figure 30, p.29 section 6.3.2.5].
- The transfer is complete and successful when the message starts with `TRANS03`; otherwise the
  message text is the error to show [x3270 `Common/ft_dft.c` lines 267-278; tnz `tnz/tnz.py`
  `get_file`/`put_file` raise unless the message starts with `TRANS03`]. `TRANS04` (complete,
  records cut) is also a successful end [IND p.36]. Both oracles only test for `TRANS03`, so
  TRANS04 falls into their "other message" path [same lines]; how x3270 shows that path was
  not read. RetroTerm should report it as success with a warning.
- x3270 cuts the message text at the first `$` [x3270 `Common/ft_dft.c` lines 259-265]. TK5 ends
  the text with `$` and fills the 85-byte message with blanks (measured 23 November 2026, Open
  question 7), so the `$` is the end of the text.
- Message text in DFT: both oracles read it as ASCII bytes [x3270 compares to the C string
  `"TRANS03"`; tnz decodes message data as ISO 8859-1]. [IND] says nothing about the message
  code page in DFT (for CUT it says EBCDIC, p.17 note 1): NOT VERIFIED in IBM text.
- A message open for a transfer the user never started: tnz answers the Open with an error
  `D0 00 08 69 04 01 00` (open failed) [tnz `tnz/tnz.py` `_process_ddm_0xd00012`]. x3270 ignores
  D0 SFs when no transfer is running [x3270 `Common/ft_dft.c` lines 98-101].

---

## 4. Buffer sizes

- [IND] p.24 section 6.3.1: DFT "supports buffer sizes ranging from 2K to 32K", meaning the
  amount of data in one data SF, before an application acknowledgement.
- The Open uses the long form (record size field) when the Query Reply limit or the user's size
  option is over 2048 [IND p.26, p.28].
- The terminal states its limits in the DDM Query Reply: LIMIN (inbound) and LIMOUT (outbound).
  `X'0000'` means "no limit" [DS p.6-37]. tnz's comment says that with `0000` "no data comes
  across", so 0 must not be sent [tnz `tnz/tnz.py` lines 152-161].
- x3270: default 16384, smallest 256, largest 32767, same value for LIMIN and LIMOUT [x3270
  `include/globals.h` lines 419-423; `Common/ft_dft.c` lines 727-749; `Common/sf.c`
  `do_qr_ddm`].
- tnz: LIMIN 32639, LIMOUT 32767, with the note that "TSO IND$FILE may not handle 32767. It
  seems like 32639 works" (seen under VICOM, not TK5) [tnz `tnz/tnz.py` lines 152-163].
- The TK5 test did not set a buffer size, so ws3270 used its default. That default is 16384 by
  the x3270 code [x3270 `Common/glue.c` line 466], but the test did not record it: NOT VERIFIED
  which size TK5 actually ran with.

Recommendation for RetroTerm: LIMIN = LIMOUT = 16384 as the first default (the oracle value TK5
most likely ran with), user-settable in Preferences from 2048 to 32639.

---

## 5. What the Query Reply must contain to get DFT

The host decides on DFT from the DDM Query Reply `X'95'` [IND p.22 section 6.2.4.11]. Its
format [DS p.6-37 "Query Reply (DDM)"]:

| Offset | Bytes | Meaning |
|---|---|---|
| 0-1 | `00 0C` | Length (12 for one subset) |
| 2 | `81` | Query Reply |
| 3 | `95` | DDM |
| 4-5 | `00 00` | Flags, reserved |
| 6-7 | LIMIN | Largest inbound DDM transmission |
| 8-9 | LIMOUT | Largest outbound DDM transmission |
| 10 | `01` | NSS, number of subsets |
| 11 | `01` | DDMSS: `X'01'` = DDM Copy Subset 1, the only valid value |

[DS p.6-37 to p.6-38]. x3270 and tnz both send exactly these 12 bytes [x3270 `Common/sf.c`
`do_qr_ddm`; tnz `tnz/tnz.py` lines 4209-4220]. [DS] also defines an optional "Direct Access ID"
self-defining parameter (`04 01 iiii`) that may follow [DS p.6-38]; neither oracle sends it.

The reply must also be listed where the host looks for it:

- In answer to Query, Query List with code `X'95'`, "Equivalent" or "All" [DS p.6-37].
- The Summary Query Reply (`X'80'`) lists the codes the terminal supports; x3270 builds its
  summary from the same table that includes DDM [x3270 `Common/sf.c` lines 77-103]. That the
  host checks the summary was not verified: NOT VERIFIED.

LIMOUT also caps outbound DDM bytes per WSF: "the sum of bytes contained in all the DDM
structured fields following a WSF command must be equal to or less than the LIMOUT value. If
this limit is exceeded, the transmission will be rejected" [DS p.6-37]. RetroTerm should log,
not crash, when a host breaks this.

---

## 6. Text translation

### 6.1 DFT

- All ASCII/EBCDIC translation is on the host, only when `ASCII` is given [IND p.23 Figure 26
  and note]. In binary mode the bytes pass unchanged both ways.
- So in ASCII mode the bytes RetroTerm receives are already "ASCII as IND$FILE sees it". That is
  IND$FILE's own fixed table, not the user's code page. x3270 keeps a copy of that fixed table
  ("ASCII to EBCDIC, as seen by IND$FILE" and back) and only uses it when the user asks to remap
  to a local code page [x3270 `Common/ft.c` lines 75-110; `Common/ft_dft.c` lines 288-370,
  471-562]. What IND$FILE's table is was not read from an IBM document: NOT VERIFIED. Without
  remapping, RetroTerm writes the bytes as they come.
- x3270 notes that IND$FILE maps EBCDIC `X'E1'` to ASCII `0x9F` and `X'FF'` to `0xFF` [x3270
  `Common/ft_dft.c` lines 354-368]. Not checked against IBM text: NOT VERIFIED.
- **Line ends with CRLF:** on download the host adds CR LF after each record [FTTERM "Option Definitions", CRLF].
  x3270 with its `Cr=remove/add` option drops every CR and every `0x1A` (end-of-file mark) when
  writing the PC file [x3270 `Common/ft_dft.c` lines 300-303]. On upload, x3270 adds a CR before
  every LF that is not already after a CR, so the host sees CR LF [x3270 `Common/ft_dft.c` lines
  502-518]. TK5: `Cr=remove` on send and `Cr=add` on receive gave correct text both ways [TK5
  section 1]. (Note: in x3270 both words switch on the same flag [x3270 `Common/ft.c` lines
  949-950]; the word only reads differently.)
- DBCS (SO/SI) handling exists in x3270 [x3270 `Common/ft_dft.c` lines 319-352, 540-549]. Not
  needed for TK5; out of scope for the first version.

### 6.2 CUT

CUT encodes every byte as displayable characters in four "quadrants": a quadrant selector
character followed by code characters, at worst doubling the size [IND p.31-32 section 6.4.1].
The full table is [IND p.32-33 Figures 44-45], with columns EBCDIC, ASCII, OTHER 1, OTHER 2,
host code and 3274 code. Selectors: `;` (EBCDIC quadrant), `=` (ASCII), `*` (OTHER 1), `'`
(OTHER 2) [IND Figure 44 header row]. RetroTerm must copy the table from the IBM figures, not
from x3270's `conv[]` table [x3270 `Common/ft_cut.c` lines 58-110], and check the two agree.
In CUT mode the ASCII translation also happens on the host [IND p.12 Figures 3-4].

---

## 7. CUT mode in detail

Needed only for a host that does not answer with DFT. Summary from [IND]; all frames are
1920-position screens.

### 7.1 Frames from the host

All start `27 F5 C2 11 5D 7F 11 40 40` = ESC (TSO only), Erase/Write, WCC, SBA to the "TCAM
signal" address, SBA to position 0 [IND p.16 Figure 11]. Then:

| Frame | Bytes after position 0 | Source |
|---|---|---|
| Command acknowledgement | `C3` control code, `ss` sequence, `81 81` status, message (blank), SBA `5D 7A` (pos 1914), SF `C1` (unprotected, MDT on), IC `13`, response area `00 00 00 00`, SF `7C` | [IND p.16 Figure 11] |
| Normal completion | as above, status `81 89`, message 80 bytes EBCDIC `TRANS03 File Transfer Complete` padded with `40` | [IND p.17 Figure 12] |
| Abort | as above, status `81 94` (file error) or `81 98` (transmission error, message is one blank) | [IND p.18 Figure 13] |
| Data request (upload) | `C2`, SF `C1`, data code `C1`, `ss`, IC, SBA `5D 7F` (pos 1919), SF `7C` | [IND p.19 Figure 14] |
| Retransmit request | as data request with `4C` instead of `C2` | [IND p.19 section 6.2.4.5] |
| Download data | `C1` data code, `ss`, `cc` checksum, `ll ll` length, data, SBA pos 1914, SF `C1`, IC, response area, SF `7C` | [IND p.20 Figure 17] |
| Download end of data | as download data, length `81 83`, data `5C A9` ("CR EOF") | [IND p.21 Figure 18] |

The PC knows a screen is a transfer frame from the `X'7C'` attribute (protected, non-display,
numeric) in the last position [IND p.15 section 6.2.3]. x3270 checks for a protected+numeric
attribute at position 1919 and then reads the frame type at position 0 [x3270
`Common/ft_cut.c` `ft_cut_data` line 392; offsets in `include/ft_cut_ds.h`].

### 7.2 Replies from the PC

| Reply | Bytes | Source |
|---|---|---|
| Positive ack | Enter (`7D`); the controller adds cursor address | [IND p.21 Figure 19] |
| Retransmit (bad data) | PF1 `F1`, cursor, SBA `5D 7B`, `4C`, `ss`, reason `rr rr` | [IND p.21 Figure 20] |
| Resync, VM | Clear `6D` | [IND p.21 Figure 21] |
| Resync, TSO | PA2 `6E` | [IND p.21 Figure 22] |
| Abort | PF2 `F2`, cursor, SBA `5D 7B`, `C3`, `ss`, reason | [IND p.21 Figure 23] |
| Upload data | Enter `7D`, cursor, SBA `40 C2`, `C1`, `ss`, `cc`, `ll ll`, data | [IND p.19 Figure 15] |
| Upload end of data | as upload data, length `81 83`, data `5C A9` | [IND p.20 Figure 16] |

"The Reason code field ... is not used in the actual implementation" [IND p.22 note under
Figure 23].

### 7.3 Sequence numbers, length and checksum

- Frame sequence is modulo 64 [IND p.19 note 1].
- Sequence, length and checksum are written as 6-bit values, each turned into one character with
  the table [IND p.36 Figure 47] (0-25 = a-z, 26-31 = `& - . , : +`, 32-57 = A-Z, 58-63 = 0-5).
  The length is a 12-bit number split into two 6-bit parts [IND p.35 section 6.4.2.1].
- CUT checksum: XOR of every byte of the data portion, low 6 bits [IND p.34 section 6.4.2],
  computed on the EBCDIC form of the encoded data [IND p.34].
- Async CRC: 32-bit, IEEE 802.5 polynomial, padded with four 1 bits to 36 bits and sent as six
  6-bit characters [IND p.34-35, Figure 46]. The polynomial's first term prints as `X**3` in the
  PDF text; it is meant to be `X**32` for a degree-32 polynomial, but that is a reading of a
  print fault: NOT VERIFIED.
- Checksum covers positions 10-1913 on download and 12-1918 on upload [IND p.34].

### 7.4 CUT error handling

- Data error (bad checksum) from host: PC replies Retransmit (PF1); host resends [IND p.14 Figure 7].
- Bad upload data at the host: host sends Retransmit Data Request (`4C`); PC resends [IND p.15 Figure 8].
- Terminating error at the PC: PC sends Abort (PF2); host sends Abort frame; PC Enter [IND p.15 Figure 9].
- Terminating error at the host: host sends Abort frame; PC Enter [IND p.15 Figure 10].
- Normal end: Normal Completion frame, PC Enter [IND p.13-14 Figures 5-6].

Figures 24-25 [IND p.22] show the frames as they sit in the PC's buffer in 3274 internal codes
(`A0` = 'A' etc.), not EBCDIC. An emulator holds EBCDIC in its buffer, so it reads the EBCDIC
values of Figures 11-18, as x3270 does with `0xC3`, `0xC2`, `0xC1` [x3270
`include/ft_cut_ds.h`].

---

## 8. Host messages

All host messages have the form `TRANSxx <text>` [IND p.36 section 6.5]:

| Code | Text (from [IND p.36-37]) |
|---|---|
| TRANS03 | File transfer complete |
| TRANS04 | File transfer complete, with records segmented (PUT only: a record longer than the host maximum was cut) |
| TRANS13 | Error writing file to host: file transfer canceled |
| TRANS14 | Error reading file from host: file transfer canceled |
| TRANS15 | Required host storage unavailable: file transfer canceled |
| TRANS16 | Incorrect request code: file transfer canceled |
| TRANS17 | Missing or incorrect TSO data set name (TSO) / CMS file identifier (VM): file transfer canceled |
| TRANS18 | Incorrect option specified: file transfer canceled |
| TRANS19 | Error reading or writing to host disk: file transfer canceled |
| TRANS28 | Invalid option XXXXXXXX: file transfer canceled |
| TRANS29 | Invalid option XXXXXXXX with RECEIVE: file transfer canceled |
| TRANS30 | Invalid option XXXXXXXX with APPEND: file transfer canceled |
| TRANS31 | Invalid option XXXXXXXX without SPACE: file transfer canceled |
| TRANS32 | Invalid option XXXXXXXX with PDS: file transfer canceled |
| TRANS33 | Only one of TRACKS, CYLINDERS, AVBLOCK allowed: file transfer canceled |
| TRANS34 | CMS file not found: file transfer canceled |
| TRANS35 | CMS disk is Read-Only: file transfer canceled |
| TRANS36 | CMS disk is not accessed: file transfer canceled |
| TRANS37 | CMS disk is full: file transfer canceled |
| TRANS99 | Host program error code XX XXXXXXXX: file transfer canceled |

Which of these TK5's IND$F205 actually sends was not tested beyond TRANS03 (implied by the
successful runs): NOT VERIFIED.

---

## 9. What RetroTerm needs, in short

1. A DDM Query Reply (section 5) with LIMIN = LIMOUT = a set size, default 16384.
2. A D0 SF handler with the states: idle, open for data (upload or download), open for message.
3. Replies built exactly as sections 3.4-3.11, each as `88` + one SF.
4. A transfer start that types the IND$FILE command into the input field, refusing if it does
   not fit (section 2.5).
5. End of transfer only on the host message; TRANS03 and TRANS04 are success.
6. ASCII mode: no translation on the PC; only CR handling (section 6.1).
7. CUT later, if a host needs it.

The tests must be unit tests against recorded byte sequences. A capture of a real TK5 transfer
is needed to make those sequences real, not copied from this text (Open questions 1-3).

---

## Sources

- IBM SS-HCS12-1372-00, "IND$FILE Datastream Description", 1995, pages 11-36. Downloaded from
  the x3270 documentation page (documentation-misc.html, link SS-HCS12-1372-00.pdf), saved as
  `IBM-manuals/SS-HCS12-1372-00.pdf`. IBM's own warning on its cover: "not intended as a
  specification ... has not been submitted to any formal IBM test".
- IBM GA23-0059-4, "3270 Data Stream Programmer's Reference", Dec 1988, `IBM-manuals/`.
  Pages 6-37 and 6-38 read; chapter 5 structured field list scanned.
- IBM "PC/HOST File Transfer and Terminal Emulator Program User's Guide and Reference", first
  edition Sep 1986, from bitsavers (`pdf/ibm/pc/3270/`), saved as
  `IBM-manuals/PC_HOST_File_Transfer_and_Terminal_Emulator_Sep86.pdf`. A 3101-terminal program
  that talks to IND$FILE through a protocol converter; used here only for the command options
  (chapter 4). Its p.1-14 names IND$FILE for VM/CMS (5664-281), MVS/TSO (5665-311) and
  CICS (5798-DQH).
- x3270 source (oracle, read only): `Common/ft.c`, `Common/ft_dft.c`, `Common/ft_cut.c`,
  `Common/sf.c`, `Common/ctlr.c`, `include/ft_dft_ds.h`, `include/ft_cut_ds.h`,
  `include/3270ds.h`, `include/globals.h`.
- IBM tnz source (oracle, read only): `tnz/tnz.py`, `docs/transfer.md`.
- TK5 guide `08-moving-files-between-pc-and-mvs.md` section 1 and its test
  `08-moving-files-between-pc-and-mvs.Tests.ps1` with results 01-04 (22 Nov 2026). The host was
  not contacted for this document.

Searched for and not found: a separate IBM "3270 PC File Transfer Program" manual (SH20-xxxx)
in the bitsavers index; the only file-transfer titles under `ibm/` were the FTTERM guide above
and server-requester programming guides.

---

## Open questions

1. **ANSWERED 23 November 2026, measured on TK5:** the message Open names `FT:MSG ` (with a trailing
   blank), the data Open `FT:DATA`. The data Open for an upload is the long form (0x29 bytes), for a
   download the short form (0x23); the message Open is always short.
2. **Which buffer size did the TK5 test run with**, and does TK5's IND$F205 accept 32639 or
   32767? Needs a trace with the Query Reply and the Open (short or long form).
   **Half answered 24 November 2026**, from the stored recording
   `src\tests\RetroTerm.Tests\Oracle\Recordings\file-transfer.txt`: RetroTerm's DDM Query Reply
   was `00 0C 81 95 00 00 40 00 40 00 01 01` (LIMIN = LIMOUT = 16384) and TK5 took it in both
   directions. 32639 and 32767 were not tried, and the 49-byte file never reached the limit.
   **ANSWERED 24 November 2026, measured on TK5** with s3270 (whose Transfer action takes the size and
   puts it in the DDM reply) through `Tk5WireTap`, a 100,025-byte text file PUT then GET, test
   `Hercules\Tk5TransferBufferSizeTests` (block sizes in section 3.8):
   - **16384:** works. DDM `00 0C 81 95 00 00 40 00 40 00 01 01`; the upload Open is the long form
     (0x29), the download Open the short form (0x23), as at 16384 before.
   - **32639:** works. DDM `00 0C 81 95 00 00 7F 7F 7F 7F 01 01`; 3 full blocks and a short one each
     way, the file back byte for byte.
   - **32767:** the download works, the upload LOSES DATA with no error. DDM
     `00 0C 81 95 00 00 7F FF 7F FF 01 01`. Block 1 (a 32757-byte record) went up, the host sent
     `F6`, s3270 sent block 1 again, and the host then sent TWO Set Cursor + Get messages in a row; s3270
     answered with blocks 2 and 3, the host sent `F6`, s3270 sent block 3 again, then block 4. Both
     sides said the transfer was complete (s3270: "100025 bytes transferred"), but the GET gave back
     65,980 bytes: block 2 was never stored. Why the host asked twice is not known: the inbound
     record (32757 bytes) is within the LIMIN it was told, so by [DS] p. 6-37 the terminal did
     nothing wrong. Not measured: whether RetroTerm at 32767 meets the same loss (its size is
     fixed at 16384, `Ibm3270Emulator.FileTransferBufferSize`), and the largest size that still
     works, which is somewhere from 32639 to 32766.
   RetroTerm keeps 16384, which works; 32639 would also work against TK5.
3. **ANSWERED 23 November 2026, measured on TK5:** yes. Every IND$FILE command was followed by a Read
   Partition Query (`F3 00 05 01 FF 02`), twice in one session. RetroTerm must answer Query at any time.
4. **TK5 defaults conflict with the 1986 manual:** TK5 made `RECFM VB LRECL 255`; FTTERM says
   TSO defaults to `RECFM(F)` and allows LRECL only 1-132. Is that because IND$F205 is a
   different (later or rewritten) program? Matters for what RetroTerm's dialog offers as defaults.
5. **Error code for a PC-side abort:** x3270 sends `0100` to Insert and Get, which IBM's table
   does not list for those requests. What does TK5 do with `0100`, `3E00` and `6000`?
6. **First record number** in the PC's Data ack and Data for Get: 1 or 0? IBM does not say;
   the two oracles appear to differ. RetroTerm sends 1, and TK5 accepted that in both directions
   (23 November 2026); whether 0 would also be accepted was not tried.
7. **ANSWERED 23 November 2026, measured on TK5:** ASCII. The message is 85 bytes:
   `TRANS03   File transfer complete`, then `$`, then blanks. The `$` marks the end of the text,
   which is why x3270 cuts there. Also measured: TK5 sends nothing after the PC's Data ack for the
   message (no Close of the message file), so the transfer ends on that ack, as IBM's figures show.
8. **IND$FILE's fixed ASCII/EBCDIC table** is not in any IBM document read here. Needed only if
   RetroTerm offers remapping to the user's code page.
9. **ANSWERED 24 November 2026, measured on TK5:** the Read Modified resend (section 3.8) IS
   needed. TK5 sends `F6` after every full upload block and stores the second copy; without the
   resend the block is lost and both sides still report success. Still open: the block size at which
   TK5 starts sending `F6` (between 3650 and 16374 bytes of record).
10. **GET/GTP and PUT/PTP:** which verb pair is CUT and which is Asynchronous? Only matters if CUT
    is built.
11. **Does TK5's IND$FILE fall back to CUT** when the Query Reply has no DDM? Only matters if
    RetroTerm ever ships without DDM.
