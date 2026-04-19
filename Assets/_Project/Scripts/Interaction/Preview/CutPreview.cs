using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Cut/Grind preview: tool approaches target, sparks stream out,
    /// a thin dark cut line appears on the surface.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: progress driven by user drag along cut direction.
    /// </summary>
    public sealed class CutPreview : ToolActionPreviewBase
    {
        public override float Duration => 1.2f;

        protected override float GuidedDragScale => 0.005f;
        protected override float AutoAssistDelay => 3f;
        protected override float AutoAssistRate => 0.5f;

        private bool _sparksSpawned;
        private bool _sparksBurst2;
        private GameObject _cutLine;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);
            _sparksSpawned = false;
            _sparksBurst2 = false;
            _cutLine = null;
        }

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            return context.ProjectDirectionToScreen(context.WeldAxis, Vector2.right);
        }

        public override void End(bool completed)
        {
            if (_ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);

            if (_cutLine != null)
            {
                if (completed)
                    Object.Destroy(_cutLine, 3f);
                else
                    Object.Destroy(_cutLine);
            }
        }

        protected override void ApplyEffects(float progress)
        {
            // At 15%: tool emission glow (orange-hot)
            if (progress >= 0.15f && _ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, new Color(1f, 0.5f, 0.1f, 1f));

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
                _cutLine.transform.localScale = new Vector3(
                    Mathf.Lerp(0.001f, 0.06f, lineProgress),
                    0.002f,
                    0.002f);
            }

            // Tool vibration during cutting. Formerly this was a direct
            // transform.position += on top of whatever the controller had
            // written — which silently broke the follow-part accumulator.
            // Now returned via ComputeOverlayOffset below so the controller
            // composes it cleanly.
        }

        /// <summary>
        /// X-axis chatter during the active-cutting window. Outside [0.15, 0.9]
        /// progress range the overlay is zero — the tool holds steady during
        /// tool-approach and spark-dissipation phases.
        /// </summary>
        public override Vector3 ComputeOverlayOffset(float progress)
        {
            if (progress <= 0.15f || progress >= 0.9f) return Vector3.zero;
            float vibrate = Mathf.Sin(progress * 80f) * 0.15f;
            return new Vector3(vibrate * 0.001f, 0f, 0f);
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
