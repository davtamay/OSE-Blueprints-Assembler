namespace OSE.Core
{
    public enum SessionMode
    {
        Tutorial,
        Guided,
        Standard,
        Challenge,
        Review
    }

    public enum SessionLifecycle
    {
        Uninitialized,
        Initializing,
        Idle,
        MachineSelected,
        SessionActive,
        StepActive,
        Validating,
        Completing,
        Completed,
        Paused,
        Restoring,
        Error
    }

    public enum StepState
    {
        Locked,
        Available,
        Active,
        Interacting,
        Validating,
        WaitingForEffect,
        WaitingForPhysicalConfirmation,
        Completed,
        Skipped,
        Reviewed,
        FailedAttempt,
        Suspended
    }

    public enum PartPlacementState
    {
        NotIntroduced,
        Available,
        Selected,
        Inspected,
        Grabbed,
        CandidatePlacement,
        InvalidPlacement,
        ValidPlacement,
        PlacedVirtually,
        MarkedPhysicallyPresent,
        HiddenByPhysicalSubstitute,
        Reset,
        Completed
    }

    public enum ToolAwarenessState
    {
        Required,
        Introduced,
        Acknowledged,
        Inspected,
        LinkedToStep,
        Optional,
        UsageCompleted
    }

    public enum ToolActionType
    {
        None,
        Measure,
        Tighten,
        Strike,
        WeldPass,
        GrindPass
    }

    public enum ToolActionFailureReason
    {
        None,
        NoActionConfigured,
        NoActiveToolEquipped,
        WrongToolEquipped,
        WrongTarget,
        AlreadyCompleted,
        InvalidStepState
    }

    public enum CapabilityTier
    {
        Minimal,
        Standard,
        Enhanced,
        Full
    }

    public enum ValidationMode
    {
        LenientTutorial,
        GuidedStandard,
        StrictChallenge,
        ExactReview,
        InstructorOverride
    }

    public enum EffectRole
    {
        PlacementFeedback,
        MilestoneFeedback,
        ProcessDemonstration,
        Welding,
        Sparks,
        HeatGlow,
        Torch,
        Dust,
        StructuralReveal,
        PreviewGuidance,
        ErrorHighlight,
        ChallengeSuccess
    }
}
