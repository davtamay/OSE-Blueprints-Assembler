using System.IO;
using OSE.App;
using OSE.Core;
using UnityEngine;

namespace OSE.Persistence
{
    /// <summary>
    /// Persists MachineSessionState to per-machine JSON files under
    /// <see cref="Application.persistentDataPath"/>/ose_sessions/.
    /// Preferred over <see cref="PlayerPrefsPersistenceService"/> for standalone and
    /// mobile builds where the 1 MB PlayerPrefs cap is a practical concern.
    /// Not suitable for WebGL (no persistent file system between sessions).
    /// </summary>
    public sealed class FilePersistenceService : IPersistenceService
    {
        private readonly string _sessionDir;

        public FilePersistenceService()
        {
            _sessionDir = Path.Combine(Application.persistentDataPath, "ose_sessions");
        }

        public void SaveSession(MachineSessionState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.MachineId))
                return;

            EnsureDirectory();
            string path = GetPath(state.MachineId);
            string json = JsonUtility.ToJson(state, prettyPrint: false);

            try
            {
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                OseLog.VerboseInfo($"[Persistence] Saved session for '{state.MachineId}' (step {state.CompletedStepCount}) → {path}");
            }
            catch (System.Exception ex)
            {
                OseLog.Error(OseErrorCode.SessionRestoreFailed,
                    $"[Persistence] Failed to write session file for '{state.MachineId}': {ex.Message}");
            }
        }

        public MachineSessionState LoadSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return null;

            string path = GetPath(machineId);
            if (!File.Exists(path))
                return null;

            string json;
            try
            {
                json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            }
            catch (System.Exception ex)
            {
                OseLog.Error(OseErrorCode.SessionRestoreFailed,
                    $"[Persistence] Failed to read session file for '{machineId}': {ex.Message}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(json))
                return null;

            MachineSessionState state;
            try
            {
                state = JsonUtility.FromJson<MachineSessionState>(json);
            }
            catch (System.Exception ex)
            {
                OseLog.Error(OseErrorCode.SessionRestoreFailed,
                    $"[Persistence] Corrupt session file for '{machineId}' — deleting. ({ex.Message})");
                TryDelete(path, machineId);
                return null;
            }

            if (state == null || string.IsNullOrWhiteSpace(state.MachineId))
            {
                OseLog.Warn(OseErrorCode.SessionRestoreFailed,
                    $"[Persistence] Session file for '{machineId}' deserialized to null or missing ID — deleting.");
                TryDelete(path, machineId);
                return null;
            }

            OseLog.VerboseInfo($"[Persistence] Loaded session for '{machineId}' (step {state.CompletedStepCount}).");
            return state;
        }

        public bool HasSavedSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return false;

            return File.Exists(GetPath(machineId));
        }

        public void ClearSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return;

            string path = GetPath(machineId);
            TryDelete(path, machineId);
            OseLog.Info($"[Persistence] Cleared saved session for '{machineId}'.");
        }

        private string GetPath(string machineId)
            => Path.Combine(_sessionDir, $"session_{SanitizeId(machineId)}.json");

        private void EnsureDirectory()
        {
            if (!Directory.Exists(_sessionDir))
                Directory.CreateDirectory(_sessionDir);
        }

        private static void TryDelete(string path, string machineId)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (System.Exception ex)
            {
                OseLog.Warn($"[Persistence] Could not delete session file for '{machineId}': {ex.Message}");
            }
        }

        // Strip characters that are invalid in file names (keep alphanumeric, dash, underscore, dot).
        private static string SanitizeId(string id)
        {
            var sb = new System.Text.StringBuilder(id.Length);
            foreach (char c in id)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_');
            return sb.ToString();
        }
    }
}
