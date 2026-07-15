using System.Collections.Generic;
using System.Globalization;
using UnityEngine.TextCore.Text;

namespace DesignSystem.Runtime.Typography
{
    /// <summary>
    /// Which font draws which script.
    ///
    /// A bundled fallback chain is a chain you GUESSED at: you picked some scripts at build time
    /// and every language you did not think of renders as empty boxes. This maps the codepoints
    /// actually present in actually-displayed text to the Noto font that actually covers them, so
    /// an app can fetch exactly what it needs and nothing it does not. That is the difference
    /// between shipping every script on earth and shipping none of them: CJK alone is 36 MB, and
    /// Tamil is 200 KB that nobody bundles because nobody thought of it.
    ///
    /// Pair with <see cref="DsGoogleFonts.EnsureScripts"/>, which downloads what this reports.
    /// </summary>
    public static class DsScripts
    {
        /// <summary>A Noto font, addressed the way the google/fonts repository addresses it.</summary>
        public readonly struct Script : System.IEquatable<Script>
        {
            public readonly string Name;    // "Japanese"
            public readonly string Dir;     // "ofl/notosansjp"    -- the repo folder, and our key
            public readonly string Family;  // "Noto Sans JP"

            /// <summary>
            /// Does this script need SHAPING to be readable — letters joined, clusters reordered,
            /// marks positioned?
            ///
            /// It decides which FONT may draw the script, not which font may exist. Shaping happens
            /// only in Unity's advanced text generator, so a font that generator will not accept is
            /// no use here even when it has every glyph: it lays them out one codepoint at a time in
            /// memory order. For Latin or CJK that is exactly right. For Arabic it leaves the letters
            /// unjoined, and for Tamil it puts the vowel signs on the wrong side of the consonants.
            /// <see cref="DsFonts.Resolve"/> passes such a font over for these scripts and takes the
            /// next one in the chain that can.
            ///
            /// This used to mean "must be bundled", because a font downloaded at runtime has no
            /// <c>Font</c> object and the advanced generator refuses one. It does not mean that any
            /// more: <see cref="DsFonts.TryEnableShaping"/> gets a native face out of the downloaded
            /// FILE, so a fetched Cairo draws its own Arabic, joined. Bundling is now a choice about
            /// what should work offline, on the first frame, and with no network — not a hard limit.
            /// </summary>
            public readonly bool Shaping;

            public Script(string name, string dir, string family, bool shaping = false)
            {
                Name = name;
                Dir = dir;
                Family = family;
                Shaping = shaping;
            }

            public bool Valid => !string.IsNullOrEmpty(Dir);

            // The repo folder IS the identity: two Scripts naming the same folder are the same
            // font, and List.Contains would otherwise fall back to reflecting over the fields.
            public bool Equals(Script other) => Dir == other.Dir;
            public override bool Equals(object obj) => obj is Script s && Equals(s);
            public override int GetHashCode() => Dir?.GetHashCode() ?? 0;
        }

        // ------------------------------------------------------------------ Han
        //
        // Han characters carry no language. The SAME codepoint is drawn differently in Simplified
        // Chinese, Traditional Chinese, Japanese and Korean, and nothing in the text says which
        // one you meant -- so no amount of fallback-chain cleverness can get this right. An app
        // knows its own locale, so it tells us. See DsFonts.ApplyFace for the other half.

        public enum HanRegion { SimplifiedChinese, TraditionalChinese, Japanese, Korean }

        /// <summary>Which region's letterforms bare Han should get. Set it from your locale.</summary>
        public static HanRegion Han = HanRegion.SimplifiedChinese;

        public static readonly Script ChineseSimplified  = new("Chinese (Simplified)",  "ofl/notosanssc", "Noto Sans SC");
        public static readonly Script ChineseTraditional = new("Chinese (Traditional)", "ofl/notosanstc", "Noto Sans TC");
        public static readonly Script Japanese           = new("Japanese",              "ofl/notosansjp", "Noto Sans JP");
        public static readonly Script Korean             = new("Korean",                "ofl/notosanskr", "Noto Sans KR");

        public static Script ForHan(HanRegion region) => region switch
        {
            HanRegion.TraditionalChinese => ChineseTraditional,
            HanRegion.Japanese           => Japanese,
            HanRegion.Korean             => Korean,
            _                            => ChineseSimplified,
        };

        // ------------------------------------------------------------------ ranges
        //
        // Latin, Greek and Cyrillic are deliberately absent: base Noto Sans covers them, it is
        // 2 MB, and it belongs in the bundle as the catch-all. Everything below is a script you
        // should not pay for until someone actually types in it.

        private readonly struct Range
        {
            public readonly uint First, Last;
            public readonly Script Script;
            public Range(uint first, uint last, Script script) { First = first; Last = last; Script = script; }
        }

        // `shaping: true` means the script has to be SHAPED to be readable, so only a font the
        // advanced text generator will accept may draw it -- see Script.Shaping. Everything that
        // joins, reorders or stacks is true; everything that lays one glyph per codepoint, in
        // order, is false and any font with the glyphs can draw it.
        public static readonly Script Arabic     = new("Arabic",     "ofl/notosansarabic",     "Noto Sans Arabic",     shaping: true);
        public static readonly Script Hebrew     = new("Hebrew",     "ofl/notosanshebrew",     "Noto Sans Hebrew",     shaping: true);
        public static readonly Script Devanagari = new("Devanagari", "ofl/notosansdevanagari", "Noto Sans Devanagari", shaping: true);
        public static readonly Script Bengali    = new("Bengali",    "ofl/notosansbengali",    "Noto Sans Bengali",    shaping: true);
        public static readonly Script Gurmukhi   = new("Gurmukhi",   "ofl/notosansgurmukhi",   "Noto Sans Gurmukhi",   shaping: true);
        public static readonly Script Gujarati   = new("Gujarati",   "ofl/notosansgujarati",   "Noto Sans Gujarati",   shaping: true);
        public static readonly Script Oriya      = new("Oriya",      "ofl/notosansoriya",      "Noto Sans Oriya",      shaping: true);
        public static readonly Script Tamil      = new("Tamil",      "ofl/notosanstamil",      "Noto Sans Tamil",      shaping: true);
        public static readonly Script Telugu     = new("Telugu",     "ofl/notosanstelugu",     "Noto Sans Telugu",     shaping: true);
        public static readonly Script Kannada    = new("Kannada",    "ofl/notosanskannada",    "Noto Sans Kannada",    shaping: true);
        public static readonly Script Malayalam  = new("Malayalam",  "ofl/notosansmalayalam",  "Noto Sans Malayalam",  shaping: true);
        public static readonly Script Sinhala    = new("Sinhala",    "ofl/notosanssinhala",    "Noto Sans Sinhala",    shaping: true);
        public static readonly Script Thai       = new("Thai",       "ofl/notosansthai",       "Noto Sans Thai",       shaping: true);
        public static readonly Script Lao        = new("Lao",        "ofl/notosanslao",        "Noto Sans Lao",        shaping: true);
        public static readonly Script Tibetan    = new("Tibetan",    "ofl/notosanstibetan",    "Noto Sans Tibetan",    shaping: true);
        public static readonly Script Myanmar    = new("Myanmar",    "ofl/notosansmyanmar",    "Noto Sans Myanmar",    shaping: true);
        public static readonly Script Khmer      = new("Khmer",      "ofl/notosanskhmer",      "Noto Sans Khmer",      shaping: true);
        public static readonly Script Javanese   = new("Javanese",   "ofl/notosansjavanese",   "Noto Sans Javanese",   shaping: true);
        public static readonly Script Adlam      = new("Adlam",      "ofl/notosansadlam",      "Noto Sans Adlam",      shaping: true);

        // One glyph per codepoint, in order. These are safe to fetch.
        public static readonly Script Armenian   = new("Armenian",   "ofl/notosansarmenian",   "Noto Sans Armenian");
        public static readonly Script Georgian   = new("Georgian",   "ofl/notosansgeorgian",   "Noto Sans Georgian");
        public static readonly Script Ethiopic   = new("Ethiopic",   "ofl/notosansethiopic",   "Noto Sans Ethiopic");
        public static readonly Script Cherokee   = new("Cherokee",   "ofl/notosanscherokee",   "Noto Sans Cherokee");
        public static readonly Script Mongolian  = new("Mongolian",  "ofl/notosansmongolian",  "Noto Sans Mongolian");

        // A sentinel: Han is real, but which FONT draws it is decided per string, not per range.
        private static readonly Script HanAmbiguous = new("Han", null, null);

        private static readonly Range[] Ranges =
        {
            new(0x0530, 0x058F, Armenian),   new(0xFB13, 0xFB17, Armenian),
            new(0x0590, 0x05FF, Hebrew),     new(0xFB1D, 0xFB4F, Hebrew),
            new(0x0600, 0x06FF, Arabic),     new(0x0750, 0x077F, Arabic),
            new(0x08A0, 0x08FF, Arabic),     new(0xFB50, 0xFDFF, Arabic),
            new(0xFE70, 0xFEFF, Arabic),
            new(0x0900, 0x097F, Devanagari), new(0xA8E0, 0xA8FF, Devanagari),
            new(0x0980, 0x09FF, Bengali),
            new(0x0A00, 0x0A7F, Gurmukhi),
            new(0x0A80, 0x0AFF, Gujarati),
            new(0x0B00, 0x0B7F, Oriya),
            new(0x0B80, 0x0BFF, Tamil),
            new(0x0C00, 0x0C7F, Telugu),
            new(0x0C80, 0x0CFF, Kannada),
            new(0x0D00, 0x0D7F, Malayalam),
            new(0x0D80, 0x0DFF, Sinhala),
            new(0x0E00, 0x0E7F, Thai),
            new(0x0E80, 0x0EFF, Lao),
            new(0x0F00, 0x0FFF, Tibetan),
            new(0x1000, 0x109F, Myanmar),
            new(0x10A0, 0x10FF, Georgian),   new(0x1C90, 0x1CBF, Georgian),
            new(0x2D00, 0x2D2F, Georgian),
            new(0x1200, 0x139F, Ethiopic),
            new(0x13A0, 0x13FF, Cherokee),
            new(0x1780, 0x17FF, Khmer),      new(0x19E0, 0x19FF, Khmer),
            new(0x1800, 0x18AF, Mongolian),
            new(0xA980, 0xA9DF, Javanese),
            new(0x1E900, 0x1E95F, Adlam),

            // Korean. Hangul is unambiguous, so it names its own font.
            new(0x1100, 0x11FF, Korean),     new(0x3130, 0x318F, Korean),
            new(0xA960, 0xA97F, Korean),     new(0xAC00, 0xD7FF, Korean),

            // Japanese. Kana is unambiguous, so it names its own font.
            new(0x3040, 0x309F, Japanese),   new(0x30A0, 0x30FF, Japanese),
            new(0x31F0, 0x31FF, Japanese),

            // Traditional Chinese. Bopomofo is only used to annotate it.
            new(0x3100, 0x312F, ChineseTraditional),

            // Han, and the punctuation all four regions share. Ambiguous by construction.
            new(0x2E80, 0x2EFF, HanAmbiguous), new(0x3000, 0x303F, HanAmbiguous),
            new(0x3400, 0x4DBF, HanAmbiguous), new(0x4E00, 0x9FFF, HanAmbiguous),
            new(0xF900, 0xFAFF, HanAmbiguous), new(0x20000, 0x2A6DF, HanAmbiguous),
        };

        // ------------------------------------------------------------------ queries

        /// <summary>
        /// The font that draws this codepoint, or null for Latin/Greek/Cyrillic (base Noto Sans),
        /// unknown scripts, and bare Han — which has no answer until you know the language.
        /// </summary>
        public static Script? Of(uint codepoint)
        {
            foreach (var r in Ranges)
                if (codepoint >= r.First && codepoint <= r.Last)
                    return r.Script.Valid ? r.Script : (Script?)null;

            return null;
        }

        /// <summary>
        /// The fonts <paramref name="text"/> needs that <paramref name="face"/> and its chain
        /// cannot already draw. Empty means it will render correctly as-is.
        ///
        /// Han is resolved for the string as a whole, not per character: kana anywhere means the
        /// kanji are Japanese, Hangul anywhere means the hanja are Korean, and otherwise it falls
        /// to <see cref="Han"/>. That is as close as anyone can get without being told the locale.
        /// </summary>
        public static List<Script> Missing(FontAsset face, string text)
        {
            var needed = new List<Script>();
            if (string.IsNullOrEmpty(text)) return needed;

            bool sawKana = false, sawHangul = false, needsHan = false;

            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                uint cp = (uint)char.ConvertToUtf32(e.GetTextElement(), 0);

                if (cp is (>= 0x3040 and <= 0x30FF)) sawKana = true;
                if (cp is (>= 0xAC00 and <= 0xD7FF) or (>= 0x1100 and <= 0x11FF)) sawHangul = true;

                // Already drawable? Then it is not missing, whatever script it is.
                if (DsFonts.Resolve(face, cp).Covered) continue;

                var hit = Of(cp);

                if (hit.HasValue)
                {
                    if (!needed.Contains(hit.Value)) needed.Add(hit.Value);
                    continue;
                }

                // Ambiguous Han, or a script we have no font for. Only the former is actionable.
                if (IsHan(cp)) needsHan = true;
            }

            if (needsHan)
            {
                var han = sawKana ? Japanese
                        : sawHangul ? Korean
                        : ForHan(Han);

                if (!needed.Contains(han)) needed.Add(han);
            }

            return needed;
        }

        private static bool IsHan(uint cp) =>
            cp is (>= 0x2E80 and <= 0x2EFF) or (>= 0x3000 and <= 0x303F) or
                  (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or
                  (>= 0xF900 and <= 0xFAFF) or (>= 0x20000 and <= 0x2A6DF);
    }
}
