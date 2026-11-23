//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Input;
using RetroTerm.Core.Terminal.Input.Keyboards;
using RetroTerm.Core.Terminal.Profiles;

namespace RetroTerm.Ibm3270
{
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
    /// <para>
    /// The picture is 25 rows: the host's 24, and line 25, the operator information area, built
    /// by RetroIO's <see cref="StatusLine3270"/> in the layout x3270's wc3270 draws (decided by
    /// Ronny, 22 and 23 November 2026). The host never writes line 25; the model 2 is still 24 by
    /// 80 to the host.
    /// </para>
    /// </remarks>
    public sealed class Ibm3270Emulator : TerminalEmulatorBase, ILocalKeyboard, INamedKeyTerminal, IHasOnScreenKeyboards, IConnectionStatusAware, IBreakSource, IStatusLineTerminal
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
            rows: StatusRow + 1);

        // Line 25: the row under the host's 24.
        private const int StatusRow = 24;

        private readonly Display3270 _display;

        // Where the connection is, for line 25. Set on the session thread by the session.
        private StatusLineConnection _connection = StatusLineConnection.NotConnected;

        /// <summary>
        /// Creates a model 2 (24 x 80) display, with line 25 under it.
        /// </summary>
        public Ibm3270Emulator()
            : base(80, StatusRow + 1, maxScrollback: 0)
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
        public event Ibm3270Message? ProtocolError;

        /// <summary>
        /// Raised when the WCC asks for the alarm (GA23-0059-4 p. 3-3).
        /// </summary>
        public event Ibm3270Alarm? Alarm;

        /// <summary>
        /// Raised by the Attn key after its PA1 short read, for the session to send the telnet
        /// break (IBreakSource).
        /// </summary>
        public event BreakRequestedHandler? BreakRequested;

        /// <summary>
        /// The 3278 model 2 screen: 24 rows of 80 columns, and line 25.
        /// </summary>
        public override TerminalProfile Profile => Model2Profile;

        /// <summary>
        /// Gets the 3270 screen model, for the key handler and for MCP.
        /// </summary>
        public Display3270 Display => _display;

        /// <summary>
        /// Gets why the keyboard is locked, or null when it is not (IStatusLineTerminal).
        /// </summary>
        /// <remarks>
        /// Every lock the operator caused (protected field, too much data, a mandatory field) is
        /// reported as an operator error: the host never lifts those, only Reset does
        /// (Display3270.Reset).
        /// </remarks>
        public string? KeyboardLockReason => _display.Lock switch
        {
            KeyboardLock.None => null,
            KeyboardLock.WaitingForHost => Ibm3270LockReasons.WaitingForHost,
            KeyboardLock.SystemLock => Ibm3270LockReasons.SystemLock,
            _ => Ibm3270LockReasons.OperatorError,
        };

        /// <summary>
        /// Gets line 25 as drawn (IStatusLineTerminal), built by the same StatusLine3270 call the
        /// screen is painted from.
        /// </summary>
        public string DrawnStatusLine
        {
            get
            {
                Span<char> line = stackalloc char[_display.Columns];
                StatusLine3270.Build(_display, _connection, null, line);
                return new string(line);
            }
        }

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
        /// Shows the new connection state on line 25.
        /// </summary>
        /// <param name="status">
        /// The connection's new state.
        /// </param>
        public void ConnectionStatusChanged(ConnectionStatus status)
        {
            _connection = status switch
            {
                ConnectionStatus.Connected => StatusLineConnection.Connected,
                ConnectionStatus.Connecting => StatusLineConnection.Connecting,
                _ => StatusLineConnection.NotConnected,
            };
            Refresh();
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
        public event Ibm3270Message? KeyNotBuilt;

        /// <summary>
        /// A PC key press: turned into a 3270 key name (PcKeyToName) and pressed through the one
        /// name table (PressNamedKey) that SENDKEY, MCP and the on-screen keyboard also use.
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

            string? name = PcKeyToName(keyName, shift, control, alt);
            if (name == null)
            {
                // Taken but unused: a Ctrl or Alt key left untaken would let the canvas turn it into
                // a C0 byte and put it in the 3270 data stream. Untaken plain keys (letters, digits)
                // come back through HandleText.
                return control || alt;
            }

            if (PressNamedKey(name, out string? reason) == NamedKeyResult.NotBuilt)
            {
                KeyNotBuilt?.Invoke(reason!);
            }

            return true;
        }

        /// <summary>
        /// The PC key to 3270 key-name table. Source: docs\spec\IBM3270\KEYBOARD.md section 7 (what
        /// x3270 and tnz map), with Ronny's choices of 22 November 2026: Alt+letter gives 3270 keys
        /// as in x3270, and PgUp/PgDn are PF7/PF8 as in tnz.
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
        /// The 3270 key name, or null when the PC key is not a 3270 key.
        /// </returns>
        internal static string? PcKeyToName(string keyName, bool shift, bool control, bool alt)
        {
            if (alt && !control)
            {
                switch (keyName)
                {
                    case "D1": return "PA1";
                    case "D2": return "PA2";
                    case "D3": return "PA3";
                    case "C": return "CLEAR";
                    case "R": return "RESET";
                    case "A": return "ATTN";
                    case "D": return "DUP";
                    case "F": return "FIELDMARK";
                    case "E": return "ERASEEOF";
                    case "I": return "INSERT";
                    default: return null;
                }
            }

            if (control || alt)
            {
                return null;
            }

            if (keyName.Length >= 2 && keyName[0] == 'F' && int.TryParse(keyName.AsSpan(1), out int f) && f >= 1 && f <= 12)
            {
                return "PF" + (shift ? f + 12 : f).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            switch (keyName)
            {
                case "Enter":
                case "Return":
                    return "ENTER";
                case "PageUp":
                    return "PF7";
                case "PageDown":
                    return "PF8";
                case "Tab":
                    return shift ? "BACKTAB" : "TAB";
                case "Home":
                    return "HOME";
                case "Left":
                    return "LEFT";
                case "Right":
                    return "RIGHT";
                case "Up":
                    return "UP";
                case "Down":
                    return "DOWN";
                case "Back":
                    return "BACKSPACE";
                case "Delete":
                    return "DELETE";
                case "Insert":
                    return "INSERT";
                case "End":
                    return "END";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The keyboards the on-screen keyboard window offers for this terminal: the 3278 and the 3270 PC.
        /// </summary>
        public IReadOnlyList<KeyboardDescription> OnScreenKeyboards => Ibm3270Keyboards.All;

        /// <summary>
        /// The prefix scripts and MCP use for this terminal's keys, as in IBM.PF3.
        /// </summary>
        public string KeyFamily => "IBM";

        /// <summary>
        /// Every key name this terminal knows, built or not.
        /// </summary>
        public IReadOnlyList<string> KeyNames => AllKeyNames;

        // Every 3270 key RetroTerm knows by name. The names are the legends on the IBM 3278 keyboard
        // (GA27-2890-4), written without spaces: ERASE EOF is ERASEEOF.
        private static readonly string[] AllKeyNames = BuildKeyNames();

        private static string[] BuildKeyNames()
        {
            string[] fixedNames =
            {
                "ENTER", "CLEAR", "PA1", "PA2", "PA3", "RESET", "TAB", "BACKTAB", "HOME", "LEFT",
                "RIGHT", "UP", "DOWN", "NEWLINE", "BACKSPACE", "DELETE", "INSERT", "END", "ERASEEOF",
                "ERASEINPUT", "DUP", "FIELDMARK", "ATTN", "SYSREQ", "CURSORSELECT",
            };
            var names = new string[fixedNames.Length + 24];
            for (int i = 0; i < fixedNames.Length; i++)
            {
                names[i] = fixedNames[i];
            }

            for (int i = 1; i <= 24; i++)
            {
                names[fixedNames.Length + i - 1] = "PF" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        /// <summary>
        /// Presses a 3270 key by its name.
        /// </summary>
        /// <param name="name">
        /// The key name without the family, such as PF3 or ENTER.
        /// </param>
        /// <param name="notBuiltReason">
        /// Set to the reason when the key is known but not built yet.
        /// </param>
        /// <returns>
        /// Whether the key was pressed, is unknown, or is not built yet.
        /// </returns>
        public NamedKeyResult PressNamedKey(string name, out string? notBuiltReason)
        {
            notBuiltReason = null;
            string key = name.ToUpperInvariant();

            if (key.Length >= 3 && key.StartsWith("PF", StringComparison.Ordinal)
                && int.TryParse(key.AsSpan(2), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int pf))
            {
                if (pf < 1 || pf > 24)
                {
                    return NamedKeyResult.UnknownName;
                }

                Attention(Aid.Pf(pf));
                return NamedKeyResult.Pressed;
            }

            switch (key)
            {
                case "ENTER": Attention(Aid.Enter); return NamedKeyResult.Pressed;
                case "CLEAR": Attention(Aid.Clear); return NamedKeyResult.Pressed;
                case "PA1": Attention(Aid.PA1); return NamedKeyResult.Pressed;
                case "PA2": Attention(Aid.PA2); return NamedKeyResult.Pressed;
                case "PA3": Attention(Aid.PA3); return NamedKeyResult.Pressed;
                case "RESET": Local(() => _display.Reset()); return NamedKeyResult.Pressed;
                case "TAB": Local(() => _display.Tab()); return NamedKeyResult.Pressed;
                case "HOME": Local(() => _display.Home()); return NamedKeyResult.Pressed;
                case "LEFT": Local(() => _display.MoveCursorBy(-1, 0)); return NamedKeyResult.Pressed;
                case "RIGHT": Local(() => _display.MoveCursorBy(1, 0)); return NamedKeyResult.Pressed;
                case "UP": Local(() => _display.MoveCursorBy(0, -1)); return NamedKeyResult.Pressed;
                case "DOWN": Local(() => _display.MoveCursorBy(0, 1)); return NamedKeyResult.Pressed;
                case "BACKTAB": Local(() => _display.BackTab()); return NamedKeyResult.Pressed;
                case "NEWLINE": Local(() => _display.NewLine()); return NamedKeyResult.Pressed;
                case "BACKSPACE": notBuiltReason = "Backspace"; return NamedKeyResult.NotBuilt;
                case "DELETE": Local(() => _display.Delete()); return NamedKeyResult.Pressed;
                case "INSERT": Local(() => _display.ToggleInsert()); return NamedKeyResult.Pressed;
                case "END": Local(() => _display.FieldEnd()); return NamedKeyResult.Pressed;
                case "ERASEEOF": Local(() => _display.EraseEndOfField()); return NamedKeyResult.Pressed;
                case "ERASEINPUT": Local(() => _display.EraseInput()); return NamedKeyResult.Pressed;
                case "DUP": Local(() => _display.Dup()); return NamedKeyResult.Pressed;
                case "FIELDMARK": Local(() => _display.FieldMark()); return NamedKeyResult.Pressed;
                case "ATTN":
                    // x3270 Attn_action in plain TN3270: PA1, then TELNET BREAK.
                    Local(() =>
                    {
                        _display.Attn();
                        BreakRequested?.Invoke();
                    });
                    return NamedKeyResult.Pressed;
                case "SYSREQ": Local(() => _display.SysReq()); return NamedKeyResult.Pressed;
                case "CURSORSELECT": Local(() => _display.CursorSelect()); return NamedKeyResult.Pressed;
                default: return NamedKeyResult.UnknownName;
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

        /// <summary>
        /// Where a key's effect on the screen runs: the session's pump thread, set by TerminalSession.
        /// Null (no session, a test) runs the change at once.
        /// </summary>
        public SessionThreadRunner? SessionThread { get; set; }

        private bool Attention(byte aid)
        {
            return Local(() => _display.Attention(aid));
        }

        private bool Local(SessionJob action)
        {
            // The decision (this key is ours) was made on the UI thread; the change runs on the
            // session thread (ILocalKeyboard.SessionThread).
            SessionThreadRunner? sessionThread = SessionThread;
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

            PaintStatusLine(columns);

            int cursor = _display.CursorAddress;
            Cursor.MoveTo(cursor / columns, cursor % columns);
        }

        // Line 25. wc3270 draws the "4" and the box in reverse video and the "A" underlined
        // (wc3270\screen.c, draw_oia); the rest is plain. A buffer cut below 25 rows (a resize
        // from outside) has no line 25 to draw.
        private void PaintStatusLine(int columns)
        {
            if (Buffer.Height <= StatusRow)
            {
                return;
            }

            Span<char> line = stackalloc char[columns];
            StatusLine3270.Build(_display, _connection, null, line);
            for (int column = 0; column < columns; column++)
            {
                CharacterAttributes flags = column switch
                {
                    StatusLine3270.ReadyColumn => CharacterAttributes.Reverse,
                    StatusLine3270.BoxColumn => CharacterAttributes.Reverse,
                    StatusLine3270.OnlineColumn => CharacterAttributes.Underline,
                    _ => CharacterAttributes.None,
                };
                Buffer.SetCell(StatusRow, column, new TerminalCell(line[column], flags, TerminalColor.Default, TerminalColor.Default));
            }
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
}
