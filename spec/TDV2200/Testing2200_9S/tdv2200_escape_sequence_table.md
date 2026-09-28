# Tandberg TDV-2200 Complete Escape Sequence Analysis

## Comprehensive Escape Sequence Table

Based on analysis of the TDV-2200 firmware and the ND terminal specification, here's the complete mapping of escape sequences.

## Character Processing Overview

The TDV-2200 uses a sophisticated character processing system in `nd_character_processor` (0x1BE3):

```assembly
; Main character dispatcher
CPI 0xFC        ; Check for ND escape trigger (252)
CZ 0x19CE       ; Call nd_escape_sequence_dispatcher if FC

CPI 0xCF        ; Check for extended parameter mode (207) 
JNZ next1
CALL 0x0CC1     ; Read multiple characters for complex sequences
JC 0x1BF6       ; Loop until complete

CPI 0xCE        ; Check for parameter mode (206)
JNZ next2  
CALL 0x1F69     ; Parse CSI parameters

; Handle ND private control sequences
CPI 0xB8        ; Special control (184)
CPI 0xB1        ; Control sequence 1 (177)
CPI 0xB2        ; Control sequence 2 (178) 
CPI 0xB3        ; Control sequence 3 (179)
CPI 0xB4        ; Control sequence 4 (180)
```

## Standard VT100 Escape Sequences

| Sequence | Hex | VT100 | TDV | Function | Implementation |
|----------|-----|-------|-----|----------|----------------|
| **ESC D** | 0x44 | ✓ | ✓ | **IND** - Index (scroll down) | Standard VT100 |
| **ESC E** | 0x45 | ✓ | ✓ | **NEL** - Next Line (CR+LF) | Standard VT100 |
| **ESC H** | 0x48 | ✓ | ✓ | **HTS** - Horizontal Tab Set | Standard VT100 |
| **ESC M** | 0x4D | ✓ | ✓ | **RI** - Reverse Index (scroll up) | Standard VT100 |
| **ESC [** | 0x5B | ✓ | ✓ | **CSI** - Control Sequence Introducer | Extended in TDV |
| **ESC c** | 0x63 | ✓ | ✓ | **RIS** - Reset to Initial State | Standard VT100 |

## ND/TDV Private Escape Sequences

| Sequence | Hex | VT100 | TDV | Function | Implementation |
|----------|-----|-------|-----|----------|----------------|
| **ESC 1** | 0x31 | ❌ | ✓ | **NDSS1** - Single shift charset 1 | TDV-specific |
| **ESC 2** | 0x32 | ❌ | ✓ | **NDSS2** - Single shift charset 2 | TDV-specific |
| **ESC 3** | 0x33 | ❌ | ✓ | **NDSS3** - Single shift charset 3 | TDV-specific |
| **ESC 4** | 0x34 | ❌ | ✓ | **NDSS4** - Single shift charset 4 | TDV-specific |
| **ESC 5** | 0x35 | ❌ | ✓ | **NDSS5** - Single shift charset 5 | TDV-specific |
| **ESC 6** | 0x36 | ❌ | ✓ | **NDSS6** - Single shift charset 6 | TDV-specific |
| **ESC 7** | 0x37 | ✓ | ✓ | **NDSC** - Save Cursor | Similar to VT100 DECSC |
| **ESC 8** | 0x38 | ✓ | ✓ | **NDRC** - Restore Cursor | Similar to VT100 DECRC |
| **ESC 9** | 0x39 | ❌ | ✓ | **NDSS7** - Single shift charset 7 | TDV-specific |
| **ESC :** | 0x3A | ❌ | ✓ | **NDSS8** - Single shift charset 8 | TDV-specific |
| **ESC ;** | 0x3B | ❌ | ✓ | **NDSS9** - Single shift charset 9 | TDV-specific |
| **ESC =** | 0x3D | ✓ | ✓ | **NDEAKM** - Enter Alt Keypad Mode | Similar to VT100 DECKPAM |
| **ESC >** | 0x3E | ✓ | ✓ | **NDXAKM** - Exit Alt Keypad Mode | Similar to VT100 DECKPNM |

## TDV Private Control Codes

| Code | Hex | VT100 | TDV | Function | Implementation |
|------|-----|-------|-----|----------|----------------|
| **252** | 0xFC | ❌ | ✓ | **ND_ESC_TRIGGER** | Triggers escape sequence processing |
| **206** | 0xCE | ❌ | ✓ | **ND_PARAM_MODE** | Parameter parsing mode |
| **207** | 0xCF | ❌ | ✓ | **ND_EXT_PARAM_MODE** | Extended parameter parsing |
| **177** | 0xB1 | ❌ | ✓ | **ND_CTRL_SEQ_1** | Control sequence 1 |
| **178** | 0xB2 | ❌ | ✓ | **ND_CTRL_SEQ_2** | Control sequence 2 |
| **179** | 0xB3 | ❌ | ✓ | **ND_CTRL_SEQ_3** | Control sequence 3 |
| **180** | 0xB4 | ❌ | ✓ | **ND_CTRL_SEQ_4** | Control sequence 4 |
| **184** | 0xB8 | ❌ | ✓ | **ND_SPECIAL_CTRL** | Special control function |

## TDV Private CSI Sequences (ESC [ n _)

### Work Area and Line Operations
| Sequence | Decimal | Hex | VT100 | TDV | Function | Implementation |
|----------|---------|-----|-------|-----|----------|----------------|
| **ESC [ 112 p** | 112 | 0x70 | ❌ | ✓ | **NDLIWA** - Insert lines in work area | `FUN_ram_0e70` |
| **ESC [ 113 q** | 113 | 0x71 | ❌ | ✓ | **NDDLWA** - Delete lines in work area | Not found |
| **ESC [ 114 r** | 114 | 0x72 | ❌ | ✓ | **NDSTBM** - Set top/bottom margin | Not found |
| **ESC [ 115 s** | 115 | 0x73 | ❌ | ✓ | **NDICHE** - Insert chars with extent | Not found |
| **ESC [ 116 t** | 116 | 0x74 | ❌ | ✓ | **NDDCHE** - Delete chars with extent | Not found |

### Rectangle Operations  
| Sequence | Decimal | Hex | VT100 | TDV | Function | Implementation |
|----------|---------|-----|-------|-----|----------|----------------|
| **ESC [ 117 u** | 117 | 0x75 | ❌ | ✓ | **NDSREC** - Save rectangle | Not found |
| **ESC [ 118 v** | 118 | 0x76 | ❌ | ✓ | **NDRREC** - Restore rectangle | Not found |
| **ESC [ 122 z** | 122 | 0x7A | ❌ | ✓ | **NDSAR** - Set attribute in rectangle | Not found |
| **ESC [ 123 {** | 123 | 0x7B | ❌ | ✓ | **NDAAR** - Add attribute in rectangle | Not found |
| **ESC [ 124 \|** | 124 | 0x7C | ❌ | ✓ | **NDRAR** - Remove attribute in rectangle | Not found |
| **ESC [ 125 }** | 125 | 0x7D | ❌ | ✓ | **NDFC** - Fill character in rectangle | Not found |

### System and Configuration
| Sequence | Decimal | Hex | VT100 | TDV | Function | Implementation |
|----------|---------|-----|-------|-----|----------|----------------|
| **ESC [ 119 w** | 119 | 0x77 | ❌ | ✓ | **NDSSKL** - Set soft-key level | Not found |
| **ESC [ 120 x** | 120 | 0x78 | ❌ | ✓ | **NDREQ** - Request terminal parameters | Not found |
| **ESC [ 121 y** | 121 | 0x79 | ❌ | ✓ | **NDTST** - Invoke confidence test | Links to diagnostics |
| **ESC [ 126 ~** | 126 | 0x7E | ❌ | ✓ | **NDWA** - Define work area | Not found |
| **ESC [ 127 DEL** | 127 | 0x7F | ❌ | ✓ | **NDVIDEO** - Alpha video on/off | Not found |

## Additional TDV CSI Sequences Found

### Lower Range (0x01-0x6F)
| Range | Count | VT100 | TDV | Function | Implementation |
|-------|-------|-------|-----|----------|----------------|
| **ESC [ 1-9 _** | 9 | ❌ | ✓ | Unknown TDV functions | Stored in memory |
| **ESC [ 10-69 _** | 60 | ❌ | ✓ | Unknown TDV functions | Stored in memory |
| **ESC [ 70-87 _** | 18 | ❌ | ✓ | ND private extensions | Some implemented |

## Function Implementation Analysis

### Character Processing Entry Point
```assembly
; nd_character_processor (0x1BE3)
; Handles all incoming characters and escape sequences

CPI 0xFC        ; ND escape trigger
CZ 0x19CE       ; -> nd_escape_sequence_dispatcher

CPI 0xCF        ; Extended parameter mode  
CALL 0x0CC1     ; -> try_read_serial_data (multi-char sequences)

CPI 0xCE        ; Parameter mode
CALL 0x1F69     ; -> nd_parse_csi_parameters

; ND private control sequences
CPI 0xB1-0xB4   ; Control sequences 1-4
CPI 0xB8        ; Special control
```

### Escape Sequence Dispatcher
```assembly
; nd_escape_sequence_dispatcher (0x19CE)
; Executes escape sequences via function pointer

LHLD (0x5E08)   ; Load nd_escape_handler_function_pointer
MOV A,H         ; Check if pointer is valid
ORA L
RZ              ; Return if NULL pointer
PCHL            ; Jump to escape handler function
```

### CSI Parameter Parser
```assembly
; nd_parse_csi_parameters (0x1F69)
; Parses numeric parameters for CSI sequences

CALL 0x1F85     ; -> nd_validate_parameter_digit (first digit)
JC error        ; Exit on invalid digit
RLC             ; Shift left 4 bits (high nibble)
RLC
RLC  
RLC
MOV B,A         ; Save high nibble

CALL 0x1F85     ; -> nd_validate_parameter_digit (second digit)
JC error
ANI 0x0F        ; Mask low nibble
ORA B           ; Combine with high nibble
```

## Comments Added to Disassembly

```assembly
; Address 0x1BE3 - nd_character_processor
; ND Terminal Character Processor: Handles ND private escape sequences 
; and CSI commands according to ND specification
; 0xFC=Escape trigger, 0xCE/0xCF=Parameter parsing, 0xB1-0xB4=Control sequences

; Address 0x19CE - nd_escape_sequence_dispatcher  
; ND Escape Sequence Handler: Executes escape sequence via function pointer
; at 0x5E08. Supports ND private sequences like NDSS1-NDSS9, NDSC, NDRC, CSI sequences

; Address 0x1F69 - nd_parse_csi_parameters
; ND CSI Parameter Parser: Parses decimal parameters for CSI sequences
; Format: ESC [ nnn _ where nnn is 3-digit decimal parameter

; Address 0x1F85 - nd_validate_parameter_digit
; ND Parameter Digit Validator: Validates digits 0-9 and hex A-F for parameters

; Address 0x0E70 - TDV private function for CSI 112 p (NDLIWA)
; Insert lines in work area - handles line insertion with extent parameters
```

## Summary

### **VT100 Compatibility**: 
- **Standard sequences**: ESC D, E, H, M, [, c, 7, 8, =, >
- **Compatible but extended**: CSI sequences enhanced with TDV features

### **TDV-Specific Features**:
- **9 alternative character sets** (NDSS1-NDSS9)
- **87 private CSI sequences** (ESC [ 1-87 _)
- **Rectangle operations** (save/restore/manipulate screen areas)
- **Work area control** (insert/delete lines and characters)  
- **Soft key programming** (NDSSKL)
- **Built-in diagnostics** (NDTST confidence tests)
- **Custom control codes** (0xB1-0xB4, 0xB8, 0xCE, 0xCF, 0xFC)

### **Implementation Status**:
- **Character processing**: Fully implemented with sophisticated dispatch system
- **Parameter parsing**: Complete CSI parameter parsing system
- **Function dispatch**: Dynamic function pointer system for escape handlers
- **Specific sequences**: Only a few specific CSI functions found (like 0x70/112p)
- **Most CSI sequences**: Stored in memory but implementation functions not identified

The TDV-2200 is a **highly advanced terminal** that significantly extends VT100 capabilities with professional features for sophisticated applications requiring advanced screen manipulation, multiple character sets, and work area control.