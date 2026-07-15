using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DesignSystem.Runtime.Typography;
using Showcase.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace UIDocumentDesignSystem.BuildTools
{
    /// <summary>
    /// Asks the typography system the questions the Editor is happy to lie about.
    ///
    /// The lie in question: with no fallback chain configured, Unity quietly serves missing
    /// glyphs from an OPERATING SYSTEM font. On Windows, Arabic silently resolves to Arial and
    /// Japanese to Microsoft YaHei — so multilingual text looks flawless in the Editor and
    /// renders as empty boxes in a WebGL build, where no OS fonts exist. Nothing warns you.
    ///
    /// So this resolves every script through <see cref="DsFonts.Coverage"/>, which walks only
    /// the explicit chain and never asks the OS. What it reports is what a BUILD will do. It has
    /// already earned its keep once: it caught Cyrillic being drawn by a JAPANESE font, because
    /// the chain had no Latin/Greek/Cyrillic catch-all at its head.
    ///
    /// It also re-checks the two facts the importer is built on, so that a future Unity upgrade
    /// that quietly changes either one fails here rather than in someone's shipped game:
    ///   1. a variable font's named instances are reachable via (instance &lt;&lt; 16) as a face index;
    ///   2. a wired fontWeightTable makes `-unity-font-style: bold` land on a REAL bold face.
    ///
    ///   Unity -batchmode -quit -executeMethod
    ///     UIDocumentDesignSystem.BuildTools.ShowcaseFontVerifier.VerifyBatch
    /// </summary>
    public static class ShowcaseFontVerifier
    {
        [MenuItem("Design System/Showcase/Verify Fonts")]
        public static void Verify() => Run();

        public static void VerifyBatch() => EditorApplication.Exit(Run() ? 0 : 1);

        private static bool Run()
        {
            var sb = new StringBuilder(4096);
            void L(string s = "") => sb.AppendLine(s);

            bool ok = true;

            L("==================== FONT VERIFICATION ====================");

            var set = AssetDatabase.LoadAssetAtPath<ShowcaseFontSet>(
                "Assets/Showcase/Resources/ShowcaseFontSet.asset");

            if (set == null)
            {
                Debug.LogError("[Fonts] No ShowcaseFontSet. Run Design System > Showcase > Bake Showcase Fonts.");
                return false;
            }

            L($"fallback chain: {string.Join(" -> ", set.fallbacks.Where(f => f).Select(f => f.name))}");

            // Every BUNDLED face has to own a Font object, because the advanced text generator will
            // not touch a FontAsset without one -- and the advanced text generator is the only thing
            // in Unity that shapes. A fallback missing its Source Font File is an Arabic line with
            // its letters unjoined, and not one other check in this report would notice: every glyph
            // is present, correct, and in the wrong shape.
            foreach (var face in set.choices.Where(c => c.family).SelectMany(c => Faces(c.family))
                         .Concat(set.fallbacks.Where(f => f)))
            {
                if (face.sourceFontFile) continue;

                L($"FAIL {face.name} has no Source Font File. The advanced text generator refuses it, " +
                  "so anything it draws comes out unshaped.");
                ok = false;
            }

            L();

            foreach (var choice in set.choices)
            {
                var family = choice.family;
                if (!family) continue;

                var weights = family.AvailableWeights();
                L($"---- {family.familyName} ----");
                L($"   weights: {string.Join(", ", weights)}" +
                  $"{(family.HasItalics() ? "   + italics" : "")}");
                L($"   stylesheet: {(choice.sheet ? choice.sheet.name : "MISSING")}");

                // Real bold, or a dilated Regular? The weight table is the difference, and
                // `isUsingAlternateTypeface` is how TextCore says which one you got.
                var regular = family.Resolve(400);
                var bold = family.Resolve(700);

                bool realBold = regular && bold && regular != bold &&
                                regular.fontWeightTable != null &&
                                regular.fontWeightTable.Length > 7 &&
                                regular.fontWeightTable[7].regularTypeface == bold;

                L($"   bold: {(realBold ? "REAL (weightTable[7] -> " + bold.name + ")" : "FAUX (weight table not wired)")}");
                if (!realBold && weights.Contains(700)) ok = false;

                // A weight table may not name the face that owns it, and only ONE face may carry
                // one. Unity's advanced text generator walks the weight table hunting for cycles,
                // counts any revisited face as one, and throws the whole table away -- which takes
                // real bold with it and floods the console with "Circular reference detected."
                // The bold check above stays green while that happens, so it has to be its own
                // assertion. This is how the bug shipped once already.
                int carriers = 0;

                foreach (var face in Faces(family))
                {
                    var table = face.fontWeightTable;
                    if (table == null) continue;

                    bool carries = false;

                    for (int slot = 1; slot < table.Length; slot++)
                    {
                        var pair = table[slot];
                        if (pair.regularTypeface || pair.italicTypeface) carries = true;

                        if (pair.regularTypeface == face || pair.italicTypeface == face)
                        {
                            L($"   FAIL weight table: {face.name} names ITSELF at slot {slot} -- a one-node cycle");
                            ok = false;
                        }
                    }

                    if (carries) carriers++;
                }

                if (carriers > 1)
                {
                    L($"   FAIL weight table: {carriers} faces carry one; only Regular may, or they " +
                      "reference each other and every one of them is circular");
                    ok = false;
                }
                else
                {
                    L($"   weight table: 1 carrier, no self-reference");
                }

                // Every script, resolved the way a build resolves it.
                foreach (var (label, sample) in Samples)
                {
                    var hit = DsFonts.Coverage(family, sample, out int missing);

                    string verdict = !hit.Covered
                        ? "NO GLYPHS"
                        : missing > 0
                            ? $"{hit.Font.name}  ({missing} chars MISSING)"
                            : hit.Font.name + (hit.FromFallback ? "  (fallback)" : "");

                    bool bad = !hit.Covered || missing > 0;
                    if (bad) ok = false;

                    L($"   {(bad ? "FAIL" : "ok  ")} {label,-12} {verdict}");
                }

                L();
            }

            // ---- Fetched scripts ---------------------------------------------------------
            //
            // Everything above is what we BUNDLE. Most scripts are not bundled -- they are fetched
            // at runtime, which is the only way "any language" is affordable, because CJK alone is
            // 36 MB. A fetch we cannot check from here we can still check ONE thing about, and it
            // is the thing that actually breaks: the font has to be addressable. DsScripts names a
            // repo folder; if that folder is not in the manifest, EnsureFont fails at runtime with
            // an empty row and nobody finds out until a user types in Tamil.
            L("---- Fetched scripts (not in the build; must be reachable) ----");

            var manifestJson = AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
                "Assets/Showcase/Resources/GoogleFontsManifest.json");

            var manifest = DsGoogleFonts.Manifest.Parse(manifestJson ? manifestJson.text : null);

            foreach (var (label, sample) in Fetched)
            {
                // Ask the same question the running showcase asks: what does this text need that
                // the bundled chain cannot draw?
                var needed = DsScripts.Missing(set.choices[0].family.Regular, sample);

                if (needed.Count == 0)
                {
                    L($"   ok   {label,-12} already covered by the bundled chain");
                    continue;
                }

                foreach (var script in needed)
                {
                    // A fetched font CAN be shaped now (proved further down, by rendering). So this
                    // is no longer a law of physics -- it is a deliberate line about what the
                    // DEFAULT fallback chain is allowed to depend on.
                    //
                    // A font the user asks for by name has no choice but to lean on TryEnableShaping;
                    // that is the feature. The chain that quietly catches every script nobody chose
                    // is different: it should still work on the first frame, with no network, and on
                    // whatever Unity ships next. So anything that must be SHAPED to be readable stays
                    // in the build, and the wire is left to carry only what draws one glyph per
                    // codepoint in order.
                    if (script.Shaping)
                    {
                        L($"   FAIL {label,-12} needs SHAPING but is left to the network. It would work " +
                          $"today, on TryEnableShaping,\n" +
                          $"        but the default chain should not rest on that. Add '{script.Dir}' " +
                          "to the baker.");
                        ok = false;
                        continue;
                    }

                    var entry = manifest.FindByDir(script.Dir);

                    if (entry?.files == null || entry.files.Length == 0)
                    {
                        L($"   FAIL {label,-12} '{script.Dir}' is not in the manifest; it can never be fetched");
                        ok = false;
                        continue;
                    }

                    L($"   ok   {label,-12} {script.Family}  <- {script.Dir}  (no shaping needed)");
                }
            }

            // ---- The hole everything else stands on ------------------------------------------
            //
            // A font downloaded at runtime has no UnityEngine.Font behind it, only a file path, and
            // Unity's advanced text generator refuses one out of hand: "FontAsset is invalid. Please
            // assign a Source Font File." Shaping lives ONLY in that generator. Taken at face value
            // it means a downloaded font can never draw Arabic, or Devanagari, or Khmer.
            //
            // DsFonts.TryEnableShaping gets past it (the method explains how, and why it is sound),
            // and every promise the fetch feature makes rests on that continuing to work. It is an
            // undocumented gap in a managed guard, so it could close in any Unity release -- and it
            // would close SILENTLY. Fetched Arabic would go back to being unjoined, every coverage
            // check above would stay green, and the first person to notice would be someone who
            // reads Arabic.
            //
            // So do not take it on trust. Render it.
            L("---- Can a downloaded font still be SHAPED? ----");

            const string arabic = "أنا قادر على أكل الزجاج";
            const string ttf = "Assets/Showcase/Resources/DsFonts/NotoSansArabic/NotoSansArabic.ttf";

            // A path and nothing else, which is all a download ever gives you.
            var fetched = System.IO.File.Exists(ttf)
                ? FontAsset.CreateFontAsset(ttf, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024)
                : null;

            if (fetched == null)
            {
                L($"   FAIL could not build a font asset from '{ttf}'");
                ok = false;
            }
            else if (fetched.sourceFontFile != null)
            {
                L("   FAIL the test font came back WITH a Font object, so it proves nothing about " +
                  "a real download");
                ok = false;
            }
            else if (!DsFonts.TryEnableShaping(fetched))
            {
                L("   FAIL Unity closed the gap: the advanced generator will not take a font that has " +
                  "only a file path.\n" +
                  "        Fetched Arabic and Indic now render UNJOINED. Bundle every shaping script " +
                  "(see DsScripts.Script.Shaping)\n" +
                  "        and stop offering the others, because DsFonts.Resolve will refuse them and " +
                  "leave the row blank.");
                ok = false;
            }
            else
            {
                // The pointer is not the proof; the pixels are. Native can hand back a live pointer
                // wrapping a dead face and only admit it when something tries to draw. And a line
                // that draws is still not a line that SHAPED -- so measure it through both
                // generators. Joining fuses letters, so the advanced line comes out NARROWER.
                float advanced = Measure(fetched, arabic, TextGeneratorType.Advanced);
                float standard = Measure(fetched, arabic, TextGeneratorType.Standard);

                if (advanced <= 1f)
                {
                    L($"   FAIL the face loaded but draws NOTHING (advanced width {advanced:F1}px). " +
                      "A live pointer around a dead face.");
                    ok = false;
                }
                else if (Mathf.Approximately(advanced, standard))
                {
                    L($"   FAIL it drew ({advanced:F1}px) but did not SHAPE: both generators measured " +
                      "the same, so the letters are not joining.");
                    ok = false;
                }
                else
                {
                    L($"   ok   drew Arabic from a bare file path, SHAPED: {advanced:F1}px advanced " +
                      $"vs {standard:F1}px standard.");
                    L("        The narrowing is the joining. A downloaded font can be shaped, so any " +
                      "Google font can serve any language it covers.");
                }
            }

            L();

            L(ok
                ? "PASS: bundled scripts render without borrowing an OS font, every fetched one is " +
                  "reachable, and a downloaded font can still be shaped."
                : "FAIL: something above would render as empty boxes, or unjoined, outside the Editor.");
            L("==================== END ====================");

            if (ok) Debug.Log(sb.ToString());
            else Debug.LogError(sb.ToString());

            return ok;
        }

        private static IEnumerable<FontAsset> Faces(DsFontFamily family)
        {
            foreach (var f in family.weights ?? System.Array.Empty<FontAsset>())
                if (f) yield return f;

            foreach (var f in family.italics ?? System.Array.Empty<FontAsset>())
                if (f) yield return f;
        }

        /// <summary>
        /// Lays out one line through one text generator on a real runtime panel and returns its
        /// width. Zero means the font drew nothing, whatever any pointer claimed.
        /// </summary>
        private static float Measure(FontAsset face, string text, TextGeneratorType generator)
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UnityDefaultRuntimeTheme");

            var go = new GameObject("FontVerifierPanel") { hideFlags = HideFlags.HideAndDontSave };

            try
            {
                var doc = go.AddComponent<UIDocument>();
                doc.panelSettings = settings;

                var label = new Label(text);
                label.style.unityFontDefinition = new StyleFontDefinition(face);
                label.style.unityTextGenerator = generator;
                label.style.fontSize = 20;

                doc.rootVisualElement.Add(label);

                // MeasureTextSize reads computedStyle, so the panel has to resolve those two inline
                // properties before the answer means anything. One update does it, and the only door
                // in is internal.
                var panel = doc.rootVisualElement.panel;
                panel?.GetType()
                     .GetMethod("UpdateWithoutRepaint",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                     ?.Invoke(panel, null);

                return label.MeasureTextSize(text, 0, VisualElement.MeasureMode.Undefined,
                                                   0, VisualElement.MeasureMode.Undefined).x;
            }
            catch
            {
                return 0f;
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(settings);
            }
        }

        // The scripts we BUNDLE. Every one of these must render from the build alone, with no
        // network and no OS font. Same lines the showcase renders, so the two cannot drift apart.
        private static readonly (string label, string sample)[] Samples =
        {
            ("Latin", "The quick brown fox jumps over the lazy dog"),
            ("Turkish", "Pijamalı hasta yağız şoföre çabucak güvendi"),
            ("Vietnamese", "Tôi có thể ăn thủy tinh mà không hại gì"),
            ("Greek", "Ξεσκεπάζω την ψυχοφθόρα βδελυγμία"),
            ("Cyrillic", "Съешь же ещё этих мягких французских булок"),
            ("Arabic", "أنا قادر على أكل الزجاج و هذا لا يؤلمني"),
            ("Hebrew", "אני יכול לאכול זכוכית וזה לא מזיק לי"),
            ("Devanagari", "मैं काँच खा सकता हूँ, मुझे उस से कोई पीड़ा नहीं होती"),
            ("Bengali", "আমি কাঁচ খেতে পারি, তাতে আমার কোনো ক্ষতি হয় না"),
            ("Tamil", "நான் கண்ணாடி சாப்பிடுவேன், அதனால் எனக்கு ஒரு கேடும் வராது"),
            ("Thai", "ฉันกินกระจกได้ แต่มันไม่ทำให้ฉันเจ็บ"),
            ("Khmer", "ខ្ញុំអាចញ៉ាំកញ្ចក់បាន ដោយគ្មានបញ្ហា"),
        };

        // The scripts we do NOT bundle, and therefore have to be able to fetch. Every one of them
        // draws one glyph per codepoint in order, which is the ONLY thing a fetched font can do.
        private static readonly (string label, string sample)[] Fetched =
        {
            ("Armenian",  "Ես կրնամ ապակի ուտել և ինծի անհանգիստ չըներ"),
            ("Georgian",  "მინას ვჭამ და არა მტკივა"),
            ("Japanese",  "私はガラスを食べられます。それは私を傷つけません。"),
            ("Korean",    "나는 유리를 먹을 수 있어요. 그래도 아프지 않아요"),
            ("Chinese",   "我能吞下玻璃而不伤身体"),
        };
    }
}
