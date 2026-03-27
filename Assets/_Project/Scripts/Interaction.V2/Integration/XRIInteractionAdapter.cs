using System.Collections.Generic;
using OSE.App;
using OSE.Core;
using OSE.Input;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace OSE.Interaction
{
    /// <summary>
    /// Routes XRI interactor events into the canonical OSE action model.
    /// Attach alongside an XRI interactor. Never call runtime logic directly
    /// from here — always dispatch through IInputRouter.
    ///
    /// Note: Confirm/Activate is handled by the Input System path
    /// (XRControllers control scheme in OSEInputActions) — the XRI 3.x
    /// activate model does not expose a public event on the interactor side.
    /// </summary>
    public class XRIInteractionAdapter : MonoBehaviour
    {
        private enum GestureHandSide
        {
            Unknown,
            Left,
            Right,
        }

        private const float DefaultNearDistanceThreshold = 0.22f;
        private const float DefaultPinchDistanceThreshold = 0.035f;

        [SerializeField] private InputActionRouter _actionRouter;
        [SerializeField] private SelectionService _selectionService;
        [SerializeField] private bool _enableHandGestureSelectBridge = true;
        [SerializeField, Min(0.01f)] private float _nearDistanceThreshold = DefaultNearDistanceThreshold;
        [SerializeField, Min(0.005f)] private float _pinchDistanceThreshold = DefaultPinchDistanceThreshold;

        private IXRSelectInteractor _selectInteractor;
        private GestureHandSide _gestureHandSide = GestureHandSide.Unknown;
        private IXRSelectInteractable _gestureSelectedInteractable;
        private bool _gestureSelectionUsesGrab;

        private static readonly List<XRHandSubsystem> HandSubsystems = new List<XRHandSubsystem>();
        private static XRHandSubsystem _handSubsystem;

        private void Awake()
        {
            _selectInteractor = GetComponent<IXRSelectInteractor>();
            if (_selectInteractor == null)
                _selectInteractor = GetComponent<XRBaseInteractor>() as IXRSelectInteractor;

            _gestureHandSide = ResolveHandSide(transform);
        }

        private void Update()
        {
            UpdateHandGestureSelectBridge();
        }

        private void OnEnable()
        {
            EnsureDependencies(logMissingRouter: true);

            if (_selectInteractor is XRBaseInteractor baseInteractor)
            {
                baseInteractor.selectEntered.AddListener(OnSelectEntered);
                baseInteractor.selectExited.AddListener(OnSelectExited);
            }
        }

        private void OnDisable()
        {
            ReleaseGestureSelection();

            if (_selectInteractor is XRBaseInteractor baseInteractor)
            {
                baseInteractor.selectEntered.RemoveListener(OnSelectEntered);
                baseInteractor.selectExited.RemoveListener(OnSelectExited);
            }
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            EnsureDependencies(logMissingRouter: false);

            GameObject target = args.interactableObject?.transform.gameObject;
            if (target != null)
                _selectionService?.NotifySelected(target);

            DispatchToRouter(CanonicalAction.Grab);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (ReferenceEquals(args.interactableObject, _gestureSelectedInteractable))
            {
                _gestureSelectedInteractable = null;
                _gestureSelectionUsesGrab = false;
            }

            EnsureDependencies(logMissingRouter: false);
            DispatchToRouter(CanonicalAction.Place);
            _selectionService?.Deselect();
        }

        private void DispatchToRouter(CanonicalAction action)
        {
            // XRI events are forwarded as canonical actions via InjectAction,
            // ensuring the runtime never has a direct XRI dependency.
            _actionRouter?.InjectAction(action);
        }

        private void EnsureDependencies(bool logMissingRouter)
        {
            if (_actionRouter == null)
                ServiceRegistry.TryGet<InputActionRouter>(out _actionRouter);

            if (_selectionService == null)
                ServiceRegistry.TryGet<SelectionService>(out _selectionService);

            if (logMissingRouter && _actionRouter == null)
                OseLog.Warn("[XRIInteractionAdapter] No InputActionRouter found in scene.");
        }

        private void UpdateHandGestureSelectBridge()
        {
            if (!_enableHandGestureSelectBridge || _gestureHandSide == GestureHandSide.Unknown)
                return;

            if (!(_selectInteractor is XRBaseInteractor baseInteractor))
                return;

            if (!TryGetTrackedHand(_gestureHandSide, out XRHand trackedHand))
            {
                ReleaseGestureSelection();
                return;
            }

            bool isPinching = IsPinching(trackedHand);
            bool isGrabbing = IsGrabbing(trackedHand);

            if (_gestureSelectedInteractable != null)
            {
                bool shouldKeepSelection = _gestureSelectionUsesGrab ? isGrabbing : isPinching;
                if (!shouldKeepSelection || !baseInteractor.IsSelecting(_gestureSelectedInteractable))
                    ReleaseGestureSelection();
                return;
            }

            if (baseInteractor.hasSelection)
                return;

            IXRSelectInteractable hoveredInteractable = GetHoveredSelectInteractable(baseInteractor);
            if (hoveredInteractable == null)
                return;

            bool isNearTarget = IsNearTarget(baseInteractor.transform.position, hoveredInteractable.transform.position);
            bool shouldSelect = isNearTarget ? isGrabbing : isPinching;
            if (!shouldSelect)
                return;

            XRInteractionManager interactionManager = baseInteractor.interactionManager;
            if (interactionManager == null)
                return;

            interactionManager.SelectEnter(baseInteractor, hoveredInteractable);

            if (baseInteractor.IsSelecting(hoveredInteractable))
            {
                _gestureSelectedInteractable = hoveredInteractable;
                _gestureSelectionUsesGrab = isNearTarget;
            }
        }

        private void ReleaseGestureSelection()
        {
            if (_gestureSelectedInteractable == null)
            {
                _gestureSelectionUsesGrab = false;
                return;
            }

            if (_selectInteractor is XRBaseInteractor baseInteractor &&
                baseInteractor.interactionManager != null &&
                baseInteractor.IsSelecting(_gestureSelectedInteractable))
            {
                baseInteractor.interactionManager.SelectExit(baseInteractor, _gestureSelectedInteractable);
            }

            _gestureSelectedInteractable = null;
            _gestureSelectionUsesGrab = false;
        }

        private IXRSelectInteractable GetHoveredSelectInteractable(XRBaseInteractor baseInteractor)
        {
            IXRSelectInteractable fallback = null;
            List<IXRHoverInteractable> hovered = baseInteractor.interactablesHovered;
            for (int i = 0; i < hovered.Count; i++)
            {
                IXRHoverInteractable hoveredInteractable = hovered[i];
                if (!(hoveredInteractable is IXRSelectInteractable selectInteractable))
                    continue;

                if (selectInteractable is XRGrabInteractable)
                    return selectInteractable;

                fallback ??= selectInteractable;
            }

            return fallback;
        }

        private bool IsNearTarget(Vector3 handPosition, Vector3 targetPosition)
        {
            float maxNearDistanceSqr = _nearDistanceThreshold * _nearDistanceThreshold;
            return (targetPosition - handPosition).sqrMagnitude <= maxNearDistanceSqr;
        }

        private bool IsPinching(XRHand hand)
        {
            if (!TryGetJointPosition(hand, XRHandJointID.ThumbTip, out Vector3 thumbTip))
                return false;

            if (!TryGetJointPosition(hand, XRHandJointID.IndexTip, out Vector3 indexTip))
                return false;

            float pinchDistanceSqr = (thumbTip - indexTip).sqrMagnitude;
            float pinchThresholdSqr = _pinchDistanceThreshold * _pinchDistanceThreshold;
            return pinchDistanceSqr <= pinchThresholdSqr;
        }

        private static bool IsGrabbing(XRHand hand)
        {
            return IsFingerCurled(hand, XRHandJointID.IndexTip, XRHandJointID.IndexProximal) &&
                   IsFingerCurled(hand, XRHandJointID.MiddleTip, XRHandJointID.MiddleProximal) &&
                   IsFingerCurled(hand, XRHandJointID.RingTip, XRHandJointID.RingProximal) &&
                   IsFingerCurled(hand, XRHandJointID.LittleTip, XRHandJointID.LittleProximal);
        }

        private static bool IsFingerCurled(XRHand hand, XRHandJointID tipJoint, XRHandJointID proximalJoint)
        {
            if (!TryGetJointPosition(hand, XRHandJointID.Wrist, out Vector3 wrist))
                return false;

            if (!TryGetJointPosition(hand, tipJoint, out Vector3 tip))
                return false;

            if (!TryGetJointPosition(hand, proximalJoint, out Vector3 proximal))
                return false;

            return (tip - wrist).sqrMagnitude <= (proximal - wrist).sqrMagnitude;
        }

        private static bool TryGetJointPosition(XRHand hand, XRHandJointID jointId, out Vector3 position)
        {
            XRHandJoint joint = hand.GetJoint(jointId);
            if (joint.TryGetPose(out Pose pose))
            {
                position = pose.position;
                return true;
            }

            position = default;
            return false;
        }

        private static bool TryGetTrackedHand(GestureHandSide handSide, out XRHand hand)
        {
            if (TryGetHandSubsystem(out XRHandSubsystem subsystem))
            {
                hand = handSide == GestureHandSide.Left ? subsystem.leftHand : subsystem.rightHand;
                if (hand.isTracked)
                    return true;
            }

            hand = default;
            return false;
        }

        private static bool TryGetHandSubsystem(out XRHandSubsystem subsystem)
        {
            if (_handSubsystem != null && _handSubsystem.running)
            {
                subsystem = _handSubsystem;
                return true;
            }

            SubsystemManager.GetSubsystems(HandSubsystems);
            for (int i = 0; i < HandSubsystems.Count; i++)
            {
                XRHandSubsystem candidate = HandSubsystems[i];
                if (candidate == null || !candidate.running)
                    continue;

                _handSubsystem = candidate;
                subsystem = candidate;
                return true;
            }

            _handSubsystem = null;
            subsystem = null;
            return false;
        }

        private static GestureHandSide ResolveHandSide(Transform origin)
        {
            Transform current = origin;
            while (current != null)
            {
                string name = current.name.ToLowerInvariant();
                if (name.Contains("left hand"))
                    return GestureHandSide.Left;
                if (name.Contains("right hand"))
                    return GestureHandSide.Right;

                current = current.parent;
            }

            return GestureHandSide.Unknown;
        }
    }
}
