using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reads/writes the sprite rectangles of a Multiple sprite texture.
///
/// Unity 2023+ marks <see cref="TextureImporter.spritesheet"/> as obsolete and
/// points at UnityEditor.U2D.Sprites.ISpriteEditorDataProvider. The provider
/// lives in the optional 2D Sprite Editor package, so this helper reaches it
/// through reflection: when the package is present the supported API is used,
/// otherwise the tool transparently falls back to the legacy spritesheet API
/// (which still works on Unity 2021.3–6.x, just with a deprecation warning).
/// </summary>
internal static class Tk2dSpriteSheetUtility
{
    internal struct RectData
    {
        public string Name;
        public Rect Rect;
        public Vector4 Border;
        public Vector2 Pivot;
        public int Alignment;
    }

    private static bool _reflectionInitialized;
    private static bool _providerAvailable;

    private static Type _factoryType;
    private static Type _rectType;
    private static MethodInfo _getProviderMethod;
    private static MethodInfo _initProviderMethod;
    private static MethodInfo _getRectsMethod;
    private static MethodInfo _setRectsMethod;
    private static MethodInfo _applyMethod;

    private static FieldInfo _nameField;
    private static FieldInfo _rectField;
    private static FieldInfo _borderField;
    private static FieldInfo _pivotField;
    private static FieldInfo _alignmentField;

    private static bool _warnedFallback;

    internal static bool ProviderAvailable
    {
        get
        {
            InitializeReflection();
            return _providerAvailable;
        }
    }

    private static void InitializeReflection()
    {
        if (_reflectionInitialized) return;
        _reflectionInitialized = true;

        try
        {
            _factoryType = FindType("UnityEditor.U2D.Sprites.SpriteDataProviderFactories");
            _rectType = FindType("UnityEditor.U2D.Sprites.SpriteRect");
            var providerType = FindType("UnityEditor.U2D.Sprites.ISpriteEditorDataProvider");

            if (_factoryType == null || _rectType == null || providerType == null)
                return;

            _getProviderMethod = _factoryType.GetMethod("GetSpriteEditorDataProviderFromObject",
                BindingFlags.Public | BindingFlags.Instance);
            _initProviderMethod = providerType.GetMethod("InitSpriteEditorDataProvider");
            _getRectsMethod = providerType.GetMethod("GetSpriteRects");
            _setRectsMethod = providerType.GetMethod("SetSpriteRects");
            _applyMethod = providerType.GetMethod("Apply");

            _nameField = _rectType.GetField("name");
            _rectField = _rectType.GetField("rect");
            _borderField = _rectType.GetField("border");
            _pivotField = _rectType.GetField("pivot");
            _alignmentField = _rectType.GetField("alignment");

            _providerAvailable =
                _getProviderMethod != null && _initProviderMethod != null &&
                _getRectsMethod != null && _setRectsMethod != null && _applyMethod != null &&
                _nameField != null && _rectField != null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Tk2dConverter] ISpriteEditorDataProvider reflection failed ({ex.Message}); " +
                             "using the legacy spritesheet API.");
            _providerAvailable = false;
        }
    }

    private static Type FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = null;
            try { type = assembly.GetType(fullName, false); }
            catch { /* dynamic/unloaded assembly — ignore */ }
            if (type != null) return type;
        }
        return null;
    }

    internal static List<RectData> GetRects(TextureImporter importer)
    {
        var result = new List<RectData>();
        if (importer == null) return result;

        InitializeReflection();

        if (_providerAvailable)
        {
            try
            {
                var provider = GetProvider(importer);
                if (provider != null)
                {
                    var rects = (Array)_getRectsMethod.Invoke(provider, null);
                    if (rects != null)
                    {
                        foreach (var rectObj in rects)
                            result.Add(ReadRect(rectObj));
                        return result;
                    }
                }
            }
            catch (Exception ex)
            {
                WarnFallback(ex);
            }
        }

        // Legacy fallback
#pragma warning disable 0618
        foreach (var smd in importer.spritesheet)
        {
            result.Add(new RectData
            {
                Name = smd.name,
                Rect = smd.rect,
                Border = smd.border,
                Pivot = smd.pivot,
                Alignment = smd.alignment
            });
        }
#pragma warning restore 0618

        return result;
    }

    internal static void SetRects(TextureImporter importer, IList<RectData> rects)
    {
        if (importer == null || rects == null) return;

        InitializeReflection();

        if (_providerAvailable)
        {
            try
            {
                var provider = GetProvider(importer);
                if (provider != null)
                {
                    var array = Array.CreateInstance(_rectType, rects.Count);
                    for (int i = 0; i < rects.Count; i++)
                    {
                        var rect = Activator.CreateInstance(_rectType);
                        WriteRect(rect, rects[i]);
                        array.SetValue(rect, i);
                    }

                    _setRectsMethod.Invoke(provider, new object[] { array });
                    _applyMethod.Invoke(provider, null);
                    return;
                }
            }
            catch (Exception ex)
            {
                WarnFallback(ex);
            }
        }

        // Legacy fallback
#pragma warning disable 0618
        importer.spritesheet = rects.Select(r => new SpriteMetaData
        {
            name = r.Name,
            rect = r.Rect,
            border = r.Border,
            pivot = r.Pivot,
            alignment = r.Alignment
        }).ToArray();
#pragma warning restore 0618
    }

    /// <summary>Importer settings a sprite definition needs (pivot+alignment).</summary>
    internal static void EnsureCenterPivot(TextureImporter importer)
    {
        if (importer == null) return;

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        if (settings.spriteAlignment != (int)SpriteAlignment.Center)
        {
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
        }
    }

    private static object GetProvider(TextureImporter importer)
    {
        var factory = Activator.CreateInstance(_factoryType);
        _factoryType.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance)?.Invoke(factory, null);

        var provider = _getProviderMethod.Invoke(factory, new object[] { importer });
        if (provider == null) return null;

        _initProviderMethod.Invoke(provider, null);
        return provider;
    }

    private static RectData ReadRect(object rect)
    {
        return new RectData
        {
            Name = (string)_nameField.GetValue(rect),
            Rect = (Rect)_rectField.GetValue(rect),
            Border = _borderField != null ? (Vector4)_borderField.GetValue(rect) : Vector4.zero,
            Pivot = _pivotField != null ? (Vector2)_pivotField.GetValue(rect) : Vector2.one * 0.5f,
            Alignment = _alignmentField != null ? Convert.ToInt32(_alignmentField.GetValue(rect)) : (int)SpriteAlignment.Center
        };
    }

    private static void WriteRect(object rect, RectData data)
    {
        _nameField.SetValue(rect, data.Name);
        _rectField.SetValue(rect, data.Rect);
        if (_borderField != null) _borderField.SetValue(rect, data.Border);
        if (_pivotField != null) _pivotField.SetValue(rect, data.Pivot);
        if (_alignmentField != null) _alignmentField.SetValue(rect, data.Alignment);
    }

    private static void WarnFallback(Exception ex)
    {
        if (_warnedFallback) return;
        _warnedFallback = true;
        Debug.LogWarning($"[Tk2dConverter] ISpriteEditorDataProvider failed ({ex.GetType().Name}: {ex.Message}); " +
                         "falling back to the legacy spritesheet API for the remaining textures.");
        _providerAvailable = false;
    }
}
