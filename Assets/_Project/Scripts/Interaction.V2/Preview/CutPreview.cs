using OSE.UI.Root;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Cut/Grind preview: tool approaches target, sparks stream out,
    /// a thin dark cut line appears on the surface.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: progress driven by user drag along cut direction.
    /// </summary>
    public sealed class CutPreview : IToolActionPreview
    {
        public float Duration => 1.2f;

        private PreviewContext _ctx;
        private float _elapsed;
        private float _guidedProgress;
        private float _autoAssistTimer;
        private bool _sparksSpawned;
        private bool _sparksBurst2;
        private GameObject _cutLine;

        private const float GuidedDragScale = 0.005f;
        private const float AutoAssistDelay = 3f;
        private const float AutoAssistRate = 0.5f;

        public void Begin(PreviewContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            _guidedProgress = 0f;
            _autoAssistTimer = 0f;
            _sparksSpawned = false;
            _sparksBurst2 = false;
            _cutLine = null;
        }

        public float TickObserve(float deltaTime)
        {
            _elapsed += deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);
            ApplyEffects(t);
            return t;
        }

        public float TickGuided(float deltaTime, Vector2 dragDelta)
        {
            Vector2 expected = GetExpectedDragDirection(_ctx);
            float dot = Vector2.Dot(dragDelta, expected);

            if (dot > 0f)
            {
                _guidedProgress += dot * GuidedDragScale;
                _autoAssistTimer = 0f;
            }
            else
            {
                _autoAssistTimer += deltaTime;
            }

            if (_autoAssistTimer >= AutoAssistDelay)
                _guidedProgress += AutoAssistRate * deltaTime;

            _guidedProgress = Mathf.Clamp01(_guidedProgress);
            ApplyEffects(_guidedProgress);
            return _guidedProgress;
        }

        public Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            // Cut = drag horizontally across
            return Vector2.right;
        }

        public void End(bool completed)
        {
            // Clear tool emission
            if (_ctx.ToolGhost != null)
                MaterialHelper.SetEmission(_ctx.ToolGhost, Color.black);

            // Cut line persists briefly then self-destructs
            if (_cutLine != null)
            {
                if (completed)
                    Object.Destroy(_cutLine, 3f);
                else
                    Object.Destroy(_cutLine);
            }
        }

        private void ApplyEffects(float progress)
        {
            // At 15%: tool emission glow (orange-hot)
            if (progress >= 0.15f && _ctx.ToolGhost != null)
                MaterialHelper.SetEmission(_ctx.ToolGhost, new Color(1f, 0.5f, 0.1f, 1f));

            // Sparks at 20%
            if (!_sparksSpawned && progress >= 0.2f)
            {
                _sparksSpawned = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * 0.12f);
            }

            // Second sparks burst at 60%
            if (!_sparksBurst2 && progress >= 0.6f)
            {
                _sparksBurst2 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * 0.08f);
            }

            // Cut line appears at 25% and extends
            if (_cutLine == null && progress >= 0.25f)
                _cutLine = SpawnCutLine(_ctx.TargetWorldPos);

            if (_cutLine != null)
            {
                float lineProgress = Mathf.InverseLerp(0.25f, 0.9f, progress);
                // Extend line by scaling X
                _cutLine.transform.localScale = new Vector3(
                    Mathf.Lerp(0.001f, 0.06f, lineProgress),
                    0.002f,
                    0.002f);
            }

            // Tool vibration during cutting
            if (_ctx.ToolGhost != null && progress > 0.15f && progress < 0.9f)
            {
                float vibrate = Mathf.Sin(progress * 80f) * 0.15f;
                _ctx.ToolGhost.transform.position += new Vector3(vibrate * 0.001f, 0f, 0f);
            }
        }

        private static GameObject SpawnCutLine(Vector3 worldPos)
        {
            GameObject line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = "CutLine";
            line.transform.position = worldPos;
            line.transform.localScale = new Vector3(0.001f, 0.002f, 0.002f);

            var col = line.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var renderer = line.GetComponent<Renderer>();
            if (renderer != null)
            {
                var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Lit"));
                mat.color = new Color(0.15f, 0.1f, 0.05f, 0.9f); // dark scorch mark
                renderer.material = mat;
            }

            return line;
        }
    }
}
