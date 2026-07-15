// The only part of the design system that needs the network. Guarded so the package keeps
// its promise of compiling into a project that has stripped UnityWebRequest -- everything
// else here (the importer, the weight tables, the fallback chain) works without it.
#if DS_WEBREQUEST

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Typography
{
    /// <summary>
    /// Downloads a Google Font at runtime and builds a live <see cref="DsFontFamily"/> from it,
    /// weights and all — in the Editor, on desktop, and in a WebGL browser tab.
    ///
    /// <para><b>Where the fonts come from.</b> Not from the Google Fonts website: both CSS
    /// endpoints and <c>fonts.google.com/download</c> answer a script with HTML, and the
    /// <c>/l/font?kit=</c> URLs they hand out are opaque subset slices. The real, canonical,
    /// license-clean source is the upstream <c>google/fonts</c> git repository, which serves
    /// raw <c>.ttf</c> over HTTPS with <c>Access-Control-Allow-Origin: *</c> — so a browser can
    /// fetch it, which is the only reason the WebGL path works at all.</para>
    ///
    /// <para><b>Why a manifest.</b> A family's folder (<c>ofl/</c>, <c>apache/</c>, <c>ufl/</c>)
    /// and its filenames cannot be derived from its name. <see cref="Manifest"/> is generated
    /// at edit time from one call to the GitHub tree API and shipped as a small JSON.</para>
    /// </summary>
    public static class DsGoogleFonts
    {
        public const string RawRoot = "https://raw.githubusercontent.com/google/fonts/main/";

        // ------------------------------------------------------------------ manifest

        [Serializable]
        public class Entry
        {
            public string family;      // "Inter"
            public string category;    // "Sans Serif"
            public string dir;         // "ofl/inter"
            public string[] files;     // "Inter[opsz,wght].ttf", "Inter-Italic[opsz,wght].ttf"

            public string Url(string file) => RawRoot + dir + "/" + Uri.EscapeDataString(file);
        }

        [Serializable]
        public class Manifest
        {
            public Entry[] entries = Array.Empty<Entry>();

            private Dictionary<string, Entry> _index;

            private Dictionary<string, Entry> _byDir;

            public Entry Find(string family)
            {
                if (string.IsNullOrWhiteSpace(family)) return null;

                if (_index == null)
                {
                    _index = new Dictionary<string, Entry>(entries.Length, StringComparer.OrdinalIgnoreCase);
                    foreach (var e in entries)
                        if (e != null && !string.IsNullOrEmpty(e.family))
                            _index[e.family] = e;
                }

                return _index.TryGetValue(family.Trim(), out var hit) ? hit : null;
            }

            /// <summary>
            /// By repository folder. This is the key <see cref="DsScripts"/> uses, because a
            /// display name is a moving target ("Noto Sans JP" has also been "Noto Sans Japanese")
            /// while <c>ofl/notosansjp</c> is the actual, stable address of the actual bytes.
            /// </summary>
            public Entry FindByDir(string dir)
            {
                if (string.IsNullOrWhiteSpace(dir)) return null;

                if (_byDir == null)
                {
                    _byDir = new Dictionary<string, Entry>(entries.Length, StringComparer.OrdinalIgnoreCase);
                    foreach (var e in entries)
                        if (e != null && !string.IsNullOrEmpty(e.dir))
                            _byDir[e.dir] = e;
                }

                return _byDir.TryGetValue(dir.Trim(), out var hit) ? hit : null;
            }

            public static Manifest Parse(string json) =>
                string.IsNullOrEmpty(json) ? new Manifest() : JsonUtility.FromJson<Manifest>(json);
        }

        // ---------------------------------------------------------------- fallbacks
        //
        // Bundling a fallback chain means guessing, at build time, which languages your users
        // read -- and every language you guessed wrong renders as empty boxes. Bundling ALL of
        // them is not an option either: the three CJK fonts alone are 36 MB. So we fetch the
        // fonts the text in front of us actually needs, once, and cache them on disk.

        /// <summary>Stamped onto every fetched face, so callers can tell it from a bundled one.</summary>
        public const string FetchedSuffix = " (fetched)";

        /// <summary>
        /// Makes a font built from a downloaded file usable, which turns entirely on one question:
        /// will the ADVANCED text generator draw it?
        ///
        /// <para>It matters because shaping lives only there. Arabic joins its letters, Devanagari
        /// and Khmer reorder their clusters, Hebrew and Arabic reverse their runs, and the standard
        /// generator does none of it — it lays one glyph per codepoint in memory order, which for
        /// those scripts is not a lesser rendering but an unreadable one. So whether a downloaded
        /// font can be shaped decides whether a downloaded font can serve those languages AT ALL.</para>
        ///
        /// <para>It looks impossible, and for a long time we believed it was. A downloaded font can
        /// only be built from a FILE PATH — nothing in Unity's API makes a <c>Font</c> from bytes —
        /// and <c>CreateFontAsset(path, ...)</c> leaves <c>sourceFontFile</c> null, which the
        /// advanced generator refuses out of hand: "FontAsset is invalid. Please assign a Source
        /// Font File", and then it draws nothing at all. Not unshaped text. Nothing.</para>
        ///
        /// <para>What was measured, before the answer turned up:</para>
        /// <code>
        ///   mode                          standard gen   advanced gen
        ///   Dynamic   + path              rasterizes     refuses the asset outright
        ///   DynamicOS + path              blank (*)      accepts it, then draws nothing
        ///   Dynamic   + path + empty      rasterizes     accepts it, then fails to init the face
        ///             Font as a shim
        ///   Static    + path, empty       rasterizes     LOADS THE FACE FROM THE PATH
        ///             character table       (once back
        ///                                    on Dynamic)
        ///
        ///   (*) DynamicOS resolves by OS family name, and a browser has no OS fonts.
        /// </code>
        ///
        /// <para>The last row is the one that counts, and <see cref="DsFonts.TryEnableShaping"/>
        /// explains exactly why it works: native is handed the file path all along, and only a
        /// managed guard keyed on <c>Dynamic</c> stops it ever trying. So a downloaded Cairo really
        /// can draw its own Arabic, joined — and the old rule, "a script that needs shaping must be
        /// bundled", is simply not true.</para>
        ///
        /// <para>The bundled chain stays anyway, and it is not dead weight: it is what serves every
        /// script the chosen font does not cover, and it is the safety net for the day this trick
        /// stops working. <see cref="DsFonts.CanShape"/> reports what actually happened rather than
        /// what we hoped, and <see cref="DsFonts.Resolve"/> will not hand a shaping script to a font
        /// that failed.</para>
        ///
        /// <para><b>Order is load-bearing here.</b> The fallbacks go on FIRST, because the native
        /// face snapshots the chain when it is created and adding to it later is a silent no-op. And
        /// the native face is created BEFORE anything rasterizes a glyph, because an empty character
        /// table is the price of admission.</para>
        /// </summary>
        private static void MakeRuntimeUsable(FontAsset fa, IReadOnlyList<FontAsset> fallbacks = null)
        {
            // Dynamic, so the managed loader falls through to m_SourceFontFilePath. Not DynamicOS:
            // that one asks the operating system for a font by name, and there is no operating
            // system here.
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fa.isMultiAtlasTexturesEnabled = true;

            // It came off the wire, it belongs to the session, and nothing should ever write it
            // into the project -- not as a sub-asset, not by a stray scene save.
            fa.hideFlags = HideFlags.DontSave;

            var chain = fallbacks ?? DsFonts.GlobalFallbacks;

            if (chain != null)
            {
                fa.fallbackFontAssetTable ??= new List<FontAsset>();

                foreach (var fb in chain)
                    if (fb && fb != fa && !fa.fallbackFontAssetTable.Contains(fb))
                        fa.fallbackFontAssetTable.Add(fb);
            }

            // Last, and only here. Both preconditions hold at exactly this point: the chain above is
            // wired, and nothing has rasterized a glyph yet.
            DsFonts.TryEnableShaping(fa);
        }

        private static readonly Dictionary<string, FontAsset> Loaded = new();
        private static readonly Dictionary<string, string> Failed = new();
        private static readonly HashSet<string> InFlight = new();

        /// <summary>Every fallback fetched so far this session, in the order they arrived.</summary>
        public static IEnumerable<FontAsset> Fallbacks => Loaded.Values;

        /// <summary>Already have this one? Then rendering it costs nothing.</summary>
        public static bool Has(string dir) => dir != null && Loaded.ContainsKey(dir);

        /// <summary>The fetched face for a script, or null if it has not been fetched.</summary>
        public static FontAsset Get(string dir) =>
            dir != null && Loaded.TryGetValue(dir, out var fa) ? fa : null;

        /// <summary>Why an earlier <see cref="EnsureFont"/> for this font failed, if it did.</summary>
        public static bool FailedFor(string dir, out string error) => Failed.TryGetValue(dir ?? "", out error);

        /// <summary>
        /// Fetches one Noto fallback face and hands it back, or returns the cached one instantly.
        ///
        /// One weight (Regular), because a fallback is there to make text legible, not to carry
        /// the design -- and Noto Sans SC at one weight is already 17 MB.
        /// </summary>
        /// <param name="onProgress">0..1 while downloading. A CJK font is worth a progress bar.</param>
        public static IEnumerator EnsureFont(
            Manifest manifest,
            DsScripts.Script script,
            Action<FontAsset> onDone,
            Action<string> onError,
            Action<float> onProgress = null)
        {
            string dir = script.Dir;

            if (string.IsNullOrEmpty(dir))
            {
                onError?.Invoke("No font for that script.");
                yield break;
            }

            if (Loaded.TryGetValue(dir, out var cached))
            {
                onDone?.Invoke(cached);
                yield break;
            }

            // Two labels can want the same font on the same frame. Let the first one fetch it and
            // the rest wait, or we download 17 MB twice and build two atlases for it.
            if (InFlight.Contains(dir))
            {
                while (InFlight.Contains(dir)) yield return null;

                if (Loaded.TryGetValue(dir, out var arrived)) onDone?.Invoke(arrived);
                else onError?.Invoke(Failed.TryGetValue(dir, out var why) ? why : "Fetch failed.");

                yield break;
            }

            var entry = manifest?.FindByDir(dir);
            if (entry?.files == null || entry.files.Length == 0)
            {
                Fail(dir, $"'{dir}' is not in the manifest.", onError);
                yield break;
            }

            // Upright. An italic fallback for a script the design never styles is dead weight.
            string file = null;
            foreach (string f in entry.files)
                if (f.IndexOf("italic", StringComparison.OrdinalIgnoreCase) < 0) { file = f; break; }
            file ??= entry.files[0];

            InFlight.Add(dir);

            string cacheDir = Path.Combine(Application.persistentDataPath, "DsFonts");
            string path = Path.Combine(cacheDir, SafeName(file));

            // A returning visitor should not pay for 17 MB twice. On WebGL persistentDataPath is
            // IDBFS, which survives the tab closing, so this cache is real there too.
            bool onDisk = false;
            try { onDisk = File.Exists(path) && new FileInfo(path).Length > 0; }
            catch { /* a probe that throws is just a cache miss */ }

            byte[] bytes = null;

            if (!onDisk)
            {
                using var req = UnityWebRequest.Get(entry.Url(file));
                var op = req.SendWebRequest();

                while (!op.isDone)
                {
                    onProgress?.Invoke(req.downloadProgress);
                    yield return null;
                }

                if (req.result != UnityWebRequest.Result.Success)
                {
                    InFlight.Remove(dir);
                    Fail(dir, $"{script.Family}: {req.error}", onError);
                    yield break;
                }

                bytes = req.downloadHandler.data;
                onProgress?.Invoke(1f);
            }

            // Read the face to find its Regular instance. Noto Sans JP's DEFAULT instance is
            // Thin, so trusting faceIndex 0 here would render every kanji hairline-thin.
            int faceIndex = 0;
            if (bytes != null)
            {
                var otf = OpenTypeFace.Read(bytes);
                if (otf == null)
                {
                    InFlight.Remove(dir);
                    Fail(dir, $"{script.Family}: not a font we can read.", onError);
                    yield break;
                }

                var inst = Pick(otf, 400);
                if (inst != null) faceIndex = inst.Value.FaceIndex;

                try
                {
                    Directory.CreateDirectory(cacheDir);
                    File.WriteAllBytes(path, bytes);
                }
                catch (Exception e)
                {
                    InFlight.Remove(dir);
                    Fail(dir, $"{script.Family}: could not write to persistentDataPath ({e.Message}).", onError);
                    yield break;
                }
            }
            else
            {
                // Cached on disk: we still need the face index, and the bytes are cheap to reread.
                try
                {
                    var otf = OpenTypeFace.Read(File.ReadAllBytes(path));
                    var inst = otf == null ? null : Pick(otf, 400);
                    if (inst != null) faceIndex = inst.Value.FaceIndex;
                }
                catch { /* fall through on faceIndex 0 rather than refuse to render at all */ }
            }

            yield return null;   // let the frame that showed the progress bar actually present

            FontAsset fa;
            try
            {
                // The one API that can do this at runtime takes a PATH -- FontAsset has no byte[]
                // factory. On WebGL that path lives in emscripten's virtual filesystem, which the
                // native font engine shares, which is the whole reason this works in a browser.
                fa = CreateQuietly(path, faceIndex);
            }
            catch (Exception e)
            {
                InFlight.Remove(dir);
                Fail(dir, $"{script.Family}: {e.Message}", onError);
                yield break;
            }

            if (fa == null)
            {
                InFlight.Remove(dir);
                Fail(dir, $"{script.Family}: the font engine would not open it.", onError);
                yield break;
            }

            fa.name = script.Family + FetchedSuffix;

            // DynamicOS, and the fallback chain wired NOW. Both are load-bearing; see the method.
            MakeRuntimeUsable(fa);

            Loaded[dir] = fa;
            Failed.Remove(dir);
            InFlight.Remove(dir);

            onDone?.Invoke(fa);
        }

        /// <summary>
        /// Fetches every font <paramref name="text"/> needs and <paramref name="face"/> cannot
        /// already draw, and hands each one back as it lands.
        ///
        /// This is the "any language, as long as the font supports it" path: it looks at the real
        /// codepoints in front of it, not at a list of languages somebody remembered to bundle.
        ///
        /// <para><b>You must apply what it gives you</b>, with <see cref="DsFonts.ApplyFace"/>, to
        /// the subtree that is in that language. There is no way to slip a fetched font into an
        /// already-rendered font's fallback chain: the advanced text generator snapshots that
        /// chain into a native object on first draw and nothing but <c>OnDestroy</c> frees it. So
        /// a late fallback is never seen, and pretending otherwise would be a silent no-op. That
        /// is not really a constraint, though — an app that knows it just switched to Tamil knows
        /// exactly which subtree to point at the Tamil font, and the bundled chain rides along
        /// behind it (see <see cref="MakeRuntimeUsable"/>) so mixed Latin still draws.</para>
        /// </summary>
        public static IEnumerator EnsureScripts(
            Manifest manifest,
            FontAsset face,
            string text,
            Action<DsScripts.Script, FontAsset> onReady = null,
            Action<DsScripts.Script, string> onError = null,
            Action<DsScripts.Script, float> onProgress = null)
        {
            var needed = DsScripts.Missing(face, text);

            foreach (var script in needed)
            {
                var s = script;   // the lambdas below outlive the loop iteration

                yield return EnsureFont(
                    manifest, s,
                    fa => onReady?.Invoke(s, fa),
                    err => onError?.Invoke(s, err),
                    p => onProgress?.Invoke(s, p));
            }
        }

        /// <summary>
        /// <see cref="FontAsset.CreateFontAsset(string,int,int,int,GlyphRenderMode,int,int)"/>, minus
        /// a warning that is Unity's fault and cannot be true.
        ///
        /// Unity builds the asset, validates it, and only THEN tells it where its file is:
        /// <code>
        ///   var fontAsset = CreateFontAssetInstance(null, ...);   // validates here...
        ///   if (fontAsset)
        ///       fontAsset.m_SourceFontFilePath = fontFilePath;    // ...and learns the path here
        /// </code>
        /// So it always logs <i>"Unable to load font face for [] font asset."</i> — note the empty
        /// name, because the asset has not been named yet either — about a font that is, moments
        /// later, perfectly loadable. There is no way to prevent it from out here; the only thing
        /// we control is whether it reaches the console. Errors and exceptions still get through,
        /// and a genuine failure is caught below by the null return.
        /// </summary>
        private static FontAsset CreateQuietly(string path, int faceIndex)
        {
            var logger = Debug.unityLogger;
            var previous = logger.filterLogType;

            logger.filterLogType = LogType.Error;

            try
            {
                return FontAsset.CreateFontAsset(
                    path, faceIndex, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            }
            finally
            {
                logger.filterLogType = previous;
            }
        }

        private static void Fail(string dir, string message, Action<string> onError)
        {
            Failed[dir] = message;
            onError?.Invoke(message);
        }

        // ------------------------------------------------------------------- loading

        /// <summary>
        /// Fetches <paramref name="entry"/>, extracts the requested weights, and builds a family.
        ///
        /// Run it on any MonoBehaviour. The family it produces is a plain in-memory
        /// ScriptableObject — it is never written to disk, so it costs nothing on a rebuild and
        /// disappears with the session.
        /// </summary>
        /// <param name="weights">CSS buckets to extract, e.g. 400 and 700. Nulls are skipped.</param>
        /// <param name="fallbacks">Script fallbacks to wire onto every face. Do not skip these.</param>
        public static IEnumerator Load(
            Entry entry,
            IReadOnlyList<int> weights,
            IReadOnlyList<FontAsset> fallbacks,
            Action<DsFontFamily> onDone,
            Action<string> onError)
        {
            if (entry == null || entry.files == null || entry.files.Length == 0)
            {
                onError?.Invoke("No such family in the manifest.");
                yield break;
            }

            string cacheDir = Path.Combine(Application.persistentDataPath, "DsFonts");

            var upright = new FontAsset[DsFontFamily.WeightCount];
            var italic = new FontAsset[DsFontFamily.WeightCount];
            string firstError = null;
            bool anyFace = false;

            foreach (string file in entry.files)
            {
                // Google publishes upright and italic as separate files. Both are variable, and
                // each carries its own full set of named instances.
                string url = entry.Url(file);
                byte[] bytes = null;

                using (var req = UnityWebRequest.Get(url))
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        firstError ??= $"{file}: {req.error}";
                        continue;
                    }

                    bytes = req.downloadHandler.data;
                }

                var face = OpenTypeFace.Read(bytes);
                if (face == null)
                {
                    firstError ??= $"{file}: not a font we can read.";
                    continue;
                }

                // FreeType opens a file, not a byte[] — FontAsset has no byte[] factory — so the
                // download has to land on disk first. On WebGL persistentDataPath is emscripten's
                // virtual filesystem, which the native font engine shares, so this still works.
                string path;
                bool wrote = false;
                try
                {
                    Directory.CreateDirectory(cacheDir);
                    path = Path.Combine(cacheDir, SafeName(file));
                    File.WriteAllBytes(path, bytes);
                    wrote = true;
                }
                catch (Exception e)
                {
                    path = null;
                    firstError ??= $"Could not cache '{file}': {e.Message}";
                }

                if (!wrote) continue;

                foreach (int weight in weights)
                {
                    var inst = Pick(face, weight);
                    if (inst == null) continue;

                    FontAsset fa;
                    try
                    {
                        fa = CreateQuietly(path, inst.Value.FaceIndex);
                    }
                    catch (Exception e)
                    {
                        firstError ??= $"{file} @{weight}: {e.Message}";
                        continue;
                    }

                    if (fa == null) continue;

                    fa.name = $"{entry.family}-{weight}{(inst.Value.Italic ? "i" : "")}";
                    MakeRuntimeUsable(fa);

                    int slot = DsFontFamily.BucketOf(weight);
                    if (inst.Value.Italic) italic[slot] = fa;
                    else upright[slot] = fa;

                    anyFace = true;

                    // Rasterizing SDF glyphs is not free, and doing a whole family in one frame
                    // stalls the tab. Let the browser breathe between faces.
                    yield return null;
                }
            }

            if (!anyFace)
            {
                onError?.Invoke(firstError ?? "No usable faces in that family.");
                yield break;
            }

            var family = ScriptableObject.CreateInstance<DsFontFamily>();
            family.name = entry.family;
            family.familyName = entry.family;
            family.weights = upright;
            family.italics = italic;
            family.fallbacks = new List<FontAsset>();

            if (fallbacks != null)
                foreach (var fb in fallbacks)
                    if (fb) family.fallbacks.Add(fb);

            WireWeightTables(family);
            DsFonts.WireFallbacks(family);

            onDone?.Invoke(family);
        }

        /// <summary>
        /// The runtime twin of the importer's weight-table wiring, and the reason a downloaded
        /// family gets real bold instead of a synthetically fattened Regular.
        ///
        /// <c>fontWeightTable</c>'s setter is internal, but the property hands back the live
        /// array and <c>FontWeightPair</c> is a struct, so indexing it yields an lvalue and the
        /// writes land in the FontAsset itself.
        ///
        /// Exactly ONE face carries the table and it never names itself — the same rule, and for
        /// the same reason, as the importer's <c>WireWeightTables</c>: the advanced text generator
        /// walks the weight table looking for cycles, counts any revisited face as one, and throws
        /// the table out. Wire it onto every face and every face contains itself, so every face is
        /// circular and real bold silently disappears.
        /// </summary>
        private static void WireWeightTables(DsFontFamily family)
        {
            var owner = family.Regular;
            if (!owner) return;

            var table = owner.fontWeightTable;
            if (table == null) return;

            for (int slot = 1; slot < table.Length; slot++)
            {
                int bucket = slot - 1;   // slot 1 == weight 100

                var upright = At(family.weights, bucket);
                var italic = At(family.italics, bucket);

                table[slot].regularTypeface = upright == owner ? null : upright;
                table[slot].italicTypeface = italic == owner ? null : italic;
            }

            owner.ReadFontAssetDefinition();

            static FontAsset At(FontAsset[] arr, int i) =>
                arr != null && i >= 0 && i < arr.Length ? arr[i] : null;
        }

        private static OpenTypeFace.NamedInstance? Pick(OpenTypeFace face, int weight)
        {
            OpenTypeFace.NamedInstance best = default;
            int bestDistance = int.MaxValue;

            foreach (var inst in face.Instances)
            {
                int d = Math.Abs(OpenTypeFace.SnapToBucket(inst.Weight) - weight);
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = inst;
            }

            return bestDistance <= 100 ? best : (OpenTypeFace.NamedInstance?)null;
        }

        private static IEnumerable<FontAsset> Faces(DsFontFamily family)
        {
            foreach (var f in family.weights ?? Array.Empty<FontAsset>())
                if (f) yield return f;

            foreach (var f in family.italics ?? Array.Empty<FontAsset>())
                if (f) yield return f;
        }

        // Google's filenames carry the variable axes in brackets ("Inter[opsz,wght].ttf").
        // Windows tolerates that; emscripten's filesystem is less forgiving, and the brackets
        // buy us nothing on disk.
        private static string SafeName(string file)
        {
            var chars = file.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '.' && chars[i] != '-' && chars[i] != '_')
                    chars[i] = '_';

            return new string(chars);
        }
    }
}

#endif
