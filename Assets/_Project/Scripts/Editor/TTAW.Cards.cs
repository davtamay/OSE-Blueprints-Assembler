// TTAW.Cards.cs — Foldable card primitive for the Inspector right pane.
// ──────────────────────────────────────────────────────────────────────────────
// Extracts the colored-left-rule + tinted-header + chevron-toggle pattern that
// previously lived inline in TTAW.Subassembly.cs (DrawActiveSubassemblyCard)
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
        /// Draws a foldable Inspector section with a tinted header, accent
        /// left-rule, chevron + title + optional count pill, and body content
        /// inside a helpBox when expanded. Expand state is persisted via
        /// EditorPrefs under <c>TTAW.card.{prefKey}</c> so it survives domain
        /// reload.
        /// </summary>
        /// <param name="title">Section title shown in the header.</param>
        /// <param name="prefKey">Unique scope-qualified key, e.g. "part/pose",
        /// "group/cues". Drives EditorPrefs persistence.</param>
        /// <param name="accent">Left-rule + header text color.</param>
        /// <param name="defaultExpanded">Initial state when prefKey has no
        /// stored value yet.</param>
        /// <param name="count">Optional count shown as a right-aligned pill
        /// in the header (e.g. cue count, part count). Null = no pill.</param>
        /// <param name="body">Callback drawing the section's body when
        /// expanded.</param>
        /// <returns>True when the card is currently expanded.</returns>
        private bool DrawCard(string title, string prefKey, Color accent,
            bool defaultExpanded, int? count, Action body)
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

            // Clickable header — whole rect toggles. Button drawn with GUIStyle.none
            // so the label + pill draw on top.
            if (GUI.Button(headerRect, GUIContent.none, GUIStyle.none))
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
            float pillWidth = count.HasValue ? 44f : 0f;
            var lblRect = new Rect(headerRect.x + 10f, headerRect.y,
                headerRect.width - 20f - pillWidth, headerRect.height);
            GUI.Label(lblRect, $"{chevron}  {title}", titleStyle);

            // Right-aligned count pill.
            if (count.HasValue)
            {
                var pillRect = new Rect(
                    headerRect.xMax - pillWidth - 6f,
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

            if (isOpen)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                try { body?.Invoke(); }
                finally { EditorGUILayout.EndVertical(); }
            }

            return isOpen;
        }

        /// <summary>
        /// Overload without a count pill. Thin convenience; all parameters
        /// pass through to the primary overload.
        /// </summary>
        private bool DrawCard(string title, string prefKey, Color accent,
            bool defaultExpanded, Action body)
            => DrawCard(title, prefKey, accent, defaultExpanded, null, body);
    }
}
