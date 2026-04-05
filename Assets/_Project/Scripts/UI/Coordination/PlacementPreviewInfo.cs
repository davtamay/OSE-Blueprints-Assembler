using System;
using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Component attached to spawned placement preview GameObjects.
    /// Stores which target/part/subassembly the preview represents so that
    /// placement handlers can match incoming selections to the correct preview.
    /// </summary>
    internal sealed class PlacementPreviewInfo : MonoBehaviour, IPlacementPreviewMarker
    {
        public string TargetId;
        public string PartId;
        public string SubassemblyId;

        public bool MatchesPart(string partId)
        {
            return !string.IsNullOrEmpty(partId) &&
                string.Equals(PartId, partId, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesSubassembly(string subassemblyId)
        {
            return !string.IsNullOrEmpty(subassemblyId) &&
                string.Equals(SubassemblyId, subassemblyId, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesSelectionId(string selectionId)
        {
            return MatchesPart(selectionId) || MatchesSubassembly(selectionId);
        }
    }
}
