using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace Showcase.Runtime
{
    // Overlay attached to a higher-sortingOrder UIDocument that visualises the
    // selector chain of whatever element the user is hovering / tapping in the
    // showcase. The overlay panel itself swallows clicks (pin / copy); the
    // rest of the overlay is pickingMode=Ignore so showcase scrolls / hovers
    // pass through.
    public class ShowcaseDocOverlay : MonoBehaviour
    {
        const int    MOBILE_BREAKPOINT = 768;
        const int    PANEL_WIDTH       = 320;
        const int    PANEL_MAX_HEIGHT  = 360;

        // Token-aligned palette (kept in sync with DesignTokens.uss). The
        // overlay deliberately does NOT load the design-system stylesheet —
        // styling it programmatically isolates the overlay from any USS
        // changes a contributor makes to the showcase.
        static readonly Color CSurface  = new Color(0.075f, 0.102f, 0.141f, 0.96f);
        static readonly Color CBorder   = new Color(0.149f, 0.188f, 0.255f, 1f);
        static readonly Color CText     = new Color(0.949f, 0.957f, 0.969f, 1f);
        static readonly Color CTextDim  = new Color(0.631f, 0.655f, 0.702f, 1f);
        static readonly Color CMuted    = new Color(0.404f, 0.439f, 0.522f, 1f);
        static readonly Color CPrimary  = new Color(0.133f, 0.773f, 0.369f, 1f);
        static readonly Color CChainTxt = new Color(0.882f, 0.898f, 0.929f, 1f);

        UIDocument _showcaseDoc;
        UIDocument _overlayDoc;

        VisualElement _panel;
        VisualElement _highlight;
        Label _titleLabel;
        ScrollView _chainScroll;
        Label _classesLabel;
        Label _hintLabel;

        VisualElement _currentLeaf;
        bool _pinned;

        // Idle-fade state: when the cursor sits on containers / empty space
        // for FADE_DELAY_MS the panel + highlight fade out via inline
        // opacity transition. Hovering a meaningful component (or pinning)
        // cancels the timer and brings them back.
        const long FADE_DELAY_MS    = 2000;
        const long FADE_DURATION_MS = 240;
        IVisualElementScheduledItem _fadeOutTimer;
        IVisualElementScheduledItem _hideAfterFade;
        bool _isFadedOut;

        public void AttachTo(UIDocument showcaseDoc, UIDocument overlayDoc)
        {
            _showcaseDoc = showcaseDoc;
            _overlayDoc  = overlayDoc;
        }

        void OnEnable()
        {
            StartCoroutine(WaitForRootsThenSetup());
        }

        IEnumerator WaitForRootsThenSetup()
        {
            // UIDocument doesn't build its rootVisualElement until its own
            // OnEnable; we attach in Bootstrap before that fires.
            while (_showcaseDoc == null || _overlayDoc == null ||
                   _showcaseDoc.rootVisualElement == null || _overlayDoc.rootVisualElement == null)
                yield return null;

            BuildOverlay(_overlayDoc.rootVisualElement);

            var showcaseRoot = _showcaseDoc.rootVisualElement;
            // TrickleDown so we observe the leaf-most pick before children
            // call StopPropagation on their own buttons.
            showcaseRoot.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            showcaseRoot.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        }

        // ── Overlay construction ────────────────────────────────────────────

        void BuildOverlay(VisualElement root)
        {
            // The overlay doc's root must pass events through wherever we
            // don't have an interactive surface; only the panel itself
            // captures input.
            root.pickingMode = PickingMode.Ignore;
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0;
            root.style.right = 0; root.style.bottom = 0;

            _highlight = MakeHighlight();
            root.Add(_highlight);

            _panel = MakePanel();
            root.Add(_panel);

            // Inline opacity transition shared by panel + highlight. We drive
            // fade-in/out by writing style.opacity = 0 or 1 elsewhere; the
            // transition makes that animated rather than instant.
            ConfigureOpacityTransition(_panel);
            ConfigureOpacityTransition(_highlight);

            _titleLabel = MakeTitle("Hover or tap any component");
            _panel.Add(_titleLabel);

            _chainScroll = new ScrollView(ScrollViewMode.Vertical);
            _chainScroll.style.flexGrow = 1;
            _chainScroll.style.maxHeight = 220;
            _chainScroll.style.marginBottom = 8;
            _panel.Add(_chainScroll);

            var classesHeading = new Label("LEAF CLASSES");
            classesHeading.style.color = CTextDim;
            classesHeading.style.fontSize = 9;
            classesHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
            classesHeading.style.letterSpacing = 1;
            _panel.Add(classesHeading);

            _classesLabel = new Label("—");
            _classesLabel.style.color = CPrimary;
            _classesLabel.style.fontSize = 11;
            _classesLabel.style.whiteSpace = WhiteSpace.Normal;
            _classesLabel.style.marginTop = 2;
            _classesLabel.style.marginBottom = 8;
            _classesLabel.RegisterCallback<ClickEvent>(_ => CopyClassesToClipboard());
            _panel.Add(_classesLabel);

            _hintLabel = new Label("Click panel to pin. Click classes to copy.");
            _hintLabel.style.color = CMuted;
            _hintLabel.style.fontSize = 10;
            _panel.Add(_hintLabel);
        }

        VisualElement MakeHighlight()
        {
            var h = new VisualElement { name = "doc-overlay-highlight" };
            h.pickingMode = PickingMode.Ignore;
            h.style.position = Position.Absolute;
            ApplyBorder(h, CPrimary, 2, 6);
            h.style.display = DisplayStyle.None;
            return h;
        }

        VisualElement MakePanel()
        {
            var p = new VisualElement { name = "doc-overlay-panel" };
            p.pickingMode = PickingMode.Position;
            p.style.position = Position.Absolute;
            p.style.width = PANEL_WIDTH;
            p.style.maxHeight = PANEL_MAX_HEIGHT;
            p.style.paddingTop = 12; p.style.paddingBottom = 12;
            p.style.paddingLeft = 14; p.style.paddingRight = 14;
            p.style.backgroundColor = CSurface;
            ApplyBorder(p, CBorder, 1, 10);
            p.style.display = DisplayStyle.None;
            p.RegisterCallback<ClickEvent>(_ =>
            {
                _pinned = !_pinned;
                if (_pinned) CancelFade(); // pinned panels never auto-hide
                UpdatePanelHint();
            });
            return p;
        }

        Label MakeTitle(string text)
        {
            var t = new Label(text);
            t.style.color = CText;
            t.style.fontSize = 13;
            t.style.unityFontStyleAndWeight = FontStyle.Bold;
            t.style.marginBottom = 6;
            return t;
        }

        static void ApplyBorder(VisualElement el, Color color, int width, int radius)
        {
            el.style.borderTopWidth = width; el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width; el.style.borderRightWidth = width;
            el.style.borderTopColor = color; el.style.borderBottomColor = color;
            el.style.borderLeftColor = color; el.style.borderRightColor = color;
            el.style.borderTopLeftRadius = radius; el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius; el.style.borderBottomRightRadius = radius;
        }

        // ── Input ───────────────────────────────────────────────────────────

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (_pinned) return;
            UpdateInspection(evt.target as VisualElement);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            // Mobile primary-trigger; on desktop also re-inspects on click
            // outside the pinned selection.
            if (_pinned) return;
            UpdateInspection(evt.target as VisualElement);
        }

        // ── Inspection ──────────────────────────────────────────────────────

        // Showcase-only layout wrappers that exist to group / arrange the demo
        // page itself, not to ship as design-system components. Hovering them
        // should walk past, not stop.
        static readonly System.Collections.Generic.HashSet<string> ContainerClasses =
            new System.Collections.Generic.HashSet<string>
            {
                "ds-root",
                "ds-section",
                "ds-section__title",
                "ds-row",
                "ds-swatch-row",
            };

        void UpdateInspection(VisualElement leaf)
        {
            if (leaf == null) return;

            // Showcase page chrome (promo banner, future headers/footers)
            // is marked with `.showcase-chrome` and is excluded from
            // inspection wholesale — the elements inside use real ds-*
            // classes for visual consistency, but they're sales copy, not
            // components a visitor came to inspect.
            if (IsInsideChrome(leaf))
            {
                ScheduleFadeOut();
                return;
            }

            // Walk up looking for the deepest element that carries a ds-*
            // class which is NOT a showcase layout container. Unity's internal
            // wrappers (unity-toggle__input etc.) are skipped automatically
            // because they don't carry a `ds-` class at all.
            var meaningful = leaf;
            while (meaningful != null && !IsMeaningfulComponent(meaningful))
                meaningful = meaningful.parent;

            if (meaningful == null)
            {
                // Cursor is over only containers / empty space. Keep the
                // previous inspection visible so the panel stays reachable
                // when the user is heading for it to click-to-pin, but kick
                // off a FADE_DELAY_MS countdown — if they don't return to
                // anything meaningful, the panel + highlight fade out.
                ScheduleFadeOut();
                return;
            }

            // Cursor is on a real component — cancel any pending fade and
            // make sure the panel is visible (fade back in if it had
            // already faded).
            CancelFade();
            EnsureVisible();

            if (meaningful == _currentLeaf) return;
            _currentLeaf = meaningful;

            RebuildChainPanel();
            UpdateHighlight();
            UpdatePanelHint();
            PositionPanelNear(meaningful);
        }

        // ── Fade ────────────────────────────────────────────────────────────

        void ConfigureOpacityTransition(VisualElement el)
        {
            el.style.transitionProperty = new StyleList<StylePropertyName>(
                new List<StylePropertyName> { new StylePropertyName("opacity") });
            el.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(FADE_DURATION_MS, TimeUnit.Millisecond) });
            el.style.transitionTimingFunction = new StyleList<EasingFunction>(
                new List<EasingFunction> { new EasingFunction(EasingMode.EaseOut) });
        }

        void ScheduleFadeOut()
        {
            if (_pinned) return;                                  // pinned panels don't auto-hide
            if (_isFadedOut) return;                              // already faded
            if (_fadeOutTimer != null) return;                    // already scheduled
            if (_panel.style.display == DisplayStyle.None) return; // never been shown

            _fadeOutTimer = _panel.schedule.Execute(() =>
            {
                _fadeOutTimer = null;
                DoFadeOut();
            }).StartingIn(FADE_DELAY_MS);
        }

        void CancelFade()
        {
            _fadeOutTimer?.Pause();
            _fadeOutTimer = null;
            _hideAfterFade?.Pause();
            _hideAfterFade = null;
        }

        void DoFadeOut()
        {
            if (_pinned) return;
            _isFadedOut = true;
            _panel.style.opacity = 0f;
            _highlight.style.opacity = 0f;

            // After the opacity transition finishes, take the panel out of
            // the picking tree (display:None) so an invisible-but-present
            // panel doesn't swallow pointer events from the showcase below.
            _hideAfterFade?.Pause();
            _hideAfterFade = _panel.schedule.Execute(() =>
            {
                _hideAfterFade = null;
                if (_isFadedOut)
                {
                    _panel.style.display = DisplayStyle.None;
                    _highlight.style.display = DisplayStyle.None;
                }
            }).StartingIn(FADE_DURATION_MS + 50);
        }

        void EnsureVisible()
        {
            // Already fully visible — nothing to do.
            if (!_isFadedOut && _panel.style.display == DisplayStyle.Flex)
                return;

            _isFadedOut = false;
            _hideAfterFade?.Pause();
            _hideAfterFade = null;

            _panel.style.display = DisplayStyle.Flex;
            if (_currentLeaf != null) _highlight.style.display = DisplayStyle.Flex;

            // Defer one frame: the display flip needs to commit before the
            // opacity write triggers a transition, otherwise the panel can
            // appear instantly at opacity 1 with no fade-in animation.
            _panel.schedule.Execute(() =>
            {
                _panel.style.opacity = 1f;
                _highlight.style.opacity = 1f;
            }).StartingIn(0);
        }

        static bool IsMeaningfulComponent(VisualElement el)
        {
            foreach (var c in el.GetClasses())
            {
                if (!c.StartsWith("ds-")) continue;
                if (ContainerClasses.Contains(c)) continue;
                return true;
            }
            return false;
        }

        static bool IsInsideChrome(VisualElement el)
        {
            var node = el;
            while (node != null)
            {
                if (node.ClassListContains("showcase-chrome")) return true;
                node = node.parent;
            }
            return false;
        }

        void RebuildChainPanel()
        {
            _chainScroll.Clear();
            _titleLabel.text = "SELECTOR CHAIN";

            var stack = new List<VisualElement>();
            var node = _currentLeaf;
            while (node != null) { stack.Add(node); node = node.parent; }
            stack.Reverse();

            int displayedDepth = 0;
            for (int i = 0; i < stack.Count; i++)
            {
                var el = stack[i];
                if (!ShouldShowInChain(el, isLeaf: i == stack.Count - 1)) continue;

                bool isLeaf = (i == stack.Count - 1);
                _chainScroll.Add(MakeChainRow(el, displayedDepth, isLeaf));
                displayedDepth++;
            }

            _classesLabel.text = JoinClasses(_currentLeaf);
        }

        Label MakeChainRow(VisualElement el, int depth, bool isLeaf)
        {
            var row = new Label(FormatNode(el, depth, isLeaf));
            row.style.fontSize = 11;
            row.style.color = isLeaf ? CPrimary : CChainTxt;
            row.style.unityFontStyleAndWeight = isLeaf ? FontStyle.Bold : FontStyle.Normal;
            row.style.whiteSpace = WhiteSpace.NoWrap;
            row.style.marginTop = 1; row.style.marginBottom = 1;
            return row;
        }

        static string FormatNode(VisualElement el, int depth, bool isLeaf)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < depth; i++) sb.Append("  ");
            if (depth > 0) sb.Append("└ ");
            sb.Append(el.GetType().Name);
            if (!string.IsNullOrEmpty(el.name) && !el.name.StartsWith("unity-"))
                sb.Append("#").Append(el.name);
            foreach (var c in el.GetClasses())
            {
                if (c.StartsWith("unity-")) continue;
                sb.Append(".").Append(c);
            }
            if (isLeaf) sb.Append("  ◀");
            return sb.ToString();
        }

        static string JoinClasses(VisualElement el)
        {
            if (el == null) return "—";
            var sb = new StringBuilder();
            foreach (var c in el.GetClasses())
            {
                if (c.StartsWith("unity-")) continue;
                if (sb.Length > 0) sb.Append(" ");
                sb.Append(".").Append(c);
            }
            return sb.Length == 0 ? "(no ds-* classes)" : sb.ToString();
        }

        static bool HasDsClass(VisualElement el)
        {
            foreach (var c in el.GetClasses())
                if (c.StartsWith("ds-")) return true;
            return false;
        }

        // Render rows for elements that carry meaning to a reader: anything
        // with a ds-* class, the leaf itself, and any element with a
        // hand-set name. Hide Unity's auto-generated wrappers.
        static bool ShouldShowInChain(VisualElement el, bool isLeaf)
        {
            if (isLeaf) return true;
            if (HasDsClass(el)) return true;
            if (!string.IsNullOrEmpty(el.name) && !el.name.StartsWith("unity-")) return true;
            return false;
        }

        // ── Highlight ───────────────────────────────────────────────────────

        IVisualElementScheduledItem _highlightTracker;

        void UpdateHighlight()
        {
            _highlightTracker?.Pause();
            if (_currentLeaf == null) {
                _highlight.style.display = DisplayStyle.None;
                return;
            }
            _highlight.style.display = DisplayStyle.Flex;

            // 16ms tick (~60Hz) — cheap; only writes 4 style fields per frame.
            // worldBound updates on layout, so without this tick the highlight
            // would lag scroll/resize by one event.
            _highlightTracker = _highlight.schedule.Execute(() =>
            {
                if (_currentLeaf == null || _currentLeaf.panel == null) {
                    _highlight.style.display = DisplayStyle.None;
                    return;
                }
                var b = _currentLeaf.worldBound;
                _highlight.style.left   = b.x - 2;
                _highlight.style.top    = b.y - 2;
                _highlight.style.width  = b.width + 4;
                _highlight.style.height = b.height + 4;
            }).Every(16);
        }

        // ── Panel positioning ───────────────────────────────────────────────

        void PositionPanelNear(VisualElement target)
        {
            // worldBound and the inline style coordinates we set live in panel
            // coordinate space, which now equals CSS pixels on every device
            // (ShowcaseBootstrap sets ps.scale = devicePixelRatio so the panel
            // grid is decoupled from the HiDPI drawing buffer). Read panel
            // dimensions from the overlay root's resolved layout — comparing
            // against Screen.width here would mix CSS-px with buffer-px and
            // dock the panel off-screen on every Retina display.
            var overlayRoot = _overlayDoc != null ? _overlayDoc.rootVisualElement : null;
            float panelW = overlayRoot != null ? overlayRoot.layout.width  : 0f;
            float panelH = overlayRoot != null ? overlayRoot.layout.height : 0f;
            if (panelW <= 0f || float.IsNaN(panelW)) panelW = Screen.width;
            if (panelH <= 0f || float.IsNaN(panelH)) panelH = Screen.height;

            if (panelW < MOBILE_BREAKPOINT)
            {
                // Mobile: docked along the bottom, full-width minus margin.
                _panel.style.left   = 12;
                _panel.style.right  = 12;
                _panel.style.bottom = 12;
                _panel.style.top    = StyleKeyword.Auto;
                _panel.style.width  = StyleKeyword.Auto;
                _panel.style.maxHeight = 240;
                return;
            }

            var b = target.worldBound;
            float left = b.xMax + 12;
            float top  = b.yMin;
            if (left + PANEL_WIDTH > panelW)        left = Mathf.Max(12, b.xMin - PANEL_WIDTH - 12);
            if (top  + PANEL_MAX_HEIGHT > panelH)   top  = Mathf.Max(12, panelH - PANEL_MAX_HEIGHT - 12);

            _panel.style.left   = left;
            _panel.style.top    = top;
            _panel.style.right  = StyleKeyword.Auto;
            _panel.style.bottom = StyleKeyword.Auto;
            _panel.style.width  = PANEL_WIDTH;
            _panel.style.maxHeight = PANEL_MAX_HEIGHT;
        }

        // ── Hint / clipboard ────────────────────────────────────────────────

        void UpdatePanelHint()
        {
            _hintLabel.text = _pinned
                ? "Pinned. Click panel to unpin."
                : "Click panel to pin. Click classes to copy.";
            _hintLabel.style.color = _pinned ? CPrimary : CMuted;
        }

        void CopyClassesToClipboard()
        {
            var s = JoinClasses(_currentLeaf);
            if (string.IsNullOrEmpty(s) || s == "—" || s.StartsWith("(no ")) return;
            GUIUtility.systemCopyBuffer = s;
            _hintLabel.text = "Copied: " + s;
            _hintLabel.style.color = CPrimary;
            _hintLabel.schedule.Execute(UpdatePanelHint).StartingIn(1500);
        }
    }
}
