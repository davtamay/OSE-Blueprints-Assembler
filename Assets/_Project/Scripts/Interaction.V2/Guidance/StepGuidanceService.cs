using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEngine;

namespace OSE.Interaction.V2
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

            // Frame the camera using bounds from the part bridge.
            // The bridge resolves bounds from actual scene renderers (ghosts,
            // spawned parts, tool targets) which are the most accurate source.
            FrameStep(evt.StepId);

            _currentHome = _cameraRig.TargetState;
            _hasHome = true;

            OseLog.Info($"[StepGuidance] Step '{evt.StepId}' — framed + captured home, dist={_currentHome.Distance:F2}");
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

            float distance = GetToolActionFramingDistance(profile);
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

        private static float GetToolActionFramingDistance(string profile)
        {
            if (string.IsNullOrEmpty(profile))
                return 0.6f;

            if (profile.Equals(ToolActionProfiles.Weld, System.StringComparison.OrdinalIgnoreCase)
                || profile.Equals(ToolActionProfiles.Cut, System.StringComparison.OrdinalIgnoreCase))
                return 0.35f; // very close — see the joint detail

            if (profile.Equals(ToolActionProfiles.Torque, System.StringComparison.OrdinalIgnoreCase))
                return 0.45f; // close enough to see the bolt rotation

            if (profile.Equals(ToolActionProfiles.Strike, System.StringComparison.OrdinalIgnoreCase))
                return 0.5f;

            return 0.6f; // default
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
