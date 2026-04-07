using OSE.Content;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace OSE.UI.Root
{
    /// <summary>
    /// Creates smooth tube-mesh GameObjects driven by <see cref="SplinePathDefinition"/> data.
    ///
    /// Uses <see cref="SplineMesh.Extrude"/> directly (no SplineExtrude MonoBehaviour) to
    /// generate the tube geometry. This avoids the Reset()/OnValidate() lifecycle crashes that
    /// occurred when SplineExtrude fired Rebuild() during AddComponent before SegmentsPerUnit
    /// or knot data could be set.
    ///
    /// Callers that create objects in edit-mode (e.g. TTAW) must set
    /// HideFlags.HideAndDontSave on the returned GameObject so objects are not saved into
    /// the scene.
    /// </summary>
    public static class SplinePartFactory
    {
        private const string ShaderName = "Universal Render Pipeline/Lit";

        /// <summary>
        /// Returns true if the placement carries valid spline data (≥2 knots).
        /// </summary>
        public static bool HasSplineData(PartPreviewPlacement placement)
        {
            return placement?.splinePath?.knots != null
                && placement.splinePath.knots.Length >= 2;
        }

        /// <summary>
        /// Creates a smooth tube GameObject using SplineMesh.Extrude.
        /// Knot positions are in parent-local space.
        /// </summary>
        /// <param name="tangentMode">
        /// <see cref="TangentMode.AutoSmooth"/> for a natural cable curve;
        /// <see cref="TangentMode.Linear"/> for rigid straight segments between knots.
        /// </param>
        /// <summary>
        /// Creates a selectable wire spline — same geometry as <see cref="Create"/> but
        /// also attaches a <see cref="WireSplineMarker"/> and a <see cref="MeshCollider"/>
        /// so the selection system can identify and raycast-hit the wire.
        /// </summary>
        public static GameObject CreateWire(
            string targetId,
            string stepId,
            SplinePathDefinition data,
            Color color,
            Transform parent,
            TangentMode tangentMode = TangentMode.AutoSmooth)
        {
            var go = Create(targetId, data, color, parent, tangentMode);
            if (go == null) return go;

            var marker = go.AddComponent<WireSplineMarker>();
            marker.targetId = targetId;
            marker.stepId   = stepId;

            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var col = go.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
                col.convex     = false;
            }

            return go;
        }

        public static GameObject Create(
            string name,
            SplinePathDefinition data,
            Color color,
            Transform parent,
            TangentMode tangentMode = TangentMode.AutoSmooth)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var knotData = data.knots;
            if (knotData == null || knotData.Length < 2)
                return root;

            // Build a plain Spline — no MonoBehaviour component, no lifecycle callbacks.
            var spline = new Spline();
            for (int i = 0; i < knotData.Length; i++)
                spline.Add(
                    new BezierKnot(new float3(knotData[i].x, knotData[i].y, knotData[i].z)),
                    tangentMode);

            float radius     = data.radius > 0f ? data.radius : 0.003f;
            int   sides      = 8;
            int   knotCount  = knotData.Length;
            // More knots → more mesh segments so the shape is fully captured.
            // Linear mode uses exact knot count; bezier uses higher density for smoothness.
            int   minSegs    = tangentMode == TangentMode.Linear ? knotCount - 1 : knotCount * 4;
            int   segPerUnit = data.segments > 0 ? data.segments : 16;
            // SplineMesh requires segments >= 3; enforce regardless of wire length.
            int   totalSegs  = Mathf.Max(3, Mathf.Max(minSegs, Mathf.RoundToInt(spline.GetLength() * segPerUnit)));

            var mesh = new Mesh { name = $"{name}_Mesh" };
            SplineMesh.Extrude(spline, mesh, radius, sides, totalSegs, capped: true);

            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr  = root.AddComponent<MeshRenderer>();
            // Prefer data.color (spline-definition color) when its alpha > 0; fall back to caller-supplied color.
            Color resolvedColor = (data.color.a > 0f)
                ? new Color(data.color.r, data.color.g, data.color.b, 1f)
                : color;
            var mat = BuildMaterial(name, resolvedColor, data.metallic, data.smoothness);
            if (mat != null) mr.sharedMaterial = mat;

            return root;
        }

        /// <summary>
        /// Creates a preview version of a spline tube. Same geometry, white color
        /// (MaterialHelper.ApplyPreviewMaterial will override the material).
        /// </summary>
        public static GameObject CreatePreview(
            string partId,
            SplinePathDefinition data,
            Transform parent)
        {
            var go = Create(partId, data, Color.white, parent);
            if (go != null)
                go.name = $"Preview_{partId}";
            return go;
        }

        private static Material BuildMaterial(string name, Color color, float metallic, float smoothness)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) return null;

            var mat = new Material(shader);
            mat.name = $"{name}_Mat";
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Metallic",   metallic);
            mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }
    }
}
