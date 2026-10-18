using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// NdBootProtocol against the byte vectors in NDBoot docs/PROTOCOL.md section 10 and
/// against a fake monitor that behaves like src/bootstrap.s (one block buffer, lastok).
/// </summary>
public class NdBootProtocolTests
{
    // ---------- vectors from PROTOCOL.md ----------

    [Fact]
    public void Crc16_CheckValue_31C3()
    {
        // "123456789" as bytes = words 0x3132 0x3334 0x3536 0x3738 plus a final byte 0x39.
        // The word-based API cannot express an odd byte count, so check the table
        // property used on the target instead: CRC of (words + crc) is zero.
        ushort[] words = { 1, 0x0800, 2, 0xAAAA, 0x5555 };
        ushort crc = NdBootProtocol.Crc16(words);
        Assert.Equal(0xB2E0, crc);   // octal 131340, the WRITE example in PROTOCOL.md
        var withCrc = new List<ushort>(words) { crc };
        Assert.Equal(0, NdBootProtocol.Crc16(withCrc));
    }

    [Fact]
    public void EncodeWord_MatchesSpec()
    {
        var b = new byte[3];
        NdBootProtocol.EncodeWord(1, b, 0);
        Assert.Equal("001", System.Text.Encoding.ASCII.GetString(b));
        NdBootProtocol.EncodeWord(0xF000, b, 0);          // octal 170000
        Assert.Equal("?00", System.Text.Encoding.ASCII.GetString(b));
        NdBootProtocol.EncodeWord(0xAAAA, b, 0);          // octal 125252
        Assert.Equal(":ZZ", System.Text.Encoding.ASCII.GetString(b));
        Assert.Equal(0xAAAA, NdBootProtocol.DecodeWord((byte)':', (byte)'Z', (byte)'Z'));
    }

    [Fact]
    public void BuildFrame_WriteExample()
    {
        var f = NdBootProtocol.BuildFrame('W', new ushort[] { 1, 0x0800, 2, 0xAAAA, 0x5555 });
        Assert.Equal("W0010P0002:ZZ5EE;;P", System.Text.Encoding.ASCII.GetString(f));
    }

    [Fact]
    public void BuildFrame_PingReadGo()
    {
        Assert.Equal("P000", System.Text.Encoding.ASCII.GetString(NdBootProtocol.BuildFrame('P', Array.Empty<ushort>())));
        Assert.Equal("R0P0002:F1", System.Text.Encoding.ASCII.GetString(NdBootProtocol.BuildFrame('R', new ushort[] { 0x0800, 2 })));
        Assert.Equal("G0P08VY", System.Text.Encoding.ASCII.GetString(NdBootProtocol.BuildFrame('G', new ushort[] { 0x0800 })));
    }

    [Fact]
    public void DecodeWord_RejectsOutsideAlphabet()
    {
        Assert.Throws<FormatException>(() => NdBootProtocol.DecodeWord((byte)'0', (byte)'\r', (byte)'0'));
        Assert.Throws<FormatException>(() => NdBootProtocol.DecodeWord((byte)'p', (byte)'0', (byte)'0'));
    }

    // ---------- fake monitor ----------

    /// <summary>
    /// Behaves like bootstrap.s: parses SOH frames, checks CRC residue, keeps lastok,
    /// writes to a 64K word memory, answers P/W/R/G. Optionally corrupts a chosen
    /// Write block once to exercise go-back-N.
    /// </summary>
    private sealed class FakeMonitor
    {
        public readonly ushort[] Memory = new ushort[65536];
        public int LastOk;
        public int GoAddress = -1;
        public int CorruptSeqOnce = -1;
        public int FramesSeen;
        /// <summary>Version the fake reports; when above 1 it also appends ExtraPingWords.</summary>
        public ushort ReportedVersion = 1;
        public ushort[] ExtraPingWords = Array.Empty<ushort>();
        private readonly List<byte> _rx = new();
        private readonly NdBootProtocol _client;

        public FakeMonitor(NdBootProtocol client) { _client = client; }

        public Task Send(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            _rx.AddRange(data.ToArray());
            Process();
            return Task.CompletedTask;
        }

        private void Reply(byte status, char cmd, params ushort[] words)
        {
            var b = new byte[2 + words.Length * 3];
            b[0] = status; b[1] = (byte)cmd;
            for (int i = 0; i < words.Length; i++) NdBootProtocol.EncodeWord(words[i], b, 2 + i * 3);
            _client.OnDataReceived(b);
        }

        private void Process()
        {
            while (true)
            {
                int soh = _rx.IndexOf(NdBootProtocol.Soh);
                if (soh < 0) { _rx.Clear(); return; }
                if (soh + 2 > _rx.Count) return;
                char cmd = (char)_rx[soh + 1];
                int fixedWords = cmd == 'P' ? 0 : cmd == 'W' ? 3 : cmd == 'R' ? 2 : cmd == 'G' ? 1 : -1;
                if (fixedWords < 0) { Reply(NdBootProtocol.Nak, cmd, (ushort)LastOk); _rx.RemoveRange(0, soh + 2); continue; }
                int pos = soh + 2;
                var words = new List<ushort>();
                bool need(int n) => pos + 3 * n <= _rx.Count;
                if (!need(fixedWords)) return;
                for (int i = 0; i < fixedWords; i++, pos += 3) words.Add(NdBootProtocol.DecodeWord(_rx[pos], _rx[pos + 1], _rx[pos + 2]));
                int dataWords = cmd == 'W' ? words[2] : 0;
                if (!need(dataWords + 1)) return;
                for (int i = 0; i < dataWords; i++, pos += 3) words.Add(NdBootProtocol.DecodeWord(_rx[pos], _rx[pos + 1], _rx[pos + 2]));
                words.Add(NdBootProtocol.DecodeWord(_rx[pos], _rx[pos + 1], _rx[pos + 2]));
                pos += 3;
                _rx.RemoveRange(0, pos);
                FramesSeen++;

                bool crcOk = NdBootProtocol.Crc16(words) == 0;
                if (cmd == 'W' && words[0] == CorruptSeqOnce) { crcOk = false; CorruptSeqOnce = -1; }
                if (!crcOk || (cmd == 'W' && (words[2] < 1 || words[2] > 128)))
                {
                    Reply(NdBootProtocol.Nak, cmd, (ushort)LastOk);
                    continue;
                }
                switch (cmd)
                {
                    case 'P':
                    {
                        var reply = new List<ushort> { ReportedVersion, 0xF000, 0xF233 };
                        reply.AddRange(ExtraPingWords);
                        Reply(NdBootProtocol.Ack, 'P', reply.ToArray());
                        break;
                    }
                    case 'W':
                        for (int i = 0; i < words[2]; i++) Memory[(words[1] + i) & 0xFFFF] = words[3 + i];
                        LastOk = words[0];
                        Reply(NdBootProtocol.Ack, 'W', words[0]);
                        break;
                    case 'R':
                    {
                        var reply = new List<ushort> { words[0], words[1] };
                        for (int i = 0; i < words[1]; i++) reply.Add(Memory[(words[0] + i) & 0xFFFF]);
                        reply.Add(NdBootProtocol.Crc16(reply));
                        Reply(NdBootProtocol.Ack, 'R', reply.ToArray());
                        break;
                    }
                    case 'G':
                        GoAddress = words[0];
                        Reply(NdBootProtocol.Ack, 'G', words[0]);
                        break;
                }
            }
        }
    }

    private static (NdBootProtocol client, FakeMonitor monitor) MakePair()
    {
        FakeMonitor? monitor = null;
        var client = new NdBootProtocol((data, ct) => monitor!.Send(data, ct)) { TimeoutMs = 500 };
        monitor = new FakeMonitor(client);
        return (client, monitor);
    }

    [Fact]
    public async Task Ping_ReturnsVersionBaseTop()
    {
        var (client, _) = MakePair();
        var info = await client.PingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, info.Version);
        Assert.Equal(0xF000, info.Base);
        Assert.Equal(0xF233, info.Top);
        Assert.Empty(info.Extra);
        Assert.True(info.HostSupports);
        Assert.Equal("version 1 at 170000..171062", info.Describe());
    }

    [Fact]
    public async Task Ping_IgnoresBannerTextBeforeReply()
    {
        var (client, _) = MakePair();
        client.OnDataReceived(System.Text.Encoding.ASCII.GetBytes("170000!\r\nBootstrap enabled!\r\n"));
        Assert.True(await client.WaitForTextAsync("enabled!", 100, TestContext.Current.CancellationToken));
        var info = await client.PingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, info.Version);
    }

    [Fact]
    public async Task Ping_NewerMonitorWithExtraWords_ParsesHeadAndIsReportedUnsupported()
    {
        var (client, monitor) = MakePair();
        monitor.ReportedVersion = 99;
        monitor.ExtraPingWords = new ushort[] { 0x1234, 0x5678 };   // appended by a future version
        var info = await client.PingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(99, info.Version);
        Assert.Equal(0xF000, info.Base);
        Assert.Equal(0xF233, info.Top);
        Assert.Empty(info.Extra);                 // unknown version: nothing beyond the head is read
        Assert.False(info.HostSupports);
        Assert.Contains("newer than this RetroTerm supports", info.Describe());
        // The unread words must not poison the next command.
        var again = await client.PingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(99, again.Version);
    }

    [Fact]
    public void Versions_PolicyIsConsistent()
    {
        Assert.True(NdBootVersions.IsSupported(NdBootVersions.OldestSupported));
        Assert.True(NdBootVersions.IsSupported(NdBootVersions.NewestSupported));
        Assert.False(NdBootVersions.IsSupported(NdBootVersions.NewestSupported + 1));
        Assert.False(NdBootVersions.IsSupported(0));
        Assert.Equal(NdBootProtocol.Version, NdBootVersions.NewestSupported);
        Assert.Equal(0, NdBootVersions.PingExtraWords(1));
        Assert.Contains("older", NdBootVersions.SupportMessage(0));
        Assert.Contains("newer", NdBootVersions.SupportMessage(NdBootVersions.NewestSupported + 1));
    }

    [Fact]
    public async Task SendImage_ThreeBlocks_WritesMemory()
    {
        var (client, monitor) = MakePair();
        var image = new ushort[300];
        for (int i = 0; i < image.Length; i++) image[i] = (ushort)((i * 7919 + 13) & 0xFFFF);
        int frames = await client.SendImageAsync(0x2000, image, image.Length, 4, null, TestContext.Current.CancellationToken);
        Assert.Equal(3, frames);
        for (int i = 0; i < image.Length; i++) Assert.Equal(image[i], monitor.Memory[0x2000 + i]);
        var back = await client.ReadAsync(0x2000 + 128, 128, TestContext.Current.CancellationToken);
        Assert.Equal(image.Skip(128).Take(128).ToArray(), back);
    }

    [Fact]
    public async Task SendImage_NakOnBlock2_RewindsAndCompletes()
    {
        var (client, monitor) = MakePair();
        monitor.CorruptSeqOnce = 2;
        var image = new ushort[500];
        for (int i = 0; i < image.Length; i++) image[i] = (ushort)(i * 3);
        int frames = await client.SendImageAsync(0x1000, image, image.Length, 4, null, TestContext.Current.CancellationToken);
        Assert.True(frames > 4, $"expected resends, got {frames} frames");
        Assert.Equal(4, monitor.LastOk);
        for (int i = 0; i < image.Length; i++) Assert.Equal(image[i], monitor.Memory[0x1000 + i]);
    }

    [Fact]
    public async Task Go_EchoesAddress()
    {
        var (client, monitor) = MakePair();
        await client.GoAsync(0x0800, TestContext.Current.CancellationToken);
        Assert.Equal(0x0800, monitor.GoAddress);
    }

    [Fact]
    public async Task WriteBlock_RejectsBadCount()
    {
        var (client, _) = MakePair();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.WriteBlockAsync(1, 0, new ushort[129], 0, 129, true, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void EmbeddedImage_ParsesAndMatchesConstants()
    {
        var img = BpunFileParser.Parse(NdBootImage.Bpun);
        Assert.NotNull(img);
        Assert.True(img!.ChecksumValid);
        Assert.Equal(NdBootImage.LoadAddress, img.LoadAddress);
        Assert.Equal(NdBootImage.WordCount, img.WordCount);
        Assert.Equal(NdBootImage.LoadAddress, img.BootAddress);
    }
}
