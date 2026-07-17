#if UNITY_6000_5_OR_NEWER
using UnityEngine;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// Registers BLUEPRINT — the design system's reference material family, and a worked example
    /// of everything a family registration involves. Copy this file's shape for your own.
    ///
    /// The three colors are not decoration, they are a contract (see <see cref="DsFxVariant"/>):
    /// ColA is the deep tone, ColB the light one, ColC the accent. <see cref="DsFxPalette"/> derives
    /// the entire tone ladder and the readable ink from them, so a variant that inverts them — pale
    /// stock in ColA, dark lines in ColB — quietly breaks the ladder and the contrast floor. That is
    /// why every variant below is dark-stock/light-linework and varies by HUE. A light appearance is
    /// what <see cref="DsFxManager.ThemeLight"/> is for; it is not a variant's job.
    /// </summary>
    public static class DsFxBlueprintFamily
    {
        private static Color Rgb(int r, int g, int b, float a = 1f)
            => new Color32((byte)r, (byte)g, (byte)b, (byte)Mathf.RoundToInt(a * 255f));

        /// <summary>The family handle. Pass it to <see cref="DsFxTheme.Apply"/>, or write
        /// <c>ds-fx-blueprint</c> on an element.</summary>
        public static readonly DsFxFamily Family = new DsFxFamily(
            "blueprint",
            "Hidden/DsFx/Blueprint",
            "Fx/Shaders/DsFxBlueprint",
            new[]
            {
                // params: x grid cell (pt), y line weight (pt),
                //         z plate construction gap (pt, 0 = single-line controls),
                //         w corner-key density.
                // Stock is DEEP on purpose: the reference sheet is near-black Prussian
                // paper, and the tone ladder derives page/panel/well from ColA — a bright
                // ColA floats the whole drawing. ColB is the light lettering/line pole,
                // ColC the linework accent.
                new DsFxVariant("cyanotype",
                    Rgb(0x0F, 0x25, 0x47), Rgb(0xC9, 0xDF, 0xF7), Rgb(0x55, 0xBD, 0xFF),
                    new Vector4(8f, 1.0f, 2.6f, 0.7f)),
                new DsFxVariant("graphite",
                    Rgb(0x22, 0x26, 0x2C), Rgb(0xC6, 0xD0, 0xDC), Rgb(0xFF, 0xB4, 0x5C),
                    new Vector4(10f, 0.9f, 2.6f, 0.4f)),
                new DsFxVariant("redline",
                    Rgb(0x0D, 0x1B, 0x38), Rgb(0xD9, 0xE4, 0xF4), Rgb(0xFF, 0x61, 0x57),
                    new Vector4(8f, 1.0f, 2.6f, 1.0f)),
            })
        {
            // The redesigned sheet is QUIET — flat stock, a low-voice grid, no tooth — so body ink
            // does not need the thick cathedral-grain counter-shadow, and the reference sheet's
            // lettering is clean print with no visible halo. The thin default seat is enough to
            // hold text over a gridline.
            HighFigure = false,

            // The grid is MIXED into the paper (a lerp toward the line color), not added as light,
            // so it behaves like grain and can stay voiced on panels. An additive family — glowing
            // scanlines, caustics — would need roughly 0.22 here instead. Blueprint also READS this
            // gain as its furniture flag (the skin lowers it exactly for panel-like raised
            // elements), so it must stay below 1: a tab strip draws as quiet chrome, not as a
            // keyed control plate.
            PanelTextureGain = 0.85f,

            // The LINEWORK is the accent, and on the reference sheet a danger control is inked
            // red line-and-letter, not merely red-stocked — so the accent adopts hard. Neutral
            // controls are untouched (adoption is chroma-gated); only genuinely semantic colors
            // re-ink their figure.
            AccentAdoptStrength = 0.85f,

            // Plotting a drawing wants a beat longer than a snap.
            InDuration = 1.0f,
        };

        /// <summary>
        /// Registered before any tree attaches. <c>[assembly: AlwaysLinkAssembly]</c> in
        /// AssemblyInfo.cs is what keeps this alive through managed stripping in a player build —
        /// without it the family silently would not exist and every ds-fx-blueprint marker would do
        /// nothing.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => DsFxRegistry.Register(Family);

#if UNITY_EDITOR
        /// <summary>The editor runs no RuntimeInitializeOnLoadMethod outside play mode, so tooling
        /// (the compile check, an inspector preview) needs the family registered here too.</summary>
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterInEditor() => DsFxRegistry.Register(Family);
#endif
    }
}
#endif
