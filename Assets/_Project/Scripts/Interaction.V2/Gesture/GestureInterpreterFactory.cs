namespace OSE.Interaction.V2
{
    /// <summary>
    /// Creates the appropriate <see cref="IGestureInterpreter"/> for the current platform.
    /// </summary>
    public static class GestureInterpreterFactory
    {
        public static IGestureInterpreter Create(InteractionMode mode)
        {
            switch (mode)
            {
                case InteractionMode.Mobile:
                    // Phase 5: MobileGestureInterpreter
                    return new DesktopGestureInterpreter();

                case InteractionMode.XR:
                    // Phase 6: XRGestureInterpreter
                    return new DesktopGestureInterpreter();

                case InteractionMode.Desktop:
                default:
                    return new DesktopGestureInterpreter();
            }
        }
    }
}
