using OSE.Content;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.ActionType.cs — DrawActionTypePicker for tool-action tasks.
// Canonical action-type dropdown (drill, tighten, weld_pass, …) with a
// Custom… fallback text field for archetype-specific extensions.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        /// <summary>
        /// Canonical action types surfaced in the inspector dropdown. The set is
        /// intentionally a superset of the runtime profiles in
        /// <c>OSE.Interaction.ToolActionProfiles</c> plus a few author-facing synonyms
        /// (e.g. "drill" vs "tighten") that make intent obvious when reading JSON.
        /// Custom strings are always allowed via the "Custom…" entry.
        /// </summary>
        private static readonly string[] s_knownActionTypes = new[]
        {
            "tighten",
            "drill",
            "screw",
            "weld_pass",
            "grind_pass",
            "cut",
            "press",
            "strike",
            "measure",
            "clamp",
            "solder",
        };

        private const string kActionTypeNoneLabel   = "(none)";
        private const string kActionTypeCustomLabel = "Custom…";

        /// <summary>
        /// Renders the action-type picker for a <see cref="ToolActionDefinition"/>.
        /// Canonical values live in a dropdown; unknown/custom values reveal a text
        /// field beneath so non-canonical strings never silently collapse. Duplicate
        /// display (dropdown + text both echoing the same canonical value) is avoided —
        /// the text field only appears when the stored value is custom or "Custom…" is
        /// actively selected.
        /// </summary>
        private void DrawActionTypePicker(StepDefinition step, ToolActionDefinition taskAction)
        {
            if (taskAction == null) return;

            string current = taskAction.actionType ?? string.Empty;
            int knownIdx = -1;
            for (int i = 0; i < s_knownActionTypes.Length; i++)
                if (s_knownActionTypes[i] == current) { knownIdx = i; break; }

            // 0 = (none), 1..N = known types, N+1 = Custom…
            var options = new string[s_knownActionTypes.Length + 2];
            options[0] = kActionTypeNoneLabel;
            for (int i = 0; i < s_knownActionTypes.Length; i++) options[i + 1] = s_knownActionTypes[i];
            options[options.Length - 1] = kActionTypeCustomLabel;

            bool isCustom = knownIdx < 0 && !string.IsNullOrEmpty(current);
            int selectedIdx =
                string.IsNullOrEmpty(current) ? 0 :
                knownIdx >= 0                 ? knownIdx + 1 :
                                                options.Length - 1;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent("Action:",
                    "What kind of action the tool performs.\n" +
                    "Canonical values match runtime tool profiles and interaction archetypes.\n" +
                    "Choose Custom… to type an archetype-specific value."),
                EditorStyles.miniLabel, GUILayout.Width(54));

            EditorGUI.BeginChangeCheck();
            int newIdx = EditorGUILayout.Popup(selectedIdx, options);
            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck() && newIdx != selectedIdx)
            {
                if (newIdx == 0)                       taskAction.actionType = null;
                else if (newIdx == options.Length - 1) { /* Custom… — keep current value so the author can edit */ }
                else                                   taskAction.actionType = s_knownActionTypes[newIdx - 1];
                if (step != null) _dirtyStepIds.Add(step.id);
                Repaint();
            }

            // Text field only when editing a custom value — canonical picks don't echo.
            if (isCustom || selectedIdx == options.Length - 1)
            {
                EditorGUI.BeginChangeCheck();
                string newCustom = EditorGUILayout.TextField(" ", current);
                if (EditorGUI.EndChangeCheck() && newCustom != current)
                {
                    taskAction.actionType = string.IsNullOrWhiteSpace(newCustom) ? null : newCustom.Trim();
                    if (step != null) _dirtyStepIds.Add(step.id);
                    Repaint();
                }
            }
        }

        /// <summary>
        /// Looks up the tool's <see cref="ToolDefinition.primaryActionType"/> so new
        /// tool-target tasks inherit a sensible default verb from the tool instead of
        /// forcing the author to pick it again. Returns null when no default is authored.
        /// </summary>
        private string ResolvePrimaryActionType(string toolId)
        {
            if (string.IsNullOrEmpty(toolId) || _pkg?.tools == null) return null;
            foreach (var td in _pkg.tools)
                if (td != null && td.id == toolId)
                    return string.IsNullOrWhiteSpace(td.primaryActionType) ? null : td.primaryActionType.Trim();
            return null;
        }
    }
}
