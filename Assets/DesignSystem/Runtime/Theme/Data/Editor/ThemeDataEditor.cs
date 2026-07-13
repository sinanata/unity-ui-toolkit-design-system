using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Data.Editor
{
    [CustomEditor(typeof(ThemeData))]
    public class ThemeDataEditor : UnityEditor.Editor
    {
        private bool _hadChanges;
        private string _snapshotJson;
        private readonly Dictionary<string, bool> _foldoutState = new();

        private void OnEnable()
        {
            _snapshotJson = EditorJsonUtility.ToJson(target);
        }

        public override void OnInspectorGUI()
        {
            var theme = (ThemeData)target;
            var isDirty = EditorUtility.IsDirty(theme) || _hadChanges;

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = isDirty;
            if (GUILayout.Button("Save", GUILayout.Height(26)))
                Save();
            if (GUILayout.Button("Revert", GUILayout.Height(26)))
                Revert();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (GUILayout.Button("Open in Theme Configurator", GUILayout.Height(26)))
            {
                var window = EditorWindow.GetWindow<ThemeConfiguratorWindow>();
                window.LoadTheme(theme);
            }

            GUILayout.Space(8);

            EditorGUI.BeginChangeCheck();
            DrawGroupedFields();
            if (EditorGUI.EndChangeCheck())
            {
                _hadChanges = true;
                EditorUtility.SetDirty(theme);
            }
        }

        private void DrawGroupedFields()
        {
            var so = serializedObject;
            so.Update();

            var groups = GroupSerializedProperties();

            foreach (var group in groups)
            {
                var expanded = _foldoutState.GetValueOrDefault(group.Key, true);

                expanded = EditorGUILayout.Foldout(expanded, group.Key, true);
                _foldoutState[group.Key] = expanded;

                if (expanded)
                {
                    EditorGUI.indentLevel++;
                    foreach (var entry in group.Value)
                    {
                        DrawProperty(entry.Prop);
                    }
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space(4);
            }

            so.ApplyModifiedProperties();
        }

        private static void DrawProperty(SerializedProperty prop)
        {
            var label = new GUIContent(prop.displayName);

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Color:
                    prop.colorValue = EditorGUILayout.ColorField(label, prop.colorValue);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = EditorGUILayout.FloatField(label, prop.floatValue);
                    break;
                default:
                    EditorGUILayout.PropertyField(prop, true);
                    break;
            }
        }
        
        private struct FieldEntry
        {
            public SerializedProperty Prop;
            public string Header;
        }

        private List<KeyValuePair<string, List<FieldEntry>>> GroupSerializedProperties()
        {
            var so = serializedObject;
            var entries = new List<FieldEntry>();
            var allFields = typeof(ThemeData).GetFields(
                BindingFlags.Public | BindingFlags.Instance);

            var currentHeader = "General";

            foreach (var f in allFields)
            {
                var prop = so.FindProperty(f.Name);
                if (prop == null) continue;

                var headerAttr = f.GetCustomAttribute<HeaderAttribute>();
                if (headerAttr != null)
                    currentHeader = headerAttr.header;

                entries.Add(new FieldEntry
                {
                    Prop = prop.Copy(),
                    Header = currentHeader
                });
            }

            var groups = new List<KeyValuePair<string, List<FieldEntry>>>();
            var groupHeader = (string)null;
            List<FieldEntry> groupList = null;

            foreach (var entry in entries)
            {
                if (entry.Header != groupHeader)
                {
                    if (groupList != null)
                        groups.Add(new KeyValuePair<string, List<FieldEntry>>(groupHeader, groupList));
                    groupHeader = entry.Header;
                    groupList = new List<FieldEntry>();
                }

                groupList?.Add(entry);
            }

            if (groupList != null)
                groups.Add(new KeyValuePair<string, List<FieldEntry>>(groupHeader, groupList));

            return groups;
        }

        private void Save()
        {
            var theme = (ThemeData)target;
            EmbedUss(theme);

            _snapshotJson = EditorJsonUtility.ToJson(theme);
            _hadChanges = false;
            EditorUtility.ClearDirty(theme);
            AssetDatabase.SaveAssets();
        }

        private void Revert()
        {
            var theme = (ThemeData)target;
            EditorJsonUtility.FromJsonOverwrite(_snapshotJson, theme);
            _hadChanges = false;
            EditorUtility.ClearDirty(theme);
            serializedObject.Update();
            Repaint();
        }

        private void OnDisable()
        {
            var theme = (ThemeData)target;
            if (!theme) return;

            if (!EditorUtility.IsDirty(theme) && !_hadChanges) return;
            var save = EditorUtility.DisplayDialog(
                "Unsaved Changes",
                $"'{theme.name}' has unsaved changes.\nDo you want to save before closing?",
                "Save", "Revert");

            if (save) Save();
            else Revert();
        }

        public static void EmbedUss(ThemeData theme)
        {
            if (!theme) return;

            var assetPath = AssetDatabase.GetAssetPath(theme);
            if (string.IsNullOrEmpty(assetPath)) return;

            var dir = Path.GetDirectoryName(assetPath);
            var tempPath = Path.Combine(dir ?? "Assets", "_temp_theme.uss");

            var uss = theme.GenerateUssString();
            File.WriteAllText(tempPath, uss);
            AssetDatabase.ImportAsset(tempPath, ImportAssetOptions.ForceSynchronousImport);

            var importedSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(tempPath);
            if (!importedSheet)
            {
                AssetDatabase.DeleteAsset(tempPath);
                return;
            }

            var existingSheet = theme.StyleSheet;
            if (existingSheet && AssetDatabase.IsSubAsset(existingSheet))
            {
                EditorUtility.CopySerialized(importedSheet, existingSheet);
                existingSheet.name = "StyleSheet";
                EditorUtility.SetDirty(existingSheet);
            }
            else
            {
                var newSheet = CreateInstance<StyleSheet>();
                EditorUtility.CopySerialized(importedSheet, newSheet);
                newSheet.name = "StyleSheet";
                AssetDatabase.AddObjectToAsset(newSheet, assetPath);
                theme.SetStyleSheetReference(newSheet);
            }

            AssetDatabase.DeleteAsset(tempPath);
            EditorUtility.SetDirty(theme);
        }
    }

    // ReSharper disable once InconsistentNaming
    internal class ThemeDataSaveProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            foreach (var path in paths)
            {
                if (!path.EndsWith(".asset")) continue;
                var theme = AssetDatabase.LoadAssetAtPath<ThemeData>(path);
                if (!theme) continue;
                ThemeDataEditor.EmbedUss(theme);
            }
            return paths;
        }
    }
}
