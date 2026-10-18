# DEC VT Family and ANSI Terminal — Verified Validation Matrix

This document provides a verified validation matrix for **ANSI X3.64-compatible terminals** and the **DEC VT family** (VT100, VT220, VT320, VT340).  
All information is based on **ECMA-48 (5th Edition, 1991)** and official DEC manuals.

---

## Combined Validation Matrix

| Definition | Notation | Representation | Origin | ANSI | VT100 | VT220 | VT320 | VT340 | Notes |
|-------------|-----------|----------------|---------|-------|--------|--------|--------|--------|--------|
| NUL – Null | C0 | 00/00 (0x00) | ECMA | YES | YES | YES | YES | YES | Ignored character |
| BEL – Bell | C0 | 00/07 (0x07) | ECMA | YES | YES | YES | YES | YES | Audible bell or visual flash |
| BS – Backspace | C0 | 00/08 (0x08) | ECMA | YES | YES | YES | YES | YES | Moves cursor left |
| HT – Horizontal Tab | C0 | 00/09 (0x09) | ECMA | YES | YES | YES | YES | YES | Tab stops at 8 columns |
| LF – Line Feed | C0 | 00/10 (0x0A) | ECMA | YES | YES | YES | YES | YES | Moves cursor down one line |
| CR – Carriage Return | C0 | 00/13 (0x0D) | ECMA | YES | YES | YES | YES | YES | Moves to start of line |
| IND – Index | ESC D | ESC D | ECMA | YES | YES | YES | YES | YES | Cursor down one line (scroll) |
| NEL – Next Line | ESC E | ESC E | ECMA | YES | YES | YES | YES | YES | CR+LF equivalent |
| RI – Reverse Index | ESC M | ESC M | ECMA | YES | YES | YES | YES | YES | Cursor up one line (scroll up) |
| CUP – Cursor Position | CSI Pn;Pn H | CSI Pn;Pn H | ECMA | YES | YES | YES | YES | YES | Direct cursor addressing |
| CUU – Cursor Up | CSI Pn A | CSI Pn A | ECMA | YES | YES | YES | YES | YES | Moves cursor up |
| CUD – Cursor Down | CSI Pn B | CSI Pn B | ECMA | YES | YES | YES | YES | YES | Moves cursor down |
| CUF – Cursor Forward | CSI Pn C | CSI Pn C | ECMA | YES | YES | YES | YES | YES | Moves cursor right |
| CUB – Cursor Backward | CSI Pn D | CSI Pn D | ECMA | YES | YES | YES | YES | YES | Moves cursor left |
| ED – Erase in Page | CSI Ps J | CSI Ps J | ECMA | YES | YES | YES | YES | YES | Clear display |
| EL – Erase in Line | CSI Ps K | CSI Ps K | ECMA | YES | YES | YES | YES | YES | Clear line section |
| SGR – Select Graphic Rendition | CSI Ps m | CSI Ps m | ECMA | YES | YES | YES | YES | YES | Bold, underline, reverse, etc. |
| DSR – Device Status Report | CSI Ps n | CSI Ps n | ECMA | YES | YES | YES | YES | YES | Cursor/status report |
| DA – Device Attributes | CSI Ps c | CSI Ps c | ECMA | YES | YES | YES | YES | YES | Report terminal ID |
| DECSC / DECRC – Save/Restore Cursor | ESC 7 / ESC 8 | ESC 7 / ESC 8 | DEC | NO | YES | YES | YES | YES | Save/restore cursor position |
| DECALN – Screen Alignment | ESC # 8 | ESC # 8 | DEC | NO | YES | YES | YES | YES | Screen fill with 'E' |
| DECSTBM – Set Scrolling Region | CSI Pt;Pb r | CSI Pt;Pb r | DEC | NO | YES | YES | YES | YES | Set scroll margins |
| DECAWM – Auto Wrap Mode | CSI ? 7 h/l | CSI ? 7 h/l | DEC | NO | YES | YES | YES | YES | Wrap at end of line |
| DECOM – Origin Mode | CSI ? 6 h/l | CSI ? 6 h/l | DEC | NO | YES | YES | YES | YES | Origin mode control |
| DECTCEM – Cursor Visibility | CSI ? 25 h/l | CSI ? 25 h/l | DEC | NO | NO | YES | YES | YES | Show/hide cursor |
| DECSCNM – Screen Mode | CSI ? 5 h/l | CSI ? 5 h/l | DEC | NO | NO | YES | YES | YES | Reverse video mode |
| DECCOLM – Column Mode | CSI ? 3 h/l | CSI ? 3 h/l | DEC | NO | NO | YES | YES | YES | 80/132 column select |
| DECSTR – Soft Reset | CSI ! p | CSI ! p | DEC | NO | NO | YES | YES | YES | Partial reset |
| DECSLRM – Set Left/Right Margins | CSI Pl;Pr s | CSI Pl;Pr s | DEC | NO | NO | NO | YES | YES | Set margins |
| VT52 Commands | ESC + variants | ESC + variants | DEC | NO | YES | NO | NO | NO | VT52 compatibility |

---

## Manual Validation Tables

### VT100 Validation Table

| Definition | Notation | Representation | Implemented | Validated | Notes |
|-------------|-----------|----------------|--------------|------------|--------|
| BEL – Bell | C0 | 0x07 |  |  |  |
| BS – Backspace | C0 | 0x08 |  |  |  |
| HT – Horizontal Tab | C0 | 0x09 |  |  |  |
| LF – Line Feed | C0 | 0x0A |  |  |  |
| CR – Carriage Return | C0 | 0x0D |  |  |  |
| IND – Index | ESC D | ESC D |  |  |  |
| NEL – Next Line | ESC E | ESC E |  |  |  |
| RI – Reverse Index | ESC M | ESC M |  |  |  |
| CUP – Cursor Position | CSI Pn;Pn H | CSI Pn;Pn H |  |  |  |
| CUU / CUD / CUF / CUB | CSI Pn A/B/C/D | CSI Pn A/B/C/D |  |  |  |
| ED – Erase in Page | CSI Ps J | CSI Ps J |  |  |  |
| EL – Erase in Line | CSI Ps K | CSI Ps K |  |  |  |
| SGR – Select Graphic Rendition | CSI Ps m | CSI Ps m |  |  |  |
| DSR – Device Status Report | CSI Ps n | CSI Ps n |  |  |  |
| DECALN | ESC # 8 | ESC # 8 |  |  |  |

---

### VT220 Validation Table

| Definition | Notation | Representation | Implemented | Validated | Notes |
|-------------|-----------|----------------|--------------|------------|--------|
| All VT100 sequences | – | – |  |  | Inherited |
| DECTCEM – Cursor Visibility | CSI ? 25 h/l | CSI ? 25 h/l |  |  |  |
| DECSCNM – Screen Mode | CSI ? 5 h/l | CSI ? 5 h/l |  |  |  |
| DECCOLM – Column Mode | CSI ? 3 h/l | CSI ? 3 h/l |  |  |  |
| DECSTR – Soft Reset | CSI ! p | CSI ! p |  |  |  |

---

### VT340 Validation Table

| Definition | Notation | Representation | Implemented | Validated | Notes |
|-------------|-----------|----------------|--------------|------------|--------|
| All VT220 sequences | – | – |  |  | Inherited |
| DECSLRM – Set Left/Right Margins | CSI Pl;Pr s | CSI Pl;Pr s |  |  |  |
| DECSCA – Select Character Protection | CSI Ps " q | CSI Ps " q |  |  |  |
| DECSCL – Select Conformance Level | CSI Ps;Ps SP q | CSI Ps;Ps SP q |  |  |  |
| DECAUPSS – Alternate Character Set | CSI Ps SP F | CSI Ps SP F |  |  |  |
| DECRQCRA – Request Checksum | CSI t | CSI t |  |  |  |

---

*(End of Document)*
