using OSE.UI.Root;
using UnityEditor;

namespace OSE.Editor
{
    /// <summary>
    /// Edit-time companion to <see cref="PartEffectBootstrap"/>. The runtime
    /// bootstrap uses <c>RuntimeInitializeOnLoadMethod</c> which only fires on
    /// Play-mode entry — edit-mode tools (TTAW's ▶ Preview in scene button)
    /// need the registry populated immediately after domain reload. This hook
    /// calls the same idempotent registration path.
    /// </summary>
    [InitializeOnLoad]
    internal static class PartEffectBootstrapEditor
    {
        static PartEffectBootstrapEditor() => PartEffectBootstrap.Register();
    }
}
