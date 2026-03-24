using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Central toggle configuration for all V2 interaction features.
    /// Create an asset via: Create → OSE → Interaction Settings.
    /// Systems check toggles at decision time (not startup) so runtime
    /// changes via the Inspector or debug window take effect immediately.
    /// </summary>
    [CreateAssetMenu(fileName = "InteractionSettings", menuName = "OSE/Interaction Settings")]
    public sealed class InteractionSettings : ScriptableObject
    {
        [Header("V2 System Master Switch")]
        [Tooltip("When false, the existing PartInteractionBridge handles all interaction unchanged.")]
        public bool UseV2Interaction = false;

        // ── Camera ──

        [Header("Camera — Features")]
        public bool EnableCameraAssist = true;
        public bool EnableAutoFraming = true;
        public bool EnableVisibilitySolver = false;
        public bool EnableSmartPivot = true;
        [Tooltip("When enabled, selecting a part pivots the camera to its ghost target (where it goes) instead of the part itself.")]
        public bool EnablePivotToTarget = true;
        public bool EnableSuggestedViews = true;
        public bool EnableOrbitGizmo = false;
        public bool EnableVisionProInteractionModel = false;
        public bool EnableCameraConstraintSphere = true;

        [Header("Camera — Sensitivity")]
        [Range(0.05f, 1f)] public float OrbitSensitivity = 0.3f;
        [Range(0.001f, 0.02f)] public float PanSensitivity = 0.005f;
        [Range(0.01f, 1f)] public float ZoomSensitivity = 0.5f;

        [Header("Camera — Smoothing")]
        [Range(1f, 20f)] public float OrbitSmoothing = 8f;
        [Range(1f, 20f)] public float PanSmoothing = 8f;
        [Range(1f, 20f)] public float ZoomSmoothing = 6f;
        [Range(1f, 10f)] public float PivotSmoothing = 4f;

        [Header("Camera — Constraints")]
        [Min(0.05f)] public float MinCameraDistance = 0.3f;
        [Min(0.1f)] public float MaxCameraDistance = 10f;
        [Range(-90f, 0f)] public float MinPitch = -10f;
        [Range(0f, 90f)] public float MaxPitch = 85f;

        // ── Placement ──

        [Header("Placement — Features")]
        public bool EnableMagneticPlacement = true;
        public bool EnablePlacementCorridors = false;
        public bool EnableGhostPathGuidance = false;

        [Header("Placement — Tuning")]
        [Range(1f, 4f)] public float MagneticRadiusMultiplier = 2f;
        [Range(10f, 90f)] public float CorridorHalfAngle = 45f;

        // ── Step Guidance ──

        [Header("Step Guidance")]
        public bool EnableStepViewGuidance = true;
        public bool EnableExplodedStepPreview = false;

        // ── Input ──

        [Header("Input — Thresholds")]
        [Range(2f, 20f)] public float DragThresholdPixels = 5f;
        [Range(0.2f, 1f)] public float LongPressDuration = 0.4f;
        [Range(0.1f, 0.5f)] public float DoubleTapWindow = 0.3f;

        // ── Tool Action Preview ──

        [Header("Tool Action Preview")]
        [Tooltip("When enabled, Use-family steps play a tool action preview animation on first target tap (I Do / We Do / You Do learning model).")]
        public bool EnableToolActionPreview = true;

        [Tooltip("Scale factor for preview animation durations. 0.5 = fast, 1.0 = normal, 2.0 = slow.")]
        [Range(0.5f, 2f)] public float PreviewDurationScale = 1f;

        [Tooltip("Seconds of idle before auto-assist kicks in during guided ('We Do') mode.")]
        [Range(1f, 5f)] public float GuidedAutoAssistDelay = 3f;

        // ── Feedback ──

        [Header("Feedback — Tuning")]
        [Range(4f, 30f)] public float SnapLerpSpeed = 12f;
        [Range(0.1f, 1f)] public float InvalidFlashDuration = 0.3f;

        // ── Layers ──

        [Header("Raycast")]
        public LayerMask PartLayerMask = ~0; // Default: all layers
    }
}
