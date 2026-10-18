using System;
using RetroTerm.Desktop.Diagnostics;
using Xunit;

namespace RetroTerm.Tests.Avalonia;

/// <summary>
/// What gets written down when the program falls over.
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b></para>
/// On 25 August 2026 RetroTerm exited during a test session and nothing could say whether it had
/// crashed or been closed. The old handler wrapped <c>Main</c> only, so a crash on any other thread
/// killed the process in silence, and what it did write went to the folder the program was
/// published into - which is not where anybody looks and, under Program Files, is not writable.
/// <para><b>What is testable here and what is not</b></para>
/// Whether the handlers actually fire cannot be tested without crashing the test host. What CAN be
/// tested is the part that was wrong in the old code: the text. Every previous entry was headed
/// "STARTUP ERROR" whatever had happened - the newest one in the old file is a menu click - and an
/// inner exception chain that goes missing is the difference between a diagnosable crash and a
/// mystery.
/// </remarks>
public class CrashLogTests
{
    /// <summary>
    /// A fixed time, so the format can be asserted without depending on the clock.
    /// </summary>
    private static readonly DateTime When = new DateTime(2026, 8, 25, 11, 4, 47, DateTimeKind.Utc);

    [Fact]
    public void TheEntrySaysWhereTheCrashCameFrom()
    {
        // The whole point of the origin. "STARTUP ERROR" on a crash that happened an hour into a
        // session sends the reader looking in the wrong place, and that is what the old file does.
        string text = CrashLog.Format("background thread", new InvalidOperationException("no"), When, true);

        Assert.Contains("BACKGROUND THREAD", text);
        Assert.DoesNotContain("STARTUP", text);
    }

    [Fact]
    public void TheEntrySaysWhetherTheProcessDied()
    {
        // An unobserved task exception does NOT kill the process, and a reader who cannot tell that
        // apart from a fatal one will go hunting for a crash that never happened.
        string fatal = CrashLog.Format("background thread", new Exception("x"), When, terminating: true);
        string survived = CrashLog.Format("unobserved task", new Exception("x"), When, terminating: false);

        Assert.Contains("PROCESS IS TERMINATING", fatal);
        Assert.Contains("process kept running", survived);
    }

    [Fact]
    public void TheWholeInnerExceptionChainIsWrittenDown()
    {
        // The lesson of 5 August 2026, now pinned rather than left in a comment. A
        // TargetInvocationException on its own names no cause at all.
        var innermost = new InvalidOperationException("the actual cause");
        var middle = new ArgumentException("wrapper", innermost);
        var outer = new Exception("outermost", middle);

        string text = CrashLog.Format("startup", outer, When, true);

        Assert.Contains("outermost", text);
        Assert.Contains("wrapper", text);
        Assert.Contains("the actual cause", text);
        Assert.Contains("--- Inner exception 1 ---", text);
        Assert.Contains("--- Inner exception 2 ---", text);
    }

    [Fact]
    public void ADeepChainDoesNotRunAway()
    {
        // Exception chains can be circular in badly behaved code. The formatter stops at ten rather
        // than filling the disk while the program is already dying.
        Exception error = new Exception("innermost");
        for (int i = 0; i < 50; i++)
        {
            error = new Exception("layer " + i, error);
        }

        string text = CrashLog.Format("startup", error, When, true);

        Assert.Contains("--- Inner exception 10 ---", text);
        Assert.DoesNotContain("--- Inner exception 11 ---", text);
    }

    [Fact]
    public void ACrashWithNoExceptionObjectStillWritesSomething()
    {
        // AppDomain.UnhandledException hands over an object, not an Exception, and code that throws
        // something which is not an Exception really does arrive this way. Returning empty text
        // here would recreate the exact silence this class was built to end.
        string text = CrashLog.Format("background thread", null, When, true);

        Assert.Contains("No exception object", text);
        Assert.Contains("BACKGROUND THREAD", text);
    }

    [Fact]
    public void TheTimeIsStamped()
    {
        string text = CrashLog.Format("startup", new Exception("x"), When, true);

        Assert.Contains("2026-08-25 11:04:47 UTC", text);
    }

    [Fact]
    public void TheLogLivesUnderTheUsersOwnFolderRatherThanBesideTheExecutable()
    {
        // The old location was AppContext.BaseDirectory. Nobody looks there, and a published
        // RetroTerm under Program Files cannot write there at all - so the handler would fail
        // silently in exactly the deployment where a crash report matters most.
        string folder = CrashLog.Folder();
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.False(string.IsNullOrEmpty(folder));

        if (!string.IsNullOrEmpty(appData))
        {
            Assert.StartsWith(appData, folder);
            Assert.EndsWith("RetroTerm", folder);
        }
    }

    [Fact]
    public void TheLogIsNamedSoAReaderCanFindIt()
    {
        Assert.EndsWith("crash.log", CrashLog.Path_());
    }
}
