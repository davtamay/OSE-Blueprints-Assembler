using UnityEngine;

namespace OSE.Runtime.Preview
{
    [CreateAssetMenu(
        fileName = "SceneContentPreviewProfile",
        menuName = "OSE/Preview/Scene Content Preview Profile")]
    public sealed class SceneContentPreviewProfile : ScriptableObject
    {
        [Header("Content Source")]
        [SerializeField] private bool _previewInEditMode = true;
        [SerializeField] private string _packageId = "tutorial_build";

        [Header("Step Preview")]
        [SerializeField, Min(1)] private int _previewStepSequenceIndex = 1;
        [SerializeField] private bool _advanceOnPlay = true;
        [SerializeField, Min(1)] private int _playModeStepSequenceIndex = 2;
        [SerializeField, Min(0f)] private float _playModeAdvanceDelay = 2.5f;

        public bool PreviewInEditMode => _previewInEditMode;

        public string PackageId => string.IsNullOrWhiteSpace(_packageId) ? string.Empty : _packageId.Trim();

        public int PreviewStepSequenceIndex => Mathf.Max(1, _previewStepSequenceIndex);

        public bool AdvanceOnPlay => _advanceOnPlay;

        public int PlayModeStepSequenceIndex => Mathf.Max(1, _playModeStepSequenceIndex);

        public float PlayModeAdvanceDelay => Mathf.Max(0f, _playModeAdvanceDelay);

        public static SceneContentPreviewProfile CreateBuiltInDefault()
        {
            SceneContentPreviewProfile profile = CreateInstance<SceneContentPreviewProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            return profile;
        }

        private void OnValidate()
        {
            _previewStepSequenceIndex = Mathf.Max(1, _previewStepSequenceIndex);
            _playModeStepSequenceIndex = Mathf.Max(1, _playModeStepSequenceIndex);
            _playModeAdvanceDelay = Mathf.Max(0f, _playModeAdvanceDelay);
        }
    }
}
