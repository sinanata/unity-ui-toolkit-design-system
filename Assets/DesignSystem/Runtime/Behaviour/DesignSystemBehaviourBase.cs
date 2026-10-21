using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Behaviour
{
    /// <summary>
    /// Runtime helpers for the ds-* design system. Auto-attaches to every
    /// UIDocument in the scene at load. Provides:
    ///   - Looping spinner rotation (USS transitions can't loop natively)
    ///   - Toggle-knob auto-injection: every <Toggle class="ds-toggle"> gets a
    ///     child <VisualElement class="ds-toggle__knob"> if one is missing
    ///   - Skeleton shimmer translation (sliding overlay)
    ///   - Pointer-drag ghosts for `.ds-draggable` / `.ds-drop-zone`
    ///   - Dropdown popup placement for `.ds-dropdown`: always opens downward,
    ///     height-capped with vertical-only scrolling (web select behavior)
    ///
    /// Authoring tip: hand-author the toggle knob in UXML when you can — it
    /// avoids a one-frame "no knob" flash during template clone. The runtime
    /// is the safety net for screens that didn't.
    ///
    /// Opting out of the automatic attach (a host that adds the component itself,
    /// for example from the one place it creates every document):
    ///   - at runtime, set <see cref="AutoAttach"/> to false before the first scene
    ///     finishes loading (a [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] method);
    ///   - at compile time, add the scripting define DS_NO_AUTO_ATTACH, which compiles
    ///     the scene-load hook out entirely.
    /// Either way <see cref="AttachToAll"/> still works when called explicitly.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class DesignSystemBehaviourBase<TComponent> : MonoBehaviour, IDsSpinnerHost where TComponent : Component
    {
        private static readonly Dictionary<System.Type, System.Type> s_componentTypeMap = new();
        private const string SPINNER_CLASS         = "ds-spinner";
        private const string SPINNER_ACTIVE_CLASS  = "is-spinning";
        private const string TOGGLE_CLASS          = "ds-toggle";
        private const string TOGGLE_KNOB_CLASS     = "ds-toggle__knob";
        private const string SKELETON_CLASS        = "ds-skeleton";
        private const string SHIMMER_CLASS         = "ds-skeleton__shimmer";
        private const string DRAGGABLE_CLASS       = "ds-draggable";
        private const string DRAG_WIRED_CLASS      = "ds-drag--wired";   // internal: marks an already-wired draggable
        private const string DROP_ZONE_CLASS       = "ds-drop-zone";
        private const string DRAG_OVER_CLASS       = "is-drag-over";
        private const string DRAG_GHOST_CLASS      = "ds-drag-ghost";
        private const string DRAGGING_CLASS        = "is-dragging";      // on the source item while its ghost is out
        private const string ROOT_CLASS            = "ds-root";
        private const string TABS_CLASS            = "ds-tabs";
        private const string TAB_CLASS             = "ds-tab";
        private const string TABPANELS_CLASS       = "ds-tabpanels";
        private const string TABPANEL_CLASS        = "ds-tabpanel";
        private const string TABS_WIRED_CLASS      = "ds-tabs--wired";   // internal: marks an already-wired tab strip
        private const string SIDE_NAV_CLASS        = "ds-side-nav";
        private const string NAV_ITEM_CLASS        = "ds-nav-item";
        private const string SIDE_NAV_WIRED_CLASS  = "ds-side-nav--wired"; // internal: marks an already-wired side nav
        private const string ACTIVE_CLASS          = "is-active";
        private const string SCROLL_AUTOHIDE_CLASS = "ds-scroll--auto-hide";
        private const string SCROLL_WIRED_CLASS    = "ds-scroll--wired"; // internal: marks an already-wired scroll view
        private const string DS_DROPDOWN_CLASS     = "ds-dropdown";
        private const string DROPDOWN_WIRED_CLASS  = "ds-dropdown--menu-wired"; // internal: marks an already-wired dropdown
        private const string POPUP_TUNED_CLASS     = "ds-popup--tuned";         // internal: marks an already-tuned popup instance

        // Unity's GenericDropdownMenu internals (names verified against
        // UnityCsReference; DropdownPopup.uss documents the full map).
        private const string POPUP_CLASS       = "unity-base-dropdown";
        private const string POPUP_OUTER_CLASS = "unity-base-dropdown__container-outer";

        // A row resolves to ~42px (28px min-height loses to 6px padding + a 14px line), so 360px is
        // eight of them: enough of a list to browse, short enough that a 40-item list still scrolls
        // rather than towering over the page.
        private const float DROPDOWN_POPUP_MAX_HEIGHT  = 360f;
        private const float DROPDOWN_POPUP_MIN_HEIGHT  = 72f;  // absolute floor when NEITHER side has room
        private const float DROPDOWN_POPUP_GAP         = 4f;   // field edge -> popup edge
        private const float DROPDOWN_POPUP_EDGE_MARGIN = 8f;   // popup edge -> panel edge breathing room
        private const float DROPDOWN_POPUP_ROW_HEIGHT  = 28f;  // fallback row height before the list has laid out
        private const int   DROPDOWN_TUNE_MAX_TICKS    = 30;   // frames to wait for the popup to attach before giving up
        private const string PANEL_TUNER_CLASS = "ds-dropdown-tuner";   // on the PANEL ROOT, only while a ds-dropdown popup is being placed

        private const long  SPIN_INTERVAL_MS   = 16;
        private const float SPIN_STEP_DEGREES  = 6f;
        private const long  RESCAN_INTERVAL_MS = 250;

        private VisualElement _root;
        private IVisualElementScheduledItem _spinTask;
        private IVisualElementScheduledItem _rescanTask;
        private float _spinAngle;
        // The `.is-spinning` elements under _root. The 16 ms tick walks THIS list, never the
        // document: the list is rebuilt by the 250 ms rescan (which walks the document anyway) and
        // topped up the moment SetSpinning starts one, so the per-frame cost is one style write per
        // spinner and nothing at all while none is spinning.
        private readonly List<VisualElement> _spinners = new();
        private System.Action _spinTick;
        private System.Action _rescan;

        /// <summary>
        /// Whether loading a scene attaches this behaviour to every <typeparamref name="TComponent"/>
        /// that does not have it yet. Default true.
        ///
        /// Set it to false before the first scene finishes loading if the host adds the component
        /// itself: from a <c>[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]</c>
        /// method, or the Awake of an object in the first scene (Unity raises sceneLoaded after Awake
        /// and OnEnable, before Start). Set it through the backend class, which is also the only
        /// place the value lives: <c>UIDocument.DesignSystemBehaviour.AutoAttach = false</c> leaves
        /// the PanelRenderer backend's own flag alone. It is never reset by the package, so with
        /// domain reload disabled it keeps whatever the host last set.
        ///
        /// Only the scene-load hook reads it; an explicit <see cref="AttachToAll"/> always runs. The
        /// scripting define <c>DS_NO_AUTO_ATTACH</c> removes the hook at compile time instead.
        /// </summary>
        public static bool AutoAttach { get; set; } = true;

        protected abstract void OnEnable();

        /// <summary>
        /// Run every auto-attached behavior over <paramref name="root"/>: knob injection, skeleton
        /// shimmers, draggables, dropdown popups, tab panels, scroll auto-hide. Idempotent, static,
        /// and free of per-instance state, so any host can call it for any panel root.
        ///
        /// This is the ONE list. Hosts that can't use the runtime MonoBehaviour — anything driving
        /// its own <c>PanelRenderer</c> reload callback, e.g. the showcase's world-space corridor —
        /// must call this rather than hand-picking `Ensure*` helpers: a hand-picked list silently
        /// stops covering the system the moment a new behavior is added here, which is exactly how
        /// tab panels and scroll auto-hide shipped dead in world space. Add new behaviors HERE and
        /// every host gets them.
        ///
        /// Spinners are deliberately NOT here: the spinner tick owns a per-instance schedule handle
        /// (see <see cref="InitFor"/>), so it belongs to the MonoBehaviour lifecycle, not to a root
        /// sweep. A host without the MonoBehaviour drives `.is-spinning` itself, as the corridor does.
        /// </summary>
        public static void EnsureAll(VisualElement root)
        {
            if (root == null) return;
            EnsureToggleKnobs(root);
            EnsureSkeletonShimmers(root);
            EnsureDraggables(root);
            EnsureDropdownMenus(root);
            EnsureTabs(root);
            EnsureSideNav(root);
            EnsureScrollAutoHide(root);
        }

        protected void InitFor(VisualElement root)
        {
            if (root == null) return;

            // A PanelRenderer reload hands over a FRESH root while this component lives on: stop the
            // previous root's loops first, or every reload would leave another rescan running.
            StopLoops();
            _root = root;
            EnsureAll(root);
            DsRuntimeShared.SpinnerHosts.Add(this);
            RefreshSpinners();

            // Periodic re-scan: ScreenBase and similar consumers clone screen
            // templates lazily when the user navigates to them. The first
            // EnsureAll pass only sees what's in the tree at attach time —
            // toggles cloned in later (e.g. Settings on first open) would
            // otherwise stay knob-less and render as a flat pill.
            // 250 ms is fast enough that the user never notices a missing knob
            // after a screen transition, and cheap enough to ignore (a Query
            // with an existence check on already-knobbed toggles is O(N) over
            // the small number of ds-toggle elements). Idempotent — every
            // helper no-ops if its work is already done. The handle is kept so
            // the rescan stops with the component (it used to run forever).
            _rescan ??= Rescan;
            _rescanTask = root.schedule.Execute(_rescan).Every(RESCAN_INTERVAL_MS);
        }

        protected virtual void OnDisable()
        {
            StopLoops();
            CancelInvoke();
        }

        private void StopLoops()
        {
            _spinTask?.Pause();
            _spinTask = null;
            _rescanTask?.Pause();
            _rescanTask = null;
            _spinners.Clear();
            _root = null;
            DsRuntimeShared.SpinnerHosts.Remove(this);
        }

        private void Rescan()
        {
            if (_root == null) return;
            EnsureAll(_root);
            RefreshSpinners();
        }

        // Rotate every element carrying `.is-spinning`, regardless of whether
        // it's a `.ds-spinner` ring, a `.ds-icon` glyph (e.g. a refresh icon
        // turning into a loading indicator on a button), or any other
        // element a screen wants to spin. The class is purely behavioral:
        // visual styling stays on whatever class the element already has.
        private void RefreshSpinners()
        {
            _spinners.Clear();
            _root?.Query(className: SPINNER_ACTIVE_CLASS).ToList(_spinners);
            UpdateSpinTask();
        }

        // The 16 ms tick exists only while something spins. A document with no spinner used to pay a
        // full-tree query every frame for nothing; now it pays nothing until one appears.
        private void UpdateSpinTask()
        {
            if (_spinners.Count == 0 || _root == null)
            {
                _spinTask?.Pause();
                return;
            }
            if (_spinTask == null)
            {
                _spinTick ??= SpinTick;
                _spinTask = _root.schedule.Execute(_spinTick).Every(SPIN_INTERVAL_MS);
            }
            else if (!_spinTask.isActive)
            {
                _spinTask.Resume();
            }
        }

        private void SpinTick()
        {
            _spinAngle = (_spinAngle + SPIN_STEP_DEGREES) % 360f;
            var rotate = new StyleRotate(new Rotate(_spinAngle));
            for (var i = _spinners.Count - 1; i >= 0; i--)
            {
                var el = _spinners[i];
                // Stopped, or gone from the panel: drop it. The next rescan finds it again if it
                // comes back spinning.
                if (el.panel == null || !el.ClassListContains(SPINNER_ACTIVE_CLASS))
                {
                    _spinners.RemoveAt(i);
                    continue;
                }
                el.style.rotate = rotate;
            }
            if (_spinners.Count == 0) _spinTask?.Pause();
        }

        // SetSpinning(el, true) reaches every live behaviour, so a spinner started from code turns on
        // the next tick rather than waiting up to 250 ms for the rescan to notice it.
        void IDsSpinnerHost.TrackSpinner(VisualElement el)
        {
            if (_root == null || el.panel == null || el.panel != _root.panel || !_root.Contains(el)) return;
            if (!_spinners.Contains(el)) _spinners.Add(el);
            UpdateSpinTask();
        }

        /// <summary>
        /// Toggle a spinning state on any element. Adds/removes the
        /// `is-spinning` marker class which the runtime's tick rotates.
        /// When stopping, snaps the rotation back to 0° so the next time
        /// the element shows it's not frozen at a random angle.
        /// </summary>
        public static void SetSpinning(VisualElement el, bool spinning)
        {
            if (el == null) return;
            if (spinning)
            {
                if (!el.ClassListContains(SPINNER_ACTIVE_CLASS))
                    el.AddToClassList(SPINNER_ACTIVE_CLASS);
                var hosts = DsRuntimeShared.SpinnerHosts;
                for (var i = 0; i < hosts.Count; i++)
                    hosts[i].TrackSpinner(el);
            }
            else
            {
                el.RemoveFromClassList(SPINNER_ACTIVE_CLASS);
                el.style.rotate = new StyleRotate(new Rotate(0f));
            }
        }

        /// <summary>
        /// Inject `<VisualElement class="ds-toggle__knob">` into every
        /// `.ds-toggle` whose unity-toggle__input wrapper doesn't already
        /// have one. Idempotent. Call this from screen bootstrap right
        /// after a template clones so the knob is present on the first
        /// frame the toggle is visible.
        /// </summary>
        public static void EnsureToggleKnobs(VisualElement root)
        {
            if (root == null) return;
            root.Query<Toggle>(className: TOGGLE_CLASS).ForEach(toggle =>
            {
                var input = toggle.Q(className: "unity-toggle__input");
                if (input == null) return;
                if (input.Q(className: TOGGLE_KNOB_CLASS) != null) return;

                var knob = new VisualElement();
                knob.AddToClassList(TOGGLE_KNOB_CLASS);
                knob.pickingMode = PickingMode.Ignore;
                input.Add(knob);
            });
        }

        /// <summary>
        /// Make every `.ds-tabs` strip actually switch content. The Nth `.ds-tab` activates the Nth
        /// `.ds-tabpanel` of the strip's panel container; clicking a tab moves `is-active` onto it and
        /// onto its panel, and off every sibling. Idempotent.
        ///
        /// Positional, not id-based, on purpose: the failure this fixes is authors shipping a tab strip
        /// with no way to switch at all, so the pattern has to work from markup alone with nothing to
        /// wire up, nothing to name and nothing to keep in sync. A strip with no panel container is
        /// left alone — some tab strips really are just filter chips over one list, and those drive
        /// their own logic off `is-active`.
        /// </summary>
        public static void EnsureTabs(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: TABS_CLASS).ForEach(tabs =>
            {
                if (tabs.ClassListContains(TABS_WIRED_CLASS)) return;
                // Mark only on success. A strip with no panels yet is not necessarily a filter-chip
                // strip: a controller may still be about to add its `.ds-tabpanels`. Marking it here
                // would retire it from the 250 ms rescan (whose whole job is catching late content)
                // and it would never wire. Re-examining an unwired strip is a walk over its parent's
                // direct children, which is cheap enough to repeat.
                if (WireTabs(tabs)) tabs.AddToClassList(TABS_WIRED_CLASS);
            });
        }

        /// <summary>
        /// Make every `.ds-side-nav` switch content, exactly like `.ds-tabs` does. A side nav IS a tab
        /// strip — vertical tabs — and it shipped with no way to switch: `EnsureTabs` only ever looked
        /// for `.ds-tabs`, so a Settings screen with a Graphics / Audio / Controls rail rendered the
        /// Graphics pane and then ignored every other item forever. Give the nav a `.ds-tabpanels`
        /// sibling and the Nth `.ds-nav-item` shows the Nth panel. Idempotent.
        /// </summary>
        public static void EnsureSideNav(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: SIDE_NAV_CLASS).ForEach(nav =>
            {
                if (nav.ClassListContains(SIDE_NAV_WIRED_CLASS)) return;
                if (WireSideNav(nav)) nav.AddToClassList(SIDE_NAV_WIRED_CLASS);
            });
        }

        /// <summary>Wire one `.ds-side-nav` to its panels. See <see cref="WireTabs"/>.</summary>
        public static bool WireSideNav(VisualElement nav) => WirePanelSwitcher(nav, NAV_ITEM_CLASS);

        /// <summary>
        /// Wire one tab strip to its panels. Pass the `.ds-tabs` element; the panel container is the
        /// `.ds-tabpanels` among its immediate siblings (the canonical layout puts it directly after).
        ///
        /// Siblings ONLY — deliberately not a subtree search. A tab strip and an unrelated tabbed view
        /// can easily share an ancestor (the showcase's Screen/World mode switch sits in the same
        /// scroll container as every demo section), and a descendant search would let the first strip
        /// on the page seize the panels of a tabbed view further down and fight its real strip over
        /// which panel is active.
        ///
        /// Returns false if there was nothing to wire (no panel container, no tabs, no panels), which
        /// is the normal, non-error outcome for a filter-chip strip.
        /// </summary>
        public static bool WireTabs(VisualElement tabs) => WirePanelSwitcher(tabs, TAB_CLASS);

        /// <summary>
        /// The one switcher behind both `.ds-tabs` (horizontal) and `.ds-side-nav` (vertical). Both are
        /// the same thing — a strip of items, a stack of panels, the Nth item shows the Nth panel —
        /// and they were only separate long enough for one of them to ship dead.
        /// </summary>
        private static bool WirePanelSwitcher(VisualElement strip, string itemClass)
        {
            if (strip?.parent == null) return false;

            VisualElement panelHost = null;
            foreach (var sibling in strip.parent.Children())
            {
                if (!sibling.ClassListContains(TABPANELS_CLASS)) continue;
                panelHost = sibling;
                break;
            }
            if (panelHost == null) return false;   // filter-chip strip, not a tabbed view — nothing to switch

            // Untyped query: a tab is a Button, but a nav item is usually a plain VisualElement. Both
            // are wired below through whichever click path they actually have.
            var itemList = strip.Query(className: itemClass).ToList();

            // Direct children of the host, for the same reason the host itself is a sibling lookup:
            // a tabbed view nested INSIDE one of these panels brings its own `.ds-tabpanel` elements,
            // and a subtree query would fold them into this strip's positional mapping.
            var panelList = new List<VisualElement>();
            foreach (var child in panelHost.Children())
                if (child.ClassListContains(TABPANEL_CLASS))
                    panelList.Add(child);

            if (itemList.Count == 0 || panelList.Count == 0) return false;

            for (var i = 0; i < itemList.Count; i++)
            {
                var index = i;   // capture per iteration, not the loop variable
                if (itemList[i] is Button button) button.clicked += () => Activate(index);
                else itemList[i].RegisterCallback<ClickEvent>(_ => Activate(index));
            }

            // Honor whatever the UXML marked active; default to the first item so a strip is never
            // rendered with every panel hidden.
            var initial = itemList.FindIndex(t => t.ClassListContains(ACTIVE_CLASS));
            Activate(initial < 0 ? 0 : initial);
            return true;

            void Activate(int index)
            {
                for (var i = 0; i < itemList.Count; i++)
                    SetActive(itemList[i], i == index);
                for (var i = 0; i < panelList.Count; i++)
                    SetActive(panelList[i], i == index);
            }

            static void SetActive(VisualElement el, bool active)
            {
                if (active) el.AddToClassList(ACTIVE_CLASS);
                else el.RemoveFromClassList(ACTIVE_CLASS);
            }
        }

        /// <summary>
        /// Attach the touch-friendly scrollbar flash to every `.ds-scroll--auto-hide` ScrollView.
        /// Without this the `is-scrolling` half of the auto-hide rule never fired — the class was
        /// defined in USS, the helper existed, and nothing ever called it, so auto-hiding scrollbars
        /// only worked for mouse users with a `:hover`. Idempotent.
        /// </summary>
        public static void EnsureScrollAutoHide(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: SCROLL_AUTOHIDE_CLASS).ForEach(scroll =>
            {
                if (scroll.ClassListContains(SCROLL_WIRED_CLASS)) return;
                scroll.AddToClassList(SCROLL_WIRED_CLASS);
                WireScrollAutoHide(scroll);
            });
        }

        /// <summary>
        /// Wire a drawer's open / close state. Adds an `is-open` class to
        /// <paramref name="wrapperOrDrawer"/> when <paramref name="opener"/>
        /// is clicked, and removes it when any of <paramref name="closers"/>
        /// (typically the close button + an optional `.ds-drawer__backdrop`)
        /// is clicked. Idempotent — calling twice with the same elements
        /// re-registers the handlers (cheap; UI Toolkit deduplicates by
        /// delegate identity).
        ///
        /// Pass the `.ds-drawer-wrap` ancestor as <paramref name="wrapperOrDrawer"/>
        /// so backdrop + drawer respond to the same class (recommended). Or
        /// pass the drawer itself for freestanding usage — the USS rules
        /// support both targets.
        ///
        /// Pure-CSS authors don't need this helper at all: any code that
        /// flips `is-open` (or `ds-drawer--open` on a self-driven drawer)
        /// triggers the same animation.
        /// </summary>
        public static void WireDrawer(Button opener, VisualElement wrapperOrDrawer, params VisualElement[] closers)
        {
            if (opener == null || wrapperOrDrawer == null) return;

            // Closed-state pointer hygiene. `opacity: 0` does NOT disable
            // picking in UI Toolkit — an invisible backdrop still captures
            // clicks and shadows the burger button beneath it. Track which
            // closers are non-button overlays (typically the backdrop) and
            // toggle their `pickingMode` in lockstep with `is-open` so they
            // only receive clicks while actually visible.
            var nonButtonClosers = new System.Collections.Generic.List<VisualElement>();

            void SyncOpenState()
            {
                bool open = wrapperOrDrawer.ClassListContains("is-open");
                if (opener.ClassListContains("ds-burger"))
                {
                    if (open) opener.AddToClassList("is-open");
                    else      opener.RemoveFromClassList("is-open");
                }
                foreach (var c in nonButtonClosers)
                    c.pickingMode = open ? PickingMode.Position : PickingMode.Ignore;
            }

            opener.clicked += () =>
            {
                if (wrapperOrDrawer.ClassListContains("is-open"))
                    wrapperOrDrawer.RemoveFromClassList("is-open");
                else
                    wrapperOrDrawer.AddToClassList("is-open");
                SyncOpenState();
            };

            if (closers == null) { SyncOpenState(); return; }
            foreach (var closer in closers)
            {
                if (closer == null) continue;
                if (closer is Button btn)
                {
                    btn.clicked += () =>
                    {
                        wrapperOrDrawer.RemoveFromClassList("is-open");
                        SyncOpenState();
                    };
                }
                else
                {
                    nonButtonClosers.Add(closer);
                    closer.RegisterCallback<PointerDownEvent>(_ =>
                    {
                        wrapperOrDrawer.RemoveFromClassList("is-open");
                        SyncOpenState();
                    });
                }
            }

            // Initial sync: in case the drawer ships with `is-open` already
            // applied (some screens want a starts-open variant), the backdrop
            // is interactive on first paint instead of one click later.
            SyncOpenState();
        }

        /// <summary>
        /// Touch-friendly auto-hide: flash the scrollbars on for ~700 ms
        /// whenever the user scrolls, even on devices that don't fire
        /// `:hover`. Pure-USS auto-hide via the `:hover` rule still works
        /// for mouse users; this helper adds the `is-scrolling` marker
        /// the auto-hide rule also responds to.
        /// </summary>
        public static void WireScrollAutoHide(VisualElement scrollView)
        {
            if (scrollView == null) return;

            IVisualElementScheduledItem clearTask = null;

            scrollView.RegisterCallback<WheelEvent>(_ => Flash(), TrickleDown.TrickleDown);
            scrollView.RegisterCallback<PointerDownEvent>(_ => Flash(), TrickleDown.TrickleDown);
            return;

            void Flash()
            {
                if (!scrollView.ClassListContains("is-scrolling"))
                    scrollView.AddToClassList("is-scrolling");
                clearTask?.Pause();
                clearTask = scrollView.schedule.Execute(() =>
                    scrollView.RemoveFromClassList("is-scrolling")).StartingIn(700);
            }
        }

        public static void EnsureSkeletonShimmers(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: SKELETON_CLASS).ForEach(el =>
            {
                if (el.Q(className: SHIMMER_CLASS) != null) return;
                var shimmer = new VisualElement();
                shimmer.AddToClassList(SHIMMER_CLASS);
                shimmer.pickingMode = PickingMode.Ignore;
                el.Add(shimmer);

                el.schedule.Execute(() =>
                {
                    // `.ds-reduced-motion` hides the shimmer (Motion.uss). A hidden overlay needs no
                    // new position every frame, and skipping the write skips its style invalidation.
                    if (shimmer.resolvedStyle.display == DisplayStyle.None) return;
                    float t = (Time.realtimeSinceStartup % 1.4f) / 1.4f;
                    shimmer.style.translate = new StyleTranslate(
                        new Translate(new Length(t * 200f - 100f, LengthUnit.Percent), 0));
                }).Every(16);
            });
        }

        /// <summary>
        /// Wire pointer-drag behavior onto every `.ds-draggable` not yet wired. Dragging spawns a
        /// `.ds-drag-ghost` that follows the pointer, highlights the `.ds-drop-zone` under it with
        /// `is-drag-over`, and on release moves the dragged element into that zone (the common
        /// "move item between containers" case — reparents on drop). Idempotent.
        ///
        /// This is the drop-in, no-code pattern: mark items `.ds-draggable`, mark containers
        /// `.ds-drop-zone`, done. For CUSTOM drop logic (split/merge/transfer like a game inventory),
        /// don't mark elements `.ds-draggable` — drive your own pointer handling and simply reuse the
        /// `.ds-drag-ghost` / `.ds-drop-zone` / `is-drag-over` visual classes for a consistent look.
        /// </summary>
        public static void EnsureDraggables(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: DRAGGABLE_CLASS).ForEach(item =>
            {
                if (item.ClassListContains(DRAG_WIRED_CLASS)) return;
                item.AddToClassList(DRAG_WIRED_CLASS);
                WireDraggable(item);
            });
        }

        public static void WireDraggable(VisualElement item)
        {
            VisualElement ghost = null;
            VisualElement currentZone = null;

            item.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                var host = GhostHost();
                if (host == null) return;

                ghost = MakeGhost(item);
                // HIERARCHY child, never Add(): when the ds-root is a ScrollView (the showcase's
                // flat page is one), Add() redirects into the SCROLLED content container, and the
                // pointer-to-local mapping there placed the ghost thousands of points above the
                // viewport — created, dragging fine, and fully clipped (measured: content scrolled
                // 10k pt put the ghost at world y ≈ -9500). A hierarchy child of the ds-root
                // element itself sits outside both the scroll transform and the viewport clip,
                // still inside the ds-root stylesheet scope, and on a plain (world-space exhibit)
                // root hierarchy.Add is simply Add — one path, both hosts.
                host.hierarchy.Add(ghost);
                item.AddToClassList(DRAGGING_CLASS);

                item.CapturePointer(e.pointerId);
                PositionGhost(e.position);
                e.StopPropagation();
            });

            item.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (!item.HasPointerCapture(e.pointerId)) return;
                PositionGhost(e.position);
                SetZone(ZoneUnder(e.position));
            });

            item.RegisterCallback<PointerUpEvent>(e =>
            {
                if (!item.HasPointerCapture(e.pointerId)) return;
                item.ReleasePointer(e.pointerId);

                var zone = ZoneUnder(e.position);
                if (zone != null && zone != item.parent)
                    zone.Add(item); // move into the drop zone
            });

            // Single cleanup path: fires from ReleasePointer above AND whenever
            // the capture is lost some other way (element removed mid-drag,
            // panel switch in the world-space gallery, focus loss). Without it
            // those exits stranded the ghost on the page.
            item.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                item.RemoveFromClassList(DRAGGING_CLASS);
                SetZone(null);
                ghost?.RemoveFromHierarchy();
                ghost = null;
            });
            return;

            VisualElement ZoneUnder(Vector2 pos)
            {
                var root = item.panel != null ? item.panel.visualTree : null;
                if (root == null) return null;
                VisualElement found = null;
                root.Query(className: DROP_ZONE_CLASS).ForEach(z =>
                {
                    if (z.worldBound.Contains(pos)) found = z;
                });
                return found;
            }

            // The ghost must live under a `.ds-root` ancestor: every ds-* rule
            // (including .ds-drag-ghost's own `position: absolute`) ships in
            // stylesheets attached AT the ds-root element, so a ghost parented
            // to panel.visualTree — ABOVE those sheets — matched nothing and
            // rendered as an unstyled full-width flow child at the bottom of
            // the panel (the "huge drag artefact" in the world-space gallery).
            VisualElement GhostHost()
            {
                for (var v = item.hierarchy.parent; v != null; v = v.hierarchy.parent)
                    if (v.ClassListContains(ROOT_CLASS)) return v;
                return item.panel?.visualTree;
            }

            void SetZone(VisualElement zone)
            {
                if (currentZone == zone) return;
                currentZone?.RemoveFromClassList(DRAG_OVER_CLASS);
                currentZone = zone;
                currentZone?.AddToClassList(DRAG_OVER_CLASS);
            }

            void PositionGhost(Vector2 pos)
            {
                if (ghost?.parent == null) return;
                var local = ghost.parent.WorldToLocal(pos);
                ghost.style.left = local.x;
                ghost.style.top = local.y;
            }
        }

        // A visual stand-in for the dragged item: same classes (so a chip's
        // ghost LOOKS like that chip) minus the drag markers, plus a shallow
        // copy of the children (icon + label is the common case). The
        // translate(-50%,-50%) centers it on the pointer without reading
        // resolvedStyle — the old half-width math ran before the ghost's
        // first layout and produced NaN offsets on the initial frame.
        private static VisualElement MakeGhost(VisualElement item)
        {
            var ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (var c in item.GetClasses())
                if (c != DRAGGABLE_CLASS && c != DRAG_WIRED_CLASS && c != DRAGGING_CLASS)
                    ghost.AddToClassList(c);

            foreach (var child in item.Children())
            {
                VisualElement copy = child is Label l ? new Label(l.text) : new VisualElement();
                foreach (var c in child.GetClasses()) copy.AddToClassList(c);
                copy.pickingMode = PickingMode.Ignore;
                ghost.Add(copy);
            }
            if (ghost.childCount == 0)
                ghost.Add(new Label("•") { pickingMode = PickingMode.Ignore });

            ghost.AddToClassList(DRAG_GHOST_CLASS);

            // Pin the ghost to the PIXEL size of the thing that was picked up.
            //
            // Copying the item's classes is what makes the ghost look right, and it is also a trap:
            // an inventory item is almost always sized RELATIVE to the cell that holds it
            // (`.inv-item { width: 100%; height: 100% }` — 100% of a 72px slot). The ghost is not in
            // that slot. It is reparented to the `.ds-root`, where the very same rule resolves to
            // 100% of the SCREEN, and the item you picked up detonates into a full-page rectangle.
            //
            // Inline styles outrank every class rule the ghost just copied, whatever the load order,
            // so measuring once here settles it. The item is laid out — the drag began with a pointer
            // down ON it — so resolvedStyle is real, not NaN.
            var w = item.resolvedStyle.width;
            var h = item.resolvedStyle.height;
            if (!float.IsNaN(w) && w > 0f) ghost.style.width = w;
            if (!float.IsNaN(h) && h > 0f) ghost.style.height = h;

            ghost.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
            return ghost;
        }

        /// <summary>
        /// Wire popup-geometry tuning onto every `.ds-dropdown` DropdownField under
        /// `root` that isn't wired yet. Unity's GenericDropdownMenu sizes the popup to
        /// the full option list and slides/flips it when that doesn't fit under the
        /// field: tall lists open UPWARD pinned to the panel edge with the default
        /// chunky scrollbars (plus a horizontal one whenever an option renders wider
        /// than the field), and inside a world-space panel — which is only as tall as
        /// its own section — the popup gets clamped to whatever sliver remains. This
        /// forces the web select model instead: the popup always opens DOWNWARD from
        /// the field, capped to the space below it (up to 320px), scrolling vertically
        /// only. Idempotent.
        /// </summary>
        public static void EnsureDropdownMenus(VisualElement root)
        {
            if (root == null) return;
            int wired = 0;
            root.Query<DropdownField>(className: DS_DROPDOWN_CLASS).ForEach(dd =>
            {
                if (dd.ClassListContains(DROPDOWN_WIRED_CLASS)) return;
                dd.AddToClassList(DROPDOWN_WIRED_CLASS);
                WireDropdownMenu(dd);
                wired++;
            });

            // This used to put `ds-dropdown-tuner` on the panel root here, for good, as soon as the
            // panel held one `.ds-dropdown`. DropdownPopup.uss hides every popup under that class until
            // it is placed, and only ds-dropdown popups are ever placed, so EVERY OTHER dropdown on a
            // shared panel (a plain DropdownField, an EnumField) opened invisible. The class now lives
            // on the panel only while a ds-dropdown's popup is being placed: see DropdownTune.

            // Only when something actually happened. This runs on a rescan loop, and logging every
            // no-op pass buries the interesting lines under a hundred "wired 0".
            if (DesignSystemEvents.DropdownDiagnostics && wired > 0)
                Debug.Log($"[dsdiag] EnsureDropdownMenus on '{root.name}': wired {wired} .ds-dropdown field(s)");
        }

        public static void WireDropdownMenu(DropdownField dd)
        {
            if (dd == null) return;
            // The popup doesn't exist until BasePopupField's own handler runs,
            // so tune one tick later, from every gesture that can open it.
            // TrickleDown: the field stops propagation once it opens the menu,
            // so a bubble-phase handler would never see the gesture. Down AND
            // up are both registered because the open gesture differs across
            // input paths (and Unity versions); a tune that finds no popup —
            // or one already tuned — is a no-op.
            //
            // TrickleDown also means these run BEFORE the field opens its menu, which is what lets
            // DropdownTune.Begin put the hide class on the panel before the popup's first frame.
            var tune = new DropdownTune(dd);
            dd.RegisterCallback<PointerDownEvent>(_ => tune.Begin(), TrickleDown.TrickleDown);
            dd.RegisterCallback<PointerUpEvent>(_ => tune.Begin(), TrickleDown.TrickleDown);
            dd.RegisterCallback<NavigationSubmitEvent>(_ => tune.Begin(), TrickleDown.TrickleDown);
        }

        /// <summary>
        /// Tune the popup as soon as it exists: POLLING, not once. One per wired field.
        ///
        /// This used to be a single `schedule.Execute`, i.e. "look for the popup on the next tick".
        /// That is a race, Unity raises no event when the menu attaches, and losing the race is SILENT:
        /// the tuner finds nothing, returns, nothing retries, and Unity's own placement quietly stands.
        /// It won the race in the editor and lost it in the WebGL player, which is why every dropdown
        /// in the build was untuned — flipping upward in world space, pinned to the field's width, and
        /// never raising the event a runtime palette needs — while the editor's own test suite passed
        /// every check. A race you lose silently is worse than a crash; this one cost days.
        ///
        /// So: try every frame until it is there, then stop. The cap keeps a dropdown that never opens
        /// (a click that turned into a drag) from polling forever.
        ///
        /// It also owns the panel's hide-until-placed class (`ds-dropdown-tuner`, read by
        /// DropdownPopup.uss): on from the gesture, before the popup exists, and off again the moment
        /// this field's popup is placed or the poll gives up. A popup that is not a ds-dropdown's opens
        /// while no ds-dropdown is mid-placement, so it is never hidden. The poll runs on the PANEL
        /// ROOT's scheduler, not the field's, so a field removed mid-poll still finishes and still takes
        /// the class off, and a second field placing a popup on the same panel keeps it on until both
        /// are done. One limit carries over unchanged from 1.5.4: a panel whose scheduler never ticks (a
        /// world-space PanelRenderer in a player) can neither place nor reveal a popup.
        /// </summary>
        private sealed class DropdownTune
        {
            private readonly DropdownField _field;
            private readonly System.Action _tick;
            private VisualElement _panelRoot;
            private IVisualElementScheduledItem _item;
            private int _tries;
            private bool _pending;

            internal DropdownTune(DropdownField field)
            {
                _field = field;
                _tick = Tick;
            }

            internal void Begin()
            {
                var root = _field.panel?.visualTree;
                if (root == null) return;
                if (_pending && root != _panelRoot) Finish();    // the field changed panels mid-poll
                if (root != _panelRoot)
                {
                    _item?.Pause();
                    _item = null;
                    _panelRoot = root;
                }

                _tries = 0;   // a second gesture (down, then up) restarts the budget, never a second poll
                if (!_pending)
                {
                    _pending = true;
                    DsRuntimeShared.TuningPanels.Add(root);
                }
                root.AddToClassList(PANEL_TUNER_CLASS);

                if (_item == null) _item = root.schedule.Execute(_tick).Every(0);   // 0 = every frame
                else _item.Resume();
            }

            private void Tick()
            {
                if (TuneDropdownPopup(_field)) { Finish(); return; }
                if (++_tries < DROPDOWN_TUNE_MAX_TICKS) return;

                Finish();
                if (DesignSystemEvents.DropdownDiagnostics) DumpPanelScope(_field);
            }

            private void Finish()
            {
                _item?.Pause();
                if (!_pending) return;
                _pending = false;
                var tuning = DsRuntimeShared.TuningPanels;
                tuning.Remove(_panelRoot);
                if (_panelRoot != null && !tuning.Contains(_panelRoot))
                    _panelRoot.RemoveFromClassList(PANEL_TUNER_CLASS);
            }
        }

        // Where the trail goes cold: the popup never turned up at panel scope. Say what IS parented
        // there, in case a Unity version renames it or hangs it somewhere else entirely.
        private static void DumpPanelScope(DropdownField dd)
        {
            var tree = dd.panel?.visualTree;
            if (tree == null) { Debug.Log($"[dsdiag] tune '{dd.name}': gave up, dd has no panel"); return; }

            var sb = new System.Text.StringBuilder();
            sb.Append($"[dsdiag] tune '{dd.name}': GAVE UP after {DROPDOWN_TUNE_MAX_TICKS} ticks, ")
              .Append($"no '.{POPUP_CLASS}' at panel scope. Panel root children ({tree.childCount}):");
            for (int i = 0; i < tree.childCount; i++)
            {
                var c = tree[i];
                sb.Append($"\n    [{i}] <{c.GetType().Name}> name='{c.name}' classes=[")
                  .Append(string.Join(",", c.GetClasses())).Append(']');
            }
            Debug.Log(sb.ToString());
        }

        /// <summary>Returns true once the popup was found and tuned, so the poller can stop.</summary>
        private static bool TuneDropdownPopup(DropdownField dd)
        {
            var panel = dd.panel;
            if (panel == null) return false;

            // The menu attaches at panel scope (a SIBLING of any ds-root), one
            // fresh instance per open — search from the very top of the panel.
            var menu = panel.visualTree.Q(className: POPUP_CLASS);
            var outer = menu?.Q(className: POPUP_OUTER_CLASS);
            var scroll = menu?.Q<ScrollView>();
            if (outer == null || scroll == null) return false;   // not there YET; the poller retries

            if (DesignSystemEvents.DropdownDiagnostics)
                Debug.Log($"[dsdiag] tune '{dd.name}': popup FOUND, tuning");

            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            // Places the popup and sizes it to its CONTENT, preferring downward.
            //
            // The old version only ever measured the space BELOW the field, and clamped what it
            // found to a 72px floor. Two bugs fell out of that, and they compounded:
            //
            //   * A field with little room beneath it (low in the viewport, or in a short world-space
            //     panel) got a 72px popup — two rows — no matter how many choices it had. A 14-item
            //     theme picker rendered as a sliver.
            //   * That sliver was then FORCED downward, past the bottom edge of the panel. Unity's own
            //     GenericDropdownMenu.EnsureVisibilityInParent notices a popup hanging outside its
            //     container and shoves it back up, we re-assert downward on the next geometry pass, and
            //     whoever writes last wins. The visible result is a dropdown that opens UPWARD — which
            //     is the one thing this method existed to prevent.
            //
            // So: measure BOTH sides, size to the actual list, and flip only when flipping genuinely
            // buys room. Once the popup honestly fits where we put it, Unity's math agrees with ours
            // and stops fighting, which is what makes "opens downward" stick.
            void Apply()
            {
                if (outer.panel == null) return;   // menu already closed

                // EVERYTHING here is in the OVERLAY's local space. That is the space `outer.style.top`
                // and `outer.style.width` are written in, and the space `resolvedStyle` reports in, so
                // it is the only space in which the numbers can be compared to each other at all.
                //
                // They used to be mixed, and it cost us both halves of the world-space bug:
                //
                //   * `worldBound` is NOT pixels inside a world-space panel. The panel root carries a
                //     pixels-to-metres transform, so a laid-out 206x48 field reports a worldBound of
                //     roughly 2.1x0.4. Sizing a popup by comparing that against `resolvedStyle` — which
                //     stays in pixels — compares 0.4 to 42 and loses every time.
                //   * The overlay is stretched to the panel root, and with WorldSpaceSizeMode.Dynamic
                //     that root has no viewport to fill: it measures 0x0 (confirmed on a live exhibit:
                //     content 240x400, exhibit root 240x460, panel root 0x0). So there was no room
                //     anywhere, on any side, for any field.
                //
                // Either one alone flips a dropdown upward. WorldToLocal fixes the first by putting
                // foreign rects into the overlay's pixels; LaidOutContainerOf fixes the second by
                // measuring the room against the exhibit's own content box — the visible wall panel,
                // and the only thing a world-space popup can sensibly be asked to stay inside.
                //
                // On a screen panel WorldToLocal is a pure translation and the panel root IS the
                // viewport, so both reduce to what this always did.
                Rect bounds = menu.contentRect;
                if (float.IsNaN(bounds.height) || bounds.height <= 1f)
                {
                    var container = LaidOutContainerOf(dd);
                    if (container.height <= 0.001f) return;   // nothing laid out yet; geometry will retry
                    bounds = menu.WorldToLocal(container);
                    if (float.IsNaN(bounds.height) || bounds.height <= 1f) return;
                }

                Rect field = menu.WorldToLocal(dd.worldBound);
                if (float.IsNaN(field.height) || field.height <= 1f) return;

                float chrome = outer.resolvedStyle.paddingTop + outer.resolvedStyle.paddingBottom
                             + outer.resolvedStyle.borderTopWidth + outer.resolvedStyle.borderBottomWidth;
                if (float.IsNaN(chrome) || chrome < 0f) chrome = 10f;

                // What the list WANTS: its natural height, capped so a 40-item list scrolls instead of
                // towering. Read from the content container, which keeps its full height even once the
                // ScrollView clips it — so this does not collapse to the cap we set on the last pass.
                float contentH = scroll.contentContainer.resolvedStyle.height;
                if (float.IsNaN(contentH) || contentH < 1f)
                    contentH = Mathf.Max(1, dd.choices?.Count ?? 1) * DROPDOWN_POPUP_ROW_HEIGHT;
                float wantH = Mathf.Min(DROPDOWN_POPUP_MAX_HEIGHT, contentH + chrome);

                float roomBelow = bounds.yMax - field.yMax - DROPDOWN_POPUP_GAP - DROPDOWN_POPUP_EDGE_MARGIN;
                float roomAbove = field.yMin - bounds.yMin - DROPDOWN_POPUP_GAP - DROPDOWN_POPUP_EDGE_MARGIN;

                // Downward unless upward is genuinely roomier. A dropdown that fits below always opens
                // below, which is what people expect; one that fits NEITHER side opens toward whichever
                // side is less bad rather than committing to a sliver in the wrong direction.
                bool down = roomBelow >= wantH || roomBelow >= roomAbove;
                float room = Mathf.Max(0f, down ? roomBelow : roomAbove);

                // Never taller than the list, never taller than the room, and only ever shorter than the
                // 72px floor when the list itself is shorter (a 2-choice dropdown is 2 rows, not 3).
                float h = Mathf.Clamp(Mathf.Min(wantH, room), Mathf.Min(wantH, DROPDOWN_POPUP_MIN_HEIGHT),
                                      DROPDOWN_POPUP_MAX_HEIGHT);

                // WIDTH: at least the field, but free to grow to fit the longest item, capped by the
                // room to its right. This is what a native <select> does, and pinning the popup to the
                // field's width instead is what turned "Medium" into "Me.." in every narrow dropdown
                // (the showcase's 110px quality pickers). `auto` lets the layout measure the items for
                // us rather than us guessing at text widths.
                float maxW = Mathf.Max(field.width, bounds.xMax - field.xMin - DROPDOWN_POPUP_EDGE_MARGIN);

                outer.style.left = field.xMin;
                outer.style.top = down ? field.yMax + DROPDOWN_POPUP_GAP
                                       : field.yMin - DROPDOWN_POPUP_GAP - h;
                outer.style.width = StyleKeyword.Auto;    // hug the widest item...
                outer.style.minWidth = field.width;       // ...never narrower than the field...
                outer.style.maxWidth = maxW;              // ...never off the right edge
                outer.style.height = StyleKeyword.Auto;   // hug the item list...
                outer.style.maxHeight = h;                // ...up to whatever we just decided it may have
                scroll.style.maxHeight = Mathf.Max(32f, h - chrome);

                if (DesignSystemEvents.DropdownDiagnostics)
                {
                    var tree = dd.panel?.visualTree;
                    Debug.Log(
                        $"[dsdiag] tune '{dd.name}' ({(dd.choices?.Count ?? 0)} choices)\n" +
                        $"  panelRoot   layout={tree?.layout}  worldBound={tree?.worldBound}\n" +
                        $"  menu        contentRect={menu.contentRect}  worldBound={menu.worldBound}\n" +
                        $"  dd          layout={dd.layout}  worldBound={dd.worldBound}\n" +
                        $"  container   {LaidOutContainerOf(dd)}\n" +
                        $"  => bounds={bounds}  field={field}\n" +
                        $"  chrome={chrome:F1} contentH={contentH:F1} wantH={wantH:F1}\n" +
                        $"  roomBelow={roomBelow:F1} roomAbove={roomAbove:F1} DOWN={down} h={h:F1} maxW={maxW:F1}");
                }
            }

            // GenericDropdownMenu re-runs its own EnsureVisibilityInParent on
            // scroll-geometry changes and writes left/top/height back, so
            // re-assert after every outer-container layout pass. Once the
            // capped popup genuinely fits below the field, Unity's own math
            // lands on the same values and the layout settles.
            // The GeometryChangedEvent hook is registered ONCE per menu element, because re-registering
            // it would stack a second identical callback on every tune.
            bool firstTune = !menu.ClassListContains(POPUP_TUNED_CLASS);
            if (firstTune)
                outer.RegisterCallback<GeometryChangedEvent>(_ => Apply());

            Apply();

            // The EVENT is raised every time — deliberately NOT gated on "is this menu new". That gate
            // was a bug waiting to happen: it assumed Unity builds a fresh popup element per open, and
            // if any Unity version instead REUSES one, the marker class survives, the gate reads false
            // forever after the first open, and a host that themes by hand silently stops being told the
            // popup exists. Handlers are expected to be idempotent (the showcase's re-stamps the same
            // inline styles), which is a far cheaper contract than a liveness assumption about someone
            // else's element pooling.
            DesignSystemEvents.RaiseDropdownPopupOpened(dd, menu);

            // REVEAL, and only now. DropdownPopup.uss keeps the popup at `opacity: 0` from the frame
            // Unity creates it until this class lands, so the first frame anyone actually sees is one
            // we have already placed AND the host has already painted. Adding it any earlier — before
            // Apply, or before the event — puts Unity's default position or the base palette on screen
            // for a frame, which is exactly the flicker this exists to kill. It goes last for that
            // reason, and it doubles as the "already hooked" marker read above.
            if (firstTune) menu.AddToClassList(POPUP_TUNED_CLASS);

            if (DesignSystemEvents.DropdownDiagnostics)
                Debug.Log($"[dsdiag] raised DropdownPopupOpened for '{dd.name}' " +
                          $"to {DesignSystemEvents.DropdownPopupSubscribers} subscriber(s)");

            return true;
        }

        /// <summary>
        /// The OUTERMOST ancestor of <paramref name="el"/> that actually has a size, in WORLD
        /// coordinates. Exists for world-space panels, where the usual "the panel root is the viewport,
        /// so it bounds everything" assumption is false: a PanelRenderer's root measures 0x0 and would
        /// hand out no room at all.
        ///
        /// The epsilon is deliberately tiny rather than a pixel: world-panel rects are in metres, and a
        /// whole exhibit is only a few units tall. The one thing this has to reject is the exactly-zero
        /// panel root.
        /// </summary>
        private static Rect LaidOutContainerOf(VisualElement el)
        {
            var best = new Rect();
            for (var p = el.parent; p != null; p = p.parent)
            {
                var wb = p.worldBound;
                if (float.IsNaN(wb.height) || wb.height <= 0.001f) continue;   // the degenerate panel root
                best = wb;   // keep climbing: the LAST valid one is the outermost
            }
            return best;
        }

        // ──────────────────────────────────────────────────────────────────
        // Auto-attach: every UIDocument in the project gets the runtime
        // without per-prefab inspector wiring. Re-scan on every scene load
        // so Activator-spawned UIDocuments are covered.
        // ──────────────────────────────────────────────────────────────────

        /*
         * To child classes,
         * add a method with [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] attribute
         * and call RegisterAutoAttach method from there
         * to be able to register the auto attach method.
         * Otherwise, you will need to add your component manually to your GameObjects.
         *
         * The component type is ALWAYS registered, so an explicit AttachToAll() keeps working. Only the
         * scene-load hook is optional: DS_NO_AUTO_ATTACH compiles it out, AutoAttach = false mutes it.
         */
        protected static void RegisterAutoAttach(System.Type runtimeComponentType)
        {
            s_componentTypeMap[typeof(TComponent)] = runtimeComponentType;
#if !DS_NO_AUTO_ATTACH
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
#endif
        }

#if !DS_NO_AUTO_ATTACH
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (AutoAttach) AttachToAll();
        }
#endif

        public static void AttachToAll()
        {
            if (!s_componentTypeMap.TryGetValue(typeof(TComponent), out var runtimeType))
                return;

            // The parameterless overload only exists from 6000.4, which is also where the
            // FindObjectsSortMode overload turns CS0618: each branch is the warning-free
            // form for its range (hence 6000_4 here, not this package's usual 6000_5).
#if UNITY_6000_4_OR_NEWER
            var docs = FindObjectsByType<TComponent>();
#else
            var docs = FindObjectsByType<TComponent>(FindObjectsSortMode.None);
#endif
            foreach (var doc in docs)
            {
                if (doc == null || doc.gameObject.GetComponent(runtimeType) != null)
                    continue;
                doc.gameObject.AddComponent(runtimeType);
            }
        }
    }

    /// <summary>A live behaviour that SetSpinning can hand a newly started spinner to.</summary>
    internal interface IDsSpinnerHost
    {
        void TrackSpinner(VisualElement el);
    }

    /// <summary>
    /// Runtime state shared by BOTH backends. The behaviour base is generic, so a static declared on it
    /// exists once per closed type (UIDocument and PanelRenderer apart), the same trap
    /// DesignSystemEvents documents; what one backend starts, the other must be able to see.
    /// </summary>
    internal static class DsRuntimeShared
    {
        /// <summary>Every enabled behaviour, of either backend.</summary>
        internal static readonly List<IDsSpinnerHost> SpinnerHosts = new();

        /// <summary>
        /// One entry per ds-dropdown popup being placed right now: the panel root that carries
        /// `ds-dropdown-tuner` for it. Several fields on one panel add the root several times, and the
        /// class comes off only when the last of them is done.
        /// </summary>
        internal static readonly List<VisualElement> TuningPanels = new();

        // Enter Play Mode with domain reload disabled keeps statics alive across sessions; start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SpinnerHosts.Clear();
            TuningPanels.Clear();
        }
    }
}
