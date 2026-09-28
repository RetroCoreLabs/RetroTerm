using System;

namespace RetroTerm.Core.Protocols;

/// <summary>
/// Factory for creating protocol connections based on connection parameters
/// Removes connection creation logic from UI layer
/// </summary>
public static class ConnectionFactory
{
    /// <summary>
    /// Connection protocol types
    /// </summary>
    public enum ProtocolType
    {
        Telnet,
        SSH,
        TN3270,
        Serial,
        Gateway,
        OpcomSimulator
    }

    /// <summary>
    /// The line-ending choices, spelled once so the stored connection, the dialog and any
    /// command surface cannot disagree about how they are written.
    /// </summary>
    /// <remarks>
    /// Plain strings rather than an enum because that is how the neighbouring settings on a
    /// connection are already stored - Language and the screen-size mode - and a stored file
    /// written by an older build must keep reading correctly.
    /// </remarks>
    public static class NewLineModes
    {
        /// <summary>
        /// Carriage return alone. What SINTRAN and the TDV terminals expect.
        /// </summary>
        public const string Cr = "CR";

        /// <summary>
        /// Carriage return followed by line feed.
        /// </summary>
        public const string CrLf = "CR+LF";

        /// <summary>
        /// Line feed alone.
        /// </summary>
        public const string Lf = "LF";

        /// <summary>
        /// Leave it to the terminal. Receive only, and the default.
        /// </summary>
        public const string Auto = "AUTO";

        /// <summary>
        /// The choices offered for INCOMING line endings, in dropdown order.
        /// </summary>
        public static string[] ReceiveChoices { get; } = { Auto, Cr, CrLf, Lf };

        /// <summary>
        /// The choices offered for the Enter key, in dropdown order. No AUTO: something
        /// definite has to go on the wire.
        /// </summary>
        public static string[] TransmitChoices { get; } = { Cr, CrLf, Lf };

        /// <summary>
        /// The bytes the Enter key sends for a transmit choice.
        /// </summary>
        /// <param name="mode">
        /// One of <see cref="TransmitChoices"/>. Anything unrecognised is treated as CR, which
        /// is what this program sent before the setting existed.
        /// </param>
        /// <returns>
        /// The bytes to put on the wire.
        /// </returns>
        public static byte[] TransmitBytes(string? mode)
        {
            if (string.Equals(mode, CrLf, StringComparison.OrdinalIgnoreCase)) return new byte[] { 0x0D, 0x0A };
            if (string.Equals(mode, Lf, StringComparison.OrdinalIgnoreCase)) return new byte[] { 0x0A };
            return new byte[] { 0x0D };
        }
    }

    /// <summary>
    /// Connection parameters for factory
    /// </summary>
    public class ConnectionParameters
    {
        public ProtocolType Protocol { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }

        // SSH-specific
        public string? Username { get; set; }
        public string? Password { get; set; }

        // Serial-specific
        public string? PortName { get; set; }
        public int BaudRate { get; set; } = 9600;
        public int DataBits { get; set; } = 8;
        public int StopBitsValue { get; set; } = 1;    // 0=None, 1=One, 2=Two, 3=OnePointFive
        public int ParityValue { get; set; } = 0;      // 0=None, 1=Odd, 2=Even, 3=Mark, 4=Space
        public int HandshakeValue { get; set; } = 0;    // 0=None, 1=XOnXOff, 2=RtsCts, 3=Both

        // Gateway-specific
        public int? GatewayIdentCode { get; set; }
        public bool GatewaySelectFirstFree { get; set; }

        // Terminal emulator settings
        public string EmulatorType { get; set; } = "VT100";
        public int Width { get; set; } = 80;
        public int Height { get; set; } = 24;

        /// <summary>
        /// Whether the window decides how many columns and rows there are, or null to let the
        /// terminal's own profile decide.
        /// </summary>
        /// <remarks>
        /// Null is the normal value and means "whatever this terminal should do": a VT or an xterm
        /// follows the window, an ND or TDV keeps its hardware geometry and is zoomed instead. That
        /// split is not decided here - it is <c>TerminalFeatures.HostResize</c> in
        /// <c>RetroTerm.Core</c>, which already draws the same line for the same reason. Named in
        /// plain text rather than linked because this project deliberately references nothing, so a
        /// cref to Core would be a link that never resolves.
        /// Set it to override for one connection. False with <see cref="Width"/> 132 is how a TDV
        /// gets SINTRAN's 132-column mode, which matters because we do not yet know whether SINTRAN
        /// asks for it with an escape sequence or expects the terminal to be set up that way.
        /// </remarks>
        public bool? FollowWindowSize { get; set; }

        // Per-connection terminal colors (hex strings)
        public string? ForegroundColor { get; set; }
        public string? BackgroundColor { get; set; }

        /// <summary>
        /// Whether this connection draws as a single-phosphor screen - the sixteen ANSI colours
        /// collapsed onto one hue, arriving as brightnesses the way a one-gun CRT showed them.
        ///
        /// Carried alongside the colours rather than derived from them, because it cannot be
        /// derived: "Amber" and "Amber (single phosphor)" store identical foreground and background
        /// hex, and this flag is the only thing that separates them.
        /// </summary>
        public bool SinglePhosphor { get; set; }

        // Terminal language for character set selection
        public string Language { get; set; } = "Norwegian";

        /// <summary>
        /// Whether the Backspace key sends DEL (0x7F) rather than BS (0x08), or null to use the
        /// terminal's own default (DEL on a TDV, BS on a VT) - the same null-means-default shape
        /// as <see cref="FollowWindowSize"/>, for the same reason.
        /// </summary>
        public bool? BackspaceSendsDel { get; set; }

        /// <summary>
        /// Whether the Delete key sends DEL (0x7F) rather than its terminal's own sequence.
        /// </summary>
        public bool DeleteSendsDel { get; set; }

        /// <summary>
        /// How an incoming line ending is treated: CR, CR+LF, LF or AUTO.
        /// </summary>
        public string NewLineReceive { get; set; } = NewLineModes.Auto;

        /// <summary>
        /// What the Enter key puts on the wire: CR, CR+LF or LF.
        /// </summary>
        public string NewLineTransmit { get; set; } = NewLineModes.Cr;

        /// <summary>
        /// Whether typed characters are echoed locally, for a host that does not echo.
        /// </summary>
        public bool LocalEcho { get; set; }

        /// <summary>
        /// Smallest gap in milliseconds between two consecutive bytes sent. 0 = full speed.
        /// </summary>
        public int TransmitDelayPerCharMs { get; set; }

        /// <summary>
        /// Extra pause in milliseconds after a line ending is sent. 0 = none.
        /// </summary>
        public int TransmitDelayPerLineMs { get; set; }

        // Event hook scripts (script library names): run after connect / on a DROPPED
        // connection. Carried here so the UI layer can fire them without re-loading
        // the stored configuration.
        public string? OnConnectScript { get; set; }
        public string? OnDisconnectScript { get; set; }

        /// <summary>
        /// Gets a human-readable display name for this connection (e.g., "myhost:23", "COM3 @ 9600bps").
        /// </summary>
        public string DisplayName => Protocol switch
        {
            ProtocolType.Serial => $"{PortName ?? "?"} @ {BaudRate}bps",
            ProtocolType.OpcomSimulator => "OPCOM Simulator",
            ProtocolType.Gateway => $"Gateway identCode {GatewayIdentCode}",
            _ => $"{Host}:{Port}"
        };
    }

    /// <summary>
    /// Parses a protocol string (from HostConfiguration) to ProtocolType enum.
    /// Single source of truth for protocol name → enum mapping.
    /// </summary>
    public static ProtocolType ParseProtocolType(string protocol)
    {
        if (string.Equals(protocol, "SSH", StringComparison.OrdinalIgnoreCase))
            return ProtocolType.SSH;
        if (string.Equals(protocol, "Serial", StringComparison.OrdinalIgnoreCase))
            return ProtocolType.Serial;
        if (string.Equals(protocol, "Gateway", StringComparison.OrdinalIgnoreCase))
            return ProtocolType.Gateway;
        if (string.Equals(protocol, "OpcomSimulator", StringComparison.OrdinalIgnoreCase))
            return ProtocolType.OpcomSimulator;
        if (string.Equals(protocol, "TN3270", StringComparison.OrdinalIgnoreCase))
            return ProtocolType.TN3270;
        return ProtocolType.Telnet;
    }

    /// <summary>
    /// Parses a parity name from a command or MCP field into the stored parity value.
    /// </summary>
    /// <param name="text">
    /// The parity as typed: none, odd, even, mark or space, or the single letters N, O, E, M, S.
    /// Case does not matter.
    /// </param>
    /// <param name="parityValue">
    /// Receives the value <see cref="ConnectionParameters.ParityValue"/> stores
    /// (0=None, 1=Odd, 2=Even, 3=Mark, 4=Space).
    /// </param>
    /// <returns>
    /// True when the text named a parity.
    /// </returns>
    public static bool TryParseParity(string? text, out int parityValue)
    {
        parityValue = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text!.Trim();
        if (Matches(t, "none", 'N')) { parityValue = 0; return true; }
        if (Matches(t, "odd", 'O')) { parityValue = 1; return true; }
        if (Matches(t, "even", 'E')) { parityValue = 2; return true; }
        if (Matches(t, "mark", 'M')) { parityValue = 3; return true; }
        if (Matches(t, "space", 'S')) { parityValue = 4; return true; }
        return false;

        static bool Matches(string t, string word, char letter)
            => string.Equals(t, word, StringComparison.OrdinalIgnoreCase)
               || (t.Length == 1 && (t[0] == letter || t[0] == char.ToLowerInvariant(letter)));
    }

    /// <summary>
    /// Parses a stop-bits count from a command or MCP field into the stored stop-bits value.
    /// </summary>
    /// <param name="text">
    /// The count as typed: 1, 1.5 or 2. It travels as text because 1.5 is not an integer.
    /// </param>
    /// <param name="stopBitsValue">
    /// Receives the value <see cref="ConnectionParameters.StopBitsValue"/> stores
    /// (1=One, 2=Two, 3=OnePointFive).
    /// </param>
    /// <returns>
    /// True when the text named a stop-bits count.
    /// </returns>
    public static bool TryParseStopBits(string? text, out int stopBitsValue)
    {
        stopBitsValue = 1;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text!.Trim();
        if (t == "1") { stopBitsValue = 1; return true; }
        if (t == "2") { stopBitsValue = 2; return true; }
        if (t == "1.5" || t == "1,5") { stopBitsValue = 3; return true; }
        return false;
    }

    /// <summary>
    /// The usual three-character framing shorthand for a serial line, e.g. 8N1 or 7E1.
    /// </summary>
    /// <param name="dataBits">
    /// Data bits per character.
    /// </param>
    /// <param name="parityValue">
    /// The stored parity value (0=None, 1=Odd, 2=Even, 3=Mark, 4=Space).
    /// </param>
    /// <param name="stopBitsValue">
    /// The stored stop-bits value (1=One, 2=Two, 3=OnePointFive).
    /// </param>
    /// <returns>
    /// The shorthand, with 1.5 stop bits spelled out as "1.5".
    /// </returns>
    public static string DescribeFraming(int dataBits, int parityValue, int stopBitsValue)
    {
        char parity = parityValue switch { 1 => 'O', 2 => 'E', 3 => 'M', 4 => 'S', _ => 'N' };
        string stop = stopBitsValue switch { 2 => "2", 3 => "1.5", _ => "1" };
        return dataBits.ToString() + parity + stop;
    }

    /// <summary>
    /// Validates the serial fields every command surface shares (baud, data_bits, parity,
    /// stop_bits) and turns the two text fields into their stored values.
    /// </summary>
    /// <param name="baud">
    /// Bits per second; must be positive.
    /// </param>
    /// <param name="dataBits">
    /// Data bits per character; 5 to 8.
    /// </param>
    /// <param name="parity">
    /// Parity as typed, or null to keep <paramref name="parityValue"/>'s incoming value.
    /// </param>
    /// <param name="stopBits">
    /// Stop bits as typed, or null to keep <paramref name="stopBitsValue"/>'s incoming value.
    /// </param>
    /// <param name="parityValue">
    /// On entry the value to keep when <paramref name="parity"/> is null; on a successful
    /// return the value to store.
    /// </param>
    /// <param name="stopBitsValue">
    /// On entry the value to keep when <paramref name="stopBits"/> is null; on a successful
    /// return the value to store.
    /// </param>
    /// <returns>
    /// Null when everything is valid, or one sentence naming the offending field by the name
    /// the caller typed (baud, data_bits, parity, stop_bits) — the same names on the script
    /// DSL and over MCP, which is what lets this live in one place.
    /// </returns>
    public static string? ValidateSerialFields(int baud, int dataBits, string? parity, string? stopBits,
        ref int parityValue, ref int stopBitsValue)
    {
        if (baud <= 0)
        {
            return $"baud must be positive, not {baud}";
        }
        if (dataBits < 5 || dataBits > 8)
        {
            return $"data_bits must be 5 to 8, not {dataBits}";
        }
        if (parity != null)
        {
            if (!TryParseParity(parity, out var parsedParity))
            {
                return $"parity '{parity}' is not one of none, odd, even, mark, space";
            }
            parityValue = parsedParity;
        }
        if (stopBits != null)
        {
            if (!TryParseStopBits(stopBits, out var parsedStop))
            {
                return $"stop_bits '{stopBits}' is not one of 1, 1.5, 2";
            }
            stopBitsValue = parsedStop;
        }
        return null;
    }

    /// <summary>
    /// Splits an address a person typed into a host and, if they gave one, a port.
    /// </summary>
    /// <param name="text">
    /// What was typed. Leading and trailing spaces are ignored.
    /// </param>
    /// <param name="host">
    /// Receives the host part, or an empty string when nothing usable was typed.
    /// </param>
    /// <param name="port">
    /// Receives the port when one was given, or -1 when it was not.
    /// </param>
    /// <returns>
    /// True when a host was found.
    /// </returns>
    /// <remarks>
    /// <para><b>Here rather than in the dialog, because a colon is not simple</b></para>
    /// Typing the port after the host is the fastest way to say it, and the obvious split on the
    /// first colon is wrong for IPv6 - the address itself is full of them. The rules are:
    ///  - bracketed, as in [::1]:23, means the brackets hold the host and anything after the
    ///    closing bracket is the port. This is the only unambiguous way to write it.
    ///  - exactly one colon with digits after it is a host and a port.
    ///  - anything else, including a bare ::1, is all host. A guess here would silently connect to
    ///    the wrong machine, which is worse than making somebody use brackets.
    /// </remarks>
    public static bool SplitHostAndPort(string? text, out string host, out int port)
    {
        host = string.Empty;
        port = -1;

        if (string.IsNullOrWhiteSpace(text)) return false;
        string trimmed = text!.Trim();

        // Bracketed IPv6, the only form that can carry a port without ambiguity.
        if (trimmed.Length > 0 && trimmed[0] == '[')
        {
            int close = trimmed.IndexOf(']');
            if (close < 0) return false;

            host = trimmed.Substring(1, close - 1);
            string after = trimmed.Substring(close + 1);
            if (after.Length > 1 && after[0] == ':' && AllDigits(after, 1))
            {
                port = int.Parse(after.Substring(1));
            }

            return host.Length > 0;
        }

        int colon = trimmed.IndexOf(':');
        bool oneColonOnly = colon >= 0 && trimmed.IndexOf(':', colon + 1) < 0;

        if (oneColonOnly && colon > 0 && colon < trimmed.Length - 1 && AllDigits(trimmed, colon + 1))
        {
            host = trimmed.Substring(0, colon);
            port = int.Parse(trimmed.Substring(colon + 1));
            return true;
        }

        host = trimmed;
        return host.Length > 0;
    }

    /// <summary>
    /// Whether every character from an index onwards is a digit.
    /// </summary>
    /// <param name="text">
    /// The text to look at.
    /// </param>
    /// <param name="start">
    /// Where to start looking.
    /// </param>
    /// <returns>
    /// True when there is at least one character and all of them are digits.
    /// </returns>
    private static bool AllDigits(string text, int start)
    {
        if (start >= text.Length) return false;

        for (int i = start; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') return false;
        }

        return true;
    }

    /// <summary>
    /// The port a protocol uses when nobody says otherwise.
    /// </summary>
    /// <param name="protocol">
    /// The protocol being connected with.
    /// </param>
    /// <returns>
    /// The well-known port, or 23 for anything with no port of its own.
    /// </returns>
    /// <remarks>
    /// <para><b>Here rather than in the dialog that needed it</b></para>
    /// Quick Connect fills the port box in when the protocol changes, and it is the only caller
    /// today. It still belongs on the factory: the moment a second surface wants to be helpful in
    /// the same way, the choice is already made in one place instead of being typed out twice with
    /// a chance of disagreeing.
    /// <para><b>The CONNECT command deliberately does NOT use this</b></para>
    /// Its <c>port</c> parameter has documented default 23 for every protocol, and scripts written
    /// against that would change meaning under them if this were wired in. A prefilled box that a
    /// person can see and edit is a different thing from a default a script cannot see.
    /// </remarks>
    public static int DefaultPortFor(ProtocolType protocol)
    {
        switch (protocol)
        {
            case ProtocolType.SSH: return 22;
            case ProtocolType.TN3270: return 23;
            default: return 23;
        }
    }

    /// <summary>
    /// Create a connection based on parameters
    /// </summary>
    public static IConnection CreateConnection(ConnectionParameters parameters)
    {
        // Validate parameters
        if (parameters.Protocol == ProtocolType.Serial)
        {
            if (string.IsNullOrWhiteSpace(parameters.PortName))
            {
                throw new ArgumentException("PortName cannot be empty for serial connections", nameof(parameters));
            }
        }
        else if (parameters.Protocol == ProtocolType.Gateway)
        {
            if (parameters.GatewayIdentCode == null)
            {
                throw new ArgumentException("GatewayIdentCode must be set for gateway connections", nameof(parameters));
            }
        }
        else if (parameters.Protocol == ProtocolType.OpcomSimulator)
        {
            // No validation needed for simulator
        }
        else
        {
            if (string.IsNullOrWhiteSpace(parameters.Host))
            {
                throw new ArgumentException("Host cannot be empty", nameof(parameters));
            }

            if (parameters.Port <= 0)
            {
                throw new ArgumentException("Port must be positive", nameof(parameters));
            }
        }

        // Create connection based on protocol type
        // Note: We use reflection to avoid compile-time dependency on RetroTerm.Core.Protocols.Net
        // This keeps the factory in the base protocol layer
        return parameters.Protocol switch
        {
            ProtocolType.Telnet => CreateTelnetConnection(parameters.Host, parameters.Port, parameters.EmulatorType),
            ProtocolType.SSH => CreateSSHConnection(parameters.Host, parameters.Port, parameters.Username, parameters.Password, parameters.EmulatorType),
            ProtocolType.TN3270 => throw new NotImplementedException("TN3270 not yet implemented"),
            ProtocolType.Serial => CreateSerialConnection(parameters),
            ProtocolType.Gateway => throw new InvalidOperationException("Gateway connections are created directly via GatewayConnection, not through the factory."),
            ProtocolType.OpcomSimulator => CreateOpcomSimulatorConnection(),
            _ => throw new ArgumentException($"Unknown protocol type: {parameters.Protocol}", nameof(parameters))
        };
    }

    private static IConnection CreateTelnetConnection(string host, int port, string emulatorType = "VT100")
    {
        // Use reflection to create TelnetConnection without compile-time dependency
        var telnetType = Type.GetType("RetroTerm.Core.Protocols.Net.TelnetConnection, RetroTerm.Core.Protocols.Net");
        if (telnetType == null)
        {
            throw new InvalidOperationException("TelnetConnection type not found. Ensure RetroTerm.Core.Protocols.Net is loaded.");
        }

        var connection = Activator.CreateInstance(telnetType, host, port) as IConnection;
        if (connection == null)
        {
            throw new InvalidOperationException("Failed to create TelnetConnection instance");
        }

        // Set TerminalType property if it exists
        var terminalTypeProperty = telnetType.GetProperty("TerminalType");
        if (terminalTypeProperty != null && terminalTypeProperty.CanWrite)
        {
            terminalTypeProperty.SetValue(connection, WireTerminalType(emulatorType));
        }

        return connection;
    }

    /// <summary>
    /// The name a host is told this terminal is, for TERMINAL-TYPE negotiation and the SSH
    /// pty request.
    /// </summary>
    /// <remarks>
    /// <para><b>Why it is not simply the emulator's name</b></para>
    /// The name becomes TERM on the far side and picks the terminfo entry there, so it has to be
    /// one the host recognises. The emulator names in this application are upper case; the xterm
    /// terminfo entries are lower case and hyphenated, and a host looking up "XTERM-256COLOR" finds
    /// nothing.
    ///
    /// <para><b>The case difference between the two protocols is deliberate</b></para>
    /// RFC 1091 has the telnet terminal type sent in upper case, and Unix telnetd lower-cases it on
    /// arrival; SSH has no such convention, so the name goes as-is. The xterm names are the
    /// exception either way, because that is how they are spelled in terminfo.
    ///
    /// <para><b>A third list, and why it is here</b></para>
    /// This duplicates names that also live in EmulatorFactory. It cannot ask that class: this
    /// assembly deliberately does not reference the emulation layer, which is what lets a protocol
    /// be used without one. Anything not named here goes out unchanged.
    /// </remarks>
    /// <param name="emulatorType">
    /// The emulator type as this application names it.
    /// </param>
    /// <returns>
    /// The name to put on the wire.
    /// </returns>
    private static string WireTerminalType(string emulatorType)
    {
        return emulatorType switch
        {
            "XTERM" => "xterm",
            "XTERM-256COLOR" => "xterm-256color",
            _ => emulatorType,
        };
    }

    private static IConnection CreateSerialConnection(ConnectionParameters parameters)
    {
        // Use reflection to create SerialConnection without compile-time dependency
        var serialType = Type.GetType("RetroTerm.Core.Protocols.Net.SerialConnection, RetroTerm.Core.Protocols.Net");
        if (serialType == null)
        {
            throw new InvalidOperationException("SerialConnection type not found. Ensure RetroTerm.Core.Protocols.Net is loaded.");
        }

        var connection = Activator.CreateInstance(serialType,
            parameters.PortName,
            parameters.BaudRate,
            parameters.DataBits,
            parameters.StopBitsValue,
            parameters.ParityValue,
            parameters.HandshakeValue) as IConnection;

        if (connection == null)
        {
            throw new InvalidOperationException("Failed to create SerialConnection instance");
        }

        // Transmit pacing, set after construction rather than added to the constructor: this
        // assembly reaches SerialConnection by reflection, so every extra constructor argument is
        // a silent break if the two sides ever disagree about the order. Properties are matched by
        // NAME, and a rename fails loudly here instead of passing the baud rate as the data bits.
        SetOptionalInt(serialType, connection, "TransmitDelayPerCharMs", parameters.TransmitDelayPerCharMs);
        SetOptionalInt(serialType, connection, "TransmitDelayPerLineMs", parameters.TransmitDelayPerLineMs);

        return connection;
    }

    /// <summary>
    /// Sets a writable int property on a reflected connection, when it has one.
    /// </summary>
    /// <param name="type">
    /// The connection's type.
    /// </param>
    /// <param name="connection">
    /// The instance to set it on.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <param name="value">
    /// The value to set. Zero is skipped, so an older connection type without the property is not
    /// treated as a failure when there was nothing to apply anyway.
    /// </param>
    /// <remarks>
    /// A missing property with a NON-zero value to apply is worth failing on: the caller asked for
    /// pacing and would otherwise get a silently unpaced line, which is the kind of quiet
    /// disagreement this reflection boundary exists to make loud.
    /// </remarks>
    private static void SetOptionalInt(Type type, object connection, string name, int value)
    {
        if (value == 0) return;

        var property = type.GetProperty(name);
        if (property == null || !property.CanWrite)
        {
            throw new InvalidOperationException(
                $"{type.Name} has no writable {name}, so the requested value {value} would be "
                + "silently ignored.");
        }

        property.SetValue(connection, value);
    }

    /// <summary>
    /// <see cref="SetOptionalInt"/>, for tests, without needing a real connection instance.
    /// </summary>
    /// <param name="type">
    /// The type to look the property up on.
    /// </param>
    /// <param name="instance">
    /// The object to set it on.
    /// </param>
    /// <param name="name">
    /// The property name.
    /// </param>
    /// <param name="value">
    /// The value to apply.
    /// </param>
    public static void SetOptionalIntForTesting(Type type, object instance, string name, int value)
        => SetOptionalInt(type, instance, name, value);

    private static IConnection CreateOpcomSimulatorConnection()
    {
        var simType = Type.GetType("RetroTerm.Core.Protocols.Net.OpcomSimulatorConnection, RetroTerm.Core.Protocols.Net");
        if (simType == null)
        {
            throw new InvalidOperationException("OpcomSimulatorConnection type not found. Ensure RetroTerm.Core.Protocols.Net is loaded.");
        }

        var connection = Activator.CreateInstance(simType) as IConnection;
        if (connection == null)
        {
            throw new InvalidOperationException("Failed to create OpcomSimulatorConnection instance");
        }

        return connection;
    }

    private static IConnection CreateSSHConnection(string host, int port, string? username, string? password, string emulatorType = "VT100")
    {
        // Use reflection to create SSHConnection without compile-time dependency
        var sshType = Type.GetType("RetroTerm.Core.Protocols.Net.SSHConnection, RetroTerm.Core.Protocols.Net");
        if (sshType == null)
        {
            throw new InvalidOperationException("SSHConnection type not found. Ensure RetroTerm.Core.Protocols.Net is loaded.");
        }

        var connection = Activator.CreateInstance(sshType, host, port, username, password) as IConnection;
        if (connection == null)
        {
            throw new InvalidOperationException("Failed to create SSHConnection instance");
        }

        // Set TerminalType property if it exists
        var terminalTypeProperty = sshType.GetProperty("TerminalType");
        if (terminalTypeProperty != null && terminalTypeProperty.CanWrite)
        {
            // Lower case over SSH - see WireTerminalType.
            terminalTypeProperty.SetValue(connection, WireTerminalType(emulatorType).ToLowerInvariant());
        }

        return connection;
    }
}

