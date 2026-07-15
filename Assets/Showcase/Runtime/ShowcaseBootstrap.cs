using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using DesignSystem.Runtime.Behaviour;
using DesignSystem.Runtime.Behaviour.UIDocument;
using DesignSystem.Runtime.Theme.Applier;
using DesignSystem.Runtime.Theme.Data;
using DesignSystem.Runtime.Typography;
using Object = UnityEngine.Object;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Showcase.Runtime
{
    // Spawns the showcase + doc-overlay UIDocuments at runtime so the .unity
    // scene stays empty (one camera). Means the scene file has no MonoBehaviour
    // GUID references that could rot during refactors — the whole stack is
    // recreated programmatically every Play.
    public static class ShowcaseBootstrap
    {
        const string DEFAULT_PANEL_SETTINGS = "DefaultPanelSettings";
        const string SHOWCASE_RES_PATH = "DesignSystemShowcase";
        const string THEME_RES_PATH    = "UnityDefaultRuntimeTheme";
        const int    MOBILE_BREAKPOINT = 768;
        const string SCENE_NAME = "Showcase";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern float LoLDS_GetDevicePixelRatio();
#endif

        // Effective devicePixelRatio for panel scaling. WebGL has the most
        // surface area for the bug — the index.html sets canvas.width =
        // innerWidth * window.devicePixelRatio so Unity renders into a HiDPI
        // buffer for crisp text, which means Screen.width reports the BUFFER
        // pixel count, not CSS pixels. ConstantPixelSize without compensation
        // shrinks every component to 1/DPR of its declared size on Retina.
        //
        // Standalone Mac with macRetinaSupport=1 has the same shape: Screen
        // returns physical pixels, Screen.dpi returns physical PPI (~218 on a
        // 5K iMac). We approximate DPR as Screen.dpi/96 (96 dpi being the
        // Windows / CSS reference) and floor at 1 so non-HiDPI desktops
        // render unchanged.
        static float GetEffectiveDpr()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                float dpr = LoLDS_GetDevicePixelRatio();
                if (dpr > 0f) return dpr;
            }
            catch { /* fall through to Screen.dpi heuristic */ }
#endif
            float dpi = Screen.dpi;
            if (dpi <= 0f) return 1f;
            return Mathf.Max(1f, dpi / 96f);
        }

        // BEFORE the scene loads, because the design system auto-attaches to every UIDocument and wires
        // its dropdowns as the scene comes up — so a flag set any later than this misses the very logs
        // that say whether the wiring happened at all.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitDiagnostics()
        {
            // A WebGL build is the one place this stuff cannot be inspected: no inspector, no
            // debugger, and the UI is pixels on a canvas with no DOM behind it. `?dsdebug=1` makes
            // the build narrate itself to the browser console.
            //
            // It points at the FONT pipeline, because that is what you cannot otherwise see: the
            // Editor happily renders a font a build cannot. The dropdown popup tuner has its own
            // token now (`?dsdropdown=1`), so its per-frame chatter does not bury the thing you
            // actually opened the console to read.
            string url = Application.absoluteURL ?? string.Empty;

            DesignSystemEvents.DropdownDiagnostics = url.Contains("dsdropdown");
            ShowcaseFonts.Diagnostics = url.Contains("dsdebug");

            if (DesignSystemEvents.DropdownDiagnostics)
                Debug.Log("[dsdiag] dropdown diagnostics ON");

            if (ShowcaseFonts.Diagnostics)
                Debug.Log("[dsfont] font diagnostics ON");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Initialize()
        {
            // Skip when the active scene isn't the showcase — protects consumers
            // who pull Assets/Showcase/ into a multi-scene project from getting
            // the showcase overlaid on their first scene at app start.
            var currentScene = SceneManager.GetActiveScene();
            if (currentScene.name != SCENE_NAME)
            {
                Debug.Log($"[ShowcaseBootstrap] Active scene is '{currentScene.name}', but expected '{SCENE_NAME}'. " +
                          "ShowcaseBootstrap will not run. Confirm the Showcase scene is added to Build Settings and set as the active scene.");
                return;
            }

            var showcaseUxml = Resources.Load<VisualTreeAsset>(SHOWCASE_RES_PATH);
            if (showcaseUxml == null)
            {
                Debug.LogError($"[ShowcaseBootstrap] Could not load {SHOWCASE_RES_PATH}.uxml from Resources. " +
                               "Confirm Assets/Showcase/Resources/DesignSystemShowcase.uxml exists.");
                return;
            }

            EnsureInputSystem();

            // The PanelSettings need a Theme Style Sheet for default Unity
            // control styling (Label fonts, Button frames, Toggle frames).
            // Without it Unity logs "No Theme Style Sheet set" and most text
            // renders invisible. The TSS at Resources/ just imports
            // unity-theme://default — same as the file Unity auto-creates
            // the first time you make a PanelSettings asset in the editor.
            var theme = Resources.Load<ThemeStyleSheet>(THEME_RES_PATH);
            if (theme == null)
            {
                Debug.LogWarning($"[ShowcaseBootstrap] Could not load {THEME_RES_PATH}.tss from Resources. " +
                                 "Default control styling will be missing. " +
                                 "Confirm Assets/Showcase/Resources/UnityDefaultRuntimeTheme.tss exists.");
            }

            var showcaseGO = new GameObject("Showcase");
            var showcaseDoc = showcaseGO.AddComponent<UIDocument>();
            showcaseDoc.panelSettings = MakePanelSettings(sortingOrder: 0, name: "ShowcasePanelSettings", theme: theme);
            showcaseDoc.visualTreeAsset = showcaseUxml;

            // Showcase-only override stylesheet — adds the universal
            // colour-property transition + .theme-light token block. Loaded
            // AFTER the design system stylesheet (which the UXML imports
            // first) so its rules win specificity ties.
            var themeOverride = Resources.Load<StyleSheet>("ShowcaseTheme");
            if (themeOverride != null && showcaseDoc.rootVisualElement != null)
                showcaseDoc.rootVisualElement.styleSheets.Add(themeOverride);

            // Focus-ring stylesheet — adds `:focus` rules so keyboard / gamepad
            // users can see which control is selected. Loaded last so it can
            // override the variant-specific `border-color` set in Buttons.uss.
            var focusRing = Resources.Load<StyleSheet>("ShowcaseFocusRing");
            if (focusRing != null && showcaseDoc.rootVisualElement != null)
                showcaseDoc.rootVisualElement.styleSheets.Add(focusRing);

            // Mobile flip + promo-button wiring + theme-toggle wiring +
            // panel-root popup stylesheet load — all run inside
            // `schedule.Execute` because `rootVisualElement.parent` isn't
            // available until UIDocument has attached the document to its
            // panel during the first OnEnable pass.
            //
            // The popup stylesheet is loaded onto `panel.visualTree` (the
            // parent of rootVisualElement) so it reaches Unity's
            // GenericDropdownMenu — which is added under
            // `GetRootVisualContainer()` (verified against
            // UnityCsReference/Modules/UIElements/Core/Controls/
            // GenericDropdownMenu.cs) — a SIBLING of rootVisualElement, not
            // a descendant. Stylesheets imported via the UXML's <Style> tag
            // scope to rootVisualElement's subtree only, so popup rules
            // placed there never matched the popup. We pull up ONLY this
            // small dedicated stylesheet (not the full DesignSystem.uss +
            // ShowcaseTheme.uss bundle, which had unintended layout effects
            // on the popup itself when loaded panel-wide).
            showcaseDoc.rootVisualElement?.schedule.Execute(() =>
            {
                var root = showcaseDoc.rootVisualElement;
                if (root == null) return;

                InstallPanelScopeSheets(root.parent);

                // A second, permanent marker on the root, purely so the theme-swap guard can be
                // written as a COMPOUND selector. `.showcase-root.ds-no-transition` scores 512, which
                // is what lets it zero `--transition-fast` out from under a ThemeData's own token
                // block — that block is `:root`-scoped (256) and gets added last, so a single-class
                // guard would lose the tie on load order at exactly the wrong moment. See
                // ShowcaseTheme.uss.
                root.AddToClassList("showcase-root");

                // Fresh-slate state: a previous scene visit may have left
                // _activeOverride set to a palette whose VisualElement refs
                // are stale. The new tree starts at the design-system
                // defaults, so reset the cache here before wiring.
                _activeOverride = null;
                _activeThemeAsset = null;
                _themeLight = false;
                CodigrateThemeApplier.ResetAll();   // drop dead-tree stamp registries
                _flatRoot = root;
                _themeToggleLocked = false;
                _themeDropdownValue = DEFAULT_OPTION;
                _themeStatusText = null;

                // A scene reloaded while the corridor was up would otherwise come back with the flat
                // page believing it is still hidden, and it would never repaint again.
                _flatVisible = true;
                _flatStale = false;

                // Scheduled against the OLD root, which is now a dead tree.
                _restoreTransitions = null;

                // Idempotent across scene reloads: the event is static and so is the handler, so a
                // straight `+=` would stack a second subscription every visit.
                DesignSystemEvents.DropdownPopupOpened -= OnDropdownPopupOpened;
                DesignSystemEvents.DropdownPopupOpened += OnDropdownPopupOpened;

                ApplyMobileClass(root);
                WirePromoLinks(root);
                WireThemeToggle(root);
                WireThemeProvider(root);
                WireCodigrateLink(root);
                WireRandomize(root);
                WireDrawerDemos(root);
                WireAutoHideScroll(root);
                ShowcaseFonts.Wire(root);
                UpdateThemingSection(root);   // show the default theme's USS before anything is picked
                SetInitialFocus(root);

                // Re-evaluate the mobile class whenever the panel root resizes
                // (browser window resize, mobile rotation, devtools toggle).
                // GeometryChangedEvent fires on every resolved layout pass so
                // the class flips below 768 CSS-px and back without a reload.
                root.RegisterCallback<GeometryChangedEvent>(_ => ApplyMobileClass(root));
            }).StartingIn(0);

            var overlayGO = new GameObject("ShowcaseDocOverlay");
            var overlayDoc = overlayGO.AddComponent<UIDocument>();
            overlayDoc.panelSettings = MakePanelSettings(sortingOrder: 1, name: "DocOverlayPanelSettings", theme: theme);

            var overlay = overlayGO.AddComponent<ShowcaseDocOverlay>();
            overlay.AttachTo(showcaseDoc, overlayDoc);

            // The DesignSystemBehaviour auto-attaches via SceneManager.sceneLoaded
            // which may fire BEFORE our AfterSceneLoad init — so the GameObjects
            // we just created would miss the initial attach. Nudge it manually.
            // The runtime is idempotent; calling twice is a no-op.
            DesignSystemBehaviour.AttachToAll();

#if UNITY_6000_5_OR_NEWER
            // World-space gallery mode + the switch that toggles into it. Only
            // on 6000.5+, where PanelRenderer + world-space PanelSettings exist.
            SetupWorldMode(showcaseDoc, overlayDoc, showcaseUxml, theme, themeOverride);
#endif
        }

#if UNITY_6000_5_OR_NEWER
        // The live corridor, so the theme handlers can mirror the flat page's
        // day/night / codigrate / random palette onto the exhibits.
        static WorldSpaceCorridor _corridor;

        // Build the world-space corridor + the mode switch and cross-wire them.
        // The primary Screen/World switch is a band inserted into the page right
        // below the hero; a matching exit lives in the in-corridor HUD (since the
        // page — and its switch — are hidden while walking the gallery). The
        // corridor builds its geometry lazily on first entry, so this is cheap.
        static void SetupWorldMode(UIDocument showcaseDoc, UIDocument overlayDoc,
                                   VisualTreeAsset showcaseUxml, ThemeStyleSheet theme, StyleSheet themeOverride)
        {
            var dsUss = Resources.Load<StyleSheet>("UI/Styles/DesignSystem/DesignSystem");

            var corridor = WorldSpaceCorridor.Create(
                showcaseDoc, overlayDoc, showcaseUxml, theme, dsUss, themeOverride);
            _corridor = corridor;
            // Wire each exhibit's CLONED controls (theme toggle, codigrate
            // provider, randomize, drawers…) as its panel comes online — the
            // corridor invokes this once per exhibit.
            corridor.PanelReady = WireWorldExhibit;

            var hudGO = new GameObject("ShowcaseModeHud");
            var hudDoc = hudGO.AddComponent<UIDocument>();
            hudDoc.panelSettings = MakePanelSettings(sortingOrder: 20, name: "HudPanelSettings", theme: theme);

            Button screenBtn = null, worldBtn = null;
            ShowcaseModeHud hud = null;

            void Reflect(bool world)
            {
                screenBtn?.EnableInClassList("is-active", !world);
                worldBtn?.EnableInClassList("is-active", world);
                hud?.SetActive(world);
            }
            void SetMode(bool world)
            {
                if (world) corridor.Show();
                else       corridor.Hide();
                Reflect(world);
            }

            var (sBtn, wBtn) = BuildInPageModeToggle(showcaseDoc.rootVisualElement,
                                                     () => SetMode(false), () => SetMode(true));
            screenBtn = sBtn; worldBtn = wBtn;

            hud = ShowcaseModeHud.Create(hudDoc, dsUss, onExit: () => SetMode(false),
                                         onBuilt: root => { _hudRoot = root; PaintHud(); });

            // Esc in the corridor calls Hide() directly; mirror it onto the UI.
            corridor.Hidden += () => Reflect(false);
            Reflect(false);
        }

        // Insert the big segmented Screen/World switch as a page band directly
        // below the hero banner (its first sibling in the .ds-root scroll view).
        // Styled with the design system's own .ds-tabs / .ds-tab — the switch is
        // built from the components it advertises.
        static (Button screen, Button world) BuildInPageModeToggle(
            VisualElement root, System.Action onScreen, System.Action onWorld)
        {
            if (root == null) return (null, null);
            var scroll = root.Q<ScrollView>(className: "ds-root");
            if (scroll == null || scroll.Q("mode-toggle-band") != null) return (null, null);

            var band = new VisualElement { name = "mode-toggle-band" };
            band.AddToClassList("showcase-chrome");   // sales chrome, not an inspectable component
            band.style.alignItems = Align.Center;
            band.style.paddingTop = 20; band.style.paddingBottom = 20;
            band.style.paddingLeft = 24; band.style.paddingRight = 24;
            band.style.borderBottomWidth = 1;
            band.style.borderBottomColor = new Color(0.149f, 0.188f, 0.255f, 1f);

            // Short + NoWrap: the long original wrapped to two lines on
            // narrower windows, which read as broken chrome.
            var caption = new Label("VIEW AS A FLAT PAGE OR WALK THE 3D GALLERY");
            caption.AddToClassList("ds-caption");
            caption.style.color = new Color(0.404f, 0.439f, 0.522f);
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            caption.style.whiteSpace = WhiteSpace.NoWrap;
            caption.style.marginBottom = 12;
            band.Add(caption);

            var tabs = new VisualElement();
            tabs.AddToClassList("ds-tabs");
            tabs.style.paddingTop = 5; tabs.style.paddingBottom = 5;
            tabs.style.paddingLeft = 5; tabs.style.paddingRight = 5;

            var screenBtn = MakeModeTab("Screen Space", onScreen);
            var worldBtn  = MakeModeTab("World Space",  onWorld);
            tabs.Add(screenBtn);
            tabs.Add(worldBtn);
            band.Add(tabs);

            // Right after the hero (child 0 of the scroll content).
            scroll.Insert(Mathf.Min(1, scroll.childCount), band);
            return (screenBtn, worldBtn);
        }

        static Button MakeModeTab(string text, System.Action onClick)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.AddToClassList("ds-tab");
            b.style.paddingTop = 10; b.style.paddingBottom = 10;
            b.style.paddingLeft = 22; b.style.paddingRight = 22;
            b.style.fontSize = 14;
            return b;
        }

        // Wire the CLONED interactive controls on one world exhibit so the
        // corridor is fully live: theme toggle, codigrate theme provider,
        // randomize, external link, drawer demos, auto-hide scroll. The
        // harvested sections are a fresh UXML instantiation — none of the
        // flat page's wiring reaches them. Q() by name only matches controls
        // that exist on THIS exhibit's panel, so blanket-calling the full set
        // per panel is safe (everything else no-ops). Called exactly once per
        // exhibit by the corridor (the wired elements live in the persistent
        // content tree, which survives panel reloads).
        static void WireWorldExhibit(VisualElement panelRoot)
        {
            if (panelRoot == null) return;

            WireThemeToggle(panelRoot);
            WireThemeProvider(panelRoot);
            WireCodigrateLink(panelRoot);
            WireRandomize(panelRoot);
            WireDrawerDemos(panelRoot);
            WireAutoHideScroll(panelRoot);

            // Fonts too, or the corridor's typography exhibits render in the default face while
            // the flat page renders in the chosen one. Wire() also stamps the CURRENT family
            // onto this clone, which matters because an exhibit can be built long after the
            // visitor picked a typeface.
            ShowcaseFonts.Wire(panelRoot);

            // Stamp the canonical theme-control state onto the fresh clone so
            // it doesn't disagree with choices made before it was built.
            MirrorThemeControls(panelRoot, _themeLight);
            UpdateHexLabels(panelRoot, _themeLight);
            UpdateThemingSection(panelRoot);
        }
#endif

        static PanelSettings MakePanelSettings(int sortingOrder, string name, ThemeStyleSheet theme)
        {
            var panelSettingsAsset = Resources.Load<PanelSettings>(DEFAULT_PANEL_SETTINGS);
            var ps = panelSettingsAsset != null ? Object.Instantiate(panelSettingsAsset) : ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = name;
            if (theme != null) ps.themeStyleSheet = theme;

            // ConstantPixelSize so components render at their declared pixel
            // sizes regardless of viewport — what you see is what they ship
            // as in your own project. A designer hovering a `.ds-btn` wants
            // to see a 36px button at 36px, not a proportionally scaled
            // version of one (which is what ScaleWithScreenSize would give).
            //
            // The flex-wrap layout in the showcase UXML already reflows when
            // the viewport narrows, and `.mobile` (added by the bootstrap
            // when CSS-width < 768) flips spacing/touch-target tokens.
            //
            // Scale tracks devicePixelRatio because the WebGL template renders
            // into a HiDPI buffer (canvas.width = innerWidth × DPR) for crisp
            // text. Without this multiplier, 1 panel-px maps to 1 buffer-px
            // → 1/DPR CSS pixels, so a 36-px button shrinks to 18 / 12 CSS-px
            // on Retina (DPR 2) / iPhone (DPR 3). With ps.scale = DPR, panel
            // pixels track CSS pixels exactly: 36 panel-px → 36 CSS-px on
            // every device, while still rendered at native HiDPI sharpness.
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = GetEffectiveDpr();
            ps.sortingOrder = sortingOrder;
            ps.targetDisplay = 0;
            ps.clearColor = sortingOrder == 0;
            ps.colorClearValue = new Color(0.043f, 0.058f, 0.090f, 1f); // --color-bg
            return ps;
        }

        static void ApplyMobileClass(VisualElement root)
        {
            if (root == null) return;

            // Compare CSS pixels (not Screen.width, which is buffer pixels =
            // CSS × DPR on WebGL with matchWebGLToCanvasSize=true). On iPhone
            // 14 Pro Max with DPR 3, Screen.width=1290 buffer-px maps to 430
            // CSS-px — the mobile layout would never trigger if we compared
            // 1290 < 768. Using rootVisualElement.layout when it has resolved
            // (post-GeometryChangedEvent) gives true panel-coordinate width;
            // before then, fall back to Screen.width / panel.scale.
            float panelWidth = root.layout.width;
            if (panelWidth <= 0f || float.IsNaN(panelWidth))
            {
                float dpr = GetEffectiveDpr();
                panelWidth = Screen.width / Mathf.Max(1f, dpr);
            }

            bool mobile = panelWidth < MOBILE_BREAKPOINT;
            if (mobile && !root.ClassListContains("mobile")) root.AddToClassList("mobile");
            if (!mobile && root.ClassListContains("mobile")) root.RemoveFromClassList("mobile");
        }

        // Spawn the EventSystem + InputSystemUIInputModule that bridges
        // keyboard / gamepad input into UI Toolkit's NavigationMoveEvent /
        // NavigationSubmitEvent / NavigationCancelEvent dispatch. Without
        // this pair, only pointer events reach the panel — D-pad and Tab
        // keys do nothing.
        //
        // No actions are pre-assigned on the module: InputSystemUIInputModule
        // auto-calls AssignDefaultActions() in OnEnable when none are set
        // (com.unity.inputsystem 1.18, line 1646), wiring keyboard arrows +
        // Enter + Escape + gamepad D-pad + leftStick + South / East buttons
        // to Move / Submit / Cancel out of the box.
        //
        // Idempotent: if another scene already created an EventSystem (e.g.
        // a project that drops the showcase into an existing app), we leave
        // it alone.
        static void EnsureInputSystem()
        {
            if (EventSystem.current != null) return;

            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }

        // First focusable wins on bootstrap — without it, the user has to
        // press the gamepad / Tab key once before navigation has a starting
        // point, which makes the showcase feel inert on launch. Theme-toggle
        // is a good anchor: it's near the top, it's the most semantically
        // meaningful control, and focusing a Toggle reads cleanly with the
        // focus ring.
        static void SetInitialFocus(VisualElement root)
        {
            if (root == null) return;
            // Toggle and Button share VisualElement/Focusable, but C#'s ??
            // operator can't infer the common base — assign in steps.
            Focusable anchor = root.Q<Toggle>("theme-toggle");
            if (anchor == null) anchor = root.Q<Button>("promo-github");
            if (anchor == null) anchor = root.Query<Button>().First();
            anchor?.Focus();
        }

        // Wire the promo-banner buttons in DesignSystemShowcase.uxml to real
        // URLs. Application.OpenURL works in the WebGL build — clicking
        // opens a new browser tab. Leap of Legends is live on all three
        // stores (2026-07-05), so the old single "Wishlist" button is now a
        // store-per-platform row.
        static void WirePromoLinks(VisualElement root)
        {
            if (root == null) return;
            void Wire(string name, string url)
            {
                var b = root.Q<Button>(name);
                if (b != null) b.clicked += () => Application.OpenURL(url);
            }
            Wire("promo-github",    "https://github.com/sinanata/unity-ui-toolkit-design-system");
            Wire("promo-steam",     "https://store.steampowered.com/app/2269500/Leap_of_Legends/");
            Wire("promo-appstore",  "https://apps.apple.com/us/app/leap-of-legends/id6761757484");
            Wire("promo-playstore", "https://play.google.com/store/apps/details?id=com.exceptionly.leapoflegends");
        }

        // Hex pairs per swatch — first value is the dark-theme hex (matches
        // DesignTokens.uss), second is the light-theme hex (matches the
        // .theme-light block in Showcase/Resources/ShowcaseTheme.uss).
        // Keep in sync with both files when adjusting palettes.
        static readonly System.Collections.Generic.Dictionary<string, (string Dark, string Light)> SwatchHex =
            new System.Collections.Generic.Dictionary<string, (string, string)>
            {
                { "hex-primary",         ("#22C55E", "#16A34A") },
                { "hex-primary-hover",   ("#16A34A", "#15803D") },
                { "hex-secondary",       ("#3B82F6", "#2563EB") },
                { "hex-tertiary",        ("#A855F7", "#9333EA") },
                { "hex-warning",         ("#F59E0B", "#D97706") },
                { "hex-danger",          ("#EF4444", "#DC2626") },
                { "hex-text-primary",    ("#F2F4F7", "#0F172A") },
                { "hex-text-secondary",  ("#A1A7B3", "#475569") },
                { "hex-text-disabled",   ("#677085", "#94A3B8") },
                { "hex-bg",              ("#0B0F17", "#F8FAFC") },
                { "hex-surface",         ("#131A24", "#FFFFFF") },
                { "hex-surface-elev",    ("#1A2330", "#F1F5F9") },
                { "hex-border",          ("#263041", "#E2E8F0") },
            };

        // Active third-party / generated palette, when set. While non-null the
        // day/night toggle is suppressed (codigrate carries its own appearance
        // signal; randomize honours the toggle's last value at generation time
        // but doesn't re-apply on subsequent toggle flips).
        //
        // This stays the canonical DESCRIPTION of the palette whichever way it is painted:
        // the hex labels in the COLORS section read it, and so does the corridor.
        static CodigrateThemeApplier.ColorMap _activeOverride;

        // ── How a palette gets painted ──────────────────────────────────────
        //
        // There are two mechanisms, and which one runs depends on one thing: did the palette
        // exist at BUILD time?
        //
        //   Baked ThemeData -> add ONE stylesheet to the root. The var() cascade repaints the
        //                      whole tree on its own, which means every `:hover`, `:disabled`
        //                      and `:checked` rule in the design system re-resolves for free.
        //                      All 12 bundled codigrate palettes take this path.
        //
        //   ColorMap only   -> walk the tree and stamp inline styles on every element
        //                      (CodigrateThemeApplier). Read that file's header for why it has
        //                      to exist: Unity cannot set a `var(--…)` custom property at
        //                      runtime, and cannot compile a StyleSheet from a string in a
        //                      player build. Randomize invents its palette at RUNTIME, so there
        //                      is no sheet to add and no way to make one. It stays on this path.
        //
        // The two must never be live at once. An inline style outranks any stylesheet, so a
        // leftover stamp from a previous Randomize would sit on top of a freshly-applied baked
        // theme and the theme would appear to do nothing. PaintRoot enforces that.
        const string BAKED_THEME_RES = "Themes/";

        // The baked theme currently painting the showcase, or null when there is none: the
        // design-system default, or a Randomize palette, which can only ever be stamped inline.
        static ThemeData _activeThemeAsset;

        static ThemeData LoadBakedTheme(string paletteKey)
        {
            if (string.IsNullOrEmpty(paletteKey)) return null;

            // Baked by Design System > Bake Showcase Themes, keyed by the palette's own metadata
            // key. A miss is not an error — the palette just falls back to inline stamping, which
            // is what every palette did before the themes were baked. That fallback is the reason
            // a bake failure degrades the showcase instead of breaking it.
            return Resources.Load<ThemeData>(BAKED_THEME_RES + paletteKey);
        }

        // ── Panel scope: where the dropdown popup lives ─────────────────────
        //
        // Unity parents a GenericDropdownMenu under the PANEL root — a SIBLING of the document root,
        // not a descendant — so nothing attached to the document root can reach it. Two sheets have
        // to be pulled up to panel scope for the popup to look like it belongs to the design system:
        // its chrome, and the TOKENS that chrome now references.
        //
        // Both are safe to hoist because both are inert: DropdownPopup.uss only matches Unity's own
        // popup classes, and DesignTokens.uss is 72 custom-property declarations and nothing else.
        // The full DesignSystem.uss must never come up here — its component rules would start
        // matching the popup's internals and break its layout.
        //
        // Every panel needs its own attach: a world-space UI is one panel PER exhibit.
        public static void InstallPanelScopeSheets(VisualElement panelScope)
        {
            if (panelScope == null) return;

            Add("UI/Styles/DesignSystem/DesignTokens");   // base palette, so var() resolves...
            Add("UI/Styles/DesignSystem/DropdownPopup");  // ...for the chrome that references it

            void Add(string path)
            {
                var sheet = Resources.Load<StyleSheet>(path);
                if (sheet != null && !panelScope.styleSheets.Contains(sheet))
                    panelScope.styleSheets.Add(sheet);
            }
        }

        // Paint the CURRENT palette onto one root. ThemeRuntime remembers what each root wears, so
        // this needs no bookkeeping from the caller and is safe to re-run on a root that was just
        // rebuilt (which is exactly what a world exhibit hands us after a UI reload).
        public static void PaintRoot(VisualElement root)
        {
            if (root == null) return;

            PaintOne(root);

            // ...and again at panel scope, which is the ONLY way to colour the dropdown popup. The
            // popup is a sibling of `root`, so the theme sheet on `root` cannot reach it; but the
            // panel root is its parent, and custom properties INHERIT. Put the token block there and
            // the popup picks the palette up for free — every baked theme, not just dark and light.
            //
            // Only the CASCADE half runs here, and only the cascade half can. The inline stamper walks
            // a tree stamping elements one at a time; pointed at the panel root it would walk the
            // document root's whole subtree a second time and double-stamp all of it. And it would
            // still miss, because the popup does not exist yet — Unity builds it on open and destroys
            // it on close. A Randomize palette therefore reaches the popup from the other end
            // entirely: OnDropdownPopupOpened, below.
            var panelScope = root.panel?.visualTree;
            if (panelScope == null || panelScope == root) return;

            if (_activeThemeAsset) ThemeRuntime.Apply(panelScope, _activeThemeAsset);
            else                   ThemeRuntime.Clear(panelScope);
        }

        // Randomize is the one palette that cannot reach the popup through the cascade: it is invented
        // at runtime, and a player build cannot compile a StyleSheet from a string, so there is no token
        // block to install at panel scope. The design system offers the only opening there is — it tells
        // us the moment a popup exists — and we paint it by hand, exactly as the rest of the tree gets
        // painted under Randomize. Baked themes ignore this: their tokens are already on the panel root
        // and the popup inherited them before it was ever shown.
        static void OnDropdownPopupOpened(DropdownField field, VisualElement menu)
        {
            bool cascadeHasIt = _activeThemeAsset || _activeOverride == null;

            if (DesignSystemEvents.DropdownDiagnostics)
                Debug.Log($"[dsdiag] popupOpened '{field?.name}': themeAsset={(_activeThemeAsset ? _activeThemeAsset.name : "null")} " +
                          $"override={(_activeOverride == null ? "null" : "SET")} -> " +
                          $"{(cascadeHasIt ? "cascade paints it (no stamp)" : "STAMPING inline")}");

            if (cascadeHasIt) return;
            CodigrateThemeApplier.StampPopup(menu, _activeOverride, field?.index ?? -1);
        }

        static void PaintOne(VisualElement root)
        {
            if (_activeThemeAsset)
            {
                // An inline style outranks any stylesheet, so a leftover stamp from an earlier
                // Randomize would sit on top of the sheet and the theme would appear to do nothing.
                CodigrateThemeApplier.Revert(root);
                ThemeRuntime.Apply(root, _activeThemeAsset);
                return;
            }

            ThemeRuntime.Clear(root);

            if (_activeOverride != null) CodigrateThemeApplier.Apply(root, _activeOverride);
            else                         CodigrateThemeApplier.Revert(root);
        }

        // Canonical theme-control state, shared by the flat page and every
        // world exhibit clone. Theme mutations can originate from ANY root
        // (the flat page, the COLORS exhibit's toggle, the THEME PROVIDER
        // exhibit's dropdown); these fields are what MirrorThemeControls
        // stamps onto all the others so the clones never disagree.
        static VisualElement _flatRoot;
        static bool _themeToggleLocked;                       // codigrate palette active → day/night toggle disabled
        static string _themeDropdownValue = DEFAULT_OPTION;   // provider dropdown selection
        static string _themeStatusText;                       // provider status label text

        // Canonical day/night state. This used to be re-read off the root's class list
        // (`root.ClassListContains("theme-light")`), which stopped being safe the moment a
        // class-scoped theme could add and remove that very class: the class is now an OUTPUT of
        // the theme state, so it cannot also be the input.
        static bool _themeLight;

        // Fan the CURRENT theme state out to every surface: the flat page, the world exhibits (via
        // the corridor), and the cloned theme CONTROLS on all of them (toggle value + lock,
        // provider dropdown selection, hex labels, status text, the THEMING section's live USS).
        //
        // Every mutation funnels through here. A theme change can originate from ANY root — the
        // flat page, the COLORS exhibit's toggle, the THEME PROVIDER exhibit's dropdown — so the
        // callers set the canonical state and call this, rather than each painting its own root and
        // then asking everyone else to catch up. That is why this takes no `sourceRoot` any more:
        // there is no special case for "the root the user touched".
        // ── Theme swaps do not animate ──────────────────────────────────────
        //
        // Every ds- component declares its own colour transition so that HOVER fades. A theme
        // change moves those same properties, on every element, at once — and USS cannot tell the
        // two apart, because a transition fires on any change to a watched property and the cascade
        // has no idea WHY the value moved. So a theme swap silently inherited the hover animation:
        // on the order of a thousand concurrent transitions on the flat page alone.
        //
        // Each running transition pins a snapshot of the element's ComputedStyle, which is a set of
        // ref-counted native blocks from Allocator.Domain. Swap faster than they retire and the live
        // blocks stack instead of draining, straight into Unity's 262,144 ceiling and a frozen tab.
        //
        // Since the stylesheet cannot make the distinction, this does: `ds-no-transition` sets
        // `transition-property: none` on a root and all its descendants (ShowcaseTheme.uss), so the
        // swap's colours resolve instantly, and it comes back off shortly after. Taking it off cannot
        // retro-start anything — by then the colours have already settled and nothing is changing.
        const string NO_TRANSITION_CLASS = "ds-no-transition";

        // Long enough to outlast the style pass the swap lands in by a wide margin, short enough that
        // a hover a moment later still fades. A burst of toggles keeps re-arming it, so transitions
        // stay off for the whole burst — which is precisely the case that used to freeze.
        const long TRANSITION_HOLD_MS = 120;

        static IVisualElementScheduledItem _restoreTransitions;

        static void HoldTransitionsOff()
        {
            SetNoTransition(true);
            if (_flatRoot == null) return;

            // The flat root always exists and its panel always ticks, so it hosts the timer for every
            // root including the corridor's (removing a class needs no scheduler of its own).
            _restoreTransitions ??= _flatRoot.schedule.Execute(() => SetNoTransition(false));
            _restoreTransitions.ExecuteLater(TRANSITION_HOLD_MS);   // re-arm, cancelling any pending run
        }

        static void SetNoTransition(bool on)
        {
            Mark(_flatRoot);
#if UNITY_6000_5_OR_NEWER
            if (_corridor != null)
                foreach (var exhibitRoot in _corridor.ExhibitRoots) Mark(exhibitRoot);
#endif
            void Mark(VisualElement root)
            {
                if (root == null) return;
                if (on) root.AddToClassList(NO_TRANSITION_CLASS);
                else    root.RemoveFromClassList(NO_TRANSITION_CLASS);
            }
        }

        static void SyncThemeEverywhere()
        {
            // BEFORE anything repaints. This runs inside the toggle's event handler, so the class is
            // on the roots before the style pass that resolves the new colours ever runs.
            HoldTransitionsOff();

            if (_flatRoot != null)
            {
                // Do not repaint a page nobody can see. In world mode the corridor hides the flat
                // showcase with `display: none`, which stops it being LAID OUT and DRAWN — it does
                // not stop it being STYLED. So every theme change was still re-resolving the flat
                // page's ~990 elements and starting a fresh set of colour transitions on them,
                // entirely invisibly, on top of the corridor's own ~30 panels. That was half the
                // cost of a world-mode toggle, spent on pixels that do not exist.
                if (_flatVisible) SyncFlatPage();
                else              _flatStale = true;   // catch it up when it comes back
            }

            // The mode switch is NOT on the flat page in world mode — the corridor hides that whole
            // document, chrome and all, and the switch you can still see belongs to ShowcaseModeHud,
            // which is its own UIDocument on its own panel. It was in neither of the two lists this
            // method paints, so it kept whatever palette it was born with: pick a theme in the
            // corridor and everything re-themed around a Screen/World toggle that did not.
            PaintHud();

#if UNITY_6000_5_OR_NEWER
            if (_corridor == null) return;
            _corridor.ApplyScreenTheme(_themeLight);   // it reads the palette back through PaintRoot
            foreach (var exhibitRoot in _corridor.ExhibitRoots)
            {
                if (exhibitRoot == null) continue;
                UpdateHexLabels(exhibitRoot, _themeLight);
                UpdateThemingSection(exhibitRoot);
                MirrorThemeControls(exhibitRoot, _themeLight);
            }
#endif
        }

        // The world-mode HUD's document root (ShowcaseModeHud). Null until the HUD has built itself,
        // which happens a frame or more after boot.
        static VisualElement _hudRoot;

        static void PaintHud()
        {
            if (_hudRoot == null) return;

            PaintRoot(_hudRoot);
            ApplyThemeClass(_hudRoot, _themeLight);

            // The HUD floats OVER the corridor, so its root must never be filled. `ds-root--hud` is
            // what normally guarantees that, but it only beats the stylesheet — and the Randomize path
            // paints by stamping INLINE styles, which outrank any class rule. Left alone, picking
            // Randomize in world mode drops an opaque sheet of `--color-bg` across the whole gallery.
            // The corridor learned this on its own exhibit roots; the same applies here.
            _hudRoot.style.backgroundColor = Color.clear;
        }

        // Is the flat showcase on screen, and does it owe itself a repaint from a theme change that
        // landed while it was hidden?
        static bool _flatVisible = true;
        static bool _flatStale;

        static void SyncFlatPage()
        {
            _flatStale = false;

            PaintRoot(_flatRoot);

            // AFTER PaintRoot, not before. Painting a class-scoped theme adds its class, and
            // clearing one removes it — so the day/night class has to be re-asserted once the
            // sheets have settled, or a swap away from a light theme would leave day mode off
            // even though the toggle still says it is on.
            ApplyThemeClass(_flatRoot, _themeLight);

            UpdateHexLabels(_flatRoot, _themeLight);
            UpdateThemingSection(_flatRoot);
            MirrorThemeControls(_flatRoot, _themeLight);
        }

        // Called by the corridor as it hides and shows the flat page. Any theme changes made while
        // the page was hidden are folded into a single repaint here, on the way back in, so the user
        // never sees a stale page and the corridor never pays for one.
        public static void SetFlatPageVisible(bool visible)
        {
            if (_flatVisible == visible) return;
            _flatVisible = visible;

            if (visible && _flatStale && _flatRoot != null) SyncFlatPage();
        }

        // ── Per-root widget cache ───────────────────────────────────────────
        //
        // The theme-driven widgets on one root, resolved ONCE. A theme change fans out to the flat
        // page plus ~30 corridor exhibits, and each of those used to re-run about eighteen UQuery
        // lookups — roughly six hundred full subtree walks per click, nearly all of them searching a
        // panel that cannot contain what is being looked for. Only the COLORS exhibit has swatch
        // labels, only THEME PROVIDER has the dropdown, only THEMING has the USS box; the other
        // thirty pay thirteen whole-tree walks to rediscover that they have no swatches.
        //
        // The cache cannot go stale. These elements are cloned with the panel and never replaced,
        // and when a PanelRenderer rebuilds a root it hands back a NEW VisualElement, which is a new
        // key. Weak keys, so a discarded root's entry goes with it.
        sealed class ThemeControls
        {
            public Toggle Toggle;
            public DropdownField Dropdown;
            public Label Status;
            public Label ThemingActive;
            public Label ThemingUss;

            // Only the swatch labels this root actually has, so the common case is an O(1) skip.
            public readonly Dictionary<string, Label> Hex = new Dictionary<string, Label>();
        }

        static readonly ConditionalWeakTable<VisualElement, ThemeControls> _themeControls = new();

        static ThemeControls ControlsFor(VisualElement root) => _themeControls.GetValue(root, BuildThemeControls);

        static ThemeControls BuildThemeControls(VisualElement root)
        {
            var c = new ThemeControls
            {
                Toggle        = root.Q<Toggle>("theme-toggle"),
                Dropdown      = root.Q<DropdownField>("theme-provider-dropdown"),
                Status        = root.Q<Label>("theme-provider-status"),
                ThemingActive = root.Q<Label>("theming-active"),
                ThemingUss    = root.Q<Label>("theming-uss"),
            };

            foreach (var name in SwatchHex.Keys)
            {
                var label = root.Q<Label>(name);
                if (label != null) c.Hex[name] = label;
            }

            return c;
        }

        // Align the theme CONTROLS on `root` with the canonical state. Uses
        // SetValueWithoutNotify throughout — mirroring must never re-enter the
        // mutation handlers.
        static void MirrorThemeControls(VisualElement root, bool light)
        {
            var c = ControlsFor(root);

            if (c.Toggle != null)
            {
                c.Toggle.SetValueWithoutNotify(light);
                c.Toggle.SetEnabled(!_themeToggleLocked);
            }

            if (c.Dropdown != null && _themeDropdownValue != null &&
                c.Dropdown.value != _themeDropdownValue &&
                c.Dropdown.choices != null && c.Dropdown.choices.Contains(_themeDropdownValue))
                c.Dropdown.SetValueWithoutNotify(_themeDropdownValue);

            if (_themeStatusText != null && c.Status != null)
                c.Status.text = _themeStatusText;
        }

        // The day/night toggle in the COLORS section header. While an override palette is active the
        // toggle is disabled (MirrorThemeControls does it), so this only ever fires for a genuine
        // user swap between the two first-party moods.
        static void WireThemeToggle(VisualElement root)
        {
            if (root == null) return;
            var toggle = root.Q<Toggle>("theme-toggle");
            if (toggle == null) return;
            toggle.RegisterValueChangedCallback(evt =>
            {
                _themeLight = evt.newValue;
                SyncThemeEverywhere();
            });
        }

        // Put the `theme-light` class on the DOCUMENT ROOT — NOT on `.ds-root`, which is the
        // ScrollView one level below it inside the UXML. That distinction is load-bearing: the
        // document root is also where `DesignSystem.uss` lands and therefore where its `:root`
        // token block resolves, so `.theme-light` and `:root` end up matching the SAME element.
        // Unity scores them identically (both 256), which means neither wins on specificity and
        // load order decides — ShowcaseTheme.uss is added after DesignSystem.uss, so it wins, and a
        // theme sheet added later still wins over both. Move this class down onto `.ds-root` and
        // that stops being true: it would then match a DESCENDANT of the element `:root` resolves
        // on, and a rule matching an element beats a value the element merely inherited, so it
        // would shadow any `:root` theme for the whole subtree.
        static void ApplyThemeClass(VisualElement root, bool light)
        {
            if (light) root.AddToClassList("theme-light");
            else       root.RemoveFromClassList("theme-light");

            // Also on `panel.visualTree`: Unity's BasePopupField adds the dropdown popup as a
            // SIBLING of the document root, so a class set only here never reaches it and the popup
            // would stay dark while the rest of the page flips.
            var panelRoot = root.panel?.visualTree;
            if (panelRoot != null && panelRoot != root)
            {
                if (light) panelRoot.AddToClassList("theme-light");
                else       panelRoot.RemoveFromClassList("theme-light");
            }
        }

        static void UpdateHexLabels(VisualElement root, bool light)
        {
            var hex = ControlsFor(root).Hex;
            if (hex.Count == 0) return;   // not the COLORS exhibit, which is nearly every root

            // While an override palette is active, the swatches reflect that
            // palette's actual values rather than the design-system Dark / Light
            // dictionary. Once Revert runs and _activeOverride goes back to
            // null the dictionary path takes over again.
            if (_activeOverride != null)
            {
                UpdateHexLabelsFromOverride(hex);
                return;
            }

            foreach (var kv in SwatchHex)
                if (hex.TryGetValue(kv.Key, out var label))
                    label.text = light ? kv.Value.Light : kv.Value.Dark;
        }

        static void UpdateHexLabelsFromOverride(Dictionary<string, Label> hex)
        {
            var m = _activeOverride;
            SetHex(hex, "hex-primary",         m.Primary);
            SetHex(hex, "hex-primary-hover",   m.PrimaryHover);
            SetHex(hex, "hex-secondary",       m.Secondary);
            SetHex(hex, "hex-tertiary",        m.Tertiary);
            SetHex(hex, "hex-warning",         m.Warning);
            SetHex(hex, "hex-danger",          m.Danger);
            SetHex(hex, "hex-text-primary",    m.TextPrimary);
            SetHex(hex, "hex-text-secondary",  m.TextSecondary);
            SetHex(hex, "hex-text-disabled",   m.TextDisabled);
            SetHex(hex, "hex-bg",              m.Bg);
            SetHex(hex, "hex-surface",         m.Surface);
            SetHex(hex, "hex-surface-elev",    m.SurfaceElev);
            SetHex(hex, "hex-border",          m.Border);
        }

        static void SetHex(Dictionary<string, Label> hex, string name, Color color)
        {
            if (hex.TryGetValue(name, out var label)) label.text = CodigrateThemeApplier.ToHex(color);
        }

        // ── THEMING section ─────────────────────────────────────────────────
        //
        // Shows the ACTUAL stylesheet the showcase is painting with, regenerated live from the
        // ThemeData that is currently applied. This works at all because GenerateUssString is a
        // RUNTIME method, not an editor one: the very call the baker makes at edit time runs again
        // here, in the browser, on the same asset. What you read in the box is what is on the root.
        const string DEFAULT_THEME_RES = "UI/Themes/Dark";
        static ThemeData _defaultThemeAsset;

        static ThemeData DefaultThemeAsset =>
            _defaultThemeAsset ? _defaultThemeAsset : _defaultThemeAsset = Resources.Load<ThemeData>(DEFAULT_THEME_RES);

        static void UpdateThemingSection(VisualElement root)
        {
            if (root == null) return;

            var c = ControlsFor(root);
            var activeLabel = c.ThemingActive;
            var ussLabel    = c.ThemingUss;
            if (activeLabel == null && ussLabel == null) return;   // not the THEMING exhibit

            // Randomize has no asset BY CONSTRUCTION, so there is no USS to show. Saying so is more
            // honest than showing the design system's default block and implying it is what paints.
            if (!_activeThemeAsset && _activeOverride != null)
            {
                if (activeLabel != null) activeLabel.text = "Random (inline)";
                if (ussLabel != null)
                    ussLabel.text =
                        "/* No stylesheet exists for this palette.\n\n" +
                        "   It was generated a moment ago, at runtime, and a player build cannot\n" +
                        "   compile USS from a string. So it is stamped inline onto every element\n" +
                        "   instead, one by one, which is the thing every OTHER theme here avoids.\n\n" +
                        "   Pick a named theme above to see the real stylesheet. */";
                return;
            }

            var theme = _activeThemeAsset ? _activeThemeAsset : DefaultThemeAsset;
            if (!theme)
            {
                if (activeLabel != null) activeLabel.text = "None";
                if (ussLabel != null)    ussLabel.text = "";
                return;
            }

            if (activeLabel != null)
                activeLabel.text = _activeThemeAsset
                    ? $"{_themeDropdownValue}   {theme.Scope}"
                    : $"Design System Dark   {theme.Scope}";

            if (ussLabel != null)
                ussLabel.text = theme.GenerateUssString();
        }

        // Theme provider — fetches the codigrate theme list, lets the user
        // pick one or fall back to "Design System default". Selection swaps the
        // inline-color stamp on the showcase tree, sets the toggle to match
        // the palette's reported appearance ("light"/"dark"), and disables the
        // toggle so the codigrate palette stays the source of truth until the
        // user picks "Design System default" again.
        //
        // "Random palette" is a permanent dropdown entry that runs the same
        // randomize path the button uses. Two reasons: (1) Unity's
        // DropdownField doesn't fire its change callback for re-selections of
        // the current value, so the button is the only way to RE-ROLL — but
        // having the entry in the dropdown gives a discoverable revert path
        // (Random ↔ Codigrate ↔ Default) without needing extra buttons.
        const string DEFAULT_OPTION = "Design System default";
        const string RANDOM_OPTION  = "Random palette";
        static List<CodigrateThemeProvider.ThemeListing> _codigrateListings;

        static void WireThemeProvider(VisualElement root)
        {
            if (root == null) return;
            var dropdown = root.Q<DropdownField>("theme-provider-dropdown");
            if (dropdown == null) return;
            var status = root.Q<Label>("theme-provider-status");

            // Default state: two stock entries until the network fetch returns.
            // Selecting "Random palette" works immediately; the codigrate
            // entries land in between once the list loads.
            dropdown.choices = new List<string> { DEFAULT_OPTION, RANDOM_OPTION };
            dropdown.index = 0;

            // The list is fetched once and shared: this method now also runs
            // for the world exhibit's CLONE of the dropdown, and a second
            // network fetch per clone would be wasted.
            if (_codigrateListings != null)
            {
                PopulateDropdownChoices(dropdown);
                if (status != null) status.text = $"{_codigrateListings.Count} themes by Codigrate available.";
            }
            else
            {
                if (status != null) status.text = "Loading codigrate themes...";
                CodigrateThemeProvider.FetchList((list, error) =>
                {
                    if (error != null || list == null)
                    {
                        if (status != null) status.text = "Codigrate themes unavailable. Random palette still works.";
                        Debug.LogWarning($"[ShowcaseBootstrap] Codigrate list fetch failed: {error}");
                        return;
                    }

                    _codigrateListings = list;
                    PopulateDropdownChoices(dropdown);
                    if (status != null) status.text = $"{list.Count} themes by Codigrate available.";
                });
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var name = evt.newValue;
                // Mirroring writes with SetValueWithoutNotify, so a change
                // event here is always a real user pick on THIS dropdown.
                if (name == DEFAULT_OPTION)
                {
                    ClearOverride(root);
                    return;
                }
                if (name == RANDOM_OPTION)
                {
                    DoRandomize(root);
                    return;
                }
                if (_codigrateListings == null) return;
                var listing = _codigrateListings.Find(l => l.Name == name);
                if (listing == null) return;

                if (status != null) status.text = $"Loading {listing.Name}...";
                CodigrateThemeProvider.FetchPalette(listing, (palette, paletteError) =>
                {
                    if (paletteError != null || palette == null)
                    {
                        if (status != null) status.text = $"Failed to load {listing.Name}.";
                        Debug.LogWarning($"[ShowcaseBootstrap] Codigrate palette fetch failed for {listing.Name}: {paletteError}");
                        return;
                    }
                    ApplyCodigratePalette(root, palette);
                    if (status != null) status.text = $"{palette.Name} · {palette.Appearance}";
                });
            });
        }

        // Build the option list from the fetched listings and re-assert the
        // canonical selection (assigning `choices` can blank the field).
        static void PopulateDropdownChoices(DropdownField dropdown)
        {
            var choices = new List<string> { DEFAULT_OPTION };
            foreach (var l in _codigrateListings) choices.Add(l.Name);
            choices.Add(RANDOM_OPTION);
            dropdown.choices = choices;
            if (_themeDropdownValue != null && choices.Contains(_themeDropdownValue))
                dropdown.SetValueWithoutNotify(_themeDropdownValue);
        }

        static void ApplyCodigratePalette(VisualElement root, CodigrateThemeProvider.ThemePalette palette)
        {
            // The ColorMap stays the canonical DESCRIPTION of the palette (the hex labels read it),
            // and the baked asset is how it gets PAINTED. A miss on the lookup is not a failure:
            // the palette simply falls back to inline stamping, exactly as it did before the themes
            // were baked, so a bad bake degrades the showcase rather than breaking it.
            _activeOverride     = CodigrateThemeApplier.FromCodigrate(palette);
            _activeThemeAsset   = LoadBakedTheme(palette.Key);
            _themeToggleLocked  = true;
            _themeDropdownValue = palette.Name;
            _themeStatusText    = $"{palette.Name} · {palette.Appearance}";

            // The palette carries its own appearance, so it drives the day/night state rather than
            // reading it. MirrorThemeControls pushes this onto the toggle and disables it.
            _themeLight = string.Equals(palette.Appearance, "light", StringComparison.OrdinalIgnoreCase);

            SyncThemeEverywhere();
        }

        static void ClearOverride(VisualElement root)
        {
            _activeOverride     = null;
            _activeThemeAsset   = null;
            _themeToggleLocked  = false;
            _themeDropdownValue = DEFAULT_OPTION;
            _themeStatusText    = _codigrateListings != null
                ? $"{_codigrateListings.Count} themes by Codigrate available."
                : null;

            // A codigrate palette may have left the mood in the other state. The toggle on the
            // interacted root holds the user's last choice; the THEME PROVIDER exhibit's panel has
            // no toggle of its own, so keep whatever we already had in that case.
            var toggle = root?.Q<Toggle>("theme-toggle");
            _themeLight = toggle?.value ?? _themeLight;

            SyncThemeEverywhere();
        }

        // Opens the public Codigrate theme catalogue. The link sits beside the
        // theme dropdown so users who like a particular theme can read its
        // story / pick up the matching JetBrains / VSCode / Ghostty version.
        static void WireCodigrateLink(VisualElement root)
        {
            if (root == null) return;
            var btn = root.Q<Button>("theme-codigrate-link");
            if (btn == null) return;
            btn.clicked += () => Application.OpenURL(CodigrateThemeProvider.SHOWCASE_URL);
        }

        // Randomize button: shares the DoRandomize path with the "Random
        // palette" dropdown entry. Clicking the button repeatedly RE-ROLLS
        // (each click produces a fresh palette in the toggle's current mood);
        // selecting "Random palette" from the dropdown is the same entrypoint
        // for users who never noticed the button.
        static void WireRandomize(VisualElement root)
        {
            if (root == null) return;
            var btn = root.Q<Button>("theme-randomize");
            if (btn == null) return;
            btn.clicked += () => DoRandomize(root);
        }

        static void DoRandomize(VisualElement root)
        {
            // Randomize can be triggered from the THEME PROVIDER exhibit, whose panel carries no
            // theme-toggle — keep the current mood in that case.
            var toggle = root?.Q<Toggle>("theme-toggle");
            _themeLight = toggle?.value ?? _themeLight;

            // THE ONE PATH THAT STILL STAMPS INLINE STYLES, and it has no choice. This palette is
            // invented right here, at runtime, and Unity cannot compile a StyleSheet from a string
            // in a player build — so there is no sheet to add and no way to make one. Every OTHER
            // palette in the showcase existed at build time and is painted through the cascade
            // instead. See the header of CodigrateThemeApplier.
            _activeOverride     = CodigrateThemeApplier.Randomize(_themeLight);
            _activeThemeAsset   = null;
            _themeToggleLocked  = false;   // randomize keeps the day/night toggle usable: re-rolling
                                           // in the same mood is the natural "try until you like it" loop
            _themeDropdownValue = RANDOM_OPTION;
            _themeStatusText    = "Random palette · click Randomize again to roll.";

            SyncThemeEverywhere();
        }

        // Hook the burger / close buttons in the three drawer demo sections to
        // their `.ds-drawer-wrap` parents. The runtime helper toggles `is-open`
        // on the wrapper; the USS rules drive the rest. Each demo wires three
        // closers — the close button, plus (for the overlay variants) the
        // backdrop, which dismisses the drawer when the dim layer is clicked.
        static void WireDrawerDemos(VisualElement root)
        {
            if (root == null) return;

            DesignSystemBehaviour.WireDrawer(
                root.Q<Button>("drawer-top-burger"),
                root.Q("drawer-top-wrap"),
                root.Q<Button>("drawer-top-close"));

            DesignSystemBehaviour.WireDrawer(
                root.Q<Button>("drawer-right-burger"),
                root.Q("drawer-right-wrap"),
                root.Q<Button>("drawer-right-close"),
                root.Q("drawer-right-backdrop"));

            DesignSystemBehaviour.WireDrawer(
                root.Q<Button>("drawer-push-burger"),
                root.Q("drawer-push-wrap"),
                root.Q<Button>("drawer-push-close"));
        }

        // Touch-friendly auto-hide for the auto-hiding scrollbar demo. Desktop
        // users get the pure-USS `:hover` rule for free; this helper covers
        // mobile, where there's no hover signal.
        static void WireAutoHideScroll(VisualElement root)
        {
            if (root == null) return;
            var sv = root.Q<ScrollView>("auto-hide-scroll");
            DesignSystemBehaviour.WireScrollAutoHide(sv);
        }
    }
}
