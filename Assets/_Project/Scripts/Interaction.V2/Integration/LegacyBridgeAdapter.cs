using OSE.Core;
using System.Reflection;
using UnityEngine;

namespace OSE.Interaction.V2.Integration
{
    /// <summary>
    /// Wraps the existing PartInteractionBridge to allow the V2 orchestrator
    /// to delegate part interaction operations (ghost spawning, snap animation,
    /// visual feedback) to the legacy system during the migration period.
    ///
    /// The legacy bridge calls public methods on PartInteractionBridge without
    /// touching its internal Update() input polling. To disable the legacy
    /// input polling, set PartInteractionBridge.ExternalControlEnabled = true.
    ///
    /// This adapter references PartInteractionBridge by type via the scene
    /// rather than a hard compile-time dependency, since PartInteractionBridge
    /// lives in OSE.UI which may have circular dependency issues.
    /// Instead, it works through the canonical action bridge and service registry.
    /// </summary>
    public sealed class LegacyBridgeAdapter
    {
        private readonly CanonicalActionBridge _actionBridge;
        private MonoBehaviour _legacyBridge; // PartInteractionBridge, loosely typed
        private FieldInfo _externalControlField;
        private MethodInfo _setExternalHoveredPartMethod;
        private MethodInfo _tryExternalClickToPlaceMethod;
        private MethodInfo _tryExternalToolActionMethod;
        private MethodInfo _getNearestToolTargetWorldPosMethod;
        private MethodInfo _tryGetGhostWorldPosForPartMethod;
        private PropertyInfo _lastToolActionWorldPosProperty;
        private GameObject _lastHoveredPart;

        public bool IsConnected => _legacyBridge != null;

        public LegacyBridgeAdapter(CanonicalActionBridge actionBridge)
        {
            _actionBridge = actionBridge;
        }

        /// <summary>
        /// Connect to the existing PartInteractionBridge in the scene.
        /// Call this after scene setup is complete.
        /// </summary>
        public void Connect(MonoBehaviour partInteractionBridge)
        {
            _legacyBridge = partInteractionBridge;
            _externalControlField = _legacyBridge.GetType().GetField("ExternalControlEnabled");
            _setExternalHoveredPartMethod = _legacyBridge.GetType().GetMethod("SetExternalHoveredPart",
                BindingFlags.Public | BindingFlags.Instance);
            _tryExternalClickToPlaceMethod = _legacyBridge.GetType().GetMethod("TryExternalClickToPlace",
                BindingFlags.Public | BindingFlags.Instance);
            _tryExternalToolActionMethod = _legacyBridge.GetType().GetMethod("TryExternalToolAction",
                BindingFlags.Public | BindingFlags.Instance);
            _getNearestToolTargetWorldPosMethod = _legacyBridge.GetType().GetMethod("TryGetNearestToolTargetWorldPos",
                BindingFlags.Public | BindingFlags.Instance);
            _tryGetGhostWorldPosForPartMethod = _legacyBridge.GetType().GetMethod("TryGetGhostWorldPosForPart",
                BindingFlags.Public | BindingFlags.Instance);
            _lastToolActionWorldPosProperty = _legacyBridge.GetType().GetProperty("LastToolActionWorldPos",
                BindingFlags.Public | BindingFlags.Instance);
            _lastHoveredPart = null;

            // Enable external control mode on the legacy bridge
            if (_externalControlField != null)
            {
                _externalControlField.SetValue(_legacyBridge, true);
                OseLog.VerboseInfo("[LegacyBridge] Connected to PartInteractionBridge, ExternalControlEnabled = true");
            }
            else
            {
                OseLog.Warn("[LegacyBridge] PartInteractionBridge missing ExternalControlEnabled field. " +
                           "Add 'public bool ExternalControlEnabled;' to PartInteractionBridge.cs");
            }
        }

        /// <summary>
        /// Disconnect and restore legacy input polling.
        /// </summary>
        public void Disconnect()
        {
            if (_legacyBridge != null)
            {
                _externalControlField?.SetValue(_legacyBridge, false);
            }
            _legacyBridge = null;
            _externalControlField = null;
            _setExternalHoveredPartMethod = null;
            _tryExternalClickToPlaceMethod = null;
            _tryExternalToolActionMethod = null;
            _getNearestToolTargetWorldPosMethod = null;
            _tryGetGhostWorldPosForPartMethod = null;
            _lastToolActionWorldPosProperty = null;
            _lastHoveredPart = null;
        }

        // ── Delegation Methods ──

        public void SelectPart(GameObject part) => _actionBridge.OnPartSelected(part);
        public void InspectPart(GameObject part) => _actionBridge.OnPartInspected(part);
        public void GrabPart(GameObject part) => _actionBridge.OnPartGrabbed(part);
        public void ReleasePart() => _actionBridge.OnPartReleased();
        public void DeselectAll() => _actionBridge.OnDeselected();

        /// <summary>
        /// Attempts click-to-place via the legacy bridge. Returns true if a selected
        /// part was snapped to a matching ghost target at the given screen position.
        /// </summary>
        public bool TryClickToPlace(Vector2 screenPos)
        {
            if (_legacyBridge == null || _tryExternalClickToPlaceMethod == null)
                return false;

            object result = _tryExternalClickToPlaceMethod.Invoke(_legacyBridge, new object[] { screenPos });
            return result is true;
        }

        /// <summary>
        /// Attempts to execute the tool primary action via the legacy bridge.
        /// Uses screen position for target resolution + single-target auto-resolve.
        /// Returns true if the action was handled.
        /// </summary>
        public bool TryToolAction(Vector2 screenPos)
        {
            if (_legacyBridge == null || _tryExternalToolActionMethod == null)
            {
                OseLog.Warn($"[LegacyBridge] TryToolAction: bridge={_legacyBridge != null}, methodFound={_tryExternalToolActionMethod != null}");
                return false;
            }

            object result = _tryExternalToolActionMethod.Invoke(_legacyBridge, new object[] { screenPos });
            return result is true;
        }

        /// <summary>
        /// Gets the world position of the last successfully executed tool action target.
        /// Returns false if unavailable (bridge not connected or no action executed yet).
        /// </summary>
        public bool TryGetLastToolActionWorldPos(out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (_legacyBridge == null || _lastToolActionWorldPosProperty == null)
                return false;

            object value = _lastToolActionWorldPosProperty.GetValue(_legacyBridge);
            if (value is Vector3 pos)
            {
                worldPos = pos;
                return worldPos != Vector3.zero;
            }
            return false;
        }

        /// <summary>
        /// Finds the world position of the nearest tool target at the given screen position.
        /// Used to focus the camera on a pulsating sphere even without a tool equipped.
        /// Returns false if no target is nearby.
        /// </summary>
        public bool TryGetNearestToolTargetWorldPos(Vector2 screenPos, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (_legacyBridge == null || _getNearestToolTargetWorldPosMethod == null)
                return false;

            object[] args = { screenPos, Vector3.zero };
            object result = _getNearestToolTargetWorldPosMethod.Invoke(_legacyBridge, args);
            if (result is true)
            {
                worldPos = (Vector3)args[1];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Returns the world position of the ghost target for the given part ID.
        /// Used to pivot the camera toward the placement destination on selection.
        /// </summary>
        public bool TryGetGhostWorldPosForPart(string partId, out Vector3 worldPos)
        {
            worldPos = Vector3.zero;
            if (_legacyBridge == null || _tryGetGhostWorldPosForPartMethod == null)
                return false;

            object[] args = { partId, Vector3.zero };
            object result = _tryGetGhostWorldPosForPartMethod.Invoke(_legacyBridge, args);
            if (result is true)
            {
                worldPos = (Vector3)args[1];
                return true;
            }
            return false;
        }

        public void SetHoveredPart(GameObject part)
        {
            if (_legacyBridge == null)
                return;

            // While hovering a part, forward every frame so hover-driven UI
            // can stay authoritative even if other systems push selected info.
            if (part == null && ReferenceEquals(_lastHoveredPart, null))
                return;

            _lastHoveredPart = part;
            _setExternalHoveredPartMethod?.Invoke(_legacyBridge, new object[] { part });
        }
    }
}
