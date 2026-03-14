using System;

namespace OSE.Content
{
    /// <summary>
    /// Optional per-package visual configuration for the 3D preview test scene.
    /// Carries where parts and targets appear in the preview scene — their positions,
    /// scales, and representative colors. This is authoring and presentation data,
    /// not game content truth.
    ///
    /// Lives inside machine.json so every streamable package knows how to configure
    /// the scene harness without any separate ScriptableObject dependency.
    /// </summary>
    [Serializable]
    public sealed class PackagePreviewConfig
    {
        /// <summary>
        /// Per-part visual placement overrides for the preview scene.
        /// The harness matches entries by partId to position and color each part.
        /// </summary>
        public PartPreviewPlacement[] partPlacements;

        /// <summary>
        /// Per-target visual placement overrides for the preview scene.
        /// The harness matches entries by targetId to position and color each target marker.
        /// </summary>
        public TargetPreviewPlacement[] targetPlacements;
    }

    /// <summary>
    /// Preview scene placement for a single part.
    /// Defines its start transform (before the step, floating nearby) and play transform
    /// (after step completion, assembled into final position). The play transform represents
    /// the canonical assembled position and is the target for the Blender layout workflow.
    /// </summary>
    [Serializable]
    public sealed class PartPreviewPlacement
    {
        public string partId;

        // Start state — where the part floats before the step
        public SceneFloat3    startPosition;
        public SceneQuaternion startRotation;
        public SceneFloat3    startScale;
        public SceneFloat4    color;

        // Play state — assembled position; populated from Blender layout GLB or scene capture
        public SceneFloat3    playPosition;
        public SceneQuaternion playRotation;
        public SceneFloat3    playScale;
    }

    /// <summary>
    /// Preview scene placement for a single placement target marker.
    /// Represents the final assembled slot position and orientation.
    /// </summary>
    [Serializable]
    public sealed class TargetPreviewPlacement
    {
        public string targetId;
        public SceneFloat3    position;
        public SceneQuaternion rotation;
        public SceneFloat3    scale;
        public SceneFloat4    color;
    }

    /// <summary>
    /// Engine-free 3-float vector for JSON-serializable positions and scales.
    /// Convert to UnityEngine.Vector3 at the scene boundary.
    /// </summary>
    [Serializable]
    public struct SceneFloat3
    {
        public float x, y, z;
    }

    /// <summary>
    /// Engine-free 4-float color for JSON-serializable RGBA values.
    /// Convert to UnityEngine.Color at the scene boundary.
    /// </summary>
    [Serializable]
    public struct SceneFloat4
    {
        public float r, g, b, a;
    }

    /// <summary>
    /// Engine-free quaternion for JSON-serializable rotations.
    /// Identity = { x:0, y:0, z:0, w:1 }.
    /// Convert to UnityEngine.Quaternion at the scene boundary.
    /// Populated automatically by PackageAssetPostprocessor when a layout GLB/FBX is imported,
    /// or manually via the SessionDriver "Capture from Scene" buttons.
    /// </summary>
    [Serializable]
    public struct SceneQuaternion
    {
        public float x, y, z, w;

        public bool IsIdentity => x == 0f && y == 0f && z == 0f && w == 0f || (x == 0f && y == 0f && z == 0f && w == 1f);
    }
}
