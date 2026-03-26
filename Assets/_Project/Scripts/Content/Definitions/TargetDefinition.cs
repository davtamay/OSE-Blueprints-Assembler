using System;
using UnityEngine;

namespace OSE.Content
{
    [Serializable]
    public sealed class TargetDefinition
    {
        public string id;
        public string name;
        public string anchorRef;
        public string description;
        public string associatedPartId;
        public string associatedSubassemblyId;
        public string[] tags;

        /// <summary>
        /// Direction of the weld/cut line in local space (normalized).
        /// Only used by Weld and Cut profiles. Zero/absent = point target.
        /// </summary>
        public SceneFloat3 weldAxis;

        /// <summary>
        /// Length of the weld/cut line in scene units.
        /// Only used when weldAxis is set. Zero = use default (0.03).
        /// </summary>
        public float weldLength;

        /// <summary>
        /// Euler-angle override for how the tool should orient during the action phase
        /// at this target (e.g. lay flat for framing square). When set and
        /// <see cref="useToolActionRotation"/> is true, the preview system uses this
        /// instead of computing orientation from camera direction.
        /// </summary>
        public bool useToolActionRotation;
        public SceneFloat3 toolActionRotation;

        public Vector3 GetWeldAxisVector()
        {
            var v = new Vector3(weldAxis.x, weldAxis.y, weldAxis.z);
            return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.zero;
        }

        public Quaternion GetToolActionRotation()
        {
            return Quaternion.Euler(toolActionRotation.x, toolActionRotation.y, toolActionRotation.z);
        }
    }
}
