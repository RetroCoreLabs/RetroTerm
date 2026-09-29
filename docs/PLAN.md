# RetroTerm — what is left to do

**Full path:** `docs\PLAN.md`
**Updated:** 11 September 2026
**Next:** parked on 11 September 2026 at Ronny's call - "these are not very important right now".
When it resumes, the TDV thread's next step is the GRM rendered test (Phase 4), and everything
else needs Ronny or a machine. The two code defects found in the September document audit are in
`BUGS.md` at the repo root.

**What this program is, in one line:** a Windows desktop terminal emulator (C#, Avalonia) that
speaks VT100/VT220/xterm, the DEC VT240/VT340 graphics terminals with Sixel and ReGIS, Tektronix
4010/4014 vectors, and the Norwegian Tandberg TDV1200/2115/2200/2215 used with Norsk Data ND-100
and ND-500 machines. It connects over telnet, SSH and real serial ports, and can be driven by hand
or through its own MCP server.

**Read these before touching anything, in this order:**

1. `CLAUDE.md` in the repo root — the rules, the build and test commands, where things live, how to
   drive a real machine over the `retroterm` MCP tools, and the traps that have each cost real
   time. It loads automatically, but read it rather than skim it: nearly every line is there
   because somebody broke the thing it warns about.
2. This file — what is outstanding, in priority order. The standing instructions about what NOT
   to touch are in Phase 3 (the virtual keyboard and its VK codes) and under "Standing judgement
   calls".

**This is the living plan, and the only one.** It holds outstanding work ONLY. Finished work is not
listed here — it is in the git history, and the evidence is in the M1–M8 documents beside
`docs\manual-tests\INDEX.md`. The order and who-does-what for by-hand work is in
`docs\manual-tests\RUN-SHEET.md`.

**State:** full `dotnet test src\RetroTerm.slnx` - 6913 passing, plus 123 in the Kermit project.
Published `1.10.26.9`. One branch only: `main`, on GitHub under RetroCoreLabs since 28 September
2026.

---

## How this is ordered

**By what it would cost to be wrong, divided by what it costs to find out.** Not by how interesting
the work is, and not by how nearly finished something looks.

Two things earned their place at the top by being cheap to settle and expensive to leave:

- **A tool that lies costs more than a feature that is missing.** The `WireScanner` item was first
  for that reason, and it is now closed: a misleading trace had sent a whole session after a
  keyboard fault that did not exist.
- **A decision Ronny can make in two minutes unblocks work that would otherwise sit for months.**
  The Sixel scroll-and-clear question was the standing example, answered on 9 September and built
  the same day. The rule stands for the next one.

**One rule decides whether an item belongs in a phase at all:** can it be settled? An item nobody
can settle yet is in Phase 4, not at the top pretending to be work.

---

## Gateway Ethernet — code lands, the UI needs a real look

The gateway now carries the emulated machine's Ethernet (frame types `0x30`/`0x31`/`0x32`),
alongside the terminals and disks it already carried. Clean-room from RetroCore's
`Emulated.HW\Common\Network` as the specification; no shared code, no project reference.

It is configured in the APPLICATION preferences — Preferences → Gateway — not in the
per-connection dialog, because which adapter or segment this machine shares is a property of
the installation and not of any one saved host. That tab also carries the enable/disable for the
WebSocket gateway itself and its listen port, so the whole gateway is set up in one place.

Mappings: not mapped, a real host adapter (pcap), join a segment (TCP/RETH), host a segment
(TCP/RETH hub), or a multicast group (UDP). The spec grammar matches RetroCore's, so a spec is
portable between the two products.

Interop is proven, not assumed: RetroTerm's `RethTcpBackend` dialled a running nd100x
`gateway.js` ethernet segment, its handshake was accepted, and the 60-byte frame it sent arrived
byte-for-byte at an independent RETH client — `ffffffffffff 025254000001 88b5`.

- [ ] **Look at the Preferences → Gateway tab.** Built blind — the two bordered sections, the
      combo width, the hint line and the target box have not been seen rendered. Needs a
      screenshot at the real window size.
- [ ] **pcap has never been run.** SharpPcap 6.3.1 is referenced and the code compiles, but no
      frame has crossed a real adapter: it needs npcap and admin rights on Windows, neither of
      which is available from WSL. First run is Ronny's.
- [ ] **UDP multicast has never been run.** Same reason — needs two hosts, or two instances, on
      one LAN.
- [ ] **HDLC (`0x10`-`0x12`) is still not implemented** in this gateway. Out of scope for the
      Ethernet work; noted so it is not mistaken for finished.

## Phase 1 — CLOSED, 2 September 2026

**The `WireScanner` stale-ESC defect is FIXED.** A lone ESC left pending by one send was welding
itself to the next keystroke in the DECODED trace, so a RIGHT arrow rendered as
`TX 1B 18  ESC.  Unrecognised sequence` while the raw block read `TX 18  BLOCK  1 bytes`.

**The cause was not the one this plan guessed.** The note here said to check whether the
one-second abandon rule was applied on the TX side. It is, and it works - a test with an
eighteen-second gap abandons the escape correctly. The real hole is underneath the timeout
entirely: the two keystrokes were a fraction of a second apart, well inside the second it waits.

**The fix is structural rather than another threshold.**
`TerminalSession.SendBytesAsync` is "THE single TX feed point", and it feeds one whole send per
call - a keypress maps to a COMPLETE byte string and an emulator reply is written whole. So on TX
a sequence still pending when a feed ends can never be completed by the next one, whatever the
gap, and it is now flushed at the end of every TX feed. RX keeps the carry-over and the timeout,
because a host's sequence genuinely does arrive split across two network reads - which is the case
the original comment rightly said must not break.

**Two existing tests had to move, and they were mis-specified rather than wrong.**
`AnEscapeFinishedPromptlyIsUntouched` and `ASequenceSplitAcrossTwoNetworkReadsIsStillOneSequence`
both describe HOST traffic - DECKPAM, and a CUP split across two packets - while being written
against a Tx scanner. They now use an Rx one and say why.

Pinned by `WireScannerGluedKeystrokeTests`, proven red-before-green with the right split: breaking
the fix fails only the glued-keystroke case and leaves both RX split-packet tests green.

## Phase 2 — Needs Ronny, no machine. Short sittings

**Cost: about 20 minutes of looking. Needs: him.**

**The headless font-manager failures are FIXED, 10 September 2026.** Kept for the lesson.

Roughly one full run in three lost the WHOLE headless UI collection at once - 268 to 340 tests -
on one exception:

```
KeyNotFoundException: The given key 'fonts:SystemFonts' was not present in the dictionary
  at Avalonia.Media.FontManager.TryGetGlyphTypeface(...)
```

**The cause was two test classes added that day which opened about 26 windows between them and
closed NONE.** Window teardown is what releases the render and font resources a headless window
holds. They close them now, and closing them turned up a real defect in the app besides:
`TerminalPopoutWindow.TeardownAsync` dereferenced its tab unconditionally, and the constructor the
XAML loader needs leaves that field null, so closing such a window threw - on a task continuation,
because `OnClosing` is `async void`.

| State | Full runs | Red |
|---|---|---|
| Windows left open | 15 | 3 |
| Those two classes filtered out | 5 | 0 |
| Windows CLOSED, everything enabled | 10 | 0 |

Ten clean runs against a one-in-three rate is under two percent by luck.

**The lesson, and it cost most of a day: two wrong causes were pursued before the exception text
was ever read.** Thread starvation was blamed, and the socket classes were serialised into the UI
collection for nothing. Then 39 good integration tests were switched off purely because the flake
appeared the day they came back - and it failed with them off. Capturing the actual exception took
one run of a loop that kept the output; the single-variable test that found the real cause took
five. Neither was tried until three ticks in.

The runner cap at four parallel collections in `tests\RetroTerm.Tests\xunit.runner.json` stays. It costs
nothing measurable. It was never proven to help on its own.

**The 37 switched-off integration tests are CLOSED, 10 September 2026.** All of them run now.
The suite went from 41 skipped to 2: one genuinely needs a TestServer running by hand, and one
is the TDV mode-report question below.

Kept for the rule it produced, which is the same one the OPCOM work produced: **a note that
nobody re-checks is worth less than no note.** Every one of the 37 blamed timing, a race or a
TCP timeout. Not one of them was any of those things. What they actually were:

- A `Dispose` blocking the Avalonia dispatcher with `GetAwaiter().GetResult()`, which HUNG
  rather than failed and hid thirteen working tests behind it.
- Two readers on a channel built `SingleReader = true`, where the loser's timeout cancelled
  the winner's read and ended the session.
- A mock stream overriding `WriteAsync(byte[], int, int, ct)` while the caller used
  `Stream.WriteAsync(bytes)`, which binds to the `ReadOnlyMemory` overload - so every byte
  the server wrote went out of a dummy socket.
- `DisableReceiveLoop` documented as required for server-side connections and called by
  nobody, so a receive loop ate everything the client sent.
- Routines ending in `WaitForEnterAsync` with nobody to press Enter.
- A test calling `GetAllSentData` twice, which DEQUEUES, so it emptied its own evidence and
  then failed for finding none.

- [ ] **M6.5, a blank screen during graphics input — RETRY on a clean build.**
      Seen 29 August on Ronny's screen, all four rounds of TEXT present and no drawing at all.
      Three explanations were tried and **two were wrong and withdrawn** — the composite does ask
      for a repaint on all three paths, and `RegisGraphicsInputRepaintTests` pins that.
      What was left standing is that the binary he was running **could not be published at all**
      that day, so it was a day-old build from a dirty tree. **That is the likeliest cause and it
      is NOT proven.** The companion report from the same day — a monitor move leaving content at
      the old size — was retried on a clean build on 31 August and did NOT reproduce, which raises
      the odds here.
      **Cheap now and getting cheaper:** `1.0.26.2-4` is clean and published. If it survives that,
      it is a real rendering defect and the first reproduction with a trace beside it.

---

## OPCOM against the real machine — CLOSED, 6 September 2026

Kept only for the rule it produced, which applies well beyond OPCOM.

OPCOM debug worked against the built-in simulator and failed against the real ND-120/CX on COM11.
Four differences were measured on the wire and fixed, and the simulator was corrected to answer
the way the machine does. `docs\OPCOM-COMMAND-REFERENCE.md` now carries the measured bytes.

**The rule: a fake replies only when spoken to and is never busy.** The worst of the four
defects was invisible to every unit test for exactly that reason — a register dump keeps
printing after its last value, and a command sent during that tail is DROPPED BY THE MACHINE
with no error. Anything that talks to real hardware needs a test that talks to real hardware.
`OpcomAgainstRealHardwareTests` does, and it is off unless `RETROTERM_OPCOM_PORT` names a port.

**The command sweep is FINISHED, 9 September 2026.** IOX read, IOX write and the memory test
were all measured on the ND-120/CX over COM11 and are in `2fb06d8`. The IOX write turned out to be
three steps on the wire rather than one, and the old code stopped after the first, so nothing ever
reached the device.

**The memory test is measured but is NOT in the hardware suite, on purpose.** It writes memory -
bank 0 came back with address 000000 holding 177777 where it had held 114631, and that word was put
back - and it answers with a bare prompt, so the reply says nothing about whether the bank passed.
Running it again would destroy memory to learn nothing new. The recorded bytes are pinned in
`OpcomProtocolRealMachineReplayTests`.

- [ ] **Run the OPCOM hardware tests whenever the D100 is on the bench anyway.**
      `$env:RETROTERM_OPCOM_PORT = "COM11"` then
      `dotnet test tests\RetroTerm.Tests\RetroTerm.Tests.csproj --filter "FullyQualifiedName~OpcomAgainstRealHardwareTests"`.
      Costs 30 seconds, needs the machine at the OPCOM prompt and RetroTerm closed.
      They cross-check a block dump against single examines rather than hardcoding values, so
      they stay valid whatever the machine is doing.

---

## Phase 3 — Needs the D100. About an hour, in a sitting together

**Cost: an hour. Needs: him at the keyboard for 3b, and the machine for both.**
Setup is known to work and takes under a minute. **80 x 25 — 25 rows, not 24.**

```
open localhost:9010 as TDV2200, 80 x 25
TRACESTART, LOGSTART
SENDRAW ESC
SEND "SYSTEM\r\r"                    <- one send; splitting it bounces to a fresh prompt
SEND "set-term-type,,93\r"
```

The D100 takes several telnet lines at once, so this can run alongside anything else on 9010.

- [ ] **M4.x — the rest of the TDV key sheet.**
      Already PASS and off the list: the four arrows, F1, and HOME, in BOTH extended and 2115
      compatibility mode. What is left is the other keys on `tdv2200-key-sheet.md`, which is
      generated from `TDV2200KeyRegistry` every test run so it cannot drift from the code.
      **Lower value than it was**, because `EveryReachableKeyAgreesWithTheRegistryTests` now walks
      every reachable key through the real mapper in both modes — so mapper-versus-registry drift
      is already guarded. What the live machine adds is only what SINTRAN DOES with each byte, and
      at the `@` prompt it mostly does nothing.
      **So: worth doing inside a session that is running anyway, not worth booking one for.**
      **Do NOT touch the virtual keyboard or its VK codes** — Ronny's call, 2 September: it is
      right as it stands.

- [ ] **s3-config graphics — he types, I only watch.**
      The only ND program known to draw. Whatever it sends is the ND graphics work list, which is
      the largest remaining unknown in the ND direction.
      **His words: "s3-config has graphics and is for configuring the system, so be careful."**
      So the split is the opposite of everywhere else: every keystroke is his, I send nothing, I
      watch the trace and read `UNHANDLED` after each screen, and nothing is saved or applied.
      **Blocked on one answer first:** is there a screen in it that draws and can be reached
      without changing anything? If no, skip it and say so — an unrun case beats a reconfigured
      machine.

---

## Phase 4 — Cannot be settled yet

**Not work.** Each needs an answer, a document or a machine before anything should be built.
Nothing here should be started to look busy.

- [ ] **ReGIS grid labels sit four half-cells too high, and the obvious fix is REFUTED.**
      Measured 28 August: the grid line is on row 100 in both halves, our `100` label occupies rows
      62-74 and the hardware's 104-114. Cause is accumulated PV spacing — `T22` twice and `T4`
      earlier in the stream, never cleared. **Resetting PV per text command was tried and
      reverted:** chapter 7's own worked example returns to the baseline with `T2` then `T6`, two
      separate commands, so the value has to persist. Something else zeroes it on hardware, most
      likely the `T(E)` closing each label's temporary text control, but nothing held here says so.
      Needs a manual that answers it, not a guess. Evidence in `M6-SIXEL-AND-REGIS.md`.
      **The most likely of this phase to become work**, because it is a documentation question
      rather than a hardware one.

- [ ] **Colour map register 7.** We keep the manual's 53% grey; hackerb9's photograph measures 46%.
      Six of the seven other visible registers agree within 8, so this is a real disagreement, not
      measurement error. Needs a VT340.

- [ ] **ReGIS glyph shapes are a stand-in.** The TDV2200 character ROM stands in because the manual
      prints six example glyphs and no font. 8 wide is right; 14 rows where ReGIS stores 10 is not.
      Replacing `BuiltInGlyph` is a one-method change **if DEC's own shapes ever turn up**.

- [ ] **The ReGIS user-defined input cursor**, `S(C(I[+5,+10]"XO"))` — two characters from the
      loaded set combined into a 16 by 24 shape. Needs the soft font to become a cursor bitmap, and
      nothing in any corpus asks for it. Its syntax is stepped over rather than misread, which has
      a test.

- [ ] **NDVIDEO's final byte.** ND-1200 section 5.59 gives it as hex 7F and the ASCII form as
      `CSI n/p`; this program uses `<`, hex 3C. 7F is DEL and is outside the ECMA-48 final range
      40-7E, so the manual entry is either an OCR artefact or describes something this parser
      cannot represent. Found during the 11 September mode alignment and deliberately NOT changed -
      see section 6.5 of `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`. Needs a cleaner scan of
      ND-12054 or a real terminal.

- [ ] **GRM's semantics - START WITH THE RENDERED TEST.** Mode 62 itself is built and walks its
      three positions; nothing acts on the position yet. On a real 2215 (section 4.2.3): ATTR makes
      the terminal ignore SGR entirely and turns `SO Y SI` into an invisible attribute cell that
      occupies a screen position and governs everything up to the next one; UNDERLINE turns SO and
      SI into underline on and off; and changing the switch erases the screen.
      Per his instruction of 11 September, this does NOT come back as a question. The next step is
      a headless rendered test that captures what ATTR operation should put on screen - the
      attribute cell invisible, the run after it rendered in that attribute - beside what this
      program does today, and the PNGs decide. Three tests in
      `TdvGraphicRenditionModeTests` already pin today's behaviour and are meant to fail when it
      changes. The one thing a test cannot settle is the power-up position: the 2215 ships on ATTR
      and this program starts on SGR because SGR is what it does.

- [ ] **61 CLL, the lamp-clear policy.** BOTH, KEY or SYN - 2215 section 8.7.1, three positions,
      and the message lamps now exist for it to act on. Smaller than GRM and needs no decision,
      but it does need reading what each position means in section 4.2.

- [ ] **PAGE mode does not change what the screen does.** ND-1200 ND private mode 3 and 2215 ANSI
      mode 47 are held and reported as of 11 September 2026, so a host can set roll/page and read
      it back, but the screen still rolls either way. What PAGE operation does when the cursor
      leaves the last line - and what a terminal does with the rest of the page - is a behaviour
      change worth its own piece of work, with a manual open. The same is true of the key click
      (no sound is made) and the two label modes (the virtual keyboard draws its labels
      regardless, and that window is a standing DO-NOT).

- [ ] **NDSS1 to NDSS9 are not implemented, and the blocker is WHICH SET each one means.**
      ND-1200 sections 2.3 and 5.54 give nine single shifts to the alternate character sets -
      `ESC 1` to `ESC 6`, then `ESC 9`, `ESC :` and `ESC ;` (the gap is because `ESC 7` and
      `ESC 8` are NDSC and NDRC, save and restore cursor). Each takes the NEXT character from
      that set.
      **Looked at properly on 11 September 2026 and deliberately NOT built.** The single-shift
      mechanism is easy - the 2215's `_pendingSingleShift` and `TDVCharacterSetManager` already do
      it for SS2 and SS3. What is missing is the mapping: the manual names nine alternate sets and
      `FontTDV2200`'s ROM holds four (`fontNumOffset` has entries 0 to 4), so sets 5 to 9 would
      paint blanks, and nothing says our set 1 is the manual's set 1. Picking a mapping is the
      same kind of guess this whole thread exists to undo.
      Nothing is lost meanwhile: an unknown `ESC <digit>` already lands in
      `CountUnrecognisedSequence`, so a host using one shows up in UNHANDLED rather than vanishing.

- [ ] **The TDV 2200 selects its character sets with ESC-LETTER lead-ins, and this program uses
      `ESC ( n`.** TDV 2200/9 S User's Guide section 10.2, "the lead-in sequence needed to activate
      the required set": `ESC A` set 2 (Greek, mathematical, graphic symbols), `ESC 0` set 3
      (subscripts and superscripts), `ESC B` set 5 (accents), `ESC C` set 6 (NORTEXT), and on
      through `ESC L` for the fifteen sets. `TDVEmulatorBase` implements `ESC ( 0-9` and
      `ESC ) 0-9` instead, and `ESC A` currently falls through to "unrecognised".
      **The manual contradicts itself, which is why this is not built.** Section 10.1's own table
      reads `ESC A Set 0`, `ESC B Set 1`, `ESC C Set 2`, `ESC D Set 3` - zero-based and shifted
      against 10.2 - and 10.2 lists `ESC A` TWICE, for set 2 and again for set 4. The scan cannot
      settle which letter selects which set. Needs a cleaner scan of section 10, or a real 2200.
      There is also a documented clash to respect when it is built: 2215 section 7.9.1 says that
      with Extended Control OFF, `ESC 0` turns EC back ON, so `ESC 0` means one thing in 2115
      operation and another in extended operation.

- [ ] **Where the TDV character-set NUMBERS come from is not established.** The emulator maps
      `ESC ( 0` to `ESC ( 9` onto `TDVCharacterSetType` - US ASCII, Graphics I, Graphics II, Math,
      Greek, Norwegian, Swedish, Danish, Finnish, German - and the TestServer's menu prints those
      names. ND-1200 section 2.2 does list `ESC (` and `ESC )` as "Designate G0/G1 character set",
      but its table leaves the final character unspecified, and the 2215 (section 7.2) has only
      FOUR sets: standard, semigraphic, subscript/superscript, and control-code display. The
      national names look like the same invention that put Danish and Finnish into the ISO 646
      variant list. Settling it needs a manual page nobody has found yet, or a real terminal.

- [ ] **`TDVProtectedAreas` is dead state.** The class keeps a whole per-cell protected map and
      nothing ever writes to it - `SetProtectedArea` and `SetProtectedRectangle` have no callers,
      so `IsProtectedArea` is always false. What the program really implements is DECSCA, which
      writes `CharacterAttributes.Protected` on the cell. Found 11 September 2026 when the last
      reader of the map, `HandleProtectedAreaQuery`, turned out to be dead too and was deleted.
      Deciding which one is the real model is the work; the ND-1200 control list has neither SPA
      nor EPA nor DECSCA, so a manual will not settle it.

- [ ] **TestServerApp state is per-process, not per-session.** One instance serves every client, so
      two clients connected AT THE SAME TIME share one menu level and one detected terminal type.
      Resetting the fields on connect (11 September) fixes the sequential case, which is the one
      that bit. The real fix is moving the fields onto an object hung off the session, and nothing
      here drives two clients at once today.

- [ ] **The two ND model identification bytes.** Needs a machine that may never appear.

- [ ] **VT52.** No manual, no corpus entry. Its identity is checked (`ESC Z`) and everything else is
      inferred — the weakest row in the validation table, and written up as such.

- [ ] **IBM 3270.** No manual and no data-stream reference is held here. Building one from
      recollection would invent EBCDIC orders, AID codes and structured fields.

- [ ] **ND graphics mode 8 reads its rectangle-fill coordinates as logical space - an assumption.**
      `src\RetroTerm.Core\Terminal\Graphics\NorskDataGraphicsModule.cs` lines 242 to 249 flag it:
      the spec names "4 coords" without saying which space. By-hand case M7.2a has no result.
      Needs one `ESC "8;...h` from a real ND host and a look at where the rectangle lands.

- [ ] **Mode 24 draws the circle when it is defined, and mode 30 "execute draw" is taken to serve
      polygons only - both assumptions.** Same file, lines 271 to 296. By-hand case M7.2b has no
      result. Needs a real host drawing a circle; if it turns out to need mode 30, the fix is one
      line - store the circle and let 30 draw it.

- [ ] **Which Tektronix coordinate bytes a host may leave out came from the 4010/4014 manuals, not
      from the ND document.** `src\RetroTerm.Core\Terminal\Graphics\TektronixVectorDecoder.cs`
      lines 51 to 53 say so. gnuplot's output agrees with the rule; no hardware has confirmed it.

- [ ] **DECDLD 94-versus-96 character set offset: the source and a status document disagree.**
      `SystemFontRenderer.cs` lines 144 to 146 say the set-size parameter is read and not acted on,
      so a 94-character set would draw one place out. `docs\EMULATOR-VALIDATION-STATUS-2026-08-11.md`
      (the Pcss paragraph near line 135) says that was fixed on 17 August. One of the two is stale.
      Read `SoftFont` and the renderer and correct whichever is wrong.

- [ ] **Two test-only properties on `TDV2200Emulator`.** `ResetWasCalled` and
      `OnEscapeDispatchInvoked`, `TDV2200Emulator.cs` lines 57 to 63, exist for tests alone.
      `Tdv2200TektronixRenderingTests` lines 191 to 211 assert on `ResetWasCalled`, so taking them
      out needs a test change first. Small and self-contained.

- [ ] **`SessionPreflightTests` was recommended on 26 August and never built.** One test per
      by-hand session, checking before Ronny is asked anything that every picture the session shows
      him exists and carries ink, that every case names its terminal type, machine and build, and
      that the published binary is newer than the last commit touching `src\`. Ronny decides
      whether wanted.

- [ ] **Two MCP roadmap items never built:** an attribute-aware screen read (colours and protected
      fields, not just text) and a headless console MCP host reusing the Core code without the
      desktop window. Ronny decides whether wanted.

- [ ] **Six of 42 bytes came back as `?` on the nexys serial link, clustered.** A 7E1 against 8N1
      framing mismatch would fail parity on nearly every odd-population character and shred the
      whole line, so this is unexplained. Needs COM11 free and the nexys board.

- [ ] **Transmit pacing is wired for serial only.** The msec-per-character and msec-per-line
      pacing is set on serial connections in `ConnectionFactory.cs` (near line 611); telnet has
      none. The gap is general, not a serial one.

- [ ] **Three SINTRAN protocol bytes are UNSOURCED.** A `0x21 0x13` packet marker, TAD protocol
      0xDD and routing protocol 0xDE were written in the old OPEN-QUESTIONS.md, and PAD 0xDA in the
      old TODO-PLAN.md, with no manual, capture or source behind any of them. Carry them as
      "find the capture or drop", never as facts.

- [ ] **STX, ETX and EOT outside 2115 mode.** 2215 section 8.4 (`spec\TDV2215\TDV2215.md` lines
      2367 to 2396) lists them in the general accepted C0 set - video off, video on, erase line -
      with no Extended Control qualifier, while the test
      `TDV2200_NormalMode_LeavesTheGenuinelyTwoOneOneFiveOnlyCodesAlone` keeps EOT off in normal
      mode. Decide with the manual open, and change the test with the citation in it.

- [ ] **TDV printing from the keyboard.** The 2200 guide
      (`spec\TDV2200\OCR\TDV-2200_9-User-s_Guide-ND_combined.md` lines 805 to 809) gives
      CTRL+STOP PRINT on G51 and CTRL+START PRINT on G52 - a screen print to a local printer.
      `TDV2200KeyRegistry.cs` gives G52 `ESC[44_` and G51 `ESC[42_` and has no CTRL column for
      either. Read 2215 sections 4.2.14, 7.4 and 8.7 before building anything; DEC media copy
      (`CSI 4 i` / `CSI 5 i`) is already in the base emulator. Two more pieces of the same job,
      from Ronny's notes of February 2026: the TDV LOG mode (a running transcript, each line
      printed as the cursor leaves it, finished on disconnect or stop), and the PFF switch that
      places the form feed (none, before, after, both). The real terminal sent only 0x20 to 0x7E
      to the printer and double-width lines at double spacing.

- [ ] **Control-mode keys on the virtual keyboard.** Ronny's notes name BREAK on Ctrl+SLUTT
      (a real line break, which needs a `SendBreak` on the connection layer; nothing in `src`
      has one today), CLEAR on Ctrl+F4 (reset the L1 to L4, WAIT, CAR and ERROR lamps), SI on
      Ctrl+F2 (0x0F) and SO on Ctrl+F3 (0x0E), and Ctrl+End mapped to Ctrl+SLUTT. The registry
      disagrees on F2 and F3: it gives Ctrl+F52 `ESC[54_` and Ctrl+F53 `ESC[57_` (lines 350 to
      353). Settle it with the 2200 guide section 8 open before changing either.

- [ ] **Host programming of PUSH keys over DCS is built on no source.** The user side is done:
      the Configure PUSH Keys window, `push-keys.json`, the 12/32/48 capacities, and a click on
      the virtual keyboard sends the stored string. But `TDVDCSHandlerFeature` (line 90) accepts a
      payload that starts with the word `PUSH` followed by a digit, a shape no manual gives.
      Ronny's notes say the host form is `ESC P P nn <hex pairs> ESC \` with nn = 01 to 16 and
      forbidden bytes 0x00, 0xA0 to 0xAF and 0xF8 to 0xFF; the 2200 guide line 822 names a hex
      lead-in for PUSH programming. Find the format in the 2215 spec or the guide, cite it, fix
      the parser, test red before green. Needs nobody.

- [ ] **Two of Ronny's ideas, not decided:** import and export of a PUSH key set as JSON, and a
      window to type a series of hex bytes and send them to the host for testing (the MCP
      `terminal_sendraw` does this from a script, the UI has nothing). Ronny decides whether
      either is wanted.

- [ ] **A pixel test for the ReGIS glyph fix.** On 17 August the built-in ReGIS letters were
      quantised to ten rows before scaling, so `e` drew as `c` and `i` lost its dot; the fix samples
      the ROM at draw height (`docs\REGIS-GAP-2026-08-17.md` lines 154 to 161, M6.4 in
      `docs\manual-tests\M6-SIXEL-AND-REGIS.md`). No test pins it: `RegisTextAndLoadTests` says
      "nothing here asserts what a letter looks like", and `M6SixelAndRegisTests` only counts lit
      pixels. Write one that asserts an `e` at S1 or above has its middle bar. Needs nobody.

- [ ] **Two leftovers from the 8 August architecture review, not re-verified since.**
      `TerminalCanvas` tears the old renderer down on every `SetEmulator` (line 512 onwards) but
      has no end-of-life cleanup of its own: no `OnDetachedFromVisualTree`, so the last renderer
      and its blink timer live until the process ends. And the review's appendix 0q says
      `FontTDV2200` has no guard for codepoints above 0x7F; `GetFontBits` at line 18436 still
      hands anything outside the national remap straight to the base lookup. Check both against
      the source before touching either. Needs nobody.

---

## Known unimplemented features — swept out of the code, 11 September 2026

Found by searching the source for what it declares about itself rather than by remembering:
`NotImplementedException`, `TODO`, and stubs that return nothing. **Every one below was then
checked for whether its code path is actually reachable**, because a TODO in dead code is not a
missing feature - and four of the candidates turned out to be exactly that.

None of these is urgent and none is in anyone's way. They are written down so the next person does
not have to do the search again, and so a stub cannot be mistaken for a feature.

- [ ] **Private mode 12, start/stop blinking cursor, is ignored.** `CSI ? 12 h` and `CSI ? 12 l`
      reach `HandlePrivateMode` in `TerminalEmulatorBase` and hit an empty case. The cursor DOES
      blink - the renderer owns a timer for it - but a host has no way to turn that off or on, and
      the request is swallowed without being counted as unhandled either. Small, self-contained,
      and testable without a machine.

- [ ] **TDV graphics extension operations do nothing, and the test says otherwise.**
      `ESC [ 1 >` enables the extension, and all three `HandleGraphicsExtensionOperation` overloads
      on `TDV2200Emulator` are empty bodies with a TODO.
      **Worse than an empty feature: the only test covering it asserts NOTHING.**
      `TDV2200EmulatorTests.HandleGraphicsExtensionOperation_ShouldProcessCorrectly` calls the
      method and then says, in its own body, "TODO: Verify that the graphics extension operation was
      processed correctly". It is green and it proves nothing, while its NAME claims the opposite -
      the same shape as the M4.2c test that cost real time. The hollow-test sweep in `be6eec4`
      missed this one.
      **Do the test first**, per the standing call about UI questions: give it something real to
      check, watch it go red, and only then decide whether the feature is worth building.

- [ ] **TDV1200 alpha and graphics mode switching are empty cases.**
      `HandleVideoToggle` is reached from the TDV1200 escape dispatch, and both arms - mode 0 alpha,
      mode 1 graphics - are `// TODO` and fall through doing nothing. Reachable, so a TDV1200 host
      switching modes is silently ignored.

- [ ] **Three-character hash sequences return false.**
      `TDVThreeCharacterSequenceFeature.HandleThreeCharacterHashSequence` is a stub. `ESC #` on its
      own is handled elsewhere - `TerminalEmulatorBase.HandleLineSize` owns DECDHL/DECDWL/DECSWL -
      so what this covers is the three-character form only. Least important of the four, and listed
      mainly so it is not rediscovered as a surprise.

**Four things that LOOK like missing features and are not - do not chase these again:**

| Looks like | Actually |
|---|---|
| `TDVConfiguration.ProcessDCS` - "TODO: Implement PUSH key processing" | **Dead file.** `TDVConfiguration` is referenced nowhere outside itself. PUSH-key programming is live in `TDVDCSHandlerFeature` |
| `TDVConfiguration.ProcessPM` - privacy message | Same dead file |
| `TDVCharacterSetsWrapper` - three stubbed setters, one returning 0 | **Dead class**, referenced nowhere. Its own comment says it exists "to provide test compatibility" |
| `TerminalFont` - "TODO: Implement other font styles" | **Dead file**, referenced nowhere outside itself |

**Not chased, and honest about why:** `SystemFont` still generates a placeholder pattern instead of
real glyph bitmaps (`GenerateCharacterBitmapWithSkia`), and reports fixed 8-pixel widths. It IS
constructed by `FontManager`, but `TerminalRenderer` does not call `GetCharacterBitmap` on the
drawing path, so whether any of it is reachable in the running app was not established. Worth ten
minutes before anybody treats it as either a defect or dead code.

## The unjudged by-hand cases — TRIAGED 2 September 2026

The M1–M8 documents hold **63 cases with a blank Result line**. That number sat in this plan as a
lump, which made it look either alarming or ignorable depending on the reader. Walking it turns it
into three piles:

| Pile | Count | What it means |
|---|---|---|
| Claims machine cover, and the named test EXISTS | 43 | Not a to-do. See the caveat |
| No cover, needs only an eye | **17** | The real backlog |
| No cover, needs hardware | 3 | M4.2a, M4.4, M7.3 — blocked like Phase 4 |

**Every one of the 43 claimed covers names a test that actually exists** — checked by pulling the
backticked identifiers out of each `Machine cover:` line and looking for each across the whole
suite. Nothing cites a test that was renamed or deleted. Where a case has no cover it usually SAYS
so: M8.1d's reads "none. Nothing here drives a real full-screen program."

**The caveat, stated plainly: "the named test exists" is not "the case is covered."** This repo has
already found three green tests that were testing nothing, and an assertion written from the
implementation is a defect with a guard on it. The 43 are not proven; they are merely not obviously
broken. Spot-checking a few against what their case actually asks is worth more than judging any
single case by eye.

**And the first version of this triage was itself wrong**, which is the same lesson again: its
pattern only matched backticked names without a dot, so every case whose cover named a
`Class.Method` was counted as having none. That put the backlog at 21 instead of 17 and would have
sent Ronny to look at three cases that are already covered. A tool that measures is worth no more
than the check that it measured the right thing.

### The 17 that need only an eye, grouped by what they need first

- [ ] **Seven I can drive against a host, so his part is looking rather than typing** — M2.4 tmux
      and htop, M2.5 less/git/ls, M3.1b what a real host thinks we are, M6.3 a real image from a
      real host, M6.4b ReGIS from a host, M1.3 and M3.4 vttest. The `ubuntu18` and `ubuntu18-xterm`
      connections are saved; he types the password.
- [ ] **Four that need the TestServer**, which I can start myself — M1.1 the standard tests on VT100
      then ANSI, M2.0 the TestServer first, M3.2 the DEC branch, M4.1 the TDV menu per model.
- [ ] **Four about the app itself, no host at all** — M8.1d resize while a full-screen program is
      drawing, M8.7d dragging the view when it does not fit, M8.8d graphics are dropped and it says
      so, M5.1b the four character sizes.
- [ ] **Two with no obvious route** — M5.2 a real plotting program, M3.5 conformance levels.

**Why this is worth doing before Phase 3:** the app-only four need nothing but the published build
and a few minutes. The seven host-driven ones can be prepared in advance — driven, captured, and
handed over as pictures to judge, the way M6.1a was, which is the difference between an hour of his
time and five minutes of it.

## Standing judgement calls

Not tasks. Recorded so that changing one is a deliberate act rather than a drift. Three of them
were made in prose on 11 September and sit here as paragraphs; the rest are in the table below.

**The message lamps are four, not three - Ronny, 11 September 2026.** ND-1200 section 5.52 gives
EXPAND, APPEND, BUSY and MESSAGE, and NDSLED/NDBLED/NDCLED are three operations on them. He asked
"I assume #1 is following spec? if yes, then do it", and it does. Built the same day, including the
virtual keyboard's four lamps - the one narrow exception to the standing DO-NOT on that window,
asked for and granted in the same answer.

**`ESC %` for the national version stays - Ronny, 11 September 2026.** He asked back: "i thought
changing the keyboard language would change the national version?" It does, and that settled it.
`MainWindow.OnVirtualKeyboardLayoutChanged` calls `ApplyLanguageVariant`, which sets
`TDVEmulatorBase.CharacterSetVariant`, so the dropdown is the normal route and always was. `ESC %`
is an extra host-side route on top of it. It has no source in any manual, and ISO 2022 reads
`ESC % G` as "switch to UTF-8", so the doubt is written into the builder and onto the TestServer's
screen rather than into a behaviour change.


**A UI question means write the UI test first - Ronny, 11 September 2026.** Asked how far to take
the graphic-rendition mode, he answered: *"for all ui you think you need me, start with ui unit test
first."* So anything that shows on screen gets a headless test and a rendered PNG before it gets a
question. Roughly 700 `[AvaloniaFact]` tests already capture real pixels. What still needs his eye
is only what no test can reach - whether a sound is audible, whether motion looks smooth, whether a
colour looks right on his monitor, and real hardware. When the answer is a CHOICE rather than a
fact, he gets two rendered PNGs to pick between, not a paragraph.

| Call | Where it lives |
|---|---|
| Serial parity errors are REPORTED, not hidden — the port keeps its real 7E1 framing and `ParityReplace` is 0 | Decided 31 August 2026, Ronny's call. .NET's `SerialPort` substitutes `?` for any byte failing the OS parity check; `c91f39a` turns that substitution off, so a suspect byte arrives as received and `OnSerialError` still fires. Opening the port 8N1 and masking bit 7 in software was PROPOSED by the RTC session and REJECTED: it makes substitution impossible but also stops us detecting a genuinely corrupt byte, and it would change framing for every serial connection |
| Shift-drag keeps the gesture local while a program tracks the mouse | M2.3, pinned by `MousePointerWiringTests.ShiftKeepsTheGestureForSelection` |
| The five Tektronix dash mask lengths | **JUDGED AND KEPT, 25 Aug 2026.** Ronny confirmed all five by eye on the 1:1 plane sheet and none changed — including the pair I expected to fail, dotted (1 on / 1 off) against short dash (2 on / 2 off), which reads as two clearly different styles. Still a reading of five words rather than a measurement: if a real 4014 turns up, photograph it |
| An SSH resize is not sent to a program already running | Decided 2026-08-19. `SendWindowChangeRequest` exists but on an INTERNAL interface behind `ShellStream`'s private `_channel`. Reaching it means reflecting into another library's internals, which breaks silently on an update. Telnet sends NAWS; SSH records the size so a new connection is right. Written up in M8.1d |
| ReGIS text at the bottom edge is PV spacing, not a clamp | ANSWERED 2026-08-19. `T22` in `registest.sh` is chapter 7's PV spacing: each digit moves half a display cell, so 22 lifts the label a whole cell |
| ReGIS pattern multiplier defaults to 2 | From the manual, twice. Not confirmed against hardware; no fixture can tell the difference |
| `ESC c` on a TDV2200 means the line type only in graph mode | Derived, not quoted. The ND analysis lists the line types and says nothing about the clash with the terminal reset |
| Tektronix character sizes are **not** implemented on the TDV2200 | The ND analysis lists what that terminal keeps from the 4014, and character sizes are not on it |
| A ReGIS plane write recolours a character only when it covered **half the cell or more** | Text is a cell buffer, not part of the bitmap. Below that threshold the one-pixel `V0` separator lines would repaint every letter they cross. Written up in M6.4h |
| Display zoom is measured from the FITTED picture, so 100% is the normal view | Decided 2026-08-19 after Ronny found three faults. Pinned by `DisplayZoomPixelTests` |
| A zoomed picture larger than the window follows the CURSOR, and is never centred | Centring an over-sized picture put the origin off the corner and the screen came up blank. Pinned by `M8WholeTerminalTests.M8_7b_*` |
| Changing the emulation keeps the connection, the screen and the scrollback; graphics are dropped and said so | Decided 2026-08-19. One registry command behind the menu, the DSL and MCP alike |
| ReGIS graphics input takes a CLICK to place the cursor and the arrow keys to refine it | Decided 2026-08-20 as keyboard-only, and OVERTURNED by Ronny on 31 August 2026. The original reason was that the manual describes only the arrow keys and nothing said who wins when a program is also asking for xterm mouse reports - but one-shot input SUSPENDS the host, so no program is listening for mouse reports while the cursor is up and no new text is arriving to select. The click can only round to the nearest plane pixel, because the plane is stretched into the text area, so the arrows keep their job. NOT known: whether a real VT330/VT340 took a mouse or tablet as a ReGIS locator - nothing in `spec\` covers it. Pinned by `RegisGraphicsInputKeyTests` and `RegisGraphicsInputMouseTests` |
| One-shot graphics input suspends the host, and Esc cancels | The suspension is the manual's. The cancel is OURS — a real VT340 has no way out except answering, and a suspended session here would look dead |
| A host must send `R(P(I))` in the SAME command string as `R(I0)` | Not a choice, a consequence: everything after the mode change is buffered, including a later request |
| The multiple-mode position report carries the null button code | The two chapters disagree; the worked example in chapter 15 wins |
| The graphics input crosshair is white, and not from the colour map | Register 7 was the obvious pick and rendered nearly invisible — 53% grey on the default map. Found by opening the PNG; every assertion passed either way |
| The input cursor's style must be chosen BEFORE `R(I0)` | Another consequence of the suspension. Both directions have a test |
| A ReGIS screen erase does NOT move the drawing point | Chapter 4's own list. The decoder homed it to 0,0 and a test asserted that, written from the code rather than the manual. Corrected 2026-08-20 |
| Smooth scroll runs the PICTURE behind the buffer and catches up, rather than dropping lines | Decided 2026-08-25, Ronny's call. Limits: never more than a screen behind, never further back than there is history. The alternate screen keeps nothing, so it gets one line — the old approximation. Pinned by `SmoothScrollQueueTests` |
| The backarrow key sends BS at power-on, not DEL | A deliberate deviation from a real VT220, so that changing it is one decision instead of two. DECBKM still works |
| Private mode 46 is DEC's DECGPBM, not xterm's XTLOGGING | Decided 25 August 2026. The spec lists both on consecutive lines. xterm's logging is "normally disabled by a compile-time option" and this program has no logging feature to collide with, so nothing is lost, and the DEC reading sits beside 43 and 44 which are already DEC's. Pinned by `GraphicPrintBackgroundModeTests` |
| Private mode 45 is xterm's XTREVWRAP, not DEC's DECGPCS | Reverse wraparound is used by live software every day; the graphic print colour syntax by almost nothing. The mirror of the 46 decision, decided on the same ground: which one costs more to get wrong |
| NDRQ report type 1 claims emulator level 93 | Ronny's call, 11 September 2026, and it is a CHOICE not a measurement. ND-1200 section 5.48 says report 1 carries \"emulator type (3 digits in the range 050 - 100)\" and names no machine; nothing else held here does either. 93 is the terminal type every D100 session hands SINTRAN (`set-term-type,,93`) and it sits inside the range. **Whether SINTRAN's terminal-type number and the ND emulator level are the same registry is NOT established** - they may be two schemes that overlap. All three models claim it, because nothing says how they should differ. One number in `TDVEmulatorBase.EmulatorLevel`; if a real ND terminal ever answers `CSI 1 x` differently, that wins |
| `CSI 80 h` / `CSI 80 l` sets a flag and reports it, and does NOT change the keypad | Ronny's call, 11 September 2026. The numeric pad Function/Numeric switch (TDV 2200/9 S User's Guide section 11.2) is set, reset, and reported through NDRQ type 2 bit 7, which is correct and complete on the wire. Nothing touches key output, because the manual names the modes and nothing held here says which sequences Function mode SENDS - wiring it would mean inventing them - and because it sits next to the virtual keyboard and its VK codes, which are a standing do-not. The answer is in the scanned 2200 PDFs if it is anywhere, and those are images with no text layer |
| 2115 mode still OBEYS CSI here, and the manuals say a real one must not | Ronny's call, 11 September 2026, made knowing it is unfaithful. TDV 2215 section 3.1: \"Only control codes from the C0-set will be accepted. If control sequences are received, the ESC code will be ignored, while the rest of the sequence will be displayed.\" ND-1200 chapter 8 agrees. This program obeys CUP, CUD and CUF in 2115 mode and `TDV2115ModeParityTests` asserts that it does. Left alone because this is the LIVE D100 path - PED enters and leaves 2115 mode for real - and a terminal that suddenly starts displaying escapes instead of obeying them could turn a working session into junk with no warning. Worth noting an old defect where a stub broke cursor movement in 2115 mode was \"fixed\" by making it obey: the stub was closer to the manual than the fix was |
| A TDV answering DECRQM is OUR extension, using DEC's numbers and DEC's values | Decided 11 September 2026, after reading the manuals. No TDV manual has the sequence: TDV 2215 Functional Specifications section 8.7 lists every CSI sequence the terminal accepts and none carries a `$` intermediate, section 8.3.2 lists everything it sends and that is CPR alone, and the 2200's own delta list (2200/9 S User's Guide section 11.2) adds no query. The TDV's own invented answer table was DELETED; what remains is `TerminalEmulatorBase`'s, which every emulator gets, and it answers about DEC's mode numbers with DECRPM's values - 0 not recognised, 1 set, 2 reset. Kept rather than suppressed because it is a useful tool and a correct DEC answer, but it is not a claim about what a Tandberg would say. The faithful reply is NDRQ, which is on the plan above |
| A TDV2200 reports GRAPHICS and TEKTRONIX unconditionally | Decided 25 August 2026. Both are unconditionally true - the vector decoder is offered every Ground-state byte and nothing gates it. `CSI n greater-than`, which used to toggle them, appears in no manual held here and is now counted instead of obeyed |
| The wire scanner abandons a half-finished sequence after one second | Decided 25 August 2026. A split packet continues in milliseconds; the observed stale ESC waited fourteen seconds and ate the next keystroke. The abandoned bytes are reported, never dropped. Pinned by `WireScannerStaleEscapeTests` |
| Graphics scroll and clear with the text, faithfully | Ronny's call, 9 September 2026. A real VT340 keeps text and graphics in one bitmap, so a whole-screen scroll moves the picture and `ED` mode 2 wipes it. Limits kept deliberately: a scroll REGION leaves the planes alone, and `ED` modes 0, 1 and 3 do not clear them. Pinned by `GraphicsScrollAndClearTests` |
| A Sixel colour introducer with two or more numbers DEFINES, with the missing coordinates zero; only a bare number selects | Both DEC manuals say it in as many words. Decided 26 August 2026 after our old rule — fewer than five numbers means select — was found to leave a register at its power-on colour and draw the picture wrong, silently. Pinned by `SixelShortColourIntroducerTests` |
| An unknown Sixel coordinate system ignores the DEFINITION but keeps the SELECTION | "Other — Ignore sequence" is printed in the table of Pu values, which is about the assignment. Throwing the colour number away too would paint into whatever register was current instead of the one named. The reading is OURS and is not in the manual |
| DECNRCM is ON at power-on | The same shape of deviation: `ESC ( A` keeps meaning British as it always has in this program, rather than the pound sign changing meaning the day the mode was added |

---

## How work gets finished here

- **Look at the pixels, then measure.** Assertions only catch what they were told to expect. Several
  defects were found by opening the artefact when nothing had failed — including one this week,
  where a test passed green while the rendered screen was still sitting on its first line.
- **A number from an experiment is only about the thing the experiment VARIED.** 11 September 2026:
  a test harness was found that built an emulator and a connection and never joined them. After
  wiring it, cutting the wire again left 26 of 29 tests green, and that was written into a commit
  message and this plan as "26 tests still assert nothing". It meant nothing of the kind - only six
  tests ever used the connection, and the other 23 feed the emulator directly, so the wire had
  nothing to do with them. The experiment measured test PLUMBING and was read as test QUALITY.
  It felt like a measurement, which is what made it dangerous. RULE #0b in a new costume.
- **Verify a claim before acting on it, including claims in this file.** Three of this repo's own
  plan entries have been wrong, and two of them would have hidden a real defect.
- **Red before green.** Break the fix on purpose once and watch the new test fail. If it will not go
  red, the test is not testing the fix.
- **Clean-room only.** Outside code is a specification to read and an oracle to run, never something
  to copy. Corpus files are fetched, never committed.
- **Finish with** `dotnet format` → `dotnet build` (0 warnings) → `dotnet test` (quote the number) →
  **`.\scripts\publish.ps1`** → `dotnet build-server shutdown`. Pass `-nodeReuse:false` to builds,
  or MSBuild worker nodes pile up and the shutdown will not clear them.
- **A GREEN BUILD DOES NOT MEAN THE APP CAN BE SHIPPED.** Debug and Release compile XAML
  differently, so a Release publish can fail on something the Debug build and every test accepted -
  it has happened, six `AVLN2000` errors, and it cost a manual-test sitting because the one artefact
  a person can actually run could not be produced. Publish after any `.axaml` or package change, and
  ALWAYS before asking Ronny to look at the app. It takes under a minute, it is the only check that
  exercises the shipping path, and it repoints `publish\current` while the old build is still
  running so nothing has to be closed.
