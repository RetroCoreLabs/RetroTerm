using System;
using System.Threading;
using RetroTerm.Desktop.Services;
using Xunit;

namespace RetroTerm.Tests.Desktop;

/// <summary>
/// Manual, human-in-the-loop checks that <see cref="BellService"/> actually makes a sound.
///
/// These are NO-OPs during normal test runs - nobody wants the suite beeping at them.
/// To hear them, set RETROTERM_AUDIBLE_BELL_TEST=1 before running:
///
///   $env:RETROTERM_AUDIBLE_BELL_TEST = "1"
///   dotnet test tests\RetroTerm.Tests\RetroTerm.Tests.csproj --filter "FullyQualifiedName~BellServiceAudibleTests"
/// </summary>
public class BellServiceAudibleTests
{
    /// <summary>
    /// Environment variable that opts in to the audible (slow) run.
    /// </summary>
    private const string OptInVariable = "RETROTERM_AUDIBLE_BELL_TEST";

    /// <summary>
    /// True when the caller asked to actually hear the tests.
    /// </summary>
    private static bool OptedIn => Environment.GetEnvironmentVariable(OptInVariable) == "1";

    [Fact]
    public void Bell_PlaysAudibly_WhenOptedIn()
    {
        if (!OptedIn) return;

        BellService.Enabled = true;

        // Five clearly separated bells, so pitch and length can be judged by ear.
        for (int i = 0; i < 5; i++)
        {
            BellService.PlayNow();
            Thread.Sleep(400);
        }

        Assert.True(true); // The assertion is the listener.
    }

    /// <summary>
    /// Every BEL in a realistic burst must ring - none silently dropped. An earlier
    /// implementation coalesced anything within 150 ms, which made the bell appear to
    /// work only "randomly". You should hear FIVE distinct beeps here, not one.
    /// </summary>
    [Fact]
    public void Bell_RingsEveryBel_InRealisticBurst_WhenOptedIn()
    {
        if (!OptedIn) return;

        BellService.Enabled = true;
        BellService.Prime();
        Thread.Sleep(200); // let the device finish opening

        for (int i = 0; i < 5; i++)
        {
            BellService.Play();
            Thread.Sleep(120); // comfortably above the 45 ms runaway-loop floor
        }

        Thread.Sleep(400);
        Assert.True(true);
    }

    /// <summary>
    /// A runaway host loop must not machine-gun the speaker. 100 bells as fast as the CPU
    /// can issue them should be capped, not queued without bound.
    /// </summary>
    [Fact]
    public void Bell_CapsRunawayLoop_WhenOptedIn()
    {
        if (!OptedIn) return;

        BellService.Enabled = true;

        for (int i = 0; i < 100; i++)
            BellService.Play();

        Thread.Sleep(600);
        Assert.True(true);
    }

    /// <summary>
    /// Measures how long <see cref="BellService.Play"/> blocks the CALLER. This runs
    /// always, opted in or not: it must never block the terminal's data path, and that is
    /// a real assertion rather than something only a human can judge.
    /// </summary>
    [Fact]
    public void Play_DoesNotBlockCaller()
    {
        BellService.Enabled = true;

        // Deliberately do NOT prime: the cold path is the one that used to block.
        // waveOutOpen takes ~160 ms, and an earlier version ran it on the caller's
        // thread whenever Prime's queued work had not finished yet - which is exactly
        // what happens on a busy machine. Play must offload that instead.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        BellService.Play();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 25,
            $"Play() blocked the caller for {sw.ElapsedMilliseconds} ms (cold path)");

        // And the warm path, once the device is genuinely open, must be faster still.
        Assert.True(BellService.EnsureReady() || !BellService.IsAudioAvailable,
            "Audio device could not be prepared");

        Thread.Sleep(60); // clear the 45 ms runaway-loop floor so this is a real ring

        sw.Restart();
        BellService.Play();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 25,
            $"Play() blocked the caller for {sw.ElapsedMilliseconds} ms (warm path)");
    }
}
