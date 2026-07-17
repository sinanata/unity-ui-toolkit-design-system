#if UNITY_6000_5_OR_NEWER
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DesignSystemHost.Editor
{
    /// <summary>
    /// Drives the REAL showcase: opens Showcase.unity, lets it boot, and picks "Blueprint (shader)"
    /// from the theme dropdown exactly as a user would — then asserts that the flat page actually
    /// became a material.
    ///
    /// Why this exists rather than another FxRenderProbe check: FxRenderProbe proves DsFxTheme.Apply
    /// works on a tree it builds itself, which is a different claim from "the dropdown does
    /// something". The bug this was written for lived entirely in the wiring between them — a global
    /// epoch counter cancelling the flat page's scheduled Apply as soon as the next root painted —
    /// and no amount of testing Apply in isolation would ever have caught it.
    ///
    /// Batch:
    ///   Unity -batchmode (NO -nographics, NO -quit) -projectPath &lt;proj&gt;
    ///         -executeMethod DesignSystemHost.Editor.ShowcaseBlueprintProbe.RunBatch
    ///         -logFile &lt;log&gt;
    /// </summary>
    public static class ShowcaseBlueprintProbe
    {
        public const string RequestPath = "Temp/showcase-blueprint-probe.json";

        [Serializable]
        public class Request { public string outputPath; }

        [MenuItem("Design System/FX/Showcase Blueprint Probe")]
        private static void RunMenu() => Queue(Path.GetFullPath("Temp/showcase-blueprint"));

        public static void RunBatch()
        {
            var outDir = ArgAfter("-probeOut") ?? Path.GetFullPath("Temp/showcase-blueprint");
            Queue(outDir);
        }

        private static void Queue(string outDir)
        {
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory("Temp");
            File.WriteAllText(RequestPath, JsonUtility.ToJson(new Request
            {
                outputPath = outDir.Replace('\\', '/'),
            }));

            // The real scene, not an empty one: the whole point is to exercise the real bootstrap.
            EditorSceneManager.OpenScene("Assets/Showcase/Showcase.unity", OpenSceneMode.Single);
            Debug.Log($"[showcase probe] entering play mode; output -> {outDir}");
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
