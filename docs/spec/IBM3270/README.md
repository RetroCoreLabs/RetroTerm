# IBM 3270 — sources for the emulation

Everything RetroTerm needs to emulate an IBM 3270 display station (3278/3279) over TN3270 and
TN3270E. Started 22 November 2026. The outstanding work is in `docs\PLAN.md`, "IBM 3270".

**How the 3270 differs from every terminal RetroTerm has today:** it is not a character stream.
The host sends a whole screen at once (a command, a Write Control Character, then orders that set
the buffer address and start protected or unprotected fields), the user types into the fields
locally, and nothing goes to the host until an attention key (Enter, PF1-PF24, PA1-PA3, Clear)
is pressed. Then the terminal sends back only the fields that were changed. Text is EBCDIC, not
ASCII.

## IBM manuals — `IBM-manuals\`

All downloaded from bitsavers.org on 22 November 2026 and checked to start with `%PDF`.

| File | What it is for us |
|---|---|
| `GA23-0059-4_3270_Data_Stream_Programmers_Reference_Dec88.pdf` | **THE specification.** Commands, WCC, orders, field and extended attributes, structured fields, Query Reply, AIDs, 12/14/16-bit addressing (appendix D). Chapter 4 is orders and attributes. |
| `GA27-2837-9_3270_Character_Set_Reference_Apr87.pdf` | **EBCDIC.** Every national code page, the I/O interface code tables, control character assignments (Fig. 9-1, page 9-2). |
| `GA23-0061-2_3274_Control_Unit_Description_and_Programmers_Guide_Mar85.pdf` | The control unit: command codes, sense/status, keyboard and operator-information-area behaviour. **Not in git** (63 MB; Ronny's decision, 22 November 2026). Download it into this folder from <https://bitsavers.org/pdf/ibm/3274/GA23-0061-2_3274_Control_Unit_Description_and_Programmers_Guide_Mar85.pdf> |
| `GA27-2739-22_3270_Information_Display_System_Introduction_Oct88.pdf` | The model family, screen sizes, keyboards offered. |
| `GA33-3056-0_..._Color_and_Programmed_Symbols_3278_3279_3287_Sep1979.pdf` | 3279 colours, extended highlighting, programmed symbol sets. |

Not yet found as a free download (I have not verified they exist on bitsavers): a 3278/3279
operator's guide with the **original IBM keyboard layouts** (typewriter, APL, data entry). The
bitsavers directory listing refused automated reads (HTTP 403), though direct file links work.

## Telnet — `RFC\`

| File | What it is |
|---|---|
| `rfc1041.txt` | Telnet 3270 regime option (1988) |
| `rfc1576.txt` | TN3270 current practices — the plain TN3270 negotiation (TERMINAL-TYPE `IBM-3278-2`, BINARY, EOR) |
| `rfc1646.txt` | TN3270 extensions for LU name and printer selection |
| `rfc1647.txt` | TN3270E, first version (replaced by 2355) |
| `rfc2355.txt` | **TN3270E** — the one to implement |
| `rfc6270.txt` | The `tn3270://` URI scheme |

## Keyboard — `Keyboard\`

From <https://www.seasip.info/VintagePC/kb3270.html> (John Elliott, updated 2013).

**Note:** that page is about the **Key Tronic KB 3270 Plus**, a 122-key keyboard made for PCs
running 3270 emulator programs. It is not the keyboard of an IBM 3278 terminal, but its key set
(24 PF keys in two rows above the main block, a ten-key block on the left with Clear, ErEOF,
PA1-PA3, Attn, SysReq, Reset and so on) is the usual "3270 on a PC" layout and a good model for the
on-screen keyboard.

| File | What it is |
|---|---|
| `kb3270plus.jpg` | Photo of the keyboard (with the Enhanced 101 keycaps fitted). Too small to read the legends. |
| `kb3270_keys.ods` | The page author's spreadsheet of every key and its scancodes in each mode. |

A real IBM 3279 with its keyboard (console-style), photo by the Retro-Computing Society of Rhode
Island, licence CC BY-SA 3.0: <https://commons.wikimedia.org/wiki/File:IBM-3279.jpg> (supplied by
Ronny, 22 November 2026; not copied here — the download was refused with HTTP 429, and the licence
asks for credit if it is ever shipped).

The key legends on the web page are drawn as pictures; open the page in a browser for them.
The same site also has a page on the IBM 1397000 122-key keyboard:
<https://www.seasip.info/VintagePC/ibm_1397000.html> (not downloaded yet).

## Other references (not downloaded)

- IBM z/OS Basic Skills, "TN3270 Enhanced": <https://www.ibm.com/docs/en/zos-basic-skills?topic=mainframe-tn3270-enhanced>
- IBM z/OS Basic Skills, "Introduction to the 3270 terminal": <https://www.ibm.com/docs/en/zos-basic-skills?topic=enhanced-introduction-3270-terminal>
- IBM z/OS Basic Skills, "3270 data stream": <https://www.ibm.com/docs/en/zos-basic-skills?topic=terminal-3270-data-stream>
- IBM z/OS Basic Skills, "SNA/IP implementation": <https://www.ibm.com/docs/en/zos-basic-skills?topic=mainframe-snaip-implementation>
  (all four supplied by Ronny, 22 November 2026; IBM's own plain introduction — a main source for `docs\IBM3270-INTRODUCTION.md`)

- Wikipedia, <https://en.wikipedia.org/wiki/IBM_3270> — model table, command and order codes, and
  a full list of the IBM manuals above with bitsavers links.
- Tommy Sprinkle, 3270 data stream programming: <https://www.tommysprinkle.com/mvs/P3270/>
- The 3270 font, a TTF remake: <https://github.com/rbanffy/3270font>
- Greg Price, 3270 graphics: <https://www.prycroft6.com.au/misc/3270grfx.html>
- IND$FILE file transfer protocol, SS-HCS12-1372-00, linked from <http://x3270.bgp.nu/documentation-misc.html>

## x3270 — oracle and second opinion, never a source of code

x3270 (<https://github.com/pmattes/x3270>, a local clone sits beside the Hercules sources on the
development machine; the Windows version is `wc3270`) is the reference open-source 3270
emulator. **Rules for using it**, the same clean-room rule as for xterm and libvterm:

- READ it to understand behaviour the manuals leave open, and RUN it (`s3270` is scriptable) as an
  oracle to compare our screen against.
- Never copy, paste or line-by-line translate its code. If something has to be ported, it is
  rewritten in C# from the IBM manual, with x3270 only as a cross-check.
- Agreeing with x3270 is not proof. The IBM manual is the authority; x3270 catches regressions.

## tnz — IBM's own Python TN3270 library, second oracle

tnz (<https://github.com/ibm/tnz>, Apache 2.0, written by IBM; a local clone sits beside the x3270
clone on the development machine) is a Python TN3270 client for z/OS. Same rules as x3270: read it,
run it as an oracle, never copy its code. The parts worth reading:

- `tnz/tnz.py` (170 KB) — the TN3270/TN3270E negotiation and the data stream: buffer, fields,
  orders, AIDs, Read Modified. Being IBM's code, it is a useful second opinion where x3270 and
  the manual seem to disagree.
- `tnz/cp310.py` — code page 310, the 3270 graphic-escape (APL) character set.
- `tnz/zti.py` — its full-screen terminal front end, and how it maps PC keys to 3270 keys.
- `tnz/py3270.py` — a py3270-compatible scripting layer, handy for oracle tests.
- `test/` — its own tests, a source of known-good byte streams.

Two oracles agreeing is still not proof; the IBM manual decides.

**Not relevant to us:** RetroTerm runs the 3270 only inside its own window. Everything in tnz (and
in x3270's `c3270`) that exists to run inside a Windows or Linux text console is not a
specification: `_termlib.py`, the curses drawing, the per-console key strings in `zti.py`
(PuTTY, Git Bash, ssh), the session column it draws because a console has no tabs (RetroTerm's
tabs do that), and the command-line shell. Read tnz for the 3270 behaviour, not for its console
front end.
