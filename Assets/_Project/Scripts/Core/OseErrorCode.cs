namespace OSE.Core
{
    /// <summary>
    /// Stable numeric identifiers for categorized OSE error and warning conditions.
    /// Include the code in log calls via <see cref="OseLog.Error(OseErrorCode,string)"/>
    /// and <see cref="OseLog.Warn(OseErrorCode,string)"/> — the four-digit prefix makes
    /// log lines greppable even when message text changes.
    ///
    /// Ranges:
    ///   1000–1999  Session / lifecycle
    ///   2000–2999  Step FSM
    ///   3000–3999  Content (packages, schema, definitions)
    ///   4000–4999  Interaction / input
    ///   5000–5999  UI / presentation (spawning, poses, visuals)
    /// </summary>
    public enum OseErrorCode
    {
        // ── Session / Lifecycle ───────────────────────────────────────────
        SessionStartFailed          = 1001,
        SessionRestoreFailed        = 1002,
        PackageLoadFailed           = 1003,
        PackageValidationFailed     = 1004,
        ServiceNotRegistered        = 1005,

        // ── Step FSM ─────────────────────────────────────────────────────
        StepFsmInvalidTransition    = 2001,
        StepCompletionBlocked       = 2002,
        StepActivationOverride      = 2003,
        StepNotActive               = 2004,

        // ── Content ──────────────────────────────────────────────────────
        MissingPartDefinition       = 3001,
        MissingTargetDefinition     = 3002,
        MissingToolDefinition       = 3003,
        SchemaVersionMismatch       = 3004,
        MissingRequiredField        = 3005,

        // ── Interaction / Input ──────────────────────────────────────────
        ToolActionResolutionFailed  = 4001,
        PlacementValidationFailed   = 4002,
        PointerRoutingFailed        = 4003,

        // ── UI / Presentation ────────────────────────────────────────────
        SpawnFailed                 = 5001,
        PoseResolutionFailed        = 5002,
        MissingSceneSetup           = 5003,
    }
}
