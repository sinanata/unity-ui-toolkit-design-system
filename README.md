# Unity UI Toolkit Design System

[![Unity](https://img.shields.io/badge/Unity-6000.0%2B-black?logo=unity&logoColor=white)](https://unity.com)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Live demo](https://img.shields.io/badge/demo-live-22C55E)](https://sinanata.github.io/unity-ui-document-design-system/)
[![PRs welcome](https://img.shields.io/badge/PRs-welcome-brightgreen.svg)](CONTRIBUTING.md)
[![Stars](https://img.shields.io/github/stars/sinanata/unity-ui-document-design-system?logo=github)](https://github.com/sinanata/unity-ui-document-design-system/stargazers)

A drop-in **design system for Unity 6 UI Toolkit** (UIDocument + UXML + USS). Tokens, components, icons, mobile responsiveness, and a runtime helper — all themed dark, all keyboard-and-touch-ready, all editable from one stylesheet. Works on flat screens **and on Unity 6000.5+ world-space panels** (`PanelRenderer`): the same components render on 3D surfaces, and the live showcase ships a walkable world-space gallery to prove it. Open-sourced as part of a small giving-back set of Unity tools — alongside the [Voronoi mesh fracturer](https://github.com/sinanata/unity-mesh-fracture), the [3D-to-sprite baker](https://github.com/sinanata/unity-3d-to-sprite-baker), the [prefab-thumbnail renderer](https://github.com/sinanata/unity-prefab-thumbnail-renderer), and the [cross-platform build orchestrator](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator).

<blockquote>
<a href="https://store.steampowered.com/app/2269500/"><img src="docs/leap-of-legends-icon.png" align="left" width="70" height="70" alt="Leap of Legends"></a>
Built for and battle-tested in <strong><a href="https://leapoflegends.com">Leap of Legends</a></strong>, a cross-platform multiplayer game now live with full cross-play across Steam, iOS, and Android. Every menu, HUD, lobby, and store screen in the game is built on this design system. Play now on <a href="https://store.steampowered.com/app/2269500/">Steam</a>, the <a href="https://apps.apple.com/us/app/leap-of-legends/id6761757484">App Store</a>, and <a href="https://play.google.com/store/apps/details?id=com.exceptionly.leapoflegends">Google Play</a>.
</blockquote>

> **Using an AI coding assistant?** This repo ships an [`AGENTS.md`](AGENTS.md) and an [`llms.txt`](llms.txt) so Copilot, Cursor, Codex, and Claude Code use the `ds-` classes and tokens correctly instead of guessing.

---

## Contents

- [Live showcase](#live-showcase)
- [Why this exists](#why-this-exists)
- [Requirements](#requirements)
- [Installation](#installation)
- [Quick start](#quick-start)
- [Mobile](#mobile)
- [Icons](#icons)
- [Architecture](#architecture)
- [Components reference](#components-reference)
- [What makes this robust](#what-makes-this-robust)
- [Contributing](#contributing)
- [Credits & support](#credits--support)
- [Licence](#licence)

## Live showcase

[![UI Toolkit Design System — interactive web showcase. Hover or tap to inspect, toggle day/night, click classes to copy.](docs/screenshots/design_system_showcase.gif)](https://sinanata.github.io/unity-ui-document-design-system/)

**[Open the interactive web demo →](https://sinanata.github.io/unity-ui-document-design-system/)** (click the gif, too)

Hover (desktop) or tap (mobile) any component to surface its **selector chain** — every parent class on the way down to the leaf, plus the leaf's class list ready to copy. Click the classes line to copy to clipboard. Toggle day / night in the COLORS section header and the whole tree retheme over 240 ms via the var() cascade. Slim themed scrollbar throughout, mobile flip below 768 px.

**World-space gallery (Unity 6000.5+).** The switch under the hero flips the showcase from the flat page into a walkable 3D corridor where every section hangs on the walls as a live world-space panel: same UXML, same USS, fully interactive (type in the inputs, flip the toggles, pick a codigrate theme from inside the gallery and every exhibit re-paints). Walk with W/A/S/D or the arrow keys, hold right-mouse or use Q/E to look, click anything, Esc or the Screen Space tab to exit. On touch devices two on-screen sticks handle walking and looking, so taps stay reserved for the components. Built on `PanelRenderer` + world-space `PanelSettings` with one panel per section, and driven by the same `DesignSystemRuntime` backends the drop-in ships.

**External theme provider.** The COLORS section also ships a dropdown of 12 [Codigrate](https://codigrate.com) IDE themes (Sequoia, Sakura, Tokyo, Paris, …) plus a `Randomize colors` button. Picking a codigrate theme fetches the palette at runtime (bundled fallback on WebGL since codigrate.com sends no CORS headers), maps its `tokens.interface` block to the DS palette, and stamps the result onto every component via inline styles — the DS USS stays the single source of truth for spacing, radii, transitions, and layout; only the colours flow from the external source. The day / night toggle is suppressed while a third-party palette is active (codigrate carries its own `appearance` field) and re-enabled when you select `Design System default`. `Randomize` generates an HSV-driven palette in the toggle's current mood for try-until-you-like-it exploration. See [CHANGELOG `[1.4.0]`](CHANGELOG.md#140--2026-05-16) for the full coverage matrix.

Build the demo locally any time:

```powershell
git clone --recurse-submodules https://github.com/sinanata/unity-ui-document-design-system
# or, after a plain clone: git submodule update --init --recursive
.\Tools\Build\Build-Showcase.ps1 -Serve
```

Serves at `http://localhost:3000`. The build flow lives in a shared [cross-platform orchestrator](https://github.com/sinanata/unity-cross-platform-local-build-orchestrator) vendored as a submodule at `Tools/.orchestrator/`. See [`Tools/Build/README.md`](Tools/Build/README.md) for daily usage — no cloud builds, no Unity license secret, entirely local.

The showcase covers 29 sections: colors (with the theme provider), typography, buttons, icons, inputs, tabs & filters, animal card, animal detail, navigation, badges & labels, toggles & checks, sliders, progress, modals / panels, toasts, empty states, tooltip, drag & drop, bottom sheet, confirm dialog, quantity stepper, pagination, loading states, notification badge, avatar, drawers, scrollbars, auto-hiding scrollbar — every one of them browsable flat or hung on the world-space gallery walls.

---

```
.ds-btn ds-btn--primary       →  rounded green CTA, hover/press/disabled built in
.ds-input    .ds-search       →  text fields with leading-icon slot + placeholder
.ds-tab      .ds-tabs         →  segmented strip with .is-active state
.ds-toggle   .ds-check        →  iOS-style switch + square checkbox (auto-knob via runtime)
.ds-modal    .ds-toast        →  overlays with header / body / actions slots
.ds-icon ds-icon--paw         →  60+ SVG icons, parent-state-driven tints
.mobile .ds-…                 →  one-class layout flip for touch targets
.theme-light                  →  add to .ds-root — every var(--color-*) re-paints, animated
```

## Why this exists

UI Toolkit ships great primitives but no design language. Every project re-invents tokens, button hierarchy, input shells, mobile breakpoints, modal scaffolding, and an icon system — usually inconsistently across screens, usually as a side-project of the actual game/app. This repo is that work, finished, kept evergreen by [a real shipping product](https://leapoflegends.com).

What you get on day one:

- **A dark-themed token palette** — primary / secondary / tertiary / warning / danger / surface stack, all referenced via `var(--color-...)`. Swap one token, the whole UI follows. The showcase ships a `.theme-light` override under `Assets/Showcase/Resources/ShowcaseTheme.uss` so you can see the cascade animate to a light palette in real time.
- **24 ready components** — buttons (5 variants × 4 states + icon + sizes), inputs (text / textarea / search / dropdown), tabs, toggles, checkboxes, radios, sliders + range, progress, modals, dialogs, drawers, toasts, badges, chips, tags, navigation (side / rail / bottom), avatars, notification dots, pagination, steppers, empty states, skeleton loaders, spinners.
- **63 SVG icons** — paw, shirt, hats, store, cart, plus arrows, chevrons, status glyphs, action icons. White-fill SVGs that tint via `-unity-background-image-tint-color` so the same artwork serves passive / hover / active / muted states.
- **One `.mobile` class** — add it to your screen root to flip every spacing token, tap target, and dropdown to touch-friendly sizes. Same UXML, same USS, two layouts.
- **A runtime helper with two backends** — a generic `DesignSystemRuntimeBase<T>` with concrete components for `UIDocument` (flat screens) and `PanelRenderer` (Unity 6000.5+ world-space panels). It auto-attaches in every scene, injects toggle knobs (Unity's `Toggle` doesn't render the iOS-style sliding pill on its own), drives spinner rotation (USS transitions can't loop), animates skeleton shimmer, and wires drag & drop.
- **Slim themed scrollbars** — 8 px-wide pill thumb in `var(--color-border-strong)` that brightens on hover, scoped to `.ds-root` so it doesn't leak into editor windows. Auto-themes with the rest of the system.

## Requirements

| Requirement | Notes |
| --- | --- |
| **Unity 6** (6000.x or newer) | Uses Unity 6 USS additions (`@import`, `background-size`, `-unity-background-image-tint-color`, parent-state cascades). Earlier versions partially work but components like the checkbox icon shrink rule rely on Unity 6 `background-size`. |
| **Unity 6000.5+** (optional) | Only for the world-space path: the `PanelRenderer` runtime backend and the showcase's 3D gallery compile out on older editors behind `UNITY_6000_5_OR_NEWER`, so the flat-screen system still works everywhere. This repo's host project sits on 6000.5.2f1. |
| `com.unity.ui` (UI Toolkit) | Built-in module — already enabled by default in Unity 6. |
| `com.unity.modules.vectorgraphics` | Built-in module in Unity 6 — already enabled by default. The standalone `com.unity.vectorgraphics` *package* is not required; Unity 6's engine ships the SVG ScriptedImporter (`fileID: 12408`) directly. The repo ships `.meta` files for every icon preset to `svgType: 3` (Texture) so they import correctly on first open. |

No other external dependencies. No NuGet, no asmdef requirements, no editor scripts.

## Installation

The design system is a single folder you drop into your project's `Assets/`:

```
your-unity-project/
└── Assets/
    └── DesignSystem/                  ← drop the whole folder
        ├── package.json               ← also consumable as a UPM package
        ├── Resources/
        │   ├── UI/Styles/DesignSystem/    ← USS + UXML showcase
        │   └── Textures/Icons/            ← 63 SVG icons
        ├── Runtime/
        │   ├── DesignSystemRuntimeBase.cs     ← generic behaviors (knobs, spinners, shimmer, drag & drop)
        │   ├── UIDocumentRuntime/             ← backend for flat screens
        │   └── PanelRendererRuntime/          ← backend for world-space panels (6000.5+)
        └── Editor/                    ← stylesheet-attach menu helper
```

**Option A — copy files:**

```powershell
# From your Unity project root, on Windows or macOS:
git clone https://github.com/sinanata/unity-ui-document-design-system ../design-system-src
cp -r ../design-system-src/Assets/DesignSystem Assets/DesignSystem
```

**Option B — git submodule (recommended for keeping the system updated):**

The submodule must live **outside** `Assets/` and the drop-in folder gets linked into `Assets/DesignSystem`. Putting the submodule directly under `Assets/` would make Unity import this repo's host project (`Assets/Showcase/`, `Assets/Editor/`, `Assets/WebGLTemplates/`) into the consuming project — and a symlink alongside that would produce duplicate-GUID errors.

```bash
cd your-unity-project
git submodule add https://github.com/sinanata/unity-ui-document-design-system Vendor/unity-ui-document-design-system
```

Then create an OS-level link from `Assets/DesignSystem` to the vendored copy:

```powershell
# Windows — directory junction (no admin / Developer Mode required)
cmd /c mklink /J Assets\DesignSystem Vendor\unity-ui-document-design-system\Assets\DesignSystem
```

```bash
# macOS / Linux — symbolic link
ln -s ../Vendor/unity-ui-document-design-system/Assets/DesignSystem Assets/DesignSystem
```

Add the link itself to your `.gitignore` so each contributor re-creates it after their first clone (the link path is per-OS and per-clone state — junctions can't roundtrip through git, and symlinks don't roundtrip cleanly across Windows / *nix):

```gitignore
# Per-clone link to the vendored design system
Assets/DesignSystem
Assets/DesignSystem.meta
```

> **Working example:** [unity-mesh-fracture](https://github.com/sinanata/unity-mesh-fracture) consumes the design system this way — see the "Cloning this demo project" section of its README for the end-to-end recipe.

**Option C — via git URL:**

Unity's Package Manager supports Git URLs directly. Add the design system as a package using one of these two methods:

- **Add to `Packages/manifest.json`:**
  ```json
  {
    "dependencies": {
      "com.sinanata.designsystem": "https://github.com/sinanata/unity-ui-document-design-system.git?path=/Assets/DesignSystem"
    }
  }
  ```

- **Or use the Package Manager window:** go to **Window → Package Manager → ＋ → "Add package from git URL..."** and paste:
  ```
  https://github.com/sinanata/unity-ui-document-design-system.git?path=/Assets/DesignSystem
  ```

This installs the design system as an immutable package under `Packages/com.sinanata.designsystem/` — ideal for projects that prefer package-manager workflows and don't need to edit the system source.

After Unity reimports, every screen with a UIDocument can opt into the system by attaching the master stylesheet:

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements">
  <Style src="project://database/Assets/DesignSystem/Resources/UI/Styles/DesignSystem/DesignSystem.uss" />

  <ui:VisualElement class="ds-root">
    <ui:Button text="Get started" class="ds-btn ds-btn--primary" />
  </ui:VisualElement>
</ui:UXML>
```

That's it. The runtime auto-attaches to every UIDocument; no per-screen wiring needed.

## Quick start

```xml
<!-- A login form, fully styled, no inline CSS -->
<ui:VisualElement class="ds-root" style="padding: 24px;">

  <ui:Label text="Sign in" class="ds-h2" style="margin-bottom: 16px;" />

  <ui:VisualElement class="ds-search" style="margin-bottom: 8px;">
    <ui:VisualElement class="ds-icon ds-icon--sm ds-icon--user ds-search__icon" />
    <ui:TextField class="ds-search__field" />
  </ui:VisualElement>

  <ui:TextField class="ds-input" style="margin-bottom: 16px;" password="true" />

  <ui:VisualElement class="ds-row" style="justify-content: space-between; margin-bottom: 16px;">
    <ui:Toggle class="ds-check" />
    <ui:Label text="Remember me" class="ds-body-1" />
  </ui:VisualElement>

  <ui:Button text="Sign in" class="ds-btn ds-btn--primary ds-btn--block" />

</ui:VisualElement>
```

Set `searchField.textEdition.placeholder = "Username"` from C# and you have a complete, themed, mobile-ready form in 12 lines of UXML.

## Mobile

Add the `.mobile` class to your screen root and the entire stylesheet flips:

```csharp
if (Screen.width < 768)              // or your own platform check
    root.AddToClassList("mobile");
```

- Buttons grow from 36 px to 48 px (touch target minimum).
- Inputs grow to 48 px height with 15 px font.
- Tabs spread to 48 px tall.
- Sliders / range thumbs grow 18 px → 24 px with recomputed centering.
- Modals widen, side rails compact, bottom-nav bar takes over from side-nav.

All in `Mobile.uss` — one file, ~350 lines, parallel-class structure to the desktop tokens. Full pattern in [docs/MOBILE.md](docs/MOBILE.md).

## Icons

63 white-fill SVGs under `Resources/Textures/Icons/`. Each has a class in `Icons.uss`:

```xml
<!-- Default tint = text-secondary -->
<ui:VisualElement class="ds-icon ds-icon--paw" />

<!-- Tint variant -->
<ui:VisualElement class="ds-icon ds-icon--lg ds-icon--accent" />

<!-- Inside a button — tint follows the button's hover/active state -->
<ui:Button class="ds-btn ds-btn--icon">
  <ui:VisualElement class="ds-icon ds-icon--search" />
</ui:Button>
```

Adding a new icon: drop the SVG into `Resources/Textures/Icons/`, set `svgType: 3` (Texture) in the importer, add one line to `Icons.uss`. SVGs ship as `fill="white"` so the design system's tint cascade can multiply onto any colour. Black-fill SVGs render black regardless of tint — see [docs/ICONS.md](docs/ICONS.md) for why and how.

## Architecture

```
Assets/
├── DesignSystem/                       ← the drop-in design system
│   ├── Resources/UI/Styles/DesignSystem/
│   │   ├── DesignSystem.uss            ← master, @imports the rest in order
│   │   ├── DesignTokens.uss            ← :root variables (colors, radii, spacing, motion)
│   │   ├── Typography.uss              ← .ds-h1 / .ds-h2 / .ds-h3 / .ds-body-1 / .ds-caption
│   │   ├── Icons.uss                   ← .ds-icon + 63 .ds-icon--<name> + state cascade
│   │   ├── Buttons.uss                 ← .ds-btn + variants + sizes + icon button
│   │   ├── Inputs.uss                  ← .ds-input / .ds-search / .ds-dropdown / .ds-textarea
│   │   ├── TabsAndFilters.uss          ← .ds-tabs / .ds-tab / .ds-view-toggle
│   │   ├── Cards.uss                   ← animal card, info row
│   │   ├── Navigation.uss              ← .ds-side-nav / .ds-side-rail / .ds-bottom-nav / profile
│   │   ├── Badges.uss                  ← .ds-badge / .ds-tag / .ds-chip / .ds-avatar / notif dot
│   │   ├── Controls.uss                ← .ds-toggle / .ds-check / .ds-radio / .ds-slider / .ds-range / scrollbars
│   │   ├── Overlays.uss                ← .ds-modal / .ds-dialog / .ds-toast / .ds-sheet / empty
│   │   ├── Feedback.uss                ← .ds-progress / .ds-spinner / .ds-skeleton / .ds-pagination
│   │   ├── Mobile.uss                  ← every .mobile-prefixed responsive override (loaded LAST)
│   │   └── DesignSystemShowcase.uxml   ← living style guide
│   ├── Runtime/
│   │   ├── DesignSystemRuntimeBase.cs  ← generic behaviors (knobs, spinners, shimmer, drag & drop)
│   │   ├── UIDocumentRuntime/          ← auto-attaches to every UIDocument
│   │   └── PanelRendererRuntime/       ← auto-attaches to every PanelRenderer (6000.5+)
│   ├── Editor/EditorHelpers.cs         ← attach-DesignSystem.uss menu action
│   └── package.json                    ← UPM package (com.sinanata.designsystem)
│
├── Showcase/                           ← showcase host project (only if cloning the repo)
│   ├── Showcase.unity                  ← minimal scene; bootstrap creates UIDocuments at runtime
│   ├── Resources/
│   │   ├── ShowcaseTheme.uss           ← .theme-light override + universal opacity transition + drawer-frame helpers
│   │   ├── ShowcaseDropdownPopup.uss   ← popup chrome at panel.visualTree scope (sibling of root)
│   │   ├── ShowcaseFocusRing.uss       ← :focus rules for keyboard / gamepad navigation
│   │   ├── DefaultPanelSettings.asset  ← base PanelSettings cloned per panel
│   │   ├── CorridorLit.mat             ← Simple Lit base for the 3D gallery (pins shader variants into WebGL builds)
│   │   ├── UnityDefaultRuntimeTheme.tss
│   │   ├── sinanata.jpg                ← avatar texture (Showcase only)
│   │   └── CodigrateThemes/            ← 13 bundled JSONs (list + 12 palettes), WebGL fallback
│   └── Runtime/
│       ├── ShowcaseBootstrap.cs        ← spawns docs, wires toggle, theme dropdown, promo links, world-mode switch
│       ├── WorldSpaceCorridor.cs       ← 3D gallery: one world-space panel per section, fit + plates + theming
│       ├── FirstPersonController.cs    ← walker (keyboard/mouse + WorldNavInput touch-stick bridge)
│       ├── ShowcaseModeHud.cs          ← in-gallery chrome: mode tabs, hints, virtual sticks
│       ├── ShowcaseDocOverlay.cs       ← selector-chain hover overlay
│       ├── CodigrateThemeProvider.cs   ← UnityWebRequest fetch + bundled fallback
│       ├── CodigrateThemeApplier.cs    ← maps codigrate colours onto every .ds-* class via inline styles
│       └── WebGLDevicePixelRatio.jslib ← exposes window.devicePixelRatio for HiDPI panel scale
│
├── Settings/                           ← URP pipeline + renderer assets (the scene's actual pipeline)
├── Editor/BuildCli.cs                  ← Unity batchmode entry for WebGL builds (+ probe utilities)
└── WebGLTemplates/ShowcaseTemplate/    ← custom WebGL template (mobile-friendly)

Tools/Build/
├── Build-Showcase.ps1                  ← shim: forwards to orchestrator submodule with our title + method + URL
├── config.example.json                 ← copy to config.local.json (gitignored)
└── README.md                           ← orchestrator docs

Tools/.orchestrator/                    ← submodule: unity-cross-platform-local-build-orchestrator
└── Tools/Build/
    ├── Build-WebGL.ps1                 ← parameter-driven WebGL flow (lockfile cleanup, Burst retry, ...)
    └── Deploy-GhPages.ps1              ← single-commit force-push via git worktree
```

Import order is load-bearing — Inputs.uss specialises selectors that Icons.uss generalises; Mobile.uss intentionally loads last so its specificity always wins. Don't reorder unless you read the comments first.

The `Assets/Showcase/`, `Assets/Editor/`, `Assets/WebGLTemplates/`, and `Tools/Build/` folders are the **host project** that runs the live demo. They're not part of the drop-in design system — if you copy `Assets/DesignSystem/` into your own project, leave them behind. Use them when you clone this repo to iterate on the design system itself.

Full architectural reasoning in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Build pipeline docs in [Tools/Build/README.md](Tools/Build/README.md).

## Components reference

One-line summary per component lives in [docs/COMPONENTS.md](docs/COMPONENTS.md). The showcase UXML is the second source of truth — every class appears there with its expected DOM structure.

## What makes this robust

- **Tokens, not hex.** Every colour, radius, spacing, motion timing references a `var(--…)` variable. Theme by editing one file (`DesignTokens.uss`).
- **Parent-state cascades for icons.** A `.ds-icon` inside `.ds-btn:hover` retints automatically — you don't write per-component `:hover .icon` rules.
- **Per-axis radius tokens.** Unity 6 USS clamps `border-radius` per axis to half the element's side. We ship `--radius-pill-9 / -16 / -20 / -28 / -36` so a circle stays a circle and a pill stays a pill regardless of element height.
- **Auto-knob injection.** Unity's `Toggle` doesn't render the sliding pill on its own. `DesignSystemRuntime` injects a `.ds-toggle__knob` child every 250 ms — covers UXML-authored screens, C#-cloned templates, and world-space panels alike.
- **No `Resources.Load<Texture2D>` in C#.** Icons resolve via USS `resource(...)` so they survive Sprite-vs-Texture import differences. The runtime never touches a backgroundImage.
- **MinMaxSlider thumbs cross-centred via `top: 50% + margin-top: -<half>px`** — Unity's stock slider positions thumbs at `top: 0` which floats them above the track. Same trick for the single slider.
- **Checkbox tick shrunk via `background-size: 12px 12px`** — the `check.svg` viewBox runs path-edge to viewBox-edge; default `stretch-to-fill` made the tick overflow the box's 2 px border. Constraining the rendered size leaves a clean inner margin.
- **Day / night theme via single class.** Adding `.theme-light` to `.ds-root` redefines every colour token under that scope; the var() cascade re-paints the whole tree. A universal `transition-property` in `ShowcaseTheme.uss` animates the swap over 240 ms. Same pattern works for any custom theme — just author the token block.
- **Progress-bar `min-height: 0` overrides.** Unity's stock `.unity-progress-bar` ships with `min-height: 21px`. `.ds-progress` resets it to 0 across container, background, and progress layers so an 8 px bar reserves exactly 8 px of vertical space (not the 21 px Unity defaults to).
- **Spinner rotation is C#-driven, no USS transition.** `DesignSystemRuntime.StartSpinners` writes `style.rotate` every 16 ms. We deliberately omit `transition-property: rotate` from `.ds-spinner` — a transition would try to ease between consecutive per-frame writes and the spinner visibly jiggles instead of spinning.

Every "why is this ugly?" complaint we hit while shipping the game lives as a comment on the rule that fixed it. Read the USS files — half of them are documentation.

## Contributing

Issues and PRs welcome. The whole system is ~1700 lines of USS plus a small runtime (a generic base and two thin backends) — readable in an afternoon, hackable in a weekend.

Areas where help is especially useful:

- Light theme — the token structure supports it (just override `:root`); a polished `LightTokens.uss` is on the roadmap.
- Localisation — RTL flips for nav-item icons / chevrons / arrow buttons, and a doc note on which classes need a mirror modifier.
- Additional icons — particularly platform glyphs (Steam / Apple / Google), gameplay glyphs (D-pad, button prompts), and currency icons.
- Editor scripts — a Unity menu item that toggles `.mobile` on the active UIDocument's root for live preview, like the showcase but for any screen.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the naming convention, file-load order, and PR checklist.

## Credits & support

Made for **[Leap of Legends](https://leapoflegends.com)**, a cross-platform physics-heavy multiplayer game now live with cross-play on Steam, iOS, Android, and Mac. If this design system saved you time:

- ⭐ Star the repo
- 🎮 Play Leap of Legends: [Steam](https://store.steampowered.com/app/2269500/) · [App Store](https://apps.apple.com/us/app/leap-of-legends/id6761757484) · [Google Play](https://play.google.com/store/apps/details?id=com.exceptionly.leapoflegends)
- 🐦 Shout out [@sinanata](https://x.com/sinanata)

## Licence

MIT — see [LICENSE](LICENSE). Free for commercial use. No warranty.

The 63 SVG icons under `Resources/Textures/Icons/` are released under the same MIT licence — use them in your own projects, ship them in commercial products, modify them freely.

---

**[Leap of Legends](https://leapoflegends.com)** · physics · multiplayer · cross-platform · out now on Steam, the App Store, and Google Play · the UI you see in every screenshot was built with this system.
