using UnityEngine;

namespace OSE.Content
{
    public enum AnimationHostKind
    {
        Part,
        Subassembly
    }

    /// <summary>
    /// Unifies parts and subassemblies behind one abstraction for animation-cue
    /// authoring and playback. Tools are not yet hosts (their cues are still
    /// step-scoped via <see cref="AnimationCueEntry.targetToolIds"/>) and will
    /// be migrated in a later pass.
    /// </summary>
    public interface IAnimationHost
    {
        string HostId { get; }
        string HostDisplayName { get; }
        AnimationHostKind HostKind { get; }

        /// <summary>
        /// The cue array owned by this host. May be null when no cues are
        /// authored — callers should tolerate null.
        /// </summary>
        AnimationCueEntry[] AnimationCues { get; set; }
    }
}
