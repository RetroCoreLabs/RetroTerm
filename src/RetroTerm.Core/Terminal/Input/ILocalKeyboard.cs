//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

namespace RetroTerm.Core.Terminal.Input;

/// <summary>
/// An emulator that handles keys ITSELF instead of sending them to the host.
/// </summary>
/// <remarks>
/// <para>
/// Every other emulator turns a key into bytes for the host at once. A block-mode terminal such
/// as the IBM 3270 does not: typing edits a field on the local screen, the cursor keys move the
/// local cursor, and only an attention key (Enter, PF, PA, Clear) sends anything. The key
/// handler offers every key to an emulator that implements this interface BEFORE the keyboard
/// mapper sees it; a key the emulator takes never reaches the mapper or the connection.
/// </para>
/// <para>
/// Key names are Avalonia's <c>Key</c> names as text ("Enter", "F1", "Tab", "Left", "PageUp",
/// "D1" for the 1 key), so Core needs no reference to Avalonia.
/// </para>
/// </remarks>
public interface ILocalKeyboard
{
    /// <summary>
    /// Offers a key press.
    /// </summary>
    /// <param name="keyName">
    /// The Avalonia key name.
    /// </param>
    /// <param name="shift">
    /// Shift held.
    /// </param>
    /// <param name="control">
    /// Ctrl held.
    /// </param>
    /// <param name="alt">
    /// Alt held.
    /// </param>
    /// <returns>
    /// True when the emulator took the key; nothing else may act on it then.
    /// </returns>
    bool HandleKey(string keyName, bool shift, bool control, bool alt);

    /// <summary>
    /// Offers typed text (what the keyboard layout produced).
    /// </summary>
    /// <param name="text">
    /// The text.
    /// </param>
    /// <returns>
    /// True when the emulator took it.
    /// </returns>
    bool HandleText(string text);
}
