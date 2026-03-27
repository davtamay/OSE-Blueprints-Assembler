using System;
using OSE.App;
using OSE.Core;
using OSE.Input;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Tracks which interactable is currently selected or inspected.
    /// Consumes canonical actions from IInputRouter; never polls XRI directly.
    /// </summary>
    public class SelectionService : MonoBehaviour
    {
        public event Action<GameObject> OnSelected;
        public event Action<GameObject> OnDeselected;
        public event Action<GameObject> OnInspected;

        public GameObject CurrentSelection { get; private set; }
        public GameObject CurrentInspection { get; private set; }

        [SerializeField] private InputActionRouter _router;

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
            if (_router == null)
                ServiceRegistry.TryGet<InputActionRouter>(out _router);

            if (_router != null)
                _router.OnAction += HandleAction;
        }

        private void OnDisable()
        {
            if (_router != null)
                _router.OnAction -= HandleAction;
        }

        private void HandleAction(CanonicalAction action)
        {
            switch (action)
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
            OnSelected?.Invoke(target);
            RuntimeEventBus.Publish(new PartSelected(target));
        }

        public void NotifyInspected(GameObject target)
        {
            CurrentInspection = target;
            OseLog.VerboseInfo($"[Selection] Inspected: {target?.name}");
            OnInspected?.Invoke(target);
            RuntimeEventBus.Publish(new PartInspected(target));
        }

        public void Deselect()
        {
            if (CurrentSelection == null) return;
            var previous = CurrentSelection;
            CurrentSelection = null;
            CurrentInspection = null;
            OseLog.VerboseInfo($"[Selection] Deselected: {previous?.name}");
            OnDeselected?.Invoke(previous);
            RuntimeEventBus.Publish(new PartDeselected(previous));
        }
    }
}
