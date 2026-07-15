using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace DesignSystem.Runtime.Typography
{
    /// <summary>
    /// One typeface, as an asset: up to nine upright weights, up to nine italics, and the
    /// script fallbacks that cover what the family itself does not.
    ///
    /// Why a family asset rather than a bare <see cref="FontAsset"/>? Because UI Toolkit has
    /// no <c>font-weight</c> USS property. It has exactly two style slots — normal and bold —
    /// and everything else has to be reached by pointing <c>-unity-font-definition</c> at a
    /// DIFFERENT FontAsset. So "the family" is not a Unity concept; it has to be one here, or
    /// every consumer re-invents it.
    ///
    /// The nine slots are the same buckets <c>FontAsset.fontWeightTable</c> uses (100..900).
    /// The importer wires that table across every member, which is what makes the design
    /// system's existing <c>-unity-font-style: bold</c> rules resolve to a REAL bold face
    /// instead of a synthetically dilated Regular.
    /// </summary>
    [CreateAssetMenu(fileName = "FontFamily", menuName = "Design System/Font Family", order = 101)]
    public class DsFontFamily : ScriptableObject
    {
        /// <summary>Number of CSS weight buckets: 100, 200, … 900.</summary>
        public const int WeightCount = 9;

        [Tooltip("Display name, e.g. \"Inter\". Shown in pickers.")]
        public string familyName = "Unknown";

        [Tooltip("Upright faces, indexed by bucket: [0]=100 … [8]=900. Empty slots are allowed.")]
        public FontAsset[] weights = new FontAsset[WeightCount];

        [Tooltip("Italic faces, same indexing. Empty when the family ships no italics.")]
        public FontAsset[] italics = new FontAsset[WeightCount];

        [Tooltip("Fonts consulted, in order, for characters this family has no glyph for.\n\n" +
                 "This list is not optional for multilingual UI. With it empty, Unity quietly " +
                 "borrows an OS font — which exists on Windows and does NOT exist on WebGL, " +
                 "so the text looks perfect in the Editor and renders as boxes in a browser.")]
        public List<FontAsset> fallbacks = new();

        /// <summary>The 400 face, or the nearest thing to it. Never null for an imported family.</summary>
        public FontAsset Regular => Resolve(400, false);

        /// <summary>Bucket index for a CSS weight: 100 -> 0, 400 -> 3, 900 -> 8.</summary>
        public static int BucketOf(int weight) => Mathf.Clamp(OpenTypeFace.SnapToBucket(weight) / 100 - 1, 0, WeightCount - 1);

        /// <summary>CSS weight for a bucket index: 0 -> 100, 8 -> 900.</summary>
        public static int WeightOf(int bucket) => (Mathf.Clamp(bucket, 0, WeightCount - 1) + 1) * 100;

        /// <summary>
        /// The best face for a requested weight. Falls outward to the nearest weight the
        /// family actually ships, so asking a two-weight family for 600 yields its Bold
        /// rather than nothing. Falls back to upright when an italic is missing, because a
        /// missing glyph is a far worse failure than a missing slant.
        /// </summary>
        public FontAsset Resolve(int weight, bool italic = false)
        {
            var primary = italic ? italics : weights;
            var secondary = italic ? weights : null;

            int want = BucketOf(weight);

            var hit = Nearest(primary, want);
            if (hit) return hit;

            if (secondary != null)
            {
                hit = Nearest(secondary, want);
                if (hit) return hit;
            }

            return null;
        }

        /// <summary>Weights this family actually ships, ascending. Drives the UI's weight ramp.</summary>
        public List<int> AvailableWeights(bool italic = false)
        {
            var arr = italic ? italics : weights;
            var list = new List<int>(WeightCount);
            if (arr == null) return list;

            for (int i = 0; i < arr.Length && i < WeightCount; i++)
                if (arr[i]) list.Add(WeightOf(i));

            return list;
        }

        public bool HasItalics()
        {
            if (italics == null) return false;
            foreach (var f in italics)
                if (f) return true;
            return false;
        }

        // Walk outward from the requested bucket. Ties break toward the HEAVIER face, which
        // matches CSS's own font-matching rule for weights at or above 400 and, more to the
        // point, keeps a "Bold" heading looking bold when the family's real bold is missing.
        private static FontAsset Nearest(FontAsset[] arr, int want)
        {
            if (arr == null || arr.Length == 0) return null;
            if (want < arr.Length && arr[want]) return arr[want];

            for (int d = 1; d < WeightCount; d++)
            {
                int up = want + d;
                if (up < arr.Length && up < WeightCount && arr[up]) return arr[up];

                int down = want - d;
                if (down >= 0 && down < arr.Length && arr[down]) return arr[down];
            }

            return null;
        }
    }
}
