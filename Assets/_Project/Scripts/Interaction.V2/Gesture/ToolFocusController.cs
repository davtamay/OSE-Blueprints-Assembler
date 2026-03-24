using System;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Orchestrates the ToolFocus lifecycle: enter, tick gesture interpreter,
    /// drive visual feedback, and exit on completion or cancellation.
    /// Created by the InteractionOrchestrator when a gesture-enabled Use step
    /// target is tapped.
    /// </summary>
    public sealed class ToolFocusController
    {
        public enum State { Inactive, Active }

        private State _state = State.Inactive;
        private IGestureInterpreter _interpreter;
        private GestureContext _context;
        private GestureType _gestureType;
        private float _autoAssistTimer;
        private GestureProgressVisual _progressVisual;

        // Callbacks
        private Action<string> _onComplete;  // targetId
        private Action _onCancel;

        // Context
        private string _targetId;
        private StepDefinition _step;

        public State CurrentState => _state;
        public bool IsActive => _state == State.Active;
        public float CurrentProgress { get; private set; }

        /// <summary>
        /// Enter ToolFocus for a specific target.
        /// </summary>
        public void Enter(
            StepDefinition step,
            string targetId,
            Vector3 targetWorldPos,
            Vector2 targetScreenPos,
            InteractionMode mode,
            Action<string> onComplete,
            Action onCancel)
        {
            _step = step;
            _targetId = targetId;
            _onComplete = onComplete;
            _onCancel = onCancel;

            _gestureType = GestureConfigResolver.ResolveGestureType(step);
            GestureConfig config = GestureConfigResolver.Resolve(step);

            // Tap gesture → complete immediately, no focus needed
            if (_gestureType == GestureType.Tap)
            {
                OseLog.VerboseInfo($"[ToolFocus] Gesture type is Tap — completing immediately for '{targetId}'.");
                _onComplete?.Invoke(_targetId);
                return;
            }

            _interpreter = GestureInterpreterFactory.Create(mode);
            _context = new GestureContext(_gestureType, targetWorldPos, targetScreenPos, config);

            _interpreter.Begin(_context);
            _state = State.Active;
            _autoAssistTimer = 0f;
            CurrentProgress = 0f;

            // Spawn progress ring at target
            _progressVisual = GestureProgressVisual.Spawn(targetWorldPos);

            OseLog.Info($"[ToolFocus] Entered — gesture={_gestureType}, target='{targetId}', mode={config.GestureMode}");
        }

        /// <summary>
        /// Tick the gesture interpreter. Call from Update when in ToolFocus state.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_state != State.Active || _interpreter == null)
                return;

            GestureFrame frame = _interpreter.Tick(deltaTime);
            CurrentProgress = frame.Progress;
            _progressVisual?.SetProgress(CurrentProgress);

            if (frame.WasCancelled)
            {
                Exit(completed: false);
                return;
            }

            if (frame.IsComplete)
            {
                Exit(completed: true);
                return;
            }

            // Auto-assist: after delay, gradually push progress toward completion
            if (_context.Config.AutoAssistDelay > 0f)
            {
                _autoAssistTimer += deltaTime;
                if (_autoAssistTimer >= _context.Config.AutoAssistDelay)
                {
                    float assistProgress = ((_autoAssistTimer - _context.Config.AutoAssistDelay) / 3f);
                    float boostedProgress = Mathf.Max(frame.Progress, assistProgress);
                    CurrentProgress = Mathf.Clamp01(boostedProgress);
                    _progressVisual?.SetProgress(CurrentProgress);

                    if (CurrentProgress >= 1f)
                    {
                        Exit(completed: true);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Force-cancel the current gesture (e.g. Escape key).
        /// </summary>
        public void Cancel()
        {
            if (_state == State.Active)
                Exit(completed: false);
        }

        private void Exit(bool completed)
        {
            _interpreter?.End();
            _interpreter = null;
            _state = State.Inactive;

            // Destroy progress visual
            if (_progressVisual != null)
            {
                UnityEngine.Object.Destroy(_progressVisual.gameObject);
                _progressVisual = null;
            }

            float finalProgress = CurrentProgress;
            CurrentProgress = 0f;

            if (completed)
            {
                OseLog.Info($"[ToolFocus] Gesture completed for '{_targetId}' (progress={finalProgress:F2}).");
                _onComplete?.Invoke(_targetId);
            }
            else
            {
                OseLog.Info($"[ToolFocus] Gesture cancelled for '{_targetId}' (progress={finalProgress:F2}).");
                _onCancel?.Invoke();
            }

            _onComplete = null;
            _onCancel = null;
            _targetId = null;
            _step = null;
        }
    }
}
