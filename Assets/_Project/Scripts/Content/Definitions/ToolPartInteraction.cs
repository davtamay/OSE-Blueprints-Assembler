using System;

namespace OSE.Content
{
    /// <summary>
    /// Authored description of HOW a tool action drives a part between its
    /// start/end pose. The what (endpoint poses) lives on
    /// <see cref="PartDefinition.stepPoses"/>; this payload only shapes the
    /// motion (archetype, axis, distance, rotation, curve).
    ///
    /// Nullable on <see cref="ToolActionDefinition"/>. A null payload means
    /// "use the default lerp archetype with axis auto-derived from the pose
    /// delta" — the same behavior as before this field existed.
    ///
    /// Do not add per-tool branches here (drill-only fields, weld-only fields).
    /// Each archetype picks up only the fields it needs; unused fields are
    /// inert at runtime and hidden in the authoring UI by
    /// <c>ArchetypeFieldProfile</c>.
    /// </summary>
    [Serializable]
    public sealed class ToolPartInteraction
    {
        /// <summary>
        /// Archetype key (e.g. "lerp", "thread_in", "axis_plunge"). See
        /// <c>OSE.Interaction.PartEffectArchetypes</c>. Empty/null ≡ "lerp".
        /// </summary>
        public string archetype;

        /// <summary>
        /// Optional motion axis. When null, archetypes that need an axis
        /// auto-derive it from the start→end pose delta (valid for lerp and
        /// axis_plunge; thread_in must author this explicitly because the
        /// rotation axis need not match position delta).
        /// </summary>
        public AxisSpec axis;

        /// <summary>
        /// Translation distance in meters along <see cref="axis"/>. 0 = auto
        /// (derived from the start→end pose delta). Used by axis_plunge,
        /// thread_in.
        /// </summary>
        public float distance;

        /// <summary>
        /// Total rotation (degrees) about <see cref="axis"/> across the action.
        /// Used by rotate_in_place and thread_in when the author prefers a
        /// fixed turn count over a thread-pitch ratio.
        /// </summary>
        public float totalRotationsDeg;

        /// <summary>
        /// Thread pitch: degrees of rotation per meter of axial travel. Used by
        /// thread_in. Set this OR <see cref="totalRotationsDeg"/>, not both —
        /// when both are present, <see cref="rotationDegPerUnit"/> wins.
        /// </summary>
        public float rotationDegPerUnit;

        /// <summary>
        /// When true (default), the tool transform tracks the part's world-space
        /// displacement each frame. Set false for effects like <c>clamp_hold</c>
        /// where the tool should stay put even as the part is adjusted.
        /// </summary>
        public bool followPart = true;

        /// <summary>
        /// Easing applied to the normalized progress before motion is computed.
        /// Canonical values: <c>"linear"</c> (default), <c>"smoothStep"</c>,
        /// <c>"easeIn"</c>, <c>"easeOut"</c>, <c>"easeInOut"</c>. Matches the
        /// vocabulary used by <c>AnimationCueEntry.easing</c>.
        /// </summary>
        public string easing;

        /// <summary>
        /// Authored end pose for the part when this tool action completes.
        /// Token grammar (see <see cref="ToPoseTokens"/>):
        /// <list type="bullet">
        ///   <item><c>null</c> / empty / <c>"auto"</c> — today's implicit resolution
        ///         (<c>stepPoses[currentStepId]</c> → <c>assembledPosition</c>).</item>
        ///   <item><c>"start"</c> — <see cref="PartPreviewPlacement.startPosition"/>.</item>
        ///   <item><c>"assembled"</c> — <see cref="PartPreviewPlacement.assembledPosition"/>.</item>
        ///   <item><c>"step:&lt;stepId&gt;"</c> — named <see cref="StepPoseEntry"/>.</item>
        /// </list>
        /// No <c>fromPose</c> counterpart exists by design — start pose is always the part's
        /// current transform at action-fire (i.e. wherever the previous task left it). See the
        /// pose-chain invariant in the approved plan.
        /// </summary>
        public string toPose;
    }

    /// <summary>Canonical tokens for <see cref="ToolPartInteraction.toPose"/>.</summary>
    public static class ToPoseTokens
    {
        public const string Auto      = "auto";
        public const string Start     = "start";
        public const string Assembled = "assembled";
        public const string StepPrefix = "step:";

        public static bool IsAuto(string token) =>
            string.IsNullOrEmpty(token) || token == Auto;
    }

    /// <summary>
    /// Describes a motion axis in a coordinate frame. String-typed space so
    /// future frames (e.g. "subassembly_local") add without breaking the enum.
    /// </summary>
    [Serializable]
    public sealed class AxisSpec
    {
        /// <summary>Known values: "part_local", "target_local", "world", "tool_action_axis".</summary>
        public string space;

        /// <summary>Unit vector in the chosen <see cref="space"/>. Normalized at resolution time.</summary>
        public SceneFloat3 vec;
    }

    /// <summary>Canonical <see cref="AxisSpec.space"/> values.</summary>
    public static class AxisSpaces
    {
        public const string PartLocal      = "part_local";
        public const string TargetLocal    = "target_local";
        public const string World          = "world";
        public const string ToolActionAxis = "tool_action_axis";
    }
}
