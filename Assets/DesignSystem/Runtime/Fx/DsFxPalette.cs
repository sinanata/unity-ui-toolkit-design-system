#if UNITY_6000_5_OR_NEWER
using UnityEngine;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// The colors a theme contributes to a material. Six values, deliberately: a material
    /// aligned to a theme must still look like the material.
    ///
    /// Build one from a <see cref="Theme.Data.ThemeData"/> asset with <see cref="FromThemeData"/>
    /// and the material follows your theme automatically; leave the whole thing null and the
    /// material renders in its own native hues.
    /// </summary>
    public sealed class DsFxThemeColors
    {
        public string Name;
        /// <summary>Palette appearance. Drives the tone ladder's DIRECTION (dark: page deepest,
        /// controls lightest; light: the reverse).</summary>
        public bool Light;
        /// <summary>Re-keys the material's accent outright: glints, embers, emissive, plus carets,
        /// selection and the focus rim.</summary>
        public Color Accent;
        /// <summary>The hue the material leans toward — a stain, not a repaint.</summary>
        public Color WindowBackground;
        /// <summary>Kept for reference and debugging; the ladder itself supplies depth ordering.</summary>
        public Color EditorBackground;
        /// <summary>Raised-chrome reference. Kept for the same reason as EditorBackground.</summary>
        public Color Surface;
        /// <summary>Biases the ink — but only as far as the contrast floor allows.</summary>
        public Color ForegroundPrimary;
        public Color ForegroundSecondary;

        /// <summary>
        /// Align a material to a design-system theme asset. This is the intended path: the same
        /// asset that drives every <c>var(--…)</c> token also drives the material, so a themed
        /// screen and its furniture cannot drift apart.
        ///
        /// Appearance is inferred from the page background's luma — a theme whose page is light IS
        /// a light theme — because <c>ThemeData</c> carries no explicit flag. Pass
        /// <paramref name="light"/> to override when a palette sits near the boundary and guesses
        /// wrong.
        /// </summary>
        public static DsFxThemeColors FromThemeData(Theme.Data.ThemeData theme, bool? light = null)
        {
            if (theme == null)
                return null;
            return new DsFxThemeColors
            {
                Name = theme.name,
                Light = light ?? Luma(theme.bgColor) > 0.5f,
                Accent = theme.primaryColor,
                WindowBackground = theme.surfaceColor,
                EditorBackground = theme.bgColor,
                Surface = theme.surfaceElevColor,
                ForegroundPrimary = theme.textPrimaryColor,
                ForegroundSecondary = theme.textSecondaryColor,
            };
        }

        private static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
    }

    /// <summary>The colors one tone level renders with, plus the inks that are readable on it.</summary>
    public struct DsFxTonePalette
    {
        public Color ColA;          // deep tone (alpha = master material alpha)
        public Color ColB;          // light tone
        public Color ColC;          // accent / glint / emissive
        public Color Ink;           // body/value text ON this tone — contrast-guaranteed
        public Color InkSecondary;  // captions, hints
        public Color Accent;        // carets, focus, selection — theme accent or variant ColC
        public Color Caret;         // accent, contrast-checked against the tone (falls back to Ink)
    }

    /// <summary>
    /// Derives the tone ladder from a variant, an appearance, and an optional theme.
    ///
    /// ALL color customization happens here, C#-side, once per skin — the shaders stay pure
    /// renderers of whatever palette arrives. That split is what lets you write a family without
    /// thinking about theming at all.
    ///
    /// The rule the whole file obeys: the material always keeps its VALUE STRUCTURE, so its
    /// texture stays readable. Themes contribute hue, saturation and accent only. That is the
    /// difference between native stained wood and tinted plastic.
    /// </summary>
    public static class DsFxPalette
    {
        // Neutral ink poles. A theme may pull them toward its foreground, never past the floor.
        private static readonly Color InkDark = new Color32(23, 27, 33, 255);
        private static readonly Color InkLight = new Color32(242, 245, 248, 255);

        /// <summary>WCAG-ish contrast floor for body/value text on material tones.</summary>
        public const float BodyContrastFloor = 4.5f;

        public static DsFxTonePalette Derive(DsFxVariant v, DsFxTone tone, bool light, DsFxThemeColors theme)
        {
            var p = new DsFxTonePalette { ColA = v.ColA, ColB = v.ColB, ColC = v.ColC };

            if (tone != DsFxTone.None)
            {
                // Panels carry the RICHEST figure in the frame; a small collapse just keeps them
                // from out-shouting controls, and the texture gain does the rest.
                if (tone == DsFxTone.Surface)
                {
                    var center = Color.Lerp(v.ColA, v.ColB, 0.5f);
                    var a = Color.Lerp(v.ColA, center, 0.25f); a.a = v.ColA.a;
                    var b = Color.Lerp(v.ColB, center, 0.25f); b.a = v.ColB.a;
                    p.ColA = a;
                    p.ColB = b;
                }

                if (light)
                {
                    // Light ladder: page lightest, raised most tinted.
                    switch (tone)
                    {
                        case DsFxTone.Bg: p.ColA = TowardWhite(v.ColA, 0.34f); p.ColB = TowardWhite(v.ColB, 0.34f); break;
                        case DsFxTone.Surface: p.ColA = TowardWhite(p.ColA, 0.16f); p.ColB = TowardWhite(p.ColB, 0.16f); break;
                        case DsFxTone.Raised: p.ColA = ScaleValue(v.ColA, 0.88f); p.ColB = ScaleValue(v.ColB, 0.88f); break;
                        case DsFxTone.Well: p.ColA = TowardWhite(v.ColA, 0.22f); p.ColB = TowardWhite(v.ColB, 0.22f); break;
                    }
                }
                else
                {
                    // Dark ladder: page deepest, raised lightest. Panels sit a step darker than
                    // controls; wells drop to a deep tray.
                    switch (tone)
                    {
                        case DsFxTone.Bg: p.ColA = ScaleValue(v.ColA, 0.58f); p.ColB = ScaleValue(v.ColB, 0.58f); break;
                        case DsFxTone.Surface: p.ColA = ScaleValue(p.ColA, 0.74f); p.ColB = ScaleValue(p.ColB, 0.74f); break;
                        case DsFxTone.Raised: p.ColA = ScaleValue(v.ColA, 1.16f); p.ColB = ScaleValue(v.ColB, 1.16f); break;
                        case DsFxTone.Well: p.ColA = ScaleValue(v.ColA, 0.36f); p.ColB = ScaleValue(v.ColB, 0.36f); break;
                    }
                }

                // Panel decorations sit a notch below control accents; the page a touch below that.
                if (tone == DsFxTone.Surface)
                    p.ColC = ScaleValue(v.ColC, 0.75f);
                else if (tone == DsFxTone.Bg)
                    p.ColC = ScaleValue(v.ColC, 0.85f);
            }

            // Theme alignment: hue/sat lean into the theme's body surface (chroma-gated — a
            // near-neutral theme leaves the material native), the accent re-keys ColC outright.
            // Value is never touched: walnut stays walnut-deep, it just takes the stain.
            if (theme != null)
            {
                p.ColA = GatedHueTransfer(p.ColA, theme.WindowBackground, 0.55f);
                p.ColB = GatedHueTransfer(p.ColB, theme.WindowBackground, 0.55f);
                p.ColC = GatedHueTransfer(p.ColC, theme.Accent, 0.90f);
            }
            p.Accent = theme != null ? theme.Accent : v.ColC;

            // Inks: the material's own enamel first, then (if themed) pulled toward the theme
            // foreground ONLY while the contrast floor holds. The floor wins over the theme, every
            // time. All checks run against the EFFECTIVE mid — what a translucent surface actually
            // reads as over the page.
            var mid = EffectiveMid(p.ColA, p.ColB, light);
            var neutral = PaintedInk(p.ColA, p.ColB, light);
            p.Ink = neutral;
            if (theme != null)
            {
                var themed = Color.Lerp(neutral, theme.ForegroundPrimary, 0.22f);
                if (Contrast(themed, mid) >= BodyContrastFloor)
                    p.Ink = themed;
            }
            p.InkSecondary = Color.Lerp(p.Ink, mid, 0.30f);

            // Caret: the accent, unless it would vanish against the tone.
            p.Caret = Contrast(p.Accent, mid) >= 3.0f ? p.Accent : p.Ink;
            return p;
        }

        /// <summary>
        /// The color a translucent surface actually READS as: its own tones blended over the page
        /// behind it. A pale pane at 16% alpha has a light mid of its own, but on a dark page it
        /// reads near-black — and picking ink from the pane's own colors puts dark text on a dark
        /// view. Opaque materials pass through unchanged.
        /// </summary>
        public static Color EffectiveMid(Color colA, Color colB, bool lightBacked)
        {
            var mid = Color.Lerp(colA, colB, 0.5f);
            if (colA.a >= 0.9f)
                return mid;
            var page = lightBacked ? new Color(0.93f, 0.95f, 0.96f) : new Color(0.05f, 0.07f, 0.10f);
            return Color.Lerp(page, mid, Mathf.Clamp01(colA.a * 1.6f));
        }

        /// <summary>
        /// Enamel ink: the near-neutral pole warmed toward the material's own tones — cream on
        /// walnut, silver-white on iron, near-black on birch. Value stays at the pole so the floor
        /// holds; only hue and a bounded saturation come from the material.
        /// </summary>
        public static Color PaintedInk(Color colA, Color colB, bool lightBacked = false)
        {
            var mid = EffectiveMid(colA, colB, lightBacked);
            // Pole by plain LUMA, not a contrast contest: enamel in a routed groove gets its
            // separation from the dark rim, and silver on mid-gray steel is correct — a strict
            // 4.5:1 contest flips exactly those captions to near-black. Genuinely light boards
            // still take dark ink.
            var luma = 0.299f * mid.r + 0.587f * mid.g + 0.114f * mid.b;
            var dark = luma > 0.60f;
            var pole = dark ? InkDark : InkLight;
            var source = dark ? colA : colB;
            Color.RGBToHSV(source, out var h, out var s, out _);
            var tinted = Color.HSVToRGB(h, Mathf.Min(s * 0.60f, dark ? 0.50f : 0.38f), dark ? 0.16f : 0.97f);
            var ink = Color.Lerp(pole, tinted, 0.85f);
            ink.a = 1f;
            // Catastrophic floor only: when even rimmed paint drowns, swap to whichever neutral
            // pole actually separates. The threshold is DELIBERATELY low (2.2, not 3.0): on a
            // mid-value surface the raw ratio always favors the near-black pole by a little, and
            // a 3.0 floor flipped exactly the captions the luma pole got right — near-black
            // lettering (plus its white counter-shadow) on navy plates. That is the failure the
            // pole-by-luma comment above exists to prevent; the flip is reserved for genuine
            // drowning, not for losing a ratio contest the light pole was already passing.
            if (Contrast(ink, mid) < 2.2f)
                ink = Contrast(InkDark, mid) >= Contrast(InkLight, mid) ? InkDark : InkLight;
            return ink;
        }

        /// <summary>
        /// Blend the hue+saturation of <paramref name="source"/> into a material color, keeping the
        /// material's VALUE so its texture stays readable.
        ///
        /// Gated on CHROMA, not HSV saturation: near-neutral sources (dark navy cards, gray inputs,
        /// muted theme backgrounds) leave the material alone; only genuinely colorful sources tint
        /// it. That gate is what stops theming from turning every surface into a token rainbow.
        /// Alpha is never adopted — a translucent family rides its own ColA.a.
        /// </summary>
        public static Color GatedHueTransfer(Color mat, Color source, float amount)
        {
            if (source.a < 0.05f)
                return mat;
            var chroma = Mathf.Max(source.r, Mathf.Max(source.g, source.b))
                       - Mathf.Min(source.r, Mathf.Min(source.g, source.b));
            var k = Mathf.InverseLerp(0.10f, 0.32f, chroma) * amount;
            if (k <= 0f)
                return mat;
            Color.RGBToHSV(mat, out _, out var ms, out var mv);
            Color.RGBToHSV(source, out var eh, out var es, out _);
            var tinted = Color.HSVToRGB(eh, Mathf.Max(ms, es * 0.85f), mv);
            var result = Color.Lerp(mat, tinted, k);
            result.a = mat.a;
            return result;
        }

        /// <summary>WCAG relative-luminance contrast ratio (1..21).</summary>
        public static float Contrast(Color a, Color b)
        {
            var la = RelativeLuma(a);
            var lb = RelativeLuma(b);
            var hi = Mathf.Max(la, lb);
            var lo = Mathf.Min(la, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        private static float RelativeLuma(Color c)
        {
            static float Lin(float u) => u <= 0.03928f ? u / 12.92f : Mathf.Pow((u + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        }

        private static Color ScaleValue(Color c, float mul)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            var result = Color.HSVToRGB(h, s, Mathf.Clamp01(v * mul));
            result.a = c.a;
            return result;
        }

        private static Color TowardWhite(Color c, float k)
        {
            var result = Color.Lerp(c, Color.white, k);
            result.a = c.a;
            return result;
        }
    }
}
#endif
