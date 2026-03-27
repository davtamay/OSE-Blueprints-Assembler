using System;
using System.Collections.Generic;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Provides shared state and services to classes extracted from
    /// <see cref="PartInteractionBridge"/>. Replaces the Func&lt;&gt; chains that
    /// previously threaded individual properties through constructor lambdas.
    ///
    /// Implemented by <see cref="PartInteractionBridge"/> itself so extracted
    /// classes can query sibling instances and bridge-owned state through a
    /// single typed reference instead of 10+ opaque delegates.
    /// </summary>
    internal interface IBridgeContext
    {
        // ── Component references ──

        PackagePartSpawner Spawner { get; }
        PreviewSceneSetup Setup { get; }
        SelectionService SelectionService { get; }

        // ── Extracted class accessors (lazily initialised; may be null during startup) ──

        DragController Drag { get; }
        PlaceStepHandler PlaceHandler { get; }
        UseStepHandler UseHandler { get; }
        ConnectStepHandler ConnectHandler { get; }
        PartVisualFeedbackManager VisualFeedback { get; }
        PreviewSpawnManager PreviewManager { get; }
        StepExecutionRouter Router { get; }
        ToolCursorManager CursorManager { get; }
        SubassemblyPlacementController SubassemblyController { get; }

        // ── Shared collections ──

        List<GameObject> SpawnedPreviews { get; }
        Dictionary<string, PartPlacementState> PartStates { get; }

        // ── Query methods ──

        GameObject FindSpawnedPart(string partId);
        bool IsSubassemblyProxy(GameObject target);
        bool ForEachProxyMember(GameObject proxy, Action<GameObject> action);
        GameObject NormalizeSelectablePlacementTarget(GameObject target);
        bool IsPartMovementLocked(string partId);
        bool IsToolModeLockedForParts();
        PartPlacementState GetPartState(string partId);
        bool IsDragging { get; }
        bool IsExternalControlEnabled { get; }
        GameObject GetHoveredPartFromXri();
        GameObject GetHoveredPartFromMouse();

        // ── Actions ──

        void ResetDragState();
        void ClearHintHighlight();
        void RestorePartVisual(GameObject part);
        void RefreshToolActionTargets();
        void DestroyObject(UnityEngine.Object obj);
        void HandlePlacementSucceeded(GameObject target);
    }
}
