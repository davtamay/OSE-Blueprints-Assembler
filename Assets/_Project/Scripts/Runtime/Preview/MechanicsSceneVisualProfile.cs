using System;
using UnityEngine;

namespace OSE.Runtime.Preview
{
    [Serializable]
    public struct PreviewCameraSettings
    {
        public Vector3 position;
        public Vector3 eulerAngles;
        public Color backgroundColor;

        public static PreviewCameraSettings Default => new PreviewCameraSettings
        {
            position = new Vector3(0f, 2.8f, -8.4f),
            eulerAngles = new Vector3(14f, 0f, 0f),
            backgroundColor = new Color(0.11f, 0.18f, 0.27f, 1f)
        };
    }

    [Serializable]
    public struct PreviewObjectAppearance
    {
        public Vector3 position;
        public Vector3 scale;
        public Color color;

        public static PreviewObjectAppearance Create(Vector3 position, Vector3 scale, Color color) =>
            new PreviewObjectAppearance
            {
                position = position,
                scale = scale,
                color = color
            };
    }

    [CreateAssetMenu(
        fileName = "MechanicsSceneVisualProfile",
        menuName = "OSE/Preview/Mechanics Scene Visual Profile")]
    public sealed class MechanicsSceneVisualProfile : ScriptableObject
    {
        [Header("Visibility")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField] private bool _showGeometryPreview = true;
        [SerializeField] private bool _showUiPreview = true;

        [Header("Camera")]
        [SerializeField] private PreviewCameraSettings _camera = default;

        [Header("Floor")]
        [SerializeField] private PreviewObjectAppearance _floor = default;

        [Header("Target Marker")]
        [SerializeField] private PreviewObjectAppearance _targetMarker = default;

        [Header("Sample Part")]
        [SerializeField] private PreviewObjectAppearance _samplePartStart = default;
        [SerializeField] private PreviewObjectAppearance _samplePartPlay = default;

        [Header("Play Mode Motion")]
        [SerializeField] private bool _animateSamplePartOnPlay = true;
        [SerializeField, Min(0f)] private float _playModeAdvanceDelay = 2.5f;

        public bool PreviewInEditMode => _previewInEditMode;

        public bool ShowGeometryPreview => _showGeometryPreview;

        public bool ShowUiPreview => _showUiPreview;

        public PreviewCameraSettings Camera => _camera;

        public PreviewObjectAppearance Floor => _floor;

        public PreviewObjectAppearance TargetMarker => _targetMarker;

        public PreviewObjectAppearance SamplePartStart => _samplePartStart;

        public PreviewObjectAppearance SamplePartPlay => _samplePartPlay;

        public bool AnimateSamplePartOnPlay => _animateSamplePartOnPlay;

        public float PlayModeAdvanceDelay => _playModeAdvanceDelay;

        public static MechanicsSceneVisualProfile CreateBuiltInDefault()
        {
            MechanicsSceneVisualProfile profile = CreateInstance<MechanicsSceneVisualProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.ApplyDefaults();
            return profile;
        }

        private void OnEnable()
        {
            if (_camera.position == Vector3.zero &&
                _camera.eulerAngles == Vector3.zero &&
                _camera.backgroundColor == default)
            {
                ApplyDefaults();
            }
        }

        private void OnValidate()
        {
            _playModeAdvanceDelay = Mathf.Max(0f, _playModeAdvanceDelay);
        }

        private void ApplyDefaults()
        {
            _previewInEditMode = true;
            _showGeometryPreview = true;
            _showUiPreview = true;
            _camera = PreviewCameraSettings.Default;
            _floor = PreviewObjectAppearance.Create(
                Vector3.zero,
                new Vector3(1.7f, 1f, 1.7f),
                new Color(0.20f, 0.24f, 0.28f, 1f));
            _targetMarker = PreviewObjectAppearance.Create(
                new Vector3(0f, 0.04f, 0f),
                new Vector3(0.9f, 0.04f, 0.9f),
                new Color(0.20f, 0.84f, 0.58f, 1f));
            _samplePartStart = PreviewObjectAppearance.Create(
                new Vector3(-2f, 0.55f, 0f),
                new Vector3(1.45f, 0.28f, 0.38f),
                new Color(0.94f, 0.55f, 0.18f, 1f));
            _samplePartPlay = PreviewObjectAppearance.Create(
                new Vector3(-0.6f, 0.55f, 0.15f),
                new Vector3(1.35f, 0.28f, 0.38f),
                _samplePartStart.color);
            _animateSamplePartOnPlay = true;
            _playModeAdvanceDelay = 2.5f;
        }
    }
}
