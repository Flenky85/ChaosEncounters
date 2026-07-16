using ChaosEncounters.Combat;
using ChaosEncounters.Logging;
using UnityModManagerNet;

namespace ChaosEncounters;

public static class Main {
    internal static UnityModManager.ModEntry.ModLogger Log;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        Log = modEntry.Logger;
        ModFileLogger.Initialize(modEntry.Path, modEntry.Info.Version, Log);
        LogInfo("General logger initialized.");
        modEntry.OnGUI = OnGUI;
        CombatStartProbe.Initialize();
        LogInfo("Chaos Encounters loaded successfully.");
        return true;
    }

    internal static void LogInfo(string message) {
        ModFileLogger.Info(message);
        Log.Log(message);
    }

    public static void OnGUI(UnityModManager.ModEntry modEntry) {

    }
}
