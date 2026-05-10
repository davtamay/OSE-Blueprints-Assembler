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
        private FramingArbiter _arbiter;

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

            // Re-frame after a part-translating animation cue ends so the
            // camera doesn't sit on the animated part's final position
            // (e.g. step 57 "return rod" — pivot stayed on the rod's end
            // pose). Priority is the active task subject: tool target, then
            // step bounds (parts/ghosts/groups/targets) — same path used at
            // step activation.
            RuntimeEventBus.Subscribe<CueCameraFollowStopped>(OnCueCameraFollowStopped);
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
        /// Wire the framing arbiter that owns step-entry framing writes.
        /// When set, <see cref="FrameStep"/> routes through the arbiter so
        /// every step-entry frame goes through one telemetry-tagged path.
        /// Falls back to direct rig writes when null (test/bootstrap paths).
        /// </summary>
        public void SetFramingArbiter(FramingArbiter arbiter)
        {
            _arbiter = arbiter;
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

            // Suppress one-shot frame when the prior step's instant placement
            // just completed. The user clicked → tool snapped to the
            // workpiece → step auto-advanced — they're already looking at
            // the right area, so framing the next step's first target reads
            // as a snap forward tied to the placement event. The flag is
            // consumed exactly once; manual nav / restore still go through
            // FrameStep normally.
            if (_suppressNextActivationFrame)
            {
                _suppressNextActivationFrame = false;
                _currentHome = _cameraRig.TargetState;
                _hasHome = true;
                OseLog.Info($"[StepGuidance] Step '{evt.StepId}' — frame suppressed (prior instant placement); home captured at current view.");
                return;
            }

            // FrameStep already prefers tool-target tight focus on Use
            // steps and falls back to step-bounds otherwise — same path
            // used by StepGuidanceCoordinator's deferred reframe so the
            // initial frame and the post-transition reframe match.
            FrameStep(evt.StepId);

            _currentHome = _cameraRig.TargetState;
            _hasHome = true;

            OseLog.Info($"[StepGuidance] Step '{evt.StepId}' — framed + captured home, dist={_currentHome.Distance:F2}");
        }

        private bool _suppressNextActivationFrame;
        private bool _suppressNextDeferredFrame;

        /// <summary>True while a one-shot deferred-frame suppression is pending.
        /// Consumed (cleared) by the coordinator's deferred reframe coroutine.</summary>
        public bool ConsumeDeferredFrameSuppression()
        {
            bool wasSet = _suppressNextDeferredFrame;
            _suppressNextDeferredFrame = false;
            return wasSet;
        }

        /// <summary>
        /// Marks that the next <see cref="OnStepActivated"/> firing AND the
        /// coordinator's paired deferred reframe should NOT move the camera —
        /// only update active step + capture home at the current view.
        /// Called by the tool action coordinator after a successful
        /// instant-placement conversion (clamps, fixtures): the user just
        /// placed the tool exactly where they were looking, so auto-framing
        /// the next step's target would read as a forward snap. One-shot —
        /// both flags clear after their respective consumption.
        /// </summary>
        public void SuppressNextActivationFrame()
        {
            _suppressNextActivationFrame = true;
            _suppressNextDeferredFrame = true;
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
        /// Subscribed to <see cref="CueCameraFollowStopped"/>. When a
        /// part-translating animation cue ends (e.g. a "return rod"
        /// pose-transition with holdAtEnd), the camera rig leaves the pivot
        /// on the animated part's final position. Re-run the step framing
        /// so priority returns to the active task subject — tool target,
        /// then step bounds (parts/ghosts/groups/targets).
        /// </summary>
        private void OnCueCameraFollowStopped(CueCameraFollowStopped evt)
        {
            if (_cameraRig == null || string.IsNullOrEmpty(_activeStepId)) return;

            // Honor the same suppression instant-placement uses for the
            // step-activation path. A pose-transition cue triggered during
            // the placement action can finish moments AFTER step auto-
            // advance, and its post-cue FrameStep would yank the camera
            // into the next step's framing — exactly the "settles then
            // snaps" symptom. Skip the reframe here too if suppression
            // is pending, then consume it.
            if (_suppressNextActivationFrame || _suppressNextDeferredFrame)
            {
                _suppressNextActivationFrame = false;
                _suppressNextDeferredFrame = false;
                _currentHome = _cameraRig.TargetState;
                _hasHome = true;
                OseLog.Info("[StepGuidance] Cue-follow stopped — frame suppressed (prior instant placement); home captured at current view.");
                return;
            }

            // Minimum-move gate: when a follow cue ends with the camera
            // pivot already inside the active step's task-weighted bounds,
            // re-running FrameStep would cause a visible re-snap to the
            // bounds.center even though the subject is already in view.
            // Skip the reframe in that case — just refresh home so recovery
            // affordances (ReturnFromToolAction / GoStepHome) snap back to
            // wherever the cue left the camera.
            Bounds activeBounds = default;
            bool boundsResolved = false;
            if (_arbiter != null)
                boundsResolved = _arbiter.TryResolveStepBounds(_activeStepId, out activeBounds);
            else if (_partBridge != null)
                boundsResolved = _partBridge.TryGetStepFocusBounds(_activeStepId, out activeBounds);
            if (boundsResolved && activeBounds.Contains(_cameraRig.TargetState.PivotPosition))
            {
                _currentHome = _cameraRig.TargetState;
                _hasHome = true;
                OseLog.Info($"[StepGuidance] Cue-follow stopped — pivot already inside step '{_activeStepId}' bounds, skipping reframe.");
                return;
            }

            FrameStep(_activeStepId);

            // Refresh home so GoStepHome / ReturnFromToolAction snap to the
            // post-animation framing, not the rod's final position.
            _currentHome = _cameraRig.TargetState;
            _hasHome = true;
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
                FrameToolTargetUnobstructed(a.targetId, worldPos, distance, step.profile);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Drives the camera to <paramref name="worldPos"/> via the vantage solver,
        /// then falls back to plain <see cref="AssemblyCameraRig.FocusOn"/> if no
        /// unobstructed angle was found in any tier. Worst-case ~14 SphereCasts
        /// on a discrete StepActivated/ToolActionCompleted event — no caching
        /// needed.
        /// </summary>
        private void FrameToolTargetUnobstructed(string targetId, Vector3 worldPos, float distance, string profile)
        {
            LayerMask occluderMask = _settings != null ? _settings.VantageOccluderMask : (LayerMask)~0;
            float probeRadius = _settings != null ? _settings.VantageProbeRadius : 0.05f;
            float nearTargetIgnore = _settings != null ? _settings.VantageNearTargetIgnore : 0.15f;

            bool solved = _cameraRig.TryFocusOnUnobstructed(
                worldPos, distance, occluderMask, probeRadius, nearTargetIgnore,
                ignoreRoots: null, out var result);

            if (solved)
            {
                OseLog.Info($"[StepGuidance] Framed tool target '{targetId}' world={worldPos} dist={result.Distance:F2} yaw={result.Yaw:F1}° tier={result.Tier} casts={result.CastsPerformed} profile='{profile}'");
            }
            else
            {
                _cameraRig.FocusOn(worldPos, distance);
                OseLog.Warn($"[StepGuidance] Vantage solver found NO clear angle for tool target '{targetId}' (casts={result.CastsPerformed}) — fell back to FocusOn at current yaw. profile='{profile}'");
            }
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
        /// Frame the camera for the given step. Default path uses the
        /// task-weighted bounds from <see cref="StepFocusComputer"/> via
        /// <see cref="PartInteractionBridge.TryGetStepFocusBounds"/>: that
        /// resolver already pivots on cursor.OpenTasks (part / tool-action
        /// associated part / partGroup proxy / wire ports) and blends with
        /// step context, so every step entry frames the same way regardless
        /// of family. Only <see cref="OnToolActionCompleted"/> uses the
        /// per-target tight focus (<see cref="TryFrameActiveToolTarget"/>)
        /// to advance to the next bolt/weld corner mid-step. Mixing the two
        /// paths on entry was the source of "Use step close, next step far"
        /// alternation.
        /// </summary>
        public void FrameStep(string stepId)
        {
            if (_cameraRig == null) return;

            if (_arbiter != null)
            {
                _arbiter.RequestFrameForStep(stepId, reason: "StepGuidance.FrameStep");
                return;
            }

            // Fallback: arbiter not wired yet (early bootstrap, tests).
            if (_partBridge == null) return;
            if (!_partBridge.TryGetStepFocusBounds(stepId, out Bounds bounds))
            {
                OseLog.Info($"[StepGuidance] Step '{stepId}' — no bounds resolved, skipping frame.");
                return;
            }
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
