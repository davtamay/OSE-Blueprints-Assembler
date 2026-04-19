using System;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Orchestrates the Tool Action Preview lifecycle:
    /// 1. Approach — tool preview detaches from camera, lerps to working position near target
    /// 2. Action — profile-specific preview plays (observe or guided)
    /// 3. Return — tool preview flies back to cursor position
    /// 4. Complete — preview re-parents to camera, callback fires
    /// </summary>
    public sealed class ToolActionPreviewController
    {
        public enum Phase { Inactive, Approach, Action, Return }

        private Phase _phase;
        private PreviewMode _mode;
        private IToolActionPreview _preview;
        private GameObject _toolPreview;
        private Transform _originalParent;
        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Vector3 _startPos, _workingPos, _surfacePos, _approachDir;
        private Quaternion _startRot, _actionRot;
        private const float BaseApproachDuration = 0.5f;
        private const float BaseReturnDuration = 0.35f;
        private float _speed;
        private float _elapsed;
        private Action<string> _onComplete;
        private Action<string, Vector3, Quaternion> _onActionDone;
        private Action _onCancel;
        private string _targetId;
        private string _profile;
        private ToolPoseConfig _toolPose;
        private Vector3 _weldAxis;
        private float _weldLength;
        private Renderer _targetSphereRenderer;
        private bool _completed;
        private bool _instantPlacement;
        private IPartEffect _partEffect;
        private Vector3 _partEffectOffset;
        private float _startScale, _targetScale;
        private ToolActionVisualGuide _visualGuide;

        // Return animation state
        private Vector3 _returnStartPos;
        private Quaternion _returnStartRot;
        private Vector3 _returnEndPos;
        private Quaternion _returnEndRot;

        public bool IsActive => _phase != Phase.Inactive;
        public Phase CurrentPhase => _phase;

        /// <summary>The tool's orientation during the action phase (before return animation).</summary>
        public Quaternion LastActionRotation => _actionRot;

        public void Enter(
            ToolActionContext ctx,
            GameObject toolPreview,
            string profile,
            PreviewMode mode,
            InteractionMode platform,
            float speed,
            Action<string> onComplete,
            Action onCancel,
            Action<string, Vector3, Quaternion> onActionDone = null)
        {
            if (toolPreview == null)
            {
                OseLog.Warn("[ToolActionPreview] Cannot enter — tool preview is null.");
                onCancel?.Invoke();
                return;
            }

            _targetId = ctx.TargetId;
            _toolPreview = toolPreview;
            _mode = mode;
            _profile = profile;
            _toolPose = ctx.ToolPose;
            _speed = Mathf.Max(speed, 0.1f);
            _weldAxis = ctx.WeldAxis;
            _weldLength = ctx.WeldLength;
            _onComplete = onComplete;
            _onActionDone = onActionDone;
            _onCancel = onCancel;
            _instantPlacement = ctx.InstantPlacement;
            _partEffect = ctx.PartEffect;
            _partEffectOffset = Vector3.zero;

            // Snapshot current preview state and detach from camera
            _originalParent = toolPreview.transform.parent;
            _originalLocalPos = toolPreview.transform.localPosition;
            _originalLocalRot = toolPreview.transform.localRotation;
            _startPos = toolPreview.transform.position;
            _startRot = toolPreview.transform.rotation;
            _surfacePos = ctx.SurfaceWorldPos;

            // Scale lerp: cursor size → assembly-matched size during approach
            _startScale = toolPreview.transform.localScale.x;
            _targetScale = _startScale * Mathf.Max(ctx.AssemblyScale, 0.01f);


            // Compute working position and action orientation.
            // Prefer authored tip distance from toolPose; fall back to AABB estimation.
            // Scale toolReach by assembly ratio so the working position matches the
            // target scale the tool will be at when it arrives.
            float toolReach = EstimateTipDistance(toolPreview, ctx.ToolPose);
            float scaleRatio = _startScale > 0.001f ? _targetScale / _startScale : 1f;
            ComputeWorkingPose(ctx.SurfaceWorldPos, toolReach * scaleRatio, profile, ctx, out _workingPos, out _actionRot);

            toolPreview.transform.SetParent(null, worldPositionStays: true);

            if (ctx.HasToolActionRotation && ctx.ToolPose != null && ctx.ToolPose.HasTipPoint)
            {
                // When the author has locked in an exact rotation, derive the working
                // position analytically: place the tool root so its tip lands exactly
                // at the surface. Use _targetScale (assembly-matched) since that's the
                // scale the tool will be at when it reaches the target.
                _workingPos = ctx.SurfaceWorldPos - _actionRot * (ctx.ToolPose.GetTipPoint() * _targetScale);
            }
            else if (ctx.HasToolActionRotation)
            {
                // Authored rotation but no tipPoint: the tool's origin is the contact
                // point, matching the editor preview which places pivot at target.
                _workingPos = ctx.SurfaceWorldPos;
            }
            else if (ctx.ToolPose != null && ctx.ToolPose.HasTipPoint)
            {
                // No authored rotation: approach from camera side and nudge the working
                // position so the tip lands at the surface using TransformPoint.
                // Temporarily set target scale so TransformPoint computes the correct offset.
                toolPreview.transform.localScale = Vector3.one * _targetScale;
                toolPreview.transform.SetPositionAndRotation(_workingPos, _actionRot);
                Vector3 currentTipWorld = toolPreview.transform.TransformPoint(ctx.ToolPose.GetTipPoint());
                const float tipClearance = 0.002f;
                Vector3 desiredTip = ctx.SurfaceWorldPos + _approachDir * tipClearance;
                _workingPos += desiredTip - currentTipWorld;
                // Restore so the approach animation starts from cursor.
                toolPreview.transform.localScale = Vector3.one * _startScale;
                toolPreview.transform.SetPositionAndRotation(_startPos, _startRot);
            }

            // Show the real tool materials during preview (not the transparent cursor tint)
            MaterialHelper.RestoreOriginals(toolPreview);

            _preview = ToolActionPreviewFactory.Create(profile);
            _phase = Phase.Approach;
            _elapsed = 0f;
            _completed = false;

            // Hide the target sphere renderer so the tool action is fully visible
            _targetSphereRenderer = FindTargetSphereRenderer(ctx.SurfaceWorldPos);
            if (_targetSphereRenderer != null)
                _targetSphereRenderer.enabled = false;

            OseLog.Info($"[ToolActionPreview] Entered — profile='{profile}', mode={mode}, speed={_speed:F2}x, target='{ctx.TargetId}', tipDist={toolReach:F3}");

            // Visual guide (progress ring + arrow for guided)
            _visualGuide = new ToolActionVisualGuide();
        }

        public void Tick(float deltaTime, Vector2 dragDelta = default, Vector2 screenPos = default)
        {
            if (_phase == Phase.Inactive) return;

            // Preview was destroyed externally (step advanced, tool changed, etc.)
            if (_toolPreview == null)
            {
                OseLog.Info("[ToolActionPreview] Preview destroyed externally — cancelling.");
                ExitDestroyed();
                return;
            }

            _elapsed += deltaTime;

            switch (_phase)
            {
                case Phase.Approach:
                    TickApproach();
                    break;
                case Phase.Action:
                    TickAction(deltaTime, dragDelta, screenPos);
                    break;
                case Phase.Return:
                    TickReturn();
                    break;
            }
        }

        public void Cancel()
        {
            if (IsActive)
            {
                _completed = false;
                BeginReturn();
            }
        }

        /// <summary>
        /// Skips the return animation entirely — the preview stays where it is.
        /// Called when the preview has been converted into a persistent tool in-place
        /// and should not fly back to the cursor.
        /// </summary>
        public void SkipReturn()
        {
            if (!IsActive) return;

            if (_preview != null)
            {
                _preview.End(true);
                _preview = null;
            }
            _visualGuide?.Exit();
            _visualGuide = null;
            RestoreTargetSphere();

            // Null the preview reference so CleanupAndNotify won't try to re-parent it
            _toolPreview = null;
            CleanupAndNotify(completed: true);
        }

        // ── Phase ticks ──

        private void TickApproach()
        {
            float t = Mathf.Clamp01(_elapsed / (BaseApproachDuration / _speed));
            float eased = EaseOutQuad(t);

            _toolPreview.transform.position = Vector3.Lerp(_startPos, _workingPos, eased);
            _toolPreview.transform.rotation = Quaternion.Slerp(_startRot, _actionRot, eased);
            float lerpedScale = Mathf.Lerp(_startScale, _targetScale, eased);
            _toolPreview.transform.localScale = Vector3.one * lerpedScale;
            if (t >= 1f)
            {
                _toolPreview.transform.position = _workingPos;
                _toolPreview.transform.rotation = _actionRot;
                _toolPreview.transform.localScale = Vector3.one * _targetScale;

                // Persistent tools (clamps, fixtures): snap into place at end of approach.
                // No action animation plays yet — a screw-tighten animation can be plugged
                // in later by removing this branch. The I Do / We Do / auto mode selection
                // still drives how the approach is presented (Observe first, Guided second,
                // auto after), so the full framework remains intact.
                if (_instantPlacement)
                {
                    _completed = true;
                    // Dispose preview object before SkipReturn() so End() is not called
                    // on a preview that was never Begin()-ed.
                    _preview = null;
                    _visualGuide?.Exit();
                    _visualGuide = null;
                    RestoreTargetSphere();
                    _onActionDone?.Invoke(_targetId, _surfacePos, _toolPreview.transform.rotation);
                    if (_phase == Phase.Inactive) return; // SkipReturn() was called
                    BeginReturn();
                    return;
                }

                _phase = Phase.Action;
                _elapsed = 0f;

                var ctx = new PreviewContext
                {
                    ToolPreview = _toolPreview,
                    TargetWorldPos = _surfacePos,
                    ToolStartWorldPos = _startPos,
                    ToolStartRotation = _startRot,
                    Profile = _profile,
                    Mode = _mode,
                    Platform = InteractionMode.Auto,
                    ToolPose = _toolPose,
                    WeldAxis = _weldAxis,
                    WeldLength = _weldLength
                };
                _preview.Begin(ctx);
                _partEffect?.Begin();

                // Activate visual guide now that the action phase is starting
                Vector2 dragDir = _preview.GetExpectedDragDirection(ctx);
                var previewStyle = ToolProfileRegistry.Get(_profile).PreviewStyle;
                _visualGuide?.Enter(_mode, previewStyle, _surfacePos, dragDir);
            }
        }

        private void TickAction(float deltaTime, Vector2 dragDelta, Vector2 screenPos)
        {
            // Speed multiplier accelerates auto-play; guided mode stays at user pace
            float progress = (_mode == PreviewMode.Observe)
                ? _preview.TickObserve(deltaTime * _speed)
                : _preview.TickGuided(deltaTime, dragDelta, screenPos);

            // Drive part transform and follow its displacement.
            //
            // When a PartEffect is active, the controller OWNS tool.position
            // each frame: position = _workingPos + cumulative-part-follow +
            // preview-overlay. This is the overlay contract defined in
            // IToolActionPreview.ComputeOverlayOffset — previews are expected
            // to NOT write position directly in this regime; anything they
            // write via ApplyEffects is overwritten here.
            //
            // The pre-refactor code used `+= _partEffectOffset` (cumulative
            // add), which only composed correctly if every preview reset
            // position to its own base every frame. That implicit contract
            // broke twice (DefaultPreview, CutPreview) and both times showed
            // up as tool massively overshooting or drifting. The overlay
            // contract removes the hidden state: controller is the only
            // writer when part-follow is active.
            if (_partEffect != null && _toolPreview != null)
            {
                Vector3 delta = _partEffect.Apply(progress);
                _partEffectOffset += delta;
                Vector3 overlay = _preview?.ComputeOverlayOffset(progress) ?? Vector3.zero;
                _toolPreview.transform.position = _workingPos + _partEffectOffset + overlay;
            }

            _visualGuide?.SetProgress(progress);

            if (progress >= 1f)
            {
                _completed = true;
                _partEffect?.End();
                _partEffect = null;
                _preview?.End(true);
                _preview = null;

                // Fire before return animation so callers can convert the preview.
                // Pass _surfacePos (the actual target surface position) rather than the
                // preview's working position, which is offset by the tool's approach distance.
                // This ensures persistent tools (clamps) are placed exactly on the target.
                // Use the preview's current rotation because the animation may have modified it.
                if (_toolPreview != null)
                    _onActionDone?.Invoke(_targetId, _surfacePos, _toolPreview.transform.rotation);

                // onActionDone may have called SkipReturn() — if so, we're already Inactive.
                if (_phase == Phase.Inactive) return;

                BeginReturn();
            }
        }

        private void TickReturn()
        {
            float t = Mathf.Clamp01(_elapsed / (BaseReturnDuration / _speed));
            float eased = EaseInQuad(t);

            _toolPreview.transform.position = Vector3.Lerp(_returnStartPos, _returnEndPos, eased);
            _toolPreview.transform.rotation = Quaternion.Slerp(_returnStartRot, _returnEndRot, eased);
            float returnScale = Mathf.Lerp(_targetScale, _startScale, eased);
            _toolPreview.transform.localScale = Vector3.one * returnScale;

            if (t >= 1f)
                FinishExit();
        }

        // ── Return animation ──

        private void BeginReturn()
        {
            if (_phase == Phase.Action && _preview != null)
            {
                _preview.End(false);
                _preview = null;
            }

            _visualGuide?.Exit();
            _visualGuide = null;

            RestoreTargetSphere();

            if (_toolPreview == null)
            {
                CleanupAndNotify(_completed);
                return;
            }

            Transform parent = _originalParent != null
                ? _originalParent
                : (CameraUtil.GetMain() != null ? CameraUtil.GetMain().transform : null);

            if (parent == null)
            {
                FinishExit();
                return;
            }

            _returnStartPos = _toolPreview.transform.position;
            _returnStartRot = _toolPreview.transform.rotation;
            _returnEndPos = parent.TransformPoint(_originalLocalPos);
            _returnEndRot = parent.rotation * _originalLocalRot;

            _phase = Phase.Return;
            _elapsed = 0f;
        }

        private void FinishExit()
        {
            if (_toolPreview != null)
            {
                // Return to transparent cursor appearance before re-parenting
                MaterialHelper.MakeTransparent(_toolPreview, 0.55f);

                Transform parent = _originalParent != null
                    ? _originalParent
                    : (CameraUtil.GetMain() != null ? CameraUtil.GetMain().transform : null);

                if (parent != null)
                {
                    _toolPreview.transform.SetParent(parent, worldPositionStays: false);
                    _toolPreview.transform.localPosition = _originalLocalPos;
                    _toolPreview.transform.localRotation = _originalLocalRot;
                    _toolPreview.transform.localScale = Vector3.one * _startScale;
                }
            }

            CleanupAndNotify(_completed);
        }

        private void ExitDestroyed()
        {
            _partEffect?.End();
            _partEffect = null;
            if (_preview != null)
            {
                _preview.End(false);
                _preview = null;
            }
            _visualGuide?.Exit();
            _visualGuide = null;
            RestoreTargetSphere();
            CleanupAndNotify(completed: false);
        }

        private void CleanupAndNotify(bool completed)
        {
            _phase = Phase.Inactive;
            _preview = null;
            _partEffect = null;

            string targetId = _targetId;
            var onComplete = _onComplete;
            var onCancel = _onCancel;

            _onComplete = null;
            _onActionDone = null;
            _onCancel = null;
            _targetId = null;
            _toolPreview = null;
            _profile = null;
            _toolPose = null;
            _instantPlacement = false;

            if (completed)
            {
                OseLog.Info($"[ToolActionPreview] Completed for '{targetId}'.");
                onComplete?.Invoke(targetId);
            }
            else
            {
                OseLog.Info($"[ToolActionPreview] Cancelled for '{targetId}'.");
                onCancel?.Invoke();
            }
        }

        // ── Helpers ──

        private void RestoreTargetSphere()
        {
            if (_targetSphereRenderer != null)
            {
                _targetSphereRenderer.enabled = true;
                _targetSphereRenderer = null;
            }
        }

        /// <summary>
        /// Computes where the tool should be positioned and how it should be oriented
        /// during the action phase. If the context carries an authored rotation it is
        /// used directly; otherwise the tool approaches from the camera side so it
        /// doesn't clip through geometry on the far side of the target.
        /// </summary>
        private void ComputeWorkingPose(Vector3 surfacePos, float toolHalfLength, string profile,
            ToolActionContext ctx, out Vector3 workingPos, out Quaternion actionRot)
        {
            Camera cam = CameraUtil.GetMain();
            Vector3 camPos = cam != null ? cam.transform.position : _startPos;
            // Guard: on the first frame after session resume, FrameToolAction runs
            // asynchronously so the camera may still be below the work surface.
            // An inverted viewpoint flips approachDir downward, placing workingPos
            // below the surface. Use a synthetic point above instead.
            if (camPos.y < surfacePos.y - 0.05f)
                camPos = surfacePos + Vector3.up * 1.5f;
            Vector3 camToSurface = (surfacePos - camPos).normalized;
            Vector3 approachDir = -camToSurface; // from surface toward camera

            var profileDesc = ToolProfileRegistry.Get(profile);
            float workingDist = profileDesc.WorkingDistance;
            workingPos = surfacePos + approachDir * (toolHalfLength + workingDist);

            // SquareCheck: prefer authored toolActionRotation when available (same as
            // clamp/torque path), fall back to dynamic camera-based pose otherwise.
            if (profileDesc.PreviewStyle == PreviewStyle.SquareCheck && !ctx.HasToolActionRotation)
            {
                ComputeSquareCheckPose(approachDir, out actionRot);
            }
            // Authored rotation takes priority — content knows best how this tool sits at this target.
            // "mesh" packages: use TargetWorldRotation (live world rotation of the spawned sphere =
            // previewRoot.rotation * placement.rotation), which is exactly what TTAW shows.
            // This makes the TTAW placement.rotation the single source of truth at runtime —
            // no Euler file value, no migration errors.
            // Legacy packages: use the stored ToolActionRotation Euler with grip correction.
            // workingPos here is a placeholder only — Enter() always overrides it with the exact
            // surfacePos - actionRot * tipPoint * scale formula when ToolPose is available.
            else if (ctx.HasToolActionRotation)
            {
                if (ctx.ToolActionRotationIsMesh)
                    actionRot = ctx.TargetWorldRotation;
                else
                {
                    actionRot = ctx.ToolActionRotation;
                    if (ctx.ToolPose != null && ctx.ToolPose.HasGripRotation)
                        actionRot = ctx.ToolActionRotation * Quaternion.Inverse(ctx.ToolPose.GetGripRotation());
                }
                workingPos = surfacePos + approachDir * toolHalfLength;
            }
            // Torque/weld: align the action axis to the fastener shaft direction from the
            // target rotation. Position is derived from TargetWorldRotation alone so it fires
            // even when ToolPose hasn't loaded yet (first tool click after app start).
            // ComputeTorquePose (which needs ToolPose) is only called for rotation when ready.
            else if (profileDesc.PreviewStyle == PreviewStyle.Torque || profileDesc.PreviewStyle == PreviewStyle.Drill)
            {
                Vector3 desiredAxis = ctx.TargetWorldRotation * Vector3.right;
                workingPos = surfacePos - desiredAxis.normalized * toolHalfLength;
                if (ctx.ToolPose != null && ctx.ToolPose.HasActionAxis)
                {
                    actionRot = ComputeTorquePose(desiredAxis, ctx.TargetWorldRotation * Vector3.up, ctx.ToolPose);
                }
                else
                {
                    // ToolPose not loaded yet — orient forward along the weld/shaft axis.
                    Vector3 up = Vector3.ProjectOnPlane(ctx.TargetWorldRotation * Vector3.up, desiredAxis.normalized);
                    if (up.sqrMagnitude < 0.001f)
                        up = Vector3.ProjectOnPlane(Vector3.up, desiredAxis.normalized);
                    actionRot = Quaternion.LookRotation(desiredAxis.normalized,
                        up.sqrMagnitude > 0.001f ? up.normalized : Vector3.up);
                }
            }
            else
            {
                // Default: point the tool tip toward the surface from the camera side.
                // Tip direction derived from tipPoint - gripPoint; fallback is Vector3.down.
                Vector3 localTipDir = (ctx.ToolPose != null && ctx.ToolPose.HasTipPoint)
                    ? ctx.ToolPose.GetTipDirection()
                    : Vector3.down;
                Vector3 currentTip = _startRot * localTipDir;
                Vector3 desiredShaft = -approachDir; // toward surface
                Quaternion shaftCorrection = Quaternion.FromToRotation(currentTip, desiredShaft);
                actionRot = shaftCorrection * _startRot;

                // Apply small profile-specific tilt for realism
                float tiltDegrees = profileDesc.ApproachTiltDegrees;
                if (Mathf.Abs(tiltDegrees) > 0.01f)
                {
                    Vector3 tiltAxis = Vector3.Cross(desiredShaft, Vector3.up).normalized;
                    if (tiltAxis.sqrMagnitude < 0.01f)
                        tiltAxis = Vector3.Cross(desiredShaft, Vector3.right).normalized;
                    actionRot = Quaternion.AngleAxis(tiltDegrees, tiltAxis) * actionRot;
                }
            }

            // Store approach direction so TickApproach can recompute exact tip
            // placement at application time (scale may change after unparenting).
            _approachDir = approachDir;
        }

        /// <summary>
        /// Computes the orientation for a framing square so it lies flat on the surface
        /// with its L-shape visible from the camera. Works for any frame face orientation
        /// (horizontal, vertical, or angled) because it derives everything from the
        /// camera approach direction rather than a hardcoded world rotation.
        ///
        /// The tool mesh has its L in the local XY plane (arms along +X and +Y) based
        /// on the cursor correction Euler(0, 180, 180). LookRotation maps:
        ///   mesh +Z → approachDir (flat face toward camera)
        ///   mesh +Y → surfaceUp (one L-arm along surface vertical)
        ///   mesh +X → surfaceRight (other L-arm along surface horizontal)
        /// </summary>
        private static void ComputeSquareCheckPose(Vector3 approachDir, out Quaternion actionRot)
        {
            // Surface plane axes derived from approach direction
            Vector3 surfaceRight = Vector3.Cross(Vector3.up, approachDir);
            if (surfaceRight.sqrMagnitude < 0.01f)
                surfaceRight = Vector3.Cross(Vector3.forward, approachDir);
            surfaceRight.Normalize();

            Vector3 surfaceUp = Vector3.Cross(approachDir, surfaceRight).normalized;

            // Flat face toward camera, L-arms in surface plane
            actionRot = Quaternion.LookRotation(approachDir, surfaceUp);
        }

        private static Quaternion ComputeTorquePose(Vector3 desiredAxis, Vector3 desiredUp, ToolPoseConfig toolPose)
        {
            Vector3 localAction = toolPose.GetActionAxis();
            Vector3 localTipDir = toolPose.HasTipPoint ? toolPose.GetTipDirection() : Vector3.up;
            Vector3 localUp = Vector3.ProjectOnPlane(localTipDir, localAction).normalized;
            if (localUp.sqrMagnitude < 0.001f)
                localUp = Vector3.up;

            Vector3 worldForward = desiredAxis.normalized;
            Vector3 worldUp = Vector3.ProjectOnPlane(desiredUp, worldForward).normalized;
            if (worldUp.sqrMagnitude < 0.001f)
                worldUp = Vector3.ProjectOnPlane(Vector3.up, worldForward).normalized;
            if (worldUp.sqrMagnitude < 0.001f)
                worldUp = Vector3.ProjectOnPlane(Vector3.forward, worldForward).normalized;

            Quaternion localBasis = Quaternion.LookRotation(localAction, localUp);
            Quaternion worldBasis = Quaternion.LookRotation(worldForward, worldUp);
            return worldBasis * Quaternion.Inverse(localBasis);
        }

        private static Renderer FindTargetSphereRenderer(Vector3 worldPos)
        {
            var colliders = Physics.OverlapSphere(worldPos, 0.5f);
            Renderer best = null;
            float bestDist = float.MaxValue;
            foreach (var col in colliders)
            {
                if (col is not SphereCollider) continue;
                float dist = Vector3.Distance(col.transform.position, worldPos);
                if (dist < bestDist)
                {
                    var r = col.GetComponent<Renderer>();
                    if (r != null)
                    {
                        bestDist = dist;
                        best = r;
                    }
                }
            }
            return best;
        }

        /// <summary>
        /// Estimates the distance from the tool pivot to the tip by projecting
        /// the combined renderer bounds onto the tip direction vector.
        /// This accounts for asymmetric meshes and off-center pivots.
        /// </summary>
        private float EstimateTipDistance(GameObject toolPreview, ToolPoseConfig toolPose)
        {
            // Tier 1: authored tipPoint — distance from mesh origin along tip direction.
            // The tool is positioned at its mesh origin, so we need origin-to-tip,
            // not grip-to-tip, for the working position math.
            if (toolPose != null && toolPose.HasTipPoint)
            {
                Vector3 tipPt = toolPose.GetTipPoint();
                Vector3 authoredTipDir = toolPose.GetTipDirection();
                // Project tipPoint onto tip direction from origin for the reach distance
                float authored = Vector3.Dot(tipPt, authoredTipDir);
                if (authored > 0.001f)
                    return authored * toolPreview.transform.lossyScale.x;
            }

            // Tier 2/3: AABB-based estimation (original logic)
            var renderers = MaterialHelper.GetRenderers(toolPreview);
            if (renderers.Length == 0) return 0.06f;

            Bounds combined = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                combined.Encapsulate(renderers[i].bounds);

            // Tip direction: derived from tipPoint - gripPoint, else legacy Vector3.down
            Vector3 localTipDir = (toolPose != null && toolPose.HasTipPoint)
                ? toolPose.GetTipDirection()
                : Vector3.down;
            Vector3 tipDir = _startRot * localTipDir;
            Vector3 pivot = toolPreview.transform.position;

            float centerDot = Vector3.Dot(combined.center - pivot, tipDir);

            Vector3 ext = combined.extents;
            float extentProjection = Mathf.Abs(ext.x * tipDir.x)
                                   + Mathf.Abs(ext.y * tipDir.y)
                                   + Mathf.Abs(ext.z * tipDir.z);

            float tipDist = centerDot + extentProjection;
            return Mathf.Max(tipDist, 0.01f);
        }

        private static float EaseOutQuad(float t) => 1f - (1f - t) * (1f - t);
        private static float EaseInQuad(float t) => t * t;
    }
}
