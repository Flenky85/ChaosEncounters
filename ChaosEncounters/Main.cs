using HarmonyLib;
using System.Text;
using ChaosEncounters.Combat;
using ChaosEncounters.Logging;
using System.Reflection;
using UnityModManagerNet;

namespace ChaosEncounters;

public static class Main {
    internal static Harmony HarmonyInstance;
    internal static UnityModManager.ModEntry.ModLogger Log;

    public static bool Load(UnityModManager.ModEntry modEntry) {
        Log = modEntry.Logger;
        ModFileLogger.Initialize(modEntry.Info.Version, Log);
        LogInfo("General logger initialized.");
        modEntry.OnGUI = OnGUI;
        HarmonyInstance = new Harmony(modEntry.Info.Id);
        LogInfo("Applying Harmony patches.");
        try {
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            LogInfo("Harmony patches applied successfully.");
        } catch (Exception exception) {
            HarmonyInstance.UnpatchAll(HarmonyInstance.Id);
            ModFileLogger.Error($"Harmony patching failed: {exception}");
            Log.Error($"Harmony patching failed: {exception}");
            throw;
        }
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
