//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using RetroTerm.Core.Commands;
using RetroTerm.Core.Commands.Builtin;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;

using Xunit;

namespace RetroTerm.Tests.Commands;

/// <summary>
/// SUBMIT against a local TcpListener that stands in for the Hercules socket card reader: it
/// reads until the client closes its side and then closes the connection, which is what
/// cardrdr.c does (read_ascii to the end of the data, then cardrdr_close_device).
/// </summary>
public class SubmitCommandTests
{
    [Fact]
    public async Task TheDeckArrivesExactlyAsTheFileHasIt()
    {
        byte[] file = Encoding.ASCII.GetBytes("//RTT1 JOB CLASS=A\r\n//STEP1 EXEC PGM=IEFBR14\r\n");
        using var reader = new StandInReader(StandInReader.Ending.CloseAfterTheDeck);
        (CommandResult result, _) = await SubmitAsync(file, reader.Port);

        Assert.True(result.Success, result.Error);
        Assert.Equal(file, await reader.ReceivedAsync());
        Assert.Contains("2 cards (" + file.Length + " bytes)", result.Output);
        Assert.Contains("closed the connection after the last card", result.Output);
    }

    [Fact]
    public async Task LineFeedsAloneWorkTheSameAsCrLf()
    {
        byte[] file = Encoding.ASCII.GetBytes("//RTT1 JOB CLASS=A\n//STEP1 EXEC PGM=IEFBR14\n");
        using var reader = new StandInReader(StandInReader.Ending.CloseAfterTheDeck);
        (CommandResult result, _) = await SubmitAsync(file, reader.Port);

        Assert.True(result.Success, result.Error);
        Assert.Equal(file, await reader.ReceivedAsync());
        Assert.Contains("2 cards", result.Output);
    }

    [Fact]
    public void TheCardsAreCountedTheWayTheReaderMakesThem()
    {
        // A last line without a line feed is a card; an empty line is a blank card; a carriage
        // return alone is skipped (read_ascii).
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("A\r\n\r\nB"), out int cards));
        Assert.Equal(3, cards);
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("A\n"), out cards));
        Assert.Equal(1, cards);
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("A\n\r"), out cards));
        Assert.Equal(1, cards);
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("\n"), out cards));
        Assert.Equal(1, cards);
    }

    [Fact]
    public void EightyColumnsFitAndEightyOneAreRefusedWithTheLineNumber()
    {
        string eighty = new string('X', 80);
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("//A JOB\n" + eighty + "\n"), out int cards));
        Assert.Equal(2, cards);

        string? problem = CardReader.Check(Encoding.ASCII.GetBytes("//A JOB\n" + eighty + "Y\n//\n"), out cards);
        Assert.NotNull(problem);
        Assert.StartsWith("line 2 is longer than 80 columns", problem);
        Assert.Equal(0, cards);

        // Blanks past column 80 change nothing on the card, so they are let through.
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes(eighty + "    \r\n"), out cards));
        Assert.Equal(1, cards);
    }

    [Fact]
    public void ATabCountsAsTheColumnsTheReaderMovesOver()
    {
        // Ten tabs reach column 80 (each moves to the next multiple of 8), so one more letter is
        // column 81.
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("\t\t\t\t\t\t\t\t\tXXXXXXXX\n"), out _));
        string? problem = CardReader.Check(Encoding.ASCII.GetBytes("\t\t\t\t\t\t\t\t\t\tX\n"), out _);
        Assert.NotNull(problem);
        Assert.StartsWith("line 1 is longer", problem);
        Assert.Null(CardReader.Check(Encoding.ASCII.GetBytes("XXX\tX\n"), out _));
    }

    [Fact]
    public void BytesThatAreNotPlainTextAreRefused()
    {
        string? utf8 = CardReader.Check(Encoding.UTF8.GetBytes("//A JOB\n// HALLØ\n"), out _);
        Assert.NotNull(utf8);
        Assert.StartsWith("line 2 has the byte C3", utf8);

        string? ctrlZ = CardReader.Check(new byte[] { (byte)'A', 0x0A, 0x1A, 0x0A }, out _);
        Assert.NotNull(ctrlZ);
        Assert.StartsWith("line 2 has the byte 1A", ctrlZ);

        Assert.Equal("the file has no cards in it", CardReader.Check(Array.Empty<byte>(), out _));
    }

    [Fact]
    public async Task ALineTooLongIsRefusedBeforeAnythingIsSent()
    {
        byte[] file = Encoding.ASCII.GetBytes("//A JOB\n" + new string('X', 81) + "\n");
        using var reader = new StandInReader(StandInReader.Ending.CloseAfterTheDeck);
        (CommandResult result, _) = await SubmitAsync(file, reader.Port);

        Assert.False(result.Success);
        Assert.Contains("line 2 is longer than 80 columns", result.Error);
        Assert.Contains("nothing was sent", result.Error);
        Assert.False(reader.SomebodyConnected);
    }

    [Fact]
    public async Task WithNoCardReaderPortSubmitSaysWhereToSetIt()
    {
        byte[] file = Encoding.ASCII.GetBytes("//A JOB\n");
        (CommandResult result, _) = await SubmitAsync(file, port: null);

        Assert.False(result.Success);
        Assert.Equal(CardReader.NoReaderMessage, result.Error);
        Assert.Contains("Manage Connections", result.Error);
    }

    [Fact]
    public async Task AMissingFileIsRefused()
    {
        var session = NewSession(port: 1);
        string path = Path.Combine(Path.GetTempPath(), "RetroTermSubmit-" + Guid.NewGuid().ToString("N") + ".jcl");
        CommandResult result = await new SubmitCommand().ExecuteAsync(session, new CommandArgs().Set("path", path).Set("timeout", "2000"), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal("file not found: " + path, result.Error);
    }

    [Fact]
    public async Task AReaderThatResetsTheConnectionIsAFailure()
    {
        using var reader = new StandInReader(StandInReader.Ending.Reset);
        (CommandResult result, _) = await SubmitAsync(Encoding.ASCII.GetBytes("//A JOB\n"), reader.Port);

        Assert.False(result.Success);
        Assert.Contains("another program is still connected to the reader", result.Error);
    }

    [Fact]
    public async Task AReaderThatNeverClosesTimesOut()
    {
        using var reader = new StandInReader(StandInReader.Ending.Never);
        (CommandResult result, _) = await SubmitAsync(Encoding.ASCII.GetBytes("//A JOB\n"), reader.Port, timeoutMs: 500);

        Assert.False(result.Success);
        Assert.True(result.TimedOut);
        Assert.Contains("had not read them all after 500 ms", result.Error);
    }

    [Fact]
    public void TheReaderComesFromTheConnectionAndItsHostDefaultsToTheConnections()
    {
        var config = new HostConfiguration { Name = "tk5", Protocol = "TN3270", Host = "mvs.example", Port = 3270, CardReaderPort = 3505 };
        Assert.Equal(("mvs.example", (int?)3505), ConnectionFactory.CardReaderAddress(config.ToConnectionParameters()));

        config.CardReaderHost = " reader.example ";
        Assert.Equal(("reader.example", (int?)3505), ConnectionFactory.CardReaderAddress(config.ToConnectionParameters()));
        Assert.Equal(3505, config.Clone().CardReaderPort);
        Assert.Equal(" reader.example ", config.Clone().CardReaderHost);

        config.CardReaderPort = null;
        Assert.Equal((null, null), ConnectionFactory.CardReaderAddress(config.ToConnectionParameters()));
    }

    private static TerminalSession NewSession(int? port)
    {
        var session = new TerminalSession(EmulatorFactory.CreateEmulator("VT100", 80, 24), "Test");
        session.CardReaderHost = port == null ? null : "127.0.0.1";
        session.CardReaderPort = port;
        return session;
    }

    private static async Task<(CommandResult Result, string Path)> SubmitAsync(byte[] file, int? port, int timeoutMs = 10_000)
    {
        string path = Path.Combine(Path.GetTempPath(), "RetroTermSubmit-" + Guid.NewGuid().ToString("N") + ".jcl");
        File.WriteAllBytes(path, file);
        try
        {
            var session = NewSession(port);
            CommandResult result = await new SubmitCommand().ExecuteAsync(session,
                new CommandArgs().Set("path", path).Set("timeout", timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture)), TestContext.Current.CancellationToken);
            return (result, path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A one-connection TCP listener on 127.0.0.1 that behaves like the Hercules socket reader:
    // it reads until the client closes its side, then closes (or resets, or never closes).
    private sealed class StandInReader : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<byte[]> _received;

        public enum Ending
        {
            CloseAfterTheDeck,
            Reset,
            Never,
        }

        public StandInReader(Ending ending)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _received = Task.Run(() => ServeAsync(ending));
        }

        public int Port { get; }

        public bool SomebodyConnected { get; private set; }

        public async Task<byte[]> ReceivedAsync()
        {
            return await _received.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public void Dispose()
        {
            _listener.Stop();
        }

        private async Task<byte[]> ServeAsync(Ending ending)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync();
            SomebodyConnected = true;
            if (ending == Ending.Reset)
            {
                // What Hercules does to a second client: accepted, then closed at once. Linger
                // zero makes the close a reset.
                client.LingerState = new LingerOption(true, 0);
                client.Close();
                return Array.Empty<byte>();
            }

            var all = new MemoryStream();
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[256];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                all.Write(buffer, 0, read);
            }

            if (ending == Ending.Never)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
            }

            return all.ToArray();
        }
    }
}
