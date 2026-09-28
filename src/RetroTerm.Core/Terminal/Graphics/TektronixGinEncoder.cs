using System;

namespace RetroTerm.Core.Terminal.Graphics;

/// <summary>
/// Encodes a crosshair position into the bytes a Tektronix 4014 sends back to the host.
///
/// This is the GIN half of the graphics foundation: the reverse of the keyboard path, same shape.
/// A pointer moves, the active graphics module turns it into report bytes, and those go out on the
/// same single host-reply channel everything else uses.
///
/// The layout is verified against <c>spec\Tektronix\nd-graphic-terminal-analysis.md</c>, which
/// carries both the bit tags and a worked example from a disassembled ND test program — the example
/// is reproduced exactly by <c>TektronixGinEncoderTests.TheWorkedExampleFromTheSpecEncodesByteForByte</c>,
/// so this is pinned to a primary source rather than to a reading of it.
///
/// A ten-bit coordinate is split into two five-bit halves, each carrying tag bits that tell the host
/// which half of which axis it is looking at:
/// <code>
///   Hi byte: 0 0 1 D9 D8 D7 D6 D5     tag 001
///   Lo byte: T1 T0 1 D4 D3 D2 D1 D0   tag varies by axis
///
///   Hi Y: 0x20 or-ed with the top five bits of y
///   Lo Y: 0x60 or-ed with the low five bits of y
///   Hi X: 0x20 or-ed with the top five bits of x
///   Lo X: 0x40 or-ed with the low five bits of x
/// </code>
///
/// Every method writes into a caller's span and returns how many bytes it wrote. Nothing here
/// allocates: a crosshair being dragged emits one of these per pointer move.
/// </summary>
public static class TektronixGinEncoder
{
    /// <summary>
    /// Bytes a real Tektronix 4014 sends: a lead byte and four coordinate bytes.
    /// </summary>
    public const int StandardReportLength = 5;

    /// <summary>
    /// Bytes a Norsk Data graphic terminal sends: the standard five plus two of its own.
    /// </summary>
    public const int NorskDataReportLength = 7;

    /// <summary>
    /// Encodes a standard Tektronix 4014 report.
    /// </summary>
    /// <param name="destination">
    /// At least <see cref="StandardReportLength"/> bytes.
    /// </param>
    /// <param name="lead">
    /// The first byte. Two things arrive here depending on what prompted the report: the ASCII code
    /// of the key the user pressed while the crosshair was up, or the terminal STATUS byte when the
    /// host asked with <c>ESC ENQ</c>. Same five-byte shape either way, which is why the caller
    /// picks it rather than this method.
    /// </param>
    /// <param name="logicalX">
    /// Crosshair X in the terminal's own space, 0..1023.
    /// </param>
    /// <param name="logicalY">
    /// Crosshair Y in the terminal's own space, 0..1023.
    /// </param>
    /// <returns>
    /// Bytes written.
    /// </returns>
    public static int Encode(Span<byte> destination, byte lead, int logicalX, int logicalY)
    {
        if (destination.Length < StandardReportLength)
        {
            throw new ArgumentException(
                $"A GIN report needs {StandardReportLength} bytes.", nameof(destination));
        }

        // Ten bits per axis is all the encoding has room for. A coordinate outside that would
        // silently wrap into a different position on screen, so it is clamped where the damage is
        // visible rather than masked where it is not.
        int x = Clamp10Bit(logicalX);
        int y = Clamp10Bit(logicalY);

        destination[0] = lead;
        destination[1] = (byte)(0x20 | ((y >> 5) & 0x1F));   // Hi Y
        destination[2] = (byte)(0x60 | (y & 0x1F));          // Lo Y
        destination[3] = (byte)(0x20 | ((x >> 5) & 0x1F));   // Hi X
        destination[4] = (byte)(0x40 | (x & 0x1F));          // Lo X

        return StandardReportLength;
    }

    /// <summary>
    /// Encodes the Norsk Data form: the standard five bytes plus two that say which ND this is.
    ///
    /// THIS IS HOW A HOST TELLS AN ND FROM A REAL 4014. Both answer the same standard
    /// <c>ESC ENQ</c>; the ND simply answers with two more bytes, each masked to seven bits, and
    /// those two identify the model — ND-324/Notis, ND-325/Net, ND-246, ND-285, ND-320, ND-322.
    /// A host that reads five bytes and stops is talking to a Tektronix; one that gets seven knows
    /// it has an ND and which one.
    /// </summary>
    /// <param name="destination">
    /// At least <see cref="NorskDataReportLength"/> bytes.
    /// </param>
    /// <param name="lead">
    /// The first byte: the key the user pressed, or the terminal status byte when the host asked
    /// with <c>ESC ENQ</c>.
    /// </param>
    /// <param name="logicalX">
    /// Crosshair X in the terminal's own space.
    /// </param>
    /// <param name="logicalY">
    /// Crosshair Y in the terminal's own space.
    /// </param>
    /// <param name="modelByte1">
    /// First model byte; masked to seven bits, as the terminal does.
    /// </param>
    /// <param name="modelByte2">
    /// Second model byte; masked to seven bits.
    /// </param>
    /// <returns>
    /// Bytes written.
    /// </returns>
    public static int EncodeNorskData(Span<byte> destination, byte lead, int logicalX, int logicalY,
        byte modelByte1, byte modelByte2)
    {
        if (destination.Length < NorskDataReportLength)
        {
            throw new ArgumentException(
                $"An ND GIN report needs {NorskDataReportLength} bytes.", nameof(destination));
        }

        Encode(destination, lead, logicalX, logicalY);

        // The receiving end ANDs each with 0x7F, so anything in the top bit is lost in transit.
        // Masking here means what we send is what a host will read back.
        destination[5] = (byte)(modelByte1 & 0x7F);
        destination[6] = (byte)(modelByte2 & 0x7F);

        return NorskDataReportLength;
    }

    /// <summary>
    /// Reads a coordinate back out of an encoded report — the host's side of the same wire.
    ///
    /// Here so the encoding is stated once. A test that decoded with its own copy of the bit
    /// arithmetic would agree with a wrong encoder just as happily as with a right one.
    /// </summary>
    /// <returns>
    /// False if the report is too short or its tag bits are not what they should be.
    /// </returns>
    public static bool TryDecode(ReadOnlySpan<byte> report, out int logicalX, out int logicalY)
    {
        logicalX = 0;
        logicalY = 0;

        if (report.Length < StandardReportLength) return false;

        // The tags are the only thing that says which byte is which, so a report that fails them
        // is not a report - it is something else that happened to arrive.
        if ((report[1] & 0xE0) != 0x20) return false;   // Hi Y
        if ((report[2] & 0xE0) != 0x60) return false;   // Lo Y
        if ((report[3] & 0xE0) != 0x20) return false;   // Hi X
        if ((report[4] & 0xE0) != 0x40) return false;   // Lo X

        logicalY = ((report[1] & 0x1F) << 5) | (report[2] & 0x1F);
        logicalX = ((report[3] & 0x1F) << 5) | (report[4] & 0x1F);
        return true;
    }

    private static int Clamp10Bit(int value)
    {
        if (value < 0) return 0;
        if (value > 1023) return 1023;
        return value;
    }
}
