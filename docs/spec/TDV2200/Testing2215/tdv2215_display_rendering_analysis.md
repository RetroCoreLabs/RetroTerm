# TDV 2215 Display Rendering Analysis: Configuration Menu System

## Overview

This document details the exact display rendering process for the TDV 2215 configuration menu system, showing how text appears on screen and what should happen during option editing with arrow keys.

## Configuration Menu Display Architecture

### Screen Layout Structure

```
Line 0:  [                    ]
Line 1:  [     T D V   2 2 1 5                    ]
Line 2:  [                    ]
Line 3:  [     Rev. Lev. 11 / 129903              ]
Line 4:  [                    ]
Line 5:  [                    ]
Line 6:  [ C O N F I G U R A T I O N   M E N U    ]
Line 7:  [                    ]
Line 8:  [ Convenience Switches                   ]
Line 9:  [ Function Switches                      ]
Line 10: [ Communication Switches                 ]
Line 11: [ PUSH-keys                              ]
Line 12: [ Tabulation-Rack                       ]
Line 13: [                    ]
Line 14: [                    ]
Line 15: [                    ]
...
Line 23: [ Selection: Cursor up and down keys     ]
Line 24: [ Enter:  ENTER key   Exit:  ESC key    ]
```

### Convenience Switches Submenu Layout

```
Line 0:  [ C o n v e n i e n c e   S w i t c h e s ]
Line 1:  [                                        ]
Line 2:  [ Cursor Type                    Line    ]
Line 3:  [ Key Click                      On      ]
Line 4:  [ Margin Bell                    On      ]
Line 5:  [ Auto Repeat                    On      ]
Line 6:  [ Key Rollover                   Enabled ]
Line 7:  [ Keyboard CAPS on power up      Off     ]
Line 8:  [                                        ]
Line 9:  [                                        ]
...
Line 23: [                                        ]
Line 24: [                                        ]
```

## Display Memory Organization

### TMS9937/CRT5027 Memory Layout
- **Screen Size**: 80 columns × 25 rows = 2000 characters
- **Character Memory**: 0x0000 - 0x07CF (2000 bytes)
- **Attribute Memory**: Separate plane for character attributes
- **Cursor Position**: Tracked in hardware registers

### Memory Address Calculation
```c
// For position (row, column):
screen_address = row * 80 + column;
```

### Example Addresses for "Cursor Type" line:
```
Line 2: [ Cursor Type                    Line    ]
        ^                               ^
        Column 1                        Column 37
        Address = 2*80 + 1 = 161       Address = 2*80 + 37 = 197
```

## Configuration Option Rendering Process

### 1. Initial Menu Display (`display_complete_configuration_option()`)

**For each configuration line:**

```c
void display_complete_configuration_option() {
    position_cursor_for_menu_option();           // Set cursor to line start
    clear_display_line();                        // Clear the line
    load_and_display_configuration_template();   // Load option template
    position_cursor_for_menu_option_alt();       // Position for value area
    render_configuration_option_text();          // Render current value
    position_cursor_for_menu_option();           // Reset cursor position
}
```

### 2. Template Processing (`process_and_display_template_text()`)

**Template Structure Example:**
```
Template: "Cursor Type\x80\x01\x02Line\x00Block\x00"
                     ^     ^  ^    ^         ^
                     |     |  |    |         |
           Option name      |  |    Option 1  Option 2
                           |  |
                      Control |
                      code    Value count
```

**Processing Steps:**
1. **Display option name**: "Cursor Type" → characters output to screen
2. **Process control code 0x80**: Switch to value display mode
3. **Read value count**: How many alternatives exist
4. **Select current value**: Based on NVRAM setting
5. **Display selected value**: "Line" or "Block"

### 3. Attribute Management

**Display Attributes:**
- **0**: Normal text (white on black)
- **3**: Highlighted text (inverse video - black on white)
- **7**: Other attribute combinations

**Attribute Application:**
```c
set_display_attribute_and_trigger_refresh(3);  // Highlight mode
output_characters_to_screen("Line");            // Characters with highlight
set_display_attribute_and_trigger_refresh(0);  // Normal mode
```

## What Should Happen During Option Editing

### 1. Entering Edit Mode (Press 0xED on "Cursor Type")

**Current Display:**
```
Line 2: [ Cursor Type                    Line    ]
```

**Expected Changes:**
1. **Cursor positioning**: Move to value area (column 37)
2. **Display alternatives**: Show "Line" and "Block" options
3. **Highlight current**: Current selection highlighted
4. **Show at bottom**: "Line Block" appears at bottom left

**Expected Bottom Display:**
```
Line 23: [ Line  Block                           ]
         ^     ^
      Option1  Option2 (current selection highlighted)
```

### 2. Arrow Key Navigation (RIGHT arrow 0xB3)

**Current Selection: "Line"**
**After RIGHT arrow: "Block"**

**Screen Updates Expected:**
1. **Clear current highlight** on "Line"
2. **Set highlight** on "Block"  
3. **Update main value display**:
   ```
   Before: [ Cursor Type                    Line    ]
   After:  [ Cursor Type                    Block   ]
   ```
4. **Update bottom alternatives**:
   ```
   Before: [ Line  Block                           ] (Line highlighted)
   After:  [ Line  Block                           ] (Block highlighted)
   ```

### 3. Complete Edit Sequence

**Step-by-step rendering:**

1. **Initial State:**
   ```
   Line 2:  [ Cursor Type                    Line    ]
   Line 23: [                                        ]
   ```

2. **Press 0xED (Enter edit mode):**
   ```
   Line 2:  [ Cursor Type                    Line    ] (cursor stops blinking)
   Line 23: [ Line  Block                           ] (Line highlighted)
   ```

3. **Press RIGHT arrow (0xB3):**
   ```
   Line 2:  [ Cursor Type                    Block   ] (value changes)
   Line 23: [ Line  Block                           ] (Block highlighted)
   ```

4. **Press LEFT arrow (0xB4):**
   ```
   Line 2:  [ Cursor Type                    Line    ] (value changes back)
   Line 23: [ Line  Block                           ] (Line highlighted)
   ```

5. **Press 0xED (Exit edit mode):**
   ```
   Line 2:  [ Cursor Type                    Line    ] (cursor resumes blinking)
   Line 23: [                                        ] (alternatives disappear)
   ```

## Hardware-Level Display Operations

### 1. Character Output Sequence

```c
// Output "Block" to replace "Line" at position (2, 37)
cursor_address = 2 * 80 + 37;           // Calculate screen address = 197
set_cursor_position(cursor_address);     // Set hardware cursor
set_display_attribute(3);               // Set highlight attribute
output_character_sequence("Block");     // Output 5 characters: "Block"
output_character(' ');                  // Clear remaining space
set_display_attribute(0);               // Return to normal
```

### 2. TMS9937/CRT5027 Register Operations

**Cursor Position Registers:**
- **Register 14**: Cursor address high byte
- **Register 15**: Cursor address low byte

**For address 197 (0x00C5):**
```c
write_crt_register(14, 0x00);  // High byte
write_crt_register(15, 0xC5);  // Low byte
```

**Attribute Control:**
- **Status Port 0x60**: Attribute and control bits
- **Bit 0**: Attribute update flag
- **Bits 1-7**: Various control functions

### 3. VSYNC Timing

**Display updates occur during VSYNC:**
```c
// In vsync_interrupt_handler()
if (display_refresh_control & 1) {
    output_to_port_0x60(value_for_statusport_2);  // Update attributes
    display_refresh_control = display_refresh_control << 1;
}
```

## Expected Function Call Sequence

### Initial Display (Working Case)

```c
edit_configuration_option_value() {
    // For each option (Line, Block):
    calculate_menu_cursor_position();                    // Position cursor
    set_display_attribute_and_trigger_refresh(3);       // Set highlight
    render_configuration_option_text();                 // Output text
}
```

### Arrow Key Navigation (Broken Case)

```c
// Current implementation (BROKEN):
if (right_arrow_pressed) {
    calculate_menu_cursor_position();                    // ✅ Position cursor
    // ❌ MISSING: set_display_attribute_and_trigger_refresh()
    // ❌ MISSING: render_configuration_option_text()
}

// Should be (FIXED):
if (right_arrow_pressed) {
    calculate_menu_cursor_position();                    // Position cursor
    set_display_attribute_and_trigger_refresh(3);       // Set highlight
    render_configuration_option_text();                 // Render new text
    update_main_value_display();                        // Update main line
}
```

## TMS9937/CRT5027 Register Map for Debugging

### Control Registers (Ports 0x30-0x3F)

| Register | Port | Purpose | Expected Values |
|----------|------|---------|-----------------|
| R0 | 0x30 | Horizontal Total | 0x63 (99 chars) |
| R1 | 0x31 | Horizontal Displayed | 0x50 (80 chars) |
| R2 | 0x32 | Horizontal Sync Position | ~0x55 |
| R3 | 0x33 | Sync Width | ~0x07 |
| R4 | 0x34 | Vertical Total | 0x1F (31 rows) |
| R5 | 0x35 | Vertical Adjust | 0x06 |
| R6 | 0x36 | Vertical Displayed | 0x19 (25 rows) |
| R7 | 0x37 | Vertical Sync Position | ~0x1B |
| R8 | 0x38 | Interlace | 0x00 |
| R9 | 0x39 | Scan Lines | 0x0F (16 lines/char) |
| R10 | 0x3A | Cursor Start | 0x0E |
| R11 | 0x3B | Cursor End | 0x0F |
| R12 | 0x3C | Start Address High | 0x00 |
| R13 | 0x3D | Start Address Low | 0x00 |
| R14 | 0x3E | Cursor Address High | Variable |
| R15 | 0x3F | Cursor Address Low | Variable |

### Status/Control Port 0x60

| Bit | Purpose | Configuration Values |
|-----|---------|---------------------|
| 0 | Attribute Update | Set when attributes change |
| 1 | Display Enable | Always 1 during normal operation |
| 2 | Control Port Mode | Hardware-dependent |
| 3 | Keyboard Activity | Set during key presses |
| 4 | Display Mode | Clear for normal, set for special |
| 5-7 | Various Control | Hardware-dependent |

## C# Emulation Debugging Prompts

### 1. Cursor Position Tracking
```csharp
// Check if cursor position updates are working
Console.WriteLine($"Cursor address written: R14={crt_reg[14]:X2}, R15={crt_reg[15]:X2}");
Console.WriteLine($"Calculated position: Row={cursor_address/80}, Col={cursor_address%80}");
```

### 2. Character Output Monitoring
```csharp
// Monitor character writes to video memory
public void WriteVideoMemory(int address, byte character) {
    if (address >= 160 && address <= 200) { // "Cursor Type" line area
        Console.WriteLine($"Video[{address}] = 0x{character:X2} ('{(char)character}') at Row={address/80}, Col={address%80}");
    }
    videoMemory[address] = character;
}
```

### 3. Attribute Changes Detection
```csharp
// Track attribute register changes
public void WriteStatusPort(byte value) {
    if ((value & 0x01) != (lastStatusValue & 0x01)) {
        Console.WriteLine($"Attribute update flag changed: {(value & 0x01) != 0}");
        Console.WriteLine($"Current attribute value: 0x{attributeRegister:X2}");
    }
    lastStatusValue = value;
}
```

### 4. VSYNC Interrupt Verification
```csharp
// Verify VSYNC timing and display updates
public void VSyncInterrupt() {
    Console.WriteLine($"VSYNC: refresh_control={displayRefreshControl:X2}, status_port={statusPortValue:X2}");
    if ((displayRefreshControl & 1) != 0) {
        Console.WriteLine("Display refresh triggered during VSYNC");
        // Apply pending attribute changes
    }
}
```

### 5. Template Processing Debug
```csharp
// Track template data processing
public void ProcessConfigTemplate(byte[] template) {
    Console.WriteLine($"Processing template: {BitConverter.ToString(template)}");
    for (int i = 0; i < template.Length; i++) {
        if (template[i] >= 0x80) {
            Console.WriteLine($"Control code 0x{template[i]:X2} at position {i}");
        } else if (template[i] < 0x20) {
            Console.WriteLine($"End marker 0x{template[i]:X2} at position {i}");
        }
    }
}
```

### 6. Bottom Line Alternative Display
```csharp
// Monitor bottom line updates (line 23)
public void CheckBottomLineUpdates() {
    int bottomLineStart = 23 * 80;  // Start of line 23
    StringBuilder line = new StringBuilder();
    for (int i = 0; i < 80; i++) {
        char c = (char)videoMemory[bottomLineStart + i];
        line.Append(c >= ' ' ? c : '.');
    }
    Console.WriteLine($"Line 23: '{line}'");
}
```

### 7. Arrow Key Response Tracking
```csharp
// Track what happens when arrow keys are pressed
public void OnArrowKeyPress(byte keyCode) {
    Console.WriteLine($"Arrow key pressed: 0x{keyCode:X2}");
    Console.WriteLine($"Before - Cursor: {GetCursorPosition()}, Attribute: 0x{attributeRegister:X2}");
    
    // Process key...
    
    Console.WriteLine($"After - Cursor: {GetCursorPosition()}, Attribute: 0x{attributeRegister:X2}");
    CheckBottomLineUpdates();
}
```

## Key Areas to Focus Debugging

### 1. **Attribute Register Management**
- Verify that `set_display_attribute_and_trigger_refresh()` calls update the attribute register
- Check that attribute changes trigger status port bit 0
- Ensure VSYNC interrupt applies attribute changes

### 2. **Cursor Position Calculation**
- Verify `calculate_menu_cursor_position()` calculates correct screen addresses
- Check that cursor moves to column 37 for value display area
- Ensure bottom line (line 23) positioning works

### 3. **Template Processing**
- Verify configuration templates load correctly from NVRAM
- Check that control codes (0x80+) are processed properly
- Ensure alternative values are extracted and displayed

### 4. **Character Output Timing**
- Verify character writes occur at correct video memory addresses
- Check that multiple character sequences update properly
- Ensure clearing of old text before writing new text

### 5. **VSYNC Synchronization**
- Verify display updates occur during VSYNC interrupts
- Check that refresh control flags are processed correctly
- Ensure timing between character writes and attribute updates

The key issue is likely in the **attribute management** or **template processing** systems, where the arrow key navigation isn't triggering the same display update sequence as the initial menu rendering.