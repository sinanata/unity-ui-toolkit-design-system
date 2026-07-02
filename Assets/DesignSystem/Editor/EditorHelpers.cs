using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Editor
{
    public static class EditorHelpers
    {
        private const string DESIGN_SYSTEM_USS_GUID = "2fe121bab235f2a4f8cbc07737f87fe2";

        [MenuItem("Assets/Design System/Add Stylesheet To Selected Layout Asset")]
        public static void AddStyleSheet()
        {
            var obj = Selection.activeObject;
            if (obj == null)
            {
                EditorUtility.DisplayDialog("No Selection", "Select a UXML (Visual Tree Asset) first.", "OK");
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".uxml"))
            {
                EditorUtility.DisplayDialog("Wrong Type", "Selected asset is not a UXML (Visual Tree Asset).", "OK");
                return;
            }

            var ussPath = AssetDatabase.GUIDToAssetPath(DESIGN_SYSTEM_USS_GUID);
            if (string.IsNullOrEmpty(ussPath))
            {
                EditorUtility.DisplayDialog("Missing",
                    "Could not find DesignSystem.uss in the project.", "OK");
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet == null ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(styleSheet, out var guid, out long fileId))
            {
                EditorUtility.DisplayDialog("Error", "Failed to load DesignSystem.uss.", "OK");
                return;
            }

            var assetName = Path.GetFileNameWithoutExtension(ussPath);
            var src = $"project://database/{ussPath}?fileID={fileId}&amp;guid={guid}&amp;type=3#{assetName}";
            var tag = $"<Style src=\"{src}\" />";

            var uxml = File.ReadAllText(assetPath);

            if (uxml.Contains(src))
            {
                EditorUtility.DisplayDialog("Already Added",
                    "This stylesheet is already referenced in the UXML.", "OK");
                return;
            }

            uxml = Regex.Replace(
                uxml,
                @"^(\s*<ui:UXML[^>]*>)\s*",
                $"$1\n  {tag}\n",
                RegexOptions.Multiline | RegexOptions.Singleline);

            File.WriteAllText(assetPath, uxml);
            AssetDatabase.ImportAsset(assetPath);
            EditorUtility.DisplayDialog("Done",
                $"Added DesignSystem.uss to\n{assetPath}", "OK");
        }

        [MenuItem("Assets/Design System/Add Stylesheet To Selected Layout Asset", true)]
        public static bool AddStyleSheetValidate()
        {
            var selected = Selection.GetFiltered<Object>(SelectionMode.Assets);
            if (selected is not { Length: 1 }) return false;
            var path = AssetDatabase.GetAssetPath(selected[0]);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".uxml");
        }
    }
}