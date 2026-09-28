using System.Collections.Generic;
using System.Text;
using RetroTerm.Core.Terminal.Parsing;

namespace RetroTerm.Tests.Terminal.Parsing;

public class EscapeSequenceParserTests
{
    [Fact]
    public void Should_Parse_Printable_ASCII()
    {
        var parser = new EscapeSequenceParser();
        var chars = new List<uint>();
        parser.OnCharacter += c => chars.Add(c);

        parser.ProcessBytes("Hello"u8);

        Assert.Equal(new uint[] { 'H', 'e', 'l', 'l', 'o' }, chars);
    }

    [Fact]
    public void Should_Parse_UTF8_Characters()
    {
        var parser = new EscapeSequenceParser();
        var chars = new List<uint>();
        parser.OnCharacter += c => chars.Add(c);

        // "Hello 世界" in UTF-8
        parser.ProcessBytes("Hello \u4E16\u754C"u8);

        Assert.Equal(8, chars.Count);
        Assert.Equal('H', chars[0]);
        Assert.Equal('e', chars[1]);
        Assert.Equal('l', chars[2]);
        Assert.Equal('l', chars[3]);
        Assert.Equal('o', chars[4]);
        Assert.Equal(' ', chars[5]);
        Assert.Equal(0x4E16u, chars[6]); // 世
        Assert.Equal(0x754Cu, chars[7]); // 界
    }

    [Fact]
    public void Should_Execute_C0_Controls()
    {
        var parser = new EscapeSequenceParser();
        var controls = new List<byte>();
        parser.OnExecute += b => controls.Add(b);

        // LF, CR, BS
        parser.ProcessBytes(new byte[] { 0x0A, 0x0D, 0x08 });

        Assert.Equal(new byte[] { 0x0A, 0x0D, 0x08 }, controls);
    }

    [Fact]
    public void Should_Parse_Simple_ESC_Sequence()
    {
        var parser = new EscapeSequenceParser();
        var escSequences = new List<byte>();
        parser.OnEscapeDispatch += p => escSequences.Add(p.FinalByte);

        // ESC D (Index down)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'D' });

        Assert.Equal((byte)'D', Assert.Single(escSequences));
        Assert.Equal(ParserState.Ground, parser.State);
    }

    [Fact]
    public void Should_Parse_CSI_Without_Parameters()
    {
        var parser = new EscapeSequenceParser();
        var csiSequences = new List<(byte Final, int[] Params)>();
        parser.OnCsiDispatch += p => csiSequences.Add((p.FinalByte, p.Parameters.ToArray()));

        // CSI J (Erase in Display)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'J' });

        Assert.Single(csiSequences);
        var seq = csiSequences[0];
        Assert.Equal((byte)'J', seq.Final);
        Assert.Empty(seq.Params);
    }

    [Fact]
    public void Should_Parse_CSI_With_Single_Parameter()
    {
        var parser = new EscapeSequenceParser();
        var csiSequences = new List<(byte Final, int[] Params)>();
        parser.OnCsiDispatch += p => csiSequences.Add((p.FinalByte, p.Parameters.ToArray()));

        // CSI 2 J (Erase entire screen)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'2', (byte)'J' });

        Assert.Single(csiSequences);
        var seq = csiSequences[0];
        Assert.Equal((byte)'J', seq.Final);
        Assert.Equal(new[] { 2 }, seq.Params);
    }

    [Fact]
    public void Should_Parse_CSI_With_Multiple_Parameters()
    {
        var parser = new EscapeSequenceParser();
        var csiSequences = new List<(byte Final, int[] Params)>();
        parser.OnCsiDispatch += p => csiSequences.Add((p.FinalByte, p.Parameters.ToArray()));

        // CSI 1;1 H (Cursor position to row 1, col 1)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'1', (byte)'H' });

        Assert.Single(csiSequences);
        var seq = csiSequences[0];
        Assert.Equal((byte)'H', seq.Final);
        Assert.Equal(2, seq.Params.Length);
        Assert.Equal(1, seq.Params[0]);
        Assert.Equal(1, seq.Params[1]);
    }

    [Fact]
    public void Should_Parse_CSI_With_Private_Marker()
    {
        var parser = new EscapeSequenceParser();
        byte privateMarker = 0;
        parser.OnCsiDispatch += p => privateMarker = p.PrivateMarker;

        // CSI ? 25 h (Show cursor - DEC private mode)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'?', (byte)'2', (byte)'5', (byte)'h' });

        Assert.Equal((byte)'?', privateMarker);
    }

    [Fact]
    public void Should_Parse_SGR_Sequence()
    {
        var parser = new EscapeSequenceParser();
        var params_list = new List<int[]>();
        parser.OnCsiDispatch += p => params_list.Add(p.Parameters.ToArray());

        // CSI 1;31;42 m (Bold, Red foreground, Green background)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)'1', (byte)';', (byte)'4', (byte)'2', (byte)'m' });

        Assert.Single(params_list);
        Assert.Equal(3, params_list[0].Length);
        Assert.Equal(1, params_list[0][0]);
        Assert.Equal(31, params_list[0][1]);
        Assert.Equal(42, params_list[0][2]);
    }

    [Fact]
    public void Should_Parse_Complex_Cursor_Movement()
    {
        var parser = new EscapeSequenceParser();
        var sequences = new List<(byte, int[])>();
        parser.OnCsiDispatch += p => sequences.Add((p.FinalByte, p.Parameters.ToArray()));

        // Move to 10,20, then move down 5, then move right 3
        // CSI 10;20 H CSI 5 B CSI 3 C
        var data = new byte[]
        {
            0x1B, (byte)'[', (byte)'1', (byte)'0', (byte)';', (byte)'2', (byte)'0', (byte)'H',
            0x1B, (byte)'[', (byte)'5', (byte)'B',
            0x1B, (byte)'[', (byte)'3', (byte)'C'
        };
        parser.ProcessBytes(data);

        Assert.Equal(3, sequences.Count);
        Assert.Equal((byte)'H', sequences[0].Item1);
        Assert.Equal(new[] { 10, 20 }, sequences[0].Item2);
        Assert.Equal((byte)'B', sequences[1].Item1);
        Assert.Equal(new[] { 5 }, sequences[1].Item2);
        Assert.Equal((byte)'C', sequences[2].Item1);
        Assert.Equal(new[] { 3 }, sequences[2].Item2);
    }

    [Fact]
    public void Should_Handle_Mixed_Text_And_Escapes()
    {
        var parser = new EscapeSequenceParser();
        var chars = new List<uint>();
        var csiCount = 0;
        parser.OnCharacter += c => chars.Add(c);
        parser.OnCsiDispatch += _ => csiCount++;

        // "Hello" CSI 1;31 m "World"
        var data = new List<byte>();
        data.AddRange("Hello"u8.ToArray());
        data.AddRange(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)';', (byte)'3', (byte)'1', (byte)'m' });
        data.AddRange("World"u8.ToArray());

        parser.ProcessBytes(data.ToArray());

        Assert.Equal(10, chars.Count);
        string text = new string(chars.Select(c => (char)c).ToArray());
        Assert.Equal("HelloWorld", text);
        Assert.Equal(1, csiCount);
    }

    [Fact]
    public void Should_Handle_OSC_Sequence()
    {
        var parser = new EscapeSequenceParser();
        byte[]? oscData = null;
        parser.OnOscDispatch += data => oscData = data.ToArray();

        // OSC 0 ; Window Title BEL
        var data = new List<byte> { 0x1B, (byte)']', (byte)'0', (byte)';' };
        data.AddRange("My Window"u8.ToArray());
        data.Add(0x07); // BEL

        parser.ProcessBytes(data.ToArray());

        Assert.NotNull(oscData);
        var oscString = Encoding.UTF8.GetString(oscData!);
        Assert.Equal("0;My Window", oscString);
    }

    [Fact]
    public void GetParam_Should_Return_Default_For_Missing_Parameter()
    {
        var parser = new EscapeSequenceParser();
        parser.OnCsiDispatch += p =>
        {
            Assert.Equal(1, p.GetParam(0, 1)); // No params, should return default
            Assert.Equal(99, p.GetParam(5, 99)); // Out of range, should return default
        };

        // CSI J (no parameters)
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'J' });
    }

    [Fact]
    public void Should_Reset_State_Correctly()
    {
        var parser = new EscapeSequenceParser();

        // Start a CSI sequence but don't complete it
        parser.ProcessBytes(new byte[] { 0x1B, (byte)'[', (byte)'1', (byte)'2' });
        Assert.Equal(ParserState.CsiParam, parser.State);

        parser.Reset();

        Assert.Equal(ParserState.Ground, parser.State);
        Assert.Empty(parser.Parameters.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'A' }, 'A')] // Cursor up
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'B' }, 'B')] // Cursor down
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'C' }, 'C')] // Cursor forward
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'D' }, 'D')] // Cursor back
    [InlineData(new byte[] { 0x1B, (byte)'[', (byte)'H' }, 'H')] // Cursor home
    public void Should_Parse_Common_CSI_Sequences(byte[] data, char expectedFinal)
    {
        var parser = new EscapeSequenceParser();
        byte finalByte = 0;
        parser.OnCsiDispatch += p => finalByte = p.FinalByte;

        parser.ProcessBytes(data);

        Assert.Equal((byte)expectedFinal, finalByte);
    }
}

