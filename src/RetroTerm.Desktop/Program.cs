using System;
using System.IO;
using Avalonia;

namespace RetroTerm.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // THE TWO PLACES A CRASH CAN HAPPEN THAT THE CATCH BELOW CANNOT SEE - a background thread,
        // and a task whose exception nobody awaited. Installed first, because a crash during
        // startup is still a crash.
        //
        // Why this was added: on 25 August 2026 RetroTerm exited during a test session and left
        // nothing behind that could say whether it had crashed or been closed. The catch below had
        // been the only handler, so anything off the main thread killed the process in silence.
        Diagnostics.CrashLog.Install();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Log error to file. Goes to the per-user application data folder now rather than beside
            // the executable: a published RetroTerm can sit somewhere the user cannot write, and
            // nobody looks in the program's own folder for a crash anyway.
            var errorLog = Diagnostics.CrashLog.Write("startup or main loop", ex, terminating: true)
                           ?? Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            // The entry has already been written by CrashLog.Write above, INCLUDING the whole inner
            // exception chain - a TargetInvocationException without its inner chain is useless for
            // diagnosis, which this project learned on 5 August 2026 and which the formatter there
            // now carries. Writing it a second time here would only produce a duplicate.
            try
            {
                Console.Error.WriteLine($"FATAL ERROR: {ex.Message}");
                Console.Error.WriteLine($"Error log written to: {errorLog}");
            }
            catch
            {
                // Console can fail too, in a windowed process with no console attached.
            }

            // Show error dialog using Avalonia (cross-platform)
            try
            {
                var message = $"RetroTerm failed to start.\n\n" +
                            $"Error: {ex.Message}\n\n" +
                            $"Type: {ex.GetType().Name}\n\n" +
                            $"Details logged to:\n{errorLog}\n\n" +
                            $"If native DLLs are missing (libSkiaSharp.dll, libHarfBuzzSharp.dll, av_libglesv2.dll),\n" +
                            $"make sure they are in the same folder as RetroTerm.Desktop.exe";

                // Use Avalonia's cross-platform dialog system
                ShowErrorDialog(message);
            }
            catch
            {
                // Dialog failed, error already in console/log
            }

            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Shows a cross-platform error dialog
    /// </summary>
    private static void ShowErrorDialog(string message)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Win32 MessageBox straight from user32.dll.
                //
                // This used to call System.Windows.Forms.MessageBox, which required a
                // FrameworkReference to Microsoft.WindowsDesktop.App.WindowsForms and put
                // ~23 MB of WinForms assemblies into every published build - for one
                // message box shown only when startup has already failed. The P/Invoke
                // below is the same dialog with no framework dependency at all.
                //
                // MB_OK (0x0) | MB_ICONERROR (0x10). Owner window is IntPtr.Zero because
                // at this point there is no window - Avalonia failed to start.
                MessageBoxW(IntPtr.Zero, message, "RetroTerm Startup Error", 0x00000010);
            }
            else if (OperatingSystem.IsMacOS())
            {
                // Use macOS native dialog
                var result = System.Diagnostics.Process.Start("osascript", $"-e 'display dialog \"{message.Replace("\"", "\\\"")}\" with title \"RetroTerm Startup Error\" with icon stop buttons {{\"OK\"}} default button \"OK\"'");
                result?.WaitForExit();
            }
            else if (OperatingSystem.IsLinux())
            {
                // Use Linux native dialog (zenity, kdialog, or xmessage)
                var result = System.Diagnostics.Process.Start("zenity", $"--error --title=\"RetroTerm Startup Error\" --text=\"{message.Replace("\"", "\\\"")}\"");
                if (result == null)
                {
                    result = System.Diagnostics.Process.Start("kdialog", $"--error \"{message.Replace("\"", "\\\"")}\" --title=\"RetroTerm Startup Error\"");
                }
                if (result == null)
                {
                    result = System.Diagnostics.Process.Start("xmessage", $"-center \"{message}\"");
                }
                result?.WaitForExit();
            }
            else
            {
                // Fallback to console for unknown platforms
                Console.Error.WriteLine("ERROR: " + message);
            }
        }
        catch
        {
            // If native dialog fails, fall back to console
            Console.Error.WriteLine("ERROR: " + message);
        }
    }

    /// <summary>
    /// Win32 MessageBox. Declared here rather than pulling in WinForms for one dialog.
    /// Safe to declare on every platform - it is only ever called inside an
    /// <see cref="OperatingSystem.IsWindows"/> guard, so the import is never resolved
    /// on Linux or macOS.
    /// </summary>
    /// <param name="hWnd">
    /// Owner window handle; <see cref="IntPtr.Zero"/> for no owner.
    /// </param>
    /// <param name="text">
    /// Message body.
    /// </param>
    /// <param name="caption">
    /// Dialog title.
    /// </param>
    /// <param name="type">
    /// MB_* flag combination.
    /// </param>
    /// <returns>
    /// The ID of the button the user pressed.
    /// </returns>
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
