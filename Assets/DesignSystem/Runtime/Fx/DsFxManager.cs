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
        /// everywhere EXCEPT a WebGL player, and that one exception is a bug report, not a
        /// preference.
        ///
        /// On Unity 6000.5 a world panel carrying custom materials drives the renderer past the end
        /// of its own geometry buffer in a WebGL player:
        ///   <c>GfxDevice::CopyBufferRanges: range reads out of bounds (srcEnd=590904 srcSize=589824)</c>
        /// The mechanism (read out of the decompiled UIR renderer, 2026-07-17): platforms without
        /// mapped GPU buffers — the WebGL AND WebGPU players — update UI geometry through
        /// <c>GpuUpdaterStaged</c>, which sizes its per-frame staging buffer from a data set's
        /// PRE-consolidation dirty count. Its padded size tiers stop at 256Ki elements; a bigger
        /// one-frame burst gets an EXACTLY-sized buffer with zero slack, and the updater then grows
        /// its ranges AFTER sizing (consolidation fills the gaps of a ≥90%-dense span; index ranges
        /// align outward to even) and reads past its own staging buffer. A full-tree material
        /// apply/revert over a large world UI is exactly such a burst: dense, gap-riddled, and far
        /// past the last tier. Editor and native players use mapped buffers and never run this code.
        ///
        /// No host-side pacing fully avoids it: the danger windows sit just below EVERY tier
        /// (~7.4-8.2Ki elements at the first one — a single big component's restyle), so chunking
        /// merely moves bursts between windows. The airtight fix is a one-instruction patch to the
        /// module itself on the BUILD machine — <c>Tools/UirStagingPatch</c> pads the staging
        /// request to 2r+64, which covers every post-sizing growth. The default stays opted out on
        /// WebGL because an unpatched build machine still ships the crash; a project built with the
        /// patch applied can opt in (the showcase's <c>?worldfx=1</c> does).
        ///
        /// The rendering itself is host-independent: dsfx_ownGeometry measures the emitting rect's
        /// size from the geometry, so a material holds at any camera distance or angle. When the
        /// engine bug is fixed, deleting the #if below is the whole change.
        ///
        /// Where it is false, a skin on a world panel simply does not attach and the element renders
        /// as stock design system — no crash, no half-applied material.
        /// </summary>
        /// <remarks>
        /// DS_UIR_STAGING_PATCHED is injected per-build by the showcase's BuildCli when it detects
        /// that the build machine's WebGL playback module carries the Tools/UirStagingPatch fix —
        /// such a player has no overrun left to guard against, so world panels default ON there
        /// too. An unpatched build keeps the gate and the <c>?worldfx=1</c> escape hatch.
        /// </remarks>
#if UNITY_WEBGL && !UNITY_EDITOR && !DS_UIR_STAGING_PATCHED
        public static bool AllowWorldSpacePanels;
#else
        public static bool AllowWorldSpacePanels = true;
#endif

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
            EnsureTicker();
            return mat;
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

    /// <summary>One SetGlobalFloat per frame; that is the entire runtime loop of the FX system.</summary>
    [DefaultExecutionOrder(-4000)]
    internal sealed class DsFxTicker : MonoBehaviour
    {
        private void Update() => DsFxManager.Tick();
    }
}
#endif
