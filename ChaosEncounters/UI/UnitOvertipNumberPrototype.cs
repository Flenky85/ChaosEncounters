using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit.PC;
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;
using Kingmaker.GameModes;
using TMPro;
using UnityEngine;

namespace ChaosEncounters.UI;

internal static class UnitOvertipNumberPrototype {
    private const string HarmonyId = "ChaosEncounters.UnitOvertipNumberPrototype";
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(UnitOvertipNumberBindPatch)).Patch();
        harmony.CreateClassProcessor(typeof(UnitOvertipNumberDestroyPatch)).Patch();
        Initialized = true;
    }

    internal static void HandleBind(OvertipUnitView view) {
        if (view is not OvertipUnitPCView pcView) {
            return;
        }

        UnitOvertipNumberPrototypeHost host = pcView.GetComponent<UnitOvertipNumberPrototypeHost>();
        host?.ClearBinding();

        OvertipEntityUnitVM viewModel = pcView.GetViewModel() as OvertipEntityUnitVM;
        Game game = Game.Instance;
        if (viewModel?.Unit == null || game?.Player?.MainCharacterEntity == null ||
            game.TurnController?.TurnBasedModeActive != true ||
            game.CurrentMode == GameModeType.SpaceCombat ||
            game.CurrentMode == GameModeType.StarSystem ||
            !viewModel.Unit.IsInCombat ||
            !ReferenceEquals(viewModel.Unit, game.Player.MainCharacterEntity)) {
            return;
        }

        if (host == null) {
            host = pcView.gameObject.AddComponent<UnitOvertipNumberPrototypeHost>();
        }
        host.Show(pcView, viewModel.UnitUIWrapper.UniqueId);
    }

    internal static void HandleDestroy(OvertipUnitView view) {
        if (view is not OvertipUnitPCView pcView) {
            return;
        }

        pcView.GetComponent<UnitOvertipNumberPrototypeHost>()?.ClearBinding();
    }
}

internal sealed class UnitOvertipNumberPrototypeHost : MonoBehaviour {
    private const string MarkerName = "ChaosEncountersNumberPrototype";
    private const string MarkerText = "7";
    private const float MarkerFontSize = 36f;

    private TextMeshProUGUI Marker;
    private string BoundUnitId;
    private bool CreationAttempted;
    private bool CreationFailureLogged;

    internal void Show(OvertipUnitPCView view, string boundUnitId) {
        ClearBinding();
        if (!EnsureCreated(view)) {
            return;
        }

        BoundUnitId = boundUnitId;
        Marker.text = MarkerText;
        Marker.gameObject.SetActive(true);
    }

    internal void ClearBinding() {
        BoundUnitId = null;
        if (Marker == null) {
            return;
        }

        Marker.text = string.Empty;
        Marker.gameObject.SetActive(false);
    }

    private bool EnsureCreated(OvertipUnitPCView view) {
        if (Marker != null) {
            return true;
        }
        if (CreationAttempted) {
            return false;
        }

        CreationAttempted = true;
        TextMeshProUGUI nativeText =
            view.m_NameBlockPCView?.GetComponentInChildren<TextMeshProUGUI>(true);
        if (nativeText == null || nativeText.font == null) {
            LogCreationFailure(
                "Unit overtip number prototype could not find a native TextMeshProUGUI font in m_NameBlockPCView; marker remains hidden.");
            return false;
        }
        if (view.m_RectTransform == null) {
            LogCreationFailure(
                "Unit overtip number prototype found a null m_RectTransform; marker remains hidden.");
            return false;
        }

        var markerObject = new GameObject(
            MarkerName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        RectTransform markerRect = (RectTransform)markerObject.transform;
        markerRect.SetParent(view.m_RectTransform, false);
        markerRect.anchorMin = new Vector2(0.5f, 0f);
        markerRect.anchorMax = new Vector2(0.5f, 0f);
        markerRect.pivot = new Vector2(0.5f, 0f);
        markerRect.anchoredPosition = new Vector2(0f, 90f);
        markerRect.sizeDelta = new Vector2(80f, 50f);

        Marker = markerObject.GetComponent<TextMeshProUGUI>();
        Marker.font = nativeText.font;
        Marker.fontSharedMaterial = nativeText.fontSharedMaterial;
        Marker.fontStyle = nativeText.fontStyle;
        Marker.fontSize = MarkerFontSize;
        Marker.enableAutoSizing = false;
        Marker.alignment = TextAlignmentOptions.Center;
        Marker.color = Color.white;
        Marker.raycastTarget = false;
        Marker.text = string.Empty;
        markerObject.SetActive(false);
        return true;
    }

    private void LogCreationFailure(string message) {
        if (CreationFailureLogged) {
            return;
        }

        CreationFailureLogged = true;
        Main.LogError(message);
    }
}

[HarmonyPatch(typeof(OvertipUnitView), nameof(OvertipUnitView.BindViewImplementation))]
internal static class UnitOvertipNumberBindPatch {
    [HarmonyPostfix]
    private static void Postfix(OvertipUnitView __instance) {
        try {
            UnitOvertipNumberPrototype.HandleBind(__instance);
        } catch (Exception exception) {
            Main.LogError($"Unit overtip number prototype bind failed: {exception}");
        }
    }
}

[HarmonyPatch(typeof(OvertipUnitView), nameof(OvertipUnitView.DestroyViewImplementation))]
internal static class UnitOvertipNumberDestroyPatch {
    [HarmonyPrefix]
    private static void Prefix(OvertipUnitView __instance) {
        try {
            UnitOvertipNumberPrototype.HandleDestroy(__instance);
        } catch (Exception exception) {
            Main.LogError($"Unit overtip number prototype cleanup failed: {exception}");
        }
    }
}
