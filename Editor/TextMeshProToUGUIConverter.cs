using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using TMPro.EditorUtilities;
using System.Text;

[CustomEditor(typeof(TextMeshPro))]
[CanEditMultipleObjects]
public class TextMeshProToUGUIConverter : TMP_EditorPanel
{
    public override void OnInspectorGUI()
    {
        DrawConvertButton();
        EditorGUILayout.Space(5);
        base.OnInspectorGUI();
    }

    private void DrawConvertButton()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("TMP \u2192 TMP_UGUI Converter", EditorStyles.boldLabel);

        if (GUILayout.Button("Convert to TextMeshProUGUI", GUILayout.Height(30)))
        {
            ConvertSelection();
        }

        if (targets.Length > 1)
        {
            EditorGUILayout.HelpBox(
                $"{targets.Length} TextMeshPro components selected. All will be converted.",
                MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    private void ConvertSelection()
    {
        int count = 0;
        StringBuilder log = new StringBuilder();
        var converted = new GameObject[targets.Length];

        Undo.SetCurrentGroupName("Convert TMP to TMP_UGUI");
        int group = Undo.GetCurrentGroup();

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] is TextMeshPro tmp)
            {
                var go = tmp.gameObject;
                if (Convert(tmp))
                {
                    converted[count] = go;
                    log.AppendLine($"  \u2022 {go.name}");
                    count++;
                }
            }
        }

        Undo.CollapseUndoOperations(group);

        if (count > 0)
        {
            Debug.Log($"Converted {count} TextMeshPro(s) to TextMeshProUGUI:\n{log}");

            var selected = new GameObject[count];
            System.Array.Copy(converted, selected, count);
            Selection.objects = selected;
        }
    }

    internal static bool Convert(TextMeshPro tmp)
    {
        if (tmp == null) return false;

        GameObject go = tmp.gameObject;

        if (go.GetComponent<TextMeshProUGUI>() != null)
        {
            Debug.LogWarning($"'{go.name}' already has a TextMeshProUGUI. Skipping.", go);
            return false;
        }

        string json = EditorJsonUtility.ToJson(tmp);

        TMP_FontAsset font = tmp.font;
        Material fontSharedMaterial = tmp.fontSharedMaterial;
        string text = tmp.text;
        float fontSize = tmp.fontSize;
        bool autoSize = tmp.autoSizeTextContainer;
        Color color = tmp.color;
        TextAlignmentOptions alignment = tmp.alignment;
        FontStyles fontStyle = tmp.fontStyle;
        bool richText = tmp.richText;
        bool raycastTarget = tmp.raycastTarget;
        bool enableWordWrapping = tmp.enableWordWrapping;
        TextOverflowModes overflowMode = tmp.overflowMode;
        float lineSpacing = tmp.lineSpacing;
        float characterSpacing = tmp.characterSpacing;
        float wordSpacing = tmp.wordSpacing;
        float paragraphSpacing = tmp.paragraphSpacing;

        if (go.GetComponentInParent<Canvas>() == null)
        {
            var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.transform.SetParent(go.transform.parent, false);
            canvasGO.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
            go.transform.SetParent(canvasGO.transform, true);
            Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
        }

        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        Undo.RegisterCompleteObjectUndo(go, "Convert TMP");

        Undo.DestroyObjectImmediate(tmp);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
            Undo.DestroyObjectImmediate(mr);

        var mf = go.GetComponent<MeshFilter>();
        if (mf != null)
            Undo.DestroyObjectImmediate(mf);

        var tmpUGUI = go.AddComponent<TextMeshProUGUI>();

        EditorJsonUtility.FromJsonOverwrite(json, tmpUGUI);

        tmpUGUI.text = text;
        tmpUGUI.font = font;
        tmpUGUI.fontSharedMaterial = fontSharedMaterial;
        tmpUGUI.fontSize = fontSize;
        tmpUGUI.autoSizeTextContainer = autoSize;
        tmpUGUI.color = color;
        tmpUGUI.alignment = alignment;
        tmpUGUI.fontStyle = fontStyle;
        tmpUGUI.richText = richText;
        tmpUGUI.raycastTarget = raycastTarget;
        tmpUGUI.enableWordWrapping = enableWordWrapping;
        tmpUGUI.overflowMode = overflowMode;
        tmpUGUI.lineSpacing = lineSpacing;
        tmpUGUI.characterSpacing = characterSpacing;
        tmpUGUI.wordSpacing = wordSpacing;
        tmpUGUI.paragraphSpacing = paragraphSpacing;

        EditorUtility.SetDirty(go);
        return true;
    }

    [MenuItem("CONTEXT/TextMeshPro/Convert to TextMeshProUGUI", false, 1000)]
    private static void ConvertFromContext(MenuCommand command)
    {
        if (command.context is TextMeshPro tmp)
        {
            Undo.SetCurrentGroupName("Convert TMP to TMP_UGUI");
            int group = Undo.GetCurrentGroup();
            Convert(tmp);
            Undo.CollapseUndoOperations(group);
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert TMP \u2192 TMP_UGUI (Selection)", false, 100)]
    private static void ConvertFromMenuItem()
    {
        var tmps = Selection.GetFiltered<TextMeshPro>(SelectionMode.Unfiltered);
        if (tmps.Length == 0) return;

        int count = 0;
        StringBuilder log = new StringBuilder();
        var converted = new GameObject[tmps.Length];

        Undo.SetCurrentGroupName("Convert TMP to TMP_UGUI");
        int group = Undo.GetCurrentGroup();

        for (int i = 0; i < tmps.Length; i++)
        {
            var go = tmps[i].gameObject;
            if (Convert(tmps[i]))
            {
                converted[count] = go;
                log.AppendLine($"  \u2022 {go.name}");
                count++;
            }
        }

        Undo.CollapseUndoOperations(group);

        if (count > 0)
        {
            Debug.Log($"Converted {count} TextMeshPro(s) to TextMeshProUGUI:\n{log}");

            var selected = new GameObject[count];
            System.Array.Copy(converted, selected, count);
            Selection.objects = selected;
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert TMP \u2192 TMP_UGUI (Selection)", true)]
    private static bool ValidateConvertFromMenuItem()
    {
        return Selection.GetFiltered<TextMeshPro>(SelectionMode.Unfiltered).Length > 0;
    }
}
