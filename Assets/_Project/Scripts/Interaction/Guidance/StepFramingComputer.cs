using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
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
            /// <summary>True when no spatial data was found and this is a generic fallback.</summary>
            public bool IsFallback;
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
                    return ComputeSourceAndTarget(step, package, findTarget);

                case ViewMode.PairEndpoints:
                    return ComputePairEndpoints(step, package, findTarget);

                case ViewMode.WorkZone:
                    return ComputeWorkZone(step, package, findTarget);

                case ViewMode.PathView:
                    return ComputeWorkZone(step, package, findTarget, pathView: true);

                case ViewMode.Overview:
                    return new FramingResult
                    {
                        Pivot = assemblyBounds.center,
                        Distance = assemblyBounds.extents.magnitude * 2.5f,
                        UseBounds = true,
                        Bounds = assemblyBounds
                    };

                case ViewMode.Inspect:
                    return ComputeWorkZone(step, package, findTarget, closeUp: true);

                case ViewMode.ToolFocus:
                    return ComputeWorkZone(step, package, findTarget, closeUp: true);

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
            MachinePackageDefinition package,
            System.Func<string, TargetPreviewPlacement> findTarget)
        {
            if (!TryBuildRelevantBounds(step, package, findTarget, includePorts: false, includeSourceStarts: true, out Bounds bounds))
                return FallbackResult();

            bounds.Expand(0.25f);

            return new FramingResult
            {
                Pivot = bounds.center,
                Distance = Mathf.Max(bounds.extents.magnitude * 3.5f, 1.0f),
                UseBounds = true,
                Bounds = bounds
            };
        }

        private static FramingResult ComputePairEndpoints(
            StepDefinition step,
            MachinePackageDefinition package,
            System.Func<string, TargetPreviewPlacement> findTarget)
        {
            if (!TryBuildRelevantBounds(step, package, findTarget, includePorts: true, includeSourceStarts: false, out Bounds bounds))
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
            MachinePackageDefinition package,
            System.Func<string, TargetPreviewPlacement> findTarget,
            bool closeUp = false,
            bool pathView = false)
        {
            if (!TryBuildRelevantBounds(step, package, findTarget, includePorts: false, includeSourceStarts: false, out Bounds bounds))
                return FallbackResult();

            bounds.Expand(closeUp ? 0.08f : 0.12f);
            float distanceMultiplier = closeUp ? 2.0f : (pathView ? 3.0f : 2.6f);
            float distance = Mathf.Max(bounds.extents.magnitude * distanceMultiplier, closeUp ? 0.6f : 0.9f);

            return new FramingResult
            {
                Pivot = bounds.center,
                Distance = distance,
                UseBounds = true,
                Bounds = bounds
            };
        }

        private static bool TryBuildRelevantBounds(
            StepDefinition step,
            MachinePackageDefinition package,
            System.Func<string, TargetPreviewPlacement> findTarget,
            bool includePorts,
            bool includeSourceStarts,
            out Bounds bounds)
        {
            bounds = default;
            Bounds accumulatedBounds = default;
            bool hasPoint = false;

            void AddPoint(Vector3 point)
            {
                if (!hasPoint)
                {
                    accumulatedBounds = new Bounds(point, Vector3.zero);
                    hasPoint = true;
                }
                else
                {
                    accumulatedBounds.Encapsulate(point);
                }
            }

            if (step.targetIds != null)
            {
                WireConnectEntry[] wireEntries = step.wireConnect?.wires;

                for (int tIdx = 0; tIdx < step.targetIds.Length; tIdx++)
                {
                    string tid = step.targetIds[tIdx];

                    // Wire entries carry portA/portB directly — prefer them over target placement.
                    WireConnectEntry wireEntry = null;
                    if (wireEntries != null)
                    {
                        foreach (var w in wireEntries)
                            if (w?.targetId == tid) { wireEntry = w; break; }
                        if (wireEntry == null && tIdx < wireEntries.Length)
                            wireEntry = wireEntries[tIdx];
                    }

                    Vector3 portA = Vector3.zero;
                    Vector3 portB = Vector3.zero;

                    if (wireEntry != null &&
                        (wireEntry.portA.x != 0f || wireEntry.portA.y != 0f || wireEntry.portA.z != 0f ||
                         wireEntry.portB.x != 0f || wireEntry.portB.y != 0f || wireEntry.portB.z != 0f))
                    {
                        portA = new Vector3(wireEntry.portA.x, wireEntry.portA.y, wireEntry.portA.z);
                        portB = new Vector3(wireEntry.portB.x, wireEntry.portB.y, wireEntry.portB.z);
                    }
                    else
                    {
                        TargetPreviewPlacement tp = findTarget?.Invoke(tid);
                        if (tp == null) continue;
                        portA = new Vector3(tp.portA.x, tp.portA.y, tp.portA.z);
                        portB = new Vector3(tp.portB.x, tp.portB.y, tp.portB.z);

                        if (portA == Vector3.zero && portB == Vector3.zero)
                        {
                            AddPoint(new Vector3(tp.position.x, tp.position.y, tp.position.z));
                            continue;
                        }
                    }

                    bool usedPorts = false;
                    if (includePorts && (portA != Vector3.zero || portB != Vector3.zero))
                    {
                        if (portA != Vector3.zero) AddPoint(portA);
                        if (portB != Vector3.zero) AddPoint(portB);
                        usedPorts = true;
                    }

                    if (!usedPorts)
                    {
                        if (portA != Vector3.zero) AddPoint(portA);
                        else if (portB != Vector3.zero) AddPoint(portB);
                    }
                }
            }

            if (step.requiredToolActions != null)
            {
                foreach (ToolActionDefinition action in step.requiredToolActions)
                {
                    if (action == null || string.IsNullOrWhiteSpace(action.targetId))
                        continue;

                    TargetPreviewPlacement tp = findTarget?.Invoke(action.targetId);
                    if (tp == null)
                        continue;

                    AddPoint(new Vector3(tp.position.x, tp.position.y, tp.position.z));
                }
            }

            if (package?.previewConfig?.partPlacements != null)
            {
                if (!string.IsNullOrWhiteSpace(step.subassemblyId) &&
                    package.TryGetSubassembly(step.subassemblyId, out SubassemblyDefinition subassembly) &&
                    subassembly?.partIds != null)
                {
                    foreach (string partId in subassembly.partIds)
                    {
                        if (string.IsNullOrWhiteSpace(partId))
                            continue;

                        if (TryGetPartPlacement(package, partId, out PartPreviewPlacement placement))
                            AddPoint(new Vector3(placement.assembledPosition.x, placement.assembledPosition.y, placement.assembledPosition.z));
                    }
                }
                else
                {
                    string[] effectiveParts = step.GetEffectiveRequiredPartIds();
                    foreach (string partId in effectiveParts)
                    {
                        if (string.IsNullOrWhiteSpace(partId))
                            continue;

                        if (TryGetPartPlacement(package, partId, out PartPreviewPlacement placement))
                            AddPoint(new Vector3(placement.assembledPosition.x, placement.assembledPosition.y, placement.assembledPosition.z));
                    }
                }

                if (includeSourceStarts)
                {
                    string[] effectiveParts = step.GetEffectiveRequiredPartIds();
                    foreach (string partId in effectiveParts)
                    {
                        if (string.IsNullOrWhiteSpace(partId))
                            continue;

                        if (TryGetPartPlacement(package, partId, out PartPreviewPlacement placement))
                            AddPoint(new Vector3(placement.startPosition.x, placement.startPosition.y, placement.startPosition.z));
                    }
                }
            }

            if (!hasPoint &&
                !string.IsNullOrWhiteSpace(step.requiredSubassemblyId) &&
                package != null &&
                package.TryGetSubassemblyPreviewPlacement(step.requiredSubassemblyId, out SubassemblyPreviewPlacement subassemblyPlacement))
            {
                AddPoint(new Vector3(subassemblyPlacement.position.x, subassemblyPlacement.position.y, subassemblyPlacement.position.z));
            }

            if (hasPoint)
                bounds = accumulatedBounds;

            return hasPoint;
        }

        private static bool TryGetPartPlacement(
            MachinePackageDefinition package,
            string partId,
            out PartPreviewPlacement placement)
        {
            PartPreviewPlacement[] partPlacements = package?.previewConfig?.partPlacements;
            if (partPlacements != null)
            {
                for (int i = 0; i < partPlacements.Length; i++)
                {
                    PartPreviewPlacement candidate = partPlacements[i];
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.partId))
                        continue;

                    if (string.Equals(candidate.partId, partId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        placement = candidate;
                        return true;
                    }
                }
            }

            placement = null;
            return false;
        }

        private static FramingResult FallbackResult()
        {
            return new FramingResult
            {
                Pivot = Vector3.zero,
                Distance = 1.5f,
                UseBounds = false,
                Bounds = default,
                IsFallback = true
            };
        }
    }
}
