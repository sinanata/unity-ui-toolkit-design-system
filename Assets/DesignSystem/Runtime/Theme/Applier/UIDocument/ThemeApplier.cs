using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier.UIDocument
{
    [UnityEngine.AddComponentMenu("Design System/UI Document Theme Applier")]
    [UnityEngine.RequireComponent(typeof(UnityEngine.UIElements.UIDocument))]
    public class ThemeApplier : ThemeApplierBase<UnityEngine.UIElements.UIDocument>
    {
        private UnityEngine.UIElements.UIDocument _doc;

        protected override void OnEnable()
        {
            if (!TryGetComponent(out _doc)) return;

            // UIDocument builds its visual tree in its own OnEnable, and component order is
            // not something we get to pick — so on the frame we come up first, there is no
            // root to theme yet. Poll until there is.
            if (_doc.rootVisualElement == null)
            {
                Invoke(nameof(TryInit), 0.05f);
                return;
            }

            ApplyTheme(_doc.rootVisualElement);
        }

        protected override void OnDisable()
        {
            CancelInvoke(nameof(TryInit));   // else a disable/enable cycle stacks pending retries
            base.OnDisable();
        }

        private void TryInit()
        {
            if (!_doc) return;

            if (_doc.rootVisualElement == null)
            {
                Invoke(nameof(TryInit), 0.05f);
                return;
            }

            ApplyTheme(_doc.rootVisualElement);
        }

        protected override void ApplyThemeToRoot()
        {
            if (!_doc) return;
            ApplyTheme(_doc.rootVisualElement);
        }

        protected override VisualElement GetCurrentRoot() => _doc ? _doc.rootVisualElement : null;
    }
}
