namespace OSE.Interaction
{
    /// <summary>
    /// The active interaction mode. Determines which input provider, camera system,
    /// and tracking components are enabled.
    ///
    /// Resolved at startup by InteractionModeResolver and used by the orchestrator
    /// to configure all subsystems consistently from a single source of truth.
    /// </summary>
    public enum InteractionMode
    {
        /// <summary>Auto-detect based on platform and device state. Use this in builds.</summary>
        Auto,

        /// <summary>Mouse + keyboard. Camera rig active, TrackedPoseDriver disabled.</summary>
        Desktop,

        /// <summary>Touch gestures. Camera rig active, TrackedPoseDriver disabled.</summary>
        Mobile,

        /// <summary>XR headset (real or simulated). TrackedPoseDriver active, camera rig disabled.</summary>
        XR
    }
}
