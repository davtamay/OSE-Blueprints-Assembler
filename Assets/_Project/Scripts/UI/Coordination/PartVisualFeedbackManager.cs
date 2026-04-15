using System;
using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages all part visual feedback: hover highlighting, selection colors,
    /// hint highlight animation, step-based part revelation/hiding, and
    /// step-completion part positioning.
    ///
    /// Extracted from <see cref="PartInteractionBridge"/> to reduce its size.
    /// Receives shared state via constructor; owns visual-only state.
    /// </summary>
    internal sealed class PartVisualFeedbackManager
    {
        // ── Shared references (not owned) ──
        private readonly IBridgeContext _ctx;

        // ── Owned visual state ──
        private GameObject _hoveredPart;
        private GameObject _hintPreview;
        private GameObject _hintSourceProxy;
        private float _hintHighlightUntil;
        private bool _partsHiddenOnSpawn;
        private readonly HashSet<string> _revealedPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activeStepPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _stepHighlightingApplied;

        // ── Shorthand for constants ──
        private static Color SelectedPartColor => InteractionVisualConstants.SelectedPartColor;
        private static Color HoveredPartColor => InteractionVisualConstants.HoveredPartColor;
        private static Color DimmedPartColor => InteractionVisualConstants.DimmedPartColor;
        private static Color ActiveStepEmission => InteractionVisualConstants.ActiveStepEmission;
        private static Color PreviewReadyColor => InteractionVisualConstants.PreviewReadyColor;
        private static Color HintHighlightColorA => InteractionVisualConstants.HintHighlightColorA;
        private static Color HintHighlightColorB => InteractionVisualConstants.HintHighlightColorB;
        private static Color HoveredSubassemblyEmission => InteractionVisualConstants.HoveredSubassemblyEmission;
        private static Color SelectedSubassemblyEmission => InteractionVisualConstants.SelectedSubassemblyEmission;

        public PartVisualFeedbackManager(IBridgeContext context)
        {
            _ctx = context;
        }

        // ════════════════════════════════════════════════════════════════════
        // Public accessors for owned state
        // ════════════════════════════════════════════════════════════════════

        public GameObject HoveredPart { get => _hoveredPart; set => _hoveredPart = value; }
        public HashSet<string> RevealedPartIds => _revealedPartIds;
        public HashSet<string> ActiveStepPartIds => _activeStepPartIds;
        public bool PartsHiddenOnSpawn => _partsHiddenOnSpawn;

        /// <summary>
        /// Resets the one-shot guard so <see cref="HideNonIntroducedParts"/> will
        /// execute again.  Call this before a full visual-state rebuild
        /// (e.g. after async GLB swap or navigation) so that newly-spawned parts
        /// that are not yet revealed get properly hidden.
        /// </summary>
        public void ResetHiddenOnSpawnGuard() => _partsHiddenOnSpawn = false;

        public GameObject HintPreview
        {
            get => _hintPreview;
            set => _hintPreview = value;
        }

        public GameObject HintSourceProxy
        {
            get => _hintSourceProxy;
            set => _hintSourceProxy = value;
        }

        public float HintHighlightUntil
        {
            get => _hintHighlightUntil;
            set => _hintHighlightUntil = value;
        }

        // ════════════════════════════════════════════════════════════════════
        // Hover visuals (called from Update)
        // ════════════════════════════════════════════════════════════════════

        public void UpdatePartHoverVisual()
        {
            if (!Application.isPlaying || _ctx.Spawner == null || _ctx.IsDragging || _ctx.IsToolModeLockedForParts())
            {
                ClearPartHoverVisual();
                return;
            }

            GameObject hoveredPart = _ctx.GetHoveredPartFromXri();
            if (hoveredPart == null)
                hoveredPart = _ctx.GetHoveredPartFromMouse();

            if (hoveredPart == _hoveredPart)
            {
                if (_hoveredPart != null && CanApplyHoverVisual(_hoveredPart, _hoveredPart.name))
                    ApplyHoveredPartVisual(_hoveredPart);
                return;
            }

            ClearPartHoverVisual();

            if (hoveredPart == null || !CanApplyHoverVisual(hoveredPart, hoveredPart.name))
                return;

            _hoveredPart = hoveredPart;
            ApplyHoveredPartVisual(_hoveredPart);
        }

        public void ClearPartHoverVisual()
        {
            if (_hoveredPart == null)
            {
                _hoveredPart = null;
                return;
            }

            RestorePartVisual(_hoveredPart);
            _hoveredPart = null;
        }

        public bool CanApplyHoverVisual(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrEmpty(partId))
                return false;

            var selectionService = _ctx.SelectionService;
            if (selectionService != null && selectionService.CurrentSelection == partGo)
                return false;

            if (_ctx.IsSubassemblyProxy(partGo))
                return true;

            PartPlacementState state = GetPartState(partId);
            return state == PartPlacementState.Available ||
                   state == PartPlacementState.Completed ||
                   state == PartPlacementState.PlacedVirtually;
        }

        public void UpdateSelectedSubassemblyVisual()
        {
            if (!Application.isPlaying)
                return;

            var selectionService = _ctx.SelectionService;
            if (selectionService == null)
                return;

            GameObject selected = _ctx.NormalizeSelectablePlacementTarget(selectionService.CurrentSelection);
            if (!_ctx.IsSubassemblyProxy(selected))
                return;

            ApplySelectedPartVisual(selected);
        }

        public void UpdatePointerDragSelectionVisual()
        {
            var draggedPart = _ctx.Drag?.DraggedPart;
            if (!_ctx.IsDragging || draggedPart == null)
                return;

            ApplySelectedPartVisual(draggedPart);
        }

        // ════════════════════════════════════════════════════════════════════
        // Part visual application (state-based)
        // ════════════════════════════════════════════════════════════════════

        public void ApplyHoveredPartVisual(GameObject partGo)
        {
            if (_ctx.IsSubassemblyProxy(partGo))
            {
                _ctx.ForEachProxyMember(partGo, member =>
                {
                    ApplyHoveredPartVisual(member);
                    MaterialHelper.SetEmission(member, HoveredSubassemblyEmission);
                });
                return;
            }

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, HoveredPartColor);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", HoveredPartColor);
        }

        public void ApplySelectedPartVisual(GameObject partGo)
        {
            if (_ctx.IsSubassemblyProxy(partGo))
            {
                _ctx.ForEachProxyMember(partGo, member =>
                {
                    ApplySelectedPartVisual(member);
                    MaterialHelper.SetEmission(member, SelectedSubassemblyEmission);
                });
                return;
            }

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, SelectedPartColor);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", SelectedPartColor);
        }

        public void RestorePartVisual(GameObject partGo)
        {
            if (partGo == null)
                return;

            if (_ctx.IsSubassemblyProxy(partGo))
            {
                _ctx.ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

            string partId = partGo.name;
            ApplyPartVisualForState(partGo, partId, GetPartState(partId));
        }

        public void ApplyPartVisualForState(GameObject partGo, string partId, PartPlacementState state)
        {
            if (partGo == null)
                return;

            if (_ctx.IsSubassemblyProxy(partGo))
            {
                switch (state)
                {
                    case PartPlacementState.Selected:
                    case PartPlacementState.Inspected:
                    case PartPlacementState.Grabbed:
                        _ctx.ForEachProxyMember(partGo, ApplySelectedPartVisual);
                        break;
                    default:
                        _ctx.ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                        break;
                }

                return;
            }

            switch (state)
            {
                case PartPlacementState.Selected:
                case PartPlacementState.Inspected:
                    ApplySelectedPartVisual(partGo);
                    break;

                case PartPlacementState.Grabbed:
                    ApplySelectedPartVisual(partGo);
                    break;

                case PartPlacementState.PlacedVirtually:
                case PartPlacementState.Completed:
                    MaterialHelper.SetEmission(partGo, Color.black);
                    ClearRendererPropertyBlocks(partGo);
                    if (ServiceRegistry.TryGet<IXRAffordanceSetup>(out var affCompleted)) affCompleted.DisableColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;

                case PartPlacementState.Available:
                default:
                    if (ServiceRegistry.TryGet<IXRAffordanceSetup>(out var affAvailable)) affAvailable.DisableColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;
            }
        }

        public void ApplyAvailablePartVisual(GameObject partGo, string partId)
        {
            if (_ctx.IsSubassemblyProxy(partGo))
            {
                _ctx.ForEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

            // Imported GLB path: hide the outline, restore originals, then
            // re-apply the step's dim tint for parts that are NOT part of
            // the active step (ApplyStepPartHighlighting tints those via
            // DimmedPartColor so the active-step parts stand out). Without
            // this the hover-exit would leave dimmed parts at their full
            // GLB colour and they'd no longer look de-emphasised.
            // Imported GLB: hide the outline + restore the real material.
            // Non-active parts used to get re-tinted with DimmedPartColor
            // here, but the user wants the native GLB material as default,
            // so the restore suffices.
            if (MaterialHelper.IsImportedModel(partGo))
            {
                MaterialHelper.ClearTint(partGo);
                return;
            }

            MaterialHelper.SetEmission(partGo, Color.black);
            ClearRendererPropertyBlocks(partGo);

            // Drop any inverted-hull outline the hover-tint path added.
            MaterialHelper.ClearTint(partGo);

            // Restore original textured materials if available
            if (MaterialHelper.RestoreOriginals(partGo))
                return;

            // Fallback for parts without original textures (primitives/placeholders)
            PartPreviewPlacement placement = _ctx.Spawner != null ? _ctx.Spawner.FindPartPlacement(partId) : null;
            Color baseColor = placement != null
                ? new Color(placement.color.r, placement.color.g, placement.color.b, placement.color.a)
                : new Color(0.94f, 0.55f, 0.18f, 1f);

            MaterialHelper.Apply(partGo, "Preview Part Material", baseColor);
        }

        public void ApplyHintSourceVisual(GameObject partGo, Color color)
        {
            if (partGo == null)
                return;

            if (MaterialHelper.IsImportedModel(partGo))
                MaterialHelper.ApplyTint(partGo, color);
            else
                MaterialHelper.Apply(partGo, "Preview Part Material", color);
        }

        // ════════════════════════════════════════════════════════════════════
        // XRI affordance / renderer property block helpers
        // ════════════════════════════════════════════════════════════════════

        public static void ClearRendererPropertyBlocks(GameObject target)
        {
            if (target == null)
                return;

            Renderer[] renderers = MaterialHelper.GetRenderers(target);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }
        }

        public void SyncPartGrabInteractivity(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrWhiteSpace(partId) || _ctx.IsSubassemblyProxy(partGo))
                return;

            bool shouldEnableGrab = !_ctx.IsPartMovementLocked(partId);

            // Delegate XRI-specific enable/disable to IXRGrabSetup so this class
            // has no direct dependency on XRGrabInteractable (ADR 005).
            //
            // Intentional: NO-TASK parts keep XRGrabInteractable enabled so
            // XRI/mouse selection still fires (yellow outline, inspector
            // panel, etc). Actual drag entry is blocked further up the stack
            // by InteractionOrchestrator.HandleBeginDrag via
            // IPartActionBridge.IsPartTaskAtCurrentStep and by
            // SelectionCoordinator.BeginDragTracking — same pattern as
            // already-placed parts, which stay selectable but not movable.
            if (ServiceRegistry.TryGet<IXRGrabSetup>(out var grabSetup))
                grabSetup.SetGrabEnabled(partGo, shouldEnableGrab);

            if (!shouldEnableGrab && _ctx.Drag?.DraggedPart == partGo)
                _ctx.ResetDragState();
        }

        /// <summary>
        /// True when <paramref name="partId"/> is a task part of the current
        /// step. Uses the <see cref="_activeStepPartIds"/> set populated by
        /// <see cref="ApplyStepPartHighlighting"/> — that already includes
        /// requiredPartIds and requiredSubassembly members. If no step is
        /// active (set is empty) we don't restrict grab. NO-TASK visualPartIds
        /// entries are never added to the set, so they're not grabbable.
        /// </summary>
        public bool IsPartGrabbableAtCurrentStep(string partId)
        {
            // If a step has been applied (even one with empty task parts —
            // e.g. a Confirm-only step with only visualPartIds), restrict to
            // that set. Empty-and-highlighted means "no part is a task here"
            // — nothing should be grabbable. Only return permissive-true when
            // no highlighting has run at all (pre-step-activation bootstrap).
            if (!_stepHighlightingApplied) return true;
            return _activeStepPartIds != null && _activeStepPartIds.Contains(partId);
        }

        // ════════════════════════════════════════════════════════════════════
        // Hint highlight animation
        // ════════════════════════════════════════════════════════════════════

        public void UpdateHintHighlight()
        {
            if ((_hintPreview == null && _hintSourceProxy == null) || _hintHighlightUntil <= 0f)
                return;

            if (Time.time >= _hintHighlightUntil)
            {
                ClearHintHighlight();
                return;
            }

            Color pulseColor = ColorPulseHelper.Lerp(HintHighlightColorA, HintHighlightColorB,
                InteractionVisualConstants.HintHighlightPulseSpeed);

            var placeHandler = _ctx.PlaceHandler;
            if (_hintPreview != null)
            {
                if (!(placeHandler != null && placeHandler.IsPreviewHighlighted && placeHandler.HoveredPreview == _hintPreview))
                    MaterialHelper.SetMaterialColor(_hintPreview, pulseColor);
            }

            if (_hintSourceProxy != null)
                _ctx.ForEachProxyMember(_hintSourceProxy, member => ApplyHintSourceVisual(member, pulseColor));
        }

        public void ClearHintHighlight()
        {
            var placeHandler = _ctx.PlaceHandler;
            if (_hintPreview != null)
            {
                if (placeHandler != null && placeHandler.IsPreviewHighlighted && placeHandler.HoveredPreview == _hintPreview)
                {
                    MaterialHelper.Apply(_hintPreview, "Preview Ready Material", PreviewReadyColor);
                }
                else
                {
                    MaterialHelper.ApplyPreviewMaterial(_hintPreview);
                }
            }

            if (_hintSourceProxy != null)
                RestorePartVisual(_hintSourceProxy);

            _hintPreview = null;
            _hintSourceProxy = null;
            _hintHighlightUntil = 0f;
        }

        // ════════════════════════════════════════════════════════════════════
        // Part revelation / hiding (step-based visibility)
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// True when <paramref name="partId"/> is explicitly acted on by
        /// <paramref name="step"/> — either via requiredPartIds/optionalPartIds
        /// or via a target whose associatedPartId matches. Task parts need to
        /// render at startPosition during the step so the trainee can pick
        /// them up and drag to the ghost.
        /// </summary>
        private bool IsPartTaskOfStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrEmpty(partId)) return false;
            if (step.requiredPartIds != null)
                foreach (var p in step.requiredPartIds)
                    if (string.Equals(p, partId, StringComparison.Ordinal)) return true;
            if (step.optionalPartIds != null)
                foreach (var p in step.optionalPartIds)
                    if (string.Equals(p, partId, StringComparison.Ordinal)) return true;
            var pkg = _ctx.Spawner?.CurrentPackage;
            if (pkg != null && step.targetIds != null)
            {
                foreach (var tid in step.targetIds)
                {
                    if (string.IsNullOrWhiteSpace(tid) || !pkg.TryGetTarget(tid, out var tgt) || tgt == null) continue;
                    if (string.Equals(tgt.associatedPartId, partId, StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        public void HideNonIntroducedParts()
        {
            if (_partsHiddenOnSpawn) return;

            var parts = _ctx.Spawner?.SpawnedParts;
            if (parts == null || parts.Count == 0)
            {
                // Async GLB load not done yet. Leave the one-shot guard open
                // so the next call (typically from SpawnerPartsReady →
                // HandlePartsReady → reveal) actually hides the parts. If we
                // set the guard here with an empty list, the first play-press
                // after reload ends up with every part active until a step
                // change flushes it.
                return;
            }
            _partsHiddenOnSpawn = true;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null) continue;

                string partId = parts[i].name;

                // Keep completed/placed parts visible
                if (_ctx.PartStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                    continue;

                // Keep already-revealed parts visible
                if (_revealedPartIds.Contains(partId))
                    continue;

                parts[i].SetActive(false);
            }

            OseLog.Info($"[PartInteraction] Hid non-introduced parts for hybrid presentation.");
        }

        public void RevealStepParts(string stepId)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            int currentSeq    = step.sequenceIndex;
            string subassemblyId = step.subassemblyId;

            // Reveal set = every part whose baked poseTable entry is
            // non-Hidden at currentSeq. This is the *same* source the TTAW
            // editor uses when scrubbing — if the author can see a part at
            // step N in the editor, the trainee sees it at step N in play.
            // Cumulative by construction: a part placed at step 50 stays
            // visible at 51, 52, ... because the normalizer bakes Assembled
            // entries forward. No more per-step field walking here — that
            // logic caused editor/play divergence (step 50 carriages
            // disappearing at step 51).
            //
            // Target-associated parts and step-scoped task parts are
            // guaranteed to be in the visible set: PoseResolverIndex seeds
            // firstVisibleSeqByPart from requiredPartIds / optionalPartIds /
            // visualPartIds / requiredSubassemblyId members, and the
            // "loose-placement" fallback pins anything with a placement to
            // the first step's seq.
            var poseTable = package.poseTable;
            var subassemblyPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (poseTable != null)
            {
                foreach (var pid in poseTable.EnumerateVisiblePartsAt(currentSeq))
                    if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
            }
            else
            {
                // Legacy fallback: package loaded without Normalize (no
                // poseTable baked). Preserve the original step-scoped reveal
                // so this code path still works.
                if (step.requiredPartIds != null)
                    foreach (var pid in step.requiredPartIds)
                        if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
                if (step.optionalPartIds != null)
                    foreach (var pid in step.optionalPartIds)
                        if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
                if (step.visualPartIds != null)
                    foreach (var pid in step.visualPartIds)
                        if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
                if (!string.IsNullOrEmpty(step.requiredSubassemblyId)
                    && package.TryGetSubassembly(step.requiredSubassemblyId, out var stepSubDef)
                    && stepSubDef?.partIds != null)
                {
                    foreach (var pid in stepSubDef.partIds)
                        if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
                }
                if (step.targetIds != null)
                {
                    foreach (var tid in step.targetIds)
                    {
                        if (string.IsNullOrWhiteSpace(tid) || !package.TryGetTarget(tid, out var tgt) || tgt == null)
                            continue;
                        if (!string.IsNullOrWhiteSpace(tgt.associatedPartId))
                            subassemblyPartIds.Add(tgt.associatedPartId);
                        if (!string.IsNullOrWhiteSpace(tgt.associatedSubassemblyId)
                            && package.TryGetSubassembly(tgt.associatedSubassemblyId, out var subDef)
                            && subDef?.partIds != null)
                        {
                            foreach (var pid in subDef.partIds)
                                if (!string.IsNullOrWhiteSpace(pid)) subassemblyPartIds.Add(pid);
                        }
                    }
                }
            }

            if (subassemblyPartIds.Count == 0)
                return;

            // Hierarchy sync FIRST — creates/updates Group_* roots (at
            // identity) and reparents visible members. Doing this before
            // positioning means SetLocalPositionAndRotation below sees roots
            // at identity, so localPos == the authored PreviewRoot-space
            // pose. If we positioned first and reparented afterward, the
            // reparent would leave parts at stale world positions.
            _ctx.Spawner?.SyncSubassemblyHierarchy(package, step);

            // Filter to parts not yet revealed
            var toReveal = new List<string>();
            foreach (string partId in subassemblyPartIds)
            {
                if (_ctx.PartStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                {
                    _revealedPartIds.Add(partId);
                    continue;
                }

                if (!_revealedPartIds.Contains(partId))
                    toReveal.Add(partId);
            }

            if (toReveal.Count == 0)
                return;

            // Activate, position, and style each newly-revealed part.
            var unplacedParts = new List<(string partId, GameObject go, float width)>();

            for (int i = 0; i < toReveal.Count; i++)
            {
                string partId = toReveal[i];
                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo == null) continue;

                PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(partId);

                // Source of truth: the baked pose-resolver entry at this seq.
                // Matches exactly what TTAW renders — past-task parts at
                // assembledPosition, current-task parts at startPosition, etc.
                // Fall back to placement.startPosition only if poseTable is
                // absent (e.g. package loaded without Normalize).
                bool hasAuthored = false;
                Vector3 pos = Vector3.zero;
                Quaternion rot = Quaternion.identity;
                Vector3 scale = Vector3.one;

                // When this step's *Place task* acts on the part and the trainee
                // hasn't yet completed it, show startPosition — they need to
                // pick it up and drag to the ghost. The poseTable was baked
                // with PoseMode.Committed which returns assembledPosition for
                // task-step parts (correct for post-completion), so we can't
                // use its resolution here. Non-Place families (Confirm,
                // Use, Connect) leave the part at its committed pose: a
                // Confirm shake-test shouldn't teleport the part back to
                // startPosition just because it's in requiredPartIds.
                bool isPlaceTaskHere = step.ResolvedFamily == OSE.Content.StepFamily.Place
                                       && IsPartTaskOfStep(step, partId);
                bool notYetPlaced = !(_ctx.PartStates.TryGetValue(partId, out var s0)
                                      && s0 is PartPlacementState.Completed or PartPlacementState.PlacedVirtually);

                if (isPlaceTaskHere && notYetPlaced && pp != null)
                {
                    pos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    rot = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    hasAuthored = !Mathf.Approximately(pos.x, 0f)
                                  || !Mathf.Approximately(pos.y, 0f)
                                  || !Mathf.Approximately(pos.z, 0f);
                }
                else if (poseTable != null && poseTable.TryGet(partId, currentSeq, out var resolution))
                {
                    pos = resolution.pos;
                    rot = resolution.rot;
                    scale = resolution.scl;
                    hasAuthored = true;
                }
                else if (pp != null)
                {
                    pos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    rot = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
                    scale = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    hasAuthored = !Mathf.Approximately(pos.x, 0f)
                                  || !Mathf.Approximately(pos.y, 0f)
                                  || !Mathf.Approximately(pos.z, 0f);
                }

                if (scale.sqrMagnitude < 0.00001f) scale = Vector3.one;
                partGo.transform.localScale = scale;
                partGo.SetActive(true);

                if (hasAuthored)
                {
                    partGo.transform.SetLocalPositionAndRotation(pos, rot);
                }
                else
                {
                    partGo.transform.localRotation = Quaternion.identity;

                    // Measure extents for fallback row layout
                    float width = InteractionVisualConstants.PartGridSpacing;
                    var renderers = MaterialHelper.GetRenderers(partGo);
                    if (renderers.Length > 0)
                    {
                        Bounds combined = renderers[0].bounds;
                        for (int r = 1; r < renderers.Length; r++)
                            combined.Encapsulate(renderers[r].bounds);
                        width = Mathf.Max(combined.size.x, combined.size.z, InteractionVisualConstants.PartGridSpacing);
                    }
                    unplacedParts.Add((partId, partGo, width));
                }

                _ctx.PartStates[partId] = PartPlacementState.Available;
                SyncPartGrabInteractivity(partGo, partId);
                ApplyPartVisualForState(partGo, partId, PartPlacementState.Available);
                _revealedPartIds.Add(partId);
            }

            // Fallback row layout for parts without authored start positions.
            if (unplacedParts.Count > 0)
            {
                float padding = 0.15f;
                float totalWidth = 0f;
                for (int i = 0; i < unplacedParts.Count; i++)
                    totalWidth += unplacedParts[i].width + (i > 0 ? padding : 0f);

                float cursor = -totalWidth * 0.5f;
                for (int i = 0; i < unplacedParts.Count; i++)
                {
                    var (_, partGo, width) = unplacedParts[i];
                    float x = cursor + width * 0.5f;
                    cursor += width + padding;
                    partGo.transform.localPosition = new Vector3(x, InteractionVisualConstants.PartLayoutY, InteractionVisualConstants.PartGridStartZ);
                }
            }

            // Second hierarchy sync — catches any members that became active
            // via the reveal loop but weren't at the first pass (spawner
            // hadn't registered them yet). No-op for already-reparented ones.
            _ctx.Spawner?.SyncSubassemblyHierarchy(package, step);

            // Deactivation pass: any spawned part NOT in the visible set at
            // currentSeq must be hidden, unless it's Completed/PlacedVirtually
            // (those are authoritative state owners and must stay visible
            // until explicit reversion). This keeps play in sync with the
            // editor when a part's lastVisibleSeq is exceeded, and when
            // jumping to an earlier step where future parts shouldn't show.
            var spawnedParts = _ctx.Spawner?.SpawnedParts;
            if (spawnedParts != null)
            {
                for (int i = 0; i < spawnedParts.Count; i++)
                {
                    var go = spawnedParts[i];
                    if (go == null) continue;
                    string pid = go.name;
                    if (subassemblyPartIds.Contains(pid)) continue;
                    if (_ctx.PartStates.TryGetValue(pid, out var st)
                        && st is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                        continue;
                    if (go.activeSelf) go.SetActive(false);
                    _revealedPartIds.Remove(pid);
                }
            }

            OseLog.Info($"[PartInteraction] Revealed {toReveal.Count} part(s) for subassembly '{subassemblyId}'.");
        }

        public void ApplyStepPartHighlighting(string stepId)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            _activeStepPartIds.Clear();
            _stepHighlightingApplied = true;
            if (step.RequiresSubassemblyPlacement &&
                package.TryGetSubassembly(step.requiredSubassemblyId, out var requiredSubassembly) &&
                requiredSubassembly?.partIds != null)
            {
                for (int i = 0; i < requiredSubassembly.partIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(requiredSubassembly.partIds[i]))
                        _activeStepPartIds.Add(requiredSubassembly.partIds[i]);
                }
            }
            else
            {
                string[] effectiveParts = step.GetEffectiveRequiredPartIds();
                for (int i = 0; i < effectiveParts.Length; i++)
                {
                    if (!string.IsNullOrEmpty(effectiveParts[i]))
                        _activeStepPartIds.Add(effectiveParts[i]);
                }
            }

            // Walk all revealed parts: highlight active via emission glow,
            // leave the rest at their native GLB material. Previous behaviour
            // replaced non-active parts' materials with a dimmed tint
            // (DimmedPartColor via ApplyTint → "OSE_Tint" instance) so active
            // parts stood out, but the user wants the native material
            // (carriage_half_material, etc.) as the default — matching the
            // editor authoring view which doesn't dim. Active-step parts
            // still get the emission glow so they remain obvious.
            foreach (string partId in _revealedPartIds)
            {
                if (_ctx.PartStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                    continue;

                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo == null) continue;

                if (_activeStepPartIds.Contains(partId))
                {
                    ApplyAvailablePartVisual(partGo, partId);
                    MaterialHelper.SetEmission(partGo, ActiveStepEmission);
                }
                else
                {
                    ClearRendererPropertyBlocks(partGo);
                    MaterialHelper.ClearTint(partGo);
                    if (MaterialHelper.IsImportedModel(partGo))
                        MaterialHelper.RestoreOriginals(partGo);
                }
                // Refresh grab state now that _activeStepPartIds is final —
                // catches NO-TASK parts that were revealed before the
                // active-set was populated (SyncPartGrabInteractivity at
                // reveal time would have seen an empty set and left grab on).
                SyncPartGrabInteractivity(partGo, partId);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Step completion: move parts to assembled position
        // ════════════════════════════════════════════════════════════════════

        public void MoveStepPartsToPlayPosition(string stepId)
        {
            var package = _ctx.Spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Use the merged set so Use-family steps (e.g. drill-tighten) can
            // move the parts their tool actions operate on to the step's pose.
            string[] partIds = step.GetAllTouchedPartIds();
            if (partIds == null || partIds.Length == 0) return;

            foreach (string partId in partIds)
            {
                MovePartToStepPose(partId, stepId);

                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo != null)
                {
                    partGo.SetActive(true);
                    _ctx.PartStates[partId] = PartPlacementState.Completed;
                    SyncPartGrabInteractivity(partGo, partId);
                    ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                    _revealedPartIds.Add(partId);
                }
            }
        }

        public void RestoreCompletedStepParts(StepDefinition[] steps)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || steps == null) return;

            // Parts are intentionally touched by multiple steps in sequence
            // (place → snug → torque → final tighten), and stepPoses are
            // applied in step order so the last write lands the part at its
            // latest authored pose. That's the designed behavior, not a bug.
            // The real ownership invariant — "only one Place step claims
            // first-placement of a partId" — is enforced by the validator's
            // PartOwnershipExclusivityPass at load/validation time.

            for (int s = 0; s < steps.Length; s++)
            {
                var step = steps[s];
                // Merged set: includes derived tool-action parts so Use-family
                // steps restore the parts they touched on navigation.
                string[] partIds = step.GetAllTouchedPartIds();
                if (partIds != null && partIds.Length > 0)
                {
                    for (int p = 0; p < partIds.Length; p++)
                    {
                        string partId = partIds[p];
                        if (string.IsNullOrEmpty(partId)) continue;

                        MovePartToStepPose(partId, step.id);

                        GameObject partGo = _ctx.FindSpawnedPart(partId);
                        if (partGo != null) partGo.SetActive(true);

                        _ctx.PartStates[partId] = PartPlacementState.Completed;
                        SyncPartGrabInteractivity(partGo, partId);
                        ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                        _revealedPartIds.Add(partId);
                    }
                }

                // Also restore NO-TASK introductions: visualPartIds entries
                // persist as scene context once introduced. Without this, a
                // Confirm-only step (e.g. step 50's "lay out all 8 carriages")
                // that puts every part in visualPartIds would let them vanish
                // on the next step — editor shows them, play hides them.
                if (step.visualPartIds != null)
                {
                    for (int p = 0; p < step.visualPartIds.Length; p++)
                    {
                        string partId = step.visualPartIds[p];
                        if (string.IsNullOrEmpty(partId)) continue;
                        // Skip if already handled above (part was also
                        // required) — preserves Completed state set above.
                        if (_revealedPartIds.Contains(partId)) continue;

                        GameObject partGo = _ctx.FindSpawnedPart(partId);
                        if (partGo != null) partGo.SetActive(true);

                        if (!_ctx.PartStates.ContainsKey(partId))
                            _ctx.PartStates[partId] = PartPlacementState.Available;
                        SyncPartGrabInteractivity(partGo, partId);
                        ApplyPartVisualForState(partGo, partId, _ctx.PartStates[partId]);
                        _revealedPartIds.Add(partId);
                    }
                }
            }
        }

        public void MovePartToPlayPosition(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return;

            PartPreviewPlacement pp = _ctx.Spawner.FindPartPlacement(partId);
            if (pp == null) return;

            GameObject partGo = _ctx.FindSpawnedPart(partId);
            if (partGo == null) return;

            Vector3    pPos   = new Vector3(pp.assembledPosition.x, pp.assembledPosition.y, pp.assembledPosition.z);
            Vector3    pScale = new Vector3(pp.assembledScale.x, pp.assembledScale.y, pp.assembledScale.z);
            Quaternion pRot   = !pp.assembledRotation.IsIdentity
                ? new Quaternion(pp.assembledRotation.x, pp.assembledRotation.y, pp.assembledRotation.z, pp.assembledRotation.w)
                : Quaternion.identity;

            partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
            partGo.transform.localScale = pScale;
        }

        /// <summary>
        /// Moves a part to its step-scoped pose after the given step completes.
        /// Falls back to assembledPosition when no stepPose exists.
        /// </summary>
        private void MovePartToStepPose(string partId, string completedStepId)
        {
            if (string.IsNullOrEmpty(partId)) return;

            StepPoseEntry stepPose = _ctx.Spawner.FindPartStepPose(partId, completedStepId);
            if (stepPose != null)
            {
                GameObject partGo = _ctx.FindSpawnedPart(partId);
                if (partGo == null) return;

                Vector3 pos = new Vector3(stepPose.position.x, stepPose.position.y, stepPose.position.z);
                Quaternion rot = !stepPose.rotation.IsIdentity
                    ? new Quaternion(stepPose.rotation.x, stepPose.rotation.y, stepPose.rotation.z, stepPose.rotation.w)
                    : Quaternion.identity;
                Vector3 scl = new Vector3(stepPose.scale.x, stepPose.scale.y, stepPose.scale.z);

                partGo.transform.SetLocalPositionAndRotation(pos, rot);
                partGo.transform.localScale = scl;
                return;
            }

            MovePartToPlayPosition(partId);
        }

        /// <summary>
        /// Moves every spawned part to its play position and marks it Completed.
        /// Used when navigating to the final "assembly complete" step so the user
        /// sees the fully assembled machine regardless of per-step part references.
        /// </summary>
        public void ShowAllPartsAssembled()
        {
            var parts = _ctx.Spawner?.SpawnedParts;
            if (parts == null) return;

            for (int i = 0; i < parts.Count; i++)
            {
                var partGo = parts[i];
                if (partGo == null) continue;

                string partId = partGo.name;
                MovePartToIntegratedOrPlayPosition(partId, partGo);
                partGo.SetActive(true);
                _ctx.PartStates[partId] = PartPlacementState.Completed;
                SyncPartGrabInteractivity(partGo, partId);
                ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                _revealedPartIds.Add(partId);
            }

            OseLog.Info($"[ShowAllPartsAssembled] Placed {parts.Count} parts at assembled positions.");
        }

        private void MovePartToIntegratedOrPlayPosition(string partId, GameObject partGo)
        {
            // In the fully-assembled view, prefer integrated member placement
            // (canonical cube pose) so subassembly members appear at their stacked
            // positions rather than their fabrication-station assembledPositions.
            IntegratedMemberPreviewPlacement imp = _ctx.Spawner?.FindIntegratedMemberPlacement(partId);
            if (imp != null)
            {
                Vector3 iPos = new Vector3(imp.position.x, imp.position.y, imp.position.z);
                Quaternion iRot = !imp.rotation.IsIdentity
                    ? new Quaternion(imp.rotation.x, imp.rotation.y, imp.rotation.z, imp.rotation.w)
                    : Quaternion.identity;
                Vector3 iScl = new Vector3(imp.scale.x, imp.scale.y, imp.scale.z);
                partGo.transform.SetLocalPositionAndRotation(iPos, iRot);
                partGo.transform.localScale = iScl;
                return;
            }

            MovePartToPlayPosition(partId);
        }

        public void RevertFutureStepParts(StepDefinition[] allSteps, int fromStepIndex)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || allSteps == null) return;

            for (int s = fromStepIndex; s < allSteps.Length; s++)
            {
                string[] partIds = allSteps[s].GetEffectiveRequiredPartIds();
                if (partIds == null || partIds.Length == 0) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    GameObject partGo = _ctx.FindSpawnedPart(partId);
                    if (partGo == null) continue;

                    // Hide future parts instead of repositioning — they'll be revealed
                    // when their step activates via RevealStepParts.
                    partGo.SetActive(false);
                    _revealedPartIds.Remove(partId);
                    _ctx.PartStates[partId] = PartPlacementState.NotIntroduced;
                }
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Cleanup
        // ════════════════════════════════════════════════════════════════════

        public void Clear()
        {
            ClearPartHoverVisual();
            ClearHintHighlight();
            _revealedPartIds.Clear();
            _activeStepPartIds.Clear();
            _partsHiddenOnSpawn = false;
        }

        // ════════════════════════════════════════════════════════════════════
        // Private helpers
        // ════════════════════════════════════════════════════════════════════

        private PartPlacementState GetPartState(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return PartPlacementState.Available;

            return _ctx.PartStates.TryGetValue(partId, out PartPlacementState state)
                ? state
                : PartPlacementState.Available;
        }
    }
}
