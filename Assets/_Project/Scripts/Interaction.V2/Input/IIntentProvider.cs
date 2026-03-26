namespace OSE.Interaction
{
    /// <summary>
    /// Translates raw platform input into semantic InteractionIntents.
    /// Implemented per platform (Desktop, Mobile). The orchestrator polls
    /// the active provider each frame.
    /// </summary>
    public interface IIntentProvider
    {
        /// <summary>
        /// Read current input state and return the highest-priority intent
        /// for this frame. Returns InteractionIntent.None when there is
        /// no meaningful input.
        /// </summary>
        InteractionIntent Poll();

        /// <summary>True while this provider is receiving input from its platform.</summary>
        bool IsActive { get; }
    }
}
