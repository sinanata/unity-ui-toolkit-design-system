# Mobile responsiveness

The design system supports two layout tiers: **desktop** (default) and **mobile**. Switching between them is a single class on the screen root:

```csharp
if (Screen.width < 768)                 // your platform check
    root.AddToClassList("mobile");
```

Same UXML, same component classes — the rendered layout flips between the two.

## How it works

`Mobile.uss` is the last file imported by `DesignSystem.uss`. Every rule in it is prefixed with `.mobile`:

```css
/* Buttons.uss — desktop default */
.ds-btn { height: 36px; }

/* Mobile.uss — mobile override */
.mobile .ds-btn { height: 48px; }
```

The mobile selector has higher specificity (`0,2,0` vs `0,1,0`) AND loads later, so it always wins when the screen root carries `.mobile`.

When `.mobile` is **absent**, the prefixed rules don't match anything — the desktop sizing applies.

## What flips

| Component | Desktop | Mobile |
| --- | --- | --- |
| `.ds-btn` | 36 px tall | 48 px tall |
| `.ds-btn--icon` | 36 × 36 | 48 × 48 |
| `.ds-input`, `.ds-search`, `.ds-dropdown` | 40 px tall, 14 px font | 48 px tall, 15 px font |
| `.ds-textarea` | 96 px min-height | 120 px min-height |
| `.ds-search__icon` | 18 × 18 | 22 × 22 |
| `.ds-tab` | 32 px tall | 48 px tall |
| `.ds-toggle` | 44 × 24 track | wider, taller |
| `.ds-check` | 20 × 20 box | 28 × 28 box |
| `.ds-radio` | 20 × 20 ring | 28 × 28 ring |
| `.ds-slider` thumb | 18 × 18 | 24 × 24 (recomputed `margin-top: -12px` for centring) |
| `.ds-range` thumbs | 18 × 18 each | 24 × 24 each |
| `.ds-progress` | 8 px tall | 10 px tall |
| `.ds-modal`, `.ds-dialog` | Auto width | Wider, with mobile padding |
| `.ds-side-rail` | Visible alongside content | Compacted; bottom-nav usually replaces it |
| `.ds-avatar--xl` | 72 × 72 | 64 × 64 |
| `.ds-queue-pill` | 40 px tall, 32 px cancel | 56 px tall, 48 px cancel (a thumb-zone control), body-1 text |
| `.ds-queue-sheet` | 320 px wide | full width up to 420 px |
| `.ds-scoreboard__row` | 36 px; rank 48, platform 28, score 48 | 32 px; rank 44 (an sm chip is 36 px), platform 24, score 44, so a 3v3 board fits the 360 px-tall reference and a half-width board keeps its names |
| `.ds-reward-track__node` | 72 px node, 40 px badge | 80 px node, 48 px badge (a claimable reward is a tap target) |
| `.ds-toast-stack` | 12 px gutter | 8 px gutter |

The rank crest, rank chip, pips, progress ring and countdown keep their size: they are sized in their own size modifiers, which already cover the phone cases (a 160 px hero crest, an 80 px countdown).

The full list lives in `Mobile.uss`, with comments on each section explaining *why* the value changed (typically: 48 px touch-target minimum from Apple HIG / Material).

## When to add the class

Three common patterns:

### 1. Static check at screen build

If your app is "always desktop" or "always mobile" depending on the platform:

```csharp
void Awake() {
    if (Application.isMobilePlatform)
        Root.AddToClassList("mobile");
}
```

### 2. Live width-based switch

If your screen runs in a window that can resize (e.g. a desktop game with mobile-friendly menus on small windows):

```csharp
void OnGeometryChanged(GeometryChangedEvent evt) {
    bool isMobileWidth = Root.resolvedStyle.width < 768;
    Root.EnableInClassList("mobile", isMobileWidth);
}

Root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
```

### 3. Settings toggle

If users can opt into a "touch mode" regardless of platform — e.g. a PC build that wants tablet-style menus:

```csharp
if (PlayerPrefs.GetInt("UseTouchUI", 0) == 1)
    Root.AddToClassList("mobile");
```

## Adding mobile overrides for new components

When you author a new component, put its desktop rules in the relevant subsystem file (`Buttons.uss`, `Cards.uss`, etc.). Then, **at the bottom of `Mobile.uss`** in the matching section, add the touch-tier overrides:

```css
/* In MyComponent.uss */
.ds-my-thing { height: 36px; padding: 8px; }

/* In Mobile.uss */
.mobile .ds-my-thing {
    height: 48px;
    padding: 12px;
}
```

Don't put `.mobile` rules in the component's own file. Keeping them centralised in `Mobile.uss` means a contributor can audit "what changes on mobile" by reading one file, not 13.

## Testing

Three ways to render the mobile tier:

### A. Hard-code the class on the showcase

Edit `DesignSystemShowcase.uxml` (or your test screen):

```xml
<ui:ScrollView mode="Vertical" class="ds-root mobile">
```

Save, hit play. Every component renders in mobile sizing.

### B. Toggle via temporary debug code

Add a one-off in your screen's `Awake()`:

```csharp
#if UNITY_EDITOR
Root.AddToClassList("mobile");
#endif
```

Comment out before merging.

### C. Build the WebGL showcase + Chrome device emulation

The most reliable way to verify the mobile flip *before* shipping a real mobile build. From the repo root:

```powershell
.\Tools\Build\Build-Showcase.ps1 -Serve
```

Builds the showcase to `build/WebGL/` and serves it at `http://localhost:3000`. Open Chrome DevTools' **Toggle device toolbar** (`Ctrl+Shift+M`) and pick a phone profile. The `ShowcaseBootstrap` calls `Screen.width < 768 → AddToClassList("mobile")` on first frame, so dropping below 768 px triggers the same flip a real phone would.

The showcase build also exercises the styled scrollbar at narrow widths and the day / night theme toggle — useful to verify cross-cutting concerns at the same time.

A future contribution that ships as part of this repo: a Unity menu item under `Tools/Design System/Toggle Mobile` that flips the class on the active UIDocument's root inside the editor. PRs welcome.

## Why one class instead of media queries

Unity 6 USS doesn't support `@media` queries. The single-class pattern is the idiomatic Unity way to express responsive design:

- Discoverable — `.mobile` shows up in the UI Toolkit Debugger panel, you can see exactly which rules are applying.
- Predictable — the class either is or isn't on the root. No "did the breakpoint fire yet?" guesswork.
- Composable — you can author further tiers (`.mobile-tablet`, `.mobile-phone`) by adding more class-prefixed rules. The pattern doesn't lock you into binary desktop/mobile.

The downside vs media queries: you must opt in via C#. There's no automatic "browser shrunk to phone width" behaviour — you write the platform check yourself. That's a worthwhile trade for the predictability and the cross-platform code paths you already have to write anyway.

## Common pitfalls

- **Forgetting `align-self`-style overrides on the mobile thumb.** `.mobile .ds-slider .unity-base-slider__dragger` needs `margin-top: -12px` (half of the new 24 px height) — the desktop -9px is wrong at the bigger size. The shipped `Mobile.uss` handles this; if you change the thumb size, recompute the margin.
- **Specificity battles with inline styles.** Inline `style="height: 36px"` in UXML will beat `.mobile .ds-btn { height: 48px }` regardless of class order. Use classes for sizing; reserve inline styles for one-off positioning (`margin: 6px` etc.).
- **Mobile rules in the wrong file.** If a `.mobile .foo` rule lives in `Buttons.uss` instead of `Mobile.uss`, future contributors won't find it. Always centralise.
- **Forgetting the class on subtemplates.** If you instantiate a sub-UXML (e.g. a popup) and clone it under your mobile screen, the new tree's root needs `.mobile` too. Either inherit from your screen's root (most flex children do) or call `subroot.AddToClassList("mobile")` at clone time.
