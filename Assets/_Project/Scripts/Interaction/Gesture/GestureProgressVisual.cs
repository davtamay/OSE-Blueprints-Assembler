using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Procedural progress ring rendered around the tool target during ToolFocus.
    /// Uses a LineRenderer to draw a circular arc that fills as the gesture progresses.
    /// Attach to a temporary GameObject that is destroyed when ToolFocus exits.
    /// </summary>
    public sealed class GestureProgressVisual : MonoBehaviour
    {
        private const int SegmentCount = 64;
        private const float RingRadius = 0.08f;

        private static readonly Color ProgressColor = new Color(0.3f, 1f, 0.6f, 0.9f);
        private static readonly Color BackgroundColor = new Color(1f, 1f, 1f, 0.15f);

        private LineRenderer _progressRing;
        private LineRenderer _backgroundRing;
        private float _progress;

        /// <summary>
        /// Spawns a progress ring at the given world position.
        /// Returns the GestureProgressVisual component for updating progress.
        /// </summary>
        public static GestureProgressVisual Spawn(Vector3 worldPosition)
        {
            var go = new GameObject("GestureProgressRing");
            go.transform.position = worldPosition + Vector3.up * 0.12f;

            var visual = go.AddComponent<GestureProgressVisual>();
            visual.Initialize();
            return visual;
        }

        private void Initialize()
        {
            // Background ring (full circle, dim)
            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(transform, false);
            _backgroundRing = bgGo.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_backgroundRing, BackgroundColor);
            SetRingPoints(_backgroundRing, 1f);

            // Progress ring (partial arc, bright)
            var progGo = new GameObject("Progress");
            progGo.transform.SetParent(transform, false);
            _progressRing = progGo.AddComponent<LineRenderer>();
            ConfigureLineRenderer(_progressRing, ProgressColor);
            _progressRing.widthMultiplier = 0.012f;
            SetRingPoints(_progressRing, 0f);
        }

        public void SetProgress(float progress01)
        {
            _progress = Mathf.Clamp01(progress01);
            if (_progressRing != null)
                SetRingPoints(_progressRing, _progress);
        }

        private void LateUpdate()
        {
            // Billboard: face the camera
            Camera cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }

        private static void ConfigureLineRenderer(LineRenderer lr, Color color)
        {
            lr.useWorldSpace = false;
            lr.loop = false;
            lr.widthMultiplier = 0.006f;
            lr.material = CreateOverlayMaterial(color);
            lr.startColor = color;
            lr.endColor = color;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sortingOrder = 10;
            lr.positionCount = 0;
        }

        private static Material CreateOverlayMaterial(Color color)
        {
            // Hidden/Internal-Colored supports _ZTest and _ZWrite properties,
            // unlike Sprites/Default which ignores them.
            var mat = new Material(Shader.Find("Hidden/Internal-Colored"));
            mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            mat.SetInt("_ZWrite", 0);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 4000;
            mat.SetColor("_Color", color);
            return mat;
        }

        private static void SetRingPoints(LineRenderer lr, float fillAmount)
        {
            int count = Mathf.Max(2, Mathf.CeilToInt(SegmentCount * fillAmount));
            if (fillAmount <= 0f)
            {
                lr.positionCount = 0;
                return;
            }

            lr.positionCount = count;
            float angleStep = (360f * fillAmount) / (count - 1);

            for (int i = 0; i < count; i++)
            {
                float angle = (-90f + i * angleStep) * Mathf.Deg2Rad; // Start from top
                lr.SetPosition(i, new Vector3(
                    Mathf.Cos(angle) * RingRadius,
                    Mathf.Sin(angle) * RingRadius,
                    0f));
            }
        }

        private void OnDestroy()
        {
            // Materials are created at runtime — clean up
            if (_progressRing != null && _progressRing.material != null)
                Destroy(_progressRing.material);
            if (_backgroundRing != null && _backgroundRing.material != null)
                Destroy(_backgroundRing.material);
        }
    }
}
