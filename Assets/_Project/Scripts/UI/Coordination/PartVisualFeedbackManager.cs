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

            MaterialHelper.SetEmission(partGo, Color.black);
            ClearRendererPropertyBlocks(partGo);

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
            if (ServiceRegistry.TryGet<IXRGrabSetup>(out var grabSetup))
                grabSetup.SetGrabEnabled(partGo, shouldEnableGrab);

            if (!shouldEnableGrab && _ctx.Drag?.DraggedPart == partGo)
                _ctx.ResetDragState();
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

        public void HideNonIntroducedParts()
        {
            if (_partsHiddenOnSpawn) return;
            _partsHiddenOnSpawn = true;

            var parts = _ctx.Spawner?.SpawnedParts;
            if (parts == null) return;

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

            // Determine which subassembly we're in
            string subassemblyId = step.subassemblyId;

            // Collect part ids from steps in this subassembly up to and including
            // the current step. Parts from future steps stay hidden until their
            // step activates — prevents e.g. brackets appearing during panel placement.
            int currentSeq = step.sequenceIndex;
            var subassemblyPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(subassemblyId))
            {
                StepDefinition[] allSteps = package.GetOrderedSteps();
                for (int s = 0; s < allSteps.Length; s++)
                {
                    if (allSteps[s].sequenceIndex > currentSeq)
                        continue;
                    if (!string.Equals(allSteps[s].subassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string[] rp = allSteps[s].GetEffectiveRequiredPartIds();
                    if (rp == null) continue;
                    for (int p = 0; p < rp.Length; p++)
                    {
                        if (!string.IsNullOrWhiteSpace(rp[p]))
                            subassemblyPartIds.Add(rp[p]);
                    }
                }
            }
            else
            {
                // No subassembly — fall back to just this step's parts
                string[] rp = step.GetEffectiveRequiredPartIds();
                if (rp != null)
                {
                    for (int p = 0; p < rp.Length; p++)
                    {
                        if (!string.IsNullOrWhiteSpace(rp[p]))
                            subassemblyPartIds.Add(rp[p]);
                    }
                }
            }

            if (subassemblyPartIds.Count == 0)
                return;

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
                Vector3 scale = pp != null
                    ? new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z)
                    : Vector3.one;

                partGo.transform.localScale = scale;
                partGo.SetActive(true);

                bool hasAuthored = pp != null &&
                    (!Mathf.Approximately(pp.startPosition.x, 0f) ||
                     !Mathf.Approximately(pp.startPosition.y, 0f) ||
                     !Mathf.Approximately(pp.startPosition.z, 0f));

                if (hasAuthored)
                {
                    Vector3 pos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    Quaternion rot = !pp.startRotation.IsIdentity
                        ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                        : Quaternion.identity;
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

            OseLog.Info($"[PartInteraction] Revealed {toReveal.Count} part(s) for subassembly '{subassemblyId}'.");
        }

        public void ApplyStepPartHighlighting(string stepId)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            _activeStepPartIds.Clear();
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

            // Walk all revealed parts: highlight active, dim the rest
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
                    if (MaterialHelper.IsImportedModel(partGo))
                        MaterialHelper.ApplyTint(partGo, DimmedPartColor);
                    else
                        MaterialHelper.Apply(partGo, "Preview Part Material", DimmedPartColor);
                    MaterialHelper.SetEmission(partGo, Color.black);
                }
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

            string[] partIds = step.GetEffectiveRequiredPartIds();
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

            for (int s = 0; s < steps.Length; s++)
            {
                var step = steps[s];
                string[] partIds = step.GetEffectiveRequiredPartIds();
                if (partIds == null || partIds.Length == 0) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    MovePartToStepPose(partId, step.id);

                    GameObject partGo = _ctx.FindSpawnedPart(partId);
                    if (partGo != null)
                    {
                        // Ensure the part is visible — HideNonIntroducedParts may
                        // have hidden it before the restore path ran.
                        partGo.SetActive(true);
                    }

                    _ctx.PartStates[partId] = PartPlacementState.Completed;
                    SyncPartGrabInteractivity(partGo, partId);
                    ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                    _revealedPartIds.Add(partId);
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
