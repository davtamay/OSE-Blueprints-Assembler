using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Attached to every rendered wire spline GO by <see cref="SplinePartFactory"/>.
    /// Identifies the GO as a completed-wire visual so the selection system can
    /// distinguish it from regular spawned parts and show wire-specific info.
    /// </summary>
    public sealed class WireSplineMarker : MonoBehaviour
    {
        /// <summary>The target ID this wire connects (e.g. "target_wire_psu_12v").</summary>
        public string targetId;

        /// <summary>The step ID that owns this wire (e.g. "step_connect_psu_12v").</summary>
        public string stepId;
    }
}
