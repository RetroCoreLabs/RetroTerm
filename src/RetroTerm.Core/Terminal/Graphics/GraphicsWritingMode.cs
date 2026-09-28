namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// What happens to a pixel already on the surface when something is drawn over it.
/// </summary>
/// <remarks>
/// <para><b>Where these come from</b></para>
/// The "4010/4014 Mode" chapter of
/// <c>spec\DEC\EK-VT3XX-GP-002_VT330_VT340_Graphics_Programming_May88.pdf</c>, "Select Raster
/// Writing Mode Features". The manual flags them itself: "NOTE: These sequences are not part of
/// the 4010/4014 protocol." They are DEC's addition, and their purpose is given in the chapter's
/// restrictions section - a real 4014 could draw WITHOUT storing, refreshing the image to keep it
/// visible, and "the VT300 can simulate write-through functions by using raster writing modes".
///
/// <para><b>Why it is surface state rather than a parameter</b></para>
/// A writing mode applies to everything drawn while it is in force - vectors, points and text
/// alike - and it is set by a sequence of its own rather than per shape. Threading it through
/// every draw call would change signatures that most protocols have no use for; a property on the
/// surface leaves them untouched and lets <c>SetPixel</c> be the one place that honours it.
/// </remarks>
public enum GraphicsWritingMode
{
    /// <summary>
    /// "Set dots on" - the ordinary case, and what every protocol that has no writing mode gets.
    /// </summary>
    Overlay = 0,

    /// <summary>
    /// "Sets dots off" - drawing rubs out instead of marking.
    /// </summary>
    Erase = 1,

    /// <summary>
    /// "Complements dots" - a lit pixel goes out and an unlit one comes on.
    /// </summary>
    /// <remarks>
    /// This is what makes a rubber-band line possible: draw it once to show it, draw it again over
    /// the same pixels to take it away and leave whatever was underneath.
    /// </remarks>
    Complement = 2,
}
