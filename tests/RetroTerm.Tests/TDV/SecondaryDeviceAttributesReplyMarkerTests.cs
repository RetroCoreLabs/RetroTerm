using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Emulators.TDV;
using RetroTerm.Tests.TDV.TestHelpers;
using Xunit;

namespace RetroTerm.Tests.TDV;

/// <summary>
/// A secondary device-attributes reply starts <c>CSI &gt;</c>. The primary reply starts
/// <c>CSI ?</c>. BUGS.md B2, fixed 8 October 2026.
/// </summary>
/// <remarks>
/// The base class default answered <c>CSI ? 1 ; 0 ; 0 c</c> to a <c>CSI &gt; c</c> request. All
/// three TDV classes override it, so nothing real ever sent that, and a test that only used the
/// real classes could not see the default. These tests use the bare test emulator, which does not
/// override it, so the default itself is what is checked.
/// </remarks>
public class SecondaryDeviceAttributesReplyMarkerTests
{
    private static string Reply(TDVEmulatorBase emulator)
    {
        var sent = new List<byte[]>();
        emulator.DataToSend += bytes => sent.Add(bytes);

        // ESC [ > c  - secondary device attributes request.
        emulator.ProcessData(Encoding.ASCII.GetBytes(((char)27) + "[>c"));

        Assert.Single(sent);
        return Encoding.ASCII.GetString(sent[0]);
    }

    [Fact]
    public void TheBaseClassDefaultIsMarkedWithGreaterThan()
    {
        string reply = Reply(new TestTDVEmulatorBase(80, 24));

        Assert.StartsWith(((char)27) + "[>", reply);
        Assert.EndsWith("c", reply);
    }

    [Fact]
    public void EveryTdvClassAnswersWithGreaterThan()
    {
        Assert.StartsWith(((char)27) + "[>", Reply(new TDV1200Emulator(80, 24)));
        Assert.StartsWith(((char)27) + "[>", Reply(new TDV2200Emulator(80, 24)));
        Assert.StartsWith(((char)27) + "[>", Reply(new TDV2215Emulator(80, 24)));
    }
}
