using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Wagenheimer.Tk2dPorter;

/// <summary>
/// Generates the runtime assets that replace tk2d's name-based lookups:
///   Resources/SpriteLibrary.asset     (tk2d sprite name → Sprite) → SetSprite(name)
///   Resources/AnimationLibrary.asset  (tk2d clip name → frames/fps) → Play(clip)
///
/// Run it BEFORE deleting tk2d: it reads every tk2d collection/animation in the
/// project and bakes their names into native assets. Paths and policies live in
/// Tools → Wagenheimer → Tk2d Porter → Settings.
/// </summary>
public static class Tk2dLibraryGenerator
{
    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Libraries/Generate Sprite + Animation Libraries", false, 450)]
    private static void GenerateMenu()
    {
        Generate();
    }

    public static string Generate()
    {
        var settings = Tk2dPorterSettings.Instance;
        var report = new StringBuilder();
        var conflicts = new List<string>();

        if (!IsInResourcesFolder(settings.spriteLibraryPath))
            report.AppendLine($"WARNING: '{settings.spriteLibraryPath}' is not under a Resources folder — " +
                              "the SpriteLibrary will not be loadable at runtime.");
        if (!IsInResourcesFolder(settings.animationLibraryPath))
            report.AppendLine($"WARNING: '{settings.animationLibraryPath}' is not under a Resources folder — " +
                              "the AnimationLibrary will not be loadable at runtime.");

        var spriteMap = new Dictionary<string, Sprite>();
        var animations = new List<tk2dSpriteAnimation>();

        // One pass over the prefabs: tk2d collections and animations are
        // MonoBehaviours on prefabs, not ScriptableObjects, so "t:" filters
        // cannot find them.
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var collection = prefab.GetComponent<tk2dSpriteCollectionData>();
            if (collection != null)
                AddCollection(collection, spriteMap, conflicts);

            var animation = prefab.GetComponent<tk2dSpriteAnimation>();
            if (animation != null)
                animations.Add(animation);
        }

        // ---- SpriteLibrary ----
        var spriteLibrary = LoadOrCreate<SpriteLibrary>(settings.spriteLibraryPath);
        spriteLibrary.sprites = spriteMap.Values.OrderBy(s => s.name).ToArray();

        // The authoritative map is tk2d NAME -> Sprite: duplicate definitions
        // get suffixes ("club_2 1") that the sprite asset name does not carry.
        spriteLibrary.entries = spriteMap
            .OrderBy(kv => kv.Key)
            .Select(kv => new SpriteEntry { name = kv.Key, sprite = kv.Value })
            .ToArray();

        EditorUtility.SetDirty(spriteLibrary);

        // ---- AnimationLibrary ----
        var clips = new List<SpriteClip>();
        foreach (var animation in animations)
        {
            if (animation.clips == null) continue;

            foreach (var clip in animation.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.name)) continue;

                var frames = new List<Sprite>();
                foreach (var frame in clip.frames)
                {
                    if (frame == null || frame.spriteCollection == null) continue;
                    if (frame.spriteId < 0 || frame.spriteId >= frame.spriteCollection.spriteDefinitions.Length)
                        continue;

                    var def = frame.spriteCollection.spriteDefinitions[frame.spriteId];
                    if (def == null) continue;

                    if (spriteMap.TryGetValue(def.name, out var sprite) && sprite != null)
                        frames.Add(sprite);
                    else
                        report.AppendLine($"Clip '{clip.name}': sprite '{def.name}' is not in the library.");
                }

                clips.Add(new SpriteClip
                {
                    name = clip.name,
                    fps = clip.fps > 0f ? clip.fps : 30f,
                    loop = clip.wrapMode == tk2dSpriteAnimationClip.WrapMode.Loop,
                    frames = frames.ToArray()
                });
            }
        }

        var animationLibrary = LoadOrCreate<AnimationLibrary>(settings.animationLibraryPath);
        animationLibrary.clips = clips.ToArray();
        EditorUtility.SetDirty(animationLibrary);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        report.Insert(0, $"[Tk2dLibraryGenerator] Sprites: {spriteLibrary.sprites.Length}, clips: {clips.Count}\n" +
                         $"  SpriteLibrary: {settings.spriteLibraryPath}\n" +
                         $"  AnimationLibrary: {settings.animationLibraryPath}\n" +
                         $"  Collections scanned, name conflicts: {conflicts.Count}\n" +
                         $"  Clips: {string.Join(", ", clips.Select(c => $"{c.name}({c.frames.Length})"))}\n");

        foreach (var conflict in conflicts.Take(30))
            report.AppendLine("  conflict: " + conflict);

        Debug.Log(report.ToString());
        return report.ToString();
    }

    private static void AddCollection(tk2dSpriteCollectionData collection, Dictionary<string, Sprite> map, List<string> conflicts)
    {
        if (collection.spriteDefinitions == null) return;

        foreach (var def in collection.spriteDefinitions)
        {
            if (def == null || string.IsNullOrEmpty(def.name)) continue;

            var sprite = ResolveSprite(def);
            if (sprite == null) continue;

            if (map.TryGetValue(def.name, out var existing))
            {
                if (existing != sprite)
                    conflicts.Add($"{def.name}: kept '{AssetDatabase.GetAssetPath(existing)}', ignored '{AssetDatabase.GetAssetPath(sprite)}'");
                continue;
            }

            map.Add(def.name, sprite);
        }
    }

    /// <summary>Resolves the native Sprite behind a tk2d definition (source texture, Single or sub-sprite).</summary>
    private static Sprite ResolveSprite(tk2dSpriteDefinition def)
    {
        if (string.IsNullOrEmpty(def.sourceTextureGUID)) return null;

        string texturePath = AssetDatabase.GUIDToAssetPath(def.sourceTextureGUID);
        if (string.IsNullOrEmpty(texturePath)) return null;

        var single = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
        if (single != null) return single;

        foreach (var asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath))
        {
            if (asset is Sprite sprite &&
                (sprite.name == def.name || sprite.name == def.name + "_0"))
                return sprite;
        }

        return null;
    }

    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;

        string directory = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
            CreateFolderRecursive(directory);

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void CreateFolderRecursive(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            CreateFolderRecursive(parent);

        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    private static bool IsInResourcesFolder(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/Resources/");
    }
}
