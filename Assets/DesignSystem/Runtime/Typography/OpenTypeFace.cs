using System;
using System.Collections.Generic;
using System.Text;

namespace DesignSystem.Runtime.Typography
{
    /// <summary>
    /// A minimal, allocation-light OpenType reader: just enough of the <c>name</c>,
    /// <c>OS/2</c>, <c>head</c> and <c>fvar</c> tables to answer one question — which
    /// typefaces are inside this file, and at what weight?
    ///
    /// Why parse the font at all instead of reading the filename? Because filenames lie,
    /// and because of variable fonts. Most of the popular Google Fonts families (Inter,
    /// Roboto, all of Noto) ship upstream as a SINGLE variable <c>.ttf</c>. FreeType opens
    /// such a file at its default instance, so a naive import yields Regular and nothing
    /// else — which is why Unity's own Font Asset Creator can only ever give you one
    /// weight out of a variable font, and why a design system built on it is stuck with
    /// synthetic bold forever.
    ///
    /// The way out is in the <c>fvar</c> table. It lists the family's NAMED INSTANCES
    /// (Thin, Light, SemiBold, Bold, Black…) and FreeType addresses them through the
    /// upper 16 bits of its face index. So <see cref="NamedInstance.FaceIndex"/> below is
    /// the whole trick: pass it to <c>FontAsset.CreateFontAsset(path, faceIndex, …)</c>
    /// and one file yields nine real typefaces with genuinely different outlines.
    ///
    /// Pure C# over a byte[], no Unity API — deliberately, so the same code runs in the
    /// editor importer AND in a player that just downloaded a font over the network.
    /// </summary>
    public sealed class OpenTypeFace
    {
        /// <summary>One selectable typeface inside a font file.</summary>
        public readonly struct NamedInstance
        {
            /// <summary>Human name from the font's own name table ("SemiBold", "Bold Italic").</summary>
            public readonly string Name;

            /// <summary>CSS-style weight, 1..1000. From the <c>wght</c> axis, or OS/2 usWeightClass.</summary>
            public readonly int Weight;

            public readonly bool Italic;

            /// <summary>
            /// The value to hand to <c>FontAsset.CreateFontAsset(path, faceIndex, …)</c>.
            /// FreeType packs the named-instance number (1-based) into the high 16 bits and
            /// the face-within-collection into the low 16. A plain static font is just 0.
            /// </summary>
            public readonly int FaceIndex;

            public NamedInstance(string name, int weight, bool italic, int faceIndex)
            {
                Name = name;
                Weight = weight;
                Italic = italic;
                FaceIndex = faceIndex;
            }

            public override string ToString() => $"{Name} ({Weight}{(Italic ? " italic" : "")})";
        }

        /// <summary>Family name (name ID 16 if present, else 1).</summary>
        public string FamilyName { get; private set; } = "Unknown";

        /// <summary>Subfamily / style name (name ID 17 if present, else 2).</summary>
        public string StyleName { get; private set; } = "Regular";

        /// <summary>OS/2 usWeightClass of the file itself, 1..1000.</summary>
        public int WeightClass { get; private set; } = 400;

        /// <summary>True when OS/2 fsSelection or head macStyle marks the file italic.</summary>
        public bool IsItalic { get; private set; }

        /// <summary>True when the file carries an <c>fvar</c> table.</summary>
        public bool IsVariable { get; private set; }

        /// <summary>Variation axis tags, e.g. <c>wght</c>, <c>opsz</c>, <c>wdth</c>. Empty when static.</summary>
        public IReadOnlyList<string> Axes => _axes;

        /// <summary>
        /// Every typeface this file can produce. A static font yields exactly one (face
        /// index 0). A variable font yields one per <c>fvar</c> named instance.
        /// </summary>
        public IReadOnlyList<NamedInstance> Instances => _instances;

        private readonly List<string> _axes = new();
        private readonly List<NamedInstance> _instances = new();

        // ---------------------------------------------------------------- parse

        /// <summary>Reads a .ttf/.otf. Returns null if the bytes are not a font we understand.</summary>
        public static OpenTypeFace Read(byte[] data)
        {
            if (data == null || data.Length < 12) return null;

            try
            {
                var face = new OpenTypeFace();
                return face.Parse(data) ? face : null;
            }
            catch (Exception)
            {
                // A corrupt or exotic font must never take the importer down with it.
                return null;
            }
        }

        private bool Parse(byte[] d)
        {
            uint tag = U32(d, 0);

            // 'ttcf' collections: read the first face's table directory. Google Fonts does not
            // ship collections, but a user-supplied font might, and silently mis-parsing one
            // would be worse than ignoring it.
            int sfntStart = 0;
            if (tag == 0x74746366u) // 'ttcf'
            {
                if (d.Length < 16) return false;
                sfntStart = (int)U32(d, 12);
                if (sfntStart < 0 || sfntStart + 12 > d.Length) return false;
                tag = U32(d, sfntStart);
            }

            // 0x00010000 = TrueType outlines, 'OTTO' = CFF outlines. Anything else (WOFF,
            // WOFF2) is not something FreeType will open here, so refuse it up front rather
            // than let it fail deeper in with a worse message.
            if (tag != 0x00010000u && tag != 0x4F54544Fu) return false;

            int numTables = U16(d, sfntStart + 4);
            var tables = new Dictionary<string, (int off, int len)>(numTables);

            for (int i = 0; i < numTables; i++)
            {
                int rec = sfntStart + 12 + i * 16;
                if (rec + 16 > d.Length) return false;

                string name = Encoding.ASCII.GetString(d, rec, 4);
                int off = (int)U32(d, rec + 8);
                int len = (int)U32(d, rec + 12);
                if (off >= 0 && len >= 0 && off + len <= d.Length)
                    tables[name] = (off, len);
            }

            ReadNames(d, tables);
            ReadMetrics(d, tables);
            ReadFvar(d, tables);

            // A static font still has exactly one selectable typeface: itself, at face 0.
            if (_instances.Count == 0)
                _instances.Add(new NamedInstance(StyleName, WeightClass, IsItalic, 0));

            return true;
        }

        // The name table is the only place a font tells you what it is called. IDs 16/17
        // ("typographic" family/subfamily) exist precisely because 1/2 were forced to lie for
        // the benefit of old Windows menus, which could only cope with four styles per family
        // -- so a 9-weight family had to masquerade as several 4-style ones. Prefer 16/17.
        private Dictionary<int, string> _names;

        private void ReadNames(byte[] d, Dictionary<string, (int off, int len)> tables)
        {
            _names = new Dictionary<int, string>();
            if (!tables.TryGetValue("name", out var t)) return;

            int count = U16(d, t.off + 2);
            int storage = t.off + U16(d, t.off + 4);

            for (int i = 0; i < count; i++)
            {
                int rec = t.off + 6 + i * 12;
                if (rec + 12 > d.Length) break;

                int platform = U16(d, rec);
                int nameId = U16(d, rec + 6);
                int len = U16(d, rec + 8);
                int off = storage + U16(d, rec + 10);
                if (off + len > d.Length || len == 0) continue;

                // Platform 3 (Windows) is UTF-16BE; platform 1 (Mac) is single-byte. Take the
                // Windows record when both exist -- it is the one that is actually maintained.
                string s = platform == 3
                    ? Encoding.BigEndianUnicode.GetString(d, off, len)
                    : Encoding.ASCII.GetString(d, off, len);

                if (string.IsNullOrWhiteSpace(s)) continue;
                if (platform == 3 || !_names.ContainsKey(nameId))
                    _names[nameId] = s;
            }

            FamilyName = Name(16) ?? Name(1) ?? "Unknown";
            StyleName = Name(17) ?? Name(2) ?? "Regular";
        }

        private string Name(int id) => _names.TryGetValue(id, out var s) ? s : null;

        private void ReadMetrics(byte[] d, Dictionary<string, (int off, int len)> tables)
        {
            if (tables.TryGetValue("OS/2", out var os2) && os2.len >= 64)
            {
                WeightClass = U16(d, os2.off + 4);          // usWeightClass
                int fsSelection = U16(d, os2.off + 62);
                IsItalic = (fsSelection & 0x01) != 0;       // bit 0 = ITALIC
            }

            // head.macStyle is the fallback italic signal, and the tie-breaker when a font
            // ships a sane macStyle but a zeroed fsSelection.
            if (!IsItalic && tables.TryGetValue("head", out var head) && head.len >= 46)
                IsItalic = (U16(d, head.off + 44) & 0x02) != 0;  // bit 1 = italic

            if (WeightClass <= 0 || WeightClass > 1000) WeightClass = 400;
        }

        private void ReadFvar(byte[] d, Dictionary<string, (int off, int len)> tables)
        {
            if (!tables.TryGetValue("fvar", out var t) || t.len < 16) return;
            IsVariable = true;

            int axesOffset = t.off + U16(d, t.off + 4);
            int axisCount = U16(d, t.off + 8);
            int axisSize = U16(d, t.off + 10);
            int instCount = U16(d, t.off + 12);
            int instSize = U16(d, t.off + 14);

            int wghtAxis = -1, italAxis = -1;
            for (int i = 0; i < axisCount; i++)
            {
                int rec = axesOffset + i * axisSize;
                if (rec + 4 > d.Length) return;

                string axis = Encoding.ASCII.GetString(d, rec, 4);
                _axes.Add(axis);
                if (axis == "wght") wghtAxis = i;
                else if (axis == "ital") italAxis = i;
            }

            int instBase = axesOffset + axisCount * axisSize;
            for (int i = 0; i < instCount; i++)
            {
                int rec = instBase + i * instSize;
                if (rec + 4 + axisCount * 4 > d.Length) return;

                int subfamilyNameId = U16(d, rec);
                // rec + 2 is flags, reserved.

                int weight = WeightClass;
                bool italic = IsItalic;

                for (int a = 0; a < axisCount; a++)
                {
                    float coord = Fixed(d, rec + 4 + a * 4);
                    if (a == wghtAxis) weight = Mathf_RoundToInt(coord);
                    else if (a == italAxis) italic = coord >= 0.5f;
                }

                string name = Name(subfamilyNameId) ?? $"Instance {i + 1}";

                // The payoff: FreeType's named-instance number is 1-based and lives in the
                // HIGH 16 bits of face_index. This is what turns one variable file into nine
                // real typefaces.
                int faceIndex = (i + 1) << 16;

                _instances.Add(new NamedInstance(name, Clamp(weight), italic, faceIndex));
            }
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Snaps an arbitrary weight onto the nine CSS weight buckets (100..900), which is
        /// the resolution <c>FontAsset.fontWeightTable</c> actually has. A variable font may
        /// name an instance at 350 or 450; both are honest, and both have to land somewhere.
        /// </summary>
        public static int SnapToBucket(int weight)
        {
            int snapped = (int)Math.Round(Clamp(weight) / 100.0) * 100;
            return Math.Max(100, Math.Min(900, snapped));
        }

        private static int Clamp(int w) => w < 1 ? 1 : (w > 1000 ? 1000 : w);

        private static int Mathf_RoundToInt(float f) => (int)Math.Round(f);

        private static ushort U16(byte[] d, int i) => (ushort)((d[i] << 8) | d[i + 1]);

        private static uint U32(byte[] d, int i) =>
            (uint)((d[i] << 24) | (d[i + 1] << 16) | (d[i + 2] << 8) | d[i + 3]);

        /// <summary>16.16 fixed point, the coordinate format fvar uses.</summary>
        private static float Fixed(byte[] d, int i) => (int)U32(d, i) / 65536f;

        public override string ToString() =>
            $"{FamilyName} ({StyleName}) weight={WeightClass}{(IsItalic ? " italic" : "")} " +
            $"{(IsVariable ? $"variable[{string.Join(",", _axes)}] {_instances.Count} instances" : "static")}";
    }
}
