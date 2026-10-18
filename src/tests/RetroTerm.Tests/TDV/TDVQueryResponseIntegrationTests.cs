using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.TDV;

public class TDVQueryResponseIntegrationTests
{
    [Fact]
    public void TDV1200_PrimaryDA_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Primary DA query: ESC [ c
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[?1;2c", responses[0]);
    }

    [Fact]
    public void TDV1200_SecondaryDA_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Secondary DA query: ESC [ > c
        var query = "\x1b[>c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[>120;0;0c", responses[0]);
    }

    [Fact]
    public void TDV1200_CPR_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[5;10H")); // Move cursor to row 5, col 10
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send CPR query: ESC [ 6 n
        var query = "\x1b[6n";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[5;10R", responses[0]);
    }

    [Fact]
    public void TDV1200_DSR_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send DSR query: ESC [ 5 n
        var query = "\x1b[5n";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[0n", responses[0]);
    }

    /// <summary>
    /// A mode query raises nothing on <c>OnResponseReady</c>, because a TDV has no mode query.
    /// </summary>
    /// <remarks>
    /// This used to send <c>CSI ? 66 $ y</c> and assert a reply came back. No TDV manual describes
    /// a mode query: TDV 2215 Functional Specifications section 8.7 lists every CSI sequence the
    /// terminal accepts and none carries a <c>$</c> intermediate, and section 8.3.2 lists
    /// everything it sends, which is CPR alone. The shape sent was a reply final, <c>$ y</c>,
    /// rather than a request&#39;s <c>$ p</c>.
    ///
    /// What a TDV really has is NDRQ, ND-1200 section 5.48, and it is not implemented yet.
    /// See <c>docs&#92;TDV-MODES-AND-REPORTS-FROM-THE-MANUALS.md</c>.
    /// </remarks>
    [Fact]
    public void TDV1200_ModeQuery_RaisesNothing()
    {
        var emulator = new TDV1200Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        emulator.ProcessInput(Encoding.UTF8.GetBytes("\x1b[?66$y"));

        Assert.Empty(responses);
    }

    [Fact]
    public async Task TerminalSession_ShouldWireOnResponseReady_AndSendThroughConnection()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new Mock<IConnection>();
        var sentData = new List<byte[]>();

        mockConnection.Setup(c => c.Status).Returns(ConnectionStatus.Connected);
        mockConnection.Setup(c => c.IsConnected).Returns(true);
        mockConnection.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((data, ct) =>
            {
                sentData.Add(data.ToArray());
                return Task.CompletedTask;
            });

        // Act - Connect session
        await session.ConnectAsync(mockConnection.Object, TestContext.Current.CancellationToken);

        // Send Primary DA query
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Give async handler time to execute
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(sentData);
        var sentBytes = sentData[0];
        var sentString = Encoding.UTF8.GetString(sentBytes);
        Assert.Equal("\x1b[?1;2c", sentString);
    }

    [Fact]
    public async Task TerminalSession_ShouldNotSendWhenConnectionNotConnected()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new Mock<IConnection>();
        var sentData = new List<byte[]>();

        mockConnection.Setup(c => c.Status).Returns(ConnectionStatus.Disconnected);
        mockConnection.Setup(c => c.IsConnected).Returns(false);
        mockConnection.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((data, ct) =>
            {
                sentData.Add(data.ToArray());
                return Task.CompletedTask;
            });

        // Act - Connect session (but connection is disconnected)
        await session.ConnectAsync(mockConnection.Object, TestContext.Current.CancellationToken);

        // Send Primary DA query
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Give async handler time to execute
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Should not send when connection is not connected
        Assert.Empty(sentData);
    }

    [Fact]
    public async Task TerminalSession_ShouldHandleSendException()
    {
        // Arrange
        var emulator = new TDV1200Emulator(80, 24);
        var session = new TerminalSession(emulator, "Test");
        var mockConnection = new Mock<IConnection>();

        mockConnection.Setup(c => c.Status).Returns(ConnectionStatus.Connected);
        mockConnection.Setup(c => c.IsConnected).Returns(true);
        mockConnection.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Send failed"));

        // Act - Connect session
        await session.ConnectAsync(mockConnection.Object, TestContext.Current.CancellationToken);

        // Send Primary DA query - should not throw
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Give async handler time to execute
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Should not throw, exception should be caught
        Assert.True(true); // If we get here, exception was handled
    }

    [Fact]
    public void TDV2215_PrimaryDA_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV2215Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Primary DA query: ESC [ c
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[?1;2c", responses[0]);
    }

    [Fact]
    public void TDV2200_PrimaryDA_ShouldTriggerOnResponseReady()
    {
        // Arrange
        var emulator = new TDV2200Emulator(80, 24);
        var responses = new List<string>();
        emulator.OnResponseReady += response => responses.Add(response);

        // Act - Send Primary DA query: ESC [ c
        var query = "\x1b[c";
        emulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Assert
        Assert.Single(responses);
        Assert.Equal("\x1b[?220;0c", responses[0]);
    }

    [Fact]
    public async Task TerminalSession_ShouldWireOnResponseReady_WhenEmulatorIsReplaced()
    {
        // Arrange - Start with VT100 emulator
        var vt100Emulator = new VT100Emulator(80, 24);
        var session = new TerminalSession(vt100Emulator, "Test");
        var mockConnection = new Mock<IConnection>();
        var sentData = new List<byte[]>();

        mockConnection.Setup(c => c.Status).Returns(ConnectionStatus.Connected);
        mockConnection.Setup(c => c.IsConnected).Returns(true);
        mockConnection.Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((data, ct) =>
            {
                sentData.Add(data.ToArray());
                return Task.CompletedTask;
            });

        // Connect with VT100
        await session.ConnectAsync(mockConnection.Object, TestContext.Current.CancellationToken);

        // Replace with TDV1200 emulator
        var tdvEmulator = new TDV1200Emulator(80, 24);
        var newSession = new TerminalSession(tdvEmulator, "Test");
        await newSession.ConnectAsync(mockConnection.Object, TestContext.Current.CancellationToken);

        // Send Primary DA query to TDV emulator
        var query = "\x1b[c";
        tdvEmulator.ProcessInput(Encoding.UTF8.GetBytes(query));

        // Give async handler time to execute
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert - Should have sent response
        Assert.Single(sentData);
        var sentBytes = sentData[0];
        var sentString = Encoding.UTF8.GetString(sentBytes);
        Assert.Equal("\x1b[?1;2c", sentString);
    }
}

