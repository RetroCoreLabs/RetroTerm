using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;
using RetroTerm.Core.Protocols;

namespace RetroTerm.Core.Protocols.Net;

/// <summary>
/// SSH protocol connection implementation using SSH.NET library.
/// Supports both password and key-based authentication.
/// </summary>
public class SSHConnection : IConnection
{
    private readonly string _host;
    private readonly int _port;
    private string _username;
    private string? _password;
    private readonly string? _privateKeyPath;
    private SshClient? _client;
    private ShellStream? _stream;
    private ConnectionStatus _status;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;

    /// <summary>
    /// Gets or sets the terminal type to announce (e.g., "vt100", "vt220", "xterm-256color")
    /// </summary>
    public string TerminalType { get; set; } = "vt100";

    /// <summary>
    /// Gets or sets the terminal size (columns x rows)
    /// </summary>
    public (int Columns, int Rows) TerminalSize { get; set; } = (80, 24);

    /// <summary>
    /// Gets or sets the host key validation callback.
    /// If null, all host keys are accepted (INSECURE!).
    /// </summary>
    public Func<string, byte[], bool>? HostKeyValidation { get; set; }

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(value);
            }
        }
    }

    public bool IsConnected => Status == ConnectionStatus.Connected;

    public string ConnectionType => "SSH";

    public string Description => $"SSH://{_username}@{_host}:{_port}";

    public event Action<ReadOnlyMemory<byte>>? DataReceived;
    public event Action<ConnectionStatus>? StatusChanged;
    public event Action<Exception>? ErrorOccurred;

    /// <summary>
    /// Creates an SSH connection with password authentication.
    /// Username and password can be null and set later via SetCredentials before connecting.
    /// </summary>
    public SSHConnection(string host, int port, string? username, string? password)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _username = username ?? "";
        _password = password;
        _status = ConnectionStatus.Disconnected;
    }

    /// <summary>
    /// Sets credentials after construction (for in-terminal prompting).
    /// </summary>
    public void SetCredentials(string username, string password)
    {
        _username = username;
        _password = password;
    }

    /// <summary>
    /// Creates an SSH connection with key-based authentication
    /// </summary>
    public SSHConnection(string host, int port, string username, string privateKeyPath, string? passphrase)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _username = username ?? throw new ArgumentNullException(nameof(username));
        _privateKeyPath = privateKeyPath ?? throw new ArgumentNullException(nameof(privateKeyPath));
        _password = passphrase; // Used as passphrase for private key
        _status = ConnectionStatus.Disconnected;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ConnectionStatus.Disconnected)
            throw new InvalidOperationException($"Cannot connect in state: {Status}");

        if (string.IsNullOrEmpty(_username))
            throw new InvalidOperationException("Username must be set before connecting");

        Status = ConnectionStatus.Connecting;

        try
        {
            // Create authentication method
            AuthenticationMethod authMethod;
            if (!string.IsNullOrEmpty(_privateKeyPath))
            {
                // Key-based authentication
                var keyFile = !string.IsNullOrEmpty(_password)
                    ? new PrivateKeyFile(_privateKeyPath, _password)
                    : new PrivateKeyFile(_privateKeyPath);
                authMethod = new PrivateKeyAuthenticationMethod(_username, keyFile);
            }
            else
            {
                // Password authentication
                authMethod = new PasswordAuthenticationMethod(_username, _password ?? "");
            }

            var connectionInfo = new ConnectionInfo(_host, _port, _username, authMethod);

            // Create and connect SSH client
            _client = new SshClient(connectionInfo);

            // Set up host key validation if provided
            if (HostKeyValidation != null)
            {
                _client.HostKeyReceived += (sender, e) =>
                {
                    e.CanTrust = HostKeyValidation(_host, e.HostKey);
                };
            }

            await Task.Run(() => _client.Connect());

            if (!_client.IsConnected)
            {
                throw new IOException("SSH connection failed");
            }

            // Create shell stream with terminal settings
            _stream = _client.CreateShellStream(
                TerminalType,
                (uint)TerminalSize.Columns,
                (uint)TerminalSize.Rows,
                (uint)TerminalSize.Columns * 8, // Width in pixels (estimate)
                (uint)TerminalSize.Rows * 16,   // Height in pixels (estimate)
                1024 * 1024); // 1MB buffer

            Status = ConnectionStatus.Connected;

            // Start receive loop
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        }
        catch (SshAuthenticationException ex)
        {
            Status = ConnectionStatus.Disconnected;
            throw new UnauthorizedAccessException("SSH authentication failed", ex);
        }
        catch (Exception ex)
        {
            Status = ConnectionStatus.Disconnected;
            _client?.Dispose();
            _client = null;
            throw new IOException($"Failed to connect to {_host}:{_port}", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        if (Status == ConnectionStatus.Disconnected)
            return;

        Status = ConnectionStatus.Disconnecting;

        try
        {
            // Stop receive loop
            _receiveCts?.Cancel();
            if (_receiveTask != null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            // Close stream and client
            _stream?.Dispose();
            _stream = null;

            _client?.Disconnect();
            _client?.Dispose();
            _client = null;
        }
        finally
        {
            Status = ConnectionStatus.Disconnected;
            _receiveCts?.Dispose();
            _receiveCts = null;
            _receiveTask = null;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("Not connected");

        try
        {
            // SSH.NET's ShellStream.WriteAsync can hang indefinitely in some scenarios
            // Use synchronous Write() wrapped in Task.Run to prevent deadlocks
            await Task.Run(() =>
            {
                _stream.Write(data.Span);
                _stream.Flush();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(new IOException("Failed to send data", ex));
            throw;
        }
    }

    /// <summary>
    /// Resizes the terminal window
    /// </summary>
    public void ResizeTerminal(int columns, int rows)
    {
        if (_stream == null)
            throw new InvalidOperationException("Not connected");

        TerminalSize = (columns, rows);

        try
        {
            // SSH.NET doesn't have a direct resize method, but we can send SIGWINCH
            // For now, we'll just update our internal state
            // A full implementation would need to send the resize signal
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(new IOException("Failed to resize terminal", ex));
        }
    }

    /// <summary>
    /// Records the new terminal size for this session.
    /// </summary>
    /// <remarks>
    /// <para><b>This does not reach the far end yet, and says so</b></para>
    /// SSH carries a resize as a <c>window-change</c> channel request, and the SSH.NET build
    /// referenced here exposes no way to send one on a shell stream. So the size is kept, and a
    /// connection opened AFTER a resize gets the right size, while a program already running does
    /// not hear about it. Saying nothing at all would be worse: the size a host is told at login
    /// would silently be the window's, then never change.
    /// Unlike <see cref="ResizeTerminal"/> this does not throw when there is no stream. A window
    /// being dragged before anything is connected is ordinary, not an error.
    /// </remarks>
    /// <param name="columns">
    /// New width in character cells.
    /// </param>
    /// <param name="rows">
    /// New height in character cells.
    /// </param>
    /// <param name="cancellationToken">
    /// Unused; nothing is sent.
    /// </param>
    /// <returns>
    /// A completed task.
    /// </returns>
    public Task ResizeTerminalAsync(int columns, int rows, CancellationToken cancellationToken = default)
    {
        TerminalSize = (columns, rows);
        return Task.CompletedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null && _stream.CanRead)
            {
                try
                {
                    var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);

                    if (bytesRead == 0)
                    {
                        // Connection closed
                        break;
                    }

                    // Fire data received event
                    var data = new byte[bytesRead];
                    Array.Copy(buffer, data, bytesRead);
                    DataReceived?.Invoke(data);
                }
                catch (SshConnectionException)
                {
                    // Connection lost
                    break;
                }
            }

            // Connection closed
            if (!cancellationToken.IsCancellationRequested && Status == ConnectionStatus.Connected)
            {
                Status = ConnectionStatus.Disconnected;
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disconnecting
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex);
            Status = ConnectionStatus.Disconnected;
        }
    }

    public void Dispose()
    {
        DisconnectAsync().Wait();
    }
}

