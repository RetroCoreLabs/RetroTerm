# RetroTerm User Manual

**Version**: 1.0.0 (In Development)  
**Last Updated**: 2025-10-17

---

## Table of Contents

1. [Introduction](#1-introduction)
2. [Installation](#2-installation)
3. [Getting Started](#3-getting-started)
4. [Terminal Emulators](#4-terminal-emulators)
5. [Connection Types](#5-connection-types)
6. [User Interface](#6-user-interface)
7. [File Transfer](#7-file-transfer)
8. [Text Selection and Clipboard](#8-text-selection-and-clipboard)
9. [Search](#9-search)
10. [Settings and Configuration](#10-settings-and-configuration)
11. [Visual Keyboard](#11-visual-keyboard)
12. [Advanced Features](#12-advanced-features)
13. [Keyboard Shortcuts](#13-keyboard-shortcuts)
14. [Troubleshooting](#14-troubleshooting)
15. [Appendix](#15-appendix)

---

## 1. Introduction

### 1.1 What is RetroTerm?

RetroTerm is a comprehensive, cross-platform terminal emulator designed to provide authentic emulation of both classic and modern terminal systems. Whether you're connecting to vintage systems like Norsk Data ND-100, DEC VAX, or IBM mainframes, or working with modern Linux servers, RetroTerm provides accurate, feature-rich terminal emulation.

### 1.2 Key Features

**Supported Terminal Types:**
- DEC VT Series: VT52, VT100, VT220, VT340
- Tandberg TDV Series: TDV1200, TDV2215, TDV2200/9
- IBM 3270 (Models 2, 3, 4, 5)
- Modern Terminals: xterm, ANSI
- Graphics Terminals: Tektronix 4010, VT340 (ReGIS/Sixel)

**Connection Protocols:**
- Telnet (RFC 854)
- SSH with SFTP/SCP file transfer
- IBM TN3270/TN3270E
- Serial Port (RS-232)
- XOT (X.25 over TCP)
- SINTRAN (Norsk Data proprietary)

**Platforms:**
- Windows 10/11 (Desktop)
- Linux (Desktop)
- macOS (Desktop)
- Web (Blazor WebAssembly)

### 1.3 System Requirements

**Desktop (Windows/Linux/macOS):**
- .NET 9.0 Runtime
- 4 GB RAM minimum (8 GB recommended)
- Graphics: Any modern GPU with OpenGL 3.0+
- Display: 1280x720 minimum (1920x1080 recommended)

**Web (Blazor):**
- Modern web browser (Chrome 90+, Firefox 88+, Edge 90+, Safari 14+)
- WebAssembly support
- 2 GB RAM minimum
- JavaScript enabled

### 1.4 License

RetroTerm is open-source software licensed under the MIT License.

---

## 2. Installation

### 2.1 Windows

**Method 1: Installer (Recommended)**
1. Download `RetroTerm-Setup-v1.0.0.msi` from the releases page
2. Run the installer
3. Follow the installation wizard
4. Launch RetroTerm from the Start Menu

**Method 2: Portable**
1. Download `RetroTerm-Portable-v1.0.0-win-x64.zip`
2. Extract to desired location
3. Run `RetroTerm.exe`

### 2.2 Linux

**Method 1: Debian/Ubuntu (deb package)**
```bash
sudo dpkg -i RetroTerm-v1.0.0-amd64.deb
sudo apt-get install -f  # Install dependencies
retroterm
```

**Method 2: Red Hat/Fedora (rpm package)**
```bash
sudo rpm -i RetroTerm-v1.0.0-x86_64.rpm
retroterm
```

**Method 3: AppImage (Universal)**
```bash
chmod +x RetroTerm-v1.0.0-x86_64.AppImage
./RetroTerm-v1.0.0-x86_64.AppImage
```

### 2.3 macOS

1. Download `RetroTerm-v1.0.0.dmg`
2. Open the DMG file
3. Drag RetroTerm.app to Applications folder
4. Launch from Applications

**Note**: First launch requires right-click → Open to bypass Gatekeeper

### 2.4 Web Version

Navigate to: `https://retroterm.example.com` (URL TBD)

No installation required - runs entirely in your browser.

---

## 3. Getting Started

### 3.1 First Launch

When you first launch RetroTerm, you'll see:
- A welcome message with RetroTerm branding
- Instructions on how to connect
- Authentic CRT phosphor green text on dark green background
- Menu bar with File and Help options
- Status bar showing "Ready"

### 3.2 Connecting to a Host

**Phase 1 (v1.0.0-alpha) supports Telnet connections.**

To connect to a Telnet server:

1. Click **File → Connect...** from the menu
2. In the connection dialog, enter:
   - **Host**: Server address (e.g., `localhost`, `example.com`)
   - **Port**: Server port (default: `23` for Telnet)
3. Click **"Connect"**

The status bar will show "Connection: Connected" when the connection is established.

**Example: Connecting to the test server**
- Host: `localhost`
- Port: `2323`
- This connects to the RetroTerm test server for testing escape sequences

### 3.3 Using the Terminal

Once connected:
- **Type normally**: All alphanumeric keys work
- **Special keys**: Enter, Tab, Backspace, ESC all function correctly
- **Arrow keys**: Up, Down, Left, Right send VT100 escape sequences
- **Navigation**: Home, End, Page Up/Down, Insert, Delete supported

The terminal displays:
- **Background**: Dark green/black (#001911) - authentic CRT phosphor color
- **Text**: Bright phosphor green (#00FF88)
- **Font**: Monospace (Consolas or Courier New, 14px)

### 3.4 Disconnecting

To disconnect from the current session:
1. Click **File → Exit** to close the application
2. Or close the window using the X button

The connection will close gracefully.

### 3.5 Understanding the Interface

The RetroTerm interface (v1.0.0-alpha) consists of:
- **Menu Bar**: File (Connect, Exit), Help (About)
- **Terminal Area**: Main display with authentic CRT colors
- **Status Bar**: Shows connection status ("Ready" or "Connection: Connected")

**Future versions will add:**
- Toolbar with quick actions
- Tab bar for multiple terminals
- Saved connection profiles
- Recent connections list

---

## 4. Terminal Emulators

### 4.1 VT100 Terminal Emulation

**Status: ✅ Implemented (v1.0.0-alpha)**

RetroTerm currently implements VT100 terminal emulation with support for:

#### Supported Escape Sequences

**C0 Control Characters:**
- `\x07` - Bell (BEL)
- `\x08` - Backspace (BS)
- `\x09` - Tab (HT)
- `\x0A` - Line Feed (LF)
- `\x0D` - Carriage Return (CR)

**CSI (Control Sequence Introducer) Sequences:**
- `ESC[{n}m` - SGR (Select Graphic Rendition) for text attributes
- `ESC[{row};{col}H` - CUP (Cursor Position)
- `ESC[{n}A` - Cursor Up
- `ESC[{n}B` - Cursor Down
- `ESC[{n}C` - Cursor Forward (Right)
- `ESC[{n}D` - Cursor Back (Left)
- `ESC[2J` - Clear entire screen
- `ESC[K` - Clear line from cursor to end

#### Character Attributes (SGR)

**Currently Parsed (rendering in Phase 4):**
- `ESC[0m` - Reset all attributes
- `ESC[1m` - Bold
- `ESC[2m` - Dim/Faint
- `ESC[4m` - Underline
- `ESC[5m` - Blink
- `ESC[7m` - Reverse video
- `ESC[8m` - Hidden/Invisible

#### Colors

**8 Basic Colors (Implemented):**
- Foreground: `ESC[30m` to `ESC[37m` (Black, Red, Green, Yellow, Blue, Magenta, Cyan, White)
- Background: `ESC[40m` to `ESC[47m` (Black, Red, Green, Yellow, Blue, Magenta, Cyan, White)

**256 Colors (Parsed, rendering Phase 4):**
- `ESC[38;5;{n}m` - Foreground color (0-255)
- `ESC[48;5;{n}m` - Background color (0-255)

### 4.2 VT Series Terminals (Future)

#### 4.1.1 VT52
_Documentation pending implementation_

#### 4.1.2 VT100
_Documentation pending implementation_

#### 4.1.3 VT220
_Documentation pending implementation_

#### 4.1.4 VT340
_Documentation pending implementation_

### 4.2 TDV (Tandberg) Terminals

#### 4.2.1 TDV1200
_Documentation pending implementation_

#### 4.2.2 TDV2215
_Documentation pending implementation_

#### 4.2.3 TDV2200/9
_Documentation pending implementation_

### 4.3 IBM 3270 Terminals

#### 4.3.1 IBM 3270 Overview
_Documentation pending implementation_

### 4.4 Modern Terminals

#### 4.4.1 xterm
_Documentation pending implementation_

#### 4.4.2 ANSI
_Documentation pending implementation_

### 4.5 Graphics Terminals

#### 4.5.1 Tektronix 4010
_Documentation pending implementation_

---

## 5. Connection Types

### 5.1 Telnet

**Status: ✅ Implemented (v1.0.0-alpha)**

RetroTerm supports Telnet protocol (RFC 854) with the following features:

#### Basic Telnet
- Connect to any Telnet server using hostname/IP and port
- Default port: 23 (standard Telnet port)
- Asynchronous connection handling
- Graceful disconnect on close

#### Option Negotiation

**NAWS (Negotiate About Window Size) - RFC 1073:**
- Automatically sends terminal size (80x24) to server
- Updates server when terminal is resized (future feature)

**TERMINAL-TYPE - RFC 1091:**
- Reports terminal type as "VT100"
- Allows server to optimize for VT100 escape sequences

#### Connection Dialog

To connect:
1. **File → Connect...**
2. Enter **Host** (e.g., `localhost`, `example.com`, `192.168.1.100`)
3. Enter **Port** (default: `23`, test server: `2323`)
4. Click **Connect**

#### Error Handling
- Connection timeout: Displays error dialog
- Network errors: Graceful error message
- Server disconnect: Clean connection closure

#### Example Servers to Try
- **RetroTerm Test Server**: `localhost:2323` (included in project)
- **BBS Systems**: Many still run on Telnet (search online)
- **MUD Games**: Classic text-based games
- **IoT Devices**: Many routers/switches use Telnet

### 5.2 SSH
**Status: 📋 Planned for Phase 4**

Future SSH implementation will include:
- SSH2 protocol support
- Public key and password authentication
- SFTP/SCP file transfer
- Host key validation and management
- Port forwarding

### 5.3 Serial Port
**Status: 📋 Planned for Phase 5**

### 5.4 TN3270
**Status: 📋 Planned for Phase 5**

### 5.5 XOT (X.25 over TCP)
**Status: 📋 Planned for Phase 6**

### 5.6 SINTRAN
**Status: 📋 Planned for Phase 6**

---

## 6. User Interface

### 6.1 Main Window

**Status: ✅ Implemented (v1.0.0-alpha)**

The RetroTerm main window consists of:

#### Components

**Menu Bar:**
- **File Menu**:
  - Connect... - Opens connection dialog
  - Exit - Closes the application
- **Help Menu**:
  - About - Displays version and application information

**Terminal Display:**
- 80 columns × 24 rows (standard VT100 size)
- Authentic CRT phosphor aesthetic:
  - Background: Dark green/black (#001911)
  - Text: Bright phosphor green (#00FF88)
- Monospace font (Consolas or Courier New, 14px)
- Scrollback buffer for terminal history
- Scroll bars appear when content exceeds visible area

**Status Bar:**
- Shows connection status
- "Ready" when not connected
- "Connection: Connected" when active session

#### Visual Design

RetroTerm follows the **RETRO-FAMILY-UI-DESIGN-SYSTEM** with:
- Dark theme by default
- Commodore Blue (#4169E1) accent color
- Professional CRT logo in window title bar and taskbar
- Consistent spacing and typography
- WCAG 2.1 AA accessibility compliance (11.6:1 contrast ratio)

#### Dialogs

**Connection Dialog:**
- Host input field (hostname or IP address)
- Port input field (default: 23)
- Cancel and Connect buttons
- Clean, modern design with rounded corners

**Error Dialog:**
- Displays connection errors with clear messages
- OK button to dismiss

**About Dialog:**
- Application name and version
- Description and supported features
- OK button to close

### 6.2 Tabbed vs Windowed Mode
**Status: 📋 Planned for Phase 5**

Future versions will support:
- Multiple terminals in tabs within one window
- Or separate windows for each terminal
- User-configurable preference

### 6.2 Menu System
_Documentation pending implementation_

### 6.3 Toolbar
_Documentation pending implementation_

### 6.4 Status Bar
_Documentation pending implementation_

### 6.5 Terminal Skins
_Documentation pending implementation_

---

## 7. File Transfer

_This section will be populated as file transfer features are implemented._

### 7.1 SFTP/SCP (SSH File Transfer)
_Documentation pending implementation_

### 7.2 Zmodem
_Documentation pending implementation_

### 7.3 Kermit
_Documentation pending implementation_

### 7.4 Xmodem/Ymodem
_Documentation pending implementation_

### 7.5 SINTRAN File Transfer
_Documentation pending implementation_

---

## 8. Text Selection and Clipboard

_This section will be populated as selection features are implemented._

### 8.1 Text Selection Modes
_Documentation pending implementation_

### 8.2 Copy and Paste
_Documentation pending implementation_

### 8.3 Rectangular Selection
_Documentation pending implementation_

---

## 9. Search

_This section will be populated as search features are implemented._

### 9.1 Quick Search
_Documentation pending implementation_

### 9.2 Regular Expression Search
_Documentation pending implementation_

### 9.3 Search Options
_Documentation pending implementation_

---

## 10. Settings and Configuration

_This section will be populated as settings features are implemented._

### 10.1 General Settings
_Documentation pending implementation_

### 10.2 Terminal Settings
_Documentation pending implementation_

### 10.3 Font Configuration
_Documentation pending implementation_

### 10.4 Color Schemes
_Documentation pending implementation_

### 10.5 Keyboard Bindings
_Documentation pending implementation_

---

## 11. Visual Keyboard

_This section will be populated as visual keyboard is implemented._

### 11.1 Enabling Visual Keyboard
_Documentation pending implementation_

### 11.2 TDV Keyboard Layouts
_Documentation pending implementation_

### 11.3 IBM 3270 Keyboard
_Documentation pending implementation_

### 11.4 Key Mapping
_Documentation pending implementation_

---

## 12. Advanced Features

_This section will be populated as advanced features are implemented._

### 12.1 Session Logging
_Documentation pending implementation_

### 12.2 Session Recording and Playback
_Documentation pending implementation_

### 12.3 Smooth Scrolling
_Documentation pending implementation_

### 12.4 Hyperlinks
_Documentation pending implementation_

### 12.5 Unicode and Emoji Support
_Documentation pending implementation_

---

## 13. Keyboard Shortcuts

### 13.1 Global Shortcuts

_This section will be populated as shortcuts are implemented._

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New Connection |
| `Ctrl+W` | Close Tab |
| `Ctrl+T` | New Tab |
| `Ctrl+Tab` | Next Tab |
| `Ctrl+Shift+Tab` | Previous Tab |
| `Ctrl+Q` | Quit RetroTerm |

### 13.2 Edit Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+C` | Copy (or interrupt) |
| `Ctrl+Shift+C` | Copy (force) |
| `Ctrl+V` | Paste |
| `Ctrl+Shift+V` | Paste (force) |
| `Ctrl+A` | Select All |
| `Ctrl+F` | Find |

### 13.3 View Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl++` | Zoom In |
| `Ctrl+-` | Zoom Out |
| `Ctrl+0` | Reset Zoom |
| `F11` | Fullscreen |

### 13.4 Terminal-Specific Shortcuts

_This section will be populated as terminal-specific features are implemented._

---

## 14. Troubleshooting

### 14.1 Common Issues (v1.0.0-alpha)

#### Connection Refused
**Problem**: Cannot connect to server using Telnet.

**Solutions**:
- Verify host address and port are correct
- Check if server is running and accessible
- Check Windows Firewall or antivirus settings
- Try connecting to `127.0.0.1` instead of `localhost`
- For test server: Ensure it's running on the specified port
- Try a different port (e.g., 2323 instead of 23)

**Testing**: Run the included test server:
```powershell
dotnet run --project tests/RetroTerm.TestServer -- 2323
```

#### Terminal Colors Look Wrong
**Problem**: White background instead of dark green, or colors inverted.

**Solutions**:
- **Fixed in latest build** - Restart RetroTerm with updated version
- Close and reopen the application
- Ensure you're running the latest published executable

Expected colors:
- Background: Dark green/black (#001911)
- Text: Bright phosphor green (#00FF88)

#### Keyboard Input Not Working
**Problem**: Only ESC key works, other keys don't send data.

**Solutions**:
- **Fixed in latest build** - Update to current version
- Click on the terminal display area to ensure focus
- Verify the connection is active (status bar shows "Connected")

All keys should now work:
- Letters (a-z, A-Z)
- Numbers (0-9)
- Special keys (Enter, Tab, Backspace, ESC)
- Arrow keys (Up, Down, Left, Right)
- Navigation (Home, End, Page Up/Down)

#### Menu Opens and Terminal Display Changes
**Problem**: Terminal background changes when opening File menu.

**Solutions**:
- **Fixed in latest build** - Update RetroTerm
- This was a focus-related bug that has been resolved

#### Test Server Won't Start
**Problem**: "Address already in use" error.

**Solutions**:
- Another process is using the port
- Stop any other Telnet servers
- Use a different port: `dotnet run --project tests/RetroTerm.TestServer -- 3000`
- On Windows, check if Telnet service is running (Services.msc)

#### Character Attributes Not Visible
**Problem**: Bold, underline, etc. not rendering.

**Status**: This is expected in v1.0.0-alpha
- Character attributes are **parsed** but not yet **rendered**
- Visual rendering will be implemented in Phase 4
- The parser correctly handles the escape sequences

#### Only 8 Colors Showing
**Problem**: 256-color test shows only basic colors.

**Status**: This is expected in v1.0.0-alpha
- 256-color sequences are **parsed** but only 8 basic colors **render**
- Full 256-color palette rendering in Phase 4

#### Copy/Paste Not Working
**Status**: Not yet implemented
- Text selection: Phase 4
- Copy/paste functionality: Phase 4
- Use server-side copy/paste mechanisms for now

### 14.2 Performance Issues

#### Slow Rendering
**Solutions**:
- Reduce scrollback buffer size (Settings → Terminal → Scrollback)
- Disable smooth scrolling (Settings → Scroll → STEP mode)
- Disable CRT effects if enabled
- Check GPU drivers are up to date

#### High Memory Usage
**Solutions**:
- Reduce number of open tabs
- Reduce scrollback buffer size
- Close sessions when not in use
- Restart RetroTerm periodically

### 14.3 Web Version Issues

#### Page Not Loading
**Solutions**:
- Clear browser cache and reload
- Check JavaScript is enabled
- Try different browser
- Check WebAssembly support

#### Connection Failed (Web)
**Solutions**:
- Verify proxy server is running and accessible
- Check WebSocket is not blocked by firewall
- Try different network (corporate firewalls may block WebSocket)

### 14.4 Getting Help

If you encounter issues not covered here:

1. Check the [FAQ](FAQ.md) (if available)
2. Search existing issues on GitHub
3. Create a new issue with:
   - RetroTerm version
   - Operating system and version
   - Steps to reproduce
   - Expected vs actual behavior
   - Screenshots if applicable

---

## 15. Appendix

### 15.1 Glossary

| Term | Definition |
|------|------------|
| **ANSI** | American National Standards Institute - character-mode terminal standard |
| **CSI** | Control Sequence Introducer - ESC [ in terminal escape sequences |
| **DCS** | Device Control String - Extended escape sequence type |
| **ECMA-48** | Standard defining control functions for terminals |
| **HDLC** | High-Level Data Link Control - Data link layer protocol |
| **LAPB** | Link Access Procedure, Balanced - X.25 link layer protocol |
| **OSC** | Operating System Command - Terminal title and other OS-level commands |
| **PAD** | Packet Assembler/Disassembler - X.25 terminal protocol |
| **ReGIS** | Remote Graphic Instruction Set - DEC vector graphics |
| **SFTP** | SSH File Transfer Protocol |
| **Sixel** | Six-pixel graphics format for terminals |
| **TN3270** | Telnet protocol for IBM 3270 terminals |
| **VT** | Video Terminal - DEC terminal series |
| **XOT** | X.25 over TCP - RFC 1613 protocol |

### 15.2 Supported Escape Sequences

_This section will be populated with complete escape sequence reference._

### 15.3 Character Sets

_This section will be populated with character set details._

### 15.4 Color Palettes

_This section will be populated with color scheme details._

---

## Change Log

### Version 1.0.0 (In Development)
- Initial release
- Features will be documented as implemented

---

## Credits

**Development Team:**
- [Your Name/Team]

**Special Thanks:**
- DEC for VT terminal specifications
- Tandberg/Norsk Data for TDV terminal documentation
- IBM for 3270 specifications
- Open source community

---

**For technical documentation, see**: [Architecture Documentation](docs/architecture.md)  
**For developers, see**: [API Reference](docs/api-reference.md)  
**For contributors, see**: [Contributing Guide](docs/contributing.md)

---

*This manual is a living document and will be updated as features are implemented and refined.*

