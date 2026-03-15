using System;

namespace OSE.Core
{
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
