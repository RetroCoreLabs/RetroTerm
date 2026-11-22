# IBM 3270 PC workstation keys

What the extra keys on the IBM 3270 Personal Computer (5271) did, and how the user moved between
its sessions. Written from IBM's own manuals in `IBM-manuals/`. Page numbers are the printed page
numbers in each manual.

## Sources

| Short name | File in `IBM-manuals/` | What it is |
|---|---|---|
| CP-UG | `SC23-0103-2_3270_PC_Control_Program_Users_Guide_and_Reference_Jul84.pdf` | 3270 PC Control Program User's Guide and Reference, SC23-0103-2, July 1984. The main source here. |
| WSP-UG | `84X0982_3270_Workstation_Program_V1.0_Users_Guide_and_Reference_Apr87.pdf` | 3270 Workstation Program 1.0 User's Guide and Reference, April 1987. The later program; gives the PC/AT keyboard equivalents. |
| CP-USE | `ZA23-0250-1_3270_PC_Using_the_Control_Program_Jul85.pdf` | Using the Control Program, July 1985. Downloaded, NOT read for this note. |
| CP-CARD | `SX23-0231-02_3270_PC_Control_Program_Summary_Card_Jun85.pdf` | Summary card. Image only (no text layer), NOT read. |
| WSP-SPEC | `90X7283_3270_Workstation_Program_V1.0_Specification.pdf` | Short product specification. NOT read in detail. |
| HINTS | `G320-9539-0_IBM_3270_Personal_Computer_Hints_and_Tips_198605.pdf` | Hints and Tips, May 1986. NOT read. |

## The two modes

The 3270 PC keyboard works in one of two modes (CP-UG 2-18 to 2-21, 6-3 to 6-4):

- **Application mode** - you type into the active window. The keyboard acts as a 3278/3279
  keyboard in a host window, a PC keyboard in the PC window, and a notepad keyboard in a notepad
  window. Only one session gets the keys at a time (CP-UG 5-3).
- **Work station control mode** - you cannot type into a window. Instead you set up the screen,
  add/delete/move/size windows, browse, copy, record keys, print, and use Help and List panels
  (CP-UG 6-4). The status line shows for example `WSCTRL WINDOW=A SCREEN=0` (WSP-UG 5-22).

## The keys

On the 3270 PC keyboard several of these share one key: one legend plain, one with Shift, one
with Alt. The groups (read from the key pictures, CP-UG 5-7, and the key panel, WSP-UG 7-27,
figure 7-6) are: WS Ctrl / ExSel; Finish / Attn; ChgSc / Jump / CrSel; Print / Ident;
Copy / Auto / Pause; Enlarge / Delete Window; Help / SysRq; Clear / Test.
Which legend is plain, which Shift and which Alt is NOT VERIFIED for every key; WSP-UG 5-22 says
"Shift + ChgSc", so ChgSc is a shifted legend.

| Key | What it did | Source |
|---|---|---|
| **WS Ctrl** (Workstation Control) | Puts you in work station control mode; press again to go back to application mode. Works in both modes. On the Enhanced PC keyboard: Shift + Esc. | CP-UG 5-7, 6-7; WSP-UG 5-22 |
| **Help** | Brings up a Help panel listing the functions that apply to what you are doing now; press again to remove it. Several different Help panels exist. | CP-UG 1-9, 6-7 |
| **Jump** | Moves to the next window, in alphabetic order of the window short names (A-Z). Works in both modes. Enhanced PC keyboard: left Alt + Page Up; XT/AT keyboard: Alt + Esc. | CP-UG 5-7, 6-8; WSP-UG 5-10 (Alt + Esc: page NOT VERIFIED) |
| **ChgSc** (Change Screen) | Moves to the next defined screen profile (a saved window layout), in number order. Works in both modes. Enhanced PC keyboard: left Alt + Home. With only one profile it says `INDWS040 Cannot use ChgSc: All other screens are empty`. | CP-UG 2-12, 5-7, 6-7; WSP-UG 5-22 |
| **Enlarge** (what other products call Zoom) | Makes the selected window, and windows selected after it, fill the whole screen until pressed again. Borders vanish and no other window shows. Works in both modes. Enhanced PC keyboard: left Alt + Insert. | CP-UG 2-14, 5-9, 6-9; WSP-UG (Alt + Insert text, page NOT VERIFIED) |
| **Delete Window** | In work station control mode, deletes the selected window from the screen profile (not from profile 0). | CP-UG 6-10; WSP-UG 5-22 |
| **Copy** | Starts the copy function (work station control mode). Copy within a window, between windows, and between screen profiles; from host, PC or notepad into a host or notepad window. You cannot copy INTO the PC window or within it. | CP-UG 2-33, 6-8. Step by step (chapter 9) NOT read. |
| **Print** | Work station control mode: prints the exact screen, or an enlarged window, to the 3270 PC's own printer. Application mode in a host window: sends the session to a printer on the 3274 control unit (a 3278 local copy). | CP-UG 5-8, 6-8 |
| **Finish** | Ends the current work station control level and goes back one level. | CP-UG 6-7 |
| **Quit** | Cancels the current function but stays at the same level (example: undo a window colour change). Shares a key with Reset. | CP-UG 6-10 |
| **Auto** | Starts the autokey function: record a series of keystrokes and play them back (logon, memo start). Recordings can be saved to disk. | CP-UG 2-34, 6-9 |
| **Pause** | During autokey: stops so the user can type variable text into a recording or playback; press again to carry on. | CP-UG 6-9 |
| **ErInp** in work station control mode | Deletes an autokey recording. (In a host window it is the normal 3278 Erase Input.) | CP-UG 5-8, 6-8 |
| **ExSel** | "Configures your host computer keyboard functions to emulate certain typewriter features." Host key, application mode. What exactly it changes is NOT VERIFIED. | CP-UG 5-7 |
| **CrSel** | Host key: cursor select - chooses a field the application marked as selectable with the cursor. | CP-UG 5-8 |
| **Ident** | Host key: the next two digits typed are the printer ID; shows the ID in the status line, also when the printer assignment changes. | CP-UG 5-9 |
| **Test** | Host key: readies the station for problem-finding functions held in the control unit, when the 3270 PC runs as a CUT (control unit terminal) display. | CP-UG 5-6, Appendix A (NOT read) |
| **Attn**, **SysRq**, **Clear** | Ordinary 3278 host keys: Attn and SysRq signal the host; Clear erases the session and signals the host. | CP-UG 5-6, 5-7 |

## Sessions and windows

- **How many.** Control Program: up to four host sessions, one PC (DOS) session, and up to two
  notepad sessions - seven at most, all on one screen at once (CP-UG 1-8, 2-4, 2-5). The later
  Workstation Program allows up to six PC sessions, four host sessions and two notepads
  (WSP-UG chapter 1; page NOT VERIFIED).
- **Windows.** Each session has one window per screen profile. A window can be the full screen
  or as small as one character. It looks into the session's "presentation space" (the whole
  screen image), which can be bigger than the window (CP-UG 2-4, 2-5).
- **Names.** Each window has a one-letter short name A-Z and a long name, shown as for example
  `R-Reminder` in the top border (CP-UG 2-5, 2-6).
- **Active window.** It has a double border and holds the cursor; the keyboard belongs to that
  session (CP-UG 2-18).
- **Notepads.** A notepad is a work area on the screen, not linked to any host. Its text can be
  saved to diskette or fixed disk (CP-UG 2-5; WSP-UG chapter 11 "Saving and Restoring Notepads",
  NOT read).
- **Screen profiles.** A saved layout of windows. Up to ten (0-9). Profile 0 always holds one
  window for every configured session and you cannot add or delete windows in it; profiles 1-9
  can hold one to seven windows each, with their own sizes and colours (CP-UG 2-10; WSP-UG 5-22).
- **Moving between them.**
  - **Jump** = next window, by letter order, inside the current profile.
  - **ChgSc** = next screen profile (whole layout), by number order. Profiles with no windows are
    skipped.
  - **Enlarge** = the current window takes the full screen; Jump then moves full-screen window to
    full-screen window (WSP-UG 5-10: "Press the Jump key ... to get to the next full-size window").
  - **WS Ctrl** = switch the keyboard between typing into a window and managing the windows.
- **Copy between sessions.** See Copy above: any window into a host or notepad window, never into
  the PC window (CP-UG 2-33).
- **Auto-windowing.** Typing past a small window's edge scrolls the window over the presentation
  space, so the user need not enlarge it to type (CP-UG 2-16).

## What RetroTerm could map each key to - PROPOSAL

Nothing below is decided. It assumes what RetroTerm has today: tabs, copy and paste, zoom, a
settings window for each connection, and a Help menu that opens the docs site.

| 3270 PC key | Possible RetroTerm action |
|---|---|
| WS Ctrl | No mode switch; RetroTerm's own menus and shortcuts already do the managing. Could open a small "workstation" menu (tabs, layout, copy, print). |
| Help | Help menu: open the 3270 page of the docs site. |
| Jump | Next tab (and Shift for previous). |
| ChgSc | Nothing today. If RetroTerm ever gets saved tab layouts or split views, switch to the next one. |
| Enlarge / Zoom | Toggle full screen for the current tab, or the existing zoom. |
| Delete Window | Close tab (with confirmation). |
| Copy | Copy selection (paste with the normal paste). Unlike the 3270 PC, paste into any session. |
| Print | Print or save the current screen. |
| Finish / Quit | Close the open dialog (Finish = OK, Quit = Cancel). |
| Auto / Pause | Only if RetroTerm gets key recording or its script runner is tied in: record / play, pause for typed input. |
| Ident | Settings window for the connection, printer section (if a 3270 printer is ever supported). |
| ExSel, CrSel, Test, Attn, SysRq, Clear | Host keys: send to the host as the 3270 key, not local actions. |
| Notepads | Could be a local "notes" tab with no connection. Not needed for a first version. |

## Open questions

- Exactly which legend on each shared key is plain, Shift and Alt (CP-UG 5-4 and 6-6 key pictures
  are not readable as text; CP-CARD is an image-only PDF and was not read).
- What ExSel's "typewriter features" are (CP-UG chapter 5 only gives one sentence).
- The full Copy steps (CP-UG chapter 9) and the browse function (chapter 8) were not read.
- The Enhanced PC keyboard equivalents were only read for WS Ctrl, Jump, ChgSc and Enlarge; the
  rest (Copy, Print, Auto, Help) are NOT VERIFIED. WSP-UG chapter 7 (keyboard definition) and
  CP-USE should hold them.
- Which page of WSP-UG gives Alt + Esc for Jump on XT/AT keyboards and Alt + Insert for Enlarge
  was not noted.
