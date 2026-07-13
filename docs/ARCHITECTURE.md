# Architecture

The design system is a stack of independent USS files imported by a single master stylesheet, plus a theme-authoring pipeline and one runtime helper script. This document walks through the layers and the load-bearing decisions behind them.

## The stack

```
DesignSystemBehaviour.cs          ← C# helper, auto-attaches to UIDocument
        │
        ▼
DesignSystem.uss                 ← master, @imports the layers below
        │
        ├── DesignTokens.uss     ← :root variables only — no element rules
        ├── Typography.uss       ← .ds-h1 / .ds-h2 / .ds-h3 / .ds-body-1 / .ds-caption
        ├── Icons.uss            ← .ds-icon base + 63 .ds-icon--<name> + parent-state cascade
        ├── Buttons.uss          ← .ds-btn variants, sizes, icon button
        ├── Inputs.uss           ← .ds-input, .ds-search, .ds-dropdown, .ds-textarea
        ├── TabsAndFilters.uss   ← .ds-tabs, .ds-tab, .ds-view-toggle
        ├── Cards.uss            ← .ds-animal-card, .ds-info-row, .ds-swatch-row
        ├── Navigation.uss       ← .ds-side-nav, .ds-side-rail, .ds-bottom-nav, .ds-profile
        ├── Badges.uss           ← .ds-badge, .ds-tag, .ds-chip, .ds-avatar, .ds-notif-*
        ├── Controls.uss         ← .ds-toggle, .ds-check, .ds-radio, .ds-slider, .ds-range, scoped scrollbars
        ├── Overlays.uss         ← .ds-modal, .ds-dialog, .ds-toast, .ds-sheet, .ds-empty
        ├── Feedback.uss         ← .ds-progress, .ds-spinner, .ds-skeleton, .ds-pagination, showcase helpers
        └── Mobile.uss           ← .mobile-prefixed overrides — loaded LAST
```

A consumer attaches **one stylesheet** (`DesignSystem.uss`) to their `UIDocument`. The `@import` chain pulls everything else.

## Tokens

`DesignTokens.uss` declares CSS custom properties on `:root`. Every other file references them via `var(--…)`:

```css
/* DesignTokens.uss */
:root {
    --color-primary: #22C55E;
    --space-3: 12px;
}

/* Buttons.uss */
.ds-btn {
    background-color: var(--color-primary);
    padding-left: var(--space-3);
}
```

To re-theme: override `:root` in a higher-priority stylesheet attached after the design system:

```css
/* MyTheme.uss — attach after DesignSystem.uss on the same UIDocument */
:root {
    --color-primary: #FF6B35;     /* warm orange */
    --color-bg: #FAFAFA;          /* light theme */
    --color-text-primary: #111827;
}
```

No need to touch `Buttons.uss` or any other file. The cascade re-paints automatically.

To switch themes at runtime, scope the overrides under a class instead of `:root` and toggle the class on `.ds-root`:

```css
/* In a stylesheet loaded after DesignSystem.uss */
.theme-light {
    --color-primary:        #16A34A;
    --color-bg:             #F8FAFC;
    --color-surface:        #FFFFFF;
    --color-text-primary:   #0F172A;
    --color-text-secondary: #475569;
    /* ...the rest of the token set... */
}
```

```csharp
// Toggle from any handler
toggle.RegisterValueChangedCallback(evt => {
    if (evt.newValue) root.AddToClassList("theme-light");
    else              root.RemoveFromClassList("theme-light");
});
```

The whole tree picks up the new values via the var() cascade. To make the swap animate (it's instant by default — children don't inherit transitions), pair the override with a universal transition rule:

```css
.ds-root,
.ds-root * {
    transition-property: background-color, color, border-color, border-top-color, border-right-color, border-bottom-color, border-left-color, -unity-background-image-tint-color;
    transition-duration: 240ms;
    transition-timing-function: ease-in-out;
}
```

This is exactly how the showcase's day / night toggle works — see `Assets/Showcase/Resources/ShowcaseTheme.uss` for the reference implementation.

### Themes that need to override colour-on-coloured-background

Some elements have a coloured background that doesn't change when the theme flips, but their text colour does. The notification dot is the canonical example: `.ds-notif-dot` is always red (the danger token), but `.ds-notif-dot__count` uses `var(--color-text-primary)` which inverts to dark slate under a light theme — black text on red. Re-route in your theme override:

```css
.theme-light .ds-notif-dot__count {
    color: var(--color-text-on-accent);   /* white in the light theme */
}
```

Same pattern applies to any "always-coloured surface, theme-aware text" pair.

### Inline `var(...)` in UXML — DON'T

Unity 6's clone-time `StyleVariableResolver` NREs when it encounters `var(...)` inside a UXML `style="..."` attribute. The crash surfaces as "The UXML file set for the UIDocument could not be cloned." Author colour and dimension overrides as USS classes:

```xml
<!-- WRONG — crashes Unity 6 on clone -->
<ui:VisualElement style="background-color: var(--color-surface);" />

<!-- RIGHT — class drives the var() resolution at USS parse time -->
<ui:VisualElement class="ds-card-surface" />
```

```css
.ds-card-surface { background-color: var(--color-surface); }
```

Inline `var(...)` works fine inside USS files themselves — it's only the inline UXML attribute that breaks.

### Per-axis radius tokens

Unity 6 USS clamps `border-radius` per axis to half the side length. A naive `border-radius: 999px` on a non-square element renders as an *ellipse*, not a CSS-style pill. The token set therefore ships explicit pill-radius values:

```css
--radius-pill-9:   9px;    /* 18×18 (toggle knob, slider thumb)         */
--radius-pill-12:  12px;   /* 24×24 (chip, tag, card check)             */
--radius-pill-16:  16px;   /* 32×32 (spinner, profile avatar)           */
--radius-pill-20:  20px;   /* 40×40 (avatar-md)                         */
...
```

Each token equals **half the height of an element it's designed for**. A 24-px chip uses `--radius-pill-12`; a 40-px avatar uses `--radius-pill-20`. Picking the right token is your responsibility — the comment on each token names its intended consumers.

## Why the import order matters

USS specificity ties resolve by source order. When two rules with the same specificity target the same element, the rule loaded *later* wins. The system uses this deliberately:

```css
/* Icons.uss — loaded 3rd */
.ds-icon { width: 20px; height: 20px; }

/* Inputs.uss — loaded 5th, AFTER Icons.uss */
.ds-search__icon { width: 18px; height: 18px; }
```

A `<VisualElement class="ds-icon ds-icon--search ds-search__icon">` element gets the **18×18** size from `.ds-search__icon`, not the 20×20 from `.ds-icon`, because Inputs.uss loads later.

This is the pattern by which "general icon" rules are specialised by per-component slot rules without writing higher-specificity selectors.

`Mobile.uss` is intentionally loaded **last** so its `.mobile`-prefixed overrides always win. If you reorder the imports, the responsive pass breaks first.

## Parent-state cascade for icons

A common request: "an icon inside a hovered button should retint." The naive solution is per-component `:hover .icon` rules, multiplied across every consumer. The system instead ships **one** cascade in `Icons.uss`:

```css
.ds-btn:hover .ds-icon,
.ds-tab:hover .ds-icon,
.ds-nav-item:hover .ds-icon,
.ds-rail-item:hover .ds-icon,
.ds-bottom-nav__item:hover .ds-icon,
.ds-pagination__btn:hover .ds-icon,
.ds-stepper__btn:hover .ds-icon {
    -unity-background-image-tint-color: var(--color-text-primary);
}
```

A new interactive container can opt into the cascade by adding its selector to the list. The icon picks up hover / active / pressed / disabled tints automatically, no per-consumer rule needed.

Filled-background controls (primary / secondary / tertiary / danger buttons, active tabs) override the cascade with on-accent ink so the glyph stays legible against the bg fill:

```css
.ds-btn--primary .ds-icon,
.ds-btn--secondary .ds-icon,
.ds-btn--tertiary .ds-icon,
.ds-btn--danger .ds-icon {
    -unity-background-image-tint-color: var(--color-text-on-accent);
}
```

Soft-bg controls (`.ds-nav-item.is-active`, `.ds-rail-item.is-active`, `.ds-bottom-nav__item.is-active`) keep their primary-tinted glyph via per-`__icon` rules in their own files. The cascade list intentionally does *not* include these, to avoid double-overriding what their own file specifies.

## The runtime layer

`DesignSystemBehaviourBase<TComponent>.cs` in `Runtime/Behaviour/` provides the shared runtime logic; concrete backends `UIDocument/DesignSystemBehaviour.cs` and `PanelRenderer/DesignSystemBehaviour.cs` auto-attach to their respective component types via `[RuntimeInitializeOnLoadMethod]` + `SceneManager.sceneLoaded`:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
static void RegisterAutoAttach() {
    SceneManager.sceneLoaded += OnSceneLoaded;
}

static void OnSceneLoaded(Scene scene, LoadSceneMode mode) {
    AttachToAllUIDocuments();      // iterate, AddComponent if missing
}
```

Every `UIDocument` in every scene gets a `DesignSystemBehaviour` MonoBehaviour. The runtime then:

1. **Injects toggle knobs.** Unity's `Toggle` element doesn't render the iOS-style sliding pill — its checkmark is a square box. The runtime queries `.ds-toggle .unity-toggle__input` and adds a `.ds-toggle__knob` child if one isn't already present. Idempotent. Runs once at attach time and re-scans every 250 ms to cover toggles cloned in lazily by screen managers (e.g. a Settings panel that creates its toggles on first open).
2. **Drives spinner rotation.** USS `transition` can't loop natively. The runtime increments `transform.rotate` by 6° every frame on `.ds-spinner.is-spinning` elements.
3. **Animates skeleton shimmer.** `.ds-skeleton` placeholders get a child `.ds-skeleton__shimmer` element which slides via `translate` from -100 % to +100 % across a 1.4 s loop.

Two helpers are exposed `public static` so screen managers can call them eagerly after a template clone:

```csharp
DesignSystemBehaviour.EnsureToggleKnobs(root);
DesignSystemBehaviour.EnsureSkeletonShimmers(root);
```

This avoids the one-frame "flat pill" flash on the very first appearance of a screen, while the periodic re-scan still picks up anything cloned later.

### UIDocument vs PanelRenderer

Both components host flat and world-space UI, and this runtime ships a backend for each: one for the traditional `UIDocument`, and one for `PanelRenderer` (Unity 6000.5+), Unity's newer native renderer that the showcase uses for its world-space gallery. As of 6000.5 Unity lists `UIDocument` under UI Toolkit > Legacy in the Add Component menu and recommends `PanelRenderer` for new work, but `UIDocument` is not marked `[Obsolete]`, compiles without warnings, keeps working with no removal planned, and still exposes the full world-space API (`worldSpaceSize`, `pivot`, `position`). Both backends stay first-class here: use `PanelRenderer` for new screens and world-space, and keep `UIDocument` for existing ones as long as you need it.

## What lives in C# vs USS

The boundary is intentional:

| Concern | Lives in |
| --- | --- |
| Colours, spacing, sizing, radii, motion timing | USS (tokens) |
| Layout — flex, position, alignment | USS |
| State variants — hover / active / disabled / .is-active | USS pseudo-classes + state classes |
| State **transitions** that USS can express | USS `transition` |
| State **transitions** that USS can't loop or animate | C# (runtime) — spinner rotation, skeleton shimmer |
| DOM-shape requirements Unity can't author in UXML | C# (runtime) — toggle knob injection |
| Localised text, dynamic image sources, click handlers | Consumer C# (your screen manager) |

If you find yourself writing C# to set a colour, the colour belongs in a token. If you find yourself writing USS for a click handler, you've taken a wrong turn.

## Mobile pattern

`.mobile` is a single class added to your screen root. Every responsive rule lives in `Mobile.uss` and is prefixed with `.mobile`:

```css
/* Buttons.uss */
.ds-btn { height: 36px; }

/* Mobile.uss */
.mobile .ds-btn { height: 48px; }     /* same selector, .mobile prefix */
```

The selector `.mobile .ds-btn` has higher specificity (`0,2,0`) than `.ds-btn` (`0,1,0`), so the mobile rule always wins when the screen root carries `.mobile`.

Pattern across the system:

- Buttons / inputs / tabs grow to 48 px tall (touch target minimum).
- Slider thumbs grow 18 px → 24 px with recomputed `margin-top: -12px` for centring.
- Modals widen, side rails compact, bottom-nav bar takes over from side-nav.

You toggle the class once at screen build time:

```csharp
if (PanelSettingsHelper.IsMobileLayout())   // your own platform check
    root.AddToClassList("mobile");
```

Same UXML, same component classes — the layout flips.

## Adding a new layer

If a new component family doesn't fit `Cards.uss` or `Overlays.uss`:

1. Create `<Family>.uss` next to the existing layer files.
2. Use only `var(--…)` token references.
3. Append to `DesignSystem.uss`'s `@import` chain — typically before `Mobile.uss`.
4. Add `.mobile` overrides for the new family in `Mobile.uss`.

The `@import` order is the single source of truth for cascade. Don't try to control order via specificity tricks; let the import sequence carry it.

## Scrollbar styling

Unity's UI Toolkit ScrollView is a compound element with internal classes outside the `.ds-` namespace. The system styles them in `Controls.uss`, scoped to `.ds-root` so the rules don't leak into editor windows or other UI Toolkit panels in the same project:

```css
.ds-root .unity-scroller__low-button,
.ds-root .unity-scroller__high-button { display: none; }

.ds-root .unity-scroll-view__vertical-scroller   { width: 8px; }
.ds-root .unity-scroll-view__horizontal-scroller { height: 8px; }

.ds-root .unity-base-slider__tracker {
    background-color: transparent;
    border-width: 0;
}

.ds-root .unity-base-slider__dragger {
    background-color: var(--color-border-strong);
    border-radius: 4px;
    border-width: 0;
    transition-property: background-color;
    transition-duration: var(--transition-fast);
}

.ds-root .unity-base-slider__dragger:hover {
    background-color: var(--color-text-secondary);
}
```

Both axes share the same thumb pattern. The thumb auto-themes through `--color-border-strong` and `--color-text-secondary`, so a theme override repaints the scrollbar with no extra rules.

Why scoped to `.ds-root` instead of the universal `.unity-base-slider__dragger`? The same internal classes are used by Unity's editor windows, the Inspector, the UI Builder, and any other UI Toolkit panel in the project. A naked `.unity-base-slider__dragger { … }` rule would re-skin every scrollbar in every editor tool — not what consumers want when dropping the design system into an existing project.

## The showcase host project

The repo doubles as a **host Unity project** that builds the live web demo. The host pieces are deliberately separated from the drop-in design system folder so consumers can copy `Assets/DesignSystem/` into their own project and leave the host behind:

```
Assets/
├── DesignSystem/                ← drop-in design system (copy this into your project)
├── Showcase/                    ← host scene + supporting scripts (host-only)
│   ├── Showcase.unity
│   ├── Resources/
│   │   ├── ShowcaseTheme.uss    ← .theme-light token override + universal transition
│   │   ├── UnityDefaultRuntimeTheme.tss
│   │   └── sinanata.jpg         ← profile photo for the AVATAR section
│   └── Runtime/
│       ├── ShowcaseBootstrap.cs
│       └── ShowcaseDocOverlay.cs
├── Editor/BuildCli.cs           ← Unity batchmode entry for WebGL builds
└── WebGLTemplates/ShowcaseTemplate/
    └── index.html               ← custom template (touch-action, viewport-fit, dark loading bar)
```

### `ShowcaseBootstrap.cs`

`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` static method that runs once per Play / build start. It:

1. Loads the showcase UXML and the default Unity TSS via `Resources.Load`.
2. Creates two GameObjects, each with a `UIDocument`. Their `PanelSettings` are also created in code (`scaleMode = ConstantPixelSize`, `themeStyleSheet = UnityDefaultRuntimeTheme`, sortingOrder 0 for the showcase doc, 1 for the doc-overlay) — no `.asset` files to maintain.
3. Loads the showcase-only `ShowcaseTheme.uss` via `Resources.Load<StyleSheet>` and adds it to the showcase root's `styleSheets` list. Loaded *after* the design system stylesheet (which the UXML imports first) so its rules win specificity ties.
4. Wires the day / night toggle, the GitHub / Steam promo links (`Application.OpenURL`), and adds `.mobile` to the showcase root when `Screen.width < 768`.
5. Calls `DesignSystemBehaviour.RegisterAutoAttach(typeof(...))` so the spinner / knob / shimmer runtime catches the bootstrap-created documents — `SceneManager.sceneLoaded` fires before our `AfterSceneLoad` init in some Unity versions.

### `ShowcaseDocOverlay.cs`

A `MonoBehaviour` attached to the second UIDocument (the higher-`sortingOrder` one). It builds an inspection panel programmatically (no UXML) and listens for `PointerMoveEvent` / `PointerDownEvent` on the showcase root.

When the cursor moves:

1. Walk up `evt.target.parent` chain.
2. Stop on the first element that carries a `ds-*` class which is **not** in the container excludelist (`ds-section`, `ds-row`, `ds-swatch-row`, `ds-section__title`, `ds-root`).
3. Also short-circuit if any ancestor carries `.showcase-chrome` — that's the promo banner, which uses real ds-* classes for visual consistency but isn't a component the visitor came to inspect.
4. Render the chain in a floating panel anchored next to the leaf (or full-bottom on mobile). Click the panel to pin / unpin. Click the leaf-classes line to copy the class list via `GUIUtility.systemCopyBuffer`.
5. If the cursor sits on only-containers (or chrome) for 2 s, fade the panel + outline highlight via inline `transition-property: opacity` (240 ms ease-out) and remove them from the picking tree until the cursor returns to a real component.

The overlay's root is `pickingMode = Ignore` so showcase events still reach the showcase document below; only the inspection panel itself is `pickingMode = Position` to capture pin / copy clicks.

### `ShowcaseTheme.uss`

Three things, all showcase-only:

1. A universal opacity / colour transition rule on `.ds-root, .ds-root *` so theme swaps animate.
2. A `.theme-light` block that redefines every colour token from `DesignTokens.uss`.
3. Targeted overrides like `.theme-light .ds-notif-dot__count { color: var(--color-text-on-accent); }` for elements whose colour-on-coloured-background pairing breaks under a naive theme swap.

This file is loaded only by the showcase bootstrap. Consumers copying `Assets/DesignSystem/` into their own project don't get it — they author their own theme overrides scoped to their own classes.

### WebGL build pipeline

The host project builds with `Tools/Build/Build-Showcase.ps1` — a Windows-only PowerShell orchestrator that mirrors the production-tested Leap of Legends `Build-All.ps1` pattern:

- Preflight (Unity exe found, stale `Temp/UnityLockfile` removed).
- Optional cache clear (`-ClearCache` for stale-Burst recovery).
- Unity batchmode invocation with live progress display from `DisplayProgressbar:` log markers.
- JSON build report (`-cliReportPath` flag honoured by `Assets/Editor/BuildCli.cs::BuildWebGL`) so the orchestrator validates success without scraping the log.
- Unity process-tree cleanup on Ctrl+C / error so orphan AssetImportWorkers don't keep `Temp/UnityLockfile` held.
- Burst-AOT cache auto-retry on the specific `bcl.exe exit 3 + empty stderr` pattern.
- Native NTSTATUS crash-code labels (`STATUS_ACCESS_VIOLATION` etc.) when Unity crashes below the C# layer.
- Optional `-Serve` (local HTTP server) and `-Deploy` (single-commit force-push to `gh-pages` via `git worktree`).

Full docs in [`Tools/Build/README.md`](../Tools/Build/README.md).
