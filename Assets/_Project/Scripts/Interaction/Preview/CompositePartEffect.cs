using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Delegates <see cref="IPartEffect"/> lifecycle to multiple effects,
    /// summing their displacement vectors. Use when a single tool action
    /// needs to drive several part transforms or combine move + glow + particles.
    /// </summary>
    internal sealed class CompositePartEffect : IPartEffect
    {
        private readonly IPartEffect[] _effects;

        public CompositePartEffect(IPartEffect[] effects)
        {
            _effects = effects;
        }

        public void Begin()
        {
            for (int i = 0; i < _effects.Length; i++)
                _effects[i].Begin();
        }

        public Vector3 Apply(float progress)
        {
            Vector3 total = Vector3.zero;
            for (int i = 0; i < _effects.Length; i++)
                total += _effects[i].Apply(progress);
            return total;
        }

        public void End()
        {
            for (int i = 0; i < _effects.Length; i++)
                _effects[i].End();
        }
    }
}
