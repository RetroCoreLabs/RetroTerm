using System;

namespace RetroTerm.Core.Terminal.Input;

/// <summary>
/// Which mouse events the host asked to be told about.
/// </summary>
public enum MouseTrackingMode
{
    /// <summary>
    /// Nothing is reported. The default, and what every terminal here did until now.
    /// </summary>
    Off,

    /// <summary>
    /// X10 compatibility (mode 9): button presses only. No releases, no modifiers.
    /// </summary>
    PressOnly,

    /// <summary>
    /// Normal tracking (mode 1000): presses and releases.
    /// </summary>
    PressAndRelease,

    /// <summary>
    /// Button-event tracking (mode 1002): presses, releases, and movement while a button is held.
    /// </summary>
    ButtonMotion,

    /// <summary>
    /// Any-event tracking (mode 1003): everything, including movement with no button down.
    /// </summary>
    AllMotion,
}

/// <summary>
/// How a mouse report is written on the wire.
/// </summary>
public enum MouseEncoding
{
    /// <summary>
    /// The original X10 form: <c>CSI M Cb Cx Cy</c>, each value offset by 32.
    /// </summary>
    /// <remarks>
    /// One byte per coordinate, so nothing past column or row 223 can be said at all. That limit is
    /// why <see cref="Sgr"/> exists and why any terminal wider than 223 columns needs it.
    /// </remarks>
    X10,

    /// <summary>
    /// The SGR form (mode 1006): CSI, a less-than sign, then b ; x ; y M for a press, lower-case m for a release.
    /// </summary>
    Sgr,
}

/// <summary>
/// What happened to the mouse.
/// </summary>
public enum MouseEventKind
{
    /// <summary>
    /// A button went down.
    /// </summary>
    Press,

    /// <summary>
    /// A button came up.
    /// </summary>
    Release,

    /// <summary>
    /// The pointer moved.
    /// </summary>
    Motion,
}

/// <summary>
/// Which button, in the numbering the wire format uses.
/// </summary>
public enum MouseButton
{
    /// <summary>
    /// Left.
    /// </summary>
    Left = 0,

    /// <summary>
    /// Middle.
    /// </summary>
    Middle = 1,

    /// <summary>
    /// Right.
    /// </summary>
    Right = 2,

    /// <summary>
    /// No button - used for movement with nothing held.
    /// </summary>
    None = 3,

    /// <summary>
    /// Wheel up. Reported as a press with no matching release, which is what a wheel is.
    /// </summary>
    WheelUp = 64,

    /// <summary>
    /// Wheel down.
    /// </summary>
    WheelDown = 65,
}

/// <summary>
/// Modifier keys held during a mouse event.
/// </summary>
[Flags]
public enum MouseModifiers
{
    /// <summary>
    /// None held.
    /// </summary>
    None = 0,

    /// <summary>
    /// Shift.
    /// </summary>
    Shift = 4,

    /// <summary>
    /// Meta, which on a PC keyboard is Alt.
    /// </summary>
    Meta = 8,

    /// <summary>
    /// Control.
    /// </summary>
    Control = 16,
}

/// <summary>
/// Turns a mouse event into the bytes a host asked for, or decides there is nothing to send.
/// </summary>
/// <remarks>
/// <para><b>Two separate questions</b></para>
/// The mode says WHICH events are reported and the encoding says HOW they are written, and a host
/// sets them with different sequences. Keeping them apart is not tidiness: mode 1006 changes the
/// wire format without changing what is reported, so a terminal that treated them as one setting
/// would either lose the mode when the encoding changed or ignore the encoding entirely.
///
/// <para><b>Nothing here touches the screen or the connection</b></para>
/// It answers "what bytes, if any" and the emulator sends them. That is what makes every rule in
/// here testable without a mouse, a window, or a host.
/// </remarks>
public sealed class MouseTracker
{
    /// <summary>
    /// Longest report this can produce, for sizing a caller's buffer.
    /// </summary>
    /// <remarks>
    /// The SGR form is the long one: introducer, up to three digits of button, two coordinates of
    /// up to five digits each, separators and a final byte.
    /// </remarks>
    public const int MaximumReportLength = 20;

    /// <summary>
    /// The largest coordinate the X10 encoding can carry.
    /// </summary>
    /// <remarks>
    /// 255 minus the 32 offset. A position past this cannot be written in one byte, and xterm sends
    /// nothing rather than a wrong number - so this does the same.
    /// </remarks>
    private const int X10MaximumCoordinate = 223;

    /// <summary>
    /// Which events the host wants.
    /// </summary>
    public MouseTrackingMode Mode { get; set; } = MouseTrackingMode.Off;

    /// <summary>
    /// How reports are written.
    /// </summary>
    public MouseEncoding Encoding { get; set; } = MouseEncoding.X10;

    /// <summary>
    /// Whether anything at all is being reported.
    /// </summary>
    public bool IsTracking => Mode != MouseTrackingMode.Off;

    /// <summary>
    /// Whether this event is one the current mode reports.
    /// </summary>
    /// <param name="kind">
    /// What happened.
    /// </param>
    /// <param name="button">
    /// Which button, or <see cref="MouseButton.None"/> for movement with nothing held.
    /// </param>
    /// <returns>
    /// True when a report should be sent.
    /// </returns>
    public bool Reports(MouseEventKind kind, MouseButton button)
    {
        switch (Mode)
        {
            case MouseTrackingMode.Off:
                return false;

            case MouseTrackingMode.PressOnly:
                // X10 knew only about presses. A release reported to a program expecting the X10
                // form reads as another press of button 3.
                return kind == MouseEventKind.Press;

            case MouseTrackingMode.PressAndRelease:
                return kind != MouseEventKind.Motion;

            case MouseTrackingMode.ButtonMotion:
                // Movement only while something is held - this is the mode a program uses to drag.
                return kind != MouseEventKind.Motion || button != MouseButton.None;

            case MouseTrackingMode.AllMotion:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Builds the report for one mouse event.
    /// </summary>
    /// <param name="kind">
    /// What happened.
    /// </param>
    /// <param name="button">
    /// Which button.
    /// </param>
    /// <param name="column">
    /// Column, counting from 1 at the left.
    /// </param>
    /// <param name="row">
    /// Row, counting from 1 at the top.
    /// </param>
    /// <param name="modifiers">
    /// Modifier keys held. Ignored in <see cref="MouseTrackingMode.PressOnly"/>, which predates
    /// them.
    /// </param>
    /// <param name="destination">
    /// At least <see cref="MaximumReportLength"/> bytes.
    /// </param>
    /// <param name="length">
    /// Bytes written.
    /// </param>
    /// <returns>
    /// False when this event is not reported, or when the position cannot be written in the current
    /// encoding.
    /// </returns>
    public bool TryBuildReport(MouseEventKind kind, MouseButton button, int column, int row,
        MouseModifiers modifiers, Span<byte> destination, out int length)
    {
        length = 0;
        if (!Reports(kind, button)) return false;
        if (column < 1 || row < 1) return false;

        int code = ButtonCode(kind, button, modifiers);

        return Encoding == MouseEncoding.Sgr
            ? TryBuildSgr(kind, code, column, row, destination, out length)
            : TryBuildX10(code, column, row, destination, out length);
    }

    /// <summary>
    /// The button field: which button, plus the modifier bits, plus 32 while moving.
    /// </summary>
    /// <remarks>
    /// A RELEASE is button 3 in the X10 form - the terminal says "something came up" and not which,
    /// because the format has nowhere to put it. The SGR form keeps the real button and marks the
    /// release with its final byte instead, which is the reason it exists.
    /// </remarks>
    private int ButtonCode(MouseEventKind kind, MouseButton button, MouseModifiers modifiers)
    {
        int code;

        if (kind == MouseEventKind.Release && Encoding == MouseEncoding.X10)
        {
            code = (int)MouseButton.None;
        }
        else
        {
            code = (int)button;
        }

        if (kind == MouseEventKind.Motion)
        {
            // Bit 5 says "this is a drag rather than a fresh press".
            code += 32;
        }

        // X10 predates modifiers entirely; adding them would change the button number a program
        // reads.
        if (Mode != MouseTrackingMode.PressOnly)
        {
            code |= (int)modifiers;
        }

        return code;
    }

    private static bool TryBuildX10(int code, int column, int row, Span<byte> destination,
        out int length)
    {
        length = 0;

        // One byte per coordinate. Past 223 there is no byte to use, and a wrong position is worse
        // than none - a program would act on the wrong cell.
        if (column > X10MaximumCoordinate || row > X10MaximumCoordinate) return false;
        if (destination.Length < 6) return false;

        destination[0] = 0x1B;
        destination[1] = (byte)'[';
        destination[2] = (byte)'M';
        destination[3] = (byte)(code + 32);
        destination[4] = (byte)(column + 32);
        destination[5] = (byte)(row + 32);
        length = 6;
        return true;
    }

    private static bool TryBuildSgr(MouseEventKind kind, int code, int column, int row,
        Span<byte> destination, out int length)
    {
        length = 0;

        // No offsets and no one-byte limit, so a wide screen can be reported honestly. The final
        // byte carries press versus release.
        var text = "\x1b[<" + code.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";" + column.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";" + row.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (kind == MouseEventKind.Release ? "m" : "M");

        if (destination.Length < text.Length) return false;

        for (int i = 0; i < text.Length; i++)
        {
            destination[i] = (byte)text[i];
        }

        length = text.Length;
        return true;
    }
}
