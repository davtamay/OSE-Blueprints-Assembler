using OSE.App;
using OSE.Content;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns the <see cref="DockArcVisual"/> lifecycle within <see cref="PartInteractionBridge"/>.
    /// Resolves the active dock arc from subassembly placement state each frame and
    /// drives the visual accordingly.
    /// </summary>
    internal sealed class DockArcCoordinator
    {
        private readonly IBridgeContext _ctx;
        private DockArcVisual _dockArcVisual;

        public DockArcCoordinator(IBridgeContext ctx) => _ctx = ctx;

        /// <summary>
        /// Evaluates current subassembly state and updates (or clears) the dock arc visual.
        /// Called from <see cref="PartInteractionBridge.Update"/> on the hover-poll interval.
        /// </summary>
        public void Update()
        {
            if (!TryResolveActiveDockArc(
                out GameObject sourceProxy,
                out Vector3 guideStartWorldPos,
                out Vector3 guideEndWorldPos,
                out Vector3 sourceUp,
                out Vector3 targetUp,
                out bool useLinearGuide))
            {
                Clear();
                return;
            }

            if (_dockArcVisual == null)
                _dockArcVisual = DockArcVisual.Spawn();

            GameObject selected = _ctx.NormalizeSelectablePlacementTarget(
                _ctx.SelectionService != null ? _ctx.SelectionService.CurrentSelection : null);
            GameObject hovered = _ctx.IsExternalControlEnabled
                ? null  // external hovered state lives in SelectionCoordinator; bridge passes it if needed
                : _ctx.VisualFeedback?.HoveredPart;
            hovered = _ctx.NormalizeSelectablePlacementTarget(hovered);

            float emphasis = useLinearGuide ? 0.7f : 0.35f;
            if (_ctx.Drag.DraggedPart == sourceProxy || selected == sourceProxy)
                emphasis = 1f;
            else if (hovered == sourceProxy)
                emphasis = 0.7f;

            if (useLinearGuide)
            {
                _dockArcVisual.SetLinearGuide(guideStartWorldPos, guideEndWorldPos, sourceUp, targetUp, emphasis);
                _dockArcVisual.SetMarkerLinks(sourceProxy, sourceProxy);
                return;
            }

            _dockArcVisual.SetArc(guideStartWorldPos, guideEndWorldPos, sourceUp, targetUp, emphasis);
            _dockArcVisual.SetMarkerLinks(sourceProxy, sourceProxy);
        }

        /// <summary>Destroys the dock arc visual if present.</summary>
        public void Clear()
        {
            if (_dockArcVisual == null)
                return;

            _dockArcVisual.Cleanup();
            _dockArcVisual = null;
        }

        // ── Private ───────────────────────────────────────────────────────

        private bool TryResolveActiveDockArc(
            out GameObject sourceProxy,
            out Vector3 guideStartWorldPos,
            out Vector3 guideEndWorldPos,
            out Vector3 sourceUp,
            out Vector3 targetUp,
            out bool useLinearGuide)
        {
            sourceProxy = null;
            guideStartWorldPos = Vector3.zero;
            guideEndWorldPos = Vector3.zero;
            sourceUp = Vector3.up;
            targetUp = Vector3.up;
            useLinearGuide = false;

            if (!Application.isPlaying ||
                !ServiceRegistry.TryGet<IMachineSessionController>(out _))
            {
                return false;
            }

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            StepController stepController = session.AssemblyController?.StepController;
            StepDefinition step = stepController?.HasActiveStep == true
                ? stepController.CurrentStepDefinition
                : null;

            if (step == null ||
                !step.RequiresSubassemblyPlacement ||
                string.IsNullOrWhiteSpace(step.requiredSubassemblyId) ||
                step.targetIds == null ||
                step.targetIds.Length != 1 ||
                _ctx.SubassemblyController == null ||
                !_ctx.SubassemblyController.TryGetProxy(step.requiredSubassemblyId, out sourceProxy))
            {
                return false;
            }

            if (step.IsAxisFitPlacement &&
                _ctx.SubassemblyController.TryGetActiveFitGuide(
                    step.requiredSubassemblyId,
                    out Vector3 fitCurrentWorld,
                    out Vector3 fitFinalWorld,
                    out Vector3 fitUp))
            {
                guideStartWorldPos = fitCurrentWorld;
                guideEndWorldPos = fitFinalWorld;
                sourceUp = fitUp;
                targetUp = fitUp;
                useLinearGuide = true;
                return true;
            }

            guideStartWorldPos = ResolveVisualAnchor(sourceProxy);
            sourceUp = sourceProxy.transform.up;

            GameObject targetPreview = _ctx.PreviewManager?.FindPreviewForTarget(step.targetIds[0]);
            if (targetPreview != null)
            {
                guideEndWorldPos = ResolveVisualAnchor(targetPreview);
                targetUp = targetPreview.transform.up;
                return true;
            }

            if (!_ctx.SubassemblyController.TryResolveTargetPose(
                    step.targetIds[0], out Vector3 targetLocalPos, out Quaternion targetRot, out _))
            {
                return false;
            }

            Transform previewRoot = _ctx.Setup != null ? _ctx.Setup.PreviewRoot : null;
            guideEndWorldPos = previewRoot != null
                ? previewRoot.TransformPoint(targetLocalPos)
                : targetLocalPos;
            targetUp = (previewRoot != null ? previewRoot.rotation : Quaternion.identity) * targetRot * Vector3.up;
            return true;
        }

        private static Vector3 ResolveVisualAnchor(GameObject target)
        {
            if (target == null)
                return Vector3.zero;

            if (PreviewSpawnManager.TryGetRenderableBounds(target, out Bounds bounds))
                return bounds.center;

            return target.transform.position;
        }
    }
}
