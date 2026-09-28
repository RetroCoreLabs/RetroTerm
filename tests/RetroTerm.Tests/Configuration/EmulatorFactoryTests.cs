using System;
using RetroTerm.Core.Configuration;
using RetroTerm.Core.Terminal.Emulators;
using RetroTerm.Core.Terminal.Emulators.TDV;
using Xunit;

namespace RetroTerm.Tests.Configuration;

public class EmulatorFactoryTests
{
    [Fact]
    public void AvailableEmulators_ShouldContainExpectedTypes()
    {
        // Act
        var emulators = EmulatorFactory.AvailableEmulators;

        // Assert
        Assert.Contains("VT100", emulators);
        Assert.Contains("VT220", emulators);
        Assert.Contains("TDV1200", emulators);
        Assert.Contains("TDV2215", emulators);
        Assert.Contains("TDV2200", emulators);
    }

    [Fact]
    public void CreateEmulator_WithVT100_ShouldReturnVT100Emulator()
    {
        // Arrange
        var config = new HostConfiguration
        {
            EmulatorType = "VT100",
            Width = 80,
            Height = 24,
            MaxScrollback = 1000
        };

        // Act
        var emulator = EmulatorFactory.CreateEmulator(config);

        // Assert
        Assert.IsType<VT100Emulator>(emulator);
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void CreateEmulator_WithTDV1200_ShouldReturnTDV1200Emulator()
    {
        // Arrange
        var config = new HostConfiguration
        {
            EmulatorType = "TDV1200",
            Width = 80,
            Height = 24,
            MaxScrollback = 1500
        };

        // Act
        var emulator = EmulatorFactory.CreateEmulator(config);

        // Assert
        Assert.IsType<TDV1200Emulator>(emulator);
        Assert.Equal(80, emulator.Width);
        Assert.Equal(24, emulator.Height);
    }

    [Fact]
    public void CreateEmulator_WithTDV2215_ShouldReturnTDV2215Emulator()
    {
        // Arrange
        var config = new HostConfiguration
        {
            EmulatorType = "TDV2215",
            Width = 80,
            Height = 25,
            MaxScrollback = 2500
        };

        // Act
        var emulator = EmulatorFactory.CreateEmulator(config);

        // Assert
        Assert.IsType<TDV2215Emulator>(emulator);
        Assert.Equal(80, emulator.Width);
        Assert.Equal(25, emulator.Height);
    }

    [Fact]
    public void CreateEmulator_WithTDV2200_ShouldReturnTDV2200Emulator()
    {
        // Arrange
        var config = new HostConfiguration
        {
            EmulatorType = "TDV2200",
            Width = 80,
            Height = 25,
            MaxScrollback = 1800
        };

        // Act
        var emulator = EmulatorFactory.CreateEmulator(config);

        // Assert
        Assert.IsType<TDV2200Emulator>(emulator);
        Assert.Equal(80, emulator.Width);
        Assert.Equal(25, emulator.Height);
    }

    [Fact]
    public void CreateEmulator_WithUnsupportedType_ShouldThrowException()
    {
        // Arrange
        var config = new HostConfiguration
        {
            EmulatorType = "Unsupported"
        };

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => EmulatorFactory.CreateEmulator(config));
    }

    [Fact]
    public void CreateEmulator_WithNullConfiguration_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => EmulatorFactory.CreateEmulator(null!));
    }

    [Fact]
    public void CreateEmulator_WithParameters_ShouldCreateCorrectEmulator()
    {
        // Act
        var emulator = EmulatorFactory.CreateEmulator("VT100", 132, 25, 2000);

        // Assert
        Assert.IsType<VT100Emulator>(emulator);
        Assert.Equal(132, emulator.Width);
        Assert.Equal(25, emulator.Height);
    }

    [Theory]
    [InlineData("VT100", 1000)]
    [InlineData("VT220", 1000)]
    [InlineData("TDV1200", 1500)]
    [InlineData("TDV2215", 2500)]
    [InlineData("TDV2200", 1800)]
    public void GetDefaultScrollback_ShouldReturnCorrectValues(string emulatorType, int expectedScrollback)
    {
        // Act
        var scrollback = EmulatorFactory.GetDefaultScrollback(emulatorType);

        // Assert
        Assert.Equal(expectedScrollback, scrollback);
    }

    // The TDV family is 80 by 25: TDV2200/9 User's Guide "all 25 lines on the screen", TDV2215
    // functional spec "maximum of 25 lines possible on the screen", and for the TDV1200 the
    // notepad description "one screen picture in size (25 lines)". This row said 24 for the
    // TDV1200 until 27 September 2026, with nothing behind it.
    [Theory]
    [InlineData("VT100", 80, 24)]
    [InlineData("VT220", 80, 24)]
    [InlineData("TDV1200", 80, 25)]
    [InlineData("TDV2215", 80, 25)]
    [InlineData("TDV2200", 80, 25)]
    [InlineData("TEK4014", 74, 35)]
    [InlineData("XTERM", 80, 24)]
    [InlineData("no-such-terminal", 80, 24)]
    public void GetRecommendedSize_ShouldReturnCorrectSizes(string emulatorType, int expectedWidth, int expectedHeight)
    {
        // Act
        var (width, height) = EmulatorFactory.GetRecommendedSize(emulatorType);

        // Assert
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Theory]
    [InlineData("VT100", true)]
    [InlineData("vt100", true)] // Case insensitive
    [InlineData("TDV1200", true)]
    [InlineData("tdv1200", true)] // Case insensitive
    [InlineData("Unsupported", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_ShouldReturnCorrectResults(string? emulatorType, bool expected)
    {
        // Act
        var isSupported = EmulatorFactory.IsSupported(emulatorType);

        // Assert
        Assert.Equal(expected, isSupported);
    }
}
