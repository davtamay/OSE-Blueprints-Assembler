using OSE.Core;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Provides step-aware camera guidance. When a step activates,
    /// this service can auto-frame the camera on the step target and
    /// provide suggested viewpoint options for the UI.
    ///
    /// All behavior is toggle-gated via InteractionSettings.
    /// </summary>
    public sealed class StepGuidanceService
    {
        private readonly InteractionSettings _settings;
        private readonly AssemblyCameraRig _cameraRig;

        private string _activeStepId;

        public StepGuidanceService(InteractionSettings settings, AssemblyCameraRig cameraRig)
        {
            _settings = settings;
            _cameraRig = cameraRig;
        }

        /// <summary>
        /// Subscribe this to RuntimeEventBus.Subscribe&lt;StepActivated&gt;.
        /// </summary>
        public void OnStepActivated(StepActivated evt)
        {
            _activeStepId = evt.StepId;

            if (!_settings.EnableStepViewGuidance || _cameraRig == null)
                return;

            // Auto-framing can be wired here once target position is resolved
            // from step data. For now, this is a hook point.
        }

        /// <summary>
        /// Called externally when a step's target position is known
        /// (e.g., resolved from previewConfig by PartInteractionBridge).
        /// </summary>
        public void AutoFrameStepTarget(UnityEngine.Vector3 targetPosition)
        {
            if (!_settings.EnableAutoFraming || _cameraRig == null)
                return;

            _cameraRig.FocusOn(targetPosition);
        }

        /// <summary>
        /// Apply a named suggested viewpoint (called from UI buttons).
        /// </summary>
        public void ApplySuggestedView(string viewLabel)
        {
            if (_cameraRig == null) return;

            var vp = viewLabel switch
            {
                "Front" => ViewpointLibrary.Front,
                "Side" => ViewpointLibrary.Side,
                "Top" => ViewpointLibrary.Top,
                "Iso" => ViewpointLibrary.Isometric,
                "Detail" => ViewpointLibrary.Detail,
                _ => ViewpointLibrary.Front
            };

            _cameraRig.ApplyViewpoint(vp, animated: true);
        }

        /// <summary>
        /// Get the list of available viewpoint labels for the current step.
        /// Used by UI to populate suggested view buttons.
        /// </summary>
        public string[] GetAvailableViewpoints()
        {
            if (!_settings.EnableSuggestedViews)
                return System.Array.Empty<string>();

            return new[] { "Front", "Side", "Top", "Iso", "Detail" };
        }
    }
}
