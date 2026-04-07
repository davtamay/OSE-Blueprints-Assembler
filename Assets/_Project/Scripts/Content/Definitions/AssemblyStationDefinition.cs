using System;

namespace OSE.Content
{
    /// <summary>
    /// Defines a named spatial workstation in the assembly scene.
    /// Each station has a physical table surface that parts rest on,
    /// a camera home position, and a layout area for part placement.
    ///
    /// Stations are authored in <see cref="PackagePreviewConfig.stations"/>
    /// and referenced by <see cref="AssemblyDefinition.stationId"/>.
    /// When the active assembly changes, the runtime transitions to the
    /// assembly's declared station — moving the camera and repositioning parts.
    ///
    /// Bench stations (axis build work) have a table surface at <see cref="surfaceY"/>.
    /// The frame station has <see cref="surfaceY"/> = 0 (floor level).
    /// </summary>
    [Serializable]
    public sealed class AssemblyStationDefinition
    {
        /// <summary>
        /// Unique station identifier. Referenced by <see cref="AssemblyDefinition.stationId"/>.
        /// E.g. "station_bench_y_left", "station_frame".
        /// </summary>
        public string id;

        /// <summary>Display name shown in the assembly transition UI overlay.</summary>
        public string displayName;

        /// <summary>
        /// World-space center of the station (table center for bench stations,
        /// frame origin for the frame station).
        /// </summary>
        public SceneFloat3 position;

        /// <summary>
        /// Y coordinate of the table surface. Parts are spawned at
        /// <c>surfaceY + 0.02 m</c> (2 cm above) so they rest visually on the table.
        /// Set to 0 for the frame station where parts attach at floor level.
        /// </summary>
        public float surfaceY;

        /// <summary>Usable table width in metres for the part layout grid. Default 1.6 m.</summary>
        public float layoutWidth = 1.6f;

        /// <summary>Usable table depth in metres for the part layout grid. Default 0.55 m.</summary>
        public float layoutDepth = 0.55f;

        /// <summary>
        /// Preferred orbital camera pivot for this station.
        /// <see cref="StepGuidanceService"/> flies the camera here when the station activates.
        /// </summary>
        public SceneFloat3 cameraHome;

        /// <summary>Preferred orbital camera distance from <see cref="cameraHome"/> in metres.</summary>
        public float cameraDistance = 2.0f;
    }
}
