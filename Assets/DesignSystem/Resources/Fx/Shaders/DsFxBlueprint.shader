// ============================================================================
// BLUEPRINT — the design system's reference material family.
//
// Read this file to learn how to write your own. It is a real, shipping
// material (nothing here is a placeholder), and it is deliberately the SIMPLEST
// honest family in the system: a FLAT technical drawing. It uses the
// foundation's distance field, grid and text kit, and pointedly does NOT use
// the lit-chrome helpers (dsfx_plate*, dsfx_wellShade, dsfx_diffuse,
// dsfx_carve/dsfx_paintCarve) — those stay in DsFx.cginc for families that
// want a raised, lit, three-dimensional surface (wood, metal, glass). A
// blueprint is ink on paper: there is no light in it, so there is nothing to
// make shine. The narrative guide is docs/MATERIALS.md; the minimal skeleton
// to copy is in there too.
//
// The idea: the interface drawn as its own reference sheet. Every element is a
// figure on drafting stock — Prussian-blue paper, a graph grid on the sheets,
// the component's own contour inked in crisp linework with its corners CUT at
// 45° the way a drafted plate is chamfered, corner keys the way a plate is
// registered for alignment, and lettering PRINTED flat the way a plotter prints.
// The element's rounded-rect DISTANCE FIELD is what makes that possible:
// the contour helper below re-cuts the USS corner radius into a chamfer, so
// every element keeps its exact authored geometry — just drawn, not molded.
//
// FLAT, ON PURPOSE. Depth on a real drawing is carried by LINE, not by shade:
// weight, setback, construction offsets, keys. So this family never evaluates
// a surface normal, never lights a face, never shades a glyph wall toward a
// key light, and never lifts a soft glow. State is spoken the same drawing
// way — hover re-inks the line brighter and lifts the whole fill one flat
// step, press strikes a second inset line and settles the fill, keyboard
// focus re-inks the contour in the accent and strikes an inset accent line,
// disabled collapses the construction to a single dim stroke. Nothing here
// reflects, nothing glows. If you are hunting a glossy artifact in a flat
// family, look for additive terms and directional dots — this file has none.
//
// THE CONSTRUCTION, per surface (matching the reference sheet):
//   controls (profile 0, real controls)   FRAME–GAP–PLATE: a dim hairline on
//        the element's true edge, a CLEAR gap (the sheet behind shows through),
//        then the inset plate carrying the main line, flat fill and corner
//        keys. This is the double-outline button construction on the sheet.
//   furniture chips (profile 0, texGain<1) nested panels, tab strips, status
//        fills: a single quiet contour — chrome holding controls, not a control.
//        (The skin lowers _FxRect.w to the family's PanelTextureGain exactly
//        for panel-LIKE raised elements, so the gain doubles as that flag.)
//   panels (profile 1)                    the drafting sheet: graph grid, one
//        bright contour, a faint setback construction line, corner keys and
//        "+" registration crosshairs.
//   wells (profile 2)                     a tray drawn in outline: deep flat
//        fill, single contour; keyboard focus re-inks it in the accent with a
//        second inset accent line — a re-inked drawing, not a glowing ring.
//
// STRUCTURE — copy this shape, it is load-bearing:
//   1. dsfx_begin  once, first.
//   2. text branch     — bail to dsfx_passthrough unless a text mode is set.
//   3. solid branch    — dsfx_ownGeometry FIRST, or descendants render as chips
//                        of your material (see DsFx.cginc for why).
//   4. dsfx_passthrough as the final fallthrough. Never return nothing.
//
// FXC survival rules this file obeys — ignore them and the compiler dies with
// no usable message:
//   - the surface is evaluated ONCE; state and life styles feed it as flat
//     MODIFIERS rather than inlining a second full evaluation.
//   - col.a is assigned ONCE in the text branch (a compound `col.a *=` after
//     piecewise .rgb writes trips "cannot use casts on l-values").
//   - keeping the fragment SIMPLE is itself a survival rule; this flat family is
//     comfortably inside what FXC accepts, with headroom the lit families spend.
//
// _FxParams: x grid cell in points, y line weight in points,
//            z plate construction gap in points (0 = single-line controls),
//            w corner-key density (0 = none).
// ============================================================================
Shader "Hidden/DsFx/Blueprint"
{
    Properties
    {
        _FxRect ("Fx Rect (w, h, ppp, texGain)", Vector) = (100, 40, 1, 0)
        _FxRadii ("Fx Corner Radii (TL, TR, BR, BL)", Vector) = (0, 0, 0, 0)
        _FxBorder ("Fx Border Widths (T, R, B, L)", Vector) = (0, 0, 0, 0)
        _FxMode ("Fx Mode (fill, text, wear, static)", Vector) = (0, 0, 0, 0)
        _FxColA ("Fx Color A", Color) = (0.5, 0.5, 0.5, 1)
        _FxColB ("Fx Color B", Color) = (0.8, 0.8, 0.8, 1)
        _FxColC ("Fx Color C", Color) = (1, 1, 1, 1)
        _FxInk ("Fx Ink", Color) = (0, 0, 0, 0)
        _FxParams ("Fx Family Params", Vector) = (1, 1, 1, 1)
        _FxStateH ("Fx Hover State", Vector) = (0, 0, -1000, 0.2)
        _FxStateP ("Fx Press State", Vector) = (0, 0, -1000, 0.2)
        _FxLife ("Fx Life State", Vector) = (1, 0, -1000, 1)
        _FxClick ("Fx Click", Vector) = (0.5, 0.5, -1000, 0)
        _FxSurface ("Fx Surface (profile, focus, anchorX, anchorY)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        // isCustomUITKShader is REQUIRED — without it UI Toolkit will not accept
        // the material through style.unityMaterial.
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "isCustomUITKShader"="true" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex dsfx_vert
            #pragma fragment frag
            // Both multi_compiles are REQUIRED: the first keeps color space correct,
            // the second matches the texture-slot count UI Toolkit batches with.
            #pragma multi_compile _ _UIE_FORCE_GAMMA
            #pragma multi_compile _ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1

            // Sitting beside DsFx.cginc, so a bare relative include is enough and this
            // shader does not care how the package was installed. Your own family, living
            // outside the package, needs the full path — see the header of DsFx.cginc.
            #include "DsFx.cginc"

            // ---------------------------------------------------------------- contour
            //
            // The element's contour with drafted 45° CHAMFER corners: the USS corner
            // radius becomes the chamfer size, cut per corner (a pill radius becomes the
            // sheet's octagonal frame). Returns float2:
            //   x — signed distance to the chamfered contour (points, negative inside);
            //   y — silhouette mask: 0 inside the corner sliver the chamfer removes.
            // The mask multiplies the OUTPUT ALPHA, so the sheet behind shows through
            // the cut — UI Toolkit's own arc coverage still owns the straight edges and
            // we never double-AA them (the plane distance is far negative there).
            float2 bpContour(float2 p, float2 sizePt, float4 radii)
            {
                float2 half_ = sizePt * 0.5;
                float2 q = p - half_;
                // this quadrant's radius (x TL, y TR, z BR, w BL), clamped like the rrect
                float r = q.x < 0.0 ? (q.y < 0.0 ? radii.x : radii.w)
                                    : (q.y < 0.0 ? radii.y : radii.z);
                r = min(r, min(half_.x, half_.y));
                // distances in from this corner's two edges: both small only at a corner
                float2 cd = half_ - abs(q);
                // 45° cut through the arc's own tangency points
                float plane = (r - (cd.x + cd.y)) * 0.70710678;
                float2 d = abs(q) - half_;
                float box = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
                float aa = fwidth(plane) + 1e-4;
                // square corners (r = 0) keep their exact silhouette: no cut
                float cut = 1.0 - smoothstep(-aa, aa, plane) * step(0.5, r);
                return float2(max(box, plane), cut);
            }

            // ---------------------------------------------------------------- grid
            //
            // Graph paper: fine cells with a heavier line every fifth. Anti-aliased by
            // screen derivative, which is what keeps it from shimmering into moire when
            // the panel is small or the display is scaled. `gridLine` is the generic
            // "draw a crisp line at a lattice" helper — steal it.
            float gridLine(float2 p, float cellPt, float weightPt)
            {
                float2 g = abs(frac(p / cellPt - 0.5) - 0.5) * cellPt;
                float d = min(g.x, g.y);
                float aa = fwidth(d) + 1e-4;
                return 1.0 - smoothstep(weightPt * 0.5 - aa, weightPt * 0.5 + aa, d);
            }

            half3 blueprintGrid(half3 col, DsFxFrag f)
            {
                float cell = max(_FxParams.x, 3.0);
                float weight = max(_FxParams.y, 0.4);
                // Grid rides f.pt (element-local), NOT f.tex: gridlines that do not meet
                // the element's own edges read as a printed texture rather than paper
                // the element was drawn on.
                float minor = gridLine(f.pt, cell, weight * 0.6);
                float major = gridLine(f.pt, cell * 5.0, weight * 1.05);
                // Only the SHEET is ruled. The page and panels (profile 1) ARE the
                // drafting paper, so they carry the graph. A raised chip is a figure
                // drawn ON the sheet and a well is an outlined tray — ruling either
                // would read as a texture printed on the thing rather than the paper
                // under it, and it fights the caption, so both stay clear.
                float paper = step(0.5, f.profile) * (1.0 - step(1.5, f.profile));
                float ink = saturate(minor * 0.22 + major * 0.45) * f.texGain * paper;
                return lerp(col, _FxColB.rgb, ink * 0.26);
            }

            // ---------------------------------------------------------------- keys
            //
            // Registration marks, the way a drafted plate is keyed for alignment:
            //   - a short right-angle BRACKET tucked into every corner of the surface
            //     (the reference sheet wears these on plates and panels alike);
            //   - a "+" crosshair set further in, on the big paper of a panel only.
            // Both are pure linework struck from corner-relative distances, so they
            // follow any contour at any size; on a chamfered corner the cut swallows
            // the bracket's very tip, which is exactly how the sheet draws it.
            // edgeInset shifts the whole apparatus inward so it can sit on an inset
            // plate. Density 0 turns it all off (see _FxParams.w).
            half3 blueprintKeys(half3 col, DsFxFrag f, float surfSd, float edgeInset, half3 ink)
            {
                if (_FxParams.w < 0.01)
                    return col;
                float weight = max(_FxParams.y, 0.4);
                float density = saturate(_FxParams.w);
                float minDim = min(f.sizePt.x, f.sizePt.y);

                // Corner-relative distances (points): corner.x is the distance from the
                // nearer VERTICAL edge, corner.y from the nearer HORIZONTAL edge, so both
                // fall to ~0 only at a genuine corner.
                float2 c = abs(f.pt - f.sizePt * 0.5);
                float2 corner = f.sizePt * 0.5 - c;

                // L-bracket: two short arms inset a hair from the (possibly inset) edge.
                // Arm length scales to the element so a small chip gets a small key.
                float inset = edgeInset + 2.4;
                float arm = edgeInset + clamp(minDim * 0.15, 3.5, 10.0);
                float av = (1.0 - smoothstep(weight, weight + 1.0, abs(corner.x - inset))) * step(corner.y, arm);
                float ah = (1.0 - smoothstep(weight, weight + 1.0, abs(corner.y - inset))) * step(corner.x, arm);
                float bracket = saturate(av + ah) * step(surfSd, -0.5);   // on the surface only
                col = lerp(col, ink, bracket * 0.60 * density);

                // "+" crosshair, panels only (profile 1) and only where there is room:
                // struck at (m, m) in from each corner.
                float isPanel = step(0.5, f.profile) * (1.0 - step(1.5, f.profile));
                float m = 12.0;
                float xarm = 4.5;
                float chv = (1.0 - smoothstep(weight, weight + 1.0, abs(corner.x - m))) * step(abs(corner.y - m), xarm);
                float chh = (1.0 - smoothstep(weight, weight + 1.0, abs(corner.y - m))) * step(abs(corner.x - m), xarm);
                float xhair = saturate(chv + chh) * isPanel * step(m * 2.8, minDim);  // 'cross' is an intrinsic
                col = lerp(col, ink, xhair * 0.50 * density);
                return col;
            }

            half4 frag(DsFxVaryings IN) : SV_Target
            {
                DsFxFrag f = dsfx_begin(IN);

                // ---- TEXT ----------------------------------------------------------
                if (f.isText)
                {
                    // No text mode set: this label is not ours to draw. Hand it back
                    // unchanged — never guess.
                    if (!(dsfx_textCarve() || dsfx_textSolid()))
                        return dsfx_passthrough(IN);

                    DsFxTextSd t = dsfx_textSd(IN);

                    // Entrance: the lettering is drawn on, left to right, the way a
                    // plotter prints it. wm.x = visible, wm.y = the pen head's position.
                    float2 wm = float2(1.0, 0.0);
                    if (f.life.mode < 0.5 && f.life.style < 1.5)
                        wm = dsfx_writeMask(f.uv.x, f.life.phase, 0.08);

                    half4 col;
                    if (dsfx_textSolid())
                    {
                        // Display lettering on plain ground: printed FLAT in the light
                        // tone, the way a sheet's titles are inked. No glyph-normal
                        // shading, no gradient — bright, even print.
                        col.rgb = _FxColB.rgb + _FxColC.rgb * wm.y * 1.2;
                    }
                    else
                    {
                        // Printed lettering on a surface: dsfx_ink() FLAT. The ink is
                        // the one channel that knows what the C# side did to this
                        // surface — tone rung, semantic adoption, the value-follow
                        // lift that brightens an active tab or a checked well — so the
                        // shader must not second-guess it (a ColB-derived "always
                        // light" print reads perfectly on deep stock and then drowns
                        // on exactly those lifted fills). The only modeling is a
                        // UNIFORM seat at the stroke edge — deliberately
                        // NON-directional: a directional wall shadow is exactly the
                        // embossed-enamel look a drawing must not have.
                        float wall = saturate(1.0 - (t.sdRaw - 0.5) / 0.11);
                        col.rgb = dsfx_ink() * (1.0 - 0.14 * wall * wall)
                                + _FxColC.rgb * wm.y * 1.5;
                    }
                    // ONE assignment. See the FXC rules in the header.
                    col.a = t.coverage * IN.typeTexSettings.z * IN.color.a * wm.x;
                    return dsfx_finish(col, f);
                }

                // ---- SOLID ---------------------------------------------------------
                if (f.isSolid)
                {
                    // FIRST. A descendant's quad batched into our draw is NOT our element:
                    // render it faithfully or it becomes an invisible chip of blueprint.
                    if (!dsfx_ownGeometry(f))
                        return dsfx_passthrough(IN);

                    // Text-only mode: the only solid geometry left is a marked ds-icon's
                    // vector shape.
                    bool icon = dsfx_textOnly();
                    if (!icon && dsfx_frameOnly() && dsfx_inBorder(f.pt, f.sizePt) < 0.5)
                        return dsfx_passthrough(IN);

                    // Life styles as MODIFIERS to the single surface evaluation below —
                    // never a second full chain.
                    float spatial = 1.0;
                    half3 pen = 0;
                    if (f.life.mode != 1.0)
                    {
                        if (f.life.mode < 0.5 && f.life.style < 0.5 && f.life.phase < 1.0)
                        {
                            // build: the drawing is plotted outward from the centre. The
                            // phase gate above matters: the reach below must clear the
                            // uv-space corner radius (sqrt 2) by the time the phase ends,
                            // and gating at 1 guarantees a finished entrance leaves the
                            // element FULLY inked — an earlier version left every corner
                            // ghosted at ~20% forever, which read as fog on the sheet.
                            float gph = dsfx_easeOutCubic(f.life.phase);
                            float2 cc = (f.uv - 0.5) * 2.0;
                            spatial = smoothstep(gph * 1.75 + 0.05, gph * 1.75 - 0.25, length(cc));
                        }
                        else if (f.life.style > 0.5 && f.life.style < 1.5)
                        {
                            // sweep: a plotter pass, with the pen at the head.
                            float2 wm = dsfx_writeMask(f.uv.x, f.life.built, 0.10);
                            spatial = wm.x;
                            pen = _FxColC.rgb * wm.y * 1.6;
                        }
                    }

                    if (icon)
                    {
                        // A pictogram printed in the same ink family as the lettering
                        // around it — dsfx_ink() pulled toward the accent so icons read
                        // as part of the drawing, not dim captions. Same trust rule as
                        // the text branch: the ink already accounts for tone, adoption
                        // and the value-follow lift. (Danger/muted variants carry their
                        // own _FxColC, so a trash icon on a danger control still inks
                        // red.) Flat by construction: vector geometry has no per-pixel
                        // gradients, and the stamp is a uniform fill.
                        half3 icol = _FxColA.rgb;
                        if (dsfx_textCarve())
                            icol = dsfx_iconStamp(icol, lerp(dsfx_ink(), _FxColC.rgb, 0.50));
                        icol += pen;
                        return dsfx_finish(half4(icol, _FxColA.a * IN.color.a * spatial), f);
                    }

                    // The drawing. ONE evaluation of the contour; every role and state
                    // below is a flat mask over it.
                    float2 cc2 = bpContour(f.pt, f.sizePt, _FxRadii);
                    float sd = cc2.x;
                    float cut = cc2.y;
                    float aa = fwidth(sd) + 1e-4;
                    float inside = -sd;                          // > 0 inside the contour
                    float weight = max(_FxParams.y, 0.4);
                    float minDim = min(f.sizePt.x, f.sizePt.y);
                    bool dis = dsfx_disabledFx();

                    // Line weights: the main contour holds a legibility floor so the
                    // linework IS the design at any size; the outer hairline of the
                    // plate construction stays a step lighter.
                    float lineW = max(weight * 1.5, 1.25);
                    float hairW = max(weight * 0.8, 0.8);

                    // Who is this? Panels and wells come from the profile; among raised
                    // elements the skin's texture gain doubles as the furniture flag —
                    // it is lowered (to PanelTextureGain) exactly for panel-LIKE raised
                    // elements: nested panels, tab strips, status fills. Real controls
                    // keep gain 1. Disabled controls drop their construction: on the
                    // sheet a dead control is a plain dim figure, not a keyed plate.
                    float isWell = step(1.5, f.profile);
                    float isPanel = step(0.5, f.profile) * (1.0 - isWell);
                    bool furniture = f.profile < 0.5 && f.texGain < 0.999;
                    bool plated = f.profile < 0.5 && !furniture && !dis
                               && _FxParams.z > 0.5 && minDim >= 34.0
                               && !dsfx_frameOnly();

                    // Ink and stock. The accent IS the resting linework — cyan on a
                    // plain plate, red on a danger plate, muted on a dead one, all
                    // decided C#-side — cut with a little of the light tone so it never
                    // crushes flat. Hover re-inks brighter; keyboard focus (wells)
                    // re-inks in the full accent. The fill shifts UNIFORMLY with state:
                    // one flat step up on hover, one flat step down under press —
                    // uniform, so it can never read as a gradient across the face.
                    float hover = saturate(f.hover);
                    float focusK = f.focus * hover;
                    half3 lineInk = lerp(_FxColC.rgb, _FxColB.rgb, 0.25 + 0.45 * hover);
                    lineInk = lerp(lineInk, _FxColC.rgb * 1.08, focusK);
                    half3 fill = _FxColA.rgb * (1.0 + 0.08 * hover * (1.0 - isPanel) - 0.10 * f.press);
                    // Nested furniture settles back INTO the sheet: the ladder lifts
                    // raised stock a step for controls, but a nested panel, strip or
                    // badge wearing that step at full voice reads as a bright island
                    // on the drawing. Sheet depth plus its quiet contour is enough.
                    fill *= furniture ? 0.80 : 1.0;
                    half3 col = blueprintGrid(fill, f);

                    float alphaMul = 1.0;
                    if (plated)
                    {
                        // FRAME–GAP–PLATE, the sheet's control construction: hairline on
                        // the true edge, clear gap, then the plate proper. The gap is
                        // carved out of the ALPHA, so the sheet behind (panel grid and
                        // all) genuinely shows through — drawn on the paper, not layered
                        // over it.
                        float g = max(_FxParams.z, 1.2);
                        float plateEdge = hairW + g;

                        float hair = 1.0 - smoothstep(hairW - aa, hairW + aa, inside);
                        float gap = smoothstep(hairW - aa, hairW + aa, inside)
                                  * (1.0 - smoothstep(plateEdge - aa, plateEdge + aa, inside));
                        float plateLine = smoothstep(plateEdge - aa, plateEdge + aa, inside)
                                        * (1.0 - smoothstep(plateEdge + lineW - aa, plateEdge + lineW + aa, inside));

                        col = lerp(col, lineInk, plateLine * 0.95);
                        col = blueprintKeys(col, f, sd + plateEdge, plateEdge, lineInk);

                        // Press: strike a second line just inside the plate contour.
                        float strike = 1.0 - smoothstep(weight * 0.7 - aa, weight * 0.7 + aa,
                                                        abs(inside - (plateEdge + lineW + 1.9)));
                        col = lerp(col, lineInk, strike * 0.55 * f.press);

                        // The hairline is dimmer ink; the gap is not ink at all.
                        col = lerp(col, lerp(lineInk, _FxColA.rgb, 0.45), hair);
                        alphaMul = 1.0 - gap;
                    }
                    else
                    {
                        // Single-contour figures: panels, wells, furniture chips, small
                        // or disabled controls, and frame-only bands.
                        float lineAlpha = isWell > 0.5 ? 0.80 : (furniture ? 0.55 : 0.92);
                        if (dis) lineAlpha *= 0.60;
                        float edge = 1.0 - smoothstep(lineW - aa, lineW + aa, inside);
                        col = lerp(col, lineInk, edge * lineAlpha);

                        // Setback construction line — the draughtsman's offset, panels
                        // only and only where there is room.
                        float roomy = isPanel * step(28.0, minDim);
                        float setback = (1.0 - smoothstep(weight * 0.5 - aa, weight * 0.5 + aa, abs(sd + 3.4))) * roomy;
                        col = lerp(col, _FxColB.rgb, setback * 0.15);

                        // Corner keys ride panels (controls carry theirs on the plate).
                        if (isPanel > 0.5)
                            col = blueprintKeys(col, f, sd, 0.0, lineInk);

                        // Press strike for the small controls that still press.
                        float strike = 1.0 - smoothstep(weight * 0.6 - aa, weight * 0.6 + aa,
                                                        abs(inside - (lineW + 1.8)));
                        col = lerp(col, lineInk, strike * 0.50 * f.press * (1.0 - isPanel) * (1.0 - isWell));

                        // Keyboard focus, wells only: the contour is already re-inked in
                        // the accent (lineInk above); strike the inset accent line and
                        // seat a faint FLAT accent wash inside the edge. A re-inked
                        // drawing — linear, banded, never a glow.
                        float fline = 1.0 - smoothstep(weight * 0.8 - aa, weight * 0.8 + aa,
                                                       abs(inside - (lineW + 2.4)));
                        col = lerp(col, _FxColC.rgb, fline * 0.85 * focusK * isWell);
                        col += _FxColC.rgb * saturate(1.0 - inside / 7.0) * 0.06 * focusK * isWell;
                    }

                    col += pen;

                    half4 outc = half4(col, _FxColA.a * IN.color.a * spatial * alphaMul * cut);
                    return dsfx_finish(outc, f);
                }

                // ---- EVERYTHING ELSE ----------------------------------------------
                // Textures (icons, avatars, images) and anything unrecognized. The
                // passthrough renders them exactly as stock UI Toolkit would.
                return dsfx_passthrough(IN);
            }
            ENDCG
        }
    }
}
