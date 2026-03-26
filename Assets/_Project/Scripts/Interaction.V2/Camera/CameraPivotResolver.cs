using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Resolves the camera pivot position based on the current interaction context.
    /// The pivot shifts dynamically so orbiting always feels centered on the
    /// relevant object. Transitions are smoothed by CameraSmoothing (pivot axis).
    /// </summary>
    public sealed class CameraPivotResolver
    {
        public enum PivotSource
        {
            AssemblyCenter,
            SelectedPart,
            PreviewTarget,
            StepTarget,
            Custom
        }

        private PivotSource _activeSource = PivotSource.AssemblyCenter;
        private Vector3 _customPivot;
        private Bounds _assemblyBounds;
        private bool _assemblyBoundsValid;

        public PivotSource ActiveSource => _activeSource;

        /// <summary>
        /// Set the assembly bounds (computed once when parts are spawned).
        /// Used as fallback pivot when nothing is selected.
        /// </summary>
        public void SetAssemblyBounds(Bounds bounds)
        {
            _assemblyBounds = bounds;
            _assemblyBoundsValid = true;
        }

        public void SetSource(PivotSource source) => _activeSource = source;
        public void SetCustomPivot(Vector3 position)
        {
            _customPivot = position;
            _activeSource = PivotSource.Custom;
        }

        /// <summary>
        /// Resolve the pivot world position given current orchestrator state.
        /// </summary>
        public Vector3 Resolve(InteractionOrchestrator orchestrator)
        {
            switch (_activeSource)
            {
                case PivotSource.SelectedPart:
                    if (orchestrator != null && orchestrator.SelectedPart != null)
                        return orchestrator.SelectedPart.transform.position;
                    return FallbackCenter();

                case PivotSource.Custom:
                    return _customPivot;

                case PivotSource.AssemblyCenter:
                default:
                    return FallbackCenter();
            }
        }

        private Vector3 FallbackCenter()
        {
            return _assemblyBoundsValid ? _assemblyBounds.center : Vector3.zero;
        }
    }
}
