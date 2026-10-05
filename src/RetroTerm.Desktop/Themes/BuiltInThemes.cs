using Avalonia.Media;

namespace RetroTerm.Desktop.Themes;

/// <summary>
/// All built-in theme definitions.
/// </summary>
public static class BuiltInThemes
{
    private static Color C(string hex) => Color.Parse(hex);

    public static readonly ThemeDefinition Dark = new()
    {
        Id = "dark",
        TextBoxBackground = C("#000000"),
        DisplayName = "Dark",
        WindowBackground = C("#1E1E1E"),
        PanelBackground = C("#2D2D30"),
        SectionBackground = C("#252526"),
        HoverBackground = C("#2A2D2E"),
        SelectedBackground = C("#094771"),
        Border = C("#3F3F46"),
        FocusBorder = C("#0A84FF"),
        PrimaryText = C("#E8E8E8"),
        SecondaryText = C("#A0A0A0"),
        DisabledText = C("#656565"),
        Accent = C("#0A84FF"),
        AccentHover = C("#3D9BFF"),
        SecondaryButton = C("#3F3F46"),
        SecondaryButtonBorder = C("#555555"),
        Error = C("#C44C4C"),
        Success = C("#4EC94E"),
        Warning = C("#D7B600"),
        SectionHeader1 = C("#569CD6"),
        SectionHeader2 = C("#4EC9B0"),
        SectionHeader3 = C("#DCDCAA"),
        SectionHeader4 = C("#C586C0"),
        TransferIndicator = C("#00AAFF"),
        RecordIndicator = C("#FF4444"),
        ErrorPanelBackground = C("#3A1D1D"),
        ErrorPanelBorder = C("#C44C4C"),
        ErrorPanelText = C("#FF99A4"),
        CodeKeyword = C("#4EC9B0"),
        CodeString = C("#CE9178"),
    };

    public static readonly ThemeDefinition Light = new()
    {
        Id = "light",
        TerminalForeground = C("#202020"),
        TerminalBackground = C("#FFFFFF"),
        TextBoxBackground = C("#FFFFFF"),
        DisplayName = "Light",
        WindowBackground = C("#F3F3F3"),
        PanelBackground = C("#F8F8F8"),
        SectionBackground = C("#FFFFFF"),
        HoverBackground = C("#EAF6FF"),
        SelectedBackground = C("#CDE8FF"),
        Border = C("#D0D0D0"),
        FocusBorder = C("#0078D7"),
        PrimaryText = C("#202020"),
        SecondaryText = C("#666666"),
        DisabledText = C("#AAAAAA"),
        Accent = C("#0078D7"),
        AccentHover = C("#106EBE"),
        SecondaryButton = C("#DDDDDD"),
        SecondaryButtonBorder = C("#BBBBBB"),
        Error = C("#C42B1C"),
        Success = C("#0F7B0F"),
        Warning = C("#D48806"),
        SectionHeader1 = C("#0451A5"),
        SectionHeader2 = C("#0F7B0F"),
        SectionHeader3 = C("#986801"),
        SectionHeader4 = C("#AF00DB"),
        TransferIndicator = C("#0066B8"),
        RecordIndicator = C("#C42B1C"),
        ErrorPanelBackground = C("#FDE7E9"),
        ErrorPanelBorder = C("#E81123"),
        ErrorPanelText = C("#8B1A1A"),
        CodeKeyword = C("#0F7B0F"),
        CodeString = C("#A31515"),
    };

    public static readonly ThemeDefinition RetroGreen = new()
    {
        Id = "retro-green",
        TerminalForeground = C("#00FF00"),
        TerminalBackground = C("#000800"),
        TextBoxBackground = C("#000800"),
        DisplayName = "Retro Green",
        WindowBackground = C("#001100"),
        PanelBackground = C("#001A00"),
        SectionBackground = C("#002200"),
        HoverBackground = C("#003300"),
        SelectedBackground = C("#004400"),
        Border = C("#004400"),
        FocusBorder = C("#00FF00"),
        PrimaryText = C("#00FF00"),
        SecondaryText = C("#00AA00"),
        DisabledText = C("#005500"),
        Accent = C("#008800"),
        AccentHover = C("#00AA00"),
        SecondaryButton = C("#003300"),
        SecondaryButtonBorder = C("#005500"),
        Error = C("#FF3300"),
        Success = C("#00FF00"),
        Warning = C("#FFFF00"),
        SectionHeader1 = C("#00CCFF"),
        SectionHeader2 = C("#00FF88"),
        SectionHeader3 = C("#CCFF00"),
        SectionHeader4 = C("#FF88FF"),
        TransferIndicator = C("#00FFFF"),
        RecordIndicator = C("#FF3300"),
        ErrorPanelBackground = C("#220000"),
        ErrorPanelBorder = C("#FF3300"),
        ErrorPanelText = C("#FF6644"),
        CodeKeyword = C("#00FF88"),
        CodeString = C("#88FF00"),
    };

    public static readonly ThemeDefinition AmberCrt = new()
    {
        Id = "amber-crt",
        TerminalForeground = C("#FFB000"),
        TerminalBackground = C("#0D0800"),
        TextBoxBackground = C("#0D0800"),
        DisplayName = "Amber CRT",
        WindowBackground = C("#1A0F00"),
        PanelBackground = C("#221400"),
        SectionBackground = C("#261800"),
        HoverBackground = C("#332200"),
        SelectedBackground = C("#443300"),
        Border = C("#553300"),
        FocusBorder = C("#FFB000"),
        PrimaryText = C("#FFB000"),
        SecondaryText = C("#CC8800"),
        DisabledText = C("#664400"),
        Accent = C("#CC7700"),
        AccentHover = C("#FF8C00"),
        SecondaryButton = C("#332200"),
        SecondaryButtonBorder = C("#664400"),
        Error = C("#FF3300"),
        Success = C("#FFB000"),
        Warning = C("#FFFF00"),
        SectionHeader1 = C("#FFCC44"),
        SectionHeader2 = C("#FFB000"),
        SectionHeader3 = C("#FF8C00"),
        SectionHeader4 = C("#FFDD88"),
        TransferIndicator = C("#FFCC00"),
        RecordIndicator = C("#FF3300"),
        ErrorPanelBackground = C("#220000"),
        ErrorPanelBorder = C("#FF3300"),
        ErrorPanelText = C("#FF6644"),
        CodeKeyword = C("#FFCC44"),
        CodeString = C("#FF8C00"),
    };

    public static readonly ThemeDefinition Nord = new()
    {
        Id = "nord",
        TerminalForeground = C("#D8DEE9"),
        TerminalBackground = C("#2E3440"),
        TextBoxBackground = C("#272C36"),
        DisplayName = "Nord",
        WindowBackground = C("#2E3440"),
        PanelBackground = C("#3B4252"),
        SectionBackground = C("#434C5E"),
        HoverBackground = C("#3B4252"),
        SelectedBackground = C("#434C5E"),
        Border = C("#4C566A"),
        FocusBorder = C("#88C0D0"),
        PrimaryText = C("#ECEFF4"),
        SecondaryText = C("#9AA4B5"),
        DisabledText = C("#4C566A"),
        Accent = C("#88C0D0"),
        AccentHover = C("#8FBCBB"),
        SecondaryButton = C("#434C5E"),
        SecondaryButtonBorder = C("#4C566A"),
        Error = C("#BF616A"),
        Success = C("#A3BE8C"),
        Warning = C("#EBCB8B"),
        SectionHeader1 = C("#88C0D0"),
        SectionHeader2 = C("#A3BE8C"),
        SectionHeader3 = C("#EBCB8B"),
        SectionHeader4 = C("#B48EAD"),
        TransferIndicator = C("#88C0D0"),
        RecordIndicator = C("#BF616A"),
        ErrorPanelBackground = C("#43292C"),
        ErrorPanelBorder = C("#BF616A"),
        ErrorPanelText = C("#D99CA3"),
        CodeKeyword = C("#8FBCBB"),
        CodeString = C("#A3BE8C"),
    };

    public static readonly ThemeDefinition Synthwave = new()
    {
        Id = "synthwave",
        TerminalForeground = C("#FFFFFF"),
        TerminalBackground = C("#262335"),
        TextBoxBackground = C("#1B1428"),
        DisplayName = "Synthwave '84",
        WindowBackground = C("#262335"),
        PanelBackground = C("#241B2F"),
        SectionBackground = C("#2A2139"),
        HoverBackground = C("#34294F"),
        SelectedBackground = C("#463465"),
        Border = C("#463465"),
        FocusBorder = C("#FF7EDB"),
        PrimaryText = C("#FFFFFF"),
        SecondaryText = C("#B893CE"),
        DisabledText = C("#614D85"),
        Accent = C("#FF7EDB"),
        AccentHover = C("#03EDF9"),
        SecondaryButton = C("#34294F"),
        SecondaryButtonBorder = C("#614D85"),
        Error = C("#FE4450"),
        Success = C("#72F1B8"),
        Warning = C("#FEDE5D"),
        SectionHeader1 = C("#03EDF9"),
        SectionHeader2 = C("#72F1B8"),
        SectionHeader3 = C("#FEDE5D"),
        SectionHeader4 = C("#FF7EDB"),
        TransferIndicator = C("#03EDF9"),
        RecordIndicator = C("#FE4450"),
        ErrorPanelBackground = C("#351525"),
        ErrorPanelBorder = C("#FE4450"),
        ErrorPanelText = C("#F97E72"),
        CodeKeyword = C("#72F1B8"),
        CodeString = C("#FF8B39"),
    };

    /// <summary>
    /// Solarized Dark (Ethan Schoonover's palette), carried over from RetroCommanderUI so the
    /// two apps in the suite offer the same theme list. The base03/base02/base01/base1/base00
    /// names in the comments are Schoonover's own.
    /// </summary>
    public static readonly ThemeDefinition SolarizedDark = new()
    {
        Id = "solarized-dark",
        TerminalForeground = C("#839496"),
        TerminalBackground = C("#002B36"),
        TextBoxBackground = C("#00212B"),
        DisplayName = "Solarized Dark",
        WindowBackground = C("#002B36"),        // base03
        PanelBackground = C("#073642"),         // base02
        SectionBackground = C("#08404F"),
        HoverBackground = C("#0B4A59"),
        SelectedBackground = C("#0B4F60"),
        Border = C("#094B5A"),
        FocusBorder = C("#268BD2"),             // blue
        PrimaryText = C("#93A1A1"),             // base1
        SecondaryText = C("#657B83"),           // base00
        DisabledText = C("#586E75"),            // base01
        Accent = C("#268BD2"),
        AccentHover = C("#4FA3E0"),
        SecondaryButton = C("#08404F"),
        SecondaryButtonBorder = C("#586E75"),
        Error = C("#DC322F"),                   // red
        Success = C("#859900"),                 // green
        Warning = C("#B58900"),                 // yellow
        SectionHeader1 = C("#268BD2"),          // blue
        SectionHeader2 = C("#2AA198"),          // cyan
        SectionHeader3 = C("#B58900"),          // yellow
        SectionHeader4 = C("#D33682"),          // magenta
        TransferIndicator = C("#2AA198"),
        RecordIndicator = C("#DC322F"),
        ErrorPanelBackground = C("#3A1F1A"),
        ErrorPanelBorder = C("#DC322F"),
        ErrorPanelText = C("#E8857F"),
        CodeKeyword = C("#2AA198"),
        CodeString = C("#859900"),
    };

    public static readonly ThemeDefinition HighContrast = new()
    {
        Id = "high-contrast",
        TerminalForeground = C("#FFFFFF"),
        TerminalBackground = C("#000000"),
        TextBoxBackground = C("#000000"),
        DisplayName = "High Contrast",
        WindowBackground = C("#000000"),
        PanelBackground = C("#000000"),
        SectionBackground = C("#1A1A1A"),
        HoverBackground = C("#333333"),
        SelectedBackground = C("#1F6FEB"),
        Border = C("#FFFFFF"),
        FocusBorder = C("#1F6FEB"),
        PrimaryText = C("#FFFFFF"),
        SecondaryText = C("#E0E0E0"),
        DisabledText = C("#808080"),
        Accent = C("#1F6FEB"),
        AccentHover = C("#FFFFFF"),
        SecondaryButton = C("#333333"),
        SecondaryButtonBorder = C("#FFFFFF"),
        Error = C("#FF0000"),
        Success = C("#00FF00"),
        Warning = C("#FFFF00"),
        SectionHeader1 = C("#00FFFF"),
        SectionHeader2 = C("#00FF00"),
        SectionHeader3 = C("#FFFF00"),
        SectionHeader4 = C("#FF00FF"),
        TransferIndicator = C("#00FFFF"),
        RecordIndicator = C("#FF0000"),
        ErrorPanelBackground = C("#000000"),
        ErrorPanelBorder = C("#FF0000"),
        ErrorPanelText = C("#FF6666"),
        CodeKeyword = C("#00FF00"),
        CodeString = C("#FFFF00"),
    };

    // IMPORTANT: All must be declared AFTER all individual themes (C# static field initialization order)
    public static readonly ThemeDefinition[] All = new[]
    {
        Dark, Light, RetroGreen, AmberCrt, Nord, SolarizedDark, Synthwave, HighContrast
    };
}
