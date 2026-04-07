// Only compiled in the editor — no runtime overhead.
#if UNITY_EDITOR
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Drives the edit-mode step preview in the Unity Editor scene view.
    ///
    /// Loads the machine package from the authoring folder, pushes the selected step
    /// into the UI panels via <see cref="IPresentationAdapter"/>, and repositions
    /// spawned parts via <see cref="IStepAwarePositioner"/> — using the exact same
    /// <see cref="SessionDriver.PushStepAndPartToUI"/> path that play mode uses, so
    /// the editor preview is visually identical to what the trainee sees at runtime.
    ///
    /// This component is editor-only (<c>[ExecuteInEditMode]</c>).
    /// <see cref="SessionDriver"/> handles the play-mode session and is a plain
    /// <see cref="MonoBehaviour"/> with no editor responsibilities.
    /// </summary>
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [AddComponentMenu("OSE/Runtime/Edit Mode Preview Driver")]
    public sealed class EditModePreviewDriver : MonoBehaviour
    {
        [Header("Preview Configuration")]
        [SerializeField] private string _packageId = "onboarding_tutorial";
        [SerializeField] private SessionMode _sessionMode = SessionMode.Tutorial;
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField, Min(1)] private int _previewStepSequenceIndex = 1;

        /// <summary>The package loaded for edit-mode preview (may be null while loading).</summary>
        public MachinePackageDefinition EditModePackage => _editModePackage;

        /// <summary>The current preview step sequence index (1-based, matches step.sequenceIndex).</summary>
        public int PreviewStepSequenceIndex => _previewStepSequenceIndex;

        private readonly MachinePackageLoader _loader = new MachinePackageLoader();
        private MachinePackageDefinition _editModePackage;
        private bool _editModePreviewApplied;
        private int _editModeLoadVersion;

        // ── Lifecycle ──

        private void OnEnable()
        {
            if (Application.isPlaying) return;
            _editModePreviewApplied = false;
            RequestEditModeRefresh();
            HidePreviewIfPossible();
        }

        private void Update()
        {
            if (Application.isPlaying) return;
            UpdateEditMode();
        }

        private void OnValidate()
        {
            if (Application.isPlaying || !isActiveAndEnabled) return;
            _editModePreviewApplied = false;
            RequestEditModeRefresh();
            SessionDriver.RaiseEditModeStepChanged(_previewStepSequenceIndex);
        }

        // ── Public API (called by ToolTargetAuthoringWindow and editor tooling) ──

        /// <summary>
        /// Switches the package being previewed. Triggers a full reload —
        /// old parts are cleared and new ones spawned.
        /// No-op in play mode or if the package ID is unchanged.
        /// </summary>
        public void SetPackage(string packageId)
        {
            if (Application.isPlaying) return;
            if (string.Equals(_packageId, packageId, System.StringComparison.Ordinal)) return;

            _packageId = packageId;
            _previewStepSequenceIndex = 1;
            _editModePreviewApplied = false;
            UnityEditor.EditorUtility.SetDirty(this);
            RequestEditModeRefresh();
        }

        /// <summary>
        /// Sets the preview step sequence index and immediately repositions spawned parts.
        /// Fires <see cref="SessionDriver.EditModeStepChanged"/> so authoring windows stay in sync.
        /// No-op in play mode.
        /// </summary>
        public void SetEditModeStep(int sequenceIndex)
        {
            if (Application.isPlaying) return;

            bool changed = _previewStepSequenceIndex != sequenceIndex;
            if (changed)
            {
                _previewStepSequenceIndex = sequenceIndex;
                _editModePreviewApplied = false;
                UnityEditor.EditorUtility.SetDirty(this);
                TryApplyEditModePreview();
                SessionDriver.RaiseEditModeStepChanged(_previewStepSequenceIndex);
            }

            // Always reposition — ApplyModifiedProperties() may have already set the
            // serialized value before this call, making changed=false while parts still move.
            ApplyStepAwarePartPositions();
        }

        /// <summary>
        /// Called by <see cref="PackageAssetPostprocessor"/> when package content changes on disk.
        /// Triggers a preview reload if this driver is showing the affected package.
        /// </summary>
        public static void NotifyPackageContentChanged(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return;

            foreach (var driver in FindObjectsByType<EditModePreviewDriver>(FindObjectsSortMode.None))
            {
                if (!driver.isActiveAndEnabled) continue;
                if (!string.Equals(driver._packageId, packageId, System.StringComparison.Ordinal)) continue;
                if (Application.isPlaying) continue;

                OseLog.Info($"[EditModePreviewDriver] Package content changed for '{packageId}' — reloading preview.");
                driver.RequestEditModeRefresh();
            }
        }

        // ── Edit mode internals ──

        private void UpdateEditMode()
        {
            if (!_previewInEditMode)
            {
                HidePreviewIfPossible();
                return;
            }

            if (!_editModePreviewApplied)
                TryApplyEditModePreview();
        }

        private void RequestEditModeRefresh()
        {
            _editModePreviewApplied = false;

            if (!_previewInEditMode)
            {
                HidePreviewIfPossible();
                return;
            }

            _ = ReloadEditModePackageAsync(++_editModeLoadVersion);
        }

        private async Task ReloadEditModePackageAsync(int loadVersion)
        {
            string packageId = _packageId;
            if (string.IsNullOrWhiteSpace(packageId)) return;

            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId);

            if (loadVersion != _editModeLoadVersion || !this) return;

            _editModePackage = result.Package;
            _editModePreviewApplied = false;
            SessionDriver.PublishPackageChanged(_editModePackage);

            if (!result.IsSuccess)
                OseLog.Warn($"[EditModePreviewDriver] Edit-mode preview failed to load '{packageId}': {result.ErrorMessage}");

            TryApplyEditModePreview();
        }

        private void TryApplyEditModePreview()
        {
            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out var ui)) return;

            ui.SetSessionMode(_sessionMode);
            ui.ShowChallengeMetrics(0, 0, 0f, 0f, ResolveChallengeActive(_sessionMode, _editModePackage));

            if (_editModePackage == null)
            {
                ui.ShowStepShell(0, 0, "Preview Unavailable", "Package not loaded.");
                _editModePreviewApplied = true;
                return;
            }

            StepDefinition[] orderedSteps = _editModePackage.GetOrderedSteps();
            if (orderedSteps.Length == 0)
            {
                ui.ShowStepShell(0, 0, "No Steps", "Package has no steps authored.");
                _editModePreviewApplied = true;
                return;
            }

            StepDefinition step = ResolveStepBySequenceIndex(orderedSteps, _previewStepSequenceIndex);

            // Use the same push path as play mode — editor preview is visually identical to runtime.
            SessionDriver.PushStepAndPartToUI(ui, _editModePackage, step,
                step.sequenceIndex, orderedSteps.Length);

            _editModePreviewApplied = true;
            ApplyStepAwarePartPositions();
        }

        private void HidePreviewIfPossible()
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out var ui))
                ui.HideAll();
        }

        private void ApplyStepAwarePartPositions()
        {
            if (Application.isPlaying) return;
            if (ServiceRegistry.TryGet<IStepAwarePositioner>(out var positioner))
            {
                positioner.ApplyStepAwarePositions(_previewStepSequenceIndex, _editModePackage);
                UnityEditor.SceneView.RepaintAll();
            }
        }

        private static StepDefinition ResolveStepBySequenceIndex(StepDefinition[] orderedSteps, int sequenceIndex)
        {
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                if (orderedSteps[i] != null && orderedSteps[i].sequenceIndex == sequenceIndex)
                    return orderedSteps[i];
            }
            return orderedSteps[0];
        }

        private static bool ResolveChallengeActive(SessionMode mode, MachinePackageDefinition package)
        {
            if (mode != SessionMode.Challenge) return false;
            if (package?.challengeConfig != null) return package.challengeConfig.enabled;
            return true;
        }
    }
}
#endif
