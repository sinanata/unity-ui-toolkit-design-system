using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using DesignSystem.Runtime.Typography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace DesignSystem.Editor.Typography
{
    /// <summary>
    /// Knows what is in Google Fonts and how to get it.
    ///
    /// <para>The download story is worth stating plainly, because the obvious routes are all
    /// dead ends. <c>fonts.googleapis.com/css</c> and <c>css2</c> answer a non-browser client
    /// with an opaque <c>/l/font?kit=</c> subset slice, and <c>fonts.google.com/download</c>
    /// returns the website's HTML rather than a zip. Both are meant for browsers, not tools.
    /// The canonical source — the one Google itself publishes from — is the
    /// <c>google/fonts</c> git repository, which serves raw <c>.ttf</c> over HTTPS. That is
    /// what this uses, and it is also why the same URLs work from inside a WebGL build:
    /// raw.githubusercontent.com sends <c>Access-Control-Allow-Origin: *</c>.</para>
    ///
    /// <para>The whole catalogue comes down in two requests: the family metadata, and one
    /// recursive git-tree listing (2000+ families, ~7 MB, not truncated). Both are cached
    /// under <c>Library/</c>, so this is a once-per-machine cost.</para>
    /// </summary>
    public static class GoogleFontsCatalog
    {
        private const string MetadataUrl = "https://fonts.google.com/metadata/fonts";
        private const string TreeUrl = "https://api.github.com/repos/google/fonts/git/trees/main?recursive=1";

        private static readonly string CacheDir =
            Path.Combine(Directory.GetCurrentDirectory(), "Library", "DsGoogleFonts");

        // ------------------------------------------------------------------- model

        public sealed class Family
        {
            public string Name;
            public string Category;      // "Sans Serif", "Serif", "Display", "Handwriting", "Monospace"
            public string Dir;           // "ofl/inter"
            public List<FontFile> Files = new();
            public int Popularity = int.MaxValue;
            public bool IsNoto;

            public bool HasVariable => Files.Any(f => f.Variable);

            /// <summary>Weights we expect to get. A variable file yields the full ramp.</summary>
            public IEnumerable<int> LikelyWeights =>
                HasVariable
                    ? new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 }
                    : Files.Where(f => !f.Italic).Select(f => f.Weight).Distinct().OrderBy(w => w);

            public override string ToString() => $"{Name} ({Category})";
        }

        public sealed class FontFile
        {
            public string Name;      // "Inter[opsz,wght].ttf"
            public int Weight;       // hint only; the parsed font is authoritative
            public bool Italic;
            public bool Variable;

            public string Url(string dir) =>
                DsGoogleFonts.RawRoot + dir + "/" + Uri.EscapeDataString(Name);
        }

        // ------------------------------------------------------------------ loading

        private static List<Family> _cache;

        /// <summary>The catalogue, sorted by popularity. Downloads and caches on first call.</summary>
        public static List<Family> Load(bool forceRefresh = false)
        {
            if (_cache != null && !forceRefresh) return _cache;

            Directory.CreateDirectory(CacheDir);

            string treeJson = Cached("tree.json", TreeUrl, forceRefresh);
            if (treeJson == null) return _cache ?? new List<Family>();

            // Metadata is a nice-to-have (real display names, category, popularity). The tree
            // alone is enough to import, so a failure here degrades rather than blocks.
            string metaJson = Cached("metadata.json", MetadataUrl, forceRefresh);

            var families = FromTree(treeJson);
            if (metaJson != null) Decorate(families, metaJson);

            _cache = families.Values
                .OrderBy(f => f.Popularity)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return _cache;
        }

        // Every .ttf in the repo lives at <license>/<slug>/<file>, and the slug IS the family
        // name lowercased with everything non-alphanumeric removed ("Noto Sans JP" ->
        // "notosansjp"). That convention is what lets the metadata and the tree be joined.
        private static Dictionary<string, Family> FromTree(string json)
        {
            var families = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in EnumerateTreePaths(json))
            {
                if (!path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = path.Split('/');

                // Exactly <license>/<slug>/<file>. Nested folders (static/, article/) are
                // duplicates or documentation, and a static/ copy would double every weight.
                if (parts.Length != 3) continue;
                if (parts[0] != "ofl" && parts[0] != "apache" && parts[0] != "ufl") continue;

                string slug = parts[1];
                string file = parts[2];

                if (!families.TryGetValue(slug, out var fam))
                {
                    // Provisional name from the filename. The metadata pass overwrites it with
                    // the real one; this is only what survives for a family Google has not
                    // published metadata for.
                    fam = new Family { Dir = parts[0] + "/" + slug, Name = DisplayName(file) };
                    families[slug] = fam;
                }

                var (weight, italic, variable) = GuessStyle(file);
                fam.Files.Add(new FontFile
                {
                    Name = file,
                    Weight = weight,
                    Italic = italic,
                    Variable = variable,
                });
            }

            // A family that ships a variable file needs nothing else; drop the statics so a
            // runtime fetch pulls one 850 KB file instead of eighteen.
            foreach (var fam in families.Values)
            {
                if (!fam.HasVariable) continue;
                fam.Files = fam.Files.Where(f => f.Variable).ToList();
            }

            return families;
        }

        /// <summary>"Noto Sans JP" -> "notosansjp", which is the folder it lives in.</summary>
        public static string Slug(string family) =>
            new string((family ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

        // JsonUtility cannot express the tree API's shape, and pulling in a JSON library for
        // one array of strings is not worth it. We only need the "path" values.
        private static IEnumerable<string> EnumerateTreePaths(string json)
        {
            const string key = "\"path\":\"";
            int i = 0;

            while (true)
            {
                i = json.IndexOf(key, i, StringComparison.Ordinal);
                if (i < 0) yield break;

                i += key.Length;
                int end = json.IndexOf('"', i);
                if (end < 0) yield break;

                yield return json.Substring(i, end - i);
                i = end;
            }
        }

        // Joins the metadata onto the tree, keyed by slug -- the only key the two actually
        // share. This is also where families get their REAL names, and it matters: deriving a
        // name from "JetBrainsMono[wght].ttf" yields "Jet Brains Mono", and two different
        // folders can easily derive the SAME string, which is how this first blew up.
        //
        // Whitespace-tolerant on purpose. Google serves this JSON pretty-printed, so scanning
        // for the minified `{"family":"` finds nothing at all -- and finds it silently, leaving
        // every category and popularity blank while the import still appears to succeed.
        private static readonly Regex FamilyRx = new(@"""family""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex CategoryRx = new(@"""category""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex PopularityRx = new(@"""popularity""\s*:\s*(\d+)", RegexOptions.Compiled);
        private static readonly Regex NotoRx = new(@"""isNoto""\s*:\s*true", RegexOptions.Compiled);

        private static void Decorate(Dictionary<string, Family> families, string json)
        {
            var hits = FamilyRx.Matches(json);
            int matched = 0;

            for (int i = 0; i < hits.Count; i++)
            {
                string name = hits[i].Groups[1].Value;
                if (!families.TryGetValue(Slug(name), out var fam)) continue;

                // One family's metadata runs from its "family" key to the next one's.
                int start = hits[i].Index;
                int end = i + 1 < hits.Count ? hits[i + 1].Index : json.Length;
                string block = json.Substring(start, end - start);

                fam.Name = name;
                matched++;

                var cat = CategoryRx.Match(block);
                if (cat.Success) fam.Category = cat.Groups[1].Value;

                fam.IsNoto = NotoRx.IsMatch(block);

                var pop = PopularityRx.Match(block);
                if (pop.Success && int.TryParse(pop.Groups[1].Value, out int p)) fam.Popularity = p;
            }

            if (matched == 0)
                Debug.LogWarning("[DsFonts] The Google Fonts metadata matched no families. Names " +
                                 "and categories will be guessed from filenames. Has the feed changed shape?");
        }

        // ---------------------------------------------------------------- filenames

        /// <summary>"Inter[opsz,wght].ttf" -> "Inter"; "NotoSansJP[wght].ttf" -> "Noto Sans JP".</summary>
        private static string DisplayName(string file)
        {
            string s = Path.GetFileNameWithoutExtension(file);

            int bracket = s.IndexOf('[');
            if (bracket > 0) s = s.Substring(0, bracket);

            int dash = s.IndexOf('-');
            if (dash > 0) s = s.Substring(0, dash);

            // "NotoSansJP" -> "Noto Sans JP": split before a capital that follows a lowercase,
            // and before the last capital of a run that is followed by a lowercase.
            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool boundary = i > 0 && char.IsUpper(c) &&
                                (!char.IsUpper(s[i - 1]) ||
                                 (i + 1 < s.Length && char.IsLower(s[i + 1])));

                if (boundary) sb.Append(' ');
                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// A first guess at what a file contains, from its name. Only ever used to decide which
        /// files are worth downloading — once the bytes are here, <see cref="OpenTypeFace"/>
        /// reads the real weight out of the font's own tables.
        /// </summary>
        private static (int weight, bool italic, bool variable) GuessStyle(string file)
        {
            string s = Path.GetFileNameWithoutExtension(file);
            bool variable = s.Contains('[');

            int bracket = s.IndexOf('[');
            if (bracket > 0) s = s.Substring(0, bracket);

            int dash = s.IndexOf('-');
            string style = dash >= 0 ? s.Substring(dash + 1) : "";

            bool italic = style.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0;
            style = style.Replace("Italic", "", StringComparison.OrdinalIgnoreCase);

            int weight = style switch
            {
                "Thin" => 100,
                "ExtraLight" or "UltraLight" => 200,
                "Light" => 300,
                "" or "Regular" or "Normal" or "Book" => 400,
                "Medium" => 500,
                "SemiBold" or "DemiBold" => 600,
                "Bold" => 700,
                "ExtraBold" or "UltraBold" => 800,
                "Black" or "Heavy" => 900,
                _ => 400,
            };

            return (weight, italic, variable);
        }

        // ---------------------------------------------------------------- transport

        /// <summary>
        /// A font file's bytes, from the on-disk cache when we already have them. Re-importing
        /// the showcase's set is ~40 MB of CJK; downloading that again on every tweak is a
        /// tax nobody should pay twice.
        /// </summary>
        public static byte[] Download(Family family, FontFile file)
        {
            string cached = Path.Combine(CacheDir, "files", family.Dir.Replace('/', '_') + "_" + file.Name);

            if (File.Exists(cached))
                return File.ReadAllBytes(cached);

            var bytes = Get(file.Url(family.Dir), $"Downloading {file.Name}", out string error);
            if (bytes == null)
            {
                Debug.LogError($"[DsFonts] Could not download '{file.Name}': {error}");
                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(cached) ?? CacheDir);
            File.WriteAllBytes(cached, bytes);
            return bytes;
        }

        /// <summary>The family's licence, so an imported font carries its terms with it.</summary>
        public static string DownloadLicense(Family family)
        {
            foreach (string name in new[] { "OFL.txt", "LICENSE.txt" })
            {
                var bytes = Get(DsGoogleFonts.RawRoot + family.Dir + "/" + name, null, out _);
                if (bytes != null) return Encoding.UTF8.GetString(bytes);
            }

            return null;
        }

        private static string Cached(string name, string url, bool force)
        {
            string path = Path.Combine(CacheDir, name);

            if (!force && File.Exists(path))
                return File.ReadAllText(path);

            var bytes = Get(url, $"Fetching {name}", out string error);
            if (bytes == null)
            {
                // A stale cache beats no catalogue at all when the network is down.
                if (File.Exists(path)) return File.ReadAllText(path);

                Debug.LogError($"[DsFonts] Could not fetch the Google Fonts catalogue: {error}");
                return null;
            }

            string text = Encoding.UTF8.GetString(bytes);
            File.WriteAllText(path, text);
            return text;
        }

        // The editor has no coroutine to lean on, so pump the request. It is behind an explicit
        // button press, and the progress bar keeps it cancellable.
        private static byte[] Get(string url, string progressTitle, out string error)
        {
            error = null;

            using var req = UnityWebRequest.Get(url);
            req.SetRequestHeader("User-Agent", "unity-ui-toolkit-design-system");

            var op = req.SendWebRequest();

            try
            {
                while (!op.isDone)
                {
                    if (progressTitle != null &&
                        EditorUtility.DisplayCancelableProgressBar("Google Fonts", progressTitle, req.downloadProgress))
                    {
                        req.Abort();
                        error = "Cancelled.";
                        return null;
                    }

                    Thread.Sleep(16);
                }
            }
            finally
            {
                if (progressTitle != null) EditorUtility.ClearProgressBar();
            }

            if (req.result == UnityWebRequest.Result.Success)
                return req.downloadHandler.data;

            error = $"{req.responseCode} {req.error}";
            return null;
        }

        // ----------------------------------------------------------------- manifest

        /// <summary>
        /// Writes the compact manifest a PLAYER needs to fetch fonts at runtime: family name,
        /// repo folder, filenames. Nothing else — it ships inside the build.
        /// </summary>
        public static void ExportRuntimeManifest(string assetPath)
        {
            var families = Load();
            if (families.Count == 0) return;

            var manifest = new DsGoogleFonts.Manifest
            {
                entries = families.Select(f => new DsGoogleFonts.Entry
                {
                    family = f.Name,
                    category = f.Category,
                    dir = f.Dir,
                    files = f.Files.Select(x => x.Name).ToArray(),
                }).ToArray(),
            };

            Directory.CreateDirectory(Path.GetDirectoryName(assetPath) ?? ".");
            File.WriteAllText(assetPath, JsonUtility.ToJson(manifest));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            Debug.Log($"[DsFonts] Runtime manifest: {manifest.entries.Length} families -> {assetPath} " +
                      $"({new FileInfo(assetPath).Length / 1024} KB)");
        }
    }
}
