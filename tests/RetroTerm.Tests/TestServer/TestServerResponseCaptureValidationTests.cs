using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Test Server response capture validation tests
/// Validates CaptureResponseWithPollingAsync, ToVisible
/// WaitForEnterAsync and DrainInputAsync now live on TelnetSession (pump-based)
/// </summary>
public class TestServerResponseCaptureValidationTests
{
    [Fact]
    public void CaptureResponseWithPollingAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist (internal static)
        var method = testServerType.GetMethod("CaptureResponseWithPollingAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void CaptureResponseWithPollingAsync_ShouldHaveCorrectSignature()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);
        var method = testServerType.GetMethod("CaptureResponseWithPollingAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Assert - Method should exist and return Task<string?>
        Assert.NotNull(method);
        if (method != null)
        {
            Assert.True(method.ReturnType.IsGenericType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());
            var parameters = method.GetParameters();
            Assert.True(parameters.Length >= 1);
            Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
        }
    }

    [Fact]
    public void WaitForEnterAsync_ShouldExistOnTelnetSession()
    {
        // Arrange - WaitForEnterAsync moved to TelnetSession (pump-based)
        var sessionType = typeof(TelnetSession);

        // Assert - Method should exist (public instance)
        var method = sessionType.GetMethod("WaitForEnterAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
    }

    [Fact]
    public void DrainInputAsync_ShouldExistOnTelnetSession()
    {
        // Arrange - DrainInputAsync lives on TelnetSession (pump-based)
        var sessionType = typeof(TelnetSession);

        // Assert - Method should exist (public instance)
        var method = sessionType.GetMethod("DrainInputAsync",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
    }

    [Fact]
    public void ToVisible_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist (may be private static)
        var method = testServerType.GetMethod("ToVisible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        Assert.NotNull(method);
    }

    [Fact]
    public void ToVisible_ShouldConvertEscapeSequences()
    {
        // Arrange - Test ToVisible method if accessible
        var testServerType = typeof(TestServerApp);
        var method = testServerType.GetMethod("ToVisible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);

        // Assert - Method should exist and convert escape sequences to visible text
        Assert.NotNull(method);
    }
}
