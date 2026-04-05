using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Abstraction for the visual drawn between two anchor points during an
    /// <see cref="AnchorToAnchorInteraction"/>. Implementations decide the look:
    /// thin measurement line, thick cable, spline-extruded mesh, physics rope, etc.
    /// </summary>
    internal interface IAnchorLineVisual
    {
        /// <summary>Update both endpoints of the visual.</summary>
        void SetEndpoints(Vector3 start, Vector3 end);

        /// <summary>
        /// Set a text label (distance readout, cable name, etc.).
        /// Implementations that don't need labels can no-op.
        /// </summary>
        void SetLabel(string text);

        /// <summary>Destroy all GameObjects owned by this visual.</summary>
        void Cleanup();
    }
}
