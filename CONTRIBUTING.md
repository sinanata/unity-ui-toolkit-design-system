# Contributing

Thanks for considering a contribution. The design system is small (~1700 lines USS + 180 lines C#) and intentionally so — every rule earns its keep against the showcase, against the live game, or against a documented Unity quirk. Read the existing files before adding new ones; they double as documentation.

## Ground rules

1. **One style for one job.** A new component should not duplicate an existing one with a different prefix. If you need a "card with a price tag", extend `.ds-animal-card` with a `__price-tag` slot, don't fork it.
2. **Tokens, never hex.** Every colour, radius, spacing, motion timing must reference a `var(--…)` from `DesignTokens.uss`. If your design needs a value that doesn't exist as a token, add the token first (one PR), then the rule (next PR).
3. **Comments answer "why", not "what".** A reader can see a `width: 18px;` declaration. The comment should explain *why 18px and not 16 or 20* — for example "= half of the 36 px button so the icon centres without `align-items: center` on every consumer."
4. **The showcase is the test suite.** Every new component must appear in `DesignSystemShowcase.uxml` in at least one state. If your rule has hover / pressed / active / disabled / `--variant` modifiers, render each one. PRs that don't update the showcase get bounced.
5. **No `var(...)` in inline UXML styles.** Unity 6's clone-time `StyleVariableResolver` NREs when it encounters `var(...)` inside a `style="..."` attribute on a UXML element — it crashes the whole `VisualTreeAsset` clone with "The UXML file set for the UIDocument could not be cloned." Author colour and dimension overrides as USS classes (e.g. `.ds-scrollbar-demo`, `.ds-swatch--primary`) and reserve inline styles for one-off literal values. Inside the USS files themselves, `var(...)` works perfectly.

## Naming convention

The system uses **BEM-ish naming with a `ds-` prefix**:

```
.ds-<block>                       e.g. .ds-btn, .ds-card, .ds-toast
.ds-<block>__<element>            e.g. .ds-toast__icon, .ds-card__footer
.ds-<block>--<modifier>           e.g. .ds-btn--primary, .ds-toast--success
.ds-<block>.is-<state>            e.g. .ds-tab.is-active, .ds-pagination__btn.is-active
```

State classes (`.is-active`, `.is-loading`, `.is-disabled`) are prefixed `is-` to distinguish them from variant modifiers.

For icons: `.ds-icon--<glyph-name>` resolves the SVG via `resource("Textures/Icons/<glyph-name>")`. Hyphenate multi-word names (`.ds-icon--more-horizontal` for `more_horizontal.svg` — the USS class uses hyphens, the file uses underscores; the mapping is explicit in `Icons.uss`).

## File-load order matters

`DesignSystem.uss` `@import`s the subsystems in a specific order. Specificity ties resolve by source order, so a rule loaded later wins over a rule loaded earlier with the same selector specificity:

```
1.  DesignTokens.uss        → :root variables (no element rules)
2.  Typography.uss          → text classes
3.  Icons.uss               → .ds-icon base + parent-state cascade
4.  Buttons.uss
5.  Inputs.uss              → .ds-search__icon overrides .ds-icon size
6.  TabsAndFilters.uss
7.  Cards.uss               → .ds-info-row__icon overrides .ds-icon size
8.  Navigation.uss          → .ds-nav-item__icon overrides .ds-icon size
9.  Badges.uss              → .ds-chip__icon overrides .ds-icon size
10. Controls.uss
11. Overlays.uss
12. Feedback.uss
13. Mobile.uss              → all .mobile overrides — loaded LAST so they always win
```

When you add a component file, append it to `DesignSystem.uss`'s import block in the right slot (typically before `Mobile.uss`). Don't reorder existing imports without reading the comments — Inputs.uss's `.ds-search__icon` rule is intentionally loaded after `Icons.uss` so its 18×18 size wins over the default `.ds-icon` 20×20.

## Where rules live

When in doubt, here's the routing:

| Component / class | File |
| --- | --- |
| Tokens (color, spacing, radius, motion) | `DesignTokens.uss` |
| `.ds-h1` / `.ds-body-1` / `.ds-caption` | `Typography.uss` |
| `.ds-icon` and per-glyph mappings | `Icons.uss` |
| `.ds-btn` and variants | `Buttons.uss` |
| Anything that hosts text input | `Inputs.uss` |
| Tab strips, view toggles, filter rows | `TabsAndFilters.uss` |
| Bordered surfaces with content (cards, info rows) | `Cards.uss` |
| Navigation containers (side / rail / bottom / profile) | `Navigation.uss` |
| Pills, chips, tags, avatars, notification dots | `Badges.uss` |
| Toggles, checkboxes, radios, sliders, range, progress, **scrollbars** | `Controls.uss` |
| Modal / dialog / drawer / toast / empty-state | `Overlays.uss` |
| Pagination, stepper, spinner, skeleton, **swatch + section helpers** | `Feedback.uss` |
| Anything `.mobile`-prefixed | `Mobile.uss` |
| Showcase-only theme overrides (`.theme-light`, the `ds-no-transition` swap guard) | `Assets/Showcase/Resources/ShowcaseTheme.uss` |

If your new rule doesn't fit any file cleanly, you've probably invented a new component family. Make a new file (`<Family>.uss`), add the import to `DesignSystem.uss`, document it in `docs/COMPONENTS.md`.

## Previewing your changes

Two ways, depending on how invasive your change is:

**Editor only.** Open the project in Unity Hub, open `Assets/Showcase/Showcase.unity`, hit Play. The bootstrap loads the showcase and your USS edits are reflected on first frame. Hover any element to see its selector chain.

**WebGL build (matches what visitors see).** From the repo root:

```powershell
.\Tools\Build\Build-Showcase.ps1 -Serve
```

Builds the showcase to `build/WebGL/` and serves it at `http://localhost:3000`. First build takes ~5 min (Unity asset reimport); subsequent builds with the Library cache warm take ~2 min. Use Chrome DevTools' device toolbar to verify the `.mobile` flip and the styled scrollbar at narrow widths. See [`Tools/Build/README.md`](Tools/Build/README.md) for the full pipeline.

## Adding a new icon

1. Drop the SVG into `Assets/DesignSystem/Resources/Textures/Icons/`.
2. **Make sure the SVG fills are `white`** — `fill="white"` and `stroke="white"`. Black-fill SVGs render black regardless of tint because `-unity-background-image-tint-color` is multiplicative (`black × any_colour = black`). The bulk-conversion script in our 2026-05-01 commit converted 63 source icons; new contributions must arrive white-filled.
3. After Unity reimports, set the importer's `SVG Type` to **Texture** (not Sprite, not VectorImage). The asset pipeline flag is `svgType: 3` in the `.svg.meta` file.
4. Add one line to `Icons.uss`:
    ```css
    .ds-icon--newglyph        { background-image: resource("Textures/Icons/newglyph"); }
    ```
5. Render it in `DesignSystemShowcase.uxml` under the ICONS section.
6. Open a PR with a screenshot of the showcase row.

## Adding a new component

1. **Sketch the DOM.** Decide which existing element type owns the block. `.ds-btn` is a `<ui:Button>`; `.ds-chip` is a `<ui:VisualElement>` with two children. Decide before you write USS.
2. **Write the rule.** Use existing tokens. Reference existing component patterns (status chips for "icon + label" layout, modals for "header + body + actions" scaffolding).
3. **Add hover / active / disabled / state variants** if the component is interactive. Don't ship a button without a hover state.
4. **Add a mobile override** in `Mobile.uss` if the component changes shape on touch — typically growing the tap target to 48 px.
5. **Render it in the showcase** in the appropriate section.
6. **Document it** in `docs/COMPONENTS.md` with a one-line description and the expected DOM.

## Pull request checklist

- [ ] Rule uses tokens, no inline hex / px / ms (except where commented as load-bearing).
- [ ] Showcase UXML updated with all states / variants of the new rule.
- [ ] No `var(...)` in inline UXML `style="..."` attributes (use a class).
- [ ] `Mobile.uss` updated if the component has a touch-tier override.
- [ ] `docs/COMPONENTS.md` line added (or relevant doc updated).
- [ ] `CHANGELOG.md` entry under the unreleased section.
- [ ] No `using LeapOfLegends.*` or product-specific imports in C# changes.
- [ ] No `Resources.Load<Texture2D>` in new C# — icons resolve via USS `resource(...)`.
- [ ] Tested in the editor with the showcase scene; tested at desktop and `.mobile` widths.
- [ ] Tested via `Tools\Build\Build-Showcase.ps1 -Serve` and confirmed the rendered WebGL build matches the editor (catches `var()`-in-inline-UXML crashes and mobile-breakpoint regressions).

## Reporting bugs

If a component looks wrong:

1. **Reproduce in the showcase** if possible. If the bug only appears in your project, attach a minimal UXML.
2. **Include Unity version.** Unity 6 USS additions (`background-size`, `background-position`, `@import`) behave differently across `6000.0.x` patch releases.
3. **Note your render pipeline.** URP / HDRP / built-in all share the UI Toolkit panel renderer, but font rendering differs.
4. **Screenshot the broken state next to the showcase's expected state.** A diff is worth a thousand words.

## Licence

By contributing you agree your contributions are released under the project's MIT licence. See [LICENSE](LICENSE).
