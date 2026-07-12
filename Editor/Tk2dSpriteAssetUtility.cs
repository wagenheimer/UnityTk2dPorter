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
    internal static Sprite FindOrCreateSpriteAsset(tk2dSpriteDefinition def, bool isSliced, Vector2 borderBL, Vector2 borderTR)
    {
        string texPath = ResolveTexturePath(def);
        if (string.IsNullOrEmpty(texPath)) return null;

        TextureImporter importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (importer == null) return null;

        bool needsReimport = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            needsReimport = true;
        }

        if (Mathf.Abs(importer.spritePixelsPerUnit - 1f) > 0.01f)
        {
            importer.spritePixelsPerUnit = 1;
            needsReimport = true;
        }

        Vector4 border = Vector4.zero;
        if (isSliced)
            border = CalculateSpriteBorder(def, borderBL, borderTR, texPath);

        // A texture only needs "Multiple" sprite mode when it's an atlas
        // shared by several tk2d definitions (a sub-region is being
        // extracted from it). A texture dedicated to exactly one definition
        // covering the whole image should stay "Single" — that's what a
        // plain, non-atlas source image normally uses, and avoids the
        // confusing "Multiple sprite mode with a single entry" result.
        bool isAtlasRegion = def.extractRegion && def.regionW > 0 && def.regionH > 0;

        return isAtlasRegion
            ? ConfigureMultipleSprite(importer, def, texPath, border, isSliced, needsReimport)
            : ConfigureSingleSprite(importer, texPath, border, needsReimport);
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

        if (importer.spriteAlignment != (int)SpriteAlignment.Center)
        {
            importer.spriteAlignment = (int)SpriteAlignment.Center;
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

        var sheet = new List<SpriteMetaData>(importer.spritesheet);
        bool found = false;

        for (int i = 0; i < sheet.Count; i++)
        {
            var smd = sheet[i];
            if (!Mathf.Approximately(smd.rect.x, spriteRect.x) ||
                !Mathf.Approximately(smd.rect.y, spriteRect.y) ||
                !Mathf.Approximately(smd.rect.width, spriteRect.width) ||
                !Mathf.Approximately(smd.rect.height, spriteRect.height))
                continue;

            if (isSliced && !Approximately(smd.border, border))
            {
                smd.border = border;
                needsReimport = true;
            }
            smd.name = def.name;
            smd.pivot = Vector2.one * 0.5f;
            smd.alignment = (int)SpriteAlignment.Center;
            sheet[i] = smd;
            found = true;
            break;
        }

        if (!found)
        {
            var smd = new SpriteMetaData
            {
                name = def.name,
                rect = spriteRect,
                pivot = Vector2.one * 0.5f,
                alignment = (int)SpriteAlignment.Center,
                border = isSliced ? border : Vector4.zero
            };
            sheet.Add(smd);
            needsReimport = true;
        }

        importer.spritesheet = sheet.ToArray();

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

    internal static string ResolveTexturePath(tk2dSpriteDefinition def)
    {
        if (!string.IsNullOrEmpty(def.sourceTextureGUID))
        {
            string path = AssetDatabase.GUIDToAssetPath(def.sourceTextureGUID);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) != null)
                return path;
        }

        string searchName = string.IsNullOrEmpty(def.name) ? "sprite" : def.name;
        string[] guids = AssetDatabase.FindAssets($"{searchName} t:Texture2D");

        foreach (var guid in guids)
        {
            string candidate = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = System.IO.Path.GetFileNameWithoutExtension(candidate);
            if (fileName.Equals(searchName, System.StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
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
        var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(texPath);
        foreach (var asset in subAssets)
        {
            if (asset is Sprite subSprite &&
                (subSprite.name == def.name || subSprite.name == $"{def.name}_0"))
            {
                return subSprite;
            }
        }

        foreach (var asset in subAssets)
        {
            if (asset is Sprite subSprite)
                return subSprite;
        }

        var mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        if (mainSprite != null) return mainSprite;

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
    /// Removes tk2d's legacy render/collision components that have no
    /// equivalent in the destination (uGUI Image or native SpriteRenderer).
    /// </summary>
    internal static void RemoveLegacyComponents(GameObject go)
    {
        var meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null)
            Undo.DestroyObjectImmediate(meshFilter);

        var meshRenderer = go.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            Undo.DestroyObjectImmediate(meshRenderer);

        var boxCollider = go.GetComponent<BoxCollider>();
        if (boxCollider != null)
            Undo.DestroyObjectImmediate(boxCollider);

        var boxCollider2D = go.GetComponent<BoxCollider2D>();
        if (boxCollider2D != null)
            Undo.DestroyObjectImmediate(boxCollider2D);
    }
}
