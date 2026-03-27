using OSE.Core;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Caches the attached <see cref="UnityEngine.Camera"/> and registers it with
    /// <see cref="CameraUtil"/> so all call sites get the cached reference instead of
    /// calling <c>Camera.main</c> each frame. Place on the main camera GameObject in
    /// every assembly scene.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class CameraMainProvider : MonoBehaviour
    {
        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            CameraUtil.SetProvider(() => _camera);
        }

        private void OnDestroy()
        {
            CameraUtil.ClearProvider();
        }
    }
}
