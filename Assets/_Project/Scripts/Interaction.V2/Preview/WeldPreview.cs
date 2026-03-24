using OSE.UI.Root;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Weld preview: torch travels along the weld seam depositing a bead behind it.
    /// Realistic MIG welding motion: start at one end, travel along joint, bead trails.
    /// Observe mode: auto-play over Duration seconds.
    /// Guided mode: progress driven by user drag.
    /// </summary>
    public sealed class WeldPreview : IToolActionPreview
    {
        public float Duration => 1.5f;

        private PreviewContext _ctx;
        private float _elapsed;
        private float _guidedProgress;
        private float _autoAssistTimer;
        private Quaternion _actionRot;
        private GameObject _weldBeadObj;
        private LineRenderer _weldLine;
        private bool _arcSpawned;

        // Computed on Begin — weld seam geometry
        private Vector3 _weldDir;
        private float _weldLen;
        private Vector3 _weldStart;
        private Vector3 _weldEnd;
        private Vector3 _approachDir; // from surface toward tool (used for tip offset)
        private float _standoff; // fixed standoff distance from surface, captured once

        private const float GuidedDragScale = 0.004f;
        private const float AutoAssistDelay = 3f;
        private const float AutoAssistRate = 0.4f;

        private const float DefaultWeldLength = 0.03f;
        private const float BeadWidth = 0.004f;

        public void Begin(PreviewContext context)
        {
            _ctx = context;
            _actionRot = context.ToolGhost != null ? context.ToolGhost.transform.rotation : Quaternion.identity;
            _elapsed = 0f;
            _guidedProgress = 0f;
            _autoAssistTimer = 0f;
            _arcSpawned = false;
            _weldBeadObj = null;
            _weldLine = null;

            // Direction from surface toward tool (for tip offset during travel).
            // Use only the horizontal + depth component so the bead stays flat on the surface.
            Vector3 toolPos = context.ToolGhost != null ? context.ToolGhost.transform.position : context.TargetWorldPos;
            _approachDir = (toolPos - context.TargetWorldPos).normalized;

            // Capture standoff once so it doesn't drift during animation
            _standoff = Vector3.Distance(toolPos, context.TargetWorldPos);

            // Weld direction: always horizontal, perpendicular to camera view.
            // This reliably produces a flat bead on the surface regardless of camera angle.
            _weldLen = context.WeldLength > 0f ? context.WeldLength : DefaultWeldLength;
            _weldDir = ComputeHorizontalWeldDir();
            float halfLen = _weldLen * 0.5f;
            _weldStart = context.TargetWorldPos - _weldDir * halfLen;
            _weldEnd = context.TargetWorldPos + _weldDir * halfLen;
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
            return Vector2.down;
        }

        public void End(bool completed)
        {
            if (_ctx.ToolGhost != null)
                MaterialHelper.SetEmission(_ctx.ToolGhost, Color.black);

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

        private void ApplyEffects(float progress)
        {
            // At 10%: arc glow + sparks
            if (!_arcSpawned && progress >= 0.1f)
            {
                _arcSpawned = true;

                if (_ctx.ToolGhost != null)
                    MaterialHelper.SetEmission(_ctx.ToolGhost, new Color(0.9f, 0.95f, 1f, 1f) * 1.5f);

                CompletionParticleEffect.TrySpawn("weld_glow",
                    _ctx.TargetWorldPos, Vector3.one * 0.06f);
            }

            // Travel progress: 0..1 across the weld seam
            float travelProgress = Mathf.InverseLerp(0.15f, 0.9f, progress);

            // ── Tool movement: smooth travel from approach landing → along weld seam ──
            if (_ctx.ToolGhost != null && progress > 0.05f && progress < 0.95f)
            {
                // Current point on the weld seam (on the surface)
                Vector3 currentWeldPoint = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);

                // Keep tool at the fixed standoff distance captured at Begin()
                Vector3 targetToolPos = currentWeldPoint + _approachDir * _standoff;

                // Smooth lerp to avoid jumps (first frame blends from approach landing)
                _ctx.ToolGhost.transform.position = Vector3.Lerp(
                    _ctx.ToolGhost.transform.position,
                    targetToolPos,
                    Time.deltaTime * 6f);

                // Slight hand wobble
                float wobble = Mathf.Sin(progress * 40f) * 0.12f;
                _ctx.ToolGhost.transform.rotation = _actionRot * Quaternion.Euler(wobble, 0f, wobble * 0.5f);
            }

            // ── Weld bead line: deposited on the surface behind the torch ──
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

            // Orient the line renderer so its local Z = surface normal (up).
            // Using world up instead of _approachDir ensures the bead ribbon lies
            // flat on the workpiece regardless of camera angle.
            _weldBeadObj.transform.rotation = Quaternion.LookRotation(Vector3.up, _weldDir);

            // Fresh weld bead: bright silver-white with metallic sheen
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
            // Bead grows from weld start toward current torch position
            Vector3 currentEnd = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);
            _weldLine.SetPosition(0, _weldStart);
            _weldLine.SetPosition(1, currentEnd);
        }

        /// <summary>
        /// Computes a horizontal weld direction perpendicular to the camera view.
        /// This always produces a flat, visible bead regardless of camera angle.
        /// </summary>
        private static Vector3 ComputeHorizontalWeldDir()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                // Camera's right vector projected to the horizontal plane
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
