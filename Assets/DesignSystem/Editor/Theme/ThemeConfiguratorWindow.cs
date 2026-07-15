using System.Collections.Generic;
using System.IO;
using DesignSystem.Runtime.Theme.Data;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Editor.Theming
{
    /// <summary>
    /// Split-pane theme editor: token fields on the right, a live preview of real components on
    /// the left. Every edit regenerates the theme's USS and hot-reloads it into the preview, so
    /// you tune a palette against the thing it will actually paint.
    ///
    /// The preview is a REAL stylesheet on a REAL component tree, not a mock-up — it goes through
    /// the same var() cascade the runtime uses, which is the point: if a token looks right here it
    /// looks right in the game.
    /// </summary>
    public class ThemeConfiguratorWindow : EditorWindow
    {
        private const string PreviewUxmlGuid   = "ae1eea539fc84b67aee9a953483d8b42";
        private const string PreviewUxmlPrefKey = "DesignSystem.ThemeConfigurator.PreviewUxmlGuid";
        private const double DebounceSeconds   = 0.15;

        // The preview's scratch stylesheet. One fixed path, in the shared git-ignored scratch
        // folder — it used to be written NEXT TO the theme asset and re-created from scratch on
        // every debounce tick, which meant dragging a colour slider churned a create/import/delete
        // cycle inside the package six times a second, and an editor crash left the file behind.
        private static string PreviewUssPath => ThemeBaker.TempFolder + "/__preview.uss";

        private ThemeData _theme;
        private SerializedObject _serialized;
        private readonly Dictionary<string, bool> _foldouts = new();
        private bool _touched;
        private string _snapshot;
        private double _debounceUntil;

        private VisualElement _previewTree;
        private ScrollView _previewContainer;
        private StyleSheet _previewSheet;
        private ObjectField _themeField;
        private ObjectField _previewUxmlField;
        private Vector2 _fieldsScroll;

        [MenuItem("Design System/Theme Configurator")]
        public static void Open() => GetWindow<ThemeConfiguratorWindow>("Theme Configurator");

        public static void OpenWith(ThemeData theme)
        {
            var window = GetWindow<ThemeConfiguratorWindow>("Theme Configurator");
            window.LoadTheme(theme);
        }

#if UNITY_6000_5_OR_NEWER
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(EntityId entityId, int line)
        {
            if (EditorUtility.EntityIdToObject(entityId) is not ThemeData theme) return false;
            OpenWith(theme);
            return true;
        }
#else
        [OnOpenAsset(1)]
        public static bool OnOpenAsset(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is not ThemeData theme) return false;
            OpenWith(theme);
            return true;
        }
#endif

        public void LoadTheme(ThemeData theme)
        {
            _theme         = theme;
            _serialized    = theme ? new SerializedObject(theme) : null;
            _snapshot      = theme ? EditorJsonUtility.ToJson(theme) : null;
            _touched       = false;
            _debounceUntil = 0;

            titleContent = new GUIContent(theme ? $"Theme - {theme.name}" : "Theme Configurator");
            _themeField?.SetValueWithoutNotify(theme);

            RebuildPreview();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Chrome
        // ──────────────────────────────────────────────────────────────────────

        private void CreateGUI()
        {
            // 1280x720 was the MINIMUM here, which no laptop can dock and nobody can shrink.
            // A floating window still opens at a sensible size; this only sets the floor.
            minSize = new Vector2(720, 420);

            var root = rootVisualElement;

            var toolbar = new VisualElement
            {
                style =
                {
                    flexDirection   = FlexDirection.Row,
                    flexShrink      = 0,
                    height          = 32,
                    paddingLeft     = 6,
                    paddingRight    = 6,
                    paddingTop      = 4,
                    paddingBottom   = 4,
                    backgroundColor = new Color(0.22f, 0.22f, 0.22f),
                },
            };
            root.Add(toolbar);

            _themeField = new ObjectField("Theme")
            {
                objectType = typeof(ThemeData),
                style = { flexGrow = 1, minWidth = 180, marginRight = 8 },
            };
            if (_theme) _themeField.SetValueWithoutNotify(_theme);
            _themeField.RegisterValueChangedCallback(evt => LoadTheme(evt.newValue as ThemeData));
            toolbar.Add(_themeField);

            _previewUxmlField = new ObjectField("Preview")
            {
                objectType = typeof(VisualTreeAsset),
                style = { flexGrow = 1, minWidth = 180, marginRight = 8 },
            };
            var savedGuid = EditorPrefs.GetString(PreviewUxmlPrefKey, "");
            if (!string.IsNullOrEmpty(savedGuid))
            {
                var saved = AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(savedGuid));
                if (saved) _previewUxmlField.SetValueWithoutNotify(saved);
            }
            _previewUxmlField.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue is VisualTreeAsset vta &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(vta, out var guid, out long _))
                    EditorPrefs.SetString(PreviewUxmlPrefKey, guid);
                else
                    EditorPrefs.SetString(PreviewUxmlPrefKey, "");

                RebuildPreview();
            });
            toolbar.Add(_previewUxmlField);

            toolbar.Add(new Button(Save)   { text = "Save" });
            toolbar.Add(new Button(Revert) { text = "Revert" });

            var split = new TwoPaneSplitView(1, 400, TwoPaneSplitViewOrientation.Horizontal)
            {
                style = { flexGrow = 1 },
            };
            root.Add(split);

            _previewContainer = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1 } };
            split.Add(_previewContainer);

            split.Add(new IMGUIContainer(DrawFields)
            {
                style = { flexGrow = 1, paddingLeft = 8, paddingRight = 8, paddingTop = 4 },
            });

            if (_theme) RebuildPreview();

            root.schedule.Execute(TickDebounce).Every(50);
        }

        private void OnDestroy()
        {
            DeletePreviewSheet();

            if (!_theme) return;
            if (!_touched && !EditorUtility.IsDirty(_theme)) return;
            if (!ThemeDataGUI.CanPrompt) return;    // domain reload / play-mode, not a real close

            var save = EditorUtility.DisplayDialog(
                "Unsaved theme",
                $"'{_theme.name}' has unsaved changes.\nSave and re-bake its stylesheet?",
                "Save", "Revert");

            if (save) Save();
            else      Revert();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Preview
        // ──────────────────────────────────────────────────────────────────────

        private void RebuildPreview()
        {
            if (_previewContainer == null) return;

            _previewContainer.Clear();
            _previewTree  = null;
            _previewSheet = null;

            if (!_theme)
            {
                _previewContainer.Add(Hint("Pick a ThemeData asset to start editing."));
                return;
            }

            var guid = ResolvePreviewUxmlGuid();
            var uxml = AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(guid));
            if (!uxml)
            {
                _previewContainer.Add(Hint(guid == PreviewUxmlGuid
                    ? "Could not load the bundled ThemePreview.uxml."
                    : "Could not load the custom preview UXML. Clear the Preview field to fall back."));
                return;
            }

            _previewTree = uxml.CloneTree();
            _previewTree.style.flexGrow = 1;
            _previewContainer.Add(_previewTree);

            PushThemeIntoPreview();
        }

        // Writes the theme's USS to the scratch file, imports it, and swaps the resulting
        // stylesheet onto the preview tree. Remove-then-add, so the theme sheet always ends up
        // LAST in the list and therefore wins ties against the design system's base tokens —
        // the same ordering rule the runtime applier depends on.
        private void PushThemeIntoPreview()
        {
            if (_previewTree == null || !_theme) return;

            ThemeBaker.EnsureTempFolder();
            var path = PreviewUssPath;

            // The typeface is an asset reference until an editor resolves it into a USS one, and
            // GenerateUssString cannot do that itself (it also runs in the player). Without this
            // the preview would show every colour change and silently ignore the font.
            ThemeBaker.SyncTypeface(_theme);

            File.WriteAllText(path, _theme.GenerateUssString());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (!sheet) return;

            if (_previewSheet && _previewTree.styleSheets.Contains(_previewSheet))
                _previewTree.styleSheets.Remove(_previewSheet);

            // A class-scoped theme only paints where its class is, so the preview has to carry it
            // — otherwise a .theme-light theme looks like it does nothing at all.
            var scopeClass = _theme.ScopeClass;
            if (scopeClass != null && !_previewTree.ClassListContains(scopeClass))
                _previewTree.AddToClassList(scopeClass);

            _previewTree.styleSheets.Add(sheet);
            _previewSheet = sheet;
        }

        private void TickDebounce()
        {
            if (_debounceUntil <= 0 || EditorApplication.timeSinceStartup < _debounceUntil) return;

            _debounceUntil = 0;
            PushThemeIntoPreview();
        }

        private static string ResolvePreviewUxmlGuid()
        {
            var pref = EditorPrefs.GetString(PreviewUxmlPrefKey, "");
            if (string.IsNullOrEmpty(pref)) return PreviewUxmlGuid;

            if (AssetDatabase.LoadAssetByGUID<VisualTreeAsset>(new GUID(pref))) return pref;

            Debug.LogWarning("[ThemeConfigurator] The custom preview UXML is gone; falling back to the bundled one.");
            return PreviewUxmlGuid;
        }

        private static void DeletePreviewSheet()
        {
            if (AssetDatabase.LoadAssetAtPath<StyleSheet>(PreviewUssPath))
                AssetDatabase.DeleteAsset(PreviewUssPath);
        }

        private static Label Hint(string text) =>
            new(text) { style = { paddingTop = 20, paddingLeft = 20, whiteSpace = WhiteSpace.Normal } };

        // ──────────────────────────────────────────────────────────────────────
        // Fields (IMGUI)
        // ──────────────────────────────────────────────────────────────────────

        private void DrawFields()
        {
            if (!_theme || _serialized == null)
            {
                EditorGUILayout.HelpBox("Pick a ThemeData asset to start editing.", MessageType.Info);
                return;
            }

            _fieldsScroll = EditorGUILayout.BeginScrollView(_fieldsScroll);

            ThemeDataGUI.DrawBakeState(_theme, _touched || EditorUtility.IsDirty(_theme));

            if (ThemeDataGUI.Draw(_serialized, _foldouts))
            {
                _touched = true;
                EditorUtility.SetDirty(_theme);
                _debounceUntil = EditorApplication.timeSinceStartup + DebounceSeconds;
            }

            EditorGUILayout.EndScrollView();
        }

        // ──────────────────────────────────────────────────────────────────────
        // Save / Revert
        // ──────────────────────────────────────────────────────────────────────

        private void Save()
        {
            if (!_theme) return;

            ThemeBaker.Bake(_theme);        // re-bakes the stylesheet AND saves the asset

            _snapshot = EditorJsonUtility.ToJson(_theme);
            _touched  = false;
            EditorUtility.ClearDirty(_theme);
        }

        private void Revert()
        {
            if (!_theme || _snapshot == null) return;

            EditorJsonUtility.FromJsonOverwrite(_snapshot, _theme);
            _touched = false;
            EditorUtility.ClearDirty(_theme);

            _serialized?.Update();
            RebuildPreview();
        }
    }
}
