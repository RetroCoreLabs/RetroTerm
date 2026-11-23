# IBM 3270 keyboard and local editing — specification for RetroTerm

Written 22 November 2026 for the coming IBM 3270 emulation and its on-screen keyboard. Every
statement names where it was read. Anything not read in a source is marked **NOT VERIFIED**.
Anything that is our own idea is marked **PROPOSAL**.

**The authority for the on-screen keyboard layout is the Key Tronic KB 3270 Plus "Native" layout as
drawn on <https://www.seasip.info/VintagePC/kb3270.html>** (Ronny's ruling, 22 November 2026). The
photos and the keypad screenshot below only help to read key positions. The IBM 3278 operator's
guide is the authority for what each key DOES.

---

## 1. Sources

### 1.1 IBM manuals (folder `IBM-manuals\`)

| File | Used for |
|---|---|
| `GA27-2890-4_3278_Display_Station_Operators_Guide_Feb84.pdf` | **Found and downloaded 22 November 2026** from bitsavers (`pdf/ibm/3278/`), starts with `%PDF`, 148 pages. Chapter 3 "Keyboards": drawings of the 3278 Typewriter, Data Entry and Data Entry-Keypunch keyboards (Figures 3-2 to 3-4, page 3-5) and a description of every key (pages 3-7 to 3-45). Chapter 2: the Operator Information Area messages (pages 2-3, 2-21, 2-22). |
| `GA23-0059-4_3270_Data_Stream_Programmers_Reference_Dec88.pdf` | AID values (Table 3-4, page 3-9), DUP and FM codes (page 4-10), numeric fields (page 4-12), field attribute bits and auto-skip (Table 4-1, page 4-13), input inhibit and System Lock (pages 3-20, 3-21). |
| `GA23-0061-2_3274_Control_Unit_Description_and_Programmers_Guide_Mar85.pdf` | Figure 6-2 "Results of Keyboard and Field Combinations" (page 6-4): when a numeric field really refuses letters. |
| `GA27-2739-22_3270_Information_Display_System_Introduction_Oct88.pdf` | Chapter 5 "Keyboards" (pages 5-1 to 5-4): the key families, and Figure 5-1, a photo of the 122-key "IBM 3270 Personal Computer Keyboard" (page 5-2). |

Other keyboard manuals seen in the bitsavers mirror listing
(`bitsavers.trailing-edge.com/pdf/ibm/3278/` and `.../3270/`) but **not downloaded**:
`S126-0029-0_3276_3278_Keyboard_Assembly_Parts_Catalog_Jan83.pdf`,
`GA27-2742-1_Operators_Guide_for_IBM_3270_Information_DIsplay_Systems_Jul72.pdf` (the older 3277),
`GA18-2044-0_IBM_3270_Description_and_Configuration_APL_Text_Feature_197902.pdf`. The 3191 folder
holds only problem-solving guides. No 3191 or 3179 operator's guide with a 122-key drawing was found.

### 1.2 The KB 3270 Plus (folder `Keyboard\`)

| File | What it is |
|---|---|
| `kb3270_keytops.svg` / `.png` | **THE layout.** The "Native" keycaps of the KB 3270 Plus. Source: <https://www.seasip.info/VintagePC/Images/kb3270_keytops.svg>, embedded in the kb3270 page. The `.png` is our rendering of the SVG. |
| `kb3270_irma.svg` / `.png` | The "IRMA" keycaps (same source page, `Images/kb3270_irma.svg`). |
| `kb3270_enh101.svg` / `.png` | The "Enhanced 101/102" keycaps (same source page, `Images/kb3270_enh101.svg`). |
| `kb3270_keynums.svg` / `.png` | The key numbers used by the spreadsheet (same source page, `Images/kb3270_keynums.svg`). |
| `kb3270_keys.ods` | The page author's spreadsheet: every key number, its scancodes per mode, and its keycap legend per layout (linked from the same page). |
| `1227T_keytops.svg` / `.png` | The **IBM 1397000** 122-key keyboard ("IBM Personal System/2 Host Connected Keyboard"), from <https://www.seasip.info/VintagePC/ibm_1397000.html> (image `Misc/Images/1227T_keytops.svg`). Second opinion on legends. |
| `kb3270plus-native-photo-1.png`, `kb3270plus-native-photo-2.png` | Photos of a real KB 3270 Plus with native keycaps, supplied by Ronny. Helpers only. |
| `kb3270plus.jpg` | The seasip page's own photo (Enhanced 101 keycaps fitted). |
| `3270-keypad-screenshot.png` | A screen shot of an on-screen 3270 keypad, supplied by Ronny. See 1.3. |

### 1.3 Where the keypad screenshot comes from

It is the **c3270 / wc3270 pop-up keypad** from x3270. Its text is the file
`Common/c3270/keypad.full` in the x3270 source (the only file with the word `Cmpose` in it), and
`Common/c3270/keypad.callbacks` says what each box does: PA(1), PA(2), Attn, PA(3), EraseEOF,
EraseInput, SysReq, Clear on the left; PF13-PF24 over PF1-PF12; Tab (`-->|`), BackTab (`|<--`),
Newline (`<-+`), Reset, Enter in the middle; Home, CursorSelect, Compose, Insert, Delete, Dup,
FieldMark on the right. In c3270/wc3270 it is opened with **Alt+K** (`Keypad()` in
`Common/fb-c3270`, keymap.base). "Cmpose" is x3270's own compose-character key, not a 3270 key.

### 1.4 Oracles (read only, never copied)

- x3270: `Common/kybd.c` (every key action), `Common/fb-c3270` (the default c3270/wc3270 keymap),
  `wc3270/screen.c` (keys that work with no keymap entry), `wc3270/select.c` (mouse),
  `x3270/fb-x3270` and `x3270/select.c` (the X11 version's mouse and Cut), `Common/glue.c` and
  `Common/toggles.c` (default settings).
- IBM tnz: `tnz/zti.py` (keys and mouse), `tnz/tnz.py` (paste), `README.md` line 103.

---

## 2. The original IBM 3278 keyboard (GA27-2890-4)

### 2.1 What exists

"Having a keyboard as part of a 3278 Display Station is optional" (page 3-3, PDF page 48). The
guide shows these types (page 3-4/3-5): Typewriter (Figure 3-2), Data Entry (3-3), Data
Entry-Keypunch Layout (3-4), Attribute Select Typewriter (3-5), Attribute Select Typewriter/APL
(3-6), Attribute Select Overlay (3-7). The Typewriter keyboard has 75 keys, or 87 with the
optional PF13-PF24 block (Figure 3-2 note "87-Key Keyboard Only"; page 3-14 "the 75-key Typewriter
keyboard"; GA27-2739-22 page 5-1 "75 keys for the IBM 3278").

### 2.2 The ALT key is the 3278's own idea of "keys a keyboard is missing"

The 3278 has fewer keys than functions. Each "alternate function key" carries a second legend **on
its front** and works only while **ALT is held** (page 3-7, page 3-9). On the Typewriter keyboard
the ALT functions are: SYS REQ, CLEAR, ERASE INPUT, ALT CURSR, IDENT, TEST, DEV CNCL, PA1, PA2,
PF1-PF12 (ALT + the digit keys 1, 2 ... 0, -, =), double-speed left/right, and Home (page 3-9).
PF13-PF24, when fitted, need no ALT (page 3-15).

**This is the model for RetroTerm: an ALT combination reaching a key that is not on the PC
keyboard is exactly how IBM's own 75-key keyboard worked.**

### 2.3 Typewriter keyboard, row by row (Figure 3-2, page 3-5, read from the rendered drawing)

Legends written "top / front": the front legend is the ALT function.

| Block | Row | Keys, left to right |
|---|---|---|
| Left, 2 wide | 1 | ATTN / SYS REQ, CURSR SEL / CLEAR |
| | 2 | (blank key), (blank) / ERASE INPUT |
| | 3 | CURSR BLINK / ALT CURSR, ERASE EOF |
| | 4 | Print (printer picture) / IDENT, Click (picture) / TEST |
| Main | 1 | `~ \``, `\| 1`/PF1, `@ 2`/PF2, `# 3`/PF3, `$ 4`/PF4, `% 5`/PF5, `¬ 6`/PF6, `& 7`/PF7, `* 8`/PF8, `( 9`/PF9, `) 0`/PF10, `_ -`/PF11, `+ =`/PF12, Backspace (←) |
| | 2 | Tab / Back Tab (→\| over \|←), Q W E R T Y U I O P, `! ¢`, `\| \` (broken bar over back slash), New Line (\|← with the Home square front legend in the drawing) |
| | 3 | Lock, A S D F G H J K L, `: ;`, `" '`, `} {`, big New Line/Return-shaped key |
| | 4 | Shift, `> <`, Z X C V B N M, `, ,`, `. .`, `? /`, Shift |
| | 5 | RESET / DEV CNCL, space bar, ALT, ENTER |
| Middle, 2 wide | 1 | DUP / PA1, FIELD MARK / PA2 |
| | 2 | Insert (`^` with a caret), Delete (`a` struck through) |
| | 3 | up, down |
| | 4 | left / double-speed left, right / double-speed right |
| Right (87-key only), 3 wide | 1-4 | PF13 PF14 PF15 / PF16 PF17 PF18 / PF19 PF20 PF21 / PF22 PF23 PF24 |

**NOT VERIFIED:** some small glyphs in the scan (the exact symbol on the row-2 right-hand key and
which key carries the Home front legend) are hard to read at the scan's resolution. Page 3-30
confirms Home is an ALT function ("Pressing the Home key while holding the ALT key down").

The Data Entry keyboards (Figures 3-3, 3-4) put digits on the right-hand letter keys (keypunch
style) and have SKIP, Numeric Shift, Numeric Lock and Alpha shift keys (pages 3-17 to 3-24). They
are not proposed for RetroTerm; listed here only so the choice is a known one.

---

## 3. The KB 3270 Plus "Native" layout (THE layout for the on-screen keyboard)

Read from `kb3270_keytops.png` (our rendering of the seasip SVG), with key names from
`kb3270_keys.ods` (column "Keycap (native)"). Where the photos differ, both are written down.
Legends are "main / second". On the seasip drawing the second legend of the left-block keys is
printed in **blue**; the page does not say what blue means. **NOT VERIFIED:** that a blue legend is
reached with ALT. (On the 3278 a front legend is reached with ALT, section 2.2; on the IBM 1397000
drawing, blue legends on the number pad are the Num-Lock-off functions.)

### 3.1 Function key block (two rows of 12, above the main block)

| Row | Keys |
|---|---|
| Top | PF13 PF14 PF15 PF16 PF17 PF18 PF19 PF20 PF21 PF22 PF23 PF24 |
| Bottom | PF1 PF2 PF3 PF4 PF5 PF6 PF7 PF8 PF9 PF10 PF11 PF12 |

Second legends on the seasip drawing: PF13 Red, PF14 Pink, PF15 Green, PF16 Yellow, PF17 Blue,
PF18 Turq, PF19 White, PF20 Black, PF21 (a ▶▶ symbol), PF23 AltCr; PF1-PF6 PSA PSB PSC PSD PSE
PSF; PF7 and PF12 ▶▶; PF9 a reversed `a`; PF10 a blinking `a`; PF11 an underlined `a`. These are the
3278/3279 "attribute select" functions (colour, programmed symbol set, reverse, blink, underscore —
GA27-2890-4 pages 3-46 onward). **Photos:** the PF keycaps show only PF1-PF24, no second legend.

### 3.2 Left block (2 wide, 5 rows)

| Row | Left key | Right key |
|---|---|---|
| 1 | Help / SysRq | Clear / Test |
| 2 | WS Ctrl / ExSel | Finish / Attn |
| 3 | ChgSc / Jump / CrSel | Erase EOF / ErInp |
| 4 | Print / `*` / Ident | Copy / Auto / Pause |
| 5 | two-squares key (□→□ with a pen-on-screen picture; the .ods calls it "Zoom") | Ctrl |

**Photos disagree:** the photos show only "Help", "Clear", "WS Ctrl", "Finish", "ChgSc / Jump",
"Erase EOF", "Print / `*`", "Copy / Auto", "□→□" and "Ctrl". The blue second legends SysRq, Test,
ExSel, Attn, CrSel, ErInp, Ident, Pause are **not visible** in either photo, and the pen picture on
the bottom-left key is not there. Whether they are worn off, printed on the key front (not visible
from above), or absent: **NOT VERIFIED**.

The IBM 1397000 (`1227T_keytops.png`) has, in the same block: Attn / SysRQ, Clear, CrSel, Pause /
ErInp, ExSel / SetUp, ErEOF / Record, Print / PrtSc / Ident, Copy / Play / Test, and two blank keys.
So the IBM keyboard puts **Attn** and **CrSel** on their own keys where the KB 3270 Plus has them as
second legends.

### 3.3 Main block

| Row | Keys, left to right |
|---|---|
| 1 | `~ \``, `! 1` (with a broken bar), `@ 2`, `# 3`, `$ 4`, `% 5`, `¬ ^ 6`, `& 7`, `* 8`, `( 9`, `) 0`, `_ -`, `+ =`, Backspace (←, wide) |
| 2 | Tab (⇥, with ⇤ Back Tab legend), Q W E R T Y U I O P, `! ¢ { [`, `\| \` (broken bar), big ↵ key (two rows high; the .ods calls it "Return") |
| 3 | Caps Lock (padlock, LED), A S D F G H J K L, `: ;`, `" '`, `} ] { [` (the .ods calls it "]") |
| 4 | Shift, `> <`, Z X C V B N M, `, <`, `. >`, `? /`, Shift (wide) |
| 5 | **Reset / Quit**, Alt, space bar, Alt, **Enter** |

The big ↵ key in rows 2-3 is the 3278's **New Line** position (compare Figure 3-2). The **Enter**
key, the 3270 ENTER attention key, is at the bottom right of the main block, as on the 3278. **NOT
VERIFIED:** that the KB 3270 Plus legend for the ↵ key is meant as New Line; the .ods only names its
PC meaning, "Return".

### 3.4 Middle block (above and around the arrows)

| Row | Keys |
|---|---|
| 1 | Dup / PA1, Field Mark / PA2, PA3 |
| 2 | Back Tab (\|←), Insert (`a` under a caret roof), Delete (`a` struck through) |
| 3 | — , up, — |
| 4 | left (with ⇇ double-speed legend), centre key with ↖ (the .ods calls it "Rule / Home"), right (with ⇉ double-speed legend) |
| 5 | — , down, — |

**Photos disagree:** the centre key of the arrow cross is **blank** in photo 1, and the left and right
arrows show a single arrow only. The .ods names come from key numbers 4b, 4c, 50, 51, 53, 55, 56.

### 3.5 Number pad

| Row | Keys |
|---|---|
| 1 | Esc, Num Lock (LED), ScrLk / Break (LED, with `,` `'` legends), Sys Req |
| 2 | 7 Home, 8 ↑, 9 PgUp, Tab (⇥) |
| 3 | 4 ←, 5, 6 →, `-` |
| 4 | 1 End, 2 ↓, 3 PgDn, Enter `+` (two rows high) |
| 5 | 0 Ins (two wide), `.` Del |

### 3.6 The other two layouts (same seasip page)

- **IRMA**: the ten left keys become F1-F10, PA3 becomes PrtSc, and the bottom row becomes Alt /
  Ctrl / Space / Reset [Dev Cncl] / Enter.
- **Enhanced 101/102**: bottom row Ctrl / Alt / Space / Alt / Ctrl, and the six keys above the
  arrows become Insert / Home / PgUp and Delete / End / PgDn.

### 3.7 IBM's own 122-key keyboard has this shape too

GA27-2739-22 Figure 5-1 (page 5-2) is a photo of the "IBM 3270 Personal Computer Keyboard": 24 PF
keys in two rows, a 2-wide block on the left (Help, Clear, Erase EOF, Copy/Auto, Print and Reset are
readable), the Dup/PA1 and Field Mark/PA2 block above the arrows, and an Enter key beside the space
bar. The legends are too small to read them all. Chapter 5 says keyboards range "from 75 keys for
the IBM 3278 Display Station to 122 keys on a 3270 Personal Computer keyboard" (page 5-1).

---

## 4. What every key does

**Two kinds of key** (GA27-2890-4 chapter 3; GA23-0059-4 Table 3-4):

- **Local keys** change the screen buffer or the cursor inside the terminal. Nothing is sent.
- **Attention keys** (Enter, PF1-PF24, PA1-PA3, Clear, Cursor Select on an attention field, Sys
  Req, Attn) send an AID byte to the host and lock the keyboard ("Do Not Enter - Time" in the
  Operator Information Area) until the host unlocks it (pages 3-14, 3-43; section 5).

### 4.1 Attention keys and their AID bytes (GA23-0059-4 Table 3-4, page 3-9)

| Key | AID (hex, EBCDIC) | What is sent |
|---|---|---|
| ENTER | 7D | AID, cursor address, modified fields |
| PF1-PF9 | F1-F9 | same as Enter |
| PF10, PF11, PF12 | 7A, 7B, 7C | same |
| PF13-PF21 | C1-C9 | same |
| PF22, PF23, PF24 | 4A, 4B, 4C | same |
| PA1 | 6C | AID only, "no input data from the screen is transmitted" (GA27-2739-22 page 5-4) |
| PA2 (CNCL) | 6E | AID only |
| PA3 | 6B | AID only |
| CLEAR | 6D | AID only; the screen is cleared locally first (GA27-2890-4 page 3-34; x3270 `Clear_action`) |
| Selector-pen attention (Cursor Select) | 7E | see 4.8 |
| Test Req and Sys Req | F0 | see 4.9 |
| Structured field | 88 | not a key |

### 4.2 Typing a character (GA27-2890-4 page 3-25; GA23-0059-4 page 4-23 region, PDF page 30)

A character goes into the buffer at the cursor, overwriting what is there (except in insert mode),
and the cursor moves one position right. The cursor must be in an unprotected field. The Spacebar
types a real space character — "the Spacebar should not be used to position the cursor"
(page 3-25).

**Auto-skip.** A field whose attribute has protected AND numeric set (bits 2 and 3 = B'11') makes
the cursor skip it (GA23-0059-4 Table 4-1, page 4-13). x3270 does this after every typed character
that lands on a field attribute, except after DUP (`key_Character`, the "Implement auto-skip"
block).

**Errors that lock the keyboard** (GA27-2890-4 pages 2-21/2-22). Each shows a "Do Not Enter" (X)
message; **RESET** clears it:

| Message | When |
|---|---|
| Go Elsewhere (X ←┼→) | typing, inserting, erasing or deleting in a protected field; Cursor Select outside a selectable field |
| Too Much Data (X ┼>) | more characters than the field holds (insert overflow) |
| Numeric Data Only (X ┼NUM) | a non-numeric character in a numeric field, see 4.4 |

x3270 differs: an operator error only locks the keyboard when the `oerrLock` setting is on, and it
is **off by default** in c3270/wc3270/x3270 (`appres.oerr_lock`, set true only in `s3270.c`);
otherwise x3270 just ignores the key (`operator_error` in `kybd.c`).

### 4.3 Insert mode, and the overflow rule (GA27-2890-4 pages 3-41 to 3-43; Figure 3-25)

- The Insert key turns insert mode on; the OIA shows the insert symbol (^).
- Typed characters push the rest of the field right. "If a field is a large one and covers more
  than one line ... characters will shift from the end of one line to the beginning of the next."
- **Overflow:** "If you try to insert more characters than the field can hold, the Do Not Enter -
  Too Much Data message appears and your keyboard is disabled." The guide adds: "the number of
  characters you can insert into a field is limited to one character less than the number of
  blanks remaining in that field" (a 25-position field holding "JOHN DOE", 17 blanks, takes 16
  more). **NOT VERIFIED:** why one less; the guide gives no reason.
- Insert mode ends with **RESET**, or with any key that talks to the host (Enter, PA, PF ...).
- x3270 (`ins_prep`): room is counted in **null** characters (X'00') between the cursor and the end
  of the field; with the `blankFill` setting (on by default, `glue.c` line 484) trailing blanks
  count too. No room = Too Much Data (KL_OERR_OVERFLOW). Reset turns insert mode off
  (`do_reset`). x3270's counting does not have the "one less" rule. **The two disagree; the IBM
  guide decides** (Open question 4).

### 4.4 Numeric fields

- GA23-0059-4 page 4-12: "Numeric fields are limited to numeric characters, the minus and
  decimal-sign characters, and the duplicate (DUP) control." Attribute bit 3 = 1 "causes an
  automatic upshift of data entry keyboard" (Table 4-1, page 4-13).
- GA27-2890-4 quick reference (page 2-3): NUM — "Only 0 through 9, period (.), minus (-), and DUP
  will be accepted."
- **But the check is a FEATURE.** GA23-0061-2 Figure 6-2 (page 6-4): on a Typewriter keyboard
  WITHOUT Keyboard Numeric Lock, a numeric field takes any character. WITH Numeric Lock: "Can only
  enter 0-9, period, and minus sign; any other characters lock keyboard." Shift overrides it on
  3278 and 3279. On a Data Entry keyboard, numeric fields shift the keyboard to its digit legends.
- x3270: the check only happens when the `numericLock` setting is true (`appres.numeric_lock`,
  `kybd.c` line 1231; no default set in `glue.c`, so **off**). When on it accepts 0-9, `+`, `-`,
  `.` and `,` — note `+` and `,` are more than IBM lists.

### 4.5 Cursor movement (GA27-2890-4 pages 3-26 to 3-31)

| Key | Exactly |
|---|---|
| New Line | Unformatted screen: first position of the next line. Formatted: the first input position on the next line that has one, searching on down; if everything is protected, first position of line 1. (pages 3-26, 3-27, Figure 3-20) |
| Tab (and SKIP) | First character of the next input field. No fields or unformatted: first position of line 1. (page 3-28) |
| Back Tab | Start of the current input field; if already there, start of the previous input field. Same no-field rule. (page 3-29) |
| Home (ALT on the 3278) | First input position on the screen; no input fields: first position of line 1. (page 3-30) |
| ← → | One position; wraps to the line above/below at the edges. (page 3-30, Figure 2-7) |
| ALT + ← → | Double speed, two positions. (page 3-30) |
| ↑ ↓ | One line; wraps top to bottom in the same column. (page 3-31) |
| Backspace | **Moves the cursor left exactly like ←. It does not erase.** (page 3-31) |

x3270 `Newline_action`: next line, first column; if that position is in an unprotected field it
stops there, otherwise moves to the next unprotected position. wc3270 maps the PC Backspace to
`Erase()` (erases the character to the left, `fb-c3270` keymap.base.3270), unlike the IBM key.

### 4.6 Erasing (GA27-2890-4 pages 3-34 CLEAR, 3-35 ERASE INPUT and ERASE EOF, 3-43 Delete)

| Key | Exactly |
|---|---|
| ERASE EOF | Blanks from the cursor to the end of the field; the cursor does not move. Unformatted: to the end of the screen. |
| ERASE INPUT (ALT) | Erases all input fields, cursor to the first input field. No input fields: nothing erased, cursor to line 1. Unformatted: whole screen erased. |
| Delete | Removes the character at the cursor; the rest of the field **on the same line** moves left one place. |
| CLEAR (ALT) | Attention key: blanks the whole screen, makes it unformatted, cursor to line 1, and sends AID 6D. (page 3-34) |

x3270 erases with nulls (X'00'), not spaces: `EraseEOF_action` writes `EBC_null` and sets the
modified bit; `EraseInput_action` writes nulls and clears the modified bits. Nulls are not sent on
Read Modified (GA23-0059-4 page 4-10 and the read-modified text, PDF page 53).

### 4.7 DUP and FIELD MARK

| Key | Code | Shown as | Exactly |
|---|---|---|---|
| DUP | X'1C' | overscored asterisk | Enters the DUP code, then **the cursor jumps to the first position of the next input field**. The host program decides what DUP means. (GA27-2890-4 pages 3-13, 3-22; GA23-0059-4 page 4-10) |
| FIELD MARK | X'1E' | overscored semicolon | Enters the FM code; marks the end of a field on an unformatted screen. The cursor moves on one place like any character. (same pages) |

x3270: `Dup_action` types X'1C' then moves to the next unprotected field; `FieldMark_action` types
X'1E'. Both are typed characters, so they obey protection, numeric and insert rules.

### 4.8 Cursor Select (GA27-2890-4 "CURSR SEL Key", page 3-33)

Works only in a selector-pen-detectable field (attribute bits 4-5 = 01 or 10, GA23-0059-4 Table 4-1).
The first character of the field, the designator, decides:

- `?` → changes to `>`, the field is selected (modified bit set). Nothing is sent.
- `>` → changes back to `?`, deselected.
- `&` or blank → an **attention** field: the keyboard locks and the host is told. x3270
  (`lightpen_select`): blank or null sends AID **7E**; `&` sends AID **7D** (Enter).

Outside a selectable field: "Go Elsewhere" (page 2-22); x3270 rings the bell.

### 4.9 Attn, Sys Req, Reset, Print and the others

| Key | Exactly |
|---|---|
| ATTN | Gets the host program's attention; works only when the OIA shows the "my job" (B) symbol; otherwise "Minus Function". (page 3-32) Over TN3270: x3270 sends Telnet IP in TN3270E mode, and in plain TN3270 sends a PA1 read then Telnet BREAK (`Attn_action`). |
| SYS REQ (ALT) | Either a test request to the host, or clear the screen and switch between application and control program. (page 3-32) x3270: in TN3270E mode `net_abort()` (a Telnet ABORT); otherwise AID F0 (`SysReq_action`). |
| RESET | Unlocks the keyboard after an operator error, ends insert mode, cancels a dead key. It does **not** work while data goes to or comes from the host, nor on printer-busy messages. (page 3-40) |
| DEV CNCL (ALT) | Cancels a print request. (page 3-40) |
| Print | Prints the screen on the assigned printer. (page 3-37) |
| IDENT, TEST, ALT CURSR, CURSR BLINK, Click | Printer assignment, test mode, cursor shape, cursor blink, key click. (pages 3-36 to 3-39) Local terminal features; no host meaning. |
| PA1, PA2, PA3 | Attention, AID only. On the 3278 Typewriter PA1/PA2 need ALT. (page 3-14) |
| PF1-PF12 | Attention. On the 3278 Typewriter they need ALT (ALT + the top-row digit keys). (page 3-14) |

The KB 3270 Plus keys **Help, WS Ctrl, Finish, ChgSc/Jump, Copy/Auto, ExSel, CrSel, Pause, Zoom**
belong to the 3270 PC multi-session programs (GA27-2739-22 page 5-3: Work Station Control Mode,
Change Screen, Jump, Copy, Zoom). They have no meaning on a single 3278 session. **NOT VERIFIED**
what "Finish", "ExSel" and "Pause" do; no source read describes them.

---

## 5. Keyboard lock

- After any attention key the keyboard is locked with "Do Not Enter - Time" until the host
  answers (GA27-2890-4 pages 3-14, 3-43).
- The host unlocks it with a Write/Erase Write whose WCC has **Keyboard Restore** (GA23-0059-4
  pages 3-20, 3-21).
- GA23-0059-4 page 3-20: TWAIT and PWAIT (waiting for the host) "cannot be cleared by the
  operator (for example, by the RESET Key)". Page 3-21: **System Lock** "can be cleared by the
  RESET key as well as by the host".
- So: operator errors and System Lock → RESET clears them; waiting for the host → RESET does not.
- x3270 has a **typeahead** setting, on by default (`glue.c` line 483): keys pressed while locked are
  queued and run after the unlock (`enq_ta`). A real 3278 has no such queue (the guide says the
  keyboard is "disabled"). RESET in x3270 first throws away the queue (`do_reset`).

---

## 6. Mouse, selection, cut and paste

### 6.1 What the oracles do

| | Single click | Double click | Other |
|---|---|---|---|
| wc3270 (`wc3270/select.c`) | button up without moving = move the cursor there | selects a word; the cursor goes back where it was | right button: paste if nothing selected, copy if something is |
| x3270 X11 (`x3270/fb-x3270` lines 195-208, 289-290) | select-start; Shift+Btn1 = MoveCursor | (X11 word select, not read) | **Ctrl+Shift+Btn1 = MouseSelect** (Cursor Select at the mouse; `xkybd.c`); middle button pastes |
| tnz (`zti.py` around line 2570) | on a selectable field: selector pen (Cursor Select); on a protected `http(s)://` word: opens it; otherwise moves the cursor | **moves the cursor there and presses ENTER** | — |

RetroTerm today (`TerminalCanvas.axaml.cs`, `OnPointerPressed`): one click = character select,
double = word, triple = line, Alt+drag = rectangle select.

### 6.2 Cut

x3270 `Cut_xaction` (`x3270/select.c`): every selected character inside an **unprotected** field is
replaced with a space and its field gets the modified bit. Protected characters are left alone.

### 6.3 Paste, including several lines across fields

x3270 (`emulate_uinput` in `kybd.c`, comment above it, and `remargin`):

- Text is typed as if keyed. Carriage returns are ignored; **a newline means "go to the start of
  the next line"** (the Newline key) unless the text just wrapped onto that line; tabs and form
  feeds become spaces.
- **Overlay paste** (on by default, `glue.c` line 485): a protected position does not lock the
  keyboard; the pasted character is dropped and the cursor moves right. A newline then moves to
  column 0 of the next row; a newline that is the last character is ignored.
- **Margined paste** (off by default, `toggles.c`): each new line starts at the column where the
  paste started, skipping protected fields; Alt+V in wc3270 pastes without the margin
  (`Paste(-nomargin)`).
- Paste stops if the cursor wraps back to the top of the screen, or if the keyboard locks.

tnz (`tnz.py` `paste_data`): each line of the text is typed starting at the **same column** on the
next row (one row per line, `onerow=True`), stopping at the bottom; afterwards the cursor goes back
to where the paste began.

---

## 7. What x3270 and tnz map on a PC

| 3270 key | wc3270 / c3270 (`fb-c3270`, `wc3270/screen.c`) | tnz (`zti.py` help_keys and key loop) |
|---|---|---|
| Enter | Enter (option: Right Ctrl = Enter, Enter = Newline, `keymap.rctrl.3270`) | Enter |
| Newline | Ctrl+J | Shift+Enter |
| PF1-PF12 | F1-F12 | F1-F12 |
| PF13-PF24 | Shift+F1-F12 | Shift+F1-F12 |
| PA1, PA2, PA3 | Alt+1, Alt+2, Alt+3 | Alt+1 or Alt+Insert, Alt+2 or Alt+Home, Alt+3 |
| Clear | Alt+C | Ctrl+L or Alt+C |
| Attn | Alt+A, Ctrl+A | Alt+A or Ctrl+C |
| SysReq | no default key | none |
| Reset | Alt+R, Ctrl+R | none found |
| Dup | Alt+D, Ctrl+D | none found |
| Field Mark | Alt+F, Ctrl+F | none found |
| Erase EOF | Alt+E, Shift+End | Ctrl+K or Shift+End |
| Erase Input | no default key | none found |
| Insert | Insert (toggle); Alt+I (on) | Insert (toggle) |
| Delete | Delete | Delete |
| Tab / Back Tab | Tab / Shift+Tab | Tab / Shift+Tab |
| Home | Home | Home |
| End (go to end of field) | End (`FieldEnd`) | End |
| Backspace | Erase left (`Erase()`) | Backspace |
| Cursor Select | no default key (pop-up keypad only) | mouse click on a selectable field |
| Word left/right | Ctrl+Left/Right | Alt+Left/Right |
| PgUp / PgDn | scroll back through old screens | **PF7 / PF8** |
| Copy / Cut / Paste | Ctrl+C / Ctrl+X / Ctrl+V | bracketed paste from the console |
| Pop-up keypad | Alt+K | — |

### 7.1 Why tnz has "Alt+letter for ISPF menus"

tnz README line 103: "Use Alt+letter to access ISPF menus". What the code does (`zti.py` around
lines 2084-2106 and 2268-2278): on Alt plus a letter it searches **only the first row** of the
screen for that letter (either case) whose extended highlighting matches its test
`(exn & 0x0C0) == 0x0C0` (commented "if underline"), puts the cursor on it and **presses ENTER**.
On an ISPF screen the first row is the action bar (Menu, Utilities, ...), so Alt+U picks Utilities.

- It is **not** console-only. The only console part is recognising the key text (`\x1b`+letter or
  `ALT_x`); the action itself works on the 3270 screen buffer and would work in any front end.
- ISPF does **not** need it: the same thing happens when the user moves the cursor to the word and
  presses Enter, which is exactly what tnz does for them. It is a shortcut.
- **NOT VERIFIED:** that ISPF draws the action-bar letters underlined (no ISPF manual was read), and
  what `plane_eh` holds in tnz; if it holds the raw highlight value (F1 blink, F2 reverse, F4
  underscore), the test matches all three, not just underline.

---

## 8. PROPOSAL — PC key to 3270 key for RetroTerm

**Everything in this section is a PROPOSAL, not a decision.** Basis: what both oracles agree on, the
3278's own ALT keys, and the KB 3270 Plus layout for the on-screen keyboard. Every key of section 3
is clickable on the on-screen keyboard regardless of this table.

| PC key | 3270 key | Basis |
|---|---|---|
| Enter, number-pad Enter | ENTER | both oracles |
| Shift+Enter | New Line | tnz |
| Right Ctrl | ENTER (only if New Line is put on Enter) | wc3270 option `rctrl` |
| F1-F12 | PF1-PF12 | both oracles |
| Shift+F1-F12 | PF13-PF24 | both oracles |
| Alt+1, Alt+2, Alt+3 | PA1, PA2, PA3 | both oracles; ALT+DUP/FM position is PA1/PA2 on the 3278 |
| Alt+C | CLEAR | both oracles |
| Pause/Break | CLEAR | PROPOSAL only, no oracle |
| Alt+A | ATTN | both oracles |
| Esc | RESET | PROPOSAL; the KB 3270 Plus Reset sits where a PC has nothing; x3270 uses Esc for its own prompt, tnz for its command line, RetroTerm has neither |
| Alt+R | RESET | x3270 |
| Alt+D | DUP | x3270 |
| Alt+F | FIELD MARK | x3270 |
| Shift+End, Alt+E | ERASE EOF | both / x3270 |
| Ctrl+Shift+End | ERASE INPUT | PROPOSAL; neither oracle has a key |
| Insert | Insert mode on/off | both oracles |
| Delete | Delete | both |
| Backspace | Erase left, as wc3270 (`Display3270.Erase`, x3270's Erase action) | Decided by Ronny, 23 November 2026 |
| Tab, Shift+Tab | Tab, Back Tab | both |
| Home | Home | both |
| End | go to end of field | both |
| Arrows | arrows | both |
| Alt+Left, Alt+Right | double-speed left/right | 3278 page 3-30 (ALT + arrow); tnz uses them for word left/right |
| Ctrl+Left, Ctrl+Right | previous/next word | x3270 |
| Alt+S | SYS REQ | PROPOSAL; PC SysRq is Alt+PrintScreen, which Windows takes |
| Alt+Q (or Ctrl+Enter) | CURSOR SELECT | PROPOSAL; neither oracle has a key |
| Ctrl+Shift+click | CURSOR SELECT at the mouse | x3270 MouseSelect |
| Click | move the cursor | all oracles |
| Double click | word select (RetroTerm today) **or** cursor + ENTER (tnz) | Open question 5 |
| Ctrl+C, Ctrl+X, Ctrl+V | copy, cut (unprotected only, 6.2), paste (6.3) | wc3270 |
| PgUp, PgDn | scroll old screens **or** PF7/PF8 | Open question 6 |
| Alt+K | show/hide the on-screen keyboard | wc3270's keypad key |

### 8.1 Every clash found

1. **Alt+letter.** tnz's ISPF shortcut takes every Alt+letter; the table above uses Alt+A, C, D, E,
   F, K, Q, R, S for 3270 keys, and x3270 uses Alt+I, L, M, N, P, Q, V for its own functions. Both
   cannot have the same letters.
2. **Ctrl+C** is Copy in wc3270 and Attn in tnz.
3. **PgUp/PgDn**: scroll history (x3270) or PF7/PF8 (tnz). ISPF uses PF7/PF8 for up/down, so tnz's
   choice is handy there.
4. **Alt+Left/Right**: double speed (3278), word move (tnz).
5. **Double click**: word select (RetroTerm, wc3270) or cursor + Enter (tnz).
6. **Alt+drag**: RetroTerm's rectangle select (`TerminalCanvas.axaml.cs` line 341). Not a key clash,
   but Alt must not be read as "Alt pressed alone" by the 3270 keyboard.
7. **Windows takes** Alt+F4 (close), Alt+Space (window menu), Alt+Tab, Alt+PrintScreen; so Alt+F4
   cannot be PF16/PF4-style and SysRq cannot be the PC SysRq key. **NOT VERIFIED:** whether
   Avalonia passes F10 and Shift+F10 (Windows menu and context-menu keys) to the terminal; PF10 and
   PF22 need them.
8. **The TDV on-screen keyboard's Alt map** (`TDV2200KeyRegistry.InitializeAltMappings`) already
   uses Alt+H, D, U, P, S, Backspace, M, F, X, A, L, R, E, W, K, V, J, I, 1-8, F1-F8, Delete, PgUp,
   PgDn. A 3270 Alt map needs its own table per emulation. **NOT VERIFIED:** whether the user's saved
   key bindings are stored per emulation or shared.
9. **Backspace**: DECIDED 23 November 2026, erase left as wc3270 does. IBM's own key only moved the cursor; wc3270's built-in key map (x3270 source, Common\fb-c3270) gives Backspace the Erase action, and that is what RetroTerm does.
10. **Esc**: Reset (proposal) versus anything RetroTerm itself does with Esc today (not checked).

### 8.2 How the on-screen keyboard follows the TDV one

The TDV keyboard (`docs\TDV-KEYBOARD-COMPLETE-REFERENCE.md`, `TDV2200KeyRegistry.cs`,
`VirtualKeyboardPanel.axaml.cs`) gives each key a grid position, its legends, and a default Alt
binding table (`DefaultAltMappings`, `DefaultAltShiftMappings`) that the user can rebind from the
panel. The 3270 keyboard should have the same parts: one registry with the section 3 layout and
the section 8 bindings, used by both the panel and the real-key path. (PROPOSAL.)

---

## 9. Open questions

1. **Second legends on the KB 3270 Plus** (SysRq, Test, ExSel, Attn, CrSel, ErInp, Ident, Pause):
   are they reached with ALT? The seasip page does not say, and the photos do not show them.
2. **Which keys to draw**: the KB 3270 Plus native layout has 3270 PC session keys (Help, WS Ctrl,
   Finish, ChgSc/Jump, Copy/Auto, Zoom) that mean nothing on one 3278 session. Draw them disabled,
   leave them off, or give them RetroTerm functions?
3. **Backspace**: move left (IBM) or erase left (wc3270)?
4. **Insert overflow**: count room the IBM way ("one less than the blanks remaining") or the x3270
   way (nulls, plus trailing blanks)? The IBM wording may be about the 3278 keeping one null; not
   understood.
5. **Double click**: keep word select, or cursor + Enter like tnz?
6. **PgUp/PgDn**: PF7/PF8 or scroll back?
7. **Alt+letter**: the 3270 keys (x3270 style, matches the 3278 ALT idea) or tnz's ISPF action-bar
   shortcut? They cannot both have the letters.
8. **Numeric fields**: enforce them (the 3274 Numeric Lock feature) or not (a plain typewriter
   keyboard, x3270's default)? If enforced, which set: IBM's 0-9 . - DUP, or x3270's 0-9 + - . , ?
9. **Operator errors**: lock until Reset (IBM) or just ignore the key (x3270 default)?
10. **Typeahead** while the keyboard is locked: queue keys (x3270 default) or refuse them (IBM)?
11. **The ↵ key in the main block**: New Line (its 3278 position) or Enter (what PC users expect)?
12. **Colour / programmed-symbol second legends on PF1-PF24** (section 3.1): show them or not? They
    only make sense with the 3279 attribute-select feature.
