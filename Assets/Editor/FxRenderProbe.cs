#if UNITY_6000_5_OR_NEWER
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DesignSystemHost.Editor
{
    /// <summary>
    /// Editor half of the material render probe. Host-project tooling, not part of the package.
    ///
    /// The compile check tells you a family COMPILES. It tells you nothing about whether it looks
    /// like anything. This renders one, to a PNG you can actually look at.
    ///
    /// Batch:
    ///   Unity -batchmode (NO -nographics, NO -quit) -projectPath &lt;proj&gt;
    ///         -executeMethod DesignSystemHost.Editor.FxRenderProbe.RunBatch
    ///         -logFile &lt;log&gt;
    ///   Output goes to Temp/fx-probe/ (override with -fxProbeOut &lt;dir&gt;).
    ///
    /// Why the request file rather than just doing the work here: rendering a UI Toolkit runtime
    /// panel needs PLAY MODE, and entering play mode tears down the current domain — this class's
    /// statics included. So we record the request on disk, queue the mode switch, and
    /// FxProbeRunner picks it up on the far side.
    /// </summary>
    public static class FxRenderProbe
    {
        public const string RequestPath = "Temp/fx-probe-request.json";

        [Serializable]
        public class Request
        {
            public string outputPath;
        }

        [MenuItem("Design System/FX/Render Probe")]
        private static void RunMenu() => Queue(Path.GetFullPath("Temp/fx-probe").Replace('\\', '/'));

        public static void RunBatch()
        {
            var outDir = ArgAfter("-fxProbeOut") ?? Path.GetFullPath("Temp/fx-probe");
            Queue(outDir.Replace('\\', '/'));
        }

        private static void Queue(string outDir)
        {
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(RequestPath, JsonUtility.ToJson(new Request { outputPath = outDir }));

            // A clean stage: whatever scene was last open must not boot alongside the probe.
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            Debug.Log($"[ds fx probe] entering play mode; output -> {outDir}");
            EditorApplication.EnterPlaymode();
        }

        private static string ArgAfter(string flag)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
                if (args[i] == flag)
                    return args[i + 1];
            return null;
        }
    }
}
#endif
