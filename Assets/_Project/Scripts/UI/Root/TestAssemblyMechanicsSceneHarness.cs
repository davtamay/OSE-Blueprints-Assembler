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
        private const string PreviewRootName = "Preview Scaffold";
        private const string LegacyPreviewRootName = "Generated Preview";
        private const string FloorName = "Preview Floor";
        private const string TargetMarkerName = "Placement Target";
        private const string SamplePartName = "Sample Beam";
        private const string UiHostName = "UI Root";

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
        private GameObject _uiHost;
        private Transform _previewRoot;
        private GameObject _floor;
        private GameObject _targetMarker;
        private GameObject _samplePart;
        private bool _previewStateApplied;
        private bool _playModeSequenceStarted;

        private void OnEnable()
        {
            _previewStateApplied = false;
            _playModeSequenceStarted = false;
            ConfigureForCurrentMode();
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (!_previewStateApplied)
                {
                    ConfigureForCurrentMode();
                }

                StartPlayModeSequenceIfNeeded();
                return;
            }

            if (!_previewInEditMode)
            {
                CachePreviewScaffold();
                SetPreviewScaffoldActive(false);
                return;
            }

            if (_previewStateApplied)
            {
                return;
            }

            ConfigureForCurrentMode();
        }

        private void OnValidate()
        {
            _previewStepNumber = Mathf.Max(1, _previewStepNumber);
            _previewTotalSteps = Mathf.Max(1, _previewTotalSteps);
            _playModeStepNumber = Mathf.Max(1, _playModeStepNumber);
            _playModeAdvanceDelay = Mathf.Max(0f, _playModeAdvanceDelay);

            _previewStateApplied = false;
            _playModeSequenceStarted = false;

            if (!isActiveAndEnabled)
            {
                return;
            }

            ConfigureForCurrentMode();
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

        private void ConfigureForCurrentMode()
        {
            if (!Application.isPlaying && !_previewInEditMode)
            {
                CachePreviewScaffold();
                SetPreviewScaffoldActive(false);
                return;
            }

            EnsurePreviewScaffold();
            SetPreviewScaffoldActive(true);
            ApplyPreviewState();
            StartPlayModeSequenceIfNeeded();
        }

        private void CachePreviewScaffold()
        {
            if (_previewRoot == null)
            {
                _previewRoot = transform.Find(PreviewRootName);

                if (_previewRoot == null)
                {
                    _previewRoot = transform.Find(LegacyPreviewRootName);

                    if (_previewRoot != null)
                    {
                        _previewRoot.name = PreviewRootName;
                    }
                }
            }

            if (_previewRoot == null)
            {
                return;
            }

            _floor = FindPreviewChild(FloorName);
            _targetMarker = FindPreviewChild(TargetMarkerName);
            _samplePart = FindPreviewChild(SamplePartName);
            _uiHost = FindPreviewChild(UiHostName);

            if (_uiHost == null)
            {
                _uiDocument = null;
                _uiDocumentBootstrap = null;
                _uiRootCoordinator = null;
                return;
            }

            _uiDocument = _uiHost.GetComponent<UIDocument>();
            _uiDocumentBootstrap = _uiHost.GetComponent<UIDocumentBootstrap>();
            _uiRootCoordinator = _uiHost.GetComponent<UIRootCoordinator>();
        }

        private void EnsurePreviewScaffold()
        {
            CachePreviewScaffold();
            _previewRoot = GetOrCreateChildTransform(PreviewRootName);

            _floor = GetOrCreatePrimitive(FloorName, PrimitiveType.Plane);
            _targetMarker = GetOrCreatePrimitive(TargetMarkerName, PrimitiveType.Cylinder);
            _samplePart = GetOrCreatePrimitive(SamplePartName, PrimitiveType.Cube);
            SetPreviewObjectActive(_floor, _showGeometryPreview);
            SetPreviewObjectActive(_targetMarker, _showGeometryPreview);
            SetPreviewObjectActive(_samplePart, _showGeometryPreview);

            EnsureUiHost();
            SetPreviewObjectActive(_uiHost, _showUiPreview);
        }

        private void EnsureUiHost()
        {
            _uiHost = GetOrCreateChildObject(UiHostName);
            _uiDocument = GetOrAddComponent<UIDocument>(_uiHost);
            _uiDocumentBootstrap = GetOrAddComponent<UIDocumentBootstrap>(_uiHost);
            _uiRootCoordinator = GetOrAddComponent<UIRootCoordinator>(_uiHost);
        }

        private void ApplyPreviewState()
        {
            ConfigureCamera();
            ApplyGeometryPreview();
            bool uiApplied = !ShouldDriveLocalUiPreview() || ApplyUiPreview(
                _previewStepNumber,
                _previewStepTitle,
                _previewInstruction,
                _previewPartFunction,
                _previewPartTool,
                _previewPartSearchTerms);
            _previewStateApplied = !_showUiPreview || uiApplied;
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

        private bool ApplyUiPreview(
            int stepNumber,
            string stepTitle,
            string instruction,
            string function,
            string tool,
            string searchTerms)
        {
            if (!_showUiPreview)
            {
                return true;
            }

            if (_uiRootCoordinator == null)
            {
                return false;
            }

            if (!_uiRootCoordinator.TryInitialize())
            {
                return false;
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

            return true;
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

            if (ShouldDriveLocalUiPreview())
            {
                ApplyUiPreview(
                    _playModeStepNumber,
                    _playModeStepTitle,
                    _playModeInstruction,
                    _playModePartFunction,
                    _playModePartTool,
                    _playModePartSearchTerms);
            }
        }

        private void StartPlayModeSequenceIfNeeded()
        {
            if (!Application.isPlaying || _playModeSequenceStarted || !_previewStateApplied)
            {
                return;
            }

            _playModeSequenceStarted = true;
            StartCoroutine(PlayModeSequence());
        }

        private bool ShouldDriveLocalUiPreview() =>
            _showUiPreview && GetComponent("MachinePackagePreviewDriver") == null;

        [ContextMenu("Refresh Preview")]
        private void RefreshPreview()
        {
            _previewStateApplied = false;
            _playModeSequenceStarted = false;
            ConfigureForCurrentMode();
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreviewScaffold()
        {
            CachePreviewScaffold();

            if (_previewRoot != null)
            {
                DestroyPreviewObject(_previewRoot.gameObject);
            }

            _previewRoot = null;
            _uiHost = null;
            _uiRootCoordinator = null;
            _uiDocumentBootstrap = null;
            _uiDocument = null;
            _samplePart = null;
            _targetMarker = null;
            _floor = null;
            _previewStateApplied = false;
            _playModeSequenceStarted = false;
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
            return instance;
        }

        private Transform GetOrCreateChildTransform(string name)
        {
            Transform existingChild = transform.Find(name);
            if (existingChild != null)
            {
                return existingChild;
            }

            GameObject instance = new GameObject(name);
            instance.transform.SetParent(transform, false);
            return instance.transform;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T existing = target.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            return target.AddComponent<T>();
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

        private GameObject FindPreviewChild(string name)
        {
            if (_previewRoot == null)
            {
                return null;
            }

            Transform child = _previewRoot.Find(name);
            return child != null ? child.gameObject : null;
        }

        private void SetPreviewScaffoldActive(bool isActive)
        {
            if (_previewRoot != null && _previewRoot.gameObject.activeSelf != isActive)
            {
                _previewRoot.gameObject.SetActive(isActive);
            }
        }

        private static void SetPreviewObjectActive(GameObject target, bool isActive)
        {
            if (target != null && target.activeSelf != isActive)
            {
                target.SetActive(isActive);
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
