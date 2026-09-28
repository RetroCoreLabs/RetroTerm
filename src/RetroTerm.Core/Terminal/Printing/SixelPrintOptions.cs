namespace RetroTerm.Core.Terminal.Printing;

/// <summary>
/// How a graphics print is encoded, from the three print options a VT had.
/// </summary>
/// <remarks>
/// <para><b>Where these come from</b></para>
/// DEC STD 070 section 7.8, Graphics Printing, and the "Printing Graphics" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>. See
/// <c>docs\PRINTING-SIXEL-TO-PDF-DESIGN-2026-08-17.md</c>.
///
/// <para><b>Compressed, expanded, rotated</b></para>
/// A VT offered exactly three shapes of print, and two of them are DEC-private modes:
///  - Compressed, the default: the print comes out the size and aspect of a VT240's.
///  - Expanded, DECGEPM, private mode 43: each sixel sent twice in horizontal succession, so the
///    picture is twice as wide. Needs 13 inch paper in portrait.
///  - Rotated, DECGRPM, private mode 47: turned 90 degrees so an expanded image fits one 8.5 inch
///    page. Rotated images are always expanded.
/// </remarks>
public sealed class SixelPrintOptions
{
    /// <summary>
    /// The sixel level the printer speaks.
    /// </summary>
    /// <remarks>
    /// Level 1 is the factory default and the older printers: no Set Raster Attributes, no
    /// background select, no horizontal grid size, aspect fixed at 2:1, and the control string is
    /// always the bare seven-bit form. Level 2, which a VT340 speaks, has all of them.
    /// </remarks>
    public int Level { get; set; } = 2;

    /// <summary>
    /// DECGPCM, private mode 44 - send colour rather than black and white.
    /// </summary>
    /// <remarks>
    /// Black and white by default, and the manual says only use colour on a VT340. A monochrome
    /// dump is the logical OR of every plane: any pixel with a bit set anywhere is ink.
    /// </remarks>
    public bool Colour { get; set; }

    /// <summary>
    /// DECGEPM, private mode 43 - expanded print, each sixel sent twice horizontally.
    /// </summary>
    public bool Expanded { get; set; }

    /// <summary>
    /// DECGRPM, private mode 47 - rotated print, 90 degrees counter-clockwise.
    /// </summary>
    /// <remarks>
    /// <para><b>Counter-clockwise, and the reason is hole punching</b></para>
    /// DEC STD 070: "the VT240 rotates the image counter clockwise, so that the left side of the
    /// paper (as it comes out of a typical dot matrix printer) corresponds to the top of the image
    /// on the terminal screen. This scanning order was chosen to allow punching holes for a
    /// looseleaf notebook on the left side of the page as it comes out of the printer."
    ///
    /// Nothing on the SCREEN ever rotated. Printing rotated, on a mode.
    /// </remarks>
    public bool Rotated { get; set; }

    /// <summary>
    /// Print the background - pixels with no bits set in any plane get colour index 0.
    /// </summary>
    /// <remarks>
    /// Off by default, so a drawing prints as ink on white paper rather than as a solid black
    /// rectangle with the picture cut out of it.
    /// </remarks>
    public bool PrintBackground { get; set; }
}
