using System;

namespace OSE.Content
{
    [Serializable]
    public sealed class AssemblyDefinition
    {
        public string id;
        public string name;
        public string description;
        public string machineId;
        public string[] subassemblyIds;
        public string[] stepIds;
        public string[] dependencyAssemblyIds;
        public string learningFocus;

        /// <summary>
        /// Optional: IDs of child assemblies that are constituent members of this assembly
        /// (assembly-of-assemblies pattern). Child assemblies play out before this assembly's
        /// own steps. Extends <see cref="dependencyAssemblyIds"/> (sequencing) with explicit
        /// membership (the children become physical constituents of this parent assembly).
        /// Resolved at load time by <c>MachinePackageNormalizer</c>.
        /// </summary>
        public string[] memberAssemblyIds;
    }
}
