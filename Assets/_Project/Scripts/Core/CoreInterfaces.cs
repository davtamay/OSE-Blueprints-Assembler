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
        event System.Action<CanonicalAction> OnAction;
        void SetContext(InputContext context);
        InputContext CurrentContext { get; }
    }

    public interface IPresentationAdapter
    {
        void SetSessionMode(SessionMode mode);
        bool IsHintDisplayAllowed { get; }
        void ShowInstruction(string instructionKey);
        void ShowHint(string hintKey);
        void ShowHintContent(string title, string message, string hintType);
        void ShowPartInfo(string partId);
        void ShowToolInfo(string toolId);
        void ShowProgressUpdate(int completedSteps, int totalSteps);
        void ShowMilestoneFeedback(string milestoneKey);
        void ShowStepShell(int currentStepNumber, int totalSteps, string title, string instruction, bool showConfirmButton = false, bool showHintButton = false, ConfirmGate confirmGate = ConfirmGate.None);
        void ShowPartInfoShell(string partName, string function, string material, string tool, string searchTerms);
        void ShowChallengeMetrics(int hintsUsed, int failedAttempts, float currentStepSeconds, float totalSeconds, bool challengeActive);
        void ShowStepCompletionToast(string message);
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
        RequestHint
    }
}
