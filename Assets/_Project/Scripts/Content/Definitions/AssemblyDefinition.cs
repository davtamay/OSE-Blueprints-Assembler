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
        /// Optional reference to an <see cref="AssemblyStationDefinition.id"/> in
        /// <see cref="PackagePreviewConfig.stations"/>. When set, all steps in this
        /// assembly take place at the named station — the camera and part layout
        /// transition to that station when this assembly starts.
        /// Leave empty for assemblies that span multiple stations or have no
        /// dedicated spatial zone.
        /// </summary>
        public string stationId;
    }
}
