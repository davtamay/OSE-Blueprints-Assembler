using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Torque preview: tool rotates around the authored insertion axis when available.
    /// Sparks burst at the halfway point.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: hybrid input — circular arc tracking (primary) plus
    /// horizontal drag fallback (secondary) so the gesture feels natural
    /// on both mouse and touch without requiring perfect circular motion.
    /// </summary>
    public sealed class TorquePreview : ToolActionPreviewBase
    {
        public override float Duration => 0.8f;

        protected override float AutoAssistDelay => 3f;
        protected override float AutoAssistRate => 0.5f;

        private Quaternion _startRot;
        private Vector3 _rotationAxisWorld;
        private bool _sparksSpawned;

        // Circular gesture tracking
        private Vector2 _targetScreenCenter;
        private float _prevAngle;
        private bool _angleInitialized;
        private float _accumulatedAngle;

        private const float TargetAngle = 120f;
        private const float GestureArc = 180f;
        private const float MinRadius = 10f;
        private const float DragFallbackScale = 0.008f;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);
            _startRot = context.ToolPreview != null ? context.ToolPreview.transform.rotation : Quaternion.identity;
            _rotationAxisWorld = Vector3.up;
            if (context.ToolPose != null && context.ToolPose.HasActionAxis)
            {
                _rotationAxisWorld = (_startRot * context.ToolPose.GetActionAxis()).normalized;
                if (_rotationAxisWorld.sqrMagnitude < 0.001f)
                    _rotationAxisWorld = Vector3.up;
            }
            _sparksSpawned = false;
            _angleInitialized = false;
            _accumulatedAngle = 0f;

            Camera cam = Camera.main;
            if (cam != null)
                _targetScreenCenter = cam.WorldToScreenPoint(context.TargetWorldPos);
            else
                _targetScreenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        public override float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyRotation(EaseInOutCubic(t));
            CheckSparks(t);
            return t;
        }

        /// <summary>Circular arc tracking + horizontal drag fallback.</summary>
        public override float TickGuided(float deltaTime, Vector2 dragDelta, Vector2 screenPos)
        {
            float angleDelta = 0f;
            bool hadInput = false;

            // Primary: circular arc tracking
            Vector2 toPointer = screenPos - _targetScreenCenter;
            float radius = toPointer.magnitude;

            if (radius > MinRadius)
            {
                float currentAngle = Mathf.Atan2(toPointer.y, toPointer.x) * Mathf.Rad2Deg;

                if (!_angleInitialized)
                {
                    _prevAngle = currentAngle;
                    _angleInitialized = true;
                }
                else
                {
                    float delta = Mathf.DeltaAngle(_prevAngle, currentAngle);
                    float cwDelta = -delta;

                    if (cwDelta > 0.3f)
                    {
                        angleDelta = cwDelta;
                        hadInput = true;
                    }

                    _prevAngle = currentAngle;
                }
            }

            // Secondary: horizontal drag fallback
            if (dragDelta.x > 1f)
            {
                float dragContribution = dragDelta.x * DragFallbackScale * GestureArc;
                if (dragContribution > angleDelta)
                {
                    angleDelta = dragContribution;
                    hadInput = true;
                }
            }

            if (hadInput)
            {
                _accumulatedAngle += angleDelta;
                _autoAssistTimer = 0f;
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            if (_autoAssistTimer >= AutoAssistDelay)
                _accumulatedAngle += AutoAssistRate * GestureArc * deltaTime;

            _accumulatedAngle = Mathf.Max(_accumulatedAngle, 0f);
            _guidedProgress = Mathf.Clamp01(_accumulatedAngle / GestureArc);

            ApplyRotation(EaseInOutCubic(_guidedProgress));
            CheckSparks(_guidedProgress);
            return _guidedProgress;
        }

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            float angleDeg = _accumulatedAngle;
            float rad = (-90f - angleDeg) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        public override void End(bool completed) { }

        private void ApplyRotation(float easedProgress)
        {
            if (_ctx.ToolPreview == null) return;
            float angle = Mathf.Lerp(0f, TargetAngle, easedProgress);
            _ctx.ToolPreview.transform.rotation = Quaternion.AngleAxis(angle, _rotationAxisWorld) * _startRot;
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
