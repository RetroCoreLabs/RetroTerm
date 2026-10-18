using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Protocols;
using RetroTerm.Core.Session;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Desktop;
using RetroTerm.Desktop.Controls;
using RetroTerm.Desktop.Models;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// The connection's Keyboard tab: what Backspace and Delete send, what the Enter key puts on
/// the wire, and whether typing is echoed locally.
/// </summary>
/// <remarks>
/// <para><b>Why these belong to the connection</b></para>
/// Every one is a property of the HOST rather than of the keyboard: a SINTRAN line and a Unix
/// login want opposite answers, and both are normally open in this window at once.
/// <para><b>The defaults must change nothing</b></para>
/// A connection saved before this tab existed has no values for any of it, and must keep behaving
/// exactly as it did. Several tests below exist only to pin that.
/// </remarks>
[Collection("Avalonia")]
public class KeyboardTabSettingsTests
{
    /// <summary>
    /// DEL, written so no raw control byte ever enters this file.
    /// </summary>
    private const string Del = "\u007F";

    // ── Enter key: what goes on the wire ──────────────────────────

    [AvaloniaFact]
    public void ByDefault_TheEnterKeySendsCarriageReturnAlone()
    {
        // What this program has always sent, and what SINTRAN and the TDVs expect.
        Assert.Equal("\r", MainWindow.ApplyTransmitNewLine("\r", null));
        Assert.Equal("\r", MainWindow.ApplyTransmitNewLine("\r", ConnectionFactory.NewLineModes.Cr));
    }

    [AvaloniaFact]
    public void TransmitCrLf_SendsBoth()
    {
        Assert.Equal("\r\n", MainWindow.ApplyTransmitNewLine("\r", ConnectionFactory.NewLineModes.CrLf));
    }

    [AvaloniaFact]
    public void TransmitLf_SendsLineFeedAlone()
    {
        Assert.Equal("\n", MainWindow.ApplyTransmitNewLine("\r", ConnectionFactory.NewLineModes.Lf));
    }

    [AvaloniaFact]
    public void OrdinaryTypingIsNeverRewritten()
    {
        // Only the carriage return is touched. A line of text with no CR must come through
        // byte for byte whatever the setting says.
        Assert.Equal("LIST-FILES", MainWindow.ApplyTransmitNewLine("LIST-FILES", ConnectionFactory.NewLineModes.CrLf));
        Assert.Equal("", MainWindow.ApplyTransmitNewLine("", ConnectionFactory.NewLineModes.Lf));
    }

    [AvaloniaFact]
    public void AnUnknownModeFallsBackToCarriageReturn()
    {
        // A file hand-edited to something meaningless must not stop the Enter key working.
        Assert.Equal("\r", MainWindow.ApplyTransmitNewLine("\r", "nonsense"));
    }

    // ── Backspace and Delete ──────────────────────────────────────

    [AvaloniaFact]
    public void ByDefault_AVtTabDoesNotSendDel()
    {
        // A VT100 tab: the connection has not chosen (null), and the VT's own default is BS.
        var tab = NewTab();
        tab.Control.SetEmulator(tab.Session.Emulator);
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23
        };

        Assert.Null(tab.Control.BackspaceSendsDel);
        Assert.False(tab.Control.EffectiveBackspaceSendsDel);
        Assert.False(tab.Control.DeleteSendsDel);
    }

    [AvaloniaFact]
    public void ByDefault_ATdvTabSendsDel()
    {
        // The same connection, no choice made, on a TDV2200: the terminal's own default is DEL.
        // Asked for by Ronny on 27 September 2026 - a SINTRAN line wants DEL for rubout, and
        // every TDV connection used to send BS unless somebody found the checkbox.
        var session = new TerminalSession(new RetroTerm.Core.Terminal.Emulators.TDV.TDV2200Emulator(80, 25), "KeyboardTabTest");
        var tab = new TabSession(session, new TerminalControl());
        tab.Control.SetEmulator(session.Emulator);
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23,
            EmulatorType = "TDV2200"
        };

        Assert.Null(tab.Control.BackspaceSendsDel);
        Assert.True(tab.Control.EffectiveBackspaceSendsDel);

        // And an explicit "no" on the connection still wins over the terminal's default.
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Telnet,
            Host = "localhost",
            Port = 23,
            EmulatorType = "TDV2200",
            BackspaceSendsDel = false
        };
        Assert.False(tab.Control.EffectiveBackspaceSendsDel);
    }

    [AvaloniaFact]
    public void TheConnectionsChoiceReachesTheControl()
    {
        // Applied by the LastConnectionParameters setter rather than at the five call sites that
        // assign it - the same reason FollowWindowSize is applied there.
        var tab = NewTab();
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = "COM11",
            BackspaceSendsDel = true,
            DeleteSendsDel = true
        };

        Assert.Equal(true, tab.Control.BackspaceSendsDel);
        Assert.True(tab.Control.DeleteSendsDel);
    }

    [AvaloniaFact]
    public void ClearingTheConnectionClearsTheKeySettings()
    {
        var tab = NewTab();
        tab.Control.SetEmulator(tab.Session.Emulator);
        tab.LastConnectionParameters = new ConnectionFactory.ConnectionParameters
        {
            Protocol = ConnectionFactory.ProtocolType.Serial,
            PortName = "COM11",
            BackspaceSendsDel = true
        };
        Assert.Equal(true, tab.Control.BackspaceSendsDel);

        tab.LastConnectionParameters = null;

        // Back to "not chosen", which on this VT100 tab means BS.
        Assert.Null(tab.Control.BackspaceSendsDel);
        Assert.False(tab.Control.EffectiveBackspaceSendsDel);
    }

    // ── Round-trip through the stored connection ──────────────────

    [AvaloniaFact]
    public void EverySettingSurvivesTheStoredConnection()
    {
        var config = new HostConfiguration
        {
            Name = "nd-console",
            Protocol = "Serial",
            PortName = "COM11",
            BackspaceSendsDel = true,
            DeleteSendsDel = true,
            NewLineReceive = ConnectionFactory.NewLineModes.CrLf,
            NewLineTransmit = ConnectionFactory.NewLineModes.Lf,
            LocalEcho = true
        };

        var parameters = config.ToConnectionParameters();

        Assert.Equal(true, parameters.BackspaceSendsDel);
        Assert.True(parameters.DeleteSendsDel);
        Assert.Equal(ConnectionFactory.NewLineModes.CrLf, parameters.NewLineReceive);
        Assert.Equal(ConnectionFactory.NewLineModes.Lf, parameters.NewLineTransmit);
        Assert.True(parameters.LocalEcho);

        // A copy keeps them too - Clone forgetting a field is how settings quietly get lost.
        var clone = config.Clone();
        Assert.Equal(true, clone.BackspaceSendsDel);
        Assert.True(clone.DeleteSendsDel);
        Assert.Equal(ConnectionFactory.NewLineModes.CrLf, clone.NewLineReceive);
        Assert.Equal(ConnectionFactory.NewLineModes.Lf, clone.NewLineTransmit);
        Assert.True(clone.LocalEcho);
    }

    [AvaloniaFact]
    public void AConnectionSavedBeforeThisTabExistedKeepsItsOldBehaviour()
    {
        // No keyboard values at all, which is every connection in the user's file today.
        var config = new HostConfiguration { Name = "old", Host = "localhost", Port = 23 };

        var parameters = config.ToConnectionParameters();

        // Backspace is "not chosen": on this VT100 record the terminal's default is the BS it
        // always sent, and only a TDV record gets DEL from a null (see ByDefault_ATdvTabSendsDel).
        Assert.Null(parameters.BackspaceSendsDel);
        Assert.False(EmulatorFactory.GetDefaultBackspaceSendsDel(config.EmulatorType));
        Assert.False(parameters.DeleteSendsDel);
        Assert.False(parameters.LocalEcho);
        Assert.Equal(ConnectionFactory.NewLineModes.Auto, parameters.NewLineReceive);
        Assert.Equal(ConnectionFactory.NewLineModes.Cr, parameters.NewLineTransmit);
    }

    [AvaloniaFact]
    public void AnEmptyStoredModeIsReadAsTheDefault()
    {
        // A hand-edited or partially written file must not produce an empty line-ending mode.
        var config = new HostConfiguration
        {
            Name = "blank",
            Host = "localhost",
            NewLineReceive = "",
            NewLineTransmit = "   "
        };

        var parameters = config.ToConnectionParameters();

        Assert.Equal(ConnectionFactory.NewLineModes.Auto, parameters.NewLineReceive);
        Assert.Equal(ConnectionFactory.NewLineModes.Cr, parameters.NewLineTransmit);
    }

    // ── The transmit byte table ───────────────────────────────────

    [AvaloniaFact]
    public void TheTransmitBytesAreWhatTheNamesSay()
    {
        Assert.Equal(new byte[] { 0x0D }, ConnectionFactory.NewLineModes.TransmitBytes(ConnectionFactory.NewLineModes.Cr));
        Assert.Equal(new byte[] { 0x0D, 0x0A }, ConnectionFactory.NewLineModes.TransmitBytes(ConnectionFactory.NewLineModes.CrLf));
        Assert.Equal(new byte[] { 0x0A }, ConnectionFactory.NewLineModes.TransmitBytes(ConnectionFactory.NewLineModes.Lf));
        Assert.Equal(new byte[] { 0x0D }, ConnectionFactory.NewLineModes.TransmitBytes(null));
    }

    [AvaloniaFact]
    public void TheDropdownsOfferWhatTheyShould()
    {
        // Receive has AUTO; transmit does not, because something definite has to go on the wire.
        Assert.Contains(ConnectionFactory.NewLineModes.Auto, ConnectionFactory.NewLineModes.ReceiveChoices);
        Assert.DoesNotContain(ConnectionFactory.NewLineModes.Auto, ConnectionFactory.NewLineModes.TransmitChoices);
        Assert.Equal(3, ConnectionFactory.NewLineModes.TransmitChoices.Length);
    }

    // ── The control byte itself ───────────────────────────────────

    [AvaloniaFact]
    public void DelIsOneTwentySeven()
    {
        // Ronny asked whether "Transmit DEL" means decimal 127. It does.
        Assert.Single(Del);
        Assert.Equal(127, (int)Del[0]);
        Assert.Equal(0x7F, (int)Del[0]);
    }

    [AvaloniaFact]
    public void NoRawControlByteSitsInTheKeyboardSource()
    {
        // A raw 0x7F was written into TerminalCanvas.axaml.cs while this feature was being built
        // and was invisible in the editor and in the diff - it took a byte dump to find. The
        // constant is spelled \u007F for that reason, and this fails if one creeps back.
        string[] files =
        {
            SourceFile("src", "RetroTerm.Desktop", "Controls", "TerminalCanvas.axaml.cs"),
            SourceFile("src", "RetroTerm.Desktop", "Controls", "TerminalControl.axaml.cs"),
            SourceFile("src", "RetroTerm.Desktop", "Models", "TabSession.cs"),
        };

        int scanned = 0;
        for (int i = 0; i < files.Length; i++)
        {
            if (!File.Exists(files[i])) continue;   // not run from a checkout
            scanned++;

            var bytes = File.ReadAllBytes(files[i]);
            for (int b = 0; b < bytes.Length; b++)
            {
                // 0x7F must be named separately. DEL is a control character but it sits ABOVE the
                // C0 range numerically, so a ">= 0x20 is fine" test lets it straight through - the
                // first version of this test did exactly that and passed with a real DEL injected
                // into TabSession.cs. That is the one byte this whole test was written to catch.
                bool isAllowed = bytes[b] != 0x7F
                    && (bytes[b] >= 0x20 || bytes[b] == 0x09 || bytes[b] == 0x0A || bytes[b] == 0x0D);
                Assert.True(isAllowed,
                    $"{files[i]} holds a raw control byte 0x{bytes[b]:X2} at offset {b}");
            }
        }

        // Without this the whole test passes when the path is wrong and nothing was read - which
        // is the "green for the wrong reason" failure this repository has already been bitten by
        // three times. If the layout moves, this fails and says so rather than going quiet.
        Assert.True(scanned > 0,
            "no source file was found to scan - the path in SourceFile no longer resolves");
    }

    private static string SourceFile(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(KeyboardTabSettingsTests).Assembly.Location) ?? "",
            "..", "..", "..", "..", ".."));
        return Path.Combine(root, Path.Combine(parts));
    }

    private static TabSession NewTab()
    {
        var session = new TerminalSession(new VT100Emulator(80, 24), "KeyboardTabTest");
        return new TabSession(session, new TerminalControl());
    }
}
