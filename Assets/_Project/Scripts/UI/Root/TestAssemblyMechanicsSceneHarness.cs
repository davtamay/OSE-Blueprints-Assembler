using System.Collections;
using OSE.Core;
using OSE.Runtime.Preview;
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

        [SerializeField] private MechanicsSceneVisualProfile _visualProfile = null;

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
        private MechanicsSceneVisualProfile _builtInVisualProfile;

        private void OnEnable()
        {
            MechanicsSceneVisualProfile.Changed += HandleProfileChanged;
            _previewStateApplied = false;
            _playModeSequenceStarted = false;
            ConfigureForCurrentMode();
        }

        private void OnDisable()
        {
            MechanicsSceneVisualProfile.Changed -= HandleProfileChanged;
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

            if (!ActiveProfile.PreviewInEditMode)
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
            _previewStateApplied = false;
            _playModeSequenceStarted = false;

            if (!isActiveAndEnabled)
            {
                return;
            }

            ConfigureForCurrentMode();
        }

        private void OnDestroy()
        {
            MechanicsSceneVisualProfile.Changed -= HandleProfileChanged;

            if (_builtInVisualProfile == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_builtInVisualProfile);
            }
            else
            {
                DestroyImmediate(_builtInVisualProfile);
            }

            _builtInVisualProfile = null;
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
            PreviewCameraSettings camera = ActiveProfile.Camera;
            cameraTransform.position = camera.position;
            cameraTransform.rotation = Quaternion.Euler(camera.eulerAngles);
            mainCamera.backgroundColor = camera.backgroundColor;
        }

        private void ConfigureForCurrentMode()
        {
            if (!Application.isPlaying && !ActiveProfile.PreviewInEditMode)
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
            SetPreviewObjectActive(_floor, ActiveProfile.ShowGeometryPreview);
            SetPreviewObjectActive(_targetMarker, ActiveProfile.ShowGeometryPreview);
            SetPreviewObjectActive(_samplePart, ActiveProfile.ShowGeometryPreview);

            EnsureUiHost();
            SetPreviewObjectActive(_uiHost, ActiveProfile.ShowUiPreview);
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
            _previewStateApplied = true;
        }

        private void ApplyGeometryPreview()
        {
            if (!ActiveProfile.ShowGeometryPreview)
            {
                return;
            }

            if (_floor != null)
            {
                PreviewObjectAppearance floor = ActiveProfile.Floor;
                _floor.transform.SetLocalPositionAndRotation(floor.position, Quaternion.identity);
                _floor.transform.localScale = floor.scale;
                ApplyPreviewMaterial(_floor, "Preview Floor Material", floor.color);
            }

            if (_targetMarker != null)
            {
                PreviewObjectAppearance targetMarker = ActiveProfile.TargetMarker;
                _targetMarker.transform.SetLocalPositionAndRotation(targetMarker.position, Quaternion.identity);
                _targetMarker.transform.localScale = targetMarker.scale;

                Collider targetCollider = _targetMarker.GetComponent<Collider>();
                if (targetCollider != null)
                {
                    targetCollider.enabled = false;
                }

                ApplyPreviewMaterial(_targetMarker, "Preview Target Material", targetMarker.color);
            }

            if (_samplePart != null)
            {
                PreviewObjectAppearance samplePart = ActiveProfile.SamplePartStart;
                _samplePart.transform.SetLocalPositionAndRotation(samplePart.position, Quaternion.identity);
                _samplePart.transform.localScale = samplePart.scale;
                ApplyPreviewMaterial(_samplePart, "Preview Part Material", samplePart.color);
            }

            if (_uiRootCoordinator != null)
            {
                _uiRootCoordinator.TryInitialize();
            }
        }

        private IEnumerator PlayModeSequence()
        {
            if (!ActiveProfile.AnimateSamplePartOnPlay)
            {
                yield break;
            }

            yield return new WaitForSeconds(ActiveProfile.PlayModeAdvanceDelay);

            if (_samplePart != null)
            {
                PreviewObjectAppearance samplePartPlay = ActiveProfile.SamplePartPlay;
                _samplePart.transform.localPosition = samplePartPlay.position;
                _samplePart.transform.localScale = samplePartPlay.scale;
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

        [ContextMenu("Refresh Preview")]
        private void RefreshPreview()
        {
            _previewStateApplied = false;
            _playModeSequenceStarted = false;
            ConfigureForCurrentMode();
        }

        private void HandleProfileChanged(MechanicsSceneVisualProfile profile)
        {
            if (profile == null || profile != _visualProfile)
            {
                return;
            }

            RefreshPreview();
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

        private MechanicsSceneVisualProfile ActiveProfile
        {
            get
            {
                if (_visualProfile != null)
                {
                    return _visualProfile;
                }

                _builtInVisualProfile ??= MechanicsSceneVisualProfile.CreateBuiltInDefault();
                return _builtInVisualProfile;
            }
        }
    }
}
