using System.Collections.Generic;
using System.Linq;
using DesignSystem.Editor.Typography;
using DesignSystem.Runtime.Typography;
using Showcase.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace UIDocumentDesignSystem.BuildTools
{
    /// <summary>
    /// Bakes the fonts the live showcase ships with: the families its picker offers, and the
    /// script fallbacks that let it render every language it claims to.
    ///
    /// Host-project code, not part of the package. Run it from
    /// <c>Design System &gt; Showcase &gt; Bake Showcase Fonts</c>, or in CI:
    ///   Unity -batchmode -quit -executeMethod
    ///     UIDocumentDesignSystem.BuildTools.ShowcaseFontBaker.BakeBatch
    /// </summary>
    public static class ShowcaseFontBaker
    {
        private const string Root = "Assets/Showcase/Resources/DsFonts";
        private const string TextSettingsPath = "Assets/Showcase/Resources/DsPanelTextSettings.asset";
        private const string ManifestPath = "Assets/Showcase/Resources/GoogleFontsManifest.json";
        private const string PanelSettingsPath = "Assets/Showcase/Resources/DefaultPanelSettings.asset";
        private const string FontSetPath = "Assets/Showcase/Resources/ShowcaseFontSet.asset";

        private static readonly int[] FullRamp = { 100, 200, 300, 400, 500, 600, 700, 800, 900 };

        /// <summary>
        /// The SCRIPT fallbacks we bundle, in order. Base Noto Sans is not here — it is the default
        /// family below, and a family must never fall back into itself. Its Regular is prepended to
        /// the stored chain instead, so it stays the Latin/Greek/Cyrillic catch-all for any FETCHED
        /// font that lacks those scripts.
        ///
        /// Every entry here needs SHAPING — Arabic joins its letters, Devanagari and Bengali and
        /// Tamil and Khmer reorder their clusters, Thai stacks its marks — and it is bundled so
        /// those scripts render offline, on the first frame, without a download. (A downloaded font
        /// CAN be shaped now; see DsFonts.TryEnableShaping. Bundling is a choice about the default
        /// experience, no longer a hard limit.) This whole list is about 5 MB. CJK is the one that
        /// is enormous — 36 MB — and it needs no shaping, so it is fetched, which is where the size
        /// win lives.
        /// </summary>
        private static readonly (string dir, string label)[] ScriptFallbacks =
        {
            ("ofl/notosansarabic", "Arabic"),
            ("ofl/notosanshebrew", "Hebrew"),
            ("ofl/notosansdevanagari", "Devanagari"),
            ("ofl/notosansbengali", "Bengali"),
            ("ofl/notosanstamil", "Tamil"),
            ("ofl/notosansthai", "Thai"),
            ("ofl/notosanskhmer", "Khmer"),
        };

        /// <summary>
        /// The one family the showcase ships as its base and default. Noto Sans, because with the
        /// script fallbacks behind it it covers, out of the box and with no network, every language
        /// the availability table lists as bundled — Latin, Greek, Cyrillic from itself, the shaping
        /// scripts from the chain. Everything else is a live Google Fonts fetch; there is no other
        /// bundled picker family, by design.
        /// </summary>
        private static readonly (string dir, int[] weights, bool italics)[] Families =
        {
            ("ofl/notosans", null, false),   // variable: the whole weight ramp for one file
        };

        [MenuItem("Design System/Showcase/Bake Showcase Fonts")]
        public static void Bake() => Run();

        public static void BakeBatch()
        {
            if (!Run()) EditorApplication.Exit(1);
            EditorApplication.Exit(0);
        }

        private static bool Run()
        {
            var catalog = GoogleFontsCatalog.Load();
            if (catalog.Count == 0)
            {
                Debug.LogError("[ShowcaseFonts] Empty catalogue; nothing baked.");
                return false;
            }

            // Script fallbacks first: the base family chains into them, so they have to exist by then.
            var scriptChain = new List<FontAsset>();

            foreach (var (dir, label) in ScriptFallbacks)
            {
                var family = Find(catalog, dir);
                if (family == null)
                {
                    Debug.LogError($"[ShowcaseFonts] '{dir}' is not in the catalogue.");
                    return false;
                }

                // 400 AND 700, so a BOLD heading in Arabic gets a real bold Arabic face rather than
                // dropping to regular. It costs one extra atlas, not another file: both weights are
                // named instances of the same variable font.
                var built = GoogleFontsImporter.Import(family, new GoogleFontsImporter.Options
                {
                    Weights = new[] { 400, 700 },
                    IncludeItalics = false,
                    OutputRoot = Root,
                    WriteStylesheet = false,     // a fallback is never the active typeface
                    Fallbacks = null,            // and must not chain into other fallbacks
                });

                if (built == null) return false;

                var regular = built.Regular;
                if (regular) scriptChain.Add(regular);

                Debug.Log($"[ShowcaseFonts] fallback ready: {label} ({built.familyName})");
            }

            // The base family (Noto Sans). It carries the SCRIPT chain only, never itself.
            var choices = new List<ShowcaseFontSet.Choice>();
            DsFontFamily baseFamily = null;

            foreach (var (dir, weights, italics) in Families)
            {
                var family = Find(catalog, dir);
                if (family == null)
                {
                    Debug.LogError($"[ShowcaseFonts] '{dir}' is not in the catalogue.");
                    return false;
                }

                var built = GoogleFontsImporter.Import(family, new GoogleFontsImporter.Options
                {
                    Weights = weights ?? FullRamp,
                    IncludeItalics = italics,
                    OutputRoot = Root,
                    Fallbacks = scriptChain,
                    WriteStylesheet = true,
                    UpgradeTypeRamp = true,
                });

                if (built == null) return false;

                // The base family owns a STYLESHEET, not just a root font: the sheet re-points
                // .ds-h3 at the real 600 face and the .ds-weight-* utilities at the ramp. A fetched
                // font has no compiled sheet and applies inline; this one bundled family is what
                // still demonstrates the token re-pointing.
                string slug = FontAssetFactory.Sanitize(built.familyName);
                var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{Root}/{slug}/{slug}.uss");

                if (sheet == null)
                {
                    Debug.LogError($"[ShowcaseFonts] No stylesheet at {Root}/{slug}/{slug}.uss.");
                    return false;
                }

                choices.Add(new ShowcaseFontSet.Choice { family = built, sheet = sheet });
                if (baseFamily == null) baseFamily = built;
            }

            // The stored chain that FETCHED fonts and the coverage checks resolve against. Base Noto
            // Sans leads it (the Latin/Greek/Cyrillic catch-all for a fetched font that has none),
            // then the script fallbacks.
            var fullChain = new List<FontAsset>();
            if (baseFamily && baseFamily.Regular) fullChain.Add(baseFamily.Regular);
            fullChain.AddRange(scriptChain);

            WriteFontSet(choices, fullChain);
            WriteTextSettings(fullChain);
            GoogleFontsCatalog.ExportRuntimeManifest(ManifestPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ShowcaseFonts] Done. {choices.Count} base family, {fullChain.Count} fallbacks.");
            return true;
        }

        /// <summary>The picker's menu. Separates families you CHOOSE from families that only FALL BACK.</summary>
        private static void WriteFontSet(List<ShowcaseFontSet.Choice> choices, List<FontAsset> chain)
        {
            var set = AssetDatabase.LoadAssetAtPath<ShowcaseFontSet>(FontSetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<ShowcaseFontSet>();
                AssetDatabase.CreateAsset(set, FontSetPath);
            }

            set.choices = choices.ToArray();
            set.fallbacks = chain.ToArray();
            EditorUtility.SetDirty(set);
        }

        /// <summary>
        /// A PanelTextSettings carrying the chain, wired into the showcase's PanelSettings —
        /// which ships with <c>textSettings: {fileID: 0}</c>, i.e. none at all.
        ///
        /// This is the belt to <c>DsFonts.WireFallbacks</c>'s braces. The per-family chain
        /// covers the families we imported; this one also covers a font DOWNLOADED at runtime,
        /// which by definition never went through the importer.
        /// </summary>
        private static void WriteTextSettings(List<FontAsset> chain)
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(settings, TextSettingsPath);
            }

            settings.fallbackFontAssets = new List<FontAsset>(chain);
            EditorUtility.SetDirty(settings);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                Debug.LogError($"[ShowcaseFonts] No PanelSettings at {PanelSettingsPath}.");
                return;
            }

            panel.textSettings = settings;
            EditorUtility.SetDirty(panel);

            Debug.Log($"[ShowcaseFonts] PanelTextSettings wired with {chain.Count} fallbacks.");
        }

        private static GoogleFontsCatalog.Family Find(List<GoogleFontsCatalog.Family> catalog, string dir) =>
            catalog.FirstOrDefault(f => f.Dir == dir);
    }
}
