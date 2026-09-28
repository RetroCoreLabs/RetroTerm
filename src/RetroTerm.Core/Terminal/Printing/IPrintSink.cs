using System;

namespace RetroTerm.Core.Terminal.Printing;

/// <summary>
/// Where a print job goes once the terminal has decided to print something.
/// </summary>
/// <remarks>
/// <para><b>Why this is an interface, and why it lives in Core</b></para>
/// A VT's printer hung off the TERMINAL, not off the host: the terminal had a printer port, and
/// media copy, autoprint and the Print key all fed it. This is that port. Core is netstandard2.1
/// and knows nothing about drawing libraries or file formats, so the port is an interface and the
/// thing that writes a PDF lives in Desktop.
///
/// <para><b>What arrives here</b></para>
/// Bytes, exactly as the host sent them, plus the page boundaries the terminal decided on. The
/// sink is NOT told whether a byte came from printer controller mode, autoprint or a screen print
/// - a printer could not tell either, and a sink that behaved differently per source would be
/// inventing a distinction the hardware did not have.
///
/// See <c>docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md</c>.
/// </remarks>
public interface IPrintSink
{
    /// <summary>
    /// Sends bytes to the printer.
    /// </summary>
    /// <remarks>
    /// A span rather than an array, so routing a chunk of host data copies nothing on the way.
    /// </remarks>
    /// <param name="data">
    /// The bytes, as received.
    /// </param>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    /// Ends the current page and starts a new one.
    /// </summary>
    /// <remarks>
    /// Raised by a form feed in the print stream and by DECPFF after a screen print. A sink that
    /// has nothing on the current page may ignore it rather than emitting a blank sheet.
    /// </remarks>
    void FormFeed();

    /// <summary>
    /// Ends the current print job, so a sink that buffers can flush what it is holding.
    /// </summary>
    /// <remarks>
    /// <para><b>FLUSH, NOT CLOSE</b></para>
    /// Called whenever printer controller mode ends and when the terminal is reset - so it happens
    /// once per print job, and a terminal makes many. It does NOT mean the sink is finished; that
    /// is what disposing it means. A sink that closed its output here would silently drop every
    /// job after the first, which is exactly what the PDF sink did until printing sixteen pictures
    /// in a row showed only the first one had survived.
    ///
    /// Calling it twice must be harmless - a host may turn the mode off having never turned it on.
    /// </remarks>
    void EndJob();
}
