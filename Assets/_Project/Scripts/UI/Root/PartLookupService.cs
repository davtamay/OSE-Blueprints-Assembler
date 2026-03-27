using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Pure-query service for part resolution, raycasting, proxy lookup,
    /// and movement-lock checks. Extracted from PartInteractionBridge to
    /// reduce its method count and isolate lookup concerns.
    /// </summary>
    internal sealed class PartLookupService
    {
        private readonly Func<PackagePartSpawner> _spawner;
        private readonly Func<PreviewSceneSetup> _setup;
        private readonly Func<SubassemblyPlacementController> _subassembly;
        private readonly Dictionary<string, PartPlacementState> _partStates;

        internal PartLookupService(
            Func<PackagePartSpawner> spawner,
            Func<PreviewSceneSetup> setup,
            Func<SubassemblyPlacementController> subassembly,
            Dictionary<string, PartPlacementState> partStates)
        {
            _spawner = spawner;
            _setup = setup;
            _subassembly = subassembly;
            _partStates = partStates;
        }

        // ── Spawned-part queries ──

        public GameObject FindSpawnedPart(string partId)
        {
            var parts = _spawner()?.SpawnedParts;
            if (parts == null) return null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].name == partId)
                    return parts[i];
            }
            return null;
        }

        public bool IsSpawnedPart(GameObject target)
        {
            if (target == null) return false;
            var parts = _spawner()?.SpawnedParts;
            if (parts == null) return false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == target)
                    return true;
            }
            return false;
        }

        public bool IsSubassemblyProxy(GameObject target) =>
            _subassembly() != null && _subassembly().IsProxy(target);

        public bool IsSelectablePlacementObject(GameObject target) =>
            IsSpawnedPart(target) || IsSubassemblyProxy(target);

        // ── Selection ID resolution ──

        public string ResolveSelectionId(GameObject target)
        {
            if (target == null) return null;
            var sub = _subassembly();
            if (sub != null &&
                sub.TryGetSubassemblyId(target, out string subassemblyId) &&
                IsSubassemblyProxy(target))
            {
                return subassemblyId;
            }
            return IsSpawnedPart(target) ? target.name : null;
        }

        public GameObject NormalizeSelectablePlacementTarget(GameObject target)
        {
            if (target == null) return target;
            var sub = _subassembly();
            if (sub == null) return target;
            GameObject proxyTarget = sub.ResolveSelectableFromHit(target.transform);
            return proxyTarget != null ? proxyTarget : target;
        }

        public void ForEachProxyMember(GameObject proxy, Action<GameObject> visitor)
        {
            if (proxy == null || visitor == null) return;
            var sub = _subassembly();
            if (sub == null) return;
            foreach (GameObject member in sub.EnumerateMemberParts(proxy))
            {
                if (member != null) visitor(member);
            }
        }

        // ── Raycast ──

        public GameObject RaycastPartAtScreen(Vector2 screenPos)
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null) return null;
            return RaycastSelectableObject(cam.ScreenPointToRay(screenPos));
        }

        public GameObject RaycastSelectableObject(Ray ray)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return null;
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            var sub = _subassembly();
            for (int i = 0; i < hits.Length; i++)
            {
                GameObject matchedProxy = sub?.ResolveSelectableFromHit(hits[i].transform);
                if (matchedProxy != null) return matchedProxy;
                GameObject matchedPart = FindPartFromHit(hits[i].transform);
                if (matchedPart != null) return matchedPart;
            }
            return null;
        }

        private GameObject FindPartFromHit(Transform hitTransform)
        {
            Transform previewRoot = _setup()?.PreviewRoot;
            while (hitTransform != null && hitTransform != previewRoot)
            {
                var parts = _spawner()?.SpawnedParts;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] != null &&
                            (parts[i].transform == hitTransform || hitTransform.IsChildOf(parts[i].transform)))
                            return parts[i];
                    }
                }
                hitTransform = hitTransform.parent;
            }
            return null;
        }

        // ── Movement lock ──

        public bool IsPartMovementLocked(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId)) return false;

            ServiceRegistry.TryGet<PartRuntimeController>(out var partController);
            var sub = _subassembly();

            if (sub != null)
            {
                var package = _spawner()?.CurrentPackage;
                GameObject partGo = FindSpawnedPart(partId);
                bool isSubassemblyId = partGo == null && sub.TryGetProxy(partId, out _);
                if (!isSubassemblyId)
                    isSubassemblyId = package != null && package.TryGetSubassembly(partId, out _);

                if (isSubassemblyId)
                {
                    if (ServiceRegistry.TryGet<MachineSessionController>(out var session))
                    {
                        var stepCtrl = session.AssemblyController?.StepController;
                        if (stepCtrl != null && stepCtrl.HasActiveStep)
                        {
                            var currentStep = stepCtrl.CurrentStepDefinition;
                            if (currentStep != null && currentStep.RequiresSubassemblyPlacement &&
                                string.Equals(currentStep.requiredSubassemblyId, partId, StringComparison.OrdinalIgnoreCase))
                                return false;
                        }
                    }

                    bool anyMemberFound = false;
                    bool anyMemberLocked = false;
                    if (package != null && package.TryGetSubassembly(partId, out var subDef) && subDef?.partIds != null)
                    {
                        foreach (string memberId in subDef.partIds)
                        {
                            if (string.IsNullOrWhiteSpace(memberId)) continue;
                            anyMemberFound = true;
                            bool memberLocked = partController != null
                                ? partController.IsPartLockedForMovement(memberId)
                                : IsPartStateLockedLocally(memberId);
                            if (memberLocked) { anyMemberLocked = true; break; }
                        }
                    }
                    if (anyMemberLocked) return true;
                    if (anyMemberFound) return false;
                }
            }

            if (partController != null)
                return partController.IsPartLockedForMovement(partId);

            return IsPartStateLockedLocally(partId);
        }

        public bool IsPartStateLockedLocally(string partId)
        {
            PartPlacementState localState = GetPartState(partId);
            return localState == PartPlacementState.PlacedVirtually ||
                   localState == PartPlacementState.Completed;
        }

        public PartPlacementState GetPartState(string partId)
        {
            if (string.IsNullOrWhiteSpace(partId)) return PartPlacementState.Available;
            return _partStates.TryGetValue(partId, out var state) ? state : PartPlacementState.Available;
        }
    }
}
