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
        public string associatedPartGroupId;
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

        /// <summary>
        /// When true, the runtime resolves this target's position by transforming
        /// <see cref="localOffsetFromPart"/> through the live <see cref="associatedPartId"/>
        /// part's transform — so the marker tracks the part through pose changes
        /// (group rotation, stepPose, etc.). Preferred over <see cref="anchorRef"/>
        /// for anchor-tracking targets; <see cref="anchorRef"/> path-style values
        /// don't resolve to live transforms and silently fall back to bake-time
        /// static placement, which goes stale the moment the part is re-posed.
        /// Rotation and scale still come from the static target placement
        /// (authored intent for tool approach).
        /// </summary>
        public bool useLocalOffsetFromPart;
        public SceneFloat3 localOffsetFromPart;

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
