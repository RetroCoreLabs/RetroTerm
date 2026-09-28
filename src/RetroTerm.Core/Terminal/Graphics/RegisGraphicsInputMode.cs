namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Which ReGIS graphics input mode the terminal is in, if any.
/// </summary>
/// <remarks>
/// <para><b>Chapter 10, "Graphics Input Modes"</b></para>
/// "This option lets you set ReGIS to one of two graphics input modes, one-shot or multiple. In a
/// graphics input mode, you can use a locator device (mouse or graphics tablet) to move the graphics
/// input cursor and send position reports."
///
/// <para><b>Off is not one of the manual's two</b></para>
/// The manual calls one-shot "the default input mode", but it means the default of the two once a
/// host has asked for graphics input at all - a terminal nobody has sent <c>R(I0)</c> to shows no
/// input cursor and swallows no data. <see cref="Off"/> is that state, and it is what this decoder
/// starts in.
/// </remarks>
public enum RegisGraphicsInputMode
{
    /// <summary>
    /// No graphics input. No input cursor, nothing buffered.
    /// </summary>
    Off,

    /// <summary>
    /// One-shot, entered by <c>R(I0)</c>.
    /// </summary>
    /// <remarks>
    /// "In one-shot mode, the terminal suspends processing of new data from the application until
    /// ReGIS sends a position report. The terminal buffers any data received from the application in
    /// this mode." One report and the terminal leaves the mode again.
    /// </remarks>
    OneShot,

    /// <summary>
    /// Multiple, entered by <c>R(I1)</c>.
    /// </summary>
    /// <remarks>
    /// "This mode lets you send more than one cursor position report without exiting graphics input
    /// mode. The terminal immediately processes characters it receives from the host, instead of
    /// buffering them as in one-shot mode."
    ///
    /// The arrow keys do NOT move the cursor here - "If you press an arrow key in multiple mode, the
    /// terminal sends that key's escape sequence to the host."
    /// </remarks>
    Multiple
}
