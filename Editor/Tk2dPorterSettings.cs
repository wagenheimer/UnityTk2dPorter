using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Project-wide policy for the tk2d converters. A default instance is used when
/// no asset exists, so the tool works out of the box; create the asset to tune
/// the behaviour per project.
/// </summary>
public class Tk2dPorterSettings : ScriptableObject
{
    public enum CanvasScaleMode
    {
        /// <summary>ScaleWithScreenSize matched to height — 1 canvas unit = 1 tk2d world unit, resolution independent.</summary>
        HeightMatch,
        /// <summary>ConstantPixelSize with the tk2d camera's current pixels-per-unit (freezes at the editor resolution).</summary>
        ConstantPixelSize,
    }

    [Header("Sprites")]
    [Tooltip("Force converted textures to Pixels Per Unit 1 and bake the tk2d sprite scale into the transform (tk2d's 1 texel = 1 unit convention).")]
    public bool normalizePixelsPerUnit = true;

    [Tooltip("Keep world-space colliders when converting to SpriteRenderer (custom click/drag code usually queries them).")]
    public bool preserveWorldColliders = true;

    [Tooltip("Disable an unsupported tk2dSpriteAnimator instead of leaving it active (it throws once its tk2dBaseSprite is gone).")]
    public bool disableUnsupportedAnimators = true;

    [Header("UI")]
    [Tooltip("Canvas created when converting world content to uGUI.")]
    public CanvasScaleMode canvasScaleMode = CanvasScaleMode.HeightMatch;

    [Tooltip("Extra 'tk2dUI*' component type names that must never be removed (project-specific gameplay scripts).")]
    public List<string> extraPreservedTk2dUITypeNames = new List<string>();

    [Header("Generated libraries")]
    public string spriteLibraryPath = "Assets/Resources/SpriteLibrary.asset";
    public string animationLibraryPath = "Assets/Resources/AnimationLibrary.asset";

    private static Tk2dPorterSettings _instance;

    public static Tk2dPorterSettings Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var guids = AssetDatabase.FindAssets("t:Tk2dPorterSettings");
            if (guids.Length > 0)
            {
                _instance = AssetDatabase.LoadAssetAtPath<Tk2dPorterSettings>(
                    AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            // Fall back to a transient default so the converters never depend on
            // the project having created the asset.
            if (_instance == null)
                _instance = CreateInstance<Tk2dPorterSettings>();

            return _instance;
        }
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Settings (Create/Select)", false, 500)]
    private static void OpenOrCreate()
    {
        var guids = AssetDatabase.FindAssets("t:Tk2dPorterSettings");
        Tk2dPorterSettings settings = null;

        if (guids.Length > 0)
            settings = AssetDatabase.LoadAssetAtPath<Tk2dPorterSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));

        if (settings == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");

            settings = CreateInstance<Tk2dPorterSettings>();
            AssetDatabase.CreateAsset(settings, "Assets/Settings/Tk2dPorterSettings.asset");
            AssetDatabase.SaveAssets();
            Debug.Log("[Tk2dPorter] Created Assets/Settings/Tk2dPorterSettings.asset");
        }

        _instance = settings;
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}
