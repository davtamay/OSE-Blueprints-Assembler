using System;
using OSE.Core;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Owns confirm gate state: current gate type, unlock status, and
    /// the suppress-on-first-call guard. Extracted from UIRootCoordinator (Phase 8).
    /// </summary>
    internal sealed class ConfirmGateController
    {
        private bool _suppressUnlock;

        public ConfirmGate Gate { get; private set; } = ConfirmGate.None;
        public bool IsUnlocked { get; set; } = true;
        public bool ProgressComplete { get; set; }

        /// <summary>
        /// Configures the gate for a new step.
        /// </summary>
        public void Configure(ConfirmGate gate, Func<bool> equipToolCheck)
        {
            Gate = gate;
            IsUnlocked = ResolveInitial(gate, equipToolCheck);
            ProgressComplete = false;
            _suppressUnlock = gate != ConfirmGate.None;
        }

        /// <summary>
        /// Called when a part is selected — unlocks SelectPart gates.
        /// Returns true if the unlock state changed (caller should refresh).
        /// </summary>
        public bool TryUnlockOnPartSelected()
        {
            if (_suppressUnlock)
            {
                _suppressUnlock = false;
                return false;
            }

            if (Gate == ConfirmGate.SelectPart && !IsUnlocked)
            {
                IsUnlocked = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called when a hint is shown — unlocks RequestHint gates.
        /// Returns true if the unlock state changed (caller should refresh).
        /// </summary>
        public bool TryUnlockOnHintRequested()
        {
            if (Gate == ConfirmGate.RequestHint && !IsUnlocked)
            {
                IsUnlocked = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called when the equip-tool gate needs checking.
        /// Returns true if the unlock state changed.
        /// </summary>
        public bool TryUpdateEquipToolGate(bool satisfied)
        {
            if (Gate != ConfirmGate.EquipTool)
                return false;

            if (IsUnlocked != satisfied)
            {
                IsUnlocked = satisfied;
                return true;
            }

            return false;
        }

        private static bool ResolveInitial(ConfirmGate gate, Func<bool> equipToolCheck)
        {
            if (gate == ConfirmGate.None)
                return true;

            if (gate == ConfirmGate.EquipTool)
                return equipToolCheck?.Invoke() ?? false;

            return false;
        }
    }
}
