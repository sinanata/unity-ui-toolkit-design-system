using System.Collections.Generic;
using DesignSystem.Runtime.Theme.Data;
using UnityEditor;
using UnityEngine;

namespace DesignSystem.Editor.Theming
{
    /// <summary>
    /// The ThemeData inspector: token fields grouped by category, plus the Save that re-bakes
    /// the companion stylesheet. All the drawing lives in <see cref="ThemeDataGUI"/>, shared
    /// with the Theme Configurator window.
    /// </summary>
    [CustomEditor(typeof(ThemeData))]
    public class ThemeDataEditor : UnityEditor.Editor
    {
        private readonly Dictionary<string, bool> _foldouts = new();
        private bool _touched;
        private string _snapshot;

        private ThemeData Theme => (ThemeData)target;

        private void OnEnable()
        {
            _snapshot = EditorJsonUtility.ToJson(target);
            _touched  = false;
        }

        public override void OnInspectorGUI()
        {
            var theme = Theme;
            var dirty = _touched || EditorUtility.IsDirty(theme);

            ThemeDataGUI.DrawBakeState(theme, dirty);

            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(!dirty))
            {
                if (GUILayout.Button("Save", GUILayout.Height(26)))   Save();
                if (GUILayout.Button("Revert", GUILayout.Height(26))) Revert();
            }

            GUILayout.Space(4);

            if (GUILayout.Button("Open in Theme Configurator", GUILayout.Height(26)))
                ThemeConfiguratorWindow.OpenWith(theme);

            GUILayout.Space(8);

            if (ThemeDataGUI.Draw(serializedObject, _foldouts))
            {
                _touched = true;
                EditorUtility.SetDirty(theme);
            }
        }

        private void Save()
        {
            var theme = Theme;
            ThemeBaker.Bake(theme);          // re-bakes the stylesheet AND saves the asset

            _snapshot = EditorJsonUtility.ToJson(theme);
            _touched  = false;
            EditorUtility.ClearDirty(theme);
        }

        private void Revert()
        {
            var theme = Theme;
            EditorJsonUtility.FromJsonOverwrite(_snapshot, theme);

            _touched = false;
            EditorUtility.ClearDirty(theme);
            serializedObject.Update();
            Repaint();
        }

        private void OnDisable()
        {
            if (!target) return;                                      // destroyed on domain reload
            if (!_touched && !EditorUtility.IsDirty(target)) return;
            if (!ThemeDataGUI.CanPrompt) return;                      // reload / play-mode: not our moment

            var save = EditorUtility.DisplayDialog(
                "Unsaved theme",
                $"'{target.name}' has unsaved changes.\nSave and re-bake its stylesheet?",
                "Save", "Revert");

            if (save) Save();
            else      Revert();
        }
    }
}
