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
        /// Optional initial presentation scale for the entire preview assembly.
        /// `1.0` means authored size. The runtime UI reports scale relative to this
        /// authored baseline, not relative to the package's chosen default.
        /// </summary>
        public float defaultAssemblyScaleMultiplier = 1f;

        /// <summary>
        /// Indicates the rotation format stored in <see cref="TargetPreviewPlacement.rotation"/>
        /// and <see cref="TargetDefinition.toolActionRotation"/>.
        /// <c>"mesh"</c> = direct mesh rotation (no gripRotation correction at use-time).
        /// Null/empty = legacy approach-vector format (gripRotation correction still applied).
        /// Set automatically by ToolTargetAuthoringWindow after migration.
        /// </summary>
        public string targetRotationFormat;

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

        /// <summary>
        /// Authored fabrication-space reference frames for completed subassemblies that
        /// later become learner-placeable units during stacking/integration steps.
        /// </summary>
        public SubassemblyPreviewPlacement[] subassemblyPlacements;

        /// <summary>
        /// Optional constrained-fit payloads for completed subassemblies that need one
        /// or more member parts to slide along a single axis while the subassembly root
        /// remains anchored to a target pose.
        /// </summary>
        public ConstrainedSubassemblyFitPreviewPlacement[] constrainedSubassemblyFitPlacements;

        /// <summary>
        /// Optional parking frames for finished subassemblies that should persist in the
        /// scene after fabrication, but should no longer occupy the active fabrication bay.
        /// Used to keep one near-camera working area while preserving visible progress.
        /// </summary>
        public SubassemblyPreviewPlacement[] completedSubassemblyParkingPlacements;

        /// <summary>
        /// Optional canonical integrated member poses for completed subassemblies after
        /// they are placed into a later assembly target. This allows stacking to teach
        /// panel movement while the final visible machine uses explicit non-overlapping
        /// member poses.
        /// </summary>
        public IntegratedSubassemblyPreviewPlacement[] integratedSubassemblyPlacements;
    }

    /// <summary>
    /// Preview scene placement for a single part.
    /// Defines its start transform (before the step, floating nearby) and assembled transform
    /// (after step completion, assembled into final position). The assembled transform represents
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

        // Assembled state — final position once placed; populated from the current preview/layout authoring pipeline
        public SceneFloat3    assembledPosition;
        public SceneQuaternion assembledRotation;
        public SceneFloat3    assembledScale;

        /// <summary>
        /// Optional step-scoped intermediate poses. After completing step X,
        /// a part with a matching entry moves to that pose instead of assembledPosition.
        /// Null/empty = legacy two-pose behavior (start → assembled).
        /// The sequence is: start → stepPoses[0] → stepPoses[1] → ... → assembled.
        /// </summary>
        public StepPoseEntry[] stepPoses;

        /// <summary>
        /// Optional spline path data. When present, the spawner creates a tube mesh
        /// via SplineContainer + SplineExtrude instead of loading a GLB asset.
        /// Used for hoses, cables, and other tubular parts.
        /// </summary>
        public SplinePathDefinition splinePath;
    }

    /// <summary>
    /// Step-scoped pose for a part. After the referenced step completes,
    /// the part occupies this pose instead of assembledPosition.
    /// </summary>
    [Serializable]
    public sealed class StepPoseEntry
    {
        /// <summary>Step ID this pose applies to (the anchor).</summary>
        public string stepId;
        /// <summary>Optional display label for the editor UI.</summary>
        public string label;
        public SceneFloat3 position;
        public SceneQuaternion rotation;
        public SceneFloat3 scale;
        /// <summary>
        /// Optional earliest step ID this pose covers (inclusive). Empty = open-ended
        /// backward (the pose applies to every step from the start of the package
        /// up to <see cref="propagateThroughStep"/> or the next-winning entry).
        /// </summary>
        public string propagateFromStep;
        /// <summary>
        /// Optional latest step ID this pose covers (inclusive). Empty = open-ended
        /// forward (the pose applies until another entry wins or assembledPosition
        /// takes over). With both bounds empty, behaves exactly like pre-propagation
        /// content: anchored to <see cref="stepId"/> with forward fall-through.
        /// </summary>
        public string propagateThroughStep;
    }

    /// <summary>
    /// Defines a tubular spline path for procedural hose/cable rendering.
    /// Knot positions are in PreviewRoot local space (same coordinate system as assembledPosition/startPosition).
    /// </summary>
    [Serializable]
    public sealed class SplinePathDefinition
    {
        /// <summary>Tube radius in meters (e.g. 0.0095 for 19mm OD hose).</summary>
        public float radius;
        /// <summary>Number of radial segments for the tube cross-section.</summary>
        public int segments;
        /// <summary>PBR metallic value (0 = rubber/plastic, 0.8 = braided steel).</summary>
        public float metallic;
        /// <summary>PBR smoothness value (0 = rough rubber, 0.5 = polished metal).</summary>
        public float smoothness;
        /// <summary>
        /// Optional RGBA wire/hose color. When alpha &gt; 0, overrides the hardcoded
        /// default in ConnectStepHandler and SplinePartFactory callers.
        /// Set to (1,0,0,1) for red wires, (0,0,0,1) for black wires, etc.
        /// </summary>
        public SceneFloat4 color;
        /// <summary>Ordered spline control points in PreviewRoot local space.</summary>
        public SceneFloat3[] knots;
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
        /// <summary>World-space port A position for pipe_connection steps (e.g. one end of a hose).</summary>
        public SceneFloat3 portA;
        /// <summary>World-space port B position for pipe_connection steps (e.g. other end of a hose).</summary>
        public SceneFloat3 portB;
    }

    /// <summary>
    /// Authored reference frame for a completed subassembly in its fabrication pose.
    /// Member-part local offsets are derived from this frame and the parts' assembled transforms.
    /// </summary>
    [Serializable]
    public sealed class SubassemblyPreviewPlacement
    {
        public string subassemblyId;

        /// <summary>Legacy single-position field. Prefer startPosition for new content.</summary>
        public SceneFloat3 position;
        public SceneQuaternion rotation;
        public SceneFloat3 scale;

        // ── Pose fields (mirrors PartPreviewPlacement) ────────────────────
        // Start state — where the group floats before placement
        public SceneFloat3     startPosition;
        public SceneQuaternion startRotation;
        public SceneFloat3     startScale;

        // Assembled state — where the group lands after placement
        public SceneFloat3     assembledPosition;
        public SceneQuaternion assembledRotation;
        public SceneFloat3     assembledScale;

        /// <summary>
        /// Optional step-scoped intermediate poses (same pattern as parts).
        /// </summary>
        public StepPoseEntry[] stepPoses;
    }

    /// <summary>
    /// Constrained-fit preview payload for a finished subassembly that must remain
    /// anchored to a target pose while a driven member subset slides along a single
    /// authored local axis.
    /// </summary>
    [Serializable]
    public sealed class ConstrainedSubassemblyFitPreviewPlacement
    {
        public string subassemblyId;
        public string targetId;
        public SceneFloat3 fitAxisLocal;
        public float minTravel;
        public float maxTravel;
        public float completionTravel;
        public float snapTolerance;
        public string[] drivenPartIds;
    }

    /// <summary>
    /// Canonical post-placement member poses for a subassembly when committed to a
    /// specific target. Positions, rotations, and scales are authored in PreviewRoot
    /// local space.
    /// </summary>
    [Serializable]
    public sealed class IntegratedSubassemblyPreviewPlacement
    {
        public string subassemblyId;
        public string targetId;
        public IntegratedMemberPreviewPlacement[] memberPlacements;
    }

    /// <summary>
    /// Canonical integrated pose for a single member part of a completed subassembly.
    /// </summary>
    [Serializable]
    public sealed class IntegratedMemberPreviewPlacement
    {
        public string partId;
        public SceneFloat3 position;
        public SceneQuaternion rotation;
        public SceneFloat3 scale;
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
    /// Populated automatically by the active preview/layout import pipeline.
    /// </summary>
    [Serializable]
    public struct SceneQuaternion
    {
        public float x, y, z, w;

        public bool IsIdentity => x == 0f && y == 0f && z == 0f && w == 0f || (x == 0f && y == 0f && z == 0f && w == 1f);
    }
}
