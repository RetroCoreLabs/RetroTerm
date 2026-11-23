# The IBM 3270 terminal - an introduction for people who have never used one

This page is for you if you want to use RetroTerm to log in to an IBM mainframe (for example
MVS 3.8J on the Hercules emulator, the "TK5" system) and the words *3270*, *field*, *PF key*,
*EBCDIC* or *LU* mean nothing to you yet. It explains what is different about this terminal,
which keys do what in RetroTerm, and walks you through a first login.

The details behind every statement here are in the spec files in `docs\spec\IBM3270\`
(`3270-DATA-STREAM.md`, `KEYBOARD.md`, `EBCDIC.md`, `TN3270-NEGOTIATION.md`,
`IND-FILE-TRANSFER.md`), with the IBM manual page for each.

---

## 1. A terminal that sends a whole screen at once

The terminals most people know (a VT100, an xterm, a TDV) send every key to the computer the moment
you press it, and the computer decides what to show.

A 3270 works the other way round. The mainframe sends a complete **form**: some text you can read,
and some boxes you can type into. You fill in the boxes on your own terminal. **Nothing goes to the
mainframe while you type.** Only when you press an **attention key** (Enter, a PF key, Clear, a PA
key) does the terminal send the boxes you changed, in one go, and wait for the answer.

That is why it is called a *block-mode* terminal, and it is the reason for most of what follows:

- Typing is local. Backspace, Insert, Delete and the arrow keys only change your screen.
- The mainframe sees nothing until you press Enter (or another attention key).
- After you press it, the keyboard is **locked** until the mainframe answers.

## 2. Fields: the boxes on the form

The screen is divided into **fields**. Each field starts with an invisible marker (it takes up one
character position, shown as a blank) that says what kind of field it is:

- **Protected** fields are text you can read but not change: titles, labels, messages. If you try
  to type into one, the keyboard locks and line 25 shows a locked-keyboard symbol.
- **Unprotected** fields are the boxes you type into, for example the box after `Logon ===>`.
- A field can be **numeric only**, **bright** (highlighted) or **hidden**. A password box is a hidden
  field: what you type is not shown on the screen.

The **Tab** key jumps to the start of the next box you can type into; **Shift+Tab** goes back.
**Home** goes to the first box on the screen. You rarely need the arrow keys.

A mouse click puts the cursor where you click. A double click puts the cursor there and presses
Enter, which picks a line in a menu; in Manage Connections, on the Keyboard tab, you can make a
double click select the word instead, as wc3270 does.

## 3. The attention keys: the ones that talk to the mainframe

| Key on the 3270 | What it usually means to the program | Key in RetroTerm |
|---|---|---|
| **Enter** | "Here is what I typed, do it." | Enter |
| **PF1 to PF12** | Program function keys. The program decides; in ISPF, PF1 = help, PF3 = back/exit, PF7/PF8 = page up/down. | F1 to F12 |
| **PF13 to PF24** | More of the same (often the same meaning as PF1-PF12). | Shift+F1 to Shift+F12 |
| **Clear** | Empty the screen. In TSO it gives you an empty screen to type the next command on. | Alt+C |
| **PA1, PA2, PA3** | "Program attention": sent without any of your typing. In TSO, PA1 interrupts a running command. | Alt+1, Alt+2, Alt+3 |
| **Attn** | Interrupt (see section 7 about TN3270E). | Alt+A |

PgUp and PgDn send PF7 and PF8, the page keys of most mainframe programs.

## 4. Keys that only change your own screen

| 3270 key | What it does | Key in RetroTerm |
|---|---|---|
| **Reset** | Unlocks the keyboard after an error (for example after typing into a protected field), and turns Insert mode off. | Alt+R |
| **Erase EOF** | Erases from the cursor to the end of the box. | Alt+E |
| **Insert** | Insert mode on/off. Line 25 shows an `I` while it is on. | Insert (Alt+I turns it on) |
| **Delete** | Deletes the character under the cursor. | Delete |
| **Backspace** | Erases the character to the left, as wc3270 does. | Backspace |
| **Dup**, **Field Mark** | Special characters for data-entry programs. | Alt+D, Alt+F |
| **End** | Goes to the end of the text in the box. | End |

Keys a PC keyboard does not have (Sys Req, Erase Input, Cursor Select, Newline and the others) are
on the on-screen keyboard (Keyboard menu), and every key has a name scripts and MCP can press with
`SENDKEY`. The full list, with the IBM manual pages, is `docs\spec\IBM3270\KEYBOARD.md`.

## 5. Line 25: the status line

Under the 24 (or more) lines of the screen is one more line the mainframe cannot write to. IBM calls
it the *operator information area*. RetroTerm draws it as wc3270 does:

- In the first columns: a `4`, then `A` (or `B` while TN3270E is in force, see section 7), then a
  box that is solid when you are connected to a program.
- `X Wait` or `X SYSTEM`: you pressed an attention key and the mainframe has not answered yet. Wait.
- `X` followed by a symbol (for example `X -f`): the keyboard is locked because of something you did.
  Press **Reset** (Alt+R).
- `I`: Insert mode is on.
- On the right: the cursor position as row/column.

**If typing does nothing, look at line 25 first.** Nearly always the keyboard is locked and Reset
is the answer.

## 6. EBCDIC and code pages

Mainframes do not use ASCII. They use **EBCDIC**, a different numbering of the letters, digits and
signs (in EBCDIC, `A` is hex C1, not 41). RetroTerm turns EBCDIC into the characters on your screen
and back again, so you never see the numbers.

There is not one EBCDIC but many **code pages**, one per country or language, which differ in a few
signs (`[`, `]`, `$`, `#`, national letters such as Æ Ø Å). If brackets or national letters look
wrong, the code page does not match the mainframe's. Pick it in Manage Connections, on the
**Keyboard** tab, in the 3270 code page box. When nothing is picked RetroTerm uses code page 037 (USA/Canada); 277 is Denmark and Norway.

## 7. TN3270 and TN3270E, and what an LU name is

A real 3270 was wired to a control unit next to the mainframe. Today the same screens travel over
the internet with **TN3270**: telnet, the old text protocol, with a few agreed rules on top
(RFC 1576). This is what RetroTerm uses, normally on port 23 or, for Hercules/TK5, port 3270.

**TN3270E** (RFC 2355) is a newer version. It adds two things you may meet:

- **LU names.** Inside the mainframe each terminal has a name, its *logical unit* or **LU** (for
  example `TCP00034`). With plain TN3270 the mainframe picks one for you and you never see it. With
  TN3270E you may ask for a particular one; some sites give each user or each printer a fixed LU
  name, and then you must ask for that name to get the right screen. If nobody gave you an LU name,
  you do not need one.
  In RetroTerm you ask for one by adding `@` and the name to the terminal type, for example
  `IBM-3278-2@TCP00034`. The name the mainframe gave you is shown on line 25.
- **Attn and Sys Req behave differently.** RetroTerm builds *basic* TN3270E, without the optional
  extra functions. Under it Attn has nothing to send and locks the keyboard with `X -f` (Reset
  unlocks it), and Sys Req does nothing, exactly as wc3270 does.

RetroTerm agrees on TN3270E by itself when the mainframe offers it, and falls back to plain TN3270
when it does not. TK5 does not offer TN3270E, so with TK5 you are always on plain TN3270.

## 8. Models and screen sizes

The IBM 3278 came in four models with different screen sizes:

| Model | Screen |
|---|---|
| 2 | 24 lines x 80 columns |
| 3 | 32 x 80 |
| 4 | 43 x 80 |
| 5 | 27 x 132 |

Every model starts with the 24 x 80 screen; a program that knows about the bigger one switches to it.
In Manage Connections the terminal box shows **IBM-3278** and the **size** box picks the model. Model
2 is the safe choice: every mainframe program can use it.

## 9. Moving files: IND$FILE

To copy a file between your PC and the mainframe, the mainframe has a program called **IND$FILE**.
RetroTerm drives it for you with two commands, in scripts and over MCP:

- `HOSTSEND "C:\path\file.txt" DATASET.NAME` copies a PC file to the mainframe.
- `HOSTRECEIVE "C:\path\file.txt" DATASET.NAME` copies a mainframe data set to the PC.

Run them at TSO's `READY` prompt, not inside ISPF. In a script they can also be written with IBM's
own names, `IND$PUT` and `IND$GET`. Details: `docs\MCP-AND-SCRIPTING.md` and
`docs\spec\IBM3270\IND-FILE-TRANSFER.md`.

Without a script, use **Transfer, IBM Host Files...** on a 3270 tab. Type a data set name, quoted or
not (for example `HERC01.DEMO.CNTL(HELLO)`), choose Text or Binary, and press **View** (shows the
data set as text in its own window), **Download...** (saves it to a PC file you pick) or
**Upload...** (sends a PC file you pick). For a new data set you can give RECFM, LRECL and
BLKSIZE on Upload; leave them empty and the mainframe decides. A member of a library only moves as
Text and takes none of those three. The window first checks that TSO is at `READY` and refuses
otherwise, so it never types into ISPF or another program. When the transfer ends it presses
Clear, so you get an empty screen back at `READY`, and shows the mainframe's answer (TRANS03 or
TRANS04 means it worked) at the bottom of the window. One transfer runs at a time; Cancel stops
waiting for it.

## 10. A first login to TK5

TK5 is a ready-made MVS 3.8J system for the Hercules emulator. Its README lists the ready-made
users (HERC01 to HERC04) and their passwords.

1. Start TK5 and wait until its console says it is up.
2. In RetroTerm open **Manage Connections** and make a new connection:
   protocol **TN3270**, host `localhost`, port `3270`, terminal **IBM-3278**, size
   `80x24 (model 2)`. Save and connect.
3. You may first see a Hercules information screen. Wait: after a moment TK5 replaces it on its own
   with the logon screen, which has `Logon ===>` near the bottom. (Do not press Clear there; that
   makes the screen come a second time and throws away what you typed.)
4. Type your user id, for example `HERC01`, and press **Enter**. Line 25 shows `X SYSTEM` for a
   moment: the keyboard is locked while MVS works.
5. At `ENTER CURRENT PASSWORD` type the password and press **Enter**. Nothing shows while you type;
   that is a hidden field doing its job.
6. Messages scroll past. When the screen ends with `***`, press **Enter** to see more.
7. You are now in the TK5 menu (ISPF/REVIEW). **PF3** (F3) goes back one level; pressing it from the
   main menu gives you TSO's `READY` prompt, where you type commands such as `LISTCAT`.
8. Before typing a new command at `READY`, press **Clear** (Alt+C) for an empty screen.
9. To finish, type `LOGOFF` at `READY` and press Enter.

If the keyboard stops responding at any point, look at line 25 and press **Reset** (Alt+R).

## 11. Printing: the 3287 printer

A 3270 printer (the IBM 3287) is a second connection, next to the terminal. The mainframe writes
to it the same way it writes to a screen, and the printer prints what it was sent.

1. In **Manage Connections** make a new connection: protocol **TN3270**, host `localhost`, port
   `3270`, terminal **IBM-3287-1**. That name is the only printer name Hercules takes; it gives
   such a connection its printer device (on TK5 that is device `00C7`).
2. On the **Printer** tab pick the output: **Text file**, **PDF**, or **Printer**. Printer makes the
   PDF and hands it to the system's print command (the default printer on Windows, `lp` on Linux
   and macOS). With Printer you can tick **Delete the PDF after printing**; unticked, the PDF stays
   in the folder.
3. Pick the output folder. Empty means `Documents\RetroTerm\Printer`.
4. Connect. The tab shows the printer's state instead of a screen: connected or not, whether a job
   is coming in, and every job printed with its file name.

**One file per print job.** A job ends when the host sends telnet Abort Output, when the connection
closes, or when no data has come for the number of seconds set on the Printer tab (5 unless you
change it; 0 turns this off). The quiet time is the same idea as x3270's `pr3287 -eojtimeout`.
The End of Message code (EM) is not a job end: it only ends one printout.

**What has not been measured yet.** On TK5 the printer is driven by JRP, a program that copies JES2
output class G to device `00C7` (the TK4- manual, "JRP on TK4-"). JRP's printer starts switched off,
and switching it on needs a logon to the JRP console (`logon applid=cjrp` at the TK5 logon screen)
with a user allowed to use JRP; the manual names HERC01 and HERC02. With HERC03, JRP answers
`JRP005T - NOT AUTHORIZED FOR JRP`. So what TK5 really sends to a printer, and whether it marks the
end of a job at all, has not been seen. Until it has, the quiet time is what ends a job from TK5.

## 12. Where to read more

- The Hercules guides in the TK5 folder, numbered 01 to 14, go on from here: data sets, the editor,
  batch jobs, compiling programs. RetroTerm's own test suite runs guides 01 to 09 against TK5.
- `docs\spec\IBM3270\` in this repository: every rule RetroTerm follows, with the IBM manual pages.
- *IBM 3270 Information Display System: Introduction*, GA27-2739, and the *3278 Display Station
  Operator's Guide*, GA27-2890: IBM's own beginner manuals, on bitsavers.
