using System.Collections.Generic;
using System.Linq;
using DesignSystem.Runtime.Typography;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Showcase.Runtime
{
    /// <summary>
    /// Drives the showcase's TYPEFACE, FONT WEIGHTS and LANGUAGE AVAILABILITY sections.
    ///
    /// Same shape as the theme controls in <c>ShowcaseBootstrap</c>: the truth lives in
    /// statics, every mutation funnels through <see cref="SyncAll"/>, and each root (the flat
    /// page plus one panel per world-space exhibit) is re-stamped from that truth. Nothing is
    /// captured across a panel reload, because a PanelRenderer hands out a brand-new root every
    /// time and a captured element would quietly go stale.
    /// </summary>
    public static class ShowcaseFonts
    {
        /// <summary>
        /// The i18n pangram set ("I can eat glass, it does not hurt me"), plus the Latin one.
        ///
        /// These are not decoration. Each line proves a different thing that a naive font setup
        /// gets wrong: Turkish needs the dotted/dotless i, Arabic and Hebrew need the text laid
        /// out right-to-left, Arabic additionally needs its letters JOINED into initial/medial/
        /// final forms, Devanagari needs its clusters reordered, and CJK needs a fallback font
        /// at all because no Latin family on earth contains a single kanji.
        ///
        /// `Shaping` marks the lines that have to be SHAPED to be readable at all: Arabic joins its
        /// letters into initial/medial/final forms, Hebrew and Arabic reverse their runs, Devanagari
        /// and Bengali and Tamil and Thai and Khmer reorder their clusters. Shaping happens only in
        /// Unity's advanced text generator, so a font that the advanced generator will not draw is no
        /// use to these lines even when it has every glyph: it would lay them out one codepoint at a
        /// time, in memory order, which is not a rendering of Arabic that any reader of Arabic would
        /// accept. DsFonts.Resolve skips such a font for these rows and takes the next one that can.
        ///
        /// That used to mean "bundled", because a downloaded font could never be shaped. It no
        /// longer does: DsFonts.TryEnableShaping gets a native font face out of a downloaded file,
        /// so a fetched Cairo draws its own Arabic, joined. The chain below is still bundled, and
        /// still earns its place, because it serves every script the chosen font does not cover.
        ///
        /// `Pin` is set only for CJK. Chinese, Japanese and Korean share Unicode codepoints for
        /// characters they draw differently, so a fallback chain -- which resolves per codepoint,
        /// not per language -- hands every shared Han character to whichever CJK font sits first.
        /// Left to the chain, all three lines render in one region's letterforms and every
        /// coverage check still passes. A real app knows which language it is showing, so it names
        /// the font. See DsFonts.ApplyFace.
        ///
        /// `Mb` marks a download big enough that the visitor should get to decide.
        /// </summary>
        private sealed class Row
        {
            public readonly string Label, Sample;
            public readonly bool Rtl, Shaping;
            public readonly DsScripts.Script? Pin;
            public readonly int Mb;

            public Row(string label, string sample, bool rtl = false, bool shaping = false,
                       DsScripts.Script? pin = null, int mb = 0)
            {
                Label = label; Sample = sample; Rtl = rtl; Shaping = shaping; Pin = pin; Mb = mb;
            }
        }

        private static readonly Row[] Scripts =
        {
            // One glyph per codepoint, in order. Any font that has the glyphs can draw these.
            new("Latin",      "The quick brown fox jumps over the lazy dog"),
            new("Turkish",    "Pijamalı hasta yağız şoföre çabucak güvendi"),
            new("Vietnamese", "Tôi có thể ăn thủy tinh mà không hại gì"),
            new("Greek",      "Ξεσκεπάζω την ψυχοφθόρα βδελυγμία"),
            new("Cyrillic",   "Съешь же ещё этих мягких французских булок"),

            // These have to be SHAPED. Having the glyphs is not enough.
            new("Arabic",     "أنا قادر على أكل الزجاج و هذا لا يؤلمني", rtl: true, shaping: true),
            new("Hebrew",     "אני יכול לאכול זכוכית וזה לא מזיק לי", rtl: true, shaping: true),
            new("Devanagari", "मैं काँच खा सकता हूँ, मुझे उस से कोई पीड़ा नहीं होती", shaping: true),
            new("Bengali",    "আমি কাঁচ খেতে পারি, তাতে আমার কোনো ক্ষতি হয় না", shaping: true),
            new("Tamil",      "நான் கண்ணாடி சாப்பிடுவேன், அதனால் எனக்கு ஒரு கேடும் வராது", shaping: true),
            new("Thai",       "ฉันกินกระจกได้ แต่มันไม่ทำให้ฉันเจ็บ", shaping: true),
            new("Khmer",      "ខ្ញុំអាចញ៉ាំកញ្ចក់បាន ដោយគ្មានបញ្ហា", shaping: true),

            // Nothing in the build covers these, so they arrive over the wire on sight.
            new("Armenian",   "Ես կրնամ ապակի ուտել և ինծի անհանգիստ չըներ"),
            new("Georgian",   "მინას ვჭამ და არა მტკივა"),

            // FETCHED, and big enough that the visitor should get to decide. This is the 36 MB.
            new("Japanese", "私はガラスを食べられます。それは私を傷つけません。", pin: DsScripts.Japanese,          mb: 9),
            new("Korean",   "나는 유리를 먹을 수 있어요. 그래도 아프지 않아요",      pin: DsScripts.Korean,            mb: 10),
            new("Chinese",  "我能吞下玻璃而不伤身体",                              pin: DsScripts.ChineseSimplified, mb: 17),
        };

        /// <summary>`?dsdebug=1`. Narrates the font pipeline to the browser console.</summary>
        public static bool Diagnostics;

        private const string SpecimenText = "Handgloves";

        private static ShowcaseFontSet _set;
        private static DsGoogleFonts.Manifest _manifest;

        private static DsFontFamily _active;
        private static StyleSheet _activeSheet;
        private static string _status = "";
        private static bool _fetching;

        // Roots we have wired. A VisualElement outlives the panel that owned it, so `panel`
        // is the only honest liveness test -- a null check on the element itself is not one.
        private static readonly List<VisualElement> _roots = new();

        // ------------------------------------------------------------------- wiring

        /// <summary>
        /// Wires one root. Safe to call per world exhibit: each panel gets its own clone of the
        /// UXML, so the Q() lookups only ever find that panel's own controls.
        /// </summary>
        public static void Wire(VisualElement root)
        {
            if (root == null) return;

            EnsureLoaded();

            _roots.RemoveAll(r => r == null || r.panel == null);
            if (!_roots.Contains(root)) _roots.Add(root);

            var button = root.Q<Button>("font-fetch-button");
            var input = root.Q<TextField>("font-fetch-input");

            if (button != null)
            {
                // `.clicked` and not a ClickEvent callback: a Button already translates pointer
                // AND keyboard/gamepad submit into `clicked`, so this stays reachable without a
                // mouse.
                button.clicked += () =>
                {
                    var field = button.panel?.visualTree.Q<TextField>("font-fetch-input") ?? input;
                    Fetch(field?.value);
                };
            }

            SyncRoot(root);
        }

        private static void EnsureLoaded()
        {
            if (_set == null)
            {
                _set = Resources.Load<ShowcaseFontSet>("ShowcaseFontSet");

                // The bundled chain, published where the runtime can see it. The baker writes it
                // into a PanelTextSettings asset, which is an EDIT-time artefact; nothing hands it
                // to DsFonts in a player. Without this a fetched font is born with no fallbacks at
                // all, and a Tamil line with one English word in it loses the English word.
                if (_set?.fallbacks != null)
                {
                    DsFonts.GlobalFallbacks.Clear();

                    foreach (var fb in _set.fallbacks)
                        if (fb) DsFonts.GlobalFallbacks.Add(fb);
                }
            }

            if (_manifest == null)
            {
                // Fully qualified: UnityEngine.TextCore.Text also defines a TextAsset (the emoji
                // fallback kind), and `using` both namespaces makes the bare name ambiguous.
                var json = Resources.Load<UnityEngine.TextAsset>("GoogleFontsManifest");
                _manifest = DsGoogleFonts.Manifest.Parse(json ? json.text : null);
            }

            if (_active == null && _set != null && _set.choices.Length > 0)
            {
                var first = _set.choices.FirstOrDefault(c => c.family);
                if (first != null)
                {
                    _active = first.family;
                    _activeSheet = first.sheet;
                }
            }
        }

        // -------------------------------------------------------------------- state

        private static void SetFamily(DsFontFamily family, StyleSheet sheet)
        {
            if (!family) return;

            _active = family;
            _activeSheet = sheet;
            SyncAll();
        }

        private static void SyncAll()
        {
            _roots.RemoveAll(r => r == null || r.panel == null);
            foreach (var root in _roots) SyncRoot(root);
        }

        private static void SyncRoot(VisualElement root)
        {
            if (root?.panel == null || _active == null) return;

            ApplyTypeface(root);
            BuildWeightRamp(root);
            BuildScripts(root);
            UpdateLabels(root);
        }

        // ---------------------------------------------------------------- typeface

        private static void ApplyTypeface(VisualElement root)
        {
            // The stylesheet is the real mechanism, and swapping it is what makes the whole
            // design system change face: it re-points `--ds-font-*`, `.ds-root`, `.ds-h3` and
            // the `.ds-weight-*` utilities in one move.
            //
            // A font fetched at runtime has no compiled stylesheet -- Unity cannot build one in
            // a player -- so that case falls back to setting the inherited property directly.
            var panelScope = root.panel.visualTree;

            foreach (var scope in new[] { root, panelScope })
            {
                if (scope == null) continue;

                foreach (var choice in _set?.choices ?? System.Array.Empty<ShowcaseFontSet.Choice>())
                    if (choice.sheet && choice.sheet != _activeSheet && scope.styleSheets.Contains(choice.sheet))
                        scope.styleSheets.Remove(choice.sheet);

                // The dropdown popup is parented at PANEL scope, as a sibling of the document
                // root, so it inherits nothing from `.ds-root`. The sheet has to land there too
                // or the popup keeps rendering in the previous typeface.
                if (_activeSheet && !scope.styleSheets.Contains(_activeSheet))
                    scope.styleSheets.Add(_activeSheet);
            }

            var fetched = _activeSheet ? null : _active;

            DsFonts.Apply(root, fetched);

            // And the panel scope too, or the dropdown POPUP comes out blank. The popup is
            // parented as a sibling of the document root, so it inherits nothing from `.ds-root`.
            // A bundled family reaches it through the stylesheet added above; a fetched family has
            // no stylesheet to reach it with, so the font -- and, just as importantly, the
            // standard-generator class that a fetched font cannot render without -- has to be set
            // here directly.
            DsFonts.Apply(panelScope, fetched);
        }

        // ------------------------------------------------------------------ weights

        private static void BuildWeightRamp(VisualElement root)
        {
            var host = root.Q<VisualElement>("font-weight-ramp");
            if (host == null) return;

            host.Clear();

            var weights = _active.AvailableWeights();
            bool italics = _active.HasItalics();

            foreach (int weight in weights)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 2;

                var number = new Label(weight.ToString());
                number.AddToClassList("ds-caption");
                number.style.width = 34;
                number.style.flexShrink = 0;
                row.Add(number);

                var sample = new Label(SpecimenText);
                sample.AddToClassList("ds-body-1");
                sample.style.fontSize = 17;
                sample.style.flexGrow = 1;
                DsFonts.ApplyWeight(sample, _active, weight);
                row.Add(sample);

                if (italics)
                {
                    var slanted = new Label(SpecimenText);
                    slanted.AddToClassList("ds-body-1");
                    slanted.style.fontSize = 17;
                    slanted.style.width = 100;
                    slanted.style.flexShrink = 0;
                    // Falls back to the upright face when the family has no italic at this
                    // weight, which is a far smaller lie than showing nothing.
                    DsFonts.ApplyWeight(slanted, _active, weight, italic: true);
                    row.Add(slanted);
                }

                host.Add(row);
            }

            var note = root.Q<Label>("font-weight-note");
            if (note == null) return;

            note.text = weights.Count == DsFontFamily.WeightCount
                ? $"All 9 weights{(italics ? " and 9 italics" : "")}, out of " +
                  $"{(italics ? "two files" : "one file")}."
                : $"{_active.familyName} ships {weights.Count} weight{(weights.Count == 1 ? "" : "s")}. " +
                  "Requests for the others resolve to the nearest one it has.";
        }

        // --------------------------------------------------- language availability

        // Column widths shared by the header and every row, so the two can never drift apart.
        private const int LangW = 92;
        private const int StatusW = 150;

        private static void BuildScripts(VisualElement root)
        {
            var host = root.Q<VisualElement>("font-scripts");
            if (host == null) return;

            host.Clear();

            // The header is rebuilt with the rows on purpose: same widths, one source, no drift.
            host.Add(AvailabilityHeader());

            int covered = 0;
            int own = 0;      // languages the active font draws with its OWN glyphs

            // `?dsdebug=1`. Every wrong answer this feature has ever given looked the same from the
            // outside: text is there, or it is not, and unjoined Arabic looks like a font choice.
            // So say out loud, per line, which font drew it and which generator it got. A shaping
            // row that reads `FLAT!` is the bug -- a script that must join, handed a font that will
            // not -- caught before anyone squints at the letters.
            var trace = Diagnostics ? new List<string>() : null;

            foreach (var r in Scripts)
            {
                // What font will actually draw this line, and do we have it yet?
                //
                // Three outcomes. The active font draws it. A bundled fallback draws it. Or nothing
                // in the stack does, and we need a fetched font -- reachable only by NAMING it on
                // the element, never by adding it to a chain (the advanced generator snapshots a
                // font's fallbacks on first draw and never re-reads them).
                FontAsset drawnBy = null;
                DsScripts.Script? want = null;
                bool fromFallback = false;
                bool fromFamily = false;

                if (r.Pin.HasValue)
                {
                    // Han unification: the codepoints cannot tell us the language, but we know it.
                    var pinned = DsGoogleFonts.Get(r.Pin.Value.Dir);
                    drawnBy = pinned;
                    if (!pinned) want = r.Pin;
                }
                else
                {
                    // requireShaping is the whole difference between "has the glyphs" and "can draw
                    // the word". A font that cannot be shaped is passed over for these lines even if
                    // it covers every character in them, and the next font that CAN gets the row.
                    var hit = DsFonts.Coverage(_active, r.Sample, out int missing, requireShaping: r.Shaping);

                    if (hit.Covered && missing == 0)
                    {
                        drawnBy = hit.Font;
                        fromFallback = hit.FromFallback;
                        fromFamily = !hit.FromFallback;
                    }
                    else
                    {
                        // Nothing in the stack covers it. Name the font that does, if it has landed.
                        var gaps = DsScripts.Missing(_active.Regular, r.Sample);
                        var need = gaps.Count > 0 ? gaps[0] : default(DsScripts.Script?);

                        var pinned = need.HasValue ? DsGoogleFonts.Get(need.Value.Dir) : null;
                        drawnBy = pinned;
                        if (!pinned) want = need;
                    }
                }

                if (drawnBy) covered++;
                if (fromFamily) own++;

                var row = new VisualElement();
                row.AddToClassList("showcase-avail__row");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                var name = new Label(r.Label);
                name.AddToClassList("ds-caption");
                name.AddToClassList("ds-text-secondary");
                name.style.width = LangW;
                name.style.flexShrink = 0;
                row.Add(name);

                var text = new Label(r.Sample);
                text.AddToClassList("ds-body-1");
                text.style.flexGrow = 1;
                text.style.flexShrink = 1;
                text.style.overflow = Overflow.Hidden;
                text.style.whiteSpace = WhiteSpace.NoWrap;
                text.style.marginRight = 12;   // a gap before the COVERAGE column

                // UI Toolkit has no `direction` property. The advanced text generator reorders the
                // RUN correctly on its own (that is bidi); what it cannot know is which edge the
                // paragraph hangs off, and an Arabic line flush-left reads as wrong to anyone who
                // reads Arabic.
                if (r.Rtl) text.style.unityTextAlign = TextAnchor.MiddleRight;

                // Name the drawing font AND, through r.Shaping, the GENERATOR it needs. This is one
                // call doing two jobs, and both matter:
                //
                //  - Naming the font makes the row draw with the face that actually covers it, and
                //    keeps the CJK lines out of Han unification (the chain would hand all three to
                //    whichever CJK font it met first and draw Chinese in Japanese letterforms).
                //
                //  - r.Shaping picks the generator. Shaping scripts (Arabic, Indic, Thai, Khmer)
                //    get the advanced generator, which joins and reorders. Everything else -- Latin,
                //    Greek, Cyrillic, and crucially CJK -- gets the standard one. CJK never needed
                //    shaping, and routing a DOWNLOADED CJK font through the advanced generator draws
                //    a black or stale block on the first frame. The standard generator draws it
                //    cleanly, first time.
                if (drawnBy) DsFonts.ApplyFace(text, drawnBy, r.Shaping);

                trace?.Add(
                    $"{r.Label}={(drawnBy ? Short(drawnBy.name) : "-")}" +
                    $"[{(!drawnBy ? "-" : !r.Shaping ? "standard" : DsFonts.CanShape(drawnBy) ? "advanced" : "FLAT!")}]");

                // Nothing can draw it yet, so do not pretend: show the row's state instead of a
                // line of empty boxes that reads as a font bug.
                if (!drawnBy) text.style.opacity = 0.25f;

                row.Add(text);
                row.Add(Status(r, drawnBy, want, fromFallback));
                host.Add(row);

                // Small scripts just appear -- that is the promise. The 9-to-17 MB ones wait to be
                // asked, because nobody should pay for Chinese to read an English demo.
                if (want.HasValue && r.Mb == 0) Ensure(want.Value);
            }

            // The last row wears no divider -- the container's own rounded border closes it off.
            // USS has no :last-child in UI Toolkit, so it is done here, where the count is known.
            if (host.childCount > 1)
                host.ElementAt(host.childCount - 1).style.borderBottomWidth = 0;

            if (trace != null)
                Debug.Log($"[dsfont] {_active.familyName}" +
                          $"{(_activeSheet ? "" : " (fetched)")} rows: {string.Join("  ", trace)}");

            var note = root.Q<Label>("font-coverage-note");
            if (note == null) return;

            note.text =
                $"{_active.familyName} covers {own} of {Scripts.Length} languages with its own glyphs (green); the " +
                $"chain behind it draws {covered - own} more (grey and amber), and {Scripts.Length - covered} are " +
                "waiting on a download. That is the design, not a gap: no single typeface covers Latin and Arabic " +
                "and Chinese, so the font you pick leads and the chain catches the rest. Fetch a font with broader " +
                "coverage -- Cairo has Arabic, Noto Sans JP has kanji -- and watch its rows turn blue. Amber is a CJK " +
                "line naming its own font on purpose, because the chain alone cannot tell Chinese from Japanese.";
        }

        /// <summary>The table header: LANGUAGE | SAMPLE | COVERAGE, on the same widths as the rows.</summary>
        private static VisualElement AvailabilityHeader()
        {
            var head = new VisualElement();
            head.AddToClassList("showcase-avail__head");
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;

            Label Cell(string t, int w, bool grow, TextAnchor align)
            {
                var l = new Label(t);
                l.AddToClassList("ds-section-label");
                l.style.marginBottom = 0;
                if (grow) l.style.flexGrow = 1;
                else { l.style.width = w; l.style.flexShrink = 0; }
                l.style.unityTextAlign = align;
                return l;
            }

            head.Add(Cell("LANGUAGE", LangW, false, TextAnchor.MiddleLeft));
            head.Add(Cell("SAMPLE", 0, true, TextAnchor.MiddleLeft));
            // Left-aligned like the others, so the check marks below line up in a column instead of
            // ragging off the right edge behind labels of different lengths.
            head.Add(Cell("COVERAGE", StatusW, false, TextAnchor.MiddleLeft));
            return head;
        }

        /// <summary>
        /// The COVERAGE cell: a green check plus the font that drew the line, a download button for
        /// the big ones, or a progress readout. The status Label is named per script so a progress
        /// tick can find and update it without rebuilding the whole table under the user.
        /// </summary>
        private static VisualElement Status(Row r, FontAsset drawnBy, DsScripts.Script? want, bool fromFallback)
        {
            // Big enough to be a choice: offer it rather than spend the user's bandwidth for them.
            if (want.HasValue && r.Mb > 0 && !_progress.ContainsKey(want.Value.Dir))
            {
                var load = new Button { text = $"Load  {r.Mb} MB" };
                load.AddToClassList("ds-btn");
                load.AddToClassList("ds-btn--ghost");
                load.AddToClassList("ds-btn--sm");
                load.style.width = StatusW;
                load.style.flexShrink = 0;
                load.style.marginBottom = 0;

                var s = want.Value;

                // Safe to rebuild from here: a click is not mid-BuildScripts. The rebuild is what
                // swaps this button for a progress readout.
                load.clicked += () => { Ensure(s); SyncAll(); };
                return load;
            }

            var cell = new VisualElement();
            cell.AddToClassList("showcase-avail__status");
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.width = StatusW;
            cell.style.flexShrink = 0;
            cell.style.justifyContent = Justify.FlexStart;
            cell.style.alignItems = Align.Center;

            if (drawnBy)
            {
                // A real check mark, as an SVG icon rather than a glyph, so it renders the same in
                // every active typeface (a fetched Latin font need not carry U+2713).
                var check = new VisualElement();
                check.AddToClassList("ds-icon");
                check.AddToClassList("ds-icon--xs");
                check.AddToClassList("ds-icon--check");
                check.AddToClassList("ds-icon--accent");
                check.style.marginRight = 5;
                cell.Add(check);

                var label = new Label(Short(drawnBy.name));
                label.AddToClassList("ds-caption");
                label.style.whiteSpace = WhiteSpace.NoWrap;

                // DsGoogleFonts stamps everything it fetches, so this is the honest test of
                // "was this in the build, or did it arrive over the wire a second ago?"
                bool fetched = drawnBy.name.EndsWith(DsGoogleFonts.FetchedSuffix, System.StringComparison.Ordinal);

                if (r.Pin.HasValue)     label.AddToClassList("ds-text-warning");    // named on purpose
                else if (fetched)       label.AddToClassList("ds-text-info");       // came over the wire
                else if (fromFallback)  label.AddToClassList("ds-text-secondary");  // a bundled fallback
                else                    label.AddToClassList("ds-text-success");    // the family itself

                cell.Add(label);
                return cell;
            }

            // Not covered yet: a download in flight, a failure, or a script with no font anywhere.
            var status = new Label();
            status.AddToClassList("ds-caption");
            status.style.unityTextAlign = TextAnchor.MiddleRight;
            if (want.HasValue) status.name = StatusName(want.Value);   // Tick updates THIS label

            if (want.HasValue && DsGoogleFonts.FailedFor(want.Value.Dir, out string error))
            {
                status.text = "failed";
                status.tooltip = error;
                status.AddToClassList("ds-text-danger");
            }
            else
            {
                status.text = want.HasValue ? StatusText(want.Value) : "no glyphs";
                status.AddToClassList(want.HasValue ? "ds-text-info" : "ds-text-danger");
            }

            cell.Add(status);
            return cell;
        }

        // ---------------------------------------------------------------- fetching

        // Downloads in flight, by repo folder. Present means "started"; the float is 0..1.
        private static readonly Dictionary<string, float> _progress = new();

        private static string StatusName(DsScripts.Script s) => "script-status-" + s.Dir.Replace('/', '-');

        private static string StatusText(DsScripts.Script s) =>
            _progress.TryGetValue(s.Dir, out float p)
                ? (p >= 1f ? "building…" : $"fetching {Mathf.RoundToInt(p * 100f)}%")
                : "queued…";

        /// <summary>
        /// Starts one fetch. Deliberately does NOT rebuild: BuildScripts calls this while it is
        /// midway through filling the very container a rebuild would clear, and the loop would
        /// then keep appending to a table that had already been rebuilt underneath it — one
        /// duplicate copy of every row per auto-fetched script. The row is already showing
        /// "queued…"; <see cref="Tick"/> keeps it current, and the callbacks rebuild once it lands.
        /// </summary>
        private static void Ensure(DsScripts.Script script)
        {
            if (_progress.ContainsKey(script.Dir) || DsGoogleFonts.Has(script.Dir)) return;

            _progress[script.Dir] = 0f;

            Runner.Get().StartCoroutine(DsGoogleFonts.EnsureFont(
                _manifest, script,
                fa =>
                {
                    _progress.Remove(script.Dir);

                    // `?dsdebug=1`. A font can arrive, be named, be applied, and still draw nothing
                    // -- that is the entire history of this feature. So say out loud whether it can
                    // produce a glyph, and whether it can SHAPE one, which are the only questions.
                    // `shapes=no` means TryEnableShaping did not take: harmless for these scripts,
                    // fatal for Arabic, and the first thing to look at if a fetched font goes flat.
                    if (Diagnostics)
                    {
                        string sample = Scripts.FirstOrDefault(r => r.Sample != null &&
                            DsScripts.Missing(_active.Regular, r.Sample).Contains(script))?.Sample;

                        var hit = DsFonts.Coverage(fa, sample ?? "", out int missing);

                        Debug.Log(
                            $"[dsfont] {script.Family}: mode={fa.atlasPopulationMode} " +
                            $"shapes={(DsFonts.CanShape(fa) ? "yes (advanced)" : "no (standard)")} " +
                            $"draws={(hit.Covered ? hit.Font.name : "NOTHING")} " +
                            $"missing={missing} glyphs={fa.characterTable.Count}");
                    }

                    SyncAll();   // it renders now, so rebuild the row and let it draw
                },
                _ =>
                {
                    _progress.Remove(script.Dir);
                    SyncAll();   // DsGoogleFonts remembers why; Status() reads it back
                },
                p =>
                {
                    _progress[script.Dir] = p;
                    Tick(script);   // 17 MB is a lot of frames; do not rebuild the table on each
                }));
        }

        /// <summary>Updates just this script's cell, leaving the rest of the table alone.</summary>
        private static void Tick(DsScripts.Script script)
        {
            _roots.RemoveAll(r => r == null || r.panel == null);

            string id = StatusName(script);

            foreach (var root in _roots)
            {
                var cell = root.Q<Label>(id);
                if (cell != null) cell.text = StatusText(script);
            }
        }

        // ------------------------------------------------------------------- labels

        private static void UpdateLabels(VisualElement root)
        {
            var specimen = root.Q<Label>("font-specimen");
            if (specimen != null) DsFonts.ApplyWeight(specimen, _active, 700);

            var name = root.Q<Label>("font-specimen-name");
            if (name != null) name.text = _active.familyName;

            var meta = root.Q<Label>("font-specimen-meta");
            if (meta != null)
            {
                int count = _active.AvailableWeights().Count;
                meta.text = $"{count} weight{(count == 1 ? "" : "s")}" +
                            $"{(_active.HasItalics() ? " and italics" : "")}" +
                            $", {_active.fallbacks.Count} script fallbacks";
            }

            var active = root.Q<Label>("type-active-family");
            if (active != null) active.text = _active.familyName;

            var status = root.Q<Label>("font-fetch-status");
            if (status != null)
                status.text = string.IsNullOrEmpty(_status)
                    ? "Type a family name. It downloads from the Google Fonts repository and becomes a live " +
                      "typeface, weights and all. Try Cairo: it covers Arabic, and the Arabic row below will " +
                      "switch to it and stay joined."
                    : _status;
        }

        // -------------------------------------------------------------------- fetch

        private static void Fetch(string familyName)
        {
            if (_fetching) return;

            if (string.IsNullOrWhiteSpace(familyName))
            {
                _status = "Type a Google Font family name first.";
                SyncAll();
                return;
            }

            var entry = _manifest?.Find(familyName);
            if (entry == null)
            {
                _status = $"'{familyName.Trim()}' is not in the Google Fonts catalogue. " +
                          "Check the spelling: the name has to match exactly.";
                SyncAll();
                return;
            }

            _fetching = true;
            _status = $"Fetching {entry.family} from Google Fonts...";
            SyncAll();

            var fallbacks = _set != null ? _set.fallbacks : System.Array.Empty<FontAsset>();

            Runner.Get().StartCoroutine(DsGoogleFonts.Load(
                entry,
                new[] { 100, 200, 300, 400, 500, 600, 700, 800, 900 },
                fallbacks,
                family =>
                {
                    _fetching = false;
                    _status = $"{family.familyName} downloaded from Google Fonts and built at runtime, " +
                              $"{family.AvailableWeights().Count} weights.";

                    // No compiled stylesheet exists for it -- Unity cannot build one in a player
                    // -- so it applies through the inherited property instead.
                    SetFamily(family, null);
                },
                error =>
                {
                    _fetching = false;
                    _status = $"Could not fetch {entry.family}: {error}";
                    SyncAll();
                }));
        }

        // The table wants the useful part of the name and nothing else: "NotoSans-Bold SDF" ->
        // "NotoSans-Bold", and "Noto Sans Armenian (fetched)" -> "Noto Sans Armenian" (the blue
        // already says it was fetched, and the full suffix wrapped the cell onto two lines).
        private static string Short(string name)
        {
            if (name.EndsWith(DsGoogleFonts.FetchedSuffix, System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - DsGoogleFonts.FetchedSuffix.Length);
            if (name.EndsWith(" SDF", System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 4);
            return name;
        }

        /// <summary>
        /// A MonoBehaviour to hang the download coroutine on. The showcase is bootstrapped from
        /// a static class, so there is nothing else to run one.
        /// </summary>
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _instance;

            public static Runner Get()
            {
                if (_instance) return _instance;

                var go = new GameObject("ShowcaseFontRunner") { hideFlags = HideFlags.HideAndDontSave };
                _instance = go.AddComponent<Runner>();
                return _instance;
            }
        }
    }
}
