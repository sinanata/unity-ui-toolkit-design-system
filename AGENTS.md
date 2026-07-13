# AGENTS.md

Guidance for AI coding agents (Codex, Cursor, GitHub Copilot, Claude Code, Windsurf, Aider, Zed, and others) working in this repository or consuming this package in another project. Humans: this is a fast, accurate map. The deeper docs are linked at the bottom.

## What this project is

A drop-in design system for **Unity 6 UI Toolkit** (UIDocument and PanelRenderer, UXML and USS). It ships design tokens, 24 components, 63 SVG icons, a one-class mobile flip, and a small auto-attaching C# runtime. Everything is themed dark and editable from a single stylesheet. Package id: `com.sinanata.designsystem`. License: MIT. The same components render on flat screens and, on Unity 6000.5+, in world space. Both `UIDocument` and `PanelRenderer` can host flat or world-space UI; the showcase uses `PanelRenderer` for its world-space gallery, and Unity now lists `UIDocument` under UI Toolkit > Legacy while keeping it fully supported (not `[Obsolete]`, still world-space capable). Prefer `PanelRenderer` for new work.

## Golden rules (do not violate)

1. **Style with tokens and classes, never hardcoded values.** Every color, radius, spacing, and motion value comes from a `var(--...)` token in `DesignTokens.uss`. Do not write raw hex, px, or ms in component rules. If a value you need has no token, add the token first, then reference it.
2. **Never put `var(...)` in an inline UXML `style="..."` attribute.** Unity 6's clone-time `StyleVariableResolver` throws and the whole `VisualTreeAsset` fails to clone ("The UXML file set for the UIDocument could not be cloned"). Author a USS class and add the class in UXML instead. `var(...)` is fine inside `.uss` files.
3. **Class naming is BEM with a `ds-` prefix.** Block `.ds-btn`, element `.ds-btn__icon` (double underscore), modifier `.ds-btn--primary` (double hyphen), state `.is-active` / `.is-open` / `.is-spinning` (prefixed `is-`). Do not invent a new prefix or fork an existing component under a new name.
4. **The showcase is the test suite.** Every component, state, and variant must appear in `Assets/Showcase/Resources/DesignSystemShowcase.uxml`. A rule change that does not update the showcase is incomplete.
5. **`.meta` files are tracked on purpose.** Do not add them to `.gitignore`. They carry `svgType: 3` for icons and the asmdef import settings consumers rely on.

## Use the design system in your project

1. Install one of three ways (README, "Installation"): copy `Assets/DesignSystem/` into your project, add it as a git submodule plus an OS-level link, or add the UPM git URL `https://github.com/sinanata/unity-ui-toolkit-design-system.git?path=/Assets/DesignSystem`.
2. Attach the master stylesheet to your UIDocument's UXML and put `ds-root` on the top element:
   ```xml
   <Style src="project://database/Assets/DesignSystem/Resources/UI/Styles/DesignSystem/DesignSystem.uss" />
   <ui:VisualElement class="ds-root">
     <ui:Button text="Get started" class="ds-btn ds-btn--primary" />
   </ui:VisualElement>
   ```
3. Build screens by composing `ds-*` classes. The canonical list of every class, its DOM, and its states is `docs/COMPONENTS.md`; the showcase UXML is the second source of truth.
4. For touch layouts, add `mobile` to the screen root (`root.AddToClassList("mobile")`). Same UXML, same classes, flipped sizing.
5. Theme by overriding tokens in a stylesheet attached after `DesignSystem.uss`. Scope the overrides under a class (for example `.theme-light`) to switch at runtime. See `docs/ARCHITECTURE.md`.
6. You do not wire the runtime. `DesignSystemBehaviour` auto-attaches to every UIDocument (and every PanelRenderer on 6000.5+) and injects toggle knobs, drives spinner rotation, animates skeleton shimmer, and wires drag and drop. If you clone templates lazily and want to avoid a one-frame flat-toggle flash, call the runtime's `EnsureToggleKnobs(root)` helper after the clone (see `docs/ARCHITECTURE.md`).

## Repository layout

- `Assets/DesignSystem/` is the shippable package, and the only folder a consumer copies.
  - `Resources/UI/Styles/DesignSystem/`: 14 USS files. `DesignSystem.uss` is the master that `@import`s the rest in a load-bearing order.
  - `Resources/Textures/Icons/`: 63 white-fill SVGs.
  - `Runtime/Behaviour/`: `DesignSystemBehaviourBase<TComponent>` plus `UIDocument/` and `PanelRenderer/` backends.
  - `Runtime/Theme/`: `ThemeData` asset, `ThemeApplierBase<T>` + two concrete backends, `ThemeConfiguratorWindow`, `ThemeDataEditor`.
  - `Editor/EditorHelpers.cs`: a menu action that attaches the stylesheet.
- `Assets/Showcase/`, `Assets/Editor/`, `Assets/WebGLTemplates/`, and `Tools/` are the **host project** that builds the live web demo. They are not part of the package. Do not copy them into a consuming project, and do not add product-specific dependencies to the package's own C#.
- `docs/`: ARCHITECTURE.md, COMPONENTS.md, ICONS.md, MOBILE.md.

## Conventions when editing the system

- **File-load order is load-bearing.** `DesignSystem.uss` imports Tokens, Typography, Icons, Buttons, Inputs, TabsAndFilters, Cards, Navigation, Badges, Controls, Overlays, Feedback, then **Mobile last**. Specificity ties resolve by source order, so a later file specializes an earlier one (for example `.ds-search__icon` at 18px wins over `.ds-icon` at 20px). Mobile loads last so `.mobile` overrides always win. Do not reorder without reading the comments.
- **Where a rule lives:** tokens in `DesignTokens.uss`, text in `Typography.uss`, icons in `Icons.uss`, and so on. The full routing table is in `CONTRIBUTING.md`. A new component family gets a new `<Family>.uss` appended to the import chain before `Mobile.uss`.
- **Icons are white-fill SVGs** imported as `svgType: 3` (Texture) and tinted via `-unity-background-image-tint-color`. Black-fill SVGs render black regardless of tint. To add one, drop the SVG in `Resources/Textures/Icons/`, set SVG Type to Texture, and add one line to `Icons.uss`: `.ds-icon--name { background-image: resource("Textures/Icons/name"); }` (the class uses hyphens, the file uses underscores).
- **No `Resources.Load<Texture2D>` for icons in C#.** Icons resolve via USS `resource(...)`.
- **No `using LeapOfLegends.*` or other product-specific imports** in the package's C#.
- Comments explain **why**, not what (for example, why 18px and not 16).

## Build, preview, and validate

Windows-first Unity 6 project (host editor 6000.5.2f1). There is no unit-test suite; validation is visual, through the showcase.

- **Editor preview:** open the project in Unity Hub, open `Assets/Showcase/Showcase.unity`, press Play. USS edits show on the next frame. Hover any element to read its selector chain.
- **WebGL build (what visitors see), from the repo root in PowerShell:**
  ```powershell
  git submodule update --init --recursive   # first time: the build orchestrator is a submodule
  .\Tools\Build\Build-Showcase.ps1 -Serve   # builds to build/WebGL/ and serves http://localhost:3000
  ```
  `-Serve` runs a local server, `-Deploy` force-pushes a single commit to `gh-pages`, `-ClearCache` recovers from a stale Burst cache. First build is about 5 minutes; warm builds about 2.
- **Verify UI changes at both desktop and `.mobile` widths**, and confirm the WebGL build matches the editor. That is what catches the `var()`-in-inline-UXML crash and mobile-breakpoint regressions.

## Pull request checklist (summary; full list in CONTRIBUTING.md)

- Rules use tokens, no raw hex, px, or ms (except where a comment marks it load-bearing).
- Showcase UXML updated with every state and variant.
- No `var(...)` in an inline UXML `style=` attribute.
- `Mobile.uss` updated if the component has a touch tier.
- `docs/COMPONENTS.md` line added or updated.
- `CHANGELOG.md` entry added (Keep a Changelog format).

## Deeper docs

- Full class reference: `docs/COMPONENTS.md`
- Architecture and rationale: `docs/ARCHITECTURE.md`
- Icons: `docs/ICONS.md`. Mobile: `docs/MOBILE.md`.
- Contribution rules: `CONTRIBUTING.md`
- Machine-readable index: `llms.txt`
