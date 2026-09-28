# Handover: give RetroTerm an MCP server so an LLM can drive a terminal

**Written:** 2026-08-04
**From:** the agent working in the NDInsight repository, `SINTRAN\XMSG` (SINTRAN III / XMSG / COSMOS)
**For:** whoever works on RetroTerm (this repository)
**Status of this document:** a REQUEST and a design proposal. Nothing here is built yet.

---

## 1. What I need, in one sentence

I need to open a connection to a live ND machine, type a command, **wait until the machine has
actually finished answering**, read what it said, and repeat - from inside an LLM session, without
a human at the keyboard.

Today I do that with a 130-line PowerShell script,
`SINTRAN\XMSG\tools\ndterm.ps1` in the NDInsight repository. It works, but it is the wrong tool, and it
has cost me real hours. Details in section 2, because the failures are the design input.

---

## 2. Why the script is not good enough (two failures from today)

These are not hypothetical. Both happened on 2026-08-04 while bringing up a COSMOS file server.

### 2.1 The login raced, and 28 minutes vanished

`ndterm.ps1` logged in with **fixed delays**: send ESC, wait 1500 ms, send the user name, wait
1500 ms, send the password. The ND (a RetroCore-emulated ND-100 running SINTRAN III) had just
restarted XMSG and was busy, so its banner arrived late. The user name went out **before the
`ENTER` prompt existed**. From then on everything was one step out of phase:

```
ENTER
PASSWORD:
===> [X-C]        <- the first real command answered the PASSWORD prompt
ENTER             <- back to the start
```

The session never left the login loop. The 13 configuration commands that followed each waited
their full 120-second timeout and then gave up. **28 minutes, no configuration applied**, and the
transcript read as though the ND was broken when the fault was entirely in my script.

I have since fixed that particular bug (wait for `ENTER`, then for `PASSWORD:`), but the shape of
the problem is permanent: *a script that guesses timing will keep guessing wrong.*

### 2.2 Prompt detection on the raw byte stream is guesswork

My "wait for the prompt" is `text.TrimEnd().EndsWith(prompt)` over the accumulated **raw bytes**.
That is wrong in principle:

- The ND emits its answer in several small writes, so I sleep 120 ms and hope the rest arrives.
- Escape sequences, cursor moves and repaints are in that byte stream. A prompt that the user sees
  on screen may never appear as those literal bytes in that order.
- A prompt echoed in the MIDDLE of a long mode-file listing ends the wait early. I patched around
  that with "only accept the prompt at the very end", which is a heuristic, not a fix.

**RetroTerm already solves this properly and I do not.** It runs the bytes through a real terminal
emulator into a screen buffer. The question "does the screen currently end with `X-C:`" has an
exact answer there. In my script it does not.

That is the single strongest argument for putting this in RetroTerm rather than improving my
script: *the screen is the truth, and only RetroTerm has the screen.*

---

## 3. What I verified about RetroTerm before writing this

I read these files. I have **not** built or run RetroTerm, and I have not written any of it.

| Thing | Where | Why it matters here |
|---|---|---|
| `IConnection` | `src\RetroTerm.Core.Protocols\IConnection.cs` | `ConnectAsync` / `SendAsync` / `DisconnectAsync`, plus `DataReceived`, `StatusChanged`, `ErrorOccurred` events. Already the unified seam over Telnet / SSH / Serial / WebSocket. |
| `TelnetConnection`, `SerialConnection`, `SSHConnection` | `src\RetroTerm.Core.Protocols.Net\` | The transports I would target. I use the raw TCP terminal ports on RetroCore. |
| `TerminalSession` | `src\RetroTerm.Core\Session\TerminalSession.cs` | Composes connection + emulator + buffer. Raises `DisplayInvalidated`. This is the object an MCP tool call should drive. |
| `TerminalBuffer`, `TerminalCell` | `src\RetroTerm.Core\Terminal\Buffer\` | The rendered screen. This is what I actually want to read. |
| `ITerminalEmulator`, `VT100Emulator`, `TDV` | `src\RetroTerm.Core\Terminal\Emulators\` | VT100 and TDV2200. SINTRAN talks to both. |
| `ISessionDataLogger` | `src\RetroTerm.Core\Session\` | Existing session capture - probably reusable for transcripts. |

**Assumption I could not check:** that `TerminalSession` can run **headless**, with no Avalonia
window. If it cannot today, that is the first real piece of work, and it is worth doing regardless
of MCP - it also makes `RetroTerm.Tests` able to test the emulator against a live server.

---

## 4. Proposed MCP tool surface

Deliberately small. Every tool maps to something I do by hand today.

### Connection lifetime

- **`terminal_open`** -> `sessionId`
  Params: `transport` (`telnet` | `raw-tcp` | `serial` | `ssh`), `host`, `port` (or `device`,
  `baud`, `dataBits`, `parity`, `stopBits`), `emulator` (`vt100` | `tdv2200`), `columns`, `rows`.
  **Must not auto-close.** See section 6 on why one long-lived connection matters.

- **`terminal_close`** - params: `sessionId`.

- **`terminal_list`** - the open sessions, so a new LLM turn can find a session it opened earlier.

### Talking

- **`terminal_send`** - params: `sessionId`, `text`, `appendReturn` (default true).
  Also needs a way to send bare control bytes - ESC is how you wake a SINTRAN line, and it is how
  you recover a wedged one. Either a `bytes` parameter (hex) or `sendEscape: true`.

- **`terminal_wait_for`** - **the important one.** Params: `sessionId`, `pattern`,
  `matchType` (`literal` | `regex`), `where` (`screen-tail` | `anywhere-on-screen` |
  `scrollback`), `timeoutMs`, and ideally `idleMs` ("the screen has not changed for N ms").
  Returns: whether it matched, what matched, the screen at that moment, and **how long it waited**.
  Matching against the RENDERED SCREEN is the whole point - see section 2.2.

- **`terminal_read_screen`** - params: `sessionId`, optional `stripTrailingBlanks`.
  Returns the visible screen as plain text plus cursor row/column. An attribute-aware variant would
  be a bonus; plain text covers almost everything I do.

- **`terminal_read_new`** - everything decoded since my last read, so I can capture a long listing
  that scrolled past.

### Diagnosis

- **`terminal_status`** - connection status, emulator, size, bytes in and out, time since last
  byte received. "Time since last byte" alone would have told me instantly that the ND was busy
  rather than broken.

### Scripting (you asked about this - I think it is worth having)

- **`terminal_run_script`** - params: `sessionId`, and a list of steps, each
  `{ send, waitFor, timeoutMs, optional }`. Returns a per-step transcript with timings and which
  step failed.

**Why scripting as well as single steps:** the login (ESC -> `ENTER` -> user -> `PASSWORD:` ->
password) is always the same five steps, and a restart sequence is 30 of them. Doing that as 30
MCP round trips is slow and wastes LLM context. But single-step tools must stay, because when
something goes wrong I need to poke at the machine one command at a time and look at the screen.
**A script that stops on the failing step and hands the live session back is the ideal.** Do not
make scripting the only path.

---

## 5. What I would delete on my side

If this exists, `ndterm.ps1` and most of `restart-xmsg-cosmos.ps1` (both in
`SINTRAN\XMSG\tools\` in the NDInsight repository) become a stored script plus a few tool calls. I am
happy to be the first user and to report back.

---

## 6. Hard-won rules - please do not design these away

These come from driving real SINTRAN machines. Each one has already bitten.

1. **One connection per session, held open.** Reconnecting to a RetroCore terminal port while a
   program is running **wedges the line**. The session must survive across many MCP calls and many
   LLM turns. Do not open-and-close per command.

2. **ESC first on a fresh connection.** A new connection shows only the RetroCore banner; ESC
   produces the SINTRAN banner and the `ENTER` prompt. ESC also recovers a wedged line. So sending
   a raw ESC must be a first-class operation, not an afterthought.

3. **Never guess a duration.** Some commands announce their own slowness - `START-NET-SERVER`
   literally prints `wait 10 sec!`. A fixed delay that is too short sends the next command INTO the
   busy one and garbles both. `terminal_wait_for` must be the normal way to sequence, and a plain
   sleep the exception.

4. **A timeout must still return what the machine said.** When a step misbehaves, the partial
   output is the most valuable thing I have. My script does this and I would have been lost
   without it.

5. **Report the wait time.** Knowing a command took 23 seconds instead of 200 ms is often the whole
   diagnosis. I found a completely separate bug today purely from timestamps in a log.

6. **Do not swallow errors into a status code.** If the connection drops mid-script, say so, say
   which step, and show the screen.

---

## 7. Open questions for you

I do not know the answers to these - they are yours to decide.

1. **Can `TerminalSession` run headless today?** If not, what is in the way?
2. **Where should the MCP server live?** A new `src\RetroTerm.Mcp` console project seems natural
   (stdio MCP), separate from `RetroTerm.Desktop`. But if you would rather host it *inside* the
   running desktop app so a human can watch the LLM drive the same window, that is arguably better
   for debugging - and it is your call. The NDInsight Unity work already uses a "server lives in
   the running editor" model on `127.0.0.1:8088`, and it works well.
3. **Should the LLM and a human be able to share one session?** Watching it type would be genuinely
   useful. It also raises the question of who owns the keyboard.
4. **Transcript format** - is `ISessionDataLogger` the right thing to reuse?
5. **Security** - localhost only, presumably. Worth stating explicitly, since this tool can type
   arbitrary commands into a machine.

---

## 8. Priority, honestly

If you only do part of this, do these three and I can throw my script away:

1. `terminal_open` / `terminal_send` / `terminal_close` with a **persistent** session
2. `terminal_wait_for` matching against the **rendered screen**
3. `terminal_read_screen`

Scripting, serial, SSH and the rest can follow.

---

## 9. Contact

I am reachable through Ronny. If anything here is wrong about how RetroTerm is put together, say
so - section 3 is what I read in one pass, not deep knowledge of your codebase.
