using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps script references alive across a tk2d → native conversion.
///
/// Destroying a tk2dSprite/tk2dUIItem breaks every serialized field in the
/// project's scripts that pointed at it (they become "Missing"/None), which
/// makes the game throw NullReferenceException even though the visuals were
/// converted correctly. Unity cannot remap an object reference when the field's
/// declared type changes, so the reference must be rebuilt by hand — or by this
/// tool, using a snapshot taken BEFORE the conversion:
///
///   1. Snapshot tk2d References   (before converting)
///   2. Convert the scene/prefab + migrate the script field types + recompile
///   3. Remap from Snapshot        (finds the new Image/Button/Toggle/... on the
///                                  same GameObject and assigns it)
///
/// Entries are keyed by GlobalObjectId, which survives reparenting/renaming
/// (the converted HUD roots are moved under a Canvas, so path-based lookup
/// would not be reliable).
/// </summary>
public static class Tk2dReferenceRemapper
{
    private const string LogFolder = "Logs";

    [Serializable]
    private class RefEntry
    {
        public string ownerGid;            // GlobalObjectId of the MonoBehaviour that owns the field
        public string ownerPath;           // readable, for the report
        public string ownerScript;         // MonoScript asset path
        public string field;
        public string fieldType;           // declared type at snapshot time (tk2d...)
        public string targetGid;           // GlobalObjectId of the referenced GameObject
        public string targetPath;          // readable, for the report
        public string targetType;          // component type at snapshot time (tk2d...)
    }

    [Serializable]
    private class SnapshotData
    {
        public string scene;
        public string scenePath;
        public string createdAt;
        public List<RefEntry> entries = new List<RefEntry>();
    }

    /// <summary>Outcome of a remap pass (also usable from automation).</summary>
    public class RemapSummary
    {
        public int applied;
        public int alreadyOk;
        public int skipped;
        public string reportPath;
        public string snapshotPath;
    }

    // ------------------------------------------------------------------ snapshot

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/References/Snapshot tk2d References (Selection or Open Scene)", false, 300)]
    private static void SnapshotMenuItem()
    {
        Snapshot();
    }

    /// <summary>Records the tk2d references of the selection (or open scene) and returns the snapshot path.</summary>
    public static string Snapshot()
    {
        var scene = GetTargetScene(out var roots);
        if (scene == null) { Debug.LogError("[Tk2dRemapper] No active scene."); return null; }

        var snapshot = new SnapshotData
        {
            scene = scene.name,
            scenePath = scene.path,
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        var skipped = new List<string>();

        foreach (var root in roots)
        {
            if (root == null) continue;

            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;

                var script = MonoScript.FromMonoBehaviour(behaviour);
                if (script == null) continue;

                var serialized = new SerializedObject(behaviour);
                var property = serialized.GetIterator();

                bool enterChildren = true;
                while (property.NextVisible(enterChildren))
                {
                    enterChildren = false;

                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;

                    var field = behaviour.GetType().GetField(
                        property.name,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null) continue;
                    // tk2d components plus TextMeshPro 3D (the UI converter
                    // replaces it with TextMeshProUGUI, which also breaks the
                    // serialized reference).
                    bool isTk2dType = field.FieldType.Name.StartsWith("tk2d");
                    bool isTmp3D = field.FieldType.Name == "TextMeshPro";
                    if (!isTk2dType && !isTmp3D) continue;

                    var target = property.objectReferenceValue as Component;
                    if (target == null)
                    {
                        skipped.Add(Describe(behaviour) + "." + property.name + " (unassigned)");
                        continue;
                    }

                    snapshot.entries.Add(new RefEntry
                    {
                        ownerGid = GlobalObjectId.GetGlobalObjectIdSlow(behaviour).ToString(),
                        ownerPath = GetReadablePath(behaviour.transform),
                        ownerScript = AssetDatabase.GetAssetPath(script),
                        field = property.name,
                        fieldType = field.FieldType.Name,
                        targetGid = GlobalObjectId.GetGlobalObjectIdSlow(target.gameObject).ToString(),
                        targetPath = GetReadablePath(target.transform),
                        targetType = target.GetType().Name
                    });
                }
            }
        }

        string path = Path.Combine(LogFolder,
            $"tk2d-refs-{Sanitize(scene.name)}-{DateTime.Now:yyyyMMdd_HHmmss}.json");
        Directory.CreateDirectory(LogFolder);
        File.WriteAllText(path, JsonUtility.ToJson(snapshot, true));

        Debug.Log($"[Tk2dRemapper] Snapshot saved: {path}\n" +
                  $"{snapshot.entries.Count} tk2d reference(s) recorded, {skipped.Count} unassigned tk2d field(s) ignored.\n" +
                  "Convert the content, migrate the script field types and recompile, then run " +
                  "\"Remap from Latest Snapshot\".");

        if (skipped.Count > 0)
            Debug.Log("[Tk2dRemapper] Unassigned tk2d fields (nothing to remap):\n  " + string.Join("\n  ", skipped.Take(40)));

        return path;
    }

    // --------------------------------------------------------------------- remap

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/References/Remap from Latest Snapshot (Open Scene)", false, 301)]
    private static void RemapLatestMenuItem()
    {
        Remap();
    }

    /// <summary>Path of the newest snapshot for the open scene, or null.</summary>
    public static string LatestSnapshotPath()
    {
        var scene = GetTargetScene(out _);
        if (scene == null) return null;

        if (!Directory.Exists(LogFolder)) return null;

        return Directory.GetFiles(LogFolder, $"tk2d-refs-{Sanitize(scene.name)}-*.json")
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();
    }

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/References/Remap from Snapshot File…", false, 302)]
    private static void RemapFileMenuItem()
    {
        string file = EditorUtility.OpenFilePanel("Select a tk2d reference snapshot", LogFolder, "json");
        if (string.IsNullOrEmpty(file)) return;
        Remap(file);
    }

    public static RemapSummary Remap(string file = null)
    {
        if (string.IsNullOrEmpty(file))
            file = LatestSnapshotPath();

        if (string.IsNullOrEmpty(file))
        {
            Debug.LogError("[Tk2dRemapper] No snapshot found. Run the snapshot BEFORE converting.");
            return null;
        }

        SnapshotData snapshot;
        try
        {
            snapshot = JsonUtility.FromJson<SnapshotData>(File.ReadAllText(file));
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Tk2dRemapper] Could not read '{file}': {ex.Message}");
            return null;
        }

        if (snapshot?.entries == null) return null;

        int applied = 0, alreadyOk = 0, skipped = 0;
        var problems = new List<string>();
        var report = new StringBuilder();
        report.AppendLine("# Tk2d Reference Remap Report");
        report.AppendLine();
        report.AppendLine($"Snapshot: {file}");
        report.AppendLine($"Scene: {snapshot.scene} ({snapshot.scenePath})");
        report.AppendLine($"Applied: {applied} | Already OK: {alreadyOk} | Skipped: {skipped}");
        report.AppendLine();

        Undo.SetCurrentGroupName("Remap tk2d references");
        int group = Undo.GetCurrentGroup();

        foreach (var entry in snapshot.entries)
        {
            var owner = Resolve<MonoBehaviour>(entry.ownerGid);
            if (owner == null)
            {
                skipped++;
                problems.Add($"owner not found: {entry.ownerPath}.{entry.field} ({entry.ownerScript})");
                continue;
            }

            var serialized = new SerializedObject(owner);
            var property = serialized.FindProperty(entry.field);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                skipped++;
                problems.Add($"field missing/not an object reference: {entry.ownerPath}.{entry.field}");
                continue;
            }

            var current = property.objectReferenceValue;
            if (current != null && current.GetType().Name != entry.targetType)
            {
                alreadyOk++; // already pointing at the converted component
                continue;
            }

            var field = owner.GetType().GetField(entry.field,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
            {
                skipped++;
                problems.Add($"field no longer exists: {entry.ownerPath}.{entry.field}");
                continue;
            }

            if (field.FieldType.Name.StartsWith("tk2d") || field.FieldType.Name == "TextMeshPro")
            {
                skipped++;
                problems.Add($"field type is still '{field.FieldType.Name}': {entry.ownerPath}.{entry.field} — " +
                             "run the script field migration and recompile before remapping");
                continue;
            }

            var targetGo = Resolve<GameObject>(entry.targetGid);
            if (targetGo == null)
            {
                skipped++;
                problems.Add($"target not found: {entry.targetPath} (for {entry.ownerPath}.{entry.field})");
                continue;
            }

            Component replacement = FindReplacement(targetGo, entry.targetType, field.FieldType);
            if (replacement == null)
            {
                skipped++;
                problems.Add($"no {DescribeCandidates(entry.targetType)} on '{entry.targetPath}' " +
                             $"for {entry.ownerPath}.{entry.field} (was {entry.targetType})");
                continue;
            }

            Undo.RecordObject(owner, "Remap tk2d reference");
            property.objectReferenceValue = replacement;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(owner);
            applied++;

            report.AppendLine($"- {entry.ownerPath}.{entry.field}: {entry.targetType} → {replacement.GetType().Name} on {entry.targetPath}");
        }

        Undo.CollapseUndoOperations(group);

        foreach (var problem in problems)
            report.AppendLine($"- SKIPPED: {problem}");

        string reportPath = Path.Combine(LogFolder, $"tk2d-refs-remap-{DateTime.Now:yyyyMMdd_HHmmss}.md");
        Directory.CreateDirectory(LogFolder);
        File.WriteAllText(reportPath, report.ToString());

        Debug.Log($"[Tk2dRemapper] Remap finished — applied={applied}, already ok={alreadyOk}, skipped={skipped}. Report: {reportPath}");

        if (skipped > 0)
            Debug.LogWarning($"[Tk2dRemapper] {skipped} reference(s) need attention:\n  " + string.Join("\n  ", problems.Take(40)));

        return new RemapSummary
        {
            applied = applied,
            alreadyOk = alreadyOk,
            skipped = skipped,
            reportPath = reportPath,
            snapshotPath = file
        };
    }

    // ------------------------------------------------------------------- helpers

    private static Component FindReplacement(GameObject target, string oldType, Type newFieldType)
    {
        var candidates = CandidatesFor(oldType);

        // Prefer a component that the field can actually hold.
        foreach (var candidate in candidates)
        {
            var component = target.GetComponents<Component>()
                .FirstOrDefault(c => c != null && c.GetType().Name == candidate);

            if (component != null && newFieldType.IsInstanceOfType(component))
                return component;
        }

        return null;
    }

    private static IEnumerable<string> CandidatesFor(string tk2dType)
    {
        switch (tk2dType)
        {
            case "tk2dUIItem":
            case "tk2dUIHoverDisabledItem":
            case "tk2dUIHoverItem":
            case "tk2dUIBaseItemControl":
                return new[] { "Button", "Toggle", "Slider" };

            case "tk2dUIToggleButton":
            case "tk2dUIToggleControl":
                return new[] { "Toggle", "Button" };

            case "tk2dUIScrollbar":
                return new[] { "Slider" };
        }

        if (tk2dType == "TextMeshPro")
            return new[] { "TextMeshProUGUI" };

        if (tk2dType.StartsWith("tk2dSprite") || tk2dType == "tk2dBaseSprite")
            return new[] { "Image", "SpriteRenderer" };

        return Array.Empty<string>();
    }

    private static string DescribeCandidates(string tk2dType)
        => "[" + string.Join("/", CandidatesFor(tk2dType)) + "]";

    private static T Resolve<T>(string globalObjectId) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(globalObjectId)) return null;

        if (!GlobalObjectId.TryParse(globalObjectId, out var id)) return null;

        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) as T;
    }

    private static Scene GetTargetScene(out GameObject[] roots)
    {
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        var scene = stage != null ? stage.scene : SceneManager.GetActiveScene();

        var selection = Selection.gameObjects
            .Where(g => g != null && g.scene == scene)
            .ToArray();

        roots = selection.Length > 0 ? selection : scene.GetRootGameObjects();

        // When a prefab stage is open, snapshot the prefab contents root.
        if (stage != null && selection.Length == 0)
            roots = new[] { stage.prefabContentsRoot };

        return scene;
    }

    private static string GetReadablePath(Transform transform)
    {
        var parts = new List<string>();
        var t = transform;
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string Describe(MonoBehaviour behaviour)
        => GetReadablePath(behaviour.transform) + " (" + behaviour.GetType().Name + ")";

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
