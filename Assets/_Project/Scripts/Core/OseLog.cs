using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OSE.Core
{
    public static class OseLog
    {
        public static bool Verbose { get; set; } = false;

        private static string _sessionTag;
        private static string _prefix = "[OSE]";

        /// <summary>
        /// Sets a short correlation tag (e.g. a session GUID prefix) that is
        /// prepended to every log line.  Call once on session start; call with
        /// <c>null</c> to clear.
        /// </summary>
        public static void SetSessionTag(string tag)
        {
            _sessionTag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
            _prefix     = _sessionTag != null ? $"[OSE][{_sessionTag}]" : "[OSE]";
        }

        private static string Prefix => _prefix;

        public static void Info(string message) =>
            Debug.Log($"{Prefix} {message}");

        public static void Warn(string message) =>
            Debug.LogWarning($"{Prefix} {message}");

        public static void Error(string message) =>
            Debug.LogError($"{Prefix} {message}");

        /// <summary>Logs an error with a stable <see cref="OseErrorCode"/> prefix for filtering.</summary>
        public static void Error(OseErrorCode code, string message) =>
            Debug.LogError($"{Prefix}[{(int)code:D4}] {message}");

        /// <summary>Logs a warning with a stable <see cref="OseErrorCode"/> prefix for filtering.</summary>
        public static void Warn(OseErrorCode code, string message) =>
            Debug.LogWarning($"{Prefix}[{(int)code:D4}] {message}");

        public static void VerboseInfo(string message)
        {
            if (Verbose)
                Debug.Log($"{Prefix}:V {message}");
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

        /// <summary>
        /// Creates a scoped timer that logs elapsed time on Dispose.
        /// Usage: <c>using (OseLog.Timed("Loading package")) { ... }</c>
        /// </summary>
        public static TimedScope Timed(string operationName) => new TimedScope(operationName);

        public readonly struct TimedScope : IDisposable
        {
            private readonly string _operation;
            private readonly long _startTicks;

            internal TimedScope(string operation)
            {
                _operation = operation;
                _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                long elapsed = Stopwatch.GetTimestamp() - _startTicks;
                double ms = (double)elapsed / Stopwatch.Frequency * 1000.0;
                Info($"[Timing] {_operation}: {ms:F1}ms");
            }
        }
    }
}
