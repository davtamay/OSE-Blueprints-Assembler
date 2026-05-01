using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Tag component placed on a spawned part GameObject when the package
    /// catalog declares the part but no GLB / FBX could be resolved (and
    /// the runtime fell back to <see cref="GameObject.CreatePrimitive"/>).
    ///
    /// <para>Without this marker the placement-application pass writes the
    /// part's authored <c>startScale</c> verbatim onto the primitive cube —
    /// for parts that haven't been authored with proper scales (typical
    /// for missing GLBs the catalog inherited <c>(1, 1, 1)</c>) the result
    /// is a 1-meter cube that obscures the entire scene. The placement
    /// pass checks for this component and clamps the scale to a small
    /// marker size (<see cref="MarkerScale"/>) so the missing-asset is
    /// visible as a tinted dot, not a blocker.</para>
    ///
    /// <para>Architectural note: the proper fix is to author the missing
    /// GLBs (or convert aggregate "parts" into <c>PartGroupDefinition</c>
    /// entries with <c>isAggregate=true</c> so they don't render at all).
    /// This component is the safety net catching the bug class so a future
    /// missing GLB never silently fills the scene with cubes again.</para>
    /// </summary>
    public sealed class NoAssetMarker : MonoBehaviour
    {
        /// <summary>Side length (meters) clamp applied to the marker primitive in the placement pass.</summary>
        public const float MarkerScale = 0.05f;

        /// <summary>RGBA tint applied to the marker so it's visually distinct from authored parts.</summary>
        public static readonly Color MarkerTint = new Color(1.0f, 0.10f, 0.95f, 1.0f);
    }
}
