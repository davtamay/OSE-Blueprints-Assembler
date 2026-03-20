using OSE.Content;
using UnityEngine;
#if UNITY_SPLINES
using Unity.Mathematics;
using UnityEngine.Splines;
#endif

namespace OSE.UI.Root
{
    /// <summary>
    /// Creates tube-mesh GameObjects driven by <see cref="SplinePathDefinition"/> data.
    /// Used by <see cref="PackagePartSpawner"/> for hose and cable parts that define
    /// spline paths instead of referencing GLB assets.
    /// Requires com.unity.splines package — gracefully no-ops when absent.
    /// </summary>
    internal static class SplinePartFactory
    {
        /// <summary>
        /// Returns true if the placement carries valid spline data (≥2 knots)
        /// and the splines package is available.
        /// </summary>
        public static bool HasSplineData(PartPreviewPlacement placement)
        {
#if UNITY_SPLINES
            return placement?.splinePath?.knots != null
                && placement.splinePath.knots.Length >= 2;
#else
            return false;
#endif
        }

#if UNITY_SPLINES
        private const int DefaultSegments = 8;
        private const int SegmentsPerUnit = 16;
        private const string ShaderName = "Universal Render Pipeline/Lit";
#endif

        /// <summary>
        /// Creates a new GameObject with SplineContainer + SplineExtrude that renders
        /// a tube along the given spline path. The knot positions are in parent-local space.
        /// Returns null when the splines package is not installed.
        /// </summary>
        public static GameObject Create(
            string name,
            SplinePathDefinition data,
            Color color,
            Transform parent)
        {
#if UNITY_SPLINES
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // --- Spline ---
            var container = go.AddComponent<SplineContainer>();
            var spline = container.Spline;
            spline.Clear();

            foreach (var k in data.knots)
                spline.Add(new BezierKnot(new float3(k.x, k.y, k.z)));

            // Auto-smooth tangents for natural curves
            for (int i = 0; i < spline.Count; i++)
                spline.SetTangentMode(i, TangentMode.AutoSmooth);

            // --- Mesh extrusion ---
            var extrude = go.AddComponent<SplineExtrude>();
            extrude.Container = container;
            extrude.Radius = data.radius;
            extrude.Sides = data.segments > 0 ? data.segments : DefaultSegments;
            extrude.SegmentsPerUnit = SegmentsPerUnit;
            extrude.Capped = true;

            // --- Material ---
            var shader = Shader.Find(ShaderName);
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.name = $"{name}_SplineMat";
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Metallic", data.metallic);
                mat.SetFloat("_Smoothness", data.smoothness);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.sharedMaterial = mat;
            }

            // Add a deferred MeshCollider that binds to the SplineExtrude mesh
            // once it has been generated. This hugs the tube shape exactly and
            // avoids the large bounding-box overlap a BoxCollider would cause.
            go.AddComponent<SplineMeshColliderBinder>();

            return go;
#else
            return null;
#endif
        }

#if UNITY_SPLINES
        /// <summary>
        /// Creates a ghost (transparent) version of a spline tube for placement preview.
        /// Uses the same knot geometry but with a transparent ghost material.
        /// </summary>
        public static GameObject CreateGhost(
            string partId,
            SplinePathDefinition data,
            Transform parent)
        {
            // Create in a neutral gray-blue ghost color — MaterialHelper.ApplyGhost will override
            var go = Create(partId, data, Color.white, parent);
            if (go != null)
                go.name = $"Ghost_{partId}";
            return go;
        }
#endif
    }

    /// <summary>
    /// Waits for SplineExtrude to generate a mesh, then assigns a MeshCollider.
    /// Destroys itself once the collider is bound.
    /// </summary>
    internal sealed class SplineMeshColliderBinder : MonoBehaviour
    {
        private MeshFilter _mf;
        private MeshCollider _mc;

        private void Start()
        {
            _mf = GetComponent<MeshFilter>();
            if (_mf != null && _mf.sharedMesh != null && _mf.sharedMesh.vertexCount > 0)
            {
                Bind();
                return;
            }
            // SplineExtrude hasn't generated the mesh yet — check each frame.
        }

        private void LateUpdate()
        {
            if (_mf == null)
                _mf = GetComponent<MeshFilter>();

            if (_mf != null && _mf.sharedMesh != null && _mf.sharedMesh.vertexCount > 0)
                Bind();
        }

        private void Bind()
        {
            if (_mc == null)
            {
                _mc = gameObject.AddComponent<MeshCollider>();
                _mc.sharedMesh = _mf.sharedMesh;
            }
            Destroy(this); // self-destruct — collider is bound
        }
    }

}

