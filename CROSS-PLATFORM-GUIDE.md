# RetroTerm - Cross-Platform Compatibility Guide

**Version**: 1.0.0  
**Last Updated**: 2025-10-17  
**Platforms**: Windows, Linux, macOS, BSD (if .NET 9 supported)

---

## Table of Contents

1. [Platform Support Matrix](#1-platform-support-matrix)
2. [File System Differences](#2-file-system-differences)
3. [Configuration and Data Directories](#3-configuration-and-data-directories)
4. [Serial Port Naming](#4-serial-port-naming)
5. [Font Locations](#5-font-locations)
6. [Line Endings](#6-line-endings)
7. [Keyboard and Input](#7-keyboard-and-input)
8. [Clipboard](#8-clipboard)
9. [Process and Shell](#9-process-and-shell)
10. [Permissions and Security](#10-permissions-and-security)
11. [Networking](#11-networking)
12. [Implementation Guidelines](#12-implementation-guidelines)

---

## 1. Platform Support Matrix

### 1.1 .NET 9 Platform Support

| Platform | .NET 9 Support | RetroTerm Support | Notes |
|----------|----------------|-------------------|-------|
| **Windows 10/11** | ✅ x64, ARM64 | ✅ Primary | Full support |
| **Linux** | ✅ x64, ARM64, ARM32 | ✅ Primary | glibc 2.23+ required |
| **macOS** | ✅ x64, ARM64 (Apple Silicon) | ✅ Primary | macOS 10.15+ |
| **FreeBSD** | ✅ x64 | 🟡 Community | .NET 9 supports FreeBSD 12+ |
| **OpenBSD** | ❌ | ❌ | No official .NET support |
| **NetBSD** | ❌ | ❌ | No official .NET support |

### 1.2 Avalonia Platform Support

Avalonia 11.0+ supports:
- ✅ Windows 10/11 (Win32, WPF backend)
- ✅ Linux (X11, Wayland)
- ✅ macOS (Cocoa)
- ✅ FreeBSD (X11)

---

## 2. File System Differences

### 2.1 Path Separators

**Issue**: Windows uses backslash (`\`), Unix uses forward slash (`/`)

**Solution**: ALWAYS use `Path.Combine()` and `Path.DirectorySeparatorChar`

```csharp
// ❌ BAD - Hardcoded separators
string configPath = baseDir + "\\" + "config.json";           // Windows-only
string configPath2 = baseDir + "/" + "config.json";           // Unix-only

// ✅ GOOD - Platform-independent
string configPath = Path.Combine(baseDir, "config.json");     // Works everywhere

// ✅ GOOD - For multiple path segments
string logPath = Path.Combine(baseDir, "logs", "2025", "01", "app.log");
```

**Implementation Checklist**:
- [ ] Audit all string concatenation for file paths
- [ ] Replace with `Path.Combine()`
- [ ] Never hardcode `\` or `/` in paths
- [ ] Use `Path.DirectorySeparatorChar` if needed

### 2.2 Drive Letters and Roots

**Issue**: Windows has drive letters (`C:\`), Unix has single root (`/`)

```csharp
// Windows paths
%USERPROFILE%\Documents\RetroTerm
D:\Data\logs

// Unix paths
~/Documents/RetroTerm
/var/log/retroterm

// macOS paths
/Users/ronny/Documents/RetroTerm
/Library/Application Support/RetroTerm
```

**Solution**: Use `Path.GetPathRoot()` and avoid assumptions

```csharp
// ❌ BAD - Assumes C: drive exists
string dataPath = @"C:\ProgramData\RetroTerm";

// ✅ GOOD - Use special folders
string dataPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "RetroTerm");
```

### 2.3 Path Length Limits

| Platform | Max Path Length | Notes |
|----------|----------------|-------|
| Windows | 260 characters (MAX_PATH) | Can enable long paths in Windows 10+ |
| Linux | 4096 characters (PATH_MAX) | Per-component limit: 255 bytes |
| macOS | 1024 characters | Per-component limit: 255 bytes |
| FreeBSD | 1024 characters | Per-component limit: 255 bytes |

**Solution**: Enable long paths for Windows

```xml
<!-- RetroTerm.Desktop.csproj -->
<PropertyGroup>
  <LongPathsEnabled>true</LongPathsEnabled>
</PropertyGroup>
```

```csharp
// Validate path length
public static bool IsPathTooLong(string path)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return path.Length > 32767; // Long path limit
    }
    return path.Length > 4096;
}
```

### 2.4 Case Sensitivity

**Issue**: 
- Windows: Case-insensitive (`Config.json` == `config.json`)
- Linux/macOS/BSD: Case-sensitive (`Config.json` != `config.json`)

**Solution**: Always use consistent casing

```csharp
// ✅ GOOD - Use lowercase for all config files
"config.json"
"known_hosts.json"
"connections.json"

// ❌ BAD - Mixed case can break on Linux
"Config.JSON"
"KnownHosts.json"
```

**File Comparison**:
```csharp
// ❌ BAD - Case-sensitive comparison
if (fileName == "config.json")

// ✅ GOOD - Platform-aware comparison
if (fileName.Equals("config.json", StringComparison.OrdinalIgnoreCase))

// ✅ GOOD - For file system operations
var comparer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? StringComparer.OrdinalIgnoreCase
    : StringComparer.Ordinal;
```

### 2.5 Invalid Characters

**Windows**: `< > : " / \ | ? *`
**Unix**: Only `/` and `\0`

```csharp
public static string SanitizeFileName(string fileName)
{
    var invalid = Path.GetInvalidFileNameChars();
    foreach (var c in invalid)
    {
        fileName = fileName.Replace(c, '_');
    }
    return fileName;
}
```

### 2.6 Hidden Files

**Windows**: File attribute flag
**Unix**: Filename starts with `.`

```csharp
public static bool IsHiddenFile(string path)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        var attributes = File.GetAttributes(path);
        return (attributes & FileAttributes.Hidden) == FileAttributes.Hidden;
    }
    else
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith('.');
    }
}
```

---

## 3. Configuration and Data Directories

### 3.1 Standard Locations

#### User Configuration Directory

| Platform | Location | Purpose |
|----------|----------|---------|
| **Windows** | `%APPDATA%\RetroTerm\` | User settings |
|  | `%APPDATA%\RetroTerm\` |  |
| **Linux** | `~/.config/RetroTerm/` | User settings |
|  | `/home/<user>/.config/RetroTerm/` |  |
| **macOS** | `~/Library/Application Support/RetroTerm/` | User settings |
|  | `/Users/<user>/Library/Application Support/RetroTerm/` |  |
| **FreeBSD** | `~/.config/RetroTerm/` | User settings |

#### System-Wide Configuration

| Platform | Location | Purpose |
|----------|----------|---------|
| **Windows** | `%PROGRAMDATA%\RetroTerm\` | System-wide settings |
|  | `C:\ProgramData\RetroTerm\` |  |
| **Linux** | `/etc/RetroTerm/` | System-wide settings |
| **macOS** | `/Library/Application Support/RetroTerm/` | System-wide settings |
| **FreeBSD** | `/usr/local/etc/RetroTerm/` | System-wide settings |

#### Cache Directory

| Platform | Location | Purpose |
|----------|----------|---------|
| **Windows** | `%LOCALAPPDATA%\RetroTerm\Cache\` | Temporary data |
| **Linux** | `~/.cache/RetroTerm/` | Temporary data |
| **macOS** | `~/Library/Caches/RetroTerm/` | Temporary data |
| **FreeBSD** | `~/.cache/RetroTerm/` | Temporary data |

#### Log Directory

| Platform | Location | Purpose |
|----------|----------|---------|
| **Windows** | `%LOCALAPPDATA%\RetroTerm\Logs\` | Application logs |
| **Linux** | `~/.local/share/RetroTerm/logs/` | Application logs |
| **macOS** | `~/Library/Logs/RetroTerm/` | Application logs |
| **FreeBSD** | `~/.local/share/RetroTerm/logs/` | Application logs |

### 3.2 Implementation

**PathManager.cs**:
```csharp
public static class PathManager
{
    public static string UserConfigDirectory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RetroTerm");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RetroTerm");
            }
            else // Linux, FreeBSD, etc.
            {
                var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                var configBase = !string.IsNullOrEmpty(xdgConfig)
                    ? xdgConfig
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
                return Path.Combine(configBase, "RetroTerm");
            }
        }
    }

    public static string UserDataDirectory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RetroTerm");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RetroTerm");
            }
            else // Linux, FreeBSD, etc.
            {
                var xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                var dataBase = !string.IsNullOrEmpty(xdgData)
                    ? xdgData
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
                return Path.Combine(dataBase, "RetroTerm");
            }
        }
    }

    public static string CacheDirectory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(UserDataDirectory, "Cache");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Caches", "RetroTerm");
            }
            else // Linux, FreeBSD, etc.
            {
                var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
                var cacheBase = !string.IsNullOrEmpty(xdgCache)
                    ? xdgCache
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
                return Path.Combine(cacheBase, "RetroTerm");
            }
        }
    }

    public static string LogDirectory
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Path.Combine(UserDataDirectory, "Logs");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Logs", "RetroTerm");
            }
            else // Linux, FreeBSD, etc.
            {
                return Path.Combine(UserDataDirectory, "logs");
            }
        }
    }

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(UserConfigDirectory);
        Directory.CreateDirectory(UserDataDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
```

### 3.3 File Locations

**Standard Files**:
```csharp
public static class ConfigFiles
{
    // User configuration
    public static string AppSettings => 
        Path.Combine(PathManager.UserConfigDirectory, "settings.json");
    
    public static string Connections => 
        Path.Combine(PathManager.UserConfigDirectory, "connections.json");
    
    public static string SSHKnownHosts => 
        Path.Combine(PathManager.UserConfigDirectory, "known_hosts.json");
    
    public static string KeyBindings => 
        Path.Combine(PathManager.UserConfigDirectory, "keybindings.json");
    
    public static string ColorSchemes => 
        Path.Combine(PathManager.UserConfigDirectory, "color-schemes.json");
    
    // User data
    public static string SessionHistory => 
        Path.Combine(PathManager.UserDataDirectory, "history.db");
    
    public static string SessionLogsDirectory => 
        Path.Combine(PathManager.UserDataDirectory, "session-logs");
    
    // Cache
    public static string FontCache => 
        Path.Combine(PathManager.CacheDirectory, "fonts");
    
    public static string GlyphCache => 
        Path.Combine(PathManager.CacheDirectory, "glyphs");
}
```

---

## 4. Serial Port Naming

### 4.1 Platform Differences

| Platform | Serial Port Naming | Example |
|----------|-------------------|---------|
| **Windows** | `COMn` | `COM1`, `COM2`, `COM10` |
| **Linux** | `/dev/ttyS*`, `/dev/ttyUSB*`, `/dev/ttyACM*` | `/dev/ttyS0`, `/dev/ttyUSB0`, `/dev/ttyACM0` |
| **macOS** | `/dev/tty.*`, `/dev/cu.*` | `/dev/tty.usbserial`, `/dev/cu.usbserial` |
| **FreeBSD** | `/dev/cuau*`, `/dev/ttyu*` | `/dev/cuaU0`, `/dev/ttyU0` |

### 4.2 Serial Port Enumeration

```csharp
public static class SerialPortHelper
{
    public static IEnumerable<string> GetAvailablePortNames()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: COM ports
            return System.IO.Ports.SerialPort.GetPortNames()
                .OrderBy(p => p);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS: /dev/tty.* and /dev/cu.*
            var ports = new List<string>();
            
            if (Directory.Exists("/dev"))
            {
                ports.AddRange(Directory.GetFiles("/dev", "tty.*"));
                ports.AddRange(Directory.GetFiles("/dev", "cu.*"));
            }
            
            return ports.OrderBy(p => p);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux: /dev/ttyS*, /dev/ttyUSB*, /dev/ttyACM*
            var ports = new List<string>();
            
            if (Directory.Exists("/dev"))
            {
                ports.AddRange(Directory.GetFiles("/dev", "ttyS*"));
                ports.AddRange(Directory.GetFiles("/dev", "ttyUSB*"));
                ports.AddRange(Directory.GetFiles("/dev", "ttyACM*"));
            }
            
            return ports.OrderBy(p => p);
        }
        else // FreeBSD, etc.
        {
            // FreeBSD: /dev/cuau*, /dev/ttyu*
            var ports = new List<string>();
            
            if (Directory.Exists("/dev"))
            {
                ports.AddRange(Directory.GetFiles("/dev", "cuau*"));
                ports.AddRange(Directory.GetFiles("/dev", "ttyu*"));
            }
            
            return ports.OrderBy(p => p);
        }
    }

    public static string GetFriendlyPortName(string portName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Already friendly (COM1, COM2, etc.)
            return portName;
        }
        else
        {
            // Unix: Show basename only by default, full path in tooltip
            return Path.GetFileName(portName);
        }
    }
}
```

### 4.3 Serial Port Permissions (Unix)

**Issue**: Unix requires read/write permissions to serial port devices

**Solution**:
```bash
# Linux: Add user to dialout group
sudo usermod -a -G dialout $USER

# macOS: Usually no special permissions needed

# FreeBSD: Add user to dialer group
sudo pw groupmod dialer -m $USER
```

**Check Permissions**:
```csharp
public static bool HasSerialPortAccess(string portName)
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return true; // Windows handles permissions automatically
    }
    
    try
    {
        // Check if we can read the device
        using var fs = File.Open(portName, FileMode.Open, FileAccess.Read);
        return true;
    }
    catch (UnauthorizedAccessException)
    {
        return false;
    }
    catch
    {
        return true; // Other errors don't indicate permission issues
    }
}
```

---

## 5. Font Locations

### 5.1 System Font Directories

| Platform | System Fonts | User Fonts |
|----------|--------------|------------|
| **Windows** | `%WINDIR%\Fonts\` | `%LOCALAPPDATA%\Microsoft\Windows\Fonts\` |
| **Linux** | `/usr/share/fonts/` | `~/.local/share/fonts/` |
|  | `/usr/local/share/fonts/` | `~/.fonts/` (legacy) |
| **macOS** | `/System/Library/Fonts/` | `~/Library/Fonts/` |
|  | `/Library/Fonts/` |  |
| **FreeBSD** | `/usr/local/share/fonts/` | `~/.local/share/fonts/` |

### 5.2 Font Discovery

```csharp
public static class FontLocator
{
    public static IEnumerable<string> GetSystemFontDirectories()
    {
        var directories = new List<string>();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            directories.Add(Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.Fonts)));
            
            var localFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "Windows", "Fonts");
            if (Directory.Exists(localFonts))
                directories.Add(localFonts);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            directories.Add("/System/Library/Fonts");
            directories.Add("/Library/Fonts");
            
            var userFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Fonts");
            if (Directory.Exists(userFonts))
                directories.Add(userFonts);
        }
        else // Linux, FreeBSD
        {
            // System fonts
            if (Directory.Exists("/usr/share/fonts"))
                directories.Add("/usr/share/fonts");
            if (Directory.Exists("/usr/local/share/fonts"))
                directories.Add("/usr/local/share/fonts");
            
            // User fonts
            var userFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "fonts");
            if (Directory.Exists(userFonts))
                directories.Add(userFonts);
            
            // Legacy location
            var legacyFonts = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".fonts");
            if (Directory.Exists(legacyFonts))
                directories.Add(legacyFonts);
        }
        
        return directories.Where(d => Directory.Exists(d));
    }
}
```

### 5.3 Embedded Font Locations

**RetroTerm Embedded Fonts** (TDV ROM fonts):
```csharp
public static class EmbeddedFonts
{
    public static string FontDirectory => 
        Path.Combine(AppContext.BaseDirectory, "assets", "fonts");
    
    public static string TDV1200Font => 
        Path.Combine(FontDirectory, "tdv1200.bdf");
    
    public static string TDV2200Font => 
        Path.Combine(FontDirectory, "tdv2200.bdf");
    
    public static string TDV2215Font => 
        Path.Combine(FontDirectory, "tdv2215.bdf");
}
```

---

## 6. Line Endings

### 6.1 Platform Differences

| Platform | Line Ending | Bytes | C# Representation |
|----------|-------------|-------|-------------------|
| **Windows** | CRLF | `0D 0A` | `\r\n` |
| **Linux** | LF | `0A` | `\n` |
| **macOS** | LF | `0A` | `\n` |
| **Classic Mac (pre-OS X)** | CR | `0D` | `\r` |

### 6.2 Reading Text Files

```csharp
// ✅ GOOD - .NET handles line endings automatically
var lines = File.ReadAllLines(path);

// ✅ GOOD - StreamReader normalizes to \n
using var reader = new StreamReader(path);
string line = reader.ReadLine();

// ⚠️ CAUTION - Binary read preserves original line endings
var bytes = File.ReadAllBytes(path);
```

### 6.3 Writing Text Files

```csharp
// ✅ GOOD - Uses platform-default line ending
File.WriteAllText(path, text);
File.WriteAllLines(path, lines);

// ✅ GOOD - Explicit line ending control
File.WriteAllText(path, text.Replace("\n", Environment.NewLine));

// ✅ GOOD - StreamWriter uses platform default
using var writer = new StreamWriter(path);
writer.WriteLine(text); // Adds Environment.NewLine
```

### 6.4 Git Configuration

**.gitattributes** (already handled by Git warnings we saw):
```
* text=auto

# C# files
*.cs text eol=crlf
*.csproj text eol=crlf
*.sln text eol=crlf

# Documentation
*.md text eol=lf
*.txt text eol=lf

# Scripts
*.sh text eol=lf
*.bash text eol=lf

# Binary files
*.png binary
*.jpg binary
*.dll binary
*.exe binary
```

---

## 7. Keyboard and Input

### 7.1 Keyboard Shortcuts

| Action | Windows/Linux | macOS | Notes |
|--------|---------------|-------|-------|
| **Copy** | Ctrl+C / Ctrl+Shift+C | Cmd+C | Terminal uses Ctrl+Shift+C |
| **Paste** | Ctrl+V / Ctrl+Shift+V | Cmd+V | Terminal uses Ctrl+Shift+V |
| **New Tab** | Ctrl+T | Cmd+T |  |
| **Close Tab** | Ctrl+W | Cmd+W |  |
| **Find** | Ctrl+F | Cmd+F |  |
| **Quit** | Ctrl+Q | Cmd+Q |  |
| **Settings** | Ctrl+, | Cmd+, |  |

**Implementation**:
```csharp
public static class KeyBindings
{
    public static KeyModifiers PrimaryModifier =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            ? KeyModifiers.Meta   // Cmd on macOS
            : KeyModifiers.Control; // Ctrl on Windows/Linux
    
    public static bool IsCopyShortcut(KeyEventArgs e)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return e.KeyModifiers == KeyModifiers.Meta && e.Key == Key.C;
        }
        else
        {
            // Terminal mode: Ctrl+Shift+C
            return e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) 
                && e.Key == Key.C;
        }
    }
}
```

### 7.2 Special Key Handling

**Unix Terminal Key Sequences**:
- Some key combinations are consumed by the terminal/shell
- SSH servers may interpret keys differently

```csharp
public static class SpecialKeys
{
    public static bool IsTerminalReservedKey(Key key, KeyModifiers modifiers)
    {
        // On Unix terminals, Ctrl+C, Ctrl+Z, Ctrl+D are special
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (modifiers == KeyModifiers.Control)
            {
                switch (key)
                {
                    case Key.C: // SIGINT
                    case Key.Z: // SIGTSTP
                    case Key.D: // EOF
                        return true;
                }
            }
        }
        return false;
    }
}
```

---

## 8. Clipboard

### 8.1 Platform Differences

| Platform | Primary Clipboard | Selection Clipboard | Notes |
|----------|------------------|---------------------|-------|
| **Windows** | ✅ | ❌ | Single clipboard |
| **Linux** | ✅ | ✅ (X11 only) | Two clipboards: CLIPBOARD and PRIMARY |
| **macOS** | ✅ | ❌ | Single clipboard (pasteboard) |

### 8.2 Linux X11 Selection

**Linux X11** has two clipboards:
- **CLIPBOARD**: Standard clipboard (Ctrl+C/Ctrl+V)
- **PRIMARY**: Selection clipboard (select text, middle-click paste)

```csharp
public static class ClipboardHelper
{
    public static async Task SetTextAsync(string text)
    {
        // Standard clipboard
        await Application.Current.Clipboard.SetTextAsync(text);
        
        // Linux X11: Also set PRIMARY selection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Avalonia handles this automatically for X11
            // No extra code needed
        }
    }
    
    public static async Task<string> GetTextAsync()
    {
        return await Application.Current.Clipboard.GetTextAsync();
    }
}
```

### 8.3 Clipboard Formats

| Platform | Text Format | HTML Format | RTF Format |
|----------|-------------|-------------|------------|
| **Windows** | CF_UNICODETEXT | CF_HTML | CF_RTF |
| **Linux** | text/plain;charset=utf-8 | text/html | text/rtf |
| **macOS** | NSPasteboardTypeString | public.html | public.rtf |

**Avalonia abstracts this for us**, but be aware when implementing custom formats.

---

## 9. Process and Shell

### 9.1 Default Shells

| Platform | Default Shell | Alternative Shells |
|----------|--------------|-------------------|
| **Windows** | `cmd.exe` | PowerShell, WSL bash |
| **Linux** | Usually `bash` | sh, zsh, fish, dash |
| **macOS** | `zsh` (since Catalina) | bash, sh, fish |
| **FreeBSD** | `tcsh` | sh, bash, zsh |

### 9.2 Shell Detection

```csharp
public static class ShellDetector
{
    public static string GetDefaultShell()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check for PowerShell 7+ first
            var pwsh = FindInPath("pwsh.exe");
            if (pwsh != null)
                return pwsh;
            
            // Fall back to cmd.exe
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "cmd.exe");
        }
        else
        {
            // Unix: Check SHELL environment variable
            var shell = Environment.GetEnvironmentVariable("SHELL");
            if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
                return shell;
            
            // Fall back to /bin/sh
            return "/bin/sh";
        }
    }
    
    private static string FindInPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return null;
        
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = path.Split(separator);
        
        foreach (var p in paths)
        {
            var fullPath = Path.Combine(p, fileName);
            if (File.Exists(fullPath))
                return fullPath;
        }
        
        return null;
    }
}
```

### 9.3 Process Execution

```csharp
public static class ProcessHelper
{
    public static ProcessStartInfo CreateShellStartInfo()
    {
        var psi = new ProcessStartInfo
        {
            FileName = ShellDetector.GetDefaultShell(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: No special environment needed
        }
        else
        {
            // Unix: Set TERM environment variable
            psi.Environment["TERM"] = "xterm-256color";
            psi.Environment["COLORTERM"] = "truecolor";
        }
        
        return psi;
    }
}
```

---

## 10. Permissions and Security

### 10.1 File Permissions (Unix)

**Unix** uses chmod permissions, **Windows** uses ACLs

```csharp
public static class FilePermissions
{
    public static void SetUnixPermissions(string path, int mode)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // .NET 9: File.SetUnixFileMode()
            File.SetUnixFileMode(path, (UnixFileMode)mode);
        }
    }
    
    public static void MakeExecutable(string path)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // chmod +x
            var mode = File.GetUnixFileMode(path);
            mode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            File.SetUnixFileMode(path, mode);
        }
    }
    
    public static void SecurePrivateKeyFile(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: Set ACL to only current user
            var fileSecurity = new FileSecurity(path, AccessControlSections.Access);
            fileSecurity.SetAccessRuleProtection(true, false);
            
            var identity = WindowsIdentity.GetCurrent();
            var rule = new FileSystemAccessRule(
                identity.User,
                FileSystemRights.FullControl,
                AccessControlType.Allow);
            
            fileSecurity.AddAccessRule(rule);
            new FileInfo(path).SetAccessControl(fileSecurity);
        }
        else
        {
            // Unix: chmod 600 (user read/write only)
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
```

### 10.2 Executable Permissions

**Issue**: Scripts need execute permission on Unix

```csharp
// After creating a script file on Unix:
if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    FilePermissions.MakeExecutable(scriptPath);
}
```

---

## 11. Networking

### 11.1 Network Interfaces

**Platform differences** in network interface naming:
- **Windows**: "Ethernet", "Wi-Fi", "Ethernet 2"
- **Linux**: "eth0", "wlan0", "enp0s3"
- **macOS**: "en0", "en1"
- **FreeBSD**: "em0", "wlan0"

**Not a major concern for RetroTerm** (we connect by hostname/IP, not interface)

### 11.2 Localhost

All platforms support:
- `localhost` → `127.0.0.1` (IPv4)
- `localhost` → `::1` (IPv6)

```csharp
// ✅ GOOD - Works everywhere
await client.ConnectAsync("localhost", 23);

// ✅ GOOD - Explicit IPv4
await client.ConnectAsync("127.0.0.1", 23);

// ✅ GOOD - Explicit IPv6
await client.ConnectAsync("::1", 23);
```

### 11.3 Firewall

**Platform-specific firewalls**:
- **Windows**: Windows Defender Firewall (may prompt on first run)
- **Linux**: iptables, ufw, firewalld (varies by distribution)
- **macOS**: pf (Packet Filter)
- **FreeBSD**: pf, ipfw

**For RetroTerm**: Only outgoing connections (no incoming), so firewall is rarely an issue.

---

## 12. Implementation Guidelines

### 12.1 Platform Detection

**Use `RuntimeInformation`**:
```csharp
using System.Runtime.InteropServices;

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    // Windows-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
{
    // Linux-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
{
    // macOS-specific code
}
else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
{
    // FreeBSD-specific code
}
```

### 12.2 Centralized Platform Utilities

**Create**: `src/RetroTerm.Core/Platform/PlatformHelper.cs`

```csharp
public static class PlatformHelper
{
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsFreeBSD => RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD);
    public static bool IsUnix => !IsWindows;
    
    public static string PlatformName =>
        IsWindows ? "Windows" :
        IsLinux ? "Linux" :
        IsMacOS ? "macOS" :
        IsFreeBSD ? "FreeBSD" :
        "Unknown";
}
```

### 12.3 Testing on Multiple Platforms

**CI/CD Matrix Testing**:
```yaml
# .github/workflows/test.yml
strategy:
  matrix:
    os: [windows-latest, ubuntu-latest, macos-latest]
    
jobs:
  test:
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v3
      - uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '9.0'
      - run: dotnet test
```

### 12.4 Documentation

**Platform-Specific Instructions**:
- Include platform-specific notes in USER-MANUAL.md
- Separate installation instructions per platform
- Document platform-specific keyboard shortcuts
- Note serial port permissions (Unix)

### 12.5 Checklist for Cross-Platform Code

For every new feature, verify:

- [ ] **File Paths**: Using `Path.Combine()`?
- [ ] **Directories**: Using `PathManager` for standard locations?
- [ ] **Line Endings**: Using `Environment.NewLine` or .NET automatic handling?
- [ ] **Case Sensitivity**: Using `StringComparison.OrdinalIgnoreCase` for file names?
- [ ] **Serial Ports**: Handling platform-specific naming?
- [ ] **Fonts**: Checking platform-specific font directories?
- [ ] **Permissions**: Setting execute permissions on Unix?
- [ ] **Keyboard**: Supporting both Ctrl and Cmd modifiers?
- [ ] **Clipboard**: Handling selection clipboard on Linux?
- [ ] **Testing**: Tested on Windows, Linux, and macOS?

---

## Summary

### Critical Cross-Platform Considerations

1. **Always use `Path.Combine()`** - Never hardcode `\` or `/`
2. **Use `PathManager`** for config/data/cache/log directories
3. **Handle serial port naming** - `COMn` vs `/dev/tty*`
4. **Support both Ctrl and Cmd** for keyboard shortcuts
5. **Set Unix file permissions** for scripts and private keys
6. **Test on all platforms** via CI/CD matrix
7. **Case-sensitive file systems** - Use consistent casing
8. **Line endings** - Let .NET handle automatically
9. **Font locations** - Platform-specific discovery
10. **Clipboard** - Handle PRIMARY selection on Linux

### Low-Risk Areas

- **Networking** - TCP/IP works the same everywhere
- **SSH/Telnet** - Protocol-level, platform-independent
- **JSON Config** - Cross-platform by nature
- **Avalonia UI** - Handles platform differences automatically
- **Terminal Emulation Logic** - Pure C#, no platform dependencies

---

**End of Cross-Platform Guide**

For implementation details, see [TODO-PLAN.md](TODO-PLAN.md)  
For architecture overview, see [retroterm_emulator_plan.md](retroterm_emulator_plan.md)

