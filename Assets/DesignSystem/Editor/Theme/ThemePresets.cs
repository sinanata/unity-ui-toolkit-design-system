using System;
using System.Collections.Generic;
using System.IO;
using DesignSystem.Runtime.Theme.Data;
using UnityEditor;
using UnityEngine;

namespace DesignSystem.Editor.Theming
{
    /// <summary>
    /// Generates the two themes the package ships with, so a consumer who installs it gets
    /// working themes rather than an empty authoring tool.
    ///
    ///   Resources/UI/Themes/Dark   — scope <c>:root</c>. The design system's own palette,
    ///                                identical to the values in DesignTokens.uss. This is the
    ///                                starting point you duplicate to make a brand theme.
    ///   Resources/UI/Themes/Light  — scope <c>.theme-light</c>. Day mode.
    ///
    /// Light is CLASS-scoped on purpose, and that is what makes the day/night pair work: the
    /// sheet sits on the root permanently and paints nothing until `theme-light` shows up on the
    /// element, so switching themes is one class toggle and the var() cascade repaints the tree.
    /// It also means Light and Dark coexist in one panel without fighting, which a pair of
    /// `:root` themes could not do.
    ///
    /// The colours are written as hex and parsed, not as float triples, so this file can be
    /// diffed against DesignTokens.uss and ShowcaseTheme.uss by eye.
    /// </summary>
    public static class ThemePresets
    {
        public const string ThemesFolder = "Assets/DesignSystem/Resources/UI/Themes";
        public const string DarkPath     = ThemesFolder + "/Dark.asset";
        public const string LightPath    = ThemesFolder + "/Light.asset";

        /// <summary>Resources path for <c>Resources.Load&lt;ThemeData&gt;</c>.</summary>
        public const string DarkResource  = "UI/Themes/Dark";
        public const string LightResource = "UI/Themes/Light";

        [MenuItem("Design System/Generate Built-in Themes")]
        public static void GenerateMenu()
        {
            var made = Generate();
            Debug.Log($"[ThemePresets] Generated and baked {made} built-in theme(s) into {ThemesFolder}.");
        }

        /// <summary>
        /// Batch entry point:
        /// <c>Unity -batchmode -quit -executeMethod DesignSystem.Editor.Theming.ThemePresets.GenerateBatch</c>
        /// </summary>
        public static void GenerateBatch()
        {
            var made = Generate();
            Debug.Log($"[ThemePresets] batch: generated {made} built-in theme(s).");

            if (Application.isBatchMode)
                EditorApplication.Exit(made == 2 ? 0 : 1);
        }

        public static int Generate()
        {
            var themes = new List<ThemeData>
            {
                Upsert(DarkPath,  ThemeData.RootScope, ApplyDark),
                Upsert(LightPath, ".theme-light",      ApplyLight),
            };

            AssetDatabase.SaveAssets();
            var baked = ThemeBaker.BakeMany(themes, out var failed);

            if (failed > 0)
                Debug.LogError($"[ThemePresets] {failed} built-in theme(s) failed to bake.");

            AssetDatabase.Refresh();
            return baked;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Dark — mirrors DesignTokens.uss exactly. If a token changes there, change it here.
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyDark(ThemeData t)
        {
            t.primaryColor        = Hex("#22C55E");
            t.primaryHoverColor   = Hex("#16A34A");
            t.primaryPressColor   = Hex("#15803D");
            t.primarySoftColor    = Hex("#22C55E", 0.16f);

            t.secondaryColor      = Hex("#3B82F6");
            t.secondaryHoverColor = Hex("#2563EB");
            t.secondaryPressColor = Hex("#1D4ED8");
            t.secondarySoftColor  = Hex("#3B82F6", 0.16f);

            t.tertiaryColor       = Hex("#A855F7");
            t.tertiaryHoverColor  = Hex("#9333EA");
            t.tertiaryPressColor  = Hex("#7E22CE");
            t.tertiarySoftColor   = Hex("#A855F7", 0.16f);

            t.warningColor        = Hex("#F59E0B");
            t.warningHoverColor   = Hex("#D97706");
            t.warningSoftColor    = Hex("#F59E0B", 0.16f);

            t.dangerColor         = Hex("#EF4444");
            t.dangerHoverColor    = Hex("#DC2626");
            t.dangerPressColor    = Hex("#B91C1C");
            t.dangerSoftColor     = Hex("#EF4444", 0.16f);

            t.textPrimaryColor    = Hex("#F2F4F7");
            t.textSecondaryColor  = Hex("#A1A7B3");
            t.textDisabledColor   = Hex("#677085");
            t.textOnAccentColor   = Hex("#0B0F17");

            t.bgColor             = Hex("#0B0F17");
            t.surfaceColor        = Hex("#131A24");
            t.surfaceElevColor    = Hex("#1A2330");
            t.surfaceHoverColor   = Hex("#1F2937");
            t.borderColor         = Hex("#263041");
            t.borderStrongColor   = Hex("#364152");
            t.overlayColor        = new Color(0f, 0f, 0f, 0.6f);

            ApplyRarity(t);
            ApplyMetrics(t);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Light — mirrors the `.theme-light` block in Showcase/Resources/ShowcaseTheme.uss.
        // Brand accents shift darker so they keep contrast on white surfaces; surfaces and text
        // invert. Rarity is deliberately NOT re-tinted: rarity carries meaning, and meaning does
        // not change with the lights.
        // ──────────────────────────────────────────────────────────────────────

        private static void ApplyLight(ThemeData t)
        {
            t.primaryColor        = Hex("#16A34A");
            t.primaryHoverColor   = Hex("#15803D");
            t.primaryPressColor   = Hex("#14532D");
            t.primarySoftColor    = Hex("#16A34A", 0.16f);

            t.secondaryColor      = Hex("#2563EB");
            t.secondaryHoverColor = Hex("#1D4ED8");
            t.secondaryPressColor = Hex("#1E40AF");
            t.secondarySoftColor  = Hex("#2563EB", 0.16f);

            t.tertiaryColor       = Hex("#9333EA");
            t.tertiaryHoverColor  = Hex("#7E22CE");
            t.tertiaryPressColor  = Hex("#6B21A8");
            t.tertiarySoftColor   = Hex("#9333EA", 0.16f);

            t.warningColor        = Hex("#D97706");
            t.warningHoverColor   = Hex("#B45309");
            t.warningSoftColor    = Hex("#D97706", 0.16f);

            t.dangerColor         = Hex("#DC2626");
            t.dangerHoverColor    = Hex("#B91C1C");
            t.dangerPressColor    = Hex("#991B1B");
            t.dangerSoftColor     = Hex("#DC2626", 0.16f);

            t.textPrimaryColor    = Hex("#0F172A");
            t.textSecondaryColor  = Hex("#475569");
            t.textDisabledColor   = Hex("#94A3B8");
            t.textOnAccentColor   = Hex("#FFFFFF");

            t.bgColor             = Hex("#F8FAFC");
            t.surfaceColor        = Hex("#FFFFFF");
            t.surfaceElevColor    = Hex("#F1F5F9");
            t.surfaceHoverColor   = Hex("#E2E8F0");
            t.borderColor         = Hex("#E2E8F0");
            t.borderStrongColor   = Hex("#CBD5E1");
            t.overlayColor        = new Color(0f, 0f, 0f, 0.5f);

            ApplyRarity(t);
            ApplyMetrics(t);
        }

        private static void ApplyRarity(ThemeData t)
        {
            t.rarityCommonColor    = Hex("#22C55E");
            t.rarityRareColor      = Hex("#3B82F6");
            t.rarityEpicColor      = Hex("#A855F7");
            t.rarityLegendaryColor = Hex("#F59E0B");
        }

        // Geometry and motion are the same in both moods: turning the lights on does not make a
        // button a different size. Kept explicit rather than left at the field defaults so a
        // generated theme is a complete statement of the design system, not a diff against it.
        private static void ApplyMetrics(ThemeData t)
        {
            t.fontSizeH1 = 26f; t.fontSizeH2 = 20f; t.fontSizeH3 = 16f;
            t.fontSizeBody1 = 14f; t.fontSizeBody2 = 12f; t.fontSizeCaption = 11f;

            t.radiusXs = 4f; t.radiusSm = 6f; t.radiusMd = 8f; t.radiusLg = 12f; t.radiusXl = 16f;

            t.spacing1 = 4f; t.spacing2 = 8f; t.spacing3 = 12f; t.spacing4 = 16f;
            t.spacing5 = 20f; t.spacing6 = 24f; t.spacing8 = 32f;

            t.borderThin = 1f; t.borderRegular = 2f;

            t.transitionFast = 120f; t.transitionMedium = 200f;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Plumbing
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates the theme if it is missing, otherwise rewrites the one that is there. Updating
        /// in place matters: the asset's GUID survives, so a ThemeApplier in a scene that already
        /// points at this theme keeps pointing at it after a regenerate.
        /// </summary>
        public static ThemeData Upsert(string assetPath, string scope, Action<ThemeData> fill)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath)?.Replace('\\', '/'));

            var theme = AssetDatabase.LoadAssetAtPath<ThemeData>(assetPath);
            if (!theme)
            {
                theme = ScriptableObject.CreateInstance<ThemeData>();
                AssetDatabase.CreateAsset(theme, assetPath);
            }

            theme.scopeSelector = scope;
            fill(theme);

            EditorUtility.SetDirty(theme);
            return theme;
        }

        /// <summary>Creates every missing folder on the way to <paramref name="folder"/>.</summary>
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            var built = parts[0];                       // "Assets"

            for (var i = 1; i < parts.Length; i++)
            {
                var next = built + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);
                built = next;
            }
        }

        public static Color Hex(string hex, float alpha = 1f)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c))
            {
                Debug.LogError($"[ThemePresets] '{hex}' is not a colour.");
                return Color.magenta;
            }

            c.a = alpha;
            return c;
        }
    }
}
