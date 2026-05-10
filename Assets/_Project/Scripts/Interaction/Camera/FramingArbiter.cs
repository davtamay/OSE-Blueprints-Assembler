using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Sole writer to <see cref="AssemblyCameraRig"/> for step-entry framing
    /// requests. Centralises bounds resolution + framing telemetry so future
    /// regressions can't introduce a parallel framer that fights this one.
    ///
    /// Phase 2 scope: the arbiter is a thin facade — no mode state, no
    /// queueing, no minimum-move gate. It exists so callers (StepFocusComputer,
    /// StepGuidanceService) route through one entry point instead of poking
    /// the rig directly. Phase 3 will add the Active⇄Free⇄Pinned mode machine
    /// and yield-on-user-input behind this same API.
    ///
    /// Tool-action transient focus (mid-step "advance to next bolt"), GoStepHome
    /// recovery, and per-tool tight focus stay on direct rig access — they're
    /// intentional, scoped writes that step-entry framing doesn't conflict with.
    /// </summary>
    public sealed class FramingArbiter
    {
        private readonly AssemblyCameraRig _rig;
        private readonly IPartActionBridge _bridge;

        private string _lastFramedStepId;
        private Bounds _lastFramedBounds;
        private bool _hasLastFrame;

        public FramingArbiter(AssemblyCameraRig rig, IPartActionBridge bridge)
        {
            _rig = rig;
            _bridge = bridge;
        }

        /// <summary>
        /// Resolves the step's task-weighted focus bounds via the part bridge
        /// and frames the camera on them. Returns false when bounds resolution
        /// fails — the caller decides how to handle (typically: skip + log).
        /// </summary>
        public bool RequestFrameForStep(string stepId, string reason)
        {
            if (_rig == null || _bridge == null || string.IsNullOrEmpty(stepId))
                return false;

            if (!_bridge.TryGetStepFocusBounds(stepId, out Bounds bounds))
            {
                OseLog.Info($"[FramingArbiter] Step '{stepId}' — no bounds resolved (reason='{reason}'), skipping frame.");
                return false;
            }

            _rig.FrameBounds(bounds);
            _lastFramedStepId = stepId;
            _lastFramedBounds = bounds;
            _hasLastFrame = true;
            OseLog.Info($"[FramingArbiter] Step '{stepId}' framed (reason='{reason}', center={bounds.center}, size={bounds.size}).");
            return true;
        }

        /// <summary>
        /// Frame the camera on an explicit bounds value. Used by callers that
        /// already resolved bounds via their own path (e.g. step-area focus
        /// before orchestrator owns interaction).
        /// </summary>
        public void RequestFrameBounds(Bounds bounds, string reason)
        {
            if (_rig == null) return;
            _rig.FrameBounds(bounds);
            _lastFramedBounds = bounds;
            _hasLastFrame = true;
            OseLog.Info($"[FramingArbiter] Bounds framed (reason='{reason}', center={bounds.center}, size={bounds.size}).");
        }

        /// <summary>
        /// Resolves bounds for the active step without applying them. Useful
        /// for the minimum-move gate (test pivot containment before deciding
        /// to reframe). Returns false if no bridge or no bounds.
        /// </summary>
        public bool TryResolveStepBounds(string stepId, out Bounds bounds)
        {
            bounds = default;
            if (_bridge == null || string.IsNullOrEmpty(stepId)) return false;
            return _bridge.TryGetStepFocusBounds(stepId, out bounds);
        }

        /// <summary>
        /// Returns the bounds of the most recently framed subject, if any.
        /// </summary>
        public bool TryGetLastFramedBounds(out Bounds bounds)
        {
            bounds = _lastFramedBounds;
            return _hasLastFrame;
        }

        public string LastFramedStepId => _lastFramedStepId;
        public AssemblyCameraRig Rig => _rig;
    }
}
