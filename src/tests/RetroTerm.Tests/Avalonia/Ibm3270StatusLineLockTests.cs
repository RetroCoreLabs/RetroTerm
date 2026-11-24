//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using System.Text;

using Avalonia.Headless.XUnit;

using RetroIO.Protocols.Tn3270;

using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Avalonia
{
    /// <summary>
    /// Line 25 for every reason the keyboard can be locked, drawn through the real renderer, one
    /// PNG each (<c>images\rendered\ibm3270-line25-*.png</c>) to look at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The words are wc3270's (x3270 source, wc3270\screen.c and Common\vstatus.c), the oracle
    /// named for line 25: "X Wait", "X SYSTEM", "X Protected", "X Overflow", "X -f". The IBM
    /// operator guide draws pictures for the same states (GA27-2890-4 p. 2-21 and 2-22: a clock
    /// for Time, a figure between two arrows for Go Elsewhere, the figure and ">" for Too Much
    /// Data), which x3270's own graphic front end draws with its 3270 font (x3270\status.c).
    /// </para>
    /// <para>
    /// Mandatory fill and mandatory entry: none of the manuals held (GA23-0059-4 p. 4-21, 4-22;
    /// GA27-2890-4 p. 2-19 to 2-23; GA23-0061-2 Appendix B) names a symbol for them, and x3270 has
    /// no such fields. Only the Do Not Enter "X" is shown; the tests for those two are named
    /// "IsNotImplementedYet" so they go red when the reason is added. docs\PLAN.md, line 25.
    /// </para>
    /// </remarks>
    [Collection("Avalonia")]
    public class Ibm3270StatusLineLockTests
    {
        // EW restore; SFE at 0 unprotected with the given validation, cursor at 1; SF at 5
        // unprotected; protected stop at 20 (as RetroIO's FieldValidationTests).
        private static Ibm3270Emulator Screen(byte validation)
        {
            var emulator = new Ibm3270Emulator();
            emulator.ConnectionStatusChanged(RetroTerm.Core.Protocols.ConnectionStatus.Connected);
            emulator.ProcessData(new byte[]
            {
                Command3270.EraseWrite, 0x02,
                Order3270.StartFieldExtended, 0x02, 0xC0, 0x40, 0xC1, validation,
                Order3270.InsertCursor,
                Order3270.SetBufferAddress, 0x40, 0x45, Order3270.StartField, 0x40,
                Order3270.SetBufferAddress, 0x40, 0xD4, Order3270.StartField, 0x60,
            });
            return emulator;
        }

        // The message area of line 25 (wc3270: column 7, 35 wide), trailing blanks cut, and the
        // rendered line saved; the first message column must have ink.
        private static string Message(Ibm3270Emulator emulator, string name)
        {
            emulator.Refresh();
            var text = new StringBuilder();
            for (int column = StatusLine3270.MessageColumn; column < StatusLine3270.MessageColumn + StatusLine3270.MessageWidth; column++)
            {
                text.Append(emulator.Buffer.GetCell(24, column).GetString());
            }

            using RenderedScreenshot shot = RenderedScreenshot.Capture(emulator, "ibm3270-line25-" + name);
            Assert.True(shot.CellHasInk(24, StatusLine3270.MessageColumn), "no ink at line 25 column 7: " + shot.SavedPath);
            return text.ToString().TrimEnd();
        }

        [AvaloniaFact]
        public void WhileTheSscpOwnsTheScreenColumnTwoDrawsTheSystemOperatorSymbol()
        {
            // GA23-0061-2 p. 5-35 and Appendix B p. B-2: the System Operator symbol, a stick
            // figure in a box, in column 3 (from 1) while the SSCP-LU session owns the screen.
            Ibm3270Emulator emulator = Screen(0);
            emulator.Tn3270EChanged(true, "TCP00034", bindImage: true, sysReq: true);
            emulator.Tn3270EHostMessageReceived(RetroTerm.Core.Protocols.Tn3270EHostMessage.SscpLuData, new byte[] { 0xC9, 0xD2, 0xD1 });

            var cell = emulator.Buffer.GetCell(24, StatusLine3270.BoxColumn);
            Assert.Equal(RetroTerm.Core.Terminal.Emulators.TerminalEmulatorBase.SystemOperatorSymbolCharacterSet, cell.CharacterSet);
            Assert.Equal(StatusLine3270.SystemOperatorSymbol.ToString(), cell.GetString());

            using RenderedScreenshot shot = RenderedScreenshot.Capture(emulator, "ibm3270-line25-system-operator");
            Assert.True(shot.CellHasInk(24, StatusLine3270.BoxColumn), "no ink at line 25 column 2: " + shot.SavedPath);

            // 3270 data takes the screen back: My Job, the reverse-video box, again.
            emulator.ProcessData(new byte[] { Command3270.Write, 0x02 });
            Assert.NotEqual(RetroTerm.Core.Terminal.Emulators.TerminalEmulatorBase.SystemOperatorSymbolCharacterSet,
                emulator.Buffer.GetCell(24, StatusLine3270.BoxColumn).CharacterSet);
        }

        [AvaloniaFact]
        public void EnterShowsXWait()
        {
            Ibm3270Emulator emulator = Screen(0);
            emulator.Display.Attention(Aid.Enter);

            Assert.Equal(KeyboardLock.WaitingForHost, emulator.Display.Lock);
            Assert.Equal("X Wait", Message(emulator, "wait"));
        }

        [AvaloniaFact]
        public void AWriteWithoutRestoreAfterEnterShowsXSystem()
        {
            Ibm3270Emulator emulator = Screen(0);
            emulator.Display.Attention(Aid.Enter);
            emulator.ProcessData(new byte[] { Command3270.Write, Wcc.ResetMdt });

            Assert.Equal(KeyboardLock.SystemLock, emulator.Display.Lock);
            Assert.Equal("X SYSTEM", Message(emulator, "system"));
        }

        [AvaloniaFact]
        public void TypingOnAProtectedPositionShowsXProtected()
        {
            Ibm3270Emulator emulator = Screen(0);
            emulator.Display.MoveCursor(20);
            Assert.False(emulator.Display.Type('x'));

            Assert.Equal(KeyboardLock.OperatorError, emulator.Display.Lock);
            Assert.Equal("X Protected", Message(emulator, "protected"));
        }

        [AvaloniaFact]
        public void InsertIntoAFullFieldShowsXOverflow()
        {
            Ibm3270Emulator emulator = Screen(0);
            for (int i = 0; i < 4; i++)
            {
                Assert.True(emulator.Display.Type('A'));
            }

            emulator.Display.MoveCursor(1);
            emulator.Display.ToggleInsert();
            Assert.False(emulator.Display.Type('B'));

            Assert.Equal(KeyboardLock.TooMuchData, emulator.Display.Lock);
            Assert.Equal("X Overflow", Message(emulator, "overflow"));
        }

        [AvaloniaFact]
        public void ARefusedFunctionShowsXMinusF()
        {
            Ibm3270Emulator emulator = Screen(0);
            emulator.Display.RefuseFunction();

            Assert.Equal(KeyboardLock.MinusFunction, emulator.Display.Lock);
            Assert.Equal("X -f", Message(emulator, "minus-function"));
        }

        [AvaloniaFact]
        public void TheMandatoryFillReasonSymbolIsNotImplementedYet()
        {
            Ibm3270Emulator emulator = Screen(FieldValidation.MandatoryFill);
            Assert.True(emulator.Display.Type('A'));
            emulator.Display.Tab();

            Assert.Equal(KeyboardLock.MandatoryFill, emulator.Display.Lock);
            Assert.Equal("X", Message(emulator, "mandatory-fill"));
        }

        [AvaloniaFact]
        public void TheMandatoryEntryReasonSymbolIsNotImplementedYet()
        {
            Ibm3270Emulator emulator = Screen(FieldValidation.MandatoryEntry);
            Assert.False(emulator.Display.Attention(Aid.Enter));

            Assert.Equal(KeyboardLock.MandatoryEntry, emulator.Display.Lock);
            Assert.Equal("X", Message(emulator, "mandatory-entry"));
        }
    }
}
