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
using RetroTerm.Core.Protocols.TelnetServer.Utilities;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Comprehensive TDV2200 tests that run through the full stack with Avalonia headless support.
/// These tests validate:
/// 1. Query/Response through real TCP connections
/// 2. Full data flow: Server -> Connection -> Session -> Emulator -> Response -> Server
/// 3. TDV2200 specification compliance
/// </summary>
[Collection("Avalonia")]
public class TDV2200AvaloniaTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private TcpListener? _server;
    private TcpClient? _serverClient;
    private TerminalSession? _session;
    private IConnection? _connection;
    private TDV2200Emulator? _emulator;
    private TelnetSession? _serverSession;
    private int _serverPort;
    private readonly List<byte[]> _receivedResponses = new List<byte[]>();

    public TDV2200AvaloniaTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        // Close TCP resources first (this will unblock any pending reads)
        try { _serverClient?.Close(); } catch { }
        try { _server?.Stop(); } catch { }

        // Then dispose managed resources (don't await async disconnect - it can hang)
        try { _connection?.Dispose(); } catch { }
        try { _session?.Dispose(); } catch { }
    }

    /// <summary>
    /// Sets up a real TCP connection between TestServer and TDV2200 emulator through TerminalSession
    /// </summary>
    private async Task SetupConnectionAsync()
    {
        // Start TCP server (simulating TestServer)
        _server = new TcpListener(IPAddress.Loopback, 0);
        _server.Start();
        _serverPort = ((IPEndPoint)_server.LocalEndpoint).Port;
        _output.WriteLine($"Test server started on port {_serverPort}");

        // Create TDV2200 emulator
        _emulator = new TDV2200Emulator(80, 24);

        // Create TerminalSession (this wires up the query/response handling)
        _session = new TerminalSession(_emulator, "TDV2200 Test");

        // Create connection using factory
        var parameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "127.0.0.1",
            Port = _serverPort,
            EmulatorType = "TDV2200"
        };

        _connection = ConnectionFactory.CreateConnection(parameters);

        // Start connection (this initiates TCP connection)
        var connectTask = _session.ConnectAsync(_connection);

        // Accept server-side connection
        _serverClient = await _server.AcceptTcpClientAsync();
        _serverSession = new TelnetSession(_serverClient);

        // Wait for connection to complete
        await connectTask;

        // Send Telnet negotiation from server
        await _serverSession.Negotiator.SendInitialAsync();

        // Wait for negotiation to settle
        await Task.Delay(200);

        _output.WriteLine("Connection established");
    }

    /// <summary>
    /// Sends a query to the terminal and captures the response
    /// </summary>
    private async Task<string?> SendQueryAndGetResponseAsync(byte[] query, int timeoutMs = 2000)
    {
        // Clear any pending data
        while (_serverClient!.Available > 0)
        {
            var buffer = new byte[_serverClient.Available];
            await _serverSession!.Stream.ReadExactlyAsync(buffer);
        }

        _output.WriteLine($"Sending query: {TDVSequenceBuilder.ToVisibleString(query)}");

        // Send query from server to terminal
        await _serverSession!.WriteBytesAsync(query);
        await _serverSession.Stream.FlushAsync();

        // Wait for response with timeout
        var responseBuilder = new List<byte>();
        var startTime = DateTime.UtcNow;
        var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (_serverClient.Available > 0)
                {
                    var buffer = new byte[_serverClient.Available];
                    int bytesRead = await _serverSession.Stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    if (bytesRead > 0)
                    {
                        for (int i = 0; i < bytesRead; i++)
                        {
                            responseBuilder.Add(buffer[i]);
                        }

                        // Check if we have a complete response (ends with a terminal character)
                        if (IsCompleteResponse(responseBuilder))
                        {
                            break;
                        }
                    }
                }
                else
                {
                    await Task.Delay(10, cts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout - return whatever we have
        }

        if (responseBuilder.Count == 0)
        {
            _output.WriteLine("No response received");
            return null;
        }

        var response = Encoding.UTF8.GetString(responseBuilder.ToArray());
        _output.WriteLine($"Received response: {TDVResponseValidator.ToVisibleString(response)}");
        return response;
    }

    /// <summary>
    /// Checks if the response appears complete (ends with expected terminal character)
    /// </summary>
    private static bool IsCompleteResponse(List<byte> data)
    {
        if (data.Count < 3) return false;

        // DA responses end with 'c'
        // CPR responses end with 'R'
        // DSR responses end with 'n'
        // DECRQM responses end with 'y'
        byte lastByte = data[data.Count - 1];
        return lastByte == 'c' || lastByte == 'R' || lastByte == 'n' || lastByte == 'y';
    }

    #region Primary DA Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_PrimaryDA_ShouldRespondWithCorrectID()
    {
        // Arrange
        await SetupConnectionAsync();
        var query = TDVSequenceBuilder.BuildDAQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid, $"Invalid DA response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.False(daResponse.IsSecondary, "Should be primary DA response");
        Assert.Equal(220, daResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Primary DA: FirmwareId={daResponse.FirmwareId}, Config={daResponse.Configuration}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_SecondaryDA_ShouldRespondWithFirmwareID()
    {
        // Arrange
        await SetupConnectionAsync();
        var query = TDVSequenceBuilder.BuildSecondaryDAQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var daResponse = TDVResponseValidator.ParseDAResponse(response);
        Assert.True(daResponse.IsValid, $"Invalid Secondary DA response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.True(daResponse.IsSecondary, "Should be secondary DA response");
        Assert.Equal(220, daResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Secondary DA: FirmwareId={daResponse.FirmwareId}, Version={daResponse.Version}, Config={daResponse.Configuration}");
    }

    #endregion

    #region CPR Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_CPR_ShouldRespondWithCursorPosition_AtHome()
    {
        // Arrange
        await SetupConnectionAsync();
        var query = TDVSequenceBuilder.BuildCPRQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var cprResponse = TDVResponseValidator.ParseCPRResponse(response);
        Assert.True(cprResponse.IsValid, $"Invalid CPR response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.Equal(1, cprResponse.Row);
        Assert.Equal(1, cprResponse.Column);

        _output.WriteLine($"TDV2200 CPR at home: Row={cprResponse.Row}, Col={cprResponse.Column}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_CPR_ShouldRespondWithCursorPosition_AfterMove()
    {
        // Arrange
        await SetupConnectionAsync();

        // Move cursor to row 10, column 20
        var moveSequence = TDVSequenceBuilder.BuildCSI(null, new[] { 10, 20 }, 'H');
        await _serverSession!.WriteBytesAsync(moveSequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(50);

        // Query cursor position
        var query = TDVSequenceBuilder.BuildCPRQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var cprResponse = TDVResponseValidator.ParseCPRResponse(response);
        Assert.True(cprResponse.IsValid, $"Invalid CPR response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.Equal(10, cprResponse.Row);
        Assert.Equal(20, cprResponse.Column);

        _output.WriteLine($"TDV2200 CPR after move: Row={cprResponse.Row}, Col={cprResponse.Column}");
    }

    #endregion

    #region DSR Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_DSR_ShouldRespondWithReadyStatus()
    {
        // Arrange
        await SetupConnectionAsync();
        var query = TDVSequenceBuilder.BuildDSRQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var dsrResponse = TDVResponseValidator.ParseDSRResponse(response);
        Assert.True(dsrResponse.IsValid, $"Invalid DSR response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.True(dsrResponse.IsOK, "DSR should indicate ready status");
        Assert.False(dsrResponse.IsFailure, "DSR should not indicate failure");

        _output.WriteLine($"TDV2200 DSR: OK={dsrResponse.IsOK}");
    }

    #endregion

    #region Terminal ID Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_TerminalID_ShouldRespondCorrectly()
    {
        // Arrange
        await SetupConnectionAsync();
        var query = TDVSequenceBuilder.BuildTerminalIDQuery();

        // Act
        var response = await SendQueryAndGetResponseAsync(query);

        // Assert
        Assert.NotNull(response);
        var terminalIdResponse = TDVResponseValidator.ParseTerminalIDResponse(response);
        Assert.True(terminalIdResponse.IsValid, $"Invalid Terminal ID response: {TDVResponseValidator.ToVisibleString(response)}");
        Assert.Equal(220, terminalIdResponse.FirmwareId);

        _output.WriteLine($"TDV2200 Terminal ID: Type={terminalIdResponse.TerminalType}, FirmwareId={terminalIdResponse.FirmwareId}");
    }

    #endregion

    #region Mode Query Tests

    /// <summary>
    /// Over a real connection: resetting mode 66 enters 2115 mode, setting it leaves.
    /// </summary>
    /// <remarks>
    /// <para><b>This replaces the one test that had been switched off for weeks</b></para>
    /// <c>TDV2200_2115CompatibilityMode_WhenDisabled_ShouldReportDisabled</c> carried the skip
    /// reason "DECRQM response parser issue - mode 40 value 0 being interpreted incorrectly". The
    /// parser was never at fault. Neither the sequence nor the mode number existed.
    ///
    /// No TDV manual has a mode query: TDV 2215 Functional Specifications section 8.7 lists every
    /// CSI sequence the terminal accepts and none carries a <c>$</c> intermediate, and section
    /// 8.3.2 lists everything it sends, which is CPR alone. Mode 40 is PCF, the printer code
    /// format (section 8.7.1). The 2115 switch is mode 66, it carries no private marker, and
    /// RESET is the 2115 side of it - section 3.1: "When this switch is set to OFF, the terminal
    /// works like a TDV 2115 from the host computer&#39;s point of view."
    ///
    /// So the state is read directly and the query is not sent. What this still exercises that a
    /// direct test does not is the whole path - the host writes the bytes, they cross the
    /// connection, and the session feeds them to the emulator.
    ///
    /// See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ModeSixtySixOverTheWire_EntersAndLeaves2115Mode()
    {
        await SetupConnectionAsync();

        Assert.False(_emulator!.Is2115CompatibilityMode,
            "a fresh TDV2200 is in extended operation, not 2115 mode");

        await _serverSession!.WriteBytesAsync(TDVSequenceBuilder.Build2115CompatibilityEnable());
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(50);
        Assert.True(_emulator.Is2115CompatibilityMode,
            "CSI 66 l, the EC switch off, should have entered 2115 mode");

        await _serverSession.WriteBytesAsync(TDVSequenceBuilder.Build2115CompatibilityDisable());
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(50);
        Assert.False(_emulator.Is2115CompatibilityMode,
            "CSI 66 h, the EC switch on, should have left 2115 mode");

        _output.WriteLine("TDV2200 over the wire: CSI 66 l enters 2115 mode, CSI 66 h leaves it");
    }

    #endregion

    #region Multiple Query Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_MultipleQueries_ShouldAllRespond()
    {
        // Arrange
        await SetupConnectionAsync();

        // Test 1: Primary DA
        var daQuery = TDVSequenceBuilder.BuildDAQuery();
        var daResponse = await SendQueryAndGetResponseAsync(daQuery);
        Assert.NotNull(daResponse);
        var parsedDa = TDVResponseValidator.ParseDAResponse(daResponse);
        Assert.True(parsedDa.IsValid, "Primary DA should be valid");
        _output.WriteLine($"Query 1 (Primary DA): Valid");

        await Task.Delay(50);

        // Test 2: Secondary DA
        var secondaryDaQuery = TDVSequenceBuilder.BuildSecondaryDAQuery();
        var secondaryDaResponse = await SendQueryAndGetResponseAsync(secondaryDaQuery);
        Assert.NotNull(secondaryDaResponse);
        var parsedSecondaryDa = TDVResponseValidator.ParseDAResponse(secondaryDaResponse);
        Assert.True(parsedSecondaryDa.IsValid && parsedSecondaryDa.IsSecondary, "Secondary DA should be valid");
        _output.WriteLine($"Query 2 (Secondary DA): Valid");

        await Task.Delay(50);

        // Test 3: CPR
        var cprQuery = TDVSequenceBuilder.BuildCPRQuery();
        var cprResponse = await SendQueryAndGetResponseAsync(cprQuery);
        Assert.NotNull(cprResponse);
        var parsedCpr = TDVResponseValidator.ParseCPRResponse(cprResponse);
        Assert.True(parsedCpr.IsValid, "CPR should be valid");
        _output.WriteLine($"Query 3 (CPR): Valid");

        await Task.Delay(50);

        // Test 4: DSR
        var dsrQuery = TDVSequenceBuilder.BuildDSRQuery();
        var dsrResponse = await SendQueryAndGetResponseAsync(dsrQuery);
        Assert.NotNull(dsrResponse);
        var parsedDsr = TDVResponseValidator.ParseDSRResponse(dsrResponse);
        Assert.True(parsedDsr.IsValid, "DSR should be valid");
        _output.WriteLine($"Query 4 (DSR): Valid");

        _output.WriteLine("All 4 queries responded correctly!");
    }

    #endregion

    #region Display Content Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_TextDisplay_ShouldAppearInBuffer()
    {
        // Arrange
        await SetupConnectionAsync();

        // Send text to terminal
        var text = "Hello TDV2200 World!";
        await _serverSession!.WriteBytesAsync(Encoding.UTF8.GetBytes(text));
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Read buffer content
        var buffer = _emulator!.GetBuffer();
        var lineBuilder = new StringBuilder();
        for (int col = 0; col < buffer.Width; col++)
        {
            var cell = buffer[0, col];
            if (cell.Codepoint == 0) break;
            lineBuilder.Append((char)cell.Codepoint);
        }
        var bufferLine = lineBuilder.ToString().TrimEnd();

        // Assert
        Assert.Equal(text, bufferLine);
        _output.WriteLine($"Buffer content: '{bufferLine}'");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_CursorMove_ShouldUpdatePosition()
    {
        // Arrange
        await SetupConnectionAsync();

        // Move cursor to row 5, column 10
        var moveSequence = TDVSequenceBuilder.BuildCSI(null, new[] { 5, 10 }, 'H');
        await _serverSession!.WriteBytesAsync(moveSequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Check cursor position
        var cursor = _emulator!.GetCursor();

        // Assert (cursor positions are 0-indexed internally, 1-indexed in sequences)
        Assert.Equal(4, cursor.Row);
        Assert.Equal(9, cursor.Column);
        _output.WriteLine($"Cursor position: Row={cursor.Row} (0-indexed), Col={cursor.Column} (0-indexed)");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_ClearScreen_ShouldClearBuffer()
    {
        // Arrange
        await SetupConnectionAsync();

        // Write some text
        await _serverSession!.WriteBytesAsync(Encoding.UTF8.GetBytes("Some text to clear"));
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(50);

        // Clear screen (ESC [ 2 J) and home cursor (ESC [ H)
        var clearSequence = Encoding.UTF8.GetBytes("\x1b[2J\x1b[H");
        await _serverSession.WriteBytesAsync(clearSequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Check buffer is cleared
        var buffer = _emulator!.GetBuffer();
        bool hasContent = false;
        for (int row = 0; row < buffer.Height; row++)
        {
            for (int col = 0; col < buffer.Width; col++)
            {
                var cell = buffer[row, col];
                if (cell.Codepoint != 0 && cell.Codepoint != ' ')
                {
                    hasContent = true;
                    break;
                }
            }
            if (hasContent) break;
        }

        // Assert
        Assert.False(hasContent, "Buffer should be empty after clear screen");
        _output.WriteLine("Buffer cleared successfully");
    }

    #endregion

    #region Attribute Tests

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_SGR_Bold_ShouldSetAttribute()
    {
        // Arrange
        await SetupConnectionAsync();

        // Send bold text: ESC [ 1 m BOLD ESC [ 0 m
        var sequence = Encoding.UTF8.GetBytes("\x1b[1mBOLD\x1b[0m");
        await _serverSession!.WriteBytesAsync(sequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Check first character attributes
        var buffer = _emulator!.GetBuffer();
        var cell = buffer[0, 0];

        // Assert
        Assert.Equal('B', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Bold), "First character should be bold");
        _output.WriteLine($"Bold attribute test: Codepoint='{(char)cell.Codepoint}', Bold={cell.Attributes.HasAttribute(CharacterAttributes.Bold)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_SGR_Underline_ShouldSetAttribute()
    {
        // Arrange
        await SetupConnectionAsync();

        // Send underlined text: ESC [ 4 m UNDER ESC [ 0 m
        var sequence = Encoding.UTF8.GetBytes("\x1b[4mUNDER\x1b[0m");
        await _serverSession!.WriteBytesAsync(sequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Check first character attributes
        var buffer = _emulator!.GetBuffer();
        var cell = buffer[0, 0];

        // Assert
        Assert.Equal('U', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Underline), "First character should be underlined");
        _output.WriteLine($"Underline attribute test: Codepoint='{(char)cell.Codepoint}', Underline={cell.Attributes.HasAttribute(CharacterAttributes.Underline)}");
    }

    [AvaloniaFact(Timeout = 10000)]
    public async Task TDV2200_SGR_Reverse_ShouldSetAttribute()
    {
        // Arrange
        await SetupConnectionAsync();

        // Send reverse video text: ESC [ 7 m REV ESC [ 0 m
        var sequence = Encoding.UTF8.GetBytes("\x1b[7mREV\x1b[0m");
        await _serverSession!.WriteBytesAsync(sequence);
        await _serverSession.Stream.FlushAsync();
        await Task.Delay(100);

        // Act - Check first character attributes
        var buffer = _emulator!.GetBuffer();
        var cell = buffer[0, 0];

        // Assert
        Assert.Equal('R', (char)cell.Codepoint);
        Assert.True(cell.Attributes.HasAttribute(CharacterAttributes.Reverse), "First character should be reverse video");
        _output.WriteLine($"Reverse attribute test: Codepoint='{(char)cell.Codepoint}', Reverse={cell.Attributes.HasAttribute(CharacterAttributes.Reverse)}");
    }

    #endregion
}
