# M4 — TDV1200 / TDV2215 / TDV2200

**Full path:** `docs\manual-tests\M4-TDV.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Machine cover:** `tests\RetroTerm.Tests\Avalonia\ManualPlan\M4TdvTests.cs`
**Generated artefact:** `tests\RetroTerm.Tests\Avalonia\images\rendered\tdv2200-key-sheet.md`

The TDV emulators have the strongest evidence in the program — OCR'd Tandberg manuals, a TDV2215
ROM dump, fonts traced from real bitmaps. So this section is not about whether the sequences are
right on paper. It is about the two things paper cannot settle: **the keyboard on real hardware**,
and **a real SINTRAN session**.

---

## M4.1 — The TestServer's TDV menu, once per model

**Needs:** the TestServer. Connect as TDV1200, then TDV2215, then TDV2200, and walk main menu 2.

| Menu | Test | Pass |
|---|---|---|
| 1 | Query/Response (DA, CPR, DSR) | The reply matches the manual's worked example |
| 2 | Character Sets | Graphics I/II, Math, Greek switch and switch back |
| 3 | Drawing Operations | Rectangle insert and delete land on the cells the test names |
| 4 | Function Keys | F1–F12 produce **TDV** codes. F1 is the terminal's OWN F1, `ESC [ 5 0 _` — NOT HJELP, which is a separate key (G53, `ESC [ 4 6 _`, no PC key). See M4.2c |
| 5 | Modes and Features | Smooth scroll, blink, the message LEDs |
| 6 | Key Detection | Every submenu — see M4.2, which is the real work |
| 7/8/9 | Per-model | 2115 compatibility, ND graphics, protected areas, transparent mode, DCS PUSH-key programming, graphics extension, Tektronix mode, ISO 646 variants |
| A | Comprehensive Demo | Nothing visibly wrong |

**Machine cover:** the TDV suite is the largest in the program — query and response, protected
areas, work areas, LEDs, rectangles, character sets, ISO 646 variants, and the rendered screenshots
`tdv2200-text.png`, `tdv2200-charset-ascii.png`, `tdv2200-charset-graphics.png`. What none of it
covers is a **real** host driving the terminal, which is M4.4.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M4.2 — The keyboard, against real hardware

**This is the case the whole section exists for**, and it is blocked on a machine that may never
appear. When one does, the pass must be quick or it will not happen — so the suite writes the whole
key map out as a sheet to print:

> `tests\RetroTerm.Tests\Avalonia\images\rendered\tdv2200-key-sheet.md`

121 keys, 46 of which send a fixed sequence. Each row gives the grid position, the key's name, its
Windows virtual key code, what it sends in **extended** mode and in **simple ASCII** mode, both as
readable text and as hex, and an empty **OK?** box to tick.

It is generated from `TDV2200KeyRegistry` every test run, so it cannot drift from the code the way
a hand-written table would. The keys with nothing in either column are ordinary letters, modifiers,
and the programmable keys — those deliberately have no fixed sequence, because a host defines what
they send.

### M4.2a — Tick the sheet

**Needs:** a real TDV2200 keyboard, or a real ND machine with one attached.
**Do:**
1. Print the sheet.
2. Start a byte trace — `terminal_tracestart`, then `terminal_traceread` as you go.
3. Press every key. Tick the ones that match. Write down what the others actually sent.
**Pass:** every row ticked. Anything that is not becomes a defect with the bytes already captured.
**Judge:** hardware.
**Result:** ______  **Date:** ______  **By:** ______

### M4.2b — The four arrows, first

**Do:** press the arrows before anything else.
**Pass:** one byte each — UP `0x1C`, DOWN `0x0B`, LEFT `0x08`, RIGHT `0x18` — in **both** extended
and simple mode.
**Why first:** a fallback to VT220 would send a whole escape sequence instead of one byte, and that
is the failure most likely to be hiding. There is no VT220 fallback in TDV mode by design; keys
with no TDV equivalent return nothing at all.
**Machine cover:** `M4_2b_TheArrowsSendOneC0Byte`.
**Judge:** hardware, but the machine already pins the intent.
**Result:** **PASS in extended mode, against real SINTRAN.** Measured 1 September 2026 on the D100
(`localhost:9010`, TDV2200, 80x25, `set-term-type,,93`), pressing each arrow through
`terminal_localkey` — the real production key path a physical or virtual keypress takes — and
reading the protocol trace:

```
UP     TX 1C   FS    "TDV: cursor up"
DOWN   TX 0B   VT    "TDV: cursor down"
LEFT   TX 08   BS    "TDV: cursor left"
RIGHT  TX 18   CAN   "TDV: cursor right"
```

One byte each, no escape sequence anywhere — so the VT220 fallback this case exists to catch is
genuinely absent. **SINTRAN acts on them too**, which the bytes alone could not show: LEFT at the
`@` prompt echoed back `SET-TERM-TYPE,,93`, its command-line recall.
**The SIMPLE ASCII half is now measured too, 2 September 2026 - PASS.**

**Two sentences of this case were corrected on 11 September 2026 and the correction matters.** It
used to say the terminal was put into 2115 mode with `CSI ? 40 h` and that the mode was "CONFIRMED
active by a real DECRQM round trip rather than assumed: `ESC[?40$p` came back `ESC[?40;1$y`".

Neither the sequence nor the mode number exists. Mode 40 is the printer code format (TDV 2215
Functional Specifications section 8.7.1) and no TDV manual has DECRQM at all. And the round trip was
not evidence about hardware: **RetroTerm is the terminal in this setup and the D100 is the host**, so
the query went into our own receive path and measured this program answering its own invented
sequence. The real way in is `CSI 66 l` - reset, no private marker - and the way out is `ESC Q`. See
`docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`.

**What still stands is the measurement this case is actually about: the key bytes.** Every arrow then sent the same single byte as in extended mode - `1C`, `0B`, `08`, `18` - which
is what `AlwaysSameCode` claims and what the User's Guide says. So M4.2b passes in BOTH modes.
**Date:** 2026-09-01  **By:** measured over MCP against the live D100

### M4.2c — F1 sends the terminal's own F1, and never a VT220 sequence

**This case used to say "F1 is HJELP", and that was WRONG.** Corrected 2 September 2026 after
measuring it against the live D100. The two are different keys:

| Grid | Key | Windows VK | Extended sequence |
|---|---|---|---|
| `F51` | the TDV's own **F1** | **112** — the PC's F1 | `ESC [ 5 0 _` |
| `G53` | **HJELP** | **0** — no PC key reaches it | `ESC [ 4 6 _` |

**Pass:** pressing F1 sends `ESC [ 5 0 _`, and nothing sends the VT220 form `ESC [ 1 1 ~`.
**Why this one is called out:** a host that receives the VT220 form does nothing at all, and nothing
on the screen says why. That risk is real; the HJELP expectation was not.
**HJELP is still reachable** — through the virtual keyboard or a user binding, since the binding
system maps any key combination to a grid position. Ronny's call, 2 September 2026: a PC F1 belongs
on the terminal's F1, and giving it to HJELP instead would leave the terminal's own F1 unreachable.
**Machine cover:** `M4_2c_TheF1KeySendsTheTerminalsOwnF1AndNeverAVt220Sequence`, which now drives
VK 112 through the real mapper. **The old test proved nothing about the F1 key** — it read grid G53
directly and never touched a keypress, so it passed no matter what F1 did.
**Result:** **PASS.** Measured 1 September 2026 on the D100 through `terminal_localkey`:
`TX 1B 5B 35 30 5F` — `ESC[50_`, "TDV Function Key Report - key=50". No VT220 form. SINTRAN echoed
`[50_` back as text, so it does not act on function keys at the `@` prompt.
**Date:** 2026-09-01  **By:** measured over MCP against the live D100

**Three documents disagreed with the code here, and the code was right.** Worth recording because
the disagreement is what cost the time:
 - `MEMORY.md` said "F1 → HJELP, VK 112 maps to G53 first in registry". False: VK 112 is on F51.
 - `docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md` contradicts ITSELF — its summary line gives HJELP as
   `ESC[46_`, while its key tables give G53 as VK 112 sending `ESC[28~`, a VT220-shaped sequence
   that the ND function-key form `ESC [ nn _` rules out.
 - This case expected `ESC[46_` from the F1 key.
 - `TdvKeyboardDocumentMatchesTheRegistryTests` exists to catch exactly this drift, but only covers
   the arrows and HOME, which is why the function-key rows went unchecked.

### M4.2d — HOME

**SETTLED FROM DOCUMENTATION, 31 August 2026. HOME sends GS `0x1D` in BOTH modes** - it is not
the exception the two entries below record it as being at the time. What still needs a real
keyboard is only whether the User's Guide itself is right; that part is unchanged and still needs
hardware that may never appear.

**How it was settled.** Ronny's instruction: find the documentation already in this repo and
decide, rather than wait indefinitely on hardware. Three research agents read every keyboard
document in the repo in parallel. `spec\Keyboards\keyboard-spec.md` section 6.8.3 settled it - it
cites the TDV-2200/9 User's Guide (ND-30.003.04 EN) directly and documents its own OCR-correction
history: an early, uncorrected OCR of User's Guide section 7.2 misread HOME's simple-mode byte as
DLE (`0x10`); a later, cross-checked OCR of section 9.1 marks B47/B48/B49 as "is always" keys and
gives HOME GS (`0x1D`) in both modes, explicitly superseding the DLE reading. No other document in
the repo cites a manual for the DLE value at all -
`docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md`, the source that value had
actually traced back to, gave no citation for it and elsewhere in its own tables listed HOME with
only one code, not two.

**What was found and fixed on the way, in two steps, ending back where the code started.**

1. `KeyboardMapper.cs` was driven through `terminal_localkey` - the real production path a
   physical or virtual keypress takes - against a real ND-100 running SINTRAN with PED open.
   Extended mode genuinely homed the cursor. Simple mode, confirmed genuinely active with a live
   DECRQM round trip, changed nothing: HOME kept sending GS. Real defect: the registry's
   SimpleAscii for HOME was `0x10` at the time, and `KeyboardMapper.cs`'s own hardcoded C0 table
   never consulted it, sending the extended-mode byte regardless of mode. Fixed in `a527aa1`.
2. That fix made the code match the registry, and the registry was itself wrong - see above.
   Corrected in `04eb2f4`: the registry's SimpleAscii for HOME is now `0x1D`, and
   `KeyboardMapper.cs`'s table reverted to send it in both modes, which is what it always did
   before step 1. Eleven files needed correcting in total, including a full rewrite of
   `HomeInSimpleModeCostsTwoCharactersTests.cs`, whose entire premise - two competing candidate
   bytes - no longer holds.

**The echo-safety finding from 28 August 2026 survives, halved.** GS is not safe to echo on a
TDV2200: the graphics side takes it unconditionally as the Tektronix enter-graph-mode code, so
every character after it becomes a vector coordinate rather than printing. That was true before
today and remains true - it now applies to HOME in both modes, since there is only one byte.
Pinned as `HomeInSimpleModeCostsTwoCharactersTests.AnEchoedHomeSwallowsEverythingAfterIt`.

**Confirmed live on the D100, 1 September 2026.** HOME pressed through `terminal_localkey` against
real SINTRAN sent **GS `0x1D`** — `TX 1D  GS  "Group Separator - TDV: home"` — which is the value
the documentation settled on, now measured through the production key path rather than only
asserted in a unit test. SINTRAN answered BEL, because HOME has no meaning at the `@` prompt; the
page-mode half was already answered separately in PED.

**The SIMPLE ASCII half still could not be measured, and the reason is worth writing down.**
Putting the terminal into 2115 compatibility mode needs `CSI ? 40 h` to arrive on the RECEIVE
path, which `terminal_echo` can do because its text goes through `WriteToTerminal` and is parsed by
the emulator exactly like bytes off the wire. It printed as literal text instead: the RetroTerm
holding the MCP port was commit `2ad56fe`, built 31 August 10:45, which **predates `ce11003`
"Give ECHO the same escape decoding SEND already has"**. Verified with
`git merge-base --is-ancestor`, not assumed. Nothing is wrong with the current code — the running
binary was simply older than the fix. **This is exactly what the MCP server banner's own warning is
for:** it names its commit, and checking it against the build you expect is the difference between
a real finding and a stale one.

**The SIMPLE ASCII half is CLOSED too, 2 September 2026 - HOME sends GS `0x1D` there as well.**
Measured on the live D100 with 2115 compatibility mode confirmed active by a DECRQM round trip
(`ESC[?40$p` answered `ESC[?40;1$y` - mode 40, value 1, set), not merely commanded. HOME then sent
`TX 1D`, the same byte as in extended mode. That is what the registry's `AlwaysSameCode` flag
claims and what `spec\Keyboards\keyboard-spec.md` §6.8.3 reads out of the User's Guide, so the
documentation, the code and the live machine now all agree.

**Result:** CLOSED. Settled from documentation, then confirmed live in BOTH modes. The only thing
still beyond reach is whether a real TDV KEYBOARD agrees with the User's Guide, which needs
hardware that may never appear.  **Date:** 2026-08-31, extended mode confirmed 2026-09-01  **By:** documentation (three
parallel research agents + `spec\Keyboards\keyboard-spec.md` §6.8.3), then measured on the D100

### M4.2f — the glued escape, CAUGHT and explained, 2 September 2026

The run sheet has carried a watch item since 20 August: *"if the trace ever shows a keystroke glued
to an earlier escape - an `ESC S` where you plainly typed `S` - stop and tell me. It happened once
and did not reproduce."* It reproduced, and the raw block settles what it is.

Pressing RIGHT showed in the decoded pane as:

```
TX  1B 18    ESC.    UNKNOWN   Unrecognised sequence - ESC final 0x18
```

Two bytes, apparently - an ESC glued to the cursor-right CAN. Reading the same press with
`raw=true` shows what actually crossed the wire:

```
TX  18       BLOCK   1 bytes
TX  18       CAN     Cancel - TDV: cursor right
```

**One byte. The keyboard is not at fault and nothing wrong reaches the host.** The gluing happens
in `WireScanner`, which is stateful by design and had a stale ESC pending from earlier traffic; it
attached that ESC to the next byte in the DECODED view only. `CLAUDE.md` already records the same
class of thing - "the observed stale ESC waited fourteen seconds and ate the next keystroke" - and
`WireScannerStaleEscapeTests` pins the one-second abandon rule.

**What is still worth doing:** the abandon rule clearly did not fire here, because the two entries
were seconds apart. Whether the rule is not applied on the TX side at all, or the pending state is
reset by something other than time, is not answered by this capture. That is a real question for
`WireScanner`, and it is a TRACE-RENDERING defect rather than a terminal one - which is exactly
why it was worth separating the two before writing anything down.

**Result:** the watch item is ANSWERED as far as the keyboard goes - the wire is correct. The
scanner question is recorded in `docs\PLAN.md`.
### M4.2e — The keys the app adds

**Needs:** the app only.
**Do:** open the Virtual Keyboard (View menu), bind a key combination to a grid position, and press
it.
**Pass:** the bound combination sends the sequence the sheet gives for that grid position, and the
binding survives a restart — it is stored in `%AppData%\RetroTerm\tdv-key-bindings.json`.
**Machine cover:** `VirtualKeyboardPanelTests`, `TDV2200AltKeyTests`, `TDV2200KeyboardAvaloniaTests`
and `UserDefinedKeyPressTests` drive the real panel and the real binding store.
**Judge:** machine, plus one press by eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M4.3 — National characters and the fonts

**Needs:** the app, on a TDV2200.
**Do:**
1. Type `ÆØÅæøå` at a prompt.
2. Switch the ISO 646 variant to Norwegian, then to International, and type them again.
3. Open `tdv2200-charset-ascii.png` and `tdv2200-charset-graphics.png` and compare with the screen.
**Pass:** the Norwegian letters appear as letters, and on the wire they are the 7-bit ASCII
positions the variant defines (`ø` as `|`, `Æ` as `[`, `Å` as `]`), not UTF-8.
**Machine cover:** `TDV2200NationalCharacterTests`, `TDV2215CharSetScreenshotTests` and the ISO 646
conversion tests. The bitmap font's *legibility at your font size* is yours.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M4.4 — A real ND machine

**Needs:** a real ND host through the retroterm MCP. `terminal_connlist` holds the machines — D100
on localhost:9010, D102 on localhost:9102, the 5000 CPU on localhost:4500, OPCOM. **Whether any of
them is running today is a thing to check, not to assume.**

**Do:**
1. `terminal_open` once, and keep the session — reconnecting mid-program hangs the line on these
   machines.
2. Send ESC first on a fresh SINTRAN connection, or it will not prompt.
3. Log in with **one** send: `SYSTEM\r\r` — the name, a carriage return, and a second one for the
   blank password. Splitting it bounces back to a fresh prompt.
4. Do ordinary work. Watch for anything the TestServer never produces.
5. Before disconnecting, read `NorskDataGraphicsModule.UnhandledSequences`.

**Pass:** an ordinary session behaves. Step 5 is the real prize and belongs to §M7 — it turns the
largest remaining ND item from guesswork into a work list.
**Judge:** hardware.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **The real TDV keyboard**, until a machine appears. M4.2 is written so that day is one afternoon
  rather than one week.
- **The two ND model identification bytes**, recorded in `docs\PLAN.md` (Phase 4, blocked on a
  machine).
- **HOME in simple ASCII mode** — M4.2d, one press.
