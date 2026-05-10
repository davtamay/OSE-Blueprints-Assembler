using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Floating presentation surface for <see cref="ToolTargetAuthoringWindow"/>'s
    /// part browser. State (pick request, hover, selection, search, view mode)
    /// lives on TTAW so SceneView ↔ list ↔ Hierarchy stay in sync — this window
    /// just calls TTAW.DrawPartBrowserPanel() and forwards the close-box to
    /// CancelPartPick. Closing via X button cancels the pick; ConfirmPartPick
    /// closes the window itself.
    /// </summary>
    public sealed class PartBrowserWindow : EditorWindow
    {
        private static PartBrowserWindow _instance;
        private ToolTargetAuthoringWindow _owner;

        public static void Show(ToolTargetAuthoringWindow owner)
        {
            if (owner == null) return;
            if (_instance == null)
            {
                _instance = CreateInstance<PartBrowserWindow>();
                _instance.titleContent = new GUIContent("Part Browser");
                _instance.minSize = new Vector2(580f, 460f);
            }
            _instance._owner = owner;
            _instance.ShowUtility();
            _instance.Focus();
        }

        public static void CloseIfOpen()
        {
            // Null _instance first so OnDestroy's CancelPartPick → CloseIfOpen
            // re-entry short-circuits instead of double-closing the same window.
            // Defer the actual Close() to the next editor tick — calling
            // Close() mid-OnGUI (e.g. from a button click handler that
            // confirms the pick) triggers Unity's "tried to begin rendering
            // while something else had not finished rendering" assertion.
            var inst = _instance;
            _instance = null;
            if (inst != null)
            {
                inst._owner = null;
                EditorApplication.delayCall += () => { if (inst != null) inst.Close(); };
            }
        }

        public static void RepaintIfOpen()
        {
            if (_instance != null) _instance.Repaint();
        }

        private void OnGUI()
        {
            if (_owner == null || !_owner.IsPartBrowserOpen)
            {
                // Defer Close() out of OnGUI — closing mid-frame triggers
                // Unity's "tried to begin rendering" assertion.
                var self = this;
                EditorApplication.delayCall += () => { if (self != null) self.Close(); };
                return;
            }
            _owner.DrawPartBrowserPanel(this);
        }

        private void OnDestroy()
        {
            // X-button close = cancel. Null _instance first so the
            // CancelPartPick → CloseIfOpen path doesn't try to re-close us
            // while Unity is already destroying this window.
            if (_instance == this) _instance = null;
            var owner = _owner;
            _owner = null;
            if (owner != null && owner.IsPartBrowserOpen)
                owner.CancelPartPick();
        }
    }
}
