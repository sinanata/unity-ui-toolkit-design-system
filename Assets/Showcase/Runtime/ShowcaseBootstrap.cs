using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using DesignSystem.Runtime.UIDocumentRuntime;
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
        const string SHOWCASE_RES_PATH = "UI/Styles/DesignSystem/DesignSystemShowcase";
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
                               "Confirm Assets/DesignSystem/Resources/UI/Styles/DesignSystem/DesignSystemShowcase.uxml exists.");
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

                var panelRoot = root.parent;
                if (panelRoot != null)
                {
                    var popupChrome = Resources.Load<StyleSheet>("ShowcaseDropdownPopup");
                    if (popupChrome != null && !panelRoot.styleSheets.Contains(popupChrome))
                        panelRoot.styleSheets.Add(popupChrome);
                }

                // Fresh-slate state: a previous scene visit may have left
                // _activeOverride set to a palette whose VisualElement refs
                // are stale. The new tree starts at the design-system
                // defaults, so reset the cache here before wiring.
                _activeOverride = null;

                ApplyMobileClass(root);
                WirePromoLinks(root);
                WireThemeToggle(root);
                WireThemeProvider(root);
                WireCodigrateLink(root);
                WireRandomize(root);
                WireDrawerDemos(root);
                WireAutoHideScroll(root);
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

            // The DesignSystemRuntime auto-attaches via SceneManager.sceneLoaded
            // which may fire BEFORE our AfterSceneLoad init — so the GameObjects
            // we just created would miss the initial attach. Nudge it manually.
            // The runtime is idempotent; calling twice is a no-op.
            DesignSystemRuntime.AttachToAll();
        }

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
        // opens a new browser tab with the GitHub repo / Steam page.
        static void WirePromoLinks(VisualElement root)
        {
            if (root == null) return;
            var gh = root.Q<Button>("promo-github");
            if (gh != null) gh.clicked += () => Application.OpenURL("https://github.com/sinanata/unity-ui-document-design-system");
            var st = root.Q<Button>("promo-steam");
            if (st != null) st.clicked += () => Application.OpenURL("https://store.steampowered.com/app/2269500/");
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
        static CodigrateThemeApplier.ColorMap _activeOverride;

        // Wire the day/night toggle in the COLORS section header. Adds /
        // removes the `theme-light` class on .ds-root; ShowcaseTheme.uss
        // redefines every colour token under that class, the universal
        // transition rule animates the swap across the whole tree, and the
        // hex labels in the COLORS section are rewritten to match.
        //
        // The class is ALSO applied to `panel.visualTree` because Unity's
        // BasePopupField adds the dropdown popup as a SIBLING of root,
        // under panel.visualTree. Without the class on that ancestor the
        // popup never sees the .theme-light token overrides and stays dark
        // while the rest of the showcase flips to light mode.
        //
        // While an override palette is active (codigrate / randomize) the
        // toggle is `SetEnabled(false)` by WireThemeProvider, so this handler
        // only fires for legitimate user-driven swaps between the two
        // first-party token sets.
        static void WireThemeToggle(VisualElement root)
        {
            if (root == null) return;
            var toggle = root.Q<Toggle>("theme-toggle");
            if (toggle == null) return;
            toggle.RegisterValueChangedCallback(evt =>
            {
                bool light = evt.newValue;
                ApplyThemeClass(root, light);
                UpdateHexLabels(root, light);
            });
        }

        static void ApplyThemeClass(VisualElement root, bool light)
        {
            if (light) root.AddToClassList("theme-light");
            else       root.RemoveFromClassList("theme-light");

            var panelRoot = root.panel?.visualTree;
            if (panelRoot != null && panelRoot != root)
            {
                if (light) panelRoot.AddToClassList("theme-light");
                else       panelRoot.RemoveFromClassList("theme-light");
            }
        }

        static void UpdateHexLabels(VisualElement root, bool light)
        {
            // While an override palette is active, the swatches reflect that
            // palette's actual values rather than the design-system Dark / Light
            // dictionary. Once Revert runs and _activeOverride goes back to
            // null the dictionary path takes over again.
            if (_activeOverride != null)
            {
                UpdateHexLabelsFromOverride(root);
                return;
            }
            foreach (var kv in SwatchHex)
            {
                var label = root.Q<Label>(kv.Key);
                if (label == null) continue;
                label.text = light ? kv.Value.Light : kv.Value.Dark;
            }
        }

        static void UpdateHexLabelsFromOverride(VisualElement root)
        {
            var m = _activeOverride;
            SetHex(root, "hex-primary",         m.Primary);
            SetHex(root, "hex-primary-hover",   m.PrimaryHover);
            SetHex(root, "hex-secondary",       m.Secondary);
            SetHex(root, "hex-tertiary",        m.Tertiary);
            SetHex(root, "hex-warning",         m.Warning);
            SetHex(root, "hex-danger",          m.Danger);
            SetHex(root, "hex-text-primary",    m.TextPrimary);
            SetHex(root, "hex-text-secondary",  m.TextSecondary);
            SetHex(root, "hex-text-disabled",   m.TextDisabled);
            SetHex(root, "hex-bg",              m.Bg);
            SetHex(root, "hex-surface",         m.Surface);
            SetHex(root, "hex-surface-elev",    m.SurfaceElev);
            SetHex(root, "hex-border",          m.Border);
        }

        static void SetHex(VisualElement root, string name, Color color)
        {
            var label = root.Q<Label>(name);
            if (label != null) label.text = CodigrateThemeApplier.ToHex(color);
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

            if (status != null) status.text = "Loading codigrate themes…";

            CodigrateThemeProvider.FetchList((list, error) =>
            {
                if (error != null || list == null)
                {
                    if (status != null) status.text = "Codigrate themes unavailable. Random palette still works.";
                    Debug.LogWarning($"[ShowcaseBootstrap] Codigrate list fetch failed: {error}");
                    return;
                }

                _codigrateListings = list;
                var choices = new List<string> { DEFAULT_OPTION };
                foreach (var l in list) choices.Add(l.Name);
                choices.Add(RANDOM_OPTION);
                dropdown.choices = choices;
                // Preserve current value across the choices swap. If the user
                // had Random selected at fetch time and we wiped it, the field
                // would render empty even though _activeOverride is still set.
                if (_activeOverride != null) dropdown.SetValueWithoutNotify(RANDOM_OPTION);
                if (status != null) status.text = $"{list.Count} themes by Codigrate available.";
            });

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var name = evt.newValue;
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

                if (status != null) status.text = $"Loading {listing.Name}…";
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

        static void ApplyCodigratePalette(VisualElement root, CodigrateThemeProvider.ThemePalette palette)
        {
            var map = CodigrateThemeApplier.FromCodigrate(palette);
            _activeOverride = map;

            // Mirror the palette's reported appearance onto the day/night
            // toggle so any leftover USS state (e.g. the `.theme-light`
            // re-routes for the notification dot) is consistent — then
            // disable the toggle while the override is active.
            bool isLight = string.Equals(palette.Appearance, "light", StringComparison.OrdinalIgnoreCase);
            var toggle = root.Q<Toggle>("theme-toggle");
            if (toggle != null)
            {
                toggle.SetValueWithoutNotify(isLight);
                toggle.SetEnabled(false);
            }
            ApplyThemeClass(root, isLight);

            CodigrateThemeApplier.Apply(root, map);
            UpdateHexLabels(root, isLight);
        }

        static void ClearOverride(VisualElement root)
        {
            _activeOverride = null;
            CodigrateThemeApplier.Revert(root);

            var toggle = root.Q<Toggle>("theme-toggle");
            if (toggle != null) toggle.SetEnabled(true);

            bool isLight = toggle != null && toggle.value;
            // Refresh hex labels back to the design-system dictionary now that
            // _activeOverride is null.
            UpdateHexLabels(root, isLight);

            var status = root.Q<Label>("theme-provider-status");
            if (status != null && _codigrateListings != null)
                status.text = $"{_codigrateListings.Count} themes by Codigrate available.";
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
            var toggle = root.Q<Toggle>("theme-toggle");
            bool isLight = toggle != null && toggle.value;
            var map = CodigrateThemeApplier.Randomize(isLight);
            _activeOverride = map;

            // Reflect the random state in the dropdown so the user can later
            // pick DEFAULT_OPTION to revert via the same mechanism that
            // reverts a codigrate palette.
            var dropdown = root.Q<DropdownField>("theme-provider-dropdown");
            if (dropdown != null && dropdown.value != RANDOM_OPTION)
                dropdown.SetValueWithoutNotify(RANDOM_OPTION);

            // Randomize keeps the toggle enabled — re-rolling in the same mood
            // is the most natural flow, and flipping to the other mood then
            // re-rolling builds the "try until you like it" loop.
            if (toggle != null) toggle.SetEnabled(true);

            CodigrateThemeApplier.Apply(root, map);
            UpdateHexLabels(root, isLight);

            var status = root.Q<Label>("theme-provider-status");
            if (status != null) status.text = "Random palette · click Randomize again to roll.";
        }

        // Hook the burger / close buttons in the three drawer demo sections to
        // their `.ds-drawer-wrap` parents. The runtime helper toggles `is-open`
        // on the wrapper; the USS rules drive the rest. Each demo wires three
        // closers — the close button, plus (for the overlay variants) the
        // backdrop, which dismisses the drawer when the dim layer is clicked.
        static void WireDrawerDemos(VisualElement root)
        {
            if (root == null) return;

            DesignSystemRuntime.WireDrawer(
                root.Q<Button>("drawer-top-burger"),
                root.Q("drawer-top-wrap"),
                root.Q<Button>("drawer-top-close"));

            DesignSystemRuntime.WireDrawer(
                root.Q<Button>("drawer-right-burger"),
                root.Q("drawer-right-wrap"),
                root.Q<Button>("drawer-right-close"),
                root.Q("drawer-right-backdrop"));

            DesignSystemRuntime.WireDrawer(
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
            DesignSystemRuntime.WireScrollAutoHide(sv);
        }
    }
}
