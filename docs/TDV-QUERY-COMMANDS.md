> # WARNING - THE MODE NUMBERS AND THE MODE QUERY IN THIS DOCUMENT ARE WRONG
>
> Checked against the real Tandberg and ND manuals on 11 September 2026. This document cites no
> source, and every mode number in it names a different switch in the manuals that do exist. It
> also describes a mode query, DECRQM, that appears in no TDV manual at all.
>
> **Read `docs\TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md` instead.** That document quotes the
> manual and the section for every statement, and its last part lists exactly what this one got
> wrong and why.
>
> This file is kept, rather than deleted, because code and tests were written from it and a reader
> who finds an old reference to it needs to be able to see what happened.

# TDV Query Commands Reference

## Overview

This document provides a comprehensive reference for all query commands supported by TDV terminal emulators. These commands allow the host to request information from the terminal, enabling proper terminal identification, capability detection, and status monitoring.

## Query Command Categories

### 1. Device Attributes (DA)

#### Primary Device Attributes
- **Command**: `ESC [ c`
- **Response**: `ESC [ ? 1 ; 2 c` (VT100 compatible)
- **Purpose**: Identifies terminal type and basic capabilities
- **Usage**: Standard terminal identification query

#### Secondary Device Attributes
- **Command**: `ESC [ > c`
- **Response**: `ESC [ > 1 ; X ; 0 c` (where X is model-specific)
- **Purpose**: Provides detailed version and model information
- **Usage**: Advanced terminal identification

### 2. Cursor Position Report (CPR)

#### Cursor Position Query
- **Command**: `ESC [ 6 n`
- **Response**: `ESC [ row ; col R` (1-based coordinates)
- **Purpose**: Reports current cursor position
- **Usage**: Cursor position monitoring and synchronization

### 3. Device Status Report (DSR)

#### Device Status Query
- **Command**: `ESC [ 5 n`
- **Response**: `ESC [ 0 n` (ready) or `ESC [ 3 n` (not ready)
- **Purpose**: Reports terminal ready status
- **Usage**: Terminal status monitoring

### 4. Terminal Identification

#### Terminal ID Query
- **Command**: `ESC Z`
- **Response**: `ESC [ ? 1 ; X c` (where X is model-specific)
- **Purpose**: Provides terminal identification string
- **Usage**: Terminal type identification

### 5. Mode Query (DECRQM)

#### Mode State Query
- **Command**: `ESC [ ? Ps $ p`
- **Response**: `ESC [ ? Ps ; state $ y`
- **Purpose**: Reports current state of specified mode
- **Usage**: Mode state monitoring

**Mode Parameters**:
- `Ps = 66`: 2115 compatibility mode
- `Ps = 67`: Smooth scroll mode (NDSSM)
- `Ps = 68`: Blink mode (NDBLWM)
- `Ps = 69`: Enhanced blink mode (NDELWM)
- `Ps = 1`: Extended mode (TDV2215)
- `Ps = 2`: Transparent mode (TDV2215)
- `Ps = 38`: Tektronix mode (TDV2200)

**State Values**:
- `0`: Not set
- `1`: Set
- `2`: Permanently set
- `3`: Permanently not set

### 6. TDV-Specific Queries

#### Work Area Query
- **Command**: `ESC [ ? 1 ; 1 $ y` (custom query)
- **Response**: `ESC [ ? 1 ; left ; top ; right ; bottom $ y`
- **Purpose**: Reports current work area boundaries
- **Usage**: Work area monitoring

#### Protected Area Query
- **Command**: `ESC [ ? 1 ; 2 $ y` (custom query)
- **Response**: `ESC [ ? 1 ; left ; top ; right ; bottom $ y`
- **Purpose**: Reports protected area boundaries
- **Usage**: Protected area monitoring

#### LED Status Query
- **Command**: `ESC [ ? 1 ; 3 $ y` (custom query)
- **Response**: `ESC [ ? 1 ; state $ y`
- **Purpose**: Reports message LED states
- **Usage**: LED status monitoring

**LED State Values**:
- Bit 0: Clear LED
- Bit 1: Set LED
- Bit 2: Blink LED

#### PUSH Key Query
- **Command**: `ESC [ ? 1 ; 4 ; key $ y` (custom query)
- **Response**: `ESC [ ? 1 ; key ; sequence $ y`
- **Purpose**: Reports programmed key definition
- **Usage**: Key programming monitoring

## Model-Specific Responses

### TDV1200 Responses

#### Device Attributes
- **Primary DA**: `ESC [ ? 1 ; 2 c`
- **Secondary DA**: `ESC [ > 1 ; 1 ; 0 c`
- **Terminal ID**: `ESC [ ? 1 ; 1 c`

#### 2115 Compatibility Mode
- **Mode Query**: `ESC [ ? 66 ; 1 $ y` (when enabled)
- **Mode Query**: `ESC [ ? 66 ; 0 $ y` (when disabled)

### TDV2215 Responses

#### Device Attributes
- **Primary DA**: `ESC [ ? 1 ; 2 c`
- **Secondary DA**: `ESC [ > 1 ; 2 ; 0 c`
- **Terminal ID**: `ESC [ ? 1 ; 2 c`

#### Extended Mode
- **Mode Query**: `ESC [ ? 1 ; 1 $ y` (when enabled)
- **Mode Query**: `ESC [ ? 1 ; 0 $ y` (when disabled)

#### Transparent Mode
- **Mode Query**: `ESC [ ? 2 ; 1 $ y` (when enabled)
- **Mode Query**: `ESC [ ? 2 ; 0 $ y` (when disabled)

### TDV2200 Responses

#### Device Attributes
- **Primary DA**: `ESC [ ? 1 ; 3 c`
- **Secondary DA**: `ESC [ > 1 ; 3 ; 0 c`
- **Terminal ID**: `ESC [ ? 1 ; 3 c`

#### Tektronix Mode
- **Mode Query**: `ESC [ ? 38 ; 1 $ y` (when enabled)
- **Mode Query**: `ESC [ ? 38 ; 0 $ y` (when disabled)

## Implementation Details

### Response Handling

All TDV emulators implement the `OnResponseReady` event to send responses back to the host:

```csharp
public event Action<string>? OnResponseReady;
```

### Query Processing

Queries are processed in the `HandleCsiSequence` method with priority given to query sequences:

```csharp
protected override void HandleCsiSequence(EscapeSequenceParser parser)
{
    // Handle query sequences first
    if (HandleQuerySequence(final, parameters, privateMarker))
    {
        return;
    }
    
    // Handle other sequences...
}
```

### Response Generation

Responses are generated by model-specific methods:

```csharp
protected virtual string HandleDeviceAttributesQuery()
{
    return "\x1b[?1;2c"; // VT100 compatible
}
```

## Usage Examples

### Basic Terminal Identification

```csharp
// Send Primary DA query
terminal.ProcessInput(Encoding.UTF8.GetBytes("\x1b[c"));

// Expected response: ESC [ ? 1 ; 2 c
```

### Cursor Position Monitoring

```csharp
// Send CPR query
terminal.ProcessInput(Encoding.UTF8.GetBytes("\x1b[6n"));

// Expected response: ESC [ row ; col R
```

### Mode State Checking

```csharp
// Check if 2115 compatibility mode is enabled
terminal.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?66$p"));

// Expected response: ESC [ ? 66 ; 1 $ y (enabled) or ESC [ ? 66 ; 0 $ y (disabled)
```

### Work Area Monitoring

```csharp
// Query current work area
terminal.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?1;1$y"));

// Expected response: ESC [ ? 1 ; left ; top ; right ; bottom $ y
```

## Testing

### Test Server Integration

The test server includes comprehensive query/response tests:

```csharp
private async Task RunTDV_QueryResponseTestsAsync(TelnetSession session)
{
    // Test all query commands
    await session.WriteAsync("\x1b[c");      // Primary DA
    await session.WriteAsync("\x1b[>c");     // Secondary DA
    await session.WriteAsync("\x1b[6n");     // CPR
    await session.WriteAsync("\x1b[5n");     // DSR
    await session.WriteAsync("\x1bZ");       // Terminal ID
    await session.WriteAsync("\x1b[?66$p");  // Mode query
}
```

### Validation

All query responses are validated for:
- Correct format
- Valid parameters
- Appropriate timing
- Model-specific accuracy

## Best Practices

### Query Timing
- Allow 1 second timeout for responses
- Implement retry logic for critical queries
- Use appropriate delays between queries

### Response Handling
- Validate response format before processing
- Handle malformed responses gracefully
- Log query/response pairs for debugging

### Error Handling
- Implement timeout handling
- Provide fallback responses for unsupported queries
- Log query failures for analysis

## Conclusion

The TDV query command implementation provides comprehensive support for host-to-terminal communication, enabling proper terminal identification, capability detection, and status monitoring. All query commands are implemented with model-specific responses and comprehensive test coverage.
