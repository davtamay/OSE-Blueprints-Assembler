using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Manages the observe-phase of Confirm-family steps that have <c>targetIds</c>.
    ///
    /// Spawns floating inspection markers at each target location. Each frame,
    /// tests whether each unvisited target lies within a 22° look cone centered
    /// on the camera forward direction. When all targets have been looked at, publishes
    /// <see cref="ObserveTargetsCompleted"/> so the UI layer unlocks the Confirm button.
    ///
    /// Registered as <see cref="IConfirmInspectionService"/> in <see cref="ServiceRegistry"/>
    /// so <c>ConfirmStepHandler</c> in OSE.Runtime can call it without a direct assembly reference.
    /// </summary>
    internal sealed class ConfirmInspectionService : IConfirmInspectionService
    {
        private readonly List<ConfirmInspectionMarker> _markers = new();
        private readonly HashSet<string> _pending = new(System.StringComparer.Ordinal);

        // Half-angle of the look cone used to determine whether the camera is
        // "looking at" a target. 22° means the target must be within 22° of the
        // camera's forward direction — tighter than a full frustum check, which
        // fires even at the periphery of a wide-FOV XR headset.
        private const float LookConeHalfAngleDeg = 22f;
        private static readonly float LookConeThreshold = Mathf.Cos(LookConeHalfAngleDeg * Mathf.Deg2Rad);

        public void ShowMarkersForStep(StepDefinition step)
        {
            ClearMarkers();
            if (step?.targetIds == null || step.targetIds.Length == 0)
                return;

            if (!ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner) || spawner == null)
                return;

            Transform previewRoot = spawner.PreviewRoot;

            for (int i = 0; i < step.targetIds.Length; i++)
            {
                string tid = step.targetIds[i];
                if (string.IsNullOrEmpty(tid)) continue;

                var placement = spawner.FindTargetPlacement(tid);
                if (placement == null) continue;

                var local    = new Vector3(placement.position.x, placement.position.y, placement.position.z);
                var worldPos = previewRoot != null ? previewRoot.TransformPoint(local) : local;

                var marker = ConfirmInspectionMarker.Spawn(worldPos, tid);
                _markers.Add(marker);
                _pending.Add(tid);
            }
        }

        public void UpdateObservations()
        {
            if (_pending.Count == 0) return;

            var cam = Camera.main;
            if (cam == null) return;

            Vector3 camPos     = cam.transform.position;
            Vector3 camForward = cam.transform.forward;
            bool anyNewlyObserved = false;

            foreach (var marker in _markers)
            {
                if (!_pending.Contains(marker.TargetId)) continue;

                Vector3 toTarget = (marker.WorldPosition - camPos).normalized;
                if (Vector3.Dot(camForward, toTarget) < LookConeThreshold) continue;

                _pending.Remove(marker.TargetId);
                marker.SetObserved();
                anyNewlyObserved = true;
            }

            if (anyNewlyObserved && _pending.Count == 0)
                RuntimeEventBus.Publish(new ObserveTargetsCompleted());
        }

        public void ClearMarkers()
        {
            foreach (var m in _markers)
                if (m != null) Object.Destroy(m.gameObject);
            _markers.Clear();
            _pending.Clear();
        }
    }
}
