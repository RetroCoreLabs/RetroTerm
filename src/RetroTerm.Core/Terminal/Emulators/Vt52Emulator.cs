using System;
using RetroTerm.Core.Terminal.Parsing;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Terminal.Emulators;

/// <summary>
/// The DEC VT52 - the terminal that came before ANSI.
/// </summary>
/// <remarks>
/// <para><b>Its escape sequences are not CSI sequences</b></para>
/// A VT52 predates ECMA-48 entirely. Its commands are a bare ESC and one letter - <c>ESC A</c> is
/// cursor up, <c>ESC J</c> erases to the end of the screen - with no introducer, no parameters and
/// no final byte. So this emulator intercepts the two-character escapes and never reaches the
/// base's ANSI handling for them.
///
/// <para><b>Direct cursor addressing reads two RAW bytes</b></para>
/// <c>ESC Y row col</c> carries its coordinates as bytes offset by 0x20, and those bytes may be
/// anything at all - including 0x1B. They must not go through escape processing, which is exactly
/// what the parser's raw-byte collection is for. Its own documentation names this sequence as the
/// reason it exists; until now nothing used it, and a VT52 host addressing the cursor would have
/// printed the coordinates on screen.
///
/// <para><b>What it does not have</b></para>
/// Colour, scrolling regions, an alternate screen, insert and delete, selective erase. A VT52 had
/// none of them, and the profile claims none of them. What it does have that later terminals lost
/// is <c>ESC I</c>, reverse line feed, and the identification reply <c>ESC / Z</c>.
/// </remarks>
public sealed class Vt52Emulator : TerminalEmulatorBase
{
    /// <summary>
    /// Builds a VT52.
    /// </summary>
    /// <param name="width">
    /// Columns. A real VT52 had 80.
    /// </param>
    /// <param name="height">
    /// Rows. A real VT52 had 24.
    /// </param>
    /// <param name="maxScrollback">
    /// Lines of history to keep. The hardware had none; the window this runs in does.
    /// </param>
    public Vt52Emulator(int width = 80, int height = 24, int maxScrollback = 1000)
        : base(width, height, maxScrollback)
    {
    }

    /// <inheritdoc/>
    public override TerminalProfile Profile => TerminalProfile.VT52;

    /// <summary>
    /// Whether the VT52 graphics character set is invoked, from <c>ESC F</c> and <c>ESC G</c>.
    /// </summary>
    public bool GraphicsCharacterSet => Vt52GraphicsCharacterSet;

    /// <summary>
    /// Whether the keypad sends application sequences, from <c>ESC =</c> and <c>ESC ></c>.
    /// </summary>
    /// <remarks>
    /// Reads the base's own flag rather than keeping a second one. The keyboard mapper already
    /// asks the base for it through GetActiveModes, so a private copy here would be the state the
    /// terminal reported and not the state the keyboard used.
    /// </remarks>
    public bool KeypadIsApplicationMode => ApplicationKeypad;

    /// <inheritdoc/>
    protected override void HandleEscapeSequence(EscapeSequenceParser parser)
    {
        // The interpretation lives on the base, because a VT100 in VT52 mode has to do exactly the
        // same thing - see DECANM. Two copies of ESC A through ESC Z is how one of them ends up
        // fixed and the other does not.
        if (TryHandleVt52Escape(parser)) return;

        base.HandleEscapeSequence(parser);
    }

    /// <inheritdoc/>
    public override string ToString() => "VT52";
}
