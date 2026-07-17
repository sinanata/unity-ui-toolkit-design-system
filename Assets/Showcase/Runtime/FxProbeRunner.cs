#if UNITY_EDITOR && UNITY_6000_5_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DesignSystem.Runtime.Fx;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystemHost
{
    /// <summary>
    /// Play-mode half of the material render probe (editor-only; never ships in a build).
    ///
    /// Woken by Temp/fx-probe-request.json, which FxRenderProbe wrote just before queuing the mode
    /// switch. Builds a small design-system tree per registered family, material-themes it through
    /// the real <see cref="DsFxTheme.Apply"/> path, freezes the clock, and writes one PNG per
    /// (family, variant, appearance).
    ///
    /// It asserts the two things a compile check cannot:
    ///   - the markers actually produced a SKIN (a family whose shader is missing or whose
    ///     registration never ran silently renders as stock chrome — and looks fine),
    ///   - the surface has TEXTURE (luma variance), i.e. it is a material and not a flat fill.
    /// </summary>
    internal static class FxProbeRunner
    {
        private const string RequestPath = "Temp/fx-probe-request.json";

        [Serializable] private class Request { public string outputPath; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Application.isEditor || !File.Exists(RequestPath))
                return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            File.Delete(RequestPath); // one shot — never re-trigger a later play mode
            var host = new GameObject("FxProbeRunner");
            host.AddComponent<Runner>().Request = request;
        }

        private sealed class Runner : MonoBehaviour
        {
            public Request Request;

            private const int W = 760, H = 520;
            private const float T = 100f; // frozen "now" — makes every capture reproducible

            private readonly List<string> _problems = new List<string>();
            private RenderTexture _rt;
            private UIDocument _document;
            private Texture2D _readback;

            private void Start()
            {
                // Batch runs are unfocused; without this, play mode stops pumping frames and the
                // coroutine parks forever.
                Application.runInBackground = true;
                StartCoroutine(Probe());
            }

            private IEnumerator Probe()
            {
                var outDir = Request.outputPath;
                Directory.CreateDirectory(outDir);
                Debug.Log($"[ds fx probe] runner up; {DsFxRegistry.All.Count} registered families");

                // Clone the showcase's OWN PanelSettings. Building a bare one gives you an empty
                // ThemeStyleSheet, which means no Unity default control styling at all — buttons,
                // fields and toggles then render as unstyled boxes and the probe silently measures
                // nothing. (Learned the hard way: the first version of this probe reported a happy
                // green pass over a screen with no controls on it.)
                var template = Resources.Load<PanelSettings>("DefaultPanelSettings");
                var settings = template != null
                    ? Instantiate(template)
                    : ScriptableObject.CreateInstance<PanelSettings>();
                if (template == null)
                    Problem("Resources/DefaultPanelSettings not found — controls will be unstyled and this " +
                            "probe's output is not worth looking at.");
                settings.scale = 1f;
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
                _rt.Create();
                settings.targetTexture = _rt;
                settings.scaleMode = PanelScaleMode.ConstantPixelSize;
                settings.clearColor = true;
                settings.colorClearValue = new Color32(10, 14, 20, 255);

                _document = gameObject.AddComponent<UIDocument>();
                _document.panelSettings = settings;
                _readback = new Texture2D(W, H, TextureFormat.RGBA32, false);

                DsFxManager.OverrideTime(T);

                foreach (var family in DsFxRegistry.All)
                {
                    foreach (var variant in family.Variants)
                    {
                        foreach (var light in new[] { false, true })
                        {
                            var tag = $"{family.Name}_{variant.Name}_{(light ? "light" : "dark")}";
                            DsFxManager.OverrideTime(T); // skins stamp t0 = T when they attach below
                            VisualElement panel = null;
                            try { panel = BuildScreen(family, variant.Name, light); }
                            catch (Exception e) { Problem($"{tag}: build threw: {e.Message}"); continue; }

                            // Real time, not frames. Two scheduled waits have to elapse before there
                            // is anything worth looking at: the mapper themes on a tick, and an
                            // adopting skin defers its background decision ~360ms to let the USS
                            // color transition settle. Capturing before that gives you a screen of
                            // elements that have not decided what they are yet.
                            yield return new WaitForSecondsRealtime(0.6f);

                            if (DsFx.SkinOf(panel) == null)
                            {
                                // The failure that looks like success: stock chrome, no error.
                                Problem($"{tag}: markers produced NO skin — the family's shader is missing, " +
                                        "or its registration never ran.");
                                continue;
                            }

                            // Advance the clock PAST the entrance before capturing. Skins stamp their
                            // life tuple with t0 = Now, so at the frozen instant they were built at,
                            // phase == 0 — every skinned element is mid-entrance, which for most
                            // styles means invisible. Freezing and capturing at the same T renders a
                            // screen of nothing and looks like a broken material.
                            DsFxManager.OverrideTime(T + 4f);
                            yield return null;
                            yield return null;
                            var png = Capture();
                            File.WriteAllBytes(Path.Combine(outDir, tag + ".png"), png);

                            // Measure INSIDE the skinned panel, not across the whole frame. Whole-image
                            // variance is worthless: two flat regions of different colors produce plenty
                            // of it, so a screen that rendered nothing at all passes.
                            var rect = PixelRect(panel);
                            if (rect.width < 8 || rect.height < 8)
                            {
                                Problem($"{tag}: the skinned panel measured {rect.width}x{rect.height}px — it never " +
                                        "laid out, so nothing was tested.");
                                continue;
                            }
                            var detail = LocalDetail(_readback, rect);
                            if (detail < 0.0015f)
                                Problem($"{tag}: the panel is FLAT (neighbour delta {detail:F5}) — the shader ran but " +
                                        "produced no figure. Expect grain, grid or linework, not a solid fill.");
                        }
                    }
                }

                yield return RevertProof(outDir);

                DsFxManager.OverrideTime(null);
                Finish(outDir);
            }

            /// <summary>
            /// The cycle a material picker actually performs: apply, revert, apply again. This is
            /// what makes a dropdown entry possible, and it has two failure modes worth catching.
            ///
            /// Revert must actually put the tree back — a leftover inline colour or a stale skin is
            /// invisible until the NEXT theme sits on top of it and appears to do nothing. And the
            /// second apply must take: DsFxTheme.Apply is not "switch to X", because the first pass's
            /// markers are still on the elements and hand-authored markers win, so an Apply without a
            /// Revert in front of it is silently a no-op.
            /// </summary>
            private IEnumerator RevertProof(string outDir)
            {
                var family = DsFxRegistry.All.Count > 0 ? DsFxRegistry.All[0] : null;
                if (family == null) yield break;

                DsFxManager.OverrideTime(T);
                var panel = BuildScreen(family, family.Variants[0].Name, false);
                var page = panel.parent;
                yield return new WaitForSecondsRealtime(0.6f);

                if (DsFx.SkinOf(panel) == null) { Problem("revert-proof: first apply produced no skin."); yield break; }
                DsFxManager.OverrideTime(T + 4f);
                yield return null; yield return null;
                var before = Capture();

                if (!DsFxTheme.Revert(page)) Problem("revert-proof: Revert reported nothing to undo.");
                yield return null; yield return null;

                if (DsFx.SkinOf(panel) != null)
                    Problem("revert-proof: a skin SURVIVED Revert — the element is still a material.");
                foreach (var cls in panel.GetClasses())
                    if (cls.StartsWith(DsFxSpec.Prefix, StringComparison.Ordinal))
                    {
                        Problem($"revert-proof: marker '{cls}' survived Revert — the next Apply would be a no-op.");
                        break;
                    }
                File.WriteAllBytes(Path.Combine(outDir, "_revert_after.png"), Capture());

                // Re-apply: this is the dropdown's Blueprint → palette → Blueprint path.
                DsFxManager.OverrideTime(T);
                DsFxTheme.Apply(page, family, family.Variants[0].Name);
                yield return new WaitForSecondsRealtime(0.6f);
                if (DsFx.SkinOf(panel) == null)
                {
                    Problem("revert-proof: RE-apply after Revert produced no skin — switching back to a " +
                            "material would silently do nothing.");
                    yield break;
                }
                DsFxManager.OverrideTime(T + 4f);
                yield return null; yield return null;
                var again = Capture();
                File.WriteAllBytes(Path.Combine(outDir, "_revert_reapplied.png"), again);

                // Apply → Revert → Apply must land exactly where the first apply did. The clock is
                // pinned and the layout is unchanged, so anything else means Revert left residue.
                if (!ByteEqual(before, again))
                    Problem("revert-proof: re-applied render DIFFERS from the first — Revert left residue " +
                            "behind (compare _revert_reapplied.png with the first blueprint capture).");
            }

            private static bool ByteEqual(byte[] a, byte[] b)
            {
                if (a == null || b == null || a.Length != b.Length) return false;
                for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
                return true;
            }

            /// <summary>A representative slice of the design system: a panel with a title, controls,
            /// a field, and a track — enough to exercise panel, plate, well and engraved text.</summary>
            private VisualElement BuildScreen(DsFxFamily family, string variantName, bool light)
            {
                var root = _document.rootVisualElement;
                root.Clear();
                var ds = Resources.Load<StyleSheet>("UI/Styles/DesignSystem/DesignSystem");
                if (ds != null && !root.styleSheets.Contains(ds))
                    root.styleSheets.Add(ds);

                var page = new VisualElement();
                page.AddToClassList("ds-root");
                page.style.flexGrow = 1;
                page.style.paddingTop = 16; page.style.paddingBottom = 16;
                page.style.paddingLeft = 16; page.style.paddingRight = 16;
                root.Add(page);

                var section = new VisualElement();
                section.AddToClassList("ds-section");
                page.Add(section);

                var title = new Label("MATERIALS") { };
                title.AddToClassList("ds-h2");
                section.Add(title);

                var body = new Label("Body text rides the panel and must stay readable.");
                section.Add(body);

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                section.Add(row);
                foreach (var (text, mod) in new[] { ("Primary", "ds-btn--primary"), ("Ghost", "ds-btn--ghost"), ("Danger", "ds-btn--danger") })
                {
                    var b = new Button { text = text };
                    b.AddToClassList("ds-btn");
                    b.AddToClassList(mod);
                    b.style.marginRight = 8;
                    row.Add(b);
                }

                var field = new TextField { value = "Input well" };
                field.AddToClassList("ds-input");
                section.Add(field);

                var toggle = new Toggle("Toggle") { value = true };
                toggle.AddToClassList("ds-toggle");
                section.Add(toggle);

                var slider = new Slider(0f, 1f) { value = 0.6f };
                slider.AddToClassList("ds-slider");
                section.Add(slider);

                var progress = new ProgressBar { value = 45f };
                progress.AddToClassList("ds-progress");
                section.Add(progress);

                // Theme through the REAL path — post-layout, exactly as a host would.
                DsFxManager.ThemeLight = light;
                DsFxManager.ActiveTheme = null; // native hues: the probe is about the family, not theming
                root.schedule.Execute(() => DsFxTheme.Apply(page, family, variantName)).ExecuteLater(0);
                return section;
            }

            private byte[] Capture()
            {
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _readback.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                _readback.Apply();
                RenderTexture.active = prev;
                return _readback.EncodeToPNG();
            }

            /// <summary>The element's rect in readback pixels. ReadPixels is bottom-up; UI Toolkit's
            /// worldBound is top-down.</summary>
            private RectInt PixelRect(VisualElement el)
            {
                var wb = el.worldBound;
                if (float.IsNaN(wb.width) || float.IsNaN(wb.height))
                    return new RectInt(0, 0, 0, 0);
                var ppp = el.panel?.scaledPixelsPerPoint ?? 1f;
                var x = Mathf.Clamp(Mathf.RoundToInt(wb.xMin * ppp), 0, W);
                var w = Mathf.Clamp(Mathf.RoundToInt(wb.width * ppp), 0, W - x);
                var top = Mathf.RoundToInt(wb.yMin * ppp);
                var h = Mathf.Clamp(Mathf.RoundToInt(wb.height * ppp), 0, H);
                var y = Mathf.Clamp(H - top - h, 0, H);
                h = Mathf.Clamp(h, 0, H - y);
                return new RectInt(x, y, w, h);
            }

            /// <summary>
            /// Mean absolute luma difference between neighbouring pixels inside <paramref name="rect"/>.
            ///
            /// This measures FIGURE — local, high-frequency detail — which is the thing a material has
            /// and a solid fill does not. Plain variance cannot tell the difference between a grained
            /// surface and two flat blocks side by side, and will happily pass a screen that rendered
            /// nothing.
            /// </summary>
            private static float LocalDetail(Texture2D tex, RectInt rect)
            {
                var px = tex.GetPixels(rect.x, rect.y, rect.width, rect.height);
                float Luma(int i) => 0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b;
                double sum = 0;
                var n = 0;
                for (var y = 0; y < rect.height; y++)
                {
                    for (var x = 1; x < rect.width; x++)
                    {
                        var i = y * rect.width + x;
                        sum += Mathf.Abs(Luma(i) - Luma(i - 1));
                        n++;
                    }
                }
                return n == 0 ? 0f : (float)(sum / n);
            }

            private void Problem(string message)
            {
                Debug.LogError("[ds fx probe] " + message);
                _problems.Add(message);
            }

            private void Finish(string outDir)
            {
                if (_problems.Count == 0)
                    Debug.Log($"[ds fx probe] OK — every registered family rendered a textured surface. PNGs in {outDir}");
                else
                    Debug.LogError($"[ds fx probe] {_problems.Count} PROBLEM(S) — see above.");
                if (Application.isBatchMode)
                    UnityEditor.EditorApplication.Exit(_problems.Count == 0 ? 0 : 2);
                else
                    UnityEditor.EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif
