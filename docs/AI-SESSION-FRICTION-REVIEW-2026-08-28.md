# What slowed the AI work down — third review, 26 to 28 August

**Full path:** `docs\AI-SESSION-FRICTION-REVIEW-2026-08-28.md`
**Covers:** 26 August 07:00 to 28 August 13:00 — a short, dense window: 5,655 transcript records,
112 `dotnet test` runs, 140 `dotnet build` runs, four Claude sessions working the same checkout.
**Reads:** the six Claude Code session transcripts for this project, kept under `%USERPROFILE%\.claude\projects\` and not in the repository (123 MB).
**Previous reviews:**
`docs\AI-SESSION-FRICTION-REVIEW-2026-08-09.md` and
`docs\AI-SESSION-FRICTION-REVIEW-2026-08-26.md`.

Every number below was counted from the transcripts, not estimated. Every quote is Ronny's own text.

---

## 0. The finding that matters most: which recommendations land

| Review | Recommendation | Shape | Landed? |
|---|---|---|---|
| 09 Aug R1 | Teach `CLAUDE.md` the repo is testable | edit a doc | **Yes** |
| 09 Aug R3 | `scripts\dev.ps1` | new file | **No** |
| 26 Aug R-B | Move four working rules into `CLAUDE.md` | edit a doc | **Yes** |
| 26 Aug R-C | Add MOTION to "cannot verify" | edit a doc | **Yes** |
| 26 Aug R-F | `Next` line at the top of `PLAN.md` | edit a doc | **Yes** |
| 26 Aug R-A | `SessionPreflightTests.cs` | new file | **No** |
| 26 Aug R-D | `scripts\dev.ps1` (second ask) | new file | **No** |
| 26 Aug R-E | One `[Trait]` on the fuzz theory | one-line edit | **No** |

**Six of six document edits landed. Zero of three new files landed, one of them asked for twice.**

**What this review did about that:** every item below was applied in the turn that proposed it, new
files included - `scripts\publish.ps1`, `EveryRegisteredCommandReachesMcpTests.cs` and
`XmlCommentStructureTests.cs`. Two of the three diagnoses were WRONG when first written and were
corrected by checking before acting, which is recorded in F-3 and F-4 rather than quietly fixed.

Verified on disk 28 August, **before** anything below was applied: there was no `scripts\` folder, no
`tests\RetroTerm.Tests\ManualPlan\` directory, and exactly one `[Trait("Category", ...)]` in the whole
suite — `"Integration"` on `TestServerConnectionTests`, not the `"Slow"` the review asked for. Two of
those three are no longer true, because F-1 and F-5 fixed them the same day; `SessionPreflightTests`
is still not written, and is the one open recommendation left from any of the three reviews.

R-E is the sharpest case. It is **one attribute on one class**, it was measured and costed, and it
still did not happen — because it was written as a recommendation for later instead of being done in
the turn that proposed it. That is the rule this review has to obey about itself: **a
recommendation is either an edit to `CLAUDE.md`, or it is applied in the same turn it is proposed.
Anything else has a zero success rate here across two reviews.**

---

## 1. Wrong assumptions I made, and what each cost

### 1.1 I withheld pushes for three loop ticks, assuming pushing was not authorised

**27 August 22:29:** *"wtf why dont it pusjh, it ha sbeen pushed many times, just fuckign check the
logs"*

He is right and the evidence was one command away. `git reflog show origin/master` records **more
than fifteen** prior pushes on this branch. I had been treating "pushing is irreversible, wait for
authorisation" as a rule that outranks what the repository plainly shows about how it is worked.

I also mis-stated the size of what was waiting — reported three commits when there were twenty-two.

**The rule this needs:** for a repeated action, the repo's own history *is* the authorisation
signal. Check `git reflog show origin/<branch>` before deciding a push needs asking about.

### 1.2 Three of the four "defects" I reported did not survive

I reviewed fifteen graphics comparison sheets and reported four defects. Two were real and are
fixed. Two were implemented and then **reverted**, and one was withdrawn:

- **ReGIS PV reset per command** — implemented, then reverted. `TheOppositeValueReturnsToTheBaseline`
  uses `T2T6'A'`, which is *two* commands, and the manual's own worked example needs the spacing to
  persist across them. The divergence is real and remains unexplained; it is now filed in Phase 4.
- **Sixel colour-introducer whole-sequence ignore** — implemented, then reverted. The fixture
  re-selects `#1` bare **28 times**, and hackerb9's `sixelcomments.md` records early VT240 firmware
  mishandling sixel palettes.
- **Ink beyond declared width** — withdrawn. The payload reaches exactly column 480. The band I read
  as overspill is the 16th defined colour becoming the text background.

**Common cause: I judged by eye, comparing two halves of a sheet rendered at different crops.** The
two claims that survived were the two I *measured* — with PIL, and by decoding the bytes in order.
This is RULE #0b in a new costume: looking at a picture is a way to find where to look, not the
analysis itself.

### 1.3 A test that passed for the wrong reason

My first xterm Sixel test drew nothing at all, because the xterm profile does not claim Sixel
support. It passed, and it proved nothing. Replaced with a profile guard test — but only after I
went looking for why it was green so fast.

### 1.4 I reported a green suite against stale binaries

Doing the red-before-green step on the CSI-intermediate fix, my deliberate break did not compile
(`if (false)` → CS0162, and warnings are errors). The `--no-build` test run that followed therefore
ran the **previous** binaries and reported everything passing. For a few minutes I believed a
"proof" that was measuring nothing.

`CLAUDE.md` already warns that a stale binary makes the suite report green against old code. It
warns about it in the context of *leftover build hosts*. It does not say the same thing about
`--no-build`, which is the faster way to get there.

### 1.5 `dotnet format` across the solution broke another session's file

`dotnet format RetroTerm.sln` removed `using Avalonia.VisualTree;` from `WindowChromeStyleTests.cs`
— a file another session was editing, where the using was needed for `GetVisualChildren`. The build
broke, and the file was not mine.

### 1.6 `git commit -m` with no path limit swept up another session's staged work

I committed and pushed `3db5173` with the whole index, which carried a peer session's staged icon
and theme work into my commit. You chose *"Leave it, tell the other session"*. The peer then had to
re-stage its remaining four files **by explicit path** to recover.

### 1.7 The escape-byte trap, still live — it bit me during this review

The Bash tool unescapes `\x1b` inside a heredoc before Python ever sees it, so a script that means
to *write the text* `\x1b` writes a real 0x1B byte into the file instead. It has put raw ESC bytes
into C# source and a raw 0x07 BEL into `M8-WHOLE-TERMINAL.md` — where it made the document beep when
catted and rendered as `ECHO ""`.

**It happened again while writing this document**: a Python regex `[^"\\]` reached the interpreter as
`[^"\]` and died with *unterminated character set*. Third occurrence in this window.

The workaround that works is `bytes([27])` or `chr(92)+'x1b'` — never a backslash escape typed into
a heredoc.

---

## 2. Friction that is mechanical, measured, and fixable

### 2.1 File locks: 25 build failures in three days, and the holder is always `testhost`

`MSB3027` (cannot copy, file in use) fired **25 times** since 26 August, with `MSB3021` alongside it
11 times. Locked assemblies, in order of frequency:

```
12  RetroTerm.Core.Protocols.dll
11  RetroTerm.Core.Protocols.TelnetServer.dll
10  RetroTerm.Core.dll
10  RetroTerm.Core.Protocols.Kermit.dll
 8  RetroTerm.Core.Protocols.Net.dll
 8  RetroTerm.Core.Protocols.WebSocket.dll
```

Every message that named its holder named the same one: **`testhost`, in 40 of 40 cases.** Not the
desktop app, not `RetroTerm.TestServer.exe`.

**This is the important part: `dotnet build-server shutdown` does not kill `testhost`.** It shuts
down the MSBuild nodes, VBCSCompiler and the Razor server. A `testhost.exe` orphaned by an
interrupted or killed test run survives the entire finish ritual and locks the DLLs for the next
build. `CLAUDE.md` tells you to *list* `testhost.exe` in step 4 but gives no way to clear it, and the
never-kill rule makes it look forbidden — so it gets left, and the next build fails.

The knock-on is worse than the delay: a locked DLL is the same condition that once let a build
"succeed" against stale code.

### 2.2 Test wall time: 2.8 hours in three days, and the fix is still one attribute

Deduplicated from the transcripts, 26 to 28 August:

```
38  test runs longer than 5 seconds
25  of those longer than 5 minutes
 2 h 48 m  total measured test wall time
   464 s   longest single run (7 m 44 s — the full suite)
```

The 26 August review measured *where* that time goes and the measurement still stands: three cases of
`EscapeParserFuzzTests.RandomBytes_LeaveEveryEmulatorConsistent` are **295 seconds of 580 — 51% of
the suite**, with the other 6,457 tests sharing the rest. The file has not changed since.

Tagging that one theory `[Trait("Category", "Slow")]` and running `--filter "Category!=Slow"` as the
routine gate would have saved roughly **two of those 2.8 hours**, losing nothing but a random-byte
sweep that does not need to run twenty-five times in three days.

Second-order cost, already observed: an eight-minute wait pushes the suite into the background, and
a backgrounded or interrupted run is exactly what orphans the `testhost` in 2.1.

### 2.3 Forty-nine kill commands in three days

`Stop-Process` / `taskkill` appeared in **49** tool calls in this window. Every one of them sits
against an absolute rule not to kill what I did not start, so every one needs a process-tree check
first, and several needed a peer session to confirm ownership.

The tree check itself works well — it settled nineteen nodes in one command. The problem is how
often it is needed, which follows directly from 2.1 and 2.2. Note also that on the last tick of this
session, `dotnet build-server shutdown` cleared nineteen orphaned MSBuild nodes with **no killing at
all** — worth knowing, because it means the kill is often unnecessary for MSBuild nodes and only
genuinely needed for `testhost`.

### 2.4 Four sessions, one checkout, no protocol for shared state

Four Claude sessions worked this repository in the window, exchanging **23 cross-session messages**:

```
23  verilog-ac      (ND-120 FPGA, reading RetroTerm as a specification)
 7  retroterm-09
 4  retroterm-db
 2  xmsg-97
```

The cross-session channel worked well as a *correction* mechanism — `verilog-ac` refused to believe
either side about the TDV keyboard and went to the source, which found
`docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md` describing a TDV in VT220 shape (arrows as `ESC[A/B/C/D`,
`Page Up`/`Page Down`/`Insert`/`Delete`/`End` keys a TDV does not have, HOME's two modes inverted).
Every one of those was wrong and the registry had been right all along. That produced
`TdvKeyboardDocumentMatchesTheRegistryTests.cs`, which is a genuinely good outcome.

What has no protocol is **shared mutable state**. Four things are shared and unguarded, and three of
them caused a real failure this window:

| Shared thing | Failure |
|---|---|
| The git index | 1.6 — my `git commit -m` swept a peer's staged work |
| The working tree | 1.5 — my `dotnet format` edited a peer's open file |
| The build server / `bin` | 2.1 — one session's `testhost` locks another's build |
| `publish\RetroTerm.Desktop.exe` | cannot be rewritten while it runs (see 3.5) |

### 2.5 "What is the plan?" — three more times, fifteen in total

27 August 13:23 *"what is the plan and how can i help"*; 27 August 22:44 *"what is the current
plan"*; 28 August 10:23 *"whay is the updated plan ?"*.

R-F landed — `docs\PLAN.md` holds outstanding work only and carries a `Next:` line. The document is
not the problem. The answer still is not offered at the moment he wants it, which is the end of a
piece of work. Note the shape of the 27 August one: *"and how can i help"*. He is not auditing; he
is trying to find his own next action.

### 2.6 The judging still lands on him, and twice he had to ask for the picture

**27 August 15:09**, doing my job for me:

> *"i am sure you as well as me are able to see that thy are different. the scaling. and espesially
> the font is almost impossible to rad in the top image ... and tne cat- vt240 is insane different in
> the colours and scaling is like the vt340 out of sale"*

**27 August 17:47:** *"open the fucking image and show me"* — then an interrupt, then *"fortget it,
do a"*.

`CLAUDE.md` already says to open the PNG after anything that draws. In both of these I had the
artefact and had not looked at it before speaking. This is the same root as 1.2: describing a
picture instead of measuring it.

---

## 3. What to change in the repository

Ordered by measured cost removed. Each one is small enough to apply in a single turn — deliberately,
because of section 0.

### F-1 — Tag the fuzz theory `Slow` and name the fast gate in `CLAUDE.md` — **DONE 28 August 2026**

**Fixes 2.2 (2.8 h in three days), and part of 2.1.** This was R-E, third time of asking, and it is
now applied rather than recommended.

**Measured after the change:** fast gate **2 m 02 s**, 6,580 passed, 0 failed. Full suite unchanged
at **6,622 passed, 0 failed, 41 skipped**. The trait went on the METHOD, not the class, so the cheap
parser-level fuzz theories stay in the fast gate.

```csharp
[Trait("Category", "Slow")]     // 295 s of the suite's 580 s live in this one theory
public class EscapeParserFuzzTests
```

Then in `CLAUDE.md`, the routine gate becomes
`dotnet test RetroTerm.sln --filter "Category!=Slow"` and the full suite runs before a commit.
Expected 7 m 44 s → about 3 minutes.

**Do not tag the headless UI tests.** They are 1 m 45 s and they are the only tests that look at
pixels; four defects have been found by them or the PNGs they write.

### F-2 — Make the finish ritual actually clear `testhost` — **DONE 28 August 2026**

**Fixes 2.1 — 25 build failures in three days.**

`dotnet build-server shutdown` does not touch `testhost`, so step 4 of the ritual leaves the exact
process that causes the locks. Change `CLAUDE.md` step 4 to say so, and give the safe command:

```powershell
dotnet build-server shutdown          # clears MSBuild nodes + VBCSCompiler; NOT testhost
Get-CimInstance Win32_Process -Filter "Name='testhost.exe'" |
  Select-Object ProcessId, ParentProcessId, CommandLine
# A testhost whose CommandLine names THIS repo's test DLL and whose parent is DEAD is an
# orphan from a finished or interrupted run. That one is safe to stop. Anything under a
# live Code.exe / devenv.exe belongs to somebody else — leave it.
```

Add the sentence that is missing from the never-kill rule: **an orphaned `testhost` holding this
repo's own test DLL, with a dead parent, is the one process the rule was never meant to protect.**

### F-3 — Cover the commands the parity harness never saw — **DONE 28 August 2026**

**Fixes the 27 August question** *"havce you update the mcp to also support all new features"*.

**My first diagnosis here was wrong, and checking it before acting is the only reason it did not
ship as a wrong fix.** I wrote that the tool list was hand-maintained. It is not:
`RetroTermToolProvider.BuildToolList` generates one tool per registered command in a loop, so any
command reaching the registry does reach MCP.

The real hole is one level up. **Every parity test builds its registry with
`BuiltinCommands.RegisterAll` alone.** The desktop (`MainWindow.Mcp.cs`,
`GetOrCreateCommandRegistry`) registers **six** groups. So CONNECT, DISCONNECT, CONNLIST, CONNSHOW,
CONNSAVE, CONNDEL, SENDFILE, RECEIVEFILE, GATEWAY, SCREENSHOT, LOCALKEY, ZOOM and PASTE were covered
by **nothing** — not the parity harness, not the tool-list check. The cross-surface guarantee
stopped at the built-in subset, silently, because the other groups need constructor dependencies the
existing tests did not want to build. They are stubs; it took nine lines.

**Landed:** `tests\RetroTerm.Tests\Mcp\EveryRegisteredCommandReachesMcpTests.cs` — three tests that
build the registry the way the desktop does and assert every registered command has a
`terminal_<name>` tool exposing every one of its parameters. Red-before-green confirmed: commenting
out the `ScreenshotCommand.RegisterAll` line fails `EveryDesktopCommandGroupIsRepresented` by name,
and **the break compiled first** (see 1.4 — a `--no-build` run after a failed build proves nothing).

### F-4 — Retire the one shadow renderer that validates itself — **DONE 28 August 2026**

**My first scope here was too broad, in exactly the way section 1.2 describes**, and reading the
assertions before rewriting them is what caught it. I claimed four files were shadow renderers.
Checking what each actually asserts:

- `TDV2200CharSetScreenshotTests`, `TDV2215CharSetScreenshotTests` and `TDV2200NationalCharacterTests`
  assert on **font bit tables** (`Assert.Equal(0x0044, glyphBits[1])`, `bits.Length == 16`). That is
  a test of the font ROM data, which is legitimate and has no business going through
  `TerminalRenderer`. Their `SKCanvas` use draws a character-chart image for a human to look at.
  **Left alone. They were never the problem.**
- `M5TektronixTests` / `M6SixelAndRegisTests` tile existing PNGs into comparison sheets. Also fine.

**One file was genuinely circular: `TDV2200ScreenshotTests`.** Its own class summary claimed *"Uses
the ACTUAL TerminalRenderer to ensure tests validate real rendering behavior"* — and it did not.
`SaveRenderTargetAsPng` is a second renderer in SkiaSharp with its own `#001911` background, its own
`#00FF88` foreground, its own 40%-bold-brightness formula, its own synthetic-bold stamp and its own
glyph loop. `ValidateBoldBrightness` and `ValidateUnderline` then read **the PNG that method had just
drawn**. The test asserted its own arithmetic.

**Landed:** the pixel assertions now come from `RenderedScreenshot.Capture` — the production chain
`TerminalCanvas` → `TerminalRenderer` → `BitmapFontRenderer`. The SkiaSharp sheet stays, because it
carries a caption and is worth looking at, but nothing is asserted against it any more, and the false
class summary is replaced with what the file actually does.

**Proved by breaking the real thing.** Disabling bold brightening in `TerminalRenderer.cs` — in
`src\`, not in the test — now fails `Screenshot_NDAAR_BoldRectangle` with *"Normal R=0,G=255,B=136;
bold R=0,G=255,B=136"*. Before this change that same break left all 19 tests green, which is the
whole finding in one line.

`RenderedScreenshot`'s own summary says it was written to remove exactly this failure mode. It just
never got used in the file it was written for.

### F-5 — Publish to a versioned folder — **DONE 28 August 2026**

**Fixes 2.4's fourth row, and the run sheet paragraph headed "Before we start — one thing needs
you", whose entire content is asking Ronny to close the app by hand.**

**Landed:** `scripts\publish.ps1` publishes to `publishersions\<version>-<n>\` and repoints the
junction `publish\current`. This also finally creates the `scripts\` folder that two reviews asked
for and never got.

**Both claims measured, not reasoned:**

- A full publish ran to completion **with RetroTerm still running** from the old flat path, which is
  the thing the flat folder made impossible.
- The junction was repointed **while a file underneath it was held open** by another process — proved
  by holding `publish\current\RetroTerm.Desktop.exe` open with `File.Open` and repointing anyway. A
  running process holds the resolved file, not the link that found it.

The old `publish\RetroTerm.Desktop.exe` is left exactly where it is and never written to, so the copy
Ronny is running is untouched. The script closes and kills nothing; it removes the reason the
question kept being asked, not the rule.

### F-6 — Three rules for `CLAUDE.md`, each from a failure above — **DONE 28 August 2026**

1. **Repeated actions are authorised by the repo's own history.** Before deciding a push needs
   asking about, run `git reflog show origin/<branch>`. (1.1)
2. **Never run `dotnet format` or `git commit -m` across the whole solution or the whole index while
   another session is working.** Format the files you touched; stage by explicit path. (1.5, 1.6)
3. **`--no-build` after a failed build tests the previous binaries.** The red-before-green step must
   confirm the break actually compiled before trusting the run. (1.4)

And one line for the escape trap, which is already in memory but has now cost three occurrences in
three days: **never type a backslash escape into a Bash heredoc.** The tool unescapes it before the
interpreter sees it. Use `bytes([27])` or `chr(92)` — or use the Write tool. (1.7)

**All four landed.** The first three went into `CLAUDE.md` with the commit that acted on this review;
the escape-trap line was missed and went in separately — which is itself the section-0 pattern, since
it was the one item written as a trailing sentence rather than a numbered rule.

### F-7 — Make the stranded-doc-block fault fail a test, not a manual check — **DONE 28 August 2026**

**Fixes what F-3's compiler check cannot reach.** Turning `GenerateDocumentationFile` on finds a
stranded block only when the param names it lands on happen to disagree with the signature. A block
with no params, or one whose names happen to match, is invisible to it — and the check is manual
anyway, needing a `CS1591` suppression that must never be committed.

**Landed:** `tests\RetroTerm.Tests\Documentation\XmlCommentStructureTests.cs`. It reads every doc
comment under `src` and `tests` and fails on two things: more than one `<summary>` in a block, which
is the stranded-block signature and has no legitimate use; and a raw control byte, which is what a
backslash escape becomes when a heredoc unescapes it first.

**The first run found ELEVEN more that the compiler check had passed.** The worst was
`IGraphicsSurface`: the entire architectural summary of the graphics layer — the plane model, the
compositor owning the coordinate transform, why everything clips rather than throws — was sitting on
the small static class above it, while the interface itself had nothing.
`TerminalEmulatorBase.SoftReset` had lost the whole DECSTR table 13-1 transcription the same way.

So the fault ran to **twenty instances** across the two passes, and only nine were reachable by the
compiler. It is the single most common documentation defect in this repository, it is invisible in a
diff, and it silently destroys the most valuable comments — the long architectural ones, because
those sit on types that have no parameters to disagree about.

---

## 4. What is working, and must not be traded away

- **The cross-session channel as a correction mechanism.** `verilog-ac` refusing to believe the
  document *or* me, and going to the registry, found five wrong rows in the keyboard reference and
  produced a test that pins them. That is the single most valuable thing that happened in this
  window.
- **Reverting a fix that the specification does not support.** Two of my four reported defects were
  implemented and then backed out after reading the manual properly. Both reverts left a comment
  block naming the attempt and why it was wrong, so nobody tries them again.
- **Separating "measured" from "seen but unexplained" in `docs\PLAN.md`.** The ReGIS PV divergence is
  real, is not understood, and is now filed as blocked rather than as work.
- **Measuring instead of eyeballing.** Every claim that survived scrutiny this window came from PIL
  or from decoding bytes in order. Every claim that did not came from looking at two halves of a
  sheet.
- **The corpora as the oracle**, and opening the PNG after anything that draws. Unchanged from the
  last review, and still the only thing here checked against reality.

---

## 5. The one-line version

The emulation work is not where the time goes — no regression in the terminal emulation was found by
Ronny in this window. **The time goes to the machinery around it:** 2.8 hours of test runs that one
attribute would halve, 25 build failures from a process the documented cleanup ritual does not clean,
49 kill commands needed to work around those two, and four sessions sharing a checkout with no rule
about the git index or the working tree. All four are repository problems with small fixes, and the
record from two previous reviews says they will only land if they are applied in the same turn they
are proposed.
