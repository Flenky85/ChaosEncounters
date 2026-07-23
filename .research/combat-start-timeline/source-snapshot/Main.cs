using ChaosEncounters.Combat;
using ChaosEncounters.Logging;
using ChaosEncounters.Research;
using ChaosEncounters.UI;
using UnityModManagerNet;

namespace ChaosEncounters;

public static class Main {
    internal static UnityModManager.ModEntry.ModLogger Log;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        Log = modEntry.Logger;
        ModFileLogger.Initialize(modEntry.Path, modEntry.Info.Version, Log);
        CombatStartTimelineLogger.Initialize(modEntry.Path, modEntry.Info.Version, Log);
        LogInfo("General logger initialized.");
        modEntry.OnGUI = OnGUI;
        CombatStartTimelineProbe.Initialize();
        CombatStartProbe.Initialize();
        DamageControlPrototype.Initialize();
        RoundEndHealingPrototype.Initialize();
        SurfaceHudIndicator.Initialize();
        UnitOvertipNumberPrototype.Initialize();
        LogInfo("Chaos Encounters loaded successfully.");
        return true;
    }

    internal static void LogInfo(string message) {
        ModFileLogger.Info(message);
        Log.Log(message);
    }

    internal static void LogWarning(string message) {
        ModFileLogger.Info($"WARNING: {message}");
        Log.Warning(message);
    }

    internal static void LogError(string message) {
        ModFileLogger.Error(message);
        Log.Error(message);
    }

    public static void OnGUI(UnityModManager.ModEntry modEntry) {

    }
}
