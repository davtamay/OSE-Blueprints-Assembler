// TTAW.Cards.cs — Foldable card primitive for the Inspector right pane.
// ──────────────────────────────────────────────────────────────────────────────
// Extracts the colored-left-rule + tinted-header + chevron-toggle pattern that
// previously lived inline in TTAW.PartGroup.cs (DrawActivePartGroupCard)
// plus the per-foldout open-state tracking of TTAW.CueContext._cueContextOpenKeys,
// and persists expand/collapse in EditorPrefs so state survives domain reload.
//
// One DrawCard call wraps any existing draw method without changing its
// internals — Slice A (UX redesign) uses this to fold every Inspector section
// under a uniform chevron header, collapse rarely-edited blocks by default,
// and promote the Animation & Effect Cues section directly under the Pose
// block. See C:\Users\davta\.claude\plans\rosy-soaring-metcalfe.md.

using System;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Muted gray accent for neutral cards (Pose, Model, Ownership, Build
        // Steps, Meta). Cues reuses CueContextAccent (orange); group-scoped
        // PARTS card reuses SubAccent (cyan).
        private static readonly Color CardAccentNeutral = new(0.55f, 0.58f, 0.65f);

        /// <summary>
        /// Draws a foldable section with a tinted header, accent left-rule,
        /// chevron + title + optional count pill + optional "+" add-button,
        /// and body content inside a helpBox when expanded. Expand state is
        /// persisted via EditorPrefs under <c>TTAW.card.{prefKey}</c> so it
        /// survives domain reload.
        /// </summary>
        /// <param name="title">Section title shown in the header.</param>
        /// <param name="prefKey">Unique scope-qualified key, e.g. "part/pose",
        /// "canvas/task-sequence". Drives EditorPrefs persistence.</param>
        /// <param name="accent">Left-rule + header text color.</param>
        /// <param name="defaultExpanded">Initial state when prefKey has no
        /// stored value yet.</param>
        /// <param name="count">Optional count shown as a right-aligned pill
        /// in the header (e.g. cue count, part count). Null = no pill.</param>
        /// <param name="onAddClick">Optional callback invoked when the "+"
        /// button in the header is clicked. Null = no add button. The button
        /// click does NOT toggle the card's expand state.</param>
        /// <param name="body">Callback drawing the section's body when
        /// expanded.</param>
        /// <returns>True when the card is currently expanded.</returns>
        private bool DrawCard(string title, string prefKey, Color accent,
            bool defaultExpanded, int? count, Action onAddClick, Action body)
        {
            string fullKey = "TTAW.card." + prefKey;
            bool isOpen = EditorPrefs.GetBool(fullKey, defaultExpanded);

            EditorGUILayout.Space(2);

            // Header strip — tinted bg + 4px accent left-rule.
            var headerRect = GUILayoutUtility.GetRect(0, 22f, GUILayout.ExpandWidth(true));
            var bgColor = new Color(
                accent.r * 0.20f + 0.06f,
                accent.g * 0.20f + 0.06f,
                accent.b * 0.20f + 0.06f,
                1f);
            EditorGUI.DrawRect(headerRect, bgColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), accent);

            // Compute sub-rects up-front so the toggle-click region excludes
            // the "+" hit area (clicking + shouldn't also toggle the card).
            const float addBtnWidth = 22f;
            const float addBtnGap   = 3f;
            float pillWidth = count.HasValue ? 44f : 0f;
            float pillGap   = count.HasValue ? 4f  : 0f;

            Rect addBtnRect = default;
            if (onAddClick != null)
            {
                addBtnRect = new Rect(
                    headerRect.xMax - addBtnWidth - 4f,
                    headerRect.y + 2f,
                    addBtnWidth,
                    headerRect.height - 4f);
            }
            float rightReserve = (onAddClick != null ? addBtnWidth + addBtnGap : 0f)
                                + pillWidth + pillGap + 6f;
            Rect toggleRect = new Rect(headerRect.x, headerRect.y,
                headerRect.width - rightReserve, headerRect.height);

            // Toggle: click on the title area (left of pill / + button).
            if (GUI.Button(toggleRect, GUIContent.none, GUIStyle.none))
            {
                isOpen = !isOpen;
                EditorPrefs.SetBool(fullKey, isOpen);
                Event.current.Use();
            }

            // Title with chevron — accent-colored bold label.
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal    = { textColor = accent },
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
            };
            string chevron = isOpen ? "▼" : "▶";
            var lblRect = new Rect(headerRect.x + 10f, headerRect.y,
                toggleRect.width - 10f, headerRect.height);
            GUI.Label(lblRect, $"{chevron}  {title}", titleStyle);

            // Right-aligned count pill (sits left of the + button when both
            // are present).
            if (count.HasValue)
            {
                float pillXMax = (onAddClick != null)
                    ? addBtnRect.xMin - addBtnGap
                    : headerRect.xMax - 6f;
                var pillRect = new Rect(
                    pillXMax - pillWidth,
                    headerRect.y + (headerRect.height - 14f) * 0.5f,
                    pillWidth,
                    14f);
                EditorGUI.DrawRect(pillRect, new Color(accent.r, accent.g, accent.b, 0.25f));
                var pillStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal    = { textColor = accent },
                    fontSize  = 9,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };
                GUI.Label(pillRect, count.Value.ToString(), pillStyle);
            }

            // "+" add-button on the far right.
            if (onAddClick != null)
            {
                var addStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = accent },
                };
                if (GUI.Button(addBtnRect, new GUIContent("+", "Add"), addStyle))
                {
                    onAddClick();
                    Event.current.Use();
                }
            }

            if (isOpen)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                try { body?.Invoke(); }
                finally { EditorGUILayout.EndVertical(); }
            }

            return isOpen;
        }

        /// <summary>
        /// Overload with count pill but no "+" button.
        /// </summary>
        private bool DrawCard(string title, string prefKey, Color accent,
            bool defaultExpanded, int? count, Action body)
            => DrawCard(title, prefKey, accent, defaultExpanded, count, null, body);

        /// <summary>
        /// Overload with neither count pill nor "+" button.
        /// </summary>
        private bool DrawCard(string title, string prefKey, Color accent,
            bool defaultExpanded, Action body)
            => DrawCard(title, prefKey, accent, defaultExpanded, null, null, body);
    }
}
