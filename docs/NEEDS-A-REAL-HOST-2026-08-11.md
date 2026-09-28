# What still needs a real host, or a human looking at it

**Written:** 2026-08-11, after a long unattended session (27 commits).
**Full path:** `docs\NEEDS-A-REAL-HOST-2026-08-11.md`

Everything below is **implemented and tested** — the suite is 4703 + 123 passing, 41 skipped,
0 warnings. What it is not is *judged*. A test can prove that bytes match a specification I
read; it cannot prove the specification is the one your hardware follows, and it cannot tell
you whether something feels right to use.

This is the list of those things, so they are decided by you rather than assumed by me.

---

## 1. Assumptions marked in the code

Each of these is a reading I took where a document was silent. They are flagged at the point
they matter, not just here.

| Where | The assumption | What would settle it |
|---|---|---|
| `NorskDataGraphicsModule.cs` mode 8 | Rectangle-fill coordinates are in the terminal's logical space, like every other coordinate in the protocol. The spec names four "coords" without saying which space. | One `ESC "8;…h` from a real ND host, and looking at where the rectangle lands. |
| `NorskDataGraphicsModule.cs` mode 24 | Defining a circle also **draws** it. The spec has a separate "execute draw" (mode 30) and lists its callers as the polygon and copy-window commands, never the circle command. | A real host drawing a circle. If it needs mode 30 the fix is one line — store, and let 30 render. |
| `SystemFontRenderer.cs` soft glyphs | A downloaded set starts at 0x20 (the 96-character form). DECDLD can also describe a 94-character set starting at 0x21, and its size parameter says which; that parameter is read and not acted on. | Any host that downloads a 94-character set. Symptom would be every glyph one position out. |
| `TektronixVectorDecoder.cs` | Which coordinate bytes a host may omit. This is standard Tek 4010/4014 behaviour, **derived rather than quoted** — the ND spec documents the byte layout and says nothing about omission. | Real hardware. In practice gnuplot's output agrees with it, which is why it is trusted. |
| `TDV2200Emulator.cs` identification | The two ND model bytes are `0x40 0x40`, the only concrete pair the spec's worked example carries. Which pair means which machine is **not known**. | Reading them off a real ND-324, ND-325, ND-246, ND-285, ND-320 or ND-322. They are settable in code. |
| `TDV2200Emulator.cs` cursor-at-last-vector | Leaving graph mode parks the text cursor at the last plotted point. **Derived, not quoted** — but gnuplot's output plainly depends on it, and the plots look wrong without it. | Already strongly evidenced by real gnuplot streams. Lowest risk on this list. |

---

## 2. Things no test in this repo can judge

**Mouse reporting.** The modes (9, 1000, 1002, 1003) and both wire encodings match the
specification, and headless tests drive real clicks through the real control. Nothing has yet
driven them from a program that actually tracks the pointer. Worth trying: `vim` over SSH,
then `tmux`, then `mc`. What to look for — does clicking put the cursor where you clicked,
does dragging select inside the program, does the wheel scroll its view rather than the
scrollback.

**Shift-to-select.** When a host is tracking the mouse, the host wins the gesture and holding
**Shift** forces local text selection — the xterm/PuTTY/gnome-terminal convention. That is a
decision, not a fact. If it feels wrong on your machine, say so and it inverts.

**Soft fonts at your font size.** A downloaded 8×12 matrix is scaled to whatever cell the
terminal draws in. The block in
`tests\RetroTerm.Tests\Avalonia\images\rendered\softfont-block.png` looks right to me, but
whether real downloaded text is legible next to Consolas at your size is a judgement about
pixels on your monitor.

**Bracketed paste in anger.** Pasting a multi-line command into a shell with the mode on
should show it, not run it. Cheap to check and worth checking once.

**The terminal type on the wire.** `xterm-256color` now goes out lower case over both telnet
and SSH; TDV names keep their own case, per RFC 1091 and the ND hosts' expectations. If a
host you use rejects a name, that mapping is one method in `ConnectionFactory`.

---

## 3. What the ND graphics counter is for

`NorskDataGraphicsModule.UnhandledSequences` counts every `ESC "` sequence recognised but not
acted on, keyed `mode:final`. Six of the thirty modes are implemented (6, 8, 9, 10, 17, 24);
the rest are counted rather than guessed at, because the spec names them without their
parameter meanings.

**Pointed at a real ND host, that counter says which modes actually matter.** That is a far
better guide to what to build next than working down the table in order — and it is the one
piece of evidence that would unblock most of the remaining ND work.

---

## 4. Suggested order, if you want one

1. **Bracketed paste** — one paste, ten seconds, and it either works or it does not.
2. **Mouse in vim over SSH** — the largest untested surface, and the one with a decision in it.
3. **Point a session at an ND host and read `UnhandledSequences`** — turns the remaining ND
   graphics work from guesswork into a list.
4. Everything else can wait for hardware that may never appear.
