using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UIDocumentDesignSystem.Showcase
{
    // Applies a flat color palette to the showcase tree via INLINE styles, then
    // can revert back to the USS cascade.
    //
    // Why inline + walk, and not override `--color-*` custom properties:
    //   Unity 6's public UI Toolkit API does not expose a way to set CSS custom
    //   properties at runtime. `VisualElement.style` is typed and covers only
    //   the built-in properties; the only documented path to feed values into
    //   `var(--…)` references is to ship a `.uss` asset, which can't be built
    //   on the fly from a remote palette. We therefore walk the showcase tree,
    //   match elements by class, and stamp inline values on every class the
    //   design system uses `var(--color-…)` on.
    //
    // The class-to-token map below is the single source of truth — anything
    // missing here will not respect the codigrate / random palette swap. The
    // map mirrors every `var(--color-…)` reference across the DS USS files:
    //     Cards.uss, TabsAndFilters.uss, Buttons.uss, Inputs.uss, Overlays.uss,
    //     Controls.uss, Feedback.uss (where .ds-section is defined), plus the
    //     showcase-only `.showcase-*` chrome.
    //
    // Surprises baked in:
    //   - .ds-input / .ds-textarea / .ds-dropdown DO NOT host their own bg;
    //     the visible fill lives on Unity-emitted children (.unity-text-input,
    //     .unity-base-text-field__input, .unity-popup-field__input, …). The
    //     applier descends into those.
    //   - Pseudo-class states (`:hover`, `:disabled`, `:checked`) can't be
    //     overridden by inline styles directly. We approximate:
    //       hover    → PointerEnter / PointerLeave callbacks per element.
    //       disabled → check `enabledSelf` and apply USS-equivalent disabled
    //                  values (surface-elev + text-disabled) inline.
    //   - The `is-active` modifier on .ds-tab / .ds-view-toggle__btn is a
    //     regular class so a Query call finds it; bg / text get the primary
    //     overlay.
    //   - The dropdown popup is added as a SIBLING of rootVisualElement and
    //     opens/closes dynamically; recolouring it would require hooking the
    //     pointer-down + scheduling. The closed-state appearance is what most
    //     of the showcase shows, so we focus there.
    public static class CodigrateThemeApplier
    {
        // Track each property we've stamped per element so Revert can clear
        // exactly the keys we touched and nothing else (avoiding clobbering
        // inline styles that the UXML author baked in deliberately).
        static readonly Dictionary<VisualElement, HashSet<TouchedProperty>> _touched
            = new Dictionary<VisualElement, HashSet<TouchedProperty>>();

        // Pointer-enter / pointer-leave hover handlers we installed during
        // Apply, kept here so Revert can detach them. Keys are the buttons
        // (and tabs) we wired up.
        static readonly Dictionary<VisualElement, EventCallback<PointerEnterEvent>> _enterHandlers
            = new Dictionary<VisualElement, EventCallback<PointerEnterEvent>>();
        static readonly Dictionary<VisualElement, EventCallback<PointerLeaveEvent>> _leaveHandlers
            = new Dictionary<VisualElement, EventCallback<PointerLeaveEvent>>();

        enum TouchedProperty
        {
            Background,
            BorderColor,
            BorderBottomColor,
            Color,
            UnityBgTint,
        }

        // ─── PUBLIC API ─────────────────────────────────────────────────────

        public static void Apply(VisualElement root, ColorMap map)
        {
            if (root == null || map == null) return;
            Revert(root);

            // Root: bg + cascading text. `color` is inherited so every Label
            // descendant picks up the new primary text colour for free —
            // except where a more-specific rule (or our own pass below) sets
            // a different value.
            Stamp(root, TouchedProperty.Background, map.Bg);
            Stamp(root, TouchedProperty.Color,      map.TextPrimary);

            // Default `.ds-icon` tint baseline (Icons.uss line 31:
            //   `-unity-background-image-tint-color: var(--color-text-secondary)`).
            // We stamp this FIRST so every later specific handler — nav
            // item active state, toast variant icon tints, chip icon tints,
            // `.ds-icon--*` modifier variants — overwrites it with their own
            // tint. Plain icons (e.g., chevrons in pagination, search bar
            // glyphs) that no specific handler touches end up with the
            // codigrate text-secondary tint, matching the DS resting colour.
            ApplyClass(root, "ds-icon", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextSecondary));

            // ── Surfaces / cards / sections ────────────────────────────────
            ApplyClass(root, "ds-section",    el => { Stamp(el, TouchedProperty.Background, map.Surface);     Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-card",       el => { Stamp(el, TouchedProperty.Background, map.Surface);     Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-card--elevated", el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            ApplyClass(root, "ds-animal-card", el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-animal-card__check",      el => Stamp(el, TouchedProperty.Background, map.Primary));
            ApplyClass(root, "ds-animal-card__check-icon", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextOnAccent));
            ApplyClass(root, "ds-animal-card__image",      el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            ApplyClass(root, "ds-animal-card__title",      el => Stamp(el, TouchedProperty.Color, map.TextPrimary));
            ApplyClass(root, "ds-animal-card__star",       el => Stamp(el, TouchedProperty.UnityBgTint, map.Warning));
            ApplyClass(root, "ds-animal-detail",           el => { Stamp(el, TouchedProperty.Background, map.Surface); Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-animal-detail__hero",     el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            ApplyClass(root, "ds-animal-detail__description", el => Stamp(el, TouchedProperty.Color, map.TextSecondary));
            ApplyClass(root, "ds-info-row__icon",  el => Stamp(el, TouchedProperty.UnityBgTint, map.TextSecondary));
            ApplyClass(root, "ds-info-row__label", el => Stamp(el, TouchedProperty.Color, map.TextSecondary));
            ApplyClass(root, "ds-info-row__value", el => Stamp(el, TouchedProperty.Color, map.TextPrimary));

            // ── Overlays: modal / dialog / toast / sheet / empty ────────────
            ApplyClass(root, "ds-modal",            el => { Stamp(el, TouchedProperty.Background, map.Surface);     Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-dialog",           el => { Stamp(el, TouchedProperty.Background, map.Surface);     Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-modal__illustration", el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            ApplyClass(root, "ds-toast",            el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            // Toast variants — coloured top-strip border + tinted leading icon
            // (Overlays.uss `.ds-toast--<variant> .ds-toast__icon` selector).
            ApplyClass(root, "ds-toast--success", el =>
            {
                Stamp(el, TouchedProperty.BorderColor, map.Primary);
                foreach (var icon in QueryByClass(el, "ds-toast__icon")) Stamp(icon, TouchedProperty.UnityBgTint, map.Primary);
            });
            ApplyClass(root, "ds-toast--info", el =>
            {
                Stamp(el, TouchedProperty.BorderColor, map.Secondary);
                foreach (var icon in QueryByClass(el, "ds-toast__icon")) Stamp(icon, TouchedProperty.UnityBgTint, map.Secondary);
            });
            ApplyClass(root, "ds-toast--warning", el =>
            {
                Stamp(el, TouchedProperty.BorderColor, map.Tertiary);
                foreach (var icon in QueryByClass(el, "ds-toast__icon")) Stamp(icon, TouchedProperty.UnityBgTint, map.Tertiary);
            });
            ApplyClass(root, "ds-toast--danger", el =>
            {
                Stamp(el, TouchedProperty.BorderColor, map.Danger);
                foreach (var icon in QueryByClass(el, "ds-toast__icon")) Stamp(icon, TouchedProperty.UnityBgTint, map.Danger);
            });
            ApplyClass(root, "ds-sheet",            el => { Stamp(el, TouchedProperty.Background, map.Surface);     Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-empty__icon-bg",   el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));

            // ── Tabs and view toggles ──────────────────────────────────────
            ApplyClass(root, "ds-tabs",        el => { Stamp(el, TouchedProperty.Background, map.SurfaceElev); Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-view-toggle", el => { Stamp(el, TouchedProperty.Background, map.SurfaceElev); Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-tab", el =>
            {
                if (el.ClassListContains("is-active"))
                {
                    Stamp(el, TouchedProperty.Background, map.Primary);
                    Stamp(el, TouchedProperty.Color,      map.TextOnAccent);
                }
                else
                {
                    Stamp(el, TouchedProperty.Color, map.TextSecondary);
                }
            });
            ApplyClass(root, "ds-view-toggle__btn", el =>
            {
                if (el.ClassListContains("is-active"))
                {
                    Stamp(el, TouchedProperty.Background,  map.Primary);
                    Stamp(el, TouchedProperty.UnityBgTint, map.TextOnAccent);
                }
                else
                {
                    Stamp(el, TouchedProperty.UnityBgTint, map.TextSecondary);
                }
            });

            // ── Buttons ────────────────────────────────────────────────────
            ApplyClass(root, "ds-btn--primary",   el => ApplyBrandButton(el, map, map.Primary,   map.PrimaryHover,   map.PrimaryPress,   map.TextOnAccent));
            ApplyClass(root, "ds-btn--secondary", el => ApplyBrandButton(el, map, map.Secondary, map.SecondaryHover, map.SecondaryPress, map.TextPrimary));
            ApplyClass(root, "ds-btn--tertiary",  el => ApplyBrandButton(el, map, map.Tertiary,  map.TertiaryHover,  Shade(map.Tertiary, 0.24f), map.TextPrimary));
            ApplyClass(root, "ds-btn--danger",    el => ApplyBrandButton(el, map, map.Danger,    map.DangerHover,    map.DangerPress,    map.TextPrimary));
            ApplyClass(root, "ds-btn--ghost",     el => ApplyGhostButton(el, map));
            ApplyClass(root, "ds-btn--icon",      el => ApplyIconButton(el, map));
            ApplyClass(root, "back-btn",          el => ApplyBackButton(el, map));

            // .ds-btn--with-icon contains a Unity Label child whose colour
            // overrides the button's own text colour. We need to colour the
            // INNER label per variant — the button-level Stamp above only
            // sets the (empty) Button text, not the wrapped Label.
            ApplyClass(root, "ds-btn--with-icon", el =>
            {
                bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
                Color labelColor = disabled
                    ? map.TextDisabled
                    : (el.ClassListContains("ds-btn--primary") ? map.TextOnAccent : map.TextPrimary);
                foreach (var lbl in QueryDescendants<Label>(el))
                    Stamp(lbl, TouchedProperty.Color, labelColor);
            });
            // back-btn label is a separate piece of chrome with its own class.
            ApplyClass(root, "back-btn__label", el => Stamp(el, TouchedProperty.Color, map.TextOnAccent));
            ApplyClass(root, "back-btn__icon",  el => Stamp(el, TouchedProperty.UnityBgTint, map.TextOnAccent));

            // ── Inputs (TextField inner element pattern) ───────────────────
            ApplyInputContainer(root, "ds-input",     map);
            ApplyInputContainer(root, "ds-textarea",  map);
            ApplySearchContainer(root, map);
            ApplyDropdownContainer(root, map);

            // ── Showcase-only chrome ───────────────────────────────────────
            ApplyClass(root, "showcase-drawer-frame", el => { Stamp(el, TouchedProperty.Background, map.Bg); Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "showcase-drawer-strip", el => Stamp(el, TouchedProperty.BorderBottomColor, map.Border));
            ApplyClass(root, "showcase-placeholder-text", el => Stamp(el, TouchedProperty.Color, map.TextDisabled));
            ApplyClass(root, "showcase-placeholder-icon", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextDisabled));

            // ── Navigation (Navigation.uss) ─────────────────────────────────
            // Side nav, side rail, bottom nav — each is a Surface-coloured
            // container with bordered chrome. The interactive items inside
            // toggle `is-active` to flip their fill + label/icon colour onto
            // the brand primary track (background uses an alpha tint of
            // primary, foreground uses the primary fill).
            ApplyClass(root, "ds-side-nav",    el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-side-rail",   el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-bottom-nav",  el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });

            ApplyClass(root, "ds-nav-item", el =>
            {
                bool active = el.ClassListContains("is-active");
                // Default ds-nav-item has transparent bg; only the active
                // variant gets the primary-soft fill.
                if (active)
                    Stamp(el, TouchedProperty.Background, WithAlpha(map.Primary, 0.16f));
                else
                    Stamp(el, TouchedProperty.Background, new Color(0f, 0f, 0f, 0f));
                foreach (var icon in QueryByClass(el, "ds-nav-item__icon"))
                    Stamp(icon, TouchedProperty.UnityBgTint, active ? map.Primary : map.TextSecondary);
                foreach (var label in QueryByClass(el, "ds-nav-item__label"))
                    Stamp(label, TouchedProperty.Color, active ? map.Primary : map.TextSecondary);
            });

            ApplyClass(root, "ds-rail-item", el =>
            {
                bool active = el.ClassListContains("is-active");
                if (active)
                    Stamp(el, TouchedProperty.Background, WithAlpha(map.Primary, 0.16f));
                else
                    Stamp(el, TouchedProperty.Background, new Color(0f, 0f, 0f, 0f));
                foreach (var icon in QueryByClass(el, "ds-rail-item__icon"))
                    Stamp(icon, TouchedProperty.UnityBgTint, active ? map.Primary : map.TextSecondary);
            });

            ApplyClass(root, "ds-bottom-nav__item", el =>
            {
                bool active = el.ClassListContains("is-active");
                Stamp(el, TouchedProperty.Background, new Color(0f, 0f, 0f, 0f));
                foreach (var icon in QueryByClass(el, "ds-bottom-nav__icon"))
                    Stamp(icon, TouchedProperty.UnityBgTint, active ? map.Primary : map.TextSecondary);
                foreach (var label in QueryByClass(el, "ds-bottom-nav__label"))
                    Stamp(label, TouchedProperty.Color, active ? map.Primary : map.TextSecondary);
            });

            ApplyClass(root, "ds-profile",          el => { Stamp(el, TouchedProperty.Background, map.SurfaceElev); Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-profile__avatar",  el => Stamp(el, TouchedProperty.Background, map.SurfaceHover));
            ApplyClass(root, "ds-profile__name",    el => Stamp(el, TouchedProperty.Color, map.TextPrimary));
            ApplyClass(root, "ds-profile__chevron", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextSecondary));

            // ── Toggles / Checks / Radios (Controls.uss) ────────────────────
            // Pseudo-class `:checked` can't be overridden via inline style, so
            // we snapshot the current value and stamp the appropriate colours.
            // We also wire a value-change callback so flipping the control
            // re-paints without needing a full re-apply.
            foreach (var t in QueryByClass(root, "ds-toggle"))
                WireToggleTrack(t as Toggle, map, isCheckbox: false);
            foreach (var t in QueryByClass(root, "ds-check"))
                WireToggleTrack(t as Toggle, map, isCheckbox: true);
            foreach (var r in QueryByClass(root, "ds-radio"))
                WireRadio(r as RadioButton, map);

            // ── Sliders / Range / Progress (Controls.uss) ───────────────────
            ApplyClass(root, "ds-slider", el =>
            {
                foreach (var t in QueryByClass(el, "unity-base-slider__tracker"))
                    Stamp(t, TouchedProperty.Background, map.SurfaceElev);
                foreach (var d in QueryByClass(el, "unity-base-slider__dragger"))
                    Stamp(d, TouchedProperty.Background, map.TextPrimary);
            });
            ApplyClass(root, "ds-range", el =>
            {
                foreach (var t in QueryByClass(el, "unity-min-max-slider__tracker"))
                    Stamp(t, TouchedProperty.Background, map.SurfaceElev);
                foreach (var d in QueryByClass(el, "unity-min-max-slider__dragger"))
                    Stamp(d, TouchedProperty.Background, map.Primary);
                foreach (var tn in QueryByClass(el, "unity-min-max-slider__min-thumb"))
                    Stamp(tn, TouchedProperty.Background, map.TextPrimary);
                foreach (var tn in QueryByClass(el, "unity-min-max-slider__max-thumb"))
                    Stamp(tn, TouchedProperty.Background, map.TextPrimary);
            });
            ApplyClass(root, "ds-progress", el =>
            {
                foreach (var bg in QueryByClass(el, "unity-progress-bar__background"))
                    Stamp(bg, TouchedProperty.Background, map.SurfaceElev);
                foreach (var pr in QueryByClass(el, "unity-progress-bar__progress"))
                    Stamp(pr, TouchedProperty.Background, map.Primary);
            });

            // ── Quantity stepper (Feedback.uss) ─────────────────────────────
            ApplyClass(root, "ds-stepper",       el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-stepper__btn",  el =>
            {
                bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
                Stamp(el, TouchedProperty.Background, map.SurfaceElev);
                Stamp(el, TouchedProperty.Color, disabled ? map.TextDisabled : map.TextPrimary);
            });
            ApplyClass(root, "ds-stepper__value", el => Stamp(el, TouchedProperty.Color, map.TextPrimary));

            // ── Pagination (Feedback.uss) ───────────────────────────────────
            ApplyClass(root, "ds-pagination",    el => { Stamp(el, TouchedProperty.Background, map.Surface);    Stamp(el, TouchedProperty.BorderColor, map.Border); });
            ApplyClass(root, "ds-pagination__btn", el =>
            {
                bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
                bool active   = el.ClassListContains("is-active");
                if (active)
                {
                    Stamp(el, TouchedProperty.Background, map.Primary);
                    Stamp(el, TouchedProperty.Color, map.TextOnAccent);
                }
                else
                {
                    Stamp(el, TouchedProperty.Background, new Color(0f, 0f, 0f, 0f));
                    Stamp(el, TouchedProperty.Color, disabled ? map.TextDisabled : map.TextSecondary);
                }
            });
            ApplyClass(root, "ds-pagination__ellipsis", el => Stamp(el, TouchedProperty.Color, map.TextSecondary));

            // ── Loading states (Feedback.uss) ───────────────────────────────
            // Spinner: ring with primary head + surface-elev tail. The four
            // border-top/right/bottom/left properties are set ASYMMETRICALLY,
            // so we can't use the BorderColor TouchedProperty (which mirrors a
            // single colour onto all four). Stamp each side individually and
            // track the touches manually for revert.
            ApplyClass(root, "ds-spinner", el =>
            {
                el.style.borderTopColor    = map.Primary;
                el.style.borderRightColor  = map.SurfaceElev;
                el.style.borderBottomColor = map.SurfaceElev;
                el.style.borderLeftColor   = map.SurfaceElev;
                if (!_touched.TryGetValue(el, out var set)) { set = new HashSet<TouchedProperty>(); _touched[el] = set; }
                set.Add(TouchedProperty.BorderColor);
            });
            ApplyClass(root, "ds-skeleton", el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));

            // ── Notification dot (Badges.uss) ───────────────────────────────
            ApplyClass(root, "ds-notif-icon", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextPrimary));
            ApplyClass(root, "ds-notif-dot",  el => { Stamp(el, TouchedProperty.Background, map.Danger); Stamp(el, TouchedProperty.BorderColor, map.Bg); });
            ApplyClass(root, "ds-notif-dot__count", el => Stamp(el, TouchedProperty.Color, map.TextPrimary));

            // ── Avatar (Badges.uss) — placeholder fill, image overrides via inline ─
            ApplyClass(root, "ds-avatar", el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));

            // ── Rarity badges (Badges.uss) ──────────────────────────────────
            // The badge's TEXT colour uses --color-rarity-* tokens which are
            // brand-meaning constants (common=green, rare=blue, …). We leave
            // those alone. Only the soft background tints flow from codigrate
            // brand colours.
            ApplyClass(root, "ds-badge--common",    el => Stamp(el, TouchedProperty.Background, WithAlpha(map.Primary,   0.16f)));
            ApplyClass(root, "ds-badge--rare",      el => Stamp(el, TouchedProperty.Background, WithAlpha(map.Secondary, 0.16f)));
            ApplyClass(root, "ds-badge--epic",      el => Stamp(el, TouchedProperty.Background, WithAlpha(map.Tertiary,  0.16f)));
            ApplyClass(root, "ds-badge--legendary", el => Stamp(el, TouchedProperty.Background, WithAlpha(map.Warning,   0.16f)));

            // ── Habitat tags (Badges.uss) ───────────────────────────────────
            ApplyClass(root, "ds-tag--amphibious", el => { Stamp(el, TouchedProperty.Background, map.Warning); Stamp(el, TouchedProperty.Color, map.TextOnAccent); });
            ApplyClass(root, "ds-tag--aquatic",    el => { Stamp(el, TouchedProperty.Background, WithAlpha(map.Secondary, 0.16f)); Stamp(el, TouchedProperty.BorderColor, map.Secondary); Stamp(el, TouchedProperty.Color, map.Secondary); });
            ApplyClass(root, "ds-tag--nocturnal",  el => { Stamp(el, TouchedProperty.Background, WithAlpha(map.Tertiary,  0.16f)); Stamp(el, TouchedProperty.BorderColor, map.Tertiary);  Stamp(el, TouchedProperty.Color, map.Tertiary); });

            // ── Status chips (Badges.uss) ───────────────────────────────────
            // The chip's `color` is inherited by the inner unity-label, so
            // applying once on the chip suffices for the text. The chip icon
            // tint is set on the `.ds-chip__icon` descendant explicitly.
            ApplyChip(root, "ds-chip--equipped", map.Primary,        map.Primary,       map.Primary);
            ApplyChip(root, "ds-chip--new",      map.Primary,        map.Primary,       map.Primary);
            ApplyChip(root, "ds-chip--owned",    new Color(0,0,0,0), map.BorderStrong,  map.TextSecondary, transparent: true);
            ApplyChip(root, "ds-chip--limited",  map.Danger,         map.Danger,        map.Danger);
            ApplyChip(root, "ds-chip--event",    map.Secondary,      map.Secondary,     map.Secondary);
            ApplyChip(root, "ds-chip--sale",     map.Warning,        map.Warning,       map.Warning);

            // ── Icon tint variants (Icons.uss) ──────────────────────────────
            ApplyClass(root, "ds-icon--primary",   el => Stamp(el, TouchedProperty.UnityBgTint, map.TextPrimary));
            ApplyClass(root, "ds-icon--secondary", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextSecondary));
            ApplyClass(root, "ds-icon--disabled",  el => Stamp(el, TouchedProperty.UnityBgTint, map.TextDisabled));
            ApplyClass(root, "ds-icon--danger",    el => Stamp(el, TouchedProperty.UnityBgTint, map.Danger));
            ApplyClass(root, "ds-icon--warning",   el => Stamp(el, TouchedProperty.UnityBgTint, map.Warning));
            ApplyClass(root, "ds-icon--info",      el => Stamp(el, TouchedProperty.UnityBgTint, map.Secondary));
            ApplyClass(root, "ds-icon--on-accent", el => Stamp(el, TouchedProperty.UnityBgTint, map.TextOnAccent));
            // ds-icon--rarity-* stay DS-default (brand-meaning constants).

            // ── Scrollbar demo frame (Feedback.uss) ─────────────────────────
            ApplyClass(root, "ds-scrollbar-demo", el => { Stamp(el, TouchedProperty.Background, map.Bg); Stamp(el, TouchedProperty.BorderColor, map.Border); });

            // ── Swatches (drive the COLORS-section fills directly) ─────────
            ApplyClass(root, "ds-swatch--primary",        el => Stamp(el, TouchedProperty.Background, map.Primary));
            ApplyClass(root, "ds-swatch--primary-hover",  el => Stamp(el, TouchedProperty.Background, map.PrimaryHover));
            ApplyClass(root, "ds-swatch--secondary",      el => Stamp(el, TouchedProperty.Background, map.Secondary));
            ApplyClass(root, "ds-swatch--tertiary",       el => Stamp(el, TouchedProperty.Background, map.Tertiary));
            ApplyClass(root, "ds-swatch--warning",        el => Stamp(el, TouchedProperty.Background, map.Warning));
            ApplyClass(root, "ds-swatch--danger",         el => Stamp(el, TouchedProperty.Background, map.Danger));
            ApplyClass(root, "ds-swatch--text-primary",   el => Stamp(el, TouchedProperty.Background, map.TextPrimary));
            ApplyClass(root, "ds-swatch--text-secondary", el => Stamp(el, TouchedProperty.Background, map.TextSecondary));
            ApplyClass(root, "ds-swatch--text-disabled",  el => Stamp(el, TouchedProperty.Background, map.TextDisabled));
            ApplyClass(root, "ds-swatch--bg",             el => Stamp(el, TouchedProperty.Background, map.Bg));
            ApplyClass(root, "ds-swatch--surface",        el => Stamp(el, TouchedProperty.Background, map.Surface));
            ApplyClass(root, "ds-swatch--surface-elev",   el => Stamp(el, TouchedProperty.Background, map.SurfaceElev));
            ApplyClass(root, "ds-swatch--border",         el => Stamp(el, TouchedProperty.Background, map.Border));

            // ── Misc small fry ────────────────────────────────────────────
            ApplyClass(root, "ds-icon--accent",  el => Stamp(el, TouchedProperty.UnityBgTint, map.Primary));
            // The `.ds-section__title` is the small uppercase caption above
            // each section — without an override the cascading colour from
            // the .ds-root pass takes over and reads it as primary instead
            // of the dimmer secondary it has in DS by default.
            ApplyClass(root, "ds-section__title", el => Stamp(el, TouchedProperty.Color, map.TextSecondary));
            // .ds-swatch__name + .ds-swatch__hex labels read against the
            // section's surface. Without explicit colours they inherit the
            // root's TextPrimary — fine on dark themes, too bright on light
            // themes where the surface is white.
            ApplyClass(root, "ds-swatch__name", el => Stamp(el, TouchedProperty.Color, map.TextPrimary));
            ApplyClass(root, "ds-swatch__hex",  el => Stamp(el, TouchedProperty.Color, map.TextSecondary));
        }

        public static void Revert(VisualElement root)
        {
            if (root == null) return;

            foreach (var kv in _touched)
            {
                var el = kv.Key;
                if (el == null) continue;
                foreach (var p in kv.Value)
                {
                    switch (p)
                    {
                        case TouchedProperty.Background:
                            el.style.backgroundColor = new StyleColor(StyleKeyword.Null); break;
                        case TouchedProperty.BorderColor:
                            el.style.borderTopColor    = new StyleColor(StyleKeyword.Null);
                            el.style.borderRightColor  = new StyleColor(StyleKeyword.Null);
                            el.style.borderBottomColor = new StyleColor(StyleKeyword.Null);
                            el.style.borderLeftColor   = new StyleColor(StyleKeyword.Null);
                            break;
                        case TouchedProperty.BorderBottomColor:
                            el.style.borderBottomColor = new StyleColor(StyleKeyword.Null); break;
                        case TouchedProperty.Color:
                            el.style.color = new StyleColor(StyleKeyword.Null); break;
                        case TouchedProperty.UnityBgTint:
                            el.style.unityBackgroundImageTintColor = new StyleColor(StyleKeyword.Null); break;
                    }
                }
            }
            _touched.Clear();

            foreach (var kv in _enterHandlers) kv.Key?.UnregisterCallback(kv.Value);
            foreach (var kv in _leaveHandlers) kv.Key?.UnregisterCallback(kv.Value);
            _enterHandlers.Clear();
            _leaveHandlers.Clear();

            // Toggle / radio value-change callbacks installed by WireToggleTrack
            // / WireRadio. UnregisterValueChangedCallback wants the exact
            // delegate we registered earlier, which is what the dictionary
            // stores keyed by the control.
            foreach (var kv in _toggleHandlers) kv.Key?.UnregisterValueChangedCallback(kv.Value);
            foreach (var kv in _radioHandlers)  kv.Key?.UnregisterValueChangedCallback(kv.Value);
            _toggleHandlers.Clear();
            _radioHandlers.Clear();
        }

        // ─── BUTTON HELPERS ─────────────────────────────────────────────────

        static void ApplyBrandButton(VisualElement el, ColorMap m, Color baseC, Color hoverC, Color pressC, Color textC)
        {
            // Disabled state mirrors the `.ds-btn--X:disabled` USS rule:
            // surface-elev fill with the border matching the fill so the
            // 2px outline doesn't read as a ring, plus the lower-contrast
            // text colour.
            bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
            if (disabled)
            {
                Stamp(el, TouchedProperty.Background, m.SurfaceElev);
                Stamp(el, TouchedProperty.BorderColor, m.SurfaceElev);
                Stamp(el, TouchedProperty.Color, m.TextDisabled);
                return;
            }

            // `.ds-btn--demo-hover` and `.ds-btn--pressed` are showcase-only
            // modifiers that statically pin the hover / press visual so the
            // BUTTONS row can show all three states side-by-side. Skip the
            // pointer hover wiring for those — they need their pinned look
            // to stay regardless of cursor position.
            if (el.ClassListContains("ds-btn--pressed"))
            {
                Stamp(el, TouchedProperty.Background, pressC);
                Stamp(el, TouchedProperty.BorderColor, pressC);
                Stamp(el, TouchedProperty.Color, textC);
                return;
            }
            if (el.ClassListContains("ds-btn--demo-hover"))
            {
                Stamp(el, TouchedProperty.Background, hoverC);
                Stamp(el, TouchedProperty.BorderColor, hoverC);
                Stamp(el, TouchedProperty.Color, textC);
                return;
            }

            Stamp(el, TouchedProperty.Background, baseC);
            Stamp(el, TouchedProperty.BorderColor, baseC);
            Stamp(el, TouchedProperty.Color, textC);
            InstallHover(el, baseC, hoverC);
        }

        static void ApplyGhostButton(VisualElement el, ColorMap m)
        {
            bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
            Color transparent = new Color(0f, 0f, 0f, 0f);

            if (disabled)
            {
                Stamp(el, TouchedProperty.Background, transparent);
                Stamp(el, TouchedProperty.BorderColor, m.Border);
                Stamp(el, TouchedProperty.Color, m.TextDisabled);
                return;
            }
            if (el.ClassListContains("ds-btn--pressed"))
            {
                // USS: ghost press fills with --color-surface.
                Stamp(el, TouchedProperty.Background, m.Surface);
                Stamp(el, TouchedProperty.BorderColor, m.Border);
                Stamp(el, TouchedProperty.Color, m.TextPrimary);
                return;
            }
            if (el.ClassListContains("ds-btn--demo-hover"))
            {
                Stamp(el, TouchedProperty.Background, m.SurfaceElev);
                Stamp(el, TouchedProperty.BorderColor, m.BorderStrong);
                Stamp(el, TouchedProperty.Color, m.TextPrimary);
                return;
            }

            // Default ghost: transparent fill + border + primary text.
            Stamp(el, TouchedProperty.Background, transparent);
            Stamp(el, TouchedProperty.BorderColor, m.Border);
            Stamp(el, TouchedProperty.Color, m.TextPrimary);

            // Hover swap is a two-axis change (bg + border) so the generic
            // InstallHover helper doesn't fit — borders go to `BorderStrong`,
            // not the bg's hover variant. Inline the callbacks here.
            if (_enterHandlers.TryGetValue(el, out var prevEnter)) el.UnregisterCallback(prevEnter);
            if (_leaveHandlers.TryGetValue(el, out var prevLeave)) el.UnregisterCallback(prevLeave);

            Color baseBg = transparent;
            Color hoverBg = m.SurfaceElev;
            Color baseBorder = m.Border;
            Color hoverBorder = m.BorderStrong;
            VisualElement elCap = el;

            EventCallback<PointerEnterEvent> onEnter = _ =>
            {
                elCap.style.backgroundColor = hoverBg;
                elCap.style.borderTopColor = hoverBorder;
                elCap.style.borderRightColor = hoverBorder;
                elCap.style.borderBottomColor = hoverBorder;
                elCap.style.borderLeftColor  = hoverBorder;
            };
            EventCallback<PointerLeaveEvent> onLeave = _ =>
            {
                elCap.style.backgroundColor = baseBg;
                elCap.style.borderTopColor = baseBorder;
                elCap.style.borderRightColor = baseBorder;
                elCap.style.borderBottomColor = baseBorder;
                elCap.style.borderLeftColor  = baseBorder;
            };
            el.RegisterCallback(onEnter);
            el.RegisterCallback(onLeave);
            _enterHandlers[el] = onEnter;
            _leaveHandlers[el] = onLeave;
        }

        static void ApplyIconButton(VisualElement el, ColorMap m)
        {
            // `.ds-btn--icon-danger` overrides the standard icon-button style
            // with the danger-soft alpha fill and danger border/text.
            if (el.ClassListContains("ds-btn--icon-danger"))
            {
                Color softDanger = m.Danger; softDanger.a = 0.16f;
                Stamp(el, TouchedProperty.Background, softDanger);
                Stamp(el, TouchedProperty.BorderColor, m.Danger);
                Stamp(el, TouchedProperty.Color, m.Danger);
                return;
            }

            // Standard icon button — same fill / border / text as
            // .ds-btn:disabled in DS (it's intentionally subtle chrome).
            Stamp(el, TouchedProperty.Background, m.SurfaceElev);
            Stamp(el, TouchedProperty.BorderColor, m.Border);
            Stamp(el, TouchedProperty.Color, m.TextPrimary);
        }

        static void ApplyBackButton(VisualElement el, ColorMap m)
        {
            bool disabled = !el.enabledSelf || el.ClassListContains("unity-disabled");
            if (disabled)
            {
                Stamp(el, TouchedProperty.Background, m.SurfaceElev);
                Stamp(el, TouchedProperty.BorderColor, m.Border);
                Stamp(el, TouchedProperty.Color, m.TextDisabled);
                return;
            }
            Stamp(el, TouchedProperty.Background, m.Primary);
            Stamp(el, TouchedProperty.BorderColor, m.Primary);
            Stamp(el, TouchedProperty.Color, m.TextOnAccent);
            InstallHover(el, m.Primary, m.PrimaryHover);
        }

        // ─── CHIP / TOGGLE / RADIO HELPERS ──────────────────────────────────

        // Per-modifier-class chip applier. Sets bg (soft tint of `brand` unless
        // `transparent=true`), border, text colour on the chip itself, and
        // tints any `.ds-chip__icon` descendant to match. The chip's text
        // colour cascades to the inner `.unity-label` via inheritance — no
        // need to walk Labels explicitly.
        static void ApplyChip(VisualElement root, string modifierClass, Color brand, Color borderC, Color textC, bool transparent = false)
        {
            foreach (var chip in QueryByClass(root, modifierClass))
            {
                Color bg = transparent ? new Color(0f, 0f, 0f, 0f) : WithAlpha(brand, 0.16f);
                Stamp(chip, TouchedProperty.Background, bg);
                Stamp(chip, TouchedProperty.BorderColor, borderC);
                Stamp(chip, TouchedProperty.Color, textC);
                foreach (var icon in QueryByClass(chip, "ds-chip__icon"))
                    Stamp(icon, TouchedProperty.UnityBgTint, textC);
            }
        }

        // Toggle / checkbox `:checked` pseudo can't be hit by inline styles
        // directly, so we snapshot the current value and stamp the colour the
        // USS rule would resolve to. A value-change callback keeps the visual
        // in sync if the user flips the control while the theme is active.
        //
        // Toggle (`ds-toggle`) — pill track that fills with primary when on.
        // Checkbox (`ds-check`) — square that fills + shows a check glyph
        // when on; we also re-tint the embedded check icon to text-on-accent.
        static void WireToggleTrack(Toggle toggle, ColorMap m, bool isCheckbox)
        {
            if (toggle == null) return;
            var checkmark = toggle.Q(className: "unity-toggle__checkmark");
            if (checkmark == null) return;

            void Repaint(bool on)
            {
                if (on)
                {
                    checkmark.style.backgroundColor = m.Primary;
                    checkmark.style.borderTopColor = m.Primary;
                    checkmark.style.borderRightColor = m.Primary;
                    checkmark.style.borderBottomColor = m.Primary;
                    checkmark.style.borderLeftColor = m.Primary;
                    if (isCheckbox)
                        checkmark.style.unityBackgroundImageTintColor = m.TextOnAccent;
                }
                else
                {
                    checkmark.style.backgroundColor = m.SurfaceElev;
                    checkmark.style.borderTopColor = m.Border;
                    checkmark.style.borderRightColor = m.Border;
                    checkmark.style.borderBottomColor = m.Border;
                    checkmark.style.borderLeftColor = m.Border;
                    if (isCheckbox)
                        checkmark.style.unityBackgroundImageTintColor = new Color(0f, 0f, 0f, 0f);
                }
            }
            Repaint(toggle.value);

            // Track properties we touched so Revert clears them.
            if (!_touched.TryGetValue(checkmark, out var set)) { set = new HashSet<TouchedProperty>(); _touched[checkmark] = set; }
            set.Add(TouchedProperty.Background);
            set.Add(TouchedProperty.BorderColor);
            if (isCheckbox) set.Add(TouchedProperty.UnityBgTint);

            // De-register a prior callback if Apply ran twice (e.g. after a
            // theme swap) so we don't leak handlers onto the same Toggle.
            if (_toggleHandlers.TryGetValue(toggle, out var prev))
                toggle.UnregisterValueChangedCallback(prev);

            EventCallback<ChangeEvent<bool>> cb = evt => Repaint(evt.newValue);
            toggle.RegisterValueChangedCallback(cb);
            _toggleHandlers[toggle] = cb;
        }

        // Radio (`ds-radio`) — outer ring + inner dot. Both elements live
        // inside the RadioButton at known class names. Same snapshot +
        // callback pattern as WireToggleTrack.
        static void WireRadio(RadioButton radio, ColorMap m)
        {
            if (radio == null) return;
            var ring = radio.Q(className: "unity-radio-button__checkmark-background");
            var dot  = radio.Q(className: "unity-radio-button__checkmark");

            void Repaint(bool on)
            {
                if (ring != null)
                {
                    ring.style.backgroundColor = m.SurfaceElev;
                    ring.style.borderTopColor = on ? m.Primary : m.Border;
                    ring.style.borderRightColor = on ? m.Primary : m.Border;
                    ring.style.borderBottomColor = on ? m.Primary : m.Border;
                    ring.style.borderLeftColor = on ? m.Primary : m.Border;
                }
                if (dot != null)
                    dot.style.backgroundColor = on ? m.Primary : new Color(0f, 0f, 0f, 0f);
            }
            Repaint(radio.value);

            if (ring != null)
            {
                if (!_touched.TryGetValue(ring, out var set)) { set = new HashSet<TouchedProperty>(); _touched[ring] = set; }
                set.Add(TouchedProperty.Background);
                set.Add(TouchedProperty.BorderColor);
            }
            if (dot != null)
            {
                if (!_touched.TryGetValue(dot, out var set2)) { set2 = new HashSet<TouchedProperty>(); _touched[dot] = set2; }
                set2.Add(TouchedProperty.Background);
            }

            if (_radioHandlers.TryGetValue(radio, out var prev))
                radio.UnregisterValueChangedCallback(prev);

            EventCallback<ChangeEvent<bool>> cb = evt => Repaint(evt.newValue);
            radio.RegisterValueChangedCallback(cb);
            _radioHandlers[radio] = cb;
        }

        static readonly Dictionary<Toggle, EventCallback<ChangeEvent<bool>>> _toggleHandlers
            = new Dictionary<Toggle, EventCallback<ChangeEvent<bool>>>();
        static readonly Dictionary<RadioButton, EventCallback<ChangeEvent<bool>>> _radioHandlers
            = new Dictionary<RadioButton, EventCallback<ChangeEvent<bool>>>();

        // ─── INPUT / DROPDOWN HELPERS ───────────────────────────────────────

        // Input fills use `m.Bg` (codigrate `editorBackground` / DS
        // `--color-bg`), matching the DS USS default + codigrate author's
        // ask. Inputs read as recessed wells INSIDE their parent
        // `.ds-section` / `.ds-card` (which sit at `m.Surface` = codigrate
        // `windowBackground`). Visually mirrors the IDE pattern these
        // palettes are designed for — the editable area is the deepest
        // layer in dark themes, lightest in light themes.
        static void ApplyInputContainer(VisualElement root, string wrapperClass, ColorMap m)
        {
            foreach (var wrapper in QueryByClass(root, wrapperClass))
            {
                foreach (var inner in QueryInnerInputFields(wrapper))
                {
                    Stamp(inner, TouchedProperty.Background, m.Bg);
                    Stamp(inner, TouchedProperty.BorderColor, m.Border);
                    Stamp(inner, TouchedProperty.Color, m.TextPrimary);
                }
            }
        }

        static void ApplySearchContainer(VisualElement root, ColorMap m)
        {
            foreach (var wrapper in QueryByClass(root, "ds-search"))
            {
                // Outer .ds-search owns the visible chrome; same `m.Bg`
                // tier as `.ds-input` for visual consistency.
                Stamp(wrapper, TouchedProperty.Background, m.Bg);
                Stamp(wrapper, TouchedProperty.BorderColor, m.Border);

                // Inner field has transparent bg; we only repaint its text.
                foreach (var inner in QueryInnerInputFields(wrapper))
                {
                    Stamp(inner, TouchedProperty.Color, m.TextPrimary);
                }
            }
            ApplyClass(root, "ds-search__icon", el => Stamp(el, TouchedProperty.UnityBgTint, m.TextSecondary));
        }

        static void ApplyDropdownContainer(VisualElement root, ColorMap m)
        {
            foreach (var wrapper in QueryByClass(root, "ds-dropdown"))
            {
                // Visible field box: .unity-popup-field__input (and aliases).
                foreach (var inner in QueryInnerPopupBoxes(wrapper))
                {
                    Stamp(inner, TouchedProperty.Background, m.Bg);
                    Stamp(inner, TouchedProperty.BorderColor, m.Border);
                    Stamp(inner, TouchedProperty.Color, m.TextPrimary);
                }

                // Selected value text element (sits inside the input box).
                foreach (var text in QueryByClass(wrapper, "unity-popup-field__text"))
                    Stamp(text, TouchedProperty.Color, m.TextPrimary);
                foreach (var text in QueryByClass(wrapper, "unity-base-popup-field__text"))
                    Stamp(text, TouchedProperty.Color, m.TextPrimary);

                // Chevron arrow tint.
                foreach (var arrow in QueryByClass(wrapper, "unity-base-popup-field__arrow"))
                    Stamp(arrow, TouchedProperty.UnityBgTint, m.TextSecondary);
            }
        }

        static IEnumerable<VisualElement> QueryInnerInputFields(VisualElement wrapper)
        {
            // Match every class variant Unity 6 emits for the visible input
            // box — same selector list the Inputs.uss rule uses. We collect
            // into a HashSet so an element matching two of the aliases isn't
            // stamped twice.
            var set = new HashSet<VisualElement>();
            void Add(string cls) { foreach (var el in QueryByClass(wrapper, cls)) set.Add(el); }
            Add("unity-text-input");
            Add("unity-text-field__input");
            Add("unity-base-text-field__input");
            Add("unity-base-field__input");
            return set;
        }

        static IEnumerable<VisualElement> QueryInnerPopupBoxes(VisualElement wrapper)
        {
            var set = new HashSet<VisualElement>();
            void Add(string cls) { foreach (var el in QueryByClass(wrapper, cls)) set.Add(el); }
            Add("unity-popup-field__input");
            Add("unity-base-popup-field__input");
            Add("unity-base-field__input");
            return set;
        }

        // ─── INTERNAL UTILITIES ─────────────────────────────────────────────

        static void ApplyClass(VisualElement root, string className, Action<VisualElement> fn)
        {
            foreach (var el in QueryByClass(root, className))
                fn(el);
        }

        static IEnumerable<VisualElement> QueryByClass(VisualElement root, string className)
        {
            var list = new List<VisualElement>();
            root.Query<VisualElement>(className: className).ForEach(list.Add);
            return list;
        }

        static IEnumerable<T> QueryDescendants<T>(VisualElement root) where T : VisualElement
        {
            var list = new List<T>();
            root.Query<T>().ForEach(list.Add);
            return list;
        }

        static void Stamp(VisualElement el, TouchedProperty prop, Color value)
        {
            if (el == null) return;
            switch (prop)
            {
                case TouchedProperty.Background:        el.style.backgroundColor = value; break;
                case TouchedProperty.BorderColor:
                    el.style.borderTopColor    = value;
                    el.style.borderRightColor  = value;
                    el.style.borderBottomColor = value;
                    el.style.borderLeftColor   = value;
                    break;
                case TouchedProperty.BorderBottomColor: el.style.borderBottomColor = value; break;
                case TouchedProperty.Color:             el.style.color = value; break;
                case TouchedProperty.UnityBgTint:       el.style.unityBackgroundImageTintColor = value; break;
            }
            if (!_touched.TryGetValue(el, out var set))
            {
                set = new HashSet<TouchedProperty>();
                _touched[el] = set;
            }
            set.Add(prop);
        }

        static void InstallHover(VisualElement el, Color baseColor, Color hoverColor)
        {
            if (_enterHandlers.TryGetValue(el, out var prevEnter)) el.UnregisterCallback(prevEnter);
            if (_leaveHandlers.TryGetValue(el, out var prevLeave)) el.UnregisterCallback(prevLeave);

            Color baseCap  = baseColor;
            Color hoverCap = hoverColor;
            VisualElement elCap = el;

            EventCallback<PointerEnterEvent> onEnter = _ =>
            {
                elCap.style.backgroundColor = hoverCap;
                elCap.style.borderTopColor = hoverCap;
                elCap.style.borderRightColor = hoverCap;
                elCap.style.borderBottomColor = hoverCap;
                elCap.style.borderLeftColor = hoverCap;
            };
            EventCallback<PointerLeaveEvent> onLeave = _ =>
            {
                elCap.style.backgroundColor = baseCap;
                elCap.style.borderTopColor = baseCap;
                elCap.style.borderRightColor = baseCap;
                elCap.style.borderBottomColor = baseCap;
                elCap.style.borderLeftColor = baseCap;
            };
            el.RegisterCallback(onEnter);
            el.RegisterCallback(onLeave);
            _enterHandlers[el] = onEnter;
            _leaveHandlers[el] = onLeave;
        }

        // ─── COLOR MAP ──────────────────────────────────────────────────────

        public sealed class ColorMap
        {
            public Color Bg;
            public Color Surface;
            public Color SurfaceElev;
            public Color SurfaceHover;
            public Color Border;
            public Color BorderStrong;
            public Color TextPrimary;
            public Color TextSecondary;
            public Color TextDisabled;
            public Color TextOnAccent;
            public Color Primary;
            public Color PrimaryHover;
            public Color PrimaryPress;
            public Color PrimarySoft;
            public Color Secondary;
            public Color SecondaryHover;
            public Color SecondaryPress;
            public Color Tertiary;
            public Color TertiaryHover;
            public Color Warning;
            public Color WarningHover;
            public Color Danger;
            public Color DangerHover;
            public Color DangerPress;
            public Color Overlay;
        }

        // Translate the codigrate "interface" group into the broader DS palette.
        // Codigrate exposes 12 colours; DS has ~25. We synthesise the missing
        // ones (hover/press tints, "soft" alpha variants, surface elevations)
        // from the codigrate inputs so brand variants stay consistent.
        //
        // Surface layering — codigrate's IDE semantics line up with DS three
        // levels deep (confirmed with codigrate's author 2026-05-17). Holding
        // the relationship the same in both light and dark themes:
        //     editorBackground  →  DS `--color-bg`           (deepest pit, page bg)
        //     windowBackground  →  DS `--color-surface`      (body / cards / sections)
        //     surface           →  DS `--color-surface-elev` (raised chrome — sidebar, drawer, modal)
        // The luminance ordering is consistent across every palette we ship:
        // dark themes have editor < window < surface; light themes have the
        // reverse, which matches DS's expectation that surfaceElev is the
        // MOST-tinted layer in a light theme and the LIGHTEST layer in a
        // dark theme. Earlier wiring had `surface → DS Surface` (using the
        // raised chrome colour for cards), which made every section + card
        // sit at the elevation tier instead of on the body, and the visible
        // page background went to `windowBackground` — wrong by one layer.
        public static ColorMap FromCodigrate(CodigrateThemeProvider.ThemePalette palette)
        {
            var t = palette.Interface;
            bool isLight = string.Equals(palette.Appearance, "light", StringComparison.OrdinalIgnoreCase);

            // Border / hover tints derive from the BODY surface (the layer
            // most cards and bordered chrome sit on), not the raised chrome,
            // so the visible 1px border on a `.ds-card` reads as a tint of
            // its own fill rather than of the bg.
            Color toward = t.PrimaryForeground;
            Color borderC      = Mix(t.WindowBackground, toward, isLight ? 0.16f : 0.20f);
            Color borderStrong = Mix(t.WindowBackground, toward, isLight ? 0.32f : 0.36f);
            Color surfaceHover = Mix(t.WindowBackground, toward, 0.08f);
            Color textDisabled = Mix(t.SecondaryForeground, t.WindowBackground, 0.45f);

            return new ColorMap
            {
                Bg             = t.EditorBackground,
                Surface        = t.WindowBackground,
                SurfaceElev    = t.Surface,
                SurfaceHover   = surfaceHover,
                Border         = borderC,
                BorderStrong   = borderStrong,
                TextPrimary    = t.PrimaryForeground,
                TextSecondary  = t.SecondaryForeground,
                TextDisabled   = textDisabled,
                TextOnAccent   = ContrastFor(t.AccentColor),
                Primary        = t.AccentColor,
                PrimaryHover   = Shade(t.AccentColor, 0.12f),
                PrimaryPress   = Shade(t.AccentColor, 0.24f),
                PrimarySoft    = WithAlpha(t.AccentColor, 0.16f),
                Secondary      = t.Info,
                SecondaryHover = Shade(t.Info, 0.12f),
                SecondaryPress = Shade(t.Info, 0.24f),
                Tertiary       = t.AlternateBackground,
                TertiaryHover  = Shade(t.AlternateBackground, 0.12f),
                Warning        = t.Warning,
                WarningHover   = t.WarningFocused,
                Danger         = t.Error,
                DangerHover    = Shade(t.Error, 0.12f),
                DangerPress    = Shade(t.Error, 0.24f),
                Overlay        = new Color(0f, 0f, 0f, isLight ? 0.5f : 0.6f),
            };
        }

        // ─── COLOR UTILS ────────────────────────────────────────────────────

        static Color Mix(Color a, Color b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color(
                Mathf.Lerp(a.r, b.r, t),
                Mathf.Lerp(a.g, b.g, t),
                Mathf.Lerp(a.b, b.b, t),
                Mathf.Lerp(a.a, b.a, t));
        }

        // Darken when the base is light, lighten when the base is dark — both
        // produce a "hover" variant whose contrast against the surrounding
        // surface stays roughly constant regardless of the palette's mood.
        static Color Shade(Color c, float amount)
        {
            float lum = Luminance(c);
            Color target = lum > 0.55f ? Color.black : Color.white;
            return Mix(c, target, amount);
        }

        static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        static Color ContrastFor(Color background)
            => Luminance(background) > 0.55f ? new Color(0.043f, 0.058f, 0.090f, 1f) : new Color(1f, 1f, 1f, 1f);

        static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // ─── RANDOMIZE ──────────────────────────────────────────────────────
        public static ColorMap Randomize(bool? forceLight = null)
        {
            bool isLight = forceLight ?? (UnityEngine.Random.value > 0.5f);
            float brandHue = UnityEngine.Random.value;

            Color primary   = Color.HSVToRGB(brandHue,                       0.7f, 0.85f);
            Color secondary = Color.HSVToRGB((brandHue + 0.55f) % 1f,        0.7f, 0.85f);
            Color tertiary  = Color.HSVToRGB((brandHue + 0.30f) % 1f,        0.7f, 0.85f);
            Color warning   = Color.HSVToRGB(0.10f, 0.85f, 0.95f);
            Color danger    = Color.HSVToRGB(0.00f, 0.75f, 0.85f);

            Color textPrimary, textSecondary, bg, surface, surfaceElev;
            if (isLight)
            {
                bg          = Color.HSVToRGB(brandHue, 0.04f, 0.98f);
                surface     = new Color(1f, 1f, 1f);
                surfaceElev = Color.HSVToRGB(brandHue, 0.04f, 0.94f);
                textPrimary   = new Color(0.06f, 0.09f, 0.16f);
                textSecondary = new Color(0.28f, 0.34f, 0.40f);
            }
            else
            {
                bg          = Color.HSVToRGB(brandHue, 0.40f, 0.08f);
                surface     = Color.HSVToRGB(brandHue, 0.30f, 0.14f);
                surfaceElev = Color.HSVToRGB(brandHue, 0.28f, 0.18f);
                textPrimary   = new Color(0.95f, 0.96f, 0.97f);
                textSecondary = new Color(0.63f, 0.65f, 0.70f);
            }

            return new ColorMap
            {
                Bg             = bg,
                Surface        = surface,
                SurfaceElev    = surfaceElev,
                SurfaceHover   = Mix(surface, textPrimary, 0.08f),
                Border         = Mix(surface, textPrimary, 0.18f),
                BorderStrong   = Mix(surface, textPrimary, 0.32f),
                TextPrimary    = textPrimary,
                TextSecondary  = textSecondary,
                TextDisabled   = Mix(textSecondary, bg, 0.45f),
                TextOnAccent   = ContrastFor(primary),
                Primary        = primary,
                PrimaryHover   = Shade(primary, 0.14f),
                PrimaryPress   = Shade(primary, 0.28f),
                PrimarySoft    = WithAlpha(primary, 0.16f),
                Secondary      = secondary,
                SecondaryHover = Shade(secondary, 0.14f),
                SecondaryPress = Shade(secondary, 0.28f),
                Tertiary       = tertiary,
                TertiaryHover  = Shade(tertiary, 0.14f),
                Warning        = warning,
                WarningHover   = Shade(warning, 0.14f),
                Danger         = danger,
                DangerHover    = Shade(danger, 0.14f),
                DangerPress    = Shade(danger, 0.28f),
                Overlay        = new Color(0f, 0f, 0f, isLight ? 0.5f : 0.6f),
            };
        }

        public static string ToHex(Color c)
        {
            int r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            int g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            int b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
