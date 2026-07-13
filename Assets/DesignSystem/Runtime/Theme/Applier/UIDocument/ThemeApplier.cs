using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier.UIDocument
{
    [AddComponentMenu("Design System/UI Document Theme Applier")]
    [RequireComponent(typeof(UnityEngine.UIElements.UIDocument))]
    public class ThemeApplier : ThemeApplierBase<UnityEngine.UIElements.UIDocument>
    {
        private UnityEngine.UIElements.UIDocument _doc;

        protected override void OnEnable()
        {
            if (!TryGetComponent(out _doc)) return;

            var root = _doc.rootVisualElement;
            if (root == null)
            {
                Invoke(nameof(TryInit), 0.05f);
                return;
            }

            ApplyTheme(root);
        }

        private void TryInit()
        {
            if (!_doc) return;
            var root = _doc.rootVisualElement;
            if (root == null) { Invoke(nameof(TryInit), 0.05f); return; }
            ApplyTheme(root);
        }

        protected override void ApplyThemeToRoot()
        {
            if (!_doc) return;
            ApplyTheme(_doc.rootVisualElement);
        }

        protected override VisualElement GetCurrentRoot()
        {
            return _doc ? _doc.rootVisualElement : null;
        }
    }
}
