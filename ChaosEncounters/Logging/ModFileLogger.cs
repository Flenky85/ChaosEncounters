using System.Text;
using UnityModManagerNet;

namespace ChaosEncounters.Logging;

internal static class ModFileLogger {
    private static string LogPath;
    private static string SurfaceHudLogPath;
    private static UnityModManager.ModEntry.ModLogger UnityLogger;

    internal static void Initialize(string modDirectory, string modVersion, UnityModManager.ModEntry.ModLogger unityLogger) {
        UnityLogger = unityLogger;
        string logsDirectory = Path.Combine(modDirectory, "Logs");
        DateTime sessionStart = DateTime.Now;
        string header = string.Join(Environment.NewLine,
            "Chaos Encounters",
            $"Mod version: {modVersion}",
            $"Session started: {sessionStart:yyyy-MM-dd HH:mm:ss.fff}",
            string.Empty,
            string.Empty);

        try {
            Directory.CreateDirectory(logsDirectory);
            LogPath = Path.Combine(logsDirectory, "General.log");
            File.WriteAllText(LogPath, header, Encoding.UTF8);
        } catch (Exception exception) {
            LogPath = null;
            UnityLogger.Error($"Failed to initialize General.log: {exception}");
        }

        try {
            Directory.CreateDirectory(logsDirectory);
            SurfaceHudLogPath = Path.Combine(logsDirectory, "SurfaceHud.log");
            File.WriteAllText(SurfaceHudLogPath, header, Encoding.UTF8);
        } catch (Exception exception) {
            SurfaceHudLogPath = null;
            UnityLogger.Error($"Failed to initialize SurfaceHud.log: {exception}");
        }
    }

    internal static void Info(string message) {
        Write("INFO", message);
    }

    internal static void Error(string message) {
        Write("ERROR", message);
    }

    internal static bool WriteSurfaceHudBlock(string message) {
        if (SurfaceHudLogPath == null) {
            return false;
        }

        try {
            File.AppendAllText(SurfaceHudLogPath, message + Environment.NewLine, Encoding.UTF8);
            return true;
        } catch (Exception exception) {
            SurfaceHudLogPath = null;
            UnityLogger.Error($"Failed to write to SurfaceHud.log: {exception}");
            return false;
        }
    }

    private static void Write(string level, string message) {
        if (LogPath == null) {
            return;
        }

        try {
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, entry, Encoding.UTF8);
        } catch (Exception exception) {
            LogPath = null;
            UnityLogger.Error($"Failed to write to General.log: {exception}");
        }
    }
}
