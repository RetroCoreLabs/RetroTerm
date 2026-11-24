# Known defects in the code

Defects found in the source and not yet fixed. Each entry names the file and lines, the evidence,
and how far it has been checked. Fix it or leave it here; never work around it.

B1 and B2 were fixed on 8 October 2026 and are gone from this file:

- B1, NDSAR read the attribute from the wrong parameter. NDSAR, NDAAR, NDRAR and NDFC now take the
  two corners first, counted from 1, then the attributes or characters, as ND-12054-1 sections 5.50,
  5.36, 5.46 and 5.43 give them. NDDWA counts from 1 too (section 5.41). NDSAR resets what the cells
  had, and the attribute numbers are the SGR table of section 5.67.
  `src\tests\RetroTerm.Tests\TDV\TdvRectangleSequencesFollowTheManualTests.cs` is written from the manual.
- B2, two dead `$y` reply helpers and a `?` where `>` is expected. The helpers are deleted and the
  secondary DA default answers `>`.

## B3 - Rectangle functions: what the manual leaves open

`src\RetroTerm.Core\Terminal\Emulators\TDV\TDVEmulatorBase.cs`, `TryReadRectangle` and
`HandleFillCharacter`.

- **One to three corner parameters.** NDSAR, NDAAR, NDRAR, NDFC and NDDWA ignore a sequence with
  one, two or three parameters. The manual gives a default only for NO parameters ("full screen or
  whole work area") and says nothing about a partial list, so ignoring is a choice, not a reading of
  the manual.
- **NDFC with more than one character.** Section 5.43 says NDFC fills the rectangle "with a
  character or a string of characters", at most ten, but not how a string is laid out over the
  rectangle (repeat along each line, along the whole rectangle, restart per line). A string is
  counted as an unrecognised sequence and draws nothing. The test
  `NdfcWithAStringOfSeveralCharactersIsNotImplementedYet` pins that and must change when this is
  settled. Not checked against a real terminal.
- **Parameter errors are not latched.** The manual says an invalid rectangle "occurs as a parameter
  error"; this program ignores the sequence and records no error (NDRQ report type 5, bit 7).
  See the notes at `TDVEmulatorBase.cs` lines 623 and 2011.

## B4 - Origin Mode is DEC's, not the TDV's

Checked 8 October 2026 by reading the code and the manual, no terminal.

ND-12054-1 section 4.8 says that with Origin Mode on, line and column numbers are relative to the
work area and the cursor cannot leave it. The switch is private mode 6, `CSI ? 6 h` (table at the
start of the "ND Private Sequences" section of 5.64). `TerminalEmulatorBase.cs` keeps mode 6 as DEC's
DECOM, which is relative to the SCROLL REGION (`RowAddressingOrigin`, line 5194) and has no idea of
the TDV work area (`TDVWorkAreas`).

What follows from it:

- CUP, HVP and the rectangle functions NDSAR, NDAAR, NDRAR and NDFC never treat their coordinates as
  work-area relative.
- NDDWA (section 5.41) is also meant to put the cursor in the home position. It does not.

Fixing it means deciding how the TDV work area and the DEC scroll region relate when both exist, and
reading section 4.8 and 5.41 for what the cursor boundary is. Do it from the manual, with tests
written first.

## B5 - TDV `CSI m` and the manual's SGR table disagree

`src\RetroTerm.Core\Terminal\Emulators\TerminalEmulatorBase.cs` `HandleSgr`, line 3771, which the
three TDV classes inherit.

ND-12054-1 section 5.67 lists SGR 0 reset, 1 ignored, 2 low intensity, 3 ignored, 4 underlined,
5 slow blink, 6 ignored, 7 inverse, 8 invisible. `HandleSgr` is the ECMA-48 one: 1 is bold, 3 is
italic, 6 is rapid blink, 9 is strikethrough and 30 to 49 set colours. The rectangle functions now
follow the manual's table (so NDAAR with 1 does nothing), while a plain `CSI 1 m` still gives bold.
The 2215 test `Ndrar_RemovesTheAttributeFromTheRectangle` used to set bold with `CSI 1 m`, which is
how this was seen.

Not decided: whether the real terminals ignore SGR 1 (the manual says so for the 1200), whether the
TDV 2200 and 2215 differ, and what the 'Graphic Rendition Mode' switch (mode 62, ATTR / UNDERLINE /
SGR, `TDVEmulatorBase.cs` line 799) changes. Needs the 2215 and 2200 manuals read for their own SGR
tables before anything is changed.

## B6 - Widening pulls history back onto the screen; libvterm's corpus says it should not

`src\RetroTerm.Core\Terminal\Buffer\TerminalBuffer.cs`, `ResizeWithReflow`. Decided 10 October 2026
after Ronny's welcome screen came back from a shrink-and-grow with its logo in pieces.

`ResizeWithReflow` used to re-lay out the screen only. Narrowing pushed rows into scrollback at the
narrow width and widening never re-wrapped or pulled them back. When the window gets WIDER it now
takes the history and the screen as one document, so the rows a shrink wrapped are rejoined and, when
widening leaves room, come back onto the screen. Two limits, both measured: narrowing leaves old
history alone, and widening folds history in only from the row after the last old line that is longer
than the new width. The ring holds a fixed number of ROWS, and re-wrapping every old line at every
width a drag passes through deleted the oldest history of a full ring (10,000 rows became 3,308 over
one drag).

The libvterm script `69screen_reflow.test`, "Shell wrapped prompt behaviour", last step (`RESIZE 5,16`),
expects the opposite for that one case: the screen keeps its five rows in place, a blank row appears at
the bottom and the first prompt stays in history. We disagree on five assertions there (screen rows 0
to 3 and the cursor). They are recorded in `ExpectedFailures` in
`src\tests\RetroTerm.Tests\Conformance\LibVtermConformanceTests.cs` with the reason.

This is a choice, not a mistake: libvterm's scrollback lives in the host, outside the terminal, so it
cannot reflow history or pull anything back. Ours is in the buffer. Not decided: whether xterm,
Windows Terminal or a real DEC terminal fill a screen from history like this. DEC terminals could not
resize at all, and no capture of the others is held here. If a capture of xterm shows it keeps the
rows in place, this entry becomes a real defect and the old rule comes back.

## B7 - `TerminalSession_ShouldSendTDV2215SecondaryDA_WithCorrectFirmwareID` fails inside the full suite

`src\tests\RetroTerm.Tests\Session\TerminalSessionRealConnectionTests.cs`, the test of that name.

- **Seen:** red once, 23 November 2026, in a full run (`Category!=Hercules`; 1 of 7,256, reported
  time 2 ms). Run alone three times straight after, it passed each time. The run printed only the
  name, not the message, so the cause is NOT known.
- **What is known:** the test talks over a real socket on fixed port 9991 (no other test uses it)
  and waits with `Task.Delay(200)` and `Task.Delay(500)` before it asserts that a reply arrived.
  That is the shape CLAUDE.md ("Never wait on the clock and then assert") says fails on a busy
  machine. But a 2 ms failure did not reach those waits, so the sleep may not be the whole story.
- **Not done:** not reproduced, not fixed. The next step is to catch the message: run the file
  repeatedly in the full suite with `--logger "console;verbosity=normal"` until it fails. Then
  replace the sleeps with a deadline poll on the received replies, as
  `TerminalSessionTestServerIntegrationTests.WaitForResponseAsync` does.

**Decided 24 November 2026 (Ronny):** RetroTerm keeps PA1 then break, as the current x3270 source does (its comment says IBM PCOMM does the same). The defect left is the test's reference: Tk5AttnTests compares with the installed s3270 4.5ga6, which sends break only, so it stays red until it runs against an s3270 built from current x3270.

## B10 - TK5 printer: a JRP job begins on PRT4 and no byte ever reaches the printer

`src\tests\RetroTerm.Tests\Hercules\Tk5PrinterTests.cs` (category Hercules, needs TK5) starts
PRT4 from the JRP console and prints two class G jobs through RetroTerm's 3287 session, with every
byte recorded by a relay in front of it. Found 23 November 2026. Usually green; red when this happens.

What is known, from the Hercules log (`mvs-tk5\log\3033.log`) and the relay:

- Three jobs of the 24 submitted that day: JOB00676 (17:18:56), JOB00692 (17:42:58) and JOB00697 (17:49:19).
  For each, JRP logged `JRPI400 ... BEGINNING ON PRT4(CUU0C7)` and never `JRPI403 ... ENDED`, and the
  relay saw NO byte from the host to the printer after the telnet negotiation. JOB00676 was the
  first job after starting PRT4, JOB00692 the second, JOB00697 the first.
- In the runs that worked, the printer sent the host nothing after the negotiation (IAC WILL/DO
  TERMINAL-TYPE, BINARY, END-OF-RECORD). For the three failed jobs the printer's side was not
  decoded (the failure report showed only the host's side then; it shows both now).
- Hercules sends a write to a 3287 client the moment the channel program runs it
  (`source\hyperion\console.c`, `loc3270_execute_ccw`, the `write:` block and `sendto_client`), and
  answers with channel end and device end at once. So the write was not run, or not issued.
- The begun job is not lost: the next time PRT4 was started, JRP printed it first (JOB00692 and
  JOB00697 both came out at the start of the next run). The test prints such leftovers before its
  own jobs and leaves them out of the checks.

What is not known: whether RetroTerm's printer, Hercules or JRP holds the job. Next step, on the
Hercules console (which only Ronny can type into): `t+00C7` before a run, `t-00C7` after; the CCW
trace then shows whether VTAM issued a write to 00C7 for the job that hung, and what came back.

**Traced 23 November 2026 (Ronny typed `t+00C7` in the Hercules console; four runs of
`Tk5PrinterTests`, one lost a job). From `mvs-tk5\log\3033.log`:**

- Job 716, 19:38:39: `JRPI400 HERC03P1 JOB00716 BEGINNING ON PRT4`, then at once
  `HHC01334I 0:00C7 CHAN: ORB ... CCW:00813010`, the write (`ccw 0D813048 ... C8D9E3E3...`,
  "HRTT PRINTER JOB"), `stat 0C00` and the I/O interrupt, then `JRPI403 ... ENDED`.
- Job 717, 19:38:49: `JRPI400 HERC03P2 JOB00717 BEGINNING ON PRT4` and then NOTHING for 00C7:
  no ORB, no CCW, no interrupt, until the test drained PRT4 at 19:40:47.
- At the next start, 19:41:10, job 717 begins again and its writes go out at once.

So MVS never started a channel program for the lost job. No byte was ever offered to the device,
so neither RetroTerm nor Hercules' 3287 code is involved: the job is held inside JRP (or the MVS
I/O path below it) before any I/O is issued. Why JRP stops there is not known; JRP is TK5's own
printer program and its source is not in this repository. RetroTerm cannot fix it; the test
reports it when it happens.
## B11 - JRP console screen: message pieces in odd places, not compared with s3270

While the JRP console showed messages as they happened (PF1), RetroTerm's screen had pieces of
messages where none should be: `JRP100I OK` starting in column 69 of a row that already had one,
a lone `0` in column 1, `,S=N` at the end of the PF key legend row and `000000` at the end of the
`Enter JRP command ==>` row (23 November 2026, `Tk5PrinterTests` output). JRP writes one record per
message line: SBA to the line, SF, SA, then the text, some of it 81 characters after the SF, so it
runs one position into the next row (decoded from the console's records the same day). Whether a
real 3270 and s3270 show the same pieces has NOT been checked; the screen was not compared. The
test reads JRP's answers from the records, not the screen, so it does not depend on this. Next
step: run the same JRP keys in s3270 and RetroTerm and compare the screens, as the guide tests do.

## B9 - 3270 Attn at TSO's READY: the screen after the next Enter differs from s3270

`src	ests\RetroTerm.Tests\Hercules\Tk5AttnTests.cs` (category Hercules, needs TK5) logs in,
goes to READY, presses Clear, Attn, then Enter, in s3270 and in RetroTerm, and compares every
screen. Found 23 November 2026. It is red. Cause found the same day: the two terminals send
different bytes for Attn, and the two x3270 sources disagree about which is right.

Measured, 23 November 2026, same steps, s3270 run with `-trace -tracefile`:

- s3270 (`s3270 v4.5ga6`, the copy named in tk5-tests.json) sends for Attn only `FF F3`
  (IAC BREAK). No PA1. Its Enter then sends `7D 40 40` and the host answers
  `F1 C3 11 C1 50 1D C8 D9 C5 C1 C4 E8 40 1D 40 11 C2 60 13`: Write, WCC C3 (reset, restore,
  reset MDT), SBA row 2 col 1, SF intensified, `READY `, SF default, SBA row 3 col 1, IC.
- RetroTerm sends `6C` (PA1, short read) and then IAC BREAK. Its Enter sends `7D 40 40`, and the
  host answers with TWO records: `F1 C1 11 5D 7F 1D 40 1D C8 4F 1D 40 11 C1 50 13` (Write, WCC C1:
  reset and reset MDT but NOT restore; SBA 1919, SF, SF intensified, `4F` = a bar at row 1
  col 2, SF, SBA row 2 col 1, IC) - this is TSO answering the PA1 - and then the same READY
  record s3270 got.
- RetroTerm applies both records correctly: the second Write does not erase, so the bar from the
  first stays on row 1. The screens differ because the HOST was sent different things.

Why the bytes differ: x3270's `Common\kybd.c` Attn_action, plain TN3270 case, read
23 November 2026 from github.com/pmattes/x3270:

- tag `4.5ga6` (the installed s3270): `net_break(0)` only.
- `master`: `ctlr_read_modified(AID_PA1, false); net_break(0);`, commented "This is what PCOMM
  does in plain TN3270 mode: PA1, then TELNET BREAK". RetroTerm (`Ibm3270Emulator` ATTN,
  RetroIO `Display3270.Attn`) follows master. The commit that added it was not found.

No IBM manual held here describes Attn on a plain TN3270 connection, so neither version is the
spec. Open: which one RetroTerm should follow is Ronny's decision. Following 4.5ga6 makes the
test green; following master needs an s3270 built from master as the oracle.

## B12 - SSH to "localhost" still waits about 2 seconds when the host listens only on IPv4

"localhost" resolves to ::1 first and 127.0.0.1 second here, and Windows reports a refused
connect to ::1 only after about 2 s. Every connection RetroTerm opens itself now goes through
`src\RetroTerm.Core.Protocols\TcpConnector.cs`, which tries IPv6 and IPv4 with a 100 ms head start
each (RFC 8305) and keeps the first that answers: telnet, TN3270 (and the 3287 printer, which uses
the same connection), SUBMIT's card reader and the RETH Ethernet backend. Measured 23 November
2026 against TK5 on `localhost`: 3270 and 3505 took 2015 to 2069 ms before and 107 to 141 ms after
(127.0.0.1 directly: under 1 ms both before and after).

What is left is SSH. `src\RetroTerm.Core.Protocols.Net\SSHConnection.cs` hands the host name to
SSH.NET, which opens the socket itself, and SSH.NET 2026.0.0 has no public way to give it a socket
connected elsewhere (its constructors take only a ConnectionInfo or host, port and credentials).
Measured the same day: SSH.NET against `localhost:3270` (an IPv4-only listener) failed after
5083 and 5032 ms, against `127.0.0.1:3270` after 3015 and 3011 ms, with a 3 s read timeout set in
both - the 2 s difference is the connect. Fixing it means either a change in SSH.NET or connecting
the socket in RetroTerm and running SSH over it; neither is done.

## B22 - Once, PF3 from TK5's ISPF menu under s3270 showed CLST020 instead of READY

Seen once on 24 November 2026 in a Scenario10 run with s3270: PF3 from the ISPF primary menu showed
"CLST020 LIST data set not allocated" instead of READY. It did not happen again and the cause is not
known. If it comes back, keep the s3270 trace of that run.

## B24 - 3270: code page 310 X'81' and X'82' (the double bars) are drawn as blanks

`src\RetroTerm.Ibm3270\Ibm3270Emulator.cs`, `CellFor`: a Graphic Escape byte (or one in character
set X'F1') goes through `CodePage.Cp310.ToUnicode`, which gives nothing for X'81' and X'82', and the
cell is then drawn as a blank. The 3287 printer gets no character for them either
(`Printer3287.cs` line 607; what it then prints was not checked).

- **What the real terminal shows:** X'81' "Double Vertical, Bar Graphic" and X'82' "Double
  Horizontal, Bar Graphic" (IBM registry CP00310.txt); the picture in GA27-2837-9 fig. 10-41 has lines
  at the left and right cell edges, and at the top and bottom edges. No Unicode character draws that
  (║ and ═ are centred lines), which is why the table leaves them empty
  (`docs\spec\IBM3270\EBCDIC.md`, "Every disagreement", rows X'81' and X'82': "Draw it ourselves").
- **Measured, 24 November 2026:** `Ibm3270CodePage310GlyphTests` draws every code page 310 byte;
  these two are the only ones with no character, so they are skipped there and nothing checks them.
  Not seen from TK5: no TK5 panel looked at sends either byte (EBCDIC.md, "TK5").
- **The fix:** draw the two edge lines in the renderer for these two bytes, the way a cell's
  underline is drawn, and add a pixel test for each. Not started.

## B23 - Binary IND$FILE: s3270 types one blank more than RetroTerm, so the file-transfer-binary oracle case is red

`src\tests\RetroTerm.Tests\Oracle\HostStreamOracleTests.cs`, case `file-transfer-binary` (recording
`Oracle\Recordings\file-transfer-binary.txt`, made on TK5 24 November 2026).

- **Measured, 24 November 2026:** every file transfer record of the replay is the same in RetroTerm
  and s3270, both 16384-byte upload blocks, the Read Modified resends, the download blocks and the
  file that comes back. The only records that differ are the two typed IND$FILE commands:
  - PUT: RetroTerm `IND$FILE PUT RTTFB.DATA RECFM(F) LRECL(80) BLKSIZE(3120)`, s3270
    `IND$FILE PUT RTTFB.DATA  RECFM(F) LRECL(80) BLKSIZE(3120)` (two blanks after the name; record 10,
    `... 4B C4 C1 E3 C1 40 D9 ...` against `... 4B C4 C1 E3 C1 40 40 D9 ...`, and the cursor one further).
  - GET: RetroTerm `IND$FILE GET RTTFB.DATA`, s3270 `IND$FILE GET RTTFB.DATA ` (a blank at the end;
    record 23).
  In ASCII mode the two type the same command, `IND$FILE PUT name ASCII CRLF` (`file-transfer` and
  `file-transfer-large` are green). How x3270 builds the command was not read (its source is not on
  this machine); what is measured is only that the extra blank comes where binary mode has no
  `ASCII CRLF` after the name.
- **Which is right:** the IND$FILE syntax of the 1986 FTTERM guide (IND-FILE-TRANSFER.md section 2)
  separates the operands by blanks and TSO takes both forms; TK5 accepted RetroTerm's. Nothing in
  RetroTerm is wrong, so there is nothing to fix here, and the comparison is not loosened for it.
- **What would make it green:** an s3270 that does not add the blank, or a decision by Ronny to drop
  this case from the oracle (it is the only binary transfer compared offline; the live round trip in
  `HostStreamRecorderTests` checks the binary file comes back byte for byte).
