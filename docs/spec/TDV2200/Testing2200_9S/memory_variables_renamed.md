# Tandberg TDV-2200 Self-Test Memory Variables

## Renamed Memory Variables Used During Diagnostics

### **Memory Checksum Variables**
| Address | Old Name | New Name | Purpose | Usage |
|---------|----------|----------|---------|-------|
| **0x8000** | DAT_ram_8000 | **memory_checksum_byte1** | First checksum byte | Sum of these two bytes determines if diagnostics run |
| **0x8001** | DAT_ram_8001 | **memory_checksum_byte2** | Second checksum byte | If sum=0 with carry=1, run diagnostics |

### **System Control Variables**
| Address | Old Name | New Name | Purpose | Usage |
|---------|----------|----------|---------|-------|
| **0x5E0C** | DAT_ram_5e0c | **system_state_checksum** | System state validation | Determines diagnostic execution, set to 0x8080 after init |
| **0x5E0E** | DAT_ram_5e0e | **display_control_register** | Display control flags | Mirror of port 0x60, controls display modes |
| **0x5E3C** | DAT_ram_5e3c | **display_mode_register** | Display mode settings | Set to 0xBF during initialization |

### **Serial Communication Test Variables**
| Address | Old Name | New Name | Purpose | Usage |
|---------|----------|----------|---------|-------|
| **0x5E05** | DAT_ram_5e05 | **serial_test_saved_function_ptr** | Backup of serial function pointer | Saves 0x5C2A during serial test |
| **0x5E07** | DAT_ram_5e07 | **serial_test_receive_buffer_flag** | Receive buffer status flag | Cleared at test start, checked for errors |
| **0x5E0A** | DAT_ram_5e0a | **serial_test_mode_flag** | Serial test mode selector | Determines test pattern type (0x2D/0x81 vs 0x2E/0x82) |
| **0x5C2A** | DAT_ram_5c2a | **serial_function_pointer** | Active serial function pointer | Modified during serial test to 0x890 |

### **Keyboard Controller Test Variables**
| Address | Old Name | New Name | Purpose | Usage |
|---------|----------|----------|---------|-------|
| **0x5E01** | DAT_ram_5e01 | **keyboard_test_response_accumulator** | Accumulates keyboard responses | Cleared at start, should become 0xAA |
| **0x5E21** | DAT_ram_5e21 | **keyboard_interrupt_flag** | Keyboard interrupt occurred flag | Set by keyboard interrupt handler |

### **Display System Variables**
| Address | Old Name | New Name | Purpose | Usage |
|---------|----------|----------|---------|-------|
| **0x5E0F** | DAT_ram_5e0f | **display_sync_control_flag** | Display synchronization control | Used in VSYNC processing |
| **0x5E16** | DAT_ram_5e16 | **interrupt_counter** | General interrupt counter | Set to 0x101, used for timing |
| **0x5E18** | DAT_ram_5e18 | **display_blink_control_flag** | Display element blink control | Controls blinking elements |
| **0x5E19** | DAT_ram_5e19 | **display_cursor_blink_flag** | Cursor blink control | Controls cursor blinking |
| **0x5E22** | DAT_ram_5e22 | **display_timing_counter** | Display timing counter | Set to 0xA769, used for display timing |

## **Diagnostic Test Flow with Memory Variables**

### **1. System Startup Memory Check**
```assembly
; Check memory integrity
LHLD (memory_checksum_byte1)    ; Load 0x8000-0x8001
ADD H+L                         ; Add bytes together  
JNZ/JNC -> run_diagnostics      ; If sum≠0 or no carry, run diagnostics
```

### **2. System State Validation**
```assembly
; Check system state
LHLD (system_state_checksum)    ; Load 0x5E0C
ADD H+L                         ; Validate system state
CMC                             ; Complement carry
JNZ/JNC -> run_diagnostics      ; Run diagnostics if state invalid
```

### **3. Serial Communication Test**
```assembly
; Initialize serial test
XRA A
STA (serial_test_receive_buffer_flag)  ; Clear 0x5E07

; Save current serial function pointer  
LHLD (serial_function_pointer)         ; Load 0x5C2A
SHLD (serial_test_saved_function_ptr)  ; Save to 0x5E05

; Set test function pointer
LXI HL, 0x890
SHLD (serial_function_pointer)         ; Modify 0x5C2A for test

; Test execution with pattern validation
LDA (serial_test_mode_flag)            ; Check 0x5E0A for test mode
; Send 0xAA pattern, expect 0xAA back

; Restore original function pointer
LHLD (serial_test_saved_function_ptr)  ; Restore from 0x5E05  
SHLD (serial_function_pointer)         ; Back to 0x5C2A
```

### **4. Keyboard Controller Test**
```assembly
; Initialize keyboard test
SUB A
STA (keyboard_test_response_accumulator) ; Clear 0x5E01

; Test loop - send 0x80, accumulate responses
; Response handler adds received data to 0x5E01

; Validate final response
LDA (keyboard_test_response_accumulator) ; Load 0x5E01
CPI 0xAA                                ; Should be 0xAA
JZ success                              ; Pass if equal
```

## **Error State Memory Locations**

### **Error Indicators**
| Condition | Memory State | Description |
|-----------|--------------|-------------|
| **Serial Buffer Error** | serial_test_receive_buffer_flag ≠ 0 | Error code 0x15 |
| **No Keyboard Response** | keyboard_test_response_accumulator ≠ 0xAA | Error code 0x19 |
| **Serial Pattern Mismatch** | Received data ≠ 0xAA | Error codes 0x17, 0x18 |
| **Serial Timeout** | No response within timeout | Error code 0x16 |

## **Memory Monitoring for Emulator Development**

### **Key Variables to Monitor**
```c
// During serial test
watch 0x5E07  // serial_test_receive_buffer_flag (should stay 0)
watch 0x5E05  // serial_test_saved_function_ptr (backup storage)
watch 0x5C2A  // serial_function_pointer (modified during test)

// During keyboard test  
watch 0x5E01  // keyboard_test_response_accumulator (should become 0xAA)
watch 0x5E21  // keyboard_interrupt_flag (set by interrupts)

// System state
watch 0x5E0C  // system_state_checksum (determines diagnostic execution)
watch 0x8000  // memory_checksum_byte1 + memory_checksum_byte2
```

### **Emulator Implementation Notes**

**For Serial Test Success:**
- Keep `serial_test_receive_buffer_flag` (0x5E07) = 0
- Echo 0xAA pattern back when written to UART data port
- Ensure `serial_function_pointer` restoration works correctly

**For Keyboard Test Success:**  
- Accumulate 0xAA into `keyboard_test_response_accumulator` (0x5E01)
- Respond to 0x80 command with 0xAA
- Set `keyboard_interrupt_flag` (0x5E21) appropriately

**For Memory Checksum Success:**
- Set `memory_checksum_byte1` + `memory_checksum_byte2` to non-zero sum with carry
- Initialize `system_state_checksum` (0x5E0C) properly

These renamed variables provide clear insight into the diagnostic test operations and simplify emulator development by showing exactly what memory locations need to be monitored and manipulated.