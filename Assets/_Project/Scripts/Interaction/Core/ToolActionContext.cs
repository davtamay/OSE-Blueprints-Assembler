using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Bundles all target-level action data resolved at the bridge layer.
    /// Flows as a single value through IPartActionBridge → InteractionOrchestrator
    /// → ToolActionPreviewController, replacing individual parameter threading.
    /// </summary>
    public struct ToolActionContext
    {
        public string TargetId;
        public Vector3 TargetWorldPos;
        public Vector3 SurfaceWorldPos;
        public Quaternion TargetWorldRotation;

        // ── Linear action (axis + length describe the tool's working
        // path, derived from the entry's startTransform/endTransform pose
        // pair at the bridge layer; legacy weldAxis/weldLength fed the
        // same pair pre-migration). Field names retained as Weld* for
        // API stability — internal label only, no longer surfaced to
        // authoring. WorkDuration is the authored drift time in seconds
        // (entry.speed > 0 ? length / speed : 0 == "use system default").
        public Vector3 WeldAxis;
        public float WeldLength;
        public float WorkDuration;

        // ── Authored tool orientation ──
        public bool HasToolActionRotation;
        public Quaternion ToolActionRotation;

        // ── Placement behaviour ──
        /// <summary>
        /// When true the approach animation ends with instant placement — no action
        /// phase plays. Used for persistent tools (clamps, fixtures) that should snap
        /// into the authored position/rotation as soon as the tool arrives.
        /// </summary>
        public bool InstantPlacement;

        /// <summary>
        /// The clean persistent scale a tool will rest at after
        /// <see cref="InstantPlacement"/> conversion (= cursor uniform scale ×
        /// tool.scaleOverride). The preview controller lerps Approach to this
        /// value when InstantPlacement is true, so the lerp lands at exactly
        /// the same scale the persistent conversion will write — no snap.
        /// Computed by the bridge layer from the active tool definition;
        /// 0 means "not authored / non-persistent" — controller falls back
        /// to the assembly-matched scale in that case.
        /// </summary>
        public float PersistentScale;

        // ── Assembly scale ──
        /// <summary>
        /// Current assembly UI scale multiplier. The preview controller lerps tool
        /// scale from its cursor size to cursor × assemblyScale during approach so
        /// the tool matches the parts at the target without being oversized on the cursor.
        /// </summary>
        public float AssemblyScale;

        // ── Tool spatial metadata (grip, tip, action axis) ──
        public ToolPoseConfig ToolPose;

        // ── Part effect ──
        /// <summary>
        /// Optional effect that drives part transform changes synchronized with action progress.
        /// Resolved at the bridge layer from TargetDefinition.associatedPartId + stepPose data.
        /// Null when the target has no associated part or no pose interpolation is needed.
        /// </summary>
        public IPartEffect PartEffect;

        /// <summary>
        /// World-space direction (normalized) the part will travel during the
        /// lerp / thread_in effect (= endWorld - startWorld). Zero when no
        /// part effect is active. Forwarded into <see cref="PreviewContext.PartMotionDirectionWorld"/>
        /// so the guided-drag arrow points toward the end pose.
        /// </summary>
        public Vector3 PartMotionDirectionWorld;

        /// <summary>
        /// When true (default), the tool preview's world position tracks the
        /// part's world-space displacement returned each frame by
        /// <see cref="IPartEffect.Apply"/>. Source: <c>ToolPartInteraction.followPart</c>.
        /// Set false for "tool stationary at the working pose, part moves
        /// alone" interactions (e.g. heat gun warming a part — drill never
        /// uses this; bolt-driven plunge always uses true).
        /// </summary>
        public bool FollowPart;

        /// <summary>
        /// When true, <see cref="ToolActionRotation"/> is already a direct mesh rotation —
        /// no <c>Inverse(gripRotation)</c> correction should be applied.
        /// False (legacy) means <c>ToolActionRotation</c> is an approach vector and the
        /// correction is still needed for correct visual placement.
        /// </summary>
        public bool ToolActionRotationIsMesh;

        /// <summary>
        /// Per-action override payload from the resolved
        /// <see cref="ToolActionDefinition.previewConfig"/>. Null when the
        /// action has no overrides authored — preview classes fall back to
        /// their hardcoded defaults via <c>Override(authored, fallback)</c>.
        /// Set by the bridge layer at the same time as the other action
        /// data; see <c>TargetActionResolver</c> / bridge equivalent.
        /// </summary>
        public ToolActionPreviewConfig PreviewConfig;
    }
}
