using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.Events;
using System.Text;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Converts tk2d sprites/buttons/toggles/scrollbars/TextMeshPro to their uGUI
/// equivalents (Image/Button/Toggle/Slider/TextMeshProUGUI). For world-space
/// gameplay sprites use Tk2dSpriteRendererConverter instead — see
/// Tk2dConversionRouter for an auto-detecting menu that picks the right one.
/// </summary>
public static class Tk2dImageConverter
{
    // Avoids showing the "AnimatorController not found" dialog more than once
    // per conversion batch (several buttons in the same selection).
    private static bool _missingButtonControllerWarned = false;

    // tk2dUI*-prefixed components that are the CONSUMING PROJECT'S OWN
    // GAMEPLAY LOGIC (e.g. custom drag-and-drop), not native tk2d visual
    // components — must never be auto-destroyed by the purge, even though the
    // name starts with "tk2dUI". Add any new custom class created under that
    // naming convention here.
    private static readonly HashSet<string> Tk2dUITypeNamesToPreserve = new HashSet<string>
    {
        "tk2dUIDragItemGrid",
        "tk2dUIDragItemClamp",
        "tk2dUIManager",
    };

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Force UI Image (Selection)", false, 110)]
    private static void ConvertFromMenuItem()
    {
        var selected = Selection.gameObjects;
        if (selected.Length == 0) return;

        int count = 0;
        StringBuilder log = new StringBuilder();
        var converted = new List<GameObject>();

        // Avoids the Inspector trying to redraw a component (TextMeshPro,
        // tk2dUIScrollbar, etc.) at the exact moment it's destroyed in the loop.
        Selection.activeObject = null;

        _missingButtonControllerWarned = false;

        Undo.SetCurrentGroupName("Convert tk2d to uGUI Image");
        int group = Undo.GetCurrentGroup();

        Debug.Log($"[Tk2dConverter] Starting conversion of {selected.Length} selected object(s)...");

        foreach (var go in selected)
        {
            EnsureCanvasParent(go);
            ConvertRecursive(go, converted, log, ref count);
            PurgeOrphanedTk2dUIComponents(go);
        }

        Undo.CollapseUndoOperations(group);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[Tk2dConverter] Conversion finished. {count} sprite(s) converted.");

        if (count > 0)
        {
            Debug.Log($"Converted {count} tk2d sprite(s) to Image:\n{log}");

            var allConverted = new List<GameObject>();
            foreach (var go in selected)
            {
                var sprites = go.GetComponentsInChildren<tk2dBaseSprite>(true);
                foreach (var spr in sprites)
                {
                    if (spr != null && spr.gameObject != null && spr.GetComponent<Image>() != null)
                    {
                        if (!allConverted.Contains(spr.gameObject))
                            allConverted.Add(spr.gameObject);
                    }
                }
            }

            if (allConverted.Count > 0)
                Selection.objects = allConverted.ToArray();
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Convert tk2d → uGUI/Force UI Image (Selection)", true)]
    private static bool ValidateConvertFromMenuItem()
    {
        var go = Selection.activeGameObject;
        return go != null && go.GetComponentInChildren<tk2dBaseSprite>(true) != null;
    }

    [MenuItem("CONTEXT/tk2dSprite/Convert to Image", false, 1000)]
    private static void ConvertSpriteFromContext(MenuCommand command)
    {
        ConvertContext(command);
    }

    [MenuItem("CONTEXT/tk2dSlicedSprite/Convert to Image", false, 1000)]
    private static void ConvertSlicedFromContext(MenuCommand command)
    {
        ConvertContext(command);
    }

    [MenuItem("CONTEXT/tk2dClippedSprite/Convert to Image", false, 1000)]
    private static void ConvertClippedFromContext(MenuCommand command)
    {
        ConvertContext(command);
    }

    [MenuItem("CONTEXT/tk2dTiledSprite/Convert to Image", false, 1000)]
    private static void ConvertTiledFromContext(MenuCommand command)
    {
        ConvertContext(command);
    }

    private static void ConvertContext(MenuCommand command)
    {
        if (command.context is tk2dBaseSprite spr)
        {
            Selection.activeObject = null;
            _missingButtonControllerWarned = false;

            Undo.SetCurrentGroupName("Convert tk2d to uGUI Image");
            int group = Undo.GetCurrentGroup();

            EnsureCanvasParent(spr.gameObject);

            var converted = new List<GameObject>();
            var log = new StringBuilder();
            int count = 0;
            ConvertRecursive(spr.gameObject, converted, log, ref count);
            PurgeOrphanedTk2dUIComponents(spr.gameObject);

            Undo.CollapseUndoOperations(group);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (count > 0)
                Debug.Log($"Converted {count} tk2d sprite(s) to Image:\n{log}");
        }
    }

    /// <summary>
    /// Removes orphaned tk2dUI* components that didn't belong to any
    /// converted sprite/button/toggle (e.g. a tk2dUIToggleButtonGroup
    /// coordinator on the parent panel, now useless and holding broken
    /// references to already-destroyed components).
    /// </summary>
    internal static void PurgeOrphanedTk2dUIComponents(GameObject root)
    {
        var all = root.GetComponentsInChildren<Component>(true);
        int removed = 0;

        foreach (var comp in all)
        {
            if (comp == null) continue;

            string typeName = comp.GetType().Name;
            if (!typeName.StartsWith("tk2dUI")) continue;

            if (Tk2dUITypeNamesToPreserve.Contains(typeName))
            {
                Debug.LogWarning($"[Tk2dConverter]   Preserving '{typeName}' on '{comp.gameObject.name}' — it's on the exclusion list (gameplay logic, not a tk2d visual).", comp.gameObject);
                continue;
            }

            try
            {
                Debug.LogWarning($"[Tk2dConverter]   Removing orphaned tk2dUI component '{typeName}' on '{comp.gameObject.name}'.", comp.gameObject);
                Undo.DestroyObjectImmediate(comp);
                removed++;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dConverter]   ✗ Failed to remove orphaned '{typeName}' on '{comp.gameObject.name}': {ex.Message}", comp.gameObject);
            }
        }

        if (removed > 0)
            Debug.Log($"[Tk2dConverter] '{root.name}': {removed} orphaned tk2dUI component(s) removed in final cleanup.");
    }

    internal static void ConvertRecursive(GameObject root, List<GameObject> converted, StringBuilder log, ref int count)
    {
        var sprites = root.GetComponentsInChildren<tk2dBaseSprite>(true);
        Debug.Log($"[Tk2dConverter] '{root.name}': {sprites.Length} tk2d sprite(s) found in the hierarchy.");

        foreach (var spr in sprites)
        {
            if (spr == null) continue;
            if (spr.gameObject.GetComponent<Image>() != null) continue;

            var go = spr.gameObject;
            var sprName = go.name;
            var sprType = spr.GetType().Name;

            Debug.Log($"[Tk2dConverter] Converting '{sprName}' ({sprType})...");

            try
            {
                if (Convert(spr))
                {
                    converted.Add(go);
                    log.AppendLine($"  • {sprName} ({sprType})");
                    count++;
                    Debug.Log($"[Tk2dConverter] ✓ '{sprName}' converted successfully.");
                }
                else
                {
                    Debug.LogWarning($"[Tk2dConverter] ✗ '{sprName}' was not converted (Convert returned false).");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dConverter] ✗ Exception converting '{sprName}': {ex}", go);
            }
        }

        // Second pass: tk2d buttons whose visual doesn't live on the
        // GameObject itself, but on state children (Off/On/Disabled via
        // tk2dUIHoverDisabledItem). Those GameObjects have no
        // tk2dBaseSprite of their own, so the loop above never touches
        // them — without this, tk2dUIItem/tk2dUITweenItem/etc. would be left
        // orphaned.
        ConvertButtonRootsRecursive(root, converted, log, ref count);

        // Third pass: tk2d toggles (same Off/On state-children pattern, but
        // via tk2dUIToggleButton/tk2dUIToggleControl).
        ConvertToggleRootsRecursive(root, converted, log, ref count);
    }

    private static void ConvertButtonRootsRecursive(GameObject root, List<GameObject> converted, StringBuilder log, ref int count)
    {
        var uiItems = root.GetComponentsInChildren<tk2dUIItem>(true);
        Debug.Log($"[Tk2dConverter] '{root.name}': {uiItems.Length} tk2dUIItem (button) found in the hierarchy.");

        foreach (var item in uiItems)
        {
            if (item == null) continue;
            var go = item.gameObject;

            if (go.GetComponent<Button>() != null) continue; // already converted (had its own sprite)
            if (go.GetComponent<tk2dBaseSprite>() != null) continue; // will be/was handled by the sprite loop
            if (go.GetComponent<tk2dUIToggleButton>() != null) continue; // it's a toggle, handled separately

            Debug.Log($"[Tk2dConverter] Converting button (no sprite of its own) '{go.name}'...");

            try
            {
                if (ConvertButtonRoot(go))
                {
                    converted.Add(go);
                    log.AppendLine($"  • {go.name} (Button Root)");
                    count++;
                    Debug.Log($"[Tk2dConverter] ✓ '{go.name}' (button) converted successfully.");
                }
                else
                {
                    Debug.LogWarning($"[Tk2dConverter] ✗ '{go.name}' (button) could not be converted automatically. " +
                                     "No already-converted (Image) visual state (Off/On) was found — convert it manually.", go);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dConverter] ✗ Exception converting button '{go.name}': {ex}", go);
            }
        }
    }

    /// <summary>
    /// Converts the root of a tk2d button whose visual lives in state
    /// children (Off/On/Disabled) instead of a tk2dBaseSprite of its own.
    /// Extracts the already-converted Image from the normal state, applies it
    /// to the root, and discards the state children.
    /// </summary>
    internal static bool ConvertButtonRoot(GameObject go)
    {
        if (go == null) return false;

        var hoverItem = go.GetComponent<tk2dUIHoverDisabledItem>();

        GameObject outGO = hoverItem != null ? hoverItem.outStateGO : null;
        GameObject overGO = hoverItem != null ? hoverItem.overStateGO : null;
        GameObject disabledGO = hoverItem != null ? hoverItem.disabledStateGO : null;

        GameObject normalStateGO = outGO != null ? outGO : FindChildByName(go, "Off", "Normal", "Idle");
        if (normalStateGO == null)
            normalStateGO = overGO != null ? overGO : FindChildByName(go, "On", "Over", "Highlighted");

        Image sourceImage = normalStateGO != null ? normalStateGO.GetComponent<Image>() : null;

        if (sourceImage == null)
        {
            return false;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        EnsureRectTransform(go);
        Undo.RegisterCompleteObjectUndo(go, "Convert tk2d Button Root");

        var sourceRect = sourceImage.GetComponent<RectTransform>();
        Vector2 size = sourceRect != null ? sourceRect.sizeDelta : Vector2.zero;

        var boxCollider = go.GetComponent<BoxCollider>();
        if (size == Vector2.zero && boxCollider != null)
        {
            size = new Vector2(boxCollider.size.x, boxCollider.size.y);
        }

        var image = Undo.AddComponent<Image>(go);
        image.sprite = sourceImage.sprite;
        image.color = sourceImage.color;
        image.type = sourceImage.type;
        image.fillCenter = sourceImage.fillCenter;
        image.raycastTarget = true;

        var rt = go.GetComponent<RectTransform>();
        if (size != Vector2.zero)
            rt.sizeDelta = size;

        // The state children (Off/On/Disabled) already had their visual
        // merged into the root Image — they're no longer needed. "Label"
        // and any other child that isn't part of the state trio is preserved.
        if (outGO != null && outGO != go) Undo.DestroyObjectImmediate(outGO);
        if (overGO != null && overGO != go && overGO != outGO) Undo.DestroyObjectImmediate(overGO);
        if (disabledGO != null && disabledGO != go && disabledGO != outGO && disabledGO != overGO) Undo.DestroyObjectImmediate(disabledGO);

        ConvertLegacyComponents(go);

        EditorUtility.SetDirty(go);
        return true;
    }

    private static GameObject FindChildByName(GameObject parent, params string[] names)
    {
        foreach (Transform child in parent.transform)
        {
            foreach (var name in names)
            {
                if (child.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                    return child.gameObject;
            }
        }
        return null;
    }

    private static void ConvertToggleRootsRecursive(GameObject root, List<GameObject> converted, StringBuilder log, ref int count)
    {
        var toggles = root.GetComponentsInChildren<tk2dUIToggleButton>(true);
        Debug.Log($"[Tk2dConverter] '{root.name}': {toggles.Length} tk2dUIToggleButton (toggle) found in the hierarchy.");

        foreach (var t in toggles)
        {
            if (t == null) continue;
            var go = t.gameObject;

            if (go.GetComponent<Toggle>() != null) continue; // already converted
            if (go.GetComponent<tk2dBaseSprite>() != null) continue; // handled by the sprite loop

            Debug.Log($"[Tk2dConverter] Converting toggle (no sprite of its own) '{go.name}'...");

            try
            {
                if (ConvertToggleRoot(go))
                {
                    converted.Add(go);
                    log.AppendLine($"  • {go.name} (Toggle Root)");
                    count++;
                    Debug.Log($"[Tk2dConverter] ✓ '{go.name}' (toggle) converted successfully.");
                }
                else
                {
                    Debug.LogWarning($"[Tk2dConverter] ✗ '{go.name}' (toggle) could not be converted automatically. " +
                                     "No already-converted (Image) visual state (Off/On) was found — convert it manually.", go);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dConverter] ✗ Exception converting toggle '{go.name}': {ex}", go);
            }
        }
    }

    /// <summary>
    /// Converts the root of a tk2d toggle whose visual lives in state
    /// children (Off/On) instead of a tk2dBaseSprite of its own. The "Off"
    /// state becomes the background Image (Toggle.targetGraphic); the "On"
    /// state is preserved as a child and becomes the checkmark (Toggle.graphic).
    /// </summary>
    internal static bool ConvertToggleRoot(GameObject go)
    {
        if (go == null) return false;

        var toggleComp = go.GetComponent<tk2dUIToggleButton>();
        if (toggleComp == null) return false;

        GameObject offGO = toggleComp.offStateGO;
        GameObject onGO = toggleComp.onStateGO;

        Image sourceImage = offGO != null ? offGO.GetComponent<Image>() : null;
        if (sourceImage == null)
            sourceImage = onGO != null ? onGO.GetComponent<Image>() : null;

        if (sourceImage == null)
        {
            return false;
        }

        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        EnsureRectTransform(go);
        Undo.RegisterCompleteObjectUndo(go, "Convert tk2d Toggle Root");

        var sourceRect = sourceImage.GetComponent<RectTransform>();
        Vector2 size = sourceRect != null ? sourceRect.sizeDelta : Vector2.zero;

        var boxCollider = go.GetComponent<BoxCollider>();
        if (size == Vector2.zero && boxCollider != null)
        {
            size = new Vector2(boxCollider.size.x, boxCollider.size.y);
        }

        var image = Undo.AddComponent<Image>(go);
        image.sprite = sourceImage.sprite;
        image.color = sourceImage.color;
        image.type = sourceImage.type;
        image.fillCenter = sourceImage.fillCenter;
        image.raycastTarget = true;

        var rt = go.GetComponent<RectTransform>();
        if (size != Vector2.zero)
            rt.sizeDelta = size;

        // "Off" was already merged into the background Image — no longer
        // needed. "On" is preserved as a child: it becomes the checkmark
        // (Toggle.graphic).
        if (offGO != null && offGO != go && offGO != onGO)
            Undo.DestroyObjectImmediate(offGO);

        ConvertLegacyComponents(go);

        EditorUtility.SetDirty(go);
        return true;
    }

    internal static bool Convert(tk2dBaseSprite spr)
    {
        if (spr == null) return false;

        GameObject go = spr.gameObject;

        if (PrefabUtility.IsPartOfPrefabInstance(go))
        {
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.UserAction);
        }

        EnsureRectTransform(go);

        Undo.RegisterCompleteObjectUndo(go, "Convert tk2d to Image");

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

        Vector2 clipBL = Vector2.zero;
        Vector2 clipTR = Vector2.one;
        if (spr is tk2dClippedSprite clipped)
        {
            clipBL = clipped.clipBottomLeft;
            clipTR = clipped.clipTopRight;
        }

        bool isSliced = spriteType == typeof(tk2dSlicedSprite);

        Sprite unitySprite = Tk2dSpriteAssetUtility.FindOrCreateSpriteAsset(def, isSliced, slicedBorderBL, slicedBorderTR);
        if (unitySprite == null)
        {
            Debug.LogWarning($"'{go.name}': Could not find/create Sprite asset for '{def.name}'. Creating Image with null sprite.", go);
        }

        Vector2 targetSize = Tk2dSpriteAssetUtility.CalculateTargetSize(spr, def, unitySprite);

        Tk2dSpriteAssetUtility.DestroyTk2dSpriteComponents(go, spr);

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = targetSize;

        var image = go.AddComponent<Image>();
        image.sprite = unitySprite;
        image.color = color;
        image.raycastTarget = true;

        if (flipX || flipY)
        {
            Vector3 localScale = rt.localScale;
            if (flipX) localScale.x *= -1;
            if (flipY) localScale.y *= -1;
            rt.localScale = localScale;
        }

        SetupCanvasSorting(go, sortingOrder);

        if (spriteType == typeof(tk2dSlicedSprite))
        {
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
        }
        else if (spriteType == typeof(tk2dClippedSprite))
        {
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = Mathf.Clamp01((clipTR.x - clipBL.x) * (clipTR.y - clipBL.y));
        }
        else if (spriteType == typeof(tk2dTiledSprite))
        {
            image.type = Image.Type.Tiled;
        }
        else
        {
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
        }

        Debug.Log($"[Tk2dConverter]   '{go.name}': Image created, starting legacy component conversion...");
        ConvertLegacyComponents(go);

        if (spriteType != typeof(tk2dSprite))
        {
            Debug.Log($"'{go.name}': Converted {spriteType.Name} to Image ({image.type}). Check result manually.", go);
        }

        EditorUtility.SetDirty(go);
        return true;
    }

    private static void ConvertLegacyComponents(GameObject go)
    {
        ConvertTextMeshProToUGUIRecursive(go);
        ConvertTk2dUIScrollbarToSlider(go);
        RemoveLegacyTk2dUIComponents(go);
        Tk2dSpriteAssetUtility.RemoveLegacyComponents(go);
    }

    private static void ConvertTextMeshProToUGUIRecursive(GameObject go)
    {
        var textMeshPros = go.GetComponents<TextMeshPro>();
        foreach (var tmp in textMeshPros)
        {
            if (tmp != null && go.GetComponent<TextMeshProUGUI>() == null)
            {
                ConvertTextMeshProToUGUI(go, tmp);
            }
        }

        foreach (Transform child in go.transform)
        {
            ConvertTextMeshProToUGUIRecursive(child.gameObject);
        }
    }

    private static void ConvertTextMeshProToUGUI(GameObject go, TextMeshPro textMeshPro)
    {
        if (textMeshPro == null) return;

        try
        {
            string text = textMeshPro.text;
            TMP_FontAsset font = textMeshPro.font;
            Material fontMaterial = textMeshPro.fontSharedMaterial;
            float fontSize = textMeshPro.fontSize;
            bool autoSize = textMeshPro.autoSizeTextContainer;
            Color color = textMeshPro.color;
            TextAlignmentOptions alignment = textMeshPro.alignment;
            FontStyles fontStyle = textMeshPro.fontStyle;
            bool richText = textMeshPro.richText;
            bool enableWordWrapping = textMeshPro.enableWordWrapping;
            TextOverflowModes overflowMode = textMeshPro.overflowMode;
            float lineSpacing = textMeshPro.lineSpacing;
            float charSpacing = textMeshPro.characterSpacing;
            float wordSpacing = textMeshPro.wordSpacing;
            float paraSpacing = textMeshPro.paragraphSpacing;

            Undo.DestroyObjectImmediate(textMeshPro);

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                rt = Undo.AddComponent<RectTransform>(go);

            var tmpUGUI = Undo.AddComponent<TextMeshProUGUI>(go);

            tmpUGUI.text = text;
            tmpUGUI.font = font;
            tmpUGUI.fontSharedMaterial = fontMaterial;
            tmpUGUI.fontSize = fontSize;
            tmpUGUI.autoSizeTextContainer = autoSize;
            tmpUGUI.color = color;
            tmpUGUI.alignment = alignment;
            tmpUGUI.fontStyle = fontStyle;
            tmpUGUI.richText = richText;
            tmpUGUI.raycastTarget = true;
            tmpUGUI.enableWordWrapping = enableWordWrapping;
            tmpUGUI.overflowMode = overflowMode;
            tmpUGUI.lineSpacing = lineSpacing;
            tmpUGUI.characterSpacing = charSpacing;
            tmpUGUI.wordSpacing = wordSpacing;
            tmpUGUI.paragraphSpacing = paraSpacing;

            // TextMeshProUGUI renders via CanvasRenderer, not a MeshRenderer —
            // the 3D TextMeshPro's MeshRenderer/MeshFilter are dead weight now.
            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null) Undo.DestroyObjectImmediate(meshRenderer);

            var meshFilter = go.GetComponent<MeshFilter>();
            if (meshFilter != null) Undo.DestroyObjectImmediate(meshFilter);

            EditorUtility.SetDirty(go);
            Debug.Log($"✓ [{go.name}] TextMeshPro → TextMeshProUGUI", go);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"✗ [{go.name}] TextMeshPro conversion failed: {ex.Message}", go);
        }
    }

    private static void ConvertTk2dUIScrollbarToSlider(GameObject go)
    {
        var scrollbar = go.GetComponent<tk2dUIScrollbar>();
        if (scrollbar == null) return;
        if (go.GetComponent<Slider>() != null) return;

        var slider = Undo.AddComponent<Slider>(go);

        slider.direction = scrollbar.scrollAxes == tk2dUIScrollbar.Axes.XAxis
            ? Slider.Direction.LeftToRight
            : Slider.Direction.BottomToTop;

        slider.minValue = 0;
        slider.maxValue = 1;
        slider.value = Mathf.Clamp01(scrollbar.Value);
        slider.wholeNumbers = false;
        slider.interactable = true;

        var image = go.GetComponent<Image>();
        if (image != null)
        {
            image.raycastTarget = true;
        }

        if (scrollbar.thumbTransform != null && scrollbar.thumbTransform != go.transform)
        {
            var handleImage = scrollbar.thumbTransform.GetComponent<Image>();
            if (handleImage != null)
                slider.handleRect = scrollbar.thumbTransform as RectTransform;
        }

        Debug.Log($"[Scrollbar] Converted '{go.name}' to Slider", go);
    }

    private static void RemoveLegacyTk2dUIComponents(GameObject go)
    {
        var uiItem = go.GetComponent<tk2dUIItem>();
        var toggleComp = go.GetComponent<tk2dUIToggleButton>();

        // A toggle also has a tk2dUIItem underneath (used to detect the
        // click); in that case we treat it as a Toggle, not a plain Button.
        bool isToggle = toggleComp != null;
        bool isButton = uiItem != null && !isToggle;

        // Capture the SendMessage data BEFORE destroying the components —
        // this preserves the same click/toggle logic tk2d used, now fired
        // via UnityEvent (Button.onClick / Toggle.onValueChanged).
        GameObject sendTarget = uiItem != null ? uiItem.sendMessageTarget : null;
        string sendMethod = uiItem != null
            ? (!string.IsNullOrEmpty(uiItem.SendMessageOnReleaseMethodName)
                ? uiItem.SendMessageOnReleaseMethodName
                : uiItem.SendMessageOnClickMethodName)
            : null;

        GameObject toggleOnStateGO = isToggle ? toggleComp.onStateGO : null;
        bool toggleIsOn = isToggle && toggleComp.IsOn;
        GameObject toggleSendTarget = isToggle ? toggleComp.SendMessageTarget : null;
        string toggleSendMethod = isToggle ? toggleComp.SendMessageOnToggleMethodName : null;

        var components = go.GetComponents<Component>();
        var componentsToRemove = new List<Component>();

        foreach (var comp in components)
        {
            if (comp == null) continue;

            string compTypeName = comp.GetType().Name;
            if (!compTypeName.StartsWith("tk2dUI")) continue;

            if (Tk2dUITypeNamesToPreserve.Contains(compTypeName))
            {
                Debug.LogWarning($"[Tk2dConverter]   Preserving '{compTypeName}' on '{go.name}' — it's on the exclusion list (gameplay logic, not a tk2d visual).", go);
                continue;
            }

            componentsToRemove.Add(comp);
        }

        Debug.Log($"[Tk2dConverter]   '{go.name}': removing {componentsToRemove.Count} tk2dUI component(s)...");

        foreach (var comp in componentsToRemove)
        {
            if (comp == null) continue;

            string compTypeName = comp.GetType().Name;
            try
            {
                Undo.DestroyObjectImmediate(comp);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Tk2dConverter]   ✗ Failed to remove '{compTypeName}' from '{go.name}': {ex.Message}", go);
            }
        }

        if (isToggle)
        {
            ConfigureToggle(go, toggleOnStateGO, toggleIsOn, toggleSendTarget, toggleSendMethod);
        }
        else if (isButton)
        {
            ConfigureButtonWithAnimation(go, sendTarget, sendMethod);
        }
    }

    private static void ConfigureToggle(GameObject go, GameObject onStateGO, bool isOn, GameObject sendTarget, string sendMethod)
    {
        Debug.Log($"[Tk2dConverter]   '{go.name}': configuring Toggle...");

        var toggle = go.GetComponent<Toggle>();
        if (toggle == null)
            toggle = Undo.AddComponent<Toggle>(go);

        var background = go.GetComponent<Image>();
        if (background != null)
        {
            toggle.targetGraphic = background;
            background.raycastTarget = true;
        }

        if (onStateGO != null)
        {
            var checkGraphic = onStateGO.GetComponent<Image>();
            if (checkGraphic != null)
                toggle.graphic = checkGraphic;
        }

        toggle.isOn = isOn;

        if (sendTarget != null && !string.IsNullOrEmpty(sendMethod))
        {
            var relay = go.GetComponent<UIEventRelay>();
            if (relay == null) relay = Undo.AddComponent<UIEventRelay>(go);
            relay.Target = sendTarget;
            relay.MethodName = sendMethod;

            UnityEventTools.AddPersistentListener(toggle.onValueChanged, relay.InvokeBool);
            Debug.Log($"[Tk2dConverter]   '{go.name}': onValueChanged → SendMessage('{sendMethod}', bool) on '{sendTarget.name}' (via relay)", go);
        }
        else
        {
            Debug.LogWarning($"[Tk2dConverter]   '{go.name}': Toggle has no SendMessage target/method — onValueChanged was not wired. Configure it manually.", go);
        }
    }

    private static void ConfigureButtonWithAnimation(GameObject go, GameObject sendTarget, string sendMethod)
    {
        Debug.Log($"[Tk2dConverter]   '{go.name}': configuring Button with Animation...");

        var button = go.GetComponent<Button>();
        if (button == null)
        {
            button = Undo.AddComponent<Button>(go);
        }

        var image = go.GetComponent<Image>();
        if (image != null)
        {
            button.targetGraphic = image;
            image.raycastTarget = true;
        }

        button.transition = Selectable.Transition.Animation;

        var animator = go.GetComponent<Animator>();
        if (animator == null)
        {
            animator = Undo.AddComponent<Animator>(go);
        }

        var buttonController = FindButtonAnimatorController();

        if (buttonController != null)
        {
            animator.runtimeAnimatorController = buttonController;
            Debug.Log($"✓ [{go.name}] Button configured with '{buttonController.name}' AnimatorController", go);
        }
        else
        {
            // Log only — an EditorUtility.DisplayDialog here is modal and
            // interrupts/reenters the batch conversion loop, causing it to
            // appear to "stop halfway" when several buttons are selected.
            Debug.LogWarning($"✗ [{go.name}] No 'Button' AnimatorController found in the project. " +
                           "Create one via Assets > Create > Animator Controller and assign it to the Animator manually.", go);

            if (!_missingButtonControllerWarned)
            {
                _missingButtonControllerWarned = true;
                EditorApplication.delayCall += () =>
                {
                    EditorUtility.DisplayDialog(
                        "AnimatorController not found",
                        "No AnimatorController named 'Button' was found in the project.\n\n" +
                        "One or more converted buttons ended up without an animation assigned " +
                        "(see the Console warnings for the full list).\n\n" +
                        "Create an Animator Controller (Assets > Create > Animator Controller), " +
                        "name it with 'Button' in the name, define the Normal/Highlighted/" +
                        "Pressed/Disabled states, and assign it manually to each button's Animator component.",
                        "OK");
                };
            }
        }

        if (sendTarget != null && !string.IsNullOrEmpty(sendMethod))
        {
            var relay = go.GetComponent<UIEventRelay>();
            if (relay == null) relay = Undo.AddComponent<UIEventRelay>(go);
            relay.Target = sendTarget;
            relay.MethodName = sendMethod;

            UnityEventTools.AddPersistentListener(button.onClick, relay.Invoke);
            Debug.Log($"[Tk2dConverter]   '{go.name}': onClick → SendMessage('{sendMethod}') on '{sendTarget.name}' (via relay)", go);
        }
        else
        {
            Debug.LogWarning($"[Tk2dConverter]   '{go.name}': no SendMessage target/method — onClick was not wired. Configure it manually.", go);
        }
    }

    private static RuntimeAnimatorController FindButtonAnimatorController()
    {
        string[] guids = AssetDatabase.FindAssets("Button t:AnimatorController");

        if (guids.Length == 0)
            return null;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            if (controller != null)
            {
                Debug.Log($"[Button] Found AnimatorController: {path}");
                return controller;
            }
        }

        return null;
    }

    internal static void EnsureCanvasParent(GameObject go)
    {
        if (go.GetComponentInParent<Canvas>() != null) return;

        var canvasGO = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.transform.SetParent(go.transform.parent, false);
        canvasGO.transform.SetSiblingIndex(go.transform.GetSiblingIndex());
        go.transform.SetParent(canvasGO.transform, true);
        Undo.RegisterCreatedObjectUndo(canvasGO, "Create Canvas");
    }

    private static void EnsureRectTransform(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt != null) return;

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
    }

    private static void SetupCanvasSorting(GameObject go, int sortingOrder)
    {
        if (sortingOrder == 0) return;

        var canvas = go.GetComponentInParent<Canvas>();
        if (canvas == null) return;

        if (canvas.sortingOrder == 0)
        {
            canvas.sortingOrder = sortingOrder;
        }
        else
        {
            var childCanvas = go.AddComponent<Canvas>();
            childCanvas.overrideSorting = true;
            childCanvas.sortingOrder = sortingOrder;
            if (go.GetComponent<CanvasScaler>() == null)
                go.AddComponent<CanvasScaler>();
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();
        }
    }
}
