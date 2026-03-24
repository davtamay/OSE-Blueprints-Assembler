namespace OSE.Interaction.V2
{
    /// <summary>
    /// The physical gesture shape the user performs during a ToolFocus engagement.
    /// Each type maps to a distinct input interpretation on every platform.
    /// </summary>
    public enum GestureType
    {
        /// <summary>Single click/tap — existing behavior, no sustained gesture.</summary>
        Tap = 0,
        /// <summary>Circular drag around the target (e.g. torque wrench tightening).</summary>
        RotaryTorque = 1,
        /// <summary>Linear drag away from anchor along an axis (e.g. tape measure pull).</summary>
        LinearPull = 2,
        /// <summary>Hold pointer steady on target for a duration (e.g. welding, soldering).</summary>
        SteadyHold = 3,
        /// <summary>Trace along a defined path or spline (e.g. grind line, weld seam).</summary>
        PathTrace = 4,
        /// <summary>Quick flick/strike motion toward target (e.g. hammer, chisel).</summary>
        ImpactStrike = 5
    }
}
