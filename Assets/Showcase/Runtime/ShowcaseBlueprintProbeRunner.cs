#if UNITY_EDITOR && UNITY_6000_5_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Linq;
using DesignSystem.Runtime.Fx;
using Showcase.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystemHost
{
    /// <summary>
    /// Play-mode half of the showcase blueprint probe (editor-only; never ships).
    ///
    /// Boots with the real showcase scene, finds the real theme dropdown, and sets it to
    /// "Blueprint (shader)" WITH notification — i.e. down the same code path a click takes. Then it
    /// asserts the flat page is genuinely a material, and screenshots it.
    ///
    /// KNOW WHAT THIS CANNOT TELL YOU. It runs in the editor, and the editor does not strip. A
    /// managed-linker failure — the classic being a type constructed only by reflection, whose
    /// constructor no compiled call site references — passes here and throws in the WebGL build.
    /// This probe green plus a clean WebGL console is the actual bar; this probe alone is not.
    /// </summary>
    internal static class ShowcaseBlueprintProbeRunner
    {
        private const string RequestPath = "Temp/showcase-blueprint-probe.json";

        [Serializable] private class Request { public string outputPath; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!Application.isEditor || !File.Exists(RequestPath))
                return;
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            File.Delete(RequestPath);
            var host = new GameObject("ShowcaseBlueprintProbeRunner");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().Request = request;
        }

        private sealed class Runner : MonoBehaviour
        {
            public Request Request;
            private int _problems;

            private void Start()
            {
                Application.runInBackground = true;
                StartCoroutine(Probe());
            }

            private void Problem(string m) { Debug.LogError("[showcase probe] " + m); _problems++; }

            private IEnumerator Probe()
            {
                Directory.CreateDirectory(Request.outputPath);

                // The showcase builds itself over several frames (documents, fonts, HUD).
                yield return new WaitForSecondsRealtime(4f);

                var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                Debug.Log($"[showcase probe] {docs.Length} UIDocument(s) in the scene");

                DropdownField dropdown = null;
                VisualElement page = null;
                foreach (var d in docs)
                {
                    var r = d.rootVisualElement;
                    if (r == null) continue;
                    var found = r.Q<DropdownField>("theme-provider-dropdown");
                    if (found != null && dropdown == null) { dropdown = found; page = r; }
                }

                if (dropdown == null)
                {
                    Problem("theme-provider-dropdown not found in any UIDocument — cannot test the picker.");
                    Finish();
                    yield break;
                }

                Debug.Log($"[showcase probe] dropdown choices: {string.Join(" | ", dropdown.choices)}");

                if (!dropdown.choices.Contains("Blueprint (shader)"))
                {
                    Problem("'Blueprint (shader)' is NOT among the dropdown choices.");
                    Finish();
                    yield break;
                }

                var sectionsBefore = page.Query(className: "ds-section").ToList();
                Debug.Log($"[showcase probe] {sectionsBefore.Count} ds-section(s) on the page");
                Shot("before");

                // The real path: value + notify == what a click does.
                dropdown.value = "Blueprint (shader)";
                yield return new WaitForSecondsRealtime(2.5f);

                // The claim under test: the page the user is LOOKING at became a material. Before the
                // per-root epoch fix this was zero — every flat-page Apply was cancelled by the HUD's.
                var skinned = sectionsBefore.Count(s => DsFx.SkinOf(s) != null);
                Debug.Log($"[showcase probe] ds-sections skinned after selecting Blueprint: {skinned}/{sectionsBefore.Count}");
                if (skinned == 0)
                    Problem("selecting Blueprint skinned NOTHING on the flat page — the picker does nothing.");

                var themed = DsFxTheme.IsThemed(page);
                Debug.Log($"[showcase probe] DsFxTheme.IsThemed(flatRoot) = {themed}");
                if (!themed) Problem("the flat root is not registered as themed.");
                Shot("blueprint");

                // ---- world space, with the material ON --------------------------------------
                // The user's report: "blueprint does not work in worldspace mode" plus a WASM
                // null-function crash. World panels are PanelRenderers drawn in 3D — a different
                // host from the flat page's screen panel — and ApplyThemeToPanel runs PaintRoot on
                // each of them, so the material code runs there too.
                yield return WorldModeProof(dropdown);

                // And back: picking a colour palette must revert cleanly.
                dropdown.value = "Design System default";
                yield return new WaitForSecondsRealtime(2.5f);
                var stillSkinned = sectionsBefore.Count(s => DsFx.SkinOf(s) != null);
                Debug.Log($"[showcase probe] ds-sections still skinned after switching back: {stillSkinned}");
                if (stillSkinned != 0)
                    Problem($"{stillSkinned} section(s) kept their skin after switching away from Blueprint.");
                Shot("reverted");

                Finish();
            }

            /// <summary>
            /// Enter world mode with the material on, then leave. Reports what the exhibits'
            /// PanelRenderer roots actually did — a world panel is a different host from the flat
            /// page's screen panel, and everything the skin reads (pixels-per-point, worldBound)
            /// behaves differently inside one.
            /// </summary>
            private IEnumerator WorldModeProof(DropdownField dropdown)
            {
                var corridor = UnityEngine.Object.FindFirstObjectByType<WorldSpaceCorridor>();
                if (corridor == null)
                {
                    Problem("no WorldSpaceCorridor in the scene — cannot test the mode toggle.");
                    yield break;
                }

                Debug.Log("[showcase probe] entering WORLD mode with Blueprint on...");
                var threw = false;
                try { corridor.Show(); }
                catch (Exception e) { threw = true; Problem("corridor.Show() THREW with the material on: " + e); }
                if (threw) yield break;

                // Exhibits build over several frames, then each gets PaintRoot'd.
                yield return new WaitForSecondsRealtime(5f);

                var roots = corridor.ExhibitRoots.ToList();
                Debug.Log($"[showcase probe] world exhibits: {roots.Count}");
                if (roots.Count == 0)
                {
                    Problem("no exhibit roots came online — cannot judge world-space material.");
                    yield break;
                }

                var themedRoots = roots.Count(r => DsFxTheme.IsThemed(r));
                var worldSections = roots.Sum(r => r.Query(className: "ds-section").ToList().Count);
                var worldSkinned = roots.Sum(r => r.Query(className: "ds-section").ToList().Count(s => DsFx.SkinOf(s) != null));
                Debug.Log($"[showcase probe] world roots themed: {themedRoots}/{roots.Count}");
                Debug.Log($"[showcase probe] world ds-sections skinned: {worldSkinned}/{worldSections}");

                // Report the panel facts the skin depends on. A world panel's pixels-per-point and
                // worldBound are the prime suspects for "renders stock in world space".
                var probeRoot = roots[0];
                var ppp = probeRoot.panel?.scaledPixelsPerPoint ?? -1f;
                var firstSection = probeRoot.Query(className: "ds-section").First();
                Debug.Log($"[showcase probe] world panel scaledPixelsPerPoint = {ppp}");
                if (firstSection != null)
                    Debug.Log($"[showcase probe] world section worldBound={firstSection.worldBound} " +
                              $"resolved={firstSection.resolvedStyle.width}x{firstSection.resolvedStyle.height}");

                // The world-space detection the skin's gate AND the _FxRect.z sign flip both hang
                // off: the panel must cast to IRuntimePanel and carry WorldSpace settings. If this
                // line ever reports otherwise, the gate is a no-op and the sign never flips.
                var runtimePanel = probeRoot.panel as IRuntimePanel;
                var settings = runtimePanel?.panelSettings;
                Debug.Log($"[showcase probe] world panel: IRuntimePanel={(runtimePanel != null)} " +
                          $"panelSettings={(settings != null ? settings.name : "null")} " +
                          $"renderMode={(settings != null ? settings.renderMode.ToString() : "?")} " +
                          $"AllowWorldSpacePanels={DsFxManager.AllowWorldSpacePanels}");
                if (runtimePanel == null || settings == null || settings.renderMode != PanelRenderMode.WorldSpace)
                    Problem("world panel does NOT report as a world-space IRuntimePanel — the skin's " +
                            "world detection (gate + _FxRect.z sign) is blind here.");

                if (worldSkinned == 0)
                    Problem("world-space exhibits got NO skins - Blueprint does nothing in world mode.");

                Shot("world_blueprint");

                // The reported bug lived HERE: material correct at one distance, gone when you
                // walked closer or farther. Sweep the camera through those poses and hold the
                // material to all of them.
                yield return DistanceProof();

                // Second reported repro: picking themes WHILE STANDING IN THE CORRIDOR. The flat
                // page is hidden then (its repaint is deliberately skipped), so the fan-out has to
                // reach the exhibits through the corridor's own wave — assert it actually does, in
                // both directions.
                Debug.Log("[showcase probe] in-world theme cycling...");
                dropdown.value = "Design System default";
                yield return new WaitForSecondsRealtime(3.5f);
                var offSkinned = corridor.ExhibitRoots.Sum(r =>
                    r.Query(className: "ds-section").ToList().Count(s => DsFx.SkinOf(s) != null));
                Debug.Log($"[showcase probe] world sections skinned after in-world default: {offSkinned}");
                if (offSkinned != 0)
                    Problem($"in-world switch to default left {offSkinned} world section(s) skinned.");

                dropdown.value = "Blueprint (shader)";
                yield return new WaitForSecondsRealtime(4f);
                var onSkinned = corridor.ExhibitRoots.Sum(r =>
                    r.Query(className: "ds-section").ToList().Count(s => DsFx.SkinOf(s) != null));
                Debug.Log($"[showcase probe] world sections skinned after in-world Blueprint: {onSkinned}");
                if (onSkinned == 0)
                    Problem("selecting Blueprint IN WORLD MODE skinned nothing on the exhibits — " +
                            "the picker only reaches the HUD (the user-reported bug).");
                Shot("world_reapplied");

                Debug.Log("[showcase probe] leaving WORLD mode...");
                try { corridor.Hide(); }
                catch (Exception e) { Problem("corridor.Hide() THREW with the material on: " + e); }
                yield return new WaitForSecondsRealtime(3f);
                Shot("world_back_to_screen");
            }

            /// <summary>
            /// The regression proof for "visible at mid distance, invisible close and far": the
            /// own-geometry test must hold wherever the camera stands, so render exhibit 0 from
            /// right against it, from a comfortable viewing spot, and from the corridor entrance
            /// at a grazing angle — and require the material to survive all three. When the test
            /// goes camera-dependent, solid fragments take the passthrough and the exhibit
            /// collapses to its dark underlay slab, which reads here as the panel region losing
            /// roughly half its brightness against the mid pose.
            /// </summary>
            private IEnumerator DistanceProof()
            {
                var cam = Camera.main;
                var anchorGo = GameObject.Find("Exhibit_0");
                if (cam == null || anchorGo == null)
                {
                    Problem("distance-proof: Camera.main or the Exhibit_0 anchor is missing — cannot sweep.");
                    yield break;
                }

                // Park the walker: the sweep drives the camera directly.
                var player = cam.GetComponent<FirstPersonController>();
                var hadPlayer = player != null && player.enabled;
                if (player != null) player.enabled = false;
                var pos0 = cam.transform.position;
                var rot0 = cam.transform.rotation;

                var anchor = anchorGo.transform.position;
                var readable = anchorGo.transform.rotation * Vector3.back; // exhibits read on local -Z
                var poses = new[]
                {
                    ("dist_close", anchor + readable * 0.85f),
                    ("dist_mid",   anchor + readable * 3.0f),
                    ("dist_far",   new Vector3(0.6f, 1.75f, Mathf.Min(anchor.z - 7.0f, 0.5f))),
                };

                // Explicit camera renders into an offscreen target: ScreenCapture needs a
                // genuinely rendering game view, which a -batchmode editor does not have (the
                // screenshot files simply never land — measured, not assumed). A render REQUEST
                // is driven by us, works headless, and the exhibits are ordinary 3D geometry in
                // the camera's frustum, so they are in the frame either way.
                var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                rt.Create();
                var readback = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);

                var luma = new float[poses.Length];
                var detail = new float[poses.Length];
                for (var i = 0; i < poses.Length; i++)
                {
                    cam.transform.position = poses[i].Item2;
                    cam.transform.rotation = Quaternion.LookRotation(anchor - poses[i].Item2, Vector3.up);
                    yield return null; // let transforms and UITK settle a frame

                    var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
                    if (!UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        Problem("distance-proof: the active pipeline rejects StandardRequest renders — cannot sweep.");
                        break;
                    }
                    // Keep targetTexture assigned while measuring so WorldToScreenPoint speaks
                    // in RT pixels.
                    cam.targetTexture = rt;
                    request.destination = rt;
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, request);

                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    readback.Apply();
                    RenderTexture.active = prev;

                    MeasurePanelRegion(readback, cam, anchorGo.transform, out luma[i], out detail[i]);
                    cam.targetTexture = null;
                    File.WriteAllBytes(Path.Combine(Request.outputPath, poses[i].Item1 + ".png"),
                        readback.EncodeToPNG());
                    Debug.Log($"[showcase probe] {poses[i].Item1}: panel mean luma={luma[i]:F4} " +
                              $"neighbour delta={detail[i]:F5}");
                }

                Destroy(readback);
                rt.Release();
                Destroy(rt);
                cam.targetTexture = null;
                cam.transform.SetPositionAndRotation(pos0, rot0);
                if (player != null) player.enabled = hadPlayer;

                if (luma[1] < 0.02f)
                {
                    Problem($"distance-proof: mid-pose panel is BLACK (luma {luma[1]:F4}) — nothing rendered, " +
                            "the sweep proves nothing.");
                    yield break;
                }
                // Vacuity guard: a wall or an empty render is FLAT. The blueprint surface always
                // carries figure (tooth + graph grid + linework), so a mid pose with no local
                // detail means the exhibits are not actually in this render — report instead of
                // green-lighting a sweep over nothing.
                if (detail[1] < 0.0012f)
                {
                    Problem($"distance-proof: mid-pose panel region shows NO figure (neighbour delta {detail[1]:F5}) — " +
                            "either the exhibits are absent from the explicit render or the material is off; " +
                            "the sweep is inconclusive.");
                    yield break;
                }
                if (luma[0] < luma[1] * 0.55f)
                    Problem($"distance-proof: material DIMS OUT close up ({luma[0]:F4} vs mid {luma[1]:F4}) — " +
                            "the own-geometry test has gone camera-dependent again.");
                if (luma[2] < luma[1] * 0.55f)
                    Problem($"distance-proof: material DIMS OUT far away ({luma[2]:F4} vs mid {luma[1]:F4}) — " +
                            "the own-geometry test has gone camera-dependent again.");
            }

            /// <summary>Mean luma and neighbour delta inside the screen projection of a 0.84 m
            /// square centred on the exhibit anchor — safely inside every fitted exhibit, so the
            /// measurement tracks the material surface rather than the wall around it.</summary>
            private static void MeasurePanelRegion(Texture2D tex, Camera cam, Transform anchor,
                out float meanLuma, out float detail)
            {
                meanLuma = 0f;
                detail = 0f;
                var right = anchor.rotation * Vector3.right;
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                for (var sx = -1; sx <= 1; sx += 2)
                for (var sy = -1; sy <= 1; sy += 2)
                {
                    var p = cam.WorldToScreenPoint(anchor.position + right * (0.42f * sx) + Vector3.up * (0.42f * sy));
                    if (p.z <= 0f)
                        return; // corner behind the camera; zeros trip the caller's sanity check
                    min = Vector2.Min(min, new Vector2(p.x, p.y));
                    max = Vector2.Max(max, new Vector2(p.x, p.y));
                }
                var x0 = Mathf.Clamp(Mathf.RoundToInt(min.x), 0, tex.width - 1);
                var x1 = Mathf.Clamp(Mathf.RoundToInt(max.x), 0, tex.width - 1);
                var y0 = Mathf.Clamp(Mathf.RoundToInt(min.y), 0, tex.height - 1);
                var y1 = Mathf.Clamp(Mathf.RoundToInt(max.y), 0, tex.height - 1);
                if (x1 - x0 < 12 || y1 - y0 < 12)
                    return;

                var w = x1 - x0 + 1;
                var h = y1 - y0 + 1;
                var px = tex.GetPixels(x0, y0, w, h);
                double sum = 0, dsum = 0;
                var dn = 0;
                for (var y = 0; y < h; y++)
                {
                    var prev = 0f;
                    for (var x = 0; x < w; x++)
                    {
                        var c = px[y * w + x];
                        var l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
                        sum += l;
                        if (x > 0) { dsum += Mathf.Abs(l - prev); dn++; }
                        prev = l;
                    }
                }
                meanLuma = (float)(sum / (w * h));
                detail = dn > 0 ? (float)(dsum / dn) : 0f;
            }

            private void Shot(string tag)
            {
                var path = Path.Combine(Request.outputPath, tag + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[showcase probe] shot -> {path}");
            }

            private void Finish()
            {
                // CaptureScreenshot writes at end of frame; give it a beat before exiting.
                StartCoroutine(Exit());
            }

            private IEnumerator Exit()
            {
                yield return new WaitForSecondsRealtime(2f);
                if (_problems == 0) Debug.Log("[showcase probe] OK - the Blueprint picker works end to end.");
                else                Debug.LogError($"[showcase probe] {_problems} PROBLEM(S).");
                if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(_problems == 0 ? 0 : 2);
                else UnityEditor.EditorApplication.isPlaying = false;
            }
        }
    }
}
#endif
