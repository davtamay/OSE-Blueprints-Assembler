// TTAW.Shell.cs — UITK shell that hosts the existing IMGUI authoring UI.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 1 of the UX redesign. The window is now structurally a UI Toolkit
// EditorWindow: Unity calls CreateGUI() once after OnEnable, we build a
// VisualElement root, and we host the existing IMGUI UI inside a single
// full-window IMGUIContainer that calls DrawAuthoringIMGUI() each frame.
//
// Visually and functionally identical to the previous OnGUI-only window.
// This file exists so future phases can incrementally replace pieces of the
// IMGUI tree with native UITK panels (toolbar, navigator, inspector, etc.)
// without ever breaking the working IMGUI logic in the other partial files.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using UnityEditor;
using UnityEngine.UIElements;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── UITK shell ────────────────────────────────────────────────────────

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow = 1;

            // Single full-window IMGUIContainer that runs the existing OnGUI body
            // (now renamed DrawAuthoringIMGUI in TTAW.Layout.cs). All Event.current,
            // GUILayout, EditorGUILayout, position, and Repaint() calls work the
            // same inside an IMGUIContainer as they did when Unity called OnGUI
            // directly.
            var imguiHost = new IMGUIContainer(DrawAuthoringIMGUI)
            {
                name = "ttaw-imgui-host"
            };
            imguiHost.style.flexGrow = 1;
            root.Add(imguiHost);
        }
    }
}
