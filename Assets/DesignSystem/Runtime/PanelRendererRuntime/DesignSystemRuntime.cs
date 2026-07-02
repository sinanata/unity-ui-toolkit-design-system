#if UNITY_6000_5_OR_NEWER
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.PanelRendererRuntime
{
    [AddComponentMenu("Design System/Panel Renderer Runtime")]
    public class DesignSystemRuntime : DesignSystemRuntimeBase<PanelRenderer>
    {
        private PanelRenderer _panelRenderer;
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

        private void OnUIReload(PanelRenderer pRenderer, VisualElement root, int version)
        {
            if (_uiVersion == version || root == null) return;
            InitFor(root);
            _uiVersion = version;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _RegisterAutoAttach() => RegisterAutoAttach();
    }
}
#endif