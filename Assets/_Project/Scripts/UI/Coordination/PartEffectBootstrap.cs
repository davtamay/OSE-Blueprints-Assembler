using OSE.Interaction;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Registers built-in bridge-layer <see cref="IPartEffect"/> factories with the
    /// <see cref="PartEffectRegistry"/>. Lives in OSE.UI.Root because
    /// <see cref="LerpPosePartEffect"/> is internal to this assembly — the registrar
    /// closes over its constructor so callers elsewhere can build one by archetype key.
    ///
    /// Additional archetypes added in Phase C+ register themselves here (or in their
    /// own bootstrap partial) using the same pattern.
    /// </summary>
    public static class PartEffectBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterOnRuntimeLoad() => Register();

        /// <summary>
        /// Idempotent registration entry point. Called from runtime load AND from
        /// the editor via <c>[InitializeOnLoadMethod]</c> (so edit-time previews —
        /// TTAW ▶ Preview in scene — work without entering Play mode). Public so
        /// the OSE.Editor assembly can invoke it without cross-assembly internals.
        /// </summary>
        public static void Register()
        {
            PartEffectRegistry.Register(PartEffectArchetypes.Lerp,      BuildLerp);
            PartEffectRegistry.Register(PartEffectArchetypes.ThreadIn,  BuildThreadIn);
            PartEffectRegistry.Register(PartEffectArchetypes.ClampHold, BuildClampHold);
        }

        private static IPartEffect BuildLerp(in PartEffectBuildArgs args)
        {
            return new LerpPosePartEffect(
                args.PartTransform, args.PreviewRoot,
                args.Start.Position, args.Start.Rotation, args.Start.Scale,
                args.End.Position,   args.End.Rotation,   args.End.Scale);
        }

        private static IPartEffect BuildThreadIn(in PartEffectBuildArgs args)
        {
            Vector3 axis  = ThreadInPartEffect.ResolveAxisLocal(
                args.Payload, args.ToolPose, args.Start.Position, args.End.Position);
            float totalDeg = ThreadInPartEffect.ResolveTotalRotation(
                args.Payload, args.Start.Position, args.End.Position);
            string easing = args.Payload?.easing;
            return new ThreadInPartEffect(
                args.PartTransform, args.PreviewRoot,
                args.Start.Position, args.Start.Rotation, args.Start.Scale,
                args.End.Position,   args.End.Scale,
                axis, totalDeg, easing);
        }

        private static IPartEffect BuildClampHold(in PartEffectBuildArgs args)
        {
            return new ClampHoldPartEffect(
                args.PartTransform,
                args.Start.Position, args.Start.Rotation, args.Start.Scale,
                args.End.Position,   args.End.Rotation,   args.End.Scale);
        }
    }
}
