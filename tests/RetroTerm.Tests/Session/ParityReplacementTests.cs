using System.IO.Ports;
using RetroTerm.Core.Protocols.Net;
using Xunit;

namespace RetroTerm.Tests.Session;

/// <summary>
/// The serial driver must not invent a '?' where a byte failed its parity check.
/// </summary>
/// <remarks>
/// <para><b>The defect, measured 30 August 2026</b></para>
/// A SINTRAN file listing over a 7E1 line arrived with question marks sprinkled through it about
/// every thirty characters - "SINT?RAN:DATA;1" where the host sent "SINTRAN:DATA;1", and
/// "ND-500-M?ON-J04" for "ND-500-MON-J04". TeraTerm on the same line showed the text clean, which
/// is what ruled out the host and the cable as the source of the character.
/// <para><b>Where the byte came from</b></para>
/// <c>SerialPort.ParityReplace</c> defaults to 63 - '?' - and any parity setting other than None
/// turns on the Windows error character. Every byte the driver believed failed its parity check
/// was therefore handed to RetroTerm as a question mark. Nothing in this application had ever set
/// the property, so that default was in force on every serial connection it had ever made.
/// <para><b>These tests open no port</b></para>
/// A SerialPort can be constructed and configured without touching hardware, which is the only
/// reason this is testable at all. Nothing here goes near COM11.
/// </remarks>
public class ParityReplacementTests
{
    [Fact]
    public void TheFrameworkDefaultReallyIsAQuestionMark()
    {
        // THE MEASUREMENT THE DIAGNOSIS RESTS ON. If a future framework version changes this
        // default, the reasoning in StopParityErrorsBecomingQuestionMarks needs re-reading, and
        // this test is what will say so.
        using var port = new SerialPort();

        Assert.Equal(63, port.ParityReplace);
        Assert.Equal('?', (char)port.ParityReplace);
    }

    [Fact]
    public void TheConnectionTurnsTheReplacementOff()
    {
        using var port = new SerialPort();

        SerialConnection.StopParityErrorsBecomingQuestionMarks(port);

        // Zero means "do not substitute" - a suspect byte arrives as it was received rather than
        // as a character the host never sent.
        Assert.Equal(0, port.ParityReplace);
    }

    [Fact]
    public void ItIsTurnedOffWhateverTheParitySetting()
    {
        // 7E1 is the ND-120 console framing and the one that showed the defect, but the error
        // character is on for any parity other than None, so the fix must not be conditional.
        var settings = new[] { Parity.None, Parity.Odd, Parity.Even, Parity.Mark, Parity.Space };

        for (int i = 0; i < settings.Length; i++)
        {
            using var port = new SerialPort("COM255", 115200, settings[i], 7, StopBits.One);

            SerialConnection.StopParityErrorsBecomingQuestionMarks(port);

            Assert.Equal(0, port.ParityReplace);
        }
    }

    [Fact]
    public void NullBytesAreNotDiscarded()
    {
        // The neighbouring trap: DiscardNull would drop a real NUL from the stream. It defaults to
        // false and must stay that way - a terminal has to see every byte the host sent.
        using var port = new SerialPort();

        Assert.False(port.DiscardNull);
    }
}
