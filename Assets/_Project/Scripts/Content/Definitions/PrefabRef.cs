using System;

namespace OSE.Content
{
    /// <summary>
    /// Provenance link from a step back to the Step Configuration Prefab it
    /// was instantiated from. Authored exclusively by the TTAW prefab
    /// drag-drop flow (see <c>Tools/instantiate_prefab.py</c> + the
    /// <c>TTAW.PrefabWizard</c> editor partial). Steps with a non-empty
    /// <c>prefabRef</c> render a "linked" badge in the editor — author can
    /// Bake (clear the ref) or Re-instantiate (rerun the prefab to pull
    /// updates).
    ///
    /// <para>Slice 1 stores enough to do manual re-instantiation. Slice 3
    /// (deferred) will compare <c>sourceMtime</c> to the prefab YAML's
    /// current mtime and surface a "prefab updated" banner so the author
    /// knows when to re-instantiate.</para>
    /// </summary>
    [Serializable]
    public sealed class PrefabRef
    {
        /// <summary>Stem of the source prefab YAML in <c>AgentAssistant/prefabs/</c> (e.g. "CarriageBuild").</summary>
        public string prefabId;

        /// <summary>
        /// Unique identifier for this instantiation, shared across every step the
        /// prefab emitted in one drop. Lets Bake / Re-instantiate operate on the
        /// whole instance rather than a single step.
        /// </summary>
        public string instanceId;

        /// <summary>
        /// Role-to-partId bindings used at instantiation time. Stored as a small
        /// array of (role, partId) pairs so the YAML diff stays human-readable
        /// (no nested-JSON-string surface). Re-instantiate replays these into
        /// the wizard so the author rarely has to re-bind.
        /// </summary>
        public PrefabRoleBinding[] bindings;

        /// <summary>
        /// ISO-8601 mtime of the prefab YAML at instantiation time. Slice 3
        /// will compare this to the file's current mtime to flag drift.
        /// </summary>
        public string sourceMtime;

        /// <summary>
        /// True when no provenance has been recorded (post-bake, hand-authored,
        /// or default-constructed). Hooked into
        /// <c>MachinePackageNormalizer.DropEmptyStepPayloads</c> so empty refs
        /// don't bloat the JSON output (per
        /// <c>feedback_jsonutility_inflates_empty_payloads</c>).
        /// </summary>
        public bool IsEmpty()
            => string.IsNullOrEmpty(prefabId)
            && string.IsNullOrEmpty(instanceId)
            && (bindings == null || bindings.Length == 0)
            && string.IsNullOrEmpty(sourceMtime);
    }

    /// <summary>Single role→partId entry in a <see cref="PrefabRef.bindings"/> array.</summary>
    [Serializable]
    public sealed class PrefabRoleBinding
    {
        public string role;
        /// <summary>Single-part role: this is the bound part id; <see cref="partIds"/> is null/empty.</summary>
        public string partId;
        /// <summary>List role (kind: part_list): this is the ordered list; <see cref="partId"/> is null.</summary>
        public string[] partIds;
    }
}
