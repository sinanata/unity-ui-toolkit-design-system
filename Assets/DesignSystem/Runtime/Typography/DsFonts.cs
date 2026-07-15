using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Typography
{
    /// <summary>
    /// The runtime face of the typography system: swap the active typeface, install the
    /// multilingual fallback chain, pre-warm glyphs, and — the part that actually saves
    /// people — tell the truth about which font will serve which character.
    ///
    /// Static and non-generic on purpose. <c>DesignSystemBehaviourBase&lt;T&gt;</c> is generic,
    /// and a static member there would exist once per closed type, so a host wiring fonts
    /// through the UIDocument backend would silently not affect the PanelRenderer one. Same
    /// reasoning as <c>DesignSystemEvents</c>.
    /// </summary>
    public static class DsFonts
    {
        /// <summary>Raised after <see cref="Apply"/> changes the typeface on a root.</summary>
        public static event System.Action<VisualElement, DsFontFamily> FamilyChanged;

        // ------------------------------------------------------------------ apply

        /// <summary>
        /// Points a subtree at a typeface. <c>-unity-font-definition</c> is an INHERITED
        /// property, so setting it on the root reaches every <see cref="TextElement"/> below
        /// without touching any of them — and because the family's weight table is wired,
        /// every existing <c>-unity-font-style: bold</c> rule in the design system starts
        /// resolving to a real bold face on the same frame.
        /// </summary>
        public static bool Apply(VisualElement root, DsFontFamily family)
        {
            if (root == null) return false;

            if (family == null)
            {
                root.style.unityFontDefinition = StyleKeyword.Null;

                // Handing the root back to the stylesheet means handing it back to a BUNDLED
                // font, so the generator becomes the stylesheet's business again too.
                root.style.unityTextGenerator = StyleKeyword.Null;

                FamilyChanged?.Invoke(root, null);
                return true;
            }

            var regular = family.Regular;
            if (!regular) return false;

            root.style.unityFontDefinition = new StyleFontDefinition(regular);

            // A family DOWNLOADED at runtime cannot be drawn by the advanced text generator, and
            // that generator is the default, so applying one to a root without this would blank
            // out every word under it. Note this reaches every descendant, which is exactly the
            // trap ApplyGenerator exists to get back out of.
            ApplyGenerator(root, regular);

            FamilyChanged?.Invoke(root, family);
            return true;
        }

        /// <summary>Points a single element at one explicit weight, bypassing the weight table.</summary>
        public static bool ApplyWeight(VisualElement element, DsFontFamily family, int weight, bool italic = false)
        {
            if (element == null || family == null) return false;

            var face = family.Resolve(weight, italic);
            if (!face) return false;

            element.style.unityFontDefinition = new StyleFontDefinition(face);
            // The face IS the weight, so asking TextCore to embolden it on top would stack a
            // synthetic dilation over a real Bold and smear it.
            element.style.unityFontStyleAndWeight = FontStyle.Normal;

            ApplyGenerator(element, face);
            return true;
        }

        /// <summary>
        /// Points a single element at one explicit face, from any family.
        ///
        /// This exists because of Han unification. Chinese, Japanese and Korean share Unicode
        /// codepoints for characters they draw DIFFERENTLY, and a fallback chain resolves per
        /// CODEPOINT, not per language. So whichever CJK font sits first in the chain wins every
        /// shared Han character, and Chinese text quietly renders in Japanese letterforms.
        /// Nothing is missing, so <see cref="Coverage"/> reports a clean pass and a verifier goes
        /// green. It is still wrong, and a Chinese reader sees it at a glance.
        ///
        /// No ordering of the chain fixes it, because the right face depends on the LANGUAGE of
        /// the run and codepoints do not carry one. Fallbacks are a safety net for text you did
        /// not plan for, not a substitute for choosing a face for text you did. So the fix is the
        /// one a localized app is already making anyway: when the active language changes, change
        /// the face. Pass the language's own font here and it leads for that subtree; the chain
        /// stays behind it for anything that font does not cover.
        /// </summary>
        public static bool ApplyFace(VisualElement element, FontAsset face, bool? contentNeedsShaping = null)
        {
            if (element == null || !face) return false;

            element.style.unityFontDefinition = new StyleFontDefinition(face);
            ApplyGenerator(element, face, contentNeedsShaping);
            return true;
        }

        /// <summary>
        /// Names the text generator for this element, inline, and the choice is driven by the
        /// CONTENT, not the font.
        ///
        /// Every method above calls this, and any code that sets <c>-unity-font-definition</c> by
        /// hand must call it too. It is not a nicety. <c>-unity-text-generator</c> is an INHERITED
        /// property whose initial value is <c>Advanced</c>, so the moment one element under a root
        /// forces <c>Standard</c>, every element beneath THAT inherits <c>Standard</c> too — and an
        /// element cannot decline a value it inherited, only overwrite it. So the generator is
        /// decided per element, here, and written inline (which also reaches a dropdown popup — it
        /// is parented at PANEL scope, outside every stylesheet, where a class would match nothing).
        ///
        /// <para><paramref name="contentNeedsShaping"/> is the whole decision:</para>
        /// <list type="bullet">
        /// <item><b>true</b> — the text must be shaped (Arabic joining, Indic reordering, bidi).
        ///   Only the advanced generator shapes, so use it — provided the face actually <see
        ///   cref="CanShape"/>. <see cref="Resolve"/> already refuses to hand a shaping run to a
        ///   face that cannot, so in practice it can.</item>
        /// <item><b>false</b> — the text is one-glyph-per-codepoint (Latin, Greek, Cyrillic, CJK,
        ///   Armenian, Georgian). Use the STANDARD generator. It is the mature default, it is
        ///   correct for this text, and it sidesteps a real bug: the advanced generator draws a
        ///   downloaded CJK font — built natively from a runtime file — as a black or stale block on
        ///   its first frame. CJK never needed shaping, so there is no reason to pay that.</item>
        /// <item><b>null</b> — the content is unknown (a whole subtree, mixed languages). A BUNDLED
        ///   font is safe in the advanced generator and it copes with shaping if any turns up, so
        ///   default it to advanced. A FETCHED font is the one with the rough edges above, so
        ///   default it to standard and let callers opt a specific shaping run back up.</item>
        /// </list>
        /// </summary>
        public static void ApplyGenerator(VisualElement element, FontAsset face, bool? contentNeedsShaping = null)
        {
            if (element == null) return;

            bool advanced;

            if (contentNeedsShaping == true) advanced = CanShape(face);
            else if (contentNeedsShaping == false) advanced = false;
            else advanced = face && face.sourceFontFile != null;   // unknown: bundled->advanced, fetched->standard

            element.style.unityTextGenerator = advanced
                ? TextGeneratorType.Advanced
                : TextGeneratorType.Standard;
        }

        /// <summary>
        /// The USS classes that do the same job declaratively, for text whose font is set in UXML
        /// rather than from C#. <see cref="ApplyGenerator"/> writes the property inline and so
        /// beats both of them; these are for the hand-authored case, where <c>ds-intl</c> is what
        /// climbs a subtree back out of an ancestor's <c>ds-intl--off</c>.
        /// </summary>
        public const string StandardGeneratorClass = "ds-intl--off";

        /// <inheritdoc cref="StandardGeneratorClass"/>
        public const string AdvancedGeneratorClass = "ds-intl";

        // ------------------------------------------------------------------ shaping

        /// <summary>
        /// Can the ADVANCED text generator draw this face, and therefore SHAPE it — join Arabic
        /// into its initial/medial/final forms, reorder Devanagari and Khmer clusters, run bidi?
        ///
        /// A face that ships in the build owns a <see cref="Font"/> object and always can. A face
        /// DOWNLOADED at runtime owns only a file path, and can only if <see cref="TryEnableShaping"/>
        /// managed to hand it a native font face. So this is not a property of the font. It is the
        /// result of an experiment, and the honest answer for a fetched font is "we tried, and here
        /// is what happened".
        /// </summary>
        public static bool CanShape(FontAsset face) =>
            face && (face.sourceFontFile != null || Shapeable.Contains(face));

        /// <summary>
        /// True when this face must be drawn by the STANDARD generator, because the advanced one
        /// will not have it. The standard generator lays one glyph per codepoint in memory order,
        /// which is correct for Latin and CJK and <b>wrong</b> for anything that shapes: Arabic
        /// comes out with its letters unjoined, which is not a degraded rendering but an unreadable
        /// one.
        /// </summary>
        public static bool NeedsStandardGenerator(FontAsset face) => face && !CanShape(face);

        /// <summary>
        /// Tries to give a DOWNLOADED font a native font face, so the advanced generator will draw
        /// it and its text can be shaped. Returns whether it worked.
        ///
        /// This is the load-bearing trick in the whole font system, so here is exactly why it works.
        /// A downloaded font has no <see cref="Font"/> object — nothing in Unity's API builds one
        /// from bytes — only a file path, and the advanced generator appears to refuse it outright:
        ///
        /// <code>
        ///   FontAsset.EnsureNativeFontAssetIsCreated()          // NativeFontAsset.cs
        ///     if (mode == Static  &amp;&amp; characterTable.Count > 0) { warn; return; }
        ///     if (mode == Dynamic &amp;&amp; sourceFontFile == null)   { warn; return; }   // us
        ///     m_NativeFontAsset = Create(faceInfo, sourceFontFile, editorRef,
        ///                                m_SourceFontFilePath, ...);   // ... the PATH!
        /// </code>
        ///
        /// Read what it hands to <c>Create</c>: the file path. Native can build a face from a path,
        /// and it is the same path the managed rasterizer already reads without complaint. Only that
        /// second guard stops it ever trying, and the guard keys on <c>Dynamic</c>. The first guard
        /// rejects <c>Static</c> only once a character table exists.
        ///
        /// So a font whose table is still EMPTY walks past both by being <c>Static</c> for the
        /// length of one call. Native gets no Font, no editor ref, and a real path, and loads the
        /// face from the file. Going back to <c>Dynamic</c> restores managed rasterization, and the
        /// native face survives because <c>EnsureNativeFontAssetIsCreated</c> returns early once the
        /// pointer is set.
        ///
        /// Two preconditions, both easy to violate by accident:
        /// <list type="bullet">
        /// <item>call it BEFORE anything rasterizes a glyph, because an empty character table is the
        ///       entry fee;</item>
        /// <item>call it AFTER the fallback chain is wired, because the native face snapshots that
        ///       chain here and there is no public way to invalidate the snapshot later.</item>
        /// </list>
        /// </summary>
        public static bool TryEnableShaping(FontAsset face)
        {
            if (!face) return false;
            if (CanShape(face)) return true;

            // Too late: with a character table in hand it reads as a baked Static asset and the
            // first guard throws it out.
            if (face.characterTable.Count > 0) return false;
            if (EnsureNative == null || NativeHandle == null) return false;

            int complaints = 0;
            void Listen(string message, string stack, LogType type)
            {
                if (type != LogType.Log) complaints++;
            }

            var previous = face.atlasPopulationMode;
            Application.logMessageReceived += Listen;

            try
            {
                face.atlasPopulationMode = AtlasPopulationMode.Static;
                EnsureNative.Invoke(face, null);

                // A pointer on its own is not proof, because native can hand back a live pointer
                // wrapping a dead face and say nothing until something tries to draw. But it is not
                // SILENT about a face it failed to load: given an empty Font as a shim it says so,
                // out loud, right here. So the evidence is the pointer AND the silence.
                bool built = (IntPtr)NativeHandle.GetValue(face) != IntPtr.Zero && complaints == 0;

                if (built) Shapeable.Add(face);
                return built;
            }
            catch
            {
                // This reaches into Unity's internals. If a future version renames either member we
                // land back on the standard generator, which is where we were before any of this,
                // and no worse.
                return false;
            }
            finally
            {
                Application.logMessageReceived -= Listen;
                face.atlasPopulationMode = previous;
            }
        }

        /// <summary>Faces the advanced generator has been PROVEN to accept. See <see cref="CanShape"/>.</summary>
        private static readonly HashSet<FontAsset> Shapeable = new();

        private static readonly MethodInfo EnsureNative = typeof(FontAsset).GetMethod(
            "EnsureNativeFontAssetIsCreated", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo NativeHandle = typeof(FontAsset).GetField(
            "m_NativeFontAsset", BindingFlags.NonPublic | BindingFlags.Instance);

        // --------------------------------------------------------------- fallbacks

        /// <summary>
        /// Gives every face in the family the same fallback chain.
        ///
        /// This is the single most important call in the file, and skipping it is the classic
        /// way to ship broken multilingual UI. With no chain, Unity quietly serves missing
        /// glyphs from an OPERATING SYSTEM font: on Windows, Arabic silently resolves to Arial
        /// and Japanese to Microsoft YaHei, so the Editor looks flawless. WebGL has no OS
        /// fonts. The same screen renders as a row of empty boxes in the browser, and nothing
        /// anywhere logs a warning.
        ///
        /// The chain is wired onto every face (not just Regular) because a bold heading is
        /// laid out through a DIFFERENT FontAsset, and that one needs the fallbacks too.
        /// </summary>
        public static void WireFallbacks(DsFontFamily family)
        {
            if (family == null) return;

            foreach (var face in AllFaces(family))
            {
                if (!face) continue;

                face.fallbackFontAssetTable ??= new List<FontAsset>();
                face.fallbackFontAssetTable.Clear();

                foreach (var fb in family.fallbacks)
                    if (fb && fb != face)
                        face.fallbackFontAssetTable.Add(fb);
            }
        }

        /// <summary>
        /// The panel-wide chain, mirrored where <see cref="Resolve"/> can see it.
        ///
        /// The renderer looks in three places, in order: the element's own font, that font's
        /// <c>fallbackFontAssetTable</c>, and the panel's <c>PanelTextSettings</c>. The first two
        /// are reachable from a <see cref="FontAsset"/>; the third is not, and a font fetched at
        /// runtime lives ONLY there. Without this list, <see cref="Coverage"/> would model two
        /// thirds of reality and confidently report gaps in text that renders perfectly.
        /// </summary>
        public static readonly List<FontAsset> GlobalFallbacks = new();

        /// <summary>
        /// Installs the chain panel-wide, so it also covers fonts the family never heard of —
        /// including one downloaded at runtime. Belt and braces alongside
        /// <see cref="WireFallbacks"/>; harmless to call both.
        /// </summary>
        public static bool InstallFallbacks(PanelSettings panelSettings, IEnumerable<FontAsset> fallbacks)
        {
            if (panelSettings == null || fallbacks == null) return false;

            var settings = panelSettings.textSettings;
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelTextSettings>();
                settings.name = "DsPanelTextSettings";
                panelSettings.textSettings = settings;
            }

            settings.fallbackFontAssets ??= new List<FontAsset>();
            settings.fallbackFontAssets.Clear();
            GlobalFallbacks.Clear();

            foreach (var fb in fallbacks)
            {
                if (!fb) continue;

                settings.fallbackFontAssets.Add(fb);
                GlobalFallbacks.Add(fb);
            }

            return true;
        }

        // ------------------------------------------------------------------- glyphs

        /// <summary>
        /// Rasterizes the glyphs for <paramref name="text"/> up front. Dynamic font atlases
        /// fill in on demand, and the first frame that shows a new script pays for every
        /// glyph in it at once — visible as a hitch on a CJK-heavy screen.
        /// </summary>
        public static bool Warm(DsFontFamily family, string text)
        {
            if (family == null || string.IsNullOrEmpty(text)) return false;

            bool complete = true;
            foreach (var face in AllFaces(family))
            {
                if (!face) continue;
                if (!face.TryAddCharacters(text, includeFontFeatures: true))
                    complete = false;
            }

            return complete;
        }

        // -------------------------------------------------------------- diagnostics

        /// <summary>Which font will actually draw this character, and did it need a fallback.</summary>
        public readonly struct Resolution
        {
            public readonly FontAsset Font;
            public readonly bool FromFallback;

            /// <summary>False means a missing glyph: a box in the build, whatever the Editor shows.</summary>
            public bool Covered => Font != null;

            public Resolution(FontAsset font, bool fromFallback)
            {
                Font = font;
                FromFallback = fromFallback;
            }
        }

        /// <summary>
        /// Resolves one codepoint against a face and its fallback chain, using only the public
        /// <c>HasCharacter</c> API.
        ///
        /// It deliberately does NOT consult Unity's OS-font fallback, and that is the feature,
        /// not a limitation: OS fonts do not exist in a WebGL build. A report built on this
        /// says what the SHIPPED app will do, so a gap shows up here rather than in a bug
        /// report from someone reading your UI in Arabic.
        /// </summary>
        /// <param name="requireShaping">
        /// Set for a script that must be SHAPED (Arabic, Hebrew, the Indic family, Thai, Khmer) and
        /// faces that cannot shape are skipped even when they have every glyph. Having the glyphs is
        /// not the same as being able to draw the word: a font that cannot shape lays Arabic out as
        /// disconnected letters, which no reader of Arabic would call a rendering of it. Better the
        /// next font in the chain, which can.
        /// </param>
        public static Resolution Resolve(FontAsset face, uint codepoint, bool requireShaping = false)
        {
            if (!face) return default;

            if (Eligible(face, requireShaping) &&
                face.HasCharacter(codepoint, searchFallbacks: false, tryAddCharacter: true))
                return new Resolution(face, false);

            if (face.fallbackFontAssetTable != null)
            {
                foreach (var fb in face.fallbackFontAssetTable)
                    if (Eligible(fb, requireShaping) &&
                        fb.HasCharacter(codepoint, searchFallbacks: true, tryAddCharacter: true))
                        return new Resolution(fb, true);
            }

            // And finally the panel-wide chain, because that is the last place the RENDERER looks
            // before it gives up. A font fetched at runtime lands here and nowhere else, so a
            // Resolve that skipped this would report a gap for text that draws perfectly.
            foreach (var fb in GlobalFallbacks)
                if (Eligible(fb, requireShaping) &&
                    fb.HasCharacter(codepoint, searchFallbacks: true, tryAddCharacter: true))
                    return new Resolution(fb, true);

            return default;
        }

        private static bool Eligible(FontAsset face, bool requireShaping) =>
            face && (!requireShaping || CanShape(face));

        /// <summary>
        /// Coverage for a whole string: the font that serves the first character it can, plus
        /// how many characters nothing in the chain can draw. Surrogate pairs count once.
        /// </summary>
        public static Resolution Coverage(DsFontFamily family, string text, out int missing,
                                          bool requireShaping = false)
        {
            missing = 0;
            if (family == null) return default;

            return Coverage(family.Regular, text, out missing, requireShaping);
        }

        /// <summary>
        /// The same question asked of one face rather than a family, which is what you need when
        /// a subtree has been pinned to a specific font by <see cref="ApplyFace"/>.
        /// </summary>
        public static Resolution Coverage(FontAsset face, string text, out int missing,
                                          bool requireShaping = false)
        {
            missing = 0;
            var result = default(Resolution);

            if (!face || string.IsNullOrEmpty(text))
                return result;

            var e = StringInfo.GetTextElementEnumerator(text);
            while (e.MoveNext())
            {
                string cluster = e.GetTextElement();
                int cp = char.ConvertToUtf32(cluster, 0);

                // Whitespace is not evidence of coverage either way.
                if (cp == ' ' || cp == '\t' || cp == '\n') continue;

                var hit = Resolve(face, (uint)cp, requireShaping);
                if (!hit.Covered) missing++;
                else if (!result.Covered) result = hit;
            }

            return result;
        }

        // ---------------------------------------------------------------- internals

        private static IEnumerable<FontAsset> AllFaces(DsFontFamily family)
        {
            if (family.weights != null)
                foreach (var f in family.weights)
                    yield return f;

            if (family.italics != null)
                foreach (var f in family.italics)
                    yield return f;
        }
    }
}
