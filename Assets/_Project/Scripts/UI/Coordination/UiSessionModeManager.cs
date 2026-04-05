using System;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Owns session-mode profile resolution for <see cref="UIRootCoordinator"/>.
    /// A session mode controls which panels are visible and whether hints are allowed.
    ///
    /// Extracted from <see cref="UIRootCoordinator"/> for single-responsibility.
    /// UIRootCoordinator holds one instance and reads <see cref="IsHintDisplayAllowed"/>,
    /// <see cref="ShowStepPanel"/>, <see cref="ShowPartInfoPanel"/>, and
    /// <see cref="ShowSessionHud"/> to decide whether to show or hide each panel.
    /// </summary>
    internal sealed class UiSessionModeManager
    {
        [Serializable]
        internal struct SessionUiModeProfile
        {
            public SessionMode Mode;
            public bool ShowStepPanel;
            public bool ShowPartInfoPanel;
            public bool ShowSessionHud;
            public bool AllowHints;
        }

        private SessionUiModeProfile[] _profiles;
        private SessionMode _activeMode = SessionMode.Guided;
        private SessionUiModeProfile _activeProfile;
        private bool _hasProfile;

        /// <summary>Callback invoked when the session mode changes. Allows the
        /// coordinator to refresh all panels without coupling to this class.</summary>
        public Action OnModeChanged;

        /// <summary>Callback invoked when hint state should be cleared (mode no longer allows hints).</summary>
        public Action OnHintsDisabled;

        public SessionMode ActiveMode => _activeMode;
        public bool IsHintDisplayAllowed => _hasProfile ? _activeProfile.AllowHints : true;
        public bool ShowStepPanel     => !_hasProfile || _activeProfile.ShowStepPanel;
        public bool ShowPartInfoPanel => !_hasProfile || _activeProfile.ShowPartInfoPanel;
        public bool ShowSessionHud    => !_hasProfile || _activeProfile.ShowSessionHud;

        public UiSessionModeManager(SessionUiModeProfile[] profiles)
        {
            _profiles = profiles ?? CreateDefaultProfiles();
            EnsureProfiles();
        }

        public void SetMode(SessionMode mode)
        {
            _activeMode = mode;
            _activeProfile = ResolveProfile(mode);
            _hasProfile = true;

            if (!_activeProfile.AllowHints)
                OnHintsDisabled?.Invoke();

            OnModeChanged?.Invoke();
        }

        public void EnsureProfiles()
        {
            if (_profiles == null || _profiles.Length == 0)
                _profiles = CreateDefaultProfiles();
        }

        public SessionUiModeProfile[] GetProfiles() => _profiles;

        public void SetProfiles(SessionUiModeProfile[] profiles)
        {
            _profiles = profiles;
            EnsureProfiles();
        }

        private SessionUiModeProfile ResolveProfile(SessionMode mode)
        {
            if (_profiles != null)
            {
                for (int i = 0; i < _profiles.Length; i++)
                {
                    if (_profiles[i].Mode == mode)
                        return _profiles[i];
                }
            }

            return new SessionUiModeProfile
            {
                Mode = mode,
                ShowStepPanel = true,
                ShowPartInfoPanel = true,
                ShowSessionHud = true,
                AllowHints = true
            };
        }

        internal static SessionUiModeProfile[] CreateDefaultProfiles()
        {
            return new[]
            {
                new SessionUiModeProfile { Mode = SessionMode.Tutorial, ShowStepPanel = true, ShowPartInfoPanel = true, ShowSessionHud = true,  AllowHints = true  },
                new SessionUiModeProfile { Mode = SessionMode.Guided,   ShowStepPanel = true, ShowPartInfoPanel = true, ShowSessionHud = true,  AllowHints = true  },
                new SessionUiModeProfile { Mode = SessionMode.Standard, ShowStepPanel = true, ShowPartInfoPanel = true, ShowSessionHud = false, AllowHints = false },
                new SessionUiModeProfile { Mode = SessionMode.Challenge, ShowStepPanel = true, ShowPartInfoPanel = true, ShowSessionHud = true, AllowHints = true  },
                new SessionUiModeProfile { Mode = SessionMode.Review,   ShowStepPanel = true, ShowPartInfoPanel = true, ShowSessionHud = false, AllowHints = false },
            };
        }
    }
}
