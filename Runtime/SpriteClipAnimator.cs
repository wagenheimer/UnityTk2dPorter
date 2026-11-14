using System;
using UnityEngine;

namespace Wagenheimer.Tk2dPorter
{
    /// <summary>
    /// Plays a <see cref="SpriteClip"/> on a native <see cref="SpriteRenderer"/>.
    /// Behavioural replacement for the tk2dSpriteAnimator usage found in most
    /// games:
    /// <code>
    /// animator.AnimationCompleted = OnCompleted;   // (animator, clipName)
    /// animator.Play("rockform");
    /// </code>
    /// Clips are resolved by name from the AnimationLibrary in Resources.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpriteClipAnimator : MonoBehaviour
    {
        [Tooltip("Renderer the frames are assigned to. Defaults to this object's SpriteRenderer.")]
        public SpriteRenderer target;

        [Tooltip("Clip played automatically on enable, if any.")]
        public string playOnEnable;

        /// <summary>Invoked when a non-looping clip finishes. (animator, clipName)</summary>
        public Action<SpriteClipAnimator, string> AnimationCompleted;

        public string CurrentClipName { get; private set; }

        private SpriteClip _clip;
        private int _frame;
        private float _timer;
        private bool _playing;

        public bool IsPlaying => _playing;

        private void Awake()
        {
            if (target == null)
                target = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(playOnEnable))
                Play(playOnEnable);
        }

        private void OnDisable()
        {
            _playing = false;
        }

        /// <summary>Starts a clip by name (from the AnimationLibrary).</summary>
        public void Play(string clipName)
        {
            var library = AnimationLibrary.Instance;
            if (library == null)
            {
                Debug.LogWarning("[SpriteClipAnimator] AnimationLibrary not found in Resources.", this);
                return;
            }

            var clip = library.Get(clipName);
            if (clip == null || clip.frames == null || clip.frames.Length == 0)
            {
                Debug.LogWarning($"[SpriteClipAnimator] Clip '{clipName}' not found (or empty) in the AnimationLibrary.", this);
                return;
            }

            Play(clip);
        }

        public void Play(SpriteClip clip)
        {
            if (clip == null || clip.frames == null || clip.frames.Length == 0) return;

            _clip = clip;
            CurrentClipName = clip.name;
            _frame = 0;
            _timer = 0f;
            _playing = true;

            ApplyFrame();
        }

        public void Stop()
        {
            _playing = false;
        }

        private void Update()
        {
            if (!_playing || _clip == null) return;

            float fps = _clip.fps > 0f ? _clip.fps : 30f;
            _timer += Time.deltaTime;

            float frameDuration = 1f / fps;
            while (_timer >= frameDuration && _playing)
            {
                _timer -= frameDuration;
                _frame++;

                if (_frame >= _clip.frames.Length)
                {
                    if (_clip.loop)
                    {
                        _frame = 0;
                    }
                    else
                    {
                        _frame = _clip.frames.Length - 1;
                        _playing = false;
                        ApplyFrame();
                        AnimationCompleted?.Invoke(this, CurrentClipName);
                        return;
                    }
                }

                ApplyFrame();
            }
        }

        private void ApplyFrame()
        {
            if (target == null || _clip == null || _clip.frames == null) return;
            if (_frame < 0 || _frame >= _clip.frames.Length) return;

            target.sprite = _clip.frames[_frame];
        }
    }
}
