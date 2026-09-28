# Control Character Handlers Complete Analysis

## Control Character Dispatch Table Handlers (0x2421)

### **NULL Character Handler (0x29b3)**
```assembly
; NULL (0x00) CHARACTER HANDLER: Processes null character - no operation
ram:29b3: CALL 0x0043       ; Call cursor utility function
ram:29b6: JMP 0x20c9        ; Jump to main character processing loop exit
```
**Function**: Minimal processing for null character, likely cursor maintenance.

### **SOH Character Handler (0x29b9)**
```assembly
; SOH (0x01) CHARACTER HANDLER: Start of heading control character  
ram:29b9: CALL 0x0040       ; Call cursor utility function
ram:29bc: JMP 0x20c9        ; Jump to main character processing loop exit
```
**Function**: Start of heading control character processing.

### **Carriage Return Handler (0x29bf)**
```assembly
; CARRIAGE RETURN (0x0D) HANDLER: Move cursor to beginning of current line
ram:29bf: CALL 0x006a       ; Call reset_cursor_to_line_start function
ram:29c2: CALL 0x0097       ; Call cursor position update function  
ram:29c5: LDA (0x5f24)      ; Load display mode flag from memory
ram:29c8: CPI 0x2           ; Compare display mode with mode 2
ram:29ca: JZ 0x29d3         ; Jump if display mode equals 2 (special CR handling)
ram:29cd: CALL 0x0106       ; Call standard cursor line advance function
ram:29d0: JMP 0x20c9        ; Jump to main character processing loop exit

; DISPLAY MODE 2 CR HANDLING: Special carriage return processing
ram:29d3: LDA (0x5818)      ; Load current cursor column position
ram:29d6: CPI 0x48          ; Compare cursor position with column 72 (0x48)
ram:29d8: JZ 0x29cd         ; Jump if at column 72 (special line wrap handling)
```

**Function**: 
- Moves cursor to beginning of current line
- Has special handling for display mode 2
- Column 72 triggers special processing

**Variables Used**:
- `display_mode_flag` (0x5f24): Controls CR behavior mode
- `current_cursor_column` (0x5818): Current cursor position

### **Line Feed Handler (0x29db)**
```assembly
; LINE FEED (0x0A) HANDLER: Advance cursor to next line
ram:29db: CALL 0x0076       ; Call advance_cursor_position function
ram:29de: MVI L,0x4e        ; Set column counter to 78 (0x4e) for line clearing
ram:29e0: MVI A,0x20        ; Load space character (0x20) for line clearing
ram:29e2: CALL 0x0145       ; Write space character to clear line remainder
ram:29e5: DCR L             ; Decrement column counter (first decrement)
ram:29e6: DCR L             ; Decrement column counter (second decrement)
ram:29e7: JNZ 0x29e2        ; Loop until line clearing complete
ram:29ea: JMP 0x20c9        ; Jump to main character processing loop exit
```

**Function**:
- Advances cursor to next line
- Clears remainder of line with spaces
- Processes 78 characters for line clearing

### **Alternative Line Feed Handler (0x2a66)**
```assembly
; ALTERNATIVE LINE FEED HANDLER: Secondary LF processing
ram:2a66: CALL 0x0076       ; Call advance_cursor_position function
ram:2a69: JMP 0x20c9        ; Jump to main character processing loop exit
```

**Function**: Simplified line feed processing without line clearing.

### **CR/LF Combination Handler (0x2adc)**
```assembly
; CR/LF COMBINATION HANDLER: Combined carriage return and line feed
ram:2adc: CALL 0x006a       ; Call reset_cursor_to_line_start (CR operation)
ram:2adf: LDA (0x5818)      ; Load current cursor column position
ram:2ae2: CPI 0x48          ; Compare cursor position with column 72 (0x48)
ram:2ae4: CNZ 0x0076        ; Call advance_cursor_position if not at column 72 (LF operation)
ram:2ae7: LDA (0x5f21)      ; Load auto_newline_flag
ram:2aea: ANA A             ; Test auto newline flag
ram:2aeb: JZ 0x20c9         ; Exit if auto newline disabled
ram:2aee: JMP 0x2aa1        ; Jump to tab processing if auto newline enabled
```

**Function**: 
- Combines CR and LF operations
- Conditional LF based on cursor position
- Auto newline feature integration

**Variables Used**:
- `current_cursor_column` (0x5818): Cursor position check
- `auto_newline_flag` (0x5f21): Auto newline enable/disable

### **Backspace Handler (0x2a6c)**
```assembly
; POSSIBLE BACKSPACE (0x08) HANDLER: Cursor movement and character deletion
ram:2a6c: LDA (0x5f22)      ; Load backspace_enable_flag
ram:2a6f: ANA A             ; Test backspace enable flag
ram:2a70: JZ 0x20c9         ; Exit if backspace disabled
ram:2a73: LDA (0x5818)      ; Load current cursor column position
ram:2a76: CPI 0x48          ; Compare cursor position with column 72 (0x48)
ram:2a78: JNZ 0x2a81        ; Jump if not at column 72 (normal backspace)
ram:2a7b: CALL 0x4167       ; Call special function for column 72 backspace
ram:2a7e: JMP 0x20c9        ; Jump to main character processing loop exit

; Normal backspace processing
ram:2a81: CALL 0x0097       ; Call cursor position update
ram:2a84: MOV A,L           ; Load cursor column
ram:2a85: ANA A             ; Test if at column 0
ram:2a86: RAR               ; Rotate right (divide by 2)
ram:2a87: MOV L,A           ; Store modified position
ram:2a88: CALL 0x0094       ; Call cursor update function
ram:2a8b: CALL 0x4167       ; Call character deletion function
```

**Function**:
- Moves cursor back one position
- Deletes character at cursor
- Special handling for column 72
- Can be disabled via flag

**Variables Used**:
- `backspace_enable_flag` (0x5f22): Enable/disable backspace
- `current_cursor_column` (0x5818): Current position

### **Horizontal Tab Handler (0x2aa1)**
```assembly
; POSSIBLE HORIZONTAL TAB (0x09) HANDLER: Tab stop processing
ram:2aa1: CALL 0x36d1       ; Call cursor setup function
ram:2aa4: CALL 0x008e       ; Call cursor boundary check function
ram:2aa7: JC 0x2ab0         ; Jump if at line boundary (tab wrap)
ram:2aaa: CALL 0x36e9       ; Call cursor advance function for tab
ram:2aad: JMP 0x20c9        ; Jump to main character processing loop exit

; Tab wrap handling
ram:2ab0: LDA (0x5f29)      ; Load scroll_disable_flag
ram:2ab3: ANA A             ; Test scroll disable flag
ram:2ab4: JNZ 0x20c9        ; Exit if scrolling disabled
ram:2ab7: CALL 0x36a5       ; Call line wrap function
ram:2aba: JMP 0x20c9        ; Jump to main character processing loop exit
```

**Function**:
- Advances cursor to next tab stop
- Handles line wrapping when tab reaches end
- Scroll disable affects behavior

**Variables Used**:
- `scroll_disable_flag` (0x5f29): Controls scroll behavior

### **Control Flag Handlers (0x29ed, 0x2a05)**
```assembly
; CONTROL CHARACTER HANDLER: Unknown control character processing
ram:29ed: LDA (0x584d)      ; Load control_flags_status
ram:29f0: MOV B,A           ; Save flags/status in B register
ram:29f1: ANI 0x1           ; Test bit 0 of status flags
ram:29f3: JNZ 0x20c9        ; Exit if bit 0 is set
ram:29f6: MVI A,0x1         ; Load bit 0 mask
ram:29f8: CALL 0x004c       ; Call flag setting function
ram:29fb: JC 0x29f8         ; Loop until flag set successfully
ram:29fe: ORA B             ; Combine with existing flags
ram:29ff: STA (0x584d)      ; Store updated control flags
ram:2a02: JMP 0x20c9        ; Jump to main character processing loop exit

; ALTERNATE CONTROL HANDLER: Secondary control character path
ram:2a05: LDA (0x584d)      ; Load control_flags_status
ram:2a08: MOV B,A           ; Save status in B register
ram:2a09: ANI 0x2           ; Test bit 1 of status flags
ram:2a0b: JNZ 0x20c9        ; Exit if bit 1 is set
ram:2a0e: MVI A,0x2         ; Load bit 1 mask
ram:2a10: JMP 0x29f8        ; Jump to flag setting routine
```

**Function**: 
- Manages control character state flags
- Bit-based flag system for different control modes
- Atomic flag setting with retry logic

**Variables Used**:
- `control_flags_status` (0x584d): Control character state flags

## Supporting Functions Called by Handlers

### **Core Cursor Functions**
- **0x006a**: `reset_cursor_to_line_start` - CR implementation
- **0x0076**: `advance_cursor_position` - LF implementation  
- **0x0097**: `update_cursor_memory_addresses` - Position updates
- **0x0106**: `advance_cursor_to_next_line` - Line advancement
- **0x0094**: `update_cursor_display_position` - Display position sync

### **Character Display Functions**
- **0x0145**: `write_character_with_attributes` - Character output
- **0x4167**: `delete_character_at_cursor` - Character deletion
- **0x36e9**: `advance_cursor_with_tab_stops` - Tab processing
- **0x36a5**: `handle_line_wrap_scroll` - Line wrapping
- **0x36d1**: `setup_cursor_for_operation` - Cursor preparation

### **Hardware Interface Functions**
- **0x004c**: `set_hardware_flags_atomic` - Atomic flag operations
- **0x008e**: `check_cursor_boundaries` - Boundary validation
- **0x0040**: `cursor_utility_operation` - General cursor utility
- **0x0043**: `cursor_maintenance_operation` - Cursor maintenance

## Variable Definitions

| Address | Name | Purpose |
|---------|------|---------|
| 0x5f24 | `display_mode_flag` | Controls display mode (mode 2 = special) |
| 0x5818 | `current_cursor_column` | Current cursor column (0-79) |
| 0x584d | `control_flags_status` | Control character state flags |
| 0x5f22 | `backspace_enable_flag` | Enable/disable backspace |
| 0x5f1f | `cursor_wrap_enable_flag` | Enable cursor line wrapping |
| 0x5f29 | `scroll_disable_flag` | Disable scrolling operations |
| 0x5f21 | `auto_newline_flag` | Auto newline after certain operations |

## Control Character Mapping Summary

| Char | Hex | Handler Address | Function Name | Description |
|------|-----|----------------|---------------|-------------|
| NUL | 0x00 | 0x29b3 | `handle_null_character` | Null character processing |
| SOH | 0x01 | 0x29b9 | `handle_start_of_heading` | Start of heading |
| STX | 0x02 | ??? | `handle_start_of_text` | Start of text |
| ETX | 0x03 | ??? | `handle_end_of_text` | End of text |
| EOT | 0x04 | ??? | `handle_end_of_transmission` | End of transmission |
| ENQ | 0x05 | ??? | `handle_enquiry` | Enquiry |
| ACK | 0x06 | ??? | `handle_acknowledge` | Acknowledge |
| BEL | 0x07 | ??? | `handle_bell` | Bell/beep |
| BS  | 0x08 | 0x2a6c | `handle_backspace` | Backspace |
| HT  | 0x09 | 0x2aa1 | `handle_horizontal_tab` | Horizontal tab |
| LF  | 0x0A | 0x29db | `handle_line_feed` | Line feed |
| VT  | 0x0B | ??? | `handle_vertical_tab` | Vertical tab |
| FF  | 0x0C | ??? | `handle_form_feed` | Form feed |
| CR  | 0x0D | 0x29bf | `handle_carriage_return` | Carriage return |
| SO  | 0x0E | ??? | `handle_shift_out` | Shift out |
| SI  | 0x0F | ??? | `handle_shift_in` | Shift in |

## Terminal Behavior Analysis

### **CR/LF Processing Modes**

1. **Standard Mode** (display_mode_flag ≠ 2):
   - CR: Move to column 0, advance line
   - LF: Advance line, clear remainder with spaces

2. **Mode 2** (display_mode_flag = 2):
   - CR: Move to column 0, special column 72 handling
   - LF: Same as standard mode

3. **Combined CR/LF**:
   - CR operation first
   - Conditional LF based on cursor position
   - Auto newline integration

### **Special Column Handling**

**Column 72 (0x48)** appears to be a special boundary:
- CR processing changes behavior at column 72
- Backspace has special handling at column 72
- Tab wrapping considers this boundary

### **Line Clearing Behavior**

Line feed handlers clear the remainder of the line with spaces:
- **0x29db**: Clears 78 characters (0x4e)
- Uses space character (0x20) for clearing
- Double decrementation in loop (decrements by 2 each iteration)

### **Flag-Based Control System**

The terminal uses multiple flags for behavior control:
- **Backspace Enable**: Can disable backspace functionality
- **Cursor Wrap Enable**: Controls line wrapping behavior
- **Scroll Disable**: Prevents scrolling operations
- **Auto Newline**: Automatic newline insertion
- **Control Flags Status**: Bit-based state management

## Debug Points for CR/LF Issues

### **Memory Locations to Monitor**
1. **0x5f24** (`display_mode_flag`): Should be 0 or 1 for normal operation
2. **0x5818** (`current_cursor_column`): Should reset to 0 after CR
3. **0x5f21** (`auto_newline_flag`): May affect CR/LF combination
4. **0x5f1f** (`cursor_wrap_enable_flag`): Affects line wrapping

### **Function Breakpoints**
1. **0x29bf**: CR handler entry point
2. **0x29db**: LF handler entry point
3. **0x006a**: `reset_cursor_to_line_start` function
4. **0x0076**: `advance_cursor_position` function

### **Assembly Sequence for CR**
```assembly
; When CR (0x0D) is received:
1. Dispatch table lookup: 0x2421 + (0x0D × 2) = 0x243B
2. Load handler address: Should be 0x29bf
3. Execute handler: CALL 0x006a (reset_cursor_to_line_start)
4. Update position: CALL 0x0097 (update_cursor_memory_addresses)
5. Mode check: LDA (0x5f24), CPI 0x2
6. Conditional processing based on display mode
```

### **Assembly Sequence for LF**
```assembly
; When LF (0x0A) is received:
1. Dispatch table lookup: 0x2421 + (0x0A × 2) = 0x2435  
2. Load handler address: Should be 0x29db
3. Execute handler: CALL 0x0076 (advance_cursor_position)
4. Line clearing: MVI L,0x4e; MVI A,0x20; loop with CALL 0x0145
5. Return to main loop: JMP 0x20c9
```

## Recommendations for C# Hardware Emulation

### **Verification Steps**
1. **Check dispatch table contents** at 0x2421
2. **Verify handler addresses** for CR (0x29bf) and LF (0x29db)
3. **Monitor cursor position variables** during CR/LF processing
4. **Test display mode flag** settings
5. **Verify function calls** to cursor movement functions

### **Common Issues**
1. **Wrong dispatch table entries**: Incorrect handler addresses
2. **Display mode conflicts**: Mode 2 affecting standard behavior
3. **Flag state issues**: Disabled features affecting operation
4. **Column boundary problems**: Column 72 special handling
5. **Memory variable corruption**: Cursor position variables

### **Testing Sequence**
Send test string: `"ABC\r\nDEF\r\n"`

**Expected Memory Changes**:
- After first CR: `current_cursor_column` = 0
- After first LF: Cursor advances to next line
- After second CR: `current_cursor_column` = 0 again
- After second LF: Cursor advances to next line

This analysis provides the complete picture of how the TDV 2215 processes control characters, with specific focus on the CR/LF handling that's causing issues in your C# emulation.