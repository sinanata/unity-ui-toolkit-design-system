#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using DesignSystem.Runtime.Fx;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace DesignSystem.Editor.Fx
{
    /// <summary>
    /// Force-compiles every REGISTERED material family's shader and reports what the compiler says.
    ///
    /// This is the fast loop for writing a family, and you want it: importing a shader only PARSES
    /// it. The first real compile otherwise happens at first draw, inside play mode, where a
    /// failure surfaces as a magenta element and one line in the console — minutes into a run, far
    /// from the edit that caused it. Run this instead and get the file and line.
    ///
    /// It is registry-driven, so it checks YOUR families too, not just the ones shipped here.
    /// </summary>
    public static class DsFxCompileCheck
    {
        [MenuItem("Design System/FX/Compile Check")]
        private static void RunMenu()
        {
            var messages = Check(out var failed);
            foreach (var m in messages)
                Debug.Log(m);
            if (failed == 0)
                Debug.Log($"[ds fx] compile check: all {DsFxRegistry.All.Count} registered families ok.");
            else
                Debug.LogError($"[ds fx] compile check: {failed} of {DsFxRegistry.All.Count} families FAILED (see above).");
        }

        /// <summary>
        /// Batch entry point:
        /// <code>
        /// Unity -batchmode -quit -projectPath &lt;proj&gt; -executeMethod DesignSystem.Editor.Fx.DsFxCompileCheck.RunBatch
        /// </code>
        /// Exits non-zero when any family fails, so CI can gate on it.
        /// </summary>
        public static void RunBatch()
        {
            var messages = Check(out var failed);
            foreach (var m in messages)
                Console.WriteLine(m);
            Console.WriteLine($"[ds fx] compile check: {DsFxRegistry.All.Count} families, {failed} failed.");
            EditorApplication.Exit(failed == 0 ? 0 : 2);
        }

        /// <summary>Compile every registered family. Returns the compiler's messages; sets
        /// <paramref name="failed"/> to the number of families with errors.</summary>
        public static List<string> Check(out int failed)
        {
            var report = new List<string>();
            failed = 0;

            if (DsFxRegistry.All.Count == 0)
            {
                report.Add("[ds fx] no families registered — nothing to check. A family registers " +
                           "itself from [RuntimeInitializeOnLoadMethod] (plus [InitializeOnLoadMethod] " +
                           "so editor tooling like this can see it outside play mode).");
                return report;
            }

            foreach (var family in DsFxRegistry.All)
            {
                var shader = string.IsNullOrEmpty(family.ShaderName) ? null : Shader.Find(family.ShaderName);
                if (shader == null && !string.IsNullOrEmpty(family.ShaderResource))
                    shader = Resources.Load<Shader>(family.ShaderResource);
                if (shader == null)
                {
                    report.Add($"[ds fx] {family.Name}: SHADER NOT FOUND (Shader.Find(\"{family.ShaderName}\"), " +
                               $"Resources.Load(\"{family.ShaderResource}\")).");
                    failed++;
                    continue;
                }

                ShaderUtil.ClearShaderMessages(shader);
                var material = new Material(shader);
                try
                {
                    ShaderUtil.CompilePass(material, 0, forceSync: true);
                    var messages = ShaderUtil.GetShaderMessages(shader);
                    foreach (var m in messages)
                    {
                        var severity = m.severity == ShaderCompilerMessageSeverity.Error ? "ERROR" : "warning";
                        report.Add($"[ds fx] {family.Name}: {severity}: {m.message} (line {m.line}, {m.platform})");
                    }
                    var hasError = ShaderUtil.ShaderHasError(shader);
                    if (hasError)
                    {
                        failed++;
                        if (!messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error))
                            report.Add($"[ds fx] {family.Name}: has errors but produced NO messages. That is the " +
                                       "signature of an FXC crash — check that dsfx_wellShade is called " +
                                       "unconditionally and that no branch inlines the surface twice.");
                    }
                    else
                    {
                        report.Add($"[ds fx] {family.Name}: ok");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }

            return report;
        }
    }
}
#endif
