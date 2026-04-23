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
        public override float Duration => Override(Cfg?.duration ?? 0f, 1.5f);

        protected override float GuidedDragScale => 0.004f;
        protected override float AutoAssistDelay => 3f;
        protected override float AutoAssistRate => 0.4f;

        private Quaternion _actionRot;
        private GameObject _weldBeadObj;
        private LineRenderer _weldLine;
        private bool _arcSpawned;
        private GameObject _arcEffect;

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
            _arcEffect = null;
            _weldBeadObj = null;
            _weldLine = null;

            // Diagnostic — confirm Begin captures a clean _actionRot each weld.
            // If this value compounds across welds (non-identity + wobble-like
            // non-zero pitch/yaw), the prior weld's End() never ran or failed
            // to restore the rotation. t = Time.time in seconds.
            OseLog.Info($"[WeldDiag] Begin  t={Time.time:F3}  target='{context.TargetId}'  _actionRot.euler={_actionRot.eulerAngles}  toolName='{(context.ToolPreview != null ? context.ToolPreview.name : "null")}'");

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
            // Diagnostic — confirm End() is actually reached AND what state it sees.
            // If duplicate-particle/compound-shake persists after my fixes, either
            // (a) this line doesn't fire between welds (End never called), or
            // (b) it fires but _arcEffect is already null (prior arc leaked
            // somewhere else), or (c) the tool rotation at entry contains
            // accumulated wobble from prior weld (the restore at line 94
            // is necessary).
            string rotAtEntry = _ctx.ToolPreview != null
                ? _ctx.ToolPreview.transform.rotation.eulerAngles.ToString()
                : "<no-tool>";
            OseLog.Info($"[WeldDiag] End    t={Time.time:F3}  completed={completed}  _arcEffect={( _arcEffect != null ? "ALIVE" : "null")}  _weldBeadObj={( _weldBeadObj != null ? "ALIVE" : "null")}  tool.rotation={rotAtEntry}  _actionRot={_actionRot.eulerAngles}");

            // Stop the continuous weld arc particles. User reported that
            // successive welds on the same step showed "duplicate particle
            // with offset" — root cause: soft-stop (StopEmitting) lets
            // in-flight particles keep rendering for ~1 s via
            // ParticleAutoDestroy. Starting weld #N+1 before weld #N's
            // particles fade gives two visible arcs at once (one at the
            // previous anchor, one at the new). Hard-destroy the GO instead
            // so each weld starts visually clean; the abrupt cut is
            // acceptable for editor tool previews.
            if (_arcEffect != null)
            {
                if (Application.isPlaying) Object.Destroy(_arcEffect);
                else                       Object.DestroyImmediate(_arcEffect);
                _arcEffect = null;
            }

            // Restore tool rotation to the captured base so subsequent welds
            // don't compound wobble. User reported that successive welds
            // shook progressively more — root cause: line 138 writes
            // `_actionRot * Quaternion.Euler(wobble)` absolutely to the tool
            // rotation, but End() never reset it. The next Begin() captured
            // the still-wobbled rotation as its new _actionRot, and that
            // weld's wobble applied on top. Linear growth per weld. Restoring
            // to _actionRot here gives each Begin() a clean baseline.
            // (_ctx is a struct — check its ToolPreview field directly; the
            // Unity-managed null check on GameObject stays correct.)
            if (_ctx.ToolPreview != null)
            {
                _ctx.ToolPreview.transform.rotation = _actionRot;
                MaterialHelper.SetEmission(_ctx.ToolPreview, Color.black);
            }

            if (_weldBeadObj != null && !completed)
            {
                Object.Destroy(_weldBeadObj);
                _weldBeadObj = null;
            }
            else if (_weldBeadObj != null)
            {
                float coolDur   = Override(Cfg?.weldCoolerDuration ?? 0f, 2f);
                Color hotColor  = Override(Cfg?.weldBeadHotColor  ?? default, new Color(0.85f, 0.82f, 0.72f, 1f));
                Color coolColor = Override(Cfg?.weldBeadCoolColor ?? default, new Color(0.55f, 0.55f, 0.52f, 1f));
                var fader = _weldBeadObj.AddComponent<WeldBeadCooler>();
                fader.Init(coolDur, hotColor, coolColor);
            }
        }

        protected override void ApplyEffects(float progress)
        {
            // All tunable values read through Override() — when the active
            // ToolActionDefinition carries a ToolActionPreviewConfig,
            // authored values win; otherwise the historical constants
            // (0.1 arc, 0.2 bead, 40 rad/s wobble, 0.12 rad amp) apply.
            float arcThreshold  = Override(Cfg?.weldArcSpawnThreshold ?? 0f, 0.1f);
            float beadThreshold = Override(Cfg?.weldBeadSpawnThreshold ?? 0f, 0.2f);
            float travelStart   = Override(Cfg?.weldBeadWindowStart ?? 0f, 0.15f);
            float travelEnd     = Override(Cfg?.weldBeadWindowEnd ?? 0f, 0.9f);
            float wobbleAmp     = Override(Cfg?.weldWobbleAmplitude ?? 0f, 0.12f);
            float wobbleFreq    = Override(Cfg?.weldWobbleFrequency ?? 0f, 40f);

            // At arc threshold: start continuous weld arc sparks that track the torch.
            if (!_arcSpawned && progress >= arcThreshold)
            {
                _arcSpawned = true;

                if (_ctx.ToolPreview != null)
                    MaterialHelper.SetEmission(_ctx.ToolPreview, new Color(0.9f, 0.95f, 1f, 1f) * 1.5f);

                _arcEffect = CompletionParticleEffect.TrySpawnContinuous("weld_arc",
                    _ctx.TargetWorldPos, Vector3.one * 0.06f);
            }

            float travelProgress = Mathf.InverseLerp(travelStart, travelEnd, progress);

            // Move the arc effect to follow the current weld point
            if (_arcEffect != null)
            {
                Vector3 currentWeldPoint = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);
                _arcEffect.transform.position = currentWeldPoint;
            }

            // Tool movement: smooth travel along weld seam
            if (_ctx.ToolPreview != null && progress > 0.05f && progress < 0.95f)
            {
                Vector3 currentWeldPoint = Vector3.Lerp(_weldStart, _weldEnd, travelProgress);
                Vector3 targetToolPos = currentWeldPoint + _approachDir * _standoff;

                _ctx.ToolPreview.transform.position = Vector3.Lerp(
                    _ctx.ToolPreview.transform.position,
                    targetToolPos,
                    Time.deltaTime * 6f);

                float wobble = Mathf.Sin(progress * wobbleFreq) * wobbleAmp;
                _ctx.ToolPreview.transform.rotation = _actionRot * Quaternion.Euler(wobble, 0f, wobble * 0.5f);
            }

            // Weld bead line
            if (_weldBeadObj == null && progress >= beadThreshold)
                SpawnWeldLine();

            if (_weldLine != null)
                UpdateWeldLine(travelProgress);
        }

        private void SpawnWeldLine()
        {
            _weldBeadObj = new GameObject("WeldBeadLine");
            _weldBeadObj.transform.position = _ctx.TargetWorldPos;

            float width = Override(Cfg?.weldBeadWidth ?? 0f, BeadWidth);
            _weldLine = _weldBeadObj.AddComponent<LineRenderer>();
            _weldLine.useWorldSpace = true;
            _weldLine.positionCount = 2;
            _weldLine.startWidth = width;
            _weldLine.endWidth = width;
            _weldLine.numCapVertices = 4;
            _weldLine.alignment = LineAlignment.TransformZ;
            _weldLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _weldLine.receiveShadows = false;

            _weldBeadObj.transform.rotation = Quaternion.LookRotation(Vector3.up, _weldDir);

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
            Color hotColor = Override(Cfg?.weldBeadHotColor ?? default, new Color(0.85f, 0.82f, 0.72f, 1f));
            var mat = new Material(shader);
            mat.color = hotColor;
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
            Camera cam = CameraUtil.GetMain();
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

        private static readonly Color DefaultHotColor = new Color(0.85f, 0.82f, 0.72f, 1f);
        private static readonly Color DefaultCoolColor = new Color(0.55f, 0.55f, 0.52f, 1f);

        private Color _hotColor = DefaultHotColor;
        private Color _coolColor = DefaultCoolColor;

        public void Init(float duration)
        {
            _duration = duration;
            _renderer = GetComponent<Renderer>();
            if (_renderer != null)
                _material = _renderer.material;
        }

        public void Init(float duration, Color hotColor, Color coolColor)
        {
            Init(duration);
            _hotColor = hotColor;
            _coolColor = coolColor;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);

            if (_material != null)
            {
                _material.color = Color.Lerp(_hotColor, _coolColor, t);

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
