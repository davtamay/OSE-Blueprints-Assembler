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

        public Vector3 GetWeldAxisVector()
        {
            var v = new Vector3(weldAxis.x, weldAxis.y, weldAxis.z);
            return v.sqrMagnitude > 0.001f ? v.normalized : Vector3.zero;
        }
    }
}
