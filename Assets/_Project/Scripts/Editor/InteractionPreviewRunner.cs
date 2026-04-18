using OSE.Interaction;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Edit-time driver for <see cref="IPartEffect"/>. Runs the same factory the
    /// runtime uses (via <see cref="PartEffectRegistry"/>), so the preview button in
    /// the Tool × Part Interaction panel shows exactly what the trainee will see —
    /// no parallel rendering path to drift out of sync.
    ///
    /// Lifecycle: <see cref="Start"/> snapshots the part's current local TRS, calls
    /// <see cref="IPartEffect.Begin"/>, then ticks <see cref="IPartEffect.Apply"/>
    /// each editor frame until the duration elapses or <see cref="Stop"/> is called.
    /// On Stop the part's original pose is always restored — replayable without
    /// accumulating drift.
    /// </summary>
    internal static class InteractionPreviewRunner
    {
        private static IPartEffect _effect;
        private static Transform   _part;
        private static Vector3     _savedPos;
        private static Quaternion  _savedRot;
        private static Vector3     _savedScale;
        private static Transform   _tool;
        private static Vector3     _savedToolLocalPos;
        private static bool        _followTool;
        private static double      _startTime;
        private static double      _duration = 1.5;

        public static bool IsRunning => _effect != null;
        public static Transform CurrentPart => _part;

        /// <summary>
        /// Starts an edit-time preview of <paramref name="effect"/> against
        /// <paramref name="part"/>. When <paramref name="tool"/> is non-null and
        /// <paramref name="followPart"/> is true, the tool transform accumulates the
        /// effect's per-frame world-space delta so the preview matches runtime
        /// behavior (drill body tracks the bolt as it plunges).
        /// </summary>
        public static void Start(IPartEffect effect, Transform part, float durationSec,
            Transform tool = null, bool followPart = true)
        {
            Stop(); // idempotent — cancel any previous run first

            if (effect == null || part == null) return;

            _effect     = effect;
            _part       = part;
            _savedPos   = part.localPosition;
            _savedRot   = part.localRotation;
            _savedScale = part.localScale;

            _tool              = tool;
            _followTool        = followPart && tool != null;
            _savedToolLocalPos = tool != null ? tool.localPosition : Vector3.zero;

            _duration  = Mathf.Max(0.1f, durationSec);
            _startTime = EditorApplication.timeSinceStartup;

            effect.Begin();
            EditorApplication.update += Tick;
            SceneView.RepaintAll();
        }

        public static void Stop()
        {
            EditorApplication.update -= Tick;

            if (_part != null)
            {
                _part.localPosition = _savedPos;
                _part.localRotation = _savedRot;
                _part.localScale    = _savedScale;
            }
            if (_tool != null)
            {
                _tool.localPosition = _savedToolLocalPos;
            }

            _effect    = null;
            _part      = null;
            _tool      = null;
            _followTool = false;
            SceneView.RepaintAll();
        }

        private static void Tick()
        {
            if (_effect == null || _part == null) { Stop(); return; }

            double elapsed = EditorApplication.timeSinceStartup - _startTime;
            float t = Mathf.Clamp01((float)(elapsed / _duration));
            Vector3 worldDelta = _effect.Apply(t);

            if (_followTool && _tool != null)
            {
                // Convert the world-space delta into the tool's parent-local frame,
                // then accumulate onto the tool's localPosition — matches the runtime
                // ToolActionPreviewBase.ApplyEffects tracking path.
                Transform parent = _tool.parent;
                Vector3 localDelta = parent != null
                    ? parent.InverseTransformVector(worldDelta)
                    : worldDelta;
                _tool.localPosition += localDelta;
            }

            SceneView.RepaintAll();

            if (t >= 1f) Stop();
        }
    }
}
