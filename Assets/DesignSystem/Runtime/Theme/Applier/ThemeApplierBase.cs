using DesignSystem.Runtime.Theme.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier
{
    [DisallowMultipleComponent]
    public abstract class ThemeApplierBase<TComponent> : MonoBehaviour where TComponent : Component
    {
        [SerializeField] protected ThemeData theme;

        public ThemeData Theme
        {
            get => theme;
            set
            {
                RemoveStyleSheet();
                theme = value;
                ApplyThemeToRoot();
            }
        }

        protected abstract void OnEnable();

        protected virtual void OnDisable()
        {
            RemoveStyleSheet();
        }

        protected abstract void ApplyThemeToRoot();

        protected void ApplyTheme(VisualElement root)
        {
            RemoveStyleSheet();
            if (!theme || root == null) return;

            var sheet = theme.StyleSheet;
            if (!sheet)
            {
                Debug.LogWarning(
                    $"[ThemeApplier] Theme '{theme.name}' has no companion .uss. " +
                    "Open it in Theme Configurator and click 'Save .uss'.", theme);
                return;
            }

            if (!root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);
        }

        private void RemoveStyleSheet()
        {
            if (!theme) return;
            var sheet = theme.StyleSheet;
            if (!sheet) return;

            var root = GetCurrentRoot();
            if (root != null && root.styleSheets.Contains(sheet))
                root.styleSheets.Remove(sheet);
        }

        protected abstract VisualElement GetCurrentRoot();
    }
}
