using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.Input
{
    public class InputContextController : MonoBehaviour
    {
        [SerializeField] private InputActionRouter _router;

        private void Awake()
        {
            if (_router == null)
                _router = GetComponent<InputActionRouter>();
        }

        public void SetFrontend()       => _router.SetContext(InputContext.Frontend);
        public void SetMachineSelect()  => _router.SetContext(InputContext.MachineSelection);
        public void SetSessionActive()  => _router.SetContext(InputContext.SessionActive);
        public void SetStepInteraction()=> _router.SetContext(InputContext.StepInteraction);
        public void SetInspection()     => _router.SetContext(InputContext.Inspection);
        public void SetPaused()         => _router.SetContext(InputContext.Paused);
        public void SetChallengeSummary()=> _router.SetContext(InputContext.ChallengeSummary);
        public void ClearContext()      => _router.SetContext(InputContext.None);
    }
}
