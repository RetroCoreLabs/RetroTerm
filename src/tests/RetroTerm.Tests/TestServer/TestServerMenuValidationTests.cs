using System;
using System.Text;
using System.Threading.Tasks;
using RetroTerm.Core.Protocols.TelnetServer.Parsing;
using RetroTerm.Core.Protocols.TelnetServer.Telnet;
using RetroTerm.TestServer.App;
using Xunit;

namespace RetroTerm.Tests.TestServer;

/// <summary>
/// Test Server validation tests
/// Validates ALL menu handlers, test methods, and response capture logic
/// </summary>
public class TestServerMenuValidationTests
{
    // Note: These tests validate the test server code structure and logic
    // Actual execution tests would require a running test server instance

    /// <summary>
    /// The menu handlers that route a typed character still exist.
    /// </summary>
    /// <remarks>
    /// Six more used to be listed here - the key-detection submenus. They were deleted on
    /// 11 September 2026 because a menu handler can only ever be reached by a plain character,
    /// and what those six branches test is escape sequences. See
    /// <c>TheKeyDetectionSubmenusHaveNoCharacterHandler</c> below, which is the same check turned
    /// into a guard.
    /// </remarks>
    [Fact]
    public void MenuHandlers_ShouldExist()
    {
        var testServerType = typeof(TestServerApp);

        Assert.NotNull(testServerType.GetMethod("HandleTDVTestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(testServerType.GetMethod("HandleTDV1200TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(testServerType.GetMethod("HandleTDV2215TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(testServerType.GetMethod("HandleTDV2200TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        Assert.NotNull(testServerType.GetMethod("HandleTDVKeyDetectionMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
    }

    /// <summary>
    /// No key-detection submenu may have a character handler again.
    /// </summary>
    /// <remarks>
    /// <para><b>What this guards, measured over a socket on 11 September 2026</b></para>
    /// Six of the ten key-detection branches - function keys, PUSH keys, soft keys, control keys,
    /// extended control, numpad function - set a menu LEVEL and returned to the main loop. That
    /// loop routes only <c>InputType.Character</c> to a handler and drops
    /// <c>InputType.EscapeSequence</c>. So the screen said "press any TDV function key" and the one
    /// thing that could not get through was a TDV function key: four valid sequences -
    /// CSI 28 _, CSI 32 _, CSI 50 _, CSI 16 _ - produced no output at all.
    ///
    /// They run their interactive test directly now, through
    /// <c>RunKeyDetectionBranchAsync</c>, and each test owns its own read loop. Bringing any of
    /// these handlers back would route those levels through the main loop again and make the
    /// branches deaf a second time.
    /// </remarks>
    [Theory]
    [InlineData("HandleTDVFunctionKeysMenuAsync")]
    [InlineData("HandleTDVPushKeysMenuAsync")]
    [InlineData("HandleTDVSoftKeysMenuAsync")]
    [InlineData("HandleTDVControlKeysMenuAsync")]
    [InlineData("HandleTDVExtendedControlKeysMenuAsync")]
    [InlineData("HandleTDVNumpadFunctionKeysMenuAsync")]
    public void TheKeyDetectionSubmenusHaveNoCharacterHandler(string methodName)
    {
        var method = typeof(TestServerApp).GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.True(method == null,
            methodName + " is back. A menu handler is only reachable by a typed character, and "
            + "that branch tests escape sequences - it would go deaf again.");
    }

    /// <summary>
    /// The branch runner that replaced them is there, and takes the test to run.
    /// </summary>
    [Fact]
    public void RunKeyDetectionBranchAsync_ExistsAndTakesTheTestToRun()
    {
        var method = typeof(TestServerApp).GetMethod("RunKeyDetectionBranchAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    /// <summary>
    /// Every remaining menu handler takes a session and a character and returns a Task.
    /// </summary>
    [Theory]
    [InlineData("HandleTDVTestsMenuAsync")]
    [InlineData("HandleTDV1200TestsMenuAsync")]
    [InlineData("HandleTDV2215TestsMenuAsync")]
    [InlineData("HandleTDV2200TestsMenuAsync")]
    [InlineData("HandleTDVKeyDetectionMenuAsync")]
    public void MenuHandlers_ShouldHaveCorrectSignatures(string methodName)
    {
        var method = typeof(TestServerApp).GetMethod(methodName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
        Assert.Equal(typeof(char), parameters[1].ParameterType);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void NavigateBackAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("NavigateBackAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
    }

    [Fact]
    public void WriteMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("WriteMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
    }

    [Fact]
    public void WriteTDVTestsMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("WriteTDVTestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void WriteTDV1200TestsMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("WriteTDV1200TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void WriteTDV2215TestsMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("WriteTDV2215TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void WriteTDV2200TestsMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("WriteTDV2200TestsMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
    }

    [Fact]
    public void HandleMainMenuAsync_ShouldExist()
    {
        // Arrange
        var testServerType = typeof(TestServerApp);

        // Assert - Method should exist
        var method = testServerType.GetMethod("HandleMainMenuAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method!.ReturnType);
        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(TelnetSession), parameters[0].ParameterType);
        Assert.Equal(typeof(char), parameters[1].ParameterType);
    }
}

