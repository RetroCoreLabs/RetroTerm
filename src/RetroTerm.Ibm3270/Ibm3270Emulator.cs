//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Ibm3270;

/// <summary>
/// An IBM 3278/3279 display station shown through RetroTerm's normal cell buffer.
/// </summary>
/// <remarks>
/// <para>
/// A 3270 is not a stream terminal: the host sends whole records and the terminal edits its
/// fields locally until the operator presses an attention key. So this class does not use the
/// base class's escape-sequence parser at all. <see cref="ProcessData"/> takes ONE complete
/// record (telnet framing already removed by the connection) and hands it to the RetroIO
/// <see cref="Display3270"/>; the screen is then copied cell by cell into
/// <see cref="TerminalEmulatorBase.Buffer"/>, which is what the renderer draws.
/// </para>
/// <para>
/// What the terminal sends back (the reply to Enter, a PF key, a read command) leaves through
/// the base class's DataToSend event as one record without telnet framing; the 3270 connection
/// adds IAC EOR.
/// </para>
/// </remarks>
public sealed class Ibm3270Emulator : TerminalEmulatorBase, ILocalKeyboard
{
    private static readonly TerminalProfile Model2Profile = new(
        name: "IBM-3278-2",
        primaryDeviceAttributes: Array.Empty<byte>(),
        secondaryDeviceAttributes: Array.Empty<byte>(),
        features: TerminalFeatures.None,
        recognisedPrivateModes: Array.Empty<int>(),
        keyboardLayout: "IBM3270",
        transportEncoding: TransportEncoding.EightBit,
        columns: 80,
        rows: 24);

    private readonly Display3270 _display;

    /// <summary>
    /// Creates a model 2 (24 x 80) display.
    /// </summary>
    public Ibm3270Emulator()
        : base(80, 24, maxScrollback: 0)
    {
        _display = new Display3270(24, 80, 24, 80);
        _display.InboundRecord += OnInboundRecord;
        _display.ProtocolError += OnProtocolError;
        _display.Alarm += OnAlarm;
        Paint();
    }

    /// <summary>
    /// Raised when the host sent something the 3270 model rejects or has not built. The text
    /// says what and cites the manual page. Never swallowed: the session shows or logs it.
    /// </summary>
    public event Action<string>? ProtocolError;

    /// <summary>
    /// Raised when the WCC asks for the alarm (GA23-0059-4 p. 3-3).
    /// </summary>
    public event Action? Alarm;

    /// <inheritdoc/>
    public override TerminalProfile Profile => Model2Profile;

    /// <summary>
    /// Gets the 3270 screen model, for the key handler and for MCP.
    /// </summary>
    public Display3270 Display => _display;

    /// <summary>
    /// Applies ONE complete outbound 3270 record and repaints.
    /// </summary>
    /// <param name="data">
    /// The record, command byte first, no telnet framing.
    /// </param>
    public override void ProcessData(ReadOnlySpan<byte> data)
    {
        BeginBatch();
        try
        {
            _display.ProcessOutbound(data);
            Paint();
            OnInvalidated();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>
    /// Copies the operator's local change (typing, cursor moves) to the screen.
    /// Call after any key that the 3270 handles locally.
    /// </summary>
    public void Refresh()
    {
        BeginBatch();
        try
        {
            Paint();
            OnInvalidated();
        }
        finally
        {
            EndBatch();
        }
    }

    /// <summary>
    /// Raised when the operator pressed a 3270 key whose function is not built yet (the text
    /// names it). The key is still taken, so it never reaches the host as stray bytes.
    /// </summary>
    public event Action<string>? KeyNotBuilt;

    /// <summary>
    /// The PC key to 3270 key table. Source: docs\spec\IBM3270\KEYBOARD.md section 7 (what x3270
    /// and tnz map), with Ronny's choices of 22 November 2026: Alt+letter gives 3270 keys as in
    /// x3270, and PgUp/PgDn are PF7/PF8 as in tnz.
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
    /// True when taken.
    /// </returns>
    public bool HandleKey(string keyName, bool shift, bool control, bool alt)
    {
        // Ctrl+Shift belongs to RetroTerm itself (copy, paste, find).
        if (control && shift)
        {
            return false;
        }

        if (alt && !control)
        {
            switch (keyName)
            {
                case "D1": return Attention(Aid.PA1);
                case "D2": return Attention(Aid.PA2);
                case "D3": return Attention(Aid.PA3);
                case "C": return Attention(Aid.Clear);
                case "R": return Local(() => _display.Reset());
                case "A": return NotBuilt("Attn (needs the TN3270 connection: IAC BRK)");
                case "D": return NotBuilt("Dup");
                case "F": return NotBuilt("Field Mark");
                case "E": return NotBuilt("Erase EOF");
                case "I": return NotBuilt("Insert");
                default: return true; // taken: an unused Alt key must not reach the line
            }
        }

        if (control || alt)
        {
            // Taken and ignored. Left untaken, the canvas would turn Ctrl+letter into a C0 byte
            // and send it into the 3270 data stream.
            return true;
        }

        if (keyName.Length >= 2 && keyName[0] == 'F' && int.TryParse(keyName.AsSpan(1), out int f) && f >= 1 && f <= 12)
        {
            return Attention(Aid.Pf(shift ? f + 12 : f));
        }

        switch (keyName)
        {
            case "Enter":
            case "Return":
                return Attention(Aid.Enter);
            case "PageUp":
                return Attention(Aid.Pf(7));
            case "PageDown":
                return Attention(Aid.Pf(8));
            case "Tab":
                return shift ? NotBuilt("Back Tab") : Local(() => _display.Tab());
            case "Home":
                return Local(() => _display.Home());
            case "Left":
                return Local(() => MoveBy(-1));
            case "Right":
                return Local(() => MoveBy(1));
            case "Up":
                return Local(() => MoveBy(-_display.Columns));
            case "Down":
                return Local(() => MoveBy(_display.Columns));
            case "Back":
                return NotBuilt("Backspace");
            case "Delete":
                return NotBuilt("Delete");
            case "Insert":
                return NotBuilt("Insert");
            case "End":
                return NotBuilt("End (go to end of field)");
            case "Escape":
                return NotBuilt("Escape has no 3270 meaning yet");
            default:
                // Letters, digits and symbols come through HandleText.
                return false;
        }
    }

    /// <summary>
    /// Types text into the field at the cursor. A refused character (protected field, keyboard
    /// locked, not in code page 037) is dropped, as on the real terminal.
    /// </summary>
    /// <param name="text">
    /// The typed text.
    /// </param>
    /// <returns>
    /// Always true: no typed text may reach the host directly.
    /// </returns>
    public bool HandleText(string text)
    {
        return Local(() =>
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    continue;
                }

                _display.Type(c);
            }
        });
    }

    /// <inheritdoc/>
    public Action<Action>? SessionThread { get; set; }

    private bool Attention(byte aid)
    {
        return Local(() => _display.Attention(aid));
    }

    private bool Local(Action action)
    {
        // The decision (this key is ours) was made on the UI thread; the change runs on the
        // session thread (ILocalKeyboard.SessionThread).
        Action<Action>? sessionThread = SessionThread;
        if (sessionThread == null)
        {
            action();
            Refresh();
        }
        else
        {
            sessionThread(() =>
            {
                action();
                Refresh();
            });
        }

        return true;
    }

    private bool NotBuilt(string what)
    {
        KeyNotBuilt?.Invoke(what);
        return true;
    }

    private void MoveBy(int delta)
    {
        int size = _display.Size;
        int a = (_display.CursorAddress + delta) % size;
        if (a < 0)
        {
            a += size;
        }

        _display.MoveCursor(a);
    }

    private void Paint()
    {
        int columns = _display.Columns;
        int size = _display.Size;
        for (int address = 0; address < size; address++)
        {
            int row = address / columns;
            int column = address % columns;
            Buffer.SetCell(row, column, CellFor(address));
        }

        int cursor = _display.CursorAddress;
        Cursor.MoveTo(cursor / columns, cursor % columns);
    }

    private TerminalCell CellFor(int address)
    {
        int fa = _display.GetFieldAttributeFor(address);
        byte attribute = fa >= 0 ? _display.GetByte(fa) : (byte)0;

        TerminalColor foreground = BaseColour(fa, attribute);
        CharacterAttributes flags = CharacterAttributes.None;

        // A field attribute position shows as a blank (GA23-0059-4 p. 4-13).
        if (_display.IsFieldAttribute(address))
        {
            return new TerminalCell(' ', flags, foreground, TerminalColor.Default);
        }

        // Nondisplay fields show nothing (p. 4-13) - a password field.
        if ((attribute & FieldAttribute.DisplayMask) == FieldAttribute.NonDisplay)
        {
            return new TerminalCell(' ', flags, foreground, TerminalColor.Default);
        }

        if ((attribute & FieldAttribute.DisplayMask) == FieldAttribute.Intensified)
        {
            flags |= CharacterAttributes.Bold;
        }

        // Extended colour: the character attribute wins over the field's (p. 4-15).
        byte colour = _display.GetCharacterColor(address);
        if (colour == 0 && fa >= 0)
        {
            colour = _display.GetFieldColor(fa);
        }

        if (colour != 0)
        {
            foreground = ExtendedColour(colour, foreground);
        }

        byte highlight = _display.GetCharacterHighlight(address);
        if (highlight == 0 && fa >= 0)
        {
            highlight = _display.GetFieldHighlight(fa);
        }

        // Highlight values X'F1' blink, X'F2' reverse, X'F4' underscore (GA23-0059-4 p. 4-18).
        switch (highlight)
        {
            case 0xF1:
                flags |= CharacterAttributes.Blink;
                break;
            case 0xF2:
                flags |= CharacterAttributes.Reverse;
                break;
            case 0xF4:
                flags |= CharacterAttributes.Underline;
                break;
        }

        // GE characters (code page 310, APL) are not drawn yet: they show as the base page
        // character until the 3270 font exists. Plan: docs\PLAN.md, the font item.
        char c = CodePage037.ToUnicode(_display.GetByte(address));
        if (c == '\0')
        {
            c = ' ';
        }

        return new TerminalCell(c, flags, foreground, TerminalColor.Default);
    }

    /// <summary>
    /// The 3279 "base colour" for a field with no extended colour: unprotected normal green,
    /// unprotected intensified red, protected normal blue, protected intensified white.
    /// </summary>
    /// <remarks>
    /// The IBM page for this table has not been found yet (not verified against GA23-0059-4); x3270
    /// shows the same four colours. Uses the palette indexes so the theme still applies.
    /// Unformatted screen: green.
    /// </remarks>
    private static TerminalColor BaseColour(int fa, byte attribute)
    {
        if (fa < 0)
        {
            return TerminalColor.FromIndex(10);
        }

        bool isProtected = FieldAttribute.IsProtected(attribute);
        bool intensified = (attribute & FieldAttribute.DisplayMask) == FieldAttribute.Intensified;
        if (isProtected)
        {
            return TerminalColor.FromIndex(intensified ? (byte)15 : (byte)12);
        }

        return TerminalColor.FromIndex(intensified ? (byte)9 : (byte)10);
    }

    /// <summary>
    /// Extended colour values X'F1'-X'F7' (GA23-0059-4 p. 4-18): blue, red, pink, green,
    /// turquoise, yellow, white. X'F0' and X'00' mean default.
    /// </summary>
    private static TerminalColor ExtendedColour(byte value, TerminalColor fallback)
    {
        switch (value)
        {
            case 0xF1: return TerminalColor.FromIndex(12);
            case 0xF2: return TerminalColor.FromIndex(9);
            case 0xF3: return TerminalColor.FromIndex(13);
            case 0xF4: return TerminalColor.FromIndex(10);
            case 0xF5: return TerminalColor.FromIndex(14);
            case 0xF6: return TerminalColor.FromIndex(11);
            case 0xF7: return TerminalColor.FromIndex(15);
            default: return fallback;
        }
    }

    private void OnInboundRecord(ReadOnlySpan<byte> record)
    {
        SendResponse(record);
    }

    private void OnProtocolError(string message)
    {
        ProtocolError?.Invoke(message);
    }

    private void OnAlarm()
    {
        Alarm?.Invoke();
    }
}
