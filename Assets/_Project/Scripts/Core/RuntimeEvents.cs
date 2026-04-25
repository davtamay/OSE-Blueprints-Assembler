using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OSE.Core
{
    /// <summary>
    /// Lightweight generic event bus for decoupled runtime communication.
    /// All runtime systems publish and subscribe through this static hub.
    /// </summary>
    public static class RuntimeEventBus
    {
        // Tracks all types that have ever been subscribed so Clear() can reset them.
        // Only written on Subscribe (cold path); never read on Publish (hot path).
        private static readonly List<Action> _clearActions = new List<Action>();

        /// <summary>
        /// Per-type static handler store. Generic static fields are instantiated once
        /// per concrete T by the CLR, giving O(1) field access with zero dictionary
        /// lookup on the hot Publish path.
        /// </summary>
        private static class Listener<T> where T : struct
        {
            // ReSharper disable once StaticMemberInGenericType
            internal static Action<T> Handler;
            // ReSharper disable once StaticMemberInGenericType
            internal static bool IsTracked;
        }

        public static void Subscribe<T>(Action<T> listener) where T : struct
        {
            // Register a clear action once per type — survives Clear() calls so
            // re-subscribing after Clear doesn't add a duplicate clear action.
            if (!Listener<T>.IsTracked)
            {
                _clearActions.Add(() => Listener<T>.Handler = null);
                Listener<T>.IsTracked = true;
            }

            Listener<T>.Handler = (Action<T>)Delegate.Combine(Listener<T>.Handler, listener);
        }

        public static void Unsubscribe<T>(Action<T> listener) where T : struct
        {
            Listener<T>.Handler = (Action<T>)Delegate.Remove(Listener<T>.Handler, listener);
        }

        /// <summary>
        /// Publishes <paramref name="evt"/> to all current subscribers of type <typeparamref name="T"/>.
        /// Zero dictionary lookup — resolves directly to the per-type static handler.
        /// </summary>
        public static void Publish<T>(T evt) where T : struct
        {
            // Snapshot before invoke so a handler that calls Unsubscribe<T> during
            // execution doesn't affect the current dispatch (Delegate is immutable).
            Listener<T>.Handler?.Invoke(evt);
        }

        /// <summary>
        /// Resets all handlers. <b>Test-only.</b> Production code must Unsubscribe
        /// in symmetric pairs with Subscribe — never bulk-clear, or in-flight
        /// systems will silently lose their listeners.
        /// </summary>
        /// <remarks>
        /// Hidden from IntelliSense (<see cref="EditorBrowsableAttribute"/>) so it
        /// does not surface in autocompletion outside test code. Still callable
        /// for the test fixtures that legitimately depend on it.
        /// </remarks>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void Clear()
        {
            // Reset all handlers. Keep _clearActions and IsTracked so re-subscribing
            // after Clear doesn't double-register clear actions.
            foreach (var clear in _clearActions)
                clear();
        }
    }

    // ── Session Events ──

    public readonly struct SessionLifecycleChanged
    {
        public readonly string MachineId;
        public readonly SessionLifecycle Previous;
        public readonly SessionLifecycle Current;

        public SessionLifecycleChanged(string machineId, SessionLifecycle previous, SessionLifecycle current)
        {
            MachineId = machineId;
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct SessionCompleted
    {
        public readonly string MachineId;
        public readonly float TotalSeconds;

        public SessionCompleted(string machineId, float totalSeconds)
        {
            MachineId = machineId;
            TotalSeconds = totalSeconds;
        }
    }

    // ── Step Events ──

    public readonly struct StepActivated
    {
        public readonly string StepId;
        public readonly string AssemblyId;
        public readonly int StepIndex;
        public readonly int TotalSteps;

        public StepActivated(string stepId, string assemblyId, int stepIndex, int totalSteps)
        {
            StepId = stepId;
            AssemblyId = assemblyId;
            StepIndex = stepIndex;
            TotalSteps = totalSteps;
        }
    }

    public readonly struct StepStateChanged
    {
        public readonly string StepId;
        public readonly StepState Previous;
        public readonly StepState Current;
        public readonly float AtSeconds;

        public StepStateChanged(string stepId, StepState previous, StepState current, float atSeconds)
        {
            StepId = stepId;
            Previous = previous;
            Current = current;
            AtSeconds = atSeconds;
        }
    }

    public readonly struct HintRequested
    {
        public readonly string StepId;
        public readonly int TotalHintsForStep;

        public HintRequested(string stepId, int totalHintsForStep)
        {
            StepId = stepId;
            TotalHintsForStep = totalHintsForStep;
        }
    }

    // ── Tool Events ──

    public readonly struct ActiveToolChanged
    {
        public readonly string PreviousToolId;
        public readonly string CurrentToolId;
        public readonly bool IsRequiredForActiveStep;

        public ActiveToolChanged(string previousToolId, string currentToolId, bool isRequiredForActiveStep)
        {
            PreviousToolId = previousToolId;
            CurrentToolId = currentToolId;
            IsRequiredForActiveStep = isRequiredForActiveStep;
        }
    }

    public readonly struct ToolActionProgressed
    {
        public readonly string StepId;
        public readonly string ToolId;
        public readonly ToolActionType ActionType;
        public readonly int CurrentCount;
        public readonly int RequiredCount;
        public readonly string Message;

        public ToolActionProgressed(
            string stepId,
            string toolId,
            ToolActionType actionType,
            int currentCount,
            int requiredCount,
            string message)
        {
            StepId = stepId;
            ToolId = toolId;
            ActionType = actionType;
            CurrentCount = currentCount;
            RequiredCount = requiredCount;
            Message = message;
        }
    }

    public readonly struct ToolActionCompleted
    {
        public readonly string StepId;
        public readonly string ToolId;
        public readonly ToolActionType ActionType;
        public readonly int RequiredCount;
        public readonly string Message;

        public ToolActionCompleted(
            string stepId,
            string toolId,
            ToolActionType actionType,
            int requiredCount,
            string message)
        {
            StepId = stepId;
            ToolId = toolId;
            ActionType = actionType;
            RequiredCount = requiredCount;
            Message = message;
        }
    }

    public readonly struct ToolActionFailed
    {
        public readonly string StepId;
        public readonly string RequiredToolId;
        public readonly string ActiveToolId;
        public readonly ToolActionType ActionType;
        public readonly ToolActionFailureReason FailureReason;
        public readonly string Message;

        public ToolActionFailed(
            string stepId,
            string requiredToolId,
            string activeToolId,
            ToolActionType actionType,
            ToolActionFailureReason failureReason,
            string message)
        {
            StepId = stepId;
            RequiredToolId = requiredToolId;
            ActiveToolId = activeToolId;
            ActionType = actionType;
            FailureReason = failureReason;
            Message = message;
        }
    }

    // ── Part Events ──

    public readonly struct PartStateChanged
    {
        public readonly string PartId;
        public readonly string StepId;
        public readonly PartPlacementState Previous;
        public readonly PartPlacementState Current;

        public PartStateChanged(string partId, string stepId, PartPlacementState previous, PartPlacementState current)
        {
            PartId = partId;
            StepId = stepId;
            Previous = previous;
            Current = current;
        }
    }

    public readonly struct PlacementAttempted
    {
        public readonly string PartId;
        public readonly string TargetId;
        public readonly bool IsValid;
        public readonly float PositionError;
        public readonly float RotationError;

        public PlacementAttempted(string partId, string targetId, bool isValid, float positionError, float rotationError)
        {
            PartId = partId;
            TargetId = targetId;
            IsValid = isValid;
            PositionError = positionError;
            RotationError = rotationError;
        }
    }

    // ── Step Readiness Events ──

    public readonly struct StepReadinessChecked
    {
        public readonly string StepId;
        public readonly bool IsReady;
        /// <summary>Total number of preflight issues found (0 = step is ready).</summary>
        public readonly int IssueCount;
        /// <summary>First issue message for logging; null when <see cref="IsReady"/> is true.</summary>
        public readonly string FirstIssue;

        public StepReadinessChecked(string stepId, bool isReady, int issueCount, string firstIssue)
        {
            StepId = stepId;
            IsReady = isReady;
            IssueCount = issueCount;
            FirstIssue = firstIssue;
        }
    }

    // ── Assembly Events ──

    public readonly struct AssemblyStarted
    {
        public readonly string AssemblyId;

        public AssemblyStarted(string assemblyId)
        {
            AssemblyId = assemblyId;
        }
    }

    public readonly struct AssemblyCompleted
    {
        public readonly string AssemblyId;

        public AssemblyCompleted(string assemblyId)
        {
            AssemblyId = assemblyId;
        }
    }

    /// <summary>
    /// Published when one assembly completes and another is queued to begin.
    /// The UI layer shows a transition overlay before resuming.
    /// </summary>
    public readonly struct AssemblyTransitionRequested
    {
        public readonly string CompletedAssemblyName;
        public readonly string NextAssemblyId;
        public readonly string NextAssemblyName;
        public readonly string NextAssemblyDescription;
        public readonly string NextLearningFocus;
        public readonly int CompletedModuleIndex;
        public readonly int TotalModules;
        public readonly int CompletedStepsGlobal;
        public readonly int TotalStepsGlobal;

        public AssemblyTransitionRequested(
            string completedAssemblyName,
            string nextAssemblyId,
            string nextAssemblyName,
            string nextAssemblyDescription,
            string nextLearningFocus,
            int completedModuleIndex,
            int totalModules,
            int completedStepsGlobal,
            int totalStepsGlobal)
        {
            CompletedAssemblyName = completedAssemblyName;
            NextAssemblyId = nextAssemblyId;
            NextAssemblyName = nextAssemblyName;
            NextAssemblyDescription = nextAssemblyDescription;
            NextLearningFocus = nextLearningFocus;
            CompletedModuleIndex = completedModuleIndex;
            TotalModules = totalModules;
            CompletedStepsGlobal = completedStepsGlobal;
            TotalStepsGlobal = totalStepsGlobal;
        }
    }

    public readonly struct MachineIntroDismissed
    {
        public readonly string MachineId;

        public MachineIntroDismissed(string machineId)
        {
            MachineId = machineId;
        }
    }

    public readonly struct MachineIntroReset
    {
        public readonly string MachineId;

        public MachineIntroReset(string machineId)
        {
            MachineId = machineId;
        }
    }

    /// <summary>
    /// Published after a saved session is restored. Carries the number of
    /// steps that were skipped so visual-layer listeners can position
    /// completed parts without replaying step events.
    /// </summary>
    public readonly struct SessionRestored
    {
        public readonly int CompletedStepCount;

        public SessionRestored(int completedStepCount)
        {
            CompletedStepCount = completedStepCount;
        }
    }

    /// <summary>
    /// Published when the user navigates to a different step via back/forward controls.
    /// The visual layer uses this to reposition parts and clear/re-spawn previews.
    /// Fires BEFORE StepStateChanged(Active) for the target step.
    /// </summary>
    public readonly struct StepNavigated
    {
        public readonly int PreviousStepIndex;
        public readonly int TargetStepIndex;
        public readonly int TotalSteps;

        public StepNavigated(int previousStepIndex, int targetStepIndex, int totalSteps)
        {
            PreviousStepIndex = previousStepIndex;
            TargetStepIndex = targetStepIndex;
            TotalSteps = totalSteps;
        }
    }

    public readonly struct RepositionModeChanged
    {
        public readonly bool IsActive;

        public RepositionModeChanged(bool isActive)
        {
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Published by <c>ConfirmInspectionService</c> when every inspection target
    /// on the active Confirm step has been framed by the camera. Triggers the
    /// UI layer to unlock the Confirm button.
    /// </summary>
    public readonly struct ObserveTargetsCompleted { }

    // ── Input / Selection Events ──
    // These bridge C# events from InputActionRouter, SelectionService, and
    // PackagePartSpawner into RuntimeEventBus so subscribers can use a single
    // event pattern throughout the codebase.

    public readonly struct CanonicalActionDispatched
    {
        public readonly CanonicalAction Action;

        public CanonicalActionDispatched(CanonicalAction action)
        {
            Action = action;
        }
    }

    public readonly struct PartSelected
    {
        public readonly UnityEngine.GameObject Target;

        public PartSelected(UnityEngine.GameObject target)
        {
            Target = target;
        }
    }

    public readonly struct PartDeselected
    {
        public readonly UnityEngine.GameObject Target;

        public PartDeselected(UnityEngine.GameObject target)
        {
            Target = target;
        }
    }

    public readonly struct PartInspected
    {
        public readonly UnityEngine.GameObject Target;

        public PartInspected(UnityEngine.GameObject target)
        {
            Target = target;
        }
    }

    public readonly struct SpawnerPartsReady
    {
    }

    /// <summary>
    /// Published by <c>ToolActionPreviewController</c> when the Action phase
    /// opens (after Approach completes, before the first progress tick).
    /// Consumed by <c>AnimationCueCoordinator</c> to register onDuringAction
    /// cues so progress-ranged cues see ticks for the CURRENT action. The
    /// old call site in <c>UseStepHandler.TryExecuteToolPrimaryAction</c>
    /// fires off the preview's onComplete callback (Return phase end), which
    /// registered cues one action late — this event fixes that off-by-one.
    /// </summary>
    public readonly struct ToolActionStarted
    {
        public readonly string Profile;
        public ToolActionStarted(string profile) => Profile = profile;
    }

    /// <summary>
    /// Published each frame by <c>ToolActionPreviewController</c> during the
    /// action phase with the normalised 0..1 progress. Consumed by the
    /// animation cue coordinator to drive progress-ranged
    /// <c>onDuringAction</c> cues (weld bead extension, drill vibration
    /// ramp, etc.). Progress is monotonic per action and resets at the
    /// start of each new action.
    /// </summary>
    public readonly struct ToolActionProgressTick
    {
        public readonly float Progress;
        public ToolActionProgressTick(float progress) => Progress = progress;
    }

    /// <summary>
    /// Published by <see cref="OSE.UI.Root.PackagePartSpawner"/> immediately after each
    /// individual GLB model swaps in to replace its placeholder during async loading.
    /// Allows the visual system to re-apply the correct material state for this part
    /// before all other GLBs have finished loading (avoids pink-material flash).
    /// </summary>
    public readonly struct SpawnerPartSwapped
    {
        public readonly string PartId;
        public SpawnerPartSwapped(string partId) => PartId = partId;
    }

    // ── Cue Camera Follow Events ──

    /// <summary>
    /// Published by <c>PoseTransitionPlayer</c> when it begins animating a
    /// single-target pose transition whose from-world-center and
    /// to-world-center differ meaningfully (i.e. the object is actually
    /// moving, not just rotating around a stationary centroid).
    ///
    /// <para>The <see cref="AssemblyCameraRig"/> subscribes to drive the
    /// camera pivot along the same eased trajectory, so the camera follows
    /// the motion tightly instead of leaving the object to drift to the
    /// edge of frame. FromWorld / ToWorld are resolved in world space at
    /// cue start — they represent where the object is right now and where
    /// it'll land when the cue finishes.</para>
    ///
    /// <para><see cref="Token"/> is an opaque reference to the originating
    /// cue instance; a matching <see cref="CueCameraFollowStopped"/> with
    /// the same token ends the follow (e.g. on Stop / cue teardown).
    /// Consumers may also stop following naturally when the follow timer
    /// reaches <see cref="DurationSeconds"/>.</para>
    /// </summary>
    public readonly struct CueCameraFollowStarted
    {
        public readonly object Token;
        public readonly UnityEngine.Vector3 FromWorld;
        public readonly UnityEngine.Vector3 ToWorld;
        public readonly float DurationSeconds;
        public readonly string Easing;

        public CueCameraFollowStarted(object token, UnityEngine.Vector3 fromWorld, UnityEngine.Vector3 toWorld, float durationSeconds, string easing)
        {
            Token           = token;
            FromWorld       = fromWorld;
            ToWorld         = toWorld;
            DurationSeconds = durationSeconds;
            Easing          = easing ?? string.Empty;
        }
    }

    /// <summary>
    /// Published when a follow-in-progress should cancel — e.g. the cue
    /// stopped mid-animation. Consumer snaps the target pivot to whatever
    /// position the follow interpolation had reached and stops driving.
    /// </summary>
    public readonly struct CueCameraFollowStopped
    {
        public readonly object Token;
        public CueCameraFollowStopped(object token) => Token = token;
    }

    // ── Package Events ──

    /// <summary>
    /// Published by <see cref="OSE.Runtime.Preview.SessionDriver"/> whenever a machine
    /// package finishes loading, in both edit and play mode. Replaces the legacy
    /// <c>SessionDriver.PackageChanged</c> static C# event.
    /// Consumers that need the full definition read <c>SessionDriver.CurrentPackage</c>,
    /// which is set before this event fires.
    /// </summary>
    public readonly struct PackageLoaded
    {
        public readonly string PackageId;
        public PackageLoaded(string packageId) { PackageId = packageId; }
    }

    // ── Assembly Picker Events ──

    /// <summary>
    /// Published when the user requests to see the assembly section picker.
    /// Fired from the intro overlay ("Choose Section") or the step panel HUD button.
    /// </summary>
    public readonly struct AssemblyPickerRequested
    {
    }

    /// <summary>
    /// Published when the user dismisses the assembly picker, optionally selecting a section.
    /// </summary>
    public readonly struct AssemblyPickerDismissed
    {
        /// <summary>Selected assembly ID, or null if the user tapped "Resume".</summary>
        public readonly string SelectedAssemblyId;
        /// <summary>Global step index of the first step in the selected assembly, or -1.</summary>
        public readonly int GlobalStepIndex;

        public AssemblyPickerDismissed(string selectedAssemblyId, int globalStepIndex)
        {
            SelectedAssemblyId = selectedAssemblyId;
            GlobalStepIndex = globalStepIndex;
        }
    }

    /// <summary>
    /// Published by <see cref="OSE.Runtime.IMachineSessionController.HotReloadTargetPlacement"/>
    /// when an authoring tool (TTAW) edits a target's preview placement during
    /// Play. Spawned target marker hosts subscribe to this event and re-apply
    /// the new transform to the live marker GameObject for the matching target
    /// id. Cosmetic / spatial only — no step-state implications.
    /// </summary>
    public readonly struct TargetPlacementHotReloaded
    {
        public readonly string TargetId;
        // Position/rotation/scale carried as raw floats so the event lives in
        // OSE.Core (which has no UnityEngine reference). The subscriber
        // converts to Vector3/Quaternion at the call site.
        public readonly float PosX, PosY, PosZ;
        public readonly float RotX, RotY, RotZ, RotW;
        public readonly float SclX, SclY, SclZ;

        public TargetPlacementHotReloaded(
            string targetId,
            float posX, float posY, float posZ,
            float rotX, float rotY, float rotZ, float rotW,
            float sclX, float sclY, float sclZ)
        {
            TargetId = targetId;
            PosX = posX; PosY = posY; PosZ = posZ;
            RotX = rotX; RotY = rotY; RotZ = rotZ; RotW = rotW;
            SclX = sclX; SclY = sclY; SclZ = sclZ;
        }
    }
}
