using ChaosEncounters.Combat;
using ChaosEncounters.Combat.Mechanics;
using UnityEngine;

namespace ChaosEncounters.UI;

internal static class ModMenu {
    private const float MinimumContentWidth = 320f;
    private const float MaximumContentWidth = 720f;
    private const string EmergencyDescription =
        "Immediately removes the active Chaos Encounters mechanic from the current combat and clears all damage modifiers, unit markers, and HUD elements created by the mod. Use this only if a mechanic makes the encounter impossible or behaves incorrectly. The combat continues and no replacement mechanic is selected.";

    private static readonly string[] TabLabels = {
        "General",
        "Encounters"
    };

    private static readonly string[] EncounterTabLabels = {
        "Common",
        "Boss"
    };

    private static readonly Dictionary<IEncounterMechanic, bool>
        PlaceholderEnabledStates = new();

    private static int SelectedTopLevelTab;
    private static int SelectedEncounterTab;

    internal static void Draw() {
        float contentWidth = Mathf.Clamp(
            Screen.width / 3f,
            MinimumContentWidth,
            MaximumContentWidth);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUILayout.BeginVertical(
            GUILayout.Width(contentWidth));

        SelectedTopLevelTab = GUILayout.Toolbar(
            SelectedTopLevelTab,
            TabLabels);

        switch (SelectedTopLevelTab) {
            case 0:
                DrawGeneralTab();
                break;
            case 1:
                DrawEncountersTab();
                break;
            default:
                SelectedTopLevelTab = 0;
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
        DrawWrappedLabel(EmergencyDescription);

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

    private static void DrawEncountersTab() {
        GUILayout.Space(8f);
        SelectedEncounterTab = GUILayout.Toolbar(
            SelectedEncounterTab,
            EncounterTabLabels);

        switch (SelectedEncounterTab) {
            case 0:
                DrawEncounterCategory(EncounterType.Common);
                break;
            case 1:
                DrawEncounterCategory(EncounterType.Boss);
                break;
            default:
                SelectedEncounterTab = 0;
                DrawEncounterCategory(EncounterType.Common);
                break;
        }
    }

    private static void DrawEncounterCategory(
        EncounterType encounterType) {
        IReadOnlyList<IEncounterMechanic> mechanics =
            EncounterMechanicController.GetRegisteredMechanics(
                encounterType);
        if (mechanics.Count == 0) {
            GUILayout.Space(8f);
            GUILayout.Label(
                "No encounter mechanics are registered in this category.");
            return;
        }

        GUILayout.Space(8f);
        for (int index = 0; index < mechanics.Count; index++) {
            IEncounterMechanic mechanic = mechanics[index];
            if (mechanic == null) {
                continue;
            }

            if (!PlaceholderEnabledStates.TryGetValue(
                    mechanic,
                    out bool placeholderEnabled)) {
                placeholderEnabled = true;
                PlaceholderEnabledStates.Add(
                    mechanic,
                    placeholderEnabled);
            }

            GUILayout.BeginVertical(GUI.skin.box);
            bool updatedPlaceholderEnabled = GUILayout.Toggle(
                placeholderEnabled,
                mechanic.DisplayName);
            if (updatedPlaceholderEnabled != placeholderEnabled) {
                PlaceholderEnabledStates[mechanic] =
                    updatedPlaceholderEnabled;
            }
            DrawWrappedLabel(mechanic.Description);
            GUILayout.EndVertical();
            GUILayout.Space(6f);
        }
    }

    private static void DrawWrappedLabel(string text) {
        GUIStyle labelStyle = GUI.skin.label;
        bool previousWordWrap = labelStyle.wordWrap;
        try {
            labelStyle.wordWrap = true;
            GUILayout.Label(
                text,
                labelStyle);
        } finally {
            labelStyle.wordWrap = previousWordWrap;
        }
    }
}
