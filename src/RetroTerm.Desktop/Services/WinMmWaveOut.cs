using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RetroTerm.Desktop.Services;

/// <summary>
/// Minimal low-latency PCM player built directly on the Windows winmm waveOut API.
///
/// WHY NOT System.Media.SoundPlayer: SoundPlayer opens and tears down the audio device
/// on every single Play call (its PlaySync also re-loads the stream first). That device
/// setup - not the tone itself - is what makes a short beep feel late and unreliable.
///
/// The fix, which is also what NAudio's WaveOutEvent does internally, is to open the
/// output device ONCE and keep it open for the lifetime of the process, then just hand
/// the driver a pre-rendered buffer per sound. First sample then comes out in a few ms.
///
/// Buffers are queued, not mixed: winmm plays successive waveOutWrite calls back to back
/// on the same device. For a terminal bell that is the behaviour we want.
///
/// Windows-only by construction; callers must check <see cref="IsSupported"/> first.
/// </summary>
internal static class WinMmWaveOut
{
    // ---- winmm constants -------------------------------------------------

    /// <summary>
    /// Let Windows pick the preferred output device (WAVE_MAPPER = (uint)-1).
    /// </summary>
    private const uint WaveMapper = 0xFFFFFFFF;

    /// <summary>
    /// No callback - we poll the header flags instead (CALLBACK_NULL).
    /// </summary>
    private const uint CallbackNull = 0x00000000;

    /// <summary>
    /// WAVE_FORMAT_PCM.
    /// </summary>
    private const ushort WaveFormatPcm = 1;

    /// <summary>
    /// WHDR_DONE - driver has finished with this buffer.
    /// </summary>
    private const uint WhdrDone = 0x00000001;

    /// <summary>
    /// WHDR_PREPARED - header has been prepared and may be written.
    /// </summary>
    private const uint WhdrPrepared = 0x00000002;

    /// <summary>
    /// WHDR_INQUEUE - buffer is queued in the driver; must not be touched.
    /// </summary>
    private const uint WhdrInQueue = 0x00000010;

    /// <summary>
    /// MMSYSERR_NOERROR.
    /// </summary>
    private const uint MmSysErrNoError = 0;

    /// <summary>
    /// Number of wave headers kept in the pool. Each one can hold a queued copy of the
    /// sound, so this is the maximum number of bells that can be outstanding at once.
    /// Eight is plenty for a terminal bell and costs nothing but a few structs.
    /// </summary>
    private const int HeaderPoolSize = 8;

    // ---- interop structures ----------------------------------------------

    /// <summary>
    /// WAVEFORMATEX - describes the PCM format handed to the driver.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormatEx
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    /// <summary>
    /// WAVEHDR - one queued buffer. The driver writes status bits back into dwFlags,
    /// which is exactly why each header lives in unmanaged memory we own: we need to
    /// read the driver's updates, not a marshalled copy of them.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    // ---- P/Invoke ---------------------------------------------------------

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutOpen(
        out IntPtr phwo, uint uDeviceID, ref WaveFormatEx pwfx,
        IntPtr dwCallback, IntPtr dwInstance, uint fdwOpen);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutPrepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutUnprepareHeader(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutWrite(IntPtr hwo, IntPtr pwh, uint cbwh);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutReset(IntPtr hwo);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutClose(IntPtr hwo);

    /// <summary>
    /// How many waveform output devices the system has. Zero on a machine with no sound
    /// hardware, which is what a hosted CI runner is.
    /// </summary>
    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint waveOutGetNumDevs();

    // ---- state ------------------------------------------------------------

    /// <summary>
    /// Open device handle, or IntPtr.Zero when not (yet) open.
    /// </summary>
    private static IntPtr _device = IntPtr.Zero;

    /// <summary>
    /// Unmanaged copy of the PCM payload, kept alive for as long as the device is.
    /// </summary>
    private static IntPtr _pcmBuffer = IntPtr.Zero;

    /// <summary>
    /// Length in bytes of <see cref="_pcmBuffer"/>.
    /// </summary>
    private static int _pcmLength;

    /// <summary>
    /// Unmanaged WAVEHDR pool, <see cref="HeaderPoolSize"/> entries.
    /// </summary>
    private static IntPtr _headers = IntPtr.Zero;

    /// <summary>
    /// Size of one WAVEHDR, cached from Marshal.
    /// </summary>
    private static int _headerSize;

    /// <summary>
    /// Set once we have tried and failed to open a device, so we stop retrying.
    /// </summary>
    private static bool _deviceUnavailable;

    /// <summary>
    /// Guards open/close/write - all of these mutate the pool.
    /// </summary>
    private static readonly object _lock = new object();

    /// <summary>
    /// True when the winmm API can be used at all (i.e. we are on Windows).
    /// </summary>
    public static bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// True when Windows reports at least one waveform output device, which is the thing
    /// <see cref="Prepare"/> needs to succeed.
    /// </summary>
    /// <remarks>
    /// <see cref="IsSupported"/> only says "this is Windows". The first GitHub build run,
    /// 28 September 2026, ran on a Windows machine with no sound hardware at all: the API was
    /// there, waveOutOpen failed, and BellService.IsAudioAvailable still said true because it
    /// was this flag under another name. The device count is what separates the two.
    /// </remarks>
    public static bool HasOutputDevice => IsSupported && waveOutGetNumDevs() > 0;

    /// <summary>
    /// Last error text from a failed open/write, for diagnostics. Null when healthy.
    /// </summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// Opens the output device (if not already open) and loads <paramref name="pcm"/> as the
    /// sound to play. Safe to call repeatedly; the device is only opened once, and the payload
    /// is only re-uploaded when it actually changed.
    /// </summary>
    /// <param name="pcm">
    /// Raw 16-bit signed mono PCM samples at <paramref name="sampleRate"/>.
    /// </param>
    /// <param name="sampleRate">
    /// Sample rate in Hz.
    /// </param>
    /// <returns>
    /// True when the device is ready to play.
    /// </returns>
    public static bool Prepare(byte[] pcm, int sampleRate)
    {
        if (!IsSupported || _deviceUnavailable)
            return false;

        lock (_lock)
        {
            // Payload changed (tone retuned) - tear the pool down and rebuild it.
            if (_device != IntPtr.Zero && _pcmLength != pcm.Length)
                CloseLocked();

            if (_device != IntPtr.Zero)
                return true;

            var format = new WaveFormatEx
            {
                wFormatTag = WaveFormatPcm,
                nChannels = 1,
                nSamplesPerSec = (uint)sampleRate,
                nBlockAlign = 2,                          // 1 channel * 16 bit / 8
                wBitsPerSample = 16,
                nAvgBytesPerSec = (uint)(sampleRate * 2),
                cbSize = 0
            };

            uint result = waveOutOpen(out IntPtr device, WaveMapper, ref format,
                IntPtr.Zero, IntPtr.Zero, CallbackNull);

            if (result != MmSysErrNoError || device == IntPtr.Zero)
            {
                // No output device, or it is in exclusive use by something else.
                _deviceUnavailable = true;
                LastError = $"waveOutOpen failed (MMSYSERR {result})";
                return false;
            }

            _device = device;

            // Copy the samples into unmanaged memory once. Every header points at this
            // same block - it is read-only as far as the driver is concerned, so sharing
            // it between queued buffers is safe and avoids per-bell allocation.
            _pcmLength = pcm.Length;
            _pcmBuffer = Marshal.AllocHGlobal(_pcmLength);
            Marshal.Copy(pcm, 0, _pcmBuffer, _pcmLength);

            _headerSize = Marshal.SizeOf<WaveHdr>();
            _headers = Marshal.AllocHGlobal(_headerSize * HeaderPoolSize);

            for (int i = 0; i < HeaderPoolSize; i++)
            {
                IntPtr headerPtr = _headers + (i * _headerSize);

                var header = new WaveHdr
                {
                    lpData = _pcmBuffer,
                    dwBufferLength = (uint)_pcmLength,
                    dwBytesRecorded = 0,
                    dwUser = IntPtr.Zero,
                    dwFlags = 0,
                    dwLoops = 0,
                    lpNext = IntPtr.Zero,
                    reserved = IntPtr.Zero
                };
                Marshal.StructureToPtr(header, headerPtr, false);

                // Prepared once, reused forever - re-preparing per bell would reintroduce
                // exactly the per-play overhead we are trying to eliminate.
                uint prep = waveOutPrepareHeader(_device, headerPtr, (uint)_headerSize);
                if (prep != MmSysErrNoError)
                {
                    LastError = $"waveOutPrepareHeader failed (MMSYSERR {prep})";
                    CloseLocked();
                    _deviceUnavailable = true;
                    return false;
                }
            }

            LastError = null;
            return true;
        }
    }

    /// <summary>
    /// Queues one playback of the prepared sound. Returns immediately - the driver plays it.
    /// </summary>
    /// <returns>
    /// True if the sound was queued. False when no device is available, or when every
    /// header in the pool is still busy (i.e. the caller is ringing faster than the
    /// hardware can play, so this one is dropped rather than unboundedly buffered).
    /// </returns>
    public static bool Play()
    {
        if (!IsSupported || _deviceUnavailable)
            return false;

        lock (_lock)
        {
            if (_device == IntPtr.Zero)
                return false;

            for (int i = 0; i < HeaderPoolSize; i++)
            {
                IntPtr headerPtr = _headers + (i * _headerSize);

                // dwFlags is at a fixed offset; read it straight back from the memory the
                // driver updates rather than marshalling the whole struct on a hot path.
                uint flags = ReadFlags(headerPtr);

                // Still queued or currently playing - skip to the next header.
                if ((flags & WhdrInQueue) != 0)
                    continue;

                // Clear WHDR_DONE from the previous playback but keep WHDR_PREPARED,
                // otherwise waveOutWrite rejects the header with WAVERR_UNPREPARED.
                WriteFlags(headerPtr, WhdrPrepared);

                uint result = waveOutWrite(_device, headerPtr, (uint)_headerSize);
                if (result != MmSysErrNoError)
                {
                    LastError = $"waveOutWrite failed (MMSYSERR {result})";
                    return false;
                }

                LastError = null;
                return true;
            }

            // Every header busy: the bell is being rung faster than it can sound.
            return false;
        }
    }

    /// <summary>
    /// Stops playback and releases the device and all unmanaged memory.
    /// </summary>
    public static void Close()
    {
        lock (_lock)
        {
            CloseLocked();
        }
    }

    /// <summary>
    /// Close implementation; caller must hold <see cref="_lock"/>.
    /// </summary>
    private static void CloseLocked()
    {
        if (_device != IntPtr.Zero)
        {
            // Reset first: unpreparing a header that is still queued fails.
            waveOutReset(_device);

            if (_headers != IntPtr.Zero)
            {
                for (int i = 0; i < HeaderPoolSize; i++)
                    waveOutUnprepareHeader(_device, _headers + (i * _headerSize), (uint)_headerSize);
            }

            waveOutClose(_device);
            _device = IntPtr.Zero;
        }

        if (_headers != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_headers);
            _headers = IntPtr.Zero;
        }

        if (_pcmBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_pcmBuffer);
            _pcmBuffer = IntPtr.Zero;
        }

        _pcmLength = 0;
    }

    /// <summary>
    /// Reads WAVEHDR.dwFlags directly out of unmanaged memory.
    /// </summary>
    private static uint ReadFlags(IntPtr headerPtr)
    {
        // Offset of dwFlags within WAVEHDR: lpData + dwBufferLength + dwBytesRecorded + dwUser.
        int offset = IntPtr.Size + 4 + 4 + IntPtr.Size;
        return unchecked((uint)Marshal.ReadInt32(headerPtr, offset));
    }

    /// <summary>
    /// Writes WAVEHDR.dwFlags directly into unmanaged memory.
    /// </summary>
    private static void WriteFlags(IntPtr headerPtr, uint flags)
    {
        int offset = IntPtr.Size + 4 + 4 + IntPtr.Size;
        Marshal.WriteInt32(headerPtr, offset, unchecked((int)flags));
    }
}
