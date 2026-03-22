using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Provides step-aware camera guidance. When a step activates,
    /// this service resolves the step's <see cref="ViewMode"/>, computes
    /// framing from package spatial data, and applies it to the camera rig.
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

            if (!_settings.EnableStepViewGuidance || _cameraRig == null)
                return;

            if (_package == null || !_package.TryGetStep(evt.StepId, out StepDefinition step))
                return;

            // Resolve view mode
            ViewMode mode = ViewModeResolver.Resolve(step);

            // Compute framing from spatial data
            Bounds assemblyBounds = _cameraRig.PivotResolver != null
                ? new Bounds(_cameraRig.PivotResolver.Resolve(null), Vector3.one * 0.5f)
                : new Bounds(Vector3.zero, Vector3.one);

            var framing = StepFramingComputer.Compute(mode, step, _package, _findTarget, assemblyBounds);

            // Transform from local to world space
            Vector3 worldPivot = _previewRoot != null
                ? _previewRoot.TransformPoint(framing.Pivot)
                : framing.Pivot;

            // Push previous home to stack before overwriting
            if (_hasHome)
                _homeStack.Push(_currentHome);

            // Apply framing to camera rig
            if (framing.UseBounds && _previewRoot != null)
            {
                // Transform bounds center to world
                Bounds worldBounds = new Bounds(worldPivot, framing.Bounds.size);
                _cameraRig.FrameBounds(worldBounds);
            }
            else
            {
                _cameraRig.FocusOn(worldPivot, framing.Distance);
            }

            // Capture home framing (target state after applying commands)
            _currentHome = _cameraRig.TargetState;
            _hasHome = true;

            OseLog.VerboseInfo($"[StepGuidance] Step '{evt.StepId}' → {mode}, pivot={worldPivot}, dist={framing.Distance:F2}");
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
