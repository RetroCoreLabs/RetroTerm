# TDV 2215 Terminal - Complete Analysis

## Self-Test Memory Addresses - Renamed & Documented

### Boot Progress & Status Tracking
| Address | New Name | Usage | Description |
|---------|----------|-------|-------------|
| 0x5FFD | `boot_progress_marker` | Boot sequence tracking | Values: 0x234 (ROM check), 0x24A (hardware test), 0x24D (init complete) |
| 0x5FFB | `current_function_marker` | Debug tracking | Tracks current operation for debugging (0x25C, 0x261, 0x26D, etc.) |
| 0x5E0C | `system_status_word` | Cold/warm boot detection | 0x8080 = warm boot (skip tests), other = cold boot (full test) |

### Memory Test Variables
| Address | New Name | Usage | Description |
|---------|----------|-------|-------------|
| 0x6000 | `display_memory_start` | Video RAM base | Start of 8KB display buffer (0x6000-0x7FFF) |
| - | Address-in-address patterns | RAM integrity | Low/high byte address written to each location for verification |

### NVRAM Test Variables  
| Address | New Name | Usage | Description |
|---------|----------|-------|-------------|
| 0x5E03 | `nvram_test_address_pointer` | NVRAM scanning | Current test address (0x50-0x5F config range) |
| 0x5E01 | `nvram_checksum_accumulator` | Data integrity | Accumulates checksum for NVRAM verification |

### Z80SIO Serial Port Test Variables
| Address | New Name | Usage | Description |
|---------|----------|-------|-------------|
| 0x5D52 | `z80sio_channel_a_test_status` | Host port test | Channel A (host communication) test results |
| 0x5D54 | `z80sio_channel_b_test_status` | Keyboard port test | Channel B (keyboard interface) test results |
| 0x5D51 | `serial_transmit_retry_counter` | TX reliability | Retry counter for serial transmission failures |
| 0x5D2E | `tx_buffer_counter` | Buffer management | Transmit buffer byte counter |

### Display & Timing Control
| Address | New Name | Usage | Description |
|---------|----------|-------|-------------|
| 0x5DF9 | `display_refresh_control` | Video timing | Controls display memory updates and VSYNC |
| 0x5E21 | `interrupt_activity_flag` | IRQ coordination | Set by interrupt handlers, cleared by main loop |
| 0x5E25 | `timer_channel_flags` | Multi-timer control | Manages multiple timer channels (bits 0-7) |
| 0x5E22 | `display_timeout_counter` | Screen timeout | 30000-count display timeout timer |

---

## String Data - Renamed & Documented

### System Identification Strings
| Address | New Name | Content | Usage |
|---------|----------|---------|-------|
| 0x221E | `terminal_model_string` | "TDV 2215" | System identification display |
| 0x2229 | `firmware_revision_string` | "Rev. Lev. 11 /" | Firmware version information |

### Error & Status Messages  
| Address | New Name | Content | Usage |
|---------|----------|---------|-------|
| 0x0286 | `error_unknown_string` | "???" | Unknown error placeholder |
| 0x028A | `error_number_prefix_string` | "ERROR NO.:" | Error message prefix |
| 0x294D | `nvram_error_message` | "Error in EAROM, call System Operator." | NVRAM failure message |

### User Interface Strings
| Address | New Name | Content | Usage |
|---------|----------|---------|-------|
| 0x1E0E | `push_key_menu_title` | "PUSH-KEY MENU" | Configuration menu header |
| 0x1E1C | `hex_code_prompt_string` | "HEX CODE : " | Hex input prompt |
| 0x1C50 | `cursor_position_debug_string` | "Cursor position :" | Debug/status display |
| 0x39BC | `configuration_menu_title` | "CONFIGURATION" | Main config menu title |

### Control Characters & ESC Sequences
| Address | New Name | Content | Usage |
|---------|----------|---------|-------|
| 0x35F0 | `ESC_character` | 0x1B | ESC character for escape sequences |
| 0x35F1 | `CSI_bracket_character` | 0x5B ('[') | CSI sequence bracket character |

---

## Major Functions Analysis - Renamed & Documented

### Character Processing & Terminal Emulation
| Original Name | New Name | Functionality |
|---------------|----------|---------------|
| `FUN_ram_2e8c` | `convert_ascii_hex_to_binary` | Converts ASCII hex chars ('0'-'9','A'-'F','a'-'f') to binary values |
| `FUN_ram_22fb` | `escape_sequence_parser` | Main ESC sequence parser entry point |
| `FUN_ram_2316` | `control_character_handler` | Handles control chars (0x11=XON, 0x13=XOFF) |
| `FUN_ram_25a6` | `control_code_dispatch_table` | Jump table dispatcher for control codes (0x00-0x1F) |
| `FUN_ram_241b` | `execute_control_character` | Executes control character functions |
| `FUN_ram_2557` | `terminal_character_output` | Main character output handler with flow control |

### Cursor & Display Management
| Original Name | New Name | Functionality |
|---------------|----------|---------------|
| `FUN_ram_31e9` | `cursor_movement_handler` | Handles cursor positioning and screen navigation |
| `FUN_ram_3675` | `write_character_with_cursor_wrap` | Character output with automatic line wrapping |
| `FUN_ram_368c` | `check_auto_wrap_condition` | Determines when to wrap cursor to next line |
| `FUN_ram_33db` | `get_default_tab_stop_count` | Returns tab stop spacing (default 1 or custom) |

### Terminal Mode & Configuration
| Original Name | New Name | Functionality |
|---------------|----------|---------------|
| `FUN_ram_3fc6` | `configure_terminal_mode_settings` | Complex mode configuration using lookup tables |
| `FUN_ram_3755` | `initialize_terminal_display_mode` | Initialize display parameters and attributes |
| `FUN_ram_37a4` | `set_display_attribute_bit` | Set individual display attribute bits |
| `FUN_ram_2b35` | `calculate_character_attribute_code` | Calculate character rendering attributes |

### Configuration Data Structures
| Address | New Name | Purpose |
|---------|----------|---------|
| 0x0AA0 | `terminal_config_data_table` | Main configuration parameter table |
| 0x387E | Configuration lookup table | Mode setting validation table |

---

## Terminal Emulation Capabilities Detected

### Control Character Support (0x00-0x1F)
- **XON/XOFF Flow Control**: 0x11 (XON), 0x13 (XOFF) detected in control handler
- **Jump Table Dispatch**: 32-entry table for all control characters
- **Bell Function**: String "Bell" found, likely 0x07 (BEL) support

### ESC Sequence Processing
- **ESC Character**: 0x1B stored at 0x35F0
- **CSI Support**: '[' character (0x5B) stored at 0x35F1 for ESC[ sequences
- **Parser Architecture**: Dedicated escape sequence parser function
- **State Machine**: Multi-stage parsing with character classification

### Display Features
- **Cursor Control**: Advanced cursor positioning with wrap detection
- **Character Attributes**: Attribute calculation and bit manipulation
- **Auto-wrap**: Configurable line wrapping behavior
- **Tab Stops**: Configurable tab stop handling

### Terminal Modes & Configuration
- **Mode Tables**: Complex lookup table system for terminal modes
- **NVRAM Storage**: Persistent configuration in EAROM
- **Flow Control**: Hardware and software flow control support
- **Multi-mode Support**: Different terminal emulation modes

### Character Set Support
- **ASCII Processing**: Full ASCII character processing
- **Hex Input**: ASCII hex to binary conversion for setup modes
- **Graphics Characters**: References to "Graphic" mode in strings

---

## Hardware Interface Architecture

### Serial Communication (Z80SIO)
- **Channel A**: Host computer communication (primary data)
- **Channel B**: Keyboard interface
- **Channel C**: Printer interface
- **Flow Control**: DTR/RTS hardware handshaking
- **Test Patterns**: 0x55, 0x2A loopback verification

### Display System
- **Video Memory**: 8KB buffer at 0x6000-0x7FFF
- **VSYNC Timing**: Interrupt-driven display refresh
- **Character Attributes**: Bit-mapped attribute system
- **Cursor Management**: Hardware cursor with blink control

### Configuration Storage  
- **NVRAM/EAROM**: 16 bytes (0x50-0x5F) for settings
- **Checksum Validation**: Data integrity verification
- **Default Fallback**: Built-in defaults if NVRAM fails

---

## Error Handling & Diagnostics

### Self-Test Error Codes
- **0x12**: DTR/RTS signal failure
- **0x13**: Serial pattern transmission failure  
- **0x14**: Serial loopback failure
- **0x19**: NVRAM checksum failure
- **0x1A**: NVRAM not responding
- **0xB0+addr**: Specific NVRAM location failure

### Debug Features
- **Progress Markers**: Boot sequence tracking
- **Function Markers**: Current operation identification
- **Status Words**: System state validation
- **Error Messages**: User-friendly error reporting

---

## Key Insights

1. **Professional Terminal**: Full VT100-class terminal emulation capabilities
2. **Robust Design**: Comprehensive self-test and error recovery
3. **Configurable**: Extensive NVRAM-based configuration system
4. **Multi-Interface**: Host, keyboard, printer interfaces with flow control
5. **Standards Compliant**: ESC/CSI sequence support for terminal compatibility
6. **Industrial Grade**: Error reporting and diagnostic capabilities

The TDV 2215 represents a sophisticated terminal controller suitable for professional computer systems, with comprehensive terminal emulation, robust hardware management, and extensive configuration capabilities typical of high-end terminals from the 1980s era.
