using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Data
{
    /// <summary>
    /// A whole design-system palette, as an asset. Every field below maps to one
    /// <c>var(--…)</c> token in <c>DesignTokens.uss</c>. <see cref="GenerateUssString"/>
    /// renders them as a USS custom-property block, and the editor bakes that block
    /// into a companion <see cref="StyleSheet"/> stored inside this same asset.
    ///
    /// A theme therefore reaches the UI through the ordinary USS cascade: the applier
    /// adds ONE stylesheet, and every `:hover`, `:disabled` and `:checked` rule in the
    /// design system re-resolves against the new token values on its own. There is no
    /// per-component work, and nothing to keep in sync when a component is added.
    ///
    /// Why that matters: the alternative is walking the tree and stamping inline styles
    /// on every element, which is what the showcase's `CodigrateThemeApplier` has to do
    /// for a palette that only exists at RUNTIME — Unity cannot compile a StyleSheet
    /// from a string in a player build. Baking the theme at EDIT time sidesteps the
    /// whole problem. A genuinely runtime-generated palette (the showcase's Randomize
    /// button) is the only case that still needs the inline path.
    /// </summary>
    [CreateAssetMenu(fileName = "Theme", menuName = "Design System/Theme", order = 100)]
    public class ThemeData : ScriptableObject
    {
        public const string RootScope = ":root";

        [Header("Scope")]
        [Tooltip("The USS selector the generated token block is written under.\n\n" +
                 ":root — themes the whole panel. Use this for a single app-wide theme.\n\n" +
                 ".theme-x — themes any subtree carrying that class. Two themes can then " +
                 "coexist in one panel, and the applier adds the class for you. This is how " +
                 "the design system's own day / night pair is built: the Light theme is " +
                 "scoped to .theme-light, so it only paints once that class is present.")]
        public string scopeSelector = RootScope;

        [Header("Brand & Semantic")]
        public Color primaryColor        = new(0.133f, 0.773f, 0.369f);
        public Color primaryHoverColor   = new(0.086f, 0.639f, 0.290f);
        public Color primaryPressColor   = new(0.082f, 0.502f, 0.239f);
        public Color primarySoftColor    = new(0.133f, 0.773f, 0.369f, 0.16f);

        public Color secondaryColor      = new(0.231f, 0.510f, 0.965f);
        public Color secondaryHoverColor = new(0.145f, 0.388f, 0.922f);
        public Color secondaryPressColor = new(0.114f, 0.306f, 0.847f);
        public Color secondarySoftColor  = new(0.231f, 0.510f, 0.965f, 0.16f);

        public Color tertiaryColor        = new(0.659f, 0.333f, 0.969f);
        public Color tertiaryHoverColor   = new(0.576f, 0.200f, 0.918f);
        public Color tertiaryPressColor   = new(0.494f, 0.133f, 0.808f);
        public Color tertiarySoftColor    = new(0.659f, 0.333f, 0.969f, 0.16f);

        public Color warningColor        = new(0.961f, 0.620f, 0.043f);
        public Color warningHoverColor   = new(0.851f, 0.467f, 0.024f);
        public Color warningSoftColor    = new(0.961f, 0.620f, 0.043f, 0.16f);

        public Color dangerColor         = new(0.937f, 0.267f, 0.267f);
        public Color dangerHoverColor    = new(0.863f, 0.149f, 0.149f);
        public Color dangerPressColor    = new(0.725f, 0.110f, 0.110f);
        public Color dangerSoftColor     = new(0.937f, 0.267f, 0.267f, 0.16f);

        [Header("Text")]
        public Color textPrimaryColor    = new(0.949f, 0.957f, 0.969f);
        public Color textSecondaryColor  = new(0.631f, 0.655f, 0.702f);
        public Color textDisabledColor   = new(0.404f, 0.439f, 0.522f);
        public Color textOnAccentColor   = new(0.043f, 0.059f, 0.090f);

        [Header("Surfaces")]
        public Color bgColor             = new(0.043f, 0.059f, 0.090f);
        public Color surfaceColor        = new(0.075f, 0.102f, 0.141f);
        public Color surfaceElevColor    = new(0.102f, 0.137f, 0.188f);
        public Color surfaceHoverColor   = new(0.122f, 0.161f, 0.216f);
        public Color borderColor         = new(0.149f, 0.188f, 0.255f);
        public Color borderStrongColor   = new(0.212f, 0.255f, 0.322f);
        public Color overlayColor        = new(0f, 0f, 0f, 0.6f);

        [Header("Rarity")]
        public Color rarityCommonColor    = new(0.133f, 0.773f, 0.369f);
        public Color rarityRareColor      = new(0.231f, 0.510f, 0.965f);
        public Color rarityEpicColor      = new(0.659f, 0.333f, 0.969f);
        public Color rarityLegendaryColor = new(0.961f, 0.620f, 0.043f);

        [Header("Typography")]
        [Tooltip("The theme's typeface. Import one with Design System > Google Fonts.\n\n" +
                 "Leave it empty and the theme changes only colours and sizes, inheriting " +
                 "whatever face is already active. Set it and the theme is a complete look.")]
        public DesignSystem.Runtime.Typography.DsFontFamily typeface;

        public float fontSizeH1      = 26f;
        public float fontSizeH2      = 20f;
        public float fontSizeH3      = 16f;
        public float fontSizeBody1   = 14f;
        public float fontSizeBody2   = 12f;
        public float fontSizeCaption = 11f;

        [Header("Radius")]
        public float radiusXs = 4f;
        public float radiusSm = 6f;
        public float radiusMd = 8f;
        public float radiusLg = 12f;
        public float radiusXl = 16f;

        [Header("Spacing")]
        public float spacing1 = 4f;
        public float spacing2 = 8f;
        public float spacing3 = 12f;
        public float spacing4 = 16f;
        public float spacing5 = 20f;
        public float spacing6 = 24f;
        public float spacing8 = 32f;

        [Header("Border Widths")]
        public float borderThin    = 1f;
        public float borderRegular = 2f;

        [Header("Motion")]
        public float transitionFast   = 120f;
        public float transitionMedium = 200f;

        [HideInInspector, SerializeField]
        private StyleSheet styleSheet;

        /// <summary>The baked companion stylesheet, stored as a sub-asset of this theme.</summary>
        public StyleSheet StyleSheet => styleSheet;

        public void SetStyleSheetReference(StyleSheet sheet) => styleSheet = sheet;
        public void ClearStyleSheetReference() => styleSheet = null;

        /// <summary>
        /// The USS value that resolves to <see cref="typeface"/>, e.g.
        /// <c>resource("DsFonts/Inter/Inter-Regular SDF")</c>.
        ///
        /// Pre-rendered by the editor rather than computed on demand, because
        /// <see cref="GenerateUssString"/> is a RUNTIME method — the showcase calls it in the
        /// browser to display the live stylesheet — and turning an asset reference into a USS
        /// reference needs <c>AssetDatabase</c>, which does not exist in a player.
        /// </summary>
        [HideInInspector, SerializeField]
        private string typefaceUss;

        public void SetTypefaceUss(string uss) => typefaceUss = uss;

        // ──────────────────────────────────────────────────────────────────────
        // Scope
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>The selector the token block is emitted under. Never empty.</summary>
        public string Scope
        {
            get
            {
                var s = scopeSelector?.Trim();
                return string.IsNullOrEmpty(s) ? RootScope : s;
            }
        }

        /// <summary>
        /// The bare class name when this theme is class-scoped (".theme-tokyo" gives
        /// "theme-tokyo"), or null when it is a <c>:root</c> theme. The applier adds this
        /// class to the element it themes and removes it again on swap, so a class-scoped
        /// theme needs no wiring beyond assigning it.
        ///
        /// Anything more elaborate than a single class (a descendant or compound selector)
        /// returns null: the author is doing something deliberate and drives the class
        /// themselves.
        /// </summary>
        public string ScopeClass
        {
            get
            {
                var s = Scope;
                if (s.Length < 2 || s[0] != '.') return null;
                for (var i = 1; i < s.Length; i++)
                {
                    var ch = s[i];
                    if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_') return null;
                }
                return s.Substring(1);
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // Token table
        // ──────────────────────────────────────────────────────────────────────

        private enum TokenUnit { Color, Px, Ms }

        private readonly struct TokenBinding
        {
            public readonly string Field;
            public readonly string Token;
            public readonly TokenUnit Unit;

            public TokenBinding(string field, string token, TokenUnit unit)
            {
                Field = field;
                Token = token;
                Unit  = unit;
            }
        }

        // Declaration order here IS the order tokens are emitted in. This was a
        // Dictionary, and Dictionary iteration order is an implementation detail of
        // both the runtime and of reflection's field ordering — the generated .uss
        // came out shuffled on a different machine and the baked sub-asset churned in
        // every diff. An array pins it.
        //
        // Deliberately ABSENT: --radius-pill-*. Those are geometry constants, not brand
        // (--radius-pill-9 means "9px, which is half of an 18px-tall element"), and Unity
        // clamps border-radius per axis to half the side. A themeable pill radius is just
        // a way to turn a pill into an ellipse. Every pill in the system is locked to an
        // explicit px size for exactly this reason; see the note in DesignTokens.uss.
        private static readonly TokenBinding[] Tokens =
        {
            new("primaryColor",         "--color-primary",           TokenUnit.Color),
            new("primaryHoverColor",    "--color-primary-hover",     TokenUnit.Color),
            new("primaryPressColor",    "--color-primary-press",     TokenUnit.Color),
            new("primarySoftColor",     "--color-primary-soft",      TokenUnit.Color),

            new("secondaryColor",       "--color-secondary",         TokenUnit.Color),
            new("secondaryHoverColor",  "--color-secondary-hover",   TokenUnit.Color),
            new("secondaryPressColor",  "--color-secondary-press",   TokenUnit.Color),
            new("secondarySoftColor",   "--color-secondary-soft",    TokenUnit.Color),

            new("tertiaryColor",        "--color-tertiary",          TokenUnit.Color),
            new("tertiaryHoverColor",   "--color-tertiary-hover",    TokenUnit.Color),
            new("tertiaryPressColor",   "--color-tertiary-press",    TokenUnit.Color),
            new("tertiarySoftColor",    "--color-tertiary-soft",     TokenUnit.Color),

            new("warningColor",         "--color-warning",           TokenUnit.Color),
            new("warningHoverColor",    "--color-warning-hover",     TokenUnit.Color),
            new("warningSoftColor",     "--color-warning-soft",      TokenUnit.Color),

            new("dangerColor",          "--color-danger",            TokenUnit.Color),
            new("dangerHoverColor",     "--color-danger-hover",      TokenUnit.Color),
            new("dangerPressColor",     "--color-danger-press",      TokenUnit.Color),
            new("dangerSoftColor",      "--color-danger-soft",       TokenUnit.Color),

            new("textPrimaryColor",     "--color-text-primary",      TokenUnit.Color),
            new("textSecondaryColor",   "--color-text-secondary",    TokenUnit.Color),
            new("textDisabledColor",    "--color-text-disabled",     TokenUnit.Color),
            new("textOnAccentColor",    "--color-text-on-accent",    TokenUnit.Color),

            new("bgColor",              "--color-bg",                TokenUnit.Color),
            new("surfaceColor",         "--color-surface",           TokenUnit.Color),
            new("surfaceElevColor",     "--color-surface-elev",      TokenUnit.Color),
            new("surfaceHoverColor",    "--color-surface-hover",     TokenUnit.Color),
            new("borderColor",          "--color-border",            TokenUnit.Color),
            new("borderStrongColor",    "--color-border-strong",     TokenUnit.Color),
            new("overlayColor",         "--color-overlay",           TokenUnit.Color),

            new("rarityCommonColor",    "--color-rarity-common",     TokenUnit.Color),
            new("rarityRareColor",      "--color-rarity-rare",       TokenUnit.Color),
            new("rarityEpicColor",      "--color-rarity-epic",       TokenUnit.Color),
            new("rarityLegendaryColor", "--color-rarity-legendary",  TokenUnit.Color),

            new("fontSizeH1",           "--font-size-h1",            TokenUnit.Px),
            new("fontSizeH2",           "--font-size-h2",            TokenUnit.Px),
            new("fontSizeH3",           "--font-size-h3",            TokenUnit.Px),
            new("fontSizeBody1",        "--font-size-body-1",        TokenUnit.Px),
            new("fontSizeBody2",        "--font-size-body-2",        TokenUnit.Px),
            new("fontSizeCaption",      "--font-size-caption",       TokenUnit.Px),

            new("radiusXs",             "--radius-xs",               TokenUnit.Px),
            new("radiusSm",             "--radius-sm",               TokenUnit.Px),
            new("radiusMd",             "--radius-md",               TokenUnit.Px),
            new("radiusLg",             "--radius-lg",               TokenUnit.Px),
            new("radiusXl",             "--radius-xl",               TokenUnit.Px),

            new("spacing1",             "--space-1",                 TokenUnit.Px),
            new("spacing2",             "--space-2",                 TokenUnit.Px),
            new("spacing3",             "--space-3",                 TokenUnit.Px),
            new("spacing4",             "--space-4",                 TokenUnit.Px),
            new("spacing5",             "--space-5",                 TokenUnit.Px),
            new("spacing6",             "--space-6",                 TokenUnit.Px),
            new("spacing8",             "--space-8",                 TokenUnit.Px),

            new("borderThin",           "--border-thin",             TokenUnit.Px),
            new("borderRegular",        "--border-regular",          TokenUnit.Px),

            new("transitionFast",       "--transition-fast",         TokenUnit.Ms),
            new("transitionMedium",     "--transition-medium",       TokenUnit.Ms),
        };

        private static Dictionary<string, FieldInfo> s_fields;

        private static FieldInfo Field(string name)
        {
            if (s_fields == null)
            {
                s_fields = new Dictionary<string, FieldInfo>(Tokens.Length);
                foreach (var f in typeof(ThemeData).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (f.FieldType == typeof(Color) || f.FieldType == typeof(float))
                        s_fields[f.Name] = f;
            }

            return s_fields.TryGetValue(name, out var hit) ? hit : null;
        }

        // ──────────────────────────────────────────────────────────────────────
        // USS generation
        // ──────────────────────────────────────────────────────────────────────

        public string GenerateUssString()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("/* GENERATED from a ThemeData asset by the Design System Theme Configurator.");
            sb.AppendLine("   Do not edit: this block is rewritten from the asset on every save. */");
            sb.Append(Scope).AppendLine(" {");

            foreach (var t in Tokens)
            {
                var field = Field(t.Field);
                if (field == null) continue;

                var value = field.GetValue(this);
                var rendered = t.Unit switch
                {
                    TokenUnit.Color when value is Color c => ColorToUss(c),
                    TokenUnit.Px    when value is float p => Num(p) + "px",
                    TokenUnit.Ms    when value is float m => Num(m) + "ms",
                    _ => null,
                };
                if (rendered == null) continue;

                sb.Append("    ").Append(t.Token).Append(": ").Append(rendered).AppendLine(";");
            }

            sb.AppendLine("}");

            AppendTypeface(sb);
            return sb.ToString();
        }

        /// <summary>
        /// Emits the theme's typeface, when it has one.
        ///
        /// It targets <c>.ds-root</c> rather than the scope element, even though
        /// <c>-unity-font-definition</c> is inherited and setting it on the scope would reach
        /// the same elements. The reason is precedence: a family's own generated sheet already
        /// declares <c>.ds-root { -unity-font-definition: … }</c>, and a rule that matches an
        /// element directly always beats a value it merely inherited — so a theme that set the
        /// font on <c>:root</c> would lose to any family sheet on the same panel, silently.
        ///
        /// A scoped theme qualifies the selector with its own class, so a <c>.theme-light</c>
        /// theme cannot repaint the typeface of a panel that is not wearing it.
        /// </summary>
        private void AppendTypeface(StringBuilder sb)
        {
            // Empty is the normal case: most themes are palettes and inherit the active face.
            // It must emit NOTHING rather than an empty declaration -- the bake is all or
            // nothing, and `-unity-font-definition: ;` would fail to compile and take every
            // colour in the theme down with it.
            if (string.IsNullOrWhiteSpace(typefaceUss)) return;

            var scopeClass = ScopeClass;
            string selector = scopeClass == null
                ? ".ds-root"
                : $".{scopeClass} .ds-root, .{scopeClass}.ds-root";

            sb.AppendLine();
            sb.Append(selector).AppendLine(" {");
            sb.Append("    -unity-font-definition: ").Append(typefaceUss).AppendLine(";");
            sb.AppendLine("}");
        }

        // Every number that reaches USS goes through here, and it is InvariantCulture on
        // purpose. The editor runs under the OS locale, and on a Turkish / German / French
        // machine 0.16f.ToString("F2") is "0,16" — which emits `rgba(34, 197, 94, 0,16)`
        // and produces a stylesheet that silently fails to parse. This is not hypothetical
        // for this repo; it is the author's own locale.
        private static string Num(float v)
        {
            var rounded = Mathf.Round(v);
            return Mathf.Approximately(v, rounded)
                ? ((int)rounded).ToString(CultureInfo.InvariantCulture)
                : v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string ColorToUss(Color c)
        {
            if (c.a >= 0.9995f)
                return "#" + ColorUtility.ToHtmlStringRGB(c);

            var r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            var g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            var b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);

            return string.Format(
                CultureInfo.InvariantCulture,
                "rgba({0}, {1}, {2}, {3})", r, g, b, Num(c.a));
        }
    }
}
