using OSE.Content;
using OSE.Core;
using UnityEditor;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.StepMutation.cs  —  Single source of truth for "I just changed which
// parts a step touches" post-mutation refresh.
//
// Background: previously every call site that mutated step.requiredPartIds /
// optionalPartIds / visualPartIds repeated the same six-call refresh sequence
// inline. Two paths drifted out of sync — CommitAddPart (task add) only ran
// BuildPartList while AddRequiredPartToStep (visibility add) ran the full
// dance, so adding via the Browser produced an invisible part task until the
// user did something that triggered a respawn. See
// `feedback_centralize_derived_data.md` in memory.
//
// This helper is the one place that knows the recipe. Every step-part
// membership mutation MUST call RefreshAfterStepPartChange(step) as its
// last step. Future fixes (e.g. "also call X here") land in one place and
// every site benefits.
//
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Suppression depth for batched mutations. When > 0, the refresh
        // body short-circuits — only the dirty mark + a deferred final
        // refresh fire. Lets multi-add callers run N mutations and pay for
        // exactly ONE rebuild + respawn at the end. Without this, looping
        // N adds runs N partial refreshes and the IMGUI rows render against
        // stale intermediate state (the "empty rows that fix on step-nav"
        // symptom).
        [System.NonSerialized] private int _stepRefreshSuppressionDepth;

        // Queue of work that depends on _parts being current. Mutators
        // that need to read PartEditState (CaptureCurrentPoseAsStepPose,
        // any future derived-data write) call QueueDerivedAction instead of
        // executing directly so we can defer execution until ONE BuildPartList
        // has run at the end of the refresh. Without this, mutators in a
        // multi-add batch had to call BuildPartList themselves before each
        // capture (N rebuilds of ~93 parts each); now BuildPartList runs once
        // and N captures drain against the resulting _parts state.
        [System.NonSerialized] private System.Collections.Generic.List<System.Action> _pendingDerivedActions
            = new System.Collections.Generic.List<System.Action>();

        /// <summary>
        /// Centralized post-mutation refresh after any step-part membership
        /// change (add/remove on <c>requiredPartIds</c>, <c>optionalPartIds</c>,
        /// <c>visualPartIds</c>, or role swaps). Marks the step dirty,
        /// invalidates derived caches, rebuilds editor lookup lists, respawns
        /// the scene, and syncs live mesh poses.
        ///
        /// Per `feedback_centralize_derived_data.md`: never scatter this
        /// recipe across callers — any new step-part mutation must end with
        /// a single call to this method so add-via-task and add-via-visibility
        /// never diverge again.
        ///
        /// Batched callers: wrap N successive mutations in
        /// <see cref="BatchStepMutations"/> to collapse N refreshes into one.
        /// </summary>
        /// <summary>
        /// Queues work that should run AFTER the next non-suppressed
        /// <see cref="RefreshAfterStepPartChange"/> rebuilds <c>_parts</c>.
        /// Mutators that need to read PartEditState (e.g. capture a part's
        /// current pose) use this instead of executing the dependent work
        /// inline — that way batch wrappers pay for one BuildPartList total,
        /// not one per mutator.
        /// </summary>
        private void QueueDerivedAction(System.Action action)
        {
            if (action != null) _pendingDerivedActions.Add(action);
        }

        private void RefreshAfterStepPartChange(StepDefinition step)
        {
            if (step == null) return;
            _dirtyStepIds.Add(step.id);

            // If we're inside a batch, skip the heavy work — the batch
            // wrapper runs a single refresh once it unwinds. The derived-
            // action queue stays intact across suppressed calls; it drains
            // here only on the outermost unwind.
            if (_stepRefreshSuppressionDepth > 0) return;

            // Phase 1: keep step.taskOrder in sync with the role arrays so
            // the renderer doesn't fall back to orphan reconciliation in
            // GetOrDeriveTaskOrder. Without this, AddVisualPartToStep et al
            // leave step.taskOrder stale; the ReorderableList caches its row
            // count from the stale array and blank rows render until the
            // author navigates away and back. markDirty: false because the
            // mutator already dirtied the step + we don't want reconcile to
            // double-count.
            ReconcileStepTaskOrder(step, markDirty: false);

            // Force the ReorderableList to rebuild its drawElementCallback
            // closure against the fresh order list. Lambdas capture locals
            // by reference, but each DrawTaskSequenceDragList call binds a
            // NEW `order` local; if the cached list isn't rebuilt, the
            // closure keeps drawing from the previous one (8 rows visible
            // via order.Count badge, but only N from the captured list
            // actually paint — the rest render blank).
            _taskSeqReorderListForStepId = null;

            InvalidateTaskOrderCache();

            // Phase 2: rebuild lookup caches. _parts is the one most derived
            // actions read from, so it MUST happen before phase 3.
            BuildPartList();
            BuildTargetList();

            // Phase 3: drain the deferred derived-action queue. Each entry
            // can now safely read _parts (just rebuilt). Errors in one
            // action don't poison the rest. Local copy + clear before
            // execution so re-entrant queues land in the next refresh, not
            // this one.
            if (_pendingDerivedActions.Count > 0)
            {
                var batch = _pendingDerivedActions;
                _pendingDerivedActions = new System.Collections.Generic.List<System.Action>();
                for (int i = 0; i < batch.Count; i++)
                {
                    try { batch[i]?.Invoke(); }
                    catch (System.Exception e)
                    {
                        OseLog.Warn($"[TTAW.StepMutation] Deferred action #{i} threw: {e}");
                    }
                }
            }

            // Phase 4: scene refresh — RespawnScene reads partStepSeq derived
            // from the (now-current) step state; SyncAllPartMeshesToActivePose
            // pushes _parts poses to live transforms. The captured stepPoses
            // from phase 3 are now visible to both.
            RespawnScene();
            SyncAllPartMeshesToActivePose();
            Repaint();
        }

        /// <summary>
        /// Suppresses per-mutation refreshes inside <paramref name="body"/>,
        /// then runs exactly ONE
        /// <see cref="RefreshAfterStepPartChange"/> against
        /// <paramref name="step"/> after the body completes. Use when adding
        /// multiple parts to a step in a loop — each individual add still
        /// dirties the step, but the heavy rebuild + respawn fires once.
        ///
        /// Re-entrant: nested batches still collapse to one refresh at the
        /// outermost unwind.
        /// </summary>
        private void BatchStepMutations(StepDefinition step, System.Action body)
        {
            if (body == null) return;
            _stepRefreshSuppressionDepth++;
            try { body(); }
            finally
            {
                _stepRefreshSuppressionDepth--;
                if (_stepRefreshSuppressionDepth == 0 && step != null)
                    RefreshAfterStepPartChange(step);
            }
        }
    }
}
