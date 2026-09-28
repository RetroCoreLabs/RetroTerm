using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.Tektronix;
using RetroTerm.Core.Terminal.Graphics;
using Xunit;

namespace RetroTerm.Tests.Terminal;

/// <summary>
/// The Tektronix 4014 as a terminal in its own right.
///
/// Everything it draws with was already built and proven, but it could only be reached by opening
/// a TDV2200 - a Norsk Data terminal that happens to contain a 4014 - and sending an ND sequence
/// first to make the picture visible. These tests drive the 4014 with nothing but the bytes a real
/// Tektronix host sends, which is the point of it having its own profile.
/// </summary>
public class Tek4014EmulatorTests
{
    private static string CorpusFolder => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(Tek4014EmulatorTests).Assembly.Location) ?? "",
        "..", "..", "..", "Conformance", "tektronix"));

    private static byte[] LoadFixture(string name)
    {
        var path = Path.Combine(CorpusFolder, name);
        Assert.True(File.Exists(path), $"fixture missing: {path}");
        return File.ReadAllBytes(path);
    }

    private static List<byte> CaptureReplies(TerminalEmulatorBase emulator)
    {
        var replies = new List<byte>();
        emulator.DataToSend += bytes => replies.AddRange(bytes);
        return replies;
    }

    [Theory]
    [InlineData("sin.tek40xx")]
    [InlineData("box.tek40xx")]
    [InlineData("points.tek40xx")]
    public void ARealPlotDrawsWithNoSetupSequenceAtAll(string name)
    {
        // Not one preparatory byte. A gnuplot stream is handed straight to the terminal, which is
        // what a 4014 is for and what the TDV path could not do without ESC "17h first.
        var emulator = new Tek4014Emulator();

        emulator.ProcessData(LoadFixture(name));

        Assert.True(emulator.Graphics!.HasAnythingToDraw(), $"{name} produced no graphics at all");
    }

    [Fact]
    public void TheScreenShowsWhatTheBeamWroteWithoutBeingAskedTo()
    {
        // A storage tube has no enable-graphics command. The ND terminal's plane starts hidden and
        // needs ESC "17h; this one is on from the moment the terminal exists.
        var emulator = new Tek4014Emulator();

        // GS, then one coordinate in the middle of the space, then a line to another.
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x38, 0x68, 0x38, 0x48 });

        Assert.True(emulator.Graphics!.HasAnythingToDraw());
    }

    [Fact]
    public void EscFormFeedErasesTheDrawingAndTheText()
    {
        var emulator = new Tek4014Emulator();
        emulator.ProcessData(Encoding.ASCII.GetBytes("HELLO"));
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x38, 0x68, 0x38, 0x48 });
        Assert.True(emulator.Graphics!.HasAnythingToDraw());

        emulator.ProcessData(new byte[] { 0x1B, 0x0C });

        Assert.False(emulator.Graphics!.HasAnythingToDraw(), "the tube keeps nothing after an erase");
        emulator.GetBuffer().TryGetCell(0, 0, out var cell);
        Assert.Equal(0u, cell.Codepoint);
        Assert.Equal(0, emulator.GetCursor().Row);
        Assert.Equal(0, emulator.GetCursor().Column);
    }

    [Fact]
    public void ABareFormFeedIsStillAFormFeed()
    {
        // Same byte, two commands, told apart only by whether an ESC came first. Without that
        // distinction every line feed would wipe the plot.
        var emulator = new Tek4014Emulator();
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x38, 0x68, 0x38, 0x48 });

        emulator.ProcessData(new byte[] { 0x0C });

        Assert.True(emulator.Graphics!.HasAnythingToDraw(), "a bare FF must not erase the tube");
    }

    [Fact]
    public void EscEnqAnswersWithFiveBytesAndNoModelIdentification()
    {
        // Five, not seven. The two extra bytes are the Norsk Data extension, and a plain 4014
        // sending them would be claiming to be a machine it is not - the length is exactly how an
        // ND host tells the two apart.
        var emulator = new Tek4014Emulator();
        var replies = CaptureReplies(emulator);

        emulator.ProcessData(new byte[] { 0x1B, 0x05 });

        Assert.Equal(TektronixGinEncoder.StandardReportLength, replies.Count);
        Assert.Equal(0x68, replies[0]);
    }

    [Fact]
    public void EscSubRaisesTheCrosshairAndArmsGin()
    {
        var emulator = new Tek4014Emulator();
        Assert.False(emulator.CrosshairVisible);

        emulator.ProcessData(new byte[] { 0x1B, 0x1A });

        Assert.True(emulator.CrosshairVisible);
        Assert.True(emulator.Gin.IsArmed);
    }

    [Fact]
    public void ItStaysSilentWhenProbedForDeviceAttributes()
    {
        // A 4014 is from 1974 and predates ANSI device attributes. Answering with a borrowed VT100
        // reply would tell a host it can send sequences this terminal has never heard of.
        var emulator = new Tek4014Emulator();
        var replies = CaptureReplies(emulator);

        emulator.ProcessData(Encoding.ASCII.GetBytes("\x1b[c"));

        Assert.Empty(replies);
    }

    [Fact]
    public void LeavingGraphModePutsTheTextCursorAtTheLastPoint()
    {
        // How a host writes a label anywhere on the screen: move the beam, drop back to alpha,
        // print. Without it a plot's axis numbers cascade diagonally across the picture.
        var emulator = new Tek4014Emulator();

        // GS, the middle of the space, US, then a label.
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x1F });

        Assert.True(emulator.GetCursor().Column > 0, "the cursor should have followed the beam");
        Assert.True(emulator.GetCursor().Row > 0);
    }

    [Fact]
    public void ResetClearsTheDrawingAndLowersTheCrosshair()
    {
        var emulator = new Tek4014Emulator();
        emulator.ProcessData(new byte[] { 0x1D, 0x30, 0x60, 0x30, 0x40, 0x38, 0x68, 0x38, 0x48 });
        emulator.ProcessData(new byte[] { 0x1B, 0x1A });

        emulator.Reset();

        Assert.False(emulator.Graphics!.HasAnythingToDraw());
        Assert.False(emulator.CrosshairVisible);
        Assert.False(emulator.Gin.IsArmed);
    }

    [Fact]
    public void TheFactoryBuildsOneAtTheRealGeometry()
    {
        Assert.True(EmulatorFactory.IsSupported("TEK4014"));

        var (width, height) = EmulatorFactory.GetRecommendedSize("TEK4014");
        Assert.Equal(74, width);
        Assert.Equal(35, height);

        var emulator = EmulatorFactory.CreateEmulator("TEK4014", width, height, 100);
        Assert.IsType<Tek4014Emulator>(emulator);
        Assert.Equal(74, emulator.Width);
        Assert.Equal(35, emulator.Height);
    }

    [Fact]
    public void TextStillPrints()
    {
        // It is a terminal as well as a plotter, and the alpha half must survive all of the above.
        var emulator = new Tek4014Emulator();

        emulator.ProcessData(Encoding.ASCII.GetBytes("AB"));

        emulator.GetBuffer().TryGetCell(0, 0, out var first);
        emulator.GetBuffer().TryGetCell(0, 1, out var second);
        Assert.Equal('A', (char)first.Codepoint);
        Assert.Equal('B', (char)second.Codepoint);
    }
}
