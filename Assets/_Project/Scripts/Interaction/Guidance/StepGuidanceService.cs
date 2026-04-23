using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Provides step-aware camera guidance. When a step activates,
    /// this service resolves framing bounds from the part bridge
    /// (which uses actual scene renderers) and applies it to the camera rig.
    ///
    /// Recovery affordances:
    /// - <see cref="GoBack"/> — return to the previous step's home framing
    /// - <see cref="GoStepHome"/> — return to the current step's home framing
    ///
    /// All behavior is toggle-gated via InteractionSettings.
    /// </summary>
    public sealed class StepGuidanceService
    {
        private readonly InteractionSettings _settings;
        private readonly AssemblyCameraRig _cameraRig;

        private IPartActionBridge _partBridge;

        private MachinePackageDefinition _package;
        private Func<string, TargetPreviewPlacement> _findTarget;
        private Transform _previewRoot;

        private string _activeStepId;

        // Home framing stack for recovery affordances
        private readonly Stack<CameraState> _homeStack = new();
        private CameraState _currentHome;
        private bool _hasHome;

        public StepGuidanceService(InteractionSettings settings, AssemblyCameraRig cameraRig)
        {
            _settings = settings;
            _cameraRig = cameraRig;

            // Re-frame after each individual tool action completes so the
            // camera follows the next incomplete target across a multi-target
            // Use step (e.g. 4-corner weld). Without this, ReturnFromToolAction
            // snaps back to the step-activation home — which was framed on
            // the FIRST target — so targets #2-N stay out of view.
            RuntimeEventBus.Subscribe<ToolActionCompleted>(OnToolActionCompleted);
        }

        /// <summary>
        /// Set the part bridge for bounds resolution.
        /// Call after bootstrap discovers PartInteractionBridge.
        /// </summary>
        public void SetPartBridge(IPartActionBridge partBridge)
        {
            _partBridge = partBridge;
        }

        /// <summary>
        /// Provide the package context needed for spatial data resolution.
        /// Call this after the package is loaded and the preview scene is set up.
        /// </summary>
        public void SetPackageContext(
            MachinePackageDefinition package,
            Func<string, TargetPreviewPlacement> findTarget,
            Transform previewRoot)
        {
            _package = package;
            _findTarget = findTarget;
            _previewRoot = previewRoot;
        }

        /// <summary>
        /// Subscribe this to RuntimeEventBus.Subscribe&lt;StepActivated&gt;.
        /// </summary>
        public void OnStepActivated(StepActivated evt)
        {
            _activeStepId = evt.StepId;

            if (_cameraRig == null)
                return;

            // Push previous home to stack before overwriting
            if (_hasHome)
                _homeStack.Push(_currentHome);

            // For Use steps with tool-action targets (weld / tack / drill /
            // torque / etc.), frame tight on the first tool target so the
            // user immediately sees WHERE to click. The broader step-focus
            // bounds pulls back to fit the whole assembly, which hides the
            // active work point — especially on multi-target weld steps
            // where 4 corners force a wide view. Falls back to FrameStep
            // when the step has no tool actions.
            if (!TryFrameActiveToolTarget(evt.StepId))
                FrameStep(evt.StepId);

            _currentHome = _cameraRig.TargetState;
            _hasHome = true;

            OseLog.Info($"[StepGuidance] Step '{evt.StepId}' — framed + captured home, dist={_currentHome.Distance:F2}");
        }

        /// <summary>
        /// Subscribed to <see cref="ToolActionCompleted"/>. Re-frames the
        /// camera on the next incomplete tool target in the same step AND
        /// updates the captured home so <see cref="ReturnFromToolAction"/>
        /// returns to that target (not the original first-target framing).
        /// </summary>
        private void OnToolActionCompleted(ToolActionCompleted evt)
        {
            if (_cameraRig == null || string.IsNullOrEmpty(_activeStepId)) return;
            if (!string.Equals(evt.StepId, _activeStepId, StringComparison.Ordinal)) return;

            if (TryFrameActiveToolTarget(_activeStepId))
            {
                // Update home so ReturnFromToolAction snaps to the NEW
                // active target after the next preview finishes its Return
                // phase, instead of the stale step-activation home.
                _currentHome = _cameraRig.TargetState;
                _hasHome = true;
            }
        }

        /// <summary>
        /// If the step has <c>requiredToolActions</c>, frame tightly on the
        /// first INCOMPLETE target using the step's profile framing distance.
        /// Skips already-completed actions via the session snapshot so
        /// subsequent welds / tightens / cuts advance the camera instead of
        /// sitting on a done target. Returns true when it framed; false when
        /// the step has no tool actions, all are completed, or no target
        /// placement resolves.
        /// </summary>
        private bool TryFrameActiveToolTarget(string stepId)
        {
            if (_package == null || _findTarget == null) return false;
            if (!_package.TryGetStep(stepId, out var step)) return false;
            if (step.requiredToolActions == null || step.requiredToolActions.Length == 0) return false;

            // Collect completed targetIds from the session snapshot so we
            // skip past any already-welded corners.
            HashSet<string> completedTargets = null;
            if (ServiceRegistry.TryGet<IMachineSessionController>(out var session) &&
                session?.ToolController != null &&
                session.ToolController.TryGetActionSnapshots(out var snapshots) &&
                snapshots != null)
            {
                completedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < snapshots.Length; i++)
                {
                    var s = snapshots[i];
                    if (s.IsCompleted && !string.IsNullOrWhiteSpace(s.TargetId))
                        completedTargets.Add(s.TargetId);
                }
            }

            foreach (var a in step.requiredToolActions)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.targetId)) continue;
                if (completedTargets != null && completedTargets.Contains(a.targetId)) continue;

                TargetPreviewPlacement placement = _findTarget(a.targetId);
                if (placement == null) continue;

                Vector3 localPos = new Vector3(placement.position.x, placement.position.y, placement.position.z);
                Vector3 worldPos = _previewRoot != null ? _previewRoot.TransformPoint(localPos) : localPos;

                float distance = ToolProfileRegistry.Get(step.profile).FramingDistance;
                _cameraRig.FocusOn(worldPos, distance);
                OseLog.Info($"[StepGuidance] Framed to tool target '{a.targetId}' world={worldPos} distance={distance:F2} profile='{step.profile}'");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Records the step activation (updates active step ID, pushes home stack)
        /// but does NOT frame the camera. Use this when the intro overlay is visible
        /// and framing should be deferred until dismissal.
        /// </summary>
        public void OnStepActivatedNoFrame(StepActivated evt)
        {
            _activeStepId = evt.StepId;

            if (_cameraRig == null)
                return;

            if (_hasHome)
                _homeStack.Push(_currentHome);
        }

        /// <summary>
        /// Captures the current camera target state as the step home framing.
        /// Call after a deferred FrameStep to set up recovery affordances.
        /// </summary>
        public void CaptureHome()
        {
            if (_cameraRig == null) return;
            _currentHome = _cameraRig.TargetState;
            _hasHome = true;
        }

        /// <summary>
        /// Frame the camera for the given step. Can be called externally
        /// for session restore or startup sync.
        /// </summary>
        public void FrameStep(string stepId)
        {
            if (_cameraRig == null || _partBridge == null)
                return;

            if (!_partBridge.TryGetStepFocusBounds(stepId, out Bounds bounds))
            {
                OseLog.Info($"[StepGuidance] Step '{stepId}' — no bounds resolved, skipping frame.");
                return;
            }

            OseLog.Info($"[StepGuidance] Step '{stepId}' — FrameBounds center={bounds.center}, size={bounds.size}");
            _cameraRig.FrameBounds(bounds);
        }

        /// <summary>
        /// Called externally when a step's target position is known
        /// (e.g., resolved from previewConfig by PartInteractionBridge).
        /// </summary>
        public void AutoFrameStepTarget(Vector3 targetPosition)
        {
            if (!_settings.EnableAutoFraming || _cameraRig == null)
                return;

            _cameraRig.FocusOn(targetPosition);
        }

        /// <summary>
        /// Apply a named suggested viewpoint (called from UI buttons).
        /// </summary>
        public void ApplySuggestedView(string viewLabel)
        {
            if (_cameraRig == null) return;

            var vp = viewLabel switch
            {
                "Front" => ViewpointLibrary.Front,
                "Side" => ViewpointLibrary.Side,
                "Top" => ViewpointLibrary.Top,
                "Iso" => ViewpointLibrary.Isometric,
                "Detail" => ViewpointLibrary.Detail,
                _ => ViewpointLibrary.Front
            };

            _cameraRig.ApplyViewpoint(vp, animated: true);
        }

        /// <summary>
        /// Get the list of available viewpoint labels for the current step.
        /// Used by UI to populate suggested view buttons.
        /// </summary>
        public string[] GetAvailableViewpoints()
        {
            if (!_settings.EnableSuggestedViews)
                return Array.Empty<string>();

            return new[] { "Front", "Side", "Top", "Iso", "Detail" };
        }

        // ── Tool Action Framing ──

        /// <summary>
        /// Tightens the camera to a close-up of the tool action target.
        /// Profile determines how close: weld/cut get very tight, torque moderate.
        /// Called when a tool action preview begins.
        /// </summary>
        public void FrameToolAction(Vector3 targetWorldPos, string profile)
        {
            if (_cameraRig == null) return;

            float distance = ToolProfileRegistry.Get(profile).FramingDistance;
            _cameraRig.FocusOn(targetWorldPos, distance);
        }

        /// <summary>
        /// Returns the camera to the step home framing after a tool action preview ends.
        /// The smooth interpolation in the camera rig makes this a gentle ease-back.
        /// </summary>
        public void ReturnFromToolAction()
        {
            if (_cameraRig == null || !_hasHome) return;
            ApplyHome();
        }

        // ── Recovery Affordances ──

        /// <summary>
        /// Return the camera to the previous step's home framing.
        /// </summary>
        public bool GoBack()
        {
            if (_cameraRig == null || _homeStack.Count == 0)
                return false;

            _currentHome = _homeStack.Pop();
            ApplyHome();
            return true;
        }

        /// <summary>
        /// Return the camera to the current step's home framing.
        /// </summary>
        public bool GoStepHome()
        {
            if (_cameraRig == null || !_hasHome)
                return false;

            ApplyHome();
            return true;
        }

        /// <summary>True when <see cref="GoBack"/> has a previous framing to return to.</summary>
        public bool CanGoBack => _homeStack.Count > 0;

        /// <summary>True when <see cref="GoStepHome"/> can restore a home framing.</summary>
        public bool CanGoStepHome => _hasHome;

        private void ApplyHome()
        {
            _cameraRig.FocusOn(_currentHome.PivotPosition, _currentHome.Distance);
        }
    }
}
