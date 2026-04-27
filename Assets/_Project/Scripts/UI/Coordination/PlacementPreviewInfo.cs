using System;
using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Component attached to spawned placement preview GameObjects.
    /// Stores which target/part/partGroup the preview represents so that
    /// placement handlers can match incoming selections to the correct preview.
    /// </summary>
    internal sealed class PlacementPreviewInfo : MonoBehaviour, IPlacementPreviewMarker
    {
        public string TargetId;
        public string PartId;
        public string PartGroupId;

        public bool MatchesPart(string partId)
        {
            return !string.IsNullOrEmpty(partId) &&
                string.Equals(PartId, partId, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesPartGroup(string partGroupId)
        {
            return !string.IsNullOrEmpty(partGroupId) &&
                string.Equals(PartGroupId, partGroupId, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesSelectionId(string selectionId)
        {
            return MatchesPart(selectionId) || MatchesPartGroup(selectionId);
        }
    }
}
