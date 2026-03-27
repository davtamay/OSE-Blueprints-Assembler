using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Attached to <see cref="DockArcVisual"/> sphere markers so that a raycast
    /// hit on the visual dot is redirected to the part it represents.
    /// <see cref="PartLookupService.RaycastSelectableObject"/> checks for this
    /// component and returns <see cref="LinkedPart"/> instead of the marker itself.
    /// </summary>
    internal sealed class DockArcMarkerProxy : MonoBehaviour
    {
        /// <summary>The selectable part this marker visually represents.</summary>
        public GameObject LinkedPart;
    }
}
