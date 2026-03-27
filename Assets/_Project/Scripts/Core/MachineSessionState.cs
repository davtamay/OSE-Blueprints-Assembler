using System;

namespace OSE.Core
{
    /// <summary>
    /// Serializable session snapshot. All fields are <c>public</c> because
    /// <c>JsonUtility</c> only serializes public fields — this is intentional
    /// and not an API surface leak. Mutate only through
    /// <see cref="OSE.Runtime.MachineSessionController"/>.
    /// </summary>
    [Serializable]
    public class MachineSessionState
    {
        // Machine identity
        public string MachineId;
        public string MachineVersion;
        public SessionMode Mode;

        // Progression
        public string CurrentAssemblyId;
        public string CurrentSubassemblyId;
        public string CurrentStepId;
        public float ElapsedSeconds;

        // Challenge
        public bool ChallengeActive;
        public int MistakeCount;
        public int HintsUsed;
        public float CurrentStepStartSeconds;
        public float CurrentStepElapsedSeconds;
        public float LastStepDurationSeconds;
        public float TotalStepDurationSeconds;
        public int CompletedStepCount;

        // Lifecycle
        public SessionLifecycle Lifecycle;
        public bool IsRestored;
    }
}
