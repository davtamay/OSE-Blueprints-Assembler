using System;
using OSE.App;
using OSE.Content;
using OSE.Core;

namespace OSE.Runtime.Preview
{
    /// <summary>
    /// Manages the machine intro overlay lifecycle: showing, self-healing, and dismissal.
    ///
    /// Extracted from <see cref="SessionDriver"/> following the constructor-injection coordinator
    /// pattern (same as <see cref="OSE.Interaction.Core.ToolActionCoordinator"/>). SessionDriver
    /// retains <c>HandleIntroReset</c> since that handler calls <c>RestartSession</c> which
    /// disposes this coordinator — keeping the reset path at the caller site avoids re-entrancy.
    ///
    /// Subscribes to:
    /// <list type="bullet">
    ///   <item><see cref="MachineIntroDismissed"/></item>
    ///   <item><see cref="AssemblyPickerRequested"/></item>
    ///   <item><see cref="AssemblyPickerDismissed"/></item>
    /// </list>
    /// Always call <see cref="Dispose"/> before discarding this instance to release subscriptions.
    /// </summary>
    internal sealed class IntroOverlayCoordinator
    {
        private readonly Func<IMachineSessionController> _getSession;
        private readonly Func<int> _getSavedCompletedSteps;
        private readonly Func<int> _getSavedTotalSteps;
        private readonly Action<bool> _setIntroActive;

        /// <summary>
        /// Called when the intro gate has been passed (intro dismissed or picker dismissed).
        /// SessionDriver uses this to set <c>_pendingStepUiPush</c> and call <c>PushStepToUI</c>.
        /// </summary>
        private readonly Action _onGatePassed;

        /// <summary>True while the intro overlay is visible and blocking step interaction.</summary>
        public bool IsActive { get; private set; }

        /// <summary>True once the user has dismissed the intro (or picker). Prevents re-showing.</summary>
        public bool IsDismissed { get; private set; }

        /// <param name="getSession">Returns the active session controller (may be null).</param>
        /// <param name="getSavedCompletedSteps">Returns the count of previously completed steps (for resume display).</param>
        /// <param name="getSavedTotalSteps">Returns the total step count from the last saved session.</param>
        /// <param name="setIntroActive">Called whenever <see cref="IsActive"/> changes; updates <c>SessionDriver.IsIntroActive</c>.</param>
        /// <param name="onGatePassed">Called when the intro gate is cleared so <c>SessionDriver</c> can push the step UI.</param>
        public IntroOverlayCoordinator(
            Func<IMachineSessionController> getSession,
            Func<int> getSavedCompletedSteps,
            Func<int> getSavedTotalSteps,
            Action<bool> setIntroActive,
            Action onGatePassed)
        {
            _getSession = getSession;
            _getSavedCompletedSteps = getSavedCompletedSteps;
            _getSavedTotalSteps = getSavedTotalSteps;
            _setIntroActive = setIntroActive;
            _onGatePassed = onGatePassed;

            RuntimeEventBus.Subscribe<MachineIntroDismissed>(HandleIntroDismissed);
            RuntimeEventBus.Subscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
            RuntimeEventBus.Subscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);
        }

        /// <summary>
        /// Unsubscribes all events and resets intro state.
        /// Call before discarding this instance (session restart, OnDisable, OnDestroy).
        /// </summary>
        public void Dispose()
        {
            RuntimeEventBus.Unsubscribe<MachineIntroDismissed>(HandleIntroDismissed);
            RuntimeEventBus.Unsubscribe<AssemblyPickerRequested>(HandleAssemblyPickerRequested);
            RuntimeEventBus.Unsubscribe<AssemblyPickerDismissed>(HandleAssemblyPickerDismissed);

            SetActive(false);
            IsDismissed = false;
        }

        /// <summary>
        /// Displays the machine intro overlay. Called after a package finishes loading.
        /// No-op if no machine definition is present or no UI adapter is registered.
        /// </summary>
        public void Show()
        {
            IMachineSessionController session = _getSession();
            if (session?.Package?.machine == null)
            {
                OseLog.Warn("[IntroOverlay] Show: no machine data — skipping intro.");
                SetActive(false);
                return;
            }

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter ui))
            {
                OseLog.Error("[IntroOverlay] Show: IPresentationAdapter not registered.");
                SetActive(false);
                return;
            }

            MachineDefinition machine = session.Package.machine;
            int savedTotal = _getSavedTotalSteps();
            int totalSteps = savedTotal > 0
                ? savedTotal
                : (session.Package?.GetOrderedSteps().Length ?? 0);

            ui.ShowMachineIntro(
                machine.GetDisplayName(),
                machine.description ?? string.Empty,
                machine.difficulty ?? string.Empty,
                machine.estimatedBuildTimeMinutes,
                machine.learningObjectives,
                machine.introImageRef,
                _getSavedCompletedSteps(),
                totalSteps);

            SetActive(ui.IsMachineIntroVisible);
        }

        /// <summary>
        /// Ensures the intro overlay remains visible while it hasn't been dismissed.
        /// Called every frame from <c>SessionDriver.UpdatePlayMode</c> to self-heal against
        /// first-frame UI build-order shifts.
        /// </summary>
        public void EnsureVisible()
        {
            if (IsDismissed || _getSession()?.Package?.machine == null)
                return;

            if (!ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter ui))
                return;

            if (ui.IsMachineIntroVisible)
            {
                SetActive(true);
                return;
            }

            Show();
        }

        // ── Event Handlers ──

        private void HandleIntroDismissed(MachineIntroDismissed evt)
        {
            OseLog.Info("[IntroOverlay] Machine intro dismissed. Handing off to step UI.");
            PassGate();
        }

        private void HandleAssemblyPickerRequested(AssemblyPickerRequested evt)
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter ui))
                ui.ShowAssemblyPicker();
        }

        private void HandleAssemblyPickerDismissed(AssemblyPickerDismissed evt)
        {
            if (ServiceRegistry.TryGet<IPresentationAdapter>(out IPresentationAdapter ui))
                ui.DismissAssemblyPicker();

            IMachineSessionController session = _getSession();
            if (!string.IsNullOrEmpty(evt.SelectedAssemblyId) && evt.GlobalStepIndex >= 0 && session != null)
            {
                OseLog.Info($"[IntroOverlay] Assembly picker: jumping to '{evt.SelectedAssemblyId}' (global step {evt.GlobalStepIndex}).");
                session.NavigateToGlobalStep(evt.GlobalStepIndex);
            }

            PassGate();
        }

        // ── Helpers ──

        /// <summary>Marks the intro gate as passed and notifies SessionDriver to push the step UI.</summary>
        private void PassGate()
        {
            IsDismissed = true;
            SetActive(false);
            _onGatePassed?.Invoke();
        }

        private void SetActive(bool value)
        {
            IsActive = value;
            _setIntroActive?.Invoke(value);
        }
    }
}
