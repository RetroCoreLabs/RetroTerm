using System;
using System.IO;

namespace RetroTerm.Core.Opcom;

/// <summary>
/// Parsed BPUN file header and data.
/// </summary>
public sealed class BpunFileData
{
    /// <summary>
    /// Program start address from the BPUN preamble.
    /// Set by the last octal number before CR in the preamble (stored in P register by SEEK/SIKI).
    /// If Action=0, execution begins at this address.
    /// </summary>
    public ushort StartAddress { get; set; }

    /// <summary>
    /// Bootstrap loader entry address (from preamble '!' delimiter context).
    /// </summary>
    public ushort BootAddress { get; set; }

    /// <summary>
    /// Memory load address — where binary data words are written.
    /// Read as first 2-byte BIN word after '!' delimiter (stored in X register by microcode).
    /// </summary>
    public ushort LoadAddress { get; set; }

    /// <summary>
    /// Number of 16-bit words in the data section.
    /// </summary>
    public ushort WordCount { get; set; }

    /// <summary>
    /// Expected checksum from file (2 bytes big-endian after data words).
    /// The microcode (STLP at CS 002226) accumulates an additive sum in the L register:
    /// L += each data word. The file checksum is XORed with L — zero means match.
    /// </summary>
    public ushort Checksum { get; set; }

    /// <summary>
    /// Calculated additive checksum (sum of all data words mod 2^16).
    /// </summary>
    public ushort CalculatedChecksum { get; set; }

    /// <summary>
    /// Post-load action field (2 bytes from file).
    /// 0 = start execution at StartAddress (microcode issues COMM,START).
    /// Non-zero = stay in OPCOM with P set to StartAddress.
    /// Note: The microcode serial loader (ETLO1) reads this via ASS8 as ASCII octal + CR,
    /// but the on-disk BPUN format stores it as 2 binary bytes.
    /// </summary>
    public ushort Action { get; set; }

    /// <summary>
    /// Whether this is a FloMon (floppy boot sector) format variant.
    /// </summary>
    public bool IsFloMon { get; set; }

    /// <summary>
    /// Whether the checksum matched.
    /// </summary>
    public bool ChecksumValid => Checksum == CalculatedChecksum;

    /// <summary>
    /// The loaded words, indexed from 0. Word[i] loads at LoadAddress + i.
    /// </summary>
    public ushort[] Words { get; set; } = Array.Empty<ushort>();
}

/// <summary>
/// BPUN (Binary Punch) file format parser.
/// Ported from nd100x/src/ndlib/load_bpun.c.
///
/// BPUN format:
/// 1. Preamble: ASCII octal numbers with '/' (address) and '!' (end preamble) delimiters
/// 2. Address: 2 bytes big-endian - load address
/// 3. Count: 2 bytes big-endian - number of words
/// 4. Data: count * 2 bytes big-endian words
/// 5. Checksum: 2 bytes big-endian
/// 6. Action: 2 bytes big-endian
///
/// FloMon variant: when address=0, count=0, checksum=0 -> switches to 4-byte-per-word format.
/// </summary>
public static class BpunFileParser
{
    private enum LoadState
    {
        Preamble,
        Address,
        Count,
        Data,
        Checksum,
        Action,
        FloMonCount,
        FloMonLoad,
    }

    /// <summary>
    /// Parses a BPUN file from disk.
    /// </summary>
    public static BpunFileData? Parse(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        byte[] fileBytes = File.ReadAllBytes(filePath);
        return Parse(fileBytes);
    }

    /// <summary>
    /// Parses BPUN data from a byte array.
    /// </summary>
    public static BpunFileData? Parse(byte[] data)
    {
        if (data == null || data.Length < 4) return null;

        var result = new BpunFileData();
        var state = LoadState.Preamble;

        // Preamble parsing state
        char[] tmpString = new char[51];
        int tmpStringPos = 0;
        ushort currentLocationCounter = 0;
        ushort loadAddress = 0;
        ushort lastValue = 0;
        ushort calculatedChecksum = 0;
        int dataCounter = 0;
        ushort dataLoadAddress = 0;
        int wordIndex = 0;

        int pos = 0;
        while (pos < data.Length)
        {
            int b = data[pos++];

            switch (state)
            {
                case LoadState.Preamble:
                {
                    char c = (char)(b & 0x7F);

                    if (c == '!')
                    {
                        if (tmpStringPos > 0)
                        {
                            int tmp = ParseOctalString(tmpString, tmpStringPos);
                            if (tmp >= 0)
                                loadAddress = (ushort)tmp;
                        }
                        if (loadAddress == result.StartAddress)
                            result.BootAddress = lastValue;
                        else
                            result.BootAddress = loadAddress;

                        state = LoadState.Address;
                        tmpStringPos = 0;
                        continue;
                    }
                    else if (c == '/')
                    {
                        if (tmpStringPos > 0)
                        {
                            int tmp = ParseOctalString(tmpString, tmpStringPos);
                            if (tmp >= 0)
                            {
                                currentLocationCounter = (ushort)tmp;
                                lastValue = currentLocationCounter;
                                result.StartAddress = currentLocationCounter;
                                if (loadAddress == 0)
                                    loadAddress = currentLocationCounter;
                            }
                        }
                        tmpStringPos = 0;
                    }
                    else if (c >= '0' && c <= '9')
                    {
                        if (tmpStringPos < 50)
                            tmpString[tmpStringPos++] = c;
                    }
                    else if (c == '\r')
                    {
                        if (tmpStringPos > 0)
                        {
                            int tmp = ParseOctalString(tmpString, tmpStringPos);
                            if (tmp >= 0)
                                lastValue = (ushort)tmp;
                            tmpStringPos = 0;
                        }
                    }
                    break;
                }

                case LoadState.Address:
                    result.LoadAddress = (ushort)(b << 8);
                    if (pos >= data.Length) return null;
                    result.LoadAddress |= (ushort)(data[pos++] & 0xFF);
                    dataLoadAddress = result.LoadAddress;
                    state = LoadState.Count;
                    break;

                case LoadState.Count:
                    result.WordCount = (ushort)(b << 8);
                    if (pos >= data.Length) return null;
                    result.WordCount |= (ushort)(data[pos++] & 0xFF);
                    dataCounter = result.WordCount * 2;
                    result.Words = new ushort[result.WordCount];
                    wordIndex = 0;
                    state = LoadState.Data;
                    break;

                case LoadState.Data:
                {
                    ushort dataWord = 0;
                    if (dataCounter > 0)
                    {
                        dataCounter--;
                        dataWord = (ushort)((b << 8) & 0xFF00);
                    }

                    if (dataCounter > 0)
                    {
                        if (pos >= data.Length) return null;
                        b = data[pos++];
                        dataCounter--;
                        dataWord |= (ushort)(b & 0xFF);
                    }

                    if (wordIndex < result.Words.Length)
                    {
                        result.Words[wordIndex++] = dataWord;
                    }

                    calculatedChecksum = (ushort)(calculatedChecksum + dataWord);

                    if (dataCounter == 0)
                        state = LoadState.Checksum;
                    break;
                }

                case LoadState.Checksum:
                    result.Checksum = (ushort)(b << 8);
                    if (pos >= data.Length) return null;
                    result.Checksum |= (ushort)(data[pos++] & 0xFF);
                    result.CalculatedChecksum = calculatedChecksum;

                    if (result.LoadAddress == 0 && result.WordCount == 0 && result.Checksum == 0)
                    {
                        state = LoadState.FloMonCount;
                    }
                    else
                    {
                        state = LoadState.Action;
                    }
                    break;

                case LoadState.Action:
                    result.Action = (ushort)(b << 8);
                    if (pos >= data.Length) return null;
                    result.Action |= (ushort)(data[pos++] & 0xFF);
                    return result;

                case LoadState.FloMonCount:
                    result.IsFloMon = true;
                    result.WordCount = (ushort)b;
                    result.Words = new ushort[result.WordCount];
                    wordIndex = 0;
                    state = LoadState.FloMonLoad;
                    break;

                case LoadState.FloMonLoad:
                {
                    ushort floWords = 0;
                    while (floWords < result.WordCount)
                    {
                        if (b != 0) return null;

                        if (pos >= data.Length) return null;
                        b = data[pos++];
                        ushort dataWord = (ushort)(b << 8);

                        if (pos >= data.Length) return null;
                        b = data[pos++];
                        if (b != 0) return null;

                        if (pos >= data.Length) return null;
                        b = data[pos++];
                        dataWord |= (ushort)(b & 0xFF);

                        if (pos >= data.Length) return null;
                        b = data[pos++];
                        if (b != 0) return null;

                        if (wordIndex < result.Words.Length)
                            result.Words[wordIndex++] = dataWord;

                        floWords++;

                        if (floWords < result.WordCount && pos < data.Length)
                            b = data[pos++];
                    }
                    return result;
                }
            }
        }

        return null;
    }

    private static int ParseOctalString(char[] chars, int length)
    {
        int result = 0;
        for (int i = 0; i < length; i++)
        {
            char c = chars[i];
            if (c < '0' || c > '7') return -1;
            result = (result << 3) | (c - '0');
        }
        return result;
    }

    /// <summary>
    /// Attempts to detect if a file is in BPUN format by checking for the preamble pattern.
    /// </summary>
    public static bool IsBpunFile(byte[] data)
    {
        if (data == null || data.Length < 10) return false;

        // BPUN preamble contains ASCII octal digits, '/' and '!' delimiters
        // Look for the '!' delimiter that ends the preamble
        for (int i = 0; i < Math.Min(data.Length, 512); i++)
        {
            byte b = (byte)(data[i] & 0x7F);
            if (b == '!') return true;
        }
        return false;
    }
}
