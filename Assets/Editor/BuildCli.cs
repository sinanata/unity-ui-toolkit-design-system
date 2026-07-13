using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UIDocumentDesignSystem.BuildTools
{
    // Unity batchmode entry points for the showcase build orchestrator.
    // Paired with Tools/Build/Build-Showcase.ps1 (Windows). Every method:
    //   1. Reads CLI flags from Environment.GetCommandLineArgs.
    //   2. Performs the build / action.
    //   3. Writes a JSON report to -cliReportPath so the orchestrator can
    //      validate success without scraping the log.
    //   4. Calls EditorApplication.Exit(0 on success / 1 on failure).
    public static class BuildCli
    {
        const string SCENE_PATH = "Assets/Showcase/Showcase.unity";

        // -executeMethod UIDocumentDesignSystem.BuildTools.BuildCli.BuildWebGL
        public static void BuildWebGL()
        {
            var report = new BuildReportData();
            try
            {
                var args = ParseArgs();
                string buildDir   = args.Get("-cliBuildPath", "build/WebGL");
                string reportPath = args.Get("-cliReportPath", "Tools/Build/output/report-BuildWebGL.json");

                Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
                report.reportPath = reportPath;

                // Defensive — even though ProjectSettings already sets these,
                // a contributor editing the asset by hand could regress. Re-
                // assert at build time so the orchestrator output is stable
                // regardless of how the asset got modified.
                PlayerSettings.WebGL.compressionFormat   = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = true;
                PlayerSettings.WebGL.template            = "PROJECT:ShowcaseTemplate";

                // Relax float→int conversion traps to match other platforms'
                // semantics. NOTE what this does NOT do: wasm i32 division by
                // zero traps at the VM level regardless of this setting — the
                // "RuntimeError: divide by zero" seen on repeated Screen/World
                // toggles survived Ignore just fine. The actual fix for that is
                // structural, in WorldSpaceCorridor.SetCorridorVisible: world
                // panels are never torn down / rebuilt (a rebuilt panel resolves
                // 0×0 for a frame, and that zero reaches an integer division in
                // engine layout). Keep Ignore anyway; it is the saner semantics.
                PlayerSettings.WebGL.wasmArithmeticExceptions = WebGLWasmArithmeticExceptions.Ignore;

                // The corridor's runtime materials clone the CorridorLit asset —
                // URP Simple Lit with _EMISSION enabled. Note it is the
                // KeepAlive renderer in Showcase.unity referencing this material
                // that defeats URP's "strip unused variants" pass (which zeroes
                // any URP-family shader no build-scene renderer uses — Resources
                // and Always-Included don't protect on 6000.5; that pass is what
                // rendered the corridor magenta twice). The asset is committed;
                // this self-heals if it's deleted or on the wrong shader.
                EnsureCorridorMaterial();

                var opts = new BuildPlayerOptions
                {
                    scenes           = new[] { SCENE_PATH },
                    locationPathName = buildDir,
                    target           = BuildTarget.WebGL,
                    targetGroup      = BuildTargetGroup.WebGL,
                    options          = BuildOptions.None,
                };

                Debug.Log($"[BuildCli] Building WebGL → {buildDir}");
                BuildReport result = BuildPipeline.BuildPlayer(opts);

                report.success     = result.summary.result == BuildResult.Succeeded;
                report.message     = result.summary.result.ToString();
                report.sizeBytes   = (long)result.summary.totalSize;
                report.durationSec = (float)result.summary.totalTime.TotalSeconds;
                report.indexPath   = Path.Combine(buildDir, "index.html");

                if (report.success) StampAndCacheBust(report.indexPath);

                if (!report.success)
                {
                    foreach (var step in result.steps)
                    foreach (var msg in step.messages)
                    {
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            report.message += "\n  " + msg.content;
                    }
                }
            }
            catch (Exception ex)
            {
                report.success = false;
                report.message = "Exception: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
            finally
            {
                WriteReport(report);
                EditorApplication.Exit(report.success ? 0 : 1);
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Append a per-build token to the loader / data / wasm URLs in the emitted index.html, and
        /// record the build time in a global the page can read.
        ///
        /// Without this you cannot trust what you are looking at. A WebGL build re-emits the same four
        /// filenames every time, and the assets are large and slow-moving, so a browser has every
        /// reason to reuse what it already has: `python -m http.server` sends no `Cache-Control` at all
        /// (which licenses heuristic caching), and Unity's own loader additionally keeps `.data` in
        /// IndexedDB, which a hard refresh does not clear. Ship a genuine fix, reload, see the bug, and
        /// you will go looking for a bug that is no longer there — which is exactly the hour this cost.
        /// A changing query string makes each build a different URL, so there is nothing to reuse.
        /// </summary>
        static void StampAndCacheBust(string indexPath)
        {
            try
            {
                if (!File.Exists(indexPath)) return;

                var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var html  = File.ReadAllText(indexPath);

                // The loader <script src> is rewritten alongside the three fetched assets: a cached
                // loader would go on requesting the un-suffixed names and undo the whole exercise.
                html = System.Text.RegularExpressions.Regex.Replace(
                    html,
                    @"(Build/[\w.\-]+?\.(?:loader\.js|framework\.js|wasm|data)(?:\.unityweb|\.br|\.gz)?)(?=[""'])",
                    "$1?v=" + stamp);

                // So "am I even looking at the new build?" is answerable in two seconds instead of an
                // hour: it prints itself, and `SHOWCASE_BUILD` is there to be typed at the console.
                html = html.Replace("</head>",
                    "  <script>window.SHOWCASE_BUILD=\"" + stamp +
                    "\";console.log(\"[showcase] build \"+window.SHOWCASE_BUILD);</script>\n</head>");

                File.WriteAllText(indexPath, html);
                Debug.Log($"[BuildCli] index.html cache-busted, build stamp {stamp}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BuildCli] cache-bust failed (build is still valid): " + ex.Message);
            }
        }

        [Serializable]
        struct BuildReportData
        {
            public bool   success;
            public string message;
            public long   sizeBytes;
            public float  durationSec;
            public string indexPath;
            public string reportPath; // not serialized to disk, just held internally
        }

        static void WriteReport(BuildReportData data)
        {
            try
            {
                if (string.IsNullOrEmpty(data.reportPath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(data.reportPath));
                // Keep the file shape stable so PowerShell ConvertFrom-Json
                // can read every field even when the build aborted early.
                var json = $"{{\n" +
                           $"  \"success\": {(data.success ? "true" : "false")},\n" +
                           $"  \"message\": \"{Escape(data.message)}\",\n" +
                           $"  \"sizeBytes\": {data.sizeBytes},\n" +
                           $"  \"durationSec\": {data.durationSec.ToString(System.Globalization.CultureInfo.InvariantCulture)},\n" +
                           $"  \"indexPath\": \"{Escape(data.indexPath)}\"\n" +
                           $"}}\n";
                File.WriteAllText(data.reportPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError("[BuildCli] Failed to write report: " + ex.Message);
            }
        }

        static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        class CliArgs
        {
            readonly string[] _argv;
            public CliArgs(string[] argv) { _argv = argv; }
            public string Get(string name, string fallback)
            {
                for (int i = 0; i < _argv.Length - 1; i++)
                    if (_argv[i] == name) return _argv[i + 1];
                return fallback;
            }
        }

        static CliArgs ParseArgs() => new CliArgs(Environment.GetCommandLineArgs());

        // Ensure Assets/Showcase/Resources/CorridorLit.mat exists, built on
        // URP Simple Lit with the _EMISSION keyword enabled. The corridor's
        // runtime materials clone this asset; its keyword set is what pins the
        // needed variants (including emission, for the glow strips — _EMISSION
        // is a shader_feature, so a variant no built material uses doesn't
        // ship) into the WebGL build. Simple Lit specifically, NOT Lit: on
        // 6000.5.2f1 putting URP/Lit in Always-Included Shaders crashes the
        // editor's shader-variant enumeration (segfault in
        // ShaderCompilation::PrepareStageVariantsForSinglePlatform) during the
        // WebGL build; Simple Lit + Unlit enumerate fine, and Blinn-Phong is
        // indistinguishable from PBR on flat-colored corridor boxes.
        public static void EnsureCorridorMaterial()
        {
            const string path = "Assets/Showcase/Resources/CorridorLit.mat";

            var shader = Shader.Find("Universal Render Pipeline/Simple Lit")
                      ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[BuildCli] URP Lit shader not found; corridor material not created.");
                return;
            }

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null && existing.shader == shader && existing.IsKeywordEnabled("_EMISSION"))
                return;
            if (existing != null) AssetDatabase.DeleteAsset(path);   // wrong shader / keywords — rebuild

            var m = new Material(shader) { name = "CorridorLit" };
            m.SetColor("_BaseColor", new Color(0.2f, 0.2f, 0.25f, 1f));
            m.SetFloat("_Smoothness", 0.1f);
            m.SetFloat("_Metallic", 0f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", new Color(0.133f, 0.773f, 0.369f) * 2f);

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            Debug.Log("[BuildCli] Created " + path + " (URP/Lit, _EMISSION on)");
        }
    }
}
