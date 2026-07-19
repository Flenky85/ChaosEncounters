using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit.PC;
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;
using Kingmaker.EntitySystem.Entities;
using TMPro;
using UnityEngine;

namespace ChaosEncounters.UI;

internal static class UnitMarker {
    private const string HarmonyId = "ChaosEncounters.UnitMarker";

    private static readonly UnitReferenceComparer UnitComparer = new();
    private static readonly Dictionary<BaseUnitEntity, string> MarkerTexts =
        new(UnitComparer);
    private static readonly Dictionary<BaseUnitEntity, OvertipUnitPCView> ActiveViews =
        new(UnitComparer);

    private static bool Initialized;
    private static bool PresentationFailureLogged;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(UnitMarkerBindPatch)).Patch();
        harmony.CreateClassProcessor(typeof(UnitMarkerDestroyPatch)).Patch();
        Initialized = true;
    }

    internal static void SetMarker(BaseUnitEntity unit, string text) {
        if (!IsSupportedUnit(unit)) {
            return;
        }
        if (string.IsNullOrEmpty(text)) {
            ClearMarker(unit);
            return;
        }
        if (MarkerTexts.TryGetValue(unit, out string currentText) &&
            string.Equals(currentText, text, StringComparison.Ordinal)) {
            return;
        }

        MarkerTexts[unit] = text;
        if (ActiveViews.TryGetValue(unit, out OvertipUnitPCView view)) {
            ShowMarker(unit, view, text);
        }
    }

    internal static void ClearMarker(BaseUnitEntity unit) {
        if (unit == null) {
            return;
        }

        MarkerTexts.Remove(unit);
        if (ActiveViews.TryGetValue(unit, out OvertipUnitPCView view)) {
            HideMarker(unit, view);
        }
    }

    internal static void ClearAllMarkers() {
        MarkerTexts.Clear();
        foreach (KeyValuePair<BaseUnitEntity, OvertipUnitPCView> binding in ActiveViews) {
            try {
                UnitMarkerHost host =
                    binding.Value?.GetComponent<UnitMarkerHost>();
                if (host != null &&
                    ReferenceEquals(host.BoundUnit, binding.Key)) {
                    host.HideMarker();
                }
            } catch (Exception exception) {
                LogPresentationFailureOnce("clear-all", exception);
            }
        }
    }

    internal static void HandleBind(OvertipUnitView view) {
        if (view is not OvertipUnitPCView pcView) {
            return;
        }

        UnitMarkerHost host = pcView.GetComponent<UnitMarkerHost>();
        if (host == null) {
            host = pcView.gameObject.AddComponent<UnitMarkerHost>();
        }
        ClearViewBinding(pcView, host);

        OvertipEntityUnitVM viewModel =
            pcView.GetViewModel() as OvertipEntityUnitVM;
        if (viewModel?.Unit is not BaseUnitEntity unit ||
            !IsSupportedUnit(unit)) {
            return;
        }

        if (ActiveViews.TryGetValue(
                unit,
                out OvertipUnitPCView previousView) &&
            !ReferenceEquals(previousView, pcView)) {
            UnitMarkerHost previousHost =
                previousView?.GetComponent<UnitMarkerHost>();
            if (previousHost != null &&
                ReferenceEquals(previousHost.BoundUnit, unit)) {
                previousHost.ClearBinding();
            }
            ActiveViews.Remove(unit);
        }

        host.Bind(unit);
        ActiveViews[unit] = pcView;
        if (MarkerTexts.TryGetValue(unit, out string text)) {
            host.ShowMarker(pcView, text);
        }
    }

    internal static void HandleDestroy(OvertipUnitView view) {
        if (view is not OvertipUnitPCView pcView) {
            return;
        }

        UnitMarkerHost host = pcView.GetComponent<UnitMarkerHost>();
        if (host != null) {
            ClearViewBinding(pcView, host);
        }
    }

    internal static void HandlePresentationFailure(
        OvertipUnitView view,
        string operation,
        Exception exception) {
        if (view is OvertipUnitPCView pcView) {
            try {
                UnitMarkerHost host =
                    pcView.GetComponent<UnitMarkerHost>();
                if (host != null) {
                    ClearViewBinding(pcView, host);
                }
            } catch {
                // Keep failures inside the custom presentation boundary.
            }
        }

        LogPresentationFailureOnce(operation, exception);
    }

    private static bool IsSupportedUnit(BaseUnitEntity unit) {
        return unit != null &&
               unit is not StarshipEntity &&
               !unit.IsDisposed &&
               unit.IsInGame;
    }

    private static void ShowMarker(
        BaseUnitEntity unit,
        OvertipUnitPCView view,
        string text) {
        try {
            UnitMarkerHost host = view?.GetComponent<UnitMarkerHost>();
            if (host == null ||
                !ReferenceEquals(host.BoundUnit, unit)) {
                RemoveActiveView(unit, view);
                return;
            }

            host.ShowMarker(view, text);
        } catch (Exception exception) {
            RemoveActiveView(unit, view);
            try {
                view?.GetComponent<UnitMarkerHost>()?.ClearBinding();
            } catch {
                // Keep failures inside the custom presentation boundary.
            }
            LogPresentationFailureOnce("update", exception);
        }
    }

    private static void HideMarker(
        BaseUnitEntity unit,
        OvertipUnitPCView view) {
        try {
            UnitMarkerHost host = view?.GetComponent<UnitMarkerHost>();
            if (host == null ||
                !ReferenceEquals(host.BoundUnit, unit)) {
                RemoveActiveView(unit, view);
                return;
            }

            host.HideMarker();
        } catch (Exception exception) {
            RemoveActiveView(unit, view);
            try {
                view?.GetComponent<UnitMarkerHost>()?.ClearBinding();
            } catch {
                // Keep failures inside the custom presentation boundary.
            }
            LogPresentationFailureOnce("clear", exception);
        }
    }

    private static void ClearViewBinding(
        OvertipUnitPCView view,
        UnitMarkerHost host) {
        BaseUnitEntity unit = host.BoundUnit;
        if (unit != null &&
            ActiveViews.TryGetValue(unit, out OvertipUnitPCView activeView) &&
            ReferenceEquals(activeView, view)) {
            ActiveViews.Remove(unit);
        }

        host.ClearBinding();
    }

    private static void RemoveActiveView(
        BaseUnitEntity unit,
        OvertipUnitPCView view) {
        if (ActiveViews.TryGetValue(
                unit,
                out OvertipUnitPCView activeView) &&
            ReferenceEquals(activeView, view)) {
            ActiveViews.Remove(unit);
        }
    }

    private static void LogPresentationFailureOnce(
        string operation,
        Exception exception) {
        if (PresentationFailureLogged) {
            return;
        }

        PresentationFailureLogged = true;
        Main.LogError(
            $"Unit marker presentation failed during {operation}: {exception}");
    }

    private sealed class UnitReferenceComparer :
        IEqualityComparer<BaseUnitEntity> {
        public bool Equals(BaseUnitEntity x, BaseUnitEntity y) {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(BaseUnitEntity obj) {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}

internal sealed class UnitMarkerHost : MonoBehaviour {
    private const string MarkerName = "ChaosEncountersUnitMarker";
    private const float MarkerFontSize = 36f;

    private TextMeshProUGUI Marker;
    private bool CreationAttempted;
    private bool CreationFailureLogged;

    internal BaseUnitEntity BoundUnit { get; private set; }

    internal void Bind(BaseUnitEntity unit) {
        ClearBinding();
        BoundUnit = unit;
    }

    internal void ShowMarker(OvertipUnitPCView view, string text) {
        if (!EnsureCreated(view)) {
            HideMarker();
            return;
        }

        if (!string.Equals(Marker.text, text, StringComparison.Ordinal)) {
            Marker.text = text;
        }
        Marker.gameObject.SetActive(true);
    }

    internal void HideMarker() {
        if (Marker == null) {
            return;
        }

        Marker.text = string.Empty;
        Marker.gameObject.SetActive(false);
    }

    internal void ClearBinding() {
        BoundUnit = null;
        HideMarker();
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
                "Unit marker could not find a native TextMeshProUGUI font in m_NameBlockPCView; marker remains hidden.");
            return false;
        }
        if (view.m_RectTransform == null) {
            LogCreationFailure(
                "Unit marker found a null m_RectTransform; marker remains hidden.");
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

[HarmonyPatch(
    typeof(OvertipUnitView),
    nameof(OvertipUnitView.BindViewImplementation))]
internal static class UnitMarkerBindPatch {
    [HarmonyPostfix]
    private static void Postfix(OvertipUnitView __instance) {
        try {
            UnitMarker.HandleBind(__instance);
        } catch (Exception exception) {
            UnitMarker.HandlePresentationFailure(
                __instance,
                "bind",
                exception);
        }
    }
}

[HarmonyPatch(
    typeof(OvertipUnitView),
    nameof(OvertipUnitView.DestroyViewImplementation))]
internal static class UnitMarkerDestroyPatch {
    [HarmonyPrefix]
    private static void Prefix(OvertipUnitView __instance) {
        try {
            UnitMarker.HandleDestroy(__instance);
        } catch (Exception exception) {
            UnitMarker.HandlePresentationFailure(
                __instance,
                "unbind",
                exception);
        }
    }
}
