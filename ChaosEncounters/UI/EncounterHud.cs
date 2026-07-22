using HarmonyLib;
using Kingmaker.Blueprints.Root;
using Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace ChaosEncounters.UI;

internal static class EncounterHud {
    private const string HarmonyId = "ChaosEncounters.EncounterHud";
    private const string ContainerName = "ChaosEncountersEncounterHud";
    private const string IndicatorName = "ChaosEncountersEncounterHudIndicator";

    private static string ActiveTitle;
    private static string ActiveDescription;
    private static SurfaceHUDPCView CurrentHud;
    private static EncounterHudHost CurrentHost;
    private static bool Initialized;
    private static bool PresentationFailureLogged;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(EncounterHudBindPatch))
            .Patch();
        Initialized = true;
    }

    internal static void Show(string title, string description) {
        if (string.IsNullOrEmpty(title)) {
            Hide();
            return;
        }
        if (string.Equals(ActiveTitle, title, StringComparison.Ordinal) &&
            string.Equals(
                ActiveDescription,
                description,
                StringComparison.Ordinal)) {
            return;
        }

        ActiveTitle = title;
        ActiveDescription = description;
        DestroyCurrentIndicator(preserveHudReference: true);
        TryCreateIndicator();
    }

    internal static void Hide() {
        ActiveTitle = null;
        ActiveDescription = null;
        DestroyCurrentIndicator(preserveHudReference: true);
    }

    internal static void ResetForAreaUnload() {
        ActiveTitle = null;
        ActiveDescription = null;
        DestroyCurrentIndicator(preserveHudReference: false);
        CurrentHud = null;
        PresentationFailureLogged = false;
    }

    internal static void HandleHudBound(SurfaceHUDPCView hud) {
        if (hud == null) {
            return;
        }

        if (!ReferenceEquals(CurrentHud, hud)) {
            DestroyCurrentIndicator(preserveHudReference: false);
            CurrentHud = hud;
        }

        if (string.IsNullOrEmpty(ActiveTitle)) {
            return;
        }

        TryCreateIndicator();
    }

    internal static void HandleHostDestroyed(
        EncounterHudHost host,
        SurfaceHUDPCView owningHud,
        bool preserveHudReference) {
        if (ReferenceEquals(CurrentHost, host)) {
            CurrentHost = null;
        }

        if (!preserveHudReference &&
            ReferenceEquals(CurrentHud, owningHud)) {
            CurrentHud = null;
        }
    }

    internal static void HandleBindFailure(
        SurfaceHUDPCView hud,
        Exception exception) {
        if (ReferenceEquals(CurrentHud, hud)) {
            DestroyCurrentIndicator(preserveHudReference: true);
        }
        LogPresentationFailureOnce("bind", exception);
    }

    internal static void HandleHostFailure(
        string operation,
        Exception exception) {
        LogPresentationFailureOnce(operation, exception);
    }

    private static void TryCreateIndicator() {
        if (string.IsNullOrEmpty(ActiveTitle) ||
            CurrentHud == null ||
            CurrentHost != null) {
            return;
        }

        GameObject containerObject = null;
        TooltipHandler tooltip = null;
        try {
            RectTransform parent = CurrentHud.transform as RectTransform;
            if (parent == null) {
                throw new InvalidOperationException(
                    "The bound Surface HUD root is not a RectTransform.");
            }

            Sprite icon =
                UIConfig.Instance?.UIIcons?.DefaultAbilityIcon;
            if (icon == null) {
                throw new InvalidOperationException(
                    "UIConfig.Instance.UIIcons.DefaultAbilityIcon is unavailable.");
            }

            containerObject = new GameObject(
                ContainerName,
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup));
            containerObject.layer = parent.gameObject.layer;
            RectTransform container =
                (RectTransform)containerObject.transform;
            container.SetParent(parent, worldPositionStays: false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.zero;
            container.pivot = Vector2.zero;
            container.anchoredPosition = new Vector2(15f, 180f);
            container.sizeDelta = new Vector2(216f, 68f);
            container.SetAsLastSibling();

            HorizontalLayoutGroup layout =
                containerObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var indicatorObject = new GameObject(
                IndicatorName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            indicatorObject.layer = containerObject.layer;
            RectTransform indicator =
                (RectTransform)indicatorObject.transform;
            indicator.SetParent(container, worldPositionStays: false);
            indicator.sizeDelta = new Vector2(50f, 50f);

            Image image = indicatorObject.GetComponent<Image>();
            image.sprite = icon;
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = true;

            var tooltipConfig = new TooltipConfig(
                InfoCallPCMethod.None,
                InfoCallConsoleMethod.None,
                tooltipPlace: indicator);
            tooltip = image.SetTooltip(
                new TooltipTemplateSimple(
                    ActiveTitle,
                    ActiveDescription),
                tooltipConfig);
            if (tooltip == null) {
                throw new InvalidOperationException(
                    "TooltipHelper.SetTooltip returned null.");
            }

            EncounterHudHost host =
                containerObject.AddComponent<EncounterHudHost>();
            host.Initialize(CurrentHud, tooltip);
            CurrentHost = host;
            tooltip = null;
        } catch (Exception exception) {
            try {
                tooltip?.Dispose();
            } catch {
                // Continue with custom container cleanup.
            }
            DestroyPartialContainer(containerObject);
            LogPresentationFailureOnce("creation", exception);
        }
    }

    private static void DestroyCurrentIndicator(
        bool preserveHudReference) {
        EncounterHudHost host = CurrentHost;
        CurrentHost = null;
        if (host == null) {
            return;
        }

        try {
            host.DisposeAndDestroy(preserveHudReference);
        } catch (Exception exception) {
            DestroyPartialContainer(host.gameObject);
            LogPresentationFailureOnce("cleanup", exception);
        }
    }

    private static void DestroyPartialContainer(
        GameObject containerObject) {
        if (containerObject == null) {
            return;
        }

        try {
            containerObject.SetActive(false);
        } catch {
            // Continue with destruction so custom UI fails closed.
        }

        try {
            UnityEngine.Object.Destroy(containerObject);
        } catch {
            // The custom object is already inactive.
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
            $"Encounter HUD presentation failed during {operation}: {exception}");
    }
}

internal sealed class EncounterHudHost : MonoBehaviour {
    private SurfaceHUDPCView OwningHud;
    private TooltipHandler Tooltip;
    private bool PreserveHudReference;
    private bool DestroyRequested;
    private bool TooltipDisposed;

    internal void Initialize(
        SurfaceHUDPCView owningHud,
        TooltipHandler tooltip) {
        OwningHud = owningHud;
        Tooltip = tooltip;
    }

    internal void DisposeAndDestroy(
        bool preserveHudReference) {
        if (DestroyRequested) {
            return;
        }

        DestroyRequested = true;
        PreserveHudReference = preserveHudReference;
        try {
            gameObject.SetActive(false);
        } finally {
            DisposeTooltip();
            Destroy(gameObject);
        }
    }

    private void OnDestroy() {
        try {
            DisposeTooltip();
        } catch (Exception exception) {
            EncounterHud.HandleHostFailure(
                "destruction",
                exception);
        } finally {
            EncounterHud.HandleHostDestroyed(
                this,
                OwningHud,
                PreserveHudReference);
            OwningHud = null;
        }
    }

    private void DisposeTooltip() {
        if (TooltipDisposed) {
            return;
        }

        TooltipDisposed = true;
        Tooltip?.Dispose();
        Tooltip = null;
    }
}

[HarmonyPatch(
    typeof(SurfaceHUDPCView),
    nameof(SurfaceHUDPCView.BindViewImplementation))]
internal static class EncounterHudBindPatch {
    [HarmonyPostfix]
    private static void Postfix(SurfaceHUDPCView __instance) {
        try {
            EncounterHud.HandleHudBound(__instance);
        } catch (Exception exception) {
            EncounterHud.HandleBindFailure(
                __instance,
                exception);
        }
    }
}
