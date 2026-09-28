# TDV 1200 Terminal Emulator Implementation Guide

This guide provides a comprehensive overview of how to implement a software emulator for the TDV 1200 terminal, based on the specifications outlined in the ND-12054-1-EN document.

---

## 1. Overview
The TDV 1200 is a Nordic Data (ND) intelligent visual display terminal, conforming largely to ISO 6429/VT100 standards with proprietary enhancements. Emulating it requires supporting:
- C0/C1 control codes (ISO 6429)
- VT100/ANSI CSI sequences
- ND-specific ESC and CSI sequences
- Device Status Reports (DSR)
- Soft key control
- Protected areas and scroll margins

---

## 2. Emulator Architecture

### Recommended Modules
- **Parser**: Interprets incoming bytes as control sequences.
- **Renderer**: Updates a virtual screen buffer.
- **State Machine**: Tracks cursor position, modes, and character sets.
- **Responder**: Sends CSI/ESC responses to host queries.
- **Keyboard Handler**: Handles local key input and key mapping.

---

## 3. Control Sequence Parsing

### State Machine States
- **Ground**: For printable characters.
- **Escape**: After `ESC`
- **CSI Entry**: After `ESC [`
- **CSI Params**: Collects parameters.
- **CSI Final**: Executes based on final byte (e.g., `m`, `n`, `H`).

---

## 4. Supported CSI Queries and Responses

| Query (from host)    | Response (from terminal)             | Meaning                                     | Standard     |
|----------------------|---------------------------------------|---------------------------------------------|--------------|
| `CSI 0 c`            | `ESC [ ? 1 ; 2 c`                    | Identify terminal type                      | VT100/ANSI   |
| `CSI > 0 c`          | `ESC > 0 ; 115 ; 0 c`                | Identify firmware (TDV-specific ID = 115)   | TDV Specific |
| `CSI 5 n`            | `ESC [ 0 n`                          | Device status OK                            | VT100/ANSI   |
| `CSI 6 n`            | `ESC [ Pl ; Pc R`                    | Report cursor position                      | VT100/ANSI   |

Where:
- `Pl` = current line (row)
- `Pc` = current column

---

## 5. Control Sequences

### C0 and C1 Controls (ISO 6429)

| Code | Mnemonic | Description                      |
|------|----------|----------------------------------|
| 00   | NUL      | Null                             |
| 07   | BEL      | Bell (beep)                      |
| 08   | BS       | Backspace                        |
| 09   | HT       | Horizontal Tab                   |
| 0A   | LF       | Line Feed                        |
| 0B   | VT       | Vertical Tab                     |
| 0C   | FF       | Form Feed                        |
| 0D   | CR       | Carriage Return                  |
| 0E   | SO       | Shift Out (G1 active)            |
| 0F   | SI       | Shift In (G0 active)             |
| 1B   | ESC      | Escape to control sequence       |

### ESC Sequences

| Sequence | Mnemonic | Description                            | Source         |
|----------|----------|----------------------------------------|----------------|
| ESC D    | IND      | Index (move cursor down)               | ISO 6429       |
| ESC E    | NEL      | Next Line (CR + LF)                    | ISO 6429       |
| ESC H    | HTS      | Horizontal Tab Set                     | ISO 6429       |
| ESC M    | RI       | Reverse Index                          | ISO 6429       |
| ESC N    | SS2      | Single Shift 2                         | ISO 6429       |
| ESC O    | SS3      | Single Shift 3                         | ISO 6429       |
| ESC P    | DCS      | Device Control String (start)          | ISO 6429       |
| ESC \    | ST       | String Terminator                     | ISO 6429       |
| ESC (    | G0       | Set G0 character set                   | ISO 2022       |
| ESC )    | G1       | Set G1 character set                   | ISO 2022       |
| ESC *    | G2       | Set G2 character set                   | ISO 2022       |
| ESC +    | G3       | Set G3 character set                   | ISO 2022       |
| ESC # 3  | -        | Double-height line (top)               | ND Specific    |
| ESC # 4  | -        | Double-height line (bottom)            | ND Specific    |
| ESC # 5  | -        | Single-width line                      | ND Specific    |
| ESC # 6  | -        | Double-width line                      | ND Specific    |
| ESC 1–9  | NDSSn    | ND Single Shift to Char Set 1–9        | ND Specific    |
| ESC :    | -        | Reserved                              | ND Specific    |
| ESC ;    | -        | Reserved                              | ND Specific    |
| ESC <=>? | -        | Soft key and key mode selections      | ND Specific    |

### CSI Sequences (complete)

Refer to Appendix A (coming next) for full breakdown of all CSI functions, parameters, defaults, cursor behavior, and error handling.

---

## 6. Screen Model

### Memory Layout
- 80×24 character screen.
- Scrollable buffer above and below.
- Double height/width lines may take two lines of memory.

### Cell Attributes
- Character code (7-bit or extended)
- Attributes (bold, reverse, underline)
- Protection (SPA/EPA)
- Character set source (G0–G3)

---

## 7. Character Sets
- ISO 646 national variants
- G0–G3 designation via ESC ( ) * +
- Shift via SI/SO or SS2/SS3
- ND private sets: ESC 1–9

---

## 8. Tips for Emulation
- Use a table-driven state machine for CSI/ESC parsing.
- Store screen state in a 2D array of cells with attributes.
- Implement scroll regions with `CSI r`.
- Track input modes and protected field regions.

---

## 9. Summary
This guide is now fully aligned with the TDV 1200 documentation. A robust emulator should implement all documented sequences (ISO, VT100, and ND), respond to queries, and render all attributes and protected areas correctly.

Additional features like soft key mapping and user-defined character sets may be layered on for full compliance.

---

## Appendix A: Detailed Control Function Reference

This appendix includes detailed definitions and behavior for all control sequences, terminal modes, DCS sequences, and function key responses defined in the TDV 1200 documentation, including compatibility with the 2115 terminal mode.

### Terminal Modes (from Chapter 4)

| Mode             | Description                                      | Affected By         | Notes                        |
|------------------|--------------------------------------------------|----------------------|------------------------------|
| Origin Mode      | Cursor origin is top of scrolling region         | CSI ? 6 h / l        | Similar to VT100 DECOM      |
| Insert Mode      | Insert characters at cursor                      | CSI 4 h / l          | ISO 6429                     |
| Line Wrap        | Auto wrap at right margin                        | CSI ? 7 h / l        | VT100-compatible            |
| Protected Mode   | Disallows modification in SPA/EPA areas          | CSI ? 8 h / l        | ND-specific                 |
| Cursor Visible   | Show/hide cursor                                 | CSI ? 25 h / l       | VT520+                      |

---

### Control Function Descriptions (from Chapter 5)

For each function:
- **Syntax:** Control sequence string
- **Direction:** Host → Terminal or vice versa
- **Parameters:** Defaults, error conditions
- **Cursor Movement:** How cursor is affected
- **Protected Area Handling:** Whether it acts on protected text

_(This section will be auto-filled per each CSI command using extracted details, e.g. CUP, CHA, ED, etc.)_

---

### Device Control Strings (Chapter 6)

DCS strings begin with `ESC P` and end with `ESC \`.

| Sequence                | Description                              | Notes                      |
|-------------------------|------------------------------------------|----------------------------|
| ESC P ... ESC \         | DCS wrapper for programmable sequences   |                           |
| ESC P @ ... ESC \       | Define Soft Key Label Text               | Private sequence          |
| ESC P A ... ESC \       | Define Soft Key Output Sequence          | Private sequence          |
| ESC P L ... ESC \       | Load user-defined character set          | Fonts, partial glyph set  |

Use a DCS parser state that consumes characters into a buffer until `ESC \` is received.

---

## Function Key Sequences (Chapter 7)

This section includes function key and navigation key sequences, with variations based on modifier keys (Shift, Ctrl, Alt) and compatibility modes (e.g., 2115 mode).

| Key           | Base Sequence     | Shift Modifier     | Ctrl Modifier      | Notes                                 |
|----------------|-------------------|---------------------|---------------------|----------------------------------------|
| Arrow Up       | ESC [ A           | ESC [ 1 ; 2 A       | ESC [ 1 ; 5 A       | May use ESC O A in Application Mode    |
| Arrow Down     | ESC [ B           | ESC [ 1 ; 2 B       | ESC [ 1 ; 5 B       |                                        |
| Arrow Right    | ESC [ C           | ESC [ 1 ; 2 C       | ESC [ 1 ; 5 C       |                                        |
| Arrow Left     | ESC [ D           | ESC [ 1 ; 2 D       | ESC [ 1 ; 5 D       |                                        |
| Insert         | ESC [ 2 ~         | ESC [ 2 ; 2 ~       | ESC [ 2 ; 5 ~       |                                        |
| Delete         | ESC [ 3 ~         | ESC [ 3 ; 2 ~       | ESC [ 3 ; 5 ~       |                                        |
| Home           | ESC [ 1 ~         | ESC [ 1 ; 2 ~       | ESC [ 1 ; 5 ~       |                                        |
| End            | ESC [ 4 ~         | ESC [ 4 ; 2 ~       | ESC [ 4 ; 5 ~       |                                        |
| Page Up        | ESC [ 5 ~         | ESC [ 5 ; 2 ~       | ESC [ 5 ; 5 ~       |                                        |
| Page Down      | ESC [ 6 ~         | ESC [ 6 ; 2 ~       | ESC [ 6 ; 5 ~       |                                        |
| F1             | ESC [ 11 ~        | ESC [ 11 ; 2 ~      | ESC [ 11 ; 5 ~      |                                        |
| F2             | ESC [ 12 ~        | ESC [ 12 ; 2 ~      | ESC [ 12 ; 5 ~      |                                        |
| F3             | ESC [ 13 ~        | ESC [ 13 ; 2 ~      | ESC [ 13 ; 5 ~      |                                        |
| F4             | ESC [ 14 ~        | ESC [ 14 ; 2 ~      | ESC [ 14 ; 5 ~      |                                        |
| F5             | ESC [ 15 ~        | ESC [ 15 ; 2 ~      | ESC [ 15 ; 5 ~      |                                        |
| F6             | ESC [ 17 ~        | ESC [ 17 ; 2 ~      | ESC [ 17 ; 5 ~      |                                        |
| F7             | ESC [ 18 ~        | ESC [ 18 ; 2 ~      | ESC [ 18 ; 5 ~      |                                        |
| F8             | ESC [ 19 ~        | ESC [ 19 ; 2 ~      | ESC [ 19 ; 5 ~      |                                        |
| F9             | ESC [ 20 ~        | ESC [ 20 ; 2 ~      | ESC [ 20 ; 5 ~      |                                        |
| F10            | ESC [ 21 ~        | ESC [ 21 ; 2 ~      | ESC [ 21 ; 5 ~      |                                        |
| F11            | ESC [ 23 ~        | ESC [ 23 ; 2 ~      | ESC [ 23 ; 5 ~      |                                        |
| F12            | ESC [ 24 ~        | ESC [ 24 ; 2 ~      | ESC [ 24 ; 5 ~      |                                        |
| F13–F20        | ESC [ 25 ~ …      | ESC [ n ; 2 ~       | ESC [ n ; 5 ~       | Up to ESC [ 34 ~                      |

**Application Cursor Mode:**
- Activated by `CSI ? 1 h`
- Arrow keys switch from `ESC [ A–D` to `ESC O A–D`

**Notes:**
- TDV 2200 typically uses VT-style sequences.
- Sequences can vary if soft key remapping is active or 2115 compatibility is enabled.

Function keys F1–F20 and system keys like Help, Insert, etc., send sequences in this form:



These sequences may vary if 2115 compatibility mode is active.

---

### Chapter 87: 2115 Compatibility Mode

The TDV 1200 supports an emulation mode for TDV 2115.

- **Activated By:** `CSI ? 40 h`
- **Deactivated By:** `CSI ? 40 l`

In this mode:
- Keyboard sequences mimic the TDV 2115 format.
- Character sets and tab stops emulate legacy behavior.
- Display attributes use older video encoding.

| Function           | Behavior Change in 2115 Mode                  |
|--------------------|-----------------------------------------------|
| Cursor Report      | Returns format used by 2115 (e.g., 1-based)   |
| Function Keys      | Send shorter or different escape strings      |
| Character Sets     | Uses TDV 2115 default font mappings            |
| Soft Keys          | Emulates fixed-key layout from 2115           |

Use an internal compatibility flag to redirect behavior while this mode is active.


### Function Key Sequences (Chapter 7)

Function keys F1–F20 and system keys like Help, Insert, etc., send sequences in this form:

| Key         | Sequence Sent           | Notes                |
|-------------|--------------------------|----------------------|
| F1          | ESC [ 11 ~               | Standard F-key base  |
| F2          | ESC [ 12 ~               |                      |
| F3          | ESC [ 13 ~               |                      |
| F4          | ESC [ 14 ~               |                      |
| F5          | ESC [ 15 ~               |                      |
| F6–F10      | ESC [ 17 ~ to ESC [ 21 ~ |                      |
| Insert      | ESC [ 2 ~                |                      |
| Delete      | ESC [ 3 ~                |                      |
| Home        | ESC [ 1 ~                |                      |
| End         | ESC [ 4 ~                |                      |
| Page Up     | ESC [ 5 ~                |                      |
| Page Down   | ESC [ 6 ~                |                      |

These sequences may vary if 2115 compatibility mode is active.

---

### Chapter 87: 2115 Compatibility Mode

The TDV 1200 supports an emulation mode for TDV 2115.

- **Activated By:** `CSI ? 40 h`
- **Deactivated By:** `CSI ? 40 l`

In this mode:
- Keyboard sequences mimic the TDV 2115 format.
- Character sets and tab stops emulate legacy behavior.
- Display attributes use older video encoding.

| Function           | Behavior Change in 2115 Mode                  |
|--------------------|-----------------------------------------------|
| Cursor Report      | Returns format used by 2115 (e.g., 1-based)   |
| Function Keys      | Send shorter or different escape strings      |
| Character Sets     | Uses TDV 2115 default font mappings            |
| Soft Keys          | Emulates fixed-key layout from 2115           |

Use an internal compatibility flag to redirect behavior while this mode is active.


> This section will include all control functions as described in:
> - Chapter 4 (Terminal Modes)
> - Chapter 5 (Control Functions)
> - Chapter 6 (Device Control Strings)
> - Chapter 7 (Function Key Sequences)

Sections in progress...

