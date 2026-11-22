//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

namespace RetroTerm.Core.Terminal.Input.Keyboards
{
    /// <summary>
    /// What kind of key a cap is, which decides its colour on screen.
    /// </summary>
    public enum KeyRole
    {
        /// <summary>
        /// A key that types a character.
        /// </summary>
        Character,

        /// <summary>
        /// A key that sends to the host (Enter, PF, PA, Clear on a 3270).
        /// </summary>
        Attention,

        /// <summary>
        /// A local editing or cursor key.
        /// </summary>
        Edit,

        /// <summary>
        /// Shift, Alt, Lock.
        /// </summary>
        Modifier,

        /// <summary>
        /// A key that is on the real keyboard but does nothing in RetroTerm.
        /// </summary>
        Blank,
    }

    /// <summary>
    /// The modifier a key latches when pressed on screen.
    /// </summary>
    public enum KeyLatch
    {
        /// <summary>
        /// Not a modifier.
        /// </summary>
        None,

        /// <summary>
        /// Shift for the next key.
        /// </summary>
        Shift,

        /// <summary>
        /// Alt for the next key: on the 3278 it reaches the function printed on the key front.
        /// </summary>
        Alt,

        /// <summary>
        /// Shift until pressed again (the 3278 Lock key).
        /// </summary>
        Lock,
    }

    /// <summary>
    /// One key cap on an on-screen keyboard: where it is, what is printed on it, and what it does.
    /// </summary>
    /// <remarks>
    /// Position and size are in key units (1 = one ordinary letter key). What it does is either
    /// text to type or a key name pressed through the terminal's one key table
    /// (<see cref="INamedKeyTerminal"/>), so a click does exactly what SENDKEY does.
    /// </remarks>
    public sealed class KeyCap
    {
        /// <summary>
        /// Creates a key cap.
        /// </summary>
        /// <param name="x">
        /// Left edge, in key units.
        /// </param>
        /// <param name="y">
        /// Top edge, in key units (row).
        /// </param>
        /// <param name="width">
        /// Width in key units.
        /// </param>
        /// <param name="legend">
        /// The main legend.
        /// </param>
        public KeyCap(double x, double y, double width, string legend)
        {
            X = x;
            Y = y;
            Width = width;
            Legend = legend;
        }

        /// <summary>
        /// Left edge, in key units.
        /// </summary>
        public double X { get; }

        /// <summary>
        /// Top edge, in key units.
        /// </summary>
        public double Y { get; }

        /// <summary>
        /// Width in key units.
        /// </summary>
        public double Width { get; }

        /// <summary>
        /// Height in key units; 1 unless the key spans rows.
        /// </summary>
        public double Height { get; set; } = 1.0;

        /// <summary>
        /// The main legend, printed in the middle (or at the bottom when there is a top legend).
        /// </summary>
        public string Legend { get; }

        /// <summary>
        /// The legend printed at the top: the shifted character.
        /// </summary>
        public string? TopLegend { get; set; }

        /// <summary>
        /// The legend printed on the key front: the function reached with Alt (3278), or a
        /// second legend the source drawing shows.
        /// </summary>
        public string? FrontLegend { get; set; }

        /// <summary>
        /// The colour family.
        /// </summary>
        public KeyRole Role { get; set; } = KeyRole.Character;

        /// <summary>
        /// The modifier this key latches, or None.
        /// </summary>
        public KeyLatch Latch { get; set; } = KeyLatch.None;

        /// <summary>
        /// Text typed without Shift, or null.
        /// </summary>
        public string? Types { get; set; }

        /// <summary>
        /// Text typed with Shift, or null to type <see cref="Types"/> again.
        /// </summary>
        public string? ShiftTypes { get; set; }

        /// <summary>
        /// The key name pressed (without the family), or null.
        /// </summary>
        public string? Press { get; set; }

        /// <summary>
        /// The key name pressed with Shift, or null to use <see cref="Press"/>.
        /// </summary>
        public string? ShiftPress { get; set; }

        /// <summary>
        /// The key name pressed with Alt (the front legend), or null when Alt does nothing here.
        /// </summary>
        public string? AltPress { get; set; }

        /// <summary>
        /// Works out what one on-screen press does.
        /// </summary>
        /// <param name="shift">
        /// Shift (or Lock) is latched.
        /// </param>
        /// <param name="alt">
        /// Alt is latched.
        /// </param>
        /// <param name="keyName">
        /// The key name to press, or null.
        /// </param>
        /// <param name="text">
        /// The text to type, or null.
        /// </param>
        /// <returns>
        /// False when the press does nothing (a blank key, or Alt on a key with no Alt function).
        /// </returns>
        public bool ResolvePress(bool shift, bool alt, out string? keyName, out string? text)
        {
            keyName = null;
            text = null;

            if (alt)
            {
                // Alt reaches only the front function; it never falls back to the plain key, as
                // on the 3278 (GA27-2890-4 page 3-7: the ALT function works only while ALT is held).
                keyName = AltPress;
                return keyName != null;
            }

            if (Press != null)
            {
                keyName = shift && ShiftPress != null ? ShiftPress : Press;
                return true;
            }

            if (Types != null)
            {
                text = shift && ShiftTypes != null ? ShiftTypes : Types;
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// A whole on-screen keyboard: its keys and the key family their names belong to.
    /// </summary>
    public sealed class KeyboardDescription
    {
        /// <summary>
        /// Creates a description.
        /// </summary>
        /// <param name="id">
        /// A short stable id, for remembering the choice.
        /// </param>
        /// <param name="name">
        /// The name shown to the person.
        /// </param>
        /// <param name="family">
        /// The key family of the key names (IBM, TDV, VT).
        /// </param>
        /// <param name="keys">
        /// The keys.
        /// </param>
        public KeyboardDescription(string id, string name, string family, IReadOnlyList<KeyCap> keys)
        {
            Id = id;
            Name = name;
            Family = family;
            Keys = keys;

            double w = 0, h = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                double r = keys[i].X + keys[i].Width;
                double b = keys[i].Y + keys[i].Height;
                if (r > w) w = r;
                if (b > h) h = b;
            }

            Width = w;
            Height = h;
        }

        /// <summary>
        /// A short stable id.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The name shown to the person.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The key family of every key name in this keyboard.
        /// </summary>
        public string Family { get; }

        /// <summary>
        /// The keys.
        /// </summary>
        public IReadOnlyList<KeyCap> Keys { get; }

        /// <summary>
        /// Total width in key units.
        /// </summary>
        public double Width { get; }

        /// <summary>
        /// Total height in key units.
        /// </summary>
        public double Height { get; }
    }

    /// <summary>
    /// A terminal that brings its own on-screen keyboards.
    /// </summary>
    public interface IHasOnScreenKeyboards
    {
        /// <summary>
        /// The keyboards this terminal can show, the usual one first.
        /// </summary>
        IReadOnlyList<KeyboardDescription> OnScreenKeyboards { get; }
    }
}
