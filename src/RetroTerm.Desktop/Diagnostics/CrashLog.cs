using System;
using System.IO;
using System.Text;

namespace RetroTerm.Desktop.Diagnostics;

/// <summary>
/// Where a crash gets written down, so that "it just disappeared" is answerable.
/// </summary>
/// <remarks>
/// <para><b>What went wrong that this exists to fix</b></para>
/// On 25 August 2026 RetroTerm exited during a test session and left nothing behind that said
/// whether it had crashed or been closed. Four things were wrong at once, and only the fourth was
/// obvious:
///  - the only handler was around <c>Main</c>, so a crash on ANY other thread killed the process
///    silently.
///  - nothing watched <see cref="System.Threading.Tasks.TaskScheduler.UnobservedTaskException"/>,
///    which is how an async failure in this program would most likely arrive.
///  - the log went to the folder the program was published into. That is not where anybody looks,
///    and under Program Files it is not even writable.
///  - every entry was headed "STARTUP ERROR" whatever had happened. The newest entry in the old
///    file is a menu click, and it says startup.
/// <para><b>The rule this follows</b></para>
/// A crash log is written for the person reading it a week later, not for the process writing it.
/// So it says WHEN, WHERE from, and the whole inner-exception chain - a
/// <c>TargetInvocationException</c> without its inner chain is useless for diagnosis, which this
/// project learned on 5 August 2026.
/// </remarks>
public static class CrashLog
{
    /// <summary>
    /// The folder crash logs go in.
    /// </summary>
    /// <returns>
    /// The per-user application data folder, or the program's own folder if that cannot be reached.
    /// </returns>
    /// <remarks>
    /// Per-user, NOT beside the executable. A published RetroTerm can sit somewhere the user cannot
    /// write, and a crash handler that throws while handling a crash tells nobody anything. The
    /// fallback is the old location, so a machine where the application data folder is unavailable
    /// still gets something rather than nothing.
    /// </remarks>
    public static string Folder()
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
            {
                return Path.Combine(appData, "RetroTerm");
            }
        }
        catch
        {
            // Falls through to the executable's own folder below.
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// The full path of the crash log.
    /// </summary>
    /// <returns>
    /// The file crashes are appended to.
    /// </returns>
    public static string Path_()
    {
        return Path.Combine(Folder(), "crash.log");
    }

    /// <summary>
    /// Renders one crash as the text that goes in the file.
    /// </summary>
    /// <param name="origin">
    /// Where the crash came from, in words a reader will understand - "startup", "background
    /// thread", "unobserved task".
    /// </param>
    /// <param name="error">
    /// The exception. Null is tolerated, because the runtime's unhandled-exception event carries an
    /// <c>object</c> and is not obliged to hand over an <see cref="Exception"/>.
    /// </param>
    /// <param name="whenUtc">
    /// The time to stamp it with. Passed in rather than read here so the format can be tested.
    /// </param>
    /// <param name="terminating">
    /// Whether the process is going down because of this.
    /// </param>
    /// <returns>
    /// The entry, ending in a blank line.
    /// </returns>
    public static string Format(string origin, Exception? error, DateTime whenUtc, bool terminating)
    {
        var text = new StringBuilder();

        text.Append('[').Append(whenUtc.ToString("yyyy-MM-dd HH:mm:ss")).Append(" UTC] ")
            .Append(origin.ToUpperInvariant())
            .Append(terminating ? " - PROCESS IS TERMINATING" : " - process kept running")
            .Append('\n');

        if (error == null)
        {
            // Not a curiosity. AppDomain.UnhandledException hands over an object, and a throw of
            // something that is not an Exception really does reach it as one of these.
            text.Append("No exception object was supplied with this crash.\n\n");
            return text.ToString();
        }

        Exception? current = error;
        int depth = 0;

        while (current != null && depth <= 10)
        {
            if (depth > 0)
            {
                text.Append("--- Inner exception ").Append(depth).Append(" ---\n");
            }

            text.Append("Message: ").Append(current.Message).Append('\n');
            text.Append("Type: ").Append(current.GetType().FullName).Append('\n');
            text.Append("Stack Trace:\n").Append(current.StackTrace).Append('\n');

            current = current.InnerException;
            depth++;
        }

        text.Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Appends one crash to the log, and never throws.
    /// </summary>
    /// <param name="origin">
    /// Where the crash came from.
    /// </param>
    /// <param name="error">
    /// The exception, if there is one.
    /// </param>
    /// <param name="terminating">
    /// Whether the process is going down because of this.
    /// </param>
    /// <returns>
    /// The path written to, or null when nothing could be written.
    /// </returns>
    /// <remarks>
    /// Swallowing everything is right here and nowhere else: this runs while the program is already
    /// failing, and an exception thrown out of a crash handler replaces a diagnosable crash with an
    /// undiagnosable one.
    /// </remarks>
    public static string? Write(string origin, Exception? error, bool terminating)
    {
        try
        {
            string folder = Folder();
            Directory.CreateDirectory(folder);

            string path = System.IO.Path.Combine(folder, "crash.log");
            File.AppendAllText(path, Format(origin, error, DateTime.UtcNow, terminating));
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Starts watching the two places a crash can happen that <c>Main</c> cannot see.
    /// </summary>
    /// <remarks>
    /// Called once, as early as possible. Neither of these can PREVENT anything - by the time
    /// either fires the decision has been made - but both turn a silent disappearance into a file
    /// with a stack trace in it.
    /// </remarks>
    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Write("background thread", args.ExceptionObject as Exception, args.IsTerminating);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            // NOT terminating: since .NET 4.5 an unobserved task exception does not kill the
            // process. It is logged because it is still a real failure that would otherwise vanish
            // completely, and in an application this async it is the likeliest kind.
            Write("unobserved task", args.Exception, terminating: false);
            args.SetObserved();
        };
    }
}
