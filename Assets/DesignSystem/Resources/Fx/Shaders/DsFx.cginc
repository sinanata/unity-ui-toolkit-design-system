#ifndef DS_FX_INCLUDED
#define DS_FX_INCLUDED

// ============================================================================
// Design System material FX - the shared foundation every material family
// builds on. Write your own family against this file; the full guide, the
// ability list and a copy-paste skeleton are in docs/MATERIALS.md.
//
// Including this from your own shader:
//   #include "Assets/DesignSystem/Resources/Fx/Shaders/DsFx.cginc"          // embedded / vendored
//   #include "Packages/com.sinanata.designsystem/Resources/Fx/Shaders/DsFx.cginc"  // installed by UPM
// Both project-root-relative forms resolve; pick the one matching your install.
// A shader sitting BESIDE this file (like DsFxBlueprint) just says "DsFx.cginc"
// and stays install-shape independent.
//
// Contract: Unity 6000.5 UITK custom materials (style.unityMaterial). The
// element's OWN geometry flows through this shader: background quads and
// border geometry arrive as SOLID fragments, the label's glyphs arrive as
// TEXT fragments carrying the TextCore SDF atlas. Layout UV (0..1 across the
// element rect, v grows DOWN) rides in appdata uv.zw - the same channel the
// ShaderGraph UITK target reads ("Layout uv in z, w").
//
// Everything is driven by per-element MaterialDefinition uniforms plus ONE
// global clock (_DsFxTime). State changes write (from, to, t0, duration)
// tuples; the shader eases between them from the clock alone, so an idle or
// transitioning element costs ZERO CPU per frame - fire and forget.
//
// Mobile/WebGL2 rules kept here: shader model 3.5, no compute, no uniform
// arrays, integer hash (sin-hash drifts on mediump), fbm capped at 3 octaves,
// derivatives only where the standard UIE shader already uses them.
// ============================================================================

#include "Internal/UnityUIE.cginc"

// ---------------------------------------------------------------- uniforms

float4 _FxRect;    // x,y: element size in points.
                   // z: pixels-per-point, and its SIGN is a flag. Positive = a screen-space panel,
                   // where one layout point really is |z| screen pixels. NEGATIVE = a WORLD-SPACE
                   // panel (PanelRenderMode.WorldSpace), drawn in 3D, where that mapping does not
                   // hold at all and slides with camera distance. Read the magnitude with abs() if
                   // your family scales anything by it; nothing in this foundation depends on it
                   // any more (dsfx_ownGeometry once did — it now measures the geometry itself and
                   // works in both hosts). The sign stays because a family tuning screen-pixel
                   // effects must know when that number stops meaning anything.
                   // w: structural texture gain (0 reads as 1 = full voice; ~0.22 = a whisper under
                   // body text). Whatever your family MULTIPLIES into the surface — grain lines,
                   // brush contrast, wave swells, powder grit — scales by it, so panels can carry
                   // text without the figure fighting the letters. The palette collapse quiets
                   // color-MIXED pattern; this is the other half. Read it as f.texGain, never raw.
float4 _FxRadii;   // corner radii in points: x TL, y TR, z BR, w BL
float4 _FxBorder;  // border widths in points: x T, y R, z B, w L
float4 _FxMode;    // x fill mode (0 fill, 1 frame-only, 2 text-only); y text mode (0 none, 1 carve, 2 solid); z wear 0..1; w 1 = freeze idle animation
float4 _FxColA;    // primary material color (deep tone). A of _FxColA = master material alpha (glass < 1)
float4 _FxColB;    // secondary (light tone / highlight)
float4 _FxColC;    // accent (rim, emissive, varnish, glow)
float4 _FxParams;  // per-family meaning, documented in each shader
float4 _FxStateH;  // hover:  from, to, t0, duration
float4 _FxStateP;  // press:  from, to, t0, duration
float4 _FxLife;    // x mode (0 in, 1 idle, 2 out); y style (0 build, 1 sweep, 2 fade); z t0; w duration
float4 _FxClick;   // x,y: click layout UV; z: t0; w: strength (0 = never clicked)
float4 _FxSurface; // x profile (0 raised control, 1 panel, 2 sunken well); y focus flag (rim = flag * eased hover);
                   // z,w pattern anchor in points (a stable per-element hash of world position): added to the
                   // pattern domain so every element is its own CUT of the material — decorrelated figure,
                   // never identical twins, never one printed sheet
float4 _FxInk;     // painted-lettering ink (rgb) — cream on wood, silver on steel: the enamel that fills
                   // routed grooves and stamped icons. a = 1 when the skin supplied it (0 derives from ColB).

float _DsFxTime;   // global clock, seconds. Set once per frame by DsFxManager (freezable: DsFxManager.OverrideTime
                   // pins it, which makes every rendered frame a pure function of (uniforms, clock) — the property
                   // the render proof relies on to compare runs byte-for-byte).

// ---------------------------------------------------------------- hashing / noise

// Integer hash (iq/Wang style): exact on GLES3/WebGL2 where sin-hashes sparkle.
uint dsfx_uhash(uint x)
{
    x ^= x >> 16; x *= 0x7feb352du;
    x ^= x >> 15; x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float dsfx_hash11(float p)
{
    return float(dsfx_uhash(asuint(p))) * (1.0 / 4294967295.0);
}

float dsfx_hash12(float2 p)
{
    uint h = dsfx_uhash(asuint(p.x) ^ dsfx_uhash(asuint(p.y)));
    return float(h) * (1.0 / 4294967295.0);
}

float2 dsfx_hash22(float2 p)
{
    uint h = dsfx_uhash(asuint(p.x) ^ dsfx_uhash(asuint(p.y)));
    return float2(float(h) * (1.0 / 4294967295.0), float(dsfx_uhash(h)) * (1.0 / 4294967295.0));
}

// Value noise on an integer lattice, smooth interpolation.
float dsfx_vnoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = dsfx_hash12(i);
    float b = dsfx_hash12(i + float2(1, 0));
    float c = dsfx_hash12(i + float2(0, 1));
    float d = dsfx_hash12(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 3-octave fbm - the ceiling for mobile. ~[0,1].
float dsfx_fbm(float2 p)
{
    float v = 0.0;
    v += 0.5000 * dsfx_vnoise(p);
    v += 0.2500 * dsfx_vnoise(p * 2.03 + 17.1);
    v += 0.1250 * dsfx_vnoise(p * 4.01 + 47.7);
    return v * (1.0 / 0.875);
}

// 2-octave cheap variant for secondary detail.
float dsfx_fbm2(float2 p)
{
    return (0.6667 * dsfx_vnoise(p) + 0.3333 * dsfx_vnoise(p * 2.13 + 5.7));
}

// ---------------------------------------------------------------- easing

float dsfx_easeOutCubic(float t)  { float u = 1.0 - t; return 1.0 - u * u * u; }
float dsfx_easeInOutCubic(float t){ return t < 0.5 ? 4.0 * t * t * t : 1.0 - pow(-2.0 * t + 2.0, 3.0) * 0.5; }
float dsfx_easeOutBack(float t)   { float c = 1.70158; float u = t - 1.0; return 1.0 + (c + 1.0) * u * u * u + c * u * u; }

// Damped spring impulse: 1 at t=0 decaying to 0 with a couple of wobbles.
float dsfx_springDecay(float t, float freq)
{
    return exp(-4.0 * t) * cos(freq * t);
}

// Evaluate a (from, to, t0, duration) state tuple against the global clock.
float dsfx_state(float4 s)
{
    float t = saturate((_DsFxTime - s.z) / max(s.w, 1e-3));
    return lerp(s.x, s.y, dsfx_easeOutCubic(t));
}

// Life snapshot: how "built" the element is (0..1) plus the raw phase data.
struct DsFxLife
{
    float built;   // 0 = fully absent, 1 = fully present (direction-aware)
    float phase;   // raw 0..1 progress of the CURRENT in/out animation
    float mode;    // 0 in, 1 idle, 2 out
    float style;   // 0 build, 1 sweep, 2 fade
};

DsFxLife dsfx_life()
{
    DsFxLife l;
    l.mode  = _FxLife.x;
    l.style = _FxLife.y;
    l.phase = saturate((_DsFxTime - _FxLife.z) / max(_FxLife.w, 1e-3));
    l.built = l.mode < 0.5 ? l.phase : (l.mode > 1.5 ? 1.0 - l.phase : 1.0);
    return l;
}

// Click impulse: 1 right after the pointer lands, decaying over ~decay seconds.
float dsfx_clickPulse(float decay)
{
    float dt = _DsFxTime - _FxClick.z;
    return (_FxClick.w > 0.001 && dt >= 0.0) ? exp(-dt / max(decay, 1e-3)) : 0.0;
}

// Expanding click ripple: returns signed ring wave at layout point p (points space).
float dsfx_clickRipple(float2 p, float2 sizePt, float speedPt, float width)
{
    float dt = _DsFxTime - _FxClick.z;
    if (_FxClick.w < 0.001 || dt < 0.0) return 0.0;
    float2 c = _FxClick.xy * sizePt;
    float d = distance(p, c);
    float r = dt * speedPt;
    float ring = exp(-abs(d - r) / max(width, 1e-3));
    return ring * exp(-dt * 2.2) * sin((d - r) * 0.35);
}

// ---------------------------------------------------------------- rounded rect

// Signed distance to the element's rounded rectangle, in points.
// p: layout position in points, origin top-left. Negative inside.
float dsfx_rrectSd(float2 p, float2 sizePt, float4 radii)
{
    float2 half_ = sizePt * 0.5;
    float2 q = p - half_;
    // pick the radius of the quadrant we are in (x: TL, y: TR, z: BR, w: BL)
    float r = q.x < 0.0 ? (q.y < 0.0 ? radii.x : radii.w)
                        : (q.y < 0.0 ? radii.y : radii.z);
    r = min(r, min(half_.x, half_.y));
    float2 d = abs(q) - half_ + r;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - r;
}

// Approximate distance to the INNER edge of the border band (for frame shading):
// how deep this point sits inside the border, 0 at the outer edge.
float dsfx_borderDepth(float2 p, float2 sizePt)
{
    float t = p.y;                 // distance from top edge
    float b = sizePt.y - p.y;      // from bottom
    float l = p.x;                 // from left
    float r = sizePt.x - p.x;      // from right
    return min(min(t / max(_FxBorder.x, 1e-3), b / max(_FxBorder.z, 1e-3)),
               min(l / max(_FxBorder.w, 1e-3), r / max(_FxBorder.y, 1e-3)));
}

// Is this point inside the border band (any side with width > 0)?
float dsfx_inBorder(float2 p, float2 sizePt)
{
    return dsfx_borderDepth(p, sizePt) < 1.0 ? 1.0 : 0.0;
}

// Surface normal for lighting: flat face that curves outward near the rounded
// edge, giving every element a soft bevel to catch light. sd: rrect distance.
// bevelWidth in points. Returns a unit normal, z toward the viewer.
// Layout v grows DOWN, so a "top-lit" light direction has NEGATIVE y.
float3 dsfx_bevelNormal(float2 p, float2 sizePt, float4 radii, float sd, float bevelWidth)
{
    // Cheap gradient of the sd field by finite differences.
    float e = 1.0;
    float dx = dsfx_rrectSd(p + float2(e, 0), sizePt, radii) - sd;
    float dy = dsfx_rrectSd(p + float2(0, e), sizePt, radii) - sd;
    float2 g = normalize(float2(dx, dy) + 1e-5);
    float k = saturate(1.0 + sd / max(bevelWidth, 1e-3)); // 0 flat center -> 1 at edge
    k = k * k;
    float tilt = k * 0.85;
    return normalize(float3(g * tilt, 1.0 - 0.6 * tilt));
}

// Shape normal adapted to the element's surface PROFILE (_FxSurface.x):
//   0 raised — the classic control bevel (identical to dsfx_bevelNormal),
//   1 panel  — a wide, gentle edge roll: sections read as big boards, not giant buttons,
//   2 well   — the bevel tips INWARD (negative tilt): the top wall shades, the
//              bottom lip catches the key light — the sunken-tray read for inputs.
float3 dsfx_shapeNormal(float2 p, float2 sizePt, float4 radii, float sd, float bevelWidth, float profile)
{
    float widthMul = profile > 1.5 ? 0.8 : (profile > 0.5 ? 2.6 : 1.0);
    float tiltMul  = profile > 1.5 ? -0.9 : (profile > 0.5 ? 0.45 : 1.0);
    float e = 1.0;
    float dx = dsfx_rrectSd(p + float2(e, 0), sizePt, radii) - sd;
    float dy = dsfx_rrectSd(p + float2(0, e), sizePt, radii) - sd;
    float2 g = normalize(float2(dx, dy) + 1e-5);
    float k = saturate(1.0 + sd / max(bevelWidth * widthMul, 1e-3));
    k = k * k;
    float tilt = k * 0.85 * tiltMul;
    return normalize(float3(g * tilt, 1.0 - 0.6 * abs(tilt)));
}

static const float3 DSFX_KEY_LIGHT = float3(-0.42, -0.62, 0.66); // top-left key, v-down space

float dsfx_diffuse(float3 n)
{
    return saturate(dot(n, normalize(DSFX_KEY_LIGHT)) * 0.5 + 0.62);
}

float dsfx_spec(float3 n, float power)
{
    // Blinn-ish with a fixed view (0,0,1); enough for stylized UI lighting.
    float3 h = normalize(normalize(DSFX_KEY_LIGHT) + float3(0, 0, 1));
    return pow(saturate(dot(n, h)), power);
}

// Time-twinkling glints: density in [0,1], cellPt = sparkle cell size in points.
float dsfx_sparkle(float2 p, float cellPt, float density, float t)
{
    float2 cell = floor(p / cellPt);
    float h = dsfx_hash12(cell);
    if (h > density) return 0.0;
    float phase = dsfx_hash12(cell + 91.7) * 6.2831;
    float tw = 0.5 + 0.5 * sin(t * (2.0 + 4.0 * dsfx_hash12(cell + 33.3)) + phase);
    float2 f = frac(p / cellPt) - 0.5;
    float d = saturate(1.0 - dot(f, f) * 6.0);
    return tw * d * d;
}

// ---------------------------------------------------------------- varyings

struct DsFxVaryings
{
    float4 pos : SV_POSITION;
    UIE_V2F_COLOR_T color : COLOR;
    float4 uvClip : TEXCOORD0;
    UIE_NOINTERPOLATION half4 typeTexSettings : TEXCOORD1;
#ifdef UNITY_PLATFORM_WEBGL
    UIE_NOINTERPOLATION float2 textCoreLoc : TEXCOORD3; // UUM-90736: no uint2 varyings on Safari
#else
    UIE_NOINTERPOLATION uint2 textCoreLoc : TEXCOORD3;
#endif
    float4 circle : TEXCOORD4;
    float4 fxData : TEXCOORD5; // xy: layout UV (0..1 across the element, v down)
                               // zw: panel-space position in points (dsfx_ownGeometry's ruler)
    UNITY_VERTEX_OUTPUT_STEREO
};

DsFxVaryings dsfx_vert(appdata_t v)
{
    v2f std = uie_std_vert(v);
    DsFxVaryings o;
    o.pos = std.pos;
    o.color = std.color;
    o.uvClip = std.uvClip;
    o.typeTexSettings = std.typeTexSettings;
    o.textCoreLoc = std.textCoreLoc;
    o.circle = std.circle;
    // Layout UV rides uv.zw (the ShaderGraph UITK channel), with v growing UP -
    // proven by scanline builds materializing from the wrong end. Flip v once
    // here so every material works in v-DOWN element space (0,0 = top-left),
    // which is what the border-width mapping and all the choreography assume.
    //
    // fxData.zw: the same vertex's PANEL-SPACE position in points — exactly the
    // point uie_std_vert projected, reproduced by applying the same dynamic
    // (bone/group) transform to the untouched input position. Reloaded here
    // rather than trusting call order to have left the uie_toWorldMat static
    // populated; it is three texel loads on a UI-sized vertex stream.
    // dsfx_ownGeometry divides this field's screen gradients by the layout-UV
    // gradients to recover the emitting rect's true size in points, which is
    // the discrimination measurement that survives ANY camera (see there).
    uie_vert_load_dynamic_transform(v);
    float2 panelPt = mul(uie_toWorldMat, v.vertex).xy;
    o.fxData = float4(v.uv.z, 1.0 - v.uv.w, panelPt);
    UNITY_TRANSFER_VERTEX_OUTPUT_STEREO(std, o);
    return o;
}

// ---------------------------------------------------------------- fragment context

struct DsFxFrag
{
    bool  isText;      // TextCore SDF glyph fragment
    bool  isSolid;     // background / border geometry
    float2 uv;         // layout UV 0..1, v down
    float2 pt;         // layout position in points
    float2 tex;        // PATTERN domain: pt + per-element anchor. Figure noise (grain,
                       // brush, banks, frost) samples THIS so every element shows its
                       // own cut of the material; geometry cues (bevels, edges,
                       // drift, env bands) stay on pt/uv.
    float2 sizePt;     // element size in points
    float2 pp;         // panel-space position in points (fxData.zw) — only dsfx_ownGeometry
                       // should need it; element-local work belongs on pt/uv/tex
    float sd;          // rounded-rect signed distance (points, negative inside), solid only
    float coverage;    // arc AA * clip-rect coverage (multiply into final alpha)
    float hover;       // eased hover state 0..1
    float press;       // eased press state 0..1
    float wear;        // 0..1
    float profile;     // 0 raised, 1 panel, 2 sunken well (_FxSurface.x)
    float focus;       // keyboard-focus flag (_FxSurface.y)
    float texGain;     // structural texture gain, 1 = full voice (_FxRect.w; 0 reads as 1)
    float idleT;       // clock for idle animation (frozen when ds-fx-static)
    DsFxLife life;
};

DsFxFrag dsfx_begin(DsFxVaryings IN)
{
    DsFxFrag f;
    half renderType = IN.typeTexSettings.x;
    f.isSolid = TestType(renderType, k_FragTypeSolid);
    f.isText  = !f.isSolid && !TestType(renderType, k_FragTypeTexture) && TestType(renderType, k_FragTypeText);
    f.uv = IN.fxData.xy;
    f.sizePt = max(_FxRect.xy, 1.0);
    f.pp = IN.fxData.zw;
    f.pt = f.uv * f.sizePt;
    f.tex = f.pt + _FxSurface.zw;
    f.profile = _FxSurface.x;
    f.focus = _FxSurface.y;
    f.texGain = _FxRect.w > 0.001 ? _FxRect.w : 1.0;
    f.sd = 0.0;
    f.coverage = 1.0;
    if (f.isSolid)
    {
        f.sd = dsfx_rrectSd(f.pt, f.sizePt, _FxRadii);
        if (TestIsArc(IN.typeTexSettings.w))
            f.coverage = ComputeCoverage(IN.circle.xy, IN.circle.zw);
    }
    f.coverage *= uie_fragment_clip(IN.uvClip.zw);
    f.hover = dsfx_state(_FxStateH);
    f.press = dsfx_state(_FxStateP);
    f.wear = _FxMode.z;
    f.life = dsfx_life();
    f.idleT = _FxMode.w > 0.5 ? 7.31 : _DsFxTime; // frozen clock still lands on a pleasant frame
    return f;
}

// ---------------------------------------------------------------- text SDF kit

struct DsFxTextSd
{
    float coverage;  // glyph pixel coverage after AA (face)
    float sdPt;      // signed distance from the glyph edge in screen px (negative inside)
    float sdRaw;     // raw SDF atlas alpha: 0.5 at the edge, rises inward. The only
                     // depth measure that is stable across font sizes/atlases — use it
                     // for groove profiles instead of sdPt.
    float2 grad;     // SDF gradient (points toward the outside of the glyph)
    float inside;    // smooth 0..1 "how deep inside the glyph" for grooves
};

// Three-tap sample through ONE slot dispatch: the 8-way texture-slot branch is
// a large macro expansion, and expanding it once instead of three times is a
// big part of keeping the whole fragment inside what FXC will compile.
#define DSFX_SAMPLE3(index) \
    a  = UNITY_SAMPLE_TEX2D(_Texture##index, uv).a; \
    ax = UNITY_SAMPLE_TEX2D(_Texture##index, uv + float2(texel.x, 0)).a; \
    ay = UNITY_SAMPLE_TEX2D(_Texture##index, uv + float2(0, texel.y)).a;

void dsfx_sampleSdf3(half index, float2 uv, float2 texel, out float a, out float ax, out float ay)
{
    UIE_BRANCH(DSFX_SAMPLE3)
}

// Sample the TextCore SDF for this glyph fragment and derive everything the
// carve/laser/solid looks need. IN must be a text fragment.
DsFxTextSd dsfx_textSd(DsFxVaryings IN)
{
    half slot = IN.typeTexSettings.y;
    TextureInfo ti = GetTextureInfo(slot);
    float2 uv = IN.uvClip.xy;

    float a, ax, ay;
    dsfx_sampleSdf3(slot, uv, ti.texelSize.xy, a, ax, ay);

    DsFxTextSd t;
    if (ti.sdfScale > 0.0)
    {
        // Stock face coverage — uie_textcore's vector sd_to_coverage reduced to the
        // face layer: distance in texels, slope from the screen-to-texture ratio
        // sharpened by the font's sharpness, dilated by the per-text extra dilate
        // that rides circle.x. The scalar sd_to_coverage this used to call gives a
        // much softer slope at UI sizes: glyph interiors plateaued near ~0.8 alpha
        // and every carve read as a pale ghost on light materials.
        float iso = IN.circle.x * 4.0 - 1.0;            // k_DilateRange / k_DilateOffset
        float ps = abs(ddx(uv.y)) + abs(ddy(uv.y));     // pixel size in texel space
        float stsr = max(ti.textureSize.y * ps, 1e-4);  // screen px per texel
        float sdTex = (a - 0.5) * ti.sdfScale + iso;    // positive inside, in texels
        t.coverage = saturate(0.5 + 2.0 * sdTex / (stsr / (ti.sharpness + 1.0)));
        t.sdPt = -sdTex / stsr;                         // signed SCREEN px, negative inside
    }
    else
    {
        t.coverage = a; // bitmap fonts: alpha is coverage; sd-based effects soften out
        t.sdPt = (0.5 - a) * 4.0;
    }
    t.sdRaw = a;
    t.grad = normalize(float2(a - ax, a - ay) + 1e-5); // points outward
    t.inside = saturate((a - 0.5) / 0.2); // atlas-spread-relative glyph interior depth
    return t;
}

// Groove lighting shared by every "carved into the surface" text treatment.
// base: the surface color this glyph is cut into. Returns the carved color.
//
// How a real routed groove reads: the groove FLOOR keeps the material (slightly
// sunk and cooled toward shadowTint in the deep middle), and the depth read
// comes from a DIRECTIONAL inner shadow — the wall between a pixel and the key
// light shades it, the opposite wall catches a lit lip. Both bands live in raw
// SDF units (t.sdRaw, 0.5 = edge), which scale with the glyph's own SDF spread:
// big titles get broad soft shadows, small labels stay legible.
half3 dsfx_carve(half3 base, DsFxTextSd t, half3 shadowTint, half3 rimTint, float depth)
{
    float2 L = normalize(DSFX_KEY_LIGHT.xy);
    float toLight = dot(t.grad, L);                             // grad points outward
    float edge = saturate(1.0 - (t.sdRaw - 0.5) / 0.26);        // 1 at the wall, 0 deep inside
    float lip  = saturate(1.0 - (t.sdRaw - 0.5) / 0.10);        // tighter band for the lit lip
    float deep = saturate((t.sdRaw - 0.56) / 0.18);

    // A uniform sink FIRST (legibility floor for thin strokes — small text would
    // otherwise be pure directional mottle), plus a constant pull toward the
    // shadow tint that softens the base pattern the way a routed groove does.
    // The directional modeling rides on top of that.
    half3 c = base * (1.0 - 0.30 * depth);
    c = lerp(c, shadowTint, (0.16 + 0.30 * deep) * depth);
    c *= 1.0 - 0.65 * depth * saturate(toLight) * edge;         // inner shadow toward the light
    c += rimTint * saturate(-toLight) * lip * 0.30 * depth;     // lit far wall
    return c;
}

// The enamel a real material surface is lettered with: cream on walnut, silver
// on steel, near-black on birch. The skin computes it C#-side (DsFxPalette
// picks the pole by surface luma and holds a contrast floor) and pushes _FxInk;
// the fallback derives a plausible light ink from ColB for materials driven
// without a skin.
half3 dsfx_ink()
{
    half3 derived = saturate(lerp(_FxColB.rgb, half3(1, 1, 1), 0.72));
    return lerp(derived, _FxInk.rgb, saturate(_FxInk.a * 2.0));
}

// Painted engraving — the default title/caption treatment for text riding a
// material, and the one to reach for first: a routed groove filled with enamel
// ink. The glyph face IS the ink; a thin wall band
// just inside the edge darkens toward the groove tint (the routed outline),
// deepest on the wall facing the key light. Depth reads from that rim alone,
// so the letters stay light and legible on any surface tone.
half3 dsfx_paintCarve(half3 ink, half3 groove, DsFxTextSd t)
{
    float2 L = normalize(DSFX_KEY_LIGHT.xy);
    float toLight = dot(t.grad, L);                       // grad points outward
    // The wall band hugs the glyph edge TIGHTLY (0.11 raw-SDF units): thin
    // strokes at caption sizes only ever reach sdRaw ~0.6, and a wider band
    // swallowed the whole stroke — hollow letters with dark hearts.
    float wall = saturate(1.0 - (t.sdRaw - 0.5) / 0.11);
    half3 c = ink * (1.0 - 0.05 * wall);
    c = lerp(c, groove, wall * wall * 0.42);
    c *= 1.0 - 0.18 * saturate(toLight) * wall;           // wall shadow toward the light
    return c;
}

// Composite a glyph face over its own glow/shadow skirt WITHOUT the divide-free
// lerp shortcut (mixing rgb toward the skirt color at partial alpha is exactly
// what painted fringes around text). This is a plain premultiplied "over",
// un-premultiplied at the end for the straight-alpha blend state.
half4 dsfx_overGlow(half3 face, float faceA, half3 glow, float glowA)
{
    float a = faceA + glowA * (1.0 - faceA);
    half3 rgb = (face * faceA + glow * glowA * (1.0 - faceA)) / max(a, 1e-4);
    return half4(rgb, a);
}

// Left-to-right write-on mask with a soft head. Returns (visible, headGlow).
float2 dsfx_writeMask(float u, float progress, float headWidth)
{
    float p = progress * (1.0 + headWidth * 2.0) - headWidth;
    float visible = smoothstep(p + headWidth, p - headWidth, u); // 1 behind the head
    float head = saturate(1.0 - abs(u - p) / max(headWidth, 1e-3));
    return float2(visible, head * head);
}

// ---------------------------------------------------------------- passthrough

// Reproduce the standard UIE look for the fragments a material leaves alone.
//
// Deliberately SLIM: solid geometry, SDF text, and a plain textured sample —
// pulling uie_std_frag's FULL machinery (gradients, SVG, dynamic atlas
// remapping) into this shader is exactly what pushed the fragment past what
// FXC survives. The texture path matters: a skinned element's own
// background-image (a checkbox tick, a radio dot, an avatar) arrives as
// texture fragments, and degrading them to the vertex tint painted them as
// SOLID SLABS — the "raw checkbox" bug. Children keep their own standard
// rendering — a material never applies to them in the first place.
UIE_FRAG_T dsfx_passthrough(DsFxVaryings IN)
{
    half renderType = IN.typeTexSettings.x;

    UIE_FRAG_T color = IN.color;
    float coverage = 1.0;

    if (TestType(renderType, k_FragTypeSolid))
    {
        if (TestIsArc(IN.typeTexSettings.w))
            coverage = ComputeCoverage(IN.circle.xy, IN.circle.zw);
    }
    else if (TestType(renderType, k_FragTypeTexture))
    {
        color *= SampleTextureSlot(IN.typeTexSettings.y, IN.uvClip.xy);
    }
    else if (!TestType(renderType, k_FragTypeTexture) && TestType(renderType, k_FragTypeText))
    {
        TextureInfo info = GetTextureInfo(IN.typeTexSettings.y);
        if (info.sdfScale > 0.0f)
        {
            SdfTextFragInput input;
            input.tint = IN.color;
            input.textureSlot = IN.typeTexSettings.y;
            input.extraDilate = IN.circle.x;
            input.uv = IN.uvClip.xy;
#ifdef UNITY_PLATFORM_WEBGL
            input.textCoreLoc = round(IN.textCoreLoc);
#else
            input.textCoreLoc = IN.textCoreLoc;
#endif
            input.opacity = IN.typeTexSettings.z;
            CommonFragOutput o = uie_std_frag_sdf_text(input);
            color = o.color; coverage = o.coverage;
        }
    }

    coverage *= uie_fragment_clip(IN.uvClip.zw);
    clip(coverage - 0.003f);
    color.a *= coverage;
    return color;
}

// ---------------------------------------------------------------- finishing

// Shared tail for material fragments: life fade, coverage, kill threshold.
UIE_FRAG_T dsfx_finish(half4 color, DsFxFrag f)
{
    // Fade life-style multiplies alpha; build/sweep styles gate spatially in
    // the material code and only soften the tail here.
    float lifeAlpha = f.life.style > 1.5 ? f.life.built : saturate(f.life.built * 4.0);
    color.a *= lifeAlpha * f.coverage;
    clip(color.a - 0.003f);
    return color;
}

// Surface chrome for the non-raised profiles (SOLID fragments only — relies
// on f.sd):
//   wells  — a deep tray: dark rim, strong inner shadow falling from the top
//            edge, softer side shading, and a lit lip along the inside bottom
//            edge where the key light catches the far wall.
//   panels — a big board: routed dark edge, a faint lit lip just inside it,
//            and a wide vignette that grounds the field toward its edges.
// Raised plates have their own chrome (dsfx_plate*) because their drop-shadow
// skirt must be carved out of the geometry BEFORE the surface is shaded.
//
// BRANCH-FREE (masks, single return) and — this is the load-bearing rule —
// callers must invoke it UNCONDITIONALLY, never from inside a profile branch.
// Line-level bisection against a real family proved that a call to THIS
// function from inside a branch reliably crashes FXC ("Lost connection with
// shader compiler"), while the identical call hoisted ABOVE the branch chain —
// or the identical math manually inlined inside the branch — compiles fine.
// The masks make an unconditional call semantically identical anyway: raised
// profiles pass through untouched. If your family dies at compile time with no
// usable message, look here first.
half3 dsfx_wellShade(half3 col, DsFxFrag f)
{
    float isWell  = step(1.5, f.profile);
    float isPanel = step(0.5, f.profile) * (1.0 - isWell);
    float d = max(-f.sd, 0.0);

    float topShade  = exp2(-f.pt.y / 6.0);
    float sideShade = exp2(-min(f.pt.x, f.sizePt.x - f.pt.x) / 4.0);
    float lipW      = exp2(-max(f.sizePt.y - f.pt.y, 0.0) / 2.1);
    float rimW      = 1.0 - smoothstep(0.35, 1.6, d);
    half3 cw = col * (1.0 - 0.46 * topShade) * (1.0 - 0.20 * sideShade);
    cw += _FxColB.rgb * lipW * 0.32;
    cw  = lerp(cw, _FxColA.rgb * 0.30, rimW * 0.62);

    float rimP = 1.0 - smoothstep(0.5, 2.4, d);
    float lipP = exp2(-abs(d - 3.6) / 1.3);
    half3 cp = col * (1.0 - 0.13 * exp2(-d / 30.0));
    cp  = lerp(cp, _FxColA.rgb * 0.42, rimP * 0.55);
    cp += _FxColB.rgb * lipP * 0.10;

    return col * (1.0 - isWell - isPanel) + cw * isWell + cp * isPanel;
}

// Keyboard-focus ring (SOLID fragments only): the accent glows in a thin band
// just inside the edge while the element is focused. The skin retargets the
// hover tuple on focus, so the ring EASES in/out; the flag gates it so a plain
// pointer hover never shows it. Distance-keyed — follows the contour.
half3 dsfx_focusRim(half3 col, DsFxFrag f)
{
    float k = f.focus * f.hover;
    if (k < 0.003) return col;
    float d = max(-f.sd, 0.0);
    float rim = exp2(-abs(d - 2.6) * 0.55);
    return col + _FxColC.rgb * rim * 0.85 * k;
}

// Text-mode helpers to keep the per-family shaders honest about modes.
bool dsfx_textCarve() { return _FxMode.y > 0.5 && _FxMode.y < 1.5; }
bool dsfx_textSolid() { return _FxMode.y > 1.5; }
bool dsfx_frameOnly() { return _FxMode.x > 0.5 && _FxMode.x < 1.5; }
bool dsfx_textOnly()  { return _FxMode.x > 1.5; }

// Disabled flag rides _FxMode.w as bit 2 (the skin adds 2 when the element is
// disabled; values are 0/1 static, 2/3 disabled+static). The idle-freeze read
// (`_FxMode.w > 0.5` in dsfx_begin) deliberately catches it too — a disabled
// control's idle animation parking is a feature, not an accident. Families use
// this for state LOOKS the muted palette alone cannot say — a dead surface that
// dulls, crazes or dusts over rather than merely losing its color.
bool dsfx_disabledFx() { return _FxMode.w > 1.5; }

// DESCENDANT DISCRIMINATION — every family's solid branch MUST start with this
// test. Minimal-repro proven, and the single easiest thing to regress.
//
// Unity 6000.5 batches DESCENDANT quads into an ancestor's custom-material
// draw. A plain solid child — a token swatch, a status dot, an active-row
// background — therefore arrives in YOUR material's isSolid branch, and if you
// shade it, it renders as a chip of your material: invisible against the panel
// around it. Everything failing the test must take dsfx_passthrough, which
// renders solids, textures and text faithfully.
//
// HOW IT TELLS: it MEASURES the size in points of whichever rect emitted this
// quad, and accepts only a match with the element's own size (_FxRect.xy).
// Layout UV spans 0..1 across the emitting rect while fxData.zw carries the
// same vertex's panel-space position in points, so along each axis
//
//     emitting size = |grad(panelPos)| / |grad(layoutUV)|
//
// Both fields sit in exact affine relation (pos = rectMin + uv * size) at
// every vertex, perspective-correct interpolation preserves that relation at
// every fragment, and the hardware takes both gradients with the SAME quad
// differencing — so the panel-to-screen mapping cancels outright. Camera
// distance, viewing angle, perspective, panel scale, DPI: all gone. This is
// what lets a material hold on a WORLD-SPACE panel as you walk toward or away
// from it. The two earlier heuristics both read the CAMERA instead of the
// geometry — an absolute pixels-per-point window (true at exactly one camera
// distance in 3D: the "visible only at mid distance" bug), then a derivative
// aspect-ratio test (equals the local screen ANISOTROPY, so near and grazing
// views pushed it out of any window) — and both are gone. The test no longer
// reads _FxRect.z at all, and one window serves both hosts.
//
// The one case this cannot catch: a FULL-BLEED solid child exactly matching
// its skinned parent emits the identical rect, so it MEASURES identical. Give
// such a child a white background-image and tint it, so it rides the texture
// path instead. (A solid quad ROTATED against an obliquely-viewed world panel
// mixes the two axes and can fall outside the window — it then renders as
// stock via the passthrough, which is the safe direction to miss.)
bool dsfx_ownGeometry(DsFxFrag f)
{
    float2 gu = float2(ddx(f.uv.x), ddy(f.uv.x));
    float2 gv = float2(ddx(f.uv.y), ddy(f.uv.y));
    float2 gx = float2(ddx(f.pp.x), ddy(f.pp.x));
    float2 gy = float2(ddx(f.pp.y), ddy(f.pp.y));
    // Degenerate layout-UV gradients (the sub-pixel AA fringe, zero-area
    // slivers) blow the estimate upward and fail the window, landing them in
    // the passthrough — the same treatment they always got.
    float ax = length(gx) / max(length(gu), 1e-7);
    float ay = length(gy) / max(length(gv), 1e-7);
    float kx = ax / f.sizePt.x;
    float ky = ay / f.sizePt.y;
    return kx > 0.70 && kx < 1.42 && ky > 0.70 && ky < 1.42;
}

// A solid fragment arriving in text-only fill mode can only be NON-BACKGROUND
// geometry: the skin clears the background in this mode (and solid text paints
// no slab), so what remains is the element's own VectorImage tessellation — a
// design-system icon wearing the material marker (the skin promotes any
// ds-icon with a text mode into fill-mode 2). Families render it as the
// material surface (ds-fx-text-solid: an icon OF the material) or painted onto
// it (ds-fx-text-carve).
//
// The paint is a UNIFORM fill toward the ink: tessellated vector geometry
// carries no per-pixel shape gradients (unlike SDF text), so there are no
// walls to shade — which is what you want anyway, so a glyph button and the
// caption beside it wear one ink.
half3 dsfx_iconStamp(half3 surface, half3 ink)
{
    return lerp(surface * 0.94, ink, 0.90);
}

// ---------------------------------------------------------------- raised plate

// The reference control read (both ideal sheets): a raised plate FLOATS on the
// panel — a soft drop shadow pools under its lower edge, a near-dark outline
// wraps it, a lit bevel runs along the key-light edges, and its base carries a
// contact shade. The shadow lives in a skirt carved out of the element's own
// outer margin (the plate is inset by the skirt), so it needs no cross-element
// tricks and follows any rounded contour at any size.
struct DsFxPlate
{
    float sd;       // plate-relative signed distance (element sd + skirt)
    float surfA;    // plate coverage with ~1px AA (0 in the skirt)
    float shadowA;  // drop-shadow alpha for skirt fragments
    float2 grad;    // outward rrect gradient (directional bands)
    float skirt;    // skirt width in points (0 = plate chrome disabled)
};

DsFxPlate dsfx_plate(DsFxFrag f)
{
    DsFxPlate p;
    float minDim = min(f.sizePt.x, f.sizePt.y);
    // Raised interactive fills only: frames shade their border band, icons are
    // vector shapes, panels/wells have their own chrome. Tiny elements keep
    // their full footprint.
    float raised = (f.profile < 0.5 && !dsfx_frameOnly() && !dsfx_textOnly()) ? 1.0 : 0.0;
    p.skirt = raised * step(18.0, minDim) * clamp(minDim * 0.07, 1.4, 2.8);
    p.sd = f.sd + p.skirt;
    p.surfA = saturate(0.5 - p.sd);
    float e = 1.0;
    float dx = dsfx_rrectSd(f.pt + float2(e, 0), f.sizePt, _FxRadii) - f.sd;
    float dy = dsfx_rrectSd(f.pt + float2(0, e), f.sizePt, _FxRadii) - f.sd;
    p.grad = normalize(float2(dx, dy) + 1e-5);
    // Shadow: the plate distance sampled a touch up-left of here — key light
    // from top-left, so the shadow pools low-right — eased across the skirt.
    float sdOff = dsfx_rrectSd(f.pt - float2(0.6, 1.7), f.sizePt, _FxRadii) + p.skirt;
    p.shadowA = p.skirt > 0.0
        ? 0.42 * exp2(-max(sdOff, 0.0) / max(p.skirt * 0.75, 0.6)) * (1.0 - p.surfA)
        : 0.0;
    return p;
}

// Outline + lit bevel + base shading, with press pushing the plate DOWN: the
// bevel dims, a top inner shadow appears, and (in compose) the shadow tightens.
half3 dsfx_plateChrome(half3 col, DsFxPlate p, DsFxFrag f, half3 rimDark, half3 bevelLit)
{
    if (p.skirt <= 0.0) return col;
    float d = max(-p.sd, 0.0);
    float outline = 1.0 - smoothstep(0.35, 1.5, d);
    float band = exp2(-max(d - 1.5, 0.0) / 1.6) * (1.0 - outline);
    float topness = saturate(-p.grad.y * 0.90 - p.grad.x * 0.25 + 0.10);
    float botness = saturate(p.grad.y * 0.85 + 0.05);
    col *= 1.0 - 0.16 * band * botness;                                        // contact base
    col *= 1.0 - 0.30 * f.press * exp2(-max(d - 1.0, 0.0) / 3.2) * topness;    // pressed-in shadow
    col += bevelLit * band * topness * (0.55 - 0.38 * f.press);                // lit top bevel
    col  = lerp(col, rimDark, outline * 0.72);
    return col;
}

// Compose the shaded plate over its own drop shadow (the skirt fragments).
half4 dsfx_plateCompose(half4 col, DsFxPlate p, DsFxFrag f)
{
    if (p.skirt <= 0.0) return col;
    float sh = p.shadowA * (1.0 - 0.45 * f.press);
    col.rgb = lerp(half3(0.010, 0.008, 0.006), col.rgb, p.surfA);
    col.a = lerp(sh * col.a, col.a, p.surfA);
    return col;
}

#endif // DS_FX_INCLUDED
