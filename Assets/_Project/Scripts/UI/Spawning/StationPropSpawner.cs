using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Spawns simple primitive workbench props from <c>previewConfig.stations[]</c>.
    /// Each station with <c>surfaceY > 0.01</c> gets a flat cube representing the
    /// table surface. The frame station (surfaceY ≈ 0) gets no prop — parts mount
    /// directly on the floor.
    ///
    /// Props are spawned when a package loads and destroyed on package change.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(PreviewSceneSetup))]
    public sealed class StationPropSpawner : MonoBehaviour
    {
        private const string PropParentName = "Station Props";
        private const float TableThickness = 0.04f; // 4 cm slab

        private PreviewSceneSetup _setup;
        private readonly List<GameObject> _props = new();

        private void OnEnable()
        {
            _setup = GetComponent<PreviewSceneSetup>();
            RuntimeEventBus.Subscribe<PackageLoaded>(OnPackageLoaded);
            RuntimeEventBus.Subscribe<StationCompositionCompleted>(OnCompositionCompleted);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<PackageLoaded>(OnPackageLoaded);
            RuntimeEventBus.Unsubscribe<StationCompositionCompleted>(OnCompositionCompleted);
            ClearProps();
        }

        private void OnPackageLoaded(PackageLoaded e)
        {
            ClearProps();

            var pkg = Runtime.Preview.SessionDriver.CurrentPackage;
            if (pkg?.previewConfig?.stations == null) return;

            SpawnStationProps(pkg.previewConfig.stations);
        }

        private void SpawnStationProps(AssemblyStationDefinition[] stations)
        {
            if (_setup.PreviewRoot == null) return;

            // Parent container under the preview root
            var parentTransform = _setup.PreviewRoot.Find(PropParentName);
            if (parentTransform == null)
            {
                var parentGo = new GameObject(PropParentName);
                parentGo.transform.SetParent(_setup.PreviewRoot, false);
                parentTransform = parentGo.transform;
            }

            foreach (var station in stations)
            {
                // Only spawn a table prop for bench stations (surfaceY > floor level)
                if (station.surfaceY < 0.01f) continue;

                var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                table.name = $"Table_{station.id}";

                // Scale: layoutWidth x thickness x layoutDepth
                table.transform.SetParent(parentTransform, false);
                table.transform.localScale = new Vector3(
                    station.layoutWidth,
                    TableThickness,
                    station.layoutDepth);

                // Position: center of the table surface at surfaceY
                // The cube pivot is at center, so offset Y down by half thickness
                // so the top face aligns with surfaceY exactly.
                table.transform.localPosition = new Vector3(
                    station.position.x,
                    station.surfaceY - TableThickness * 0.5f,
                    station.position.z);

                // Neutral material — light grey, no specularity
                var renderer = table.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    mat.color = new Color(0.75f, 0.75f, 0.75f, 1f);
                    // Disable specular highlights for a matte look
                    mat.SetFloat("_Smoothness", 0.1f);
                    renderer.sharedMaterial = mat;
                }

                // Remove the default box collider — table is visual only
                var collider = table.GetComponent<Collider>();
                if (collider != null)
                {
                    if (Application.isPlaying) Destroy(collider);
                    else DestroyImmediate(collider);
                }

                _props.Add(table);
                OseLog.Info($"[StationProps] Spawned table for '{station.displayName}' at y={station.surfaceY}");
            }
        }

        private void OnCompositionCompleted(StationCompositionCompleted e)
        {
            // Optionally dim the table when all parts leave the bench.
            // For now, just log — visual feedback can be refined later.
            foreach (var prop in _props)
            {
                if (prop == null) continue;
                if (prop.name == $"Table_{e.StationId}")
                {
                    var renderer = prop.GetComponent<Renderer>();
                    if (renderer != null)
                        renderer.material.color = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                    OseLog.Info($"[StationProps] Bench '{e.StationId}' composition complete — table dimmed.");
                    break;
                }
            }
        }

        private void ClearProps()
        {
            foreach (var go in _props)
            {
                if (go == null) continue;
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
            _props.Clear();

            // Clean up the parent container too
            if (_setup != null && _setup.PreviewRoot != null)
            {
                var parent = _setup.PreviewRoot.Find(PropParentName);
                if (parent != null)
                {
                    if (Application.isPlaying) Destroy(parent.gameObject);
                    else DestroyImmediate(parent.gameObject);
                }
            }
        }
    }
}
