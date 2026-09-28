using System;
using System.Diagnostics;
using System.Threading;
using RetroTerm.Core.Logging;

namespace RetroTerm.Desktop.Services;

/// <summary>
/// Plays the terminal bell (BEL, 0x07 - Ctrl-G).
///
/// UI-layer concern only: the emulator raises <c>TerminalEmulatorBase.Bell</c>, and this
/// class turns that into an audible beep.
///
/// DESIGN NOTES (learned the hard way):
///
/// 1. The tone is synthesized ONCE into a raw PCM buffer and uploaded to the audio device
///    up front. Nothing is generated, encoded or loaded at BEL time.
///
/// 2. Playback goes through <see cref="WinMmWaveOut"/>, which keeps the waveOut device
///    OPEN for the process lifetime. System.Media.SoundPlayer was used first and was
///    audibly late: it opens and closes the device on every call. Device setup, not the
///    tone, was the delay.
///
/// 3. BELs are QUEUED, not coalesced. An earlier version dropped any bell arriving within
///    150 ms of another, which made the bell look like it "randomly" worked. Now every BEL
///    rings; only a runaway host loop hits the <see cref="MinIntervalMs"/> cap.
/// </summary>
public static class BellService
{
    /// <summary>
    /// Default bell pitch in Hz. 1000 Hz reads as a crisp "beep"; the X11/xterm default of
    /// 400 Hz is authentic but, at any audible length, sounds like a dull "bup".
    /// </summary>
    public const double DefaultFrequencyHz = 1000.0;

    /// <summary>
    /// Default tone length in milliseconds. Short is the point: 60 ms is long enough to have
    /// a definite pitch and short enough to feel instantaneous rather than droning.
    /// </summary>
    public const int DefaultDurationMs = 60;

    /// <summary>
    /// Default peak amplitude as a fraction of full scale.
    /// </summary>
    public const double DefaultAmplitude = 0.7;

    /// <summary>
    /// Sample rate of the synthesized tone. 44.1 kHz is universally supported.
    /// </summary>
    private const int SampleRate = 44100;

    /// <summary>
    /// Attack ramp in milliseconds. Without it the waveform starts on a step discontinuity,
    /// which is heard as a click in front of the tone. Deliberately tiny - a long attack is
    /// what makes a beep feel soft and late.
    /// </summary>
    private const double AttackMs = 1.0;

    /// <summary>
    /// Release ramp in milliseconds, so the tail does not click either.
    /// </summary>
    private const double ReleaseMs = 5.0;

    /// <summary>
    /// Hard floor between two bells (~20/second). This is ONLY a runaway-loop guard so a
    /// host spewing BELs cannot machine-gun the speaker; normal bells are never affected.
    /// </summary>
    private const long MinIntervalMs = 45;

    /// <summary>
    /// Timestamp (Stopwatch ticks) of the last bell that was actually rung.
    /// </summary>
    private static long _lastBellTicks = long.MinValue;

    /// <summary>
    /// Backing fields for the tunable tone parameters.
    /// </summary>
    private static double _frequencyHz = DefaultFrequencyHz;
    private static int _durationMs = DefaultDurationMs;
    private static double _amplitude = DefaultAmplitude;

    /// <summary>
    /// Cached raw PCM payload (16-bit signed mono), built on demand.
    /// </summary>
    private static byte[]? _pcm;

    /// <summary>
    /// Guards <see cref="_pcm"/> construction and device (re)preparation.
    /// </summary>
    private static readonly object _toneLock = new object();

    /// <summary>
    /// True once the current <see cref="_pcm"/> has been handed to the device.
    /// </summary>
    private static bool _devicePrepared;

    /// <summary>
    /// Gets or sets whether the audible bell is enabled. When false, BEL is silently ignored.
    /// </summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Bell pitch in Hz, clamped to an audible range. Setting it re-synthesizes the tone.
    /// </summary>
    public static double FrequencyHz
    {
        get => _frequencyHz;
        set
        {
            double clamped = value < 50.0 ? 50.0 : (value > 12000.0 ? 12000.0 : value);
            if (clamped == _frequencyHz) return;
            _frequencyHz = clamped;
            InvalidateTone();
        }
    }

    /// <summary>
    /// Bell length in milliseconds, clamped. Setting it re-synthesizes the tone.
    /// </summary>
    public static int DurationMs
    {
        get => _durationMs;
        set
        {
            int clamped = value < 10 ? 10 : (value > 2000 ? 2000 : value);
            if (clamped == _durationMs) return;
            _durationMs = clamped;
            InvalidateTone();
        }
    }

    /// <summary>
    /// Peak amplitude, 0.0 - 1.0. Setting it re-synthesizes the tone.
    /// </summary>
    public static double Amplitude
    {
        get => _amplitude;
        set
        {
            double clamped = value < 0.0 ? 0.0 : (value > 1.0 ? 1.0 : value);
            if (clamped == _amplitude) return;
            _amplitude = clamped;
            InvalidateTone();
        }
    }

    /// <summary>
    /// Synthesizes the tone and opens the audio device ahead of time, so the FIRST bell is
    /// as fast as every later one. Call once at startup; safe to call repeatedly.
    /// Returns without blocking the caller.
    /// </summary>
    public static void Prime()
    {
        ThreadPool.UnsafeQueueUserWorkItem(static _ =>
        {
            try
            {
                EnsureReady();
            }
            catch (Exception ex)
            {
                ApplicationLogger.Log(LogCategory.UI, LogLevel.Warn, "BellService",
                    $"Bell priming failed: {ex.GetType().Name}: {ex.Message}");
            }
        }, null);
    }

    /// <summary>
    /// True when this machine can actually play the bell: Windows, with at least one waveform
    /// output device. False on non-Windows and on a Windows machine with no sound hardware,
    /// where the bell degrades to the console BEL character.
    /// </summary>
    /// <remarks>
    /// Until 28 September 2026 this only asked whether the platform was Windows, so on a hosted
    /// CI runner it said true while the device could not be opened, and the test that holds
    /// "either the device is ready or audio is unavailable" failed with "Audio device could not
    /// be prepared". The answer now comes from the device count, which is the fact the test
    /// and the fallback both need.
    /// </remarks>
    public static bool IsAudioAvailable => WinMmWaveOut.HasOutputDevice;

    /// <summary>
    /// Synthesizes the tone and opens the audio device SYNCHRONOUSLY, without making a
    /// sound. Blocks for as long as opening the device takes (~160 ms on a cold, busy
    /// machine), so do not call it from a latency-sensitive thread - use
    /// <see cref="Prime"/> for that.
    /// </summary>
    /// <returns>
    /// True when the device is ready to play.
    /// </returns>
    public static bool EnsureReady()
    {
        return EnsureDeviceReady();
    }

    /// <summary>
    /// Rings the bell. Safe to call from any thread and effectively instant: the sound is
    /// already synthesized and the device is already open, so this just hands the driver a
    /// pointer. Never blocks the caller.
    /// </summary>
    public static void Play()
    {
        if (!Enabled)
            return;

        // Runaway-loop guard only. Stopwatch is monotonic, unlike DateTime.
        long now = Stopwatch.GetTimestamp();
        long last = Interlocked.Read(ref _lastBellTicks);
        if (last != long.MinValue)
        {
            long elapsedMs = (now - last) * 1000L / Stopwatch.Frequency;
            if (elapsedMs < MinIntervalMs)
                return;
        }
        Interlocked.Exchange(ref _lastBellTicks, now);

        // Fast path: the device is already open, so ringing is just handing the driver
        // a pointer to an already-prepared buffer. Microseconds, safe on any thread.
        if (_devicePrepared)
        {
            RingCore();
            return;
        }

        // Slow path: the device is not open yet (Prime has not finished, or this is the
        // very first bell). waveOutOpen has been measured at ~160 ms, which must NEVER
        // happen on the caller's thread - that is the terminal's data path. Hand the
        // whole thing to the thread pool; the bell arrives a fraction of a second late
        // exactly once, instead of stalling the parser.
        ThreadPool.UnsafeQueueUserWorkItem(static _ => RingCore(), null);
    }

    /// <summary>
    /// Opens the device if needed and queues one playback, logging rather than throwing
    /// on failure. May block if the device still has to be opened, so callers on a
    /// latency-sensitive thread must reach this via the thread pool.
    /// </summary>
    private static void RingCore()
    {
        try
        {
            if (EnsureDeviceReady() && WinMmWaveOut.Play())
                return;

            FallbackBeep();
        }
        catch (Exception ex)
        {
            // A missing beep must never take down the terminal - but never swallow it
            // silently either, or "no sound" is indistinguishable from "never wired up".
            ApplicationLogger.Log(LogCategory.UI, LogLevel.Warn, "BellService",
                $"Bell playback failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Rings the bell synchronously, bypassing both the rate guard and <see cref="Enabled"/>,
    /// and waits roughly <see cref="DurationMs"/> for it to finish. For previewing/tuning
    /// only (settings dialog, audible tests) - the terminal data path uses <see cref="Play"/>.
    /// </summary>
    public static void PlayNow()
    {
        if (EnsureDeviceReady() && WinMmWaveOut.Play())
        {
            // waveOutWrite is asynchronous; hold the caller so a preview loop does not
            // fire every tone into the queue at once.
            Thread.Sleep(_durationMs + 10);
            return;
        }

        FallbackBeep();
    }

    /// <summary>
    /// Releases the audio device. Call on application shutdown.
    /// </summary>
    public static void Shutdown()
    {
        WinMmWaveOut.Close();
        lock (_toneLock)
        {
            _devicePrepared = false;
        }
    }

    /// <summary>
    /// Returns the current tone as a complete RIFF/WAVE file. Diagnostics only - lets a
    /// silent bell be dumped to disk and played with an external tool to prove whether the
    /// problem is the waveform or the playback.
    /// </summary>
    public static byte[] GetWaveFile()
    {
        byte[] pcm = GetPcm();
        var wav = new byte[44 + pcm.Length];

        WriteAscii(wav, 0, "RIFF");
        WriteInt32(wav, 4, 36 + pcm.Length);
        WriteAscii(wav, 8, "WAVE");
        WriteAscii(wav, 12, "fmt ");
        WriteInt32(wav, 16, 16);                  // PCM fmt chunk size
        WriteInt16(wav, 20, 1);                   // format = PCM
        WriteInt16(wav, 22, 1);                   // mono
        WriteInt32(wav, 24, SampleRate);
        WriteInt32(wav, 28, SampleRate * 2);      // byte rate
        WriteInt16(wav, 32, 2);                   // block align
        WriteInt16(wav, 34, 16);                  // bits per sample
        WriteAscii(wav, 36, "data");
        WriteInt32(wav, 40, pcm.Length);

        Buffer.BlockCopy(pcm, 0, wav, 44, pcm.Length);
        return wav;
    }

    /// <summary>
    /// Ensures the tone exists and the device holds it. Cheap after the first call.
    /// </summary>
    private static bool EnsureDeviceReady()
    {
        if (_devicePrepared)
            return true;

        lock (_toneLock)
        {
            if (_devicePrepared)
                return true;

            if (!WinMmWaveOut.IsSupported)
                return false;

            _devicePrepared = WinMmWaveOut.Prepare(GetPcm(), SampleRate);

            if (!_devicePrepared && WinMmWaveOut.LastError != null)
            {
                ApplicationLogger.Log(LogCategory.UI, LogLevel.Warn, "BellService",
                    $"Audio device unavailable: {WinMmWaveOut.LastError}");
            }

            return _devicePrepared;
        }
    }

    /// <summary>
    /// Last resort when there is no usable audio device (non-Windows, or no output device):
    /// emit the host terminal's own BEL character.
    /// </summary>
    private static void FallbackBeep()
    {
        Console.Out.Write('\a');
        Console.Out.Flush();
    }

    /// <summary>
    /// Discards the synthesized tone so the next bell is rebuilt from the current settings,
    /// and forces the device to be re-loaded with it.
    /// </summary>
    private static void InvalidateTone()
    {
        lock (_toneLock)
        {
            _pcm = null;
            _devicePrepared = false;
        }
    }

    /// <summary>
    /// Returns (building on first use) the raw 16-bit mono PCM for the bell.
    /// </summary>
    private static byte[] GetPcm()
    {
        var cached = Volatile.Read(ref _pcm);
        if (cached != null)
            return cached;

        lock (_toneLock)
        {
            _pcm ??= BuildPcm();
            return _pcm;
        }
    }

    /// <summary>
    /// Synthesizes the bell as raw 16-bit signed mono PCM - no container, because winmm
    /// takes bare samples and the format is described separately by WAVEFORMATEX.
    /// </summary>
    private static byte[] BuildPcm()
    {
        // Snapshot the tunables once: a concurrent setter must not change the waveform
        // halfway through synthesis.
        double frequencyHz = _frequencyHz;
        int durationMs = _durationMs;
        double amplitude = _amplitude;

        int sampleCount = (int)((long)SampleRate * durationMs / 1000);
        var pcm = new byte[sampleCount * 2];

        double omega = 2.0 * Math.PI * frequencyHz / SampleRate;

        int attackSamples = (int)(AttackMs * SampleRate / 1000.0);
        if (attackSamples < 1) attackSamples = 1;

        int releaseSamples = (int)(ReleaseMs * SampleRate / 1000.0);
        if (releaseSamples < 1) releaseSamples = 1;
        int releaseStart = sampleCount - releaseSamples;

        for (int i = 0; i < sampleCount; i++)
        {
            // Plain sine: a terminal beep is a tone, not a struck bell. Harmonics and a
            // decay envelope were tried and just made it sound soft and indistinct.
            double sample = Math.Sin(omega * i);

            // Very short linear attack/release purely to kill the edge clicks.
            if (i < attackSamples)
                sample *= (double)i / attackSamples;
            if (i >= releaseStart)
                sample *= (double)(sampleCount - i) / releaseSamples;

            short value = (short)(sample * amplitude * short.MaxValue);
            int offset = i * 2;
            pcm[offset] = (byte)(value & 0xFF);
            pcm[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    /// <summary>
    /// Writes a 4-character ASCII tag at the given offset.
    /// </summary>
    private static void WriteAscii(byte[] buffer, int offset, string tag)
    {
        for (int i = 0; i < tag.Length; i++)
            buffer[offset + i] = (byte)tag[i];
    }

    /// <summary>
    /// Writes a little-endian 32-bit integer.
    /// </summary>
    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>
    /// Writes a little-endian 16-bit integer.
    /// </summary>
    private static void WriteInt16(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
