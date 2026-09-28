
# VT100 and ND Terminal Escape Sequences

## 2.2 Three Character Escape Sequences

| Sequence             | Mnemonic | Interpretation                  |
|----------------------|----------|---------------------------------|
| ESC # <23> 3 <33>    | NDDHLT   | Double height line - top        |
| ESC # <23> 4 <34>    | NDDHLB   | Double height line - bottom     |
| ESC # <23> 5 <35>    | NDSWL    | Single width line               |
| ESC # <23> 6 <36>    | NDDWL    | Double width line               |
| ESC ( <28>           | -        | Designate G0 character set      |
| ESC ) <29>           | -        | Designate G1 character set      |

## 2.3 ND Private Escape Sequences

| Sequence           | Mnemonic | Interpretation                               |
|--------------------|----------|----------------------------------------------|
| ESC 0 <30>         |          |                                               |
| ESC 1 <31>         | NDSS1    | Single shift to alternative character set 1   |
| ESC 2 <32>         | NDSS2    | Single shift to alternative character set 2   |
| ESC 3 <33>         | NDSS3    | Single shift to alternative character set 3   |
| ESC 4 <34>         | NDSS4    | Single shift to alternative character set 4   |
| ESC 5 <35>         | NDSS5    | Single shift to alternative character set 5   |
| ESC 6 <36>         | NDSS6    | Single shift to alternative character set 6   |
| ESC 7 <37>         | NDSC     | Save cursor                                   |
| ESC 8 <38>         | NDRC     | Restore cursor                                |
| ESC 9 <39>         | NDSS7    | Single shift to alternative character set 7   |
| ESC : <3A>         | NDSS8    | Single shift to alternative character set 8   |
| ESC ; <3B>         | NDSS9    | Single shift to alternative character set 9   |
| ESC = <3D>         | NDEAKM   | Enter alternate keypad mode                   |
| ESC > <3E>         | NDXAKM   | Exit alternate keypad mode                    |

## 2.4 C1 Set

| Sequence         | Mnemonic | Interpretation                                   |
|------------------|----------|--------------------------------------------------|
| ESC D <44>       | IND      | Index                                            |
| ESC E <45>       | NEL      | Newline                                          |
| ESC H <48>       | HTS      | Horizontal tab set                               |
| ESC M <4D>       | RI       | Reverse index                                    |
| ESC N <4E>       | SS2      | Single shift 2                                   |
| ESC O <4F>       | SS3      | Single shift 3                                   |
| ESC P <50>       | DCS      | Device control string                            |
| ESC [ <5B>       | CSI      | Control sequence introducer                      |
| ESC \ <5C>      | ST       | String terminator (used only with DCS)           |

## 2.5 Control Functions Represented by ESC Fs Sequences

| Sequence        | Mnemonic | Interpretation          |
|-----------------|----------|-------------------------|
| ESC c <63>      | RIS      | Reset to initial state  |
| ESC n <6E>      | LS2      | Locking shift 2         |
| ESC o <6F>      | LS3      | Locking shift 3         |

## 2.6 CSI Sequence Parameter Rules

- `n`: numeric character, ASCII coded 1–255  
- `l`: numeric character, ASCII coded 1–(number of lines)  
- `c`: numeric character, ASCII coded 1–(number of columns)  
- `s`: special parameter, ASCII coded as specified  
- `ms`: multiple selective parameters separated by semicolon

## 2.7 CSI Sequences with a Single Intermediate Character

| Sequence      | Hex        | Interpretation     |
|---------------|------------|--------------------|
| CSI n <SP> @  | <20> <40>  | SL Scroll left     |
| CSI n <SP> A  | <20> <41>  | SR Scroll right    |

## 2.8 ND Private CSI Sequences

| Sequence     | Hex  | Mnemonic  | Interpretation                         |
|--------------|------|-----------|----------------------------------------|
| CSI n;m p    | <70> | NDLIWA    | Insert lines in work area              |
| CSI n;m q    | <71> | NDDLWA    | Delete lines in work area              |
| CSI n;m r    | <72> | NDSTBM    | Set top and bottom margin              |
| CSI n;m s    | <73> | NDICHE    | Insert characters with extent          |
| CSI n;m t    | <74> | NDDCHE    | Delete characters with extent          |
| CSI ms u     | <75> | NDSREC    | Save rectangle                         |
| CSI ms v     | <76> | NDRREC    | Restore rectangle                      |
| CSI s w      | <77> | NDSSKL    | Set soft-key level                     |
| CSI ms x     | <78> | NDREQ     | Request and report terminal parameters |
| CSI 2;s y    | <79> | NDTST     | Invoke confidence test                 |
| CSI ms z     | <7A> | NDSAR     | Set attribute in rectangle             |
| CSI ms {     | <7B> | NDAAR     | Add attribute in rectangle             |
| CSI ms \|    | <7C> | NDRAR     | Remove attribute in rectangle          |
| CSI ms }     | <7D> | NDFC      | Fill character(s) in rectangle         |
| CSI ms ~     | <7E> | NDWA      | Define work area                       |
| CSI n/p      | <7F> | NDVIDEO   | Alpha video on/off                     |

## 2.9 ND Private CSI Sequences with Reserved `pl`

- `CSI < ms` — Reserved  
- `CSI = ms` — Reserved  
- `CSI > ms` — Reserved  

| Sequence     | Hex   | Mnemonic | Interpretation            |
|--------------|-------|----------|---------------------------|
| CSI ? ms A   | <41>  | NDCLED   | Clear message LED(s)      |
| CSI ? ms B   | <42>  | NDSLED   | Set message LED(s)        |
| CSI ? ms C   | <43>  | NDBLED   | Blink message LED(s)      |
