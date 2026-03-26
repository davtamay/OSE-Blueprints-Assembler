using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Semantic input intent produced by an IIntentProvider each frame.
    /// The orchestrator consumes this to drive the interaction state machine.
    /// Stack-allocated (struct) to avoid GC pressure.
    /// </summary>
    public readonly struct InteractionIntent
    {
        public enum Kind
        {
            None,
            Select,
            BeginDrag,
            ContinueDrag,
            EndDrag,
            Orbit,
            Pan,
            Zoom,
            Focus,
            ResetView,
            Inspect,
            Cancel
        }

        public Kind IntentKind { get; }
        public Vector2 ScreenPosition { get; }
        public Vector2 ScreenDelta { get; }
        public float ScrollDelta { get; }
        public float PinchDelta { get; }
        public GameObject HitTarget { get; }
        public bool IsOverUI { get; }

        public InteractionIntent(
            Kind intentKind,
            Vector2 screenPosition = default,
            Vector2 screenDelta = default,
            float scrollDelta = 0f,
            float pinchDelta = 0f,
            GameObject hitTarget = null,
            bool isOverUI = false)
        {
            IntentKind = intentKind;
            ScreenPosition = screenPosition;
            ScreenDelta = screenDelta;
            ScrollDelta = scrollDelta;
            PinchDelta = pinchDelta;
            HitTarget = hitTarget;
            IsOverUI = isOverUI;
        }

        public static InteractionIntent None => new(Kind.None);

        public bool IsNone => IntentKind == Kind.None;
        public bool IsCameraIntent => IntentKind is Kind.Orbit or Kind.Pan or Kind.Zoom;
        public bool IsPartIntent => IntentKind is Kind.Select or Kind.BeginDrag or Kind.ContinueDrag or Kind.EndDrag or Kind.Inspect;
    }
}
