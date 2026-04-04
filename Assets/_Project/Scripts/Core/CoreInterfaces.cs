using System.Threading;
using System.Threading.Tasks;

namespace OSE.Core
{
    public interface IContentLoader
    {
        Task<MachineSessionState> LoadMachineAsync(string machineId, CancellationToken cancellationToken = default);
        bool IsVersionCompatible(string machineId, string contentVersion);
    }

    public interface IPlacementValidator
    {
        PlacementValidationResult Validate(PlacementValidationRequest request);
    }

    public interface IEffectPlayer
    {
        void Play(EffectRole role, UnityEngine.Vector3 worldPosition);
        void Stop(EffectRole role);
        bool IsPlaying(EffectRole role);

        /// <summary>
        /// Triggers a controller haptic pulse. No-op on non-XR platforms.
        /// </summary>
        /// <param name="amplitude">Vibration strength, 0–1.</param>
        /// <param name="duration">Duration in seconds.</param>
        void PlayHaptic(EffectRole role, float amplitude = 0.3f, float duration = 0.08f);
    }

    public interface IPersistenceService
    {
        void SaveSession(MachineSessionState state);
        MachineSessionState LoadSession(string machineId);
        bool HasSavedSession(string machineId);
        void ClearSession(string machineId);
    }

    public interface IInputRouter
    {
        void SetContext(InputContext context);
        InputContext CurrentContext { get; }
        void InjectAction(CanonicalAction action);
    }

    /// <summary>Hint-display capability — safe to depend on from HintManager.</summary>
    public interface IHintPresenter
    {
        bool IsHintDisplayAllowed { get; }
        void ShowHint(string hintKey);
        void ShowHintContent(string title, string message, string hintType);
    }

    /// <summary>Step-level feedback capability — instruction text, progress, toasts.</summary>
    public interface IStepPresenter
    {
        void ShowInstruction(string instructionKey);
        void ShowProgressUpdate(int completedSteps, int totalSteps);
        void ShowMilestoneFeedback(string milestoneKey);
        void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction, bool showConfirmButton = false, bool showHintButton = false, ConfirmGate confirmGate = ConfirmGate.None);
        void ShowChallengeMetrics(int hintsUsed, int failedAttempts, float currentStepSeconds, float totalSeconds, bool challengeActive);
        void ShowStepCompletionToast(string message);
    }

    /// <summary>Part/tool info panel capability.</summary>
    public interface IPartInfoPresenter
    {
        void ShowPartInfo(string partId);
        void ShowToolInfo(string toolId);
        void ShowPartInfoShell(string partName, string function, string material, string tool, string searchTerms);
        void HidePartInfoPanel();
    }

    /// <summary>Machine intro overlay capability.</summary>
    public interface IMachineIntroPresenter
    {
        bool IsMachineIntroVisible { get; }
        void ResetMachineIntroState();
        void ShowMachineIntro(string title, string description, string difficulty, int estimatedMinutes, string[] learningObjectives, string imageRef, int savedCompletedSteps = 0, int savedTotalSteps = 0);
        void DismissMachineIntro();
    }

    /// <summary>Assembly section picker capability.</summary>
    public interface IAssemblyPickerPresenter
    {
        bool IsAssemblyPickerVisible { get; }
        void ShowAssemblyPicker();
        void DismissAssemblyPicker();
    }

    /// <summary>
    /// Full presentation surface. Composes all focused sub-interfaces so
    /// existing consumers keep compiling. Prefer narrower interfaces for new
    /// dependencies (e.g. IHintPresenter, IStepPresenter).
    /// </summary>
    public interface IPresentationAdapter
        : IHintPresenter, IStepPresenter, IPartInfoPresenter,
          IMachineIntroPresenter, IAssemblyPickerPresenter
    {
        void SetSessionMode(SessionMode mode);
        void HideAll();
    }

    public enum CanonicalAction
    {
        Select,
        Inspect,
        Grab,
        Move,
        Rotate,
        Place,
        Confirm,
        Cancel,
        Navigate,
        Zoom,
        Orbit,
        RequestHint,
        ToggleToolMenu,
        ToolPrimaryAction,
        Next,
        Previous,
        Pause,
        TogglePhysicalMode,
        ChallengeRestart
    }

    public enum InputContext
    {
        None,
        Frontend,
        MachineSelection,
        SessionActive,
        StepInteraction,
        Inspection,
        Paused,
        ChallengeSummary
    }

    public enum ConfirmGate
    {
        None,
        SelectPart,
        RequestHint,
        EquipTool
    }
}
