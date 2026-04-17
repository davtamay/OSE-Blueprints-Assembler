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

        // Subassembly root GO lifecycle — mirrors TTAW Phase A2 so play-mode
        // hierarchy matches the authoring view. Keyed by subassembly id.
        private readonly Dictionary<string, GameObject> _subassemblyRoots =
            new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
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

#if UNITY_EDITOR
        private void Awake()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Nukes all PreviewRoot children on play-mode transitions to guarantee a
        /// clean slate. Without this, pooled (deactivated) parts and editor ghosts
        /// can survive across the boundary and appear as lingering objects.
        /// </summary>
        private void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            // ExitingEditMode  → about to enter play: destroy editor parts so play starts clean
            // ExitingPlayMode  → about to return to edit: destroy play parts so editor starts clean
            if (state != UnityEditor.PlayModeStateChange.ExitingEditMode &&
                state != UnityEditor.PlayModeStateChange.ExitingPlayMode)
                return;

            _spawnedParts.Clear();
            _ghostManager?.Clear();

            if (_setup == null) _setup = GetComponent<PreviewSceneSetup>();
            if (_setup?.PreviewRoot == null) return;

            // Destroy spawned children of PreviewRoot (parts, ghosts, tool targets,
            // previews) but preserve the "UI Root" host — it holds the UIDocument
            // with its PanelSettings asset reference that can't be recreated from code.
            var root = _setup.PreviewRoot;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null) continue;
                if (child.name == "UI Root") continue;
                DestroyImmediate(child.gameObject);
            }
        }
#endif

        private bool _pendingFirstPlayHide;

        private void Update()
        {
            // First-play defensive hide: edit-mode left every part active,
            // and the async-spawn/session-init ordering isn't reliable. Once
            // we have a package AND the spawner has populated its parts
            // list, deactivate every spawned part in one pass so the
            // subsequent RevealStepParts activates only the current step's
            // set. Runs exactly once per play session.
            if (!_pendingFirstPlayHide || !Application.isPlaying) return;
            if (_setup?.PreviewRoot == null) return;
            var pkg = _currentPackage ?? SessionDriver.CurrentPackage;
            // Fire as soon as the package is known. Don't gate on
            // _spawnedParts.Count — on the first play press the edit-mode
            // parts are already children of PreviewRoot with their partId
            // names, but _spawnedParts may still be empty until the play-
            // mode spawn cycle runs. We want to deactivate them NOW, before
            // the user sees the flash.
            if (pkg?.parts == null) return;

            var ids = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (var p in pkg.parts)
                if (p != null && !string.IsNullOrEmpty(p.id)) ids.Add(p.id);
            for (int i = _setup.PreviewRoot.childCount - 1; i >= 0; i--)
            {
                var child = _setup.PreviewRoot.GetChild(i);
                if (child == null) continue;
                if (!ids.Contains(child.name)) continue;
                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
            _pendingFirstPlayHide = false;
        }

        private void OnEnable()
        {
            if (Application.isPlaying) _pendingFirstPlayHide = true;
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

            // Post-domain-reload rehydration. Runs in BOTH edit and play mode.
            //
            // Domain reload wipes _spawnedParts (in-memory list) but every part
            // GO under PreviewRoot survives (serialized scene state). Without
            // this pass, every visibility-applying method that iterates
            // _spawnedParts (ApplyStepAwarePositions in editor, the reveal /
            // deactivate pass at runtime) sees an empty list and silently
            // no-ops — which is why TTAW step scrubbing "does nothing" after
            // a compile and why the first Play after compile leaks every part.
            //
            // Adopt the surviving children into _spawnedParts here. Subsequent
            // SpawnPackageParts calls will find them via PreviewRoot.Find and
            // skip re-instantiation. Editor callers then apply step visibility
            // against the full set; play-mode deactivation also covers them.
            if (_setup?.PreviewRoot != null)
            {
                Transform root = _setup.PreviewRoot;
                var partIdSet = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                var bootPkg = _currentPackage ?? SessionDriver.CurrentPackage;
                if (bootPkg?.parts != null)
                    foreach (var p in bootPkg.parts)
                        if (p != null && !string.IsNullOrEmpty(p.id)) partIdSet.Add(p.id);

                var trackedNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                for (int i = 0; i < _spawnedParts.Count; i++)
                    if (_spawnedParts[i] != null) trackedNames.Add(_spawnedParts[i].name);

                // First pass: evict stale transient GOs left over from the
                // pre-compile session. Domain reload wipes _subassemblyRoots
                // (PackagePartSpawner) and _fabricationGroupRoot
                // (AnimationCueCoordinator) — the C# fields reset to empty /
                // null, but the scene GameObjects survive. On the next step
                // activation, fresh Group_* / _AnimCue_* GOs get created
                // alongside the stale ones, which the user sees as duplicate
                // "frame cube" + "frame pieces" or static + animated copies
                // of the same target. Reparent their non-group children back
                // to PreviewRoot first so we don't destroy live parts along
                // with the stale wrapper, then destroy the wrapper itself.
                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    string nm = child.name;
                    if (nm == null) continue;
                    bool isOrphanGroup =
                        nm.StartsWith("Group_",                System.StringComparison.Ordinal) ||
                        nm.StartsWith("_AnimCue_FabGroup_",    System.StringComparison.Ordinal) ||
                        nm.StartsWith("_AnimCue_AnimGroup_",   System.StringComparison.Ordinal);
                    if (!isOrphanGroup) continue;

                    for (int c = child.childCount - 1; c >= 0; c--)
                    {
                        var inner = child.GetChild(c);
                        if (inner == null) continue;
                        inner.SetParent(root, worldPositionStays: true);
                    }
                    SafeDestroy(child.gameObject);
                }

                for (int i = root.childCount - 1; i >= 0; i--)
                {
                    Transform child = root.GetChild(i);
                    if (child == null) continue;
                    string nm = child.name;
                    if (nm == null) continue;
                    if (nm.StartsWith("EditGhost_", System.StringComparison.Ordinal))
                    {
                        Destroy(child.gameObject);
                        continue;
                    }
                    if (!partIdSet.Contains(nm)) continue;

                    // Adopt any part child not already tracked.
                    if (!trackedNames.Contains(nm))
                    {
                        _spawnedParts.Add(child.gameObject);
                        trackedNames.Add(nm);
                    }

                    // Play-mode: start every adopted part inactive. The reveal
                    // pass turns only the current step's parts back on. Edit
                    // mode leaves visibility to ApplyStepAwarePositions.
                    if (Application.isPlaying && child.gameObject.activeSelf)
                        child.gameObject.SetActive(false);
                }
            }

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
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
        }

        // ── Public API ──

        /// <summary>
        /// Finds the <see cref="PartPreviewPlacement"/> for a given part id from
        /// the current preview config. Returns null if not found.
        /// </summary>
        public PartPreviewPlacement FindPartPlacement(string partId)
            => _configLookup.FindPartPlacement(partId);

        /// <summary>
        /// Returns the step-scoped pose override for a part at a specific step,
        /// or null if no override exists (caller falls back to assembledPosition).
        /// </summary>
        public StepPoseEntry FindPartStepPose(string partId, string stepId)
            => _configLookup.FindPartStepPose(partId, stepId);

        /// <inheritdoc/>
        public void ClearGhosts() => _ghostManager?.Clear();

        /// <summary>
        /// Repositions existing spawned parts for step-aware preview in edit mode.
        /// Parts from steps before <paramref name="targetSequenceIndex"/> are shown
        /// at their assembledPosition; the current step's part at startPosition; future
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

            // All the visibility / stacking / integrated-member / "which pose
            // fires" questions we used to answer here are now baked into
            // pkg.poseTable by MachinePackageNormalizer. This method does two
            // things: look up the baked pose per part, and compute editor-only
            // visual state (ghosting + emission tint). Everything else was
            // deleted with Step 4 of the pose-system rewrite.
            var orderedSteps = pkg.GetOrderedSteps();
            int lastStepSeq  = orderedSteps.Length > 0 ? orderedSteps[orderedSteps.Length - 1].sequenceIndex : 0;
            bool fullyAssembled = targetSequenceIndex > lastStepSeq;

            // For fully-assembled view, read each part's pose at the last
            // ordered step — that's the committed post-build state.
            int lookupSeq = fullyAssembled ? lastStepSeq : targetSequenceIndex;

            // currentStepTaskParts drives the emission glow only. The pose
            // decision itself is PoseTable's problem.
            StepDefinition currentStepDef = null;
            for (int si = 0; si < orderedSteps.Length; si++)
            {
                if (orderedSteps[si] != null && orderedSteps[si].sequenceIndex == targetSequenceIndex)
                { currentStepDef = orderedSteps[si]; break; }
            }
            var currentStepTaskParts = new HashSet<string>(System.StringComparer.Ordinal);
            if (currentStepDef != null)
            {
                if (currentStepDef.requiredPartIds != null)
                    foreach (string pid in currentStepDef.requiredPartIds)
                        if (!string.IsNullOrEmpty(pid)) currentStepTaskParts.Add(pid);
                if (currentStepDef.optionalPartIds != null)
                    foreach (string pid in currentStepDef.optionalPartIds)
                        if (!string.IsNullOrEmpty(pid)) currentStepTaskParts.Add(pid);
            }

            var poseTable = pkg.poseTable;
            if (poseTable == null)
            {
                // Package wasn't run through MachinePackageNormalizer.Normalize;
                // nothing to render. Not an error — legit during intermediate
                // load states. Only log once per call chain, not once per part.
                foreach (var partGo in _spawnedParts)
                    if (partGo != null) partGo.SetActive(false);
                return;
            }

            // Subassembly ghost suppression was previously applied here, hiding
            // member parts behind a translucent ghost silhouette during a
            // stacking step. The editor authoring view doesn't apply this, so
            // suppressing at runtime made play diverge from what the author
            // sees in TTAW. Authoring is the source of truth for visibility —
            // every part the resolver places gets shown.
            var ghostedSubassemblyPartIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // Phase-A2 hierarchy parity: mirror TTAW's subassembly root GO
            // lifecycle so the runtime scene graph matches the authoring view.
            // Creates a root per visible group, reparents member parts, nests
            // aggregates, applies the step's working-orientation. Runs BEFORE
            // positioning so subsequent SetLocalPositionAndRotation calls
            // resolve against the correct parent transform.
            EnsureSubassemblyRoots(pkg, currentStepDef);

            foreach (var partGo in _spawnedParts)
            {
                if (partGo == null) continue;
                PartPreviewPlacement pp = FindPartPlacement(partGo.name);
                if (pp == null) continue;
                if (SplinePartFactory.HasSplineData(pp)) continue;

                // Ghost suppression still hides members that are replaced by a
                // translucent ghost silhouette.
                if (ghostedSubassemblyPartIds.Contains(partGo.name))
                {
                    partGo.SetActive(false);
                    continue;
                }

                // Visibility is authoritative: PoseTable.IsVisibleAt decides.
                // Future-step parts (no entry at lookupSeq, or explicitly
                // Hidden) are deactivated — no startPosition fallback for
                // them. This matches TTAW.SyncAllPartMeshesToActivePose which
                // hides future parts via TryGetStepAwarePose returning false.
                // Without this hide, domain-reload and step-scrub paths leak
                // every future part at its startPosition.
                if (!poseTable.IsVisibleAt(partGo.name, lookupSeq))
                {
                    if (partGo.activeSelf) partGo.SetActive(false);
                    continue;
                }

                Vector3 rpos; Quaternion rrot; Vector3 rscl;
                if (poseTable.TryGet(partGo.name, lookupSeq, out var resolution))
                {
                    rpos = resolution.pos; rrot = resolution.rot; rscl = resolution.scl;
                }
                else
                {
                    // Visible at this seq but no pose entry — use start pose
                    // as the display anchor (rare; e.g. partially-authored
                    // placements where IsVisibleAt is true but TryGet hasn't
                    // indexed a concrete pose).
                    rpos = new Vector3(pp.startPosition.x, pp.startPosition.y, pp.startPosition.z);
                    rrot = new Quaternion(pp.startRotation.x, pp.startRotation.y, pp.startRotation.z, pp.startRotation.w);
                    if (rrot.x == 0f && rrot.y == 0f && rrot.z == 0f && rrot.w == 0f) rrot = Quaternion.identity;
                    rscl = new Vector3(pp.startScale.x, pp.startScale.y, pp.startScale.z);
                    if (rscl.sqrMagnitude < 0.00001f) rscl = Vector3.one;
                }

                partGo.SetActive(true);
                partGo.transform.SetLocalPositionAndRotation(rpos, rrot);
                partGo.transform.localScale = rscl;

                if (!Application.isPlaying)
                {
                    // Emission glow on the parts the current step acts on —
                    // the same "this is the live task" cue as before.
                    bool isCurrentTask = currentStepTaskParts.Contains(partGo.name);
                    MaterialHelper.ClearTint(partGo);
                    MaterialHelper.SetEmission(partGo, isCurrentTask
                        ? InteractionVisualConstants.ActiveStepEmission
                        : Color.black);
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

            // Working orientation is intentionally NOT applied to group roots
            // here: baked poses on placements are PreviewRoot-space, so a
            // non-identity root would double-rotate every member. Working
            // orientation is an editor-only gizmo for authoring; runtime
            // renders from the baked poses directly.

            // ── Edit-mode ghost previews ──
            // Show translucent ghosts at the play (target) position for parts that
            // are currently at their start position (i.e. the parts being placed in
            // the active step). This mirrors what PreviewSpawnManager does at runtime.
            //
            // EditModeGhostManager still expects the legacy partStepSeq +
            // subassemblyParts maps (it uses them for ghost visibility timing,
            // not pose resolution). We rebuild small versions here; when the
            // ghost manager migrates to PoseTable these can go away.
            var ghostPartStepSeq   = new Dictionary<string, int>(System.StringComparer.Ordinal);
            var ghostSubassemblyParts = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var step in orderedSteps)
            {
                if (step == null) continue;
                int seq = step.sequenceIndex;
                void Note(string pid)
                {
                    if (string.IsNullOrEmpty(pid)) return;
                    if (!ghostPartStepSeq.TryGetValue(pid, out int cur) || seq < cur) ghostPartStepSeq[pid] = seq;
                }
                if (step.requiredPartIds != null) foreach (var p in step.requiredPartIds) Note(p);
                if (step.optionalPartIds != null) foreach (var p in step.optionalPartIds) Note(p);
                if (step.visualPartIds   != null) foreach (var p in step.visualPartIds)   Note(p);
                if (!string.IsNullOrEmpty(step.requiredSubassemblyId)
                    && pkg.TryGetSubassembly(step.requiredSubassemblyId, out var subDef)
                    && subDef?.partIds != null)
                {
                    foreach (var p in subDef.partIds) { Note(p); if (!string.IsNullOrEmpty(p)) ghostSubassemblyParts.Add(p); }
                }
            }
            _ghostManager?.SpawnGhosts(pkg, orderedSteps, targetSequenceIndex, fullyAssembled, ghostPartStepSeq, ghostSubassemblyParts);
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
        /// canonical assembled poses instead of individual <c>assembledPosition</c>.
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
            {
                // In play mode, start every part inactive — the reveal pass
                // (PartVisualFeedbackManager.RevealStepParts) activates only
                // the ones that belong to the current step. Without this, a
                // race between spawner-done and the first HideNonIntroduced-
                // Parts call leaves every part visible on the first play
                // press ("have to stop/play twice to see correct count").
                if (Application.isPlaying && showGeometry)
                    SetObjectActive(p, false);
                else
                    SetObjectActive(p, showGeometry);
            }
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

        /// <summary>
        /// Public entry for runtime callers (step activation, reveal pass) to
        /// refresh the subassembly root hierarchy. Creates/destroys Group_*
        /// roots, reparents visible members, nests aggregates. Roots stay at
        /// PreviewRoot origin + identity rotation — the baked poses on
        /// placements are PreviewRoot-space, so roots must NOT have a
        /// non-identity transform (would double-rotate members). Working
        /// orientation is an editor-gizmo authoring concept; at runtime the
        /// poses already encode whatever the author intended.
        /// </summary>
        public void SyncSubassemblyHierarchy(MachinePackageDefinition pkg, StepDefinition currentStep)
        {
            EnsureSubassemblyRoots(pkg, currentStep);
        }

        /// <summary>
        /// Returns the persistent Group_* root GameObject for a subassembly,
        /// or null if none exists (no visible members at the current step).
        /// Other systems — notably AnimationCueCoordinator — target this root
        /// so per-group animations play on the same hierarchy the user sees
        /// and grabs, rather than creating a parallel temporary parent.
        /// </summary>
        public GameObject GetSubassemblyRoot(string subassemblyId)
        {
            if (string.IsNullOrEmpty(subassemblyId)) return null;
            return _subassemblyRoots.TryGetValue(subassemblyId, out var go) ? go : null;
        }

        // ── Phase-A2 parity: subassembly root GO lifecycle ────────────────────
        //
        // Matches TTAW.PackageLoad.cs EnsureAllSubassemblyRoots. For every
        // subassembly with at least one visible member, we create a "Group_*"
        // GameObject under PreviewRoot (at origin, identity rotation),
        // reparent the member parts under it, nest aggregate→child groups,
        // and apply the current step's workingOrientation to the matching
        // root. When no longer needed, roots are destroyed and members move
        // back under PreviewRoot.
        //
        // Member localPositions remain authored in PreviewRoot space — the
        // root sits at origin/identity by default so reparenting is a no-op
        // geometrically. The working-orientation rotation is the ONLY thing
        // that sets a non-identity transform on the root, and that naturally
        // rotates all members via Unity parenting.
        private void EnsureSubassemblyRoots(MachinePackageDefinition pkg, StepDefinition step)
        {
            var previewRoot = _setup?.PreviewRoot;
            if (previewRoot == null || pkg == null)
            {
                DestroyAllSubassemblyRoots();
                return;
            }

            var allSubs = pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0)
            {
                DestroyAllSubassemblyRoots();
                return;
            }

            // A part is "visible" at the current step if the spawned GO is active.
            // (We run AFTER the spawner spawns all parts but BEFORE the per-part
            // positioning loop below — so active-self at this point reflects
            // prior step state. That's fine: we only care about membership for
            // the root, not the current pose.)
            var visiblePartIds = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var go in _spawnedParts)
                if (go != null) visiblePartIds.Add(go.name);

            var neededIds = new HashSet<string>(System.StringComparer.Ordinal);

            // Pass 1 — create a root per subassembly with any visible member.
            foreach (var sub in allSubs)
            {
                if (sub == null || string.IsNullOrEmpty(sub.id)) continue;

                bool hasVisibleMember = false;
                if (sub.partIds != null)
                {
                    foreach (var pid in sub.partIds)
                        if (!string.IsNullOrEmpty(pid) && visiblePartIds.Contains(pid))
                        { hasVisibleMember = true; break; }
                }
                if (!hasVisibleMember && sub.isAggregate && sub.memberSubassemblyIds != null)
                {
                    foreach (var childId in sub.memberSubassemblyIds)
                    {
                        if (string.IsNullOrEmpty(childId)) continue;
                        if (!pkg.TryGetSubassembly(childId, out var childSub) || childSub?.partIds == null) continue;
                        foreach (var pid in childSub.partIds)
                            if (!string.IsNullOrEmpty(pid) && visiblePartIds.Contains(pid))
                            { hasVisibleMember = true; break; }
                        if (hasVisibleMember) break;
                    }
                }
                if (!hasVisibleMember) continue;

                neededIds.Add(sub.id);

                bool freshRoot = false;
                if (!_subassemblyRoots.TryGetValue(sub.id, out var rootGO) || rootGO == null)
                {
                    // Protective adopt: if an orphan Group_* with this exact
                    // name already lives under PreviewRoot (leftover from a
                    // domain reload that wiped _subassemblyRoots), adopt it
                    // into the dict instead of creating a duplicate. Without
                    // this, the dict-miss path below would instantiate a
                    // new GO beside the orphan and the user sees two copies.
                    string expectedName = $"Group_{sub.GetDisplayName()}";
                    var existing = previewRoot.Find(expectedName);
                    if (existing != null)
                    {
                        rootGO = existing.gameObject;
                        _subassemblyRoots[sub.id] = rootGO;
                    }
                    else
                    {
                        rootGO = new GameObject(expectedName);
                        rootGO.transform.SetParent(previewRoot, false);
                        _subassemblyRoots[sub.id] = rootGO;
                        freshRoot = true;
                    }
                }
                rootGO.transform.localPosition = Vector3.zero;
                rootGO.transform.localRotation = Quaternion.identity;
                rootGO.transform.localScale    = Vector3.one;

                // Group root grab is only enabled when the current step's
                // task IS this subassembly (requiredSubassemblyId match, or a
                // target's associatedSubassemblyId match). Otherwise the group
                // is scene context — no-task — and must not be grabbable.
                // This prevents the user from dragging a past-placed group
                // like the carriage frame around by grabbing the root.
                bool groupIsTaskTarget = StepTargetsSubassembly(pkg, step, sub.id);
                if (freshRoot && Application.isPlaying && _xrGrabSetup != null)
                    EnsureGroupRootCollider(rootGO);
                if (Application.isPlaying && _xrGrabSetup != null)
                {
                    if (groupIsTaskTarget)
                        _xrGrabSetup.EnableGrab(rootGO);
                    else
                        _xrGrabSetup.SetGrabEnabled(rootGO, false);
                }

                // Reparent visible members (non-aggregates only — aggregates own
                // parts indirectly through child subassemblies).
                if (!sub.isAggregate && sub.partIds != null)
                {
                    foreach (var pid in sub.partIds)
                    {
                        if (string.IsNullOrEmpty(pid)) continue;
                        var memberGO = FindSpawnedGo(pid);
                        if (memberGO == null) continue;
                        if (memberGO.transform.parent != rootGO.transform)
                            memberGO.transform.SetParent(rootGO.transform, worldPositionStays: true);
                        // Members' grabs stay managed by
                        // PartVisualFeedbackManager.SyncPartGrabInteractivity,
                        // which gates on the active-step part set — NO-TASK
                        // members never get grab. The group root handles the
                        // whole-group grab when the group IS the step's task.
                        if (Application.isPlaying && groupIsTaskTarget)
                            _xrGrabSetup?.SetGrabEnabled(memberGO, false);
                    }
                }
            }

            // Pass 2 — nest child group roots under aggregate parent roots.
            foreach (var sub in allSubs)
            {
                if (sub == null || !sub.isAggregate || sub.memberSubassemblyIds == null) continue;
                if (!_subassemblyRoots.TryGetValue(sub.id, out var parentGO) || parentGO == null) continue;

                foreach (var childId in sub.memberSubassemblyIds)
                {
                    if (string.IsNullOrEmpty(childId)) continue;
                    if (!_subassemblyRoots.TryGetValue(childId, out var childGO) || childGO == null) continue;
                    if (childGO.transform.parent != parentGO.transform)
                        childGO.transform.SetParent(parentGO.transform, worldPositionStays: true);
                }
            }

            // Pass 3 — destroy roots no longer needed; move orphaned members back under PreviewRoot.
            List<string> toRemove = null;
            foreach (var kv in _subassemblyRoots)
                if (!neededIds.Contains(kv.Key))
                    (toRemove ??= new List<string>()).Add(kv.Key);

            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    if (!_subassemblyRoots.TryGetValue(id, out var go) || go == null)
                    { _subassemblyRoots.Remove(id); continue; }

                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                    {
                        var child = go.transform.GetChild(i);
                        child.SetParent(previewRoot, worldPositionStays: true);
                        // Re-enable the member's individual grab now that
                        // it's no longer under a group root.
                        if (Application.isPlaying)
                            _xrGrabSetup?.SetGrabEnabled(child.gameObject, true);
                    }
                    SafeDestroy(go);
                    _subassemblyRoots.Remove(id);
                }
            }
        }

        private static bool StepTargetsSubassembly(MachinePackageDefinition pkg, StepDefinition step, string subId)
        {
            if (step == null || string.IsNullOrEmpty(subId)) return false;
            if (string.Equals(step.requiredSubassemblyId, subId, System.StringComparison.Ordinal)) return true;
            if (pkg != null && step.targetIds != null)
            {
                foreach (var tid in step.targetIds)
                {
                    if (string.IsNullOrWhiteSpace(tid) || !pkg.TryGetTarget(tid, out var tgt) || tgt == null) continue;
                    if (string.Equals(tgt.associatedSubassemblyId, subId, System.StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private static void EnsureGroupRootCollider(GameObject root)
        {
            // XRGrabInteractable needs a collider to receive raycasts/hover.
            // A small invisible sphere at the root's pivot is enough — member
            // colliders still work for per-part snap/ray operations, but the
            // grab hit-test uses the root's collider so the whole group moves.
            if (root.GetComponent<Collider>() != null) return;
            var sc = root.AddComponent<SphereCollider>();
            sc.radius = 0.05f;
            sc.isTrigger = true;
        }

        private void DestroyAllSubassemblyRoots()
        {
            var previewRoot = _setup?.PreviewRoot;

            // Destroy the roots we're tracking in the dict.
            foreach (var kv in _subassemblyRoots)
            {
                var go = kv.Value;
                if (go == null) continue;
                if (previewRoot != null)
                {
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                    {
                        var child = go.transform.GetChild(i);
                        child.SetParent(previewRoot, worldPositionStays: true);
                        if (Application.isPlaying)
                            _xrGrabSetup?.SetGrabEnabled(child.gameObject, true);
                    }
                }
                SafeDestroy(go);
            }
            _subassemblyRoots.Clear();

            // Sweep any orphan Group_* / _AnimCue_* GOs that are NOT in the
            // dict — domain reload wipes _subassemblyRoots (and the
            // AnimationCueCoordinator's _fabricationGroupRoot), leaving the
            // scene GOs dangling. Without this sweep they survive into the
            // first play/spawn cycle and EnsureSubassemblyRoots creates
            // duplicates alongside them. Belt-and-suspenders: the OnEnable
            // rehydration does the same sweep, but that only covers the
            // enable path. This covers every call site (ClearSpawnedParts,
            // EnsureSubassemblyRoots, HandlePackageChanged).
            if (previewRoot != null)
            {
                for (int i = previewRoot.childCount - 1; i >= 0; i--)
                {
                    Transform child = previewRoot.GetChild(i);
                    if (child == null) continue;
                    string nm = child.name;
                    if (nm == null) continue;
                    bool isOrphanGroup =
                        nm.StartsWith("Group_",              System.StringComparison.Ordinal) ||
                        nm.StartsWith("_AnimCue_FabGroup_",  System.StringComparison.Ordinal) ||
                        nm.StartsWith("_AnimCue_AnimGroup_", System.StringComparison.Ordinal);
                    if (!isOrphanGroup) continue;

                    for (int c = child.childCount - 1; c >= 0; c--)
                    {
                        var inner = child.GetChild(c);
                        if (inner == null) continue;
                        inner.SetParent(previewRoot, worldPositionStays: true);
                        if (Application.isPlaying)
                            _xrGrabSetup?.SetGrabEnabled(inner.gameObject, true);
                    }
                    SafeDestroy(child.gameObject);
                }
            }
        }

        private GameObject FindSpawnedGo(string partId)
        {
            if (string.IsNullOrEmpty(partId)) return null;
            foreach (var go in _spawnedParts)
                if (go != null && go.name == partId) return go;
            return null;
        }

        private void ClearSpawnedParts()
        {
#if UNITY_EDITOR
            // If the inspector is showing one of the parts we're about to destroy,
            // clear the selection first so Unity's inspectors don't throw
            // MissingReferenceException / SerializedObjectNotCreatableException.
            if (!Application.isPlaying && UnityEditor.Selection.activeGameObject != null)
            {
                bool needsClear = false;
                foreach (var go in _spawnedParts)
                {
                    if (go == null) continue;
                    if (UnityEditor.Selection.activeGameObject == go ||
                        UnityEditor.Selection.activeGameObject.transform.IsChildOf(go.transform))
                    { needsClear = true; break; }
                }
                if (needsClear)
                {
                    // Wipe the full selection array (Selection.objects rather
                    // than activeGameObject alone — Unity's tracker inspects
                    // the whole array) AND force a synchronous rebuild so the
                    // cached m_Targets references are released before the
                    // GOs are destroyed below. Without this, the inspector's
                    // OnEnable fires after destroy with a dead target and
                    // throws MissingReferenceException.
                    UnityEditor.Selection.objects = System.Array.Empty<UnityEngine.Object>();
                    UnityEditor.ActiveEditorTracker.sharedTracker.ForceRebuild();
                }
            }
#endif
            _ghostManager?.Clear();
            DestroyAllSubassemblyRoots();
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
                if (child != null)
                    toDestroy.Add(child);
            }

            bool hadAny = toDestroy.Count > 0;
            foreach (var child in toDestroy)
            {
                if (child != null)
                    DestroyImmediate(child.gameObject);
            }

            if (hadAny)
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
