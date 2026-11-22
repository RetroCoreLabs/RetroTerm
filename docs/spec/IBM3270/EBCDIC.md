# EBCDIC for the IBM 3270 emulation

What the bytes in a 3270 screen buffer mean, how each one is drawn, and which
Unicode character RetroTerm should turn it into. Written for the coming IBM 3270
emulation in C#. Nothing here is implemented yet.

## How to read this file

Every fact names where it came from:

- **GA27-2837-9** - IBM *3270 Information Display System Character Set Reference*,
  April 1987, in `docs/spec/IBM3270/IBM-manuals/`. Page numbers are the printed
  page; the PDF page is given as "PDF p. N". The code tables in this PDF are
  scanned pictures and its text layer is unreadable, so every table value below
  that is credited to GA27-2837-9 was **read off a picture of the page** (rendered
  at 150 and 300 dots per inch), not off the text layer.
- **GA23-0059-4** - IBM *3270 Data Stream Programmer's Reference*, December 1988,
  same folder.
- **IBM registry** - IBM's own code page files `CP00037.txt`, `CP00277.txt`,
  `CP00310.txt` from
  <https://public.dhe.ibm.com/software/globalization/gcoc/attachments/>
  (fetched 22 November 2026). These give each byte an IBM character id and name,
  but **no Unicode value**.
- **ICU** - the ICU mapping tables (byte to Unicode, made by IBM), from
  <https://github.com/unicode-org/icu-data/tree/main/charset/data/ucm>, files
  `ibm-37_P100-1999.ucm`, `ibm-277_P100-1999.ucm`, `ibm-273_P100-1999.ucm`,
  `ibm-278_P100-1999.ucm`, `ibm-285_P100-1999.ucm`, `ibm-297_P100-1999.ucm`,
  `ibm-500_P100-1999.ucm`, `ibm-1047_P100-1995.ucm`, `ibm-1140_P100-1997.ucm`
  to `ibm-1149_P100-1997.ucm` (fetched 22 November 2026).
- **unicode.org** - <https://www.unicode.org/Public/MAPPINGS/VENDORS/MICSFT/EBCDIC/CP037.TXT>.
- **Python** - the `cp037`, `cp273`, `cp500`, `cp1140` codecs built into Python 3.11.
- **x3270** - the x3270 source (oracle only, no code copied): `Common/unicode.c`
  (code page tables, the APL table `apl2uc[]`, aliases), `Common/apl.c`,
  `Common/sf.c` (query reply), `Common/ctlr.c` (GE order), `Common/codepage.c`.
- **tnz** - IBM's tnz source (oracle only): `tnz/tnz.py`, `tnz/cp310.py`,
  `tnz/ati.py`, `tnz/zti.py`, `README.md`.

"NOT VERIFIED" means nobody here read it in a source; it is a guess or a memory
and must be checked before code relies on it.

The tables in this file were built by a script that read the ICU files, the x3270
tables, the tnz table and the IBM registry and compared them byte by byte. The
script is not kept; every difference it found is listed in
[Every disagreement](#every-disagreement-between-the-sources).

---

## What EBCDIC is

EBCDIC is IBM's 8-bit character code for its mainframes. It is not ASCII and not
in the same order: the letter A is X'C1', not X'41'; the digits are X'F0' to
X'F9'; the space is X'40'. The letters are not in one run either: A-I are
X'C1'-X'C9', J-R are X'D1'-X'D9', S-Z are X'E2'-X'E9' (GA27-2837-9 p. 10-13,
fig. 10-12, PDF p. 256; same in every national table).

There is no single EBCDIC. Each country has its own **code page**: the same byte
can be a different character in Norway and in the US. The letters, digits and
most punctuation are the same everywhere; a small set of positions changes.
GA27-2837-9 calls those the "national use" positions and lists 14 of them:
X'4A', X'5A', X'6A', X'79', X'5B', X'7B', X'7C', X'5F', X'A1', X'C0', X'D0',
X'E0', X'4F', X'7F' (fig. 10-43 part 1 and 2, pp. 10-44 and 10-45, PDF pp. 289-290).
The later 190-character "country extended code pages" (CECP) move more than
these 14 (see the 277 table below: 22 bytes differ from 037).

A 3270 screen holds one byte per character cell. Bytes X'00' to X'3F' and X'FF'
are not characters: they are orders (instructions in the write data) or
controls stored in the buffer. Bytes X'40' to X'FE' are characters
(GA23-0059-4 p. 4-9 and 4-10, PDF pp. 74-75).

## Which code pages the 3270 manual defines

**GA27-2837-9 does not use code page numbers at all.** It has no "037", "277",
"CECP" or "CCSID" anywhere: it names each table by language ("Danish/Norwegian
I/O Interface Code"). Its text layer was searched for "CECP", "code page" and
"CGCS" with no hit, and the chapter 10 tables were looked at as pictures; the
numbers are not on them. The link from language name to number below comes from
the IBM registry, ICU and x3270, not from the manual.

The manual's tables (1987) show only the base set, about 94 characters per
language, not the 190 of a CECP. Chapter 10 (3174, 3274, 3276 attached 3178,
3179, 3180, 3278, 3279, 3290 and printers, p. 10-1, PDF p. 244) has the tables
that matter for a 3278/3279 emulation:

| Language in GA27-2837-9 | Figure, page (PDF page) | Code page number | Number from |
|---|---|---|---|
| English (U.S.) | 10-12, p. 10-13 (256) | 037 | IBM registry `CP00037.txt`, x3270 alias `us` |
| Danish/Norwegian | 10-9, p. 10-10 (253) | 277 | IBM registry `CP00277.txt` "Denmark, Norway - CECP"; x3270 alias `norwegian` |
| Danish/Norwegian (Alternate) | 10-10, p. 10-11 (254) | none found | - |
| Finnish/Swedish | 10-16 (not rendered) | 278 | x3270 aliases `finnish`, `swedish` |
| Austrian/German | 10-2 (not rendered) | 273 | x3270 alias `german` |
| English (U.K.) | 10-11 (not rendered) | 285 | x3270 alias `uk` |
| French | 10-18 (not rendered) | 297 | x3270 alias `french` |
| International | 10-25, p. 10-26 (269) | 500 | x3270 alias `belgian`; ICU |
| APL/Text Feature, 2-byte (GE) | 10-41, p. 10-42 (287) | 310 | IBM registry `CP00310.txt` "Graphic Escape APL/TN" |
| Control characters | 10-1, p. 10-2 (245) | - | - |

The 14 national-use values for 037, 273, 277, 278, 285, 297 and 500 were read off
fig. 10-43 part 2 (p. 10-45, PDF p. 290) and checked against ICU; the result is in
[Every disagreement](#every-disagreement-between-the-sources).

Code pages the manual does not have, and where they come from:

- **1047** (Latin-1 "open systems" page, used by z/OS UNIX). ICU and x3270. It
  differs from 037 in six bytes (ICU):

  | Byte | 037 | 1047 |
  |---|---|---|
  | X'5F' | ¬ U+00AC | ^ U+005E |
  | X'AD' | Ý U+00DD | \[ U+005B |
  | X'B0' | ^ U+005E | ¬ U+00AC |
  | X'BA' | \[ U+005B | Ý U+00DD |
  | X'BB' | \] U+005D | ¨ U+00A8 |
  | X'BD' | ¨ U+00A8 | \] U+005D |

- **1140 to 1149**, the euro pages. Each is its base page with one byte changed to
  € U+20AC (ICU, compared byte by byte, all 256 bytes):

  | Euro page | Base page | Byte that became € | Was |
  |---|---|---|---|
  | 1140 | 037 (US) | X'9F' | ¤ U+00A4 |
  | 1141 | 273 (German) | X'9F' | ¤ U+00A4 |
  | **1142** | **277 (Danish/Norwegian)** | **X'5A'** | ¤ U+00A4 |
  | 1143 | 278 (Finnish/Swedish) | X'5A' | ¤ U+00A4 |
  | 1146 | 285 (UK) | X'9F' | ¤ U+00A4 |
  | 1147 | 297 (French) | X'9F' | ¤ U+00A4 |
  | 1148 | 500 (International) | X'9F' | ¤ U+00A4 |
  | 1144, 1145, 1149 | 280, 284, 871 | not checked (base pages not fetched) | - |

  So the Norwegian euro sits at X'5A', not at X'9F' as in the US page. A
  Norwegian user who picks "277" sees ¤ where the host meant €, and the other
  way round.

  x3270's tables for 1140 to 1149 agree with ICU in every byte X'41' to X'FE'.
  None of 1140-1149 is in GA27-2837-9 (it is from 1987; the euro came in 1999).

- **CGCSGID numbers** (what a terminal reports to the host for its character set,
  4 bytes: 2-byte character set number, then 2-byte code page number,
  GA23-0059-4 p. 6-28, PDF p. 199). x3270 sends character set 697 (X'02B9') plus
  the code page for 037, 273, 277, 278, 285, 297, 500, 1047 (`codepage.c`,
  `DEFAULT_CGEN 0x02b90000`, `set_cgcsgid()`), and character set 695 (X'02B7')
  for 1140 to 1149 (`unicode.c`, e.g. `cp1142` = `0x02b70476`). tnz always sends
  697 for the base set, with a "FIXME how do we determine?" comment (`tnz.py`,
  `encoding` setter). The 695/697 split is the oracles' claim; NOT VERIFIED
  against an IBM document.

---

## Controls in a 3270 buffer and how each is drawn

The 3278/3279 control table is GA27-2837-9 fig. 10-1, p. 10-2 (PDF p. 245). The
older 3277 table is fig. 9-1, p. 9-2 (PDF p. 223) and draws some of them
differently. GA23-0059-4 p. 4-10 (PDF p. 75), "Format Control Orders", says the
same thing for the data stream. What is in the buffer and what each shows:

| Control | Byte | 3278/3279 shows (GA27-2837-9 fig. 10-1 notes 1 and 2) | GA23-0059-4 p. 4-10 shows | 3277 showed (fig. 9-1 note 2) | Read back as |
|---|---|---|---|---|---|
| NUL | X'00' | blank (not in notes; GA23 says blank) | a blank, left out of Read Modified | - | X'00' on Read Buffer, nothing on Read Modified (GA23 p. 4-10) |
| SUB | X'3F' | not stated | **a solid circle** | not stated | X'3F' (GA23: "transmitted as control codes") |
| DUP | X'1C' | **asterisk with a bar over it** in dual-case mode; plain `*` in mono-case mode; printed as `*` | an overscore asterisk | `*` | X'1C' |
| FM | X'1E' | **semicolon with a bar over it** in dual-case mode; plain `;` in mono-case mode; printed as `;` | an overscore semicolon | `;` | X'1E' |
| FF | X'0C' | blank | a blank | `<` | X'0C' (GA23); the 3277 returned X'8C' instead (fig. 9-1 note 3) |
| CR | X'0D' | blank | a blank | - (not in 3277 table) | X'0D' |
| NL | X'15' | blank | a blank | `5` | X'15' |
| EM | X'19' | blank | a blank | `9` | X'19' |
| EO | X'FF' | not stated | a blank | - (not in 3277 table) | X'FF' |
| GE | X'08' | an order, never stored as itself | - | - | sent back as X'08' + byte (see below) |

Other orders in fig. 10-1 are not stored as characters: PT X'05', SBA X'11',
EUA X'12', IC X'13', SF X'1D', SA X'28', SFE X'29', MF X'2C', RA X'3C'.

Fig. 10-1 note 4: the "alternate" national tables (such as Danish/Norwegian
Alternate) do not support GE, SFE, MF, SA, EO or CR. Note 3: on a 3274 with
Configuration Support C, an undefined control X'00'-X'3F' or X'FF' gets a
negative response.

What the oracles do (they do not agree with each other or with GA23 on SUB and EO):

- **x3270** `ebcdic_base_to_unicode()` in `unicode.c`: DUP gives `*` or a private
  character for its own font with the bar; FM gives `;` or a private character;
  **EO gives U+25CF ● solid circle; SUB gives U+25A0 ■ solid square.**
- **tnz** `tnz.py` (`__trans_dc_to_c`, `__trans_ords`): NUL, FF, CR, NL, EM, EO
  become a space. The comment says SUB is a solid circle, DUP an overscore
  asterisk, FM an overscore semicolon; the code maps them to U+2218 ∘ (ring, not
  solid), U+2611 ☑ and U+2612 ☒ with "???" in its own comments.

**No Unicode equal:** "asterisk with bar over" and "semicolon with bar over" have
no single Unicode character. Options are `*` or `;` followed by U+0305 COMBINING
OVERLINE, or drawing the bar ourselves over the glyph. Lucida Console has neither
U+0305 nor U+0332 (see [Lucida Console](#lucida-console-measured)), so drawing
the bar is the one that works with that font.

Lowercase in mono-case mode: lowercase letters are stored as sent but **shown as
uppercase** (GA27-2837-9 fig. 10-12 note 2, and the same note on every national
table).

Undefined character bytes: on the old control units an undefined byte X'40'-X'FE'
was shown as a hyphen and the hyphen X'60' was read back; with 3274 Configuration
Support C, D or T, "an unspecified character" is shown and **the original byte is
read back** (fig. 10-12 note 1, and every national table). GA23-0059-4 p. 4-10
says the same: what is drawn is the implementation's choice, but if "graphic code
point integrity" is supported the byte must come back unchanged. The emulation
must keep the byte, never the Unicode character.

---

## Code page 037 - US/Canada, full table

Source: ICU `ibm-37_P100-1999.ucm`. Each cell is the character and its Unicode
number. SP = space, NBSP = no-break space U+00A0, SHY = soft hyphen U+00AD, EO =
the EO control.

Checked against: unicode.org `CP037.TXT` (all 256 bytes: same); Python `cp037`
(all 256 bytes: same); x3270 `cp037` (X'41'-X'FE': same); GA27-2837-9 fig. 10-12
(p. 10-13, PDF p. 256) for every character it shows (the base set): same; the 14
national-use values of fig. 10-43 for U.S. English: same. Note the manual shows
X'6A' as a broken bar ¦ and X'4F' as a solid bar |, which is what ICU says.

| hi\lo | _0 | _1 | _2 | _3 | _4 | _5 | _6 | _7 | _8 | _9 | _A | _B | _C | _D | _E | _F |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 4_ | SP 0020 | NBSP 00A0 | â 00E2 | ä 00E4 | à 00E0 | á 00E1 | ã 00E3 | å 00E5 | ç 00E7 | ñ 00F1 | ¢ 00A2 | . 002E | \< 003C | ( 0028 | + 002B | \| 007C |
| 5_ | & 0026 | é 00E9 | ê 00EA | ë 00EB | è 00E8 | í 00ED | î 00EE | ï 00EF | ì 00EC | ß 00DF | ! 0021 | $ 0024 | \* 002A | ) 0029 | ; 003B | ¬ 00AC |
| 6_ | - 002D | / 002F | Â 00C2 | Ä 00C4 | À 00C0 | Á 00C1 | Ã 00C3 | Å 00C5 | Ç 00C7 | Ñ 00D1 | ¦ 00A6 | , 002C | % 0025 | \_ 005F | \> 003E | ? 003F |
| 7_ | ø 00F8 | É 00C9 | Ê 00CA | Ë 00CB | È 00C8 | Í 00CD | Î 00CE | Ï 00CF | Ì 00CC | \` 0060 | : 003A | \# 0023 | @ 0040 | ' 0027 | = 003D | " 0022 |
| 8_ | Ø 00D8 | a 0061 | b 0062 | c 0063 | d 0064 | e 0065 | f 0066 | g 0067 | h 0068 | i 0069 | « 00AB | » 00BB | ð 00F0 | ý 00FD | þ 00FE | ± 00B1 |
| 9_ | ° 00B0 | j 006A | k 006B | l 006C | m 006D | n 006E | o 006F | p 0070 | q 0071 | r 0072 | ª 00AA | º 00BA | æ 00E6 | ¸ 00B8 | Æ 00C6 | ¤ 00A4 |
| A_ | µ 00B5 | \~ 007E | s 0073 | t 0074 | u 0075 | v 0076 | w 0077 | x 0078 | y 0079 | z 007A | ¡ 00A1 | ¿ 00BF | Ð 00D0 | Ý 00DD | Þ 00DE | ® 00AE |
| B_ | ^ 005E | £ 00A3 | ¥ 00A5 | · 00B7 | © 00A9 | § 00A7 | ¶ 00B6 | ¼ 00BC | ½ 00BD | ¾ 00BE | \[ 005B | \] 005D | ¯ 00AF | ¨ 00A8 | ´ 00B4 | × 00D7 |
| C_ | { 007B | A 0041 | B 0042 | C 0043 | D 0044 | E 0045 | F 0046 | G 0047 | H 0048 | I 0049 | SHY 00AD | ô 00F4 | ö 00F6 | ò 00F2 | ó 00F3 | õ 00F5 |
| D_ | } 007D | J 004A | K 004B | L 004C | M 004D | N 004E | O 004F | P 0050 | Q 0051 | R 0052 | ¹ 00B9 | û 00FB | ü 00FC | ù 00F9 | ú 00FA | ÿ 00FF |
| E_ | \\ 005C | ÷ 00F7 | S 0053 | T 0054 | U 0055 | V 0056 | W 0057 | X 0058 | Y 0059 | Z 005A | ² 00B2 | Ô 00D4 | Ö 00D6 | Ò 00D2 | Ó 00D3 | Õ 00D5 |
| F_ | 0 0030 | 1 0031 | 2 0032 | 3 0033 | 4 0034 | 5 0035 | 6 0036 | 7 0037 | 8 0038 | 9 0039 | ³ 00B3 | Û 00DB | Ü 00DC | Ù 00D9 | Ú 00DA | EO |

Bytes X'00'-X'3F': controls and orders, see the section above. ICU maps them to
C0/C1 control characters (for example X'15' to U+0085 and X'25' to U+000A); that
is for text files, **not** for a 3270 buffer, where X'15' is NL and is drawn as a
blank.

x3270 has a second US table, **`bracket`** (its default when no code page is
given, `codepage.c`), which is 037 with four bytes moved so that the square
brackets sit where old IBM terminals had them: X'AD' = \[ U+005B, X'BD' = \]
U+005D, X'BA' = Ý U+00DD, X'BB' = ¨ U+00A8 (x3270 `unicode.c`, `bracket` table,
compared to `cp037`). It is not an IBM code page; it reports itself as 037.

## Code page 277 - Denmark/Norway, full table

Source: ICU `ibm-277_P100-1999.ucm`. **Bold** cells differ from 037.

Checked against: x3270 `cp277` (X'41'-X'FE': same in every byte); GA27-2837-9
fig. 10-9 (p. 10-10, PDF p. 253) for every character it shows: same; the 14
national-use values for Danish/Norwegian in fig. 10-43 (p. 10-45): same. Python
has no `cp277` codec (lookup fails), so Python was not a check here.

| hi\lo | _0 | _1 | _2 | _3 | _4 | _5 | _6 | _7 | _8 | _9 | _A | _B | _C | _D | _E | _F |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 4_ | SP 0020 | NBSP 00A0 | â 00E2 | ä 00E4 | à 00E0 | á 00E1 | ã 00E3 | **} 007D** | ç 00E7 | ñ 00F1 | **\# 0023** | . 002E | \< 003C | ( 0028 | + 002B | **! 0021** |
| 5_ | & 0026 | é 00E9 | ê 00EA | ë 00EB | è 00E8 | í 00ED | î 00EE | ï 00EF | ì 00EC | ß 00DF | **¤ 00A4** | **Å 00C5** | \* 002A | ) 0029 | ; 003B | **^ 005E** |
| 6_ | - 002D | / 002F | Â 00C2 | Ä 00C4 | À 00C0 | Á 00C1 | Ã 00C3 | **$ 0024** | Ç 00C7 | Ñ 00D1 | **ø 00F8** | , 002C | % 0025 | \_ 005F | \> 003E | ? 003F |
| 7_ | **¦ 00A6** | É 00C9 | Ê 00CA | Ë 00CB | È 00C8 | Í 00CD | Î 00CE | Ï 00CF | Ì 00CC | \` 0060 | : 003A | **Æ 00C6** | **Ø 00D8** | ' 0027 | = 003D | " 0022 |
| 8_ | **@ 0040** | a 0061 | b 0062 | c 0063 | d 0064 | e 0065 | f 0066 | g 0067 | h 0068 | i 0069 | « 00AB | » 00BB | ð 00F0 | ý 00FD | þ 00FE | ± 00B1 |
| 9_ | ° 00B0 | j 006A | k 006B | l 006C | m 006D | n 006E | o 006F | p 0070 | q 0071 | r 0072 | ª 00AA | º 00BA | **{ 007B** | ¸ 00B8 | **\[ 005B** | **\] 005D** |
| A_ | µ 00B5 | **ü 00FC** | s 0073 | t 0074 | u 0075 | v 0076 | w 0077 | x 0078 | y 0079 | z 007A | ¡ 00A1 | ¿ 00BF | Ð 00D0 | Ý 00DD | Þ 00DE | ® 00AE |
| B_ | **¢ 00A2** | £ 00A3 | ¥ 00A5 | · 00B7 | © 00A9 | § 00A7 | ¶ 00B6 | ¼ 00BC | ½ 00BD | ¾ 00BE | **¬ 00AC** | **\| 007C** | ¯ 00AF | ¨ 00A8 | ´ 00B4 | × 00D7 |
| C_ | **æ 00E6** | A 0041 | B 0042 | C 0043 | D 0044 | E 0045 | F 0046 | G 0047 | H 0048 | I 0049 | SHY 00AD | ô 00F4 | ö 00F6 | ò 00F2 | ó 00F3 | õ 00F5 |
| D_ | **å 00E5** | J 004A | K 004B | L 004C | M 004D | N 004E | O 004F | P 0050 | Q 0051 | R 0052 | ¹ 00B9 | û 00FB | **\~ 007E** | ù 00F9 | ú 00FA | ÿ 00FF |
| E_ | \\ 005C | ÷ 00F7 | S 0053 | T 0054 | U 0055 | V 0056 | W 0057 | X 0058 | Y 0059 | Z 005A | ² 00B2 | Ô 00D4 | Ö 00D6 | Ò 00D2 | Ó 00D3 | Õ 00D5 |
| F_ | 0 0030 | 1 0031 | 2 0032 | 3 0033 | 4 0034 | 5 0035 | 6 0036 | 7 0037 | 8 0038 | 9 0039 | ³ 00B3 | Û 00DB | Ü 00DC | Ù 00D9 | Ú 00DA | EO |

The 22 bytes where 277 differs from 037 (ICU):

| Byte | 037 | 277 | Unicode name of the 277 character |
|---|---|---|---|
| 47 | å U+00E5 | } U+007D | RIGHT CURLY BRACKET |
| 4A | ¢ U+00A2 | \# U+0023 | NUMBER SIGN |
| 4F | \| U+007C | ! U+0021 | EXCLAMATION MARK |
| 5A | ! U+0021 | ¤ U+00A4 | CURRENCY SIGN |
| 5B | $ U+0024 | Å U+00C5 | LATIN CAPITAL LETTER A WITH RING ABOVE |
| 5F | ¬ U+00AC | ^ U+005E | CIRCUMFLEX ACCENT |
| 67 | Å U+00C5 | $ U+0024 | DOLLAR SIGN |
| 6A | ¦ U+00A6 | ø U+00F8 | LATIN SMALL LETTER O WITH STROKE |
| 70 | ø U+00F8 | ¦ U+00A6 | BROKEN BAR |
| 7B | \# U+0023 | Æ U+00C6 | LATIN CAPITAL LETTER AE |
| 7C | @ U+0040 | Ø U+00D8 | LATIN CAPITAL LETTER O WITH STROKE |
| 80 | Ø U+00D8 | @ U+0040 | COMMERCIAL AT |
| 9C | æ U+00E6 | { U+007B | LEFT CURLY BRACKET |
| 9E | Æ U+00C6 | \[ U+005B | LEFT SQUARE BRACKET |
| 9F | ¤ U+00A4 | \] U+005D | RIGHT SQUARE BRACKET |
| A1 | \~ U+007E | ü U+00FC | LATIN SMALL LETTER U WITH DIAERESIS |
| B0 | ^ U+005E | ¢ U+00A2 | CENT SIGN |
| BA | \[ U+005B | ¬ U+00AC | NOT SIGN |
| BB | \] U+005D | \| U+007C | VERTICAL LINE |
| C0 | { U+007B | æ U+00E6 | LATIN SMALL LETTER AE |
| D0 | } U+007D | å U+00E5 | LATIN SMALL LETTER A WITH RING ABOVE |
| DC | ü U+00FC | \~ U+007E | TILDE |

Danish/Norwegian Alternate (GA27-2837-9 fig. 10-10, p. 10-11, PDF p. 254) is a
different, smaller table: X'4A' = ø, X'5A' = å, X'5B' = Å, X'7B' = Æ, X'7C' = Ø,
X'7F' = æ, and it has no #, @, ", ! (X'4F' is |, X'5F' is ¬). It is "Not
Supported by 3174". No code page number for it was found. RetroTerm should not
need it unless a host is found that uses it.

## The other pages asked for: 273, 278, 285, 297, 500

Full tables are in ICU and x3270; they are not copied here. Each was compared
byte by byte (ICU against x3270 for X'41'-X'FE', against Python where Python has
the codec, and the 14 national-use bytes against GA27-2837-9 fig. 10-43).
Differences are in [Every disagreement](#every-disagreement-between-the-sources):
273 one (Python), 278 two (x3270), 285 one (manual), 297 one (manual), 500 none.

---

## Code page 310 - APL and Text graphics (the GE set)

### How GE selects it

The Graphic Escape order is X'08' followed by one byte. That byte is taken from
the **alternate character set**, not from the normal code page (GA23-0059-4
p. 4-9, PDF p. 74):

1. GE + byte wins over any character set given by a field or character attribute,
   for data going out and coming in.
2. GE + X'00' to X'3F' or X'FF' is rejected (sense code X'1003').
3. GE + X'40' to X'FE' are valid characters.
4. If the terminal has no alternate set, they are rejected (sense code X'0863').

GA27-2837-9 fig. 10-41 note 1 says the same from the other side: "These codes,
each preceded by a Graphic Escape (hex 08) control character, transmit the
graphics shown" (p. 10-42, PDF p. 287). So in the buffer a GE character is
**one cell** holding the byte plus a mark "from the alternate set"; on a read it
goes back to the host as X'08' + byte. tnz does exactly that: GE stores the byte
with character set X'F1' (`tnz.py`, `_process_order_0x8`), and on read puts X'08' in front of each such cell
(`tnz.py`, `__append_char_bytes`). x3270
stores it with a `CS_GE` flag (`ctlr.c`, `case ORDER_GE`).

The same set can also be selected for a whole field or for single characters with
the character set attribute X'43' set to X'F1' in SA, SFE or MF. GA23-0059-4
p. 4-19 (PDF p. 84) says X'F0' to X'F7' are the local ids of the built-in
(non-loadable) sets; it does not say that X'F1' is APL. x3270 treats X'F1' as APL
(`ctlr.c`: `if (*cp == 0xf1) cs = CS_APL;`). tnz reports its APL set with local
id X'F1'. That "X'F1' = APL" is the oracles' rule; NOT VERIFIED in an IBM page.

### How the terminal tells the host it has the set

Query Reply (Character Sets), X'85' (GA23-0059-4 pp. 6-21 to 6-28, PDF pp. 191-199):

- Flags byte 1, bit 0 **ALT**: "the device can support an alternate character set
  through the use of the GE control character" (p. 6-22). Bit 6 **GF**: CGCSGIDs
  are present; must be 1 for new implementations (p. 6-23). Both x3270 and tnz send
  X'82' = ALT + GF (`sf.c` `do_qr_charsets`; `tnz.py`).
- One descriptor per set. "The character set with device specific ID equal to X'01'
  is defined as the alternate character page" (p. 6-25). The base set has SET X'00'.
- Each descriptor ends with the CGCSGID (p. 6-28).

What both oracles send for the GE set: SET X'01', flags X'00', local id X'F1',
CGCSGID **X'03C3' X'0136'** = character set 963, code page 310 (x3270 `sf.c`,
"CGCSGID: 3179-style APL2"; tnz `cs_F1 = 963`, `cp_F1 = 310`). The IBM registry
gives 963 as the "Maximal Character Set" of code page 310, which agrees.
tnz turns ALT on only when its alternate codec is `cp310` (`tnz.py`, `self.alt = 1`),
and picks `cp310` when the local console is UTF-8.

### What "3278T" means

The tnz README line 28 says "Code page 310 for APL graphic symbols (i.e. 3278T
terminal type)" and line 77 says "In Windows, the Lucida Console font works well
with graphic character APL symbols exploited by ISPF."

What was checked:

- **"3278T" is not sent on the wire by either oracle.** The string appears in the
  tnz README and `docs/index.md` only; not in any tnz `.py` file and nowhere in the
  x3270 source. tnz's telnet terminal type is `IBM-DYNAMIC` (`tnz.py`,
  `self.terminal_type`); x3270 sends `IBM-3278-n` or `IBM-3278-n-E` or
  `IBM-DYNAMIC` (`telnet.c`). RFC 1647 lists the valid names (`IBM-3278-2` ...
  `IBM-3278-5-E`, `IBM-DYNAMIC`, lines 368-372); there is no "3278T".
- What tnz does on the wire to give the host APL is the GE support above: ALT flag
  plus a second descriptor for code page 310.

What was not checked: "3278T" is, as far as I remember, one of the terminal types
the user picks **inside ISPF** (ISPF Settings, "Terminal Type"), and it tells ISPF
it may use the Text/APL symbols (box corners and lines through GE). That is
NOT VERIFIED; no ISPF manual is in this repository.

### The box-drawing characters ISPF uses

Code page 310 has a full set of single-line box pieces (IBM registry names, and
seen in fig. 10-41):

| Byte | IBM name | Unicode |
|---|---|---|
| X'C5' | Upper Left Box Corner | ┌ U+250C |
| X'D5' | Upper Right Box Corner | ┐ U+2510 |
| X'C4' | Lower Left Box Corner | └ U+2514 |
| X'D4' | Lower Right Box Corner | ┘ U+2518 |
| X'A2' | Center Box Bar Horizontal | ─ U+2500 |
| X'85' | Center Vertical, Bar Graphic | │ U+2502 |
| X'C6' | Left Middle Box Side | ├ U+251C |
| X'D6' | Right Middle Box Side | ┤ U+2524 |
| X'D7' | Middle Box Top | ┬ U+252C |
| X'C7' | Middle Box Bottom | ┴ U+2534 |
| X'D3' | Box Intersection | ┼ U+253C |

**Which of these ISPF actually sends is NOT VERIFIED.** Neither oracle says, and
no ISPF document or host trace is in this repository. A trace from a real ISPF
session with GE support turned on would settle it. Note that X'85' is in the group
fig. 10-41 note 4 calls "graphic plot characters" that "cannot be entered from
either the APL or the Text keyboard" (X'81'-X'85' and X'91'-X'95').

### Full table

Columns:

- **IBM name** - id and name from the IBM registry `CP00310.txt`.
- **In 1987 manual** - is the character in GA27-2837-9 fig. 10-41 (p. 10-42,
  PDF p. 287), read from a 300 dpi picture of the page.
- **x3270**, **tnz** - what each maps the byte to (`unicode.c` `apl2uc[]`;
  `cp310.py` `DECODING_TABLE`). "-" = nothing.
- **Use** - the Unicode this file proposes. Where x3270 and tnz agree and the
  manual picture matches, that is it. Where they differ the choice is explained
  in [Every disagreement](#every-disagreement-between-the-sources).
- **Lucida Console** - does `lucon.ttf` have a glyph for the "Use" character.

X'40' is the space. Every byte not listed is undefined in the IBM registry.

| Byte | IBM name | In 1987 manual | x3270 | tnz | Use | Lucida Console |
|---|---|---|---|---|---|---|
| 41 | LA480000 A Line Below Capital/A Underscore (APL) | yes | A (or circled) | 𝐴 U+1D434 | A + U+0332 | A yes, U+0332 no |
| 42 | LB480000 B Line Below Capital/B Underscore (APL) | yes | B (or circled) | 𝐵 U+1D435 | B + U+0332 | B yes, U+0332 no |
| 43 | LC480000 C Line Below Capital/C Underscore (APL) | yes | C (or circled) | 𝐶 U+1D436 | C + U+0332 | C yes, U+0332 no |
| 44 | LD480000 D Line Below Capital/D Underscore (APL) | yes | D (or circled) | 𝐷 U+1D437 | D + U+0332 | D yes, U+0332 no |
| 45 | LE480000 E Line Below Capital/E Underscore (APL) | yes | E (or circled) | 𝐸 U+1D438 | E + U+0332 | E yes, U+0332 no |
| 46 | LF480000 F Line Below Capital/F Underscore (APL) | yes | F (or circled) | 𝐹 U+1D439 | F + U+0332 | F yes, U+0332 no |
| 47 | LG480000 G Line Below Capital/G Underscore (APL) | yes | G (or circled) | 𝐺 U+1D43A | G + U+0332 | G yes, U+0332 no |
| 48 | LH480000 H Line Below Capital/H Underscore (APL) | yes | H (or circled) | 𝐻 U+1D43B | H + U+0332 | H yes, U+0332 no |
| 49 | LI480000 I Line Below Capital/I Underscore (APL) | yes | I (or circled) | 𝐼 U+1D43C | I + U+0332 | I yes, U+0332 no |
| 51 | LJ480000 J Line Below Capital/J Underscore (APL) | yes | J (or circled) | 𝐽 U+1D43D | J + U+0332 | J yes, U+0332 no |
| 52 | LK480000 K Line Below Capital/K Underscore (APL) | yes | K (or circled) | 𝐾 U+1D43E | K + U+0332 | K yes, U+0332 no |
| 53 | LL480000 L Line Below Capital/L Underscore (APL) | yes | L (or circled) | 𝐿 U+1D43F | L + U+0332 | L yes, U+0332 no |
| 54 | LM480000 M Line Below Capital/M Underscore (APL) | yes | M (or circled) | 𝑀 U+1D440 | M + U+0332 | M yes, U+0332 no |
| 55 | LN480000 N Line Below Capital/N Underscore (APL) | yes | N (or circled) | 𝑁 U+1D441 | N + U+0332 | N yes, U+0332 no |
| 56 | LO480000 O Line Below Capital/O Underscore (APL) | yes | O (or circled) | 𝑂 U+1D442 | O + U+0332 | O yes, U+0332 no |
| 57 | LP480000 P Line Below Capital/P Underscore (APL) | yes | P (or circled) | 𝑃 U+1D443 | P + U+0332 | P yes, U+0332 no |
| 58 | LQ480000 Q Line Below Capital/Q Underscore (APL) | yes | Q (or circled) | 𝑄 U+1D444 | Q + U+0332 | Q yes, U+0332 no |
| 59 | LR480000 R Line Below Capital/R Underscore (APL) | yes | R (or circled) | 𝑅 U+1D445 | R + U+0332 | R yes, U+0332 no |
| 62 | LS480000 S Line Below Capital/S Underscore (APL) | yes | S (or circled) | 𝑆 U+1D446 | S + U+0332 | S yes, U+0332 no |
| 63 | LT480000 T Line Below Capital/T Underscore (APL) | yes | T (or circled) | 𝑇 U+1D447 | T + U+0332 | T yes, U+0332 no |
| 64 | LU480000 U Line Below Capital/U Underscore (APL) | yes | U (or circled) | 𝑈 U+1D448 | U + U+0332 | U yes, U+0332 no |
| 65 | LV480000 V Line Below Capital/V Underscore (APL) | yes | V (or circled) | 𝑉 U+1D449 | V + U+0332 | V yes, U+0332 no |
| 66 | LW480000 W Line Below Capital/W Underscore (APL) | yes | W (or circled) | 𝑊 U+1D44A | W + U+0332 | W yes, U+0332 no |
| 67 | LX480000 X Line Below Capital/X Underscore (APL) | yes | X (or circled) | 𝑋 U+1D44B | X + U+0332 | X yes, U+0332 no |
| 68 | LY480000 Y Line Below Capital/Y Underscore (APL) | yes | Y (or circled) | 𝑌 U+1D44C | Y + U+0332 | Y yes, U+0332 no |
| 69 | LZ480000 Z Line Below Capital/Z Underscore (APL) | yes | Z (or circled) | 𝑍 U+1D44D | Z + U+0332 | Z yes, U+0332 no |
| 70 | SL370000 Less Greater (APL) | no | ⋄ U+22C4 | ⋄ U+22C4 | ⋄ U+22C4 | NO |
| 71 | SL510000 Up Caret (APL) | yes | ∧ U+2227 | ∧ U+2227 | ∧ U+2227 | NO |
| 72 | SL450000 Diaeresis (APL) | yes | ¨ U+00A8 | ¨ U+00A8 | ¨ U+00A8 | yes |
| 73 | SL270000 Quad Jot (APL) | no | ⌻ U+233B | ⌻ U+233B | ⌻ U+233B | NO |
| 74 | SL860000 Iota Underbar (APL) | no | ⍸ U+2378 | ⍸ U+2378 | ⍸ U+2378 | NO |
| 75 | SL870000 Epsilon Underbar (APL) | no | ⍷ U+2377 | ⍷ U+2377 | ⍷ U+2377 | NO |
| 76 | SL340000 Left Tack (APL) | no | ⊢ U+22A2 | ⊢ U+22A2 | ⊢ U+22A2 | NO |
| 77 | SL350000 Right Tack (APL) | no | ⊣ U+22A3 | ⊣ U+22A3 | ⊣ U+22A3 | NO |
| 78 | SL500000 Down Caret (APL) | yes | ∨ U+2228 | ∨ U+2228 | ∨ U+2228 | NO |
| 80 | SL460000 Tilde (APL) | yes | ∼ U+223C | ∼ U+223C | ∼ U+223C | NO |
| 81 | SF630000 Double Vertical, Bar Graphic | yes | - | ║ U+2551 | no Unicode equal | - |
| 82 | SF620000 Double Horizontal, Bar Graphic | yes | - | ═ U+2550 | no Unicode equal | - |
| 83 | SF640000 Left Vertical, Bar Graphic | yes | ⎸ U+23B8 | ⎸ U+23B8 | ⎸ U+23B8 | NO |
| 84 | SF650000 Right Vertical, Bar Graphic | yes | ⎹ U+23B9 | ⎹ U+23B9 | ⎹ U+23B9 | NO |
| 85 | SF660000 Center Vertical, Bar Graphic | yes | │ U+2502 | │ U+2502 | │ U+2502 | yes |
| 8A | SL610000 Up Arrow (APL) | yes | ↑ U+2191 | ↑ U+2191 | ↑ U+2191 | yes |
| 8B | SL620000 Down Arrow (APL) | yes | ↓ U+2193 | ↓ U+2193 | ↓ U+2193 | yes |
| 8C | SL560000 Not Greater (APL) | yes | ≤ U+2264 | ≤ U+2264 | ≤ U+2264 | yes |
| 8D | SL010000 Up Stile (APL) | yes | ⌈ U+2308 | ⌈ U+2308 | ⌈ U+2308 | NO |
| 8E | SL020000 Down Stile (APL) | yes | ⌊ U+230A | ⌊ U+230A | ⌊ U+230A | NO |
| 8F | SL600000 Right Arrow (APL) | yes | → U+2192 | → U+2192 | → U+2192 | yes |
| 90 | SL360000 Quad (APL) | yes | ⎕ U+2395 | ⎕ U+2395 | ⎕ U+2395 | NO |
| 91 | SF580000 Solid Fill Character, Left Half | yes | ▌ U+258C | ▌ U+258C | ▌ U+258C | yes |
| 92 | SF590000 Solid Fill Character, Right Half | yes | ▐ U+2590 | ▐ U+2590 | ▐ U+2590 | yes |
| 93 | SF600000 Solid Fill Character, Top Half | yes | ▀ U+2580 | ▀ U+2580 | ▀ U+2580 | yes |
| 94 | SF570000 Solid Fill Character, Bottom Half | yes | ▄ U+2584 | ▄ U+2584 | ▄ U+2584 | yes |
| 95 | SF610000 Solid Fill Character | yes | ■ U+25A0 | █ U+2588 | █ U+2588 | yes |
| 9A | SL430000 Right Shoe (APL) | yes | ⊃ U+2283 | ⊃ U+2283 | ⊃ U+2283 | NO |
| 9B | SL420000 Left Shoe (APL) | yes | ⊂ U+2282 | ⊂ U+2282 | ⊂ U+2282 | NO |
| 9C | SM490000 Lozenge | yes | ¤ U+00A4 | ⌑ U+2311 | ⌑ U+2311 | NO |
| 9D | SL080000 Circle (APL) | yes | ○ U+25CB | ○ U+25CB | ○ U+25CB | yes |
| 9E | SA020000 Plus or Minus Sign | yes | ± U+00B1 | ± U+00B1 | ± U+00B1 | yes |
| 9F | SL590000 Left Arrow (APL) | yes | ← U+2190 | ← U+2190 | ← U+2190 | yes |
| A0 | SL630000 Overbar (APL) | yes | ¯ U+00AF | ¯ U+00AF | ¯ U+00AF | yes |
| A1 | SM190000 Degree Symbol | yes | ° U+00B0 | ° U+00B0 | ° U+00B0 | yes |
| A2 | SF100000 Center Box Bar Horizontal | yes | ─ U+2500 | ─ U+2500 | ─ U+2500 | yes |
| A3 | SM570000 Bullet | yes | • U+2022 | ∙ U+2219 | • U+2022 | yes |
| A4 | LN012000 n Small Subscript | yes | - | ₙ U+2099 | ₙ U+2099 | NO |
| AA | SL400000 Up Shoe (APL) | yes | ∩ U+2229 | ∩ U+2229 | ∩ U+2229 | yes |
| AB | SL410000 Down Shoe (APL) | yes | ∪ U+222A | ∪ U+222A | ∪ U+222A | NO |
| AC | SL230000 Down Tack (APL) | yes | ⊥ U+22A5 | ⊥ U+22A5 | ⊥ U+22A5 | NO |
| AD | SL770000 Left Bracket (APL) | yes | \[ U+005B | \[ U+005B | \[ U+005B | yes |
| AE | SL570000 Not Less (APL) | yes | ≥ U+2265 | ≥ U+2265 | ≥ U+2265 | yes |
| AF | SL250000 Jot (APL) | yes | ∘ U+2218 | ∘ U+2218 | ∘ U+2218 | NO |
| B0 | SL710000 Alpha (APL) | yes | ⍺ U+237A | ⍺ U+237A | ⍺ U+237A | NO |
| B1 | SL720000 Epsilon (APL) | yes | ∊ U+220A | ∈ U+2208 | ∊ U+220A | NO |
| B2 | SL730000 Iota (APL) | yes | ⍳ U+2373 | ⍳ U+2373 | ⍳ U+2373 | NO |
| B3 | SL740000 Rho (APL) | yes | ⍴ U+2374 | ⍴ U+2374 | ⍴ U+2374 | NO |
| B4 | SL750000 Omega (APL) | yes | ⍵ U+2375 | ⍵ U+2375 | ⍵ U+2375 | NO |
| B6 | SL550000 Times (APL) | yes | × U+00D7 | × U+00D7 | × U+00D7 | yes |
| B7 | SL640000 Slope (APL) | yes | \\ U+005C | ∖ U+2216 | \\ U+005C | yes |
| B8 | SL540000 Divide (APL) | yes | ÷ U+00F7 | ÷ U+00F7 | ÷ U+00F7 | yes |
| BA | SL030000 Del (APL) | yes | ∇ U+2207 | ∇ U+2207 | ∇ U+2207 | NO |
| BB | SL060000 Delta (APL) | yes | ∆ U+2206 | ∆ U+2206 | ∆ U+2206 | yes |
| BC | SL220000 Up Tack (APL) | yes | ⊤ U+22A4 | ⊤ U+22A4 | ⊤ U+22A4 | NO |
| BD | SL780000 Right Bracket (APL) | yes | \] U+005D | \] U+005D | \] U+005D | yes |
| BE | SL820000 Not Equal (APL) | yes | ≠ U+2260 | ≠ U+2260 | ≠ U+2260 | yes |
| BF | SL380000 Stile (APL) | yes | ∣ U+2223 | │ U+2502 | ∣ U+2223 | NO |
| C0 | SM110000 Left Brace | yes | { U+007B | { U+007B | { U+007B | yes |
| C1 | SP061000 Left Parenthesis Superscript | yes | ⁼ U+207C | ⁽ U+207D | ⁽ U+207D | NO |
| C2 | SA011000 Plus Sign Superscript | yes | + U+002B | ⁺ U+207A | ⁺ U+207A | NO |
| C3 | SM470000 Solid Square/Histogram/Square Bullet | yes | ∎ U+220E | ■ U+25A0 | ■ U+25A0 | yes |
| C4 | SF020000 Lower Left Box Corner | yes | └ U+2514 | └ U+2514 | └ U+2514 | yes |
| C5 | SF010000 Upper Left Box Corner | yes | ┌ U+250C | ┌ U+250C | ┌ U+250C | yes |
| C6 | SF080000 Left Middle Box Side | yes | ├ U+251C | ├ U+251C | ├ U+251C | yes |
| C7 | SF070000 Middle Box Bottom | yes | ┴ U+2534 | ┴ U+2534 | ┴ U+2534 | yes |
| C8 | SM240000 Section Symbol (USA)/Paragraph Symbol (Europe) | yes | § U+00A7 | § U+00A7 | § U+00A7 | yes |
| CA | SL170000 Up Caret Tilde (APL) | yes | ⍲ U+2372 | ⍲ U+2372 | ⍲ U+2372 | NO |
| CB | SL180000 Down Caret Tilde (APL) | yes | ⍱ U+2371 | ⍱ U+2371 | ⍱ U+2371 | NO |
| CC | SL260000 Left Bracket Right Bracket (APL) | no | ⌷ U+2337 | ⌷ U+2337 | ⌷ U+2337 | NO |
| CD | SL090000 Circle Stile (APL) | yes | ⌽ U+233D | ⌽ U+233D | ⌽ U+233D | NO |
| CE | SL280000 Quad Slope (APL) | no | ⍂ U+2342 | ⍂ U+2342 | ⍂ U+2342 | NO |
| CF | SL100000 Circle Slope (APL) | yes | ⍉ U+2349 | ⍉ U+2349 | ⍉ U+2349 | NO |
| D0 | SM140000 Right Brace | yes | } U+007D | } U+007D | } U+007D | yes |
| D1 | SP071000 Right Parenthesis Superscript | yes | ⁾ U+207E | ⁾ U+207E | ⁾ U+207E | NO |
| D2 | SP101000 Minus Sign Superscript | yes | - U+002D | ⁻ U+207B | ⁻ U+207B | NO |
| D3 | SF050000 Box Intersection | yes | ┼ U+253C | ┼ U+253C | ┼ U+253C | yes |
| D4 | SF040000 Lower Right Box Corner | yes | ┘ U+2518 | ┘ U+2518 | ┘ U+2518 | yes |
| D5 | SF030000 Upper Right Box Corner | yes | ┐ U+2510 | ┐ U+2510 | ┐ U+2510 | yes |
| D6 | SF090000 Right Middle Box Side | yes | ┤ U+2524 | ┤ U+2524 | ┤ U+2524 | yes |
| D7 | SF060000 Middle Box Top | yes | ┬ U+252C | ┬ U+252C | ┬ U+252C | yes |
| D8 | SM250000 Paragraph Symbol (USA) | yes | ¶ U+00B6 | ¶ U+00B6 | ¶ U+00B6 | yes |
| DA | SL240000 Down Tack Up Tack (APL) | yes | ⌶ U+2336 | ⌶ U+2336 | ⌶ U+2336 | NO |
| DB | SL580000 Quote Dot (APL) | yes | ! U+0021 | ǃ U+01C3 | ! U+0021 | yes |
| DC | SL050000 Del Stile (APL) | yes | ⍒ U+2352 | ⍒ U+2352 | ⍒ U+2352 | NO |
| DD | SL070000 Delta Stile (APL) | yes | ⍋ U+234B | ⍋ U+234B | ⍋ U+234B | NO |
| DE | SL130000 Quad Quote (APL) | yes | ⍞ U+235E | ⍞ U+235E | ⍞ U+235E | NO |
| DF | SL210000 Up Shoe Null (APL) | yes | ⍝ U+235D | ⍝ U+235D | ⍝ U+235D | NO |
| E0 | SL300000 Equal Underbar (APL) | no | ≡ U+2261 | ≡ U+2261 | ≡ U+2261 | yes |
| E1 | ND012000 One Subscript | yes | ₁ U+2081 | ₁ U+2081 | ₁ U+2081 | NO |
| E2 | ND022000 Two Subscript | yes | ʂ U+0282 | ₂ U+2082 | ₂ U+2082 | NO |
| E3 | ND032000 Three Subscript | yes | ʃ U+0283 | ₃ U+2083 | ₃ U+2083 | NO |
| E4 | (not in IBM registry) | no | ⍤ U+2364 | ⍤ U+2364 | undefined (open question 1) | - |
| E5 | (not in IBM registry) | no | ⍥ U+2365 | ⍥ U+2365 | undefined (open question 1) | - |
| E6 | (not in IBM registry) | no | ⍪ U+236A | ⍪ U+236A | undefined (open question 1) | - |
| E7 | (not in IBM registry) | no | € U+20AC | € U+20AC | undefined (open question 1) | - |
| EA | SL150000 Slash Bar (APL) | yes | ⌿ U+233F | ⌿ U+233F | ⌿ U+233F | NO |
| EB | SL160000 Slope Bar (APL) | yes | ⍀ U+2340 | ⍀ U+2340 | ⍀ U+2340 | NO |
| EC | SL320000 Diaeresis Dot (APL) | no | ∵ U+2235 | ∵ U+2235 | ∵ U+2235 | NO |
| ED | SL120000 Circle Bar | yes | ⊖ U+2296 | ⊖ U+2296 | ⊖ U+2296 | NO |
| EE | SL140000 Quad Divide (APL) | yes | ⌹ U+2339 | ⌹ U+2339 | ⌹ U+2339 | NO |
| EF | SL200000 Up Tack Jot (APL) | yes | ⍕ U+2355 | ⍕ U+2355 | ⍕ U+2355 | NO |
| F0 | ND101000 Zero Superscript | yes | ⁰ U+2070 | ⁰ U+2070 | ⁰ U+2070 | NO |
| F1 | ND011000 One Superscript | yes | ¹ U+00B9 | ¹ U+00B9 | ¹ U+00B9 | yes |
| F2 | ND021000 Two Superscript | yes | ² U+00B2 | ² U+00B2 | ² U+00B2 | yes |
| F3 | ND031000 Three Superscript | yes | ³ U+00B3 | ³ U+00B3 | ³ U+00B3 | yes |
| F4 | ND041000 Four Superscript | yes | ⁴ U+2074 | ⁴ U+2074 | ⁴ U+2074 | NO |
| F5 | ND051000 Five Superscript | yes | ⁵ U+2075 | ⁵ U+2075 | ⁵ U+2075 | NO |
| F6 | ND061000 Six Superscript | yes | ⁶ U+2076 | ⁶ U+2076 | ⁶ U+2076 | NO |
| F7 | ND071000 Seven Superscript | yes | ⁷ U+2077 | ⁷ U+2077 | ⁷ U+2077 | NO |
| F8 | ND081000 Eight Superscript | yes | ⁸ U+2078 | ⁸ U+2078 | ⁸ U+2078 | NO |
| F9 | ND091000 Nine Superscript | yes | ⁹ U+2079 | ⁹ U+2079 | ⁹ U+2079 | NO |
| FB | SL040000 Del Tilde (APL) | yes | ⍫ U+236B | ⍫ U+236B | ⍫ U+236B | NO |
| FC | SL330000 Delta Underbar (APL) | yes | ⍙ U+2359 | ⍙ U+2359 | ⍙ U+2359 | NO |
| FD | SL110000 Circle Star (APL) | yes | ⍟ U+235F | ⍟ U+235F | ⍟ U+235F | NO |
| FE | SL190000 Down Tack Jot (APL) | yes | ⍎ U+234E | ⍎ U+234E | ⍎ U+234E | NO |

The underscored letters X'41'-X'69': there is **no Unicode character** for "A with
a line under it". x3270 shows a plain letter or, with an option, a circled letter
(U+24B6 onward, `apl_to_unicode()`); tnz uses mathematical italic capitals
U+1D434 onward, with no underline at all. Proposed: keep the byte, draw the plain
letter and draw the underline ourselves; for copy to the clipboard, letter +
U+0332 COMBINING LOW LINE.

Other characters with no Unicode equal: X'81' (bars at the left and right edges
of the cell) and X'82' (bars at the top and bottom edges). The registry calls them
"Double Vertical, Bar Graphic" and "Double Horizontal, Bar Graphic"; the picture
in fig. 10-41 shows lines at the cell edges, not the centred double line of ║ ═
that tnz uses. They must be drawn by RetroTerm itself. X'83'/X'84' (left and right
edge bar) have Unicode ⎸ ⎹ but Lucida Console has neither.

Name trap, not a disagreement: IBM's APL names and Unicode names for the tacks are
swapped. IBM calls X'AC' "Down Tack" and X'BC' "Up Tack"; the picture shows ⊥ at
X'AC' and ⊤ at X'BC', which are Unicode UP TACK U+22A5 and DOWN TACK U+22A4. Both
oracles map by shape, as proposed here. The same naming swap applies to X'76'/X'77'
(IBM "Left Tack"/"Right Tack", oracles ⊢ U+22A2 / ⊣ U+22A3); those two are not in
the 1987 picture, so their shape is NOT VERIFIED.

---

## Lucida Console, measured

Ronny reports Lucida Console works well for the ISPF APL symbols; tnz's README
says the same (line 77). The font file `lucon.ttf` from the Windows Fonts folder on the
development machine was opened with Python fontTools 4.56.0 and its character
map read. The file reports itself as "Lucida Console, Version 5.01", 644 mapped
characters.

Results:

- **Code page 037, 277 and 1142:** every character X'40'-X'FE' is in the font.
- **Code page 310:** of the 110 bytes with a proposed Unicode character, **46 are
  in the font and 64 are not.** In: all eleven box pieces, the half and full
  blocks X'91'-X'95', the arrows ↑ ↓ → ←, ≤ ≥ ≠ ≡ ∆ ∩ ○ ± × ÷ ¨ ¯ ° • ¹ ² ³ § ¶
  { } [ ] \ ! ■. Missing: almost every real APL symbol - X'70' ⋄, X'71' ∧, X'78' ∨,
  X'80' ∼, X'8D' ⌈, X'8E' ⌊, X'90' ⎕, X'9A' ⊃, X'9B' ⊂, X'AB' ∪, X'AC' ⊥, X'AF' ∘,
  X'B0'-X'B4' ⍺ ∊ ⍳ ⍴ ⍵, X'BA' ∇, X'BC' ⊤, X'BF' ∣, the whole X'CA'-X'CF',
  X'DA', X'DC'-X'DF', X'EA'-X'EF' groups, X'FB'-X'FE', X'73'-X'77', X'83', X'84',
  X'9C' ⌑, X'A4' ₙ, the superscripts X'C1', X'C2', X'D1', X'D2', X'F0', X'F4'-X'F9',
  and the subscripts X'E1'-X'E3'.
  The full list is the "NO" rows of the table above.
- Also missing: U+0332 and U+0331 (combining underline), U+25CF ● (x3270's EO),
  U+2218 ∘ (tnz's SUB), U+2611 and U+2612 (tnz's DUP/FM), the circled letters
  U+24B6 and the math italic letters U+1D434 (the two oracles' choices for the
  underscored letters). Present: U+25A0 ■, U+203E ‾, U+00AF ¯, U+20AC €, U+00A4 ¤.

So what Lucida Console does well is the **box drawing and the block shapes**,
which is most of what ISPF panels show. The APL symbols themselves are not in the
font. If they appear on screen in a Windows program using Lucida Console, the
system must be filling them in from another font; which one, and whether
RetroTerm's Avalonia text drawing does the same, is NOT VERIFIED.

---

## Which Unicode characters to use, and which have none

Rules for the emulation (proposed; based on the sections above):

1. The buffer stores the **EBCDIC byte** and a character set mark (base, or GE/APL
   X'F1'). Unicode is only for drawing and for copy and paste. This is required
   anyway: undefined bytes must be read back unchanged (GA23-0059-4 p. 4-10).
2. Base set: byte X'40'-X'FE' to Unicode through the chosen code page table
   (ICU-equal tables above). X'FF' is EO.
3. Controls: drawn as in the control table; DUP and FM drawn with a bar over them.
4. GE / X'F1' set: the "Use" column of the code page 310 table.
5. No Unicode equal, must be drawn by RetroTerm: DUP, FM, the underscored letters
   X'41'-X'69', X'81', X'82'. Lucida Console also lacks X'83', X'84' and 64 APL
   symbols, so either RetroTerm draws those too, or a second font is used for them
   (open question).

---

## How the user picks a code page

**x3270** (`unicode.c`, `codepage.c`):

- A `codePage` setting (also given as an option) takes a number (`277`, `cp277`)
  or a name. Names include: `us` and `us-intl` = 037, `us-euro` = 1140,
  `norwegian` = 277, `norwegian-euro` = 1142, `finnish` and `swedish` = 278,
  `finnish-euro` and `swedish-euro` = 1143, `german` = 273, `german-euro` = 1141,
  `uk` = 285, `uk-euro` = 1146, `french` = 297, `french-euro` = 1147,
  `belgian` = 500, `belgian-euro` = 1148, and more (alias table `cpaliases[]`).
  There is no `danish` alias.
- With nothing set, x3270 uses `bracket` (`codepage.c`: `cpname = "bracket"`).
- Code pages it has: 037, 273, 275, 277, 278, 280, 284, 285, 297, 424, 500, 803,
  870, 871, 875, 880, 924, 1025, 1026, 1047, 1123, 1140-1149, 1154, 1158, 1160,
  and double-byte pages 930, 933, 935, 937, 939, 1364, 1388, 1390, 1399.
- APL (code page 310) is always on: it is not a choice. x3270 always reports the GE
  set in its query reply (`sf.c`).

**tnz** (`tnz.py`, `ati.py`, `zti.py`):

- Setting `SESSION_CODE_PAGE` (for example `cp037`; a bare number gets `cp` put in
  front) picks the base page (`ati.py` lines 2645-2651; listed in `zti.py` help
  line 1085).
- The page must exist as a Python codec. Python 3.11 has `cp037`, `cp273`,
  `cp500`, `cp1140`, but **not** `cp277` (checked: lookup fails). tnz's README
  suggests installing the `ebcdic` package from PyPI; whether that package has
  cp277 and cp1142 is NOT VERIFIED.
- Default: `cp037`, then it tries `cp1047` and keeps it if that codec exists
  (`tnz.py`, "smart detection").
- The GE set `cp310` is turned on automatically when the console is UTF-8, and only
  then is ALT reported to the host.

For RetroTerm the choice belongs in the connection settings window (no environment
variables, per the repository rules), as a list of code pages by number and name.

---

## Every disagreement between the sources

### Base code pages

| Page | Byte | What differs | Which to follow |
|---|---|---|---|
| 278 | X'71' and X'E0' | **x3270 swaps them**: x3270 X'71' = É U+00C9, X'E0' = \\ U+005C. ICU X'71' = \\, X'E0' = É. GA27-2837-9 fig. 10-43 (p. 10-45) shows É at X'E0' for Finnish/Swedish. | ICU and the manual (two against one). |
| 273 | X'BC' | Python `cp273` = ‾ U+203E overline; ICU = ¯ U+00AF macron. x3270 agrees with ICU. Manual: X'BC' is not in its base table. | ICU. |
| 285 | X'A1' | GA27-2837-9 fig. 10-43 draws a high bar for English (U.K.) at X'A1'; read as ‾ overline. ICU and x3270 = ¯ U+00AF macron. Both are "a bar above"; which Unicode is a matter of naming, the picture cannot tell them apart. | ICU. |
| 297 | X'79' | GA27-2837-9 fig. 10-43 shows a grave accent \` at X'79' for French (1987). ICU and x3270 = µ U+00B5. | ICU (the later CECP); the 1987 base table predates it. NOT VERIFIED which one real French hosts expect. |
| 037 | X'AD', X'BA', X'BB', X'BD' | x3270's default `bracket` table differs from 037 (see the 037 section). It is not a disagreement about 037, but a user who does not set a code page in x3270 gets it. | 037 for RetroTerm. |

No disagreement for 037 (ICU, unicode.org, Python, x3270, manual), 277 (ICU,
x3270, manual), 500 (ICU, Python, x3270, manual national-use bytes), 1047 (ICU,
x3270), 1140-1149 (ICU, x3270; 1140 also Python).

### Controls

| Control | GA27-2837-9 fig. 10-1 | GA23-0059-4 p. 4-10 | x3270 | tnz |
|---|---|---|---|---|
| SUB X'3F' | not stated | solid circle | ■ U+25A0 solid square | ∘ U+2218 (ring, not solid; comment says solid circle) |
| EO X'FF' | not stated | blank | ● U+25CF solid circle | blank |
| DUP X'1C' | \* with bar, dual-case; \* mono-case | overscore asterisk | \* (or own-font character) | ☑ U+2611 |
| FM X'1E' | ; with bar, dual-case; ; mono-case | overscore semicolon | ; (or own-font character) | ☒ U+2612 |

Follow GA23-0059-4 and GA27-2837-9: SUB solid circle, EO blank, DUP/FM with a bar.

### Code page 310

| Byte | IBM registry name | 1987 manual picture | x3270 | tnz | Proposed, and why |
|---|---|---|---|---|---|
| X'70', X'73'-X'77', X'CC', X'CE', X'E0', X'EC' | in the registry | **not in the 1987 picture** | defined | defined | Use them (the registry has them; both oracles agree). The 1987 manual is older than these additions. |
| X'E4'-X'E7' | **not in the registry** | not in the picture | ⍤ ⍥ ⍪ € | ⍤ ⍥ ⍪ € | Undefined, as IBM says. x3270's own comment marks them "(1) Not on Code Page 310" (`apl.c`). Open question. |
| X'81' | Double Vertical, Bar Graphic | lines at left and right cell edges | nothing | ║ U+2551 | Draw it ourselves. |
| X'82' | Double Horizontal, Bar Graphic | lines at top and bottom cell edges | nothing | ═ U+2550 | Draw it ourselves. |
| X'95' | Solid Fill Character | whole cell black | ■ U+25A0 | █ U+2588 | █ U+2588 (whole cell, matches the picture and the name). |
| X'9C' | Lozenge | square with hollow sides | ¤ U+00A4 | ⌑ U+2311 | ⌑ U+2311 SQUARE LOZENGE (matches the picture). Not in Lucida Console; ¤ is. |
| X'A3' | Bullet | small dot | • U+2022 | ∙ U+2219 | • U+2022 (IBM name is "Bullet"). |
| X'A4' | n Small Subscript | n with the subscript mark | nothing | ₙ U+2099 | ₙ U+2099. |
| X'B1' | Epsilon (APL) | ε | ∊ U+220A | ∈ U+2208 | ∊ U+220A (the APL epsilon in common APL use; NOT VERIFIED against an IBM Unicode table). |
| X'B7' | Slope (APL) | \\ | \\ U+005C | ∖ U+2216 | \\ U+005C. |
| X'BF' | Stile (APL) | vertical bar | ∣ U+2223 | │ U+2502 | ∣ U+2223 (a stile is a symbol, not a box line; X'85' is the box line). |
| X'C1' | Left Parenthesis Superscript | ( with the superscript mark | **⁼ U+207C (superscript equals - wrong)** | ⁽ U+207D | ⁽ U+207D. x3270 is wrong here. |
| X'C2' | Plus Sign Superscript | + with superscript mark | + U+002B (plain) | ⁺ U+207A | ⁺ U+207A. |
| X'D2' | Minus Sign Superscript | − with superscript mark | - U+002D (plain) | ⁻ U+207B | ⁻ U+207B. |
| X'C3' | Solid Square/Histogram/Square Bullet | small black square | ∎ U+220E | ■ U+25A0 | ■ U+25A0. |
| X'DB' | Quote Dot (APL) | ! | ! U+0021 | ǃ U+01C3 | ! U+0021 (U+01C3 is a click letter). |
| X'E2', X'E3' | Two Subscript, Three Subscript | 2, 3 with subscript mark | **ʂ U+0282, ʃ U+0283 (typing error)** | ₂ U+2082, ₃ U+2083 | ₂ U+2082, ₃ U+2083. x3270 is wrong here. |
| X'41'-X'69' (letters) | A-Z Underscore | letters with a line under | plain or circled letter | math italic letter | Draw the underline (see above). |

Also seen in x3270, not a mapping disagreement: in `apl.c` the keyboard name
`Xunderbar` is given `UPRIV2 + 'Z'` instead of `'X'` (line 105). Only x3270's
key input is affected.

---

## Open questions

1. **X'E4'-X'E7' in the GE set** (⍤ ⍥ ⍪ €): IBM's registry leaves them undefined;
   both x3270 and tnz fill them in. Follow IBM (undefined), or follow the oracles?
2. **What ISPF sends.** Which GE characters ISPF actually uses for its boxes and
   whether it needs the "3278T" setting inside ISPF: needs a trace from a real
   z/OS ISPF session with GE reported in the query reply. Not available here.
3. **"3278T" meaning.** Believed to be an ISPF terminal type setting; NOT VERIFIED.
   Needs an ISPF manual.
4. **Fonts for the APL symbols.** Lucida Console lacks 64 of the 110 GE
   characters. Draw them ourselves, ship or name a second font for them, or rely on
   system font fallback (whether Avalonia does that on Windows is NOT VERIFIED)?
5. **X'F1' as the APL local id** in SA/SFE/MF: both oracles use it; no IBM page
   read here says so.
6. **French 297 X'79'**: µ (ICU, later CECP) or \` (1987 manual)?
7. **CGCSGID character set number**: 697 for the base pages and 695 for the euro
   pages, as x3270 sends; tnz always sends 697. Not checked against an IBM
   document (GA23-0059-4 p. 6-28 points to GA27-3831, which is not in this
   repository).
8. **Danish vs Norwegian**: both use 277 / 1142 (IBM registry "Denmark, Norway");
   x3270 has only a `norwegian` name. RetroTerm's list should show both names.
