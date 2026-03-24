using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;
using ToolActionTargetInfo = OSE.UI.Root.PartInteractionBridge.ToolActionTargetInfo;

namespace OSE.UI.Root
{
    /// <summary>
    /// Handles <see cref="StepFamily.Use"/> (tool_action) steps.
    /// Spawns pulsating tool-action target spheres, detects pointer proximity,
    /// executes tool actions via <see cref="ToolRuntimeController"/>, and
    /// manages visual feedback (pulse, hover, fail-flash, fade).
    /// </summary>
    internal sealed class UseStepHandler : IStepFamilyHandler
    {
        // ── Colours ──
        private static readonly Color ToolTargetIdleColor        = new Color(0.25f, 0.9f, 1.0f, 0.62f);

        private static readonly Color ToolTargetHoverColor       = new Color(0.55f, 1.0f, 1.0f, 0.9f);
        private static readonly Color ToolTargetFailColor        = new Color(1.0f, 0.35f, 0.25f, 0.9f);

        // ── Tuning ──
        private const float ToolTargetPulseSpeed     = 3.6f;
        private const float ToolTargetScalePulse     = 0.12f;
        private const float ToolTargetHeightPulse    = 0.05f;
        private const float ToolTargetColliderRadius  = 1.5f;
        private const float ToolBoundsReadyPaddingPx  = 18f;
        private const float ScreenProximityDesktop    = 120f;
        private const float ScreenProximityMobile     = 180f;
        private const float ToolTargetFadeStartDistance = 3.0f;
        private const float ToolTargetFadeEndDistance   = 0.8f;

        // ── Dependencies ──
        private readonly PackagePartSpawner _spawner;
        private readonly Func<PreviewSceneSetup> _getSetup;
        private readonly Func<ToolCursorManager> _getCursorManager;
        private readonly List<GameObject> _spawnedGhosts;
        private readonly Func<string> _getSequentialTargetId;
        private readonly Func<bool> _advanceSequentialTarget;

        // Profile constants from ToolActionProfiles

        // ── Feedback defaults ──
        private static readonly Color DefaultCompletionColor = new Color(0.2f, 1.0f, 0.4f, 0.9f);
        private const float DefaultPulseScale = 1.8f;

        // ── State ──
        private readonly List<GameObject> _spawnedToolActionTargets = new();
        private GameObject _hoveredToolActionTarget;
        private ToolActionTargetInfo _readyToolActionTarget;
        private bool _retryPending;
        private string _activeProfile;
        private Color _completionEffectColor = DefaultCompletionColor;
        private string _completionParticleId;
        private float _completionPulseScale = DefaultPulseScale;

        // ── Preview tracking ("I Do / We Do / You Do") ──
        private int _completedTargetCountForStep;

        // ── Measure state ──
        private StepMeasurementPayload _measurePayload;
        private Vector3? _measureAnchorAWorldPos;
        private AnchorToAnchorInteraction _anchorInteraction;

        public UseStepHandler(
            PackagePartSpawner spawner,
            Func<PreviewSceneSetup> getSetup,
            Func<ToolCursorManager> getCursorManager,
            List<GameObject> spawnedGhosts,
            Func<string> getSequentialTargetId,
            Func<bool> advanceSequentialTarget)
        {
            _spawner              = spawner;
            _getSetup             = getSetup;
            _getCursorManager     = getCursorManager;
            _spawnedGhosts        = spawnedGhosts;
            _getSequentialTargetId = getSequentialTargetId;
            _advanceSequentialTarget = advanceSequentialTarget;
        }

        // ── Public accessors for bridge delegation ──

        /// <summary>Number of currently-spawned tool-action target markers.</summary>
        public int SpawnedTargetCount => _spawnedToolActionTargets.Count;

        /// <summary>
        /// Number of tool targets completed so far in this step.
        /// Used by the preview system to resolve the learning phase:
        /// 0 → Observe ("I Do"), 1 → Guided ("We Do"), 2+ → Solo ("You Do").
        /// </summary>
        public int CompletedTargetCountForStep => _completedTargetCountForStep;

        /// <summary>Increments the completed target count. Called by the orchestrator after a preview completes.</summary>
        public void IncrementCompletedTargetCount() => _completedTargetCountForStep++;

        /// <summary>
        /// Returns combined world bounds for the currently spawned tool-action markers.
        /// Used by camera framing so Use steps include the live target markers in view.
        /// </summary>
        public bool TryGetSpawnedTargetBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker == null || !marker.activeInHierarchy)
                    continue;

                if (!TryGetWorldBounds(marker, out Bounds markerBounds))
                    continue;

                if (!hasBounds)
                {
                    bounds = markerBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(markerBounds);
                }
            }

            return hasBounds;
        }

        /// <summary>
        /// Ticks tool-target visuals and retry logic while Use is active.
        /// </summary>
        public void Tick()
        {
            UpdateToolActionTargetVisuals();
            UpdateToolCursorProximity();
            _anchorInteraction?.Tick();

            if (_retryPending)
                RefreshToolActionTargets();
        }

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        bounds.Encapsulate(renderers[i].bounds);
                }

                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                        bounds.Encapsulate(colliders[i].bounds);
                }

                return true;
            }

            return false;
        }

        // ====================================================================
        //  IStepFamilyHandler
        // ====================================================================

        public void OnStepActivated(in StepHandlerContext context)
        {
            _completedTargetCountForStep = 0;
            _activeProfile = context.Step.profile;

            var fb = context.Step.feedback;
            _completionEffectColor = TryParseHexColor(fb?.completionEffectColor, DefaultCompletionColor);
            _completionPulseScale = fb != null && fb.completionPulseScale > 0f
                ? fb.completionPulseScale
                : DefaultPulseScale;
            _completionParticleId = fb?.completionParticleId;

            // Measure profile state — JsonUtility creates default instances for
            // [Serializable] class fields even when absent from JSON, so check IsConfigured.
            var rawMeasurement = context.Step.measurement;
            _measurePayload = (rawMeasurement != null && rawMeasurement.IsConfigured) ? rawMeasurement : null;
            _measureAnchorAWorldPos = null;
            CleanupAnchorInteraction();

            RefreshToolActionTargets();
        }

        public bool TryHandlePointerAction(in StepHandlerContext context)
        {
            return false;
        }

        public bool TryHandlePointerDown(in StepHandlerContext context, Vector2 screenPos)
        {
            // Use steps don't consume raw pointer-down. The bridge's
            // TryHandleToolActionPointerDown guards against part selection
            // when tool mode is locked; actual execution goes through the
            // canonical action path.
            return false;
        }

        public void Update(in StepHandlerContext context, float deltaTime)
        {
            Tick();
        }

        public void OnStepCompleted(in StepHandlerContext context)
        {
            ClearToolActionTargets();
            CleanupAnchorInteraction();
        }

        public void Cleanup()
        {
            ClearToolActionTargets();
            CleanupAnchorInteraction();
            _activeProfile = null;
            _measurePayload = null;
            _measureAnchorAWorldPos = null;
            _completedTargetCountForStep = 0;
        }

        // ====================================================================
        //  Public methods (called by bridge)
        // ====================================================================

        /// <summary>
        /// Re-spawns tool action target markers for the current step.
        /// Called by bridge on step activation, tool change, and retry.
        /// </summary>
        public void RefreshToolActionTargets()
        {
            ClearToolActionTargets();
            _retryPending = false;

            PreviewSceneSetup setup = _getSetup();
            if (_spawner == null || setup == null)
                return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var earlySession))
                return;

            StepController earlyStepCtrl = earlySession?.AssemblyController?.StepController;
            if (earlyStepCtrl == null || !earlyStepCtrl.HasActiveStep)
                return;

            // Sync profile from current step so color/behavior is correct
            // regardless of whether OnStepActivated was called.
            StepDefinition currentStep = earlyStepCtrl.CurrentStepDefinition;
            _activeProfile = currentStep?.profile;

            if (currentStep?.requiredToolActions == null || currentStep.requiredToolActions.Length == 0)
                return;

            MachineSessionController session = earlySession;
            bool spawnedAny = false;

            bool runtimeMatchesStep =
                session?.ToolController != null &&
                string.Equals(session.ToolController.ActiveStepId, currentStep.id, StringComparison.OrdinalIgnoreCase);

            if (runtimeMatchesStep &&
                TryGetActionSnapshots(out ToolRuntimeController.ToolActionSnapshot[] actionSnapshots, out session))
            {
                string sequentialTargetId = ResolveSequentialTargetId(currentStep, actionSnapshots);

                for (int i = 0; i < actionSnapshots.Length; i++)
                {
                    ToolRuntimeController.ToolActionSnapshot action = actionSnapshots[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    if (!StepDefinesToolAction(currentStep, action.ToolId, action.TargetId))
                        continue;

                    if (!string.IsNullOrWhiteSpace(sequentialTargetId) &&
                        !string.Equals(action.TargetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (TrySpawnToolActionTargetMarker(session.Package, setup, action.ToolId, action.TargetId))
                        spawnedAny = true;
                }
            }
            else
            {
                if (!TrySpawnFallbackStepToolActionTargets(currentStep, session?.Package, setup))
                {
                    if (ActiveStepHasRequiredToolActions())
                        _retryPending = true;
                    else
                        TryWarnMissingPrimaryToolActionSnapshot();
                    return;
                }

                spawnedAny = _spawnedToolActionTargets.Count > 0;
                if (spawnedAny)
                    OseLog.Warn($"[UseStepHandler] Falling back to step-defined tool targets for step '{currentStep?.id}'.");
            }

            if (!spawnedAny && ActiveStepHasRequiredToolActions())
                _retryPending = true;
        }

        /// <summary>Destroys all spawned tool action target markers.</summary>
        public void ClearToolActionTargets()
        {
            _hoveredToolActionTarget = null;
            _readyToolActionTarget = null;

            for (int i = _spawnedToolActionTargets.Count - 1; i >= 0; i--)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker != null)
                    UnityEngine.Object.Destroy(marker);
            }

            _spawnedToolActionTargets.Clear();
        }

        private bool TrySpawnFallbackStepToolActionTargets(
            StepDefinition step,
            MachinePackageDefinition package,
            PreviewSceneSetup setup)
        {
            if (step?.requiredToolActions == null || package == null || setup == null)
                return false;

            bool spawnedAny = false;
            string sequentialTargetId = ResolveSequentialTargetId(step);

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.toolId) || string.IsNullOrWhiteSpace(action.targetId))
                    continue;

                if (!string.IsNullOrWhiteSpace(sequentialTargetId) &&
                    !string.Equals(action.targetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TrySpawnToolActionTargetMarker(package, setup, action.toolId.Trim(), action.targetId.Trim()))
                    spawnedAny = true;
            }

            return spawnedAny;
        }

        private bool TrySpawnToolActionTargetMarker(
            MachinePackageDefinition package,
            PreviewSceneSetup setup,
            string requiredToolId,
            string targetId)
        {
            if (package == null || setup == null || string.IsNullOrWhiteSpace(requiredToolId) || string.IsNullOrWhiteSpace(targetId))
                return false;

            if (!TryResolveToolActionTargetPose(package, targetId, out Vector3 markerPos, out Quaternion markerRot, out Vector3 markerScale))
            {
                OseLog.Warn($"[UseStepHandler] Could not resolve tool target pose for '{targetId}'.");
                return false;
            }

            if (TryGetGhostTargetPose(targetId, out Vector3 ghostPos, out Quaternion ghostRot, out Vector3 ghostScale))
            {
                markerPos = ghostPos;
                markerRot = ghostRot;
                markerScale = ghostScale;
            }

            Transform previewRoot = setup.PreviewRoot;
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (previewRoot != null)
                marker.transform.SetParent(previewRoot, false);

            marker.name = $"ToolTarget_{requiredToolId}_{targetId}";
            marker.transform.SetLocalPositionAndRotation(markerPos, markerRot);
            marker.transform.localScale = ResolveToolTargetMarkerScale(markerScale);

            // Record surface position before lifting the sphere for clickability
            Vector3 surfaceWorldPos = marker.transform.position;

            float markerLift = Mathf.Max(markerScale.y * 0.75f, marker.transform.localScale.y * 0.6f);
            marker.transform.position += Vector3.up * markerLift;

            PackagePartSpawner.EnsureColliders(marker);
            SphereCollider toolCol = marker.GetComponent<SphereCollider>();
            if (toolCol != null)
                toolCol.radius = ToolTargetColliderRadius;
            MaterialHelper.ApplyToolTargetMarker(marker, ToolTargetIdleColor);

            ToolActionTargetInfo info = marker.GetComponent<ToolActionTargetInfo>();
            if (info == null)
                info = marker.AddComponent<ToolActionTargetInfo>();
            info.TargetId = targetId;
            info.RequiredToolId = requiredToolId;
            info.BaseScale = marker.transform.localScale;
            info.BaseLocalPosition = marker.transform.localPosition;
            info.SurfaceWorldPos = surfaceWorldPos;

            // Populate weld line data from TargetDefinition (if present)
            if (package.TryGetTarget(targetId, out TargetDefinition targetDef))
            {
                Vector3 axis = targetDef.GetWeldAxisVector();
                if (axis.sqrMagnitude > 0.001f && previewRoot != null)
                    info.WeldAxis = previewRoot.TransformDirection(axis);
                else
                    info.WeldAxis = axis;
                info.WeldLength = targetDef.weldLength;
            }

            _spawnedToolActionTargets.Add(marker);

            OseLog.VerboseInfo(
                $"[UseStepHandler] Spawned tool target marker '{marker.name}' for target '{info.TargetId}' at local {info.BaseLocalPosition} / world {marker.transform.position}.");
            return true;
        }

        private void SpawnClickEffectForTarget(string targetId)
        {
            if (string.IsNullOrEmpty(_activeProfile))
                return;

            bool isTorque = string.Equals(_activeProfile, ToolActionProfiles.Torque, StringComparison.OrdinalIgnoreCase);
            bool isMeasure = string.Equals(_activeProfile, ToolActionProfiles.Measure, StringComparison.OrdinalIgnoreCase);
            if (!isTorque && !isMeasure)
                return;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject marker = _spawnedToolActionTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Vector3 markerWorldPos = marker.transform.position;

                ToolActionClickEffect.Spawn(markerWorldPos, marker.transform.localScale,
                    _completionEffectColor, _completionPulseScale);
                CompletionParticleEffect.TrySpawn(_completionParticleId,
                    markerWorldPos, marker.transform.localScale);

                // Cache anchor position for measurement line drawing
                if (isMeasure && _measurePayload != null)
                {
                    if (string.Equals(targetId, _measurePayload.startAnchorTargetId, StringComparison.OrdinalIgnoreCase))
                        _measureAnchorAWorldPos = markerWorldPos;
                }

                return;
            }
        }

        private bool IsMeasureProfile()
        {
            return string.Equals(_activeProfile, ToolActionProfiles.Measure, StringComparison.OrdinalIgnoreCase);
        }

        // ====================================================================
        //  Measure — thin adapter over AnchorToAnchorInteraction
        // ====================================================================

        private void BeginMeasurePhase2()
        {
            if (!_measureAnchorAWorldPos.HasValue || _measurePayload == null)
                return;

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session))
                return;

            if (!TryResolveToolActionTargetPose(session.Package, _measurePayload.endAnchorTargetId,
                    out Vector3 pos, out Quaternion rot, out Vector3 scale))
            {
                OseLog.Warn($"[UseStepHandler] Could not resolve end anchor pose for '{_measurePayload.endAnchorTargetId}'.");
                return;
            }

            if (TryGetGhostTargetPose(_measurePayload.endAnchorTargetId, out Vector3 gp, out Quaternion gr, out Vector3 gs))
            {
                pos = gp;
                rot = gr;
                scale = gs;
            }

            // Spawn end anchor marker (pulsing sphere the user must tap)
            SpawnMeasureEndMarker(pos, rot, scale);

            // Compute end anchor world position (same transform as marker spawning)
            Vector3 endWorldPos = ResolveMarkerWorldPos(pos, scale);

            // Create reusable anchor-to-anchor interaction
            float screenThreshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop;
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

            OseLog.Info($"[UseStepHandler] Measure phase 2: live line tracking cursor, end marker spawned.");
        }

        private bool TryCompleteMeasurePhase2(string interactedTargetId, out bool shouldCompleteStep)
        {
            shouldCompleteStep = false;

            if (_anchorInteraction == null || !_anchorInteraction.IsActive || _measurePayload == null)
                return false;

            if (!string.Equals(interactedTargetId, _measurePayload.endAnchorTargetId, StringComparison.OrdinalIgnoreCase))
                return false;

            // Get end marker world position for the completion check
            Vector3 endWorldPos = Vector3.zero;
            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                var marker = _spawnedToolActionTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info != null && string.Equals(info.TargetId, interactedTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    endWorldPos = marker.transform.position;
                    break;
                }
            }

            // Force-complete since the user explicitly tapped the end anchor target
            if (!_anchorInteraction.TryCompleteAtAnchor(endWorldPos, forceComplete: true))
                return false;

            SpawnClickEffectForTarget(interactedTargetId);
            ClearToolActionTargets();
            shouldCompleteStep = true;
            return true;
        }

        private void OnAnchorInteractionCompleted(AnchorToAnchorInteraction.Result result)
        {
            // Soft validation — log only
            if (_measurePayload != null && _measurePayload.toleranceMm > 0f)
            {
                float measuredMm = result.DistanceMeters * 1000f;
                float error = Mathf.Abs(measuredMm - _measurePayload.expectedValueMm);
                if (error > _measurePayload.toleranceMm)
                    OseLog.Warn($"[UseStepHandler] Measure validation: {measuredMm:F1}mm vs expected {_measurePayload.expectedValueMm:F1}mm (error {error:F1}mm > tol {_measurePayload.toleranceMm:F1}mm).");
                else
                    OseLog.Info($"[UseStepHandler] Measure validation passed: {measuredMm:F1}mm.");
            }

            OseLog.Info($"[UseStepHandler] Measure complete: {result.FormattedLabel} ({result.DistanceMeters:F4}m).");
        }

        private void HighlightEndAnchorMarker(bool ready)
        {
            if (_measurePayload == null)
                return;

            Color color = ready
                ? new Color(0.2f, 1f, 0.4f, 0.9f)
                : ToolTargetIdleColor;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                var marker = _spawnedToolActionTargets[i];
                if (marker == null) continue;
                var info = marker.GetComponent<ToolActionTargetInfo>();
                if (info != null && string.Equals(info.TargetId, _measurePayload.endAnchorTargetId, StringComparison.OrdinalIgnoreCase))
                {
                    MaterialHelper.SetMaterialColor(marker, color);
                    return;
                }
            }
        }

        private void SpawnMeasureEndMarker(Vector3 localPos, Quaternion localRot, Vector3 scale)
        {
            PreviewSceneSetup setup = _getSetup();
            Transform previewRoot = setup?.PreviewRoot;

            GameObject guide = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (previewRoot != null)
                guide.transform.SetParent(previewRoot, false);

            guide.name = $"MeasureEndGuide_{_measurePayload.endAnchorTargetId}";
            guide.transform.SetLocalPositionAndRotation(localPos, localRot);
            guide.transform.localScale = ResolveToolTargetMarkerScale(scale);
            float lift = Mathf.Max(scale.y * 0.75f, guide.transform.localScale.y * 0.6f);
            guide.transform.position += Vector3.up * lift;

            PackagePartSpawner.EnsureColliders(guide);
            var col = guide.GetComponent<SphereCollider>();
            if (col != null) col.radius = ToolTargetColliderRadius;
            MaterialHelper.ApplyToolTargetMarker(guide, ToolTargetIdleColor);

            var info = guide.AddComponent<ToolActionTargetInfo>();
            info.TargetId = _measurePayload.endAnchorTargetId;
            info.RequiredToolId = null;
            info.BaseScale = guide.transform.localScale;
            info.BaseLocalPosition = guide.transform.localPosition;

            _spawnedToolActionTargets.Add(guide);
        }

        private Vector3 ResolveMarkerWorldPos(Vector3 localPos, Vector3 scale)
        {
            float lift = Mathf.Max(scale.y * 0.75f, ResolveToolTargetMarkerScale(scale).y * 0.6f);
            PreviewSceneSetup setup = _getSetup();
            if (setup?.PreviewRoot != null)
                return setup.PreviewRoot.TransformPoint(localPos) + Vector3.up * lift;
            return localPos + Vector3.up * lift;
        }

        private void CleanupAnchorInteraction()
        {
            if (_anchorInteraction != null)
            {
                _anchorInteraction.NearBChanged -= HighlightEndAnchorMarker;
                _anchorInteraction.Completed -= OnAnchorInteractionCompleted;
                _anchorInteraction.Cleanup();
                _anchorInteraction = null;
            }
        }

        /// <summary>
        /// Executes the tool primary action via ToolRuntimeController.
        /// For Measure profile, the first anchor click suppresses step completion
        /// and spawns the end anchor guide. The second click (end anchor) completes
        /// the measurement and the step.
        /// </summary>
        public bool TryExecuteToolPrimaryAction(
            string interactedTargetId,
            out bool shouldCompleteStep,
            out bool handled)
        {
            shouldCompleteStep = false;
            handled = false;

            // Phase 2: end anchor tap — bypass ToolRuntimeController (action already completed)
            if (_anchorInteraction != null && _anchorInteraction.IsActive
                && TryCompleteMeasurePhase2(interactedTargetId, out shouldCompleteStep))
            {
                handled = true;
                return true;
            }

            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session.ToolController == null)
            {
                OseLog.VerboseInfo("[UseStepHandler] TryExecuteToolPrimaryAction: no session or tool controller.");
                return false;
            }

            // Normal tool action execution via ToolRuntimeController
            ToolRuntimeController.ToolActionExecutionResult toolResult =
                session.ToolController.TryExecutePrimaryAction(interactedTargetId);

            handled = toolResult.Handled;
            shouldCompleteStep = toolResult.ShouldCompleteStep;

            bool actionRan = handled && toolResult.FailureReason == ToolActionFailureReason.None;
            if (!actionRan)
            {
                if (toolResult.FailureReason != ToolActionFailureReason.None)
                    OseLog.Info($"[UseStepHandler] Tool action rejected ({toolResult.FailureReason}): {toolResult.Message}.");
                return false;
            }

            OseLog.Info($"[UseStepHandler] Tool action succeeded on '{interactedTargetId}'. shouldComplete={shouldCompleteStep}, profile={_activeProfile}, hasMeasurePayload={_measurePayload != null}.");

            SpawnClickEffectForTarget(interactedTargetId);

            // Measure profile: suppress step completion after first anchor, enter drag mode
            if (IsMeasureProfile() && shouldCompleteStep && _measurePayload != null)
            {
                shouldCompleteStep = false;
                ClearToolActionTargets();
                BeginMeasurePhase2();
                OseLog.Info("[UseStepHandler] Measure phase 1 complete — tap end anchor to finish.");
                return true;
            }

            bool actionCompletedButStepContinues =
                !shouldCompleteStep &&
                toolResult.CurrentCount > 0 &&
                toolResult.CurrentCount >= toolResult.RequiredCount;

            if (actionCompletedButStepContinues && _advanceSequentialTarget != null)
                _advanceSequentialTarget();

            RefreshToolActionTargets();
            return true;
        }

        /// <summary>
        /// Completes the step if the tool action result says so.
        /// </summary>
        public static bool HandleToolPrimaryResult(
            MachineSessionController session,
            StepController stepController,
            bool shouldCompleteStep)
        {
            if (!shouldCompleteStep)
            {
                OseLog.Info("[UseStepHandler] HandleToolPrimaryResult: shouldCompleteStep=false — not completing.");
                return true;
            }

            string stepId = stepController?.CurrentStepState.StepId ?? "unknown";
            OseLog.Info($"[UseStepHandler] HandleToolPrimaryResult: COMPLETING step '{stepId}'.");
            stepController.CompleteStep(session.GetElapsedSeconds());
            return true;
        }

        /// <summary>
        /// Executes the tool action using the current pointer position.
        /// Called from bridge's canonical action path and external V2 orchestrator.
        /// </summary>
        public bool TryExecuteToolPrimaryActionFromPointer(
            MachineSessionController session,
            StepController stepController,
            bool allowStepCompletion = true)
        {
            string interactedTargetId = null;
            StepDefinition currentStep = stepController != null && stepController.HasActiveStep
                ? stepController.CurrentStepDefinition
                : null;

            if (TryGetPointerPosition(out Vector2 pointerPos) &&
                TryResolveToolActionTargetForExecution(pointerPos, out ToolActionTargetInfo resolvedTarget))
                interactedTargetId = resolvedTarget.TargetId;

            // Measure phase 2 fallback: the normal resolution requires tool ghost
            // bounds overlap which is too strict for the end anchor. Fall back to
            // raycast + screen proximity so a simple tap/click works.
            if (interactedTargetId == null && _anchorInteraction != null && _anchorInteraction.IsActive)
            {
                if (TryGetToolActionTargetAtScreen(pointerPos, out ToolActionTargetInfo hitTarget))
                    interactedTargetId = hitTarget.TargetId;
            }

            if (interactedTargetId == null)
            {
                OseLog.VerboseInfo($"[UseStepHandler] Tool action: no ready tool target resolved. Spawned targets={_spawnedToolActionTargets.Count}.");
                return false;
            }

            interactedTargetId = NormalizeSequentialExecutionTargetId(currentStep, interactedTargetId);

            if (!TryExecuteToolPrimaryAction(interactedTargetId, out bool shouldCompleteStep, out bool handled))
            {
                OseLog.VerboseInfo($"[UseStepHandler] Tool action failed for target '{interactedTargetId}'. Check ToolRuntimeController logs.");
                return false;
            }

            if (!handled)
                return false;

            if (!allowStepCompletion)
                return true;

            return HandleToolPrimaryResult(session, stepController, shouldCompleteStep);
        }

        /// <summary>
        /// Detects the tool action target at the given screen position using
        /// raycast + screen-space proximity fallback.
        /// </summary>
        public bool TryGetToolActionTargetAtScreen(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            if (TryGetToolActionTargetByRaycast(screenPos, out targetInfo))
                return true;

            return TryGetNearestToolTargetByScreenProximity(screenPos, out targetInfo);
        }

        /// <summary>
        /// Resolves the target that can actually execute a Use step.
        /// Prefers the current ready target driven by tool-ghost bounds.
        /// </summary>
        public bool TryResolveToolActionTargetForExecution(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            if (TryGetToolActionTargetByRaycast(screenPos, out targetInfo))
                return true;

            if (TryGetReadyToolActionTarget(out targetInfo))
                return true;

            ToolCursorManager cursorManager = _getCursorManager();
            if (cursorManager?.ToolGhost == null || !cursorManager.ToolGhost.activeSelf)
                return TryGetNearestToolTargetByScreenProximity(screenPos, out targetInfo);

            // Tool ghost is active but not overlapping a target — fall back to
            // raycast + screen-proximity so clicks near the target still register
            // even when the ghost bounding rect misses due to offset/orientation.
            return TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        /// <summary>
        /// Returns the world position of the nearest tool action target within screen proximity.
        /// </summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (!TryGetToolActionTargetAtScreen(screenPos, out ToolActionTargetInfo info))
                return false;
            worldPos = info.transform.position;
            return true;
        }

        /// <summary>
        /// Focuses camera on a tool target near the pointer. Returns true if a target
        /// was found (camera may still be focused even if the result is consumed).
        /// </summary>
        public bool TryFocusCameraOnToolTarget(Vector2 screenPos)
        {
            if (_spawnedToolActionTargets.Count == 0)
                return false;

            if (!TryGetToolActionTargetAtScreen(screenPos, out ToolActionTargetInfo targetInfo))
                return false;

            if (targetInfo == null)
                return false;

            Camera cam = Camera.main;
            if (cam == null)
                return true;

            cam.SendMessage("FocusOn", targetInfo.transform.position, SendMessageOptions.DontRequireReceiver);
            return true;
        }

        /// <summary>
        /// Applies fail-flash colour to all spawned tool targets (reset next frame).
        /// </summary>
        public void FlashToolTargetOnFailure()
        {
            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                if (_spawnedToolActionTargets[i] == null) continue;
                MaterialHelper.ApplyToolTargetMarker(_spawnedToolActionTargets[i], ToolTargetFailColor);
            }
        }

        // ====================================================================
        //  Private visual update
        // ====================================================================

        private void UpdateToolActionTargetVisuals()
        {
            if (_spawnedToolActionTargets.Count == 0)
            {
                _hoveredToolActionTarget = null;
                _readyToolActionTarget = null;
                return;
            }

            _hoveredToolActionTarget = TryGetHoveredToolActionTarget(out ToolActionTargetInfo hoveredTarget)
                ? hoveredTarget.gameObject
                : null;

            Color idlePulseColor = ToolTargetIdleColor;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * ToolTargetPulseSpeed);
            float intensity = Mathf.Lerp(0.75f, 1.25f, pulse);
            idlePulseColor = new Color(
                Mathf.Clamp01(idlePulseColor.r * intensity),
                Mathf.Clamp01(idlePulseColor.g * intensity),
                Mathf.Clamp01(idlePulseColor.b * intensity),
                Mathf.Clamp01(0.55f + 0.35f * pulse));

            GameObject readyTarget = _readyToolActionTarget != null
                ? _readyToolActionTarget.gameObject
                : null;

            Camera cam = Camera.main;

            for (int i = _spawnedToolActionTargets.Count - 1; i >= 0; i--)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null)
                {
                    _spawnedToolActionTargets.RemoveAt(i);
                    continue;
                }

                Color targetColor = (target == _hoveredToolActionTarget || target == readyTarget)
                    ? ToolTargetHoverColor
                    : idlePulseColor;

                if (cam != null)
                {
                    float dist = Vector3.Distance(cam.transform.position, target.transform.position);
                    if (dist < ToolTargetFadeStartDistance)
                    {
                        float t = Mathf.InverseLerp(ToolTargetFadeEndDistance, ToolTargetFadeStartDistance, dist);
                        targetColor.a *= t;
                    }
                }

                MaterialHelper.SetMaterialColor(target, targetColor);

                ToolActionTargetInfo info = target.GetComponent<ToolActionTargetInfo>();
                Vector3 baseScale = info != null && info.BaseScale.sqrMagnitude > 0f
                    ? info.BaseScale
                    : target.transform.localScale;
                float scaleFactor = 1f + (ToolTargetScalePulse * pulse);
                target.transform.localScale = baseScale * scaleFactor;

                Vector3 baseLocalPosition = info != null
                    ? info.BaseLocalPosition
                    : target.transform.localPosition;
                target.transform.localPosition = baseLocalPosition + (Vector3.up * (ToolTargetHeightPulse * (pulse - 0.5f)));
            }
        }

        private void UpdateToolCursorProximity()
        {
            var cursorManager = _getCursorManager();
            if (cursorManager == null)
                return;

            if (_spawnedToolActionTargets.Count == 0)
            {
                _readyToolActionTarget = null;
                if (cursorManager.CursorInReadyState)
                    cursorManager.RestoreColor();
                return;
            }

            if (!TryGetReadyToolActionTarget(out ToolActionTargetInfo readyTarget))
            {
                _readyToolActionTarget = null;
                if (cursorManager.CursorInReadyState)
                    cursorManager.RestoreColor();
                return;
            }

            _readyToolActionTarget = readyTarget;
            cursorManager.SetReadyState(true);
        }

        // ====================================================================
        //  Private detection helpers
        // ====================================================================

        private bool TryGetHoveredToolActionTarget(out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            if (!TryGetPointerPosition(out Vector2 screenPos))
                return false;
            return TryGetToolActionTargetAtScreen(screenPos, out targetInfo);
        }

        private bool TryGetNearestToolTargetByScreenProximity(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;
            Camera cam = Camera.main;
            if (cam == null) return false;

            float threshold = Application.isMobilePlatform ? ScreenProximityMobile : ScreenProximityDesktop;
            float closestDist = threshold;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null) continue;

                Vector3 sp = cam.WorldToScreenPoint(target.transform.position);
                if (sp.z <= 0f) continue;

                float dist = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    var info = target.GetComponent<ToolActionTargetInfo>();
                    if (info != null)
                        targetInfo = info;
                }
            }
            return targetInfo != null;
        }

        private bool TryGetToolActionTargetByRaycast(Vector2 screenPos, out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0)
                return false;

            Array.Sort(hits, static (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                targetInfo = FindToolActionTargetFromHit(hits[i].transform);
                if (targetInfo != null)
                    return true;
            }

            targetInfo = null;
            return false;
        }

        private bool TryGetReadyToolActionTarget(out ToolActionTargetInfo targetInfo)
        {
            targetInfo = null;

            ToolCursorManager cursorManager = _getCursorManager();
            Camera cam = Camera.main;
            GameObject toolGhost = cursorManager?.ToolGhost;
            if (cam == null || toolGhost == null || !toolGhost.activeSelf)
                return false;

            if (!TryGetToolGhostScreenRect(cam, toolGhost, out Rect toolRect))
                return false;

            Rect paddedRect = ExpandRect(toolRect, ToolBoundsReadyPaddingPx);
            Vector2 rectCenter = paddedRect.center;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _spawnedToolActionTargets.Count; i++)
            {
                GameObject target = _spawnedToolActionTargets[i];
                if (target == null)
                    continue;

                Vector3 targetScreen = cam.WorldToScreenPoint(target.transform.position);
                if (targetScreen.z <= 0f)
                    continue;

                Vector2 targetPoint = new Vector2(targetScreen.x, targetScreen.y);
                if (!paddedRect.Contains(targetPoint))
                    continue;

                ToolActionTargetInfo info = target.GetComponent<ToolActionTargetInfo>();
                if (info == null)
                    continue;

                float score = Vector2.SqrMagnitude(targetPoint - rectCenter);
                if (score < bestScore)
                {
                    bestScore = score;
                    targetInfo = info;
                }
            }

            return targetInfo != null;
        }

        private string ResolveSequentialTargetId(
            StepDefinition currentStep,
            ToolRuntimeController.ToolActionSnapshot[] actionSnapshots = null)
        {
            if (currentStep == null || !currentStep.IsSequential)
                return null;

            string sequentialTargetId = _getSequentialTargetId();
            if (!string.IsNullOrWhiteSpace(sequentialTargetId))
                return sequentialTargetId;

            if (actionSnapshots != null)
            {
                for (int i = 0; i < actionSnapshots.Length; i++)
                {
                    ToolRuntimeController.ToolActionSnapshot action = actionSnapshots[i];
                    if (!action.IsConfigured || action.IsCompleted || string.IsNullOrWhiteSpace(action.TargetId))
                        continue;

                    if (!StepDefinesToolAction(currentStep, action.ToolId, action.TargetId))
                        continue;

                    return action.TargetId.Trim();
                }
            }

            ToolActionDefinition[] requiredActions = currentStep.requiredToolActions;
            if (requiredActions == null)
                return null;

            for (int i = 0; i < requiredActions.Length; i++)
            {
                ToolActionDefinition action = requiredActions[i];
                if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                    continue;

                return action.targetId.Trim();
            }

            return null;
        }

        private static bool StepDefinesToolAction(StepDefinition step, string toolId, string targetId)
        {
            if (step?.requiredToolActions == null ||
                string.IsNullOrWhiteSpace(toolId) ||
                string.IsNullOrWhiteSpace(targetId))
            {
                return false;
            }

            for (int i = 0; i < step.requiredToolActions.Length; i++)
            {
                ToolActionDefinition action = step.requiredToolActions[i];
                if (action == null)
                    continue;

                if (!string.Equals(action.toolId?.Trim(), toolId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.Equals(action.targetId?.Trim(), targetId.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                return true;
            }

            return false;
        }

        private string NormalizeSequentialExecutionTargetId(StepDefinition currentStep, string interactedTargetId)
        {
            if (string.IsNullOrWhiteSpace(interactedTargetId) || currentStep == null || !currentStep.IsSequential)
                return interactedTargetId;

            string sequentialTargetId = null;
            if (TryGetActionSnapshots(out ToolRuntimeController.ToolActionSnapshot[] actionSnapshots, out _))
                sequentialTargetId = ResolveSequentialTargetId(currentStep, actionSnapshots);

            if (string.IsNullOrWhiteSpace(sequentialTargetId))
                sequentialTargetId = ResolveSequentialTargetId(currentStep);

            if (string.IsNullOrWhiteSpace(sequentialTargetId) ||
                string.Equals(interactedTargetId, sequentialTargetId, StringComparison.OrdinalIgnoreCase))
            {
                return interactedTargetId;
            }

            return sequentialTargetId;
        }

        private static bool TryGetToolGhostScreenRect(Camera cam, GameObject toolGhost, out Rect screenRect)
        {
            screenRect = default;

            Renderer[] renderers = toolGhost.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            bool hasPoint = false;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                Bounds bounds = renderer.bounds;
                Vector3 extents = bounds.extents;

                for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
                {
                    Vector3 corner = new Vector3(
                        bounds.center.x + ((cornerIndex & 1) == 0 ? -extents.x : extents.x),
                        bounds.center.y + ((cornerIndex & 2) == 0 ? -extents.y : extents.y),
                        bounds.center.z + ((cornerIndex & 4) == 0 ? -extents.z : extents.z));

                    Vector3 screenPoint = cam.WorldToScreenPoint(corner);
                    if (screenPoint.z <= 0f)
                        continue;

                    hasPoint = true;
                    minX = Mathf.Min(minX, screenPoint.x);
                    minY = Mathf.Min(minY, screenPoint.y);
                    maxX = Mathf.Max(maxX, screenPoint.x);
                    maxY = Mathf.Max(maxY, screenPoint.y);
                }
            }

            if (!hasPoint)
                return false;

            screenRect = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return screenRect.width > 0f && screenRect.height > 0f;
        }

        private static Rect ExpandRect(Rect rect, float padding)
        {
            return Rect.MinMaxRect(
                rect.xMin - padding,
                rect.yMin - padding,
                rect.xMax + padding,
                rect.yMax + padding);
        }

        private static ToolActionTargetInfo FindToolActionTargetFromHit(Transform hitTransform)
        {
            while (hitTransform != null)
            {
                ToolActionTargetInfo info = hitTransform.GetComponent<ToolActionTargetInfo>();
                if (info != null)
                    return info;
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        // ====================================================================
        //  Private tool action snapshot resolution
        // ====================================================================

        private static bool TryGetActionSnapshots(
            out ToolRuntimeController.ToolActionSnapshot[] snapshots,
            out MachineSessionController session)
        {
            snapshots = Array.Empty<ToolRuntimeController.ToolActionSnapshot>();
            session = null;

            if (!ServiceRegistry.TryGet(out session) ||
                session == null ||
                session.Package == null ||
                session.ToolController == null)
            {
                return false;
            }

            return session.ToolController.TryGetActionSnapshots(out snapshots);
        }

        private static void TryWarnMissingPrimaryToolActionSnapshot()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session == null)
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step?.requiredToolActions == null || step.requiredToolActions.Length == 0)
                return;

            OseLog.Warn($"[UseStepHandler] Active step '{step.id}' has required tool actions, but no tool action snapshots were available.");
        }

        private static bool ActiveStepHasRequiredToolActions()
        {
            if (!ServiceRegistry.TryGet<MachineSessionController>(out var session) || session == null)
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return false;

            StepDefinition step = stepController.CurrentStepDefinition;
            return step?.requiredToolActions != null && step.requiredToolActions.Length > 0;
        }

        // ====================================================================
        //  Private pose resolution
        // ====================================================================

        private bool TryResolveToolActionTargetPose(
            MachinePackageDefinition package,
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one * 0.25f;

            TargetPreviewPlacement targetPlacement = _spawner.FindTargetPlacement(targetId);
            if (targetPlacement != null)
            {
                position = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
                rotation = !targetPlacement.rotation.IsIdentity
                    ? new Quaternion(targetPlacement.rotation.x, targetPlacement.rotation.y, targetPlacement.rotation.z, targetPlacement.rotation.w)
                    : Quaternion.identity;
                scale = new Vector3(targetPlacement.scale.x, targetPlacement.scale.y, targetPlacement.scale.z);
                return true;
            }

            if (package != null &&
                package.TryGetTarget(targetId, out TargetDefinition targetDef) &&
                !string.IsNullOrWhiteSpace(targetDef.associatedPartId))
            {
                PartPreviewPlacement partPlacement = _spawner.FindPartPlacement(targetDef.associatedPartId);
                if (partPlacement != null)
                {
                    position = new Vector3(partPlacement.playPosition.x, partPlacement.playPosition.y, partPlacement.playPosition.z);
                    rotation = !partPlacement.playRotation.IsIdentity
                        ? new Quaternion(partPlacement.playRotation.x, partPlacement.playRotation.y, partPlacement.playRotation.z, partPlacement.playRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(partPlacement.playScale.x, partPlacement.playScale.y, partPlacement.playScale.z);
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetGhostTargetPose(
            string targetId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId) || _spawnedGhosts.Count == 0)
                return false;

            PreviewSceneSetup setup = _getSetup();
            Transform previewRoot = setup != null ? setup.PreviewRoot : null;

            for (int i = _spawnedGhosts.Count - 1; i >= 0; i--)
            {
                GameObject ghost = _spawnedGhosts[i];
                if (ghost == null) continue;

                var info = ghost.GetComponent<PartInteractionBridge.GhostPlacementInfo>();
                if (info == null || !string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform tx = ghost.transform;
                if (previewRoot != null)
                {
                    position = previewRoot.InverseTransformPoint(tx.position);
                    rotation = Quaternion.Inverse(previewRoot.rotation) * tx.rotation;
                }
                else
                {
                    position = tx.position;
                    rotation = tx.rotation;
                }

                scale = tx.localScale;
                return true;
            }

            return false;
        }

        private static Vector3 ResolveToolTargetMarkerScale(Vector3 sourceScale)
        {
            float dominant = Mathf.Max(sourceScale.x, Mathf.Max(sourceScale.y, sourceScale.z));
            float uniform = Mathf.Clamp(dominant * 0.55f, 0.15f, 0.40f);
            return Vector3.one * uniform;
        }

        // ====================================================================
        //  Static pointer helper (mirrors PartInteractionBridge.TryGetPointerPosition)
        // ====================================================================

        private static bool TryGetPointerPosition(out Vector2 screenPos)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = Vector2.zero;
            return false;
        }

        private static Color TryParseHexColor(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
                return fallback;
            return ColorUtility.TryParseHtmlString(hex, out var parsed) ? parsed : fallback;
        }
    }
}
