using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace OSE.UI.Bindings
{
    /// <summary>
    /// XRI SDK implementation of <see cref="IXRInputConfigurator"/>.
    /// Finds the scene's <see cref="XRUIInputModule"/> and binds all UI input actions
    /// from the supplied <see cref="InputActionAsset"/>.
    /// </summary>
    internal sealed class XRUIInputConfigurator : IXRInputConfigurator
    {
        private InputActionReference _pointActionReference;
        private InputActionReference _leftClickActionReference;
        private InputActionReference _middleClickActionReference;
        private InputActionReference _rightClickActionReference;
        private InputActionReference _scrollWheelActionReference;
        private InputActionReference _navigateActionReference;
        private InputActionReference _submitActionReference;
        private InputActionReference _cancelActionReference;

        public bool TryConfigure(InputActionAsset inputActions)
        {
            if (!Application.isPlaying)
                return false;

            if (inputActions == null)
                return false;

            EventSystem eventSystem = EventSystem.current != null
                ? EventSystem.current
                : Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
                return false;

            XRUIInputModule uiInputModule = eventSystem.GetComponent<XRUIInputModule>();
            if (uiInputModule == null)
                return false;

            InputActionMap uiActionMap = inputActions.FindActionMap("XRI UI", throwIfNotFound: false);
            if (uiActionMap == null)
                return false;

            uiInputModule.pointAction       = GetOrCreate(uiActionMap, "Point",       ref _pointActionReference);
            uiInputModule.leftClickAction   = GetOrCreate(uiActionMap, "Click",        ref _leftClickActionReference);
            uiInputModule.middleClickAction = GetOrCreate(uiActionMap, "MiddleClick",  ref _middleClickActionReference);
            uiInputModule.rightClickAction  = GetOrCreate(uiActionMap, "RightClick",   ref _rightClickActionReference);
            uiInputModule.scrollWheelAction = GetOrCreate(uiActionMap, "ScrollWheel",  ref _scrollWheelActionReference);
            uiInputModule.navigateAction    = GetOrCreate(uiActionMap, "Navigate",     ref _navigateActionReference);
            uiInputModule.submitAction      = GetOrCreate(uiActionMap, "Submit",       ref _submitActionReference);
            uiInputModule.cancelAction      = GetOrCreate(uiActionMap, "Cancel",       ref _cancelActionReference);

            uiInputModule.enableBuiltinActionsAsFallback = false;

            return uiInputModule.pointAction       != null
                && uiInputModule.leftClickAction   != null
                && uiInputModule.scrollWheelAction != null
                && uiInputModule.navigateAction    != null
                && uiInputModule.submitAction      != null
                && uiInputModule.cancelAction      != null;
        }

        /// <summary>Destroys all cached InputActionReference ScriptableObjects. Call from OnDestroy.</summary>
        internal void DestroyActionReferences()
        {
            DestroyRef(ref _pointActionReference);
            DestroyRef(ref _leftClickActionReference);
            DestroyRef(ref _middleClickActionReference);
            DestroyRef(ref _rightClickActionReference);
            DestroyRef(ref _scrollWheelActionReference);
            DestroyRef(ref _navigateActionReference);
            DestroyRef(ref _submitActionReference);
            DestroyRef(ref _cancelActionReference);
        }

        private static void DestroyRef(ref InputActionReference r)
        {
            if (r == null) return;
            if (Application.isPlaying) Object.Destroy(r);
            else Object.DestroyImmediate(r);
            r = null;
        }

        private static InputActionReference GetOrCreate(
            InputActionMap actionMap,
            string actionName,
            ref InputActionReference cached)
        {
            if (cached != null)
                return cached;
            InputAction action = actionMap.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
                return null;
            cached = InputActionReference.Create(action);
            return cached;
        }
    }
}
