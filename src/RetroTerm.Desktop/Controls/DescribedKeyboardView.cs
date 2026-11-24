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

            List<KeyLabel> labels = LayoutLabels(cap);
            for (int i = 0; i < labels.Count; i++)
            {
                context.DrawText(labels[i].Text, labels[i].Origin);
            }
        }

        /// <summary>
        /// One legend of a cap, laid out and placed: the text and where its top left goes, in this
        /// control's coordinates. Drawing and the label-fit test both use this, so the test
        /// measures what is drawn.
        /// </summary>
        internal readonly struct KeyLabel
        {
            public KeyLabel(string which, FormattedText text, Point origin)
            {
                Which = which;
                Text = text;
                Origin = origin;
            }

            /// <summary>
            /// "main", "top" or "front".
            /// </summary>
            public string Which { get; }

            public FormattedText Text { get; }

            public Point Origin { get; }
        }

        /// <summary>
        /// The key's drawn rectangle in this control's coordinates.
        /// </summary>
        internal static Rect KeyRect(KeyCap cap) => CapRect(cap);

        /// <summary>
        /// Lays out every legend of a cap.
        /// </summary>
        internal static List<KeyLabel> LayoutLabels(KeyCap cap)
        {
            Rect rect = CapRect(cap);
            var labels = new List<KeyLabel>(3);
            IBrush ink = cap.Role == KeyRole.Blank ? BlankLegendBrush : LegendBrush;

            // The legends are placed from the outside in, each by its MEASURED size, so none of
            // them can run into another or off the key. Two faults this replaced (found by eye on
            // the 3278, then pinned by EveryLegendsInkFitsItsKeyAndTouchesNoOtherLegend): the front
            // legend got a fixed 14 px strip at 9 pt, so ERASE INPUT and ALT CURSR wrapped to two
            // lines and ran off the bottom of the key; and the main legend was sized by counting
            // characters and moved up a fixed 6 px whatever the front legend took, so CURSR BLINK
            // ran into ALT CURSR.
            double top = rect.Y + 2;
            double bottom = rect.Bottom - 2;
            double inner = rect.Width - 8;

            // Front legend first: ONE line, at the largest size up to 9 pt whose width fits the
            // key, sat on the key's bottom edge. Everything above it is what the others may use.
            if (cap.FrontLegend != null)
            {
                IBrush front = cap.Role == KeyRole.Blank ? BlankLegendBrush : FrontLegendBrush;
                FormattedText formatted = OneLine(cap.FrontLegend, 9, front, inner);
                double y = bottom - formatted.Height;
                labels.Add(new KeyLabel("front", formatted, new Point(rect.X + (rect.Width - formatted.Width) / 2, y)));
                bottom = y;
            }

            if (cap.TopLegend != null)
            {
                FormattedText formatted = OneLine(cap.TopLegend, 11, ink, inner);
                labels.Add(new KeyLabel("top", formatted, new Point(rect.X + 5, top)));
                top += formatted.Height;
                if (cap.Legend.Length > 0)
                {
                    // Left aligned under the top legend, like the shifted pair on a real cap.
                    FormattedText main = Fitted(cap.Legend, cap.FrontLegend != null ? 11 : 13, ink, inner, bottom - top, TextAlignment.Left);
                    labels.Add(new KeyLabel("main", main, new Point(rect.X + 5, top + (bottom - top - main.Height) / 2)));
                }
            }
            else if (cap.Legend.Length > 0)
            {
                // Centred in the space left between the key's top and the front legend. It may wrap
                // (CURSR BLINK takes two lines) but must fit that space, so the size comes down
                // until the measured block does.
                FormattedText main = Fitted(cap.Legend, LegendSize(cap.Legend), ink, inner, bottom - top, TextAlignment.Center);
                labels.Add(new KeyLabel("main", main, new Point(rect.X + 4, top + (bottom - top - main.Height) / 2)));
            }

            return labels;
        }

        // The smallest size a legend is ever drawn at; below this it is not readable anyway.
        private const double MinimumLegendSize = 6;

        // A layout width no legend reaches, so the text is measured on one line at its natural
        // width. Never pass 0 for "no limit": a MaxTextWidth of 0 laid out nothing at all, every
        // top and front legend vanished (seen in the rendered PNG on 24 November 2026).
        private const double UnwrappedWidth = 10000;

        // One line, no wrapping: the largest size up to `size` whose measured width fits.
        private static FormattedText OneLine(string text, double size, IBrush brush, double maxWidth)
        {
            FormattedText formatted = Format(text, size, brush, UnwrappedWidth, TextAlignment.Left);
            while (formatted.Width > maxWidth && size > MinimumLegendSize)
            {
                size -= 0.5;
                formatted = Format(text, size, brush, UnwrappedWidth, TextAlignment.Left);
            }

            return formatted;
        }

        // Wrapped to `maxWidth`: the largest size up to `size` whose measured block fits both the
        // width (a single word wider than the box overflows it, so width is checked too) and the
        // height it is given.
        private static FormattedText Fitted(string text, double size, IBrush brush, double maxWidth, double maxHeight, TextAlignment alignment)
        {
            FormattedText formatted = Format(text, size, brush, maxWidth, alignment);
            while ((formatted.Height > maxHeight || formatted.Width > maxWidth) && size > MinimumLegendSize)
            {
                size -= 0.5;
                formatted = Format(text, size, brush, maxWidth, alignment);
            }

            return formatted;
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
