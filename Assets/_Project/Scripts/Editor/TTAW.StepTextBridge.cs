using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEditor;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.StepTextBridge.cs  —  internal accessors used by StepTextAuthoringWindow.
//
// The text-editing window is a separate EditorWindow but shares TTAW's loaded
// package + dirty-tracking sets so a single Save flushes both spatial and
// textual edits in one pass. Rather than expose every private field, this
// partial gives the text window a narrow seam: read-only access to the step
// and hint catalogs, mutation hooks that mark the right dirty set, and a
// trigger to flush via TTAW's existing WriteJson path.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        internal string CurrentPackageId => _pkgId;
        internal MachinePackageDefinition CurrentPackage => _pkg;

        /// <summary>
        /// The step id TTAW is currently focused on (the step header card row),
        /// or null when TTAW is in "All Steps" mode (filter index 0). Used by
        /// StepTextAuthoringWindow to mirror TTAW's selection so the text
        /// editor always shows whichever step the author has open.
        /// </summary>
        internal string SelectedStepId
        {
            get
            {
                if (_stepIds == null) return null;
                if (_stepFilterIdx <= 0 || _stepFilterIdx >= _stepIds.Length) return null;
                return _stepIds[_stepFilterIdx];
            }
        }

        internal StepDefinition GetStepById(string stepId)
        {
            if (_pkg?.steps == null || string.IsNullOrEmpty(stepId)) return null;
            foreach (var s in _pkg.steps) if (s != null && s.id == stepId) return s;
            return null;
        }

        internal HintDefinition GetHintById(string hintId)
        {
            if (_pkg?.hints == null || string.IsNullOrEmpty(hintId)) return null;
            foreach (var h in _pkg.hints) if (h != null && h.id == hintId) return h;
            return null;
        }

        internal IReadOnlyList<HintDefinition> GetAllHints()
        {
            if (_pkg?.hints == null) return System.Array.Empty<HintDefinition>();
            return _pkg.hints;
        }

        /// <summary>Marks a step's TEXT payloads dirty so WriteJson re-emits guidance/validation/feedback/reinforcement.</summary>
        internal void MarkStepTextDirty(string stepId)
        {
            if (string.IsNullOrEmpty(stepId)) return;
            _dirtyStepIds.Add(stepId);
            Repaint();
        }

        /// <summary>Marks an existing hint's fields dirty so WriteJson re-emits its title / message / scoping.</summary>
        internal void MarkHintDirty(string hintId)
        {
            if (string.IsNullOrEmpty(hintId)) return;
            _dirtyHintIds.Add(hintId);
            Repaint();
        }

        /// <summary>
        /// Registers a brand-new hint with the open TTAW window. The hint is
        /// staged in <c>_newHintDefs</c> and will be appended to the right
        /// shard (<c>shared.json</c> or the assembly file owning its scope id)
        /// on the next save. Returns false when the id collides with an
        /// existing hint.
        /// </summary>
        internal bool RegisterNewHint(HintDefinition hint)
        {
            if (hint == null || string.IsNullOrEmpty(hint.id)) return false;
            if (GetHintById(hint.id) != null)
            {
                OseLog.Warn($"[StepTextAuthoring] RegisterNewHint: id '{hint.id}' already exists.");
                return false;
            }
            _newHintDefs.Add(hint);
            // Mirror immediately so editor UIs see the new hint without waiting for save.
            var existing = _pkg.hints != null ? new List<HintDefinition>(_pkg.hints) : new List<HintDefinition>();
            existing.Add(hint);
            _pkg.hints = existing.ToArray();
            Repaint();
            return true;
        }

        /// <summary>Flushes all dirty edits via TTAW's existing WriteJson pipeline.</summary>
        internal void FlushTextEdits() => WriteJson();
    }
}
