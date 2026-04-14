using System;
using System.Collections.Generic;
using UnityEngine;

namespace OSE.Content.Loading
{
    /// <summary>
    /// How a PoseResolution was derived. The resolver commits to exactly one
    /// of these per (part, seq) cell so downstream code never has to re-run
    /// the decision tree.
    /// </summary>
    public enum PoseSource
    {
        /// <summary>Part isn't visible at this seq — skip rendering.</summary>
        Hidden,
        /// <summary>Part is pre-task: sitting at its bench/layout start pose.</summary>
        Start,
        /// <summary>Part is post-task: resting at its individual assembled pose.</summary>
        Assembled,
        /// <summary>Part is NO-TASK-introduced and has no task claim yet at this seq.</summary>
        NoTaskStart,
        /// <summary>An author-written stepPose span covers this seq.</summary>
        ExplicitSpan,
        /// <summary>Member of a subassembly that was stacked onto a target at or before this seq.</summary>
        IntegratedMember,
        /// <summary>Part is driven by a baked GroupRigidBody at this seq.</summary>
        GroupRigidBody,
        /// <summary>Part is driven by the group's startRigidBody (fabrication-layout chain).</summary>
        StartRigidBody,
    }

    /// <summary>
    /// How an editor/runtime caller wants the resolver to interpret the
    /// part's task-step seq. Runtime always uses <see cref="Committed"/>.
    /// </summary>
    public enum PoseMode
    {
        /// <summary>The pose the runtime would commit to after the step completes.</summary>
        Committed,
        /// <summary>Author is previewing "before they place it" (startPosition).</summary>
        StartPreview,
        /// <summary>Author is previewing "after they place it" (assembledPosition).</summary>
        AssembledPreview,
    }

    /// <summary>
    /// One baked answer in the <see cref="PoseTable"/>. Immutable; every field
    /// is set at construction. Callers read and apply — never mutate.
    /// </summary>
    public readonly struct PoseResolution : IEquatable<PoseResolution>
    {
        public readonly Vector3    pos;
        public readonly Quaternion rot;
        public readonly Vector3    scl;
        public readonly PoseSource source;
        /// <summary>
        /// Context for diagnostics and the "What's Changing" panel. E.g. the
        /// stepId of an <see cref="PoseSource.ExplicitSpan"/>, or
        /// "<c>subassemblyId|targetId</c>" for an
        /// <see cref="PoseSource.IntegratedMember"/>. Never used for behaviour.
        /// </summary>
        public readonly string meta;

        public PoseResolution(Vector3 pos, Quaternion rot, Vector3 scl, PoseSource source, string meta)
        {
            this.pos    = pos;
            this.rot    = rot;
            this.scl    = scl;
            this.source = source;
            this.meta   = meta ?? string.Empty;
        }

        public static PoseResolution Hidden => new PoseResolution(
            Vector3.zero, Quaternion.identity, Vector3.one, PoseSource.Hidden, string.Empty);

        public bool IsHidden => source == PoseSource.Hidden;

        public bool Equals(PoseResolution other)
            => pos == other.pos
            && rot == other.rot
            && scl == other.scl
            && source == other.source
            && string.Equals(meta, other.meta, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PoseResolution other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + pos.GetHashCode();
                h = h * 31 + rot.GetHashCode();
                h = h * 31 + scl.GetHashCode();
                h = h * 31 + (int)source;
                h = h * 31 + (meta != null ? meta.GetHashCode() : 0);
                return h;
            }
        }
    }

    /// <summary>
    /// Composite key for the <see cref="PoseTable"/>. A specific part at a
    /// specific viewing sequence index.
    /// </summary>
    public readonly struct PoseKey : IEquatable<PoseKey>
    {
        public readonly string partId;
        public readonly int viewSeq;

        public PoseKey(string partId, int viewSeq)
        {
            this.partId  = partId ?? string.Empty;
            this.viewSeq = viewSeq;
        }

        public bool Equals(PoseKey other) => viewSeq == other.viewSeq
            && string.Equals(partId, other.partId, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PoseKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = partId != null ? partId.GetHashCode() : 0;
                return (h * 397) ^ viewSeq;
            }
        }
    }

    /// <summary>
    /// Pre-baked answer set for every visible (part, seq) pair in a package.
    /// Populated once by <see cref="MachinePackageNormalizer"/>; read-only
    /// thereafter. Editor and runtime both look up through this table — no
    /// resolver logic lives at the call site.
    ///
    /// Not serialized; lives on <see cref="MachinePackageDefinition.poseTable"/>
    /// for the lifetime of the in-memory package.
    /// </summary>
    public sealed class PoseTable
    {
        private readonly Dictionary<PoseKey, PoseResolution> _map;
        private readonly Dictionary<string, int> _firstVisibleSeq;
        private readonly Dictionary<string, int> _lastVisibleSeq;

        // Retained refs so the table can re-resolve a single cell with a
        // different PoseMode (editor Start/Assembled preview toggle). The
        // baked map always contains the Committed answer; on-demand calls
        // re-run the resolver for the selected part only.
        private readonly MachinePackageDefinition _pkg;
        private readonly PoseResolverIndex _idx;

        internal PoseTable(
            Dictionary<PoseKey, PoseResolution> map,
            Dictionary<string, int> firstVisibleSeq,
            Dictionary<string, int> lastVisibleSeq,
            MachinePackageDefinition pkg,
            PoseResolverIndex idx)
        {
            _map             = map             ?? new Dictionary<PoseKey, PoseResolution>();
            _firstVisibleSeq = firstVisibleSeq ?? new Dictionary<string, int>(StringComparer.Ordinal);
            _lastVisibleSeq  = lastVisibleSeq  ?? new Dictionary<string, int>(StringComparer.Ordinal);
            _pkg = pkg;
            _idx = idx;
        }

        public int Count => _map.Count;

        public bool TryGet(string partId, int viewSeq, out PoseResolution resolution)
            => _map.TryGetValue(new PoseKey(partId, viewSeq), out resolution);

        /// <summary>
        /// Re-resolve a single cell with a caller-chosen
        /// <see cref="PoseMode"/>. Runtime callers always use
        /// <see cref="PoseMode.Committed"/> (the default baked state) and
        /// should prefer <see cref="TryGet"/> for the cheap dictionary
        /// lookup. Editor Start/Assembled preview toggles call this to
        /// override the bake for a single part — the other parts still read
        /// from the baked table, so editor and runtime don't diverge.
        /// </summary>
        public PoseResolution Resolve(string partId, int viewSeq, PoseMode mode)
        {
            if (mode == PoseMode.Committed)
                return _map.TryGetValue(new PoseKey(partId, viewSeq), out var r) ? r : PoseResolution.Hidden;
            if (_pkg == null || _idx == null)
                return PoseResolution.Hidden;
            return PoseResolver.Resolve(partId, viewSeq, _pkg, _idx, mode);
        }

        public PoseResolution GetOrHidden(string partId, int viewSeq)
            => _map.TryGetValue(new PoseKey(partId, viewSeq), out var r) ? r : PoseResolution.Hidden;

        /// <summary>
        /// Earliest seq at which <paramref name="partId"/> is visible. Returns
        /// <c>int.MaxValue</c> if the part never appears.
        /// </summary>
        public int FirstVisibleSeq(string partId)
            => _firstVisibleSeq.TryGetValue(partId ?? string.Empty, out int seq) ? seq : int.MaxValue;

        /// <summary>
        /// Latest seq at which <paramref name="partId"/> is visible. Returns
        /// <c>int.MinValue</c> if the part never appears.
        /// </summary>
        public int LastVisibleSeq(string partId)
            => _lastVisibleSeq.TryGetValue(partId ?? string.Empty, out int seq) ? seq : int.MinValue;

        public IEnumerable<PoseKey> Keys => _map.Keys;
    }
}
