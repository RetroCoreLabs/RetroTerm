namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Which shape the ReGIS graphics input cursor is drawn as.
/// </summary>
/// <remarks>
/// <para><b>Chapter 2, "Graphics Input Cursor"</b></para>
/// "The I suboption lets you select the style of graphics input cursor." The table it prints numbers
/// them: omitted is the crosshair, 0 is "Crosshair (default)", 1 is the diamond, 2 is the crosshair
/// again, 3 the rubber band line and 4 the rubber band rectangle.
///
/// Two numbers giving the same shape is not a transcription slip - the same table does it for the
/// OUTPUT cursor as well, where "0 or 1" both give the diamond. Both are kept as written.
///
/// <para><b>The shapes themselves are chapter 1's</b></para>
/// Each value carries the manual's own sentence, because none of them can be judged against a
/// photograph - no fixture in any corpus turns an input cursor on.
/// </remarks>
public enum RegisCursorStyle
{
    /// <summary>
    /// Two lines crossing the whole screen. Numbers 0 and 2, and the default.
    /// </summary>
    /// <remarks>
    /// "This cursor is a horizontal and a vertical line. The horizontal line is the width of the
    /// screen, and the vertical line is the height of the screen. The two lines intersect at the
    /// active position... The crosshair is the default input cursor."
    /// </remarks>
    Crosshair,

    /// <summary>
    /// A diamond standing on its point at the cursor. Number 1.
    /// </summary>
    /// <remarks>
    /// "This cursor is a 21 x 21 pixel diamond. You can use this cursor for input and output
    /// operations."
    /// </remarks>
    Diamond,

    /// <summary>
    /// A line from the drawing point to the cursor. Number 3.
    /// </summary>
    /// <remarks>
    /// "This cursor is a single line, with its origin fixed at the current drawing (output) position
    /// and its endpoint at the current cursor position. You can only use this cursor as an input
    /// cursor."
    /// </remarks>
    RubberBandLine,

    /// <summary>
    /// A rectangle from the drawing point to the cursor. Number 4.
    /// </summary>
    /// <remarks>
    /// "This cursor is a rectangle, with one corner fixed at the current drawing (output) position
    /// and the opposite corner at the current cursor position. You can only use this cursor as an
    /// input cursor."
    /// </remarks>
    RubberBandRectangle
}
