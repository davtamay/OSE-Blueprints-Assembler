using UnityEngine;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Continuous sine-based positional vibration with ramp envelope.
    /// Recreates DrillPreview's 55 Hz tremor (amp vec (1, 0.7, 0.5) *
    /// 0.0004 m, ramp in 0..0.15, ramp out 0.85..1) and CutPreview's
    /// x-axis vibration. Adds per-frame offset to the host's local
    /// position; restores baseline on Stop.
    /// </summary>
    public sealed class ToolVibrationPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "toolVibration";
        public bool IsPlaying { get; private set; }

        private AnimationCueContext _ctx;
        private float _elapsed;
        private Vector3[] _baselinePos;

        public void Start(AnimationCueContext context)
        {
            _ctx = context;
            _elapsed = 0f;
            IsPlaying = true;
            if (context.Targets != null)
            {
                _baselinePos = new Vector3[context.Targets.Count];
                for (int i = 0; i < context.Targets.Count; i++)
                    _baselinePos[i] = context.Targets[i] != null ? context.Targets[i].transform.localPosition : Vector3.zero;
            }

            // Diagnostic — if two vibration players are running simultaneously
            // on the same tool (the 2nd weld's "shakes quicker" symptom), two
            // Start lines will appear for the same target between Stop calls.
            int targetCount = context.Targets != null ? context.Targets.Count : 0;
            string firstTargetName = targetCount > 0 && context.Targets[0] != null
                ? context.Targets[0].name : "<none>";
            Vector3 baseline0 = _baselinePos != null && _baselinePos.Length > 0 ? _baselinePos[0] : Vector3.zero;
            OseLog.Info($"[WeldDiag] Vib.Start  t={Time.time:F3}  targets={targetCount}  first='{firstTargetName}'  baseline0={baseline0}  hash=0x{GetHashCode():X}");
        }

        public bool Tick(float deltaTime)
        {
            if (!IsPlaying) return false;
            _elapsed += deltaTime;
            float dur = _ctx.Duration > 0f ? _ctx.Duration : 1f;
            float t = Mathf.Clamp01(_elapsed / dur);
            Apply(t);
            if (t >= 1f) { Stop(); return false; }
            return true;
        }

        public void TickProgress(float progress01) => Apply(Mathf.Clamp01(progress01));

        public void Stop()
        {
            // Diagnostic — if Stop doesn't fire between welds, StopDuringAction
            // failed to reach this player, and the prior vibration keeps
            // running on top of the new one (visible as accumulating shake).
            // The hash lets us pair a Stop with its Start.
            int targetCount = _ctx.Targets != null ? _ctx.Targets.Count : 0;
            string firstTargetName = targetCount > 0 && _ctx.Targets[0] != null
                ? _ctx.Targets[0].name : "<none>";
            Vector3 baseline0 = _baselinePos != null && _baselinePos.Length > 0 ? _baselinePos[0] : Vector3.zero;
            OseLog.Info($"[WeldDiag] Vib.Stop   t={Time.time:F3}  targets={targetCount}  first='{firstTargetName}'  baseline0={baseline0}  hash=0x{GetHashCode():X}  wasPlaying={IsPlaying}");

            if (_ctx.Targets != null && _baselinePos != null)
            {
                for (int i = 0; i < _ctx.Targets.Count && i < _baselinePos.Length; i++)
                {
                    var go = _ctx.Targets[i];
                    if (go != null) go.transform.localPosition = _baselinePos[i];
                }
            }
            IsPlaying = false;
        }

        private void Apply(float progress01)
        {
            var e = _ctx.Entry;
            Vector3 amp = new Vector3(
                e.vibrationAxes.x != 0f ? e.vibrationAxes.x : 0.0004f,
                e.vibrationAxes.y != 0f ? e.vibrationAxes.y : 0.00028f,
                e.vibrationAxes.z != 0f ? e.vibrationAxes.z : 0.0002f);
            float freq = e.vibrationFrequency > 0f ? e.vibrationFrequency : 55f;
            float rampIn  = e.vibrationRampIn  > 0f ? e.vibrationRampIn  : 0.15f;
            float rampOut = e.vibrationRampOut > 0f ? e.vibrationRampOut : 0.85f;

            // Smoothstep envelope: ease in over [0, rampIn], hold at 1,
            // ease out over [rampOut, 1].
            float env = 1f;
            if (progress01 < rampIn && rampIn > 0f)
                env = Mathf.SmoothStep(0f, 1f, progress01 / rampIn);
            else if (progress01 > rampOut && rampOut < 1f)
                env = Mathf.SmoothStep(1f, 0f, (progress01 - rampOut) / (1f - rampOut));

            // Three decorrelated sines so the motion looks organic rather
            // than a single-axis buzz.
            float phase = progress01 * freq;
            Vector3 offset = new Vector3(
                Mathf.Sin(phase)         * amp.x,
                Mathf.Sin(phase * 1.3f + 1.7f) * amp.y,
                Mathf.Sin(phase * 0.9f + 3.1f) * amp.z) * env;

            if (_ctx.Targets == null || _baselinePos == null) return;
            for (int i = 0; i < _ctx.Targets.Count && i < _baselinePos.Length; i++)
            {
                var go = _ctx.Targets[i];
                if (go != null) go.transform.localPosition = _baselinePos[i] + offset;
            }
        }
    }
}
