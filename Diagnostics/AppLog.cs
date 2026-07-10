using System.IO;

namespace TouhouScaleChanger.Diagnostics;

/// <summary>
/// Minimal thread-safe file logger. Best-effort: logging must never throw and never
/// take down the host process, so every failure is swallowed.
/// </summary>
public static class AppLog
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TouhouScaleChanger", "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "touhouscalechanger.log");
    private const long MaxLogBytes = 512 * 1024;

    public static string DirectoryPath => LogDirectory;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                RollIfNeeded();
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (exception is not null) line += Environment.NewLine + exception;
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging is best-effort; never propagate failures.
        }
    }

    private static void RollIfNeeded()
    {
        try
        {
            var info = new FileInfo(LogPath);
            if (!info.Exists || info.Length < MaxLogBytes) return;
            var archive = LogPath + ".1";
            File.Delete(archive);
            File.Move(LogPath, archive);
        }
        catch
        {
            // If rolling fails, keep appending to the current file.
        }
    }
}
