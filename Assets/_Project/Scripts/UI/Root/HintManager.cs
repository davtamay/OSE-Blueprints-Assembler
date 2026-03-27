using System;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages hint resolution, presentation, and the hint world canvas.
    /// Extracted from PartInteractionBridge (Phase 4).
    /// </summary>
    internal sealed class HintManager
    {
        private readonly IBridgeContext _ctx;

        private HintWorldCanvas _hintWorldCanvas;

        public HintManager(IBridgeContext context)
        {
            _ctx = context;
        }

        public void HandleHintRequested(HintRequested evt)
        {
            var package = _ctx.Spawner?.CurrentPackage;
            if (package == null)
                return;

            if (!package.TryGetStep(evt.StepId, out var step))
                return;

            HintDefinition hint = ResolveHintForStep(package, step, evt.TotalHintsForStep);
            if (hint == null && !step.RequiresSubassemblyPlacement)
                return;

            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui) && !ui.IsHintDisplayAllowed)
                return;

            string hintTitle = hint?.title;
            string hintMessage = hint?.message;
            Transform targetTransform = ResolveHintTargetTransform(hint);
            GameObject sourceProxy = null;
            GameObject targetPreview = targetTransform != null ? targetTransform.gameObject : null;

            if (TryBuildSubassemblyHintPresentation(
                    step,
                    hint,
                    out string stackTitle,
                    out string stackMessage,
                    out Transform stackAnchor,
                    out sourceProxy,
                    out GameObject stackPreview))
            {
                hintTitle = stackTitle;
                hintMessage = stackMessage;
                targetTransform = stackAnchor;
                targetPreview = stackPreview;
            }

            if (ui != null)
                ui.ShowHintContent(hintTitle, hintMessage, hint?.type);

            if (targetTransform != null)
            {
                var feedback = _ctx.VisualFeedback;
                if (feedback != null)
                {
                    feedback.HintPreview = targetPreview;
                    feedback.HintSourceProxy = sourceProxy;
                    feedback.HintHighlightUntil = Time.time + InteractionVisualConstants.HintHighlightDuration;
                }
                if (targetPreview != null)
                    MaterialHelper.ApplyPreviewMaterial(targetPreview);

                if (_hintWorldCanvas == null)
                    ServiceRegistry.TryGet<HintWorldCanvas>(out _hintWorldCanvas);

                if (_hintWorldCanvas == null)
                {
                    var go = new GameObject("Hint World Canvas");
                    _hintWorldCanvas = go.AddComponent<HintWorldCanvas>();
                }

                _hintWorldCanvas.ShowHint(hint?.type, hintTitle, hintMessage, targetTransform);
            }
        }

        internal static HintDefinition ResolveHintForStep(MachinePackageDefinition package, StepDefinition step, int totalHintsForStep)
        {
            if (package == null || step == null)
                return null;

            string[] hintIds = step.hintIds;
            if (hintIds == null || hintIds.Length == 0)
                return null;

            int index = Mathf.Clamp(totalHintsForStep - 1, 0, hintIds.Length - 1);
            string hintId = hintIds[index];
            if (string.IsNullOrWhiteSpace(hintId))
                return null;

            if (package.TryGetHint(hintId, out HintDefinition hint))
                return hint;

            return null;
        }

        internal Transform ResolveHintTargetTransform(HintDefinition hint)
        {
            if (hint == null)
                return null;

            var previews = _ctx.PreviewManager?.SpawnedPreviews;
            if (previews == null)
                return null;

            if (!string.IsNullOrWhiteSpace(hint.targetId))
            {
                foreach (var preview in previews)
                {
                    if (preview == null) continue;
                    PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                    if (info != null && string.Equals(info.TargetId, hint.targetId, StringComparison.OrdinalIgnoreCase))
                        return preview.transform;
                }
            }

            if (!string.IsNullOrWhiteSpace(hint.partId))
            {
                foreach (var preview in previews)
                {
                    if (preview == null) continue;
                    PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                    if (info != null && info.MatchesPart(hint.partId))
                        return preview.transform;
                }
            }

            return null;
        }

        internal bool TryBuildSubassemblyHintPresentation(
            StepDefinition step,
            HintDefinition authoredHint,
            out string title,
            out string message,
            out Transform worldAnchor,
            out GameObject sourceProxy,
            out GameObject targetPreview)
        {
            title = null;
            message = null;
            worldAnchor = null;
            sourceProxy = null;
            targetPreview = null;

            var subCtrl = _ctx.SubassemblyController;
            if (step == null || !step.RequiresSubassemblyPlacement || subCtrl == null)
                return false;

            string subassemblyId = step.requiredSubassemblyId;
            if (string.IsNullOrWhiteSpace(subassemblyId) ||
                !subCtrl.TryGetProxy(subassemblyId, out sourceProxy))
            {
                return false;
            }

            string targetId = !string.IsNullOrWhiteSpace(authoredHint?.targetId)
                ? authoredHint.targetId
                : (step.targetIds != null && step.targetIds.Length > 0 ? step.targetIds[0] : null);

            if (!string.IsNullOrWhiteSpace(targetId))
            {
                var previews = _ctx.PreviewManager?.SpawnedPreviews;
                if (previews != null)
                {
                    foreach (GameObject preview in previews)
                    {
                        if (preview == null)
                            continue;

                        PlacementPreviewInfo info = preview.GetComponent<PlacementPreviewInfo>();
                        if (info != null && string.Equals(info.TargetId, targetId, StringComparison.OrdinalIgnoreCase))
                        {
                            targetPreview = preview;
                            break;
                        }
                    }
                }
            }

            if (!subCtrl.TryGetDisplayInfo(sourceProxy, out string displayName, out _))
                displayName = subassemblyId;

            title = $"Move {displayName}";
            message = $"Move the completed {displayName} as one finished panel. Drag the whole frame side toward the highlighted target and it will rotate into place as it docks.";

            worldAnchor = sourceProxy.transform;
            return true;
        }
    }
}
