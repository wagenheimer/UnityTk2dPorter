using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Sprite conversion utilities shared between the UI converter (Image) and the
/// world-space converter (SpriteRenderer) — nothing here is specific to
/// either destination.
/// </summary>
internal static class Tk2dSpriteAssetUtility
{
    // Textures already warned about (missing/ambiguous) so a batch conversion
    // does not spam the same warning once per sprite.
    private static readonly HashSet<string> WarnedTextures = new HashSet<string>();

    /// <summary>
    /// Finds (or creates) the native Sprite asset for a tk2d sprite definition.
    /// The texture is resolved from the sprite's own collection — never by
    /// searching the whole project for a file with the same name, which could
    /// silently import an unrelated texture as a Sprite and corrupt it.
    /// </summary>
    internal static Sprite FindOrCreateSpriteAsset(tk2dBaseSprite spr, tk2dSpriteDefinition def, bool isSliced, Vector2 borderBL, Vector2 borderTR)
    {
        if (def == null) return null;

        var collection = spr != null ? spr.Collection : null;
        string texPath = ResolveTexturePath(def, collection);
        if (string.IsNullOrEmpty(texPath)) return null;

        TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (importer == null)
        {
            WarnOnce(texPath, $"[Tk2dConverter] '{texPath}' is not a texture asset that can be imported as a Sprite — " +
                               "the sprite definition was skipped. Check the tk2d collection's atlas/texture reference.");
            return null;
        }

        bool isAtlasRegion = def.extractRegion && def.regionW > 0 && def.regionH > 0;

        Vector4 border = Vector4.zero;
        if (isSliced)
            border = CalculateSpriteBorder(def, borderBL, borderTR, texPath);

        // Already converted and up to date? Return it without touching any
        // import setting — reprocessing an already-correct texture on every
        // run is slow and risks corrupting textures shared with other tools.
        if (IsSpriteUpToDate(texPath, def, isAtlasRegion, isSliced, border, importer))
        {
            var existing = LoadSpriteAtPath(texPath, def);
            if (existing != null) return existing;
        }

        bool needsReimport = false;

        // tk2d works in "1 texel = 1 world unit": it bakes its own scale into
        // the mesh instead of using the transform. Native sprites derive their
        // size from pixelsPerUnit, so the texture must use PPU 1 — otherwise a
        // converted SpriteRenderer would need a 100x localScale, and gameplay
        // code that assigns an absolute localScale (CoinBonus sets
        // Vector3.one, LevelBooster sets 0.4, etc.) would render at the wrong
        // size.
        if (importer.textureType != TextureImporterType.Sprite)
            importer.textureType = TextureImporterType.Sprite;

        if (Mathf.Abs(importer.spritePixelsPerUnit - 1f) > 0.01f)
        {
            Debug.Log($"[Tk2dConverter] '{texPath}': Pixels Per Unit {importer.spritePixelsPerUnit:0.##} → 1 " +
                      "(tk2d's 1 texel = 1 unit convention).");
            importer.spritePixelsPerUnit = 1f;
            needsReimport = true;
        }

        // A texture only needs "Multiple" sprite mode when it's an atlas
        // shared by several tk2d definitions (a sub-region is being
        // extracted from it). A texture dedicated to exactly one definition
        // covering the whole image should stay "Single" — that's what a
        // plain, non-atlas source image normally uses, and avoids the
        // confusing "Multiple sprite mode with a single entry" result.
        return isAtlasRegion
            ? ConfigureMultipleSprite(importer, def, texPath, border, isSliced, needsReimport)
            : ConfigureSingleSprite(importer, texPath, border, needsReimport);
    }

    /// <summary>
    /// True when the texture already carries a sprite with this definition's
    /// name, the right import mode, and (for sliced sprites) the right border.
    /// </summary>
    private static bool IsSpriteUpToDate(string texPath, tk2dSpriteDefinition def, bool isAtlasRegion, bool isSliced, Vector4 border, TextureImporter importer)
    {
        if (importer.textureType != TextureImporterType.Sprite) return false;

        // tk2d's 1 texel = 1 unit convention — see FindOrCreateSpriteAsset.
        if (Mathf.Abs(importer.spritePixelsPerUnit - 1f) > 0.01f) return false;

        if (isAtlasRegion)
        {
            if (importer.spriteImportMode != SpriteImportMode.Multiple) return false;

            var existing = LoadSpriteAtPath(texPath, def);
            if (existing == null) return false;
            if (isSliced && !Approximately(existing.border, border)) return false;
            return true;
        }

        if (importer.spriteImportMode != SpriteImportMode.Single) return false;
        if (AssetDatabase.LoadAssetAtPath<Sprite>(texPath) == null) return false;
        if (isSliced && !Approximately(importer.spriteBorder, border)) return false;
        return true;
    }

    /// <summary>
    /// Creates every sprite definition referenced by <paramref name="sprites"/>
    /// in a single reimport per atlas texture. Converting a 100-sprite
    /// hierarchy one sprite at a time reimports the same 2048x2048 texture 100
    /// times (minutes of editor stalls); prewarming reduces that to one
    /// reimport per texture, and the per-sprite path then just reads it back.
    ///
    /// Best-effort: any failure is logged and the regular per-sprite path
    /// still works afterwards.
    /// </summary>
    internal static void Prewarm(IEnumerable<tk2dBaseSprite> sprites)
    {
        if (sprites == null) return;

        var byTexture = new Dictionary<string, Dictionary<string, Tk2dSpriteSheetUtility.RectData>>();

        foreach (var spr in sprites)
        {
            if (spr == null) continue;

            var def = spr.CurrentSprite;
            if (def == null) continue;
            if (!(def.extractRegion && def.regionW > 0 && def.regionH > 0)) continue;

            string texPath = ResolveTexturePath(def, spr.Collection);
            if (string.IsNullOrEmpty(texPath)) continue;

            if (!byTexture.TryGetValue(texPath, out var entries))
            {
                entries = new Dictionary<string, Tk2dSpriteSheetUtility.RectData>();
                byTexture[texPath] = entries;
            }

            if (entries.ContainsKey(def.name)) continue;

            Vector4 border = Vector4.zero;
            if (spr is tk2dSlicedSprite sliced)
            {
                border = CalculateSpriteBorder(
                    def,
                    new Vector2(sliced.borderLeft, sliced.borderBottom),
                    new Vector2(sliced.borderRight, sliced.borderTop),
                    texPath);
            }

            entries[def.name] = new Tk2dSpriteSheetUtility.RectData
            {
                Name = def.name,
                Rect = new Rect(def.regionX, def.regionY, def.regionW, def.regionH),
                Pivot = Vector2.one * 0.5f,
                Alignment = (int)SpriteAlignment.Center,
                Border = border
            };
        }

        foreach (var kv in byTexture)
        {
            string texPath = kv.Key;
            try
            {
                var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer == null) continue;

                if (importer.textureType != TextureImporterType.Sprite)
                    importer.textureType = TextureImporterType.Sprite;

                // The texture is becoming a tk2d atlas: 1 texel = 1 world unit.
                if (Mathf.Abs(importer.spritePixelsPerUnit - 1f) > 0.01f)
                    importer.spritePixelsPerUnit = 1;

                if (importer.spriteImportMode != SpriteImportMode.Multiple)
                    importer.spriteImportMode = SpriteImportMode.Multiple;

                var sheet = Tk2dSpriteSheetUtility.GetRects(importer);
                bool changed = false;

                foreach (var entry in kv.Value.Values)
                {
                    int index = sheet.FindIndex(e => e.Name == entry.Name);
                    if (index >= 0)
                    {
                        // Fill in a missing border on an entry created by an
                        // earlier run (the plain-sprite pass adds sprites with
                        // no border; a later sliced conversion needs it).
                        if (!Approximately(sheet[index].Border, entry.Border))
                        {
                            var updated = sheet[index];
                            updated.Border = entry.Border;
                            sheet[index] = updated;
                            changed = true;
                        }
                        continue;
                    }

                    sheet.Add(entry);
                    changed = true;
                }

                if (changed)
                {
                    Tk2dSpriteSheetUtility.SetRects(importer, sheet);
                    importer.SaveAndReimport();
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Tk2dConverter] Could not pre-create the sprites for '{texPath}': {ex.Message}. " +
                                 "Falling back to per-sprite conversion.");
            }
        }
    }

    private static Sprite ConfigureSingleSprite(TextureImporter importer, string texPath, Vector4 border, bool needsReimport)
    {
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            needsReimport = true;
        }

        if (!Approximately(importer.spriteBorder, border))
        {
            importer.spriteBorder = border;
            needsReimport = true;
        }

        // spriteAlignment/spritePivot aren't exposed directly on
        // TextureImporter — they live in TextureImporterSettings.
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (settings.spriteAlignment != (int)SpriteAlignment.Center)
        {
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            needsReimport = true;
        }

        if (needsReimport)
        {
            importer.SaveAndReimport();
            AssetDatabase.Refresh();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
    }

    private static Sprite ConfigureMultipleSprite(TextureImporter importer, tk2dSpriteDefinition def, string texPath, Vector4 border, bool isSliced, bool needsReimport)
    {
        Rect spriteRect = new Rect(def.regionX, def.regionY, def.regionW, def.regionH);

        if (importer.spriteImportMode != SpriteImportMode.Multiple)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;
            needsReimport = true;
        }

        var sheet = Tk2dSpriteSheetUtility.GetRects(importer);

        // Match by NAME first: two tk2d definitions can legally share the same
        // atlas region (e.g. a duplicated sprite), and matching only by rect
        // would silently rename/overwrite the other one.
        int index = sheet.FindIndex(e => e.Name == def.name);

        if (index >= 0)
        {
            var smd = sheet[index];
            if (!Mathf.Approximately(smd.Rect.x, spriteRect.x) ||
                !Mathf.Approximately(smd.Rect.y, spriteRect.y) ||
                !Mathf.Approximately(smd.Rect.width, spriteRect.width) ||
                !Mathf.Approximately(smd.Rect.height, spriteRect.height))
            {
                smd.Rect = spriteRect;
                needsReimport = true;
            }

            if (smd.Alignment != (int)SpriteAlignment.Center)
            {
                smd.Pivot = Vector2.one * 0.5f;
                smd.Alignment = (int)SpriteAlignment.Center;
                needsReimport = true;
            }

            if (isSliced && !Approximately(smd.Border, border))
            {
                smd.Border = border;
                needsReimport = true;
            }

            sheet[index] = smd;
        }
        else
        {
            sheet.Add(new Tk2dSpriteSheetUtility.RectData
            {
                Name = def.name,
                Rect = spriteRect,
                Pivot = Vector2.one * 0.5f,
                Alignment = (int)SpriteAlignment.Center,
                Border = isSliced ? border : Vector4.zero
            });
            needsReimport = true;
        }

        if (needsReimport)
            Tk2dSpriteSheetUtility.SetRects(importer, sheet);

        if (needsReimport)
        {
            importer.SaveAndReimport();
            AssetDatabase.Refresh();
        }

        return LoadSpriteAtPath(texPath, def);
    }

    internal static bool Approximately(Vector4 a, Vector4 b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.x - b.x) < eps &&
               Mathf.Abs(a.y - b.y) < eps &&
               Mathf.Abs(a.z - b.z) < eps &&
               Mathf.Abs(a.w - b.w) < eps;
    }

    /// <summary>
    /// Resolves the texture asset that backs a tk2d sprite definition, using
    /// only information owned by that definition/collection:
    ///   1. the definition's serialized source texture GUID;
    ///   2. the collection's texture page for the definition's material;
    ///   3. the definition's own material _MainTex.
    /// Returns null (and logs why) when none of those resolve — the old
    /// project-wide "find a texture with this name" fallback could pick an
    /// unrelated file and reimport it as a Sprite.
    /// </summary>
    internal static string ResolveTexturePath(tk2dSpriteDefinition def, tk2dSpriteCollectionData collection)
    {
        if (def == null) return null;

        if (!string.IsNullOrEmpty(def.sourceTextureGUID))
        {
            string path = AssetDatabase.GUIDToAssetPath(def.sourceTextureGUID);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                return path;
        }

        if (collection != null)
        {
            if (collection.textures != null &&
                def.materialId >= 0 && def.materialId < collection.textures.Length)
            {
                var page = collection.textures[def.materialId];
                string pagePath = page != null ? AssetDatabase.GetAssetPath(page) : null;
                if (!string.IsNullOrEmpty(pagePath)) return pagePath;
            }

            if (collection.materials != null &&
                def.materialId >= 0 && def.materialId < collection.materials.Length)
            {
                string matPath = ResolveMaterialTexturePath(collection.materials[def.materialId]);
                if (!string.IsNullOrEmpty(matPath)) return matPath;
            }
        }

        string materialPath = ResolveMaterialTexturePath(def.material);
        if (!string.IsNullOrEmpty(materialPath)) return materialPath;

        WarnOnce("missing:" + def.name,
            $"[Tk2dConverter] Could not resolve the texture for sprite '{def.name}'. " +
            "Its tk2d collection has no texture page/material assigned and the definition carries no source texture GUID. " +
            "Assign the texture in the collection and run the conversion again.");

        return null;
    }

    private static string ResolveMaterialTexturePath(Material material)
    {
        if (material == null) return null;

        var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : material.mainTexture;
        if (texture == null) return null;

        return AssetDatabase.GetAssetPath(texture);
    }

    internal static Vector4 CalculateSpriteBorder(tk2dSpriteDefinition def, Vector2 borderBL, Vector2 borderTR, string texPath)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null) return Vector4.zero;

        float texW = tex.width;
        float texH = tex.height;

        float regionW = def.extractRegion ? def.regionW : texW;
        float regionH = def.extractRegion ? def.regionH : texH;

        if (regionW <= 0 || regionH <= 0) return Vector4.zero;

        return new Vector4(
            borderBL.x * regionW,
            borderBL.y * regionH,
            borderTR.x * regionW,
            borderTR.y * regionH
        );
    }

    internal static Sprite LoadSpriteAtPath(string texPath, tk2dSpriteDefinition def)
    {
        if (string.IsNullOrEmpty(texPath) || def == null) return null;

        var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(texPath);
        foreach (var asset in subAssets)
        {
            if (asset is Sprite subSprite &&
                (subSprite.name == def.name || subSprite.name == $"{def.name}_0"))
            {
                return subSprite;
            }
        }

        return null;
    }

    internal static Vector2 CalculateTargetSize(tk2dBaseSprite spr, tk2dSpriteDefinition def, Sprite unitySprite)
    {
        Vector3 tk2dScale = spr.scale;

        if (spr is tk2dSlicedSprite sliced)
        {
            return new Vector2(
                sliced.dimensions.x * Mathf.Abs(tk2dScale.x),
                sliced.dimensions.y * Mathf.Abs(tk2dScale.y)
            );
        }

        if (spr is tk2dTiledSprite tiled)
        {
            return new Vector2(
                tiled.dimensions.x * Mathf.Abs(tk2dScale.x),
                tiled.dimensions.y * Mathf.Abs(tk2dScale.y)
            );
        }

        if (unitySprite != null)
        {
            float w = unitySprite.textureRect.width;
            float h = unitySprite.textureRect.height;
            return new Vector2(w * Mathf.Abs(tk2dScale.x), h * Mathf.Abs(tk2dScale.y));
        }

        Vector3 size = def.untrimmedBoundsData[1];
        Vector2 texel = def.texelSize;
        if (texel.x > 0 && texel.y > 0)
        {
            return new Vector2(
                (size.x / texel.x) * Mathf.Abs(tk2dScale.x),
                (size.y / texel.y) * Mathf.Abs(tk2dScale.y)
            );
        }

        return new Vector2(100, 100);
    }

    /// <summary>
    /// Local scale multiplier that turns a SpriteRenderer sprite into the same
    /// world size the tk2d sprite had. tk2d bakes its own scale into the mesh
    /// vertices (1 texel = 1 world unit), while a SpriteRenderer renders at
    /// sprite.pixelsPerUnit — so a Sprite that was already imported at PPU 100
    /// must be scaled by 100 to keep the original size.
    /// </summary>
    internal static Vector2 CalculateWorldScale(tk2dBaseSprite spr, Sprite unitySprite)
    {
        Vector3 tk2dScale = spr != null ? spr.scale : Vector3.one;
        float pixelsPerUnit = unitySprite != null ? Mathf.Max(0.0001f, unitySprite.pixelsPerUnit) : 1f;

        return new Vector2(
            Mathf.Abs(tk2dScale.x) * pixelsPerUnit,
            Mathf.Abs(tk2dScale.y) * pixelsPerUnit);
    }

    /// <summary>
    /// Destroys the tk2dBaseSprite component (and any remaining ones on the
    /// same GameObject) — used right before adding the uGUI/native equivalent.
    /// </summary>
    internal static void DestroyTk2dSpriteComponents(GameObject go, tk2dBaseSprite spr)
    {
        Undo.DestroyObjectImmediate(spr);

        var remainingTk2d = go.GetComponents<tk2dBaseSprite>();
        for (int i = remainingTk2d.Length - 1; i >= 0; i--)
        {
            if (remainingTk2d[i] != null)
                Undo.DestroyObjectImmediate(remainingTk2d[i]);
        }
    }

    /// <summary>
    /// Removes tk2d's legacy render components that have no equivalent in the
    /// destination (uGUI Image or native SpriteRenderer).
    ///
    /// Colliders are only removed for UI conversion: uGUI hit-testing uses the
    /// RectTransform, so a leftover collider is dead weight. For world-space
    /// conversion the collider is often load-bearing (custom drag/drop and
    /// click code queries it), so it's preserved by default instead of being
    /// silently deleted.
    /// </summary>
    internal static void RemoveLegacyComponents(GameObject go, bool destroyColliders = true)
    {
        var meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null)
            Undo.DestroyObjectImmediate(meshFilter);

        var meshRenderer = go.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            Undo.DestroyObjectImmediate(meshRenderer);

        if (!destroyColliders) return;

        var boxCollider = go.GetComponent<BoxCollider>();
        if (boxCollider != null)
            Undo.DestroyObjectImmediate(boxCollider);

        var boxCollider2D = go.GetComponent<BoxCollider2D>();
        if (boxCollider2D != null)
            Undo.DestroyObjectImmediate(boxCollider2D);
    }

    /// <summary>
    /// Unpacks the enclosing prefab instance (rooted at the outermost prefab
    /// instance), so components can be replaced/added on the instance.
    ///
    /// PrefabUtility.UnpackPrefabInstance throws unless it receives the ROOT
    /// instance object — passing a child (which is what the converters see when
    /// they walk a hierarchy) fails with "must be called with a root Prefab
    /// instance GameObject" and the whole conversion silently converts nothing.
    /// </summary>
    internal static void UnpackIfPrefabInstance(GameObject go)
    {
        if (go == null) return;
        if (!PrefabUtility.IsPartOfPrefabInstance(go)) return;

        var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
        if (root == null) return;

        PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.UserAction);
    }

    /// <summary>
    /// A tk2dSpriteAnimator drives a tk2dBaseSprite through a
    /// tk2dSpriteAnimation asset. There is no 1:1 native equivalent (the clips
    /// are tk2d sprite-id timelines, not Unity AnimationClips), and leaving the
    /// component enabled after its tk2dBaseSprite is destroyed makes it throw
    /// NullReferenceException on the first Play(). Disable it (kept on the
    /// object, reversible) and warn loudly so it can be converted by hand.
    /// </summary>
    internal static bool DisableUnsupportedAnimator(GameObject go)
    {
        if (go == null) return false;

        var animator = go.GetComponent<tk2dSpriteAnimator>();
        if (animator == null) return false;

        Undo.RecordObject(animator, "Disable tk2dSpriteAnimator");
        animator.enabled = false;
        EditorUtility.SetDirty(animator);

        Debug.LogWarning($"[Tk2dConverter] '{go.name}' has a tk2dSpriteAnimator, which has no native equivalent. " +
                         "The component was DISABLED (not removed) so the scene does not throw at runtime — " +
                         "recreate its clips as Unity AnimationClips (SpriteRenderer.sprite keyframes) and remove it.",
                         go);

        return true;
    }

    private static string _tk2dUiCodeRoot;
    private static bool _tk2dUiCodeRootResolved;

    private static readonly HashSet<string> AlwaysPreserveTypeNames = new HashSet<string>
    {
        // Shared tk2d service: project-specific drag/drop scripts keep using it
        // even after their visuals are converted.
        "tk2dUIManager",
    };

    /// <summary>
    /// True when a 'tk2dUI*'-named component is actually owned by tk2d (its
    /// script lives under the same folder as tk2dUIManager) and can therefore
    /// be destroyed after conversion.
    ///
    /// Custom project classes often copy tk2d's 'tk2dUI*' naming convention for
    /// their own gameplay logic (drag-and-drop, clamping, etc.). Detecting them
    /// by script path keeps the tool reusable in any project without a
    /// hard-coded per-project exclusion list.
    /// </summary>
    internal static bool IsRemovableTk2dUIComponent(Component comp)
    {
        if (comp == null) return false;

        string typeName = comp.GetType().Name;
        if (!typeName.StartsWith("tk2dUI")) return false;
        if (AlwaysPreserveTypeNames.Contains(typeName)) return false;

        string tk2dRoot = Tk2dUiCodeRoot;
        if (string.IsNullOrEmpty(tk2dRoot)) return true; // can't tell: keep historical behaviour

        string scriptPath = GetScriptPath(comp);
        if (string.IsNullOrEmpty(scriptPath)) return true;

        return scriptPath.Replace('\\', '/')
                         .StartsWith(tk2dRoot, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string GetScriptPath(Component comp)
    {
        var behaviour = comp as MonoBehaviour;
        if (behaviour == null) return null;

        var script = MonoScript.FromMonoBehaviour(behaviour);
        return script != null ? AssetDatabase.GetAssetPath(script) : null;
    }

    /// <summary>Folder that contains the tk2d UI source (…/tk2dUI/), cached.</summary>
    private static string Tk2dUiCodeRoot
    {
        get
        {
            if (_tk2dUiCodeRootResolved) return _tk2dUiCodeRoot;
            _tk2dUiCodeRootResolved = true;

            foreach (var guid in AssetDatabase.FindAssets("tk2dUIManager t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.EndsWith("/tk2dUIManager.cs", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                // …/tk2dUI/Code/Core/tk2dUIManager.cs  →  …/tk2dUI/
                string code = System.IO.Path.GetDirectoryName(path);              // …/Core
                string ui = System.IO.Path.GetDirectoryName(code);                // …/Code
                string root = System.IO.Path.GetDirectoryName(ui);                // …/tk2dUI
                if (!string.IsNullOrEmpty(root))
                {
                    _tk2dUiCodeRoot = root.Replace('\\', '/') + "/";
                    break;
                }
            }

            return _tk2dUiCodeRoot;
        }
    }

    private static void WarnOnce(string key, string message)
    {
        if (!WarnedTextures.Add(key)) return;
        Debug.LogWarning(message);
    }
}
