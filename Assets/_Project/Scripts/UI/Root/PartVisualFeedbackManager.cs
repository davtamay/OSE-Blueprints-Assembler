using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
        private readonly PackagePartSpawner _spawner;
        private readonly Dictionary<string, PartPlacementState> _partStates;
        private readonly Func<string, GameObject> _findSpawnedPart;
        private readonly Func<GameObject, bool> _isSubassemblyProxy;
        private readonly Func<GameObject, Action<GameObject>, bool> _forEachProxyMember;
        private readonly Func<SelectionService> _getSelectionService;
        private readonly Func<bool> _isDragging;
        private readonly Func<bool> _isToolModeLockedForParts;
        private readonly Func<GameObject> _getHoveredPartFromXri;
        private readonly Func<GameObject> _getHoveredPartFromMouse;
        private readonly Func<PlaceStepHandler> _getPlaceHandler;
        private readonly Func<GameObject> _getDraggedPart;
        private readonly Func<string, bool> _isPartMovementLocked;
        private readonly Func<GameObject, GameObject> _normalizeSelectableTarget;
        private readonly Action _resetDragState;

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

        public PartVisualFeedbackManager(
            PackagePartSpawner spawner,
            Dictionary<string, PartPlacementState> partStates,
            Func<string, GameObject> findSpawnedPart,
            Func<GameObject, bool> isSubassemblyProxy,
            Func<GameObject, Action<GameObject>, bool> forEachProxyMember,
            Func<SelectionService> getSelectionService,
            Func<bool> isDragging,
            Func<bool> isToolModeLockedForParts,
            Func<GameObject> getHoveredPartFromXri,
            Func<GameObject> getHoveredPartFromMouse,
            Func<PlaceStepHandler> getPlaceHandler,
            Func<GameObject> getDraggedPart,
            Func<string, bool> isPartMovementLocked,
            Func<GameObject, GameObject> normalizeSelectableTarget,
            Action resetDragState)
        {
            _spawner = spawner;
            _partStates = partStates;
            _findSpawnedPart = findSpawnedPart;
            _isSubassemblyProxy = isSubassemblyProxy;
            _forEachProxyMember = forEachProxyMember;
            _getSelectionService = getSelectionService;
            _isDragging = isDragging;
            _isToolModeLockedForParts = isToolModeLockedForParts;
            _getHoveredPartFromXri = getHoveredPartFromXri;
            _getHoveredPartFromMouse = getHoveredPartFromMouse;
            _getPlaceHandler = getPlaceHandler;
            _getDraggedPart = getDraggedPart;
            _isPartMovementLocked = isPartMovementLocked;
            _normalizeSelectableTarget = normalizeSelectableTarget;
            _resetDragState = resetDragState;
        }

        // ════════════════════════════════════════════════════════════════════
        // Public accessors for owned state
        // ════════════════════════════════════════════════════════════════════

        public GameObject HoveredPart => _hoveredPart;
        public HashSet<string> RevealedPartIds => _revealedPartIds;
        public HashSet<string> ActiveStepPartIds => _activeStepPartIds;
        public bool PartsHiddenOnSpawn => _partsHiddenOnSpawn;

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
            if (!Application.isPlaying || _spawner == null || _isDragging() || _isToolModeLockedForParts())
            {
                ClearPartHoverVisual();
                return;
            }

            GameObject hoveredPart = _getHoveredPartFromXri();
            if (hoveredPart == null)
                hoveredPart = _getHoveredPartFromMouse();

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

            var selectionService = _getSelectionService();
            if (selectionService != null && selectionService.CurrentSelection == partGo)
                return false;

            if (_isSubassemblyProxy(partGo))
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

            var selectionService = _getSelectionService();
            if (selectionService == null)
                return;

            GameObject selected = _normalizeSelectableTarget(selectionService.CurrentSelection);
            if (!_isSubassemblyProxy(selected))
                return;

            ApplySelectedPartVisual(selected);
        }

        public void UpdatePointerDragSelectionVisual()
        {
            var draggedPart = _getDraggedPart();
            if (!_isDragging() || draggedPart == null)
                return;

            ApplySelectedPartVisual(draggedPart);
        }

        // ════════════════════════════════════════════════════════════════════
        // Part visual application (state-based)
        // ════════════════════════════════════════════════════════════════════

        public void ApplyHoveredPartVisual(GameObject partGo)
        {
            if (_isSubassemblyProxy(partGo))
            {
                _forEachProxyMember(partGo, member =>
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
            if (_isSubassemblyProxy(partGo))
            {
                _forEachProxyMember(partGo, member =>
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

            if (_isSubassemblyProxy(partGo))
            {
                _forEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

            string partId = partGo.name;
            ApplyPartVisualForState(partGo, partId, GetPartState(partId));
        }

        public void ApplyPartVisualForState(GameObject partGo, string partId, PartPlacementState state)
        {
            if (partGo == null)
                return;

            if (_isSubassemblyProxy(partGo))
            {
                switch (state)
                {
                    case PartPlacementState.Selected:
                    case PartPlacementState.Inspected:
                    case PartPlacementState.Grabbed:
                        _forEachProxyMember(partGo, ApplySelectedPartVisual);
                        break;
                    default:
                        _forEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
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
                    DisablePartColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;

                case PartPlacementState.Available:
                default:
                    DisablePartColorAffordance(partGo);
                    ApplyAvailablePartVisual(partGo, partId);
                    break;
            }
        }

        public void ApplyAvailablePartVisual(GameObject partGo, string partId)
        {
            if (_isSubassemblyProxy(partGo))
            {
                _forEachProxyMember(partGo, member => ApplyPartVisualForState(member, member.name, GetPartState(member.name)));
                return;
            }

            MaterialHelper.SetEmission(partGo, Color.black);
            ClearRendererPropertyBlocks(partGo);

            // Restore original textured materials if available
            if (MaterialHelper.RestoreOriginals(partGo))
                return;

            // Fallback for parts without original textures (primitives/placeholders)
            PartPreviewPlacement placement = _spawner != null ? _spawner.FindPartPlacement(partId) : null;
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

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }
        }

        public static void DisablePartColorAffordance(GameObject target)
        {
            if (target == null)
                return;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider != null)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(stateProvider);
                else
                    UnityEngine.Object.DestroyImmediate(stateProvider);
            }

            var receivers = target.GetComponentsInChildren<ColorMaterialPropertyAffordanceReceiver>(includeInactive: true);
            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] == null)
                    continue;

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(receivers[i]);
                else
                    UnityEngine.Object.DestroyImmediate(receivers[i]);
            }

            var blockHelpers = target.GetComponentsInChildren<MaterialPropertyBlockHelper>(includeInactive: true);
            for (int i = 0; i < blockHelpers.Length; i++)
            {
                if (blockHelpers[i] == null)
                    continue;

                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(blockHelpers[i]);
                else
                    UnityEngine.Object.DestroyImmediate(blockHelpers[i]);
            }
        }

        public void SyncPartGrabInteractivity(GameObject partGo, string partId)
        {
            if (partGo == null || string.IsNullOrWhiteSpace(partId) || _isSubassemblyProxy(partGo))
                return;

            XRGrabInteractable grabInteractable = partGo.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
                return;

            bool shouldEnableGrab = !_isPartMovementLocked(partId);
            if (grabInteractable.enabled == shouldEnableGrab)
                return;

            grabInteractable.enabled = shouldEnableGrab;

            Rigidbody rb = partGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (!shouldEnableGrab && _getDraggedPart() == partGo)
                _resetDragState();
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

            var placeHandler = _getPlaceHandler();
            if (_hintPreview != null)
            {
                if (!(placeHandler != null && placeHandler.IsPreviewHighlighted && placeHandler.HoveredPreview == _hintPreview))
                    MaterialHelper.SetMaterialColor(_hintPreview, pulseColor);
            }

            if (_hintSourceProxy != null)
                _forEachProxyMember(_hintSourceProxy, member => ApplyHintSourceVisual(member, pulseColor));
        }

        public void ClearHintHighlight()
        {
            var placeHandler = _getPlaceHandler();
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

            var parts = _spawner?.SpawnedParts;
            if (parts == null) return;

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] == null) continue;

                string partId = parts[i].name;

                // Keep completed/placed parts visible
                if (_partStates.TryGetValue(partId, out var state) &&
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
            var package = _spawner?.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            // Determine which subassembly we're in
            string subassemblyId = step.subassemblyId;

            // Collect all part ids for this subassembly (from all its steps)
            var subassemblyPartIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(subassemblyId))
            {
                StepDefinition[] allSteps = package.GetOrderedSteps();
                for (int s = 0; s < allSteps.Length; s++)
                {
                    if (!string.Equals(allSteps[s].subassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string[] rp = allSteps[s].requiredPartIds;
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
                string[] rp = step.requiredPartIds;
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
                if (_partStates.TryGetValue(partId, out var state) &&
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
                GameObject partGo = _findSpawnedPart(partId);
                if (partGo == null) continue;

                PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
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
                    var renderers = partGo.GetComponentsInChildren<Renderer>(true);
                    if (renderers.Length > 0)
                    {
                        Bounds combined = renderers[0].bounds;
                        for (int r = 1; r < renderers.Length; r++)
                            combined.Encapsulate(renderers[r].bounds);
                        width = Mathf.Max(combined.size.x, combined.size.z, InteractionVisualConstants.PartGridSpacing);
                    }
                    unplacedParts.Add((partId, partGo, width));
                }

                _partStates[partId] = PartPlacementState.Available;
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
            var package = _spawner?.CurrentPackage;
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
            else if (step.requiredPartIds != null)
            {
                for (int i = 0; i < step.requiredPartIds.Length; i++)
                {
                    if (!string.IsNullOrEmpty(step.requiredPartIds[i]))
                        _activeStepPartIds.Add(step.requiredPartIds[i]);
                }
            }

            // Walk all revealed parts: highlight active, dim the rest
            foreach (string partId in _revealedPartIds)
            {
                if (_partStates.TryGetValue(partId, out var state) &&
                    state is PartPlacementState.Completed or PartPlacementState.PlacedVirtually)
                    continue;

                GameObject partGo = _findSpawnedPart(partId);
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
            var package = _spawner.CurrentPackage;
            if (package == null || !package.TryGetStep(stepId, out var step))
                return;

            string[] partIds = step.requiredPartIds;
            if (partIds == null) return;

            foreach (string partId in partIds)
                MovePartToPlayPosition(partId);
        }

        public void RestoreCompletedStepParts(StepDefinition[] steps)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || steps == null) return;

            for (int s = 0; s < steps.Length; s++)
            {
                string[] partIds = steps[s].requiredPartIds;
                if (partIds == null) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    MovePartToPlayPosition(partId);

                    GameObject partGo = _findSpawnedPart(partId);
                    if (partGo != null)
                    {
                        // Ensure the part is visible — HideNonIntroducedParts may
                        // have hidden it before the restore path ran.
                        partGo.SetActive(true);
                    }

                    _partStates[partId] = PartPlacementState.Completed;
                    SyncPartGrabInteractivity(partGo, partId);
                    ApplyPartVisualForState(partGo, partId, PartPlacementState.Completed);
                    _revealedPartIds.Add(partId);
                }
            }
        }

        public void MovePartToPlayPosition(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return;

            PartPreviewPlacement pp = _spawner.FindPartPlacement(partId);
            if (pp == null) return;

            GameObject partGo = _findSpawnedPart(partId);
            if (partGo == null) return;

            Vector3    pPos   = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
            Vector3    pScale = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
            Quaternion pRot   = !pp.playRotation.IsIdentity
                ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                : Quaternion.identity;

            partGo.transform.SetLocalPositionAndRotation(pPos, pRot);
            partGo.transform.localScale = pScale;
        }

        public void RevertFutureStepParts(StepDefinition[] allSteps, int fromStepIndex)
        {
            var package = _spawner?.CurrentPackage;
            if (package == null || allSteps == null) return;

            for (int s = fromStepIndex; s < allSteps.Length; s++)
            {
                string[] partIds = allSteps[s].requiredPartIds;
                if (partIds == null) continue;

                for (int p = 0; p < partIds.Length; p++)
                {
                    string partId = partIds[p];
                    if (string.IsNullOrEmpty(partId)) continue;

                    GameObject partGo = _findSpawnedPart(partId);
                    if (partGo == null) continue;

                    // Hide future parts instead of repositioning — they'll be revealed
                    // when their step activates via RevealStepParts.
                    partGo.SetActive(false);
                    _revealedPartIds.Remove(partId);
                    _partStates[partId] = PartPlacementState.NotIntroduced;
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

            return _partStates.TryGetValue(partId, out PartPlacementState state)
                ? state
                : PartPlacementState.Available;
        }
    }
}
