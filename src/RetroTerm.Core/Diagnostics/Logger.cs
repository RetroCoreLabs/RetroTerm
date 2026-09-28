using System;
using System.IO;

namespace RetroTerm.Core.Diagnostics;

/// <summary>
/// Simple logger that works in Release builds
/// Logs to both console and file
/// </summary>
public static class Logger
{
    private static readonly object _lock = new object();
    private static string? _logFilePath;
    private static bool _enabled = true;

    static Logger()
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RetroTerm", "Logs");
            Directory.CreateDirectory(logDir);
            _logFilePath = Path.Combine(logDir, $"retroterm_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            Log("INIT", $"Logger initialized. Log file: {_logFilePath}");
        }
        catch
        {
            _logFilePath = null;
        }
    }

    public static void Log(string category, string message)
    {
        if (!_enabled) return;

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logMessage = $"[{timestamp}] [{category}] {message}";

        lock (_lock)
        {
            // Console output
            Console.WriteLine(logMessage);

            // File output
            if (_logFilePath != null)
            {
                try
                {
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                }
                catch
                {
                    // Ignore file write errors
                }
            }
        }
    }

    public static void LogError(string category, string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message} - {ex.Message}\n{ex.StackTrace}" : message;
        Log($"ERROR:{category}", fullMessage);
    }

    public static string? GetLogFilePath() => _logFilePath;
}


