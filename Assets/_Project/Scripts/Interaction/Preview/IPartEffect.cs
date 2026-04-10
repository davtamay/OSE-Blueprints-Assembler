using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Progress-driven visual effect on a part during the tool action phase.
    /// Created at the bridge layer (OSE.UI) where part references are available,
    /// passed through <see cref="ToolActionContext"/> as an opaque callback.
    /// The preview system calls <see cref="Apply"/> each frame but never
    /// inspects the effect's internals.
    /// </summary>
    public interface IPartEffect
    {
        /// <summary>Called once when the action phase begins. Snaps part to start pose.</summary>
        void Begin();

        /// <summary>
        /// Called every frame with normalized progress [0..1].
        /// Returns the world-space displacement applied to the part this frame,
        /// so the tool preview can follow the part's movement.
        /// </summary>
        Vector3 Apply(float progress);

        /// <summary>Called when the action phase ends. Snaps to final pose.</summary>
        void End();
    }
}
