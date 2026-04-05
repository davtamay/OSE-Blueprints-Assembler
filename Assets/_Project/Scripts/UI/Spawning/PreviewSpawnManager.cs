using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages preview spawning for placement steps.
    /// Owns the <c>SpawnedPreviews</c> list, sequential-target state,
    /// and all Preview* static math helpers.
    /// Extracted from PartInteractionBridge (Phase 3).
    /// </summary>
    internal sealed class PreviewSpawnManager
    {
        private readonly IBridgeContext _ctx;

        private bool _isSequentialStep;
        private int _sequentialTargetIndex;

        /// <summary>The shared preview list. Passed by reference to PlaceStepHandler / UseStepHandler.</summary>
        public List<GameObject> SpawnedPreviews => _ctx.SpawnedPreviews;

        public bool IsSequentialStep => _isSequentialStep;

        public PreviewSpawnManager(IBridgeContext context)
        {
            _ctx = context;
        }

        // ── Public API ──

        public void SpawnPreviewsForStep(string stepId)
        {
            ClearPreviews();
            var package = _ctx.Spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Pipe connection steps are handled entirely by ConnectStepHandler via the router.
            if (step.IsPipeConnection)
                return;

            string[] targetIds = step.targetIds;
            if (targetIds == null || targetIds.Length == 0)
                return;

            _isSequentialStep = step.IsSequential;
            _sequentialTargetIndex = 0;

            if (_isSequentialStep)
            {
                // Sequential: spawn only the first target's preview.
                SpawnPreviewForTarget(package, targetIds[0]);
            }
            else
            {
                // Parallel (default): spawn all previews at once.
                foreach (string targetId in targetIds)
                    SpawnPreviewForTarget(package, targetId);
            }
        }

        /// <summary>
        /// Called after a part is placed on a preview in sequential mode.
        /// Advances to the next target and spawns its preview/tool-target,
        /// or returns true if all targets are done.
        /// </summary>
        public bool AdvanceSequentialTarget()
        {
            if (!_isSequentialStep) return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return true;

            var package = session.Package;
            StepController stepController = session.AssemblyController?.StepController;
            if (package == null || stepController == null || !stepController.HasActiveStep)
                return true;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step?.targetIds == null) return true;

            _sequentialTargetIndex++;
            if (_sequentialTargetIndex >= step.targetIds.Length)
                return true; // all targets done

            SpawnPreviewForTarget(package, step.targetIds[_sequentialTargetIndex]);
            _ctx.RefreshToolActionTargets();
            return false;
        }

        /// <summary>
        /// Returns the target ID that is currently active in sequential mode,
        /// or null if not in sequential mode or index is out of range.
        /// </summary>
        public string GetCurrentSequentialTargetId()
        {
            if (!_isSequentialStep) return null;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return null;

            StepDefinition step = session.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step?.targetIds == null || _sequentialTargetIndex >= step.targetIds.Length)
                return null;

            return step.targetIds[_sequentialTargetIndex];
        }

        public GameObject FindPreviewForTarget(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return null;

            for (int i = 0; i < _ctx.SpawnedPreviews.Count; i++)
            {
                GameObject preview = _ctx.SpawnedPreviews[i];
                if (preview == null)
                    continue;

                PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                    return preview;
            }

            return null;
        }

        public void ClearPreviews()
        {
            foreach (var preview in _ctx.SpawnedPreviews)
            {
                if (preview == null) continue;
                _ctx.DestroyObject(preview);
            }
            _ctx.SpawnedPreviews.Clear();
        }

        public void ResetSequentialState()
        {
            _isSequentialStep = false;
            _sequentialTargetIndex = 0;
        }

        // ── Preview spawning ──

        private void SpawnPreviewForTarget(MachinePackageDefinition package, string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) { OseLog.Warn("[PartInteraction] SpawnPreviewForTarget: targetId is null/empty."); return; }
            if (!package.TryGetTarget(targetId, out var target)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: target '{targetId}' not found in package."); return; }

            if (!string.IsNullOrWhiteSpace(target.associatedSubassemblyId))
            {
                SpawnPreviewForSubassemblyTarget(package, targetId, target);
                return;
            }

            string associatedPartId = target.associatedPartId;
            if (string.IsNullOrEmpty(associatedPartId)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: target '{targetId}' has no associatedPartId."); return; }
            if (!package.TryGetPart(associatedPartId, out var part)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' not found in package."); return; }

            // Skip preview for parts already placed or completed from a prior step.
            if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
            {
                var partState = partController.GetPartState(associatedPartId);
                if (partState == PartPlacementState.Completed ||
                    partState == PartPlacementState.PlacedVirtually)
                {
                    OseLog.VerboseInfo($"[PartInteraction] SpawnPreviewForTarget: skipping preview for '{associatedPartId}' — already {partState}.");
                    return;
                }
                OseLog.VerboseInfo($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' state={partState}, proceeding with preview.");
            }

            Transform previewRoot = GetPreviewRoot();

            TargetPreviewPlacement tp = _ctx.Spawner.FindTargetPlacement(targetId);
            PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(associatedPartId);

            // --- Spline parts: create a procedural preview tube instead of loading a GLB ---
            if (SplinePartFactory.HasSplineData(pp))
            {
                GameObject splinePreview = SplinePartFactory.CreatePreview(associatedPartId, pp.splinePath, previewRoot);
                if (splinePreview == null)
                {
                    OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: failed to create spline preview for '{associatedPartId}'.");
                    return;
                }

                PlacementPreviewInfo splineInfo = splinePreview.AddComponent<PlacementPreviewInfo>();
                splineInfo.TargetId = targetId;
                splineInfo.PartId = associatedPartId;

                // Spline preview sits at play position (0,0,0) / scale (1,1,1) — knots define the routing
                splinePreview.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                splinePreview.transform.localScale = Vector3.one;

                // Convert existing colliders to triggers for click-to-place
                foreach (var col in splinePreview.GetComponentsInChildren<Collider>(true))
                    col.isTrigger = true;

                MaterialHelper.ApplyPreviewMaterial(splinePreview);
                _ctx.SpawnedPreviews.Add(splinePreview);
                OseLog.Info($"[PartInteraction] Spline preview spawned for '{associatedPartId}' at target '{targetId}'. Total previews: {_ctx.SpawnedPreviews.Count}");
                return;
            }

            // --- Standard GLB-based preview ---
            string previewRef = part.assetRef;
            if (string.IsNullOrEmpty(previewRef)) { OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: part '{associatedPartId}' has no assetRef."); return; }

            Vector3 previewPos;
            Quaternion previewRot;
            Vector3 previewScale;

            // playPosition is the single source of truth for where a part ends up
            // when placed. Preview must appear at the same location so there is no
            // discrepancy between the preview and the actual placement.
            // TargetPreviewPlacement is only used as fallback for targets without
            // an associated part placement (tool-action targets, checkpoints, etc.).
            if (pp != null)
            {
                previewPos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                previewRot = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;
            }
            else if (tp != null)
            {
                previewPos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                previewRot = !tp.rotation.IsIdentity
                    ? new Quaternion(tp.rotation.x, tp.rotation.y, tp.rotation.z, tp.rotation.w)
                    : Quaternion.identity;
            }
            else
            {
                previewPos = Vector3.zero;
                previewRot = Quaternion.identity;
            }

            // Preview should mirror the live source part dimensions exactly.
            GameObject sourcePart = _ctx.FindSpawnedPart(associatedPartId);
            if (sourcePart != null)
            {
                previewScale = sourcePart.transform.localScale;
            }
            else if (pp != null)
            {
                Vector3 authoredStartScale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                Vector3 authoredPlayScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                previewScale = authoredStartScale.sqrMagnitude > 0f
                    ? authoredStartScale
                    : (authoredPlayScale.sqrMagnitude > 0f ? authoredPlayScale : Vector3.one);
            }
            else if (tp != null)
            {
                previewScale = new Vector3(tp.scale.x, tp.scale.y, tp.scale.z);
            }
            else
            {
                previewScale = Vector3.one * 0.5f;
            }

            GameObject preview = _ctx.Spawner.TryLoadPackageAsset(previewRef);
            if (preview == null)
            {
                preview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                if (previewRoot != null)
                    preview.transform.SetParent(previewRoot, false);
            }

            preview.name = $"Preview_{associatedPartId}";
            PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
            if (info == null)
                info = preview.AddComponent<PlacementPreviewInfo>();
            info.TargetId = targetId;
            info.PartId = associatedPartId;
            preview.transform.SetLocalPositionAndRotation(previewPos, previewRot);
            preview.transform.localScale = previewScale;

            foreach (var col in preview.GetComponentsInChildren<Collider>(true))
                _ctx.DestroyObject(col);

            // Fit a BoxCollider to the combined renderer bounds so the trigger
            // is accurate to the mesh shape rather than an oversized sphere.
            var previewRenderers = MaterialHelper.GetRenderers(preview);
            var clickCollider = preview.AddComponent<BoxCollider>();
            clickCollider.isTrigger = true;
            if (previewRenderers.Length > 0)
            {
                Bounds combined = previewRenderers[0].bounds;
                for (int ri = 1; ri < previewRenderers.Length; ri++)
                    combined.Encapsulate(previewRenderers[ri].bounds);
                Vector3 lossyScale = preview.transform.lossyScale;
                clickCollider.center = preview.transform.InverseTransformPoint(combined.center);
                clickCollider.size = new Vector3(
                    lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                    lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                    lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            }

            MaterialHelper.ApplyPreviewMaterial(preview);
            _ctx.SpawnedPreviews.Add(preview);
            OseLog.Info($"[PartInteraction] Preview spawned for '{associatedPartId}' at target '{targetId}' pos={previewPos} scale={previewScale}. Total previews: {_ctx.SpawnedPreviews.Count}");
        }

        private void SpawnPreviewForSubassemblyTarget(MachinePackageDefinition package, string targetId, TargetDefinition target)
        {
            string subassemblyId = target.associatedSubassemblyId;
            if (string.IsNullOrWhiteSpace(subassemblyId))
                return;

            var subCtrl = _ctx.SubassemblyController;
            if (subCtrl == null || !subCtrl.IsSubassemblyReady(subassemblyId))
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: subassembly '{subassemblyId}' is not ready for placement.");
                return;
            }

            if (!package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) || subassembly == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: subassembly '{subassemblyId}' not found in package.");
                return;
            }

            SubassemblyPreviewPlacement frame = _ctx.Spawner.FindSubassemblyPlacement(subassemblyId);
            if (frame == null)
            {
                OseLog.Warn($"[PartInteraction] SpawnPreviewForTarget: subassembly '{subassemblyId}' has no authored preview frame.");
                return;
            }

            Transform previewRoot = GetPreviewRoot();
            GameObject subPreviewRoot = new GameObject($"Preview_{subassemblyId}");
            if (previewRoot != null)
                subPreviewRoot.transform.SetParent(previewRoot, false);

            if (subCtrl.TryResolveTargetPose(targetId, out Vector3 previewPos, out Quaternion previewRot, out Vector3 previewScale))
            {
                subPreviewRoot.transform.SetLocalPositionAndRotation(previewPos, previewRot);
                subPreviewRoot.transform.localScale = previewScale;
            }
            else
            {
                subPreviewRoot.transform.SetLocalPositionAndRotation(
                    ToVector3(frame.position),
                    ToQuaternion(frame.rotation));
                subPreviewRoot.transform.localScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);
            }

            Vector3 framePos = ToVector3(frame.position);
            Quaternion frameRot = ToQuaternion(frame.rotation);
            Vector3 frameScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);
            IntegratedSubassemblyPreviewPlacement integratedPlacement = _ctx.Spawner.FindIntegratedSubassemblyPlacement(subassemblyId, targetId);
            ConstrainedSubassemblyFitPreviewPlacement fitPlacement = _ctx.Spawner.FindConstrainedSubassemblyFitPlacement(subassemblyId, targetId);
            Vector3 fitAxisLocal = fitPlacement != null ? ToVector3(fitPlacement.fitAxisLocal) : Vector3.zero;
            if (fitAxisLocal.sqrMagnitude > 0.000001f)
                fitAxisLocal.Normalize();
            float fitTravel = fitPlacement != null
                ? Mathf.Clamp(
                    fitPlacement.completionTravel,
                    Mathf.Min(fitPlacement.minTravel, fitPlacement.maxTravel),
                    Mathf.Max(fitPlacement.minTravel, fitPlacement.maxTravel))
                : 0f;
            bool isAxisFitPreview = fitPlacement?.drivenPartIds != null && fitPlacement.drivenPartIds.Length > 0;
            HashSet<string> fitDrivenPartIds = isAxisFitPreview
                ? new HashSet<string>(fitPlacement.drivenPartIds, StringComparer.OrdinalIgnoreCase)
                : null;
            var fitPreviewChildren = isAxisFitPreview ? new List<Transform>() : null;

            string[] memberIds = subassembly.partIds ?? Array.Empty<string>();
            for (int i = 0; i < memberIds.Length; i++)
            {
                string memberId = memberIds[i];
                if (string.IsNullOrWhiteSpace(memberId) || !package.TryGetPart(memberId, out PartDefinition part))
                    continue;
                if (isAxisFitPreview && !fitDrivenPartIds.Contains(memberId))
                    continue;

                PartPreviewPlacement placement = _ctx.Spawner.FindPartPlacement(memberId);
                if (placement == null)
                    continue;

                GameObject childPreview = _ctx.Spawner.TryLoadPackageAsset(part.assetRef);
                if (childPreview == null)
                {
                    // Asset not imported yet — create a primitive placeholder so the
                    // ghost silhouette still appears at the correct position/scale.
                    childPreview = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    childPreview.name = $"PreviewFallback_{memberId}";
                    if (previewRoot != null)
                        childPreview.transform.SetParent(previewRoot, false);
                }

                childPreview.transform.SetParent(subPreviewRoot.transform, false);

                Vector3 memberLocalPos;
                Quaternion memberLocalRot;
                Vector3 memberLocalScale;

                if (TryGetIntegratedMemberPlacement(integratedPlacement, memberId, out Vector3 integratedPos, out Quaternion integratedRot, out Vector3 integratedScale))
                {
                    memberLocalPos = InverseTransformPoint(
                        subPreviewRoot.transform.localPosition,
                        subPreviewRoot.transform.localRotation,
                        SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one),
                        integratedPos);
                    memberLocalRot = Quaternion.Inverse(subPreviewRoot.transform.localRotation) * integratedRot;
                    memberLocalScale = DivideScale(integratedScale, SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one));
                }
                else
                {
                    Vector3 memberPlayPos = new Vector3(placement.playPosition.x, placement.playPosition.y, placement.playPosition.z);
                    Quaternion memberPlayRot = !placement.playRotation.IsIdentity
                        ? new Quaternion(placement.playRotation.x, placement.playRotation.y, placement.playRotation.z, placement.playRotation.w)
                        : Quaternion.identity;
                    Vector3 memberPlayScale = SanitizeScale(new Vector3(placement.playScale.x, placement.playScale.y, placement.playScale.z), Vector3.one);

                    memberLocalPos = InverseTransformPoint(framePos, frameRot, frameScale, memberPlayPos);
                    memberLocalRot = Quaternion.Inverse(frameRot) * memberPlayRot;
                    memberLocalScale = DivideScale(memberPlayScale, frameScale);
                }

                if (fitPlacement?.drivenPartIds != null &&
                    Array.IndexOf(fitPlacement.drivenPartIds, memberId) >= 0)
                {
                    memberLocalPos += fitAxisLocal * (fitTravel - fitPlacement.minTravel);
                }

                childPreview.transform.SetLocalPositionAndRotation(memberLocalPos, memberLocalRot);
                childPreview.transform.localScale = memberLocalScale;
                if (fitPreviewChildren != null)
                    fitPreviewChildren.Add(childPreview.transform);

                foreach (Collider collider in childPreview.GetComponentsInChildren<Collider>(true))
                    _ctx.DestroyObject(collider);
            }

            if (isAxisFitPreview && fitPreviewChildren != null && fitPreviewChildren.Count > 0)
            {
                Vector3 anchorLocal = Vector3.zero;
                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    anchorLocal += fitPreviewChildren[i].localPosition;
                anchorLocal /= fitPreviewChildren.Count;

                subPreviewRoot.transform.localPosition = TransformPoint(
                    subPreviewRoot.transform.localPosition,
                    subPreviewRoot.transform.localRotation,
                    SanitizeScale(subPreviewRoot.transform.localScale, Vector3.one),
                    anchorLocal);

                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    fitPreviewChildren[i].localPosition -= anchorLocal;
            }

            PlacementPreviewInfo info = subPreviewRoot.AddComponent<PlacementPreviewInfo>();
            info.TargetId = targetId;
            info.SubassemblyId = subassemblyId;

            ApplyPreviewMaterialClickCollider(subPreviewRoot, minAxisSize: isAxisFitPreview ? 0.09f : 0.18f, paddingWorld: isAxisFitPreview ? 0.03f : 0.08f);
            MaterialHelper.ApplyPreviewMaterial(subPreviewRoot);
            _ctx.SpawnedPreviews.Add(subPreviewRoot);
            OseLog.Info($"[PartInteraction] Composite preview spawned for subassembly '{subassemblyId}' at target '{targetId}'. Total previews: {_ctx.SpawnedPreviews.Count}");
        }

        // ── Static helpers ──

        private static bool TryGetIntegratedMemberPlacement(
            IntegratedSubassemblyPreviewPlacement integratedPlacement,
            string partId,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (integratedPlacement?.memberPlacements == null || string.IsNullOrWhiteSpace(partId))
                return false;

            for (int i = 0; i < integratedPlacement.memberPlacements.Length; i++)
            {
                IntegratedMemberPreviewPlacement member = integratedPlacement.memberPlacements[i];
                if (member == null || !string.Equals(member.partId, partId, StringComparison.OrdinalIgnoreCase))
                    continue;

                position = ToVector3(member.position);
                rotation = ToQuaternion(member.rotation);
                scale = SanitizeScale(ToVector3(member.scale), Vector3.one);
                return true;
            }

            return false;
        }

        internal static void ApplyPreviewMaterialClickCollider(GameObject preview, float minAxisSize = 0.06f, float paddingWorld = 0f)
        {
            if (preview == null)
                return;

            Renderer[] previewRenderers = MaterialHelper.GetRenderers(preview);
            BoxCollider clickCollider = preview.GetComponent<BoxCollider>();
            if (clickCollider == null)
                clickCollider = preview.AddComponent<BoxCollider>();

            clickCollider.isTrigger = true;
            if (previewRenderers.Length <= 0)
                return;

            Bounds combined = previewRenderers[0].bounds;
            for (int ri = 1; ri < previewRenderers.Length; ri++)
                combined.Encapsulate(previewRenderers[ri].bounds);

            if (paddingWorld > 0f)
                combined.Expand(paddingWorld);

            Vector3 lossyScale = preview.transform.lossyScale;
            clickCollider.center = preview.transform.InverseTransformPoint(combined.center);
            Vector3 localSize = new Vector3(
                lossyScale.x != 0f ? combined.size.x / lossyScale.x : 1f,
                lossyScale.y != 0f ? combined.size.y / lossyScale.y : 1f,
                lossyScale.z != 0f ? combined.size.z / lossyScale.z : 1f);
            clickCollider.size = new Vector3(
                Mathf.Max(localSize.x, minAxisSize),
                Mathf.Max(localSize.y, minAxisSize),
                Mathf.Max(localSize.z, minAxisSize));
        }

        internal static Vector3 ToVector3(SceneFloat3 value) => new Vector3(value.x, value.y, value.z);

        internal static Quaternion ToQuaternion(SceneQuaternion value)
        {
            return !value.IsIdentity
                ? new Quaternion(value.x, value.y, value.z, value.w)
                : Quaternion.identity;
        }

        internal static Vector3 SanitizeScale(Vector3 value, Vector3 fallback)
        {
            return new Vector3(
                Mathf.Approximately(value.x, 0f) ? fallback.x : value.x,
                Mathf.Approximately(value.y, 0f) ? fallback.y : value.y,
                Mathf.Approximately(value.z, 0f) ? fallback.z : value.z);
        }

        internal static Vector3 DivideScale(Vector3 value, Vector3 divisor)
        {
            Vector3 safe = SanitizeScale(divisor, Vector3.one);
            return new Vector3(value.x / safe.x, value.y / safe.y, value.z / safe.z);
        }

        internal static Vector3 MultiplyScale(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        internal static Vector3 TransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 localPoint)
        {
            return origin + rotation * MultiplyScale(scale, localPoint);
        }

        internal static Vector3 InverseTransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 point)
        {
            Vector3 translated = Quaternion.Inverse(rotation) * (point - origin);
            return DivideScale(translated, scale);
        }

        internal static bool TryGetRenderableBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = MaterialHelper.GetRenderers(target);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                bounds = renderer.bounds;
                for (int j = i + 1; j < renderers.Length; j++)
                {
                    if (renderers[j] != null)
                        bounds.Encapsulate(renderers[j].bounds);
                }

                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(includeInactive: false);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null)
                    continue;

                bounds = collider.bounds;
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    if (colliders[j] != null)
                        bounds.Encapsulate(colliders[j].bounds);
                }

                return true;
            }

            return false;
        }

        // ── Private helpers ──

        private Transform GetPreviewRoot()
        {
            var setup = _ctx.Setup;
            return setup != null ? setup.PreviewRoot : null;
        }
    }
}
