using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesignSystem.Runtime.Typography;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace DesignSystem.Editor.Typography
{
    /// <summary>
    /// Pick a Google Font, get a working design-system typeface.
    ///
    /// The window exists because Unity's own Font Asset Creator cannot do this. Point it at a
    /// modern Google Font and it produces ONE weight, because almost all of them now ship as a
    /// single variable file and FreeType opens those at their default instance. This importer
    /// reads the font's <c>fvar</c> table instead, pulls every named instance out of the one
    /// file, and wires the weight table so the design system's existing bold rules resolve to
    /// a real bold face.
    /// </summary>
    public sealed class GoogleFontsWindow : EditorWindow
    {
        private static readonly int[] AllWeights = { 100, 200, 300, 400, 500, 600, 700, 800, 900 };

        private string _search = "";
        private string _category = "All";
        private Vector2 _listScroll, _panelScroll;

        private List<GoogleFontsCatalog.Family> _catalog;
        private List<GoogleFontsCatalog.Family> _filtered;
        private GoogleFontsCatalog.Family _selected;

        private readonly HashSet<int> _weights = new() { 400, 700 };
        private bool _italics;
        private bool _upgradeTypeRamp = true;
        private string _outputRoot = GoogleFontsImporter.DefaultRoot;
        private readonly List<FontAsset> _fallbacks = new();

        [MenuItem("Design System/Google Fonts", priority = 20)]
        public static void Open()
        {
            var window = GetWindow<GoogleFontsWindow>("Google Fonts");
            window.minSize = new Vector2(720, 460);
        }

        private void OnEnable() => Reload(false);

        private void Reload(bool force)
        {
            _catalog = GoogleFontsCatalog.Load(force);
            Filter();
        }

        private void Filter()
        {
            IEnumerable<GoogleFontsCatalog.Family> q = _catalog ?? new List<GoogleFontsCatalog.Family>();

            if (_category != "All")
                q = q.Where(f => f.Category == _category);

            if (!string.IsNullOrWhiteSpace(_search))
                q = q.Where(f => f.Name.IndexOf(_search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0);

            _filtered = q.ToList();
        }

        private void OnGUI()
        {
            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawPanel();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();

                _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(220));

                var categories = new[] { "All", "Sans Serif", "Serif", "Display", "Handwriting", "Monospace" };
                int i = Mathf.Max(0, System.Array.IndexOf(categories, _category));
                i = EditorGUILayout.Popup(i, categories, EditorStyles.toolbarPopup, GUILayout.Width(120));
                _category = categories[i];

                if (EditorGUI.EndChangeCheck()) Filter();

                GUILayout.FlexibleSpace();

                GUILayout.Label(_catalog == null ? "—" : $"{_filtered.Count} / {_catalog.Count} families",
                    EditorStyles.miniLabel);

                if (GUILayout.Button("Refresh catalogue", EditorStyles.toolbarButton))
                    Reload(true);

                if (GUILayout.Button("Import from disk…", EditorStyles.toolbarButton))
                    ImportLocal();
            }
        }

        private void DrawList()
        {
            using var scope = new EditorGUILayout.VerticalScope(GUILayout.Width(280));
            _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

            if (_filtered == null || _filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _catalog == null || _catalog.Count == 0
                        ? "No catalogue. Check your connection and press Refresh."
                        : "Nothing matches that search.",
                    MessageType.Info);
            }
            else
            {
                // 2000 families is more than IMGUI wants to lay out every repaint, and nobody
                // scrolls past the first few dozen anyway. Search is the real navigation.
                foreach (var family in _filtered.Take(300))
                {
                    bool on = family == _selected;
                    var style = on ? EditorStyles.boldLabel : EditorStyles.label;

                    var rect = EditorGUILayout.GetControlRect(GUILayout.Height(20));
                    if (on) EditorGUI.DrawRect(rect, new Color(0.24f, 0.37f, 0.59f, 0.35f));

                    GUI.Label(rect, family.Name, style);

                    var meta = new Rect(rect.x + rect.width - 90, rect.y, 88, rect.height);
                    GUI.Label(meta, family.HasVariable ? "variable" : "static", EditorStyles.miniLabel);

                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        Select(family);
                        Event.current.Use();
                        Repaint();
                    }
                }

                if (_filtered.Count > 300)
                    EditorGUILayout.LabelField($"…and {_filtered.Count - 300} more. Search to narrow.",
                        EditorStyles.miniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void Select(GoogleFontsCatalog.Family family)
        {
            _selected = family;

            // Default to what the family can actually deliver, so the weight boxes are never a
            // promise it cannot keep.
            _weights.Clear();
            var likely = family.LikelyWeights.ToList();

            foreach (int w in new[] { 400, 700 })
                if (likely.Contains(w)) _weights.Add(w);

            if (_weights.Count == 0 && likely.Count > 0) _weights.Add(likely[0]);
        }

        private void DrawPanel()
        {
            using var scope = new EditorGUILayout.VerticalScope();
            _panelScroll = EditorGUILayout.BeginScrollView(_panelScroll);

            if (_selected == null)
            {
                EditorGUILayout.HelpBox(
                    "Pick a family on the left.\n\n" +
                    "Variable families (most of the popular ones) carry every weight in a single " +
                    "file, so importing the whole 100-900 ramp costs no more download than " +
                    "importing one weight.",
                    MessageType.Info);

                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField(_selected.Name, EditorStyles.largeLabel);
            EditorGUILayout.LabelField(
                $"{_selected.Category}   ·   {_selected.Dir}   ·   " +
                $"{(_selected.HasVariable ? "variable" : $"{_selected.Files.Count} static files")}",
                EditorStyles.miniLabel);

            EditorGUILayout.Space();

            // ---- weights ----
            EditorGUILayout.LabelField("Weights", EditorStyles.boldLabel);

            var available = _selected.LikelyWeights.ToHashSet();

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (int w in AllWeights)
                {
                    bool has = available.Contains(w);

                    using (new EditorGUI.DisabledScope(!has))
                    {
                        bool on = _weights.Contains(w) && has;
                        bool now = GUILayout.Toggle(on, w.ToString(), EditorStyles.miniButton, GUILayout.Width(42));

                        if (now && !on) _weights.Add(w);
                        else if (!now && on) _weights.Remove(w);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("All", EditorStyles.miniButton, GUILayout.Width(60)))
                    foreach (int w in available) _weights.Add(w);

                if (GUILayout.Button("None", EditorStyles.miniButton, GUILayout.Width(60)))
                    _weights.Clear();

                GUILayout.FlexibleSpace();
            }

            if (_selected.HasVariable)
                EditorGUILayout.HelpBox(
                    "This family is a single variable file. Every weight above comes out of that " +
                    "one download, so taking the full ramp is free.",
                    MessageType.None);

            EditorGUILayout.Space();
            _italics = EditorGUILayout.Toggle("Include italics", _italics);

            // ---- output ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _outputRoot = EditorGUILayout.TextField("Folder", _outputRoot);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    string picked = EditorUtility.SaveFolderPanel("Import fonts into", "Assets", "");
                    if (!string.IsNullOrEmpty(picked) && picked.StartsWith(Application.dataPath))
                        _outputRoot = "Assets" + picked.Substring(Application.dataPath.Length);
                }
            }

            if (!_outputRoot.Contains("/Resources"))
                EditorGUILayout.HelpBox(
                    "Outside a Resources folder the generated stylesheet has to reference the fonts " +
                    "by GUID instead of resource(). That works, but it breaks if the assets are " +
                    "later moved between projects.",
                    MessageType.Warning);

            _upgradeTypeRamp = EditorGUILayout.Toggle(
                new GUIContent("Upgrade type ramp",
                    "Point .ds-h3 at the real 600 face and .ds-caption at 500, which is what " +
                    "Typography.uss asks for and cannot have without a font family."),
                _upgradeTypeRamp);

            // ---- fallbacks ----
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Script fallbacks", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Fonts consulted for characters this family has no glyph for. Leave this empty and " +
                "Unity quietly borrows an OS font, which exists on Windows and does NOT exist in a " +
                "WebGL build — so multilingual text looks perfect in the Editor and renders as empty " +
                "boxes in a browser.",
                MessageType.Warning);

            for (int i = 0; i < _fallbacks.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _fallbacks[i] = (FontAsset)EditorGUILayout.ObjectField(
                        _fallbacks[i], typeof(FontAsset), false);

                    if (GUILayout.Button("−", GUILayout.Width(24)))
                    {
                        _fallbacks.RemoveAt(i);
                        break;
                    }
                }
            }

            if (GUILayout.Button("Add fallback", GUILayout.Width(110)))
                _fallbacks.Add(null);

            // ---- go ----
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_weights.Count == 0))
            {
                if (GUILayout.Button($"Import {_selected.Name}", GUILayout.Height(32)))
                    Import();
            }

            EditorGUILayout.EndScrollView();
        }

        private void Import()
        {
            var built = GoogleFontsImporter.Import(_selected, new GoogleFontsImporter.Options
            {
                Weights = _weights.OrderBy(w => w).ToList(),
                IncludeItalics = _italics,
                OutputRoot = _outputRoot,
                Fallbacks = _fallbacks.Where(f => f).ToList(),
                WriteStylesheet = true,
                UpgradeTypeRamp = _upgradeTypeRamp,
            });

            if (built == null)
            {
                EditorUtility.DisplayDialog("Google Fonts",
                    $"Could not import {_selected.Name}. See the Console.", "OK");
                return;
            }

            Selection.activeObject = built;
            EditorGUIUtility.PingObject(built);
        }

        private void ImportLocal()
        {
            string folder = EditorUtility.OpenFolderPanel(
                "Folder of .ttf / .otf files", "", "");

            if (string.IsNullOrEmpty(folder)) return;

            var files = Directory.GetFiles(folder)
                .Where(f => f.EndsWith(".ttf", System.StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".otf", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                EditorUtility.DisplayDialog("Google Fonts", "No .ttf or .otf files in that folder.", "OK");
                return;
            }

            // The family name comes from the fonts themselves, not the folder, so a folder called
            // "download (3)" still produces an "Inter" family.
            string name = null;
            foreach (string f in files)
            {
                var face = OpenTypeFace.Read(File.ReadAllBytes(f));
                if (face == null) continue;

                name = face.FamilyName;
                break;
            }

            if (name == null)
            {
                EditorUtility.DisplayDialog("Google Fonts", "None of those files parsed as a font.", "OK");
                return;
            }

            var built = GoogleFontsImporter.ImportLocal(name, files, new GoogleFontsImporter.Options
            {
                Weights = AllWeights,
                OutputRoot = _outputRoot,
                Fallbacks = _fallbacks.Where(f => f).ToList(),
                WriteStylesheet = true,
                UpgradeTypeRamp = _upgradeTypeRamp,
            });

            if (built == null) return;

            Selection.activeObject = built;
            EditorGUIUtility.PingObject(built);
        }
    }
}
