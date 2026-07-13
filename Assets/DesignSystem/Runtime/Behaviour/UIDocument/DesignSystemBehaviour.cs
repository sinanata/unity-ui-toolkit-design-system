using UnityEngine;

namespace DesignSystem.Runtime.Behaviour.UIDocument
{
    [AddComponentMenu("Design System/UI Document Behaviour")]
    public class DesignSystemBehaviour : DesignSystemBehaviourBase<UnityEngine.UIElements.UIDocument>
    {
        private UnityEngine.UIElements.UIDocument _doc;

        protected override void OnEnable()
        {
            if (!TryGetComponent(out _doc)) return;
            
            var root = _doc.rootVisualElement;
            if (root == null)
            {
                // The visual tree hasn't materialised yet (common when this
                // component is added in Awake). Defer one frame.
                _doc.rootVisualElement?.schedule.Execute(() => InitFor(_doc.rootVisualElement)).StartingIn(0);
                // Fallback: poll briefly until the root exists.
                SchedulePollRoot();
                return;
            }
            InitFor(root);
        }

        private void SchedulePollRoot()
        {
            // schedule via a temporary helper element since UIDocument.schedule
            // isn't available without a root. Use MonoBehaviour-side coroutine
            // semantics through Invoke.
            Invoke(nameof(TryInit), 0.05f);
        }
        
        private void TryInit()
        {
            if (_doc == null) return;
            var root = _doc.rootVisualElement;
            if (root == null) { Invoke(nameof(TryInit), 0.05f); return; }
            InitFor(root);
        }
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void _RegisterAutoAttach() => RegisterAutoAttach(typeof(DesignSystemBehaviour));
    }
}