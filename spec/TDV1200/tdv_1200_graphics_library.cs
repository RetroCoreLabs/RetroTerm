// TDV1200GraphicsLib.cs
// High-level C# graphics library for Tandberg TDV 1200 terminals.
// Implements GPGS-like primitives (lines, circles, polygons, text) using Norsk Data raster commands.
// Designed for NuGet packaging with full XML documentation.

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;

namespace RetroCore.Tdv1200
{
    /// <summary>
    /// Provides a high-level graphics API for the Tandberg TDV 1200 terminal.
    /// Wraps Norsk Data's ISO 6429 raster protocol, translating lines, polygons, circles,
    /// and fills into rectangle-based operations. Suitable for integration as a NuGet library.
    /// </summary>
    public class Tdv1200Device : IDisposable
    {
        private readonly SerialPort _port;

        /// <summary>
        /// Initializes the TDV 1200 graphics device interface.
        /// </summary>
        /// <param name="portName">Serial port name (e.g., COM3)</param>
        /// <param name="baud">Baud rate, default 9600</param>
        public Tdv1200Device(string portName, int baud = 9600)
        {
            _port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
            _port.Open();
        }

        private void Send(string s)
        {
            _port.Write(s);
            Thread.Sleep(1); // pacing for terminal
        }

        private static string CSI(string cmd, params int[] args)
            => $"\x1B[{string.Join(';', args)}{cmd}";

        #region Low-level Primitives

        /// <summary>
        /// Switches between text and graphics video planes.
        /// </summary>
        public void SetVideo(bool graphicsOn) => Send(CSI("<7F", graphicsOn ? 1 : 0));

        /// <summary>
        /// Defines an active rectangular work area for subsequent graphics operations.
        /// </summary>
        public void DefineWorkArea(int x1, int y1, int x2, int y2) => Send(CSI("~", x1, y1, x2, y2));

        /// <summary>
        /// Fills a rectangular region with the specified attribute.
        /// </summary>
        public void FillRect(int x1, int y1, int x2, int y2, int attr = 1) => Send(CSI("z", attr, x1, y1, x2, y2));

        /// <summary>
        /// Copies a rectangular region from one area to another.
        /// </summary>
        public void CopyRect(int sx1, int sy1, int sx2, int sy2, int dx, int dy)
        {
            Send(CSI("u", sx1, sy1, sx2, sy2)); // Save rectangle
            Send(CSI("v", dx, dy));             // Restore rectangle
        }

        /// <summary>
        /// Draws a single pixel by filling a 1×1 rectangle.
        /// </summary>
        public void DrawPixel(int x, int y, int attr = 1) => FillRect(x, y, x + 1, y + 1, attr);

        #endregion

        #region Derived Primitives

        /// <summary>
        /// Draws a line between two points using Bresenham's algorithm.
        /// </summary>
        public void DrawLine(int x1, int y1, int x2, int y2, int attr = 1)
        {
            int dx = Math.Abs(x2 - x1), sx = x1 < x2 ? 1 : -1;
            int dy = -Math.Abs(y2 - y1), sy = y1 < y2 ? 1 : -1;
            int err = dx + dy, e2;

            while (true)
            {
                DrawPixel(x1, y1, attr);
                if (x1 == x2 && y1 == y2) break;
                e2 = 2 * err;
                if (e2 >= dy) { err += dy; x1 += sx; }
                if (e2 <= dx) { err += dx; y1 += sy; }
            }
        }

        /// <summary>
        /// Draws a circle using midpoint circle algorithm.
        /// </summary>
        public void DrawCircle(int cx, int cy, int radius, int attr = 1)
        {
            int x = radius, y = 0, err = 0;
            while (x >= y)
            {
                DrawPixel(cx + x, cy + y, attr);
                DrawPixel(cx + y, cy + x, attr);
                DrawPixel(cx - y, cy + x, attr);
                DrawPixel(cx - x, cy + y, attr);
                DrawPixel(cx - x, cy - y, attr);
                DrawPixel(cx - y, cy - x, attr);
                DrawPixel(cx + y, cy - x, attr);
                DrawPixel(cx + x, cy - y, attr);
                if (err <= 0) { y++; err += 2 * y + 1; }
                if (err > 0) { x--; err -= 2 * x + 1; }
            }
        }

        /// <summary>
        /// Fills a polygon using scanline fill algorithm.
        /// </summary>
        public void FillPolygon(List<(int X, int Y)> vertices, int attr = 1)
        {
            if (vertices.Count < 3) return;
            int minY = int.MaxValue, maxY = int.MinValue;
            foreach (var v in vertices) { minY = Math.Min(minY, v.Y); maxY = Math.Max(maxY, v.Y); }

            for (int y = minY; y <= maxY; y++)
            {
                var nodes = new List<int>();
                for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
                {
                    var vi = vertices[i]; var vj = vertices[j];
                    if ((vi.Y < y && vj.Y >= y) || (vj.Y < y && vi.Y >= y))
                    {
                        int x = vi.X + (y - vi.Y) * (vj.X - vi.X) / (vj.Y - vi.Y);
                        nodes.Add(x);
                    }
                }
                nodes.Sort();
                for (int i = 0; i < nodes.Count; i += 2)
                {
                    if (i + 1 >= nodes.Count) break;
                    FillRect(nodes[i], y, nodes[i + 1], y + 1, attr);
                }
            }
        }

        /// <summary>
        /// Draws a hatched or patterned rectangle fill.
        /// </summary>
        public void HatchRect(int x1, int y1, int x2, int y2, int patternType = 1)
        {
            for (int y = y1; y < y2; y++)
            {
                if ((patternType & 1) != 0 && (y % 2 == 0)) FillRect(x1, y, x2, y + 1, 1);
                if ((patternType & 2) != 0 && (y % 4 == 0)) FillRect(x1, y, x2, y + 1, 1);
            }
        }

        /// <summary>
        /// Draws rasterized text using a placeholder pixel pattern.
        /// </summary>
        public void DrawText(int x, int y, string text)
        {
            int offsetX = x;
            foreach (char c in text)
            {
                for (int cy = 0; cy < 8; cy++)
                    for (int cx = 0; cx < 6; cx++)
                        if (((c + cx + cy) % 3) == 0)
                            DrawPixel(offsetX + cx, y + cy);
                offsetX += 7;
            }
        }

        #endregion

        /// <summary>
        /// Releases the serial port and device resources.
        /// </summary>
        public void Dispose() => _port?.Dispose();
    }

    /// <summary>
    /// Example usage demonstrating all high-level primitives.
    /// </summary>
    public static class Demo
    {
        public static void Main()
        {
            using var tdv = new Tdv1200Device("COM3", 19200);

            tdv.SetVideo(true);
            tdv.DefineWorkArea(0, 0, 1023, 1023);

            tdv.FillRect(50, 50, 150, 80); // Rectangle
            tdv.DrawLine(20, 20, 300, 200); // Line
            tdv.DrawCircle(200, 200, 40);   // Circle

            var polygon = new List<(int X, int Y)> { (100, 300), (150, 350), (120, 400), (80, 370) };
            tdv.FillPolygon(polygon);

            tdv.HatchRect(300, 50, 380, 120, 3);
            tdv.DrawText(20, 450, "TDV 1200 TEST");

            tdv.CopyRect(50, 50, 150, 80, 400, 400);
            tdv.SetVideo(false);
        }
    }
}
