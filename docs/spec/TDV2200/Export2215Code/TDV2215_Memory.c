typedef unsigned char   undefined;

typedef unsigned char    bool;
typedef unsigned char    byte;
typedef unsigned char    undefined1;
typedef unsigned short    undefined2;



void RST0(void)

{
                    // /*
                    //  * TDV 2215 TERMINAL FIRMWARE - Revision Level 11
                    //  * 
                    //  * TANDBERG DATA A/S - Part no. 385604 - February 1983
                    //  * 
                    //  * COMPLETE NVRAM MEMORY MAP (ER3400 Non-Volatile RAM):
                    //  * 
                    //  * 0x50-0x5A: Soft-Switch Configuration Data (11 bytes)
                    //  *   - Packed bit fields for 37 soft-switches (Convenience, Function,
                    // Communication)
                    //  *   - Extracted via template table at 0x0AA0-0x0AAA into RAM at
                    // 0x5F00-0x5F24
                    //  *   - Permanent switch values, copied to temporary switches at power-up
                    //  * 
                    //  * 0x60-0x6B: PUSH-Key 1 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0x6C-0x77: PUSH-Key 1 (Shifted) - 12 bytes maximum per sequence  
                    //  * 0x78-0x83: PUSH-Key 2 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0x84-0x8F: PUSH-Key 2 (Shifted) - 12 bytes maximum per sequence
                    //  * 0x90-0x9B: PUSH-Key 3 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0x9C-0xA7: PUSH-Key 3 (Shifted) - 12 bytes maximum per sequence
                    //  * 0xA8-0xB3: PUSH-Key 4 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0xB4-0xBF: PUSH-Key 4 (Shifted) - 12 bytes maximum per sequence
                    //  * 0xC0-0xCB: PUSH-Key 5 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0xCC-0xD7: PUSH-Key 5 (Shifted) - 12 bytes maximum per sequence
                    //  * 0xD8-0xE3: PUSH-Key 6 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0xE0-0xEB: PUSH-Key 6 (Shifted) - 12 bytes maximum per sequence
                    //  * 0x100-0x10B: PUSH-Key 7 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0x120-0x12B: PUSH-Key 7 (Shifted) - 12 bytes maximum per sequence
                    //  * 0x140-0x14B: PUSH-Key 8 (Non-shifted) - 12 bytes maximum per sequence
                    //  * 0x170-0x17B: PUSH-Key 8 (Shifted) - 12 bytes maximum per sequence
                    //  * 
                    //  * Tabulation Rack Storage: (Address range TBD - loaded/saved via menu
                    // system)
                    //  *   - Horizontal tab stop positions
                    //  *   - Permanent/temporary tab rack with menu control
                    //  * 
                    //  * TOTAL: 16 PUSH-key sequences (8 keys × 2 shifts) + soft-switches + tab
                    // stops
                    //  * 
                    //  * TERMINAL SPECIFICATIONS:
                    //  * - TDV 2115 compatible with extensions (ESC Q enables extended mode)
                    //  * - 15″ screen with bonded anti-reflex faceplate, adjustable stand
                    //  * - Low-profile, detachable, anti-glare keytops
                    //  * - 256 characters: upper/lowercase, semigraphic set (line drawing,
                    // histogram, subscript, superscript, plot)
                    //  * - Character-by-character transmission with simultaneous send/receive mode
                    //  * - Insert, delete, erase functions with numeric parameters
                    //  * - Direct cursor addressing and device status report
                    //  * - Local and remote printer control with optional 2000-character print
                    // buffer
                    //  * - Asynchronous transmission 50–19200 baud, XON/XOFF handshake
                    //  * - V.24 (RS-232-C), V.11 (RS-422), 20mA current loop interfaces
                    //  * - Soft-switches in non-volatile memory (convenience, function,
                    // communication)
                    //  * - 8 PUSH-keys with SHIFT for 16 programmable sequences stored in NVRAM
                    //  * - Transparent mode for debugging
                    //  * - Double-width characters, graphic rendition control, tabulation
                    //  * - Power-up self-test with error codes
                    //  */
  disableMaskableInterrupts();
  terminal_boot_sequence();
  return;
}



void RST7(void)

{
  enableMaskableInterrupts();
  return;
}



void disable_video_off_control(void)

{
                    // CURSOR UTILITY: Function called by SOH character handler
  clear_status_port_bit3();
  return;
}



void enable_video_off_control(void)

{
                    // CURSOR UTILITY: Function called by NULL character handler
  set_status_port_bit3();
  return;
}



undefined1 enable_cursor_enable_signal(void)

{
  undefined2 in_AF;
  
  video_off_control_flag = 0;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 | 0x10;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 set_vsync_flag_and_clear_status_bit4(void)

{
  undefined2 in_AF;
  
  video_off_control_flag = 1;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 & 0xef;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



byte check_display_attribute_port(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  byte bVar1;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (((bVar1 != 0) && (bVar1 < 9)) && (disableMaskableInterrupts(), (in_io_00000010 & 2) == 0)) {
    enableMaskableInterrupts();
    return bVar1;
  }
  enableMaskableInterrupts();
  return bVar1;
}



byte check_hardware_status_port_bit2(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  byte bVar1;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (((bVar1 == 0) || (bVar1 < 9)) && (disableMaskableInterrupts(), (in_io_00000010 & 2) == 0)) {
    enableMaskableInterrupts();
    return bVar1;
  }
  enableMaskableInterrupts();
  return bVar1;
}



void check_interrupt_flag_and_validate_af(void)

{
  check_interrupt_flag_and_validate_af();
  return;
}



void validate_af_with_interrupt_disable(void)

{
  validate_af_with_interrupt_disable();
  return;
}



void send_keyboard_init_command(void)

{
  write_to_keyboard_return_carry_if_busy();
  return;
}



void setup_cursor_position(void)

{
  setup_cursor_position();
  return;
}



void reset_cursor_to_line_start(void)

{
  reset_cursor_to_line_start();
  return;
}



void advance_cursor_one_character(void)

{
  advance_cursor_one_character();
  return;
}



void advance_cursor_position(void)

{
  increase_cursor_character();
  return;
}



void move_cursor_back_one_position(void)

{
  move_cursor_back_one_position();
  return;
}



void move_cursor_back_one_position_safe(void)

{
  move_cursor_back_one_position_safe();
  return;
}



void update_cursor_memory_wrapper(void)

{
  update_cursor_memory_wrapper();
  return;
}



void update_cursor_memory_wrapper_alt(void)

{
                    // BOUNDARY CHECK: Validates cursor position within screen boundaries
  update_cursor_memory_wrapper_alt();
  return;
}



void update_cursor_memory_addresses(void)

{
  update_cursor_memory_addresses();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 update_cursor_addresses(void)

{
  undefined2 in_AF;
  undefined2 in_HL;
  
  _next_cursor_character_address = in_HL;
  return_register_a();
  _current_cursor_character_address = in_HL;
  return (char)((ushort)in_AF >> 8);
}



void no_operation_placeholder(void)

{
                    // CURSOR POSITION UPDATE: Updates cursor memory addresses and position
  no_operation_placeholder();
  return;
}



void write_pattern_to_display_area_and_update_attributes(void)

{
  write_pattern_to_display_area_and_update_attributes();
  return;
}



void write_char_to_display_with_timing(void)

{
  write_char_to_display_with_timing();
  return;
}



void read_status_port_low_nibble_direct(void)

{
  read_input_port_low_nibble();
  return;
}



void thunk_set_display_attribute_and_trigger_refresh(void)

{
  set_display_attribute_and_trigger_refresh();
  return;
}



void disable_attribute_write_enable(void)

{
  clear_status_port_bit0();
  return;
}



// WARNING: Unknown calling convention -- yet parameter storage is locked

void write_char_to_video_memory(void)

{
  write_to_display();
  return;
}



void display_string_until_control_char(void)

{
  write_string();
  return;
}



void store_character_to_buffer_atomic(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  if (BYTE_ram_5de0 == 0) {
    BYTE_ram_5de0 = 1;
  }
  store_to_buffer_or_nvram((char)((ushort)in_AF >> 8));
  enableMaskableInterrupts();
  return;
}



// WARNING: Unknown calling convention -- yet parameter storage is locked

void poll_keyboard_input_buffer(void)

{
  get_keyboard_char_atomic();
  return;
}



// WARNING: Unknown calling convention -- yet parameter storage is locked

void transmit_to_host_serial(void)

{
  keyboard_to_host_transmission();
  return;
}



void serial_start_transmission_if_ready(void)

{
  serial_start_transmission_if_ready();
  return;
}



void serial_tx_complete_interrupt_handler(void)

{
  serial_tx_complete_interrupt_handler();
  return;
}



void serial_state_advance_to_busy(void)

{
  serial_state_advance_to_busy();
  return;
}



void serial_transmission_error_handler(void)

{
  serial_transmission_error_handler();
  return;
}



void serial_error_recovery_and_reset(void)

{
  serial_error_recovery_and_reset();
  return;
}



void set_host_port_status(void)

{
  configure_z80sio_channel_a_status();
  return;
}



void configure_interrupt_handlers(void)

{
  configure_interrupt_handlers();
  return;
}



void wait_refresh_cycles_and_clear(void)

{
  wait_refresh_cycles_and_clear();
  return;
}



void conditional_line_feed_operation(void)

{
  conditional_line_feed_operation();
  return;
}



void clear_line_and_wait_vsync_alt(void)

{
  clear_line_and_wait_vsync_alt();
  return;
}



void clear_current_line_and_move_up(void)

{
  clear_current_line_and_move_up();
  return;
}



void clear_display_line(void)

{
                    // CURSOR LINE ADVANCE: Advances cursor to next line
  clear_display_line();
  return;
}



void clear_display_memory_range(void)

{
  clear_display_memory_range();
  return;
}



void display_fill_pattern_with_interrupt_check(void)

{
  display_fill_pattern_with_interrupt_check();
  return;
}



void scroll_display_region_down(void)

{
  scroll_display_region_down();
  return;
}



void insert_line_at_cursor(void)

{
  insert_line_at_cursor();
  return;
}



void save_config_to_nvram(void)

{
  read_from_nvram();
  return;
}



void write_byte_to_nvram(void)

{
  write_byte_to_nvram();
  return;
}



void disable_z80sio_channel_a_receive(void)

{
  disable_cha_rx();
  return;
}



void reset_keyboard_buffers_and_update_uart(void)

{
  update_chA_reg3_Rx_maybe();
  return;
}



void configure_z80sio_channels(void)

{
  setup_serial_channel_A_and_B();
  return;
}



void di_push_all(void)

{
  di_push_all();
  return;
}



void thunk_configure_display_control_port_and_wait_ready(void)

{
  configure_display_control_port_and_wait_ready();
  return;
}



undefined1 wait_hardware_ready_and_read_memory(void)

{
  byte bVar1;
  undefined1 *in_HL;
  
  return_register_a();
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
  return *in_HL;
}



void write_char_to_video_memory_and_refresh(void)

{
                    // WRITE CHARACTER: Writes character to display with attributes
  write_char_to_video_memory_and_refresh();
  return;
}



byte read_status_port_low_nibble(void)

{
  byte in_io_00000070;
  byte bVar1;
  
  return_register_a();
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
  return in_io_00000070 & 0xf;
}



void write_display_data_wait_ready(void)

{
  write_display_data_wait_ready();
  return;
}



void fill_display_region_with_pattern(void)

{
  byte bVar1;
  undefined2 uVar2;
  byte *in_DE;
  char cVar3;
  
  return_af_register_high_byte();
  return_af_register_high_byte();
  bVar1 = set_display_status_flag_alt();
  uVar2 = 0x8000;
  do {
    do {
      *in_DE = bVar1;
      bVar1 = readInterruptMask();
      bVar1 = bVar1 & (byte)((ushort)uVar2 >> 8);
      cVar3 = bVar1 == 0;
    } while (!(bool)cVar3);
    check_display_memory_boundary_markers();
    bVar1 = compare_de_hl_registers();
  } while (cVar3 == '\0');
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte();
  return;
}



void load_config_from_nvram(void)

{
  load_config_from_nvram();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 save_config_to_nvram(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte bVar2;
  undefined1 uVar3;
  byte bVar4;
  char cVar5;
  char cVar6;
  byte *pbVar7;
  byte *pbVar8;
  byte bVar9;
  
  pbVar7 = &sw_cursor_type;
  bVar4 = 0;
  pbVar8 = _config_template_pointer;
  do {
    bVar2 = 0;
    cVar5 = '\t';
    bVar9 = *pbVar8 >> 7;
    bVar1 = *pbVar8 << 1 | bVar9;
    do {
      do {
        cVar5 = cVar5 + -1;
        if (cVar5 == '\0') goto LAB_ram_132b;
        bVar9 = -((char)bVar1 >> 7);
        bVar1 = bVar1 << 1 | bVar9;
      } while (bVar9 == 0);
      bVar9 = *pbVar7;
      pbVar7 = pbVar7 + 1;
      cVar6 = cVar5;
      while (cVar6 = cVar6 + -1, cVar6 != '\0') {
        bVar9 = bVar9 << 1 | bVar9 >> 7;
      }
      bVar2 = bVar9 | bVar2;
      bVar9 = 0;
    } while (cVar5 != '\0');
LAB_ram_132b:
    write_byte_to_nvram(bVar2);
    if ((bool)bVar9) {
      return (char)((ushort)in_AF >> 8);
    }
    pbVar8 = pbVar8 + 1;
    bVar4 = bVar4 + 1;
    if (10 < bVar4) {
      uVar3 = return_stack_value_high_byte();
      return uVar3;
    }
  } while( true );
}



byte display_configuration_submenu_and_handle_navigation(void)

{
  byte bVar1;
  char cVar2;
  byte bVar3;
  byte bVar4;
  byte *pbVar5;
  byte *in_HL;
  undefined1 uVar6;
  
  clear_display_memory_range();
  pbVar5 = &config_menu_state_buffer;
  cVar2 = '\x05';
  do {
    *pbVar5 = *in_HL;
    in_HL = in_HL + 1;
    pbVar5 = pbVar5 + 1;
    cVar2 = cVar2 + -1;
  } while (cVar2 != '\0');
  load_config_from_nvram_template();
  update_cursor_memory_addresses();
  display_formatted_string_with_templates();
  set_display_attribute_and_trigger_refresh(0xe);
  write_char_and_advance_cursor(0x20);
  write_char_and_advance_cursor();
  if (config_menu_level != 0) {
    write_char_and_advance_cursor(config_menu_level + 0x30);
    write_to_display();
  }
  set_display_attribute_and_trigger_refresh(0);
  bVar3 = config_option_start_index;
  do {
    display_complete_configuration_option();
    bVar3 = bVar3 + 1;
    bVar4 = config_option_start_index;
  } while ((byte)(config_option_end_index + 1) != bVar3);
LAB_ram_1a81:
  bVar3 = bVar4;
  no_operation_placeholder();
  uVar6 = 0;
  write_pattern_to_display_area_and_update_attributes(0);
  do {
    display_complete_configuration_option();
    no_operation_placeholder();
    write_pattern_to_display_area_and_update_attributes(3);
    while( true ) {
      do {
        bVar1 = get_keyboard_char_atomic();
      } while ((bool)uVar6);
      uVar6 = bVar1 < 0xed;
      if (bVar1 == 0xed) break;
      bVar4 = config_option_start_index;
      if (bVar1 == 0xb8) goto LAB_ram_1a81;
      if (bVar1 == 0xb1) {
        uVar6 = config_option_start_index < bVar3;
        if (config_option_start_index != bVar3) {
          bVar4 = bVar3 - 1;
          goto LAB_ram_1a81;
        }
      }
      else {
        if (bVar1 == 0xb2) {
          bVar4 = bVar3;
          if (config_option_end_index != bVar3) {
            bVar4 = bVar3 + 1;
          }
          goto LAB_ram_1a81;
        }
        if ((bVar1 == 0xf8) || (bVar1 == 0xdb)) {
          clear_display_memory_range(bVar1,bVar1);
          set_display_attribute_and_trigger_refresh(0);
          return bVar1;
        }
        uVar6 = bVar1 < 0xfc;
        if (bVar1 == 0xfc) {
          call_display_function_pointer();
        }
        else {
          check_interrupt_flag_and_validate_af();
        }
      }
    }
    edit_configuration_option_value();
  } while( true );
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 push_key_menu_interface(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 uVar2;
  byte in_C;
  byte bVar3;
  undefined1 uVar5;
  short sVar4;
  char cVar6;
  char *pcVar7;
  char cVar8;
  
  clear_display_memory_range();
  update_cursor_addresses();
  pcVar7 = "PUSH-KEY MENU";
  display_formatted_string_with_templates();
  bVar3 = 1;
  do {
    cVar8 = (char)pcVar7;
    clear_status_port_bit0();
    calculate_cursor_position_from_offset();
    pcVar7 = (char *)(ushort)(byte)(cVar8 - 4U);
    update_cursor_addresses(cVar8 - 4U);
    display_decimal_number(bVar3);
    increase_cursor_character();
    write_to_display();
    calculate_cursor_position_from_offset();
    get_push_key_nvram_address_from_table(bVar3 - 1);
    validate_push_key_index_and_get_length();
    do {
      set_display_attribute_and_trigger_refresh(4);
      bVar1 = read_from_nvram();
      if ((bVar1 < 0x20) || (0x7f < bVar1)) {
        set_display_attribute_and_trigger_refresh(7);
      }
      write_char_and_advance_cursor();
      pcVar7 = pcVar7 + 1;
      in_C = in_C - 1;
    } while (in_C != 0);
    bVar3 = bVar3 + 1;
    in_C = 0;
    cVar8 = bVar3 < 0x11;
  } while (bVar3 != 0x11);
  clear_status_port_bit0();
  update_cursor_addresses();
  write_string();
  set_display_attribute_and_trigger_refresh(1);
  write_string();
  set_display_attribute_and_trigger_refresh(4);
LAB_ram_1ce2:
  bVar3 = 1;
LAB_ram_1ce4:
  calculate_cursor_position_from_offset();
  validate_push_key_index_and_get_length(bVar3 - 1);
  get_push_key_nvram_address_from_table();
  sVar4 = 0x100;
LAB_ram_1cf1:
  do {
    uVar5 = (undefined1)((ushort)sVar4 >> 8);
    uVar2 = read_from_nvram();
    sVar4 = CONCAT11(uVar5,uVar2);
    update_cursor_addresses();
    set_display_attribute_and_trigger_refresh(1);
    display_error_code_custom_chars((char)sVar4);
    update_cursor_addresses();
    set_display_attribute_and_trigger_refresh(4);
    do {
      while( true ) {
        while( true ) {
          do {
            bVar1 = get_keyboard_char_atomic();
          } while ((bool)cVar8);
          cVar8 = bVar1 < 0xfc;
          if (bVar1 != 0xfc) break;
          call_display_function_pointer();
        }
        uVar2 = bVar1 < 0xcf;
        if (bVar1 != 0xcf) break;
        do {
          bVar1 = get_keyboard_char_atomic();
        } while ((bool)uVar2);
LAB_ram_1d93:
        cVar8 = '\0';
        if (((bVar1 != 0) && (cVar8 = bVar1 < 0xf8, (bool)cVar8)) &&
           ((cVar8 = bVar1 < 0xb0, !(bool)cVar8 || (cVar8 = bVar1 < 0xa0, (bool)cVar8)))) {
          write_byte_to_nvram();
          if ((bool)cVar8) {
            check_interrupt_flag_and_validate_af();
          }
          bVar1 = read_from_nvram();
          if ((bVar1 < 0x80) && (0x1f < bVar1)) {
            write_to_display();
          }
          else {
            set_display_attribute_and_trigger_refresh(7);
            write_to_display();
            set_display_attribute_and_trigger_refresh(4);
          }
LAB_ram_1d73:
          bVar1 = (byte)((ushort)sVar4 >> 8);
          cVar8 = bVar1 < in_C;
          if (bVar1 != in_C) {
            sVar4 = (ushort)(byte)(bVar1 + 1) << 8;
            increase_cursor_character();
          }
          goto LAB_ram_1cf1;
        }
LAB_ram_1d43:
        check_interrupt_flag_and_validate_af();
      }
      cVar8 = bVar1 < 0xce;
      if (bVar1 == 0xce) {
        bVar1 = parse_hex_byte();
        if (cVar8 == '\0') goto LAB_ram_1d93;
        goto LAB_ram_1d43;
      }
      if (bVar1 == 0xdb) {
        clear_display_memory_range();
        set_display_attribute_and_trigger_refresh(0);
        return (char)((ushort)in_AF >> 8);
      }
      cVar8 = bVar1 < 0xb8;
      if (bVar1 == 0xb8) goto LAB_ram_1ce2;
      cVar8 = bVar1 < 0xb1;
      if (bVar1 == 0xb1) {
LAB_ram_1d58:
        bVar3 = bVar3 - 1;
        if (bVar3 != 0) goto LAB_ram_1ce4;
        goto LAB_ram_1ce2;
      }
      if (bVar1 == 0xb2) {
        bVar3 = bVar3 + 1;
        cVar8 = bVar3 < 0x11;
        if (bVar3 == 0x11) goto LAB_ram_1d58;
        goto LAB_ram_1ce4;
      }
      if (bVar1 == 0xb3) goto LAB_ram_1d73;
      if (bVar1 != 0xb4) goto LAB_ram_1d93;
      cVar6 = (char)((ushort)sVar4 >> 8);
      cVar8 = cVar6 == '\0';
    } while (cVar6 == '\x01');
    sVar4 = (ushort)(byte)(cVar6 - 1) << 8;
    move_cursor_back_one_position_safe();
  } while( true );
}



void store_push_key_string_to_nvram(void)

{
  store_push_key_string_to_nvram();
  return;
}



void load_push_key_string_from_nvram(void)

{
                    // THUNK: Calls push key string loader from NVRAM
  load_push_key_string_from_nvram();
  return;
}



void scroll_lines_up_to_cursor(void)

{
  scroll_lines_up_to_cursor();
  return;
}



void scroll_lines_up_from_cursor(void)

{
  scroll_lines_up_from_cursor();
  return;
}



void scroll_lines_down_from_cursor(void)

{
  scroll_lines_down_from_cursor();
  return;
}



void setup_memory_region_for_display_operation(void)

{
  setup_memory_region_for_display_operation();
  return;
}



void restore_memory_region_after_display_operation(void)

{
  restore_memory_region_after_display_operation();
  return;
}



void process_and_display_template_text(void)

{
  process_and_display_template_text();
  return;
}



void display_formatted_string_with_templates(void)

{
  display_formatted_string_with_templates();
  return;
}



void display_decimal_number(void)

{
  display_decimal_number();
  return;
}



void wait_vertical_sync(void)

{
  wait_for_VSYNC();
  return;
}



void compare_de_hl_registers(void)

{
  compare_de_hl_registers();
  return;
}



void default_function_handler(void)

{
  return;
}



// WARNING: Control flow encountered bad instruction data
// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void terminal_boot_sequence(void)

{
  byte bVar1;
  short sVar2;
  char cVar3;
  
                    // TDV 2215 Terminal Boot Sequence - Power-up self-test sequence as per section
                    // 3.4. Tests ROM, NVRAM, communication interfaces, and displays error codes if
                    // malfunctions found. Loads soft-switches from non-volatile memory and
                    // initializes to TDV 2115 compatible mode (Extended Control OFF by default).
                    // Sets up 256-character support including semigraphic character set.
  disableMaskableInterrupts();
                    // Initial delay loop - Allow hardware to stabilize after power-on
                    // Check for presence of ROM at 0x8000 - Extended memory test
                    // Check system status word for cold boot vs warm boot
  sVar2 = 0x3000;
  do {
    sVar2 = sVar2 + -1;
  } while ((char)sVar2 != '\0' || (char)((ushort)sVar2 >> 8) != '\0');
                    // MAIN logic starts after the wait loop
                    // 
                    // Check if we have ROM in 0x8000
  if (((byte)(DAT_ram_8000 + DAT_ram_8001) == '\0') && (CARRY1(DAT_ram_8000,DAT_ram_8001))) {
    _boot_progress_marker = 0x234;
    initialize_system_variables();
                    // WARNING: Bad instruction - Truncating control flow here
    halt_baddata();
  }
  bVar1 = (byte)((ushort)_system_status_word >> 8);
                    // Display startup message and initialize keyboard
  cVar3 = !CARRY1(bVar1,(byte)_system_status_word);
  if (((byte)(bVar1 + (byte)_system_status_word) != '\0') || ((bool)cVar3)) {
    _boot_progress_marker = 0x24a;
    hardware_power_on_test();
  }
                    // Hardware Power-On Test sequence begins - comprehensive hardware validation
  _boot_progress_marker = 0x24d;
  bVar1 = initialize_system_variables();
  _boot_progress_marker = (ushort)bVar1 << 8;
  if (cVar3 != '\0') {
    _current_function_marker = 599;
    write_string();
    _current_function_marker = 0x25c;
    display_error_code_custom_chars();
    _current_function_marker = 0x261;
    write_to_keyboard_return_carry_if_busy(0x18);
  }
  _system_status_word = 0x8080;
                    // Set system ready flag and call hardware extensions
  _current_function_marker = 0x26d;
  call_hardware_extension();
  _current_function_marker = 0x273;
  call_hardware_extension();
  _current_function_marker = 0x279;
  write_string();
  do {
                    // WARNING: Do nothing block with infinite loop
  } while( true );
}



void call_hardware_extension(void)

{
  byte *in_HL;
  byte in_stack_00000002;
  
  if ((byte)(*in_HL + in_HL[1]) != '\0') {
    return;
  }
  if (!CARRY1(*in_HL,in_HL[1])) {
    return;
  }
  enableMaskableInterrupts();
                    // WARNING: Could not recover jumptable at 0x0285. Too many branches
                    // WARNING: Treating indirect jump as call
  (*(code *)(in_HL + 2))((char)((ushort)_in_stack_00000002 >> 8));
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void host_serial_interrupt_handler(void)

{
  interrupt_activity_flag = 0x5e;
  return;
}



void read_keyboard_data_with_param(undefined1 param_1)

{
                    // Read Keyboard Data with Parameter
                    // Gets function pointer from 0x5c24 and calls keyboard handler
                    // This is the main interface between terminal software and keyboard controller
                    // Parameter in A register specifies what type of keyboard operation to perform
  read_keyboard_data(param_1);
  return;
}



void read_keyboard_data(void)

{
  code *UNRECOVERED_JUMPTABLE;
  
                    // WARNING: Could not recover jumptable at 0x0357. Too many branches
                    // WARNING: Treating indirect jump as call
  (*UNRECOVERED_JUMPTABLE)();
  return;
}



void jump_to___5c22__can_be_0x29e_return_From_isr(void)

{
                    // WARNING: Could not recover jumptable at 0x0405. Too many branches
                    // WARNING: Treating indirect jump as call
  (*interrupt_service_routine_handler)();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 power_fail_interrupt_handler(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte bVar2;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (((char)((ushort)_system_status_word >> 8) == -0x80) && ((char)_system_status_word == -0x80)) {
    if (BYTE_ram_5e20 != 0) {
      BYTE_ram_5e20 = 2;
      return uVar1;
    }
    read_keyboard_data();
  }
  bVar2 = readInterruptMask();
  if ((bVar2 & 8) != 0) {
    enableMaskableInterrupts();
  }
  return uVar1;
}



void ISR_RST_6_5(void)

{
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void keyboard_buffer_interrupt_handler(void)

{
                    // TDV 2215 Keyboard Buffer Handler - Processes input from low-profile
                    // detachable keyboard. Handles convenience switches: Key Click (4.1.2), Auto
                    // Repeat (4.1.4), Key Rollover (4.1.5), and CAPS on Power Up (4.1.6). Manages
                    // PUSH-key sequence transmission and special key functions (MODE, PRINT, LINE,
                    // etc.).
  interrupt_activity_flag = 0x5e;
  return;
}



void call_function_pointer_6(void)

{
                    // WARNING: Could not recover jumptable at 0x048d. Too many branches
                    // WARNING: Treating indirect jump as call
  (*keyboard_test_handler)();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void printer_interrupt_handler(void)

{
                    // TDV 2215 Printer Interrupt Handler - Manages printer operations per Printer
                    // Mode switch (4.2.14): LOCAL/REM (local screen copy + remote MC commands),
                    // REMOTE (MC commands only), LOG (line-by-line data transfer). Supports
                    // optional 2000-character print buffer for background printing. Handles Form
                    // Feed control (4.2.15) and XON/XOFF handshake (4.3.13).
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte printer_interface_handler(void)

{
  byte in_io_00000060;
  byte bVar1;
  bool bVar2;
  
                    // Handles printer interface operations based on BYTE_ram_5de0 state. State 0:
                    // returns status register value. State 1: checks ControlPort2 bit 0 (ATRW),
                    // sets printer timer (3000), and processes printer data. Uses
                    // read_from_buffer_or_nvram for data processing.
  if (BYTE_ram_5de0 == 0) {
    return BYTE_ram_5e11;
  }
  if (BYTE_ram_5de0 != 1) {
    return BYTE_ram_5de0;
  }
  if ((sw_line_local_status == 0) && ((in_io_00000060 & 1) != 0)) {
    _printer_timer_counter = 3000;
    timer_channel_flags = timer_channel_flags | 8;
    bVar1 = combine_status_register_and_c_register();
    return bVar1;
  }
  bVar2 = false;
  bVar1 = read_from_buffer_or_nvram();
  if (!bVar2) {
    return bVar1;
  }
  BYTE_ram_5de0 = 0;
  return 0;
}



void printer_data_processor(void)

{
  byte in_io_00000019;
  char cVar1;
  
                    // Processes printer data based on baud rate and input codes. Handles XON/XOFF
                    // flow control (0x11/0x13). If baud_rate_current=0, enqueues data to circular
                    // buffer. Special handling for codes 0x11 (XON) and 0x13 (XOFF) with state
                    // management.
  cVar1 = false;
  if (sw_underline_representation != 0) {
    if (in_io_00000019 == 0x13) {
      combine_status_register_and_c_register();
      BYTE_ram_5de0 = 2;
    }
    else {
      cVar1 = in_io_00000019 < 0x11;
      if (in_io_00000019 != 0x11) goto LAB_ram_0516;
      combine_status_register_and_c_register();
      BYTE_ram_5de0 = 1;
    }
    return;
  }
LAB_ram_0516:
  circular_buffer_enqueue();
  if (cVar1 == '\0') {
    return;
  }
  read_keyboard_data(0x80);
  return;
}



void read_keyboard_data_wrapper(void)

{
                    // Simple wrapper function that calls read_keyboard_data(). Used by VSYNC
                    // interrupt handler and other keyboard processing routines for consistent
                    // keyboard data reading interface.
  read_keyboard_data();
  return;
}



byte combine_status_register_and_c_register(void)

{
  byte in_C;
  
                    // Combines BYTE_ram_5e11 status register value with C register using OR
                    // operation. Returns combined status value for printer and serial interface
                    // status reporting.
  return BYTE_ram_5e11 | in_C;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 vsync_interrupt_handler(void)

{
  byte in_io_00000060;
  undefined2 in_AF;
  byte bVar1;
  byte bVar2;
  byte bVar3;
  char cVar4;
  short sVar5;
  short *psVar6;
  
  set_to_1_by_vsync = 1;
  bVar3 = (in_io_00000060 & 0xc) >> 2;
  if ((expected_control_port2_value_maybe & 3) != bVar3) {
    expected_control_port2_value_maybe = expected_control_port2_value_maybe & 0x28 | bVar3;
    jump_to___5c22__can_be_0x29e_return_From_isr();
  }
  bVar1 = 0;
  bVar2 = 0xff;
  bVar3 = display_refresh_control & 3;
  display_refresh_control = bVar3 << 1;
  if (bVar3 == 0) {
    if (video_off_control_flag == 0) {
      bVar1 = 0x10;
      bVar2 = 0xff;
    }
  }
  else if (bVar3 == 1) {
    bVar1 = 0;
    bVar2 = 0xef;
  }
  if (interrupt_activity_flag == 0) {
    if ((sw_roll_page_mode == 0) &&
       (keyboard_activity_timeout_counter = keyboard_activity_timeout_counter + -1,
       keyboard_activity_timeout_counter == 0)) {
      bVar1 = bVar1 | 8;
    }
  }
  else {
    keyboard_activity_timeout_counter = 30000;
    if (status_port_bit3_control_flag == 0) {
      bVar2 = bVar2 & 0xf7;
    }
  }
  value_for_statusport_2 = value_for_statusport_2 & bVar2 | bVar1;
  if (statusport2_flag == 0) {
    in_io_00000060 = value_for_statusport_2;
  }
  interrupt_activity_flag = 0;
  bVar3 = timer_channel_flags;
  if ((timer_channel_flags & 0xf) == 0) goto LAB_ram_0634;
  if ((timer_channel_flags & 8) != 0) {
    if ((in_io_00000060 & 1) != 0) {
      _printer_timer_counter = _printer_timer_counter + -1;
      if (_printer_timer_counter != 0) goto LAB_ram_05f4;
      read_keyboard_data_wrapper(8);
      check_printer_status();
    }
    bVar3 = bVar3 & 0xf7;
  }
LAB_ram_05f4:
  if ((bVar3 & 7) != 0) {
    cVar4 = '\x03';
    psVar6 = (short *)&DAT_ram_5c00;
    do {
      bVar1 = bVar3 & 1;
      bVar3 = bVar3 >> 1 | bVar3 << 7;
      if (bVar1 == 0) {
LAB_ram_0623:
        psVar6 = psVar6 + 2;
      }
      else {
        sVar5 = *psVar6 + -1;
        *psVar6 = sVar5;
        if ((char)sVar5 != '\0' || (char)((ushort)sVar5 >> 8) != '\0') goto LAB_ram_0623;
        psVar6 = psVar6 + 2;
        read_keyboard_data();
        bVar3 = bVar3 & 0x7f;
      }
      cVar4 = cVar4 + -1;
    } while (cVar4 != '\0');
    bVar1 = (bVar3 << 1 | bVar3 >> 7) << 1;
    bVar3 = (bVar1 | (byte)(bVar3 << 1) >> 7) << 1 | bVar1 >> 7;
  }
LAB_ram_0634:
  timer_channel_flags = bVar3;
  return (char)((ushort)in_AF >> 8);
}



undefined1 validate_display_memory_bounds(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte in_L;
  byte in_H;
  
                    // Validates display memory bounds. Checks if L register is in range 1-80
                    // (0x01-0x50) and H register is in range 1-25 (0x01-0x19). Used extensively by
                    // cursor management and display memory operations to prevent out-of-bounds
                    // access.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((((in_L != 0) && (in_L < 0x51)) && (in_H != 0)) && (in_H < 0x1a)) {
    return uVar1;
  }
  return uVar1;
}



undefined1 validate_display_region_with_bounds_check(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined2 in_HL;
  char in_CY_flag;
  
  validate_display_memory_bounds();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_CY_flag == '\0') ||
     (((char)((ushort)in_HL >> 8) == '\x1a' &&
      (in_CY_flag = (char)in_HL == '\0', (char)in_HL == '\x01')))) {
    compare_de_hl_registers();
    if (in_CY_flag != '\0') {
      return uVar1;
    }
  }
  return uVar1;
}



undefined1 return_af_register_high_byte(void)

{
  undefined2 in_AF;
  
  return (char)((ushort)in_AF >> 8);
}



undefined1 return_register_a(void)

{
  undefined2 in_AF;
  
  return (char)((ushort)in_AF >> 8);
}



void check_display_memory_boundary_markers(void)

{
  char in_L;
  char in_H;
  
  if (in_L == 'O') {
    if (in_H != 'x') {
      return;
    }
    return;
  }
  if (in_L != -2) {
    return;
  }
  if (in_H != '`') {
    return;
  }
  return;
}



void check_scroll_direction_boundary_markers(void)

{
  char in_L;
  char in_H;
  
  if ((byte)(scroll_direction_flag + in_L) == 'P') {
    if (in_H != 'x') {
      return;
    }
    return;
  }
  if ((byte)(scroll_direction_flag + in_L) != -1) {
    return;
  }
  if (in_H != '`') {
    return;
  }
  return;
}



undefined1 sleep_BC_loops(void)

{
  undefined2 in_AF;
  short in_BC;
  
  do {
    in_BC = in_BC + -1;
  } while (in_BC != 0);
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Removing unreachable block (ram,0x06d7)

void wait_for_VSYNC(void)

{
  enableMaskableInterrupts();
  do {
  } while( true );
}



byte display_decimal_number(void)

{
  short in_AF;
  byte bVar1;
  char cVar2;
  byte bVar3;
  char cVar4;
  bool bVar5;
  
  bVar3 = (byte)((ushort)in_AF >> 8);
  if (bVar3 < 100) {
    cVar4 = '0';
    while( true ) {
      bVar1 = (byte)((ushort)in_AF >> 8);
      in_AF = (ushort)(byte)(bVar1 - 10) << 8;
      if (bVar1 < 10) break;
      cVar4 = cVar4 + '\x01';
    }
    bVar5 = 0xc5 < (byte)(bVar1 - 10);
    cVar2 = bVar1 + 0x30;
    write_char_and_advance_cursor(cVar4);
    if (!bVar5) {
      write_char_and_advance_cursor(cVar2);
      if (!bVar5) {
        bVar3 = pop_hl_and_af_return_no_carry();
        return bVar3;
      }
    }
  }
  return bVar3;
}



byte display_error_code_custom_chars(void)

{
  byte bVar1;
  undefined2 in_AF;
  byte bVar2;
  byte bVar3;
  byte bVar4;
  bool bVar5;
  
                    // Error Code Display Using Custom Character Set
                    // Converts 8-bit error code to 2 custom characters (NOT standard hex digits!)
                    // Error 0x14: high nibble 1→'q', low nibble 4→'t', displays as 'qt' or 'QT'
                    // Uses custom conversion: 0-9→p-y, A-F→extended chars
                    // This explains why error codes don't show as normal hex digits!
  bVar2 = (byte)((ushort)in_AF >> 8);
  bVar3 = (bVar2 << 1 | bVar2 >> 7) << 1;
  bVar1 = (bVar3 | (byte)(bVar2 << 1) >> 7) << 1;
  bVar5 = (bool)(bVar1 >> 7);
  bVar4 = bVar2;
  convert_nibble_to_custom_char((bVar1 | bVar3 >> 7) << 1 | bVar5);
  if (!bVar5) {
    convert_nibble_to_custom_char(bVar4);
    if (!bVar5) {
      bVar3 = pop_hl_and_af_return_no_carry();
      return bVar3;
    }
  }
  return bVar2;
}



void convert_nibble_to_custom_char(byte param_1)

{
  byte bVar1;
  byte uVar2;
  
                    // TDV 2215 Custom Character Converter - Converts nibble values to custom
                    // characters from the extended 256-character set. Supports line drawing
                    // characters, histogram symbols, numeric sub/superscript, and plot characters
                    // that extend beyond the basic TDV 2115 character set.
  param_1 = param_1 & 0xf;
  bVar1 = BCDadjust(param_1 + 0x90);
  hasEvenParity(bVar1);
  uVar2 = BCDadjust(bVar1 + 0x40 +
                    (CARRY1(bVar1,0x6f < param_1) || 0xbf < (byte)(bVar1 + (0x6f < param_1))));
  hasEvenParity(uVar2);
  write_to_display();
  increase_cursor_character();
  return;
}



void write_char_and_advance_cursor(void)

{
  write_to_display();
  increase_cursor_character();
  return;
}



byte parse_hex_byte(void)

{
  byte bVar1;
  byte bVar2;
  undefined1 in_CY_flag;
  bool bVar3;
  
  bVar1 = validate_hex_input_character();
  if ((bool)in_CY_flag) {
    return bVar1;
  }
  bVar3 = false;
  bVar1 = ((bVar1 << 1 | in_CY_flag) & 0x1e) << 3;
  bVar2 = validate_hex_input_character(bVar1,bVar1);
  if (!bVar3) {
    return bVar2 & 0xf | bVar1;
  }
  return bVar2;
}



void validate_hex_input_character(void)

{
  byte bVar1;
  
  bVar1 = read_and_normalize_keyboard_char();
  if (bVar1 < 0x30) {
    return;
  }
  if ((9 < (byte)(bVar1 - 0x30)) && (0xf < (byte)(bVar1 - 0x37))) {
    return;
  }
  return;
}



byte read_and_normalize_keyboard_char(void)

{
  byte bVar1;
  undefined1 in_CY_flag;
  
  do {
                    // CHARACTER READ AND NORMALIZATION LOOP
                    // Calls keyboard buffer read function and normalizes character case
                    // Converts lowercase to uppercase, handles special character ranges
    bVar1 = get_keyboard_char_atomic();
                    // WAIT FOR CHARACTER LOOP: Keep polling until character available
                    // If carry set (buffer empty), loop back and try again
  } while ((bool)in_CY_flag);
  if (bVar1 < 0x60) {
    return bVar1;
  }
  if ((0xdf < bVar1) && (bVar1 < 0xea)) {
    return bVar1 + 0x50;
  }
  return bVar1 & 0x5f;
}



void compare_de_hl_registers(void)

{
  char in_D;
  char in_H;
  
                    // Compare DE and HL Registers - Used for memory range boundary checking
                    // Sets Z flag if DE == HL (end of memory range reached)
                    // This determines when memory testing/clearing loops should terminate
  if (in_D != in_H) {
    return;
  }
  return;
}



void compare_parameter_with_h_register(undefined1 param_1,char param_2)

{
  char in_H;
  
  if (param_2 != in_H) {
    return;
  }
  return;
}



void compare_parameter_with_d_register(undefined1 param_1,char param_2)

{
  char in_D;
  
  if (param_2 != in_D) {
    return;
  }
  return;
}



byte keyboard_status_and_error_handler(void)

{
  byte in_io_00000010;
  byte in_io_00000011;
  ushort in_AF;
  byte bVar1;
  
  bVar1 = (byte)(in_AF >> 8);
  error_message_flag = bVar1 & 1;
  if ((in_AF & 0x200) != 0) {
    do {
      do {
      } while ((in_io_00000010 & 1) == 0);
    } while (((in_io_00000011 & 0xfc) != 0xdc) ||
            (error_message_flag = in_io_00000011 & 1, (in_io_00000011 & 2) != 0));
    return value_for_statusport_2;
  }
  return bVar1 & 2;
}



void set_display_status_flag(void)

{
  disableMaskableInterrupts();
  statusport2_flag = 1;
  enableMaskableInterrupts();
  return;
}



void set_display_status_flag_alt(void)

{
  disableMaskableInterrupts();
  statusport2_flag = 1;
  enableMaskableInterrupts();
  return;
}



void send_A_to_statusport(void)

{
  statusport2_flag = 1;
  enableMaskableInterrupts();
  return;
}



void configure_display_control_port_attributes(void)

{
  disableMaskableInterrupts();
  send_A_to_statusport(value_for_statusport_2 & 0xd4 | 0xb);
  return;
}



byte write_AttributeRegister_And_ControlPort2(void)

{
                    // I/O Write to port 0x70: ChipSelect.AttributeRegister. Writing to 74LS173
                    // 4-bit D-type register with tri-state for character attribute control. Sets
                    // display attributes (foreground/background colors, intensity, etc.)
  disableMaskableInterrupts();
  statusport2_flag = 0;
                    // I/O Write to port 0x60: ChipSelect.ControlPort2. Writing to 74LS377 8-bit
                    // flip-flop U81. Controls display memory mapping: ATRW(bit0), CHRW(bit1),
                    // BKCUR(bit2), VOF(bit3), CEN(bit4), /SELDOM(bit5), /BAC(bit6), /DISSEL(bit7)
  enableMaskableInterrupts();
  return value_for_statusport_2;
}



undefined1 popall_and_ret(void)

{
  undefined2 in_stack_00000006;
  
  return (char)((ushort)in_stack_00000006 >> 8);
}



undefined1 return_stack_value_high_byte(void)

{
  undefined2 in_stack_00000006;
  
  return (char)((ushort)in_stack_00000006 >> 8);
}



undefined1 pop_hl_and_af_return_no_carry(void)

{
  undefined2 in_stack_00000002;
  
  return (char)((ushort)in_stack_00000002 >> 8);
}



undefined1 return_stack_value_high_byte_alt(void)

{
  undefined2 in_stack_00000006;
  
  return (char)((ushort)in_stack_00000006 >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 initialize_system_variables(void)

{
  byte bVar1;
  byte bVar2;
  undefined2 in_AF;
  char cVar3;
  
                    // System Variable Initialization - DEBUG CHECKPOINT
                    // Check if this function properly initializes:
                    // - keyboard_transmission_enable (0x5f13) to 1
                    // - keyboard_buffer_counter (0x5d2e) to 0x20  
                    // - serial_transmission_state (0x5d51) to 0
                    // - keyboard_handler_function_pointer (0x5c24) to valid address
  disableMaskableInterrupts();
  keyboard_transmit_with_serial_retry();
  setInterruptMask(10);
  ContolPort1_value = 0x48;
  value_for_statusport_2 = 0xf2;
  cursor_row_position = 0x18;
  _current_cursor_character_address = 0x6000;
  _next_cursor_character_address = 0x101;
  keyboard_activity_timeout_counter = 30000;
  bVar1 = 0x92;
  sleep_BC_loops(0x20,1);
  _ISR_RETURN_ADDRESS = 0x10;
  ISR_65_RETURN_ADDRESS = 0x10;
  ISR_VSCYN_RETURN_ADDRESS = 0x10;
  _nvram_test_current_address = &Sleep_0xAC22_loops;
  _serial_interrupt_return_handler = 0x29f;
  _hardware_handler_pointer = 0x44d;
  _device_control_handler = 0x494;
  primary_interrupt_handler = 0x29e;
  dispatch_function_pointer_2 = 0x29e;
  interrupt_service_routine_handler = 0x29e;
  keyboard_communication_handler = 0x29e;
  keyboard_input_handler = 0x29e;
  keyboard_test_handler = 0x29e;
  keyboard_data_handler = 0x29e;
  keyboard_handler_function_pointer = 0x29e;
  _config_template_pointer = &config_template_table;
  load_config_from_nvram();
  setup_serial_and_display_ports();
  disableMaskableInterrupts();
  do {
  } while ((bVar1 & 2) == 0);
  bVar2 = 0x2a;
  cVar3 = '\t';
  do {
    sleep_BC_loops();
    if ((bVar1 & 1) == 0) break;
    if ((bVar2 & 0xfc) == 0xdc) {
      keyboard_status_and_error_handler(bVar2 & 3);
      BYTE_ram_5e0a = 0;
      goto LAB_ram_08c9;
    }
    cVar3 = cVar3 + -1;
  } while (cVar3 != '\0');
  error_message_flag = 0;
  BYTE_ram_5e0a = 1;
LAB_ram_08c9:
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



void setup_serial_and_display_ports(void)

{
  configure_display_control_port_and_wait_ready();
  setup_serial_channel_A_and_B();
  di_push_all();
  clear_display_memory_range();
  return;
}



undefined1 configure_display_control_port_and_wait_ready(void)

{
  byte bVar1;
  undefined2 in_AF;
  char cVar2;
  undefined1 uVar3;
  
  disableMaskableInterrupts();
  bVar1 = 0;
  if (sw_end_of_line_wrap != 0) {
    bVar1 = 4;
  }
  value_for_statusport_2 = value_for_statusport_2 & 0xfb | bVar1;
                    // I/O Write to port 0x60: ChipSelect.ControlPort2 (74LS377 U81). Writing
                    // display control configuration with BKCUR bit manipulation for block cursor
                    // control during display setup operations.
  do {
    uVar3 = 0;
    cVar2 = sw_printer_form_feed == 0;
    if ((bool)cVar2) {
      wait_for_hardware_ready_bit2();
    }
    if (cVar2 == '\0') {
      validate_af_with_interrupt_disable();
    }
  } while ((bool)uVar3);
  do {
    uVar3 = 0;
    cVar2 = sw_send_receive_mode == 0;
    if ((bool)cVar2) {
      check_hardware_ready_bit2();
    }
    if (cVar2 == '\0') {
      validate_af_with_interrupt_disable_alt();
    }
  } while ((bool)uVar3);
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 setup_serial_channel_A_and_B(void)

{
  undefined2 in_AF;
  undefined *puVar1;
  undefined1 *puVar2;
  
                    // Z80SIO Dual Channel Setup - Channel A and B initialization
                    // Sets up both serial channels with standard parameters
                    // Channel A and B both configured with WR0, WR4, WR5 registers
  disableMaskableInterrupts();
  ContolPort1_value =
       (~sw_roll_type & 1 | (sw_clear_lamps & 1) << 1 | sw_clear_lamps >> 7) << 3 | 0x40;
                    // I/O Write to port 0x50: ChipSelect.ControlPort1 (74LS377 U29). Writing
                    // control configuration with bits: C0(0), C1(1), NVSEL(2), V24EN(3), ECSA(4),
                    // /SSE(5), /SETSEL(6), DISBRD(7) for system control and memory mapping.
  puVar2 = &keyboard_tx_buffer_start;
  keyboard_transmit_with_serial_retry();
  init_host_serial_port();
                    // Initialize serial port B similar to A
                    // I/O Write to port 0x03: Z80SIO2 Channel B Control Register
                    // (ChipSelect.SerialIO, chip_reg=3, B/A=1, C/D=1). Writing 0x18 for channel
                    // reset - initializes serial channel B for communication.
                    // I/O Write to port 0x03: Z80SIO2 Channel B WR4 Register. Writing 0x04 for
                    // clock mode and serial format configuration: async mode, 1x clock, parity
                    // settings.
  calculate_z80sio_wr4_register(sw_margin_bell);
                    // I/O Write to port 0x03: Z80SIO2 Channel B WR4 Register. Writing calculated
                    // value (0x44 typical) for communication parameters: parity odd, 1 stop bit,
                    // async mode with clock divisor.
                    // I/O Write to port 0x03: Z80SIO2 Channel B WR5 Register select. Writing 0x05
                    // to select WR5 for transmit control configuration in next write operation.
  puVar1 = calculate_z80sio_wr5_register(sw_cursor_type);
  printer_char_transmission_mask = (byte)puVar1;
  delay_loop(sw_bell);
  puVar2 = puVar2 + 1;
  delay_loop(sw_auto_repeat);
  handle_keyboard_communication(puVar2[1]);
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 di_push_all(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  check_printer_status();
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



byte check_printer_status(void)

{
  byte *pbVar1;
  byte *pbVar2;
  
  keyboard_transmit_with_serial_retry();
                    // I/O Write to port 0x18: MC6850 Printer Control Register
                    // (ChipSelect.KeyboardAndPrinter, chip_reg=8, A3=1). Writing 0x97 to configure
                    // printer serial interface: data format, baud rate, and control settings.
  pbVar1 = &DAT_ram_0a90;
  do {
    pbVar2 = pbVar1;
    pbVar1 = pbVar2 + 2;
  } while (*pbVar2 !=
           ((sw_cursor_return |
            (sw_graphic_rendition_mode << 1 | sw_graphic_rendition_mode >> 7) << 1 |
            (byte)(sw_graphic_rendition_mode << 1) >> 7) & 7));
  pbVar2 = pbVar2 + 1;
  BYTE_ram_5e11 = *pbVar2;
  delay_loop(sw_beginning_of_line_wrap);
                    // I/O Write to port 0x23: Z80SIO2 Channel A Control Register
                    // (ChipSelect.SerialIO, chip_reg=3, B/A=0, C/D=1). Writing WR5 register
                    // configuration for transmit control, RTS, DTR, and data bits.
                    // I/O Write to port 0x22: Z80SIO2 Channel A Data Register (ChipSelect.SerialIO,
                    // chip_reg=2, B/A=0, C/D=0). Writing data byte to serial transmission buffer
                    // for host communication.
  return pbVar2[1];
}



void init_host_serial_port(void)

{
                    // TDV 2215 Host Serial Port Initialization - Configures Z80SIO Channel A per
                    // communication switches (4.3). Sets transmission speed (50-19200 baud), code
                    // format (7/8 bit, parity, stop bits), handshake (OFF/XON-XOFF/DTR), and
                    // interface type (V.24/V.11). Supports 20mA current loop via optional adapter
                    // with V.24 setting.
  calculate_z80sio_wr4_register(sw_timeout);
  calculate_z80sio_wr5_register(sw_key_rollover);
  return;
}



byte calculate_z80sio_wr4_register(char param_1)

{
  byte bVar1;
  char in_C;
  byte bVar2;
  
                    // Calculate Z80SIO WR4 register value by combining clock configuration (in C)
                    // and character format configuration (in A). Returns complete WR4 register
                    // value for programming Z80SIO character format and timing.
  bVar2 = 0x40;
  if ((param_1 != '\0') && (bVar2 = 0x43, param_1 != '\x01')) {
    bVar2 = 0x41;
  }
  bVar1 = 4;
  if (in_C != '\0') {
    bVar1 = 0xc;
  }
  return bVar1 | bVar2;
}



// WARNING: Unknown calling convention

undefined * calculate_z80sio_wr5_register(byte offset)

{
  undefined *puVar1;
  
                    // Calculate Z80SIO WR5 register value from transmit configuration in A
                    // register. Processes DTR, RTS, and transmit control settings into proper WR5
                    // format for Z80SIO programming.
  puVar1 = (undefined *)0x407f;
  if (offset != 0) {
    puVar1 = (undefined *)0xc0ff;
  }
  return puVar1;
}



void delay_loop(char param_1)

{
  param_1 = param_1 + '\x01';
  do {
    param_1 = param_1 + -1;
  } while (param_1 != '\0');
  return;
}



void handle_keyboard_communication(void)

{
  undefined1 uVar1;
  
  keyboard_data_ready_flag = 0;
  uVar1 = false;
  if (sw_printer_mode == 0) {
    return;
  }
  read_keyboard_data_with_param(0x11);
  if ((bool)uVar1) {
    return;
  }
  do {
    serial_start_transmission_request();
  } while ((bool)uVar1);
  return;
}



void keyboard_transmit_with_serial_retry(void)

{
  undefined1 *in_HL;
  char in_Z_flag;
  
  do {
                    // MEMORY CLEAR FUNCTION - INFINITE LOOP BUG
                    // Called with HL=start address, DE=end address
                    // Should clear memory from HL to DE but loops infinitely
                    // BUG: Condition check is wrong - never reaches terminal_main_loop because
                    // stuck here
    *in_HL = 0;
    in_HL = in_HL + 1;
    compare_de_hl_registers();
  } while (in_Z_flag == '\0');
  return;
}



void hardware_power_on_test(void)

{
  char in_CY_flag;
  
                    // TDV 2215 Hardware Power-On Test - Comprehensive hardware validation during
                    // terminal startup. Tests all major subsystems including display controller,
                    // serial interfaces (V.24/V.11/current loop), keyboard interface, NVRAM, and
                    // Z80SIO ports. Ensures all hardware is functioning correctly before terminal
                    // becomes operational.
  nvram_range_memory_test();
  initialize_system_variables(6);
  if (in_CY_flag == '\0') {
    initialize_display_memory();
  }
  if (in_CY_flag == '\0') {
    test_z80sio_ports();
  }
  if (in_CY_flag == '\0') {
    test_keyboard_communication();
  }
  if (in_CY_flag == '\0') {
    test_nvram();
  }
  if (in_CY_flag == '\0') {
    load_nvram_config();
  }
  return;
}



undefined1 pop_and_return_OK(void)

{
  undefined2 in_stack_00000000;
  
  return (char)((ushort)in_stack_00000000 >> 8);
}



void pop_and_return_with_carry(void)

{
  return;
}



void load_nvram_config(void)

{
                    // Step 5: Load NVRAM Configuration - Restore saved terminal settings
  nvram_test_and_load_config();
  return;
}



void test_nvram(void)

{
                    // Step 4: NVRAM Test - Verify non-volatile memory for configuration storage
  nvram_test_and_load_config();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address
// WARNING: Unknown calling convention -- yet parameter storage is locked

byte nvram_test_and_load_config(void)

{
  char cVar1;
  byte bVar2;
  undefined2 in_BC;
  char cVar3;
  bool bVar4;
  undefined2 in_stack_00000000;
  
                    // ER3400 NVRAM Test Function - Complete NVRAM validation sequence
                    // 1. Tests signature bytes at 0x50,0x51 for 0x80 pattern
                    // 2. Performs full NVRAM scan checking configuration data
                    // 3. Validates checksum against expected 0xAA value
                    // 4. Returns specific error codes for different failure types
  _nvram_current_address_pointer = 0x50;
  cVar1 = read_from_nvram();
                    // NVRAM Signature Check - First byte at 0x6050 must be 0x80
                    // This is the critical test that's failing in the emulator!
                    // ER3400 should return 0x80 from address 0x50 when NVSEL enabled
  if (cVar1 == -0x80) {
    cVar1 = read_from_nvram();
                    // NVRAM Signature Check - Second byte at 0x6051 must also be 0x80
                    // Both signature bytes must match for NVRAM to be considered valid
    if (cVar1 == -0x80) {
                    // Clear NVRAM Checksum Accumulator - Initialize to 0 before processing
                    // configuration data
      nvram_checksum_accumulator = 0;
      do {
                    // MAIN NVRAM PROCESSING LOOP START
                    // This is the core loop that processes NVRAM addresses 0x52 through 0x5E
                    // Loop continues until current address reaches 0x5F (end condition)
        cVar1 = (char)in_BC;
                    // Load current NVRAM address being processed (starts at 0x52, ends at 0x5E)
                    // Increment to next NVRAM address (0x52→0x53→0x54...→0x5E→0x5F)
        bVar2 = (byte)(_nvram_current_address_pointer + 1);
                    // LOOP EXIT CONDITION: Check if we've reached address 0x5F (beyond last config
                    // address 0x5E)
        cVar3 = bVar2 < 0x5f;
                    // EXIT LOOP: Jump to final validation if we've processed all addresses
                    // (0x52-0x5E)
        if (bVar2 == 0x5f) {
                    // FINAL VALIDATION AFTER LOOP: Check if any errors occurred during processing
                    // C register contains error flags accumulated during loop execution
          if ((cVar1 != '\0') && (nvram_checksum_accumulator != 0xaa)) {
            bVar2 = pop_and_return_with_carry(0x19);
            return bVar2;
          }
          return (byte)((ushort)in_stack_00000000 >> 8);
        }
        _nvram_current_address_pointer = _nvram_current_address_pointer + 1;
        bVar2 = read_from_nvram();
        in_BC = CONCAT11(bVar2,cVar1);
        delay_loop_function((byte)((byte)(bVar2 >> 1 | cVar3 << 7) >> 1 | bVar2 << 7) >> 1 |
                            (bVar2 >> 1) << 7);
        if (((byte)((ushort)in_BC >> 8) & 0xc0) == 0x40) {
          bVar4 = false;
          if ((char)in_BC == '\0') {
            nvram_range_memory_test();
            if (bVar4) {
                    // ERROR CODE CALCULATION MYSTERY:
                    // Code calculates: current_address - 0x50 
                    // But actual error codes suggest the full address (0x51, 0x54) is used
                    // This indicates either:
                    // 1. Different error path than shown here, OR  
                    // 2. The address itself becomes the error code through another mechanism
                    // INVESTIGATION NEEDED: Trace exactly how 0x51/0x54 get into A register
              bVar2 = pop_and_return_with_carry(nvram_current_address_pointer + 0xb0);
              return bVar2;
            }
            goto LAB_ram_0b3b;
          }
        }
        else {
LAB_ram_0b3b:
          if ((((byte)((ushort)in_BC >> 8) & 0xc0) == 0x80) && ((char)in_BC != '\0')) {
            accumulate_memory_checksum();
          }
        }
        delay_loop_function((char)((ushort)in_BC >> 8));
                    // LOOP BACK: Return to start of processing loop for next NVRAM address
      } while( true );
    }
  }
  bVar2 = pop_and_return_with_carry(0x1a);
  return bVar2;
}



void delay_loop_function(byte param_1)

{
                    // Delay/Wait Loop Function - Creates timing delays based on parameter
                    // Used during NVRAM configuration processing for timing control
                    // Parameter & 7 determines delay length (0 and 7 = no delay)
  param_1 = param_1 & 7;
  if (param_1 == 0) {
    return;
  }
  if (param_1 == 7) {
    return;
  }
  do {
    param_1 = param_1 - 1;
  } while (param_1 != 0);
  return;
}



char nvram_range_memory_test(void)

{
  char cVar1;
  char *in_HL;
  char *pcVar2;
  char *pcVar3;
  char in_Z_flag;
  char cVar4;
  
                    // NVRAM Range Memory Test - Tests memory range specified by HL register
                    // This function performs memory testing during NVRAM configuration load
                    // CRITICAL: The HL register contains the memory range to test
                    // If memory test fails, it causes the system to return error codes
                    // The unmapped memory access (0x4800-0x57FF) is likely coming from this
                    // function!
  pcVar2 = in_HL;
  do {
    *pcVar2 = (char)pcVar2;
    pcVar2 = pcVar2 + 1;
    compare_de_hl_registers();
    pcVar3 = in_HL;
  } while (in_Z_flag == '\0');
  do {
    cVar1 = *pcVar3;
    cVar4 = cVar1 == (char)pcVar3;
    if (!(bool)cVar4) goto LAB_ram_0bc4;
    pcVar3 = pcVar3 + 1;
    compare_de_hl_registers();
    pcVar2 = in_HL;
  } while (cVar4 == '\0');
  do {
    *pcVar2 = (char)((ushort)pcVar2 >> 8);
    pcVar2 = pcVar2 + 1;
    compare_de_hl_registers();
  } while (cVar4 == '\0');
  do {
    cVar1 = *in_HL;
    cVar4 = cVar1 == (char)((ushort)in_HL >> 8);
    if (!(bool)cVar4) break;
    in_HL = in_HL + 1;
    cVar1 = compare_de_hl_registers();
  } while (cVar4 == '\0');
LAB_ram_0bc4:
  keyboard_transmit_with_serial_retry();
  return cVar1;
}



undefined1 accumulate_memory_checksum(void)

{
  undefined2 in_AF;
  char cVar1;
  char *in_HL;
  bool bVar2;
  
                    // Memory Checksum Accumulator Function
                    // Adds bytes from memory range (HL to DE) to the global checksum accumulator
                    // Process: 1) Sum all bytes in range HL to DE into local accumulator
                    //          2) Add local sum to global nvram_checksum_accumulator
                    //          3) This is called for each configuration data block
  cVar1 = '\0';
  do {
    cVar1 = cVar1 + *in_HL;
    bVar2 = cVar1 == '\0';
    in_HL = in_HL + 1;
    compare_de_hl_registers();
  } while (!bVar2);
  nvram_checksum_accumulator = nvram_checksum_accumulator + cVar1;
  return (char)((ushort)in_AF >> 8);
}



void initialize_display_memory(void)

{
  byte bVar1;
  byte bVar2;
  ushort uVar3;
  byte *pbVar4;
  char cVar5;
  byte *pbStack_c;
  
                    // TDV 2215 Display Memory Initialization - Initializes video memory for the 15"
                    // display screen. Sets up memory regions for character display, attributes, and
                    // the 256-character set support (including line drawing, histogram,
                    // sub/superscript, and plot characters). Part of the terminal's TDV 2115
                    // compatible display system with extensions.
  configure_display_control_port_attributes();
  uVar3 = 0;
  pbVar4 = &display_memory_start;
  pbStack_c = &display_memory_start;
  do {
    bVar1 = (byte)uVar3;
    *pbVar4 = bVar1;
    uVar3 = (ushort)(byte)(bVar1 + 1);
    cVar5 = (byte)(bVar1 + 1) == 0;
    check_display_memory_boundary_markers();
    compare_de_hl_registers();
  } while (cVar5 == '\0');
  uVar3 = 0;
  while( true ) {
    if ((*pbStack_c != (byte)uVar3) || ((bVar1 & 0xf) != (*pbStack_c & 0xf))) break;
    bVar2 = (byte)uVar3 + 1;
    uVar3 = (ushort)bVar2;
    cVar5 = bVar2 == 0;
    check_display_memory_boundary_markers();
    compare_de_hl_registers();
    if (cVar5 != '\0') {
      clear_display_memory_range();
      pop_and_return_OK();
      return;
    }
  }
  clear_display_memory_range();
  pop_and_return_with_carry();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 test_z80sio_ports(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte bVar2;
  char cVar3;
  char cVar4;
  bool bVar5;
  
                    // TDV 2215 Z80SIO Port Test - Tests the Z80SIO serial controller used for host
                    // communication. Validates proper operation of V.24 (RS-232-C), V.11 (RS-422),
                    // and current loop interfaces. Critical for ensuring reliable
                    // asynchronous/isochronous transmission at 50-19200 baud rates.
  disableMaskableInterrupts();
  cVar4 = '\0';
  cVar3 = (value_for_statusport_2 & 0xbf) == 0;
  statusport2_flag = 1;
  init_host_serial_port();
  do {
    uVar1 = keyboard_to_host_transmission();
  } while (cVar4 == '\0');
  _z80sio_channel_a_test_status = 0;
  _z80sio_channel_b_test_status = 0;
  _BYTE_ram_5e05 = keyboard_communication_handler;
  keyboard_communication_handler = 0xcf1;
  uVar1 = update_uart_txcontrol_check_DTR_RTS(uVar1,0);
  if ((((bool)cVar3) || (uVar1 = update_uart_txcontrol_check_DTR_RTS(uVar1,0x80), cVar3 == '\0')) ||
     (update_uart_txcontrol_check_DTR_RTS(uVar1,2), cVar3 == '\0')) {
set_A_0x12_return_ERR:
    bVar2 = 0x12;
    goto LAB_ram_0cb1;
  }
  bVar5 = false;
  counter__init_0 = 0;
                    // This is the place it fails right now!!
  bVar2 = test_keyboard_to_host_transmission(0x55);
  if (bVar5) {
set_A_0x14_return_ERR:
    bVar2 = 0x14;
  }
  else {
    bVar5 = bVar2 < 0x55;
    if (bVar2 == 0x55) {
      cVar3 = test_keyboard_to_host_transmission(0x2a);
      if (bVar5) goto set_A_0x14_return_ERR;
      if (cVar3 == '*') {
        bVar2 = counter__init_0;
        if (counter__init_0 == 0) goto LAB_ram_0cb1;
        goto set_A_0x12_return_ERR;
      }
    }
    bVar2 = 0x13;
  }
LAB_ram_0cb1:
  disableMaskableInterrupts();
  keyboard_communication_handler = _BYTE_ram_5e05;
  cVar3 = '\0';
  statusport2_flag = 0;
  init_host_serial_port(value_for_statusport_2);
  enableMaskableInterrupts();
  if (cVar3 == '\0') {
    return (char)((ushort)in_AF >> 8);
  }
  uVar1 = pop_and_return_with_carry(bVar2);
  return uVar1;
}



byte update_uart_txcontrol_check_DTR_RTS(void)

{
  byte in_io_00000060;
  
                    // Check StatusPort bit 1 = MOBAC => INV(/DTR1 AND /RTS1)
  return in_io_00000060 & 2;
}



void test_keyboard_to_host_transmission(void)

{
  undefined1 uVar1;
  
                    // TDV 2215 Keyboard to Host Transmission Test - Tests keyboard communication
                    // path to host. Validates character-by-character transmission through the
                    // serial interface using XON/XOFF flow control. Ensures keyboard input can
                    // reach the host via V.24, V.11, or current loop interfaces.
  uVar1 = configure_host_tx_register();
  sleep_BC_loops(uVar1,0x5b);
  keyboard_to_host_transmission();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte test_keyboard_communication(void)

{
  byte in_AF;
  char cVar2;
  byte uVar1;
  byte cVar1;
  bool bVar3;
  
  do {
    cVar1 = 0;
    counter__init_0 = 0;
    get_keyboard_char_atomic();
  } while (cVar1 == 0);
  _BYTE_ram_5e05 = keyboard_test_handler;
  keyboard_test_handler = 0xcf1;
  enableMaskableInterrupts();
  bVar3 = false;
  cVar2 = send_keyboard_command_and_wait();
  if ((!bVar3) && (cVar2 == -0x56)) {
    bVar3 = false;
    cVar2 = send_keyboard_command_and_wait();
    if ((!bVar3) && ((cVar2 == -0x56 && (bVar3 = false, counter__init_0 == 0)))) goto LAB_ram_0d55;
  }
  bVar3 = true;
LAB_ram_0d55:
  keyboard_test_handler = _BYTE_ram_5e05;
  if (bVar3) {
    uVar1 = pop_and_return_with_carry();
    return uVar1;
  }
  return (byte)((ushort)_in_AF >> 8);
}



void send_keyboard_command_and_wait(void)

{
                    // Send command to keyboard, wait for response
                    // Sequence: send command -> delay -> check response
                    // Used by keyboard communication test to verify handshake protocol
  write_to_keyboard_return_carry_if_busy();
  sleep_BC_loops();
  get_keyboard_char_atomic();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte configure_interrupt_handlers(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte bVar2;
  undefined2 in_DE;
  undefined2 in_HL;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (bVar1 < 3) {
    disableMaskableInterrupts();
    if (bVar1 == 0) {
      bVar2 = 1;
      _DAT_ram_5c00 = in_DE;
      _DAT_ram_5c02 = in_HL;
    }
    else if (bVar1 == 1) {
      bVar2 = 2;
      _DAT_ram_5c04 = in_DE;
      _DAT_ram_5c06 = in_HL;
    }
    else {
      bVar2 = 4;
      _DAT_ram_5c08 = in_DE;
      _DAT_ram_5c0a = in_HL;
    }
    if ((char)((ushort)in_DE >> 8) == '\0' && (char)in_DE == '\0') {
      timer_channel_flags = timer_channel_flags & bVar2;
    }
    else {
      timer_channel_flags = timer_channel_flags | bVar2;
    }
    enableMaskableInterrupts();
    return bVar1;
  }
  return bVar1;
}



undefined1 disable_cha_rx(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  channelA_last_Register_written = 3;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 update_chA_reg3_Rx_maybe(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  if (channelA_last_Register_written != 0) {
                    // Clear A and all some variables
                    // 
    channelA_last_Register_written = 0;
    keyboard_buffer_counter = 0;
    keyboard_tx_buffer_write_pointer = 0;
    keyboard_tx_buffer_read_pointer = 0;
                    // Update ChannelA, register 3 (RX)
    handle_keyboard_communication(chA_register3_value);
  }
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 serial_state_advance_to_busy(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  if (serial_transmission_state == 0) {
    serial_transmission_state = 2;
  }
  else if (serial_transmission_state == 1) {
    z80sio_channel_a_test_status = 1;
  }
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 serial_tx_complete_interrupt_handler(void)

{
  undefined2 in_AF;
  char cVar1;
  
  disableMaskableInterrupts();
                    // Handle serial transmission completion based on state: 0=IDLE,
                    // 1=READY_TO_SEND, 2=SENDING_DATA, 3=ERROR_OR_DISABLED
  if ((serial_transmission_state != 3) && (serial_transmission_state != 0)) {
    if (serial_transmission_state == 1) {
      z80sio_channel_a_test_status = 0;
    }
    else {
      z80sio_channel_a_test_status = 0;
      cVar1 = '\0';
      if ((transmission_buffer_empty_flag == 0) &&
         (dequeue_char_from_keyboard_buffer(), cVar1 != '\0')) {
        serial_transmission_state = 0;
      }
      else {
        transmission_buffer_empty_flag = 0;
        serial_transmission_state = 1;
      }
    }
  }
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 serial_transmission_error_handler(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  Last_Channel_A_Command = Last_Channel_A_Command | 0x10;
  serial_transmission_state = 3;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 serial_error_recovery_and_reset(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (serial_transmission_state == 3) {
    Last_Channel_A_Command = Last_Channel_A_Command & 0xef;
    BYTE_ram_5d53 = 0;
    z80sio_channel_b_test_status = 0;
    BYTE_ram_5d55 = 0;
    transmission_buffer_empty_flag = 0;
                    // Reset serial_transmission_state = 0 (IDLE) during error recovery
    serial_transmission_state = 0;
    handle_keyboard_communication();
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



byte configure_z80sio_channel_a_status(void)

{
  undefined2 in_AF;
  byte bVar1;
  
  disableMaskableInterrupts();
  bVar1 = (byte)((ushort)in_AF >> 8);
  Last_Channel_A_Command = Last_Channel_A_Command & 0x7d | bVar1 & 0x82;
  ContolPort1_value = ContolPort1_value & 0xdf | ~bVar1 & 0x20;
  enableMaskableInterrupts();
  return bVar1;
}



byte serial_start_transmission_request(void)

{
  undefined2 in_AF;
  byte bVar1;
  
  disableMaskableInterrupts();
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (serial_transmission_state == 3) {
    enableMaskableInterrupts();
    return bVar1;
  }
  if (serial_transmission_state == 1) {
    if (transmission_buffer_empty_flag == 0) {
      transmission_buffer_empty_flag = bVar1;
      enableMaskableInterrupts();
      return bVar1;
    }
    enableMaskableInterrupts();
    return bVar1;
  }
  serial_transmission_state = 1;
  enableMaskableInterrupts();
  return bVar1;
}



undefined1 serial_start_transmission_if_ready(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 uVar2;
  bool bVar3;
  
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
                    // CHECK: system_mode_state must be 2 for keyboard-to-serial transmission. If
                    // not 2, function fails and returns carry.
  bVar3 = system_mode_state == 0;
  uVar2 = uVar1;
  if (system_mode_state != 1) {
    if ((system_mode_state != 2) || (serial_transmission_state == 3)) goto LAB_ram_0f11;
    bVar3 = false;
    if (serial_transmission_state == 0) {
                    // Set serial_transmission_state = 1 (TRANSMITTING) before OUT(0x00)
      serial_transmission_state = 1;
    }
    else {
      enqueue_char_to_keyboard_buffer();
      if (bVar3) goto LAB_ram_0f17;
    }
    bVar3 = false;
    if (sw_push_key_programming == 0) goto LAB_ram_0f11;
  }
  write_to_keyboard_tx_buffer(uVar2);
  if (bVar3) {
LAB_ram_0f17:
    enableMaskableInterrupts();
    return uVar1;
  }
LAB_ram_0f11:
  enableMaskableInterrupts();
  return uVar1;
}



undefined1 configure_host_tx_register(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (serial_transmission_state == 3) {
    enableMaskableInterrupts();
    return uVar1;
  }
  if (serial_transmission_state == 0) {
    serial_transmission_state = 1;
    enableMaskableInterrupts();
    return uVar1;
  }
  uVar1 = enqueue_char_to_keyboard_buffer(uVar1);
  enableMaskableInterrupts();
  return uVar1;
}



// WARNING: Unknown calling convention -- yet parameter storage is locked

byte keyboard_to_host_transmission(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte bVar2;
  bool bVar3;
  
                    // TDV 2215 Keyboard to Host Transmission - Handles character-by-character
                    // transmission from keyboard to host. Supports asynchronous/isochronous
                    // transmission at 50-19200 baud using XON/XOFF flow control. Works with V.24
                    // (RS-232-C), V.11 (RS-422), or current loop interfaces.
  disableMaskableInterrupts();
  bVar3 = false;
  if (((sw_printer_mode != 0) && (bVar3 = false, keyboard_data_ready_flag != 0)) &&
     (bVar3 = keyboard_buffer_counter < 0x20, keyboard_buffer_counter == 0x20)) {
    bVar1 = read_keyboard_data_with_param(0x11);
    bVar2 = transmission_buffer_empty_flag;
    if (!bVar3) {
      bVar3 = false;
      if (transmission_buffer_empty_flag != 0) goto LAB_ram_0f84;
      if ((serial_transmission_state != 3) && (bVar2 = bVar1, serial_transmission_state == 0)) {
                    // Set serial_transmission_state = 1 (TRANSMITTING) after successful keyboard
                    // transmission
        serial_transmission_state = 1;
        bVar2 = transmission_buffer_empty_flag;
      }
    }
    transmission_buffer_empty_flag = bVar2;
    bVar3 = false;
    keyboard_data_ready_flag = 0;
  }
LAB_ram_0f84:
  bVar2 = read_from_keyboard_tx_buffer();
  if (bVar3) {
    enableMaskableInterrupts();
    return (byte)((ushort)in_AF >> 8);
  }
                    // Apply keyboard character transmission mask: Load mask from 0x5E1B and AND
                    // with character in B register. This filters characters before transmission to
                    // host computer. If mask is wrong (e.g. 0x00), characters get corrupted leading
                    // to communication failure and potential QT error during startup tests.
                    // AND character with transmission mask. This is where character filtering
                    // occurs - if keyboard_char_transmission_mask is wrong (e.g. 0x00), characters
                    // get corrupted. This can cause the Z80SIO keyboard test to fail with QT error
                    // during startup.
  enableMaskableInterrupts();
  return keyboard_char_transmission_mask & bVar2;
}



undefined1 send_data_to_printer(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  
                    // TDV 2215 Printer Data Transmission - Sends data to printer (local or remote).
                    // Part of the terminal's printer control feature which supports background
                    // printing with one-page print buffer. Manages printer data flow and
                    // communication protocols.
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (BYTE_ram_5d99 == 0) {
    BYTE_ram_5d99 = 1;
    enableMaskableInterrupts();
    return uVar1;
  }
  uVar1 = printer_buffer_store_wrapper(uVar1);
  enableMaskableInterrupts();
  return uVar1;
}



byte read_printer_buffer_with_mask(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 in_CY_flag;
  
  disableMaskableInterrupts();
  bVar1 = keyboard_buffer_operation_3();
  if (!(bool)in_CY_flag) {
                    // Apply printer character transmission mask: Load mask from 0x5E1C and AND with
                    // character in B register. Identical pattern to keyboard masking but for
                    // printer data transmission.
    enableMaskableInterrupts();
    return printer_char_transmission_mask & bVar1;
  }
  enableMaskableInterrupts();
  return (byte)((ushort)in_AF >> 8);
}



void store_character_to_buffer_atomic(void)

{
  undefined2 in_AF;
  
  disableMaskableInterrupts();
  if (BYTE_ram_5de0 == 0) {
    BYTE_ram_5de0 = 1;
  }
  store_to_buffer_or_nvram((char)((ushort)in_AF >> 8));
  enableMaskableInterrupts();
  return;
}



undefined1 read_circular_buffer_atomic(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 in_CY_flag;
  
  disableMaskableInterrupts();
  uVar1 = circular_buffer_dequeue();
  if (!(bool)in_CY_flag) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 check_control_port_bit0(void)

{
  byte in_io_00000060;
  undefined2 in_AF;
  undefined1 uVar1;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_io_00000060 & 1) != 0) {
    return uVar1;
  }
  return uVar1;
}



// WARNING: Unknown calling convention -- yet parameter storage is locked

byte get_keyboard_char_atomic(void)

{
  undefined2 in_AF;
  byte uVar1;
  bool in_CY_flag;
  
                    // ATOMIC KEYBOARD CHARACTER READ
                    // Disables interrupts, reads character from keyboard buffer
                    // Used by main character processing loop
  disableMaskableInterrupts();
  uVar1 = read_from_keyboard_input_buffer();
  if (!in_CY_flag) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return (byte)((ushort)in_AF >> 8);
}



byte write_to_keyboard_return_carry_if_busy(void)

{
  byte in_io_00000010;
  byte in_AF;
  byte uVar1;
  
  disableMaskableInterrupts();
                    // I/O Read from port 0x10: MC6850 Keyboard Status Register
                    // (ChipSelect.KeyboardAndPrinter, chip_reg=0). Status register bits:
                    // bit0=receive data full, bit1=transmit data empty, bit2=data carrier detect,
                    // bit3=clear to send, etc.
  uVar1 = (byte)((ushort)_in_AF >> 8);
  if ((in_io_00000010 & 2) != 0) {
                    // I/O Write to port 0x11: MC6850 Keyboard Transmit Data Register
                    // (ChipSelect.KeyboardAndPrinter, chip_reg=1). Sends command/data byte to
                    // keyboard through serial interface. Only writes if transmit buffer is empty
                    // (status bit 1=1).
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



byte check_display_attribute_port(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  byte bVar1;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (((bVar1 != 0) && (bVar1 < 9)) && (disableMaskableInterrupts(), (in_io_00000010 & 2) == 0)) {
    enableMaskableInterrupts();
    return bVar1;
  }
  enableMaskableInterrupts();
  return bVar1;
}



byte check_hardware_status_port_bit2(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  byte bVar1;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (((bVar1 == 0) || (bVar1 < 9)) && (disableMaskableInterrupts(), (in_io_00000010 & 2) == 0)) {
    enableMaskableInterrupts();
    return bVar1;
  }
  enableMaskableInterrupts();
  return bVar1;
}



byte validate_af_register_with_interrupt_check(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  byte bVar1;
  
  bVar1 = (byte)((ushort)in_AF >> 8);
  if (((bVar1 != 0) && (bVar1 < 9)) && (disableMaskableInterrupts(), (in_io_00000010 & 2) == 0)) {
    enableMaskableInterrupts();
    return bVar1;
  }
  enableMaskableInterrupts();
  return bVar1;
}



undefined1 check_interrupt_flag_and_validate_af(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  undefined1 uVar1;
  
                    // Checks keyboard/printer interface status and validates AF register. Reads
                    // from MC6850 Serial Chip status register (bit 2) for keyboard interface.
                    // Returns early if interrupt_disable_flag is set.
                    // I/O Read from port 0x10: MC6850 Keyboard Status Register.
                    // ChipSelect.KeyboardAndPrinter, chip_reg=0. Status bits: bit 2=transmit data
                    // register empty, used for interface validation during AF register operations.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (sw_echo != 0) {
    return uVar1;
  }
  disableMaskableInterrupts();
  if ((in_io_00000010 & 2) != 0) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



undefined1 wait_for_hardware_ready_bit2(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_io_00000010 & 2) != 0) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



undefined1 validate_af_with_interrupt_disable(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
                    // Validates AF register with keyboard/printer interface check. Reads MC6850
                    // Serial Chip status (bit 2) with interrupts disabled for atomic operation.
                    // Used for safe register validation during I/O operations.
                    // I/O Read from port 0x10: MC6850 Keyboard Status Register
                    // (ChipSelect.KeyboardAndPrinter). Reading status bit 2 with interrupts
                    // disabled for atomic validation of keyboard interface during register
                    // operations.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_io_00000010 & 2) != 0) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



undefined1 check_hardware_ready_bit2(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_io_00000010 & 2) != 0) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



undefined1 validate_af_with_interrupt_disable_alt(void)

{
  byte in_io_00000010;
  undefined2 in_AF;
  undefined1 uVar1;
  
  disableMaskableInterrupts();
                    // Alternative AF register validation with keyboard/printer interface status
                    // check. Similar to validate_af_with_interrupt_disable but different call
                    // pattern. Ensures safe register operations during serial I/O.
                    // I/O Read from port 0x10: MC6850 Keyboard Status Register. Alternative atomic
                    // read of keyboard interface status (bit 2) for AF register validation with
                    // interrupt protection.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_io_00000010 & 2) != 0) {
    enableMaskableInterrupts();
    return uVar1;
  }
  enableMaskableInterrupts();
  return uVar1;
}



byte store_byte_in_circular_buffer(void)

{
  byte uVar1;
  byte bVar1;
  byte in_DE;
  byte *in_HL;
  byte *pbVar2;
  byte in_stack_00000000;
  
                    // CORE CIRCULAR BUFFER STORAGE FUNCTION
                    // Character in A register comes from UART read at 0x045d
                    // Manages circular buffer with proper pointer wrap-around
                    // Buffer control: (HL), Buffer data: (DE)+offset
  uVar1 = (byte)((ushort)_in_stack_00000000 >> 8);
  if (*in_HL != 0x20) {
    *in_HL = *in_HL + 1;
    pbVar2 = in_HL + 1;
    bVar1 = *pbVar2;
    if (bVar1 == 0x1f) {
      *pbVar2 = 0xff;
    }
    *pbVar2 = *pbVar2 + 1;
                    // STORE CHARACTER: A register contains character from UART read at 0x045d
                    // POP AF restores the character read from UART
                    // Character is properly stored in circular buffer at calculated address
    *(byte *)((ushort)bVar1 + _in_DE) = uVar1;
    return uVar1;
  }
                    // SHARED BUFFER FULL EXIT HANDLER: Common cleanup code for multiple buffer
                    // functions. When any buffer is full (keyboard TX buffer, circular buffer,
                    // etc.), execution jumps here to clean up stack (POP AF) and return without
                    // storing data. Prevents buffer overflow by discarding new data when buffer
                    // capacity exceeded. Used by: store_byte_in_circular_buffer,
                    // write_to_keyboard_tx_buffer, and other buffer management functions.
  return uVar1;
}



byte read_char_from_circular_buffer(void)

{
  byte bVar1;
  byte in_DE;
  byte *in_HL;
  byte *pbVar2;
  
                    // CORE BUFFER READ FUNCTION - Reads next character from circular buffer
                    // Entry: HL=buffer_control_addr (0x5dbd), DE=buffer_data_addr (0x5d9d)
                    // Returns: Character in A register, Carry clear if successful
                    // CRITICAL: Buffer counter at (HL) must be > 0 or function returns immediately
                    // TEST BUFFER EMPTY: If buffer counter == 0, return immediately without reading
                    // DEBUG ISSUE: This test always passes (buffer appears empty) preventing reads
  if (*in_HL != 0) {
                    // DECREMENT BUFFER COUNTER: Reduce count of characters in buffer
    *in_HL = *in_HL - 1;
    pbVar2 = in_HL + 2;
                    // LOAD READ POINTER: Get current read position in circular buffer
    bVar1 = *pbVar2;
                    // CHECK READ POINTER WRAP: If pointer == 0x1f, need to wrap around
    if (bVar1 == 0x1f) {
      *pbVar2 = 0xff;
    }
                    // INCREMENT READ POINTER: Advance to next position with wrap-around
    *pbVar2 = *pbVar2 + 1;
                    // READ CHARACTER FROM BUFFER: Load actual character data from buffer
                    // CRITICAL DEBUG POINT: This instruction is never reached (buffer always empty)
    return *(byte *)((ushort)bVar1 + _in_DE);
  }
  return *in_HL;
}



void manage_keyboard_input_buffer(void)

{
                    // KEYBOARD INPUT BUFFER MANAGEMENT
                    // Sets up buffer addresses: HL=0x5dbd (control), DE=0x5d9d (data)
                    // UART data is properly read at 0x045d using IN instruction
                    // Calls core buffer storage function
  store_byte_in_circular_buffer();
  return;
}



void read_from_keyboard_input_buffer(void)

{
                    // KEYBOARD INPUT BUFFER READ WRAPPER
                    // Sets up buffer addresses: HL=0x5dbd (control), DE=0x5d9d (data)
                    // Calls core buffer read function
  read_char_from_circular_buffer();
  return;
}



void enqueue_char_to_keyboard_buffer(void)

{
  store_byte_in_circular_buffer();
  return;
}



void dequeue_char_from_keyboard_buffer(void)

{
  read_char_from_circular_buffer();
  return;
}



void printer_buffer_store_wrapper(void)

{
  store_byte_in_circular_buffer();
  return;
}



void poll_keyboard_buffer_with_carry_check(void)

{
  read_char_from_circular_buffer();
  return;
}



undefined1 write_to_keyboard_tx_buffer(void)

{
  ushort uVar1;
  undefined2 in_AF;
  undefined1 uVar2;
  
  uVar2 = (undefined1)((ushort)in_AF >> 8);
  if (keyboard_buffer_counter != 0xff) {
    keyboard_buffer_counter = keyboard_buffer_counter + 1;
    uVar1 = (ushort)keyboard_tx_buffer_write_pointer;
    if (keyboard_tx_buffer_write_pointer == 0xff) {
      keyboard_tx_buffer_write_pointer = 0xff;
    }
    keyboard_tx_buffer_write_pointer = keyboard_tx_buffer_write_pointer + 1;
    (&keyboard_tx_buffer_start)[uVar1] = uVar2;
    return uVar2;
  }
  return uVar2;
}



byte read_from_keyboard_tx_buffer(void)

{
  byte bVar1;
  
                    // Read From Keyboard TX Buffer
                    // Extracts next character from TX buffer for serial transmission
                    // Manages FIFO buffer with read pointer and counter
                    // HL = buffer counter (0x5d2e), DE = buffer start (0x5c2e)
                    // Returns character in A, updates read pointer and counter
  bVar1 = keyboard_tx_buffer_read_pointer;
  if (keyboard_buffer_counter != 0) {
    keyboard_buffer_counter = keyboard_buffer_counter - 1;
    if (keyboard_tx_buffer_read_pointer == 0xff) {
      keyboard_tx_buffer_read_pointer = 0xff;
    }
    keyboard_tx_buffer_read_pointer = keyboard_tx_buffer_read_pointer + 1;
    return (&keyboard_tx_buffer_start)[bVar1];
  }
  return keyboard_buffer_counter;
}



void buffer_store_wrapper_alt(void)

{
  store_byte_in_circular_buffer();
  return;
}



void keyboard_buffer_operation_3(void)

{
  read_char_from_circular_buffer();
  return;
}



byte circular_buffer_enqueue(void)

{
  ushort uVar1;
  undefined2 in_AF;
  byte bVar2;
  
  bVar2 = (byte)((ushort)in_AF >> 8);
  if (circular_buffer_count != 8) {
    circular_buffer_count = circular_buffer_count + 1;
    uVar1 = (ushort)circular_buffer_write_index;
    if (circular_buffer_write_index == 7) {
      circular_buffer_write_index = 0xff;
    }
    circular_buffer_write_index = circular_buffer_write_index + 1;
    (&circular_buffer_data)[uVar1] = bVar2;
    return bVar2;
  }
  return bVar2;
}



byte circular_buffer_dequeue(void)

{
  byte bVar1;
  
  bVar1 = circular_buffer_read_index;
  if (circular_buffer_count != 0) {
    circular_buffer_count = circular_buffer_count - 1;
    if (circular_buffer_read_index == 7) {
      circular_buffer_read_index = 0xff;
    }
    circular_buffer_read_index = circular_buffer_read_index + 1;
    return (&circular_buffer_data)[bVar1];
  }
  return circular_buffer_count;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 store_to_buffer_or_nvram(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  short sVar2;
  char cVar3;
  
  cVar3 = nvram_reset_flag == 0;
  if ((bool)cVar3) {
    uVar1 = store_byte_in_circular_buffer();
    return uVar1;
  }
  sVar2 = _BYTE_ram_5def;
  compare_de_hl_registers();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (!(bool)cVar3) {
    _BYTE_ram_5def = sVar2 + 1;
    compare_de_hl_registers();
    if (cVar3 != '\0') {
      _BYTE_ram_5df1 = -1;
    }
    _BYTE_ram_5df1 = _BYTE_ram_5df1 + 1;
    *(undefined1 *)(_nvram_reset_address + _BYTE_ram_5df1) = uVar1;
    return uVar1;
  }
  return uVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 read_from_buffer_or_nvram(void)

{
  bool bVar1;
  bool bVar2;
  undefined1 uVar3;
  char cVar4;
  
  if (nvram_reset_flag == 0) {
    uVar3 = read_char_from_circular_buffer();
    return uVar3;
  }
  bVar1 = (char)_BYTE_ram_5def == '\0';
  bVar2 = (char)((ushort)_BYTE_ram_5def >> 8) == '\0';
  cVar4 = bVar1 && bVar2;
  if (!bVar1 || !bVar2) {
    _BYTE_ram_5def = _BYTE_ram_5def + -1;
    compare_de_hl_registers();
    if (cVar4 != '\0') {
      _BYTE_ram_5df3 = -1;
    }
    _BYTE_ram_5df3 = _BYTE_ram_5df3 + 1;
    return *(undefined1 *)(_nvram_reset_address + _BYTE_ram_5df3);
  }
  return 0;
}



byte read_from_nvram(void)

{
  undefined2 in_AF;
  byte *in_HL;
  undefined1 in_CY_flag;
  
                    // ER3400 NVRAM Read Function - Reads 4-bit nibbles from ER3400 EAROM
                    // A10=0: Normal read mode, /S1=1 (C1=0,C0=1) - S1=0 from read cycle, inverted
                    // to /S1=1
                    // Combines two 4-bit reads: lower nibble + (upper nibble from +0x200) to form
                    // 8-bit value
                    // ER3400 stores 1024x4 bits, accessed via 10-bit addressing (A0-A9)
  map_to_nvram_address();
  if (!(bool)in_CY_flag) {
    disableMaskableInterrupts();
    enableMaskableInterrupts();
    return *(char *)CONCAT11((char)((ushort)in_HL >> 8) + '\x02',(char)in_HL) << 4 | *in_HL & 0xf;
  }
  return (byte)((ushort)in_AF >> 8);
}



byte write_byte_to_nvram(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte bVar2;
  byte bVar3;
  byte bVar4;
  undefined1 in_CY_flag;
  bool bVar5;
  undefined1 uVar6;
  
                    // ER3400 Byte Write Function - Writes 8-bit value as two 4-bit nibbles
                    // Splits byte into: lower nibble (bits 0-3) and upper nibble (bits 4-7)
                    // Two separate ER3400 write operations: addr and addr+0x200 for nibble pair
                    // ER3400 Control: A10=0, /S1=0 (write mode: C1=0,C0=0)
  bVar2 = (byte)((ushort)in_AF >> 8);
  bVar4 = bVar2;
  map_to_nvram_address();
  if (!(bool)in_CY_flag) {
    BYTE_ram_5e20 = 1;
    bVar3 = bVar4 >> 4;
    bVar5 = false;
    uVar1 = write_to_nvram(bVar4 & 0xf);
    if (!bVar5) {
      uVar1 = write_to_nvram(bVar3);
    }
    uVar6 = BYTE_ram_5e20 == 0;
    bVar5 = BYTE_ram_5e20 != 1;
    BYTE_ram_5e20 = 0;
    if (bVar5) {
      power_fail_interrupt_handler();
    }
    if (!(bool)uVar6) {
      bVar2 = return_stack_value_high_byte(uVar1);
      return bVar2;
    }
  }
  return bVar2;
}



byte map_to_nvram_address(void)

{
  byte in_H;
  
                    // ER3400 Address Mapping Function
                    // Maps CPU addresses to ER3400 NVRAM space:
                    // - If H<2: Maps to 0x60xx range (H+0x60)  
                    // - A10 bit controls ER3400 mode: A10=0(read/write),
                    // A10=1(erase/accept_address)
                    // - /S1 signal from R/W controls C0: /S1=1(read), /S1=0(write)
  if (in_H < 2) {
    return in_H + 0x60;
  }
  return in_H;
}



byte write_to_nvram(byte param_1)

{
  byte bVar1;
  byte *in_HL;
  
                    // ER3400 NVRAM Write Function - Controls ER3400 via A10 and /S1 signals
                    // A10=0 (addresses <0x400): Normal read/write mode
                    // /S1=0: Write mode (C1=0,C0=0) - S1=1 from R/W signal, inverted to /S1=0
                    // NVSEL (ControlPort1 bit 2) must be enabled for ER3400 to respond
  disableMaskableInterrupts();
                    // I/O Write to port 0x50: ChipSelect.ControlPort1. Setting NVSEL bit (bit 2) to
                    // enable ER3400 4096-bit NVRAM (U43) for write operation. ControlPort1 controls
                    // memory mapping and NVRAM access.
  bVar1 = *in_HL & 0xf;
  if (bVar1 != param_1) {
                    // ER3400 ERASE SEQUENCE: INR H four times adds 0x400 to address
                    // This sets A10=1, putting ER3400 in ACCEPT ADDRESS mode (C1=1,C0=1 when
                    // reading)
                    // Then ERASE mode (C1=1,C0=0 when writing) - This implements ER3400 erase
                    // before write!
                    // ER3400 ERASE OPERATION: Write to address+0x400 (A10=1)
                    // This triggers ERASE mode (C1=1,C0=0) on the ER3400 at the accepted address
                    // Erases the 4-bit location before writing new data
    *(byte *)CONCAT11((char)((ushort)in_HL >> 8) + '\x04',(char)in_HL) = bVar1;
    sleep_BC_loops(bVar1,5);
    bVar1 = *in_HL;
                    // The actual write to NVRAM happens here
    *in_HL = param_1;
    sleep_BC_loops(bVar1,0);
  }
                    // I/O Write to port 0x50: ChipSelect.ControlPort1. Restoring original
                    // ControlPort1 configuration after NVRAM write operation, clearing NVSEL bit to
                    // disable ER3400 NVRAM access.
  enableMaskableInterrupts();
  return ContolPort1_value;
}



void load_config_from_nvram(void)

{
                    // TDV 2215 NVRAM Configuration Loader - Loads permanent soft-switches from
                    // non-volatile memory into temporary RAM copies. Handles three switch
                    // categories: Convenience (4.1), Function (4.2), and Communication (4.3).
                    // Default values used on first boot or after factory reset. Permanent switches
                    // survive power-off, temporary switches are used for actual operation.
                    // Set output buffer to 0x5f00 - Terminal configuration data area
  load_config_from_nvram_template();
  popall_and_ret();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void load_config_from_nvram_template(void)

{
  byte bVar1;
  byte bVar2;
  byte bVar3;
  byte bVar4;
  byte *in_DE;
  char cVar5;
  char cVar6;
  bool bVar7;
  byte *pbStack_4;
  byte bStack_1;
  
                    // /*
                    //  * TDV 2215 NVRAM Configuration Template Processor
                    //  * 
                    //  * NVRAM SOFT-SWITCH STORAGE LAYOUT (11 bytes):
                    //  * NVRAM 0x50: Configuration byte 0 (Convenience switches 1-3)
                    //  * NVRAM 0x51: Configuration byte 1 (Convenience switches 4-6) 
                    //  * NVRAM 0x52: Configuration byte 2 (Function switches 1-3)
                    //  * NVRAM 0x53: Configuration byte 3 (Function switches 4-6)
                    //  * NVRAM 0x54: Configuration byte 4 (Function switches 7-9)
                    //  * NVRAM 0x55: Configuration byte 5 (Function switches 10-12)
                    //  * NVRAM 0x56: Configuration byte 6 (Function switches 13-15)
                    //  * NVRAM 0x57: Configuration byte 7 (Communication switches 1-3)
                    //  * NVRAM 0x58: Configuration byte 8 (Communication switches 4-6)
                    //  * NVRAM 0x59: Configuration byte 9 (Communication switches 7-9)
                    //  * NVRAM 0x5A: Configuration byte 10 (Communication switches 10-16)
                    //  * 
                    //  * Process: Reads template from 0x0AA0, extracts bit fields from NVRAM
                    // 0x50-0x5A,
                    //  * builds configuration structure at 0x5F00-0x5F24 (37 individual switch
                    // values)
                    //  */
                    // Initialize config parameter counter to -1 (will be incremented to 0)
  bStack_1 = 0xff;
  pbStack_4 = _config_template_pointer;
  do {
    bVar4 = *pbStack_4;
    pbStack_4 = pbStack_4 + 1;
    bStack_1 = bStack_1 + 1;
                    // Process maximum 11 configuration parameters (0-10)
    if (10 < bStack_1) {
      return;
    }
                    // Read NVRAM byte at current parameter address (A = parameter index 0-10)
                    // This reads from NVRAM address 0x50+A to get raw configuration data
    bVar1 = read_from_nvram();
                    // Initialize bit field extraction loop: 8 bits to process per byte
    cVar5 = '\b';
                    // Rotate template byte left to get next bit pattern
                    // Template defines which bits to extract from NVRAM data
    bVar4 = bVar4 << 1 | bVar4 >> 7;
    do {
      bVar2 = 1;
      do {
        bVar7 = (bool)(bVar4 >> 7);
        bVar4 = bVar4 << 1 | bVar7;
        bVar3 = bVar1;
        cVar6 = cVar5;
                    // If template bit is 1, extract bit field from NVRAM data
        if (bVar7) break;
                    // Calculate bit mask: L = (L*2)+1, creates mask like 1,3,7,15,31,63,127,255
                    // This determines how many consecutive bits to extract
        bVar2 = bVar2 * '\x02' + 1;
        cVar5 = cVar5 + -1;
        cVar6 = cVar5;
      } while (cVar5 != '\0');
      while (cVar5 = cVar5 + -1, cVar5 != '\0') {
                    // Rotate NVRAM data right to align bit field for extraction
                    // Positions the desired bits in the correct location
        bVar3 = bVar3 >> 1 | bVar3 << 7;
      }
                    // Extract bit field: NVRAM_data AND mask
                    // Store extracted configuration value to output buffer
      *in_DE = bVar3 & bVar2;
      in_DE = in_DE + 1;
      cVar5 = cVar6 + -1;
    } while (cVar5 != '\0');
  } while( true );
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 save_config_to_nvram(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte bVar2;
  undefined1 uVar3;
  byte bVar4;
  char cVar5;
  char cVar6;
  byte *pbVar7;
  byte *pbVar8;
  byte bVar9;
  
                    // TDV 2215 NVRAM Configuration Saver - Transfers temporary soft-switches to
                    // permanent storage in non-volatile memory. Initiated via Configuration Exit
                    // menu (5.2) when user selects "Make Switches Permanent". Preserves switch
                    // settings across power cycles. Also saves tabulation rack and PUSH-key
                    // sequences.
  pbVar7 = &sw_cursor_type;
  bVar4 = 0;
  pbVar8 = _config_template_pointer;
  do {
    bVar2 = 0;
    cVar5 = '\t';
    bVar9 = *pbVar8 >> 7;
    bVar1 = *pbVar8 << 1 | bVar9;
    do {
      do {
        cVar5 = cVar5 + -1;
        if (cVar5 == '\0') goto LAB_ram_132b;
        bVar9 = -((char)bVar1 >> 7);
        bVar1 = bVar1 << 1 | bVar9;
      } while (bVar9 == 0);
      bVar9 = *pbVar7;
      pbVar7 = pbVar7 + 1;
      cVar6 = cVar5;
      while (cVar6 = cVar6 + -1, cVar6 != '\0') {
        bVar9 = bVar9 << 1 | bVar9 >> 7;
      }
      bVar2 = bVar9 | bVar2;
      bVar9 = 0;
    } while (cVar5 != '\0');
LAB_ram_132b:
                    // Write packed configuration byte to NVRAM at current parameter address
                    // This calls the actual NVRAM write function with ER3400 erase-before-write
    write_byte_to_nvram(bVar2);
    if ((bool)bVar9) {
      return (char)((ushort)in_AF >> 8);
    }
    pbVar8 = pbVar8 + 1;
    bVar4 = bVar4 + 1;
    if (10 < bVar4) {
      uVar3 = return_stack_value_high_byte();
      return uVar3;
    }
  } while( true );
}



void setup_cursor_position(void)

{
                    // TDV 2215 Cursor Position Setup - Implements cursor positioning controlled by
                    // wrap switches: BOL (4.2.6) for beginning-of-line behavior (STOP/WRAP), EOL
                    // (4.2.7) for end-of-line behavior (STOP/WRAP), and RPM (4.2.8) for roll/page
                    // mode (ROLL/PAGE). Supports direct cursor addressing via DLE and CSI
                    // sequences.
  update_cursor_addresses();
  return;
}



undefined1 reset_cursor_to_line_start(void)

{
  undefined2 in_AF;
  
  current_cursor_character_address = 0;
  next_cursor_character_address = 1;
  return (char)((ushort)in_AF >> 8);
}



undefined1 advance_cursor_with_boundary_check(void)

{
  undefined1 uVar1;
  char in_CY_flag;
  
  uVar1 = advance_cursor_one_character();
  if ((bool)in_CY_flag) {
    return uVar1;
  }
  compare_de_hl_registers();
  if (in_CY_flag != '\0') {
    return uVar1;
  }
  return uVar1;
}



undefined1 increase_cursor_with_boundary_check(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  char in_CY_flag;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  increase_cursor_character();
  if ((bool)in_CY_flag) {
    return uVar1;
  }
  compare_de_hl_registers();
  if (in_CY_flag != '\0') {
    return uVar1;
  }
  return uVar1;
}



undefined1 advance_cursor_one_character(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  
                    // ADVANCE CURSOR CHARACTER: Advances cursor one character position with
                    // boundary checking
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (next_cursor_character_address == 0x50) {
    return uVar1;
  }
  current_cursor_character_address = next_cursor_character_address;
  next_cursor_character_address = next_cursor_character_address + 1;
  return uVar1;
}



void increase_cursor_character(void)

{
  if (next_cursor_character_address != 0x50) {
    current_cursor_character_address = next_cursor_character_address;
    next_cursor_character_address = next_cursor_character_address + 1;
    return;
  }
  if (current_cursor_row != 0x19) {
    update_cursor_addresses();
    return;
  }
  return;
}



undefined1 move_cursor_back_with_boundary_check(void)

{
  undefined1 uVar1;
  undefined1 in_CY_flag;
  
  uVar1 = move_cursor_back_one_position();
  if ((bool)in_CY_flag) {
    return uVar1;
  }
  compare_de_hl_registers();
  if (!(bool)in_CY_flag) {
    return uVar1;
  }
  return uVar1;
}



undefined1 cursor_back_with_boundary_check_alt(void)

{
  undefined1 uVar1;
  undefined1 in_CY_flag;
  
  uVar1 = move_cursor_back_one_position_safe();
  if ((bool)in_CY_flag) {
    return uVar1;
  }
  compare_de_hl_registers();
  if (!(bool)in_CY_flag) {
    return uVar1;
  }
  return uVar1;
}



undefined1 move_cursor_back_one_position(void)

{
  byte bVar1;
  undefined2 in_AF;
  undefined1 uVar2;
  
  bVar1 = next_cursor_character_address;
  uVar2 = (undefined1)((ushort)in_AF >> 8);
  if (next_cursor_character_address == 1) {
    return uVar2;
  }
  next_cursor_character_address = next_cursor_character_address - 1;
  current_cursor_character_address = bVar1 - 2;
  return uVar2;
}



undefined1 move_cursor_back_one_position_safe(void)

{
  byte bVar1;
  undefined2 in_AF;
  undefined1 uVar2;
  
  bVar1 = next_cursor_character_address;
  uVar2 = (undefined1)((ushort)in_AF >> 8);
  if (next_cursor_character_address != 1) {
    next_cursor_character_address = next_cursor_character_address - 1;
    current_cursor_character_address = bVar1 - 2;
    return uVar2;
  }
  if (current_cursor_row != 1) {
    update_cursor_addresses(current_cursor_row - 1);
    return uVar2;
  }
  return uVar2;
}



void update_cursor_memory_wrapper(void)

{
  update_cursor_memory_addresses();
  return;
}



undefined1 conditional_cursor_update_up(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte in_H;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if ((in_H <= current_cursor_row) && (current_cursor_row != in_H)) {
    uVar1 = update_cursor_memory_wrapper(uVar1);
    return uVar1;
  }
  return uVar1;
}



undefined1 cursor_update_with_register_compare(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 in_CY_flag;
  
  compare_de_hl_registers();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (!(bool)in_CY_flag) {
    uVar1 = update_cursor_memory_wrapper(uVar1);
    return uVar1;
  }
  return uVar1;
}



void update_cursor_memory_wrapper_alt(void)

{
  update_cursor_memory_addresses();
  return;
}



undefined1 conditional_cursor_update_down(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte in_H;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (current_cursor_row < in_H) {
    uVar1 = update_cursor_memory_wrapper_alt(uVar1);
    return uVar1;
  }
  return uVar1;
}



undefined1 cursor_update_with_carry_check(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  char in_CY_flag;
  
  compare_de_hl_registers();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (in_CY_flag != '\0') {
    uVar1 = update_cursor_memory_wrapper_alt(uVar1);
    return uVar1;
  }
  return uVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 update_cursor_memory_addresses(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined2 in_HL;
  undefined1 in_CY_flag;
  
  validate_display_memory_bounds();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (!(bool)in_CY_flag) {
    _next_cursor_character_address = in_HL;
    return_register_a();
    _current_cursor_character_address = in_HL;
    return uVar1;
  }
  return uVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 update_cursor_addresses(void)

{
  undefined2 in_AF;
  undefined2 in_HL;
  
  _next_cursor_character_address = in_HL;
  return_register_a();
  _current_cursor_character_address = in_HL;
  return (char)((ushort)in_AF >> 8);
}



void no_operation_placeholder(void)

{
  return;
}



undefined1 clear_status_port_bit3(void)

{
  undefined2 in_AF;
  
                    // Disables Video Off control (VOF) signal. Clears bit 3 of ControlPort2 which
                    // controls the VOF signal to the Attribute Generator. VOF=0 enables video
                    // display output.
  status_port_bit3_control_flag = 0;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 & 0xf7;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 set_status_port_bit3(void)

{
  undefined2 in_AF;
  
                    // Enables Video Off control (VOF) signal. Sets bit 3 of ControlPort2 which
                    // controls the VOF signal to the Attribute Generator. VOF=1 disables video
                    // display output (blank screen).
  status_port_bit3_control_flag = 1;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 | 8;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 enable_vsync_interrupt(void)

{
  undefined2 in_AF;
  
                    // Enables Cursor Enable (CEN) signal. Sets bit 4 of ControlPort2 which controls
                    // the CEN signal to the Attribute Generator. CEN=1 enables cursor display.
  video_off_control_flag = 0;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 | 0x10;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



undefined1 set_vsync_flag_and_clear_status_bit4(void)

{
  undefined2 in_AF;
  
  video_off_control_flag = 1;
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 & 0xef;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



void wait_display_refresh_cycles(void)

{
  byte counter;
  
  do {
    counter = wait_for_VSYNC();
  } while (counter != 0x13);
  cursor_row_position = cursor_row_position + 1;
  if (0x18 < cursor_row_position) {
    cursor_row_position = 0;
  }
  update_cursor_addresses();
  return;
}



undefined1 cursor_down_with_wraparound(void)

{
  undefined2 in_AF;
  
  wait_for_VSYNC();
  cursor_row_position = cursor_row_position + 1;
  if (0x18 < cursor_row_position) {
    cursor_row_position = 0;
  }
  update_cursor_addresses();
  return (char)((ushort)in_AF >> 8);
}



undefined1 wait_vsync_and_update_cursor_row(void)

{
  undefined2 in_AF;
  char cVar1;
  
  do {
    cVar1 = wait_for_VSYNC();
  } while (cVar1 != '-');
  wait_for_VSYNC();
  cursor_row_position = cursor_row_position - 1;
  if (cursor_row_position == 0xff) {
    cursor_row_position = 0x18;
  }
  update_cursor_addresses();
  return (char)((ushort)in_AF >> 8);
}



undefined1 cursor_up_with_wraparound(void)

{
  undefined2 in_AF;
  
  wait_for_VSYNC();
  cursor_row_position = cursor_row_position - 1;
  if (cursor_row_position == 0xff) {
    cursor_row_position = 0x18;
  }
  update_cursor_addresses();
  return (char)((ushort)in_AF >> 8);
}



undefined1 cursor_down_and_clear_line(void)

{
  undefined2 in_AF;
  
  cursor_down_with_wraparound();
  display_memory_block_copy_with_timing(2);
  return (char)((ushort)in_AF >> 8);
}



undefined1 conditional_line_feed_operation(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  char cVar2;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (sw_printer_mode == 0) {
    cVar2 = keyboard_buffer_counter < 0xc4;
    if (!(bool)cVar2) {
      clear_display_line();
    }
    if (cVar2 == '\0') {
      cursor_row_position = cursor_row_position + 1;
      if (0x18 < cursor_row_position) {
        cursor_row_position = 0;
      }
      update_cursor_addresses();
      return uVar1;
    }
  }
  cursor_down_with_wraparound();
  clear_display_line();
  return uVar1;
}



undefined1 clear_line_and_cursor_up(void)

{
  undefined2 in_AF;
  
  display_memory_block_copy_with_timing(2);
  cursor_up_with_wraparound();
  return (char)((ushort)in_AF >> 8);
}



undefined1 clear_current_line_and_move_up(void)

{
  undefined2 in_AF;
  
  clear_display_line();
  cursor_up_with_wraparound();
  return (char)((ushort)in_AF >> 8);
}



undefined1 wait_refresh_and_clear_line(void)

{
  undefined2 in_AF;
  
  wait_display_refresh_cycles();
  display_memory_block_copy_with_timing(2);
  return (char)((ushort)in_AF >> 8);
}



undefined1 wait_refresh_cycles_and_clear(void)

{
  undefined2 in_AF;
  
  wait_display_refresh_cycles();
  clear_display_line();
  return (char)((ushort)in_AF >> 8);
}



undefined1 clear_line_and_wait_vsync(void)

{
  undefined2 in_AF;
  
  display_memory_block_copy_with_timing(2);
  wait_vsync_and_update_cursor_row();
  return (char)((ushort)in_AF >> 8);
}



undefined1 clear_line_and_wait_vsync_alt(void)

{
  undefined2 in_AF;
  
  clear_display_line();
  wait_vsync_and_update_cursor_row();
  return (char)((ushort)in_AF >> 8);
}



byte set_display_attribute_and_trigger_refresh(void)

{
  undefined2 in_AF;
  
  AttributeRegisterValue = (byte)((ushort)in_AF >> 8);
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 | 1;
  enableMaskableInterrupts();
  return AttributeRegisterValue;
}



undefined1 clear_status_port_bit0(void)

{
  undefined2 in_AF;
  
                    // Disables Attribute Write (ATRW) control. Clears bit 0 of ControlPort2 which
                    // disables writing attribute codes into the Display RAM (2KB SRAM U52 at
                    // 0x6000).
  disableMaskableInterrupts();
  value_for_statusport_2 = value_for_statusport_2 & 0xfe;
  enableMaskableInterrupts();
  return (char)((ushort)in_AF >> 8);
}



byte read_input_port_low_nibble_with_return(void)

{
  byte in_io_00000070;
  byte bVar1;
  
                    // Reads Status Port low nibble (bits 0-3) after waiting for display timing.
                    // Status Port bits: 0=RXPR1(printer ready), 1=MOBAC(modem control),
                    // 2=/CAR(carrier detect), 3=/CI(call indicator).
  return_register_a();
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
                    // I/O Read from port 0x70: Status Port (ChipSelect.ControlPort2 read). Reading
                    // 74LS240 U82 status register. Bits 0-3: RXPR1(printer ready from E2-V.11),
                    // MOBAC(Z80SIO2 RTS&DTR), /CAR(carrier detect from SN75189AN), /CI(call
                    // indicator from V.24)
  return in_io_00000070 & 0xf;
}



byte read_input_port_low_nibble(void)

{
  byte in_io_00000070;
  byte bVar1;
  
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
  return in_io_00000070 & 0xf;
}



undefined1 write_display_data_wait_ready(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte *in_HL;
  
  return_register_a();
  bVar1 = set_display_status_flag_alt();
  do {
    *in_HL = bVar1;
    bVar1 = readInterruptMask();
    bVar1 = bVar1 & 0x80;
  } while (bVar1 != 0);
  write_AttributeRegister_And_ControlPort2();
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 write_char_to_display_with_timing(void)

{
  undefined2 in_AF;
  byte bVar1;
  byte *pbVar2;
  
  pbVar2 = _current_cursor_character_address;
  bVar1 = set_display_status_flag_alt();
  do {
    *pbVar2 = bVar1;
    bVar1 = readInterruptMask();
    bVar1 = bVar1 & 0x80;
  } while (bVar1 != 0);
  write_AttributeRegister_And_ControlPort2();
  return (char)((ushort)in_AF >> 8);
}



void fill_display_region_with_pattern(void)

{
  byte bVar1;
  undefined2 uVar2;
  byte *in_DE;
  char cVar3;
  
  return_af_register_high_byte();
  return_af_register_high_byte();
  bVar1 = set_display_status_flag_alt();
  uVar2 = 0x8000;
  do {
    do {
      *in_DE = bVar1;
      bVar1 = readInterruptMask();
      bVar1 = bVar1 & (byte)((ushort)uVar2 >> 8);
      cVar3 = bVar1 == 0;
    } while (!(bool)cVar3);
    check_display_memory_boundary_markers();
    bVar1 = compare_de_hl_registers();
  } while (cVar3 == '\0');
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 write_pattern_to_display_area_and_update_attributes(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 uVar2;
  undefined2 uVar3;
  byte *pbVar4;
  char cVar5;
  undefined1 in_CY_flag;
  
  validate_display_region_with_bounds_check();
  if (!(bool)in_CY_flag) {
    return_af_register_high_byte();
    pbVar4 = _current_cursor_character_address;
    bVar1 = set_display_status_flag_alt();
    uVar3 = 0x8000;
    do {
      do {
        *pbVar4 = bVar1;
        bVar1 = readInterruptMask();
        bVar1 = bVar1 & (byte)((ushort)uVar3 >> 8);
        cVar5 = bVar1 == 0;
      } while (!(bool)cVar5);
      check_display_memory_boundary_markers();
      bVar1 = compare_de_hl_registers();
    } while (cVar5 == '\0');
    write_AttributeRegister_And_ControlPort2();
    uVar2 = return_stack_value_high_byte();
    return uVar2;
  }
  return (char)((ushort)in_AF >> 8);
}



undefined1 wait_hardware_ready_and_read_memory(void)

{
  byte bVar1;
  undefined1 *in_HL;
  
  return_register_a();
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
  return *in_HL;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 read_char_from_display_with_timing(void)

{
  byte bVar1;
  
  do {
    bVar1 = readInterruptMask();
  } while ((bVar1 & 0x80) != 0);
  return *_current_cursor_character_address;
}



undefined1 write_char_to_video_memory_and_refresh(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte bVar2;
  undefined1 *in_HL;
  
  uVar1 = return_register_a();
  do {
    *in_HL = uVar1;
    bVar2 = readInterruptMask();
  } while ((bVar2 & 0x80) != 0);
  display_refresh_control = display_refresh_control | 1;
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address
// WARNING: Unknown calling convention -- yet parameter storage is locked

void write_to_display(void)

{
  undefined1 *puVar1;
  undefined2 in_AF;
  byte bVar2;
  
                    // Character display function - writes single character from A register to
                    // screen
                    // Used by error display to show individual characters of error code
  puVar1 = _current_cursor_character_address;
  do {
    *puVar1 = (char)((ushort)in_AF >> 8);
    bVar2 = readInterruptMask();
  } while ((bVar2 & 0x80) != 0);
  display_refresh_control = display_refresh_control | 1;
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte display_string_template_processor(void)

{
  undefined2 in_DE;
  byte *in_HL;
  bool bVar1;
  
  display_template_processing_mode = 1;
  _display_buffer_position_counter = in_DE;
  while( true ) {
    bVar1 = *in_HL < 0x20;
    if (bVar1) {
      return *in_HL;
    }
    send_to_display();
    if (bVar1) break;
    in_HL = in_HL + 1;
  }
  return *in_HL;
}



byte write_string(void)

{
  byte *in_HL;
  bool bVar1;
  
                    // String display function - displays "ERROR NO.:" prefix before error code
                    // HL register points to string to display
  display_template_processing_mode = 0;
  while( true ) {
    bVar1 = *in_HL < 0x20;
    if (bVar1) {
      return *in_HL;
    }
    send_to_display();
    if (bVar1) break;
    in_HL = in_HL + 1;
  }
  return *in_HL;
}



void display_memory_block_copy_with_timing(void)

{
  byte bVar1;
  undefined1 uVar2;
  ushort in_DE;
  undefined1 *puVar3;
  char cVar4;
  undefined2 in_HL;
  undefined1 *puVar5;
  undefined1 in_CY_flag;
  
  bVar1 = (byte)((ushort)in_HL >> 8);
  set_display_status_flag();
  in_DE = in_DE & 0xff00;
  validate_display_memory_bounds();
  if (!(bool)in_CY_flag) {
    return_register_a();
    validate_display_memory_bounds();
    if (!(bool)in_CY_flag) {
      return_register_a();
      puVar3 = (undefined1 *)(in_DE & 0xff00);
      puVar5 = (undefined1 *)((ushort)bVar1 << 8);
      do {
        do {
          uVar2 = *puVar5;
          bVar1 = readInterruptMask();
        } while ((bVar1 & 0x80) != 0);
        do {
          *puVar3 = uVar2;
          bVar1 = readInterruptMask();
        } while ((bVar1 & 0x80) != 0);
        do {
          *puVar5 = 0;
          bVar1 = readInterruptMask();
        } while ((bVar1 & 0x80) != 0);
        cVar4 = (char)puVar5 + '\x01';
        puVar5 = (undefined1 *)CONCAT11((char)((ushort)puVar5 >> 8),cVar4);
        puVar3 = (undefined1 *)CONCAT11((char)((ushort)puVar3 >> 8),(char)puVar3 + '\x01');
      } while (cVar4 != 'P');
      finalize_display_operation();
      return;
    }
  }
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte_alt();
  return;
}



void display_memory_copy_without_clear(void)

{
  byte bVar1;
  undefined1 uVar2;
  ushort in_DE;
  undefined1 *puVar3;
  char cVar4;
  undefined2 in_HL;
  undefined1 *puVar5;
  undefined1 in_CY_flag;
  
  bVar1 = (byte)((ushort)in_HL >> 8);
  set_display_status_flag();
  in_DE = in_DE & 0xff00;
  validate_display_memory_bounds();
  if (!(bool)in_CY_flag) {
    return_register_a();
    validate_display_memory_bounds();
    if (!(bool)in_CY_flag) {
      return_register_a();
      puVar3 = (undefined1 *)(in_DE & 0xff00);
      puVar5 = (undefined1 *)((ushort)bVar1 << 8);
      do {
        do {
          uVar2 = *puVar5;
          bVar1 = readInterruptMask();
        } while ((bVar1 & 0x80) != 0);
        do {
          *puVar3 = uVar2;
          bVar1 = readInterruptMask();
        } while ((bVar1 & 0x80) != 0);
        cVar4 = (char)puVar5 + '\x01';
        puVar5 = (undefined1 *)CONCAT11((char)((ushort)puVar5 >> 8),cVar4);
        puVar3 = (undefined1 *)CONCAT11((char)((ushort)puVar3 >> 8),(char)puVar3 + '\x01');
      } while (cVar4 != 'P');
      finalize_display_operation();
      return;
    }
  }
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte_alt();
  return;
}



undefined1 clear_display_line(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 uVar2;
  char cVar3;
  undefined2 in_HL;
  undefined1 *puVar4;
  undefined1 in_CY_flag;
  
                    // CLEAR DISPLAY LINE: Clears a line of display memory with hardware
                    // synchronization
  bVar1 = (byte)((ushort)in_HL >> 8);
  validate_display_memory_bounds();
  if (!(bool)in_CY_flag) {
    return_register_a();
    set_display_status_flag_alt();
    enableMaskableInterrupts();
    puVar4 = (undefined1 *)((ushort)bVar1 << 8);
    do {
      do {
        *puVar4 = 0;
        bVar1 = readInterruptMask();
      } while ((bVar1 & 0x80) != 0);
      cVar3 = (char)puVar4 + '\x01';
      puVar4 = (undefined1 *)CONCAT11((char)((ushort)puVar4 >> 8),cVar3);
    } while (cVar3 != 'P');
    uVar2 = finalize_display_operation();
    return uVar2;
  }
  return (char)((ushort)in_AF >> 8);
}



void clear_display_memory_range(void)

{
  char cVar1;
  undefined1 *puVar2;
  char cVar3;
  
  setup_cursor_position();
  puVar2 = (undefined1 *)0x6050;
  configure_display_control_port_attributes(0);
  do {
    do {
      cVar3 = (char)((ushort)puVar2 >> 8);
      cVar1 = (char)puVar2 + -1;
      puVar2 = (undefined1 *)CONCAT11(cVar3,cVar1);
      *puVar2 = 0;
    } while (cVar1 != '\0');
    cVar3 = cVar3 + '\x01';
    puVar2 = (undefined1 *)CONCAT11(cVar3,0x50);
  } while (cVar3 != 'z');
  wait_for_VSYNC();
  write_attr_and_control2_and_pop_Return();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void display_fill_pattern_with_interrupt_check(void)

{
  byte bVar1;
  byte bVar2;
  undefined2 in_BC;
  undefined1 uVar3;
  undefined1 *puVar4;
  char cVar5;
  undefined1 in_CY_flag;
  
  uVar3 = (undefined1)((ushort)in_BC >> 8);
  set_display_status_flag();
  validate_display_region_with_bounds_check();
  if (!(bool)in_CY_flag) {
    return_af_register_high_byte();
    bVar2 = 0x80;
    puVar4 = _current_cursor_character_address;
    do {
      do {
        *puVar4 = uVar3;
        bVar1 = readInterruptMask();
        cVar5 = (bVar1 & bVar2) == 0;
      } while (!(bool)cVar5);
      check_display_memory_boundary_markers();
      compare_de_hl_registers();
    } while (cVar5 == '\0');
    finalize_display_operation();
    return;
  }
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte_alt();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void scroll_display_region_down(void)

{
  undefined1 uVar1;
  byte bVar2;
  undefined1 uVar3;
  short in_DE;
  undefined1 *puVar4;
  undefined1 *puVar5;
  undefined2 in_HL;
  char cVar6;
  char cVar7;
  
  uVar3 = (undefined1)((ushort)in_HL >> 8);
  set_display_status_flag();
  check_register_hl_for_start_marker();
  puVar5 = _next_cursor_character_address;
  scroll_direction_flag = 1;
  puVar4 = _next_cursor_character_address;
  while( true ) {
    cVar7 = '\0';
    cVar6 = (char)((ushort)in_DE >> 8) == '\0' && (char)in_DE == '\0';
    if ((bool)cVar6) break;
    check_register_hl_for_end_marker();
    in_DE = in_DE + -1;
  }
  compare_parameter_with_d_register();
  if (cVar7 == '\0') {
    return_af_register_high_byte();
    return_af_register_high_byte();
    return_af_register_high_byte();
    while( true ) {
      do {
        uVar3 = *puVar4;
        bVar2 = readInterruptMask();
      } while ((bVar2 & 0x80) != 0);
      do {
        *puVar5 = uVar3;
        bVar2 = readInterruptMask();
        uVar1 = (bVar2 & 0x80) == 0;
      } while (!(bool)uVar1);
      compare_parameter_with_d_register();
      if ((bool)uVar1) break;
      check_scroll_direction_boundary_markers();
      check_scroll_direction_boundary_markers();
    }
    while (compare_parameter_with_h_register(), !(bool)uVar1) {
      check_scroll_direction_boundary_markers();
LAB_ram_1880:
      do {
        *puVar5 = 0;
        bVar2 = readInterruptMask();
        uVar1 = (bVar2 & 0x80) == 0;
      } while (!(bool)uVar1);
    }
    write_AttributeRegister_And_ControlPort2();
    return_stack_value_high_byte();
    return;
  }
  check_register_hl_for_start_marker();
  compare_parameter_with_d_register();
  if (cVar6 == '\0') {
    write_AttributeRegister_And_ControlPort2();
    return_stack_value_high_byte_alt();
    return;
  }
  uVar1 = return_af_register_high_byte();
  return_af_register_high_byte(uVar1,uVar3);
  goto LAB_ram_1880;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void scroll_display_region_up(void)

{
  undefined1 uVar1;
  byte bVar2;
  undefined1 uVar3;
  short in_DE;
  undefined1 *puVar4;
  undefined1 *puVar5;
  undefined2 in_HL;
  char cVar6;
  bool bVar7;
  
  uVar3 = (undefined1)((ushort)in_HL >> 8);
  set_display_status_flag();
  puVar5 = _next_cursor_character_address;
  scroll_direction_flag = 0xff;
  puVar4 = _next_cursor_character_address;
  while( true ) {
    bVar7 = false;
    cVar6 = (char)((ushort)in_DE >> 8) == '\0' && (char)in_DE == '\0';
    if ((bool)cVar6) break;
    check_register_hl_for_start_marker();
    in_DE = in_DE + -1;
  }
  compare_parameter_with_d_register();
  if ((!bVar7) && (!(bool)cVar6)) {
    check_register_hl_for_end_marker();
    compare_parameter_with_d_register();
    if (cVar6 == '\0') {
      write_AttributeRegister_And_ControlPort2();
      return_stack_value_high_byte_alt();
      return;
    }
    uVar1 = return_af_register_high_byte();
    return_af_register_high_byte(uVar1,uVar3);
    goto LAB_ram_1880;
  }
  return_af_register_high_byte();
  return_af_register_high_byte();
  return_af_register_high_byte();
  while( true ) {
    do {
      uVar3 = *puVar4;
      bVar2 = readInterruptMask();
    } while ((bVar2 & 0x80) != 0);
    do {
      *puVar5 = uVar3;
      bVar2 = readInterruptMask();
      uVar1 = (bVar2 & 0x80) == 0;
    } while (!(bool)uVar1);
    compare_parameter_with_d_register();
    if ((bool)uVar1) break;
    check_scroll_direction_boundary_markers();
    check_scroll_direction_boundary_markers();
  }
  while (compare_parameter_with_h_register(), !(bool)uVar1) {
    check_scroll_direction_boundary_markers();
LAB_ram_1880:
    do {
      *puVar5 = 0;
      bVar2 = readInterruptMask();
      uVar1 = (bVar2 & 0x80) == 0;
    } while (!(bool)uVar1);
  }
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void insert_line_at_cursor(void)

{
  undefined1 uVar1;
  byte bVar2;
  undefined1 uVar3;
  undefined1 *puVar4;
  short in_DE;
  undefined1 *in_HL;
  char cVar5;
  bool bVar6;
  
                    // TDV 2215 Insert Line Function - Implements IL (Insert Line) CSI sequence with
                    // numeric parameter. Available only in extended mode when EC switch enabled.
                    // Part of new editing capabilities that extend beyond TDV 2115 compatibility.
                    // Works with Vertical Editing Mode switch to control behavior relative to
                    // cursor position.
  set_display_status_flag();
  check_register_hl_for_start_marker();
  uVar3 = (undefined1)((ushort)_next_cursor_character_address >> 8);
  scroll_direction_flag = 0xff;
  puVar4 = in_HL;
  while( true ) {
    bVar6 = false;
    cVar5 = (char)((ushort)in_DE >> 8) == '\0' && (char)in_DE == '\0';
    if ((bool)cVar5) break;
    check_register_hl_for_start_marker();
    in_DE = in_DE + -1;
  }
  compare_parameter_with_d_register();
  if ((!bVar6) && (!(bool)cVar5)) {
    check_register_hl_for_end_marker();
    compare_parameter_with_d_register();
    if (cVar5 == '\0') {
      write_AttributeRegister_And_ControlPort2();
      return_stack_value_high_byte_alt();
      return;
    }
    uVar1 = return_af_register_high_byte();
    return_af_register_high_byte(uVar1,uVar3);
    goto LAB_ram_1880;
  }
  return_af_register_high_byte();
  return_af_register_high_byte();
  return_af_register_high_byte();
  while( true ) {
    do {
      uVar3 = *puVar4;
      bVar2 = readInterruptMask();
    } while ((bVar2 & 0x80) != 0);
    do {
      *in_HL = uVar3;
      bVar2 = readInterruptMask();
      uVar1 = (bVar2 & 0x80) == 0;
    } while (!(bool)uVar1);
    compare_parameter_with_d_register();
    if ((bool)uVar1) break;
    check_scroll_direction_boundary_markers();
    check_scroll_direction_boundary_markers();
  }
  while (compare_parameter_with_h_register(), !(bool)uVar1) {
    check_scroll_direction_boundary_markers();
LAB_ram_1880:
    do {
      *in_HL = 0;
      bVar2 = readInterruptMask();
      uVar1 = (bVar2 & 0x80) == 0;
    } while (!(bool)uVar1);
  }
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void delete_line_at_cursor(void)

{
  undefined1 uVar1;
  byte bVar2;
  undefined1 uVar3;
  undefined1 *puVar4;
  short in_DE;
  undefined1 *in_HL;
  char cVar5;
  char cVar6;
  
                    // TDV 2215 Delete Line Function - Implements DL (Delete Line) CSI sequence with
                    // numeric parameter. Available only in extended mode when EC switch enabled.
                    // Part of enhanced editing functions that require host support. Behavior
                    // controlled by Vertical Editing Mode switch setting.
  set_display_status_flag();
  uVar3 = (undefined1)((ushort)_next_cursor_character_address >> 8);
  scroll_direction_flag = 1;
  puVar4 = in_HL;
  while( true ) {
    cVar6 = '\0';
    cVar5 = (char)((ushort)in_DE >> 8) == '\0' && (char)in_DE == '\0';
    if ((bool)cVar5) break;
    check_register_hl_for_end_marker();
    in_DE = in_DE + -1;
  }
  compare_parameter_with_d_register();
  if (cVar6 == '\0') {
    return_af_register_high_byte();
    return_af_register_high_byte();
    return_af_register_high_byte();
    while( true ) {
      do {
        uVar3 = *puVar4;
        bVar2 = readInterruptMask();
      } while ((bVar2 & 0x80) != 0);
      do {
        *in_HL = uVar3;
        bVar2 = readInterruptMask();
        uVar1 = (bVar2 & 0x80) == 0;
      } while (!(bool)uVar1);
      compare_parameter_with_d_register();
      if ((bool)uVar1) break;
      check_scroll_direction_boundary_markers();
      check_scroll_direction_boundary_markers();
    }
    while (compare_parameter_with_h_register(), !(bool)uVar1) {
      check_scroll_direction_boundary_markers();
LAB_ram_1880:
      do {
        *in_HL = 0;
        bVar2 = readInterruptMask();
        uVar1 = (bVar2 & 0x80) == 0;
      } while (!(bool)uVar1);
    }
    write_AttributeRegister_And_ControlPort2();
    return_stack_value_high_byte();
    return;
  }
  check_register_hl_for_start_marker();
  compare_parameter_with_d_register();
  if (cVar5 == '\0') {
    write_AttributeRegister_And_ControlPort2();
    return_stack_value_high_byte_alt();
    return;
  }
  uVar1 = return_af_register_high_byte();
  return_af_register_high_byte(uVar1,uVar3);
  goto LAB_ram_1880;
}



void check_register_hl_for_end_marker(void)

{
  char in_L;
  char in_H;
  
  if (in_L != 'P') {
    return;
  }
  if (in_H != -1) {
    return;
  }
  return;
}



void check_register_hl_for_start_marker(void)

{
  char in_L;
  char in_H;
  
  if (in_L != '\x01') {
    return;
  }
  if (in_H != '\0') {
    return;
  }
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void complex_display_memory_swap_operation(void)

{
  byte in_io_00000070;
  byte bVar1;
  byte bVar2;
  byte *in_BC;
  byte *in_DE;
  byte *pbVar3;
  undefined2 in_HL;
  undefined1 uVar4;
  undefined1 in_CY_flag;
  
  validate_display_region_with_bounds_check();
  if ((bool)in_CY_flag) {
    write_AttributeRegister_And_ControlPort2();
    return_stack_value_high_byte_alt();
    return;
  }
  return_af_register_high_byte();
  pbVar3 = _current_cursor_character_address;
  _temp_memory_address = in_HL;
  configure_display_control_port_attributes();
  temp_attribute_byte = in_io_00000070;
  while( true ) {
    temp_display_char = *pbVar3;
    bVar2 = *in_BC >> 4;
    *pbVar3 = *in_DE;
    *in_DE = temp_display_char;
    bVar1 = *in_BC & 0xf | temp_attribute_byte << 4;
    uVar4 = bVar1 == 0;
    *in_BC = bVar1;
    check_display_memory_boundary_markers();
    compare_de_hl_registers();
    if ((bool)uVar4) break;
    temp_display_char = *pbVar3;
    bVar1 = *in_BC & 0xf;
    temp_attribute_byte = bVar2;
    *pbVar3 = in_DE[1];
    in_DE[1] = temp_display_char;
    bVar2 = *in_BC & 0xf0 | temp_attribute_byte & 0xf;
    uVar4 = bVar2 == 0;
    *in_BC = bVar2;
    check_display_memory_boundary_markers();
    compare_de_hl_registers();
    if ((bool)uVar4) break;
    in_DE = in_DE + 2;
    in_BC = in_BC + 1;
    temp_attribute_byte = bVar1;
  }
  wait_for_VSYNC();
  finalize_display_operation();
  return;
}



void write_attr_and_control2_and_pop_Return(void)

{
  write_AttributeRegister_And_ControlPort2();
  popall_and_ret();
  return;
}



void finalize_display_operation(void)

{
  write_AttributeRegister_And_ControlPort2();
  return_stack_value_high_byte();
  return;
}



undefined1 scroll_lines_down_to_cursor(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 uVar2;
  byte bVar3;
  undefined2 in_HL;
  byte bVar4;
  bool bVar5;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  bVar4 = (byte)((ushort)in_HL >> 8);
  bVar5 = current_cursor_row < bVar4;
  if (current_cursor_row == bVar4) {
    clear_display_line();
    uVar1 = return_stack_value_high_byte();
    return uVar1;
  }
  uVar2 = uVar1;
  bVar3 = current_cursor_row;
  if (bVar5) {
    while( true ) {
      bVar4 = bVar4 - 1;
      display_memory_block_copy_with_timing(uVar2);
      if (bVar5) break;
      bVar5 = bVar4 < bVar3;
      if (bVar4 == bVar3) {
        return uVar1;
      }
    }
  }
  return uVar1;
}



undefined1 scroll_lines_up_to_cursor(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 uVar2;
  byte bVar3;
  undefined2 in_HL;
  byte bVar4;
  bool bVar5;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  bVar4 = (byte)((ushort)in_HL >> 8);
  bVar5 = current_cursor_row < bVar4;
  if (current_cursor_row == bVar4) {
    clear_display_line();
    uVar1 = return_stack_value_high_byte();
    return uVar1;
  }
  uVar2 = uVar1;
  bVar3 = current_cursor_row;
  if (!bVar5) {
    while( true ) {
      bVar4 = bVar4 + 1;
      display_memory_block_copy_with_timing(uVar2);
      if (bVar5) break;
      bVar5 = bVar4 < bVar3;
      if (bVar4 == bVar3) {
        return uVar1;
      }
    }
  }
  return uVar1;
}



undefined1 scroll_lines_up_from_cursor(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 uVar2;
  undefined2 in_HL;
  byte bVar3;
  byte bVar4;
  bool bVar5;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  bVar3 = (byte)((ushort)in_HL >> 8);
  bVar5 = current_cursor_row < bVar3;
  if (current_cursor_row == bVar3) {
    clear_display_line();
    uVar1 = return_stack_value_high_byte();
    return uVar1;
  }
  uVar2 = uVar1;
  bVar4 = current_cursor_row;
  if (bVar5) {
    while( true ) {
      bVar4 = bVar4 + 1;
      display_memory_block_copy_with_timing(uVar2);
      if (bVar5) break;
      bVar5 = bVar4 < bVar3;
      if (bVar4 == bVar3) {
        return uVar1;
      }
    }
  }
  return uVar1;
}



undefined1 scroll_lines_down_from_cursor(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined1 uVar2;
  undefined2 in_HL;
  byte bVar3;
  byte bVar4;
  bool bVar5;
  
                    // Scrolls display lines downward from cursor position to target row. If cursor
                    // is at target row, clears the line. Otherwise performs block copy operations
                    // moving lines down until reaching target position.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  bVar3 = (byte)((ushort)in_HL >> 8);
  bVar5 = current_cursor_row < bVar3;
  if (current_cursor_row == bVar3) {
    clear_display_line();
    uVar1 = return_stack_value_high_byte();
    return uVar1;
  }
  uVar2 = uVar1;
  bVar4 = current_cursor_row;
  if (!bVar5) {
    while( true ) {
      bVar4 = bVar4 - 1;
      display_memory_block_copy_with_timing(uVar2);
      if (bVar5) break;
      bVar5 = bVar3 < bVar4;
      if (bVar3 == bVar4) {
        return uVar1;
      }
    }
  }
  return uVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 setup_memory_region_for_display_operation(void)

{
  undefined2 in_AF;
  char cVar1;
  undefined1 uVar2;
  undefined1 uVar3;
  undefined2 in_BC;
  undefined2 in_HL;
  byte bVar5;
  byte *pbVar4;
  
                    // Sets up memory region for display operations. Validates row count (1-25),
                    // clears 120-byte buffer at ram:5e40, and prepares memory region for complex
                    // display operations with cursor address management.
  uVar3 = (undefined1)((ushort)in_BC >> 8);
  bVar5 = (byte)((ushort)in_HL >> 8);
  if ((bVar5 != 0) && (bVar5 < 0x1a)) {
    pbVar4 = &memory_region_buffer;
    cVar1 = 'x';
    memory_region_row_count = bVar5;
    do {
      *pbVar4 = 0;
      pbVar4 = pbVar4 + 1;
      cVar1 = cVar1 + -1;
    } while (cVar1 != '\0');
    no_operation_placeholder();
    _memory_region_pointer = pbVar4;
    uVar2 = update_cursor_addresses();
    uVar2 = complex_display_memory_swap_operation(uVar2,0x5e);
    update_cursor_addresses(uVar2,uVar3);
    uVar3 = pop_hl_and_af_return_no_carry();
    return uVar3;
  }
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 restore_memory_region_after_display_operation(void)

{
  byte bVar1;
  undefined1 uVar2;
  undefined1 uVar3;
  undefined2 in_AF;
  undefined2 in_BC;
  undefined2 uVar4;
  
  bVar1 = memory_region_row_count;
                    // Restores memory region after display operation. Clears the region row count
                    // flag and performs cleanup operations to restore normal display state after
                    // complex memory operations.
  uVar3 = (undefined1)((ushort)in_BC >> 8);
  if (memory_region_row_count != 0) {
    memory_region_row_count = 0;
    uVar4 = CONCAT11(bVar1,1);
    no_operation_placeholder();
    _memory_region_pointer = uVar4;
    uVar2 = update_cursor_addresses();
    uVar2 = complex_display_memory_swap_operation(uVar2,0x5e);
    update_cursor_addresses(uVar2,uVar3);
    uVar3 = pop_hl_and_af_return_no_carry();
    return uVar3;
  }
  return (char)((ushort)in_AF >> 8);
}



byte display_configuration_submenu_and_handle_navigation(void)

{
  byte bVar1;
  char cVar2;
  byte bVar3;
  byte bVar4;
  byte *pbVar5;
  byte *in_HL;
  undefined1 uVar6;
  
  clear_display_memory_range();
  pbVar5 = &config_menu_state_buffer;
  cVar2 = '\x05';
  do {
    *pbVar5 = *in_HL;
    in_HL = in_HL + 1;
    pbVar5 = pbVar5 + 1;
    cVar2 = cVar2 + -1;
  } while (cVar2 != '\0');
  load_config_from_nvram_template();
  update_cursor_memory_addresses();
  display_formatted_string_with_templates();
  set_display_attribute_and_trigger_refresh(0xe);
  write_char_and_advance_cursor(0x20);
  write_char_and_advance_cursor();
  if (config_menu_level != 0) {
    write_char_and_advance_cursor(config_menu_level + 0x30);
    write_to_display();
  }
  set_display_attribute_and_trigger_refresh(0);
  bVar3 = config_option_start_index;
  do {
    display_complete_configuration_option();
    bVar3 = bVar3 + 1;
    bVar4 = config_option_start_index;
  } while ((byte)(config_option_end_index + 1) != bVar3);
LAB_ram_1a81:
  bVar3 = bVar4;
  no_operation_placeholder();
  uVar6 = 0;
  write_pattern_to_display_area_and_update_attributes(0);
  do {
    display_complete_configuration_option();
    no_operation_placeholder();
    write_pattern_to_display_area_and_update_attributes(3);
    while( true ) {
      do {
        bVar1 = get_keyboard_char_atomic();
      } while ((bool)uVar6);
      uVar6 = bVar1 < 0xed;
      if (bVar1 == 0xed) break;
      bVar4 = config_option_start_index;
      if (bVar1 == 0xb8) goto LAB_ram_1a81;
      if (bVar1 == 0xb1) {
        uVar6 = config_option_start_index < bVar3;
        if (config_option_start_index != bVar3) {
          bVar4 = bVar3 - 1;
          goto LAB_ram_1a81;
        }
      }
      else {
        if (bVar1 == 0xb2) {
          bVar4 = bVar3;
          if (config_option_end_index != bVar3) {
            bVar4 = bVar3 + 1;
          }
          goto LAB_ram_1a81;
        }
        if ((bVar1 == 0xf8) || (bVar1 == 0xdb)) {
          clear_display_memory_range(bVar1,bVar1);
          set_display_attribute_and_trigger_refresh(0);
          return bVar1;
        }
        uVar6 = bVar1 < 0xfc;
        if (bVar1 == 0xfc) {
          call_display_function_pointer();
        }
        else {
          check_interrupt_flag_and_validate_af();
        }
      }
    }
    edit_configuration_option_value();
  } while( true );
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void call_display_function_pointer(void)

{
  if ((char)((ushort)_display_function_pointer >> 8) == '\0' &&
      (char)_display_function_pointer == '\0') {
    return;
  }
                    // WARNING: Could not recover jumptable at 0x1afe. Too many branches
                    // WARNING: Treating indirect jump as call
  (*_display_function_pointer)();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void edit_configuration_option_value(void)

{
  undefined1 uVar1;
  byte bVar2;
  byte bVar4;
  ushort uVar3;
  
  bVar4 = *_config_current_option_index + 1;
  bVar2 = 0;
  do {
    bVar2 = bVar2 + 1;
    calculate_menu_cursor_position();
    uVar1 = 0;
    if (bVar4 == bVar2) {
      uVar1 = 3;
    }
    set_display_attribute_and_trigger_refresh(uVar1);
    render_configuration_option_text();
  } while (config_total_options_count != bVar2);
  bVar4 = *_config_selected_option_index;
  uVar1 = bVar4 < bVar2;
  if (!(bool)uVar1) {
    bVar4 = 0;
  }
  uVar3 = (ushort)(byte)(bVar4 + 1);
LAB_ram_1b2d:
  do {
    calculate_menu_cursor_position();
    do {
      do {
        bVar2 = get_keyboard_char_atomic();
      } while ((bool)uVar1);
      bVar4 = (byte)uVar3;
      if (bVar2 == 0xed) {
        *_config_selected_option_index = bVar4 - 1;
        clear_display_line();
        clear_display_line();
        clear_display_line();
        return;
      }
      uVar1 = bVar2 < 0xb8;
      if (bVar2 == 0xb8) {
LAB_ram_1b40:
        uVar3 = 1;
        goto LAB_ram_1b2d;
      }
      uVar1 = bVar2 < 0xb4;
      if (bVar2 == 0xb4) {
        uVar3 = (ushort)(byte)(bVar4 - 1);
        if ((byte)(bVar4 - 1) == 0) goto LAB_ram_1b40;
        goto LAB_ram_1b2d;
      }
      uVar1 = bVar2 < 0xb3;
    } while ((bVar2 != 0xb3) ||
            (uVar1 = config_total_options_count < bVar4, config_total_options_count == bVar4));
    uVar3 = (ushort)(byte)(bVar4 + 1);
  } while( true );
}



void display_complete_configuration_option(void)

{
  position_cursor_for_menu_option();
  clear_display_line();
  load_and_display_configuration_template();
  position_cursor_for_menu_option_alt();
  render_configuration_option_text();
  position_cursor_for_menu_option();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void load_and_display_configuration_template(void)

{
  undefined1 uVar1;
  short in_BC;
  byte bVar2;
  byte *pbVar3;
  
  pbVar3 = _config_menu_state_buffer;
  skip_to_next_template_section();
  while (bVar2 = (char)((ushort)in_BC >> 8) - 1, in_BC = (ushort)bVar2 << 8, bVar2 != 0) {
    skip_to_next_template_section();
    bVar2 = pbVar3[1];
    pbVar3 = pbVar3 + 2;
    do {
      skip_control_characters();
      bVar2 = bVar2 - 1;
    } while (bVar2 != 0);
  }
  process_and_display_template_text();
  _config_selected_option_index = &sw_cursor_type + *pbVar3;
  _config_current_option_index = &DAT_ram_5eb8 + *pbVar3;
  uVar1 = 0x20;
  if (*_config_selected_option_index != *_config_current_option_index) {
    uVar1 = 0x2a;
  }
  no_operation_placeholder(uVar1);
  update_cursor_memory_addresses();
  write_to_display();
  config_total_options_count = pbVar3[1];
  _config_option_text_pointer = pbVar3 + 2;
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void render_configuration_option_text(void)

{
  byte bVar1;
  ushort in_BC;
  byte *pbVar2;
  
                    // Render option text (for system config)
  pbVar2 = _config_option_text_pointer;
  while (bVar1 = (char)in_BC - 1, in_BC = (ushort)bVar1, bVar1 != 0) {
    skip_control_characters();
  }
  if (0x7f < *pbVar2) {
    config_option_display_flag = 0;
    config_option_display_code = *pbVar2;
  }
  process_and_display_template_text();
  return;
}



void skip_to_next_template_section(void)

{
  byte bVar1;
  byte *in_HL;
  
  do {
    bVar1 = *in_HL;
    in_HL = in_HL + 1;
  } while (0x1f < bVar1);
  return;
}



void skip_control_characters(void)

{
  byte bVar1;
  byte *in_HL;
  
  if (0x7f < *in_HL) {
    return;
  }
  do {
    bVar1 = *in_HL;
    in_HL = in_HL + 1;
  } while (0x1f < bVar1);
  return;
}



void position_cursor_for_menu_option(undefined1 param_1,char param_2)

{
  update_cursor_addresses((param_2 - config_option_start_index) + '\x03');
  return;
}



void position_cursor_for_menu_option_alt(undefined1 param_1,char param_2)

{
  update_cursor_addresses((param_2 - config_option_start_index) + '\x03');
  return;
}



void calculate_menu_cursor_position(void)

{
  byte bVar1;
  char in_C;
  
  bVar1 = 1;
  if ((6 < config_total_options_count) && (bVar1 = 1, 0xc < config_total_options_count)) {
    bVar1 = 1;
  }
  while (in_C = in_C + -1, in_C != '\0') {
    bVar1 = bVar1 + 0xd;
    if (0x44 < bVar1) {
      bVar1 = 1;
    }
  }
  update_cursor_memory_addresses();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 push_key_menu_interface(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 uVar2;
  byte in_C;
  byte bVar3;
  undefined1 uVar5;
  short sVar4;
  char cVar6;
  char *pcVar7;
  char cVar8;
  
                    // TDV 2215 PUSH-Key Menu Interface - Manages 8 physical PUSH-keys (16 with
                    // SHIFT) as per section 5.3. Each key can store programmable sequences in
                    // NVRAM. Controlled by PKP soft-switch (4.2.11) - can be ALLOWED or PROHIBITED.
                    // Sequences can be programmed locally via menu or remotely via DCS (Device
                    // Control String) from host.
  clear_display_memory_range();
  update_cursor_addresses();
  pcVar7 = "PUSH-KEY MENU";
  display_formatted_string_with_templates();
  bVar3 = 1;
  do {
    cVar8 = (char)pcVar7;
    clear_status_port_bit0();
    calculate_cursor_position_from_offset();
    pcVar7 = (char *)(ushort)(byte)(cVar8 - 4U);
    update_cursor_addresses(cVar8 - 4U);
    display_decimal_number(bVar3);
    increase_cursor_character();
    write_to_display();
    calculate_cursor_position_from_offset();
    get_push_key_nvram_address_from_table(bVar3 - 1);
    validate_push_key_index_and_get_length();
    do {
      set_display_attribute_and_trigger_refresh(4);
      bVar1 = read_from_nvram();
      if ((bVar1 < 0x20) || (0x7f < bVar1)) {
        set_display_attribute_and_trigger_refresh(7);
      }
      write_char_and_advance_cursor();
      pcVar7 = pcVar7 + 1;
      in_C = in_C - 1;
    } while (in_C != 0);
    bVar3 = bVar3 + 1;
    in_C = 0;
    cVar8 = bVar3 < 0x11;
  } while (bVar3 != 0x11);
  clear_status_port_bit0();
  update_cursor_addresses();
  write_string();
  set_display_attribute_and_trigger_refresh(1);
  write_string();
  set_display_attribute_and_trigger_refresh(4);
LAB_ram_1ce2:
  bVar3 = 1;
LAB_ram_1ce4:
  calculate_cursor_position_from_offset();
  validate_push_key_index_and_get_length(bVar3 - 1);
  get_push_key_nvram_address_from_table();
  sVar4 = 0x100;
LAB_ram_1cf1:
  do {
    uVar5 = (undefined1)((ushort)sVar4 >> 8);
    uVar2 = read_from_nvram();
    sVar4 = CONCAT11(uVar5,uVar2);
    update_cursor_addresses();
    set_display_attribute_and_trigger_refresh(1);
    display_error_code_custom_chars((char)sVar4);
    update_cursor_addresses();
    set_display_attribute_and_trigger_refresh(4);
    do {
      while( true ) {
        while( true ) {
          do {
            bVar1 = get_keyboard_char_atomic();
          } while ((bool)cVar8);
          cVar8 = bVar1 < 0xfc;
          if (bVar1 != 0xfc) break;
          call_display_function_pointer();
        }
        uVar2 = bVar1 < 0xcf;
        if (bVar1 != 0xcf) break;
        do {
          bVar1 = get_keyboard_char_atomic();
        } while ((bool)uVar2);
LAB_ram_1d93:
        cVar8 = '\0';
        if (((bVar1 != 0) && (cVar8 = bVar1 < 0xf8, (bool)cVar8)) &&
           ((cVar8 = bVar1 < 0xb0, !(bool)cVar8 || (cVar8 = bVar1 < 0xa0, (bool)cVar8)))) {
          write_byte_to_nvram();
          if ((bool)cVar8) {
            check_interrupt_flag_and_validate_af();
          }
          bVar1 = read_from_nvram();
          if ((bVar1 < 0x80) && (0x1f < bVar1)) {
            write_to_display();
          }
          else {
            set_display_attribute_and_trigger_refresh(7);
            write_to_display();
            set_display_attribute_and_trigger_refresh(4);
          }
LAB_ram_1d73:
          bVar1 = (byte)((ushort)sVar4 >> 8);
          cVar8 = bVar1 < in_C;
          if (bVar1 != in_C) {
            sVar4 = (ushort)(byte)(bVar1 + 1) << 8;
            increase_cursor_character();
          }
          goto LAB_ram_1cf1;
        }
LAB_ram_1d43:
        check_interrupt_flag_and_validate_af();
      }
      cVar8 = bVar1 < 0xce;
      if (bVar1 == 0xce) {
        bVar1 = parse_hex_byte();
        if (cVar8 == '\0') goto LAB_ram_1d93;
        goto LAB_ram_1d43;
      }
      if (bVar1 == 0xdb) {
        clear_display_memory_range();
        set_display_attribute_and_trigger_refresh(0);
        return (char)((ushort)in_AF >> 8);
      }
      cVar8 = bVar1 < 0xb8;
      if (bVar1 == 0xb8) goto LAB_ram_1ce2;
      cVar8 = bVar1 < 0xb1;
      if (bVar1 == 0xb1) {
LAB_ram_1d58:
        bVar3 = bVar3 - 1;
        if (bVar3 != 0) goto LAB_ram_1ce4;
        goto LAB_ram_1ce2;
      }
      if (bVar1 == 0xb2) {
        bVar3 = bVar3 + 1;
        cVar8 = bVar3 < 0x11;
        if (bVar3 == 0x11) goto LAB_ram_1d58;
        goto LAB_ram_1ce4;
      }
      if (bVar1 == 0xb3) goto LAB_ram_1d73;
      if (bVar1 != 0xb4) goto LAB_ram_1d93;
      cVar6 = (char)((ushort)sVar4 >> 8);
      cVar8 = cVar6 == '\0';
    } while (cVar6 == '\x01');
    sVar4 = (ushort)(byte)(cVar6 - 1) << 8;
    move_cursor_back_one_position_safe();
  } while( true );
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte calculate_cursor_position_from_offset(undefined1 param_1,byte param_2)

{
  byte bVar1;
  byte bVar2;
  byte bVar3;
  
                    // TDV 2215 Cursor Position Calculation - Calculates cursor positions for direct
                    // cursor addressing feature. The terminal supports host position reporting and
                    // precise cursor control across the 15" display screen. Part of the TDV 2115
                    // compatible cursor addressing system.
  bVar3 = 3;
  bVar1 = param_2;
  if (4 < param_2) {
    bVar1 = param_2 - 4;
    bVar3 = 5;
    if (4 < bVar1) {
      param_2 = param_2 - 8;
      bVar2 = 7;
      bVar3 = 8;
      while (param_2 = param_2 - 1, param_2 != 0) {
        bVar3 = bVar3 + 2;
      }
      goto update_cursor_addresses;
    }
  }
  param_2 = bVar1;
  bVar2 = 7;
  while (bVar1 = bVar1 - 1, bVar1 != 0) {
    param_2 = bVar2 + 0x12;
    bVar2 = param_2;
  }
update_cursor_addresses:
  next_cursor_character_address = bVar2;
  current_cursor_row = bVar3;
  return_register_a();
  current_cursor_character_address = bVar2;
  current_cursor_character_address_1 = bVar3;
  return param_2;
}



undefined1 store_push_key_string_to_nvram(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  byte bVar2;
  char in_C;
  char cVar3;
  byte *in_HL;
  undefined1 in_CY_flag;
  bool bVar4;
  
                    // Stores push-key string to NVRAM. Validates push-key index, gets NVRAM
                    // address, and writes string data while filtering invalid characters (0x00,
                    // >0xF7, and control codes 0xA0-0xAF).
  validate_push_key_index_and_get_length();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (!(bool)in_CY_flag) {
    get_push_key_nvram_address_from_table();
    cVar3 = '\x01';
    while( true ) {
      if ((((cVar3 == '\0') || (bVar2 = *in_HL, bVar2 == 0)) || (0xf7 < bVar2)) ||
         ((bVar4 = bVar2 < 0xb0, bVar4 && (bVar4 = bVar2 < 0xa0, !bVar4)))) {
        cVar3 = '\0';
        bVar4 = false;
      }
      write_byte_to_nvram();
      if (bVar4) break;
      in_HL = in_HL + 1;
      in_C = in_C + -1;
      if (in_C == '\0') {
        return uVar1;
      }
    }
  }
  return uVar1;
}



char load_push_key_string_from_nvram(void)

{
  undefined2 in_AF;
  char cVar1;
  char in_C;
  char cVar2;
  char *in_HL;
  undefined1 in_CY_flag;
  
                    // PUSH KEY HANDLER: Loads programmable push key string from NVRAM (Keys 1-16)
  validate_push_key_index_and_get_length();
  if ((bool)in_CY_flag) {
    return (char)((ushort)in_AF >> 8);
  }
  cVar2 = in_C;
  get_push_key_nvram_address_from_table();
  do {
    cVar1 = read_from_nvram();
    *in_HL = cVar1;
    in_HL = in_HL + 1;
    in_C = in_C + -1;
  } while (in_C != '\0');
  *in_HL = '\0';
  cVar1 = '\0';
  do {
    in_HL = in_HL + -1;
    if (*in_HL != ' ') {
      return cVar2;
    }
    *in_HL = '\0';
    cVar1 = cVar1 + '\x01';
  } while (cVar1 != cVar2);
  return cVar2;
}



byte validate_push_key_index_and_get_length(byte param_1)

{
                    // PUSH KEY VALIDATOR: Validates push key index (1-16) and determines string
                    // length
  if (((7 < param_1) && (0xb < param_1)) && (0xf < param_1)) {
    return param_1;
  }
  return param_1;
}



undefined1 get_push_key_nvram_address_from_table(void)

{
  undefined2 in_AF;
  
                    // PUSH KEY NVRAM LOOKUP: Gets NVRAM address from push key table at 0x1ed1
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte display_formatted_string_with_templates(void)

{
  byte bVar1;
  byte *in_HL;
  byte *pbVar2;
  bool bVar3;
  char cVar4;
  
  display_template_processing_mode = 0;
  set_display_status_flag_alt();
  bVar1 = next_cursor_character_address;
  if (next_cursor_character_address < 0x4f) {
    if ((next_cursor_character_address & 1) != 0) {
      send_to_display(0x20);
    }
    send_to_display(0x20);
    do {
      while( true ) {
        bVar1 = *in_HL;
        in_HL = in_HL + 1;
        if (bVar1 < 0x20) {
          write_AttributeRegister_And_ControlPort2();
          return bVar1;
        }
        bVar3 = bVar1 < 0x80;
        if (bVar3) break;
        if ((char)((ushort)_system_config_pointer >> 8) != '\0' ||
            (char)_system_config_pointer != '\0') {
          pbVar2 = *(byte **)(_system_config_pointer + (ushort)(byte)(bVar1 * '\x02'));
          while( true ) {
            bVar1 = *pbVar2;
            pbVar2 = pbVar2 + 1;
            cVar4 = bVar1 < 0x20;
            if ((bool)cVar4) break;
            output_character_to_display_buffer();
            if (cVar4 != '\0') {
              bVar1 = finalize_display_output();
              return bVar1;
            }
          }
        }
        bVar3 = *in_HL < 0x80;
        if ((!bVar3) && (bVar1 = output_character_to_display_buffer(0x20), bVar3))
        goto code_r0x1fce;
      }
      bVar1 = output_character_to_display_buffer();
    } while (!bVar3);
  }
code_r0x1fce:
  write_AttributeRegister_And_ControlPort2();
  return bVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte display_string_to_buffer_with_templates(void)

{
  byte bVar1;
  undefined2 in_DE;
  byte *in_HL;
  byte *pbVar2;
  bool bVar3;
  char cVar4;
  
  display_template_processing_mode = 1;
  _display_buffer_position_counter = in_DE;
  do {
    while( true ) {
      bVar1 = *in_HL;
      in_HL = in_HL + 1;
      if (bVar1 < 0x20) {
        write_AttributeRegister_And_ControlPort2();
        return bVar1;
      }
      bVar3 = bVar1 < 0x80;
      if (!bVar3) break;
      bVar1 = output_character_to_display_buffer();
      if (bVar3) goto code_r0x1fce;
    }
    if ((char)((ushort)_system_config_pointer >> 8) != '\0' || (char)_system_config_pointer != '\0')
    {
      pbVar2 = *(byte **)(_system_config_pointer + (ushort)(byte)(bVar1 * '\x02'));
      while( true ) {
        bVar1 = *pbVar2;
        pbVar2 = pbVar2 + 1;
        cVar4 = bVar1 < 0x20;
        if ((bool)cVar4) break;
        output_character_to_display_buffer();
        if (cVar4 != '\0') {
          bVar1 = finalize_display_output();
          return bVar1;
        }
      }
    }
    bVar3 = *in_HL < 0x80;
  } while ((bVar3) || (bVar1 = output_character_to_display_buffer(0x20), !bVar3));
code_r0x1fce:
  write_AttributeRegister_And_ControlPort2();
  return bVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte process_and_display_template_text(void)

{
  byte bVar1;
  byte *in_HL;
  byte *pbVar2;
  bool bVar3;
  char cVar4;
  
  display_template_processing_mode = 0;
  do {
    while( true ) {
      bVar1 = *in_HL;
      in_HL = in_HL + 1;
      if (bVar1 < 0x20) {
        write_AttributeRegister_And_ControlPort2();
        return bVar1;
      }
      bVar3 = bVar1 < 0x80;
      if (!bVar3) break;
      bVar1 = output_character_to_display_buffer();
      if (bVar3) goto code_r0x1fce;
    }
    if ((char)((ushort)_system_config_pointer >> 8) != '\0' || (char)_system_config_pointer != '\0')
    {
      pbVar2 = *(byte **)(_system_config_pointer + (ushort)(byte)(bVar1 * '\x02'));
      while( true ) {
        bVar1 = *pbVar2;
        pbVar2 = pbVar2 + 1;
        cVar4 = bVar1 < 0x20;
        if ((bool)cVar4) break;
        output_character_to_display_buffer();
        if (cVar4 != '\0') {
          bVar1 = finalize_display_output();
          return bVar1;
        }
      }
    }
    bVar3 = *in_HL < 0x80;
  } while ((bVar3) || (bVar1 = output_character_to_display_buffer(0x20), !bVar3));
code_r0x1fce:
  write_AttributeRegister_And_ControlPort2();
  return bVar1;
}



byte output_character_to_display_buffer(void)

{
  byte bVar1;
  byte in_C;
  undefined1 in_CY_flag;
  
  bVar1 = send_to_display();
  if (!(bool)in_CY_flag) {
    if (in_C == 0) {
      return in_C;
    }
    send_to_display(0x20);
    if (next_cursor_character_address < 0x4f) {
      return next_cursor_character_address;
    }
    write_to_display();
    bVar1 = reset_cursor_to_line_start();
  }
  return bVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

char send_to_display(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  char cVar2;
  char cVar3;
  
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (display_template_processing_mode == 0) {
    cVar2 = write_char_and_advance_cursor(uVar1);
    return cVar2;
  }
  write_char_to_video_memory_and_refresh(uVar1);
  cVar3 = (char)((ushort)_display_buffer_position_counter >> 8);
  cVar2 = (char)_display_buffer_position_counter + '\x01';
  if (cVar2 == 'Q') {
    cVar3 = cVar3 + '\x01';
    cVar2 = '\x01';
  }
  _display_buffer_position_counter = CONCAT11(cVar3,cVar2);
  return cVar3;
}



undefined1 finalize_display_output(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  
                    // TDV 2215 Terminal Main Loop - Central processing for simultaneous
                    // send/receive mode. Handles character-by-character transmission, processes
                    // C0/C1 control codes, CSI sequences, and manages cursor positioning. Supports
                    // both TDV 2115 compatible mode and extended operation (when EC switch enabled
                    // via ESC Q).
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  write_AttributeRegister_And_ControlPort2();
  return uVar1;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void terminal_main_loop(void)

{
  char cVar1;
  byte *pbVar2;
  char in_CY_flag;
  
  do {
                    // Terminal Main Loop - CHARACTER PROCESSING ENTRY POINT
                    // This is where characters from keyboard flow into terminal processing
                    // If special keys not working, check if characters reach this function
                    // Characters should flow: MC6850 → buffers → main loop →
                    // special_character_processor
  } while ((bool)in_CY_flag);
  check_extension_and_start_config_menu(1);
  pbVar2 = &terminal_buffer_start;
  do {
    *pbVar2 = 0;
    pbVar2 = pbVar2 + 1;
    compare_de_hl_registers();
  } while (in_CY_flag == '\0');
  _saved_vscyn_isr_address = ISR_VSCYN_RETURN_ADDRESS;
  ISR_VSCYN_RETURN_ADDRESS = 0x228d;
  _saved_dispatch_function_ptr = dispatch_function_pointer_2;
  _default_handler_address = 0x20c9;
  create_bit_pattern_display();
  _system_config_pointer = &UNK_ram_3921;
  _display_function_pointer = send_form_feed_sequence;
  _config_template_pointer = &more_config_data;
  load_config_from_nvram();
  sw_printer_mode = 1;
  keyboard_handler_function_pointer = 0x2239;
  configure_display_baud_settings();
  initialize_display_registers();
  initialize_terminal_display_mode();
  _terminal_counter_value = 100;
  interrupt_service_routine_handler = 0x22a0;
                    // CRITICAL: Initialize terminal configuration during main loop startup
  reset_display_state_and_configure();
  _nvram_test_current_address = 0x42a0;
  dispatch_function_pointer_2 = 0x227d;
  keyboard_communication_handler = 0x22d1;
  keyboard_test_handler = 0x22d1;
  keyboard_data_handler = 0x22ca;
  cVar1 = save_config_to_nvram();
  if (cVar1 == 'R') {
    nvram_reset_flag = 1;
    _nvram_reset_address = 0x5000;
  }
  _input_handler_address = 0x209d;
  _main_loop_function_pointer = (code *)0x20cd;
  if (keyboard_init_command_1 != 0) {
    send_keyboard_command_with_retry(0x23);
  }
  if (keyboard_init_command_2 != 0) {
    send_keyboard_command_with_retry(0x30);
  }
  _character_handler_function_pointer = 0x21ba;
  _terminal_buffer_start = _terminal_buffer_copy;
                    // WARNING: Could not recover jumptable at 0x20cc. Too many branches
                    // WARNING: Treating indirect jump as call
  (*_main_loop_function_pointer)();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void setup_display_mode_handlers(void)

{
  current_cursor_column = 0x48;
  if (cursor_wrap_enabled_flag == 0) {
    if (sw_printer_speed == '\x02') {
      display_attribute_counter = thunk_set_display_attribute_and_trigger_refresh(0);
    }
    _terminal_buffer_copy = 0x215f;
    _terminal_buffer_start = 0x215f;
    return;
  }
  _terminal_buffer_copy = 0x21d4;
  _terminal_buffer_start = 0x21d4;
  return;
}



undefined1 atomic_read_and_clear_byte(void)

{
  undefined1 uVar1;
  undefined1 *in_HL;
  
  disableMaskableInterrupts();
  uVar1 = *in_HL;
  *in_HL = 0;
  enableMaskableInterrupts();
  return uVar1;
}



void set_attribute_processing_mode(void)

{
  undefined1 uVar1;
  undefined1 uVar2;
  
                    // TDV 2215 Attribute Processing Mode - Handles graphic rendition based on GRM
                    // switch setting (4.2.3). ATTR mode: attributes occupy character positions (SO
                    // Y SI sequences). UNDERLINE mode: SO/SI codes control underline. SGR mode:
                    // uses CSI sequences for character-by-character graphic rendition control.
  uVar2 = 0;
  if (control_status_flag != 0) {
    return;
  }
  uVar1 = 8;
  do {
    uVar1 = check_display_attribute_port(uVar1);
  } while ((bool)uVar2);
  control_status_flag = 1;
  return;
}



undefined1 escape_sequence_parser(void)

{
  undefined2 in_AF;
  undefined1 in_Z_flag;
  
                    // TDV 2215 Escape Sequence Parser - Processes ESC sequences per ECMA-6/35/48
                    // standards. In TDV 2115 mode: ignores ESC except ESC Q (enables Extended
                    // Control). In extended mode: handles C1 controls (ESC 40-5F), CSI sequences
                    // (ESC [), three-character sequences (DWL/SWL), and device control strings (DCS
                    // for PUSH-key loading).
  compare_de_hl_registers();
  if ((bool)in_Z_flag) {
    control_character_handler();
  }
  return (char)((ushort)in_AF >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void control_character_handler(void)

{
  char cVar1;
  byte bVar2;
  
  disableMaskableInterrupts();
  _main_loop_function_pointer = 0x20cd;
  enableMaskableInterrupts();
  cVar1 = atomic_read_and_clear_byte();
  if (cVar1 == '\x11') {
    cVar1 = serial_tx_complete_interrupt_handler();
  }
  cVar1 = cVar1 == '\x13';
  if ((bool)cVar1) {
    serial_state_advance_to_busy();
  }
  atomic_read_and_clear_byte();
  if (cVar1 == '\0') {
    configure_terminal_from_table();
  }
  atomic_read_and_clear_byte();
  if (cVar1 == '\0') {
    configure_terminal_mode_settings();
  }
  bVar2 = atomic_read_and_clear_byte();
  if ((bVar2 & 0x7f) != 0) {
    set_attribute_processing_mode();
  }
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void special_character_processor(byte param_1)

{
                    // TDV 2215 Special Character Processor - Processes extended character set
                    // including line drawing characters, histogram symbols, numeric
                    // sub/superscript, and plot characters. Part of the 256-character support that
                    // extends the basic TDV 2115 character set. Handles special display attributes
                    // and character rendering.
  if (0xaf < param_1) {
    high_character_dispatch_table();
    return;
  }
  _input_handler_address = &DAT_ram_581c;
  load_push_key_string_from_nvram(param_1 & 0xf);
                    // WARNING: Could not recover jumptable at 0x20cc. Too many branches
                    // WARNING: Treating indirect jump as call
  (*_main_loop_function_pointer)();
  return;
}



void execute_control_character(void)

{
  control_character_dispatcher();
  return;
}



byte terminal_character_output(byte character_to_output)

{
  byte processed_character;
  undefined1 should_continue_processing;
  
                    // TDV 2215 Terminal Character Output - Handles display of 256-character set
                    // including ASCII (20-7F), control codes (00-1F), and semigraphic characters
                    // (line drawing, histogram, subscript, superscript, plot). Processes graphic
                    // rendition based on GRM switch: attributes (ATTR), underline mode (UNDERLINE),
                    // or SGR sequences. Supports double-width characters and transparent mode
                    // display.
  current_output_character = character_to_output;
  if (cursor_wrap_enabled_flag != 0) {
    write_character_with_cursor_wrap(3);
  }
  do {
    should_continue_processing = 0;
    processed_character = current_output_character;
  } while (serial_transmission_busy_flag != 0);
  do {
    escape_sequence_parser(processed_character);
    processed_character = serial_start_transmission_if_ready();
  } while ((bool)should_continue_processing);
  serial_transmission_busy_flag = serial_transmission_config;
  return current_output_character;
}



void send_escape_character(void)

{
                    // Sends ESC character (0x1B) to terminal output. Used as building block for
                    // ANSI escape sequences. Called by special character processor for escape
                    // sequence generation.
  terminal_character_output(0x1b);
  return;
}



void send_escape_bracket_sequence(void)

{
                    // Sends ESC[ sequence (0x1B 0x5B) to terminal output. Generates ANSI escape
                    // sequence prefix for cursor movement, screen control, and formatting commands.
                    // Essential for terminal control sequences.
  send_escape_character();
  terminal_character_output(0x5b);
  return;
}



undefined1 check_backspace_enable_flag(void)

{
  undefined1 uVar1;
  undefined2 in_AF;
  
                    // Checks backspace_enable_flag to determine if backspace operations are
                    // allowed. Returns early if backspace is disabled. Used by special character
                    // processor to control backspace behavior in different terminal modes.
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (sw_printer_handshake != 0) {
    return uVar1;
  }
  return uVar1;
}



void high_character_dispatch_table(byte param_1)

{
  short in_HL;
  
                    // WARNING: Could not recover jumptable at 0x25b3. Too many branches
                    // WARNING: Treating indirect jump as call
  (**(code **)(in_HL + (ushort)(byte)((param_1 & 0xf) * '\x02')))(param_1);
  return;
}



void control_character_dispatcher(byte param_1)

{
  short in_HL;
  
                    // CONTROL CHARACTER DISPATCHER: Dual dispatch system for control chars
                    // (0x00-0x1F) and high chars (0xB0+ lower 4 bits)
                    // WARNING: Could not recover jumptable at 0x25b3. Too many branches
                    // WARNING: Treating indirect jump as call
  (**(code **)(in_HL + (ushort)(byte)((param_1 & 0x1f) * '\x02')))(param_1);
  return;
}



byte terminal_configuration_menu_and_keyboard_handler(void)

{
  byte bVar1;
  undefined2 in_AF;
  byte bVar2;
  byte bVar3;
  undefined2 uVar4;
  undefined2 uVar5;
  byte bVar6;
  byte bVar7;
  short sVar8;
  char cVar9;
  undefined1 uVar10;
  byte bVar11;
  char cVar12;
  bool bVar13;
  undefined1 uVar14;
  
                    // TDV 2215 Configuration Menu Handler - Implements soft-switch setup as per
                    // section 5. Accessed via MODE key (double press with SHIFT). Manages three
                    // switch categories: Convenience (cursor type, key click, etc.), Function
                    // (time-out, graphic rendition, etc.), Communication (baud rate, parity, etc.).
                    // Provides permanent/temporary switch management with NVRAM storage.
  bVar2 = (byte)((ushort)in_AF >> 8);
  cVar12 = bVar2 == 1;
  bVar3 = bVar2;
  if (!(bool)cVar12) {
    configure_display_baud_settings();
  }
  if (cVar12 == '\0') {
    di_push_all();
  }
  bVar1 = keyboard_init_command_2;
  bVar11 = system_mode_state;
  bVar7 = sw_vertical_editing_mode;
  uVar4 = CONCAT11(bVar3,1);
  if (terminal_mode_selector != 0) {
    uVar4 = CONCAT11(bVar3,2);
  }
LAB_ram_25d7:
  set_vsync_flag_and_clear_status_bit4();
  clear_display_memory_range();
  cVar12 = 0;
  thunk_set_display_attribute_and_trigger_refresh(0);
  refresh_cursor_and_display();
  refresh_cursor_and_sync();
  do {
    validate_af_with_interrupt_disable();
  } while ((bool)cVar12);
  do {
    poll_keyboard_input_buffer();
  } while (cVar12 == '\0');
  do {
    send_keyboard_init_command();
  } while ((bool)cVar12);
  sVar8 = 0x600;
  do {
    do {
      poll_keyboard_input_buffer();
      cVar9 = (char)((ushort)sVar8 >> 8);
    } while ((bool)cVar12);
    write_char_to_video_memory();
    advance_cursor_position();
    sVar8 = (ushort)(byte)(cVar9 - 1U) << 8;
  } while ((byte)(cVar9 - 1U) != 0);
  thunk_configure_display_control_port_and_wait_ready();
  bVar13 = (char)uVar4 == '\0';
  uVar5 = uVar4;
  refresh_cursor_and_display();
  cVar12 = '\x04';
  if ((!bVar13) && (cVar12 = '\x05', (char)uVar5 != '\x01')) {
    cVar12 = '\x06';
  }
  do {
    refresh_cursor_and_sync();
    cVar12 = cVar12 + -1;
  } while (cVar12 != '\0');
  refresh_cursor_and_sync();
  refresh_cursor_and_sync();
LAB_ram_2660:
  uVar14 = 0xf6 < (byte)((ushort)uVar4 >> 8);
  fill_display_region_with_pattern(3);
  update_cursor_memory_addresses();
LAB_ram_2673:
  do {
    while( true ) {
      while( true ) {
        while( true ) {
          no_operation_placeholder();
          update_cursor_memory_addresses();
          set_vsync_flag_and_clear_status_bit4();
          do {
            bVar3 = poll_keyboard_input_buffer();
          } while ((bool)uVar14);
          bVar6 = (byte)((ushort)uVar4 >> 8);
          sVar8 = (ushort)bVar6 << 8;
          uVar14 = bVar3 < 0xb2;
          if (bVar3 != 0xb2) break;
          while( true ) {
            bVar3 = (char)((ushort)sVar8 >> 8) + 1;
            sVar8 = (ushort)bVar3 << 8;
            bVar13 = bVar3 == 0;
            menu_navigation_dispatcher();
            if ((bool)uVar14) break;
            if (!bVar13) {
              uVar4 = CONCAT11((char)((ushort)sVar8 >> 8),(char)uVar4);
              goto LAB_ram_264c;
            }
          }
        }
        uVar14 = bVar3 < 0xb1;
        if (bVar3 != 0xb1) break;
        while( true ) {
          bVar3 = (char)((ushort)sVar8 >> 8) - 1;
          sVar8 = (ushort)bVar3 << 8;
          bVar13 = bVar3 == 0;
          if (bVar13) break;
          menu_navigation_dispatcher();
          if (!bVar13) {
            uVar4 = CONCAT11((char)((ushort)sVar8 >> 8),(char)uVar4);
            goto LAB_ram_264c;
          }
        }
      }
      cVar12 = bVar3 == 0xb8;
      if ((bool)cVar12) goto code_r0x26b0;
                    // CONFIGURATION MODE TRIGGER: Character 0xed enters configuration mode
                    // CTRL-HELP sequence should generate 0xed character to reach this code
      if (bVar3 != 0xed) break;
      if ((char)uVar4 == '\0') {
        uVar14 = bVar6 == 0;
        if ((bVar6 != 1) || (check_and_display_error_message(), !(bool)uVar14)) {
          display_menu_option();
          process_and_display_template_text();
        }
      }
      else {
        enable_cursor_enable_signal();
        bVar3 = (byte)((ushort)uVar4 >> 8);
        if (bVar3 == 1) {
LAB_ram_27e2:
          display_configuration_submenu_and_handle_navigation();
          goto LAB_ram_25d7;
        }
        uVar14 = bVar3 < 2;
        if (bVar3 == 2) {
LAB_ram_27dc:
          check_and_display_error_message();
          if (!(bool)uVar14) goto LAB_ram_27e2;
        }
        else {
          uVar14 = bVar3 < 3;
          if (bVar3 == 3) {
            configuration_change_flag = 3;
            goto LAB_ram_27dc;
          }
          uVar14 = bVar3 < 4;
          if (bVar3 == 4) {
            check_and_display_warning_message();
            if (!(bool)uVar14) {
              push_key_menu_interface();
              goto LAB_ram_25d7;
            }
          }
          else {
            if (bVar3 == 5) {
              create_bit_pattern_display();
              goto LAB_ram_25d7;
            }
            uVar14 = bVar3 < 6;
            if (bVar3 == 6) {
              factory_reset_nvram();
              if (error_message_flag == 0) {
                return error_message_flag;
              }
              thunk_set_display_attribute_and_trigger_refresh(8);
              process_and_display_template_text();
              bVar3 = thunk_set_display_attribute_and_trigger_refresh(0);
              return bVar3;
            }
          }
        }
      }
    }
    uVar14 = bVar3 < 0xfc;
    if (bVar3 == 0xfc) {
      send_form_feed_sequence();
      goto LAB_ram_2673;
    }
                    // MODE SWITCHING HANDLER: Character 0xdb handles mode transitions
                    // Used for switching between different terminal operating modes
    uVar14 = bVar3 < 0xdb;
    if (bVar3 == 0xdb) {
      uVar14 = 0;
      cVar12 = (char)uVar4 == '\0';
      if ((bool)cVar12) {
        display_menu_option(1);
        if ((bool)cVar12) {
          save_config_to_nvram();
        }
        if (!(bool)uVar14) {
          display_menu_option(2);
          if (cVar12 != '\0') {
            load_config_from_nvram();
            uVar14 = 0;
            cVar12 = '\x01';
            configuration_change_flag = 0;
          }
          display_menu_option(3);
          if ((bool)cVar12) {
            save_bit_pattern_to_nvram();
          }
          if (!(bool)uVar14) {
            display_menu_option(4);
            if ((bool)cVar12) {
              create_bit_pattern_display();
            }
            clear_display_memory_range();
            disable_attribute_write_enable();
            enable_cursor_enable_signal();
            uVar14 = keyboard_init_command_2 < bVar1;
            if (keyboard_init_command_2 != bVar1) {
              if ((bool)uVar14) {
                do {
                  send_keyboard_init_command(0x20);
                } while ((bool)uVar14);
                cVar12 = '\b';
                do {
                  wait_vertical_sync();
                  cVar12 = cVar12 + -1;
                } while (cVar12 != '\0');
                poll_keyboard_input_buffer();
              }
              else {
                do {
                  send_keyboard_init_command();
                } while ((bool)uVar14);
              }
            }
            thunk_configure_display_control_port_and_wait_ready();
            if (configuration_change_flag != 0) {
              configuration_change_flag = 0;
              sw_line_local_status = sw_underline_representation;
              configure_display_baud_settings();
              di_push_all();
              configure_z80sio_channels();
              apply_terminal_configuration();
              if (sw_roll_type != 0) {
                system_mode_state = 2;
              }
              if (system_mode_state != bVar11) {
                reset_display_state_and_configure();
              }
              if (sw_vertical_editing_mode != bVar7) {
                reset_display_state_and_configure();
              }
            }
            return bVar2;
          }
        }
        clear_display_line();
        thunk_set_display_attribute_and_trigger_refresh(9);
        refresh_cursor_and_sync();
        do {
          send_keyboard_init_command(0x24);
          poll_keyboard_input_buffer();
        } while ((bool)uVar14);
        do {
                    // WARNING: Do nothing block with infinite loop
        } while( true );
      }
      uVar4 = 0x100;
      goto LAB_ram_25d7;
    }
    check_interrupt_flag_and_validate_af();
  } while( true );
code_r0x26b0:
  uVar10 = 1;
  menu_navigation_dispatcher();
  uVar14 = (undefined1)uVar4;
  uVar4 = CONCAT11(uVar10,uVar14);
  if (cVar12 != '\0') {
    uVar4 = CONCAT11(2,uVar14);
  }
LAB_ram_264c:
  no_operation_placeholder();
  fill_display_region_with_pattern(0);
  if ((char)uVar4 != '\0') {
    process_and_display_template_text();
  }
  goto LAB_ram_2660;
}



void check_and_display_error_message(void)

{
  if (error_message_flag == 0) {
    return;
  }
  thunk_set_display_attribute_and_trigger_refresh(8);
  process_and_display_template_text();
  thunk_set_display_attribute_and_trigger_refresh(0);
  return;
}



void check_and_display_warning_message(void)

{
  if (warning_message_flag == 0) {
    return;
  }
  thunk_set_display_attribute_and_trigger_refresh(8);
  process_and_display_template_text();
  thunk_set_display_attribute_and_trigger_refresh(0);
  return;
}



byte menu_navigation_dispatcher(void)

{
  byte bVar1;
  char in_C;
  byte in_D;
  
  if ((byte)(in_C + 4U) < in_D) {
    return in_C + 4U;
  }
  if (in_D == 1) {
    bVar1 = wait_hardware_ready_and_read_memory(0xb);
    return bVar1;
  }
  if (in_D == 2) {
    bVar1 = display_menu_option(1);
    return bVar1;
  }
  if (in_D != 3) {
    if ((byte)(in_D - 4) != 0) {
      return in_D - 4;
    }
    bVar1 = display_menu_option(3);
    return bVar1;
  }
  bVar1 = display_menu_option(4);
  return bVar1;
}



void display_menu_option(char param_1)

{
  wait_hardware_ready_and_read_memory(param_1 + '\t');
  return;
}



void refresh_cursor_and_sync(void)

{
  update_cursor_memory_addresses();
  process_and_display_template_text();
  return;
}



void refresh_cursor_and_display(void)

{
  update_cursor_memory_addresses();
  display_formatted_string_with_templates();
  return;
}



byte calculate_character_attribute_code(void)

{
  if (BYTE_ram_5f2c == 0) {
    return 4;
  }
  if (BYTE_ram_5f2c < 0xe) {
    if (4 < BYTE_ram_5f2c) {
      return BYTE_ram_5f2c;
    }
    return BYTE_ram_5f2c - 1;
  }
  return BYTE_ram_5f2c;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

byte process_character_with_attributes(byte param_1)

{
  byte bVar1;
  undefined2 in_DE;
  undefined2 uVar2;
  byte bVar3;
  byte bVar4;
  bool bVar5;
  
  do {
                    // Processes character with display attributes based on ASCII range. Maps
                    // character ranges to attribute values: 0x20-0x2F→2, 0x30-0x3F→8,
                    // 0x40-0x4F→1, 0x50-0x5F→4, 0x60-0x6F→15, 0x70-0x7E→0. Handles
                    // character display with attribute generation and cursor management.
    if (param_1 < 0x50) {
      if (param_1 < 0x30) {
        if (param_1 < 0x20) {
          return param_1;
        }
        bVar1 = 2;
      }
      else if (param_1 < 0x40) {
        bVar1 = 8;
      }
      else {
        bVar1 = 1;
      }
LAB_ram_2b5c:
      write_char_to_video_memory();
      write_char_to_display_with_timing(0xf);
      uVar2 = _display_register_config;
      no_operation_placeholder();
      bVar4 = (byte)((ushort)in_DE >> 8);
      bVar3 = (byte)((ushort)uVar2 >> 8);
      bVar5 = bVar4 < bVar3;
      if (bVar4 == bVar3) {
        bVar5 = (byte)in_DE < (byte)uVar2;
      }
      if (bVar5) {
        _display_register_config = in_DE;
        display_register_zero = bVar1;
      }
      bVar1 = handle_cursor_wrap_and_scroll();
      return bVar1;
    }
    if (param_1 < 0x70) {
      if (param_1 < 0x60) {
        bVar1 = 4;
      }
      else {
        bVar1 = 0xf;
      }
      goto LAB_ram_2b5c;
    }
    if (param_1 < 0x7f) {
      bVar1 = 0;
      goto LAB_ram_2b5c;
    }
    if (param_1 < 0x80) {
      return param_1;
    }
    param_1 = param_1 & 0x7f;
  } while( true );
}



byte convert_ascii_hex_to_binary(byte param_1)

{
  if (0x66 < param_1) {
    return param_1;
  }
  if (param_1 < 0x61) {
    if (0x46 < param_1) {
      return param_1;
    }
    if (param_1 < 0x41) {
      if (0x39 < param_1) {
        return param_1;
      }
      if (param_1 < 0x30) {
        return param_1;
      }
      goto LAB_ram_2eb0;
    }
  }
  else {
    param_1 = param_1 - 0x20;
  }
  param_1 = param_1 - 7;
LAB_ram_2eb0:
  return param_1 - 0x30;
}



byte convert_ascii_digit_to_binary(byte param_1)

{
                    // Converts ASCII digit character (0x30-0x39) to binary value (0-9). Returns
                    // (char - 0x30) for digits, unchanged value for non-digits. Used for parsing
                    // numeric parameters in escape sequences and commands.
  if ((param_1 < 0x3a) && (0x2f < param_1)) {
    return param_1 - 0x30;
  }
  return param_1;
}



void cursor_movement_handler(void)

{
  byte bVar1;
  char in_C;
  char cVar2;
  undefined2 in_HL;
  char cVar3;
  
  bVar1 = wait_hardware_ready_and_read_memory();
  if ((bVar1 & 0xf0) == 0x10) {
    return;
  }
  cVar2 = (char)in_HL;
  cVar3 = (char)((ushort)in_HL >> 8);
  do {
    cVar2 = cVar2 + -1;
    if (cVar2 == '\0') {
      if (in_C == '\0') break;
      cVar3 = cVar3 + -1;
      cVar2 = 'P';
      if (cVar3 == '\0') break;
    }
    bVar1 = wait_hardware_ready_and_read_memory();
  } while ((bVar1 & 0xf0) != 0x10);
  update_cursor_addresses();
  display_fill_pattern_with_interrupt_check(1);
  return;
}



char get_default_tab_stop_count(void)

{
  if (tab_buffer_data != '\0') {
    return tab_buffer_data;
  }
  return '\x01';
}



undefined1 get_next_tab_stop_from_buffer(void)

{
  ushort uVar1;
  
                    // Gets next tab stop from circular buffer. Decrements tab_buffer_index and
                    // returns value from tab_buffer_data array. Returns 0xFF when buffer empty.
                    // Used for horizontal tab processing in terminal.
  if (tab_buffer_index != 0xff) {
    uVar1 = (ushort)tab_buffer_index;
    tab_buffer_index = tab_buffer_index - 1;
    return (&tab_buffer_data)[uVar1];
  }
  return 0xff;
}



char lookup_value_in_table(char param_1)

{
  char *in_HL;
  
  while( true ) {
    if (*in_HL == param_1) {
      return in_HL[1];
    }
    if (*in_HL == -1) break;
    in_HL = in_HL + 2;
  }
  return '\0';
}



void send_keyboard_command_with_retry(void)

{
  undefined1 in_CY_flag;
  
  do {
    send_keyboard_init_command();
  } while ((bool)in_CY_flag);
  return;
}



void process_control_flags_with_status_check(void)

{
  byte bVar1;
  byte bVar2;
  byte bVar3;
  ushort uVar4;
  undefined1 in_CY_flag;
  byte bVar5;
  
  do {
    check_hardware_status_port_bit2();
  } while ((bool)in_CY_flag);
  control_status_flag = 0;
  if (control_flags_status != 0) {
    uVar4 = 4;
    bVar5 = 0;
    bVar2 = control_flags_status;
    while (bVar3 = (char)uVar4 - 1, bVar3 != 0) {
      uVar4 = (ushort)bVar3;
      bVar1 = bVar2 & 1;
      bVar3 = bVar2 & 1;
      bVar2 = bVar2 >> 1 | bVar5 << 7;
      bVar5 = bVar3;
      if (bVar1 != 0) {
        do {
          check_hardware_status_port_bit2();
          bVar5 = bVar3;
        } while ((bool)bVar3);
      }
    }
    control_flags_status = 0;
    return;
  }
  return;
}



void process_control_flags_direct(void)

{
  byte bVar1;
  byte bVar2;
  byte bVar3;
  ushort uVar4;
  byte bVar5;
  
                    // Direct processing of control_flags_status bits without initial status check.
                    // Similar bit rotation processing as process_control_flags_with_status_check
                    // but skips initial hardware wait.
  if (control_flags_status != 0) {
    uVar4 = 4;
    bVar5 = 0;
    bVar2 = control_flags_status;
    while (bVar3 = (char)uVar4 - 1, bVar3 != 0) {
      uVar4 = (ushort)bVar3;
      bVar1 = bVar2 & 1;
      bVar3 = bVar2 & 1;
      bVar2 = bVar2 >> 1 | bVar5 << 7;
      bVar5 = bVar3;
      if (bVar1 != 0) {
        do {
          check_hardware_status_port_bit2();
          bVar5 = bVar3;
        } while ((bool)bVar3);
      }
    }
    control_flags_status = 0;
    return;
  }
  return;
}



void display_character_with_cursor_management(void)

{
  byte in_L;
  byte in_H;
  char cVar1;
  
  write_char_to_video_memory();
  check_auto_wrap_condition();
  cVar1 = current_cursor_column < 0x48;
  if (current_cursor_column == 0x48) {
    advance_cursor_one_character();
    if (cVar1 == '\0') {
      return;
    }
  }
  else {
    no_operation_placeholder();
    if (in_L < 0x4d) {
      advance_cursor_position();
      advance_cursor_position();
      return;
    }
  }
  if (sw_printer_code_format == 0) {
    return;
  }
  no_operation_placeholder();
  if (in_H < 0x19) {
    cursor_setup_for_movement();
    update_cursor_addresses();
    cursor_tab_advance();
    return;
  }
  if (scroll_disable_flag != 0) {
    return;
  }
  cursor_setup_for_movement();
  line_wrap_handler();
  reset_cursor_to_line_start();
  return;
}



void handle_cursor_wrap_and_scroll(void)

{
  byte in_L;
  byte in_H;
  char cVar1;
  
                    // Handles cursor wrapping and scrolling at line boundaries. Checks column
                    // position (0x48), manages auto-wrap, advances cursor, and handles scrolling
                    // when reaching bottom. Integrates with cursor_advance_enable_flag and
                    // scroll_disable_flag.
  check_auto_wrap_condition();
  cVar1 = current_cursor_column < 0x48;
  if (current_cursor_column == 0x48) {
    advance_cursor_one_character();
    if (cVar1 == '\0') {
      return;
    }
  }
  else {
    no_operation_placeholder();
    if (in_L < 0x4d) {
      advance_cursor_position();
      advance_cursor_position();
      return;
    }
  }
  if (sw_printer_code_format == 0) {
    return;
  }
  no_operation_placeholder();
  if (in_H < 0x19) {
    cursor_setup_for_movement();
    update_cursor_addresses();
    cursor_tab_advance();
    return;
  }
  if (scroll_disable_flag != 0) {
    return;
  }
  cursor_setup_for_movement();
  line_wrap_handler();
  reset_cursor_to_line_start();
  return;
}



void write_character_with_cursor_wrap(void)

{
  char in_CY_flag;
  
  thunk_set_display_attribute_and_trigger_refresh();
  write_char_to_video_memory();
  check_auto_wrap_condition();
  advance_cursor_position();
  if (in_CY_flag == '\0') {
    return;
  }
  conditional_line_feed_operation();
  reset_cursor_to_line_start();
  return;
}



void check_auto_wrap_condition(void)

{
  byte in_L;
  
  if ((display_refresh_control & 6) == 6) {
    return;
  }
  no_operation_placeholder();
  if (current_cursor_column != in_L) {
    return;
  }
  if (BYTE_ram_5f25 != 0) {
    return;
  }
  check_interrupt_flag_and_validate_af();
  return;
}



void line_wrap_handler(void)

{
  char cVar1;
  
                    // Manages line wrapping behavior based on line_feed_mode_flag. Handles
                    // conditional line feed, display refresh, and form feed modes. Supports
                    // different form feed behaviors including pattern filling based on
                    // AttributeRegisterValue.
  cVar1 = line_feed_mode_flag == 0;
  if ((bool)cVar1) {
    conditional_line_feed_operation();
  }
  if (cVar1 == '\0') {
    wait_refresh_cycles_and_clear();
  }
  if (sw_printer_speed == '\0') {
    initialize_display_registers();
    return;
  }
  if (sw_printer_speed != '\x01') {
    cursor_tab_advance();
    return;
  }
  if (AttributeRegisterValue == 0) {
    return;
  }
  fill_display_region_with_pattern();
  return;
}



void cursor_setup_for_movement(void)

{
                    // Prepares cursor for movement operations. Checks cursor_wrap_mode (value 2)
                    // and calls scan_and_output_display_memory for cursor positioning. Used before
                    // complex cursor movements.
  if (cursor_wrap_mode != 2) {
    return;
  }
  no_operation_placeholder();
  scan_and_output_display_memory();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void initialize_display_registers(void)

{
  display_register_zero = 0;
  _display_register_config = 0x101;
  return;
}



void cursor_tab_advance(void)

{
  char cVar1;
  byte bVar2;
  byte in_L;
  
                    // Advances cursor to next tab stop position based on form_feed_mode_flag.
                    // Handles status port reading (0x0E/0x0F values), attribute setting, and cursor
                    // positioning. Supports multiple tab advance modes and column boundary checks.
  if (sw_printer_speed != '\x02') {
    return;
  }
  no_operation_placeholder();
  cVar1 = read_status_port_low_nibble();
  if (cVar1 != '\x0e') {
    cVar1 = display_attribute_counter < 0x10;
    if ((bool)cVar1) {
      thunk_set_display_attribute_and_trigger_refresh();
    }
    if (cVar1 == '\0') {
      disable_attribute_write_enable();
    }
    if (current_cursor_column == 0x48) {
      return;
    }
    if (in_L == 1) {
      in_L = 2;
    }
    update_cursor_addresses(in_L >> 1);
    current_cursor_column = 0x48;
    return;
  }
  if (current_cursor_column == 0x48) {
    thunk_set_display_attribute_and_trigger_refresh(0xe);
    bVar2 = in_L << 1 | in_L >> 7;
    if (0x4d < bVar2) {
      bVar2 = 0x4e;
    }
    update_cursor_addresses(bVar2);
    current_cursor_column = 0x40;
    return;
  }
  no_operation_placeholder();
  if (in_L == 1) {
    advance_cursor_position();
    return;
  }
  if (in_L != 0x50) {
    return;
  }
  update_cursor_addresses();
  return;
}



void set_cursor_to_end_position(void)

{
  byte in_L;
  
                    // Sets cursor to end position (column 0x48). Adjusts input value if 1 to 2,
                    // then shifts right by 1 for cursor address calculation. Used for positioning
                    // cursor at line end.
  if (in_L == 1) {
    in_L = 2;
  }
  update_cursor_addresses(in_L >> 1);
  current_cursor_column = 0x48;
  return;
}



void set_cursor_advanced_position(void)

{
  byte bVar1;
  byte in_L;
  
                    // Sets cursor to advanced position (column 0x40) with attribute refresh.
                    // Rotates input value left, clamps to 0x4E maximum, and updates cursor
                    // addresses. Used for advanced cursor positioning.
  thunk_set_display_attribute_and_trigger_refresh();
  bVar1 = in_L << 1 | in_L >> 7;
  if (0x4d < bVar1) {
    bVar1 = 0x4e;
  }
  update_cursor_addresses(bVar1);
  current_cursor_column = 0x40;
  return;
}



void initialize_terminal_display_mode(void)

{
  byte bVar1;
  byte bVar2;
  char cVar3;
  char cVar4;
  byte bVar5;
  byte bVar6;
  
                    // TDV 2215 Display Mode Initialization - Sets up character generator for 9x14
                    // dot cell (7x9 matrix). Configures 256-character support including standard
                    // ASCII (20-7F), semigraphic set (line drawing, histogram, subscript,
                    // superscript, plot characters), and double-width character capability.
                    // Initializes graphic rendition attributes and cursor type display.
  disable_attribute_write_enable();
  display_attribute_register = calculate_character_attribute_code();
  DAT_ram_581b = 0;
  setup_display_mode_handlers();
  BYTE_ram_5811 = (~sw_bell + 0xd) * '\x02';
  if ((sw_printer_handshake == 0) && (sw_printer_speed == '\x02')) {
    sw_printer_speed = '\0';
  }
  apply_terminal_configuration();
  bVar5 = 0;
  if (control_status_flag != 0) {
    set_display_attribute_bit();
  }
  cVar3 = '\x04';
  cVar4 = '\0';
  bVar2 = control_flags_status;
  while (cVar3 = cVar3 + -1, cVar3 != '\0') {
    cVar4 = cVar4 + '\x01';
    bVar1 = bVar2 & 1;
    bVar6 = bVar2 & 1;
    bVar2 = bVar2 >> 1 | bVar5 << 7;
    bVar5 = bVar6;
    if (bVar1 != 0) {
      set_display_attribute_bit(cVar4);
      bVar5 = bVar6;
    }
  }
  return;
}



void set_display_attribute_bit(void)

{
  undefined1 in_CY_flag;
  
  do {
    check_display_attribute_port();
  } while ((bool)in_CY_flag);
  return;
}



char convert_digit_to_ascii(byte param_1)

{
  for (; 9 < param_1; param_1 = param_1 - 10) {
  }
  return param_1 + 0x30;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

void check_extension_and_start_config_menu(void)

{
  ushort uVar1;
  undefined2 in_AF;
  byte bVar2;
  byte *pbVar3;
  char cVar4;
  byte *pbVar5;
  char in_Z_flag;
  ushort in_stack_00000000;
  
                    // CHECK EXTENSION AND START CONFIG MENU
                    // Checks for hardware extension conditions and optionally starts configuration
                    // menu
                    // Only calls terminal_configuration_menu_and_keyboard_handler under specific
                    // conditions
                    // ISSUE: Conditions not met, so keyboard handler never starts
  pbVar5 = &ISR_RETURN_ADDRESS;
  pbVar3 = &BYTE_ram_58ff;
  do {
    *pbVar3 = *pbVar5;
    pbVar5 = pbVar5 + 1;
    pbVar3 = pbVar3 + 1;
    compare_de_hl_registers();
  } while (in_Z_flag == '\0');
  _system_config_pointer = &UNK_ram_3921;
  _config_template_pointer = &more_config_data;
  load_config_from_nvram();
  create_bit_pattern_display();
  cVar4 = (char)((ushort)in_AF >> 8);
  _BYTE_ram_58fd = (char *)(in_stack_00000000 & 0xff00 ^ 0xe000);
  if ((*_BYTE_ram_58fd != -0x80) || (_BYTE_ram_58fd[1] != -0x80)) {
    _BYTE_ram_58fd = (char *)0x0;
    return;
  }
  uVar1 = (ushort)_BYTE_ram_58fd >> 8;
  bVar2 = save_config_to_nvram();
  if (bVar2 != (byte)((byte)uVar1 ^ 0xe0)) {
    return;
  }
                    // CHECK B REGISTER FOR KEYBOARD HANDLER ACTIVATION
                    // DCR B - Decrements B register
                    // RZ - Returns if B becomes zero (preventing keyboard handler from starting)
                    // DEBUG: Check what value is in B register at this point
  if (cVar4 == '\x01') {
    return;
  }
                    // CONDITIONAL CALL TO KEYBOARD HANDLER
                    // Only executes if B register != 0 after DCR B at 0x3818
                    // This is where the main keyboard processing should start but conditions not
                    // met
  terminal_configuration_menu_and_keyboard_handler(6);
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 factory_reset_nvram(void)

{
  undefined1 uVar1;
  undefined1 uVar2;
  byte *pbVar3;
  byte bVar4;
  short sVar5;
  byte *pbVar6;
  char cVar7;
  undefined1 in_CY_flag;
  
                    // NVRAM Factory Reset / Initialization Function
                    // This function appears to write default configuration to NVRAM
                    // Called from configuration menu option 6 (0xed then option 6)
                    // This is likely the factory reset function you're looking for!
  write_byte_to_nvram(terminal_mode_selector);
  if (!(bool)in_CY_flag) {
    sVar5 = 0;
    while( true ) {
      uVar1 = save_config_to_nvram();
      uVar2 = save_config_to_nvram(uVar1,uVar1);
      write_byte_to_nvram(uVar1);
      if ((bool)in_CY_flag) break;
      write_byte_to_nvram(uVar2);
      if ((bool)in_CY_flag) break;
      sVar5 = sVar5 + 1;
      bVar4 = (byte)sVar5;
      in_CY_flag = 0xb < bVar4;
      if (bVar4 == 0xb) {
        sVar5 = 0x10;
      }
      else {
        in_CY_flag = 0x1a < bVar4;
        cVar7 = bVar4 == 0x1a;
        if ((bool)cVar7) {
          pbVar6 = &ISR_RETURN_ADDRESS;
          pbVar3 = &BYTE_ram_58ff;
          do {
            *pbVar6 = *pbVar3;
            pbVar6 = pbVar6 + 1;
            pbVar3 = pbVar3 + 1;
            compare_de_hl_registers();
          } while (cVar7 == '\0');
          _boot_progress_marker = CONCAT11((char)((ushort)_BYTE_ram_58fd >> 8),7);
          return 6;
        }
      }
    }
  }
  clear_display_line();
  thunk_set_display_attribute_and_trigger_refresh(9);
  refresh_cursor_and_sync();
  do {
    send_keyboard_init_command(0x24);
    poll_keyboard_input_buffer();
  } while ((bool)in_CY_flag);
  do {
                    // WARNING: Do nothing block with infinite loop
  } while( true );
}



undefined1 scan_and_output_display_memory(void)

{
  undefined2 in_AF;
  char cVar1;
  byte bVar2;
  byte bVar3;
  ushort uVar4;
  short sVar5;
  byte bVar6;
  undefined2 in_HL;
  char cVar7;
  
                    // Scans display memory and outputs printable characters with CR/LF formatting.
                    // Reads status port for control (0x0E/0x0F), filters characters (0x21-0x7F),
                    // and adds line breaks. Used for screen printing/capture functionality.
  cVar7 = (char)((ushort)in_HL >> 8);
  bVar6 = 1;
  sVar5 = (ushort)(byte)(cVar7 + 1) << 8;
  do {
    uVar4 = 0;
    do {
      bVar3 = (byte)uVar4;
      cVar1 = read_status_port_low_nibble();
      if (cVar1 == '\x0f') goto LAB_ram_3f3c;
      if (cVar1 == '\x0e') {
        if (bVar3 == 0) {
          cVar1 = wait_hardware_ready_and_read_memory();
          if (cVar1 != ' ') {
            uVar4 = 1;
            goto LAB_ram_3f2f;
          }
        }
        else {
          if (bVar3 != 1) {
            uVar4 = (ushort)(byte)(bVar3 - 1);
            goto LAB_ram_3f2f;
          }
          bVar3 = 2;
        }
LAB_ram_3f3c:
        uVar4 = (ushort)bVar3;
      }
      else {
        uVar4 = 0;
LAB_ram_3f2f:
        bVar2 = wait_hardware_ready_and_read_memory();
        bVar3 = (byte)uVar4;
        if ((bVar2 < 0x21) || (0x7f < bVar2)) goto LAB_ram_3f3c;
        send_spaces_to_output();
        store_character_to_buffer_atomic();
      }
      bVar6 = bVar6 + 1;
    } while (bVar6 < 0x51);
    cVar7 = cVar7 + '\x01';
    bVar6 = 1;
    store_character_to_buffer_atomic(0xd);
    store_character_to_buffer_atomic(10);
    if (cVar7 == (char)((ushort)sVar5 >> 8)) {
      return (char)((ushort)in_AF >> 8);
    }
  } while( true );
}



undefined1 send_form_feed_sequence(void)

{
  undefined2 in_AF;
  
                    // Sends form feed sequence based on form_feed_control_flags. Sends form feed
                    // (0x0C) conditionally, carriage return (0x0D), calls row scanner, and
                    // optionally another form feed. Used for page formatting.
  if ((form_feed_control_flags & 1) != 0) {
    store_character_to_buffer_atomic(0xc);
  }
  store_character_to_buffer_atomic(0xd);
  scan_display_memory_by_rows();
  if ((form_feed_control_flags & 2) != 0) {
    store_character_to_buffer_atomic(0xc);
  }
  return (char)((ushort)in_AF >> 8);
}



undefined1 scan_display_memory_by_rows(void)

{
  undefined2 in_AF;
  char cVar1;
  byte bVar2;
  ushort uVar3;
  undefined2 in_DE;
  byte bVar4;
  undefined2 in_HL;
  
  do {
    uVar3 = 0;
    do {
      bVar4 = (byte)uVar3;
      cVar1 = read_status_port_low_nibble();
      if (cVar1 == '\x0f') goto LAB_ram_3f3c;
      if (cVar1 == '\x0e') {
        if (bVar4 == 0) {
          cVar1 = wait_hardware_ready_and_read_memory();
          if (cVar1 != ' ') {
            uVar3 = 1;
            goto LAB_ram_3f2f;
          }
        }
        else {
          if (bVar4 != 1) {
            uVar3 = (ushort)(byte)(bVar4 - 1);
            goto LAB_ram_3f2f;
          }
          bVar4 = 2;
        }
LAB_ram_3f3c:
        uVar3 = (ushort)bVar4;
      }
      else {
        uVar3 = 0;
LAB_ram_3f2f:
        bVar2 = wait_hardware_ready_and_read_memory();
        bVar4 = (byte)uVar3;
        if ((bVar2 < 0x21) || (0x7f < bVar2)) goto LAB_ram_3f3c;
        send_spaces_to_output();
        store_character_to_buffer_atomic();
      }
      cVar1 = (char)((ushort)in_HL >> 8);
      bVar4 = (char)in_HL + 1;
      in_HL = CONCAT11(cVar1,bVar4);
    } while (bVar4 < 0x51);
    in_HL = CONCAT11(cVar1 + '\x01',1);
    store_character_to_buffer_atomic(0xd);
    store_character_to_buffer_atomic(10);
    if ((char)((ushort)in_HL >> 8) == (char)((ushort)in_DE >> 8)) {
      return (char)((ushort)in_AF >> 8);
    }
  } while( true );
}


/*
Unable to decompile 'send_spaces_to_output'
Cause: 
Low-level Error: Overlapping input varnodes
*/


undefined1 store_character_to_buffer_atomic(void)

{
  ushort in_AF;
  ushort uVar1;
  bool bVar2;
  
                    // Stores character to output buffer atomically. Checks
                    // buffer_output_disable_flag and calls store_character_to_buffer_atomic if
                    // enabled. Used for all character output with buffer control.
  uVar1 = in_AF & 0xff00;
  do {
    bVar2 = false;
    if (buffer_output_disable_flag != 0) break;
    store_character_to_buffer_atomic((char)(uVar1 >> 8));
  } while (bVar2);
  return (char)(in_AF >> 8);
}



void reset_display_state_and_configure(void)

{
  display_config_register = 0;
  build_terminal_config_from_table();
  return;
}



void enable_terminal_control_and_configure(void)

{
                    // Enables terminal control mode and configures from table. Sets
                    // terminal_control_flags to 1 and calls build_terminal_config_from_table. Used
                    // for activating terminal control features.
  terminal_control_flags = 1;
  build_terminal_config_from_table();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 configure_terminal_mode_settings(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  undefined2 uVar2;
  ushort uVar3;
  byte *pbVar4;
  char cVar5;
  
                    // TDV 2215 Terminal Mode Configuration - Configures terminal operating modes
                    // based on soft-switch settings. Handles Extended Control switch (EC) for TDV
                    // 2115 compatibility vs extended operation, Graphic Rendition Mode
                    // (ATTR/UNDERLINE/SGR), Send-Receive Mode (SIMULTANEOUS/TRANSPARENT), and other
                    // function switches from sections 4.2-4.3.
  uVar2 = 0x8000;
  if (terminal_control_flags == 0) {
    uVar1 = build_terminal_config_from_table();
    return uVar1;
  }
  uVar1 = 0;
  cVar5 = (expected_control_port2_value_maybe & 1) == 0;
  do {
    if ((bool)cVar5) {
      check_hardware_status_port_bit2();
    }
    if (cVar5 == '\0') {
      check_display_attribute_port();
    }
  } while ((bool)uVar1);
  pbVar4 = &DAT_ram_387e;
  do {
    if (*pbVar4 == 0xff) {
LAB_ram_40c0:
      return (char)((ushort)in_AF >> 8);
    }
    if (((*pbVar4 & (display_config_register | (byte)((ushort)uVar2 >> 8))) == pbVar4[1]) &&
       ((pbVar4[2] &
        ((sw_vertical_editing_mode & 1) << 2 |
        expected_control_port2_value_maybe & 0x2b |
        (byte)(system_mode_state << 7) >> 1 | (system_mode_state >> 1) << 7 | (byte)uVar2)) ==
        pbVar4[3])) {
      uVar3 = CONCAT11(pbVar4[4],pbVar4[5]);
      _terminal_config_word = uVar3;
      if ((pbVar4[4] & 0x40) != 0) {
        disable_z80sio_channel_a_receive();
      }
      if ((uVar3 & 0x1000) != 0) {
        reset_keyboard_buffers_and_update_uart();
      }
      set_host_port_status((byte)(uVar3 >> 8) & 0xa2);
      if ((uVar3 & 0xc0) == 0) {
        send_keyboard_command_with_retry_alt(0xf);
      }
      else if (((byte)uVar3 & 0xc0) == 0x40) {
        send_keyboard_command_with_retry_alt(7);
      }
      else {
        send_keyboard_command_with_retry_alt(0x17);
      }
      if ((uVar3 & 0x30) == 0) {
        send_keyboard_command_with_retry_alt(0xd);
      }
      else if (((byte)uVar3 & 0x30) == 0x10) {
        send_keyboard_command_with_retry_alt(5);
      }
      else {
        send_keyboard_command_with_retry_alt(0x15);
      }
      if ((uVar3 & 0x100) != 0) {
        configure_interrupt_handlers(2);
        terminal_control_flags = 0;
      }
      system_mode_state = (byte)(uVar3 >> 10) & 3;
      display_config_register = (byte)uVar3 & 0xf;
      goto LAB_ram_40c0;
    }
    pbVar4 = pbVar4 + 6;
  } while( true );
}



void configure_terminal_from_table(void)

{
                    // Configures terminal settings from configuration table. Wrapper for
                    // build_terminal_config_from_table without setting control flags. Used by
                    // control character handler for configuration updates.
  build_terminal_config_from_table();
  return;
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 build_terminal_config_from_table(void)

{
  undefined2 in_DE;
  ushort uVar1;
  byte *pbVar2;
  char cVar3;
  undefined1 uVar4;
  undefined2 in_stack_00000004;
  
                    // BUILD TERMINAL CONFIG FROM TABLE - Scans configuration table (0x387e) to find
                    // matching display/hardware configuration, then sets terminal_config_word and
                    // applies all terminal settings. This is where system_mode_state gets set based
                    // on table lookup.
  uVar4 = 0;
  cVar3 = (expected_control_port2_value_maybe & 1) == 0;
  do {
    if ((bool)cVar3) {
      check_hardware_status_port_bit2();
    }
    if (cVar3 == '\0') {
      check_display_attribute_port();
    }
  } while ((bool)uVar4);
  pbVar2 = &DAT_ram_387e;
  do {
    if (*pbVar2 == 0xff) {
LAB_ram_40c0:
      return (char)((ushort)in_stack_00000004 >> 8);
    }
    if (((*pbVar2 & (display_config_register | (byte)((ushort)in_DE >> 8))) == pbVar2[1]) &&
       ((pbVar2[2] &
        ((sw_vertical_editing_mode & 1) << 2 |
        expected_control_port2_value_maybe & 0x2b |
        (byte)(system_mode_state << 7) >> 1 | (system_mode_state >> 1) << 7 | (byte)in_DE)) ==
        pbVar2[3])) {
      uVar1 = CONCAT11(pbVar2[4],pbVar2[5]);
      _terminal_config_word = uVar1;
      if ((pbVar2[4] & 0x40) != 0) {
        disable_z80sio_channel_a_receive();
      }
      if ((uVar1 & 0x1000) != 0) {
        reset_keyboard_buffers_and_update_uart();
      }
      set_host_port_status((byte)(uVar1 >> 8) & 0xa2);
      if ((uVar1 & 0xc0) == 0) {
        send_keyboard_command_with_retry_alt(0xf);
      }
      else if (((byte)uVar1 & 0xc0) == 0x40) {
        send_keyboard_command_with_retry_alt(7);
      }
      else {
        send_keyboard_command_with_retry_alt(0x17);
      }
      if ((uVar1 & 0x30) == 0) {
        send_keyboard_command_with_retry_alt(0xd);
      }
      else if (((byte)uVar1 & 0x30) == 0x10) {
        send_keyboard_command_with_retry_alt(5);
      }
      else {
        send_keyboard_command_with_retry_alt(0x15);
      }
      if ((uVar1 & 0x100) != 0) {
        configure_interrupt_handlers(2);
        terminal_control_flags = 0;
      }
      system_mode_state = (byte)(uVar1 >> 10) & 3;
      display_config_register = (byte)uVar1 & 0xf;
      goto LAB_ram_40c0;
    }
    pbVar2 = pbVar2 + 6;
  } while( true );
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 apply_terminal_configuration(void)

{
  undefined2 in_DE;
  ushort uVar1;
  byte *pbVar2;
  byte *in_HL;
  undefined1 in_Z_flag;
  undefined2 in_stack_00000004;
  
                    // Applies terminal configuration from table lookup. Searches configuration
                    // table based on DE register values, sets terminal_config_word, configures
                    // keyboard commands, interrupt handlers, and system modes. Controls Z80SIO,
                    // keyboard, and display settings.
  while (!(bool)in_Z_flag) {
    while( true ) {
      if (in_HL[3] == 0xff) goto LAB_ram_40c0;
      if ((in_HL[3] & (byte)((ushort)in_DE >> 8)) == in_HL[4]) break;
      in_HL = in_HL + 6;
    }
    pbVar2 = in_HL + 5;
    in_HL = in_HL + 6;
    in_Z_flag = (*pbVar2 & (byte)in_DE) == *in_HL;
  }
  uVar1 = CONCAT11(in_HL[1],in_HL[2]);
  _terminal_config_word = uVar1;
  if ((in_HL[1] & 0x40) != 0) {
    disable_z80sio_channel_a_receive();
  }
  if ((uVar1 & 0x1000) != 0) {
    reset_keyboard_buffers_and_update_uart();
  }
  set_host_port_status((byte)(uVar1 >> 8) & 0xa2);
  if ((uVar1 & 0xc0) == 0) {
    send_keyboard_command_with_retry_alt(0xf);
  }
  else if (((byte)uVar1 & 0xc0) == 0x40) {
    send_keyboard_command_with_retry_alt(7);
  }
  else {
    send_keyboard_command_with_retry_alt(0x17);
  }
  if ((uVar1 & 0x30) == 0) {
    send_keyboard_command_with_retry_alt(0xd);
  }
  else if (((byte)uVar1 & 0x30) == 0x10) {
    send_keyboard_command_with_retry_alt(5);
  }
  else {
    send_keyboard_command_with_retry_alt(0x15);
  }
  if ((uVar1 & 0x100) != 0) {
    configure_interrupt_handlers(2);
    terminal_control_flags = 0;
  }
  system_mode_state = (byte)(uVar1 >> 10) & 3;
  display_config_register = (byte)uVar1 & 0xf;
LAB_ram_40c0:
  return (char)((ushort)in_stack_00000004 >> 8);
}



// WARNING: Globals starting with '_' overlap smaller symbols at the same address

undefined1 apply_terminal_configuration(void)

{
  undefined2 in_AF;
  ushort uVar1;
  
                    // APPLY TERMINAL CONFIGURATION - Processes terminal configuration word (0x58a6)
                    // and configures all system components: Z80 SIO channels, keyboard controller,
                    // display settings, interrupt handlers, and system/display modes. This is the
                    // master configuration function that sets up the entire terminal based on
                    // configuration bits.
                    // Load 16-bit terminal configuration word from 0x58a6
                    // Check bit 14 (0x4000): If set, disable Z80 SIO Channel A receive
  uVar1 = _terminal_config_word;
  if ((_terminal_config_word & 0x4000) != 0) {
    disable_z80sio_channel_a_receive();
  }
                    // Check bit 12 (0x1000): If set, reset keyboard buffers and update UART
  if ((uVar1 & 0x1000) != 0) {
    reset_keyboard_buffers_and_update_uart();
  }
                    // Configure Z80 SIO Channel A status using bits 8,7,5,1 (0xa2 mask)
  set_host_port_status((byte)(uVar1 >> 8) & 0xa2);
                    // Configure keyboard controller based on bits 6-7: 00=cmd 0x0F, 01=cmd 0x07,
                    // other=cmd 0x17
  if ((uVar1 & 0xc0) == 0) {
    send_keyboard_command_with_retry_alt(0xf);
  }
  else if (((byte)uVar1 & 0xc0) == 0x40) {
    send_keyboard_command_with_retry_alt(7);
  }
  else {
    send_keyboard_command_with_retry_alt(0x17);
  }
                    // Configure keyboard mode based on bits 4-5: 00=cmd 0x0D, 01=cmd 0x05,
                    // other=cmd 0x15
  if ((uVar1 & 0x30) == 0) {
    send_keyboard_command_with_retry_alt(0xd);
  }
  else if (((byte)uVar1 & 0x30) == 0x10) {
    send_keyboard_command_with_retry_alt(5);
  }
  else {
    send_keyboard_command_with_retry_alt(0x15);
  }
                    // Check bit 8 (0x100): If set, configure interrupt handlers and clear control
                    // flags
  if ((uVar1 & 0x100) != 0) {
    configure_interrupt_handlers(2);
    terminal_control_flags = 0;
  }
                    // Extract system_mode_state from bits 10-11: 0=SETUP, 1=SPECIAL_A, 2=NORMAL,
                    // 3=SPECIAL_B
                    // CRITICAL: Sets system_mode_state = (config_word >> 10) & 3. Must be 2 for
                    // keyboard-to-serial transmission to work.
  system_mode_state = (byte)(uVar1 >> 10) & 3;
                    // Extract display_config_register from bits 0-3 (0x0F mask)
  display_config_register = (byte)uVar1 & 0xf;
  return (char)((ushort)in_AF >> 8);
}



void send_keyboard_command_with_retry_alt(void)

{
  undefined1 in_CY_flag;
  
  do {
    send_keyboard_init_command();
  } while ((bool)in_CY_flag);
  return;
}



undefined1 save_bit_pattern_to_nvram(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined1 uVar2;
  char cVar3;
  byte bVar4;
  byte *pbVar5;
  bool bVar6;
  
                    // Saves bit pattern buffer to NVRAM. Processes 80-byte buffer at ram:58a8,
                    // converts '+' markers to 1 bits and others to 0 bits, packs into bytes, and
                    // stores to NVRAM addresses 0x10-0x19. Used for bit pattern configuration
                    // storage.
  bVar4 = 0x10;
  pbVar5 = &bit_pattern_buffer;
  while( true ) {
    bVar1 = 0;
    cVar3 = '\b';
    do {
      if (*pbVar5 == 0x2b) {
        bVar6 = false;
        bVar1 = bVar1 << 1 | bVar1 >> 7 | 1;
      }
      else {
        bVar6 = (bool)(bVar1 >> 7);
        bVar1 = bVar1 << 1 | bVar6;
      }
      pbVar5 = pbVar5 + 1;
      cVar3 = cVar3 + -1;
    } while (cVar3 != '\0');
    write_byte_to_nvram();
    uVar2 = (undefined1)((ushort)in_AF >> 8);
    if (bVar6) break;
    bVar4 = bVar4 + 1;
    if (0x19 < bVar4) {
      return uVar2;
    }
  }
  return uVar2;
}



undefined1 create_bit_pattern_display(void)

{
  char cVar1;
  undefined2 in_AF;
  byte bVar2;
  char cVar3;
  byte bVar4;
  byte *pbVar5;
  byte in_CY_flag;
  
  pbVar5 = &bit_pattern_buffer;
  bVar4 = 0x10;
  do {
    bVar2 = save_config_to_nvram();
    cVar3 = '\b';
    do {
      *pbVar5 = 0x20;
      cVar1 = (char)bVar2 >> 7;
      bVar2 = bVar2 << 1 | in_CY_flag;
      if (-cVar1 != 0) {
        *pbVar5 = 0x2b;
      }
      pbVar5 = pbVar5 + 1;
      cVar3 = cVar3 + -1;
      in_CY_flag = -cVar1;
    } while (cVar3 != '\0');
    bVar4 = bVar4 + 1;
    in_CY_flag = bVar4 < 0x1a;
  } while ((bool)in_CY_flag);
  return (char)((ushort)in_AF >> 8);
}



undefined1 find_next_marker_forward(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  ushort in_DE;
  char *in_HL;
  
                    // Finds next '+' marker forward from current position. Searches through buffer
                    // until DE reaches 'P' (80) or '+' marker found. Used for navigation in bit
                    // pattern editor. Calls no_operation_wrapper for cursor setup.
  no_operation_wrapper();
  do {
    uVar1 = (undefined1)((ushort)in_AF >> 8);
    if ((char)in_DE == 'P') {
      return uVar1;
    }
    in_DE = (ushort)(byte)((char)in_DE + 1);
    in_HL = in_HL + 1;
  } while (*in_HL != '+');
  return uVar1;
}



undefined1 find_next_marker_backward(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  ushort in_DE;
  char *in_HL;
  
                    // Finds next '+' marker backward from current position. Searches backward
                    // through buffer until DE reaches 1 or '+' marker found. Used for reverse
                    // navigation in bit pattern editor. Calls no_operation_wrapper for cursor
                    // setup.
  no_operation_wrapper();
  do {
    uVar1 = (undefined1)((ushort)in_AF >> 8);
    if ((char)in_DE == '\x01') {
      return uVar1;
    }
    in_DE = (ushort)(byte)((char)in_DE - 1);
    in_HL = in_HL + -1;
  } while (*in_HL != '+');
  return uVar1;
}



void delete_character_at_cursor(void)

{
                    // TDV 2215 Delete Character Function - Implements DCH (Delete Character) CSI
                    // sequence with numeric parameter. Extended mode feature for character-level
                    // editing. Works in conjunction with ICH (Insert Character) and ECH (Erase
                    // Character) functions for comprehensive text editing capabilities.
  find_next_marker_forward();
  update_cursor_addresses();
  return;
}



void delete_character_backward(void)

{
                    // Deletes character backward from cursor by finding previous marker and
                    // updating cursor addresses. Used for backspace operations in bit pattern
                    // editor.
  find_next_marker_backward();
  update_cursor_addresses();
  return;
}



undefined1 check_for_marker_at_cursor(void)

{
  undefined2 in_AF;
  undefined1 uVar1;
  char *in_HL;
  
                    // Checks if current cursor position contains '+' marker. Returns status
                    // indicating marker presence. Used for marker detection in bit pattern editor.
  no_operation_wrapper();
  uVar1 = (undefined1)((ushort)in_AF >> 8);
  if (*in_HL == '+') {
    return uVar1;
  }
  return uVar1;
}



void set_marker_at_cursor(void)

{
  undefined1 *in_HL;
  
                    // Sets '+' marker (0x2B) at current cursor position. Used to mark bit positions
                    // in bit pattern editor. Sets bit to 1 in the pattern.
  no_operation_wrapper();
  *in_HL = 0x2b;
  return;
}



void clear_marker_at_cursor(void)

{
  undefined1 *in_HL;
  
                    // Clears marker at cursor by setting space character (0x20). Used to clear bit
                    // positions in bit pattern editor. Sets bit to 0 in the pattern.
  no_operation_wrapper();
  *in_HL = 0x20;
  return;
}



undefined1 clear_bit_pattern_buffer(void)

{
  undefined2 in_AF;
  char cVar1;
  byte *pbVar2;
  
                    // Clears entire bit pattern buffer (80 bytes at ram:58a8) by filling with space
                    // characters (0x20). Initializes buffer for new bit pattern entry. Sets all
                    // bits to 0.
  pbVar2 = &bit_pattern_buffer;
  cVar1 = 'P';
  do {
    *pbVar2 = 0x20;
    pbVar2 = pbVar2 + 1;
    cVar1 = cVar1 + -1;
  } while (cVar1 != '\0');
  return (char)((ushort)in_AF >> 8);
}



void no_operation_wrapper(void)

{
                    // No-operation wrapper function. Calls no_operation_placeholder. Used as common
                    // setup function for bit pattern operations requiring cursor positioning.
  no_operation_placeholder();
  return;
}



undefined1 create_bit_pattern_display(void)

{
  undefined2 in_AF;
  byte bVar1;
  undefined2 uVar2;
  undefined1 in_CY_flag;
  
                    // Creates bit pattern display interface. Clears screen, displays template, sets
                    // up editing environment with attributes, handles keyboard input for bit
                    // pattern editing. Main interface for bit pattern configuration with full
                    // editor functionality.
  clear_display_memory_range();
  update_cursor_addresses();
  display_formatted_string_with_templates();
  update_cursor_addresses();
  uVar2 = 0x1950;
  write_pattern_to_display_area_and_update_attributes(4);
  disable_attribute_write_enable();
  check_for_marker_at_cursor();
  if (!(bool)in_CY_flag) {
    write_char_to_video_memory();
  }
  while( true ) {
    delete_character_at_cursor();
    if ((bool)in_CY_flag) break;
    write_char_to_video_memory();
  }
  do {
    reset_cursor_to_line_start();
    while( true ) {
      no_operation_placeholder();
      update_cursor_addresses();
      display_string_until_control_char();
      display_decimal_number((char)uVar2);
      update_cursor_addresses();
      do {
        bVar1 = poll_keyboard_input_buffer();
      } while ((bool)in_CY_flag);
      if (bVar1 == 0xdb) {
        clear_display_memory_range();
        return (char)((ushort)in_AF >> 8);
      }
      in_CY_flag = bVar1 < 0xb8;
      if (bVar1 == 0xb8) break;
      handle_bit_pattern_input();
    }
  } while( true );
}



void handle_bit_pattern_input(char param_1)

{
                    // Handles keyboard input for bit pattern editor. Processes cursor movement
                    // (0xB4/0xB3), marker operations (0x99/0xCA), character input (space vs
                    // others), and editing commands. Maps keyboard codes to bit pattern operations.
  if (param_1 == -0x4c) {
    move_cursor_back_one_position();
    return;
  }
  if (param_1 == -0x4d) {
    advance_cursor_one_character();
    return;
  }
  if (param_1 == -0x67) {
    find_next_marker_forward();
    update_cursor_addresses();
    return;
  }
  if (param_1 != -0x36) {
    if (param_1 == ' ') {
      write_char_to_video_memory();
      clear_marker_at_cursor();
      advance_cursor_one_character();
      return;
    }
    write_char_to_video_memory();
    set_marker_at_cursor();
    advance_cursor_one_character();
    return;
  }
  find_next_marker_backward();
  update_cursor_addresses();
  return;
}



undefined1 configure_display_baud_settings(void)

{
  undefined2 in_AF;
  byte bVar1;
  
                    // TDV 2215 Baud Rate Configuration - Configures communication baud rates from
                    // 50-19200 baud for asynchronous/isochronous transmission. Sets up timing
                    // parameters for V.24 (RS-232-C), V.11 (RS-422), or current loop interfaces.
                    // Baud rate settings are stored in NVRAM and adjustable via the terminal's menu
                    // system.
  bVar1 = baud_rate_setting + 1;
  if (2 < bVar1) {
    bVar1 = baud_rate_setting + 2;
  }
  sw_cursor_return = bVar1 & 3;
  sw_graphic_rendition_mode = bVar1 >> 2 & 1;
  return (char)((ushort)in_AF >> 8);
}


