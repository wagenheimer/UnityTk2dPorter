using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Converts native Unity SpriteRenderer components to uGUI Image components.
/// Automatically handles Canvas creation/parenting, RectTransform sizing,
/// drawMode (Simple/Sliced/Tiled), color, and flips.
/// </summary>
public static class SpriteRendererToImageConverter
{
    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert SpriteRenderer \u2192 Image (Selection)", false, 103)]
    private static void ConvertFromMenuItem()
    {
        var selected = Selection.gameObjects;
        if (selected.Length == 0) return;

        Selection.activeObject = null;

        int count = 0;
        StringBuilder log = new StringBuilder();
        var converted = new List<GameObject>();

        Undo.SetCurrentGroupName("Convert SpriteRenderer to Image");
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
            Debug.Log($"[SpriteRendererToImageConverter] Converted {count} SpriteRenderer(s) to Image:\n{log}");
            Selection.objects = converted.ToArray();
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert SpriteRenderer \u2192 Image (Selection)", true)]
    private static bool ValidateConvertFromMenuItem()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponentInChildren<SpriteRenderer>(true) != null;
    }

    [MenuItem("CONTEXT/SpriteRenderer/Convert to Image (uGUI)", false, 1000)]
    private static void ConvertFromContext(MenuCommand command)
    {
        if (command.context is SpriteRenderer sr)
        {
            Selection.activeObject = null;

            Undo.SetCurrentGroupName("Convert SpriteRenderer to Image");
            int group = Undo.GetCurrentGroup();

            var converted = new List<GameObject>();
            var log = new StringBuilder();
            int count = 0;

            Convert(sr, converted, log, ref count);

            Undo.CollapseUndoOperations(group);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (count > 0)
            {
                Debug.Log($"[SpriteRendererToImageConverter] Converted SpriteRenderer on '{sr.gameObject.name}' to Image:\n{log}");
                Selection.objects = converted.ToArray();
            }
        }
    }

    public static void ConvertRecursive(GameObject root, List<GameObject> converted, StringBuilder log, ref int count)
    {
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        foreach (var sr in renderers)
        {
            Convert(sr, converted, log, ref count);
        }
    }

    public static bool Convert(SpriteRenderer sr, List<GameObject> converted, StringBuilder log, ref int count)
    {
        if (sr == null) return false;

        GameObject go = sr.gameObject;
        if (go.GetComponent<Image>() != null) return false; // Already an Image

        // Store SpriteRenderer values
        Sprite sprite = sr.sprite;
        Color color = sr.color;
        SpriteDrawMode drawMode = sr.drawMode;
        Vector2 size = sr.size;
        bool flipX = sr.flipX;
        bool flipY = sr.flipY;

        // Unpack prefab instance if part of a prefab before changing hierarchy or component types
        UnpackIfPrefabInstance(go);

        // Ensure canvas parent exists
        Tk2dImageConverter.EnsureCanvasParent(go);

        // Ensure RectTransform exists before adding Image
        EnsureRectTransform(go);

        Undo.RegisterCompleteObjectUndo(go, "Convert SpriteRenderer to Image");

        // Destroy SpriteRenderer
        Undo.DestroyObjectImmediate(sr);

        // Remove leftover MeshFilter/MeshRenderer if present
        var mf = go.GetComponent<MeshFilter>();
        if (mf != null) Undo.DestroyObjectImmediate(mf);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) Undo.DestroyObjectImmediate(mr);

        // Add or get Image component safely
        var image = go.GetComponent<Image>();
        if (image == null)
        {
            image = Undo.AddComponent<Image>(go);
        }

        if (image == null)
        {
            Debug.LogError($"[SpriteRendererToImageConverter] Failed to add Image component to '{go.name}'.", go);
            return false;
        }

        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false; // Default false for decorative graphics

        // Handle drawMode -> Image.Type
        switch (drawMode)
        {
            case SpriteDrawMode.Sliced:
                image.type = Image.Type.Sliced;
                break;
            case SpriteDrawMode.Tiled:
                image.type = Image.Type.Tiled;
                break;
            default:
                image.type = Image.Type.Simple;
                break;
        }

        // Adjust RectTransform
        var rt = go.GetComponent<RectTransform>();
        if (rt != null)
        {
            if (drawMode == SpriteDrawMode.Sliced || drawMode == SpriteDrawMode.Tiled)
            {
                if (sprite != null && size != Vector2.zero)
                {
                    // SpriteRenderer size is in world units (1 unit = pixelsPerUnit)
                    float ppu = sprite.pixelsPerUnit > 0 ? sprite.pixelsPerUnit : 100f;
                    rt.sizeDelta = size * ppu;
                }
            }
            else if (sprite != null && rt.sizeDelta == Vector2.zero)
            {
                rt.sizeDelta = sprite.rect.size;
            }

            // Apply flips via RectTransform localScale
            Vector3 localScale = rt.localScale;
            if (flipX && localScale.x > 0) localScale.x = -localScale.x;
            if (flipY && localScale.y > 0) localScale.y = -localScale.y;
            rt.localScale = localScale;
        }

        if (!converted.Contains(go))
            converted.Add(go);

        count++;
        log.AppendLine($"  \u2022 '{go.name}' \u2192 Image (Type: {image.type})");
        return true;
    }

    private static void UnpackIfPrefabInstance(GameObject go)
    {
        if (go == null) return;
        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root != null)
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction);
            }
        }
    }

    private static RectTransform EnsureRectTransform(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) return rt;

        UnpackIfPrefabInstance(go);

        var t = go.transform;
        Vector3 pos = t.localPosition;
        Vector3 rot = t.localEulerAngles;
        Vector3 scale = t.localScale;

        rt = Undo.AddComponent<RectTransform>(go);

        rt.localPosition = pos;
        rt.localEulerAngles = rot;
        rt.localScale = scale;
        rt.anchorMin = Vector2.one * 0.5f;
        rt.anchorMax = Vector2.one * 0.5f;
        rt.pivot = Vector2.one * 0.5f;

        return rt;
    }
}
