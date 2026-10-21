using System;
using UnityEngine.UIElements;

namespace DesignSystem.Runtime.Controls
{
    /// <summary>
    /// Transient notices with no MonoBehaviour behind them:
    /// <code>DsToast.Show(screenRoot, "Saved", "success");</code>
    ///
    /// <see cref="Show"/> finds the <c>.ds-toast-stack</c> directly under the host, creates one anchored
    /// to the top if there is none, adds a <c>.ds-toast</c> to it, and takes the toast away again after
    /// <c>durationMs</c> using the panel's own scheduler. The stack is added as a HIERARCHY child, so on
    /// a ScrollView host it floats over the scrolled content instead of scrolling away with it. To anchor
    /// at the bottom, author <c>&lt;ui:VisualElement class="ds-toast-stack ds-toast-stack--bottom" picking-mode="Ignore"/&gt;</c>
    /// under the host in UXML; Show uses whichever stack it finds there.
    ///
    /// Motion is all USS (Competitive.uss): a toast arrives with <c>is-entering</c>, which comes off
    /// after its first layout so it transitions in, and leaves with <c>is-leaving</c>, being removed when
    /// that transition ends. Under a <c>.ds-reduced-motion</c> ancestor it appears and goes without
    /// either. The newest toast sits nearest the anchored edge; past <see cref="MaxVisible"/> the oldest
    /// leaves early.
    ///
    /// Kinds: "info" (default), "success", "warning", "danger" ("error" also means danger), compared
    /// ignoring case; anything else is info. The close button is deliberately not focusable: a notice
    /// that happens to be on screen must not take a place in a gamepad's focus order.
    ///
    /// The timers ride the panel scheduler, which a world-space PanelRenderer panel does not tick in a
    /// player; show toasts on screen-space panels.
    /// </summary>
    public static class DsToast
    {
        public const string StackClass = "ds-toast-stack";
        public const string StackTopClass = "ds-toast-stack--top";
        public const string StackBottomClass = "ds-toast-stack--bottom";
        public const string EnteringClass = "is-entering";
        public const string LeavingClass = "is-leaving";
        public const string ReducedMotionClass = "ds-reduced-motion";

        private const string ToastClass = "ds-toast";
        private const string ToastIconClass = "ds-toast__icon";
        private const string MessageClass = "ds-toast__message";
        private const string CloseClass = "ds-toast__close";
        private const string IconClass = "ds-icon";
        private const string IconSmallClass = "ds-icon--sm";
        private const string CloseGlyphClass = "ds-icon--close";

        // Comfortably longer than the exit the stylesheet ships (--transition-fast). A transition that
        // never runs (a zero duration from a theme, a panel hidden mid-exit) raises no TransitionEndEvent,
        // and the toast must still go.
        private const long LeaveFallbackMs = 600;

        private static readonly EventCallback<GeometryChangedEvent> s_OnFirstLayout = OnFirstLayout;
        private static readonly EventCallback<TransitionEndEvent> s_OnLeft = OnLeft;

        /// <summary>Most toasts one stack shows at once; when another arrives, the oldest leaves.</summary>
        public static int MaxVisible { get; set; } = 3;

        /// <summary>
        /// Show <paramref name="text"/> as a toast under <paramref name="host"/> (normally a screen root)
        /// for <paramref name="durationMs"/> milliseconds; zero or less keeps it until it is closed or
        /// passed to <see cref="Dismiss"/>. Returns the toast element.
        /// </summary>
        public static VisualElement Show(VisualElement host, string text, string kind = "info", int durationMs = 4000)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));

            var stack = FindOrCreateStack(host);
            var toast = Build(text, kind);

            var newestFirst = !stack.ClassListContains(StackBottomClass);
            if (newestFirst) stack.Insert(0, toast);
            else stack.Add(toast);
            TrimOverflow(stack, newestFirst);

            if (!IsReducedMotion(stack))
            {
                toast.AddToClassList(EnteringClass);
                toast.RegisterCallback(s_OnFirstLayout);
            }

            if (durationMs > 0)
                toast.schedule.Execute(() => Dismiss(toast)).StartingIn(durationMs);
            return toast;
        }

        /// <summary>Send <paramref name="toast"/> out now (animated unless motion is reduced). Idempotent.</summary>
        public static void Dismiss(VisualElement toast)
        {
            if (toast == null || toast.hierarchy.parent == null || toast.ClassListContains(LeavingClass)) return;
            if (toast.panel == null || IsReducedMotion(toast))
            {
                toast.RemoveFromHierarchy();
                return;
            }

            toast.RemoveFromClassList(EnteringClass);   // dismissed before it finished arriving
            toast.AddToClassList(LeavingClass);
            toast.RegisterCallback(s_OnLeft);
            toast.schedule.Execute(() => toast.RemoveFromHierarchy()).StartingIn(LeaveFallbackMs);
        }

        private static VisualElement FindOrCreateStack(VisualElement host)
        {
            // Hierarchy children first: that is where a stack Show created lives. Then content
            // children, where a UXML-authored stack lands when the host is a ScrollView or a template.
            var children = host.hierarchy;
            for (var i = 0; i < children.childCount; i++)
                if (children[i].ClassListContains(StackClass)) return Prepare(children[i]);

            var content = host.contentContainer;
            if (content != null && content != host)
                for (var i = 0; i < content.childCount; i++)
                    if (content[i].ClassListContains(StackClass)) return Prepare(content[i]);

            var stack = new VisualElement { name = StackClass };
            stack.AddToClassList(StackClass);
            stack.AddToClassList(StackTopClass);
            host.hierarchy.Add(stack);
            return Prepare(stack);
        }

        private static VisualElement Prepare(VisualElement stack)
        {
            // Absolute and full width: a stack that caught the pointer would eat every click in its band.
            // The toasts themselves stay pickable (their close button).
            stack.pickingMode = PickingMode.Ignore;
            stack.BringToFront();   // over whatever the host added after it
            return stack;
        }

        private static VisualElement Build(string text, string kind)
        {
            ResolveKind(kind, out var kindClass, out var glyphClass);

            var toast = new VisualElement();
            toast.AddToClassList(ToastClass);
            toast.AddToClassList(kindClass);

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(IconClass);
            icon.AddToClassList(IconSmallClass);
            icon.AddToClassList(glyphClass);
            icon.AddToClassList(ToastIconClass);
            toast.Add(icon);

            var message = new Label(text) { pickingMode = PickingMode.Ignore };
            message.AddToClassList(MessageClass);
            toast.Add(message);

            var close = new Button(() => Dismiss(toast)) { focusable = false };
            close.AddToClassList(CloseClass);
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList(IconClass);
            glyph.AddToClassList(IconSmallClass);
            glyph.AddToClassList(CloseGlyphClass);
            close.Add(glyph);
            toast.Add(close);
            return toast;
        }

        private static void ResolveKind(string kind, out string toastClass, out string glyphClass)
        {
            if (Is(kind, "success"))
            {
                toastClass = "ds-toast--success";
                glyphClass = "ds-icon--check";
            }
            else if (Is(kind, "warning"))
            {
                toastClass = "ds-toast--warning";
                glyphClass = "ds-icon--warning";
            }
            else if (Is(kind, "danger") || Is(kind, "error"))
            {
                toastClass = "ds-toast--danger";
                glyphClass = "ds-icon--error";
            }
            else
            {
                toastClass = "ds-toast--info";
                glyphClass = "ds-icon--info";
            }

            static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // Dismiss the oldest live toasts beyond MaxVisible. Dismiss can remove a toast on the spot
        // (reduced motion), so both walks are written to survive their own removals.
        private static void TrimOverflow(VisualElement stack, bool newestFirst)
        {
            var live = 0;
            for (var i = 0; i < stack.childCount; i++)
                if (IsLiveToast(stack[i])) live++;

            var excess = live - Math.Max(1, MaxVisible);
            if (excess <= 0) return;

            if (newestFirst)
            {
                // Oldest at the end: walk backwards, removals only touch indices already passed.
                for (var i = stack.childCount - 1; i >= 0 && excess > 0; i--)
                {
                    var child = stack[i];
                    if (!IsLiveToast(child)) continue;
                    excess--;
                    Dismiss(child);
                }
            }
            else
            {
                // Oldest at the front: step past a toast only if it is still there (animating out).
                var i = 0;
                while (i < stack.childCount && excess > 0)
                {
                    var child = stack[i];
                    if (!IsLiveToast(child)) { i++; continue; }
                    excess--;
                    Dismiss(child);
                    if (child.hierarchy.parent == stack) i++;
                }
            }
        }

        private static bool IsLiveToast(VisualElement el) =>
            el.ClassListContains(ToastClass) && !el.ClassListContains(LeavingClass);

        private static bool IsReducedMotion(VisualElement el)
        {
            for (var e = el; e != null; e = e.hierarchy.parent)
                if (e.ClassListContains(ReducedMotionClass)) return true;
            return false;
        }

        private static void OnFirstLayout(GeometryChangedEvent evt)
        {
            if (evt.currentTarget is not VisualElement toast) return;
            toast.UnregisterCallback(s_OnFirstLayout);
            // Its first style pass (opacity 0, offset) has been computed by now, so taking the class
            // off is a style CHANGE, which is what a transition needs to start from.
            toast.RemoveFromClassList(EnteringClass);
        }

        private static void OnLeft(TransitionEndEvent evt)
        {
            // An icon's tint transition can end inside a leaving toast too; only the toast's own counts.
            if (evt.target != evt.currentTarget) return;
            if (evt.currentTarget is VisualElement toast && toast.ClassListContains(LeavingClass))
                toast.RemoveFromHierarchy();
        }
    }
}
