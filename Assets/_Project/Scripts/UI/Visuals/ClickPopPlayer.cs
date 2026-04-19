using UnityEngine;
using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// One-shot ring-pulse effect wrapping <see cref="ToolActionClickEffect.Spawn"/>.
    /// Fires once at Start (or at <c>startProgress</c> when progress-ranged)
    /// and self-completes. Recreates the completion ring that fires when a
    /// tool-target tap lands.
    /// </summary>
    public sealed class ClickPopPlayer : IAnimationCuePlayer
    {
        public string AnimationType => "clickPop";
        public bool IsPlaying { get; private set; }

        public void Start(AnimationCueContext context)
        {
            IsPlaying = true;
            var e = context.Entry;
            Color color = e.fromColor.a > 0f
                ? new Color(e.fromColor.r, e.fromColor.g, e.fromColor.b, e.fromColor.a)
                : new Color(0.2f, 1.0f, 0.4f, 0.9f);
            float pulse = e.pulseScale > 0f ? e.pulseScale : 1.8f;

            if (context.Targets != null)
            {
                for (int i = 0; i < context.Targets.Count; i++)
                {
                    var go = context.Targets[i];
                    if (go == null) continue;
                    ToolActionClickEffect.Spawn(go.transform.position, go.transform.localScale, color, pulse);
                }
            }

            // Burst — no further frames needed. IsPlaying stays true for one
            // tick so the coordinator's Update sees the cue once; then Stop().
            IsPlaying = false;
        }

        public bool Tick(float deltaTime) => false;
        public void Stop() { IsPlaying = false; }
    }
}
