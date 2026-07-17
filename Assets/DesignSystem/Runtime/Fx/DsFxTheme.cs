#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using DesignSystem.Runtime.Behaviour;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>Flags a material theme carries onto every element it skins.</summary>
    public struct DsFxThemeOptions
    {
        public int Wear;      // 0 none, 1 worn, 2 heavy
        public bool Static;   // freeze idle animation
        public int InStyle;   // 0 build, 1 sweep, 2 fade
        public int OutStyle;
    }

    /// <summary>
    /// Applies ONE material family across an entire design-system tree so the result reads as a
    /// NATIVE material UI — carpentry, not components with a texture.
    ///
    /// This is the class that knows what the design system's own components ARE: that a ds-btn is
    /// a board standing proud, that a ds-input's inner box is a tray carved into the panel, that a
    /// slider's rail is furniture and its dragger is the control. The PAGE stays on its token
    /// background (the page is the wall, the material is the furniture); everything resting on it
    /// sits on the <see cref="DsFxTone"/> ladder. Color is <see cref="DsFxPalette"/>'s job; THIS
    /// class decides who sits on which rung — and enforces:
    ///
    /// THE READABILITY GROUND RULES (no per-component exceptions):
    ///   1. Body text is NEVER material. Every text element gets an ink chosen for the tone it
    ///      actually rides, with a counter-toned shadow — contrast floor 4.5:1.
    ///   2. Titles riding a material surface are ENGRAVED into it (engraving IS the native title
    ///      treatment); solid material lettering is reserved for text on plain ground.
    ///   3. Captions and icons riding a raised control carve/stamp into it, at the control's own
    ///      tone, so the groove floor continues the surface around it.
    ///   4. Input wells are SUNKEN trays. Value text is ink (never carved), the caret and selection
    ///      wear the theme accent (contrast-checked), and keyboard focus draws the accent rim.
    ///   5. Containers are material PANELS — never a bare frame around a flat token middle, which
    ///      is exactly the "half modern, half themed" look this bans.
    ///   6. Tracks and grooves are wells; the moving part rides raised. Knobs and dots stay stock.
    ///   7. Semantic color survives only through chroma-gated adoption on CONTROLS (a danger button
    ///      stays recognizably red) and on STATUS panels. Other panels and wells are neutral — the
    ///      token rainbow on every surface is the other half of the "mix" look.
    ///   8. Token swatches, avatars, skeletons, spinners and loose icons on plain ground stay
    ///      stock: their job is showing the real palette, or animating.
    ///   9. Only real controls answer the pointer. Panels, tracks under moving parts, status fills,
    ///      badges and carved captions are inert: a section is a wall, not a button.
    ///
    /// The mapper only ADDS marker classes you could have authored by hand, then runs
    /// <see cref="DsFx.ApplyMarkers"/>. Call it AFTER the tree is attached and laid out (roles read
    /// resolvedStyle). Hand-authored ds-fx- markers always win: a marked element is skipped,
    /// subtree included.
    ///
    /// COST: every skinned element is one extra draw call. Theming a whole screen is a deliberate
    /// choice, not a free one.
    /// </summary>
    public static class DsFxTheme
    {
        private enum Role { Skip, Heading, Control, Surface, Container, Field, Composite, Well }

        /// <summary>What the children of an element ride on.</summary>
        private enum FillKind
        {
            None,     // plain (or non-material opaque) ground — leave text/icons stock
            Control,  // a raised interactive fill — captions carve, icons stamp
            Panel,    // a material panel — text gets ink, icons get ink tint
        }

        private readonly struct FillCtx
        {
            public readonly FillKind Kind;
            public readonly DsFxTone Tone;
            public readonly DsFxTonePalette Pal;
            public FillCtx(FillKind kind, DsFxTone tone, DsFxTonePalette pal) { Kind = kind; Tone = tone; Pal = pal; }
        }

        private sealed class Ctx
        {
            public DsFxFamily Family;
            public string FamilyClass;
            public DsFxThemeOptions Options;
            public DsFxTonePalette BgPal, SurfacePal, RaisedPal, WellPal;
            public int Themed;

            /// <summary>How to unwind everything this pass wrote, newest last. See <see cref="Revert"/>.</summary>
            public List<Action> Undo = new List<Action>();

            /// <summary>Add a class and remember to take it off.</summary>
            public void Mark(VisualElement el, string cls)
            {
                if (el.ClassListContains(cls))
                    return; // someone else owns it; do not claim the undo
                el.AddToClassList(cls);
                Undo.Add(() => el.RemoveFromClassList(cls));
            }

            /// <summary>
            /// Set an inline style and remember to clear it. Clearing means StyleKeyword.Null —
            /// "fall back to the cascade" — which is right because these properties are ones the
            /// mapper introduces. An element that already had its OWN inline value for the same
            /// property loses it on revert; that is a documented edge, not a silent one.
            /// </summary>
            public void Set(Action apply, Action clear)
            {
                apply();
                Undo.Add(clear);
            }

            /// <summary>A busy surface needs a heavier counter-shadow under body ink; the thin
            /// default drowns in the figure. The family declares this about itself.</summary>
            public bool BusyFigure => Family.HighFigure;

            public DsFxTonePalette PalFor(DsFxTone tone) => tone switch
            {
                DsFxTone.Bg => BgPal,
                DsFxTone.Raised => RaisedPal,
                DsFxTone.Well => WellPal,
                _ => SurfacePal,
            };
        }

        // Never skinned: texture-bearing or runtime-animated elements, and the color swatches whose
        // entire job is showing the SOURCE tokens. (ds-icon is handled by the walk itself: stamped
        // or inked when riding a material fill.)
        private static readonly string[] SkipClasses =
        {
            "ds-avatar", "ds-spinner", "ds-swatch",
            "ds-toggle__knob", "ds-carousel-dot",
            "ds-notif-dot", "ds-drawer__backdrop", "ds-drag-ghost",
        };

        // Explicit ISLANDS: their subtree keeps its own designed colors even though their
        // background is an image or a translucent wash the opaque-bg island rule cannot see.
        private static readonly string[] IslandClasses =
        {
            "ds-notif-wrap",
        };

        // Sunken trays: recessed material wells that hold non-material content. Skeletons read as
        // empty loading trays; scroll demos become opaque viewports. Illustration grounds become
        // recessed PICTURE wells — stock gray line-art washes out on raw figure but pops against a
        // deep tray.
        private static readonly string[] WellClasses =
        {
            "ds-skeleton", "ds-scrollbar-demo",
            "ds-empty__icon-bg", "ds-modal__illustration", "ds-animal-card__image",
            "ds-animal-detail__hero",
        };

        // Titles: carved into the surface they ride (solid only on plain ground).
        private static readonly string[] HeadingClasses =
        {
            "ds-h1", "ds-h2", "ds-h3", "ds-section__title",
        };

        // Interactive controls: raised material; TextElements additionally carve.
        private static readonly string[] ControlClasses =
        {
            "ds-btn", "ds-tab", "ds-chip", "ds-tag", "ds-badge",
            "ds-pagination__btn", "ds-stepper__btn", "ds-view-toggle__btn",
        };

        // Non-text raised surfaces (children carve/stamp like control captions). ds-nav-item IS
        // here: a side nav is a tab strip, and its rows wear the material exactly like horizontal
        // ds-tab buttons. Rail items, bottom-nav items and sheet rows are deliberately NOT: the
        // design system draws those flat — only their active/hover token backgrounds paint — so
        // they stay stock and their icons/labels take the panel ink.
        private static readonly string[] SurfaceClasses =
        {
            "ds-meter__fill", "ds-nav-item",
        };

        // Editable wells: the material becomes a sunken tray.
        private static readonly string[] FieldClasses =
        {
            "ds-input", "ds-textarea", "ds-dropdown", "ds-search",
        };

        // Material panels that hold content. First level rides Surface tone; a container nested
        // inside another panel steps up to Raised, so depth stays legible. ds-tabpanel is
        // deliberately absent: tab content areas are PLAIN in the design system — no background, no
        // border — the strip alone signals the group.
        private static readonly string[] ContainerClasses =
        {
            "ds-section", "ds-card", "ds-animal-card", "ds-animal-detail",
            "ds-modal", "ds-dialog", "ds-drawer", "ds-sheet", "ds-toast",
            "ds-tooltip", "ds-empty", "ds-profile",
            "ds-side-nav", "ds-side-rail", "ds-bottom-nav",
            "ds-drop-zone", "ds-drawer-wrap",
            // Control strips: bordered chrome holding controls. Without these they stay opaque
            // token islands inside material panels — the mix look.
            "ds-tabs", "ds-view-toggle", "ds-stepper", "ds-pagination",
        };

        // Panels that keep their SEMANTIC color: a toast is a status message, and the design system
        // paints it green/blue/amber/red on purpose. The chroma gate still ignores neutral panels,
        // so this stays an exception, not a leak.
        private static readonly string[] AdoptContainerClasses =
        {
            "ds-toast",
        };

        // Composite stock controls: the material goes on the PART that paints, at the part's
        // rightful tone. Grooves are wells; the moving part is raised. An empty selector ("")
        // targets the composite root itself. `inert` marks parts that must never answer the pointer
        // themselves: status displays, and the groove UNDER a moving part (the dragger is the
        // control, its rail is furniture).
        private enum PartRecipe { Fill, Frame }
        private static readonly Dictionary<string, (string cls, PartRecipe recipe, DsFxTone tone, bool adopt, bool inert)[]> CompositeParts = new()
        {
            // Toggle/check/radio boxes ADOPT: their checked state is a token color swap, and the
            // design system shows it. The composite root re-reads adoption on every ChangeEvent, so
            // ON/OFF recolors the material live. Check/radio are FILL wells, not frames: the frame
            // recipe leaves their middles as raw token slabs, and the tick image itself survives
            // the shader's texture passthrough.
            ["ds-toggle"] = new[] { ("unity-toggle__checkmark", PartRecipe.Fill, DsFxTone.Well, true, false) },
            ["ds-check"] = new[] { ("unity-toggle__checkmark", PartRecipe.Fill, DsFxTone.Well, true, false) },
            ["ds-radio"] = new[] { ("unity-radio-button__checkmark-background", PartRecipe.Fill, DsFxTone.Well, true, false) },
            ["ds-slider"] = new[]
            {
                ("unity-base-slider__tracker", PartRecipe.Fill, DsFxTone.Well, false, true),
                ("unity-base-slider__dragger", PartRecipe.Fill, DsFxTone.Raised, true, false),
            },
            ["ds-range"] = new[]
            {
                ("unity-min-max-slider__tracker", PartRecipe.Fill, DsFxTone.Well, false, true),
                ("unity-min-max-slider__dragger", PartRecipe.Fill, DsFxTone.Raised, true, false),
                ("unity-min-max-slider__min-thumb", PartRecipe.Fill, DsFxTone.Raised, true, false),
                ("unity-min-max-slider__max-thumb", PartRecipe.Fill, DsFxTone.Raised, true, false),
            },
            ["ds-progress"] = new[]
            {
                ("unity-progress-bar__background", PartRecipe.Fill, DsFxTone.Well, false, true),
                ("unity-progress-bar__progress", PartRecipe.Fill, DsFxTone.Raised, true, true),
            },
            // The meter bar itself is the groove; ds-meter__fill (a SURFACE class) rides raised +
            // adopt on top of it. Both are status, not controls.
            ["ds-meter"] = new[] { ("", PartRecipe.Fill, DsFxTone.Well, false, true) },
        };

        // The inner element that actually paints a field's well, newest class first (TextField
        // emits several historical class names; dropdowns their own).
        private static readonly string[] FieldSurfaceClasses =
        {
            "unity-text-input", "unity-text-field__input", "unity-base-text-field__input",
            "unity-base-popup-field__input", "unity-popup-field__input", "unity-base-field__input",
        };

        // Text classes that read as secondary (dimmer ink, still above the floor).
        private static readonly string[] SecondaryTextClasses =
        {
            "ds-caption", "ds-swatch__hex",
        };

        /// <summary>
        /// Theme every recognized design-system component under <paramref name="root"/> with the
        /// material, then bring the markers alive. The root itself is left on its default token
        /// background. Returns how many elements were skinned. Idempotent per element: explicit
        /// ds-fx- markers and already-themed elements are left alone.
        ///
        /// Set <see cref="DsFxManager.ActiveTheme"/> and <see cref="DsFxManager.ThemeLight"/>
        /// BEFORE calling.
        /// </summary>
        public static int Apply(VisualElement root, DsFxFamily family, string variantName, DsFxThemeOptions options = default)
        {
            if (root == null || family == null)
                return 0;

            var variant = family.Find(variantName);
            var light = DsFxManager.ThemeLight;
            var theme = DsFxManager.ActiveTheme;
            var ctx = new Ctx
            {
                Family = family,
                FamilyClass = DsFxSpec.Prefix + family.Name
                            + (string.IsNullOrEmpty(variantName) ? "" : "--" + variantName),
                Options = options,
                BgPal = DsFxPalette.Derive(variant, DsFxTone.Bg, light, theme),
                SurfacePal = DsFxPalette.Derive(variant, DsFxTone.Surface, light, theme),
                RaisedPal = DsFxPalette.Derive(variant, DsFxTone.Raised, light, theme),
                WellPal = DsFxPalette.Derive(variant, DsFxTone.Well, light, theme),
            };

            // The PAGE stays on the default token background: the canvas behind sections keeps the
            // theme stylesheet's color, and the material lives on the components — panels resting
            // on a plain page, not wall-to-wall paneling, which buries the depth hierarchy.
            // FillKind.None here also keeps page-level text on its token ink. The bg tone remains
            // available to hand-authored ds-fx-tone--bg markers, just never mapper-applied.
            var rootFill = new FillCtx(FillKind.None, DsFxTone.Bg, ctx.BgPal);
            var count = root.hierarchy.childCount;
            for (var i = 0; i < count; i++)
                Walk(root.hierarchy[i], ctx, rootFill);

            ThemeScrollers(root, ctx);
            DsFx.ApplyMarkers(root);
            WireDragGhostRepair(root, ctx);
            WirePopupSkins(root, ctx);

            // Remember how to put this tree back. Applying twice without reverting stacks two
            // undo sets, and the older one would restore stale values — so the newer pass wins
            // and the older is dropped. Callers who switch materials should Revert first; this
            // just keeps the failure boring.
            Undos.Remove(root);
            Undos.Add(root, ctx.Undo);
            return ctx.Themed;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, List<Action>> Undos =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, List<Action>>();

        /// <summary>
        /// Put a tree back the way <see cref="Apply"/> found it: skins off, markers stripped, inline
        /// styles cleared, callbacks unregistered. Returns false when this root was never themed.
        ///
        /// This exists because a material picker needs it. Apply is not idempotent-with-a-different-
        /// answer — you cannot simply Apply a second family over the first, because the markers of
        /// the first are still on the elements and hand-authored markers win. Revert, then Apply.
        ///
        /// The one edge worth knowing: reverting clears the inline properties the mapper writes
        /// (color, textShadow, backgroundColor, tint, border colors, the scrollbar form) back to the
        /// USS cascade rather than to a remembered previous inline value. If the host had its own
        /// inline value for one of those on a themed element, it does not survive.
        /// </summary>
        public static bool Revert(VisualElement root)
        {
            if (root == null || !Undos.TryGetValue(root, out var undo))
                return false;

            // Skins first: the manipulator's own removal restores unityMaterial and any background
            // it forced, and doing it before the classes come off keeps that path intact.
            DsFx.RemoveAll(root);

            // Newest first — later writes may have layered on earlier ones.
            for (var i = undo.Count - 1; i >= 0; i--)
            {
                try { undo[i](); }
                catch (Exception e) { Debug.LogWarning($"[ds fx] revert step failed: {e.Message}"); }
            }
            Undos.Remove(root);
            return true;
        }

        /// <summary>True when <paramref name="root"/> currently carries a material theme.</summary>
        public static bool IsThemed(VisualElement root)
            => root != null && Undos.TryGetValue(root, out _);

        /// <summary>
        /// Scrollbars keep the design system's MINIMAL slim form. Full materials turn them into
        /// chunky furniture, and a PAINTED track turns into an invisible dark square: a scroller's
        /// slider/tracker can measure 24pt in an arbitrary tree (the 8px slider rule loses the
        /// cascade), so the track color renders as a square-ended slab overflowing the tray's
        /// rounded corners. The canon is an INVISIBLE track with a floating pill thumb — enforced
        /// inline here so it holds in any tree regardless of sheet order. The auto-hide opacity
        /// transition rides the SCROLLER's opacity property, which none of this touches.
        /// </summary>
        private static void ThemeScrollers(VisualElement root, Ctx ctx)
        {
            var thumb = Color.Lerp(ctx.RaisedPal.ColB, ctx.RaisedPal.Ink, 0.20f);
            thumb.a = 0.95f;
            SlimScrollbars(root, thumb, ctx.Undo);
        }

        /// <summary>
        /// Enforce the slim scrollbar form on every Scroller under <paramref name="root"/>:
        /// invisible track, 8pt pill thumb, no arrow buttons. Public because any surface hosting
        /// themed content wants the same treatment for its stock scrollers — the default theme's
        /// 24pt tracker and arrow squares are exactly the artifacts this kills.
        /// </summary>
        /// <param name="undo">Optional: actions that restore each element are appended here, so a
        /// caller (like <see cref="Revert"/>) can put the scrollbars back.</param>
        public static void SlimScrollbars(VisualElement root, Color thumb, List<Action> undo = null)
        {
            root.Query<Scroller>().ForEach(scroller =>
            {
                var vertical = scroller.direction == SliderDirection.Vertical;

                var low = scroller.Q(className: "unity-scroller__low-button");
                if (low != null)
                {
                    low.style.display = DisplayStyle.None;
                    undo?.Add(() => low.style.display = StyleKeyword.Null);
                }
                var high = scroller.Q(className: "unity-scroller__high-button");
                if (high != null)
                {
                    high.style.display = DisplayStyle.None;
                    undo?.Add(() => high.style.display = StyleKeyword.Null);
                }

                var slider = scroller.Q(className: "unity-base-slider");
                if (slider != null)
                {
                    if (vertical) slider.style.width = 8;
                    else slider.style.height = 8;
                    slider.style.marginTop = 0; slider.style.marginBottom = 0;
                    slider.style.marginLeft = 0; slider.style.marginRight = 0;
                    undo?.Add(() =>
                    {
                        slider.style.width = StyleKeyword.Null; slider.style.height = StyleKeyword.Null;
                        slider.style.marginTop = StyleKeyword.Null; slider.style.marginBottom = StyleKeyword.Null;
                        slider.style.marginLeft = StyleKeyword.Null; slider.style.marginRight = StyleKeyword.Null;
                    });
                }

                var tracker = scroller.Q(className: "unity-base-slider__tracker");
                if (tracker != null)
                {
                    tracker.style.backgroundColor = Color.clear;
                    tracker.style.borderTopWidth = 0; tracker.style.borderRightWidth = 0;
                    tracker.style.borderBottomWidth = 0; tracker.style.borderLeftWidth = 0;
                    undo?.Add(() =>
                    {
                        tracker.style.backgroundColor = StyleKeyword.Null;
                        tracker.style.borderTopWidth = StyleKeyword.Null; tracker.style.borderRightWidth = StyleKeyword.Null;
                        tracker.style.borderBottomWidth = StyleKeyword.Null; tracker.style.borderLeftWidth = StyleKeyword.Null;
                    });
                }

                var dragger = scroller.Q(className: "unity-base-slider__dragger");
                if (dragger != null)
                {
                    dragger.style.backgroundColor = thumb;
                    if (vertical) { dragger.style.width = 8; dragger.style.left = 0; }
                    else { dragger.style.height = 8; dragger.style.top = 0; }
                    dragger.style.borderTopLeftRadius = 4; dragger.style.borderTopRightRadius = 4;
                    dragger.style.borderBottomRightRadius = 4; dragger.style.borderBottomLeftRadius = 4;
                    dragger.style.borderTopWidth = 0; dragger.style.borderRightWidth = 0;
                    dragger.style.borderBottomWidth = 0; dragger.style.borderLeftWidth = 0;
                    undo?.Add(() =>
                    {
                        dragger.style.backgroundColor = StyleKeyword.Null;
                        dragger.style.width = StyleKeyword.Null; dragger.style.height = StyleKeyword.Null;
                        dragger.style.left = StyleKeyword.Null; dragger.style.top = StyleKeyword.Null;
                        dragger.style.borderTopLeftRadius = StyleKeyword.Null; dragger.style.borderTopRightRadius = StyleKeyword.Null;
                        dragger.style.borderBottomRightRadius = StyleKeyword.Null; dragger.style.borderBottomLeftRadius = StyleKeyword.Null;
                        dragger.style.borderTopWidth = StyleKeyword.Null; dragger.style.borderRightWidth = StyleKeyword.Null;
                        dragger.style.borderBottomWidth = StyleKeyword.Null; dragger.style.borderLeftWidth = StyleKeyword.Null;
                    });
                }
            });
        }

        /// <summary>
        /// Dropdown popups are parented at PANEL scope — a SIBLING of the themed root — so the walk
        /// above can never reach one, and it opened as a stock token panel inside a material screen
        /// (the "mix" look rule 5 bans). The design-system runtime raises
        /// <see cref="DesignSystemEvents.DropdownPopupOpened"/> after it has placed, sized and
        /// revealed every popup, on BOTH backends (UIDocument and PanelRenderer, screen-space and
        /// world-space panels alike — the event lives outside the generic behaviour base for exactly
        /// that reason), which makes it the one hook that reaches popups in every host.
        ///
        /// Only the visible chrome (`container-outer`) is skinned, as the floating panel it is; the
        /// outer `.unity-base-dropdown` element is a full-panel transparent click-catcher and must
        /// never be painted. The rows inside are descendants of a skinned element, so they take the
        /// passthrough: their hover token highlight keeps working, and item text keeps
        /// DropdownPopup.uss's token ink, which resolves at panel scope in both hosts. Idempotent by
        /// the family-class guard — the runtime warns that popups may be REUSED across opens.
        /// </summary>
        private static void WirePopupSkins(VisualElement root, Ctx ctx)
        {
            Action<DropdownField, VisualElement> onOpened = (dd, menu) =>
            {
                if (dd == null || menu == null || root.panel == null || dd.panel != root.panel)
                    return;
                // Ownership: on a panel with several themed roots (the HUD and the page, say),
                // only the root that actually contains the field skins its popup.
                if (!root.Contains(dd))
                    return;
                var outer = menu.Q(className: "unity-base-dropdown__container-outer");
                if (outer == null || outer.ClassListContains(ctx.FamilyClass))
                    return;
                outer.AddToClassList(ctx.FamilyClass);
                outer.AddToClassList(DsFxSpec.Prefix + "tone--surface");
                outer.AddToClassList(DsFxSpec.Prefix + "inert");
                outer.AddToClassList(DsFxSpec.Prefix + "in--" + AnimStyleName(ctx.Options.InStyle));
                if (ctx.Options.Static)
                    outer.AddToClassList(DsFxSpec.Prefix + "static");
                DsFx.ApplyMarkers(outer);
                var thumb = Color.Lerp(ctx.RaisedPal.ColB, ctx.RaisedPal.Ink, 0.20f);
                thumb.a = 0.95f;
                SlimScrollbars(outer, thumb);
            };
            DesignSystemEvents.DropdownPopupOpened += onOpened;
            ctx.Undo.Add(() =>
            {
                DesignSystemEvents.DropdownPopupOpened -= onOpened;
                // A popup Unity chose to REUSE would keep its skin past a Revert (it lives at
                // panel scope, outside the swept subtree). Strip any live one now.
                var panelRoot = root.panel?.visualTree;
                panelRoot?.Query(className: "unity-base-dropdown__container-outer").ForEach(outer =>
                {
                    if (!outer.ClassListContains(ctx.FamilyClass))
                        return;
                    DsFx.RemoveAll(outer);
                    foreach (var cls in new List<string>(outer.GetClasses()))
                        if (cls.StartsWith(DsFxSpec.Prefix, StringComparison.Ordinal))
                            outer.RemoveFromClassList(cls);
                });
            });
        }

        /// <summary>
        /// Drag ghosts are built AT DRAG TIME by the runtime as fresh elements copying the dragged
        /// item's classes — ds-fx- markers and the wired flag included — but never its skin, so
        /// ghosts render as stock chips. Any pointer-down can start a drag; one tick later, rewire
        /// whatever ghost appeared so it keeps its material while it follows the pointer.
        /// </summary>
        private static void WireDragGhostRepair(VisualElement root, Ctx ctx)
        {
            // Ticker-deferred (not root.schedule): drags happen on world panels too, and a
            // world panel does not tick its scheduler in a player (see DsFxManager.RunAfter).
            EventCallback<PointerDownEvent> cb = _ => DsFxManager.RunAfter(0f, () =>
                root.Query(className: "ds-drag-ghost").ForEach(ghost =>
                {
                    if (DsFx.SkinOf(ghost) != null)
                        return;
                    DsFx.Rewire(ghost);
                    // A ghost exists MID-INTERACTION: it must be fully inked the frame it
                    // appears. Playing the plot-in entrance on it is wrong (the user is
                    // already holding the thing), and the entrance is also the one
                    // mechanism whose clock edge can leave an element clipped invisible
                    // when a host's frame pacing disagrees with the stamp — a drag ghost
                    // is the worst place to gamble on that. Park every skin under the
                    // ghost at IDLE, fully built.
                    var idle = new Vector4(1f, 0f, -1000f, 1f);
                    DsFx.SkinOf(ghost)?.ForceStates(life: idle);
                    ghost.Query<VisualElement>().ForEach(el => DsFx.SkinOf(el)?.ForceStates(life: idle));
                }));
            root.RegisterCallback(cb, TrickleDown.TrickleDown);
            ctx.Undo.Add(() => root.UnregisterCallback(cb, TrickleDown.TrickleDown));
        }

        /// <summary>Rule 1: ink plus a counter-toned shadow so text survives busy materials. A
        /// high-figure family gets a THICK opposite-color halo — the thin default drowns against
        /// cathedral grain.</summary>
        private static void ApplyInk(VisualElement el, Ctx ctx, Color ink, bool strong = false)
        {
            var dark = 0.299f * ink.r + 0.587f * ink.g + 0.114f * ink.b < 0.5f;
            var shadow = new TextShadow
            {
                offset = new Vector2(0f, strong ? 1.4f : 1f),
                blurRadius = strong ? 4f : 2f,
                color = dark
                    ? new Color(1f, 1f, 1f, strong ? 0.60f : 0.35f)
                    : new Color(0f, 0f, 0f, strong ? 0.90f : 0.6f),
            };
            ctx.Set(
                () => { el.style.color = ink; el.style.textShadow = shadow; },
                () => { el.style.color = StyleKeyword.Null; el.style.textShadow = StyleKeyword.Null; });
        }

        private static bool IsSecondaryText(VisualElement el)
        {
            foreach (var cls in SecondaryTextClasses)
                if (el.ClassListContains(cls))
                    return true;
            return false;
        }

        private static void Walk(VisualElement el, Ctx ctx, FillCtx fill)
        {
            var role = Classify(el, out var explicitlyMarked);

            // Hand-authored markers (or an earlier pass) win — and their subtree keeps its own
            // story: we neither re-mark nor re-ink below them.
            if (explicitlyMarked)
                return;

            var childFill = fill;
            switch (role)
            {
                case Role.Heading:
                    MarkHeading(el, ctx, fill);
                    childFill = new FillCtx(FillKind.None, fill.Tone, fill.Pal);
                    break;

                case Role.Control:
                    // Badges and tags are labels wearing control chrome — status, not buttons; they
                    // must not answer the pointer.
                    MarkRole(el, ctx, "tone--raised", adopt: true,
                        carve: el is TextElement text && !string.IsNullOrEmpty(text.text) && !el.ClassListContains("ds-btn--icon"),
                        inert: el.ClassListContains("ds-badge") || el.ClassListContains("ds-tag"));
                    childFill = new FillCtx(FillKind.Control, DsFxTone.Raised, ctx.RaisedPal);
                    break;

                case Role.Surface:
                    // Meter fills are status displays; nav rows are real interaction targets.
                    MarkRole(el, ctx, "tone--raised", adopt: true, carve: false,
                        inert: el.ClassListContains("ds-meter__fill"));
                    childFill = new FillCtx(FillKind.Control, DsFxTone.Raised, ctx.RaisedPal);
                    break;

                case Role.Container:
                {
                    // Rule 5 + the ladder: first-level panels ride Surface; a panel nested inside
                    // another panel steps up to Raised so it still reads as its own board. The
                    // routed edge is shader-side (sd bands) — no forced border geometry, so
                    // containers that are borderless in the design system stay borderless.
                    var tone = fill.Kind == FillKind.Panel && fill.Tone != DsFxTone.Bg
                        ? DsFxTone.Raised : DsFxTone.Surface;
                    var adoptPanel = false;
                    foreach (var cls in AdoptContainerClasses)
                        if (el.ClassListContains(cls)) { adoptPanel = true; break; }
                    MarkRole(el, ctx, tone == DsFxTone.Raised ? "tone--raised" : "tone--surface",
                        adopt: adoptPanel, carve: false, inert: true); // panels are furniture
                    childFill = new FillCtx(FillKind.Panel, tone, ctx.PalFor(tone));
                    break;
                }

                case Role.Well:
                    // Sunken trays holding non-material content. The skeleton's shimmer child stays
                    // stock but gets a brighter wash inline — 4% white vanishes on textured wells.
                    // Translucent grounds get opaque geometry: the material rides the element's own
                    // bg quads, and a 0.1-alpha quad renders a 0.1-alpha tray.
                    MarkRole(el, ctx, "tone--well", adopt: false, carve: false, inert: true);
                    if (el.resolvedStyle.backgroundColor.a < 0.9f)
                        ctx.Set(() => el.style.backgroundColor = Color.white,
                                () => el.style.backgroundColor = StyleKeyword.Null);
                    var shimmer = el.Q(className: "ds-skeleton__shimmer");
                    if (shimmer != null)
                    {
                        // A full-bleed solid child shares the tray's UV density, so the shader's
                        // own-geometry test cannot exclude it — ride the TEXTURE passthrough
                        // instead: a white texture tinted to a soft sweep.
                        ctx.Set(
                            () =>
                            {
                                shimmer.style.backgroundImage = Background.FromTexture2D(Texture2D.whiteTexture);
                                shimmer.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0.14f);
                                shimmer.style.backgroundColor = Color.clear;
                            },
                            () =>
                            {
                                shimmer.style.backgroundImage = StyleKeyword.Null;
                                shimmer.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                                shimmer.style.backgroundColor = StyleKeyword.Null;
                            });
                    }
                    childFill = new FillCtx(FillKind.None, DsFxTone.Well, ctx.WellPal);
                    break;

                case Role.Field:
                    MarkField(el, ctx);
                    childFill = new FillCtx(FillKind.None, fill.Tone, fill.Pal);
                    break;

                case Role.Composite:
                    MarkComposite(el, ctx);
                    // The composite root stays transparent: its loose labels still ride whatever
                    // panel the row sits on.
                    break;

                case Role.Skip:
                    childFill = SkipChildFill(el, ctx, fill);
                    break;
            }

            var count = el.hierarchy.childCount;
            for (var i = 0; i < count; i++)
                Walk(el.hierarchy[i], ctx, childFill);
        }

        /// <summary>
        /// Unrecognized elements still carry the readability rules: text and icons riding a material
        /// fill get carve/ink treatment, and an opaque non-material background turns its subtree
        /// into a plain-ground island (its own chrome keeps its own text colors).
        /// </summary>
        private static FillCtx SkipChildFill(VisualElement el, Ctx ctx, FillCtx fill)
        {
            if (fill.Kind == FillKind.None)
                return fill;

            // Explicit islands first: badge stacks and friends keep their own colors.
            foreach (var cls in IslandClasses)
                if (el.ClassListContains(cls))
                    return new FillCtx(FillKind.None, fill.Tone, fill.Pal);

            // Spinner: stock geometry and rotation, recolored for the material ground — the token
            // pair (accent arc on surface-elev track) sinks into a material panel.
            if (el.ClassListContains("ds-spinner"))
            {
                var track = fill.Pal.ColA * 0.5f;
                track.a = 1f;
                ctx.Set(
                    () =>
                    {
                        el.style.borderTopColor = fill.Pal.Accent;
                        el.style.borderRightColor = track;
                        el.style.borderBottomColor = track;
                        el.style.borderLeftColor = track;
                    },
                    () =>
                    {
                        el.style.borderTopColor = StyleKeyword.Null;
                        el.style.borderRightColor = StyleKeyword.Null;
                        el.style.borderBottomColor = StyleKeyword.Null;
                        el.style.borderLeftColor = StyleKeyword.Null;
                    });
                return new FillCtx(FillKind.None, fill.Tone, fill.Pal);
            }

            // Icons: stamped into controls (rule 3); on panels, only NEUTRAL glyphs take the panel
            // ink. A colorful token tint (accent, gold, danger, rarity, toast status) is
            // information — inking it uniform is what makes toasts and badges unreadable.
            // Chroma-gated like adoption.
            if (el.ClassListContains("ds-icon"))
            {
                if (fill.Kind == FillKind.Control)
                {
                    ctx.Mark(el, ctx.FamilyClass);
                    ctx.Mark(el, DsFxSpec.Prefix + "text-carve");
                    ctx.Mark(el, ToneClass(fill.Tone));
                    ctx.Mark(el, DsFxSpec.Prefix + "inert"); // the control behind it owns the interaction
                    AddSharedFlags(el, ctx);
                    ctx.Themed++;
                }
                else
                {
                    var cur = el.resolvedStyle.unityBackgroundImageTintColor;
                    var chroma = Mathf.Max(cur.r, Mathf.Max(cur.g, cur.b))
                               - Mathf.Min(cur.r, Mathf.Min(cur.g, cur.b));
                    if (chroma < 0.10f)
                    {
                        var tint = fill.Pal.Ink;
                        tint.a = 0.92f;
                        ctx.Set(() => el.style.unityBackgroundImageTintColor = tint,
                                () => el.style.unityBackgroundImageTintColor = StyleKeyword.Null);
                    }
                }
                return new FillCtx(FillKind.None, fill.Tone, fill.Pal);
            }

            // Text: carve on controls (rule 3), ink on panels (rule 1).
            if (el is TextElement te && !string.IsNullOrEmpty(te.text))
            {
                if (fill.Kind == FillKind.Control)
                {
                    ctx.Mark(el, ctx.FamilyClass);
                    ctx.Mark(el, DsFxSpec.Prefix + "text-carve");
                    ctx.Mark(el, DsFxSpec.Prefix + "frame"); // its own quad stays a passthrough — no slab under the label
                    ctx.Mark(el, ToneClass(fill.Tone));
                    ctx.Mark(el, DsFxSpec.Prefix + "inert"); // the control behind it owns the interaction
                    AddSharedFlags(el, ctx);
                    ctx.Themed++;
                }
                else
                {
                    ApplyInk(te, ctx, IsSecondaryText(te) ? fill.Pal.InkSecondary : fill.Pal.Ink, ctx.BusyFigure);
                }
                return fill;
            }

            // An opaque background OR a background image that is NOT ours = a modern island
            // (promo chrome, illustrations, gradient art). Its subtree keeps its own designed colors.
            var bg = el.resolvedStyle.backgroundColor;
            var bgImage = el.resolvedStyle.backgroundImage;
            if (bg.a > 0.5f || bgImage.texture != null || bgImage.vectorImage != null
                || bgImage.sprite != null || bgImage.renderTexture != null)
                return new FillCtx(FillKind.None, fill.Tone, fill.Pal);

            return fill;
        }

        private static Role Classify(VisualElement el, out bool explicitlyMarked)
        {
            explicitlyMarked = false;
            var hasDs = false;
            foreach (var cls in el.GetClasses())
            {
                // ORDER IS LOAD-BEARING. Every ds-fx- marker also starts with "ds-", so the marker
                // test MUST come first. Reversed, a marked element reads as a plain design-system
                // component, the mapper marks it a second time on top of its own story, and
                // hand-authored intent is silently overwritten.
                if (cls.StartsWith(DsFxSpec.Prefix, StringComparison.Ordinal) || cls == DsFx.WiredClass)
                {
                    explicitlyMarked = true;
                    return Role.Skip;
                }
                if (cls.StartsWith("ds-", StringComparison.Ordinal))
                    hasDs = true;
            }

            if (!hasDs)
                return Role.Skip;

            foreach (var cls in SkipClasses)
                if (el.ClassListContains(cls)) return Role.Skip;
            foreach (var cls in WellClasses)
                if (el.ClassListContains(cls)) return Role.Well;
            foreach (var cls in HeadingClasses)
                if (el.ClassListContains(cls)) return Role.Heading;
            foreach (var cls in ControlClasses)
                if (el.ClassListContains(cls)) return Role.Control;
            foreach (var cls in SurfaceClasses)
                if (el.ClassListContains(cls)) return Role.Surface;
            foreach (var cls in FieldClasses)
                if (el.ClassListContains(cls)) return Role.Field;
            foreach (var kv in CompositeParts)
                if (el.ClassListContains(kv.Key)) return Role.Composite;
            foreach (var cls in ContainerClasses)
                if (el.ClassListContains(cls)) return Role.Container;

            return Role.Skip;
        }

        private static string ToneClass(DsFxTone tone) => tone switch
        {
            DsFxTone.Bg => DsFxSpec.Prefix + "tone--bg",
            DsFxTone.Raised => DsFxSpec.Prefix + "tone--raised",
            DsFxTone.Well => DsFxSpec.Prefix + "tone--well",
            _ => DsFxSpec.Prefix + "tone--surface",
        };

        /// <param name="toneSuffix">The marker body after the ds-fx- prefix, e.g. "tone--raised".</param>
        private static void MarkRole(VisualElement el, Ctx ctx, string toneSuffix, bool adopt, bool carve, bool inert = false)
        {
            ctx.Mark(el, ctx.FamilyClass);
            ctx.Mark(el, DsFxSpec.Prefix + toneSuffix);
            if (carve)
                ctx.Mark(el, DsFxSpec.Prefix + "text-carve");
            if (adopt)
                ctx.Mark(el, DsFxSpec.Prefix + "adopt");
            if (inert)
                ctx.Mark(el, DsFxSpec.Prefix + "inert");
            AddSharedFlags(el, ctx);
            ctx.Themed++;
        }

        /// <summary>
        /// Rule 2: a title riding material carves into it, AT the tone of the surface it rides — the
        /// world-anchored pattern makes the groove floor continue the panel figure seamlessly. On
        /// plain ground it stays solid material lettering.
        /// </summary>
        private static void MarkHeading(VisualElement el, Ctx ctx, FillCtx fill)
        {
            ctx.Mark(el, ctx.FamilyClass);
            if (fill.Kind == FillKind.None)
            {
                ctx.Mark(el, DsFxSpec.Prefix + "text-solid");
            }
            else
            {
                ctx.Mark(el, DsFxSpec.Prefix + "text-carve");
                ctx.Mark(el, DsFxSpec.Prefix + "frame"); // no slab under the title
                ctx.Mark(el, ToneClass(fill.Tone));
            }
            ctx.Mark(el, DsFxSpec.Prefix + "inert"); // titles are furniture, not controls
            AddSharedFlags(el, ctx);
            ctx.Themed++;
        }

        /// <summary>
        /// Rule 4. Fields materialize the element that PAINTS the well (ds-search draws its own
        /// chrome; input/textarea/dropdown draw on an inner unity input box) as a sunken tray. Value
        /// text is ink with a counter-shadow, the caret and selection wear the contrast-checked
        /// accent, and focus lights the accent rim via the skin's focus wiring.
        /// </summary>
        private static void MarkField(VisualElement el, Ctx ctx)
        {
            var surface = el.ClassListContains("ds-search") ? el : null;
            if (surface == null)
            {
                foreach (var cls in FieldSurfaceClasses)
                {
                    surface = el.Q(className: cls);
                    if (surface != null)
                        break;
                }
            }
            if (surface == null)
                return;

            ctx.Mark(surface, ctx.FamilyClass);
            ctx.Mark(surface, DsFxSpec.Prefix + "tone--well");
            AddSharedFlags(surface, ctx);
            ctx.Themed++;

            // Ink rides the well (color inherits to value text); the inner input box gets it too
            // when it is a distinct element.
            var pal = ctx.WellPal;
            ApplyInk(surface, ctx, pal.Ink, ctx.BusyFigure);
            var innerText = el.Q(className: "unity-text-input");
            if (innerText != null && innerText != surface)
                ApplyInk(innerText, ctx, pal.Ink, ctx.BusyFigure);

            // Caret + selection: the theme accent, never the invisible default.
            var tf = el as TextField ?? el.Q<TextField>();
            if (tf != null)
            {
                // textSelection has no "unset" — put back what was there.
                var prevCaret = tf.textSelection.cursorColor;
                var prevSel = tf.textSelection.selectionColor;
                var sel = pal.Accent;
                sel.a = 0.35f;
                ctx.Set(
                    () => { tf.textSelection.cursorColor = pal.Caret; tf.textSelection.selectionColor = sel; },
                    () => { tf.textSelection.cursorColor = prevCaret; tf.textSelection.selectionColor = prevSel; });
            }

            // The dropdown chevron is a VectorImage background — stamp it.
            StampFieldIcon(el.Q(className: "unity-base-popup-field__arrow"), ctx);
            // The search glyph likewise (its field stops the walk, so stamp it here).
            StampFieldIcon(el.Q(className: "ds-search__icon"), ctx);
        }

        private static void StampFieldIcon(VisualElement icon, Ctx ctx)
        {
            if (icon == null || icon.ClassListContains(ctx.FamilyClass))
                return;
            ctx.Mark(icon, ctx.FamilyClass);
            ctx.Mark(icon, DsFxSpec.Prefix + "text-carve");
            ctx.Mark(icon, DsFxSpec.Prefix + "tone--well");
            ctx.Mark(icon, DsFxSpec.Prefix + "inert"); // the field owns the interaction
            if (!icon.ClassListContains("ds-icon"))
                ctx.Mark(icon, "ds-icon"); // vector-icon promotion path in DsFxSpec
            AddSharedFlags(icon, ctx);
            ctx.Themed++;
        }

        /// <summary>Rule 6: grooves are wells, the moving part is raised, knobs stay stock. Boolean
        /// composites re-read their adoption on every value change: checked state is a token color
        /// swap, and the material must follow it (the USS color transition settles before the
        /// ~380ms re-read).</summary>
        private static void MarkComposite(VisualElement el, Ctx ctx)
        {
            foreach (var kv in CompositeParts)
            {
                if (!el.ClassListContains(kv.Key))
                    continue;
                var adoptedParts = new List<VisualElement>();
                foreach (var (cls, recipe, tone, adopt, inert) in kv.Value)
                {
                    var part = cls.Length == 0 ? el : el.Q(className: cls);
                    if (part == null || part.ClassListContains(ctx.FamilyClass))
                        continue;
                    ctx.Mark(part, ctx.FamilyClass);
                    ctx.Mark(part, ToneClass(tone));
                    if (recipe == PartRecipe.Frame)
                        ctx.Mark(part, DsFxSpec.Prefix + "frame");
                    if (adopt)
                    {
                        ctx.Mark(part, DsFxSpec.Prefix + "adopt");
                        adoptedParts.Add(part);
                    }
                    if (inert)
                        ctx.Mark(part, DsFxSpec.Prefix + "inert");
                    AddSharedFlags(part, ctx);
                    ctx.Themed++;
                }
                if (adoptedParts.Count > 0)
                    el.RegisterCallback<ChangeEvent<bool>>(_ =>
                    {
                        foreach (var part in adoptedParts)
                            DsFx.SkinOf(part)?.RefreshAdopt();
                    });
                return;
            }
        }

        private static void AddSharedFlags(VisualElement el, Ctx ctx)
        {
            var options = ctx.Options;
            if (options.Wear == 1) ctx.Mark(el, DsFxSpec.Prefix + "worn");
            if (options.Wear == 2) ctx.Mark(el, DsFxSpec.Prefix + "worn--heavy");
            if (options.Static) ctx.Mark(el, DsFxSpec.Prefix + "static");
            ctx.Mark(el, DsFxSpec.Prefix + "in--" + AnimStyleName(options.InStyle));
            ctx.Mark(el, DsFxSpec.Prefix + "out--" + AnimStyleName(options.OutStyle));
        }

        private static string AnimStyleName(int style) => style switch { 1 => "sweep", 2 => "fade", _ => "build" };
    }
}
#endif
