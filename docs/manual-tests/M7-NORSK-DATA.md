# M7 — Norsk Data `ESC "` graphics, on a real machine

**Full path:** `docs\manual-tests\M7-NORSK-DATA.md`
**Parent:** `docs\manual-tests\INDEX.md`

**This section produces a work list rather than a verdict**, and that makes it different from every
other one here. Six of the thirty ND graphics modes are implemented. The other twenty-four are
**counted, not guessed** — the module records every sequence it did not handle — and one real
session turns that count into a list of what actually gets used.

---

## M7.1 — Read the unhandled sequences off a real machine

**Needs:** a real ND host. Nothing else can do this, and no test server can imitate it.

**The machines** are in `terminal_connlist` — D100 on localhost:9010, D102 on localhost:9102, the
5000 CPU on localhost:4500, and OPCOM. **Whether any of them is running today is a thing to check,
not to assume.**

**Do:**
1. `terminal_open` once and keep the session. Reconnecting mid-program hangs the line on these
   machines.
2. Send ESC first on a fresh SINTRAN connection, or it will not prompt.
3. Log in with **one** send: `SYSTEM\r\r`. Splitting it bounces back to a fresh prompt.
4. **Do ordinary graphics work** — whatever a real user does on it. This is the part that cannot be
   scripted, because the point is what a real program sends, not what we think it sends.
5. Before disconnecting, read `NorskDataGraphicsModule.UnhandledSequences`. It is keyed
   `mode:final`.

**Pass:** you come away with a **list**. That single reading turns the largest remaining ND item
from guesswork into a work list, which is why it was placed early in the order of things that
need a real host.

**Machine cover:** `NorskDataGraphicsRenderingTests` and the rendered `nd-graphics-*.png` files
cover the six modes that are built — the rectangle, drawing over text, hiding and clearing. Nothing
can cover the other twenty-four, because nothing here knows what they do.
The first pass, 20 August 2026 (Ronny + Claude), ran PED alone and produced a five-key work list;
its capture is in `FINDINGS-2026-08-20.md`.

**THE SWEEP WAS RUN, 28 August 2026**, against D100 on `localhost:9010` as a TDV2200, reading
`UNHANDLED` after each program rather than once at the end. Programs run, in order: `QED`,
`HELP` (the full command listing), `MAIL`, `PED`, `LOOK-AT`, `SPLIT`, `CHAT` (the machine's own
NDCHAT 2.1), `CHATBK`, `FILE-STATISTICS`.

**Sequences found - six keys, not five:**

```
DCS L            mode 62 set      mode 30 reset      mode 7 reset      mode 80 reset
CSI R            <- NEW. Nothing had ever looked at a program's EXIT.
```

**Where the sixth came from.** Every one of the first five is sent at STARTUP, which is all the
20 August session captured. `CSI R` is sent at EXIT, by PED and by NDCHAT alike - they share the
ND screen-handling library, so the ritual is the library's rather than either program's:

```
1B 5B 31 3B 32 3B 33 3B 34 52      ESC [ 1;2;3;4 R
1B 5B 36 36 3B 36 32 3B 38 30 6C   ESC [ 66;62;80 l
```

A CSI ending in `R` is CPR, the cursor position REPORT - something a terminal sends a host, and it
carries two parameters, not four. So this is a TDV command sharing a final byte, and no manual held
here says what it does. Counted, not implemented, which is the answer mode 62 already had. Pinned
as `tests\RetroTerm.Tests\TDV\PedExitCaptureTests.cs`.

**And the exit ritual exposed a real defect in our own emulator, now fixed.**
`TDVEmulatorBase.HandleNDSpecificSequence` tested `parameters[0]` alone and, on a match, handed the
WHOLE sequence to `HandleNDPrivateSequence`, which also reads only the first parameter. So in
`ESC [ 66;62;80 l` mode 66 was handled - it is ND's 2115 compatibility alias - and modes 62 and 80
were **neither acted on nor counted**. They simply vanished.

That is worse than losing two modes. The unhandled counter is the instrument this whole section is
read off, and it was under-reporting in exactly the place a real host mixes ND modes with ordinary
ones. Every test in the suite had sent these numbers ONE AT A TIME, where they count correctly,
which is why nothing was red.

The fix routes every number in a mode list to the handler that owns it. A list with no ND mode in
it behaves exactly as before. `IRM` set beside an ND mode now actually inserts, where it used to be
silently dropped - that case has a test too.

**Judge:** hardware.
**Result:** **DONE - the sweep produced a six-key list and one fixed defect**  **Date:** 28 Aug 2026  **By:** Claude, on D100

---

## M7.2 — Two assumptions marked in the code, both settled by the same session

While connected, and with a byte trace running, settle these. **Both are marked as assumptions in
the source**, which is why they are worth a minute each:

| # | Question | How to tell |
|---|---|---|
| M7.2a | Are mode 8's rectangle-fill coordinates in **logical** space? | Fill a rectangle whose corners you know, and see whether it lands where logical coordinates say or where screen ones do |
| M7.2b | Does **defining** a circle (mode 24) also **draw** it? | Define one and send nothing else. If it appears, defining draws |

**Result M7.2a:** ______  **Result M7.2b:** ______  **Date:** ______

---

## M7.3 — The rest of a real session

**Do:** while you are there, use it normally — editors, listings, whatever the machine is for.
**Pass:** nothing the TestServer never produces goes wrong. Anything that does is worth the bytes:
capture with `terminal_traceread` and the screen with `terminal_snapshot`, because only the bytes
can be turned into a test.
**Judge:** hardware.
**Result:** ______  **Date:** ______  **By:** ______

---

## What this section cannot settle

- **Everything, without a machine.** This is the one section where the whole content is blocked on
  hardware being up.
- **The two ND model identification bytes** are a separate item and need a machine that may never
  appear — `docs\PLAN.md`, Phase 4.
