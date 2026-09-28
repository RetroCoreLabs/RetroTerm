using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Configuration;

/// <summary>
/// Represents a saved host configuration with all connection and emulator parameters
/// </summary>
public class HostConfiguration
{
    /// <summary>
    /// Unique identifier for this configuration
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for this configuration
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Host address or hostname
    /// </summary>
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// Port number
    /// </summary>
    public int Port { get; set; } = 23;

    /// <summary>
    /// Connection protocol (Telnet, SSH, etc.)
    /// </summary>
    public string Protocol { get; set; } = "Telnet";

    /// <summary>
    /// Username for authentication (SSH only)
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authentication (SSH only)
    /// </summary>
    [JsonIgnore] // Don't serialize passwords for security
    public string? Password { get; set; }

    /// <summary>
    /// Terminal emulator type
    /// </summary>
    public string EmulatorType { get; set; } = "VT100";

    /// <summary>
    /// Terminal width in columns
    /// </summary>
    public int Width { get; set; } = 80;

    /// <summary>
    /// Terminal height in rows
    /// </summary>
    public int Height { get; set; } = 24;

    /// <summary>
    /// Whether the window decides how many columns and rows there are, or null to let the
    /// terminal's own profile decide.
    /// </summary>
    /// <remarks>
    /// Null is the normal value: a VT or an xterm follows the window, an ND or TDV keeps the
    /// geometry its hardware had and is zoomed instead. Set it to override that for one saved
    /// connection - false with a width of 132 is how a TDV gets SINTRAN's 132-column mode.
    /// </remarks>
    public bool? FollowWindowSize { get; set; }

    /// <summary>
    /// Maximum scrollback lines
    /// </summary>
    public int MaxScrollback { get; set; } = 1000;

    /// <summary>
    /// Additional emulator-specific settings
    /// </summary>
    public Dictionary<string, object> EmulatorSettings { get; set; } = new();

    /// <summary>
    /// Additional connection-specific settings
    /// </summary>
    public Dictionary<string, object> ConnectionSettings { get; set; } = new();

    /// <summary>
    /// When this configuration was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this configuration was last used
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Number of times this configuration has been used
    /// </summary>
    public int UseCount { get; set; } = 0;

    /// <summary>
    /// Whether this configuration is marked as favorite
    /// </summary>
    public bool IsFavorite { get; set; } = false;

    /// <summary>
    /// Tags for categorizing configurations
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Notes about this configuration
    /// </summary>
    public string? Notes { get; set; }

    // Per-connection terminal colors (hex strings like "#00FF88")
    public string? ForegroundColor { get; set; }
    public string? BackgroundColor { get; set; }

    /// <summary>
    /// Whether this connection draws as a SINGLE-PHOSPHOR screen: the sixteen ANSI colours
    /// collapsed onto one hue, arriving as sixteen brightnesses the way a one-gun CRT had no
    /// choice but to show them.
    ///
    /// A SEPARATE FIELD RATHER THAN SOMETHING INFERRED FROM THE COLOURS, because it cannot be
    /// inferred: "Amber" and "Amber (single phosphor)" carry byte-for-byte identical foreground
    /// and background hex. The only thing that separates them is this flag.
    ///
    /// Defaults to false, so every connection saved before this existed keeps drawing exactly as
    /// it did. That is what makes reading an old file safe without a migration step.
    /// </summary>
    public bool SinglePhosphor { get; set; }

    // Terminal language for character set selection
    public string Language { get; set; } = "Norwegian";

    // Event hook scripts (names from the script library, %AppData%\RetroTerm\scripts).
    // OnConnectScript runs against the session right after this connection is
    // established (auto-login); OnDisconnectScript runs when the connection DROPS
    // (remote close/failure — not on a deliberate disconnect), e.g. to reconnect.
    public string? OnConnectScript { get; set; }
    public string? OnDisconnectScript { get; set; }

    // Gateway-specific properties
    public int? GatewayIdentCode { get; set; }
    public string? GatewayTerminalName { get; set; }
    public bool GatewaySelectFirstFree { get; set; }

    /// <summary>
    /// Whether the Backspace key sends DEL (0x7F) instead of BS (0x08).
    /// </summary>
    /// <remarks>
    /// The same choice TeraTerm offers as "Transmit DEL by: Backspace key". Which one a host wants
    /// is not guessable - it is a property of the host's line discipline, not of the keyboard - so
    /// it lives on the connection, where a SINTRAN line and a Unix login can disagree.
    /// Null means the connection has not chosen and the terminal's own default applies: DEL on
    /// a TDV, BS on a VT (TerminalProfile.BackspaceSendsDel). Until 27 September 2026 this was a
    /// plain bool defaulting to false, so every TDV connection sent BS unless somebody found the
    /// checkbox; files written before then hold false for every connection and are migrated to
    /// null once by ConfigurationManager (see SettingsVersion).
    /// </remarks>
    public bool? BackspaceSendsDel { get; set; }

    /// <summary>
    /// Which set of defaults this record was last saved under.
    /// </summary>
    /// <remarks>
    /// The numbers:
    ///  - 0 is anything written before 27 September 2026.
    ///  - 1 means the Backspace choice is null when unset, and a fixed-screen terminal has been given its own row count.
    /// ConfigurationManager.MigrateSettings raises it on load, and every save writes the current number.
    /// </remarks>
    public int SettingsVersion { get; set; }

    /// <summary>
    /// Whether the Delete key sends DEL (0x7F) instead of its escape sequence.
    /// </summary>
    /// <remarks>
    /// TeraTerm's "Transmit DEL by: Delete key". Defaults to false, so a VT-style Delete keeps
    /// sending the sequence its terminal defines.
    /// </remarks>
    public bool DeleteSendsDel { get; set; }

    /// <summary>
    /// How an incoming line ending is treated: CR, CR+LF, LF, or AUTO.
    /// </summary>
    /// <remarks>
    /// AUTO is the default and means "do what the terminal already does", which is the behaviour
    /// every existing connection has. The other three force one reading, for a host that sends a
    /// bare CR where the terminal expects both, or the reverse - the case where a listing prints
    /// down the right-hand edge or overwrites itself on one line.
    /// </remarks>
    public string NewLineReceive { get; set; } = "AUTO";

    /// <summary>
    /// What the Enter key puts on the wire: CR, CR+LF, or LF.
    /// </summary>
    /// <remarks>
    /// CR is the default and is what SINTRAN and the TDV terminals expect. A host that wants LF
    /// alone, or both, is told here rather than by changing what the key means everywhere.
    /// </remarks>
    public string NewLineTransmit { get; set; } = "CR";

    /// <summary>
    /// Whether typed characters are echoed to this terminal locally.
    /// </summary>
    /// <remarks>
    /// For a host that does not echo. Defaults to false: a host that DOES echo would otherwise
    /// show every character twice, which is the more common case here.
    /// </remarks>
    public bool LocalEcho { get; set; }

    /// <summary>
    /// Smallest gap, in milliseconds, between two consecutive bytes sent to this connection.
    /// Zero means send at full speed, which is what this program has always done.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a terminal ever needs to slow down</b></para>
    /// A real terminal is paced by a person's fingers; this one sends a whole line as one burst
    /// with no gap at all. Hardware that polls its UART in software, rather than taking an
    /// interrupt per character, can miss bytes at that rate - the character is overwritten before
    /// the poll comes round again. The ND-120's emulated period UART showed exactly this, and it
    /// is the reason a "li-fi" typed by a script arrived as "HF".
    /// <para><b>Per connection, not global</b></para>
    /// It belongs to the far end. One machine needing 20 ms a character is no reason to throttle
    /// a telnet session in the next tab, and both are normally open at once.
    /// </remarks>
    public int TransmitDelayPerCharMs { get; set; }

    /// <summary>
    /// Extra pause, in milliseconds, after a line ending is sent. Zero means no extra pause.
    /// </summary>
    /// <remarks>
    /// Added ON TOP of the per-character delay, and applied after a CR or an LF. This is the one
    /// that matters for a host which does real work when a line arrives - echoing a prompt,
    /// opening a file - and drops whatever is sent while it is busy.
    /// </remarks>
    public int TransmitDelayPerLineMs { get; set; }

    // Serial-specific properties
    public string? PortName { get; set; }
    public int BaudRate { get; set; } = 9600;
    public int DataBits { get; set; } = 8;
    public int StopBitsValue { get; set; } = 1;
    public int ParityValue { get; set; } = 0;
    public int HandshakeValue { get; set; } = 0;

    /// <summary>
    /// Creates a copy of this configuration with a new ID
    /// </summary>
    public HostConfiguration Clone()
    {
        return new HostConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{Name} (Copy)",
            Host = Host,
            Port = Port,
            Protocol = Protocol,
            Username = Username,
            Password = Password,
            EmulatorType = EmulatorType,
            Width = Width,
            Height = Height,
            MaxScrollback = MaxScrollback,
            EmulatorSettings = new Dictionary<string, object>(EmulatorSettings),
            ConnectionSettings = new Dictionary<string, object>(ConnectionSettings),
            CreatedAt = DateTime.UtcNow,
            LastUsedAt = DateTime.UtcNow,
            UseCount = 0,
            IsFavorite = IsFavorite,
            Tags = new List<string>(Tags),
            Notes = Notes,
            ForegroundColor = ForegroundColor,
            BackgroundColor = BackgroundColor,
            SinglePhosphor = SinglePhosphor,
            Language = Language,
            GatewayIdentCode = GatewayIdentCode,
            GatewayTerminalName = GatewayTerminalName,
            GatewaySelectFirstFree = GatewaySelectFirstFree,
            BackspaceSendsDel = BackspaceSendsDel,
            DeleteSendsDel = DeleteSendsDel,
            NewLineReceive = NewLineReceive,
            NewLineTransmit = NewLineTransmit,
            LocalEcho = LocalEcho,
            TransmitDelayPerCharMs = TransmitDelayPerCharMs,
            TransmitDelayPerLineMs = TransmitDelayPerLineMs,
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            StopBitsValue = StopBitsValue,
            ParityValue = ParityValue,
            HandshakeValue = HandshakeValue
        };
    }

    /// <summary>
    /// Converts this HostConfiguration to ConnectionParameters for the ConnectionFactory.
    /// Single source of truth for HostConfiguration → ConnectionParameters mapping.
    /// </summary>
    public Protocols.ConnectionFactory.ConnectionParameters ToConnectionParameters()
    {
        return new Protocols.ConnectionFactory.ConnectionParameters
        {
            Protocol = Protocols.ConnectionFactory.ParseProtocolType(Protocol),
            Host = Host,
            Port = Port,
            Username = string.IsNullOrWhiteSpace(Username) ? null : Username,
            Password = string.IsNullOrWhiteSpace(Password) ? null : Password,
            EmulatorType = EmulatorType ?? "VT100",
            Width = Width,
            Height = Height,
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            StopBitsValue = StopBitsValue,
            ParityValue = ParityValue,
            HandshakeValue = HandshakeValue,
            GatewayIdentCode = GatewayIdentCode,
            GatewaySelectFirstFree = GatewaySelectFirstFree,
            ForegroundColor = string.IsNullOrWhiteSpace(ForegroundColor) ? null : ForegroundColor,
            BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor) ? null : BackgroundColor,
            Language = Language,
            BackspaceSendsDel = BackspaceSendsDel,
            DeleteSendsDel = DeleteSendsDel,
            NewLineReceive = string.IsNullOrWhiteSpace(NewLineReceive) ? "AUTO" : NewLineReceive,
            NewLineTransmit = string.IsNullOrWhiteSpace(NewLineTransmit) ? "CR" : NewLineTransmit,
            LocalEcho = LocalEcho,
            // Negative is meaningless and would throw inside Task.Delay - clamp rather than trust
            // a hand-edited file.
            TransmitDelayPerCharMs = TransmitDelayPerCharMs < 0 ? 0 : TransmitDelayPerCharMs,
            TransmitDelayPerLineMs = TransmitDelayPerLineMs < 0 ? 0 : TransmitDelayPerLineMs,
            OnConnectScript = string.IsNullOrWhiteSpace(OnConnectScript) ? null : OnConnectScript,
            OnDisconnectScript = string.IsNullOrWhiteSpace(OnDisconnectScript) ? null : OnDisconnectScript
        };
    }

    /// <summary>
    /// Updates the last used timestamp and increments use count
    /// </summary>
    public void MarkAsUsed()
    {
        LastUsedAt = DateTime.UtcNow;
        UseCount++;
    }

    /// <summary>
    /// Gets a display string for this configuration
    /// </summary>
    public override string ToString()
    {
        return $"{Name} ({ToConnectionParameters().DisplayName} via {Protocol})";
    }
}
