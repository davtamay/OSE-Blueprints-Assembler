using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Component attached to spawned tool-action target spheres.
    /// Stores per-target metadata used by <see cref="UseStepHandler"/> for
    /// hit detection, tool pose resolution, and weld-line rendering.
    /// </summary>
    internal sealed class ToolActionTargetInfo : MonoBehaviour
    {
        public string TargetId;
        public string RequiredToolId;
        public Vector3 BaseScale;
        public Vector3 BaseLocalPosition;
        /// <summary>World position of the actual action point on the surface (before sphere lift).</summary>
        public Vector3 SurfaceWorldPos;
        /// <summary>Vertical distance the sphere was lifted above the surface for click-ability.</summary>
        public float MarkerLift;
        /// <summary>World rotation of the authored target marker before it is lifted for clickability.</summary>
        public Quaternion TargetWorldRotation;
        /// <summary>Direction of the weld line in world space (normalized). Zero = point target.</summary>
        public Vector3 WeldAxis;
        /// <summary>Length of the weld line in scene units.</summary>
        public float WeldLength;
        /// <summary>When true, use <see cref="ToolActionRotation"/> instead of computing orientation from camera.</summary>
        public bool HasToolActionRotation;
        /// <summary>Authored tool orientation at this target (world-space Euler angles).</summary>
        public Quaternion ToolActionRotation;
    }
}
