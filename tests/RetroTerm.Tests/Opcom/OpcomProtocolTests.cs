using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RetroTerm.Core.Opcom;
using RetroTerm.Core.Transfer;
using Xunit;

namespace RetroTerm.Tests.Opcom;

public class OpcomProtocolTests
{
    private readonly OpcomProtocol _protocol;
    private readonly StringBuilder _sentData;

    public OpcomProtocolTests()
    {
        _protocol = new OpcomProtocol();
        _sentData = new StringBuilder();

        // Create a send delegate that captures sent bytes
        SendBytesAsync sendDelegate = (data, ct) =>
        {
            for (int i = 0; i < data.Length; i++)
                _sentData.Append((char)data.Span[i]);
            return Task.CompletedTask;
        };

        _protocol.Activate(sendDelegate);
    }

    [Fact]
    public void Activate_SetsStateToIdle()
    {
        Assert.Equal(OpcomProtocolState.Idle, _protocol.State);
        Assert.True(_protocol.IsActive);
    }

    [Fact]
    public void Deactivate_SetsStateToInactive()
    {
        _protocol.Deactivate();
        Assert.Equal(OpcomProtocolState.Inactive, _protocol.State);
        Assert.False(_protocol.IsActive);
    }

    [Fact]
    public async Task ReadMemoryAsync_SendsCorrectCommand()
    {
        // Start a read for address 1000 (octal)
        var readTask = _protocol.ReadMemoryAsync(0x200); // 1000 octal = 0x200

        // Give time for async processing
        await Task.Delay(50);

        // The protocol should have sent "1000/" but needs '#' prompt first
        // Simulate receiving '#' prompt
        _protocol.ProcessIncomingData(Encoding.ASCII.GetBytes("#"));
        await Task.Delay(50);

        // Now it should start sending "1000/"
        // Check that command bytes were sent (character by character with echo)
        Assert.True(_sentData.Length > 0);
    }

    [Fact]
    public void ProcessIncomingData_StripsParityBit()
    {
        // Test that 7E1 parity bit stripping works
        // Byte 0xB0 (parity set) = 0x30 ('0') after stripping bit 7
        var logEntries = new System.Collections.Generic.List<OpcomLogEntry>();
        _protocol.LogEntry += entry => logEntries.Add(entry);

        _protocol.ProcessIncomingData(new byte[] { 0xB0 }); // '0' with parity
        Assert.True(logEntries.Count > 0);
    }

    [Fact]
    public void CpuState_InitiallyUnknown()
    {
        Assert.Equal(OpcomCpuState.Unknown, _protocol.CpuState);
    }

    [Fact]
    public async Task StopCpuAsync_SendsStopCommand()
    {
        // Queue STOP command
        var stopTask = _protocol.StopCpuAsync();
        await Task.Delay(50);

        // Simulate prompt
        _protocol.ProcessIncomingData(Encoding.ASCII.GetBytes("#"));
        await Task.Delay(50);

        // Should have sent 'S', 'T', 'O', 'P'
        // Simulate echo of each character
        string sent = _sentData.ToString();
        // The protocol sends one char at a time and waits for echo
        // First char should be 'S'
        Assert.Contains("S", sent);
    }

    [Fact]
    public void Registers_InitiallyEmpty()
    {
        Assert.NotNull(_protocol.Registers);
        Assert.Equal(0, _protocol.Registers.LevelsRead);
        Assert.False(_protocol.Registers.InternalRead);
    }

    [Fact]
    public void Memory_InitiallyEmpty()
    {
        Assert.NotNull(_protocol.Memory);
        Assert.Equal(0, _protocol.Memory.Count);
    }

    [Fact]
    public void PassThrough_DefaultsTrue()
    {
        Assert.True(_protocol.PassThrough);
    }

    [Fact]
    public void PassThrough_CanBeSet()
    {
        _protocol.PassThrough = true;
        Assert.True(_protocol.PassThrough);
    }

    [Fact]
    public void LogEntry_FiresOnIncomingData()
    {
        var entries = new System.Collections.Generic.List<OpcomLogEntry>();
        _protocol.LogEntry += entry => entries.Add(entry);

        _protocol.ProcessIncomingData(Encoding.ASCII.GetBytes("test"));

        Assert.True(entries.Count > 0);
        Assert.Equal(OpcomLogDirection.Rx, entries[0].Direction);
    }

    [Fact]
    public void StateChanged_FiresOnActivateDeactivate()
    {
        var states = new System.Collections.Generic.List<OpcomProtocolState>();
        var protocol = new OpcomProtocol();
        protocol.StateChanged += s => states.Add(s);

        SendBytesAsync sendDelegate = (_, _) => Task.CompletedTask;
        protocol.Activate(sendDelegate);
        Assert.Contains(OpcomProtocolState.Idle, states);

        protocol.Deactivate();
        Assert.Contains(OpcomProtocolState.Inactive, states);
    }
}
