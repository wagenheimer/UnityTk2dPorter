using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

/// <summary>
/// Fixes the 9-slice/tile scale of Images that were converted by an older
/// version of the tool (or by hand) without adjusting
/// <see cref="Image.pixelsPerUnitMultiplier"/>.
///
/// Converting a tk2d sprite keeps its texture's own Pixels Per Unit. uGUI scales
/// Sliced/Tiled borders by spritePixelsPerUnit / (canvas.referencePixelsPerUnit *
/// pixelsPerUnitMultiplier), so a sprite at PPU 1 on a canvas with Reference PPU
/// 100 draws borders 100x too big unless the multiplier is 1/100. This pass is
/// idempotent and safe to run on any already-converted hierarchy.
/// </summary>
public static class Tk2dUiNormalizer
{
    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Re-normalize converted UI (Selection)", false, 150)]
    private static void NormalizeSelection()
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0) return;

        int changed = 0;

        Undo.SetCurrentGroupName("Re-normalize converted UI");
        int group = Undo.GetCurrentGroup();

        foreach (var root in roots)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite == null) continue;
                if (image.type != Image.Type.Sliced && image.type != Image.Type.Tiled) continue;

                var canvas = image.canvas;
                if (canvas == null || canvas.referencePixelsPerUnit <= 0f) continue;

                float expected = image.sprite.pixelsPerUnit / canvas.referencePixelsPerUnit;
                if (expected <= 0f) continue;
                if (Mathf.Approximately(image.pixelsPerUnitMultiplier, expected)) continue;

                Undo.RecordObject(image, "Re-normalize pixelsPerUnitMultiplier");
                image.pixelsPerUnitMultiplier = expected;
                EditorUtility.SetDirty(image);
                changed++;
            }
        }

        Undo.CollapseUndoOperations(group);

        Debug.Log($"[Tk2dConverter] Re-normalized {changed} Image(s) (Sliced/Tiled border scale). " +
                  "Nothing else was touched.");
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Re-normalize converted UI (Selection)", true)]
    private static bool ValidateNormalizeSelection()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponentInChildren<Image>(true) != null;
    }
}
