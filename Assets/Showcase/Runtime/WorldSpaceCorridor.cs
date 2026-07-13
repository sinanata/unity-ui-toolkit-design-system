// World-space "gallery corridor" for the design-system showcase.
//
// Screen-space mode (the classic flat page) and this mode are the two halves
// of the big toggle at the top of the showcase. In world mode every
// `.ds-section` from the showcase UXML becomes its own UI Toolkit panel
// mounted on the wall of a corridor, alternating left / right as you walk
// forward. You can walk up to any exhibit and click its controls — the panels
// are live UI, not screenshots.
//
// Controls: WASD / arrows + right-mouse or Q/E look on desktop; on touch
// devices the HUD (ShowcaseModeHud) shows two virtual sticks that write into
// WorldNavInput, which the FirstPersonController reads. Exhibit taps and
// camera motion can never conflict — see WorldNavInput for the contract.
//
// Requires Unity 6000.5+ (PanelRenderer + world-space PanelSettings). The whole
// file compiles out on older editors so the package still imports there; the
// screen-space showcase is unaffected and the world toggle simply hides.
#if UNITY_6000_5_OR_NEWER
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;
// The ds-* runtime helpers (toggle-knob / skeleton / draggable injection) live
// as public statics on the generic base; reach them through the concrete
// UIDocument subclass, which inherits them.
using DsBehaviour = DesignSystem.Runtime.Behaviour.UIDocument.DesignSystemBehaviour;

namespace Showcase.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldSpaceCorridor : MonoBehaviour
    {
        // ── Corridor geometry (metres) ──────────────────────────────────────
        const float CORRIDOR_WIDTH   = 7.2f;
        const float CORRIDOR_HEIGHT  = 4.6f;
        const float WALL_THICK       = 0.25f;
        const float PANEL_SPACING    = 4.0f;   // z-step between alternating exhibits
        const float FIRST_PANEL_Z    = 7.5f;
        const float PANEL_EYE_Y      = 2.05f;  // vertical centre of an exhibit
        const float PANEL_INSET      = 0.28f;  // gap from the wall face
        const float PANEL_YAW_TOWARD = 14f;    // degrees each exhibit angles toward the entrance

        // ── Exhibit fit ─────────────────────────────────────────────────────
        // Each exhibit is scaled by the SMALLER of the height/width ratios so it
        // always fits inside this box on the wall — nothing overflows, and tall
        // sections just shrink to show all their content ("dynamic + usable").
        const float TARGET_PANEL_HEIGHT = 2.5f;    // world height a fitted exhibit fits within
        const float MAX_PANEL_WIDTH     = 2.5f;
        const float PREFIT_SCALE        = 0.0001f; // sub-mm: invisible AND unpickable until measured
        const float FIT_TIMEOUT         = 20f;     // seconds of visible time before giving up on a panel
        const float FIT_STABLE_FOR      = 0.15f;   // a measurement must hold this long to count as settled
        const float SPIN_DEG_PER_SEC    = 360f;    // matches DesignSystemBehaviourBase's 6° / 16 ms tick

        // Palette (linear-ish sRGB values matching DesignTokens.uss).
        static readonly Color C_CLEAR    = new Color(0.020f, 0.028f, 0.045f); // camera/fog background (near-black)
        static readonly Color C_FLOOR    = new Color(0.075f, 0.090f, 0.130f);
        static readonly Color C_WALL     = new Color(0.135f, 0.165f, 0.215f);
        static readonly Color C_CEIL     = new Color(0.050f, 0.065f, 0.100f);
        static readonly Color C_PLATE    = new Color(0.205f, 0.245f, 0.315f); // exhibit mounting slab, a step above C_WALL
        static readonly Color C_TRIM     = new Color(0.133f, 0.773f, 0.369f); // --color-primary
        static readonly Color C_ACCENT2  = new Color(0.231f, 0.510f, 0.965f); // --color-secondary

        // Injected by Create().
        UIDocument _showcaseDoc;
        UIDocument _overlayDoc;
        VisualTreeAsset _showcaseUxml;
        ThemeStyleSheet _theme;
        StyleSheet _dsUss;
        StyleSheet _themeOverrideUss;
        StyleSheet _focusRingUss;   // loaded lazily in EnsureBuilt

        // Built lazily on first entry.
        GameObject _content;            // parent of everything the corridor owns
        GameObject _geometry;           // shell boxes + lamps + plates — the SetActive-toggled half
        bool _built;
        Camera _cam;
        FirstPersonController _player;
        readonly List<PanelBinding> _panels = new List<PanelBinding>();
        Material _baseMaterial;
        Material _plateMat;
        float _spinAngle;      // drives .is-spinning rotation across all world panels
        float _spinRescanIn;   // countdown to the next .is-spinning re-query

        // Theme state mirrored from the flat page so the exhibits match whatever
        // day/night / codigrate / random palette the user has chosen.
        //
        // Only the day/night MOOD lives here. The palette itself — a baked ThemeData painted
        // through the USS cascade, or a runtime-generated ColorMap stamped inline — belongs to
        // ShowcaseBootstrap, and its PaintRoot applies whichever is current. So an exhibit rebuilt
        // on a later reload picks up the live theme without the corridor mirroring a second copy
        // of it and getting to disagree.
        bool _themeLight;

        // Cached scene state we restore when leaving world mode.
        bool _isShown;
        Vector3 _camPos0; Quaternion _camRot0;
        CameraClearFlags _camClear0; Color _camBg0; float _camFov0;
        bool _fog0; Color _fogColor0; FogMode _fogMode0; float _fogStart0, _fogEnd0;
        Color _ambient0; UnityEngine.Rendering.AmbientMode _ambientMode0;
        Material _skybox0; bool _skyboxCached;

        public bool IsShown => _isShown;

        // Raised at the end of Hide() (including the Esc-driven exit) so the
        // mode switch can un-highlight "World Space" without us reaching into
        // the HUD directly.
        public event System.Action Hidden;

        // Invoked once per exhibit when its panel first comes online, with the
        // panel's root element. The bootstrap uses it to wire the exhibit's
        // CLONED interactive controls (theme toggle / provider / drawers) —
        // the corridor itself stays wiring-agnostic.
        public System.Action<VisualElement> PanelReady;

        // Live panel roots of every exhibit (for cross-panel state mirroring,
        // e.g. keeping the cloned theme controls in agreement).
        public IEnumerable<VisualElement> ExhibitRoots
        {
            get
            {
                foreach (var p in _panels)
                {
                    var r = p?.Root;
                    if (r != null) yield return r;
                }
            }
        }

        // ── Construction ────────────────────────────────────────────────────

        public static WorldSpaceCorridor Create(
            UIDocument showcaseDoc, UIDocument overlayDoc,
            VisualTreeAsset showcaseUxml, ThemeStyleSheet theme,
            StyleSheet dsUss, StyleSheet themeOverrideUss)
        {
            var go = new GameObject("WorldSpaceCorridor");
            var c = go.AddComponent<WorldSpaceCorridor>();
            c._showcaseDoc = showcaseDoc;
            c._overlayDoc = overlayDoc;
            c._showcaseUxml = showcaseUxml;
            c._theme = theme;
            c._dsUss = dsUss;
            c._themeOverrideUss = themeOverrideUss;
            return c;
        }

        // ── Show / hide (the mode switch) ───────────────────────────────────

        public void Show()
        {
            if (_isShown) return;
            _isShown = true;

            EnsureBuilt();

            // Stop the flat showcase from painting over the 3D view. We keep its
            // UIDocument enabled (so all the runtime wiring survives) but hide
            // the tree and turn off its opaque clear so the camera's corridor
            // render shows through.
            SetScreenShowcaseVisible(false);

            CacheAndPrepareScene();
            SetCorridorVisible(true);

            WorldNavInput.Reset();
            if (_player != null)
            {
                _player.enabled = true;
                _player.SyncFromTransform();
            }
        }

        public void Hide()
        {
            if (!_isShown) return;
            _isShown = false;

            SetCorridorVisible(false);
            if (_player != null) _player.enabled = false;
            WorldNavInput.Reset();

            RestoreScene();
            SetScreenShowcaseVisible(true);

            Hidden?.Invoke();
        }

        // The exhibits' PanelRenderers are NEVER SetActive-toggled: disabling
        // one tears its panel down, and the rebuilt panel resolves a 0×0 size
        // for a moment on re-enable. Deep in engine layout that zero reaches
        // an integer division, and on WebGL an i32 divide-by-zero is a hard,
        // uncatchable wasm trap — the "RuntimeError: divide by zero" that
        // killed the player after a few Screen/World toggles.
        // (PlayerSettings.WebGL.wasmArithmeticExceptions = Ignore does NOT
        // cover it: that flag only relaxes float→int conversion traps.)
        // So the 3D geometry toggles normally, while the panels toggle
        // VISIBILITY — which skips painting and picking but keeps their
        // layout alive and their size non-zero for the whole session.
        void SetCorridorVisible(bool on)
        {
            if (_geometry != null) _geometry.SetActive(on);
            foreach (var p in _panels)
            {
                var root = p?.Root;
                if (root != null)
                    root.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
        }

        // The design-system spinner is C#-driven (a scheduled rotate on the
        // UIDocument root). World exhibits are PanelRenderers, not UIDocuments,
        // so nothing ticks them — drive .is-spinning here instead while the
        // corridor is on screen. Time-based so the rate matches the base
        // runtime's 6°/16ms schedule at any frame rate. The matching elements
        // are cached per panel and re-queried once a second (30 live panels ×
        // a class Query every frame is measurable on WebGL; SetSpinning class
        // flips are rare, so a 1 s stale window is invisible).
        void Update()
        {
            if (!_isShown || _panels.Count == 0) return;

            _spinAngle = (_spinAngle + SPIN_DEG_PER_SEC * Time.deltaTime) % 360f;
            _spinRescanIn -= Time.deltaTime;
            bool rescan = _spinRescanIn <= 0f;
            if (rescan) _spinRescanIn = 1f;

            var rot = new StyleRotate(new Rotate(new Angle(_spinAngle, AngleUnit.Degree)));
            foreach (var p in _panels)
            {
                var root = p?.Root;
                if (root == null) continue;
                if (rescan || p.Spinners == null)
                    p.Spinners = root.Query(className: "is-spinning").ToList();
                for (int i = 0; i < p.Spinners.Count; i++)
                    p.Spinners[i].style.rotate = rot;
            }
        }

        // ── Theme sync (from the flat page) ─────────────────────────────────

        // Called by the bootstrap whenever the flat page's theme changes: the
        // day/night toggle, a codigrate palette, randomize, or revert. Stores the
        // state and fans it out to every exhibit; OnReload also re-applies it so
        // panels rebuilt on a later mode-toggle come back themed.
        public void ApplyScreenTheme(bool light)
        {
            _themeLight = light;
            foreach (var p in _panels) ApplyThemeToPanel(p);
        }

        void ApplyThemeToPanel(PanelBinding p)
        {
            var root = p?.Root;
            if (root == null) return;

            // Paint FIRST. A class-scoped theme (every light-appearance codigrate palette bakes to
            // `.theme-light`) adds and removes that very class as it goes on and off, so the
            // day/night class has to be re-asserted AFTER the sheets have settled or a swap would
            // leave the mood inverted.
            ShowcaseBootstrap.PaintRoot(root);

            if (_themeLight) root.AddToClassList("theme-light");
            else             root.RemoveFromClassList("theme-light");

            // Mirror the mood onto the panel's visualTree too. PaintRoot has already put the theme's
            // SHEET there (that is how the dropdown popup gets themed at all), but a class-scoped
            // theme gates on a class, and the day/night class is the showcase's own state rather than
            // the theme's — so it has to be asserted here, at the same scope, or a `.theme-light`
            // palette would land its tokens on the panel root with nothing to switch them on.
            var panelScope = root.panel?.visualTree;
            if (panelScope != null && panelScope != root)
            {
                if (_themeLight) panelScope.AddToClassList("theme-light");
                else             panelScope.RemoveFromClassList("theme-light");
            }

            // The INLINE path stamps the palette's bg onto the root, and no class (not even
            // `ds-root--hud`) outranks an inline style — so a world exhibit, whose backdrop is the
            // 3D plate behind it, has to re-assert transparency as the last write. The cascade path
            // never writes an inline bg at all, so this is simply a no-op there.
            root.style.backgroundColor = Color.clear;
        }

        void SetScreenShowcaseVisible(bool visible)
        {
            if (_showcaseDoc != null)
            {
                if (_showcaseDoc.panelSettings != null)
                    _showcaseDoc.panelSettings.clearColor = visible;
                var r = _showcaseDoc.rootVisualElement;
                if (r != null) r.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (_overlayDoc != null)
            {
                var r = _overlayDoc.rootVisualElement;
                if (r != null) r.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // Tell the bootstrap, so it can stop repainting a page nobody can see. `display: none`
            // takes an element out of layout and rendering, but NOT out of style resolution, so the
            // flat page kept paying full price for every theme change made while the corridor was up.
            // Last, and after the display flip: on the way back in this flushes any theme change that
            // landed while the page was hidden, and it should flush onto a page that is already
            // visible again.
            ShowcaseBootstrap.SetFlatPageVisible(visible);
        }

        void CacheAndPrepareScene()
        {
            if (_cam == null) return;

            _camPos0 = _cam.transform.position;
            _camRot0 = _cam.transform.rotation;
            _camClear0 = _cam.clearFlags;
            _camBg0 = _cam.backgroundColor;
            _camFov0 = _cam.fieldOfView;

            _fog0 = RenderSettings.fog;
            _fogColor0 = RenderSettings.fogColor;
            _fogMode0 = RenderSettings.fogMode;
            _fogStart0 = RenderSettings.fogStartDistance;
            _fogEnd0 = RenderSettings.fogEndDistance;
            _ambient0 = RenderSettings.ambientLight;
            _ambientMode0 = RenderSettings.ambientMode;

            // Enclosed corridor look: solid near-black clear + distance fog so
            // the far end fades to black. The Showcase scene itself ships with
            // no skybox and a SolidColor camera (a stripped skybox variant in a
            // WebGL build renders as the magenta error shader), but a consumer
            // scene might still have one — null it while in world mode and
            // restore on exit.
            _skybox0 = RenderSettings.skybox; _skyboxCached = true;
            RenderSettings.skybox = null;

            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = C_CLEAR;
            // 66° vertical suits landscape; portrait phones get more, because
            // at 9:16 a 66° vertical frustum is only ~40° horizontal and the
            // corridor turns claustrophobic.
            _cam.fieldOfView = Screen.height > Screen.width ? 78f : 66f;

            RenderSettings.fog = true;
            RenderSettings.fogColor = C_CLEAR;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 10f;
            RenderSettings.fogEndDistance = 34f;
            // Corridor surfaces are URP/Lit: flat ambient gives them a sane
            // base level (the scene has no skybox to derive a probe from);
            // the lamp row does the shaping on top.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.27f, 0.34f);

            // Drop the player at the entrance, looking down the corridor.
            _cam.transform.position = new Vector3(0f, _player != null ? _player.EyeHeight : PANEL_EYE_Y, 0f);
            _cam.transform.rotation = Quaternion.identity;
        }

        void RestoreScene()
        {
            if (_cam == null) return;
            _cam.transform.position = _camPos0;
            _cam.transform.rotation = _camRot0;
            _cam.clearFlags = _camClear0;
            _cam.backgroundColor = _camBg0;
            _cam.fieldOfView = _camFov0;

            RenderSettings.fog = _fog0;
            RenderSettings.fogColor = _fogColor0;
            RenderSettings.fogMode = _fogMode0;
            RenderSettings.fogStartDistance = _fogStart0;
            RenderSettings.fogEndDistance = _fogEnd0;
            RenderSettings.ambientLight = _ambient0;
            RenderSettings.ambientMode = _ambientMode0;

            if (_skyboxCached) { RenderSettings.skybox = _skybox0; _skyboxCached = false; }
        }

        // ── Build ───────────────────────────────────────────────────────────

        void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            _baseMaterial = ResolveBaseMaterial();
            _plateMat = MakeMat(C_PLATE, 0.2f);
            _focusRingUss = Resources.Load<StyleSheet>("ShowcaseFocusRing");
            _cam = Camera.main;
            if (_cam == null)
            {
                Debug.LogError("[WorldSpaceCorridor] No Camera.main — cannot build world mode.");
                return;
            }

            // The walker + the world-space raycaster live on the camera.
            _player = _cam.gameObject.GetComponent<FirstPersonController>();
            if (_player == null) _player = _cam.gameObject.AddComponent<FirstPersonController>();
            _player.enabled = false;
            _player.ExitRequested += Hide;
            // Typing into an exhibit's TextField must not walk the camera —
            // WASD/QE are letters first, movement keys second.
            _player.SuppressKeys = AnyTextInputFocused;

            var sections = HarvestSections();

            _content = new GameObject("CorridorContent");
            _content.transform.SetParent(transform, false);

            // The SetActive-toggled half (see SetCorridorVisible): everything
            // 3D that is NOT a panel. Panel anchors live directly under
            // _content and stay active for the whole session.
            _geometry = new GameObject("Geometry");
            _geometry.transform.SetParent(_content.transform, false);

            float lastZ = FIRST_PANEL_Z + Mathf.Max(0, sections.Count - 1) * PANEL_SPACING;
            float endZ = lastZ + PANEL_SPACING + 2f;

            BuildShell(endZ);
            BuildLighting(endZ);

            // Confine the walker to the interior.
            float halfW = CORRIDOR_WIDTH * 0.5f - 0.7f;
            _player.BoundsX = new Vector2(-halfW, halfW);
            _player.BoundsZ = new Vector2(-1.5f, endZ - 1.5f);

            BuildEntranceTitle(sections.Count);
            BuildExhibits(sections, HarvestSectionWidths(sections.Count));
            AddWorldRaycaster();

            StartCoroutine(FitExhibits());
            // No SetActive(false) here: EnsureBuilt only runs from Show(), and
            // the panels must stay alive from now on (see SetCorridorVisible).
        }

        // Clone the showcase tree once and lift every top-level section out of
        // it. Harvested elements are detached but fully intact — each carries
        // its own inline widths and ds-* classes; re-applying DesignSystem.uss
        // to the destination panel restores all component styling + icons.
        List<VisualElement> HarvestSections()
        {
            var result = new List<VisualElement>();
            if (_showcaseUxml == null) return result;

            var tree = _showcaseUxml.Instantiate();
            var sections = tree.Query<VisualElement>(className: "ds-section").ToList();
            foreach (var s in sections)
            {
                s.RemoveFromHierarchy();
                result.Add(s);
            }
            return result;
        }

        // Each exhibit must be pinned to its section's REAL width. The UXML
        // authors those as inline `style="width: 490px"` attributes — but
        // UXML-authored inline styles live in a per-element inline stylesheet
        // that the C# `style.width` getter does NOT surface (it only reflects
        // styles set from code), so reading the harvested clones always fell
        // back to a flat 360 and every non-360 section rendered squeezed or
        // stretched (the "buttons overflow their container" report: BUTTONS
        // is authored 490 wide). Ground truth that needs no UXML parsing: the
        // LIVE flat page has the same sections already laid out — same
        // class query, same document order — so index-match their resolved
        // widths.
        List<float> HarvestSectionWidths(int count)
        {
            var widths = new List<float>(count);
            List<VisualElement> flat = null;
            var flatRoot = _showcaseDoc != null ? _showcaseDoc.rootVisualElement : null;
            if (flatRoot != null)
                flat = flatRoot.Query<VisualElement>(className: "ds-section").ToList();

            for (int i = 0; i < count; i++)
            {
                float w = 0f;
                if (flat != null && i < flat.Count)
                    w = flat[i].resolvedStyle.width;
                widths.Add(w > 20f && !float.IsNaN(w) ? Mathf.Round(w) : 360f);
            }
            return widths;
        }

        // True while any world exhibit's text input has keyboard focus.
        bool AnyTextInputFocused()
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                var root = _panels[i]?.Root;
                var focused = root?.panel?.focusController?.focusedElement as VisualElement;
                if (focused == null) continue;
                if (focused is TextField || focused.GetFirstAncestorOfType<TextField>() != null)
                    return true;
            }
            return false;
        }

        // ── Corridor shell ──────────────────────────────────────────────────

        void BuildShell(float endZ)
        {
            float midZ = (endZ - 3f) * 0.5f;
            float len = endZ + 3f;

            var floorMat = MakeMat(C_FLOOR, 0.15f);
            var wallMat  = MakeMat(C_WALL, 0.05f);
            var ceilMat  = MakeMat(C_CEIL, 0.0f);

            AddBoxRow("Floor",   new Vector3(0f, -0.1f, midZ),                 new Vector3(CORRIDOR_WIDTH, 0.2f, len), floorMat);
            AddBoxRow("Ceiling", new Vector3(0f, CORRIDOR_HEIGHT + 0.1f, midZ), new Vector3(CORRIDOR_WIDTH, 0.2f, len), ceilMat);
            AddBoxRow("WallL", new Vector3(-CORRIDOR_WIDTH * 0.5f, CORRIDOR_HEIGHT * 0.5f, midZ), new Vector3(WALL_THICK, CORRIDOR_HEIGHT, len), wallMat);
            AddBoxRow("WallR", new Vector3( CORRIDOR_WIDTH * 0.5f, CORRIDOR_HEIGHT * 0.5f, midZ), new Vector3(WALL_THICK, CORRIDOR_HEIGHT, len), wallMat);
            AddBox("CapFar",  new Vector3(0f, CORRIDOR_HEIGHT * 0.5f, endZ),  new Vector3(CORRIDOR_WIDTH, CORRIDOR_HEIGHT, 0.25f), wallMat);
            AddBox("CapNear", new Vector3(0f, CORRIDOR_HEIGHT * 0.5f, -3f),   new Vector3(CORRIDOR_WIDTH, CORRIDOR_HEIGHT, 0.25f), wallMat);

            // Emissive guide strips: green at floor level, blue at the ceiling.
            var trimMat   = MakeEmissive(C_TRIM, 2.4f);
            var accentMat = MakeEmissive(C_ACCENT2, 1.6f);
            float x = CORRIDOR_WIDTH * 0.5f - WALL_THICK * 0.5f - 0.02f;
            AddBoxRow("TrimL", new Vector3(-x, 0.14f, midZ), new Vector3(0.06f, 0.06f, len), trimMat);
            AddBoxRow("TrimR", new Vector3( x, 0.14f, midZ), new Vector3(0.06f, 0.06f, len), trimMat);
            AddBoxRow("CeilStrip", new Vector3(0f, CORRIDOR_HEIGHT - 0.02f, midZ), new Vector3(0.16f, 0.04f, len), accentMat);
        }

        // Forward rendering evaluates a fixed number of additional lights PER
        // OBJECT (m_AdditionalLightsPerObjectLimit, 8 here). A single 120 m
        // wall mesh would pick 8 lamps for its WHOLE length and leave the far
        // corridor unlit — so every long shell piece is built as a row of
        // segments, each short enough to grab its own nearest lamps. The
        // handful of extra draw calls is nothing (SRP batcher, shared
        // materials).
        void AddBoxRow(string name, Vector3 center, Vector3 size, Material mat)
        {
            const float SEG = 12.8f;   // ≈ two lamp intervals per segment
            int count = Mathf.Max(1, Mathf.CeilToInt(size.z / SEG));
            float segLen = size.z / count;
            float z0 = center.z - size.z * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var c = new Vector3(center.x, center.y, z0 + (i + 0.5f) * segLen);
                AddBox(count == 1 ? name : $"{name}_{i}", c, new Vector3(size.x, size.y, segLen), mat);
            }
        }

        void BuildLighting(float endZ)
        {
            // Overhead point lights spaced down the hall — the corridor
            // surfaces are URP/Lit, so these do the actual illumination
            // (Forward+ clusters handle a whole lamp row on the long wall
            // meshes). The emissive strips + flat ambient keep the corridor
            // readable even where a browser falls back to a lower light
            // budget.
            int lights = Mathf.CeilToInt((endZ + 3f) / (PANEL_SPACING * 1.6f)) + 1;
            for (int i = 0; i < lights; i++)
            {
                float z = i * (PANEL_SPACING * 1.6f);
                var go = new GameObject($"Lamp_{i}");
                go.transform.SetParent(_geometry.transform, false);
                go.transform.position = new Vector3(0f, CORRIDOR_HEIGHT - 0.3f, z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = PANEL_SPACING * 2.4f;
                l.intensity = 2.6f;
                l.color = (i % 2 == 0)
                    ? new Color(1f, 0.96f, 0.90f)   // warm
                    : new Color(0.86f, 0.92f, 1f);  // cool
                l.shadows = LightShadows.None;      // 30+ shadow casters would tank WebGL
            }
        }

        // ── Exhibits (one PanelRenderer per section) ────────────────────────

        void BuildExhibits(List<VisualElement> sections, List<float> widths)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                bool left = (i % 2 == 0);
                float z = FIRST_PANEL_Z + i * PANEL_SPACING;
                float wallX = CORRIDOR_WIDTH * 0.5f - WALL_THICK - PANEL_INSET;
                float x = left ? -wallX : wallX;

                // The readable face of a world PanelRenderer is its local -Z, so
                // point +Z AWAY from the walkway (LookRotation(-readable)); doing
                // the naive LookRotation(readable) showed every panel's mirrored
                // back. `readable` points into the corridor and tilts gently
                // toward the entrance so exhibits are legible on the approach.
                float tan = Mathf.Tan(PANEL_YAW_TOWARD * Mathf.Deg2Rad);
                Vector3 readable = new Vector3(left ? 1f : -1f, 0f, -tan).normalized;
                Quaternion rot = Quaternion.LookRotation(-readable, Vector3.up);

                var wrapper = WrapSection(sections[i], widths[i], i + 1, sections.Count);
                var binding = MakeWorldPanel($"Exhibit_{i}", new Vector3(x, PANEL_EYE_Y, z), rot, wrapper, widths[i] + 44f);
                binding.WantsPlate = true;
                _panels.Add(binding);
            }
        }

        void BuildEntranceTitle(int count)
        {
            var content = new VisualElement();
            content.style.alignItems = Align.Center;
            content.style.paddingTop = 18; content.style.paddingBottom = 18;
            content.style.paddingLeft = 28; content.style.paddingRight = 28;

            var kicker = new Label("WORLD-SPACE GALLERY");
            kicker.style.color = C_TRIM; kicker.style.fontSize = 15;
            kicker.style.unityFontStyleAndWeight = FontStyle.Bold; kicker.style.letterSpacing = 3;
            kicker.style.marginBottom = 8;
            content.Add(kicker);

            var title = new Label("Unity 6 UI Toolkit Design System");
            title.style.color = Color.white; title.style.fontSize = 34;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleCenter; title.style.whiteSpace = WhiteSpace.Normal;
            content.Add(title);

            string hint = WorldNavInput.TouchUiLikely
                ? "Use the left stick to walk and the right stick to look around.\nTap any card to interact."
                : "W / S walk   ·   A / D strafe   ·   hold Right-Mouse or Q / E to look   ·   Esc to exit";
            var sub = new Label($"{count} live components line the walls ahead. Walk up and try anything.\n" + hint);
            sub.style.color = new Color(0.63f, 0.66f, 0.70f); sub.style.fontSize = 15;
            sub.style.unityTextAlign = TextAnchor.MiddleCenter; sub.style.whiteSpace = WhiteSpace.Normal;
            sub.style.marginTop = 12;
            content.Add(sub);

            // Straddling the walkway near the entrance, facing the arriving
            // player (who looks toward +Z). Readable face is local -Z, so +Z
            // points +Z.
            var rot = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up);
            var binding = MakeWorldPanel("EntranceTitle", new Vector3(0f, 2.5f, 3.6f), rot, content, 620f);
            binding.WantsPlate = false;   // free-floating sign, not a wall exhibit
            _panels.Add(binding);
        }

        // Frame a harvested section with a small header ("03 / 29") and a fixed
        // content width so the Dynamic world panel has a definite size to
        // resolve to. `sectionWidth` comes from the flat page's resolved
        // layout (HarvestSectionWidths) — the clone's own UXML inline width
        // is not readable from C#.
        VisualElement WrapSection(VisualElement section, float sectionWidth, int index, int total)
        {
            var wrap = new VisualElement();
            // Fixed width gives the Dynamic world panel a definite size to resolve
            // to; height stays free so the exhibit shows ALL of its content, then
            // FitExhibits scales the whole panel down to fit the wall. Centre the
            // children: the section is narrower than the wrap, so without this it
            // sits left-biased and, once the whole panel is centred on the wall,
            // reads as "off-centre with a corner cut off".
            wrap.style.width = sectionWidth + 44f;
            wrap.style.alignItems = Align.Center;
            wrap.style.paddingTop = 12; wrap.style.paddingBottom = 14;
            wrap.style.paddingLeft = 12; wrap.style.paddingRight = 12;

            var counter = new Label($"{index:00} / {total:00}");
            counter.style.color = C_TRIM;
            counter.style.fontSize = 11;
            counter.style.unityFontStyleAndWeight = FontStyle.Bold;
            counter.style.letterSpacing = 2;
            counter.style.marginBottom = 6;
            counter.style.alignSelf = Align.Center;
            wrap.Add(counter);

            section.style.marginTop = 0; section.style.marginBottom = 0;
            section.style.marginLeft = 0; section.style.marginRight = 0;
            section.style.flexShrink = 0;                    // never squeeze below its declared width
            section.style.width = sectionWidth;
            section.style.alignSelf = Align.Center;
            wrap.Add(section);
            return wrap;
        }

        // Build a single world-space UI panel. Content is injected in the
        // reload callback (Unity fires it for code-built panels too) — see
        // PanelBinding.OnReload for the pile-up guard that keeps re-entrancy
        // and Show/Hide re-enable cycles from duplicating anything.
        PanelBinding MakeWorldPanel(string name, Vector3 pos, Quaternion rot, VisualElement content, float contentWidth)
        {
            // The anchor carries the wall position + facing; the PanelRenderer is
            // a child at local identity. FitExhibits sets the child's local scale
            // AND a local offset of -localBounds.center*scale, which centres the
            // exhibit on the anchor no matter where the panel's pivot sits (UI
            // Toolkit panels pivot at their top-left, so without this they'd hang
            // low and off to one side — the "overflowing from the wall" report).
            var anchor = new GameObject(name);
            anchor.transform.SetParent(_content.transform, false);
            anchor.transform.SetPositionAndRotation(pos, rot);

            var go = new GameObject(name + "_Panel");
            go.transform.SetParent(anchor.transform, false);
            // Born microscopic + transparent, revealed by FitExhibits once its
            // real size is known. Without this, every panel spends its first
            // seconds at raw layout scale — a wall of giant, top-left-pivoted
            // planes swinging through the corridor (the "misaligned cards" of
            // the first build).
            go.transform.localScale = Vector3.one * PREFIT_SCALE;

            var ps = MakeWorldPanelSettings(name + "_PS");

            var pr = go.AddComponent<PanelRenderer>();
            pr.panelSettings = ps;
            pr.visualTreeAsset = null;                 // content comes from code
            pr.worldSpaceSizeMode = WorldSpaceSizeMode.Dynamic;

            // Only PanelEventHandler is needed per world panel: it dispatches
            // picked pointer events into the panel, and setting its panel makes
            // its GameObject the panel's selectableGameObject (the target the
            // camera's WorldDocumentRaycaster routes hits to). PanelRaycaster
            // is for flat/screen panels and no-ops on world panels.
            var handler = go.AddComponent<PanelEventHandler>();

            var binding = new PanelBinding
            {
                Owner = this,
                Renderer = pr,
                Content = content,
                ContentWidth = contentWidth,
                Ds = _dsUss,
                Theme = _themeOverrideUss,
                Focus = _focusRingUss,
                Handler = handler,
            };
            pr.RegisterUIReloadCallback(binding.OnReload);
            return binding;
        }

        PanelSettings MakeWorldPanelSettings(string name)
        {
            var asset = Resources.Load<PanelSettings>("DefaultPanelSettings");
            var ps = asset != null ? Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = name;
            if (_theme != null) ps.themeStyleSheet = _theme;
            ps.renderMode = PanelRenderMode.WorldSpace;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = 1f;
            ps.clearColor = false;
            return ps;
        }

        void AddWorldRaycaster()
        {
            if (_cam == null) return;
            var wr = _cam.GetComponent<WorldDocumentRaycaster>();
            if (wr == null) wr = _cam.gameObject.AddComponent<WorldDocumentRaycaster>();
            wr.camera = _cam;
        }

        // Wait for the panels to lay out, then uniformly scale each so its
        // exhibit fits the corridor (target height, clamped by width) and
        // reveal it. All panels are polled in the same loop — they clone and
        // lay out concurrently, so exhibit 20 is usually ready right behind
        // exhibit 1 (the old one-at-a-time wait serialized ~30 panels into
        // seconds of visible pop-in). localBounds is rotation/scale
        // independent, so measuring at PREFIT_SCALE is exact.
        IEnumerator FitExhibits()
        {
            var pending = new List<PanelBinding>(_panels);
            float budget = FIT_TIMEOUT;
            while (pending.Count > 0 && budget > 0f)
            {
                yield return null;
                // Panels only lay out while the corridor content is active —
                // don't burn the budget (or mis-measure) while hidden.
                if (!_isShown) continue;
                float dt = Time.deltaTime;
                budget -= dt;

                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    var p = pending[i];
                    if (p?.Renderer == null) { pending.RemoveAt(i); continue; }

                    Vector3 size = p.Renderer.localBounds.size;
                    bool measurable = size.x > 0.001f && size.y > 0.001f;
                    if (measurable && Mathf.Abs(size.y - p.LastMeasuredHeight) < 0.002f)
                    {
                        p.StableFor += dt;
                        if (p.StableFor >= FIT_STABLE_FOR)
                        {
                            FitPanel(p, size);
                            pending.RemoveAt(i);
                            continue;
                        }
                    }
                    else
                    {
                        p.StableFor = 0f;
                    }
                    p.LastMeasuredHeight = size.y;
                }
            }

            // Never fit a garbage scale: a panel that failed to measure stays
            // microscopic (effectively hidden) instead of covering the hall.
            foreach (var p in pending)
                Debug.LogWarning("[WorldSpaceCorridor] '" +
                    (p?.Renderer != null ? p.Renderer.name : "?") +
                    "' never reported a usable size; exhibit left hidden.");
        }

        void FitPanel(PanelBinding p, Vector3 size)
        {
            // Fit inside the height x width box: the smaller ratio wins so the
            // exhibit never overflows its slot in either dimension.
            float scale = Mathf.Clamp(
                Mathf.Min(TARGET_PANEL_HEIGHT / size.y, MAX_PANEL_WIDTH / size.x),
                0.0005f, 100f);

            var t = p.Renderer.transform;
            t.localScale = Vector3.one * scale;
            // Centre the content on the anchor: shift the panel by the negated
            // (scaled) bounds centre so its middle lands on the wall point,
            // independent of the panel's pivot.
            t.localPosition = -p.Renderer.localBounds.center * scale;

            p.Fitted = true;
            if (p.Root != null) p.Root.style.opacity = 1f;

            if (p.WantsPlate && t.parent != null)
                AddPlate(t.parent, size.x * scale, size.y * scale);
        }

        // A slightly-lighter slab bridging exhibit → wall. Gives every card a
        // physical mount (so it reads as hung, not floating), puts local
        // contrast behind dark cards, and hides the wall gap at glancing
        // angles: its depth reaches back INTO the wall, so no seam shows.
        // Built after the fit so it hugs the exhibit's real world size.
        void AddPlate(Transform anchor, float w, float h)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "ExhibitPlate";
            go.transform.SetParent(anchor, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.26f);   // front face ~1 cm behind the panel plane
            go.transform.localScale = new Vector3(w + 0.26f, h + 0.26f, 0.5f);
            // Placed in anchor space for the math, then moved under the
            // geometry group (pose preserved) so Hide() can SetActive it off
            // with the rest of the shell — the anchor itself must stay active
            // for its PanelRenderer.
            go.transform.SetParent(_geometry.transform, true);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = _plateMat;
        }

        // ── Primitive / material helpers ────────────────────────────────────

        void AddBox(string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_geometry.transform, false);
            go.transform.position = center;
            go.transform.localScale = size;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);           // keep physics rays from snagging on the shell
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = mat;
        }

        // Opaque lit clone, following unity-mesh-fracture's MaterialFactory
        // recipe. The explicit white _BaseMap bind matters: on WebGL2,
        // runtime-created materials that rely on the shader's `= "white"`
        // texture default sometimes sample (0,0,0,0) and render black.
        Material MakeMat(Color c, float smoothness)
        {
            var m = new Material(_baseMaterial);
            m.SetTexture("_BaseMap", Texture2D.whiteTexture);
            m.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            SetBaseColor(m, c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", 0f);
            m.SetColor("_EmissionColor", Color.black);   // walls: no glow, lamps + base color carry them
            return m;
        }

        Material MakeEmissive(Color c, float intensity)
        {
            var m = MakeMat(c * 0.4f, 0f);
            // The committed CorridorLit asset ships with _EMISSION enabled,
            // which is what pins this shader_feature variant into the build —
            // EnableKeyword at runtime can only select variants that exist.
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * intensity); // strips: raise emission so they read as light
            return m;
        }

        // URP/Lit uses _BaseColor; legacy fallbacks use _Color. Set whichever
        // the shader exposes.
        static void SetBaseColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color"))     m.SetColor("_Color", c);
        }

        // Base surface material = the CorridorLit asset under Resources — a
        // plain URP Simple Lit material with _EMISSION enabled, committed to
        // the repo (BuildCli.EnsureCorridorMaterial self-heals it). What keeps
        // its shader variants compiled into the WebGL build is the
        // CorridorMaterialKeepAlive renderer in Showcase.unity: on 6000.5,
        // URP's "strip unused variants" pass zeroes every URP-family shader
        // that no BUILD-SCENE renderer references — Resources references and
        // even Always-Included Shaders don't protect ("After scriptable
        // stripping: 0" in the build log = the magenta corridor). Two other
        // dead ends for the record: a hand-rolled minimal shader (same
        // zeroing), and Always-Included URP/Lit à la unity-mesh-fracture
        // (6000.5.2f1 segfaults enumerating Lit's variant space at build
        // time).
        static Material ResolveBaseMaterial()
        {
            var baseMat = Resources.Load<Material>("CorridorLit");
            if (baseMat != null) return new Material(baseMat);

            // Fallbacks (fresh clone before the asset was generated).
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) return new Material(shader);

            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var pm = new Material(probe.GetComponent<Renderer>().sharedMaterial);
            Destroy(probe);
            return pm;
        }

        void OnDestroy()
        {
            if (_player != null)
            {
                _player.ExitRequested -= Hide;
                _player.SuppressKeys = null;
            }
        }

        // ── Per-panel binding ───────────────────────────────────────────────

        sealed class PanelBinding
        {
            public WorldSpaceCorridor Owner;
            public PanelRenderer Renderer;
            public VisualElement Content;
            public VisualElement Root;      // last reload's panel root (for spin + theme)
            public float ContentWidth;
            public StyleSheet Ds;
            public StyleSheet Theme;
            public StyleSheet Focus;    // ShowcaseFocusRing — keyboard focus parity with the flat page
            public PanelEventHandler Handler;

            public bool WantsPlate;
            public bool Fitted;                     // FitExhibits has sized + revealed this panel
            public bool Wired;                      // PanelReady already invoked for this exhibit
            public float LastMeasuredHeight = -1f;  // fit-loop stability tracking
            public float StableFor;
            public List<VisualElement> Spinners;    // cached .is-spinning targets (null = re-query)

            // Re-entrant on purpose: PanelRenderer can rebuild its root on
            // enable, and any element we added in a prior callback would be
            // orphaned. Re-parenting the same Content element (rather than
            // cloning) both restores it and avoids the documented duplicate
            // pile-up, since a VisualElement can only live in one place.
            public void OnReload(PanelRenderer pr, VisualElement root, int version)
            {
                if (root == null) return;
                Root = root;
                Spinners = null;   // new root, stale cache

                // The design system scopes a whole family of rules under `.ds-root` (scrollbar
                // restyling, the 14px/-text-primary base) — on the flat page that's the page
                // ScrollView. Without the class, world exhibits get the chunky default-theme
                // scrollbars and a 12px baseline. The page background that comes with it is
                // unwanted here (the exhibit's backdrop is the 3D plate), which is exactly what
                // `ds-root--hud` is for: the ds-root cascade with no opaque fill.
                root.AddToClassList("ds-root");
                root.AddToClassList("ds-root--hud");

                // Carries the theme-swap guard, exactly as on the flat page: the guard is written as
                // `.showcase-root.ds-no-transition` so it outranks a ThemeData's `:root` token block
                // and can zero --transition-fast during a swap. See ShowcaseTheme.uss.
                root.AddToClassList("showcase-root");

                // Pin the root to the exhibit's own width and centre the wrap in
                // it. Without a definite width the Dynamic world panel resolved to
                // a large default with the content floating inside it. minHeight
                // keeps the panel from momentarily resolving to a ZERO size during
                // the toggle's re-layout, which is what tipped engine layout math
                // into the divide-by-zero on WebGL.
                root.style.width = ContentWidth;
                root.style.minHeight = 64;
                root.style.flexGrow = 0;
                root.style.alignItems = Align.Center;
                // Invisible until measured + scaled; opacity (unlike display)
                // still lays out, so FitExhibits can size it while hidden.
                root.style.opacity = Fitted ? 1f : 0f;
                // Fresh roots default visible; respect the current mode
                // (reloads shouldn't happen while hidden anymore, but if one
                // does, the exhibit must not paint over the flat page).
                root.style.visibility = Owner != null && Owner.IsShown
                    ? Visibility.Visible : Visibility.Hidden;

                if (Ds != null && !root.styleSheets.Contains(Ds)) root.styleSheets.Add(Ds);
                if (Theme != null && !root.styleSheets.Contains(Theme)) root.styleSheets.Add(Theme);
                if (Focus != null && !root.styleSheets.Contains(Focus)) root.styleSheets.Add(Focus);

                if (Content != null && Content.parent != root)
                    root.Add(Content);

                // Every auto-attached DS behavior, in one call. The exhibits are
                // FRESH UXML clones (HarvestSections instantiates the showcase
                // asset), so nothing the flat page's runtime wired carries over —
                // each world panel has to be wired on its own. This used to be a
                // hand-picked list of Ensure* calls and it silently rotted: tab
                // panels and scroll auto-hide shipped dead in world space because
                // nobody remembered to add them here. EnsureAll is the seam; new
                // behaviors arrive for free.
                //
                // Spinners are the one exception, and are not in EnsureAll: they
                // need a MonoBehaviour's schedule handle, so the corridor drives
                // `.is-spinning` across all panels itself (see Update).
                DsBehaviour.EnsureAll(root);

                // Re-apply whatever theme the flat page is currently on, so a
                // theme picked BEFORE entering the corridor (or while inside it)
                // shows on the exhibits too.
                Owner?.ApplyThemeToPanel(this);

                // The world-space raycaster + event handler need the runtime
                // IPanel. PanelSettings doesn't expose it publicly, but the
                // callback's root element is already attached to that panel —
                // VisualElement.panel is the same IPanel.
                var panel = root.panel;
                if (panel != null && Handler != null)
                    Handler.panel = panel;

                // Dropdown popups: Unity 6's GenericDropdownMenu adds the popup as a SIBLING of the
                // panel root (under the panel's visualTree), so root-attached stylesheets never reach
                // it. Each exhibit is its OWN panel, so each one needs its own attach — the popup
                // chrome, and the token block that chrome's var(--color-*) references resolve against.
                // ShowcaseBootstrap.PaintRoot then adds the active theme's sheet on top, at the same
                // scope, which is what lets a world-space popup follow the theme.
                var panelScope = root.parent ?? panel?.visualTree;
                ShowcaseBootstrap.InstallPanelScopeSheets(panelScope);

                // Wire the exhibit's cloned controls exactly ONCE: the wired
                // elements live in the persistent Content tree (re-parented,
                // never rebuilt), so re-running on a reload would double-
                // register their callbacks.
                if (!Wired)
                {
                    Wired = true;
                    Owner?.PanelReady?.Invoke(root);
                }
            }
        }
    }
}
#endif
