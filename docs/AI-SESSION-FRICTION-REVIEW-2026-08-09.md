# What slowed the AI work down — a review of the session logs

**Written:** 2026-08-09
**Source:** the seven Claude Code session transcripts for this project, kept under `%USERPROFILE%\.claude\projects\` and not in the repository (~28 MB, 11 July – 9 August 2026)
**Method:** pulled every message Ronny actually typed (197 of them), then counted failure signatures across the raw logs and checked each claim against the repo as it stands today.

Everything below is either a quote from the logs, a count from the logs, or a check I just ran.
Where I am guessing, it says so.

---

## 1. The counts

Failure signatures across all seven transcripts:

| Signature | Hits | What it means |
|---|---|---|
| `error CS1503` | 48 | wrong argument type — I called something with the shape I expected, not the shape it has |
| `error CS1061` | 42 | **member does not exist** — I assumed an API |
| `Failed! - Failed` (test run) | 68 | a test run came back red |
| `error CS1715` / `CS0115` | 30 | override signature wrong — I guessed a base member |
| `error xUnit####` | 17 | analyzer rules I did not know about until the build told me |
| `--verify-no-changes` failures | 5 | forgot `dotnet format` before committing |

The single biggest number is CS1061: **42 times I called a method or property that was not there.**
That is the "assumed instead of read" failure, and it is the most expensive one because it only
surfaces after a full solution build.

---

## 2. The recurring friction, in order of cost

### 2.1 Same bug, second surface — the SEND `\r` story

The most expensive pattern in the whole log, because it looks finished when it is not.

- Session `2df36a6e`, msg 2219: *"script console `SEND zzz` sends `zzz` AND a fucking cr or lf or crlf. that us unacceptble. you need to add `\r` or `\n` for that"* → fixed in the **script parser**.
- Same session, msg 4191–4194, weeks later: *"you fucked up the fuckin mcp for sending with `\r`"* … *"`text=SYSTEM\r` should fucing send a cr not the fucking text `\r`"* → the **MCP tool surface** never got the same treatment.

Same defect, twice, because there were two independent paths into one command.
Commit `fe9a6cc` ("Decode SEND escapes on the MCP surface, not just in scripts") is the fix,
and it is the right shape: the escape rule now lives on `CommandParameter.DecodeEscapes`
(`src\RetroTerm.Core\Commands\CommandParameter.cs:48`) and both surfaces read it.

The identical shape appears again at msg 2727:
*"when i create a new connection using mcp its different from the ui … you need to use same logic, dont duplucate code"*.

**This is the highest-value thing to systematise.** See recommendation R2.

### 2.2 UI changes shipped unverified

Ronny had to say this three separate times, escalating:

- `2df36a6e` msg 3192: *"please us unit tests to validate ui"*
- `2df36a6e` msg 4108: *"you can drive the ui from unit tests. dont fucking forget"*
- `358bc01d` msg 1012: *"can we not use unit tests created for avalon ui to do the ui valdiation"*

Before that landed, the script-editor work cost **six** correcting turns in a row — msgs 2933
(a screenshot), 2968, 3129, 3157, 3169, 3192 — over text colours, a black-on-black editor, a
missing connection dropdown and a caret line/col that was off by one. All of it was visible on
screen and invisible to me.

It is fixed now in practice: there are **282 `[AvaloniaFact]` tests** across 21 files in
`tests\RetroTerm.Tests\Avalonia\`. But **`CLAUDE.md` still does not mention headless Avalonia
testing anywhere** — its "Testing Philosophy" section lists unit / integration / TestServer /
validation and stops. A fresh agent reads CLAUDE.md, does not learn that UI is testable here, and
repeats 2026-07.

### 2.3 Things an agent cannot check at all — the BEL saga

Session `256f2ef0`, msg 16 *"when we receoved a BEL (0x07) we need the terminal to beep"* to
msg 448 *"i heard it"*: **eleven** correcting turns, roughly 430 transcript entries.
The whole cost was that only Ronny could tell whether it worked:

> *"hahah that was underwheling.. i could heas ashort static"*
> *"you are doing soemthign wrong, i cant hear no nothing.. maybeyou exit the program before sound is started/finished"*
> *"i heared two loud and looong beeps"*
> *"i can eee in the protocol monitor that i receive BEL but there is no fucking sound"*
> *"ok, i can hear this hoorubls sorry 'bup' sound … we need to fucking have a biip and it need to be instant"*

Nothing in the repo says "sound cannot be verified by the agent — ask for a listening test early".
So I ground through eight variants before asking for one.

### 2.4 Undocumented units and unvalidated inputs

- msg 3475: *"what is the timeout number s ? ms ? hours ? yeats ? and why ythe fuck is it not documented"*
- msg 3509 / 3514: a script silently did not loop, because `timeout=300/ontimeout=wake` parsed as garbage rather than erroring
- msg 3557: *"you need to validate if waitfor (and others) have correct parameters of ontiomeout has a valid label"*

Three consecutive frustrations, one root cause: the script DSL accepted anything and explained
nothing. Fixed in `c266f6d`, `8c1bf41`, `90f8d37`. Worth remembering as the pattern —
**a DSL that parses garbage costs the user more than a DSL that refuses it.**

### 2.5 The build/test/cleanup ritual, re-derived every session

Every session re-discovers the same things: use PowerShell not bash; never `cd`; run
`dotnet format` before committing (5 misses); finish with `dotnet build-server shutdown` and
check for leftover hosts; check `Win32_Process.CommandLine` before assuming a stray `dotnet.exe`
is mine. All of it is written in `CLAUDE.md` as prose, and none of it is executable.
The repo has `build-release.bat`, `start-retroterm-with-testserver.bat` and a `Makefile` —
nothing for the inner loop.

### 2.6 Documents that lie

Session `cc22e4bd` was an entire cleanup session: *"there is so many .md files in the root folder …
catalog all of them … move all md files we dont need into a new folder called garbage"*.
116 files were retired. Today the root still holds **14 `.md` files**, including
`TODO-PLAN.md`, `PLAN-MCP-SCRIPTING.md`, `OPEN-QUESTIONS.md`, `FEATURE-STATUS.md` —
plan documents with no "last checked against the code" marker on them.
My own memory file records `FEATURE-STATUS.md` as stale and under-reporting shipped features.
*(I have not re-verified that staleness today — flagging it as recorded, not as measured.)*

Untracked junk sitting in the repo root right now, none of it in `.gitignore`:

```
New Text Document.txt
RESTART-PROMPT.txt
session.txt      session.log      retroterm_console.log
test-tdv-basic.cs
test-tdv-keys-comprehensive.cs
```

The last two are **standalone test programs** — exactly the thing the project rules forbid.
They are referenced by no `.csproj`, so they are dead, but a future agent grepping for TDV key
tests will find them and take them for real.

### 2.7 Wrong facts written down confidently

Two cases where a written note was worse than no note:

- The auto-memory file claimed `TDV2200KeyboardMapper` did not exist. It does —
  `src\RetroTerm.Core\Terminal\Input\KeyboardMapper.cs:417`. The note had conflated it with the
  name-keyed `TDVKeyboardMapper`. Corrected 2026-08-09 by re-reading the source.
- The bitmap renderer carried its own 13-entry Unicode→ASCII table for national characters that
  disagreed with the ROM's own table — two entries mapped to the same position (Ø and ø both
  0x60), which is the one-glance falsifier. Replaced in `ba4fada`.

Both are the same failure: a plausible table nobody could check against anything.

---

## 3. What to change in the codebase

Ordered by how much future thrash each one removes.

### R1 — Teach `CLAUDE.md` what this repo can already do

Cheapest, biggest. Add to the Testing Philosophy section:

- **UI is testable headlessly.** `[AvaloniaFact]` + `[Collection("Avalonia")]`,
  282 of them today; window-level pixel capture via `RenderedScreenshot` (added in `755f498`).
  **Any UI change ships with one.**
- **What the agent cannot verify, and must ask about instead of iterating:** audible sound,
  real serial/hardware keyboards, the look of a colour on Ronny's monitor, anything on real ND
  hardware. Ask on turn one, not turn eight.
- The build-server shutdown / stray-host rule, pointing at R3's script.

### R2 — A cross-surface contract test for every command

The `SEND \r` bug happened twice because a command's behaviour was defined at each call site.
`CommandParameter.DecodeEscapes` fixed that for escapes; generalise it:

> One test that walks the whole `ISessionCommand` registry and asserts that each command, driven
> through the **script parser** and through the **MCP tool provider**, produces the identical
> `ISessionCommand` invocation for the identical inputs — parameter names, types, defaults,
> escape handling, required-ness.

New surfaces (a future REST/CLI front end) then inherit correctness instead of re-earning it.
The existing `ae1c74c` test ("capability commands surface as MCP tools with valid schemas") is
half of this already — it checks the schema, not the behaviour.

### R3 — `scripts\dev.ps1`, one file, the whole inner loop

```
.\scripts\dev.ps1 build     # dotnet build RetroTerm.sln
.\scripts\dev.ps1 test      # dotnet test, full paths, no cd
.\scripts\dev.ps1 fmt       # dotnet format + verify
.\scripts\dev.ps1 check     # fmt + build + test — the pre-commit gate
.\scripts\dev.ps1 clean     # build-server shutdown, then LIST (never kill) leftover hosts
                            #   with their command lines so the operator decides
.\scripts\dev.ps1 publish
```

`clean` must **list** and not kill — the never-kill-what-you-did-not-start rule is exactly the
thing a script would get wrong.

### R4 — Delete the bait

`git rm`/delete `test-tdv-basic.cs`, `test-tdv-keys-comprehensive.cs`, `New Text Document.txt`,
`RESTART-PROMPT.txt`; add `session.log`, `session.txt`, `retroterm_console.log`, `*.log` to
`.gitignore`. Anything that is a real test moves into `tests\RetroTerm.Tests\`.

### R5 — One owner per lookup table, with an agreement test

For the ISO 646 / national-character tables and any other table that exists in more than one place
(Core, the fonts, the renderers): pick the owner (`TDVCharacterSets` is already it), delete the
copies, and add a test that asserts the derived views agree with the owner **and that no two
entries map to the same ROM position** — the check that would have caught the Ø/ø collision on
day one.

### R6 — Date-stamp the plan documents, or bin them

Every `.md` that describes intent rather than fact gets one line at the top:

```
Status: <current|superseded by X|unverified>   Last checked against the code: YYYY-MM-DD
```

Anything unstamped and older than a release is moved to `garbage\`. `FEATURE-STATUS.md`,
`TODO-PLAN.md`, `PLAN-MCP-SCRIPTING.md` and `OPEN-QUESTIONS.md` are the four to do first.

### R7 — XML doc comments on the seam types

This is the only lever against the 42 CS1061s. The types an agent touches on almost every task —
`TerminalEmulatorBase`, `TerminalSession`, `IConnection`, `TerminalBuffer`,
`EscapeSequenceParser`, `TerminalProfile`, `ISessionCommand` — should carry `///` summaries on
every public member, so the member list is readable without opening five files. It will not
eliminate wrong guesses, but it moves the discovery from "full solution build, two minutes" to
"one Read".

---

## 4. What is already working, and should not be undone

- **Warnings are errors**, set once in `Directory.Build.props:11` with a comment explaining why —
  after Ronny had to demand it (msg 514). It has held.
- **The migration doc as a running ledger.** `docs\ARCHITECTURE-REVIEW-TERMINAL-EMULATION-2026-08-08.md`
  carries an appendix per increment and strikes bugs off a verified list as they are fixed.
  It is the reason a compacted session can pick the work back up.
- **One commit per increment, each with a plain-English subject.** The last 40 subjects read as a
  changelog with no editing.
- **Proving red before green.** The blink-timer leak fix was confirmed by removing the `Dispose()`
  call and watching the test fail first. Keep doing that; a lifetime test that never failed
  proves nothing.

---

## 4b. "Make the tests comprehensive" — where the suite is actually thin

Asked to say what comprehensive should mean here, I measured the suite instead of guessing.
3378 tests, distributed like this (declared `[Fact]`/`[Theory]`/`[AvaloniaFact]` per area):

| Area | Files | Tests | |
|---|---|---|---|
| TDV | 54 | 867 | |
| Terminal | 25 | 342 | |
| Avalonia | 21 | 313 | |
| Session | 12 | 148 | |
| TestServer | 9 | 137 | |
| Desktop | 7 | 130 | |
| Commands | 8 | 62 | ← 31 command classes |
| **Mcp** | **2** | **19** | ← the surface where `SEND \r` shipped broken |
| Protocols | 1 | 12 | ← TelnetConnection / SSH, the real network layer |
| Integration | 1 | 5 | |

The emulation core is well covered. The **command surface is not**, and that is precisely where
the bugs you had to catch by hand came from.

### The real gap: structure is checked exhaustively, behaviour is checked by example

Reading the existing MCP tests, three of them are already registry-driven and exhaustive:

- `ToolList_ContainsSessionToolsAndOneToolPerCommand`
- `ToolList_CommandTools_CarryGeneratedSchemas`
- `Help_Overview_ListsEveryRegisteredCommand`

So "does every command appear as a tool, with a well-formed schema" is genuinely comprehensive.

Everything about **what a tool does** is hand-picked, one test per command someone remembered:
`Send_LiteralBackslashR_DecodesToCarriageReturn`, `WaitFor_Timeout_CarriesScreen`, and so on.
About eight of the 31 commands have that attention. The rest — `SNAPSHOT`, `READNEW`, `CLEAR`,
`RESET`, `SLEEP`, `ECHO`, `STATUS`, `WAITIDLE`, `LOGSTART`/`LOGSTOP` and the `CONN*` family —
are covered only incidentally through the scripting tests, on one surface.

**That shape is exactly how `SEND \r` shipped broken over MCP.** The schema was right. The
decoding was not. No structural test can see that difference, and no example test existed for it
until after you hit it.

### R2′ — registry-driven behavioural equivalence (the comprehensive version)

Replace "one test per command someone thought of" with one test that generates the matrix:

> For **every** command in the registry, and **every** parameter it declares, drive the same
> input through the **script parser** and through the **MCP tool provider**, intercept at
> `ISessionCommand.ExecuteAsync` with a recording decorator, and assert both surfaces deliver an
> identical `CommandArgs`.

Inputs generated per parameter from its declared type and flags:

- string, plain
- string containing `\r`, `\n`, `\e`, `\xNN` — the `DecodeEscapes` cases, **and** the negative:
  a parameter *without* the flag (`WAITFOR`'s regex pattern) must come through untouched
- string with a dangling backslash / bad escape → same error on both surfaces
- int: valid, out of range, non-numeric → same rejection
- bool: `true`/`1`/`yes` per whatever the parser accepts
- required parameter omitted → same error naming the same parameter
- optional parameter omitted → same default

31 commands × their parameters, generated, not typed. It costs one file, it covers commands
nobody has thought about, and — the point — **a new command is covered the moment it is
registered**, and a parameter wired on one surface only fails immediately.

Add the mirror of the existing structural test while you are in there: every MCP tool must map
back to a registered command, so nobody hand-writes a tool that bypasses the registry and its
escape rules.

### R8 — fuzz the escape parser

There is no fuzz or stress test over `EscapeSequenceParser` today (I checked: no test file
exercises it with generated byte streams). It is the hottest and most defect-prone code in the
repo, it is the source of the DLE-cursor-addressing class of bug, and it is the easiest thing in
the codebase to fuzz because the invariants are simple and total:

- feed random bytes, split at random boundaries across `ProcessData` calls
- never throws
- cursor always inside the buffer
- buffer dimensions unchanged
- scrollback never exceeds its cap
- no unbounded growth in parameter/intermediate collectors (the CSI parameter list, DCS payload)

Run it over every registered profile, with a fixed seed set so a failure is reproducible, plus
the pathological cases by hand: a CSI with 200 parameters, an unterminated DCS of 1 MB, ESC as
the last byte of a chunk, a UTF-8 sequence split across chunks (both with `DecodeUtf8` on and off).

This is the one addition that finds defects nobody has thought of yet. Everything else in this
document only stops known defects recurring.

---

## 5. Uncommitted right now

Nothing. Phase 5 part 2 (the cursor blink timer) went in as `f70c8c1` and is pushed;
3378 + 123 tests green, 0 warnings.
