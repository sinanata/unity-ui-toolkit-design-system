using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime
{
    /// <summary>
    /// Runtime helpers for the ds-* design system. Auto-attaches to every
    /// UIDocument in the scene at load. Provides:
    ///   - Looping spinner rotation (USS transitions can't loop natively)
    ///   - Toggle-knob auto-injection: every <Toggle class="ds-toggle"> gets a
    ///     child <VisualElement class="ds-toggle__knob"> if one is missing
    ///   - Skeleton shimmer translation (sliding overlay)
    ///
    /// Authoring tip: hand-author the toggle knob in UXML when you can — it
    /// avoids a one-frame "no knob" flash during template clone. The runtime
    /// is the safety net for screens that didn't.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class DesignSystemRuntimeBase<TComponent> : MonoBehaviour where TComponent : Component
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
        
        private IVisualElementScheduledItem _spinTask;
        private float _spinAngle;
        
        protected abstract void OnEnable();

        protected void InitFor(VisualElement root)
        {
            if (root == null) return;
            EnsureToggleKnobs(root);
            EnsureSkeletonShimmers(root);
            EnsureDraggables(root);
            StartSpinners(root);

            // Periodic re-scan: ScreenBase and similar consumers clone screen
            // templates lazily when the user navigates to them. The first
            // EnsureToggleKnobs/Shimmers pass only sees what's in the tree at
            // attach time — toggles cloned in later (e.g. Settings on first
            // open) would otherwise stay knob-less and render as a flat pill.
            // 250 ms is fast enough that the user never notices a missing knob
            // after a screen transition, and cheap enough to ignore (a Query
            // with an existence check on already-knobbed toggles is O(N) over
            // the small number of ds-toggle elements). Idempotent — both
            // helpers no-op if the children already exist.
            root.schedule.Execute(() =>
            {
                EnsureToggleKnobs(root);
                EnsureSkeletonShimmers(root);
                EnsureDraggables(root);
            }).Every(250);
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
                var root = Root();
                if (root == null) return;

                ghost = new VisualElement();
                ghost.AddToClassList(DRAG_GHOST_CLASS);
                ghost.pickingMode = PickingMode.Ignore;
                var label = item.Q<Label>();
                ghost.Add(new Label(label != null ? label.text : "•"));
                root.Add(ghost);

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

                SetZone(null);
                ghost?.RemoveFromHierarchy();
                ghost = null;
            });
            return;

            VisualElement ZoneUnder(Vector2 pos)
            {
                var root = Root();
                if (root == null) return null;
                VisualElement found = null;
                root.Query(className: DROP_ZONE_CLASS).ForEach(z =>
                {
                    if (z.worldBound.Contains(pos)) found = z;
                });
                return found;
            }

            VisualElement Root() => item.panel != null ? item.panel.visualTree : null;

            void SetZone(VisualElement zone)
            {
                if (currentZone == zone) return;
                currentZone?.RemoveFromClassList(DRAG_OVER_CLASS);
                currentZone = zone;
                currentZone?.AddToClassList(DRAG_OVER_CLASS);
            }

            void PositionGhost(Vector2 pos)
            {
                if (ghost == null) return;
                ghost.style.left = pos.x - ghost.resolvedStyle.width / 2f;
                ghost.style.top = pos.y - ghost.resolvedStyle.height / 2f;
            }
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
