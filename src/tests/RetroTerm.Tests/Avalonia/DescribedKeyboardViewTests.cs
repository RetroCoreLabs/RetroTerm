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

namespace RetroTerm.Tests.Avalonia
{
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

        /// <summary>
        /// Every legend on every key of every layout drawn by the shared control is rendered on its
        /// own and its INK (the pixels it actually paints, not the box the text layout reports) is
        /// measured: the ink must stay inside the key's edge, and no two legends of one key may
        /// touch or overlap. Found by eye first on the 3278: ALT CURSR ran into CURSR BLINK and
        /// ERASE INPUT ran off the bottom of its key.
        /// </summary>
        [AvaloniaTheory]
        [InlineData("IBM3278")]
        [InlineData("KB3270PLUS")]
        public void EveryLegendsInkFitsItsKeyAndTouchesNoOtherLegend(string id)
        {
            var description = id == "IBM3278" ? Ibm3270Keyboards.Ibm3278 : Ibm3270Keyboards.Kb3270Plus;
            var faults = new List<string>();

            for (int i = 0; i < description.Keys.Count; i++)
            {
                var cap = description.Keys[i];
                var key = DescribedKeyboardView.KeyRect(cap);
                // The edge pen is 1 px wide on the key's outline; ink must stay inside it.
                var inside = key.Deflate(1);
                var labels = DescribedKeyboardView.LayoutLabels(cap);
                var inks = new PixelRect?[labels.Count];

                for (int j = 0; j < labels.Count; j++)
                {
                    inks[j] = InkBounds(labels[j], key);
                    if (inks[j] is not PixelRect ink)
                    {
                        // Every legend in the list has text, so it must paint something. Without
                        // this a layout that drew NOTHING passed: a first fix laid every top and
                        // front legend out empty and this test stayed green until the PNG was read.
                        faults.Add(Name(cap) + ": " + labels[j].Which + " legend paints no ink");
                        continue;
                    }

                    if (ink.X < inside.X || ink.Y < inside.Y || ink.Right > inside.Right || ink.Bottom > inside.Bottom)
                    {
                        faults.Add(Name(cap) + ": " + labels[j].Which + " legend ink " + ink + " leaves the key " + inside);
                    }
                }

                for (int a = 0; a < labels.Count; a++)
                {
                    for (int b = a + 1; b < labels.Count; b++)
                    {
                        if (inks[a] is PixelRect ia && inks[b] is PixelRect ib && ia.Intersects(ib))
                        {
                            faults.Add(Name(cap) + ": " + labels[a].Which + " legend ink " + ia + " runs into " + labels[b].Which + " legend ink " + ib);
                        }
                    }
                }
            }

            Assert.True(faults.Count == 0, string.Join("\n", faults));
        }

        private static string Name(KeyCap cap)
        {
            return "'" + cap.Legend + "'" + (cap.TopLegend != null ? " top '" + cap.TopLegend + "'" : string.Empty)
                + (cap.FrontLegend != null ? " front '" + cap.FrontLegend + "'" : string.Empty);
        }

        // Renders one legend alone on a transparent bitmap that covers its key plus a wide margin
        // (so ink that overflows is still seen), and returns the box of every painted pixel in the
        // control's coordinates, or null when it paints nothing.
        private static PixelRect? InkBounds(DescribedKeyboardView.KeyLabel label, Rect key)
        {
            const int Margin = 40;
            int left = (int)key.X - Margin;
            int top = (int)key.Y - Margin;
            int width = (int)key.Width + 2 * Margin;
            int height = (int)key.Height + 2 * Margin;

            byte[] png;
            using (var target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                using (var context = target.CreateDrawingContext())
                {
                    context.DrawText(label.Text, new Point(label.Origin.X - left, label.Origin.Y - top));
                }

                using var stream = new MemoryStream();
                target.Save(stream, global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                png = stream.ToArray();
            }

            using var bitmap = SKBitmap.Decode(png);
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).Alpha > 0)
                    {
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
            }

            if (maxX < 0)
            {
                return null;
            }

            // Right and Bottom of a PixelRect are one past the last pixel.
            return new PixelRect(minX + left, minY + top, maxX - minX + 1, maxY - minY + 1);
        }

        [AvaloniaFact]
        public void AClickOnAKeyPressesItAndShiftLatchesForOneKey()
        {
            var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Ibm3278 };
            var presses = new List<string?>();
            view.KeyPressed += (cap, keyName, text) => presses.Add(text);

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
            Assert.Equal("a", presses[0]);
            Assert.Equal("A", presses[1]);
            Assert.Equal("a", presses[2]);
            Assert.False(view.ShiftLatched);
        }

        [AvaloniaFact]
        public void AltThenTheOneKeyPressesPf1()
        {
            var view = new DescribedKeyboardView { Description = Ibm3270Keyboards.Ibm3278 };
            string? pressed = null;
            view.KeyPressed += (cap, keyName, text) => pressed = keyName;

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
            view.KeyPressed += (cap, keyName, text) => count++;

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
}
