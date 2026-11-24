//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Avalonia.Headless.XUnit;
using Avalonia.Media;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Desktop.Rendering;
using RetroTerm.Ibm3270;

using SkiaSharp;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// What the real 3270 renderer draws for the code page 310 bytes (the APL and box set,
    /// reached by Graphic Escape or by character set X'F1'): every byte drawn alone into the same
    /// cell and its pixels kept, and the whole set drawn as one table into
    /// <c>images\rendered\ibm3270-cp310-table.png</c> to look at.
    /// </summary>
    /// <remarks>
    /// The 3270 draws through <see cref="SystemFontRenderer"/>, whose first font is Consolas, not
    /// Lucida Console; a character Consolas lacks is drawn by whatever font Avalonia's font manager
    /// matches for it. The output lists, for each byte, whether Consolas has it and which font
    /// drew it otherwise. docs\spec\IBM3270\EBCDIC.md, "What RetroTerm draws", holds the result.
    /// </remarks>
    [Collection("Avalonia")]
    public class Ibm3270CodePage310GlyphTests
    {
        private readonly ITestOutputHelper _output;

        public Ibm3270CodePage310GlyphTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [AvaloniaFact]
        public void EveryCodePage310CharacterIsDrawnWithItsOwnGlyph()
        {
            // A font with no glyph for a character draws its missing-glyph box, and that box is the
            // same for every missing character. So: ink in every cell, and no two different
            // characters with the same pixels. Each is drawn into the same cell (row 0, column 1),
            // so the same glyph gives the very same pixels.
            var signatures = new Dictionary<string, int>();
            var report = new StringBuilder();
            Typeface first = new Typeface(SystemFontRenderer.DefaultFontFamily);
            GlyphTypeface consolas = first.GlyphTypeface;
            _output.WriteLine("first font: " + consolas.FamilyName);
            for (int b = 0x41; b <= 0xFE; b++)
            {
                char c = CodePage.Cp310.ToUnicode((byte)b);
                if (c == '\0')
                {
                    continue;
                }

                using RenderedScreenshot shot = RenderedScreenshot.Capture(Screen(b));
                int ink = shot.InkPixelsInCell(0, 1);
                string signature = Signature(shot, 0, 1);
                string hex = b.ToString("X2", CultureInfo.InvariantCulture);
                bool inFirst = consolas.CharacterToGlyphMap.ContainsGlyph(c);
                string drawnBy = consolas.FamilyName;
                if (!inFirst && FontManager.Current.TryMatchCharacter(c, FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, null, CultureInfo.InvariantCulture, out Typeface match))
                {
                    drawnBy = match.GlyphTypeface.FamilyName;
                }

                _output.WriteLine(hex + " U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) + " " + c + "  in " + consolas.FamilyName + ": " + (inFirst ? "yes" : "no") + ", drawn by " + drawnBy + ", ink " + ink);
                if (ink == 0)
                {
                    report.Append("X'").Append(hex).Append("' ").Append(c).Append(" draws nothing\n");
                }

                if (signatures.TryGetValue(signature, out int earlier))
                {
                    report.Append("X'").Append(hex).Append("' ").Append(c).Append(" draws the same pixels as X'")
                        .Append(earlier.ToString("X2", CultureInfo.InvariantCulture)).Append("' (a missing-glyph box?)\n");
                }
                else
                {
                    signatures[signature] = b;
                }
            }

            using RenderedScreenshot table = RenderedScreenshot.Capture(Table(), "ibm3270-cp310-table");
            _output.WriteLine("table: " + table.SavedPath);
            Assert.True(report.Length == 0, report.ToString());
        }

        [AvaloniaFact]
        public void AnUnderscoredCodePage310LetterIsDrawnWithTheLineUnderIt()
        {
            // X'41' after GE is "A Line Below Capital/A Underscore (APL)" (CP00310.txt); X'C1' is
            // the plain A of code page 37. Same cell, so the only difference is the line.
            using RenderedScreenshot underscored = RenderedScreenshot.Capture(Screen(0x41), "ibm3270-cp310-underscored-a");
            var plain = new Ibm3270Emulator();
            plain.ProcessData(new byte[] { Command3270.EraseWrite, 0x02, Order3270.SetBufferAddress, 0x40, 0xC1, 0xC1 });
            using RenderedScreenshot letter = RenderedScreenshot.Capture(plain);

            // The line: a pixel row of the cell where the underscored A lights at least half the
            // width more than the plain A does (the line may share a row with the feet of the A,
            // so the plain A's own pixels on that row are allowed). Only the line can do that.
            int x0 = (int)underscored.CellWidth;
            int x1 = (int)(2 * underscored.CellWidth);
            int height = (int)underscored.CellHeight;
            SKColor background = underscored.PixelAt(0, (int)(5.5 * underscored.CellHeight));
            int lineRow = -1;
            for (int y = 0; y < height && lineRow < 0; y++)
            {
                int lit = 0;
                int litPlain = 0;
                for (int x = x0; x < x1; x++)
                {
                    if (!RenderedScreenshot.ApproximatelyEqual(underscored.PixelAt(x, y), background, 24))
                    {
                        lit++;
                    }

                    if (!RenderedScreenshot.ApproximatelyEqual(letter.PixelAt(x, y), background, 24))
                    {
                        litPlain++;
                    }
                }

                if (lit - litPlain >= (x1 - x0) / 2)
                {
                    lineRow = y;
                }
            }

            Assert.True(lineRow >= 0, "no line under the underscored A");
        }

        [AvaloniaFact]
        public void DoubleVerticalBarGraphicIsALineOnTheLeftAndRightCellEdges()
        {
            // X'81' "Double Vertical, Bar Graphic" (CP00310.txt): GA27-2837-9 fig. 10-41 draws a
            // line down each side edge of the cell and nothing between. docs\BUGS.md B24.
            using RenderedScreenshot shot = RenderedScreenshot.Capture(Screen(0x81), "ibm3270-cp310-double-vertical-bar");
            int x0 = (int)Math.Round(shot.CellWidth);
            int x1 = (int)Math.Round(2 * shot.CellWidth) - 1;
            int height = (int)shot.CellHeight;
            SKColor background = shot.PixelAt(0, (int)(5.5 * shot.CellHeight));
            var faults = new StringBuilder();
            for (int y = 1; y < height - 1; y++)
            {
                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x0, y), background, 24))
                {
                    faults.Append("left edge dark at y ").Append(y).Append('\n');
                }

                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x1, y), background, 24))
                {
                    faults.Append("right edge dark at y ").Append(y).Append('\n');
                }

                for (int x = x0 + 2; x <= x1 - 2; x++)
                {
                    if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 24))
                    {
                        faults.Append("lit inside the cell at ").Append(x).Append(',').Append(y).Append('\n');
                    }
                }
            }

            Assert.True(faults.Length == 0, shot.SavedPath + "\n" + faults);
        }

        [AvaloniaFact]
        public void DoubleHorizontalBarGraphicIsALineOnTheTopAndBottomCellEdges()
        {
            // X'82' "Double Horizontal, Bar Graphic" (CP00310.txt): GA27-2837-9 fig. 10-41 draws a
            // line along the top and the bottom edge of the cell and nothing between.
            using RenderedScreenshot shot = RenderedScreenshot.Capture(Screen(0x82), "ibm3270-cp310-double-horizontal-bar");
            int x0 = (int)Math.Round(shot.CellWidth);
            int x1 = (int)Math.Round(2 * shot.CellWidth) - 1;
            int y0 = 0;
            int y1 = (int)Math.Round(shot.CellHeight) - 1;
            SKColor background = shot.PixelAt(0, (int)(5.5 * shot.CellHeight));
            var faults = new StringBuilder();
            for (int x = x0 + 1; x < x1; x++)
            {
                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y0), background, 24))
                {
                    faults.Append("top edge dark at x ").Append(x).Append('\n');
                }

                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y1), background, 24))
                {
                    faults.Append("bottom edge dark at x ").Append(x).Append('\n');
                }

                for (int y = y0 + 2; y <= y1 - 2; y++)
                {
                    if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, y), background, 24))
                    {
                        faults.Append("lit inside the cell at ").Append(x).Append(',').Append(y).Append('\n');
                    }
                }
            }

            Assert.True(faults.Length == 0, shot.SavedPath + "\n" + faults);
        }

        [AvaloniaFact]
        public void ABoxOfCodePage310PiecesJoinsWithoutGaps()
        {
            // The box TK5's RPF main menu draws (Graphic Escape X'C5' X'A2' X'D5' X'85' X'C4'
            // X'D4', measured 24 November 2026), five wide and three high, at rows 1-3 columns
            // 1-5. Through the middle of the top line and of the left side, every pixel must be
            // lit: a gap between two cells would show as a broken line.
            var emulator = new Ibm3270Emulator();
            emulator.ProcessData(new byte[]
            {
                Command3270.EraseWrite, 0x02,
                Order3270.SetBufferAddress, 0xC1, 0xD1, // row 1, column 1 (81)
                Order3270.GraphicEscape, 0xC5, Order3270.GraphicEscape, 0xA2, Order3270.GraphicEscape, 0xA2,
                Order3270.GraphicEscape, 0xA2, Order3270.GraphicEscape, 0xD5,
                Order3270.SetBufferAddress, 0xC2, 0x61, // row 2, column 1 (161)
                Order3270.GraphicEscape, 0x85,
                Order3270.SetBufferAddress, 0xC2, 0xE5, // row 2, column 5 (165)
                Order3270.GraphicEscape, 0x85,
                Order3270.SetBufferAddress, 0xC3, 0xF1, // row 3, column 1 (241)
                Order3270.GraphicEscape, 0xC4, Order3270.GraphicEscape, 0xA2, Order3270.GraphicEscape, 0xA2,
                Order3270.GraphicEscape, 0xA2, Order3270.GraphicEscape, 0xD4,
            });
            using RenderedScreenshot shot = RenderedScreenshot.Capture(emulator, "ibm3270-cp310-box");
            SKColor background = shot.PixelAt((int)(40.5 * shot.CellWidth), (int)(10.5 * shot.CellHeight));

            // The top line: find its pixel row in the middle cell, then walk it from the middle
            // of the corner cell to the middle of the other corner cell.
            int midX = (int)(3.5 * shot.CellWidth);
            int lineY = -1;
            for (int y = (int)shot.CellHeight; y < (int)(2 * shot.CellHeight) && lineY < 0; y++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(midX, y), background, 24))
                {
                    lineY = y;
                }
            }

            Assert.True(lineY >= 0, "no top line in the middle cell");
            var gaps = new StringBuilder();
            for (int x = (int)(1.5 * shot.CellWidth) + 1; x < (int)(5.5 * shot.CellWidth) - 1; x++)
            {
                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, lineY), background, 24))
                {
                    gaps.Append("top line dark at x ").Append(x).Append('\n');
                }
            }

            // The left side: its pixel column in row 2, walked from the middle of row 1 to the
            // middle of row 3.
            int midY = (int)(2.5 * shot.CellHeight);
            int sideX = -1;
            for (int x = (int)shot.CellWidth; x < (int)(2 * shot.CellWidth) && sideX < 0; x++)
            {
                if (!RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(x, midY), background, 24))
                {
                    sideX = x;
                }
            }

            Assert.True(sideX >= 0, "no left side in row 2");
            // One pixel short of each end: the corners' arms meet there, on a fractional pixel.
            for (int y = (int)(1.5 * shot.CellHeight) + 1; y < (int)(3.5 * shot.CellHeight) - 1; y++)
            {
                if (RenderedScreenshot.ApproximatelyEqual(shot.PixelAt(sideX, y), background, 24))
                {
                    gaps.Append("left side dark at y ").Append(y).Append('\n');
                }
            }

            Assert.True(gaps.Length == 0, "the box has gaps (" + shot.SavedPath + "):\n" + gaps);
        }

        // A model 2 screen with one code page 310 byte, behind a Graphic Escape, at row 0 column 1.
        private static Ibm3270Emulator Screen(int b)
        {
            var emulator = new Ibm3270Emulator();
            emulator.ProcessData(new byte[]
            {
                Command3270.EraseWrite, 0x02,
                Order3270.SetBufferAddress, 0x40, 0xC1,
                Order3270.GraphicEscape, (byte)b,
            });
            return emulator;
        }

        // Every byte X'40'-X'FF' behind a Graphic Escape, laid out as the code page chart: one
        // row pair per high digit, every fourth column per low digit.
        private static Ibm3270Emulator Table()
        {
            var table = new List<byte> { Command3270.EraseWrite, 0x02 };
            Span<byte> two = stackalloc byte[2];
            for (int b = 0x40; b <= 0xFF; b++)
            {
                int address = (((b >> 4) - 4) * 2 * 80) + 4 + ((b & 0x0F) * 4);
                BufferAddress.Encode(address, 1920, two);
                table.Add(Order3270.SetBufferAddress);
                table.Add(two[0]);
                table.Add(two[1]);
                table.Add(Order3270.GraphicEscape);
                table.Add((byte)b);
            }

            var emulator = new Ibm3270Emulator();
            emulator.ProcessData(table.ToArray());
            return emulator;
        }

        // The cell's pixels as text, red and green of each: equal text, equal picture.
        private static string Signature(RenderedScreenshot shot, int row, int col)
        {
            var text = new StringBuilder();
            int x0 = (int)(col * shot.CellWidth);
            int x1 = (int)((col + 1) * shot.CellWidth);
            int y0 = (int)(row * shot.CellHeight);
            int y1 = (int)((row + 1) * shot.CellHeight);
            for (int y = y0; y < y1; y++)
            {
                for (int x = x0; x < x1; x++)
                {
                    SKColor p = shot.PixelAt(x, y);
                    text.Append(p.Red.ToString("X2", CultureInfo.InvariantCulture)).Append(p.Green.ToString("X2", CultureInfo.InvariantCulture));
                }
            }

            return text.ToString();
        }
    }
}
