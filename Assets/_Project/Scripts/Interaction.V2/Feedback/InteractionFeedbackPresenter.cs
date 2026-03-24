using UnityEngine;
using OSE.UI.Root;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Coordinates all visual feedback for the V2 interaction system.
    /// Receives InteractionFeedbackData from the orchestrator each frame
    /// and delegates to sub-feedback handlers.
    ///
    /// This component is presentation-only — it never decides behavior.
    /// Place on a root-level GameObject in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InteractionFeedbackPresenter : MonoBehaviour
    {
        [SerializeField] private InteractionSettings _settings;

        // Sub-feedback handlers (plain C# classes, not MonoBehaviours)
        private HoverFeedback _hover;
        private SelectionFeedback _selection;

        private GameObject _lastHovered;
        private GameObject _lastSelected;

        private void Awake()
        {
            _hover = new HoverFeedback();
            _selection = new SelectionFeedback();
        }

        /// <summary>
        /// Set the settings reference at runtime (used when auto-created by bootstrap).
        /// </summary>
        public void Initialize(InteractionSettings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Called by InteractionOrchestrator each frame with current state.
        /// </summary>
        public void UpdateFeedback(InteractionState state, InteractionFeedbackData data)
        {
            if (_settings != null && !_settings.UseV2Interaction) return;

            bool allowHoverVisuals = state == InteractionState.Idle
                                  || state == InteractionState.PartHovered
                                  || state == InteractionState.PartSelected
                                  || state == InteractionState.DraggingPart;

            bool selectedHover = data.SelectedPart != null
                              && data.HoveredPart != null
                              && data.HoveredPart == data.SelectedPart;

            // Clear visuals that are no longer active.
            if (_lastSelected != null
                && _lastSelected != data.SelectedPart
                && _lastSelected != data.HoveredPart)
            {
                _selection.ClearSelection(_lastSelected);
            }

            if (_lastHovered != null
                && _lastHovered != data.HoveredPart
                && _lastHovered != data.SelectedPart)
            {
                _hover.ClearHover(_lastHovered);
            }

            // Re-apply active visuals every frame so external systems cannot override them.
            if (data.SelectedPart != null)
                _selection.ApplySelection(data.SelectedPart);

            if (data.HoveredPart != null && allowHoverVisuals)
            {
                if (selectedHover)
                    _hover.ApplySelectedHover(data.HoveredPart);
                else
                    _hover.ApplyHover(data.HoveredPart);
            }

            _lastSelected = data.SelectedPart;
            _lastHovered = data.HoveredPart;
        }
    }

    /// <summary>
    /// Applies hover highlight to parts under the pointer.
    /// </summary>
    public sealed class HoverFeedback
    {
        private static readonly Color HoverColor = new(0.5f, 0.9f, 1f, 1f);
        private static readonly Color SelectedHoverColor = new(0.6f, 0.82f, 1f, 1f);

        public void ApplyHover(GameObject part)
        {
            if (part == null) return;
            if (MaterialHelper.IsImportedModel(part))
                MaterialHelper.ApplyTint(part, HoverColor);
            else
                MaterialHelper.Apply(part, "Preview Part Material", HoverColor);
        }

        public void ApplySelectedHover(GameObject part)
        {
            if (part == null) return;
            if (MaterialHelper.IsImportedModel(part))
                MaterialHelper.ApplyTint(part, SelectedHoverColor);
            else
                MaterialHelper.Apply(part, "Preview Part Material", SelectedHoverColor);
        }

        public void ClearHover(GameObject part)
        {
            if (part == null) return;
            if (MaterialHelper.IsImportedModel(part))
                MaterialHelper.ClearTint(part);
            else
                MaterialHelper.RestoreOriginals(part);
        }
    }

    /// <summary>
    /// Applies selection highlight (amber) to the selected part.
    /// </summary>
    public sealed class SelectionFeedback
    {
        private static readonly Color SelectedColor = new(1f, 0.85f, 0.2f, 1f);

        public void ApplySelection(GameObject part)
        {
            if (part == null) return;
            if (MaterialHelper.IsImportedModel(part))
                MaterialHelper.ApplyTint(part, SelectedColor);
            else
                MaterialHelper.Apply(part, "Preview Part Material", SelectedColor);
        }

        public void ClearSelection(GameObject part)
        {
            if (part == null) return;
            if (MaterialHelper.IsImportedModel(part))
                MaterialHelper.ClearTint(part);
            else
                MaterialHelper.RestoreOriginals(part);
        }
    }
}
