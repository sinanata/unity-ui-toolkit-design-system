using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Data
{
    [CreateAssetMenu(fileName = "Theme", menuName = "Design System/Theme", order = 100)]
    public class ThemeData : ScriptableObject
    {
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
        public float borderThin   = 1f;
        public float borderRegular = 2f;

        [Header("Motion")]
        public float transitionFast   = 120f;
        public float transitionMedium = 200f;

        [HideInInspector, SerializeField]
        private StyleSheet styleSheet;

        public StyleSheet StyleSheet => styleSheet;

        // ──────────────────────────────────────────────────
        // USS Variable Mapping
        // ──────────────────────────────────────────────────

        private static readonly Dictionary<string, string> FieldToUss = new()
        {
            ["primaryColor"]         = "--color-primary",
            ["primaryHoverColor"]    = "--color-primary-hover",
            ["primaryPressColor"]    = "--color-primary-press",
            ["primarySoftColor"]     = "--color-primary-soft",
            ["secondaryColor"]       = "--color-secondary",
            ["secondaryHoverColor"]  = "--color-secondary-hover",
            ["secondaryPressColor"]  = "--color-secondary-press",
            ["secondarySoftColor"]   = "--color-secondary-soft",
            ["tertiaryColor"]        = "--color-tertiary",
            ["tertiaryHoverColor"]   = "--color-tertiary-hover",
            ["tertiaryPressColor"]   = "--color-tertiary-press",
            ["tertiarySoftColor"]    = "--color-tertiary-soft",
            ["warningColor"]         = "--color-warning",
            ["warningHoverColor"]    = "--color-warning-hover",
            ["warningSoftColor"]     = "--color-warning-soft",
            ["dangerColor"]          = "--color-danger",
            ["dangerHoverColor"]     = "--color-danger-hover",
            ["dangerPressColor"]     = "--color-danger-press",
            ["dangerSoftColor"]      = "--color-danger-soft",
            ["textPrimaryColor"]     = "--color-text-primary",
            ["textSecondaryColor"]   = "--color-text-secondary",
            ["textDisabledColor"]    = "--color-text-disabled",
            ["textOnAccentColor"]    = "--color-text-on-accent",
            ["bgColor"]              = "--color-bg",
            ["surfaceColor"]         = "--color-surface",
            ["surfaceElevColor"]     = "--color-surface-elev",
            ["surfaceHoverColor"]    = "--color-surface-hover",
            ["borderColor"]          = "--color-border",
            ["borderStrongColor"]    = "--color-border-strong",
            ["overlayColor"]         = "--color-overlay",
            ["rarityCommonColor"]    = "--color-rarity-common",
            ["rarityRareColor"]      = "--color-rarity-rare",
            ["rarityEpicColor"]      = "--color-rarity-epic",
            ["rarityLegendaryColor"] = "--color-rarity-legendary",
            ["fontSizeH1"]           = "--font-size-h1",
            ["fontSizeH2"]           = "--font-size-h2",
            ["fontSizeH3"]           = "--font-size-h3",
            ["fontSizeBody1"]        = "--font-size-body-1",
            ["fontSizeBody2"]        = "--font-size-body-2",
            ["fontSizeCaption"]      = "--font-size-caption",
            ["radiusXs"]             = "--radius-xs",
            ["radiusSm"]             = "--radius-sm",
            ["radiusMd"]             = "--radius-md",
            ["radiusLg"]             = "--radius-lg",
            ["radiusXl"]             = "--radius-xl",
            ["spacing1"]             = "--space-1",
            ["spacing2"]             = "--space-2",
            ["spacing3"]             = "--space-3",
            ["spacing4"]             = "--space-4",
            ["spacing5"]             = "--space-5",
            ["spacing6"]             = "--space-6",
            ["spacing8"]             = "--space-8",
            ["borderThin"]           = "--border-thin",
            ["borderRegular"]        = "--border-regular",
            ["transitionFast"]       = "--transition-fast",
            ["transitionMedium"]     = "--transition-medium",
        };

        // ReSharper disable once InconsistentNaming
        private static Dictionary<string, FieldInfo> s_fieldMap;

        private static Dictionary<string, FieldInfo> GetFieldMap()
        {
            if (s_fieldMap != null) return s_fieldMap;

            s_fieldMap = new Dictionary<string, FieldInfo>();
            var fields = typeof(ThemeData).GetFields(
                BindingFlags.Public | BindingFlags.Instance);

            foreach (var f in fields)
            {
                if (f.FieldType != typeof(Color) && f.FieldType != typeof(float))
                    continue;
                if (FieldToUss.TryGetValue(f.Name, out var uss))
                    s_fieldMap[uss] = f;
            }

            return s_fieldMap;
        }

        public static Dictionary<string, FieldInfo> GetTokenMap() => GetFieldMap();

        // ──────────────────────────────────────────────────
        // USS Generation
        // ──────────────────────────────────────────────────

        public string GenerateUssString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("/* Generated by Theme Configurator */");
            sb.AppendLine(":root {");

            var map = GetFieldMap();
            foreach (var kv in map)
            {
                var value = kv.Value.GetValue(this);
                switch (value)
                {
                    case Color col:
                        sb.AppendLine($"  {kv.Key}: {ColorToUss(col)};");
                        break;
                    case float f:
                        var unit = kv.Key.Contains("transition") ? "ms" : "px";
                        sb.AppendLine($"  {kv.Key}: {f:F0}{unit};");
                        break;
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string ColorToUss(Color c)
        {
            if (Mathf.Approximately(c.a, 1f))
                return $"#{ColorUtility.ToHtmlStringRGB(c)}";

            var r = Mathf.RoundToInt(c.r * 255f);
            var g = Mathf.RoundToInt(c.g * 255f);
            var b = Mathf.RoundToInt(c.b * 255f);
            return $"rgba({r}, {g}, {b}, {c.a:F2})";
        }

        public void SetStyleSheetReference(StyleSheet sheet)
        {
            styleSheet = sheet;
        }

        public void ClearStyleSheetReference()
        {
            styleSheet = null;
        }
    }
}
