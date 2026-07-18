using System.Globalization;
using System.Text;
using ChaosEncounters.Logging;
using HarmonyLib;
using Kingmaker.Code.UI.MVVM.View.SurfaceCombat.PC;
using UnityEngine;
using UnityEngine.UI;

namespace ChaosEncounters.UI;

internal static class SurfaceHudHierarchyProbe {
    private const string HarmonyId = "ChaosEncounters.SurfaceHudHierarchyProbe";
    private static bool Initialized;

    internal static void Initialize() {
        if (Initialized) {
            return;
        }

        new Harmony(HarmonyId)
            .CreateClassProcessor(typeof(SurfaceHudHierarchyPatch))
            .Patch();
        Initialized = true;
    }
}

[HarmonyPatch(typeof(SurfaceHUDPCView), nameof(SurfaceHUDPCView.BindViewImplementation))]
internal static class SurfaceHudHierarchyPatch {
    [HarmonyPostfix]
    private static void Postfix(SurfaceHUDPCView __instance) {
        if (__instance == null ||
            __instance.GetComponent<SurfaceHudDumpMarker>() != null) {
            return;
        }

        try {
            string hierarchyDump = SurfaceHudHierarchyDumper.CreateDump(__instance);
            if (ModFileLogger.WriteSurfaceHudBlock(hierarchyDump)) {
                __instance.gameObject.AddComponent<SurfaceHudDumpMarker>();
            }
        } catch (Exception exception) {
            ModFileLogger.WriteSurfaceHudBlock(
                "========== SURFACE HUD HIERARCHY DUMP FAILED ==========\n" +
                $"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n" +
                $"HUD instance ID: {__instance.GetInstanceID()}\n" +
                $"Exception: {exception}");
        }
    }
}

internal sealed class SurfaceHudDumpMarker : MonoBehaviour {
}

internal static class SurfaceHudHierarchyDumper {
    private static readonly string[] CandidateTerms = {
        "action", "party", "portrait", "combatlog", "combat log", "currentunit", "current unit",
        "initiative", "endturn", "end turn", "lower", "bottom", "left", "buff", "status"
    };

    internal static string CreateDump(SurfaceHUDPCView hudView) {
        var nodeOutput = new StringBuilder(64 * 1024);
        var candidates = new List<CandidateInfo>();
        var worldCorners = new Vector3[4];
        int nodeCount = 0;
        int maximumDepth = 0;

        Traverse(
            hudView.transform,
            string.Empty,
            0,
            nodeOutput,
            candidates,
            worldCorners,
            ref nodeCount,
            ref maximumDepth);

        var output = new StringBuilder(nodeOutput.Length + 4096);
        output.AppendLine("========== SURFACE HUD HIERARCHY DUMP BEGIN ==========");
        output.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        output.AppendLine($"HUD instance ID: {hudView.GetInstanceID()}");
        output.AppendLine($"Root object name: {hudView.gameObject.name}");
        output.AppendLine($"Screen resolution: {Screen.width} x {Screen.height}");
        output.AppendLine($"Screen safe area: {FormatRect(Screen.safeArea)}");
        output.AppendLine($"Hierarchy node count: {nodeCount}");
        output.AppendLine($"Maximum recorded depth: {maximumDepth}");
        output.AppendLine($"Root activeSelf: {hudView.gameObject.activeSelf}");
        output.AppendLine($"Root activeInHierarchy: {hudView.gameObject.activeInHierarchy}");
        output.AppendLine();
        output.Append(nodeOutput);
        AppendCandidateSummary(output, candidates);
        output.AppendLine("========== SURFACE HUD HIERARCHY DUMP END ==========");
        return output.ToString();
    }

    private static void Traverse(
        Transform transform,
        string parentPath,
        int depth,
        StringBuilder output,
        List<CandidateInfo> candidates,
        Vector3[] worldCorners,
        ref int nodeCount,
        ref int maximumDepth) {
        nodeCount++;
        if (depth > maximumDepth) {
            maximumDepth = depth;
        }

        string path = depth == 0
            ? "."
            : (parentPath.Length == 0 ? transform.name : parentPath + "/" + transform.name);
        Component[] components = transform.GetComponents<Component>();

        output.AppendLine($"Node: {path}");
        output.AppendLine($"  Name: {transform.name}");
        output.AppendLine($"  Depth: {depth}");
        output.AppendLine($"  SiblingIndex: {transform.GetSiblingIndex()}");
        output.AppendLine($"  InstanceID: {transform.GetInstanceID()}");
        output.AppendLine($"  ActiveSelf: {transform.gameObject.activeSelf}");
        output.AppendLine($"  ActiveInHierarchy: {transform.gameObject.activeInHierarchy}");
        output.AppendLine($"  Layer: {transform.gameObject.layer}");
        output.AppendLine($"  Tag: {GetSafeTag(transform.gameObject)}");
        output.AppendLine($"  TransformType: {transform.GetType().FullName}");
        output.AppendLine($"  LocalPosition: {FormatVector3(transform.localPosition)}");
        output.AppendLine($"  LocalRotation: {FormatQuaternion(transform.localRotation)}");
        output.AppendLine($"  LocalScale: {FormatVector3(transform.localScale)}");

        string screenRectangle = "not applicable (not a RectTransform)";
        if (transform is RectTransform rectTransform) {
            output.AppendLine($"  AnchorMin: {FormatVector2(rectTransform.anchorMin)}");
            output.AppendLine($"  AnchorMax: {FormatVector2(rectTransform.anchorMax)}");
            output.AppendLine($"  Pivot: {FormatVector2(rectTransform.pivot)}");
            output.AppendLine($"  AnchoredPosition: {FormatVector2(rectTransform.anchoredPosition)}");
            output.AppendLine($"  SizeDelta: {FormatVector2(rectTransform.sizeDelta)}");
            output.AppendLine($"  OffsetMin: {FormatVector2(rectTransform.offsetMin)}");
            output.AppendLine($"  OffsetMax: {FormatVector2(rectTransform.offsetMax)}");
            output.AppendLine($"  Rect: {FormatRect(rectTransform.rect)}");
            AppendScreenSpaceInformation(output, rectTransform, worldCorners, out screenRectangle);
        } else {
            output.AppendLine("  RectTransform: no");
            output.AppendLine("  ScreenSpace: not applicable (not a RectTransform)");
        }

        output.AppendLine("  Components:");
        for (int i = 0; i < components.Length; i++) {
            Component component = components[i];
            output.AppendLine($"    - {(component == null ? "<missing component>" : component.GetType().FullName)}");
        }
        output.AppendLine();

        if (IsCandidate(transform.name, components)) {
            candidates.Add(new CandidateInfo(
                path,
                screenRectangle,
                GetRelevantLayoutComponents(components),
                GetMaskComponents(components),
                transform.gameObject.activeSelf,
                transform.gameObject.activeInHierarchy));
        }

        for (int i = 0; i < transform.childCount; i++) {
            Traverse(
                transform.GetChild(i),
                depth == 0 ? string.Empty : path,
                depth + 1,
                output,
                candidates,
                worldCorners,
                ref nodeCount,
                ref maximumDepth);
        }
    }

    private static void AppendScreenSpaceInformation(
        StringBuilder output,
        RectTransform rectTransform,
        Vector3[] worldCorners,
        out string screenRectangle) {
        rectTransform.GetWorldCorners(worldCorners);
        output.AppendLine(
            $"  WorldCorners: BL={FormatVector3(worldCorners[0])}; TL={FormatVector3(worldCorners[1])}; " +
            $"TR={FormatVector3(worldCorners[2])}; BR={FormatVector3(worldCorners[3])}");

        Canvas canvas = rectTransform.GetComponentInParent<Canvas>();
        if (canvas == null) {
            screenRectangle = "unavailable (no parent Canvas)";
            output.AppendLine($"  ScreenSpace: {screenRectangle}");
            return;
        }

        Camera camera = null;
        if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) {
            camera = canvas.worldCamera;
            if (camera == null) {
                screenRectangle = $"unavailable ({canvas.renderMode} Canvas has no worldCamera)";
                output.AppendLine($"  Canvas: {canvas.name} ({canvas.renderMode})");
                output.AppendLine($"  ScreenSpace: {screenRectangle}");
                return;
            }
        }

        Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[0]);
        Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[1]);
        Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[2]);
        Vector2 bottomRight = RectTransformUtility.WorldToScreenPoint(camera, worldCorners[3]);
        float minimumX = Mathf.Min(bottomLeft.x, topLeft.x, topRight.x, bottomRight.x);
        float minimumY = Mathf.Min(bottomLeft.y, topLeft.y, topRight.y, bottomRight.y);
        float maximumX = Mathf.Max(bottomLeft.x, topLeft.x, topRight.x, bottomRight.x);
        float maximumY = Mathf.Max(bottomLeft.y, topLeft.y, topRight.y, bottomRight.y);
        screenRectangle = FormatRect(new Rect(minimumX, minimumY, maximumX - minimumX, maximumY - minimumY));

        output.AppendLine($"  Canvas: {canvas.name} ({canvas.renderMode})");
        output.AppendLine($"  CanvasCamera: {(camera == null ? "<overlay/no camera>" : camera.name)}");
        output.AppendLine(
            $"  ScreenCorners: BL={FormatVector2(bottomLeft)}; TL={FormatVector2(topLeft)}; " +
            $"TR={FormatVector2(topRight)}; BR={FormatVector2(bottomRight)}");
        output.AppendLine($"  ScreenRect: {screenRectangle}");
    }

    private static bool IsCandidate(string objectName, Component[] components) {
        for (int i = 0; i < CandidateTerms.Length; i++) {
            if (objectName.IndexOf(CandidateTerms[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        for (int i = 0; i < components.Length; i++) {
            Component component = components[i];
            string typeName = component?.GetType().Name;
            if (typeName == null) {
                continue;
            }
            for (int j = 0; j < CandidateTerms.Length; j++) {
                if (typeName.IndexOf(CandidateTerms[j], StringComparison.OrdinalIgnoreCase) >= 0) {
                    return true;
                }
            }
        }
        return false;
    }

    private static string GetRelevantLayoutComponents(Component[] components) {
        var names = new List<string>();
        for (int i = 0; i < components.Length; i++) {
            Component component = components[i];
            if (component is LayoutGroup ||
                component is LayoutElement ||
                component is ContentSizeFitter ||
                component is AspectRatioFitter ||
                component is ScrollRect) {
                names.Add(component.GetType().FullName);
            }
        }
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static string GetMaskComponents(Component[] components) {
        var names = new List<string>();
        for (int i = 0; i < components.Length; i++) {
            Component component = components[i];
            if (component is Mask || component is RectMask2D) {
                names.Add(component.GetType().FullName);
            }
        }
        return names.Count == 0 ? "none" : string.Join(", ", names);
    }

    private static void AppendCandidateSummary(StringBuilder output, List<CandidateInfo> candidates) {
        output.AppendLine("========== LOWER HUD NAME/COMPONENT CANDIDATES ==========");
        output.AppendLine("Candidates are name/component matches only; they are not semantic conclusions.");
        output.AppendLine($"Candidate count: {candidates.Count}");
        for (int i = 0; i < candidates.Count; i++) {
            CandidateInfo candidate = candidates[i];
            output.AppendLine($"Candidate: {candidate.Path}");
            output.AppendLine($"  ScreenRect: {candidate.ScreenRectangle}");
            output.AppendLine($"  LayoutComponents: {candidate.LayoutComponents}");
            output.AppendLine($"  Masks: {candidate.MaskComponents}");
            output.AppendLine($"  ActiveSelf: {candidate.ActiveSelf}");
            output.AppendLine($"  ActiveInHierarchy: {candidate.ActiveInHierarchy}");
        }
        output.AppendLine();
    }

    private static string GetSafeTag(GameObject gameObject) {
        try {
            return gameObject.tag;
        } catch (Exception exception) {
            return $"<unavailable: {exception.GetType().Name}>";
        }
    }

    private static string FormatVector2(Vector2 value) {
        return $"({FormatFloat(value.x)}, {FormatFloat(value.y)})";
    }

    private static string FormatVector3(Vector3 value) {
        return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)})";
    }

    private static string FormatQuaternion(Quaternion value) {
        return $"({FormatFloat(value.x)}, {FormatFloat(value.y)}, {FormatFloat(value.z)}, {FormatFloat(value.w)})";
    }

    private static string FormatRect(Rect value) {
        return $"x={FormatFloat(value.x)}, y={FormatFloat(value.y)}, " +
               $"width={FormatFloat(value.width)}, height={FormatFloat(value.height)}";
    }

    private static string FormatFloat(float value) {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private sealed class CandidateInfo {
        internal readonly string Path;
        internal readonly string ScreenRectangle;
        internal readonly string LayoutComponents;
        internal readonly string MaskComponents;
        internal readonly bool ActiveSelf;
        internal readonly bool ActiveInHierarchy;

        internal CandidateInfo(
            string path,
            string screenRectangle,
            string layoutComponents,
            string maskComponents,
            bool activeSelf,
            bool activeInHierarchy) {
            Path = path;
            ScreenRectangle = screenRectangle;
            LayoutComponents = layoutComponents;
            MaskComponents = maskComponents;
            ActiveSelf = activeSelf;
            ActiveInHierarchy = activeInHierarchy;
        }
    }
}
