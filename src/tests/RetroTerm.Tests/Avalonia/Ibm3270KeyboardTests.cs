//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Collections.Generic;
using System.Text;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Desktop.Controls;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// Real key events through the real TerminalCanvas into the 3270: typing stays on the local
/// screen, and only Enter / PF / PA / Clear send a record.
/// </summary>
[Collection("Avalonia")]
public class Ibm3270KeyboardTests
{
    // EW, WCC restore, SF(protected) "NAME", SF(unprotected) IC, SBA(20) SF(autoskip).
    private static readonly byte[] Screen =
    {
        Command3270.EraseWrite, 0x02,
        Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
        Order3270.StartField, 0x40,
        Order3270.InsertCursor,
        Order3270.SetBufferAddress, 0x40, 0xD4,
        Order3270.StartField, 0xF0,
    };

    private static (Window Window, Ibm3270Emulator Emulator, List<byte[]> Records, StringBuilder MapperOutput) Build()
    {
        var window = new Window { Width = 800, Height = 600 };
        var canvas = new TerminalCanvas();
        window.Content = canvas;
        window.Show();

        var emulator = new Ibm3270Emulator();
        emulator.ProcessData(Screen);
        canvas.SetEmulator(emulator);
        canvas.Focus();

        var records = new List<byte[]>();
        emulator.DataToSend += records.Add;

        // Anything here means a key went past the 3270 to the stream-terminal path: a defect.
        var mapperOutput = new StringBuilder();
        canvas.InputReceived += text => mapperOutput.Append(text);

        return (window, emulator, records, mapperOutput);
    }

    [AvaloniaFact]
    public void TypingStaysOnTheScreenAndEnterSendsTheField()
    {
        var (window, emulator, records, mapperOutput) = Build();

        window.KeyTextInput("hi");
        Assert.Empty(records);
        Assert.Equal('h', (char)emulator.Buffer.GetCell(0, 6).Codepoint);

        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.Single(records);
        Assert.Equal(new byte[] { 0x7D, 0x40, 0xC8, 0x11, 0x40, 0xC6, 0x88, 0x89 }, records[0]);
        Assert.Equal(string.Empty, mapperOutput.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void FunctionKeysArePfKeysAndShiftAddsTwelve()
    {
        var (window, emulator, records, _) = Build();

        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.None);
        emulator.ProcessData(new byte[] { Command3270.Write, Wcc.KeyboardRestore });
        window.KeyPressQwerty(PhysicalKey.F3, RawInputModifiers.Shift);

        Assert.Equal(Aid.Pf(3), records[0][0]);
        Assert.Equal(Aid.Pf(15), records[1][0]);
        window.Close();
    }

    [AvaloniaFact]
    public void PageDownIsPf8AndAlt1IsPa1()
    {
        var (window, emulator, records, _) = Build();

        window.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);
        emulator.ProcessData(new byte[] { Command3270.Write, Wcc.KeyboardRestore });
        window.KeyPressQwerty(PhysicalKey.Digit1, RawInputModifiers.Alt);

        Assert.Equal(Aid.Pf(8), records[0][0]);
        Assert.Equal(new byte[] { Aid.PA1 }, records[1]);
        window.Close();
    }

    [AvaloniaFact]
    public void ArrowKeysMoveTheLocalCursorAndSendNothing()
    {
        var (window, emulator, records, mapperOutput) = Build();

        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Equal(87, emulator.Display.CursorAddress);
        Assert.Equal(1, emulator.Cursor.Row);
        Assert.Equal(7, emulator.Cursor.Column);
        Assert.Empty(records);
        Assert.Equal(string.Empty, mapperOutput.ToString());
        window.Close();
    }

    [AvaloniaFact]
    public void BackspaceErasesLeftOnTheScreenAndSendsNothing()
    {
        // Erase left, as wc3270 does (Ronny, 23 November 2026; KEYBOARD.md open question 3).
        var (window, emulator, records, mapperOutput) = Build();
        var notBuilt = new List<string>();
        emulator.KeyNotBuilt += notBuilt.Add;
        window.KeyTextInput("hi");

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        Assert.Empty(notBuilt);
        Assert.Equal('h', (char)emulator.Buffer.GetCell(0, 6).Codepoint);
        Assert.NotEqual('i', (char)emulator.Buffer.GetCell(0, 7).Codepoint);
        Assert.Equal(7, emulator.Cursor.Column);
        Assert.Empty(records);
        Assert.Equal(string.Empty, mapperOutput.ToString());
        window.Close();
    }
}
