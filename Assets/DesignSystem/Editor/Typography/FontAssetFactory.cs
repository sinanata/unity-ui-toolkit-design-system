using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesignSystem.Runtime.Typography;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace DesignSystem.Editor.Typography
{
    /// <summary>
    /// Turns a font file into a wired-up <see cref="DsFontFamily"/>: one
    /// <see cref="FontAsset"/> per weight, a weight table that makes bold and italic resolve
    /// to real typefaces, and a fallback chain for the scripts the family does not cover.
    ///
    /// Three things here are not obvious and all three are load-bearing:
    ///
    /// 1. <b>Weights come out of ONE file.</b> Most popular Google Fonts ship a single
    ///    variable <c>.ttf</c>. <see cref="OpenTypeFace"/> reads its named instances and hands
    ///    back a FreeType face index per weight; <c>CreateFontAsset</c> takes that index. So a
    ///    9-weight family costs one 850 KB file in the build, not nine.
    ///
    /// 2. <b><c>sourceFontFile</c> must be set, and its setter is internal.</b> A FontAsset
    ///    built from a path has no source font, so inside a BUILD it has nothing to rasterize
    ///    from and any glyph that was not baked at import time simply never appears. Wiring it
    ///    to the imported <see cref="Font"/> fixes that, and TextCore honours the face index
    ///    stored in <c>FaceInfo</c>, so the Bold asset keeps rasterizing Bold outlines.
    ///
    /// 3. <b>The weight table's setter is internal too.</b> Both go through
    ///    <see cref="SerializedObject"/>, which is also the only path that reliably persists.
    /// </summary>
    public static class FontAssetFactory
    {
        /// <summary>How glyphs get rasterized. Defaults match TMP's, which are tuned for UI text.</summary>
        public struct Settings
        {
            public int SamplingPointSize;
            public int AtlasPadding;
            public int AtlasWidth;
            public int AtlasHeight;
            public GlyphRenderMode RenderMode;

            public static Settings Default => new()
            {
                SamplingPointSize = 90,
                AtlasPadding = 9,
                AtlasWidth = 1024,
                AtlasHeight = 1024,
                RenderMode = GlyphRenderMode.SDFAA,
            };
        }

        /// <summary>A source file plus the faces we want out of it.</summary>
        public sealed class Source
        {
            public string AssetPath;              // "Assets/.../Inter.ttf"
            public OpenTypeFace Face;
            public IReadOnlyList<int> Weights;    // CSS buckets to extract, e.g. 400, 700
        }

        // A FontAsset's weight table has ten slots; slot N is weight N*100, and slot 0 is
        // unused. TextCore indexes it directly, so the shape is not ours to choose.
        private const int WeightSlots = 10;

        /// <summary>
        /// Builds (or rebuilds in place) a family under <paramref name="folder"/>.
        /// Existing assets are overwritten rather than replaced, so GUIDs survive and every
        /// scene, theme and stylesheet already pointing at this font keeps pointing at it.
        /// </summary>
        public static DsFontFamily Build(
            string familyName,
            string folder,
            IReadOnlyList<Source> sources,
            IReadOnlyList<FontAsset> fallbacks,
            Settings settings)
        {
            if (string.IsNullOrEmpty(familyName) || sources == null || sources.Count == 0)
                return null;

            EnsureFolder(folder);

            var upright = new FontAsset[DsFontFamily.WeightCount];
            var italic = new FontAsset[DsFontFamily.WeightCount];

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (var src in sources)
                {
                    if (src?.Face == null || src.Weights == null) continue;

                    var font = AssetDatabase.LoadAssetAtPath<Font>(src.AssetPath);
                    if (font == null)
                    {
                        Debug.LogError($"[DsFonts] '{src.AssetPath}' did not import as a Font.");
                        continue;
                    }

                    foreach (int weight in src.Weights)
                    {
                        var inst = PickInstance(src.Face, weight);
                        if (inst == null) continue;

                        var built = CreateFace(familyName, folder, src.AssetPath, font,
                                               inst.Value, weight, settings);
                        if (!built) continue;

                        int slot = DsFontFamily.BucketOf(weight);
                        if (inst.Value.Italic) italic[slot] = built;
                        else upright[slot] = built;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            if (upright.All(f => !f) && italic.All(f => !f))
            {
                Debug.LogError($"[DsFonts] '{familyName}' produced no faces.");
                return null;
            }

            var family = LoadOrCreate<DsFontFamily>($"{folder}/{Sanitize(familyName)}.asset");
            family.familyName = familyName;
            family.weights = upright;
            family.italics = italic;
            family.fallbacks = fallbacks?.Where(f => f).ToList() ?? new List<FontAsset>();

            WireWeightTables(family);
            DsFonts.WireFallbacks(family);

            foreach (var face in Faces(family))
                EditorUtility.SetDirty(face);

            EditorUtility.SetDirty(family);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return family;
        }

        // ------------------------------------------------------------------ one face

        private static FontAsset CreateFace(
            string familyName, string folder, string sourceAssetPath, Font font,
            OpenTypeFace.NamedInstance inst, int weight, Settings s)
        {
            // FreeType needs a real filesystem path, not an "Assets/…" one.
            string fullPath = Path.GetFullPath(sourceAssetPath);

            var fa = FontAsset.CreateFontAsset(
                fullPath, inst.FaceIndex,
                s.SamplingPointSize, s.AtlasPadding, s.RenderMode,
                s.AtlasWidth, s.AtlasHeight);

            if (fa == null)
            {
                Debug.LogError($"[DsFonts] FreeType refused '{sourceAssetPath}' at face index " +
                               $"0x{inst.FaceIndex:X} ({inst.Name}).");
                return null;
            }

            string faceName = $"{Sanitize(familyName)}-{StyleSuffix(weight, inst.Italic)}";
            string assetPath = $"{folder}/{faceName} SDF.asset";

            fa.name = faceName + " SDF";
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fa.isMultiAtlasTexturesEnabled = true;   // CJK will not fit one 1024 atlas

            // The two internal setters. Without m_SourceFontFile the asset cannot rasterize a
            // single new glyph once it is inside a build.
            var so = new SerializedObject(fa);
            var srcProp = so.FindProperty("m_SourceFontFile");
            if (srcProp == null)
            {
                Debug.LogError("[DsFonts] FontAsset has no m_SourceFontFile field. Unity's " +
                               "serialized layout changed; the importer needs updating.");
                return null;
            }

            srcProp.objectReferenceValue = font;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Populate the creation settings so Unity's own Font Asset inspector shows sane
            // values and its "Update Atlas Texture" button keeps working on our assets.
            var ces = fa.fontAssetCreationEditorSettings;
            ces.sourceFontFileGUID = AssetDatabase.AssetPathToGUID(sourceAssetPath);
            ces.faceIndex = inst.FaceIndex;
            ces.pointSize = s.SamplingPointSize;
            ces.pointSizeSamplingMode = 1;   // custom size, not "auto"
            ces.padding = s.AtlasPadding;
            ces.atlasWidth = s.AtlasWidth;
            ces.atlasHeight = s.AtlasHeight;
            ces.renderMode = (int)s.RenderMode;
            ces.includeFontFeatures = true;  // kerning + ligatures + Arabic joining forms
            fa.fontAssetCreationEditorSettings = ces;

            return WriteAsset(fa, assetPath);
        }

        // Overwrite in place when the asset already exists, so its GUID survives a re-import
        // and nothing that references it breaks.
        private static FontAsset WriteAsset(FontAsset fresh, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);

            if (existing == null)
            {
                AssetDatabase.CreateAsset(fresh, assetPath);
                AttachSubAssets(fresh, assetPath);
                return fresh;
            }

            // Drop the old atlas + material before copying over them, or every re-import leaves
            // another orphaned Texture2D inside the .asset and the file grows without bound.
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(assetPath))
                if (sub != existing && sub != null)
                    UnityEngine.Object.DestroyImmediate(sub, true);

            EditorUtility.CopySerialized(fresh, existing);
            AttachSubAssets(existing, assetPath);
            return existing;
        }

        // The atlas texture and material are created in memory by CreateFontAsset. They have to
        // become sub-assets or they vanish on reload and the font renders blank.
        private static void AttachSubAssets(FontAsset fa, string assetPath)
        {
            if (fa.atlasTextures != null)
            {
                for (int i = 0; i < fa.atlasTextures.Length; i++)
                {
                    var tex = fa.atlasTextures[i];
                    if (!tex || AssetDatabase.Contains(tex)) continue;

                    tex.name = $"{fa.name} Atlas{(i == 0 ? "" : $" {i}")}";
                    AssetDatabase.AddObjectToAsset(tex, assetPath);
                }
            }

            var mat = fa.material;
            if (mat && !AssetDatabase.Contains(mat))
            {
                mat.name = $"{fa.name} Material";
                AssetDatabase.AddObjectToAsset(mat, assetPath);
            }
        }

        // ------------------------------------------------------------- weight tables

        /// <summary>
        /// Gives every face the whole family's weight table.
        ///
        /// This is what makes the existing design system work unchanged. UI Toolkit has no
        /// <c>font-weight</c> property — only <c>-unity-font-style: bold</c> — and TextCore
        /// answers that by looking up slot 7 of the CURRENT font's weight table. Wire it and
        /// every one of the design system's bold rules resolves to the family's real Bold.
        /// Leave it empty and TextCore fakes bold by dilating the Regular outline, which is
        /// what the 42 components have been getting until now.
        ///
        /// <b>Exactly one face carries the table, and it never points at itself.</b> That is not
        /// tidiness, it is a hard requirement of the advanced text generator. Unity's
        /// <c>NativeFontAsset.HasRecursion</c> walks the weight table as well as the fallback
        /// chain and calls ANY revisited node a cycle — so a table wired onto every face contains
        /// that very face, every face is instantly circular, and ATG answers with a console full
        /// of <i>"Circular reference detected. Cannot add Inter-Black SDF to the fallbacks."</i>
        /// and then DROPS the entry, quietly taking real bold with it. Wiring more faces buys
        /// nothing and costs the whole feature.
        ///
        /// One table on Regular is also what Unity's own Font Asset inspector models, and it is
        /// sufficient: body text is laid out through Regular, so bold, italic and bold-italic all
        /// find their real face from there. A heading already pinned to SemiBold by
        /// <c>.ds-weight-600</c> needs no table, because <c>DsFonts.ApplyWeight</c> clears the bold
        /// flag — the face IS the weight.
        /// </summary>
        private static void WireWeightTables(DsFontFamily family)
        {
            var owner = family.Regular;
            if (!owner) return;

            var so = new SerializedObject(owner);
            var table = so.FindProperty("m_FontWeightTable");
            if (table == null)
            {
                Debug.LogError("[DsFonts] FontAsset has no m_FontWeightTable field. Unity's " +
                               "serialized layout changed; the importer needs updating.");
                return;
            }

            if (table.arraySize < WeightSlots) table.arraySize = WeightSlots;

            for (int slot = 1; slot < WeightSlots; slot++)
            {
                int bucket = slot - 1;   // slot 1 == weight 100 == bucket 0
                var pair = table.GetArrayElementAtIndex(slot);

                var upright = Get(family.weights, bucket);
                var italic = Get(family.italics, bucket);

                // Skip the owner: a face that lists itself is a one-node cycle, and ATG throws the
                // entire table away over it.
                pair.FindPropertyRelative("regularTypeface").objectReferenceValue =
                    upright == owner ? null : upright;
                pair.FindPropertyRelative("italicTypeface").objectReferenceValue =
                    italic == owner ? null : italic;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            owner.ReadFontAssetDefinition();

            // Clear every other face, or a table left behind by an older bake keeps right on
            // forming cycles even though we no longer write one.
            foreach (var face in Faces(family))
            {
                if (!face || face == owner) continue;

                var faceSo = new SerializedObject(face);
                var faceTable = faceSo.FindProperty("m_FontWeightTable");
                if (faceTable == null) continue;

                for (int slot = 1; slot < WeightSlots && slot < faceTable.arraySize; slot++)
                {
                    var pair = faceTable.GetArrayElementAtIndex(slot);
                    pair.FindPropertyRelative("regularTypeface").objectReferenceValue = null;
                    pair.FindPropertyRelative("italicTypeface").objectReferenceValue = null;
                }

                faceSo.ApplyModifiedPropertiesWithoutUndo();
                face.ReadFontAssetDefinition();
            }

            static FontAsset Get(FontAsset[] arr, int i) =>
                arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
        }

        // ---------------------------------------------------------------- selection

        /// <summary>
        /// The instance to use for a requested weight. Exact match wins; otherwise take the
        /// nearest, which is what makes a two-weight family still answer a request for 600.
        /// </summary>
        private static OpenTypeFace.NamedInstance? PickInstance(OpenTypeFace face, int weight)
        {
            if (face.Instances.Count == 0) return null;

            OpenTypeFace.NamedInstance best = default;
            int bestDistance = int.MaxValue;

            foreach (var inst in face.Instances)
            {
                int d = Math.Abs(OpenTypeFace.SnapToBucket(inst.Weight) - weight);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = inst;
            }

            // A static Bold file asked for 100 should not quietly answer with its Bold. Only
            // accept a substitute inside one bucket; beyond that the weight genuinely is absent.
            return bestDistance <= 100 ? best : (OpenTypeFace.NamedInstance?)null;
        }

        private static IEnumerable<FontAsset> Faces(DsFontFamily family)
        {
            foreach (var f in family.weights ?? Array.Empty<FontAsset>())
                if (f) yield return f;

            foreach (var f in family.italics ?? Array.Empty<FontAsset>())
                if (f) yield return f;
        }

        // ------------------------------------------------------------------- naming

        /// <summary>"Inter-SemiBoldItalic", the convention every foundry and Google Fonts uses.</summary>
        public static string StyleSuffix(int weight, bool italic)
        {
            string name = DsFontFamily.BucketOf(weight) switch
            {
                0 => "Thin",
                1 => "ExtraLight",
                2 => "Light",
                3 => "Regular",
                4 => "Medium",
                5 => "SemiBold",
                6 => "Bold",
                7 => "ExtraBold",
                _ => "Black",
            };

            if (!italic) return name;
            return name == "Regular" ? "Italic" : name + "Italic";
        }

        public static string Sanitize(string name) =>
            string.Concat((name ?? "Font").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            var built = parts[0];   // "Assets"

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
