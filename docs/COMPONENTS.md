# Component reference

One-line summary per component class. The showcase UXML (`Assets/Showcase/Resources/DesignSystemShowcase.uxml`) is the second source of truth — every class here appears there with its expected DOM structure and at least one rendered state.

## Screen root

| Class | Use |
| --- | --- |
| `.ds-root` | Topmost element of any screen. Cascades the tokens and paints `--color-bg` edge to edge. |
| `.ds-root--hud` | Compose with `.ds-root` for UI that floats **over gameplay** — transparent background, content-sized. |

Every screen needs `ds-root`: it is the scope the rest of the system hangs off — the text ramp, plus the scrollbar, focus-ring and transition families, which all select through it. (The tokens themselves are declared in a `:root` block in `DesignTokens.uss`; they cascade from the topmost element, which is where `ds-root` goes.) But `ds-root` also paints an opaque background, which is right for a full-screen menu and wrong for a health bar. A HUD, a minimap, a crosshair, a damage vignette — anything the game world must show through — carries `class="ds-root ds-root--hud"`.

## Motion

Every transition in the system takes its duration from a token, and new work should take its easing from one too.

| Token | Value | Use |
| --- | --- | --- |
| `--transition-fast` | 120ms | Hover, press, focus: feedback on the thing under the pointer. |
| `--transition-medium` | 200ms | State changes: a toggle, a tab, a drawer, a toast arriving. |
| `--transition-slow` | 320ms | A panel or sheet moving in, a progress fill catching up. |
| `--transition-emphasis` | 450ms | One-off attention: a match found, a rank step. |
| `--ease-standard` | `ease-out-cubic` | Arriving or settling. Most UI motion. |
| `--ease-emphasized` | `ease-out-back` | A small overshoot that draws the eye. Rarely. |
| `--ease-exit` | `ease-in-cubic` | Leaving: accelerate away. |
| `--ease-linear` | `linear` | Progress that must track time exactly. |

```css
.my-panel {
    transition-property: translate, opacity;
    transition-duration: var(--transition-slow);
    transition-timing-function: var(--ease-standard);
}
```

The four 1.6.0 tokens (`--transition-slow`, `--transition-emphasis` and the easings) are **not themeable yet**: `ThemeData` has no field for them, so a theme leaves them at these values. The same goes for every other token added in 1.6.0 (tier and team colours, `--space-half`, `--border-thick`, `--font-size-display`).

### Reduced motion

| Class | Use |
| --- | --- |
| `.ds-reduced-motion` | On any ancestor (normally the screen root): every ds-* transition below it runs in zero time, decorative loops hide, and `DsToast` skips its enter and exit animation. |

Drive it from a Settings toggle or the platform's reduce-motion preference: `root.EnableInClassList("ds-reduced-motion", reduce)`. Functional motion stays on purpose: a spinner still turns (it is how "working" is said) and a countdown ring still empties (it is the information). Material effects (`DsFx`) animate from one global clock; pin it with `DsFxManager.OverrideTime` if a reduced-motion screen should hold them still.

`Motion.uss` does it in two layers. It zeroes the four duration tokens on the element, which reaches every rule that times itself with `var(--transition-*)`, a host's included. And it lists every animated family with a literal `0s`, because a theme sheet that re-declares the tokens on the same element can win the token layer on load order. The families it lists: buttons, icons, animal cards, inputs (text, search, dropdown), tabs and the view toggle, side nav, rail and bottom nav, toggles, checks, radios, meters, scrollbar thumbs, auto-hiding scrollbars, drawers (panel, close button, backdrop), drop zones, draggables, pagination, steppers, step pips, the queue pill, the reward track (fill and badges) and stacked toasts. The skeleton shimmer is hidden. **A family that adds a transition adds a line there and here.**

## Buttons

| Class | Use |
| --- | --- |
| `.ds-btn` | Base class; carries 1 px transparent border so widths match across variants. |
| `.ds-btn--primary` | Green CTA, on-accent (dark) text. |
| `.ds-btn--secondary` | Blue secondary action. |
| `.ds-btn--tertiary` | Purple tertiary action. |
| `.ds-btn--ghost` | Transparent fill with a 1 px text-secondary border. |
| `.ds-btn--danger` | Red destructive action. |
| `.ds-btn--icon` | 36 × 36 square button for icon-only actions; child `.ds-icon` is auto-centred. |
| `.ds-btn--icon-danger` | Red-tinted icon button (e.g. trash). |
| `.ds-btn--sm` | 28 px height. |
| `.ds-btn--lg` | 44 px height. |
| `.ds-btn--block` | Full-width. |
| `.ds-btn--pressed` | Force the `:active` look (locked state). |

DOM:

```xml
<ui:Button text="Save" class="ds-btn ds-btn--primary" />
<ui:Button class="ds-btn ds-btn--icon"><ui:VisualElement class="ds-icon ds-icon--search" /></ui:Button>
```

## Inputs

| Class | Use |
| --- | --- |
| `.ds-input` | Single-line `<TextField>`; matches every Unity 6 inner-class variant. |
| `.ds-input.is-active` | Force the focus look (locked state, e.g. for a placeholder demo). |
| `.ds-input--error` | Red border for validation errors. |
| `.ds-search` | Wrapper that adds a leading-icon slot before a transparent inner field. |
| `.ds-search__icon` | The leading glyph (18 × 18). |
| `.ds-search__field` | The inner `<TextField>`; its box is stripped so it disappears into the wrapper. |
| `.ds-search__clear` | Trailing clear-button slot (16 × 16). |
| `.ds-dropdown` | `<DropdownField>` with chevron and value text styled. |
| `.ds-textarea` | Multiline `<TextField>` with min-height 96 px. |
| `.ds-textarea__wrap` | Wrapper if you need a counter row below. |
| `.ds-textarea__counter` | The "0/120" caption. |

Set placeholders via `field.textEdition.placeholder = "..."` in C#. Unity 6's API supports `hidePlaceholderOnFocus = true` to clear on focus.

### Dropdowns: pre-select with `index`, never `value`

```xml
<ui:DropdownField choices="Low,Medium,High,Ultra" index="3" class="ds-dropdown"/>
```

`DropdownField.value` is **not a UXML attribute**. Unity ignores it without a word and the field renders **blank** — the single most common way a Settings screen ships with empty dropdowns. `index` is zero-based and must be a valid position in `choices`.

### Dropdowns: the popup needs a host-side attach

The open menu (`unity-base-dropdown__*`) is parented by Unity as a **sibling of `.ds-root`**, not a descendant. Stylesheets a UXML `<Style>` tag imports scope to that UXML's subtree, so **no design-system rule can reach the popup** and no markup a screen author writes will change that. Popup chrome therefore ships as its own sheet, which the **host** attaches at panel scope, once per `UIDocument`, after the first layout pass — together with the token block its colours reference:

```csharp
var panelScope = doc.rootVisualElement.parent
              ?? doc.rootVisualElement.panel?.visualTree;
foreach (var path in new[] { "UI/Styles/DesignSystem/DesignTokens",     // so var(--color-*) resolves...
                             "UI/Styles/DesignSystem/DropdownPopup" })  // ...for the chrome that uses it
{
    var sheet = Resources.Load<StyleSheet>(path);
    if (panelScope != null && sheet != null && !panelScope.styleSheets.Contains(sheet))
        panelScope.styleSheets.Add(sheet);
}
```

Every panel needs its own attach — a world-space UI is one panel **per quad**, not one for the scene. Skip it and dropdowns open Unity-grey with chunky default scrollbars. This is a host responsibility, not a screen bug.

**Other dropdowns on the same panel are left alone.** `DropdownPopup.uss` hides a popup for the frames between Unity creating it and the runtime placing it, keyed off `ds-dropdown-tuner` on the panel root. Since 1.6.0 the runtime adds that class only from the gesture that opens a `.ds-dropdown` and removes it as soon as that popup is placed (or after 30 frames if none appears), so a plain `DropdownField` or `EnumField` sharing the panel opens normally. Up to 1.5.4 the class stayed on the panel for good, and those popups opened invisible.

**Theming the popup.** The chrome uses `var(--color-*)`, and custom properties *inherit*, so defining the tokens at panel scope is what colours the popup — the panel root is its parent. To make it follow a theme, add that theme's stylesheet at panel scope too, exactly as you add it to the document root:

```csharp
ThemeRuntime.Apply(doc.rootVisualElement, tokyo);   // the page
ThemeRuntime.Apply(panelScope, tokyo);              // ...and the popup, which is not inside the page
```

A `ThemeData` compiles to a pure `{scope} { --token: value }` block, which is why hoisting one is safe. Never hoist the full `DesignSystem.uss`: its component rules start matching Unity's internal popup elements and quietly break the popup's own layout.

**Theming the popup when there is no stylesheet.** The above is the whole story for any palette that exists as an asset. It does not work for a palette invented at *runtime* — Unity cannot set a custom property from C#, and a player build cannot compile a `StyleSheet` from a string, so there is nothing to hoist. Such a palette has to be stamped inline, and the popup is the one element a tree-walking stamper cannot reach: Unity builds it fresh under the panel root on every open and destroys it on close, so at the moment your stamp runs it does not exist. Subscribe instead:

```csharp
DesignSystemEvents.DropdownPopupOpened += (field, menu) =>
{
    // Fires once per popup instance, after the design system has placed and sized it.
    // Nothing to unregister afterwards: the popup is transient.
};
```

Two things to know if you take this path. An inline background **outranks the stylesheet**, so the moment you stamp a row's resting colour you own its `:hover` and `:checked` too and must drive them from `PointerEnter` / `PointerLeave` yourself. And the checked row is `items[field.index]`: a `DropdownField`'s menu holds one row per choice, in `choices` order, with no separators. The showcase's Randomize button is the worked example (`CodigrateThemeApplier.StampPopup`).

## Tabs & filters

| Class | Use |
| --- | --- |
| `.ds-tabs` | Container; flex-row segmented strip with surface-elev fill. |
| `.ds-tab` | Single tab (`<Button>`); pair with `.is-active` for the selected state. |
| `.ds-tabpanels` | Container for the tab bodies; put it as a sibling right after `.ds-tabs`. |
| `.ds-tabpanel` | One tab's content; hidden unless `.is-active`. |
| `.ds-view-toggle` | Container for grid/list view switching. |
| `.ds-view-toggle__btn` | Square icon button inside the toggle. |

A tab strip on its own is a filter row — it styles state and you drive the filtering. To switch **content**, add a `.ds-tabpanels` sibling: the Nth `.ds-tab` shows the Nth `.ds-tabpanel`, and the runtime (`DesignSystemBehaviour.EnsureTabs`) wires the clicks with no C# and no ids.

```xml
<ui:VisualElement class="ds-tabs">
    <ui:Button text="Graphics" class="ds-tab is-active"/>
    <ui:Button text="Audio"    class="ds-tab"/>
</ui:VisualElement>
<ui:VisualElement class="ds-tabpanels">
    <ui:VisualElement class="ds-tabpanel is-active">...graphics rows...</ui:VisualElement>
    <ui:VisualElement class="ds-tabpanel">...audio rows...</ui:VisualElement>
</ui:VisualElement>
```

`is-active` is the only state, so a C# controller can drive the same markup by flipping the class.

## Toggles, checks, radios

| Class | Use |
| --- | --- |
| `.ds-toggle` | iOS-style switch; runtime injects the sliding knob. The 44 × 24 track never shrinks (`flex-shrink: 0`), so in a narrow row the label wraps instead of the track collapsing into a circle. |
| `.ds-toggle__knob` | The white pill — usually injected, can be hand-authored too. |
| `.ds-check` | Square checkbox; the tick icon is constrained via `background-size: 12px 12px;` so it doesn't overflow the 2 px border. |
| `.ds-radio` | Standard radio button styled with surface-elev fill + primary inner dot. |

## Sliders & range

| Class | Use |
| --- | --- |
| `.ds-slider` | Single-value `<Slider>`; thumb cross-centred via `margin-top: -9px`. |
| `.ds-slider--filled` | Variant that highlights the filled portion. |
| `.ds-range` | `<MinMaxSlider>`; tracker, dragger, and both thumbs cross-centred via `top: 50%; margin-top: -<half>px;`. |
| `.ds-progress` | `<ProgressBar>`; 8 px tall by default. Title hidden — app chrome, not a game bar. |
| `.ds-progress-row` | Wrapper that adds a head row with title + percentage. |

## Meters (health, mana, XP, stamina)

A game bar is not `.ds-progress`. It is thick enough to read a number **on**, and the number is centred over the fill.

| Class | Use |
| --- | --- |
| `.ds-meter` | The bar. 20 px — the HUD default. |
| `.ds-meter__fill` | The filled portion. Drive it with `style="width: 62%;"`. |
| `.ds-meter__label` | The number, centred over the fill. |
| `.ds-meter--sm` | 10 px. A status pip — too short for a label. |
| `.ds-meter--lg` | 24 px. A boss bar or a cast bar. |
| `.ds-meter--secondary` `--tertiary` `--warning` `--danger` | Fill colour by role. |
| `.ds-meter__fill.is-warning` `.is-danger` | Live states — flip from game code as a value crosses a threshold. |

```xml
<ui:VisualElement class="ds-meter ds-meter--danger">
    <ui:VisualElement name="hp-fill" class="ds-meter__fill" style="width: 62%;"/>
    <ui:Label text="184 / 240" class="ds-meter__label"/>
</ui:VisualElement>
```

The label is a **sibling of the fill, placed after it**. UI Toolkit has no `z-index` — later in the DOM is the only way to paint over the fill — and a Label *inside* the fill is clipped to the fill's width and slides around as the value changes.

**Do not hand-roll this.** The obvious hand-roll is broken and reads as correct:

```css
.xp-track { height: 12px; overflow: hidden; }   /* clips at 12px      */
.xp-text  { position: absolute; top: -2px; }    /* text is ~14px tall */
```

The label is taller than the track that clips it, so you ship a bar with the top two pixels of some text peeking out of it.

**A HUD bar is 20 px.** It is not 40. At 40 px with a `ds-h3` number on it, two of them own a quarter of the player's screen.

## Cards

| Class | Use |
| --- | --- |
| `.ds-animal-card` | Example product card; demonstrates layered children + `.is-selected` / `.is-epic` modifiers + check pin. |
| `.ds-animal-card__image` | Top image area. |
| `.ds-animal-card__rarity` | Top-right rarity badge slot. |
| `.ds-animal-card__title-row` | Title + accessory icon row. |
| `.ds-animal-card__check` | The selected-state check pin (rendered last so it z-orders on top). |
| `.ds-info-row` | Two-column attribute row (icon+label on left, value on right). |
| `.ds-info-row__left` | The flex-row wrapper for the icon + label. |
| `.ds-info-row__icon` | 16 × 16 leading icon. |
| `.ds-info-row__label` | Attribute name (text-secondary). |
| `.ds-info-row__value` | Value text (text-primary). |
| `.ds-swatch-row` | Token swatch row used in the showcase. |

## Navigation

| Class | Use |
| --- | --- |
| `.ds-side-nav` | Full-width vertical nav with icon + label rows. **This is a vertical tab strip** — see below. |
| `.ds-nav-item` | A row inside `.ds-side-nav`; pair with `.is-active`. |
| `.ds-nav-item__icon` | 18 × 18 icon. |
| `.ds-nav-item__label` | Label, flex-grows to fill remaining space. |
| `.ds-side-rail` | Icon-only vertical rail; 56 px wide. |
| `.ds-rail-item` | A 40 × 40 cell inside the rail. |
| `.ds-bottom-nav` | Mobile bottom tab bar; 64 px tall. |
| `.ds-bottom-nav__item` | A column-flex tab with icon above label. |
| `.ds-profile` | Avatar + name + chevron chip used at the bottom of side-nav. |
| `.ds-profile__avatar` | 32 × 32 circular avatar slot. |
| `.ds-profile__name` | Display name (bold). |
| `.ds-profile__chevron` | Drop-down indicator. |

### A side nav is a tab strip

It switches panels through the **same mechanism as `.ds-tabs`**: give it a `.ds-tabpanels` sibling and the Nth `.ds-nav-item` shows the Nth `.ds-tabpanel`. Positional — nothing to name, nothing to wire.

```xml
<ui:VisualElement style="flex-direction: row;">
    <ui:VisualElement class="ds-side-nav">
        <ui:VisualElement class="ds-nav-item is-active">
            <ui:Label text="Graphics" class="ds-nav-item__label"/></ui:VisualElement>
        <ui:VisualElement class="ds-nav-item">
            <ui:Label text="Audio" class="ds-nav-item__label"/></ui:VisualElement>
    </ui:VisualElement>

    <ui:VisualElement class="ds-tabpanels">
        <ui:VisualElement class="ds-tabpanel is-active"> ...graphics... </ui:VisualElement>
        <ui:VisualElement class="ds-tabpanel">           ...audio...    </ui:VisualElement>
    </ui:VisualElement>
</ui:VisualElement>
```

Without the `.ds-tabpanels` sibling a side nav is **inert**: it highlights on hover and never switches anything, so a Settings screen ships with Audio and Controls permanently unreachable.

**Every panel is a real panel.** A `.ds-tabpanel` holding a single `<Label text="Audio…"/>` is not a tab — it is a tab that looks broken, because clicking it replaces a screenful of content with three words.

## Badges & labels

| Class | Use |
| --- | --- |
| `.ds-badge` | Small uppercase pill (22 px tall); pair with rarity variants. |
| `.ds-badge--common` | Green / primary-soft. |
| `.ds-badge--rare` | Blue / secondary-soft. |
| `.ds-badge--epic` | Purple / tertiary-soft. |
| `.ds-badge--legendary` | Amber / warning-soft. |
| `.ds-tag` | Habitat-style filled pill (24 px tall). |
| `.ds-tag--amphibious` / `--aquatic` / `--nocturnal` | Variant fills. |
| `.ds-chip` | Status pill with leading-icon slot + label (24 px tall). |
| `.ds-chip__icon` | 14 × 14 leading icon. |
| `.ds-chip--equipped` / `--new` / `--owned` / `--limited` / `--event` / `--sale` | Per-state colour. |
| `.ds-avatar` | Circular avatar; pair with `--sm` (24), `--md` (40), `--lg` (56), `--xl` (72). |
| `.ds-notif-wrap` | Wrapper for an icon + corner notification dot. |
| `.ds-notif-icon` | The base icon (e.g. bell). |
| `.ds-notif-dot` | The red corner dot; 18 × 18 single-digit, `--multi` for 2+ digit pill. |
| `.ds-notif-dot__count` | Count text inside the dot. |

## Overlays

| Class | Use |
| --- | --- |
| `.ds-modal` | Generic modal panel. |
| `.ds-modal__header` | Top row with title + close button. |
| `.ds-modal__title` | Modal title text. |
| `.ds-modal__close` | 24 × 24 icon-button in the header. |
| `.ds-modal__body` | Main content area. |
| `.ds-modal__actions` | Bottom row of action buttons (flex-row, equal-grow). |
| `.ds-modal__list-row` | Row inside a modal for list-style content (sort options, filter checkboxes). |
| `.ds-modal__list-label` | Label inside a list row. |
| `.ds-modal__illustration` | Centered illustration block (e.g. for limit reached). |
| `.ds-dialog` | Centered confirm dialog (slimmer than full modal). |
| `.ds-dialog__title` / `__message` / `__actions` | Dialog slots. |
| `.ds-toast` | Non-blocking notification toast. |
| `.ds-toast__icon` | 18 × 18 leading icon (auto-tinted by toast variant). |
| `.ds-toast__message` | Body text (white-space: normal). |
| `.ds-toast__close` | 18 × 18 trailing close. |
| `.ds-toast--success` / `--info` / `--warning` / `--danger` | Per-variant fill + icon tint. |
| `.ds-sheet` | Mobile bottom drawer. |
| `.ds-sheet__handle` | Top drag-handle pill. |
| `.ds-sheet__row` | A single drawer row. |
| `.ds-sheet__row-icon` / `__row-label` | Row slots. |
| `.ds-sheet__row--danger` | Red text + icon for destructive rows (e.g. Release). |
| `.ds-empty` | Empty-state block (icon-bg + title + message + CTA). |
| `.ds-empty__icon-bg` | 64 × 64 surface-elev circle behind the icon. |
| `.ds-empty__title` / `__message` | Stack of centred labels. |
| `.ds-tooltip` | Floating info surface; the consumer positions it (mouse-follow / edge-flip). Elevated tier + strong border. |
| `.ds-tooltip__title` / `__subtitle` / `__body` | Title (body-1 bold), subtitle (caption), body (body-2, wraps). |
| `.ds-tooltip__divider` | Hairline separator between sections. |
| `.ds-tooltip__row` | Flex-row stat line; `.ds-tooltip__row-label` (left) + `.ds-tooltip__row-value` (right, bold). |

## Feedback

| Class | Use |
| --- | --- |
| `.ds-spinner` | Loader; pair with `.is-spinning` to start the rotation. The runtime drives the rotation in C#; do **not** add `transition-property: rotate` — it would compound across per-frame writes and make the spinner jiggle. |
| `.ds-spinner--lg` | 48 × 48 variant. |
| `.ds-skeleton` | Placeholder shape. |
| `.ds-skeleton--card` | Card-shaped variant. |
| `.ds-skeleton__shimmer` | Sliding overlay element (auto-injected). |
| `.ds-pagination` | Container. |
| `.ds-pagination__btn` | Page button (32 × 32 square); pair with `.is-active`. |
| `.ds-pagination__ellipsis` | "…" between page numbers. |
| `.ds-stepper` | Quantity selector container. |
| `.ds-stepper__btn` | − / + button. |
| `.ds-stepper__value` | Value display. |

## Scrollbars

The system styles UI Toolkit's internal scroll classes, **scoped to `.ds-root`** so the rules don't bleed into editor windows or other UI Toolkit panels. You don't write a class — any `<ui:ScrollView>` inside a `.ds-root`-rooted hierarchy gets the slim themed scrollbar automatically.

| Class | Use |
| --- | --- |
| `.unity-scroller__low-button`, `.unity-scroller__high-button` | Hidden via `display: none` — no arrow buttons. |
| `.unity-scroll-view__vertical-scroller` | 8 px wide. |
| `.unity-scroll-view__horizontal-scroller` | 8 px tall. |
| `.unity-base-slider__tracker` | Transparent — thumb floats on the page surface. |
| `.unity-base-slider__dragger` | The visible thumb. 4 px radius pill, `var(--color-border-strong)` background, brightens to `var(--color-text-secondary)` on hover. Auto-themes through tokens. |

To render a framed scroll-view demo (used in the showcase's SCROLLBARS section):

```xml
<ui:ScrollView mode="Vertical" class="ds-scrollbar-demo" style="height: 120px; flex-grow: 1;">
    <ui:Label text="..." class="ds-body-2" />
</ui:ScrollView>
```

The `.ds-scrollbar-demo` class adds a `var(--color-bg)` background, `var(--color-border)` outline, and 8 px padding — useful for surfacing the scrollbar visually in documentation contexts. Lives in `Feedback.uss`.

## Drag & drop

| Class | Use |
| --- | --- |
| `.ds-draggable` | Marks an element draggable; the runtime auto-wires pointer drag + a ghost. |
| `.ds-drop-zone` | A container that accepts a dropped `.ds-draggable` (reparents the item on drop). |
| `.ds-drop-zone.is-drag-over` | Highlight applied to a drop zone while a drag hovers it. |
| `.ds-drag-ghost` | The floating preview that follows the pointer during a drag. Reuse it from custom drag code (e.g. a game inventory) for a consistent look. |

The runtime (`DesignSystemBehaviour.EnsureDraggables`) auto-wires `.ds-draggable` for the simple "move between zones" case. Inventories with split / merge / transfer logic drive their own pointer handling and reuse only the `.ds-drag-ghost` / `.is-drag-over` visuals.

## Competitive (ranked play)

`Competitive.uss` (1.6.0): the pieces a ranked 1v1 / 2v2 / 3v3 ladder needs. **Colour is never the only signal**: each tier has its own frame shape, a chip prints the tier letter, a team row carries an accent bar. The tier colour reaches every part through one inherited custom property, `--ds-rank-tier`, which the tier modifier sets on the block.

### Tokens (not themeable yet)

| Token | Value | |
| --- | --- | --- |
| `--color-tier-bronze` | `#D08A4E` | warm copper |
| `--color-tier-silver` | `#B4C1D3` | cool silver |
| `--color-tier-gold` | `#F2C94C` | |
| `--color-tier-platinum` | `#2DD4BF` | teal |
| `--color-tier-diamond` | `#5B9BFF` | sapphire |
| `--color-tier-champion` | `#B57BFF` | royal purple |
| `--color-tier-legend` | `#FFF6E0` | near-white |
| `--color-tier-unranked` | `#677085` | the disabled grey |
| `--color-team-a` / `--color-team-b` | `#3B82F6` / `#F97316` | blue against orange: they stay apart under protanopia, deuteranopia and tritanopia |

Every tier colour clears 4.5:1 against `--color-surface-elev`, so tier-coloured chip text reads at 11-12 px. Unranked does not, and no component sets text in it.

### Rank crest

| Class | Use |
| --- | --- |
| `.ds-rank-crest` | Column: frame, then pips, then label. Without a tier modifier it wears the unranked look. |
| `.ds-rank-crest--bronze` `--silver` `--gold` `--platinum` `--diamond` `--champion` `--legend` `--unranked` | Tier: sets the frame shape and `--ds-rank-tier`. |
| `.ds-rank-crest--sm` `--md` `--lg` `--xl` `--hero` | 32, 48 (default), 64, 96, 160 px frame. Pips and label scale with it. |
| `.ds-rank-crest__frame` | The tier frame (a white SVG from `Resources/Textures/Ranks`, tinted). Centres the emblem. |
| `.ds-rank-crest__emblem` | Empty slot the game fills (55 % of the frame, tinted near-white): `style.backgroundImage` from C#, or a class. |
| `.ds-rank-crest__pips` | Holds a `.ds-pips` (steps I to III). |
| `.ds-rank-crest__label` | Optional caption ("Gold II"). |
| `.ds-rank-crest.is-trials` | Placement matches: the unranked frame and colour whatever the tier class; the game puts its "?" in the emblem. |
| `.ds-rank-crest.is-locked` | Not reached yet: tinted grey (which desaturates single-colour art) and dimmed, keeping the tier's shape. |

```xml
<ui:VisualElement class="ds-rank-crest ds-rank-crest--gold ds-rank-crest--lg">
    <ui:VisualElement class="ds-rank-crest__frame">
        <ui:VisualElement name="emblem" class="ds-rank-crest__emblem"/>
    </ui:VisualElement>
    <ui:VisualElement class="ds-rank-crest__pips ds-pips">
        <ui:VisualElement class="ds-pips__pip is-filled"/>
        <ui:VisualElement class="ds-pips__pip is-current"/>
        <ui:VisualElement class="ds-pips__pip"/>
    </ui:VisualElement>
    <ui:Label text="Gold II" class="ds-rank-crest__label"/>
</ui:VisualElement>
```

The frames are distinct without colour: bronze a lily-pad ring (notched), silver a rounded shield, gold a shield in laurels, platinum a shield with small wings, diamond a faceted gem, champion a crowned ring, legend a starburst, unranked a dashed ring. Each keeps its inner area centred and translucent, so the emblem reads on top.

### Rank chip and step pips

| Class | Use |
| --- | --- |
| `.ds-rank-chip` | Compact pill for rows: `__shape` (the tier frame at 16 px) and `__text` ("G2", "L"). |
| `.ds-rank-chip--<tier>` | Same eight tiers as the crest. |
| `.ds-rank-chip--sm` / `--md` | 18 px / 22 px (default) tall. |
| `.ds-pips` | A row of step markers; the count is the number of children. |
| `.ds-pips__pip` | One step. Plain = still to come. |
| `.ds-pips__pip.is-filled` / `.is-current` | Done / under way (a ring, readable without colour). |
| `.ds-pips--sm` `--md` `--lg` | 6, 8 (default), 12 px. Inside a crest the crest's size wins. |

### Progress ring and countdown

`DsProgressRing` (`DesignSystem.Runtime.Controls`, a `[UxmlElement]`) draws a track and an arc with `Painter2D`. It has no update loop: setting `value` repaints only when the number changes.

| Attribute / class | Use |
| --- | --- |
| `value` | 0..1, clamped. |
| `thickness` | Stroke width in px (default 4), shared by track and arc. |
| `start-angle` | Degrees clockwise from 3 o'clock; default -90 (12 o'clock). |
| `.ds-progress-ring` | Added by the control. Sets the default 48 px size and the two colours below; centres children. |
| `--ds-ring-color` / `--ds-ring-track-color` | Custom properties the control reads through `customStyle`. Default `--color-primary` / `--color-surface-elev`. |
| `.ds-progress-ring--warning` / `--danger` / `--tier-<tier>` | Arc colour by role or tier. |
| `.ds-countdown` | Ready check: `__ring` (a DsProgressRing, 80 px) holding `__value` (the number), then `__label`. |
| `.ds-countdown.is-urgent` / `.is-done` | Last seconds (danger) / finished (greyed). |
| `.ds-countdown--sm` | 48 px ring, for a rejoin banner. |

```xml
<ui:VisualElement class="ds-countdown">
    <DesignSystem.Runtime.Controls.DsProgressRing value="0.7" thickness="6" class="ds-countdown__ring">
        <ui:Label name="seconds" text="7" class="ds-countdown__value"/>
    </DesignSystem.Runtime.Controls.DsProgressRing>
    <ui:Label text="Accept match" class="ds-countdown__label"/>
</ui:VisualElement>
```

Set the ring from the remaining time (every frame is fine, it is one ring) and change the number only when the displayed second changes.

### Queue pill and sheet

| Class | Use |
| --- | --- |
| `.ds-queue-pill` | Floating "searching" pill, 40 px (56 on `.mobile`). Sizes to its content; the host positions it. |
| `.ds-queue-pill__spinner` | A `.ds-spinner` (with `is-spinning`) sized to the pill. |
| `.ds-queue-pill__label` / `__time` | "Searching" / elapsed time (the time has a min-width so ticking digits do not shuffle the pill). |
| `.ds-queue-pill__chips` | `.ds-chip` children, one per playlist searched. |
| `.ds-queue-pill__cancel` | A `ds-btn ds-btn--icon`, round; a 48 px target on `.mobile`. |
| `.ds-queue-pill.is-found` | A match is ready: primary border, slight pop (one transition, no loop). |
| `.ds-queue-pill.is-expanded` | The pill is showing its sheet; drops its chips. |
| `.ds-queue-sheet` | The expanded panel: `__header`, `__body`, `__footer` (buttons in the footer share its width). |

USS has no sibling selector, so `is-expanded` cannot open the sheet by itself: the host shows and hides the sheet and flips the class.

### Scoreboard

| Class | Use |
| --- | --- |
| `.ds-scoreboard` | The board. |
| `.ds-scoreboard__header` | Column titles; uses the same cell classes as the rows, so columns line up. |
| `.ds-scoreboard__row` | One player, 36 px (32 on `.mobile`). |
| `.ds-scoreboard__row--team-a` / `--team-b` | Left accent bar in the team colour. |
| `.ds-scoreboard__row.is-self` / `.is-disconnected` / `.is-ai` | Your row (lifted, bold) / inside the rejoin window (faded, italic) / a leaver's labelled AI (shows the tag). |
| `.ds-scoreboard__cell` | Every cell. Fixed width except the name. |
| `.ds-scoreboard__rank` | Holds a `.ds-rank-chip--sm`. |
| `.ds-scoreboard__name` | Takes the slack and ellipsizes. Holds `__name-text` and `__ai-tag`. |
| `.ds-scoreboard__ai-tag` | The "AI" tag; present in every row, shown only on `is-ai`. |
| `.ds-scoreboard__platform` | Holds a 16 px `.ds-icon` (`monitor`, `handheld`, `gamepad`, `keyboard`, `touch`). |
| `.ds-scoreboard__score` | Right-aligned, bold. |

```xml
<ui:VisualElement class="ds-scoreboard__row ds-scoreboard__row--team-a is-ai">
    <ui:VisualElement class="ds-scoreboard__cell ds-scoreboard__rank">
        <ui:VisualElement class="ds-rank-chip ds-rank-chip--sm ds-rank-chip--platinum">
            <ui:VisualElement class="ds-rank-chip__shape"/>
            <ui:Label text="P1" class="ds-rank-chip__text"/>
        </ui:VisualElement>
    </ui:VisualElement>
    <ui:VisualElement class="ds-scoreboard__cell ds-scoreboard__name">
        <ui:Label text="Mika" class="ds-scoreboard__name-text"/>
        <ui:Label text="AI" class="ds-scoreboard__ai-tag"/>
    </ui:VisualElement>
    <ui:VisualElement class="ds-scoreboard__cell ds-scoreboard__platform">
        <ui:VisualElement class="ds-icon ds-icon--handheld"/>
    </ui:VisualElement>
    <ui:Label text="1420" class="ds-scoreboard__cell ds-scoreboard__score"/>
</ui:VisualElement>
```

### Reward track

| Class | Use |
| --- | --- |
| `.ds-reward-track` | A row of fixed-width (72 px, 80 on `.mobile`) nodes that never shrinks, so inside a horizontal `ScrollView` it scrolls. |
| `.ds-reward-track__line` | The track behind the nodes, from the first node's centre to the last. First in the DOM, so it paints underneath. |
| `.ds-reward-track__fill` | Inside the line; the game sets `style.width` as a percentage. With N nodes, node k sits at k / (N - 1). Transitions over `--transition-slow`. |
| `.ds-reward-track__node` | One reward: badge, then label. |
| `.ds-reward-track__node-icon` | 40 px badge (48 on `.mobile`) drawing its glyph at 20 px: put a `.ds-icon--<name>` glyph class on it, or set `style.backgroundImage`. Leave `.ds-icon` off (it would resize and retint the badge). |
| `.ds-reward-track__node-label` | Caption under the badge; ellipsizes to the node. |
| `.is-locked` / `.is-unlocked` / `.is-claimed` / `.is-current` | On the node: not earned / earned, waiting for a tap / taken / the next to earn (combines with `is-locked`). |

### Toast stack and `DsToast`

| Class / API | Use |
| --- | --- |
| `.ds-toast-stack` | Absolute, full-width container that stacks `.ds-toast` children (360 px max each). Must not catch the pointer: `picking-mode="Ignore"`. |
| `.ds-toast-stack--top` / `--bottom` | Anchor. The newest toast sits nearest the anchored edge. |
| `.ds-toast.is-entering` / `.is-leaving` | Set by `DsToast`: in from a half-height offset, out the same way, faster. |
| `DsToast.Show(host, text, kind = "info", durationMs = 4000)` | Finds the stack under `host` (or creates a top one), adds a toast, removes it after `durationMs` with the panel scheduler. No MonoBehaviour. Returns the toast. |
| `DsToast.Dismiss(toast)` | Send one out early. `durationMs <= 0` keeps a toast until this or its close button. |
| `DsToast.MaxVisible` | Most toasts per stack (default 3); the oldest leaves when another arrives. |

Kinds are `info`, `success`, `warning` and `danger` (`error` is accepted), the same variants as the static `.ds-toast--*` classes. Under `.ds-reduced-motion` toasts appear and go without animating. The close button is not focusable on purpose: a notice that happens to be on screen must not take a place in a gamepad's focus order. Timers run on the panel scheduler, which a world-space `PanelRenderer` panel does not tick in a player, so use toasts on screen-space panels.

```csharp
using DesignSystem.Runtime.Controls;

DsToast.Show(screenRoot, "Promoted to Gold II", "success");     // 4 s, top of the screen
var ban = DsToast.Show(screenRoot, "Matchmaking cooldown: 5 minutes", "warning", durationMs: 0);
// ...later
DsToast.Dismiss(ban);
```

## Icons

`.ds-icon` is the base class. Pair with one of 121 `.ds-icon--<name>` classes from `Icons.uss` (the full list, by group, is in [ICONS.md](ICONS.md)). The original set:

```
arrow-up arrow-down arrow-left arrow-right
chevron-up chevron-down chevron-left chevron-right
sort-asc sort-desc

check close info error warning help smile frown

plus edit trash share refresh sync search filter
more-horizontal more-vertical menu list
lock unlock eye settings bell clock calendar user home target

paw shirt hats store cart bag gift heart shield sword
bolt fire flame sparkle sun

leaf tree mountain droplet

mic sound speak nametag
```

For platform and input glyphs (a scoreboard row, a team reveal): `monitor`, `handheld`, `console`, `gamepad`, `keyboard`, `mouse` and `touch` (1.6.0).

Sizes: `--xs` (12), `--sm` (16), default (20), `--lg` (24), `--xl` (32), `--xxl` (48).

Tint variants: `--primary` (text-primary), `--secondary` (text-secondary), `--disabled`, `--accent` (primary green), `--gold` (warning amber), `--danger`, `--warning`, `--info`, `--on-accent`, `--rarity-common`, `--rarity-rare`, `--rarity-epic`, `--rarity-legendary`.

## Typography

| Class | Style |
| --- | --- |
| `.ds-h1` | 26 px / bold |
| `.ds-h2` | 20 px / bold |
| `.ds-h3` | 16 px / semibold |
| `.ds-body-1` | 14 px / regular |
| `.ds-body-2` | 12 px / regular |
| `.ds-caption` | 11 px / medium / text-secondary |
| `.ds-text-success` | Helper colour utility (primary green). |
| `.ds-text-primary` | Helper colour utility (text-primary white). |
| `.ds-nowrap` | One line, refuses to shrink. **Use on any label sitting in a row next to a fixed-size element.** |
| `.ds-truncate` | One line, takes the space it's given, ellipsizes the rest. For user data of unknown length. |
| `.ds-intl` | Advanced text generator: Arabic joining forms, right-to-left reordering, Devanagari clusters. Compose on a root: `class="ds-root ds-intl"`. Costs nothing for Latin-only UI. |
| `.ds-weight-100` … `.ds-weight-900` | One class per weight the imported family ships. Requires a font family — see below. |

### Weights need a font family

`.ds-h3` is *semibold* and `.ds-caption` is *medium* in the table above, but out of the box both render as plain bold. That is not an oversight: USS has **no `font-weight` property**, UI Toolkit has exactly two style slots (`normal` and `bold`), and with no font family loaded there is no semibold face to reach for. Worse, `-unity-font-style: bold` with an unwired weight table does not even give you a real bold — TextCore *synthesizes* one by dilating the Regular outline.

Import a family (`Design System > Google Fonts`) and all of that resolves. The generated stylesheet declares `--ds-font-100` … `--ds-font-900`, points `.ds-root` at `var(--ds-font-body)`, adds the `.ds-weight-*` classes, wires each face's `fontWeightTable` so every existing `-unity-font-style: bold` rule lands on the family's **real** Bold, and re-points `.ds-h3` at the real 600 and `.ds-caption` at the real 500.

```xml
<ui:Label text="SemiBold" class="ds-body-1 ds-weight-600" />
```

Full detail in [FONTS.md](FONTS.md), including why a fallback chain is mandatory for multilingual text and why its order matters.

### Prose wraps. Labels do not.

Every class in the table above sets `white-space: normal`. That is right for a paragraph and wrong for the short fixed labels that make up most of a UI — a slot name, a stat value, a menu entry.

The reason it bites is **flexbox, not typography**. Inside a `flex-direction: row`, a Label is `flex-shrink: 1` like everything else, so when the row is tight Yoga shrinks the *label* rather than the fixed-size icon next to it — and a `white-space: normal` Label that has been shrunk below its text width does not overflow the way a browser would. It wraps. If the box is narrower than the word, it wraps **mid-word**.

That is how `Armor` renders as `Armo / r` under a 56 px slot with 130 px of free space to its right. Nothing overflowed; the label just volunteered to be 30 px wide.

```xml
<!-- WRONG: wraps mid-word the moment the row is tight -->
<ui:VisualElement style="flex-direction: row; align-items: center;">
    <ui:VisualElement class="equip-slot"/>
    <ui:Label text="Main Hand" class="ds-caption"/>
</ui:VisualElement>

<!-- RIGHT -->
<ui:VisualElement style="flex-direction: row; align-items: center;">
    <ui:VisualElement class="equip-slot"/>
    <ui:Label text="Main Hand" class="ds-caption ds-nowrap"/>
</ui:VisualElement>
```

A quest description, a tooltip body, a death-screen blurb: leave those wrapping.

## Runtime behaviour: auto-attach and its switches

`DesignSystemBehaviour` (one per backend: `Behaviour.UIDocument` and, on 6000.5+, `Behaviour.PanelRenderer`) attaches itself to every document that exists when a scene loads, and runs the behaviours the classes above rely on: toggle knobs, skeleton shimmer, drag and drop, dropdown placement, tab and side-nav switching, scrollbar auto-hide, and the spinner rotation.

| Switch | Effect |
| --- | --- |
| `DesignSystemBehaviour.AutoAttach = false` | Mutes the scene-load attach for that backend. Set it before the first scene finishes loading (a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]` method, or an `Awake` in the first scene). |
| Scripting define `DS_NO_AUTO_ATTACH` | Compiles the scene-load hook out of both backends. |
| `DesignSystemBehaviour.AttachToAll()` | Attaches to every document now. Works under either switch. |

Turn it off when the host adds the component itself, for example from the one helper that creates every `UIDocument` (which also covers documents created after the scene loaded, which the scene hook never sees):

```csharp
using DsBehaviour = DesignSystem.Runtime.Behaviour.UIDocument.DesignSystemBehaviour;

[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void TakeOverDesignSystemAttach() => DsBehaviour.AutoAttach = false;

static void SetupUIDocument(UIDocument doc)
{
    if (!doc.TryGetComponent<DsBehaviour>(out _)) doc.gameObject.AddComponent<DsBehaviour>();
}
```

What an attached behaviour costs: one walk of its document every 250 ms (the rescan that catches lazily cloned screens, stopped when the component is disabled), and a 16 ms spinner tick that exists only while something under the document carries `is-spinning`, touching only those elements. `SetSpinning(el, true)` starts a spinner on the next tick; an `is-spinning` class added any other way (in UXML, or `AddToClassList` from code) is picked up by the next rescan, within 250 ms.

## Showcase helpers

These classes exist for the showcase / live demo and aren't part of the drop-in design system per se — but they're useful patterns for anyone building a similar style guide on top of the system. All live in `Feedback.uss` so they ship in the same import chain.

| Class | Use |
| --- | --- |
| `.ds-section` | Bordered section card (background, border, radius, padding) used to group related component demos in the showcase. |
| `.ds-section__title` | Uppercase / bold / spaced label inside `.ds-section`. |
| `.ds-row` | Flex-row helper with `align-items: center` — used inside sections to lay out controls in a line. |
| `.ds-row__gap > *` | Adds `margin-right: var(--space-2)` to direct children — useful for spaced rows. |
| `.ds-col-gap > *` | Adds `margin-bottom: var(--space-2)` — same pattern, vertical. |
| `.ds-swatch-row` | Flex-row containing a swatch + name + hex label. |
| `.ds-swatch` | 14 × 14 colour chip used in the COLORS section. Border uses `var(--color-border)` so it themes correctly. |
| `.ds-swatch__name`, `.ds-swatch__hex` | Slots inside a swatch row. |
| `.ds-swatch--<token>` | One per colour token: `--primary`, `--primary-hover`, `--secondary`, `--tertiary`, `--warning`, `--danger`, `--text-primary`, `--text-secondary`, `--text-disabled`, `--bg`, `--surface`, `--surface-elev`, `--border`. Each binds the swatch's background to its `var(--color-*)`, so a theme override repaints the COLORS section automatically. |
| `.ds-btn--demo-hover` | Combine with a button variant (e.g. `class="ds-btn ds-btn--primary ds-btn--demo-hover"`) to lock the button to its `*-hover` token. Used in the showcase's BUTTONS section to display Default / Hover / Pressed / Disabled side-by-side without needing the cursor to actually be over the button. |
| `.ds-scrollbar-demo` | Framed `<ui:ScrollView>` wrapper — adds `var(--color-bg)` background, `var(--color-border)` outline, and 8 px padding. Used in the showcase's SCROLLBARS section. |
| `.showcase-chrome` | Marks an element (and all its descendants) as showcase page chrome — promo banner, future headers / footers. The selector-chain hover overlay (`ShowcaseDocOverlay.cs`) skips inspection inside `.showcase-chrome`. The `.ds-h1` colour is locked to a fixed light value under `.showcase-chrome` regardless of theme so titles stay readable on the always-black banner. |
