using System;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// UI panel controllers bind presenter output onto a view tree.
    /// They never own gameplay truth or runtime progression state.
    /// </summary>
    public abstract class PanelControllerBase<TViewModel>
    {
        protected VisualElement RootElement { get; private set; }

        public bool IsBound => RootElement != null;

        public void Bind(VisualElement parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            Unbind();

            RootElement = CreateView();
            RootElement.name = PanelName;
            parent.Add(RootElement);
            CacheView(RootElement);
        }

        public void Unbind()
        {
            if (RootElement == null)
            {
                return;
            }

            OnUnbind();
            RootElement.RemoveFromHierarchy();
            RootElement = null;
        }

        public void Show(TViewModel viewModel)
        {
            EnsureBound();
            ApplyViewModel(viewModel);
            SetVisible(true);
        }

        public void Hide()
        {
            if (RootElement == null)
            {
                return;
            }

            SetVisible(false);
        }

        protected abstract string PanelName { get; }

        protected abstract VisualElement CreateView();

        protected abstract void CacheView(VisualElement root);

        protected abstract void ApplyViewModel(TViewModel viewModel);

        protected virtual void OnUnbind()
        {
        }

        protected void SetVisible(bool isVisible)
        {
            RootElement.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void EnsureBound()
        {
            if (RootElement == null)
            {
                throw new InvalidOperationException($"{PanelName} has not been bound to a UIDocument root.");
            }
        }
    }
}
