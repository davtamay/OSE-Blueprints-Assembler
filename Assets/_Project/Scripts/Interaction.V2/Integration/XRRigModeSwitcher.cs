using System.Collections.Generic;
using OSE.App;
using OSE.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

#if XR_HANDS_1_1_OR_NEWER
using UnityEngine.XR.Hands;
#endif

namespace OSE.Interaction
{
    /// <summary>
    /// Enables either the controller rig or the hand-tracking rig based on
    /// runtime device availability (controllers vs hands).
    /// </summary>
    public class XRRigModeSwitcher : MonoBehaviour
    {
        [Header("Rig Roots")]
        [SerializeField] private GameObject _controllerRigRoot;
        [SerializeField] private GameObject _handRigRoot;

        [Header("Behavior")]
        [SerializeField] private bool _preferHandsWhenTracked = true;
        [SerializeField, Min(0f)] private float _switchCooldownSeconds = 0.5f;
        [SerializeField] private bool _logSwitches;
        [SerializeField] private bool _autoAddInteractionAdapters = true;

#if XR_HANDS_1_1_OR_NEWER
        private XRHandSubsystem _handSubsystem;
#endif

        private bool _usingHands;
        private float _lastSwitchTime;

        /// <summary>True when the hand-tracking rig is active; false when controllers are active.</summary>
        public bool UsingHands => _usingHands;

        private void Awake()
        {
            ResolveRigReferences();
            ServiceRegistry.Register<XRRigModeSwitcher>(this);
        }

        private void OnDestroy()
        {
            ServiceRegistry.Unregister<XRRigModeSwitcher>();
        }

        private void OnEnable()
        {
            RefreshHandSubsystem();
            ApplyMode(ChooseMode(initial: true));
        }

        private void Update()
        {
            bool desired = ChooseMode(initial: false);
            if (desired != _usingHands && Time.unscaledTime - _lastSwitchTime >= _switchCooldownSeconds)
                ApplyMode(desired);

            if (_autoAddInteractionAdapters)
            {
                var activeRig = _usingHands ? _handRigRoot : _controllerRigRoot;
                EnsureAdapters(activeRig);
            }
        }

        private void ResolveRigReferences()
        {
            if (_controllerRigRoot == null)
                _controllerRigRoot = FindRigRoot("XR Origin (Controllers)");

            if (_controllerRigRoot == null)
                _controllerRigRoot = FindRigRoot("XR Origin (XR Rig)");

            if (_handRigRoot == null)
                _handRigRoot = FindRigRoot("XR Origin Hands (XR Rig)");
        }

        private GameObject FindRigRoot(string name)
        {
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                if (root != null && root.name == name)
                    return root;
            }

            return GameObject.Find(name);
        }

        private bool ChooseMode(bool initial)
        {
            bool handsTracked = AreHandsTracked();
            bool controllersTracked = AreControllersTracked();

            bool useHands;
            if (_preferHandsWhenTracked)
            {
                if (handsTracked) useHands = true;
                else if (controllersTracked) useHands = false;
                else useHands = _usingHands;
            }
            else
            {
                if (controllersTracked) useHands = false;
                else if (handsTracked) useHands = true;
                else useHands = _usingHands;
            }

            if (initial && !_usingHands)
            {
                if (handsTracked) useHands = true;
                else if (controllersTracked) useHands = false;
            }

            if (useHands && _handRigRoot == null && _controllerRigRoot != null)
                useHands = false;
            else if (!useHands && _controllerRigRoot == null && _handRigRoot != null)
                useHands = true;

            return useHands;
        }

        private bool AreControllersTracked()
        {
            foreach (var device in InputSystem.devices)
            {
                if (device is XRController xrController)
                {
                    if (xrController.isTracked != null)
                    {
                        if (xrController.isTracked.isPressed)
                            return true;
                    }
                    else
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool AreHandsTracked()
        {
#if XR_HANDS_1_1_OR_NEWER
            if (_handSubsystem == null || !_handSubsystem.running)
                RefreshHandSubsystem();

            if (_handSubsystem != null && _handSubsystem.running)
                return _handSubsystem.leftHand.isTracked || _handSubsystem.rightHand.isTracked;
#endif

            return false;
        }

        private void RefreshHandSubsystem()
        {
#if XR_HANDS_1_1_OR_NEWER
            if (_handSubsystem != null && _handSubsystem.running)
                return;

            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetInstances(subsystems);
            for (int i = 0; i < subsystems.Count; i++)
            {
                if (subsystems[i] != null && subsystems[i].running)
                {
                    _handSubsystem = subsystems[i];
                    return;
                }
            }

            _handSubsystem = subsystems.Count > 0 ? subsystems[0] : null;
#endif
        }

        private void ApplyMode(bool useHands)
        {
            _usingHands = useHands;
            _lastSwitchTime = Time.unscaledTime;

            if (_handRigRoot != null)
                _handRigRoot.SetActive(useHands);

            if (_controllerRigRoot != null)
                _controllerRigRoot.SetActive(!useHands);

            if (_autoAddInteractionAdapters)
                EnsureAdapters(useHands ? _handRigRoot : _controllerRigRoot);

            if (_logSwitches)
                OseLog.Info($"[XRRigModeSwitcher] Using {(useHands ? "hands" : "controllers")}.");
        }

        private void EnsureAdapters(GameObject rigRoot)
        {
            if (rigRoot == null)
                return;

            var interactors = rigRoot.GetComponentsInChildren<XRBaseInteractor>(true);
            int interactorCount = 0;
            int addedCount = 0;

            foreach (var interactor in interactors)
            {
                if (interactor == null || interactor is not IXRSelectInteractor)
                    continue;

                interactorCount++;
                if (interactor.GetComponent<XRIInteractionAdapter>() == null)
                {
                    interactor.gameObject.AddComponent<XRIInteractionAdapter>();
                    addedCount++;
                }
            }

            if (_logSwitches && addedCount > 0)
                OseLog.Info($"[XRRigModeSwitcher] Adapters on active rig: interactors={interactorCount}, added={addedCount}.");
        }
    }
}
