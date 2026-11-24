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
using RetroTerm.Core.Transfer;

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
    public sealed class Ibm3270Emulator : TerminalEmulatorBase, ILocalKeyboard, INamedKeyTerminal, IHasOnScreenKeyboards, IConnectionStatusAware, IBreakSource, IStatusLineTerminal, IPointerCursorTerminal, IFieldPaste, IFieldScreen, IHostFileTransfer, ICodePageTerminal, ITn3270EAware, IRuledRowTerminal, IHostRecordOutcomeTerminal, IModelScreenTerminal
    {
        // The 3279 colours as x3270 draws them in RGB mode: Common\c3270\cscreen.c, rgbmap (the
        // table wc3270 also takes its blue from, remap_blue in wc3270\screen.c). Fixed RGB, not
        // the theme's palette: a 3279 has its own colours (Ronny, 22 November 2026: align the
        // colours with wc3270), and the theme's dark green background made the blue dim.
        private static readonly TerminalColor NeutralBlack = TerminalColor.FromRgb(0x10, 0x10, 0x10);
        private static readonly TerminalColor Blue = TerminalColor.FromRgb(0x1E, 0x90, 0xFF);
        private static readonly TerminalColor Red = TerminalColor.FromRgb(0xFF, 0x00, 0x00);
        private static readonly TerminalColor Pink = TerminalColor.FromRgb(0xFF, 0x00, 0xFF);
        private static readonly TerminalColor Green = TerminalColor.FromRgb(0x32, 0xCD, 0x32);
        private static readonly TerminalColor Turquoise = TerminalColor.FromRgb(0x00, 0xFF, 0xFF);
        private static readonly TerminalColor Yellow = TerminalColor.FromRgb(0xFF, 0xFF, 0x00);
        private static readonly TerminalColor NeutralWhite = TerminalColor.FromRgb(0xFF, 0xFF, 0xFF);

        // The operator information area: the row under the model's largest screen (line 25 on a
        // model 2, line 44 on a model 4).
        private readonly int _statusRow;

        // The alternate screen: the model's, or a larger one (the oversize constructor).
        private readonly int _alternateColumns;
        private readonly int _alternateRows;

        private readonly TerminalProfile _profile;

        // The largest file transfer structured field, both ways (Display3270.FileTransferLimit).
        private const int FileTransferBufferSize = 16384;

        // The local ID the Character Sets query reply gives code page 310, the Graphic Escape set
        // (RetroIO Display3270, QueryCode.CharacterSets: "SET 1, flags 0, local ID X'F1'").
        private const byte GraphicEscapeSetId = 0xF1;

        // Not readonly: Reset puts a new one in place, a 3278 just switched on (see Reset).
        private Display3270 _display;

        // True while the connection runs TN3270E (set by the session through ITn3270EAware).
        private bool _inTn3270E;

        // How the last host record went (set by ProcessData, read by the session for TN3270E
        // RESPONSES).
        private HostRecordOutcome _lastRecordOutcome = HostRecordOutcome.Applied;

        // The LU name the TN3270E host gave, shown on line 25; null when there is none.
        private string? _luName;

        // The PC side of IND$FILE (IHostFileTransfer). Fed by the display's X'D0' fields.
        // Not readonly: built again with each new display (Reset).
        private IndFileTransfer _transfer;

        // Where the connection is, for line 25. Set on the session thread by the session.
        private StatusLineConnection _connection = StatusLineConnection.NotConnected;

        /// <summary>
        /// Creates a model 2 (24 x 80) display, with line 25 under it.
        /// </summary>
        public Ibm3270Emulator()
            : this(2)
        {
        }

        /// <summary>
        /// Creates a 3278 of the given model, with its operator information area under it.
        /// </summary>
        /// <remarks>
        /// Every model starts on the default size, 24 by 80, and switches to its alternate size
        /// when the host sends Erase/Write Alternate (GA23-0059-4 p. 3-5; RFC 1576 section 4).
        /// The picture is always as large as the alternate size, with the status row under it;
        /// while the host uses the default size, the cells outside it are drawn blank.
        /// </remarks>
        /// <param name="model">
        /// The model: 2 (24 x 80), 3 (32 x 80), 4 (43 x 80) or 5 (27 x 132).
        /// </param>
        public Ibm3270Emulator(int model)
            : this(model, AlternateColumns(model), AlternateRows(model))
        {
        }

        /// <summary>
        /// Creates a 3278 of the given model whose alternate screen may be larger than the
        /// model's: the "oversize" screen of x3270 (its -oversize option), a size the host learns
        /// from the Query Reply instead of from the model number.
        /// </summary>
        /// <remarks>
        /// <para>
        /// RFC 2355 section 7.1 names the device type for it, IBM-DYNAMIC: "no pre-defined
        /// alternate display size; this value will be passed from the client to host
        /// applications as part of the Query Reply structured field, and it can represent any
        /// display size the client and the host application can support". So a screen larger than
        /// the model's announces <see cref="DynamicTerminalType"/> instead of IBM-3278-n-E
        /// (<see cref="TerminalType"/>), and the Usable Area and Implicit Partition replies carry
        /// the size (RetroIO Display3270). s3270 4.5 does the same: with -oversize 100x50 it
        /// answered TERMINAL-TYPE with IBM-DYNAMIC, and without it IBM-3278-2-E (measured 24
        /// November 2026 with a listener that sent IAC DO TERMINAL-TYPE and SEND).
        /// </para>
        /// <para>
        /// The limits are x3270's, measured on s3270 4.5 the same day: each dimension at least the
        /// model's ("Invalid oversize columns (70): Less than model 2 columns (80)"; 132x20 on a
        /// model 2 refused for its rows), and at most 16383 cells (129x127 taken, 128x128 refused
        /// as "Exceeds protocol limit"). 16383 is the largest 14-bit buffer address plus one: the
        /// implicit partition is "always set to 12/14-bit addressing" (GA23-0059-4 p. 4-4,
        /// 6-88), and 16-bit addresses exist only in a partition made by Create Partition
        /// (p. D-1), which is not built. A size equal to the model's is the model itself and
        /// announces the model's name; what s3270 sends for that case was not measured.
        /// </para>
        /// </remarks>
        /// <param name="model">
        /// The model: 2, 3, 4 or 5. Its default size is still 24 by 80 (GA23-0059-4 p. 3-5).
        /// </param>
        /// <param name="alternateColumns">
        /// Columns of the alternate screen, at least the model's.
        /// </param>
        /// <param name="alternateRows">
        /// Rows of the alternate screen, at least the model's.
        /// </param>
        public Ibm3270Emulator(int model, int alternateColumns, int alternateRows)
            : base(CheckedColumns(model, alternateColumns, alternateRows), alternateRows + 1, maxScrollback: 0)
        {
            Model = model;
            _alternateColumns = alternateColumns;
            _alternateRows = alternateRows;
            _statusRow = alternateRows;
            TerminalType = TerminalTypeFor(model, alternateColumns, alternateRows);
            _profile = new TerminalProfile(
                name: TerminalType,
                primaryDeviceAttributes: Array.Empty<byte>(),
                secondaryDeviceAttributes: Array.Empty<byte>(),
                features: TerminalFeatures.None,
                recognisedPrivateModes: Array.Empty<int>(),
                keyboardLayout: "IBM3270",
                transportEncoding: TransportEncoding.EightBit,
                columns: alternateColumns,
                rows: _statusRow + 1);
            (_display, _transfer) = CreateDisplay();
            Paint();
        }

        /// <summary>
        /// The TERMINAL-TYPE of a screen larger than its model's: "IBM-DYNAMIC" (RFC 2355
        /// section 7.1, and what s3270 4.5 sends with -oversize, measured 24 November 2026).
        /// </summary>
        public const string DynamicTerminalType = "IBM-DYNAMIC";

        /// <summary>
        /// The largest screen, in cells, that 14-bit buffer addresses reach: 16383 (GA23-0059-4
        /// Appendix D). s3270 4.5 refuses 16384 too ("Exceeds protocol limit").
        /// </summary>
        public const int MaximumCells = 0x3FFF;

        /// <summary>
        /// Gets the TERMINAL-TYPE this terminal announces: the model's name, such as
        /// "IBM-3278-4-E", or <see cref="DynamicTerminalType"/> for a larger screen.
        /// </summary>
        public string TerminalType { get; }

        /// <summary>
        /// Gets the columns of the alternate screen, the one Erase/Write Alternate switches to.
        /// </summary>
        public int AlternateScreenColumns => _alternateColumns;

        /// <summary>
        /// Gets the rows of the alternate screen, the one Erase/Write Alternate switches to.
        /// </summary>
        public int AlternateScreenRows => _alternateRows;

        /// <summary>
        /// Gets whether the alternate screen is larger than the model's own.
        /// </summary>
        public bool IsOversize => _alternateColumns != AlternateColumns(Model) || _alternateRows != AlternateRows(Model);

        /// <summary>
        /// Checks an alternate screen size against x3270's limits (see the three-argument
        /// constructor): each dimension at least the model's, and at most
        /// <see cref="MaximumCells"/> cells. The ONE rule: the constructor refuses by it, and the
        /// screen_size argument of terminal_open, CONNECT and CONNSAVE and the Manage
        /// Connections size box are checked by it (through ConnectionFactory, by type name).
        /// </summary>
        /// <param name="model">
        /// The model: 2, 3, 4 or 5.
        /// </param>
        /// <param name="columns">
        /// Columns of the alternate screen.
        /// </param>
        /// <param name="rows">
        /// Rows of the alternate screen.
        /// </param>
        /// <returns>
        /// Null when the size can be built, else what is wrong, naming the limit.
        /// </returns>
        public static string? CheckScreenSize(int model, int columns, int rows)
        {
            int modelColumns = AlternateColumns(model);
            int modelRows = AlternateRows(model);
            if (columns < modelColumns)
            {
                return "a model " + model + " screen has at least " + modelColumns + " columns, got " + columns;
            }

            if (rows < modelRows)
            {
                return "a model " + model + " screen has at least " + modelRows + " rows, got " + rows;
            }

            if ((long)columns * rows > MaximumCells)
            {
                return columns + "x" + rows + " is " + ((long)columns * rows) + " cells, and a screen has at most " + MaximumCells
                    + ": 14-bit buffer addresses reach no further, and 16-bit ones need Create Partition, which is not built (GA23-0059-4 p. D-1)";
            }

            return null;
        }

        /// <summary>
        /// The TERMINAL-TYPE a model with this alternate screen announces: the model's name
        /// (<see cref="TerminalTypeFor(int)"/>) when the screen is the model's own, else
        /// <see cref="DynamicTerminalType"/>. The constructor sets <see cref="TerminalType"/> from
        /// this, and ConnectionFactory builds the TN3270 connection's name from it, so the name
        /// on the wire and the screen the host gets cannot disagree.
        /// </summary>
        /// <param name="model">
        /// The model: 2, 3, 4 or 5.
        /// </param>
        /// <param name="columns">
        /// Columns of the alternate screen.
        /// </param>
        /// <param name="rows">
        /// Rows of the alternate screen.
        /// </param>
        /// <returns>
        /// Such as "IBM-3278-2-E" or "IBM-DYNAMIC".
        /// </returns>
        public static string TerminalTypeFor(int model, int columns, int rows)
        {
            return columns == AlternateColumns(model) && rows == AlternateRows(model)
                ? TerminalTypeFor(model)
                : DynamicTerminalType;
        }

        // Refuses a size outside x3270's limits (CheckScreenSize), naming the limit, and gives
        // back the columns for the base constructor.
        private static int CheckedColumns(int model, int columns, int rows)
        {
            string? problem = CheckScreenSize(model, columns, rows);
            if (problem != null)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), columns + "x" + rows, problem);
            }

            return columns;
        }

        // A 3278 as it is when switched on, wired to this emulator: no fields, no data, the
        // keyboard free, the default size.
        private (Display3270 Display, IndFileTransfer Transfer) CreateDisplay()
        {
            var display = new Display3270(24, 80, _alternateRows, _alternateColumns);
            display.InboundRecord += OnInboundRecord;
            display.ProtocolError += OnProtocolError;
            display.Alarm += OnAlarm;

            // File transfer on: the Query Reply then carries DDM, which is what makes IND$FILE
            // pick DFT mode (docs/spec/IBM3270/IND-FILE-TRANSFER.md sections 1.3 and 5; TK5 uses
            // DFT, measured 22 November 2026). 16384 is the default size of section 9 item 1.
            display.FileTransferLimit = FileTransferBufferSize;
            var transfer = new IndFileTransfer(display);
            transfer.Finished += OnTransferFinished;
            return (display, transfer);
        }

        /// <summary>
        /// Puts the terminal back as it is when switched on: a new 3270 screen model with no
        /// fields and no data, the keyboard not locked, the default size, no TN3270E.
        /// </summary>
        /// <remarks>
        /// MainWindow calls this before it reconnects a tab that keeps its emulator. Until 23
        /// November 2026 there was no override, so only the base buffer was cleared and the 3270
        /// model kept everything from the dropped session: measured by
        /// Tn3270ReconnectTests.AResetBeforeAReconnectUnlocksTheKeyboard, a keyboard left waiting
        /// for the host (Enter sent, no answer before the line went) was still waiting on the new
        /// connection. A new model, rather than clearing its fields one by one, cannot miss one.
        /// What the operator chose is not session state and is carried over: the code page and
        /// the screen settings the host reads from the Query Reply.
        /// </remarks>
        public override void Reset()
        {
            base.Reset();

            Display3270 old = _display;
            old.InboundRecord -= OnInboundRecord;
            old.ProtocolError -= OnProtocolError;
            old.Alarm -= OnAlarm;
            _transfer.Finished -= OnTransferFinished;

            (_display, _transfer) = CreateDisplay();
            _display.CodePage = old.CodePage;
            _display.Color = old.Color;
            _display.CellWidth = old.CellWidth;
            _display.CellHeight = old.CellHeight;
            _display.FileTransferLimit = old.FileTransferLimit;

            _inTn3270E = false;
            _luName = null;
            _lastRecordOutcome = HostRecordOutcome.Applied;
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
        /// The 3278 screen: the model's alternate size, and the status row under it.
        /// </summary>
        public override TerminalProfile Profile => _profile;

        /// <summary>
        /// Gets the 3278 model, 2 to 5.
        /// </summary>
        public int Model { get; }

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
        /// Gets the row the thin rule above line 25 is drawn on top of (IRuledRowTerminal), or -1
        /// when the buffer was cut below line 25 and there is no line 25 to set off.
        /// </summary>
        public int RuleAboveRow => Buffer.Height > _statusRow ? _statusRow : -1;

        /// <summary>
        /// Gets line 25 as drawn (IStatusLineTerminal), built by the same StatusLine3270 call the
        /// screen is painted from.
        /// </summary>
        public string DrawnStatusLine
        {
            get
            {
                Span<char> line = stackalloc char[_alternateColumns];
                StatusLine3270.Build(_display, _connection, _luName, _inTn3270E, line);
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
                bool applied = _display.ProcessOutbound(data);
                _lastRecordOutcome = applied ? HostRecordOutcome.Applied : ErrorKind(data);
                Paint();
                OnInvalidated();
            }
            finally
            {
                EndBatch();
            }
        }

        /// <summary>
        /// Gets how the record given to the last <see cref="ProcessData"/> call went, for a TN3270E
        /// response (RFC 2355 section 10.4.1).
        /// </summary>
        public HostRecordOutcome LastRecordOutcome => _lastRecordOutcome;

        /// <summary>
        /// Says which error a refused record is.
        /// </summary>
        /// <remarks>
        /// GA23-0059-4 Appendix A: "For non-SNA environments, all 3270 data stream errors are
        /// rejected with an Op-Check (OC) except for invalid commands. Unsupported commands are
        /// rejected with Command Reject (CR)." So the one question is whether the command byte is
        /// a command at all. The list is the eight commands Display3270.ProcessOutbound takes
        /// (GA23-0059-4 p. 3-2); a command code it does not take is the only command error it
        /// raises, and every other error it raises comes from inside a known command.
        /// </remarks>
        /// <param name="record">
        /// The refused record, command byte first.
        /// </param>
        /// <returns>
        /// Command Reject for an unknown command, Operation Check for everything else.
        /// </returns>
        private static HostRecordOutcome ErrorKind(ReadOnlySpan<byte> record)
        {
            if (record.Length == 0)
            {
                // ProcessOutbound accepts an empty record, so this is not reached; kept so the
                // method never reads past the end.
                return HostRecordOutcome.OperationCheck;
            }

            switch (record[0])
            {
                case Command3270.Write:
                case Command3270.EraseWrite:
                case Command3270.EraseWriteAlternate:
                case Command3270.EraseAllUnprotected:
                case Command3270.ReadBuffer:
                case Command3270.ReadModified:
                case Command3270.ReadModifiedAll:
                case Command3270.WriteStructuredField:
                    return HostRecordOutcome.OperationCheck;
                default:
                    return HostRecordOutcome.CommandReject;
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
        /// Gets a value indicating whether the connection runs TN3270E, which changes Attn and
        /// Sys Req.
        /// </summary>
        public bool InTn3270E => _inTn3270E;

        /// <summary>
        /// The connection's TN3270E state changed.
        /// </summary>
        /// <param name="on">
        /// True when TN3270E is in force.
        /// </param>
        /// <param name="luName">
        /// The LU name the host gave, or null.
        /// </param>
        public void Tn3270EChanged(bool on, string? luName)
        {
            _inTn3270E = on;
            _luName = on ? luName : null;
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
        public static string? PcKeyToName(string keyName, bool shift, bool control, bool alt)
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
        /// Whether a 3270 key goes to the host (an attention key: Enter, Clear, PA, PF, Attn, Sys Req,
        /// Cursor Select) rather than only changing the screen (GA23-0059-4 p. 3-12, the AID list).
        /// </summary>
        /// <param name="name">
        /// The key name, as in <see cref="KeyNames"/>.
        /// </param>
        /// <returns>
        /// True for an attention key.
        /// </returns>
        public static bool IsAttentionKey(string name)
        {
            return name is "ENTER" or "CLEAR" or "PA1" or "PA2" or "PA3" or "ATTN" or "SYSREQ" or "CURSORSELECT"
                || (name.Length > 2 && name[0] == 'P' && name[1] == 'F');
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

        /// <summary>
        /// Every key name, without an emulator, for the Key Reference window.
        /// </summary>
        public static IReadOnlyList<string> AllKeyNamesForReference => AllKeyNames;

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
                case "BACKSPACE": Local(() => _display.Erase()); return NamedKeyResult.Pressed; // erase left, as wc3270 (Ronny, 23 November 2026)
                case "DELETE": Local(() => _display.Delete()); return NamedKeyResult.Pressed;
                case "INSERT": Local(() => _display.ToggleInsert()); return NamedKeyResult.Pressed;
                case "END": Local(() => _display.FieldEnd()); return NamedKeyResult.Pressed;
                case "ERASEEOF": Local(() => _display.EraseEndOfField()); return NamedKeyResult.Pressed;
                case "ERASEINPUT": Local(() => _display.EraseInput()); return NamedKeyResult.Pressed;
                case "DUP": Local(() => _display.Dup()); return NamedKeyResult.Pressed;
                case "FIELDMARK": Local(() => _display.FieldMark()); return NamedKeyResult.Pressed;
                case "ATTN":
                    Local(() =>
                    {
                        if (_inTn3270E)
                        {
                            // x3270 Attn_action, TN3270E: IAC IP only when an SNA session is
                            // bound, otherwise Minus Function. Basic TN3270E agrees no BIND-IMAGE,
                            // so nothing is ever bound here.
                            _display.RefuseFunction();
                            return;
                        }

                        // x3270 Attn_action in plain TN3270: PA1, then TELNET BREAK.
                        _display.Attn();
                        BreakRequested?.Invoke();
                    });
                    return NamedKeyResult.Pressed;
                case "SYSREQ":
                    // x3270 SysReq_action, TN3270E: net_abort, which sends IAC AO only when the
                    // SYSREQ function was agreed. Basic TN3270E agrees none, so nothing happens.
                    if (!_inTn3270E)
                    {
                        Local(() => _display.SysReq());
                    }

                    return NamedKeyResult.Pressed;
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

        /// <summary>
        /// Gets or sets whether a double click presses Enter (true, the default, as tnz does) or
        /// selects the word (false, as wc3270 and x3270 do).
        /// </summary>
        public bool DoubleClickPressesEnter { get; set; } = true;

        /// <summary>
        /// A mouse click on the screen: the cursor goes to that cell; a double click also presses
        /// Enter (IPointerCursorTerminal, as tnz does).
        /// </summary>
        /// <remarks>
        /// Line 25 and anything below the host's rows is not a buffer address, so a click there is
        /// not taken. While the keyboard is locked Display3270.MoveCursor refuses the move, as it
        /// refuses the arrow keys, and the Enter is refused the same way.
        /// </remarks>
        /// <param name="row">
        /// The row, from 0.
        /// </param>
        /// <param name="column">
        /// The column, from 0.
        /// </param>
        /// <param name="doubleClick">
        /// True for the second click of a double click.
        /// </param>
        /// <returns>
        /// True when the click was on the host's screen.
        /// </returns>
        public bool PointerClick(int row, int column, bool doubleClick)
        {
            int columns = _display.Columns;
            if (row < 0 || column < 0 || column >= columns || row >= _display.Size / columns)
            {
                return false;
            }

            // Double click set to select the word (wc3270, x3270): not taken, so the canvas keeps
            // its word selection and the cursor stays where the first click put it.
            if (doubleClick && !DoubleClickPressesEnter)
            {
                return false;
            }

            int address = (row * columns) + column;
            Local(() =>
            {
                _display.MoveCursor(address);
                if (doubleClick)
                {
                    _display.Attention(Aid.Enter);
                }
            });
            return true;
        }

        /// <summary>
        /// Pastes into the fields as x3270's paste does (Display3270.Paste): a line break goes to
        /// the input field on the next row and never presses Enter (IFieldPaste).
        /// </summary>
        /// <param name="text">
        /// The pasted text.
        /// </param>
        public void PasteIntoFields(string text)
        {
            Local(() => _display.Paste(text));
        }

        /// <summary>
        /// Lists every field for the FIELDS command (IFieldScreen).
        /// </summary>
        /// <remarks>
        /// One line per field, in buffer order:
        ///  - its number from 1, then the row and column of its first character (counted from 1),
        ///    and its length, the positions up to the next field attribute.
        ///  - its kind: protected or input, then numeric, hidden (nondisplay), bright
        ///    (intensified) and modified (MDT set) when they apply (GA23-0059-4 p. 4-13).
        ///  - its extended colour by name (GA23-0061-2 p. 2-10), when the host set one.
        ///  - its text as drawn, in quotes. A hidden field is drawn blank, so its text is never
        ///    given, whatever the host put in it (a password).
        /// Called on the session thread, after Paint, so the drawn text is the current screen.
        /// </remarks>
        /// <returns>
        /// The lines, or "no fields (unformatted screen)".
        /// </returns>
        public string DescribeFields()
        {
            int size = _display.Size;
            int columns = _display.Columns;
            var text = new System.Text.StringBuilder(size + 256);
            int number = 0;
            for (int fa = 0; fa < size; fa++)
            {
                if (!_display.IsFieldAttribute(fa))
                {
                    continue;
                }

                number++;
                byte attribute = _display.GetByte(fa);
                int first = (fa + 1) % size;
                int length = 0;
                var content = new System.Text.StringBuilder(columns);
                for (int a = first; !_display.IsFieldAttribute(a) && length < size; a = (a + 1) % size)
                {
                    content.Append(Buffer.GetCell(a / columns, a % columns).GetString());
                    length++;
                }

                bool hidden = (attribute & FieldAttribute.DisplayMask) == FieldAttribute.NonDisplay;
                text.Append("field ").Append(number)
                    .Append(" row ").Append((first / columns) + 1)
                    .Append(" col ").Append((first % columns) + 1)
                    .Append(" length ").Append(length)
                    .Append(FieldAttribute.IsProtected(attribute) ? " protected" : " input");
                if ((attribute & FieldAttribute.Numeric) != 0)
                {
                    text.Append(" numeric");
                }

                if (hidden)
                {
                    text.Append(" hidden");
                }
                else if ((attribute & FieldAttribute.DisplayMask) == FieldAttribute.Intensified)
                {
                    text.Append(" bright");
                }

                if ((attribute & FieldAttribute.Mdt) != 0)
                {
                    text.Append(" modified");
                }

                string? colour = ColourName(_display.GetFieldColor(fa));
                if (colour != null)
                {
                    text.Append(" colour ").Append(colour);
                }

                text.Append(" \"").Append(content.ToString().TrimEnd()).Append("\"\n");
            }

            return number == 0 ? "no fields (unformatted screen)" : text.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Replaces one input field's text (IFieldScreen): cursor to its first position, Erase
        /// EOF, then the text typed through Display3270.Type, the operator's own path, so the
        /// field's rules (numeric, overflow) apply as they would to a person typing.
        /// </summary>
        /// <param name="number">
        /// The field number from DescribeFields, from 1.
        /// </param>
        /// <param name="text">
        /// The text.
        /// </param>
        /// <returns>
        /// Null when done, or the reason it was refused.
        /// </returns>
        public string? FillField(int number, string text)
        {
            if (_display.Lock != KeyboardLock.None)
            {
                return "the keyboard is locked (" + KeyboardLockReason + ")";
            }

            int size = _display.Size;
            int found = 0;
            for (int fa = 0; fa < size; fa++)
            {
                if (!_display.IsFieldAttribute(fa) || ++found != number)
                {
                    continue;
                }

                if (FieldAttribute.IsProtected(_display.GetByte(fa)))
                {
                    return "field " + number + " is protected";
                }

                int first = (fa + 1) % size;
                int length = 0;
                for (int a = first; !_display.IsFieldAttribute(a) && length < size; a = (a + 1) % size)
                {
                    length++;
                }

                if (text.Length > length)
                {
                    return "field " + number + " holds " + length + " characters, the text has " + text.Length;
                }

                _display.MoveCursor(first);
                _display.EraseEndOfField();
                for (int i = 0; i < text.Length; i++)
                {
                    if (!_display.Type(text[i]))
                    {
                        return "the field refused '" + text[i] + "' (keyboard now " + (KeyboardLockReason ?? "unlocked") + ")";
                    }
                }

                Refresh();
                return null;
            }

            return "there is no field " + number + " (the screen has " + found + ")";
        }

        /// <summary>
        /// Moves the cursor to a cell (IFieldScreen), refused while the keyboard is locked.
        /// </summary>
        /// <param name="row">
        /// The row, from 1.
        /// </param>
        /// <param name="column">
        /// The column, from 1.
        /// </param>
        /// <returns>
        /// Null when moved, or the reason it was refused.
        /// </returns>
        public string? MoveCursorTo(int row, int column)
        {
            int columns = _display.Columns;
            int rows = _display.Size / columns;
            if (row < 1 || row > rows || column < 1 || column > columns)
            {
                return "row " + row + " column " + column + " is not on the " + rows + " by " + columns + " screen";
            }

            if (_display.Lock != KeyboardLock.None)
            {
                return "the keyboard is locked (" + KeyboardLockReason + ")";
            }

            _display.MoveCursor(((row - 1) * columns) + column - 1);
            Refresh();
            return null;
        }

        /// <summary>
        /// The base code pages a connection can be set to, as their IBM numbers in RetroIO's
        /// order ("37", "273", "277", ...): the words the script and MCP <c>codepage</c> argument
        /// accepts (ConnectionFactory.Ibm3270CodePageNumbers reads this by name).
        /// </summary>
        /// <remarks>
        /// Made from CodePage.All, the same list the Manage Connections code page box is filled
        /// from, so the window and the commands can never offer different pages. Code page 310 is
        /// not in it: it is the Graphic Escape set beside every base page, never one itself.
        /// </remarks>
        /// <returns>
        /// One number per base page.
        /// </returns>
        public static string[] CodePageNumbers()
        {
            IReadOnlyList<CodePage> all = CodePage.All;
            var numbers = new string[all.Count];
            for (int i = 0; i < numbers.Length; i++)
            {
                numbers[i] = all[i].Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return numbers;
        }

        /// <summary>
        /// The TERMINAL-TYPE a model announces: "IBM-3278-" and the model, with "-E" for the
        /// extended data stream, as x3270 builds it (telnet.c, the Asprintf of "IBM-%s%s").
        /// </summary>
        /// <param name="model">
        /// The model, 2 to 5.
        /// </param>
        /// <returns>
        /// For example "IBM-3278-4-E".
        /// </returns>
        public static string TerminalTypeFor(int model)
        {
            AlternateRows(model);
            return "IBM-3278-" + model.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-E";
        }

        /// <summary>
        /// A model's alternate screen, the one an Erase/Write Alternate switches to, as columns x
        /// rows: such as "132x27" for model 5.
        /// </summary>
        /// <param name="model">
        /// 2, 3, 4 or 5.
        /// </param>
        /// <returns>
        /// The size, written the way a connection stores it.
        /// </returns>
        public static string AlternateScreenSize(int model)
        {
            return AlternateColumns(model).ToString(System.Globalization.CultureInfo.InvariantCulture) + "x"
                + AlternateRows(model).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // The alternate screen sizes (RFC 1576 section 4; x3270 include\3270ds.h, MODEL_n_ROWS).
        private static int AlternateRows(int model) => model switch
        {
            2 => 24,
            3 => 32,
            4 => 43,
            5 => 27,
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "a 3278 is model 2, 3, 4 or 5"),
        };

        private static int AlternateColumns(int model) => model == 5 ? 132 : AlternateRows(model) > 0 ? 80 : 0;

        // The extended colour values (GA23-0061-2 p. 2-10); X'00' is the default, so no name.
        private static string? ColourName(byte colour) => colour switch
        {
            0xF1 => "blue",
            0xF2 => "red",
            0xF3 => "pink",
            0xF4 => "green",
            0xF5 => "turquoise",
            0xF6 => "yellow",
            0xF7 => "neutral",
            0x00 => null,
            _ => "X'" + colour.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) + "'",
        };

        /// <summary>
        /// Raised when an IND$FILE transfer ends (IHostFileTransfer), on the session thread.
        /// </summary>
        public event HostFileTransferFinished? HostFileTransferFinished;

        /// <summary>
        /// Arms an IND$FILE transfer and types its command into the input field under the cursor,
        /// then presses Enter, as x3270 does (IND-FILE-TRANSFER.md section 2.5).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The field is emptied first: x3270 clears it, and TSO would otherwise read whatever was
        /// left after the command. Refused when the keyboard is locked, when the cursor is on a
        /// protected field, or when the command does not fit: a command cut off at the field's
        /// end would run on the host with a different meaning (section 2.5).
        /// </para>
        /// <para>
        /// On a screen without fields (TSO after Clear) the input runs from the cursor to the
        /// end of the screen.
        /// </para>
        /// </remarks>
        /// <param name="upload">
        /// True for IND$FILE PUT (PC to host), false for GET.
        /// </param>
        /// <param name="file">
        /// The PC file's stream. Not closed here.
        /// </param>
        /// <param name="command">
        /// The whole command, for example "IND$FILE PUT HELLO.TEXT ASCII CRLF".
        /// </param>
        /// <returns>
        /// Null when the command was sent, or the reason nothing was started.
        /// </returns>
        public string? StartHostFileTransfer(bool upload, System.IO.Stream file, string command)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(command);
            if (_transfer.State != FileTransferState.Idle)
            {
                return "a file transfer is already " + _transfer.State;
            }

            if (_display.Lock != KeyboardLock.None)
            {
                return "the keyboard is locked (" + KeyboardLockReason + ")";
            }

            // The input field under the cursor: from after its attribute to the next attribute.
            int size = _display.Size;
            int cursor = _display.CursorAddress;
            int attribute = -1;
            for (int i = 0; i < size; i++)
            {
                int a = (cursor - i + size) % size;
                if (_display.IsFieldAttribute(a))
                {
                    attribute = a;
                    break;
                }
            }

            int first;
            int length;
            if (attribute < 0)
            {
                first = cursor;
                length = size - cursor;
            }
            else
            {
                if (attribute == cursor || FieldAttribute.IsProtected(_display.GetByte(attribute)))
                {
                    return "the cursor is not in an input field";
                }

                first = (attribute + 1) % size;
                length = 0;
                for (int a = first; !_display.IsFieldAttribute(a) && length < size; a = (a + 1) % size)
                {
                    length++;
                }
            }

            if (command.Length > length)
            {
                return "the command has " + command.Length + " characters and the input field holds " + length;
            }

            _display.MoveCursor(first);
            _display.EraseEndOfField();
            for (int i = 0; i < command.Length; i++)
            {
                if (!_display.Type(command[i]))
                {
                    Refresh();
                    return "the field refused '" + command[i] + "' (keyboard now " + (KeyboardLockReason ?? "unlocked") + ")";
                }
            }

            if (upload)
            {
                _transfer.ExpectUpload(file);
            }
            else
            {
                _transfer.ExpectDownload(file);
            }

            _display.Attention(Aid.Enter);
            Refresh();
            return null;
        }

        /// <summary>
        /// Gives up on a transfer the host has not opened yet (IHostFileTransfer). One the host
        /// has opened ends with the host's message, which is the only way IND$FILE ends (section
        /// 3.13).
        /// </summary>
        /// <inheritdoc/>
        int ICodePageTerminal.CodePage => _display.CodePage.Number;

        /// <inheritdoc/>
        IReadOnlyList<string> ICodePageTerminal.CodePages
        {
            get
            {
                IReadOnlyList<CodePage> all = CodePage.All;
                var names = new List<string>(all.Count);
                for (int i = 0; i < all.Count; i++)
                {
                    names.Add(all[i].Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + all[i].Name);
                }

                return names;
            }
        }

        /// <inheritdoc/>
        string? ICodePageTerminal.SetCodePage(int number)
        {
            // Code page 310 is the Graphic Escape set beside every base page, never a base page
            // itself (Display3270.CodePage refuses it), and CodePage.All holds base pages only.
            if (number == CodePage.Cp310.Number || !CodePage.TryGet(number, out CodePage page))
            {
                return "no base code page " + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; CODEPAGE with no number lists them";
            }

            _display.CodePage = page;
            Refresh();
            return null;
        }

        public void CancelHostFileTransfer()
        {
            if (_transfer.State == FileTransferState.Armed)
            {
                _transfer.Disarm();
                HostFileTransferFinished?.Invoke(false, "cancelled before the host started the transfer");
            }
        }

        private void OnTransferFinished(bool success, string message)
        {
            HostFileTransferFinished?.Invoke(success, message);
        }

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
            int rows = _display.Rows;
            int width = _alternateColumns;
            var blank = new TerminalCell(' ', CharacterAttributes.None, NeutralWhite, NeutralBlack);
            for (int row = 0; row < _statusRow; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    // Outside the size the host is using (the default 24 x 80 on a larger model).
                    Buffer.SetCell(row, column, row < rows && column < columns ? CellFor((row * columns) + column) : blank);
                }
            }

            PaintStatusLine(width);

            int cursor = _display.CursorAddress;
            Cursor.MoveTo(cursor / columns, cursor % columns);
        }

        // Line 25. wc3270 draws the "4" and the box in reverse video and the "A" underlined
        // (wc3270\screen.c, draw_oia); the rest is plain. A buffer cut below 25 rows (a resize
        // from outside) has no line 25 to draw.
        private void PaintStatusLine(int columns)
        {
            if (Buffer.Height <= _statusRow)
            {
                return;
            }

            Span<char> line = stackalloc char[columns];
            StatusLine3270.Build(_display, _connection, _luName, _inTn3270E, line);
            for (int column = 0; column < columns; column++)
            {
                CharacterAttributes flags = column switch
                {
                    StatusLine3270.ReadyColumn => CharacterAttributes.Reverse,
                    StatusLine3270.BoxColumn => CharacterAttributes.Reverse,
                    StatusLine3270.OnlineColumn => CharacterAttributes.Underline,
                    _ => CharacterAttributes.None,
                };
                Buffer.SetCell(_statusRow, column, new TerminalCell(line[column], flags, NeutralWhite, NeutralBlack));
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
                return new TerminalCell(' ', flags, foreground, NeutralBlack);
            }

            // Nondisplay fields show nothing (p. 4-13) - a password field.
            if ((attribute & FieldAttribute.DisplayMask) == FieldAttribute.NonDisplay)
            {
                return new TerminalCell(' ', flags, foreground, NeutralBlack);
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

            // A character the host sent behind a Graphic Escape order is from code page 310 (APL,
            // box pieces; GA23-0059-4 p. 4-9), every other one from the screen's own base page.
            // Whether the font has a glyph for it is a separate question (docs\PLAN.md, the font item).
            // The same set is also reached WITHOUT Graphic Escape, by the character set attribute
            // (type X'43', SA or SFE) naming local ID X'F1', which our Character Sets query reply
            // gives code page 310 (Display3270, QueryCode.CharacterSets). The character attribute
            // wins over the field's, X'00' meaning "take the field's" (GA23-0059-4 p. 4-19). TK5's
            // M.7 TERMTEST writes its APL sample line and its symbol set 1 table this way
            // (SA 43 F1, measured 24 November 2026); until then only GE was looked at here.
            byte charset = _display.GetCharacterCharacterSet(address);
            if (charset == 0 && fa >= 0)
            {
                charset = _display.GetFieldCharacterSet(fa);
            }

            byte code = _display.GetByte(address);
            bool alternate = _display.IsGraphicEscape(address) || charset == GraphicEscapeSetId;
            char c = alternate ? CodePage.Cp310.ToUnicode(code) : _display.CodePage.ToUnicode(code);
            if (c == '\0')
            {
                c = ' ';
            }

            return new TerminalCell(c, flags, foreground, NeutralBlack);
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
            // wc3270\screen.c, field_colors: green, red, blue, neutral white.
            if (fa < 0)
            {
                return Green;
            }

            bool isProtected = FieldAttribute.IsProtected(attribute);
            bool intensified = (attribute & FieldAttribute.DisplayMask) == FieldAttribute.Intensified;
            if (isProtected)
            {
                return intensified ? NeutralWhite : Blue;
            }

            return intensified ? Red : Green;
        }

        /// <summary>
        /// Extended colour values X'F1'-X'F7' (GA23-0059-4 p. 4-18): blue, red, pink, green,
        /// turquoise, yellow, white. X'F0' and X'00' mean default.
        /// </summary>
        private static TerminalColor ExtendedColour(byte value, TerminalColor fallback)
        {
            switch (value)
            {
                case 0xF1: return Blue;
                case 0xF2: return Red;
                case 0xF3: return Pink;
                case 0xF4: return Green;
                case 0xF5: return Turquoise;
                case 0xF6: return Yellow;
                case 0xF7: return NeutralWhite;
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
