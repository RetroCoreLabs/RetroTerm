using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using RetroTerm.Core.Opcom;
using RetroTerm.Desktop.Views;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The OPCOM log must not freeze the window when a transfer floods it.
///
/// Reported by Ronny, 6 September 2026: he opened the OPCOM Log during a BPUN upload and
/// the window stopped responding - Windows greyed it out and marked it Not Responding -
/// while the upload itself carried on happily. The transfer did not need the UI thread;
/// the log did, and it was asking for far more than there was.
///
/// The old code posted one dispatcher item per LOGGED BYTE, and each of those rebuilt the
/// entire log into a new string, assigned it to the text block and scrolled to the end. A
/// deposit-loop upload logs a send and an echo for every character of every word, so a
/// few thousand words meant tens of thousands of full re-layouts of a ten-thousand-line
/// block. The queue outran the thread and never caught up.
///
/// These tests pin the two properties that make that impossible: the log entry handler
/// does no UI work at all, and the display is refreshed from a buffer instead.
/// </summary>
[Collection("Avalonia")]
public class OpcomLogFloodTests
{
    private static OpcomLogEntry Entry(string text)
    {
        var bytes = new byte[text.Length];
        for (int i = 0; i < text.Length; i++) bytes[i] = (byte)text[i];
        return new OpcomLogEntry(OpcomLogDirection.Tx, bytes);
    }

    [AvaloniaFact]
    public void AFloodOfLogEntriesDoesNotTouchTheDisplay()
    {
        var window = new OpcomDebugWindow();
        var display = window.FindControl<TextBlock>("LogDisplayText")!;
        var panel = window.FindControl<DockPanel>("LogPanel")!;

        // The log tab is not showing, which is the case during an upload started from the
        // upload tab. Nothing should reach the screen at all.
        Assert.False(panel.IsVisible);

        for (int i = 0; i < 5000; i++)
        {
            window.HandleLogEntryForTest(Entry("0"));
        }
        window.RefreshLogDisplay();

        Assert.True(string.IsNullOrEmpty(display.Text),
            "the hidden log was rendered anyway, which is the work that froze the window");
    }

    [AvaloniaFact]
    public void TheBufferedLinesAppearOnceTheLogIsLookedAt()
    {
        var window = new OpcomDebugWindow();
        var display = window.FindControl<TextBlock>("LogDisplayText")!;
        var panel = window.FindControl<DockPanel>("LogPanel")!;

        for (int i = 0; i < 50; i++) window.HandleLogEntryForTest(Entry("A"));

        // Opening the tab shows what piled up while it was hidden.
        panel.IsVisible = true;
        window.RefreshLogDisplay();

        Assert.False(string.IsNullOrEmpty(display.Text), "nothing was shown when the log was opened");
        Assert.Contains("[TX] A", display.Text);
    }

    [AvaloniaFact]
    public void ManyEntriesCollapseIntoOneRefreshRatherThanOnePerEntry()
    {
        var window = new OpcomDebugWindow();
        var display = window.FindControl<TextBlock>("LogDisplayText")!;
        var panel = window.FindControl<DockPanel>("LogPanel")!;
        panel.IsVisible = true;

        for (int i = 0; i < 2000; i++) window.HandleLogEntryForTest(Entry("B"));

        // Still nothing on screen: the entries are buffered, not drawn.
        Assert.True(string.IsNullOrEmpty(display.Text),
            "an entry drew itself immediately, which is the per-entry work that caused the freeze");

        window.RefreshLogDisplay();
        Assert.Equal(2000, CountLines(display.Text));

        // A second refresh with nothing new must do nothing at all - that is what stops a
        // 10 Hz timer rebuilding a large string forever while the log sits idle.
        display.Text = "sentinel";
        window.RefreshLogDisplay();
        Assert.Equal("sentinel", display.Text);
    }

    [AvaloniaFact]
    public void TheLogStopsGrowingAtItsLimitInsteadOfForever()
    {
        var window = new OpcomDebugWindow();
        var panel = window.FindControl<DockPanel>("LogPanel")!;
        var display = window.FindControl<TextBlock>("LogDisplayText")!;
        panel.IsVisible = true;

        // Well past the ten-thousand-line cap. A long upload logs far more than this.
        for (int i = 0; i < 12000; i++) window.HandleLogEntryForTest(Entry("C"));
        window.RefreshLogDisplay();

        int lines = CountLines(display.Text);
        Assert.True(lines <= 10000, "the log grew past its limit and held " + lines + " lines");
        Assert.True(lines > 9000, "the log dropped far more than it should have, holding only " + lines);
    }

    [AvaloniaFact]
    public void FormattingAnEntryNeedsNoWindowAtAll()
    {
        // The formatter runs on the protocol's thread, so it must not read a control.
        // If this ever needs a window to work, the freeze comes straight back.
        string text = OpcomDebugWindow.FormatLogEntry(Entry("AB"), showHex: false);
        Assert.Contains("[TX] AB", text);
        Assert.EndsWith("\n", text);

        string hex = OpcomDebugWindow.FormatLogEntry(Entry("AB"), showHex: true);
        Assert.Contains("41 42", hex);
    }

    [AvaloniaFact]
    public void ControlCharactersAreShownByName()
    {
        var entry = new OpcomLogEntry(OpcomLogDirection.Rx, new byte[] { 0x0D, 0x0A, 0x1B, 0x07 });
        string text = OpcomDebugWindow.FormatLogEntry(entry, showHex: false);
        Assert.Contains("<CR>", text);
        Assert.Contains("<LF>", text);
        Assert.Contains("<ESC>", text);
        Assert.Contains("<07>", text);
        Assert.Contains("[RX]", text);
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int n = 0;
        for (int i = 0; i < text!.Length; i++) if (text[i] == '\n') n++;
        return n;
    }
}
