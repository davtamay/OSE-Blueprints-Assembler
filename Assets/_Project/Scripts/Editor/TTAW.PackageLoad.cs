using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OSE.App;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using OSE.Interaction;
using OSE.Runtime.Preview;
using OSE.UI.Root;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.PackageLoad.cs  —  Package list, LoadPkg, step/target/part builders,
//                         scene respawn, and tool-lookup helpers.
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Package loading ───────────────────────────────────────────────────

        private void RefreshPackageList()
        {
            string root = PackageJsonUtils.AuthoringRoot;
            if (!Directory.Exists(root)) { _packageIds = Array.Empty<string>(); return; }
            var dirs = Directory.GetDirectories(root);
            var ids  = new List<string>();
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "machine.json"))) ids.Add(Path.GetFileName(d));
            _packageIds = ids.ToArray();
        }

        private void LoadPkg(string id) => LoadPkg(id, restoring: false);

        private void LoadPkg(string id, bool restoring)
        {
            Cleanup();
            _pkg   = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            if (_pkg == null) return;
            _assetResolver.BuildCatalog(_pkgId, _pkg.parts ?? System.Array.Empty<PartDefinition>());

            // When restoring after domain reload, keep the serialized _stepFilterIdx.
            // Otherwise reset to "All Steps" and try to sync from SessionDriver.
            if (!restoring)
            {
                _stepFilterIdx = 0;
            }

            BuildStepOptions();
            BuildTargetToolMap();

            if (!restoring)
            {
                // Sync initial step from SessionDriver if present
                var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
                if (driver != null && _stepSequenceIdxs != null)
                {
                    int seq = driver.PreviewStepSequenceIndex;
                    for (int k = 1; k < _stepSequenceIdxs.Length; k++)
                    {
                        if (_stepSequenceIdxs[k] == seq) { _stepFilterIdx = k; break; }
                    }
                }
            }

            // Clamp in case stored index is out of range after package edit
            if (_stepOptions != null && _stepFilterIdx >= _stepOptions.Length)
                _stepFilterIdx = 0;

            UpdateActiveStep();
            BuildTargetList();
            BuildPartList();
            RespawnScene();
            SyncAllPartMeshesToActivePose(); // must come AFTER RespawnScene

            // Run the validation dashboard against the freshly-loaded package
            // so authors see the current health snapshot the moment the window
            // opens. (Phase 6 — see TTAW.Validation.cs.)
            RunValidation();
        }

        private void BuildStepOptions()
        {
            var optList  = new List<string> { "(All Steps)" };
            var idList   = new List<string> { null };
            var seqList  = new List<int>    { 0 };

            if (_pkg?.steps != null)
            {
                // Include ALL steps so session driver and authoring window always
                // navigate the same step index. Steps without targets show empty list.
                var allSteps = new List<StepDefinition>(_pkg.steps.Length);
                foreach (var step in _pkg.steps)
                    if (step != null) allSteps.Add(step);
                allSteps.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));

                foreach (var step in allSteps)
                {
                    string toolName = "(no tool)";
                    if (step.relevantToolIds != null && step.relevantToolIds.Length > 0 && _pkg.tools != null)
                    {
                        foreach (var td in _pkg.tools)
                        {
                            if (td != null && td.id == step.relevantToolIds[0])
                            { toolName = td.name; break; }
                        }
                    }

                    int targetCount    = step.targetIds?.Length ?? 0;
                    string profilePart = string.IsNullOrEmpty(step.profile) ? "" : $"  ·  {step.profile}";
                    string noTargets   = targetCount == 0 ? "  ·  (no targets)" : "";
                    string display     = $"[{step.sequenceIndex}] {step.name}  ·  {toolName}{profilePart}{noTargets}";
                    optList.Add(display);
                    idList.Add(step.id);
                    seqList.Add(step.sequenceIndex);
                }
            }

            _stepOptions      = optList.ToArray();
            _stepIds          = idList.ToArray();
            _stepSequenceIdxs = seqList.ToArray();
        }

        /// <summary>
        /// Builds a reverse map: targetId → display tool name, sourced from
        /// step.requiredToolActions[].toolId → ToolDefinition.name.
        /// First match wins (one tool per target is the common case).
        /// </summary>
        private void BuildTargetToolMap()
        {
            _targetToolMap    = new Dictionary<string, string>(StringComparer.Ordinal);
            _targetToolIdMap  = new Dictionary<string, string>(StringComparer.Ordinal);
            _toolActionTargetIds = new HashSet<string>(StringComparer.Ordinal);
            if (_pkg?.steps == null) return;

            foreach (var step in _pkg.steps)
            {
                if (step?.requiredToolActions == null) continue;
                foreach (var action in step.requiredToolActions)
                {
                    if (string.IsNullOrEmpty(action?.targetId) || string.IsNullOrEmpty(action.toolId)) continue;
                    _toolActionTargetIds.Add(action.targetId);
                    if (_targetToolMap.ContainsKey(action.targetId)) continue;

                    string toolName = action.toolId;   // fallback = raw id
                    if (_pkg.tools != null)
                        foreach (var td in _pkg.tools)
                            if (td != null && td.id == action.toolId) { toolName = td.name; break; }

                    _targetToolMap[action.targetId]   = toolName;
                    _targetToolIdMap[action.targetId] = action.toolId;
                }
            }
        }

        private void BuildTargetList()
        {
            if (_pkg?.targets == null) { _targets = Array.Empty<TargetEditState>(); return; }

            // Determine which targetIds to show.
            // Always assign a HashSet (even empty) when a step is selected — null means
            // "no filter" (All Steps mode) and would show every package target.
            HashSet<string> filterIds = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                string stepId = _stepIds[_stepFilterIdx];
                if (stepId != null)
                {
                    var step = FindStep(stepId);
                    filterIds = step?.targetIds != null && step.targetIds.Length > 0
                        ? new HashSet<string>(step.targetIds, StringComparer.Ordinal)
                        : new HashSet<string>(StringComparer.Ordinal);
                }
            }

            // Build a wire-entry lookup for the active step so wire targets get portA/portB
            // from the wire entry when they have no target placement.
            StepDefinition activeStep = _stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length
                ? FindStep(_stepIds[_stepFilterIdx]) : null;
            var wirePortByTargetId = new Dictionary<string, (Vector3 a, Vector3 b)>(StringComparer.Ordinal);
            if (activeStep?.wireConnect?.wires != null)
                foreach (var we in activeStep.wireConnect.wires)
                    if (we?.targetId != null)
                        wirePortByTargetId[we.targetId] = (
                            new Vector3(we.portA.x, we.portA.y, we.portA.z),
                            new Vector3(we.portB.x, we.portB.y, we.portB.z));

            var list = new List<TargetEditState>();
            foreach (var def in _pkg.targets)
            {
                if (def == null) continue;
                if (filterIds != null && !filterIds.Contains(def.id)) continue;

                TargetPreviewPlacement placement = FindPlacement(def.id);
                bool hasP = placement != null;

                // For unplaced targets, default position to the associated part's assembledPosition
                // so they appear ON the part in the SceneView rather than at world origin.
                Vector3 defaultPos = Vector3.zero;
                if (!hasP && !string.IsNullOrEmpty(def.associatedPartId))
                {
                    var pp = FindPartPlacement(def.associatedPartId);
                    if (pp != null) defaultPos = PackageJsonUtils.ToVector3(pp.assembledPosition);
                }

                // Port positions: target placement first, then wire entry fallback.
                Vector3 portA = hasP ? PackageJsonUtils.ToVector3(placement.portA) : Vector3.zero;
                Vector3 portB = hasP ? PackageJsonUtils.ToVector3(placement.portB) : Vector3.zero;
                if (portA == Vector3.zero && portB == Vector3.zero && wirePortByTargetId.TryGetValue(def.id, out var wp))
                { portA = wp.a; portB = wp.b; }

                var state = new TargetEditState
                {
                    def                     = def,
                    placement               = placement,
                    hasPlacement            = hasP,
                    position                = hasP ? PackageJsonUtils.ToVector3(placement.position)         : defaultPos,
                    rotation                = hasP ? PackageJsonUtils.ToUnityQuaternion(placement.rotation)  : Quaternion.identity,
                    scale                   = hasP ? PackageJsonUtils.ToVector3(placement.scale)             : Vector3.one * DefaultTargetScale,
                    portA                   = portA,
                    portB                   = portB,
                    weldAxis                = def.GetWeldAxisVector(),
                    weldLength              = def.weldLength,
                    useToolActionRotation   = def.useToolActionRotation,
                    toolActionRotationEuler = new Vector3(def.toolActionRotation.x, def.toolActionRotation.y, def.toolActionRotation.z),
                    isDirty                 = false,
                };
                list.Add(state);
            }

            // Preserve selection across rebuilds by matching target ID.
            // _selectedTargetId is serialized, so it survives domain reload even when _targets is null.
            string prevSelectedId = (_selectedIdx >= 0 && _targets != null && _selectedIdx < _targets.Length)
                ? _targets[_selectedIdx].def.id : _selectedTargetId;

            _targets     = list.ToArray();
            _selectedIdx = -1;
            if (prevSelectedId != null)
            {
                for (int i = 0; i < _targets.Length; i++)
                    if (_targets[i].def.id == prevSelectedId) { _selectedIdx = i; break; }
            }
            if (_selectedIdx < 0 && _targets.Length > 0) _selectedIdx = 0;
            _selectedTargetId = (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                ? _targets[_selectedIdx].def.id : null;
            _multiSelected.Clear();
            if (_selectedIdx >= 0) RefreshToolPreview(ref _targets[_selectedIdx]);
            else ClearToolPreview();
        }

        private TargetPreviewPlacement FindPlacement(string targetId)
        {
            var arr = _pkg?.previewConfig?.targetPlacements;
            if (arr == null) return null;
            foreach (var p in arr)
                if (p != null && p.targetId == targetId) return p;
            return null;
        }

        /// <summary>
        /// For every part in the package that has no <c>assetRef</c>, attempts to find
        /// a GLB in the parts folder whose stem (after stripping _approved / _mesh) matches
        /// the part ID. Matched parts are written to machine.json immediately.
        /// Reports how many were linked and how many still need manual assignment.
        /// </summary>
        private void AutoLinkPartsByFilename()
        {
            if (_pkg?.parts == null || string.IsNullOrEmpty(_pkgId)) return;

            int linked = 0, skipped = 0;
            foreach (var def in _pkg.parts)
            {
                if (def == null || !string.IsNullOrEmpty(def.assetRef)) continue;
                var res = _assetResolver.Resolve(def.id);
                if (res.IsResolved)
                {
                    def.assetRef = Path.GetFileName(res.AssetPath);
                    _dirtyPartAssetRefIds.Add(def.id);
                    linked++;
                }
                else
                {
                    skipped++;
                }
            }

            if (linked > 0)
            {
                // Rebuild part states so the new assetRefs take effect in the scene
                BuildPartList();
                RespawnScene();
                SyncAllPartMeshesToActivePose();
                WriteJson();
                Debug.Log($"[PartAutoLink] Linked {linked} parts by filename. " +
                          $"{skipped} still need manual assetRef (shared/renamed meshes).");
            }
            else
            {
                Debug.Log($"[PartAutoLink] No filename matches found. " +
                          $"{skipped} parts need manual assetRef assignment via the Model Asset field.");
            }
        }

        /// <summary>
        /// Returns the effective asset filename for a part: explicit <c>assetRef</c> if set,
        /// otherwise resolved via all 3 resolver passes (filename match + GLB node search).
        /// Returns null when neither is available.
        /// </summary>
        private string ResolvePartAssetRef(PartDefinition def)
        {
            if (def == null) return null;
            if (!string.IsNullOrEmpty(def.assetRef)) return def.assetRef;
            var res = _assetResolver.Resolve(def.id);
            return res.IsResolved ? Path.GetFileName(res.AssetPath) : null;
        }

        private PartPreviewPlacement FindPartPlacement(string partId)
        {
            var arr = _pkg?.previewConfig?.partPlacements;
            if (arr == null) return null;
            foreach (var p in arr)
                if (p != null && p.partId == partId) return p;
            return null;
        }

        private StepDefinition FindStep(string stepId)
        {
            if (_pkg?.steps == null) return null;
            foreach (var s in _pkg.steps)
                if (s != null && s.id == stepId) return s;
            return null;
        }

        /// <summary>
        /// Returns the ToolDefinition for the first ToolActionDefinition that references
        /// <paramref name="targetId"/>. Used during one-time migration to mesh-rotation format.
        /// </summary>
        private ToolDefinition FindToolForTarget(string targetId)
        {
            if (_pkg?.steps == null || _pkg.tools == null) return null;
            foreach (var step in _pkg.steps)
            {
                if (step?.requiredToolActions == null) continue;
                foreach (var action in step.requiredToolActions)
                {
                    if (action?.targetId != targetId || string.IsNullOrEmpty(action.toolId)) continue;
                    foreach (var tool in _pkg.tools)
                        if (tool?.id == action.toolId) return tool;
                }
            }
            return null;
        }

        private string FindToolName(string toolId)
        {
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) return td.name;
            return toolId; // fallback to raw id
        }

        /// <summary>
        /// Returns the set of persistent-tool IDs that are "live" immediately before
        /// <paramref name="atStep"/> starts — i.e. placed by a prior step and not yet
        /// explicitly removed by an intermediate step's removePersistentToolIds.
        /// </summary>
        private HashSet<string> GetActivePersistentToolIds(StepDefinition atStep)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            if (_pkg?.steps == null) return active;

            // Walk steps in sequence order up to (but not including) atStep
            foreach (var s in _pkg.steps)
            {
                if (s == null || s.sequenceIndex >= atStep.sequenceIndex) continue;

                // Any tool action that uses a persistent tool adds it
                if (s.requiredToolActions != null)
                    foreach (var action in s.requiredToolActions)
                    {
                        if (string.IsNullOrEmpty(action?.toolId)) continue;
                        if (IsToolPersistent(action.toolId)) active.Add(action.toolId);
                    }

                // Explicit removals from this intermediate step clean it up
                if (s.removePersistentToolIds != null)
                    foreach (var id in s.removePersistentToolIds)
                        active.Remove(id);
            }

            return active;
        }

        private bool IsToolPersistent(string toolId)
        {
            if (_pkg?.tools != null)
                foreach (var td in _pkg.tools)
                    if (td != null && td.id == toolId) return td.persistent;
            return false;
        }

        // ── Scene setup ────────────────────────────────────────────────────────

        private void RespawnScene()
        {
            // No hidden preview root — we work directly with the live spawned parts.
            // Just compute the step-aware context cache and position live parts accordingly.
            if (_pkg?.previewConfig?.partPlacements == null) return;

            _previewAssembled = 0;
            _previewCurrent   = 0;
            _previewHidden    = 0;

            bool stepSelected = _stepFilterIdx > 0 && _stepIds != null
                                && _stepFilterIdx < _stepIds.Length
                                && _stepIds[_stepFilterIdx] != null;

            int currentSeq  = int.MaxValue;
            var partStepSeq = new Dictionary<string, int>(StringComparer.Ordinal);
            var currentStepSubassemblyPartIds = new HashSet<string>(StringComparer.Ordinal);

            if (stepSelected && _pkg.steps != null)
            {
                var sel = FindStep(_stepIds[_stepFilterIdx]);
                if (sel != null) currentSeq = sel.sequenceIndex;

                if (sel != null && !string.IsNullOrEmpty(sel.requiredSubassemblyId)
                    && _pkg.TryGetSubassembly(sel.requiredSubassemblyId, out SubassemblyDefinition curSubDef)
                    && curSubDef?.partIds != null)
                {
                    foreach (string pid in curSubDef.partIds)
                        if (!string.IsNullOrEmpty(pid)) currentStepSubassemblyPartIds.Add(pid);
                }

                foreach (var step in _pkg.steps)
                {
                    if (step?.requiredPartIds != null)
                    {
                        foreach (string pid in step.requiredPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }

                    // visualPartIds — same visibility timing as requiredPartIds
                    if (step?.visualPartIds != null)
                    {
                        foreach (string pid in step.visualPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }
                    // optionalPartIds — same visibility as requiredPartIds
                    if (step?.optionalPartIds != null)
                    {
                        foreach (string pid in step.optionalPartIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }

                    if (!string.IsNullOrEmpty(step?.requiredSubassemblyId)
                        && _pkg.TryGetSubassembly(step.requiredSubassemblyId, out SubassemblyDefinition subDef)
                        && subDef?.partIds != null)
                    {
                        foreach (string pid in subDef.partIds)
                        {
                            if (string.IsNullOrEmpty(pid)) continue;
                            if (!partStepSeq.ContainsKey(pid) || step.sequenceIndex < partStepSeq[pid])
                                partStepSeq[pid] = step.sequenceIndex;
                        }
                    }
                }
            }

            // Cache working orientation for TryGetStepAwarePose editor preview
            StepWorkingOrientationPayload wo = null;
            Vector3 subFramePos = Vector3.zero;
            var woParts = new HashSet<string>(StringComparer.Ordinal);
            if (stepSelected)
            {
                var sel = FindStep(_stepIds[_stepFilterIdx]);
                if (sel?.workingOrientation != null && !string.IsNullOrWhiteSpace(sel.subassemblyId))
                {
                    wo = sel.workingOrientation;
                    // Collect all parts belonging to this subassembly
                    if (_pkg.TryGetSubassembly(sel.subassemblyId, out SubassemblyDefinition woSubDef)
                        && woSubDef?.partIds != null)
                    {
                        foreach (string pid in woSubDef.partIds)
                            if (!string.IsNullOrEmpty(pid)) woParts.Add(pid);
                    }
                    // Look up subassembly frame center for rotation pivot
                    if (_pkg.previewConfig?.subassemblyPlacements != null)
                    {
                        foreach (var sp in _pkg.previewConfig.subassemblyPlacements)
                        {
                            if (sp != null && string.Equals(sp.subassemblyId, sel.subassemblyId, System.StringComparison.OrdinalIgnoreCase))
                            {
                                subFramePos = new Vector3(sp.position.x, sp.position.y, sp.position.z);
                                break;
                            }
                        }
                    }
                }
            }

            _sceneBuildStepActive          = stepSelected;
            _sceneBuildCurrentSeq          = currentSeq;
            _sceneBuildPartStepSeq         = partStepSeq;
            _sceneBuildCurrentSubassembly  = currentStepSubassemblyPartIds;
            _sceneBuildWorkingOrientation      = wo;
            _sceneBuildSubassemblyFramePos     = subFramePos;
            _sceneBuildWorkingOrientationParts = woParts;

            // ── Phase A2: subassembly root GOs + reparenting ──────────────────
            // Create a root GO for EVERY group that has visible members in this
            // step — not just the step's own subassemblyId. This means the
            // Hierarchy shows all groups and their member parts as children.
            EnsureAllSubassemblyRoots(stepSelected ? FindStep(_stepIds[_stepFilterIdx]) : null);

            // Position and show/hide live parts based on step-aware context.
            SyncAllPartMeshesToActivePose();

            // Capture fallback positions: parts that have no placement data
            // (hasPlacement == false) but are visible in the scene get their
            // positions captured from the spawner's live GO. This ensures they
            // persist correctly when navigating to the next step.
            CaptureUnplacedPartPositions();

            // Add MeshColliders to live parts so click-to-snap works on their surfaces.
            AddMeshCollidersToLiveParts();

            // Refresh tool preview using the spawner's PreviewRoot as coordinate space.
            if (_selectedIdx >= 0 && _selectedIdx < _targets.Length)
                RefreshToolPreview(ref _targets[_selectedIdx]);
        }

        /// <summary>
        /// Clears the Unity editor selection if any of the supplied objects (or their
        /// children) are currently selected, then forces all open inspectors to rebuild
        /// so they release stale references before <see cref="DestroyImmediate"/> runs.
        /// </summary>
        private static void DeselectIfSelected(params UnityEngine.Object[] objects)
        {
            if (objects == null || objects.Length == 0) return;
            var sel = UnityEditor.Selection.objects;
            if (sel == null || sel.Length == 0) return;

            bool needsClear = false;
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                if (System.Array.IndexOf(sel, obj) >= 0) { needsClear = true; break; }
                if (obj is GameObject go)
                {
                    foreach (var s in sel)
                        if (s is GameObject sg && sg != null && sg.transform.IsChildOf(go.transform))
                        { needsClear = true; break; }
                }
                if (needsClear) break;
            }

            if (!needsClear) return;

            // Wipe the full selection array (more thorough than activeObject = null).
            UnityEditor.Selection.objects = System.Array.Empty<UnityEngine.Object>();
            // Force all open inspectors to rebuild synchronously so they release the
            // stale m_Targets references before DestroyImmediate executes.
            UnityEditor.ActiveEditorTracker.sharedTracker.ForceRebuild();
        }

        // ── Phase A2: subassembly root GO lifecycle ───────────────────────────

        /// <summary>
        /// Creates a root GO for every group (subassembly) that has at least one
        /// visible member part in the current step. Each visible part is parented
        /// under its group's root. Parts not in any group stay under PreviewRoot.
        /// </summary>
        private void EnsureAllSubassemblyRoots(StepDefinition step)
        {
            var previewRoot = GetPreviewRoot();
            if (previewRoot == null || _pkg == null)
            {
                DestroyAllSubassemblyRoots();
                return;
            }

            var allSubs = _pkg.GetSubassemblies();
            if (allSubs == null || allSubs.Length == 0)
            {
                DestroyAllSubassemblyRoots();
                return;
            }

            // Collect all visible part IDs (from the scene-build cache)
            var visibleParts = _sceneBuildPartStepSeq != null
                ? new HashSet<string>(_sceneBuildPartStepSeq.Keys, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            // Track which roots we need this frame
            var neededIds = new HashSet<string>(StringComparer.Ordinal);

            // Pass 1: create root GOs for groups that have ANY visible member
            // parts in this step — matching what the runtime spawner shows.
            // This keeps 1:1 consistency between editor and play mode.
            foreach (var sub in allSubs)
            {
                if (sub == null) continue;

                bool hasVisibleMember = false;
                if (sub.partIds != null)
                {
                    foreach (var pid in sub.partIds)
                    {
                        if (!string.IsNullOrEmpty(pid) && visibleParts.Contains(pid))
                        { hasVisibleMember = true; break; }
                    }
                }
                if (!hasVisibleMember && sub.isAggregate && sub.memberSubassemblyIds != null)
                {
                    foreach (var childId in sub.memberSubassemblyIds)
                    {
                        if (string.IsNullOrEmpty(childId)) continue;
                        if (!_pkg.TryGetSubassembly(childId, out var childSub) || childSub?.partIds == null) continue;
                        foreach (var pid in childSub.partIds)
                        {
                            if (!string.IsNullOrEmpty(pid) && visibleParts.Contains(pid))
                            { hasVisibleMember = true; break; }
                        }
                        if (hasVisibleMember) break;
                    }
                }

                if (!hasVisibleMember) continue;

                neededIds.Add(sub.id);

                // Create or reuse root GO
                if (!_subassemblyRootGOs.TryGetValue(sub.id, out var rootGO) || rootGO == null)
                {
                    rootGO = new GameObject($"Group_{sub.GetDisplayName()}");
                    rootGO.hideFlags = HideFlags.DontSave;
                    rootGO.transform.SetParent(previewRoot, false);
                    rootGO.transform.localPosition = Vector3.zero;
                    rootGO.transform.localRotation = Quaternion.identity;
                    rootGO.transform.localScale    = Vector3.one;
                    _subassemblyRootGOs[sub.id] = rootGO;
                }

                // Group roots always sit at the PreviewRoot origin with identity
                // rotation. Authored part positions are already in PreviewRoot
                // space, so any non-zero root position would cause a double-offset.
                // Working orientation is the ONLY thing that should set a non-identity
                // transform on the root, and it's applied via the gizmo (Phase A3).
                rootGO.transform.localPosition = Vector3.zero;
                rootGO.transform.localRotation = Quaternion.identity;

                // Parent visible member parts under this root (non-aggregates only —
                // aggregates own parts indirectly through child groups).
                if (!sub.isAggregate && sub.partIds != null)
                {
                    var memberSet = new HashSet<string>(sub.partIds, StringComparer.Ordinal);
                    ReparentMembersUnderRoot(rootGO.transform, previewRoot, memberSet);
                }
            }

            // Pass 2: nest child group roots under their parent aggregate roots.
            // This gives the real hierarchy: rotating a parent group rotates all
            // child groups and their parts (free via Unity parenting).
            foreach (var sub in allSubs)
            {
                if (sub == null || !sub.isAggregate || sub.memberSubassemblyIds == null) continue;
                if (!_subassemblyRootGOs.TryGetValue(sub.id, out var parentGO) || parentGO == null) continue;

                foreach (var childId in sub.memberSubassemblyIds)
                {
                    if (string.IsNullOrEmpty(childId)) continue;
                    if (!_subassemblyRootGOs.TryGetValue(childId, out var childGO) || childGO == null) continue;
                    if (childGO.transform.parent != parentGO.transform)
                        childGO.transform.SetParent(parentGO.transform, worldPositionStays: true);
                }
            }

            // Destroy roots that are no longer needed
            var toRemove = new List<string>();
            foreach (var kvp in _subassemblyRootGOs)
            {
                if (!neededIds.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }
            foreach (var id in toRemove)
            {
                if (_subassemblyRootGOs.TryGetValue(id, out var go) && go != null)
                {
                    for (int i = go.transform.childCount - 1; i >= 0; i--)
                        go.transform.GetChild(i).SetParent(previewRoot, worldPositionStays: true);
                    DestroyImmediate(go);
                }
                _subassemblyRootGOs.Remove(id);
            }
        }

        private Vector3 GetSubassemblyFramePos(string subId)
        {
            if (_pkg?.previewConfig?.subassemblyPlacements != null)
            {
                foreach (var sp in _pkg.previewConfig.subassemblyPlacements)
                {
                    if (sp != null && string.Equals(sp.subassemblyId, subId, System.StringComparison.OrdinalIgnoreCase))
                        return new Vector3(sp.position.x, sp.position.y, sp.position.z);
                }
            }
            return Vector3.zero;
        }

        private void ReparentMembersUnderRoot(
            Transform rootTransform,
            Transform previewRoot,
            HashSet<string> memberPartIds)
        {
            // Try the spawner service first; fall back to walking the scene
            // (same fallback pattern as FindLivePartGO).
            var spawnedParts = ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner)
                ? spawner?.SpawnedParts : null;
            List<GameObject> parts = null;
            if (spawnedParts != null && spawnedParts.Count > 0)
            {
                parts = new List<GameObject>(spawnedParts);
            }

            if (parts == null || parts.Count == 0)
            {
                // Fallback: collect all children of PreviewRoot + all group roots
                parts = new List<GameObject>();
                if (previewRoot != null)
                {
                    for (int i = 0; i < previewRoot.childCount; i++)
                    {
                        var child = previewRoot.GetChild(i);
                        if (child == null) continue;
                        // Direct children of PreviewRoot (ungrouped parts)
                        if (!child.name.StartsWith("Group_"))
                            parts.Add(child.gameObject);
                        // Children of group roots (already-grouped parts)
                        for (int j = 0; j < child.childCount; j++)
                        {
                            var grandchild = child.GetChild(j);
                            if (grandchild != null && !grandchild.name.StartsWith("Group_"))
                                parts.Add(grandchild.gameObject);
                        }
                    }
                }
            }

            foreach (var go in parts)
            {
                if (go == null) continue;
                bool isMember = memberPartIds.Contains(go.name);
                if (isMember && go.transform.parent != rootTransform)
                    go.transform.SetParent(rootTransform, worldPositionStays: true);
                else if (!isMember && go.transform.parent == rootTransform)
                    go.transform.SetParent(previewRoot, worldPositionStays: true);
            }
        }

        private static void ApplySubassemblyRootOrientation(
            Transform rootTransform,
            StepWorkingOrientationPayload wo,
            Vector3 subFramePos)
        {
            if (wo == null)
            {
                rootTransform.localRotation = Quaternion.identity;
                rootTransform.localPosition = subFramePos;
                return;
            }

            bool hasRot = wo.subassemblyRotation.x != 0f
                       || wo.subassemblyRotation.y != 0f
                       || wo.subassemblyRotation.z != 0f;

            rootTransform.localRotation = hasRot
                ? Quaternion.Euler(wo.subassemblyRotation.x, wo.subassemblyRotation.y, wo.subassemblyRotation.z)
                : Quaternion.identity;

            Vector3 offset = Vector3.zero;
            if (wo.subassemblyPositionOffset.x != 0f
                || wo.subassemblyPositionOffset.y != 0f
                || wo.subassemblyPositionOffset.z != 0f)
                offset = new Vector3(wo.subassemblyPositionOffset.x, wo.subassemblyPositionOffset.y, wo.subassemblyPositionOffset.z);

            rootTransform.localPosition = subFramePos + offset;
        }

        /// <summary>
        /// Unparents all member parts and destroys all root GOs. Safe to call
        /// when no roots exist.
        /// </summary>
        private void DestroyAllSubassemblyRoots()
        {
            var previewRoot = GetPreviewRoot();
            // Unparent all spawned parts back to PreviewRoot first (regardless of
            // nesting depth), then destroy all root GOs.
            if (ServiceRegistry.TryGet<ISpawnerQueryService>(out var spawner) && spawner?.SpawnedParts != null)
            {
                foreach (var go in spawner.SpawnedParts)
                {
                    if (go != null && go.transform.parent != previewRoot)
                        go.transform.SetParent(previewRoot, worldPositionStays: true);
                }
            }
            foreach (var kvp in _subassemblyRootGOs)
            {
                if (kvp.Value != null)
                    DestroyImmediate(kvp.Value);
            }
            _subassemblyRootGOs.Clear();
        }
    }
}
