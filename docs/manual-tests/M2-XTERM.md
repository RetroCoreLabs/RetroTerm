# M2 — xterm and xterm-256color

**Full path:** `docs\manual-tests\M2-XTERM.md`
**Index:** `docs\manual-tests\INDEX.md`
**Overview row:** `docs\MANUAL-TEST-PLAN-2026-08-17.md` §M2
**Backs:** `docs\FINISH-PLAN-2026-08-11.md` P1.4, and the sweep at
`docs\XTERM-CTLSEQS-SWEEP-2026-08-17.md`

`ctlseqs.txt` has been walked end to end and every sequence is marked implemented, deliberately not,
or missing. **So this pass is not about the document. It is about real programs**, which is the one
thing no test server can imitate — and it holds the two open decisions in the whole plan.

---

## M2.0 — The TestServer first

**Needs:** the TestServer, main menu **4**.

It covers the same ground mechanically: the alternate screen, mouse reporting in both encodings
with every event decoded into words, bracketed paste, focus reporting, the title stack, all three
colour depths, the editing corner cases against a column ruler, and the cursor shapes.

**Run it first.** It tells you whether the sequences work at all; the SSH pass below then tells you
whether real programs are happy with them. A failure here is a defect; a failure below with this
passing is usually a decision.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M2.1 — vim

**Needs:** a real Linux or BSD host over SSH.
**Do:** open a big file, scroll, `:split`, visual-select, `:q`.
**Pass:** the alternate screen is used — quitting restores the shell exactly as it was, with the
scrollback intact and not one line of vim in it.
**Machine cover:** `M8_2b_TheAlternateScreenAddsNothingToTheScrollback` and `AlternateScreenTests`
pin the buffer behaviour; `SavedCursorPerScreenTests` pins the one that shipped wrong — each screen
owns its own DECSC slot, and there used to be only one, so the alternate screen's save destroyed the
main screen's.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M2.2 — The mouse in vim

**Needs:** the same host.
**Do:** click in the text, drag to select, roll the wheel.
**Pass:** the cursor lands where you clicked; the drag selects **inside vim**; the wheel scrolls
vim's view rather than our scrollback.
**Why this one matters most:** mouse reporting is the largest surface in the program with no
automated end-to-end cover. The wire format is tested; whether vim is happy with it is not.
**Machine cover:** `MousePointerWiringTests` drives real clicks on the real canvas and asserts the
bytes, in both encodings. **`MouseWheelWiringTests`, added 28 August 2026, covers the wheel** — which
had no cover at all while carrying more rules than the buttons do. It pins all three claimants and
the order they are asked in: Control plus wheel zooms even while the host is tracking, a tracking
host gets the wheel, and Shift forces our own scrollback so it is never out of reach. All three
failures look identical from the outside — "the wheel did nothing" or "the wheel did the wrong
thing" — with no error anywhere, which is why they needed pinning.

Neither file can ask vim what it made of the bytes. That is what is left.
**Judge:** eye.
**Result:** PASS, driven live 31 August 2026 against a real Ubuntu vim. First attempt used the stored `ubuntu18` connection (VT100), and nothing happened - correctly: the trace showed vim NEVER sent a mouse-tracking enable sequence, because `TERM=vt100` has no mouse capability in terminfo, so there was genuinely nothing to report to. Not a defect - a test-setup gap. Retried on a new `ubuntu18-xterm` connection (`TERM=xterm`); the trace confirmed vim sent `ESC[?1006;1000h` and `ESC[?1002h` for real this time. Ronny then drove it himself: the click landed the cursor, the drag selected inside vim - confirmed by vim's own `-- VISUAL --` indicator, the strongest signal available - and the wheel scrolled vim's view.  **Date:** 2026-08-31  **By:** Ronny

---

## M2.3 — Shift while vim is tracking the mouse — **a decision, not a fact**

**Do:** hold Shift and drag inside vim while it is tracking.
**What happens now:** the gesture stays local and selects text on our side, so text can still be
copied out of a full-screen program.
**This follows xterm and PuTTY, and it is a choice.** No document obliges it. **Ronny judges it**,
and whatever he decides is what the code should say — the current behaviour is written down in
`MousePointerWiringTests.ShiftKeepsTheGestureForSelection` so that changing it is a deliberate act.
**Judge:** Ronny.
**Decision:** KEEP - Shift+drag stays local, confirmed 31 August 2026 on the real ubuntu18-xterm connection while vim was genuinely tracking the mouse (see M2.2). Matches xterm and PuTTY's own convention, and it is the only way to select and copy text out of a full-screen program without disabling mouse tracking first.  **Date:** 2026-08-31

---

## M2.4 — tmux and htop

**Do:** split panes, run `htop` in one, resize the window.
**Pass:** both panes redraw correctly; no stale text at a pane border.
**Expect, and do not report as a defect:** the programs are never told the window changed size —
see `docs\manual-tests\M8-WHOLE-TERMINAL.md` §M8.1, which records that gap
with the evidence.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M2.5 — less, git and ls

| Do | Pass |
|---|---|
| `less` on a long file: page up and down, `/search`, `q` | Same alternate-screen restore as vim |
| `git log --color`, `ls --color` | Colours right, and 256-colour themes not collapsed to 16 |
| `printf '\033]0;hello\007'` | The tab title changes to `hello` |

**Machine cover:** the OSC title tests, and the colour depth tests. The 256-colour ramp is also
rendered as `colour-red-blue-yellow.png`.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## M2.6 — Bracketed paste — **ten seconds, and it closes an open item**

**Needs:** a real shell.
**Do:** copy a **multi-line** command and paste it into bash.
**Pass:** it appears on one line and **does not run** until you press Enter.
**Why it is called out:** it is item 1 of
`docs\NEEDS-A-REAL-HOST-2026-08-11.md`, it costs one paste, and until it is
done the feature is untested outside unit tests.
**Machine cover:** `BracketedPasteAndFocusTests`, including the case that matters for safety — a
paste containing the end-of-paste marker must not be able to end its own bracket and run the rest
as commands.
**Judge:** eye. **This is the cheapest open item in the whole plan.**
**Result:** PASS, driven live over MCP against a real Ubuntu shell on 31 August 2026. A three-line paste (`echo LINE-ONE` / `echo LINE-TWO` / `echo LINE-THREE`) held as unrun text across all three lines - Ronny confirmed on his own screen - and ran only after Enter, all three together in order. First try was confounded by a Sixel picture left on screen from an earlier VT340 test (the graphics-plane-doesn't-clear gap, see PLAN.md); a fresh VT100 session settled it cleanly. The cheapest open item in the plan is CLOSED.  **Date:** 2026-08-31  **By:** Ronny

**Do:** with mode 1004 on, click away to another window and back, watching `cat -v`.
**Pass:** the host sees focus in and focus out.
**Machine cover:** `FocusReportingTests` drives real focus events on the real control.
**Judge:** eye.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **Whether real programs are happy** — that is the whole point of it, and it needs a real host.
- **M2.3 is a decision** and stays open until Ronny makes it.
- **Resize is not reported to the host** (M2.4), recorded in M8.1.
