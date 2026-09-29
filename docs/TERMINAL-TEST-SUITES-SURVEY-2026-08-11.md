# Terminal test suites and conformance corpora — survey for RetroTerm

Date: 2026-08-11
Consumer: RetroTerm (C# / .NET 10), emulating DEC VT52/VT100/VT102/VT220/VT240/VT320/VT340/VT420,
xterm, Tektronix 4014, and Tandberg TDV.

## How to read this document

Every entry is marked with one of:

- **VERIFIED** — I fetched the URL (or downloaded and unpacked the archive) and read the actual
  content. File names, counts and licence text below are copied from what I read.
- **NOT FOUND** — I looked and the thing was not there. What I tried is written down.
- **UNVERIFIED** — I could not fetch it. Marked explicitly; nothing described from memory.

Nothing in this document is written from recollection. Where I could not confirm something,
it says so.

Counts come from the GitHub contents API or from `tar tzf` on the downloaded tarball, on
2026-08-11. They will drift as upstream changes.

---

## Summary table

| Suite | Licence | Form | Needs live terminal? | Overlaps vendored libvterm? | Effort |
|---|---|---|---|---|---|
| xterm.js `escape_sequence_files` | MIT | byte stream + expected screen text | **No** | Partly | **Low** |
| vt340test | CC0-1.0 | `.six` / ReGIS byte streams + real-hardware PNGs | No | **No** | Low–Medium |
| Alacritty ref tests | Apache-2.0 | raw recording + Alacritty-shaped grid JSON | No | Partly | Medium |
| vttest | X11/MIT + BSD-3 | interactive C program | Yes (or via `-l`/`-c`) | Partly (8 files) | Medium |
| MarkLodato/vt100-parser | MIT | byte stream + expected screen text | No | Partly | Low (subset) |
| esctest2 | **GPL-2.0** | Python, live terminal | **Yes** | Partly | High |
| Microsoft Terminal parser/adapter tests | MIT | C++ TAEF unit tests | No | Partly | High |
| wezterm `term/src/test` | MIT | inline Rust `#[test]` | No | Yes | High |
| kitty `kitty_tests` | **GPL-3.0** | Python, live kitty | Yes | Partly | High |
| neovim terminal tests | NOASSERTION | Lua busted specs | Yes (PTY) | Yes | High |
| ncurses `test/` | MIT-style | interactive C demo programs | Yes | No | High |
| terminfo.dev | not stated on page | live-terminal Playwright harness | Yes | No | High |
| Ghostty conformance dir | — | **NOT FOUND** | — | — | — |

---

## 1. xterm.js escape-sequence fixtures — VERIFIED

**URL fetched:** `https://api.github.com/repos/xtermjs/xterm.js/contents/test/fixtures/escape_sequence_files`
Browsable at `https://github.com/xtermjs/xterm.js/tree/master/test/fixtures/escape_sequence_files`

### What it actually contains

162 files in one flat directory. Broken down by what I counted:

- **76 `.in` files** — raw byte streams, escape sequences included, no wrapper format at all.
- **77 `.text` files** — the expected screen contents as plain text, one line per screen row.
- One `.text` has no matching `.in`: **`t0031-HPB.text`**. The `NOTES` file explains why (see below).
- `NOTES` — 633 bytes.
- `run_tests.py` — 2313 bytes.

Verified sample. `t0010-RI.in`, shown with `cat -v`:

```
a
b
c
d^[Me^[Mf^[Mg
h
i
j....................................................................k^[Ml^[Mm^[Mn
```

and the matching `t0010-RI.text`:

```
a  g                                                                    n
h f                                                                    m
ie                                                                    l
j....................................................................k
```

That is the whole format. Feed the `.in` bytes to the emulator, dump the screen as text,
compare to `.text`.

`NOTES` quoted verbatim:

> All tests are made for 80x25 terminal. Make sure to run tests with 80x25.
>
> Create .text files from xterm (expected output)
> - open xterm
> - resize xterm to 80x25
> - run `python run_tests.py`
> - copy & paste whole window output into editor
> - add 26th empty line (due to line handling in toString) - not a bug, a feature ;)
> - advance to next test with ^D
>
> Known problems
> ##############
>
> t0031-HBP:
>     - no documentation at all about CSIj found - skipping
>
> t0050-ICH:
>     - bug in xterm? (cant ICH last real char, always sticks to last col)
>     - text used from https://github.com/MarkLodato/vt100-parser/blob/master/test/t0050-ICH.text

Two things worth taking from that note. First, **the expected output was captured from real
xterm**, so this is an oracle against a real implementation, not somebody's opinion. Second,
`t0031-HPB` was deliberately skipped — that explains the orphan `.text` file, it is not corruption.

### Full list of test names (from the `.in` files)

t0001-all_printable, t0002-history, t0002j-simple_string, t0003-line_wrap, t0003j-LF, t0004-LF,
t0004j-CR, t0005-CR, t0006-IND, t0007-space_at_end, t0008-BS, t0009-NEL, t0010-RI, t0011-RI_scroll,
t0012-VT, t0013-FF, t0014-CAN, t0015-SUB, t0016-SU, t0017-SD, t0020-CUF, t0021-CUB, t0022-CUU,
t0023-CUU_scroll, t0024-CUD, t0025-CUP, t0026-CNL, t0027-CPL, t0030-HPR, t0032-VPB, t0033-VPB_scroll,
t0034-VPR, t0035-HVP, t0040-REP, t0050-ICH, t0051-IL, t0052-DL, t0053-DCH, t0054-ECH, t0055-EL,
t0056-ED, t0057-ED3, t0060-DECSC, t0061-CSI_s, t0070-DECSTBM_LF, t0071-DECSTBM_IND, t0072-DECSTBM_NEL,
t0073-DECSTBM_RI, t0074-DECSTBM_SU_SD, t0075-DECSTBM_CUU_CUD, t0076-DECSTBM_IL_DL, t0077-DECSTBM_quirks,
t0078-DECSTBM_CPL_CNL, t0079-DECSTBM_VPR, t0080-HT, t0081-TBC, t0082-HTS, t0083-CHT, t0084-CBT,
t0090-alt_screen, t0091-alt_screen_ED3, t0092-alt_screen_DECSC, t0100-IRM, t0101-NLM, t0102-DECAWM,
t0103-reverse_wrap, t0300-vttest1, t0500-bash_long_line, t0501-bash_ls, t0502-bash_ls_color,
t0503-zsh_ls_color, t0504-vim, t600-DECSTBM_SR, t601-DECSTBM_SL, t602-DECSTBM_DECIC, t603-DECSTBM_DECDC

### Licence

The repo licence reported by the GitHub API is **MIT** (`.license.spdx_id` = `MIT`). I did not find
a separate licence file inside the fixtures directory — the fixtures inherit the repo licence.
Note the `NOTES` file credits one `.text` file to MarkLodato/vt100-parser, which is also MIT.

### Coverage

C0 controls, cursor movement, scrolling regions (DECSTBM, heavily — 12 separate tests including a
`_quirks` one), insert/delete char and line, erase, tabs, alt screen, IRM/NLM/DECAWM, reverse wrap,
REP, and the VT420 left/right margin ops (DECIC/DECDC, SL/SR). Plus four "real program" captures:
bash long lines, `ls`, `ls --color`, zsh `ls --color`, and vim.

No graphics. No mouse. No character sets. No VT52. No Tektronix. No responses/queries — the
format has no way to express "terminal replies with X".

### How it is run

`run_tests.py` is a live-terminal harness — it imports `termios`, turns off echo on
`sys.stdin.fileno()`, writes `\x1bc\x1b[H` to reset, and globs `*.in`. **You do not need it.**
The `.in`/`.text` pairs are self-contained data files. The harness only exists because that is how
the `.text` files were captured from xterm in the first place.

### Practical value and effort — LOW effort, highest value

This is the closest thing to a drop-in corpus for RetroTerm that exists. The runner is maybe 40
lines of C#: read `.in` as bytes, `emulator.ProcessData(bytes)`, dump the 80x25 buffer to strings,
compare against `.text` lines with trailing-space trimming. It needs no PTY, no Python, no live
terminal, and it slots straight into the existing `Conformance/` folder pattern next to the
libvterm runner.

The one gotcha to plan for: the expected screens are 80x25 and trailing whitespace handling is
fiddly (the NOTES mention a deliberate extra 26th line for their own `toString`). Compare
right-trimmed lines and ignore the trailing blank.

### Overlap with vendored libvterm

Partial and complementary rather than duplicated. libvterm's `.test` files assert on *internal
callbacks* — `?cursor = 0,3`, putglyph calls, damage rectangles. These fixtures assert on the
*final visible screen*. The same feature (say DECSTBM) is tested by both, but they catch different
mistakes: libvterm catches a wrong intermediate callback, this catches a screen that ends up wrong.
libvterm has no equivalent of the bash/vim/zsh real-session captures at all.

---

## 2. vt340test — VERIFIED

**URL fetched:** `https://api.github.com/repos/hackerb9/vt340test/contents` and the `sixeltests`,
`regis` and `LICENSE` paths. Browsable at `https://github.com/hackerb9/vt340test`

### What it actually contains

Top-level directories and files I listed: `acschars.c`, `apl`, `basics.md`, `charset`, `checkmode`,
`checkmode.md`, `chkwinch.sh`, `colormap`, `dalatency.sh`, `docs`, `emulators`, `errata.md`,
`flowcontrol.md`, `glitches.md`, `j4james`, `jerch`, `kermitdemos`, `keyboard`, `locator`,
`mediacopy`, `mmj.md`, `pagememory.md`, `physicalsixels.md`, `regis`, `scrollspeed.sh`,
`sixeltests`, `testdecsdm.sh`, `testscroll.sh`, `usage`, `vms`, `vttime`.

In `sixeltests/` (first 40 entries listed): `6chars.py`, `6chars.txt`, `8bit.six`, `animation.sh`,
`cat-libsixel.png`, `cat-libsixel.six`, `cat-original.png`, `cat-original.six`, `cat-vt240.png`,
`cat-vt240.six`, `cat-vt340.png`, `cat-vt340.six`, `colorwheel`, `colorwheel+dither.png`,
`colorwheel+dither.six`, `colorwheel.png`, `colorwheel.six`, `comment.six`, `cp16gray.six`,
`decdwl-quirk1.png`, `decdwl-quirk1.sh`, `decdwl-quirk2.png`, `decdwl-quirk2.sh`,
`decdwl-quirk3.gif`, `decdwl-quirk3.sh`, `encoding.md`, `encoding.py`, `enigma.six`,
`extremeratio.png`, `extremeratio.six`, `graphpaper.sh`, `map8.png`, `map8.six`, `multisize.png`,
`multisize.six`, `offscreen.sh`, `p2effect.png`, `p2effect.sh`, `pageflip.sh`,
`resetpalette-sixel.sh`.

The pattern to notice: **`.six` files come paired with `.png` files.** A sixel byte stream and a
picture of what it should look like. Several are named after the hardware that produced them
(`cat-vt340.png`, `cat-vt240.png`), so the reference image is what a real terminal actually drew.

In `regis/`: `colorplanes.md`, `faketextcolor.md/.png/.sh`, `gettingstarted.md`, `greek.fnt`,
`greek.png`, `hersheydemo.regis`, `hls.md`, `interco`, `mdraw`, `ode`, `offsetdirections.png`,
`offsetdirections.svg`, `regis-commands.txt`, `regis-decdwl.png`, `regis-decdwl.sh`,
`registest-bitplane.png`, `registest-checkerboard.png`, `registest-grid.png`, `registest-raf.png`,
`registest.sh`, `resetpalette.regis`, `textcolorsandregis.md`.

### Licence — CC0-1.0

The GitHub API reports `"spdx_id": "CC0-1.0"`. The `LICENSE` file begins:

```
Creative Commons Legal Code

CC0 1.0 Universal
```

CC0 is public-domain dedication. This is the most permissive licence of anything in this survey —
there is no attribution requirement and no copyleft. Vendoring is unencumbered.

### Coverage

Sixel graphics (encoding, colour maps, aspect ratio, DECDWL interaction, scrolling, off-screen
placement, page flipping, palette reset, animation), ReGIS graphics, VT340 character sets and soft
fonts, colour lookup table behaviour, the locator (mouse) protocol, media copy (print), page
memory, and flow control. The repo README (per the search result, which I did not fetch directly —
see caveat below) describes it as tests run against a real VT340+ terminal.

**Caveat, stated plainly:** I listed the repository contents through the API and read the LICENSE.
I did *not* fetch and read `README.md`, so the description of the author's intent above comes from
a web search snippet, not from reading the file. The file listing itself is verified.

### How it is run

The `.sh` files are shell scripts that `printf` escape sequences at a live terminal. But the `.six`
and `.regis` files are **plain byte streams on disk** — no harness required. And the `.png` files
give you an expected result to compare against.

### Practical value and effort — LOW to MEDIUM

RetroTerm already renders Sixel (the CLAUDE.md notes a three-colour Sixel image caught the
red/blue byte-order defect). This repo turns that from "one image somebody made" into a real
corpus, with reference pictures taken from actual DEC hardware.

The low-effort slice: vendor the `.six` files, feed each one, render, and write the PNG out to
`tests\RetroTerm.Tests\Avalonia\images\rendered\`. Then *look at them* next to the reference PNGs.
That alone would be worth doing, and it matches the "assert on the pixels — and then LOOK at them"
rule already in CLAUDE.md.

Medium effort begins if you want automated pixel comparison against the reference PNGs. Those were
captured from a physical CRT in some cases, so exact-match will not work; you would need a
perceptual or downsampled comparison, or hand-picked spot checks (`BrightestColorInCell` on chosen
cells, which RetroTerm already has).

ReGIS is a separate question — RetroTerm's emulator list includes VT340, and ReGIS is a VT340
feature, but I did not check whether RetroTerm implements ReGIS at all. If it does not, that part
of the repo is a specification to build against rather than a test corpus.

### Overlap with vendored libvterm — NONE

libvterm has no graphics tests whatsoever. Its 43 files are all parser, state, and screen text.
This is entirely new ground.

---

## 3. Alacritty reference tests — VERIFIED

**URL fetched:** `https://api.github.com/repos/alacritty/alacritty/contents/alacritty_terminal/tests/ref`
and the `ref.rs` harness. Browsable at
`https://github.com/alacritty/alacritty/tree/master/alacritty_terminal/tests`

### What it actually contains

`alacritty_terminal/tests/` holds exactly two entries: `ref.rs` and `ref/`.

`ref/` contains **45 subdirectories**, one per test:

alt_reset, clear_underline, colored_reset, colored_underline, csi_rep, decaln_reset, deccolm_reset,
delete_chars_reset, delete_lines, erase_chars_reset, erase_in_line, fish_cc, grid_reset, history,
hyperlinks, indexed_256_colors, insert_blank_reset, issue_855, ll,
newline_with_cursor_beyond_scroll_region, origin_goto, region_scroll_down, row_reset, saved_cursor,
saved_cursor_alt, scroll_in_region_up_preserves_history, scroll_up_reset, selective_erasure, sgr,
tab_rendering, tmux_git_log, tmux_htop, underline, vim_24bitcolors_bce, vim_large_window_scroll,
vim_simple_edit, vttest_cursor_movement_1, vttest_insert, vttest_origin_mode_1, vttest_origin_mode_2,
vttest_scroll, vttest_tab_clear_set, wrapline_alt_toggle, zerowidth, zsh_tab_completion.

(45 directories; the `ref_tests!` macro in `ref.rs` lists 40 of them in the portion I read, so a
few directories are not currently wired into the macro — I read the first 70 lines of `ref.rs` and
the list was truncated there, so I cannot say which.)

Each directory holds four files. Verified against `ref/vttest_scroll/`:

- `alacritty.recording` — raw byte stream. Confirmed by decoding it; it starts with a real shell
  prompt with SGR sequences, an OSC 2 title set, then vttest launching:
  ```
  ^[]2;vttest^G^[]1;vttest^G^[[0c^[[?1l^[[?3l^[[?4l^[[?5l^[[?6l^[[?7h^[[?8l^[[?40h^[[?45l^[[r^[[0m^[[2J^[[3;10HVT100 test program, version 2.7 (20140305)
  ```
  This is a genuine recorded terminal session, warts and all.
- `size.json` — `{"columns":105,"screen_lines":29}`
- `config.json`
- `grid.json` — the expected result, serialised from **Alacritty's own Rust structs**:
  ```json
  {"raw":{"inner":[{"inner":[{"c":" ","fg":{"Named":"Foreground"},"bg":{"Named":"Background"},"flags":"","extra":null}, ...
  ```

`ref.rs` is a Rust harness using serde; the `ref_tests!` macro generates one `#[test]` per
directory name.

### Licence — Apache-2.0

GitHub API reports `"spdx_id": "Apache-2.0"`. Apache-2.0 is permissive but has an attribution and
notice requirement, and a patent grant clause. Compatible with vendoring into a permissively
licensed project; you must keep the licence text and note modifications.

### Coverage

SGR including 256-colour and 24-bit, underline styles and coloured underlines, scrolling regions
and history interaction, DECALN, DECCOLM, origin mode, saved cursor on both screens, selective
erasure, tabs, hyperlinks (OSC 8), zero-width characters, and line wrapping. Plus real captured
sessions of vim, tmux+htop, tmux+git log, fish, zsh completion, and four vttest sub-tests.

No graphics, no mouse, no VT52, no Tektronix, no character sets.

### How it is run

No PTY and no live terminal — the recordings are already captured. Alacritty's own harness is
`cargo test`. The recordings are the reusable part.

### Practical value and effort — MEDIUM

Split this in two.

**The recordings are immediately useful and cost almost nothing.** They are real byte streams from
real programs. RetroTerm could feed each one at the stated size and snapshot its own rendered
screen. That gives a *regression* corpus — it catches "we changed something and 12 screens moved"
— even without adopting Alacritty's expected output.

**The `grid.json` expected output is the expensive part.** It is Alacritty's internal cell struct,
with Alacritty's colour naming (`{"Named":"Foreground"}`), Alacritty's flag encoding, and
Alacritty's grid/history representation. Using it as a true oracle means writing a translator from
that shape into `TerminalCell`, and reconciling every place the two emulators legitimately differ
in representation (how history rows are stored, what "Named Foreground" resolves to). That is real
work and it will produce a stream of differences that are not bugs.

Recommendation: take the recordings, skip the grid.json, and let RetroTerm's own snapshot be the
baseline — which is exactly the pattern already used for the 97 baselined libvterm cases.

### Overlap with vendored libvterm

Partial on the escape-sequence side (scrolling, SGR, cursor save). Zero overlap on the recorded
real-program sessions, which is where the value is.

---

## 4. vttest — VERIFIED (downloaded and unpacked)

**URLs fetched:** `https://invisible-island.net/vttest/` (overview page),
`https://invisible-island.net/vttest/vttest.html`, and the source tarball
`https://invisible-island.net/datafiles/release/vttest.tar.gz` — 243,249 bytes, unpacked to
`vttest-20251205/`.

The overview page describes it as "a test utility that demonstrates the non-compatibility of
so-called 'VT100-compatible' terminals."

### What it actually contains

26 C source files, 20,117 lines total (`wc -l` on `*.c`):

charsets.c, color.c, draw.c, esc.c, keyboard.c, main.c, mouse.c, nonvt100.c, printer.c, replay.c,
reports.c, reset.c, setup.c, sixel.c, status.c, **tek4014.c**, ttymodes.c, unix_io.c, utf8.c,
vms_io.c, vt220.c, vt320.c, vt420.c, vt52.c, vt520.c, xterm.c.

Main menu, read from `main.c` lines 145–154:

```
0. Exit
1. Test of cursor movements
2. Test of screen features
3. Test of character sets
4. Test of double-sized characters
5. Test of keyboard
6. Test of terminal reports
7. Test of VT52 mode
8. Test of VT102 features (Insert/Delete Char/Line)
9. Test of known bugs
10. Test of reset and self-test
```

### Two findings that matter specifically to RetroTerm

**`tek4014.c` exists and is a real Tektronix 4014 test menu.** Read from lines 416–422:

```c
{ "Exit",                                        NULL },
{ "Clear screen",                                tek_clear },
{ "'Hello World!' in each font",                 tek_hello },
{ "Get mouse-clicks, showing coordinates",       tek_mouse_coords },
{ "Get mouse-clicks, drawing lines between",     tek_mouse_lines },
{ "Draw a grid",                                 tek_grid_demo },
```

This is the only Tektronix 4014 test material I found anywhere in this survey. Three of the five
tests (`tek_clear`, `tek_hello`, `tek_grid_demo`) are pure output and need no input at all. Note
the two mouse tests need real clicks and are not usable headlessly.

Also worth knowing: the vttest overview page and `vttest.html` make **no mention of Tektronix**.
The web pages understate what the program does. I only found this by unpacking the source. This is
a case where the documentation would have led you wrong.

**`sixel.c` is NOT a sixel-graphics test.** Despite the filename, its menu (lines 391–394) is:

```c
{ "Download the soft characters (DECDLD)",   tst_DECDLD },
{ "Examine the soft characters, one-by-one", tst_display_one },
{ "Examine all the soft characters",         tst_display_all },
{ "Clear the soft characters",               tst_cleanup },
```

and the only external entry point declared in `vttest.h` is `extern int tst_softchars(MENU_ARGS);`.
It is named for the sixel *encoding* used by DECDLD soft character sets, not for sixel images.
**vttest does not test sixel graphics.** If you want sixel graphics tests, that is vt340test
(entry 2), not vttest.

### Licence

From `package/debian/copyright`, quoted:

```
Files:     * */*
Copyright: 1996-2024,2025 by Thomas E. Dickey
License:   X11 License Distribution Modification Variant
  Permission to use, copy, modify, and distribute this software and its
  documentation for any purpose and without fee is hereby granted,
  provided that the above copyright notice appear in all copies and that
  both that copyright notice and this permission notice appear in
  supporting documentation, and that the name of the above listed
  copyright holder(s) not be used in advertising or publicity pertaining
  to distribution of the software without specific, written prior
  permission.

Files:     main.c
Copyright: 1984 by Per Lindberg
License:   BSD-3-Clause
```

So: X11/MIT-style for the bulk, BSD-3-Clause for `main.c`. Both permissive. The overview page also
confirms the relicensing history — the original 1984 QZ notice said "Non-commercial use and copying
allowed", and the author obtained a BSD relicence in 2007.

### Coverage

The widest of anything here. VT52, VT100, VT102, VT220, VT320, VT420, VT520, xterm extensions,
ISO 6429, character sets and NRCS, double-width/double-height, colour and BCE, rectangle operations,
terminal reports, printer, mouse, UTF-8, status line, DECDLD soft fonts, and Tektronix 4014.
That maps onto RetroTerm's stated emulator list better than any other single suite.

### How it is run — and the escape hatch

Normally: an interactive, menu-driven C program that a human runs against a live terminal and
judges by eye. On the face of it that is useless for automated testing.

But the man page (`vttest.1`, options section, quoted) documents two flags:

```
-c commands
       replay commands recorded by the logging option.
       Some keyboard and mouse input is required, depending on the tests,
       but otherwise menu selection and next-page responses are automated.

-l     log test results to vttest.log.
```

And `replay.c` implements it — `setup_replay(const char *pathname)` opens the file `"rb"`,
`is_replaying()` gates it, with pause and marker support.

So there is a route: run vttest once against something, with `-l`, capture the byte stream it
emits, and vendor that stream as a corpus. Note the caveat in the man page — *some* tests still
need keyboard and mouse input, so this does not automate the whole program.

### Practical value and effort — MEDIUM

The value is high because of breadth and because of the Tektronix menu, which is unique. The effort
is medium because you have to build the corpus yourself rather than pick it up ready-made: compile
vttest (it is autoconf'd C, so on Windows that means WSL, MSYS2, or capturing the stream once on a
Linux box and checking the bytes in), drive it under `-l`, and split the log into per-test streams.
Then RetroTerm feeds those streams and snapshots its own screens.

There is a cheaper shortcut for the Tektronix part specifically: `tek4014.c` is 400-odd lines of C
that mostly `printf`s a known sequence. Reading it and hand-writing the equivalent byte streams as
xUnit test data is a couple of hours and needs no build at all. Given RetroTerm just did
Tektronix 4014 work (the recent commits mention 4014 five-byte addresses and incremental plot),
this is the most directly relevant thing in the survey after entry 1.

### Overlap with vendored libvterm

The vendored corpus already contains **8 vttest-derived files**: `90vttest_01-movement-1` through
`-4` and `90vttest_02-screen-1` through `-4`. So vttest menu items 1 and 2 are already covered,
in libvterm's callback-assertion form. Menu items 3–10 and the whole Tektronix menu are **not**
covered by anything RetroTerm currently has.

---

## 5. MarkLodato/vt100-parser — VERIFIED

**URL fetched:** `https://api.github.com/repos/MarkLodato/vt100-parser` and its `contents` and
`contents/test`. Browsable at `https://github.com/MarkLodato/vt100-parser`

### What it actually contains

Repo root: `CHANGELOG`, `LICENSE`, `Makefile`, `NOTES`, `README.rst`, `TODO`, `rawcat`, `test`,
`vt100.py`. The API describes it as "A VT100-compatible, output-only terminal emulator."

`test/` contains **44 entries**. Based on the xterm.js `NOTES` file citing
`vt100-parser/blob/master/test/t0050-ICH.text`, this is the upstream that the xterm.js fixtures
were taken and extended from — same `tNNNN-NAME.in` / `.text` naming.

**Caveat:** I confirmed the entry count (44) and the licence via the API, and confirmed the naming
convention via the xterm.js NOTES citation. I did **not** list the individual filenames in `test/`,
so I cannot state the exact `.in`/`.text` split for this repo.

### Licence — MIT

GitHub API reports `"key":"mit", "spdx_id":"MIT"`.

### Practical value and effort — LOW effort, but likely redundant

Same format as entry 1, so the same tiny runner reads both. But 44 entries against xterm.js's 162
strongly suggests it is a subset. Take entry 1 first; only come back here to diff the two and pick
up anything xterm.js dropped.

### Overlap

Near-total overlap with entry 1, by construction.

---

## 6. esctest2 — VERIFIED (and the licence is a problem)

**URLs fetched:** `https://github.com/ThomasDickey/esctest2`,
`https://raw.githubusercontent.com/ThomasDickey/esctest2/master/README.txt`, and the API listings of
`contents`, `contents/LICENSE`, `contents/esctest/tests`.

### What it actually contains

Repo root is three entries: `LICENSE`, `README.txt`, `esctest`.

`esctest/tests/` contains **80 Python files**, one per escape sequence, listed in full:

__init__.py, apc.py, bs.py, cbt.py, cha.py, change_color.py, change_dynamic_color.py,
change_special_color.py, cht.py, cnl.py, cpl.py, cr.py, cub.py, cud.py, cuf.py, cup.py, cuu.py,
da.py, da2.py, dch.py, dcs.py, decaln.py, decbi.py, deccra.py, decdc.py, decdsr.py, decera.py,
decfi.py, decfra.py, decic.py, decid.py, decrc.py, decrectops.py, decrqm.py, decrqss.py, decscl.py,
decsed.py, decsel.py, decsera.py, decset.py, decset_tite_inhibit.py, decstbm.py, decstr.py, dl.py,
ech.py, ed.py, el.py, ff.py, hpa.py, hpr.py, hts.py, hvp.py, ich.py, il.py, ind.py, lf.py,
manipulate_selection_data.py, nel.py, pm.py, rep.py, reset_color.py, reset_special_color.py, ri.py,
ris.py, rm.py, s8c1t.py, save_restore_cursor.py, scorc.py, sd.py, sgr.py, sm.py, sm_title.py,
sos.py, su.py, tbc.py, vpa.py, vpr.py, vt.py, xterm_save.py, xterm_winops.py.

From the README, quoted:

> esctest is a suite of unit tests that test a terminal emulator's similarity to a theoretical
> ideal. That ideal is defined as "xterm, but without bugs in George's opinion."

> All tests are automatic; no user interaction is required.

Tests are Python methods using helpers from `escutil` — `AssertEQ()`, `AssertTrue()`,
`AssertScreenCharsInRectEqual()`. Run as `esctest.py --expected-terminal={iTerm2,xterm}`, with
flags including `--include` (regex filter), `--stop-on-failure`, and `--max-vt-level`.

### Licence — GPL-2.0. This is a blocker for vendoring.

The API reports the licence, and `LICENSE` begins:

```
                    GNU GENERAL PUBLIC LICENSE
                       Version 2, June 1991

 Copyright (C) 1989, 1991 Free Software Foundation, Inc.,
```

GPL-2.0 is copyleft. Copying these test files into the RetroTerm repository would put a GPL
obligation on the combined work. Unless RetroTerm is itself GPL (I did not check RetroTerm's own
licence — **this needs confirming before anyone acts on it**), **do not vendor esctest2**.

### Coverage

Broad and precise: VT100 through VT510 per the README, DEC rectangle operations (DECCRA, DECFRA,
DECERA, DECSERA, DECRECTOPS), DECSET private modes, DECRQM/DECRQSS reports, xterm window operations,
colour manipulation via OSC, selection data, and 8-bit control handling. Also `--max-vt-level` to
scope by conformance level.

No graphics. No Tektronix. No TDV, obviously.

### How it is run — this is the other problem

It drives a **live terminal over a PTY** and reads back responses. `AssertScreenCharsInRectEqual`
works by sending cursor-position and screen-content queries to the terminal and parsing the replies.
From the README:

> The framework sends "character value of a cell," "various window attributes, and cursor position"
> queries to validate responses. Tests cannot examine certain properties like cell colors, making
> some control sequences untestable.

So it needs Python, a PTY, and a terminal process that answers queries. RetroTerm would have to
expose itself as a PTY-attached process that behaves like a terminal, which is a substantial
harness.

### Practical value and effort — HIGH effort, and legally blocked as a vendored corpus

Two independent reasons to deprioritise: GPL-2.0 prevents vendoring, and the live-PTY-plus-Python
model is exactly the thing the brief says RetroTerm cannot easily do.

What it is still good for, legitimately: **reading it as a specification.** Reading `decstbm.py` to
learn what the edge cases of a scrolling region are, and then writing your own xUnit test from
scratch, is not a derivative work in any practical sense. That is a fine use. Copying the file is
not.

### Overlap with vendored libvterm

Partial. libvterm covers the common CSI operations. esctest2 goes much deeper on the DEC rectangle
family and on report/query sequences, which the libvterm corpus barely touches
(`26state_query.test` is the only one).

---

## 7. Microsoft Terminal parser and adapter tests — VERIFIED

**URL fetched:** API listings of `repos/microsoft/terminal/contents/src/terminal/parser/ut_parser`
and `.../src/terminal/adapter/ut_adapter`.

### What it actually contains

`src/terminal/parser/ut_parser/`: `Base64Test.cpp`, `InputEngineTest.cpp`, `OutputEngineTest.cpp`,
`Parser.UnitTests.vcxproj`, `Parser.UnitTests.vcxproj.filters`, `StateMachineTest.cpp`,
`product.pbxproj`, `run.bat`, `sources`, `sources.dep`, `testmd.definition`.

`src/terminal/adapter/ut_adapter/`: `Adapter.UnitTests.vcxproj`, `Adapter.UnitTests.vcxproj.filters`,
`MouseInputTest.cpp`, `TestHook.cpp`, `TestHook.h`, `WexHelpers.hpp`, `adapterTest.cpp`,
`inputTest.cpp`, `kittyKeyboardProtocol.cpp`, `product.pbxproj`, `run.bat`, `sources`,
`sources.dep`, `testmd.definition`.

Note `kittyKeyboardProtocol.cpp` — conhost has adopted the kitty keyboard protocol and tests it.

### Licence — MIT

GitHub API reports `"spdx_id": "MIT"`.

### How it is run

C++ unit tests built with MSBuild against **WEX/TAEF** (the `WexHelpers.hpp` and `testmd.definition`
files are the tell). They are not a data corpus — the sequences are embedded in C++ assertions
against conhost's own internal parser and adapter objects.

### Practical value and effort — HIGH effort, low value as a corpus

There is nothing here to vendor. Extracting the cases means reading C++ and hand-porting each
assertion, and the assertions are written against conhost's internal types, not against a screen.

Its real value is as **reading material**: `OutputEngineTest.cpp` and `StateMachineTest.cpp` are a
well-maintained description of parser edge cases from a team that had to match xterm behaviour on
Windows, and MIT means quoting or adapting individual cases is fine. `MouseInputTest.cpp` is worth
a look if RetroTerm's mouse handling needs work.

### Overlap with vendored libvterm

Substantial on parser state-machine behaviour — libvterm's `02parser.test` covers the same ground
in a far easier-to-consume form.

---

## 8. wezterm terminal tests — VERIFIED

**URL fetched:** API listing of `repos/wezterm/wezterm/contents/term/src/test`, plus `LICENSE.md`.

### What it actually contains

Six Rust files, with sizes:

| File | Bytes |
|---|---|
| `c0.rs` | 1,319 |
| `c1.rs` | 2,377 |
| `csi.rs` | 13,538 |
| `image.rs` | 3,104 |
| `mod.rs` | 38,371 |
| `selection.rs` | 3,202 |

Total roughly 61 KB of Rust. `mod.rs` at 38 KB holds the bulk, which in Rust convention means the
shared harness plus the main body of tests.

`image.rs` is notable — wezterm supports both sixel and the iTerm2 image protocol, so that file is
the only image-protocol test code in this survey outside vt340test.

### Licence — MIT

`LICENSE.md` begins:

```
MIT License

Copyright (c) 2018-Present Wez Furlong
```

(The GitHub API's top-level `license.spdx_id` reads `NOASSERTION`, but the actual `LICENSE.md` file
is plain MIT. The API's automatic detection was wrong here — worth noting as a reminder not to
trust the API field alone.)

### How it is run

`cargo test`. Inline Rust `#[test]` functions asserting against wezterm's internal `Terminal` and
`Screen` types. No data files.

### Practical value and effort — HIGH effort, low value

Same problem as Microsoft Terminal: the tests are code, tied to wezterm's own types. Porting means
reading Rust and rewriting each case. 61 KB is not a huge amount of reading, and `image.rs` might
be worth a skim for sixel edge cases, but there is nothing to vendor.

### Overlap with vendored libvterm

High. `c0.rs`, `c1.rs`, `csi.rs` cover the same territory as libvterm's `02parser`, `10state_*`
and `13state_edit` files, which RetroTerm already runs.

---

## 9. kitty test suite — VERIFIED (and GPL-3.0)

**URL fetched:** API listing of `repos/kovidgoyal/kitty/contents/kitty_tests` plus the repo licence.

### What it actually contains

Python test modules plus font files. First 40 entries listed: `CascadiaCode-Regular.otf`,
`ComfyCode-Regular.ttf`, `FiraCode-Medium.otf`, `GraphemeBreakTest.json`, `LiberationMono-Regular.ttf`,
`__init__.py`, `atexit.py`, `base.py`, `check_build.py`, `child.py`, `clipboard.py`,
`command_palette.py`, `completion.py`, `crypto.py`, `datatypes.py`, `dnd.py`, `dnd_kitten.py`,
`file_transmission.py`, `fonts.py`, `glfw.py`, `gr.py`, `graphics.py`, `iosevka-regular.ttf`,
`keys.py`, `layout.py`, `main.py`, `mouse.py`, `multicell.py`, `notifications.py`, `open_actions.py`,
`options.py`, `panels.py`, `parser.py`, `remote_control.py`, `screen.py`, `search_query_parser.py`,
`shell_integration.py`, `shm.py`, `slang.py`, `ssh.py` (listing truncated at 40).

Relevant ones would be `parser.py`, `screen.py`, `graphics.py`, `gr.py`, `keys.py`, `mouse.py`.
`GraphemeBreakTest.json` is a Unicode data file, useful in principle for width handling.

### Licence — GPL-3.0. Blocker.

GitHub API: `"key":"gpl-3.0", "spdx_id":"GPL-3.0"`. Stronger copyleft than esctest2. Do not vendor.

### How it is run

Python, against kitty's own C/Python internals; much of it needs a running kitty. Large parts test
kitty-specific things (kittens, remote control, SSH integration, the kitty graphics protocol) that
RetroTerm does not implement.

### Practical value and effort — HIGH effort, blocked licence, mostly off-target

Skip.

### Overlap

`parser.py` and `screen.py` overlap libvterm heavily.

---

## 10. neovim terminal tests — VERIFIED

**URL fetched:** API listing of `repos/neovim/neovim/contents/test/functional/terminal`.

### What it actually contains

16 Lua files: `altscreen_spec.lua`, `api_spec.lua`, `buffer_spec.lua`, `channel_spec.lua`,
`clipboard_spec.lua`, `cursor_spec.lua`, `edit_spec.lua`, `ex_terminal_spec.lua`,
`highlight_spec.lua`, `mouse_spec.lua`, `parser_spec.lua`, `scrollback_spec.lua`,
`synchronized_output_spec.lua`, `tui_spec.lua`, `window_spec.lua`, `window_split_tab_spec.lua`.

### Licence

GitHub API reports `NOASSERTION` — meaning its automatic detector could not classify it. Neovim is
generally Apache-2.0 with the Vim licence for inherited parts, but **I did not fetch and read
neovim's LICENSE file**, so I am not stating its licence as fact here. It would need checking
before any use.

### How it is run

`busted` Lua specs driving a real nvim instance with a real PTY and a `:terminal` buffer. Nothing
here is a data corpus.

Important context: **neovim's terminal is libvterm.** These specs test neovim's integration around
libvterm, not the emulation itself — the emulation is the thing RetroTerm has already vendored the
tests for.

### Practical value and effort — HIGH effort, near-zero value

Skip. The emulation layer under test is the one RetroTerm already covers.

### Overlap with vendored libvterm

Total, by construction.

---

## 11. ncurses test programs — VERIFIED

**URL fetched:** API listing of `repos/ThomasDickey/ncurses-snapshots/contents/test` and
`contents/COPYING`.

### What it actually contains

**131 entries.** A sample of what I listed: `back_ground.c`, `background.c`, `blue.c`, `bs.c`,
`bulgarian-utf8.txt`, `cardfile.c`, `chgat.c`, `clip_printw.c`, `color_content.c`, `color_set.c`,
`combine.c`, `demo_altkeys.c`, `demo_defkey.c`, `demo_forms.c`, `demo_keyok.c`, `demo_menus.c`,
`demo_new_pair.c`, `demo_panels.c`, `demo_tabs.c`, `demo_termcap.c`, `demo_terminfo.c`, `ditto.c`,
`dots.c`, `dots_curses.c`, `dots_mvcur.c`, `dots_termcap.c`, `dump_window.c`, `echochar.c`,
`edit_field.c`, `extended_color.c`, `filter.c`, `firework.c`, `firstlast.c`, `foldkeys.c`,
`form_driver_w.c`.

### Licence — MIT-style

`COPYING` begins:

```
Copyright 2018-2025,2026 Thomas E. Dickey
Copyright 1998-2017,2018 Free Software Foundation, Inc.

Permission is hereby granted, free of charge, to any person obtaining a
copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction...
```

Permissive.

### How it is run

These are interactive C demo programs. They are compiled, linked against ncurses, and run by a
human at a terminal. Their output depends entirely on the terminfo entry ncurses picks, so they are
not testing escape sequences directly — they test that a curses application looks right.

### Practical value and effort — HIGH effort, low value

Wrong shape for the job. These are demos, not tests: they have no expected output to compare
against, and their behaviour is mediated by terminfo. Getting value out would mean building
ncurses, running each demo under a capture, and eyeballing.

The one genuinely interesting angle: RetroTerm ships TDV emulators, and TDV terminals have terminfo
entries. If anyone ever wants to check that a curses app drives a TDV correctly, this is where the
apps would come from. That is a niche and distant use.

### Overlap with vendored libvterm — none, but also not comparable

---

## 12. terminfo.dev — VERIFIED (page fetched, but incomplete information)

**URL fetched:** `https://www.terminfo.dev/`

### What the page says

> Feature support tables for terminal emulators — powered by Termless, Playwright for terminals.

> Tested on real terminal applications. Run `npx terminfo.dev` to test yours.

Scale, from the page: 7 terminal applications (iTerm2, Ghostty, VS Code, Kitty, Warp, Cursor,
Terminal.app), 254 total features, 5 baseline groupings (Core, Modern, Rich, Unicode, Legacy).
Standards listed include ECMA-48, VT100, VT220, xterm, Sixel, the kitty graphics protocol, and
iTerm2 extensions.

### What I could NOT determine

**The page does not state a licence and does not link a public repository** that I could see in the
fetched content. I have not verified where the test definitions live or under what terms.

A web search result described a related project as a "caniuse.com for terminal emulators" with
250+ pages, 133 features and 19 terminals — those numbers do not match the 254/7 the site itself
reports, so at least one of those descriptions is out of date. I am reporting both and trusting
neither. Treat the search-derived numbers as unverified.

### How it is run

`npx terminfo.dev` drives a live terminal through the "Termless" harness. Node and a running
terminal required.

### Practical value and effort — HIGH effort, unknown licence

Not usable as a corpus without finding the source and its licence first. Its real value right now
is as a **checklist**: the 254-feature matrix is a good way to see what RetroTerm does not yet
claim to support, especially in the modern-xterm and graphics areas. Reading a support table is
free; adopting the harness is not.

---

## 13. Ghostty conformance tests — NOT FOUND

Ghostty was named in a search result as having "done a comprehensive xterm audit... and building a
set of conformance test cases", so I went looking.

**What I tried:**

- Listed `repos/ghostty-org/ghostty/contents` (repo root). There is **no `conformance` directory**.
  The root holds `.agents`, `.clang-format`, `.editorconfig`, `.envrc`, `.gitattributes`, `.github`,
  `.gitignore`, `.gitmodules`, `.mailmap`, `.prettierignore`, `.shellcheckrc`, `.swiftlint.yml`,
  `AGENTS.md`, `AI_POLICY.md`, `CLAUDE.md`, `CMakeLists.txt`, `CODEOWNERS`, `CONTRIBUTING.md`,
  `Doxyfile`, `DoxygenLayout.xml`, `HACKING.md`, `LICENSE`, `Makefile`, `PACKAGING.md`,
  `README.md`, `build.zig`, `build.zig.zon` and friends (listing truncated at 30).
- Listed `contents/test` — it holds only `README.md`, `fuzz-libghostty`, `ucs-detect.sh`,
  `windows`. No escape-sequence conformance corpus.
- Ran two GitHub code searches: `repo:ghostty-org/ghostty filename:*.conformance` → **0 results**;
  `repo:ghostty-org/ghostty path:conformance` → **0 results**.

**Conclusion:** as of 2026-08-11 there is no conformance corpus at those locations. Ghostty's
terminal tests appear to be inline Zig tests inside `src/terminal/` (I listed that directory:
`Parser.zig`, `Screen.zig`, `Terminal.zig`, `charsets.zig`, `csi.zig`, `dcs.zig`,
`device_attributes.zig`, `device_status.zig` and so on) — same shape as wezterm, i.e. code not data.
Ghostty's licence is MIT per the API. If a conformance directory existed historically it has moved
or gone; I did not check git history.

---

## What the vendored libvterm corpus already covers

For reference when judging overlap. Local path:
`tests\RetroTerm.Tests\Conformance\libvterm\`

**43 `.test` files** plus `LICENSE-libvterm.txt`:

02parser, 03encoding_utf8, 10state_putglyph, 11state_movecursor, 12state_scroll, 13state_edit,
14state_encoding, 15state_mode, 16state_resize, 17state_mouse, 18state_termprops, 20state_wrapping,
21state_tabstops, 22state_save, 25state_input, 26state_query, 27state_reset, 28state_dbl_wh,
29state_fallback, 30state_pen, 31state_rep, 32state_flow, 40state_selection, 60screen_ascii,
61screen_unicode, 62screen_damage, 63screen_resize, 64screen_pen, 65screen_protect, 66screen_extent,
67screen_dbl_wh, 68screen_termprops, 69screen_pushline, 69screen_reflow, 90vttest_01-movement-1
through -4, 90vttest_02-screen-1 through -4, 92lp1640917.

Licence, from `LICENSE-libvterm.txt`:

```
The MIT License

Copyright (c) 2008 Paul Evans <leonerd@leonerd.org.uk>
```

Format, from `11state_movecursor.test`:

```
INIT
UTF8 1
WANTSTATE

!Implicit
PUSH "ABC"
  ?cursor = 0,3
!Backspace
PUSH "\b"
  ?cursor = 0,2
```

`PUSH` feeds bytes; `?cursor = r,c` asserts internal state. Runner is
`Conformance\LibVtermConformanceRunner.cs` with `LibVtermScript.cs` parsing the format.

**The gap this leaves.** libvterm's corpus is all parser and screen-state. It contains **nothing**
on: graphics of any kind (sixel, ReGIS), Tektronix, VT52 mode, character sets and NRCS beyond
encoding, double-size character *rendering* (it tests the state flags, not pixels), colour
rendering, DEC rectangle operations, printer, or any real captured application session. Every
suite in this survey that is worth adopting is worth adopting because it fills part of that gap.

---

## Ranked recommendation

### Do these, in this order

**1. xterm.js `escape_sequence_files` — do this first.**
MIT, 76 self-contained `.in`/`.text` pairs, no harness needed, expected output captured from real
xterm. The C# runner is under an hour's work and the corpus slots straight into the existing
`Conformance/` folder next to the libvterm runner. It tests the *visible screen*, which is the
angle libvterm's callback assertions structurally cannot check — a wrong screen with right
callbacks passes libvterm today. It also brings four real captured sessions (vim, bash, zsh) that
exercise the emulator the way a user does. Best value per hour by a wide margin.

**2. vt340test — do this second, at least the `.six` files.**
CC0, so there is no licence question at all. It is the only real graphics corpus in this survey,
and it comes with reference PNGs taken from actual VT340 and VT240 hardware. RetroTerm already has
sixel rendering and already has the `RenderedScreenshot.Capture` machinery and a documented habit
of finding graphics defects by looking at PNGs — two of the three defects listed in CLAUDE.md were
graphics defects found by eye. Feeding 15-odd real `.six` files through and looking at the results
next to DEC's own output is exactly that process, scaled up. Start with the cheap version (render
and eyeball), add spot assertions afterwards.

**3. vttest's `tek4014.c`, hand-ported.**
X11/BSD licensed, and it is the only Tektronix 4014 test material I found anywhere. Three of its
five tests need no input. Given the recent 4014 work in this repo, reading 400 lines of C and
writing the equivalent byte streams as xUnit data is a small, well-targeted job. Do this rather
than trying to build and drive the whole vttest program.

If a fourth is wanted: **Alacritty's `alacritty.recording` files** (Apache-2.0), used as a
regression corpus against RetroTerm's own snapshots. Ignore their `grid.json` — translating
Alacritty's internal cell representation is where the cost is, and it buys little that the
recordings alone do not.

### Do not do these

**esctest2** — two independent blockers. It is **GPL-2.0**, so the files cannot be copied into a
permissively licensed repo; and it needs Python driving a live terminal over a PTY that answers
queries, which is precisely the harness shape the brief rules out. Read it as a specification for
the DEC rectangle operations, which it covers better than anything else. Do not vendor it.
(Before relying on the licence argument, someone should confirm RetroTerm's own licence — I did not
check it.)

**kitty** — GPL-3.0, needs a live kitty, and much of the suite tests kitty-only features RetroTerm
does not implement.

**neovim terminal tests** — they test neovim's *integration* with libvterm. The emulation
underneath is libvterm, whose tests RetroTerm already runs. Nothing new, and it needs a PTY and a
Lua harness to get it.

**ncurses `test/`** — 131 interactive demo programs with no expected output. Wrong shape entirely:
these check that a curses *application* looks right, mediated by terminfo, not that escape
sequences are handled correctly.

**wezterm and Microsoft Terminal** — both MIT and both worth *reading*, but neither has data to
vendor. Every case is a hand-port from Rust or C++ against that project's internal types, and both
overlap the already-vendored libvterm corpus heavily. Low return for high effort.

**terminfo.dev** — cannot be recommended because I could not verify a repository or a licence.
Useful today only as a feature checklist to read.

---

## Open questions someone should settle

- **What licence is RetroTerm itself under?** I did not check. The GPL argument against esctest2
  and kitty assumes RetroTerm is not GPL. If it is, that changes the conclusion for both.
- **Does RetroTerm implement ReGIS?** The emulator list includes VT340, and ReGIS is a VT340
  feature, but I did not look at the source. If it does not, vt340test's `regis/` directory is a
  specification rather than a test corpus.
- **vt340test's `README.md`** — I listed the repo and read its LICENSE but did not read the README.
  Anyone adopting it should read it first for the author's caveats about which results came from
  real hardware and which did not.
