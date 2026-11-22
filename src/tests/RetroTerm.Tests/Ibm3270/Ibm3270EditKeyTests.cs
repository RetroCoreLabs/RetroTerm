//
// SPDX-License-Identifier: MIT
// Copyright (c) 1985-2026 Ronny Hansen
// RetroCore Labs — https://github.com/RetroCoreLabs/RetroTerm
// RetroTerm — Connecting today's machines to yesterday's systems
//

using RetroIO.Protocols.Tn3270;

using RetroTerm.Core.Terminal.Input;
using RetroTerm.Ibm3270;

using Xunit;

namespace RetroTerm.Tests.Ibm3270
{
    /// <summary>
    /// The 3270 editing keys reach the RetroIO screen model by name (the names scripts, MCP and
    /// the on-screen keyboard use). What each key does is RetroIO's and is tested there; these
    /// tests prove the name reaches the right function.
    /// </summary>
    public class Ibm3270EditKeyTests
    {
        // A protected field "NAME" (addresses 1-4), then an unprotected field whose first
        // character is address 6, where the cursor starts.
        private static readonly byte[] Screen =
        {
            Command3270.EraseWrite, 0x02,
            Order3270.StartField, 0x60, 0xD5, 0xC1, 0xD4, 0xC5,
            Order3270.StartField, 0x40,
            Order3270.InsertCursor,
            Order3270.SetBufferAddress, 0x40, 0xD4,
            Order3270.StartField, 0xF0,
        };

        private const int FieldStart = 6;

        private static Ibm3270Emulator TypedHi()
        {
            var e = new Ibm3270Emulator();
            e.ProcessData(Screen);
            e.HandleText("hi");
            return e;
        }

        private static void Press(Ibm3270Emulator e, string name)
        {
            Assert.Equal(NamedKeyResult.Pressed, e.PressNamedKey(name, out string? reason));
            Assert.Null(reason);
        }

        [Fact]
        public void DeleteRemovesTheCharacterAtTheCursor()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "LEFT");
            Press(e, "LEFT");
            Press(e, "DELETE");

            Assert.Equal(0x89, e.Display.GetByte(FieldStart)); // 'i' moved left
        }

        [Fact]
        public void EraseEofClearsFromTheCursorToTheEndOfTheField()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "LEFT");
            Press(e, "LEFT");
            Press(e, "ERASEEOF");

            Assert.Equal(0x00, e.Display.GetByte(FieldStart));
            Assert.Equal(0x00, e.Display.GetByte(FieldStart + 1));
        }

        [Fact]
        public void EraseInputClearsTheInputAndPutsTheCursorInTheFirstField()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "ERASEINPUT");

            Assert.Equal(0x00, e.Display.GetByte(FieldStart));
            Assert.Equal(FieldStart, e.Display.CursorAddress);
        }

        [Fact]
        public void InsertTurnsInsertModeOnAndOff()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "INSERT");
            Assert.True(e.Display.InsertMode);
            Press(e, "INSERT");
            Assert.False(e.Display.InsertMode);
        }

        [Fact]
        public void DupAndFieldMarkPutTheirCodesInTheField()
        {
            // DUP is X'1C' and FIELD MARK X'1E' in the buffer (GA23-0059-4, the character codes).
            var e = new Ibm3270Emulator();
            e.ProcessData(Screen);
            Press(e, "FIELDMARK");
            Assert.Equal(0x1E, e.Display.GetByte(FieldStart));

            e.ProcessData(Screen);
            Press(e, "DUP");
            Assert.Equal(0x1C, e.Display.GetByte(FieldStart));
        }

        [Fact]
        public void BackTabGoesBackToTheStartOfTheField()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "BACKTAB");

            Assert.Equal(FieldStart, e.Display.CursorAddress);
        }

        [Fact]
        public void NewLineMovesTheCursorOffTheFirstRow()
        {
            Ibm3270Emulator e = TypedHi();
            Press(e, "NEWLINE");

            Assert.NotEqual(FieldStart + 2, e.Display.CursorAddress);
        }

        [Fact]
        public void TheArrowKeysMoveOneColumnOrOneRow()
        {
            var e = new Ibm3270Emulator();
            e.ProcessData(Screen);
            Press(e, "DOWN");
            Assert.Equal(FieldStart + 80, e.Display.CursorAddress);
            Press(e, "RIGHT");
            Assert.Equal(FieldStart + 81, e.Display.CursorAddress);
            Press(e, "UP");
            Press(e, "LEFT");
            Assert.Equal(FieldStart, e.Display.CursorAddress);
        }
    }
}
