using RetroTerm.Core.Terminal.Emulators.TDV;

namespace RetroTerm.Tests.TDV.TestHelpers;

/// <summary>
/// Concrete implementation of TDVEmulatorBase for testing
/// </summary>
public class TestTDVEmulatorBase : TDVEmulatorBase
{
    public TestTDVEmulatorBase(int width, int height) : base(width, height) { }

    // The base is ECMA-48, not a terminal, so every emulator must declare what it is.
    public override RetroTerm.Core.Terminal.Profiles.TerminalProfile Profile
        => RetroTerm.Core.Terminal.Profiles.TerminalProfile.ForTdv("TDV", "\x1b[?1;2c");

    /// <summary>
    /// Override abstract methods for testing
    /// </summary>
    public override string GetTerminalType() => "TDV";

    /// <summary>
    /// Override abstract methods for testing
    /// </summary>
    public override string GetTerminalCapabilities() => "TDV+NDGRAPHICS+NDWORKAREA+NDPROTECTED+NDLEDS+NDPUSHKEYS";

    /// <summary>
    /// Make ResetToInitialState accessible for testing
    /// </summary>
    public new void ResetToInitialState()
    {
        base.ResetToInitialState();
    }

    /// <summary>
    /// Expose internal state for test assertions
    /// </summary>
    public bool IsSmoothScrollMode => SmoothScrollMode;

    /// <summary>
    /// Beginning of Line Wrap Mode, NDBLWM. The manuals call this a WRAP mode, not a blink mode -
    /// ND-1200 section 4.10. It shares the base class's reverse-wrap flag.
    /// </summary>
    public bool IsBeginningOfLineWrapMode => ReverseWrapMode;

    /// <summary>
    /// End of Line Wrap Mode, NDELWM. ND-1200 section 4.11. Shares the base class's autowrap flag.
    /// </summary>
    public bool IsEndOfLineWrapMode => AutoWrapMode;

    /// <summary>
    /// Numeric pad in "Function" mode rather than "Numeric".
    /// TDV 2200/9 S User's Guide section 11.2.
    /// </summary>
    public bool IsNumericPadFunctionMode => NumericPadFunctionMode;

    /// <summary>
    /// Helper method to convert string to byte array for testing
    /// </summary>
    public static ReadOnlySpan<byte> StringToBytes(string input)
    {
        return System.Text.Encoding.UTF8.GetBytes(input);
    }
}
