using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop.Controls;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The window drives how many columns and rows the terminal has, and a wrapped paragraph is
/// re-laid out at the new width rather than cut off.
/// </summary>
/// <remarks>
/// <para><b>What this replaced</b></para>
/// The terminal used to be a fixed grid that was SCALED to fit the window, with letterbox bars
/// down the sides. A maximised window ran an 80 by 24 program at 80 by 24, very large. Ronny asked
/// for the window to decide the size instead, and for wrapped paragraphs to reflow rather than be
/// truncated.
/// The reflow itself was already built - <c>TerminalBuffer.ResizeWithReflow</c> - and had no caller
/// anywhere in the UI. What was missing was everything between a window edge being dragged and that
/// method running.
/// </remarks>
[Collection("Avalonia")]
public class WindowDrivesTerminalSizeTests
{
    /// <summary>
    /// Lays a canvas out at a given pixel size and returns every size it asked for.
    /// </summary>
    /// <param name="canvas">
    /// The canvas under test.
    /// </param>
    /// <param name="width">
    /// Width in pixels to arrange at.
    /// </param>
    /// <param name="height">
    /// Height in pixels to arrange at.
    /// </param>
    /// <returns>
    /// The requested sizes, in the order they were asked for.
    /// </returns>
    private static List<(int Columns, int Rows)> ArrangeAt(TerminalCanvas canvas, double width, double height)
    {
        var asked = new List<(int, int)>();
        void Record(int columns, int rows) => asked.Add((columns, rows));

        canvas.TerminalResizeRequested += Record;
        try
        {
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
        }
        finally
        {
            canvas.TerminalResizeRequested -= Record;
        }

        return asked;
    }

    [AvaloniaFact]
    public void TheCanvasAsksForAsManyWholeCellsAsFit()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        // Measure one cell through the same renderer the canvas uses, so this test cannot drift
        // away from the production font metrics.
        var oneCell = ArrangeAt(canvas, 1000, 600);
        Assert.NotEmpty(oneCell);

        var (columns, rows) = oneCell[oneCell.Count - 1];

        // Whatever the font is, the answer must be the number of WHOLE cells that fit - never more,
        // or the last column is drawn off the edge.
        Assert.True(columns > 0 && rows > 0);
        Assert.True(columns * CellWidth(canvas) <= 1000 + 0.001,
            $"{columns} columns do not fit in 1000 pixels");
        Assert.True(rows * CellHeight(canvas) <= 600 + 0.001,
            $"{rows} rows do not fit in 600 pixels");

        // And one more would not fit.
        Assert.True((columns + 1) * CellWidth(canvas) > 1000);
        Assert.True((rows + 1) * CellHeight(canvas) > 600);
    }

    [AvaloniaFact]
    public void ADoubledWidthAsksForAboutTwiceAsManyColumns()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        var narrow = ArrangeAt(canvas, 500, 600);
        int narrowColumns = narrow[narrow.Count - 1].Columns;

        // The emulator has not actually resized - nothing wired it to a session here - so the canvas
        // still compares against 80 by 24 and will ask again.
        var wide = ArrangeAt(canvas, 1000, 600);
        int wideColumns = wide[wide.Count - 1].Columns;

        Assert.True(wideColumns > narrowColumns);
        Assert.InRange(wideColumns, narrowColumns * 2 - 2, narrowColumns * 2 + 2);
    }

    [AvaloniaFact]
    public void AWindowDraggedToNothingStillAsksForAUsableTerminal()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        var asked = ArrangeAt(canvas, 3, 3);
        Assert.NotEmpty(asked);

        var (columns, rows) = asked[asked.Count - 1];

        // A one-column terminal would reflow the whole history into a vertical ribbon on the way
        // down and never recover it on the way back out.
        Assert.True(columns >= 20, $"asked for {columns} columns");
        Assert.True(rows >= 4, $"asked for {rows} rows");
    }

    [AvaloniaFact]
    public void AskingForTheSizeItAlreadyIsSaysNothing()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);

        var canvas = new TerminalCanvas();
        canvas.SetEmulator(emulator);

        // Find the pixel size that gives exactly the 80 by 24 the emulator already is.
        double width = 80 * CellWidth(canvas);
        double height = 24 * CellHeight(canvas);

        var asked = ArrangeAt(canvas, width, height);

        Assert.Empty(asked);
    }

    [AvaloniaFact]
    public async Task TheSessionResizesTheEmulatorAndReflowsAWrappedParagraph()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 40, 10, 100);
        using var session = new TerminalSession(emulator, "resize");

        // A paragraph long enough to wrap at 40 columns, sent as ONE line so the terminal wraps it
        // itself. That is what makes it a paragraph rather than two lines, and the wrap flag is the
        // only record of the difference.
        string paragraph = new string('a', 30) + new string('b', 30);
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(paragraph));

        // At 40 columns the first row holds all thirty a's and the first ten b's; the paragraph
        // carries on onto the second row, which is what the wrap flag records.
        Assert.Equal(40, emulator.Width);
        Assert.Equal((uint)'a', emulator.Buffer[0, 0].Codepoint);
        Assert.Equal((uint)'a', emulator.Buffer[0, 29].Codepoint);
        Assert.Equal((uint)'b', emulator.Buffer[0, 30].Codepoint);
        Assert.Equal((uint)'b', emulator.Buffer[1, 0].Codepoint);

        // Widen. The paragraph must re-lay out, not stay broken where it happened to break.
        await session.ResizeAsync(80, 10);

        Assert.Equal(80, emulator.Width);
        Assert.Equal(10, emulator.Height);

        // All sixty characters now fit on one line, in order, with nothing lost at the old edge.
        for (int col = 0; col < 30; col++)
        {
            Assert.Equal((uint)'a', emulator.Buffer[0, col].Codepoint);
        }

        for (int col = 30; col < 60; col++)
        {
            Assert.Equal((uint)'b', emulator.Buffer[0, col].Codepoint);
        }
    }

    [AvaloniaFact]
    public async Task AParagraphSurvivesWideThenNarrowThenWideAgain()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 10, 200);
        using var session = new TerminalSession(emulator, "round trip");

        string paragraph = new string('x', 70);
        emulator.ProcessData(System.Text.Encoding.ASCII.GetBytes(paragraph));

        await session.ResizeAsync(40, 10);
        await session.ResizeAsync(80, 10);

        // Seventy characters, back on one line, none of them lost on the way through 40 columns.
        int found = 0;
        for (int col = 0; col < 80; col++)
        {
            if (emulator.Buffer[0, col].Codepoint == 'x') found++;
        }

        Assert.Equal(70, found);
    }

    [AvaloniaFact]
    public async Task TheHostIsToldTheNewSize()
    {
        var emulator = (TerminalEmulatorBase)EmulatorFactory.CreateEmulator("VT100", 80, 24, 100);
        using var session = new TerminalSession(emulator, "naws");

        var connection = new SizeRecordingConnection();
        await session.ConnectAsync(connection);

        await session.ResizeAsync(132, 50);

        // The first entry is the emulator's own size, given to the connection before it opened
        // (TerminalSession.ConnectAsync, 27 September 2026); the second is the resize.
        Assert.Equal(new List<(int, int)> { (80, 24), (132, 50) }, connection.Sizes);
    }

    /// <summary>
    /// One cell's width, measured through the canvas's own renderer.
    /// </summary>
    /// <param name="canvas">
    /// The canvas to ask.
    /// </param>
    /// <returns>
    /// Width in pixels.
    /// </returns>
    private static double CellWidth(TerminalCanvas canvas) => canvas.GetRenderer()!.GetCharWidth();

    /// <summary>
    /// See <see cref="CellWidth"/>.
    /// </summary>
    /// <param name="canvas">
    /// The canvas to ask.
    /// </param>
    /// <returns>
    /// Height in pixels.
    /// </returns>
    private static double CellHeight(TerminalCanvas canvas) => canvas.GetRenderer()!.GetCharHeight();
}

/// <summary>
/// A connection that goes nowhere and writes down every size it is told about.
/// </summary>
/// <remarks>
/// The point of the resize work is that the far end HEARS about it. A test double that records the
/// call is the only way to check that without a real host, and it also proves the default interface
/// method is being overridden rather than silently doing nothing.
/// </remarks>
internal sealed class SizeRecordingConnection : RetroTerm.Core.Protocols.IConnection
{
    /// <summary>
    /// Every size this connection has been told, in order.
    /// </summary>
    public List<(int Columns, int Rows)> Sizes { get; } = new List<(int, int)>();

    public RetroTerm.Core.Protocols.ConnectionStatus Status { get; private set; }
        = RetroTerm.Core.Protocols.ConnectionStatus.Disconnected;

    public string ConnectionType => "Recording";

    public string Description => "records window sizes";

    public event Action<RetroTerm.Core.Protocols.ConnectionStatus>? StatusChanged;

    /// <summary>
    /// Never raised - this connection delivers nothing. The empty accessors say so, rather than
    /// leaving a field the compiler correctly points out is never used.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? DataReceived
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Never raised; see <see cref="DataReceived"/>.
    /// </summary>
    public event Action<Exception>? ErrorOccurred
    {
        add { }
        remove { }
    }

    public Task ConnectAsync(System.Threading.CancellationToken cancellationToken = default)
    {
        Status = RetroTerm.Core.Protocols.ConnectionStatus.Connected;
        StatusChanged?.Invoke(Status);
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, System.Threading.CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task DisconnectAsync()
    {
        Status = RetroTerm.Core.Protocols.ConnectionStatus.Disconnected;
        StatusChanged?.Invoke(Status);
        return Task.CompletedTask;
    }

    public Task ResizeTerminalAsync(int columns, int rows,
        System.Threading.CancellationToken cancellationToken = default)
    {
        Sizes.Add((columns, rows));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Nothing to release. Declared only because IConnection is IDisposable.
    }
}
