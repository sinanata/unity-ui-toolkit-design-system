#if UNITY_6000_5_OR_NEWER
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Fx
{
    /// <summary>
    /// Entry points for the fire-and-forget material FX system.
    ///
    /// Elements opt in through marker classes on themselves:
    ///   <code>class="ds-btn ds-fx-blueprint ds-fx-text-carve"</code>
    /// and you call <see cref="ApplyMarkers"/> once per composed screen. From then on the element
    /// hovers, presses, ripples and animates entirely on its own — see <see cref="DsFxManager"/>
    /// for why that costs nothing per frame.
    ///
    /// To material-theme a whole design-system tree instead of marking elements by hand, use
    /// <see cref="DsFxTheme.Apply"/>, which writes these same markers for you.
    /// </summary>
    public static class DsFx
    {
        private static readonly ConditionalWeakTable<VisualElement, DsFxSkin> Skins =
            new ConditionalWeakTable<VisualElement, DsFxSkin>();

        /// <summary>Marker recording that a subtree walk already skinned this element.</summary>
        public const string WiredClass = "ds-fx-wired";

        /// <summary>Skin one element explicitly.</summary>
        public static DsFxSkin Apply(VisualElement element, DsFxSpec spec)
        {
            if (element == null || spec == null)
                return null;
            if (Skins.TryGetValue(element, out var existing))
                return existing;
            var skin = new DsFxSkin(spec);
            element.AddToClassList(WiredClass);
            element.AddManipulator(skin);
            Skins.Add(element, skin);
            return skin;
        }

        /// <summary>
        /// Walk a composed screen and skin every element carrying ds-fx- markers. Call it AFTER the
        /// tree is attached and laid out.
        ///
        /// Idempotent: elements already wired are left alone, so re-composition after a screen
        /// change costs one class check per element.
        /// </summary>
        public static int ApplyMarkers(VisualElement root)
        {
            if (root == null)
                return 0;
            var applied = 0;
            Walk(root, ref applied);
            return applied;
        }

        private static void Walk(VisualElement el, ref int applied)
        {
            if (!el.ClassListContains(WiredClass))
            {
                var spec = DsFxSpec.FromClasses(el.GetClasses());
                if (spec != null && Apply(el, spec) != null)
                    applied++;
            }
            var count = el.hierarchy.childCount;
            for (var i = 0; i < count; i++)
                Walk(el.hierarchy[i], ref applied);
        }

        /// <summary>The skin on an element, if any.</summary>
        public static DsFxSkin SkinOf(VisualElement element)
            => element != null && Skins.TryGetValue(element, out var s) ? s : null;

        /// <summary>
        /// Unskin one element: the manipulator comes off, which is what restores its
        /// <c>unityMaterial</c> and any background the skin forced (see
        /// <see cref="DsFxSkin"/>'s unregister path). The element renders as stock again.
        ///
        /// This does NOT remove the ds-fx- marker classes — a later <see cref="ApplyMarkers"/>
        /// would then just re-skin it. Callers who mean "stop being this material" strip the
        /// markers too; <see cref="DsFxTheme.Revert"/> does both.
        /// </summary>
        public static bool Remove(VisualElement element)
        {
            if (element == null || !Skins.TryGetValue(element, out var skin))
                return false;
            element.RemoveManipulator(skin);
            element.RemoveFromClassList(WiredClass);
            Skins.Remove(element);
            return true;
        }

        /// <summary>Unskin every skinned element under root (inclusive). Returns how many.</summary>
        public static int RemoveAll(VisualElement root)
        {
            if (root == null)
                return 0;
            var removed = 0;
            Walk(root, ref removed);
            return removed;

            static void Walk(VisualElement el, ref int removed)
            {
                if (Remove(el))
                    removed++;
                var count = el.hierarchy.childCount;
                for (var i = 0; i < count; i++)
                    Walk(el.hierarchy[i], ref removed);
            }
        }

        /// <summary>
        /// Re-wire a subtree whose elements CARRY the wired marker but never got a skin — the
        /// drag-ghost case: the design-system runtime builds a ghost by copying the dragged item's
        /// classes, marker and all, into a fresh element. Strips the stale marker wherever no skin
        /// is registered, then applies markers.
        /// </summary>
        public static int Rewire(VisualElement root)
        {
            if (root == null)
                return 0;
            Strip(root);
            return ApplyMarkers(root);

            static void Strip(VisualElement el)
            {
                if (el.ClassListContains(WiredClass) && !Skins.TryGetValue(el, out _))
                    el.RemoveFromClassList(WiredClass);
                var count = el.hierarchy.childCount;
                for (var i = 0; i < count; i++)
                    Strip(el.hierarchy[i]);
            }
        }

        /// <summary>Play the OUT animation on every skinned element under root, then run onDone
        /// (scheduled on the root) once the longest animation has finished.</summary>
        public static void PlayOut(VisualElement root, Action onDone = null, float duration = 0.6f)
        {
            if (root == null)
            {
                onDone?.Invoke();
                return;
            }
            var any = false;
            root.Query<VisualElement>().ForEach(el =>
            {
                if (Skins.TryGetValue(el, out var skin))
                {
                    skin.PlayOut(duration);
                    any = true;
                }
            });
            if (onDone != null)
            {
                if (any)
                    root.schedule.Execute(onDone).StartingIn((long)(duration * 1000) + 40);
                else
                    onDone();
            }
        }
    }
}
#endif
