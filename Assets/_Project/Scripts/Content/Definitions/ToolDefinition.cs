using System;
using UnityEngine;

namespace OSE.Content
{
    [Serializable]
    public sealed class ToolDefinition
    {
        public string id;
        public string name;
        public string category;
        public string purpose;
        public string usageNotes;
        public string safetyNotes;
        public string[] searchTerms;
        public string assetRef;

        /// <summary>
        /// Set to true to use <see cref="orientationEuler"/> instead of automatic
        /// shaft-detection in <c>ComputeUprightCorrection</c>.
        /// </summary>
        public bool useOrientationOverride;

        /// <summary>
        /// Euler-angle override (X, Y, Z) for the tool ghost cursor orientation.
        /// Only used when <see cref="useOrientationOverride"/> is true.
        /// </summary>
        public Vector3 orientationEuler;

        /// <summary>
        /// Multiplier applied on top of <c>ToolCursorUniformScale</c> for this
        /// tool's ghost indicator. 0 or 1 = no override (use default scale).
        /// </summary>
        public float scaleOverride;

        public bool HasOrientationOverride => useOrientationOverride;

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            return "Unnamed Tool";
        }
    }
}
