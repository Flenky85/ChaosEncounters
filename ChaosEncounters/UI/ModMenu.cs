using ChaosEncounters.Combat.Mechanics;
using UnityEngine;

namespace ChaosEncounters.UI;

internal static class ModMenu {
    private const float MinimumContentWidth = 320f;
    private const float MaximumContentWidth = 720f;
    private const string EmergencyDescription =
        "Immediately removes the active Chaos Encounters mechanic from the current combat and clears all damage modifiers, unit markers, and HUD elements created by the mod. Use this only if a mechanic makes the encounter impossible or behaves incorrectly. The combat continues and no replacement mechanic is selected.";

    private static readonly string[] TabLabels = {
        "General"
    };

    private static int SelectedTab;

    internal static void Draw() {
        float contentWidth = Mathf.Clamp(
            Screen.width / 3f,
            MinimumContentWidth,
            MaximumContentWidth);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(
            GUILayout.Width(contentWidth));

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

        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    private static void DrawGeneralTab() {
        GUILayout.Space(8f);
        GUILayout.Label("Emergency Controls");
        GUIStyle labelStyle = GUI.skin.label;
        bool previousWordWrap = labelStyle.wordWrap;
        try {
            labelStyle.wordWrap = true;
            GUILayout.Label(
                EmergencyDescription,
                labelStyle);
        } finally {
            labelStyle.wordWrap = previousWordWrap;
        }

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
