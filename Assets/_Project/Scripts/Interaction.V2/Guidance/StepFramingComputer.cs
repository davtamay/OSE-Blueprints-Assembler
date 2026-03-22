using OSE.Content;
using UnityEngine;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Computes camera framing parameters (pivot + bounds) from a step's
    /// <see cref="ViewMode"/> and spatial data extracted from the machine package.
    /// </summary>
    public static class StepFramingComputer
    {
        public struct FramingResult
        {
            public Vector3 Pivot;
            public float Distance;
            public bool UseBounds;
            public Bounds Bounds;
        }

        /// <summary>
        /// Compute framing for the given view mode and step spatial data.
        /// Spatial positions are in previewRoot local space — caller must
        /// transform to world space before applying to the camera rig.
        /// </summary>
        public static FramingResult Compute(
            ViewMode mode,
            StepDefinition step,
            MachinePackageDefinition package,
            System.Func<string, TargetPreviewPlacement> findTarget,
            Bounds assemblyBounds)
        {
            switch (mode)
            {
                case ViewMode.SourceAndTarget:
                    return ComputeSourceAndTarget(step, findTarget);

                case ViewMode.PairEndpoints:
                    return ComputePairEndpoints(step, findTarget);

                case ViewMode.WorkZone:
                    return ComputeWorkZone(step, findTarget);

                case ViewMode.PathView:
                    return ComputeWorkZone(step, findTarget); // same spatial logic, slightly more pull-back

                case ViewMode.Overview:
                    return new FramingResult
                    {
                        Pivot = assemblyBounds.center,
                        Distance = assemblyBounds.extents.magnitude * 2.5f,
                        UseBounds = true,
                        Bounds = assemblyBounds
                    };

                case ViewMode.Inspect:
                    return ComputeWorkZone(step, findTarget, closeUp: true);

                default:
                    return new FramingResult
                    {
                        Pivot = assemblyBounds.center,
                        Distance = assemblyBounds.extents.magnitude * 2.5f,
                        UseBounds = true,
                        Bounds = assemblyBounds
                    };
            }
        }

        private static FramingResult ComputeSourceAndTarget(
            StepDefinition step,
            System.Func<string, TargetPreviewPlacement> findTarget)
        {
            // Collect target positions from the step's targetIds
            Bounds bounds = new Bounds();
            bool hasPoint = false;

            if (step.targetIds != null)
            {
                foreach (string tid in step.targetIds)
                {
                    TargetPreviewPlacement tp = findTarget?.Invoke(tid);
                    if (tp == null) continue;

                    Vector3 pos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                    if (!hasPoint) { bounds = new Bounds(pos, Vector3.zero); hasPoint = true; }
                    else bounds.Encapsulate(pos);
                }
            }

            if (!hasPoint)
                return FallbackResult();

            // Pad the bounds to ensure comfortable framing
            bounds.Expand(0.15f);

            return new FramingResult
            {
                Pivot = bounds.center,
                Distance = Mathf.Max(bounds.extents.magnitude * 2.5f, 0.5f),
                UseBounds = true,
                Bounds = bounds
            };
        }

        private static FramingResult ComputePairEndpoints(
            StepDefinition step,
            System.Func<string, TargetPreviewPlacement> findTarget)
        {
            // For Connect steps, use portA/portB positions; for Measure, use target positions
            Bounds bounds = new Bounds();
            bool hasPoint = false;

            if (step.targetIds != null)
            {
                foreach (string tid in step.targetIds)
                {
                    TargetPreviewPlacement tp = findTarget?.Invoke(tid);
                    if (tp == null) continue;

                    // Try port positions first (Connect steps)
                    Vector3 portA = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                    Vector3 portB = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);

                    if (portA != Vector3.zero || portB != Vector3.zero)
                    {
                        if (portA != Vector3.zero)
                        {
                            if (!hasPoint) { bounds = new Bounds(portA, Vector3.zero); hasPoint = true; }
                            else bounds.Encapsulate(portA);
                        }
                        if (portB != Vector3.zero)
                        {
                            if (!hasPoint) { bounds = new Bounds(portB, Vector3.zero); hasPoint = true; }
                            else bounds.Encapsulate(portB);
                        }
                    }
                    else
                    {
                        // Fall back to target position
                        Vector3 pos = new Vector3(tp.position.x, tp.position.y, tp.position.z);
                        if (!hasPoint) { bounds = new Bounds(pos, Vector3.zero); hasPoint = true; }
                        else bounds.Encapsulate(pos);
                    }
                }
            }

            if (!hasPoint)
                return FallbackResult();

            bounds.Expand(0.1f);

            return new FramingResult
            {
                Pivot = bounds.center,
                Distance = Mathf.Max(bounds.extents.magnitude * 2.8f, 0.5f),
                UseBounds = true,
                Bounds = bounds
            };
        }

        private static FramingResult ComputeWorkZone(
            StepDefinition step,
            System.Func<string, TargetPreviewPlacement> findTarget,
            bool closeUp = false)
        {
            // Centroid of all target positions
            Vector3 sum = Vector3.zero;
            int count = 0;

            if (step.targetIds != null)
            {
                foreach (string tid in step.targetIds)
                {
                    TargetPreviewPlacement tp = findTarget?.Invoke(tid);
                    if (tp == null) continue;

                    sum += new Vector3(tp.position.x, tp.position.y, tp.position.z);
                    count++;
                }
            }

            // Also check requiredToolActions for target positions
            if (step.requiredToolActions != null)
            {
                foreach (var action in step.requiredToolActions)
                {
                    if (action == null || string.IsNullOrEmpty(action.targetId)) continue;
                    TargetPreviewPlacement tp = findTarget?.Invoke(action.targetId);
                    if (tp == null) continue;

                    sum += new Vector3(tp.position.x, tp.position.y, tp.position.z);
                    count++;
                }
            }

            if (count == 0)
                return FallbackResult();

            Vector3 centroid = sum / count;
            float distance = closeUp ? 0.6f : 1.0f;

            return new FramingResult
            {
                Pivot = centroid,
                Distance = distance,
                UseBounds = false,
                Bounds = default
            };
        }

        private static FramingResult FallbackResult()
        {
            return new FramingResult
            {
                Pivot = Vector3.zero,
                Distance = 1.5f,
                UseBounds = false,
                Bounds = default
            };
        }
    }
}
