using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Self-animating "click pop" effect spawned at a tool-action target position
    /// on successful action. Scales up briefly, fades out, then self-destructs.
    /// Fully detached from the step handler — survives step transitions.
    /// </summary>
    internal sealed class ToolActionClickEffect : MonoBehaviour
    {
        private Vector3 _baseScale;
        private float _elapsed;
        private Renderer _renderer;
        private Material _material;
        private Color _startColor;
        private Color _endColor;
        private float _pulseScale;

        private const float Duration = 0.35f;

        public static void Spawn(Vector3 worldPosition, Vector3 baseScale, Color color, float pulseScale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ToolClickEffect";
            go.transform.position = worldPosition;
            go.transform.localScale = baseScale;

            // No physics needed
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var effect = go.AddComponent<ToolActionClickEffect>();
            effect._baseScale = baseScale;
            effect._startColor = color;
            effect._endColor = new Color(color.r, color.g, color.b, 0f);
            effect._pulseScale = pulseScale;
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
            {
                Destroy(gameObject);
                return;
            }

            // Sprites/Default is always available, supports color + alpha,
            // and renders transparent without any keyword/blend setup.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Destroy(gameObject);
                return;
            }

            _material = new Material(shader) { name = "ClickEffect", color = _startColor };
            _renderer.sharedMaterial = _material;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);

            // Scale: pop up then settle
            float scaleCurve = 1f + (_pulseScale - 1f) * Mathf.Sin(t * Mathf.PI);
            transform.localScale = _baseScale * scaleCurve;

            // Fade out
            _material.color = Color.Lerp(_startColor, _endColor, t);

            if (t >= 1f)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
