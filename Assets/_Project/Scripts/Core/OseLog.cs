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

        // Dedup state: tight retry loops (e.g. session-failed-to-start firing every frame)
        // were hammering the WebGL console with thousands of identical lines per second,
        // making the app unresponsive and unreadable. We emit the first occurrence, suppress
        // identical follow-ups, and surface the suppressed count when a *different* message
        // arrives so users still see how often the failure recurred.
        private static string _lastMessage;
        private static int _suppressedCount;
        private static LogType _lastLevel;
        private static readonly object _dedupLock = new object();

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

        public static void Info(string message)  => Emit(LogType.Log,     $"{Prefix} {message}");
        public static void Warn(string message)  => Emit(LogType.Warning, $"{Prefix} {message}");
        public static void Error(string message) => Emit(LogType.Error,   $"{Prefix} {message}");

        /// <summary>Logs an error with a stable <see cref="OseErrorCode"/> prefix for filtering.</summary>
        public static void Error(OseErrorCode code, string message) =>
            Emit(LogType.Error, $"{Prefix}[{(int)code:D4}] {message}");

        /// <summary>Logs a warning with a stable <see cref="OseErrorCode"/> prefix for filtering.</summary>
        public static void Warn(OseErrorCode code, string message) =>
            Emit(LogType.Warning, $"{Prefix}[{(int)code:D4}] {message}");

        public static void VerboseInfo(string message)
        {
            if (Verbose)
                Emit(LogType.Log, $"{Prefix}:V {message}");
        }

        private static void Emit(LogType level, string fullMessage)
        {
            lock (_dedupLock)
            {
                if (_lastMessage == fullMessage)
                {
                    _suppressedCount++;
                    return;
                }

                if (_suppressedCount > 0)
                    WriteRaw(_lastLevel, $"{Prefix} (previous message repeated {_suppressedCount}x — suppressed)");

                _suppressedCount = 0;
                _lastMessage     = fullMessage;
                _lastLevel       = level;
            }

            WriteRaw(level, fullMessage);
        }

        private static void WriteRaw(LogType level, string fullMessage)
        {
            switch (level)
            {
                case LogType.Warning: Debug.LogWarning(fullMessage); break;
                case LogType.Error:   Debug.LogError(fullMessage);   break;
                default:              Debug.Log(fullMessage);        break;
            }
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
