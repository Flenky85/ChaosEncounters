using System;
using System.IO;
using System.Text;
using UnityModManagerNet;

namespace ChaosEncounters.Logging;

internal static class UnitOvertipHierarchyLogger {
    private static readonly object SyncRoot = new();
    private static string LogPath;
    private static UnityModManager.ModEntry.ModLogger UnityLogger;
    private static bool Enabled;

    internal static void Initialize(
        string modDirectory,
        string modVersion,
        UnityModManager.ModEntry.ModLogger unityLogger) {
        lock (SyncRoot) {
            UnityLogger = unityLogger;
            Enabled = false;

            try {
                LogPath = Path.Combine(modDirectory, "Logs", "UnitOvertipHierarchy.log");
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));

                var header = new StringBuilder(192);
                header.AppendLine("Chaos Encounters - Unit Overtip Hierarchy Probe");
                header.Append("Mod version: ").AppendLine(modVersion ?? "<unknown>");
                header.Append("Session start: ")
                    .AppendLine(DateTimeOffset.Now.ToString("O"));
                header.AppendLine();

                File.WriteAllText(LogPath, header.ToString(), new UTF8Encoding(false));
                Enabled = true;
            } catch (Exception exception) {
                LogPath = null;
                UnityLogger?.Error(
                    $"Failed to initialize Logs/UnitOvertipHierarchy.log; hierarchy probe logging is disabled: {exception}");
            }
        }
    }

    internal static void WriteDiagnostic(string diagnostic) {
        WriteEntry(diagnostic);
    }

    internal static void WriteError(string message) {
        WriteEntry($"Unit overtip hierarchy probe error: {message}");
    }

    private static void WriteEntry(string entry) {
        lock (SyncRoot) {
            if (!Enabled) {
                return;
            }

            try {
                File.AppendAllText(LogPath, entry + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
            } catch (Exception exception) {
                Enabled = false;
                UnityLogger?.Error(
                    $"Failed to write Logs/UnitOvertipHierarchy.log; further hierarchy probe writes are disabled: {exception}");
            }
        }
    }
}
