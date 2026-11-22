# 3270 Data Stream - Specification for RetroTerm

What the 3270 data stream is, byte by byte, as RetroTerm has to handle it. This document says
what the bytes mean. It does not say how the C# code is built.

## Sources and how they are cited

| Short name | What it is | Where |
|---|---|---|
| **GA23-0059-4** | IBM 3270 Information Display System, *Data Stream Programmer's Reference*, December 1988. **The authority.** | the IBM-manuals folder |
| **GA23-0061-2** | IBM 3274 Control Unit Description and Programmer's Guide, March 1985. Used for the channel command codes and the base-colour rule. | the IBM-manuals folder |
| **x3270** | x3270 source (oracle, read only, never copied): `include/3270ds.h`, `Common/ctlr.c`, `Common/sf.c` | the hercules source tree, x3270 folder |
| **tnz** | IBM's Python tnz (oracle, read only, never copied): `tnz/tnz.py` | the hercules source tree, tnz folder |
| **z/OS Basic Skills** | IBM web page "3270 data stream", https://www.ibm.com/docs/en/zos-basic-skills?topic=terminal-3270-data-stream | web |

Every fact carries "GA23-0059-4 p. X-Y" (the page number printed on the manual page, not the PDF
page number). The text layer of the PDF is old OCR (letter O for zero, B for 8). Every table that
came out garbled was rendered to an image and read from the image; the values below come from the
images, not from the OCR text.

Where the manual is silent and an oracle is used, the text says so. Where sources disagree, all of
them are written down. Anything not checked is marked **NOT VERIFIED**.

Byte values are written `X'nn'` as IBM does. **Bit 0 is the most significant bit** (IBM numbering),
so "bit 7" is the value 0x01 and "bit 0" is 0x80.

---

## 1. Overview in plain words

Taken from the z/OS Basic Skills page (fetched 2026-11-22; only a summary of the page was
available to me through the fetch tool, not the full page text, and the sibling pages in that topic
were not read):

- The 3270 data stream carries data between a mainframe program and a 3270 display (screen plus
  keyboard), and also to 3270 printers.
- The terminal keeps a **character buffer**. Each buffer location is one position on the screen.
  The host changes it by sending data; the user changes it by typing.
- Text is **EBCDIC**.
- The host sends a **command** (Write, Erase/Write, Erase All Unprotected, Read Modified, Write
  Structured Field ...). A write is usually followed by a **Write Control Character (WCC)** that
  can sound the alarm or unlock the keyboard.
- The screen is split into **fields**. Each field starts with a **field attribute** byte that says:
  protected or not (only unprotected fields take input), shown or hidden (hidden is used for
  passwords), bright or normal, letters or numbers only.
- **Extended attributes** add colour and highlighting.
- When the user presses Enter, a PF key, PA key or Clear, the terminal sends an **Attention
  Identifier (AID)** byte telling the host which key it was, and usually the changed fields.

Places where the Basic Skills page and GA23-0059-4 disagree:

| Point | Basic Skills page | GA23-0059-4 |
|---|---|---|
| AID | listed among the "basic data stream commands" | not a command. It appears only in the inbound stream and must be its first byte (p. 3-8). |
| Numeric field content | "digits, a period, and a minus sign" | "numeric characters, the minus and decimal-sign characters, and the duplicate (DUP) control" (p. 4-12). The manual adds DUP. |
| WSF purpose | sends "control information such as color and screen dimensions" | WSF carries structured fields of every kind (p. 3-7, 5-1); colour is set by orders (SFE/SA/MF), screen size by EWA or Erase/Reset. The page's wording is loose, not wrong in an important way. |

---

## 2. Outbound commands

The command is the first byte of the stream. Only one command per chain (GA23-0059-4 p. 3-2).

### 2.1 Command codes

There are two sets of codes. The **SNA / TN3270** set is what a TN3270 host sends
(GA23-0059-4 p. 3-2, Table 3-1). The **channel (CCW)** set is what a locally attached
(3274 model B/D) controller receives (GA23-0061-2 p. 1-14, Figure 1-8). A TN3270 client sees the
SNA set; x3270 accepts both.

| Command | Abbr. | SNA / EBCDIC (GA23-0059-4 p. 3-2) | ASCII (p. 3-2) | Channel code, 3274 B/D (GA23-0061-2 p. 1-14) | x3270 `3270ds.h` channel name |
|---|---|---|---|---|---|
| Write | W | X'F1' | X'31' | X'01' | CMD_W 0x01 |
| Erase/Write | EW | X'F5' | X'35' | X'05' | CMD_EW 0x05 |
| Erase/Write Alternate | EWA | X'7E' | X'3D' | X'0D' | CMD_EWA 0x0d |
| Read Buffer | RB | X'F2' | X'32' | X'02' | CMD_RB 0x02 |
| Read Modified | RM | X'F6' | X'36' | X'06' | CMD_RM 0x06 |
| Read Modified All | RMA | X'6E' | X'3E' | NA (not listed for 3274 B/D) | CMD_RMA 0x0e |
| Erase All Unprotected | EAU | X'6F' | X'3F' | X'0F' | CMD_EAU 0x0f |
| Write Structured Field | WSF | X'F3' | none - WSF not supported in ASCII | X'11' (3274 D only) | CMD_WSF 0x11 |
| No Operation | NOP | - | - | X'03' | CMD_NOP 0x03 |

The ASCII codes were read from the page image; the OCR text showed X'3S' and X'30' (wrong).

Disagreement: GA23-0061-2 gives **no** channel code for RMA (marked NA); x3270 uses X'0E'. Where
X'0E' comes from I have not found in the manuals I read. **NOT VERIFIED.**

"The command codes are not unique code points; they rely on position to resolve ambiguity"
(GA23-0059-4 p. 3-2) - X'F1' is Write as the first byte but PF1 or "1" elsewhere.

### 2.2 The Write Control Character (WCC)

The byte after W, EW, EWA is the WCC. WSF and EAU have no WCC (p. 3-2, p. 5-1; EAU in Outbound
3270DS has no WCC, p. 5-40).

| Bit | Mask | Meaning (GA23-0059-4 p. 3-3, Table 3-2) |
|---|---|---|
| 0 | X'80' | No function when reset is supported. Otherwise set per Figure D-1 to keep the byte a printable character. |
| 1 | X'40' | **Reset.** 1 = reset partition characteristics to defaults. On EW/EWA: reset inbound reply mode to Field, go back to implicit partition state (p. 3-6). Not usable in ASCII. |
| 2, 3 | X'20', X'10' | Reserved. |
| 4 | X'08' | **Start printer** - local copy at the end of the write. No printer: negative response X'0801'. |
| 5 | X'04' | **Sound alarm** at the end of the operation. |
| 6 | X'02' | **Keyboard restore** - unlock the keyboard and reset the AID. |
| 7 | X'01' | **Reset MDT** - clear every MDT bit in the existing buffer before any data or orders are done. |

x3270 `3270ds.h` uses the same masks (0x40, 0x08, 0x04, 0x02, 0x01). They agree.

Timing: "All WCC functions except for Reset MDT are deferred until data is written and orders are
performed" (p. 3-2). Reset MDT happens first (p. 3-3).

A write command with no WCC and no data is a no-op (p. 3-2). An EW with no WCC is a no-op: nothing
is erased and no read is acknowledged (p. 3-6).

### 2.3 Write (W)

- Writes into the buffer without erasing other locations (p. 3-5).
- If no SBA follows the WCC, writing starts **at the cursor address** (p. 3-5).
- The buffer address moves on by one for each character stored (p. 3-5).

### 2.4 Erase/Write (EW) - in this order (p. 3-6)

1. In implicit partition state, set the implicit partition to the **default** size.
2. Fill the whole buffer with nulls.
3. Set all character attributes and extended field attributes to X'00'.
4. Erase all field validation attributes.
5. Cursor to address 0.
6. If WCC bit 1 (reset) is 1: reply mode back to Field; destroy partitions, make implicit
   partition 0 at default size, INPID = 0, INOP = Read Modified.
7. If WCC keyboard restore is 1, acknowledge any outstanding read or enter.
8. Give a negative trigger reply.
9. Do the write.

### 2.5 Erase/Write Alternate (EWA)

Same as EW but uses the **alternate** screen size. "If there is no alternate screen size, the EWA is
treated the same as an Erase/Write command" (p. 3-6). "EW sets and uses the default display screen
size while EWA sets and uses the alternate display screen size" (p. 3-5).

So the screen size changes only on EW (to default) and EWA (to alternate). The default and alternate
sizes are reported to the host in the Implicit Partition query reply (section 9.7). Within an
Outbound 3270DS or Present Absolute Format structured field, EW and EWA **do not** change the screen
size - "Use the Erase/Reset structured field to do this" (p. 5-40 note).

### 2.6 Erase All Unprotected (EAU) (p. 3-7)

- Clear all unprotected character locations to nulls; reset their character attributes.
- Reset the MDT bit of every unprotected field.
- Unlock the keyboard. Reset the AID.
- Cursor to the first character position of the first unprotected field.
- If every location is protected: nothing cleared, no MDT reset, but keyboard still unlocked, AID
  reset, cursor to address 0 of the partition.

### 2.7 Write Structured Field (WSF) (p. 3-7, 5-1)

`WSF` then one or more structured fields:

| Bytes | Content |
|---|---|
| 0-1 | Length of this structured field, **including** the two length bytes |
| 2 (or 2-3) | ID (one byte, or two bytes for X'0Fnn' and X'10nn' IDs) |
| ... | parameters and data |

A length of 0 on an Outbound or Inbound 3270DS means "this is the last field; it runs to the end
of the chain" (p. 5-2). A device without structured fields rejects WSF (sense X'1003') (p. 3-7).

Outbound structured field IDs (GA23-0059-4 p. 5-2, read from the page image):

| ID | Name | ID | Name |
|---|---|---|---|
| X'0E' | Activate Partition | X'01' | Read Partition |
| X'0F85' | Begin/End of File | X'1030' | Request Recovery Data |
| X'0C' | Create Partition | X'00' | Reset Partition |
| X'0D' | Destroy Partition | X'1033' | Restart |
| X'03' | Erase/Reset | X'41' | SCS Data |
| X'0F05' | Load Color Table | X'0F04' | Select Color Table |
| X'0F07' | Load Line Type | X'4A' | Select Format Group |
| X'06' | Load Programmed Symbols | X'1032' | Set Checkpoint Interval |
| X'0F0A' | Modify Partition | X'0F01' | Set MSR Control |
| X'0F71' | Outbound Text Header | X'0F84' | Set Printer Characteristics |
| X'0FC1' | Outbound Type 1 Text | X'09' | Set Reply Mode |
| X'40' | Outbound 3270DS | X'0B' | Set Window Origin |
| X'4B' | Present Absolute Format | X'4C' | Present Relative Format |

The ones a modern emulator needs are described below: Read Partition, Erase/Reset, Set Reply
Mode, Outbound 3270DS.

#### Outbound 3270DS, X'40' (p. 5-40)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'40' |
| 3 | PID, X'00'-X'7E'. A device without partitions must get 0. |
| 4 | Partition command: X'F1' Write, X'F5' Erase/Write, X'7E' Erase/Write Alternate, X'6F' Erase All Unprotected, X'F7' BSC Copy |
| 5 | WCC (for F1, F5, 7E). **No WCC for X'6F'.** |
| 6-n | orders and data |

#### Erase/Reset, X'03' (p. 5-15)

| Byte | Content |
|---|---|
| 0-1 | Length (X'0004') |
| 2 | X'03' |
| 3 | Flags. Bit 0 IPZ: 0 = default size, 1 = alternate size. Bits 1-7 reserved. |

Destroys all partitions and makes implicit partition 0 at the chosen size.

#### Set Reply Mode, X'09' (p. 5-61, 5-62)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'09' |
| 3 | PID (X'00'-X'7F' per the image; note it says 7F here, 7E elsewhere) |
| 4 | Mode: X'00' Field, X'01' Extended field, X'02' Character, other reserved (rejected) |
| 5-n | Only for mode X'02': list of attribute type codes that are sent inbound with SA orders and that the operator may select |

---

## 3. Orders

Orders are one byte, value X'00'-X'3F'. Codes in that range that are not orders are rejected
(GA23-0059-4 p. 4-1). Inbound streams may contain only SF, SFE, SBA, SA, GE (p. 4-1).

### 3.1 Order codes (GA23-0059-4 p. 4-2, from the page image)

| Order | EBCDIC | ASCII | Bytes after the order |
|---|---|---|---|
| Start Field (SF) | X'1D' | X'1D' | 1: field attribute |
| Start Field Extended (SFE) | X'29' | not supported | 1 count n, then n pairs (type, value) |
| Set Buffer Address (SBA) | X'11' | X'11' | 2: address |
| Set Attribute (SA) | X'28' | not supported | 2: type, value |
| Modify Field (MF) | X'2C' | not supported | 1 count n, then n pairs |
| Insert Cursor (IC) | X'13' | X'13' | 0 |
| Program Tab (PT) | X'05' | X'09' | 0 |
| Repeat to Address (RA) | X'3C' | X'14' | 2 stop address, then 1 character (or GE + 1 character) |
| Erase Unprotected to Address (EUA) | X'12' | X'12' | 2: stop address |
| Graphic Escape (GE) | X'08' | not supported | 1: character |

x3270 `3270ds.h` has the same EBCDIC codes, plus X'2B' ("Yale sub command"), which is not in the
manual.

### 3.2 SF - Start Field, X'1D' (p. 4-2)

- The next byte is **always** a field attribute.
- It is stored at the current buffer address; the address moves on by 1.
- The extended field attribute of that position is set to its default.
- Inbound: put before every field attribute in a Read Buffer reply.

### 3.3 SFE - Start Field Extended, X'29' (p. 4-2, 4-3)

`X'29'  n  (type value) x n`

- The field attribute itself is one of the pairs, type X'C0'.
- Unspecified types take their default. n = 0 sets all defaults.
- Unknown types or values that the device cannot keep and send back are rejected.
- If a type appears twice, the last one wins (all are still checked).
- Manual silent on what is stored when no X'C0' pair is present. x3270 `ctlr.c`: **NOT VERIFIED**
  what it does - not read.

### 3.4 SBA - Set Buffer Address, X'11' (p. 4-3, 4-4)

`X'11'  a1  a2`

- Address is relative to the partition origin.
- **Address out of range: the write stops at once, with a negative response** (p. 4-3).
  x3270 `ctlr.c` agrees: if the address is >= rows x cols it shows an error and ends the write.
- Inbound (RM / RMA): an SBA with the address of the field attribute + 1 replaces each modified
  field's attribute. Not used in Read Buffer replies (p. 4-4).
- Address encoding: section 6.

### 3.5 SA - Set Attribute, X'28' (p. 4-5, 4-6)

`X'28'  type  value`

- Sets the "current" character attribute applied to every following character, until another SA
  changes it, another write command arrives, Clear is pressed, or power off.
- The current set is built type by type: SA colour red, then SA charset X'F3', then SA colour blue
  gives (default highlight, blue, X'F3'). `SA X'00' X'00'` puts all three back to default (example
  on p. 4-5).
- Type X'00' is always supported by SA.
- Inbound: SA orders are only sent in Character reply mode, only for types listed in Set Reply
  Mode, and only when a value changes; the start state is all defaults (p. 4-5, 4-6, 3-12).

### 3.6 MF - Modify Field, X'2C' (p. 4-6, 4-7)

`X'2C'  n  (type value) x n`

- Changes the field attribute / extended attributes **at the current buffer address**, then moves
  the address on by 1.
- Types not given stay as they are (unlike SFE).
- **If the current address does not hold a field attribute, MF is rejected.**
- n = 0: only checks there is a field attribute there, then address + 1.
- Repeated type: last wins.

### 3.7 IC - Insert Cursor, X'13' (p. 4-7)

Moves the cursor to the current buffer address. The buffer address does not change.

### 3.8 PT - Program Tab, X'05' (p. 4-7)

- Moves the buffer address to the first character position of the next **unprotected** field.
- If the current address **is** the field attribute of an unprotected field, it moves on by one
  only.
- **Null fill:** if PT does *not* come straight after a command, an order, or an order sequence
  (WCC, IC, RA are given as examples), nulls are stored from the current address to the end of the
  field - "regardless of the value of bit 2 (protected/unprotected)". If it does come straight
  after one, the buffer is not changed.
- PT resets the character attribute of each location it nulls.
- **End of buffer:** the search stops at the last location. No unprotected field found -> address
  becomes 0. An unprotected field attribute found in the **last** location -> address also becomes 0.
- **To search again from the top**, a second PT must follow immediately; the search then starts at 0.
  If the first PT was still nulling when it hit the end of the buffer, the second PT goes on nulling
  from 0 to the end of the current field.

x3270 `ctlr.c` (read, not copied): tracks "last thing was a command or order" and "last PT ended at
0 while nulling"; nulls when the former is false **or** the latter is true; nulls only up to the
next field attribute; a found field before the current address (that is, a wrap) is treated as
"not found" and the address becomes 0. This matches the manual. One detail: x3270 counts an SBA
as "an order", so `SBA PT` does not null. The manual says "order", so it agrees.

### 3.9 RA - Repeat to Address, X'3C' (p. 4-8)

`X'3C'  a1 a2  c`   or   `X'3C'  a1 a2  X'08' c`

- Stores the character from the current address up to **but not including** the stop address.
- **Stop address lower than current: wraps** from the last location to the first.
- **Stop address equal to current: fills the whole buffer.**
- Afterwards the buffer address is the stop address.
- **Invalid stop address** (for example 1920 on a 1920 screen): the write stops there, nothing
  stored.
- The character may be any EBCDIC code that has a graphic on the device, or a GE + code pair
  (2 bytes). Otherwise rejected (sense X'1003' or X'1001').
- Character attributes from earlier SA orders apply to every repeated character.
- **Field attributes (and their extended attributes) in the range are overwritten.**

x3270 `ctlr.c` takes GE after the address the same way (one more byte, the character comes from
the alternate set).

### 3.10 EUA - Erase Unprotected to Address, X'12' (p. 4-9)

`X'12'  a1 a2`

- Nulls every **unprotected** character location from the current address up to but not including
  the stop address.
- Same wrap and "equal = whole buffer" rules as RA.
- Field attributes and extended field attributes are not touched; character attributes of nulled
  positions are reset.
- Afterwards the buffer address is the stop address.
- Invalid stop address: write stops, nothing erased, address unchanged, negative response X'1005'.

### 3.11 GE - Graphic Escape, X'08' (p. 4-9, 4-10)

`X'08'  c`

- The character comes from the alternate character set. It beats any character set in the
  extended field attribute or character attribute, both directions, any reply mode.
- c = X'00'-X'3F' or X'FF': rejected (sense X'1003').
- c = X'40'-X'FE': valid.
- No alternate set present: X'40'-X'FE' rejected (sense X'0863').
- Optional; when not supported, GE is rejected.

### 3.12 Format control codes stored in the buffer (p. 4-10)

| Code | EBCDIC | ASCII | Shown as | Read back |
|---|---|---|---|---|
| NUL | X'00' | X'00' | blank | as X'00' on Read Buffer; left out on Read Modified |
| SUB | X'3F' | - | solid circle | as SUB |
| DUP | X'1C' | X'1C' | overscore asterisk | as DUP |
| FM | X'1E' | X'1E' | overscore semicolon | as FM |
| FF | X'0C' | X'0C' | blank | as FF |
| CR | X'0D' | X'0D' | blank | as CR |
| NL | X'15' | X'0A' | blank | as NL |
| EM | X'19' | X'19' | blank | as EM |
| EO | X'FF' | - | blank | NOT VERIFIED (manual row lists only "a blank") |

### 3.13 Control sequence cut by the end of the chain

"If a control sequence attempts to span a chain, it is ended on the chain boundary" (p. 4-1).
x3270 `ctlr.c` stops the write with "Record too short" when an order's bytes are missing.

---

## 4. The field attribute byte (GA23-0059-4 p. 4-13, Table 4-1, from the image)

| Bit | Mask | Meaning |
|---|---|---|
| 0-1 | X'C0' | No meaning. Set per Figure D-1 so the byte is a printable EBCDIC/ASCII character. In SNA/EBCDIC the display **ignores** these bits on input and sets them itself (p. 4-14). |
| 2 | X'20' | 0 = unprotected, 1 = protected |
| 3 | X'10' | 0 = alphanumeric, 1 = numeric. **Bits 2 and 3 both 1 = autoskip** (protected numeric: the cursor skips the field). |
| 4-5 | X'0C' | 00 = display, not pen-detectable. 01 = display, pen-detectable. 10 = **intensified**, pen-detectable. 11 = **nondisplay**, not detectable, not printable. |
| 6 | X'02' | Reserved, must be 0 |
| 7 | X'01' | **MDT** - modified data tag |

x3270 `3270ds.h` uses the same masks.

Other rules (p. 4-13, 4-14):

- The attribute takes one buffer position and is shown as a blank.
- The operator cannot type over it. The host can overwrite it with data.
- Tab stops are at the first position after the attribute of each unprotected field (p. 4-13).
- Intensify is the only highlight in the field attribute itself.
- Nondisplay overrides every extended and character attribute for display, but those attributes
  are still sent inbound (p. 4-17).
- Default field attribute: unprotected, alphanumeric, display/not detectable, not intensified,
  MDT off (p. 4-28, Table 4-2).

---

## 5. Extended attributes and character attributes

### 5.1 Three kinds of attribute (p. 4-11)

- **Field attribute** - the byte above; takes a buffer position.
- **Extended field attribute** - colour, highlighting, character set, outlining, validation,
  transparency for a whole field; takes no position.
- **Character attribute** - colour, highlighting, character set for one character; takes no
  position. It belongs to the character, not the position: overwriting, clearing or erasing a
  character replaces it; insert/delete move it with the character (p. 4-15).

### 5.2 Which one wins (p. 4-15, 4-16, from the image)

| Screen | Character attribute | Result |
|---|---|---|
| Formatted | default (X'00') | the extended field attribute is used |
| Formatted | not default | the character attribute is used |
| Unformatted | either | the character attribute is used (device default where it is X'00') |

Each attribute type (colour, highlighting, character set) is decided on its own.

### 5.3 Attribute type codes (p. 4-16, from the image)

Types X'00'-X'7F' have plain numeric values; X'C0'-X'FF' have bit-coded values. Types not listed
are rejected (sense X'1003').

| Type | Name | In extended field attribute (SFE/MF) | In character attribute (SA) |
|---|---|---|---|
| X'00' | All character attributes (reset) | NA | Yes |
| X'C0' | 3270 field attribute | Yes | NA |
| X'C1' | Field validation | Yes | NA |
| X'C2' | Field outlining | Yes | NA |
| X'41' | Extended highlighting | Yes | Yes |
| X'42' | Foreground colour | Yes | Yes |
| X'43' | Character set | Yes | Yes |
| X'45' | Background colour | Yes | Yes |
| X'46' | Transparency | Yes | Yes |

Value X'00' must be accepted for every supported type (p. 4-17). x3270 also defines X'FE'
"input control", not in this manual.

### 5.4 Values

**X'00' All character attributes** - only value X'00'; resets colour, highlighting and character
set of the current SA set to default. Only in SA (p. 4-17).

**X'41' Extended highlighting** (p. 4-18). One value per field or character.

| Value | Meaning |
|---|---|
| X'00' | Default |
| X'F0' | Normal (as the field attribute says) |
| X'F1' | Blink |
| X'F2' | Reverse video |
| X'F4' | Underscore |
| other | reserved |

The Highlight query reply also lists **X'F8' Intensify** as a valid value (p. 6-56). Chapter 4
does not list X'F8'. See Open questions.

**X'42' foreground / X'45' background colour** (p. 4-18). X'00' = device default colour from the
Color query reply; X'F7' = neutral (or the colour from a triple-plane symbol set); all other values
"as indicated by Query Reply (Color)". The manual chapter 4 does **not** list the colour numbers.
They are in GA23-0061-2 p. 2-10 and in the Color query reply example (GA23-0059-4 p. 6-30):

| Value | Colour (GA23-0061-2 p. 2-10) |
|---|---|
| X'00' | default |
| X'F1' | blue |
| X'F2' | red |
| X'F3' | pink |
| X'F4' | green |
| X'F5' | turquoise |
| X'F6' | yellow |
| X'F7' | neutral - white on a display |

x3270 `sf.c` reports 16 colours (X'F1' up to X'FF') in its Color query reply; X'F8'-X'FF' are not
in either manual. See Open questions.

**X'43' Character set** (p. 4-19)

| Value | Meaning |
|---|---|
| X'00' | Default |
| X'40'-X'EF' | loadable sets (LCID) |
| X'F0'-X'F7' | nonloadable sets |
| X'F8'-X'FE' | double-byte sets |
| X'FF' | invalid as a value (sense X'1003') |

**X'C2' Field outlining** (p. 4-19, 4-20). Bit values: X'01' underline, X'02' right line,
X'04' overline, X'08' left line; any OR of these (16 kinds, X'0F' = full box). The left line is
drawn in the field attribute position, the right line in the next field attribute position. Always
drawn normal intensity, not reversed, not blinking, default colour.

**X'46' Transparency** (p. 4-20)

| Value | Meaning |
|---|---|
| X'00' | Default |
| X'F0' | background transparent (OR) |
| X'F1' | background transparent (XOR) |
| X'FF' | background opaque |

**X'C1' Field validation** (p. 4-21). Bits 0-4 must be 0.

| Bit | Mask | Meaning |
|---|---|---|
| 5 | X'04' | Mandatory fill |
| 6 | X'02' | Mandatory entry |
| 7 | X'01' | Trigger |

- Mandatory fill is ignored on a protected field (p. 4-17, 4-22).
- Mandatory fill: once typed into, the field must be filled before the cursor leaves it (DUP
  counts as filled); otherwise input is inhibited until Reset (p. 4-21, 4-22).
- Mandatory entry: an enter action is refused while any unprotected mandatory-entry field has
  MDT 0; cursor goes to the first such field (p. 4-22).
- Trigger: leaving a typed-in trigger field sends AID X'7F' with cursor, field address and data;
  the cursor stays in the field until the host replies; keystrokes are queued. Affirmative reply:
  a write with keyboard restore to that partition, or a null RU with CD/EB. Anything else is a
  negative reply (p. 4-23 to 4-26). Fill validation happens before the trigger (p. 4-22).

### 5.5 Defaults (p. 4-28, Table 4-2, from the image)

| Type | For a field | Character, formatted screen | Character, unformatted screen |
|---|---|---|---|
| Field attribute | unprotected, A/N, display/not detectable, no intensify, MDT off | NA | NA |
| Colour | per Color query reply | field's colour | per Color query reply |
| Extended highlight | per Highlight query reply | field's highlight | per Highlight query reply |
| Character set | set X'00' | field's set | set X'00' |
| Field validation | none | NA | NA |
| Field outlining | no line | NA | NA |
| Transparency | per Field Outlining query reply (sic, as printed) | field's background transparency | per Background Transparency query reply |

---

## 6. Buffer addresses (GA23-0059-4 p. 4-4, Appendix D p. D-1, D-2)

### 6.1 The three forms

In 12/14-bit mode (implicit partition 0 is **always** 12/14) the top two bits of the **first**
address byte are flags (p. 4-4, from the image; also p. D-1):

| Bits 0-1 of byte 1 | Meaning |
|---|---|
| B'00' | 14-bit binary: low 6 bits of byte 1 then all 8 bits of byte 2 |
| B'01' | 12-bit coded: low 6 bits of each byte joined |
| B'10' | Reserved - **the data stream is rejected** |
| B'11' | 12-bit coded |

- 12-bit example: address 160 = binary 000010 100000 -> bits 2-7 of byte 1 = 000010, of byte 2 =
  100000 (p. 4-4, D-1).
- 14-bit example: 800 = X'320' -> `xx000011 00100000` (p. D-1).
- 16-bit mode (only when set by Create Partition): all 16 bits are the address. Example 3100 =
  X'0C1C' (p. D-1). "If no partitions are defined, 12- and 14-bit addressing is assumed" (p. D-1).
- For 12-bit coded addresses outbound, the Usable Area page adds: "The second byte will have the
  first 2 bits as B'01' or B'11'. Otherwise, the data stream will be rejected" (p. 6-88).
- Displays that only do 12-bit ignore bits 0-1 of both bytes outbound (p. D-1).

### 6.2 Which form to send inbound

- Partition larger than 4096 characters: all inbound addresses in 14-bit form (p. 4-4, p. 6-88).
- Otherwise 12-bit form, using the Figure D-1 code table (p. D-1). Page 4-4 says "less than 4096";
  page 6-88 says "equal to or less than 4096". See Open questions.
- 16-bit partition: always 16-bit inbound (p. 4-4).

Oracles:

| | Decode reserved B'10' | Encode 12 vs 14 bit |
|---|---|---|
| Manual | reject | 12-bit if size <= 4096 (p. 6-88) / < 4096 (p. 4-4) |
| x3270 `ctlr.c` | does **not** reject: any first byte whose top two bits are not 00 is decoded as 12-bit | 14-bit if rows x cols > 0x1000, else 12-bit (agrees with p. 6-88) |
| tnz `tnz.py` | B'10' -> error "reserved address mode" (agrees with manual) | 12-bit if buffer size <= 4095, else 14-bit (agrees with p. 4-4) |

### 6.3 The 6-bit code table (GA23-0059-4 p. D-2, Figure D-1, from the image)

Used for 12-bit addresses inbound, for field attribute bits 0-1 and WCC bits 0-1. Row = value of
bits 2-7 (0-63); the byte sent is the EBCDIC (or ASCII) column.

| 6-bit | EBCDIC | ASCII | 6-bit | EBCDIC | ASCII | 6-bit | EBCDIC | ASCII | 6-bit | EBCDIC | ASCII |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 00 | 40 | 20 | 16 | 50 | 26 | 32 | 60 | 2D | 48 | F0 | 30 |
| 01 | C1 | 41 | 17 | D1 | 4A | 33 | 61 | 2F | 49 | F1 | 31 |
| 02 | C2 | 42 | 18 | D2 | 4B | 34 | E2 | 53 | 50 | F2 | 32 |
| 03 | C3 | 43 | 19 | D3 | 4C | 35 | E3 | 54 | 51 | F3 | 33 |
| 04 | C4 | 44 | 20 | D4 | 4D | 36 | E4 | 55 | 52 | F4 | 34 |
| 05 | C5 | 45 | 21 | D5 | 4E | 37 | E5 | 56 | 53 | F5 | 35 |
| 06 | C6 | 46 | 22 | D6 | 4F | 38 | E6 | 57 | 54 | F6 | 36 |
| 07 | C7 | 47 | 23 | D7 | 50 | 39 | E7 | 58 | 55 | F7 | 37 |
| 08 | C8 | 48 | 24 | D8 | 51 | 40 | E8 | 59 | 56 | F8 | 38 |
| 09 | C9 | 49 | 25 | D9 | 52 | 41 | E9 | 5A | 57 | F9 | 39 |
| 10 | 4A | 5B | 26 | 5A | 5D | 42 | 6A | 7C | 58 | 7A | 3A |
| 11 | 4B | 2E | 27 | 5B | 24 | 43 | 6B | 2C | 59 | 7B | 23 |
| 12 | 4C | 3C | 28 | 5C | 2A | 44 | 6C | 25 | 60 | 7C | 40 |
| 13 | 4D | 28 | 29 | 5D | 29 | 45 | 6D | 5F | 61 | 7D | 27 |
| 14 | 4E | 2B | 30 | 5E | 3B | 46 | 6E | 3E | 62 | 7E | 3D |
| 15 | 4F | 21 | 31 | 5F | 5E | 47 | 6F | 3F | 63 | 7F | 22 |

(6-bit values in decimal; EBCDIC and ASCII in hex.)

---

## 7. Base colour - four colours from two attribute bits

GA23-0059-4 says only that "Base color (four colors) can be produced ... by use of combinations of
the field intensify and field protection attribute bits" and points to GA33-3056 (p. 1-7). The
table itself is in GA23-0061-2 p. 2-8, Figure 2-6 (from the image):

| Field | Bit 2 (protect) | Bits 4-5 | Switch "monochrome" | Switch "base colour" |
|---|---|---|---|---|
| Unprotected, normal | 0 | 0x (00 or 01) | green | **green** |
| Unprotected, intensified | 0 | 10 | white | **red** |
| Protected, normal | 1 | 0x | green | **blue** |
| Protected, intensified | 1 | 10 | white | **white** |

(Nondisplay, bits 4-5 = 11, shows nothing.) Bit 3 (numeric) does not matter.

When base colour is turned off (GA23-0061-2 p. 2-10): on a formatted screen where a character and
its field both have colour X'00', base colours are suppressed if colour type X'42' has been used in
the data stream after an EW/EWA or a Set Reply Mode. While suppressed, the default is white for
intensified fields and green for all others. Base colour comes back on an EW or EWA with WCC reset
bit 1 = 1, or on the Clear, SysReq or Test key. Default colour on an unformatted screen is always
green. X'F7' shows white except with a triple-plane symbol set.

The emulator therefore keeps one flag: "extended colour seen since the last EW/EWA" (my wording,
from the rule above).

---

## 8. Inbound: AIDs, reads and replies

### 8.1 AID values (GA23-0059-4 p. 3-9, Table 3-4, from the image)

| Key / event | EBCDIC | ASCII | | Key / event | EBCDIC | ASCII |
|---|---|---|---|---|---|---|
| No AID generated | X'60' | X'2D' | | PF13 | X'C1' | X'41' |
| No AID (printer only) | X'E8' | X'59' | | PF14 | X'C2' | X'42' |
| Structured field | X'88' | - | | PF15 | X'C3' | X'43' |
| Read partition | X'61' (never first byte) | - | | PF16 | X'C4' | X'44' |
| Trigger action | X'7F' | - | | PF17 | X'C5' | X'45' |
| Test Req and Sys Req | X'F0' | X'30' | | PF18 | X'C6' | X'46' |
| PF1 | X'F1' | X'31' | | PF19 | X'C7' | X'47' |
| PF2 | X'F2' | X'32' | | PF20 | X'C8' | X'48' |
| PF3 | X'F3' | X'33' | | PF21 | X'C9' | X'49' |
| PF4 | X'F4' | X'34' | | PF22 | X'4A' | X'5B' |
| PF5 | X'F5' | X'35' | | PF23 | X'4B' | X'2E' |
| PF6 | X'F6' | X'36' | | PF24 | X'4C' | X'3C' |
| PF7 | X'F7' | X'37' | | PA1 | X'6C' | X'25' |
| PF8 | X'F8' | X'38' | | PA2 (Cncl) | X'6E' | X'3E' |
| PF9 | X'F9' | X'39' | | PA3 | X'6B' | X'2C' |
| PF10 | X'7A' | X'3A' | | CLEAR | X'6D' | X'5F' |
| PF11 | X'7B' | X'23' | | CLEAR PARTITION | X'6A' | - |
| PF12 | X'7C' | X'40' | | ENTER | X'7D' | X'27' |
| | | | | Selector-pen attention | X'7E' | X'3D' |
| | | | | Magnetic reader (operator ID) | X'E6' | X'57' |
| | | | | Magnetic reader number | X'E7' | X'58' |

The OCR text of this table had the ASCII column shifted by one row; the values above come from the
image.

x3270 sends Sys Req (X'F0') in an SSCP-LU session as the four bytes SOH % / STX instead (`ctlr.c`);
that is a session-level behaviour the manual table does not describe here. **NOT VERIFIED** against
a manual.

### 8.2 Read heading and the normal reply (p. 3-10)

`AID  cursor-address(2)  data...`

The first three bytes are the "read heading". When structured fields go inbound the stream is
`X'88'  structured-field ...` instead.

### 8.3 Read Modified (RM) and the short read (p. 3-12, 3-13)

**Short read:** if the AID is **CLEAR, CNCL (PA2) or a PA key**, only the **AID byte** is sent - no
cursor address, no data (p. 3-13). The manual names CLEAR, CNCL, PA keys. Clear Partition is not
named for the short read.

**Selector-pen attention** on a space or null designator: for each field with MDT set, only the
heading, SBA and attribute address + 1 are sent, no data (p. 3-12).

**Otherwise (Enter, PF keys, ...):**

- Formatted buffer: heading, then for each field with MDT = 1, in buffer order from 0 to the end:
  `SBA  (address of attribute + 1)  data`. **Nulls are left out** (null suppression). Field and
  Extended Field modes have the same layout (p. 3-13). In Character mode SA orders are added when a
  listed attribute changes.
- Formatted but nothing modified: the 3-byte heading only (p. 3-12).
- Unformatted buffer (no fields): heading then all data from address 0 to the end, nulls left out,
  no SBA. The buffer address is then set to 0 (p. 3-13).

Oracles on which AIDs short-read:

| Source | Short read (AID only) |
|---|---|
| Manual p. 3-13 | CLEAR, CNCL (PA2), PA keys |
| x3270 `ctlr.c` | PA1, PA2, PA3, CLEAR. Selector pen: no field data. RMA sends everything. Agrees. |
| tnz `tnz.py` | any AID from X'6B' to X'6F' (PA3, CLEAR, PA1, PA2, and X'6F' which is not an AID). Agrees for the defined keys. |

### 8.4 Read Modified All (RMA) (p. 3-13, 3-14)

Like RM but **always** sends addresses and data of all modified fields whatever the AID - also for
short-read AIDs, trigger (X'7F'), Clear Partition (X'6A') and Read Partition (X'61').

### 8.5 Read Buffer (RB) (p. 3-11, 3-12)

Sends the whole buffer from address 0 to the end. Nulls are **not** suppressed (X'00' is read back,
p. 4-10).

| Reply mode | Layout |
|---|---|
| Field | `AID cursor(2)` then, at each field attribute, `SF attr`, and the data |
| Extended field | `SFE n (type value)...` instead of SF. Types at default value are not sent. No character attributes. |
| Character | as Extended field, plus SA orders where a listed attribute changes |

### 8.6 Reply modes (Set Reply Mode, p. 3-11, 5-61, 5-62)

| Mode | Value | What may appear inbound |
|---|---|---|
| Field (default) | X'00' | SBA, SF, characters, field attributes, GE |
| Extended field | X'01' | SBA, SF, SFE, characters, field and extended field attributes, GE |
| Character | X'02' | SBA, SF, SFE, SA, characters, all three attribute kinds (only the listed types), GE |

EW/EWA with WCC reset sets the mode back to Field (p. 3-6).

### 8.7 Inbound 3270DS, X'80' (p. 6-7)

Used only for partitions with non-zero PID. Partition 0 data is sent as plain 3270 data, not in
this field.

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'80' |
| 3 | PID |
| 4 | AID |
| 5-6 | Cursor |
| 7-n | Orders and data |

---

## 9. Read Partition Query and the query replies

### 9.1 Read Partition, X'01' (p. 5-47, 5-48, from the image)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'01' |
| 3 | PID: X'00'-X'7E' for reads, **X'FF' for a query** |
| 4 | Type: X'02' Query, X'03' Query List, X'6E' RMA, X'F2' RB, X'F6' RM |
| 5 | Only for Query List. Bits 0-1 REQTYP: B'00' QCODE list, B'01' Equivalent + list, B'10' All, B'11' reserved. Bits 2-7 reserved. |
| 6-n | Only for Query List: QCODEs requested |

Rules (p. 5-48, 6-13, 6-14):

- Query is rejected if PID is not X'FF'.
- Read Partition must be the last structured field in the chain.
- QCODE list: send each supported reply once (no duplicates). None supported -> send the **Null**
  reply. B'00' with no list -> Null reply.
- Equivalent: send what a Query would send, plus any listed ones, no duplicates.
- All: send every supported reply; any list is ignored.
- In Retry state, Read Partition is rejected (sense X'0871') (p. 3-19, 5-48).

**The reply:** `X'88'` then the query replies one after another. Only the first is preceded by
X'88' (p. 6-17). Each query reply is:

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | QCODE |
| 4-n | parameters |

Summary (X'80') must always be sent for Query and for every Query List form (p. 6-14).

### 9.2 Which replies go back for a plain Query (p. 6-15, 6-16, from the image)

Replies marked "Query: Yes" are sent on a Query **if the device supports the function**. The
replies a modern display emulator is expected to support are in bold; "required for SAA" is from the
page of each reply.

| Reply | QCODE | On Query | Equivalent | All |
|---|---|---|---|---|
| **Summary** (SAA) | X'80' | Yes | Yes | Yes |
| **Usable Area** (SAA) | X'81' | Yes | Yes | Yes |
| Text Partitions | X'83' | Yes | Yes | Yes |
| **Alphanumeric Partitions** | X'84' | Yes | Yes | Yes |
| **Character Sets** (SAA) | X'85' | Yes | Yes | Yes |
| **Color** | X'86' | Yes | Yes | Yes |
| **Highlighting** | X'87' | Yes | Yes | Yes |
| **Reply Modes** | X'88' | Yes | Yes | Yes |
| Field Validation | X'8A' | Yes | Yes | Yes |
| MSR Control | X'8B' | Yes | Yes | Yes |
| Field Outlining | X'8C' | Yes | Yes | Yes |
| Partition Characteristics | X'8E' | No | No | Yes |
| OEM AUX Devices | X'8F' | No | No | Yes |
| Format Presentation | X'90' | Yes | Yes | Yes |
| DBCS Asia | X'91' | Yes | Yes | Yes |
| Save/Restore Format | X'92' | No | No | Yes |
| DDM | X'95' | Yes | Yes | Yes |
| Storage Pools | X'96' | No | No | Yes |
| DIA | X'97' | Yes | Yes | Yes |
| Data Chaining | X'98' | Yes | Yes | Yes |
| Auxiliary Devices | X'99' | Yes | Yes | Yes |
| 3270IPDS | X'9A' | No | Yes | Yes |
| Product Defined Data Stream | X'9C' | No | No | Yes |
| Begin/End of File | X'9F' | Yes | Yes | Yes |
| Device Characteristics | X'A0' | Yes | Yes | Yes |
| **RPQ Names** | X'A1' | Yes | Yes | Yes |
| Data Streams | X'A2' | Yes | Yes | Yes |
| **Implicit Partition** (SAA) | X'A6' | Yes | Yes | Yes |
| Paper Feed Techniques | X'A7' | Yes | Yes | Yes |
| Transparency | X'A8' | No | No | Yes |
| Settable Printer Characteristics | X'A9' | No | No | Yes |
| IOCA Auxiliary Device | X'AA' | No | No | Yes |
| Cooperative Proc. Requestor | X'AB' | Yes | Yes | Yes |
| Segment | X'B0' | No | No | Yes |
| Procedure | X'B1' | No | No | Yes |
| Line Type | X'B2' | No | No | Yes |
| Port | X'B3' | No | No | Yes |
| Graphic Color | X'B4' | No | No | Yes |
| Extended Drawing Routine | X'B5' | No | No | Yes |
| Graphic Symbol Sets | X'B6' | No | No | Yes |
| Null | X'FF' | No | No | No |

What the oracles actually send:

| Oracle | Replies |
|---|---|
| x3270 `sf.c` | Summary, Usable Area, Alphanumeric Partitions, Character Sets, Color, Highlighting, Reply Modes, DBCS-Asia (only in DBCS mode), DDM, RPQ Names, Implicit Partition, Null |
| tnz `tnz.py` | Summary, Usable Area, Character Sets, Color (if colour capable), Highlight, Reply Modes, DDM, Implicit Partition |

### 9.3 Summary, X'80' (p. 6-81)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'80' |
| 4-n | QCODEs of every supported reply, including X'80' itself, **excluding Null** |

### 9.4 Usable Area, X'81' (p. 6-85 to 6-89, table from the image)

Always sent on Query and every Query List form.

| Byte | Bit | Content |
|---|---|---|
| 0-1 | | Length |
| 2 | | X'81' |
| 3 | | X'81' |
| 4 | 0 | Reserved |
| | 1 | 0 = non-page printer, 1 = page printer |
| | 2 | Reserved (a few early products set 1 to mean "no Outbound 3270DS / Erase/Reset") |
| | 3 | HC: 1 = hard copy device |
| | 4-7 | ADDR: X'0' reserved, **X'1' 12/14-bit**, X'2' reserved, **X'3' 12/14/16-bit**, X'F' unmapped |
| 5 | 0 | VCP: variable cells (0 unless Create Partition is supported) |
| | 1 | CHAR: 0 = matrix characters, 1 = non-matrix (then bytes 10-20 are zero) |
| | 2 | CELLUNITS: 0 = W/H in cells, 1 = in pels |
| | 3-7 | 0 |
| 6-7 | | **W** - width of usable area |
| 8-9 | | **H** - height of usable area |
| 10 | | UNITS: X'00' inches, X'01' millimetres |
| 11-14 | | Xr: X distance between pels, 2-byte numerator, 2-byte denominator |
| 15-18 | | Yr: same for Y |
| 19 | | AW: X units in the default cell |
| 20 | | AH: Y units in the default cell |
| 21-22 | | BUFFSZ: character buffer size in bytes |
| 23-26 | | XMIN, YMIN, XMAX, YMAX (only with VCP) |

Length rules (p. 6-87): bytes 0-20 always; 21-26 must be present if any self-defining parameter
follows; may be left off when there are none; BUFFSZ may be left off when not applicable.
Xr/Yr unknown -> X'FFFFFFFF' (not allowed if Load PS is supported) (p. 6-89).

**How large or odd screen sizes are reported:** W and H are the usable area of the display surface
(p. 6-89); W x H is the most characters a nonloadable character set can show. With CELLUNITS = 0
they are in character cells, so a 160 x 62 screen is W = X'00A0', H = X'003E'. Nothing in the
manual ties W/H to the 24x80 / 32x80 / 43x80 / 27x132 model sizes. The screen sizes the host
switches between are reported separately, in the Implicit Partition reply (9.7). BUFFSZ is 2 bytes,
so it cannot hold more than 65 535 (my reading of the field width; the manual says nothing about a
larger buffer). Addresses above 16 383 need 16-bit mode (ADDR = X'3' plus Create Partition).

Oracles:

| | ADDR | W x H | UNITS / Xr / Yr | AW x AH | BUFFSZ |
|---|---|---|---|---|---|
| x3270 `sf.c` | X'1' (12/14 only) | max cols x max rows | mm, canned 3279-2 values | canned 3279-2 values | cols x rows (16 bits, no overflow check) |
| tnz `tnz.py` | X'1' | alternate cols x rows | inches, 1/96 and 1/96 | 6 x 12 | left off (21-byte base) |

### 9.5 Color, X'86' (p. 6-29, 6-30, from the images)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'86' |
| 4 | Flags. Bit 1: printer only, black ribbon loaded. Others reserved. |
| 5 | NP: number of CAV/CI pairs |
| n, n+1 | CAV (attribute value accepted), CI (colour shown for it) - repeated NP times |
| then | optional background subfield: L, X'02', CAVDEF = X'00', CIDEF (default background colour) |

Rules: the first pair **must** be CAV = X'00' with CI = the device default colour (p. 6-29). The
pair for X'F7' gives the colour used for single-plane neutral. A device shows the colour CI or its
default colour. The background subfield is present only if background colour is supported, and
then every foreground value is also allowed as background (p. 6-30).

Example given by IBM for a colour display (p. 6-30): pairs 00/F4, F1/F1, F2/F2, F3/F3, F4/F4,
F5/F5, F6/F6, F7/F7. For a mono display: 00/F4, then every other value mapped to 00.

x3270 `sf.c` sends 00/F4 (green), then F1 ... FF mapped to themselves (colour mode) or to 00, NP =
16; no background subfield. tnz: not read in detail (**NOT VERIFIED**).

### 9.6 Highlight, X'87' (p. 6-56)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'87' |
| 4 | NP: number of value/action pairs |
| n, n+1 | Vi (value accepted), Ai (what the device does) |

Valid values: X'00' default, X'F0' normal, X'F1' blink, X'F2' reverse, X'F4' underscore, X'F8'
intensify. An action of X'00' means "same as default". Highlighting is exclusive: one value per
field or character.

### 9.7 Implicit Partition, X'A6' (p. 6-59 to 6-61, from the images)

**This is where the default and alternate screen sizes go.**

Base:

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'A6' |
| 4-5 | Flags X'0000' (reserved) |

Display sizes self-defining parameter (required for displays):

| Byte | Content |
|---|---|
| 0 | X'0B' (length) |
| 1 | X'01' |
| 2 | X'00' |
| 3-4 | WD - default width (cells) |
| 5-6 | HD - default height |
| 7-8 | WA - alternate width |
| 9-10 | HA - alternate height |

Rules: all four non-zero; no alternate size -> alternate = default. In SNA the sizes are the ones
set at BIND; in non-SNA, the ones in effect when the reply is made (p. 6-60). Printers use X'03'
with 4-byte buffer sizes instead (p. 6-61). An optional character-cell-size parameter is needed only
when the cell size differs from Usable Area AW/AH and Load PS is supported (p. 6-61).

Oracles:

| | WD x HD | WA x HA |
|---|---|---|
| x3270 `sf.c` | fixed 80 x 24 | max cols x max rows (its comments name them the other way round; the bytes written are cols, then rows) |
| tnz `tnz.py` | default cols x rows | alternate cols x rows |

### 9.8 Reply Modes, X'88' (p. 6-74)

`L  X'81'  X'88'  modes...` - one byte per supported mode: X'00' Field, X'01' Extended field,
X'02' Character.

### 9.9 Character Sets, X'85' (p. 6-21, 6-22, from the images)

Required for SAA. Base:

| Byte | Bit | Content |
|---|---|---|
| 0-1 | | Length |
| 2 | | X'81' |
| 3 | | X'85' |
| 4 | 0 | ALT: GE supported |
| | 1 | MULTID: multiple LCIDs |
| | 2 | LOADABLE: Load PS supported |
| | 3 | EXT: Load PS extended |
| | 4 | MS: more than one slot size |
| | 5 | CH2: 2-byte sets |
| | 6 | GF: CGCSGID present - **must be 1** |
| | 7 | Reserved |
| 5 | 1 | PSCS (other bits reserved) |
| 6 | | SDW - default slot width |
| 7 | | SDH - default slot height |
| 8-11 | | FORM - Load PS formats supported (bit coded) |
| 12 | | DL - length of each descriptor |
| 13-n | | descriptors (at least the first 3 bytes of one must be present) |

The descriptor layout is on p. 6-24 onward; **NOT VERIFIED** here - not read.

### 9.10 RPQ Names, X'A1' (p. 6-75)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'A1' |
| 4-7 | Device type |
| 8-11 | Model (X'00000000' = all) |
| 12 | RPQL - length of name **including this byte** |
| 13-n | RPQ name |

### 9.11 Alphanumeric Partitions, X'84' (p. 6-18, 6-19)

| Byte | Content |
|---|---|
| 0-1 | Length |
| 2 | X'81' |
| 3 | X'84' |
| 4 | NA - max number of partitions (0 = only partition 0, only via 3270 commands) |
| 5-6 | M - total partition storage (X'FFFF' reserved) |
| 7 | Flags: bit 0 vertical scroll, bit 3 all-points addressing, bit 4 partition protection, bit 5 local copy, bit 6 Modify Partition |
| 8- | Buffer allocation self-defining parameter (CM, RO, CO, FO), needed unless CM = 1 and the rest 0 |

### 9.12 Null, X'FF' (p. 6-67)

`X'0004'  X'81'  X'FF'`. Sent only when a Query List asked for nothing the device supports. Never
listed in Summary (p. 6-81).

---

## 10. Keyboard lock and unlock

- When an AID is set, input is inhibited until a write with WCC keyboard restore, or an EAU
  (p. 3-8).
- WCC keyboard restore (bit 6) unlocks the keyboard and resets the AID (p. 3-3).
- EAU unlocks the keyboard and resets the AID (p. 3-7).
- An enter action sets "enter inhibit" (no further AID can be sent) and, unless it is a trigger
  action, TWAIT (shown "X Clock") and System Lock (shown "X System") (p. 3-15, 3-20, 3-21).
- TWAIT cannot be cleared by the operator's Reset key; System Lock can (p. 3-20, 3-21).
- A host acknowledgment that leaves the terminal in send/contention clears System Lock, except a
  bare SNA Change Direction, which clears TWAIT but leaves System Lock on so the operator must press
  Reset (p. 3-21).
- Mandatory-fill, mandatory-entry and negative trigger replies raise input-inhibit conditions that
  only Reset clears (p. 4-21, 4-22, 4-24).
- x3270 `sf.c` locks the keyboard after sending query replies (`kybd_inhibit(true)` at the end of a
  query reply). The manual says a Query is acknowledged by any outbound transmission except a read
  (p. 3-23); it does not say "lock". See Open questions.

Read acknowledgment for an enter on partition 0 (p. 3-23): an EW, EWA or W with keyboard restore,
or an EAU.

---

## 11. MDT rules

The MDT is bit 7 (X'01') of the field attribute.

| Event | MDT | Source |
|---|---|---|
| Operator changes an unprotected field (incl. Erase EOF) | set to 1 | p. 4-13, 4-14 |
| Host writes a field attribute with bit 7 = 1 | 1 (field sent on the next RM even if not typed in) | p. 4-13, 4-22 |
| WCC bit 7 (reset MDT) | all MDT bits cleared before the write | p. 3-3 |
| EAU | MDT cleared in every unprotected field | p. 3-7 |
| Erase Input key | MDT cleared in every unprotected field | p. 4-14 |
| Clear key | all field attributes become nulls (so no fields) | p. 4-13 |
| Selector pen select / deselect | sets / resets MDT | p. 4-22, 4-24 |
| Trigger action | MDT not changed | p. 4-25 |

Read Modified and Read Modified All send exactly the fields with MDT = 1 (p. 3-10, 4-4).

---

## 12. Read states (SNA)

For an emulator without partitions this is short, but the rules are here (p. 3-17 to 3-19):

- Normal read: enter action -> Retry Enter; a read command as the first byte -> read, stay Normal;
  Read Partition -> Retry Read.
- Retry Enter / Retry Read: a Read Modified command **repeats** the last inbound operation; Read
  Partition is rejected (X'0871').
- There is no retry state for a read command sent as the first byte in Normal state.

---

## Open questions

1. **RMA channel code.** GA23-0061-2 Figure 1-8 lists no local channel code for Read Modified All;
   x3270 uses X'0E'. Not found in either manual. For TN3270, which uses X'6E', this does not matter.
2. **12-bit vs 14-bit at exactly 4096 cells.** GA23-0059-4 p. 4-4 says 12-bit for "less than 4096",
   p. 6-88 says "equal to or less than 4096". x3270 follows p. 6-88 (12-bit at 4096); tnz follows
   p. 4-4 (14-bit at 4096). 12-bit can hold 0-4095, so a 4096-cell buffer fits either way.
3. **Reserved address flag B'10'.** Manual: reject. tnz: rejects. x3270: decodes it as a 12-bit
   address and carries on.
4. **Highlight X'F8' (intensify).** Listed as valid in the Highlight query reply (p. 6-56), not in
   the chapter 4 value list (p. 4-18).
5. **Colours above X'F7'.** Neither manual defines X'F8'-X'FF' (the 3290 column on p. 6-30 shows
   X'F8' as a CI value only). x3270 offers X'F1'-X'FF' in its Color reply. What a host may send
   there and what to show is not in the sources I read.
6. **PID range in Set Reply Mode.** The table says X'00'-X'7F' (p. 5-61); every other structured
   field says X'00'-X'7E'. Possibly a misprint; not settled.
7. **Keyboard after a query reply.** x3270 locks the keyboard after sending query replies. The
   manual only describes the retry state and acknowledgment; it does not say the keyboard locks.
   What a real 3274/3279 does is not settled.
8. **SFE without an X'C0' pair.** The manual does not say what field attribute is stored when SFE
   gives no X'C0' pair. Oracle code for this was not read.
9. **Very large screens.** Usable Area BUFFSZ is 2 bytes; the manual does not say what to report
   when rows x cols exceeds 65 535, nor whether a host can use more than 16 383 positions without
   Create Partition (16-bit mode is set only by Create Partition, p. 4-4, D-1).
10. **Default (WD x HD) size.** x3270 always reports 80 x 24 as the default; tnz reports its
    configured default. The manual only says the values must be non-zero and equal to the
    alternate when there is no alternate (p. 6-60).
11. **Sys Req in an SSCP-LU session.** x3270 sends SOH % / STX instead of AID X'F0'. Not checked
    against a manual.
12. **Not read in this pass:** Character Sets descriptor layout (p. 6-24 on), Create Partition
    (16-bit mode), Field Validation and Field Outlining query reply layouts (p. 6-49, 6-52),
    Transparency query reply, Usable Area self-defining parameters (p. 6-90 to 6-94), the Reset
    Actions table (Appendix C), and tnz's Color reply.
13. **z/OS Basic Skills page.** Only a summary of the page was obtained through the fetch tool,
    not the full text, and its sibling pages were not read.
