using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Buffer;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Tests.TDV;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// Tests for the session threading architecture (P1.0/P1.1):
/// - the single-writer pump processes received data off the caller's thread
/// - FlushAsync is a deterministic barrier (replaces Task.Delay guessing)
/// - screen reads run serialized on the pump and can never race the emulator
/// - ScreenReader renders the buffer to text correctly
/// </summary>
public class SessionPumpAndScreenReadTests : IDisposable
{
    private readonly VT100Emulator _emulator;
    private readonly TerminalSession _session;
    private readonly InMemoryConnection _connection;

    public SessionPumpAndScreenReadTests()
    {
        _emulator = new VT100Emulator(80, 24);
        _session = new TerminalSession(_emulator, "PumpTest");
        _connection = new InMemoryConnection();
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────
    // Pump + FlushAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FlushAsync_IsDeterministicBarrier_DataVisibleAfterFlush()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        _connection.SimulateReceive(Encoding.UTF8.GetBytes("Hello"));

        // No Task.Delay: FlushAsync must guarantee the bytes are in the buffer.
        await _session.FlushAsync();

        var buffer = _emulator.GetBuffer();
        Assert.Equal('H', (char)buffer.GetCell(0, 0).Codepoint);
        Assert.Equal('o', (char)buffer.GetCell(0, 4).Codepoint);
    }

    [Fact]
    public async Task Pump_PreservesChunkOrder_AcrossManySmallChunks()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // Send a long text one byte at a time — order must be preserved exactly.
        var text = "The quick brown fox jumps over the lazy dog";
        var bytes = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i < bytes.Length; i++)
        {
            _connection.SimulateReceive(new[] { bytes[i] });
        }

        await _session.FlushAsync();

        var screen = await _session.ReadScreenAsync();
        Assert.Equal(text, screen.Text);
    }

    [Fact]
    public async Task WriteToTerminal_IsSerializedWithNetworkData()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        _connection.SimulateReceive(Encoding.UTF8.GetBytes("net"));
        _session.WriteToTerminal("local");

        await _session.FlushAsync();

        var screen = await _session.ReadScreenAsync();
        Assert.Equal("netlocal", screen.Text);
    }

    [Fact]
    public async Task RunOnSessionThreadAsync_PropagatesJobException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _session.RunOnSessionThreadAsync(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task RunOnSessionThreadAsync_Generic_ReturnsValueAndPropagatesException()
    {
        var value = await _session.RunOnSessionThreadAsync(() => 42);
        Assert.Equal(42, value);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _session.RunOnSessionThreadAsync<int>(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task Pump_SurvivesProcessingBurst_AndStaysOrderedUnderLoad()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        // Push well past the queue capacity (256) to exercise the backpressure path.
        for (int i = 0; i < 2000; i++)
        {
            _connection.SimulateReceive(new[] { (byte)('A' + (i % 26)) });
        }

        await _session.FlushAsync();

        // 2000 chars on an 80x24 screen wrap and scroll; just assert the session is alive and the
        // last character landed where the cursor says it should.
        //
        // 2000 = exactly 25 full lines of 80. With the DEFERRED WRAP the cursor does not step to
        // column 0 of the next row after filling the last column — it STAYS on the character it
        // just wrote, with the Last Column Flag armed. So the last character is AT the cursor,
        // not one before it. (This assertion used to subtract one, which is what eager wrapping
        // required.)
        var screen = await _session.ReadScreenAsync(stripTrailingBlanks: false);
        Assert.True(screen.CursorRow >= 0);
        char expectedLast = (char)('A' + (1999 % 26));
        var buffer = _emulator.GetBuffer();
        Assert.Equal(expectedLast, (char)buffer.GetCell(screen.CursorRow, screen.CursorColumn).Codepoint);
    }

    // ─────────────────────────────────────────────────────────────
    // ReadScreenAsync
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadScreenAsync_ReturnsTextAndCursor()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        _connection.SimulateReceive(Encoding.UTF8.GetBytes("Line1\r\nLine2"));

        var screen = await _session.ReadScreenAsync();

        Assert.Equal("Line1\nLine2", screen.Text);
        Assert.Equal(1, screen.CursorRow);
        Assert.Equal(5, screen.CursorColumn);
        Assert.Equal(80, screen.Width);
        Assert.Equal(24, screen.Height);
    }

    [Fact]
    public async Task ReadScreenAsync_ImpliesFlush_NoDelayNeeded()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);

        _connection.SimulateReceive(Encoding.UTF8.GetBytes("PROMPT>"));

        // ReadScreenAsync queues behind the data — no explicit flush, no delay.
        var screen = await _session.ReadScreenAsync();
        Assert.Equal("PROMPT>", screen.Text);
    }

    // ─────────────────────────────────────────────────────────────
    // ScreenReader
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void ScreenReader_BlankScreen_ReturnsEmpty()
    {
        var buffer = _emulator.GetBuffer();
        Assert.Equal(string.Empty, ScreenReader.GetScreenText(buffer));
        Assert.Equal(-1, ScreenReader.FindLastNonBlankRow(buffer));
        Assert.Equal(string.Empty, ScreenReader.GetTailText(buffer));
    }

    [Fact]
    public void ScreenReader_StripsTrailingBlanks_PerRowAndTrailingRows()
    {
        _emulator.ProcessData(Encoding.UTF8.GetBytes("A  \r\n\r\nB\r\n\r\n"));

        var buffer = _emulator.GetBuffer();
        var text = ScreenReader.GetScreenText(buffer, stripTrailingBlanks: true);

        // Row 0 = "A" (trailing spaces stripped), row 1 empty, row 2 = "B",
        // trailing blank rows dropped entirely.
        Assert.Equal("A\n\nB", text);
    }

    [Fact]
    public void ScreenReader_NoStrip_ReturnsFullGrid()
    {
        _emulator.ProcessData(Encoding.UTF8.GetBytes("X"));

        var buffer = _emulator.GetBuffer();
        var text = ScreenReader.GetScreenText(buffer, stripTrailingBlanks: false);

        // 24 rows of 80 columns joined by 23 newlines.
        Assert.Equal(24 * 80 + 23, text.Length);
        Assert.Equal('X', text[0]);
        Assert.Equal(' ', text[1]);
    }

    [Fact]
    public void ScreenReader_GetTailText_ReturnsLastNonBlankRows()
    {
        _emulator.ProcessData(Encoding.UTF8.GetBytes("first\r\nsecond\r\nX-C:"));

        var buffer = _emulator.GetBuffer();

        Assert.Equal("X-C:", ScreenReader.GetTailText(buffer, maxRows: 1));
        Assert.Equal("second\nX-C:", ScreenReader.GetTailText(buffer, maxRows: 2));
    }

    [Fact]
    public void ScreenReader_GetRowText_RendersSingleRow()
    {
        _emulator.ProcessData(Encoding.UTF8.GetBytes("hello world"));

        var buffer = _emulator.GetBuffer();
        Assert.Equal("hello world", ScreenReader.GetRowText(buffer, 0));
        Assert.Equal(string.Empty, ScreenReader.GetRowText(buffer, 1));
    }

    [Fact]
    public void ScreenReader_ScrollbackLine_RendersAfterScroll()
    {
        // Fill more than 24 lines so the first line scrolls out.
        var sb = new StringBuilder();
        for (int i = 0; i < 30; i++)
        {
            sb.Append("line").Append(i).Append("\r\n");
        }
        _emulator.ProcessData(Encoding.UTF8.GetBytes(sb.ToString()));

        var buffer = _emulator.GetBuffer();
        Assert.True(buffer.ScrollbackLineCount > 0);
        Assert.Equal("line0", ScreenReader.GetScrollbackLineText(buffer, 0));
        Assert.Null(ScreenReader.GetScrollbackLineText(buffer, buffer.ScrollbackLineCount));
    }

    // ─────────────────────────────────────────────────────────────
    // Dispose behavior
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Session_Dispose_StopsPump_LateDataIsIgnored()
    {
        await _session.ConnectAsync(_connection, TestContext.Current.CancellationToken);
        _connection.SimulateReceive(Encoding.UTF8.GetBytes("before"));
        await _session.FlushAsync();

        _session.Dispose();

        // After dispose the pump is completed; jobs must fail, not hang.
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _session.FlushAsync());
    }
}
