//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroTerm.Core.Terminal.Emulators;

namespace RetroTerm.Core.Terminal.Input.Keyboards
{
    /// <summary>
    /// Hands one press on a described on-screen keyboard to the tab's terminal: a key name goes
    /// through the terminal's key table (the same one SENDKEY uses), text is typed on the
    /// terminal's own keyboard.
    /// </summary>
    public static class OnScreenKeyPress
    {
        /// <summary>
        /// Delivers a press.
        /// </summary>
        /// <param name="emulator">
        /// The active tab's terminal, or null when there is no tab.
        /// </param>
        /// <param name="family">
        /// The key family of the keyboard that was clicked.
        /// </param>
        /// <param name="keyName">
        /// The key name, without the family, or null.
        /// </param>
        /// <param name="text">
        /// The text to type, or null.
        /// </param>
        /// <returns>
        /// Null when the press was delivered; otherwise what to tell the person (shown in the
        /// status bar), because nothing was sent.
        /// </returns>
        public static string? Deliver(TerminalEmulatorBase? emulator, string family, string? keyName, string? text)
        {
            if (emulator == null)
            {
                return "No terminal tab is active";
            }

            if (keyName != null)
            {
                if (emulator is not INamedKeyTerminal named || named.KeyFamily != family)
                {
                    // The window follows the active tab, so this means the tab changed between
                    // the drawing and the click. Refuse rather than press a wrong key.
                    return family + "." + keyName + " is not a key of this tab's terminal; nothing was sent";
                }

                switch (named.PressNamedKey(keyName, out string? reason))
                {
                    case NamedKeyResult.Pressed:
                        return null;
                    case NamedKeyResult.NotBuilt:
                        return family + "." + keyName + " is not built yet (" + reason + "); nothing was sent";
                    default:
                        return "Unknown key " + family + "." + keyName + "; nothing was sent";
                }
            }

            if (text != null)
            {
                if (emulator is ILocalKeyboard local)
                {
                    local.HandleText(text);
                    return null;
                }

                return "This terminal does not take typed text from the on-screen keyboard";
            }

            return null;
        }
    }
}
