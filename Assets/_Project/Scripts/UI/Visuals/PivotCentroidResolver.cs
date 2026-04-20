using OSE.Content;
using OSE.Content.Loading;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Single source of truth for the "body centroid" of a subassembly group
    /// root used as a rotation pivot by cue players and authoring gizmos.
    ///
    /// <para>A group's rotation should pivot around its established body
    /// (halves, bearings, etc.) — members introduced in prior steps — not
    /// the freshly-revealed staging parts of the current step which sit
    /// at their startPosition and would skew the centroid away from the
    /// physical body center. The filter excludes:</para>
    ///
    /// <list type="bullet">
    ///   <item>Inactive children (hidden or not yet revealed).</item>
    ///   <item>Children at the group root's origin (not yet positioned).</item>
    ///   <item>Children whose <see cref="PoseTable.FirstVisibleSeq"/> is
    ///     greater than or equal to the current step's sequenceIndex
    ///     (being introduced THIS step → at startPosition, not body).</item>
    /// </list>
    ///
    /// <para>Runtime (<see cref="AnimationCueCoordinator"/>), the player
    /// fallback (<see cref="PoseTransitionPlayer"/>), and the editor pivot
    /// gizmo all route through this method — so what the author sees in
    /// the scene view is bit-for-bit the pivot the runtime will use.</para>
    /// </summary>
    public static class PivotCentroidResolver
    {
        /// <summary>
        /// Returns the group-local body centroid, or null when no body
        /// members are present (e.g. the first step introducing the
        /// subassembly — callers should display "no body yet" rather
        /// than fabricate a fake centroid).
        /// </summary>
        public static Vector3? ComputeBodyCentroidLocal(
            Transform groupRoot,
            MachinePackageDefinition pkg,
            StepDefinition step)
        {
            if (groupRoot == null || pkg?.poseTable == null || step == null)
                return null;

            int currentSeq = step.sequenceIndex;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < groupRoot.childCount; i++)
            {
                var c = groupRoot.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.localPosition.sqrMagnitude < 0.0001f) continue;
                int firstSeq = pkg.poseTable.FirstVisibleSeq(c.name);
                if (firstSeq >= currentSeq) continue;
                sum += c.localPosition;
                n++;
            }
            return n > 0 ? (Vector3?)(sum / n) : null;
        }
    }
}
