using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Builds transform-owner proxies for finished subassemblies without reparenting the
    /// actual part GameObjects. Member part poses are recomputed from the proxy transform
    /// and cached authored local offsets.
    /// </summary>
    public sealed class SubassemblyPlacementController : IDisposable, OSE.Runtime.ISubassemblyPlacementService
    {
        private readonly ISpawnerContext _ctx;
        private readonly Dictionary<string, ProxyRecord> _records = new Dictionary<string, ProxyRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _memberToSubassembly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Tracks stacking subassemblies that must stay at cube positions until all
        // member parts are confirmed loaded. Seeded by EnforceIntegratedPositions;
        // cleared by ResetReplayState. No time limit — retried every frame until confirmed.
        private readonly Dictionary<string, StepDefinition> _pendingIntegratedBySubassembly =
            new Dictionary<string, StepDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _pendingRemovalBuffer = new List<string>();

        private string _activeSubassemblyId;

        private sealed class ProxyRecord
        {
            public string SubassemblyId;
            public GameObject Root;
            public string[] MemberPartIds;
            public Vector3[] MemberLocalPositions;
            public Quaternion[] MemberLocalRotations;
            public Vector3[] MemberLocalScales;
            public string CurrentTargetId;
            public FitRecord ActiveFit;
            public float CurrentFitTravel;

            // ── Perf: cached member GameObjects (avoid FindSpawnedPart linear scan per frame) ──
            public GameObject[] CachedMemberObjects;

            // ── Perf: cached local-space bounds (avoid per-frame renderer queries) ──
            public Bounds CachedLocalBounds;
            public bool HasCachedBounds;

            // ── Perf: dirty flag — skip member update when proxy hasn't moved ──
            public bool TransformDirty;
            public Vector3 LastPosition;
            public Quaternion LastRotation;
            public Vector3 LastScale;
        }

        private sealed class FitRecord
        {
            public string TargetId;
            public Vector3 AxisLocal;
            public float MinTravel;
            public float MaxTravel;
            public float CompletionTravel;
            public float SnapTolerance;
            public HashSet<string> DrivenPartIds;
        }

        internal SubassemblyPlacementController(ISpawnerContext context)
        {
            _ctx = context;
        }

        public string ActiveSubassemblyId => _activeSubassemblyId;

        public void Dispose()
        {
            ClearAll();
        }

        public void ClearAll()
        {
            foreach (KeyValuePair<string, ProxyRecord> pair in _records)
            {
                if (pair.Value?.Root != null)
                    UnityEngine.Object.Destroy(pair.Value.Root);
            }

            _records.Clear();
            _memberToSubassembly.Clear();
            _activeSubassemblyId = null;
        }

        public void ResetReplayState()
        {
            foreach (KeyValuePair<string, ProxyRecord> pair in _records)
            {
                ProxyRecord record = pair.Value;
                if (record?.Root == null)
                    continue;

                ClearFitState(record);
                record.CurrentTargetId = null;
                record.Root.SetActive(false);
            }

            _activeSubassemblyId = null;
            _pendingIntegratedBySubassembly.Clear();
        }

        public void RefreshForStep(string stepId)
        {
            ResetActiveProxyOnly();

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out StepDefinition step) || step == null)
                return;

            if (!step.IsPlacement || string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                return;

            if (!IsSubassemblyReady(step.requiredSubassemblyId))
            {
                OseLog.Warn($"[SubassemblyPlacement] Step '{stepId}' requires subassembly '{step.requiredSubassemblyId}', but not all member parts are completed.");
                return;
            }

            if (!EnsureProxyRecord(step.requiredSubassemblyId, out ProxyRecord record))
                return;

            ClearFitState(record);
            if (step.IsAxisFitPlacement &&
                step.targetIds != null &&
                step.targetIds.Length == 1 &&
                TryActivateConstrainedFit(record, step.targetIds[0]))
            {
                ApplyProxyTransform(record);
                record.Root.SetActive(true);
                _activeSubassemblyId = record.SubassemblyId;
                return;
            }

            if (!MoveProxyToParkingPlacement(record))
                MoveProxyToAuthoredFrame(record);
            ApplyProxyTransform(record);
            record.Root.SetActive(true);
            _activeSubassemblyId = record.SubassemblyId;
        }

        public void ApplyCompletedSubassemblyParking(string activeStepId, StepDefinition[] completedSteps)
        {
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || completedSteps == null || completedSteps.Length == 0)
                return;

            string activeFabricationSubassemblyId = null;
            if (!string.IsNullOrWhiteSpace(activeStepId) &&
                package.TryGetStep(activeStepId, out StepDefinition activeStep) &&
                activeStep != null)
            {
                activeFabricationSubassemblyId = activeStep.subassemblyId;
            }

            HashSet<string> completedStepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> stackedSubassemblyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < completedSteps.Length; i++)
            {
                StepDefinition step = completedSteps[i];
                if (step == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(step.id))
                    completedStepIds.Add(step.id);

                if (!string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                    stackedSubassemblyIds.Add(step.requiredSubassemblyId);
            }

            foreach (SubassemblyDefinition subassembly in package.GetSubassemblies())
            {
                if (subassembly == null || string.IsNullOrWhiteSpace(subassembly.id))
                    continue;

                if (string.Equals(subassembly.id, activeFabricationSubassemblyId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (stackedSubassemblyIds.Contains(subassembly.id))
                {
                    // Stacked subassemblies don't need a visible proxy, but we still
                    // need the member→subassembly mapping so clicking on their parts
                    // shows subassembly info and lock checks work correctly.
                    EnsureMemberMapping(subassembly);
                    continue;
                }

                if (!IsSubassemblyReady(subassembly.id))
                    continue;

                if (!AreAllSubassemblyStepsCompleted(subassembly, completedStepIds, package))
                    continue;

                if (!EnsureProxyRecord(subassembly.id, out ProxyRecord record))
                    continue;

                if (!MoveProxyToParkingPlacement(record))
                    continue;

                ApplyProxyTransform(record);
                record.Root.SetActive(false);
            }
        }

        public void HandleStepCompleted(string stepId)
        {
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out StepDefinition step) || step == null)
                return;

            if (string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                return;

            if (_records.TryGetValue(step.requiredSubassemblyId, out ProxyRecord record) && record?.Root != null)
            {
                ClearFitState(record);

                // MoveStepPartsToPlayPosition (called just before this in StepStateResponder)
                // moves all requiredPartIds to their flat playPosition, undoing the integrated
                // cube placement that was committed when the user placed the panel.
                // Re-apply integrated positions so stacked bars stay at their cube positions.
                if (!string.IsNullOrWhiteSpace(record.CurrentTargetId))
                    TryApplyIntegratedPlacement(record, record.CurrentTargetId);
                else
                    record.Root.SetActive(false);
            }

            if (string.Equals(_activeSubassemblyId, step.requiredSubassemblyId, StringComparison.OrdinalIgnoreCase))
                _activeSubassemblyId = null;
        }

        /// <summary>
        /// Hides member bars of every proxy that has been built but not yet placed
        /// (<c>CurrentTargetId == null</c>) and is not the currently active proxy.
        ///
        /// Called after <see cref="RefreshForStep"/> so completed-fabrication panels
        /// don't remain visible during other panels' stacking steps.  Their bars are
        /// re-shown by <see cref="ApplyProxyTransform"/> when their own stacking step
        /// activates.
        /// </summary>
        public void HideNonActivePendingProxyBars()
        {
            foreach (var pair in _records)
            {
                ProxyRecord record = pair.Value;
                if (record?.Root == null || record.MemberPartIds == null)
                    continue;

                // Already placed at a target → bars are at cube positions, keep visible.
                if (!string.IsNullOrWhiteSpace(record.CurrentTargetId))
                    continue;

                // This is the currently active proxy → its bars are shown by ApplyProxyTransform.
                if (string.Equals(_activeSubassemblyId, pair.Key, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Pending (fabrication complete, not yet stacked) → hide bars so they
                // don't clutter the scene during other panels' stacking steps.
                // Guard: never hide a bar that is already Completed — it was explicitly
                // placed in a prior step and must remain visible (e.g. when navigating
                // backward from a stacking step into the fabrication sequence).
                for (int i = 0; i < record.MemberPartIds.Length; i++)
                {
                    string memberId = record.MemberPartIds[i];
                    PartPlacementState memberState = _ctx.GetPartState(memberId);
                    if (memberState == PartPlacementState.Completed ||
                        memberState == PartPlacementState.PlacedVirtually)
                        continue;

                    GameObject partGo = (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                        ? record.CachedMemberObjects[i] : null;
                    if (partGo == null)
                        partGo = _ctx.FindSpawnedPart(memberId);
                    if (partGo != null) partGo.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Final-pass guarantee: for every completed stacking step, forces member bars
        /// to their integrated cube positions using per-member lookup (no targetId
        /// required). Also seeds the pending-integration set so that any member parts
        /// not yet loaded are repositioned as soon as they appear (see
        /// <see cref="TickPendingIntegration"/>). Call AFTER all other visual restores.
        /// </summary>
        public void EnforceIntegratedPositions(StepDefinition[] completedSteps)
        {
            if (completedSteps == null || completedSteps.Length == 0)
                return;

            for (int s = 0; s < completedSteps.Length; s++)
            {
                StepDefinition step = completedSteps[s];
                if (step == null || string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                    continue;

                // Seed persistent enforcement — retried every frame until all members load.
                _pendingIntegratedBySubassembly[step.requiredSubassemblyId] = step;

                // Best-effort immediate pass (some parts may not be loaded yet).
                TryEnforceSubassemblyIntegration(step);
            }
        }

        /// <summary>
        /// Called every frame from <see cref="PartInteractionBridge"/>. Retries
        /// integrated-position enforcement for every pending stacking subassembly.
        /// Once all member parts for a subassembly are found and positioned it is
        /// removed from the pending set — no further retries until the next navigation.
        /// </summary>
        public void TickPendingIntegration()
        {
            if (_pendingIntegratedBySubassembly.Count == 0) return;

            _pendingRemovalBuffer.Clear();
            foreach (KeyValuePair<string, StepDefinition> kvp in _pendingIntegratedBySubassembly)
            {
                if (TryEnforceSubassemblyIntegration(kvp.Value))
                    _pendingRemovalBuffer.Add(kvp.Key);
            }
            foreach (string id in _pendingRemovalBuffer)
                _pendingIntegratedBySubassembly.Remove(id);
        }

        /// <summary>
        /// Attempts to move all member parts of a stacking step to their integrated
        /// cube positions. Returns <c>true</c> when every member that has integration
        /// data was found and positioned (enforcement complete); <c>false</c> when at
        /// least one such part is not yet spawned (retry next frame).
        /// </summary>
        private bool TryEnforceSubassemblyIntegration(StepDefinition step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.requiredSubassemblyId)) return true;

            string[] memberIds = null;
            GameObject[] cachedObjects = null;
            GameObject proxyRoot = null;

            if (_records.TryGetValue(step.requiredSubassemblyId, out ProxyRecord record) && record != null)
            {
                memberIds = record.MemberPartIds;
                cachedObjects = record.CachedMemberObjects;
                proxyRoot = record.Root;
            }
            else
            {
                MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
                if (package != null &&
                    package.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition sub) &&
                    sub?.partIds != null)
                    memberIds = sub.partIds;
            }

            if (memberIds == null || memberIds.Length == 0) return true;

            bool allFound = true;
            bool anyMoved = false;

            for (int i = 0; i < memberIds.Length; i++)
            {
                string pid = memberIds[i];
                if (string.IsNullOrWhiteSpace(pid)) continue;

                IntegratedMemberPreviewPlacement imp = _ctx.Spawner.FindIntegratedMemberPlacement(pid);
                if (imp == null) continue; // no integration data — not a blocking condition

                // Use cached reference if live; fall back to lookup for stale/destroyed GOs.
                // A ternary would pick the dead C# reference and skip FindSpawnedPart.
                GameObject partGo = (cachedObjects != null && i < cachedObjects.Length)
                    ? cachedObjects[i] : null;
                if (partGo == null)
                {
                    partGo = _ctx.FindSpawnedPart(pid);
                    if (cachedObjects != null && i < cachedObjects.Length)
                        cachedObjects[i] = partGo;
                }
                if (partGo == null) { allFound = false; continue; }

                partGo.transform.SetLocalPositionAndRotation(
                    ToVector3(imp.position),
                    ToQuaternion(imp.rotation));
                partGo.transform.localScale = SanitizeScale(ToVector3(imp.scale), Vector3.one);
                partGo.SetActive(true);
                anyMoved = true;
            }

            if (anyMoved && proxyRoot != null)
                proxyRoot.SetActive(false);

            return allFound;
        }

        public void RestoreCompletedPlacements(StepDefinition[] completedSteps)
        {
            if (completedSteps == null || completedSteps.Length == 0)
                return;

            for (int i = 0; i < completedSteps.Length; i++)
            {
                StepDefinition step = completedSteps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                    continue;

                if (!EnsureProxyRecord(step.requiredSubassemblyId, out ProxyRecord record))
                    continue;

                string targetId = step.targetIds != null && step.targetIds.Length > 0
                    ? step.targetIds[0]
                    : null;

                ClearFitState(record);
                if (TryApplyIntegratedPlacement(record, targetId))
                    continue;

                if (TryRestoreConstrainedFitPlacement(record, targetId))
                    continue;

                if (!TryGetTargetLocalPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
                    continue;

                record.Root.transform.SetLocalPositionAndRotation(targetPos, targetRot);
                record.Root.transform.localScale = targetScale;
                record.CurrentTargetId = targetId;
                ApplyProxyTransform(record);
            }
        }

        public bool IsSubassemblyReady(string subassemblyId)
        {
            if (string.IsNullOrWhiteSpace(subassemblyId))
                return false;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) || subassembly == null)
                return false;

            string[] memberIds = subassembly.partIds ?? Array.Empty<string>();
            if (memberIds.Length == 0)
                return false;

            for (int i = 0; i < memberIds.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(memberIds[i]))
                    continue;

                PartPlacementState state = _ctx.GetPartState(memberIds[i]);
                if (state != PartPlacementState.Completed && state != PartPlacementState.PlacedVirtually)
                {
                    OseLog.Warn($"[SubassemblyPlacement] IsSubassemblyReady('{subassemblyId}'): member '{memberIds[i]}' is {state}, not Completed/PlacedVirtually.");
                    return false;
                }
            }

            return true;
        }

        public bool IsProxy(GameObject target)
        {
            return target != null && target.GetComponent<SubassemblyPlacementProxy>() != null;
        }

        public bool TryGetProxy(string subassemblyId, out GameObject proxyRoot)
        {
            proxyRoot = null;
            if (string.IsNullOrWhiteSpace(subassemblyId))
                return false;

            if (!_records.TryGetValue(subassemblyId, out ProxyRecord record) || record?.Root == null)
                return false;

            proxyRoot = record.Root;
            return true;
        }

        public bool TryGetSubassemblyId(GameObject target, out string subassemblyId)
        {
            subassemblyId = null;
            if (target == null)
                return false;

            SubassemblyPlacementProxy proxy = target.GetComponent<SubassemblyPlacementProxy>();
            if (proxy != null && !string.IsNullOrWhiteSpace(proxy.SubassemblyId))
            {
                subassemblyId = proxy.SubassemblyId;
                return true;
            }

            return _memberToSubassembly.TryGetValue(target.name, out subassemblyId);
        }

        public GameObject ResolveSelectableFromHit(Transform hitTransform)
        {
            if (hitTransform == null)
                return null;

            // Fast path: check the active proxy first (the one being dragged/placed).
            if (!string.IsNullOrWhiteSpace(_activeSubassemblyId)
                && _records.TryGetValue(_activeSubassemblyId, out ProxyRecord activeRecord)
                && activeRecord?.Root != null && activeRecord.Root.activeInHierarchy)
            {
                Transform current = hitTransform;
                while (current != null)
                {
                    SubassemblyPlacementProxy proxy = current.GetComponent<SubassemblyPlacementProxy>();
                    if (proxy != null && string.Equals(proxy.SubassemblyId, _activeSubassemblyId, StringComparison.OrdinalIgnoreCase))
                        return activeRecord.Root;
                    current = current.parent;
                }

                GameObject partGo = FindMemberPartFromHit(activeRecord, hitTransform);
                if (partGo != null)
                    return activeRecord.Root;
            }

            // Fallback: check if the hit belongs to ANY subassembly with an active proxy
            // (e.g. parked/completed subassemblies that are visible but not currently being placed).
            Transform walkHit = hitTransform;
            Transform previewRoot = _ctx.Setup?.PreviewRoot;
            while (walkHit != null && walkHit != previewRoot)
            {
                // Direct proxy hit
                SubassemblyPlacementProxy proxyComp = walkHit.GetComponent<SubassemblyPlacementProxy>();
                if (proxyComp != null
                    && !string.IsNullOrWhiteSpace(proxyComp.SubassemblyId)
                    && _records.TryGetValue(proxyComp.SubassemblyId, out ProxyRecord hitRecord)
                    && hitRecord?.Root != null && hitRecord.Root.activeInHierarchy)
                {
                    return hitRecord.Root;
                }

                // Member part reverse lookup
                if (_memberToSubassembly.TryGetValue(walkHit.name, out string ownerSubId)
                    && _records.TryGetValue(ownerSubId, out ProxyRecord ownerRecord)
                    && ownerRecord?.Root != null && ownerRecord.Root.activeInHierarchy)
                {
                    return ownerRecord.Root;
                }

                walkHit = walkHit.parent;
            }

            return null;
        }

        public bool TryGetDisplayInfo(GameObject target, out string displayName, out string description)
        {
            displayName = null;
            description = null;

            if (!TryGetSubassemblyId(target, out string subassemblyId))
                return false;

            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) || subassembly == null)
                return false;

            displayName = subassembly.GetDisplayName();
            description = subassembly.description ?? string.Empty;
            return true;
        }

        public IEnumerable<GameObject> EnumerateMemberParts(GameObject target)
        {
            if (!TryGetSubassemblyId(target, out string subassemblyId))
                yield break;

            // Prefer proxy record if available (has cached member list).
            if (_records.TryGetValue(subassemblyId, out ProxyRecord record) && record?.MemberPartIds != null)
            {
                for (int i = 0; i < record.MemberPartIds.Length; i++)
                {
                    GameObject partGo = _ctx.FindSpawnedPart(record.MemberPartIds[i]);
                    if (partGo != null)
                        yield return partGo;
                }
                yield break;
            }

            // Fallback for stacked subassemblies without a proxy record:
            // look up partIds from the subassembly definition directly.
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package != null && package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) &&
                subassembly?.partIds != null)
            {
                for (int i = 0; i < subassembly.partIds.Length; i++)
                {
                    GameObject partGo = _ctx.FindSpawnedPart(subassembly.partIds[i]);
                    if (partGo != null)
                        yield return partGo;
                }
            }
        }

        public bool TryResolveTargetPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            return TryGetTargetLocalPose(targetId, out position, out rotation, out scale);
        }

        public bool TryGetActiveFitGuide(string subassemblyId, out Vector3 currentWorldPos, out Vector3 finalWorldPos, out Vector3 upWorld)
        {
            currentWorldPos = Vector3.zero;
            finalWorldPos = Vector3.zero;
            upWorld = Vector3.up;

            if (string.IsNullOrWhiteSpace(subassemblyId) ||
                !_records.TryGetValue(subassemblyId, out ProxyRecord record) ||
                record?.Root == null ||
                record.ActiveFit == null ||
                record.ActiveFit.DrivenPartIds == null ||
                record.ActiveFit.DrivenPartIds.Count == 0)
            {
                return false;
            }

            int currentCount = 0;
            int finalCount = 0;
            Vector3 currentAccum = Vector3.zero;
            Vector3 finalAccum = Vector3.zero;

            Transform root = record.Root.transform;
            Vector3 rootPos = root.localPosition;
            Quaternion rootRot = root.localRotation;
            Vector3 rootScale = SanitizeScale(root.localScale, Vector3.one);

            for (int i = 0; i < record.MemberPartIds.Length; i++)
            {
                string partId = record.MemberPartIds[i];
                if (!record.ActiveFit.DrivenPartIds.Contains(partId))
                    continue;

                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo != null && TryGetWorldBounds(partGo, out Bounds currentBounds))
                {
                    currentAccum += currentBounds.center;
                    upWorld = partGo.transform.up;
                    currentCount++;
                }

                Vector3 finalLocalPosition = record.MemberLocalPositions[i] +
                    (record.ActiveFit.AxisLocal * (record.ActiveFit.CompletionTravel - record.ActiveFit.MinTravel));
                Vector3 finalPreviewLocal = TransformPoint(rootPos, rootRot, rootScale, finalLocalPosition);
                Vector3 finalWorld = root.parent != null
                    ? root.parent.TransformPoint(finalPreviewLocal)
                    : finalPreviewLocal;
                finalAccum += finalWorld;
                finalCount++;
            }

            if (currentCount == 0 || finalCount == 0)
                return false;

            currentWorldPos = currentAccum / currentCount;
            finalWorldPos = finalAccum / finalCount;
            return true;
        }

        public bool TryApplyPlacementPreview(GameObject target, string targetId, float nearestDist, float previewRadius)
        {
            if (!TryGetSubassemblyId(target, out string subassemblyId))
                return false;

            if (!_records.TryGetValue(subassemblyId, out ProxyRecord record) ||
                record?.Root == null ||
                record.ActiveFit == null ||
                !string.Equals(record.ActiveFit.TargetId, targetId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetTargetLocalPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
            {
                return false;
            }

            Vector3 draggedLocalPos = target.transform.localPosition;
            Quaternion draggedLocalRot = target.transform.localRotation;
            Vector3 draggedLocalScale = SanitizeScale(target.transform.localScale, targetScale);

            Vector3 previewAxis = targetRot * record.ActiveFit.AxisLocal;
            if (previewAxis.sqrMagnitude < 0.000001f)
                previewAxis = Vector3.right;
            previewAxis.Normalize();

            float projectedTravel = record.ActiveFit.MinTravel + Vector3.Dot(draggedLocalPos - targetPos, previewAxis);
            projectedTravel = Mathf.Clamp(projectedTravel, record.ActiveFit.MinTravel, record.ActiveFit.MaxTravel);

            float closeness = 1f - Mathf.Clamp01(nearestDist / Mathf.Max(previewRadius, 0.001f));
            float travelBlend = Mathf.Lerp(0.08f, 0.3f, closeness);
            float rotationBlend = Mathf.Lerp(0.08f, 0.3f, closeness);
            float scaleBlend = Mathf.Lerp(0.04f, 0.14f, closeness);

            record.Root.transform.localPosition = targetPos;
            record.Root.transform.localRotation = Quaternion.Slerp(draggedLocalRot, targetRot, rotationBlend);
            record.Root.transform.localScale = Vector3.Lerp(draggedLocalScale, targetScale, scaleBlend);
            record.CurrentFitTravel = Mathf.Lerp(record.CurrentFitTravel, projectedTravel, travelBlend);
            ApplyProxyTransform(record);
            return true;
        }

        public bool IsPlacementCommitReady(GameObject target, string targetId)
        {
            if (!TryGetSubassemblyId(target, out string subassemblyId))
                return false;

            if (!_records.TryGetValue(subassemblyId, out ProxyRecord record) || record == null)
                return false;

            if (record.ActiveFit == null || !string.Equals(record.ActiveFit.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                return true;

            return Mathf.Abs(record.CurrentFitTravel - record.ActiveFit.CompletionTravel) <= record.ActiveFit.SnapTolerance;
        }

        public bool TryApplyPlacement(string subassemblyId, string targetId)
        {
            if (!EnsureProxyRecord(subassemblyId, out ProxyRecord record))
                return false;

            if (TryApplyIntegratedPlacement(record, targetId))
                return true;

            if (TryCommitConstrainedFitPlacement(record, targetId))
                return true;

            if (!TryGetTargetLocalPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
                return false;

            record.Root.transform.SetLocalPositionAndRotation(targetPos, targetRot);
            record.Root.transform.localScale = targetScale;
            record.CurrentTargetId = targetId;
            ApplyProxyTransform(record);
            return true;
        }

        public void ApplyProxyTransform(GameObject target)
        {
            if (!TryGetSubassemblyId(target, out string subassemblyId))
                return;

            if (_records.TryGetValue(subassemblyId, out ProxyRecord record))
                ApplyProxyTransform(record);
        }

        public bool IsActiveStepPlacementSatisfied(string stepId)
        {
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out StepDefinition step) || step == null)
                return false;

            if (string.IsNullOrWhiteSpace(step.requiredSubassemblyId))
                return false;

            if (!_records.TryGetValue(step.requiredSubassemblyId, out ProxyRecord record))
                return false;

            string targetId = step.targetIds != null && step.targetIds.Length == 1
                ? step.targetIds[0]
                : null;

            return !string.IsNullOrWhiteSpace(targetId) &&
                   string.Equals(record.CurrentTargetId, targetId, StringComparison.OrdinalIgnoreCase);
        }

        private void ResetActiveProxyOnly()
        {
            if (string.IsNullOrWhiteSpace(_activeSubassemblyId))
                return;

            if (_records.TryGetValue(_activeSubassemblyId, out ProxyRecord record) && record?.Root != null)
                record.Root.SetActive(false);

            _activeSubassemblyId = null;
        }

        /// <summary>
        /// Populates _memberToSubassembly for all parts in a subassembly without
        /// creating a proxy GameObject. Used for stacked/completed subassemblies
        /// that don't need a visible proxy but still need the mapping for info display.
        /// </summary>
        private void EnsureMemberMapping(SubassemblyDefinition subassembly)
        {
            if (subassembly?.partIds == null) return;
            for (int i = 0; i < subassembly.partIds.Length; i++)
            {
                string partId = subassembly.partIds[i];
                if (!string.IsNullOrWhiteSpace(partId))
                    _memberToSubassembly[partId] = subassembly.id;
            }
        }

        private bool EnsureProxyRecord(string subassemblyId, out ProxyRecord record)
        {
            if (_records.TryGetValue(subassemblyId, out record) && record?.Root != null)
                return true;

            record = BuildProxyRecord(subassemblyId);
            if (record == null)
                return false;

            _records[subassemblyId] = record;
            return true;
        }

        private ProxyRecord BuildProxyRecord(string subassemblyId)
        {
            MachinePackageDefinition package = _ctx.Spawner?.CurrentPackage;
            PreviewSceneSetup setup = _ctx.Setup;
            Transform previewRoot = setup != null ? setup.PreviewRoot : null;
            if (package == null || previewRoot == null)
                return null;

            if (!package.TryGetSubassembly(subassemblyId, out SubassemblyDefinition subassembly) || subassembly == null)
                return null;

            SubassemblyPreviewPlacement frame = _ctx.Spawner.FindSubassemblyPlacement(subassemblyId);
            if (frame == null)
            {
                OseLog.Warn($"[SubassemblyPlacement] Missing previewConfig.subassemblyPlacements entry for '{subassemblyId}'.");
                return null;
            }

            string[] memberIds = subassembly.partIds ?? Array.Empty<string>();
            if (memberIds.Length == 0)
                return null;

            GameObject proxyRoot = new GameObject($"SubassemblyProxy_{subassemblyId}");
            proxyRoot.transform.SetParent(previewRoot, false);
            proxyRoot.transform.SetLocalPositionAndRotation(ToVector3(frame.position), ToQuaternion(frame.rotation));
            proxyRoot.transform.localScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);

            SubassemblyPlacementProxy proxy = proxyRoot.AddComponent<SubassemblyPlacementProxy>();
            proxy.SubassemblyId = subassemblyId;

            Vector3[] localPositions = new Vector3[memberIds.Length];
            Quaternion[] localRotations = new Quaternion[memberIds.Length];
            Vector3[] localScales = new Vector3[memberIds.Length];

            for (int i = 0; i < memberIds.Length; i++)
            {
                string partId = memberIds[i];
                if (string.IsNullOrWhiteSpace(partId))
                    continue;

                PartPreviewPlacement partPlacement = _ctx.Spawner.FindPartPlacement(partId);
                if (partPlacement == null)
                {
                    OseLog.Warn($"[SubassemblyPlacement] Missing part placement for subassembly member '{partId}'.");
                    UnityEngine.Object.Destroy(proxyRoot);
                    return null;
                }

                Vector3 playPos = new Vector3(partPlacement.playPosition.x, partPlacement.playPosition.y, partPlacement.playPosition.z);
                Quaternion playRot = !partPlacement.playRotation.IsIdentity
                    ? new Quaternion(partPlacement.playRotation.x, partPlacement.playRotation.y, partPlacement.playRotation.z, partPlacement.playRotation.w)
                    : Quaternion.identity;
                Vector3 playScale = SanitizeScale(new Vector3(partPlacement.playScale.x, partPlacement.playScale.y, partPlacement.playScale.z), Vector3.one);

                localPositions[i] = InverseTransformPoint(proxyRoot.transform.localPosition, proxyRoot.transform.localRotation, proxyRoot.transform.localScale, playPos);
                localRotations[i] = Quaternion.Inverse(proxyRoot.transform.localRotation) * playRot;
                localScales[i] = DivideScale(playScale, proxyRoot.transform.localScale);
                _memberToSubassembly[partId] = subassemblyId;
            }

            // Cache member GameObjects to avoid per-frame FindSpawnedPart linear scans.
            GameObject[] cachedMembers = new GameObject[memberIds.Length];
            for (int i = 0; i < memberIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(memberIds[i]))
                    cachedMembers[i] = _ctx.FindSpawnedPart(memberIds[i]);
            }

            ProxyRecord record = new ProxyRecord
            {
                SubassemblyId = subassemblyId,
                Root = proxyRoot,
                MemberPartIds = memberIds,
                MemberLocalPositions = localPositions,
                MemberLocalRotations = localRotations,
                MemberLocalScales = localScales,
                CachedMemberObjects = cachedMembers,
                TransformDirty = true
            };

            RecalculateBoundsAndCollider(record);
            proxyRoot.SetActive(false);
            return record;
        }

        private void MoveProxyToAuthoredFrame(ProxyRecord record)
        {
            if (record?.Root == null)
                return;

            SubassemblyPreviewPlacement frame = _ctx.Spawner.FindSubassemblyPlacement(record.SubassemblyId);
            if (frame == null)
                return;

            record.Root.transform.SetLocalPositionAndRotation(ToVector3(frame.position), ToQuaternion(frame.rotation));
            record.Root.transform.localScale = SanitizeScale(ToVector3(frame.scale), Vector3.one);
            record.CurrentTargetId = null;
            record.TransformDirty = true;
        }

        private bool MoveProxyToParkingPlacement(ProxyRecord record)
        {
            if (record?.Root == null)
                return false;

            SubassemblyPreviewPlacement parking = _ctx.Spawner.FindCompletedSubassemblyParkingPlacement(record.SubassemblyId);
            if (parking == null)
                return false;

            record.Root.transform.SetLocalPositionAndRotation(ToVector3(parking.position), ToQuaternion(parking.rotation));
            record.Root.transform.localScale = SanitizeScale(ToVector3(parking.scale), Vector3.one);
            record.CurrentTargetId = null;
            record.TransformDirty = true;
            return true;
        }

        private void ApplyProxyTransform(ProxyRecord record)
        {
            if (record?.Root == null || record.MemberPartIds == null)
                return;

            Transform root = record.Root.transform;
            Vector3 rootPos = root.localPosition;
            Quaternion rootRot = root.localRotation;
            Vector3 rootScale = SanitizeScale(root.localScale, Vector3.one);

            // Dirty-flag check: skip member update if proxy hasn't moved.
            bool posChanged = rootPos != record.LastPosition;
            bool rotChanged = rootRot != record.LastRotation;
            bool sclChanged = rootScale != record.LastScale;
            if (!record.TransformDirty && !posChanged && !rotChanged && !sclChanged)
                return;

            record.LastPosition = rootPos;
            record.LastRotation = rootRot;
            record.LastScale = rootScale;
            record.TransformDirty = false;

            bool anyPartMissing = false;
            for (int i = 0; i < record.MemberPartIds.Length; i++)
            {
                // Use cached member reference; fall back to lookup if stale.
                GameObject partGo = (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                    ? record.CachedMemberObjects[i]
                    : null;
                if (partGo == null)
                {
                    partGo = _ctx.FindSpawnedPart(record.MemberPartIds[i]);
                    if (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                        record.CachedMemberObjects[i] = partGo;
                }
                if (partGo == null)
                {
                    anyPartMissing = true;
                    continue;
                }

                Vector3 memberLocalPosition = record.MemberLocalPositions[i];
                if (record.ActiveFit != null &&
                    record.ActiveFit.DrivenPartIds != null &&
                    record.ActiveFit.DrivenPartIds.Contains(record.MemberPartIds[i]))
                {
                    float fitOffset = record.CurrentFitTravel - record.ActiveFit.MinTravel;
                    memberLocalPosition += record.ActiveFit.AxisLocal * fitOffset;
                }

                Vector3 localPos = TransformPoint(rootPos, rootRot, rootScale, memberLocalPosition);
                Quaternion localRot = rootRot * record.MemberLocalRotations[i];
                Vector3 localScale = MultiplyScale(rootScale, record.MemberLocalScales[i]);

                partGo.transform.SetLocalPositionAndRotation(localPos, localRot);
                partGo.transform.localScale = localScale;
                partGo.SetActive(true);
            }

            // Use cached bounds instead of per-frame renderer queries.
            if (!anyPartMissing)
                ApplyCachedBoundsToCollider(record, rootPos, rootRot, rootScale);
        }

        private bool TryApplyIntegratedPlacement(ProxyRecord record, string targetId)
        {
            if (record?.Root == null)
                return false;

            // Pass 1: exact (subassemblyId, targetId) lookup.
            IntegratedSubassemblyPreviewPlacement integrated = null;
            if (!string.IsNullOrWhiteSpace(targetId))
                integrated = _ctx.Spawner.FindIntegratedSubassemblyPlacement(record.SubassemblyId, targetId);

            if (integrated != null && integrated.memberPlacements != null && integrated.memberPlacements.Length > 0)
            {
                for (int i = 0; i < integrated.memberPlacements.Length; i++)
                {
                    IntegratedMemberPreviewPlacement memberPlacement = integrated.memberPlacements[i];
                    if (memberPlacement == null || string.IsNullOrWhiteSpace(memberPlacement.partId))
                        continue;

                    GameObject partGo = _ctx.FindSpawnedPart(memberPlacement.partId);
                    if (partGo == null)
                        continue;

                    partGo.transform.SetLocalPositionAndRotation(
                        ToVector3(memberPlacement.position),
                        ToQuaternion(memberPlacement.rotation));
                    partGo.transform.localScale = SanitizeScale(ToVector3(memberPlacement.scale), Vector3.one);
                    partGo.SetActive(true);
                }

                record.CurrentTargetId = targetId;
                record.Root.SetActive(false);
                if (string.Equals(_activeSubassemblyId, record.SubassemblyId, StringComparison.OrdinalIgnoreCase))
                    _activeSubassemblyId = null;
                return true;
            }

            // Pass 2: per-member fallback — searches all integratedSubassemblyPlacements
            // by partId alone, so it works even when targetId is null/mismatched.
            if (record.MemberPartIds == null || record.MemberPartIds.Length == 0)
                return false;

            bool anyMoved = false;
            for (int i = 0; i < record.MemberPartIds.Length; i++)
            {
                string pid = record.MemberPartIds[i];
                if (string.IsNullOrWhiteSpace(pid)) continue;

                IntegratedMemberPreviewPlacement imp = _ctx.Spawner.FindIntegratedMemberPlacement(pid);
                if (imp == null) continue;

                GameObject partGo = (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                    ? record.CachedMemberObjects[i] : null;
                if (partGo == null)
                {
                    partGo = _ctx.FindSpawnedPart(pid);
                    if (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                        record.CachedMemberObjects[i] = partGo;
                }
                if (partGo == null) continue;

                partGo.transform.SetLocalPositionAndRotation(
                    ToVector3(imp.position),
                    ToQuaternion(imp.rotation));
                partGo.transform.localScale = SanitizeScale(ToVector3(imp.scale), Vector3.one);
                partGo.SetActive(true);
                anyMoved = true;
            }

            if (!anyMoved)
                return false;

            if (!string.IsNullOrWhiteSpace(targetId))
                record.CurrentTargetId = targetId;
            record.Root.SetActive(false);
            if (string.Equals(_activeSubassemblyId, record.SubassemblyId, StringComparison.OrdinalIgnoreCase))
                _activeSubassemblyId = null;
            return true;
        }

        private bool TryRestoreConstrainedFitPlacement(ProxyRecord record, string targetId)
        {
            if (record?.Root == null || !TryActivateConstrainedFit(record, targetId))
                return false;

            record.CurrentFitTravel = Mathf.Clamp(record.ActiveFit.CompletionTravel, record.ActiveFit.MinTravel, record.ActiveFit.MaxTravel);
            record.CurrentTargetId = targetId;
            ApplyProxyTransform(record);
            record.Root.SetActive(false);
            if (string.Equals(_activeSubassemblyId, record.SubassemblyId, StringComparison.OrdinalIgnoreCase))
                _activeSubassemblyId = null;
            return true;
        }

        private bool TryCommitConstrainedFitPlacement(ProxyRecord record, string targetId)
        {
            if (record?.Root == null || !TryActivateConstrainedFit(record, targetId))
                return false;

            if (!TryGetTargetLocalPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
                return false;

            record.Root.transform.SetLocalPositionAndRotation(targetPos, targetRot);
            record.Root.transform.localScale = targetScale;
            record.CurrentFitTravel = Mathf.Clamp(record.ActiveFit.CompletionTravel, record.ActiveFit.MinTravel, record.ActiveFit.MaxTravel);
            record.CurrentTargetId = targetId;
            ApplyProxyTransform(record);
            return true;
        }

        private bool TryActivateConstrainedFit(ProxyRecord record, string targetId)
        {
            if (record?.Root == null || string.IsNullOrWhiteSpace(targetId))
                return false;

            ConstrainedSubassemblyFitPreviewPlacement placement = _ctx.Spawner.FindConstrainedSubassemblyFitPlacement(record.SubassemblyId, targetId);
            if (placement == null)
                return false;

            if (!TryGetTargetLocalPose(targetId, out Vector3 targetPos, out Quaternion targetRot, out Vector3 targetScale))
                return false;

            Vector3 axisLocal = ToVector3(placement.fitAxisLocal);
            if (axisLocal.sqrMagnitude < 0.000001f)
                axisLocal = Vector3.right;
            axisLocal.Normalize();

            float minTravel = Mathf.Min(placement.minTravel, placement.maxTravel);
            float maxTravel = Mathf.Max(placement.minTravel, placement.maxTravel);
            float completionTravel = Mathf.Clamp(placement.completionTravel, minTravel, maxTravel);

            record.ActiveFit = new FitRecord
            {
                TargetId = targetId,
                AxisLocal = axisLocal,
                MinTravel = minTravel,
                MaxTravel = maxTravel,
                CompletionTravel = completionTravel,
                SnapTolerance = Mathf.Max(placement.snapTolerance, 0.001f),
                DrivenPartIds = new HashSet<string>(placement.drivenPartIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase)
            };

            record.Root.transform.SetLocalPositionAndRotation(targetPos, targetRot);
            record.Root.transform.localScale = targetScale;
            record.CurrentFitTravel = minTravel;
            record.CurrentTargetId = null;
            return true;
        }

        private static void ClearFitState(ProxyRecord record)
        {
            if (record == null)
                return;

            record.ActiveFit = null;
            record.CurrentFitTravel = 0f;
        }

        /// <summary>
        /// Computes world bounds from member renderers, converts to proxy-local space,
        /// and caches them in the record. Called once at build time and on explicit recalc.
        /// </summary>
        private void RecalculateBoundsAndCollider(ProxyRecord record)
        {
            if (record?.Root == null)
                return;

            Bounds? combined = null;
            for (int i = 0; i < record.MemberPartIds.Length; i++)
            {
                GameObject partGo = (record.CachedMemberObjects != null && i < record.CachedMemberObjects.Length)
                    ? record.CachedMemberObjects[i]
                    : _ctx.FindSpawnedPart(record.MemberPartIds[i]);
                if (!TryGetWorldBounds(partGo, out Bounds bounds))
                    continue;

                combined = combined.HasValue
                    ? Encapsulate(combined.Value, bounds)
                    : bounds;
            }

            if (!combined.HasValue)
            {
                record.HasCachedBounds = false;
                return;
            }

            Bounds worldBounds = combined.Value;
            Vector3 localCenter = record.Root.transform.InverseTransformPoint(worldBounds.center);
            Vector3 localSize = AbsVector(record.Root.transform.InverseTransformVector(worldBounds.size));
            localSize = SanitizeScale(localSize, Vector3.one * 0.01f);

            // Expand for thin panels so the user can grab/select the whole panel.
            localSize = new Vector3(
                Mathf.Max(localSize.x, 0.18f),
                Mathf.Max(localSize.y, 0.18f),
                Mathf.Max(localSize.z, 0.18f));

            record.CachedLocalBounds = new Bounds(localCenter, localSize);
            record.HasCachedBounds = true;

            BoxCollider collider = record.Root.GetComponent<BoxCollider>();
            if (collider == null)
                collider = record.Root.AddComponent<BoxCollider>();
            collider.center = localCenter;
            collider.size = localSize;
        }

        /// <summary>
        /// Applies the cached local-space bounds to the proxy collider without re-querying renderers.
        /// </summary>
        private static void ApplyCachedBoundsToCollider(ProxyRecord record, Vector3 rootPos, Quaternion rootRot, Vector3 rootScale)
        {
            if (record?.Root == null || !record.HasCachedBounds)
                return;

            BoxCollider collider = record.Root.GetComponent<BoxCollider>();
            if (collider == null)
                return;

            collider.center = record.CachedLocalBounds.center;
            collider.size = record.CachedLocalBounds.size;
        }

        private static bool AreAllSubassemblyStepsCompleted(
            SubassemblyDefinition subassembly,
            HashSet<string> completedStepIds,
            MachinePackageDefinition package = null)
        {
            if (subassembly == null)
                return false;

            // Prefer derived steps from step.subassemblyId when package is available
            if (package != null)
            {
                StepDefinition[] derivedSteps = package.GetStepsForSubassembly(subassembly.id);
                if (derivedSteps.Length == 0)
                    return false;

                for (int i = 0; i < derivedSteps.Length; i++)
                {
                    if (!completedStepIds.Contains(derivedSteps[i].id))
                        return false;
                }
                return true;
            }

            // Fallback to subassembly.stepIds
            if (subassembly.stepIds == null || subassembly.stepIds.Length == 0)
                return false;

            for (int i = 0; i < subassembly.stepIds.Length; i++)
            {
                string stepId = subassembly.stepIds[i];
                if (string.IsNullOrWhiteSpace(stepId))
                    continue;

                if (!completedStepIds.Contains(stepId))
                    return false;
            }

            return true;
        }

        private bool TryGetTargetLocalPose(string targetId, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            scale = Vector3.one;

            if (string.IsNullOrWhiteSpace(targetId))
                return false;

            TargetPreviewPlacement targetPlacement = _ctx.Spawner.FindTargetPlacement(targetId);
            if (targetPlacement == null)
                return false;

            position = new Vector3(targetPlacement.position.x, targetPlacement.position.y, targetPlacement.position.z);
            rotation = !targetPlacement.rotation.IsIdentity
                ? new Quaternion(targetPlacement.rotation.x, targetPlacement.rotation.y, targetPlacement.rotation.z, targetPlacement.rotation.w)
                : Quaternion.identity;
            scale = SanitizeScale(new Vector3(targetPlacement.scale.x, targetPlacement.scale.y, targetPlacement.scale.z), Vector3.one);
            return true;
        }

        private GameObject FindMemberPartFromHit(ProxyRecord record, Transform hitTransform)
        {
            Transform previewRoot = _ctx.Setup?.PreviewRoot;
            while (hitTransform != null && hitTransform != previewRoot)
            {
                for (int i = 0; i < record.MemberPartIds.Length; i++)
                {
                    GameObject partGo = _ctx.FindSpawnedPart(record.MemberPartIds[i]);
                    if (partGo != null &&
                        (partGo.transform == hitTransform || hitTransform.IsChildOf(partGo.transform)))
                        return partGo;
                }

                hitTransform = hitTransform.parent;
            }

            return null;
        }

        private static Vector3 TransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 localPoint)
        {
            return origin + rotation * MultiplyScale(scale, localPoint);
        }

        private static Vector3 InverseTransformPoint(Vector3 origin, Quaternion rotation, Vector3 scale, Vector3 point)
        {
            Vector3 translated = Quaternion.Inverse(rotation) * (point - origin);
            return DivideScale(translated, scale);
        }

        private static Vector3 MultiplyScale(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x * right.x, left.y * right.y, left.z * right.z);
        }

        private static Vector3 DivideScale(Vector3 value, Vector3 divisor)
        {
            Vector3 safe = SanitizeScale(divisor, Vector3.one);
            return new Vector3(value.x / safe.x, value.y / safe.y, value.z / safe.z);
        }

        private static Vector3 SanitizeScale(Vector3 value, Vector3 fallback)
        {
            return new Vector3(
                Mathf.Approximately(value.x, 0f) ? fallback.x : value.x,
                Mathf.Approximately(value.y, 0f) ? fallback.y : value.y,
                Mathf.Approximately(value.z, 0f) ? fallback.z : value.z);
        }

        private static Vector3 ToVector3(SceneFloat3 value) => new Vector3(value.x, value.y, value.z);

        private static Quaternion ToQuaternion(SceneQuaternion value)
        {
            return !value.IsIdentity
                ? new Quaternion(value.x, value.y, value.z, value.w)
                : Quaternion.identity;
        }

        private static bool TryGetWorldBounds(GameObject target, out Bounds bounds)
        {
            bounds = default;
            if (target == null)
                return false;

            Renderer[] renderers = MaterialHelper.GetRenderers(target);
            if (renderers != null && renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                return true;
            }

            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders != null && colliders.Length > 0)
            {
                bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    bounds.Encapsulate(colliders[i].bounds);
                return true;
            }

            return false;
        }

        private static Bounds Encapsulate(Bounds left, Bounds right)
        {
            left.Encapsulate(right);
            return left;
        }

        private static Vector3 AbsVector(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }

    internal sealed class SubassemblyPlacementProxy : MonoBehaviour
    {
        public string SubassemblyId;
    }
}
