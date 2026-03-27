using UnityEngine;

namespace OSE.Core
{
    /// <summary>
    /// Caches the <see cref="Renderer"/> array for a GameObject hierarchy.
    /// Automatically invalidates when children are added or removed.
    /// Attach via <see cref="MaterialHelper.GetRenderers"/> — never add manually.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RendererCache : MonoBehaviour
    {
        private Renderer[] _renderers;
        private bool _dirty = true;

        public Renderer[] Renderers
        {
            get
            {
                if (_dirty || _renderers == null)
                {
                    _renderers = GetComponentsInChildren<Renderer>(true);
                    _dirty = false;
                }
                return _renderers;
            }
        }

        public void Invalidate() => _dirty = true;

        private void OnTransformChildrenChanged() => _dirty = true;
    }
}
