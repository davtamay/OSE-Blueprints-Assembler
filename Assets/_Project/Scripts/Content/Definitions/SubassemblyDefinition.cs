using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSE.Content
{
    /// <summary>
    /// Derived rigid-body representation of a group at a specific target.
    /// Populated by <see cref="Loading.MachinePackageNormalizer"/> from
    /// <see cref="PackagePreviewConfig.integratedSubassemblyPlacements"/>. Editor
    /// and scene code consume this so a group pose behaves like a single part's
    /// pose: one transform to move, member offsets are fixed.
    /// Never serialized — always derived at load time.
    /// </summary>
    public sealed class GroupRigidBody
    {
        public string targetId;
        public Vector3 groupCenter;          // centroid of member positions (PreviewRoot-local)
        public Quaternion groupRotation;     // identity unless a canonical rotation is derivable
        public Dictionary<string, Vector3> memberPositionOffsets;    // partId → pos relative to groupCenter
        public Dictionary<string, Quaternion> memberRotationOffsets; // partId → rot relative to groupRotation
        public Dictionary<string, Vector3> memberScales;             // partId → authored scale
    }

    [Serializable]
    public sealed class SubassemblyDefinition
    {
        public string id;
        public string name;
        public string assemblyId;
        public string description;
        public string[] partIds;
        public string[] stepIds;
        public string milestoneMessage;

        /// <summary>
        /// True = this subassembly is a composite/aggregate of other child subassemblies
        /// and its <see cref="partIds"/> list may intentionally overlap with child
        /// subassemblies' partIds (e.g. a "complete axis unit" that contains parts
        /// already owned by carriage/idler/motor subassemblies).
        /// Aggregate subassemblies are exempt from the
        /// PartOwnershipExclusivityPass sibling-collision check and are not indexed
        /// by MachinePackageNormalizer.IndexPartOwnership.
        /// </summary>
        public bool isAggregate;

        /// <summary>
        /// Optional: IDs of sub-subassemblies that this subassembly is physically built FROM.
        /// Enables hierarchical emergence beyond the flat partIds model:
        /// sub-subassemblies → subassembly → assembly → machine.
        /// Member subassembly steps play before the parent subassembly's own steps.
        /// Resolved at load time by <c>MachinePackageNormalizer</c>.
        /// </summary>
        public string[] memberSubassemblyIds;

        /// <summary>
        /// Derived rigid-body cache keyed by targetId. Populated by
        /// <see cref="Loading.MachinePackageNormalizer.BakeGroupRigidBody"/>.
        /// Never persisted. Enables the editor to treat a group-at-target as
        /// a single rigid transform (group center + fixed member offsets)
        /// instead of 24 independent per-member poses.
        /// </summary>
        [NonSerialized]
        public Dictionary<string, GroupRigidBody> rigidBodyByTargetId;

        /// <summary>
        /// Derived "start pose" rigid body — the fabrication layout, computed
        /// from each member's <c>partPlacements[].assembledPosition</c>
        /// (the finished-panel position after the group's own build steps).
        /// Constant regardless of current step. Populated by
        /// <see cref="Loading.MachinePackageNormalizer.BakeGroupRigidBody"/>.
        /// </summary>
        [NonSerialized]
        public GroupRigidBody startRigidBody;

        public string GetDisplayName()
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();

            if (!string.IsNullOrWhiteSpace(id))
                return id.Trim();

            return "Unnamed Subassembly";
        }
    }
}
