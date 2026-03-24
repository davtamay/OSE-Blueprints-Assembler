using OSE.UI.Root;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Torque preview: wrench rotates 90 degrees around the target's up axis.
    /// Sparks burst at the halfway point.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: progress driven by user drag in clockwise screen direction.
    /// </summary>
    public sealed class TorquePreview : IToolActionPreview
    {
        public float Duration => 0.8f;

        private PreviewContext _ctx;
        private float _elapsed;
        private float _guidedProgress;
        private float _autoAssistTimer;
        private Quaternion _startRot;
        private bool _sparksSpawned;

        private const float TargetAngle = 90f;
        private const float GuidedDragScale = 0.005f; // pixels to progress
        private const float AutoAssistDelay = 3f;
        private const float AutoAssistRate = 0.5f; // progress per second after delay

        public void Begin(PreviewContext context)
        {
            _ctx = context;
            _startRot = context.ToolGhost != null ? context.ToolGhost.transform.rotation : Quaternion.identity;
            _elapsed = 0f;
            _guidedProgress = 0f;
            _autoAssistTimer = 0f;
            _sparksSpawned = false;
        }

        public float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyRotation(EaseInOutCubic(t));
            CheckSparks(t);
            return t;
        }

        public float TickGuided(float deltaTime, Vector2 dragDelta)
        {
            // Measure drag component in expected direction (clockwise = rightward on screen)
            Vector2 expected = GetExpectedDragDirection(_ctx);
            float dot = Vector2.Dot(dragDelta, expected);

            if (dot > 0f)
            {
                _guidedProgress += dot * GuidedDragScale;
                _autoAssistTimer = 0f; // reset auto-assist on valid input
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            // Auto-assist kicks in after delay
            if (_autoAssistTimer >= AutoAssistDelay)
                _guidedProgress += AutoAssistRate * deltaTime;

            _guidedProgress = Mathf.Clamp01(_guidedProgress);
            ApplyRotation(EaseInOutCubic(_guidedProgress));
            CheckSparks(_guidedProgress);
            return _guidedProgress;
        }

        public Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            // Clockwise rotation maps to rightward drag on screen
            return Vector2.right;
        }

        public void End(bool completed) { }

        private void ApplyRotation(float easedProgress)
        {
            if (_ctx.ToolGhost == null) return;
            float angle = Mathf.Lerp(0f, TargetAngle, easedProgress);
            _ctx.ToolGhost.transform.rotation = _startRot * Quaternion.AngleAxis(angle, Vector3.up);
        }

        private void CheckSparks(float progress)
        {
            if (!_sparksSpawned && progress >= 0.5f)
            {
                _sparksSpawned = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * 0.1f);
            }
        }

        private static float EaseInOutCubic(float t)
        {
            return t < 0.5f
                ? 4f * t * t * t
                : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
        }
    }
}
