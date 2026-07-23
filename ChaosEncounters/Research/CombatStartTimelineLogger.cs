using System.Text;
using UnityModManagerNet;

namespace ChaosEncounters.Research;

internal static class CombatStartTimelineLogger {
    internal const string SchemaVersion = "1";
    internal const string FileName = "ChaosEncounters-combat-start-timeline.log";

    private static readonly object Sync = new();
    private static string LogPath;
    private static UnityModManager.ModEntry.ModLogger UnityLogger;

    internal static void Initialize(
        string modDirectory,
        string modVersion,
        UnityModManager.ModEntry.ModLogger unityLogger) {
        UnityLogger = unityLogger;
        DateTime started = DateTime.Now;

        try {
            string logsDirectory = Path.Combine(modDirectory, "Logs");
            Directory.CreateDirectory(logsDirectory);
            LogPath = Path.Combine(logsDirectory, FileName);
            string header = string.Join(Environment.NewLine,
                "Chaos Encounters combat-start timeline research probe",
                $"Chaos Encounters version: {modVersion}",
                $"Session timestamp: {started:yyyy-MM-dd HH:mm:ss.fff zzz}",
                $"Probe schema version: {SchemaVersion}",
                $"Game mode: {SafeGameValue(() => Kingmaker.Game.Instance.CurrentMode.ToString())}",
                $"Area: {SafeGameValue(() => Kingmaker.Game.Instance.CurrentlyLoadedArea?.name ?? "Unavailable")}",
                $"Loaded game being processed: {SafeGameValue(() => Kingmaker.Game.Instance.State != null ? "UnknownAtModStartup" : "false")}",
                $"Turn-based mode already active: {SafeGameValue(() => Kingmaker.Game.Instance.TurnController?.TbActive.ToString() ?? "Unavailable")}",
                string.Empty);
            File.WriteAllText(LogPath, header, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        } catch (Exception exception) {
            LogPath = null;
            UnityLogger?.Error($"Failed to initialize {FileName}: {exception}");
        }
    }

    internal static void Write(string record) {
        lock (Sync) {
            if (LogPath == null) {
                return;
            }

            try {
                using var stream = new FileStream(
                    LogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite);
                using var writer = new StreamWriter(
                    stream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.WriteLine(record);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            } catch (Exception exception) {
                LogPath = null;
                UnityLogger?.Error($"{FileName} was disabled after a write failure: {exception}");
            }
        }
    }

    private static string SafeGameValue(Func<string> read) {
        try {
            return read() ?? "Unavailable";
        } catch {
            return "Unavailable";
        }
    }
}
