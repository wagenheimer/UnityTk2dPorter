using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Converts world-space tk2d sprites (gameplay, non-UI) to a native
/// SpriteRenderer. Sibling of Tk2dImageConverter, which handles the UI case
/// (Image/Canvas) — this converter never deals with Canvas, RectTransform,
/// Button/Toggle/Slider, or any other UI concept.
/// </summary>
public static class Tk2dSpriteRendererConverter
{
    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Force Sprite Renderer (Selection)", false, 111)]
    private static void ConvertFromMenuItem()
    {
        var selected = Selection.gameObjects;
        if (selected.Length == 0) return;

        Selection.activeObject = null;

        int count = 0;
        StringBuilder log = new StringBuilder();
        var converted = new List<GameObject>();

        Undo.SetCurrentGroupName("Convert tk2d to SpriteRenderer");
        int group = Undo.GetCurrentGroup();

        foreach (var go in selected)
        {
            ConvertRecursive(go, converted, log, ref count);
        }

        Undo.CollapseUndoOperations(group);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (count > 0)
        {
            Debug.Log($"[Tk2dSpriteRendererConverter] Converted {count} tk2d sprite(s) to SpriteRenderer:\n{log}");
            Selection.objects = converted.ToArray();
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Force Sprite Renderer (Selection)", true)]
    private static bool ValidateConvertFromMenuItem()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponentInChildren<tk2dBaseSprite>(true) != null;
    }

    internal static void ConvertRecursive(GameObject root, List<GameObject> converted, StringBuilder log, ref int count)
    {
        var sprites = root.GetComponentsInChildren<tk2dBaseSprite>(true);

        foreach (var spr in sprites)
        {
            if (spr == null) continue;
            if (spr.gameObject.GetComponent<SpriteRenderer>() != null) continue;

            var go = spr.gameObject;
            var sprName = go.name;
            var sprType = spr.GetType().Name;

            try
            {
                if (Convert(spr))
                {
                    converted.Add(go);
                    log.AppendLine($"  • {sprName} ({sprType})");
                    count++;
                    Debug.Log($"[Tk2dSpriteRendererConverter] ✓ '{sprName}' converted successfully.");
                }
                else
                {
                    Debug.LogWarning($"[Tk2dSpriteRendererConverter] ✗ '{sprName}' was not converted.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dSpriteRendererConverter] ✗ Exception converting '{sprName}': {ex}", go);
            }
        }
    }

    internal static bool Convert(tk2dBaseSprite spr)
    {
        if (spr == null) return false;

        GameObject go = spr.gameObject;

        if (go.GetComponent<tk2dUIItem>() != null)
        {
            Debug.LogWarning($"[Tk2dSpriteRendererConverter] '{go.name}' has a tk2dUIItem (it's a button/UI element) — " +
                             "use the UI converter (Force UI Image) instead of Sprite Renderer.", go);
            return false;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        Undo.RegisterCompleteObjectUndo(go, "Convert tk2d to SpriteRenderer");

        var def = spr.CurrentSprite;
        if (def == null)
        {
            Debug.LogWarning($"'{go.name}' has no CurrentSprite definition.", go);
            return false;
        }

        System.Type spriteType = spr.GetType();

        Color color = spr.color;
        bool flipX = spr.FlipX;
        bool flipY = spr.FlipY;
        int sortingOrder = spr.SortingOrder;

        Vector2 slicedBorderBL = Vector2.zero;
        Vector2 slicedBorderTR = Vector2.zero;
        if (spr is tk2dSlicedSprite sliced)
        {
            slicedBorderBL = new Vector2(sliced.borderLeft, sliced.borderBottom);
            slicedBorderTR = new Vector2(sliced.borderRight, sliced.borderTop);
        }

        bool isSliced = spriteType == typeof(tk2dSlicedSprite);

        Sprite unitySprite = Tk2dSpriteAssetUtility.FindOrCreateSpriteAsset(def, isSliced, slicedBorderBL, slicedBorderTR);
        if (unitySprite == null)
        {
            Debug.LogWarning($"'{go.name}': Could not find/create Sprite asset for '{def.name}'. Creating SpriteRenderer with null sprite.", go);
        }

        Vector2 targetSize = Tk2dSpriteAssetUtility.CalculateTargetSize(spr, def, unitySprite);

        Tk2dSpriteAssetUtility.DestroyTk2dSpriteComponents(go, spr);
        Tk2dSpriteAssetUtility.RemoveLegacyComponents(go);

        var spriteRenderer = go.AddComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            Debug.LogError($"[Tk2dSpriteRendererConverter] Failed to add SpriteRenderer component to '{go.name}'.", go);
            return false;
        }
        spriteRenderer.sprite = unitySprite;
        spriteRenderer.color = color;
        spriteRenderer.flipX = flipX;
        spriteRenderer.flipY = flipY;
        spriteRenderer.sortingOrder = sortingOrder;

        if (spriteType == typeof(tk2dSlicedSprite))
        {
            spriteRenderer.drawMode = SpriteDrawMode.Sliced;
            spriteRenderer.size = targetSize;
        }
        else if (spriteType == typeof(tk2dTiledSprite))
        {
            spriteRenderer.drawMode = SpriteDrawMode.Tiled;
            spriteRenderer.size = targetSize;
        }
        else
        {
            spriteRenderer.drawMode = SpriteDrawMode.Simple;

            if (spriteType == typeof(tk2dClippedSprite))
            {
                Debug.LogWarning($"'{go.name}': tk2dClippedSprite has no native fill/clip equivalent on " +
                                 "SpriteRenderer — converted as a simple sprite (without the clipping effect). " +
                                 "Review manually if clipping is required.", go);
            }
        }

        if (spriteType != typeof(tk2dSprite))
        {
            Debug.Log($"'{go.name}': Converted {spriteType.Name} to SpriteRenderer ({spriteRenderer.drawMode}). Check result manually.", go);
        }

        EditorUtility.SetDirty(go);
        return true;
    }
}
