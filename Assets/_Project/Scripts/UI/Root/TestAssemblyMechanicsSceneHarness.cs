using System.Collections;
using OSE.Core;
using OSE.UI.Bindings;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Root
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TestAssemblyMechanicsSceneHarness : MonoBehaviour
    {
        [Header("Edit Mode Preview")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField] private bool _showGeometryPreview = true;
        [SerializeField] private bool _showUiPreview = true;
        [SerializeField] private bool _animatePreviewOnPlay = true;
        [SerializeField, Min(0f)] private float _playModeAdvanceDelay = 2.5f;

        [Header("Preview Step")]
        [SerializeField, Min(1)] private int _previewStepNumber = 1;
        [SerializeField, Min(1)] private int _previewTotalSteps = 3;
        [SerializeField] private string _previewStepTitle = "Inspect the chassis beam";
        [SerializeField, TextArea(2, 5)] private string _previewInstruction =
            "Compare the orange beam to the green target marker. This panel mirrors the kind of guidance that will come from runtime systems later.";

        [Header("Preview Part Info")]
        [SerializeField] private string _previewPartName = "Chassis Beam";
        [SerializeField, TextArea(2, 4)] private string _previewPartFunction =
            "Connects two frame members and keeps the assembly square.";
        [SerializeField] private string _previewPartMaterial = "Mild steel box tubing";
        [SerializeField] private string _previewPartTool = "Tape measure, clamps";
        [SerializeField] private string _previewPartSearchTerms = "frame beam crossmember steel tubing";

        [Header("Play Mode Transition")]
        [SerializeField, Min(1)] private int _playModeStepNumber = 2;
        [SerializeField] private string _playModeStepTitle = "Move the beam toward the target";
        [SerializeField, TextArea(2, 5)] private string _playModeInstruction =
            "When you press Play, the sample part slides closer to the target after a short delay so you can separate static editor preview from runtime behavior.";
        [SerializeField, TextArea(2, 4)] private string _playModePartFunction =
            "Supports frame alignment before fastening or welding.";
        [SerializeField] private string _playModePartTool = "Square, clamps, welder";
        [SerializeField] private string _playModePartSearchTerms = "frame alignment chassis beam crossmember";

        private UIRootCoordinator _uiRootCoordinator;
        private UIDocumentBootstrap _uiDocumentBootstrap;
        private UIDocument _uiDocument;
        private Transform _previewRoot;
        private GameObject _floor;
        private GameObject _targetMarker;
        private GameObject _samplePart;
        private bool _editModePreviewApplied;

        private void OnEnable()
        {
            _editModePreviewApplied = false;

            if (Application.isPlaying)
            {
                EnsurePreviewScaffold();
                ApplyPreviewState();
                StartCoroutine(PlayModeSequence());
                return;
            }

            if (_previewInEditMode)
            {
                EnsurePreviewScaffold();
                ApplyPreviewState();
            }
            else
            {
                ClearPreviewScaffold();
            }
        }

        private void Update()
        {
            if (Application.isPlaying || !_previewInEditMode)
            {
                return;
            }

            if (_editModePreviewApplied)
            {
                return;
            }

            EnsurePreviewScaffold();
            ApplyPreviewState();
        }

        private void OnValidate()
        {
            _previewStepNumber = Mathf.Max(1, _previewStepNumber);
            _previewTotalSteps = Mathf.Max(1, _previewTotalSteps);
            _playModeStepNumber = Mathf.Max(1, _playModeStepNumber);
            _playModeAdvanceDelay = Mathf.Max(0f, _playModeAdvanceDelay);

            _editModePreviewApplied = false;

            if (Application.isPlaying)
            {
                ApplyPreviewState();
                return;
            }

            if (_previewInEditMode)
            {
                EnsurePreviewScaffold();
                ApplyPreviewState();
            }
            else
            {
                ClearPreviewScaffold();
            }
        }

        private void OnDestroy()
        {
            if (!Application.isPlaying)
            {
                ClearPreviewScaffold();
            }
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

        private void EnsurePreviewScaffold()
        {
            _previewRoot = GetOrCreateChildTransform("Generated Preview");

            if (_showGeometryPreview)
            {
                _floor = GetOrCreatePrimitive("Preview Floor", PrimitiveType.Plane);
                _targetMarker = GetOrCreatePrimitive("Placement Target", PrimitiveType.Cylinder);
                _samplePart = GetOrCreatePrimitive("Sample Beam", PrimitiveType.Cube);
            }
            else
            {
                DestroyPreviewObject(ref _floor);
                DestroyPreviewObject(ref _targetMarker);
                DestroyPreviewObject(ref _samplePart);
            }

            if (_showUiPreview)
            {
                EnsureUiHost();
            }
            else
            {
                DestroyPreviewObject(ref _uiRootCoordinator);
                DestroyPreviewObject(ref _uiDocumentBootstrap);
                DestroyPreviewObject(ref _uiDocument);
            }
        }

        private void EnsureUiHost()
        {
            GameObject uiHost = GetOrCreateChildObject("UI Root");
            _uiDocument = GetOrAddComponent<UIDocument>(uiHost);
            _uiDocumentBootstrap = GetOrAddComponent<UIDocumentBootstrap>(uiHost);
            _uiRootCoordinator = GetOrAddComponent<UIRootCoordinator>(uiHost);
        }

        private void ApplyPreviewState()
        {
            ConfigureCamera();
            ApplyGeometryPreview();
            ApplyUiPreview(
                _previewStepNumber,
                _previewStepTitle,
                _previewInstruction,
                _previewPartFunction,
                _previewPartTool,
                _previewPartSearchTerms);

            if (!Application.isPlaying)
            {
                _editModePreviewApplied = true;
            }
        }

        private void ApplyGeometryPreview()
        {
            if (!_showGeometryPreview)
            {
                return;
            }

            if (_floor != null)
            {
                _floor.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                _floor.transform.localScale = new Vector3(1.7f, 1f, 1.7f);
                ApplyPreviewMaterial(_floor, "Preview Floor Material", new Color(0.20f, 0.24f, 0.28f, 1f));
            }

            if (_targetMarker != null)
            {
                _targetMarker.transform.SetLocalPositionAndRotation(new Vector3(0f, 0.04f, 0f), Quaternion.identity);
                _targetMarker.transform.localScale = new Vector3(0.9f, 0.04f, 0.9f);

                Collider targetCollider = _targetMarker.GetComponent<Collider>();
                if (targetCollider != null)
                {
                    targetCollider.enabled = false;
                }

                ApplyPreviewMaterial(_targetMarker, "Preview Target Material", new Color(0.20f, 0.84f, 0.58f, 1f));
            }

            if (_samplePart != null)
            {
                _samplePart.transform.SetLocalPositionAndRotation(new Vector3(-2f, 0.55f, 0f), Quaternion.identity);
                _samplePart.transform.localScale = new Vector3(1.45f, 0.28f, 0.38f);
                ApplyPreviewMaterial(_samplePart, "Preview Part Material", new Color(0.94f, 0.55f, 0.18f, 1f));
            }
        }

        private void ApplyUiPreview(
            int stepNumber,
            string stepTitle,
            string instruction,
            string function,
            string tool,
            string searchTerms)
        {
            if (!_showUiPreview || _uiRootCoordinator == null)
            {
                return;
            }

            if (!_uiRootCoordinator.TryInitialize())
            {
                _editModePreviewApplied = false;
                return;
            }

            _uiRootCoordinator.ShowPartInfoShell(
                _previewPartName,
                function,
                _previewPartMaterial,
                tool,
                searchTerms);

            _uiRootCoordinator.ShowStepShell(
                stepNumber,
                _previewTotalSteps,
                stepTitle,
                instruction);
        }

        private IEnumerator PlayModeSequence()
        {
            if (!_animatePreviewOnPlay)
            {
                yield break;
            }

            yield return new WaitForSeconds(_playModeAdvanceDelay);

            if (_samplePart != null)
            {
                _samplePart.transform.localPosition = new Vector3(-0.6f, 0.55f, 0.15f);
                _samplePart.transform.localScale = new Vector3(1.35f, 0.28f, 0.38f);
            }

            ApplyUiPreview(
                _playModeStepNumber,
                _playModeStepTitle,
                _playModeInstruction,
                _playModePartFunction,
                _playModePartTool,
                _playModePartSearchTerms);
        }

        [ContextMenu("Refresh Preview")]
        private void RefreshPreview()
        {
            _editModePreviewApplied = false;
            EnsurePreviewScaffold();
            ApplyPreviewState();
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreviewScaffold()
        {
            DestroyPreviewObject(ref _uiRootCoordinator);
            DestroyPreviewObject(ref _uiDocumentBootstrap);
            DestroyPreviewObject(ref _uiDocument);
            DestroyPreviewObject(ref _samplePart);
            DestroyPreviewObject(ref _targetMarker);
            DestroyPreviewObject(ref _floor);

            if (_previewRoot != null)
            {
                DestroyPreviewObject(_previewRoot.gameObject);
                _previewRoot = null;
            }

            _editModePreviewApplied = false;
        }

        private GameObject GetOrCreatePrimitive(string name, PrimitiveType primitiveType)
        {
            GameObject instance = GetOrCreateChildObject(name);
            if (instance.GetComponent<MeshFilter>() == null || instance.GetComponent<MeshRenderer>() == null)
            {
                DestroyPreviewObject(instance);
                instance = GameObject.CreatePrimitive(primitiveType);
                instance.name = name;
                instance.transform.SetParent(_previewRoot, false);
                ApplyPreviewHideFlags(instance);
            }

            return instance;
        }

        private GameObject GetOrCreateChildObject(string name)
        {
            Transform existingChild = _previewRoot != null ? _previewRoot.Find(name) : null;
            if (existingChild != null)
            {
                return existingChild.gameObject;
            }

            GameObject instance = new GameObject(name);
            instance.transform.SetParent(_previewRoot, false);
            ApplyPreviewHideFlags(instance);
            return instance;
        }

        private Transform GetOrCreateChildTransform(string name)
        {
            Transform existingChild = transform.Find(name);
            if (existingChild != null)
            {
                ApplyPreviewHideFlags(existingChild.gameObject);
                return existingChild;
            }

            GameObject instance = new GameObject(name);
            instance.transform.SetParent(transform, false);
            ApplyPreviewHideFlags(instance);
            return instance.transform;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            T created = target.AddComponent<T>();

            if (!Application.isPlaying)
            {
                created.hideFlags = HideFlags.DontSaveInEditor;
            }

            return created;
        }

        private static void ApplyPreviewMaterial(GameObject target, string materialName, Color color)
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

            Material material = renderer.sharedMaterial;
            if (material == null || material.shader != shader || material.name != materialName)
            {
                material = new Material(shader)
                {
                    name = materialName
                };

                if (!Application.isPlaying)
                {
                    material.hideFlags = HideFlags.DontSaveInEditor;
                }

                renderer.sharedMaterial = material;
            }

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
        }

        private static void ApplyPreviewHideFlags(GameObject target)
        {
            if (Application.isPlaying)
            {
                return;
            }

            target.hideFlags = HideFlags.DontSaveInEditor;

            Component[] components = target.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    components[i].hideFlags = HideFlags.DontSaveInEditor;
                }
            }
        }

        private static void DestroyPreviewObject<T>(ref T component) where T : Object
        {
            if (component == null)
            {
                return;
            }

            DestroyPreviewObject(component);
            component = null;
        }

        private static void DestroyPreviewObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private static void DestroyPreviewObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (target is Component component)
            {
                DestroyPreviewObject(component.gameObject);
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
