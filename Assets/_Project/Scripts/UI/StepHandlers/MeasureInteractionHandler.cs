using System;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles the two-phase measure interaction:
    /// Phase 1 — user taps the start anchor (normal tool action).
    /// Phase 2 — end anchor marker spawns, live line tracks cursor,
    ///           user taps end anchor to complete measurement and step.
    /// </summary>
    internal sealed class MeasureInteractionHandler
    {
        private readonly ISpawnerContext _ctx;
        private readonly ToolTargetSpawner _spawner;
        private readonly ToolTargetAnimator _animator;

        private StepMeasurementPayload _measurePayload;
        private Vector3? _measureAnchorAWorldPos;
        private AnchorToAnchorInteraction _anchorInteraction;

        public MeasureInteractionHandler(
            ISpawnerContext ctx,
            ToolTargetSpawner spawner,
            ToolTargetAnimator animator)
        {
            _ctx = ctx;
            _spawner = spawner;
            _animator = animator;
        }

        /// <summary>True when the anchor-to-anchor interaction is actively tracking.</summary>
        public bool IsActive => _anchorInteraction != null && _anchorInteraction.IsActive;

        /// <summary>The current measurement payload, if any.</summary>
        public StepMeasurementPayload Payload => _measurePayload;

        // ====================================================================
        //  Lifecycle
        // ====================================================================

        /// <summary>Initialises measure state from the activated step definition.</summary>
        public void OnStepActivated(StepDefinition step)
        {
            var rawMeasurement = step.measurement;
            _measurePayload = (rawMeasurement != null && rawMeasurement.IsConfigured) ? rawMeasurement : null;
            _measureAnchorAWorldPos = null;
            Cleanup();
        }

        /// <summary>Ticks the anchor interaction (live line update).</summary>
        public void Tick()
        {
            _anchorInteraction?.Tick();
        }

        /// <summary>Tears down the anchor interaction and resets state.</summary>
        public void Cleanup()
        {
            if (_anchorInteraction != null)
            {
                _anchorInteraction.NearBChanged -= HighlightEndAnchorMarker;
                _anchorInteraction.Completed -= OnAnchorInteractionCompleted;
                _anchorInteraction.Cleanup();
                _anchorInteraction = null;
            }
        }

        /// <summary>Full reset — clears payload and anchor position as well.</summary>
        public void Reset()
        {
            Cleanup();
            _measurePayload = null;
            _measureAnchorAWorldPos = null;
        }

        // ====================================================================
        //  Phase transitions
        // ====================================================================

        /// <summary>
        /// Records the start anchor world position (called after phase-1 click effect).
        /// </summary>
        public void SetAnchorAWorldPos(Vector3 pos)
        {
            _measureAnchorAWorldPos = pos;
        }

        /// <summary>
        /// Begins phase 2: spawns end anchor marker, starts live measurement line.
        /// Call after the first anchor tool action succeeds.
        /// </summary>
        public void BeginPhase2()
        {
            if (!_measureAnchorAWorldPos.HasValue || _measurePayload == null)
                return;

            if (!_spawner.TryResolveToolActionTargetPose(
                    GetPackage(), _measurePayload.endAnchorTargetId,
                    out Vector3 pos, out Quaternion rot, out Vector3 scale))
            {
                OseLog.Warn($"[MeasureInteractionHandler] Could not resolve end anchor pose for '{_measurePayload.endAnchorTargetId}'.");
                return;
            }

            if (_spawner.TryGetPreviewTargetPose(_measurePayload.endAnchorTargetId, out Vector3 gp, out Quaternion gr, out Vector3 gs))
            {
                pos = gp;
                rot = gr;
                scale = gs;
            }

            _spawner.SpawnMeasureEndMarker(
                _measurePayload.endAnchorTargetId, pos, rot, scale,
                ToolTargetAnimator.ToolTargetIdleColor);

            Vector3 endWorldPos = _spawner.ResolveMarkerWorldPos(pos, scale);

            float screenThreshold = StepHandlerConstants.Proximity.GetThreshold();
            Color liveColor = new Color(1f, 0.8f, 0.2f, 0.9f);
            Color resultColor = new Color(1f, 0.8f, 0.2f, 1f);
            var config = new AnchorToAnchorInteraction.Config
            {
                AnchorA = _measureAnchorAWorldPos.Value,
                AnchorB = endWorldPos,
                DisplayUnit = _measurePayload.displayUnit ?? "mm",
                NearBScreenThreshold = screenThreshold,
                LiveVisualFactory = (a, b) => MeasurementLineVisual.Spawn(a, b, "", liveColor),
                ResultVisualFactory = (a, b) =>
                {
                    float d = Vector3.Distance(a, b);
                    string lbl = MeasurementLineVisual.FormatDistance(d, _measurePayload.displayUnit ?? "mm");
                    return MeasurementLineVisual.Spawn(a, b, lbl, resultColor);
                }
            };

            _anchorInteraction = new AnchorToAnchorInteraction(config);
            _anchorInteraction.NearBChanged += HighlightEndAnchorMarker;
            _anchorInteraction.Completed += OnAnchorInteractionCompleted;
            _anchorInteraction.StartFromAnchor();

            OseLog.Info($"[MeasureInteractionHandler] Phase 2: live line tracking cursor, end marker spawned.");
        }

        /// <summary>
        /// Attempts to complete phase 2 when the user taps the end anchor.
        /// Returns true if this handler consumed the interaction.
        /// </summary>
        /// <summary>
        /// Attempts to complete phase 2 when the user taps the end anchor.
        /// Does NOT clear targets or spawn effects — caller is responsible
        /// so effects fire before the marker GameObjects are destroyed.
        /// </summary>
        public bool TryCompletePhase2(string interactedTargetId, out bool shouldCompleteStep, out Vector3 endWorldPos)
        {
            shouldCompleteStep = false;
            endWorldPos = Vector3.zero;

            if (_anchorInteraction == null || !_anchorInteraction.IsActive || _measurePayload == null)
                return false;

            if (!string.Equals(interactedTargetId, _measurePayload.endAnchorTargetId, StringComparison.OrdinalIgnoreCase))
                return false;

            for (int i = 0; i < _spawner.SpawnedTargets.Count; i++)
            {
                var marker = _spawner.SpawnedTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info != null && string.Equals(info.TargetId, interactedTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    endWorldPos = marker.transform.position;
                    break;
                }
            }

            if (!_anchorInteraction.TryCompleteAtAnchor(endWorldPos, forceComplete: true))
                return false;

            shouldCompleteStep = true;
            return true;
        }

        public bool IsMeasureProfile(StepProfile profile) => profile == StepProfile.Measure;

        // ====================================================================
        //  Private
        // ====================================================================

        private void HighlightEndAnchorMarker(bool ready)
        {
            if (_measurePayload == null)
                return;

            Color color = ready
                ? new Color(0.2f, 1f, 0.4f, 0.9f)
                : ToolTargetAnimator.ToolTargetIdleColor;

            _animator.SetTargetColor(_measurePayload.endAnchorTargetId, color);
        }

        private void OnAnchorInteractionCompleted(AnchorToAnchorInteraction.Result result)
        {
            if (_measurePayload != null && _measurePayload.toleranceMm > 0f)
            {
                float measuredMm = result.DistanceMeters * 1000f;
                float error = Mathf.Abs(measuredMm - _measurePayload.expectedValueMm);
                if (error > _measurePayload.toleranceMm)
                    OseLog.Warn($"[MeasureInteractionHandler] Measure validation: {measuredMm:F1}mm vs expected {_measurePayload.expectedValueMm:F1}mm (error {error:F1}mm > tol {_measurePayload.toleranceMm:F1}mm).");
                else
                    OseLog.Info($"[MeasureInteractionHandler] Measure validation passed: {measuredMm:F1}mm.");
            }

            OseLog.Info($"[MeasureInteractionHandler] Measure complete: {result.FormattedLabel} ({result.DistanceMeters:F4}m).");
        }

        private static MachinePackageDefinition GetPackage()
        {
            if (App.ServiceRegistry.TryGet<OSE.Runtime.IMachineSessionController>(out var session))
                return session.Package;
            return null;
        }
    }
}
