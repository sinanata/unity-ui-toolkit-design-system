#if UNITY_6000_5_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// Attaches a material skin to one element and then gets out of the way.
    ///
    /// The manipulator's only job is to translate element facts into uniforms: geometry (size,
    /// corner radii, border widths) whenever layout changes, and interaction states as
    /// (from, to, t0, duration) tuples stamped at event time. The shader does ALL animation from
    /// those tuples plus the global clock, so a hovering, rippling, animating element costs the
    /// CPU nothing between events.
    ///
    /// The element's own rendering is the canvas: its background quads, border geometry and glyph
    /// SDFs all flow through the family shader via <c>style.unityMaterial</c>. Nothing is proxied,
    /// so text stays live (change a Button's text and the engraving follows) and layout, picking
    /// and accessibility are untouched.
    ///
    /// LOAD-BEARING: every push builds a FRESH MaterialDefinition. This is not style — it is
    /// correctness. The inline-style setter skips application when the incoming definition
    /// compares EQUAL to the stored one, and the stored one holds a REFERENCE to the definition's
    /// property list. Mutate one shared list and every later push compares the list against
    /// itself — always equal, never applied — and the element freezes at whatever the first push
    /// said. (Which is how an entire screen once rendered at entrance-phase zero: invisible.) A
    /// fresh definition per push makes the comparison old-values vs new-values, which is the one
    /// that means anything.
    /// </summary>
    public sealed class DsFxSkin : Manipulator
    {
        private readonly DsFxSpec _spec;
        private Material _material;
        private bool _hasMaterial;
        private bool _forcedBackground;

        // The full uniform state, kept C#-side; Push() serializes it into a new definition.
        private Vector4 _rect = new Vector4(1, 1, 1, 0);
        private Vector4 _radii;
        private Vector4 _border;
        private Vector4 _hover = new Vector4(0, 0, -1000, 0.001f);
        private Vector4 _press = new Vector4(0, 0, -1000, 0.001f);
        private Vector4 _life = new Vector4(1, 0, -1000, 1);
        private Vector4 _click = NoClick;
        private Color _adoptSource = Color.clear; // element bg captured before any forcing
        private Vector2 _anchor;                  // pattern-domain offset: a hash of world position
        private bool _focused;                    // keyboard focus (drives the accent rim)
        private bool _pointerOver;
        private DsFxTonePalette _tonePal;         // resolved ladder colors when Tone != None

        private static readonly Vector4 NoClick = new Vector4(0.5f, 0.5f, -1000f, 0f);

        private static float Hash01(float seed)
        {
            var s = Mathf.Sin(seed) * 43758.5453f;
            return s - Mathf.Floor(s);
        }

        public DsFxSpec Spec => _spec;

        public DsFxSkin(DsFxSpec spec) => _spec = spec;

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<AttachToPanelEvent>(OnAttach);
            target.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            // Adoption LIVENESS: a state swap recolors the element's token background through a
            // USS transition (tabs, nav items and rail items all declare one), and an adopting
            // skin must re-read its source when that settles — inert or not (a meter fill is
            // inert and still changes color). This is not an interaction callback, so it does
            // not breach the inert guarantee below. Elements pinned by a forced inline
            // background never fire it (inline wins the cascade, so the computed value never
            // transitions) — which is exactly why the pointer-up path also refreshes.
            if (_spec.AdoptColor)
                target.RegisterCallback<TransitionEndEvent>(OnTransitionEnd);
            // Inert skins are DECORATION: page boards, panels, tracks, carved captions. Registering
            // NO interaction callbacks is what guarantees a section can never hover-glow or thud
            // like a button.
            if (!_spec.Inert)
            {
                target.RegisterCallback<PointerEnterEvent>(OnEnter);
                target.RegisterCallback<PointerLeaveEvent>(OnLeave);
                target.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
                target.RegisterCallback<FocusInEvent>(OnFocusIn);
                target.RegisterCallback<FocusOutEvent>(OnFocusOut);
            }
            if (target.panel != null)
                Begin();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            target.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            target.UnregisterCallback<TransitionEndEvent>(OnTransitionEnd);
            target.UnregisterCallback<PointerEnterEvent>(OnEnter);
            target.UnregisterCallback<PointerLeaveEvent>(OnLeave);
            target.UnregisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            target.UnregisterCallback<PointerUpEvent>(OnUp);
            target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            target.UnregisterCallback<FocusInEvent>(OnFocusIn);
            target.UnregisterCallback<FocusOutEvent>(OnFocusOut);
            if (_hasMaterial)
            {
                target.style.unityMaterial = StyleKeyword.Null;
                if (_forcedBackground)
                    target.style.backgroundColor = StyleKeyword.Null;
                _hasMaterial = false;
            }
        }

        private void OnAttach(AttachToPanelEvent _)
        {
            // A re-host (screen ↔ world panel) re-raises this event on an element that is already
            // skinned. Everything panel-dependent must be re-evaluated: the world-space gate, and
            // the signed pixels-per-point ReadGeometry stamps. On a gated host the material comes
            // OFF — leaving it attached is exactly the WebGL crash the gate exists to prevent.
            if (_hasMaterial)
            {
                if (!DsFxManager.AllowWorldSpacePanels && IsWorldSpace(target.panel))
                {
                    target.style.unityMaterial = StyleKeyword.Null;
                    if (_forcedBackground)
                    {
                        target.style.backgroundColor = StyleKeyword.Null;
                        _forcedBackground = false;
                    }
                    _hasMaterial = false;
                    return;
                }
                ReadGeometry();
                Push();
                return;
            }
            Begin();
        }

        private static bool _warnedWorldSpace;

        /// <summary>Prepare the skin and start the IN animation.</summary>
        private void Begin()
        {
            if (_hasMaterial)
                return;

            // World-space panels are opted out ONLY where the engine bug lives (a WebGL player,
            // by default) — see DsFxManager.AllowWorldSpacePanels. Bail BEFORE touching
            // unityMaterial: a half-applied material is worse than none, and the element renders
            // as stock instead.
            DsFxManager.Diag($"skin begin: '{target.GetType().Name}' world={IsWorldSpace(target.panel)} " +
                             $"allow={DsFxManager.AllowWorldSpacePanels} panel={(target.panel != null)}");

            if (!DsFxManager.AllowWorldSpacePanels && IsWorldSpace(target.panel))
            {
                if (!_warnedWorldSpace)
                {
                    _warnedWorldSpace = true;
                    Debug.LogWarning(
                        "[ds fx] Material skins are skipped on WORLD-SPACE panels in this player: Unity 6000.5 " +
                        "overruns its own UI geometry buffer there in a WebGL player (GfxDevice::CopyBufferRanges " +
                        "out of bounds). Those elements render as stock design system. Set " +
                        "DsFxManager.AllowWorldSpacePanels = true to opt in anyway. Screen-space panels are " +
                        "unaffected, and world-space panels are on by default in every other host.");
                }
                return;
            }

            _material = DsFxManager.MaterialFor(_spec.Family);
            if (_material == null)
                return;

            // Tone-marked skins render the ladder palette (variant colors shifted to their level,
            // aligned to the active theme) instead of the raw variant. Resolved once — a theme
            // switch rebuilds the tree anyway.
            if (_spec.Tone != DsFxTone.None)
                _tonePal = DsFxPalette.Derive(_spec.Variant, _spec.Tone,
                    DsFxManager.ThemeLight, DsFxManager.ActiveTheme);

            // Color adoption reads the element's OWN resolved background — the token that used to
            // paint it — and blends its hue into the material palette, so a danger button stays
            // recognizably red WOOD.
            //
            // USS transitions animate background-color IN on attach (ds-btn declares one), so a
            // read at Begin time catches the transition's transparent first frame. Adoption
            // therefore re-reads once the transition has certainly settled — and the
            // transparent-bg forcing below must WAIT for that same moment, or it would paint
            // inline white over the token and hide it forever.
            if (_spec.AdoptColor)
            {
                _adoptSource = target.resolvedStyle.backgroundColor;
                // Deferred through the FX ticker, NOT target.schedule: a world-space panel does
                // not tick its per-panel scheduler in a player, and this re-read silently never
                // ran there (see DsFxManager.RunAfter).
                DsFxManager.RunAfter(0.36f, () =>
                {
                    if (!_hasMaterial)
                        return;
                    _adoptSource = target.resolvedStyle.backgroundColor;
                    if (_spec.FillMode == 0 && _adoptSource.a <= 0.001f && !_forcedBackground)
                    {
                        // Genuinely transparent (a ghost button): give the fill its geometry now
                        // that we know no token color is coming.
                        target.style.backgroundColor = Color.white;
                        _forcedBackground = true;
                    }
                    Push();
                });
            }

            // Material fills need solid geometry to shade; a transparent background emits none.
            // Text-only mode conversely must not paint a slab behind the letters. Inline styles
            // keep both promises without touching USS.
            if (_spec.FillMode == 2)
            {
                target.style.backgroundColor = Color.clear;
                _forcedBackground = true;
            }
            else if (_spec.FillMode == 0 && !_spec.AdoptColor && target.resolvedStyle.backgroundColor.a <= 0.001f)
            {
                // Frame mode does NOT get this treatment: its fill passes through untouched, and a
                // frame around a transparent middle is a perfectly good (and common) look.
                // Adopting skins defer this decision to their settled re-read above.
                //
                // Tone skins force a deep underlay of their OWN palette rather than white:
                // a translucent family shows the forced color through itself, and a white-backed
                // page is exactly the wrong read.
                if (_spec.Tone != DsFxTone.None)
                {
                    var u = _tonePal.ColA;
                    target.style.backgroundColor = new Color(u.r * 0.45f, u.g * 0.45f, u.b * 0.45f, 1f);
                }
                else
                {
                    target.style.backgroundColor = Color.white;
                }
                _forcedBackground = true;
            }

            _life = new Vector4(0f, _spec.InStyle, DsFxManager.Now, _spec.Family.InDuration);
            ReadGeometry();
            _hasMaterial = true;
            Push();
            DsFxManager.Diag($"skin attached: material pushed, rect={_rect}");
        }

        /// <summary>
        /// Is this element hosted by a WORLD-SPACE panel — one drawn in 3D rather than over the
        /// screen? The shader has to know, because a world panel breaks the assumption that one
        /// layout point equals <c>scaledPixelsPerPoint</c> screen pixels: the real ratio depends on
        /// where the camera is standing. See dsfx_ownGeometry.
        /// </summary>
        private static bool IsWorldSpace(IPanel panel)
        {
            var settings = (panel as IRuntimePanel)?.panelSettings;
            return settings != null && settings.renderMode == PanelRenderMode.WorldSpace;
        }

        private void ReadGeometry()
        {
            var rs = target.resolvedStyle;
            var w = float.IsNaN(rs.width) ? 0f : rs.width;
            var h = float.IsNaN(rs.height) ? 0f : rs.height;
            var ppp = target.panel?.scaledPixelsPerPoint ?? 1f;
            // The SIGN of ppp tells the shader which host it is drawing into: negative = world
            // space, where one layout point is NOT ppp screen pixels and anything a family scales
            // by it must stop trusting it. The foundation itself no longer reads the sign —
            // dsfx_ownGeometry measures the emitting rect from the geometry, so it holds at any
            // camera distance — but the flag stays for families tuning screen-pixel effects.
            if (IsWorldSpace(target.panel))
                ppp = -ppp;
            // _FxRect.w = structural texture gain. Panels stay VOICED for families whose figure is
            // carried by the palette, but an additive-pattern family fights body text at that
            // level and declares a lower PanelTextureGain. Nested panels ride Raised tone but are
            // INERT furniture — they damp like panels, or the ladder's second storey glows at full
            // control voice behind text.
            var panelLike = _spec.Tone == DsFxTone.Surface
                         || (_spec.Tone == DsFxTone.Raised && _spec.Inert);
            _rect = new Vector4(Mathf.Max(w, 1f), Mathf.Max(h, 1f), ppp,
                panelLike ? _spec.Family.PanelTextureGain : 0f);
            _radii = new Vector4(
                rs.borderTopLeftRadius, rs.borderTopRightRadius,
                rs.borderBottomRightRadius, rs.borderBottomLeftRadius);
            _border = new Vector4(
                rs.borderTopWidth, rs.borderRightWidth,
                rs.borderBottomWidth, rs.borderLeftWidth);

            // Pattern anchor: a stable HASH of the element's world position. Each element becomes
            // its own cut of stock — decorrelated figure — instead of every element restarting the
            // pattern at its own corner (identical twins) or windowing one continuous sheet (reads
            // as a single printed system). Same layout → same anchor, so a frozen clock stays
            // byte-deterministic.
            var wb = target.worldBound.position;
            if (!float.IsNaN(wb.x) && !float.IsNaN(wb.y))
                _anchor = new Vector2(Hash01(wb.x * 12.9898f + wb.y * 78.233f),
                                      Hash01(wb.x * 39.3468f + wb.y * 11.135f)) * 2048f;
        }

        /// <summary>
        /// Blend the hue of the element's own (token-driven) color into a material color, keeping
        /// the material's value so its texture stays readable. The chroma gate means near-neutral
        /// surfaces keep the pure material — only genuinely colorful semantics tint it.
        /// </summary>
        private static Color Adopt(Color mat, Color source, float amount)
            => DsFxPalette.GatedHueTransfer(mat, source, amount);

        /// <summary>
        /// Disabled controls go ASHEN: the material keeps its figure but loses most of its chroma
        /// and a little light. Applied at push time from enabledInHierarchy.
        /// </summary>
        private static Color Mute(Color c)
        {
            var l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            var ash = Color.Lerp(c, new Color(l, l, l, c.a), 0.62f);
            return Color.Lerp(ash, new Color(0.42f, 0.40f, 0.38f, c.a), 0.14f) * new Color(0.92f, 0.92f, 0.92f, 1f);
        }

        /// <summary>Serialize the current state into a fresh definition and apply it.</summary>
        private void Push()
        {
            if (!_hasMaterial)
                return;
            var def = MaterialDefinition.FromMaterial(_material);
            var v = _spec.Variant;
            var toned = _spec.Tone != DsFxTone.None;
            var colA = toned ? _tonePal.ColA : v.ColA;
            var colB = toned ? _tonePal.ColB : v.ColB;
            var colC = toned ? _tonePal.ColC : v.ColC;
            // Lettering enamel, computed from the UN-adopted palette: one ink across every role
            // tint (cream on green, blue and red wood alike).
            var ink = toned ? _tonePal.Ink : DsFxPalette.PaintedInk(colA, colB);
            var a = Adopt(colA, _adoptSource, 0.80f);
            var b = Adopt(colB, _adoptSource, 0.65f);
            // How hard the accent adopts is the family's call: see DsFxFamily.AccentAdoptStrength.
            var c = Adopt(colC, _adoptSource, _spec.Family.AccentAdoptStrength);
            // A control whose STATE is a token value swap must SHOW it. Hue-only adoption
            // (which rightly keeps the material's value for semantic tints) renders a checked
            // box, an ON toggle track and an active tab nearly identical to their off states
            // on deep stock. So for adopting CONTROL surfaces (raised chips and boolean wells —
            // never panels, or a status toast would floodlight) with a genuinely colorful
            // source, the deep tone FOLLOWS the token's value too — gated on the same chroma
            // ramp as adoption, so neutral grounds never lift. The lettering ink is then
            // recomputed against the lifted FILL itself (both arguments the fill: the glyphs
            // sit on pure stock, and the A/B-midpoint model over-reads a near-white ColB).
            if (_spec.AdoptColor && _adoptSource.a >= 0.05f
                && (_spec.Tone == DsFxTone.Raised || _spec.Tone == DsFxTone.Well))
            {
                var chroma = Mathf.Max(_adoptSource.r, Mathf.Max(_adoptSource.g, _adoptSource.b))
                           - Mathf.Min(_adoptSource.r, Mathf.Min(_adoptSource.g, _adoptSource.b));
                var lift = Mathf.InverseLerp(0.10f, 0.32f, chroma);
                if (lift > 0f)
                {
                    Color.RGBToHSV(a, out var ah, out var asat, out var av);
                    Color.RGBToHSV(_adoptSource, out _, out _, out var sv);
                    a = Color.HSVToRGB(ah, asat, Mathf.Lerp(av, Mathf.Max(av, sv * 0.80f), lift));
                    a.a = colA.a;
                    ink = DsFxPalette.PaintedInk(a, a);
                }
            }
            if (target != null && !target.enabledInHierarchy)
            {
                a = Mute(a); b = Mute(b); c = Mute(c);
                var il = 0.299f * ink.r + 0.587f * ink.g + 0.114f * ink.b;
                ink = Color.Lerp(ink, new Color(il, il, il, ink.a), 0.70f);
            }
            def.SetColor("_FxColA", a);
            def.SetColor("_FxColB", b);
            def.SetColor("_FxColC", c);
            def.SetColor("_FxInk", ink);
            def.SetVector("_FxParams", v.Params);
            // _FxMode.w is a tiny bitfield: +1 freeze-idle (ds-fx-static), +2 disabled. Disabled
            // rides along so families can add state TEXTURE on top of the muted palette; it also
            // parks the idle animation, which is what a dead control should do.
            var modeW = (_spec.Static ? 1f : 0f)
                      + (target != null && !target.enabledInHierarchy ? 2f : 0f);
            def.SetVector("_FxMode", new Vector4(_spec.FillMode, _spec.TextMode, _spec.Wear, modeW));
            def.SetVector("_FxRect", _rect);
            def.SetVector("_FxRadii", _radii);
            def.SetVector("_FxBorder", _border);
            def.SetVector("_FxStateH", _hover);
            def.SetVector("_FxStateP", _press);
            def.SetVector("_FxLife", _life);
            def.SetVector("_FxClick", _click);
            def.SetVector("_FxSurface", new Vector4(
                _spec.Tone switch
                {
                    DsFxTone.Well => 2f,
                    DsFxTone.Bg or DsFxTone.Surface => 1f,
                    _ => 0f,
                },
                _focused ? 1f : 0f,
                _anchor.x, _anchor.y));
            target.style.unityMaterial = new StyleMaterialDefinition(def);
        }

        private void OnGeometry(GeometryChangedEvent _)
        {
            if (!_hasMaterial)
                return;
            ReadGeometry();
            Push();
        }

        // ------------------------------------------------------------ states

        private static float Eval(Vector4 s, float now)
        {
            var t = Mathf.Clamp01((now - s.z) / Mathf.Max(s.w, 1e-3f));
            var u = 1f - t;
            return Mathf.Lerp(s.x, s.y, 1f - u * u * u); // easeOutCubic, mirrors the shader
        }

        /// <summary>Retarget a state tuple from its CURRENT eased value, so a hover-out mid-hover-in
        /// glides back from wherever it was instead of snapping.</summary>
        private static Vector4 Retarget(Vector4 s, float to, float duration, float now)
            => new Vector4(Eval(s, now), to, now, duration);

        private void OnEnter(PointerEnterEvent _)
        {
            _pointerOver = true;
            SetHover(1f);
        }

        private void OnLeave(PointerLeaveEvent _)
        {
            _pointerOver = false;
            // A focused element HOLDS its lifted state (and the accent rim riding it): the pointer
            // wandering off must not fade the focus signal.
            if (!_focused)
                SetHover(0f);
        }

        /// <summary>
        /// Keyboard focus lifts the surface exactly like hover — the eased hover tuple IS the
        /// animation — and additionally raises the focus flag the shader multiplies into its accent
        /// rim. Hover alone never shows the rim.
        ///
        /// WELL skins only: a field is where a held focus state is information. On a button (which
        /// takes focus on every click) the same treatment reads as stuck hover.
        /// </summary>
        private void OnFocusIn(FocusInEvent _)
        {
            if (_spec.Tone != DsFxTone.Well)
                return;
            _focused = true;
            SetHover(1f);
        }

        private void OnFocusOut(FocusOutEvent _)
        {
            if (_spec.Tone != DsFxTone.Well)
                return;
            _focused = false;
            SetHover(_pointerOver ? 1f : 0f);
        }

        private void SetHover(float to)
        {
            if (!_hasMaterial) return;
            _hover = Retarget(_hover, to, 0.22f, DsFxManager.Now);
            Push();
        }

        private void OnDown(PointerDownEvent evt)
        {
            if (!_hasMaterial) return;
            var now = DsFxManager.Now;
            _press = Retarget(_press, 1f, 0.09f, now);
            var rs = target.resolvedStyle;
            _click = new Vector4(
                Mathf.Clamp01(evt.localPosition.x / Mathf.Max(rs.width, 1f)),
                Mathf.Clamp01(evt.localPosition.y / Mathf.Max(rs.height, 1f)),
                now, 1f);
            Push();
        }

        private void OnUp(PointerUpEvent _) => ReleasePress();
        private void OnCaptureOut(PointerCaptureOutEvent _) => ReleasePress();

        private void ReleasePress()
        {
            if (!_hasMaterial) return;
            _press = Retarget(_press, 0f, 0.28f, DsFxManager.Now);
            Push();
            // A click is how a control changes state (a tab activates, a nav item selects).
            // Chase the state's token recolor once the USS transition settles. The
            // TransitionEnd path cannot see it when a forced inline background pins the
            // cascade (transparent ghost controls, inactive tabs) — this path can, because
            // RefreshAdopt unmasks the forced background before reading.
            if (_spec.AdoptColor)
                RefreshAdopt();
        }

        private static readonly StylePropertyName BackgroundColorProp = "background-color";

        /// <summary>
        /// A background-color transition just settled on this element: its token background
        /// changed underneath us (state class swap, pseudo-class change), so the adoption
        /// source is stale. Fires on the DE-activated sibling of a clicked tab too — the one
        /// element no pointer event reaches. No unmask dance is needed here: a forced inline
        /// background never transitions in the first place.
        /// </summary>
        private void OnTransitionEnd(TransitionEndEvent evt)
        {
            if (!_hasMaterial || evt.target != target || !evt.AffectsProperty(BackgroundColorProp))
                return;
            _adoptSource = target.resolvedStyle.backgroundColor;
            Push();
        }

        // ------------------------------------------------------------ life

        /// <summary>Play the OUT animation now. The element is left at built = 0 (invisible);
        /// removing it from the tree afterward is the caller's business.</summary>
        public void PlayOut(float duration = 0.6f)
        {
            if (!_hasMaterial) return;
            _life = new Vector4(2f, _spec.OutStyle, DsFxManager.Now, duration);
            Push();
        }

        /// <summary>Replay the IN animation.</summary>
        public void PlayIn(float? duration = null)
        {
            if (!_hasMaterial) return;
            _life = new Vector4(0f, _spec.InStyle, DsFxManager.Now, duration ?? _spec.Family.InDuration);
            Push();
        }

        /// <summary>
        /// Re-read the adoption source after a STATE change recolors the element's token background
        /// (a toggle flipping on, a checkbox checking). Waits out the USS color transition (ds
        /// controls declare ~200ms ones) before the read, exactly like attach-time adoption does.
        /// </summary>
        public void RefreshAdopt(long delayMs = 380)
        {
            if (!_hasMaterial || !_spec.AdoptColor)
                return;
            // Ticker-deferred for the same reason as the attach-time re-read: world panels do
            // not tick their schedulers in a player.
            DsFxManager.RunAfter(delayMs / 1000f, () =>
            {
                if (!_hasMaterial)
                    return;
                if (_forcedBackground)
                {
                    // The forced inline background exists to give a transparent control solid
                    // geometry — but inline wins the cascade, so it also MASKS any token
                    // recolor a later state swap applies (an activating tab's is-active color
                    // could never be read through it). Unmask, let a style pass land, read
                    // the truth, and re-force only if the element is genuinely still
                    // transparent.
                    target.style.backgroundColor = StyleKeyword.Null;
                    _forcedBackground = false;
                    DsFxManager.RunAfter(0.05f, () =>
                    {
                        if (!_hasMaterial)
                            return;
                        _adoptSource = target.resolvedStyle.backgroundColor;
                        if (_spec.FillMode == 0 && _adoptSource.a <= 0.001f)
                        {
                            target.style.backgroundColor = Color.white;
                            _forcedBackground = true;
                        }
                        Push();
                    });
                    return;
                }
                _adoptSource = target.resolvedStyle.backgroundColor;
                Push();
            });
        }

        /// <summary>Stamp states directly, so a frozen clock can be shown exact moments. For proofs
        /// and tests.</summary>
        public void ForceStates(Vector4? hover = null, Vector4? press = null, Vector4? life = null, Vector4? click = null)
        {
            if (!_hasMaterial) return;
            if (hover.HasValue) _hover = hover.Value;
            if (press.HasValue) _press = press.Value;
            if (life.HasValue) _life = life.Value;
            if (click.HasValue) _click = click.Value;
            Push();
        }
    }
}
#endif
