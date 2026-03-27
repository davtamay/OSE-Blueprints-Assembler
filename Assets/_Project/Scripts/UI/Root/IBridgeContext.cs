using System;
using System.Collections.Generic;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime;
using UnityEngine;

namespace OSE.UI.Root
{
    // ── Focused sub-interfaces ─────────────────────────────────────────
    // Each extracted class depends on the narrowest possible sub-interface
    // rather than the full IBridgeContext. This enforces the Interface
    // Segregation Principle and makes dependency audits trivial.

    /// <summary>Core package/scene access — used by almost all consumers.</summary>
    internal interface ISpawnerContext
    {
        PackagePartSpawner Spawner { get; }
        PreviewSceneSetup Setup { get; }
        GameObject FindSpawnedPart(string partId);
        PartPlacementState GetPartState(string partId);
        Dictionary<string, PartPlacementState> PartStates { get; }
        void DestroyObject(UnityEngine.Object obj);
    }

    /// <summary>Part resolution, proxy queries, and movement lock checks.</summary>
    internal interface IPartQueryContext
    {
        bool IsSubassemblyProxy(GameObject target);
        bool ForEachProxyMember(GameObject proxy, Action<GameObject> action);
        GameObject NormalizeSelectablePlacementTarget(GameObject target);
        bool IsPartMovementLocked(string partId);
        bool IsToolModeLockedForParts();
        SubassemblyPlacementController SubassemblyController { get; }
    }

    /// <summary>Selection, drag, and hover state.</summary>
    internal interface IInteractionStateContext
    {
        SelectionService SelectionService { get; }
        DragController Drag { get; }
        bool IsDragging { get; }
        bool IsExternalControlEnabled { get; }
        GameObject GetHoveredPartFromXri();
        GameObject GetHoveredPartFromMouse();
        void ResetDragState();
    }

    /// <summary>Preview lifecycle and spawned preview list.</summary>
    internal interface IPreviewContext
    {
        List<GameObject> SpawnedPreviews { get; }
        PreviewSpawnManager PreviewManager { get; }
        void RefreshToolActionTargets();
        void HandlePlacementSucceeded(GameObject target);
    }

    /// <summary>Cross-handler references for orchestration between siblings.</summary>
    internal interface ISiblingAccessContext
    {
        PlaceStepHandler PlaceHandler { get; }
        UseStepHandler UseHandler { get; }
        ConnectStepHandler ConnectHandler { get; }
        PartVisualFeedbackManager VisualFeedback { get; }
        StepExecutionRouter Router { get; }
        ToolCursorManager CursorManager { get; }
        void ClearHintHighlight();
        void RestorePartVisual(GameObject part);
    }

    // ── Composite interface ────────────────────────────────────────────

    /// <summary>
    /// Full context surface — implemented by <see cref="PartInteractionBridge"/>.
    /// Consumers should depend on the narrowest sub-interface they need;
    /// only the bridge itself and legacy callers should reference this directly.
    /// </summary>
    internal interface IBridgeContext :
        ISpawnerContext,
        IPartQueryContext,
        IInteractionStateContext,
        IPreviewContext,
        ISiblingAccessContext
    { }
}
