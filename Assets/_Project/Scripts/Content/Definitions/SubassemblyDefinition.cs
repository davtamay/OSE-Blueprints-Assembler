using System;

namespace OSE.Content
{
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
