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
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class DesignSystemBehaviourBase<TComponent> : MonoBehaviour where TComponent : Component
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

        private const float DROPDOWN_POPUP_MAX_HEIGHT  = 320f; // web-select feel: tall lists scroll instead of towering
        private const float DROPDOWN_POPUP_MIN_HEIGHT  = 72f;  // never degenerate into an unusable sliver
        private const float DROPDOWN_POPUP_GAP         = 4f;   // field bottom -> popup top
        private const float DROPDOWN_POPUP_EDGE_MARGIN = 8f;   // popup bottom -> panel bottom breathing room
        
        private IVisualElementScheduledItem _spinTask;
        private float _spinAngle;
        
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
        /// Spinners are deliberately NOT here: <see cref="StartSpinners"/> owns a per-instance
        /// schedule handle, so it belongs to the MonoBehaviour lifecycle, not to a root sweep.
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
            EnsureAll(root);
            StartSpinners(root);

            // Periodic re-scan: ScreenBase and similar consumers clone screen
            // templates lazily when the user navigates to them. The first
            // EnsureAll pass only sees what's in the tree at attach time —
            // toggles cloned in later (e.g. Settings on first open) would
            // otherwise stay knob-less and render as a flat pill.
            // 250 ms is fast enough that the user never notices a missing knob
            // after a screen transition, and cheap enough to ignore (a Query
            // with an existence check on already-knobbed toggles is O(N) over
            // the small number of ds-toggle elements). Idempotent — every
            // helper no-ops if its work is already done.
            root.schedule.Execute(() => EnsureAll(root)).Every(250);
        }

        protected virtual void OnDisable()
        {
            _spinTask?.Pause();
            _spinTask = null;
            CancelInvoke();
        }

        private void StartSpinners(VisualElement root)
        {
            // Rotate every element carrying `.is-spinning`, regardless of whether
            // it's a `.ds-spinner` ring, a `.ds-icon` glyph (e.g. a refresh icon
            // turning into a loading indicator on a button), or any other
            // element a screen wants to spin. The class is purely behavioral —
            // visual styling stays on whatever class the element already has.
            _spinTask = root.schedule.Execute(() =>
            {
                _spinAngle = (_spinAngle + 6f) % 360f;
                root.Query(className: SPINNER_ACTIVE_CLASS).ForEach(el =>
                {
                    el.style.rotate = new StyleRotate(new Rotate(_spinAngle));
                });
            }).Every(16);
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
                host.Add(ghost);
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
            root.Query<DropdownField>(className: DS_DROPDOWN_CLASS).ForEach(dd =>
            {
                if (dd.ClassListContains(DROPDOWN_WIRED_CLASS)) return;
                dd.AddToClassList(DROPDOWN_WIRED_CLASS);
                WireDropdownMenu(dd);
            });
        }

        public static void WireDropdownMenu(DropdownField dd)
        {
            // The popup doesn't exist until BasePopupField's own handler runs,
            // so tune one tick later, from every gesture that can open it.
            // TrickleDown: the field stops propagation once it opens the menu,
            // so a bubble-phase handler would never see the gesture. Down AND
            // up are both registered because the open gesture differs across
            // input paths (and Unity versions); a tune that finds no popup —
            // or one already tuned — is a no-op.
            dd.RegisterCallback<PointerDownEvent>(_ => ScheduleDropdownTune(dd), TrickleDown.TrickleDown);
            dd.RegisterCallback<PointerUpEvent>(_ => ScheduleDropdownTune(dd), TrickleDown.TrickleDown);
            dd.RegisterCallback<NavigationSubmitEvent>(_ => ScheduleDropdownTune(dd), TrickleDown.TrickleDown);
        }

        private static void ScheduleDropdownTune(DropdownField dd) =>
            dd.schedule.Execute(() => TuneDropdownPopup(dd));

        private static void TuneDropdownPopup(DropdownField dd)
        {
            var panel = dd.panel;
            if (panel == null) return;

            // The menu attaches at panel scope (a SIBLING of any ds-root), one
            // fresh instance per open — search from the very top of the panel.
            var menu = panel.visualTree.Q(className: POPUP_CLASS);
            var outer = menu?.Q(className: POPUP_OUTER_CLASS);
            var scroll = menu?.Q<ScrollView>();
            if (outer == null || scroll == null) return;

            scroll.mode = ScrollViewMode.Vertical;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;

            void Apply()
            {
                if (outer.panel == null) return;   // menu already closed
                var fieldWB = dd.worldBound;
                var menuWB = menu.worldBound;

                float top = fieldWB.yMax - menuWB.yMin + DROPDOWN_POPUP_GAP;
                float availBelow = menuWB.height - top - DROPDOWN_POPUP_EDGE_MARGIN;
                float capH = Mathf.Min(DROPDOWN_POPUP_MAX_HEIGHT,
                             Mathf.Max(DROPDOWN_POPUP_MIN_HEIGHT, availBelow));

                float chrome = outer.resolvedStyle.paddingTop + outer.resolvedStyle.paddingBottom
                             + outer.resolvedStyle.borderTopWidth + outer.resolvedStyle.borderBottomWidth;
                if (float.IsNaN(chrome) || chrome < 0f) chrome = 10f;

                outer.style.left = fieldWB.xMin - menuWB.xMin;
                outer.style.top = top;
                outer.style.width = fieldWB.width;
                outer.style.height = StyleKeyword.Auto;   // hug the item list...
                outer.style.maxHeight = capH;             // ...up to the cap
                scroll.style.maxHeight = Mathf.Max(32f, capH - chrome);
            }

            // GenericDropdownMenu re-runs its own EnsureVisibilityInParent on
            // scroll-geometry changes and writes left/top/height back, so
            // re-assert after every outer-container layout pass. Once the
            // capped popup genuinely fits below the field, Unity's own math
            // lands on the same values and the layout settles.
            if (!menu.ClassListContains(POPUP_TUNED_CLASS))
            {
                menu.AddToClassList(POPUP_TUNED_CLASS);
                outer.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            }
            Apply();
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
         */
        protected static void RegisterAutoAttach(System.Type runtimeComponentType)
        {
            s_componentTypeMap[typeof(TComponent)] = runtimeComponentType;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToAll();

        public static void AttachToAll()
        {
            if (!s_componentTypeMap.TryGetValue(typeof(TComponent), out var runtimeType))
                return;

            var docs = FindObjectsByType<TComponent>();
            foreach (var doc in docs)
            {
                if (doc == null || doc.gameObject.GetComponent(runtimeType) != null)
                    continue;
                doc.gameObject.AddComponent(runtimeType);
            }
        }
    }
}
