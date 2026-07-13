using System.Runtime.CompilerServices;
using DesignSystem.Runtime.Theme.Data;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Theme.Applier
{
    /// <summary>
    /// Puts a <see cref="ThemeData"/> onto a <see cref="VisualElement"/> root, and takes it off
    /// again. Two things happen: the theme's baked stylesheet is added to the root, and — if the
    /// theme is class-scoped — the class it is scoped to goes on alongside it.
    ///
    /// That is the entire theming mechanism. The <c>ThemeApplier</c> components are thin wrappers
    /// over these two calls, and it is a static seam rather than component-only because a host
    /// that builds its panels in code has nowhere to hang a MonoBehaviour — the showcase's
    /// world-space corridor spawns one PanelRenderer per section at runtime and repaints them all
    /// from a single call.
    ///
    /// EACH ROOT REMEMBERS WHAT IT IS WEARING, so no caller has to. <see cref="Apply"/> takes the
    /// previous theme off before putting the new one on, which means swapping, re-applying and
    /// clearing are all just <c>Apply</c>. Callers that track "the theme I applied last" get this
    /// wrong the moment a root is rebuilt under them (a PanelRenderer hands you a brand new
    /// VisualElement on every UI reload) and leave a dead stylesheet behind, with the old theme
    /// still painting underneath the new one.
    ///
    /// WHERE YOU ADD THE SHEET MATTERS. UI Toolkit resolves equal-specificity rules by stylesheet
    /// order, and a sheet added LAST to an element wins ties on that element. Unity scores `:root`
    /// and a class selector identically (both 256), so a theme's `:root { --token: … }` block does
    /// NOT outrank a `.theme-light { --token: … }` block by specificity — it wins only by going on
    /// last. So a theme has to land on the same element the base tokens resolve on, which is the
    /// panel or document root where `DesignSystem.uss` itself lands. Put it on an ancestor instead
    /// and any token block attached further down will shadow it for that subtree, because a rule
    /// matching an element beats a value the element merely inherited.
    /// </summary>
    public static class ThemeRuntime
    {
        // Weak keys: when a root is thrown away (and a PanelRenderer throws one away on every UI
        // reload) its entry goes with it. A plain Dictionary here would pin every root the
        // showcase has ever built.
        private static readonly ConditionalWeakTable<VisualElement, ThemeData> Worn = new();

        /// <summary>The theme <paramref name="root"/> is currently wearing, or null.</summary>
        public static ThemeData WornBy(VisualElement root) =>
            root != null && Worn.TryGetValue(root, out var theme) ? theme : null;

        /// <summary>
        /// Dresses <paramref name="root"/> in <paramref name="theme"/>, taking off whatever it was
        /// wearing first. Passing null just undresses it, so this one call covers "apply", "swap"
        /// and "back to the base tokens".
        ///
        /// Returns false when nothing was applied: no root, no theme, or a theme that has never
        /// been baked and therefore has no stylesheet to add.
        /// </summary>
        public static bool Apply(VisualElement root, ThemeData theme)
        {
            if (root == null) return false;

            // Re-applying the theme a root ALREADY wears, correctly, is a no-op — and has to be.
            // Adding or removing a stylesheet bumps the panel's style version, and a version bump
            // re-resolves every element under that panel from scratch; a re-resolve rebuilds each
            // element's ComputedStyle, which is a handful of ref-counted native blocks. So the
            // Clear-then-Add below is not "cheap because nothing changes", it is two full-panel
            // restyles that produce an identical result.
            //
            // That is affordable once. The showcase repaints ~30 world panels from a single theme
            // change, and most of those panels are already wearing the theme being applied, so
            // without this guard one toggle bought ~60 pointless full-panel restyles. It is the
            // difference between a theme swap being free and it being the most expensive thing on
            // the frame.
            //
            // "Correctly" is the load-bearing word: the sheet must actually still be attached and
            // the scope class actually still on the element. Callers legitimately strip both (the
            // corridor re-asserts `theme-light` after every paint), so trusting the bookkeeping
            // alone would let a root drift out of its own theme and never be repainted.
            if (theme && WornBy(root) == theme)
            {
                var worn = theme.StyleSheet;
                var wornClass = theme.ScopeClass;
                if (worn && root.styleSheets.Contains(worn) &&
                    (wornClass == null || root.ClassListContains(wornClass)))
                    return true;
            }

            Clear(root);

            if (!theme) return false;

            var sheet = theme.StyleSheet;
            if (!sheet) return false;

            var scopeClass = theme.ScopeClass;
            if (scopeClass != null && !root.ClassListContains(scopeClass))
                root.AddToClassList(scopeClass);

            // Last in the list, so it wins ties against everything already there.
            if (!root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);

            Worn.Remove(root);
            Worn.Add(root, theme);
            return true;
        }

        /// <summary>
        /// Takes whatever theme <paramref name="root"/> is wearing back off, returning it to the
        /// base tokens. Idempotent, and a no-op on a root that was never themed.
        /// </summary>
        public static void Clear(VisualElement root)
        {
            if (root == null || !Worn.TryGetValue(root, out var theme)) return;

            Worn.Remove(root);
            if (!theme) return;

            var sheet = theme.StyleSheet;
            if (sheet && root.styleSheets.Contains(sheet))
                root.styleSheets.Remove(sheet);

            var scopeClass = theme.ScopeClass;
            if (scopeClass != null)
                root.RemoveFromClassList(scopeClass);
        }

        /// <summary>
        /// Adds a theme's stylesheet WITHOUT its scope class, so the rules are present but inert
        /// until something else puts the class on the element. That is how a day / night PAIR
        /// works: both sheets sit on the root permanently and one class toggle picks which paints.
        ///
        /// The root is NOT recorded as wearing this theme, because it isn't — <see cref="Clear"/>
        /// will not take it off again. This installs a permanent layer; use <see cref="Apply"/> for
        /// a theme you intend to swap.
        ///
        /// Meaningless for a <c>:root</c> theme: with no class to gate it, it paints the moment it
        /// is added, which is what <see cref="Apply"/> already does.
        /// </summary>
        public static bool InstallSheet(VisualElement root, ThemeData theme)
        {
            if (root == null || !theme) return false;

            var sheet = theme.StyleSheet;
            if (!sheet) return false;

            if (!root.styleSheets.Contains(sheet))
                root.styleSheets.Add(sheet);

            return true;
        }
    }
}
