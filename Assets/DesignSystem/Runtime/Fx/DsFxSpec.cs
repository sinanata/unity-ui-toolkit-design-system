#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// The four tone levels one material renders a whole screen with. A material UI is not
    /// "components tinted wood-color" — it is ONE material worked at different depths, and
    /// this is the ladder that says how deep.
    ///
    ///   Bg      — the page itself. Deepest cut (dark themes) / lightest board (light themes).
    ///   Surface — sections, cards, sheets: panels resting on the page.
    ///   Raised  — buttons, tabs, chips, nav items, draggers: boards standing proud of their panel.
    ///   Well    — input trays, toggle tracks, slider grooves, progress tracks: carved INTO the
    ///             surface. Always the deep tone plus the shader's sunken profile.
    ///
    /// Ladder DIRECTION follows appearance: dark themes run page-deepest to control-lightest,
    /// light themes the reverse.
    /// </summary>
    public enum DsFxTone
    {
        /// <summary>No ladder: render the variant's exact colors on the raised profile. What a
        /// hand-authored marker with no tone gets — a showroom swatch, a one-off hero surface.</summary>
        None = 0,
        Bg = 1,
        Surface = 2,
        Raised = 3,
        Well = 4,
    }

    /// <summary>
    /// Everything the <c>ds-fx-</c> marker classes on one element said, parsed.
    ///
    /// The grammar:
    ///   ds-fx-&lt;family&gt;[--&lt;variant&gt;]    the material itself; family must be registered
    ///   ds-fx-text-carve | ds-fx-text-solid   engraved-and-inked lettering | lettering MADE of the material
    ///   ds-fx-frame                           shade the border band only; the fill passes through
    ///   ds-fx-worn[--heavy]                   wear
    ///   ds-fx-static                          freeze the idle animation
    ///   ds-fx-in--build|sweep|fade            entrance choreography
    ///   ds-fx-out--build|sweep|fade           exit choreography
    ///
    /// Plus three markers <see cref="DsFxTheme"/> writes at runtime, which you may also author
    /// by hand when driving elements yourself:
    ///   ds-fx-tone--bg|surface|raised|well    render at that ladder tone and surface profile
    ///   ds-fx-adopt                           tint the palette from the element's OWN resolved
    ///                                         background, so semantic color survives the material
    ///                                         (a danger button stays recognizably red)
    ///   ds-fx-inert                           decoration, not a control: no hover, press, click or
    ///                                         focus response. A section is a wall, not a button.
    ///
    /// Unknown <c>ds-fx-</c> heads are ignored rather than thrown: a marker vocabulary that
    /// hard-fails on a typo would take a whole screen down over one class.
    /// </summary>
    public sealed class DsFxSpec
    {
        /// <summary>The prefix every marker in this vocabulary carries.</summary>
        public const string Prefix = "ds-fx-";

        public DsFxFamily Family;
        public DsFxVariant Variant;
        public int TextMode;    // 0 none, 1 carve, 2 solid
        public int FillMode;    // 0 fill (+integrated frame), 1 frame-only, 2 text-only
        public float Wear;
        public bool Static;
        public int InStyle;     // 0 build, 1 sweep, 2 fade
        public int OutStyle;
        public bool AdoptColor;
        public DsFxTone Tone;
        public bool Inert;

        public static int ParseAnimStyle(string s) => s switch { "sweep" => 1, "fade" => 2, _ => 0 };

        /// <summary>
        /// Parse an element's class list. Null when the element carries no
        /// <c>ds-fx-&lt;registered-family&gt;</c> marker — which is the overwhelmingly common case,
        /// so this stays a cheap prefix test per class.
        /// </summary>
        public static DsFxSpec FromClasses(IEnumerable<string> classes)
        {
            DsFxSpec spec = null;
            string variantName = null;
            int textMode = 0, fillMode = 0, inStyle = 0, outStyle = 0;
            float wear = 0f;
            bool statik = false, adopt = false, isIcon = false, inert = false;
            var tone = DsFxTone.None;

            foreach (var cls in classes)
            {
                if (cls == null)
                    continue;
                if (cls == "ds-icon")
                {
                    // A design-system icon is VectorImage geometry: its material treatment rides
                    // the text modes, which need the background cleared (fill-mode 2) so only the
                    // vector shape reaches the shader's icon pass.
                    isIcon = true;
                    continue;
                }
                if (!cls.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;

                var body = cls.Substring(Prefix.Length);
                var split = body.IndexOf("--", StringComparison.Ordinal);
                var head = split >= 0 ? body.Substring(0, split) : body;
                var mod = split >= 0 ? body.Substring(split + 2) : null;

                switch (head)
                {
                    case "text-carve": textMode = 1; break;
                    case "text-solid": textMode = 2; break;
                    case "frame": fillMode = 1; break;
                    case "worn": wear = mod == "heavy" ? 1f : 0.6f; break;
                    case "static": statik = true; break;
                    case "adopt": adopt = true; break;
                    case "inert": inert = true; break;
                    case "tone":
                        tone = mod switch
                        {
                            "bg" => DsFxTone.Bg,
                            "surface" => DsFxTone.Surface,
                            "raised" => DsFxTone.Raised,
                            "well" => DsFxTone.Well,
                            _ => DsFxTone.None,
                        };
                        break;
                    case "in": inStyle = ParseAnimStyle(mod); break;
                    case "out": outStyle = ParseAnimStyle(mod); break;
                    default:
                        if (DsFxRegistry.TryGet(head, out var family))
                        {
                            spec ??= new DsFxSpec();
                            spec.Family = family;
                            variantName = mod;
                        }
                        break;
                }
            }

            if (spec == null)
                return null;

            spec.Variant = spec.Family.Find(variantName);
            spec.TextMode = textMode;
            // Solid text stands alone (no fill under it); an icon with ANY text mode does too —
            // its vector shape IS the thing being materialized.
            spec.FillMode = textMode == 2 || (isIcon && textMode != 0) ? 2 : fillMode;
            spec.Wear = wear;
            spec.Static = statik;
            spec.InStyle = inStyle;
            spec.OutStyle = outStyle;
            spec.AdoptColor = adopt;
            spec.Tone = tone;
            spec.Inert = inert;
            return spec;
        }
    }
}
#endif
