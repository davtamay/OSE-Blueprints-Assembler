using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Receiver.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Rendering;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.State;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Theme;
using UnityEngine.XR.Interaction.Toolkit.AffordanceSystem.Theme.Primitives;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;

namespace OSE.UI.Root
{
    /// <summary>
    /// Static utility that wires XR Grab Interactable components and color
    /// affordance visuals onto spawned part GameObjects.
    /// Extracted from <see cref="PackagePartSpawner"/> for single-responsibility.
    /// </summary>
    internal static class XRPartInteractionSetup
    {
        internal static readonly Color HoveredAffordanceColor = new Color(0.60f, 0.82f, 1.0f, 1.0f);
        internal static readonly Color SelectedAffordanceColor = new Color(1.0f, 0.85f, 0.2f, 1.0f);

        internal static void TryEnableXRGrabInteractable(GameObject target, PartGrabConfig grabConfig = null)
        {
            if (target == null)
                return;

            XRGrabInteractable grabInteractable = target.GetComponent<XRGrabInteractable>();
            if (grabInteractable == null)
            {
                var rb = target.GetComponent<Rigidbody>();
                if (rb == null)
                    rb = target.AddComponent<Rigidbody>();

                rb.isKinematic = true;
                rb.useGravity = false;

                grabInteractable = target.AddComponent<XRGrabInteractable>();
            }

            // Apply authored grip point as XRI attachTransform offset
            if (grabConfig != null && grabConfig.HasGripPoint)
            {
                var attachName = "GripAttach";
                var existing = target.transform.Find(attachName);
                Transform attach = existing != null ? existing : new GameObject(attachName).transform;
                attach.SetParent(target.transform, false);
                attach.localPosition = grabConfig.GetGripPoint();
                if (grabConfig.HasGripRotation)
                    attach.localRotation = grabConfig.GetGripRotation();

                grabInteractable.useDynamicAttach = false;
                grabInteractable.attachTransform = attach;
            }

            DisablePartColorAffordance(target);
            ClearRendererPropertyBlocks(target);
        }

        internal static void DisablePartColorAffordance(GameObject target)
        {
            if (target == null)
                return;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(stateProvider);
                else
                    Object.DestroyImmediate(stateProvider);
            }

            var receivers = target.GetComponentsInChildren<ColorMaterialPropertyAffordanceReceiver>(includeInactive: true);
            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(receivers[i]);
                else
                    Object.DestroyImmediate(receivers[i]);
            }

            var blockHelpers = target.GetComponentsInChildren<MaterialPropertyBlockHelper>(includeInactive: true);
            for (int i = 0; i < blockHelpers.Length; i++)
            {
                if (blockHelpers[i] == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(blockHelpers[i]);
                else
                    Object.DestroyImmediate(blockHelpers[i]);
            }
        }

        internal static void EnsurePartColorAffordance(GameObject target, XRGrabInteractable grabInteractable)
        {
            if (target == null || grabInteractable == null)
                return;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                stateProvider = target.AddComponent<XRInteractableAffordanceStateProvider>();

            stateProvider.interactableSource = grabInteractable;
            stateProvider.transitionDuration = 0.08f;
            stateProvider.ignoreHoverEvents = true;
            stateProvider.ignoreHoverPriorityEvents = true;
            stateProvider.ignoreFocusEvents = true;
            stateProvider.ignoreSelectEvents = true;
            stateProvider.ignoreActivateEvents = true;
            stateProvider.selectClickAnimationMode = XRInteractableAffordanceStateProvider.SelectClickAnimationMode.None;
            stateProvider.activateClickAnimationMode = XRInteractableAffordanceStateProvider.ActivateClickAnimationMode.None;

            ColorAffordanceTheme theme = CreatePartColorAffordanceTheme();
            var renderers = MaterialHelper.GetRenderers(target);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;

                var blockHelper = renderer.GetComponent<MaterialPropertyBlockHelper>();
                if (blockHelper == null)
                    blockHelper = renderer.gameObject.AddComponent<MaterialPropertyBlockHelper>();
                blockHelper.rendererTarget = renderer;
                blockHelper.materialIndex = 0;

                var colorReceiver = renderer.GetComponent<ColorMaterialPropertyAffordanceReceiver>();
                if (colorReceiver == null)
                    colorReceiver = renderer.gameObject.AddComponent<ColorMaterialPropertyAffordanceReceiver>();

                colorReceiver.affordanceStateProvider = stateProvider;
                colorReceiver.replaceIdleStateValueWithInitialValue = true;
                colorReceiver.materialPropertyBlockHelper = blockHelper;
                colorReceiver.colorPropertyName = ResolveColorPropertyName(renderer.sharedMaterial);

                colorReceiver.affordanceTheme = theme;
            }
        }

        internal static string ResolveColorPropertyName(Material material)
        {
            if (material != null)
            {
                if (material.HasProperty("_BaseColor"))
                    return "_BaseColor";

                if (material.HasProperty("_Color"))
                    return "_Color";
            }

            return "_BaseColor";
        }

        internal static ColorAffordanceTheme CreatePartColorAffordanceTheme()
        {
            var theme = new ColorAffordanceTheme
            {
                colorBlendMode = ColorBlendMode.Solid,
                blendAmount = 1f
            };
            theme.SetAnimationCurve(AnimationCurve.Linear(0f, 0f, 1f, 1f));
            theme.SetAffordanceThemeDataList(new List<AffordanceThemeData<Color>>
            {
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.disabled),
                    animationStateStartValue = Color.clear,
                    animationStateEndValue = Color.clear
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.idle),
                    animationStateStartValue = Color.clear,
                    animationStateEndValue = Color.clear
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.hovered),
                    animationStateStartValue = HoveredAffordanceColor,
                    animationStateEndValue = HoveredAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.hoveredPriority),
                    animationStateStartValue = HoveredAffordanceColor,
                    animationStateEndValue = HoveredAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.selected),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.activated),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                },
                new AffordanceThemeData<Color>
                {
                    stateName = nameof(AffordanceStateShortcuts.focused),
                    animationStateStartValue = SelectedAffordanceColor,
                    animationStateEndValue = SelectedAffordanceColor
                }
            });

            return theme;
        }

        internal static bool TryApplyAffordanceState(GameObject target, byte stateIndex, float transitionAmount = 1f)
        {
            if (target == null)
                return false;

            var stateProvider = target.GetComponent<XRInteractableAffordanceStateProvider>();
            if (stateProvider == null)
                return false;

            stateProvider.UpdateAffordanceState(new AffordanceStateData(stateIndex, transitionAmount));
            return true;
        }

        internal static void ClearRendererPropertyBlocks(GameObject target)
        {
            if (target == null)
                return;

            var renderers = MaterialHelper.GetRenderers(target);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }
        }

        /// <summary>
        /// Adds MeshColliders to every child with a MeshFilter for accurate raycasting.
        /// Falls back to a fitted BoxCollider if no MeshFilters exist.
        /// </summary>
        public static void EnsureColliders(GameObject target)
        {
            // Spline parts use SplineMeshColliderBinder for deferred MeshCollider — skip EnsureColliders
            if (target.GetComponent<SplineMeshColliderBinder>() != null)
                return;

            // Add MeshColliders to every mesh child that doesn't already have one.
            // Don't skip when some children already have colliders — GLB imports
            // may only include a collider on one child, leaving others unclickable.
            bool addedAny = false;
            var meshFilters = target.GetComponentsInChildren<MeshFilter>(true);
            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                {
                    mf.gameObject.AddComponent<MeshCollider>();
                    addedAny = true;
                }
            }

            // Also handle SkinnedMeshRenderer children (rare in glTFast but possible)
            var skinned = target.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in skinned)
            {
                if (smr.sharedMesh != null && smr.GetComponent<Collider>() == null)
                {
                    var mc = smr.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = smr.sharedMesh;
                    addedAny = true;
                }
            }

            if (!addedAny && target.GetComponentInChildren<Collider>(true) == null)
            {
                // No mesh filters and no colliders at all — add a fitted BoxCollider
                var renderers = MaterialHelper.GetRenderers(target);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);
                    var box = target.AddComponent<BoxCollider>();
                    box.center = target.transform.InverseTransformPoint(bounds.center);
                    box.size = target.transform.InverseTransformVector(bounds.size);
                }
                else
                {
                    target.AddComponent<BoxCollider>();
                }
            }

            EnsureThinPartSelectionProxy(target);
        }

        internal static void EnsureThinPartSelectionProxy(GameObject target)
        {
            if (target == null)
                return;

            if (target.GetComponent<BoxCollider>() != null)
                return;

            var renderers = MaterialHelper.GetRenderers(target);
            if (renderers.Length == 0)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 localSize = AbsVector(target.transform.InverseTransformVector(bounds.size));
            float minAxis = Mathf.Min(localSize.x, Mathf.Min(localSize.y, localSize.z));

            const float MinClickableAxisMeters = 0.012f;
            if (minAxis >= MinClickableAxisMeters)
                return;

            var proxy = target.AddComponent<BoxCollider>();
            proxy.center = target.transform.InverseTransformPoint(bounds.center);
            proxy.size = new Vector3(
                Mathf.Max(localSize.x, MinClickableAxisMeters),
                Mathf.Max(localSize.y, MinClickableAxisMeters),
                Mathf.Max(localSize.z, MinClickableAxisMeters));
        }

        private static Vector3 AbsVector(Vector3 value)
        {
            return new Vector3(
                Mathf.Abs(value.x),
                Mathf.Abs(value.y),
                Mathf.Abs(value.z));
        }
    }
}
