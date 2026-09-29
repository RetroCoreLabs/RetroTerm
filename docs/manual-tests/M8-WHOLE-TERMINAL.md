# M8 — the terminal as a whole

**Full path:** `docs\manual-tests\M8-WHOLE-TERMINAL.md`
**Parent:** `docs\manual-tests\INDEX.md`
**Machine cover:** `tests\RetroTerm.Tests\Avalonia\ManualPlan\M8WholeTerminalTests.cs`

These cases belong to no single emulator. They are the things that go wrong in the program around
the emulation — the window, the scrollback, the clipboard, the speaker, the terminal list.

Run this once per release on whichever terminal you use most, and once on a TDV2200 as well,
because the TDV path uses a bitmap font renderer and the VT path does not.

---

## What you need

Nothing outside the app for M8.1 to M8.5. M8.6 wants a real host that goes quiet for a while — a
SINTRAN login left sitting does the job.

    Build:   .\scripts\publish.ps1        (works while the old build is still running)
    Run:     publish\current\RetroTerm.Desktop.exe

---

## M8.1 — Resizing the window

**This changed on 2026-08-19.** The window now DRIVES the terminal size: as many whole character
cells as fit, and wrapped paragraphs re-laid out at the new width. It used to magnify a fixed
80x24 and letterbox the remainder, and the host was never told anything.

What was added: a size handler on the canvas, `TerminalSession.ResizeAsync`, and
`IConnection.ResizeTerminalAsync` so one call reaches whichever protocol is in use. The reflow
itself already existed — `TerminalBuffer.ResizeWithReflow` — and had no caller anywhere in the UI.

**One honest limit, and it is in the code:** over SSH the new size is recorded but **not sent**.
SSH carries a resize as a `window-change` channel request and the SSH.NET build here exposes no
way to send one on a shell stream. Telnet does send it, as NAWS. So M8.1c below behaves
differently on the two protocols, on purpose.

### M8.1a — A bigger window gives you MORE TERMINAL, not bigger text

**Needs:** nothing but the app.
**Setup:** connect anything, fill the screen with text (`ls -la`, or the TestServer's Standard
Tests menu item 1).
**Do:**
1. Drag the window corner out to roughly twice the size.
2. Drag it back.
3. Maximise it.
**Pass:** the text stays the SAME SIZE throughout and more of it fits — more columns and more
rows. Maximised, a full-screen program should be using the whole window. The letterbox border is
now at most one cell wide on each edge, being the remainder that no longer divides.
**This is the case that would have caught the old behaviour**, where the same 80 columns simply
got larger.
**Machine cover:** `WindowDrivesTerminalSizeTests` checks the arithmetic — as many whole cells as
fit, never one more — and that doubling the width asks for about twice the columns. It cannot
judge whether the result looks right while being dragged.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.1b — A window dragged very small

**Needs:** nothing but the app.
**Do:** drag the window as narrow and as short as it will go, then back out again.
**Pass:** it stops shrinking the terminal at **20 columns by 4 rows** and will not go below that,
and dragging back out restores a usable screen. What must NOT happen is the history being reflowed
into a one-character-wide ribbon, which cannot be undone by widening again.
**Machine cover:** `AWindowDraggedToNothingStillAsksForAUsableTerminal`.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.1c — Reflow: a wrapped paragraph must re-lay out

**Needs:** nothing but the app.
**Do:** print a paragraph long enough to wrap — one long line, not several — then widen the
window, then narrow it, then widen it again.
**Pass:** the paragraph re-breaks at each width, with no text lost at the old edge and no text
left stranded on a line of its own after widening. Going narrow and back must return the same
words.
**Why one long line matters:** a line that filled up and carried on and a line that ended with a
newline look identical in the grid. The per-line wrap flag is the only record of which it was, and
reflow is the only thing that reads it.
**Machine cover:** `TheSessionResizesTheEmulatorAndReflowsAWrappedParagraph` and
`AParagraphSurvivesWideThenNarrowThenWideAgain`.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.1d — Resize while a full-screen program is drawing

**Needs:** a real host with `htop` or `vim`. **Do this over TELNET and over SSH** — they differ.
**Do:** start `htop`, then resize the window while it is redrawing.
**Pass over Telnet:** `htop` re-lays out to the new size, because NAWS told it.
**Pass over SSH:** `htop` keeps drawing at the size it was given at login. **That is the recorded
limit above, not a defect you have found.** A program started AFTER the resize does get the right
size.
**In both cases:** no torn frame that survives the next redraw, and no crash.
**Note the alternate screen is deliberately NOT reflowed** — a full-screen program is told the new
size and repaints itself, and re-wrapping its lines underneath it would corrupt a display it is
about to redraw anyway.
**Machine cover:** none. Nothing here drives a real full-screen program.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.2 — Scrollback

### M8.2a — Rolling back shows what left the screen

**Needs:** nothing but the app.
**Setup:** produce more output than one screen — `dir /s` on Windows, `ls -R /usr` on a host, or
just hold Enter down for a while after typing something.
**Do:** roll the wheel up.
**Pass:** rows that had scrolled off come back, three lines per notch, and rolling down returns you
to the live view.
**Machine cover:** `M8_2a_RollingTheWheelBackShowsRowsThatLeftTheScreen` drives a real wheel event on
the real canvas and captures the rendered pixels. It cannot see smoothness or judge the scrollbar.
**Judge:** machine, plus an eye for smoothness.
**Result:** ______  **Date:** ______  **By:** ______

### M8.2b — The alternate screen does not pollute the scrollback

**Needs:** a host with `vim` or `less`, or the TestServer's xterm branch (main menu 4).
**Do:**
1. Note the last few lines on screen.
2. Open `vim`, move around, quit with `:q`.
3. Roll back through the scrollback.
**Pass:** the shell's history is exactly as it was. Not one line of vim's screen is in it.
**Machine cover:** `M8_2b_TheAlternateScreenAddsNothingToTheScrollback` counts scrollback lines
across a switch to the alternate buffer and back.
**Judge:** machine, and confirm by eye once with a real vim.
**Result:** ______  **Date:** ______  **By:** ______

### M8.2c — The wheel belongs to the program when the program is full-screen

**Needs:** a host with `less` on a long file.
**Do:** roll the wheel inside `less`.
**Pass:** `less` scrolls its own view. Our scrollback does not move — there is nothing behind the
alternate screen to scroll to.
**Machine cover:** `M8_2c_TheWheelDoesNotScrollBackWhileTheAlternateScreenIsUp`.
**Judge:** machine, confirmed once by eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.3 — Copy and paste

### M8.3a — What you select is what you get

**Needs:** nothing but the app.
**Do:** drag across part of a line, press Ctrl+Shift+C, paste into Notepad.
**Pass:** exactly the characters that were highlighted, no leading or trailing spaces that were not
on the screen.
**Machine cover:** `SelectionManagerTests`, `SelectedColumnRangeTests` and
`MenuAndShortcutTests.TerminalCanvas_SelectAll_ThenGetSelectedText_ShouldReturnBufferContent`. None
of them goes near the real Windows clipboard, which is the half a person has to check.
**Fixed while writing this case, 2026-08-17:** the copy took each row to its full width and added
the blank right-hand end as spaces, while the HIGHLIGHT trimmed it — so what you got was not what
you selected. Both now read their row range from the same method, which is what
`SelectionManager`'s own note about one accessor keeping the two telling the same story asked for.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.3b — A line the terminal wrapped copies as ONE line

**Needs:** nothing but the app.
**Setup:** produce a line longer than the window is wide, so the terminal wraps it itself. A command
line of about 120 characters does it at 80 columns. Do not use a program that breaks the line
itself — the point is a line the TERMINAL split.
**Do:** select the whole thing across both screen rows, copy, paste into Notepad.
**Pass:** one line in Notepad. Pasting it back into a shell runs one command.
**Why this case exists:** the terminal knows which rows were wrapped — `TerminalBuffer.IsLineWrapped`
— and `ScreenReader` already honours it. The copy path did not, so it put a newline at every screen
row boundary, and a copied long command pasted back into a shell ran as two commands with the second
half as a command of its own. Found while writing this document, 2026-08-17.
**Machine cover:** `M8_3b_ALineTheTerminalWrappedCopiesWithoutABreak` and its scrollback twin.
**Judge:** machine, confirmed once by eye through the real clipboard.
**Result:** the machine half is covered and green. **JUDGED BY RONNY, 31 August 2026: PASS.** A real 124-character command wrapped itself at column 80 in the TestServer's session, was selected across both rows, copied and pasted into a real Notepad, and came back as ONE line - the wrap is invisible in the paste. The 2026-08-17 fix (ScreenReader honouring `TerminalBuffer.IsLineWrapped`) holds through the real clipboard.  **Date:** 2026-08-31  **By:** Ronny

### M8.3c — Paste reaches the host

**Needs:** any connection with an echoing host.
**Do:** copy `echo hello` from Notepad, click in the terminal, press Ctrl+Shift+V.
**Pass:** the text appears at the prompt exactly once.
**Machine cover:** `ClipboardManagerTests` for the copy-then-paste round trip and
`BracketedPasteAndFocusTests` for the wrapping the emulator puts around it. The canvas's own paste
method is deliberately NOT automated: it is `async void` and asks the real Avalonia clipboard, which
headless does not have, so a test of it would only prove the test's own stub works.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.3d — Bracketed paste

Same case as **§M2.7** of the overview — do it there, once, and record it there. Repeated here only
so a reader of M8 does not think it was forgotten.

---

## M8.4 — The bell

### M8.4a — It sounds like a bell

**Needs:** speakers, and Ronny's ears. **Nothing in this repository can judge this.**
**Do:** `printf '\a'` on a host, or press the TestServer's bell test.
**Pass:** one clean tone, immediately, at the moment the byte arrives. Not a click, not a "bup", not
a delay you can notice.
**Machine cover:** the shape of the wave only — see M8.4b. Whether it is audible is what cost eleven
correcting turns once, and it is still true that no assertion can hear it.
**Judge:** ear.
**Result:** PASS - "a nice fine beep", judged 27 August 2026 with three BEL bytes 1.2 s apart delivered by ECHO with a literal BEL byte (0x07). Audible, instant, not a "bup".  **Date:** 2026-08-27  **By:** Ronny

### M8.4b — Listen to it without running the app

**Needs:** nothing.
**Do:** run the suite once, then play
`tests\RetroTerm.Tests\Avalonia\images\rendered\bell.wav`.
**Pass:** the same tone you hear in the app. If the file sounds right and the app does not, the
fault is in the playback path, not in the tone — which is the whole reason the file is written.
**Machine cover:** `M8_4b_TheBellIsWrittenOutAsAWaveFileToListenTo` writes it and checks the header
says what `BellService` was asked for.
**Judge:** ear, but a much cheaper one.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.5 — Every terminal in the list is really that terminal

**Needs:** nothing but the app. **Fourteen terminals**, not fifteen — the overview's count was wrong;
`EmulatorFactory.AvailableEmulators` holds VT52, VT100, VT102, VT220, VT240, VT320, VT340, VT420,
XTERM, XTERM-256COLOR, TDV1200, TDV2215, TDV2200 and TEK4014. ANSI can be built by name but is
deliberately not offered in the list.

**Do:** for each one — File, New Tab As, pick it, connect to the TestServer, press `I` for Terminal
information.
**Pass:** the TestServer names the terminal you picked. A silent fall back to VT100 is the failure
this case exists for, and it is invisible until something asks.
**Machine cover:** `TerminalTypeIsSelectableTests` (the dropdown offers every one),
`NewTabAsMenuTests` (the menu does too) and `ChosenTerminalReachesTheTabTests` (each survives being
saved and re-opened). What no test covers is the round trip through a real socket to a host that
asks — which is what the `I` menu does.
**Judge:** eye, fourteen times. Worth it once per release.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.6 — Long idle, then traffic

**Needs:** a real host.
**Do:** connect, leave it alone for half an hour with the window visible, come back and type.
**Pass:** it responds at once; the screen is not stale; Task Manager does not show the app burning a
core while nothing happens.
**Machine cover:** `CursorBlinkLifetimeTests` proves a replaced renderer's blink timer is actually
stopped, which is one known way to burn a core. It says nothing about half an hour.
**Judge:** eye and Task Manager.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.7 — Display zoom

**This changed on 2026-08-19, twice.** Zoom was added, and then corrected after Ronny found three
faults by eye that sixteen automated tests could not see: 300% showed nothing, coming back from full
screen showed nothing, and 75% looked like it moved the screen instead of scaling it.

Two causes, both fixed. A picture LARGER than the window was still being centred, so the terminal's
origin — where every prompt is — sat off the top-left corner and the screen came up blank. And the
percentage multiplied the FONT's natural size rather than the fitted one, so an ordinary window
showed a fixed 80x24 grid at about 177% and every rung below that made the picture *smaller* than
the default view.

**100% now means the picture the window shows on its own.** There is no separate "Fit" entry any
more, because it was the same thing under a second name showing a different size.

### M8.7a — 100% is the picture you already had

**Needs:** nothing but the app.
**Setup:** connect anything and fill the screen. Use a TDV2200 — its grid is pinned, so the zoom is
doing all the work.
**Do:** note how big the text is, then pick 100% in the status-bar dropdown.
**Pass:** nothing moves. 100% is where it already was.
**This is the case that would have caught the old behaviour**, where choosing 100% abruptly halved
the picture and left it floating in the middle of a black window.
**Machine cover:** `DisplayZoomPixelTests.TheNormalViewFillsTheWindow` measures that the ink reaches
the window edge on one axis.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.7b — Zooming in keeps the cursor in view

**Needs:** nothing but the app.
**Do:**
1. Press Ctrl+plus repeatedly up to 300%.
2. Type something.
3. Press Enter a few times so the cursor walks down the screen.
4. Ctrl+0 to come back.
**Pass:** the text gets bigger each step and **you can always see the cursor**. At 300% the picture
is far wider than the window and the view moves to follow what you are typing. Ctrl+0 returns to the
whole screen.
**The failure this exists for is a blank window.** If any step shows nothing at all, the anchoring
has regressed.
**Machine cover: nearly all of it, as of 2026-08-20.** Ronny asked whether this could be automated,
and the measurable half can:

- `M8WholeTerminalTests.M8_7b_ZoomingInKeepsTheCursorInView` walks the whole ladder with real
  Ctrl+plus presses and checks the cursor is visible at every rung, then again after Ctrl+0.
- `M8WholeTerminalTests.M8_7b_TheViewFollowsTheCursorDownTheScreen` types twenty lines at 300% and
  checks the cursor stays in view as it walks down.
- `DisplayZoomPixelTests` measures the ink at 300% for the home corner and the far corner.

**How they ask the question is worth knowing**, because it is reusable: the screen is rendered
twice, once as it is and once with the cursor hidden by DECTCEM, and the two pictures compared. If
they differ, the cursor is on screen. No searching the pixels for the cursor's shape (it is the same
colour as the text), and no recomputing where it ought to be (that is the arithmetic under test).

Both were confirmed by putting the original fault back: with the centring restored, all three go red
at once. With only the cursor-following disabled, the ladder test still passes and the typing test
fails at line 8 — which is what proves they cover different things.

**What is left for you:** whether following the cursor feels smooth rather than lurching, and
whether the magnified text is pleasant to read. Neither is a pixel question.
**Judge:** eye, for the feel only.
**Result:** driven live over MCP on 27 August 2026 and photographed at both ends of the ladder - `m87b-zoom-100.png` and `m87b-zoom-300.png` in the rendered images folder. At 300 percent the text is three times the size, the view is anchored so the cursor stays on screen, and the ReGIS drawing scales with it. Ctrl+0 came back to 100. **JUDGED BY RONNY, 31 August 2026: PASS.** Driven himself through the whole ladder, the cursor stayed visible at every step and it feels right. M8.7b is CLOSED. That closes Phase 1.  **Date:** 2026-08-31  **By:** Ronny

### M8.7c — Full screen and back

**Needs:** nothing but the app.
**Do:** at 200%, maximise the window, then restore it. Repeat at 100%.
**Pass:** text is visible at every point. Nothing goes blank on the way in or out.
**This is one of the three faults Ronny reported**, and it had the same cause as M8.7b.
**Machine cover:** `M8WholeTerminalTests.M8_7c_GoingFullScreenAndBackKeepsTheCursorInView` renders
the same zoom at an ordinary window, a maximised one, an ordinary one again and a small one, and
checks the cursor is visible at each. It goes red with the old centring restored. What it does not
do is drag a real window frame around, which is the only part left.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.7d — Dragging the view when it does not fit

**Needs:** a mouse with a middle button.
**Do:** at 300%, hold the middle button and drag.
**Pass:** the picture moves with the pointer, one pixel per pixel, and stops at its own edges —
you cannot drag the terminal off the window. At 100% the middle button does nothing, because
there is nothing off the edge to reach.
**Judge:** eye and feel.
**Result:** ______  **Date:** ______  **By:** ______

---

## M8.8 — Changing the emulation on a live connection

**New on 2026-08-19.** View, Emulation changes the terminal type of the tab you are looking at
**without dropping the connection**. Before this, correcting the type meant reconnecting, because
the connect path threw the whole tab away when the type differed — so discovering the wrong choice
after logging in cost a login.

The screen, cursor and scrollback come across. Graphics do not, and cannot: the terminal being
switched to may have no bitmap at all. The status bar says so when there were any.

### M8.8a — The connection survives, and so does the screen

**Needs:** a real host, or the TestServer.
**Setup:** connect as a VT100 and log in, so there is something on screen worth keeping.
**Do:** View, Emulation, TDV2200.
**Pass:** you stay logged in — no reconnect, no new prompt — and the text that was on screen is
still there. The tab title and the status bar now say TDV2200. Type something: it reaches the host
as before.
**Machine cover:** `EmulationChangeTests` proves the connection object is unchanged and the screen,
cursor and scrollback carry over; `EmulationMenuTests` proves the canvas is rewired rather than left
drawing the old terminal. None of it talks to a real host.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.8b — Fixed-geometry terminals snap to their real size

**Needs:** nothing but the app.
**Do:** on a maximised window, start as XTERM (which fills the window — perhaps 150x50), then
switch to TDV2200, then to TEK4014, then back to XTERM.
**Pass:** TDV2200 becomes exactly **80x25** and TEK4014 becomes **74x35**, whatever the window is,
because that is what the hardware was. XTERM goes back to filling the window. Over Telnet the host
is told each new size.
**Machine cover:** `EmulationChangeTests.AFixedGeometryTerminalSnapsToItsRealSize` and
`ATerminalThatFollowsTheWindowKeepsTheSizeOnScreen`.
**Judge:** eye, plus the size in the status bar.
**Result:** ______  **Date:** ______  **By:** ______

### M8.8c — The new terminal is really the one parsing

**Needs:** the TestServer.
**Do:** connect as VT100, press `I` for Terminal information and note what it says. Change to
VT220 with View, Emulation. Press `I` again.
**Pass:** the second answer names VT220. A terminal that reported the old type would mean the change
reached the screen but not the query/response path.
**Machine cover:** `TheNewTerminalIsTheOneDecodingWhatArrivesNext` sends a VT52-only sequence after
switching to VT52 and checks it is obeyed. The round trip through a real socket is this case.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.8d — Graphics are dropped, and it says so

**Needs:** the Sixel or ReGIS fixtures from M6.
**Do:** on a VT340, draw something. Then View, Emulation, VT100.
**Pass:** the picture goes, and the status bar says the graphics were dropped rather than leaving
you to wonder. The TEXT is still there.
**Why it is not a defect:** a VT100 has no bitmap, and there is no honest way to reinterpret a ReGIS
plane as something a VT100 could hold.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

### M8.8e — From a script and from MCP

**Needs:** the Script Console, and an MCP client if you have one to hand.
**Do:** in the Script Console, run `EMULATION type=TDV2200`. Then try `EMULATION type=VT999`.
**Pass:** the first changes the terminal exactly as the menu does. The second is refused with a
message naming the terminals that DO exist, and the session is left alone.
**Why this case exists:** the menu, the script DSL and the MCP tool are three ways into one
behaviour, and this repository has already shipped a behaviour fixed on one surface and broken on
another for weeks (`SEND` swallowing `\r`). All three go through the same registry command.
**Machine cover:** `CommandSurfaceEquivalenceTests` generates the check across the whole registry,
so a parameter wired on one surface only fails immediately.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## Gaps this document recorded rather than tested

1. **A resize does not reach a program already running over SSH.** The size is recorded and a
   connection opened afterwards gets it, but SSH carries a resize as a `window-change` channel
   request and the SSH.NET build referenced here exposes no way to send one on a shell stream.
   Telnet has no such gap — it sends NAWS. Checked by M8.1d, on both protocols.
2. **The real clipboard is outside every automated test.** Selection and the copy path are covered;
   `AvaloniaClipboardService` talking to Windows is not, and headless has no clipboard to talk to.
