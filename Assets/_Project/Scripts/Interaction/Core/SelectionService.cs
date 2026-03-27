using OSE.App;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Tracks which interactable is currently selected or inspected.
    /// Consumes canonical actions via RuntimeEventBus; never polls XRI directly.
    /// </summary>
    public class SelectionService : MonoBehaviour
    {
        public GameObject CurrentSelection { get; private set; }
        public GameObject CurrentInspection { get; private set; }

        private void Awake()
        {
            ServiceRegistry.Register<SelectionService>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Unregister<SelectionService>();
        }

        private void OnEnable()
        {
            RuntimeEventBus.Subscribe<CanonicalActionDispatched>(HandleActionEvent);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<CanonicalActionDispatched>(HandleActionEvent);
        }

        private void HandleActionEvent(CanonicalActionDispatched evt)
        {
            switch (evt.Action)
            {
                case CanonicalAction.Select:
                    OseLog.VerboseInfo("[Selection] Select action received.");
                    break;
                case CanonicalAction.Inspect:
                    OseLog.VerboseInfo("[Selection] Inspect action received.");
                    break;
                case CanonicalAction.Cancel:
                    Deselect();
                    break;
            }
        }

        public void NotifySelected(GameObject target)
        {
            CurrentSelection = target;
            OseLog.VerboseInfo($"[Selection] Selected: {target?.name}");
            RuntimeEventBus.Publish(new PartSelected(target));
        }

        public void NotifyInspected(GameObject target)
        {
            CurrentInspection = target;
            OseLog.VerboseInfo($"[Selection] Inspected: {target?.name}");
            RuntimeEventBus.Publish(new PartInspected(target));
        }

        public void Deselect()
        {
            if (CurrentSelection == null) return;
            var previous = CurrentSelection;
            CurrentSelection = null;
            CurrentInspection = null;
            OseLog.VerboseInfo($"[Selection] Deselected: {previous?.name}");
            RuntimeEventBus.Publish(new PartDeselected(previous));
        }
    }
}
