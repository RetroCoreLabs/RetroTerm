# RetroTerm Quick Start Guide

**Version:** 1.0.0-alpha (Phase 3 Complete)  
**Date:** October 17, 2025

---

## 🚀 Running RetroTerm

### Published Executables (Standalone - No Installation Required)

Both RetroTerm and the Test Server are available as self-contained executables that run without any .NET installation.

#### RetroTerm Terminal Emulator
**Location:** `src\RetroTerm.Desktop\bin\Release\net10.0\win-x64\publish\RetroTerm.Desktop.exe`  
**Size:** ~82 MB  
**Requirements:** Windows 10/11 (64-bit)

```powershell
# Run RetroTerm
.\src\RetroTerm.Desktop\bin\Release\net10.0\win-x64\publish\RetroTerm.Desktop.exe
```

#### RetroTerm Test Server
**Location:** `tests\RetroTerm.TestServer\bin\Release\net10.0\win-x64\publish\RetroTerm.TestServer.exe`  
**Size:** ~71 MB  
**Requirements:** Windows 10/11 (64-bit)

```powershell
# Run Test Server on port 2323 (default)
.\tests\RetroTerm.TestServer\bin\Release\net10.0\win-x64\publish\RetroTerm.TestServer.exe

# Run Test Server on custom port
.\tests\RetroTerm.TestServer\bin\Release\net10.0\win-x64\publish\RetroTerm.TestServer.exe 3000
```

---

## 🧪 Quick Test (5 Minutes)

### Step 1: Start Test Server
Open PowerShell:
```powershell
# from the repository root
.\tests\RetroTerm.TestServer\bin\Release\net10.0\win-x64\publish\RetroTerm.TestServer.exe 2323
```

You should see:
```
RetroTerm Test Server listening on port 2323
Waiting for connections...
```

### Step 2: Start RetroTerm
Open another PowerShell window:
```powershell
# from the repository root
.\src\RetroTerm.Desktop\bin\Release\net10.0\win-x64\publish\RetroTerm.Desktop.exe
```

### Step 3: Connect
1. In RetroTerm, click **File → Connect...**
2. Enter:
   - **Host:** `localhost`
   - **Port:** `2323`
3. Click **Connect**

### Step 4: Test Escape Sequences
You should see a colorful banner. The test menu appears:

```
=== Test Menu ===
1. Basic Colors (8 colors)
2. Cursor Movement
3. Character Attributes (Bold, Underline, etc.)
4. Scrolling Test
5. Line Drawing Characters
6. Screen Clear Test
7. 256 Color Test
Q. Quit
```

**Press number keys 1-7** to run each test!

---

## ✨ What's Working

### Terminal Display
- ✅ Authentic CRT phosphor aesthetic
  - Background: Dark green/black (#001911)
  - Text: Bright phosphor green (#00FF88)
- ✅ 80×24 terminal buffer
- ✅ Scrollback history
- ✅ Monospace font (Consolas/Courier New, 14px)

### Keyboard Input
- ✅ All alphanumeric keys (a-z, A-Z, 0-9)
- ✅ Special characters (!, @, #, $, %, etc.)
- ✅ Control keys (Enter, Tab, Backspace, ESC)
- ✅ Arrow keys (Up, Down, Left, Right)
- ✅ Navigation (Home, End, Page Up/Down, Insert, Delete)

### Escape Sequences
- ✅ 25+ VT100 escape sequences supported
- ✅ 8 basic colors (foreground/background)
- ✅ Cursor positioning and movement
- ✅ Screen clear operations
- ✅ Character attributes (parsed, rendering Phase 4)

### Network
- ✅ Telnet (RFC 854)
- ✅ NAWS option negotiation
- ✅ TERMINAL-TYPE negotiation
- ✅ Error handling

---

## 📁 File Locations

```
RetroTerm/
├── src/RetroTerm.Desktop/bin/Release/net10.0/win-x64/publish/
│   └── RetroTerm.Desktop.exe         (~82 MB, standalone)
│
├── tests/RetroTerm.TestServer/bin/Release/net10.0/win-x64/publish/
│   └── RetroTerm.TestServer.exe      (~71 MB, standalone)
│
├── docs/
│   ├── QUICK-TEST-GUIDE.md           (Detailed testing instructions)
│   ├── USER-MANUAL.md                (Complete user manual)
│   └── PHASE3-FEATURES.md            (Feature list)
│
└── QUICK-START.md                    (This file)
```

---

## 🐛 Troubleshooting

### "Cannot connect to server"
- Verify TestServer is running: Check PowerShell window shows "listening on port 2323"
- Try `127.0.0.1` instead of `localhost`
- Check Windows Firewall isn't blocking the connection

### "Address already in use"
- Another process is using port 2323
- Use a different port: `RetroTerm.TestServer.exe 3000`

### Colors look wrong
- **Fixed in Phase 3!** Ensure you're using the latest published executable
- Expected: Dark green background, bright green text

### Keyboard not working
- **Fixed in Phase 3!** Click on terminal area to ensure focus
- All keys should work now

---

## 📚 More Information

- **[Complete User Manual](USER-MANUAL.md)** - Full documentation
- **[Quick Test Guide](docs/QUICK-TEST-GUIDE.md)** - Detailed testing procedures
- **[Phase 3 Features](docs/PHASE3-FEATURES.md)** - What's implemented
- **[Phase 3 Completion](docs/PHASE3-COMPLETION-SUMMARY.md)** - Completion report

---

## 🎯 Test Server Menu

Once connected, press these keys to test different features:

| Key | Test | What It Tests |
|-----|------|---------------|
| **1** | Basic Colors | 8 foreground and background colors |
| **2** | Cursor Movement | Cursor positioning (ESC[H) |
| **3** | Character Attributes | Bold, underline, reverse video |
| **4** | Scrolling | 30 lines with scrollback |
| **5** | Line Drawing | Box characters (┌─┐│└┘) |
| **6** | Screen Clear | ESC[2J and ESC[H |
| **7** | 256 Colors | Full color palette test |
| **Q** | Quit | Close connection |

---

## ⚠️ Known Limitations (Phase 3)

These features are parsed but not yet visually rendered (coming in Phase 4):

- **Bold/Underline:** Not visually distinct yet
- **Blinking:** No animation yet
- **256 Colors:** Only 8 basic colors render
- **Text Selection:** Not implemented
- **Copy/Paste:** Not functional

---

## 🚀 Phase 4 Preview

Coming next:
- Visual rendering of bold, underline, blink
- Full 256-color palette
- Cursor styles and blinking
- Text selection with mouse
- Copy/paste functionality
- Smooth scrolling animation

---

**RetroTerm v1.0.0-alpha: A modern terminal emulator with vintage soul** 🖥️✨

*For more help, see [USER-MANUAL.md](USER-MANUAL.md)*

