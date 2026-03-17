using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Snapshot of interaction state for the current frame,
    /// consumed by InteractionFeedbackPresenter to update visuals.
    /// Produced by the InteractionOrchestrator each frame.
    /// </summary>
    public struct InteractionFeedbackData
    {
        public GameObject HoveredPart;
        public GameObject SelectedPart;
        public GameObject DraggedPart;

        // Placement assist state
        public Vector3? MagneticTargetPosition;
        public float MagneticStrength;
        public bool IsInCorridor;
        public bool IsValidPlacement;

        // Ghost path guidance
        public Vector3? GhostPathStart;
        public Vector3? GhostPathEnd;

        // Step info
        public bool StepActive;
        public string ActiveStepId;
    }
}
