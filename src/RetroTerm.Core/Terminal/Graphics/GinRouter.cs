using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Turns a pointer on the screen into the bytes a host expects back — the reverse of the keyboard
/// path, and the last block of the graphics foundation.
///
/// A pointer event arrives in SURFACE pixels because that is where the mouse is. The host wants the
/// terminal's own logical space. The conversion is not done here: it is asked of
/// <see cref="GraphicsViewport"/>, the one owner of that arithmetic, because a second copy of the Y
/// flip is precisely how a crosshair ends up half a screen from where it is pointing.
///
/// GIN is MODAL. A terminal is not always reporting: the host arms it, a crosshair appears, the user
/// clicks or types, one report goes out and the mode ends. Reporting while disarmed would send a
/// host bytes it never asked for, in the middle of whatever else it was reading.
/// </summary>
public sealed class GinRouter
{
    private readonly GraphicsViewport _viewport;

    /// <summary>
    /// Whether the host has armed GIN and a report is wanted. False by default: a terminal that
    /// started reporting pointer movement unasked would corrupt every session that never wanted
    /// graphics at all.
    /// </summary>
    public bool IsArmed { get; private set; }

    /// <summary>
    /// Whether to answer as a Norsk Data terminal - two extra bytes identifying the model - or as
    /// a plain Tektronix 4014.
    /// </summary>
    public bool ReportAsNorskData { get; set; }

    /// <summary>
    /// First ND model byte, sent only when <see cref="ReportAsNorskData"/> is set.
    /// </summary>
    public byte NorskDataModelByte1 { get; set; }

    /// <summary>
    /// Second ND model byte.
    /// </summary>
    public byte NorskDataModelByte2 { get; set; }

    /// <summary>
    /// Where the crosshair is, in surface pixels. Only meaningful while armed.
    /// </summary>
    public int CrosshairSurfaceX { get; private set; }

    /// <summary>
    /// Where the crosshair is, in surface pixels.
    /// </summary>
    public int CrosshairSurfaceY { get; private set; }

    /// <param name="viewport">
    /// The transform that converts between surface pixels and the terminal's own space.
    /// </param>
    public GinRouter(GraphicsViewport viewport)
    {
        _viewport = viewport ?? throw new ArgumentNullException(nameof(viewport));
    }

    /// <summary>
    /// Longest report this can produce, for sizing a caller's buffer.
    /// </summary>
    public int MaximumReportLength => TektronixGinEncoder.NorskDataReportLength;

    /// <summary>
    /// Arms GIN. The host has asked for a crosshair and wants one report back.
    /// </summary>
    public void Arm()
    {
        IsArmed = true;
    }

    /// <summary>
    /// Disarms without reporting - the host cancelled, or the terminal was reset.
    /// </summary>
    public void Disarm()
    {
        IsArmed = false;
    }

    /// <summary>
    /// Moves the crosshair. Does not report: a real terminal reports when the user ACTS, not on
    /// every twitch of the pointer, and streaming a report per mouse move would flood the line.
    /// </summary>
    /// <param name="surfaceX">
    /// Pointer position in surface pixels.
    /// </param>
    /// <param name="surfaceY">
    /// Pointer position in surface pixels.
    /// </param>
    public void MoveCrosshair(int surfaceX, int surfaceY)
    {
        CrosshairSurfaceX = surfaceX;
        CrosshairSurfaceY = surfaceY;
    }

    /// <summary>
    /// Builds the report for the crosshair's current position and disarms.
    /// </summary>
    /// <param name="lead">
    /// The key the user pressed, or the terminal status byte when the host asked with
    /// <c>ESC ENQ</c>. The wire format is the same either way.
    /// </param>
    /// <param name="destination">
    /// At least <see cref="MaximumReportLength"/> bytes.
    /// </param>
    /// <param name="length">
    /// Bytes written.
    /// </param>
    /// <returns>
    /// False when GIN was not armed, in which case nothing is written.
    /// </returns>
    public bool TryBuildReport(byte lead, Span<byte> destination, out int length)
    {
        length = 0;
        if (!IsArmed) return false;

        // The viewport does the flip and the clamp. Nothing here knows which way Y runs.
        _viewport.ToLogical(CrosshairSurfaceX, CrosshairSurfaceY, out int logicalX, out int logicalY);

        length = ReportAsNorskData
            ? TektronixGinEncoder.EncodeNorskData(destination, lead, logicalX, logicalY,
                NorskDataModelByte1, NorskDataModelByte2)
            : TektronixGinEncoder.Encode(destination, lead, logicalX, logicalY);

        // One arming, one report. The host arms again when it wants another.
        IsArmed = false;
        return true;
    }
}
