using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// Every primary DA reply, checked against a real DEC document.
/// </summary>
/// <remarks>
/// <para><b>Where the expected answers come from</b></para>
/// The VT330/VT340 Text Programming manual (EK-VT3XX-TP-001), "Alias Primary DA Responses From the
/// VT300", lists what a VT300 answers when set up to identify as an earlier terminal. DEC would
/// hardly print an alias that did not match the real machine, so that table is a document for
/// terminals whose own manuals are not held here:
///
///  - VT100 DA: <c>ESC [ ? 1; 2 c</c>
///  - VT101 DA: <c>ESC [ ? 1; 0 c</c>
///  - VT102 DA: <c>ESC [ ? 6 c</c>
///  - VT125 DA: <c>ESC [ ? 12; 7; 1; 10; 102 c</c>
///  - VT131 DA: <c>ESC [ ? 7 c</c>
///  - VT220 DA: <c>CSI ? 62; 1; 2; 6; 7; 8; 9 c</c>
///  - VT240 DA: <c>CSI ? 62; 1; 2; 3; 4; 6; 7; 8; 9 c</c>
///
/// The VT300's own reply is in the same chapter, and the VT420's is in its own manual.
///
/// <para><b>Why some of ours are shorter</b></para>
/// This emulator's rule is that an extension goes into the DA reply only once the thing behind it
/// works. So a reply here may be a SUBSET of the manual's - never a superset, and never a different
/// number. Each omission below names the capability that is genuinely absent. That is the whole
/// point of the test: a DA reply is a promise, and an unkept one sends a host down a path the
/// terminal cannot follow.
/// </remarks>
public class DeviceAttributesAgainstTheManualTests
{
    private static string Ask(string type)
    {
        var emulator = EmulatorFactory.CreateEmulator(type, 80, 24, 100);
        var replies = new StringBuilder();
        emulator.DataToSend += bytes => replies.Append(Encoding.ASCII.GetString(bytes));

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[c"));
        return replies.ToString();
    }

    [Fact]
    public void TheVt100MatchesTheManualExactly()
    {
        // "VT100 DA: ESC [ ? 1; 2 c" - a VT100 with the Advanced Video Option, which is what this
        // emulator provides.
        Assert.Equal("\x1b[?1;2c", Ask("VT100"));
    }

    [Fact]
    public void AndSoDoesTheVt102()
    {
        // "VT102 DA: ESC [ ? 6 c". The VT102 answers with a single number rather than a list -
        // the extension-list convention came later.
        Assert.Equal("\x1b[?6c", Ask("VT102"));
    }

    [Fact]
    public void TheVt220IsAnHonestSubset()
    {
        // The manual gives 62; 1; 2; 6; 7; 8; 9. Ours omits 2 (printer port) and 9 (national
        // replacement character sets), and neither exists here. Nothing is claimed that is absent,
        // and nothing absent from the manual is claimed.
        Assert.Equal("\x1b[?62;1;6;7;8c", Ask("VT220"));
    }

    [Fact]
    public void TheVt240IsToo()
    {
        // The manual gives 62; 1; 2; 3; 4; 6; 7; 8; 9. The 4 - Sixel - was missing until this
        // table was read; see MonochromeGraphicsTests for that story. 2 and 9 are still out.
        Assert.Equal("\x1b[?62;1;3;4;6;7;8c", Ask("VT240"));
    }

    [Fact]
    public void TheVt340SaysItIsAVt300WithBothKindsOfGraphics()
    {
        // 63 is the VT300 service class; 3 is ReGIS and 4 is Sixel, which is what a VT340 is for.
        Assert.Equal("\x1b[?63;1;3;4;6;7;8c", Ask("VT340"));
    }

    [Fact]
    public void TheVt320SaysItIsAVt300Without()
    {
        // The same service class with no graphics at all - a VT320 is the text member of the
        // family.
        Assert.Equal("\x1b[?63;1;6;7;8c", Ask("VT320"));
    }

    [Fact]
    public void TheVt420SaysItIsALevelFourTerminal()
    {
        // 64 is the VT400 service class. The VT420 Programmer Reference is held in spec\DEC.
        Assert.Equal("\x1b[?64;1;6;7;8c", Ask("VT420"));
    }

    [Fact]
    public void EveryReplyIsWellFormedAndPrivate()
    {
        // The shape itself is worth pinning: a primary DA reply is CSI ? ... c, and a host that
        // gets anything else cannot tell it from another report entirely.
        string[] types = { "VT100", "VT102", "VT220", "VT240", "VT320", "VT340", "VT420" };

        for (int i = 0; i < types.Length; i++)
        {
            string reply = Ask(types[i]);

            Assert.StartsWith("\x1b[?", reply);
            Assert.EndsWith("c", reply);
        }
    }
}
