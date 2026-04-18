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
        /// Euler-angle override (X, Y, Z) for the tool preview cursor orientation.
        /// Only used when <see cref="useOrientationOverride"/> is true.
        /// </summary>
        public Vector3 orientationEuler;

        /// <summary>
        /// Multiplier applied on top of <c>ToolCursorUniformScale</c> for this
        /// tool's preview indicator. 0 or 1 = no override (use default scale).
        /// </summary>
        public float scaleOverride;

        /// <summary>
        /// When true, this tool remains on the workpiece after placement
        /// (e.g. clamps, fixtures, vises). Checked by <c>ToolActionProfiles.IsToolPersistent</c>.
        /// </summary>
        public bool persistent;

        /// <summary>
        /// Spatial metadata: grip point, tip point, action axis, and XR hand hints.
        /// Auto-detected by analyze_tool_pose.py, refined via ToolPoseGizmoEditor.
        /// When present, takes priority over <see cref="orientationEuler"/>.
        /// </summary>
        public ToolPoseConfig toolPose;

        /// <summary>
        /// Default <c>actionType</c> inherited by new tool-target tasks that use this tool
        /// (drill→"drill", framing square→"measure", wrench→"tighten", etc.). Authors can
        /// still override per step for off-label use. Null means "no default — author picks".
        /// </summary>
        public string primaryActionType;

        public bool HasOrientationOverride => useOrientationOverride;

        /// <summary>
        /// True when <see cref="toolPose"/> carries any authored spatial data.
        /// </summary>
        public bool HasToolPose => toolPose != null
            && (toolPose.HasGripPoint || toolPose.HasTipPoint);

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
