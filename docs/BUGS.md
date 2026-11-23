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

## B9 - 3270 Attn at TSO's READY: the screen after the next Enter differs from s3270

`src\tests\RetroTerm.Tests\Hercules\Tk5AttnTests.cs` (category Hercules, needs TK5) logs in,
goes to READY, presses Clear, Attn, then Enter, in s3270 and in RetroTerm, and compares every
screen. Found 23 November 2026. It is red.

What is known:

- RetroTerm sends what x3270 sends for Attn on plain TN3270: PA1 alone, then IAC BREAK
  (x3270 `Common\kybd.c` Attn_action lines 994-998, `Common\telnet.c` net_break; RetroTerm
  `Ibm3270Emulator` ATTN, `Tn3270Protocol.SendBreak`).
- RetroTerm's records after Attn: sent `6C` (PA1), no host record, sent `7D 40 40` (Enter),
  then host `F1 C1 11 5D 7F 1D 40 1D C8 4F 1D 40 11 C1 50 13` (a Write: an intensified field
  holding `4F`, shown as a vertical bar, keyboard NOT restored) and host
  `F1 C3 11 C1 50 1D C8 D9 C5 C1 C4 E8 ...` (READY on row 2, keyboard restored).
- RetroTerm's screen at the end of the step: row 1 a bar, row 2 empty, line 25 X SYSTEM.
  s3270's: row 1 empty, row 2 READY. The Clear after it differs too (RetroTerm shows
  INVALID COMMAND NAME SYNTAX and READY).
- One code difference, not shown to be the cause: x3270 sends Attn's PA1 directly
  (`ctlr_read_modified(AID_PA1, false)`), without the keyboard lock its attention keys set;
  RetroTerm's Attn locks the keyboard like any attention key.

What is not known: what s3270 received in the same run. No byte trace of the s3270 side was
taken, so it is not settled whether the host answered the two terminals differently, or answered
the same and the screens were read at different moments. Next step: run s3270 with its trace
(`-trace -tracefile`) in the same test and compare the records byte by byte.
