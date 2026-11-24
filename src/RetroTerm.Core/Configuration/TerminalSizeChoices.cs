//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Globalization;

using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Core.Configuration
{
    /// <summary>
    /// The screen sizes a connection form offers for a terminal: only what that terminal really
    /// had (Ronny, 23 November 2026).
    /// </summary>
    /// <remarks>
    /// Three kinds of terminal:
    ///  - The IBM 3278: the four models, each with its own fixed screen. The form shows the
    ///    terminal as IBM-3278 and picks the model with the size.
    ///  - A terminal that may follow the window (the profile has TerminalFeatures.HostResize, as
    ///    the VT100, the VT220 and the xterms do): any width and height may be typed in, held to
    ///    the same 1 to 512 limits a host's own resize request is held to (Ronny: "some terminals
    ///    allow free-size, like xterm"). The list is only a set of shortcuts, not a claim about
    ///    the hardware.
    ///  - Every other terminal (the TDVs, the VT52, the Tektronix 4014): its one screen, from its
    ///    profile.
    /// Until this class the form offered the same six sizes for every terminal, a TDV could be
    /// set to 132 x 43, and a 3270 connection ignored the box altogether.
    /// </remarks>
    public static class TerminalSizeChoices
    {
        /// <summary>
        /// The shortcuts offered for a terminal that takes any size: the six the form always
        /// listed.
        /// </summary>
        public static readonly string[] AnySizeShortcuts = { "80x24", "80x25", "132x24", "132x25", "80x43", "80x50" };

        /// <summary>
        /// The name the terminal box shows for every 3278 model; the size box picks the model.
        /// </summary>
        public const string Ibm3278 = "IBM-3278";

        /// <summary>
        /// Whether a terminal type is one of the IBM 3278 models, or the plain IBM-3278.
        /// </summary>
        /// <param name="emulatorType">
        /// The terminal type, as a connection stores it.
        /// </param>
        /// <returns>
        /// True for IBM-3278 and IBM-3278-2 to IBM-3278-5, in any case.
        /// </returns>
        public static bool Is3278(string? emulatorType)
        {
            return emulatorType != null && emulatorType.StartsWith(Ibm3278, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The sizes to offer for a terminal type.
        /// </summary>
        /// <param name="emulatorType">
        /// The terminal type, as a connection stores it.
        /// </param>
        /// <returns>
        /// For a 3278, one entry per model, such as "80x24 (model 2)". For a terminal that takes
        /// any size, the shortcuts with the terminal's own size first. Otherwise the terminal's
        /// one size.
        /// </returns>
        public static string[] For(string emulatorType)
        {
            if (Is3278(emulatorType))
            {
                string[] labels = new string[ConnectionFactory.Ibm3278Models.Length];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = ModelLabel(i);
                }

                return labels;
            }

            var (width, height) = EmulatorFactory.GetRecommendedSize(emulatorType);
            string own = Format(width, height);
            if (!AllowsAnySize(emulatorType))
            {
                return new[] { own };
            }

            // The terminal's own size first, then the shortcuts that are not that size.
            int count = 1;
            for (int i = 0; i < AnySizeShortcuts.Length; i++)
            {
                if (AnySizeShortcuts[i] != own)
                {
                    count++;
                }
            }

            string[] list = new string[count];
            list[0] = own;
            int next = 1;
            for (int i = 0; i < AnySizeShortcuts.Length; i++)
            {
                if (AnySizeShortcuts[i] != own)
                {
                    list[next++] = AnySizeShortcuts[i];
                }
            }

            return list;
        }

        /// <summary>
        /// Whether any width and height may be typed in for a terminal type.
        /// </summary>
        /// <param name="emulatorType">
        /// The terminal type, as a connection stores it.
        /// </param>
        /// <returns>
        /// True when the terminal's profile has TerminalFeatures.HostResize; false for a 3278 and
        /// for an unknown type.
        /// </returns>
        public static bool AllowsAnySize(string emulatorType)
        {
            if (Is3278(emulatorType))
            {
                return false;
            }

            try
            {
                return EmulatorFactory.GetProfile(emulatorType).Supports(TerminalFeatures.HostResize);
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether a size may be typed into the size box for a terminal type: any terminal that
        /// takes any size (<see cref="AllowsAnySize"/>), and a 3278, whose screen may be made
        /// larger than its model's (Ibm3270Emulator's three-argument constructor). For a 3278
        /// the list is still the four models; a typed size is read by <see cref="Read3278Size"/>.
        /// </summary>
        /// <param name="emulatorType">
        /// The terminal type, as a connection stores it.
        /// </param>
        /// <returns>
        /// True when the box should be editable.
        /// </returns>
        public static bool AcceptsTypedSize(string emulatorType)
        {
            return AllowsAnySize(emulatorType) || Is3278(emulatorType);
        }

        /// <summary>
        /// Reads what was typed or picked in a 3278's size box: a model entry picks that model;
        /// a size that is exactly a model's own screen picks that model; any other size is a
        /// screen larger than the current model's, checked by the emulator's own limits
        /// (ConnectionFactory.CheckIbm3270ScreenSize).
        /// </summary>
        /// <param name="text">
        /// The text in the box.
        /// </param>
        /// <param name="currentModel">
        /// The model the connection has now, IBM-3278-2 to IBM-3278-5; a larger screen keeps it.
        /// </param>
        /// <param name="model">
        /// The model to store.
        /// </param>
        /// <param name="screenSize">
        /// The screen to store, such as "100x50", or null for the model's own.
        /// </param>
        /// <returns>
        /// Null when the text is a screen this terminal can have, else why not.
        /// </returns>
        public static string? Read3278Size(string? text, string currentModel, out string model, out string? screenSize)
        {
            model = ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, currentModel);
            screenSize = null;

            string? picked = ModelFor(text);
            if (picked != null)
            {
                model = picked;
                return null;
            }

            if (!ConnectionFactory.TryReadScreenSize(text, out int columns, out int rows))
            {
                return "a screen size is columns x rows, such as 100x50";
            }

            string typed = Format(columns, rows);
            for (int i = 0; i < ConnectionFactory.Ibm3278ModelSizes.Length; i++)
            {
                if (ConnectionFactory.Ibm3278ModelSizes[i] == typed)
                {
                    model = ConnectionFactory.Ibm3278Models[i];
                    return null;
                }
            }

            int modelNumber = model[model.Length - 1] - '0';
            string? problem = ConnectionFactory.CheckIbm3270ScreenSize(modelNumber, typed, out _, out _);
            if (problem != null)
            {
                return problem;
            }

            screenSize = typed;
            return null;
        }

        /// <summary>
        /// The size-box entry for a 3278 model.
        /// </summary>
        /// <param name="emulatorType">
        /// IBM-3278-2 to IBM-3278-5.
        /// </param>
        /// <returns>
        /// Such as "132x27 (model 5)", or null when the type is not one of the four models.
        /// </returns>
        public static string? LabelFor3278(string emulatorType)
        {
            for (int i = 0; i < ConnectionFactory.Ibm3278Models.Length; i++)
            {
                if (string.Equals(emulatorType, ConnectionFactory.Ibm3278Models[i], StringComparison.OrdinalIgnoreCase))
                {
                    return ModelLabel(i);
                }
            }

            return null;
        }

        /// <summary>
        /// The 3278 model a size-box entry picks.
        /// </summary>
        /// <param name="label">
        /// An entry from <see cref="For"/> for a 3278.
        /// </param>
        /// <returns>
        /// IBM-3278-2 to IBM-3278-5, or null when the entry is not a model.
        /// </returns>
        public static string? ModelFor(string? label)
        {
            for (int i = 0; i < ConnectionFactory.Ibm3278Models.Length; i++)
            {
                if (label == ModelLabel(i))
                {
                    return ConnectionFactory.Ibm3278Models[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Reads a typed size, columns x rows, such as "100x40".
        /// </summary>
        /// <param name="text">
        /// The text typed or chosen; spaces around the numbers and an upper-case X are accepted.
        /// </param>
        /// <param name="columns">
        /// The columns, when the text is a size.
        /// </param>
        /// <param name="rows">
        /// The rows, when the text is a size.
        /// </param>
        /// <returns>
        /// False when the text is not two whole numbers joined by an x, or either is outside
        /// TerminalEmulatorBase.MinimumHostRequestedSize to MaximumHostRequestedSize (1 to 512).
        /// </returns>
        public static bool TryParse(string? text, out int columns, out int rows)
        {
            // The form is read by the one reader the 3278 screen size uses too.
            if (!ConnectionFactory.TryReadScreenSize(text, out columns, out rows))
            {
                return false;
            }

            return columns >= TerminalEmulatorBase.MinimumHostRequestedSize && columns <= TerminalEmulatorBase.MaximumHostRequestedSize
                && rows >= TerminalEmulatorBase.MinimumHostRequestedSize && rows <= TerminalEmulatorBase.MaximumHostRequestedSize;
        }

        /// <summary>
        /// Writes a size the way a connection stores it.
        /// </summary>
        /// <param name="columns">
        /// The columns.
        /// </param>
        /// <param name="rows">
        /// The rows.
        /// </param>
        /// <returns>
        /// Such as "80x24".
        /// </returns>
        public static string Format(int columns, int rows)
        {
            return columns.ToString(CultureInfo.InvariantCulture) + "x" + rows.ToString(CultureInfo.InvariantCulture);
        }

        private static string ModelLabel(int index)
        {
            string model = ConnectionFactory.Ibm3278Models[index].Substring(Ibm3278.Length + 1);
            return ConnectionFactory.Ibm3278ModelSizes[index] + " (model " + model + ")";
        }
    }
}
