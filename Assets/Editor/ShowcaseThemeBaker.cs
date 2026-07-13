using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesignSystem.Editor.Theming;
using DesignSystem.Runtime.Theme.Data;
using Showcase.Runtime;
using UnityEditor;
using UnityEngine;

namespace UIDocumentDesignSystem.BuildTools
{
    /// <summary>
    /// Bakes the 12 bundled Codigrate palettes into ThemeData assets, so the showcase can paint
    /// them through the USS cascade instead of stamping inline styles onto every element.
    ///
    /// Read the header of CodigrateThemeApplier for why the inline walk exists at all: Unity has
    /// no way to set a `var(--…)` custom property at runtime, and no way to compile a StyleSheet
    /// from a string in a player, so a palette that first appears at RUNTIME can only be applied
    /// by walking the tree. That is a real constraint and Randomize still lives under it.
    ///
    /// But these twelve palettes are not runtime palettes. codigrate.com sends no CORS headers,
    /// so the WebGL build — the live demo, the thing everyone actually sees — has ALWAYS read them
    /// from the bundled JSON mirror. They are known at build time. So we bake them at build time,
    /// and the showcase gets, for free, everything the inline stamper explicitly could not do:
    /// real :hover, real :disabled, real :checked, and a dropdown popup that changes colour.
    ///
    /// SCOPE. A light palette bakes to `.theme-light`, a dark one to `:root` — matching the class
    /// the showcase already puts on the root for that palette's appearance. That is not cosmetic:
    /// it means every token override is compared against an IDENTICAL selector already in the
    /// cascade (`:root` in DesignTokens.uss, `.theme-light` in the Light preset), so load order
    /// alone decides the winner and no cross-selector specificity question is ever asked. The
    /// applier adds its sheet last, so it wins.
    /// </summary>
    public static class ShowcaseThemeBaker
    {
        private const string SourceDir = "Assets/Showcase/Resources/CodigrateThemes";

        public const string ThemesDir      = "Assets/Showcase/Resources/Themes";
        public const string ResourcePrefix = "Themes/";

        [MenuItem("Design System/Bake Showcase Themes")]
        public static void BakeMenu()
        {
            var baked = Bake(out var failed);
            if (failed > 0) Debug.LogError($"[ShowcaseThemeBaker] Baked {baked}, failed {failed}.");
            else            Debug.Log($"[ShowcaseThemeBaker] Baked {baked} showcase theme(s) into {ThemesDir}.");
        }

        /// <summary>
        /// Batch entry point. Generates the package's built-in Dark / Light pair AND the showcase
        /// palettes in one pass, because the build needs both:
        /// <c>Unity -batchmode -quit -executeMethod UIDocumentDesignSystem.BuildTools.ShowcaseThemeBaker.BakeAllBatch</c>
        /// </summary>
        public static void BakeAllBatch()
        {
            var presets = ThemePresets.Generate();
            var baked   = Bake(out var failed);

            Debug.Log($"[ShowcaseThemeBaker] batch: presets={presets} showcase={baked} failed={failed}");

            if (Application.isBatchMode)
                EditorApplication.Exit(failed > 0 || presets != 2 ? 1 : 0);
        }

        public static int Bake(out int failed)
        {
            failed = 0;

            if (!Directory.Exists(SourceDir))
            {
                Debug.LogError($"[ShowcaseThemeBaker] No bundled palettes at {SourceDir}.");
                failed = 1;
                return 0;
            }

            var themes = new List<ThemeData>();

            // Sorted, so a regenerate produces the same assets in the same order on every machine.
            var files = Directory.GetFiles(SourceDir, "*.json")
                                 .OrderBy(p => p, StringComparer.Ordinal);

            foreach (var file in files)
            {
                var slug = Path.GetFileNameWithoutExtension(file);
                if (slug == "list") continue;                       // the catalogue, not a palette

                CodigrateThemeProvider.ThemePalette palette;
                try
                {
                    palette = CodigrateThemeProvider.ParsePaletteJson(File.ReadAllText(file));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ShowcaseThemeBaker] {slug}.json did not parse: {e.Message}");
                    failed++;
                    continue;
                }

                // The runtime looks the baked asset up by the palette's metadata key, so the two
                // have to agree. They do today; this catches the day an upstream rename quietly
                // breaks the lookup and every theme silently falls back to the inline stamper.
                if (!string.Equals(palette.Key, slug, StringComparison.Ordinal))
                {
                    Debug.LogError(
                        $"[ShowcaseThemeBaker] {slug}.json declares key '{palette.Key}'. The runtime " +
                        "resolves themes by key, so the asset would never be found. Rename one to match.");
                    failed++;
                    continue;
                }

                var light = string.Equals(palette.Appearance, "light", StringComparison.OrdinalIgnoreCase);
                var scope = light ? ".theme-light" : ThemeData.RootScope;
                var map   = CodigrateThemeApplier.FromCodigrate(palette);

                // Upsert starts from a fresh ThemeData whose defaults ARE the design system's, so
                // everything the palette does not speak to — rarity, type scale, radii, spacing,
                // motion — keeps the design system's values. A palette changes the colours, not
                // the geometry.
                themes.Add(ThemePresets.Upsert(
                    $"{ThemesDir}/{palette.Key}.asset",
                    scope,
                    theme => CodigrateThemeApplier.WriteInto(map, theme)));
            }

            AssetDatabase.SaveAssets();

            var baked = ThemeBaker.BakeMany(themes, out var bakeFailures);
            failed += bakeFailures;

            AssetDatabase.Refresh();
            return baked;
        }
    }
}
