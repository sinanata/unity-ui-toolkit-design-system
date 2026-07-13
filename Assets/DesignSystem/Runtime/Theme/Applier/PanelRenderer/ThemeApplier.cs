#if UNITY_6000_5_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier.PanelRenderer
{
    [AddComponentMenu("Design System/Panel Renderer Theme Applier")]
    [RequireComponent(typeof(UnityEngine.UIElements.PanelRenderer))]
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
            _uiVersion = 0;
            base.OnDisable();
        }

        private void OnUIReload(UnityEngine.UIElements.PanelRenderer pRenderer, VisualElement root, int version)
        {
            if (_uiVersion == version || root == null) return;
            _root = root;
            ApplyTheme(root);
            _uiVersion = version;
        }

        protected override void ApplyThemeToRoot()
        {
            if (!_panelRenderer || _root == null) return;
            ApplyTheme(_root);
        }

        protected override VisualElement GetCurrentRoot()
        {
            return _root;
        }
    }
}
#endif
