using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Mechanically migrates the field DECLARATIONS of scripts that reference
/// tk2d components (tk2dUIItem, tk2dSprite, TextMeshPro, etc.) to the
/// uGUI/native equivalents already used by Tk2dImageConverter /
/// Tk2dSpriteRendererConverter. Only rewrites the type TOKEN — never touches
/// method bodies, event wiring, or property access; those cases are only
/// reported for manual review (see ManualReviewPatterns).
/// </summary>
public static class Tk2dScriptFieldMigrator
{
    // Default scan root. Override this constant for your project's script
    // folder, or point it at "Assets" to scan everything (the exclusion list
    // below still keeps third-party/vendor folders out).
    private const string DefaultRoot = "Assets";

    private static readonly string[] ExcludedPathPrefixes =
    {
        "Assets/TK2DROOT/",
        "Assets/Plugins/",
    };

    // tk2dSprite* types have TWO possible destinations (Image or
    // SpriteRenderer, depending on which converter ran on the referenced
    // GameObject) — they're resolved dynamically via
    // ResolveSpriteFieldTargetType, not through this static table.
    private static readonly Dictionary<string, string> StaticTypeMap = new Dictionary<string, string>
    {
        { "tk2dUIToggleControl", "Toggle" },
        { "tk2dUIToggleButton", "Toggle" },
        { "tk2dUIHoverDisabledItem", "Button" },
        { "tk2dUIItem", "Button" },
        { "tk2dUIScrollbar", "Slider" },
        { "TextMeshPro", "TextMeshProUGUI" },
    };

    private static readonly string[] SpriteTypeTokens =
    {
        "tk2dSlicedSprite", "tk2dClippedSprite", "tk2dTiledSprite", "tk2dBaseSprite", "tk2dSprite",
    };

    private static readonly HashSet<string> UsingUIRequiredTargets = new HashSet<string> { "Image", "Button", "Toggle", "Slider" };

    private static readonly (Regex Pattern, string Hint)[] ManualReviewPatterns =
    {
        (new Regex(@"\.On(Release|Click|Toggle|Press|Scroll)\w*\s*[+\-]="),
            "tk2d C# event subscription (+=/-=) — rewire manually to Button.onClick.AddListener / Toggle.onValueChanged.AddListener / Slider.onValueChanged.AddListener."),
        (new Regex(@"\.IsOn\b"),
            "tk2dUIToggleButton/tk2dUIToggleControl.IsOn — Toggle uses .isOn (lowercase) + SetIsOnWithoutNotify() to avoid re-firing events."),
        (new Regex(@"\.Value\b"),
            "tk2dUIScrollbar.Value — Slider uses .value (lowercase)."),
        (new Regex(@"\.SelectedIndex\b"),
            "tk2d toggle-group SelectedIndex — no direct Toggle/ToggleGroup equivalent; use a Toggle[] + index-scan helper instead."),
        (new Regex(@"\.Enabled\b"),
            "tk2dUIItem/tk2dUIHoverDisabledItem.Enabled — Button/Selectable uses .interactable."),
        (new Regex(@"\.SetSprite\s*\("),
            "tk2dSprite.SetSprite(name) — no Image/SpriteRenderer equivalent; requires resolving a Sprite asset and assigning .sprite manually."),
        (new Regex(@"GetComponent(?:InChildren|InParent)?<(tk2dUI\w+|tk2dSprite|tk2dSlicedSprite|tk2dClippedSprite|tk2dTiledSprite|tk2dBaseSprite|tk2dSpriteAnimator)>\s*\("),
            "GetComponent<tk2dXxx>() call site — update the generic argument to the uGUI/native equivalent (tk2dSpriteAnimator has no 1:1 mapping)."),
        (new Regex(@"\(\s*tk2dUI\w+\s+\w+\s*\)"),
            "Method parameter typed as a tk2d type (e.g. (tk2dUIItem obj)) — UIEventRelay.Invoke()/InvokeBool(bool) can only call 0-arg or 1-bool-arg methods; change the signature manually."),
    };

    [MenuItem("Tools/Seven Sails/Migrate tk2d Script Fields → uGUI (Dry Run Report)", false, 200)]
    private static void DryRunReport() => RunScan(apply: false);

    [MenuItem("Tools/Seven Sails/Migrate tk2d Script Fields → uGUI (Apply)", false, 201)]
    private static void ApplyMigration()
    {
        if (!EditorUtility.DisplayDialog(
                "Apply tk2d Script Field Migration",
                "This will rewrite the TYPE DECLARATION of tk2d-typed fields to their uGUI equivalents, " +
                "directly in .cs files, under " + DefaultRoot + " (except TK2DROOT and Plugins).\n\n" +
                "Method bodies, event wiring, and property access are NOT touched — see the report " +
                "for the manual-review items.\n\n" +
                "This modifies .cs files on disk. Make sure your working tree is committed first. Continue?",
                "Apply", "Cancel"))
            return;

        RunScan(apply: true);
    }

    private class RewrittenField
    {
        public int Line;
        public string OldType;
        public string NewType;
    }

    private class ManualReviewEntry
    {
        public int Line;
        public string MatchedText;
        public string Hint;
    }

    private static void RunScan(bool apply)
    {
        var files = EnumerateTargetFiles(DefaultRoot).ToList();

        var rewritten = new Dictionary<string, List<RewrittenField>>();
        var manualReview = new Dictionary<string, List<ManualReviewEntry>>();
        int filesChanged = 0;
        int fieldsRewritten = 0;

        foreach (var path in files)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Tk2dScriptFieldMigrator] Failed to read '{path}': {ex.Message}");
                continue;
            }

            bool fileChanged = false;
            var fileRewrites = new List<RewrittenField>();
            var fileManualReview = new List<ManualReviewEntry>();

            System.Type ownerType = ResolveScriptType(path);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                string newLine = TryRewriteFieldDeclaration(line, ownerType, out string oldType, out string newType);
                if (newLine != line)
                {
                    lines[i] = newLine;
                    fileChanged = true;
                    fileRewrites.Add(new RewrittenField { Line = i + 1, OldType = oldType, NewType = newType });
                    fieldsRewritten++;
                }

                foreach (var (pattern, hint) in ManualReviewPatterns)
                {
                    var m = pattern.Match(line);
                    if (m.Success)
                    {
                        fileManualReview.Add(new ManualReviewEntry { Line = i + 1, MatchedText = m.Value.Trim(), Hint = hint });
                    }
                }
            }

            if (fileChanged)
            {
                EnsureRequiredUsings(lines, fileRewrites, out lines);

                if (apply)
                {
                    try
                    {
                        File.WriteAllLines(path, lines);
                        filesChanged++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Tk2dScriptFieldMigrator] Failed to write '{path}': {ex.Message}");
                    }
                }
                else
                {
                    filesChanged++;
                }

                rewritten[path] = fileRewrites;
            }

            if (fileManualReview.Count > 0)
                manualReview[path] = fileManualReview;
        }

        string reportPath = WriteReport(rewritten, manualReview, apply);

        Debug.Log($"[Tk2dScriptFieldMigrator] {(apply ? "Apply" : "Dry Run")} finished. " +
                  $"{files.Count} file(s) scanned, {filesChanged} file(s) {(apply ? "changed" : "would be changed")}, " +
                  $"{fieldsRewritten} field(s) {(apply ? "rewritten" : "would be rewritten")}, " +
                  $"{manualReview.Values.Sum(v => v.Count)} manual-review line(s). Report: {reportPath}");

        if (apply)
            AssetDatabase.Refresh();
    }

    private static System.Type ResolveScriptType(string path)
    {
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
        return script != null ? script.GetClass() : null;
    }

    private static IEnumerable<string> EnumerateTargetFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;

        // The tool's own Editor/ folder is skipped implicitly because none of
        // these files declare a field of a tk2d/TextMeshPro type.
        foreach (var path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            string normalized = path.Replace('\\', '/');
            if (ExcludedPathPrefixes.Any(p => normalized.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                continue;

            yield return normalized;
        }
    }

    private static readonly Regex FieldDeclRegex = BuildFieldDeclRegex();

    private static Regex BuildFieldDeclRegex()
    {
        var allTypes = StaticTypeMap.Keys.Concat(SpriteTypeTokens)
            .OrderByDescending(t => t.Length)
            .Select(Regex.Escape);

        string typeAlternation = string.Join("|", allTypes);

        string pattern =
            @"^(?<prefix>\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal|static|readonly|\s)+)" +
            @"(?<type>" + typeAlternation + @")\b(?<array>\s*\[\s*\])?" +
            @"\s+(?<rest>[A-Za-z_][A-Za-z0-9_]*\s*(?:=\s*[^;]+)?;.*)$";

        return new Regex(pattern, RegexOptions.Compiled);
    }

    private static string TryRewriteFieldDeclaration(string line, System.Type ownerType, out string oldType, out string newType)
    {
        oldType = newType = null;

        // Skips using directives and full-line comments — avoids a false
        // positive inside "using TMPro;" or a fully commented-out line.
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("using ") || trimmed.StartsWith("//"))
            return line;

        var m = FieldDeclRegex.Match(line);
        if (!m.Success) return line;

        string tk2dType = m.Groups["type"].Value;
        string fieldName = ExtractFieldName(m.Groups["rest"].Value);

        string resolvedType;
        if (SpriteTypeTokens.Contains(tk2dType))
        {
            resolvedType = ResolveSpriteFieldTargetType(ownerType, fieldName, out bool wasResolved);
            if (!wasResolved)
            {
                // Could not confirm Image vs SpriteRenderer from the real
                // serialized reference — defaults to Image (this tool's
                // historical default) but this case is not separately
                // flagged in ManualReviewPatterns; see the "Known
                // Limitations" section of the generated report.
            }
        }
        else if (!StaticTypeMap.TryGetValue(tk2dType, out resolvedType))
        {
            return line;
        }

        oldType = tk2dType;
        newType = resolvedType;

        return line.Substring(0, m.Groups["type"].Index) + resolvedType
             + line.Substring(m.Groups["type"].Index + tk2dType.Length);
    }

    private static string ExtractFieldName(string rest)
    {
        var m = Regex.Match(rest, @"^([A-Za-z_][A-Za-z0-9_]*)");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Tries to determine whether a tk2dSprite*-typed field should become
    /// Image (UI) or SpriteRenderer (world-space) by looking up the real
    /// serialized reference in the project's prefabs (scenes are not opened —
    /// switching the active scene mid-batch-scan would be too disruptive). If
    /// no answer is found, falls back to the historical default (Image) and
    /// reports wasResolved=false.
    /// </summary>
    private static string ResolveSpriteFieldTargetType(System.Type ownerType, string fieldName, out bool wasResolved)
    {
        wasResolved = false;
        if (ownerType == null || string.IsNullOrEmpty(fieldName))
            return "Image";

        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");

        foreach (var guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject root = null;

            try
            {
                root = PrefabUtility.LoadPrefabContents(prefabPath);
                if (root == null) continue;

                var components = root.GetComponentsInChildren(ownerType, true);
                foreach (var comp in components)
                {
                    var so = new SerializedObject(comp);
                    var prop = so.FindProperty(fieldName);
                    if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    var refObj = prop.objectReferenceValue;
                    if (refObj == null) continue;

                    GameObject targetGO = (refObj as Component)?.gameObject ?? refObj as GameObject;
                    if (targetGO == null) continue;

                    if (targetGO.GetComponent<UnityEngine.UI.Image>() != null)
                    {
                        wasResolved = true;
                        return "Image";
                    }

                    if (targetGO.GetComponent<SpriteRenderer>() != null)
                    {
                        wasResolved = true;
                        return "SpriteRenderer";
                    }
                }
            }
            catch
            {
                // Invalid/corrupted prefab — ignore and continue with the next one.
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }

        return "Image";
    }

    private static void EnsureRequiredUsings(string[] lines, List<RewrittenField> rewrites, out string[] result)
    {
        bool needsUI = rewrites.Any(r => UsingUIRequiredTargets.Contains(r.NewType));
        bool needsTMP = rewrites.Any(r => r.NewType == "TextMeshProUGUI");

        bool hasUI = lines.Any(l => Regex.IsMatch(l, @"^\s*using\s+UnityEngine\.UI\s*;"));
        bool hasTMP = lines.Any(l => Regex.IsMatch(l, @"^\s*using\s+TMPro\s*;"));

        var list = new List<string>(lines);
        int lastUsingIndex = -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (Regex.IsMatch(list[i], @"^\s*using\s+[\w.]+\s*;"))
                lastUsingIndex = i;
        }

        int insertAt = lastUsingIndex + 1;

        if (needsTMP && !hasTMP)
        {
            list.Insert(insertAt, "using TMPro;");
            insertAt++;
        }

        if (needsUI && !hasUI)
        {
            list.Insert(insertAt, "using UnityEngine.UI;");
        }

        result = list.ToArray();
    }

    private static string WriteReport(
        Dictionary<string, List<RewrittenField>> rewritten,
        Dictionary<string, List<ManualReviewEntry>> manualReview,
        bool applied)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Tk2d Script Field Migration Report");
        sb.AppendLine();
        sb.AppendLine($"Mode: {(applied ? "APPLY (files written)" : "DRY RUN (no files changed)")}");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Files with rewritten fields: {rewritten.Count}");
        sb.AppendLine($"Total fields rewritten: {rewritten.Values.Sum(v => v.Count)}");
        sb.AppendLine($"Files with manual-review items: {manualReview.Count}");
        sb.AppendLine($"Total manual-review lines: {manualReview.Values.Sum(v => v.Count)}");
        sb.AppendLine();

        sb.AppendLine("## Rewritten Fields");
        sb.AppendLine();
        foreach (var kv in rewritten.OrderBy(k => k.Key))
        {
            sb.AppendLine($"### {kv.Key}");
            foreach (var f in kv.Value)
                sb.AppendLine($"- line {f.Line}: `{f.OldType}` → `{f.NewType}`");
            sb.AppendLine();
        }

        sb.AppendLine("## Manual Review Required");
        sb.AppendLine();
        foreach (var kv in manualReview.OrderBy(k => k.Key))
        {
            sb.AppendLine($"### {kv.Key}");
            foreach (var e in kv.Value)
                sb.AppendLine($"- line {e.Line}: `{e.MatchedText}` — {e.Hint}");
            sb.AppendLine();
        }

        var signatureHits = manualReview.Values.SelectMany(v => v)
            .Where(e => e.Hint.Contains("UIEventRelay"))
            .ToList();

        sb.AppendLine("## Methods Needing Signature Change");
        sb.AppendLine();
        if (signatureHits.Count == 0)
        {
            sb.AppendLine("(none detected)");
        }
        else
        {
            foreach (var kv in manualReview.OrderBy(k => k.Key))
            {
                var hits = kv.Value.Where(e => e.Hint.Contains("UIEventRelay")).ToList();
                foreach (var e in hits)
                    sb.AppendLine($"- {kv.Key}:{e.Line}: `{e.MatchedText}`");
            }
        }
        sb.AppendLine();

        sb.AppendLine("## Known Limitations");
        sb.AppendLine();
        sb.AppendLine("1. Multi-line field declarations are not matched.");
        sb.AppendLine("2. Generic/nested tk2d types (`List<tk2dSprite>`) are not detected.");
        sb.AppendLine("3. Trailing-comment/string false-negative edge cases aren't fully guarded without a real parser.");
        sb.AppendLine("4. **Inspector references become Missing/None after Apply** — changing a field's declared " +
                       "type doesn't remap Unity's serialized object reference in any scene/prefab that had it " +
                       "assigned. Every scene/prefab using a migrated field must be manually reopened and have the " +
                       "correct component re-dragged in. Run the visual converters (Tk2dImageConverter / " +
                       "Tk2dSpriteRendererConverter) FIRST, then this migrator, then re-verify wiring per scene.");
        sb.AppendLine("5. Image vs SpriteRenderer resolution for tk2dSprite* fields only searches PREFABS " +
                       "(scene-only usages are not resolved, to avoid disruptively opening scenes during a batch " +
                       "scan) — unresolved fields default to `Image`.");
        sb.AppendLine("6. `tk2dUIToggleButtonGroup`, `tk2dSpriteAnimator`, `tk2dUIDragItemGrid`-style custom " +
                       "drag-and-drop classes have no auto-rewrite mapping — left untouched, only surfaced via " +
                       "manual-review detection where applicable.");

        string dir = "Logs";
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"Tk2dScriptFieldMigrationReport_{DateTime.Now:yyyyMMdd_HHmmss}.md");
        File.WriteAllText(path, sb.ToString());

        return path;
    }
}
