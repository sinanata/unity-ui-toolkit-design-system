using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesignSystem.Runtime.Typography;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace DesignSystem.Editor.Typography
{
    /// <summary>
    /// Pulls a family out of Google Fonts and leaves behind everything the design system needs:
    /// the source <c>.ttf</c>, one <see cref="FontAsset"/> per weight, a
    /// <see cref="DsFontFamily"/> that ties them together, the licence, and a stylesheet that
    /// makes it the active typeface.
    /// </summary>
    public static class GoogleFontsImporter
    {
        /// <summary>Where imported families land. Under Resources so USS can <c>resource()</c> them.</summary>
        public const string DefaultRoot = "Assets/Resources/DsFonts";

        public sealed class Options
        {
            public IReadOnlyList<int> Weights = new[] { 400, 700 };
            public bool IncludeItalics;
            public string OutputRoot = DefaultRoot;
            public IReadOnlyList<FontAsset> Fallbacks;
            public bool WriteStylesheet = true;
            public bool UpgradeTypeRamp = true;
            public FontAssetFactory.Settings Atlas = FontAssetFactory.Settings.Default;
        }

        /// <summary>Downloads and imports. Returns null on failure, having logged why.</summary>
        public static DsFontFamily Import(GoogleFontsCatalog.Family family, Options options)
        {
            if (family == null) return null;
            options ??= new Options();

            string slug = FontAssetFactory.Sanitize(family.Name);
            string folder = $"{options.OutputRoot}/{slug}";

            var wanted = Choose(family, options);
            if (wanted.Count == 0)
            {
                Debug.LogError($"[DsFonts] '{family.Name}' has no file covering the requested weights.");
                return null;
            }

            EnsureFolder(folder);

            var sources = new List<FontAssetFactory.Source>();

            foreach (var file in wanted)
            {
                var bytes = GoogleFontsCatalog.Download(family, file);
                if (bytes == null) continue;

                // The font's own tables, not its filename, decide what is inside it. That matters:
                // Noto Sans JP's variable file reports Thin as its DEFAULT instance, so importing
                // it "as-is" would render every Japanese glyph hairline-thin.
                var face = OpenTypeFace.Read(bytes);
                if (face == null)
                {
                    Debug.LogError($"[DsFonts] '{file.Name}' is not a font we can read.");
                    continue;
                }

                // Google names variable files "Inter[opsz,wght].ttf". Brackets are legal in an
                // asset path but read badly everywhere else; the axes are recoverable from the
                // font itself, so drop them.
                string assetPath = $"{folder}/{slug}{(file.Italic ? "-Italic" : "")}" +
                                   $"{(file.Variable ? "" : "-" + FontAssetFactory.StyleSuffix(file.Weight, false))}.ttf";

                File.WriteAllBytes(assetPath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                // The FontAsset rasterizes new glyphs at runtime out of this Font, so the font
                // data has to actually be in it.
                if (AssetImporter.GetAtPath(assetPath) is TrueTypeFontImporter ttf && !ttf.includeFontData)
                {
                    ttf.includeFontData = true;
                    ttf.SaveAndReimport();
                }

                sources.Add(new FontAssetFactory.Source
                {
                    AssetPath = assetPath,
                    Face = face,
                    Weights = file.Variable
                        ? options.Weights.ToList()             // one variable file covers the ramp
                        : new List<int> { file.Weight },
                });
            }

            if (sources.Count == 0) return null;

            var built = FontAssetFactory.Build(family.Name, folder, sources, options.Fallbacks, options.Atlas);
            if (built == null) return null;

            SaveLicense(family, folder);

            if (options.WriteStylesheet)
                FontUssWriter.Write(built, folder, options.UpgradeTypeRamp);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[DsFonts] Imported {family.Name}: " +
                      $"{built.AvailableWeights().Count} weights" +
                      $"{(built.HasItalics() ? " + italics" : "")} -> {folder}");

            return built;
        }

        /// <summary>Imports a family already sitting on disk. No network, and works for any font.</summary>
        public static DsFontFamily ImportLocal(
            string familyName, IEnumerable<string> filePaths, Options options)
        {
            options ??= new Options();

            string slug = FontAssetFactory.Sanitize(familyName);
            string folder = $"{options.OutputRoot}/{slug}";
            EnsureFolder(folder);

            var sources = new List<FontAssetFactory.Source>();

            foreach (string src in filePaths)
            {
                var bytes = File.ReadAllBytes(src);
                var face = OpenTypeFace.Read(bytes);
                if (face == null)
                {
                    Debug.LogWarning($"[DsFonts] Skipped '{Path.GetFileName(src)}': not a readable font.");
                    continue;
                }

                string assetPath = $"{folder}/{Path.GetFileName(src)}";
                if (Path.GetFullPath(src) != Path.GetFullPath(assetPath))
                    File.Copy(src, assetPath, true);

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

                // A variable file can serve every requested weight. A static one only has the
                // weight it was cut at -- asking it for others just re-bakes the same outlines.
                var weights = face.IsVariable
                    ? options.Weights.ToList()
                    : new List<int> { OpenTypeFace.SnapToBucket(face.WeightClass) };

                sources.Add(new FontAssetFactory.Source
                {
                    AssetPath = assetPath,
                    Face = face,
                    Weights = weights,
                });
            }

            if (sources.Count == 0) return null;

            var built = FontAssetFactory.Build(familyName, folder, sources, options.Fallbacks, options.Atlas);
            if (built == null) return null;

            if (options.WriteStylesheet)
                FontUssWriter.Write(built, folder, options.UpgradeTypeRamp);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return built;
        }

        // ---------------------------------------------------------------- internals

        // A variable file carries the whole weight ramp, so one (plus its italic twin) is all we
        // need. Only a static-only family has to be assembled file by file.
        private static List<GoogleFontsCatalog.FontFile> Choose(
            GoogleFontsCatalog.Family family, Options options)
        {
            var picked = new List<GoogleFontsCatalog.FontFile>();

            if (family.HasVariable)
            {
                var upright = family.Files.FirstOrDefault(f => f.Variable && !f.Italic);
                if (upright != null) picked.Add(upright);

                if (options.IncludeItalics)
                {
                    var ital = family.Files.FirstOrDefault(f => f.Variable && f.Italic);
                    if (ital != null) picked.Add(ital);
                }

                return picked;
            }

            foreach (int weight in options.Weights)
            {
                var hit = family.Files.FirstOrDefault(f => !f.Italic && f.Weight == weight);
                if (hit != null) picked.Add(hit);

                if (!options.IncludeItalics) continue;

                var ital = family.Files.FirstOrDefault(f => f.Italic && f.Weight == weight);
                if (ital != null) picked.Add(ital);
            }

            return picked;
        }

        private static void SaveLicense(GoogleFontsCatalog.Family family, string folder)
        {
            string text = GoogleFontsCatalog.DownloadLicense(family);
            if (string.IsNullOrEmpty(text)) return;

            // .txt so Unity does not try to compile or import it as anything clever.
            File.WriteAllText($"{folder}/LICENSE.txt", text);
            AssetDatabase.ImportAsset($"{folder}/LICENSE.txt");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            string built = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{built}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(built, parts[i]);

                built = next;
            }
        }
    }
}
