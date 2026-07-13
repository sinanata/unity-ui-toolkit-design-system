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
        ├── Icons.uss            ← .ds-icon base + 120 .ds-icon--<name> + parent-state cascade
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

### Theming with an asset (the supported path)

A `ThemeData` asset holds every token, generates the block below for you, and stores the compiled stylesheet as a sub-asset of itself. Add a `ThemeApplier` to your `UIDocument` or `PanelRenderer`, point it at the theme, and that one sheet goes onto the root:

```csharp
applier.Theme = tokyo;                       // swap at runtime; the cascade repaints the tree
ThemeRuntime.Apply(root, tokyo);             // or drive it directly, for panels you build in code
```

That is the whole mechanism. Because the theme arrives as a real stylesheet rather than as inline styles, every `:hover`, `:disabled` and `:checked` rule in the design system re-resolves against the new token values on its own — no per-component work, and nothing to update when you add a component.

`Dark` and `Light` ship in `Resources/UI/Themes/`. Duplicate `Dark` and edit it in `Design System > Theme Configurator`, which previews live against real components.

**Why the asset has to be baked at EDIT time.** Unity's public UI Toolkit API cannot set a `var(--…)` custom property at runtime, and cannot compile a `StyleSheet` from a string in a player build. So a palette that first exists at RUNTIME can only be applied by walking the tree and stamping inline styles on every element — which is exactly what the showcase's `Randomize` button still has to do, and why `CodigrateThemeApplier` is 1,100 lines. Bake the theme at edit time and the whole problem evaporates.

**Scope: `:root` or a class.** `scopeSelector` decides which selector the token block is written under. `:root` themes the whole panel. A class such as `.theme-night` themes only a subtree carrying that class — so two themes can live in one panel, and a day / night PAIR is just both sheets on the root with one class toggle picking between them. That is how the shipped `Light` theme (`.theme-light`) works.

> **Unity scores `:root` and a class selector identically — both 256.** Verified by reading the specificity Unity writes into a compiled `StyleSheet`. So a `:root { --token: … }` block does **not** outrank a `.theme-light { --token: … }` block; it wins only by being added later. Two consequences. First, a theme sheet must go on the **same element the base tokens resolve on** (the panel or document root, where `DesignSystem.uss` lands) — put it on an ancestor and a token block attached lower down will shadow it, because a rule matching an element beats a value the element merely inherited. Second, the applier adds its sheet **last**, which is what makes it win.

### Theming by hand (the manual equivalent)

The asset generates precisely this, so it is worth knowing. Override `:root` in a higher-priority stylesheet attached after the design system:

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

The whole tree picks up the new values via the var() cascade, instantly. This is exactly how the showcase's day / night toggle works — see `Assets/Showcase/Resources/ShowcaseTheme.uss` for the reference implementation.

### Do not animate the swap

It is tempting to make the change cross-fade by pairing the override with a universal transition rule (`.ds-root * { transition-property: background-color, …; transition-duration: 240ms; }`). The showcase shipped exactly that, and it had to be taken out: it is the most expensive thing you can do to a UI Toolkit panel.

Every `ds-` component **already** animates its own colours so that hover fades. A theme swap moves those same properties, on every element, at the same moment — and USS cannot tell the two changes apart, because a transition fires on any change to a watched property and the cascade has no idea *why* the value moved. So the swap silently inherits the hover animation across the entire tree.

That is not merely slow. A running transition pins a snapshot of the element's `ComputedStyle`, and a `ComputedStyle` is a set of ref-counted native blocks taken from `Allocator.Domain`. Swap faster than the transitions retire and the live blocks stack instead of draining, until Unity starts logging `Allocator.Domain has reached its limit of 262144 tracked allocations` and the tab freezes. On a ~1,000-element screen it took a few dozen swaps; with several panels alive at once, about fifteen.

If you need the swap suppressed *and* your hover transitions intact, do what the showcase does: put a class on the root for the frames the swap lands in, and take it off afterwards.

```css
.ds-no-transition,
.ds-no-transition * {
    transition-property: none;
}
```

```csharp
root.AddToClassList("ds-no-transition");
ThemeRuntime.Apply(root, theme);                       // colours resolve instantly
root.schedule.Execute(() => root.RemoveFromClassList("ds-no-transition")).ExecuteLater(120);
```

Taking the class back off cannot retro-start anything: by then the colours have already settled and nothing is changing. Re-arm the timer on every swap so a rapid burst stays suppressed for its whole duration.

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

### Geometry in a world-space panel is not what you think

Two assumptions that hold everywhere else are false inside a world-space `PanelRenderer`, and code that positions anything by hand will be wrong in a way that looks like a styling bug. Both cost real time here before they were understood.

**`worldBound` is in metres, not pixels.** The panel root carries a pixels-to-metres transform, so a laid-out `206x48` field reports a `worldBound` of about `2.1x0.4`. `resolvedStyle`, `layout` and everything you write to `style.*` stay in pixels. Arithmetic that mixes the two compares `0.4` against `42` and silently loses every comparison. On a screen panel the transform is identity and the bug is invisible, so it ships.

**The panel root can measure 0x0.** With `WorldSpaceSizeMode.Dynamic` the panel has no viewport to fill, so `panel.visualTree` has no size while the content lays out normally as its child — an exhibit measuring `240x460` hanging inside a `0x0` root. Any "clamp this to the panel" logic gets no room, on any side, for anything.

The rule that survives both: **do the geometry in the local space of the element you are writing to**, and convert anything foreign in with `WorldToLocal`, which is a pure translation on a screen panel and therefore free. When you need a bounding box and the panel root is degenerate, walk up to the element's outermost ancestor that actually has a size — in the showcase's corridor that is the exhibit's content box, which is the visible wall panel and the only meaningful boundary anyway. `TuneDropdownPopup` in `DesignSystemBehaviourBase` is the worked example.

A corollary for anything that *measures* a world-space panel, including tests: a threshold like `width < 10 means it never laid out` is a pixel assumption. Against metres it is a false negative, and `PopupProbe` spent its whole life reporting `field never laid out (2.1x0.4)` and skipping every world-space check because of it.

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
│   │   ├── DesignSystemShowcase.uxml  ← the living style guide
│   │   ├── ShowcaseTheme.uss    ← .theme-light token override + the ds-no-transition swap guard
│   │   ├── CodigrateThemes/     ← the 12 bundled palette JSONs (the source)
│   │   ├── Themes/              ← the same 12, baked into ThemeData assets (what actually paints)
│   │   ├── UnityDefaultRuntimeTheme.tss
│   │   └── sinanata.jpg         ← profile photo for the AVATAR section
│   └── Runtime/
│       ├── ShowcaseBootstrap.cs
│       └── ShowcaseDocOverlay.cs
├── Editor/
│   ├── BuildCli.cs              ← Unity batchmode entry for WebGL builds
│   └── ShowcaseThemeBaker.cs    ← turns the palette JSONs into the ThemeData assets above
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

1. `.ds-no-transition`, which the bootstrap puts on the roots for the frames a theme swap lands in so the swap does not animate (see "Do not animate the swap" above).
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
