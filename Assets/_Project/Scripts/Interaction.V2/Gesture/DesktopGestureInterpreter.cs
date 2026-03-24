using UnityEngine;
using UnityEngine.InputSystem;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Desktop (mouse) gesture interpreter. Translates mouse input into
    /// normalized gesture progress for each <see cref="GestureType"/>.
    /// Phase 2: RotaryTorque only. Other types return immediate completion.
    /// </summary>
    public sealed class DesktopGestureInterpreter : IGestureInterpreter
    {
        private GestureContext _context;
        private float _accumulatedAngle;
        private float _holdTimer;
        private Vector2 _lastPointerPos;
        private bool _active;

        public bool Supports(GestureType gestureType) => true;

        public void Begin(GestureContext context)
        {
            _context = context;
            _accumulatedAngle = 0f;
            _holdTimer = 0f;
            _active = true;

            var mouse = Mouse.current;
            _lastPointerPos = mouse != null ? mouse.position.ReadValue() : context.TargetScreenPosition;
        }

        public GestureFrame Tick(float deltaTime)
        {
            if (!_active)
                return new GestureFrame { WasCancelled = true };

            var mouse = Mouse.current;
            if (mouse == null)
                return new GestureFrame { WasCancelled = true };

            Vector2 currentPos = mouse.position.ReadValue();

            // Cancel on right-click
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _active = false;
                return new GestureFrame { WasCancelled = true };
            }

            switch (_context.GestureType)
            {
                case GestureType.RotaryTorque:
                    return TickRotaryTorque(currentPos, deltaTime);

                case GestureType.SteadyHold:
                    return TickSteadyHold(currentPos, deltaTime);

                case GestureType.LinearPull:
                    return TickLinearPull(currentPos);

                case GestureType.ImpactStrike:
                    return TickImpactStrike(currentPos, deltaTime);

                // Tap and unsupported types: complete immediately
                case GestureType.Tap:
                default:
                    return new GestureFrame { Progress = 1f, IsComplete = true, GestureValue = 0f };
            }
        }

        public void End()
        {
            _active = false;
        }

        // ── RotaryTorque: circular drag around target screen position ──

        private GestureFrame TickRotaryTorque(Vector2 currentPos, float deltaTime)
        {
            // Compute angular change around the target screen position
            Vector2 prevDir = (_lastPointerPos - _context.TargetScreenPosition);
            Vector2 currDir = (currentPos - _context.TargetScreenPosition);

            if (prevDir.sqrMagnitude > 1f && currDir.sqrMagnitude > 1f)
            {
                float prevAngle = Mathf.Atan2(prevDir.y, prevDir.x) * Mathf.Rad2Deg;
                float currAngle = Mathf.Atan2(currDir.y, currDir.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(prevAngle, currAngle);

                // Only accumulate clockwise rotation (negative delta in screen space = CW)
                // Accept both directions for standard mode
                _accumulatedAngle += Mathf.Abs(delta);
            }

            _lastPointerPos = currentPos;

            float target = _context.Config.TargetAngleDegrees;
            float progress = target > 0f ? Mathf.Clamp01(_accumulatedAngle / target) : 1f;

            return new GestureFrame
            {
                Progress = progress,
                IsComplete = progress >= 1f,
                GestureValue = _accumulatedAngle
            };
        }

        // ── SteadyHold: keep pointer near target for duration ──

        private GestureFrame TickSteadyHold(Vector2 currentPos, float deltaTime)
        {
            float dist = Vector2.Distance(currentPos, _context.TargetScreenPosition);
            bool nearTarget = dist < 80f; // pixels

            if (nearTarget)
                _holdTimer += deltaTime;
            else
                _holdTimer = Mathf.Max(0f, _holdTimer - deltaTime * 0.5f); // decay slowly if drifting

            float target = _context.Config.HoldDurationSeconds;
            float progress = target > 0f ? Mathf.Clamp01(_holdTimer / target) : 1f;

            return new GestureFrame
            {
                Progress = progress,
                IsComplete = progress >= 1f,
                GestureValue = _holdTimer
            };
        }

        // ── LinearPull: drag away from target along initial direction ──

        private GestureFrame TickLinearPull(Vector2 currentPos)
        {
            float pixelDist = Vector2.Distance(currentPos, _context.TargetScreenPosition);
            // Convert screen pixels to approximate world units (rough heuristic)
            float worldDist = pixelDist * 0.001f;

            float target = _context.Config.TargetPullDistance;
            float progress = target > 0f ? Mathf.Clamp01(worldDist / target) : 1f;

            _lastPointerPos = currentPos;

            return new GestureFrame
            {
                Progress = progress,
                IsComplete = progress >= 1f,
                GestureValue = worldDist
            };
        }

        // ── ImpactStrike: detect quick flick toward target ──

        private GestureFrame TickImpactStrike(Vector2 currentPos, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                _lastPointerPos = currentPos;
                return new GestureFrame();
            }

            float speed = Vector2.Distance(currentPos, _lastPointerPos) / deltaTime;
            _lastPointerPos = currentPos;

            float threshold = _context.Config.StrikeSpeedThreshold;
            float progress = threshold > 0f ? Mathf.Clamp01(speed / threshold) : 0f;

            return new GestureFrame
            {
                Progress = progress,
                IsComplete = progress >= 1f,
                GestureValue = speed
            };
        }
    }
}
