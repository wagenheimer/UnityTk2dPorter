using UnityEngine;
using UnityEditor;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// Automatically decides, per selected GameObject, whether conversion should
/// go to UI (Image, via Tk2dImageConverter) or to a plain sprite
/// (SpriteRenderer, via Tk2dSpriteRendererConverter): if the object is
/// already under a Canvas in the hierarchy, it's UI; otherwise it's
/// world-space. The "Force ..." menus on each of the two converters remain
/// available to pick a mode manually.
/// </summary>
public static class Tk2dConversionRouter
{
    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Auto-detect (Selection)", false, 100)]
    private static void ConvertAutoDetect()
    {
        var selected = Selection.gameObjects;
        if (selected.Length == 0) return;

        Selection.activeObject = null;

        int uiCount = 0;
        int spriteCount = 0;
        var uiLog = new StringBuilder();
        var spriteLog = new StringBuilder();
        var uiConverted = new List<GameObject>();
        var spriteConverted = new List<GameObject>();

        Undo.SetCurrentGroupName("Convert tk2d (auto-detect)");
        int group = Undo.GetCurrentGroup();

        foreach (var go in selected)
        {
            bool isUI = go.GetComponentInParent<Canvas>() != null;

            if (isUI)
            {
                Debug.Log($"[Tk2dConverter] '{go.name}': already under a Canvas — converting as UI Image.");
                Tk2dImageConverter.EnsureCanvasParent(go);
                Tk2dImageConverter.ConvertRecursive(go, uiConverted, uiLog, ref uiCount);
                Tk2dImageConverter.PurgeOrphanedTk2dUIComponents(go);
            }
            else
            {
                Debug.Log($"[Tk2dConverter] '{go.name}': not under a Canvas — converting as Sprite Renderer.");
                Tk2dSpriteRendererConverter.ConvertRecursive(go, spriteConverted, spriteLog, ref spriteCount);
            }
        }

        Undo.CollapseUndoOperations(group);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (uiCount > 0)
            Debug.Log($"[Tk2dConverter] {uiCount} sprite(s) converted to Image:\n{uiLog}");
        if (spriteCount > 0)
            Debug.Log($"[Tk2dConverter] {spriteCount} sprite(s) converted to SpriteRenderer:\n{spriteLog}");

        var allConverted = new List<GameObject>();
        allConverted.AddRange(uiConverted);
        allConverted.AddRange(spriteConverted);
        if (allConverted.Count > 0)
            Selection.objects = allConverted.ToArray();
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Auto-detect (Selection)", true)]
    private static bool ValidateConvertAutoDetect()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponentInChildren<tk2dBaseSprite>(true) != null;
    }
}
