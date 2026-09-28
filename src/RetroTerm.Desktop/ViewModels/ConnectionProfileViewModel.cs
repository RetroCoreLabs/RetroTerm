using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Desktop.ViewModels;

/// <summary>
/// ViewModel for a single connection profile. Wraps all HostConfiguration fields
/// with change notification and dirty tracking via snapshot comparison.
/// </summary>
public class ConnectionProfileViewModel : INotifyPropertyChanged
{
    // Snapshot for dirty tracking
    private string _snapshotName = "";
    private string _snapshotProtocol = "Telnet";
    private string _snapshotHost = "localhost";
    private int _snapshotPort = 23;
    private string _snapshotUsername = "";
    private string _snapshotPassword = "";
    private string _snapshotEmulatorType = "VT100";
    private string _snapshotSize = "80x24";
    private string _snapshotPortName = "";
    private int _snapshotBaudRate = 9600;
    private int _snapshotDataBits = 8;
    private int _snapshotStopBitsValue = 1;
    private int _snapshotParityValue;
    private int _snapshotHandshakeValue;
    private int? _snapshotGatewayIdentCode;
    private string? _snapshotGatewayTerminalName;
    private bool _snapshotGatewaySelectFirstFree;
    private string _snapshotForegroundColor = "";
    private string _snapshotBackgroundColor = "";
    private bool _snapshotSinglePhosphor;
    private bool? _snapshotFollowWindowSize;
    private string _snapshotLanguage = "Norwegian";
    private string? _snapshotOnConnectScript;
    private string? _snapshotOnDisconnectScript;
    private bool? _snapshotBackspaceSendsDel;
    private bool _snapshotDeleteSendsDel;
    private string _snapshotNewLineReceive = ConnectionFactory.NewLineModes.Auto;
    private string _snapshotNewLineTransmit = ConnectionFactory.NewLineModes.Cr;
    private bool _snapshotLocalEcho;
    private int _snapshotTransmitDelayPerCharMs;
    private int _snapshotTransmitDelayPerLineMs;

    // Backing fields
    private string _name = "";
    private string _protocol = "Telnet";
    private string _host = "localhost";
    private int _port = 23;
    private string _username = "";
    private string _password = "";
    private string _emulatorType = "VT100";
    private string _size = "80x24";
    private string _portName = "";
    private int _baudRate = 9600;
    private int _dataBits = 8;
    private int _stopBitsValue = 1;
    private int _parityValue;
    private int _handshakeValue;
    private int? _gatewayIdentCode;
    private string? _gatewayTerminalName;
    private bool _gatewaySelectFirstFree;
    private string _foregroundColor = "";
    private string _backgroundColor = "";
    private bool _singlePhosphor;
    private bool? _followWindowSize;
    private string _language = "Norwegian";
    // Connection event hooks: library script names run on connect / on dropped line.
    private string? _onConnectScript;
    private string? _onDisconnectScript;
    // Keyboard tab: what the keys send and how line endings are handled.
    private bool? _backspaceSendsDel;
    private bool _deleteSendsDel;
    private string _newLineReceive = ConnectionFactory.NewLineModes.Auto;
    private string _newLineTransmit = ConnectionFactory.NewLineModes.Cr;
    private bool _localEcho;
    private int _transmitDelayPerCharMs;
    private int _transmitDelayPerLineMs;

    private bool _hasSnapshot;
    private string? _configId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(CanSave)); } }
    }

    public string Protocol
    {
        get => _protocol;
        set { if (_protocol != value) { _protocol = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string Host
    {
        get => _host;
        set { if (_host != value) { _host = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int Port
    {
        get => _port;
        set { if (_port != value) { _port = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string Username
    {
        get => _username;
        set { if (_username != value) { _username = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string Password
    {
        get => _password;
        set { if (_password != value) { _password = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// The terminal type. Choosing one also sets <see cref="Size"/> to that terminal's own screen.
    /// </summary>
    /// <remarks>
    /// The size is part of what the terminal is. A TDV2200 picked in the dialog used to keep
    /// whatever size the previous choice had, which was 80 by 24 for a new profile, and so lost its
    /// 25th row. Loading a saved profile does not go through this setter, so a saved size stays.
    /// </remarks>
    public string EmulatorType
    {
        get => _emulatorType;
        set
        {
            if (_emulatorType != value)
            {
                _emulatorType = value;
                var (width, height) = EmulatorFactory.GetRecommendedSize(value);
                Size = $"{width}x{height}";
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    public string Size
    {
        get => _size;
        set { if (_size != value) { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string PortName
    {
        get => _portName;
        set { if (_portName != value) { _portName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int BaudRate
    {
        get => _baudRate;
        set { if (_baudRate != value) { _baudRate = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int DataBits
    {
        get => _dataBits;
        set { if (_dataBits != value) { _dataBits = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int StopBitsValue
    {
        get => _stopBitsValue;
        set { if (_stopBitsValue != value) { _stopBitsValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int ParityValue
    {
        get => _parityValue;
        set { if (_parityValue != value) { _parityValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int HandshakeValue
    {
        get => _handshakeValue;
        set { if (_handshakeValue != value) { _handshakeValue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public int? GatewayIdentCode
    {
        get => _gatewayIdentCode;
        set { if (_gatewayIdentCode != value) { _gatewayIdentCode = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string? GatewayTerminalName
    {
        get => _gatewayTerminalName;
        set { if (_gatewayTerminalName != value) { _gatewayTerminalName = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public bool GatewaySelectFirstFree
    {
        get => _gatewaySelectFirstFree;
        set { if (_gatewaySelectFirstFree != value) { _gatewaySelectFirstFree = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string ForegroundColor
    {
        get => _foregroundColor;
        set { if (_foregroundColor != value) { _foregroundColor = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    public string BackgroundColor
    {
        get => _backgroundColor;
        set { if (_backgroundColor != value) { _backgroundColor = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Whether this connection draws as a single-phosphor screen. Carried separately from the
    /// colours because it cannot be inferred from them: the two Amber presets store identical hex.
    /// </summary>
    public bool SinglePhosphor
    {
        get => _singlePhosphor;
        set { if (_singlePhosphor != value) { _singlePhosphor = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// How this connection decides its screen size, as the three words the dropdown shows.
    /// </summary>
    /// <remarks>
    /// Three states rather than a checkbox, because "use whatever this terminal should do" is the
    /// normal answer and is not the same as either of the other two. The default comes from the
    /// terminal's profile - see TerminalCanvas.FollowWindowSize - so a TDV holds its hardware
    /// geometry and a VT follows the window without anyone choosing.
    /// </remarks>
    public string ScreenSizeMode
    {
        get => _followWindowSize switch
        {
            true => FollowWindowLabel,
            false => FixedSizeLabel,
            _ => DefaultSizeLabel,
        };
        set
        {
            bool? wanted = value switch
            {
                FollowWindowLabel => true,
                FixedSizeLabel => false,
                _ => null,
            };

            if (_followWindowSize == wanted) return;

            _followWindowSize = wanted;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    /// <summary>
    /// The dropdown's choices, in order.
    /// </summary>
    public static string[] ScreenSizeModes { get; } =
        { DefaultSizeLabel, FollowWindowLabel, FixedSizeLabel };

    /// <summary>
    /// Let the terminal's own profile decide.
    /// </summary>
    public const string DefaultSizeLabel = "Default for terminal";

    /// <summary>
    /// The window decides how many columns and rows there are.
    /// </summary>
    public const string FollowWindowLabel = "Follow window";

    /// <summary>
    /// Keep the size below, and scale the picture to the window.
    /// </summary>
    public const string FixedSizeLabel = "Fixed size";

    public string Language
    {
        get => _language;
        set { if (_language != value) { _language = value ?? "Norwegian"; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Whether Backspace sends DEL (0x7F) instead of BS (0x08); null leaves it to the terminal.
    /// </summary>
    public bool? BackspaceSendsDel
    {
        get => _backspaceSendsDel;
        set { if (_backspaceSendsDel != value) { _backspaceSendsDel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Whether Delete sends DEL (0x7F) instead of its terminal's own sequence.
    /// </summary>
    public bool DeleteSendsDel
    {
        get => _deleteSendsDel;
        set { if (_deleteSendsDel != value) { _deleteSendsDel = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// How an incoming line ending is treated: AUTO, CR, CR+LF or LF.
    /// </summary>
    public string NewLineReceive
    {
        get => _newLineReceive;
        set { if (_newLineReceive != value) { _newLineReceive = value ?? ConnectionFactory.NewLineModes.Auto; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// What the Enter key puts on the wire: CR, CR+LF or LF.
    /// </summary>
    public string NewLineTransmit
    {
        get => _newLineTransmit;
        set { if (_newLineTransmit != value) { _newLineTransmit = value ?? ConnectionFactory.NewLineModes.Cr; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Whether typed characters are echoed locally, for a host that does not echo.
    /// </summary>
    public bool LocalEcho
    {
        get => _localEcho;
        set { if (_localEcho != value) { _localEcho = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Smallest gap in milliseconds between two bytes sent. 0 sends at full speed.
    /// </summary>
    /// <remarks>
    /// Negatives are clamped to zero rather than rejected: the box takes typed text, and a stray
    /// minus sign should mean "no delay" rather than throw inside Task.Delay later.
    /// </remarks>
    public int TransmitDelayPerCharMs
    {
        get => _transmitDelayPerCharMs;
        set
        {
            int wanted = value < 0 ? 0 : value;
            if (_transmitDelayPerCharMs != wanted)
            {
                _transmitDelayPerCharMs = wanted;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    /// <summary>
    /// Extra pause in milliseconds after a line ending is sent. 0 adds nothing.
    /// </summary>
    public int TransmitDelayPerLineMs
    {
        get => _transmitDelayPerLineMs;
        set
        {
            int wanted = value < 0 ? 0 : value;
            if (_transmitDelayPerLineMs != wanted)
            {
                _transmitDelayPerLineMs = wanted;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDirty));
            }
        }
    }

    /// <summary>
    /// Library script run right after this connection is established (auto-login). Null = none.
    /// </summary>
    public string? OnConnectScript
    {
        get => _onConnectScript;
        set { if (_onConnectScript != value) { _onConnectScript = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// Library script run when the connection DROPS (never on deliberate disconnect). Null = none.
    /// </summary>
    public string? OnDisconnectScript
    {
        get => _onDisconnectScript;
        set { if (_onDisconnectScript != value) { _onDisconnectScript = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDirty)); } }
    }

    /// <summary>
    /// True when no snapshot exists (fresh/new profile).
    /// </summary>
    public bool IsNew => !_hasSnapshot;

    /// <summary>
    /// True when current values differ from the snapshot.
    /// </summary>
    public bool IsDirty
    {
        get
        {
            if (!_hasSnapshot) return !string.IsNullOrWhiteSpace(_name);

            return _name != _snapshotName
                || _protocol != _snapshotProtocol
                || _host != _snapshotHost
                || _port != _snapshotPort
                || _username != _snapshotUsername
                || _password != _snapshotPassword
                || _emulatorType != _snapshotEmulatorType
                || _size != _snapshotSize
                || _portName != _snapshotPortName
                || _baudRate != _snapshotBaudRate
                || _dataBits != _snapshotDataBits
                || _stopBitsValue != _snapshotStopBitsValue
                || _parityValue != _snapshotParityValue
                || _handshakeValue != _snapshotHandshakeValue
                || _gatewayIdentCode != _snapshotGatewayIdentCode
                || _gatewayTerminalName != _snapshotGatewayTerminalName
                || _gatewaySelectFirstFree != _snapshotGatewaySelectFirstFree
                || _foregroundColor != _snapshotForegroundColor
                || _backgroundColor != _snapshotBackgroundColor
                || _singlePhosphor != _snapshotSinglePhosphor
                || _followWindowSize != _snapshotFollowWindowSize
                || _language != _snapshotLanguage
                || _onConnectScript != _snapshotOnConnectScript
                || _onDisconnectScript != _snapshotOnDisconnectScript
                || _backspaceSendsDel != _snapshotBackspaceSendsDel
                || _deleteSendsDel != _snapshotDeleteSendsDel
                || _newLineReceive != _snapshotNewLineReceive
                || _newLineTransmit != _snapshotNewLineTransmit
                || _localEcho != _snapshotLocalEcho
                || _transmitDelayPerCharMs != _snapshotTransmitDelayPerCharMs
                || _transmitDelayPerLineMs != _snapshotTransmitDelayPerLineMs;
        }
    }

    /// <summary>
    /// True when there are dirty changes and the name is not empty.
    /// </summary>
    public bool CanSave => IsDirty && !string.IsNullOrWhiteSpace(_name);

    /// <summary>
    /// The configuration ID (null for new profiles).
    /// </summary>
    public string? ConfigId => _configId;

    /// <summary>
    /// Loads values from a HostConfiguration and takes a snapshot.
    /// </summary>
    public void LoadFromConfiguration(HostConfiguration config)
    {
        _configId = config.Id;

        _name = config.Name;
        _protocol = config.Protocol;
        _host = config.Host;
        _port = config.Port;
        _username = config.Username ?? "";
        _password = config.Password ?? "";
        _emulatorType = config.EmulatorType ?? "VT100";
        _size = $"{config.Width}x{config.Height}";
        _portName = config.PortName ?? "";
        _baudRate = config.BaudRate;
        _dataBits = config.DataBits;
        _stopBitsValue = config.StopBitsValue;
        _parityValue = config.ParityValue;
        _handshakeValue = config.HandshakeValue;
        _gatewayIdentCode = config.GatewayIdentCode;
        _gatewayTerminalName = config.GatewayTerminalName;
        _gatewaySelectFirstFree = config.GatewaySelectFirstFree;
        _foregroundColor = config.ForegroundColor ?? "";
        _backgroundColor = config.BackgroundColor ?? "";
        _singlePhosphor = config.SinglePhosphor;
        _followWindowSize = config.FollowWindowSize;
        _language = config.Language ?? "Norwegian";
        _onConnectScript = config.OnConnectScript;
        _onDisconnectScript = config.OnDisconnectScript;
        _backspaceSendsDel = config.BackspaceSendsDel;
        _deleteSendsDel = config.DeleteSendsDel;
        // A file written before these existed has null here, and the fallbacks are the
        // behaviour that build had - so an old connection opens behaving exactly as it did.
        _newLineReceive = string.IsNullOrWhiteSpace(config.NewLineReceive)
            ? ConnectionFactory.NewLineModes.Auto : config.NewLineReceive;
        _newLineTransmit = string.IsNullOrWhiteSpace(config.NewLineTransmit)
            ? ConnectionFactory.NewLineModes.Cr : config.NewLineTransmit;
        _localEcho = config.LocalEcho;
        _transmitDelayPerCharMs = config.TransmitDelayPerCharMs;
        _transmitDelayPerLineMs = config.TransmitDelayPerLineMs;

        TakeSnapshot();
        NotifyAllChanged();
    }

    /// <summary>
    /// Resets to default values for a new profile (no snapshot).
    /// </summary>
    public void LoadDefaults()
    {
        _configId = null;
        _hasSnapshot = false;

        _name = "";
        _protocol = "Telnet";
        _host = "localhost";
        _port = 23;
        _username = "";
        _password = "";
        _emulatorType = "VT100";
        _size = "80x24";
        _portName = "";
        _baudRate = 9600;
        _dataBits = 8;
        _stopBitsValue = 1;
        _parityValue = 0;
        _handshakeValue = 0;
        _gatewayIdentCode = null;
        _gatewayTerminalName = null;
        _gatewaySelectFirstFree = false;
        _foregroundColor = "";
        _backgroundColor = "";
        _singlePhosphor = false;
        _followWindowSize = null;
        _language = "Norwegian";
        _onConnectScript = null;
        _onDisconnectScript = null;

        NotifyAllChanged();
    }

    /// <summary>
    /// Builds a HostConfiguration from the current values for saving.
    /// </summary>
    public HostConfiguration ToHostConfiguration()
    {
        ParseSize(out int width, out int height);

        return new HostConfiguration
        {
            Id = _configId ?? Guid.NewGuid().ToString(),
            Name = _name,
            Protocol = _protocol,
            Host = _host,
            Port = _port,
            Username = string.IsNullOrWhiteSpace(_username) ? null : _username,
            Password = string.IsNullOrWhiteSpace(_password) ? null : _password,
            EmulatorType = _emulatorType,
            Width = width,
            Height = height,
            PortName = string.IsNullOrWhiteSpace(_portName) ? null : _portName,
            BaudRate = _baudRate,
            DataBits = _dataBits,
            StopBitsValue = _stopBitsValue,
            ParityValue = _parityValue,
            HandshakeValue = _handshakeValue,
            ForegroundColor = string.IsNullOrWhiteSpace(_foregroundColor) ? null : _foregroundColor,
            BackgroundColor = string.IsNullOrWhiteSpace(_backgroundColor) ? null : _backgroundColor,
            SinglePhosphor = _singlePhosphor,
            FollowWindowSize = _followWindowSize,
            Language = _language,
            BackspaceSendsDel = _backspaceSendsDel,
            DeleteSendsDel = _deleteSendsDel,
            NewLineReceive = _newLineReceive,
            NewLineTransmit = _newLineTransmit,
            LocalEcho = _localEcho,
            TransmitDelayPerCharMs = _transmitDelayPerCharMs,
            TransmitDelayPerLineMs = _transmitDelayPerLineMs,
            GatewayIdentCode = _gatewayIdentCode,
            GatewayTerminalName = _gatewayTerminalName,
            GatewaySelectFirstFree = _gatewaySelectFirstFree,
            OnConnectScript = string.IsNullOrWhiteSpace(_onConnectScript) ? null : _onConnectScript,
            OnDisconnectScript = string.IsNullOrWhiteSpace(_onDisconnectScript) ? null : _onDisconnectScript
        };
    }

    /// <summary>
    /// Builds ConnectionParameters from the current form values for connecting.
    /// </summary>
    public ConnectionFactory.ConnectionParameters ToConnectionParameters()
    {
        ParseSize(out int width, out int height);

        return new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ParseProtocolType(_protocol),
            Host = _host,
            Port = _port,
            Username = string.IsNullOrWhiteSpace(_username) ? null : _username,
            Password = string.IsNullOrWhiteSpace(_password) ? null : _password,
            EmulatorType = _emulatorType,
            Width = width,
            Height = height,
            PortName = _portName,
            BaudRate = _baudRate,
            DataBits = _dataBits,
            StopBitsValue = _stopBitsValue,
            ParityValue = _parityValue,
            HandshakeValue = _handshakeValue,
            GatewayIdentCode = _gatewayIdentCode,
            GatewaySelectFirstFree = _gatewaySelectFirstFree,
            ForegroundColor = string.IsNullOrWhiteSpace(_foregroundColor) ? null : _foregroundColor,
            BackgroundColor = string.IsNullOrWhiteSpace(_backgroundColor) ? null : _backgroundColor,
            SinglePhosphor = _singlePhosphor,
            FollowWindowSize = _followWindowSize,
            Language = _language,
            BackspaceSendsDel = _backspaceSendsDel,
            DeleteSendsDel = _deleteSendsDel,
            NewLineReceive = _newLineReceive,
            NewLineTransmit = _newLineTransmit,
            LocalEcho = _localEcho,
            TransmitDelayPerCharMs = _transmitDelayPerCharMs,
            TransmitDelayPerLineMs = _transmitDelayPerLineMs,
            OnConnectScript = string.IsNullOrWhiteSpace(_onConnectScript) ? null : _onConnectScript,
            OnDisconnectScript = string.IsNullOrWhiteSpace(_onDisconnectScript) ? null : _onDisconnectScript
        };
    }

    /// <summary>
    /// Marks the current state as clean (takes a new snapshot).
    /// </summary>
    public void MarkClean()
    {
        TakeSnapshot();
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsNew));
    }

    private void TakeSnapshot()
    {
        _hasSnapshot = true;
        _snapshotName = _name;
        _snapshotProtocol = _protocol;
        _snapshotHost = _host;
        _snapshotPort = _port;
        _snapshotUsername = _username;
        _snapshotPassword = _password;
        _snapshotOnConnectScript = _onConnectScript;
        _snapshotOnDisconnectScript = _onDisconnectScript;
        _snapshotEmulatorType = _emulatorType;
        _snapshotSize = _size;
        _snapshotPortName = _portName;
        _snapshotBaudRate = _baudRate;
        _snapshotDataBits = _dataBits;
        _snapshotStopBitsValue = _stopBitsValue;
        _snapshotParityValue = _parityValue;
        _snapshotHandshakeValue = _handshakeValue;
        _snapshotGatewayIdentCode = _gatewayIdentCode;
        _snapshotGatewayTerminalName = _gatewayTerminalName;
        _snapshotGatewaySelectFirstFree = _gatewaySelectFirstFree;
        _snapshotForegroundColor = _foregroundColor;
        _snapshotBackgroundColor = _backgroundColor;
        _snapshotSinglePhosphor = _singlePhosphor;
        _snapshotFollowWindowSize = _followWindowSize;
        _snapshotLanguage = _language;
        _snapshotBackspaceSendsDel = _backspaceSendsDel;
        _snapshotDeleteSendsDel = _deleteSendsDel;
        _snapshotNewLineReceive = _newLineReceive;
        _snapshotNewLineTransmit = _newLineTransmit;
        _snapshotLocalEcho = _localEcho;
        _snapshotTransmitDelayPerCharMs = _transmitDelayPerCharMs;
        _snapshotTransmitDelayPerLineMs = _transmitDelayPerLineMs;
    }

    private void ParseSize(out int width, out int height)
    {
        width = 80;
        height = 24;
        if (_size != null)
        {
            var parts = _size.Split('x');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out width);
                int.TryParse(parts[1], out height);
            }
        }
    }

    private void NotifyAllChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Protocol));
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(Username));
        OnPropertyChanged(nameof(Password));
        OnPropertyChanged(nameof(EmulatorType));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(PortName));
        OnPropertyChanged(nameof(BaudRate));
        OnPropertyChanged(nameof(DataBits));
        OnPropertyChanged(nameof(StopBitsValue));
        OnPropertyChanged(nameof(ParityValue));
        OnPropertyChanged(nameof(HandshakeValue));
        OnPropertyChanged(nameof(GatewayIdentCode));
        OnPropertyChanged(nameof(GatewayTerminalName));
        OnPropertyChanged(nameof(GatewaySelectFirstFree));
        OnPropertyChanged(nameof(ForegroundColor));
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(IsNew));
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
