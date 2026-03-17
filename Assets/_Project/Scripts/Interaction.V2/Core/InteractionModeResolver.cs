using System.Runtime.InteropServices;
using OSE.Core;
using UnityEngine;
using UnityEngine.XR;

namespace OSE.Interaction.V2
{
    /// <summary>
    /// Single source of truth for which interaction mode the app should use.
    /// Resolves once at startup, handles all platform permutations:
    ///
    /// ┌─────────────────────────────────┬────────────────────────┐
    /// │ Condition                       │ Result                 │
    /// ├─────────────────────────────────┼────────────────────────┤
    /// │ Editor override != Auto         │ Use override directly  │
    /// │ Editor + XR device/sim active   │ XR                     │
    /// │ Editor + no XR                  │ Desktop                │
    /// │ WebGL  + XR headset (Quest etc) │ XR                     │
    /// │ WebGL  + mobile browser         │ Mobile                 │
    /// │ WebGL  + desktop browser        │ Desktop                │
    /// │ Android/iOS standalone          │ Mobile                 │
    /// │ Windows/Mac/Linux standalone    │ Desktop                │
    /// └─────────────────────────────────┴────────────────────────┘
    /// </summary>
    public static class InteractionModeResolver
    {
        // ── WebGL jslib imports ──

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern bool WebGL_IsXRHeadset();
        [DllImport("__Internal")] private static extern bool WebGL_IsMobileDevice();
#endif

        /// <summary>
        /// Resolve the interaction mode for the current session.
        /// Pass the editor override from InteractionOrchestrator; Auto means auto-detect.
        /// </summary>
        public static InteractionMode Resolve(InteractionMode editorOverride = InteractionMode.Auto)
        {
            InteractionMode mode;

#if UNITY_EDITOR
            if (editorOverride != InteractionMode.Auto)
            {
                mode = editorOverride;
                OseLog.Info($"[InteractionModeResolver] Editor override: {mode}");
                return mode;
            }
            mode = ResolveEditor();
#elif UNITY_WEBGL
            mode = ResolveWebGL();
#elif UNITY_ANDROID || UNITY_IOS
            mode = ResolveMobileStandalone();
#else
            mode = InteractionMode.Desktop;
#endif

            OseLog.Info($"[InteractionModeResolver] Resolved mode: {mode}");
            return mode;
        }

        // ── Editor ──

        private static InteractionMode ResolveEditor()
        {
            if (XRSettings.isDeviceActive)
            {
                OseLog.Info($"[InteractionModeResolver] Editor XR active. Device: '{XRSettings.loadedDeviceName}'");
                return InteractionMode.XR;
            }

            if (IsEditorXRSimulatorActive(out string simulatorSource))
            {
                OseLog.Info($"[InteractionModeResolver] Editor XR simulator active ({simulatorSource}).");
                return InteractionMode.XR;
            }

            return InteractionMode.Desktop;
        }

        private static bool IsEditorXRSimulatorActive(out string source)
        {
            var allMonos = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var mono in allMonos)
            {
                if (mono == null || !mono.enabled || !mono.gameObject.activeInHierarchy)
                    continue;

                var type = mono.GetType();
                string typeName = type.Name;
                string fullTypeName = type.FullName ?? string.Empty;

                // XRI simulator types can come from either the core toolkit (XRDeviceSimulator)
                // or the sample package (XRInteractionSimulator), so match by name/fullname.
                if (typeName is "XRDeviceSimulator" or "XRInteractionSimulator"
                    || fullTypeName == "UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRDeviceSimulator"
                    || fullTypeName == "UnityEngine.XR.Interaction.Toolkit.Samples.InteractionSimulator.XRInteractionSimulator")
                {
                    source = $"{typeName} on '{mono.gameObject.name}'";
                    return true;
                }
            }

            // Fallback: explicit simulator root object active in the scene.
            var simulatorRoot = GameObject.Find("XR Interaction Simulator");
            if (simulatorRoot != null && simulatorRoot.activeInHierarchy)
            {
                source = $"active GameObject '{simulatorRoot.name}'";
                return true;
            }

            source = string.Empty;
            return false;
        }

        // ── WebGL ──

        private static InteractionMode ResolveWebGL()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (WebGL_IsXRHeadset())
                return InteractionMode.XR;

            if (WebGL_IsMobileDevice())
                return InteractionMode.Mobile;
#endif
            return InteractionMode.Desktop;
        }

        // ── Mobile standalone (Android/iOS native, NOT WebGL) ──

        private static InteractionMode ResolveMobileStandalone()
        {
            if (XRSettings.isDeviceActive)
                return InteractionMode.XR;

            return InteractionMode.Mobile;
        }
    }
}
