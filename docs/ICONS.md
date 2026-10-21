# Icons

The design system ships **121 SVG icons** under `Assets/DesignSystem/Resources/Textures/Icons/`. Every icon is rendered via the same class system: a base `.ds-icon` for sizing + tint cascade, plus a per-glyph `.ds-icon--<name>` that resolves the SVG.

## Anatomy

```xml
<ui:VisualElement class="ds-icon ds-icon--paw" />
```

This produces a 20 × 20 element with the paw SVG painted via `background-image`, tinted with `--color-text-secondary` by default. Inside an interactive parent (`.ds-btn`, `.ds-tab`, `.ds-nav-item`, etc.), the tint flips automatically based on the parent's hover / active / pressed / disabled state.

## Sizes

```
.ds-icon              → 20 × 20  (default)
.ds-icon--xs          → 12 × 12
.ds-icon--sm          → 16 × 16
.ds-icon--md          → 20 × 20  (alias of default)
.ds-icon--lg          → 24 × 24
.ds-icon--xl          → 32 × 32
.ds-icon--xxl         → 48 × 48
```

Component slot rules (`.ds-search__icon`, `.ds-info-row__icon`, `.ds-chip__icon`, etc.) override these with their own per-component dimensions. The class load order is set up so per-component slot sizes win — see [ARCHITECTURE.md § Why the import order matters](ARCHITECTURE.md#why-the-import-order-matters).

## Tint variants

When a glyph needs a colour that doesn't follow the parent state — e.g. a sparkle that should always render gold:

```xml
<ui:VisualElement class="ds-icon ds-icon--sm ds-icon--sparkle ds-icon--gold" />
```

Available tint variants:

```
--primary            text-primary (near-white)
--secondary          text-secondary (default — usually omit)
--disabled           text-disabled (gray)
--accent             primary green
--gold               warning amber (for "rare/featured" markers)
--danger             red
--warning            amber
--info               blue
--on-accent          dark ink (for use over primary-green fills)
--rarity-common      green
--rarity-rare        blue
--rarity-epic        purple
--rarity-legendary   amber
```

## Parent-state cascade

`Icons.uss` declares a chain of selectors that retint `.ds-icon` based on its parent's state:

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

This means a `.ds-icon` inside a `.ds-btn:hover` brightens automatically — no per-button rule needed.

If you author a new interactive container, add its selector to the cascade:

```css
/* In Icons.uss */
.ds-btn:hover .ds-icon,
...
.ds-my-new-container:hover .ds-icon {
    -unity-background-image-tint-color: var(--color-text-primary);
}
```

Same pattern for `:active`, `.is-active`, `:disabled`.

### When NOT to use the cascade

Some interactive containers — `.ds-nav-item.is-active`, `.ds-rail-item.is-active`, `.ds-bottom-nav__item.is-active` — paint the icon with primary green on a soft fill rather than on-accent on a green fill. Their per-`__icon` rules in their own files (`Navigation.uss`) own the active-state tint. The cascade list in `Icons.uss` intentionally **omits** these; if you add them, you'll double-override the per-class rule.

## The set

All 121 glyphs, by the group they are declared in inside `Icons.uss`. The class is the file name with underscores hyphenated (`vr_headset.svg` is `.ds-icon--vr-headset`).

| Group | Icons |
| --- | --- |
| **Arrows** | `arrow-up`, `arrow-down`, `arrow-left`, `arrow-right`, `chevron-up`, `chevron-down`, `chevron-left`, `chevron-right`, `sort-asc`, `sort-desc` |
| **Status / feedback** | `check`, `close`, `info`, `error`, `warning`, `help`, `smile`, `frown` |
| **Actions** | `plus`, `edit`, `trash`, `share`, `refresh`, `sync`, `search`, `filter`, `more-horizontal`, `more-vertical`, `menu`, `list`, `lock`, `unlock`, `eye`, `settings`, `bell`, `clock`, `calendar`, `user`, `home`, `target` |
| **Game / cosmetics** | `paw`, `shirt`, `hats`, `store`, `cart`, `bag`, `gift`, `heart`, `shield`, `sword`, `bolt`, `fire`, `flame`, `sparkle`, `sun` |
| **Nature / habitat** | `leaf`, `tree`, `mountain`, `droplet` |
| **Voice / audio** | `mic`, `sound`, `speak`, `nametag` |
| **Gaming hardware** | `arcade`, `cartridge`, `console`, `handheld`, `gamepad`, `joystick`, `dpad`, `keyboard`, `mouse`, `touch` (1.6.0), `monitor`, `headset`, `vr-headset`, `disc` |
| **System & connectivity** | `power`, `battery`, `wifi`, `bug`, `save` |
| **Combat & gear** | `axe`, `bow`, `hammer`, `pickaxe`, `swords`, `helmet`, `bomb`, `crosshair`, `skull` |
| **Fantasy & adventure** | `alien`, `castle`, `crown`, `gem`, `ghost`, `mushroom`, `portal`, `potion`, `robot`, `wand`, `treasure`, `key`, `map`, `compass` |
| **Progression & rewards** | `trophy`, `medal`, `leaderboard`, `star`, `coin`, `cards`, `dice`, `puzzle` |
| **Media & transport** | `play`, `pause`, `music`, `book`, `flag`, `flag-checkered`, `rocket`, `snowflake` |

### Platform and input glyphs

A scoreboard row or a team reveal shows where a player plays and with what. The set covers both: platform `monitor` (desktop), `handheld`, `console`; input `keyboard`, `mouse`, `gamepad`, `touch`.

### Rank frames are not icons

`Resources/Textures/Ranks/rank_frame_<tier>.svg` (bronze, silver, gold, platinum, diamond, champion, legend, unranked) are the frames of `.ds-rank-crest` and `.ds-rank-chip` (see [COMPONENTS.md](COMPONENTS.md), Competitive). They follow the same white-fill rule and import settings, but use a 64 × 64 viewBox, keep a translucent inner plate (`fill-opacity`) for the crest's emblem to sit on, and have no `.ds-icon--*` class: the crest and chip tier modifiers bind them.

## Adding a new icon

1. **Create / source the SVG.** Use a 24 × 24 viewBox by convention — it matches every shipped icon.
2. **Make it white-fill.** Every visible path must use `fill="white"` and `stroke="white"` (where applicable). See "The white-fill rule" below.
3. **Drop into `Assets/DesignSystem/Resources/Textures/Icons/`.**
4. **Set the importer to "Texture".** After Unity reimports the SVG, select it in the Project window. In the Inspector, find **SVG Type** and set it to **Texture** (not Sprite, not VectorImage). The `.svg.meta` file should record `svgType: 3`.
5. **Add a class to `Icons.uss`.** Append one line under the relevant section:
    ```css
    .ds-icon--myglyph     { background-image: resource("Textures/Icons/myglyph"); }
    ```
6. **Render it in the showcase** (`DesignSystemShowcase.uxml`) under the ICONS section, so the new glyph is visible in the live style guide.
7. **Open a PR** with a screenshot of the showcase row.

## The white-fill rule

This is the single non-obvious thing about the icon system, and the source of every "the icon is black even though my CSS says it's blue" bug.

### Why white?

`-unity-background-image-tint-color` in UI Toolkit is **multiplicative**:

```
result_pixel = source_pixel × tint_color
```

If your source SVG renders to black pixels (RGB 0, 0, 0):

```
black × any_tint = black
```

The tint has zero effect. The icon renders as the original SVG fill colour regardless of what the design system tries to do.

For the tint to multiply onto an arbitrary target colour, the source pixels must be **white** (RGB 1, 1, 1):

```
white × tint_color = tint_color
```

So every shipped SVG uses `fill="white"` and `stroke="white"`.

### Migrating black-fill icons

If you have a library of black-fill SVGs, bulk-rewrite them:

```python
import os, glob
for path in glob.glob('Assets/DesignSystem/Resources/Textures/Icons/*.svg'):
    with open(path, 'r', encoding='utf-8') as f:
        content = f.read()
    new = (content
           .replace('fill="black"', 'fill="white"')
           .replace('stroke="black"', 'stroke="white"'))
    if new != content:
        with open(path, 'w', encoding='utf-8', newline='') as f:
            f.write(new)
```

Re-import in Unity and the entire design system's tint cascade starts working. (This is exactly what we did when packaging this repo — the source set was 100 % black-filled and rendered as solid black icons across every component until the bulk swap.)

### Edge cases

- **Clip paths and masks.** SVGs with `<clipPath>` or `<mask>` may have an inner `<rect fill="white">` that defines the visible area. **Don't** convert those — the inner white IS the mask and must stay white. The path replacements above only touch `fill="black"`/`stroke="black"`.
- **Multi-colour artwork.** If your icon is intentionally polychrome (e.g. a country flag), don't put it in `.ds-icon`. The system assumes monochrome glyphs that take their colour from a single tint. Use a regular `<VisualElement>` with `background-image` and skip the design system's tint cascade.

## SVG import settings

After Unity imports an SVG, the Inspector's importer should look like:

```
SVG Type: Texture (3)
Texture Size: 256 (square)
Filter Mode: Bilinear
Sample Count: 4 (anti-aliasing)
Keep Texture Aspect Ratio: ✓
```

These are the defaults applied by Unity 6's **built-in** SVG `ScriptedImporter` (`fileID: 12408` inside the engine itself). The standalone `com.unity.vectorgraphics` *package* is **not** required — the built-in `com.unity.modules.vectorgraphics: 1.0.0` module ships with every Unity 6 install and includes the importer.

If your icon renders blocky or corner-clipped, double-check:

- `keepTextureAspectRatio: 1`
- `textureSize: 256`
- `sampleCount: 4`

The repo ships pre-built `.svg.meta` files for every icon with the right settings, so they import correctly on first project open. If you import a fresh SVG, Unity uses its own defaults — verify the importer settings in the Inspector match the table above.

## Why not VectorImage / Sprite?

Both are Unity-side options for `com.unity.vectorgraphics`:

| Type | Works with `background-image` | Tints via `-unity-background-image-tint-color` | Notes |
| --- | --- | --- | --- |
| **Texture** (svgType: 3) | ✓ | ✓ | What we use. Rasterised at import time. |
| Sprite (svgType: 0) | Partial | ✗ | UI Toolkit accepts Sprites for `background-image`, but the tint property doesn't apply uniformly. |
| VectorImage (svgType: 4) | ✓ | ✗ | The vector data preserves original colours; tint is ignored. |

We use **Texture** so a single SVG asset can render in every component colour the design system needs — primary, danger, gold, on-accent — by tinting only.

## Renaming

The class name uses hyphens (`.ds-icon--more-horizontal`); the file name uses underscores (`more_horizontal.svg`). The mapping is explicit in `Icons.uss`:

```css
.ds-icon--more-horizontal { background-image: resource("Textures/Icons/more_horizontal"); }
```

Stick to this convention for new icons — hyphens in the class, underscores in the file. (The `resource(...)` call uses the file name minus extension, so it follows the file convention.)
