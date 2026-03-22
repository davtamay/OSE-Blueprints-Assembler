using System;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Reusable two-anchor interaction: tap A → live line follows cursor → tap B → complete.
    /// Usable for measurement, cable routing, alignment checks, or any A-to-B interaction.
    ///
    /// <para>The visual is pluggable via <see cref="Config.VisualFactory"/>:
    /// pass a factory that returns a <see cref="MeasurementLineVisual"/> for measurement,
    /// a <see cref="CableLineVisual"/> for cables, or any future <see cref="IAnchorLineVisual"/>.</para>
    ///
    /// <para><b>Usage:</b></para>
    /// <list type="number">
    ///   <item>Create an instance with configuration.</item>
    ///   <item>Call <see cref="StartFromAnchor"/> when anchor A is confirmed.</item>
    ///   <item>Call <see cref="Tick"/> every frame to drive the live line + highlight.</item>
    ///   <item>Call <see cref="TryCompleteAtAnchor"/> when the user taps — returns true if near B.</item>
    ///   <item>Call <see cref="Cleanup"/> on step change or teardown.</item>
    /// </list>
    /// </summary>
    internal sealed class AnchorToAnchorInteraction
    {
        /// <summary>
        /// Factory signature: given (start, end) world positions, return a visual.
        /// </summary>
        public delegate IAnchorLineVisual VisualFactoryDelegate(Vector3 start, Vector3 end);

        /// <summary>Configuration for the interaction.</summary>
        public struct Config
        {
            /// <summary>World position of anchor A (start).</summary>
            public Vector3 AnchorA;
            /// <summary>World position of anchor B (end / target).</summary>
            public Vector3 AnchorB;
            /// <summary>Display unit for distance labels ("mm", "inches", "cm", "ft"). Ignored when visual has no label.</summary>
            public string DisplayUnit;
            /// <summary>Screen proximity threshold (px) for "near B" highlight.</summary>
            public float NearBScreenThreshold;
            /// <summary>
            /// Factory that creates the live tracking visual. Called once at <see cref="StartFromAnchor"/>.
            /// When null, falls back to a default <see cref="MeasurementLineVisual"/>.
            /// </summary>
            public VisualFactoryDelegate LiveVisualFactory;
            /// <summary>
            /// Factory that creates the final result visual. Called once at completion.
            /// When null, falls back to a default <see cref="MeasurementLineVisual"/>.
            /// </summary>
            public VisualFactoryDelegate ResultVisualFactory;
        }

        /// <summary>Result data passed to <see cref="Completed"/>.</summary>
        public readonly struct Result
        {
            public readonly Vector3 StartWorldPos;
            public readonly Vector3 EndWorldPos;
            public readonly float DistanceMeters;
            public readonly string FormattedLabel;

            public Result(Vector3 start, Vector3 end, float distance, string label)
            {
                StartWorldPos = start;
                EndWorldPos = end;
                DistanceMeters = distance;
                FormattedLabel = label;
            }
        }

        /// <summary>Fired when the user taps anchor B and the interaction completes.</summary>
        public event Action<Result> Completed;

        /// <summary>
        /// Fired every frame during tracking with (isNearB).
        /// Use this to highlight the end anchor marker in the host system.
        /// </summary>
        public event Action<bool> NearBChanged;

        private readonly Config _config;
        private IAnchorLineVisual _liveVisual;
        private IAnchorLineVisual _resultVisual;
        private bool _active;
        private bool _wasNearB;

        /// <summary>True while the interaction is active (between Start and Complete/Cleanup).</summary>
        public bool IsActive => _active;

        /// <summary>The final result visual, available after completion until Cleanup.</summary>
        public IAnchorLineVisual ResultVisual => _resultVisual;

        public AnchorToAnchorInteraction(Config config)
        {
            _config = config;
        }

        /// <summary>
        /// Begin the interaction. Call after anchor A is confirmed.
        /// Spawns a live visual from A that will track the cursor.
        /// </summary>
        public void StartFromAnchor()
        {
            Cleanup();

            if (_config.LiveVisualFactory != null)
                _liveVisual = _config.LiveVisualFactory(_config.AnchorA, _config.AnchorA);
            else
                _liveVisual = MeasurementLineVisual.Spawn(_config.AnchorA, _config.AnchorA, "");

            _active = true;
        }

        /// <summary>
        /// Call every frame while active. Moves the live visual endpoint to follow the pointer
        /// and fires <see cref="NearBChanged"/> when proximity to B changes.
        /// </summary>
        public void Tick()
        {
            if (!_active || _liveVisual == null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            if (!TryGetPointerScreenPos(out Vector2 screenPos))
                return;

            // Project pointer onto horizontal plane at anchor A height
            Vector3 lineEnd = _config.AnchorA;
            Ray ray = cam.ScreenPointToRay(screenPos);
            float planeY = _config.AnchorA.y;
            if (Mathf.Abs(ray.direction.y) > 0.001f)
            {
                float dist = (planeY - ray.origin.y) / ray.direction.y;
                if (dist > 0f)
                    lineEnd = ray.origin + ray.direction * dist;
            }

            _liveVisual.SetEndpoints(_config.AnchorA, lineEnd);

            // Live distance label (visual can no-op if it doesn't support labels)
            float distance = Vector3.Distance(_config.AnchorA, lineEnd);
            string unit = _config.DisplayUnit ?? "mm";
            _liveVisual.SetLabel(MeasurementLineVisual.FormatDistance(distance, unit));

            // Check proximity to B
            bool nearB = false;
            Vector3 bScreen = cam.WorldToScreenPoint(_config.AnchorB);
            if (bScreen.z > 0f)
            {
                float screenDist = Vector2.Distance(screenPos, new Vector2(bScreen.x, bScreen.y));
                nearB = screenDist < _config.NearBScreenThreshold;
            }

            if (nearB != _wasNearB)
            {
                _wasNearB = nearB;
                NearBChanged?.Invoke(nearB);
            }
        }

        /// <summary>
        /// Call when the user taps/clicks a target. If <paramref name="tapWorldPos"/> is
        /// close enough to anchor B (or <paramref name="forceComplete"/> is true),
        /// completes the interaction: destroys the live visual, spawns the final result visual,
        /// and fires <see cref="Completed"/>.
        /// </summary>
        /// <returns>True if the interaction completed.</returns>
        public bool TryCompleteAtAnchor(Vector3 tapWorldPos, bool forceComplete = false)
        {
            if (!_active)
                return false;

            if (!forceComplete)
            {
                Camera cam = Camera.main;
                if (cam == null)
                    return false;

                Vector3 bScreen = cam.WorldToScreenPoint(_config.AnchorB);
                Vector3 tapScreen = cam.WorldToScreenPoint(tapWorldPos);
                if (bScreen.z <= 0f || tapScreen.z <= 0f)
                    return false;

                float screenDist = Vector2.Distance(
                    new Vector2(bScreen.x, bScreen.y),
                    new Vector2(tapScreen.x, tapScreen.y));

                if (screenDist > _config.NearBScreenThreshold * 1.5f)
                    return false;
            }

            // Destroy live visual
            if (_liveVisual != null)
            {
                _liveVisual.Cleanup();
                _liveVisual = null;
            }

            // Spawn final result visual snapped to exact A → B
            Vector3 a = _config.AnchorA;
            Vector3 b = _config.AnchorB;
            float distance = Vector3.Distance(a, b);
            string unit = _config.DisplayUnit ?? "mm";
            string label = MeasurementLineVisual.FormatDistance(distance, unit);

            if (_config.ResultVisualFactory != null)
                _resultVisual = _config.ResultVisualFactory(a, b);
            else
                _resultVisual = MeasurementLineVisual.Spawn(a, b, label);

            _active = false;
            Completed?.Invoke(new Result(a, b, distance, label));
            return true;
        }

        /// <summary>
        /// Destroys all visuals and resets state. Safe to call multiple times.
        /// </summary>
        public void Cleanup()
        {
            _active = false;
            _wasNearB = false;

            if (_liveVisual != null)
            {
                _liveVisual.Cleanup();
                _liveVisual = null;
            }
            if (_resultVisual != null)
            {
                _resultVisual.Cleanup();
                _resultVisual = null;
            }
        }

        // ── Pointer helper ──

        private static bool TryGetPointerScreenPos(out Vector2 screenPos)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                screenPos = mouse.position.ReadValue();
                return true;
            }

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                screenPos = touch.primaryTouch.position.ReadValue();
                return true;
            }

            screenPos = Vector2.zero;
            return false;
        }
    }
}
