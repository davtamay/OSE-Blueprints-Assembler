// TTAW.Breadcrumb.cs — Persistent context strip at the top of the Inspector.
// ──────────────────────────────────────────────────────────────────────────────
// Slice D (UX redesign). Shows "Step 55 › Group › Carriage 1 (Y-Left)" with a
// family-colored left rule so the author always knows which step, which scope,
// and which entity they're editing — without scrolling past the context header
// that lives inside each dispatcher branch. The accent is driven by the step's
// ResolvedFamily (Place / Use / Connect / Confirm) so the crumb also signals
// what kind of interaction the step is.
//
// Color palette decisions:
//   Place   = blue    — "set something down precisely"
//   Use     = amber   — warm/active, aligns with the existing CueContextAccent
//                       (cues are most common on Use steps anyway)
//   Connect = purple  — distinct, uncommon enough to pop
//   Confirm = green   — "check / pass / move on"
//
// See plan: C:\Users\davta\.claude\plans\rosy-soaring-metcalfe.md — Slice D.

using System;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Family accent palette ────────────────────────────────────────────
        //
        // Four semantic colors for the four step families. Shared by the
        // breadcrumb left rule and (later) any other family-driven surface.
        // Kept separate from the pose-authoring palette (PoseAccent*) so the
        // two concepts don't compete for the same hues.

        private static readonly Color FamilyAccentPlace   = new(0.30f, 0.55f, 0.95f);  // blue
        private static readonly Color FamilyAccentUse     = new(0.95f, 0.65f, 0.20f);  // amber (matches CueContextAccent)
        private static readonly Color FamilyAccentConnect = new(0.70f, 0.45f, 0.90f);  // purple
        private static readonly Color FamilyAccentConfirm = new(0.35f, 0.80f, 0.45f);  // green
        private static readonly Color FamilyAccentUnknown = new(0.55f, 0.58f, 0.62f);  // muted gray

        private static Color FamilyAccentFor(StepDefinition step)
        {
            if (step == null) return FamilyAccentUnknown;
            switch (step.ResolvedFamily)
            {
                case StepFamily.Place:   return FamilyAccentPlace;
                case StepFamily.Use:     return FamilyAccentUse;
                case StepFamily.Connect: return FamilyAccentConnect;
                case StepFamily.Confirm: return FamilyAccentConfirm;
                default:                 return FamilyAccentUnknown;
            }
        }

        private static string FamilyLabelFor(StepDefinition step)
        {
            if (step == null) return "?";
            switch (step.ResolvedFamily)
            {
                case StepFamily.Place:   return "Place";
                case StepFamily.Use:     return "Use";
                case StepFamily.Connect: return "Connect";
                case StepFamily.Confirm: return "Confirm";
                default:                 return "?";
            }
        }

        // ── Breadcrumb renderer ──────────────────────────────────────────────

        /// <summary>
        /// Draws the Inspector's sticky context strip:
        /// <c>▐ Step 55 · Group · Carriage 1 (Y-Left)    [Confirm]</c>
        ///
        /// <para>Left rule is tinted with the step's family accent so the
        /// author sees at a glance whether this is a Place / Use / Connect /
        /// Confirm interaction. The scope chip says whether the task is a
        /// Part / Group / Tool / Target / Wire / Confirm. The entity label
        /// resolves the friendly display name when available, falling back to
        /// the id string.</para>
        ///
        /// <para>Keep the render cheap — this runs on every OnGUI frame when
        /// the Inspector is visible.</para>
        /// </summary>
        private void DrawInspectorBreadcrumb(StepDefinition step, TaskOrderEntry selEntry)
        {
            if (step == null) return;

            Color accent = FamilyAccentFor(step);

            // Strip rect. Fixed 22 px tall — tall enough to read comfortably,
            // short enough to feel like chrome rather than content.
            var row = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            var bg = new Color(accent.r * 0.18f + 0.05f,
                               accent.g * 0.18f + 0.05f,
                               accent.b * 0.18f + 0.05f, 1f);
            EditorGUI.DrawRect(row, bg);
            EditorGUI.DrawRect(new Rect(row.x, row.y, 4f, row.height), accent);

            // Resolve scope + entity name from the task entry.
            (string scopeWord, string entityName) = ResolveBreadcrumbScope(step, selEntry);

            // Left-side crumb: Step N · Scope · Entity
            int seq = step.sequenceIndex;
            string crumb = entityName != null
                ? $"Step {seq}  ·  {scopeWord}  ·  {TruncateMiddle(entityName, 44)}"
                : $"Step {seq}  ·  {scopeWord}";

            var crumbStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 11,
            };

            // Family badge on the right edge.
            string familyLabel = FamilyLabelFor(step);
            var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal    = { textColor = accent },
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize  = 9,
            };
            float badgeW = Mathf.Max(48f, badgeStyle.CalcSize(new GUIContent(familyLabel)).x + 10f);
            var badgeRect = new Rect(row.xMax - badgeW - 6f, row.y + 3f, badgeW, row.height - 6f);
            EditorGUI.DrawRect(badgeRect, new Color(accent.r, accent.g, accent.b, 0.22f));
            GUI.Label(badgeRect, familyLabel, badgeStyle);

            // Tooltip on hover shows step name + id for cross-reference.
            string tip = $"{step.name ?? step.id}\n" +
                         $"id: {step.id}\n" +
                         $"family: {step.ResolvedFamily}" +
                         (string.IsNullOrEmpty(step.profile) ? "" : $"\nprofile: {step.profile}");

            var crumbRect = new Rect(row.x + 10f, row.y,
                row.width - 20f - badgeW, row.height);
            GUI.Label(crumbRect, new GUIContent(crumb, tip), crumbStyle);
        }

        /// <summary>
        /// Resolves a scope word and friendly entity name for the breadcrumb.
        /// Part tasks whose id matches a partGroup render as "Group".
        /// </summary>
        private (string scopeWord, string entityName) ResolveBreadcrumbScope(
            StepDefinition step, TaskOrderEntry selEntry)
        {
            if (selEntry == null) return ("—", null);

            string kind = selEntry.kind ?? "";
            string id   = selEntry.id ?? "";

            if (string.Equals(kind, "part", StringComparison.Ordinal))
            {
                // A "part" entry can refer to a partGroup when the id
                // matches a group — match the dispatcher's own isGroupTask
                // probe so the crumb and the body agree on scope.
                if (_pkg != null && _pkg.TryGetPartGroup(id, out var sub) && sub != null)
                    return ("Group", sub.GetDisplayName());
                return ("Part", TaskInstanceId.ToPartId(id));
            }
            if (string.Equals(kind, "wire", StringComparison.Ordinal))
                return ("Wire", id);
            if (string.Equals(kind, "toolAction", StringComparison.Ordinal)
                || string.Equals(kind, "target", StringComparison.Ordinal))
                return ("Tool", id);
            if (string.Equals(kind, "confirm", StringComparison.Ordinal)
                || string.Equals(kind, "confirm_action", StringComparison.Ordinal))
                return ("Confirm", null);

            return (kind, id);
        }

        /// <summary>
        /// Truncates long identifiers at the middle with an ellipsis so the
        /// discriminating suffix stays visible. ("y_left_carriage_half_a"
        /// truncated to 20 → "y_left_ca…half_a", not "y_left_carriage_h…")
        /// </summary>
        private static string TruncateMiddle(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            int keep = (maxLen - 1) / 2;
            return s.Substring(0, keep) + "…" + s.Substring(s.Length - keep);
        }

        /// <summary>
        /// Draws a small accent-tinted pill (family label, tool summary, etc.)
        /// used in the Slice-ME-A consolidated step meta row. Auto-sizes to
        /// the label content + 12 px padding; fills behind the text at 22 %
        /// accent alpha and renders the text in the full accent color.
        /// </summary>
        private static void DrawStepMetaPill(string label, Color accent, string tooltip)
        {
            var style = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = accent },
                fontSize  = 10,
            };
            var content = new GUIContent(label, tooltip);
            float w = style.CalcSize(content).x + 14f;
            var rect = GUILayoutUtility.GetRect(w, 18f, GUILayout.Width(w), GUILayout.Height(18f));
            EditorGUI.DrawRect(rect, new Color(accent.r, accent.g, accent.b, 0.22f));
            GUI.Label(rect, content, style);
            GUILayout.Space(4);
        }
    }
}
