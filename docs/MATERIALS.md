# Materials (FX)

GPU materials for UI Toolkit: real surfaces — stock, linework, depth, engraved lettering — rendered
by a custom shader per element, on Unity 6000.5+.

The system ships one family, **blueprint**, and an engine for writing your own. The engine is the
point. Wood, metal, water and the rest are not here and are not coming; what *is* here is everything
you need to build them, including the parts that took the longest to get right.

> **Requires Unity 6000.5+.** The whole `DesignSystem.Runtime.Fx` namespace is behind
> `#if UNITY_6000_5_OR_NEWER`, because it is built on `style.unityMaterial`, which does not exist
> before then. On 6000.0–6000.4 the design system works exactly as it always has, minus this.

## Contents

- [The idea](#the-idea)
- [Quick start](#quick-start)
- [The `ds-fx-` grammar](#the-ds-fx--grammar)
- [The tone ladder](#the-tone-ladder)
- [The readability rules](#the-readability-rules)
- [Theming](#theming)
- [What the foundation gives you](#what-the-foundation-gives-you)
- [Write your own family](#write-your-own-family)
- [Rules that will cost you a day if you skip them](#rules-that-will-cost-you-a-day-if-you-skip-them)
- [Cost, and restraint](#cost-and-restraint)

## The idea

Three things make this work, and they are worth understanding before you write any of it.

**The element's own rendering is the canvas.** A skin does not proxy, wrap or replace anything. It
sets `style.unityMaterial`, and the element's existing background quads, border geometry and glyph
SDFs flow through your shader. Text stays live — change a `Button`'s text and your engraving follows
it. Layout, picking, focus and accessibility never learn that anything happened.

**One float per frame is the entire runtime.** Every animation — an idle ripple, a hover lift, a
click impulse, an entrance — is a pure function of the global clock `_DsFxTime` evaluated against
`(from, to, t0, duration)` tuples stamped at event time. `DsFxManager` writes that clock once per
frame and does nothing else. There is no per-element update, no repaint scheduling, no render
texture. A screen full of animating materials costs the CPU nothing between events.

**Because of that, rendering is reproducible.** `DsFxManager.OverrideTime(t)` pins the clock, and
every rendered frame becomes a pure function of (uniforms, clock). Two captures of the same instant
are byte-identical. That is not a testing feature bolted on; it falls out of the architecture, and it
means you can hold a material refactor to a pixel-exact bar.

## Quick start

**Install:** you already have it. It ships inside `Assets/DesignSystem/`.

### 1. Mark up an element

```xml
<ui:Button text="Get started" class="ds-btn ds-fx-blueprint ds-fx-text-carve" />
```

```csharp
using DesignSystem.Runtime.Fx;

// After the tree is attached and laid out:
DsFx.ApplyMarkers(root);
```

That element is now a drafted blueprint plate with flat printed lettering, and it hovers, presses and
animates on its own.

### 2. Or material-theme the whole screen

```csharp
DsFxManager.ThemeLight = false;
DsFxManager.ActiveTheme = DsFxThemeColors.FromThemeData(myThemeAsset); // or null for native hues

// Post-layout: roles read resolvedStyle.
var skinned = DsFxTheme.Apply(root, DsFxBlueprintFamily.Family, "cyanotype");
```

`DsFxTheme` knows what the design system's components *are* — that a `ds-btn` is a board standing
proud, that a `ds-input`'s inner box is a tray carved into the panel, that a slider's rail is
furniture and its dragger is the control. It walks the tree, assigns every element its rung on the
[tone ladder](#the-tone-ladder), enforces the [readability rules](#the-readability-rules), and writes
the same markers you could have authored by hand.

Call it **after** the tree is attached and laid out — roles read `resolvedStyle`.

### Switching materials, and switching back

```csharp
DsFxTheme.Revert(root);                                  // back to stock: skins off, markers stripped,
                                                         // inline styles cleared, callbacks unhooked
DsFxTheme.Apply(root, MossFamily.Family, "spring");      // now a different material
DsFxTheme.IsThemed(root);                                // → false after Revert
```

**Revert before you re-Apply.** `Apply` is not "switch to X". The previous pass's markers are still on
the elements, and hand-authored markers win — so an `Apply` over a themed tree is silently a no-op,
which looks exactly like a broken material. This is the single most likely mistake when wiring a
material picker.

One edge worth knowing: `Revert` clears the inline properties the mapper writes (color, textShadow,
backgroundColor, image tint, border colors, the scrollbar form) back to the **USS cascade**, not to a
remembered previous inline value. If your host had its own inline value for one of those on a themed
element, it does not survive the round trip.

The showcase's theme dropdown is the worked example: pick *Blueprint (shader)* and every component
becomes a material; pick a colour palette and it reverts.

### 3. Or write your own family

See [Write your own family](#write-your-own-family). That is the interesting one.

## The `ds-fx-` grammar

Markers are read by C#, never by USS. They carry no style rules — they are instructions to a runtime,
which is why they are their own vocabulary rather than BEM component classes.

| Marker | Does |
| --- | --- |
| `ds-fx-<family>[--<variant>]` | The material. `<family>` must be registered; unknown variants fall back to the family default. |
| `ds-fx-text-carve` | Lettering engraved into the surface and filled with enamel ink. The default for text on a material. |
| `ds-fx-text-solid` | Lettering *made of* the material. For text on plain ground — a hero title, not a caption. |
| `ds-fx-frame` | Shade the border band only; the fill passes through untouched. |
| `ds-fx-worn` / `ds-fx-worn--heavy` | Wear. |
| `ds-fx-static` | Freeze the idle animation. |
| `ds-fx-in--build\|sweep\|fade` | Entrance choreography. |
| `ds-fx-out--build\|sweep\|fade` | Exit choreography. |

Three more are written by `DsFxTheme` at runtime, and you can author them by hand when driving
elements yourself:

| Marker | Does |
| --- | --- |
| `ds-fx-tone--bg\|surface\|raised\|well` | Render at that ladder rung and surface profile. |
| `ds-fx-adopt` | Tint the palette from the element's own resolved background, so semantic color survives the material — a danger button stays recognizably red. |
| `ds-fx-inert` | Decoration, not a control: no hover, press, click or focus response. |

Hand-authored markers always win: `DsFxTheme` skips a marked element and its whole subtree.

## The tone ladder

A material UI is not "components tinted wood-color". It is **one material worked at different
depths**. Four rungs:

| Tone | Who sits on it | Profile |
| --- | --- | --- |
| **bg** | the page itself | panel |
| **surface** | sections, cards, sheets, modals, drawers, toasts | panel — routed edge + vignette |
| **raised** | buttons, tabs, chips, nav items, draggers, meter fills | plate — floats on a drop-shadow skirt |
| **well** | input trays, toggle tracks, slider grooves, progress tracks | sunken tray — deep top inner shadow, lit bottom lip |

Direction follows appearance: a **dark** theme runs page-deepest to control-lightest; a **light**
theme reverses it.

Two mechanisms make the ladder read as *one* material rather than four tints:

- **Per-element pattern anchor** (`_FxSurface.zw`, reaching your shader as `f.tex`). A stable hash of
  the element's world position offsets the pattern domain, so every element is its own cut of stock.
  No two elements are pattern twins, and nothing reads as one printed sheet. Sample figure from
  `f.tex`; keep geometry cues (bevels, edges) on `f.pt`.
- **Surface profiles** (`_FxSurface.x`, reaching you as `f.profile`). Raised gets plate chrome, panel
  the routed board treatment, well the sunken tray.

One deliberate non-rule: `DsFxTheme` **never skins the page root**. The page is the wall, the material
is the furniture. Wall-to-wall paneling buries the depth hierarchy.

## The readability rules

Encoded in `DsFxTheme` and followed without per-component exceptions. They exist because every one of
them was learned by breaking it.

1. **Body text is never material.** Every text element gets an ink chosen for the tone it actually
   rides, plus a counter-toned shadow. A [high-figure](#the-family-descriptor) family gets a *thick*
   halo — the thin default drowns in grain.
2. **Titles and captions riding material are engraved** (`ds-fx-text-carve`): a routed groove filled
   with enamel. The rim supplies local separation, which is why the enamel can run silver on mid-gray
   steel. Solid material lettering is for plain ground only.
3. **Captions and icons on a raised control print onto it** in the same enamel, so a glyph button and
   the caption beside it wear one ink.
4. **Input wells are sunken trays.** Value text is ink, never carved. The caret and selection wear the
   theme accent (contrast-checked; falls back to ink). Keyboard focus draws an accent rim.
5. **Containers are material panels** — never a bare frame around a flat token middle, which is
   exactly the half-modern, half-themed look this bans. A panel nested in a panel steps up one rung.
6. **Tracks and grooves are wells; the moving part rides raised.** Knobs and dots stay stock.
7. **Semantic color survives through chroma-gated adoption on controls and status panels.** Other
   panels and wells stay neutral — a token rainbow on every surface is the other half of the mix look.
8. **Swatches, avatars, skeletons, spinners and loose icons on plain ground stay stock.** Their job is
   showing the real palette, or animating.
9. **Only real controls answer the pointer.** Panels, tracks under moving parts, status fills, badges
   and carved captions are `ds-fx-inert`. A section is a wall, not a button.
10. **Opaque non-material chrome is an island.** An element that keeps its own opaque background keeps
    its own text colors. The rules apply to what rides the material.
11. **Disabled controls go ashen.** The palette mutes; the figure stays.

## Theming

`DsFxPalette.Derive(variant, tone, light, theme)` computes every rung's palette. With `theme == null`
the material renders in its own native hues. Pass a `DsFxThemeColors` and it aligns:

| Theme color | Where it lands |
| --- | --- |
| `Accent` | re-keys the material accent outright — glints, emissive, rim; plus carets, selection, focus rim |
| `WindowBackground` | hue+saturation lean of the deep and light tones, chroma-gated, **value untouched** |
| `ForegroundPrimary` | ink bias — but only as far as the 4.5:1 floor allows |
| appearance | ladder direction |

The rule the whole file obeys: **the material keeps its value structure; themes contribute hue,
saturation and accent only.** That is the difference between native stained wood and tinted plastic.

Bind it to your own theme asset and the material cannot drift from the token cascade:

```csharp
DsFxManager.ActiveTheme = DsFxThemeColors.FromThemeData(myTheme);
```

Appearance is inferred from the theme's `bgColor` luma, since `ThemeData` carries no explicit flag.
Pass the `light:` argument to override.

The **chroma gate** (`DsFxPalette.GatedHueTransfer`) is why this does not turn everything into a
rainbow: near-neutral sources leave the material alone, and only genuinely colorful ones tint it. One
implementation serves two scales — theme-wide alignment, and `ds-fx-adopt` per control.

## What the foundation gives you

Everything below is in `Resources/Fx/Shaders/DsFx.cginc`, already written, already
mobile/WebGL2-safe. Blueprint deliberately uses only the FLAT subset (distance field, grid math, text
kit, passthrough, life/state) and leaves the lit chrome — `dsfx_plate*`, `dsfx_wellShade`,
`dsfx_diffuse`/`dsfx_spec`, `dsfx_paintCarve`'s directional cousin `dsfx_carve` — to families that
want a three-dimensional surface; read `DsFxBlueprint.shader` for the flat half in context and the
skeleton below for the lit half.

**Fragment context** — `dsfx_begin(IN)` returns a `DsFxFrag` with everything pre-chewed: `isSolid` /
`isText`, layout `uv` and `pt` (points), the anchored pattern domain `tex`, `sizePt`, the rounded-rect
signed distance `sd`, `coverage`, eased `hover` / `press`, `wear`, `profile`, `focus`, `texGain`,
`idleT`, and the `life` snapshot.

**The passthrough** — `dsfx_passthrough(IN)` renders solids, textures and SDF text exactly as stock UI
Toolkit would. Return it for anything that is not yours. It samples textures properly rather than
degrading them to a vertex tint, which is what keeps a checkbox tick a tick instead of a slab.

**Descendant discrimination** — `dsfx_ownGeometry(f)`. Non-negotiable; see
[the rules](#rules-that-will-cost-you-a-day-if-you-skip-them).

**Shape and light** — `dsfx_rrectSd` (the element's exact contour, any size, any radius),
`dsfx_shapeNormal` (profile-aware: raised bevels, panels roll gently, wells tip *inward*),
`dsfx_diffuse`, `dsfx_spec`, `dsfx_sparkle`, `dsfx_borderDepth`, `dsfx_inBorder`, `DSFX_KEY_LIGHT`.

**Depth chrome** — the grammar that makes the ladder read: `dsfx_plate` / `dsfx_plateChrome` /
`dsfx_plateCompose` (a raised plate floats: inset by a skirt carved from the element's own margin,
drop shadow pooling under the lower edge, dark outline, lit bevel, contact shade, press pushes it
*down*), `dsfx_wellShade` (sunken trays and routed panel boards), `dsfx_focusRim`.

**Text** — `dsfx_textSd` (coverage, signed distance, gradient, and `sdRaw`, the only depth measure
stable across font sizes), `dsfx_paintCarve` (enamel in a routed rim — the default), `dsfx_carve`
(cut into the surface, groove floor keeps the material), `dsfx_ink` (the enamel C# computed for this
tone — already guaranteed readable, do not second-guess it), `dsfx_overGlow` (premultiplied compositing
that does not fringe), `dsfx_iconStamp`.

**Motion** — `dsfx_state` (evaluate a tuple), `dsfx_life`, `dsfx_clickPulse`, `dsfx_clickRipple`,
`dsfx_writeMask` (left-to-right write-on with a glowing head), `dsfx_easeOutCubic` /
`easeInOutCubic` / `easeOutBack`, `dsfx_springDecay`.

**Noise** — `dsfx_hash11/12/22`, `dsfx_vnoise`, `dsfx_fbm` (3 octaves), `dsfx_fbm2` (2). Integer-hashed,
because sin-hashes sparkle on GLES3/WebGL2 mediump.

**Modes** — `dsfx_textCarve()`, `dsfx_textSolid()`, `dsfx_frameOnly()`, `dsfx_textOnly()`,
`dsfx_disabledFx()`.

## Write your own family

Two pieces: a shader, and a registration.

### The shader

```hlsl
Shader "Hidden/MyGame/Moss"
{
    Properties
    {
        // Copy this whole block from DsFxBlueprint.shader. The uniform names are the contract.
        _FxRect ("Fx Rect", Vector) = (100, 40, 1, 0)
        // ... _FxRadii, _FxBorder, _FxMode, _FxColA/B/C, _FxInk, _FxParams,
        //     _FxStateH, _FxStateP, _FxLife, _FxClick, _FxSurface
    }
    SubShader
    {
        // isCustomUITKShader is REQUIRED — without it UI Toolkit rejects the material.
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "PreviewType"="Plane" "isCustomUITKShader"="true" }
        Cull Off  ZWrite Off  Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex dsfx_vert
            #pragma fragment frag
            #pragma multi_compile _ _UIE_FORCE_GAMMA
            #pragma multi_compile _ _UIE_TEXTURE_SLOT_COUNT_4 _UIE_TEXTURE_SLOT_COUNT_2 _UIE_TEXTURE_SLOT_COUNT_1

            // Vendored / embedded install:
            #include "Assets/DesignSystem/Resources/Fx/Shaders/DsFx.cginc"
            // Installed by UPM instead? Use:
            // #include "Packages/com.sinanata.designsystem/Resources/Fx/Shaders/DsFx.cginc"

            half3 mossSurface(DsFxFrag f)
            {
                // Figure rides f.tex (the ANCHORED domain) so every element is its own cut.
                // Scale whatever you MULTIPLY by f.texGain so panels can quiet down under text.
                float growth = dsfx_fbm(f.tex * 0.08);
                half3 col = lerp(_FxColA.rgb, _FxColB.rgb, saturate(growth));
                col *= 1.0 - 0.25 * f.texGain * growth;
                return col;
            }

            half4 frag(DsFxVaryings IN) : SV_Target
            {
                DsFxFrag f = dsfx_begin(IN);

                if (f.isText)
                {
                    if (!(dsfx_textCarve() || dsfx_textSolid()))
                        return dsfx_passthrough(IN);          // not ours — hand it back
                    DsFxTextSd t = dsfx_textSd(IN);
                    half4 col;
                    col.rgb = dsfx_paintCarve(dsfx_ink(), _FxColA.rgb * 0.3, t);
                    col.a = t.coverage * IN.typeTexSettings.z * IN.color.a;  // ONE assignment
                    return dsfx_finish(col, f);
                }

                if (f.isSolid)
                {
                    if (!dsfx_ownGeometry(f))                 // FIRST. Always.
                        return dsfx_passthrough(IN);

                    DsFxPlate pl = dsfx_plate(f);
                    half3 col = mossSurface(f);
                    col *= dsfx_diffuse(dsfx_shapeNormal(f.pt, f.sizePt, _FxRadii, pl.sd, 6.0, f.profile));
                    col = dsfx_plateChrome(col, pl, f, _FxColA.rgb * 0.3, _FxColB.rgb);
                    col = dsfx_focusRim(dsfx_wellShade(col, f), f);   // UNCONDITIONAL

                    half4 outc = half4(col, _FxColA.a * IN.color.a);
                    outc = dsfx_plateCompose(outc, pl, f);
                    return dsfx_finish(outc, f);
                }

                return dsfx_passthrough(IN);                  // textures, everything else
            }
            ENDCG
        }
    }
}
```

### The registration

```csharp
using DesignSystem.Runtime.Fx;
using UnityEngine;

public static class MossFamily
{
    public static readonly DsFxFamily Family = new DsFxFamily(
        "moss",                          // the ds-fx-moss marker head
        "Hidden/MyGame/Moss",            // Shader.Find name
        "Shaders/Moss",                  // Resources.Load fallback — MUST be under a Resources folder
        new[]
        {
            // params: whatever your shader says they are. Document them in its header.
            new DsFxVariant("spring", new Color(...), new Color(...), new Color(...), new Vector4(...)),
        })
    { HighFigure = true };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register() => DsFxRegistry.Register(Family);
}
```

Now `ds-fx-moss--spring` works in UXML, and `DsFxTheme.Apply(root, MossFamily.Family, "spring")`
themes an entire screen in moss.

### The three colors are a contract

`DsFxPalette` derives the whole tone ladder *and* the readable ink from them, so they must mean what
their names say: **ColA is the deep tone** (its alpha is the master material alpha — set it below 1
for a translucent family), **ColB is the light tone**, **ColC is the accent**. A variant that inverts
them — pale stock in ColA, dark lines in ColB — quietly breaks the ladder and the contrast floor. Vary
by hue; a light *appearance* is `DsFxManager.ThemeLight`'s job, not a variant's.

### The family descriptor

Four traits, and they are the only per-family knowledge the pipeline itself acts on. Everything else
belongs in your shader, driven by `DsFxVariant.Params`.

| Trait | Default | Set it when |
| --- | --- | --- |
| `InDuration` | `0.9` | Your entrance wants a different beat. |
| `PanelTextureGain` | `0.85` | Your pattern is **additive light** (glowing scanlines, caustics, rain) — drop to ~`0.22` or paragraphs on your panels become unreadable. Palette-carried figure (grain, brushing) stays at the default. |
| `AccentAdoptStrength` | `0.40` | The accent **is** your family's identity (a neon tube, an emissive rim) — raise toward `0.80`, or a danger button keeps your hue instead of going red. |
| `HighFigure` | `false` | Your surface is busy. Body ink then gets a thick counter-halo. Declare it honestly: it costs nothing, and unreadable text is the one failure a user cannot work around. |

### The fast loop

**`Design System > FX > Compile Check`** force-compiles every registered family and gives you the file
and line. Use it. Importing a shader only *parses* it — the first real compile otherwise happens at
first draw inside play mode, where a failure is a magenta element and one console line, minutes into a
run and far from the edit that caused it.

For CI: `-executeMethod DesignSystem.Editor.Fx.DsFxCompileCheck.RunBatch` exits non-zero on failure.

## Rules that will cost you a day if you skip them

**Start your solid branch with `dsfx_ownGeometry(f)`.** Unity 6000.5 batches *descendant* quads into
an ancestor's custom-material draw. A plain solid child — a token swatch, a status dot, an active-row
background — arrives in *your* `isSolid` branch, and if you shade it, it renders as a chip of your
material: invisible against the panel around it. The test *measures the emitting rect*: layout UV
spans 0..1 across whichever rect emitted the quad, `fxData.zw` carries the same vertex's panel-space
position in points, and the ratio of their screen derivatives recovers the rect's true size per axis
— the camera cancels out, so the measurement holds at any distance, angle, or DPI, on screen and
world panels alike. Only a match with the element's own size renders as material; everything else
takes the passthrough. The one case it cannot catch is a **full-bleed** solid child exactly matching
its skinned parent — the rects are identical, so they measure identical. Give that child a white
background-image and tint it so it rides the texture path instead.

**World-space panels are on by default everywhere EXCEPT a WebGL player — and that exception is an
engine bug, not a policy.** On Unity 6000.5, a `PanelRenderMode.WorldSpace` panel carrying custom
materials drives the renderer past the end of its own UI geometry buffer in a WebGL player:

```
GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=590904 srcSize=589824)
```

That is Unity's staged UI-geometry updater (the code path for platforms without mapped GPU buffers —
the WebGL and WebGPU players; editor and native players never run it) sizing its staging buffer from
the *pre*-consolidation dirty count, then growing its ranges *after* sizing (gap-filling
consolidation, index alignment). Its padded tiers are {8Ki, 64Ki} vertices / {8Ki, 256Ki} indices,
so any one-frame burst landing within ~11% below a tier — or above the last one — overruns. The
first window is ~7.4–8.2Ki elements, the size of ONE big component's restyle, which is why no
host-side pacing can fully dodge it and why the skipped copies leave panels rendering **stale**
geometry (a restyled world UI that keeps its old look) before the eventual fatal wasm `bounds`
exception.

The actual fix is `Tools/UirStagingPatch`: a one-instruction Cecil patch to the WebGL playback
engine's managed module on the **build machine**, padding the staging request to `2r+64` so capacity
always covers the growth. Apply it once per editor install (`Apply-UirStagingPatch.ps1`, UAC prompt,
backup + `-Restore` included); it takes effect on the next WebGL build. The corridor additionally
paces its restyles (`ThemeWave`/`RevealWave`, one exhibit per frame) — that spreads load and reads
nicely, but it is not the safety mechanism.

`DsFxManager.AllowWorldSpacePanels` defaults to `false` only in a WebGL player built on an
**unpatched** machine — the showcase's `BuildCli` detects the patch and injects the per-build define
`DS_UIR_STAGING_PATCHED`, which turns the default `true` there too, so a patched build needs no flag
at all. Everywhere else it is already `true`. Where it is false, `DsFxSkin` does not attach on a
world panel: those elements render as stock design system (one console warning says so), and the
screen ↔ world toggle stays clean. Screen-space panels are mostly fine in practice but ride the same
defective updater, so patched builds are safer across the board. The showcase's `?worldfx=1` remains
as a manual override for unpatched test builds; when Unity fixes the updater, deleting the `#if`
around the default is the whole change.

**Even with the overrun patched, a WebGL player's *native* world-panel draw silently ignores
per-element custom materials.** Skins attach, inline styles hold, and the managed renderer records
the per-material command lists correctly — verified by decompiling the draw path and by an in-player
census (35/35 sections skinned, zero errors) — yet the panel draws with stock chrome. That is below
the design-system layer, not fixable from a shader or a manipulator. So a host that wants materials
on world-space UI *in a WebGL build* renders each panel to a RenderTexture shown on a quad — the
flat-panel pipeline, which draws materials correctly in every player. The showcase's
`WorldSpaceCorridor` is the worked example (WebGL only, behind `UseRtExhibits`; `?fxrt=1` forces it
elsewhere for testing): a transparent-clear `ScreenSpaceOverlay` panel per exhibit, alpha-blended
over the lit mounting plate by a tiny unlit `Hidden/DsShowcase/RtScreen` shader kept in `Resources`
so URP variant-stripping cannot zero it (the FX shaders survive the same way); the quad is
hand-built because `GameObject.CreatePrimitive` attaches a `MeshCollider` and physics is stripped
from WebGL; it stays hidden until the first real content measure so it never pops in at the estimate;
the nearest exhibit re-renders at 2× for near-camera sharpness (a fixed RT blurs when magnified, a
native panel re-rasterizes every frame); and pointer input routes through
`PanelSettings.SetScreenToPanelSpaceFunction` with an analytic camera-ray/quad-plane intersection —
returning **RT-pixel space** (`points × panel scale`), because the engine consumes that function as
`screenToPanelSpace(screen) / base.scale` and for a `ConstantPixelSize` panel `base.scale` is the
`PanelSettings.scale` the supersampling drives. Editor and native players keep the direct
world-space path, where materials draw straight.

Two more world-space facts worth knowing even though the foundation now handles them for you:

- `dsfx_ownGeometry` needs no special path in 3D: the emitting-rect measurement above is
  host-independent. (Historical note, because the symptom is distinctive: the earlier heuristics read
  the *camera* — first an absolute pixels-per-point window, true at exactly one camera distance, so
  the material rendered at mid range and **vanished as you walked toward or away**; then a
  derivative aspect-ratio test, which equals the local screen *anisotropy*, so near and grazing views
  broke it the same way. If you ever see distance-dependent vanishing again, someone reintroduced a
  camera-dependent term into that test.)
- `DsFxSkin` flags a world host by pushing `_FxRect.z` **negative**, so read pixels-per-point as
  `abs(_FxRect.z)` if your family uses it. A raw read silently mis-tunes anything scaled by it in 3D
  — and in world space that number does not describe the screen anyway, so prefer point-space sizing
  throughout.

**Call `dsfx_wellShade` unconditionally.** Never from inside a profile branch. Line-level bisection
proved that a call from inside a branch reliably crashes FXC — *"Lost connection with shader
compiler"*, no message, no line — while the identical call hoisted above the branch chain compiles
fine. The function is mask-based and branch-free, so an unconditional call is semantically identical:
raised profiles pass through untouched. **If your family dies at compile time with no usable message,
look here first.**

**Evaluate your surface once.** Feed life styles in as *modifiers* rather than inlining a second full
surface chain in another branch. Inlining it twice in one branch is the other reliable way to crash
FXC.

**Assign `col.a` exactly once** in the text branch. A compound `col.a *=` after piecewise `.rgb`
writes trips *"cannot use casts on l-values"*.

**Sample figure from `f.tex`, not `f.pt`.** `f.pt` restarts the pattern at every element's corner —
identical twins. `f.tex` is `f.pt` plus the per-element anchor.

**Use the integer hashes.** `dsfx_hash*` are integer-based because sin-hashes sparkle on GLES3/WebGL2
mediump. Keep fbm at 3 octaves; that is the mobile ceiling.

**Test in a real build.** The editor hides GLES compile failures that a WebGL build surfaces.

**Do not fight `dsfx_ink()`.** It is the enamel C# already computed for the tone this text rides,
contrast-floor and all.

The C# side has one rule of its own, and it is in `DsFxSkin`: **every push builds a fresh
`MaterialDefinition`.** The inline-style setter skips application when the incoming definition compares
equal to the stored one, and the stored one holds a *reference* to the definition's property list.
Mutate one shared list and every later push compares the list against itself — always equal, never
applied — and the element freezes at whatever the first push said. If you extend the skin, keep that
property.

## Cost, and restraint

**Every skinned element is one extra draw call.** Materials are for hero surfaces, not for every
element on screen. `DsFxTheme.Apply` deliberately skins a whole tree because that is the honest stress
test — and the thing you should look at before deciding how much of your UI deserves this.

Materials compose with token theming rather than replacing it: material colors are shader uniforms,
not tokens, so your `ThemeData` still restyles everything the material does not paint. A transient
overlay attached outside the themed tree (a dropdown popup) stays on stock chrome by design.
