#if UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Showcase.Runtime;

namespace UIDocumentDesignSystem.BuildTools
{
    // One-shot diagnostic: enter play mode and open every kind of showcase
    // dropdown — on the flat page and inside world-space exhibits — then
    // measure the popup against the field and its panel. Verifies the
    // EnsureDropdownMenus contract with numbers: opens DOWNWARD, height
    // capped, pinned to the field's width, no horizontal scroller, popup
    // bottom inside the panel, and (once a theme is applied) wearing the
    // page's palette. Run:
    //   Unity -executeMethod UIDocumentDesignSystem.BuildTools.PopupProbe.Run
    // (no -quit: the probe exits the editor itself when done)
    //
    // MEASURE IN THE OVERLAY'S LOCAL SPACE. Nothing else here is safe, and the
    // reason is worth keeping: `worldBound` is pixels inside a screen panel but
    // METRES inside a world-space one, because the panel root carries a
    // pixels-to-metres transform. A laid-out 206x48 field reports a worldBound
    // of about 2.1x0.4 there.
    //
    // This probe used to read those metres, compare them against `resolvedStyle`
    // pixels, conclude "field never laid out (2.1x0.4)", and skip every world
    // test — which is how a world-space placement bug shipped green, and why the
    // header here spent a while asserting that world panels simply cannot resolve
    // a layout in a background editor. They can. They always could. The probe was
    // reading the wrong units. WorldToLocal normalises everything into the pixels
    // that `style.top` and `resolvedStyle` both speak, and on a screen panel it
    // is a pure translation, so the screen numbers are unchanged.
    [InitializeOnLoad]
    static class PopupProbeHook
    {
        static PopupProbeHook()
        {
            if (SessionState.GetBool(PopupProbe.FLAG, false))
                EditorApplication.update += PopupProbe.Tick;
        }
    }

    public static class PopupProbe
    {
        public const string FLAG = "PopupProbe.armed";

        // Compile smoke test for CI-ish batch runs: if scripts don't compile,
        // -executeMethod never resolves and Unity exits nonzero on its own.
        public static void CompileCheck() => EditorApplication.Exit(0);

        enum Step
        {
            WaitFlat, ScreenColors, ScreenInputs, ScreenNarrow, ScreenStuffed, ScreenFlipUp,
            ApplyTheme, ScreenThemedPopup, Randomize, ScreenRandomPopup, EnterWorld,
            WorldColors, WorldInputs, WorldSort, Report
        }

        // The design system's own --color-surface-elev. If a themed popup still paints THIS, the
        // theme never reached panel scope.
        static readonly Color BASE_SURFACE_ELEV = new Color(0.102f, 0.137f, 0.188f);

        static readonly CustomStyleProperty<Color> SURFACE_ELEV =
            new CustomStyleProperty<Color>("--color-surface-elev");

        // When set, Measure also asserts the popup is wearing the same palette as the page. The two
        // are different questions, because the two palettes arrive by different roads: a baked theme
        // rides the CASCADE (assert against the page's resolved token), a Randomize palette is STAMPED
        // inline and has no token to resolve (assert against the palette object itself).
        static bool _expectThemed;
        static bool _expectRandom;

        // Screen-only entry point. The world steps need a Game view that genuinely renders and cost
        // ~1500 frames each to time out when it doesn't, so when the question is about the flat page
        // (which is where the theme dropdown lives) skip them.
        //   Unity -executeMethod UIDocumentDesignSystem.BuildTools.PopupProbe.RunScreen
        //
        // SessionState, not a static bool: entering play mode reloads the domain and would wipe it.
        public const string SCREEN_FLAG = "PopupProbe.screenOnly";

        static bool ScreenOnly => SessionState.GetBool(SCREEN_FLAG, false);

        public static void RunScreen()
        {
            SessionState.SetBool(SCREEN_FLAG, true);
            Run();
        }

        static Step _step = Step.WaitFlat;
        static int _frames;         // global frame counter (timeout guard)
        static int _phase;          // sub-phase inside a dropdown test
        static int _phaseFrames;
        static int _failures;
        static VisualElement _flatRoot;
        static WorldSpaceCorridor _corridor;
        static DropdownField _screenDd;   // cached: stuffing renames its choices, and Show() reparents it
        static List<string> _choicesBackup;
        static EditorWindow _gameView;
        static readonly List<string> _log = new();

        // PanelRenderer panels lay out only when a camera actually renders
        // them, and a background-launched editor may never paint its Game
        // view — screen UIDocuments still lay out (player-loop driven) but
        // every world exhibit stays a 2x0 stub. Acquire the Game view once
        // (creating + focusing it if the saved layout lacks one) and mark it
        // dirty every tick so the world panels get real render passes.
        static void PumpGameView()
        {
            if (_gameView == null)
            {
                var t = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                if (t != null) _gameView = EditorWindow.GetWindow(t, false, null, true);
            }
            if (_gameView != null) _gameView.Repaint();
        }

        public static void Run()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Showcase/Showcase.unity");
            // PanelRenderer panels only lay out while the Game view actually
            // renders — make sure one exists and has focus before play mode.
            EditorApplication.ExecuteMenuItem("Window/General/Game");
            SessionState.SetBool(FLAG, true);
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        public static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            PumpGameView();
            _frames++;
            _phaseFrames++;
            if (_frames > 15000)   // hard stop (layout gates may wait out FIT_TIMEOUT per panel)
            {
                Fail("TIMEOUT", $"stuck at {_step}/{_phase}");
                Done();
                return;
            }

            switch (_step)
            {
                case Step.WaitFlat:
                {
                    if (_frames < 55) return;
                    var showcase = GameObject.Find("Showcase");
                    _flatRoot = showcase != null ? showcase.GetComponent<UIDocument>()?.rootVisualElement : null;
                    _corridor = Object.FindAnyObjectByType<WorldSpaceCorridor>();
                    if (_flatRoot == null || _corridor == null) return;
                    Next(Step.ScreenColors);
                    return;
                }

                // The COLORS section's theme picker. This is the dropdown users actually complain
                // about and the one the probe never covered: it sits high on a long page, inside the
                // page ScrollView, and it is the only dropdown whose choices are replaced at runtime.
                case Step.ScreenColors:
                    RunDropdownTest("SCREEN colors(theme picker)",
                        _flatRoot.Q<DropdownField>("theme-provider-dropdown"),
                        expectVScroller: false, then: Step.ScreenInputs);
                    return;

                case Step.ScreenInputs:
                    if (_screenDd == null) _screenDd = FindByFirstChoice(_flatRoot, "Common");
                    RunDropdownTest("SCREEN inputs", _screenDd,
                        expectVScroller: false, then: Step.ScreenNarrow);
                    return;

                // The 140px quality picker in TABBED PANELS. A popup pinned to its field's width
                // cannot show "Medium", and for a long time this one rendered it as "Me..". It is the
                // narrowest dropdown in the showcase, so it is the one that proves the popup grows.
                case Step.ScreenNarrow:
                    RunDropdownTest("SCREEN narrow(quality)", FindByFirstChoice(_flatRoot, "Low"),
                        expectVScroller: false, then: Step.ScreenStuffed);
                    return;

                case Step.ScreenStuffed:
                {
                    if (_screenDd != null && _choicesBackup == null && _phase == 0)
                    {
                        _choicesBackup = new List<string>(_screenDd.choices);
                        var stuffed = new List<string>();
                        for (int i = 0; i < 40; i++) stuffed.Add($"Stuffed {i} with a deliberately long palette name");
                        _screenDd.choices = stuffed;
                        _screenDd.index = 0;
                    }
                    RunDropdownTest("SCREEN stuffed(40)", _screenDd, expectVScroller: true,
                        then: Step.ScreenFlipUp);
                    return;
                }

                // The flip. Park a field hard against the BOTTOM of the viewport, where there is no
                // room beneath it, and the popup must open UPWARD with a real list — not be forced
                // down off the panel and clamped to a sliver, which is what produced the original
                // "opens upwards, two items tall" report.
                case Step.ScreenFlipUp:
                    RunDropdownTest("SCREEN flip-up(no room below)", _screenDd, expectVScroller: false,
                        then: Step.ApplyTheme, parkAtBottom: true);
                    return;

                // Pick a real third-party palette through the actual UI.
                case Step.ApplyTheme:
                {
                    var picker = _flatRoot.Q<DropdownField>("theme-provider-dropdown");
                    if (picker?.choices == null || picker.choices.Count < 2)
                    {
                        // The provider populates its list from the bundled palette JSON; give it time.
                        if (_phaseFrames > 900) { Fail("ApplyTheme", "theme list never populated"); Next(Step.Report); }
                        return;
                    }
                    if (_phase == 0)
                    {
                        picker.value = picker.choices[1];   // first real palette after "Design System default"
                        _log.Add($"[PopupProbe] applied theme '{picker.choices[1]}'");
                        _phase = 1; _phaseFrames = 0;
                        return;
                    }
                    if (_phaseFrames < 30) return;   // let the swap land and styles re-resolve
                    _expectThemed = true;
                    Next(Step.ScreenThemedPopup);
                    return;
                }

                // THE one thing the cascade can silently fail to reach. The popup is not in the document
                // root's subtree — Unity parents it under the PANEL root — so if a `:root` theme sheet
                // added at panel scope does not actually match the panel root, the tokens never land and
                // the popup stays dark while the whole page re-themes around it. Nothing else in the
                // showcase would show that, which is exactly how it went unnoticed.
                case Step.ScreenThemedPopup:
                    RunDropdownTest("SCREEN themed popup", _screenDd, expectVScroller: false,
                        then: Step.Randomize);
                    return;

                // And the ONE palette that cannot use that road. Randomize is invented at runtime, so
                // there is no stylesheet to add at panel scope and nothing for the popup to inherit —
                // it has to be stamped, at the only moment it exists. Left unstamped it silently keeps
                // the base dark/light chrome while the page around it goes lime green, which is exactly
                // what shipped.
                case Step.Randomize:
                {
                    var btn = _flatRoot.Q<Button>("theme-randomize");
                    if (btn == null)
                    {
                        if (_phaseFrames > 300) { Fail("Randomize", "'theme-randomize' button not found"); Next(Step.Report); }
                        return;
                    }
                    if (_phase == 0)
                    {
                        using (var e = NavigationSubmitEvent.GetPooled())
                        {
                            e.target = btn;
                            btn.SendEvent(e);
                        }
                        _phase = 1; _phaseFrames = 0;
                        return;
                    }
                    if (_phaseFrames < 30) return;   // let the stamp land

                    // The cascade check would now fail by design: Randomize CLEARS the theme sheet, so
                    // the page resolves --color-surface-elev back to the base value and "did the page
                    // move?" answers no. Different palette, different assertion.
                    _expectThemed = false;
                    _expectRandom = true;
                    _log.Add($"[PopupProbe] randomized: surface-elev={Hex(RandomSurfaceElev())}");
                    Next(Step.ScreenRandomPopup);
                    return;
                }

                case Step.ScreenRandomPopup:
                    RunDropdownTest("SCREEN random popup", _screenDd, expectVScroller: false,
                        then: ScreenOnly ? Step.Report : Step.EnterWorld);
                    return;

                case Step.EnterWorld:
                {
                    if (_choicesBackup != null)   // restore before leaving the flat page
                    {
                        if (_screenDd != null) { _screenDd.choices = _choicesBackup; _screenDd.index = 0; }
                        _choicesBackup = null;
                    }
                    // Go through the REAL mode switch (hero band tab), not
                    // corridor.Show(): the bootstrap also moves the camera into
                    // the corridor and wires the exhibits, and world panels
                    // that no camera renders never resolve a layout.
                    if (_phase == 0)
                    {
                        var worldTab = FindButtonByText(_flatRoot, "World Space");
                        if (worldTab == null)
                        {
                            if (_phaseFrames > 300) { Fail("EnterWorld", "'World Space' tab not found"); Next(Step.Report); }
                            return;
                        }
                        using (var e = NavigationSubmitEvent.GetPooled())
                        {
                            e.target = worldTab;
                            worldTab.SendEvent(e);
                        }
                        _phase = 1; _phaseFrames = 0;
                        return;
                    }
                    if (_phaseFrames < 180) return;   // camera swap + first panel reloads
                    Next(Step.WorldColors);
                    return;
                }

                case Step.WorldColors:
                    RunDropdownTest("WORLD colors", FindWorld(r => r.Q<DropdownField>("theme-provider-dropdown")),
                        expectVScroller: false, then: Step.WorldInputs);
                    return;

                case Step.WorldInputs:
                    RunDropdownTest("WORLD inputs", FindWorld(r => FindByFirstChoice(r, "Common")),
                        expectVScroller: false, then: Step.WorldSort);
                    return;

                case Step.WorldSort:
                    RunDropdownTest("WORLD sort(bottom)", FindWorld(r => FindByFirstChoice(r, "Newest")),
                        expectVScroller: false, then: Step.Report);
                    return;

                case Step.Report:
                {
                    foreach (var line in _log) Debug.Log(line);
                    Debug.Log($"[PopupProbe] {( _failures == 0 ? "ALL PASS" : _failures + " FAILURE(S)")}");
                    Done();
                    return;
                }
            }
        }

        // Sub-phases: 0 = scroll into view, 1 = send open event, 2 = wait + measure, 3 = close,
        // 4 = wait closed.
        //
        // parkAtBottom drives the OTHER half of the placement contract: a field with no room beneath
        // it must flip UP and still show a real list. That is the case the old tuner got catastrophically
        // wrong — it forced the popup down anyway, off the bottom of the panel, where Unity's own
        // visibility pass shoved it back up; the two fought, and the user got an upward popup clamped
        // to a 72px sliver.
        static void RunDropdownTest(string tag, DropdownField dd, bool expectVScroller, Step then,
                                    bool parkAtBottom = false)
        {
            if (dd == null)
            {
                if (_phaseFrames < 300) return;   // still settling? keep looking
                Fail(tag, "dropdown not found");
                Next(then);
                return;
            }

            var menu = dd.panel?.visualTree.Q(className: "unity-base-dropdown");
            switch (_phase)
            {
                case 0:
                {
                    // Never open against an un-laid-out panel.
                    //
                    // Ask `layout`, NOT `worldBound`. A world-space panel carries a pixels-to-metres
                    // transform on its root, so a perfectly laid-out 206x48 field reports a worldBound
                    // of about 2.1x0.4 — and the old check ("width < 10 means it never laid out")
                    // called that a failure and skipped every world test. It was not a rendering
                    // limitation at all; the probe was reading metres and expecting pixels. `layout` is
                    // the element's own rect, in the same pixels everything else here is measured in.
                    var lay = dd.layout;
                    if (float.IsNaN(lay.width) || float.IsNaN(lay.height) || lay.width < 10f || lay.height < 10f)
                    {
                        if (_phaseFrames > 1500)
                        {
                            Fail(tag, $"field never laid out (layout {lay.width:F1}x{lay.height:F1}, " +
                                      $"worldBound {dd.worldBound.width:F2}x{dd.worldBound.height:F2})");
                            Next(then);
                        }
                        return;
                    }

                    // SCROLL THE FIELD INTO VIEW FIRST. Without this the probe opened dropdowns on
                    // fields sitting far below the fold (the COLORS picker measured y=536 inside a
                    // 431px panel), where there is no room below by construction — so it was scoring
                    // the popup against a situation no user is ever in, and every run "passed" with a
                    // 2-row popup. Park the field ~80px under the top of the viewport, which is where
                    // it lands when someone scrolls down to a section. ScrollTo() is deliberately NOT
                    // used: it scrolls the minimum distance and parks the field at the very BOTTOM
                    // edge, which is the pathological case rather than the normal one.
                    var sv = dd.GetFirstAncestorOfType<ScrollView>();
                    if (sv != null && _phaseFrames < 2)
                    {
                        // Offset from the viewport TOP at which to park the field: a little below it
                        // for the normal case, or a field-height above the BOTTOM to starve it of room.
                        float inset = parkAtBottom ? sv.worldBound.height - dd.worldBound.height - 4f : 80f;
                        float y = dd.worldBound.yMin - sv.worldBound.yMin + sv.scrollOffset.y - inset;
                        sv.scrollOffset = new Vector2(sv.scrollOffset.x, Mathf.Max(0f, y));
                        return;   // let the scroll settle before measuring anything
                    }
                    if (_phaseFrames < 8) return;

                    _phase = 1; _phaseFrames = 0;
                    return;
                }

                case 1:
                    using (var e = NavigationSubmitEvent.GetPooled())
                    {
                        e.target = dd;
                        dd.SendEvent(e);
                    }
                    _phase = 2; _phaseFrames = 0;
                    return;

                case 2:
                    if (menu == null)
                    {
                        if (_phaseFrames > 120) { Fail(tag, "popup never opened"); Next(then); }
                        return;
                    }
                    if (_phaseFrames < 20) return;   // tuner + geometry settle
                    Measure(tag, dd, menu, expectVScroller, expectDown: !parkAtBottom);
                    _phase = 3; _phaseFrames = 0;
                    return;

                case 3:
                    if (menu != null)
                    {
                        var outer = menu.Q(className: "unity-base-dropdown__container-outer");
                        if (outer != null && outer.panel != null)
                            using (var e = KeyDownEvent.GetPooled('\0', KeyCode.Escape, EventModifiers.None))
                            {
                                e.target = outer;
                                outer.SendEvent(e);
                            }
                    }
                    _phase = 4; _phaseFrames = 0;
                    return;

                case 4:
                    if (menu != null)
                    {
                        if (_phaseFrames > 60) menu.RemoveFromHierarchy();   // Escape didn't land; force it
                        else return;
                    }
                    Next(then);
                    return;
            }
        }

        static void Measure(string tag, DropdownField dd, VisualElement menu, bool expectVScroller,
                            bool expectDown)
        {
            var outer = menu.Q(className: "unity-base-dropdown__container-outer");
            var scroll = menu.Q<ScrollView>();
            if (outer == null || scroll == null) { Fail(tag, "popup internals missing"); return; }

            // Everything below is in the OVERLAY's local space — the same pixels `outer.style.top`
            // is written in, and the same pixels `resolvedStyle` reports. Two things forced this:
            //
            //   * `worldBound` is metres inside a world-space panel and pixels inside a screen one,
            //     so a probe that mixed worldBound with resolvedStyle was comparing 0.4 against 42.
            //   * `dd.panel.visualTree.worldBound` — what `p` used to be — is 0x0 in a world panel,
            //     because WorldSpaceSizeMode.Dynamic gives the root no viewport to fill. `room` came
            //     out negative, `wantRows` collapsed to 0, and "enough rows" passed trivially.
            //
            // WorldToLocal normalises all of it, and on a screen panel it is a pure translation, so
            // the screen numbers are unchanged.
            var f = menu.WorldToLocal(dd.worldBound);
            var o = menu.WorldToLocal(outer.worldBound);
            var p = menu.WorldToLocal(RoomBounds(dd, menu));
            float gap = expectDown ? o.yMin - f.yMax    // popup hangs below the field
                                   : f.yMin - o.yMax;   // popup sits above it

            // ROWS is the number the user actually experiences, and the number the old assertions
            // never looked at. A 14-choice picker rendering 2 rows passed every check here — "not
            // tiny" was >= 60px and the 72px floor cleared it — which is how a broken popup shipped
            // green. Score the thing being complained about.
            int choices = dd.choices?.Count ?? 0;
            float chrome = outer.resolvedStyle.paddingTop + outer.resolvedStyle.paddingBottom
                         + outer.resolvedStyle.borderTopWidth + outer.resolvedStyle.borderBottomWidth;
            if (float.IsNaN(chrome) || chrome < 0f) chrome = 10f;

            // MEASURE the row, don't assume it. `min-height: 28px` is a floor, not the height: 6px of
            // padding top and bottom plus a 14px line resolve to ~42px, so counting rows at 28px
            // overstates them by half and would let a too-short popup score as roomy.
            var item = scroll.Q(className: "unity-base-dropdown__item");
            float rowH = item != null && item.resolvedStyle.height > 1f ? item.resolvedStyle.height : 28f;
            int rows = Mathf.FloorToInt((o.height - chrome) / rowH);

            // The popup owes the user every row that the LIST has, the ROOM allows, and the CAP
            // permits — whichever runs out first. Stating it that way keeps the check honest in a
            // short panel (this editor Game view is only ~431px, so a 14-item list genuinely cannot
            // show 8 rows below the field) while still catching the bug that started this: 14 choices,
            // ample room, and a 2-row popup, which the old `height >= 60px` check waved through.
            float room = (expectDown ? p.yMax - f.yMax : f.yMin - p.yMin) - 4f - 8f;   // GAP + EDGE_MARGIN
            int roomRows = Mathf.Max(0, Mathf.FloorToInt(room / rowH));
            int wantRows = Mathf.Min(Mathf.Min(choices, roomRows), 8);

            bool placed  = gap >= -0.5f;            // on the side we expected, and...
            bool snug    = gap <= 12f;              //   ...tucked against the field, not floating
            bool capped  = o.height <= 362f;
            bool enough  = rows >= wantRows;
            bool inPanel = o.yMin >= p.yMin - 1f && o.yMax <= p.yMax + 1f;   // no floor escape hatch

            // NOT "the popup is exactly the field's width" — that assertion is what let "Medium"
            // render as "Me.." in every narrow dropdown and still score green. A popup pinned to a
            // 110px field cannot show its own items. The contract is a native <select>'s: at least the
            // field, free to grow to fit the longest item, never past the container's right edge.
            bool widthOk = o.width >= f.width - 2f && o.width <= p.width + 2f;

            // And the point of the width: does any item actually FIT? Compare each label's natural
            // text width against the width it was given. This is the assertion the user's bug report
            // was, and nothing here was making it.
            int truncated = 0;
            string worst = "";
            foreach (var lbl in scroll.Query<TextElement>().ToList())
            {
                if (string.IsNullOrEmpty(lbl.text)) continue;
                float natural = lbl.MeasureTextSize(lbl.text, 0, VisualElement.MeasureMode.Undefined,
                                                    0, VisualElement.MeasureMode.Undefined).x;
                float given = lbl.resolvedStyle.width;
                if (float.IsNaN(natural) || float.IsNaN(given) || given < 1f) continue;
                if (natural > given + 1f) { truncated++; if (worst.Length == 0) worst = lbl.text; }
            }
            bool textFits = truncated == 0;

            // The popup is `opacity: 0` from birth until the tuner reveals it, which is what kills the
            // flash of Unity's un-tuned popup. The failure mode of that trick is a dropdown that never
            // comes back — so assert it did. Nothing else here would notice: an invisible popup has
            // perfectly good geometry.
            bool visible = outer.resolvedStyle.opacity >= 0.99f;

            bool hHidden = scroll.horizontalScroller.resolvedStyle.display == DisplayStyle.None;
            bool vOk     = !expectVScroller ||
                           scroll.verticalScroller.resolvedStyle.display == DisplayStyle.Flex;

            // Does the popup wear the page's palette? Compare what it actually PAINTED against what the
            // document root resolves --color-surface-elev to. If the theme reached panel scope, both are
            // the palette's colour. If it did not, the page moved and the popup stayed on the base dark
            // value — and the two diverge. Also guard the guard: if the page itself is still on the base
            // palette then no theme was applied at all and this proves nothing, so say so.
            bool themedOk = true;
            string themeNote = "";
            if (_expectThemed)
            {
                Color pageElev = _flatRoot.customStyle.TryGetValue(SURFACE_ELEV, out var pe) ? pe : BASE_SURFACE_ELEV;
                Color popupBg = outer.resolvedStyle.backgroundColor;
                bool pageMoved = !Near(pageElev, BASE_SURFACE_ELEV);
                bool popupMatches = Near(popupBg, pageElev);
                themedOk = pageMoved && popupMatches;
                themeNote = $" pageElev={Hex(pageElev)} popupBg={Hex(popupBg)} " +
                            $"pageMoved={pageMoved} popupMatchesPage={popupMatches}";
            }
            // Randomize has no token to resolve — the palette exists only as an object, and the popup
            // is only ever inline-stamped from it. So ask the palette directly. (Guard the guard: a
            // random palette that happened to land on the base colour would make this prove nothing.)
            else if (_expectRandom)
            {
                Color want = RandomSurfaceElev();
                Color popupBg = outer.resolvedStyle.backgroundColor;
                bool paletteLive = !Near(want, BASE_SURFACE_ELEV);
                bool popupMatches = Near(popupBg, want);
                themedOk = paletteLive && popupMatches;
                themeNote = $" randomElev={Hex(want)} popupBg={Hex(popupBg)} " +
                            $"paletteLive={paletteLive} popupMatchesPalette={popupMatches}";
            }

            bool pass = placed && snug && capped && enough && inPanel && widthOk && textFits
                        && visible && hHidden && vOk && themedOk;
            if (!pass) _failures++;

            _log.Add($"[PopupProbe] {(pass ? "PASS" : "FAIL")} {tag}: " +
                     $"field=({f.xMin:F0},{f.yMin:F0} {f.width:F0}x{f.height:F0}) " +
                     $"popup=({o.xMin:F0},{o.yMin:F0} {o.width:F0}x{o.height:F0}) " +
                     $"opens={(expectDown ? "DOWN" : "UP")} gap={gap:F1} " +
                     $"panelH={p.height:F0} choices={choices} rowH={rowH:F0} rows={rows}/{wantRows} | " +
                     $"placed={placed} snug={snug} capped={capped} enoughRows={enough} " +
                     $"inPanel={inPanel} widthOk={widthOk} " +
                     $"textFits={textFits}{(textFits ? "" : $"(cut {truncated}, e.g. '{worst}')")} " +
                     $"visible={visible}(op={outer.resolvedStyle.opacity:F2}) " +
                     $"hHidden={hHidden} vOk={vOk}" + themeNote);
        }

        // The outermost ancestor of the field that actually has a size — the same rectangle the tuner
        // bounds the popup to. On a screen panel that IS the panel root. On a world-space panel the
        // panel root is 0x0 and this walks past it to the exhibit's content box. Returned in WORLD
        // units; the caller converts.
        static Rect RoomBounds(VisualElement el, VisualElement overlay)
        {
            var best = overlay.worldBound;
            if (!float.IsNaN(best.height) && best.height > 0.001f) return best;   // screen: the overlay is the panel

            best = new Rect();
            for (var p = el.parent; p != null; p = p.parent)
            {
                var wb = p.worldBound;
                if (float.IsNaN(wb.height) || wb.height <= 0.001f) continue;   // the degenerate panel root
                best = wb;
            }
            return best;
        }

        // Reach into the bootstrap for the live Randomize palette. Private on purpose (nothing outside
        // the showcase should steer it), and a probe reading it back is exactly what reflection is for.
        static Color RandomSurfaceElev()
        {
            var field = typeof(ShowcaseBootstrap).GetField(
                "_activeOverride", BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) is CodigrateThemeApplier.ColorMap map
                ? map.SurfaceElev
                : BASE_SURFACE_ELEV;
        }

        static bool Near(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static DropdownField FindByFirstChoice(VisualElement root, string first)
        {
            if (root == null) return null;
            DropdownField hit = null;
            root.Query<DropdownField>(className: "ds-dropdown").ForEach(d =>
            {
                if (hit == null && d.choices != null && d.choices.Count > 0 && d.choices[0] == first)
                    hit = d;
            });
            return hit;
        }

        static DropdownField FindWorld(System.Func<VisualElement, DropdownField> pick)
        {
            foreach (var root in _corridor.ExhibitRoots)
            {
                var dd = pick(root);
                if (dd != null) return dd;
            }
            return null;
        }

        static Button FindButtonByText(VisualElement root, string text)
        {
            if (root == null) return null;
            Button hit = null;
            root.Query<Button>().ForEach(b =>
            {
                if (hit == null && b.text == text) hit = b;
            });
            return hit;
        }

        static void Next(Step s) { _step = s; _phase = 0; _phaseFrames = 0; }

        static void Fail(string tag, string why)
        {
            _failures++;
            _log.Add($"[PopupProbe] FAIL {tag}: {why}");
        }

        static void Done()
        {
            SessionState.SetBool(FLAG, false);
            SessionState.SetBool(SCREEN_FLAG, false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }
    }
}
#endif
