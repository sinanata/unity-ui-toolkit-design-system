#if UNITY_6000_5_OR_NEWER
using System.Collections.Generic;
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
    // bottom inside the panel. Run:
    //   Unity -executeMethod UIDocumentDesignSystem.BuildTools.PopupProbe.Run
    // (no -quit: the probe exits the editor itself when done)
    //
    // Environment caveat, learned the hard way: the SCREEN tests are reliable
    // anywhere, but the WORLD tests only produce real numbers when the Game
    // view genuinely renders. In an editor launched from a script into the
    // background, PanelRenderer panels never resolve a layout (fields measure
    // ~2x0) even with the GameView acquired, focused and Repaint()-pumped
    // every tick — same root cause as the -batchmode limitation. Expect
    // "field never laid out" world FAILs in that setup; verify world-space
    // popups in a WebGL build (or an editor you're actually looking at)
    // instead. The tuner math itself is shared and covered by the screen
    // tests, which exercise the small-panel clamp path (field below the
    // fold -> floor cap + forced downward placement).
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
            WaitFlat, ScreenInputs, ScreenStuffed, EnterWorld,
            WorldColors, WorldInputs, WorldSort, Report
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
                    Next(Step.ScreenInputs);
                    return;
                }

                case Step.ScreenInputs:
                    if (_screenDd == null) _screenDd = FindByFirstChoice(_flatRoot, "Common");
                    RunDropdownTest("SCREEN inputs", _screenDd,
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
                    RunDropdownTest("SCREEN stuffed(40)", _screenDd, expectVScroller: true, then: Step.EnterWorld);
                    return;
                }

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

        // Sub-phases: 0 = send open event, 1 = wait + measure, 2 = close, 3 = wait closed.
        static void RunDropdownTest(string tag, DropdownField dd, bool expectVScroller, Step then)
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
                    // Never open against an un-laid-out panel: world exhibits
                    // resolve their layout only once the camera renders them
                    // (and the fit loop settles), which takes a while.
                    var wb = dd.worldBound;
                    if (float.IsNaN(wb.width) || float.IsNaN(wb.height) || wb.width < 10f || wb.height < 10f)
                    {
                        if (_phaseFrames > 1500)
                        {
                            Fail(tag, $"field never laid out ({wb.width:F1}x{wb.height:F1})");
                            Next(then);
                        }
                        return;
                    }
                    using (var e = NavigationSubmitEvent.GetPooled())
                    {
                        e.target = dd;
                        dd.SendEvent(e);
                    }
                    _phase = 1; _phaseFrames = 0;
                    return;
                }

                case 1:
                    if (menu == null)
                    {
                        if (_phaseFrames > 120) { Fail(tag, "popup never opened"); Next(then); }
                        return;
                    }
                    if (_phaseFrames < 20) return;   // tuner + geometry settle
                    Measure(tag, dd, menu, expectVScroller);
                    _phase = 2; _phaseFrames = 0;
                    return;

                case 2:
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
                    _phase = 3; _phaseFrames = 0;
                    return;

                case 3:
                    if (menu != null)
                    {
                        if (_phaseFrames > 60) menu.RemoveFromHierarchy();   // Escape didn't land; force it
                        else return;
                    }
                    Next(then);
                    return;
            }
        }

        static void Measure(string tag, DropdownField dd, VisualElement menu, bool expectVScroller)
        {
            var outer = menu.Q(className: "unity-base-dropdown__container-outer");
            var scroll = menu.Q<ScrollView>();
            var panelRoot = dd.panel.visualTree;
            if (outer == null || scroll == null) { Fail(tag, "popup internals missing"); return; }

            var f = dd.worldBound;
            var o = outer.worldBound;
            float gap = o.yMin - f.yMax;

            bool below   = gap >= -0.5f;
            bool snug    = gap <= 12f;
            bool capped  = o.height <= 322f;
            bool notTiny = o.height >= 60f;
            // A popup at the 72px floor may legitimately poke past the panel
            // edge when the field sits at the very bottom — don't flag that.
            bool inPanel = o.yMax <= panelRoot.worldBound.yMax + 1f || o.height <= 74f;
            bool widthOk = Mathf.Abs(o.width - f.width) <= 2f;
            bool hHidden = scroll.horizontalScroller.resolvedStyle.display == DisplayStyle.None;
            bool vOk     = !expectVScroller ||
                           scroll.verticalScroller.resolvedStyle.display == DisplayStyle.Flex;

            bool pass = below && snug && capped && notTiny && inPanel && widthOk && hHidden && vOk;
            if (!pass) _failures++;

            _log.Add($"[PopupProbe] {(pass ? "PASS" : "FAIL")} {tag}: " +
                     $"field=({f.xMin:F0},{f.yMin:F0} {f.width:F0}x{f.height:F0}) " +
                     $"popup=({o.xMin:F0},{o.yMin:F0} {o.width:F0}x{o.height:F0}) gap={gap:F1} " +
                     $"panelH={panelRoot.worldBound.height:F0} choices={dd.choices?.Count} | " +
                     $"below={below} snug={snug} capped={capped} notTiny={notTiny} " +
                     $"inPanel={inPanel} widthOk={widthOk} hHidden={hHidden} vOk={vOk}");
        }

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
            EditorApplication.update -= Tick;
            EditorApplication.Exit(_failures == 0 ? 0 : 1);
        }
    }
}
#endif
