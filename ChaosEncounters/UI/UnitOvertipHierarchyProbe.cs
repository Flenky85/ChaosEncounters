using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ChaosEncounters.Logging;
using HarmonyLib;
using Kingmaker;
using Kingmaker.Code.UI.MVVM.View.Other;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit;
using Kingmaker.Code.UI.MVVM.View.Overtips.Unit.PC;
using Kingmaker.Code.UI.MVVM.View.Party.PC;
using Kingmaker.Code.UI.MVVM.VM.Overtips.Unit;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.GameModes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChaosEncounters.UI;

internal static class UnitOvertipHierarchyProbe {
    private const string HarmonyId = "ChaosEncounters.UnitOvertipHierarchyProbe";
    private const int MaximumDepth = 16;
    private const int MaximumNodes = 256;
    private const int MaximumGraphics = 128;

    private static readonly HashSet<OvertipUnitPCView> BoundViews = new();
    private static readonly List<OvertipUnitPCView> SnapshotViews = new();
    private static readonly List<OvertipUnitPCView> InvalidViews = new();
    private static readonly List<Graphic> NodeGraphics = new();
    private static readonly List<Renderer> NodeRenderers = new();
    private static bool Initialized;
    private static int SnapshotNumber;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        var harmony = new Harmony(HarmonyId);
        harmony.CreateClassProcessor(typeof(UnitOvertipBindPatch)).Patch();
        harmony.CreateClassProcessor(typeof(UnitOvertipDestroyPatch)).Patch();
        Initialized = true;
    }

    internal static void CaptureMarkerSnapshot() {
        try {
            int snapshotNumber = ++SnapshotNumber;
            int registeredBeforeCleanup = BoundViews.Count;
            CollectValidViews();

            Game game = Game.Instance;
            bool turnBasedCombatActive = game?.TurnController?.TurnBasedModeActive == true;
            bool terrestrialMode = game != null &&
                game.CurrentMode != GameModeType.SpaceCombat &&
                game.CurrentMode != GameModeType.StarSystem;
            bool terrestrialTurnBasedCombatActive = terrestrialMode && turnBasedCombatActive;

            var builder = new StringBuilder(32768);
            builder.AppendLine("============================================================");
            builder.Append("Unit marker snapshot #").AppendLine(snapshotNumber.ToString(CultureInfo.InvariantCulture));
            builder.Append("Timestamp: ").AppendLine(DateTimeOffset.Now.ToString("O"));
            builder.Append("Registered views before invalid-entry cleanup: ")
                .AppendLine(registeredBeforeCleanup.ToString(CultureInfo.InvariantCulture));
            builder.Append("Valid currently bound views inspected: ")
                .AppendLine(SnapshotViews.Count.ToString(CultureInfo.InvariantCulture));
            builder.Append("Terrestrial turn-based combat active: ")
                .AppendLine(terrestrialTurnBasedCombatActive.ToString());

            for (int index = 0; index < SnapshotViews.Count; index++) {
                OvertipUnitPCView view = SnapshotViews[index];
                try {
                    OvertipEntityUnitVM viewModel = view.GetViewModel() as OvertipEntityUnitVM;
                    if (viewModel?.Unit == null) {
                        builder.AppendLine();
                        builder.Append("View inspection error: instance ")
                            .Append(view == null ? "<destroyed>" : view.GetInstanceID().ToString(CultureInfo.InvariantCulture))
                            .AppendLine(" no longer has a bound unit view model.");
                        continue;
                    }

                    AppendUnitSnapshot(builder, view, viewModel);
                } catch (Exception exception) {
                    builder.AppendLine();
                    builder.Append("View inspection error: instance ")
                        .Append(view == null ? "<destroyed>" : view.GetInstanceID().ToString(CultureInfo.InvariantCulture))
                        .Append("; ").Append(exception.GetType().Name).Append(": ").AppendLine(exception.Message);
                }
            }

            builder.AppendLine("============================================================");
            UnitOvertipHierarchyLogger.WriteDiagnostic(builder.ToString());
        } catch (Exception exception) {
            UnitOvertipHierarchyLogger.WriteError(
                $"Unit marker snapshot failed: {exception.GetType().Name}: {exception.Message}");
        } finally {
            SnapshotViews.Clear();
            InvalidViews.Clear();
            NodeGraphics.Clear();
            NodeRenderers.Clear();
        }
    }

    internal static void Register(OvertipUnitView view) {
        if (view is OvertipUnitPCView pcView && pcView.IsBinded) {
            BoundViews.Add(pcView);
        }
    }

    internal static void Unregister(OvertipUnitView view) {
        if (view is OvertipUnitPCView pcView) {
            BoundViews.Remove(pcView);
        }
    }

    private static void CollectValidViews() {
        SnapshotViews.Clear();
        InvalidViews.Clear();

        foreach (OvertipUnitPCView view in BoundViews) {
            try {
                if (view != null && view.IsBinded &&
                    view.GetViewModel() is OvertipEntityUnitVM viewModel && viewModel.Unit != null) {
                    SnapshotViews.Add(view);
                    continue;
                }
            } catch {
                // A destroyed or malformed pooled view is simply removed from the diagnostic registry.
            }

            InvalidViews.Add(view);
        }

        for (int index = 0; index < InvalidViews.Count; index++) {
            BoundViews.Remove(InvalidViews[index]);
        }
    }

    private static void AppendUnitSnapshot(
        StringBuilder builder,
        OvertipUnitPCView view,
        OvertipEntityUnitVM viewModel) {
        Transform root = view.transform;
        CanvasGroup rootCanvasGroup = view.GetComponent<CanvasGroup>();
        RectTransform rootRect = root as RectTransform;

        builder.AppendLine();
        builder.AppendLine("Unit:");
        builder.Append("  Bound unit name: ").AppendLine(viewModel.UnitUIWrapper.Name ?? "<null>");
        builder.Append("  Stable unit unique ID: ").AppendLine(viewModel.UnitUIWrapper.UniqueId ?? "<null>");
        builder.Append("  Runtime view type: ").AppendLine(view.GetType().FullName);
        builder.Append("  View instance ID: ").AppendLine(view.GetInstanceID().ToString(CultureInfo.InvariantCulture));
        builder.Append("  Root GameObject name: ").AppendLine(view.gameObject.name);
        builder.Append("  Root activeSelf: ").AppendLine(view.gameObject.activeSelf.ToString());
        builder.Append("  Root activeInHierarchy: ").AppendLine(view.gameObject.activeInHierarchy.ToString());
        builder.Append("  Overtip visibility: ").AppendLine(view.Visibility.Value.ToString());
        builder.Append("  Unit is in combat: ").AppendLine(viewModel.Unit.IsInCombat.ToString());
        builder.Append("  Unit allegiance: ").AppendLine(GetAllegiance(viewModel));

        builder.AppendLine("Root overtip:");
        AppendCanvasGroup(builder, "  CanvasGroup", rootCanvasGroup);
        builder.Append("  RectTransform anchoredPosition: ")
            .AppendLine(rootRect == null ? "<null>" : FormatVector2(rootRect.anchoredPosition));

        builder.AppendLine("m_RectTransform (ScaledGroup):");
        AppendTransformState(builder, root, view.m_RectTransform, "  ");
        AppendCanvasGroup(builder, "  m_InnerCanvasGroup", view.m_InnerCanvasGroup);

        builder.AppendLine("Defense marker state:");
        AppendMarkerState(
            builder,
            root,
            "m_CoverBlockPCView",
            view.m_CoverBlockPCView?.transform,
            view.m_CoverBlockPCView?.m_CanvasGroup);
        if (view.m_CoverBlockPCView != null) {
            AppendImageState(builder, "    Direct m_Icon", view.m_CoverBlockPCView.m_Icon);
        }
        if (view.m_OvertipTargetDefensesPCView == null) {
            builder.AppendLine("  m_OvertipTargetDefensesPCView: <null>");
        } else {
            AppendMarkerState(
                builder, root, "ShieldPlace",
                view.m_OvertipTargetDefensesPCView.m_ShieldBlock?.transform,
                view.m_OvertipTargetDefensesPCView.m_ShieldBlockCanvasGroup);
            AppendMarkerState(
                builder, root, "CoverPlace",
                view.m_OvertipTargetDefensesPCView.m_Cover?.transform,
                view.m_OvertipTargetDefensesPCView.m_CoverBlockCanvasGroup);
            AppendMarkerState(
                builder, root, "DodgePlace",
                view.m_OvertipTargetDefensesPCView.m_Dodge?.transform,
                view.m_OvertipTargetDefensesPCView.m_DodgeBlockCanvasGroup);
            AppendMarkerState(
                builder, root, "ParryPlace",
                view.m_OvertipTargetDefensesPCView.m_Parry?.transform,
                view.m_OvertipTargetDefensesPCView.m_ParryBlockCanvasGroup);
        }

        int activeBuffIcons = AppendBuffBlock(builder, root, view.m_UnitBuffPartPCView, view.m_UnitBuffsCanvasGroup);

        builder.AppendLine("Active graphic inventory inside overtip:");
        int nodesVisited = 0;
        int graphicsReported = 0;
        int otherActiveGraphics = 0;
        bool nodesTruncated = false;
        bool graphicsTruncated = false;
        AppendActiveInventory(
            builder,
            root,
            root,
            view.m_OvertipTargetDefensesPCView?.transform,
            view.m_CoverBlockPCView?.transform,
            view.m_UnitBuffPartPCView?.transform,
            0,
            ref nodesVisited,
            ref graphicsReported,
            ref otherActiveGraphics,
            ref nodesTruncated,
            ref graphicsTruncated);
        builder.Append("  Nodes visited: ").Append(nodesVisited.ToString(CultureInfo.InvariantCulture))
            .Append("/").AppendLine(MaximumNodes.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Graphics reported: ").Append(graphicsReported.ToString(CultureInfo.InvariantCulture))
            .Append("/").AppendLine(MaximumGraphics.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Maximum depth: ").AppendLine(MaximumDepth.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Node traversal truncated: ").AppendLine(nodesTruncated.ToString());
        builder.Append("  Graphic reporting truncated: ").AppendLine(graphicsTruncated.ToString());

        bool shieldActive = view.m_OvertipTargetDefensesPCView?.m_ShieldBlock != null &&
            view.m_OvertipTargetDefensesPCView.m_ShieldBlock.activeInHierarchy;
        builder.AppendLine("Interpretation summary:");
        builder.Append("  ShieldPlace active: ").AppendLine(shieldActive.ToString());
        builder.Append("  Active buff icons: ").AppendLine(activeBuffIcons.ToString(CultureInfo.InvariantCulture));
        builder.Append("  Other active overtip graphics: ").AppendLine(otherActiveGraphics.ToString(CultureInfo.InvariantCulture));
    }

    private static string GetAllegiance(OvertipEntityUnitVM viewModel) {
        if (viewModel.Unit is BaseUnitEntity baseUnit && baseUnit.IsInPlayerParty) {
            return "Player character";
        }
        if (viewModel.Unit.IsPlayerEnemy) {
            return "Enemy";
        }
        if (viewModel.Unit.IsPlayerFaction || viewModel.Unit.IsHelpingPlayerFaction) {
            return "Ally";
        }
        if (viewModel.Unit.IsNeutral) {
            return "Neutral";
        }
        return "Neutral";
    }

    private static void AppendTransformState(
        StringBuilder builder,
        Transform root,
        Transform transform,
        string indent) {
        if (transform == null) {
            builder.Append(indent).AppendLine("<null>");
            return;
        }

        RectTransform rect = transform as RectTransform;
        builder.Append(indent).Append("Path: ").AppendLine(GetRelativePath(root, transform));
        builder.Append(indent).Append("activeSelf: ").AppendLine(transform.gameObject.activeSelf.ToString());
        builder.Append(indent).Append("activeInHierarchy: ").AppendLine(transform.gameObject.activeInHierarchy.ToString());
        builder.Append(indent).Append("localScale: ").AppendLine(FormatVector3(transform.localScale));
        builder.Append(indent).Append("anchoredPosition: ")
            .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.anchoredPosition));
        builder.Append(indent).Append("sizeDelta: ")
            .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.sizeDelta));
    }

    private static void AppendMarkerState(
        StringBuilder builder,
        Transform root,
        string label,
        Transform marker,
        CanvasGroup serializedCanvasGroup) {
        builder.Append("  ").Append(label).AppendLine(":");
        if (marker == null) {
            builder.AppendLine("    <null>");
            return;
        }

        RectTransform rect = marker as RectTransform;
        Image image = marker.GetComponent<Image>();
        Transform iconTransform = marker.Find("Icon");
        Image iconImage = iconTransform?.GetComponent<Image>();

        builder.Append("    Path: ").AppendLine(GetRelativePath(root, marker));
        builder.Append("    activeSelf: ").AppendLine(marker.gameObject.activeSelf.ToString());
        builder.Append("    activeInHierarchy: ").AppendLine(marker.gameObject.activeInHierarchy.ToString());
        builder.Append("    anchoredPosition: ")
            .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.anchoredPosition));
        builder.Append("    sizeDelta: ")
            .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.sizeDelta));
        builder.Append("    CanvasGroup alpha: ")
            .AppendLine(serializedCanvasGroup == null ? "<null>" : FormatFloat(serializedCanvasGroup.alpha));
        builder.Append("    CanvasGroup blocksRaycasts: ")
            .AppendLine(serializedCanvasGroup == null ? "<null>" : serializedCanvasGroup.blocksRaycasts.ToString());
        AppendImageState(builder, "    Root Image", image);
        if (iconTransform == null) {
            builder.AppendLine("    Child Icon: <null>");
        } else {
            builder.Append("    Child Icon activeSelf: ").AppendLine(iconTransform.gameObject.activeSelf.ToString());
            builder.Append("    Child Icon activeInHierarchy: ").AppendLine(iconTransform.gameObject.activeInHierarchy.ToString());
            AppendImageState(builder, "    Child Icon Image", iconImage);
        }
    }

    private static int AppendBuffBlock(
        StringBuilder builder,
        Transform root,
        UnitBuffPartPCView buffPart,
        CanvasGroup buffsCanvasGroup) {
        builder.AppendLine("Buff block state:");
        if (buffPart == null) {
            builder.AppendLine("  m_UnitBuffPartPCView: <null>");
            AppendCanvasGroup(builder, "  m_UnitBuffsCanvasGroup", buffsCanvasGroup);
            return 0;
        }

        Transform transform = buffPart.transform;
        builder.Append("  Path: ").AppendLine(GetRelativePath(root, transform));
        builder.Append("  activeSelf: ").AppendLine(buffPart.gameObject.activeSelf.ToString());
        builder.Append("  activeInHierarchy: ").AppendLine(buffPart.gameObject.activeInHierarchy.ToString());
        builder.Append("  Direct child count: ").AppendLine(transform.childCount.ToString(CultureInfo.InvariantCulture));
        if (buffsCanvasGroup == null) {
            builder.AppendLine("  m_UnitBuffsCanvasGroup: <null>");
        } else {
            builder.Append("  m_UnitBuffsCanvasGroup path: ")
                .AppendLine(GetRelativePath(root, buffsCanvasGroup.transform));
            builder.Append("  m_UnitBuffsCanvasGroup activeSelf: ")
                .AppendLine(buffsCanvasGroup.gameObject.activeSelf.ToString());
            builder.Append("  m_UnitBuffsCanvasGroup activeInHierarchy: ")
                .AppendLine(buffsCanvasGroup.gameObject.activeInHierarchy.ToString());
            AppendCanvasGroup(builder, "  m_UnitBuffsCanvasGroup", buffsCanvasGroup);
        }

        int activeBuffIcons = 0;
        activeBuffIcons += AppendDirectBuffChildren(builder, root, "Main container", buffPart.m_MainContainer);
        activeBuffIcons += AppendDirectBuffChildren(builder, root, "Additional container", buffPart.m_AdditionalContainer);
        activeBuffIcons += AppendDirectBuffChildren(builder, root, "Important place 1", buffPart.m_ImportantBuffViewPlace1);
        activeBuffIcons += AppendDirectBuffChildren(builder, root, "Important place 2", buffPart.m_ImportantBuffViewPlace2);
        activeBuffIcons += AppendDirectBuffChildren(builder, root, "Important place 3", buffPart.m_ImportantBuffViewPlace3);
        return activeBuffIcons;
    }

    private static int AppendDirectBuffChildren(
        StringBuilder builder,
        Transform root,
        string containerLabel,
        Transform container) {
        if (container == null) {
            return 0;
        }

        int count = 0;
        int childCount = container.childCount;
        for (int index = 0; index < childCount; index++) {
            Transform child = container.GetChild(index);
            BuffPCView buffView = child.GetComponent<BuffPCView>();
            if (buffView == null || !child.gameObject.activeSelf) {
                continue;
            }

            count++;
            RectTransform rect = child as RectTransform;
            Image image = buffView.m_Icon;
            builder.Append("  Active buff child (").Append(containerLabel).AppendLine("):");
            builder.Append("    Path: ").AppendLine(GetRelativePath(root, child));
            builder.Append("    Sibling index: ").AppendLine(child.GetSiblingIndex().ToString(CultureInfo.InvariantCulture));
            builder.Append("    GameObject name: ").AppendLine(child.gameObject.name);
            builder.Append("    activeSelf: ").AppendLine(child.gameObject.activeSelf.ToString());
            builder.Append("    activeInHierarchy: ").AppendLine(child.gameObject.activeInHierarchy.ToString());
            builder.Append("    anchoredPosition: ")
                .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.anchoredPosition));
            builder.Append("    sizeDelta: ")
                .AppendLine(rect == null ? "<not RectTransform>" : FormatVector2(rect.sizeDelta));
            AppendImageState(builder, "    Main Image", image);
            builder.Append("    Visible rank text: ").AppendLine(GetVisibleText(buffView.m_Rank));
            builder.Append("    Visible damage text: ").AppendLine(GetVisibleText(buffView.m_Damage));
        }
        return count;
    }

    private static void AppendActiveInventory(
        StringBuilder builder,
        Transform root,
        Transform current,
        Transform defensesRoot,
        Transform coverRoot,
        Transform buffsRoot,
        int depth,
        ref int nodesVisited,
        ref int graphicsReported,
        ref int otherActiveGraphics,
        ref bool nodesTruncated,
        ref bool graphicsTruncated) {
        if (nodesVisited >= MaximumNodes) {
            nodesTruncated = true;
            return;
        }

        nodesVisited++;
        if (current.gameObject.activeInHierarchy) {
            NodeGraphics.Clear();
            current.GetComponents(NodeGraphics);
            for (int index = 0; index < NodeGraphics.Count; index++) {
                Graphic graphic = NodeGraphics[index];
                if (graphic == null || !graphic.enabled || graphic.color.a <= 0f) {
                    continue;
                }

                if (!IsWithin(current, defensesRoot) && !IsWithin(current, coverRoot) && !IsWithin(current, buffsRoot)) {
                    otherActiveGraphics++;
                }
                if (graphicsReported >= MaximumGraphics) {
                    graphicsTruncated = true;
                    continue;
                }

                graphicsReported++;
                RectTransform rect = graphic.rectTransform;
                builder.Append("  Graphic: path=").Append(GetRelativePath(root, current))
                    .Append("; type=").Append(graphic.GetType().FullName)
                    .Append("; sprite=").Append(graphic is Image image ? GetObjectName(image.sprite) : "<not Image>")
                    .Append("; color=").Append(FormatColor(graphic.color))
                    .Append("; alpha=").Append(FormatFloat(graphic.color.a))
                    .Append("; material=").Append(GetObjectName(graphic.material))
                    .Append("; anchoredPosition=").Append(FormatVector2(rect.anchoredPosition))
                    .Append("; sizeDelta=").Append(FormatVector2(rect.sizeDelta))
                    .Append("; localScale=").Append(FormatVector3(current.localScale))
                    .Append("; raycastTarget=").AppendLine(graphic.raycastTarget.ToString());
            }

            NodeRenderers.Clear();
            current.GetComponents(NodeRenderers);
            for (int index = 0; index < NodeRenderers.Count; index++) {
                Renderer renderer = NodeRenderers[index];
                if (renderer == null || !renderer.enabled ||
                    (renderer is not SpriteRenderer && renderer is not MeshRenderer &&
                     renderer is not SkinnedMeshRenderer && renderer is not ParticleSystemRenderer)) {
                    continue;
                }

                builder.Append("  Renderer: path=").Append(GetRelativePath(root, current))
                    .Append("; type=").Append(renderer.GetType().FullName)
                    .Append("; enabled=").Append(renderer.enabled)
                    .Append("; materials=");
                AppendMaterialNames(builder, renderer.sharedMaterials);
                builder.Append("; sortingLayer=").Append(renderer.sortingLayerName)
                    .Append("; sortingOrder=").AppendLine(renderer.sortingOrder.ToString(CultureInfo.InvariantCulture));
            }
        }

        int childCount = current.childCount;
        if (depth >= MaximumDepth) {
            if (childCount > 0) {
                nodesTruncated = true;
            }
            return;
        }

        for (int index = 0; index < childCount; index++) {
            if (nodesVisited >= MaximumNodes) {
                nodesTruncated = true;
                return;
            }
            AppendActiveInventory(
                builder, root, current.GetChild(index), defensesRoot, coverRoot, buffsRoot, depth + 1,
                ref nodesVisited, ref graphicsReported, ref otherActiveGraphics,
                ref nodesTruncated, ref graphicsTruncated);
        }
    }

    private static void AppendCanvasGroup(StringBuilder builder, string label, CanvasGroup canvasGroup) {
        if (canvasGroup == null) {
            builder.Append(label).AppendLine(": <null>");
            return;
        }
        builder.Append(label).Append(" alpha: ").AppendLine(FormatFloat(canvasGroup.alpha));
        builder.Append(label).Append(" blocksRaycasts: ").AppendLine(canvasGroup.blocksRaycasts.ToString());
    }

    private static void AppendImageState(StringBuilder builder, string label, Image image) {
        if (image == null) {
            builder.Append(label).AppendLine(": <null>");
            return;
        }
        builder.Append(label).Append(" enabled: ").AppendLine(image.enabled.ToString());
        builder.Append(label).Append(" sprite: ").AppendLine(GetObjectName(image.sprite));
        builder.Append(label).Append(" color: ").AppendLine(FormatColor(image.color));
        builder.Append(label).Append(" color alpha: ").AppendLine(FormatFloat(image.color.a));
        builder.Append(label).Append(" material: ").AppendLine(GetObjectName(image.material));
        builder.Append(label).Append(" raycastTarget: ").AppendLine(image.raycastTarget.ToString());
    }

    private static void AppendMaterialNames(StringBuilder builder, Material[] materials) {
        if (materials == null || materials.Length == 0) {
            builder.Append("<none>");
            return;
        }
        for (int index = 0; index < materials.Length; index++) {
            if (index > 0) {
                builder.Append(", ");
            }
            builder.Append(GetObjectName(materials[index]));
        }
    }

    private static string GetVisibleText(TextMeshProUGUI text) {
        return text != null && text.enabled && text.gameObject.activeInHierarchy
            ? text.text ?? "<null>"
            : "<hidden>";
    }

    private static bool IsWithin(Transform transform, Transform possibleRoot) {
        return possibleRoot != null && (transform == possibleRoot || transform.IsChildOf(possibleRoot));
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

    private static string GetObjectName(UnityEngine.Object value) {
        return value == null ? "<null>" : value.name;
    }

    private static string FormatColor(Color value) {
        return $"({FormatFloat(value.r)}, {FormatFloat(value.g)}, {FormatFloat(value.b)}, {FormatFloat(value.a)})";
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
}

[HarmonyPatch(typeof(OvertipUnitView), nameof(OvertipUnitView.BindViewImplementation))]
internal static class UnitOvertipBindPatch {
    [HarmonyPostfix]
    private static void Postfix(OvertipUnitView __instance) {
        UnitOvertipHierarchyProbe.Register(__instance);
    }
}

[HarmonyPatch(typeof(OvertipUnitView), nameof(OvertipUnitView.DestroyViewImplementation))]
internal static class UnitOvertipDestroyPatch {
    [HarmonyPrefix]
    private static void Prefix(OvertipUnitView __instance) {
        UnitOvertipHierarchyProbe.Unregister(__instance);
    }
}
