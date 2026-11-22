//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

using RetroTerm.Core.Terminal.Input.Keyboards;

namespace RetroTerm.Desktop.Controls
{
    /// <summary>
    /// Draws any <see cref="KeyboardDescription"/> and turns clicks into key presses. One control
    /// for every terminal's keyboard (Ronny, 22 November 2026: one window, one shared model).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Shift and Alt latch for the next key, as on the TDV keyboard; Lock stays on until pressed
    /// again. The latches live here, not in the terminal, because they belong to the on-screen
    /// keyboard: the physical keyboard has its own Shift.
    /// </para>
    /// <para>
    /// Key colours are the hardware's, so they are fixed and NOT themed (the same rule the
    /// RetroCore UI keeps for LEDs and printed key legends).
    /// </para>
    /// </remarks>
    public sealed class DescribedKeyboardView : Control
    {
        private KeyboardDescription? _description;
        private KeyCap? _pressed;

        /// <summary>
        /// Pixels per key unit.
        /// </summary>
        public const double UnitSize = 46;

        private const double Gap = 3;

        private static readonly IBrush CharacterBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0xD4, 0xC7));
        private static readonly IBrush AttentionBrush = new SolidColorBrush(Color.FromRgb(0x7E, 0x9C, 0xC0));
        private static readonly IBrush EditBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xA6, 0x9E));
        private static readonly IBrush ModifierBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x81, 0x7A));
        private static readonly IBrush BlankBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x5C, 0x58));
        private static readonly IBrush LatchedBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xB8, 0x4A));
        private static readonly IBrush PressedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly IBrush LegendBrush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        private static readonly IBrush BlankLegendBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        private static readonly IBrush FrontLegendBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x3F, 0x8F));
        private static readonly IPen EdgePen = new Pen(new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x30)), 1);
        // The platform's default font. A named list ("Segoe UI, Arial") was not resolved in the
        // rendered test and a colour fallback font drew I, P and the arrows in blue and purple.
        private static readonly Typeface Face = new Typeface(FontFamily.Default);

        /// <summary>
        /// Raised for each key press that does something.
        /// </summary>
        public event OnScreenKeyPressed? KeyPressed;

        /// <summary>
        /// Creates the control.
        /// </summary>
        public DescribedKeyboardView()
        {
            // Grey-scale text smoothing: subpixel smoothing put blue and purple fringes on thin
            // legends (I, L, the arrows) in the rendered image.
            TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        }

        /// <summary>
        /// The keyboard drawn, or null for none.
        /// </summary>
        public KeyboardDescription? Description
        {
            get => _description;
            set
            {
                _description = value;
                ShiftLatched = false;
                AltLatched = false;
                LockOn = false;
                _pressed = null;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        /// <summary>
        /// Shift is latched for the next key.
        /// </summary>
        public bool ShiftLatched { get; private set; }

        /// <summary>
        /// Alt is latched for the next key.
        /// </summary>
        public bool AltLatched { get; private set; }

        /// <summary>
        /// Lock (shift until pressed again) is on.
        /// </summary>
        public bool LockOn { get; private set; }

        /// <summary>
        /// Presses a cap as a click would. For tests and for the pointer handler.
        /// </summary>
        /// <param name="cap">
        /// The cap.
        /// </param>
        public void Press(KeyCap cap)
        {
            switch (cap.Latch)
            {
                case KeyLatch.Shift:
                    ShiftLatched = !ShiftLatched;
                    InvalidateVisual();
                    return;
                case KeyLatch.Alt:
                    AltLatched = !AltLatched;
                    InvalidateVisual();
                    return;
                case KeyLatch.Lock:
                    LockOn = !LockOn;
                    InvalidateVisual();
                    return;
            }

            bool did = cap.ResolvePress(ShiftLatched || LockOn, AltLatched, out string? keyName, out string? text);

            // The latches are for ONE key, whether or not that key did anything.
            ShiftLatched = false;
            AltLatched = false;
            InvalidateVisual();

            if (did)
            {
                KeyPressed?.Invoke(cap, keyName, text);
            }
        }

        /// <summary>
        /// The cap under a point in this control's coordinates, or null.
        /// </summary>
        /// <param name="point">
        /// The point.
        /// </param>
        /// <returns>
        /// The cap, or null.
        /// </returns>
        public KeyCap? HitTest(Point point)
        {
            if (_description == null)
            {
                return null;
            }

            IReadOnlyList<KeyCap> keys = _description.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                if (CapRect(keys[i]).Contains(point))
                {
                    return keys[i];
                }
            }

            return null;
        }

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize)
        {
            if (_description == null)
            {
                return default;
            }

            return new Size(_description.Width * UnitSize + Gap, _description.Height * UnitSize + Gap);
        }

        /// <inheritdoc/>
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            KeyCap? cap = HitTest(e.GetPosition(this));
            if (cap != null)
            {
                _pressed = cap;
                Press(cap);
                e.Handled = true;
            }
        }

        /// <inheritdoc/>
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (_pressed != null)
            {
                _pressed = null;
                InvalidateVisual();
            }
        }

        /// <inheritdoc/>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (_description == null)
            {
                return;
            }

            IReadOnlyList<KeyCap> keys = _description.Keys;
            for (int i = 0; i < keys.Count; i++)
            {
                DrawCap(context, keys[i]);
            }
        }

        private static Rect CapRect(KeyCap cap)
        {
            return new Rect(
                cap.X * UnitSize + Gap,
                cap.Y * UnitSize + Gap,
                cap.Width * UnitSize - Gap,
                cap.Height * UnitSize - Gap);
        }

        private void DrawCap(DrawingContext context, KeyCap cap)
        {
            Rect rect = CapRect(cap);
            context.DrawRectangle(FillFor(cap), EdgePen, rect, 4, 4);

            IBrush ink = cap.Role == KeyRole.Blank ? BlankLegendBrush : LegendBrush;

            if (cap.TopLegend != null)
            {
                DrawText(context, cap.TopLegend, 11, ink, new Point(rect.X + 5, rect.Y + 2), rect.Width - 8);
                // With a front legend as well (ChgSc/Jump, Print/*), the main legend sits higher so
                // the two do not run into each other.
                double mainY = cap.FrontLegend != null ? rect.Y + rect.Height * 0.30 : rect.Y + rect.Height / 2 - 2;
                DrawText(context, cap.Legend, cap.FrontLegend != null ? 11 : 13, ink, new Point(rect.X + 5, mainY), rect.Width - 8);
            }
            else
            {
                DrawCentred(context, cap.Legend, LegendSize(cap.Legend), ink, rect, cap.FrontLegend != null ? -6 : 0);
            }

            if (cap.FrontLegend != null)
            {
                IBrush front = cap.Role == KeyRole.Blank ? BlankLegendBrush : FrontLegendBrush;
                DrawCentred(context, cap.FrontLegend, 9, front, new Rect(rect.X, rect.Bottom - 15, rect.Width, 14), 0);
            }
        }

        private IBrush FillFor(KeyCap cap)
        {
            if (ReferenceEquals(cap, _pressed))
            {
                return PressedBrush;
            }

            if ((cap.Latch == KeyLatch.Shift && ShiftLatched)
                || (cap.Latch == KeyLatch.Alt && AltLatched)
                || (cap.Latch == KeyLatch.Lock && LockOn))
            {
                return LatchedBrush;
            }

            switch (cap.Role)
            {
                case KeyRole.Attention: return AttentionBrush;
                case KeyRole.Edit: return EditBrush;
                case KeyRole.Modifier: return ModifierBrush;
                case KeyRole.Blank: return BlankBrush;
                default: return CharacterBrush;
            }
        }

        // Long legends (FIELD MARK, CURSR SEL) get a smaller size so they fit one key.
        private static double LegendSize(string legend)
        {
            return legend.Length <= 2 ? 15 : legend.Length <= 5 ? 11 : legend.Length <= 9 ? 9 : 8;
        }

        // Centred inside the area: the text is laid out the area's width wide and centred by the
        // layout itself, then placed vertically by its measured height. Long legends wrap.
        private static void DrawCentred(DrawingContext context, string text, double size, IBrush brush, Rect area, double yOffset)
        {
            if (text.Length == 0)
            {
                return;
            }

            FormattedText formatted = Format(text, size, brush, area.Width - 4, TextAlignment.Center);
            double y = area.Y + (area.Height - formatted.Height) / 2 + yOffset;
            context.DrawText(formatted, new Point(area.X + 2, y));
        }

        private static void DrawText(DrawingContext context, string text, double size, IBrush brush, Point at, double maxWidth)
        {
            context.DrawText(Format(text, size, brush, maxWidth, TextAlignment.Left), at);
        }

        private static FormattedText Format(string text, double size, IBrush brush, double maxWidth, TextAlignment alignment)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush)
            {
                MaxTextWidth = Math.Max(1, maxWidth),
                TextAlignment = alignment,
            };
        }
    }
}
