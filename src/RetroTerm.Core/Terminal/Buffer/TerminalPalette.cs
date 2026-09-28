namespace RetroTerm.Core.Terminal.Buffer;

/// <summary>
/// THE 256-colour palette. One table, in Core, for everything that needs to know what colour
/// index N is.
///
/// WHY THIS EXISTS. There were two of these and they disagreed. The renderer built its brushes
/// from the xterm ramp (0, 95, 135, 175, 215, 255) while <see cref="TerminalColor"/> computed
/// <c>(idx / 36) * 51</c>, an even six-way split giving 0, 51, 102, 153, 204, 255. Both are
/// plausible-looking arithmetic; only the first is what xterm actually does, and the two agree at
/// exactly the two ends — which is why the existing test, checking index 16 and index 231, passed
/// while 214 of the 216 cube colours were reported wrong by everything that asked Core.
///
/// The renderer painted the right colour on screen and Core answered a different one to anything
/// that asked — so the screen and any exported/reported colour could not be made to agree.
/// </summary>
public static class TerminalPalette
{
    /// <summary>
    /// The six levels of the 6x6x6 colour cube, as xterm defines them. NOT an even split: the gap
    /// from 0 to 95 is deliberately wider than the 40 between the rest, so that dark colours stay
    /// distinguishable from black.
    /// </summary>
    private static readonly byte[] CubeLevels = { 0, 95, 135, 175, 215, 255 };

    /// <summary>
    /// The standard 16, as R,G,B triples at index*3. These are the xterm defaults; a terminal
    /// theme can override what is drawn, but this is what an unthemed index means.
    /// </summary>
    private static readonly byte[] BaseColours =
    {
        0, 0, 0,          // 0  Black
        205, 0, 0,        // 1  Red
        0, 205, 0,        // 2  Green
        205, 205, 0,      // 3  Yellow
        0, 0, 238,        // 4  Blue
        205, 0, 205,      // 5  Magenta
        0, 205, 205,      // 6  Cyan
        229, 229, 229,    // 7  White
        127, 127, 127,    // 8  Bright Black (Gray)
        255, 0, 0,        // 9  Bright Red
        0, 255, 0,        // 10 Bright Green
        255, 255, 0,      // 11 Bright Yellow
        92, 92, 255,      // 12 Bright Blue
        255, 0, 255,      // 13 Bright Magenta
        0, 255, 255,      // 14 Bright Cyan
        255, 255, 255,    // 15 Bright White
    };

    /// <summary>
    /// The RGB triple for a palette index.
    ///
    /// 0-15 are the standard colours, 16-231 the 6x6x6 cube, 232-255 the 24-step grey ramp.
    /// Every byte value is covered, so there is no "unknown index" case to get wrong.
    /// </summary>
    public static (byte R, byte G, byte B) GetRgb(byte index)
    {
        if (index < 16)
        {
            int at = index * 3;
            return (BaseColours[at], BaseColours[at + 1], BaseColours[at + 2]);
        }

        if (index < 232)
        {
            // Cube ordering is red-major: index = 16 + 36*r + 6*g + b.
            int cubeIndex = index - 16;
            return (CubeLevels[cubeIndex / 36], CubeLevels[(cubeIndex / 6) % 6], CubeLevels[cubeIndex % 6]);
        }

        // 24 greys from 8 to 238 in steps of 10. Neither pure black nor pure white — those are
        // already in the cube, and repeating them here would waste two of the 24 steps.
        byte grey = (byte)(8 + (index - 232) * 10);
        return (grey, grey, grey);
    }
}
