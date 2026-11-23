# TN3270 and TN3270E - how a connection is set up

This is the connection spec for RetroTerm's coming IBM 3270 emulation: what goes on the wire from
the moment the TCP connection opens until the first screen is shown, and the few telnet commands
used after that (Attn, SysReq). The 3270 data stream itself (commands, orders, fields) is not
covered here.

**Where each fact comes from.** Every statement carries one of these marks:

- `[RFC nnnn s.x]` - the RFC text in `RFC/` next to this file, section x.
- `[x3270 file]` / `[tnz file]` - read in the x3270 or IBM tnz source (oracles: read, never copied).
- `[TK5]` - measured on the local Hercules MVS 3.8J TK5 system, port 3270, 22 Nov 2026.
- `[IBM: page]` - IBM z/OS Basic Skills pages, URLs in "Sources" at the end.
- **NOT VERIFIED** - not read and not measured. Treat as a claim.

The base telnet RFCs (854 telnet, 856 BINARY, 885 EOR, 1091 TERMINAL-TYPE) were NOT fetched for
this document. Option and command numbers below are taken from RFC 1576 s.3, RFC 2355 s.3, and
the oracle source.

---

## 1. Plain words first: what an LU is, and who hands them out

**Background, from IBM.** A 3270 is a "non-programmable (sometimes called dumb)" text terminal
[IBM: intro-3270]. Originally it sat on an IBM network called SNA (Systems Network Architecture,
"a proprietary network architecture developed by IBM") [IBM: tn3270-enhanced]. "The technology
used to move from SNA 3270 applications to TCP/IP is called TN3270, short for Telnet 3270"
[IBM: intro-3270].

**An LU** ("logical unit") is, for our purpose, **the name of one terminal (or one printer) as the
mainframe software knows it.** RFC 2355 s.7.1.1 says it outright: "device-name", "LU name" and
"network name" are the same thing - "a specific terminal or printer device". Names are at most
8 characters, case does not matter [RFC 2355 s.7.1.1].

**Why there is a name at all.** On a modern z/OS, a program called the TN3270E server sits between
TCP/IP and SNA. "The TN3270E server converts a TN3270E TCP/IP connection to an SNA session", and
"the terminal LU of the SNA session is emulated" so that the mainframe program behaves "as though
it were connected to a non-programmable 3270 terminal" [IBM: tn3270-enhanced], [IBM: tn3270e-impl].
So each TCP connection is given one of the server's terminal names (LUs). The server keeps
**pools** of these names; a client that asks for nothing gets any free one ("generic request"), a
client that names one gets that one or is refused ("specific request") [RFC 2355 s.7.1.1].
IBM also lists "mapping an IP address to an LU name" as a server feature [IBM: tn3270e-desc-parent].

**Why a client would ask for a named one:**

- The mainframe has rules tied to the terminal name - which programs it may reach, which printer
  its output goes to, what department it belongs to [RFC 2355 s.7.1.1].
- A printer session can ask for "the printer that belongs to my terminal" (ASSOCIATE), so print
  output from a screen lands in the matching printer tab [RFC 2355 s.7.1.3],
  [IBM: tn3270e-description].
- Site policy may require a fixed terminal name [RFC 2355 s.7.1.4].

How SNA and IP otherwise meet on z/OS (Enterprise Extender wraps SNA "into UDP frames", DLSw "into
TCP packets") is a separate thing from TN3270 [IBM: snaip]. The SNA/IP page does not mention LUs
or the TN3270 server [IBM: snaip].

**What applies to TK5.** TK5 is MVS 3.8J running under Hercules on this PC. **There is no TN3270E
server and no SNA network in that path.** Hercules itself listens on port 3270 (`CNSLPORT 3270` in
`mvs-tk5/conf/tk5.cnf` line 16) and plugs each connection into one of its emulated **local 3270
devices** - device numbers 00C0-00C6 (and 00C7 a 3287 printer) for VTAM, and 03C0-03C7 with the
group name `TCAM` (`tk5.cnf` lines 75-95). The "LU name" a TK5 client can choose is therefore a
**Hercules device number or group name**, not a VTAM LU name [TK5], see section 9. The terminal
names that VTAM inside MVS gives those devices are NOT VERIFIED.

---

## 2. Telnet options and commands used

| Name | Value (decimal / hex) | Used for | Source |
|---|---|---|---|
| IAC | 255 / FF | start of every telnet command | [tnz tnz.py] `IAC -> IAC IAC` |
| SE | 240 / F0 | end of subnegotiation | [tnz tnz.py] |
| SB | 250 / FA | start of subnegotiation | [tnz tnz.py] |
| WILL/WONT/DO/DONT | 251/252/253/254 = FB/FC/FD/FE | option on/off | [TK5] capture, [tnz tnz.py] |
| EOR (command) | 239 / EF | end of one 3270 record | [tnz tnz.py] `IAC EOR` |
| BRK | 243 / F3 | Attn in plain TN3270 | [tnz tnz.py] `send_command(243)  # BRK` |
| IP | 244 / F4 | Attn in TN3270E | [tnz tnz.py] `send_command(244)  # IP` |
| AO | 245 / F5 | SysReq in TN3270E | value: RFC 854 command table (`RFC/rfc854.txt`, read 22 Nov 2026); use: [RFC 2355 s.10.5.2], [x3270 telnet.c] `net_abort` |
| BINARY option | 0 / 00 | 8-bit data both ways | [RFC 1576 s.3] |
| TERMINAL-TYPE option | 24 / 18 | name the terminal | [RFC 1576 s.3] |
| EOR option | 25 / 19 | records end with IAC EOR | [RFC 1576 s.3] |
| TN3270E option | 40 / 28 | the enhanced protocol | [RFC 2355 s.3] |
| TERMINAL-TYPE IS / SEND | 0 / 1 | inside SB 18 | [TK5] `FF FA 18 01 FF F0` from host |

**All three of TERMINAL-TYPE, BINARY and EOR are needed, BINARY and EOR in both directions**
(each side sends WILL and DO) [RFC 1576 s.4]. If the server later turns off BINARY or EOR
(`IAC WONT BINARY` or `IAC WONT EOR`) the session is no longer TN3270 and the client must treat
what follows as plain text (NVT) [RFC 1576 s.4].

Other options a TN3270 client may meet: 3270-REGIME, SUPPRESS-GO-AHEAD, ECHO, TIMING-MARK; any
other option "should be rejected" unless handled for NVT mode [RFC 1576 s.3]. The order of the
TERMINAL-TYPE, EOR and BINARY negotiation "is not significant" [RFC 1576 s.4].

---

## 3. Plain TN3270 - the exact bytes

RFC 1576 s.4 gives the typical order:

```
Server: IAC DO TERMINAL-TYPE
Client: IAC WILL TERMINAL-TYPE
Server: IAC SB TERMINAL-TYPE SEND IAC SE
Client: IAC SB TERMINAL-TYPE IS <name> IAC SE
Server: IAC DO EOR IAC WILL EOR
Client: IAC WILL EOR IAC DO EOR
Server: IAC DO BINARY IAC WILL BINARY
Client: IAC WILL BINARY IAC DO BINARY
Server: <3270 data> IAC EOR
```

**What TK5 actually sent, measured 22 Nov 2026** (client answered `IBM-3278-2`, times in seconds
from connect; `S>C` host to client, `C>S` client to host):

```
0.000 S>C ff fd 18                       IAC DO TERMINAL-TYPE
0.000 C>S ff fb 18                       IAC WILL TERMINAL-TYPE
0.001 S>C ff fa 18 01 ff f0              IAC SB TERMINAL-TYPE SEND IAC SE
0.001 C>S ff fa 18 00 49 42 4d 2d 33 32 37 38 2d 32 ff f0
                                         IAC SB TERMINAL-TYPE IS "IBM-3278-2" IAC SE
0.001 S>C ff fb 00                       IAC WILL BINARY
0.001 C>S ff fd 00                       IAC DO BINARY
0.001 S>C ff fd 00                       IAC DO BINARY
0.001 C>S ff fb 00                       IAC WILL BINARY
0.001 S>C ff fb 19 ff fd 19              IAC WILL EOR  IAC DO EOR
0.001 C>S ff fd 19                       IAC DO EOR
0.001 C>S ff fb 19                       IAC WILL EOR
0.001 S>C f5 42 11 40 40 1d 60 c8 85 ... ff ef      first 3270 record (1535 bytes)
0.004 S>C f5 c3 11 40 40 e3 85 99 ... ff ef         second 3270 record (1324 bytes)
```
[TK5]

Things the capture shows:

- **TK5 never sends `IAC DO TN3270E`.** In every run (sections 9, 10) the only options it raised
  were TERMINAL-TYPE, BINARY, EOR - plus ECHO when the type was not a 3270 [TK5].
- TK5 does **BINARY first, then EOR**, the reverse of the RFC example; harmless, the order is free
  [RFC 1576 s.4], [TK5].
- How TCP split the bytes varies from run to run: in some runs `ff fd 00` and `ff fb 19` arrived in
  one read, in others separately [TK5]. The client must never assume one command per read.
- The client did not wait for anything before the first screen: data starts right after the last
  option answer, with no extra handshake [TK5].

**The first two records, decoded (EBCDIC code page 037)** [TK5]:

1. `F5 42 ...` - F5 is Erase/Write, 42 the control byte (WCC). Text: a Hercules information
   screen - "Hercules Version : 4.9.1.11612-SDL-gee86c4de", host name, host OS, processors,
   "LPAR Name : HERCULES", **"Device number : 0:00C1"**, "Subchannel : 001A", then a big "TK5 /
   Update 5" banner. It uses SBA (`11`), SF (`1D`) orders. Note: this record contains **SA orders
   with colour (`28 42 F6`)** even though the client said `IBM-3278-2`, a type without
   extended attributes [RFC 1576 s.4]. So the client must cope with SA orders whatever type it
   announced [TK5].
2. `F5 C3 ...` - Erase/Write, WCC C3. Text: the TK5 logon screen - "Terminal CUU0C1", date, time,
   ASCII-art logo, "The MVS 3.8j Tur(n)key system", credits, "MVS 3.8j Level 8505", "Update 5",
   **"Logon ===>"**, and "RUNNING TK5". This is the screen a user types the user id into [TK5].

The client then closed the socket. Nothing was typed into the host [TK5].

### Terminal type names (plain TN3270)

| Name | Screen | Meaning | Source |
|---|---|---|---|
| IBM-3278-2 | 24 x 80 | mono, model 2 | [RFC 1576 s.4] |
| IBM-3278-3 | 32 x 80 alternate | model 3 | [RFC 1576 s.4] |
| IBM-3278-4 | 43 x 80 alternate | model 4 | [RFC 1576 s.4] |
| IBM-3278-5 | 27 x 132 alternate | model 5 | [RFC 1576 s.4] |
| IBM-3279-n | as above | colour series, "capable of extended attributes" | [RFC 1576 s.4] |
| ...-E suffix | as above | can take the extended data stream (structured fields; some servers also read it as colour, highlighting) | [RFC 1576 s.4, s.7] |
| IBM-DYNAMIC | any | no fixed alternate size; size comes from Query Reply | [RFC 2355 s.4], [tnz tnz.py] default type |

Every 3270 model has the 24 x 80 "primary" size plus its own "alternate" size [RFC 2355 s.4].
x3270 builds the name as `IBM-327{8|9}-{2..5}` plus `-E` when it supports the extended data stream,
and uses `IBM-DYNAMIC` when a custom size is set [x3270 telnet.c create_3270_termtype], [x3270 model.c].
A client that sends `-E`, `IBM-DYNAMIC` or a printer type **must answer a Read Partition Query**
[RFC 2355 s.12], [RFC 1576 s.7].

### "3278T" and code page 310

The tnz README (line 28) says: "Code page 310 for APL graphic symbols (i.e. 3278T terminal type)"
[tnz README.md]. What that is on the wire, read in tnz:

- tnz sends **no "3278T" terminal type**. Its default type string is `IBM-DYNAMIC` and a user can
  override it; nothing in tnz.py, ati.py or zti.py builds a "3278T" name [tnz tnz.py line 178,
  ati.py line 2663].
- Code page 310 (APL symbols) reaches the screen two ways: the **GE order, byte `08` followed by
  one character byte**, which tnz stores with character set `F1` ("Process Graphic Escape (GE)
  order"); and **character set attribute `F1`** set by SA/SFE, which tnz sends back to the host as
  `08` + byte for each cell [tnz tnz.py lines 3043-3066, 3268-3278, 3612-3630].
- tnz only uses its cp310 table when the local console is UTF-8; otherwise it treats the APL set as
  absent [tnz tnz.py lines 226-237].
- Whether any host expects a terminal-type string with "T" in it is NOT VERIFIED. Not tried on TK5.

---

## 4. TN3270E - the exact bytes (from RFC and oracles; TK5 does not offer it)

**What RetroTerm does (built 23 November 2026, src/RetroTerm.Ibm3270/Tn3270Protocol.cs):**
It answers DO TN3270E with WILL, asks for the device type in its terminal type (an @name suffix
becomes CONNECT name), keeps the device type and LU the host grants, and asks for ONE function,
RESPONSES (`28 03 07 02`, as tnz does). A host that grants less gets basic TN3270E (RFC 2355
section 9); a host REQUEST with more is answered with REQUEST of what both want, and a function
the host removed is not asked for again (RFC 2355 s.7.2.1). 3270-DATA records lose their 5-byte
header on the way in; on the way out the header is `00 00 00 <seq hi> <seq lo>`, the sequence
counting from 0 with RESPONSES (back to 0 after 32767, RFC 2355 s.10.4) and 0000 without. Other
data types are reported rather than shown. A refused device type, or functions the host adds on
its own, end TN3270E with WONT TN3270E and the connection goes on as plain TN3270 (x3270
backoff_tn3270e). In TN3270E, Attn locks the keyboard with Minus Function and Sys Req does
nothing, as x3270 does when no SNA session is bound and SYSREQ is not agreed. Not tested against
a live host: TK5 never offers TN3270E (measured again 23 November 2026: its first bytes are
`FF FD 18`, then TERMINAL-TYPE SEND, then BINARY and EOR, and no `FF FD 28`).

**RESPONSES, as built (RFC 2355 s.10.4.1, GA23-0059-4 Appendix A):** the response is sent AFTER
the emulator has applied the record, on the session thread: the session tells the connection when
a record starts and how it went (`IHostRecordResponseConnection`, filled from
`Ibm3270Emulator.LastRecordOutcome`, which comes from RetroIO's `Display3270.ProcessOutbound`).

| Host RESPONSE-FLAG | Record applied | Record refused |
|---|---|---|
| 00 NO-RESPONSE | nothing | nothing |
| 01 ERROR-RESPONSE | nothing | `02 00 01 <seq> <status> FF EF` |
| 02 ALWAYS-RESPONSE | `02 00 00 <seq> 00 FF EF` | `02 00 01 <seq> <status> FF EF` |

- `<seq>` is the host record's own SEQ-NUMBER, a byte FF in it doubled. The data part is ONE byte
  [RFC 2355 s.10.4.1]; there is no sense code on the wire. The server turns the byte into a sense
  code itself (X'00' -> 0x10030000, X'02' -> 0x10050000).
- `<status>`: **00 Command Reject** when the command code is not one of the eight 3270 commands
  RetroIO takes (F1 F5 7E 6F F2 F6 6E F3), **02 Operation Check** for every other refused record
  (a bad order, buffer address, structured field, or something not built). GA23-0059-4 Appendix A:
  "For non-SNA environments, all 3270 data stream errors are rejected with an Op-Check (OC) except
  for invalid commands. Unsupported commands are rejected with Command Reject (CR)." x3270 makes
  the same split (process_ds: PDS_BAD_CMD for an unknown command; ctlr_write: PDS_BAD_ADDR).
- When the record itself makes a reply (Read Buffer, Read Modified, a query reply) and asked for
  ALWAYS-RESPONSE, the positive response goes just BEFORE that reply, and not again after it
  (x3270 net_output sends tn3270e_ack first). If the record then still fails, the negative one
  goes too, as x3270 does.
- If nothing can say how the record went (an emulator that does not report it, such as the 3287
  printer, or one that threw), no response is sent and an error is reported.

Where RetroTerm differs from x3270 (Common/telnet.c, read 23 November 2026):
- x3270 asks for BIND-IMAGE, RESPONSES and SYSREQ; RetroTerm asks for RESPONSES only, because the
  others are not built.
- x3270 decides "held until the output" from the command (PDS_OKAY_OUTPUT for the reads); if such
  a record never actually sent output, x3270 would send no positive response at all. RetroTerm
  decides from what really happened: a positive response still owed when the record is done is
  sent then.
- x3270 acks an NVT-DATA record that asks for ALWAYS-RESPONSE; RetroTerm does not show NVT data
  yet, reports it, and does not answer it.

TN3270E starts when the server sends `IAC DO TN3270E` and the client answers `IAC WILL TN3270E`
(either side may start, but the server normally does) [RFC 2355 s.13.2]. Then the device type is
agreed **inside TN3270E subnegotiation**, not with TERMINAL-TYPE.

Command codes inside `SB 28` [RFC 2355 s.3], [x3270 tn3270e.h]:

| Code | Name | | Code | Name |
|---|---|---|---|---|
| 00 | ASSOCIATE | | 05 | REASON |
| 01 | CONNECT | | 06 | REJECT |
| 02 | DEVICE-TYPE | | 07 | REQUEST |
| 03 | FUNCTIONS | | 08 | SEND |
| 04 | IS | | | |

Sequence (hex; `name` is ASCII, upper case) [RFC 2355 s.7.1, s.7.2], [tnz tnz.py lines 2199-2296]:

```
S>C FF FD 28                               DO TN3270E
C>S FF FB 28                               WILL TN3270E
S>C FF FA 28 08 02 FF F0                   SEND DEVICE-TYPE
C>S FF FA 28 02 07 <type> FF F0            DEVICE-TYPE REQUEST <type>
    or FF FA 28 02 07 <type> 01 <lu> FF F0       ... CONNECT <lu>
    or FF FA 28 02 07 IBM-3287-1 00 <lu> FF F0   ... ASSOCIATE <terminal lu>  (printer only)
S>C FF FA 28 02 04 <type> 01 <lu> FF F0    DEVICE-TYPE IS <type> CONNECT <lu given>
 or S>C FF FA 28 02 06 05 <reason> FF F0   DEVICE-TYPE REJECT REASON <reason>
C>S FF FA 28 03 07 <function list> FF F0   FUNCTIONS REQUEST
S>C FF FA 28 03 04 <list> FF F0            FUNCTIONS IS   (agreed)
 or S>C FF FA 28 03 07 <smaller list> FF F0  server asks for less; client answers IS or REQUEST again
```

Reject reasons: 00 CONN-PARTNER, 01 DEVICE-IN-USE, 02 INV-ASSOCIATE, 03 INV-NAME, 04
INV-DEVICE-TYPE, 05 TYPE-NAME-ERROR, 06 UNKNOWN-ERROR, 07 UNSUPPORTED-REQ [RFC 2355 s.3],
[tnz tnz.py line 2215].

- The server takes or refuses the request **as a whole** [RFC 2355 s.7.1.4].
- The IS reply carries the **name the server actually gave** (the LU). A client that asked for a
  specific name and gets another one may end the TN3270E negotiation [RFC 2355 s.7.1.4]. This is
  how RetroTerm can show the user which terminal name each tab got.
- Valid device types in TN3270E: IBM-3278-2..5 with or without -E, IBM-DYNAMIC, printer
  IBM-3287-1 [RFC 2355 s.4]. Note: **no 3279 names** in that list; "3278" is not meant to rule out
  colour - colour support is told by Query Reply [RFC 2355 s.4]. IBM's page says z/OS "supports
  models beginning with 3278-2 through 3279-5" [IBM: tn3270e-description], which differs; which one a
  real z/OS server accepts is NOT VERIFIED.
- tnz asks only for RESPONSES (`28 03 07 02`) [tnz tnz.py line 2251]. x3270 asks for BIND-IMAGE,
  RESPONSES, SYSREQ (and CONTENTION-RESOLUTION in some builds) [x3270 telnet.c lines 902-906].
  pr3287 (the printer) asks for BIND-IMAGE, DATA-STREAM-CTL, RESPONSES, SCS-CTL-CODES, SYSREQ
  [x3270 pr3287/telnet.c lines 360-364].

### Functions

| Code | Name | What it gives | Source |
|---|---|---|---|
| 00 | BIND-IMAGE | server passes the SNA BIND (session start) and UNBIND to the client | [RFC 2355 s.10.3] |
| 01 | DATA-STREAM-CTL | printer data stream control | [RFC 2355 s.10.2] |
| 02 | RESPONSES | host can ask "did it work?"; client answers per sequence number | [RFC 2355 s.10.4] |
| 03 | SCS-CTL-CODES | printer: SNA character stream control codes | [RFC 2355 s.10.1] |
| 04 | SYSREQ | SysReq key over telnet AO | [RFC 2355 s.10.5] |
| 05 | CONTENTION-RESOLUTION | named in x3270, not in RFC 2355's table | [x3270 tn3270e.h] |
| 06 | SNA-SENSE | named in x3270, not in RFC 2355's table | [x3270 tn3270e.h] |

### The 5-byte TN3270E header

Every TN3270E data record is `<5-byte header><data><IAC EOR>`; the data part may be empty
[RFC 2355 s.8].

```
| DATA-TYPE (1) | REQUEST-FLAG (1) | RESPONSE-FLAG (1) | SEQ-NUMBER (2, big-endian) |
```
[RFC 2355 s.8.1], [x3270 tn3270e.h] (`EH_SIZE 5`)

DATA-TYPE: 00 3270-DATA, 01 SCS-DATA, 02 RESPONSE, 03 BIND-IMAGE, 04 UNBIND, 05 NVT-DATA,
06 REQUEST, 07 SSCP-LU-DATA, 08 PRINT-EOJ [RFC 2355 s.8.1.1]; x3270 also has 09 BID
[x3270 tn3270e.h].

REQUEST-FLAG only means something when DATA-TYPE is REQUEST (00 ERR-COND-CLEARED); otherwise send
00 [RFC 2355 s.8.1.2]. x3270 also names 01 SEND-DATA, 02 KEYBOARD-RESTORE, 04 SIGNAL
[x3270 tn3270e.h].

RESPONSE-FLAG on 3270-DATA / SCS-DATA: 00 no response wanted, 01 answer only on error, 02 always
answer. On a RESPONSE record: 00 positive, 01 negative [RFC 2355 s.8.1.3].

SEQ-NUMBER is only used when RESPONSES was agreed; otherwise 0000. **A byte FF in it is doubled
like any other data** [RFC 2355 s.8.1.4].

A positive answer as tnz builds it, for a host record with RESPONSE-FLAG 02 and sequence number n:
`02 00 00 <n hi> <n lo> 00 FF EF` - type RESPONSE, flags, sequence, data byte 00 = Device End
[tnz tnz.py lines 2119-2125].

The client sends its own 3270 input with header `00 00 00 00 00` [tnz tnz.py send_3270_data].

---

## 5. Several tabs to one host

- **Plain TN3270** has no way to ask for a name in the protocol; each connection gets whatever the
  server gives [RFC 2355 s.7.1.1] ("traditional tn3270 clients" get the generic pool).
  Some clients put it into the terminal type as `<type>@<name>` - tnz does exactly this
  [tnz tnz.py send_terminal_type]. **TK5/Hercules honours that form** (section 9) [TK5].
- **TN3270E**: each tab sends its own DEVICE-TYPE REQUEST, either generic (gets any free name) or
  with `CONNECT <name>` (that name or refused, with DEVICE-IN-USE if another session has it)
  [RFC 2355 s.7.1.2]. A printer tab for a terminal tab sends `ASSOCIATE <the terminal's name as
  returned in IS>` [RFC 2355 s.7.1.3].
- Each tab is a separate TCP connection with its own negotiation; nothing is shared [RFC 2355 s.7.1].

---

## 6. Records and IAC inside them

- A 3270 record has no length field; **`IAC EOR` ends it** [RFC 1576 s.6], [RFC 2355 s.8].
- **Any byte FF inside a record is sent as `FF FF`** and one `FF` is kept on receipt; a single FF is
  a telnet command [RFC 2355 s.8], [RFC 1576 s.6], [tnz tnz.py] `value.replace(b"\xff", b"\xff\xff")`.
  The sender doubles, the receiver undoubles - on both directions, and in the TN3270E header too.
- Telnet commands should come **between** records, not inside them; if IAC IP or IAC AO arrive
  inside a record the receiver processes them after the record [RFC 2355 s.8].
- **Records split across TCP reads, and several records come in one read.** The TK5 records of
  1535 and 1324 bytes came as separate reads here, but option replies were split and joined
  differently from run to run [TK5]. The parser must be a byte-by-byte state machine that keeps
  partial state (half a record, an `FF` at the end of a read, an unfinished `SB ... IAC SE`)
  between reads. The capture script did this and found no record broken.

---

## 7. Attn and SysReq

| Key | Plain TN3270 | TN3270E | Source |
|---|---|---|---|
| Attn | `IAC BRK` (FF F3) | `IAC IP` (FF F4) | [RFC 1576 s.8], [RFC 2355 s.11] |
| SysReq | `IAC IP` on some servers, Test Request key on others | `IAC AO`, only if SYSREQ function agreed | [RFC 1576 s.9], [RFC 2355 s.10.5.2] |

- tnz: Attn = IP when TN3270E, else BRK [tnz tnz.py attn()].
- x3270: in plain TN3270, Attn sends **PA1 (a Read Modified with AID PA1) and then IAC BRK**, "what
  PCOMM does"; in TN3270E it sends IP only when the session is bound [x3270 kybd.c Attn_action].
  SysReq (net_abort) sends AO only if SYSREQ was agreed [x3270 telnet.c net_abort].
- A server for non-SNA 3270 devices ignores BRK / IP [RFC 1576 s.8], [RFC 2355 s.11]. **TK5 is
  non-SNA local 3270 devices under Hercules** (section 1), so Attn may do nothing there. Hercules'
  own notes say "Attn ... on a Hercules local 3270 terminal it is often not passed" (TK5 notes,
  `docs/01-first-login-and-3270-basics.md` line 252). Not measured: sending Attn would be input
  to the host, which this work was told not to do. NOT VERIFIED on TK5.
- TN3270E SysReq in detail: after AO the server enters "suspended mode", data goes as
  SSCP-LU-DATA, LOGOFF ends the session, a second AO resumes [RFC 2355 s.10.5.2].

---

## 8. Things TK5 does with other terminal types (measured)

All runs 22 Nov 2026, same script, each one disconnected after the first screens [TK5].

| Sent as TERMINAL-TYPE | What TK5 did |
|---|---|
| IBM-3278-2 | negotiated BINARY + EOR, two screens, device 00C1 |
| IBM-3278-2-E | identical negotiation; same two records |
| IBM-3279-2-E | identical negotiation; same two records |
| IBM-3278-3 / -4 / -5 / -5-E | identical negotiation; the records shown had the same lengths. Whether the logon screen used the bigger size was not checked (records begin with Erase/Write `F5`, not Erase/Write Alternate `7E`). |
| IBM-3287-1 | negotiated BINARY + EOR, then **sent no data** within 3 s (00C7 is defined as a 3287 in tk5.cnf; why nothing came is NOT VERIFIED) |
| VT100 | host sent `FF FD 01` (DO ECHO) and then **plain ASCII text** "HHC01027I Hercules version 4.9.1..." - Hercules treats a non-IBM type as a line-mode console connection |
| IBM-9999-9 | negotiated BINARY + EOR, then asked **again** `FF FA 18 01 FF F0` (TERMINAL-TYPE SEND); after the same answer it **closed the connection** |

No run ever got `IAC DO TN3270E` [TK5].

**Offering TN3270E ourselves.** Right after connect, the client sent `FF FB 28 FF FD 28` (WILL
TN3270E, DO TN3270E) unasked. TK5 answered `FF FE 28` (DONT TN3270E) [TK5]. A WONT reply to the DO
was not seen in the 2 seconds the probe waited; NOT VERIFIED whether it comes later.

**So: TK5 does not offer TN3270E and refuses it when offered. RetroTerm must speak plain TN3270
to reach it** [TK5].

---

## 9. Choosing the terminal on TK5 (the "@name" form)

Terminal type sent as `IBM-3278-2@<name>` [TK5]:

| name | Result |
|---|---|
| 00C2 | accepted; info screen says "Device number : 0:00C2" |
| C3 | accepted; info screen says "Device number : 0:00C3" (short form works) |
| TCAM | accepted (group name from `tk5.cnf`); one record received |
| TERM01 | refused with a 3270 screen `F5 40 ...` reading "HHC01029E Connection rejected, no available 3270 device in the TERM01 group", then closed |

So on TK5 the "LU name" is a Hercules device number or a device group name. With `@00C2`,
`@C3` and `@TCAM` only the first (Hercules) record came within the 3-second wait; the TK5 logon
screen did not. Why is NOT VERIFIED (it may need a longer wait, or VTAM may treat those devices
differently).

The Hercules source was not available here, so the full rules for the `@` form are NOT VERIFIED;
only the four cases above were measured.

---

## 10. What the RetroTerm client should do (derived from the above)

1. Answer `DO TERMINAL-TYPE` with WILL; answer `SB 18 01` with the configured type (default
   `IBM-3278-2`), optionally `@<name>` for TK5-style hosts.
2. Answer `DO`/`WILL` for BINARY and EOR with WILL/DO; refuse other options except ECHO /
   SUPPRESS-GO-AHEAD in NVT mode [RFC 1576 s.3].
3. Answer `DO TN3270E` with WILL, then the DEVICE-TYPE / FUNCTIONS exchange of section 4. Built
   with the RESPONSES function, basic TN3270E when the host will not have it (see the top of
   section 4).
4. Treat the session as 3270 only while BINARY and EOR are on in both directions (plus TN3270E
   agreed, if used). Back to NVT text if either goes off [RFC 1576 s.4].
5. Byte-level parser that survives any TCP split, undoubles FF, ends records on IAC EOR, strips
   the 5-byte header in TN3270E and answers RESPONSES when asked.
6. Accept SA and other extended orders even after announcing a non -E type [TK5].
7. Attn: BRK in TN3270, IP in TN3270E. SysReq: AO in TN3270E with SYSREQ agreed; in plain TN3270
   it is server-dependent [RFC 1576 s.9].
8. Show the user the terminal name the host assigned (IS CONNECT in TN3270E; on TK5 the
   "Device number" / "CUU0C1" line of the screen).

---

## Sources

- RFC 1576 TN3270 Current Practices; RFC 2355 TN3270 Enhancements (replaces RFC 1647); RFC 1646
  TN3287 printers; RFC 1041 3270-REGIME; RFC 6270 tn3270 URI - all in `RFC/` beside this file.
- Base telnet RFCs, NOT read for this document: RFC 854 https://www.rfc-editor.org/rfc/rfc854,
  RFC 856 https://www.rfc-editor.org/rfc/rfc856, RFC 885 https://www.rfc-editor.org/rfc/rfc885,
  RFC 1091 https://www.rfc-editor.org/rfc/rfc1091.
- x3270 source (the Hercules folder's copy): `Common/telnet.c`, `Common/kybd.c`, `Common/model.c`,
  `Common/pr3287/telnet.c`, `include/tn3270e.h`.
- IBM tnz source: `tnz/tnz.py`, `tnz/ati.py`, `README.md`.
- TK5 notes: `README.md`, `docs/01-first-login-and-3270-basics.md`, `mvs-tk5/conf/tk5.cnf` in the
  Hercules folder.
- IBM z/OS Basic Skills (fetched 22 Nov 2026, the pages loaded fine):
  - [IBM: tn3270-enhanced] https://www.ibm.com/docs/en/zos-basic-skills?topic=mainframe-tn3270-enhanced
  - [IBM: intro-3270] https://www.ibm.com/docs/en/zos-basic-skills?topic=enhanced-introduction-3270-terminal
  - [IBM: snaip] https://www.ibm.com/docs/en/zos-basic-skills?topic=mainframe-snaip-implementation
  - [IBM: tn3270e-desc-parent] https://www.ibm.com/docs/en/zosbasics/com.ibm.zos.znetwork/znetwork_266.htm
  - [IBM: tn3270e-description] https://www.ibm.com/docs/en/zosbasics/com.ibm.zos.znetwork/znetwork_268.htm
  - [IBM: tn3270e-impl] https://www.ibm.com/docs/en/zosbasics/com.ibm.zos.znetwork/znetwork_276.htm
- Measurements: a Python capture script run against localhost:3270 on 22 Nov 2026; nothing was
  typed into the host; the script was deleted afterwards.

---

## Open questions

1. Does Attn (IAC BRK) do anything on TK5? Not tried, because it is input to the host.
2. Do models 3, 4, 5 get a larger screen on TK5 (Erase/Write Alternate, `7E`)? Only the record
   lengths were compared.
3. Why did `@00C2`, `@C3`, `@TCAM` connections get only the Hercules screen and not the TK5 logon
   screen within 3 seconds? Needs a longer wait.
4. Why did `IBM-3287-1` get no data? Needs a printer job, or the Hercules source.
5. The full Hercules rules for the `@name` form (case, group names, what "TCAM" connects to) -
   needs the Hercules source.
6. Does TK5 send `WONT TN3270E` in reply to an unasked `DO TN3270E`? Only `DONT` was seen.
7. Does a real z/OS TN3270E server accept `IBM-3279-n` names in DEVICE-TYPE (IBM page says yes,
   RFC 2355 lists only 3278)? No such server here.
8. Is there any host that expects a "3278T" (APL) terminal-type string? Nothing found in tnz.
9. The telnet AO byte value (245) was not read from RFC 854.
