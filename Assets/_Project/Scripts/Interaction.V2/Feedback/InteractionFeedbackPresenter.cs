using UnityEngine;
using OSE.UI.Root;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;

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
            TryApplyAffordanceState(part, AffordanceStateShortcuts.hovered);
            MaterialHelper.SetMaterialColor(part, HoverColor);
        }

        public void ApplySelectedHover(GameObject part)
        {
            if (part == null) return;
            TryApplyAffordanceState(part, AffordanceStateShortcuts.hoveredPriority);
            MaterialHelper.SetMaterialColor(part, SelectedHoverColor);
        }

        public void ClearHover(GameObject part)
        {
            if (part == null) return;
            if (!TryApplyAffordanceState(part, AffordanceStateShortcuts.idle))
                MaterialHelper.SetMaterialColor(part, Color.white);
        }

        private static bool TryApplyAffordanceState(GameObject part, byte stateIndex, float transitionAmount = 1f)
        {
            if (part == null)
                return false;

            var stateProvider = part.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                return false;

            stateProvider.UpdateAffordanceState(new AffordanceStateData(stateIndex, transitionAmount));
            return true;
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
            TryApplyAffordanceState(part, AffordanceStateShortcuts.selected);
            MaterialHelper.SetMaterialColor(part, SelectedColor);
        }

        public void ClearSelection(GameObject part)
        {
            if (part == null) return;
            if (!TryApplyAffordanceState(part, AffordanceStateShortcuts.idle))
                MaterialHelper.SetMaterialColor(part, Color.white);
        }

        private static bool TryApplyAffordanceState(GameObject part, byte stateIndex, float transitionAmount = 1f)
        {
            if (part == null)
                return false;

            var stateProvider = part.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                return false;

            stateProvider.UpdateAffordanceState(new AffordanceStateData(stateIndex, transitionAmount));
            return true;
        }
    }
}
