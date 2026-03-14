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
        public static event Action<MechanicsSceneVisualProfile> Changed;

        [Header("Visibility")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField] private bool _showGeometryPreview = true;
        [SerializeField] private bool _showUiPreview = true;

        [Header("Camera")]
        [SerializeField] private PreviewCameraSettings _camera = default;

        [Header("Floor")]
        [SerializeField] private PreviewObjectAppearance _floor = default;

        public bool PreviewInEditMode => _previewInEditMode;

        public bool ShowGeometryPreview => _showGeometryPreview;

        public bool ShowUiPreview => _showUiPreview;

        public PreviewCameraSettings Camera => _camera;

        public PreviewObjectAppearance Floor => _floor;

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

            NotifyChanged();
        }

        private void OnValidate()
        {
            NotifyChanged();
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
        }

        private void NotifyChanged()
        {
            Changed?.Invoke(this);
        }
    }
}
