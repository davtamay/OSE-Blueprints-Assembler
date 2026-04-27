using System;

namespace OSE.Content
{
    /// <summary>
    /// Lightweight reference to a Step Configuration Prefab. One entry per
    /// instantiation. Lives inside the assembly file alongside the
    /// concrete entities that the prefab does NOT supply (that prefab brings
    /// only steps in Slice 1; partDefinitions / partGroupDefinition layers
    /// are added in Slice 2). At load time
    /// <see cref="OSE.Content.Loading.MachinePackageNormalizer.ExpandPrefabInstances"/>
    /// expands every entry into virtual
    /// <see cref="StepDefinition"/>s tagged with a matching
    /// <see cref="PrefabRef"/>; the rest of the system never sees the
    /// difference.
    ///
    /// <para>Edits to the source prefab YAML propagate to every instance on
    /// the next package load — no JSON duplication in assembly files.</para>
    /// </summary>
    [Serializable]
    public sealed class PrefabInstance
    {
        /// <summary>Stem of the source prefab YAML in <c>AgentAssistant/prefabs/</c>.</summary>
        public string prefabId;

        /// <summary>Unique per-instance id used for Bake / Discard scoping and provenance.</summary>
        public string instanceId;

        /// <summary>
        /// Step-id prefix applied at expansion time:
        /// <c>step_{prefix}_{step.id_suffix}</c>.
        /// </summary>
        public string prefix;

        /// <summary>
        /// First <see cref="StepDefinition.sequenceIndex"/> assigned to the
        /// emitted steps. Subsequent steps increment sequentially.
        /// </summary>
        public int startSeq;

        /// <summary>
        /// Assembly id every emitted step is tagged with. Echoes the
        /// assembly-file the instance lives in so multi-file packages keep
        /// the same authoring locality on save.
        /// </summary>
        public string assemblyId;

        /// <summary>
        /// PartGroup id every emitted step is tagged with. Slice 1 expects the
        /// part group to already exist in the package; Slice 2 will let the
        /// prefab author it via <c>partGroupDefinition:</c>.
        /// </summary>
        public string partGroupId;

        /// <summary>Role → partId bindings consumed by template substitution.</summary>
        public PrefabRoleBinding[] bindings;

        /// <summary>
        /// Per-instance overrides for prefab <c>options:</c> entries.
        /// Slice 1 supports string-typed options only; Slice 2 adds vector3
        /// and other typed values via <see cref="PrefabOptionValue.valueJson"/>.
        /// </summary>
        public PrefabOptionValue[] options;

        /// <summary>
        /// Slice 3 placeholder — fine-grained per-field overrides
        /// (e.g. <c>step:place_bearings.guidance.instructionText</c>).
        /// Empty in Slice 1. Stored on the instance so the JSON shape is
        /// stable across slices.
        /// </summary>
        public PrefabOverride[] overrides;

        /// <summary>
        /// When true, the expander emits no <see cref="PartDefinition"/>s
        /// (or sibling <see cref="PartPreviewPlacement"/>s) for this
        /// instance — used when the target package already declares the
        /// parts and the prefab should only contribute groupings + steps.
        /// Slice 2e: per-section import toggles surfaced in the wizard's
        /// "what will be created" preview.
        /// </summary>
        public bool skipParts;

        /// <summary>When true, the expander emits no <see cref="PartGroupDefinition"/> for this instance.</summary>
        public bool skipPartGroup;

        /// <summary>When true, the expander emits no <see cref="StepDefinition"/>s for this instance.</summary>
        public bool skipSteps;

        public bool IsEmpty()
            => string.IsNullOrEmpty(prefabId)
            && string.IsNullOrEmpty(instanceId);
    }

    /// <summary>
    /// Per-instance override for a named prefab option.
    /// <see cref="valueJson"/> carries a JSON-encoded scalar so structured
    /// values (vector3, etc.) flow through the same field. Slice 1 emits
    /// string values literally (no JSON quotes); Slice 2 will introduce
    /// typed parsing keyed on the prefab's option declaration.
    /// </summary>
    [Serializable]
    public sealed class PrefabOptionValue
    {
        public string key;
        public string valueJson;
    }

    /// <summary>Slice 3 placeholder. Ignored by the Slice 1 expander.</summary>
    [Serializable]
    public sealed class PrefabOverride
    {
        public string path;
        public string valueJson;
    }
}
