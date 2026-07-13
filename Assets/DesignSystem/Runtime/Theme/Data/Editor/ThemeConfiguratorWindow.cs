using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Data.Editor
{
    public class ThemeConfiguratorWindow : EditorWindow
    {
        private const string PREVIEW_UXML_GUID = "ae1eea539fc84b67aee9a953483d8b42";
        private const string PREVIEW_UXML_PREF_KEY = "DesignSystem.ThemeConfigurator.PreviewUxmlGuid";
        private const double DEBOUNCE_DELAY = 0.15;

        private ThemeData _theme;
        private SerializedObject _serializedObject;
        private readonly Dictionary<string, bool> _foldoutState = new();
        private bool _hadChanges;
        private string _snapshotJson;
        private double _debounceUntil;

        private VisualElement _showcaseClone;
        private ScrollView _previewContainer;
        private StyleSheet _previewStyleSheet;
        private string _previewTempPath;
        private ObjectField _themeField;
        private ObjectField _previewUxmlField;

        [MenuItem("Design System/Theme Configurator")]
        public static void Open() => GetWindow<ThemeConfiguratorWindow>("Theme Configurator");

#if UNITY_6000_5_OR_NEWER
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            var obj = EditorUtility.EntityIdToObject(entityId);
            if (obj is not ThemeData theme) return false;
            var window = GetWindow<ThemeConfiguratorWindow>();
            window.LoadTheme(theme);
            return true;
        }
#else
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            var obj = EditorUtility.InstanceIDToObject(instanceId);
            if (obj is not ThemeData theme) return false;
            var window = GetWindow<ThemeConfiguratorWindow>();
            window.LoadTheme(theme);
            return true;
        }
#endif

        public void LoadTheme(ThemeData theme)
        {
            _theme = theme;
            _serializedObject = new SerializedObject(theme);
            _snapshotJson = EditorJsonUtility.ToJson(theme);
            _hadChanges = false;
            _debounceUntil = 0;
            titleContent = new GUIContent($"Theme Configurator - {theme.name}");

            _themeField?.SetValueWithoutNotify(theme);

            SetupShowcase();
        }

        private void CreateGUI()
        {
            minSize = new Vector2(1280, 720);
            var root = rootVisualElement;

            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexShrink = 0,
                    height = 32,
                    paddingLeft = 6,
                    paddingRight = 6,
                    paddingTop = 4,
                    paddingBottom = 4,
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f)
                }
            };
            root.Add(toolbar);

            _themeField = new ObjectField
            {
                objectType = typeof(ThemeData),
                style = { flexGrow = 1, minWidth = 180, marginRight = 8 }
            };
            if (_theme)
                _themeField.SetValueWithoutNotify(_theme);
            _themeField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is ThemeData td)
                    LoadTheme(td);
            });
            toolbar.Add(_themeField);

            _previewUxmlField = new ObjectField
            {
                objectType = typeof(VisualTreeAsset),
                style = { flexGrow = 1, minWidth = 180, marginRight = 8 }
            };
            var savedUxmlGuid = EditorPrefs.GetString(PREVIEW_UXML_PREF_KEY, "");
            if (!string.IsNullOrEmpty(savedUxmlGuid))
            {
                var savedAsset = AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(savedUxmlGuid));
                if (savedAsset)
                    _previewUxmlField.SetValueWithoutNotify(savedAsset);
            }
            _previewUxmlField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is VisualTreeAsset vta)
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(vta, out var guid, out long _);
                    EditorPrefs.SetString(PREVIEW_UXML_PREF_KEY, guid);
                }
                else
                {
                    EditorPrefs.SetString(PREVIEW_UXML_PREF_KEY, "");
                }
                SetupShowcase();
            });
            toolbar.Add(_previewUxmlField);

            var saveBtn = new Button(Save) { text = "Save" };
            toolbar.Add(saveBtn);

            var revertBtn = new Button(Revert) { text = "Revert" };
            toolbar.Add(revertBtn);

            var split = new TwoPaneSplitView(1, 400, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1 }
            };
            root.Add(split);

            _previewContainer = new ScrollView(ScrollViewMode.Vertical)
            {
                style = { flexGrow = 1 }
            };
            split.Add(_previewContainer);

            var editorImgui = new IMGUIContainer(OnEditorGUI)
            {
                style =
                {
                    flexGrow = 1,
                    paddingLeft = 8,
                    paddingRight = 8,
                    paddingTop = 4
                }
            };
            split.Add(editorImgui);

            if (_theme)
                SetupShowcase();

            root.schedule.Execute(CheckDebounce).Every(50);
        }

        private void OnDestroy()
        {
            CleanupTempFile();

            if (!_theme || (!_hadChanges && !EditorUtility.IsDirty(_theme)))
                return;

            var save = EditorUtility.DisplayDialog(
                "Unsaved Changes",
                $"'{_theme.name}' has unsaved changes.\nDo you want to save before closing?",
                "Save", "Revert");

            if (save) Save();
            else Revert();
        }

        private void CleanupTempFile()
        {
            if (string.IsNullOrEmpty(_previewTempPath) || !File.Exists(_previewTempPath)) return;
            AssetDatabase.DeleteAsset(_previewTempPath);
            _previewTempPath = null;
        }

        private string GetPreviewTempPath()
        {
            if (!_theme) return null;

            var assetPath = AssetDatabase.GetAssetPath(_theme);
            var dir = Path.GetDirectoryName(assetPath);
            return Path.Combine(dir ?? "Assets", "_theme_preview.uss");
        }

        // ──────────────────────────────────────
        // Preview
        // ──────────────────────────────────────

        private static string ResolvePreviewUxmlGuid()
        {
            var prefGuid = EditorPrefs.GetString(PREVIEW_UXML_PREF_KEY, "");
            if (!string.IsNullOrEmpty(prefGuid))
            {
                var asset = AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(prefGuid));
                if (asset) return prefGuid;
                Debug.LogWarning($"ThemeConfigurator: Custom preview UXML not found, falling back to default.");
            }
            return PREVIEW_UXML_GUID;
        }

        private void SetupShowcase()
        {
            if (_previewContainer == null || !_theme) return;

            _previewContainer.Clear();

            if (_previewStyleSheet && _showcaseClone != null)
                _showcaseClone.styleSheets.Remove(_previewStyleSheet);
            _previewStyleSheet = null;

            CleanupTempFile();
            _previewTempPath = null;
            _showcaseClone = null;

            var previewGuid = ResolvePreviewUxmlGuid();
            var showcaseAsset = AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(previewGuid));
            if (!showcaseAsset)
            {
                _previewContainer.Add(new Label(
                    previewGuid == PREVIEW_UXML_GUID
                        ? "Could not load preview UXML"
                        : "Could not load custom preview UXML")
                {
                    style = { color = Color.red, paddingTop = 20, paddingLeft = 20 }
                });
                return;
            }

            _showcaseClone = showcaseAsset.CloneTree();
            _showcaseClone.style.flexGrow = 1;
            _previewContainer.Add(_showcaseClone);

            ApplyThemeStylesheet();
        }

        private void ApplyThemeStylesheet()
        {
            if (_showcaseClone == null || !_theme) return;

            if (_previewStyleSheet)
                _showcaseClone.styleSheets.Remove(_previewStyleSheet);

            if (string.IsNullOrEmpty(_previewTempPath))
                _previewTempPath = GetPreviewTempPath();
            if (string.IsNullOrEmpty(_previewTempPath)) return;

            if (File.Exists(_previewTempPath))
                AssetDatabase.DeleteAsset(_previewTempPath);

            var uss = _theme.GenerateUssString();
            File.WriteAllText(_previewTempPath, uss);
            AssetDatabase.ImportAsset(_previewTempPath, ImportAssetOptions.ForceSynchronousImport);

            _previewStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(_previewTempPath);
            if (_previewStyleSheet)
                _showcaseClone.styleSheets.Add(_previewStyleSheet);
        }

        private void CheckDebounce()
        {
            if (_debounceUntil > 0 && EditorApplication.timeSinceStartup >= _debounceUntil)
            {
                _debounceUntil = 0;
                ApplyThemeStylesheet();
            }
        }

        // ──────────────────────────────────────
        // Editor panel (IMGUI)
        // ──────────────────────────────────────

        private Vector2 _editorScrollPos;

        private void OnEditorGUI()
        {
            if (_theme == null || _serializedObject == null)
            {
                EditorGUILayout.HelpBox("Select a ThemeData asset to begin editing.", MessageType.Info);
                return;
            }

            _editorScrollPos = EditorGUILayout.BeginScrollView(_editorScrollPos);

            _serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            DrawGroupedFields();
            if (EditorGUI.EndChangeCheck())
            {
                _hadChanges = true;
                EditorUtility.SetDirty(_theme);
                _serializedObject.ApplyModifiedProperties();
                _debounceUntil = EditorApplication.timeSinceStartup + DEBOUNCE_DELAY;
            }
            else
            {
                _serializedObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawGroupedFields()
        {
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
                        DrawProperty(entry.Prop);
                    EditorGUI.indentLevel--;
                }

                GUILayout.Space(4);
            }
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
            var so = _serializedObject;
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

                groupList!.Add(entry);
            }

            if (groupList != null)
                groups.Add(new KeyValuePair<string, List<FieldEntry>>(groupHeader, groupList));

            return groups;
        }

        // ──────────────────────────────────────
        // Save / Revert
        // ──────────────────────────────────────

        private void Save()
        {
            if (!_theme) return;

            ThemeDataEditor.EmbedUss(_theme);

            _snapshotJson = EditorJsonUtility.ToJson(_theme);
            _hadChanges = false;
            EditorUtility.ClearDirty(_theme);
            AssetDatabase.SaveAssets();
        }

        private void Revert()
        {
            if (!_theme) return;

            EditorJsonUtility.FromJsonOverwrite(_snapshotJson, _theme);
            _hadChanges = false;
            EditorUtility.ClearDirty(_theme);

            _serializedObject?.Update();

            SetupShowcase();
        }
    }
}
