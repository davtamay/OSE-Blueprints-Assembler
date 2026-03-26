using UnityEngine;

namespace OSE.Runtime.Session
{
    /// <summary>
    /// Extension point for tool-specific animations and visual behaviors.
    /// Implement this interface on a MonoBehaviour and attach it to the tool
    /// preview prefab (or instantiate dynamically) to add custom animations
    /// like ratchet motion, tape extending, dial readouts, etc.
    ///
    /// The system discovers animators via GetComponent on the tool preview
    /// and calls lifecycle methods automatically. Multiple animators can
    /// coexist on the same tool for composable effects.
    ///
    /// STATUS: Interface defined — runtime wiring not yet implemented.
    /// Implement the hooks in PartInteractionBridge when ready.
    /// </summary>
    public interface IToolAnimator
    {
        /// <summary>
        /// Called once when the tool preview is spawned and ready.
        /// Use for initialization (cache references, set initial pose).
        /// </summary>
        void OnToolEquipped(GameObject toolPreview);

        /// <summary>
        /// Called when the tool enters "ready" state (near a valid target).
        /// Use for anticipation animations (e.g. wrench slightly opens,
        /// tape measure hook wiggles, grinder disc starts spinning).
        /// </summary>
        void OnReadyStateEnter(Vector3 targetWorldPos);

        /// <summary>
        /// Called when the tool leaves "ready" state (moved away from target).
        /// Use to revert anticipation animations back to idle pose.
        /// </summary>
        void OnReadyStateExit();

        /// <summary>
        /// Called when a tool action executes successfully on a target.
        /// Use for the main usage animation (ratchet clicks, tape extends
        /// to measurement point, welder arcs, crimper closes).
        /// </summary>
        /// <param name="targetWorldPos">World position of the action target.</param>
        /// <param name="actionProgress">0..1 normalized progress (for multi-count actions).</param>
        /// <param name="isComplete">True if this was the final action (step will complete).</param>
        void OnActionExecuted(Vector3 targetWorldPos, float actionProgress, bool isComplete);

        /// <summary>
        /// Called when the tool is unequipped or the preview is destroyed.
        /// Use for cleanup (stop coroutines, release pooled objects).
        /// </summary>
        void OnToolUnequipped();
    }
}
