// TTAW.Helpers.cs — Clipboard-aware field helpers: Vector3FieldClip, FloatFieldClip,
//                   FormatVec3, TryParseVec3, TryParseFloat, and the pending-paste dictionaries.
// ──────────────────────────────────────────────────────────────────────────────────────────
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.
// ──────────────────────────────────────────────────────────────────────────────────────────
//
// Unity's EditorGUILayout.Vector3Field allows per-component text copy/paste
// (the usual Ctrl+C / Ctrl+V inside an active text cell), but provides no
// single-click way to copy or paste an entire 3-value tuple. These helpers
// wrap Vector3Field / FloatField and add a right-click context menu with
// Copy / Paste (and per-component Copy X/Y/Z for vectors).
//
// Clipboard format for vectors: "x, y, z" using round-trip "R" formatting.
// Parser also accepts "Vector3(x,y,z)", "(x,y,z)", whitespace-separated,
// and semicolon-separated variants so values can be pasted from logs,
// JSON fragments, or other editors without reformatting.
//
// GenericMenu callbacks fire after the current OnGUI pass, so a paste
// cannot mutate the field's `out` value on the same frame. Instead we
// stash the parsed value in a dictionary keyed by the field's stable
// GUIUtility control ID and apply it on the next draw.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime.Preview;
using OSE.UI.Root;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        private static readonly Dictionary<int, Vector3> _pendingVec3Paste  = new Dictionary<int, Vector3>();
        private static readonly Dictionary<int, float>   _pendingFloatPaste = new Dictionary<int, float>();

        private static string FormatVec3(Vector3 v)
            => $"{v.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"{v.y.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"{v.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}";

        private static bool TryParseVec3(string s, out Vector3 v)
        {
            v = default;
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Strip common wrappers: "Vector3(", trailing ")", "(", "f" suffixes.
            string cleaned = s.Trim();
            if (cleaned.StartsWith("Vector3", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(7);
            cleaned = cleaned.Trim().TrimStart('(', '[', '{').TrimEnd(')', ']', '}');

            // Split on comma, semicolon, or whitespace — any combo works.
            var parts = cleaned.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' },
                                      StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(parts[0].TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, ci, out float x)) return false;
            if (!float.TryParse(parts[1].TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, ci, out float y)) return false;
            if (!float.TryParse(parts[2].TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, ci, out float z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        private static bool TryParseFloat(string s, out float f)
        {
            f = 0f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(s.Trim().TrimEnd('f', 'F'), System.Globalization.NumberStyles.Float, ci, out f);
        }

        /// <summary>
        /// Drop-in replacement for <see cref="EditorGUILayout.Vector3Field(string, Vector3)"/>
        /// that adds a right-click context menu with Copy / Paste / Copy X|Y|Z.
        /// </summary>
        private static Vector3 Vector3FieldClip(string label, Vector3 value)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);

            // Apply any paste queued by the previous frame before drawing, so the
            // field reflects the new value immediately and BeginChangeCheck fires.
            if (_pendingVec3Paste.TryGetValue(id, out var pasted))
            {
                _pendingVec3Paste.Remove(id);
                value = pasted;
                GUI.changed = true;
            }

            // Always draw the label on its own line above the X/Y/Z row, then
            // split remaining width equally between three FloatFields with
            // tiny sublabels. This keeps every component visible no matter
            // how narrow the inspector pane gets — Unity's stock Vector3Field
            // clips Z below ~285px because its component widths are fixed.
            float lineH = EditorGUIUtility.singleLineHeight;
            bool hasLabel = !string.IsNullOrEmpty(label);
            float totalH  = hasLabel ? lineH * 2f : lineH;
            Rect rowRect  = EditorGUILayout.GetControlRect(true, totalH);

            Event e = Event.current;
            bool showMenu = e.type == EventType.ContextClick && rowRect.Contains(e.mousePosition);
            if (showMenu) e.Use();

            Rect fieldsRect;
            if (hasLabel)
            {
                var labelRect = new Rect(rowRect.x, rowRect.y, rowRect.width, lineH);
                GUI.Label(labelRect, label, EditorStyles.label);
                fieldsRect = new Rect(rowRect.x, rowRect.y + lineH, rowRect.width, lineH);
            }
            else
            {
                fieldsRect = new Rect(rowRect.x, rowRect.y, rowRect.width, lineH);
            }

            float gap  = 4f;
            float colW = Mathf.Floor((fieldsRect.width - gap * 2f) / 3f);
            float prevLabelW = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 14f;
            var rx = new Rect(fieldsRect.x,                          fieldsRect.y, colW, lineH);
            var ry = new Rect(fieldsRect.x + colW + gap,             fieldsRect.y, colW, lineH);
            var rz = new Rect(fieldsRect.x + (colW + gap) * 2f,      fieldsRect.y,
                              fieldsRect.xMax - (fieldsRect.x + (colW + gap) * 2f), lineH);
            float nx = EditorGUI.FloatField(rx, "X", value.x);
            float ny = EditorGUI.FloatField(ry, "Y", value.y);
            float nz = EditorGUI.FloatField(rz, "Z", value.z);
            EditorGUIUtility.labelWidth = prevLabelW;
            Vector3 result = new Vector3(nx, ny, nz);

            if (showMenu)
            {
                Vector3 captured = result;
                int capturedId = id;
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = FormatVec3(captured);
                });
                bool canPaste = TryParseVec3(EditorGUIUtility.systemCopyBuffer, out Vector3 clipboardVec);
                if (canPaste)
                {
                    menu.AddItem(new GUIContent($"Paste  ({FormatVec3(clipboardVec)})"), false, () =>
                    {
                        _pendingVec3Paste[capturedId] = clipboardVec;
                        if (EditorWindow.focusedWindow != null)
                            EditorWindow.focusedWindow.Repaint();
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Paste"));
                }
                menu.AddSeparator("");
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                menu.AddItem(new GUIContent("Copy X"), false, () =>
                    EditorGUIUtility.systemCopyBuffer = captured.x.ToString("R", ci));
                menu.AddItem(new GUIContent("Copy Y"), false, () =>
                    EditorGUIUtility.systemCopyBuffer = captured.y.ToString("R", ci));
                menu.AddItem(new GUIContent("Copy Z"), false, () =>
                    EditorGUIUtility.systemCopyBuffer = captured.z.ToString("R", ci));
                menu.ShowAsContext();
            }

            return result;
        }

        /// <summary>
        /// Drop-in replacement for <see cref="EditorGUILayout.FloatField(string, float)"/>
        /// that adds a right-click context menu with Copy / Paste.
        /// </summary>
        private static float FloatFieldClip(string label, float value)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);

            if (_pendingFloatPaste.TryGetValue(id, out var pasted))
            {
                _pendingFloatPaste.Remove(id);
                value = pasted;
                GUI.changed = true;
            }

            Rect rowRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);

            Event e = Event.current;
            bool showMenu = e.type == EventType.ContextClick && rowRect.Contains(e.mousePosition);
            if (showMenu) e.Use();

            float result = EditorGUI.FloatField(rowRect, label, value);

            if (showMenu)
            {
                float captured = result;
                int capturedId = id;
                var menu = new GenericMenu();
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                menu.AddItem(new GUIContent("Copy"), false, () =>
                    EditorGUIUtility.systemCopyBuffer = captured.ToString("R", ci));
                bool canPaste = TryParseFloat(EditorGUIUtility.systemCopyBuffer, out float clipboardF);
                if (canPaste)
                {
                    menu.AddItem(new GUIContent($"Paste  ({clipboardF.ToString("R", ci)})"), false, () =>
                    {
                        _pendingFloatPaste[capturedId] = clipboardF;
                        if (EditorWindow.focusedWindow != null)
                            EditorWindow.focusedWindow.Repaint();
                    });
                }
                else
                {
                    menu.AddDisabledItem(new GUIContent("Paste"));
                }
                menu.ShowAsContext();
            }

            return result;
        }
    }
}
