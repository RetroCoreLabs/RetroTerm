# Manual test plan — one section per phase of the finish plan

**Full path:** `docs\MANUAL-TEST-PLAN-2026-08-17.md`
**Belongs to:** `docs\FINISH-PLAN-2026-08-11.md` — every phase there links to its
section here.

The automated suite proves the emulators match documents and corpora. It cannot prove they are
usable, and it never drives them from a program that is *trying to use a terminal*. This document is
the by-hand pass: what to run, what host or program is needed, the exact steps, and what a pass
looks like.

**This document is the overview.** The detailed by-hand documents — exact keystrokes, exact bytes,
what a pass looks like, and per case whether a machine already checks it — live in
`docs\manual-tests\`, indexed by
`docs\manual-tests\INDEX.md`. Sections that have one say so below.

**Only phases whose code is validated as working get a manual pass here.** Phases that are still
blocked on a document or on missing code (P4.1 printer, P7 IBM 3270) have no section — there is
nothing to exercise yet.

---

## What is NOT verified in this document

Stated up front so nothing here is read as fact:

- **`vttest` is not installed on this machine, and I have not checked whether it is.** Every step
  that names it is written on the basis that it is the standard DEC exerciser and is packaged for
  Debian/Ubuntu (`apt install vttest`) and available in source from invisible-island.net. **Assumption.**
- **Whether the ND hosts are up.** `terminal_connlist` holds D100 (localhost:9010), D102
  (localhost:9102), the 5000 CPU (localhost:4500) and OPCOM. Whether any is running today is a thing
  to check, not to assume.
- **`img2sixel` / `lsix` availability** on whatever host is used for M6. **Assumption.**
- Every "expected" line below is what the *documents* say should happen. Where the real terminal is
  the judge and no document settles it, the step says so and asks for Ronny's eyes.

---

## M0 — Setup, done once before any section

| Step | Command |
|---|---|
| Build the app | `dotnet publish src\RetroTerm.Desktop\RetroTerm.Desktop.csproj -c Release -r win-x64` |
| Binary | `publish\RetroTerm.Desktop.exe` |
| Start the exerciser | `dotnet run --project tests\RetroTerm.TestServer\RetroTerm.TestServer.csproj` |
| Its main menu | 1 Standard (VT100/ANSI) · 2 TDV (when a TDV connects) · **3 DEC** · **4 xterm** · **5 Graphics** · I Terminal information |
| Connect | New Tab, host `127.0.0.1`, the TestServer's port, and **pick the terminal under test in the dropdown** |

**Record everything.** Two facilities exist and both should be on for a manual pass, because a
defect seen once by eye is worth nothing without the bytes that caused it:

- `terminal_logstart` — a transcript of the session.
- `terminal_tracestart` / `terminal_traceread` — byte level, which is what you want when a screen
  goes wrong and you need to know what the host actually sent.
- `terminal_snapshot` / `terminal_readscreen` — the rendered screen as text, to paste into a bug note.

**Fill this in per section.** A manual pass with no written result is a manual pass that will be run
again from scratch next month.

| Section | Date | Terminal | Result | Notes |
|---|---|---|---|---|
| | | | | |

---

## M1 — ANSI / VT100 core  (backs P0)

**Detailed document:** `docs\manual-tests\M1-ANSI-AND-VT100.md`.

**Evidence today:** outside artifacts (xterm.js screens, libvterm callbacks). Strongest area in the
program. This pass is about whether it *behaves* as well as it *matches*.

**What you need:** the TestServer only. No outside host.

**Steps** — connect as `VT100`, then as `ANSI`, and run the whole *Standard Tests* menu:

| Menu | Test | Pass looks like |
|---|---|---|
| 1 | Basic Colors | Eight foreground and eight background colours, all distinct, none bleeding into the next cell |
| 2 | Cursor Movement | The cursor lands exactly where the text says it should, including at the four corners |
| 3 | Character Attributes | Bold, underline, blink, reverse, and combinations, each clearly different from plain |
| 4 | Scrolling Test | Text scrolls smoothly with no torn or duplicated row at the top or bottom |
| 5 | Line Drawing Characters | A box with unbroken corners and joins — gaps here mean the DEC graphics set is mapped wrong |
| 6 | Screen Clear Test | Nothing left behind, and the scrollback is only affected where the test says so |
| 7 | 256 Color Test | A smooth ramp, no banding that looks like duplicate entries |
| 8 | Tab Stops (HT, HTS, TBC) | Columns line up at 8, custom stops honoured, a cleared stop skipped |
| 9 | Character Sets (G0-G3) | Switching sets changes the glyphs and switching back restores them |
| A | Scrolling Region (DECSTBM) | Only the region scrolls; text above and below it stays still |
| B | Cursor Save/Restore | The cursor returns to the saved spot **with its attributes** |
| D | VT100 Key Decoder | Every key you press prints the sequence the VT100 manual gives |

**Then the harder one, needing an outside program:** `vttest` from a Linux box or WSL over
telnet/SSH. Menus 1 (cursor movement), 2 (screen features) and 3 (character sets) are the classic
DEC self-test and they exercise things the TestServer does not. **Assumption: vttest is available.**

**Pass:** every screen matches vttest's own printed description. vttest tells you on screen what you
should be seeing, which is why it is worth the setup.

---

## M2 — xterm / xterm-256color  (backs P1.4)

**Detailed document:** `docs\manual-tests\M2-XTERM.md`, which carries the two open
decisions - Shift while a program tracks the mouse, and the resize gap.

**Evidence today:** `ctlseqs.txt` is held, but only modes 1004, 1006 and 2004 have been walked
against it. This pass is the one that finds what a real program needs and we do not do.

**What you need:** a **real Linux or BSD host over SSH**. The point is a program that is genuinely
driving the terminal, which no test server can imitate.

**First, though**, main menu option **4** in the TestServer now covers the same ground mechanically —
alternate screen, mouse reporting in both encodings with every event decoded into words, bracketed
paste, focus reporting, the title stack, all three colour depths, the editing corner cases against a
column ruler, and the cursor shapes. Run that first: it tells you whether the sequences work at all,
and then the SSH pass below tells you whether real programs are happy.

| # | Program | Do this | Pass looks like |
|---|---|---|---|
| M2.1 | `vim` | Open a big file, scroll, `:split`, visual-select, `:q` | The alternate screen is used — quitting restores the shell exactly as it was, scrollback intact |
| M2.2 | `vim` with the mouse | Click in the text, drag to select, roll the wheel | Cursor lands where you clicked; drag selects **inside vim**; the wheel scrolls vim's view, not our scrollback |
| M2.3 | `vim`, holding **Shift** | Shift-drag while vim is tracking the mouse | Local text selection wins. **This is a decision, not a fact** — it follows xterm/PuTTY. Ronny judges it |
| M2.4 | `tmux` + `htop` | Split panes, resize the window | Both panes redraw correctly after the resize; no stale text at a pane border |
| M2.5 | `less` on a long file | Page up and down, `/search`, `q` | Same alternate-screen restore as vim |
| M2.6 | `git log --color`, `ls --color` | Just look | Colours right, and 256-colour themes not collapsed to 16 |
| M2.7 | Bracketed paste | Paste a **multi-line** command into bash | It appears on one line and **does not run** until you press Enter. Ten seconds, and it settles `NEEDS-A-REAL-HOST` item 1 |
| M2.8 | Window title | `printf '\033]0;hello\007'` | The tab title changes to `hello` |
| M2.9 | Focus reporting | Click away to another window and back with mode 1004 on | The host sees focus in/out — visible in `cat -v` |

---

## M3 — DEC VT220 / VT320 / VT340 / VT420  (backs P1.1, P1.2, P1.3)

**Detailed document:** `docs\manual-tests\M3-DEC-TERMINALS.md`, with a generated
sheet of what every one of the fourteen terminals answers to eight questions.

**Evidence today:** inferred, except the VT420 manual which is held but not yet walked. This is the
weakest area and the manual pass matters most here.

**M3.0 is done** — the TestServer now has a **DEC Terminal Tests** branch, main menu option **3**, so
this section needs no outside program:

| Key | Test | What it drives |
|---|---|---|
| 1 | Reports and Identity | DA1/DA2/DA3, DSR, CPR, DECXCPR, four DECRQSS requests including an invalid one, DECRQM. Every reply is printed as text **and as hex** beside its expected shape |
| 2 | Page Memory | DECSLPP, then NP, PP, PPA, PPR, PPB across three written pages |
| 3 | Rectangle Operations | DECFRA, DECCRA including an **overlapping** copy, DECERA, DECSERA, DECRQCRA |
| 4 | Margins and Column Editing | DECLRMM, DECSLRM, DECIC/DECDC, SL/SR, and origin mode against the left margin |
| 5 | Soft Font and User Keys | DECDLD in the 96- **and** 94-character forms, then DECUDK on F1 and F2 |
| 6 | Status Line | DECSSDT, DECSASD |
| 7 | National Character Sets | Ten NRCS sets one per row, Norwegian/Danish included |
| 8 | Conformance Levels | DECSCL 61–65, each re-queried with DA |
| 9 | VT52 Mode | Enter, ESC Y addressing, graphics mode, ESC Z identity, and back |
| A | Selective Erase | DECSCA, then DECSEL and DECSED sparing what is protected |

**Two of those probe features that do not exist yet.** Verified 2026-08-17 by searching the source:
**DECRQCRA** has no handler and the **status line** (DECSASD/DECSSDT) is not implemented at all. Both
are in the menu deliberately and say so on screen — nothing happening there is the known state, not
a defect you have just found.

`vttest` stays as the second opinion where it is available: menu 6 (reports), 7 (VT52), 9 (VT220 and
up), 11 (VT420 rectangles and margins). **Assumption: vttest is available.**

| # | Terminal | Check | Pass looks like |
|---|---|---|---|
| M3.1 | VT220 | vttest menu 9 — reports, DECUDK, user-defined keys | The DA reply names a VT220 and the reports match the menu's own text |
| M3.2 | VT320 | Page memory — `NP`, `PP`, `PPA`, and `DECSLPP` for page length | Paging moves between pages and the text on each survives |
| M3.3 | VT340 | Soft fonts (DECDLD), status line, character sets, against Ronny's OCR | Downloaded glyphs appear where the manual says, and are legible at your font size — **your eyes, not a test** |
| M3.4 | VT420 | vttest menu 11 — rectangle fill/copy/erase, left/right margins | Rectangles land on the coordinates the manual gives; a copy that overlaps its source is not smeared |
| M3.5 | All | DECSCL conformance level switching | Setting a lower level makes the higher-level sequences stop working |
| M3.6 | VT52 | vttest menu 7, and a VT100 dropped into VT52 with `ESC [ ? 2 l` | VT52's short sequences work, and `ESC <` gets you back |

---

## M4 — TDV1200 / 2215 / 2200  (validated: Document)

**Detailed document:** `docs\manual-tests\M4-TDV.md`, which carries the
generated key sheet - every key and the bytes it sends, with a box to tick against real hardware.

**Evidence today:** the strongest in the program — OCR'd Tandberg manuals, a TDV2215 ROM dump, fonts
from real bitmaps. Two hosts are worth using.

**Part A — the TestServer**, connect as each TDV in turn and walk the *TDV Terminal Tests* menu:

| Menu | Test | Pass looks like |
|---|---|---|
| 1 | Query/Response (DA, CPR, DSR) | The reply string matches the manual's worked example |
| 2 | Character Sets | Graphics I/II, Math, Greek switch and switch back |
| 3 | Drawing Operations | Rectangle insert/delete land on the right cells |
| 4 | Function Keys | F1–F12 produce the TDV codes, **not** VT220 ones — F1 must be HJELP (`ESC[46_`) |
| 5 | Modes & Features | Smooth scroll, blink, the message LEDs |
| 6 | Key Detection | Every submenu: PUSH keys, soft keys, control keys, 2115 C0 codes, arrows, modifiers, extended control mode, numpad |
| 7/8/9 | Per-model tests | 2115 compatibility, ND graphics, protected areas, transparent mode, DCS PUSH-key programming, graphics extension, Tektronix mode, ISO 646 variants |
| A | Comprehensive Demo | The full feature demo with nothing visibly wrong |

**Part B — a real ND machine** through the retroterm MCP (`terminal_connlist` for the ports; open
once and keep the session; ESC first on a fresh SINTRAN line; log in with one send). Log in to
SINTRAN, run a normal session, and watch for anything the TestServer never produces.

**Pass:** arrows send the C0 codes (UP 0x1C, DOWN 0x0B, LEFT 0x08, RIGHT 0x18, HOME 0x1D), Tab sends
`ESC[16_`, and no key falls back to a VT220 sequence.

**Still needs hardware, and cannot be closed here:** the two ND model identification bytes, and the
real TDV keyboard. Recorded in `NEEDS-A-REAL-HOST` §1.

---

## M5 — Tektronix 4010 / 4014  (backs P3)

**Detailed document:** `docs\manual-tests\M5-TEKTRONIX.md`.

**Evidence today:** the 4014 manual plus three genuine gnuplot streams. Plots render and the corpus
is clean.

**What you need:** any host with **gnuplot** — that is the program that exercises this for real.

| # | Step | Pass looks like |
|---|---|---|
| M5.1 | `gnuplot`, `set term tek40xx`, `plot sin(x)` | A curve with axes, border and labels, all in place |
| M5.2 | Labels specifically | Axis labels sit **at** their ticks — a diagonal cascade of labels is the defect that already shipped once |
| M5.3 | `plot` a second time without clearing | The old plot is still there under the new one until an explicit page erase |
| M5.4 | Leave graph mode and type | Text starts **at the last plotted point** — this is derived, not quoted, and gnuplot depends on it |
| M5.5 | TDV2200 → Tektronix Mode from the TestServer menu | 4010 compatibility works from the TDV side too |
| M5.6 | Phosphor | Switch the theme to amber. **Vectors must go amber with the text.** A real single-phosphor machine cannot draw green lines on an amber screen |

---

## M6 — Sixel and ReGIS graphics  (backs P2)

**Detailed document:** `docs\manual-tests\M6-SIXEL-AND-REGIS.md`, which pairs every
fixture with its real-hardware capture on one sheet.

**Evidence today:** none from real hardware. This section is the *only* human check that exists for
Sixel and ReGIS until P2 lands.

**Use the TestServer first** — main menu option **5** draws all of this without any outside program:
three-colour Sixel bars, a grey ramp with a square for aspect, Sixel mixed with text, a ReGIS
drawing, two Tektronix plots and a phosphor check. The three-bar image is pinned by an automated
test (`TestServerNewSuiteTests`) that decodes it and asserts red on the left and blue on the right,
so if the screen disagrees with the menu the fault is in the renderer, not in the test data.

**Then, for real images:** a host with `img2sixel` (libsixel) or `lsix`. **Assumption: available.**
Failing that, `cat` a `.six` file straight to the terminal — vt340test's files are fetched by
`tools\fetch-conformance-corpora.ps1` once P2.1 adds them.

| # | Step | Pass looks like |
|---|---|---|
| M6.1 | `img2sixel photo.png` on a **three-colour** image | Colours correct. A two-colour image hides a red/blue swap — that is exactly how the last one survived for months |
| M6.2 | A tall image | Scrolls correctly, and text after it starts below the image |
| M6.3 | Sixel next to text | Text and graphics on the same screen, neither erasing the other |
| M6.4 | ReGIS | Judgement suspended — how complete `RegisDecoder` is has not been written down. **P2.4 first** |

---

## M7 — ND `ESC "` graphics  (backs P4.2, and this IS P5.3)

**Detailed document:** `docs\manual-tests\M7-NORSK-DATA.md`.

**Evidence today:** 6 of 30 modes implemented; the other 24 are counted, not guessed.

**What you need:** a real ND host. Nothing else can do this.

1. Open a session against a real ND machine through the retroterm MCP.
2. Do normal graphics work — whatever a real user does on it.
3. Read `NorskDataGraphicsModule.UnhandledSequences` (keyed `mode:final`).

**Pass:** you come away with a **list** of the modes that actually get used. That single reading
turns the largest remaining ND item from guesswork into a work list, which is why it sits third in
the `NEEDS-A-REAL-HOST` order.

While connected, also settle the two assumptions marked in the code: whether mode 8's rectangle-fill
coordinates are in logical space, and whether defining a circle (mode 24) also draws it.

---

## M8 — The terminal as a whole, whatever the emulation

**Detailed document:** `docs\manual-tests\M8-WHOLE-TERMINAL.md`. Run that
rather than the table below, which is kept only as the summary. Two rows here are wrong and the
detailed document says why: the host is **not** told about a resize, and there are **fourteen**
terminals in the list, not fifteen.

Run once per release, on whichever terminal you use most.

| # | Check | Pass looks like |
|---|---|---|
| M8.1 | Resize the window while a full-screen program runs | Redraws cleanly; the host is told the new size |
| M8.2 | Scrollback | Scrolls back over a long build log; the alternate screen does not pollute it |
| M8.3 | Copy and paste both ways | Selection matches what is on screen, including over a wrapped line |
| M8.4 | The bell | Audible, immediate, and not a "bup". **Ronny's ears — no test in this repo can judge it** |
| M8.5 | Every terminal in the New Tab dropdown | All fifteen connect and are actually that terminal, not silently a VT100 |
| M8.6 | Long idle, then traffic | No stuck rendering, no runaway CPU |

---

## Rules for a manual pass

- **Write the result down in the table in M0.** An unrecorded pass will be re-run from scratch.
- **Turn the trace on before you start**, not after something goes wrong.
- When a screen looks wrong, capture **the bytes** (`terminal_traceread`) as well as the screen
  (`terminal_snapshot`). A screenshot alone cannot be turned into a test.
- Anything a document settles becomes an automated test the same day; the manual pass exists to
  **find** those, not to replace them.
- Anything no document settles goes in `docs\NEEDS-A-REAL-HOST-2026-08-11.md` with the reason.
