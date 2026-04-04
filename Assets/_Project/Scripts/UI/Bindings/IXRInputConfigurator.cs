using UnityEngine.InputSystem;

namespace OSE.UI.Bindings
{
    /// <summary>
    /// Abstracts XR UI input module configuration so <see cref="UIDocumentBootstrap"/>
    /// is not directly coupled to the XRI SDK input pipeline.
    /// Swap the implementation to support non-XRI input systems (e.g. flat-screen mouse-only).
    /// </summary>
    internal interface IXRInputConfigurator
    {
        /// <summary>
        /// Attempts to bind XR UI input actions from the supplied asset.
        /// Returns true when configuration is complete and no further calls are needed.
        /// </summary>
        bool TryConfigure(InputActionAsset inputActions);
    }
}
