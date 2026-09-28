# Handoff

**Full path:** `docs\HANDOFF.md`
**The plan:** `docs\PLAN.md` — outstanding work only
**By-hand pass:** `docs\manual-tests\INDEX.md` and `RUN-SHEET.md`
**The TDV evidence:** `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md`

**A STABLE NAME, on purpose.** One living file, rewritten rather than added to. Git keeps the old
ones. The dated handoffs from before the convention are left alone as history.

**Last updated:** 11 September 2026, at `772c12e`.
**State:** full `dotnet test RetroTerm.sln` — **6867 passing, 1 skipped, 0 failed**, plus 123 in
the Kermit project. Published `1.0.26.2-1`, which is what `publish\current` points at. Working
tree clean, in sync with the remote. One branch, `master`.

**Why the work stopped here:** Ronny's call, 11 September — *"i think we should write a handoff
and continue later - these are not very important right now"*. Nothing is half-built. Every commit
on the day is green and pushed.

---

## What 11 September was about

One instruction drove the whole day: *"check the tdv 2200 manuals also - and then align the 2215
to its manual as good as possible, the same for the 2200."* The three manuals in `spec\` were read
instead of the two uncited documents in `docs\` that the code had been written from, and nearly
everything those two documents taught turned out to be invented. Twenty-odd commits, all on
`master`, all pushed. The full evidence is the modes document above; its nine sections are the
reading order.

**What changed in the emulator, in the order it was found:**

- **No TDV has DECRQM.** The mode query it answered was made up. A TDV reports with NDRQ,
  `CSI Ps x`, and that is implemented with report types 1, 2, 5 and 6.
- **Every private mode number was wrong.** 66 was the only right number and it was inverted; the
  2115 switch is `CSI 66 l` to enter and `ESC Q` to leave. Proven from a real TDV2200 termcap, not
  inferred.
- **The 2215 was stealing DEC private modes 1, 2 and 40** for invented "extended", "transparent"
  and 2115 switches. Extended mode IS the Extended Control switch and now reads off it; transparent
  mode is a keyboard soft-switch no host can set.
- **NDRAR and NDFC were swapped everywhere** — emulators, sequence builder, trace decoder. 7C is
  NDRAR, 7D is NDFC.
- **The message lamps are four, not three.** EXPAND, APPEND, BUSY, MESSAGE, each off/lit/blinking,
  driven by `CSI ? Ps A/B/C`. Nothing decoded those sequences before. The virtual keyboard draws all
  four — the one narrow exception to the standing DO-NOT on that window, granted in the same answer.
- **All eight bits of NDRQ report 2 parameter 3 now carry state**: line wrap, roll/page, key click,
  the two label modes, the three-position decimal separator, numeric pad. Six of the eight had
  answered 0 whatever the terminal was told.
- **Three 2215 mode numbers named switches we already had under DEC numbers** — 32 is LNM, 55 is
  DECARM (inverted), 68 is the cursor shape — and were ignored on the 2215's own number.
- **Mode 62 GRM**, which a real PED sends twice in one sequence, is held and walks its three
  positions. Its semantics are not built — see below.
- **The TestServer** sent invented sequences at real terminals in six places, printed the letters
  "SPA" and "NDCLED" on screen instead of sending anything, and had six key-detection branches that
  were deaf to every escape sequence. All fixed by driving it over a socket, which is the method
  that found every one of them.

**Two things I got wrong and withdrew the same day**, both in the git history with the correction:

- "26 tests still assert nothing" — measured no such thing (`be6eec4`).
- "Two numpad keys are named a position out" — four keyboard photographs say the registry is right
  and the one contradicting table is OCR-damaged (`9736e52`, modes document section 8).

---

## The decisions Ronny made today — all recorded in the plan's standing calls

| Decision | Answer |
|---|---|
| NDRQ report type 1 emulator level | Claim 93, the SINTRAN term-type — a choice, not a measurement |
| Numeric pad switch (mode 80) | Flag and report only |
| 2115 mode obeying CSI | Leave it obeying CSI for now |
| Message lamps | Four-lamp model per ND-1200 §5.52, virtual keyboard lamps included |
| `ESC %` national version | Keep it; the keyboard-language dropdown already does the real job |
| GRM semantics | **"for all ui you think you need me, start with ui unit test first"** |

That last one is a standing rule now, in memory and in the plan: anything that shows on screen gets
a headless test and a rendered PNG before it gets a question.

---

## Where to pick up

**Start here:** `docs\PLAN.md`. Every open item is a checkbox line there, grouped by what it needs.

**If continuing the TDV thread**, in this order:

1. **GRM's semantics** — the next step is a headless rendered test showing what ATTR operation
   should put on screen (an invisible attribute cell, the run after it drawn in that attribute)
   beside what the program does today; the PNGs decide. Three tests in
   `TdvGraphicRenditionModeTests` pin today's behaviour and are meant to fail when it changes. The
   power-up position (2215 ships on ATTR, we start on SGR) is the one thing a test cannot settle.
2. **61 CLL, the lamp-clear policy** — three positions, now has lamps to act on, needs no decision.
3. **The numpad ENTER Function-mode code** `CSI 81 _` — one registry row, needs Ronny because the
   registry is keyboard territory.

**Blocked on a document nobody has found**, and honestly so: NDSS1–9 (which glyph set each names),
the 2200's ESC-letter character-set lead-ins (the manual contradicts itself between §10.1 and
§10.2), and NDVIDEO's final byte.

**Blocked on Ronny or a machine:** everything else in the plan — the Gateway tab, M6.5, the D100
sittings, the Phase 4 hardware questions, and the by-hand cases.

---

## Things the next session must know

- **Drive the TestServer over a socket to test it.** `scratchpad\probe.ps1` (session scratchpad,
  not in the repo — rebuild it from the description in the modes document if needed) answers telnet
  negotiation with refusals, waits on silence, and sends keys. Reading the server's code found
  nothing; driving it found everything.
- **The TestServer resets its menu state per client, but shares it between simultaneous clients.**
  Plan item.
- **The Bash heredoc trap is real and bit again today** — a `\n` inside a Python string written
  through a heredoc became a newline in a C# comment and broke the build. Build paths from
  `chr(92)`, or use the Write tool.
- **Kill your own TestServer before building** — it holds
  `RetroTerm.Core.Protocols.TelnetServer.dll`. By recorded PID, never by name. The other
  `dotnet.exe` processes on the box today belonged to a live RetroCore run in another session.
- **The virtual keyboard is still a DO-NOT**, except the four lamp indicators, changed with
  permission on 11 September.
