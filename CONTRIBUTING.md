# Contributing to RetroTerm

Thanks for looking. This file says how to get a build running, what the code is expected to look
like, and what will get a change sent back.

---

## Getting a build

| # | Requirement | How to check |
|---|---|---|
| 1 | The .NET SDK. The projects target .NET 9; the repository is built with SDK 10.0.302 | `dotnet --info` |
| 2 | A Windows desktop session - the UI is Avalonia and `RetroTerm.Tests` targets `net9.0-windows` | not usable over a plain SSH session |

Everything RetroTerm depends on comes from nuget.org. There is no private feed and no sibling
repository to set up: a fresh clone restores on its own.

```powershell
# from the repository root
dotnet build RetroTerm.sln -nodeReuse:false
dotnet test  RetroTerm.sln -nodeReuse:false
```

The main suite is large - 6,906 tests, about two minutes - so `--filter` while you
work on one area, and the full run before you open a pull request:

```powershell
dotnet test --filter "FullyQualifiedName~TDV2200" -nodeReuse:false
```

`build-release.bat` publishes the client and the test server as self-contained Windows x64
executables into `publish\`. `start-retroterm-with-testserver.bat` then starts both.

> [!IMPORTANT]
> Pass **`-nodeReuse:false`** on every `dotnet build` and `dotnet test` you run by hand. Node
> reuse is on by default and leaves MSBuild worker processes behind that hold a lock on the
> output DLL; the next build then quietly keeps the old binary and **the tests report green
> against stale code.** This has actually happened, which is why it is written down.

### Claiming a terminal feature

Every terminal this emulates is built from its manual, and every capability it claims in a
Device Attributes reply has a test that proves it. That is the rule for a change too: **do not
make a terminal claim something the code does not do**, and do not build from recollection. The
IBM 3270 entry in the README is not started for exactly that reason - there is no data-stream
reference in `spec/`, and inventing one would produce something that matches no real host. The
manuals the existing emulators were built from are in `spec/`.

---

## What the code is expected to look like

 - **No LINQ, and avoid `foreach`.** Use index-based `for` loops. Where a hot path allocates, use
   `Span<T>`, `ArrayPool<T>`, `stackalloc` or a ring buffer. These are emulation and protocol
   paths; an enumerator allocation per packet is a real cost, not a theoretical one.
 - **Unit tests only**, in the existing test projects. Never add a standalone program that prints
   something and exits - it cannot fail a build.
 - **No FluentAssertions.** Use the test framework's own asserts.
 - **Keep the comments, and add more.** A comment that records a datasheet reference, a
   specification clause, or the reason a thing is NOT done the obvious way is the most valuable
   line in the file. Never delete one because a style guide prefers less; replace one only when it
   has become factually wrong.
 - **Plain words.** In code, comments, commit messages and documentation alike. No jargon where a
   normal word works.
 - **Run `dotnet format`** if whitespace drifts.

### Naming

Package id = root namespace = folder name. The standard is `NAMING.md` in the `RetroCoreLabs/repo-standards`
repository. The organisation is **RetroCoreLabs**; the old name `HackerCorpLabs` is retired and
must not appear in anything new.

---

## Documentation changes

```powershell
python eng\check-docs.py
```

It must exit 0. It fails on a relative link that does not resolve, a relative link that escapes
the repository root, a machine-specific absolute path, and a Mermaid diagram that is malformed or
breaks the shared Mermaid colour standard - this repository carries its own copy as `MERMAID-COLOR-STANDARDS.md`.

> [!WARNING]
> **Never put an absolute path in anything committed here.** A drive letter or a home directory is
> correct on exactly one machine, and this repository is public. A path inside the repository goes
> repo-relative; a sibling repository is named, not located; anything outside is described. A
> variable such as `%USERPROFILE%` or `~` is fine - that is the portable way to name a per-user
> location.

A README follows `README-TEMPLATE.md` in the `RetroCoreLabs/repo-standards` repository: one sentence at the top that a
stranger to the subject understands, four badges that can each be wrong, install before
build-from-source, every application in a table linking its own README, licence last.

---

## Submitting a change

 1. Branch off `master`. Do not commit to `master` directly.
 2. One logical change per commit. A commit message says **what changed and why**, in plain
    words - the subject line in the imperative, then a blank line, then the reasoning. If you
    measured something, put the number in the message.
 3. `dotnet build` and the full `dotnet test` pass, both with `-nodeReuse:false`, before you open the pull request.
 4. Say in the pull request what you verified and how. "Tests pass" on its own is not useful;
    "the VT320 DA reply now names only the four extensions it has, and `TerminalTypeIsSelectableTests` covers it" is.
 5. If something is still broken or unfinished, say so plainly. A known gap that is written down
    is fine. One that is hidden is not.

---

## Licence

By contributing you agree your work is licensed under the MIT licence in [LICENSE](LICENSE).
