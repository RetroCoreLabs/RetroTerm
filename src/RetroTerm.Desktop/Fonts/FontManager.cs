using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroTerm.Desktop.Fonts;

/// <summary>
/// Manages TDV font system with ROM extraction, bitmap fonts, and system font fallback
/// Provides font enumeration, selection, and configuration
/// </summary>
public class FontManager
{
    private readonly Dictionary<string, BitmapFont> _bitmapFonts;
    private readonly Dictionary<string, SystemFont> _systemFonts;
    private readonly TDVFontExtractor _fontExtractor;
    private readonly string _fontDirectory;
    private BitmapFont? _currentBitmapFont;
    private SystemFont? _currentSystemFont;
    private bool _useBitmapFonts;

    public FontManager(string fontDirectory = "assets/fonts/tdv/")
    {
        _bitmapFonts = new Dictionary<string, BitmapFont>();
        _systemFonts = new Dictionary<string, SystemFont>();
        _fontExtractor = new TDVFontExtractor(fontDirectory);
        _fontDirectory = fontDirectory;
        _useBitmapFonts = true;

        LoadAvailableFonts();
    }

    /// <summary>
    /// Loads all available fonts
    /// </summary>
    private void LoadAvailableFonts()
    {
        LoadBitmapFonts();
        LoadSystemFonts();
    }

    /// <summary>
    /// Loads bitmap fonts from extracted ROM data
    /// </summary>
    private void LoadBitmapFonts()
    {
        if (!Directory.Exists(_fontDirectory))
        {
            return;
        }

        var fontFiles = Directory.GetFiles(_fontDirectory, "*.tdvfont");

        foreach (var fontFile in fontFiles)
        {
            try
            {
                var fontData = _fontExtractor.LoadFontData(Path.GetFileNameWithoutExtension(fontFile));
                if (fontData != null)
                {
                    var bitmapFont = new BitmapFont(fontData);
                    _bitmapFonts[fontData.Name] = bitmapFont;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue loading other fonts
                Console.WriteLine($"Error loading font {fontFile}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads system fonts
    /// </summary>
    private void LoadSystemFonts()
    {
        var availableFonts = SystemFont.GetAvailableFonts();

        foreach (var fontFamily in availableFonts)
        {
            try
            {
                var systemFont = new SystemFont(fontFamily, 14);
                _systemFonts[fontFamily] = systemFont;
            }
            catch (Exception ex)
            {
                // Log error but continue loading other fonts
                Console.WriteLine($"Error loading system font {fontFamily}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Extracts fonts from TDV ROM dumps
    /// </summary>
    public void ExtractFontsFromROMs(string tdv1200U5Path, string tdv1200U15Path,
                                   string tdv2200U32Path, string tdv2200U33Path, string tdv2200U63Path)
    {
        try
        {
            // Extract TDV1200 fonts
            _fontExtractor.ExtractTDV1200Fonts(tdv1200U5Path, tdv1200U15Path);

            // Extract TDV2200 fonts
            _fontExtractor.ExtractTDV2200Fonts(tdv2200U32Path, tdv2200U33Path, tdv2200U63Path);

            // Reload bitmap fonts
            LoadBitmapFonts();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error extracting fonts from ROMs: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Sets the current font
    /// </summary>
    public void SetCurrentFont(string fontName)
    {
        if (_bitmapFonts.TryGetValue(fontName, out var bitmapFont))
        {
            _currentBitmapFont = bitmapFont;
            _useBitmapFonts = true;
        }
        else if (_systemFonts.TryGetValue(fontName, out var systemFont))
        {
            _currentSystemFont = systemFont;
            _useBitmapFonts = false;
        }
        else
        {
            throw new ArgumentException($"Font '{fontName}' not found");
        }
    }

    /// <summary>
    /// Gets the current font
    /// </summary>
    public string GetCurrentFont()
    {
        if (_useBitmapFonts && _currentBitmapFont != null)
        {
            return _currentBitmapFont.Name;
        }
        else if (_currentSystemFont != null)
        {
            return _currentSystemFont.FontFamily;
        }

        return "Default";
    }

    /// <summary>
    /// Gets all available fonts
    /// </summary>
    public string[] GetAvailableFonts()
    {
        var fonts = new List<string>();

        // Add bitmap fonts
        fonts.AddRange(_bitmapFonts.Keys);

        // Add system fonts
        fonts.AddRange(_systemFonts.Keys);

        return fonts.ToArray();
    }

    /// <summary>
    /// Gets bitmap fonts only
    /// </summary>
    public string[] GetBitmapFonts()
    {
        return _bitmapFonts.Keys.ToArray();
    }

    /// <summary>
    /// Gets system fonts only
    /// </summary>
    public string[] GetSystemFonts()
    {
        return _systemFonts.Keys.ToArray();
    }

    /// <summary>
    /// Renders a character using the current font
    /// </summary>
    public void RenderCharacter(int characterCode, byte[] outputBuffer, int x, int y, int outputWidth, int outputHeight)
    {
        if (_useBitmapFonts && _currentBitmapFont != null)
        {
            _currentBitmapFont.RenderCharacter(characterCode, outputBuffer, x, y, outputWidth, outputHeight);
        }
        else if (_currentSystemFont != null)
        {
            _currentSystemFont.RenderCharacter(characterCode, outputBuffer, x, y, outputWidth, outputHeight);
        }
    }

    /// <summary>
    /// Gets character width
    /// </summary>
    public int GetCharacterWidth(int characterCode)
    {
        if (_useBitmapFonts && _currentBitmapFont != null)
        {
            return _currentBitmapFont.GetCharacterWidth(characterCode);
        }
        else if (_currentSystemFont != null)
        {
            return _currentSystemFont.GetCharacterWidth(characterCode);
        }

        return 8; // Default width
    }

    /// <summary>
    /// Gets character height
    /// </summary>
    public int GetCharacterHeight()
    {
        if (_useBitmapFonts && _currentBitmapFont != null)
        {
            return _currentBitmapFont.GetCharacterHeight();
        }
        else if (_currentSystemFont != null)
        {
            return _currentSystemFont.GetCharacterHeight();
        }

        return 16; // Default height
    }

    /// <summary>
    /// Checks if a character is available
    /// </summary>
    public bool HasCharacter(int characterCode)
    {
        if (_useBitmapFonts && _currentBitmapFont != null)
        {
            return _currentBitmapFont.HasCharacter(characterCode);
        }
        else if (_currentSystemFont != null)
        {
            return _currentSystemFont.HasCharacter(characterCode);
        }

        return true; // Default to available
    }

    /// <summary>
    /// Gets font metrics
    /// </summary>
    public FontManagerMetrics GetFontMetrics()
    {
        return new FontManagerMetrics
        {
            CurrentFont = GetCurrentFont(),
            UseBitmapFonts = _useBitmapFonts,
            BitmapFontCount = _bitmapFonts.Count,
            SystemFontCount = _systemFonts.Count,
            CharacterWidth = GetCharacterWidth(0),
            CharacterHeight = GetCharacterHeight()
        };
    }

    /// <summary>
    /// Clears all font caches
    /// </summary>
    public void ClearCaches()
    {
        foreach (var font in _bitmapFonts.Values)
        {
            font.ClearCache();
        }

        foreach (var font in _systemFonts.Values)
        {
            font.ClearCache();
        }
    }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    public (int TotalCachedCharacters, long TotalMemoryUsage) GetCacheStats()
    {
        var totalCached = 0;
        var totalMemory = 0L;

        foreach (var font in _bitmapFonts.Values)
        {
            var (cached, memory) = font.GetCacheStats();
            totalCached += cached;
            totalMemory += memory;
        }

        foreach (var font in _systemFonts.Values)
        {
            var (cached, memory) = font.GetCacheStats();
            totalCached += cached;
            totalMemory += memory;
        }

        return (totalCached, totalMemory);
    }

    /// <summary>
    /// Enables bitmap font mode
    /// </summary>
    public void EnableBitmapFonts()
    {
        _useBitmapFonts = true;
    }

    /// <summary>
    /// Enables system font mode
    /// </summary>
    public void EnableSystemFonts()
    {
        _useBitmapFonts = false;
    }

    /// <summary>
    /// Gets the best available font for a specific terminal type
    /// </summary>
    public string GetBestFontForTerminal(string terminalType)
    {
        return terminalType switch
        {
            "TDV1200" => GetBestTDV1200Font(),
            "TDV2215" => GetBestTDV2215Font(),
            "TDV2200" => GetBestTDV2200Font(),
            _ => GetDefaultFont()
        };
    }

    /// <summary>
    /// Gets the best TDV1200 font
    /// </summary>
    private string GetBestTDV1200Font()
    {
        var tdv1200Fonts = _bitmapFonts.Keys.Where(k => k.Contains("TDV1200")).ToArray();
        return tdv1200Fonts.FirstOrDefault() ?? GetDefaultFont();
    }

    /// <summary>
    /// Gets the best TDV2215 font
    /// </summary>
    private string GetBestTDV2215Font()
    {
        var tdv2215Fonts = _bitmapFonts.Keys.Where(k => k.Contains("TDV2215")).ToArray();
        return tdv2215Fonts.FirstOrDefault() ?? GetDefaultFont();
    }

    /// <summary>
    /// Gets the best TDV2200 font
    /// </summary>
    private string GetBestTDV2200Font()
    {
        var tdv2200Fonts = _bitmapFonts.Keys.Where(k => k.Contains("TDV2200")).ToArray();
        return tdv2200Fonts.FirstOrDefault() ?? GetDefaultFont();
    }

    /// <summary>
    /// Gets the default font
    /// </summary>
    private string GetDefaultFont()
    {
        return _systemFonts.Keys.FirstOrDefault() ?? "Consolas";
    }
}

/// <summary>
/// Font manager metrics
/// </summary>
public class FontManagerMetrics
{
    public string CurrentFont { get; set; } = string.Empty;
    public bool UseBitmapFonts { get; set; }
    public int BitmapFontCount { get; set; }
    public int SystemFontCount { get; set; }
    public int CharacterWidth { get; set; }
    public int CharacterHeight { get; set; }
}
