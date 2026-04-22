// TTAW.PoseUI.cs — Shared UI primitives for pose-source authoring.
// ──────────────────────────────────────────────────────────────────────────────
// Slice B (UX redesign). Consolidates the toggle styling used to switch between
// Start / Assembled / NO-TASK pose modes so Part and Group Inspectors look
// identical. Colors are fixed (not accent-driven): green means "staging /
// pre-placement", blue means "final / assembled", light-blue means "NO TASK
// presence", muted gray means "inactive selection". Keeps semantic meaning
// visible at a glance no matter which scope is being edited.
//
// See plan: C:\Users\davta\.claude\plans\rosy-soaring-metcalfe.md — Slice B.

using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Pose-source accent colors. Shared by Part + Group (and, later,
        // the unified cue callers) so the same concept reads the same color
        // everywhere. If you change these, update them in one place only.
        private static readonly Color PoseAccentStart     = new(0.30f, 0.78f, 0.36f);  // green — staging / pre-placement
        private static readonly Color PoseAccentAssembled = new(0.20f, 0.62f, 0.95f);  // blue  — final / assembled
        private static readonly Color PoseAccentNoTask    = new(0.70f, 0.88f, 1.00f);  // light blue — NO TASK visual-only presence
        private static readonly Color PoseAccentInactive  = new(0.55f, 0.55f, 0.58f);  // muted gray — inactive selection

        /// <summary>
        /// Returns a bold, accent-colored <see cref="GUIStyle"/> for a pose-
        /// source toggle button. Pass <paramref name="baseStyle"/> =
        /// <c>EditorStyles.miniButtonLeft/Mid/Right</c> for grouped rows, or
        /// <c>null</c> for a standalone button. The active state drives the
        /// text color; inactive always falls back to muted gray.
        /// </summary>
        /// <param name="active">True if this toggle represents the currently-
        /// selected pose source.</param>
        /// <param name="accentOn">Color to use when <paramref name="active"/>
        /// is true. Typically one of <see cref="PoseAccentStart"/>,
        /// <see cref="PoseAccentAssembled"/>, <see cref="PoseAccentNoTask"/>.</param>
        /// <param name="baseStyle">Optional base style (e.g.
        /// <c>EditorStyles.miniButtonLeft</c>). When null, uses
        /// <c>EditorStyles.miniButton</c>.</param>
        private static GUIStyle PoseToggleStyle(bool active, Color accentOn, GUIStyle baseStyle = null)
        {
            var baseline = baseStyle ?? EditorStyles.miniButton;
            return new GUIStyle(baseline)
            {
                fontStyle = FontStyle.Bold,
                normal    = { textColor = active ? accentOn : PoseAccentInactive },
                onNormal  = { textColor = active ? accentOn : PoseAccentInactive },
                focused   = { textColor = active ? accentOn : PoseAccentInactive },
                onFocused = { textColor = active ? accentOn : PoseAccentInactive },
            };
        }
    }
}
