using System;
using System.Collections.Generic;

namespace OSE.Interaction
{
    /// <summary>
    /// Maps <see cref="PreviewStyle"/> values to <see cref="IToolActionPreview"/> factories.
    /// Built-in previews are registered at static init time. Call <see cref="Register"/> to
    /// add new preview types without modifying this class (Open/Closed).
    /// </summary>
    public static class ToolActionPreviewFactory
    {
        private static readonly Dictionary<PreviewStyle, Func<IToolActionPreview>> _registry =
            new Dictionary<PreviewStyle, Func<IToolActionPreview>>
            {
                [PreviewStyle.Torque]      = () => new TorquePreview(),
                [PreviewStyle.Weld]        = () => new WeldPreview(),
                [PreviewStyle.Cut]         = () => new CutPreview(),
                [PreviewStyle.SquareCheck] = () => new SquareCheckPreview(),
            };

        /// <summary>
        /// Registers (or replaces) a factory for a given <see cref="PreviewStyle"/>.
        /// Call from a <c>[RuntimeInitializeOnLoadMethod]</c> or bootstrap to add new preview types.
        /// </summary>
        public static void Register(PreviewStyle style, Func<IToolActionPreview> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            _registry[style] = factory;
        }

        public static IToolActionPreview Create(string profile)
        {
            var style = ToolProfileRegistry.Get(profile).PreviewStyle;
            return _registry.TryGetValue(style, out var factory) ? factory() : new DefaultPreview();
        }
    }
}
