using OSE.Content;

namespace OSE.UI.Root
{
    /// <summary>
    /// Pure-lookup helper that resolves placement data from a
    /// <see cref="PackagePreviewConfig"/>. Extracted from
    /// <see cref="PackagePartSpawner"/> for single-responsibility.
    /// </summary>
    internal sealed class PreviewConfigLookup
    {
        private PackagePreviewConfig _config;

        internal void SetConfig(PackagePreviewConfig config)
        {
            _config = config;
        }

        internal PartPreviewPlacement FindPartPlacement(string partId)
        {
            if (_config?.partPlacements == null) return null;
            foreach (var p in _config.partPlacements)
                if (p.partId == partId) return p;
            return null;
        }

        internal TargetPreviewPlacement FindTargetPlacement(string targetId)
        {
            if (_config?.targetPlacements == null) return null;
            foreach (var t in _config.targetPlacements)
                if (t.targetId == targetId) return t;
            return null;
        }

        internal SubassemblyPreviewPlacement FindSubassemblyPlacement(string subassemblyId)
        {
            if (_config?.subassemblyPlacements == null) return null;
            foreach (var placement in _config.subassemblyPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }
            return null;
        }

        internal ConstrainedSubassemblyFitPreviewPlacement FindConstrainedSubassemblyFitPlacement(string subassemblyId, string targetId)
        {
            if (_config?.constrainedSubassemblyFitPlacements == null)
                return null;

            foreach (var placement in _config.constrainedSubassemblyFitPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(placement.targetId, targetId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }

            return null;
        }

        internal SubassemblyPreviewPlacement FindCompletedSubassemblyParkingPlacement(string subassemblyId)
        {
            if (_config?.completedSubassemblyParkingPlacements == null) return null;
            foreach (var placement in _config.completedSubassemblyParkingPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }
            return null;
        }

        internal IntegratedSubassemblyPreviewPlacement FindIntegratedSubassemblyPlacement(string subassemblyId, string targetId)
        {
            if (_config?.integratedSubassemblyPlacements == null)
                return null;

            foreach (var placement in _config.integratedSubassemblyPlacements)
            {
                if (placement != null &&
                    string.Equals(placement.subassemblyId, subassemblyId, System.StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(placement.targetId, targetId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return placement;
                }
            }

            return null;
        }

        internal IntegratedMemberPreviewPlacement FindIntegratedMemberPlacement(string partId)
        {
            if (string.IsNullOrEmpty(partId))
                return null;

            IntegratedSubassemblyPreviewPlacement[] intPlacements = _config?.integratedSubassemblyPlacements;
            if (intPlacements == null)
                return null;

            for (int ip = 0; ip < intPlacements.Length; ip++)
            {
                IntegratedSubassemblyPreviewPlacement intPlacement = intPlacements[ip];
                if (intPlacement?.memberPlacements == null) continue;

                for (int mp = 0; mp < intPlacement.memberPlacements.Length; mp++)
                {
                    IntegratedMemberPreviewPlacement member = intPlacement.memberPlacements[mp];
                    if (member != null && string.Equals(member.partId, partId, System.StringComparison.OrdinalIgnoreCase))
                        return member;
                }
            }

            return null;
        }
    }
}
