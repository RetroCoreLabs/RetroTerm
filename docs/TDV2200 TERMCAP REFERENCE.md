# TANDBERG TDV2200 TERMCAP REFERENCE

***NOTE! Created by ChatGPT based on TermCap definitons***

# Table of Contents

## Part 1 – TDV2200 Termcap Analysis (Capabilities al–ku)
- Original TDV2200 Termcap Entry
- al – Insert Line
- AL – Insert Multiple Lines
- am – Automatic Margins
- bs – Backspace Support
- cd – Clear to End of Screen
- ce – Clear to End of Line
- cm – Cursor Motion
- cl – Clear Screen
- PN / PS – Printer Control
- co – Columns
- dc / DC – Delete Character(s)
- dl / DL – Delete Line(s)
- do – Cursor Down
- ei / im – Insert Mode Handling
- ho – Home Cursor
- ic / IC – Insert Character(s)
- is – Initialization String
- kb / kd / kh / kl / kr / ku – Keyboard Navigation

## Part 2 – TDV2200 Remaining Capabilities
- li – Screen Lines
- mi – Cursor Motion During Insert
- nd – Cursor Right
- se / so – Standout Mode
- sf / SF – Scroll Forward
- sr – Reverse Scroll
- ue / us – Underline
- ve / vs – Visual Emphasis
- TDV Graphics Commands (GC, GH, GL, GR, G1–G4, GU, GD, GV)
- EN – TDV Private Mode
- PU / PD – Page Navigation
- Function Keys (k0–k8)
- Soft Label Keys (l0–l7)

## Part 3 – TDV1200 Termcap Analysis
- Original TDV1200 Entry
- General Screen Control
- Initialization Sequence
- Function Keys k1–k8
- Keyboard Differences from TDV2200

## Part 4 – TDV1200 Remaining Capabilities
- Soft Label Keys l0–l7
- Cursor Keys
- Screen Attributes
- Graphics Commands
- Printer Support
- TDV Extensions

## Part 5 – Norsk Data Terminal Entries
- ND224
- ND242
- ND246
- ND320
- Inheritance Analysis
- Comparison Table

## Part 6 – TDV2200 Terminfo Overview
- General Terminal Capabilities
- Display Dimensions
- Cursor Movement
- Editing Functions
- Screen Control

## Part 7 – TDV2200 Keyboard Architecture
- Function Keys kf0–kf20
- Command Keys
- Copy / Move / Print / Help
- Office Automation Functions

## Part 8 – Extended Function Keys and Editing
- kf21–kf50
- Editing Keys
- Navigation Keys
- Option Keys
- Begin / End Functions

## Part 9 – Navigation and Application Control
- Cursor Keys
- Home Key
- Page Navigation
- Help / Exit / Print
- Copy / Move Functions
- Keyboard Grouping Analysis

## Part 10 – Display Attributes and TDV Modes
- Reverse Video
- Underline
- Blink
- Dim
- Tab Control
- Printer Support
- Graphics System
- Private Modes
- Initialization Sequences

## Part 11 – Extended Function Keys and Soft Labels
- kf51–kf63
- Soft Label Architecture
- Keyboard Matrix
- VT100 Comparison

## Part 12 – Alternative Definitions and Emulator Notes
- Alternative TDV2200 Definitions
- TDV1200 vs TDV2200
- Termcap ↔ Terminfo Cross Reference
- Emulator Design Guidance

## Part 13 – TDV1200 Extended Keyboard
- Cursor Control
- Editing Functions
- Forms Navigation
- Application Control
- Printer Operations

## Part 14 – TDV1200 Function Key Matrix
- Function Key Mapping
- Soft Labels
- Firmware Modes
- TDV1200 vs TDV2200 Comparison

## Part 15 – ND246 Terminfo Analysis
- Binary Cursor Protocol
- Visual Bell Protocol
- TDV2200 Inheritance
- Compatibility Behavior
- Emulator Implications

## Part 16 – ND246 and ND320 Analysis
- ND246 Keyboard Behavior
- ND246 Visual Bell Analysis
- ND320 Analysis
- Terminal Family Evolution
- Complete Family Comparison

## Part 17 – TDV Escape Sequence Reverse Engineering
- TDV Key Encoding Scheme
- Function-Key Namespace
- Graphics Commands
- Private Modes
- Keyboard Reconstruction Strategy

## Part 18 – Keyboard Reconstruction and NOTIS Context
- Reconstructed Keyboard Layout
- Office Automation Keys
- Editing Cluster
- Navigation Cluster
- Emulator Checklist
- Open Questions

## Part 19 – Termcap and Terminfo Syntax Reference
- Capability Types
- Escape Sequence Syntax
- Control Character Notation
- Formatting Operators
- Inheritance Mechanisms
- Runtime Usage

## Part 20 – Proven Facts vs Hypotheses
- Evidence Classification
- What Is Proven
- What Is Strong Evidence
- What Is Hypothesis
- What Remains Unknown
- Recommended Research Sources

## Part 1 - Complete TDV2200 Termcap Analysis (Capabilities al through ku)

# Original TDV2200 Termcap Entry

```termcap
t1|tdv2200|Tandberg TDV2200/9S:\
        :al=\E[L:AL=\E[%dL:am:\
        :bs:cd=\E[J:ce=\E[K:cm=\E[%i%d;%dH:\
        :cl=\E[H\E[2J:PN=\E[5i:PS=\E[4i:\
        :co#80:dc=\E[P:DC=\E[%dP:dl=\E[M:DL=\E[%dM:\
        :do=\E[B:ei=:ho=\E[H:ic=\E[@:IC=\E[%d@:im=:\
        :is=\E[62;36;66l\EQ\E[36;62;62h\E[0m:\
        :kb=^H:kd=^K:kh=^]:kl=^H:kr=^X:ku=^\:\
        :li#25:mi:nd=\E[C:se=\E[0m:sf=\E[S:SF=\E[%dS:\
        :so=\E[7m:sr=\E[T:ue=\E[0m:up=\E[A:us=\E[4m:\
        :ve=\E[0m:vs=\E[2m:\
        :GC=\E15:GH=\E10:GL=\E14:GR=\E16:G1=\E19:G2=\E17:\
        :G3=\E11:G4=\E13:GU=\E12:GD=\E18:GV=\E1.:\
        :EN=\E[=C:PU=\E[S:PD=\E[T:\
        :k0=\E[50_:k1=\E[50_:k2=\E[52_:k3=\E[55_:k4=\E[58_:\
        :k5=\E[60_:k6=\E[62_:k7=\E[64_:k8=\E[66_:\
        :l0=\E[51_:l1=\E[53_:l2=\E[56_:l3=\E[59_:l4=\E[61_:l5=\E[63_:\
        :l6=\E[65_:l7=\E[67_:
```

---

# Capability: al

Original:

```termcap
al=\E[L
```

| Property        | Value           |
| --------------- | --------------- |
| Capability      | al              |
| Type            | String          |
| Meaning         | Insert one line |
| Sequence        | ESC[L           |
| Hex             | 1B 5B 4C        |
| ANSI Name       | IL              |
| ANSI Equivalent | Yes             |

### Detailed Description

Inserts a blank line at the current cursor row.

Lines below are pushed downward.

---

# Capability: AL

Original:

```termcap
AL=\E[%dL
```

| Property        | Value          |
| --------------- | -------------- |
| Capability      | AL             |
| Type            | String         |
| Meaning         | Insert N lines |
| Sequence        | ESC[nL         |
| Parameter       | %d             |
| ANSI Name       | IL             |
| ANSI Equivalent | Yes            |

### Example

```text
ESC[5L
```

Insert five lines.

---

# Capability: am

Original:

```termcap
am
```

| Property   | Value                      |
| ---------- | -------------------------- |
| Capability | am                         |
| Type       | Boolean                    |
| Meaning    | Automatic margins          |
| Effect     | Cursor wraps at right edge |

### Detailed Description

When column 80 is reached the cursor automatically wraps to the next line.

---

# Capability: bs

Original:

```termcap
bs
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | bs                  |
| Type       | Boolean             |
| Meaning    | Backspace supported |
| Character  | BS                  |
| ASCII      | 8                   |
| Hex        | 08                  |

---

# Capability: cd

Original:

```termcap
cd=\E[J
```

| Property        | Value                  |
| --------------- | ---------------------- |
| Capability      | cd                     |
| Meaning         | Clear to end of screen |
| Sequence        | ESC[J                  |
| Hex             | 1B 5B 4A               |
| ANSI Name       | ED                     |
| ANSI Compatible | Yes                    |

### Detailed Description

Erases from current cursor position to end of display.

---

# Capability: ce

Original:

```termcap
ce=\E[K
```

| Property   | Value                |
| ---------- | -------------------- |
| Capability | ce                   |
| Meaning    | Clear to end of line |
| Sequence   | ESC[K                |
| Hex        | 1B 5B 4B             |
| ANSI Name  | EL                   |

---

# Capability: cm

Original:

```termcap
cm=\E[%i%d;%dH
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | cm              |
| Meaning    | Cursor motion   |
| ANSI Name  | CUP             |
| Format     | ESC[row;columnH |
| Parameter  | %i              |
| Parameter  | %d              |

### Detailed Description

Primary cursor positioning capability.

All screen-oriented applications rely on this capability.

### Example

```text
ESC[10;20H
```

Moves cursor to row 10 column 20.

---

# Capability: cl

Original:

```termcap
cl=\E[H\E[2J
```

| Property        | Value        |
| --------------- | ------------ |
| Capability      | cl           |
| Meaning         | Clear screen |
| Sequence        | ESC[H ESC[2J |
| ANSI Compatible | Yes          |

### Hex

```hex
1B 5B 48
1B 5B 32 4A
```

---

# Capability: PN

Original:

```termcap
PN=\E[5i
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | PN          |
| Meaning    | Printer off |
| ANSI Name  | MC5         |
| Sequence   | ESC[5i      |

### Detailed Description

Stops printer logging mode.

---

# Capability: PS

Original:

```termcap
PS=\E[4i
```

| Property   | Value      |
| ---------- | ---------- |
| Capability | PS         |
| Meaning    | Printer on |
| ANSI Name  | MC4        |
| Sequence   | ESC[4i     |

### Detailed Description

Starts printer logging mode.

---

# Capability: co

Original:

```termcap
co#80
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | co                |
| Type       | Numeric           |
| Value      | 80                |
| Meaning    | Number of columns |

---

# Capability: dc

Original:

```termcap
dc=\E[P
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | dc               |
| Meaning    | Delete character |
| ANSI Name  | DCH              |
| Sequence   | ESC[P            |

### Detailed Description

Deletes one character at cursor position.

Remaining characters shift left.

---

# Capability: DC

Original:

```termcap
DC=\E[%dP
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | DC                  |
| Meaning    | Delete N characters |
| ANSI Name  | DCH                 |
| Parameter  | %d                  |

---

# Capability: dl

Original:

```termcap
dl=\E[M
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | dl          |
| Meaning    | Delete line |
| ANSI Name  | DL          |
| Sequence   | ESC[M       |

---

# Capability: DL

Original:

```termcap
DL=\E[%dM
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | DL             |
| Meaning    | Delete N lines |
| ANSI Name  | DL             |
| Parameter  | %d             |

---

# Capability: do

Original:

```termcap
do=\E[B
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | do          |
| Meaning    | Cursor down |
| ANSI Name  | CUD         |
| Sequence   | ESC[B       |

---

# Capability: ei

Original:

```termcap
ei=
```

| Property   | Value                                                |
| ---------- | ---------------------------------------------------- |
| Capability | ei                                                   |
| Meaning    | End insert mode                                      |
| Sequence   | Empty                                                |
| Notes      | TDV2200 does not require explicit end-insert command |

---

# Capability: ho

Original:

```termcap
ho=\E[H
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | ho          |
| Meaning    | Home cursor |
| Sequence   | ESC[H       |
| ANSI Name  | CUP         |

---

# Capability: ic

Original:

```termcap
ic=\E[@
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | ic               |
| Meaning    | Insert character |
| ANSI Name  | ICH              |
| Sequence   | ESC[@            |

---

# Capability: IC

Original:

```termcap
IC=\E[%d@
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | IC                  |
| Meaning    | Insert N characters |
| ANSI Name  | ICH                 |
| Parameter  | %d                  |

---

# Capability: im

Original:

```termcap
im=
```

| Property   | Value                                                          |
| ---------- | -------------------------------------------------------------- |
| Capability | im                                                             |
| Meaning    | Enter insert mode                                              |
| Sequence   | Empty                                                          |
| Notes      | TDV2200 performs insertion directly without modal insert state |

---

# Capability: is

Original:

```termcap
is=\E[62;36;66l\EQ\E[36;62;62h\E[0m
```

| Property        | Value                 |
| --------------- | --------------------- |
| Capability      | is                    |
| Meaning         | Initialization string |
| Executed        | Terminal startup      |
| ANSI Compatible | Partially             |

### Breakdown

| Sequence      | Description               |
| ------------- | ------------------------- |
| ESC[62;36;66l | Disable private TDV modes |
| ESCQ          | Proprietary TDV command   |
| ESC[36;62;62h | Enable TDV modes          |
| ESC[0m        | Reset display attributes  |

---

# Capability: kb

Original:

```termcap
kb=^H
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | kb            |
| Meaning    | Backspace key |
| Character  | ^H            |
| ASCII      | 8             |
| Hex        | 08            |

---

# Capability: kd

Original:

```termcap
kd=^K
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kd              |
| Meaning    | Cursor down key |
| Character  | ^K              |
| ASCII      | 11              |
| Hex        | 0B              |

---

# Capability: kh

Original:

```termcap
kh=^]
```

| Property   | Value    |
| ---------- | -------- |
| Capability | kh       |
| Meaning    | Home key |
| Character  | ^]       |
| ASCII      | 29       |
| Hex        | 1D       |

---

# Capability: kl

Original:

```termcap
kl=^H
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kl          |
| Meaning    | Cursor left |
| Character  | ^H          |
| ASCII      | 8           |
| Hex        | 08          |

---

# Capability: kr

Original:

```termcap
kr=^X
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | kr           |
| Meaning    | Cursor right |
| Character  | ^X           |
| ASCII      | 24           |
| Hex        | 18           |

---

# Capability: ku

Original:

```termcap
ku=^\
```

| Property   | Value     |
| ---------- | --------- |
| Capability | ku        |
| Meaning    | Cursor up |
| Character  | ^\        |
| ASCII      | 28        |
| Hex        | 1C        |

---

# END OF PART 1
# TANDBERG TDV2200 TERMCAP REFERENCE

## Part 2 - Remaining TDV2200 Capabilities

---

# Capability: li

Original:

```termcap
li#25
```

| Property         | Value                   |
| ---------------- | ----------------------- |
| Capability       | li                      |
| Type             | Numeric                 |
| Value            | 25                      |
| Meaning          | Number of display lines |
| Physical Display | 25 rows                 |

### Detailed Description

The TDV2200 provides a standard 80x25 display.

Applications such as NOTIS assume 25 visible rows.

---

# Capability: mi

Original:

```termcap
mi
```

| Property        | Value                                  |
| --------------- | -------------------------------------- |
| Capability      | mi                                     |
| Type            | Boolean                                |
| Meaning         | Cursor motion works during insert mode |
| ANSI Equivalent | N/A                                    |

### Detailed Description

Some terminals prohibit cursor movement while insert mode is active.

TDV2200 allows cursor motion without leaving insert state.

---

# Capability: nd

Original:

```termcap
nd=\E[C
```

| Property   | Value                 |
| ---------- | --------------------- |
| Capability | nd                    |
| Meaning    | Non-destructive space |
| ANSI Name  | CUF                   |
| Sequence   | ESC[C                 |
| Hex        | 1B 5B 43              |

### Detailed Description

Moves cursor one column right without changing screen contents.

---

# Capability: se

Original:

```termcap
se=\E[0m
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | se                |
| Meaning    | End standout mode |
| ANSI Name  | SGR0              |
| Sequence   | ESC[0m            |
| Hex        | 1B 5B 30 6D       |

### Detailed Description

Restores normal display after reverse-video highlighting.

---

# Capability: sf

Original:

```termcap
sf=\E[S
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | sf             |
| Meaning    | Scroll forward |
| ANSI Name  | SU             |
| Sequence   | ESC[S          |
| Hex        | 1B 5B 53       |

### Detailed Description

Scrolls screen upward by one line.

---

# Capability: SF

Original:

```termcap
SF=\E[%dS
```

| Property   | Value                  |
| ---------- | ---------------------- |
| Capability | SF                     |
| Meaning    | Scroll forward N lines |
| ANSI Name  | SU                     |
| Parameter  | %d                     |
| Example    | ESC[10S                |

---

# Capability: so

Original:

```termcap
so=\E[7m
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | so                  |
| Meaning    | Start standout mode |
| ANSI Name  | Reverse Video       |
| Sequence   | ESC[7m              |

### Detailed Description

One of the most heavily used attributes in NOTIS.

Menus, selections and highlighted fields typically use standout mode.

---

# Capability: sr

Original:

```termcap
sr=\E[T
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | sr             |
| Meaning    | Reverse scroll |
| ANSI Name  | SD             |
| Sequence   | ESC[T          |

### Detailed Description

Scrolls screen contents downward.

Opposite of `sf`.

---

# Capability: ue

Original:

```termcap
ue=\E[0m
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | ue            |
| Meaning    | End underline |
| ANSI Name  | SGR0          |
| Sequence   | ESC[0m        |

---

# Capability: up

Original:

```termcap
up=\E[A
```

| Property   | Value     |
| ---------- | --------- |
| Capability | up        |
| Meaning    | Cursor up |
| ANSI Name  | CUU       |
| Sequence   | ESC[A     |
| Hex        | 1B 5B 41  |

---

# Capability: us

Original:

```termcap
us=\E[4m
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | us              |
| Meaning    | Start underline |
| ANSI Name  | Underline       |
| Sequence   | ESC[4m          |

### Detailed Description

Displays following text underlined until `ue`.

---

# Capability: ve

Original:

```termcap
ve=\E[0m
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | ve                  |
| Meaning    | End visual emphasis |
| ANSI Name  | SGR0                |
| Sequence   | ESC[0m              |

---

# Capability: vs

Original:

```termcap
vs=\E[2m
```

| Property   | Value                 |
| ---------- | --------------------- |
| Capability | vs                    |
| Meaning    | Start visual emphasis |
| ANSI Name  | Dim                   |
| Sequence   | ESC[2m                |

### Detailed Description

Places terminal into dim display mode.

Not all emulators implement this correctly.

---

# Capability: GC

Original:

```termcap
GC=\E15
```

| Property        | Value            |
| --------------- | ---------------- |
| Capability      | GC               |
| Meaning         | Graphics command |
| Type            | Proprietary TDV  |
| ANSI Equivalent | None             |

### Notes

Part of TDV graphics subsystem.

Requires firmware documentation for precise interpretation.

---

# Capability: GH

Original:

```termcap
GH=\E10
```

| Property       | Value           |
| -------------- | --------------- |
| Capability     | GH              |
| Likely Meaning | Horizontal line |
| Type           | TDV graphics    |

---

# Capability: GL

Original:

```termcap
GL=\E14
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GL           |
| Likely Meaning | Left border  |
| Type           | TDV graphics |

---

# Capability: GR

Original:

```termcap
GR=\E16
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GR           |
| Likely Meaning | Right border |
| Type           | TDV graphics |

---

# Capability: G1

Original:

```termcap
G1=\E19
```

| Property       | Value             |
| -------------- | ----------------- |
| Capability     | G1                |
| Likely Meaning | Upper-left corner |
| Type           | TDV graphics      |

---

# Capability: G2

Original:

```termcap
G2=\E17
```

| Property       | Value              |
| -------------- | ------------------ |
| Capability     | G2                 |
| Likely Meaning | Upper-right corner |
| Type           | TDV graphics       |

---

# Capability: G3

Original:

```termcap
G3=\E11
```

| Property       | Value             |
| -------------- | ----------------- |
| Capability     | G3                |
| Likely Meaning | Lower-left corner |
| Type           | TDV graphics      |

---

# Capability: G4

Original:

```termcap
G4=\E13
```

| Property       | Value              |
| -------------- | ------------------ |
| Capability     | G4                 |
| Likely Meaning | Lower-right corner |
| Type           | TDV graphics       |

---

# Capability: GU

Original:

```termcap
GU=\E12
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GU           |
| Likely Meaning | Upper border |
| Type           | TDV graphics |

---

# Capability: GD

Original:

```termcap
GD=\E18
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GD           |
| Likely Meaning | Lower border |
| Type           | TDV graphics |

---

# Capability: GV

Original:

```termcap
GV=\E1.
```

| Property       | Value           |
| -------------- | --------------- |
| Capability     | GV              |
| Likely Meaning | Vertical border |
| Type           | TDV graphics    |

---

# Capability: EN

Original:

```termcap
EN=\E[=C
```

| Property        | Value                   |
| --------------- | ----------------------- |
| Capability      | EN                      |
| Meaning         | Proprietary TDV command |
| Sequence        | ESC[=C                  |
| ANSI Equivalent | None                    |

### Notes

Related to extended TDV operating modes.

---

# Capability: PU

Original:

```termcap
PU=\E[S
```

| Property   | Value   |
| ---------- | ------- |
| Capability | PU      |
| Meaning    | Page Up |
| Sequence   | ESC[S   |

---

# Capability: PD

Original:

```termcap
PD=\E[T
```

| Property   | Value     |
| ---------- | --------- |
| Capability | PD        |
| Meaning    | Page Down |
| Sequence   | ESC[T     |

---

# Function Keys

## Capability: k0

Original:

```termcap
k0=\E[50_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k0             |
| Meaning    | Function Key 0 |
| Sequence   | ESC[50_        |

---

## Capability: k1

Original:

```termcap
k1=\E[50_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k1             |
| Meaning    | Function Key 1 |
| Sequence   | ESC[50_        |

---

## Capability: k2

Original:

```termcap
k2=\E[52_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k2             |
| Meaning    | Function Key 2 |
| Sequence   | ESC[52_        |

---

## Capability: k3

Original:

```termcap
k3=\E[55_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k3             |
| Meaning    | Function Key 3 |
| Sequence   | ESC[55_        |

---

## Capability: k4

Original:

```termcap
k4=\E[58_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k4             |
| Meaning    | Function Key 4 |
| Sequence   | ESC[58_        |

---

## Capability: k5

Original:

```termcap
k5=\E[60_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k5             |
| Meaning    | Function Key 5 |
| Sequence   | ESC[60_        |

---

## Capability: k6

Original:

```termcap
k6=\E[62_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k6             |
| Meaning    | Function Key 6 |
| Sequence   | ESC[62_        |

---

## Capability: k7

Original:

```termcap
k7=\E[64_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k7             |
| Meaning    | Function Key 7 |
| Sequence   | ESC[64_        |

---

## Capability: k8

Original:

```termcap
k8=\E[66_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k8             |
| Meaning    | Function Key 8 |
| Sequence   | ESC[66_        |

---

# Soft Label Keys

## Capability: l0

```termcap
l0=\E[51_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l0               |
| Meaning    | Soft Label Key 0 |
| Sequence   | ESC[51_          |

## Capability: l1

```termcap
l1=\E[53_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l1               |
| Meaning    | Soft Label Key 1 |
| Sequence   | ESC[53_          |

## Capability: l2

```termcap
l2=\E[56_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l2               |
| Meaning    | Soft Label Key 2 |
| Sequence   | ESC[56_          |

## Capability: l3

```termcap
l3=\E[59_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l3               |
| Meaning    | Soft Label Key 3 |
| Sequence   | ESC[59_          |

## Capability: l4

```termcap
l4=\E[61_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l4               |
| Meaning    | Soft Label Key 4 |
| Sequence   | ESC[61_          |

## Capability: l5

```termcap
l5=\E[63_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l5               |
| Meaning    | Soft Label Key 5 |
| Sequence   | ESC[63_          |

## Capability: l6

```termcap
l6=\E[65_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l6               |
| Meaning    | Soft Label Key 6 |
| Sequence   | ESC[65_          |

## Capability: l7

```termcap
l7=\E[67_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l7               |
| Meaning    | Soft Label Key 7 |
| Sequence   | ESC[67_          |

# END OF TDV2200 TERMCAP

NEXT PART:
TDV1200 TERMCAP — capability-by-capability using the exact same format.

# TANDBERG TDV1200 TERMCAP REFERENCE

## Part 3 - TDV1200 Termcap Analysis

# Original TDV1200 Termcap Entry

```termcap
t2|tdv1200|Tandberg TDV1200:\
        :al=\E[L:AL=\E[%dL:am:\
        :bs:cd=\E[J:ce=\E[K:cm=\E[%i%d;%dH:\
        :cl=\E[H\E[2J:PN=\E[5i:PS=\E[4i:\
        :co#80:dc=\E[P:DC=\E[%dP:dl=\E[M:DL=\E[%dM:\
        :do=\E[B:ei=:ho=\E[H:ic=\E[@:IC=\E[%d@:im=:\
        :is=\E[>6;8;9l\EQ\E[66h\E[1Q\E[0m:\
        :k1=\E[M:k2=\E[L:k3=\E[=F:k4=\E[=G:\
        :k5=\EOP:k6=\EOQ:k7=\EOR:k8=\EOS:\
        :l0=\E[=d:l1=\E[=e:l2=\E[=f:l3=\E[=g:l4=\E[=h:l5=\E[=i:\
        :l6=\E[=j:l7=\E[=k:kb=\E[D:kd=\E[B:\
        :kh=\E[H:kl=\E[D:kr=\E[C:ku=\E[A:\
        :li#25:mi:nd=\E[C:se=\E[0m:sf=\E[S:SF=\E[%dS:\
        :so=\E[7m:sr=\E[T:ue=\E[0m:up=\E[A:us=\E[4m:\
        :ve=\E[0m:vs=\E[2m:\
        :GC=\E15:GH=\E10:GL=\E14:GR=\E16:G1=\E19:G2=\E17:\
        :G3=\E11:G4=\E13:GU=\E12:GD=\E18:GV=\E1.:\
        :EN=\E[=C:PU=\E[S:PD=\E[T:
```

---

# Overview

The TDV1200 and TDV2200 share most screen-management functionality.

Major differences are:

1. Different initialization string
2. Different keyboard encoding
3. Different function key assignments
4. Different soft-label assignments
5. Slightly different operating mode commands

Most screen-oriented software can use either terminal with only keyboard mappings changing.

---

# Capability: al

Original:

```termcap
al=\E[L
```

| Property        | Value           |
| --------------- | --------------- |
| Capability      | al              |
| Meaning         | Insert one line |
| Sequence        | ESC[L           |
| Hex             | 1B 5B 4C        |
| ANSI Equivalent | IL              |

### Description

Insert one blank line at cursor position.

---

# Capability: AL

Original:

```termcap
AL=\E[%dL
```

| Property        | Value                 |
| --------------- | --------------------- |
| Capability      | AL                    |
| Meaning         | Insert multiple lines |
| Sequence        | ESC[nL                |
| ANSI Equivalent | IL                    |
| Parameter       | %d                    |

---

# Capability: am

Original:

```termcap
am
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | am                |
| Type       | Boolean           |
| Meaning    | Automatic margins |

### Description

Cursor wraps automatically at end of line.

---

# Capability: bs

Original:

```termcap
bs
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | bs                  |
| Meaning    | Backspace supported |
| ASCII      | 08                  |

---

# Capability: cd

Original:

```termcap
cd=\E[J
```

| Property   | Value                  |
| ---------- | ---------------------- |
| Capability | cd                     |
| Meaning    | Clear to end of screen |
| ANSI Name  | ED                     |
| Sequence   | ESC[J                  |

---

# Capability: ce

Original:

```termcap
ce=\E[K
```

| Property   | Value                |
| ---------- | -------------------- |
| Capability | ce                   |
| Meaning    | Clear to end of line |
| ANSI Name  | EL                   |
| Sequence   | ESC[K                |

---

# Capability: cm

Original:

```termcap
cm=\E[%i%d;%dH
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | cm            |
| Meaning    | Cursor motion |
| ANSI Name  | CUP           |
| Sequence   | ESC[row;colH  |

### Example

```text
ESC[5;10H
```

Moves to row 5 column 10.

---

# Capability: cl

Original:

```termcap
cl=\E[H\E[2J
```

| Property        | Value        |
| --------------- | ------------ |
| Capability      | cl           |
| Meaning         | Clear screen |
| Sequence        | ESC[H ESC[2J |
| ANSI Compatible | Yes          |

---

# Capability: PN

Original:

```termcap
PN=\E[5i
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | PN          |
| Meaning    | Printer off |
| Sequence   | ESC[5i      |

---

# Capability: PS

Original:

```termcap
PS=\E[4i
```

| Property   | Value      |
| ---------- | ---------- |
| Capability | PS         |
| Meaning    | Printer on |
| Sequence   | ESC[4i     |

---

# Capability: co

Original:

```termcap
co#80
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | co           |
| Value      | 80           |
| Meaning    | Screen width |

---

# Capability: dc

Original:

```termcap
dc=\E[P
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | dc               |
| Meaning    | Delete character |
| ANSI Name  | DCH              |
| Sequence   | ESC[P            |

---

# Capability: DC

Original:

```termcap
DC=\E[%dP
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | DC                  |
| Meaning    | Delete N characters |
| ANSI Name  | DCH                 |
| Parameter  | %d                  |

---

# Capability: dl

Original:

```termcap
dl=\E[M
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | dl          |
| Meaning    | Delete line |
| ANSI Name  | DL          |

---

# Capability: DL

Original:

```termcap
DL=\E[%dM
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | DL             |
| Meaning    | Delete N lines |
| ANSI Name  | DL             |
| Parameter  | %d             |

---

# Capability: do

Original:

```termcap
do=\E[B
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | do          |
| Meaning    | Cursor down |
| ANSI Name  | CUD         |
| Sequence   | ESC[B       |

---

# Capability: ei

Original:

```termcap
ei=
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | ei              |
| Meaning    | End insert mode |
| Sequence   | Empty           |

### Notes

TDV1200 does not require a dedicated exit-insert command.

---

# Capability: ho

Original:

```termcap
ho=\E[H
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | ho          |
| Meaning    | Home cursor |
| Sequence   | ESC[H       |

---

# Capability: ic

Original:

```termcap
ic=\E[@
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | ic               |
| Meaning    | Insert character |
| ANSI Name  | ICH              |
| Sequence   | ESC[@            |

---

# Capability: IC

Original:

```termcap
IC=\E[%d@
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | IC                  |
| Meaning    | Insert N characters |
| ANSI Name  | ICH                 |
| Parameter  | %d                  |

---

# Capability: im

Original:

```termcap
im=
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | im                |
| Meaning    | Enter insert mode |
| Sequence   | Empty             |

---

# Capability: is

Original:

```termcap
is=\E[>6;8;9l\EQ\E[66h\E[1Q\E[0m
```

| Property   | Value                    |
| ---------- | ------------------------ |
| Capability | is                       |
| Meaning    | Initialization string    |
| Type       | Proprietary TDV sequence |

## Breakdown

| Sequence    | Description      |
| ----------- | ---------------- |
| ESC[>6;8;9l | Disable modes    |
| ESCQ        | TDV command      |
| ESC[66h     | Enable mode 66   |
| ESC[1Q      | TDV command      |
| ESC[0m      | Reset attributes |

### Notes

Different from TDV2200 initialization.

Indicates firmware-level differences between the terminal generations.

---

# Capability: k1

Original:

```termcap
k1=\E[M
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k1             |
| Meaning    | Function Key 1 |
| Sequence   | ESC[M          |

---

# Capability: k2

Original:

```termcap
k2=\E[L
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k2             |
| Meaning    | Function Key 2 |
| Sequence   | ESC[L          |

---

# Capability: k3

Original:

```termcap
k3=\E[=F
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k3             |
| Meaning    | Function Key 3 |
| Sequence   | ESC[=F         |

---

# Capability: k4

Original:

```termcap
k4=\E[=G
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k4             |
| Meaning    | Function Key 4 |
| Sequence   | ESC[=G         |

---

# Capability: k5

Original:

```termcap
k5=\EOP
```

| Property   | Value                 |
| ---------- | --------------------- |
| Capability | k5                    |
| Meaning    | Function Key 5        |
| Sequence   | ESCOP                 |
| Notes      | DEC-style PF sequence |

---

# Capability: k6

Original:

```termcap
k6=\EOQ
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k6             |
| Meaning    | Function Key 6 |
| Sequence   | ESCOQ          |

---

# Capability: k7

Original:

```termcap
k7=\EOR
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k7             |
| Meaning    | Function Key 7 |
| Sequence   | ESCOR          |

---

# Capability: k8

Original:

```termcap
k8=\EOS
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | k8             |
| Meaning    | Function Key 8 |
| Sequence   | ESCOS          |

# END OF PART 3
# TANDBERG TDV1200 TERMCAP REFERENCE

## Part 4 - Remaining TDV1200 Capabilities

---

# Capability: l0

Original:

```termcap
l0=\E[=d
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l0               |
| Meaning    | Soft Label Key 0 |
| Sequence   | ESC[=d           |
| Type       | TDV proprietary  |

---

# Capability: l1

Original:

```termcap
l1=\E[=e
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l1               |
| Meaning    | Soft Label Key 1 |
| Sequence   | ESC[=e           |

---

# Capability: l2

Original:

```termcap
l2=\E[=f
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l2               |
| Meaning    | Soft Label Key 2 |
| Sequence   | ESC[=f           |

---

# Capability: l3

Original:

```termcap
l3=\E[=g
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l3               |
| Meaning    | Soft Label Key 3 |
| Sequence   | ESC[=g           |

---

# Capability: l4

Original:

```termcap
l4=\E[=h
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l4               |
| Meaning    | Soft Label Key 4 |
| Sequence   | ESC[=h           |

---

# Capability: l5

Original:

```termcap
l5=\E[=i
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l5               |
| Meaning    | Soft Label Key 5 |
| Sequence   | ESC[=i           |

---

# Capability: l6

Original:

```termcap
l6=\E[=j
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l6               |
| Meaning    | Soft Label Key 6 |
| Sequence   | ESC[=j           |

---

# Capability: l7

Original:

```termcap
l7=\E[=k
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | l7               |
| Meaning    | Soft Label Key 7 |
| Sequence   | ESC[=k           |

---

# Capability: kb

Original:

```termcap
kb=\E[D
```

| Property        | Value         |
| --------------- | ------------- |
| Capability      | kb            |
| Meaning         | Backspace Key |
| Sequence        | ESC[D         |
| ANSI Equivalent | Cursor Left   |

### Notes

Unlike TDV2200, TDV1200 transmits ANSI-style escape sequences rather than raw control characters.

---

# Capability: kd

Original:

```termcap
kd=\E[B
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kd              |
| Meaning    | Cursor Down Key |
| Sequence   | ESC[B           |
| ANSI Name  | CUD             |

---

# Capability: kh

Original:

```termcap
kh=\E[H
```

| Property   | Value    |
| ---------- | -------- |
| Capability | kh       |
| Meaning    | Home Key |
| Sequence   | ESC[H    |

---

# Capability: kl

Original:

```termcap
kl=\E[D
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kl          |
| Meaning    | Cursor Left |
| Sequence   | ESC[D       |
| ANSI Name  | CUB         |

---

# Capability: kr

Original:

```termcap
kr=\E[C
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | kr           |
| Meaning    | Cursor Right |
| Sequence   | ESC[C        |
| ANSI Name  | CUF          |

---

# Capability: ku

Original:

```termcap
ku=\E[A
```

| Property   | Value     |
| ---------- | --------- |
| Capability | ku        |
| Meaning    | Cursor Up |
| Sequence   | ESC[A     |
| ANSI Name  | CUU       |

---

# Capability: li

Original:

```termcap
li#25
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | li             |
| Type       | Numeric        |
| Value      | 25             |
| Meaning    | Number of rows |

---

# Capability: mi

Original:

```termcap
mi
```

| Property   | Value                            |
| ---------- | -------------------------------- |
| Capability | mi                               |
| Meaning    | Cursor motion during insert mode |
| Type       | Boolean                          |

---

# Capability: nd

Original:

```termcap
nd=\E[C
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | nd           |
| Meaning    | Cursor right |
| Sequence   | ESC[C        |
| ANSI Name  | CUF          |

---

# Capability: se

Original:

```termcap
se=\E[0m
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | se                |
| Meaning    | End standout mode |
| Sequence   | ESC[0m            |
| ANSI Name  | SGR0              |

---

# Capability: sf

Original:

```termcap
sf=\E[S
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | sf             |
| Meaning    | Scroll forward |
| Sequence   | ESC[S          |

---

# Capability: SF

Original:

```termcap
SF=\E[%dS
```

| Property   | Value                  |
| ---------- | ---------------------- |
| Capability | SF                     |
| Meaning    | Scroll forward N lines |
| Sequence   | ESC[nS                 |

---

# Capability: so

Original:

```termcap
so=\E[7m
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | so                  |
| Meaning    | Start standout mode |
| Sequence   | ESC[7m              |
| ANSI Name  | Reverse Video       |

---

# Capability: sr

Original:

```termcap
sr=\E[T
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | sr             |
| Meaning    | Reverse scroll |
| Sequence   | ESC[T          |

---

# Capability: ue

Original:

```termcap
ue=\E[0m
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | ue            |
| Meaning    | End underline |
| Sequence   | ESC[0m        |

---

# Capability: up

Original:

```termcap
up=\E[A
```

| Property   | Value     |
| ---------- | --------- |
| Capability | up        |
| Meaning    | Cursor Up |
| Sequence   | ESC[A     |

---

# Capability: us

Original:

```termcap
us=\E[4m
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | us              |
| Meaning    | Start underline |
| Sequence   | ESC[4m          |

---

# Capability: ve

Original:

```termcap
ve=\E[0m
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | ve                  |
| Meaning    | End visual emphasis |
| Sequence   | ESC[0m              |

---

# Capability: vs

Original:

```termcap
vs=\E[2m
```

| Property   | Value                 |
| ---------- | --------------------- |
| Capability | vs                    |
| Meaning    | Start visual emphasis |
| Sequence   | ESC[2m                |
| ANSI Name  | Dim                   |

---

# Graphics Capabilities

The TDV1200 uses the exact same graphics definitions as TDV2200.

---

# Capability: GC

```termcap
GC=\E15
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | GC               |
| Meaning    | Graphics command |
| Type       | TDV proprietary  |

---

# Capability: GH

```termcap
GH=\E10
```

| Property       | Value           |
| -------------- | --------------- |
| Capability     | GH              |
| Likely Meaning | Horizontal line |

---

# Capability: GL

```termcap
GL=\E14
```

| Property       | Value       |
| -------------- | ----------- |
| Capability     | GL          |
| Likely Meaning | Left border |

---

# Capability: GR

```termcap
GR=\E16
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GR           |
| Likely Meaning | Right border |

---

# Capability: G1

```termcap
G1=\E19
```

| Property       | Value             |
| -------------- | ----------------- |
| Capability     | G1                |
| Likely Meaning | Upper-left corner |

---

# Capability: G2

```termcap
G2=\E17
```

| Property       | Value              |
| -------------- | ------------------ |
| Capability     | G2                 |
| Likely Meaning | Upper-right corner |

---

# Capability: G3

```termcap
G3=\E11
```

| Property       | Value             |
| -------------- | ----------------- |
| Capability     | G3                |
| Likely Meaning | Lower-left corner |

---

# Capability: G4

```termcap
G4=\E13
```

| Property       | Value              |
| -------------- | ------------------ |
| Capability     | G4                 |
| Likely Meaning | Lower-right corner |

---

# Capability: GU

```termcap
GU=\E12
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GU           |
| Likely Meaning | Upper border |

---

# Capability: GD

```termcap
GD=\E18
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GD           |
| Likely Meaning | Lower border |

---

# Capability: GV

```termcap
GV=\E1.
```

| Property       | Value           |
| -------------- | --------------- |
| Capability     | GV              |
| Likely Meaning | Vertical border |

---

# Capability: EN

Original:

```termcap
EN=\E[=C
```

| Property   | Value                        |
| ---------- | ---------------------------- |
| Capability | EN                           |
| Meaning    | Proprietary TDV mode command |
| Sequence   | ESC[=C                       |

---

# Capability: PU

Original:

```termcap
PU=\E[S
```

| Property   | Value   |
| ---------- | ------- |
| Capability | PU      |
| Meaning    | Page Up |
| Sequence   | ESC[S   |

---

# Capability: PD

Original:

```termcap
PD=\E[T
```

| Property   | Value     |
| ---------- | --------- |
| Capability | PD        |
| Meaning    | Page Down |
| Sequence   | ESC[T     |

# END OF TDV1200 TERMCAP

# NORSK DATA TERMINAL REFERENCES

## Part 5 - ND224, ND242, ND246 and ND320

---

# ND224

## Original Entry

```termcap
n0|nd224|Norsk Data 224,VTM 3:\
        :tc=tdv2115:
```

---

# Capability: tc

Original:

```termcap
tc=tdv2115
```

| Property      | Value                      |
| ------------- | -------------------------- |
| Capability    | tc                         |
| Meaning       | Terminal continuation      |
| Inherits From | tdv2115                    |
| Type          | Special termcap capability |

### Detailed Description

The ND224 definition contains no local capabilities.

Instead it inherits every capability from:

```text
tdv2115
```

Unfortunately the TDV2115 definition is not present in the supplied material.

Therefore the exact capabilities cannot be reconstructed from this source alone.

### Historical Notes

The terminal is identified as:

```text
Norsk Data 224
VTM 3
```

which suggests a very early Norsk Data video terminal generation.

---

# ND242

## Original Entry

```termcap
n1|nd242|Norsk Data 242,VTM 36:\
       :tc=tdv2115:
```

---

# Capability: tc

Original:

```termcap
tc=tdv2115
```

| Property      | Value                      |
| ------------- | -------------------------- |
| Capability    | tc                         |
| Meaning       | Terminal continuation      |
| Inherits From | tdv2115                    |
| Type          | Special termcap capability |

### Detailed Description

Like ND224, the ND242 contains no locally defined capabilities.

Everything comes from:

```text
tdv2115
```

which is not included in the uploaded source.

---

# ND246

## Original Entry

```termcap
n2|nd246|Norsk Data 246,VTM 53:\
        :cm=^P%.%.:\
        :vb=^B^A^A^A^A^A^C:\
        :tc=tdv2200:
```

---

# Overview

ND246 is the most interesting Norsk Data terminal definition.

Unlike ND224 and ND242 it overrides selected TDV2200 behavior.

Everything not explicitly overridden comes from:

```termcap
tc=tdv2200
```

---

# Capability: cm

Original:

```termcap
cm=^P%.%.
```

| Property        | Value                   |
| --------------- | ----------------------- |
| Capability      | cm                      |
| Meaning         | Cursor positioning      |
| Type            | Proprietary ND protocol |
| ANSI Compatible | No                      |
| Override        | TDV2200 cm              |

---

## Detailed Analysis

TDV2200 uses:

```text
ESC[row;columnH
```

ND246 instead uses:

```text
^P row column
```

Cursor positioning therefore appears to use a dedicated binary protocol.

### Character Breakdown

| Character | ASCII       | Hex      |
| --------- | ----------- | -------- |
| ^P        | DLE         | 10       |
| %.        | Output byte | Variable |
| %.        | Output byte | Variable |

### Example

Move to row 10 column 20:

```text
^P 0A 14
```

This is substantially more compact than ANSI cursor positioning.

---

# Capability: vb

Original:

```termcap
vb=^B^A^A^A^A^A^C
```

| Property        | Value                  |
| --------------- | ---------------------- |
| Capability      | vb                     |
| Meaning         | Visual Bell            |
| Type            | Proprietary ND command |
| ANSI Compatible | No                     |

---

## Detailed Analysis

Sequence:

```text
^B ^A ^A ^A ^A ^A ^C
```

Hex:

```hex
02 01 01 01 01 01 03
```

### Interpretation

Likely protocol:

| Byte  | Meaning        |
| ----- | -------------- |
| ^B    | Start command  |
| ^A... | Flash sequence |
| ^C    | End command    |

Instead of sounding an audible bell, the terminal flashes.

---

# Capability: tc

Original:

```termcap
tc=tdv2200
```

| Property      | Value                 |
| ------------- | --------------------- |
| Capability    | tc                    |
| Meaning       | Terminal continuation |
| Inherits From | tdv2200               |

---

## Inherited Capabilities

The following capabilities are inherited directly from TDV2200:

### Screen

* al
* AL
* cd
* ce
* cl
* dc
* DC
* dl
* DL

### Cursor

* do
* ho
* nd
* up

### Attributes

* so
* se
* us
* ue
* vs
* ve

### Keyboard

* kb
* kd
* kh
* kl
* kr
* ku

### Graphics

* GC
* GH
* GL
* GR
* G1
* G2
* G3
* G4
* GU
* GD
* GV

### Function Keys

* k0-k8

### Soft Label Keys

* l0-l7

### Printer Support

* PS
* PN

### TDV Modes

* EN
* PU
* PD

---

# ND246 Effective Definition

Conceptually:

```text
TDV2200
 + custom cursor positioning
 + custom visual bell
 = ND246
```

---

# ND320

## Original Entry

```termcap
n3|nd320|Norsk Data 320,VTM 93,103:\
        :tc=tdv2200:
```

---

# Capability: tc

Original:

```termcap
tc=tdv2200
```

| Property      | Value                 |
| ------------- | --------------------- |
| Capability    | tc                    |
| Meaning       | Terminal continuation |
| Inherits From | tdv2200               |

---

## Detailed Description

Unlike ND246, the ND320 defines no local overrides.

Therefore:

```text
ND320 = TDV2200
```

from a termcap perspective.

Every capability is inherited.

---

# Effective ND320 Capabilities

### Display

* 80 columns
* 25 rows

### Cursor

* ANSI cursor movement
* ANSI positioning

### Editing

* Insert/Delete character
* Insert/Delete line

### Scrolling

* Forward scroll
* Reverse scroll

### Attributes

* Reverse video
* Underline
* Dim

### Graphics

* TDV graphics subsystem

### Function Keys

* k0-k8

### Soft Label Keys

* l0-l7

### Printer Support

* PS
* PN

### TDV Modes

* EN
* PU
* PD

---

# Comparison Table

| Terminal | Base Definition | Overrides |
| -------- | --------------- | --------- |
| ND224    | TDV2115         | None      |
| ND242    | TDV2115         | None      |
| ND246    | TDV2200         | cm, vb    |
| ND320    | TDV2200         | None      |

---

# Conclusions

The uploaded material reveals three generations:

### Generation 1

* ND224
* ND242

Based on TDV2115.

### Generation 2

* TDV2200

Provides full ANSI-like functionality and TDV extensions.

### Generation 3

* ND246
* ND320

Built on TDV2200.

ND246 retains older Norsk Data cursor protocols while ND320 is effectively a pure TDV2200.

# END OF PART 5

# TDV2200 TERMINFO REFERENCE

## Part 6 - General Terminal Capabilities and Core Screen Functions

# Original Entry Header

```terminfo
tdv2200|Tandberg TDV2200/9S,
   am,
   cols#80, lines#25,
   bel=^G, blink=\E[5m, cbt=\E[38_, clear=^Y,
   cnorm=\E[0m, cr=\r, cub1=\b, cud1=^K, cuf1=^X,
   cup=\E[%i%p1%d;%p2%dH,
   ...
```

---

# Overview

Unlike termcap, terminfo provides:

* standardized capability names
* parameterized formatting
* larger keyboard definitions
* more detailed feature descriptions

The TDV2200 terminfo entry is substantially richer than the termcap entry.

---

# Capability: am

Original:

```terminfo
am
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | am                |
| Meaning    | Automatic margins |
| Type       | Boolean           |

### Description

Cursor automatically wraps when reaching the right margin.

---

# Capability: cols

Original:

```terminfo
cols#80
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | cols         |
| Value      | 80           |
| Meaning    | Screen width |

---

# Capability: lines

Original:

```terminfo
lines#25
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | lines         |
| Value      | 25            |
| Meaning    | Screen height |

---

# Capability: bel

Original:

```terminfo
bel=^G
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | bel          |
| Meaning    | Audible bell |
| Character  | ^G           |
| ASCII      | 07           |

### Description

Produces an audible beep.

---

# Capability: blink

Original:

```terminfo
blink=\E[5m
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | blink               |
| Meaning    | Start blinking text |
| ANSI Name  | Blink               |
| Sequence   | ESC[5m              |

---

# Capability: cbt

Original:

```terminfo
cbt=\E[38_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | cbt             |
| Meaning    | Back tab        |
| Sequence   | ESC[38_         |
| Type       | TDV proprietary |

### Description

Moves cursor to previous tab stop.

---

# Capability: clear

Original:

```terminfo
clear=^Y
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | clear        |
| Meaning    | Clear screen |
| Character  | ^Y           |
| ASCII      | 25           |
| Hex        | 19           |

### Important Observation

Unlike ANSI terminals:

```text
ESC[2J
```

TDV2200 uses:

```text
^Y
```

for full-screen clear.

This is a unique TDV behavior.

---

# Capability: cnorm

Original:

```terminfo
cnorm=\E[0m
```

| Property   | Value                       |
| ---------- | --------------------------- |
| Capability | cnorm                       |
| Meaning    | Normal cursor/display state |
| Sequence   | ESC[0m                      |

---

# Capability: cr

Original:

```terminfo
cr=\r
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | cr              |
| Meaning    | Carriage return |
| Character  | CR              |
| ASCII      | 13              |

---

# Capability: cub1

Original:

```terminfo
cub1=\b
```

| Property   | Value                    |
| ---------- | ------------------------ |
| Capability | cub1                     |
| Meaning    | Cursor left one position |
| Character  | BS                       |
| ASCII      | 08                       |

### Observation

TDV2200 uses raw control characters for cursor movement.

---

# Capability: cud1

Original:

```terminfo
cud1=^K
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | cud1        |
| Meaning    | Cursor down |
| Character  | ^K          |
| ASCII      | 11          |
| Hex        | 0B          |

---

# Capability: cuf1

Original:

```terminfo
cuf1=^X
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | cuf1         |
| Meaning    | Cursor right |
| Character  | ^X           |
| ASCII      | 24           |
| Hex        | 18           |

---

# Capability: cup

Original:

```terminfo
cup=\E[%i%p1%d;%p2%dH
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | cup             |
| Meaning    | Cursor position |
| ANSI Name  | CUP             |
| Sequence   | ESC[row;columnH |

### Parameters

| Parameter | Meaning                  |
| --------- | ------------------------ |
| %i        | Increment row and column |
| %p1       | Row                      |
| %p2       | Column                   |
| %d        | Decimal conversion       |

---

# Capability: cuu1

Original:

```terminfo
cuu1=^\
```

| Property   | Value     |
| ---------- | --------- |
| Capability | cuu1      |
| Meaning    | Cursor up |
| Character  | ^\        |
| ASCII      | 28        |
| Hex        | 1C        |

---

# Capability: dch1

Original:

```terminfo
dch1=\E[P
```

| Property   | Value                |
| ---------- | -------------------- |
| Capability | dch1                 |
| Meaning    | Delete one character |
| ANSI Name  | DCH                  |
| Sequence   | ESC[P                |

---

# Capability: dim

Original:

```terminfo
dim=\E[2m
```

| Property   | Value    |
| ---------- | -------- |
| Capability | dim      |
| Meaning    | Dim text |
| ANSI Name  | Dim      |
| Sequence   | ESC[2m   |

---

# Capability: dl

Original:

```terminfo
dl=\E[%p1%dM
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | dl             |
| Meaning    | Delete N lines |
| ANSI Name  | DL             |
| Parameter  | %p1            |

---

# Capability: dl1

Original:

```terminfo
dl1=\E[M
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | dl1             |
| Meaning    | Delete one line |
| ANSI Name  | DL              |

---

# Capability: ed

Original:

```terminfo
ed=\E[J
```

| Property   | Value                   |
| ---------- | ----------------------- |
| Capability | ed                      |
| Meaning    | Clear to end of display |
| ANSI Name  | ED                      |

---

# Capability: el

Original:

```terminfo
el=\E[K
```

| Property   | Value                |
| ---------- | -------------------- |
| Capability | el                   |
| Meaning    | Clear to end of line |
| ANSI Name  | EL                   |

---

# Capability: home

Original:

```terminfo
home=^]
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | home        |
| Meaning    | Home cursor |
| Character  | ^]          |
| ASCII      | 29          |
| Hex        | 1D          |

---

# Capability: knl

Original:

```terminfo
knl=\r
```

| Property   | Value    |
| ---------- | -------- |
| Capability | knl      |
| Meaning    | New line |
| Character  | CR       |

---

# END OF PART 6

# TDV2200 TERMINFO REFERENCE

## Part 7 - Keyboard Architecture (Function Keys and Command Keys)

---

# TDV2200 Keyboard Overview

The TDV2200 keyboard is substantially more sophisticated than a VT100 keyboard.

The terminfo definition exposes:

* Function keys
* Soft-label keys
* Command keys
* Copy keys
* Move keys
* Print keys
* Help keys
* Edit keys
* Block operations

The keyboard was designed to support applications such as:

* NOTIS
* Office systems
* Form processing
* Data entry applications

rather than simple command-line operation.

---

# Function Key Group

## Capability: kf0

Original:

```terminfo
kf0=\E[24_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf0             |
| Meaning    | Function Key 0  |
| Sequence   | ESC[24_         |
| Type       | TDV proprietary |

---

## Capability: kf1

Original:

```terminfo
kf1=\E[50_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf1            |
| Meaning    | Function Key 1 |
| Sequence   | ESC[50_        |

---

## Capability: kf2

Original:

```terminfo
kf2=\E[52_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf2            |
| Meaning    | Function Key 2 |
| Sequence   | ESC[52_        |

---

## Capability: kf3

Original:

```terminfo
kf3=\E[55_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf3            |
| Meaning    | Function Key 3 |
| Sequence   | ESC[55_        |

---

## Capability: kf4

Original:

```terminfo
kf4=\E[58_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf4            |
| Meaning    | Function Key 4 |
| Sequence   | ESC[58_        |

---

## Capability: kf5

Original:

```terminfo
kf5=\E[60_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf5            |
| Meaning    | Function Key 5 |
| Sequence   | ESC[60_        |

---

## Capability: kf6

Original:

```terminfo
kf6=\E[62_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf6            |
| Meaning    | Function Key 6 |
| Sequence   | ESC[62_        |

---

## Capability: kf7

Original:

```terminfo
kf7=\E[64_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf7            |
| Meaning    | Function Key 7 |
| Sequence   | ESC[64_        |

---

## Capability: kf8

Original:

```terminfo
kf8=\E[66_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf8            |
| Meaning    | Function Key 8 |
| Sequence   | ESC[66_        |

---

## Capability: kf9

Original:

```terminfo
kf9=\E[84_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kf9            |
| Meaning    | Function Key 9 |
| Sequence   | ESC[84_        |

---

## Capability: kf10

Original:

```terminfo
kf10=\E[42_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf10            |
| Meaning    | Function Key 10 |
| Sequence   | ESC[42_         |

---

## Capability: kf11

Original:

```terminfo
kf11=\E[00_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf11            |
| Meaning    | Function Key 11 |
| Sequence   | ESC[00_         |

---

## Capability: kf12

Original:

```terminfo
kf12=\E[30_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf12            |
| Meaning    | Function Key 12 |
| Sequence   | ESC[30_         |

---

## Capability: kf13

Original:

```terminfo
kf13=\E[51_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf13            |
| Meaning    | Function Key 13 |
| Sequence   | ESC[51_         |

---

## Capability: kf14

Original:

```terminfo
kf14=\E[53_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf14            |
| Meaning    | Function Key 14 |
| Sequence   | ESC[53_         |

---

## Capability: kf15

Original:

```terminfo
kf15=\E[56_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf15            |
| Meaning    | Function Key 15 |
| Sequence   | ESC[56_         |

---

## Capability: kf16

Original:

```terminfo
kf16=\E[59_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf16            |
| Meaning    | Function Key 16 |
| Sequence   | ESC[59_         |

---

## Capability: kf17

Original:

```terminfo
kf17=\E[61_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf17            |
| Meaning    | Function Key 17 |
| Sequence   | ESC[61_         |

---

## Capability: kf18

Original:

```terminfo
kf18=\E[63_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf18            |
| Meaning    | Function Key 18 |
| Sequence   | ESC[63_         |

---

## Capability: kf19

Original:

```terminfo
kf19=\E[65_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf19            |
| Meaning    | Function Key 19 |
| Sequence   | ESC[65_         |

---

## Capability: kf20

Original:

```terminfo
kf20=\E[67_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf20            |
| Meaning    | Function Key 20 |
| Sequence   | ESC[67_         |

---

# Special Command Keys

## Capability: kCAN

Original:

```terminfo
kCAN=\E[11_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kCAN    |
| Meaning    | Cancel  |
| Sequence   | ESC[11_ |

### Usage

Abort current operation.

---

## Capability: kCMD

Original:

```terminfo
kCMD=\E[43_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kCMD    |
| Meaning    | Command |
| Sequence   | ESC[43_ |

### Usage

Switch to command processing mode.

---

## Capability: kCPY

Original:

```terminfo
kCPY=\E[13_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kCPY    |
| Meaning    | Copy    |
| Sequence   | ESC[13_ |

### Usage

Copy selected data or form field.

---

## Capability: kEXT

Original:

```terminfo
kEXT=\E[49_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kEXT    |
| Meaning    | Exit    |
| Sequence   | ESC[49_ |

### Usage

Exit current operation.

---

## Capability: kHLP

Original:

```terminfo
kHLP=\E[47_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kHLP    |
| Meaning    | Help    |
| Sequence   | ESC[47_ |

### Usage

Display help information.

---

## Capability: kMOV

Original:

```terminfo
kMOV=\E[15_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kMOV    |
| Meaning    | Move    |
| Sequence   | ESC[15_ |

### Usage

Move field or block.

---

## Capability: kPRT

Original:

```terminfo
kPRT=\E[45_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kPRT    |
| Meaning    | Print   |
| Sequence   | ESC[45_ |

### Usage

Print current screen or object.

---

## Capability: kUND

Original:

```terminfo
kUND=\E[31_
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kUND      |
| Meaning    | Underline |
| Sequence   | ESC[31_   |

### Usage

Formatting command used by applications.

---

# Observations

The TDV2200 keyboard clearly supports application-level operations rather than merely character entry.

The presence of dedicated:

* HELP
* COPY
* MOVE
* PRINT
* COMMAND
* CANCEL

keys strongly suggests integration with form-oriented office applications such as NOTIS.

# END OF PART 7
# TDV2200 TERMINFO REFERENCE

## Part 8 - Extended Function Keys, Editing Keys and Navigation Keys

---

# Extended Function Key Group

## Capability: kf21

Original:

```terminfo
kf21=\E[85_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf21            |
| Meaning    | Function Key 21 |
| Sequence   | ESC[85_         |

---

## Capability: kf22

Original:

```terminfo
kf22=\E[43_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf22            |
| Meaning    | Function Key 22 |
| Sequence   | ESC[43_         |

---

## Capability: kf23

Original:

```terminfo
kf23=\E[01_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf23            |
| Meaning    | Function Key 23 |
| Sequence   | ESC[01_         |

---

## Capability: kf24

Original:

```terminfo
kf24=\E[31_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf24            |
| Meaning    | Function Key 24 |
| Sequence   | ESC[31_         |

---

## Capability: kf25

Original:

```terminfo
kf25=\E[02_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf25            |
| Meaning    | Function Key 25 |
| Sequence   | ESC[02_         |

---

## Capability: kf26

Original:

```terminfo
kf26=\E[03_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf26            |
| Meaning    | Function Key 26 |
| Sequence   | ESC[03_         |

---

## Capability: kf27

Original:

```terminfo
kf27=\E[04_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf27            |
| Meaning    | Function Key 27 |
| Sequence   | ESC[04_         |

---

## Capability: kf28

Original:

```terminfo
kf28=\E[06_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf28            |
| Meaning    | Function Key 28 |
| Sequence   | ESC[06_         |

---

## Capability: kf29

Original:

```terminfo
kf29=\E[08_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf29            |
| Meaning    | Function Key 29 |
| Sequence   | ESC[08_         |

---

## Capability: kf30

Original:

```terminfo
kf30=\E[09_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf30            |
| Meaning    | Function Key 30 |
| Sequence   | ESC[09_         |

---

## Capability: kf31

Original:

```terminfo
kf31=\E[12_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf31            |
| Meaning    | Function Key 31 |
| Sequence   | ESC[12_         |

---

## Capability: kf32

Original:

```terminfo
kf32=\E[14_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf32            |
| Meaning    | Function Key 32 |
| Sequence   | ESC[14_         |

---

## Capability: kf33

Original:

```terminfo
kf33=\E[46_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf33            |
| Meaning    | Function Key 33 |
| Sequence   | ESC[46_         |

---

## Capability: kf34

Original:

```terminfo
kf34=\E[38_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf34            |
| Meaning    | Function Key 34 |
| Sequence   | ESC[38_         |

---

## Capability: kf35

Original:

```terminfo
kf35=\E[40_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf35            |
| Meaning    | Function Key 35 |
| Sequence   | ESC[40_         |

---

## Capability: kf36

Original:

```terminfo
kf36=\E[44_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf36            |
| Meaning    | Function Key 36 |
| Sequence   | ESC[44_         |

---

## Capability: kf37

Original:

```terminfo
kf37=\E[35_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf37            |
| Meaning    | Function Key 37 |
| Sequence   | ESC[35_         |

---

## Capability: kf38

Original:

```terminfo
kf38=\E[37_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf38            |
| Meaning    | Function Key 38 |
| Sequence   | ESC[37_         |

---

## Capability: kf39

Original:

```terminfo
kf39=\E[13_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf39            |
| Meaning    | Function Key 39 |
| Sequence   | ESC[13_         |

---

## Capability: kf40

Original:

```terminfo
kf40=\E[15_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf40            |
| Meaning    | Function Key 40 |
| Sequence   | ESC[15_         |

---

## Capability: kf41

Original:

```terminfo
kf41=\E[39_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf41            |
| Meaning    | Function Key 41 |
| Sequence   | ESC[39_         |

---

## Capability: kf42

Original:

```terminfo
kf42=\E[41_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf42            |
| Meaning    | Function Key 42 |
| Sequence   | ESC[41_         |

---

## Capability: kf43

Original:

```terminfo
kf43=\E[26_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf43            |
| Meaning    | Function Key 43 |
| Sequence   | ESC[26_         |

---

## Capability: kf44

Original:

```terminfo
kf44=\E[27_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf44            |
| Meaning    | Function Key 44 |
| Sequence   | ESC[27_         |

---

## Capability: kf45

Original:

```terminfo
kf45=\E[49_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf45            |
| Meaning    | Function Key 45 |
| Sequence   | ESC[49_         |

---

## Capability: kf46

Original:

```terminfo
kf46=\E[25_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf46            |
| Meaning    | Function Key 46 |
| Sequence   | ESC[25_         |

---

## Capability: kf47

Original:

```terminfo
kf47=\E[22_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf47            |
| Meaning    | Function Key 47 |
| Sequence   | ESC[22_         |

---

## Capability: kf48

Original:

```terminfo
kf48=\E[23_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf48            |
| Meaning    | Function Key 48 |
| Sequence   | ESC[23_         |

---

## Capability: kf49

Original:

```terminfo
kf49=\E[86_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf49            |
| Meaning    | Function Key 49 |
| Sequence   | ESC[86_         |

---

## Capability: kf50

Original:

```terminfo
kf50=\E[87_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf50            |
| Meaning    | Function Key 50 |
| Sequence   | ESC[87_         |

---

# Special Navigation Keys

## Capability: kbeg

Original:

```terminfo
kbeg=\E[35_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kbeg    |
| Meaning    | Begin   |
| Sequence   | ESC[35_ |

### Description

Move to beginning of form, field or operation.

---

## Capability: kdo

Original:

```terminfo
kdo=\E[86_
```

| Property   | Value                 |
| ---------- | --------------------- |
| Capability | kdo                   |
| Meaning    | Down/Page Down Action |
| Sequence   | ESC[86_               |

---

## Capability: kopt

Original:

```terminfo
kopt=\E[20_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kopt    |
| Meaning    | Option  |
| Sequence   | ESC[20_ |

---

## Capability: kOPT

Original:

```terminfo
kOPT=\E[21_
```

| Property   | Value          |
| ---------- | -------------- |
| Capability | kOPT           |
| Meaning    | Shifted Option |
| Sequence   | ESC[21_        |

---

# Editing Keys

## Capability: ked

Original:

```terminfo
ked=\E[48_
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | ked          |
| Meaning    | Erase to End |
| Sequence   | ESC[48_      |

---

## Capability: kel

Original:

```terminfo
kel=\E[11_
```

| Property   | Value      |
| ---------- | ---------- |
| Capability | kel        |
| Meaning    | Erase Line |
| Sequence   | ESC[11_    |

---

## Capability: kil1

Original:

```terminfo
kil1=\E[18_
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kil1        |
| Meaning    | Insert Line |
| Sequence   | ESC[18_     |

---

## Capability: kdl1

Original:

```terminfo
kdl1=\E[19_
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kdl1        |
| Meaning    | Delete Line |
| Sequence   | ESC[19_     |

---

## Capability: kdch1

Original:

```terminfo
kdch1=\E[10_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | kdch1            |
| Meaning    | Delete Character |
| Sequence   | ESC[10_          |

---

## Capability: kich1

Original:

```terminfo
kich1=\E[82_
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | kich1            |
| Meaning    | Insert Character |
| Sequence   | ESC[82_          |

---

# Observations

At this point it becomes clear that the TDV2200 keyboard is closer to a dedicated office workstation terminal than a traditional ASCII terminal.

The keyboard contains direct hardware keys for:

* Insert line
* Delete line
* Insert character
* Delete character
* Copy
* Move
* Help
* Print
* Option
* Begin
* Cancel
* Command

These are features normally associated with office automation systems.

# END OF PART 8
# TDV2200 TERMINFO REFERENCE

## Part 9 - Navigation, Editing and Application Control Keys

---

# Capability: kcan

Original:

```terminfo
kcan=\E[11_
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | kcan                |
| Meaning    | Cancel              |
| Sequence   | ESC[11_             |
| Category   | Application Control |

### Description

Terminates or aborts the current operation.

Commonly used by NOTIS forms.

---

# Capability: kcmd

Original:

```terminfo
kcmd=\E[43_
```

| Property   | Value               |
| ---------- | ------------------- |
| Capability | kcmd                |
| Meaning    | Command             |
| Sequence   | ESC[43_             |
| Category   | Application Control |

### Description

Switches application into command-processing mode.

---

# Capability: kcpy

Original:

```terminfo
kcpy=\E[13_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kcpy    |
| Meaning    | Copy    |
| Sequence   | ESC[13_ |
| Category   | Editing |

### Description

Used by office applications for field copying and block operations.

---

# Capability: kctab

Original:

```terminfo
kctab=\E[38_
```

| Property   | Value      |
| ---------- | ---------- |
| Capability | kctab      |
| Meaning    | Back Tab   |
| Sequence   | ESC[38_    |
| Category   | Navigation |

### Description

Moves to previous tab stop or previous field.

---

# Capability: kcub1

Original:

```terminfo
kcub1=^H
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kcub1       |
| Meaning    | Cursor Left |
| Character  | ^H          |
| ASCII      | 08          |
| Hex        | 08          |

---

# Capability: kcud1

Original:

```terminfo
kcud1=^K
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kcud1       |
| Meaning    | Cursor Down |
| Character  | ^K          |
| ASCII      | 11          |
| Hex        | 0B          |

---

# Capability: kcuf1

Original:

```terminfo
kcuf1=^X
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | kcuf1        |
| Meaning    | Cursor Right |
| Character  | ^X           |
| ASCII      | 24           |
| Hex        | 18           |

---

# Capability: kcuu1

Original:

```terminfo
kcuu1=^\
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kcuu1     |
| Meaning    | Cursor Up |
| Character  | ^\        |
| ASCII      | 28        |
| Hex        | 1C        |

---

# Cursor Key Summary

| Direction | Character | ASCII | Hex |
| --------- | --------- | ----- | --- |
| Left      | ^H        | 8     | 08  |
| Down      | ^K        | 11    | 0B  |
| Right     | ^X        | 24    | 18  |
| Up        | ^\        | 28    | 1C  |

### Observation

TDV2200 uses single control characters rather than ANSI escape sequences.

This makes cursor-key processing extremely efficient.

---

# Capability: kent

Original:

```terminfo
kent=\r
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kent      |
| Meaning    | Enter Key |
| Character  | CR        |
| ASCII      | 13        |
| Hex        | 0D        |

### Description

Primary field completion key.

---

# Capability: kext

Original:

```terminfo
kext=\E[49_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kext    |
| Meaning    | Exit    |
| Sequence   | ESC[49_ |

### Description

Terminates current screen or operation.

---

# Capability: khlp

Original:

```terminfo
khlp=\E[47_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | khlp    |
| Meaning    | Help    |
| Sequence   | ESC[47_ |

### Description

Requests context-sensitive help.

---

# Capability: khome

Original:

```terminfo
khome=^]
```

| Property   | Value |
| ---------- | ----- |
| Capability | khome |
| Meaning    | Home  |
| Character  | ^]    |
| ASCII      | 29    |
| Hex        | 1D    |

### Description

Move to home position.

---

# Capability: khts

Original:

```terminfo
khts=\E[36_
```

| Property   | Value              |
| ---------- | ------------------ |
| Capability | khts               |
| Meaning    | Horizontal Tab Set |
| Sequence   | ESC[36_            |

### Description

Defines a tab stop at current cursor position.

---

# Capability: kmov

Original:

```terminfo
kmov=\E[15_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kmov    |
| Meaning    | Move    |
| Sequence   | ESC[15_ |

### Description

Move block, field or selected data.

---

# Capability: knp

Original:

```terminfo
knp=\E[T
```

| Property   | Value     |
| ---------- | --------- |
| Capability | knp       |
| Meaning    | Next Page |
| Sequence   | ESC[T     |

### Description

Page forward operation.

---

# Capability: kpp

Original:

```terminfo
kpp=\E[S
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | kpp           |
| Meaning    | Previous Page |
| Sequence   | ESC[S         |

### Description

Page backward operation.

---

# Capability: kprt

Original:

```terminfo
kprt=\E[45_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kprt    |
| Meaning    | Print   |
| Sequence   | ESC[45_ |

### Description

Initiates printing.

May print:

* Current field
* Current form
* Entire screen

depending upon application.

---

# Capability: kund

Original:

```terminfo
kund=\E[31_
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kund      |
| Meaning    | Underline |
| Sequence   | ESC[31_   |

### Description

Formatting key used by applications.

---

# TDV2200 Office Automation Key Groups

The keyboard can now be divided into logical groups.

---

## Cursor Navigation

| Key   | Function |
| ----- | -------- |
| kcub1 | Left     |
| kcud1 | Down     |
| kcuf1 | Right    |
| kcuu1 | Up       |
| khome | Home     |

---

## Page Navigation

| Key | Function      |
| --- | ------------- |
| kpp | Previous Page |
| knp | Next Page     |

---

## Editing

| Key   | Function         |
| ----- | ---------------- |
| kcpy  | Copy             |
| kmov  | Move             |
| kich1 | Insert Character |
| kdch1 | Delete Character |
| kil1  | Insert Line      |
| kdl1  | Delete Line      |

---

## Application Control

| Key  | Function |
| ---- | -------- |
| kcan | Cancel   |
| kcmd | Command  |
| khlp | Help     |
| kext | Exit     |
| kprt | Print    |

---

## Formatting

| Key  | Function  |
| ---- | --------- |
| kund | Underline |
| khts | Set Tab   |

---

# Architectural Observation

The TDV2200 keyboard appears optimized for:

* NOTIS
* Office automation
* Form processing
* Structured data entry
* Document editing

rather than Unix shell operation.

Many of these dedicated keys have no equivalent on VT100-class terminals.

# END OF PART 9
# TDV2200 TERMINFO REFERENCE

## Part 10 - Display Attributes, Printer Support, Tab Control and TDV Proprietary Modes

---

# Display Attribute Capabilities

The TDV2200 supports a number of display attributes beyond ordinary text.

These capabilities are used extensively by:

* NOTIS
* FORM systems
* Screen editors
* Data entry applications

---

# Capability: sgr0

Original:

```terminfo
sgr0=\E[0m
```

| Property   | Value                |
| ---------- | -------------------- |
| Capability | sgr0                 |
| Meaning    | Reset all attributes |
| ANSI Name  | SGR0                 |
| Sequence   | ESC[0m               |
| Hex        | 1B 5B 30 6D          |

### Description

Returns terminal to normal display state.

Cancels:

* Reverse video
* Underline
* Blink
* Dim
* Any TDV display emphasis

---

# Capability: rev

Original:

```terminfo
rev=\E[7m
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | rev           |
| Meaning    | Reverse Video |
| ANSI Name  | Reverse       |
| Sequence   | ESC[7m        |

### Description

Foreground and background are exchanged.

Commonly used for:

* Menus
* Selected fields
* Current cursor field

---

# Capability: smul

Original:

```terminfo
smul=\E[4m
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | smul            |
| Meaning    | Start Underline |
| ANSI Name  | Underline       |
| Sequence   | ESC[4m          |

---

# Capability: rmul

Original:

```terminfo
rmul=\E[0m
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | rmul          |
| Meaning    | End Underline |
| Sequence   | ESC[0m        |

---

# Capability: dim

Original:

```terminfo
dim=\E[2m
```

| Property   | Value    |
| ---------- | -------- |
| Capability | dim      |
| Meaning    | Dim Text |
| Sequence   | ESC[2m   |

### Description

Displays text at reduced intensity.

---

# Capability: blink

Original:

```terminfo
blink=\E[5m
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | blink         |
| Meaning    | Blinking Text |
| Sequence   | ESC[5m        |

### Description

Makes displayed characters blink.

Widely used in alarm and operator screens.

---

# Attribute Summary

| Attribute | Sequence |
| --------- | -------- |
| Normal    | ESC[0m   |
| Reverse   | ESC[7m   |
| Underline | ESC[4m   |
| Blink     | ESC[5m   |
| Dim       | ESC[2m   |

---

# Tab Control

The TDV2200 provides a much richer tab system than ordinary ANSI terminals.

---

# Capability: cbt

Original:

```terminfo
cbt=\E[38_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | cbt             |
| Meaning    | Cursor Back Tab |
| Sequence   | ESC[38_         |

### Description

Moves to previous tab stop.

---

# Capability: khts

Original:

```terminfo
khts=\E[36_
```

| Property   | Value              |
| ---------- | ------------------ |
| Capability | khts               |
| Meaning    | Set Horizontal Tab |
| Sequence   | ESC[36_            |

### Description

Creates a tab stop at current position.

---

# Tab Usage

These features were especially useful for:

* Forms
* Data entry
* Column-oriented editing

Common in Norsk Data office software.

---

# Printer Support

A major TDV feature not present on most personal computer terminals.

---

# Capability: mc4

Original:

```terminfo
mc4=\E[4i
```

| Property   | Value      |
| ---------- | ---------- |
| Capability | mc4        |
| Meaning    | Printer On |
| Sequence   | ESC[4i     |

### Description

Begin printer logging.

Characters are simultaneously displayed and printed.

---

# Capability: mc5

Original:

```terminfo
mc5=\E[5i
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | mc5         |
| Meaning    | Printer Off |
| Sequence   | ESC[5i      |

### Description

Stops printer logging.

---

# Historical Notes

Many TDV terminals were connected to:

* Matrix printers
* Daisy-wheel printers
* Letter-quality printers

through local serial printer ports.

This allowed:

* Screen dumps
* Form printing
* Report generation

without host intervention.

---

# TDV Proprietary Graphics System

The TDV graphics system is one of the most distinctive parts of the terminal.

---

# Capability: GH

Original:

```terminfo
GH=\E10
```

| Property       | Value                      |
| -------------- | -------------------------- |
| Capability     | GH                         |
| Likely Meaning | Horizontal Graphic Element |

---

# Capability: GV

Original:

```terminfo
GV=\E1.
```

| Property       | Value                    |
| -------------- | ------------------------ |
| Capability     | GV                       |
| Likely Meaning | Vertical Graphic Element |

---

# Capability: GL

Original:

```terminfo
GL=\E14
```

| Property       | Value       |
| -------------- | ----------- |
| Capability     | GL          |
| Likely Meaning | Left Border |

---

# Capability: GR

Original:

```terminfo
GR=\E16
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GR           |
| Likely Meaning | Right Border |

---

# Capability: GU

Original:

```terminfo
GU=\E12
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GU           |
| Likely Meaning | Upper Border |

---

# Capability: GD

Original:

```terminfo
GD=\E18
```

| Property       | Value        |
| -------------- | ------------ |
| Capability     | GD           |
| Likely Meaning | Lower Border |

---

# Corner Elements

| Capability | Likely Meaning     |
| ---------- | ------------------ |
| G1         | Upper Left Corner  |
| G2         | Upper Right Corner |
| G3         | Lower Left Corner  |
| G4         | Lower Right Corner |

### Intended Usage

These commands likely allowed applications to build forms such as:

```text
┌────────────────────┐
│ Customer Number    │
├────────────────────┤
│                    │
└────────────────────┘
```

without transmitting actual box-drawing characters.

---

# Graphics Clear

## Capability: GC

Original:

```terminfo
GC=\E15
```

| Property   | Value                             |
| ---------- | --------------------------------- |
| Capability | GC                                |
| Meaning    | Graphics Clear / Graphics Control |
| Type       | TDV Proprietary                   |

### Notes

Precise behavior requires firmware documentation.

---

# TDV Private Modes

The most mysterious part of the terminal definition.

---

# Capability: EN

Original:

```terminfo
EN=\E[=C
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | EN               |
| Meaning    | TDV Mode Command |
| Sequence   | ESC[=C           |

### Notes

Not part of ANSI.

Appears throughout TDV definitions.

Likely switches between terminal operating modes.

---

# TDV Initialization

## TDV2200

Original:

```terminfo
is=\E[62;36;66l\EQ\E[36;62;62h\E[0m
```

---

## Breakdown

| Sequence      | Purpose           |
| ------------- | ----------------- |
| ESC[62;36;66l | Disable TDV modes |
| ESCQ          | TDV command       |
| ESC[36;62;62h | Enable TDV modes  |
| ESC[0m        | Reset attributes  |

---

# Observations

Mode numbers:

```text
36
62
66
```

appear repeatedly.

These are almost certainly firmware-controlled private features.

Potential candidates:

* Function-key mode
* Graphics mode
* Form-processing mode
* Keyboard mode
* Printer mode

Further reverse engineering requires:

* TDV service manuals
* TDV programmer reference
* Firmware dumps

---

# Screen Management Summary

| Capability | Function           |
| ---------- | ------------------ |
| clear      | Clear screen       |
| cup        | Cursor positioning |
| home       | Home cursor        |
| el         | Clear line         |
| ed         | Clear display      |
| dl         | Delete lines       |
| il         | Insert lines       |
| dch1       | Delete characters  |
| ich1       | Insert characters  |

---

# Display Attribute Summary

| Capability | Function      |
| ---------- | ------------- |
| rev        | Reverse video |
| smul       | Underline     |
| blink      | Blink         |
| dim        | Dim           |
| sgr0       | Normal        |

---

# TDV Extensions Summary

| Group         | Purpose               |
| ------------- | --------------------- |
| Graphics      | Form drawing          |
| Printer       | Local printing        |
| Tabs          | Structured data entry |
| Function Keys | Application control   |
| Private Modes | TDV firmware control  |

# END OF PART 10
# TDV2200 TERMINFO REFERENCE

## Part 11 - Remaining Function Keys, Soft Labels and Keyboard Matrix

---

# Extended Function Keys (Continuation)

The TDV2200 terminfo definition continues beyond the traditional F1-F20 range.

This is a strong indication that the terminal firmware exposes logical application functions rather than merely physical keys.

---

# Capability: kf51

Original:

```terminfo
kf51=\E[88_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf51            |
| Meaning    | Function Key 51 |
| Sequence   | ESC[88_         |

---

# Capability: kf52

Original:

```terminfo
kf52=\E[89_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf52            |
| Meaning    | Function Key 52 |
| Sequence   | ESC[89_         |

---

# Capability: kf53

Original:

```terminfo
kf53=\E[90_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf53            |
| Meaning    | Function Key 53 |
| Sequence   | ESC[90_         |

---

# Capability: kf54

Original:

```terminfo
kf54=\E[91_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf54            |
| Meaning    | Function Key 54 |
| Sequence   | ESC[91_         |

---

# Capability: kf55

Original:

```terminfo
kf55=\E[92_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf55            |
| Meaning    | Function Key 55 |
| Sequence   | ESC[92_         |

---

# Capability: kf56

Original:

```terminfo
kf56=\E[93_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf56            |
| Meaning    | Function Key 56 |
| Sequence   | ESC[93_         |

---

# Capability: kf57

Original:

```terminfo
kf57=\E[94_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf57            |
| Meaning    | Function Key 57 |
| Sequence   | ESC[94_         |

---

# Capability: kf58

Original:

```terminfo
kf58=\E[95_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf58            |
| Meaning    | Function Key 58 |
| Sequence   | ESC[95_         |

---

# Capability: kf59

Original:

```terminfo
kf59=\E[96_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf59            |
| Meaning    | Function Key 59 |
| Sequence   | ESC[96_         |

---

# Capability: kf60

Original:

```terminfo
kf60=\E[97_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf60            |
| Meaning    | Function Key 60 |
| Sequence   | ESC[97_         |

---

# Capability: kf61

Original:

```terminfo
kf61=\E[98_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf61            |
| Meaning    | Function Key 61 |
| Sequence   | ESC[98_         |

---

# Capability: kf62

Original:

```terminfo
kf62=\E[99_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf62            |
| Meaning    | Function Key 62 |
| Sequence   | ESC[99_         |

---

# Capability: kf63

Original:

```terminfo
kf63=\E[100_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kf63            |
| Meaning    | Function Key 63 |
| Sequence   | ESC[100_        |

---

# Observation: Why 64 Function Keys?

The TDV2200 almost certainly did not have 64 physical function keys.

Instead:

```text
Physical Keys
+
Shift
+
Ctrl
+
Application Modes
=
Logical Function Keys
```

The terminfo database exposes the logical functions available to software.

This was common on office terminals where applications dynamically reassigned key functions.

---

# Soft Label Architecture

The TDV terminals support soft-label keys.

Unlike a VT100:

```text
F1 F2 F3 F4
```

the TDV architecture associates:

```text
Physical Key
+
Displayed Label
+
Logical Function
```

---

# Soft Label Group

| Label | Sequence |
| ----- | -------- |
| l0    | ESC[51_  |
| l1    | ESC[53_  |
| l2    | ESC[56_  |
| l3    | ESC[59_  |
| l4    | ESC[61_  |
| l5    | ESC[63_  |
| l6    | ESC[65_  |
| l7    | ESC[67_  |

---

# Example NOTIS Screen

A NOTIS screen might display:

```text
+------------------------------------------------+
| Customer Registration                           |
+------------------------------------------------+
| Name:                                           |
| Address:                                        |
| Phone:                                          |
+------------------------------------------------+
| F1=Save  F2=Delete  F3=Search  F4=Print         |
+------------------------------------------------+
```

Internally:

```text
F1 -> l0
F2 -> l1
F3 -> l2
F4 -> l3
```

Applications redefine labels dynamically.

---

# Keyboard Matrix Summary

## Cursor Keys

| Key   | Sequence |
| ----- | -------- |
| Left  | ^H       |
| Right | ^X       |
| Up    | ^\       |
| Down  | ^K       |
| Home  | ^]       |

---

## Editing Keys

| Key   | Function         |
| ----- | ---------------- |
| kdch1 | Delete Character |
| kich1 | Insert Character |
| kdl1  | Delete Line      |
| kil1  | Insert Line      |
| ked   | Erase To End     |
| kel   | Erase Line       |

---

## Form Navigation

| Key   | Function      |
| ----- | ------------- |
| kpp   | Previous Page |
| knp   | Next Page     |
| kctab | Previous Tab  |
| khts  | Set Tab       |

---

## Application Control

| Key  | Function |
| ---- | -------- |
| kcan | Cancel   |
| kcmd | Command  |
| khlp | Help     |
| kext | Exit     |
| kprt | Print    |

---

## Block Operations

| Key  | Function |
| ---- | -------- |
| kcpy | Copy     |
| kmov | Move     |

---

## Formatting

| Key   | Function      |
| ----- | ------------- |
| kund  | Underline     |
| blink | Blink         |
| rev   | Reverse Video |

---

# Comparison With VT100

| Feature               | VT100   | TDV2200          |
| --------------------- | ------- | ---------------- |
| ANSI Cursor Control   | Yes     | Yes              |
| Function Keys         | Limited | Extensive        |
| Soft Labels           | No      | Yes              |
| Block Operations      | No      | Yes              |
| Copy Key              | No      | Yes              |
| Move Key              | No      | Yes              |
| Print Key             | No      | Yes              |
| Help Key              | No      | Yes              |
| Local Printer Support | Limited | Extensive        |
| Forms Processing      | Limited | Built-in Support |

---

# Architectural Conclusion

The TDV2200 should not be viewed merely as a character terminal.

It is closer to:

```text
Terminal
+
Forms Processor
+
Office Workstation
+
Printer Controller
```

This explains why the terminfo definition is dramatically larger than a VT100 definition.

The keyboard was designed around structured applications rather than shell usage.

# END OF PART 11
# TDV2200 / TDV1200 REFERENCE

## Part 12 - Alternative TDV2200 Definitions, Cross References and Emulator Notes

---

# Alternative TDV2200 Definition (1997 Posting)

The uploaded material contains an alternative TDV2200 definition posted to Usenet in 1997.

This version was intended for:

* Linux
* BSD
* Modern Unix systems

rather than Norsk Data Unix environments.

---

# Why Multiple Definitions Exist

Terminal definitions evolved because:

1. Different operating systems interpreted termcap differently.
2. Different terminal firmware revisions existed.
3. Some applications required exact keyboard mappings.
4. Some systems preferred ANSI-compatible sequences.
5. Some systems used proprietary TDV sequences.

---

# Comparison: TDV1200 vs TDV2200

## Screen Control

| Capability       | TDV1200 | TDV2200 |
| ---------------- | ------- | ------- |
| Clear Screen     | Yes     | Yes     |
| Cursor Position  | Yes     | Yes     |
| Insert Character | Yes     | Yes     |
| Delete Character | Yes     | Yes     |
| Insert Line      | Yes     | Yes     |
| Delete Line      | Yes     | Yes     |
| Scroll           | Yes     | Yes     |

Result:

```text id="1p4y3m"
Nearly identical
```

---

## Keyboard

### TDV1200

Uses more ANSI-like sequences:

```text id="0s2k14"
ESC[A
ESC[B
ESC[C
ESC[D
```

for cursor navigation.

### TDV2200

Uses control characters:

```text id="ik3xyg"
^H
^K
^X
^\
```

for navigation.

Result:

```text id="b7v4fu"
TDV2200 keyboard protocol is faster and more compact.
```

---

## Initialization

### TDV1200

```text id="u1w88v"
ESC[>6;8;9l
ESCQ
ESC[66h
ESC[1Q
ESC[0m
```

### TDV2200

```text id="gm0wzn"
ESC[62;36;66l
ESCQ
ESC[36;62;62h
ESC[0m
```

Result:

Different firmware mode architecture.

---

# Termcap to Terminfo Cross Reference

One of the most useful tables when implementing an emulator.

---

## Cursor Movement

| Termcap | Terminfo | Meaning         |
| ------- | -------- | --------------- |
| cm      | cup      | Cursor Position |
| up      | cuu1     | Cursor Up       |
| do      | cud1     | Cursor Down     |
| nd      | cuf1     | Cursor Right    |
| kl      | cub1     | Cursor Left     |
| ho      | home     | Home Cursor     |

---

## Screen Management

| Termcap | Terminfo | Meaning                 |
| ------- | -------- | ----------------------- |
| cl      | clear    | Clear Screen            |
| cd      | ed       | Clear To End Of Display |
| ce      | el       | Clear To End Of Line    |

---

## Character Editing

| Termcap | Terminfo | Meaning          |
| ------- | -------- | ---------------- |
| ic      | ich1     | Insert Character |
| dc      | dch1     | Delete Character |

---

## Line Editing

| Termcap | Terminfo | Meaning        |
| ------- | -------- | -------------- |
| al      | il1      | Insert Line    |
| dl      | dl1      | Delete Line    |
| AL      | il       | Insert N Lines |
| DL      | dl       | Delete N Lines |

---

## Attributes

| Termcap | Terminfo | Meaning       |
| ------- | -------- | ------------- |
| so      | rev      | Reverse Video |
| se      | sgr0     | End Reverse   |
| us      | smul     | Underline     |
| ue      | rmul     | End Underline |
| vs      | dim      | Dim           |
| ve      | sgr0     | End Dim       |

---

## Printer Support

| Termcap | Terminfo | Meaning     |
| ------- | -------- | ----------- |
| PS      | mc4      | Printer On  |
| PN      | mc5      | Printer Off |

---

# Emulator Implementation Guide

---

# Minimum Emulator

To support:

* vi
* more
* less
* shell programs

you need:

| Capability | Required |
| ---------- | -------- |
| clear      | Yes      |
| cup        | Yes      |
| cuf1       | Yes      |
| cub1       | Yes      |
| cuu1       | Yes      |
| cud1       | Yes      |
| el         | Yes      |
| ed         | Yes      |

---

# Full Screen Applications

To support:

* NOTIS
* forms applications
* editors

also implement:

| Capability    | Required |
| ------------- | -------- |
| il            | Yes      |
| dl            | Yes      |
| ich           | Yes      |
| dch           | Yes      |
| reverse video | Yes      |
| underline     | Yes      |

---

# Complete TDV Emulation

Requires:

| Capability Group | Required |
| ---------------- | -------- |
| Graphics         | Yes      |
| Soft Labels      | Yes      |
| Function Keys    | Yes      |
| Printer Support  | Yes      |
| TDV Modes        | Yes      |

---

# Graphics Emulation Strategy

The uploaded material strongly suggests:

```text id="7ecwxq"
GH
GV
GL
GR
GU
GD
G1
G2
G3
G4
```

represent logical drawing elements.

Recommended implementation:

| Command | Unicode |
| ------- | ------- |
| GH      | ─       |
| GV      | │       |
| G1      | ┌       |
| G2      | ┐       |
| G3      | └       |
| G4      | ┘       |
| GU      | ┬       |
| GD      | ┴       |
| GL      | ├       |
| GR      | ┤       |

This provides a modern approximation.

---

# Keyboard Emulation Strategy

Map:

| TDV Key | PC Key            |
| ------- | ----------------- |
| kHLP    | F1                |
| kCAN    | Esc               |
| kCMD    | F2                |
| kCPY    | Ctrl+C            |
| kMOV    | Ctrl+M            |
| kPRT    | PrintScreen       |
| kEXT    | Alt+F4 equivalent |

for emulator usability.

---

# Most Important Discovery

The uploaded terminfo definitions reveal that the TDV2200 was not merely:

```text id="hk9m9q"
ANSI terminal
```

It was:

```text id="6c4g29"
ANSI terminal
+
office workstation
+
forms processor
+
local printer controller
+
programmable keyboard system
```

which explains the unusually rich keyboard definition.

---

# Capability Index

## Display

* clear
* cup
* home
* ed
* el

## Cursor

* cuu1
* cud1
* cub1
* cuf1

## Editing

* ich1
* dch1
* il1
* dl1

## Attributes

* rev
* smul
* rmul
* blink
* dim

## Graphics

* GH
* GV
* GL
* GR
* GU
* GD
* G1
* G2
* G3
* G4
* GC

## Printer

* mc4
* mc5

## Application Keys

* kcan
* kcmd
* kcpy
* khlp
* kext
* kmov
* kprt

## Navigation

* kpp
* knp
* khome

## Tabs

* cbt
* khts

## Function Keys

* kf0-kf63

## Soft Labels

* l0-l7

---

# END OF PART 12
# TDV1200 TERMINFO REFERENCE

## Part 13 - TDV1200 Extended Keyboard Architecture

---

# TDV1200 Terminfo Overview

The TDV1200 terminfo definition exposes substantially more of the keyboard architecture than the TDV1200 termcap entry.

The keyboard appears designed for:

* Data entry
* Form processing
* Text editing
* NOTIS applications
* Office automation

Like the TDV2200, many keys represent logical application functions rather than simple character input.

---

# Keyboard Architecture

The TDV1200 keyboard can be divided into six logical groups:

| Group               | Purpose                     |
| ------------------- | --------------------------- |
| Cursor Control      | Movement                    |
| Editing             | Character and line editing  |
| Forms Navigation    | Moving between fields       |
| Application Control | Help, Exit, Command         |
| Printer Functions   | Printing                    |
| Function Keys       | Application-defined actions |

---

# Cursor Navigation

## Capability: kcub1

Original:

```terminfo
kcub1=\E[D
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kcub1       |
| Meaning    | Cursor Left |
| Sequence   | ESC[D       |
| ANSI Name  | CUB         |

---

## Capability: kcuf1

Original:

```terminfo
kcuf1=\E[C
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | kcuf1        |
| Meaning    | Cursor Right |
| Sequence   | ESC[C        |
| ANSI Name  | CUF          |

---

## Capability: kcuu1

Original:

```terminfo
kcuu1=\E[A
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kcuu1     |
| Meaning    | Cursor Up |
| Sequence   | ESC[A     |
| ANSI Name  | CUU       |

---

## Capability: kcud1

Original:

```terminfo
kcud1=\E[B
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kcud1       |
| Meaning    | Cursor Down |
| Sequence   | ESC[B       |
| ANSI Name  | CUD         |

---

## Capability: khome

Original:

```terminfo
khome=\E[H
```

| Property   | Value |
| ---------- | ----- |
| Capability | khome |
| Meaning    | Home  |
| Sequence   | ESC[H |

---

# Observation

Unlike TDV2200:

```text
TDV1200
  → ANSI escape sequences

TDV2200
  → Control characters
```

The TDV1200 keyboard protocol is closer to DEC terminals.

---

# Editing Functions

## Capability: kdch1

Original:

```terminfo
kdch1=\E[P
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | kdch1            |
| Meaning    | Delete Character |
| Sequence   | ESC[P            |

---

## Capability: kich1

Original:

```terminfo
kich1=\E[@
```

| Property   | Value            |
| ---------- | ---------------- |
| Capability | kich1            |
| Meaning    | Insert Character |
| Sequence   | ESC[@            |

---

## Capability: kdl1

Original:

```terminfo
kdl1=\E[M
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kdl1        |
| Meaning    | Delete Line |
| Sequence   | ESC[M       |

---

## Capability: kil1

Original:

```terminfo
kil1=\E[L
```

| Property   | Value       |
| ---------- | ----------- |
| Capability | kil1        |
| Meaning    | Insert Line |
| Sequence   | ESC[L       |

---

## Capability: kel

Original:

```terminfo
kel=\E[11_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | kel             |
| Meaning    | Erase Line      |
| Sequence   | ESC[11_         |
| Type       | TDV Proprietary |

---

## Capability: ked

Original:

```terminfo
ked=\E[48_
```

| Property   | Value           |
| ---------- | --------------- |
| Capability | ked             |
| Meaning    | Erase To End    |
| Sequence   | ESC[48_         |
| Type       | TDV Proprietary |

---

# Forms Navigation

The TDV1200 was clearly intended for structured forms.

---

## Capability: kpp

Original:

```terminfo
kpp=\E[S
```

| Property   | Value         |
| ---------- | ------------- |
| Capability | kpp           |
| Meaning    | Previous Page |
| Sequence   | ESC[S         |

---

## Capability: knp

Original:

```terminfo
knp=\E[T
```

| Property   | Value     |
| ---------- | --------- |
| Capability | knp       |
| Meaning    | Next Page |
| Sequence   | ESC[T     |

---

## Capability: khts

Original:

```terminfo
khts=\E[36_
```

| Property   | Value        |
| ---------- | ------------ |
| Capability | khts         |
| Meaning    | Set Tab Stop |
| Sequence   | ESC[36_      |

---

## Capability: kctab

Original:

```terminfo
kctab=\E[38_
```

| Property   | Value    |
| ---------- | -------- |
| Capability | kctab    |
| Meaning    | Back Tab |
| Sequence   | ESC[38_  |

---

# Application Control

## Capability: kcan

Original:

```terminfo
kcan=\E[11_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kcan    |
| Meaning    | Cancel  |
| Sequence   | ESC[11_ |

---

## Capability: kcmd

Original:

```terminfo
kcmd=\E[43_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kcmd    |
| Meaning    | Command |
| Sequence   | ESC[43_ |

---

## Capability: khlp

Original:

```terminfo
khlp=\E[47_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | khlp    |
| Meaning    | Help    |
| Sequence   | ESC[47_ |

---

## Capability: kext

Original:

```terminfo
kext=\E[49_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kext    |
| Meaning    | Exit    |
| Sequence   | ESC[49_ |

---

# Block Operations

The TDV1200 includes dedicated block-processing keys.

---

## Capability: kcpy

Original:

```terminfo
kcpy=\E[13_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kcpy    |
| Meaning    | Copy    |
| Sequence   | ESC[13_ |

---

## Capability: kmov

Original:

```terminfo
kmov=\E[15_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kmov    |
| Meaning    | Move    |
| Sequence   | ESC[15_ |

---

# Printer Operations

## Capability: kprt

Original:

```terminfo
kprt=\E[45_
```

| Property   | Value   |
| ---------- | ------- |
| Capability | kprt    |
| Meaning    | Print   |
| Sequence   | ESC[45_ |

---

# Formatting Controls

## Capability: kund

Original:

```terminfo
kund=\E[31_
```

| Property   | Value     |
| ---------- | --------- |
| Capability | kund      |
| Meaning    | Underline |
| Sequence   | ESC[31_   |

---

## Capability: kUND

Original:

```terminfo
kUND=\E[31_
```

| Property   | Value             |
| ---------- | ----------------- |
| Capability | kUND              |
| Meaning    | Underline Command |
| Sequence   | ESC[31_           |

---

# Function Key Architecture

The TDV1200 exposes an extensive logical function-key space.

Unlike a VT100:

```text
PF1
PF2
PF3
PF4
```

the TDV1200 supports:

```text
F1-F63
```

through combinations of:

* Physical keys
* Shift states
* Application-defined mappings

---

# Physical Function Key Group

| Key | Sequence |
| --- | -------- |
| k1  | ESC[M    |
| k2  | ESC[L    |
| k3  | ESC[=F   |
| k4  | ESC[=G   |
| k5  | ESCOP    |
| k6  | ESCOQ    |
| k7  | ESCOR    |
| k8  | ESCOS    |

---

# Observation

The first TDV1200 function keys resemble:

```text
DEC PF1-PF4
```

while later keys are TDV-specific.

This suggests the TDV1200 was designed to maintain compatibility with existing DEC-oriented software while extending functionality for Norsk Data environments.

---

# Summary

The TDV1200 keyboard architecture sits midway between:

```text
VT100
```

and

```text
TDV2200
```

It retains ANSI/DEC-style cursor sequences while already exposing:

* Application control keys
* Copy/Move functions
* Printer support
* Forms navigation
* Extended logical function keys

This makes it one of the more sophisticated terminal architectures of its era.

# END OF PART 13
# TDV1200 TERMINFO REFERENCE

## Part 14 - Function Key Matrix, Soft Labels and Firmware Architecture

---

# TDV1200 Logical Function Key Architecture

The terminfo definition exposes a much larger logical keyboard than the physical keyboard would suggest.

The terminal firmware translates:

```text id="sxu1iv"
Physical Key
+
Shift State
+
Application Mode
```

into:

```text id="7xv4vn"
Logical Function Key
```

which is then transmitted to the host.

---

# Function Key Matrix

The following table summarizes the logical function-key space exposed through terminfo.

| Logical Key | Purpose                 |
| ----------- | ----------------------- |
| kf0-kf8     | Primary function keys   |
| kf9-kf20    | Secondary function keys |
| kf21-kf40   | Application functions   |
| kf41-kf63   | Extended functions      |

---

# Primary Function Keys

## kf1

Original:

```terminfo id="30t56j"
kf1=\E[M
```

| Property     | Value    |
| ------------ | -------- |
| Logical Key  | kf1      |
| Physical Key | F1       |
| Sequence     | ESC[M    |
| Category     | Function |

---

## kf2

Original:

```terminfo id="kmv8pz"
kf2=\E[L
```

| Property     | Value |
| ------------ | ----- |
| Logical Key  | kf2   |
| Physical Key | F2    |
| Sequence     | ESC[L |

---

## kf3

Original:

```terminfo id="2u8kzc"
kf3=\E[=F
```

| Property     | Value  |
| ------------ | ------ |
| Logical Key  | kf3    |
| Physical Key | F3     |
| Sequence     | ESC[=F |

---

## kf4

Original:

```terminfo id="s9x49l"
kf4=\E[=G
```

| Property     | Value  |
| ------------ | ------ |
| Logical Key  | kf4    |
| Physical Key | F4     |
| Sequence     | ESC[=G |

---

## kf5

Original:

```terminfo id="a97mhd"
kf5=\EOP
```

| Property      | Value   |
| ------------- | ------- |
| Logical Key   | kf5     |
| Physical Key  | F5      |
| Sequence      | ESCOP   |
| Compatibility | DEC PF1 |

---

## kf6

Original:

```terminfo id="vjlwmr"
kf6=\EOQ
```

| Property      | Value   |
| ------------- | ------- |
| Logical Key   | kf6     |
| Physical Key  | F6      |
| Sequence      | ESCOQ   |
| Compatibility | DEC PF2 |

---

## kf7

Original:

```terminfo id="mjlwm8"
kf7=\EOR
```

| Property      | Value   |
| ------------- | ------- |
| Logical Key   | kf7     |
| Physical Key  | F7      |
| Sequence      | ESCOR   |
| Compatibility | DEC PF3 |

---

## kf8

Original:

```terminfo id="iym1zy"
kf8=\EOS
```

| Property      | Value   |
| ------------- | ------- |
| Logical Key   | kf8     |
| Physical Key  | F8      |
| Sequence      | ESCOS   |
| Compatibility | DEC PF4 |

---

# DEC Compatibility Layer

One of the most interesting discoveries.

The TDV1200 preserves:

```text id="r9tw1t"
PF1
PF2
PF3
PF4
```

compatibility.

This suggests software migration from:

* VT52
* VT100
* VT102
* VT220

environments.

---

# Soft Label Architecture

The TDV1200 contains programmable soft labels.

---

## Soft Label Group

### l0

Original:

```terminfo id="4zwg8q"
l0=\E[=d
```

| Property | Value  |
| -------- | ------ |
| Label    | l0     |
| Sequence | ESC[=d |

---

### l1

Original:

```terminfo id="c6l8mb"
l1=\E[=e
```

| Property | Value  |
| -------- | ------ |
| Label    | l1     |
| Sequence | ESC[=e |

---

### l2

Original:

```terminfo id="n2fjrk"
l2=\E[=f
```

| Property | Value  |
| -------- | ------ |
| Label    | l2     |
| Sequence | ESC[=f |

---

### l3

Original:

```terminfo id="jv87x9"
l3=\E[=g
```

| Property | Value  |
| -------- | ------ |
| Label    | l3     |
| Sequence | ESC[=g |

---

### l4

Original:

```terminfo id="x5ymtw"
l4=\E[=h
```

| Property | Value  |
| -------- | ------ |
| Label    | l4     |
| Sequence | ESC[=h |

---

### l5

Original:

```terminfo id="34xgny"
l5=\E[=i
```

| Property | Value  |
| -------- | ------ |
| Label    | l5     |
| Sequence | ESC[=i |

---

### l6

Original:

```terminfo id="x4d0cu"
l6=\E[=j
```

| Property | Value  |
| -------- | ------ |
| Label    | l6     |
| Sequence | ESC[=j |

---

### l7

Original:

```terminfo id="gz6d57"
l7=\E[=k
```

| Property | Value  |
| -------- | ------ |
| Label    | l7     |
| Sequence | ESC[=k |

---

# Typical Application Usage

Applications could dynamically assign labels.

Example:

```text id="n6tgzw"
F1 Save
F2 Search
F3 Delete
F4 Print
F5 Help
F6 Exit
```

The displayed label changed while the physical key remained unchanged.

---

# Firmware Operating Modes

The TDV1200 initialization string contains several private mode controls.

Original:

```terminfo id="zv1n9f"
ESC[>6;8;9l
ESCQ
ESC[66h
ESC[1Q
ESC[0m
```

---

# Private Mode Analysis

## Mode 6

```text id="0jhb0v"
ESC[>6l
```

| Property | Value       |
| -------- | ----------- |
| Mode     | 6           |
| Type     | TDV Private |
| Function | Unknown     |

---

## Mode 8

```text id="n2sm08"
ESC[>8l
```

| Property | Value       |
| -------- | ----------- |
| Mode     | 8           |
| Type     | TDV Private |
| Function | Unknown     |

---

## Mode 9

```text id="mgwb7e"
ESC[>9l
```

| Property | Value       |
| -------- | ----------- |
| Mode     | 9           |
| Type     | TDV Private |
| Function | Unknown     |

---

## Mode 66

```text id="8ppffn"
ESC[66h
```

| Property | Value       |
| -------- | ----------- |
| Mode     | 66          |
| Type     | TDV Private |
| Function | Unknown     |

### Observation

Mode 66 appears in both:

* TDV1200
* TDV2200

which suggests a core TDV firmware feature.

---

# TDV1200 vs TDV2200 Keyboard Comparison

| Feature         | TDV1200  | TDV2200       |
| --------------- | -------- | ------------- |
| Cursor Keys     | ANSI     | Control Chars |
| Function Keys   | Extended | Extended      |
| Soft Labels     | Yes      | Yes           |
| Printer Control | Yes      | Yes           |
| Copy/Move Keys  | Yes      | Yes           |
| Help Key        | Yes      | Yes           |
| Tab Control     | Yes      | Yes           |

---

# Important Difference

## TDV1200

Uses:

```text id="itwlf4"
ESC[A
ESC[B
ESC[C
ESC[D
```

for navigation.

---

## TDV2200

Uses:

```text id="mmp34t"
^\
^K
^X
^H
```

for navigation.

---

# Why This Matters

When implementing an emulator:

### TDV1200

Can often be emulated using:

```text id="xg2r9q"
ANSI terminal engine
```

plus TDV extensions.

### TDV2200

Requires:

```text id="fg7d5s"
custom keyboard decoder
```

because cursor keys do not generate ANSI sequences.

---

# Summary

The TDV1200 represents a transitional architecture:

```text id="5ngl9f"
DEC-style terminal
+
TDV office automation features
+
soft labels
+
extended function keys
```

while the TDV2200 moves further toward a dedicated office workstation terminal.

# END OF PART 14

# ND246 TERMINFO REFERENCE

## Part 15 - Binary Cursor Protocol, Visual Bell and TDV2200 Compatibility

---

# ND246 Terminfo Overview

The ND246 occupies a unique position in the Norsk Data terminal family.

Unlike:

* ND224
* ND242

which inherit TDV2115 behavior,

and unlike:

* ND320

which simply inherits TDV2200 behavior,

the ND246 contains explicit overrides.

This indicates that Norsk Data wanted to preserve compatibility with existing ND software while adopting the TDV2200 feature set.

---

# Original ND246 Definition

```terminfo
nd246|Norsk Data 246|VTM53,
        cup=^P%p1%c%p2%c,
        flash=^B^A^A^A^A^A^C,
        use=tdv2200,
```

---

# Capability: cup

Original:

```terminfo
cup=^P%p1%c%p2%c
```

| Property        | Value                   |
| --------------- | ----------------------- |
| Capability      | cup                     |
| Meaning         | Cursor Position         |
| Type            | Proprietary ND Protocol |
| ANSI Compatible | No                      |
| Overrides       | TDV2200                 |

---

# Detailed Analysis

Unlike TDV2200:

```text
ESC[row;columnH
```

the ND246 transmits:

```text
^P row column
```

where:

```text
^P = DLE = 0x10
```

followed by two raw coordinate bytes.

---

# Binary Format

Structure:

```text
+------+---------+---------+
| 0x10 | Row     | Column  |
+------+---------+---------+
```

---

# Example

Move cursor to:

```text
Row    10
Column 20
```

Result:

```hex
10 0A 14
```

---

# Comparison With ANSI

ANSI:

```text
ESC[10;20H
```

Hex:

```hex
1B 5B 31 30 3B 32 30 48
```

8 bytes

---

ND246:

```hex
10 0A 14
```

3 bytes

---

# Efficiency Analysis

| Protocol | Bytes |
| -------- | ----- |
| ANSI     | 8     |
| ND246    | 3     |

Reduction:

```text
62.5%
```

This was important on:

* 1200 bps links
* 2400 bps links
* X.21 networks
* X.25 PAD connections

---

# Historical Context

Norsk Data software often ran through:

* PADs
* X.25
* synchronous links
* multiplexers

Every transmitted byte mattered.

The ND246 protocol reflects that design philosophy.

---

# Capability: flash

Original:

```terminfo
flash=^B^A^A^A^A^A^C
```

| Property        | Value       |
| --------------- | ----------- |
| Capability      | flash       |
| Meaning         | Visual Bell |
| Type            | Proprietary |
| ANSI Equivalent | None        |

---

# Byte Breakdown

| Byte | Name | Hex |
| ---- | ---- | --- |
| ^B   | STX  | 02  |
| ^A   | SOH  | 01  |
| ^A   | SOH  | 01  |
| ^A   | SOH  | 01  |
| ^A   | SOH  | 01  |
| ^A   | SOH  | 01  |
| ^C   | ETX  | 03  |

---

# Likely Interpretation

Structure resembles:

```text
STX
COMMAND
DATA
ETX
```

style packet framing.

Possible meaning:

```text
Start Flash
Flash Screen
End Flash
```

---

# Why Use Visual Bell?

Audible bells become problematic when:

* many terminals share a room
* operators work continuously
* alarms occur frequently

A visual flash is less disruptive.

---

# Capability: use

Original:

```terminfo
use=tdv2200
```

| Property   | Value              |
| ---------- | ------------------ |
| Capability | use                |
| Meaning    | Inherit Definition |
| Parent     | tdv2200            |

---

# Inherited Features

Everything else comes directly from TDV2200.

---

# Cursor System

Inherited:

* Left
* Right
* Up
* Down
* Home

except absolute cursor positioning.

---

# Display Management

Inherited:

* clear
* el
* ed
* dl
* il
* dch
* ich

---

# Display Attributes

Inherited:

* reverse
* underline
* blink
* dim

---

# Graphics

Inherited:

* GH
* GV
* GL
* GR
* GU
* GD
* G1
* G2
* G3
* G4
* GC

---

# Function Keys

Inherited:

```text
kf0-kf63
```

Entire TDV2200 keyboard architecture is available.

---

# Soft Labels

Inherited:

```text
l0-l7
```

---

# Printer Support

Inherited:

```text
mc4
mc5
```

Printer control remains unchanged.

---

# Effective Architecture

Conceptually:

```text
TDV2200
    +
ND Binary Cursor Protocol
    +
ND Visual Bell
    =
ND246
```

---

# Compatibility Goals

The design suggests Norsk Data wanted:

### Existing Software

To continue using:

```text
^P row col
```

cursor commands.

---

### New Software

To gain:

```text
TDV2200
graphics
function keys
printer support
```

without modification.

---

# Emulator Design Implications

When implementing ND246:

## Option 1

Native ND246 Mode

Implement:

```text
^P row col
```

directly.

Most accurate.

---

## Option 2

Translate Internally

Convert:

```text
^P row col
```

to internal cursor coordinates.

Simpler implementation.

---

# Recommended Internal Model

```text
Terminal Core
       ^
       |
+------+------+
| ND246 Driver |
+-------------+
| TDV2200 Driver |
+-------------+
```

Only the front-end protocol differs.

The screen engine can be shared.

---

# Compatibility Summary

| Feature                | TDV2200  | ND246       |
| ---------------------- | -------- | ----------- |
| ANSI Cursor Position   | Yes      | No          |
| Binary Cursor Position | No       | Yes         |
| Graphics               | Yes      | Yes         |
| Function Keys          | Yes      | Yes         |
| Soft Labels            | Yes      | Yes         |
| Printer Support        | Yes      | Yes         |
| Visual Bell            | Standard | ND Specific |

---

# Most Important Discovery

The ND246 definition demonstrates that Norsk Data intentionally preserved legacy cursor protocols while adopting the TDV2200 terminal architecture.

This explains why older ND applications could continue to function without modification.

# END OF PART 15

# NORSK DATA TERMINAL FAMILY

## Part 16 - ND246 Keyboard Protocol, ND320 Analysis and Complete Family Comparison

---

# ND246 Keyboard Architecture

One of the interesting aspects of the ND246 definition is what it does **not** override.

The terminal overrides:

```text
Cursor Positioning
Visual Bell
```

but leaves the keyboard definition inherited from TDV2200.

---

# Effective Keyboard Architecture

The actual configuration becomes:

```text
ND246
  Cursor Protocol      -> ND Native
  Visual Bell          -> ND Native
  Keyboard             -> TDV2200
  Graphics             -> TDV2200
  Printer Support      -> TDV2200
  Screen Editing       -> TDV2200
```

---

# Effective Cursor Keys

Inherited from TDV2200:

| Function | Character | ASCII | Hex |
| -------- | --------- | ----- | --- |
| Left     | ^H        | 8     | 08  |
| Right    | ^X        | 24    | 18  |
| Up       | ^\        | 28    | 1C  |
| Down     | ^K        | 11    | 0B  |
| Home     | ^]        | 29    | 1D  |

---

# Why Keep TDV2200 Keyboard?

This decision was probably deliberate.

Changing cursor-positioning protocols:

```text
Host -> Terminal
```

does not affect keyboard input:

```text
Terminal -> Host
```

Therefore existing applications could continue operating.

---

# ND246 and SINTRAN

The ND246 was heavily used with:

* SINTRAN III
* SINTRAN III VSX
* NOTIS
* Office Information Systems

The binary cursor protocol is particularly interesting because it aligns with Norsk Data's general philosophy:

```text
CPU Cycles Expensive
Memory Expensive
Bandwidth Expensive
```

Use the smallest possible protocol.

---

# Example Screen Update

ANSI terminal:

```text
ESC[10;20H
HELLO
```

Bytes transmitted:

```text
8 + 5 = 13 bytes
```

---

ND246:

```text
10 0A 14
HELLO
```

Bytes transmitted:

```text
3 + 5 = 8 bytes
```

---

Reduction:

```text
38%
```

For heavily updated forms this becomes significant.

---

# ND246 Visual Bell Reverse Engineering

Visual bell:

```text
02 01 01 01 01 01 03
```

---

# Possible Interpretation 1

Packet Structure

```text
STX
FLASH
FLASH
FLASH
FLASH
FLASH
ETX
```

---

# Possible Interpretation 2

Mini Command Protocol

```text
STX
Command 1
Parameter 1
Parameter 1
Parameter 1
Parameter 1
ETX
```

---

# Possible Interpretation 3

Legacy VTM Protocol

The sequence resembles early Norsk Data terminal command framing.

Several early ND devices used:

```text
STX
...
ETX
```

framing.

---

# Verification Required

To determine actual behavior we need one of:

* ND246 firmware dump
* VTM53 programming manual
* Oscilloscope capture
* Real terminal test

Without those sources we can only identify the structure, not the precise meaning.

---

# ND320 Analysis

Original Definition:

```termcap
n3|nd320|Norsk Data 320,VTM 93,103:
        tc=tdv2200
```

---

# What This Tells Us

ND320 contains:

```text
No Overrides
```

Every capability comes from TDV2200.

---

# Effective Architecture

```text
ND320
    =
TDV2200
```

from the operating system perspective.

---

# Why Have Separate Names?

Likely because:

### Hardware

Different terminal hardware.

### Marketing

Different product family.

### Firmware

Minor internal firmware changes.

### Compatibility

Applications may check terminal type.

---

# OS Perspective

SINTRAN sees:

```text
ND320
```

Application requests:

```text
Cursor Position
Insert Line
Delete Line
Function Key
```

Result:

```text
TDV2200 sequences
```

are transmitted.

---

# Terminal Family Evolution

Based on the supplied definitions.

---

# Generation 1

## TDV2115 Family

Members:

```text
ND224
ND242
```

Characteristics:

* Older architecture
* Unknown capabilities
* Earlier VTM generation

---

# Generation 2

## TDV1200

Characteristics:

* ANSI-oriented
* DEC compatible
* Extended office functions
* Soft labels

---

# Generation 3

## TDV2200

Characteristics:

* Faster keyboard encoding
* Larger logical keyboard
* More sophisticated firmware
* Office workstation orientation

---

# Generation 4

## ND246

Characteristics:

* TDV2200 compatibility
* Legacy ND cursor protocol
* Legacy ND visual bell

---

# Generation 5

## ND320

Characteristics:

* Pure TDV2200 behavior
* New hardware platform

---

# Complete Family Comparison

| Feature                | ND224   | ND242   | TDV1200 | TDV2200 | ND246   | ND320   |
| ---------------------- | ------- | ------- | ------- | ------- | ------- | ------- |
| Base                   | TDV2115 | TDV2115 | Native  | Native  | TDV2200 | TDV2200 |
| ANSI Cursor Position   | ?       | ?       | Yes     | Yes     | No      | Yes     |
| Binary Cursor Position | ?       | ?       | No      | No      | Yes     | No      |
| Soft Labels            | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Graphics               | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Printer Support        | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Help Key               | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Copy Key               | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Move Key               | ?       | ?       | Yes     | Yes     | Yes     | Yes     |
| Extended Function Keys | ?       | ?       | Yes     | Yes     | Yes     | Yes     |

---

# Emulator Architecture Recommendation

A modern emulator should probably implement:

```text
TDV2200 Core
```

and then layer compatibility modes on top.

---

# Recommended Structure

```text
+---------------------+
| Terminal Core       |
+---------------------+
          ^
          |
+---------+---------+
| TDV2200 Driver    |
+-------------------+
| TDV1200 Driver    |
+-------------------+
| ND246 Driver      |
+-------------------+
| ND320 Driver      |
+-------------------+
```

---

# Driver Differences

## TDV1200

Override:

```text
Keyboard Encoding
Initialization
```

---

## TDV2200

Base Implementation

---

## ND246

Override:

```text
Cursor Position
Visual Bell
```

---

## ND320

Alias:

```text
TDV2200
```

---

# Final Conclusions

The material reveals that the TDV2200 became the foundation for later Norsk Data terminal products.

The most important discovery is that the ND246 preserved a compact binary cursor-positioning protocol while inheriting nearly all TDV2200 functionality. This strongly suggests a compatibility strategy aimed at protecting existing SINTRAN and NOTIS software investments while introducing newer terminal hardware.

# END OF PART 16

# TDV2200 REVERSE ENGINEERING REFERENCE

## Part 17 - TDV Private Escape Sequences and Keyboard Encoding Analysis

---

# Introduction

The TDV2200 uses several classes of escape sequences:

| Group              | Example       |
| ------------------ | ------------- |
| ANSI Standard      | ESC[A         |
| ANSI Extended      | ESC[5m        |
| TDV Function Keys  | ESC[50_       |
| TDV Graphics       | ESC15         |
| TDV Private Modes  | ESC[=C        |
| TDV Initialization | ESC[62;36;66l |

The ANSI portions are well understood.

The TDV-specific portions require reverse engineering.

---

# TDV Function Key Encoding Scheme

One of the first things that becomes apparent is that TDV keys follow a consistent pattern.

Example:

```text
ESC[50_
ESC[52_
ESC[55_
ESC[58_
ESC[60_
ESC[62_
ESC[64_
ESC[66_
```

---

# Common Structure

All keys appear to use:

```text
ESC [ nn _
```

where:

| Field | Meaning        |
| ----- | -------------- |
| ESC   | Escape         |
| [     | CSI            |
| nn    | Key Identifier |
| _     | TDV Terminator |

---

# Example

Function Key 1:

```text
ESC[50_
```

Hex:

```hex
1B 5B 35 30 5F
```

---

# Example

Function Key 8:

```text
ESC[66_
```

Hex:

```hex
1B 5B 36 36 5F
```

---

# Important Observation

Unlike ANSI:

```text
ESCOP
```

or

```text
ESC[15~
```

TDV uses:

```text
ESC[NN_
```

for nearly everything.

This makes decoding extremely easy.

---

# Logical Key Number Space

The firmware appears to allocate identifiers from approximately:

```text
00
through
100
```

---

# Examples

| Key  | Sequence |
| ---- | -------- |
| kf11 | ESC[00_  |
| kf23 | ESC[01_  |
| kf25 | ESC[02_  |
| kf26 | ESC[03_  |
| kf27 | ESC[04_  |
| kf28 | ESC[06_  |

---

# Hypothesis

The terminal internally uses:

```text
Key Number
```

rather than

```text
Physical Key
```

encoding.

Applications receive logical actions.

---

# TDV Keyboard Namespace

The keyboard namespace appears divided into ranges.

---

# Range 00-19

Editing and control functions.

Examples:

| Code | Function         |
| ---- | ---------------- |
| 10   | Delete Character |
| 11   | Cancel           |
| 13   | Copy             |
| 15   | Move             |
| 18   | Insert Line      |
| 19   | Delete Line      |

---

# Range 20-39

Navigation and formatting.

Examples:

| Code | Function     |
| ---- | ------------ |
| 20   | Option       |
| 21   | Shift Option |
| 31   | Underline    |
| 35   | Begin        |
| 36   | Set Tab      |
| 38   | Back Tab     |

---

# Range 40-49

Application functions.

Examples:

| Code | Function     |
| ---- | ------------ |
| 43   | Command      |
| 45   | Print        |
| 47   | Help         |
| 48   | Erase To End |
| 49   | Exit         |

---

# Range 50-69

Primary function keys.

Examples:

| Code | Function |
| ---- | -------- |
| 50   | F1       |
| 52   | F2       |
| 55   | F3       |
| 58   | F4       |
| 60   | F5       |
| 62   | F6       |
| 64   | F7       |
| 66   | F8       |

---

# Range 80-99

Extended function keys.

Examples:

| Code | Function      |
| ---- | ------------- |
| 84   | F9            |
| 85   | F21           |
| 86   | Down/Function |
| 87   | Function      |
| 88   | F51           |
| 99   | F62           |

---

# TDV Graphics Commands

Graphics commands are unusual because they do not follow CSI syntax.

---

# Horizontal Line

Original:

```text
ESC10
```

---

# Vertical Line

Original:

```text
ESC1.
```

---

# Upper Border

Original:

```text
ESC12
```

---

# Lower Border

Original:

```text
ESC18
```

---

# Left Border

Original:

```text
ESC14
```

---

# Right Border

Original:

```text
ESC16
```

---

# Corner Elements

| Command | Meaning     |
| ------- | ----------- |
| ESC19   | Upper Left  |
| ESC17   | Upper Right |
| ESC11   | Lower Left  |
| ESC13   | Lower Right |

---

# Internal Graphics Character Set

Most likely architecture:

```text
ASCII
+
Graphics ROM
```

similar to:

* VT100 Special Graphics
* IBM 3270
* Tektronix terminals

---

# Possible Internal Mapping

| TDV Command | Character   |
| ----------- | ----------- |
| GH          | Horizontal  |
| GV          | Vertical    |
| G1          | Upper Left  |
| G2          | Upper Right |
| G3          | Lower Left  |
| G4          | Lower Right |

---

# TDV Mode Commands

The least understood portion.

---

# EN

Original:

```text
ESC[=C
```

---

# Possible Meaning

Candidate functions:

| Possibility   | Confidence |
| ------------- | ---------- |
| Forms Mode    | Medium     |
| Keyboard Mode | Medium     |
| Office Mode   | Medium     |
| Graphics Mode | Low        |
| Printer Mode  | Low        |

---

# Initialization Modes

TDV2200:

```text
ESC[62;36;66l
ESCQ
ESC[36;62;62h
```

---

# Observations

Mode numbers:

```text
36
62
66
```

appear repeatedly.

This suggests firmware configuration bits.

---

# Similarity To DEC Private Modes

DEC terminals use:

```text
ESC[?1h
ESC[?7h
ESC[?25h
```

TDV appears to use:

```text
ESC[36h
ESC[62h
ESC[66h
```

for a similar purpose.

---

# Reverse Engineering Strategy

If a real TDV2200 becomes available:

## Step 1

Connect serial analyzer.

---

## Step 2

Capture power-up traffic.

---

## Step 3

Press every key.

---

## Step 4

Build mapping table:

```text
Physical Key
      →
Escape Sequence
      →
Logical Function
```

---

## Step 5

Capture graphics operations.

---

## Step 6

Compare with terminfo.

---

# Preliminary Keyboard Layout Reconstruction

Based on the key groups.

Likely top row:

```text
F1  F2  F3  F4
F5  F6  F7  F8
```

---

Dedicated office keys:

```text
HELP
PRINT
COPY
MOVE
EXIT
COMMAND
OPTION
CANCEL
```

---

Editing cluster:

```text
INS CHAR
DEL CHAR
INS LINE
DEL LINE
```

---

Navigation cluster:

```text
HOME
TAB
BACKTAB
PAGE UP
PAGE DOWN
```

---

# Most Significant Discovery

The TDV2200 protocol is remarkably regular.

Nearly every application key follows:

```text
ESC[NN_
```

This means an emulator can decode the keyboard using a simple lookup table rather than a complicated state machine.

# END OF PART 17

# TDV2200 REVERSE ENGINEERING REFERENCE

## Part 18 - Reconstructed Keyboard Layout, NOTIS Usage and Emulator Implementation Checklist

---

# Reconstructed TDV2200 Physical Keyboard Layout

The exact TDV2200 keyboard layout is not contained in the termcap/terminfo definitions.

However, the key groupings allow a fairly strong reconstruction of the logical layout.

---

# Likely Upper Function-Key Area

```text
+---------------------------------------------------------+
| F1 | F2 | F3 | F4 | F5 | F6 | F7 | F8 |
+---------------------------------------------------------+
```

Mapped to:

| Physical Key | Sequence |
| ------------ | -------- |
| F1           | ESC[50_  |
| F2           | ESC[52_  |
| F3           | ESC[55_  |
| F4           | ESC[58_  |
| F5           | ESC[60_  |
| F6           | ESC[62_  |
| F7           | ESC[64_  |
| F8           | ESC[66_  |

---

# Likely Office Automation Key Group

The following keys appear consistently in TDV1200, TDV2200 and later ND terminals.

```text
+------------------------------------------------------+
| HELP | PRINT | COPY | MOVE | COMMAND | EXIT |
+------------------------------------------------------+
```

---

## HELP

```text
ESC[47_
```

Used by:

* NOTIS
* Form systems
* Interactive applications

---

## PRINT

```text
ESC[45_
```

Used for:

* Screen print
* Report print
* Form print

---

## COPY

```text
ESC[13_
```

Used for:

* Copy field
* Copy record
* Copy block

---

## MOVE

```text
ESC[15_
```

Used for:

* Move field
* Move block
* Rearrange text

---

## COMMAND

```text
ESC[43_
```

Likely equivalent to:

```text
Command Mode
```

in many office applications.

---

## EXIT

```text
ESC[49_
```

Leaves current form or screen.

---

# Editing Cluster

Likely arranged similarly to:

```text
+--------------------------------+
| INS CHR | DEL CHR |
| INS LIN | DEL LIN |
+--------------------------------+
```

---

## Insert Character

```text
ESC[82_
```

---

## Delete Character

```text
ESC[10_
```

---

## Insert Line

```text
ESC[18_
```

---

## Delete Line

```text
ESC[19_
```

---

# Navigation Cluster

Likely separate from cursor keys.

```text
+--------------------------------+
| HOME |
| TAB  | BACKTAB |
| PGUP | PGDN |
+--------------------------------+
```

---

## HOME

```text
^]
```

---

## SET TAB

```text
ESC[36_
```

---

## BACK TAB

```text
ESC[38_
```

---

## PAGE UP

```text
ESC[S
```

---

## PAGE DOWN

```text
ESC[T
```

---

# Cursor Key Cluster

Unlike most ANSI terminals, TDV2200 uses control characters.

```text
             UP (^\\)

LEFT (^H)         RIGHT (^X)

             DOWN (^K)
```

---

# Why Use Control Characters?

Advantages:

### Smaller

```text
^K
```

1 byte

versus

```text
ESC[B
```

3 bytes

---

### Faster

No escape-sequence parsing.

---

### Reduced Bandwidth

Very important on:

* 1200 bps
* 2400 bps
* X.25 PAD links

---

# NOTIS Usage Model

NOTIS was heavily form-oriented.

A typical screen might look like:

```text
+------------------------------------------------+
| CUSTOMER RECORD                                |
+------------------------------------------------+
| Name:                                          |
| Address:                                       |
| Phone:                                         |
+------------------------------------------------+
| F1 Save  F2 Search  F3 Delete  F4 Print        |
+------------------------------------------------+
```

---

# Likely Key Assignments

| Key  | Action       |
| ---- | ------------ |
| F1   | Save         |
| F2   | Search       |
| F3   | Delete       |
| F4   | Print        |
| HELP | Help         |
| EXIT | Leave Screen |
| COPY | Copy Field   |
| MOVE | Move Field   |

---

# Why Soft Labels Matter

The physical key never changes.

The label changes.

Example:

Screen A:

```text
F1 Save
F2 Search
F3 Print
```

Screen B:

```text
F1 Next
F2 Previous
F3 Exit
```

Same key.

Different meaning.

---

# SINTRAN Forms Processing

The TDV2200 appears optimized for forms processing.

Features supporting this:

| Feature       | Purpose           |
| ------------- | ----------------- |
| Tab Stops     | Field Navigation  |
| Soft Labels   | Dynamic Commands  |
| Reverse Video | Current Field     |
| Underline     | Data Entry Fields |
| Copy/Move     | Record Editing    |
| Page Keys     | Multi-page Forms  |

---

# Complete Emulator Implementation Checklist

---

# Level 1 - Basic Terminal

Supports:

* Shell
* Login
* More
* Less

Required:

```text
clear
cup
ed
el
home
cursor movement
```

---

# Level 2 - Full Screen Applications

Supports:

* Editors
* Forms

Required:

```text
insert character
delete character
insert line
delete line
reverse video
underline
```

---

# Level 3 - TDV Compatible

Supports:

* NOTIS
* SINTRAN forms

Required:

```text
soft labels
help key
command key
copy key
move key
page keys
tab system
```

---

# Level 4 - Complete TDV2200

Required:

```text
graphics subsystem
printer support
private modes
all function keys
```

---

# Graphics Emulation Recommendation

Implement:

| TDV Command | Unicode |
| ----------- | ------- |
| GH          | ─       |
| GV          | │       |
| G1          | ┌       |
| G2          | ┐       |
| G3          | └       |
| G4          | ┘       |
| GU          | ┬       |
| GD          | ┴       |
| GL          | ├       |
| GR          | ┤       |

This provides excellent visual compatibility.

---

# Open Questions

The uploaded material leaves several unanswered questions.

---

## Question 1

What do TDV private modes:

```text
36
62
66
```

actually control?

---

## Question 2

What exactly does:

```text
ESC[=C
```

do?

---

## Question 3

What is the true meaning of:

```text
ESC15
ESC10
ESC14
ESC16
```

graphics commands?

---

## Question 4

How are the 64 logical function keys mapped onto physical hardware?

---

## Question 5

Did firmware revisions change key numbering?

---

# Documentation Needed

To answer these questions we would ideally locate:

* TDV1200 Programmer Reference Manual
* TDV2200 Programmer Reference Manual
* TDV2200 Service Manual
* VTM53 Technical Manual
* VTM93 Technical Manual
* TDV Firmware Listings
* NOTIS Terminal Programming Guide

---

# Final Assessment

Based solely on the termcap and terminfo entries, the TDV2200 emerges as a sophisticated office terminal architecture combining:

```text
ANSI Terminal
+
Forms Processor
+
Office Workstation
+
Soft Label System
+
Printer Controller
+
Graphics Engine
```

This places it much closer to terminals such as the IBM 3270 family or dedicated office workstations than to a conventional VT100.

# END OF PART 18

# TDV TERMINAL REFERENCE

## Part 19 - Complete Glossary of Termcap and Terminfo Syntax

---

# Introduction

To fully understand the TDV1200, TDV2200, ND246 and ND320 definitions, it is necessary to understand:

1. Termcap syntax
2. Terminfo syntax
3. Capability types
4. Formatting operators
5. Inheritance
6. Runtime behavior

This chapter serves as a reference manual for the remainder of the document.

---

# Capability Types

Termcap and terminfo support three capability types.

---

# Boolean Capability

Presence means:

```text
TRUE
```

Absence means:

```text
FALSE
```

Example:

```termcap
am
```

---

Meaning:

```text
Automatic Margins Supported
```

---

Additional Examples

```termcap
bs
mi
```

| Capability | Meaning                          |
| ---------- | -------------------------------- |
| am         | Automatic wrap                   |
| bs         | Backspace supported              |
| mi         | Cursor motion during insert mode |

---

# Numeric Capability

Stores an integer.

Example:

```termcap
co#80
```

---

Meaning:

```text
Columns = 80
```

---

Examples

```termcap
co#80
li#25
```

| Capability | Value      |
| ---------- | ---------- |
| co         | 80 columns |
| li         | 25 lines   |

---

# String Capability

Contains characters sent to the terminal.

Example:

```termcap
ce=\E[K
```

---

Meaning:

```text
Send ESC[K
```

to clear the remainder of the current line.

---

Examples

```termcap
ce=\E[K
cd=\E[J
cl=\E[H\E[2J
```

---

# Escape Character Conventions

---

# \E

Represents:

```ascii
ESC
```

ASCII:

```text
27
```

Hex:

```hex
1B
```

---

Example

```termcap
\E[K
```

becomes:

```hex
1B 5B 4B
```

---

# \r

Carriage Return

ASCII:

```text
13
```

Hex:

```hex
0D
```

---

# \n

Line Feed

ASCII:

```text
10
```

Hex:

```hex
0A
```

---

# \b

Backspace

ASCII:

```text
8
```

Hex:

```hex
08
```

---

# Control Character Notation

Termcap commonly uses:

```text
^X
```

notation.

---

# Example

```text
^G
```

means:

```ascii
BEL
```

ASCII:

```text
7
```

Hex:

```hex
07
```

---

# Common Control Characters

| Notation | Name | ASCII | Hex |
| -------- | ---- | ----- | --- |
| ^A       | SOH  | 1     | 01  |
| ^B       | STX  | 2     | 02  |
| ^C       | ETX  | 3     | 03  |
| ^G       | BEL  | 7     | 07  |
| ^H       | BS   | 8     | 08  |
| ^K       | VT   | 11    | 0B  |
| ^P       | DLE  | 16    | 10  |
| ^X       | CAN  | 24    | 18  |
| ^Y       | EM   | 25    | 19  |
| ^\       | FS   | 28    | 1C  |
| ^]       | GS   | 29    | 1D  |

---

# Termcap Formatting Operators

Used inside parameterized strings.

---

# %d

Convert parameter to decimal.

Example:

```termcap
AL=\E[%dL
```

Parameter:

```text
5
```

Result:

```text
ESC[5L
```

---

# %i

Increment first two parameters.

Example:

```termcap
cm=\E[%i%d;%dH
```

---

Without:

```text
Row=0 Col=0
```

Would generate:

```text
ESC[0;0H
```

---

With `%i`

Generates:

```text
ESC[1;1H
```

because ANSI terminals use 1-based coordinates.

---

# %.

Output parameter as raw character.

Example:

```termcap
cm=^P%.%.
```

---

Input:

```text
10
20
```

Output:

```hex
10 0A 14
```

---

Used by ND246 binary cursor protocol.

---

# Terminfo Formatting Operators

Terminfo replaced many termcap operators with a stack language.

---

# %p1

Push parameter 1 onto stack.

---

Example

```terminfo
%p1
```

means:

```text
Row
```

---

# %p2

Push parameter 2 onto stack.

---

Example

```terminfo
%p2
```

means:

```text
Column
```

---

# %d

Convert top stack value to decimal.

---

Example

```terminfo
%p1%d
```

Row:

```text
10
```

Output:

```text
10
```

---

# %c

Convert top stack value to character.

---

Example

```terminfo
%p1%c
```

Row:

```text
10
```

Output:

```hex
0A
```

---

Used by ND246.

---

# Example Breakdown

TDV2200:

```terminfo
cup=\E[%i%p1%d;%p2%dH
```

---

Input:

```text
Row=9
Column=19
```

---

Output:

```text
ESC[10;20H
```

---

# ND246 Example

```terminfo
cup=^P%p1%c%p2%c
```

Input:

```text
Row=10
Column=20
```

Output:

```hex
10 0A 14
```

---

# Inheritance

One of the most important features.

---

# Termcap Inheritance

Syntax:

```termcap
tc=tdv2200
```

Meaning:

```text
Copy every capability
from tdv2200
```

unless overridden locally.

---

Example

```termcap
nd246:
        cm=^P%.%.
        tc=tdv2200:
```

---

Result:

Everything comes from TDV2200

except:

```text
cm
```

which is replaced.

---

# Terminfo Inheritance

Syntax:

```terminfo
use=tdv2200
```

Same concept.

---

Example

```terminfo
nd246,
        cup=^P%p1%c%p2%c,
        use=tdv2200,
```

---

Result:

All TDV2200 capabilities are inherited.

Only `cup` is overridden.

---

# Runtime Usage

---

# How Applications Use Termcap

Application:

```c
tgetstr("ce", &buffer);
```

Returns:

```text
ESC[K
```

Application sends:

```text
ESC[K
```

to terminal.

---

# How Applications Use Terminfo

Application:

```c
tigetstr("el");
```

Returns:

```text
ESC[K
```

Application sends:

```text
ESC[K
```

to terminal.

---

# Typical NOTIS Flow

Step 1

Move cursor:

```text
cup
```

---

Step 2

Enable reverse video:

```text
rev
```

---

Step 3

Display field:

```text
Customer Number
```

---

Step 4

Restore normal:

```text
sgr0
```

---

Step 5

Wait for:

```text
kcub1
kcuf1
kent
khlp
```

---

# SINTRAN Usage Model

SINTRAN applications generally never know:

```text
ESC[K
```

or

```text
ESC[7m
```

directly.

They request:

```text
Clear Line
Reverse Video
Move Cursor
```

through terminal libraries.

The terminal database supplies the correct sequences.

---

# Why This Matters For Emulation

If you emulate:

```text
Actual Escape Sequences
```

correctly,

then:

```text
NOTIS
SINTRAN
Editors
Forms Systems
```

work automatically.

The application never needs modification.

---

# Capability Lookup Summary

| Request          | Capability |
| ---------------- | ---------- |
| Move Cursor      | cm / cup   |
| Clear Line       | ce / el    |
| Clear Screen     | cl / clear |
| Reverse Video    | so / rev   |
| Underline        | us / smul  |
| Insert Line      | al / il    |
| Delete Line      | dl / dl    |
| Insert Character | ic / ich   |
| Delete Character | dc / dch   |

---

# END OF PART 19

# TDV TERMINAL REFERENCE

## Part 20 - What Can Actually Be Proven From The Definitions

> **Important:** This section intentionally avoids assumptions. Everything below is derived directly from the uploaded termcap/terminfo definitions or from standard termcap/terminfo behavior. Where something is unknown, it is explicitly marked as unknown.

---

# What We Know About NOTIS

From the uploaded terminal definitions alone:

**We cannot prove how NOTIS used the terminal.**

We can only prove that the terminal definitions provide capabilities that NOTIS *could* have used.

Examples:

| Capability              | Exists |
| ----------------------- | ------ |
| Cursor Positioning      | Yes    |
| Reverse Video           | Yes    |
| Underline               | Yes    |
| Insert/Delete Character | Yes    |
| Insert/Delete Line      | Yes    |
| Function Keys           | Yes    |
| Help Key                | Yes    |
| Page Keys               | Yes    |
| Soft Labels             | Yes    |

The actual NOTIS source code would be required to prove exactly which capabilities it used.

---

# What We Can Prove About Screen Updating

The definitions contain:

```termcap
cm=\E[%i%d;%dH
```

or

```terminfo
cup=\E[%i%p1%d;%p2%dH
```

Therefore applications can:

1. Position cursor.
2. Write text.
3. Position cursor elsewhere.
4. Write more text.

This proves random-access screen updates are supported.

---

# Example

The following is provably possible:

```text
Move cursor to row 5 col 10
Write "HELLO"
Move cursor to row 15 col 20
Write "WORLD"
```

because `cup/cm` exists.

---

# What We Can Prove About Highlighting

The definitions contain:

```termcap
so=\E[7m
se=\E[0m
```

or:

```terminfo
rev=\E[7m
sgr0=\E[0m
```

Therefore applications can display:

```text
Normal text

Highlighted text

Normal text
```

using reverse video.

---

# What We Cannot Prove

We cannot prove:

```text
Reverse video was used for fields.
Reverse video was used for menus.
Reverse video was used for errors.
```

Those are application decisions.

The terminal definition only proves the capability exists.

---

# What We Can Prove About Underlining

The definitions contain:

```termcap
us=\E[4m
ue=\E[0m
```

Therefore applications can display underlined text.

---

# What We Cannot Prove

We cannot prove:

```text
Underline was used for input fields.
Underline was used for headings.
Underline was used for hyperlinks.
```

The terminal definition does not contain application behavior.

---

# What We Can Prove About Soft Labels

The definitions contain:

```termcap
l0=
l1=
...
l7=
```

and associated key sequences.

Therefore:

**The terminal supports at least eight logical label keys.**

---

# What We Cannot Prove

We cannot prove:

```text
The labels were visible.
The labels were printed.
The labels were displayed on screen.
```

We only know the terminal definition contains logical soft-label capabilities.

The actual terminal manual would be needed.

---

# What We Can Prove About Function Keys

The definitions expose:

```text
kf0
kf1
...
kf63
```

Therefore software can distinguish at least 64 logical function-key events.

---

# What We Cannot Prove

We cannot prove:

```text
64 physical keys existed.
32 physical keys existed.
8 physical keys existed.
```

Terminfo logical keys are not evidence of physical keyboard layout.

---

# What We Can Prove About Graphics

The definitions contain:

```text
GC
GH
GV
GL
GR
GU
GD
G1
G2
G3
G4
```

Therefore:

The terminal exposes a graphics-related command set.

---

# What We Cannot Prove

We cannot prove:

```text
GH = horizontal line
GV = vertical line
G1 = upper-left corner
```

Those are reasonable hypotheses but are not proven by the definitions.

Without:

* TDV programmer manual
* firmware source
* actual terminal testing

their meaning remains unknown.

---

# What We Can Prove About Printing

The definitions contain:

```termcap
PS=\E[4i
PN=\E[5i
```

or:

```terminfo
mc4=\E[4i
mc5=\E[5i
```

Therefore:

The terminal supports a printer-related mode.

---

# What We Cannot Prove

We cannot prove whether:

```text
A local printer port exists.
Printing is pass-through.
Printing creates a screen dump.
Printing logs characters.
```

The definitions only identify the capability.

---

# What We Can Prove About ND246

ND246 overrides:

```termcap
cm=^P%.%.
```

or:

```terminfo
cup=^P%p1%c%p2%c
```

This proves:

1. Cursor positioning begins with DLE (^P).
2. Two raw character values follow.
3. ANSI cursor positioning is not used.

---

# What We Cannot Prove

We cannot prove:

```text
Byte 1 = row
Byte 2 = column
```

without observing a real ND246.

The naming of `cup` strongly suggests that interpretation, but the terminal definition itself does not document the protocol.

---

# What We Can Prove About Visual Bell

ND246 defines:

```text
^B^A^A^A^A^A^C
```

for `flash`.

Therefore:

The terminal receives that sequence when visual bell is requested.

---

# What We Cannot Prove

We cannot prove:

```text
Screen flashes.
Screen inverts.
LED blinks.
Keyboard light flashes.
```

Only the terminal firmware can tell us.

---

# What We Can Prove About TDV2200 Cursor Keys

Definitions explicitly state:

```text
Left  = ^H
Down  = ^K
Right = ^X
Up    = ^\
Home  = ^]
```

Therefore these exact bytes are transmitted.

This is proven.

---

# What We Can Prove About TDV1200 Cursor Keys

Definitions explicitly state:

```text
ESC[A
ESC[B
ESC[C
ESC[D
```

Therefore TDV1200 uses ANSI-style cursor sequences.

This is proven.

---

# What We Can Prove About Private Modes

The initialization strings contain:

```text
36
62
66
```

inside:

```text
ESC[36h
ESC[62h
ESC[66h
```

or similar sequences.

Therefore:

The firmware contains private operating modes numbered:

```text
36
62
66
```

---

# What We Cannot Prove

We cannot prove what those modes actually do.

There is no description in the uploaded material.

---

# Evidence Levels

A useful classification when continuing reverse engineering.

| Level           | Meaning                             |
| --------------- | ----------------------------------- |
| Proven          | Explicitly present in definition    |
| Strong Evidence | Directly implied by capability type |
| Hypothesis      | Plausible interpretation            |
| Unknown         | No evidence available               |

---

# Examples

## Proven

```text
TDV2200 sends ^H for cursor left.
```

---

## Strong Evidence

```text
ND246 uses a compact binary cursor protocol.
```

because the cursor positioning sequence uses raw bytes.

---

## Hypothesis

```text
GH means horizontal line.
```

Possible, but not proven.

---

## Unknown

```text
Mode 66 enables graphics mode.
```

No evidence currently available.

---

# Recommended Next Steps

To continue without assumptions, additional evidence is needed from:

1. TDV1200 Programmer Reference Manual
2. TDV2200 Programmer Reference Manual
3. TDV2200 Service Manual
4. ND246/VTM53 Technical Manual
5. ND320/VTM93 Technical Manual
6. Real terminal captures
7. NOTIS source code
8. SINTRAN terminal driver source code

Only those sources can move items from:

```text
Hypothesis
```

to:

```text
Proven
```

# END OF PART 20

