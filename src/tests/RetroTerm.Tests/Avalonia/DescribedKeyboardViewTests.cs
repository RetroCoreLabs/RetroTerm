//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.IO;

using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;

using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Desktop.Controls;
using RetroTerm.Ibm3270;

using SkiaSharp;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The shared on-screen keyboard control: it draws a description and turns clicks into presses.
/// Writes keyboard-IBM3278.png and keyboard-KB3270PLUS.png to the rendered images folder.
/// </summary>
[Collection("Avalonia")]
public class DescribedKeyboardViewTests
{
    [AvaloniaTheory]
    [InlineData("IBM3278")]
    [InlineData("KB3270PLUS")]
    public void EveryKeyIsDrawnInsideTheControl(string id)
    {
        var description = id == "IBM3278" ? Ibm3270Keyboards.Ibm3278 : Ibm3270Keyboards.Kb3270Plus;
        var view = new DescribedKeyboardView { Description = description };
        var bitmap = Render(view, "keyboard-" + id);

        // Every key's centre has key colour, not the empty background: a key drawn outside the
        // measured size, or not at all, would leave its centre transparent.
        for (int i = 0; i < description.Keys.Count; i++)
        {
            var cap = description.Keys[i];
            int x = (int)((cap.X + cap.Width / 2) * DescribedKeyboardView.UnitSize);
            int y = (int)((cap.Y + cap.Height / 2) * DescribedKeyboardView.UnitSize);
            Assert.True(bitmap.GetPixel(x, y).Alpha > 0, "key '" + cap.Legend + "' has no pixels at its centre");
        }
    }

    [AvaloniaFact]
    public void AClickOnAKeyPressesItAndShiftLatchesForOneKey()
    {
        var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Ibm3278 };
        var presses = new List<DescribedKeyPressedEventArgs>();
        view.KeyPressed += (_, e) => presses.Add(e);

        var a = Find(view, "A");
        var shift = Find(view, "SHIFT");
        var size = new Size(1400, 400);
        view.Measure(size);
        view.Arrange(new Rect(view.DesiredSize));

        // Hit testing finds the cap at its own centre.
        double cx = (a.X + 0.5) * DescribedKeyboardView.UnitSize;
        double cy = (a.Y + 0.5) * DescribedKeyboardView.UnitSize;
        Assert.Same(a, view.HitTest(new Point(cx, cy)));

        view.Press(a);
        view.Press(shift);
        Assert.True(view.ShiftLatched);
        view.Press(a);
        view.Press(a);

        Assert.Equal(3, presses.Count);
        Assert.Equal("a", presses[0].Text);
        Assert.Equal("A", presses[1].Text);
        Assert.Equal("a", presses[2].Text);
        Assert.False(view.ShiftLatched);
    }

    [AvaloniaFact]
    public void AltThenTheOneKeyPressesPf1()
    {
        var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Ibm3278 };
        string? pressed = null;
        view.KeyPressed += (_, e) => pressed = e.KeyName;

        view.Press(Find(view, "ALT"));
        view.Press(Find(view, "1"));

        Assert.Equal("PF1", pressed);
        Assert.False(view.AltLatched);
    }

    [AvaloniaFact]
    public void ABlankKeyDoesNothing()
    {
        var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Kb3270Plus };
        int count = 0;
        view.KeyPressed += (_, _) => count++;

        view.Press(Find(view, "Help"));

        Assert.Equal(0, count);
    }

    private static KeyCap Find(DescribedKeyboardView view, string legend)
    {
        var keys = view.Description!.Keys;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Legend == legend)
            {
                return keys[i];
            }
        }

        throw new Xunit.Sdk.XunitException("no key '" + legend + "'");
    }

    private static SKBitmap Render(DescribedKeyboardView view, string saveAs)
    {
        view.Measure(new Size(4000, 4000));
        var size = view.DesiredSize;
        view.Arrange(new Rect(size));

        using var target = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96));
        target.Render(view);

        byte[] png;
        using (var stream = new MemoryStream())
        {
            target.Save(stream, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            png = stream.ToArray();
        }

        var folder = RenderedScreenshot.ImagesFolder;
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, saveAs + ".png"), png);
        return SKBitmap.Decode(png);
    }
}
