using UnityEngine;

namespace OSE.Core
{
    public static class OseLog
    {
        public static bool Verbose { get; set; } = false;

        public static void Info(string message) =>
            Debug.Log($"[OSE] {message}");

        public static void Warn(string message) =>
            Debug.LogWarning($"[OSE] {message}");

        public static void Error(string message) =>
            Debug.LogError($"[OSE] {message}");

        public static void VerboseInfo(string message)
        {
            if (Verbose)
                Debug.Log($"[OSE:V] {message}");
        }

        public static void StepEvent(string stepId, StepState state) =>
            VerboseInfo($"[Step] {stepId} → {state}");

        public static void SessionEvent(string machineId, SessionLifecycle lifecycle) =>
            Info($"[Session] {machineId} → {lifecycle}");

        public static void ValidationEvent(string stepId, bool isValid, ValidationFailureReason reason = ValidationFailureReason.None)
        {
            if (isValid)
                VerboseInfo($"[Validation] {stepId} VALID");
            else
                Info($"[Validation] {stepId} INVALID ({reason})");
        }
    }
}
