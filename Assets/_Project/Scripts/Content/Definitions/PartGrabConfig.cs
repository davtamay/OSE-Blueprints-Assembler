using System;
using UnityEngine;

namespace OSE.Content
{
    /// <summary>
    /// Spatial metadata for grabbing a part in XR. Lighter than <see cref="ToolPoseConfig"/>
    /// — parts only need a grip point and orientation, not tip/action axes.
    ///
    /// Authored in machine.json per part, auto-detected by PCA or refined via the Pose Editor.
    /// All positions/directions are in the GLB model's local space.
    /// </summary>
    [Serializable]
    public sealed class PartGrabConfig
    {
        /// <summary>Local offset from mesh origin to the natural grab point.</summary>
        public SceneFloat3 gripPoint;

        /// <summary>Euler correction so the part is held in a natural orientation.</summary>
        public SceneFloat3 gripRotation;

        /// <summary>"right", "left", or "either" — preferred hand for XR grab.</summary>
        public string handedness;

        /// <summary>"power_grip", "pinch", or "two_hand" — drives XR hand animation.</summary>
        public string poseHint;

        // ── Presence checks ──

        public bool HasGripPoint =>
            gripPoint.x != 0f || gripPoint.y != 0f || gripPoint.z != 0f;

        public bool HasGripRotation =>
            gripRotation.x != 0f || gripRotation.y != 0f || gripRotation.z != 0f;

        // ── Conversion helpers ──

        public Vector3 GetGripPoint() => new(gripPoint.x, gripPoint.y, gripPoint.z);
        public Quaternion GetGripRotation() => Quaternion.Euler(gripRotation.x, gripRotation.y, gripRotation.z);
    }
}
