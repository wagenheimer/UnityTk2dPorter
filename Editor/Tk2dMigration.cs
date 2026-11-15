using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Stable, structured entry points for automation (agents, CI, batch tools).
///
/// The menu items remain the interactive path; this class is the programmatic
/// one, so a script can drive the whole migration without depending on menu
/// labels or editor selection state:
///
/// <code>
/// Tk2dMigration.Audit();
/// Tk2dMigration.GenerateLibraries();
/// var snapshot = Tk2dMigration.SnapshotReferences();
/// var summary  = Tk2dMigration.ConvertPrefab("Assets/.../Card.prefab");
/// var remap    = Tk2dMigration.RemapReferences(snapshot);
/// </code>
/// </summary>
public static class Tk2dMigration
{
    public enum Mode
    {
        /// <summary>Content already under a Canvas becomes uGUI; everything else becomes SpriteRenderer.</summary>
        Auto,
        /// <summary>Force uGUI Image/Button/Toggle/Slider conversion.</summary>
        UI,
        /// <summary>Force native SpriteRenderer conversion.</summary>
        SpriteRenderer,
    }

    public class ConversionSummary
    {
        public int converted;
        public int tk2dRemaining;
        public string[] roots;
        public string prefabPath;
        public string log;
    }

    /// <summary>Converts the current selection (auto-detect by default).</summary>
    public static ConversionSummary ConvertSelection(Mode mode = Mode.Auto)
    {
        var selected = Selection.gameObjects;
        if (selected == null || selected.Length == 0)
        {
            Debug.LogWarning("[Tk2dMigration] Nothing selected.");
            return null;
        }

        var summary = new ConversionSummary { roots = selected.Select(g => g.name).ToArray() };

        var uiPrewarm = new List<tk2dBaseSprite>();
        var worldPrewarm = new List<tk2dBaseSprite>();

        foreach (var go in selected)
        {
            bool isUI = mode == Mode.UI || (mode == Mode.Auto && go.GetComponentInParent<Canvas>() != null);
            var target = isUI ? uiPrewarm : worldPrewarm;
            target.AddRange(go.GetComponentsInChildren<tk2dBaseSprite>(true));
        }

        Tk2dImageConverter.BeginCanvasBatch();
        Tk2dSpriteAssetUtility.Prewarm(uiPrewarm, 0f, 100f);
        Tk2dSpriteAssetUtility.Prewarm(worldPrewarm, 1f, 1f);

        var convertedObjects = new List<GameObject>();
        var log = new StringBuilder();
        int count = 0;

        foreach (var go in selected)
        {
            bool isUI = mode == Mode.UI || (mode == Mode.Auto && go.GetComponentInParent<Canvas>() != null);

            if (isUI)
            {
                Tk2dImageConverter.EnsureCanvasParent(go);
                Tk2dImageConverter.ConvertRecursive(go, convertedObjects, log, ref count);
                Tk2dImageConverter.PurgeOrphanedTk2dUIComponents(go);
            }
            else
            {
                Tk2dSpriteRendererConverter.ConvertRecursive(go, convertedObjects, log, ref count);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Tk2dImageConverter.RefreshCreatedCanvases();

        summary.converted = count;
        summary.tk2dRemaining = selected.Sum(g => g.GetComponentsInChildren<tk2dBaseSprite>(true).Length);
        summary.log = log.ToString();

        Debug.Log($"[Tk2dMigration] Converted {count} sprite(s); {summary.tk2dRemaining} tk2d sprite(s) remain in the selection.\n{summary.log}");
        return summary;
    }

    /// <summary>Opens a prefab in Prefab Mode, converts it, saves it and returns the summary.</summary>
    public static ConversionSummary ConvertPrefab(string assetPath, Mode mode = Mode.Auto)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (asset == null)
        {
            Debug.LogError($"[Tk2dMigration] Prefab not found: {assetPath}");
            return null;
        }

        AssetDatabase.OpenAsset(asset);
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage == null)
        {
            Debug.LogError($"[Tk2dMigration] Could not open Prefab Mode for {assetPath}");
            return null;
        }

        Selection.objects = new Object[] { stage.prefabContentsRoot };
        var summary = ConvertSelection(mode);

        PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath);
        AssetDatabase.SaveAssets();

        if (summary != null)
            summary.prefabPath = stage.assetPath;

        Debug.Log($"[Tk2dMigration] Saved prefab {assetPath} (converted {summary?.converted ?? 0}).");
        return summary;
    }

    /// <summary>Read-only project inventory; returns the Markdown report path.</summary>
    public static string Audit() => Tk2dAudit.Audit();

    /// <summary>Generates Resources/SpriteLibrary.asset and Resources/AnimationLibrary.asset.</summary>
    public static string GenerateLibraries() => Tk2dLibraryGenerator.Generate();

    /// <summary>Snapshots tk2d references of the selection/open scene; returns the JSON path.</summary>
    public static string SnapshotReferences() => Tk2dReferenceRemapper.Snapshot();

    /// <summary>Remaps the migrated fields from a snapshot (latest for the open scene when null).</summary>
    public static Tk2dReferenceRemapper.RemapSummary RemapReferences(string snapshotPath = null)
        => Tk2dReferenceRemapper.Remap(snapshotPath);
}
