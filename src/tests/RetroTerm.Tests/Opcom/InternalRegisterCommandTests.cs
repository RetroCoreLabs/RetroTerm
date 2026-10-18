using System;
using RetroTerm.Core.Opcom;
using Xunit;

namespace RetroTerm.Tests.Opcom;

/// <summary>
/// Tests that internal register OPCOM commands use correct Ixx notation.
/// OPCOM spec: Internal registers are addressed as Iy where y = 0-15 (octal).
/// Names like PANS, STS, OPR are NOT valid OPCOM commands (only I0, I1, I2, etc.)
/// </summary>
public class InternalRegisterCommandTests
{
    [Theory]
    [InlineData(0, "I0")]     // PANS
    [InlineData(1, "I1")]     // STS
    [InlineData(2, "I2")]     // OPR
    [InlineData(3, "I3")]     // PSR
    [InlineData(4, "I4")]     // PVL
    [InlineData(5, "I5")]     // IIC
    [InlineData(6, "I6")]     // PID
    [InlineData(7, "I7")]     // PIE
    [InlineData(8, "I10")]    // CSR (octal 10 = decimal 8)
    [InlineData(9, "I11")]    // ACTL
    [InlineData(10, "I12")]   // ALD
    [InlineData(11, "I13")]   // PES
    [InlineData(12, "I14")]   // PCR
    [InlineData(13, "I15")]   // PEA
    public void GetReadCommand_ReturnsCorrectIxxNotation(int index, string expected)
    {
        string actual = InternalRegisterDefs.GetReadCommand(index);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetReadCommand_NeverReturnsName()
    {
        // Verify no command returns a name like PANS, STS, etc.
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            string cmd = InternalRegisterDefs.GetReadCommand(i);
            Assert.StartsWith("I", cmd);
            // The rest should be octal digits
            for (int j = 1; j < cmd.Length; j++)
            {
                Assert.True(cmd[j] >= '0' && cmd[j] <= '7',
                    $"Register {i}: command '{cmd}' has non-octal char '{cmd[j]}' at position {j}");
            }
        }
    }

    [Fact]
    public void RegisterDefinitions_Have14Entries()
    {
        Assert.Equal(14, InternalRegisterDefs.Count);
        Assert.Equal(14, InternalRegisterDefs.Registers.Length);
    }

    [Fact]
    public void RegisterDefinitions_OprIsWritable()
    {
        // OPR (index 2) should be writable
        Assert.True(InternalRegisterDefs.Registers[2].Writable);
        Assert.Equal("OPR", InternalRegisterDefs.Registers[2].OpcomName);
    }

    [Fact]
    public void RegisterDefinitions_AldIsWritable()
    {
        // ALD (index 10) should be writable
        Assert.True(InternalRegisterDefs.Registers[10].Writable);
        Assert.Equal("ALD", InternalRegisterDefs.Registers[10].OpcomName);
    }

    [Fact]
    public void RegisterDefinitions_MostAreReadOnly()
    {
        int writableCount = 0;
        for (int i = 0; i < InternalRegisterDefs.Count; i++)
        {
            if (InternalRegisterDefs.Registers[i].Writable)
                writableCount++;
        }
        Assert.Equal(2, writableCount); // Only OPR and ALD
    }

    [Fact]
    public void RegisterNumbers_MatchOctalConvention()
    {
        // Registers 0-7 use decimal numbers matching octal
        // Registers 8-13 use decimal numbers 8-13, but OPCOM uses octal 10-15
        Assert.Equal(0, InternalRegisterDefs.Registers[0].Number);   // I0
        Assert.Equal(7, InternalRegisterDefs.Registers[7].Number);   // I7
        Assert.Equal(8, InternalRegisterDefs.Registers[8].Number);   // I10 (octal)
        Assert.Equal(13, InternalRegisterDefs.Registers[13].Number); // I15 (octal)
    }
}
