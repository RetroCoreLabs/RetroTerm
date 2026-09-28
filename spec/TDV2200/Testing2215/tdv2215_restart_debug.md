# TDV 2215 Terminal Restart Problem Debug Guide

## Problem Analysis
Your terminal is successfully completing the self-test, displaying characters, but then restarting. This indicates an issue in the **main operational loop** rather than the boot sequence.

---

## Root Cause Categories

### 1. Interrupt System Problems
**Most Likely Cause**: VSYNC interrupt issues causing system instability

### 2. Main Loop Corruption
**Function Pointer Corruption**: Invalid jumps in the main processing loop

### 3. Timeout/Watchdog Issues
**System Timeout**: 30-second timeout counter triggering reset

### 4. Memory Corruption
**Stack/Variable Corruption**: Corrupted function pointers or stack overflow

---

## Critical Debug Breakpoints

### Phase 1: Main Loop Entry Verification
**Breakpoint 1.1**: `0x2002` (terminal_main_loop entry)
**Test**:
- System should reach this point after successful self-test
- Check `system_status_word` (0x5E0C) = 0x8080 (operational state)
- Verify interrupts are enabled

**Breakpoint 1.2**: `0x20CC` (main loop jump table)
**Test**:
- Check `_BYTE_ram_5806` value = 0x20CD
- This is the main character processing dispatch
- **CRITICAL**: If this address is corrupted, system will crash/restart

### Phase 2: Character Processing Loop
**Breakpoint 2.1**: `0x20CD` (character processing function)
**Test**:
- This function processes each character input
- Should be called repeatedly during normal operation
- Check parameter values (character codes)

**Breakpoint 2.2**: End of `0x20CD` function (before indirect jump)
**Test**:
- Check `_BYTE_ram_5806` still equals 0x20CD
- If corrupted, this causes restart loop
- Monitor function pointer integrity

### Phase 3: Interrupt Coordination
**Breakpoint 3.1**: `0x052C` (vsync_interrupt_handler entry)
**Test**:
- Should be called every 16.67ms (60Hz)
- Check `interrupt_activity_flag` (0x5E21) behavior
- Monitor `DAT_ram_5e22` timeout counter

**Breakpoint 3.2**: `0x0634` (end of VSYNC handler)
**Test**:
- `interrupt_activity_flag` should be cleared to 0
- Check `timer_channel_flags` (0x5E25) processing
- Verify I/O port 0x60 operations

### Phase 4: Timeout Monitoring
**Breakpoint 4.1**: Inside VSYNC handler timeout check
**Address**: Around `0x0580` (timeout decrement)
**Test**:
```c
if ((BYTE_ram_5f0d == 0) && (--DAT_ram_5e22 == 0)) {
    // TIMEOUT CONDITION - May cause restart
    bVar1 = bVar1 | 8;  // Set timeout bit
}
```
- Monitor `DAT_ram_5e22` countdown (starts at 30000)
- If reaches 0: **System timeout may trigger restart**
- Check `BYTE_ram_5f0d` state

---

## Memory Watches During Debug

### Critical Function Pointers (Check for Corruption)
| Address | Name | Expected Value | Purpose |
|---------|------|----------------|---------|
| 0x5806 | `_BYTE_ram_5806` | 0x20CD | Main loop dispatch |
| 0x5808 | `_BYTE_ram_5808` | 0x209D | Secondary dispatch |
| 0x580A | `_BYTE_ram_580a` | 0x20C9 | Tertiary dispatch |

### State Variables (Monitor for Abnormal Values)
| Address | Name | Normal Range | Critical Values |
|---------|------|--------------|-----------------|
| 0x5E21 | `interrupt_activity_flag` | 0x00 or 0x5E | If stuck at 0x5E: Interrupt problem |
| 0x5E22 | `DAT_ram_5e22` | 30000 → 0 | If 0: Timeout triggered |
| 0x5E25 | `timer_channel_flags` | 0x00-0xFF | Active timer channels |
| 0x5DF9 | `display_refresh_control` | 0x00-0x0F | Display timing state |

### System Status
| Address | Name | Expected Value | Critical Check |
|---------|------|----------------|----------------|
| 0x5E0C | `system_status_word` | 0x8080 | If changed: System state corrupted |
| 0x5F13 | `MUST_BE_SET_addr_5f13` | 0x01 | Must be 1 for normal operation |

---

## Specific Restart Scenarios & Solutions

### Scenario 1: VSYNC Interrupt Problem
**Symptoms**: Restart occurs at regular intervals (16-33ms)
**Debug Steps**:
1. Set breakpoint at `0x052C` (VSYNC entry)
2. Check if interrupt fires every 16.67ms
3. Monitor I/O port 0x60 read operations
4. Check `expected_control_port2_value_maybe` changes

**Common Issues**:
- I/O port 0x60 not implemented correctly
- VSYNC interrupt frequency wrong (should be 60Hz)
- Control port status bits incorrect

**Fix**: Implement proper I/O port 0x60 with expected bit patterns

### Scenario 2: Function Pointer Corruption
**Symptoms**: Restart occurs after several characters processed
**Debug Steps**:
1. Watch `_BYTE_ram_5806` for changes from 0x20CD
2. Set memory breakpoint on 0x5806 writes
3. Trace function call stack

**Common Issues**:
- Stack overflow corrupting memory
- Bad pointer arithmetic in character processing
- Buffer overruns

**Fix**: Check stack size and memory protection

### Scenario 3: 30-Second Timeout
**Symptoms**: Restart occurs after exactly 30 seconds of operation
**Debug Steps**:
1. Monitor `DAT_ram_5e22` countdown in VSYNC handler
2. Check `BYTE_ram_5f0d` state (should be non-zero to prevent timeout)
3. Set breakpoint when `DAT_ram_5e22` reaches 0

**Common Issues**:
- Host communication not working (no activity resets timeout)
- `BYTE_ram_5f0d` incorrectly set to 0
- Timeout handling triggers restart

**Fix**: Ensure host communication or disable timeout

### Scenario 4: Interrupt Flag Stuck
**Symptoms**: System becomes unresponsive then restarts
**Debug Steps**:
1. Monitor `interrupt_activity_flag` (0x5E21)
2. Check if flag gets stuck at 0x5E (not cleared)
3. Verify VSYNC handler clears flag properly

**Common Issues**:
- Interrupt handler not completing properly
- Main loop not processing interrupt flag
- Race condition between interrupts and main loop

**Fix**: Ensure proper interrupt/main loop coordination

---

## Quick Diagnostic Test Sequence

### Test 1: Basic Operation Check (30 seconds)
1. **Start terminal emulator**
2. **Set breakpoint at 0x2002** (main loop entry)
3. **Continue execution** - should hit immediately
4. **Set breakpoint at 0x20CD** (character processing)
5. **Continue** - should hit when characters are processed
6. **Monitor for 30 seconds** - check for timeout restart

### Test 2: Interrupt Coordination Test
1. **Set breakpoint at 0x052C** (VSYNC)
2. **Count hits over 1 second** - should be ~60 hits
3. **Watch `interrupt_activity_flag`** - should toggle 0x00 ↔ 0x5E
4. **Check `DAT_ram_5e22`** - should decrement from 30000

### Test 3: Function Pointer Stability Test  
1. **Set memory watch on 0x5806** (main dispatch pointer)
2. **Process several characters**
3. **Verify pointer remains 0x20CD**
4. **Check for memory corruption**

---

## Common Emulator Implementation Issues

### 1. Missing or Incorrect I/O Port 0x60
**Problem**: VSYNC handler crashes when reading control port
**Fix**: Implement I/O port 0x60 with proper bit patterns
```c
// Port 0x60 should return control/status bits
// Bits 0-1: Control state
// Other bits: Hardware status
```

### 2. VSYNC Interrupt Timing Wrong
**Problem**: Too fast/slow VSYNC causes instability
**Fix**: Ensure exactly 60Hz (16.67ms intervals)

### 3. Stack Overflow
**Problem**: Insufficient stack space causes corruption
**Fix**: Increase stack size or check for infinite recursion

### 4. Memory Protection
**Problem**: Function pointers corrupted by bad memory writes
**Fix**: Implement memory protection or bounds checking

### 5. Host Communication Timeout
**Problem**: No activity causes 30-second timeout restart
**Fix**: 
- Implement proper host serial communication
- Or disable timeout by setting `BYTE_ram_5f0d = 1`

---

## Emergency Fixes for Testing

### Disable 30-Second Timeout
**Memory Patch**: Set `BYTE_ram_5f0d` (0x5F0D) = 1
**Effect**: Prevents timeout-based restart

### Force Activity Flag
**Memory Patch**: Periodically set `interrupt_activity_flag` (0x5E21) = 0x5E
**Effect**: Simulates activity to prevent timeout

### Bypass Function Pointer Dispatch
**Code Patch**: Replace indirect jump with direct return
**Effect**: Prevents dispatch table corruption issues

---

## Success Indicators

### Terminal Running Properly
- **Characters display correctly** ✓ (You have this)
- **No restart after 30+ seconds** ← (Your target)
- **Cursor blinks properly** (1Hz rate)
- **Keyboard input works**
- **Host communication functional**

Use this systematic approach to identify and fix the restart condition in your TDV 2215 terminal emulator.
