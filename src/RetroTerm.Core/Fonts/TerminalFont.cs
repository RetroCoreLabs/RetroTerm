using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


// Reference:
//		https://www.masswerk.at/nowgobang/2019/dec-crt-typography
//		https://vt100.net/dec/vt220/glyphs
//		https://vt100.net/dec/vt320/glyphs


namespace RetroTerm.Core.Fonts
{
    public enum FontStyle
    {
        VT100,
        VT220,
        VT320,
        TandbergVDU301,
        TandbergTDV2200
    }

    /// <summary>
    /// Raster font to be used by ConsoleDisplay
    /// </summary>
    public class TerminalFont
    {
        readonly FontBase theFont;

        public TerminalFont(FontStyle style)
        {
            switch (style)
            {
                case FontStyle.TandbergTDV2200:
                    theFont = new FontTDV2200();
                    break;

                default:
                    // TODO: Implement other font styles.
                    // Until then, fail loudly here rather than leaving theFont null and
                    // throwing an unexplained NullReferenceException on the next line.
                    throw new NotSupportedException(
                        $"Font style {style} is not implemented yet; only {nameof(FontStyle.TandbergTDV2200)} is available.");
            }

            FontHeight = theFont.Height;
            if (theFont.HeightToUse > 0) FontHeight = theFont.HeightToUse;
            FontWidth = theFont.Width;
            FontStretchY = theFont.stretchY;

            // Recalc Height
            FontHeight = FontHeight * FontStretchY;
        }


        public int FontHeight { get; set; }
        public int FontWidth { get; set; }

        // Stretch font in the Y directin how many times ?
        public int FontStretchY { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fontValue">
        /// 
        /// </param>
        /// <param name="fontNum">
        /// If fontNum != 0, use alternative font
        /// </param>
        /// <returns>
        /// 
        /// </returns>
        public ushort[]? GetFontBits(ushort fontValue, int fontNum)
        {
            if (theFont == null) return null;
            return theFont.GetFontBits(fontValue, fontNum);
        }
    }
}
