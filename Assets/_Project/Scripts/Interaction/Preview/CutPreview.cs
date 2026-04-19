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
        public override float Duration => Override(Cfg?.duration ?? 0f, 1.2f);

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
            float emitThresh  = Override(Cfg?.cutEmissionThreshold ?? 0f, 0.15f);
            Color glowColor   = Override(Cfg?.cutGlowColor ?? default, new Color(1f, 0.5f, 0.1f, 1f));
            float s1Threshold = Override(Cfg?.cutSpark1Threshold ?? 0f, 0.2f);
            float s1Scale     = Override(Cfg?.cutSpark1Scale ?? 0f, 0.12f);
            float s2Threshold = Override(Cfg?.cutSpark2Threshold ?? 0f, 0.6f);
            float s2Scale     = Override(Cfg?.cutSpark2Scale ?? 0f, 0.08f);
            float lineSpawn   = Override(Cfg?.cutLineSpawn ?? 0f, 0.25f);
            float lineWinEnd  = Override(Cfg?.cutLineWindowEnd ?? 0f, 0.9f);

            // Tool emission glow (orange-hot by default).
            if (progress >= emitThresh && _ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, glowColor);

            if (!_sparksSpawned && progress >= s1Threshold)
            {
                _sparksSpawned = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * s1Scale);
            }

            if (!_sparksBurst2 && progress >= s2Threshold)
            {
                _sparksBurst2 = true;
                CompletionParticleEffect.TrySpawn("torque_sparks",
                    _ctx.TargetWorldPos, Vector3.one * s2Scale);
            }

            if (_cutLine == null && progress >= lineSpawn)
                _cutLine = SpawnCutLine(_ctx.TargetWorldPos, Override(Cfg?.cutLineColor ?? default, new Color(0.15f, 0.1f, 0.05f, 0.9f)));

            if (_cutLine != null)
            {
                float lineProgress = Mathf.InverseLerp(lineSpawn, lineWinEnd, progress);
                _cutLine.transform.localScale = new Vector3(
                    Mathf.Lerp(0.001f, 0.06f, lineProgress),
                    0.002f,
                    0.002f);
            }
        }

        /// <summary>
        /// X-axis chatter during the active-cutting window. Outside the
        /// authored [windowStart, windowEnd] progress range the overlay is
        /// zero — the tool holds steady during tool-approach and spark-
        /// dissipation phases.
        /// </summary>
        public override Vector3 ComputeOverlayOffset(float progress)
        {
            float winStart = Override(Cfg?.cutVibrationWindowStart ?? 0f, 0.15f);
            float winEnd   = Override(Cfg?.cutVibrationWindowEnd ?? 0f, 0.9f);
            float freq     = Override(Cfg?.cutVibrationFrequency ?? 0f, 80f);
            float amp      = Override(Cfg?.cutVibrationAmplitude ?? 0f, 0.00015f);

            if (progress <= winStart || progress >= winEnd) return Vector3.zero;
            float vibrate = Mathf.Sin(progress * freq) * amp;
            return new Vector3(vibrate, 0f, 0f);
        }

        private static GameObject SpawnCutLine(Vector3 worldPos, Color color)
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
                mat.color = color;
                renderer.material = mat;
            }

            return line;
        }
    }
}
