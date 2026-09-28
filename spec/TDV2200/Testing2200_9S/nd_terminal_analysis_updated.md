# Tandberg TDV-2200 ND Terminal Analysis - Updated with Specifications

## Updated Analysis Based on ND Terminal Escape Sequence Specification

After analyzing the ND terminal specification document, I can now provide a much more accurate picture of the Tandberg TDV-2200's escape sequence processing capabilities.

## **Updated Function Names and Analysis**

### **Core ND Terminal Processing Functions**

| Address | Original Name | Updated Name | ND Function |
|---------|---------------|--------------|-------------|
| **0x1BE3** | process_incoming_character | **nd_character_processor** | Main ND character processing dispatcher |
| **0x19CE** | call_escape_sequence_handler | **nd_escape_sequence_dispatcher** | ND escape sequence function dispatcher |
| **0x1F69** | parse_hex_parameter_sequence | **nd_parse_csi_parameters** | Parse CSI numeric parameters |
| **0x1F85** | validate_hex_digit_input | **nd_validate_parameter_digit** | Validate parameter digits (0-9, A-F) |
| **0x11CE** | handle_special_control_sequence | **nd_handle_control_sequence** | Handle ND control sequences |

### **Updated Memory Variables**

| Address | Original Name | Updated Name | ND Purpose |
|---------|---------------|--------------|------------|
| **0x5E08** | escape_sequence_handler_pointer | **nd_escape_handler_function_pointer** | Points to active ND escape handler |
| **0x5C2A** | serial_function_pointer | **nd_character_processing_function_pointer** | Main ND character processor |
| **0x5E20** | character_generator_select_flag | **nd_character_set_selection_flag** | ND alternative character set selection |

## **ND Character Code Mappings**

### **ND Private Control Codes Found in Firmware**

| Code | Hex | ND Function | Description |
|------|-----|-------------|-------------|
| **252** | 0xFC | **ND_ESC_TRIGGER** | Triggers ND escape sequence processing |
| **206** | 0xCE | **ND_PARAM_MODE** | Enters parameter parsing mode for CSI sequences |
| **207** | 0xCF | **ND_EXT_PARAM_MODE** | Extended parameter parsing (multi-character) |
| **177** | 0xB1 | **ND_CTRL_SEQ_1** | ND private control sequence 1 |
| **178** | 0xB2 | **ND_CTRL_SEQ_2** | ND private control sequence 2 |
| **179** | 0xB3 | **ND_CTRL_SEQ_3** | ND private control sequence 3 |
| **180** | 0xB4 | **ND_CTRL_SEQ_4** | ND private control sequence 4 |
| **184** | 0xB8 | **ND_SPECIAL_CTRL** | Special ND control function |

## **ND Escape Sequences Supported**

### **ND Private Escape Sequences (ESC n)**
Based on the specification, the terminal supports:

| Sequence | Code | Mnemonic | Function |
|----------|------|----------|----------|
| ESC 1 | 0x31 | **NDSS1** | Single shift to alternative character set 1 |
| ESC 2 | 0x32 | **NDSS2** | Single shift to alternative character set 2 |
| ESC 3 | 0x33 | **NDSS3** | Single shift to alternative character set 3 |
| ESC 4 | 0x34 | **NDSS4** | Single shift to alternative character set 4 |
| ESC 5 | 0x35 | **NDSS5** | Single shift to alternative character set 5 |
| ESC 6 | 0x36 | **NDSS6** | Single shift to alternative character set 6 |
| ESC 7 | 0x37 | **NDSC** | Save cursor position |
| ESC 8 | 0x38 | **NDRC** | Restore cursor position |
| ESC 9 | 0x39 | **NDSS7** | Single shift to alternative character set 7 |
| ESC : | 0x3A | **NDSS8** | Single shift to alternative character set 8 |
| ESC ; | 0x3B | **NDSS9** | Single shift to alternative character set 9 |
| ESC = | 0x3D | **NDEAKM** | Enter alternate keypad mode |
| ESC > | 0x3E | **NDXAKM** | Exit alternate keypad mode |

### **Standard C1 Control Sequences**
| Sequence | Code | Mnemonic | Function |
|----------|------|----------|----------|
| ESC D | 0x44 | **IND** | Index (cursor down with scroll) |
| ESC E | 0x45 | **NEL** | Newline (CR+LF) |
| ESC H | 0x48 | **HTS** | Horizontal tab set |
| ESC M | 0x4D | **RI** | Reverse index (cursor up with scroll) |
| ESC [ | 0x5B | **CSI** | Control sequence introducer |
| ESC c | 0x63 | **RIS** | Reset to initial state |

## **ND Private CSI Sequences Found**

### **CSI Sequences in Firmware Memory**
The firmware contains these ND private CSI sequences:

| CSI Code | Decimal | Mnemonic | Function |
|----------|---------|----------|----------|
| CSI 112 p | 0x70 | **NDLIWA** | Insert lines in work area |
| CSI 113 q | 0x71 | **NDDLWA** | Delete lines in work area |
| CSI 114 r | 0x72 | **NDSTBM** | Set top and bottom margin |
| CSI 115 s | 0x73 | **NDICHE** | Insert characters with extent |
| CSI 116 t | 0x74 | **NDDCHE** | Delete characters with extent |
| CSI 117 u | 0x75 | **NDSREC** | Save rectangle |
| CSI 118 v | 0x76 | **NDRREC** | Restore rectangle |
| CSI 119 w | 0x77 | **NDSSKL** | Set soft-key level |
| CSI 120 x | 0x78 | **NDREQ** | Request terminal parameters |
| CSI 121 y | 0x79 | **NDTST** | Invoke confidence test |
| CSI 122 z | 0x7A | **NDSAR** | Set attribute in rectangle |

### **Additional CSI Sequences Found**
The firmware also contains many other CSI sequences (0-87) suggesting support for:
- Cursor positioning and movement
- Character and line insertion/deletion  
- Screen region manipulation
- Attribute setting and control
- Function key programming

## **ND Terminal Architecture**

### **Character Processing Flow**
```c
nd_character_processor(uint8_t character) {
    switch (character) {
        case 0xFC:  // ND escape trigger
            nd_escape_sequence_dispatcher();
            break;
            
        case 0xCE:  // Parameter mode
            nd_parse_csi_parameters();
            break;
            
        case 0xCF:  // Extended parameter mode
            // Read multiple characters for complex sequences
            do {
                character = try_read_serial_data();
            } while (parsing_needed);
            break;
            
        case 0xB1:  // ND control sequence 1
        case 0xB2:  // ND control sequence 2  
        case 0xB3:  // ND control sequence 3
        case 0xB4:  // ND control sequence 4
            nd_handle_control_sequence(character);
            break;
            
        default:
            if (character >= 0x20 && character <= 0x7F) {
                // Normal printable characters
                output_character_to_screen(character);
            }
            break;
    }
}
```

### **ND Function Pointer Architecture**
```c
// Main character processing dispatcher
(*nd_character_processing_function_pointer)();  // 0x5C2A

// Escape sequence handler dispatcher  
(*nd_escape_handler_function_pointer)();        // 0x5E08
```

## **ND Terminal Capabilities**

### **Supported ND Features**
1. **Alternative Character Sets**: 9 alternative character sets (NDSS1-NDSS9)
2. **Cursor Control**: Save/restore cursor (NDSC/NDRC)
3. **Keypad Modes**: Alternate keypad mode (NDEAKM/NDXAKM)
4. **Rectangle Operations**: Save/restore/manipulate screen rectangles
5. **Work Area Control**: Insert/delete lines and characters in work areas
6. **Margin Control**: Set top and bottom margins (NDSTBM)
7. **Soft Keys**: Programmable function keys (NDSSKL)
8. **Diagnostics**: Built-in confidence tests (NDTST)
9. **Parameter Reporting**: Terminal parameter requests (NDREQ)

### **Character Set Support**
- **G0/G1 Character Sets**: Standard and alternate character sets
- **Single Shift**: Temporary character set switching
- **Locking Shift**: Permanent character set switching  
- **Alternative Character Sets**: 9 different alternative sets

## **Emulator Implementation Guide**

### **Key ND Character Codes to Handle**
```c
#define ND_ESC_TRIGGER      0xFC    // Escape sequence trigger
#define ND_PARAM_MODE       0xCE    // Parameter parsing mode
#define ND_EXT_PARAM_MODE   0xCF    // Extended parameter parsing
#define ND_CTRL_SEQ_1       0xB1    // Control sequence 1
#define ND_CTRL_SEQ_2       0xB2    // Control sequence 2
#define ND_CTRL_SEQ_3       0xB3    // Control sequence 3
#define ND_CTRL_SEQ_4       0xB4    // Control sequence 4
```

### **Function Pointer Monitoring**
```c
// Monitor these function pointers for mode changes
uint16_t* nd_char_processor = (uint16_t*)0x5C2A;
uint16_t* nd_escape_handler = (uint16_t*)0x5E08;
```

### **CSI Sequence Processing**
```c
// CSI sequences use decimal parameters followed by underscore
// Format: ESC [ nnn _
// Where nnn is decimal parameter (000-255)
void handle_csi_sequence(uint8_t param) {
    switch (param) {
        case 112: nd_insert_lines_work_area(); break;    // NDLIWA
        case 113: nd_delete_lines_work_area(); break;    // NDDLWA  
        case 114: nd_set_top_bottom_margin(); break;     // NDSTBM
        case 115: nd_insert_chars_extent(); break;       // NDICHE
        case 116: nd_delete_chars_extent(); break;       // NDDCHE
        case 117: nd_save_rectangle(); break;            // NDSREC
        case 118: nd_restore_rectangle(); break;         // NDRREC
        case 119: nd_set_softkey_level(); break;         // NDSSKL
        case 120: nd_request_parameters(); break;        // NDREQ
        case 121: nd_invoke_confidence_test(); break;    // NDTST
        case 122: nd_set_attribute_rectangle(); break;   // NDSAR
        // ... handle other sequences
    }
}
```

## **Conclusion**

The Tandberg TDV-2200 implements a **comprehensive ND terminal specification** with:

- **Full ND private escape sequence support**
- **Extensive CSI command set** (80+ sequences)
- **Multiple character set support** (9 alternative sets)
- **Advanced terminal features** (rectangles, work areas, soft keys)
- **Professional diagnostic capabilities**

This makes it a **high-end professional terminal** with capabilities far beyond basic VT100 compatibility, designed for sophisticated applications requiring advanced screen manipulation and character set support.