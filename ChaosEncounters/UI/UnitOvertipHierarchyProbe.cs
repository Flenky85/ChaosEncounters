using System;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit.PC;
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;
using Kingmaker.GameModes;
using ChaosEncounters.Logging;
using UnityEngine;
using UnityEngine.UI;

namespace ChaosEncounters.UI;

internal static class UnitOvertipHierarchyProbe {
    private const string HarmonyId = "ChaosEncounters.UnitOvertipHierarchyProbe";
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(UnitOvertipHierarchyProbePatch))
            .Patch();
        Initialized = true;
    }
}

[HarmonyPatch(typeof(OvertipUnitView), nameof(OvertipUnitView.BindViewImplementation))]
internal static class UnitOvertipHierarchyProbePatch {
    private const int MaximumDepth = 16;
    private const int MaximumNodes = 256;

    private static bool Attempted;
    private static bool InProgress;

    [HarmonyPostfix]
    private static void Postfix(OvertipUnitView __instance) {
        if (Attempted || InProgress || __instance == null || !__instance.IsBinded) {
            return;
        }

        OvertipEntityUnitVM viewModel = __instance.GetViewModel() as OvertipEntityUnitVM;
        if (viewModel?.Unit == null) {
            return;
        }

        Game game = Game.Instance;
        bool turnBasedCombatActive = game?.TurnController?.TurnBasedModeActive == true;
        bool terrestrialMode = game != null &&
            game.CurrentMode != GameModeType.SpaceCombat &&
            game.CurrentMode != GameModeType.StarSystem;
        bool unitInCombat = viewModel.Unit.IsInCombat;
        bool unitInTurnBasedMode = viewModel.UnitState.IsTBM.Value;

        if (__instance is not OvertipUnitPCView ||
            !terrestrialMode ||
            !unitInCombat ||
            !unitInTurnBasedMode ||
            !turnBasedCombatActive) {
            return;
        }

        Attempted = true;
        InProgress = true;
        try {
            UnitOvertipHierarchyLogger.WriteDiagnostic(
                BuildDiagnostic(__instance, viewModel, unitInCombat, turnBasedCombatActive));
        } catch (Exception exception) {
            UnitOvertipHierarchyLogger.WriteError(exception.ToString());
        } finally {
            InProgress = false;
        }
    }

    private static string BuildDiagnostic(
        OvertipUnitView view,
        OvertipEntityUnitVM viewModel,
        bool unitInCombat,
        bool turnBasedCombatActive) {
        Transform root = view.transform;
        var references = new[] {
            new ImportantReference("m_RectTransform", view.m_RectTransform),
            new ImportantReference("m_InnerCanvasGroup.transform", view.m_InnerCanvasGroup?.transform),
            new ImportantReference("m_HealthBlockView.transform", view.m_HealthBlockView?.transform),
            new ImportantReference("m_NameBlockPCView.transform", view.m_NameBlockPCView?.transform),
            new ImportantReference("m_HitChanceBlockPCView.transform", view.m_HitChanceBlockPCView?.transform),
            new ImportantReference("m_CoverBlockPCView.transform", view.m_CoverBlockPCView?.transform),
            new ImportantReference("m_OvertipTargetDefensesPCView.transform", view.m_OvertipTargetDefensesPCView?.transform),
            new ImportantReference("m_OvertipTargetNameView.transform", view.m_OvertipTargetNameView?.transform),
            new ImportantReference("m_OvertipAimView.transform", view.m_OvertipAimView?.transform),
            new ImportantReference("m_CombatTextBlockPCView.transform", view.m_CombatTextBlockPCView?.transform),
            new ImportantReference("m_BarkBlockPCView.transform", view.m_BarkBlockPCView?.transform),
            new ImportantReference("m_UnitBuffPartPCView.transform", view.m_UnitBuffPartPCView?.transform),
            new ImportantReference("m_UnitBuffsCanvasGroup.transform", view.m_UnitBuffsCanvasGroup?.transform)
        };

        var builder = new StringBuilder(32768);
        builder.AppendLine("Unit overtip hierarchy probe:");
        builder.Append("  Runtime view type: ").AppendLine(view.GetType().FullName);
        builder.Append("  View component instance ID: ").AppendLine(view.GetInstanceID().ToString(CultureInfo.InvariantCulture));
        builder.Append("  Root GameObject name: ").AppendLine(view.gameObject.name);
        builder.Append("  Bound unit name: ").AppendLine(viewModel.UnitUIWrapper.Name ?? "<null>");
        builder.Append("  Stable unit unique ID: ").AppendLine(viewModel.UnitUIWrapper.UniqueId ?? "<null>");
        builder.Append("  Visibility: ").AppendLine(view.Visibility.Value.ToString());
        builder.Append("  Unit is in combat: ").AppendLine(unitInCombat.ToString());
        builder.Append("  Turn-based combat is active: ").AppendLine(turnBasedCombatActive.ToString());
        builder.AppendLine();
        builder.AppendLine("Important serialized references:");
        for (int index = 0; index < references.Length; index++) {
            builder.Append("  ").Append(references[index].Name).Append(": ")
                .AppendLine(GetRelativePath(root, references[index].Transform));
        }

        builder.AppendLine();
        builder.AppendLine("Bounded hierarchy tree:");
        int nodeCount = 0;
        bool truncated = false;
        AppendTransform(builder, root, root, references, 0, ref nodeCount, ref truncated);
        builder.Append("Hierarchy nodes reported: ").Append(nodeCount.ToString(CultureInfo.InvariantCulture))
            .Append(" (maximum ").Append(MaximumNodes.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
        builder.Append("Hierarchy maximum depth: ").AppendLine(MaximumDepth.ToString(CultureInfo.InvariantCulture));
        builder.Append("Hierarchy truncated: ").AppendLine(truncated.ToString());
        return builder.ToString();
    }

    private static void AppendTransform(
        StringBuilder builder,
        Transform root,
        Transform current,
        ImportantReference[] references,
        int depth,
        ref int nodeCount,
        ref bool truncated) {
        if (nodeCount >= MaximumNodes) {
            truncated = true;
            return;
        }

        nodeCount++;
        builder.Append("  - path=").Append(GetRelativePath(root, current))
            .Append("; depth=").Append(depth.ToString(CultureInfo.InvariantCulture))
            .Append("; siblingIndex=").Append(current.GetSiblingIndex().ToString(CultureInfo.InvariantCulture))
            .Append("; activeSelf=").Append(current.gameObject.activeSelf)
            .Append("; activeInHierarchy=").Append(current.gameObject.activeInHierarchy)
            .Append("; localScale=").Append(FormatVector3(current.localScale));
        AppendReferenceAnnotations(builder, current, references);
        builder.AppendLine();

        if (current is RectTransform rectTransform) {
            builder.Append("      RectTransform: anchorMin=").Append(FormatVector2(rectTransform.anchorMin))
                .Append("; anchorMax=").Append(FormatVector2(rectTransform.anchorMax))
                .Append("; pivot=").Append(FormatVector2(rectTransform.pivot))
                .Append("; anchoredPosition=").Append(FormatVector2(rectTransform.anchoredPosition))
                .Append("; sizeDelta=").AppendLine(FormatVector2(rectTransform.sizeDelta));
        }

        Component[] components = current.GetComponents<Component>();
        for (int index = 0; index < components.Length; index++) {
            AppendComponent(builder, components[index]);
        }

        int childCount = current.childCount;
        if (depth >= MaximumDepth) {
            if (childCount > 0) {
                truncated = true;
            }
            return;
        }

        for (int index = 0; index < childCount; index++) {
            if (nodeCount >= MaximumNodes) {
                truncated = true;
                return;
            }
            AppendTransform(builder, root, current.GetChild(index), references, depth + 1, ref nodeCount, ref truncated);
        }
    }

    private static void AppendComponent(StringBuilder builder, Component component) {
        switch (component) {
            case CanvasGroup canvasGroup:
                builder.Append("      CanvasGroup: alpha=").Append(FormatFloat(canvasGroup.alpha))
                    .Append("; interactable=").Append(canvasGroup.interactable)
                    .Append("; blocksRaycasts=").Append(canvasGroup.blocksRaycasts)
                    .Append("; ignoreParentGroups=").AppendLine(canvasGroup.ignoreParentGroups.ToString());
                break;
            case HorizontalLayoutGroup horizontal:
                AppendHorizontalOrVerticalLayout(builder, "HorizontalLayoutGroup", horizontal, horizontal.spacing);
                break;
            case VerticalLayoutGroup vertical:
                AppendHorizontalOrVerticalLayout(builder, "VerticalLayoutGroup", vertical, vertical.spacing);
                break;
            case GridLayoutGroup grid:
                builder.Append("      GridLayoutGroup: enabled=").Append(grid.enabled)
                    .Append("; cellSize=").Append(FormatVector2(grid.cellSize))
                    .Append("; spacing=").Append(FormatVector2(grid.spacing))
                    .Append("; startCorner=").Append(grid.startCorner)
                    .Append("; startAxis=").Append(grid.startAxis)
                    .Append("; childAlignment=").Append(grid.childAlignment)
                    .Append("; constraint=").Append(grid.constraint)
                    .Append("; constraintCount=").AppendLine(grid.constraintCount.ToString(CultureInfo.InvariantCulture));
                break;
            case ContentSizeFitter fitter:
                builder.Append("      ContentSizeFitter: enabled=").Append(fitter.enabled)
                    .Append("; horizontalFit=").Append(fitter.horizontalFit)
                    .Append("; verticalFit=").AppendLine(fitter.verticalFit.ToString());
                break;
            case LayoutElement layoutElement:
                builder.Append("      LayoutElement: enabled=").Append(layoutElement.enabled)
                    .Append("; ignoreLayout=").Append(layoutElement.ignoreLayout)
                    .Append("; minWidth=").Append(FormatFloat(layoutElement.minWidth))
                    .Append("; minHeight=").Append(FormatFloat(layoutElement.minHeight))
                    .Append("; preferredWidth=").Append(FormatFloat(layoutElement.preferredWidth))
                    .Append("; preferredHeight=").Append(FormatFloat(layoutElement.preferredHeight))
                    .Append("; flexibleWidth=").Append(FormatFloat(layoutElement.flexibleWidth))
                    .Append("; flexibleHeight=").Append(FormatFloat(layoutElement.flexibleHeight))
                    .Append("; layoutPriority=").AppendLine(layoutElement.layoutPriority.ToString(CultureInfo.InvariantCulture));
                break;
            case Mask mask:
                builder.Append("      Mask: enabled=").Append(mask.enabled)
                    .Append("; showMaskGraphic=").AppendLine(mask.showMaskGraphic.ToString());
                break;
            case RectMask2D rectMask:
                builder.Append("      RectMask2D: enabled=").AppendLine(rectMask.enabled.ToString());
                break;
            case Graphic graphic:
                builder.Append("      Graphic: type=").Append(graphic.GetType().FullName)
                    .Append("; raycastTarget=").Append(graphic.raycastTarget)
                    .Append("; enabled=").Append(graphic.enabled)
                    .Append("; colorAlpha=").AppendLine(FormatFloat(graphic.color.a));
                break;
        }
    }

    private static void AppendHorizontalOrVerticalLayout(
        StringBuilder builder,
        string typeName,
        HorizontalOrVerticalLayoutGroup layout,
        float spacing) {
        builder.Append("      ").Append(typeName).Append(": enabled=").Append(layout.enabled)
            .Append("; spacing=").Append(FormatFloat(spacing))
            .Append("; childAlignment=").Append(layout.childAlignment)
            .Append("; childControlWidth=").Append(layout.childControlWidth)
            .Append("; childControlHeight=").Append(layout.childControlHeight)
            .Append("; childForceExpandWidth=").Append(layout.childForceExpandWidth)
            .Append("; childForceExpandHeight=").AppendLine(layout.childForceExpandHeight.ToString());
    }

    private static void AppendReferenceAnnotations(
        StringBuilder builder,
        Transform transform,
        ImportantReference[] references) {
        bool wroteHeader = false;
        for (int index = 0; index < references.Length; index++) {
            if (references[index].Transform != transform) {
                continue;
            }

            builder.Append(wroteHeader ? ", " : "; refs=");
            builder.Append(references[index].Name);
            wroteHeader = true;
        }
    }

    private static string GetRelativePath(Transform root, Transform transform) {
        if (transform == null) {
            return "<null>";
        }
        if (transform == root) {
            return "<root>";
        }

        string path = string.Empty;
        Transform current = transform;
        while (current != null && current != root) {
            path = path.Length == 0 ? current.name : current.name + "/" + path;
            current = current.parent;
        }
        return current == root ? path : "<outside-root>/" + path;
    }

    private static string FormatVector2(Vector2 value) {
        return $"({FormatFloat(value.x)}, {FormatFloat(value.y)})";
    }

    private static string FormatVector3(Vector3 value) {
        return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)})";
    }

    private static string FormatFloat(float value) {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private readonly struct ImportantReference {
        internal readonly string Name;
        internal readonly Transform Transform;

        internal ImportantReference(string name, Transform transform) {
            Name = name;
            Transform = transform;
        }
    }
}
