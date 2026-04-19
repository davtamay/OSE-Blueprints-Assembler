using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Draws a <see cref="LineRenderer"/> between two scene anchors
    /// resolved via <see cref="AnimationAnchorResolver"/>. Recreates the
    /// weld-bead trail (weldStart → weldEnd, extending from 20% to 90% of
    /// action progress) and the cut scorch mark. Supports partial draw via
    /// progress — at t=0.5 the line extends halfway from A toward B.
    /// </summary>
    public sealed class LineBetweenAnchorsPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "lineBetweenAnchors";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private GameObject _lineGO;
        private LineRenderer _line;
        private Vector3 _posA;
        private Vector3 _posB;
        private bool _hasAnchors;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;

            _hasAnchors = ResolveAnchors(context, out _posA, out _posB);
            if (!_hasAnchors)
            {
                IsPlaying = false;
                return;
            }

            _lineGO = new GameObject("[Cue] LineBetweenAnchors");
            var host = context.Targets != null && context.Targets.Count > 0 ? context.Targets[0] : null;
            if (host != null) _lineGO.transform.SetParent(host.transform, worldPositionStays: true);

            _line = _lineGO.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.numCapVertices = 4;

            var e = context.Entry;
            float width = e.lineWidth > 0f ? e.lineWidth : 0.004f;
            _line.startWidth = width;
            _line.endWidth   = width;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = shader != null ? new Material(shader) : null;
            if (mat != null)
            {
                Color baseCol = e.fromColor.a > 0f
                    ? new Color(e.fromColor.r, e.fromColor.g, e.fromColor.b, e.fromColor.a)
                    : new Color(0.85f, 0.82f, 0.72f, 1f);
                mat.color = baseCol;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseCol);
                if (mat.HasProperty("_Metallic"))  mat.SetFloat("_Metallic", 0.8f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.6f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    Color emi = e.fromEmission.a > 0f
                        ? new Color(e.fromEmission.r, e.fromEmission.g, e.fromEmission.b, e.fromEmission.a)
                        : new Color(1f, 0.9f, 0.7f, 1f);
                    float intensity = e.lineEmissionIntensity > 0f ? e.lineEmissionIntensity : 1.5f;
                    mat.SetColor("_EmissionColor", emi * intensity);
                    mat.EnableKeyword("_EMISSION");
                }
                _line.material = mat;
            }

            Apply(0f);
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;
            float dur = _ctx.Duration > 0f ? _ctx.Duration : 1f;
            float t = Mathf.Clamp01(_elapsed / dur);
            Apply(t);
            if (t >= 1f) { IsPlaying = false; return false; }
            return true;
        }

        public void TickProgress(float progress01) => Apply(Mathf.Clamp01(progress01));

        public void Stop()
        {
            // Line persists (weld bead stays as a mark) — don't destroy.
            // Step transition / Cleanup will tear down via scene lifecycle.
            IsPlaying = false;
        }

        private void Apply(float t)
        {
            if (!_hasAnchors || _line == null) return;
            Vector3 endpoint = Vector3.Lerp(_posA, _posB, t);
            _line.SetPosition(0, _posA);
            _line.SetPosition(1, endpoint);
        }

        private static bool ResolveAnchors(AnimationCueContext ctx, out Vector3 a, out Vector3 b)
        {
            a = Vector3.zero;
            b = Vector3.zero;
            var e = ctx.Entry;
            bool hasA = AnimationAnchorResolver.TryResolveAnchor(e.anchorARef, ctx, out a);
            bool hasB = AnimationAnchorResolver.TryResolveAnchor(e.anchorBRef, ctx, out b);
            if (!hasA || !hasB)
            {
                Transform host = ctx.Targets != null && ctx.Targets.Count > 0 ? ctx.Targets[0]?.transform : null;
                if (host != null)
                {
                    if (!hasA) a = host.position;
                    if (!hasB) b = host.position + host.forward * 0.1f;
                }
                return host != null;
            }
            return true;
        }
    }
}
