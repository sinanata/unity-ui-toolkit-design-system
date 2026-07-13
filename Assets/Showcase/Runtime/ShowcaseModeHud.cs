using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Showcase.Runtime
{
    // The world-mode overlay: a transparent, high-sortingOrder UIDocument that
    // rides above the 3D corridor. It only appears while walking the gallery and
    // carries the things you need in there — a movement-hint strip, a way out
    // (the "Screen Space" button, mirroring the Esc key), and on touch devices
    // the virtual sticks that drive the walker.
    //
    // The PRIMARY Screen/World switch lives in the page itself, below the hero
    // (built by ShowcaseBootstrap). This overlay is just the in-corridor chrome,
    // since that page switch is hidden while the flat page is hidden.
    //
    // Touch scheme: dual virtual sticks (left = walk, right = look) writing
    // into WorldNavInput, which FirstPersonController reads. The sticks are the
    // ONLY touch surfaces that move the camera — they capture their pointer on
    // this screen-space panel, so a stick drag can never reach an exhibit, and
    // a tap on an exhibit can never turn into camera motion. That's the
    // "mobile-friendly but not interaction-conflicting" contract.
    public sealed class ShowcaseModeHud
    {
        const float STICK_SIZE = 128f;
        const float KNOB_SIZE  = 54f;

        static readonly Color C_BORDER     = new Color(0.149f, 0.188f, 0.255f, 1f);
        static readonly Color C_HINT_TEXT  = new Color(0.82f, 0.84f, 0.88f);
        static readonly Color C_LABEL_TEXT = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color C_PRIMARY    = new Color(0.133f, 0.773f, 0.369f); // --color-primary
        static readonly Color C_SECONDARY  = new Color(0.231f, 0.510f, 0.965f); // --color-secondary

        readonly UIDocument _doc;
        readonly StyleSheet _dsUss;
        readonly Action _onExit;
        readonly Action<VisualElement> _onBuilt;

        /// <summary>
        /// The HUD's document root, once it exists. The host needs it to repaint the mode switch on a
        /// theme change: the switch is a real `.ds-tabs`, so it takes its colours from the `--color-*`
        /// tokens on this root and from nowhere else. Null until <see cref="Build"/> runs, which is a
        /// frame or more after Create returns — hence <c>onBuilt</c> rather than a property you poll.
        /// </summary>
        public VisualElement Root { get; private set; }

        VisualElement _overlay;
        VisualElement _hints;
        Label _hintText;
        VisualElement _tabsWrap;
        Button _screenTab, _worldTab;
        VisualElement _moveStickWrap, _lookStickWrap;
        VirtualStick _moveStick, _lookStick;
        bool _touchUi;

        public static ShowcaseModeHud Create(UIDocument doc, StyleSheet dsUss, Action onExit,
                                             Action<VisualElement> onBuilt = null)
        {
            var hud = new ShowcaseModeHud(doc, dsUss, onExit, onBuilt);
            hud.ScheduleBuild();
            return hud;
        }

        ShowcaseModeHud(UIDocument doc, StyleSheet dsUss, Action onExit, Action<VisualElement> onBuilt)
        {
            _doc = doc;
            _dsUss = dsUss;
            _onExit = onExit;
            _onBuilt = onBuilt;
        }

        void ScheduleBuild()
        {
            var pump = new GameObject("HudBuildPump").AddComponent<BuildPump>();
            pump.Run(_doc, Build);
        }

        void Build(VisualElement root)
        {
            if (root == null) return;
            if (_dsUss != null && !root.styleSheets.Contains(_dsUss)) root.styleSheets.Add(_dsUss);

            // This is the case `ds-root--hud` exists for, so the showcase's own HUD
            // is built out of it: the corridor has to stay visible through this
            // panel, and plain `ds-root` would paint `--color-bg` over the whole
            // gallery. The pairing buys the ds-root cascade (text ramp, scrollbar
            // and focus-ring families) with no fill — which is what lets the mode
            // switch below be real `.ds-tabs` rather than a hand-styled lookalike.
            root.AddToClassList("ds-root");
            root.AddToClassList("ds-root--hud");

            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;

            _overlay = new VisualElement { name = "world-hud" };
            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0; _overlay.style.top = 0; _overlay.style.right = 0; _overlay.style.bottom = 0;
            root.Add(_overlay);

            BuildModeTabs(_overlay);
            BuildHints(_overlay);
            BuildSticks(_overlay);

            // Decide touch vs desktop chrome now, then keep polling cheaply:
            // on some browsers the Touchscreen device only registers on the
            // first physical touch, so the sticks must be able to appear late.
            _touchUi = !WorldNavInput.TouchUiLikely;   // force first Refresh to apply
            RefreshTouchUi();
            _overlay.schedule.Execute(RefreshTouchUi).Every(500);

            _overlay.style.display = DisplayStyle.None; // screen mode by default

            Root = root;
            _onBuilt?.Invoke(root);   // the host paints us; see ShowcaseBootstrap.PaintHud
        }

        // The SAME segmented Screen/World switch the flat page shows under its
        // hero (ShowcaseBootstrap.BuildInPageModeToggle) — consistent chrome
        // in both modes. "World Space" carries is-active while the corridor is
        // up; clicking "Screen Space" exits (mirroring Esc).
        void BuildModeTabs(VisualElement overlay)
        {
            _tabsWrap = new VisualElement { name = "hud-mode-tabs" };
            _tabsWrap.AddToClassList("ds-tabs");
            _tabsWrap.style.position = Position.Absolute;
            _tabsWrap.style.top = 14;
            _tabsWrap.style.left = Length.Percent(50);
            _tabsWrap.style.translate = new Translate(Length.Percent(-50), 0f);
            _tabsWrap.style.paddingTop = 5; _tabsWrap.style.paddingBottom = 5;
            _tabsWrap.style.paddingLeft = 5; _tabsWrap.style.paddingRight = 5;

            _screenTab = MakeModeTab("Screen Space", () => _onExit?.Invoke());
            _worldTab  = MakeModeTab("World Space", null);
            _worldTab.AddToClassList("is-active");

            _tabsWrap.Add(_screenTab);
            _tabsWrap.Add(_worldTab);
            overlay.Add(_tabsWrap);
        }

        static Button MakeModeTab(string text, Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.AddToClassList("ds-tab");
            b.style.paddingTop = 10; b.style.paddingBottom = 10;
            b.style.paddingLeft = 22; b.style.paddingRight = 22;
            b.style.fontSize = 14;
            return b;
        }

        void BuildHints(VisualElement overlay)
        {
            _hints = new VisualElement { name = "mode-hints" };
            _hints.pickingMode = PickingMode.Ignore;
            _hints.style.position = Position.Absolute;
            _hints.style.bottom = 14;
            _hints.style.left = Length.Percent(50);
            _hints.style.translate = new Translate(Length.Percent(-50), 0f);
            _hints.style.flexDirection = FlexDirection.Row;
            _hints.style.alignItems = Align.Center;
            _hints.style.maxWidth = Length.Percent(92);
            _hints.style.paddingTop = 8; _hints.style.paddingBottom = 8;
            _hints.style.paddingLeft = 16; _hints.style.paddingRight = 16;
            _hints.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.72f);
            // 10px, not a 999 pill: on a short strip a pill radius rounds the
            // ends into half-circles and the whole bar reads as an ellipse.
            RoundBorder(_hints, C_BORDER, 1, 10);

            _hintText = new Label();
            _hintText.style.color = C_HINT_TEXT;
            _hintText.style.fontSize = 12;
            _hintText.style.whiteSpace = WhiteSpace.NoWrap;
            _hintText.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hints.Add(_hintText);

            overlay.Add(_hints);
        }

        void BuildSticks(VisualElement overlay)
        {
            _moveStickWrap = MakeStickWrap("WALK", C_PRIMARY, v => WorldNavInput.Move = v, out _moveStick);
            _moveStickWrap.style.left = 26;
            overlay.Add(_moveStickWrap);

            _lookStickWrap = MakeStickWrap("LOOK", C_SECONDARY, v => WorldNavInput.Look = v, out _lookStick);
            _lookStickWrap.style.right = 26;
            overlay.Add(_lookStickWrap);
        }

        VisualElement MakeStickWrap(string caption, Color knobColor, Action<Vector2> onValue, out VirtualStick stick)
        {
            var wrap = new VisualElement { name = $"stick-{caption.ToLowerInvariant()}" };
            wrap.pickingMode = PickingMode.Ignore;
            wrap.style.position = Position.Absolute;
            wrap.style.bottom = 26;
            wrap.style.alignItems = Align.Center;
            wrap.style.display = DisplayStyle.None;   // RefreshTouchUi decides

            var label = new Label(caption);
            label.pickingMode = PickingMode.Ignore;
            label.style.color = C_LABEL_TEXT;
            label.style.fontSize = 10;
            label.style.letterSpacing = 2;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 6;
            wrap.Add(label);

            stick = new VirtualStick(STICK_SIZE, KNOB_SIZE, knobColor, onValue);
            wrap.Add(stick);
            return wrap;
        }

        // Swap chrome between the desktop and touch schemes. Cheap: no-ops
        // unless the answer changed since the last poll.
        void RefreshTouchUi()
        {
            bool touch = WorldNavInput.TouchUiLikely;
            if (touch == _touchUi) return;
            _touchUi = touch;

            if (_moveStickWrap != null)
                _moveStickWrap.style.display = touch ? DisplayStyle.Flex : DisplayStyle.None;
            if (_lookStickWrap != null)
                _lookStickWrap.style.display = touch ? DisplayStyle.Flex : DisplayStyle.None;

            if (_hintText != null)
            {
                _hintText.text = touch
                    ? "Left stick walk    ·    right stick look    ·    tap a card to interact"
                    : "W / S walk    ·    A / D strafe    ·    hold Right-Mouse or Q / E look    ·    click to interact    ·    Esc exit";
                _hintText.style.whiteSpace = touch ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            }

            if (_hints != null)
            {
                // Touch: park the hints under the mode tabs — the bottom
                // corners belong to the sticks and a 360px-wide portrait
                // screen has no room between them. Desktop: bottom strip.
                _hints.style.top    = touch ? (StyleLength)78 : (StyleLength)StyleKeyword.Auto;
                _hints.style.bottom = touch ? (StyleLength)StyleKeyword.Auto : (StyleLength)14;
            }

            // ≥44px touch targets on touch devices.
            foreach (var tab in new[] { _screenTab, _worldTab })
            {
                if (tab == null) continue;
                tab.style.paddingTop = touch ? 12 : 10;
                tab.style.paddingBottom = touch ? 12 : 10;
                tab.style.paddingLeft = touch ? 24 : 22;
                tab.style.paddingRight = touch ? 24 : 22;
            }
        }

        static void RoundBorder(VisualElement el, Color c, int w, int r)
        {
            el.style.borderTopWidth = w; el.style.borderBottomWidth = w;
            el.style.borderLeftWidth = w; el.style.borderRightWidth = w;
            el.style.borderTopColor = c; el.style.borderBottomColor = c;
            el.style.borderLeftColor = c; el.style.borderRightColor = c;
            el.style.borderTopLeftRadius = r; el.style.borderTopRightRadius = r;
            el.style.borderBottomLeftRadius = r; el.style.borderBottomRightRadius = r;
        }

        // Show the overlay only while in world mode.
        public void SetActive(bool world)
        {
            if (_overlay != null)
                _overlay.style.display = world ? DisplayStyle.Flex : DisplayStyle.None;

            if (world)
            {
                RefreshTouchUi();
            }
            else
            {
                // A stick can be mid-drag when Esc fires; make sure no stale
                // deflection keeps steering next time the corridor opens.
                _moveStick?.ForceRelease();
                _lookStick?.ForceRelease();
                WorldNavInput.Reset();
            }
        }

        // A fixed-base virtual joystick rendered as two circles. The base
        // captures its pointer, so once a drag starts here the exhibits never
        // see it — and vice versa, a drag that started on an exhibit never
        // reaches the stick.
        sealed class VirtualStick : VisualElement
        {
            readonly VisualElement _knob;
            readonly Action<Vector2> _onValue;
            readonly float _radius;
            int _pointerId = -1;

            public VirtualStick(float size, float knobSize, Color knobColor, Action<Vector2> onValue)
            {
                _onValue = onValue;
                _radius = (size - knobSize) * 0.5f;

                pickingMode = PickingMode.Position;
                style.width = size;
                style.height = size;
                style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.55f);
                RoundBorder(this, new Color(0.28f, 0.34f, 0.44f, 0.9f), 1, (int)(size * 0.5f));

                _knob = new VisualElement { name = "stick-knob" };
                _knob.pickingMode = PickingMode.Ignore;
                _knob.style.position = Position.Absolute;
                _knob.style.left = (size - knobSize) * 0.5f;
                _knob.style.top = (size - knobSize) * 0.5f;
                _knob.style.width = knobSize;
                _knob.style.height = knobSize;
                _knob.style.backgroundColor = new Color(knobColor.r, knobColor.g, knobColor.b, 0.92f);
                RoundBorder(_knob, new Color(1f, 1f, 1f, 0.18f), 1, (int)(knobSize * 0.5f));
                Add(_knob);

                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCancelEvent>(_ => ForceRelease());
                RegisterCallback<PointerCaptureOutEvent>(_ => ForceRelease());
            }

            void OnPointerDown(PointerDownEvent e)
            {
                _pointerId = e.pointerId;
                this.CapturePointer(_pointerId);
                Apply(e.localPosition);
                e.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (e.pointerId != _pointerId || !this.HasPointerCapture(_pointerId)) return;
                Apply(e.localPosition);
                e.StopPropagation();
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (e.pointerId != _pointerId) return;
                this.ReleasePointer(_pointerId);   // triggers PointerCaptureOut → ForceRelease
                e.StopPropagation();
            }

            void Apply(Vector3 localPos)
            {
                var half = new Vector2(resolvedStyle.width, resolvedStyle.height) * 0.5f;
                var d = new Vector2(localPos.x, localPos.y) - half;
                d = Vector2.ClampMagnitude(d, _radius);
                _knob.style.translate = new Translate(d.x, d.y);

                // Screen-Y grows downward; sticks report Y-up.
                var v = new Vector2(d.x, -d.y) / _radius;
                _onValue?.Invoke(v.sqrMagnitude < 0.006f ? Vector2.zero : v);   // ~8% dead zone
            }

            public void ForceRelease()
            {
                if (_pointerId != -1 && this.HasPointerCapture(_pointerId))
                    this.ReleasePointer(_pointerId);
                _pointerId = -1;
                _knob.style.translate = new Translate(0f, 0f);
                _onValue?.Invoke(Vector2.zero);
            }
        }

        // Waits for the UIDocument root then builds, without the caller owning a
        // MonoBehaviour.
        sealed class BuildPump : MonoBehaviour
        {
            public void Run(UIDocument doc, Action<VisualElement> onReady) => StartCoroutine(Wait(doc, onReady));

            System.Collections.IEnumerator Wait(UIDocument doc, Action<VisualElement> onReady)
            {
                while (doc == null || doc.rootVisualElement == null) yield return null;
                onReady(doc.rootVisualElement);
                Destroy(gameObject);
            }
        }
    }
}
