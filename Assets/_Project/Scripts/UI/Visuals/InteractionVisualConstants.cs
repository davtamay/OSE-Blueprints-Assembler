using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Shared visual tuning constants for the interaction bridge layer.
    /// Centralizes colors, thresholds, and timing values used across
    /// PartInteractionBridge and its extracted subsystems.
    /// </summary>
    internal static class InteractionVisualConstants
    {
        // ── Part visual state colors ──
        public static readonly Color SelectedPartColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);
        public static readonly Color GrabbedPartColor = new Color(1.0f, 0.65f, 0.1f, 1.0f);
        public static readonly Color HoveredPartColor = new Color(0.60f, 0.82f, 1.0f, 1.0f);
        public static readonly Color DimmedPartColor = new Color(0.58f, 0.58f, 0.58f, 1.0f);
        public static readonly Color ActiveStepEmission = new Color(0.15f, 0.35f, 0.6f);
        public static readonly Color PreviewReadyColor = new Color(0.3f, 1.0f, 0.5f, 0.4f);

        // ── Hint highlight ──
        public static readonly Color HintHighlightColorA = new Color(0.95f, 0.85f, 0.2f, 0.4f);
        public static readonly Color HintHighlightColorB = new Color(1.0f, 0.95f, 0.35f, 0.7f);
        public const float HintHighlightDuration = 6f;
        public const float HintHighlightPulseSpeed = 4f;

        // ── Subassembly emission ──
        public static readonly Color HoveredSubassemblyEmission = new Color(0.05f, 0.16f, 0.28f);
        public static readonly Color SelectedSubassemblyEmission = new Color(0.35f, 0.22f, 0.02f);

        // ── Drag tuning ──
        public const float DragThresholdPixels = 5f;
        public const float ScrollDepthSpeed = 0.5f;
        public const float PinchDepthSpeed = 0.02f;
        public const float DepthAdjustSpeed = 0.01f;
        public const float MinDragRayDistance = 0.05f;
        public const float DragViewportMargin = 0.03f;
        public const float DragFloorEpsilon = 0.001f;

        // ── Part grid layout ──
        public const float PartGridSpacing = 0.6f;
        public const float PartGridStartZ = -2.8f;
        public const float PartLayoutY = 0.55f;
    }
}
