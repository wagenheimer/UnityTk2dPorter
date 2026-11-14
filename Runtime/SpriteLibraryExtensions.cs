using UnityEngine;
using UnityEngine.UI;

namespace Wagenheimer.Tk2dPorter
{
    /// <summary>
    /// Drop-in replacements for the tk2d sprite API, so migrated call sites stay
    /// almost unchanged:
    ///
    /// <code>
    /// // before: public tk2dSprite frontSprite;  frontSprite.SetSprite("club_A");
    /// // after:  public SpriteRenderer frontSprite; frontSprite.SetSprite("club_A");
    /// </code>
    ///
    /// Names are resolved through the generated <see cref="SpriteLibrary"/>.
    /// </summary>
    public static class SpriteLibraryExtensions
    {
        /// <summary>tk2d's SetSprite(string), backed by the SpriteLibrary. Returns false when the name is unknown.</summary>
        public static bool SetSprite(this SpriteRenderer renderer, string spriteName)
        {
            if (renderer == null) return false;

            var sprite = SpriteLibrary.Find(spriteName);
            if (sprite == null)
            {
                Debug.LogWarning($"[Tk2dPorter] Sprite '{spriteName}' not found in the SpriteLibrary.", renderer);
                return false;
            }

            renderer.sprite = sprite;
            return true;
        }

        public static bool SetSprite(this Image image, string spriteName)
        {
            if (image == null) return false;

            var sprite = SpriteLibrary.Find(spriteName);
            if (sprite == null)
            {
                Debug.LogWarning($"[Tk2dPorter] Sprite '{spriteName}' not found in the SpriteLibrary.", image);
                return false;
            }

            image.sprite = sprite;
            return true;
        }

        /// <summary>tk2d's CurrentSprite.name.</summary>
        public static string GetSpriteName(this SpriteRenderer renderer)
            => renderer != null && renderer.sprite != null ? renderer.sprite.name : null;

        public static string GetSpriteName(this Image image)
            => image != null && image.sprite != null ? image.sprite.name : null;

        /// <summary>tk2dRadialSprite.VisibleAmount → Image.Filled fillAmount.</summary>
        public static void SetVisibleAmount(this Image image, float amount)
        {
            if (image == null) return;
            image.fillAmount = Mathf.Clamp01(amount);
        }

        /// <summary>
        /// tk2dClippedSprite.ClipRect → Image.Filled fillAmount. Only the clip axis
        /// (horizontal vs vertical) is mapped; a rect that trims both edges cannot
        /// be represented by uGUI and is reported in the console.
        /// </summary>
        public static void SetClipRect(this Image image, Rect clipRect)
        {
            if (image == null) return;

            if (image.fillMethod == Image.FillMethod.Vertical)
            {
                image.fillAmount = Mathf.Clamp01(clipRect.height);
                if (clipRect.y > 0.001f)
                    Debug.LogWarning($"[Tk2dPorter] '{image.name}': vertical clip keeps the top part; " +
                                     "Image.Filled can only fill from the bottom.", image);
            }
            else
            {
                image.fillAmount = Mathf.Clamp01(clipRect.width);
                if (clipRect.x > 0.001f)
                    Debug.LogWarning($"[Tk2dPorter] '{image.name}': horizontal clip keeps the right part; " +
                                     "Image.Filled can only fill from the left.", image);
            }
        }
    }
}
