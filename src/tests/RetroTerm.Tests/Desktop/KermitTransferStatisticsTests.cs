using System;
using System.Threading;
using RetroTerm.Core.Protocols.Kermit;
using Xunit;

namespace RetroTerm.Tests.Desktop;

public class KermitTransferStatisticsTests
{
    [Fact]
    public void NewStatistics_AllCountersAreZero()
    {
        var stats = new KermitTransferStatistics();

        Assert.Equal(0, stats.CharsSent);
        Assert.Equal(0, stats.CharsReceived);
        Assert.Equal(0, stats.FileBytes);
        Assert.Equal(0, stats.PacketsSent);
        Assert.Equal(0, stats.PacketsReceived);
        Assert.Equal(0, stats.Retries);
        Assert.Equal(0, stats.Timeouts);
        Assert.Equal(0, stats.EffectiveBaud);
        Assert.Equal("Idle", stats.StateName);
        Assert.Equal("", stats.CurrentFile);
        Assert.Null(stats.LastError);
    }

    [Fact]
    public void AddCharsSent_IncrementsCounterAndPackets()
    {
        var stats = new KermitTransferStatistics();

        stats.AddCharsSent(100);
        Assert.Equal(100, stats.CharsSent);
        Assert.Equal(1, stats.PacketsSent);

        stats.AddCharsSent(50);
        Assert.Equal(150, stats.CharsSent);
        Assert.Equal(2, stats.PacketsSent);
    }

    [Fact]
    public void AddCharsReceived_IncrementsCounterAndPackets()
    {
        var stats = new KermitTransferStatistics();

        stats.AddCharsReceived(200);
        Assert.Equal(200, stats.CharsReceived);
        Assert.Equal(1, stats.PacketsReceived);

        stats.AddCharsReceived(75);
        Assert.Equal(275, stats.CharsReceived);
        Assert.Equal(2, stats.PacketsReceived);
    }

    [Fact]
    public void AddFileBytes_IncrementsCounter()
    {
        var stats = new KermitTransferStatistics();

        stats.AddFileBytes(1024);
        Assert.Equal(1024, stats.FileBytes);

        stats.AddFileBytes(2048);
        Assert.Equal(3072, stats.FileBytes);
    }

    [Fact]
    public void AddRetry_IncrementsCounter()
    {
        var stats = new KermitTransferStatistics();

        stats.AddRetry();
        stats.AddRetry();
        stats.AddRetry();
        Assert.Equal(3, stats.Retries);
    }

    [Fact]
    public void AddTimeout_IncrementsCounter()
    {
        var stats = new KermitTransferStatistics();

        stats.AddTimeout();
        stats.AddTimeout();
        Assert.Equal(2, stats.Timeouts);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        var stats = new KermitTransferStatistics();

        stats.AddCharsSent(100);
        stats.AddCharsReceived(200);
        stats.AddFileBytes(300);
        stats.AddRetry();
        stats.AddTimeout();
        stats.StateName = "Transferring";
        stats.CurrentFile = "test.dat";
        stats.LastError = "some error";
        stats.Start();

        stats.Reset();

        Assert.Equal(0, stats.CharsSent);
        Assert.Equal(0, stats.CharsReceived);
        Assert.Equal(0, stats.FileBytes);
        Assert.Equal(0, stats.PacketsSent);
        Assert.Equal(0, stats.PacketsReceived);
        Assert.Equal(0, stats.Retries);
        Assert.Equal(0, stats.Timeouts);
        Assert.Equal("Idle", stats.StateName);
        Assert.Equal("", stats.CurrentFile);
        Assert.Null(stats.LastError);
    }

    [Fact]
    public void Elapsed_ReturnsZeroBeforeStart()
    {
        var stats = new KermitTransferStatistics();
        Assert.Equal(TimeSpan.Zero, stats.Elapsed);
    }

    [Fact]
    public void Elapsed_IncrementsAfterStart()
    {
        var stats = new KermitTransferStatistics();
        stats.Start();
        Thread.Sleep(50);
        Assert.True(stats.Elapsed.TotalMilliseconds > 0);
        stats.Stop();
    }

    [Fact]
    public void Elapsed_StopsAfterStop()
    {
        var stats = new KermitTransferStatistics();
        stats.Start();
        Thread.Sleep(50);
        stats.Stop();
        var elapsed1 = stats.Elapsed;
        Thread.Sleep(50);
        var elapsed2 = stats.Elapsed;
        Assert.Equal(elapsed1, elapsed2);
    }

    [Fact]
    public void EffectiveBaud_ZeroWhenNoTimeElapsed()
    {
        var stats = new KermitTransferStatistics();
        stats.AddFileBytes(1000);
        Assert.Equal(0, stats.EffectiveBaud);
    }

    [Fact]
    public void EffectiveBaud_CalculatesCorrectly()
    {
        var stats = new KermitTransferStatistics();
        stats.Start();
        Thread.Sleep(100);
        stats.AddFileBytes(1000);
        stats.Stop();

        // Baud = fileBytes * 10 / seconds
        // With ~0.1 seconds and 1000 bytes: ~100000 baud
        int baud = stats.EffectiveBaud;
        Assert.True(baud > 50000, $"Expected baud > 50000, got {baud}");
        Assert.True(baud < 200000, $"Expected baud < 200000, got {baud}");
    }

    [Fact]
    public void Updated_EventFires_OnAddCharsSent()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.AddCharsSent(10);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Updated_EventFires_OnAddCharsReceived()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.AddCharsReceived(10);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Updated_EventFires_OnAddFileBytes()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.AddFileBytes(10);
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Updated_EventFires_OnAddRetry()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.AddRetry();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Updated_EventFires_OnAddTimeout()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.AddTimeout();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Updated_EventFires_OnReset()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.Reset();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void NotifyUpdated_FiresEvent()
    {
        var stats = new KermitTransferStatistics();
        int fireCount = 0;
        stats.Updated += () => fireCount++;

        stats.NotifyUpdated();
        Assert.Equal(1, fireCount);
    }

    [Fact]
    public void Properties_AreSettable()
    {
        var stats = new KermitTransferStatistics();

        stats.StateName = "SendingData";
        Assert.Equal("SendingData", stats.StateName);

        stats.CurrentFile = "myfile.txt";
        Assert.Equal("myfile.txt", stats.CurrentFile);

        stats.LastError = "timeout";
        Assert.Equal("timeout", stats.LastError);

        stats.Use8BitQuoting = true;
        Assert.True(stats.Use8BitQuoting);

        stats.BlockCheckType = 3;
        Assert.Equal(3, stats.BlockCheckType);

        stats.MaxSendDataLength = 77;
        Assert.Equal(77, stats.MaxSendDataLength);
    }
}
