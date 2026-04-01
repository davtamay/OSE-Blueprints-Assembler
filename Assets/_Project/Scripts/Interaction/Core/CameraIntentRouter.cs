using System;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Routes camera-related intents (orbit, pan, zoom, focus) to the
    /// <see cref="AssemblyCameraRig"/>. Extracted from InteractionOrchestrator
    /// to keep camera concerns in a focused class.
    /// </summary>
    internal sealed class CameraIntentRouter
    {
        private readonly AssemblyCameraRig _cameraRig;
        private readonly Camera _camera;

        /// <summary>
        /// Delegate the orchestrator provides to validate + normalize a hit target.
        /// Returns null when the hit is not a valid part.
        /// </summary>
        private readonly Func<GameObject, GameObject> _validateAndNormalize;

        /// <summary>Returns true when tool mode locks out part interaction.</summary>
        private readonly Func<bool> _isToolModeLocked;

        /// <summary>Returns true when the given part is locked for movement.</summary>
        private readonly Func<GameObject, bool> _isPartLockedForMovement;

        public CameraIntentRouter(
            AssemblyCameraRig cameraRig,
            Camera camera,
            Func<GameObject, GameObject> validateAndNormalize,
            Func<bool> isToolModeLocked,
            Func<GameObject, bool> isPartLockedForMovement)
        {
            _cameraRig = cameraRig;
            _camera = camera;
            _validateAndNormalize = validateAndNormalize;
            _isToolModeLocked = isToolModeLocked;
            _isPartLockedForMovement = isPartLockedForMovement;
        }

        /// <summary>
        /// Routes an orbit / pan / zoom intent to the camera rig.
        /// Handles contextual scroll (depth-adjust while dragging or with selected part).
        /// Returns the <see cref="InteractionState"/> the orchestrator should transition to,
        /// or null if no transition is needed (e.g. depth-adjust consumed the intent).
        /// </summary>
        public InteractionState? RouteCameraIntent(
            InteractionIntent intent,
            InteractionState currentState,
            GameObject selectedPart,
            GameObject draggedPart)
        {
            if (intent.IntentKind == InteractionIntent.Kind.Zoom)
            {
                float scrollAmount = intent.ScrollDelta + intent.PinchDelta;

                // -- Contextual scroll: depth adjust vs camera zoom --
                // While dragging → always depth-adjust the dragged part
                if (currentState == InteractionState.DraggingPart && draggedPart != null && _camera != null)
                {
                    draggedPart.transform.position += _camera.transform.forward * scrollAmount * 5f;
                    return null;
                }

                // Scroll over the selected part → depth-adjust it (forward/backward)
                if (selectedPart != null && intent.HitTarget != null)
                {
                    GameObject validHit = _validateAndNormalize(intent.HitTarget);
                    if (validHit != null &&
                        validHit == selectedPart &&
                        _camera != null &&
                        !_isToolModeLocked() &&
                        !_isPartLockedForMovement(selectedPart))
                    {
                        selectedPart.transform.position += _camera.transform.forward * scrollAmount * 5f;
                        return null;
                    }
                }

                // Otherwise → camera zoom
                if (_cameraRig != null)
                {
                    InteractionState? transition = null;
                    if (!IsCameraState(currentState) && currentState != InteractionState.DraggingPart)
                        transition = InteractionState.CameraZoom;
                    _cameraRig.ApplyZoom(scrollAmount);
                    return transition;
                }
                return null;
            }

            // Never orbit/pan while dragging a part
            if (currentState == InteractionState.DraggingPart)
                return null;

            if (_cameraRig == null) return null;

            switch (intent.IntentKind)
            {
                case InteractionIntent.Kind.Orbit:
                    _cameraRig.ApplyOrbit(intent.ScreenDelta);
                    return InteractionState.CameraOrbit;

                case InteractionIntent.Kind.Pan:
                    _cameraRig.ApplyPan(intent.ScreenDelta);
                    return InteractionState.CameraPan;
            }

            return null;
        }

        /// <summary>Focus camera on the selected part, or reset to default if none selected.</summary>
        public void HandleFocus(GameObject selectedPart)
        {
            if (_cameraRig == null) return;

            if (selectedPart != null)
                _cameraRig.FocusOn(selectedPart.transform.position);
            else
                _cameraRig.ResetToDefault();
        }

        /// <summary>Returns true if the state is one of the camera-manipulation states.</summary>
        public static bool IsCameraState(InteractionState state) =>
            state is InteractionState.CameraOrbit
                or InteractionState.CameraPan
                or InteractionState.CameraZoom;
    }
}
