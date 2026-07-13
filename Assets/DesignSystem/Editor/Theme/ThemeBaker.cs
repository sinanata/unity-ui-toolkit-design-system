using System.Collections.Generic;
using System.IO;
using DesignSystem.Runtime.Theme.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Editor.Theming
{
    /// <summary>
    /// Compiles a <see cref="ThemeData"/>'s token values into a real <see cref="StyleSheet"/>
    /// and stores it as a SUB-ASSET of the theme. One file then carries both the numbers a
    /// designer edits and the stylesheet the runtime hands to a panel, which is why the
    /// runtime needs no editor code and no string parsing.
    ///
    /// Unity has no public "parse a USS string into a StyleSheet" API, so the bake goes the
    /// long way round: write the generated USS to a temp .uss, let the asset importer compile
    /// it, copy the compiled result into the sub-asset, and delete the temp file. That is
    /// also precisely why a theme cannot be authored at RUNTIME — there is no importer in a
    /// player — and why baking at edit time is the whole trick.
    ///
    /// This only ever runs from an explicit Save or from Rebake All. It used to also run from
    /// `AssetModificationProcessor.OnWillSaveAssets`, which mutates the AssetDatabase while a
    /// save is already in flight. Unity does not support that: it re-entered the save pipeline
    /// and left stray `_temp_theme.uss` files next to the theme. If a theme is edited by some
    /// route that never presses Save (a merge, a script, a hand edit), run
    /// <c>Design System &gt; Rebake All Themes</c>.
    /// </summary>
    public static class ThemeBaker
    {
        private const string SubAssetName = "StyleSheet";

        /// <summary>
        /// One scratch folder, at the project root and git-ignored, shared with the Theme
        /// Configurator's live preview. The temp .uss used to be written NEXT TO the theme
        /// asset, so a crash mid-bake left a stray stylesheet inside the package itself, ready
        /// for someone to commit by accident.
        /// </summary>
        public const string TempFolder = "Assets/_DesignSystemTemp";

        private const string TempAsset = TempFolder + "/__bake.uss";

        [MenuItem("Design System/Rebake All Themes")]
        public static void RebakeAllMenu()
        {
            var baked = RebakeAll(out var failed);
            if (failed > 0)
                Debug.LogError($"[ThemeBaker] Baked {baked} theme(s); {failed} failed. See the errors above.");
            else
                Debug.Log($"[ThemeBaker] Baked {baked} theme(s).");
        }

        /// <summary>
        /// Batch entry point:
        /// <c>Unity -batchmode -quit -executeMethod DesignSystem.Editor.Theming.ThemeBaker.BakeAllBatch</c>.
        /// Exits non-zero if any theme fails, so CI notices.
        /// </summary>
        public static void BakeAllBatch()
        {
            var baked = RebakeAll(out var failed);
            Debug.Log($"[ThemeBaker] batch bake complete: baked={baked} failed={failed}");

            if (Application.isBatchMode)
                EditorApplication.Exit(failed > 0 ? 1 : 0);
        }

        /// <summary>Re-bakes every ThemeData asset in the project.</summary>
        public static int RebakeAll(out int failed)
        {
            var guids  = AssetDatabase.FindAssets("t:" + nameof(ThemeData));
            var themes = new List<ThemeData>(guids.Length);

            foreach (var guid in guids)
            {
                var theme = AssetDatabase.LoadAssetAtPath<ThemeData>(AssetDatabase.GUIDToAssetPath(guid));
                if (theme) themes.Add(theme);
            }

            return BakeMany(themes, out failed);
        }

        /// <summary>Bakes one theme and saves. For loops, prefer <see cref="BakeMany"/>.</summary>
        public static bool Bake(ThemeData theme)
        {
            return BakeMany(new[] { theme }, out _) == 1;
        }

        /// <summary>
        /// Bakes a batch, setting the scratch folder up once and saving once at the end. Returns how
        /// many succeeded.
        /// </summary>
        public static int BakeMany(IEnumerable<ThemeData> themes, out int failed)
        {
            var baked = 0;
            failed = 0;

            try
            {
                EnsureTempFolder();

                foreach (var theme in themes)
                {
                    if (BakeInternal(theme)) baked++;
                    else                     failed++;
                }
            }
            finally
            {
                // The FILE goes, the folder stays. The Theme Configurator's live preview keeps
                // its own sheet in here, and pulling the folder out from under it mid-session
                // would leave the preview pointing at a dead asset.
                if (AssetDatabase.LoadAssetAtPath<StyleSheet>(TempAsset))
                    AssetDatabase.DeleteAsset(TempAsset);

                AssetDatabase.SaveAssets();
            }

            return baked;
        }

        /// <summary>Creates the shared scratch folder if it is not there yet.</summary>
        public static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "_DesignSystemTemp");
        }

        private static bool BakeInternal(ThemeData theme)
        {
            if (!theme) return false;

            var assetPath = AssetDatabase.GetAssetPath(theme);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("[ThemeBaker] Theme is not saved to disk, so it has nowhere to keep its stylesheet.", theme);
                return false;
            }

            File.WriteAllText(TempAsset, theme.GenerateUssString());
            AssetDatabase.ImportAsset(TempAsset, ImportAssetOptions.ForceSynchronousImport);

            var compiled = AssetDatabase.LoadAssetAtPath<StyleSheet>(TempAsset);
            if (!compiled)
            {
                Debug.LogError(
                    $"[ThemeBaker] '{theme.name}' generated USS that did not compile. " +
                    $"The scope selector is '{theme.Scope}' — is it a valid USS selector?", theme);
                return false;
            }

            var existing = theme.StyleSheet;
            if (existing && AssetDatabase.IsSubAsset(existing))
            {
                // Copy INTO the existing sub-asset rather than replacing it, so its GUID +
                // fileID survive and every scene, prefab and ThemeApplier already pointing at
                // this stylesheet keeps pointing at it.
                EditorUtility.CopySerialized(compiled, existing);
                existing.name = SubAssetName;
                EditorUtility.SetDirty(existing);
            }
            else
            {
                var sheet = ScriptableObject.CreateInstance<StyleSheet>();
                EditorUtility.CopySerialized(compiled, sheet);
                sheet.name = SubAssetName;
                AssetDatabase.AddObjectToAsset(sheet, assetPath);
                theme.SetStyleSheetReference(sheet);
            }

            EditorUtility.SetDirty(theme);
            return true;
        }
    }
}
