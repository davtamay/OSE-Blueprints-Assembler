using System.Collections;
using OSE.Core;
using OSE.UI.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    [DisallowMultipleComponent]
    public sealed class TestAssemblyMechanicsSceneHarness : MonoBehaviour
    {
        private UIRootCoordinator _uiRootCoordinator;
        private GameObject _samplePart;

        private IEnumerator Start()
        {
            ConfigureCamera();
            CreateFloor();
            CreateTargetMarker();
            _samplePart = CreateSamplePart();

            yield return CreateUiHost();

            ShowInitialPreview();

            yield return new WaitForSeconds(2.5f);

            AdvancePreviewStep();
        }

        private void ConfigureCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                OseLog.Warn("[TestAssemblyMechanicsSceneHarness] No main camera found for preview setup.");
                return;
            }

            Transform cameraTransform = mainCamera.transform;
            cameraTransform.position = new Vector3(0f, 2.8f, -8.4f);
            cameraTransform.rotation = Quaternion.Euler(14f, 0f, 0f);
            mainCamera.backgroundColor = new Color(0.11f, 0.18f, 0.27f, 1f);
        }

        private void CreateFloor()
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Preview Floor";
            floor.transform.SetParent(transform, false);
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(1.7f, 1f, 1.7f);
            ApplyPreviewMaterial(floor, new Color(0.20f, 0.24f, 0.28f, 1f));
        }

        private void CreateTargetMarker()
        {
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            target.name = "Placement Target";
            target.transform.SetParent(transform, false);
            target.transform.position = new Vector3(0f, 0.04f, 0f);
            target.transform.localScale = new Vector3(0.9f, 0.04f, 0.9f);

            Collider targetCollider = target.GetComponent<Collider>();
            if (targetCollider != null)
            {
                targetCollider.enabled = false;
            }

            ApplyPreviewMaterial(target, new Color(0.20f, 0.84f, 0.58f, 1f));
        }

        private GameObject CreateSamplePart()
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "Sample Beam";
            part.transform.SetParent(transform, false);
            part.transform.position = new Vector3(-2f, 0.55f, 0f);
            part.transform.localScale = new Vector3(1.45f, 0.28f, 0.38f);
            ApplyPreviewMaterial(part, new Color(0.94f, 0.55f, 0.18f, 1f));
            return part;
        }

        private IEnumerator CreateUiHost()
        {
            GameObject uiHost = new GameObject("UI Root");
            uiHost.transform.SetParent(transform, false);

            uiHost.AddComponent<UIDocument>();
            uiHost.AddComponent<UIDocumentBootstrap>();
            _uiRootCoordinator = uiHost.AddComponent<UIRootCoordinator>();

            const int maxAttempts = 10;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (_uiRootCoordinator.TryInitialize())
                {
                    yield break;
                }

                yield return null;
            }

            OseLog.Warn("[TestAssemblyMechanicsSceneHarness] UI root did not initialize within the preview window.");
        }

        private void ShowInitialPreview()
        {
            if (_uiRootCoordinator == null)
            {
                return;
            }

            _uiRootCoordinator.ShowStepShell(
                1,
                3,
                "Inspect the chassis beam",
                "Compare the orange beam to the green target marker. This is the kind of instruction panel that will guide the learner step-by-step.");

            _uiRootCoordinator.ShowPartInfoShell(
                "Chassis Beam",
                "Connects two frame members and keeps the assembly square.",
                "Mild steel box tubing",
                "Tape measure, clamps",
                "frame beam crossmember steel tubing");
        }

        private void AdvancePreviewStep()
        {
            if (_samplePart != null)
            {
                _samplePart.transform.position = new Vector3(-0.6f, 0.55f, 0.15f);
                _samplePart.transform.localScale = new Vector3(1.35f, 0.28f, 0.38f);
            }

            if (_uiRootCoordinator == null)
            {
                return;
            }

            _uiRootCoordinator.ShowStepShell(
                2,
                3,
                "Move the beam toward the target",
                "The sample part shifts closer to the placement marker after a short delay. Later phases will replace this preview logic with runtime-driven step and selection state.");

            _uiRootCoordinator.ShowPartInfoShell(
                "Chassis Beam",
                "Supports frame alignment before fastening or welding.",
                "Mild steel box tubing",
                "Square, clamps, welder",
                "frame alignment chassis beam crossmember");
        }

        private static void ApplyPreviewMaterial(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                OseLog.Warn("[TestAssemblyMechanicsSceneHarness] No compatible shader found for preview material.");
                return;
            }

            Material material = new Material(shader);

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_EmissionColor"))
            {
                material.SetColor("_EmissionColor", color * 0.08f);
            }

            renderer.sharedMaterial = material;
        }
    }
}
