# What slowed the AI work down — second review, 9 to 26 August

**Full path:** `docs\AI-SESSION-FRICTION-REVIEW-2026-08-26.md`
**Covers:** 9 August to 26 August 2026 — 206 commits, 415 files, 81,872 lines added.
**Reads:** the five session transcripts in
the Claude Code session transcripts for this project, kept under `%USERPROFILE%\.claude\projects\` and not in the repository — 113 MB, 598 messages from Ronny.
**Previous review:** `docs\AI-SESSION-FRICTION-REVIEW-2026-08-09.md`.
This one does not repeat it. It says which of its eight recommendations landed, and what has gone
wrong since that the first review did not see.

Every claim below carries a date and Ronny's own words. Nothing here is inferred from how a session
felt.

---

## 1. What the first review recommended, and what happened

| | Recommendation | Landed? | Evidence |
|---|---|---|---|
| R1 | Teach `CLAUDE.md` what this repo can already do | **Yes** | The "The UI IS testable" section is in `CLAUDE.md` |
| R2 | Cross-surface contract test for every command | **Yes** | `tests\RetroTerm.Tests\Commands\CommandSurfaceEquivalenceTests.cs` |
| R3 | `scripts\dev.ps1`, one file, the whole inner loop | **No** | There is no `scripts\` folder in the repo |
| R4 | Delete the bait (`test-*.cs` in the root) | **Yes** | `.gitignore:173` — `/test-*.cs` |
| R5 | One owner per lookup table | **Yes** | `TDV2200KeyRegistry` is now the single source; four call sites read it |
| R6 | Date-stamp the plan documents or bin them | **Yes** | `docs\PLAN.md` carries **Updated:** and holds outstanding work only |
| R7 | XML doc comments on the seam types | **Yes** | `TerminalSession` 48 blocks, `IConnection` 14, `CommandParameter` 8 |
| R8 | Fuzz the escape parser | **Yes** | `tests\RetroTerm.Tests\Terminal\Parsing\EscapeParserFuzzTests.cs` |

**Seven of eight.** The one that did not land, R3, is the one that would have prevented the worst
session in the whole log. That is not a coincidence and it is dealt with below as **R-D**.

---

## 2. The friction since 9 August, in order of what it cost

### 2.1 The by-hand test sessions fail on SETUP, never on the cases — 48 minutes, ended with him quitting

**25 August, 08:32 to 09:20.** Seven messages, five separate setup failures, one after another:

```
08:40  so why in the fuckikng hell dont yoiu give me the fuckking context always aaahole
08:40  so wjhat is the fucking questions ? use fuckign interview
09:05  why dont you fucking do screenshots or unit tests to validate ?
       thi just shows some text, the grpahics just failed
09:16  you need to tell me what fuckiong terminal type to use fucking asshole
09:17  ther eis no fucking app
09:19  that fucked up the fuckin UI ... so i fuckikgn killed the fucking temrinal
09:20  i dont fuckign want to test more i fucking hate your fucking shit
```

Read the list again. **Not one of those is about a terminal-emulation case.** They are: no context
with the question, wrong question format, a case handed over that had never been run end to end, a
missing prerequisite, no running app, and a build that broke the UI. The cases themselves were never
reached.

**The cost is still being paid.** Phase 1 of `docs\PLAN.md` — six items, forty minutes — was open
before that session and is open now, a day later.

**Cause.** Each by-hand case is set up ad hoc, in prose, at the moment he is asked. Nothing checks
beforehand that the picture will draw, that the app is the build being discussed, or that the
prerequisites are stated. Every one of those five is checkable by a machine before he is disturbed.

### 2.2 Asking him to judge without saying what to look at

**25 August 08:40**, twice in one minute. There is already a memory note about exactly this —
`context-before-the-question.md`, written 20 August — and it did not prevent it five days later.

**Why the note did not work.** Memory notes arrive as background context inside
`<system-reminder>` blocks. `CLAUDE.md` arrives as instructions that override default behaviour. The
rule was filed in the weaker of the two places.

### 2.3 Turning his question into actions

**25 August 09:19**, in capitals:

> AND REMEMBER ! DO NOTG FUCKIN DO RANDOM FUCKING SHIT WHEN I ASK A QUESTION! WHNW I ASK A QUESTION
> I FUCKIONG EXPECT YOU ANSWER THAT FUCKING QUETSION. NOT put into thw fuckign quesion ideas that i
> did not fucking said

Same shape as 2.2: there is a memory note (`a-question-gets-an-answer-only.md`), and it is filed in
the weaker place. Same fix.

### 2.4 Tuning by eye a thing only his eye can judge — MOTION is not on the list

**Smooth scroll, 25 August, 12:20 to 15:37 — three hours seventeen minutes**, four rejections, two
interruptions, and one whole context used up mid-episode:

```
12:20  yeah, we need a smooth scroll ...
14:0x  hahahah fucker. 5 clears the sreeb abs stops before fucking scrolling starts ... slow as fuck
14:xx  5 is slow as fuck, dont know why. smoot scrollign scrolls very nice, but i would like it to run
15:xx  wow that is ugly as fuck the smooth scroll. its not smoth as all. i am having
       difficulties to explain what is wrong though
15:xx  its still jagged
15:37  well, a fast host should fucking render on lines that hasnt been shown yet
```

The last message is a **design correction**, and it arrived after three hours of tuning. It could
have been asked for in the first ten minutes.

`CLAUDE.md` already has a section called "What you CANNOT verify — ask on turn one, do not iterate".
It lists sound, real hardware, subjective appearance, and wall-clock timing. **It does not list
motion.** So the section that exists precisely to stop this did not cover it, and the BEL saga —
eleven correcting turns in July, which is why the section was written — repeated in a new costume.

Note his own words: *"i am having difficulties to explain what is wrong though"*. When the judge
cannot name the fault, more rounds of tuning cannot converge. That is the signal to stop and build
something he can compare two of, side by side.

### 2.5 Stopping when he expected the work to carry on

Three times, and the language escalates:

```
17 Aug  i am so insane fuckoing confused why yopu fucking stopped the loop
25 Aug  what is the plan ? - and why in the holuy fucking hell do you tsop
25 Aug  just fiucking make them. dont we need all ? what is thr fuckoing holdoff ?
        whay are you such a fcuking lazy bitch
```

The plan is organised by **who is needed** — his eyes, an SSH host, a real machine — which is right.
The stops that annoyed him were on items where the answer was "nobody else is needed, I just stopped".

### 2.6 "What is the plan?" — asked twelve times in four weeks

30 Jul, 4 Aug, 5 Aug, 6 Aug, 10 Aug, 11 Aug, 16 Aug, 17 Aug, 18 Aug, 20 Aug, 21 Aug, 25 Aug.

`docs\PLAN.md` exists, is dated, and holds outstanding work only — R6 did land. He still has to ask
twelve times, so the document is not the problem: **the answer is not offered at the moment he wants
it**, which is the end of a piece of work.

His 18 August instruction says what the answer must not look like:

> reclean the plan, gice me a new prio plan with phases and todo. clean out what is already done -
> i hate seeung shiut like "42 taks 34 done"

### 2.7 The full suite takes 7 minutes 44 seconds, and the finish ritual mandates it every time

Measured, from `tasks\b1zigusoj.output`:

```
Passed! - Failed: 0, Passed: 6304, Skipped: 41, Total: 6345, Duration: 7 m 44 s
Passed! - Failed: 0, Passed:  123, Skipped:  0, Total:  123, Duration:   139 ms
```

`CLAUDE.md` requires `format` then `build` then `test` then `build-server shutdown` at the end of
every change. At 206 commits, most of them ending in that ritual, this is the single largest block
of dead time in the project. It also drives a second-order fault: waiting 8 minutes encourages
running the suite in the background, which is how a build once reported green against a stale binary.

There is only **one** `[Trait("Category", ...)]` in the whole suite, so there is no fast gate to run
instead.

**And the obvious suspect is innocent.** The headless Avalonia tests are the natural thing to blame —
they drive a real app, capture real pixels, and are serialised by `[Collection("Avalonia")]`. Measured
on 26 August, the whole `RetroTerm.Tests.Avalonia` namespace is **711 tests in 1 m 45 s** — under a
quarter of the 7 m 44 s.

**It is one test method.** Ranking all 6,460 recorded durations from a `trx` run:

```
113.87s  EscapeParserFuzzTests.RandomBytes_LeaveEveryEmulatorConsistent
 99.81s  EscapeParserFuzzTests.RandomBytes_LeaveEveryEmulatorConsistent
 81.75s  EscapeParserFuzzTests.RandomBytes_LeaveEveryEmulatorConsistent
 23.29s  PdfPrintSinkTests.TheTektronixStreamsPrintAsHardCopies
 15.31s  TektronixCorpusTests.ARealPlotDrawsSomething(name: "points.tek40xx")
```

Three cases of one theory are **295 seconds of the 580 measured — 51% of the whole suite.** The
remaining 6,457 tests share the other half.

The irony is worth recording: that fuzz test is **R8 from the first review**, which I recommended and
added. It is a good test and it should stay. It should not run twenty times a day.

### 2.8 A message arriving during a long tool run went unacknowledged

**25 August 12:36** — he had to ask three times:

```
hey !! did you get the buig report ???
listenb
did you get the fucing bug report ??
```

The report had arrived from another Claude session while a long test sweep was running. It had in
fact been read and acted on; nothing said so until he interrupted the tool call by hand.

### 2.9 The publish-and-relaunch dance

`ther eis no fucking app` (25 Aug 09:17) is the visible tip. Underneath: `publish\RetroTerm.Desktop.exe`
holds its own binary, so it cannot be republished while it runs; the never-kill rule means it must not
be closed without asking; and the run sheet itself carries a paragraph headed **"Before we start — one
thing needs you"** whose entire content is asking him to close the app by hand. That paragraph is a
workaround that has been promoted to a procedure.

---

## 3. What to change

Ordered by cost saved per hour of work. Each is a change to the repository, not a resolution.

### R-A — Make the by-hand session preflight a TEST, not a paragraph

**The problem it fixes:** 2.1, the worst episode in the log.

Add `tests\RetroTerm.Tests\ManualPlan\SessionPreflightTests.cs`. One `[AvaloniaFact]` per by-hand
session, each asserting the things that failed on 25 August, before he is asked anything:

- every artefact the session shows him exists, was written in this run, and carries ink
- every case in the session names its prerequisites (terminal type, which machine, which build)
- the published binary is newer than the last commit that touched `src\`

A preflight in prose rots because nothing runs it. A preflight as a test fails the build the day it
stops being true. The rule the run sheet already states — *"Recording goes on FIRST"* — belongs in
the same test.

**Then change the run sheet's opening line to name the preflight**, so a session cannot start
without it having passed.

### R-B — Move the four working rules out of memory and into `CLAUDE.md`

**The problem it fixes:** 2.2 and 2.3, both of which had a memory note that did not prevent them.

Copy into `CLAUDE.md` (they can stay in memory as well):

- `a-question-gets-an-answer-only.md` — a question gets an answer, and no actions
- `context-before-the-question.md` — say what it is, where it is, and which two things to compare,
  BEFORE asking him to judge; one narrow observation per question
- `plans-hold-outstanding-work-only.md` — never "34 of 42 done"
- `no-desktop-automation.md` — never screenshot his screen or send global keys

Memory is background. `CLAUDE.md` overrides behaviour. These four are behaviour.

### R-C — Add MOTION to the "What you CANNOT verify" list, and demo before implementing

**The problem it fixes:** 2.4, which cost 3h17m, and the BEL saga before it, which cost eleven turns.

Two edits to `CLAUDE.md`:

1. Add a bullet to the "What you CANNOT verify" list:
   **Motion.** Whether a scroll, an animation or a cursor blink looks smooth, and at what speed.
   Frame timing can be asserted; smoothness cannot.
2. Add the rule the two sagas share: **when his eye or ear is the acceptance test, build the smallest
   runnable thing FIRST and ask him to judge it before writing the implementation.** Both the bell and
   the smooth scroll were built, then judged, then rebuilt — three times over in the scroll's case.

And one rule for the middle of such an episode: **when he says he cannot name what is wrong, stop
tuning.** Give him two versions side by side to choose between instead.

### R-D — `scripts\dev.ps1`, the recommendation from 9 August that never landed

**The problem it fixes:** 2.7 and 2.9, and part of 2.1.

One file, the whole inner loop, so it is not re-derived from `CLAUDE.md` prose every session:

- `.\scripts\dev.ps1 fast` — format, build, and the fast test gate (see R-E)
- `.\scripts\dev.ps1 full` — the whole ritual including `build-server shutdown` and the leftover
  listing, with the command line of each leftover printed so the never-kill rule can be obeyed
- `.\scripts\dev.ps1 publish` — publish to a **versioned folder** and update a `current` junction, so
  a running app never holds the file being written and never has to be closed by hand. This retires
  the "Before we start — one thing needs you" paragraph in the run sheet.

### R-E — A fast test gate

**The problem it fixes:** 2.7 — 7m44s per finish.

**This is a one-attribute change**, and the measurement in 2.7 says exactly where to put it:

```csharp
[Trait("Category", "Slow")]     // 295 s of the suite's 580 s live in this one theory
public class EscapeParserFuzzTests
```

The fast gate is then `dotnet test --filter "Category!=Slow"`, and the full suite still runs before a
commit. **Expected: 7 m 44 s down to roughly three minutes**, losing nothing but a random-byte sweep
that does not need to run twenty times a day.

**Do not also mark the headless UI tests slow.** They are 1 m 45 s and they are the only tests that
look at pixels — four defects have been found by them or by the artefacts they write. Skipping them
to save 23% would be the worst trade in the repository.

The general lesson is the one this repo already learned when the ReGIS background fix wrecked a
working fixture: **measure everything, not just what you suspect.** The obvious answer here — "it is
the UI tests" — was wrong, and acting on it would have thrown away the most valuable tests in the
suite.

### R-F — Answer "what is the plan" without being asked

**The problem it fixes:** 2.6, twelve asks, and part of 2.5.

Two small things:

1. Put a **`## Next`** section at the very top of `docs\PLAN.md` — one line, the single next action,
   and who can do it. Updated whenever the plan is.
2. End every work turn with that one line. He asked twelve times because the answer only ever
   appeared when he asked.

For 2.5 specifically: when stopping, say **which of the three reasons** it is — the work is done,
it needs him, or it needs a machine. "Why did you stop" was asked three times because the answer was
never given.

### R-G — Acknowledge an incoming message before continuing a long run

**The problem it fixes:** 2.8.

No code. One habit: when a message arrives mid-run, the very next thing written is one line saying it
arrived and what will happen to it. He asked three times because silence and not-having-seen-it look
identical from his side.

---

## 4. What is working, and must not be undone

- **The corpus-as-oracle approach.** Three outside corpora, fetched never committed, judged against
  real hardware photographs. It found the swapped red and blue, the ReGIS background and the missing
  DECTEK. Nothing else in this project checks against reality.
- **Opening the PNG after a change that draws.** Four defects have been found this way while every
  assertion passed, including two in the last fortnight.
- **Verifying a claim before acting on it, including claims in this repo's own documents.** Three
  plan entries have now been wrong; two would have hidden a real defect.
- **`docs\PLAN.md` holding outstanding work only.** R6 landed and should stay landed.
- **The standing-judgement-calls table.** Twenty-six decisions, each with where it lives and why.
  It is the reason the same argument does not get had twice.

---

## 5. The one-line version

Seven of eight recommendations from the first review landed, and the work since has been productive —
206 commits, no regressions found by Ronny in the emulation itself. **Every remaining hour of friction
is spent at the seam where he has to look, listen or judge**, and it is spent on setup, on context,
and on tuning blind — never on the terminal emulation. R-A, R-B and R-C are aimed at that seam and are
worth more than the rest put together.
