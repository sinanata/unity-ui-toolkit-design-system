using System;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Behaviour
{
    /// <summary>
    /// Hooks the design system offers a HOST application, for the few moments a host cannot reach on
    /// its own.
    ///
    /// Deliberately NOT on the behaviour base. That class is generic
    /// (<c>DesignSystemBehaviourBase&lt;TComponent&gt;</c>), and a static event declared there would
    /// exist once per CLOSED generic type: a host subscribing through the UIDocument backend would
    /// never hear an event raised through the PanelRenderer one, and the bug would look like "it works
    /// on screen and does nothing in world space". One copy, both backends raise through it.
    /// </summary>
    public static class DesignSystemEvents
    {
        /// <summary>
        /// Raised once per dropdown popup instance, right after the design system has placed and sized
        /// it.
        ///
        /// Almost no host needs this. A theme that lives in a stylesheet reaches the popup by itself:
        /// the popup is parented at panel scope, so tokens installed on the panel root cascade into it
        /// for free. This exists for the host whose palette is invented at RUNTIME and therefore has no
        /// stylesheet to install — it has to paint the popup by hand, and it has no other way to reach
        /// one. Unity builds the popup fresh under the panel root on every open and destroys it on
        /// close, so it is not in the host's tree, cannot be pre-styled, and does not need cleaning up.
        /// </summary>
        public static event Action<DropdownField, VisualElement> DropdownPopupOpened;

        internal static void RaiseDropdownPopupOpened(DropdownField field, VisualElement menu) =>
            DropdownPopupOpened?.Invoke(field, menu);

        internal static int DropdownPopupSubscribers =>
            DropdownPopupOpened?.GetInvocationList()?.Length ?? 0;

        /// <summary>
        /// Log every number the dropdown popup tuner computes, to the player log / browser console.
        /// Off by default; a host turns it on when the popup misbehaves somewhere it cannot be
        /// debugged, which for a WebGL build is everywhere. The showcase wires it to a `?dsdebug=1`
        /// query string.
        /// </summary>
        public static bool DropdownDiagnostics;
    }
}
