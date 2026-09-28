using RetroTerm.Core.Transfer;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Raised when the NDBoot monitor answers a frame with NAK. LastOk is the sequence
/// number of the last Write block the monitor accepted (0 if none).
/// </summary>
public sealed class NdBootNakException : Exception
{
    public char Command { get; }
    public int LastOk { get; }

    public NdBootNakException(char command, int lastOk)
        : base($"NAK {command} lastok={Convert.ToString(lastOk, 8)}")
    {
        Command = command;
        LastOk = lastOk;
    }
}

/// <summary>
/// What a PING told us about the monitor on the ND.
/// </summary>
public sealed class NdBootMonitorInfo
{
    /// <summary>Protocol version the monitor speaks (word 1 of the PING reply).</summary>
    public int Version { get; }
    /// <summary>First word of the monitor image.</summary>
    public int Base { get; }
    /// <summary>First word after the monitor and its block buffer; Base..Top-1 is reserved.</summary>
    public int Top { get; }
    /// <summary>Words after ver/base/top that this host knew how to read for that version.</summary>
    public ushort[] Extra { get; }

    public NdBootMonitorInfo(int version, int baseAddress, int top, ushort[] extra)
    {
        Version = version;
        Base = baseAddress;
        Top = top;
        Extra = extra;
    }

    /// <summary>True when this RetroTerm can drive a monitor of this version.</summary>
    public bool HostSupports => NdBootVersions.IsSupported(Version);

    public string Describe()
    {
        string range = $"{Convert.ToString(Base, 8).PadLeft(6, '0')}..{Convert.ToString(Top - 1, 8).PadLeft(6, '0')}";
        return $"version {Version} at {range}" + (HostSupports ? "" : " (" + NdBootVersions.SupportMessage(Version) + ")");
    }
}

/// <summary>
/// Version policy for the NDBoot wire protocol. The rules (NDBoot docs/PROTOCOL.md,
/// section 13) are:
///  - A monitor of version N answers every frame defined for versions 1..N exactly as
///    that version defined it. New versions may add commands and may APPEND words to
///    replies, never change or reorder existing ones.
///  - The PING reply always starts with version, base, top. A host reads those three,
///    then only the extra words it knows for that version, and ignores the rest.
///  - A host therefore works with any monitor from OldestSupported up to
///    NewestSupported; a newer monitor is reported, not driven.
/// To support a new version: raise NewestSupported, extend PingExtraWords and gate the
/// new commands on info.Version in the code that uses them.
/// </summary>
public static class NdBootVersions
{
    public const int OldestSupported = 1;
    public const int NewestSupported = 1;

    public static bool IsSupported(int version) => version >= OldestSupported && version <= NewestSupported;

    /// <summary>Number of PING reply words after ver/base/top that a given version defines.</summary>
    public static int PingExtraWords(int version)
    {
        switch (version)
        {
            case 1: return 0;
            default: return 0;   // unknown version: read nothing beyond the fixed head
        }
    }

    public static string SupportMessage(int version)
    {
        if (version < OldestSupported)
            return $"monitor version {version} is older than this RetroTerm supports ({OldestSupported}..{NewestSupported}); reinstall the built-in monitor";
        if (version > NewestSupported)
            return $"monitor version {version} is newer than this RetroTerm supports ({OldestSupported}..{NewestSupported}); update RetroTerm";
        return $"monitor version {version} supported";
    }
}

/// <summary>
/// Host side of the NDBoot console download protocol (NDBoot docs/PROTOCOL.md, version 1).
///
/// The monitor (NdBootImage) runs on the ND at 0170000 and reads the console. Every
/// frame from the host is SOH, a command letter, then 16-bit words encoded as three
/// characters each from the contiguous alphabet 0x30..0x6F, the last word being a
/// CRC-16/XMODEM over the preceding words. Replies are ACK or NAK plus the command
/// letter plus words. Everything on the wire is 7-bit, so the line stays at 7E1.
///
/// Feed received bytes to <see cref="OnDataReceived"/>; send goes through the delegate
/// given to the constructor. All public methods are sequential: one command at a time.
/// </summary>
public sealed class NdBootProtocol
{
    public const int Version = 1;
    public const byte Soh = 0x01;
    public const byte Ack = 0x06;
    public const byte Nak = 0x15;
    public const int MaxBlockWords = 128;
    private const int Alpha0 = 0x30;

    private readonly SendBytesAsync _send;
    private readonly object _sync = new();
    private readonly List<byte> _pending = new();
    private readonly SemaphoreSlim _rxSignal = new(0, int.MaxValue);

    /// <summary>Reply timeout for one frame.</summary>
    public int TimeoutMs { get; set; } = 3000;

    /// <summary>
    /// When greater than 0, frames are sent in chunks of this many bytes with
    /// <see cref="ChunkDelayMs"/> between them. Needed only for the nd100x pipe
    /// transport (256-byte keyboard queue); a serial port paces itself.
    /// </summary>
    public int ChunkSize { get; set; }
    public int ChunkDelayMs { get; set; } = 10;

    public NdBootProtocol(SendBytesAsync send)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
    }

    // ==================== Encoding and CRC ====================

    public static ushort Crc16(IReadOnlyList<ushort> words, int offset = 0, int count = -1)
    {
        if (count < 0) count = words.Count - offset;
        int crc = 0;
        for (int i = offset; i < offset + count; i++)
        {
            ushort w = words[i];
            crc = Crc16Byte(crc, (byte)(w >> 8));
            crc = Crc16Byte(crc, (byte)(w & 0xFF));
        }
        return (ushort)crc;
    }

    private static int Crc16Byte(int crc, byte b)
    {
        crc ^= b << 8;
        for (int k = 0; k < 8; k++)
            crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        return crc;
    }

    public static void EncodeWord(ushort w, byte[] dst, int offset)
    {
        dst[offset] = (byte)(Alpha0 + ((w >> 12) & 0x0F));
        dst[offset + 1] = (byte)(Alpha0 + ((w >> 6) & 0x3F));
        dst[offset + 2] = (byte)(Alpha0 + (w & 0x3F));
    }

    public static ushort DecodeWord(byte c0, byte c1, byte c2)
    {
        int w = 0;
        foreach (byte c in new[] { c0, c1, c2 })
        {
            if (c < 0x30 || c > 0x6F)
                throw new FormatException($"character 0x{c:X2} outside the NDBoot alphabet");
            w = ((w << 6) + (c - Alpha0)) & 0xFFFF;
        }
        return (ushort)w;
    }

    /// <summary>Builds SOH cmd words.. crc with the CRC appended.</summary>
    public static byte[] BuildFrame(char command, IReadOnlyList<ushort> words)
    {
        var frame = new byte[2 + (words.Count + 1) * 3];
        frame[0] = Soh;
        frame[1] = (byte)command;
        int pos = 2;
        for (int i = 0; i < words.Count; i++, pos += 3)
            EncodeWord(words[i], frame, pos);
        EncodeWord(Crc16(words), frame, pos);
        return frame;
    }

    // ==================== Receive side ====================

    /// <summary>Feed every byte received from the console while NDBoot mode is active.</summary>
    public void OnDataReceived(byte[] data)
    {
        lock (_sync)
        {
            for (int i = 0; i < data.Length; i++)
                _pending.Add((byte)(data[i] & 0x7F));
        }
        _rxSignal.Release();
    }

    public void ClearPending()
    {
        lock (_sync) _pending.Clear();
    }

    /// <summary>Waits until <paramref name="text"/> has appeared on the line.</summary>
    public async Task<bool> WaitForTextAsync(string text, int timeoutMs, CancellationToken ct = default)
    {
        byte[] needle = System.Text.Encoding.ASCII.GetBytes(text);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            lock (_sync)
            {
                int idx = IndexOf(_pending, needle, 0);
                if (idx >= 0)
                {
                    _pending.RemoveRange(0, idx + needle.Length);
                    return true;
                }
            }
            int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0) return false;
            await _rxSignal.WaitAsync(remaining, ct).ConfigureAwait(false);
        }
    }

    private static int IndexOf(List<byte> hay, byte[] needle, int start)
    {
        for (int i = start; i + needle.Length <= hay.Count; i++)
        {
            int k = 0;
            while (k < needle.Length && hay[i + k] == needle[k]) k++;
            if (k == needle.Length) return i;
        }
        return -1;
    }

    /// <summary>
    /// Scans for ACK/NAK + command, then reads words. countFn returns the total number
    /// of words expected given those decoded so far (lets Read learn n from the reply).
    /// Throws NdBootNakException on NAK, TimeoutException when the monitor stays quiet.
    /// </summary>
    private async Task<List<ushort>> ReadReplyAsync(char command, Func<List<ushort>, int> countFn,
        int timeoutMs, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool complete;
            List<ushort>? words = null;
            byte status = 0;
            lock (_sync)
            {
                complete = TryParseReply(command, countFn, out status, out words);
            }
            if (complete)
            {
                if (status == Nak)
                    throw new NdBootNakException(command, words![0]);
                return words!;
            }
            int remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remaining <= 0)
                throw new TimeoutException($"no reply to {command} within {timeoutMs} ms");
            await _rxSignal.WaitAsync(remaining, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Must hold _sync. Consumes the reply from _pending when complete.</summary>
    private bool TryParseReply(char command, Func<List<ushort>, int> countFn, out byte status, out List<ushort>? words)
    {
        status = 0;
        words = null;
        int start = -1;
        for (int i = 0; i + 1 < _pending.Count; i++)
        {
            if ((_pending[i] == Ack || _pending[i] == Nak) && _pending[i + 1] == (byte)command)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return false;

        status = _pending[start];
        var list = new List<ushort>();
        int pos = start + 2;
        while (true)
        {
            int need = status == Nak ? 1 : countFn(list);
            if (list.Count >= need) break;
            if (pos + 3 > _pending.Count) return false;
            list.Add(DecodeWord(_pending[pos], _pending[pos + 1], _pending[pos + 2]));
            pos += 3;
        }
        _pending.RemoveRange(0, pos);
        words = list;
        return true;
    }

    // ==================== Send side ====================

    private async Task SendAsync(byte[] frame, CancellationToken ct)
    {
        if (ChunkSize <= 0)
        {
            await _send(frame, ct).ConfigureAwait(false);
            return;
        }
        for (int off = 0; off < frame.Length; off += ChunkSize)
        {
            int n = Math.Min(ChunkSize, frame.Length - off);
            await _send(new ReadOnlyMemory<byte>(frame, off, n), ct).ConfigureAwait(false);
            await Task.Delay(ChunkDelayMs, ct).ConfigureAwait(false);
        }
    }

    // ==================== Commands ====================

    /// <summary>
    /// PING. Reads version, base, top, then the extra words this host knows for that
    /// version (NdBootVersions.PingExtraWords); anything a newer monitor appends beyond
    /// that is left unread and discarded by the next ClearPending.
    /// </summary>
    public async Task<NdBootMonitorInfo> PingAsync(CancellationToken ct = default, int? timeoutMs = null)
    {
        ClearPending();
        await SendAsync(BuildFrame('P', Array.Empty<ushort>()), ct).ConfigureAwait(false);
        var w = await ReadReplyAsync('P',
            ws => 3 + (ws.Count >= 1 ? NdBootVersions.PingExtraWords(ws[0]) : 0),
            timeoutMs ?? TimeoutMs, ct).ConfigureAwait(false);
        var extra = new ushort[w.Count - 3];
        for (int i = 0; i < extra.Length; i++) extra[i] = w[3 + i];
        return new NdBootMonitorInfo(w[0], w[1], w[2], extra);
    }

    /// <summary>Sends one WRITE block. When wait is true, returns the ACKed seq.</summary>
    public async Task<int> WriteBlockAsync(int seq, int address, ushort[] words, int offset, int count,
        bool wait, CancellationToken ct = default)
    {
        if (count < 1 || count > MaxBlockWords)
            throw new ArgumentOutOfRangeException(nameof(count), "1..128 words per block");
        var payload = new ushort[3 + count];
        payload[0] = (ushort)seq;
        payload[1] = (ushort)address;
        payload[2] = (ushort)count;
        Array.Copy(words, offset, payload, 3, count);
        await SendAsync(BuildFrame('W', payload), ct).ConfigureAwait(false);
        if (!wait) return -1;
        var w = await ReadReplyAsync('W', _ => 1, TimeoutMs, ct).ConfigureAwait(false);
        return w[0];
    }

    /// <summary>READ n words from address; the reply's CRC is verified.</summary>
    public async Task<ushort[]> ReadAsync(int address, int count, CancellationToken ct = default)
    {
        ClearPending();
        await SendAsync(BuildFrame('R', new ushort[] { (ushort)address, (ushort)count }), ct).ConfigureAwait(false);
        var w = await ReadReplyAsync('R', ws => ws.Count >= 2 ? 2 + ws[1] + 1 : 2,
            TimeoutMs + count * 10, ct).ConfigureAwait(false);
        if (w[0] != address || w[1] != count)
            throw new InvalidDataException($"READ echoed {Convert.ToString(w[0], 8)}/{w[1]}, expected {Convert.ToString(address, 8)}/{count}");
        if (Crc16(w) != 0)
            throw new InvalidDataException("READ reply CRC mismatch");
        var data = new ushort[count];
        for (int i = 0; i < count; i++) data[i] = w[2 + i];
        return data;
    }

    /// <summary>GO: the monitor ACKs and jumps. After this the monitor is gone.</summary>
    public async Task GoAsync(int address, CancellationToken ct = default)
    {
        ClearPending();
        await SendAsync(BuildFrame('G', new ushort[] { (ushort)address }), ct).ConfigureAwait(false);
        var w = await ReadReplyAsync('G', _ => 1, TimeoutMs, ct).ConfigureAwait(false);
        if (w[0] != address)
            throw new InvalidDataException($"GO echoed {Convert.ToString(w[0], 8)}, expected {Convert.ToString(address, 8)}");
    }

    /// <summary>
    /// Go-back-N upload of count words to address. progress(wordsDone, totalWords).
    /// Returns the number of frames sent including resends.
    /// </summary>
    public async Task<int> SendImageAsync(int address, ushort[] words, int count, int window,
        Action<int, int>? progress, CancellationToken ct = default)
    {
        if (window < 1) window = 1;
        int blocks = (count + MaxBlockWords - 1) / MaxBlockWords;
        int acked = 0;
        int nextSend = 1;
        int sent = 0;
        int silentRounds = 0;
        ClearPending();

        while (acked < blocks)
        {
            ct.ThrowIfCancellationRequested();
            if (silentRounds >= 5)
                throw new TimeoutException($"monitor stopped answering after block {acked}");
            while (nextSend <= blocks && nextSend - acked <= window)
            {
                int off = (nextSend - 1) * MaxBlockWords;
                int n = Math.Min(MaxBlockWords, count - off);
                await WriteBlockAsync(nextSend, address + off, words, off, n, false, ct).ConfigureAwait(false);
                sent++;
                nextSend++;
            }
            try
            {
                var w = await ReadReplyAsync('W', _ => 1, TimeoutMs, ct).ConfigureAwait(false);
                int seq = w[0];
                silentRounds = 0;
                if (seq == acked + 1)
                {
                    acked = seq;
                    progress?.Invoke(Math.Min(count, acked * MaxBlockWords), count);
                }
                else if (seq > acked + 1)
                {
                    throw new InvalidDataException($"ACK for block {seq}, expected {acked + 1}");
                }
            }
            catch (NdBootNakException e)
            {
                acked = e.LastOk;
                nextSend = acked + 1;
                ClearPending();
            }
            catch (TimeoutException)
            {
                silentRounds++;
                nextSend = acked + 1;
                ClearPending();
            }
        }
        return sent;
    }
}
