using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Runtime;
using OSE.Runtime.Preview;
using UnityEngine;

namespace OSE.UI.Root
{
    /// <summary>
    /// Subscribes to <see cref="SessionDriver.PackageChanged"/> and spawns / positions
    /// GLB part GameObjects under the <see cref="PreviewSceneSetup.PreviewRoot"/> transform.
    /// Exposes the spawned part list so sibling components (PartInteractionBridge) can
    /// interact with them.
    ///
    /// This component knows nothing about runtime events, click interaction, or previews.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PreviewSceneSetup))]
    public sealed class PackagePartSpawner : MonoBehaviour, Interaction.ISpawnerQueryService, IStepAwarePositioner
    {
        private const string SamplePartName = "Sample Beam";
        private PreviewSceneSetup _setup;
        private MachinePackageDefinition _currentPackage;
        private PackagePreviewConfig _currentPreviewConfig;
        private readonly List<GameObject> _spawnedParts = new List<GameObject>();
        private readonly PackageAssetResolver _resolver = new PackageAssetResolver();
        private readonly PreviewConfigLookup _configLookup = new PreviewConfigLookup();
        private EditModeGhostManager _ghostManager;
        private PartAssetLoader _assetLoader;
        private IXRGrabSetup _xrGrabSetup;

        // Guards re-entrant async spawns: if HandlePackageChanged fires while a spawn is
        // in-flight, the previous task is cancelled before the new one starts.
        private CancellationTokenSource _spawnCts;

        // Prevents synchronous re-entrancy: editor callbacks (e.g. OnDisable on a
        // destroyed child) could publish a PackageLoaded event while ClearSpawnedParts
        // is iterating. The flag makes the nested call a no-op.
        private bool _isHandlingPackageChange;

        // ── Public accessors ──

        public IReadOnlyList<GameObject> SpawnedParts => _spawnedParts;
        public MachinePackageDefinition CurrentPackage => _currentPackage;
        public PackagePreviewConfig CurrentPreviewConfig => _currentPreviewConfig;
        public Transform PreviewRoot => _setup != null ? _setup.PreviewRoot : null;

        /// <summary>
        /// Controls where GLB assets are fetched from at runtime.
        /// Defaults to <see cref="StreamingAssetsSource"/> (reads from the build's StreamingAssets folder).
        /// Swap for a <see cref="RemoteAssetSource"/> to load from S3, a CDN, or any HTTP server.
        /// </summary>
        public IAssetSource AssetSource
        {
            get => _assetLoader?.AssetSource;
            set { if (_assetLoader != null) _assetLoader.AssetSource = value; }
        }

        // ── Lifecycle ──

        private void OnEnable()
        {
            _setup = GetComponent<PreviewSceneSetup>();
            _assetLoader = new PartAssetLoader(
                () => _currentPackage?.packageId,
                () => _setup?.PreviewRoot);
#if !UNITY_EDITOR
            _assetLoader.AssetSource = new StreamingAssetsSource();
#endif
            _ghostManager = new EditModeGhostManager(
                _spawnedParts,
                () => _setup?.PreviewRoot,
                FindPartPlacement,
                FindSubassemblyPlacement,
                FindIntegratedSubassemblyPlacement,
                FindConstrainedSubassemblyFitPlacement,
                _resolver,
                TryLoadPackageAsset);

            if (!ServiceRegistry.TryGet<IXRGrabSetup>(out _xrGrabSetup))
                _xrGrabSetup = new XRGrabSetupAdapter();

            // Register so sibling systems (PartInteractionBridge, PartVisualFeedbackManager)
            // can resolve IXRGrabSetup without directly importing XRI types (ADR 005).
            ServiceRegistry.Register<IXRGrabSetup>(_xrGrabSetup);
            ServiceRegistry.Register<IXRAffordanceSetup>(new XRAffordanceSetupAdapter());
            ServiceRegistry.Register<Interaction.ISpawnerQueryService>(this);
            ServiceRegistry.Register<IStepAwarePositioner>(this);
            RuntimeEventBus.Subscribe<PackageLoaded>(OnPackageLoaded);

            // Catch up if this component enabled after the latest package event.
            if (SessionDriver.CurrentPackage != null)
            {
                HandlePackageChanged(SessionDriver.CurrentPackage);
            }
        }

        private void OnDisable()
        {
            _spawnCts?.Cancel();
            _spawnCts?.Dispose();
            _spawnCts = null;
            RuntimeEventBus.Unsubscribe<PackageLoaded>(OnPackageLoaded);
            ServiceRegistry.Unregister<IXRGrabSetup>();
            ServiceRegistry.Unregister<IXRAffordanceSetup>();
            ServiceRegistry.Unregister<Interaction.ISpawnerQueryService>();
            ServiceRegistry.Unregister<IStepAwarePositioner>();
            _ghostManager?.Clear(); // destroy ghost GOs before manager is orphaned
            DestroyPooledParts();
        }

        // ── Public API ──

        /// <summary>
        /// Finds the <see cref="PartPreviewPlacement"/> for a given part id from
        /// the current preview config. Returns null if not found.
        /// </summary>
        public PartPreviewPlacement FindPartPlacement(string partId)
            => _configLookup.FindPartPlacement(partId);

        /// <summary>
        /// Repositions existing spawned parts for step-aware preview in edit mode.
        /// Parts from steps before <paramref name="targetSequenceIndex"/> are shown
        /// at their playPosition; the current step's part at startPosition; future
        /// parts are hidden. Pass 0 to show all parts at startPosition (All Steps mode).
        /// </summary>
        public void ApplyStepAwarePositions(int targetSequenceIndex, MachinePackageDefinition pkg)
        {
            if (_spawnedParts.Count == 0) { _ghostManager?.Clear(); return; }

            if (pkg == null || targetSequenceIndex <= 0)
            {
                // All Steps mode — restore all parts to startPosition and make visible
                _ghostManager?.Clear();
                foreach (var partGo in _spawnedParts)
                {
                    if (partGo == null) continue;
                    partGo.SetActive(true);
                    RestoreEditModeVisual(partGo);
                }
                PositionPartsFallback();
                return;
            }

            // Build partId → earliest sequenceIndex map from step definitions.
            // Also track which parts belong to any subassembly — they are pre-assembled
            // before their step and always use playPosition regardless of current step.
            var partStepSeq      = new Dictionary<string, int>(System.StringComparer.Ordinal);
            var subassemblyParts = new HashSet<string>(System.StringComparer.Ordinal);
            var orderedSteps     = pkg.GetOrderedSteps();
            foreach (var step in orderedSteps)
            {
                if (step == null) continue;
                string[] stepParts = step.GetEffectiveRequiredPartIds();
                foreach (string pid in stepParts)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                        partStepSeq[pid] = step.sequenceIndex;
                }
                if (!string.IsNullOrEmpty(step.requiredSubassemblyId) &&
                    pkg.TryGetSubassembly(step.requiredSubassemblyId, out var subDef) &&
                    subDef?.partIds != null)
                {
                    foreach (string pid in subDef.partIds)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                            partStepSeq[pid] = step.sequenceIndex;
                        subassemblyParts.Add(pid);
                    }
                }
            }

            // "Fully Assembled" mode: targetSequenceIndex past the last step means
            // show every assigned part at its playPosition (same as runtime final view).
            int lastStepSeq = orderedSteps.Length > 0
                ? orderedSteps[orderedSteps.Length - 1].sequenceIndex
                : 0;
            bool fullyAssembled = targetSequenceIndex > lastStepSeq;

            // Build the set of subassembly IDs whose stacking step is completed
            // (sequenceIndex < targetSequenceIndex).  Only these subassemblies have
            // their members placed at integrated (cube) positions; members whose
            // stacking step hasn't happened yet stay at their fabrication playPosition.
            var stackedSubassemblyIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var step in orderedSteps)
            {
                if (step == null || string.IsNullOrEmpty(step.requiredSubassemblyId)) continue;
                if (fullyAssembled || step.sequenceIndex < targetSequenceIndex)
                    stackedSubassemblyIds.Add(step.requiredSubassemblyId);
            }

            // Build partId → IntegratedMemberPreviewPlacement map ONLY for subassemblies
            // whose stacking step is completed.  This ensures bars stay at fabrication
            // positions during their fabrication steps and move to cube positions only
            // after their stacking step completes.
            var integratedMemberMap = new Dictionary<string, IntegratedMemberPreviewPlacement>(System.StringComparer.Ordinal);
            IntegratedSubassemblyPreviewPlacement[] intPlacements = _currentPreviewConfig?.integratedSubassemblyPlacements;
            if (intPlacements != null)
            {
                for (int ip = 0; ip < intPlacements.Length; ip++)
                {
                    IntegratedSubassemblyPreviewPlacement intPlacement = intPlacements[ip];
                    if (intPlacement?.memberPlacements == null) continue;
                    if (!stackedSubassemblyIds.Contains(intPlacement.subassemblyId ?? "")) continue;
                    for (int mp = 0; mp < intPlacement.memberPlacements.Length; mp++)
                    {
                        IntegratedMemberPreviewPlacement member = intPlacement.memberPlacements[mp];
                        if (member != null && !string.IsNullOrEmpty(member.partId))
                            integratedMemberMap[member.partId] = member;
                    }
                }
            }

            // ── Pre-compute which part IDs will be covered by subassembly ghosts ──
            // At the current step, subassembly member parts would otherwise render at
            // their individual play positions AND as ghost children — hide the real ones
            // so only the ghost silhouette shows.  Single-part ghosts are fine: the real
            // part sits at its start position while the ghost shows the play position.
            var ghostedSubassemblyPartIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (!Application.isPlaying && !fullyAssembled)
            {
                StepDefinition ghostStep = null;
                foreach (var step in orderedSteps)
                {
                    if (step != null && step.sequenceIndex == targetSequenceIndex)
                    { ghostStep = step; break; }
                }
                if (ghostStep?.targetIds != null)
                {
                    foreach (string tid in ghostStep.targetIds)
                    {
                        if (string.IsNullOrEmpty(tid) || !pkg.TryGetTarget(tid, out var tgt)) continue;

                        if (!string.IsNullOrWhiteSpace(tgt.associatedSubassemblyId) &&
                            pkg.TryGetSubassembly(tgt.associatedSubassemblyId, out var sub) &&
                            sub?.partIds != null)
                        {
                            foreach (string pid in sub.partIds)
                            {
                                if (!string.IsNullOrEmpty(pid))
                                    ghostedSubassemblyPartIds.Add(pid);
                            }
                        }
                    }
                }
            }

            foreach (var partGo in _spawnedParts)
            {
                if (partGo == null) continue;
                PartPreviewPlacement pp = FindPartPlacement(partGo.name);
                if (pp == null) continue;
                if (SplinePartFactory.HasSplineData(pp)) continue;

                bool assigned = partStepSeq.TryGetValue(partGo.name, out int partSeq);
                if (!assigned || (!fullyAssembled && partSeq > targetSequenceIndex))
                {
                    partGo.SetActive(false);
                }
                // Hide real parts whose subassembly ghost replaces them
                else if (ghostedSubassemblyPartIds.Contains(partGo.name))
                {
                    partGo.SetActive(false);
                }
                else
                {
                    partGo.SetActive(true);
                    // Subassembly members are pre-assembled before their step — no
                    // meaningful individual startPosition, always use playPosition.
                    bool usePlay = fullyAssembled || partSeq < targetSequenceIndex || subassemblyParts.Contains(partGo.name);

                    Vector3 pos;
                    Quaternion rot;
                    Vector3 scl;

                    // Prefer integrated member placement when available — these are
                    // the canonical assembled poses that play mode commits via
                    // SubassemblyPlacementController.TryApplyIntegratedPlacement().
                    if (usePlay && integratedMemberMap.TryGetValue(partGo.name, out IntegratedMemberPreviewPlacement imp))
                    {
                        pos = new Vector3(imp.position.x, imp.position.y, imp.position.z);
                        rot = !imp.rotation.IsIdentity
                            ? new Quaternion(imp.rotation.x, imp.rotation.y, imp.rotation.z, imp.rotation.w)
                            : Quaternion.identity;
                        scl = new Vector3(imp.scale.x, imp.scale.y, imp.scale.z);
                    }
                    else if (usePlay)
                    {
                        pos = new Vector3(pp.playPosition.x, pp.playPosition.y, pp.playPosition.z);
                        rot = !pp.playRotation.IsIdentity
                            ? new Quaternion(pp.playRotation.x, pp.playRotation.y, pp.playRotation.z, pp.playRotation.w)
                            : Quaternion.identity;
                        scl = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                    }
                    else
                    {
                        pos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                        rot = !pp.startRotation.IsIdentity
                            ? new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w)
                            : Quaternion.identity;
                        scl = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    }

                    // Fall back to playScale when startScale is zero
                    if (!usePlay && scl.sqrMagnitude < 0.00001f)
                        scl = new Vector3(pp.playScale.x, pp.playScale.y, pp.playScale.z);
                    partGo.transform.SetLocalPositionAndRotation(pos, rot);
                    partGo.transform.localScale = scl;

                    // ── Visual feedback (edit-mode only) ──
                    // Keep original materials on all parts. Active step parts get an
                    // emission glow; prior-step parts keep their textures with no glow.
                    if (!Application.isPlaying)
                    {
                        MaterialHelper.ClearTint(partGo);
                        MaterialHelper.SetEmission(partGo, !usePlay
                            ? InteractionVisualConstants.ActiveStepEmission
                            : Color.black);
                    }
                }
            }

            // ── Fully-assembled visual restore ──
            if (!Application.isPlaying && fullyAssembled)
            {
                foreach (var partGo in _spawnedParts)
                {
                    if (partGo != null)
                        RestoreEditModeVisual(partGo);
                }
            }

            // ── Edit-mode ghost previews ──
            // Show translucent ghosts at the play (target) position for parts that
            // are currently at their start position (i.e. the parts being placed in
            // the active step). This mirrors what PreviewSpawnManager does at runtime.
            _ghostManager?.SpawnGhosts(pkg, orderedSteps, targetSequenceIndex, fullyAssembled, partStepSeq, subassemblyParts);
        }

        // ── Edit-mode visual helpers ────────────────────────────────────

        /// <summary>
        /// Restores a part to its neutral visual state: original materials, no
        /// emission, no tint.  Used when leaving step-aware mode or entering
        /// "All Steps" / "Fully Assembled" view.
        /// </summary>
        private static void RestoreEditModeVisual(GameObject partGo)
        {
            if (partGo == null) return;
            MaterialHelper.SetEmission(partGo, Color.black);
            MaterialHelper.ClearTint(partGo);
        }

        // ── Edit-mode ghost previews — delegated to EditModeGhostManager.cs ──

        // ── Placement config lookup ──

        /// <summary>
        /// Finds the <see cref="TargetPreviewPlacement"/> for a given target id.
        /// </summary>
        public TargetPreviewPlacement FindTargetPlacement(string targetId)
            => _configLookup.FindTargetPlacement(targetId);

        /// <summary>
        /// Finds the <see cref="SubassemblyPreviewPlacement"/> for a given subassembly id.
        /// </summary>
        public SubassemblyPreviewPlacement FindSubassemblyPlacement(string subassemblyId)
            => _configLookup.FindSubassemblyPlacement(subassemblyId);

        /// <summary>
        /// Finds the constrained-fit payload for a subassembly placement target.
        /// </summary>
        public ConstrainedSubassemblyFitPreviewPlacement FindConstrainedSubassemblyFitPlacement(string subassemblyId, string targetId)
            => _configLookup.FindConstrainedSubassemblyFitPlacement(subassemblyId, targetId);

        /// <summary>
        /// Finds the optional parking frame for a completed fabricated subassembly.
        /// </summary>
        public SubassemblyPreviewPlacement FindCompletedSubassemblyParkingPlacement(string subassemblyId)
            => _configLookup.FindCompletedSubassemblyParkingPlacement(subassemblyId);

        /// <summary>
        /// Finds the canonical integrated placement authored for a completed subassembly
        /// when it is committed to a specific assembly target.
        /// </summary>
        public IntegratedSubassemblyPreviewPlacement FindIntegratedSubassemblyPlacement(string subassemblyId, string targetId)
            => _configLookup.FindIntegratedSubassemblyPlacement(subassemblyId, targetId);

        /// <summary>
        /// Returns the integrated member placement for a specific partId, or null if the
        /// part is not covered by any <c>integratedSubassemblyPlacements</c> entry.
        /// Used by play-mode restore paths to place completed subassembly members at their
        /// canonical assembled poses instead of individual <c>playPosition</c>.
        /// </summary>
        public IntegratedMemberPreviewPlacement FindIntegratedMemberPlacement(string partId)
            => _configLookup.FindIntegratedMemberPlacement(partId);

        /// <summary>
        /// Applies a package snapshot immediately. Used by late-initializing listeners
        /// that need deterministic startup behavior in play mode.
        /// </summary>
        public void ApplyPackageSnapshot(MachinePackageDefinition package)
        {
            if (package == null)
                return;

            HandlePackageChanged(package);
        }

        /// <summary>
        /// Asynchronously loads a package model asset using the registered <see cref="AssetSource"/>.
        /// Delegates to <see cref="PartAssetLoader.LoadAsync"/>.
        /// </summary>
        public Task<GameObject> LoadPackageAssetAsync(
            string assetRef,
            Transform parent = null,
            CancellationToken ct = default)
            => _assetLoader.LoadAsync(assetRef, parent, ct);

        /// <summary>
        /// Loads a package model asset from AssetDatabase (editor) or null (builds — use <see cref="LoadPackageAssetAsync"/>).
        /// Delegates to <see cref="PartAssetLoader.TryLoad"/>.
        /// </summary>
        public GameObject TryLoadPackageAsset(string assetRef) => _assetLoader.TryLoad(assetRef);

        private GameObject TryLoadCombinedGlbNode(
            string assetRef,
            string nodeName,
            Dictionary<string, GameObject> cache)
            => _assetLoader.TryLoadCombinedNode(assetRef, nodeName, cache);

        /// <summary>
        /// Guarantees a bare filename (e.g. "foo.glb") is returned as a proper
        /// package-relative path ("assets/parts/foo.glb") so that
        /// <see cref="StreamingAssetsSource"/> builds the correct URI at runtime.
        /// Paths that already contain a directory separator are returned unchanged.
        /// </summary>
        private static string EnsurePartsSubfolder(string assetRef)
        {
            if (string.IsNullOrEmpty(assetRef)) return assetRef;
            if (assetRef.Contains('/') || assetRef.Contains('\\')) return assetRef;
            return "assets/parts/" + assetRef;
        }

        // ── Events ──

        private void OnPackageLoaded(PackageLoaded e) => HandlePackageChanged(SessionDriver.CurrentPackage);

        private void HandlePackageChanged(MachinePackageDefinition package)
        {
            if (_isHandlingPackageChange) return;
            _isHandlingPackageChange = true;
            try
            {
                HandlePackageChangedCore(package);
            }
            finally
            {
                _isHandlingPackageChange = false;
            }
        }

        private void HandlePackageChangedCore(MachinePackageDefinition package)
        {
            // Cancel any in-flight async spawn before clearing parts.
            _spawnCts?.Cancel();
            _spawnCts?.Dispose();
            _spawnCts = new CancellationTokenSource();

            _currentPackage = package;
            _currentPreviewConfig = package?.previewConfig;
            _configLookup.SetConfig(_currentPreviewConfig);

            // Build the asset resolution catalog — scans the parts folder and maps
            // part IDs to GLB files (individual or nodes inside combined files).
            // Spline parts (hoses, cables with splinePath data) are rendered procedurally
            // by SplinePartFactory and never need a GLB — exclude them from the catalog
            // so they don't appear as false "unresolved" errors.
            if (package != null && !string.IsNullOrWhiteSpace(package.packageId))
            {
                PartDefinition[] glbParts = package.parts;
                if (package.previewConfig?.partPlacements != null)
                {
                    var splineIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    foreach (var pp in package.previewConfig.partPlacements)
                        if (SplinePartFactory.HasSplineData(pp))
                            splineIds.Add(pp.partId);

                    if (splineIds.Count > 0)
                        glbParts = System.Array.FindAll(package.parts, p => !splineIds.Contains(p.id));
                }

                _resolver.BuildCatalog(package.packageId, glbParts);
                if (_resolver.HasUnresolved)
                    _resolver.LogUnresolved(package.packageId);
                OseLog.Info($"[PackagePartSpawner] Asset catalog: {_resolver.ResolvedCount} resolved, " +
                            $"{_resolver.UnresolvedParts.Count} unresolved.");
            }

            ClearSpawnedParts();
            RespawnAndPosition();
        }

        // ── Spawning ──

        private void RespawnAndPosition()
        {
            if (_setup.PreviewRoot == null)
                return;

#if UNITY_EDITOR
            SpawnPackageParts();
            PositionParts();
            // Immediately apply step-aware positions so the editor preview
            // matches play-mode layout from the moment parts spawn.
            if (!Application.isPlaying)
            {
                var driver = FindAnyObjectByType<EditModePreviewDriver>();
                if (driver != null)
                {
                    int stepSeq = driver.PreviewStepSequenceIndex;
                    var pkg = driver.EditModePackage;
                    if (stepSeq > 0 && pkg != null)
                        ApplyStepAwarePositions(stepSeq, pkg);
                }
            }
            RuntimeEventBus.Publish(new SpawnerPartsReady());
#else
            _ = SpawnPackagePartsAsync(_spawnCts.Token);
#endif
        }

        private async Task SpawnPackagePartsAsync(CancellationToken ct)
        {
            SpawnPackageParts();                  // allocates primitives / spline parts synchronously
            await SpawnGlbPartsAsync(ct);         // then fills in GLB models asynchronously
            if (ct.IsCancellationRequested) return;
            PositionParts();
            RuntimeEventBus.Publish(new SpawnerPartsReady());
        }

        /// <summary>
        /// Async pass: for each part that still has a primitive placeholder, load the real GLB
        /// and replace the placeholder. Runs after <see cref="SpawnPackageParts"/> has populated
        /// the list (with primitives where assets weren't available synchronously).
        /// </summary>
        private async Task SpawnGlbPartsAsync(CancellationToken ct)
        {
            if (_currentPackage?.parts == null || AssetSource == null)
                return;

            for (int i = 0; i < _spawnedParts.Count; i++)
            {
                if (ct.IsCancellationRequested) return;
                GameObject existing = _spawnedParts[i];
                if (existing == null) continue;

                // Find the PartDefinition for this slot
                PartDefinition partDef = null;
                foreach (var p in _currentPackage.parts)
                {
                    if (string.Equals(p.id, existing.name, System.StringComparison.OrdinalIgnoreCase))
                    { partDef = p; break; }
                }

                if (partDef == null)
                    continue;

                // Resolve asset path via catalog, falling back to explicit assetRef.
                // The catalog always returns a proper relative path (e.g. "assets/parts/foo.glb").
                // When falling back to the raw field, ensure a bare filename gets the subfolder
                // prefix so StreamingAssetsSource constructs the correct URI at runtime.
                AssetResolution resolution = _resolver.Resolve(partDef.id);
                string assetRefToLoad = resolution.IsResolved
                    ? resolution.AssetPath
                    : EnsurePartsSubfolder(partDef.assetRef);
                if (string.IsNullOrWhiteSpace(assetRefToLoad))
                    continue;

                // Skip if this is already an imported model (not a primitive placeholder)
                if (MaterialHelper.IsImportedModel(existing))
                    continue;

                // Skip spline parts
                PartPreviewPlacement splinePP = FindPartPlacement(partDef.id);
                if (SplinePartFactory.HasSplineData(splinePP))
                    continue;

                // TODO: combined GLB node extraction at runtime (currently only individual GLBs)
                GameObject loaded = await LoadPackageAssetAsync(assetRefToLoad, _setup.PreviewRoot, ct);
                if (loaded == null) continue;

                // Re-check cancellation immediately after the await: a navigation or package
                // change may have fired while the GLB was loading. Destroy the freshly-loaded
                // GO so it doesn't linger as an untracked child of PreviewRoot.
                if (ct.IsCancellationRequested)
                {
                    SafeDestroy(loaded);
                    return;
                }

                // Swap placeholder for real model
                loaded.name = partDef.id;
                MaterialHelper.MarkAsImported(loaded);
                if (Application.isPlaying)
                    TryEnableXRGrabInteractable(loaded, partDef.grabConfig);

                // Preserve the placeholder's transform — it may have been moved to
                // an integrated cube position (or any other non-default pose) by
                // EnforceIntegratedPositions / navigation before this GLB finished loading.
                // Without copying, the new GLB spawns at PreviewRoot origin and
                // ShouldPreservePartTransform (Completed state) would lock it there.
                loaded.transform.SetLocalPositionAndRotation(
                    existing.transform.localPosition,
                    existing.transform.localRotation);
                loaded.transform.localScale = existing.transform.localScale;

                // Preserve the placeholder's active state — if HideNonIntroducedParts
                // already ran and hid it, the replacement must stay hidden too.
                bool wasActive = existing.activeSelf;

                // Destroy placeholder, insert real model at same list index
                SafeDestroy(existing);
                _spawnedParts[i] = loaded;

                if (!wasActive)
                    loaded.SetActive(false);

                // Notify the visual system immediately so it can re-apply the correct
                // material state for this part. Without this, the newly swapped-in GLB
                // renders with raw glTFast materials until SpawnerPartsReady fires after
                // ALL GLBs have loaded — causing a pink flash during Shader Graph compilation.
                RuntimeEventBus.Publish(new SpawnerPartSwapped(partDef.id));
            }
        }

        private void SpawnPackageParts()
        {
            _spawnedParts.Clear();
            bool enableRuntimeGrab = Application.isPlaying;

            if (_currentPackage?.parts == null || _currentPackage.parts.Length == 0)
            {
                _spawnedParts.Add(GetOrCreatePrimitive(SamplePartName, PrimitiveType.Cube));
                return;
            }

            // Cache for combined GLBs: load once, extract multiple nodes.
            var combinedGlbCache = new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var part in _currentPackage.parts)
            {
                if (string.IsNullOrWhiteSpace(part.id))
                    continue;

                Transform existing = _setup.PreviewRoot.Find(part.id);
                if (existing != null)
                {
                    if (Application.isPlaying)
                    {
                        EnsureColliders(existing.gameObject);
                    }
                    if (enableRuntimeGrab)
                        TryEnableXRGrabInteractable(existing.gameObject, part.grabConfig);
                    _spawnedParts.Add(existing.gameObject);
                    continue;
                }

                // Spline-based parts (hoses, cables) — create procedural tube mesh
                PartPreviewPlacement splinePP = FindPartPlacement(part.id);
                if (SplinePartFactory.HasSplineData(splinePP))
                {
                    Color sc = new Color(splinePP.color.r, splinePP.color.g, splinePP.color.b, splinePP.color.a);
                    GameObject splineGo = SplinePartFactory.Create(part.id, splinePP.splinePath, sc, _setup.PreviewRoot);
                    MaterialHelper.MarkAsImported(splineGo);
                    if (enableRuntimeGrab)
                        TryEnableXRGrabInteractable(splineGo);
                    _spawnedParts.Add(splineGo);
                    continue;
                }

                // Resolve via the asset catalog (supports both individual and combined GLBs)
                AssetResolution resolution = _resolver.Resolve(part.id);
                string assetRefToLoad = resolution.IsResolved ? resolution.AssetPath : part.assetRef;
                GameObject go = null;

                if (resolution.IsResolved && resolution.IsNodeInCombined)
                {
                    // Combined GLB — extract the specific child node
                    go = TryLoadCombinedGlbNode(assetRefToLoad, resolution.NodeName, combinedGlbCache);
                }

                if (go == null && !string.IsNullOrWhiteSpace(assetRefToLoad))
                {
                    go = TryLoadPackageAsset(assetRefToLoad);
                }

                if (go != null)
                {
                    MaterialHelper.MarkAsImported(go);
                }
                else
                    go = GetOrCreatePrimitive(part.id, PrimitiveType.Cube);
                go.name = part.id;
                if (enableRuntimeGrab)
                    TryEnableXRGrabInteractable(go, part.grabConfig);
                _spawnedParts.Add(go);
            }

            // Clean up cached combined GLB roots (the extracted nodes are already reparented)
            foreach (var kvp in combinedGlbCache)
                SafeDestroy(kvp.Value);

            bool showGeometry = _setup.ActiveProfile.ShowGeometryPreview;
            foreach (var p in _spawnedParts)
                SetObjectActive(p, showGeometry);
        }

        // Layout constants moved to PartPositionResolver — access via PartPositionResolver.LayoutRadius etc.

        public void RefreshLoosePartPresentationLayout()
        {
            if (_setup?.PreviewRoot == null)
                return;

            PositionParts();
        }

        private void PositionParts()
        {
            PartPositionResolver.PositionParts(
                _spawnedParts,
                _currentPackage,
                Application.isPlaying,
                _setup.ActiveProfile.ShowGeometryPreview,
                FindPartPlacement,
                ShouldPreservePartTransform);
        }

        /// <summary>
        /// Edit-mode fallback: use previewConfig positions or linear grid.
        /// Delegates to <see cref="PartPositionResolver.PositionPartsFallback"/>.
        /// </summary>
        private void PositionPartsFallback()
        {
            PartPositionResolver.PositionPartsFallback(
                _spawnedParts,
                Application.isPlaying,
                FindPartPlacement,
                ShouldPreservePartTransform);
        }


        // ── Cleanup ──

        private void ClearSpawnedParts()
        {
            _ghostManager?.Clear();
            foreach (var go in _spawnedParts)
            {
                if (go == null) continue;

                if (Application.isPlaying)
                {
                    // Pool the GO by deactivating — SpawnPackageParts finds it via
                    // PreviewRoot.Find(part.id) on the next spawn and reactivates it,
                    // avoiding a destroy+instantiate+load cycle on every package change.
                    // DestroyPooledParts() cleans them up when the component is disabled.
                    go.SetActive(false);
                }
                else
                {
                    go.transform.SetParent(null);
                    SafeDestroy(go);
                }
            }
            _spawnedParts.Clear();

            if (!Application.isPlaying)
                Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// Destroys all deactivated (pooled) children of the preview root.
        /// Called from <see cref="OnDisable"/> to ensure pooled GOs don't linger
        /// after the component is torn down.
        /// </summary>
        private void DestroyPooledParts()
        {
            if (_setup?.PreviewRoot == null) return;

            var toDestroy = new List<Transform>(_setup.PreviewRoot.childCount);
            for (int i = 0; i < _setup.PreviewRoot.childCount; i++)
            {
                Transform child = _setup.PreviewRoot.GetChild(i);
                if (child != null && !child.gameObject.activeSelf)
                    toDestroy.Add(child);
            }
            foreach (var child in toDestroy)
            {
                if (child != null)
                    SafeDestroy(child.gameObject);
            }

            if (Application.isPlaying && toDestroy.Count > 0)
                Resources.UnloadUnusedAssets();
        }

        // ── Helpers ──

        private GameObject GetOrCreatePrimitive(string name, PrimitiveType type)
        {
            Transform existing = _setup.PreviewRoot != null ? _setup.PreviewRoot.Find(name) : null;
            if (existing != null)
            {
                var go = existing.gameObject;
                if (go.GetComponent<MeshFilter>() != null && go.GetComponent<MeshRenderer>() != null)
                    return go;
                SafeDestroy(go);
            }

            var prim = GameObject.CreatePrimitive(type);
            prim.name = name;
            prim.transform.SetParent(_setup.PreviewRoot, false);
            return prim;
        }

        private void TryEnableXRGrabInteractable(GameObject target, PartGrabConfig grabConfig = null)
            => _xrGrabSetup?.EnableGrab(target, grabConfig);

        /// <summary>
        /// Adds MeshColliders to every child with a MeshFilter for accurate raycasting.
        /// Falls back to a fitted BoxCollider if no MeshFilters exist.
        /// </summary>
        public static void EnsureColliders(GameObject target)
            => XRPartInteractionSetup.EnsureColliders(target);

        private static bool ShouldPreservePartTransform(string partId)
        {
            if (!Application.isPlaying || string.IsNullOrWhiteSpace(partId))
                return false;

            if (!ServiceRegistry.TryGet<IPartRuntimeController>(out var partController) ||
                partController == null)
            {
                return false;
            }

            PartPlacementState state = partController.GetPartState(partId);
            return state == PartPlacementState.Grabbed ||
                   state == PartPlacementState.PlacedVirtually ||
                   state == PartPlacementState.Completed;
        }

        private static void SetObjectActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
                target.SetActive(active);
        }

        private static void SafeDestroy(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

    }
}
