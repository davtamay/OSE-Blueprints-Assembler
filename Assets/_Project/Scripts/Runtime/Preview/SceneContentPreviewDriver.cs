using System.Collections;
using System.Text;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SceneContentPreviewDriver : MonoBehaviour
    {
        [SerializeField] private SceneContentPreviewProfile _previewProfile = null;

        [Header("Debug Status")]
        [SerializeField, TextArea(2, 6)] private string _statusMessage = "Awaiting package load.";
        [SerializeField] private string _loadedMachineName = string.Empty;

        private readonly MachinePackageLoader _loader = new MachinePackageLoader();
        private MachinePackageDefinition _package;
        private bool _previewApplied;
        private bool _playModeAdvanceStarted;
        private int _loadVersion;
        private SceneContentPreviewProfile _builtInPreviewProfile;

        private void OnEnable()
        {
            _previewApplied = false;
            _playModeAdvanceStarted = false;
            RequestRefresh();
        }

        private void Update()
        {
            if (!Application.isPlaying && !ActiveProfile.PreviewInEditMode)
            {
                return;
            }

            if (!_previewApplied)
            {
                TryApplyPreview(ActiveProfile.PreviewStepSequenceIndex);
            }

            StartPlayModeAdvanceIfNeeded();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            RequestRefresh();
        }

        private void OnDestroy()
        {
            if (_builtInPreviewProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_builtInPreviewProfile);
            }
            else
            {
                DestroyImmediate(_builtInPreviewProfile);
            }

            _builtInPreviewProfile = null;
        }

        private void RequestRefresh()
        {
            _previewApplied = false;
            _playModeAdvanceStarted = false;
            _ = ReloadPackageAsync(++_loadVersion);
        }

        private async Task ReloadPackageAsync(int loadVersion)
        {
            string packageId = ActiveProfile.PackageId;
            _statusMessage = string.IsNullOrWhiteSpace(packageId)
                ? "Package id is required."
                : $"Loading package '{packageId}'...";

            MachinePackageLoadResult result = await _loader.LoadFromStreamingAssetsAsync(packageId);

            if (loadVersion != _loadVersion || !this)
            {
                return;
            }

            _package = result.Package;
            _previewApplied = false;
            _playModeAdvanceStarted = false;

            if (result.IsSuccess)
            {
                _loadedMachineName = _package.GetDisplayMachineName();
                _statusMessage = result.Validation.HasWarnings
                    ? $"Loaded '{_loadedMachineName}' with warnings."
                    : $"Loaded '{_loadedMachineName}'.";
            }
            else
            {
                _loadedMachineName = string.Empty;
                _statusMessage = BuildFailureMessage(result);
            }

            TryApplyPreview(ActiveProfile.PreviewStepSequenceIndex);
        }

        private bool TryApplyPreview(int stepSequenceIndex)
        {
            if (!ShouldApplyPreviewNow())
            {
                return false;
            }

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter presentationAdapter))
            {
                return false;
            }

            if (_package == null)
            {
                ApplyFailurePreview(
                    presentationAdapter,
                    "Scene Content Preview Unavailable",
                    _statusMessage);
                _previewApplied = true;
                return true;
            }

            StepDefinition[] orderedSteps = _package.GetOrderedSteps();
            if (orderedSteps.Length == 0)
            {
                ApplyFailurePreview(
                    presentationAdapter,
                    "Package Has No Steps",
                    $"{_package.GetDisplayMachineName()} loaded, but no steps were authored.");
                _previewApplied = true;
                return true;
            }

            StepDefinition selectedStep = ResolveStep(orderedSteps, stepSequenceIndex);
            PartDefinition selectedPart = ResolvePrimaryPart(selectedStep);

            presentationAdapter.ShowStepShell(
                selectedStep.sequenceIndex,
                orderedSteps.Length,
                selectedStep.GetDisplayName(),
                selectedStep.BuildInstructionBody());

            presentationAdapter.ShowPartInfoShell(
                selectedPart != null ? selectedPart.GetDisplayName() : "Referenced Part Missing",
                selectedPart != null ? selectedPart.function : $"No part metadata was found for step '{selectedStep.GetDisplayName()}'.",
                selectedPart != null ? selectedPart.material : "Material metadata missing.",
                ResolveToolSummary(selectedStep, selectedPart),
                ResolveSearchTerms(selectedPart));

            _previewApplied = true;
            return true;
        }

        private void ApplyFailurePreview(
            IPresentationAdapter presentationAdapter,
            string title,
            string instruction)
        {
            presentationAdapter.ShowStepShell(0, 0, title, instruction);
            presentationAdapter.ShowPartInfoShell(
                string.IsNullOrWhiteSpace(ActiveProfile.PackageId) ? "No Package Id" : ActiveProfile.PackageId,
                instruction,
                _loadedMachineName,
                MachinePackageLoader.BuildMachineJsonPath(ActiveProfile.PackageId),
                "scene content preview");
        }

        private void StartPlayModeAdvanceIfNeeded()
        {
            if (!Application.isPlaying || !ActiveProfile.AdvanceOnPlay || _playModeAdvanceStarted || !_previewApplied)
            {
                return;
            }

            _playModeAdvanceStarted = true;
            StartCoroutine(AdvancePreviewStepCoroutine());
        }

        private IEnumerator AdvancePreviewStepCoroutine()
        {
            yield return new WaitForSeconds(ActiveProfile.PlayModeAdvanceDelay);
            _previewApplied = false;
            TryApplyPreview(ActiveProfile.PlayModeStepSequenceIndex);
        }

        private bool ShouldApplyPreviewNow() =>
            Application.isPlaying || ActiveProfile.PreviewInEditMode;

        private static StepDefinition ResolveStep(StepDefinition[] orderedSteps, int sequenceIndex)
        {
            for (int i = 0; i < orderedSteps.Length; i++)
            {
                StepDefinition step = orderedSteps[i];
                if (step != null && step.sequenceIndex == sequenceIndex)
                {
                    return step;
                }
            }

            return orderedSteps[0];
        }

        private PartDefinition ResolvePrimaryPart(StepDefinition step)
        {
            string partId = ResolvePrimaryPartId(step);
            if (string.IsNullOrWhiteSpace(partId))
            {
                return null;
            }

            return _package.TryGetPart(partId, out PartDefinition part) ? part : null;
        }

        private static string ResolvePrimaryPartId(StepDefinition step)
        {
            if (step == null)
            {
                return string.Empty;
            }

            string requiredPartId = GetFirstValue(step.requiredPartIds);
            if (!string.IsNullOrWhiteSpace(requiredPartId))
            {
                return requiredPartId;
            }

            return GetFirstValue(step.optionalPartIds);
        }

        private string ResolveToolSummary(StepDefinition step, PartDefinition part)
        {
            StringBuilder builder = new StringBuilder();
            AppendTools(builder, step != null ? step.relevantToolIds : null);

            if (builder.Length == 0 && part != null)
            {
                AppendTools(builder, part.toolIds);
            }

            return builder.Length > 0
                ? builder.ToString()
                : "No tool metadata referenced for this preview step.";
        }

        private void AppendTools(StringBuilder builder, string[] toolIds)
        {
            if (toolIds == null)
            {
                return;
            }

            for (int i = 0; i < toolIds.Length; i++)
            {
                string toolId = toolIds[i];
                if (string.IsNullOrWhiteSpace(toolId) || !_package.TryGetTool(toolId, out ToolDefinition tool))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(tool.GetDisplayName());
            }
        }

        private static string ResolveSearchTerms(PartDefinition part)
        {
            if (part == null || part.searchTerms == null || part.searchTerms.Length == 0)
            {
                return "No search terms authored for this part.";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < part.searchTerms.Length; i++)
            {
                string searchTerm = part.searchTerms[i];
                if (string.IsNullOrWhiteSpace(searchTerm))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(searchTerm.Trim());
            }

            return builder.Length > 0
                ? builder.ToString()
                : "No search terms authored for this part.";
        }

        private static string GetFirstValue(string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i].Trim();
                }
            }

            return string.Empty;
        }

        private static string BuildFailureMessage(MachinePackageLoadResult result)
        {
            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                builder.Append(result.ErrorMessage.Trim());
            }

            if (result.Validation != null && result.Validation.Issues.Length > 0)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine();
                }

                builder.Append(result.Validation.FormatSummary());
            }

            return builder.Length > 0
                ? builder.ToString()
                : "Scene content preview failed to load.";
        }

        private SceneContentPreviewProfile ActiveProfile
        {
            get
            {
                if (_previewProfile != null)
                {
                    return _previewProfile;
                }

                _builtInPreviewProfile ??= SceneContentPreviewProfile.CreateBuiltInDefault();
                return _builtInPreviewProfile;
            }
        }
    }
}
