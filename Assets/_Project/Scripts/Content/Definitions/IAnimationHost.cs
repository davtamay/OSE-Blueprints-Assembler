using UnityEngine;

namespace OSE.Content
{
    public enum AnimationHostKind
    {
        Part,
        PartGroup,
        Tool
    }

    /// <summary>
    /// Unifies parts, partGroups, and tools behind one abstraction for
    /// animation-cue authoring and playback. Tool cues fire via the cursor
    /// preview GameObject resolved by <c>CursorManager.ToolPreview</c>.
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
