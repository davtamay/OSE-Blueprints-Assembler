using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEngine;

namespace OSE.Interaction
{
    /// <summary>
    /// Local transform snapshot used to seed an <see cref="IPartEffect"/>.
    /// </summary>
    public readonly struct PoseSnapshot
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        public PoseSnapshot(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }
    }

    /// <summary>
    /// Parameters a <see cref="PartEffectFactoryFn"/> needs to build an
    /// <see cref="IPartEffect"/>. Additive — new fields can be appended
    /// without breaking existing factories.
    /// </summary>
    public struct PartEffectBuildArgs
    {
        public Transform PartTransform;
        public Transform PreviewRoot;
        public PoseSnapshot Start;
        public PoseSnapshot End;

        /// <summary>Tool spatial metadata (tip/action axis). Optional; null for tool-less effects.</summary>
        public ToolPoseConfig ToolPose;

        /// <summary>
        /// Authored motion-shape payload from <see cref="ToolActionDefinition.interaction"/>.
        /// Null ≡ archetype's auto-derivation (lerp from poses).
        /// </summary>
        public ToolPartInteraction Payload;
    }

    /// <summary>
    /// Factory delegate producing an <see cref="IPartEffect"/> for a given archetype.
    /// Factories live wherever their concrete effect class can be instantiated —
    /// the bridge layer (OSE.UI.Root) for effects with internal access,
    /// OSE.Interaction for effects that are fully public.
    /// </summary>
    public delegate IPartEffect PartEffectFactoryFn(in PartEffectBuildArgs args);

    /// <summary>
    /// Central archetype → factory map. The bridge layer's part-effect resolver
    /// looks up the authored archetype (defaulting to <see cref="PartEffectArchetypes.Lerp"/>)
    /// and delegates effect construction here. Adding a new archetype =
    /// write one factory + register it in bootstrap; no other code changes.
    /// </summary>
    public static class PartEffectRegistry
    {
        private static readonly Dictionary<string, PartEffectFactoryFn> s_factories =
            new Dictionary<string, PartEffectFactoryFn>(StringComparer.Ordinal);

        public static void Register(string archetype, PartEffectFactoryFn factory)
        {
            if (string.IsNullOrEmpty(archetype))
                throw new ArgumentException("Archetype key must be non-empty.", nameof(archetype));
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));
            s_factories[archetype] = factory;
        }

        public static bool TryGet(string archetype, out PartEffectFactoryFn factory)
        {
            if (!string.IsNullOrEmpty(archetype) && s_factories.TryGetValue(archetype, out factory))
                return true;
            factory = null;
            return false;
        }

        public static IEnumerable<string> RegisteredArchetypes => s_factories.Keys;

        /// <summary>
        /// Convenience: resolve, with automatic fallback to <see cref="PartEffectArchetypes.Lerp"/>.
        /// Returns null only if "lerp" itself is unregistered (bootstrap failure).
        /// </summary>
        public static IPartEffect Build(string archetype, in PartEffectBuildArgs args)
        {
            if (TryGet(archetype, out var factory)) return factory(args);
            if (TryGet(PartEffectArchetypes.Lerp, out var fallback)) return fallback(args);
            return null;
        }
    }
}
