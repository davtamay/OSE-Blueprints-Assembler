using System;

namespace OSE.Content
{
    /// <summary>
    /// Inline end-pose for a single <see cref="TaskOrderEntry"/>. When authored,
    /// the runtime snaps the part this task touches to this transform on task
    /// completion. When null, the task falls back to
    /// <see cref="PartPreviewPlacement.assembledPosition"/>.
    ///
    /// <para>Introduced by Phase G.2 to replace the shared
    /// <c>PartPreviewPlacement.stepPoses[]</c> array with inline per-task poses.
    /// Each task owns its end-pose exclusively — no other task can read or
    /// mutate it, which enforces the pose-chain linearity invariant by
    /// construction.</para>
    ///
    /// <para>Serialized transform components use the same <see cref="SceneFloat3"/>
    /// and <see cref="SceneQuaternion"/> types as every other pose field in the
    /// package schema so <see cref="UnityEngine.JsonUtility"/> round-trips cleanly.</para>
    /// </summary>
    [Serializable]
    public sealed class TaskEndTransform
    {
        public SceneFloat3     position;
        public SceneQuaternion rotation;
        public SceneFloat3     scale;
    }
}
