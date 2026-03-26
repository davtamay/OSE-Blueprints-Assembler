using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Weld preview: torch travels along the weld seam depositing a bead behind it.
    /// Realistic MIG welding motion: start at one end, travel along joint, bead trails.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: progress driven by user drag.
    /// </summary>
    public sealed class WeldPreview : ToolActionPreviewBase
    {
        public override float Duration => 1.5f;

        protected override float GuidedDragScale => 0.004f;
        protected override float AutoAssistDelay => 3f;
        protected override float AutoAssistRate => 0.4f;

        private Quaternion _actionRot;
        private GameObject _weldBeadObj;
        private LineRenderer _weldLine;
        private bool _arcSpawned;

        // Computed on Begin — weld seam geometry
        private Vector3 _weldDir;
        private float _weldLen;
        private Vector3 _weldStart;
        private Vector3 _weldEnd;
        private Vector3 _approachDir;
        private float _standoff;

        private const float DefaultWeldLength = 0.03f;
        private const float BeadWidth = 0.004f;

        public override void Begin(PreviewContext context)
        {
            base.Begin(context);
            _actionRot = context.ToolPreview != null ? context.ToolPreview.transform.rotation : Quaternion.identity;
            _arcSpawned = false;
            _weldBeadObj = null;
            _weldLine = null;

            Vector3 toolPos = context.ToolPreview != null ? context.ToolPreview.transform.position : context.TargetWorldPos;
            _approachDir = (toolPos - context.TargetWorldPos).normalized;
            _standoff = Vector3.Distance(toolPos, context.TargetWorldPos);

            _weldLen = context.WeldLength > 0f ? context.WeldLength : DefaultWeldLength;
            _weldDir = context.WeldAxis.sqrMagnitude > 0.001f
                ? context.WeldAxis.normalized
                : ComputeHorizontalWeldDir();
            float halfLen = _weldLen * 0.5f;
            _weldStart = context.TargetWorldPos - _weldDir * halfLen;
            _weldEnd = context.TargetWorldPos + _weldDir * halfLen;
        }

        public override Vector2 GetExpectedDragDirection(PreviewContext context)
        {
            return context.ProjectDirectionToScreen(_weldDir, Vector2.right);
        }

        public override void End(bool completed)
        {
            if (_ctx.ToolPreview != null)
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);

            if (_weldBeadObj != null && !completed)
            {
                Object.Destroy(_weldBeadObj);
                _weldBeadObj = null;
            }
            else if (_weldBeadObj != null)
            {
                var fader = _weldBeadObj.AddComponent<WeldBeadCooler>();
                fader.Init(2f);
            }
        }

        protected override void ApplyEffects(float progress)
        {
            // At 10%: arc glow + sparks
            if (!_arcSpawned && progress >= 0.1f)
            {
                _arcSpawned = true;

                if (_ctx.ToolPreview != null)
                    MaterialHelper.SetEmission(_ctx.ToolPreview, new Color(0.9f, 0.95f, 1f, 1f) * 1.5f);

                CompletionParticleEffect.TrySpawn("weld_glow",
                    _ctx.TargetWorldPos, Vector3.one * 0.06f);
            }

            float travelProgress = Mathf.InverseLerp(0.15f, 0.9f, progress);

            // Tool movement: smooth travel along weld seam
            if (_ctx.ToolPreview != null && progress > 0.05f && progress < 0.95f)
            {
                Vector3 currentWeldPoint = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);
                Vector3 targetToolPos = currentWeldPoint + _approachDir * _standoff;

                _ctx.ToolPreview.transform.position = Vector3.Lerp(
                    _ctx.ToolPreview.transform.position,
                    targetToolPos,
                    Time.deltaTime * 6f);

                float wobble = Mathf.Sin(progress * 40f) * 0.12f;
                _ctx.ToolPreview.transform.rotation = _actionRot * Quaternion.Euler(wobble, 0f, wobble * 0.5f);
            }

            // Weld bead line
            if (_weldBeadObj == null && progress >= 0.2f)
                SpawnWeldLine();

            if (_weldLine != null)
                UpdateWeldLine(travelProgress);
        }

        private void SpawnWeldLine()
        {
            _weldBeadObj = new GameObject("WeldBeadLine");
            _weldBeadObj.transform.position = _ctx.TargetWorldPos;

            _weldLine = _weldBeadObj.AddComponent<LineRenderer>();
            _weldLine.useWorldSpace = true;
            _weldLine.positionCount = 2;
            _weldLine.startWidth = BeadWidth;
            _weldLine.endWidth = BeadWidth;
            _weldLine.numCapVertices = 4;
            _weldLine.alignment = LineAlignment.TransformZ;
            _weldLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _weldLine.receiveShadows = false;

            _weldBeadObj.transform.rotation = Quaternion.LookRotation(Vector3.up, _weldDir);

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            mat.color = new Color(0.85f, 0.82f, 0.72f, 1f);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0.8f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.6f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.9f, 0.7f, 1f) * 1.5f);
            }
            _weldLine.material = mat;

            _weldLine.SetPosition(0, _weldStart);
            _weldLine.SetPosition(1, _weldStart);
        }

        private void UpdateWeldLine(float travelProgress)
        {
            Vector3 currentEnd = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);
            _weldLine.SetPosition(0, _weldStart);
            _weldLine.SetPosition(1, currentEnd);
        }

        private static Vector3 ComputeHorizontalWeldDir()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 camRight = cam.transform.right;
                Vector3 horizontal = new Vector3(camRight.x, 0f, camRight.z);
                if (horizontal.sqrMagnitude > 0.001f)
                    return horizontal.normalized;
            }
            return Vector3.right;
        }
    }

    /// <summary>
    /// Cools a weld bead from bright silver-white to matte silver-gray (realistic MIG weld cooling).
    /// Emission fades out as bead solidifies. Bead persists as a permanent weld mark.
    /// </summary>
    internal sealed class WeldBeadCooler : MonoBehaviour
    {
        private float _duration;
        private float _elapsed;
        private Renderer _renderer;
        private Material _material;

        private static readonly Color HotColor = new Color(0.85f, 0.82f, 0.72f, 1f);
        private static readonly Color CoolColor = new Color(0.55f, 0.55f, 0.52f, 1f);

        public void Init(float duration)
        {
            _duration = duration;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _material = _renderer.material;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            if (_material != null)
            {
                _material.color = Color.Lerp(HotColor, CoolColor, t);

                if (_material.HasProperty("_EmissionColor"))
                {
                    Color emissive = Color.Lerp(
                        new Color(1f, 0.9f, 0.7f, 1f) * 1.5f,
                        Color.black,
                        t);
                    _material.SetColor("_EmissionColor", emissive);
                }
            }

            if (t >= 1f)
                Destroy(this);
        }

        private void OnDestroy()
        {
            // Bead stays as permanent weld mark
        }
    }
}
