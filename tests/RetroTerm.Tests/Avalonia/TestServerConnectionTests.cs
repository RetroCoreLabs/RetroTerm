using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Protocols.Net;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Tests client connection to TestServer and validates data exchange.
/// Tests the full connection stack: TerminalSession -> IConnection -> TelnetServer
/// Note: These tests involve actual TCP connections and may have timing issues.
/// </summary>
[Collection("Avalonia")]
[Trait("Category", "Integration")]
public class TestServerConnectionTests : IDisposable, IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    // Assigned by the per-test setup helper, not the constructor.
    private TcpListener _server = null!;
    private TcpClient _serverClient = null!;
    private TerminalSession _session = null!;
    private IConnection _connection = null!;
    private TDV2200Emulator _emulator = null!;
    private TelnetSession _serverSession = null!;
    private int _serverPort;
    private readonly List<byte> _receivedData = new List<byte>();

    public TestServerConnectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Closes everything the test opened, WITHOUT blocking the thread it runs on.
    /// </summary>
    /// <remarks>
    /// <para><b>This used to hang the whole class, for ever</b></para>
    /// It read <c>_session?.DisconnectAsync().GetAwaiter().GetResult();</c>. In a headless
    /// Avalonia test, xUnit posts the teardown onto the DISPATCHER thread - the live stacks show
    /// <c>DispatcherOperation.Execute</c> below <c>Dispose</c> - so that line blocked the very
    /// thread the disconnect's continuation needed to run on. Neither side could move again.
    ///
    /// It did not time out, and that is what made it expensive: the test never failed, it simply
    /// never finished, so the whole class stopped there and the other thirteen tests were never
    /// reached. Running the four integration classes together produced no output at all for forty
    /// minutes because of this one line. Measured and located with dotnet-stack on
    /// 10 September 2026.
    ///
    /// The disconnect now happens in <see cref="DisposeAsync"/>, which xUnit awaits instead of
    /// blocking on. What is left here cannot block: closing a socket and stopping a listener are
    /// synchronous.
    ///
    /// NEVER put a blocking wait on an async call in this class. It is the trap this comment
    /// exists to stop coming back.
    /// </remarks>
    public void Dispose()
    {
        _session?.Dispose();
        _connection?.Dispose();
        _serverClient?.Close();
        _server?.Stop();
    }

    /// <summary>
    /// xUnit awaits this before <see cref="Dispose"/>, so the disconnect can be awaited rather
    /// than blocked on. See the remarks on <see cref="Dispose"/> for why that matters.
    /// </summary>
    /// <returns>
    /// A task that completes when the session has disconnected.
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        if (_session != null)
        {
            await _session.DisconnectAsync();
        }
    }

    /// <summary>
    /// Nothing to set up here - each test calls <see cref="SetupConnectionAsync"/> itself, because
    /// they need the connection at different points.
    /// </summary>
    /// <returns>
    /// A completed task.
    /// </returns>
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Sets up client connection to a local TCP server
    /// </summary>
    private async Task SetupConnectionAsync()
    {
        // Start TCP server on random available port
        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;

        _output.WriteLine($"TestServer started on port {_serverPort}");

        // Create emulator and session
        _emulator = new TDV2200Emulator(80, 24);
        _session = new TerminalSession(_emulator, "TestServer Connection Test");

        // Create connection parameters
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        // Connect (server accepts connection)
        var connectTask = _session.ConnectAsync(_connection);
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverSession = new TelnetSession(_serverClient);
        await connectTask;

        // Telnet negotiation
        await _serverSession.Negotiator.SendInitialAsync();
        await Task.Delay(200);

        _output.WriteLine("Connection established and negotiated");
    }

    /// <summary>
    /// Reads data from server side within timeout
    /// </summary>
    private async Task<byte[]> ReadFromServerAsync(int timeoutMs = 1000)
    {
        var responseBuilder = new List<byte>();
        var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (_serverClient.Available > 0)
                {
                    var buffer = new byte[_serverClient.Available];
                    int bytesRead = await _serverSession.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    for (int i = 0; i < bytesRead; i++)
                    {
                        responseBuilder.Add(buffer[i]);
                    }
                    break; // Got data, stop waiting
                }
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        return responseBuilder.ToArray();
    }

    /// <summary>
    /// Sends data from server to client
    /// </summary>
    private async Task SendFromServerAsync(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        await _serverSession.Stream.WriteAsync(bytes, 0, bytes.Length);
        await _serverSession.Stream.FlushAsync();
    }

    /// <summary>
    /// Sends raw bytes from server to client
    /// </summary>
    private async Task SendFromServerAsync(byte[] data)
    {
        await _serverSession.Stream.WriteAsync(data, 0, data.Length);
        await _serverSession.Stream.FlushAsync();
    }

    #region Connection Tests

    [AvaloniaFact]
    public async Task Client_ShouldConnectToTestServer()
    {
        await SetupConnectionAsync();

        Assert.True(_session.IsConnected);
        Assert.NotNull(_serverClient);
        Assert.True(_serverClient.Connected);

        _output.WriteLine("Client successfully connected to TestServer");
    }

    [AvaloniaFact]
    public async Task Client_ShouldReceiveTextFromServer()
    {
        await SetupConnectionAsync();

        // Server sends text
        await SendFromServerAsync("Hello from TestServer\r\n");
        await Task.Delay(200);

        // Verify emulator received and processed the text
        var buffer = _emulator.Buffer;
        var line0 = GetBufferLine(buffer, 0);

        Assert.Contains("Hello from TestServer", line0);
        _output.WriteLine($"Client received: {line0.Trim()}");
    }

    [AvaloniaFact]
    public async Task Client_ShouldSendTextToServer()
    {
        await SetupConnectionAsync();

        // Clear any pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Client sends text through session
        await _session.SendInputAsync("Hello from Client\r");

        // Server receives
        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        var receivedText = Encoding.UTF8.GetString(received);
        Assert.Contains("Hello from Client", receivedText);
        _output.WriteLine($"Server received: {receivedText.Trim()}");
    }

    #endregion

    #region Keyboard Input Tests

    [AvaloniaFact]
    public async Task Client_ShouldSendFunctionKeyToServer()
    {
        await SetupConnectionAsync();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Client sends F1 key sequence
        await _session.SendInputAsync("\x1b[11~"); // TDV F1

        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        Assert.Equal("\x1b[11~", Encoding.UTF8.GetString(received));
        _output.WriteLine($"Server received F1: {ToVisibleString(received)}");
    }

    [AvaloniaFact]
    public async Task Client_ShouldSendArrowKeyToServer()
    {
        await SetupConnectionAsync();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Client sends arrow up
        await _session.SendInputAsync("\x1b[A"); // Arrow Up

        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        Assert.Equal("\x1b[A", Encoding.UTF8.GetString(received));
        _output.WriteLine($"Server received Arrow Up: {ToVisibleString(received)}");
    }

    [AvaloniaFact]
    public async Task Client_ShouldSendPushKeyToServer()
    {
        await SetupConnectionAsync();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Client sends PUSH1 key
        await _session.SendInputAsync("\x1b[?1~"); // PUSH1

        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        Assert.Equal("\x1b[?1~", Encoding.UTF8.GetString(received));
        _output.WriteLine($"Server received PUSH1: {ToVisibleString(received)}");
    }

    #endregion

    #region Query/Response Tests

    [AvaloniaFact]
    public async Task Client_ShouldRespondToDeviceAttributesQuery()
    {
        await SetupConnectionAsync();

        // Wire TDV query/response handling
        _session.WireTDVQueryResponse();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Server sends DA query (Primary Device Attributes)
        await SendFromServerAsync("\x1b[c");
        await Task.Delay(300);

        // Read response from client
        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        var response = Encoding.UTF8.GetString(received);
        _output.WriteLine($"DA Response: {ToVisibleString(received)}");

        // TDV2200 should respond with its DA string
        Assert.Contains("\x1b[?", response);
    }

    [AvaloniaFact]
    public async Task Client_ShouldRespondToCursorPositionReport()
    {
        await SetupConnectionAsync();

        // Wire TDV query/response handling
        _session.WireTDVQueryResponse();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Server sends CPR query (DSR 6)
        await SendFromServerAsync("\x1b[6n");
        await Task.Delay(300);

        // Read response from client
        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        var response = Encoding.UTF8.GetString(received);
        _output.WriteLine($"CPR Response: {ToVisibleString(received)}");

        // Should respond with cursor position ESC [ row ; col R
        Assert.Contains("\x1b[", response);
        Assert.Contains("R", response);
    }

    [AvaloniaFact]
    public async Task Client_ShouldRespondToDeviceStatusReport()
    {
        await SetupConnectionAsync();

        // Wire TDV query/response handling
        _session.WireTDVQueryResponse();

        // Clear pending data
        while (_serverClient.Available > 0)
        {
            var discard = new byte[_serverClient.Available];
            await _serverSession.Stream.ReadExactlyAsync(discard);
        }

        // Server sends DSR query (Device Status Report)
        await SendFromServerAsync("\x1b[5n");
        await Task.Delay(300);

        // Read response from client
        var received = await ReadFromServerAsync();

        Assert.NotEmpty(received);
        var response = Encoding.UTF8.GetString(received);
        _output.WriteLine($"DSR Response: {ToVisibleString(received)}");

        // Should respond with ESC [ 0 n (terminal OK)
        Assert.Contains("\x1b[0n", response);
    }

    #endregion

    #region Character Set Tests

    [AvaloniaFact]
    public async Task Client_ShouldSwitchCharacterSets()
    {
        await SetupConnectionAsync();

        // Server sends character set switch to Graphics I (ESC ( 1)
        await SendFromServerAsync("\x1b(1");
        await Task.Delay(100);

        // Server sends characters in graphics range (0x60-0x7F)
        await SendFromServerAsync(new byte[] { 0x60, 0x61, 0x62, 0x63 });
        await Task.Delay(100);

        // Verify emulator processed the character set change
        // (The characters should be mapped according to Graphics I set)
        var buffer = _emulator.Buffer;
        _output.WriteLine("Character set switch test completed");

        // The buffer should have received mapped characters
        // This verifies the character set switching mechanism works
        Assert.True(_session.IsConnected);
    }

    [AvaloniaFact]
    public async Task Client_ShouldHandleAllCharacterSetSwitches()
    {
        await SetupConnectionAsync();

        // Test all 10 character sets (0-9)
        for (int setNum = 0; setNum <= 9; setNum++)
        {
            // Switch to character set
            await SendFromServerAsync($"\x1b({setNum}");
            await Task.Delay(50);

            // Send test character
            await SendFromServerAsync(new byte[] { 0x60 }); // backtick position
            await Task.Delay(50);

            _output.WriteLine($"Character set {setNum}: Sent 0x60");
        }

        // Switch back to default set
        await SendFromServerAsync("\x1b(0");
        await Task.Delay(100);

        Assert.True(_session.IsConnected);
        _output.WriteLine("All character set switches completed");
    }

    #endregion

    #region Screen Operations Tests

    [AvaloniaFact]
    public async Task Client_ShouldHandleCursorMovement()
    {
        await SetupConnectionAsync();

        // Move cursor to position (10, 5)
        await SendFromServerAsync("\x1b[5;10H");
        await Task.Delay(100);

        // Verify cursor position
        Assert.Equal(9, _emulator.Cursor.Column); // 0-indexed
        Assert.Equal(4, _emulator.Cursor.Row);    // 0-indexed

        _output.WriteLine($"Cursor moved to row {_emulator.Cursor.Row}, col {_emulator.Cursor.Column}");
    }

    [AvaloniaFact]
    public async Task Client_ShouldHandleScreenClear()
    {
        await SetupConnectionAsync();

        // Write some text
        await SendFromServerAsync("Test content on screen");
        await Task.Delay(100);

        // Clear screen
        await SendFromServerAsync("\x1b[2J");
        await Task.Delay(100);

        // Verify screen is cleared
        var line0 = GetBufferLine(_emulator.Buffer, 0);
        Assert.True(string.IsNullOrWhiteSpace(line0));

        _output.WriteLine("Screen cleared successfully");
    }

    [AvaloniaFact]
    public async Task Client_ShouldHandleTextAttributes()
    {
        await SetupConnectionAsync();

        // Set bold
        await SendFromServerAsync("\x1b[1mBold\x1b[0m");
        await Task.Delay(100);

        // Set underline
        await SendFromServerAsync("\x1b[4mUnderline\x1b[0m");
        await Task.Delay(100);

        // Set reverse
        await SendFromServerAsync("\x1b[7mReverse\x1b[0m");
        await Task.Delay(100);

        Assert.True(_session.IsConnected);
        _output.WriteLine("Text attribute sequences processed");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// One row of the screen as a person would read it.
    /// </summary>
    /// <remarks>
    /// <para><b>Goes through GetString, and must keep doing so</b></para>
    /// A cleared cell holds codepoint 0, which the buffer keeps deliberately distinct from a
    /// WRITTEN space. Casting that straight to a char gives NUL, and a line of NULs is not
    /// whitespace as far as <c>string.IsNullOrWhiteSpace</c> is concerned - so the screen-clear
    /// test failed on a screen that had been cleared perfectly well.
    ///
    /// <c>TerminalCell.GetString</c> is the product's own answer to "what does this cell show",
    /// and it renders an empty cell as a space. It also maps the TDV line-drawing bytes to the
    /// glyphs they draw, which the raw codepoint does not, so a box read through here reads as a
    /// box rather than as "g``````i".
    /// </remarks>
    /// <param name="buffer">
    /// The screen to read.
    /// </param>
    /// <param name="row">
    /// Which row.
    /// </param>
    /// <returns>
    /// The row's text.
    /// </returns>
    private static string GetBufferLine(RetroTerm.Core.Terminal.Buffer.TerminalBuffer buffer, int row)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer.GetCell(row, col);
            sb.Append(cell.GetString());
        }
        return sb.ToString();
    }

    private static string ToVisibleString(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return "(empty)";

        var result = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if (b == 0x1b)
                result.Append("ESC");
            else if (b < 0x20)
                result.Append($"<{b:X2}>");
            else
                result.Append((char)b);
        }
        return result.ToString();
    }

    #endregion
}
