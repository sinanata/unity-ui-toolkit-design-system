#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// The whole per-frame cost of the FX system lives here: ONE global float.
    ///
    /// Every animation — an idle ripple, a hover sheen, a click impulse, an entrance — is a pure
    /// function of <c>_DsFxTime</c> evaluated in the shader against (from, to, t0, duration)
    /// tuples stamped at event time. No per-element updates, no repaint scheduling, no render
    /// textures. The ticker writes the clock once per frame; skins read <see cref="Now"/> when
    /// they stamp a state change, so CPU and GPU agree on what time it is.
    ///
    /// That design is why a screen full of animated materials costs the CPU nothing between
    /// events — and why <see cref="OverrideTime"/> works: pin the clock and every rendered frame
    /// becomes a pure function of (uniforms, clock), so two captures of the same instant are
    /// byte-identical. Reproducible rendering is a consequence of the architecture, not a feature
    /// bolted onto it.
    /// </summary>
    public static class DsFxManager
    {
        private static readonly int TimeProp = Shader.PropertyToID("_DsFxTime");
        private static readonly int LiteProp = Shader.PropertyToID("_DsFxLite");
        private static readonly Dictionary<DsFxFamily, Material> Materials = new Dictionary<DsFxFamily, Material>();
        private static DsFxTicker _ticker;
        private static float? _timeOverride;

        public static float Now => _timeOverride ?? Time.unscaledTime;

        /// <summary>
        /// The theme a tone-marked skin aligns to (null = native material hues) and the appearance
        /// driving the tone ladder's direction. Set BOTH before applying markers or themes — skins
        /// read them when they attach. Elements with no tone marker ignore both entirely.
        /// </summary>
        public static DsFxThemeColors ActiveTheme;
        public static bool ThemeLight;

        /// <summary>
        /// Materials on WORLD-SPACE panels (<c>PanelRenderMode.WorldSpace</c>). On by default
        /// everywhere, with one exception that is an engine version, not a preference: a WebGL
        /// player running on an engine that predates Unity's fix for the staged-updater overrun.
        ///
        /// On Unity 6000.5 through 6000.6.1f1 a world panel carrying custom materials drives the
        /// renderer past the end of its own geometry buffer in a WebGL (or WebGPU) player:
        ///   <c>GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=590904 srcSize=589824)</c>
        /// <c>GpuUpdaterStaged</c>, the updater of platforms without mapped GPU buffers, sized its
        /// per-frame staging buffer from a data set's PRE-consolidation dirty count, then grew the
        /// ranges after sizing (consolidation fills the gaps of a 90%-dense span; index ranges align
        /// outward to even) and read past its own buffer. A full-tree material apply over a large
        /// world UI is exactly that kind of burst. No host-side pacing avoids it, because a danger
        /// window sits just below every size tier.
        ///
        /// We root-caused it from the decompiled renderer on 2026-07-17 and reported it. Unity
        /// fixed it as UUM-151098 in 6000.6.2f1 and 6000.7.0b1 by consolidating first and sizing
        /// every staging request exactly (<see cref="DsUirStagingFix"/>). 6000.5 is Won't Fix, so
        /// <c>Tools/UirStagingPatch</c> remains the build-machine fix for that line.
        ///
        /// So the default is decided per engine, at first read: <c>true</c> in the editor, in
        /// native players, in a WebGL player whose engine carries Unity's fix, and in one built on
        /// a machine with the patch (<c>BuildCli</c> detects it and defines
        /// <c>DS_UIR_STAGING_PATCHED</c>); <c>false</c> only in a WebGL player on an unfixed,
        /// unpatched engine. Assigning the property overrides the default for the session, which
        /// is what the showcase's <c>?worldfx=1</c> does.
        ///
        /// The rendering itself is host-independent: dsfx_ownGeometry measures the emitting rect's
        /// size from the geometry, so a material holds at any camera distance or angle. Where this
        /// is false, a skin on a world panel simply does not attach and the element renders as
        /// stock design system: no crash, no half-applied material.
        /// </summary>
        public static bool AllowWorldSpacePanels
        {
            get => _allowWorldSpacePanels ??= DefaultAllowWorldSpacePanels();
            set => _allowWorldSpacePanels = value;
        }

        private static bool? _allowWorldSpacePanels;

        private static bool DefaultAllowWorldSpacePanels()
        {
#if UNITY_WEBGL && !UNITY_EDITOR && !DS_UIR_STAGING_PATCHED
            return DsUirStagingFix.EngineHasFix;
#else
            return true;
#endif
        }

        /// <summary>
        /// Lite rendering, for entry-level GPUs. Every material keeps its form (shape, bevel,
        /// chrome, edges, state, entrance) and drops most of its procedural figure, the part
        /// that dominates its per-pixel cost. The foundation's noise falls to one octave, its
        /// glints draw nothing, and a family tests <c>dsfx_lite()</c> to skip more of its own
        /// (see the lite rendering section of <c>DsFx.cginc</c>). On a phone with an entry-level
        /// GPU, a few large skinned panels drawn in full can take a large share of the frame.
        ///
        /// The default is decided at first read: on where <see cref="IsEntryLevelGpu"/> says so
        /// for this device's GPU, off everywhere else. Assigning the property overrides the
        /// default for the session, for example from a host's own graphics preset. One global
        /// shader float carries it (<c>_DsFxLite</c>), so switching needs no re-skin and takes
        /// effect on the next frame.
        /// </summary>
        public static bool Lite
        {
            get => _lite ??= IsEntryLevelGpu(SystemInfo.graphicsDeviceName);
            set
            {
                _lite = value;
                ApplyLite();
            }
        }

        private static bool? _lite;
        private static float _liteApplied = -1f;

        /// <summary>
        /// Entry-level phone and tablet GPUs, read from a renderer string
        /// (<see cref="SystemInfo.graphicsDeviceName"/>, also the unmasked string a WebGL
        /// player sees through ANGLE): Adreno 3xx to 5xx except the 530 and 540, Mali-4xx and
        /// Mali-T (Utgard and Midgard), the small Bifrost Mali-G31, G51 and G52, and PowerVR
        /// GE7xxx and GE8xxx. Everything else is treated as capable, desktop GPUs included.
        /// </summary>
        public static bool IsEntryLevelGpu(string deviceName)
        {
            if (string.IsNullOrEmpty(deviceName))
                return false;
            string g = deviceName.ToLowerInvariant();
            var adreno = System.Text.RegularExpressions.Regex.Match(g, @"adreno[^0-9]*([0-9]{3})");
            if (adreno.Success && int.TryParse(adreno.Groups[1].Value, out int model))
                return model < 600 && model != 530 && model != 540;
            if (g.Contains("mali-4") || g.Contains("mali-t"))
                return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(g, @"mali-g(31|51|52)\b"))
                return true;
            return g.Contains("powervr") && (g.Contains("ge7") || g.Contains("ge8"));
        }

        private static void ApplyLite()
        {
            float value = Lite ? 1f : 0f;
            if (value == _liteApplied)
                return;
            _liteApplied = value;
            Shader.SetGlobalFloat(LiteProp, value);
            Diag($"lite rendering {(value > 0f ? "on" : "off")} (gpu '{SystemInfo.graphicsDeviceName}')");
        }

        /// <summary>
        /// Narrate the material pipeline to the console: skin attachment decisions, material
        /// resolution, deferred-action ticks, theme fan-out. For chasing player-only failures
        /// (the showcase turns it on with <c>?fxdiag=1</c>); each site logs a handful of times,
        /// not per frame.
        /// </summary>
        public static bool Diagnostics;

        internal static int DiagBudget = 400;

        /// <summary>One line into the diagnostics narration (no-op unless <see cref="Diagnostics"/>;
        /// self-limits). Public so host tooling — the showcase, probes — can narrate its own steps
        /// into the same stream.</summary>
        public static void Diag(string message)
        {
            if (!Diagnostics || DiagBudget <= 0)
                return;
            DiagBudget--;
            Debug.Log("[ds fx diag] " + message);
        }

        /// <summary>Pin the FX clock. Pass null to hand it back to real time.</summary>
        public static void OverrideTime(float? seconds)
        {
            _timeOverride = seconds;
            if (seconds.HasValue)
                Shader.SetGlobalFloat(TimeProp, seconds.Value);
        }

        /// <summary>
        /// The shared material for a family. Per-element looks ride MaterialDefinition property
        /// overrides, so ONE material object serves every element of the family — the element
        /// count does not multiply materials.
        /// </summary>
        public static Material MaterialFor(DsFxFamily family)
        {
            if (family == null)
                return null;
            if (Materials.TryGetValue(family, out var mat) && mat != null)
                return mat;

            var shader = string.IsNullOrEmpty(family.ShaderName) ? null : Shader.Find(family.ShaderName);
            if (shader == null && !string.IsNullOrEmpty(family.ShaderResource))
                shader = Resources.Load<Shader>(family.ShaderResource);
            if (shader == null)
            {
                Debug.LogWarning($"[ds fx] shader for '{family.Name}' not found (tried Shader.Find(\"{family.ShaderName}\") " +
                                 $"and Resources.Load(\"{family.ShaderResource}\")); elements keep their standard look. " +
                                 "A shader outside a Resources folder is stripped from player builds — that is the usual cause.");
                return null;
            }

            Diag($"MaterialFor '{family.Name}': shader='{shader.name}' supported={shader.isSupported}");
            mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "DsFx-" + family.Name };
            Materials[family] = mat;
            // The rendering quality is global, like the clock: in place before the first skinned draw.
            ApplyLite();
            EnsureTicker();
            return mat;
        }

        /// <summary>
        /// Drop a family's shared material.
        ///
        /// The cache is keyed by the family OBJECT and never evicts, which is right for a fixed
        /// book — every family lives as long as the app. A host that registers families per loaded
        /// content retires family objects as it goes, and without this each retired one leaves its
        /// Material and its compiled shader variants resident for the rest of the session.
        ///
        /// Call it alongside <see cref="DsFxRegistry.Remove"/>, and BEFORE it: the cache has no
        /// handle to evict with once the name is gone.
        /// </summary>
        public static void Forget(DsFxFamily family)
        {
            if (family == null || !Materials.TryGetValue(family, out var mat))
                return;
            Materials.Remove(family);
            if (mat != null)
                Object.DestroyImmediate(mat);
        }

        private static void EnsureTicker()
        {
            if (_ticker != null || !Application.isPlaying)
                return;
            var host = new GameObject("DsFxTicker") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(host);
            _ticker = host.AddComponent<DsFxTicker>();
        }

        // ---- scheduler-independent deferral ----------------------------------------------
        //
        // VisualElement.schedule is PER-PANEL, and a world-space PanelRenderer panel does not
        // tick its scheduler in a player — the showcase corridor drives its own spinners for
        // exactly that reason, and material theming deferred through such a scheduler silently
        // never ran on world exhibits in a WebGL player (while the editor, which ticks every
        // panel, stayed green). Anything the FX pipeline defers — the post-layout theme Apply,
        // a skin's settled adoption re-read, drag-ghost rewires — must therefore ride the
        // ticker: a plain MonoBehaviour.Update that runs wherever the FX system runs at all.

        private struct Deferred
        {
            public float Due;
            public Action Action;
        }

        private static readonly List<Deferred> DeferredActions = new List<Deferred>();

        /// <summary>
        /// Run <paramref name="action"/> after <paramref name="seconds"/> of unscaled time,
        /// independent of any panel's scheduler (see the note above — world-space panels do not
        /// tick theirs in a player). Runs on the main thread from the FX ticker; outside play
        /// mode, where no ticker exists, the action runs inline.
        /// </summary>
        public static void RunAfter(float seconds, Action action)
        {
            if (action == null)
                return;
            EnsureTicker();
            if (_ticker == null)
            {
                action();
                return;
            }
            DeferredActions.Add(new Deferred
            {
                Due = Time.unscaledTime + Mathf.Max(0f, seconds),
                Action = action,
            });
            Diag($"RunAfter queued ({seconds:F2}s, {DeferredActions.Count} pending, ticker={(_ticker != null)})");
        }

        internal static void Tick()
        {
            if (!_timeOverride.HasValue)
                Shader.SetGlobalFloat(TimeProp, Time.unscaledTime);
            ApplyLite();
            if (DeferredActions.Count == 0)
                return;
            var now = Time.unscaledTime;
            for (var i = DeferredActions.Count - 1; i >= 0; i--)
            {
                if (DeferredActions[i].Due > now)
                    continue;
                var action = DeferredActions[i].Action;
                DeferredActions.RemoveAt(i);
                try { action(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }

    /// <summary>One SetGlobalFloat per frame (and one more when lite rendering changes); that is the entire runtime loop of the FX system.</summary>
    [DefaultExecutionOrder(-4000)]
    internal sealed class DsFxTicker : MonoBehaviour
    {
        private void Update() => DsFxManager.Tick();
    }
}
#endif
