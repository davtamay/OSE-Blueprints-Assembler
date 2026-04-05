using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using System.Text;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns the selection lifecycle within <see cref="PartInteractionBridge"/>:
    /// pointer-driven part selection, deselection, subassembly proxy selection,
    /// external hover info, and part/subassembly UI info push.
    /// </summary>
    internal sealed class SelectionCoordinator
    {
        private readonly IBridgeContext _ctx;

        // ── Per-selection transient state ─────────────────────────────────
        private bool _suppressSelectionEvents;
        private GameObject _lastSelectedVisualTarget;
        private float _selectionTime = -1f;
        private GameObject _externalHoveredPartForUi;

        /// <summary>
        /// Timestamp of the most recent accepted selection (realtimeSinceStartup).
        /// Read by PartInteractionBridge.TryHandleClickToPlace for its 50ms cooldown.
        /// </summary>
        public float SelectionTime => _selectionTime;

        public SelectionCoordinator(IBridgeContext ctx) => _ctx = ctx;

        // ── Selection event handlers ──────────────────────────────────────

        public void HandleSelectionServiceSelected(GameObject target)
            => HandleSelectionServiceSelection(target, isInspect: false);

        public void HandleSelectionServiceInspected(GameObject target)
            => HandleSelectionServiceSelection(target, isInspect: true);

        public void HandleSelectionServiceDeselected(GameObject target)
        {
            target = _ctx.NormalizeSelectablePlacementTarget(target);
            if (_suppressSelectionEvents)
                return;

            if (_ctx.IsSubassemblyProxy(target))
                _ctx.RestorePartVisual(target);
            else if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                partController.DeselectPart();

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                ui.HidePartInfoPanel();

            _ctx.PlaceHandler?.StopPreviewSelectionPulse();
            _ctx.ResetDragState();
        }

        public void HandlePlacementSucceeded(GameObject target)
        {
            target = _ctx.NormalizeSelectablePlacementTarget(target);
            if (!_ctx.IsSubassemblyProxy(target))
                return;

            _ctx.VisualFeedback?.ClearPartHoverVisual();
            _externalHoveredPartForUi = null;
            _ctx.PlaceHandler?.StopPreviewSelectionPulse();
            _ctx.ResetDragState();

            if (ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                partController.DeselectPart();

            _ctx.RestorePartVisual(target);
            DeselectFromSelectionService();
        }

        // ── Hover state ───────────────────────────────────────────────────

        /// <summary>
        /// Called by orchestrator via IPartActionBridge while external control is enabled.
        /// Shows hovered-part info while hovering; when hover clears, restores selected-part
        /// info if any, otherwise hides the panel.
        /// </summary>
        public void SetExternalHoveredPart(GameObject hoveredPart)
        {
            if (!Application.isPlaying || !_ctx.IsExternalControlEnabled)
                return;

            hoveredPart = _ctx.NormalizeSelectablePlacementTarget(hoveredPart);

            if (_ctx.IsToolModeLockedForParts())
            {
                _ctx.VisualFeedback?.ClearPartHoverVisual();
                _externalHoveredPartForUi = null;
                if (ServiceRegistry.TryGet<IPresentationAdapter>(out var toolModeUi) && toolModeUi is UIRootCoordinator toolUi)
                    toolUi.ClearHoverPartInfo();
                return;
            }

            bool hoverChanged = hoveredPart != _externalHoveredPartForUi;
            if (hoverChanged)
            {
                _ctx.VisualFeedback?.ClearPartHoverVisual();
                if (hoveredPart != null && _ctx.VisualFeedback != null &&
                    _ctx.VisualFeedback.CanApplyHoverVisual(hoveredPart, hoveredPart.name))
                {
                    _ctx.VisualFeedback.HoveredPart = hoveredPart;
                    _ctx.VisualFeedback.ApplyHoveredPartVisual(hoveredPart);
                }
            }

            _externalHoveredPartForUi = hoveredPart;

            if (_externalHoveredPartForUi != null)
            {
                if (_ctx.IsSubassemblyProxy(_externalHoveredPartForUi))
                {
                    PushSubassemblyInfoToUI(_externalHoveredPartForUi, isHoverInfo: true);
                }
                else if (_ctx.SubassemblyController != null &&
                         _ctx.SubassemblyController.TryGetSubassemblyId(_externalHoveredPartForUi, out _) &&
                         _ctx.SubassemblyController.TryGetDisplayInfo(_externalHoveredPartForUi, out _, out _))
                {
                    PushSubassemblyInfoToUI(_externalHoveredPartForUi, isHoverInfo: true);
                }
                else
                {
                    PushPartInfoToUI(_externalHoveredPartForUi.name, isHoverInfo: true);
                }
                return;
            }

            if (!hoverChanged)
                return;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var uiAdapter) && uiAdapter is UIRootCoordinator clearUi)
                clearUi.ClearHoverPartInfo();

            GameObject selected = _ctx.SelectionService?.CurrentSelection;
            if (selected != null)
            {
                selected = _ctx.NormalizeSelectablePlacementTarget(selected);
                if (_ctx.IsSubassemblyProxy(selected))
                    PushSubassemblyInfoToUI(selected);
                else
                    PushPartInfoToUI(selected.name);
            }
            else if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui2))
            {
                ui2.HidePartInfoPanel();
            }
        }

        // ── Deselect ──────────────────────────────────────────────────────

        public void DeselectFromSelectionService()
        {
            if (_ctx.SelectionService == null)
                return;

            GameObject current = _ctx.NormalizeSelectablePlacementTarget(_ctx.SelectionService.CurrentSelection);
            if (_ctx.IsSubassemblyProxy(current))
            {
                _ctx.RestorePartVisual(current);
                if (_ctx.VisualFeedback?.HoveredPart == current)
                    _ctx.VisualFeedback?.ClearPartHoverVisual();
            }

            if (_lastSelectedVisualTarget != null)
            {
                _ctx.RestorePartVisual(_lastSelectedVisualTarget);
                _lastSelectedVisualTarget = null;
            }

            _suppressSelectionEvents = true;
            _ctx.SelectionService.Deselect();
            _suppressSelectionEvents = false;
        }

        // ── UI info push ──────────────────────────────────────────────────

        public void PushPartInfoToUI(string partId, bool isHoverInfo = false)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null) return;
            if (!package.TryGetPart(partId, out var part)) return;
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui)) return;

            string toolNames = string.Empty;
            if (part.toolIds != null && part.toolIds.Length > 0)
            {
                var sb = new StringBuilder();
                foreach (string toolId in part.toolIds)
                {
                    if (string.IsNullOrEmpty(toolId)) continue;
                    if (package.TryGetTool(toolId, out var tool))
                    {
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(tool.GetDisplayName());
                    }
                }
                toolNames = sb.ToString();
            }

            string displayName = part.GetDisplayName();
            string functionText = part.function ?? string.Empty;
            string materialText = part.material ?? string.Empty;
            string searchTerms = part.searchTerms != null ? string.Join(" ", part.searchTerms) : string.Empty;

            if (isHoverInfo && ui is UIRootCoordinator hoverUi)
                hoverUi.ShowHoverPartInfoShell(displayName, functionText, materialText, toolNames, searchTerms);
            else
                ui.ShowPartInfoShell(displayName, functionText, materialText, toolNames, searchTerms);
        }

        public void PushSubassemblyInfoToUI(GameObject target, bool isHoverInfo = false)
        {
            if (_ctx.SubassemblyController == null ||
                !_ctx.SubassemblyController.TryGetDisplayInfo(target, out string displayName, out string description) ||
                !ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
            {
                return;
            }

            const string material = "Completed panel";
            const string searchTerms = "finished subassembly panel cube joining";

            if (isHoverInfo && ui is UIRootCoordinator hoverUi)
                hoverUi.ShowHoverPartInfoShell(displayName, description ?? string.Empty, material, string.Empty, searchTerms);
            else
                ui.ShowPartInfoShell(displayName, description ?? string.Empty, material, string.Empty, searchTerms);
        }

        // ── Private ───────────────────────────────────────────────────────

        private void HandleSelectionServiceSelection(GameObject target, bool isInspect)
        {
            target = _ctx.NormalizeSelectablePlacementTarget(target);
            if (_suppressSelectionEvents || target == null)
                return;

            if (_ctx.IsToolModeLockedForParts())
            {
                DeselectFromSelectionService();
                return;
            }

            if (!_ctx.IsSelectablePlacementObject(target))
                return;

            if (_lastSelectedVisualTarget != null && _lastSelectedVisualTarget != target)
            {
                _ctx.RestorePartVisual(_lastSelectedVisualTarget);
                _lastSelectedVisualTarget = null;
            }

            bool accepted;
            string selectionId = _ctx.ResolveSelectionId(target);
            bool isProxy = _ctx.IsSubassemblyProxy(target);
            bool isMemberOfSubassembly = !isProxy && _ctx.SubassemblyController != null &&
                _ctx.SubassemblyController.TryGetSubassemblyId(target, out _);

            if (isProxy)
            {
                accepted = !string.IsNullOrWhiteSpace(selectionId);
                if (accepted)
                {
                    PushSubassemblyInfoToUI(target, isHoverInfo: false);
                    _ctx.VisualFeedback?.ClearPartHoverVisual();
                    _ctx.VisualFeedback?.ApplySelectedPartVisual(target);
                    _lastSelectedVisualTarget = target;
                }
            }
            else
            {
                if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController))
                    return;

                accepted = isInspect
                    ? partController.InspectPart(target.name)
                    : partController.SelectPart(target.name);

                if (accepted && isMemberOfSubassembly &&
                    _ctx.SubassemblyController.TryGetDisplayInfo(target, out _, out _))
                {
                    PushSubassemblyInfoToUI(target, isHoverInfo: false);
                    _ctx.VisualFeedback?.ClearPartHoverVisual();
                }
            }

            if (!accepted)
            {
                DeselectFromSelectionService();
                return;
            }

            OseLog.Info($"[PartInteraction] Selected item '{selectionId ?? target.name}'");
            _selectionTime = Time.realtimeSinceStartup;
            _lastSelectedVisualTarget = target;
            _ctx.PlaceHandler?.StartPreviewSelectionPulse(selectionId ?? target.name);
            if (!_ctx.IsSubassemblyProxy(target))
                TryAutoCompleteSelectionStep(target.name);

            if (_ctx.Drag.PointerDown && _ctx.Drag.PendingSelectPart == target)
            {
                if (_ctx.IsPartMovementLocked(selectionId))
                {
                    OseLog.VerboseInfo($"[PartInteraction] Drag tracking blocked for locked item '{selectionId}'.");
                    _ctx.Drag.PendingSelectPart = null;
                    return;
                }

                _ctx.Drag.BeginDragTracking(target, selectionId);
            }
        }

        private void TryAutoCompleteSelectionStep(string selectedPartId)
        {
            if (string.IsNullOrWhiteSpace(selectedPartId))
                return;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return;

            StepController stepController = session.AssemblyController?.StepController;
            if (stepController == null || !stepController.HasActiveStep)
                return;

            StepDefinition step = stepController.CurrentStepDefinition;
            if (step == null || !HasEventTag(step.eventTags, "select"))
                return;

            if (!IsPartValidForSelectionStep(step, selectedPartId))
                return;

            stepController.CompleteStep(session.GetElapsedSeconds());
        }

        // ── Static helpers ────────────────────────────────────────────────

        private static bool IsPartValidForSelectionStep(StepDefinition step, string partId)
        {
            if (step == null || string.IsNullOrWhiteSpace(partId))
                return false;

            string[] effectiveParts = step.GetEffectiveRequiredPartIds();
            if (ContainsId(effectiveParts, partId))
                return true;

            if (HasAnyIds(effectiveParts))
                return false;

            if (ContainsId(step.optionalPartIds, partId))
                return true;

            if (HasAnyIds(step.optionalPartIds))
                return false;

            return true;
        }

        private static bool HasEventTag(string[] tags, string expectedTag)
        {
            if (tags == null || tags.Length == 0 || string.IsNullOrWhiteSpace(expectedTag))
                return false;

            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(tags[i], expectedTag, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool ContainsId(string[] ids, string expectedId)
        {
            if (ids == null || ids.Length == 0 || string.IsNullOrWhiteSpace(expectedId))
                return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i]))
                    continue;

                if (string.Equals(ids[i].Trim(), expectedId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool HasAnyIds(string[] ids)
        {
            if (ids == null || ids.Length == 0)
                return false;

            for (int i = 0; i < ids.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(ids[i]))
                    return true;
            }

            return false;
        }
    }
}
