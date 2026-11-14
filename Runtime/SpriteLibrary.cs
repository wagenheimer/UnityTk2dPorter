using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.Tk2dPorter
{
    /// <summary>
    /// Name → Sprite lookup that replaces <c>tk2dSprite.SetSprite(string)</c>.
    ///
    /// The asset is generated from the project's tk2d sprite collections by
    /// Tools → Wagenheimer → Tk2d Porter → Libraries, and is loaded from
    /// Resources at runtime so gameplay code can keep using sprite NAMES
    /// (level data, save data) instead of hard references.
    /// </summary>
    [CreateAssetMenu(menuName = "Wagenheimer/Tk2d Porter/Sprite Library", fileName = "SpriteLibrary")]
    public class SpriteLibrary : ScriptableObject
    {
        [Tooltip("Every sprite that can be requested by name.")]
        public Sprite[] sprites;

        [Tooltip("Resources name used to load this asset at runtime (file name without extension).")]
        public string resourcesName = "SpriteLibrary";

        private Dictionary<string, Sprite> _map;
        private static SpriteLibrary _instance;

        public static SpriteLibrary Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<SpriteLibrary>("SpriteLibrary");
                return _instance;
            }
        }

        /// <summary>Sprite for a tk2d sprite name, or null when the library has no entry.</summary>
        public static Sprite Find(string spriteName)
        {
            var library = Instance;
            return library != null ? library.Get(spriteName) : null;
        }

        public Sprite Get(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>(sprites != null ? sprites.Length : 0);
                if (sprites != null)
                {
                    foreach (var sprite in sprites)
                    {
                        if (sprite == null) continue;
                        if (!_map.ContainsKey(sprite.name))
                            _map.Add(sprite.name, sprite);
                    }
                }
            }

            return _map.TryGetValue(spriteName, out var result) ? result : null;
        }

        private void OnEnable()
        {
            _instance = this;
            _map = null;
        }
    }
}
