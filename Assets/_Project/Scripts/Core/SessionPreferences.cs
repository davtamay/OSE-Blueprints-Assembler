using System;
using UnityEngine;

namespace OSE.Core
{
    /// <summary>
    /// Mode-agnostic, user-flippable session display preferences. PlayerPrefs-backed
    /// so the choice survives editor reloads and standalone restarts. Subscribers
    /// (e.g. <c>PartVisualFeedbackManager</c> via <c>UIRootCoordinator</c>) listen to
    /// <see cref="Changed"/> and re-apply visibility when the user toggles a flag.
    /// </summary>
    public static class SessionPreferences
    {
        private const string KeyShowAllPartsAtStaging = "ose.showAllPartsAtStaging";

        public static event Action Changed;

        public static bool ShowAllPartsAtStaging
        {
            get => PlayerPrefs.GetInt(KeyShowAllPartsAtStaging, 0) != 0;
        }

        public static void SetShowAllPartsAtStaging(bool value)
        {
            if (ShowAllPartsAtStaging == value) return;
            PlayerPrefs.SetInt(KeyShowAllPartsAtStaging, value ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }
    }
}
