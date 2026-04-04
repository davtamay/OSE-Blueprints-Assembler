using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages edit-mode ghost GameObjects — translucent silhouettes that show
    /// where parts will end up after the active step completes.
    ///
    /// Extracted from <see cref="PackagePartSpawner"/> for single-responsibility.
    /// Owns the ghost list lifetime; delegates asset loading and placement lookups
    /// back to the spawner via injected delegates.
    /// </summary>
    internal sealed class EditModeGhostManager
    {
        private readonly List<GameObject> _ghosts = new List<GameObject>();

        // ── Injected dependencies ──

        /// <summary>Read-only access to the spawner's live part list for mesh cloning.</summary>
        private readonly IReadOnlyList<GameObject> _spawnedParts;

        private readonly Func<Transform>                                                    _getPreviewRoot;
        private readonly Func<string, PartPreviewPlacement>                                 _findPartPlacement;
        private readonly Func<string, SubassemblyPreviewPlacement>                          _findSubassemblyPlacement;
        private readonly Func<string, string, IntegratedSubassemblyPreviewPlacement>        _findIntegratedPlacement;
        private readonly Func<string, string, ConstrainedSubassemblyFitPreviewPlacement>    _findConstrainedFitPlacement;
        private readonly PackageAssetResolver                                               _resolver;
        private readonly Func<string, GameObject>                                           _tryLoadAsset;

        public EditModeGhostManager(
            IReadOnlyList<GameObject> spawnedParts,
            Func<Transform> getPreviewRoot,
            Func<string, PartPreviewPlacement> findPartPlacement,
            Func<string, SubassemblyPreviewPlacement> findSubassemblyPlacement,
            Func<string, string, IntegratedSubassemblyPreviewPlacement> findIntegratedPlacement,
            Func<string, string, ConstrainedSubassemblyFitPreviewPlacement> findConstrainedFitPlacement,
            PackageAssetResolver resolver,
            Func<string, GameObject> tryLoadAsset)
        {
            _spawnedParts              = spawnedParts;
            _getPreviewRoot            = getPreviewRoot;
            _findPartPlacement         = findPartPlacement;
            _findSubassemblyPlacement  = findSubassemblyPlacement;
            _findIntegratedPlacement   = findIntegratedPlacement;
            _findConstrainedFitPlacement = findConstrainedFitPlacement;
            _resolver                  = resolver;
            _tryLoadAsset              = tryLoadAsset;
        }

        // ── Public API ──

        public void Clear()
        {
            foreach (var ghost in _ghosts)
            {
                if (ghost != null)
                    SafeDestroy(ghost);
            }
            _ghosts.Clear();
        }

        public void SpawnGhosts(
            MachinePackageDefinition pkg,
            StepDefinition[] orderedSteps,
            int targetSequenceIndex,
            bool fullyAssembled,
            Dictionary<string, int> partStepSeq,
            HashSet<string> subassemblyParts)
        {
            Clear();

            Transform previewRoot = _getPreviewRoot();
            if (Application.isPlaying || fullyAssembled || previewRoot == null)
                return;

            // Find the step at targetSequenceIndex
            StepDefinition currentStep = null;
            foreach (var step in orderedSteps)
            {
                if (step != null && step.sequenceIndex == targetSequenceIndex)
                { currentStep = step; break; }
            }

            if (currentStep == null)
            {
                OseLog.VerboseInfo($"[EditGhost] No step found at sequenceIndex={targetSequenceIndex}.");
                return;
            }

            string[] targetIds = currentStep.targetIds;
            if (targetIds == null || targetIds.Length == 0)
                return;

            // Build the set of part IDs that this step is actively placing.
            var currentStepPartIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (currentStep.requiredPartIds != null)
            {
                foreach (string pid in currentStep.requiredPartIds)
                {
                    if (!string.IsNullOrEmpty(pid))
                        currentStepPartIds.Add(pid);
                }
            }

            foreach (string targetId in targetIds)
            {
                if (string.IsNullOrEmpty(targetId)) continue;
                if (!pkg.TryGetTarget(targetId, out var target)) continue;

                // Subassembly target — spawn composite ghost with all member parts
                if (!string.IsNullOrWhiteSpace(target.associatedSubassemblyId))
                {
                    SpawnSubassemblyGhost(pkg, targetId, target, previewRoot);
                    continue;
                }

                string partId = target.associatedPartId;
                if (string.IsNullOrEmpty(partId)) continue;

                bool isCurrentStepPart = false;
                if (partStepSeq.TryGetValue(partId, out int partSeq) &&
                    partSeq == targetSequenceIndex &&
                    !subassemblyParts.Contains(partId))
                {
                    isCurrentStepPart = true;
                }
                else if (currentStepPartIds.Contains(partId))
                {
                    isCurrentStepPart = true;
                }

                if (!isCurrentStepPart) continue;

                PartPreviewPlacement pp = _findPartPlacement(partId);
                if (pp == null) continue;

                Vector3 ghostPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                Quaternion ghostRot = !pp.playRotation.IsIdentity
                    ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                    : Quaternion.identity;
                Vector3 ghostScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                if (ghostScale.sqrMagnitude < 0.00001f) ghostScale = Vector3.one;

                GameObject sourcePart = FindSpawnedPart(partId);

                GameObject ghost = null;
                if (sourcePart != null && !IsPrimitive(sourcePart))
                {
                    ghost = UnityEngine.Object.Instantiate(sourcePart, previewRoot);
                }
                else
                {
                    AssetResolution resolution = _resolver.Resolve(partId);
                    string assetRefToLoad = resolution.IsResolved ? resolution.AssetPath : null;

                    if (string.IsNullOrWhiteSpace(assetRefToLoad) &&
                        pkg.TryGetPart(partId, out var partDef) &&
                        !string.IsNullOrWhiteSpace(partDef.assetRef))
                    {
                        assetRefToLoad = partDef.assetRef;
                    }

                    if (!string.IsNullOrWhiteSpace(assetRefToLoad))
                        ghost = _tryLoadAsset(assetRefToLoad);
                }

                if (ghost == null)
                {
                    OseLog.VerboseInfo($"[EditGhost] Could not create ghost for part '{partId}' at target '{targetId}' — no mesh available.");
                    continue;
                }

                ghost.name = $"EditGhost_{partId}";
                ghost.transform.SetParent(previewRoot, false);
                ghost.transform.SetLocalPositionAndRotation(ghostPos, ghostRot);
                ghost.transform.localScale = ghostScale;
                ghost.SetActive(true);

                foreach (var col in ghost.GetComponentsInChildren<Collider>(true))
                    SafeDestroy(col);
                foreach (var interactable in ghost.GetComponentsInChildren<XRBaseInteractable>(true))
                    SafeDestroy(interactable);

                MaterialHelper.ApplyPreviewMaterial(ghost);
                _ghosts.Add(ghost);
                OseLog.VerboseInfo($"[EditGhost] Spawned ghost for '{partId}' at target '{targetId}' pos={ghostPos} scale={ghostScale}.");
            }
        }

        // ── Private helpers ──

        private void SpawnSubassemblyGhost(
            MachinePackageDefinition pkg,
            string targetId,
            TargetDefinition target,
            Transform previewRoot)
        {
            string subassemblyId = target.associatedSubassemblyId;
            if (!pkg.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) ||
                subassembly?.partIds == null || subassembly.partIds.Length == 0)
                return;

            SubassemblyPreviewPlacement frame = _findSubassemblyPlacement(subassemblyId);
            if (frame == null)
            {
                OseLog.VerboseInfo($"[EditGhost] Subassembly '{subassemblyId}' has no authored preview frame — skipping ghost.");
                return;
            }

            var subGhostRoot = new GameObject($"EditGhost_{subassemblyId}");
            subGhostRoot.transform.SetParent(previewRoot, false);

            Vector3    framePos   = PreviewSpawnManager.ToVector3(frame.position);
            Quaternion frameRot   = PreviewSpawnManager.ToQuaternion(frame.rotation);
            Vector3    frameScale = PreviewSpawnManager.SanitizeScale(PreviewSpawnManager.ToVector3(frame.scale), Vector3.one);

            subGhostRoot.transform.SetLocalPositionAndRotation(framePos, frameRot);
            subGhostRoot.transform.localScale = frameScale;

            IntegratedSubassemblyPreviewPlacement integratedPlacement =
                _findIntegratedPlacement(subassemblyId, targetId);

            ConstrainedSubassemblyFitPreviewPlacement fitPlacement =
                _findConstrainedFitPlacement(subassemblyId, targetId);

            Vector3 fitAxisLocal = fitPlacement != null
                ? PreviewSpawnManager.ToVector3(fitPlacement.fitAxisLocal) : Vector3.zero;
            if (fitAxisLocal.sqrMagnitude > 0.000001f)
                fitAxisLocal.Normalize();

            float fitTravel = fitPlacement != null
                ? Mathf.Clamp(
                    fitPlacement.completionTravel,
                    Mathf.Min(fitPlacement.minTravel, fitPlacement.maxTravel),
                    Mathf.Max(fitPlacement.minTravel, fitPlacement.maxTravel))
                : 0f;

            bool isAxisFitPreview = fitPlacement?.drivenPartIds != null && fitPlacement.drivenPartIds.Length > 0;
            var fitPreviewChildren = isAxisFitPreview ? new List<Transform>() : null;

            for (int i = 0; i < subassembly.partIds.Length; i++)
            {
                string memberId = subassembly.partIds[i];
                if (string.IsNullOrWhiteSpace(memberId) || !pkg.TryGetPart(memberId, out PartDefinition part))
                    continue;
                if (isAxisFitPreview && System.Array.IndexOf(fitPlacement.drivenPartIds, memberId) < 0)
                    continue;

                PartPreviewPlacement placement = _findPartPlacement(memberId);
                if (placement == null) continue;

                GameObject childGhost = null;
                GameObject sourcePart = FindSpawnedPart(memberId);

                if (sourcePart != null && !IsPrimitive(sourcePart))
                {
                    childGhost = UnityEngine.Object.Instantiate(sourcePart, subGhostRoot.transform);
                }
                else
                {
                    AssetResolution resolution = _resolver.Resolve(memberId);
                    string assetRef = resolution.IsResolved ? resolution.AssetPath : part.assetRef;
                    if (!string.IsNullOrWhiteSpace(assetRef))
                        childGhost = _tryLoadAsset(assetRef);
                }

                if (childGhost == null) continue;

                childGhost.name = $"EditGhost_{memberId}";
                childGhost.transform.SetParent(subGhostRoot.transform, false);

                Vector3 memberLocalPos;
                Quaternion memberLocalRot;
                Vector3 memberLocalScale;

                if (integratedPlacement?.memberPlacements != null &&
                    TryFindIntegratedMember(integratedPlacement, memberId,
                        out Vector3 intPos, out Quaternion intRot, out Vector3 intScale))
                {
                    memberLocalPos = PreviewSpawnManager.InverseTransformPoint(
                        subGhostRoot.transform.localPosition,
                        subGhostRoot.transform.localRotation,
                        PreviewSpawnManager.SanitizeScale(subGhostRoot.transform.localScale, Vector3.one),
                        intPos);
                    memberLocalRot   = Quaternion.Inverse(subGhostRoot.transform.localRotation) * intRot;
                    memberLocalScale = PreviewSpawnManager.DivideScale(intScale,
                        PreviewSpawnManager.SanitizeScale(subGhostRoot.transform.localScale, Vector3.one));
                }
                else
                {
                    Vector3    memberPlayPos   = new Vector3(placement.playPosition.x, placement.playPosition.y, placement.playPosition.z);
                    Quaternion memberPlayRot   = !placement.playRotation.IsIdentity
                        ? new Quaternion(placement.playRotation.x, placement.playRotation.y, placement.playRotation.z, placement.playRotation.w)
                        : Quaternion.identity;
                    Vector3    memberPlayScale  = PreviewSpawnManager.SanitizeScale(
                        new Vector3(placement.playScale.x, placement.playScale.y, placement.playScale.z), Vector3.one);

                    memberLocalPos   = PreviewSpawnManager.InverseTransformPoint(framePos, frameRot, frameScale, memberPlayPos);
                    memberLocalRot   = Quaternion.Inverse(frameRot) * memberPlayRot;
                    memberLocalScale = PreviewSpawnManager.DivideScale(memberPlayScale, frameScale);
                }

                if (fitPlacement?.drivenPartIds != null &&
                    System.Array.IndexOf(fitPlacement.drivenPartIds, memberId) >= 0)
                {
                    memberLocalPos += fitAxisLocal * (fitTravel - fitPlacement.minTravel);
                }

                childGhost.transform.SetLocalPositionAndRotation(memberLocalPos, memberLocalRot);
                childGhost.transform.localScale = memberLocalScale;
                childGhost.SetActive(true);
                fitPreviewChildren?.Add(childGhost.transform);

                foreach (var col in childGhost.GetComponentsInChildren<Collider>(true))
                    SafeDestroy(col);
                foreach (var interactable in childGhost.GetComponentsInChildren<XRBaseInteractable>(true))
                    SafeDestroy(interactable);
            }

            if (isAxisFitPreview && fitPreviewChildren != null && fitPreviewChildren.Count > 0)
            {
                Vector3 anchorLocal = Vector3.zero;
                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    anchorLocal += fitPreviewChildren[i].localPosition;
                anchorLocal /= fitPreviewChildren.Count;

                subGhostRoot.transform.localPosition = PreviewSpawnManager.TransformPoint(
                    subGhostRoot.transform.localPosition,
                    subGhostRoot.transform.localRotation,
                    PreviewSpawnManager.SanitizeScale(subGhostRoot.transform.localScale, Vector3.one),
                    anchorLocal);

                for (int i = 0; i < fitPreviewChildren.Count; i++)
                    fitPreviewChildren[i].localPosition -= anchorLocal;
            }

            MaterialHelper.ApplyPreviewMaterial(subGhostRoot);
            _ghosts.Add(subGhostRoot);
            OseLog.VerboseInfo($"[EditGhost] Spawned subassembly ghost for '{subassemblyId}' at target '{targetId}'.");
        }

        private GameObject FindSpawnedPart(string partId)
        {
            foreach (var go in _spawnedParts)
            {
                if (go != null && string.Equals(go.name, partId, System.StringComparison.OrdinalIgnoreCase))
                    return go;
            }
            return null;
        }

        private static bool TryFindIntegratedMember(
            IntegratedSubassemblyPreviewPlacement intPlacement,
            string partId,
            out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale    = Vector3.one;
            if (intPlacement?.memberPlacements == null) return false;

            for (int i = 0; i < intPlacement.memberPlacements.Length; i++)
            {
                IntegratedMemberPreviewPlacement m = intPlacement.memberPlacements[i];
                if (m == null || !string.Equals(m.partId, partId, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                position = PreviewSpawnManager.ToVector3(m.position);
                rotation = PreviewSpawnManager.ToQuaternion(m.rotation);
                scale    = PreviewSpawnManager.SanitizeScale(PreviewSpawnManager.ToVector3(m.scale), Vector3.one);
                return true;
            }
            return false;
        }

        private static bool IsPrimitive(GameObject go)
        {
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            string meshName = mf.sharedMesh.name;
            return meshName == "Sphere" || meshName == "Capsule" || meshName == "Cylinder"
                || meshName == "Cube"   || meshName == "Plane"   || meshName == "Quad";
        }

        private static void SafeDestroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
