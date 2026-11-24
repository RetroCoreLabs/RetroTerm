//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using RetroIO.Protocols.Tn3270;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Ibm3270;
using RetroTerm.Tests.TDV;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3287 printer session: one file per print job, and the three things that end a job
    /// (telnet Abort Output, the connection closing, the quiet time). Time is moved by hand, never
    /// waited for.
    /// </summary>
    public sealed class Ibm3287PrinterEmulatorTests : IDisposable
    {
        private static readonly DateTime Start = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);

        // A folder of its own in %TEMP%, removed afterwards; never the user's Documents.
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "RetroTerm.Tests.Printer3287." + Guid.NewGuid().ToString("N"));

        private DateTime _now = Start;

        public void Dispose()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private Ibm3287PrinterEmulator Create(int? quietSeconds = null)
        {
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(new TextPrintJobOutput(_folder), quietSeconds);
            printer.ConnectionStatusChanged(ConnectionStatus.Connected);
            return printer;
        }

        private static byte[] PrintLine(string text) =>
            Printer3287Tests.Record(Command3270.EraseWrite, 0x08, text, (byte)0x19);

        private string[] Files()
        {
            if (!Directory.Exists(_folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(_folder);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }

        [Fact]
        public void AbortOutputFromTheHostEndsTheJobAndEachJobIsItsOwnFile()
        {
            Ibm3287PrinterEmulator printer = Create();

            printer.ProcessData(PrintLine("FIRST"));
            printer.EndOfJobReceived();
            printer.ProcessData(PrintLine("SECOND"));
            printer.EndOfJobReceived();

            string[] files = Files();
            Assert.Equal(2, files.Length);
            Assert.Equal("FIRST" + Environment.NewLine, File.ReadAllText(files[0]));
            Assert.Equal("SECOND" + Environment.NewLine, File.ReadAllText(files[1]));
            Assert.Equal(2, printer.Jobs.Count);
            Assert.Equal(files[0], printer.Jobs[0].Result);
            Assert.EndsWith("-001.txt", files[0], StringComparison.Ordinal);
            Assert.EndsWith("-002.txt", files[1], StringComparison.Ordinal);
        }

        [Fact]
        public void AnEndOfJobWithNothingPrintedWritesNoFile()
        {
            Ibm3287PrinterEmulator printer = Create();

            printer.EndOfJobReceived();

            Assert.Empty(Files());
            Assert.Empty(printer.Jobs);
        }

        [Fact]
        public void TheQuietTimeEndsTheJobAtFiveSecondsAndNotBefore()
        {
            Ibm3287PrinterEmulator printer = Create();
            printer.ProcessData(PrintLine("QUIET"));

            Assert.False(printer.CheckQuietTime(Start.AddMilliseconds(4999)));
            Assert.Empty(Files());

            Assert.True(printer.CheckQuietTime(Start.AddSeconds(5)));
            Assert.Single(Files());
            Assert.Equal("5 seconds with no data", printer.Jobs[0].EndedBy);
        }

        [Fact]
        public void TheQuietTimeCountsFromTheLastData()
        {
            Ibm3287PrinterEmulator printer = Create();
            printer.ProcessData(PrintLine("ONE"));
            _now = Start.AddSeconds(3);
            printer.ProcessData(Printer3287Tests.Record(Command3270.Write, 0x08));

            Assert.False(printer.CheckQuietTime(Start.AddSeconds(6)));
            Assert.True(printer.CheckQuietTime(Start.AddSeconds(8)));
            Assert.Single(Files());
        }

        [Fact]
        public void TheQuietTimeComesFromTheConnectionAndZeroTurnsItOff()
        {
            Ibm3287PrinterEmulator printer = Create(quietSeconds: 0);
            printer.ProcessData(PrintLine("NEVER BY TIME"));

            Assert.False(printer.CheckQuietTime(Start.AddHours(1)));
            Assert.Empty(Files());

            Ibm3287PrinterEmulator two = Create(quietSeconds: 2);
            two.ProcessData(PrintLine("TWO"));
            Assert.True(two.CheckQuietTime(Start.AddSeconds(2)));
        }

        [Fact]
        public void DataWithoutStartPrintIsNotAJob()
        {
            Ibm3287PrinterEmulator printer = Create();
            printer.ProcessData(Printer3287Tests.Record(Command3270.EraseWrite, 0x30, "BUFFER ONLY"));

            Assert.False(printer.CheckQuietTime(Start.AddMinutes(1)));
            Assert.Empty(Files());
        }

        [Fact]
        public void TheConnectionClosingEndsTheJob()
        {
            Ibm3287PrinterEmulator printer = Create();
            printer.ProcessData(PrintLine("BYE"));

            printer.ConnectionStatusChanged(ConnectionStatus.Disconnected);

            Assert.Single(Files());
            Assert.Equal("the connection closed", printer.Jobs[0].EndedBy);
        }

        [Fact]
        public void AJobThatCannotBeWrittenIsListedAsFailedWithTheReason()
        {
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(new FailingOutput(), null);
            printer.ProcessData(PrintLine("LOST"));

            printer.EndOfJobReceived();

            Assert.Single(printer.Jobs);
            Assert.True(printer.Jobs[0].Failed);
            Assert.Equal("disk full", printer.Jobs[0].Result);
            Assert.Contains("disk full", printer.LastProblem, StringComparison.Ordinal);
        }

        [Fact]
        public void AFailedJobMakesThePrinterNotReadyAndIsWrittenWhenTheFolderWorksAgain()
        {
            var output = new SwitchableOutput(_folder) { Fails = true };
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(output, 0);
            printer.ConnectionStatusChanged(ConnectionStatus.Connected);

            printer.ProcessData(PrintLine("FIRST"));
            printer.EndOfJobReceived();
            Assert.False(printer.IsReady);
            Assert.Contains("job 1 was not written: folder is read-only", printer.NotReadyReason, StringComparison.Ordinal);
            Assert.Contains("NOT READY, 1 job waiting", ScreenText(printer), StringComparison.Ordinal);

            // Plain TN3270 cannot tell the host, so the next job is still taken, and waits
            // behind the first one.
            printer.ProcessData(PrintLine("SECOND"));
            Assert.Equal(HostRecordOutcome.Applied, printer.LastRecordOutcome);
            printer.EndOfJobReceived();
            Assert.Equal(2, printer.Jobs.Count);
            Assert.True(printer.Jobs[1].Failed);
            Assert.Equal(1, output.Attempts);

            // Fixed, but the retry waits its 10 seconds.
            output.Fails = false;
            _now = Start.AddSeconds(Ibm3287PrinterEmulator.NotReadyRetrySeconds - 1);
            Assert.False(printer.RetryWhenNotReady(_now));
            Assert.Empty(Files());

            _now = Start.AddSeconds(Ibm3287PrinterEmulator.NotReadyRetrySeconds);
            Assert.True(printer.RetryWhenNotReady(_now));
            Assert.True(printer.IsReady);
            Assert.Null(printer.NotReadyReason);
            Assert.Null(printer.LastProblem);
            string[] files = Files();
            Assert.Equal(2, files.Length);
            Assert.Equal("FIRST" + Environment.NewLine, File.ReadAllText(files[0]));
            Assert.Equal("SECOND" + Environment.NewLine, File.ReadAllText(files[1]));
            Assert.False(printer.Jobs[0].Failed);
            Assert.False(printer.Jobs[1].Failed);
            Assert.DoesNotContain("NOT READY", ScreenText(printer), StringComparison.Ordinal);
        }

        [Fact]
        public void WithResponsesANotReadyPrinterRefusesRecordsAndSaysWhenItIsReady()
        {
            var output = new SwitchableOutput(_folder) { Fails = true };
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(output, 0);
            printer.ConnectionStatusChanged(ConnectionStatus.Connected);
            printer.Tn3270EChanged(true, "PRT0001", bindImage: false, sysReq: false, responses: true);
            var raised = new List<Tn3270EKey>();
            printer.Tn3270EKeyPressed += raised.Add;

            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC8, 0xC9 });
            Assert.Equal(HostRecordOutcome.Applied, printer.LastRecordOutcome);
            printer.EndOfJobReceived();
            Assert.False(printer.IsReady);

            // Not ready, and the host asked to hear (ERROR-RESPONSE, ALWAYS-RESPONSE): the record
            // is refused, unprinted, as Intervention Required (GA23-0061-2 p. 4-26, "The printout
            // is suppressed").
            printer.HostRecordResponseRequested(HostRecordResponseRequest.ErrorResponse);
            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC1 });
            Assert.Equal(HostRecordOutcome.InterventionRequired, printer.LastRecordOutcome);
            Assert.Equal(0, printer.Printer.PendingBytes);
            printer.HostRecordResponseRequested(HostRecordResponseRequest.AlwaysResponse);
            printer.ProcessData(PrintLine("REFUSED"));
            Assert.Equal(HostRecordOutcome.InterventionRequired, printer.LastRecordOutcome);
            Assert.Empty(raised);

            output.Fails = false;
            _now = Start.AddSeconds(Ibm3287PrinterEmulator.NotReadyRetrySeconds);
            Assert.True(printer.RetryWhenNotReady(_now));
            Assert.Equal(new[] { Tn3270EKey.ErrorConditionCleared }, raised.ToArray());
            Assert.Equal("HI" + Environment.NewLine, File.ReadAllText(Assert.Single(Files())));

            // Ready: records print again.
            printer.ProcessData(PrintLine("AFTER"));
            Assert.Equal(HostRecordOutcome.Applied, printer.LastRecordOutcome);
            Assert.True(printer.Printer.PendingBytes > 0);
        }

        [Fact]
        public void ANoResponseRecordWhileNotReadyIsKeptAsAWaitingJobNotRefused()
        {
            // RFC 2355 8.1.3, NO-RESPONSE: "The receiver must therefore not send any response to
            // this data-message." A refusal would never reach the host, so the record is printed
            // into a job that waits behind the one that could not be written.
            var output = new SwitchableOutput(_folder) { Fails = true };
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(output, 0);
            printer.ConnectionStatusChanged(ConnectionStatus.Connected);
            printer.Tn3270EChanged(true, "PRT0001", bindImage: false, sysReq: false, responses: true);
            var raised = new List<Tn3270EKey>();
            printer.Tn3270EKeyPressed += raised.Add;

            printer.HostRecordResponseRequested(HostRecordResponseRequest.NoResponse);
            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC8, 0xC9 });
            printer.EndOfJobReceived();
            Assert.False(printer.IsReady);

            // Not ready, NO-RESPONSE: taken, not refused.
            printer.HostRecordResponseRequested(HostRecordResponseRequest.NoResponse);
            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC2, 0xD6 });
            Assert.Equal(HostRecordOutcome.Applied, printer.LastRecordOutcome);
            Assert.True(printer.Printer.PendingBytes > 0);
            printer.EndOfJobReceived();
            Assert.Equal(2, printer.Jobs.Count);
            Assert.True(printer.Jobs[1].Failed);
            Assert.Equal("waiting: the printer is not ready", printer.Jobs[1].Result);

            // The flag is used for one record only: the next record without a flag from the
            // session is NO-RESPONSE too, never the flag of an earlier record.
            printer.HostRecordResponseRequested(HostRecordResponseRequest.AlwaysResponse);
            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC1 });
            Assert.Equal(HostRecordOutcome.InterventionRequired, printer.LastRecordOutcome);
            printer.Tn3270EHostMessageReceived(Tn3270EHostMessage.ScsData, new byte[] { 0xC5 });
            Assert.Equal(HostRecordOutcome.Applied, printer.LastRecordOutcome);
            printer.EndOfJobReceived();

            // Ready again: every waiting job is written, in order.
            output.Fails = false;
            _now = Start.AddSeconds(Ibm3287PrinterEmulator.NotReadyRetrySeconds);
            Assert.True(printer.RetryWhenNotReady(_now));
            string[] files = Files();
            Array.Sort(files, StringComparer.Ordinal);
            Assert.Equal(3, files.Length);
            Assert.Equal("HI" + Environment.NewLine, File.ReadAllText(files[0]));
            Assert.Equal("BO" + Environment.NewLine, File.ReadAllText(files[1]));
            Assert.Equal("E" + Environment.NewLine, File.ReadAllText(files[2]));
            Assert.Equal(new[] { Tn3270EKey.ErrorConditionCleared }, raised.ToArray());
        }

        [Fact]
        public void TheStatusScreenListsTheJobFile()
        {
            Ibm3287PrinterEmulator printer = Create();
            printer.ProcessData(PrintLine("SHOWN"));
            printer.EndOfJobReceived();

            string screen = ScreenText(printer);
            Assert.Contains("Connection: connected", screen, StringComparison.Ordinal);
            Assert.Contains("Jobs printed: 1", screen, StringComparison.Ordinal);
            Assert.Contains("-001.txt", screen, StringComparison.Ordinal);
        }

        [Fact]
        public void TelnetAbortOutputInPlainTn3270RaisesTheEndOfJob()
        {
            int ends = 0;
            var errors = new List<string>();
            var protocol = new Tn3270Protocol(_ => { }, Ibm3287PrinterEmulator.TerminalType);
            protocol.AbortOutputReceived += () => ends++;
            protocol.ProtocolError += errors.Add;

            // The TK5 handshake (docs\spec\IBM3270\TN3270-NEGOTIATION.md section 3), a record, then
            // IAC AO.
            protocol.Feed(new byte[]
            {
                0xFF, 0xFD, 0x18, 0xFF, 0xFA, 0x18, 0x01, 0xFF, 0xF0,
                0xFF, 0xFB, 0x00, 0xFF, 0xFD, 0x00, 0xFF, 0xFB, 0x19, 0xFF, 0xFD, 0x19,
                0xF5, 0x08, 0xC1, 0x19, 0xFF, 0xEF,
                0xFF, 0xF5,
            });

            Assert.Equal(1, ends);
            Assert.Empty(errors);
        }

        [Fact]
        public async Task TheSessionHandsAbortOutputToThePrinterAfterTheDataBeforeIt()
        {
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(new TextPrintJobOutput(_folder), null);
            var session = new TerminalSession(printer, "Test");
            var connection = new PrinterConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

            connection.SimulateReceive(PrintLine("ORDERED"));
            connection.RaiseEndOfJob();
            await session.FlushAsync();

            Assert.Single(Files());
            Assert.Equal("ORDERED" + Environment.NewLine, File.ReadAllText(Files()[0]));
            await session.DisconnectAsync();
        }

        [Fact]
        public async Task DisconnectingTheSessionWritesTheJobInProgress()
        {
            // TK5 sends nothing at the end of a JRP job (measured 23 November 2026,
            // Tk5PrinterTests), so a job can still be open when the user closes the connection;
            // closing must write it, through the real session as the app closes it.
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(new TextPrintJobOutput(_folder), 0);
            var session = new TerminalSession(printer, "Test");
            var connection = new PrinterConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);

            connection.SimulateReceive(PrintLine("OPEN AT CLOSE"));
            await session.FlushAsync();
            Assert.Empty(Files());

            await session.DisconnectAsync();
            await session.FlushAsync();

            Assert.Single(Files());
            Assert.Equal("OPEN AT CLOSE" + Environment.NewLine, File.ReadAllText(Files()[0]));
            Assert.Equal("the connection closed", printer.Jobs[0].EndedBy);
            session.Dispose();
        }

        [Fact]
        public async Task ClosingTheTabWritesTheJobInProgress()
        {
            // Closing a tab disconnects and disposes the session at once. The job end is queued on
            // the session thread by the disconnect; the dispose must still let it run.
            var printer = new Ibm3287PrinterEmulator(() => _now);
            printer.Configure(new TextPrintJobOutput(_folder), 0);
            var session = new TerminalSession(printer, "Test");
            var connection = new PrinterConnection();
            await session.ConnectAsync(connection, TestContext.Current.CancellationToken);
            connection.SimulateReceive(PrintLine("OPEN AT DISPOSE"));
            await session.FlushAsync();

            await session.DisconnectAsync();
            session.Dispose();

            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (Files().Length == 0 && clock.ElapsedMilliseconds < 5000)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.Single(Files());
        }

        [Fact]
        public void TheFactoryMakesThePrinterForIbm3287()
        {
            Assert.Equal(ConnectionFactory.Ibm3287, ConnectionFactory.EmulatorTypeFor(ConnectionFactory.ProtocolType.TN3270, "ibm-3287-1"));
            Assert.IsType<Ibm3287PrinterEmulator>(EmulatorFactory.CreateEmulator(ConnectionFactory.Ibm3287));

            IConnection connection = ConnectionFactory.CreateConnection(new ConnectionFactory.ConnectionParameters
            {
                Protocol = ConnectionFactory.ProtocolType.TN3270,
                Host = "localhost",
                Port = 3270,
                EmulatorType = ConnectionFactory.Ibm3287,
            });
            Tn3270Connection tn3270 = Assert.IsType<Tn3270Connection>(connection);
            Assert.Equal("IBM-3287-1", tn3270.TerminalType);
            connection.Dispose();
        }

        private static string ScreenText(Ibm3287PrinterEmulator printer)
        {
            var text = new System.Text.StringBuilder();
            for (int row = 0; row < printer.Buffer.Height; row++)
            {
                for (int column = 0; column < printer.Buffer.Width; column++)
                {
                    text.Append((char)printer.Buffer.GetCell(row, column).Codepoint);
                }

                text.Append('\n');
            }

            return text.ToString();
        }

        private sealed class FailingOutput : IPrintJobOutput
        {
            public string Description => "nowhere";

            public string Write(PrintJob job, int number, DateTime localTime) => throw new IOException("disk full");
        }

        // Text files in the test folder, or a refusal while Fails is set (a read-only folder).
        private sealed class SwitchableOutput : IPrintJobOutput
        {
            private readonly TextPrintJobOutput _text;

            public SwitchableOutput(string folder)
            {
                _text = new TextPrintJobOutput(folder);
            }

            public bool Fails { get; set; }

            public int Attempts { get; private set; }

            public string Description => _text.Description;

            public string Write(PrintJob job, int number, DateTime localTime)
            {
                Attempts++;
                if (Fails)
                {
                    throw new UnauthorizedAccessException("folder is read-only");
                }

                return _text.Write(job, number, localTime);
            }
        }

        private sealed class PrinterConnection : InMemoryConnection, IPrintJobEndConnection
        {
            public event Action? EndOfJobReceived;

            public void RaiseEndOfJob() => EndOfJobReceived?.Invoke();
        }
    }
}
