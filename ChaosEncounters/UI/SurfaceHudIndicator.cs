using HarmonyLib;
using Kingmaker.Blueprints.Root;
using Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Templates;
using Kingmaker.Code.UI.MVVM.VM.Tooltip.Utils;
using Owlcat.Runtime.UI.Tooltips;
using UnityEngine;
using UnityEngine.UI;

namespace ChaosEncounters.UI;

internal static class SurfaceHudIndicator {
    private const string HarmonyId = "ChaosEncounters.SurfaceHudIndicator";
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(SurfaceHudIndicatorPatch))
            .Patch();
        Initialized = true;
    }
}

[HarmonyPatch(typeof(SurfaceHUDPCView), nameof(SurfaceHUDPCView.BindViewImplementation))]
internal static class SurfaceHudIndicatorPatch {
    [HarmonyPostfix]
    private static void Postfix(SurfaceHUDPCView __instance) {
        SurfaceHudIndicatorController.HandleHudBound(__instance);
    }
}

internal static class SurfaceHudIndicatorController {
    private const string ParentPath = "SurfaceActionBarPCView/AdditionalContainer";
    private const string ContainerName = "ChaosEncountersIndicatorContainer";
    private const string IndicatorName = "ChaosEncountersIndicator_MatarEnOrden";
    private const string TooltipTitle = "Matar en orden";
    private const string TooltipDescription = "Mata a los enemigos siguiendo el orden indicado.";

    private static bool IsCombatActive;
    private static bool ParentLookupAttempted;
    private static SurfaceHUDPCView CurrentHud;
    private static RectTransform CurrentParent;
    private static ChaosEncounterIndicatorHost CurrentHost;

    internal static void HandleCombatStateChanged(bool inCombat) {
        IsCombatActive = inCombat;
        if (inCombat) {
            EnsureIndicator();
        } else {
            RemoveIndicator(preserveHudReference: true);
        }
    }

    internal static void HandleHudBound(SurfaceHUDPCView hud) {
        if (hud == null) {
            return;
        }

        if (CurrentHud != hud) {
            if (IsCombatActive && CurrentHud != null) {
                Main.LogInfo("Surface HUD rebound while combat is active.");
            }

            RemoveIndicator(preserveHudReference: false);
            CurrentHud = hud;
            CurrentParent = null;
            ParentLookupAttempted = false;
        }

        if (IsCombatActive) {
            EnsureIndicator();
        }
    }

    internal static void HandleHostDestroyed(
        ChaosEncounterIndicatorHost host,
        SurfaceHUDPCView owningHud,
        bool preserveHudReference) {
        if (ReferenceEquals(CurrentHost, host)) {
            CurrentHost = null;
        }

        if (!preserveHudReference && CurrentHud == owningHud) {
            CurrentHud = null;
            CurrentParent = null;
            ParentLookupAttempted = false;
        }
    }

    private static void EnsureIndicator() {
        if (!IsCombatActive || CurrentHud == null || CurrentHost != null) {
            return;
        }

        if (!ParentLookupAttempted) {
            ParentLookupAttempted = true;
            CurrentParent = CurrentHud.transform.Find(ParentPath) as RectTransform;
            if (CurrentParent == null) {
                Main.LogWarning($"Surface HUD parent '{ParentPath}' was not found; indicator was not created.");
            }
        }

        if (CurrentParent == null) {
            return;
        }

        Sprite icon;
        try {
            icon = UIConfig.Instance?.UIIcons?.DefaultAbilityIcon;
        } catch (Exception exception) {
            Main.LogError($"Failed to obtain the default ability icon; indicator was not created: {exception}");
            return;
        }

        if (icon == null) {
            Main.LogError("UIConfig.Instance.UIIcons.DefaultAbilityIcon is unavailable; indicator was not created.");
            return;
        }

        GameObject containerObject = null;
        TooltipHandler tooltip = null;
        try {
            containerObject = new GameObject(ContainerName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            containerObject.layer = CurrentParent.gameObject.layer;
            RectTransform container = (RectTransform)containerObject.transform;
            container.SetParent(CurrentParent, worldPositionStays: false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.zero;
            container.pivot = Vector2.zero;
            container.anchoredPosition = new Vector2(18f, 185f);
            container.sizeDelta = new Vector2(216f, 68f);
            container.SetAsLastSibling();

            HorizontalLayoutGroup layout = containerObject.GetComponent<HorizontalLayoutGroup>();
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
            RectTransform indicator = (RectTransform)indicatorObject.transform;
            indicator.SetParent(container, worldPositionStays: false);
            indicator.sizeDelta = new Vector2(68f, 68f);

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
                new TooltipTemplateSimple(TooltipTitle, TooltipDescription),
                tooltipConfig);
            if (tooltip == null) {
                throw new InvalidOperationException("TooltipHelper.SetTooltip returned null.");
            }

            CurrentHost = containerObject.AddComponent<ChaosEncounterIndicatorHost>();
            CurrentHost.Initialize(CurrentHud, tooltip);
            tooltip = null;
            Main.LogInfo("Chaos encounter indicator container created.");
        } catch (Exception exception) {
            tooltip?.Dispose();
            if (containerObject != null) {
                containerObject.SetActive(false);
                UnityEngine.Object.Destroy(containerObject);
            }
            Main.LogError($"Failed to create the Surface HUD indicator: {exception}");
        }
    }

    private static void RemoveIndicator(bool preserveHudReference) {
        if (CurrentHost == null) {
            CurrentHost = null;
            return;
        }

        ChaosEncounterIndicatorHost host = CurrentHost;
        CurrentHost = null;
        host.DisposeAndDestroy(preserveHudReference);
        Main.LogInfo("Chaos encounter indicator container removed.");
    }
}

internal sealed class ChaosEncounterIndicatorHost : MonoBehaviour {
    private SurfaceHUDPCView OwningHud;
    private TooltipHandler Tooltip;
    private bool PreserveHudReference;
    private bool IsDisposed;

    internal void Initialize(SurfaceHUDPCView owningHud, TooltipHandler tooltip) {
        OwningHud = owningHud;
        Tooltip = tooltip;
    }

    internal void DisposeAndDestroy(bool preserveHudReference) {
        PreserveHudReference = preserveHudReference;
        gameObject.SetActive(false);
        DisposeTooltip();
        Destroy(gameObject);
    }

    private void OnDestroy() {
        DisposeTooltip();
        SurfaceHudIndicatorController.HandleHostDestroyed(this, OwningHud, PreserveHudReference);
        OwningHud = null;
    }

    private void DisposeTooltip() {
        if (IsDisposed) {
            return;
        }

        IsDisposed = true;
        Tooltip?.Dispose();
        Tooltip = null;
    }
}
