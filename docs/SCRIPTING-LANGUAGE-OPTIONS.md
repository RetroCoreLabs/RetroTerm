# Script language options for RetroTerm

Research note, 22 November 2026. Question from the owner: should RetroTerm add REXX, Lua or another
language to its own script language (.rts), or replace .rts with one? Triggered by IBM 3270 support,
where users want macros: log in, move through ISPF, wait for screen text, transfer files.

**The owner's weighting:** our needs are NOT advanced, so simple wins. Speed matters little.

Rules for this note: every fact has a source (a URL or `file:line`). Anything not checked is
marked **not verified**. Release dates come from the NuGet API on 22 November 2026; GitHub activity
comes from `gh api repos/<owner>/<repo>` on the same day ("last push" is the repository's
`pushed_at`).

---

## 1. What we have today

- The language is line based: one command per line, plus `SET`, `IF`/`ELSE`/`ENDIF`, `LABEL`,
  `GOTO`, `GOSUB`, `RETURN` (`src/RetroTerm.Core/Scripting/ScriptParser.cs:314`, `:336`, `:361`,
  `:367`, `:386`, `:391`, `:396`, `:401`).
- No loops other than `GOTO`, no functions with parameters, no string functions, no arithmetic.
- `WAITFOR` and `WAITIDLE` take `ontimeout=label`, so a timeout is a planned branch
  (`docs/MCP-AND-SCRIPTING.md`, "Control flow" section).
- Stored connections can run a library script on connect and on an unplanned disconnect
  (`docs/MCP-AND-SCRIPTING.md`, "Connection event hooks" section).
- A runaway loop is stopped after 100 000 executed steps
  (`src/RetroTerm.Core/Scripting/ScriptRunner.cs:169`, `:307`), and a run takes a
  `CancellationToken` (`ScriptRunner.cs:120`, `:163`).
- **Every command step goes through `CommandRegistry.ExecuteAsync`** (`ScriptRunner.cs:238`). The MCP
  tool provider calls the same method (`src/RetroTerm.Mcp/RetroTermToolProvider.cs:601`). So script
  and MCP share one command set; that is the most valuable part of the design and any choice below
  must keep it.
- The registry holds about 34 commands, e.g. `CONNECT`, `SEND`, `SENDKEY`, `WAITFOR`, `WAITIDLE`,
  `READSCREEN`, `SNAPSHOT`, `SCREENSHOT`, `LOGSTART` (scan of `Name => "..."` in
  `src/RetroTerm.Core/Commands/`). `ExecuteAsync` is at `src/RetroTerm.Core/Commands/CommandRegistry.cs:87`.
- 72 tests in `src/tests/RetroTerm.Tests/Scripting/` (count of `[Fact]`/`[Theory]` lines).
- The app is published as one self-contained file (`src/RetroTerm.Desktop/RetroTerm.Desktop.csproj:19`
  to `:21`), net10.0 (`:4`). Native libraries already ship inside it and are unpacked to a temp
  folder on first run (comment at `RetroTerm.Desktop.csproj:8` to `:18`). So a native library is
  possible, but it is one more binary per platform (win-x64, linux-x64, osx-x64, osx-arm64).

## 2. The owner's future needs, and where each belongs

**Assumption, not verified:** the owner wrote "LUA streams". This note reads that as 3270 **LU**
(logical unit) sessions, i.e. a display LU plus a 3287 printer LU, not the Lua language.

| Need | Best home | Why |
|---|---|---|
| Trigger a command | CommandRegistry | Already how .rts and MCP work. |
| Upload / download a file (IND$FILE over a display LU) | A new command, e.g. `IND$SEND` / `IND$RECEIVE` | The transfer is a protocol with its own data stream; it must live in C# beside the 3270 model so MCP gets it too. No script language does this by itself. |
| Printer LU session (3287) | Connection/session setting plus commands | Opening an LU is a connection matter, not a language matter. |
| Handle printer output from the host | An event hook: "on printer output, run script X" | Same shape as the existing onconnect/ondisconnect hooks. |
| Export to PDF, or print through the OS printer | Commands, e.g. `PRINT` / `EXPORTPDF` | Printing is per platform (Windows first) and needs C# either way. A language cannot reach an OS printer safely on its own. |

**Finding:** every one of these needs is a **command** or a **hook**, not a language feature. None of
them needs loops, objects or a standard library. The work is the same size whichever language is
chosen, because it is C# behind the registry. (I did not find `SENDFILE`/`RECEIVEFILE` in the
`Name => "..."` scan of `src/RetroTerm.Core/Commands/`, although the MCP server lists
`terminal_sendfile` and `terminal_receivefile` tools; whether those go through the registry is
**not verified**.)

## 3. The candidates

### 3.1 Keep .rts and extend it a little

- Licence: ours (MIT project). Pure managed. Nothing to ship.
- Already has stop (cancel token) and a step limit (see section 1). Files and network are out of
  reach because scripts can only call registry commands.
- A blocking wait fits today: the runner `await`s each command (`ScriptRunner.cs:238`).
- **What a small extension would add**, each a guess at size:
  - `WHILE cond` / `ENDWHILE` (or `REPEAT n`): a few hours to a day with tests. Guess.
  - A handful of string functions in `SET` (`LEFT`, `SUBSTR`, `POS`, `UPPER`, `LENGTH`, `WORD`) and
    plain integer `+ -`: one to two days. Guess.
  - `SET var = SCREEN(row,col,len)` to read a field: one day, once the 3270 buffer exists. Guess.
  - A hook "on printer output run script X": same pattern as the connection hooks; one to two days. Guess.
- Mainframe users: unfamiliar syntax, but small and close to how ZOC or PCOMM recorded macros read
  (not verified as a comparison). General users: easy.
- Existing .rts scripts and the MCP path: untouched.
- **Would it be enough for section 2?** Yes, as far as I can see: every need there is a command or a
  hook, and the extension covers the little logic around them (retry loops, picking a word off the
  screen). This is my judgement, not a measurement.
- Risk: the language grows a piece at a time until it is a worse REXX. The parser already rejects
  unknown input by name (project rule), which helps keep it small.

### 3.2 REXX

- What mainframe tools use: ZOC's script language is REXX with Zoc* extension functions; its
  built-in interpreter on Windows and macOS is Regina, and ooRexx can be chosen instead
  (https://emtec.com/zoc/help/en/10610/introduction-to-zoc-rexx). REXX is also the z/OS TSO/ISPF
  language (https://www.ibm.com/docs/SSLTBW_3.2.0/pdf/ikjc300_v3r2.pdf, title only checked).
- **No .NET implementation found.** `gh search repos "rexx interpreter" --language=C#` returned
  nothing, and `gh search repos rexx --language=C#` returned one unrelated repository
  (22 November 2026). A missing search hit is not proof none exists, but none showed up.
- Regina: LGPL v2 (https://sourceforge.net/projects/regina-rexx,
  https://metadata.ftp-master.debian.org/changelogs/main/r/regina-rexx/stable_copyright). Native C.
  LGPL inside a single-file publish needs the user to be able to swap the library; whether that
  works with an embedded native file is **not verified** and needs a licence reading.
- ooRexx: Common Public License 1.0 (https://www.oorexx.org/license.html). Native C++. CPL is a
  weak copyleft; mixing with MIT is possible in principle, exact duties **not verified**.
- NetRexx: Java based (repo `RexxLA/NetRexx`, last push 20 Nov 2026, licence not detected by GitHub).
  Would need a JVM. Not a fit.
- BREXX: not checked.
- Options: (a) ship Regina natively for four platforms and bridge each Zoc-style function to the
  registry; (b) write our own REXX interpreter in C# (clean room, from the ANSI REXX standard).
  Effort guess: (a) two to four weeks including native packaging per platform and a C callback
  bridge; (b) four to eight weeks for a useful subset. Both guesses.
- Waiting for screen text: a REXX call into the host would block a thread; needs a worker thread per
  script and care with the session pump rule. **Not verified** for Regina's callback model.
- Sandbox: Regina has full file access through `LINEIN`/`LINEOUT` and `ADDRESS SYSTEM` (not
  verified in this session). Locking that down is extra work.
- Familiarity: the best for mainframe users. Low for general users.
- .rts scripts would stay; REXX would be a second language next to them.

### 3.3 Lua

| Library | Kind | Licence | Last release (NuGet) | Last push |
|---|---|---|---|---|
| MoonSharp | Pure managed | BSD-style, copyright Marco Mastropaolo (first lines of `LICENSE`; GitHub reports NOASSERTION, exact clause count **not verified**) | 3.0.0-beta.1, 4 July 2026 | 14 Oct 2026 |
| NLua + KeraLua | Native Lua 5.4 underneath | MIT (GitHub) | NLua 1.7.9, 1 May 2026; KeraLua 1.4.11, 19 Oct 2026 | Sep / Nov 2026 |
| NeoLua | Managed, compiles to .NET | Apache-2.0 | 1.3.20-beta.1, 15 Jan 2026 | 27 Mar 2026 |

(Dates from `https://api.nuget.org/v3/registration5-gz-semver2/<id>/index.json`; read in m/d/y.)

- Commands would be exposed as one Lua function per registry command, generated from the registry
  metadata so the MCP rule "one behaviour, one place" holds.
- Waiting: Lua coroutines let a script yield while the host awaits; MoonSharp has coroutines
  (not verified against its current docs in this session).
- Sandbox: Lua lets the host leave out `io` and `os`; how MoonSharp limits steps or time is
  **not verified**.
- MoonSharp's latest is a beta; the last stable 2.x is older (date **not verified**).
- Familiarity: known from games and tools; not a mainframe language.
- Effort guess: one to two weeks for the binding, the coroutine wait and tests. Guess.

### 3.4 JavaScript

- **Jint**: BSD-2-Clause (https://www.nuget.org/packages/Jint/4.18.0/license), 4.18.0 on
  13 Nov 2026, last push 22 Nov 2026. Pure .NET, "without native dependencies"
  (https://github.com/sebastienros/jint). Has `MaxStatements`, `TimeoutInterval`, a memory limit and
  a cancellation token; the README warns limits "are cooperative and cannot preempt arbitrary host
  callbacks" (same page). Supports promises and async functions (same page).
- **ClearScript** (Microsoft.ClearScript.Core 7.5.1.1, 4 Sep 2026; MIT on GitHub): wraps the V8
  engine, which is native per platform. Heavier to ship than Jint.
- Waiting: a command could be an `async` host function the script `await`s; exact Jint Task
  bridging **not verified**.
- Familiarity: very high for general users, none for mainframe users.
- Effort guess: one to two weeks with Jint. Guess.

### 3.5 Python

- **IronPython 3**: Apache-2.0, last NuGet release 3.4.2 on 20 Dec 2024 (NuGet API), though the repo
  had a push on 22 Nov 2026. Pure managed. No release in almost two years.
- **pythonnet**: MIT, 3.2.1 on 8 Oct 2026. Needs a real CPython installed or shipped; native per
  platform. Too heavy for a single-file terminal.
- Sandbox: Python is hard to lock down (not verified for IronPython specifically).
- Familiarity: high for general users.
- Effort guess: two to three weeks with IronPython. Guess.

### 3.6 C# scripting (Roslyn `CSharpScript`)

- Microsoft.CodeAnalysis.CSharp.Scripting 5.12.0, MIT, 13 Nov 2026 (NuGet API).
- Pure managed, but it compiles and loads full .NET code: a script can touch files, network,
  anything. No sandbox in .NET Core (no AppDomain limits; **not verified** further).
- Adds the compiler to the published file; size increase **not verified** (tens of MB is my guess).
- Async/await fits naturally.
- Familiarity: only for C# developers. Effort guess: one week. Guess.

### 3.7 What other 3270 emulators do

- **x3270 / s3270**: no built-in language. Another program drives it ("peer script") or the
  emulator starts one ("child script"), usually expect, perl or ksh
  (https://manpages.ubuntu.com/manpages/jammy/man1/x3270-script.1.html). A Python wrapper exists
  (https://pypi.org/project/py3270/0.1.5). RetroTerm's MCP server already plays this role.
- **IBM Personal Communications**: recorded `.mac` macros, VBScript through the Host Access Class
  Library automation objects, and EHLLAPI for C/C++
  (https://www.ibm.com/docs/en/SSEQ5Y_14.0.0/info_docs/autoobj.html,
  https://community.ibm.com/community/user/ibmz-and-linuxone/blogs/badal-burathoki/2021/09/03/excel-automation-pcomm).
  IBM's ACS product dropped `.mac`/`.vbs` for a new multi-platform macro language because "Linux and
  Mac have no idea what VBScript is" (https://www.ibm.com/support/pages/ibm-i-access-client-solutions-5250-macro-scripting).
- **ZOC**: REXX (section 3.2).

So there is no one standard. Mainframe users who script an emulator have met REXX (ZOC), VBScript
(PCOMM) or an outside program (x3270).

## 4. Comparison

| | .rts extended | REXX (Regina) | REXX (own C#) | Lua (MoonSharp) | Lua (NLua) | JS (Jint) | IronPython 3 | Roslyn C# |
|---|---|---|---|---|---|---|---|---|
| Licence fits MIT | yes | LGPL v2, check | yes | BSD-style, check text | MIT | BSD-2 | Apache-2.0 | MIT |
| Native binaries | none | 4 platforms | none | none | 4 platforms | none | none | none |
| Last release | ours | not checked | ours | beta, Jul 2026 | May 2026 | Nov 2026 | Dec 2024 | Nov 2026 |
| Stop / step limit | have both | not verified | we build it | not verified | not verified | yes (README) | not verified | no |
| Kept away from files | yes | needs work | yes | yes (omit io/os) | yes (omit io/os) | yes by default (not verified) | hard | no |
| Blocking wait | have it | thread per script | we build it | coroutines | coroutines | async/promise | thread | async |
| Mainframe users know it | no | yes | yes | no | no | no | no | no |
| General users know it | small | no | no | some | some | yes | yes | devs only |
| Effort (guess) | 1 to 2 weeks | 2 to 4 weeks | 4 to 8 weeks | 1 to 2 weeks | 2 weeks | 1 to 2 weeks | 2 to 3 weeks | 1 week |
| .rts and MCP | unchanged | second language | second language | second language | second language | second language | second language | second language |

Every column needs the same C# commands for IND$FILE, the printer LU, printing and PDF export; that
work is not in these effort guesses.

## 5. Recommendation

**Keep .rts as the only language and extend it a little. Put every 3270 feature in CommandRegistry
as commands and hooks. Do not add a second language now.**

Reasons:

1. The owner's needs are simple, and every one of them (section 2) is a command or a hook, which the
   registry gives to .rts and MCP at the same time. A new language adds nothing to those needs.
2. It keeps one command set for scripts and MCP (`ScriptRunner.cs:238`,
   `RetroTermToolProvider.cs:601`), which the repo's own rules treat as essential.
3. No new licence, no native files for four platforms, no new sandbox to get right. Stop and step
   limits already exist.
4. The existing 72 tests and every user's .rts scripts keep working.
5. Anyone who wants a full language already has one: drive RetroTerm over MCP, the way x3270 users
   drive s3270 from Python or expect.

The extension: a loop (`WHILE`/`ENDWHILE`), about six string functions, integer `+ -`, reading a
screen field into a variable, and an "on printer output" hook.

Costs:

- Mainframe users do not get REXX, which is what some will expect from ZOC. If that demand turns up,
  the next step is **Jint or MoonSharp, not REXX**: no .NET REXX was found, and Regina is native and
  LGPL.
- We own a home-made language for good. Every new feature is ours to design, test and document.
- The extension must be held small on purpose, or it drifts into a weak general language.

If a second language is wanted anyway, **Jint** is the best fit found: pure .NET, permissive
licence, released this month, and it has statement, time and cancel limits in its README.

## 6. Open questions

- Does "LUA streams" mean 3270 LU sessions? (Assumed, not verified.)
- Do real users ask for REXX by name, or for "macros"? That decides whether REXX matters at all.
- Do `terminal_sendfile` / `terminal_receivefile` go through CommandRegistry? Not verified.
- Should a recorded-macro feature (watch a login, write the .rts) come first? ZOC has one
  (https://emtec.com/zoc/help/en/10610/introduction-to-zoc-rexx); it may matter more than syntax.
- Regina's LGPL inside a single-file publish: allowed or not? Needs a licence reading if REXX
  returns to the table.
