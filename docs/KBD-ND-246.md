# ND 246 Keyboard Layout Documentation

## Overview

This document provides a comprehensive reference for the ND 246 keyboard layout used with TDV2200 terminals. The keyboard supports 13 national layout variants based on ISO 646 character set standards.

**Source**: TDV-2200/9 User's Guide (ND), Part no. 408356, Publication no. 5599, November 1984
**Documentation Path**: `spec\TDV2200\OCR\TDV-2200_9-User-s_Guide-ND_combined.md`

## Supported National Variants

1. **Norwegian (no)** - Default variant
2. **Danish (dk)**
3. **Swedish (sv)**
4. **German (de)**
5. **US ASCII (us)**
6. **French (fr)**
7. **SDS** - Norsk Data Standard
8. **FAO** - Special variant
9. **English/UK (en)**
10. **Swiss (ch)**
11. **Finnish (fi)**
12. **Icelandic (is)**
13. **Keyboard** - Debug mode showing grid positions

## Keyboard Matrix Grid System

The keyboard uses a letter-number grid system:
- **Rows**: A through G (A=bottom/spacebar, G=top/function keys)
- **Columns**: 0-54 (includes main area, navigation area, and numeric pad)
- **Special Column 99**: Leftmost modifier keys in each row

### Row Layout
```
G-row: ESC, P1-P8, MERK/FELT/AVSH/SETN/ORD/LOKAL, STRYK/KOPI/FLYTT, FUNK/SKRIV/HJELP/SLUTT
F-row: Blank in main area, F51-F54 (PUSH SI, HEX SO, CLEAR, empty)
E-row: CAPS, 1-0, special chars, @, navigation/function keys, empty
D-row: INNS/EXPS, CTRL, Q-P, Å/Ü/etc., DEL, wider key, PG DN/ANGRE/PG UP, 7-9, -
C-row: MODE, LOCK, A-L, Ø/Ö, Æ/Ä, ', RETURN, ERASE PAGE/LINE/INSERT, 4-6, +
B-row: SHIFT, Z-/, -, SHIFT, LEFT/HOME/RIGHT, 1-3, ENTER (tall)
A-row: Spacebar, 0, DOWN, ENTER, wide 0, ., ENTER (tall)
```

## Complete Key Mapping by Position

### Main Keyboard Area - Row G (Top)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **G0** | ESC | ESC | ESC | ESC | ESC | ESC | ESC | ESC | ESC | ESC | ESC | ESC |
| **G1-G8** | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 | P1-P8 |
| **G9** | MERK | MRK | MERK | MERK | MARK | MARQ | MERK | MARK | MERK | MARK | MERK | MERK |
| **G10** | FELT | FELT | FÄLT | FELT | FIELD | CHAMP | FELT | FIELD | FELT | FIELD | KENTTÄ | FELT |
| **G11** | AVSH | AVSH | AVSH | AVSH | PARA | PARA | AVSH | PARA | AVSH | PARA | KAPPALE | AVSH |
| **G12** | SETN | SETN | SETN | SETN | SENT | SENT | SETN | SENT | SETN | SENT | LAUSE | SETN |
| **G13** | ORD | ORD | ORD | ORD | WORD | MOT | ORD | WORD | ORD | WORD | SANA | ORD |
| **G14** | LOKAL | LOKAL | LOKAL | LOKAL | LOCAL | LOCAL | LOKAL | LOCAL | LOKAL | LOCAL | LOKAL | LOKAL |
| **G47** | STRYK | STRYK | STRYK | STRYK | DELETE | SUPPR | STRYK | DELETE | STRYK | DELETE | POISTA | STRYK |
| **G48** | KOPI | KOPI | KOPI | KOPI | COPY | COPIE | KOPI | COPY | KOPI | COPY | KOPIOI | KOPI |
| **G49** | FLYTT | FLYTT | FLYTT | FLYTT | MOVE | DEPL | FLYTT | MOVE | FLYTT | MOVE | SIIRRÄ | FLYTT |
| **G51** | FUNK | FUNK | FUNK | FUNK | FUNC | FONC | FUNK | FUNC | FUNK | FUNC | FUNK | FUNK |
| **G52** | SKRIV | SKRIV | SKRIV | SKRIV | PRINT | IMPRI | SKRIV | PRINT | SKRIV | PRINT | KIRJ | SKRIV |
| **G53** | HJELP | HJLP | HJÄLP | HJELP | HELP | AIDE | HJELP | HELP | HJELP | HELP | AUTA | HJÁLP |
| **G54** | SLUTT | SLUT | SLUTT | SLUTT | EXIT | FIN | SLUTT | EXIT | SLUTT | EXIT | LOPPU | HÆTTA |

### Main Keyboard Area - Row E (Number Row)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **E0** | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS | CAPS |
| **E1** | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! | 1<br>! |
| **E2** | 2<br>" | 2<br>" | 2<br>" | 2<br>" | 2<br>@ | 2<br>" | 2<br>" | 2<br>" | 2<br>" | 2<br>" | 2<br>" | 2<br>" |
| **E3** | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># | 3<br># |
| **E4** | 4<br>¤ | 4<br>$ | 4<br>¤ | 4<br>$ | 4<br>$ | 4<br>$ | 4<br>$ | 4<br>$ | 4<br>§ | 4<br>$ | 4<br>$ | 4<br>$ |
| **E5** | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% | 5<br>% |
| **E6** | 6<br>& | 6<br>& | 6<br>& | 6<br>& | 6<br>^ | 6<br>& | 6<br>& | 6<br>& | 6<br>& | 6<br>& | 6<br>& | 6<br>& |
| **E7** | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>& | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>/ | 7<br>/ |
| **E8** | 8<br>( | 8<br>( | 8<br>( | 8<br>( | 8<br>* | 8<br>( | 8<br>( | 8<br>( | 8<br>( | 8<br>( | 8<br>( | 8<br>( |
| **E9** | 9<br>) | 9<br>) | 9<br>) | 9<br>) | 9<br>( | 9<br>) | 9<br>) | 9<br>) | 9<br>) | 9<br>) | 9<br>) | 9<br>) |
| **E10** | 0<br>= | 0<br>= | 0<br>= | 0<br>= | 0<br>) | 0<br>= | 0<br>= | 0<br>= | 0<br>= | 0<br>= | 0<br>= | 0<br>= |
| **E11** | +<br>? | +<br>? | +<br>? | +<br>? | -<br>_ | +<br>? | +<br>? | +<br>? | +<br>? | +<br>? | +<br>? | +<br>? |
| **E12** | \|<br>` | \|<br>` | \|<br>` | \|<br>` | =<br>+ | \|<br>` | \|<br>` | \|<br>` | \|<br>` | \|<br>` | \|<br>` | \|<br>` |
| **E13** | ´<br>\` | ´<br>\` | ´<br>\` | ´<br>\` | \`<br>~ | ´<br>\` | ´<br>\` | ´<br>\` | ´<br>\` | ´<br>\` | ´<br>\` | ´<br>\` |
| **E14** | @ | @ | @ | @ | @ | @ | @ | @ | @ | @ | @ | @ |

### Main Keyboard Area - Row D (QWERTY Row)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **D99** | INNS<br>EKSP | INS<br>EXP | INS<br>EXP | EINS<br>ERWE | INS<br>EXP | INS<br>EXP | INS<br>EXP | APPND<br>EXP | APPND<br>EXP | IN.L<br>EXP | INS<br>EXP | INS<br>EXP |
| **D0** | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL | CTRL |
| **D1** | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q | Q<br>q |
| **D2** | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w | W<br>w |
| **D3** | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e | E<br>e |
| **D4** | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r | R<br>r |
| **D5** | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t | T<br>t |
| **D6** | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y | Y<br>y |
| **D7** | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u | U<br>u |
| **D8** | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i | I<br>i |
| **D9** | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o | O<br>o |
| **D10** | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p | P<br>p |
| **D11** | Å<br>å | Å<br>å | Å<br>å | Ü<br>ü | [<br>{ | ^<br>¨ | Å<br>å | [<br>{ | [<br>{ | Ü<br>ü | Å<br>å | Ð<br>ð |
| **D12** | ¨<br>^ | ¨<br>^ | ¨<br>^ | +<br>* | ]<br>} | $<br>£ | ¨<br>^ | ]<br>} | ]<br>} | +<br>* | ¨<br>^ | Þ<br>þ |
| **D13** | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) | (wider) |
| **D47** | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN | PG DN |
| **D48** | ANGRE<br>EKSP | OPHÆV<br>UDVID | ÅNGRA<br>UTÖKA | RÜCKGÄNG<br>ERWEIT | CANCEL<br>EXPAND | ANNUL<br>ETENDRE | ANGRE<br>EKSP | CANCEL<br>EXPAND | ANGRE<br>EKSP | RÜCKG<br>ERWEI | PERU<br>LAAJ | ANGRE<br>EKSP |
| **D49** | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP | PG UP |

### Main Keyboard Area - Row C (ASDF Row)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **C99** | MODE | MODE | MODE | MODE | MODE | MODE | MODE | MODE | MODE | MODE | MODE | MODE |
| **C0** | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK | LOCK |
| **C1** | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a | A<br>a |
| **C2** | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s | S<br>s |
| **C3** | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d | D<br>d |
| **C4** | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f | F<br>f |
| **C5** | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g | G<br>g |
| **C6** | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h | H<br>h |
| **C7** | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j | J<br>j |
| **C8** | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k | K<br>k |
| **C9** | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l | L<br>l |
| **C10** | Ø<br>ø | Ø<br>ø | Ö<br>ö | Ö<br>ö | ;<br>: | Ö<br>ö | Ø<br>ø | ;<br>: | ;<br>: | Ö<br>ö | Ö<br>ö | ;<br>: |
| **C11** | Æ<br>æ | Æ<br>æ | Ä<br>ä | Ä<br>ä | '<br>" | Ä<br>ä | Æ<br>æ | '<br>" | '<br>" | Ä<br>ä | Ä<br>ä | '<br>" |
| **C12** | '<br>* | '<br>* | '<br>* | #<br>' | \<br>\| | '<br>* | '<br>* | #<br>~ | #<br>~ | #<br>' | '<br>* | '<br>* |
| **C13** | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN | RETURN |
| **C47** | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE | ERASE<br>PAGE |
| **C48** | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE | ERASE<br>LINE |
| **C49** | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT | INSERT |

### Main Keyboard Area - Row B (ZXCV Row)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **B99** | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT |
| **B0** | ><br>< | ><br>< | ><br>< | <<br>> | Z<br>z | <<br>> | ><br>< | Z<br>z | Z<br>z | <<br>> | ><br>< | ><br>< |
| **B1** | Z<br>z | Z<br>z | Z<br>z | Y<br>y | X<br>x | W<br>w | Z<br>z | X<br>x | X<br>x | Y<br>y | Z<br>z | Z<br>z |
| **B2** | X<br>x | X<br>x | X<br>x | X<br>x | C<br>c | X<br>x | X<br>x | C<br>c | C<br>c | X<br>x | X<br>x | X<br>x |
| **B3** | C<br>c | C<br>c | C<br>c | C<br>c | V<br>v | C<br>c | C<br>c | V<br>v | V<br>v | C<br>c | C<br>c | C<br>c |
| **B4** | V<br>v | V<br>v | V<br>v | V<br>v | B<br>b | V<br>v | V<br>v | B<br>b | B<br>b | V<br>v | V<br>v | V<br>v |
| **B5** | B<br>b | B<br>b | B<br>b | B<br>b | N<br>n | B<br>b | B<br>b | N<br>n | N<br>n | B<br>b | B<br>b | B<br>b |
| **B6** | N<br>n | N<br>n | N<br>n | N<br>n | M<br>m | N<br>n | N<br>n | M<br>m | M<br>m | N<br>n | N<br>n | N<br>n |
| **B7** | M<br>m | M<br>m | M<br>m | M<br>m | ,<br>< | ,<br>; | M<br>m | ,<br>< | ,<br>< | M<br>m | M<br>m | M<br>m |
| **B8** | ,<br>; | ,<br>; | ,<br>; | ,<br>; | .<br>> | .<br>: | ,<br>; | .<br>> | .<br>> | ,<br>; | ,<br>; | ,<br>; |
| **B9** | .<br>: | .<br>: | .<br>: | .<br>: | /<br>? | /<br>! | .<br>: | /<br>? | /<br>? | .<br>: | .<br>: | .<br>: |
| **B10** | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ | -<br>_ |
| **B11** | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT | SHIFT |
| **B47** | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT | LEFT |
| **B48** | HOME | HOME | HOME | HOME | HOME | HOME | HOME | HOME | HOME | HOME | HOME | HOME |
| **B49** | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT | RIGHT |

### Main Keyboard Area - Row A (Spacebar Row)

| Grid Pos | Norwegian | Danish | Swedish | German | US ASCII | French | SDS | English | FAO | Swiss | Finnish | Icelandic |
|----------|-----------|--------|---------|--------|----------|--------|-----|---------|-----|-------|---------|-----------|
| **A5** | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE | SPACE |
| **A47** | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| **A48** | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN | DOWN |
| **A49** | →\| | →\| | →\| | →\| | →\| | →\| | →\| | →\| | →\| | →\| | →\| | →\| |

### Numeric Pad Area (Columns 51-54)

| Grid Pos | Label | Function |
|----------|-------|----------|
| **G51** | FUNK | Function key |
| **G52** | SKRIV/PRINT | Print screen |
| **G53** | HJELP/HELP | Help |
| **G54** | SLUTT/EXIT | Exit |
| **F51** | PUSH SI | Push SI character |
| **F52** | HEX SO | Hex SO character |
| **F53** | CLEAR | Clear |
| **F54** | (empty) | Empty |
| **D51** | 7 | Numpad 7 |
| **D52** | 8 | Numpad 8 |
| **D53** | 9 | Numpad 9 |
| **D54** | - | Numpad minus |
| **C51** | 4 | Numpad 4 |
| **C52** | 5 | Numpad 5 |
| **C53** | 6 | Numpad 6 |
| **C54** | + | Numpad plus |
| **B51** | 1 | Numpad 1 |
| **B52** | 2 | Numpad 2 |
| **B53** | 3 | Numpad 3 |
| **B54** | ENTER | Numpad enter (tall) |
| **A51** | 0 | Numpad 0 (wide) |
| **A53** | . | Numpad decimal |
| **A54** | (part of B54) | Tall enter continuation |

## Special Key Properties

### Toggle Keys with LED Indicators

1. **C0 (LOCK)**: Caps lock key with LED indicator showing active state
2. **E0 (CAPS)**: Additional caps indication (depending on variant)

### Wide Keys

- **A5**: Spacebar (6x width)
- **A51**: Numpad 0 (2x width)
- **C0**: LOCK (1.5x width)
- **E0**: CAPS (1.5x width)
- **B99**: Left SHIFT (1.5x width)
- **B11**: Right SHIFT (1.5x width)
- **D13**: Filler key (1.5x width)

### Tall Keys

- **B54/A54**: Numpad ENTER (2x height)
- **C13**: RETURN (2x height in some variants)

## Key Color Coding

Based on physical keyboard photos (kbd-ND246-2.jpeg):

- **Beige/White**: Standard alphanumeric keys
- **Orange**: Special function keys (MODE, CTRL, INNS/EXPS, CAPS, @, special navigation keys, numeric pad operations)
- **Brown**: Navigation cluster (arrows, HOME, PG UP/DN, etc.)
- **LED Indicators**: Red dots on toggle keys (LOCK, CAPS)

## CSI Sequence Codes

Each key generates specific CSI (Control Sequence Introducer) sequences when pressed:

Format: `<1B 5B XX XX 5F>` (CSI nn _) where:
- `1B` = ESC
- `5B` = `[` (CSI introducer)
- `XX XX` = Two ASCII digit key identifier (00–87)
- `5F` = `_` (final byte)

**OCR correction (2026-02):** Original OCR misread `5B` as `58`, making sequences appear as `ESC X` instead of `ESC [` (CSI). The hex values below are corrected.

### Examples:

| Key | Unshifted | Shifted | Function |
|-----|-----------|---------|----------|
| **D99** | `<1B 5B 38 32 5F>` (CSI 82 _) | `<1B 5B 38 33 5F>` (CSI 83 _) | INNS/EXPS |
| **C99** | `<1B 5B 38 34 5F>` (CSI 84 _) | `<1B 5B 38 35 5F>` (CSI 85 _) | Squiggle (≈) |
| **D48** | `<1B 5B 33 30 5F>` (CSI 30 _) | `<1B 5B 33 31 5F>` (CSI 31 _) | ANGRE/CANCEL |

## Implementation Notes

### Current Code Status (as of latest commit)

The keyboard layout is implemented in:
- **File**: `src/RetroTerm.Desktop/Models/TDV2200KeyLayout.cs`
- **Enum**: `NationalKeyboardLayout` in `src/RetroTerm.Desktop/Models/KeyboardLayout.cs`

### Key Positions Implemented:

✅ **C99**: MODE (orange key, leftmost in C-row)
✅ **C0**: LOCK with LED (1.5x width, toggle key)
✅ **D99**: INNS/EXPS (orange key, leftmost in D-row)
✅ **D0**: CTRL (main keyboard area)
✅ **National character mappings**: Æ/Ø/Å (Nordic), Ä/Ö/Ü (German), etc.

### Areas for Improvement:

1. **Number row (E-row)**: Verify all shifted characters match specification
2. **B-row**: Verify < > positioning and AZERTY variants
3. **Special characters**: Verify § ¤ £ positions across all layouts
4. **CSI sequences**: Not yet implemented for special keys

## References

1. **TDV-2200/9 User's Guide (ND)**, Part no. 408356, Publication no. 5599, November 1984
   - Appendix A: Keyboard Key Numbering (Lines 1068-1080)
   - National Variations Table (Lines 296-350)
   - Extended Key Labels (Lines 362-408)
   - ASCII Character Variations (Lines 436-450)
   - CSI Sequences (Lines 835-943)

2. **Physical Keyboard Images**:
   - `spec/TDV2200/kbd-ND246-2.jpeg` - Norwegian ND 246 keyboard photo
   - `spec/TDV2200/kbd-ND246-1.jpeg` - Alternative view
   - `spec/TDV2200/kbd.png` - Schematic layout

3. **Implementation Files**:
   - `src/RetroTerm.Desktop/Models/TDV2200KeyLayout.cs` - Layout implementation
   - `src/RetroTerm.Desktop/Models/KeyboardLayout.cs` - Layout enum and definitions
   - `src/RetroTerm.Core/Terminal/Emulators/TDV/TDV2200ISO646Variant.cs` - ISO 646 variants

## Version History

- **2025-11-21**: Initial documentation created based on TDV-2200/9 User's Guide
- **2025-11-21**: Corrected C99, D99, D0 key positions based on Norwegian layout specification
- **2025-11-21**: Added comprehensive national variant mappings for all 13 supported layouts

---

**Document Path**: `docs\KBD-ND-246.md`
