using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read-only inventory of every tk2d component in the project, so an agent (or
/// a human) can plan a migration instead of discovering blockers halfway.
///
/// Outputs both a Markdown summary and a machine-readable JSON under Logs/.
/// Nothing is modified.
/// </summary>
public static class Tk2dAudit
{
    private class Row
    {
        public string Kind;
        public string Path;
        public Dictionary<string, int> Counts;
    }

    // Types that have no automatic equivalent — these are the blockers an agent
    // must plan for.
    private static readonly string[] UnsupportedTypes =
    {
        "tk2dSpriteAnimator",
        "tk2dUIToggleButtonGroup",
        "tk2dUIDragItemGrid",
        "tk2dUIDragItemClamp",
        "tk2dRadialSprite",
        "tk2dCameraAnchor",
        "tk2dCamera",
        "tk2dUICamera",
        "tk2dUIManager",
        "tk2dUIScrollbar",
    };

    // Demo/vendor content that is not part of the game's migration surface.
    private static readonly string[] IgnorePathPrefixes =
    {
        "Assets/TK2DROOT/",
    };

    [MenuItem("Tools/Wagenheimer/Tk2d Porter/Audit Project (Report)", false, 400)]
    private static void AuditMenu()
    {
        Audit();
    }

    public static string Audit()
    {
        var rows = new List<Row>();

        // ---- prefabs (loaded, so component types are exact) ----
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
            if (IgnorePathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            var counts = prefab.GetComponentsInChildren<MonoBehaviour>(true)
                .Where(c => c != null && c.GetType().Name.StartsWith("tk2d"))
                .GroupBy(c => c.GetType().Name)
                .ToDictionary(g => g.Key, g => g.Count());

            if (counts.Count == 0) continue;

            // Collections/animations are tk2d SOURCE DATA (read by the library
            // generator), not content that gets sprite-converted.
            bool isData = prefab.GetComponent<tk2dSpriteCollectionData>() != null ||
                          prefab.GetComponent<tk2dSpriteAnimation>() != null;

            rows.Add(new Row { Kind = isData ? "data" : "prefab", Path = path, Counts = counts });
        }

        // ---- scenes (text scan: never opens/changes a scene) ----
        var guidMap = BuildTk2dScriptGuidMap();
        var sceneGuids = AssetDatabase.FindAssets("t:Scene");

        foreach (var guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;
            if (IgnorePathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase))) continue;

            string text;
            try { text = File.ReadAllText(path); }
            catch { continue; }

            var counts = new Dictionary<string, int>();
            foreach (var kv in guidMap)
            {
                // Count component INSTANCES (m_Script), not every reference to
                // the script GUID (which would also count e.g. field references
                // and prefab overrides).
                int n = Regex.Matches(text, "m_Script: \\{fileID: 11500000, guid: " + kv.Key).Count;
                if (n > 0) counts[kv.Value] = n;
            }

            if (counts.Count == 0) continue;
            rows.Add(new Row { Kind = "scene", Path = path, Counts = counts });
        }

        // ---- build report ----
        var sb = new StringBuilder();
        sb.AppendLine("# tk2d Audit");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Files with tk2d components: {rows.Count}");
        sb.AppendLine();

        sb.AppendLine("| File | Kind | tk2d components | Blockers |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var row in rows.OrderBy(r => r.Kind).ThenBy(r => r.Path))
        {
            int total = row.Counts.Values.Sum();
            var blockers = row.Counts.Where(kv => UnsupportedTypes.Contains(kv.Key))
                .Select(kv => kv.Key + " x" + kv.Value);
            sb.AppendLine($"| {row.Path} | {row.Kind} | {total} | {string.Join(", ", blockers)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Totals by component type");
        sb.AppendLine();
        foreach (var group in rows.SelectMany(r => r.Counts)
                     .GroupBy(kv => kv.Key)
                     .OrderByDescending(g => g.Sum(kv => kv.Value)))
        {
            bool unsupported = UnsupportedTypes.Contains(group.Key);
            sb.AppendLine($"- `{group.Key}`: {group.Sum(kv => kv.Value)}" + (unsupported ? " **(blocker / needs manual work)**" : ""));
        }
        sb.AppendLine();

        sb.AppendLine("## Suggested order");
        sb.AppendLine();
        sb.AppendLine("1. Snapshot references for each scene/prefab (Tools → Tk2d Porter → References → Snapshot).");
        sb.AppendLine("2. Convert prefabs first (they are referenced by scenes), then scenes.");
        sb.AppendLine("3. Migrate the script field types, fix the reported call sites, recompile.");
        sb.AppendLine("4. Remap the references from the snapshots (per scene/prefab).");
        sb.AppendLine("5. Play-test each scene; compare against a baseline capture.");
        sb.AppendLine();
        sb.AppendLine("Blockers listed above (`tk2dSpriteAnimator`, custom drag classes, radial sprites, camera/anchors)");
        sb.AppendLine("need a bespoke replacement (a native frame animator, a pointer-drag component, Image.Filled/Radial360,");
        sb.AppendLine("and a Canvas with tk2dCameraAnchor removed).");

        Directory.CreateDirectory("Logs");
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string mdPath = Path.Combine("Logs", $"tk2d-audit-{stamp}.md");
        File.WriteAllText(mdPath, sb.ToString());

        string jsonPath = Path.Combine("Logs", $"tk2d-audit-{stamp}.json");
        File.WriteAllText(jsonPath, JsonUtility.ToJson(new AuditJson
        {
            generatedAt = DateTime.Now.ToString("s"),
            files = rows.Select(r => new AuditFile
            {
                kind = r.Kind,
                path = r.Path,
                counts = r.Counts.Select(kv => new AuditCount { type = kv.Key, count = kv.Value }).ToArray()
            }).ToArray()
        }, true));

        int totalComponents = rows.SelectMany(r => r.Counts).Sum(kv => kv.Value);
        int blockerCount = rows.SelectMany(r => r.Counts).Where(kv => UnsupportedTypes.Contains(kv.Key)).Sum(kv => kv.Value);

        Debug.Log($"[Tk2dAudit] {rows.Count} file(s), {totalComponents} tk2d component(s), {blockerCount} blocker(s).\n" +
                  $"Markdown: {mdPath}\nJSON: {jsonPath}");

        return mdPath;
    }

    [Serializable]
    private class AuditJson
    {
        public string generatedAt;
        public AuditFile[] files;
    }

    [Serializable]
    private class AuditFile
    {
        public string kind;
        public string path;
        public AuditCount[] counts;
    }

    [Serializable]
    private class AuditCount
    {
        public string type;
        public int count;
    }

    /// <summary>
    /// Maps the GUID of every tk2d script (file name starts with "tk2d") to its
    /// type name, so scene YAML can be counted without opening the scene.
    /// </summary>
    private static Dictionary<string, string> BuildTk2dScriptGuidMap()
    {
        var map = new Dictionary<string, string>();

        foreach (var metaPath in Directory.GetFiles("Assets", "tk2d*.cs.meta", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(metaPath);           // tk2dSprite.cs.meta
            string typeName = fileName.Substring(0, fileName.Length - ".cs.meta".Length);

            string firstLine;
            try { firstLine = File.ReadLines(metaPath).FirstOrDefault(l => l.StartsWith("guid: ")); }
            catch { continue; }

            if (string.IsNullOrEmpty(firstLine)) continue;

            string guid = firstLine.Substring("guid: ".Length).Trim();
            map[guid] = typeName;
        }

        return map;
    }
}
