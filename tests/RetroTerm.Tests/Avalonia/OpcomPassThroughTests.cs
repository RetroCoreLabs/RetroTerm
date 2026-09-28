using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Pass-through is ticked when the OPCOM window opens, so what the ND sends must reach the
/// terminal screen from the very first byte.
///
/// Ronny reported on 8 September 2026 that it does not: nothing passes through until the
/// box is unticked and ticked again. Toggling it off and on ends on the value it already
/// had, so whatever is broken cannot be the flag by itself - which is why this is a test
/// rather than a guess.
/// </summary>
[Collection("Avalonia")]
public class OpcomPassThroughTests
{
    private readonly ITestOutputHelper _output;

    public OpcomPassThroughTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// A connection that does nothing except let a test push bytes in, the way a serial
    /// port or a socket would.
    /// </summary>
    private sealed class FakeConnection : IConnection
    {
        public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
        public string ConnectionType => "Fake";
        public string Description => "fake connection";

        public event Action<ConnectionStatus>? StatusChanged;
        public event Action<ReadOnlyMemory<byte>>? DataReceived;
#pragma warning disable CS0067
        public event Action<Exception>? ErrorOccurred;
#pragma warning restore CS0067

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            Status = ConnectionStatus.Connected;
            StatusChanged?.Invoke(Status);
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DisconnectAsync()
        {
            Status = ConnectionStatus.Disconnected;
            StatusChanged?.Invoke(Status);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        /// <summary>Pushes bytes at the session exactly as a real transport would.</summary>
        public void Receive(string text) => DataReceived?.Invoke(Encoding.ASCII.GetBytes(text));
    }

    private static string ScreenOf(TerminalEmulatorBase emulator)
    {
        var buffer = emulator.GetBuffer();
        var sb = new StringBuilder();
        for (int row = 0; row < emulator.Height; row++)
        {
            for (int col = 0; col < emulator.Width; col++)
            {
                if (buffer.TryGetCell(row, col, out var cell) && cell.Codepoint >= 32)
                {
                    sb.Append((char)cell.Codepoint);
                }
                else
                {
                    sb.Append(' ');
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Gives the session pump time to drain, since it runs on its own thread.
    /// </summary>
    private static void Settle() => Thread.Sleep(400);

    [AvaloniaFact]
    public void WhatTheMachineSendsReachesTheScreenWithoutTouchingTheCheckbox()
    {
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "PassThroughTest");
        var connection = new FakeConnection();
        session.ConnectAsync(connection).GetAwaiter().GetResult();

        var window = new OpcomDebugWindow();
        window.Initialize(session);

        var box = window.FindControl<CheckBox>("PassThroughCheckBox")!;
        Assert.True(box.IsChecked == true, "the checkbox should start ticked");

        var protocol = session.ActiveOpcomHandler as OpcomProtocol;
        Assert.NotNull(protocol);
        _output.WriteLine($"PassThrough={protocol!.PassThrough}  IsActive={protocol.IsActive}");

        connection.Receive("HELLO");
        Settle();

        string screen = ScreenOf(emulator);
        Assert.True(screen.Contains("HELLO"),
            "the ND's output never reached the terminal even though pass-through is ticked");
    }

    /// <summary>
    /// The master clear button says MACL, because that is the command. "MCL" is echoed by
    /// the machine and then rejected with a question mark, so a button labelled MCL names
    /// something that does not work - measured on the ND-120/CX on 6 September 2026.
    /// </summary>
    [AvaloniaFact]
    public void TheMasterClearButtonIsLabelledWithTheCommandThatActuallyWorks()
    {
        var window = new OpcomDebugWindow();
        var button = window.FindControl<Button>("MclButton")!;
        Assert.Equal("MACL", button.Content?.ToString());
    }

    [AvaloniaFact]
    public void SwitchingPassThroughOffBehindTheUsersBackUntucksTheBox()
    {
        // An upload and the NDBoot monitor check both turn pass-through off for their
        // duration. The window must SHOW that, or it goes on claiming pass-through is on
        // while nothing passes through - which is the state Ronny found it in.
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "PassThroughHonestyTest");

        var window = new OpcomDebugWindow();
        window.Initialize(session);

        var box = window.FindControl<CheckBox>("PassThroughCheckBox")!;
        var protocol = session.ActiveOpcomHandler as OpcomProtocol;
        Assert.NotNull(protocol);

        Assert.True(box.IsChecked == true && protocol!.PassThrough,
            "the window should start with the box ticked and the protocol agreeing");

        window.SetPassThroughForTest(false);
        Assert.False(protocol!.PassThrough, "the protocol was not switched off");
        Assert.False(box.IsChecked == true,
            "the protocol has pass-through off but the box still shows it ticked");

        window.SetPassThroughForTest(true);
        Assert.True(protocol.PassThrough);
        Assert.True(box.IsChecked == true, "the box was not put back when pass-through returned");
    }

    [AvaloniaFact]
    public void TheCheckboxAndTheProtocolAgreeAsSoonAsTheWindowIsInitialised()
    {
        // The window wires the checkbox in its CONSTRUCTOR, where the protocol does not
        // exist yet, and creates the protocol later in Initialize. If Initialize does not
        // carry the checkbox's value across, the two start out disagreeing and only the
        // first toggle brings them together.
        var emulator = new VT100Emulator(80, 24);
        using var session = new TerminalSession(emulator, "PassThroughAgreeTest");

        var window = new OpcomDebugWindow();
        var box = window.FindControl<CheckBox>("PassThroughCheckBox")!;
        box.IsChecked = false;              // as if it were unticked before connecting

        window.Initialize(session);

        var protocol = session.ActiveOpcomHandler as OpcomProtocol;
        Assert.NotNull(protocol);
        Assert.False(protocol!.PassThrough,
            "the window opened with pass-through unticked but the protocol still has it on -"
            + " the checkbox and the protocol only agree after the first toggle");
    }
}
