using System.Collections.Generic;
using OSE.App;
using OSE.Content;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Debug-only utility: fires every <c>requiredToolActions</c> entry on the
    /// currently-active step in sequence, paced by a configurable inter-action
    /// delay. Lets the author watch a multi-target tighten/weld step animate
    /// end-to-end without manually clicking each target sphere — useful when
    /// validating <c>interaction</c> payloads (lerp, thread_in) or comparing
    /// step 58 vs 59 (hand-tighten vs drill-tighten of the same 4 bolts).
    ///
    /// <para>Listens to <see cref="ToolActionCompleted"/> so each next action
    /// fires only after the previous animation finishes, plus the delay. If
    /// an action fails (target unmatched, not a Use step, no tool actions),
    /// the chain aborts with a console warning.</para>
    ///
    /// <para>Edit-mode invocation logs and bails — the runtime needs Play.</para>
    /// </summary>
    internal static class DebugAutoFireToolActions
    {
        private const string MenuPath_Fire     = "OSE/Debug/Auto-Fire All Tool Actions On Active Step";
        private const string MenuPath_Cancel   = "OSE/Debug/Cancel Auto-Fire";
        private const string PrefKey_DelayMs   = "OSE.Debug.AutoFireDelayMs";
        private const int    DefaultDelayMs    = 600;

        // Active chain state
        private static List<string> _pending;
        private static double _nextFireAt;
        private static bool _waitingForCompleted;
        private static string _stepId;

        [MenuItem(MenuPath_Fire, false, 200)]
        private static void FireFromMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[DebugAutoFire] Enter Play mode first — tool actions execute through the runtime.");
                return;
            }

            if (_pending != null && _pending.Count > 0)
            {
                Debug.LogWarning("[DebugAutoFire] A chain is already running. Cancel first via 'OSE/Debug/Cancel Auto-Fire'.");
                return;
            }

            if (!ServiceRegistry.TryGet<IMachineSessionController>(out var session) || session == null)
            {
                Debug.LogWarning("[DebugAutoFire] No active IMachineSessionController. Make sure a session is running.");
                return;
            }

            var step = session.AssemblyController?.StepController?.CurrentStepDefinition;
            if (step == null)
            {
                Debug.LogWarning("[DebugAutoFire] No active step.");
                return;
            }

            var actions = step.requiredToolActions;
            if (actions == null || actions.Length == 0)
            {
                Debug.LogWarning($"[DebugAutoFire] Step '{step.id}' has no requiredToolActions.");
                return;
            }

            _pending = new List<string>(actions.Length);
            foreach (var a in actions)
                if (a != null && !string.IsNullOrEmpty(a.targetId))
                    _pending.Add(a.targetId);

            if (_pending.Count == 0)
            {
                Debug.LogWarning($"[DebugAutoFire] Step '{step.id}' has requiredToolActions but none have a targetId.");
                _pending = null;
                return;
            }

            int delayMs = EditorPrefs.GetInt(PrefKey_DelayMs, DefaultDelayMs);
            _stepId = step.id;
            _nextFireAt = EditorApplication.timeSinceStartup; // fire first immediately
            _waitingForCompleted = false;

            RuntimeEventBus.Subscribe<ToolActionCompleted>(OnToolActionCompleted);
            EditorApplication.update += Tick;

            Debug.Log($"[DebugAutoFire] Started chain on step '{step.id}': {_pending.Count} target(s), {delayMs}ms inter-action delay.");
        }

        [MenuItem(MenuPath_Cancel, false, 201)]
        private static void CancelFromMenu()
        {
            if (_pending == null)
            {
                Debug.Log("[DebugAutoFire] No chain running.");
                return;
            }
            EndChain("canceled by menu");
        }

        [MenuItem(MenuPath_Fire, true)]
        private static bool ValidateFire() => Application.isPlaying;

        [MenuItem(MenuPath_Cancel, true)]
        private static bool ValidateCancel() => _pending != null;

        private static void Tick()
        {
            if (_pending == null) { Detach(); return; }
            if (!Application.isPlaying) { EndChain("exited play mode"); return; }
            if (_waitingForCompleted) return;

            if (EditorApplication.timeSinceStartup < _nextFireAt) return;

            if (_pending.Count == 0)
            {
                EndChain("finished");
                return;
            }

            string targetId = _pending[0];
            _pending.RemoveAt(0);

            if (!ServiceRegistry.TryGet<IPartActionBridge>(out var bridge) || bridge == null)
            {
                EndChain("IPartActionBridge unavailable");
                return;
            }

            bool ok = bridge.TryToolAction(targetId);
            if (!ok)
            {
                Debug.LogWarning($"[DebugAutoFire] TryToolAction('{targetId}') returned false. Continuing to next.");
                _nextFireAt = EditorApplication.timeSinceStartup + 0.05;
                return;
            }

            Debug.Log($"[DebugAutoFire] Fired '{targetId}'. Remaining: {_pending.Count}.");
            _waitingForCompleted = true;
        }

        private static void OnToolActionCompleted(ToolActionCompleted evt)
        {
            if (_pending == null) return;
            int delayMs = EditorPrefs.GetInt(PrefKey_DelayMs, DefaultDelayMs);
            _nextFireAt = EditorApplication.timeSinceStartup + (delayMs / 1000.0);
            _waitingForCompleted = false;
        }

        private static void EndChain(string reason)
        {
            Debug.Log($"[DebugAutoFire] Chain ended ({reason}). Step was '{_stepId}'.");
            _pending = null;
            _stepId = null;
            _waitingForCompleted = false;
            Detach();
        }

        private static void Detach()
        {
            EditorApplication.update -= Tick;
            RuntimeEventBus.Unsubscribe<ToolActionCompleted>(OnToolActionCompleted);
        }
    }
}
