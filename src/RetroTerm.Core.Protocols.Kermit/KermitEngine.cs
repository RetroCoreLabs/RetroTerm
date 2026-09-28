using System.Text;

namespace RetroTerm.Core.Protocols.Kermit;

/// <summary>
/// Transport-agnostic Kermit protocol engine.
/// Processes incoming bytes and emits outgoing bytes through events.
///
/// The caller owns the transport layer. Usage pattern:
/// <code>
/// var engine = new KermitEngine(options, fileHandler);
/// engine.DataToSend += bytes => transport.Write(bytes.Span);
/// engine.BeginSend("myfile.dat");
/// // ... when bytes arrive from transport:
/// engine.ProcessReceivedData(receivedBytes);
/// </code>
///
/// The engine implements the Kermit protocol state machine for:
/// - Sending files (S → F → D... → Z → B)
/// - Receiving files (wait for S → F → D... → Z → B)
/// - Server mode (accept incoming file transfers)
///
/// Ported from C-Kermit ckcpro.w (state machine) and ckcfns.c (protocol functions).
/// </summary>
public sealed class KermitEngine
{
    // --- Configuration ---
    private readonly KermitOptions _options;
    private readonly IKermitFileHandler _fileHandler;

    // --- State ---
    private KermitState _state;
    private byte _sequence;
    private int _retryCount;
    private string? _currentFileName;

    // --- Negotiated parameters ---
    private KermitParameters _localParams;
    private KermitParameters _remoteParams;
    private bool _use8BitQuoting;
    private byte _ebqPrefix;
    private byte _ctlPrefix;
    private byte _remoteEol;
    private int _maxSendDataLength;
    private int _negotiatedCheckType = 1; // 1=6-bit, 2=12-bit, 3=CRC-16
    private bool _checkTypeNegotiated;    // false until S/Y exchange completes

    // --- Receive buffer: accumulates bytes until a complete packet is found ---
    private readonly byte[] _receiveBuffer;
    private int _receiveLength;

    // --- Send buffer: builds outgoing packets ---
    private readonly byte[] _sendBuffer;

    // --- Last sent packet for retransmission on NAK ---
    private readonly byte[] _lastSentPacket;
    private int _lastSentLength;

    // --- Data encoding/decoding scratch buffers ---
    private readonly byte[] _encodeBuffer;
    private readonly byte[] _decodeBuffer;

    // --- File read buffer for sender ---
    private readonly byte[] _fileReadBuffer;
    private int _fileReadOffset;
    private int _fileReadLength;

    /// <summary>
    /// Maximum internal buffer size.
    /// </summary>
    private const int BufferSize = 1024;

    /// <summary>
    /// Creates a new Kermit protocol engine.
    /// </summary>
    /// <param name="options">
    /// Protocol configuration.
    /// </param>
    /// <param name="fileHandler">
    /// File I/O abstraction.
    /// </param>
    public KermitEngine(KermitOptions options, IKermitFileHandler fileHandler)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _fileHandler = fileHandler ?? throw new ArgumentNullException(nameof(fileHandler));

        _localParams = KermitParameters.FromOptions(options);
        _remoteParams = new KermitParameters();
        _ctlPrefix = options.ControlQuote;
        _ebqPrefix = options.EighthBitQuote;
        _use8BitQuoting = options.Use8BitQuoting;
        _remoteEol = options.EndOfLine;
        _maxSendDataLength = KermitConst.DefaultPacketSize - 3; // len - seq - type - check

        _receiveBuffer = new byte[BufferSize];
        _sendBuffer = new byte[BufferSize];
        _lastSentPacket = new byte[BufferSize];
        _encodeBuffer = new byte[BufferSize];
        _decodeBuffer = new byte[BufferSize];
        _fileReadBuffer = new byte[BufferSize];
    }

    // --- Events ---

    /// <summary>
    /// Fired when the engine has bytes to transmit on the wire.
    /// The <see cref="ReadOnlyMemory{T}"/> is backed by an internal buffer;
    /// the handler must consume the data before returning.
    /// </summary>
    public event Action<ReadOnlyMemory<byte>>? DataToSend;

    /// <summary>
    /// Fired when a file transfer begins (carries the filename).
    /// </summary>
    public event Action<string>? FileStarting;

    /// <summary>
    /// Fired when a file transfer completes (carries the filename).
    /// </summary>
    public event Action<string>? FileCompleted;

    /// <summary>
    /// Fired when the entire transfer session completes.
    /// </summary>
    public event Action? TransferCompleted;

    /// <summary>
    /// Fired on protocol errors (carries the error message).
    /// </summary>
    public event Action<string>? ErrorOccurred;

    // --- Public properties ---

    /// <summary>
    /// Current protocol state.
    /// </summary>
    public KermitState State => _state;

    /// <summary>
    /// Current packet sequence number.
    /// </summary>
    public byte SequenceNumber => _sequence;

    /// <summary>
    /// Whether 8th-bit quoting was negotiated.
    /// </summary>
    public bool NegotiatedUse8BitQuoting => _use8BitQuoting;

    /// <summary>
    /// The negotiated 8th-bit quote prefix character.
    /// </summary>
    public byte NegotiatedEbqPrefix => _ebqPrefix;

    /// <summary>
    /// The negotiated control quote prefix character.
    /// </summary>
    public byte NegotiatedCtlPrefix => _ctlPrefix;

    /// <summary>
    /// The negotiated maximum send data length per packet.
    /// </summary>
    public int NegotiatedMaxSendDataLength => _maxSendDataLength;

    /// <summary>
    /// Current retry count for the current packet.
    /// </summary>
    public int RetryCount => _retryCount;

    // --- Public commands ---

    /// <summary>
    /// Initiates sending a file. The engine will immediately emit the
    /// Send-Init (S) packet via <see cref="DataToSend"/> and transition
    /// to <see cref="KermitState.SendingInit"/>.
    /// </summary>
    /// <param name="fileName">
    /// Name of the file to send.
    /// </param>
    public void BeginSend(string fileName)
    {
        if (_state != KermitState.Idle)
            throw new InvalidOperationException($"Cannot begin send in state {_state}");

        _currentFileName = fileName;
        _sequence = 0;
        _retryCount = 0;
        _fileReadOffset = 0;
        _fileReadLength = 0;

        _localParams = KermitParameters.FromOptions(_options);

        // Build and send S packet with our parameters
        Span<byte> paramData = stackalloc byte[16];
        int paramLen = _localParams.Encode(paramData);

        SendPacket(PacketType.SendInit, paramData[..paramLen]);
        _state = KermitState.SendingInit;
    }

    /// <summary>
    /// Enters receive mode, waiting for the remote sender's Send-Init (S) packet.
    /// </summary>
    public void BeginReceive()
    {
        if (_state != KermitState.Idle)
            throw new InvalidOperationException($"Cannot begin receive in state {_state}");

        _sequence = 0;
        _retryCount = 0;
        _localParams = KermitParameters.FromOptions(_options);
        _state = KermitState.ReceivingInit;
    }

    /// <summary>
    /// Enters server mode, waiting for client commands.
    /// Currently supports receiving files (responds to S packets).
    /// </summary>
    public void BeginServe()
    {
        if (_state != KermitState.Idle)
            throw new InvalidOperationException($"Cannot begin serve in state {_state}");

        _sequence = 0;
        _retryCount = 0;
        _localParams = KermitParameters.FromOptions(_options);
        _state = KermitState.Serving;
    }

    /// <summary>
    /// Cancels the current operation by sending an Error packet.
    /// </summary>
    public void Cancel()
    {
        if (_state == KermitState.Idle || _state == KermitState.Completed || _state == KermitState.Failed)
            return;

        SendError("Transfer cancelled");
        _fileHandler.CloseFile();
        _state = KermitState.Failed;
    }

    /// <summary>
    /// Called by the transport layer when a timeout occurs (no data received
    /// within the negotiated timeout period). Retransmits the last sent packet
    /// or fails if max retries exceeded.
    ///
    /// The engine itself has no clock — the caller is responsible for managing
    /// timers. Typical usage in KermitFileTransfer:
    /// <code>
    /// // After engine emits DataToSend, start a timer for Options.Timeout seconds.
    /// // If timer fires before next ProcessReceivedData call:
    /// engine.NotifyTimeout();
    /// </code>
    /// </summary>
    public void NotifyTimeout()
    {
        if (_state == KermitState.Idle || _state == KermitState.Completed || _state == KermitState.Failed)
            return;

        RetryOrFail();
    }

    /// <summary>
    /// Resets the engine to idle state for reuse.
    /// </summary>
    public void Reset()
    {
        _state = KermitState.Idle;
        _sequence = 0;
        _retryCount = 0;
        _receiveLength = 0;
        _fileReadOffset = 0;
        _fileReadLength = 0;
        _currentFileName = null;
        _negotiatedCheckType = 1;
        _checkTypeNegotiated = false;
    }

    /// <summary>
    /// Feeds bytes received from the transport layer into the engine.
    /// The engine will parse packets and advance the state machine,
    /// potentially firing <see cref="DataToSend"/> with response packets.
    /// </summary>
    /// <param name="data">
    /// Raw bytes from the transport.
    /// </param>
    public void ProcessReceivedData(ReadOnlySpan<byte> data)
    {
        // Append to receive buffer
        int copyLen = Math.Min(data.Length, _receiveBuffer.Length - _receiveLength);
        data[..copyLen].CopyTo(_receiveBuffer.AsSpan(_receiveLength));
        _receiveLength += copyLen;

        // Try to extract and process complete packets
        while (TryExtractPacket(out KermitPacket packet))
        {
            if (!packet.CheckValid)
            {
                // Bad checksum: send NAK for expected sequence
                SendNak(_sequence);
                _retryCount++;
                if (_retryCount > _options.MaxRetries)
                {
                    Fail("Too many checksum errors");
                    return;
                }
                continue;
            }

            _retryCount = 0;
            ProcessPacket(in packet);

            if (_state == KermitState.Failed || _state == KermitState.Completed)
                return;
        }
    }

    // --- Packet extraction from receive buffer ---

    /// <summary>
    /// Tries to extract one complete packet from the receive buffer.
    /// On success, the consumed bytes are removed from the buffer.
    /// </summary>
    private bool TryExtractPacket(out KermitPacket packet)
    {
        packet = default;

        // Find SOH marker
        int sohIndex = -1;
        for (int i = 0; i < _receiveLength; i++)
        {
            byte b = StripParityIfNeeded(_receiveBuffer[i]);
            if (b == _options.Mark)
            {
                sohIndex = i;
                break;
            }
        }

        if (sohIndex < 0)
        {
            // No SOH found: discard all data
            _receiveLength = 0;
            return false;
        }

        // Discard bytes before SOH
        if (sohIndex > 0)
        {
            CompactReceiveBuffer(sohIndex);
        }

        // Need at least SOH + LEN + SEQ + TYPE = 4 bytes
        if (_receiveLength < 4) return false;

        byte lenChar = StripParityIfNeeded(_receiveBuffer[1]);
        int len = KermitConst.UnChar(lenChar);

        // Total packet bytes: SOH(1) + LEN(1) + len payload bytes + EOL(1)
        // But EOL might not have arrived yet, and len includes SEQ+TYPE+DATA+CHECK.
        // We need SOH + LEN + len bytes minimum (EOL is optional for parsing).
        int packetEnd = 2 + len;
        if (_receiveLength < packetEnd) return false;

        // Parse fields (strip parity from each byte)
        byte seq = (byte)KermitConst.UnChar(StripParityIfNeeded(_receiveBuffer[2]));
        byte type = StripParityIfNeeded(_receiveBuffer[3]);

        // S/I packets and their ACKs always use type 1; others use negotiated type
        bool isInitPacket = type == PacketType.SendInit || type == PacketType.Init;
        // ACK to S/I: if we haven't negotiated yet, it's still type 1
        int checkType = (_checkTypeNegotiated && !isInitPacket) ? _negotiatedCheckType : 1;
        int checkLen = checkType == 3 ? 3 : checkType == 2 ? 2 : 1;

        // Data length = len - 2 (SEQ + TYPE) - checkLen
        int dataLen = len - 2 - checkLen;
        if (dataLen < 0) dataLen = 0;

        // Copy and strip parity from data
        byte[] dataBytes = new byte[dataLen];
        for (int i = 0; i < dataLen; i++)
        {
            dataBytes[i] = StripParityIfNeeded(_receiveBuffer[4 + i]);
        }

        // Verify checksum: covers LEN through end of DATA
        int checksumCoverLen = 1 + len - checkLen; // LEN + SEQ + TYPE + DATA
        Span<byte> checksumSpan = stackalloc byte[checksumCoverLen];
        for (int i = 0; i < checksumCoverLen; i++)
        {
            checksumSpan[i] = StripParityIfNeeded(_receiveBuffer[1 + i]);
        }

        bool checkValid;
        int checkStart = 1 + checksumCoverLen; // position of first check byte

        switch (checkType)
        {
            case 3:
            {
                int crc = KermitChecksum.ComputeType3(checksumSpan);
                byte c1 = KermitConst.ToChar((crc >> 12) & 0x0F);
                byte c2 = KermitConst.ToChar((crc >> 6) & 0x3F);
                byte c3 = KermitConst.ToChar(crc & 0x3F);
                checkValid = c1 == StripParityIfNeeded(_receiveBuffer[checkStart])
                          && c2 == StripParityIfNeeded(_receiveBuffer[checkStart + 1])
                          && c3 == StripParityIfNeeded(_receiveBuffer[checkStart + 2]);
                break;
            }
            case 2:
            {
                int chk2 = KermitChecksum.ComputeType2(checksumSpan);
                byte c1 = KermitConst.ToChar((chk2 >> 6) & 0x3F);
                byte c2 = KermitConst.ToChar(chk2 & 0x3F);
                checkValid = c1 == StripParityIfNeeded(_receiveBuffer[checkStart])
                          && c2 == StripParityIfNeeded(_receiveBuffer[checkStart + 1]);
                break;
            }
            default:
            {
                int chk1 = KermitChecksum.ComputeType1(checksumSpan);
                byte expected = KermitConst.ToChar(chk1);
                byte received = StripParityIfNeeded(_receiveBuffer[checkStart]);
                checkValid = expected == received;
                break;
            }
        }

        // Consume the packet from the buffer (including EOL if present)
        int consumeLen = packetEnd;
        if (consumeLen < _receiveLength)
        {
            byte nextByte = StripParityIfNeeded(_receiveBuffer[consumeLen]);
            // Skip EOL character if present
            if (nextByte == _remoteEol || nextByte == KermitConst.DefaultEol)
                consumeLen++;
        }
        CompactReceiveBuffer(consumeLen);

        packet = new KermitPacket(seq, type, dataBytes, checkValid);
        return true;
    }

    private void CompactReceiveBuffer(int offset)
    {
        int remaining = _receiveLength - offset;
        if (remaining > 0)
        {
            Buffer.BlockCopy(_receiveBuffer, offset, _receiveBuffer, 0, remaining);
        }
        _receiveLength = remaining;
    }

    [System.Runtime.CompilerServices.MethodImpl(
        System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private byte StripParityIfNeeded(byte b)
    {
        return _options.Parity != ParityMode.None ? KermitConst.StripParity(b) : b;
    }

    // --- State machine ---

    private void ProcessPacket(in KermitPacket packet)
    {
        // Handle Error packets in any state
        if (packet.IsError)
        {
            string msg = packet.Data.Length > 0
                ? Encoding.ASCII.GetString(packet.Data.Span)
                : "Remote error";
            Fail(msg);
            return;
        }

        switch (_state)
        {
            case KermitState.SendingInit:
                HandleSendingInit(in packet);
                break;
            case KermitState.SendingFileHeader:
                HandleSendingFileHeader(in packet);
                break;
            case KermitState.SendingData:
                HandleSendingData(in packet);
                break;
            case KermitState.SendingEof:
                HandleSendingEof(in packet);
                break;
            case KermitState.SendingBreak:
                HandleSendingBreak(in packet);
                break;
            case KermitState.ReceivingInit:
                HandleReceivingInit(in packet);
                break;
            case KermitState.ReceivingFileHeader:
                HandleReceivingFileHeader(in packet);
                break;
            case KermitState.ReceivingData:
                HandleReceivingData(in packet);
                break;
            case KermitState.Serving:
                HandleServing(in packet);
                break;
        }
    }

    // --- Sender state handlers ---

    /// <summary>
    /// Sent S packet, expecting Y (ACK) with remote's parameters.
    /// On ACK: parse remote params, open file, send F packet.
    /// </summary>
    private void HandleSendingInit(in KermitPacket packet)
    {
        if (packet.IsNak || (packet.IsAck && packet.Sequence != _sequence))
        {
            RetryOrFail();
            return;
        }

        if (!packet.IsAck) return;

        // Parse remote parameters from ACK data
        ApplyRemoteParameters(packet.Data.Span);
        _checkTypeNegotiated = true; // S/Y exchange complete, use negotiated check type

        // Open the file for reading
        if (!_fileHandler.OpenFileForRead(_currentFileName!, out _))
        {
            SendError($"Cannot open file: {_currentFileName}");
            _state = KermitState.Failed;
            return;
        }

        FileStarting?.Invoke(_currentFileName!);

        // Advance sequence and send F (File Header) packet
        _sequence = NextSequence(_sequence);

        // Encode the filename (quote special characters)
        byte[] nameBytes = Encoding.ASCII.GetBytes(Path.GetFileName(_currentFileName!));
        int encodedLen = KermitEncoding.Encode(
            nameBytes, _encodeBuffer, _maxSendDataLength,
            _ctlPrefix, _ebqPrefix, _use8BitQuoting, out _);

        SendPacket(PacketType.FileHeader, _encodeBuffer.AsSpan(0, encodedLen));
        _state = KermitState.SendingFileHeader;
    }

    /// <summary>
    /// Sent F packet, expecting Y (ACK).
    /// On ACK: start sending data packets.
    /// </summary>
    private void HandleSendingFileHeader(in KermitPacket packet)
    {
        if (packet.IsNak || (packet.IsAck && packet.Sequence != _sequence))
        {
            RetryOrFail();
            return;
        }

        if (!packet.IsAck) return;

        _sequence = NextSequence(_sequence);
        SendNextDataPacket();
    }

    /// <summary>
    /// Sent D packet, expecting Y (ACK).
    /// On ACK: send next D or Z if file is done.
    /// </summary>
    private void HandleSendingData(in KermitPacket packet)
    {
        if (packet.IsNak || (packet.IsAck && packet.Sequence != _sequence))
        {
            RetryOrFail();
            return;
        }

        if (!packet.IsAck) return;

        _sequence = NextSequence(_sequence);
        SendNextDataPacket();
    }

    /// <summary>
    /// Sent Z (EOF) packet, expecting Y (ACK).
    /// On ACK: send B (Break/EOT) packet.
    /// </summary>
    private void HandleSendingEof(in KermitPacket packet)
    {
        if (packet.IsNak || (packet.IsAck && packet.Sequence != _sequence))
        {
            RetryOrFail();
            return;
        }

        if (!packet.IsAck) return;

        _fileHandler.CloseFile();
        FileCompleted?.Invoke(_currentFileName!);

        _sequence = NextSequence(_sequence);
        SendPacket(PacketType.Break, ReadOnlySpan<byte>.Empty);
        _state = KermitState.SendingBreak;
    }

    /// <summary>
    /// Sent B (Break) packet, expecting Y (ACK).
    /// On ACK: transfer complete.
    /// </summary>
    private void HandleSendingBreak(in KermitPacket packet)
    {
        if (packet.IsNak || (packet.IsAck && packet.Sequence != _sequence))
        {
            RetryOrFail();
            return;
        }

        if (!packet.IsAck) return;

        _state = KermitState.Completed;
        TransferCompleted?.Invoke();
    }

    /// <summary>
    /// Reads file data, encodes it, and sends the next D packet.
    /// If the file is exhausted, sends Z (EOF) instead.
    /// </summary>
    private void SendNextDataPacket()
    {
        // Refill file read buffer if empty
        if (_fileReadOffset >= _fileReadLength)
        {
            _fileReadLength = _fileHandler.ReadFile(_fileReadBuffer);
            _fileReadOffset = 0;
        }

        if (_fileReadLength <= 0)
        {
            // End of file: send Z packet
            SendPacket(PacketType.EndOfFile, ReadOnlySpan<byte>.Empty);
            _state = KermitState.SendingEof;
            return;
        }

        // Encode as much raw data as fits in one packet
        ReadOnlySpan<byte> rawData = _fileReadBuffer.AsSpan(_fileReadOffset, _fileReadLength - _fileReadOffset);
        int encodedLen = KermitEncoding.Encode(
            rawData, _encodeBuffer, _maxSendDataLength,
            _ctlPrefix, _ebqPrefix, _use8BitQuoting, out int consumed);

        _fileReadOffset += consumed;

        SendPacket(PacketType.Data, _encodeBuffer.AsSpan(0, encodedLen));
        _state = KermitState.SendingData;
    }

    // --- Receiver state handlers ---

    /// <summary>
    /// Waiting for S (Send-Init). Parse remote params, reply with Y + our params.
    /// </summary>
    private void HandleReceivingInit(in KermitPacket packet)
    {
        if (packet.Type != PacketType.SendInit) return;

        ApplyRemoteParameters(packet.Data.Span);

        // Reply with ACK containing our parameters
        Span<byte> paramData = stackalloc byte[16];
        int paramLen = _localParams.Encode(paramData);

        SendAck(packet.Sequence, paramData[..paramLen]);
        _checkTypeNegotiated = true; // S/Y exchange complete, use negotiated check type
        _sequence = NextSequence(packet.Sequence);
        _state = KermitState.ReceivingFileHeader;
    }

    /// <summary>
    /// Waiting for F (File Header) or B (Break/EOT).
    /// F: open file, ACK, move to ReceivingData.
    /// B: ACK, transfer complete.
    /// </summary>
    private void HandleReceivingFileHeader(in KermitPacket packet)
    {
        if (packet.Type == PacketType.Break)
        {
            SendAck(packet.Sequence, ReadOnlySpan<byte>.Empty);
            _state = KermitState.Completed;
            TransferCompleted?.Invoke();
            return;
        }

        if (packet.Type != PacketType.FileHeader) return;

        // Decode the filename from the quoted data
        int nameLen = KermitEncoding.Decode(
            packet.Data.Span, _decodeBuffer,
            _ctlPrefix, _ebqPrefix, _use8BitQuoting);

        string fileName = Encoding.ASCII.GetString(_decodeBuffer, 0, nameLen);
        _currentFileName = fileName;

        if (!_fileHandler.OpenFileForWrite(fileName))
        {
            SendError($"Cannot create file: {fileName}");
            _state = KermitState.Failed;
            return;
        }

        FileStarting?.Invoke(fileName);
        SendAck(packet.Sequence, ReadOnlySpan<byte>.Empty);
        _sequence = NextSequence(packet.Sequence);
        _state = KermitState.ReceivingData;
    }

    /// <summary>
    /// Waiting for D (Data) or Z (EOF).
    /// D: decode data, write to file, ACK.
    /// Z: close file, ACK, move to ReceivingFileHeader.
    /// </summary>
    private void HandleReceivingData(in KermitPacket packet)
    {
        if (packet.Type == PacketType.EndOfFile)
        {
            _fileHandler.CloseFile();
            FileCompleted?.Invoke(_currentFileName!);
            SendAck(packet.Sequence, ReadOnlySpan<byte>.Empty);
            _sequence = NextSequence(packet.Sequence);
            _state = KermitState.ReceivingFileHeader;
            return;
        }

        if (packet.Type != PacketType.Data) return;

        // Decode the quoted data and write to file
        int decoded = KermitEncoding.Decode(
            packet.Data.Span, _decodeBuffer,
            _ctlPrefix, _ebqPrefix, _use8BitQuoting);

        _fileHandler.WriteFile(_decodeBuffer.AsSpan(0, decoded));

        SendAck(packet.Sequence, ReadOnlySpan<byte>.Empty);
        _sequence = NextSequence(packet.Sequence);
    }

    // --- Server state handler ---

    /// <summary>
    /// Server mode: waiting for client commands.
    /// Currently supports receiving files (S packet triggers receiver flow).
    /// </summary>
    private void HandleServing(in KermitPacket packet)
    {
        if (packet.Type == PacketType.SendInit)
        {
            // Incoming file transfer: act as receiver
            HandleReceivingInit(in packet);
            return;
        }

        if (packet.Type == PacketType.Generic)
        {
            // Generic server command — check for Finish ('F') or Bye ('L')
            if (packet.Data.Length > 0)
            {
                byte cmd = packet.Data.Span[0];
                if (cmd == (byte)'F' || cmd == (byte)'L')
                {
                    SendAck(packet.Sequence, ReadOnlySpan<byte>.Empty);
                    _state = KermitState.Completed;
                    TransferCompleted?.Invoke();
                    return;
                }
            }

            SendError("Unknown server command");
            return;
        }

        // Unrecognized packet in server mode: NAK it
        SendNak(packet.Sequence);
    }

    // --- Packet building and transmission ---

    /// <summary>
    /// Builds and sends a packet, also saving it for potential retransmission.
    /// </summary>
    private void SendPacket(byte type, ReadOnlySpan<byte> data)
    {
        int len = BuildPacket(_sendBuffer, _sequence, type, data);
        SaveLastSent(_sendBuffer, len);
        EmitData(_sendBuffer, len);
    }

    /// <summary>
    /// Builds and sends an ACK (Y) packet with the given sequence number.
    /// </summary>
    private void SendAck(byte sequence, ReadOnlySpan<byte> data)
    {
        int len = BuildPacket(_sendBuffer, sequence, PacketType.Ack, data);
        SaveLastSent(_sendBuffer, len);
        EmitData(_sendBuffer, len);
    }

    /// <summary>
    /// Builds and sends a NAK (N) packet with the given sequence number.
    /// </summary>
    private void SendNak(byte sequence)
    {
        int len = BuildPacket(_sendBuffer, sequence, PacketType.Nak, ReadOnlySpan<byte>.Empty);
        EmitData(_sendBuffer, len);
    }

    /// <summary>
    /// Sends an Error (E) packet with a text message.
    /// </summary>
    private void SendError(string message)
    {
        Span<byte> msgBytes = stackalloc byte[Math.Min(message.Length, 80)];
        int msgLen = Encoding.ASCII.GetBytes(message.AsSpan(0, msgBytes.Length), msgBytes);
        int len = BuildPacket(_sendBuffer, _sequence, PacketType.Error, msgBytes[..msgLen]);
        EmitData(_sendBuffer, len);
    }

    /// <summary>
    /// Builds a complete Kermit packet in the output buffer.
    /// Format: MARK LEN SEQ TYPE DATA CHECK EOL
    /// </summary>
    /// <remarks>
    /// The S (Send-Init) and I (Init) packets and their ACKs always use type 1
    /// checksum. The negotiated check type applies to all subsequent packets.
    /// Check type affects both the LEN field (which includes check byte count)
    /// and the check bytes themselves:
    ///   Type 1: 1 byte  — 6-bit folded checksum
    ///   Type 2: 2 bytes — 12-bit checksum (high 6 + low 6)
    ///   Type 3: 3 bytes — CRC-16 (4-bit + 6-bit + 6-bit)
    /// </remarks>
    /// <returns>
    /// Total number of bytes in the packet.
    /// </returns>
    internal int BuildPacket(Span<byte> output, byte seq, byte type, ReadOnlySpan<byte> data)
    {
        int pos = 0;
        byte eol = _remoteEol;

        // S/I packets and their ACKs always use type 1 checksum
        bool isInitPacket = type == PacketType.SendInit || type == PacketType.Init;
        int checkType = (_checkTypeNegotiated && !isInitPacket) ? _negotiatedCheckType : 1;
        int checkLen = checkType == 3 ? 3 : checkType == 2 ? 2 : 1;

        // MARK
        output[pos++] = _options.Mark;

        // LEN: data length + SEQ(1) + TYPE(1) + CHECK(checkLen)
        output[pos++] = KermitConst.ToChar(data.Length + 2 + checkLen);

        // SEQ
        output[pos++] = KermitConst.ToChar(seq);

        // TYPE
        output[pos++] = type;

        // DATA
        data.CopyTo(output[pos..]);
        pos += data.Length;

        // CHECKSUM: covers LEN through last DATA byte
        int checksumStart = 1; // position of LEN
        int checksumLen = pos - checksumStart;
        ReadOnlySpan<byte> checksumSpan = output.Slice(checksumStart, checksumLen);

        switch (checkType)
        {
            case 3:
            {
                int crc = KermitChecksum.ComputeType3(checksumSpan);
                output[pos++] = KermitConst.ToChar((crc >> 12) & 0x0F);
                output[pos++] = KermitConst.ToChar((crc >> 6) & 0x3F);
                output[pos++] = KermitConst.ToChar(crc & 0x3F);
                break;
            }
            case 2:
            {
                int chk2 = KermitChecksum.ComputeType2(checksumSpan);
                output[pos++] = KermitConst.ToChar((chk2 >> 6) & 0x3F);
                output[pos++] = KermitConst.ToChar(chk2 & 0x3F);
                break;
            }
            default:
            {
                int chk1 = KermitChecksum.ComputeType1(checksumSpan);
                output[pos++] = KermitConst.ToChar(chk1);
                break;
            }
        }

        // EOL
        output[pos++] = eol;

        // Apply parity to all bytes if needed
        if (_options.Parity != ParityMode.None)
        {
            for (int i = 0; i < pos; i++)
            {
                output[i] = ApplyParity(output[i]);
            }
        }

        return pos;
    }

    private byte ApplyParity(byte b)
    {
        return _options.Parity switch
        {
            ParityMode.Even => KermitConst.AddEvenParity(b),
            ParityMode.Space => KermitConst.StripParity(b),
            ParityMode.Mark => (byte)(b | 0x80),
            ParityMode.Odd => (byte)(KermitConst.AddEvenParity(b) ^ 0x80),
            _ => b
        };
    }

    private void SaveLastSent(byte[] buffer, int length)
    {
        Buffer.BlockCopy(buffer, 0, _lastSentPacket, 0, length);
        _lastSentLength = length;
    }

    private void EmitData(byte[] buffer, int length)
    {
        DataToSend?.Invoke(new ReadOnlyMemory<byte>(buffer, 0, length));
    }

    // --- Retry / fail helpers ---

    private void RetryOrFail()
    {
        _retryCount++;
        if (_retryCount > _options.MaxRetries)
        {
            Fail("Too many retries");
            return;
        }

        // Retransmit last packet
        if (_lastSentLength > 0)
        {
            EmitData(_lastSentPacket, _lastSentLength);
        }
    }

    private void Fail(string reason)
    {
        _fileHandler.CloseFile();
        _state = KermitState.Failed;
        ErrorOccurred?.Invoke(reason);
    }

    // --- Parameter negotiation ---

    /// <summary>
    /// Parses the remote side's parameters and computes negotiated settings.
    /// The Kermit negotiation rule is: use the minimum (most conservative)
    /// value for each parameter, since both sides must be able to handle it.
    /// </summary>
    private void ApplyRemoteParameters(ReadOnlySpan<byte> data)
    {
        _remoteParams = new KermitParameters();
        _remoteParams.Decode(data);

        // Max data we can send = remote's max packet length - overhead (SEQ+TYPE+CHECK = 3)
        _maxSendDataLength = Math.Min(
            _remoteParams.MaxPacketLength - 3,
            KermitConst.MaxShortLength - 3);

        if (_maxSendDataLength < 10)
            _maxSendDataLength = 10;

        // Use remote's EOL for outbound packets
        if (_remoteParams.EndOfLine > 0 && _remoteParams.EndOfLine < 32)
            _remoteEol = _remoteParams.EndOfLine;

        // Control prefix: use remote's preference
        _ctlPrefix = _remoteParams.ControlQuote;

        // Negotiate 8th-bit quoting
        _use8BitQuoting = KermitParameters.Negotiate8BitQuoting(
            _localParams.EighthBitQuote,
            _remoteParams.EighthBitQuote,
            out _ebqPrefix);

        // Negotiate block check type: use the minimum of what both sides support.
        // Both must agree; if they disagree, fall back to type 1.
        int localCheck = _options.BlockCheckType;
        int remoteCheck = _remoteParams.CheckType - '0';
        if (remoteCheck < 1 || remoteCheck > 3) remoteCheck = 1;
        _negotiatedCheckType = Math.Min(localCheck, remoteCheck);
        if (_negotiatedCheckType < 1 || _negotiatedCheckType > 3) _negotiatedCheckType = 1;
        // NOTE: _checkTypeNegotiated is set to true AFTER the S/Y exchange completes,
        // not here. This ensures the ACK to the S packet still uses type 1.
        // It is set in HandleSendingInit (sender got ACK) and HandleReceivingInit (receiver sent ACK).

        // Recalculate max send data length with correct check size
        int checkLen = _negotiatedCheckType == 3 ? 3 : _negotiatedCheckType == 2 ? 2 : 1;
        _maxSendDataLength = Math.Min(
            _remoteParams.MaxPacketLength - 2 - checkLen,
            KermitConst.MaxShortLength - 2 - checkLen);
        if (_maxSendDataLength < 10) _maxSendDataLength = 10;
    }

    // --- Utilities ---

    private static byte NextSequence(byte seq) => (byte)((seq + 1) % KermitConst.SequenceModulo);
}
