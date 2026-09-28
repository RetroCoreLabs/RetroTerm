using System;
using System.Text;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Avalonia.ManualPlan;

/// <summary>
/// M6.4j - changing a line attribute clears the ReGIS graphics on that row, and setting it to what
/// it already was does not.
/// </summary>
/// <remarks>
/// <para><b>Both halves are measured, not derived</b></para>
/// hackerb9's <c>regis-decdwl.sh</c> exists to ask this exact question, and its photograph
/// <c>regis-decdwl.png</c> answers it. The script draws ONE flag across six rows that differ only in
/// the order of three things - line Attributes, Text and Graphics - and prints the ordering beside
/// each row:
///  - 1. ATG, 3. TAG and 5. AGT set the attribute BEFORE the graphics. The flag survives.
///  - 2. GAT, 4. GTA and 6. TGA set it AFTER. The hardware erases the flag on those rows.
/// The no-change half is the useful one, and the script says why: it means a program can update the
/// text on a double-width line without erasing the picture behind it, because setting the attribute
/// the line already has costs nothing.
/// <para><b>What is deliberately NOT done</b></para>
/// The text is untouched. The fixture is explicit that unlike Sixel, ReGIS "does not reset the line
/// attribute flags to single width nor does it clear the underlying text buffer" - and calls that a
/// good thing, because it is what lets wide text and graphics coexist.
/// </remarks>
[Collection("Avalonia")]
public class M6RegisLineAttributeTests
{
    /// <summary>
    /// Escape, as bytes. Written this way because "\x1b#6" is a single character - <c>\x</c> takes
    /// up to four hex digits and swallows the '#'.
    /// </summary>
    private const byte Esc = 0x1B;

    /// <summary>
    /// Sends a sequence built from an escape and some ASCII.
    /// </summary>
    /// <param name="emulator">
    /// Terminal to send to.
    /// </param>
    /// <param name="rest">
    /// What follows the escape.
    /// </param>
    private static void SendEscape(TerminalEmulatorBase emulator, string rest)
    {
        byte[] bytes = new byte[rest.Length + 1];
        bytes[0] = Esc;
        for (int i = 0; i < rest.Length; i++) bytes[i + 1] = (byte)rest[i];
        emulator.ProcessData(bytes);
    }

    /// <summary>
    /// Fills a band of the ReGIS plane so there is something to erase.
    /// </summary>
    /// <param name="emulator">
    /// Terminal to draw on.
    /// </param>
    /// <param name="top">
    /// Top of the band in plane pixels.
    /// </param>
    /// <param name="height">
    /// Height of the band in plane pixels.
    /// </param>
    private static void DrawBand(TerminalEmulatorBase emulator, int top, int height)
    {
        var regis = new StringBuilder();
        regis.Append("P1p");
        regis.Append("W(I2)");
        regis.Append("P[0,").Append(top).Append(']');
        regis.Append("F(V[400,").Append(top).Append("][400,").Append(top + height - 1)
             .Append("][0,").Append(top + height - 1).Append("])");

        byte[] head = new byte[regis.Length + 1];
        head[0] = Esc;
        for (int i = 0; i < regis.Length; i++) head[i + 1] = (byte)regis[i];
        emulator.ProcessData(head);
        emulator.ProcessData(new byte[] { Esc, (byte)'\\' });
    }

    /// <summary>
    /// Counts the lit pixels on one text row's band of the ReGIS plane.
    /// </summary>
    /// <param name="emulator">
    /// Terminal to measure.
    /// </param>
    /// <param name="row">
    /// Text row.
    /// </param>
    /// <returns>
    /// How many pixels are not transparent.
    /// </returns>
    private static int LitOnRow(TerminalEmulatorBase emulator, int row)
    {
        var plane = emulator.Graphics?.FindPlane(TerminalEmulatorBase.RegisPlaneId);
        if (plane == null) return 0;

        int cellHeight = emulator.GraphicsCellHeight;
        int top = row * cellHeight;
        int lit = 0;

        var surface = plane.Surface;
        for (int y = top; y < top + cellHeight && y < surface.Height; y++)
        {
            for (int x = 0; x < surface.Width; x++)
            {
                if (!surface.GetPixel(x, y).IsTransparent) lit++;
            }
        }

        return lit;
    }

    [AvaloniaFact]
    public void GraphicsDrawnBeforeTheAttributeAreErasedByIt()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // Row 4 of the plane, which is one character row.
        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight);

        Assert.True(LitOnRow(emulator, 4) > 0, "the band drew nothing, so there is nothing to erase");

        // Now make row 4 double-width - the fixture's cases 2, 4 and 6.
        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        Assert.Equal(0, LitOnRow(emulator, 4));
    }

    [AvaloniaFact]
    public void GraphicsDrawnAfterTheAttributeSurvive()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        // Attribute first - the fixture's cases 1, 3 and 5.
        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight);

        Assert.True(LitOnRow(emulator, 4) > 0, "the flag was erased by an attribute set before it");
    }

    [AvaloniaFact]
    public void SettingTheAttributeItAlreadyHasErasesNothing()
    {
        // The half that makes the feature useful: a program can rewrite the text on a double-width
        // line without losing the picture behind it.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight);
        int before = LitOnRow(emulator, 4);
        Assert.True(before > 0);

        // Same attribute again.
        SendEscape(emulator, "#6");

        Assert.Equal(before, LitOnRow(emulator, 4));
    }

    [AvaloniaFact]
    public void OnlyTheRowThatChangedIsCleared()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight * 3);   // rows 4, 5 and 6

        Assert.True(LitOnRow(emulator, 4) > 0);
        Assert.True(LitOnRow(emulator, 5) > 0);
        Assert.True(LitOnRow(emulator, 6) > 0);

        SendEscape(emulator, "[6;1H");
        SendEscape(emulator, "#6");

        Assert.True(LitOnRow(emulator, 4) > 0, "row 4 lost its graphics and its attribute never changed");
        Assert.Equal(0, LitOnRow(emulator, 5));
        Assert.True(LitOnRow(emulator, 6) > 0, "row 6 lost its graphics and its attribute never changed");
    }

    [AvaloniaFact]
    public void GoingBackToSingleWidthAlsoClears()
    {
        // It is the CHANGE that clears, not double-width in particular. DECSWL on a line that is
        // currently double is just as much a change as DECDWL on one that is single.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight);
        Assert.True(LitOnRow(emulator, 4) > 0);

        SendEscape(emulator, "#5");   // DECSWL

        Assert.Equal(0, LitOnRow(emulator, 4));
    }

    /// <summary>
    /// Sends a small Sixel image.
    /// </summary>
    /// <param name="emulator">
    /// Terminal to send to.
    /// </param>
    private static void SendSixel(TerminalEmulatorBase emulator)
    {
        // DCS q, one band of six pixels twenty wide in colour 1, then ST. Built from bytes for the
        // usual reason - the escape cannot be written as a string literal safely.
        var body = new StringBuilder();
        body.Append("Pq#1;2;100;0;0#1");
        for (int i = 0; i < 20; i++) body.Append('~');

        byte[] bytes = new byte[body.Length + 1];
        bytes[0] = Esc;
        for (int i = 0; i < body.Length; i++) bytes[i + 1] = (byte)body[i];

        emulator.ProcessData(bytes);
        emulator.ProcessData(new byte[] { Esc, (byte)'\\' });
    }

    [AvaloniaFact]
    public void ASixelImagePutsEveryLineBackToSingleWidth()
    {
        // Quirk 2 exists only to show this: "The VT340 resets all line attributes to single-width
        // when a sixel image is received."
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        SendEscape(emulator, "[3;1H");
        SendEscape(emulator, "#6");
        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        Assert.True(emulator.Buffer[2, 0].DoubleWidth);
        Assert.True(emulator.Buffer[4, 0].DoubleWidth);

        SendEscape(emulator, "[10;1H");
        SendSixel(emulator);

        Assert.False(emulator.Buffer[2, 0].DoubleWidth);
        Assert.False(emulator.Buffer[4, 0].DoubleWidth);
    }

    [AvaloniaFact]
    public void AnXtermIsNotSubjectToTheVt340sQuirk()
    {
        // Claimed by the model it was observed on and by nothing else. xterm and libsixel draw Sixel
        // and do not do this, so a profile that does not claim it must be left alone.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("xterm", 80, 24, 100);

        SendEscape(emulator, "[3;1H");
        SendEscape(emulator, "#6");
        Assert.True(emulator.Buffer[2, 0].DoubleWidth);

        SendEscape(emulator, "[10;1H");
        SendSixel(emulator);

        Assert.True(emulator.Buffer[2, 0].DoubleWidth);
    }

    [AvaloniaFact]
    public void TheTextUnderASixelImageIsLeftAlone()
    {
        // The captures say a VT340 clears the text buffer too. DELIBERATELY NOT DONE: on the
        // hardware those characters stay on screen as pixels in the shared bitmap - "retained only
        // in the bitmap buffer and cannot be edited" - so clearing our cell buffer would make them
        // vanish, which is further from the photograph than leaving them. Same split as M6.4h.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        SendEscape(emulator, "[3;1HLOREM");

        SendEscape(emulator, "[3;1H");
        SendSixel(emulator);

        Assert.Equal((uint)'L', emulator.Buffer[2, 0].Codepoint);
        Assert.Equal((uint)'M', emulator.Buffer[2, 4].Codepoint);
    }

    [AvaloniaFact]
    public void TheTextOnTheRowIsNotTouched()
    {
        // Explicit in the fixture: unlike Sixel, ReGIS does not clear the text buffer, and that is
        // what lets wide text and graphics coexist.
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT340", 80, 24, 100);

        SendEscape(emulator, "[5;1HHELLO");

        int cellHeight = emulator.GraphicsCellHeight;
        DrawBand(emulator, 4 * cellHeight, cellHeight);

        SendEscape(emulator, "[5;1H");
        SendEscape(emulator, "#6");

        Assert.Equal(0, LitOnRow(emulator, 4));
        Assert.Equal((uint)'H', emulator.Buffer[4, 0].Codepoint);
        Assert.Equal((uint)'O', emulator.Buffer[4, 4].Codepoint);
        Assert.True(emulator.Buffer[4, 0].DoubleWidth);
    }
}
