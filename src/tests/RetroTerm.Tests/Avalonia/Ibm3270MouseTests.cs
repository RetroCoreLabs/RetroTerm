//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Desktop.Controls;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The mouse on a 3270 tab, through the real canvas: a click puts the cursor on the cell, a double
/// click also presses Enter, and a drag still selects text (as IBM's tnz does).
/// </summary>
[Collection("Avalonia")]
public class Ibm3270MouseTests
{
    // EW, WCC restore, SF protected "NAME", SF unprotected, IC: input field from address 6.
    private static readonly byte[] Screen =
    {
        Command3270.EraseWrite, 0x02,
        Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
        Order3270.StartField, 0x40,
        Order3270.InsertCursor,
    };

    private static (Window Window, TerminalCanvas Canvas, Ibm3270Emulator Emulator, List<byte[]> Sent) Build()
    {
        var window = new Window { Width = 1000, Height = 700 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = new Ibm3270Emulator();
        canvas.SetEmulator(emulator);
        var sent = new List<byte[]>();
        emulator.DataToSend += data => sent.Add(data);
        emulator.ProcessData(Screen);
        Dispatcher.UIThread.RunJobs();

        // The canvas works out its zoom and offset while it draws. Draw once now, so the point a
        // test computes for a cell is the one the click is decoded with; without this a busy run
        // drew between the two and the click landed at half the distance.
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, canvas, emulator, sent);
    }

    private static Point At(Window window, TerminalCanvas canvas, int row, int column)
    {
        return canvas.TranslatePoint(canvas.CellCentreForTesting(row, column), window)!.Value;
    }

    [AvaloniaFact]
    public void AClickPutsTheCursorOnTheCell()
    {
        var (window, canvas, emulator, sent) = Build();

        Point p = At(window, canvas, 3, 10);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal((3 * 80) + 10, emulator.Display.CursorAddress);
        Assert.Empty(sent);
        window.Close();
    }

    [AvaloniaFact]
    public void ADoubleClickPutsTheCursorThereAndPressesEnter()
    {
        var (window, canvas, emulator, sent) = Build();

        Point p = At(window, canvas, 0, 8);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // AID Enter, cursor 8 (X'40' X'C8'), no modified fields.
        Assert.Single(sent);
        Assert.Equal(new byte[] { Aid.Enter, 0x40, 0xC8 }, sent[0]);
        window.Close();
    }

    [AvaloniaFact]
    public void SetToSelectTheWordADoubleClickSelectsItAndSendsNothing()
    {
        // The other choice of the connection's double-click setting (Ronny, 23 November 2026):
        // as wc3270 and x3270, the word under the mouse is selected and Enter is not pressed.
        var (window, canvas, emulator, sent) = Build();
        emulator.DoubleClickPressesEnter = false;

        Point p = At(window, canvas, 0, 2);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(sent);
        Assert.True(canvas.GetSelectionManager()!.HasSelection);
        Assert.Equal(2, emulator.Display.CursorAddress);
        window.Close();
    }

    [AvaloniaFact]
    public void ADragSelectsTextAndLeavesTheCursorAlone()
    {
        var (window, canvas, emulator, _) = Build();
        int before = emulator.Display.CursorAddress;

        window.MouseDown(At(window, canvas, 0, 1), MouseButton.Left);
        window.MouseMove(At(window, canvas, 0, 4));
        window.MouseUp(At(window, canvas, 0, 4), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, emulator.Display.CursorAddress);
        Assert.True(canvas.GetSelectionManager()!.HasSelection);
        window.Close();
    }

    [AvaloniaFact]
    public void APasteFillsTheFieldsAndSendsNothing()
    {
        // The Paste menu's path. The input field runs from 6 to the end of the screen, so the line
        // break's New Line lands on row 2, address 80. Before IFieldPaste the line break was Enter.
        var (window, canvas, emulator, sent) = Build();

        canvas.PasteText("ab\ncd");
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(sent);
        Assert.Equal(0x81, emulator.Display.GetByte(6));
        Assert.Equal(0x83, emulator.Display.GetByte(80));
        Assert.Equal(82, emulator.Display.CursorAddress);
        window.Close();
    }

    [AvaloniaFact]
    public void AClickOnLine25DoesNotMoveTheCursor()
    {
        var (window, canvas, emulator, _) = Build();
        int before = emulator.Display.CursorAddress;

        Point p = At(window, canvas, 24, 10);
        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(before, emulator.Display.CursorAddress);
        window.Close();
    }
}
