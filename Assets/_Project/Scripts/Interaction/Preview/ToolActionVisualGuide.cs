using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Manages world-space visual overlays during a tool action preview:
    /// progress ring (both modes) and directional arrow (Guided only).
    /// Spawns a curved arc arrow for Torque profiles, straight arrow for others.
    /// One instance per preview lifecycle — created on Enter, destroyed on Exit.
    /// </summary>
    public sealed class ToolActionVisualGuide
    {
        private GestureProgressVisual _progressRing;
        private DirectionalArrowOverlay _arrow;
        private CurvedArrowOverlay _curvedArrow;
        private PreviewMode _mode;

        public void Enter(PreviewMode mode, PreviewStyle style, Vector3 targetWorldPos, Vector2 expectedDragDir)
        {
            _mode = mode;
            _progressRing = GestureProgressVisual.Spawn(targetWorldPos);
            _progressRing.SetProgress(0f);

            if (mode == PreviewMode.Guided)
            {
                if (style == PreviewStyle.Torque)
                    _curvedArrow = CurvedArrowOverlay.Spawn(targetWorldPos);
                else
                    _arrow = DirectionalArrowOverlay.Spawn(targetWorldPos, expectedDragDir);
            }
        }

        public void SetProgress(float progress01)
        {
            if (_progressRing != null)
                _progressRing.SetProgress(progress01);

            float fade = 1f - progress01;
            if (_arrow != null)
                _arrow.SetFade(fade);
            if (_curvedArrow != null)
                _curvedArrow.SetFade(fade);
        }

        public void Exit()
        {
            if (_progressRing != null)
                Object.Destroy(_progressRing.gameObject);
            if (_arrow != null)
                Object.Destroy(_arrow.gameObject);
            if (_curvedArrow != null)
                Object.Destroy(_curvedArrow.gameObject);
            _progressRing = null;
            _arrow = null;
            _curvedArrow = null;
        }
    }
}
