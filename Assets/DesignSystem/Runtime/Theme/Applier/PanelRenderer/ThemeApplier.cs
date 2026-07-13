#if UNITY_6000_5_OR_NEWER
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier.PanelRenderer
{
    [UnityEngine.AddComponentMenu("Design System/Panel Renderer Theme Applier")]
    [UnityEngine.RequireComponent(typeof(UnityEngine.UIElements.PanelRenderer))]
    public class ThemeApplier : ThemeApplierBase<UnityEngine.UIElements.PanelRenderer>
    {
        private UnityEngine.UIElements.PanelRenderer _panelRenderer;
        private VisualElement _root;
        private int _uiVersion;

        protected override void OnEnable()
        {
            if (!TryGetComponent(out _panelRenderer)) return;
            _panelRenderer.RegisterUIReloadCallback(OnUIReload);
        }

        protected override void OnDisable()
        {
            _panelRenderer?.UnregisterUIReloadCallback(OnUIReload);
            base.OnDisable();       // uses GetCurrentRoot(), so _root has to still be live here
            _root = null;
            _uiVersion = 0;
        }

        // A PanelRenderer rebuilds its visual tree on reload and hands us a BRAND NEW root.
        // The stylesheet we put on the old one went with it, so every reload has to re-apply
        // — which ApplyTheme does, because the base tracks what it last applied and the new
        // root simply has nothing to take off.
        private void OnUIReload(UnityEngine.UIElements.PanelRenderer renderer, VisualElement root, int version)
        {
            if (root == null || _uiVersion == version) return;

            _root = root;
            _uiVersion = version;
            ApplyTheme(root);
        }

        protected override void ApplyThemeToRoot()
        {
            if (!_panelRenderer || _root == null) return;
            ApplyTheme(_root);
        }

        protected override VisualElement GetCurrentRoot() => _root;
    }
}
#endif
