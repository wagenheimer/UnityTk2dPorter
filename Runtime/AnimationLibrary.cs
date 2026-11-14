using System.Collections.Generic;
using UnityEngine;

namespace Wagenheimer.Tk2dPorter
{
    /// <summary>One named sprite sequence, converted from a tk2dSpriteAnimationClip.</summary>
    [System.Serializable]
    public class SpriteClip
    {
        public string name;
        public float fps = 30f;
        public bool loop;
        public Sprite[] frames;
    }

    /// <summary>
    /// Name-keyed list of clips, replacing <c>tk2dSpriteAnimation</c>. Generated
    /// from the project's tk2d animation assets by Tools → Wagenheimer → Tk2d
    /// Porter → Libraries, and loaded from Resources at runtime so gameplay code
    /// can keep calling <c>Play("clipName")</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Wagenheimer/Tk2d Porter/Animation Library", fileName = "AnimationLibrary")]
    public class AnimationLibrary : ScriptableObject
    {
        public SpriteClip[] clips;

        private Dictionary<string, SpriteClip> _map;
        private static AnimationLibrary _instance;

        public static AnimationLibrary Instance
        {
            get
            {
                if (_instance == null)
                    _instance = Resources.Load<AnimationLibrary>("AnimationLibrary");
                return _instance;
            }
        }

        public SpriteClip Get(string clipName)
        {
            if (string.IsNullOrEmpty(clipName)) return null;

            if (_map == null)
            {
                _map = new Dictionary<string, SpriteClip>(clips != null ? clips.Length : 0);
                if (clips != null)
                {
                    foreach (var clip in clips)
                    {
                        if (clip == null || string.IsNullOrEmpty(clip.name)) continue;
                        if (!_map.ContainsKey(clip.name))
                            _map.Add(clip.name, clip);
                    }
                }
            }

            return _map.TryGetValue(clipName, out var result) ? result : null;
        }

        private void OnEnable()
        {
            _instance = this;
            _map = null;
        }
    }
}
