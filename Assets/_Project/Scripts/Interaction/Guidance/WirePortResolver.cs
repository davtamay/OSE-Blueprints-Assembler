using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Resolves the previewRoot-local port positions (portA, portB) for a
    /// wire entry on a Connect-family step. Single source of truth shared by
    /// the framing systems so wire-step camera focus stays consistent.
    ///
    /// Resolution order:
    ///   1. <c>step.wireConnect.wires[]</c> entry matching <paramref name="targetId"/>
    ///      with non-zero portA/portB — authored geometry wins.
    ///   2. Index-aligned <c>step.wireConnect.wires[]</c> entry when no
    ///      targetId match exists (legacy parallel-array authoring).
    ///   3. <see cref="TargetPreviewPlacement"/>'s portA/portB (when
    ///      authored on the target itself rather than the wire entry).
    /// Returns false when no non-zero port pair could be resolved.
    /// </summary>
    public static class WirePortResolver
    {
        public static bool TryGetPortLocalPositions(
            StepDefinition step,
            string targetId,
            System.Func<string, TargetPreviewPlacement> findTarget,
            out Vector3 portA,
            out Vector3 portB)
        {
            portA = Vector3.zero;
            portB = Vector3.zero;
            if (step == null || string.IsNullOrEmpty(targetId))
                return false;

            WireConnectEntry[] wireEntries = step.wireConnect?.wires;
            WireConnectEntry wireEntry = null;
            int matchIdx = -1;

            if (wireEntries != null)
            {
                for (int i = 0; i < wireEntries.Length; i++)
                {
                    var w = wireEntries[i];
                    if (w == null) continue;
                    if (string.Equals(w.targetId, targetId, System.StringComparison.Ordinal))
                    {
                        wireEntry = w;
                        matchIdx = i;
                        break;
                    }
                }

                // Index-aligned fallback: wire entries authored as parallel array
                // to step.targetIds without per-entry targetId set.
                if (wireEntry == null && step.targetIds != null)
                {
                    for (int i = 0; i < step.targetIds.Length; i++)
                    {
                        if (string.Equals(step.targetIds[i], targetId, System.StringComparison.Ordinal))
                        {
                            if (i < wireEntries.Length && wireEntries[i] != null)
                            {
                                wireEntry = wireEntries[i];
                                matchIdx = i;
                            }
                            break;
                        }
                    }
                }
            }

            if (wireEntry != null && (IsNonZero(wireEntry.portA) || IsNonZero(wireEntry.portB)))
            {
                portA = new Vector3(wireEntry.portA.x, wireEntry.portA.y, wireEntry.portA.z);
                portB = new Vector3(wireEntry.portB.x, wireEntry.portB.y, wireEntry.portB.z);
                return true;
            }

            TargetPreviewPlacement tp = findTarget?.Invoke(targetId);
            if (tp == null) return false;

            Vector3 tA = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
            Vector3 tB = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);
            if (tA == Vector3.zero && tB == Vector3.zero)
                return false;

            portA = tA;
            portB = tB;
            return true;
        }

        private static bool IsNonZero(SceneFloat3 v) => v.x != 0f || v.y != 0f || v.z != 0f;
    }
}
