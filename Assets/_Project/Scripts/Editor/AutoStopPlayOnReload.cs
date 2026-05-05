using UnityEditor;
using UnityEngine;

using OSE.Core;
namespace OSE.Editor
{
    /// <summary>
    /// Stops Play mode automatically when scripts recompile mid-Play.
    ///
    /// <para><b>Why this exists.</b> The runtime's session state lives in
    /// plain-class fields on <c>MachineSessionController</c>, registered in
    /// <c>ServiceRegistry</c> (also plain-class state). Domain reload —
    /// triggered when scripts compile during Play — wipes all of that. After
    /// reload, <c>AppBootstrap.Awake</c> creates a fresh, empty
    /// <c>MachineSessionController</c> with no loaded package, no active
    /// step, no completed steps. Meanwhile <c>PackagePartSpawner.OnEnable</c>
    /// re-adopts the surviving scene GameObjects and forces them
    /// <c>SetActive(false)</c> for "Play-mode reveal will turn the right ones
    /// back on" — but the reveal never runs because the session has nothing
    /// to reveal. Visible symptom: parts disappear, scene looks empty,
    /// trainee is "stuck" in Play but the experience is gutted. The user has
    /// hit this 5+ times under the diagnosis "parts mysteriously not showing
    /// at step N."</para>
    ///
    /// <para><b>The fix.</b> Force Play to exit before the reload completes.
    /// Next Enter Play hits a clean Awake → Bootstrap → session start →
    /// auto-resume from the persisted last-completed step. Standard Unity
    /// workflow for projects whose runtime state doesn't survive domain
    /// reload — much smaller than refactoring every controller for
    /// serializable state. Authoring iteration loop becomes:
    /// <list type="number">
    ///   <item>Edit script in IDE.</item>
    ///   <item>Save → Unity recompiles → Play exits cleanly.</item>
    ///   <item>Press Play → resume from where you left off.</item>
    /// </list></para>
    ///
    /// <para><b>Reverting.</b> Delete this file. The "fix" is purely
    /// defensive — no runtime consumer depends on it. Removing it brings
    /// back the broken-after-recompile behavior but doesn't break anything
    /// else.</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoStopPlayOnReload
    {
        static AutoStopPlayOnReload()
        {
            // beforeAssemblyReload fires immediately before Unity reloads the
            // C# domain. By then it's too late to halt the reload, but we
            // CAN exit Play first so the reload happens in Edit mode (where
            // the reset-to-empty-state is harmless and expected).
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        private static void OnBeforeAssemblyReload()
        {
            if (!EditorApplication.isPlaying) return;

            // Setting isPlaying = false during beforeAssemblyReload tells
            // Unity to exit Play. The reload itself still proceeds; by the
            // time scripts have finished recompiling and the new domain is
            // up, the editor is back in Edit mode with a clean session
            // boot waiting for the next Play press.
            OseLog.Info("[OSE] Script recompile detected during Play — exiting Play mode to avoid runtime state loss. Press Play again to resume from your last completed step.");
            EditorApplication.isPlaying = false;
        }
    }
}
