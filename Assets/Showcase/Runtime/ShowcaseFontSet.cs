using System;
using DesignSystem.Runtime.Typography;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Showcase.Runtime
{
    /// <summary>
    /// What the showcase's font picker offers, and what its fallback chain is.
    ///
    /// It exists because <c>Resources.LoadAll&lt;DsFontFamily&gt;</c> cannot tell a family you
    /// are meant to PICK from one that is only ever a FALLBACK — the seven Noto families are
    /// there to cover Arabic and CJK, not to be offered as a UI typeface. Written by
    /// <c>ShowcaseFontBaker</c>; host-project only, not part of the package.
    /// </summary>
    public sealed class ShowcaseFontSet : ScriptableObject
    {
        [Serializable]
        public sealed class Choice
        {
            public DsFontFamily family;

            /// <summary>
            /// The family's generated stylesheet. Swapping THIS is what re-points the design
            /// system's tokens — `.ds-h3` at the real 600 face, `.ds-caption` at 500, and the
            /// `.ds-weight-*` utilities at the whole ramp. Applying the font inline instead
            /// would change the body text and leave those rules pointing at the old family.
            /// </summary>
            public StyleSheet sheet;
        }

        public Choice[] choices = Array.Empty<Choice>();

        /// <summary>Script fallbacks, in order. Wired onto every family the picker offers.</summary>
        public FontAsset[] fallbacks = Array.Empty<FontAsset>();
    }
}
