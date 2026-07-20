using ChaosEncounters.Combat.Mechanics;
using UnityEngine;

namespace ChaosEncounters.UI;

internal static class ModMenu {
    private const string EmergencyDescription =
        "Immediately removes the active Chaos Encounters mechanic from the current combat and clears all damage modifiers, unit markers, and HUD elements created by the mod. Use this only if a mechanic makes the encounter impossible or behaves incorrectly. The combat continues and no replacement mechanic is selected.";

    private static readonly string[] TabLabels = {
        "General"
    };

    private static int SelectedTab;

    internal static void Draw() {
        SelectedTab = GUILayout.Toolbar(
            SelectedTab,
            TabLabels);

        switch (SelectedTab) {
            case 0:
                DrawGeneralTab();
                break;
            default:
                SelectedTab = 0;
                DrawGeneralTab();
                break;
        }
    }

    private static void DrawGeneralTab() {
        GUILayout.Space(8f);
        GUILayout.Label("Emergency Controls");
        GUILayout.Label(EmergencyDescription);

        bool previousGuiEnabled = GUI.enabled;
        try {
            GUI.enabled =
                previousGuiEnabled &&
                EncounterMechanicController.HasActiveMechanic;
            if (GUILayout.Button("Disable Active Mechanic")) {
                EncounterMechanicController
                    .DisableActiveMechanicForCurrentCombat();
            }
        } finally {
            GUI.enabled = previousGuiEnabled;
        }
    }
}
