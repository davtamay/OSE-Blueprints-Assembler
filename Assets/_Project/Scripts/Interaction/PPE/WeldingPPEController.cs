using System.Collections;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace OSE.Interaction
{
    /// <summary>
    /// Toggles welding PPE visuals (glove material swap + visor overlay) when
    /// the active step has a Weld profile. Attach to a GameObject under the XR
    /// Origin rig and wire up hand renderers + visor prefab in the inspector.
    /// </summary>
    public sealed class WeldingPPEController : MonoBehaviour
    {
        [Header("Hand Renderers")]
        [Tooltip("SkinnedMeshRenderer for the left hand visual (from XRI hand prefab).")]
        [SerializeField] private SkinnedMeshRenderer _leftHandRenderer;

        [Tooltip("SkinnedMeshRenderer for the right hand visual (from XRI hand prefab).")]
        [SerializeField] private SkinnedMeshRenderer _rightHandRenderer;

        [Header("Glove")]
        [Tooltip("Opaque URP/Lit material that looks like a welding glove.")]
        [SerializeField] private Material _gloveMaterial;

        [Header("Visor")]
        [Tooltip("Prefab with a Quad mesh + semi-transparent tinted visor material.")]
        [SerializeField] private GameObject _visorPrefab;

        [Header("Transition")]
        [SerializeField] private float _transitionDuration = 0.3f;

        private bool _ppeActive;
        private GameObject _visorInstance;
        private Material _visorMaterialInstance;

        private Material[] _leftOriginalMaterials;
        private Material[] _rightOriginalMaterials;
        private Coroutine _transitionCoroutine;

        private void OnEnable()
        {
            RuntimeEventBus.Subscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Subscribe<StepActivated>(HandleStepActivated);
        }

        private void OnDisable()
        {
            RuntimeEventBus.Unsubscribe<StepStateChanged>(HandleStepStateChanged);
            RuntimeEventBus.Unsubscribe<StepActivated>(HandleStepActivated);

            if (_ppeActive)
                DeactivatePPEImmediate();
        }

        private void HandleStepActivated(StepActivated evt)
        {
            // StepActivated fires before StepStateChanged(Active) in some paths.
            // Use it as a secondary trigger to ensure we catch weld steps.
            EvaluateStep(evt.StepId);
        }

        private void HandleStepStateChanged(StepStateChanged evt)
        {
            if (evt.Current == StepState.Active)
            {
                EvaluateStep(evt.StepId);
            }
            else if (evt.Current == StepState.Completed || evt.Current == StepState.Suspended)
            {
                if (_ppeActive)
                    SetPPE(false);
            }
        }

        private void EvaluateStep(string stepId)
        {
            bool isWeld = IsWeldProfile(stepId);

            if (isWeld && !_ppeActive)
                SetPPE(true);
            else if (!isWeld && _ppeActive)
                SetPPE(false);
        }

        private bool IsWeldProfile(string stepId)
        {
            if (string.IsNullOrWhiteSpace(stepId))
                return false;

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session))
                return false;

            StepDefinition step = session?.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step == null)
                return false;

            return step.ResolvedProfile == StepProfile.Weld;
        }

        // ── Activation / Deactivation ──────────────────────────────────

        private void SetPPE(bool active)
        {
            if (_transitionCoroutine != null)
                StopCoroutine(_transitionCoroutine);

            _transitionCoroutine = StartCoroutine(TransitionPPE(active));
        }

        private IEnumerator TransitionPPE(bool activate)
        {
            if (activate)
            {
                SaveHandMaterials();
                ApplyGloveMaterials();
                EnsureVisor();
                _ppeActive = true;
            }

            float elapsed = 0f;
            float from = activate ? 0f : 1f;
            float to = activate ? 1f : 0f;

            while (elapsed < _transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _transitionDuration);
                float alpha = Mathf.Lerp(from, to, t);
                SetVisorAlpha(alpha);
                yield return null;
            }

            SetVisorAlpha(to);

            if (!activate)
            {
                RestoreHandMaterials();
                HideVisor();
                _ppeActive = false;
            }

            _transitionCoroutine = null;
        }

        private void DeactivatePPEImmediate()
        {
            if (_transitionCoroutine != null)
            {
                StopCoroutine(_transitionCoroutine);
                _transitionCoroutine = null;
            }

            RestoreHandMaterials();
            HideVisor();
            _ppeActive = false;
        }

        // ── Glove Materials ────────────────────────────────────────────

        private void SaveHandMaterials()
        {
            if (_leftHandRenderer != null)
                _leftOriginalMaterials = _leftHandRenderer.sharedMaterials;
            if (_rightHandRenderer != null)
                _rightOriginalMaterials = _rightHandRenderer.sharedMaterials;
        }

        private void ApplyGloveMaterials()
        {
            if (_gloveMaterial == null)
                return;

            ApplyGloveToRenderer(_leftHandRenderer);
            ApplyGloveToRenderer(_rightHandRenderer);
        }

        private void ApplyGloveToRenderer(SkinnedMeshRenderer renderer)
        {
            if (renderer == null)
                return;

            int slotCount = renderer.sharedMaterials.Length;
            if (slotCount <= 0) slotCount = 1;

            Material[] gloves = new Material[slotCount];
            for (int i = 0; i < slotCount; i++)
                gloves[i] = _gloveMaterial;

            renderer.sharedMaterials = gloves;
        }

        private void RestoreHandMaterials()
        {
            if (_leftHandRenderer != null && _leftOriginalMaterials != null)
                _leftHandRenderer.sharedMaterials = _leftOriginalMaterials;
            if (_rightHandRenderer != null && _rightOriginalMaterials != null)
                _rightHandRenderer.sharedMaterials = _rightOriginalMaterials;

            _leftOriginalMaterials = null;
            _rightOriginalMaterials = null;
        }

        // ── Visor ──────────────────────────────────────────────────────

        private void EnsureVisor()
        {
            if (_visorInstance != null)
            {
                _visorInstance.SetActive(true);
                return;
            }

            if (_visorPrefab == null)
            {
                CreateProceduralVisor();
                return;
            }

            Camera cam = CameraUtil.GetMain();
            if (cam == null) return;

            _visorInstance = Instantiate(_visorPrefab, cam.transform);
            _visorInstance.name = "WeldingVisor";
            CacheVisorMaterial();
        }

        private void CreateProceduralVisor()
        {
            Camera cam = CameraUtil.GetMain();
            if (cam == null) return;

            _visorInstance = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _visorInstance.name = "WeldingVisor";

            // Remove collider — this is purely visual.
            var col = _visorInstance.GetComponent<Collider>();
            if (col != null) Destroy(col);

            Transform t = _visorInstance.transform;
            t.SetParent(cam.transform, false);
            t.localPosition = new Vector3(0f, 0f, 0.3f);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(0.8f, 0.5f, 1f);

            // Build a transparent tinted material.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) return;

            _visorMaterialInstance = new Material(shader) { name = "WeldingVisorMat" };
            _visorMaterialInstance.SetFloat("_Surface", 1f);      // transparent
            _visorMaterialInstance.SetFloat("_Blend", 0f);        // alpha blend
            _visorMaterialInstance.SetOverrideTag("RenderType", "Transparent");
            _visorMaterialInstance.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _visorMaterialInstance.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _visorMaterialInstance.SetInt("_ZWrite", 0);
            _visorMaterialInstance.renderQueue = 3500;
            _visorMaterialInstance.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            // Dark amber-green tint simulating auto-darkening welding glass.
            Color visorColor = new Color(0.05f, 0.15f, 0.02f, 0f);
            if (_visorMaterialInstance.HasProperty("_BaseColor"))
                _visorMaterialInstance.SetColor("_BaseColor", visorColor);
            if (_visorMaterialInstance.HasProperty("_Color"))
                _visorMaterialInstance.SetColor("_Color", visorColor);

            _visorInstance.GetComponent<Renderer>().sharedMaterial = _visorMaterialInstance;
        }

        private void CacheVisorMaterial()
        {
            if (_visorInstance == null) return;
            var renderer = _visorInstance.GetComponentInChildren<Renderer>();
            if (renderer != null)
                _visorMaterialInstance = renderer.material; // instance copy
        }

        private void SetVisorAlpha(float normalizedAlpha)
        {
            if (_visorMaterialInstance == null) return;

            // Map 0..1 to 0..0.35 — the max visor opacity.
            float alpha = normalizedAlpha * 0.35f;

            if (_visorMaterialInstance.HasProperty("_BaseColor"))
            {
                Color c = _visorMaterialInstance.GetColor("_BaseColor");
                c.a = alpha;
                _visorMaterialInstance.SetColor("_BaseColor", c);
            }

            if (_visorMaterialInstance.HasProperty("_Color"))
            {
                Color c = _visorMaterialInstance.GetColor("_Color");
                c.a = alpha;
                _visorMaterialInstance.SetColor("_Color", c);
            }
        }

        private void HideVisor()
        {
            if (_visorInstance != null)
                _visorInstance.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_visorMaterialInstance != null)
                Destroy(_visorMaterialInstance);
            if (_visorInstance != null)
                Destroy(_visorInstance);
        }
    }
}
