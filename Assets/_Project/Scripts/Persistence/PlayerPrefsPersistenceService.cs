using OSE.App;
using OSE.Core;
using UnityEngine;

namespace OSE.Persistence
{
    /// <summary>
    /// Persists MachineSessionState to PlayerPrefs as JSON.
    /// One key per machineId: "ose_session_{machineId}".
    /// </summary>
    public sealed class PlayerPrefsPersistenceService : IPersistenceService
    {
        private const string KeyPrefix = "ose_session_";

        public void SaveSession(MachineSessionState state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.MachineId))
                return;

            string json = JsonUtility.ToJson(state);
            PlayerPrefs.SetString(KeyPrefix + state.MachineId, json);
            PlayerPrefs.Save();

            OseLog.VerboseInfo($"[Persistence] Saved session for '{state.MachineId}' (step {state.CompletedStepCount}).");
        }

        public MachineSessionState LoadSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return null;

            string key = KeyPrefix + machineId;
            if (!PlayerPrefs.HasKey(key))
                return null;

            string json = PlayerPrefs.GetString(key);
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
                    $"[Persistence] Corrupt session data for '{machineId}' — clearing. ({ex.Message})");
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return null;
            }

            if (state == null || string.IsNullOrWhiteSpace(state.MachineId))
            {
                OseLog.Warn(OseErrorCode.SessionRestoreFailed,
                    $"[Persistence] Session data for '{machineId}' deserialized to null or missing ID — clearing.");
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                return null;
            }

            OseLog.VerboseInfo($"[Persistence] Loaded session for '{machineId}' (step {state.CompletedStepCount}).");
            return state;
        }

        public bool HasSavedSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return false;

            return PlayerPrefs.HasKey(KeyPrefix + machineId);
        }

        public void ClearSession(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId))
                return;

            PlayerPrefs.DeleteKey(KeyPrefix + machineId);
            PlayerPrefs.Save();

            OseLog.Info($"[Persistence] Cleared saved session for '{machineId}'.");
        }
    }
}
