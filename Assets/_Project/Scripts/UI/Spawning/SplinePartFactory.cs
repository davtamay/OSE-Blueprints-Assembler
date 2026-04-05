using OSE.Content;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Creates tube-mesh GameObjects driven by <see cref="SplinePathDefinition"/> data.
    /// Used by <see cref="PackagePartSpawner"/> for hose and cable parts that define
    /// spline paths instead of referencing GLB assets.
    ///
    /// Implementation: one cylinder primitive per knot-to-knot segment.
    /// No SplineExtrude / com.unity.splines dependency — eliminates the
    /// OnValidate crash that fired before SegmentsPerUnit could be set.
    /// </summary>
    internal static class SplinePartFactory
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
        /// Creates a parent GameObject containing one capsule/cylinder segment per knot pair.
        /// Knot positions are in parent-local space.
        /// </summary>
        public static GameObject Create(
            string name,
            SplinePathDefinition data,
            Color color,
            Transform parent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);

            var mat = BuildMaterial(name, color, data.metallic, data.smoothness);

            var knots = data.knots;
            float diameter = data.radius * 2f;

            for (int i = 0; i < knots.Length - 1; i++)
            {
                var a = new Vector3(knots[i].x,     knots[i].y,     knots[i].z);
                var b = new Vector3(knots[i + 1].x, knots[i + 1].y, knots[i + 1].z);

                Vector3 midpoint = (a + b) * 0.5f;
                float   length   = Vector3.Distance(a, b);
                if (length < 0.0001f) continue;

                // Unity cylinder primitive is 2 units tall along local Y, radius 0.5
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                seg.name = $"{name}_seg{i}";
                seg.transform.SetParent(root.transform, false);
                seg.transform.localPosition = midpoint;
                seg.transform.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
                seg.transform.localScale    = new Vector3(diameter, length * 0.5f, diameter);

                // Remove default capsule collider — tube collisions not needed for wire visuals
                var col = seg.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Object.Destroy(col);
                    else Object.DestroyImmediate(col);
                }

                if (mat != null)
                    seg.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }

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
