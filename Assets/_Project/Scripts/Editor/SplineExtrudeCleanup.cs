#if UNITY_SPLINES
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace OSE.Editor
{
    /// <summary>
    /// One-shot cleanup: removes all SplineExtrude + SplineContainer components
    /// (and their parent GameObjects when they have no other components) from the
    /// open scene. Run via OSE → Cleanup → Remove SplineExtrude Objects.
    ///
    /// Background: an earlier version of SplinePartFactory used SplineExtrude to
    /// render wire tubes. Those objects may have been saved into the scene during
    /// edit-mode testing and now throw ArgumentOutOfRangeException on OnValidate.
    /// The factory has since been replaced with cylinder primitives; this tool
    /// removes the leftover SplineExtrude GameObjects.
    /// </summary>
    public static class SplineExtrudeCleanup
    {
        [MenuItem("OSE/Cleanup/Remove SplineExtrude Objects from Scene")]
        private static void RemoveSplineExtrudeObjects()
        {
            var all = Object.FindObjectsByType<SplineExtrude>(FindObjectsSortMode.None);
            if (all.Length == 0)
            {
                EditorUtility.DisplayDialog("SplineExtrude Cleanup",
                    "No SplineExtrude components found in the open scene.", "OK");
                return;
            }

            int removed = 0;
            foreach (var se in all)
            {
                if (se == null) continue;
                var go = se.gameObject;

                // If the GameObject has no other meaningful components, destroy the whole object.
                // "Meaningful" = not Transform, not SplineExtrude, not SplineContainer,
                // not MeshFilter, not MeshRenderer (all added together by the factory).
                var comps = go.GetComponents<Component>();
                bool onlySplineComps = true;
                foreach (var c in comps)
                {
                    if (c is Transform || c is SplineExtrude || c is SplineContainer
                        || c is MeshFilter || c is MeshRenderer)
                        continue;
                    onlySplineComps = false;
                    break;
                }

                Undo.RegisterFullObjectHierarchyUndo(go, "Remove SplineExtrude Object");

                if (onlySplineComps)
                {
                    Undo.DestroyObjectImmediate(go);
                }
                else
                {
                    Undo.DestroyObjectImmediate(se);
                }
                removed++;
            }

            EditorUtility.DisplayDialog("SplineExtrude Cleanup",
                $"Removed {removed} SplineExtrude object(s). Save the scene to persist.", "OK");

            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        }
    }
}
#endif
