using System;
using OSE.App;
using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.Input
{
    public class InputActionRouter : MonoBehaviour, IInputRouter
    {
        public event Action<CanonicalAction> OnAction;

        public InputContext CurrentContext { get; private set; } = InputContext.None;

        private PlayerInput _playerInput;

        private void Awake()
        {
            _playerInput = GetComponent<PlayerInput>();
            if (_playerInput != null)
                _playerInput.notificationBehavior = PlayerNotifications.InvokeUnityEvents;

            ServiceRegistry.Register<IInputRouter>(this);
            ServiceRegistry.Register<InputActionRouter>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Unregister<IInputRouter>();
            ServiceRegistry.Unregister<InputActionRouter>();
        }

        public void SetContext(InputContext context)
        {
            if (CurrentContext == context) return;
            OseLog.VerboseInfo($"[Input] Context: {CurrentContext} → {context}");
            CurrentContext = context;
        }

        // Called by PlayerInput Unity Events (wired in Inspector or via SendMessage)
        public void OnSelect(InputAction.CallbackContext ctx)           { if (ctx.performed) Dispatch(CanonicalAction.Select); }
        public void OnInspect(InputAction.CallbackContext ctx)          { if (ctx.performed) Dispatch(CanonicalAction.Inspect); }
        public void OnGrab(InputAction.CallbackContext ctx)             { if (ctx.performed) Dispatch(CanonicalAction.Grab); }
        public void OnPlace(InputAction.CallbackContext ctx)            { if (ctx.performed) Dispatch(CanonicalAction.Place); }
        public void OnConfirm(InputAction.CallbackContext ctx)          { if (ctx.performed) Dispatch(CanonicalAction.Confirm); }
        public void OnCancel(InputAction.CallbackContext ctx)           { if (ctx.performed) Dispatch(CanonicalAction.Cancel); }
        public void OnRequestHint(InputAction.CallbackContext ctx)      { if (ctx.performed) Dispatch(CanonicalAction.RequestHint); }
        public void OnToggleToolMenu(InputAction.CallbackContext ctx)   { if (ctx.performed) Dispatch(CanonicalAction.ToggleToolMenu); }
        public void OnToolPrimaryAction(InputAction.CallbackContext ctx) { if (ctx.performed) Dispatch(CanonicalAction.ToolPrimaryAction); }
        public void OnTogglePhysicalMode(InputAction.CallbackContext ctx) { if (ctx.performed) Dispatch(CanonicalAction.TogglePhysicalMode); }
        public void OnNext(InputAction.CallbackContext ctx)             { if (ctx.performed) Dispatch(CanonicalAction.Next); }
        public void OnPrevious(InputAction.CallbackContext ctx)         { if (ctx.performed) Dispatch(CanonicalAction.Previous); }
        public void OnPause(InputAction.CallbackContext ctx)            { if (ctx.performed) Dispatch(CanonicalAction.Pause); }
        public void OnChallengeRestart(InputAction.CallbackContext ctx) { if (ctx.performed) Dispatch(CanonicalAction.ChallengeRestart); }

        private void Dispatch(CanonicalAction action)
        {
            if (CurrentContext == InputContext.None) return;
            OseLog.VerboseInfo($"[Input] Action: {action} (context: {CurrentContext})");
            OnAction?.Invoke(action);
            RuntimeEventBus.Publish(new CanonicalActionDispatched(action));
        }

        /// <summary>
        /// Allows adapter components (e.g. XRIInteractionAdapter) to inject a
        /// canonical action as if it came from a binding. Respects context gate.
        /// </summary>
        public void InjectAction(CanonicalAction action) => Dispatch(action);
    }
}
