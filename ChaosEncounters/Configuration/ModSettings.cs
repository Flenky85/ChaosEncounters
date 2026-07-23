using System.Text;
using Newtonsoft.Json;

namespace ChaosEncounters.Configuration;

internal static class ModSettings {
    private const int CurrentVersion = 1;
    private const string SettingsFileName = "Settings.json";
    private const string LegacyDisabledNemesisProtocolId =
        "Link";
    private const string CurrentDisabledNemesisProtocolId =
        "NemesisProtocol";

    private static readonly HashSet<string>
        DisabledEncounterMechanicIds =
            new(StringComparer.Ordinal);

    private static string SettingsPath;
    private static bool Initialized;

    internal static void Initialize(string modPath) {
        if (Initialized) {
            return;
        }

        Initialized = true;
        DisabledEncounterMechanicIds.Clear();
        try {
            if (string.IsNullOrWhiteSpace(modPath)) {
                throw new ArgumentException(
                    "The deployed mod path is unavailable.",
                    nameof(modPath));
            }

            SettingsPath = Path.Combine(
                modPath,
                SettingsFileName);
            if (!File.Exists(SettingsPath)) {
                SaveSettingsCore();
                return;
            }

            string json = File.ReadAllText(
                SettingsPath,
                Encoding.UTF8);
            SettingsData settings =
                JsonConvert.DeserializeObject<SettingsData>(json);
            ValidateSettings(settings);

            foreach (string mechanicId in
                settings.DisabledEncounterMechanicIds) {
                DisabledEncounterMechanicIds.Add(mechanicId);
            }
            if (DisabledEncounterMechanicIds.Remove(
                    LegacyDisabledNemesisProtocolId)) {
                DisabledEncounterMechanicIds.Add(
                    CurrentDisabledNemesisProtocolId);
                try {
                    SaveSettingsCore();
                } catch (Exception exception) {
                    Main.LogError(
                        $"Encounter settings migration could not be saved to " +
                        $"'{SettingsPath ?? "<unavailable>"}': {exception}");
                }
            }
        } catch (Exception exception) {
            DisabledEncounterMechanicIds.Clear();
            Main.LogWarning(
                $"Encounter settings could not be initialized from " +
                $"'{SettingsPath ?? "<unavailable>"}'; all mechanics " +
                $"are enabled for this process: {exception}");
        }
    }

    internal static bool IsEncounterMechanicEnabled(
        string mechanicId) {
        return string.IsNullOrEmpty(mechanicId) ||
               !DisabledEncounterMechanicIds.Contains(mechanicId);
    }

    internal static void SetEncounterMechanicEnabled(
        string mechanicId,
        bool enabled) {
        if (string.IsNullOrEmpty(mechanicId)) {
            return;
        }

        bool changed = enabled
            ? DisabledEncounterMechanicIds.Remove(mechanicId)
            : DisabledEncounterMechanicIds.Add(mechanicId);
        if (!changed) {
            return;
        }

        try {
            SaveSettingsCore();
        } catch (Exception exception) {
            Main.LogError(
                $"Encounter settings could not be saved to " +
                $"'{SettingsPath ?? "<unavailable>"}': {exception}");
        }
    }

    private static void ValidateSettings(SettingsData settings) {
        if (settings == null) {
            throw new InvalidDataException(
                "The settings document is empty.");
        }
        if (settings.Version != CurrentVersion) {
            throw new InvalidDataException(
                $"Unsupported settings version {settings.Version}; " +
                $"expected {CurrentVersion}.");
        }
        if (settings.DisabledEncounterMechanicIds == null) {
            throw new InvalidDataException(
                "DisabledEncounterMechanicIds is missing or null.");
        }

        foreach (string mechanicId in
            settings.DisabledEncounterMechanicIds) {
            if (string.IsNullOrWhiteSpace(mechanicId)) {
                throw new InvalidDataException(
                    "DisabledEncounterMechanicIds contains an empty ID.");
            }
        }
    }

    private static void SaveSettingsCore() {
        if (string.IsNullOrEmpty(SettingsPath)) {
            throw new InvalidOperationException(
                "The encounter settings path is unavailable.");
        }

        var settings = new SettingsData {
            Version = CurrentVersion,
            DisabledEncounterMechanicIds =
                DisabledEncounterMechanicIds
        };
        string json = JsonConvert.SerializeObject(
            settings,
            Formatting.Indented);
        File.WriteAllText(
            SettingsPath,
            json,
            Encoding.UTF8);
    }

    private sealed class SettingsData {
        public int Version { get; set; }
        public HashSet<string> DisabledEncounterMechanicIds {
            get;
            set;
        }
    }
}
