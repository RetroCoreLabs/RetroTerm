# OPCOM Command Reference - ND-100/ND-110/ND-120

## Overview

**OPCOM** (OPerator's COMmunication) is a microprogram residing inside the ND-100 family CPU (ND-100, ND-110, ND-110/CX, ND-120/CX). It provides low-level access to the CPU for operator functions, debugging, and service purposes.

OPCOM runs **independently** from SINTRAN III. It is part of the CPU microcode and is available even when no software is loaded. It communicates exclusively through the **console terminal** (terminal 1). When OPCOM is active, the console keyboard is connected to the OPCOM microprogram. Program output to the console continues normally.

### Key Characteristics

- **Uppercase only** - OPCOM accepts only uppercase letters
- **Octal numbers only** - All numeric values and addresses are octal unless explicitly stated otherwise
- **Prompt** - The `#` character indicates OPCOM is ready for commands
- **Non-destructive** - Entering OPCOM does not stop or affect a running CPU
- **Console-only** - Cannot be used from any terminal other than terminal 1
- **No spaces** - Spaces are not permitted within commands. The space character itself is interpreted as a command to **cancel** all previously entered characters
- **Immediate interpretation** - Most commands are interpreted immediately and must NOT be ended with CR. When CR is required, it is shown explicitly as `↵`
- **Unrecognized input** - Any unrecognized character produces `?` and cancels all previously entered characters (same as space)

**Sources**: ND-30.003.7 EN SINTRAN III System Supervisor, Section 2.1.3; nd110.hackercorp.no/Terminal

---

## What a real ND-120/CX actually sends — measured, 6 September 2026

Everything below was read off the wire of a real ND-120/CX over COM11 at 9600 7E1, byte by
byte, not taken from a manual. Each of these differed from what the code assumed, and
each difference broke something that all the unit tests said was fine.

**A space is echoed and NOTHING follows it.** The manual's "space cancels input" is true,
but there is no `#` prompt afterwards. The next command may be typed immediately. Waiting
for a prompt here is a deadlock: it never comes.

**`#` is not always a prompt.** It is also the first character OPCOM prints on a new
output line. A memory deposit answers `CR LF # VALUE SPACE`, where the value is the
contents of the NEXT address and the machine is still in examine mode. Treating that `#`
as "ready" leaves OPCOM sitting in examine mode, and the next command gets typed into it.

**A slash means a value is coming back, and a space closes it.** After `ADDR/` or `REG/`
the machine sends six octal digits and then a SPACE. The replacement value may only be
typed after that space. Characters typed earlier are dropped without any error.

**Dump output is `CR LF # CR` first, then the data.** The `#` comes BEFORE the values, not
after. The data is lines of `AAAAAA /VVVVVV VVVVVV ...`, eight words to a line, each word
followed by a space, lines separated by `CR LF`. The six digits at the head of each line
are the ADDRESS of its first word, not a value.

**Nothing marks the end of a dump.** There is no trailing prompt. The only way to know a
dump has finished is to count the words that were asked for.

**A register dump keeps printing after its last value.** `0<1RD` ends with `CR LF 000020 /`
— the start of a line that never receives any values. Anything typed during that tail is
DROPPED BY THE MACHINE, silently. A command issued the instant the last value arrives is
lost, which looks exactly like the machine ignoring you. Wait for the line to go quiet.

**`IRD` prints 15 values**, not the 16 this document promises below and not the 14 the
register table names.

**Master clear is `MACL` and it needs a carriage return.** `MCL`, which every earlier
version of this document gave, is rejected. See the MACL section below.

**Rejection has two shapes, and the order differs.** A single unrecognised character or a
lowercase letter is echoed and answered with a bare `?` — no CR LF, no prompt. A command
that was typed and then ended with CR is answered `CR LF # ?` — the prompt line first and
the question mark after it.

**`*` prints the location followed by a space**, and no prompt.

---

## Entering and Leaving OPCOM

### Entering OPCOM

| Method | Description |
|--------|-------------|
| `@OPCOM` | SINTRAN command (requires user SYSTEM) |
| `@STOP-SYSTEM` | Stops SINTRAN and enters OPCOM (CPU in STOP mode) |
| **OPCOM button** | Press OPCOM on operator panel (key switch must be ON) |
| **STOP button** | Press STOP on operator panel (CPU enters STOP mode + OPCOM) |

The CPU must be in OPCOM or STOP mode. The OPCOM indicator on the display panel will be lit. When OPCOM is active, the prompt `#` appears on the console terminal.

### Leaving OPCOM

| Command | Description |
|---------|-------------|
| **ESC** | Terminates OPCOM mode. **Has no effect if CPU is in STOP mode** - the CPU must be running |
| **LOAD completes** | OPCOM exits automatically when a load completes and CPU starts running |

Before leaving OPCOM, restore the display panel:

```
#ACT/
```

Then press ESC to return to SINTRAN.

---

## Commands Available in Any OPCOM Mode

These commands work whether the CPU is running or stopped:

| Command | Syntax | Description |
|---------|--------|-------------|
| **Examine memory/register** | `/` | Examine memory location or register |
| **Register dump** | `RD` | Dump working registers |
| **Internal register dump** | `IRD` | Dump internal CPU registers |
| **Scratch register dump** | `RDE` | Dump scratch registers (microprogram debugging) |
| **Examine mode** | `E` | Specify physical or virtual address mode |
| **Display format** | `F` | Set display panel format |
| **Print current location** | `*` | Print address of last examined location |
| **STOP** | `STOP` | Put CPU in STOP mode |
| **MACL** | `MACL` + CR | Master Clear (reinitialize, no microcode load). NOT `MCL` - see below |
| **Leave OPCOM** | `ESC` | Exit OPCOM (no effect if CPU is stopped) |

## Commands Available Only in STOP Mode

These commands are **only legal when the CPU is stopped**:

| Command | Syntax | Description |
|---------|--------|-------------|
| **Start program** | `!` | Start execution at specified address |
| **Single step** | `Z` | Execute one (or N) instructions |
| **Bootstrap load** | `$` or `&` | Load program from storage device |
| **Set breakpoint** | `.` | Set breakpoint at specified address |
| **Execute instruction** | `"` | Execute an instruction entered from console |
| **Memory test** | `#` | Start microprogrammed memory test |

---

## System Control Commands

### STOP - Halt CPU

```
#STOP
```

Puts the CPU in STOP mode. Program execution halts but can be resumed. All register contents are preserved. Also available as a button on the operator panel. No CR terminator.

### MACL - Master Clear

```
#MACL↵
```

**The command is `MACL` and it needs a carriage return.** Measured on a real ND-120/CX over
COM11 on 6 September 2026, after `MCL` was tried and rejected. Typing `MACL` alone gets
the four letters echoed and nothing more; the carriage return is what runs it, and the
answer is `CR LF # #`. Typing `MCL` and a carriage return answers `CR LF # ?`.

Everything below this paragraph, and every other mention of `MCL` in this document before
today, was wrong. The code sent `MCL` too, so master clear could never have worked against
real hardware.

Hardware initialization of the ND-100 CPU and interface parts. Clears vital registers. Program execution **cannot** be resumed. Memory contents remain unchanged (warm restart is possible). Note: master clear does **not** reload the microcode.

**NOTE**: On ND-110/CX, use the MCL **button** on the operator panel (to properly renew the microprogram).

When it completes, two number signs appear: `##`

**IMPORTANT**: Wait for `##` before issuing any LOAD command.

---

## Load Commands (STOP Mode Only)

The `$` and `&` commands both cause the CPU to load (and possibly execute) a program from a storage device. They are equivalent. The device address is an octal value entered immediately before the command. If the value is omitted, the load is defined by the ALD switch setting.

### Load from ALD Setting

```
#&
```
or
```
#$
```

Loads from the device configured in the Automatic Load Descriptor (ALD) thumbwheel switch on the CPU card.

### Load from Specific Device

```
#ddddd&
```
or
```
#ddddd$
```

Where `ddddd` is the octal hardware device address. The default load format is **BPUN** (binary). For SMD and Winchester disks, **bootstrap format** is used. To specify bootstrap load explicitly, set **bit 13** of the device address to 1 (e.g., if device address is 1550₈, enter `21550&`).

### All Known Boot Device Addresses

| Command | Device Address | Device Type | Load Format |
|---------|---------------|-------------|-------------|
| `#&` | (from ALD) | Per ALD switch | Per ALD |
| `#400&` | 400₈ | Paper TAPE-READER-1 | BPUN (binary) |
| `#500&` | 500₈ | ST-506 Winchester disk controller 1 | Bootstrap |
| `#1540&` | 1540₈ | SMD/ECC disk controller 1 | Bootstrap |
| `#1560&` | 1560₈ | FLOPPY-DISC-1, unit 0 (left-hand floppy) | BPUN (binary) |
| `#1570&` | 1570₈ | FLOPPY-DISC-2, unit 0 (right-hand, seldom present) | BPUN (binary) |
| `#1600&` | 1600₈ | HDLC Remote load 1 (network boot) | BPUN (binary) |
| `#20500&` | 20500₈ | ST-506 Winchester disk controller 1 | Bootstrap |
| `#20510&` | 20510₈ | ST-506 Winchester disk controller 2 | Bootstrap |
| `#21540&` | 21540₈ | ECC/SMD disk controller 1 | Bootstrap |
| `#21550&` | 21550₈ | ECC/SMD disk controller 2 | Bootstrap |
| `#144300&` | 144300₈ | SCSI disk controller | Bootstrap |

### Load Format Types

- **BPUN (Binary)** - Binary Punched format. Loads a binary image (standalone programs from floppy, tape, or network)
- **Bootstrap** - Mass storage bootstrap format. Loads from a disk with filesystem structure (SINTRAN from disk). Selected by setting bit 13 of device address to 1

### Device Address Notes

- Only **unit 0** can be selected for loading via ALD
- The start address after load is always the power-fail restart address **20₈**
- ALD settings 8-15 specify **load and run** (auto-start at address 20₈)
- ALD settings 2-7 specify **load only** (CPU remains in STOP after load)
- SCSI device address 144300₈ is defined in the SINTRAN boot table (MDISCS/SCDIS in PH-P2-OPPSTART.NPL)

---

## Program Execution Commands (STOP Mode Only)

### Start Program (!)

```
#xxxxxx!
```

Execution starts at the octal address entered immediately before `!`. If no address is given, the current value of the P register (program counter) is used:

```
#!
```

### Important SINTRAN Start Addresses

| Address | Purpose |
|---------|---------|
| `20` | Power-fail restart address |
| `21` | Warm start address |
| `22` | Cold start address |

### Common Start Sequences

**Continue execution (STOP pressed accidentally):**
```
#!
```

**Warm start (power-fail restart):**
```
#STOP
#20!
```

**Cold start (MACM loaded):**
```
#22!
```

**Full cold start from disk:**
```
#STOP
#MCL
(wait for ##)
#&
```

**Start MEMTOF (memory dump utility):**
```
#151!
```

### Single Step Execution (Z)

```
#Z
```

Executes **one** instruction (or one interrupt level change). If a value is entered before Z, that many instructions are executed:

```
#10Z
```

Executes 10₈ (8 decimal) instructions.

Page faults, protect violations, and interrupt level changes are executed correctly but count as extra instructions. An overhead of approximately **3 µs** is introduced between each instruction during single-stepping.

### Set Breakpoint (.)

```
#xxxxxx.
```

Sets a breakpoint at octal address `xxxxxx`. When the program reaches that address, execution stops and the `.` character is echoed to the console.

An overhead of approximately **3 µs** is introduced between each instruction when breakpoint checking is active.

If the specified address is never reached, execution continues until a character other than `0-7` or `A-Y` is typed on the console.

### Execute Entered Instruction (")

```
#oooooo"
```

Executes the instruction with octal opcode `oooooo` continuously. Execution stops when a character other than `0-7` or `A-Y` is typed.

**Example:** The PON (paging on) instruction has octal code 150410:

```
#150410"
```

This turns on the paging system.

---

## Memory Examine and Deposit Commands

### Examine Mode (E)

Controls whether subsequent memory commands use physical or virtual addressing.

**Physical examine (paging off):**
```
#E↵
```

No number before E selects physical memory. Addresses may contain up to **24 bits** (8 octal digits) in physical mode.

**Virtual examine via page table:**
```
#ptE↵
```

Where `pt` is the page table number (0 to 17₈).

**NOTE (ND-110/CX):** The number used is the **actual page table number** directly. This differs from the ND-100 CPU which used an encoded value.

**ND-100 with MMS II** page table encoding:
```
y = x / 4         (integer division, discard remainder)
z = y * 10₈ + (x - y * 4₈)
```

**Example:**
```
#5E↵
```

If paging is on, future memory references will be made via page table 5.

When virtual addressing is used, page faults and protect violations are **ignored**.

### Examine Memory Location (/)

```
#xxxxxx/yyyyyy
```

Enter the memory address followed immediately by `/`. The contents are displayed. You then have the option of:

- **Changing the contents** (entering a new value)
- **Viewing the next location** (pressing CR)

### Deposit in STOP Mode

Enter the new octal value followed by CR. The location is changed and the next location is displayed:

```
#100/137777 0↵
```

This displays contents of address 100₈ (which is 137777₈) and changes it to 0. The next location (101₈) is then displayed.

Just pressing CR without a value displays the next location **without** changing the current one.

### Deposit in RUN Mode (DEP)

Changing memory while SINTRAN is running can produce unpredictable results. To guard against doing this inadvertently, you must follow the value with **DEP** instead of CR:

```
#100/137777 0DEP
```

Changes location 100₈ to 0 while the CPU is running.

### Examine Memory Range / Memory Dump (<)

```
#xxxxxx<yyyyyy↵
```

Dumps contents of memory from address `xxxxxx` to `yyyyyy`. Eight addresses are printed per line. The dump uses the 64KW memory bank last addressed by a memory examine (`/`) command. **A memory examine should always be done before a memory dump.**

The dump may be stopped by pressing any key.

### Print Current Location (*)

```
#*
```

Prints the current physical or virtual address on which the next memory examine or deposit will take place. The current location counter is set by the `/` command and is incremented each time CR is typed afterward.

---

## Register Commands

### Examine Register (R/)

Same syntax as memory examine, but with a register name instead of address. The program level (0-17₈) may be specified before the register name. If omitted, level 0 is assumed.

**Working registers** can be specified by name or number:

| Number | Name | Register |
|--------|------|----------|
| R0 | S | Status register |
| R1 | D | D register |
| R2 | P | Program counter |
| R3 | B | Base register |
| R4 | L | Link register |
| R5 | A | Accumulator |
| R6 | T | T register |
| R7 | X | Index register |

**Internal registers** are addressed as `Iy` where y = 0-15:

| Number | Name | Register |
|--------|------|----------|
| I0 | PANS | Panel Status |
| I1 | STS | Status |
| I2 | OPR | Operator Panel Register (simulated) |
| I3 | PSR | Program Status Register |
| I4 | PVL | Previous Level |
| I5 | IIC | Internal Interrupt Code |
| I6 | PID | Program Identification |
| I7 | PIE | Priority Interrupt Enable |
| I10 | CSR | Cache Status Register |
| I11 | ACTL | Active Level |
| I12 | ALD | Automatic Load Descriptor |
| I13 | PES | Paging Error Status |
| I14 | PCR | Program Control Register |
| I15 | PEA | Paging Error Address |

**NOTE**: OPR (I2) is a simulated panel switch register which can be written to from OPCOM. Programs can read its contents with the `TRA OPR` instruction.

**Deposit in STOP mode:** Enter new value followed by **DEP**:

```
#A/126500 100DEP
```

**Examples:**

```
#A/126500           Examine A register on level 0
#7P/140003          Examine P register on level 7
#7R2/140003         Examine R2 (= P register) on level 7
#I7/030013          Examine PIE register (internal register 7)
#OPR/00100          Examine the OPR pseudo register
#I12/021540         Examine ALD register (shows ALD switch value)
```

### Register Dump (xx<yyRD)

```
#xx<yyRD↵
```

Dumps the working registers on program levels `xx` to `yy`. One register set per line. Registers are printed in order: **STS, D, P, B, L, A, T, X**.

To dump only one level, set `xx` equal to `yy`. If `xx` and `yy` are omitted, level 0 is dumped:

```
#<RD↵              Dump registers on level 0
#0<17RD↵           Dump registers on all 16 levels (0-17₈)
#5<5RD↵            Dump registers on level 5 only
```

### Internal Register Dump (IRD)

```
#IRD↵
```

Displays the internal registers. **Only allowed when CPU is in STOP mode** to avoid unintentional unlocking of PEA, PES, and IIC when the CPU is running.

**Measured on a real ND-120/CX, 6 September 2026: it prints 15 values, not 16.** The count
matters, because nothing marks the end of the dump.

### Scratch Register Dump (xx<yyRDE)

```
#xx<yyRDE↵
```

Dumps the 8 scratch registers (microprogram-accessible only) on program levels `xx` to `yy`. One set per line. Intended for **microprogram debugging only**.

---

## I/O Commands

### Read/Write I/O Device (IO/)

```
#ddddIO/
```

Where `dddd` is the device address. The CPU executes an **IOX instruction** using the given device address.

- **Even address**: Input to CPU (read from device). Input data is displayed on the console but not stored anywhere
- **Odd address**: Output from CPU (write to device). Output data is taken from the **OPR pseudo-register**

None of the working registers is affected.

**To set up OPR before an output operation**, use the register examine command:

```
#OPR/xxxxx yyyyyDEP     Set OPR to yyyyy
#ddddIO/                 Execute IOX with device address dddd (odd = output)
```

---

## Display Panel Commands

The display panel is optional. Several pseudo-registers control what it shows.

### ACT - Normal Display

```
#ACT/
```

Restores normal display: active levels, clock, and activity. Always issue this before leaving OPCOM, as other OPCOM operations may have changed the display mode.

### BUS - Bus Activity Display

```
#zzBUS/
```

Shows bus activity on the display. The two-digit code `zz` specifies what data is displayed:

**First digit (data source):**

| Digit | Source |
|-------|--------|
| 0 | CPU data |
| 1 | DMA data |
| 2 | CPU address |
| 3 | DMA address |

**Second digit (access type):**

| Digit | Access |
|-------|--------|
| 0 | Nothing displayed |
| 1 | Read access only |
| 2 | Write access only |
| 3 | Read and write |

**Example:**

```
#23BUS/
```

The display shows all data written from the CPU to memory. The function field shows "ACWR".

### OPR - OPR Register Display

```
#OPR/
```

The display shows the contents of the OPR register. This is a simulated panel switch register writable from OPCOM. Programs can read it with the `TRA OPR` instruction.

### U - User Register Display

```
#U/xxxxxx
```

The display shows the contents of a scratch register writable by the `TRR LMP` instruction. Used by DISC-TEMA to show the cylinder number during disk operations.

### Display Format (F)

```
#uuzzyxF↵
```

Defines the display format for the optional display unit. `F` without arguments (or with argument 0) sets the default octal format.

**x - Number representation:**

| x | Representation |
|---|----------------|
| 0 | Octal (default). `zz` has no effect |
| 1 | Unary - 4 bits decoded to light 1 of 16 indicators. `zz` = which 4 bits |
| 2 | Binary. `zz` has no effect |

**y - Stretch code (afterglow):**

| y | Stretch |
|---|---------|
| 0 | No stretching |
| 1 | Zeros are stretched |
| 2 | Ones are stretched |
| 3 | Zeros and ones are stretched |

**zz - Lower start bit for unary display** (0-24₈): Position of lowest bit for unary representation.

**uu - Display processor maintenance codes** (4 bits):

| uu | Function |
|----|----------|
| 1 | Display year and month |
| 2 | Inhibit message |
| 4 | Initialize panel processor |
| 10 | Abort message |

**Example:**

```
#1421F↵
```

Bits 14-17₈ (bits 12-15 decimal) shown in unary representation with afterglow on ones.

---

## Memory Test (STOP Mode Only)

### Internal Memory Test (#)

```
#bb#
```

Starts a microprogrammed memory test. Memory is tested in banks (segments) of **64 KW** (64K words). The bank number `bb` is entered immediately before the `#` character.

A second `#` is printed on the console if the test is **successful**. If an error is found, the test stops and `?` is sent to the console.

**Registers after a failed test:**

| Register | Contents |
|----------|----------|
| **T** | Failing bits |
| **P** | Failing address |
| **D** | Error pattern |
| **L** | Test pattern |
| **B** | Start address |
| **X** | Stop address |

---

## ALD - Automatic Load Descriptor

The ALD is a **thumbwheel switch** on the CPU card with 16 positions (0-15). It determines boot behavior on power-up, power failure recovery, and operator LOAD commands. The CPU copies the ALD setting into internal register **I12**.

### Reading the ALD Value

```
#I12/xxxxxx
```

or equivalently:

```
#112/xxxxxx
```

Reads internal register I12 (ALD), displaying the current value in octal.

### ALD Switch Position Table

| ALD Pos | I12 Value | Load Action |
|:---:|:---:|---|
| 15 | 000000 | STOP (no load) |
| 14 | 001560 | BPUN load from floppy (1560₈) and run from 20₈ |
| 13 | 020500 | Bootstrap load from Winchester (500₈) and run from 20₈ |
| 12 | 021540 | Bootstrap load from SMD disk (1540₈) and run from 20₈ |
| 11 | 000400 | BPUN load from paper tape (400₈) and run from 20₈ |
| 10 | 001600 | BPUN load from HDLC (1600₈) and run from 20₈ |
| 9 | -- | Run from 20₈ (no load) |
| 8 | -- | Run from 20₈ (no load) |
| 7 | 100000 | STOP (no load) |
| 6 | 101560 | Binary load from floppy (1560₈), load only |
| 5 | 120500 | Mass storage load from Winchester (500₈), load only |
| 4 | 121540 | Mass storage load from SMD disk (1540₈), load only |
| 3 | 100400 | Binary load from paper tape (400₈), load only |
| 2 | 101600 | Binary load from HDLC (1600₈), load only |

The load action is triggered when:
- `$` or `&` (without a preceding device address) is typed
- The LOAD button is pressed on the operator panel
- Power is restored with the key switch in LOCK position after an extended power failure (standby power was lost)

### ALD Behavior Summary

- **Positions 8-15**: Load and **run** from address 20₈ (auto-start)
- **Positions 2-7**: Load **only** (CPU remains stopped after load)
- **Positions 4, 5, 12, 13**: Bootstrap load from disk
- **All other positions**: BPUN format expected
- **Positions 7 and 15**: STOP, no loading done
- **Positions 8 and 9**: Run from address 20₈ without loading

**Typical production setting**: Position 12 (SMD) or 13 (Winchester) - loads and auto-starts SINTRAN.

### Decoding the I12 Register Value

The I12 register is an 18-bit value:

| Bits | Field | Meaning |
|------|-------|---------|
| 17 (MSB) | Load on power-up | 1 = Load even with standby power present (positions 2-7) |
| 16 | Load format | 0 = BPUN (binary), 1 = Mass storage (bootstrap) |
| 15-0 | Device address | Hardware device address (octal) |

**Decoding examples:**

| I12 Value (oct) | Bit 17 | Bit 16 | Device | Interpretation |
|:---:|:---:|:---:|:---:|---|
| 121540 | 1 | 0 | 1540 | Always load, mass storage, SMD disk |
| 101560 | 1 | 0 | 1560 | Always load, binary, floppy |
| 021540 | 0 | 0 | 1540 | Load on power loss only, mass storage, SMD |
| 020500 | 0 | 0 | 500 | Load on power loss only, mass storage, Winchester |
| 100000 | 1 | 0 | 0 | STOP (no device) |
| 000000 | 0 | 0 | 0 | STOP (no device) |

### Operator Panel Key Switch

| Position | Behavior |
|----------|----------|
| **LOCK** | Normal operation. ALD behavior per table above |
| **ON** | Manual operation. STOP after power failure; must start manually |
| **OFF** | Power off |

The key should normally be in **LOCK** position. If in ON position, the computer enters STOP mode after power failure and requires manual restart.

---

## Command Summary Table

### Always Available (OPCOM or STOP mode)

| Command | Syntax | Description |
|---------|--------|-------------|
| **EXAMINE** | `xxxxxx/` | Examine memory location |
| **DEPOSIT (stopped)** | `xxxxxx/old new↵` | Change memory (STOP mode) |
| **DEPOSIT (running)** | `xxxxxx/old newDEP` | Change memory (RUN mode, use with caution) |
| **RANGE DUMP** | `xxxxxx<yyyyyy↵` | Dump memory range |
| **PRINT LOCATION** | `*` | Print current examine address |
| **EXAMINE MODE** | `E↵` or `ptE↵` | Select physical or virtual addressing |
| **REGISTER EXAMINE** | `[lv]reg/` | Examine register (optional level prefix) |
| **REG BLOCK DUMP** | `xx<yyRD↵` | Dump working registers, levels xx-yy |
| **INTERNAL REGS** | `IRD↵` | Dump 16 internal registers (STOP only) |
| **SCRATCH REGS** | `xx<yyRDE↵` | Dump scratch registers (microprogram debug) |
| **I/O ACCESS** | `ddddIO/` | Execute IOX instruction on device |
| **DISPLAY FORMAT** | `uuzzyxF↵` | Set display panel format |
| **ACT** | `ACT/` | Restore normal display |
| **BUS** | `zzBUS/` | Show bus activity on display |
| **OPR** | `OPR/` | Show OPR register on display |
| **U** | `U/` | Show user register on display |
| **STOP** | `STOP` | Halt CPU |
| **MACL** | `MACL` + CR | Master Clear |
| **LEAVE OPCOM** | `ESC` | Exit OPCOM (CPU must be running) |

### STOP Mode Only

| Command | Syntax | Description |
|---------|--------|-------------|
| **START** | `xxxxxx!` or `!` | Start execution at address (or current PC) |
| **SINGLE STEP** | `Z` or `nZ` | Execute 1 or n instructions |
| **BREAKPOINT** | `xxxxxx.` | Set breakpoint at address |
| **LOAD** | `&` or `ddddd&` | Bootstrap/BPUN load from ALD or device |
| **LOAD** | `$` or `ddddd$` | Same as `&` |
| **EXEC INSTRUCTION** | `oooooo"` | Execute instruction with opcode oooooo |
| **MEMORY TEST** | `bb#` | Test memory bank bb (64KW) |

---

## Important Notes

1. **OPCOM is a microprogram**, not software. It exists in CPU microcode and is available even when no software is loaded in memory.

2. **OPCOM does not stop the CPU** when entered. If SINTRAN is running, it continues. Only the STOP command (or STOP button) halts execution.

3. **Memory deposits differ by CPU state**: In STOP mode, use value + CR. In RUN mode, use value + DEP. This prevents accidental memory corruption.

4. **Always restore display** with `ACT/` before leaving OPCOM, as other operations change the display mode.

5. **Wait for `##`** after MCL before issuing LOAD commands. A single `#` means MCL is still in progress.

6. **ALD only loads from unit 0** - It is not possible to select any other disk drive than unit 0 for loading.

7. **ND-110/CX page table numbering** uses actual page table numbers directly. ND-100 with MMS II uses an encoded value (see Examine Mode section).

8. **Breakpoints and single-step** introduce ~3 µs overhead per instruction.

9. **IRD is STOP-only** to prevent unintentional unlocking of PEA, PES, and IIC registers.

10. **Space cancels input** - Pressing space discards all characters typed so far. Any unrecognized character also cancels and produces `?`.

---

## Sources

- ND-30.003.7 EN SINTRAN III System Supervisor, Section 2.1.3 (pages 33-39)
- ND-30.003.7 EN SINTRAN III System Supervisor, Section 11.4 (crash dump procedure)
- ND-30.003.7 EN SINTRAN III System Supervisor, Section 11.5 (RT-program hang procedure)
- ND-30.003.7 EN SINTRAN III System Supervisor, Appendix K (ALD switch settings, Table 29)
- nd110.hackercorp.no/Terminal (ND-110 microcode emulator OPCOM documentation)
- SINTRAN/OS/01-BOOT-SEQUENCE.md (MDISCS table, SCSI device address 144300₈)
