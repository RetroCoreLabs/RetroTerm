# Complete NVRAM Analysis - First 13 Bytes (0x6000-0x600C)

## ER3400 NVRAM Architecture

**Type**: ER3400 EAROM (Electrically Alterable ROM)  
**Capacity**: 1024 × 4 bits  
**Access**: Split into low nibble (0x60xx) and high nibble (0x62xx)  
**Write Protocol**: Erase-before-write (erase at addr+0x400)

## NVRAM Memory Layout

### Configuration Data Structure (Bytes 0-10)
Template-driven configuration system using template at 0x0aa0: `E8 E8 E8 FF F8 AF 80 80 80 80 80`

| NVRAM Byte | Physical Addresses | Template | Parameters | Function |
|------------|-------------------|----------|------------|----------|
| 0 | 0x6000/0x6200 | 0xE8 | 0-3 | Serial Channel B + Base Config |
| 1 | 0x6001/0x6201 | 0xE8 | 4-7 | Serial Channel A Configuration |
| 2 | 0x6002/0x6202 | 0xE8 | 8-11 | Baud Rate Configuration |
| 3 | 0x6003/0x6203 | 0xFF | 12-19 | **Core System Control** |
| 4 | 0x6004/0x6204 | 0xF8 | 20-24 | Extended Configuration |
| 5 | 0x6005/0x6205 | 0xAF | 25-30 | Mixed Control Flags |
| 6 | 0x6006/0x6206 | 0x80 | 31 | Single Control Flag |
| 7 | 0x6007/0x6207 | 0x80 | 32 | Single Control Flag |
| 8 | 0x6008/0x6208 | 0x80 | 33 | Single Control Flag |
| 9 | 0x6009/0x6209 | 0x80 | 34 | Single Control Flag |
| 10 | 0x600A/0x620A | 0x80 | 35 | Single Control Flag |

### Signature/Control Bytes (Outside Template System)
| NVRAM Byte | Physical Addresses | Function | Value |
|------------|-------------------|----------|-------|
| 11 | 0x600B/0x620B | Unknown/Reserved | ? |
| 12 | 0x600C/0x620C | Unknown/Reserved | ? |

## Detailed Parameter Mapping

### NVRAM Byte 0 (0x6000/0x6200) - Parameters 0-3
**Template 0xE8**: Extracts 4 bit fields

| Param | RAM Address | Function | Code Usage |
|-------|-------------|----------|------------|
| 0 | 0x5f00 | terminal_config_ram (base) | Referenced throughout system |
| 1 | 0x5f01 | z80sio_chb_wr4_clock_config | Channel B clock configuration |
| 2 | 0x5f02 | z80sio_chb_wr4_char_config | Channel B character format |
| 3 | 0x5f03 | Delay parameter | Used in serial timing (0x0982) |

### NVRAM Byte 1 (0x6001/0x6201) - Parameters 4-7
**Template 0xE8**: Extracts 4 bit fields

| Param | RAM Address | Function | Code Usage |
|-------|-------------|----------|------------|
| 4 | 0x5f04 | z80sio_cha_wr5_config | Channel A WR5 register |
| 5 | 0x5f05 | z80sio_cha_wr4_clock_config | Channel A clock configuration |
| 6 | 0x5f06 | z80sio_cha_wr4_char_config | Channel A character format |
| 7 | 0x5f07 | Display timing parameter | initialize_terminal_display_mode |

### NVRAM Byte 2 (0x6002/0x6202) - Parameters 8-11
**Template 0xE8**: Extracts 4 bit fields

| Param | RAM Address | Function | Code Usage |
|-------|-------------|----------|------------|
| 8 | 0x5f08 | baud_rate_mask_high | Baud rate configuration |
| 9 | 0x5f09 | baud_rate_current | Current baud rate setting |
| 10 | 0x5f0a | baud_rate_mask_low | Baud rate configuration |
| 11 | 0x5f0b | Unknown parameter | General configuration |

### NVRAM Byte 3 (0x6003/0x6203) - Parameters 12-19 ⭐ CRITICAL
**Template 0xFF**: Extracts 8 individual bits

| Bit | Param | RAM Address | Function | Code Usage |
|-----|-------|-------------|----------|------------|
| 7 | 12 | 0x5f0c | Unknown flag | General configuration |
| 6 | 13 | 0x5f0d | keyboard_processing_disabled_flag | vsync_interrupt_handler |
| 5 | 14 | 0x5f0e | display_mode_flag | setup_serial_channel_A_and_B |
| 4 | 15 | 0x5f0f | Serial communication flag | Serial configuration |
| **3** | **16** | **0x5f10** | **keyboard_data_available_flag** | **serial_start_transmission_if_ready** |
| 2 | 17 | 0x5f11 | Unknown flag | General configuration |
| **1** | **18** | **0x5f12** | **display_attribute_mode** | **Communication Mode V24/V11** |
| 0 | 19 | 0x5f13 | keyboard_transmission_enable | Keyboard control |

### NVRAM Byte 4 (0x6004/0x6204) - Parameters 20-24
**Template 0xF8**: Extracts 5 bit fields

| Param | RAM Address | Function | Code Usage |
|-------|-------------|----------|------------|
| 20 | 0x5f14 | Unknown parameter | General configuration |
| 21 | 0x5f15 | Unknown parameter | General configuration |
| 22 | 0x5f16 | Unknown parameter | General configuration |
| 23 | 0x5f17 | baud_rate_backup | Backup baud rate storage |
| 24 | 0x5f18 | Unknown parameter | General configuration |

### NVRAM Bytes 5-10 (0x6005-0x600A) - Parameters 25-35
**Template 0xAF/0x80**: Various bit field extractions

| Param Range | Function | Template |
|-------------|----------|----------|
| 25-30 | Mixed control flags | 0xAF (6 parameters) |
| 31 | Single control flag | 0x80 |
| 32 | Single control flag | 0x80 |
| 33 | Single control flag | 0x80 |
| 34 | Single control flag | 0x80 |
| 35 | Single control flag | 0x80 |

**Note**: Parameter 34 (0x5f22) is referenced in display code.

## NVRAM Address Space (Outside Template System)

### Signature Bytes (NVRAM Test Function)
| Logical Addr | Physical Addr | Function | Expected Value |
|--------------|---------------|----------|----------------|
| 0x50 | 0x6050/0x6250 | Signature byte 1 | 0x80 |
| 0x51 | 0x6051/0x6251 | Signature byte 2 | 0x80 |

### Configuration Data Range (Template System)
| Logical Addr | Physical Addr | Function |
|--------------|---------------|----------|
| 0x52-0x5C | 0x6052-0x605C | Configuration data (11 bytes) |

### Checksum/Control Range
| Logical Addr | Physical Addr | Function | Notes |
|--------------|---------------|----------|-------|
| 0x5D | 0x605D/0x625D | Checksum accumulator | Expected: 0xAA |
| 0x5E | 0x605E/0x625E | Configuration end marker | Loop termination |

### Unknown/Reserved (Bytes 11-12)
| NVRAM Byte | Physical Addr | Function | Status |
|------------|---------------|----------|--------|
| 11 | 0x600B/0x620B | Unknown/Reserved | No code references found |
| 12 | 0x600C/0x620C | Unknown/Reserved | No code references found |

## Critical System Mode Control

### For system_mode_state = 2 (Normal Mode):
**Required**: NVRAM byte 3 (0x6003/0x6203) = 0x08
- **Bit 3 SET**: keyboard_data_available_flag = 1
- **Bit 2 CLEAR**: Unknown flag = 0

### Communication Mode V24/V11:
**Control**: NVRAM byte 3 bit 1 (parameter 18)
- **V24**: Bit 1 SET (0x02)
- **V11**: Bit 1 CLEAR (0x00)

## ER3400 Write Protocol

### Write Sequence for Each Nibble:
1. **READ** current value at 0x6xxx
2. **ERASE** at 0x6xxx + 0x400 (A10=1)
3. **READ** verify erase
4. **WRITE** new value at 0x6xxx (A10=0)
5. **READ** verify write

### Example: Writing to NVRAM byte 3
```
Lower nibble: 0x6003 → Erase: 0x6403 → Write: 0x6003
Upper nibble: 0x6203 → Erase: 0x6603 → Write: 0x6203
```

## Menu System Integration

### Communication Switches Menu Impact:
- **Communication Mode**: Controls parameter 18 (NVRAM byte 3 bit 1)
- **Send Receive Mode**: Controls parameter 15 (NVRAM byte 3 bit 4)
- **Unknown options**: Control parameters 16-17 (NVRAM byte 3 bits 3-2)

### Configuration Flow:
1. **Startup**: NVRAM → RAM extraction via template
2. **Runtime**: System uses RAM parameters (0x5f00-0x5f23)
3. **Menu**: Direct RAM editing
4. **Save**: RAM → NVRAM packing via template

## Factory Reset Function

**Location**: factory_reset_nvram @ 0x3820  
**Process**: Writes default values to all NVRAM locations  
**Range**: Covers addresses 0x50 through approximately 0x6B  
**Trigger**: Configuration menu option 6

This analysis provides complete traceability from NVRAM physical storage through template extraction to runtime usage for the first 13 NVRAM bytes.