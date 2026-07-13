using DesignSystem.Runtime.Theme.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier
{
    /// <summary>
    /// Drop this on a UI host and its root wears the assigned <see cref="ThemeData"/>.
    /// Assign <see cref="Theme"/> at runtime to swap themes; assign null to fall back to
    /// the base tokens in <c>DesignTokens.uss</c>.
    ///
    /// All the actual work is <see cref="ThemeRuntime"/>. This class exists to bind that
    /// to a component lifecycle and to the two UI Toolkit backends.
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class ThemeApplierBase<TComponent> : MonoBehaviour where TComponent : Component
    {
        [SerializeField] protected ThemeData theme;

        public ThemeData Theme
        {
            get => theme;
            set
            {
                theme = value;
                ApplyThemeToRoot();
            }
        }

        protected abstract void OnEnable();

        protected virtual void OnDisable() => ThemeRuntime.Clear(GetCurrentRoot());

        protected abstract void ApplyThemeToRoot();

        protected abstract VisualElement GetCurrentRoot();

        protected void ApplyTheme(VisualElement root)
        {
            if (root == null) return;

            if (theme && !theme.StyleSheet)
            {
                Debug.LogWarning(
                    $"[ThemeApplier] Theme '{theme.name}' has no baked stylesheet, so it will not " +
                    "paint anything. Open it in Design System > Theme Configurator and press Save, " +
                    "or run Design System > Rebake All Themes.", theme);
            }

            // ThemeRuntime tracks what each root wears, so this takes the previous theme off for
            // us — including across a PanelRenderer reload, which hands us a brand new root.
            ThemeRuntime.Apply(root, theme);
        }
    }
}
