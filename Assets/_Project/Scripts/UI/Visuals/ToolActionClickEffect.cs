using UnityEngine;
using UnityEngine.Pool;

namespace OSE.UI.Root
{
    /// <summary>
    /// Self-animating "click pop" effect spawned at a tool-action target position
    /// on successful action. Scales up briefly, fades out, then returns to pool.
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
        private const int DefaultCapacity = 4;
        private const int MaxSize = 16;

        private static ObjectPool<GameObject> _pool;

        private static ObjectPool<GameObject> Pool
        {
            get
            {
                if (_pool == null)
                {
                    _pool = new ObjectPool<GameObject>(
                        createFunc: CreatePooledObject,
                        actionOnGet: obj => obj.SetActive(true),
                        actionOnRelease: obj => obj.SetActive(false),
                        actionOnDestroy: obj =>
                        {
                            if (obj != null) Destroy(obj);
                        },
                        defaultCapacity: DefaultCapacity,
                        maxSize: MaxSize);
                }
                return _pool;
            }
        }

        private static GameObject CreatePooledObject()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "ToolClickEffect";

            // No physics needed
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            go.AddComponent<ToolActionClickEffect>();
            go.SetActive(false);
            return go;
        }

        public static void Spawn(Vector3 worldPosition, Vector3 baseScale, Color color, float pulseScale)
        {
            GameObject go = Pool.Get();
            go.transform.position = worldPosition;
            go.transform.localScale = baseScale;

            var effect = go.GetComponent<ToolActionClickEffect>();
            effect._baseScale = baseScale;
            effect._startColor = color;
            effect._endColor = new Color(color.r, color.g, color.b, 0f);
            effect._pulseScale = pulseScale;
            effect._elapsed = 0f;
            effect.EnsureMaterial();
            effect._material.color = color;
        }

        private void EnsureMaterial()
        {
            if (_material != null)
                return;

            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
                return;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return;

            _material = new Material(shader) { name = "ClickEffect", color = _startColor };
            _renderer.sharedMaterial = _material;
        }

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);

            // Scale: pop up then settle
            float scaleCurve = 1f + (_pulseScale - 1f) * Mathf.Sin(t * Mathf.PI);
            transform.localScale = _baseScale * scaleCurve;

            // Fade out
            if (_material != null)
                _material.color = Color.Lerp(_startColor, _endColor, t);

            if (t >= 1f)
                Pool.Release(gameObject);
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
        }
    }
}
