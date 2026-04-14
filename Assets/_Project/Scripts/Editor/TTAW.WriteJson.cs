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
// TTAW.WriteJson.cs  —  AnyDirty, SyncAllToolRotations, WriteJson, RevertFromBackup,
//                       RevertAllChanges, ExtractFromGlbAnchors, and the JSON
//                       injection helpers (TryInjectBlock, TryRemoveBlock, etc.).
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Write to JSON ─────────────────────────────────────────────────────

        /// <summary>
        /// Mirrors the runtime validator's "single Place owner per partId"
        /// rule but runs at save-time so the author never persists a
        /// conflicting state. Returns true when at least one part appears
        /// in <c>requiredPartIds</c> of two or more <c>Place</c>-family steps;
        /// <paramref name="message"/> describes the first offending part.
        /// </summary>
        /// <summary>
        /// Resolves every Place-family-ownership collision by keeping the
        /// part in the step with the lowest <c>sequenceIndex</c> and removing
        /// it from every later Place step's <c>requiredPartIds</c>. Those
        /// later steps get marked dirty so the removal persists. Returns the
        /// number of (partId, step) entries removed.
        ///
        /// Rule: the physically-first Place step wins. If the author authored
        /// an out-of-order conflict they can still hand-edit after the fix.
        /// </summary>
        private int AutoResolvePlaceOwnershipConflicts()
        {
            if (_pkg?.steps == null) return 0;
            var owners = new Dictionary<string, List<StepDefinition>>(StringComparer.Ordinal);
            foreach (var s in _pkg.steps)
            {
                if (s == null || s.requiredPartIds == null) continue;
                if (s.ResolvedFamily != StepFamily.Place) continue;
                foreach (var pid in s.requiredPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!owners.TryGetValue(pid, out var list))
                        owners[pid] = list = new List<StepDefinition>();
                    list.Add(s);
                }
            }

            int removals = 0;
            foreach (var kvp in owners)
            {
                if (kvp.Value.Count <= 1) continue;
                kvp.Value.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));
                // First (lowest seq) keeps the part; strip from the rest.
                for (int i = 1; i < kvp.Value.Count; i++)
                {
                    var later = kvp.Value[i];
                    if (later.requiredPartIds == null) continue;
                    var rem = new List<string>(later.requiredPartIds);
                    if (rem.Remove(kvp.Key))
                    {
                        later.requiredPartIds = rem.Count > 0 ? rem.ToArray() : Array.Empty<string>();
                        _dirtyStepIds.Add(later.id);
                        removals++;
                        Debug.LogWarning($"[TTAW] Auto-fix: removed '{kvp.Key}' from requiredPartIds of Place step '{later.id}' (kept in earlier step '{kvp.Value[0].id}').");
                    }
                }
            }
            return removals;
        }

        private bool TryFindPlaceOwnershipConflict(out string message)
        {
            message = null;
            if (_pkg?.steps == null) return false;
            var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var s in _pkg.steps)
            {
                if (s == null || s.requiredPartIds == null) continue;
                if (s.ResolvedFamily != StepFamily.Place) continue;
                foreach (var pid in s.requiredPartIds)
                {
                    if (string.IsNullOrEmpty(pid)) continue;
                    if (!owners.TryGetValue(pid, out var list))
                        owners[pid] = list = new List<string>();
                    list.Add(s.id);
                }
            }
            foreach (var kvp in owners)
            {
                if (kvp.Value.Count <= 1) continue;
                message = $"partId '{kvp.Key}' is in requiredPartIds of multiple Place-family steps:\n  • " + string.Join("\n  • ", kvp.Value);
                return true;
            }
            return false;
        }

        private bool AnyDirty()
        {
            if (_dirtyToolIds.Count > 0 || _dirtyStepIds.Count > 0) return true;
            if (_dirtyTaskOrderStepIds.Count > 0) return true;
            if (_dirtyPartAssetRefIds.Count > 0)  return true;
            if (_dirtyPartToolIds.Count > 0)      return true;
            if (_dirtySubassemblyIds.Count > 0)   return true;
            if (_targets != null) foreach (var t in _targets) if (t.isDirty) return true;
            if (_parts   != null) foreach (var p in _parts)   if (p.isDirty) return true;
            return false;
        }

        /// <summary>
        /// Re-derives toolActionRotation for ALL targets from their placement.rotation quaternions.
        /// Fixes any Euler-convention mismatch introduced by external (Python) migration scripts.
        /// Does not require targets to be dirty — processes the entire previewConfig.targetPlacements array.
        /// </summary>
        private void SyncAllToolRotationsFromPlacements()
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null) return;
            if (_pkg.previewConfig?.targetPlacements == null || _pkg.previewConfig.targetPlacements.Length == 0)
            {
                Debug.LogWarning("[ToolTargetAuthoring] SyncAllToolRotations: no targetPlacements in previewConfig.");
                return;
            }

            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[ToolTargetAuthoring] machine.json not found for '{_pkgId}'"); return; }

            bool   isSplit2     = PackageJsonUtils.IsSplitLayout(_pkgId);
            var    rotContents  = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!isSplit2) rotContents[jsonPath] = File.ReadAllText(jsonPath);

            void InjectRot(string entityId, string field, string value)
            {
                string fp = isSplit2 ? PackageJsonUtils.FindEntityFilePath(_pkgId, entityId) : jsonPath;
                if (fp == null) return;
                if (!rotContents.TryGetValue(fp, out string cnt))
                    rotContents[fp] = cnt = File.ReadAllText(fp);
                TryInjectBlock(ref cnt, entityId, field, value);
                rotContents[fp] = cnt;
            }

            Transform pr = GetPreviewRoot();
            Quaternion rootRot = pr != null ? pr.rotation : Quaternion.identity;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            int count = 0;

            foreach (var p in _pkg.previewConfig.targetPlacements)
            {
                if (p == null || string.IsNullOrEmpty(p.targetId)) continue;
                var sq = p.rotation;
                Quaternion localRot = new Quaternion(sq.x, sq.y, sq.z, sq.w);
                Vector3 worldEuler = (rootRot * localRot).eulerAngles;
                InjectRot(p.targetId, "useToolActionRotation", "true");
                string tarJson = $"{{ \"x\": {R(worldEuler.x).ToString(inv)}, \"y\": {R(worldEuler.y).ToString(inv)}, \"z\": {R(worldEuler.z).ToString(inv)} }}";
                InjectRot(p.targetId, "toolActionRotation", tarJson);
                count++;
            }

            if (!isSplit2 && rotContents.TryGetValue(jsonPath, out string rotResult))
            {
                try { JsonUtility.FromJson<MachinePackageDefinition>(rotResult); }
                catch (Exception ex)
                {
                    Debug.LogError($"[ToolTargetAuthoring] SyncAllToolRotations: result would be invalid JSON, aborting.\n{ex.Message}");
                    return;
                }
            }

            string ts2 = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string firstRotBackup = null;
            foreach (var kv in rotContents)
            {
                string fp = kv.Key; string content = kv.Value;
                string backupDir2 = Path.Combine(Path.GetDirectoryName(fp)!, ".pose_backups");
                Directory.CreateDirectory(backupDir2);
                string bak2 = Path.Combine(backupDir2, $"{Path.GetFileNameWithoutExtension(fp)}_syncrot_{ts2}.json");
                File.Copy(fp, bak2, true);
                if (firstRotBackup == null) firstRotBackup = bak2;
                File.WriteAllText(fp, content);
            }
            _lastBackupPath = firstRotBackup;

            AssetDatabase.Refresh();
            PackageSyncTool.Sync();
            Debug.Log($"[ToolTargetAuthoring] SyncAllToolRotations: updated toolActionRotation for {count} targets (backup: {firstRotBackup}).");

            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildTargetList();
        }

        private void WriteJson() => WriteJson(reloadAfter: true);

        /// <summary>
        /// Writes all dirty items to machine.json. When <paramref name="reloadAfter"/> is true
        /// (default), reloads the package and respawns the scene for a clean-slate state. When
        /// false, keeps the current in-memory state and only syncs dirty flags — use this for
        /// per-task saves so the user's editing flow is not interrupted.
        /// </summary>
        private void WriteJson(bool reloadAfter)
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null || _targets == null) return;
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[ToolTargetAuthoring] machine.json not found for '{_pkgId}'"); return; }

            // Guarantee the drift-free invariant at write time: every step's
            // taskOrder is a projection of its role arrays. Mutators already
            // reconcile as they write, but this pass catches any path that
            // touches step data without going through the reconciler (e.g.
            // external editors, batch edits, or future code). Cheap — only
            // mutates steps that actually drifted.
            ReconcileAllStepTaskOrders();

            // Multi-Place is supported — no save-time block. The runtime
            // validator still reports multi-owner partIds but as a Warning
            // (see PartOwnershipExclusivityPass.CheckMultiPlaceStepCollisions).
            // Log an info line for discoverability so the author can verify
            // they meant to author multiple placements.
            if (TryFindPlaceOwnershipConflict(out string multiPlaceSummary))
            {
                Debug.Log($"[TTAW] {multiPlaceSummary}\nMulti-placement is supported; saving.");
            }

            // Step 1: Update working pkg.previewConfig.targetPlacements
            if (_pkg.previewConfig == null) _pkg.previewConfig = new PackagePreviewConfig();
            var placements = _pkg.previewConfig.targetPlacements != null
                ? new List<TargetPreviewPlacement>(_pkg.previewConfig.targetPlacements)
                : new List<TargetPreviewPlacement>();

            foreach (ref TargetEditState t in _targets.AsSpan())
            {
                if (!t.isDirty) continue;
                string targetId = t.def.id;
                int    idx      = placements.FindIndex(p => p != null && p.targetId == targetId);
                TargetPreviewPlacement entry = idx >= 0
                    ? placements[idx]
                    : new TargetPreviewPlacement { targetId = t.def.id };

                entry.position = PackageJsonUtils.ToFloat3(t.position);
                entry.rotation = PackageJsonUtils.ToQuaternion(t.rotation);
                entry.scale    = PackageJsonUtils.ToFloat3(t.scale);
                entry.portA    = PackageJsonUtils.ToFloat3(t.portA);
                entry.portB    = PackageJsonUtils.ToFloat3(t.portB);

                if (idx < 0) entry.color = new SceneFloat4 { r = 0f, g = 0.9f, b = 1f, a = 0.7f };

                if (idx >= 0) placements[idx] = entry;
                else          placements.Add(entry);
            }
            _pkg.previewConfig.targetPlacements = placements.ToArray();

            // Step 1b: Merge dirty _parts into previewConfig.partPlacements
            if (_parts != null)
            {
                var pp = _pkg.previewConfig.partPlacements != null
                    ? new List<PartPreviewPlacement>(_pkg.previewConfig.partPlacements)
                    : new List<PartPreviewPlacement>();
                foreach (ref PartEditState p in _parts.AsSpan())
                {
                    if (!p.isDirty) continue;
                    string pid = p.def.id;
                    int pidx = pp.FindIndex(e => e != null && e.partId == pid);
                    var entry = pidx >= 0 ? pp[pidx] : new PartPreviewPlacement { partId = pid };
                    entry.startPosition = PackageJsonUtils.ToFloat3(p.startPosition);
                    entry.startRotation = PackageJsonUtils.ToQuaternion(p.startRotation);
                    entry.startScale    = PackageJsonUtils.ToFloat3(p.startScale);
                    entry.assembledPosition  = PackageJsonUtils.ToFloat3(p.assembledPosition);
                    entry.assembledRotation  = PackageJsonUtils.ToQuaternion(p.assembledRotation);
                    entry.assembledScale     = PackageJsonUtils.ToFloat3(p.assembledScale);
                    entry.color = new SceneFloat4 { r = p.color.r, g = p.color.g, b = p.color.b, a = p.color.a };

                    // Mirror startPosition edits into the authoritative
                    // PartDefinition.stagingPose. BakeStagingPoses runs on
                    // every load and unconditionally overwrites
                    // placement.startPosition from part.stagingPose.position,
                    // so without this mirror a gizmo-edited start pose
                    // vanishes on reload — "goes back to where we first
                    // grabbed it". CLAUDE.md explicitly says stagingPose is
                    // the source of truth; this is the authoring surface that
                    // writes it.
                    if (p.def != null)
                    {
                        if (p.def.stagingPose == null) p.def.stagingPose = new StagingPose();
                        p.def.stagingPose.position = entry.startPosition;
                        p.def.stagingPose.rotation = entry.startRotation;
                        p.def.stagingPose.scale    = entry.startScale;
                        // color stays untouched — not part of gizmo edits.
                    }
                    // Filter out synthetic NO-TASK waypoints (baked by
                    // MachinePackageNormalizer.BakeNoTaskWaypoints). They're
                    // recomputed on every load from visualPartIds + startPosition
                    // and must never round-trip to JSON.
                    entry.stepPoses = null;
                    if (p.stepPoses != null && p.stepPoses.Count > 0)
                    {
                        var authored = new List<StepPoseEntry>(p.stepPoses.Count);
                        foreach (var spEntry in p.stepPoses)
                        {
                            if (spEntry == null) continue;
                            if (!string.IsNullOrEmpty(spEntry.label)
                                && spEntry.label.StartsWith(MachinePackageNormalizer.AutoNoTaskLabel, StringComparison.Ordinal))
                                continue;
                            // Auto-heal: entries with empty propagateFromStep
                            // would be saved as "unbounded from anchor" —
                            // resolver treats that as "this step → end" so
                            // set explicitly to match for JSON transparency.
                            if (string.IsNullOrEmpty(spEntry.propagateFromStep)
                                && !string.IsNullOrEmpty(spEntry.stepId))
                                spEntry.propagateFromStep = spEntry.stepId;
                            authored.Add(spEntry);
                        }
                        if (authored.Count > 0) entry.stepPoses = authored.ToArray();
                    }
                    if (pidx >= 0) pp[pidx] = entry; else pp.Add(entry);
                }
                _pkg.previewConfig.partPlacements = pp.ToArray();
            }

            // Step 1c: Merge dirty _groups into previewConfig.subassemblyPlacements
            if (_groups != null)
            {
                var gp = _pkg.previewConfig.subassemblyPlacements != null
                    ? new List<SubassemblyPreviewPlacement>(_pkg.previewConfig.subassemblyPlacements)
                    : new List<SubassemblyPreviewPlacement>();
                foreach (ref GroupEditState g in _groups.AsSpan())
                {
                    if (!g.isDirty || g.def == null) continue;
                    string gid = g.def.id;
                    int gidx = gp.FindIndex(e => e != null && e.subassemblyId == gid);
                    var entry = gidx >= 0 ? gp[gidx] : new SubassemblyPreviewPlacement { subassemblyId = gid };
                    entry.startPosition     = PackageJsonUtils.ToFloat3(g.startPosition);
                    entry.startRotation     = PackageJsonUtils.ToQuaternion(g.startRotation);
                    entry.startScale        = PackageJsonUtils.ToFloat3(g.startScale);
                    entry.assembledPosition = PackageJsonUtils.ToFloat3(g.assembledPosition);
                    entry.assembledRotation = PackageJsonUtils.ToQuaternion(g.assembledRotation);
                    entry.assembledScale    = PackageJsonUtils.ToFloat3(g.assembledScale);
                    entry.stepPoses         = g.stepPoses?.Count > 0 ? g.stepPoses.ToArray() : null;
                    // Also update legacy position field for backward compat
                    entry.position = entry.startPosition;
                    entry.rotation = entry.startRotation;
                    entry.scale    = entry.startScale;
                    if (gidx >= 0) gp[gidx] = entry; else gp.Add(entry);
                }
                _pkg.previewConfig.subassemblyPlacements = gp.ToArray();
            }

            // Determine whether this package uses split-layout (assemblies/ folder).
            bool   isSplit          = PackageJsonUtils.IsSplitLayout(_pkgId);
            string previewCfgPath   = isSplit ? PackageJsonUtils.GetPreviewConfigJsonPath(_pkgId) : jsonPath;

            // Step 2: Validate original JSON (monolithic only)
            if (!isSplit)
            {
                string orig = File.ReadAllText(jsonPath);
                try { JsonUtility.FromJson<MachinePackageDefinition>(orig); }
                catch (Exception ex)
                {
                    Debug.LogError($"[ToolTargetAuthoring] machine.json is already invalid, aborting.\n{ex.Message}");
                    return;
                }
            }

            // Step 3: Write previewConfig block to the correct file.
            _pkg.previewConfig.targetRotationFormat = "mesh";
            if (previewCfgPath != null)
                PackageJsonUtils.WritePreviewConfig(previewCfgPath, _pkg.previewConfig);

            // ── Per-file content cache ────────────────────────────────────────────
            var fileContents = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!isSplit)
                fileContents[jsonPath] = File.ReadAllText(jsonPath);

            string ResolveEntityFile(string entityId) =>
                isSplit ? PackageJsonUtils.FindEntityFilePath(_pkgId, entityId) : jsonPath;

            void InjectField(string entityId, string field, string value)
            {
                string fp = ResolveEntityFile(entityId);
                if (fp == null) return;
                if (!fileContents.TryGetValue(fp, out string cnt))
                    fileContents[fp] = cnt = File.ReadAllText(fp);
                TryInjectBlock(ref cnt, entityId, field, value);
                fileContents[fp] = cnt;
            }

            void RemoveField(string entityId, string field)
            {
                string fp = ResolveEntityFile(entityId);
                if (fp == null) return;
                if (!fileContents.TryGetValue(fp, out string cnt))
                    fileContents[fp] = cnt = File.ReadAllText(fp);
                TryRemoveBlock(ref cnt, entityId, field);
                fileContents[fp] = cnt;
            }

            // Step 4: Inject TargetDefinition fields for dirty targets
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var t in _targets)
            {
                if (!t.isDirty) continue;

                string axisJson = $"{{ \"x\": {R(t.weldAxis.x).ToString(inv)}, \"y\": {R(t.weldAxis.y).ToString(inv)}, \"z\": {R(t.weldAxis.z).ToString(inv)} }}";
                InjectField(t.def.id, "weldAxis", axisJson);

                if (t.weldLength > 0.0001f)
                    InjectField(t.def.id, "weldLength", R(t.weldLength).ToString(inv));

                Quaternion worldRot   = GetPreviewRoot() is Transform wr
                    ? wr.rotation * t.rotation
                    : t.rotation;
                Vector3 worldEuler    = worldRot.eulerAngles;
                InjectField(t.def.id, "useToolActionRotation", "true");
                string tarJson = $"{{ \"x\": {R(worldEuler.x).ToString(inv)}, \"y\": {R(worldEuler.y).ToString(inv)}, \"z\": {R(worldEuler.z).ToString(inv)} }}";
                InjectField(t.def.id, "toolActionRotation", tarJson);
            }

            // Step 5a: Inject ToolDefinition.persistent for dirty tools
            foreach (string toolId in _dirtyToolIds)
            {
                ToolDefinition toolDef = null;
                if (_pkg?.tools != null)
                    foreach (var td in _pkg.tools)
                        if (td != null && td.id == toolId) { toolDef = td; break; }
                if (toolDef != null)
                    InjectField(toolId, "persistent", toolDef.persistent ? "true" : "false");
            }
            _dirtyToolIds.Clear();

            // Step 5b: Inject modified step fields for dirty steps
            foreach (string stepId in _dirtyStepIds)
            {
                var step = FindStep(stepId);
                if (step == null) continue;

                // removePersistentToolIds
                string idsJson = step.removePersistentToolIds == null || step.removePersistentToolIds.Length == 0
                    ? "[]"
                    : "[ " + string.Join(", ", Array.ConvertAll(step.removePersistentToolIds, id => $"\"{id}\"")) + " ]";
                InjectField(stepId, "removePersistentToolIds", idsJson);

                // targetIds
                if (step.targetIds != null)
                {
                    string tJson = step.targetIds.Length == 0 ? "[]"
                        : "[ " + string.Join(", ", Array.ConvertAll(step.targetIds, id => $"\"{id}\"")) + " ]";
                    InjectField(stepId, "targetIds", tJson);
                }

                // requiredPartIds
                if (step.requiredPartIds != null)
                {
                    string pJson = step.requiredPartIds.Length == 0 ? "[]"
                        : "[ " + string.Join(", ", Array.ConvertAll(step.requiredPartIds, id => $"\"{id}\"")) + " ]";
                    InjectField(stepId, "requiredPartIds", pJson);
                }

                // requiredSubassemblyId — group placement
                if (!string.IsNullOrEmpty(step.requiredSubassemblyId))
                    InjectField(stepId, "requiredSubassemblyId", $"\"{step.requiredSubassemblyId}\"");
                else
                    RemoveField(stepId, "requiredSubassemblyId");

                // visualPartIds — show-without-require (Phase 7, legacy)
                if (step.visualPartIds != null && step.visualPartIds.Length > 0)
                {
                    string vJson = "[ " + string.Join(", ", Array.ConvertAll(step.visualPartIds, id => $"\"{id}\"")) + " ]";
                    InjectField(stepId, "visualPartIds", vJson);
                }
                else
                {
                    RemoveField(stepId, "visualPartIds");
                }

                // optionalPartIds — visible but not required for completion
                if (step.optionalPartIds != null && step.optionalPartIds.Length > 0)
                {
                    string oJson = "[ " + string.Join(", ", Array.ConvertAll(step.optionalPartIds, id => $"\"{id}\"")) + " ]";
                    InjectField(stepId, "optionalPartIds", oJson);
                }
                else
                {
                    RemoveField(stepId, "optionalPartIds");
                }

                // requiredToolActions
                if (step.requiredToolActions != null)
                {
                    string aJson;
                    if (step.requiredToolActions.Length == 0)
                    {
                        aJson = "[]";
                    }
                    else
                    {
                        var rows = Array.ConvertAll(step.requiredToolActions, a =>
                        {
                            if (a == null) return "{}";
                            var sb = new System.Text.StringBuilder("{");
                            if (!string.IsNullOrEmpty(a.id))       sb.Append($"\"id\":\"{a.id}\",");
                            if (!string.IsNullOrEmpty(a.toolId))   sb.Append($"\"toolId\":\"{a.toolId}\",");
                            if (!string.IsNullOrEmpty(a.targetId)) sb.Append($"\"targetId\":\"{a.targetId}\"");
                            else if (sb[sb.Length - 1] == ',')     sb.Length--; // trim trailing comma
                            sb.Append("}");
                            return sb.ToString();
                        });
                        aJson = "[ " + string.Join(", ", rows) + " ]";
                    }
                    InjectField(stepId, "requiredToolActions", aJson);
                }

                // wireConnect
                if (step.wireConnect?.IsConfigured == true)
                {
                    var wc = step.wireConnect;
                    var inv2 = System.Globalization.CultureInfo.InvariantCulture;

                    // Sync portA/portB from TargetEditState → wire entry
                    if (_targets != null)
                        foreach (ref var te in _targets.AsSpan())
                            if (te.isDirty && wc.wires != null)
                                foreach (var we in wc.wires)
                                    if (we != null && we.targetId == te.def?.id)
                                    {
                                        we.portA = PackageJsonUtils.ToFloat3(te.portA);
                                        we.portB = PackageJsonUtils.ToFloat3(te.portB);
                                    }

                    var wRows = Array.ConvertAll(wc.wires, w =>
                    {
                        if (w == null) return "{}";
                        var sb = new System.Text.StringBuilder("{");
                        if (!string.IsNullOrEmpty(w.targetId))           sb.Append($"\"targetId\":\"{w.targetId}\",");
                        sb.Append($"\"portA\":{{\"x\":{R(w.portA.x).ToString(inv2)},\"y\":{R(w.portA.y).ToString(inv2)},\"z\":{R(w.portA.z).ToString(inv2)}}},");
                        sb.Append($"\"portB\":{{\"x\":{R(w.portB.x).ToString(inv2)},\"y\":{R(w.portB.y).ToString(inv2)},\"z\":{R(w.portB.z).ToString(inv2)}}},");
                        sb.Append($"\"color\":{{\"r\":{R(w.color.r).ToString(inv2)},\"g\":{R(w.color.g).ToString(inv2)},\"b\":{R(w.color.b).ToString(inv2)},\"a\":{R(w.color.a).ToString(inv2)}}},");
                        sb.Append($"\"radius\":{R(w.radius > 0 ? w.radius : 0.003f).ToString(inv2)},");
                        sb.Append($"\"subdivisions\":{Mathf.Max(1, w.subdivisions)},");
                        sb.Append($"\"sag\":{R(w.sag > 0f ? w.sag : 1.0f).ToString(inv2)},");
                        if (!string.IsNullOrEmpty(w.interpolation)) sb.Append($"\"interpolation\":\"{w.interpolation}\",");
                        if (!string.IsNullOrEmpty(w.portAPolarityType))  sb.Append($"\"portAPolarityType\":\"{w.portAPolarityType}\",");
                        if (!string.IsNullOrEmpty(w.portBPolarityType))  sb.Append($"\"portBPolarityType\":\"{w.portBPolarityType}\",");
                        if (!string.IsNullOrEmpty(w.portAConnectorType)) sb.Append($"\"portAConnectorType\":\"{w.portAConnectorType}\",");
                        if (!string.IsNullOrEmpty(w.portBConnectorType)) sb.Append($"\"portBConnectorType\":\"{w.portBConnectorType}\",");
                        sb.Append($"\"polarityOrderMatters\":{(w.polarityOrderMatters ? "true" : "false")}");
                        sb.Append("}");
                        return sb.ToString();
                    });
                    string wcJson = $"{{\"enforcePortOrder\":{(wc.enforcePortOrder ? "true" : "false")},\"wires\":[ {string.Join(", ", wRows)} ]}}";
                    InjectField(stepId, "wireConnect", wcJson);
                }

                // taskOrder
                if (step.taskOrder != null && step.taskOrder.Length > 0)
                    InjectField(stepId, "taskOrder", BuildTaskOrderJson(new List<TaskOrderEntry>(step.taskOrder)));

                // workingOrientation
                if (step.workingOrientation != null)
                {
                    var wo2 = step.workingOrientation;
                    var sb = new System.Text.StringBuilder("{");
                    sb.Append($"\"subassemblyRotation\":{{\"x\":{R(wo2.subassemblyRotation.x).ToString(inv)},\"y\":{R(wo2.subassemblyRotation.y).ToString(inv)},\"z\":{R(wo2.subassemblyRotation.z).ToString(inv)}}},");
                    sb.Append($"\"subassemblyPositionOffset\":{{\"x\":{R(wo2.subassemblyPositionOffset.x).ToString(inv)},\"y\":{R(wo2.subassemblyPositionOffset.y).ToString(inv)},\"z\":{R(wo2.subassemblyPositionOffset.z).ToString(inv)}}}");
                    if (!string.IsNullOrEmpty(wo2.hint))
                        sb.Append($",\"hint\":\"{wo2.hint.Replace("\"", "\\\"")}\"");
                    sb.Append("}");
                    InjectField(stepId, "workingOrientation", sb.ToString());
                }
                else
                {
                    RemoveField(stepId, "workingOrientation");
                }

                // animationCues
                if (step.animationCues?.cues?.Length > 0)
                {
                    string acRaw  = JsonUtility.ToJson(step.animationCues);
                    string acJson = PackageJsonUtils.RoundFloatsInJson(acRaw);
                    InjectField(stepId, "animationCues", acJson);
                }
                else
                {
                    RemoveField(stepId, "animationCues");
                }

                // particleEffects
                if (step.particleEffects?.effects?.Length > 0)
                {
                    string peRaw  = JsonUtility.ToJson(step.particleEffects);
                    string peJson = PackageJsonUtils.RoundFloatsInJson(peRaw);
                    InjectField(stepId, "particleEffects", peJson);
                }
                else
                {
                    RemoveField(stepId, "particleEffects");
                }
            }
            _dirtyStepIds.Clear();
            _dirtyTaskOrderStepIds.Clear();

            // Step 5c: Inject assetRef for parts whose model was changed
            foreach (string partId in _dirtyPartAssetRefIds)
            {
                if (_parts == null) break;
                foreach (ref PartEditState p in _parts.AsSpan())
                {
                    if (p.def?.id != partId) continue;
                    if (!string.IsNullOrEmpty(p.def.assetRef))
                        InjectField(partId, "assetRef", $"\"{p.def.assetRef}\"");
                    break;
                }
            }
            _dirtyPartAssetRefIds.Clear();

            // Step 5c-bis: Inject toolIds for parts whose tool affinity changed (Phase 7b).
            // Empty arrays are removed (omitted) so legacy parts don't grow a
            // zero-length field after a save.
            foreach (string partId in _dirtyPartToolIds)
            {
                if (_pkg?.parts == null) break;
                PartDefinition part = null;
                for (int pi = 0; pi < _pkg.parts.Length; pi++)
                {
                    if (_pkg.parts[pi]?.id == partId) { part = _pkg.parts[pi]; break; }
                }
                if (part == null) continue;
                if (part.toolIds != null && part.toolIds.Length > 0)
                {
                    string tJson = "[ " + string.Join(", ",
                        Array.ConvertAll(part.toolIds, id => $"\"{id}\"")) + " ]";
                    InjectField(partId, "toolIds", tJson);
                }
                else
                {
                    RemoveField(partId, "toolIds");
                }
            }
            _dirtyPartToolIds.Clear();

            // Step 5c-ter: Inject fields for dirty subassemblies (Phase 7e).
            // The subassembly object already exists in the file (inserted by
            // InsertSubassembly or loaded from disk); we just update its fields.
            foreach (string subId in _dirtySubassemblyIds)
            {
                if (!_pkg.TryGetSubassembly(subId, out SubassemblyDefinition sub) || sub == null)
                    continue;

                // name
                if (!string.IsNullOrEmpty(sub.name))
                    InjectField(subId, "name", $"\"{sub.name}\"");

                // partIds
                if (sub.partIds != null && sub.partIds.Length > 0)
                {
                    string pJson = "[ " + string.Join(", ",
                        Array.ConvertAll(sub.partIds, id => $"\"{id}\"")) + " ]";
                    InjectField(subId, "partIds", pJson);
                }
                else
                {
                    RemoveField(subId, "partIds");
                }

                // stepIds
                if (sub.stepIds != null && sub.stepIds.Length > 0)
                {
                    string sJson = "[ " + string.Join(", ",
                        Array.ConvertAll(sub.stepIds, id => $"\"{id}\"")) + " ]";
                    InjectField(subId, "stepIds", sJson);
                }
                else
                {
                    RemoveField(subId, "stepIds");
                }

                // assemblyId
                if (!string.IsNullOrEmpty(sub.assemblyId))
                    InjectField(subId, "assemblyId", $"\"{sub.assemblyId}\"");

                // description
                if (!string.IsNullOrEmpty(sub.description))
                    InjectField(subId, "description", $"\"{sub.description}\"");
                else
                    RemoveField(subId, "description");

                // isAggregate is redundant ONLY when memberSubassemblyIds is
                // populated (InferAggregateFlag can re-derive it on load). For
                // partId-overlap aggregates with no memberSubassemblyIds, the
                // explicit flag remains the only structural signal — keep it.
                bool hasMembers = sub.memberSubassemblyIds != null && sub.memberSubassemblyIds.Length > 0;
                if (sub.isAggregate && !hasMembers)
                    InjectField(subId, "isAggregate", "true");
                else
                    RemoveField(subId, "isAggregate");

                // memberSubassemblyIds — persist the aggregate's child groups
                // so drag-and-drop additions survive save/reload.
                if (hasMembers)
                {
                    var msb = new System.Text.StringBuilder("[");
                    for (int m = 0; m < sub.memberSubassemblyIds.Length; m++)
                    {
                        if (m > 0) msb.Append(", ");
                        msb.Append('"').Append(sub.memberSubassemblyIds[m]).Append('"');
                    }
                    msb.Append("]");
                    InjectField(subId, "memberSubassemblyIds", msb.ToString());
                }
                else
                {
                    RemoveField(subId, "memberSubassemblyIds");
                }

                // milestoneMessage
                if (!string.IsNullOrEmpty(sub.milestoneMessage))
                    InjectField(subId, "milestoneMessage", $"\"{sub.milestoneMessage}\"");
                else
                    RemoveField(subId, "milestoneMessage");
            }
            _dirtySubassemblyIds.Clear();

            // Step 5d: Write stagingPose to parts[] for dirty parts.
            if (_parts != null)
            {
                foreach (ref PartEditState p in _parts.AsSpan())
                {
                    if (!p.isDirty || p.def == null) continue;

                    var staging = new StagingPose
                    {
                        position = PackageJsonUtils.ToFloat3(p.startPosition),
                        rotation = PackageJsonUtils.ToQuaternion(p.startRotation),
                        scale    = PackageJsonUtils.ToFloat3(p.startScale),
                        color    = new SceneFloat4
                        {
                            r = p.color.r,
                            g = p.color.g,
                            b = p.color.b,
                            a = p.color.a,
                        },
                    };
                    string stagingJson = PackageJsonUtils.RoundFloatsInJson(UnityEngine.JsonUtility.ToJson(staging));
                    InjectField(p.def.id, "stagingPose", stagingJson);
                }
            }

            // Step 5e: Validate result (monolithic only)
            if (!isSplit && fileContents.TryGetValue(jsonPath, out string monoJson))
            {
                try { JsonUtility.FromJson<MachinePackageDefinition>(monoJson); }
                catch (Exception ex)
                {
                    Debug.LogError($"[ToolTargetAuthoring] Write would produce invalid JSON, aborting.\n{ex.Message}");
                    return;
                }
            }

            // Step 6: Backup + write all modified files
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string firstBackup = null;
            foreach (var kv in fileContents)
            {
                string fp      = kv.Key;
                string content = kv.Value;
                string backupDir = Path.Combine(Path.GetDirectoryName(fp)!, ".pose_backups");
                Directory.CreateDirectory(backupDir);
                string bak = Path.Combine(backupDir, $"{Path.GetFileNameWithoutExtension(fp)}_{ts}.json");
                File.Copy(fp, bak, true);
                if (firstBackup == null) firstBackup = bak;
                File.WriteAllText(fp, content);
            }
            _lastBackupPath = firstBackup;

            // Targeted import instead of full AssetDatabase.Refresh — only the
            // files we actually wrote need to tell Unity about their change.
            // Full-tree Refresh was typically 1-3s; per-file is ~instant.
            foreach (var kv in fileContents)
            {
                string projRel = ToProjectRelative(kv.Key);
                if (projRel != null)
                    AssetDatabase.ImportAsset(projRel, ImportAssetOptions.ForceUpdate);
            }

            Debug.Log($"[ToolTargetAuthoring] Written {_pkgId} (backup: {firstBackup})");

            if (reloadAfter)
            {
                // Step 7: Reload and clear dirty flags
                _pkg = PackageJsonUtils.LoadPackage(_pkgId);
                if (_pkg != null)
                    _assetResolver.BuildCatalog(_pkgId, _pkg.parts ?? System.Array.Empty<PartDefinition>());
                _taskSeqReorderList          = null;
                _taskSeqReorderListForStepId = null;
                InvalidateTaskOrderCache();
                BuildTargetToolMap();
                BuildTargetList();
                BuildPartList();
                RespawnScene();
                SyncAllPartMeshesToActivePose();
            }
            else
            {
                if (_targets != null)
                    for (int i = 0; i < _targets.Length; i++)
                        _targets[i].isDirty = false;
                if (_parts != null)
                    for (int i = 0; i < _parts.Length; i++)
                        _parts[i].isDirty = false;
            }
            _poseSwitchCooldownUntil = EditorApplication.timeSinceStartup + 0.5;

            // StreamingAssets sync is NOT needed on every save — the editor
            // reads authoring JSON directly, and PackageSyncPreprocessor syncs
            // automatically before every build via IPreprocessBuildWithReport.
            // Only the validation dashboard needs to refresh post-save.
            EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                RunValidation();
                Repaint();
            };
        }

        private static string ToProjectRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;
            string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string root = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + full.Substring(root.Length);
        }

        private void RevertFromBackup()
        {
            if (!File.Exists(_lastBackupPath)) return;
            string backupName = Path.GetFileNameWithoutExtension(_lastBackupPath);
            string sourceFile;
            if (PackageJsonUtils.IsSplitLayout(_pkgId))
            {
                int lastUnderscore2 = backupName.LastIndexOf('_');
                int lastUnderscore1 = lastUnderscore2 > 0 ? backupName.LastIndexOf('_', lastUnderscore2 - 1) : -1;
                string baseName = lastUnderscore1 > 0 ? backupName.Substring(0, lastUnderscore1) : backupName;
                string packageDir = Path.Combine(PackageJsonUtils.AuthoringRoot, _pkgId);
                string asmFile    = Path.Combine(packageDir, "assemblies", $"{baseName}.json");
                string rootFile   = Path.Combine(packageDir, $"{baseName}.json");
                if (File.Exists(asmFile))       sourceFile = asmFile;
                else if (File.Exists(rootFile)) sourceFile = rootFile;
                else { Debug.LogWarning($"[ToolTargetAuthoring] RevertFromBackup: cannot locate source for backup '{_lastBackupPath}'."); return; }
            }
            else
            {
                sourceFile = PackageJsonUtils.GetJsonPath(_pkgId);
                if (sourceFile == null) return;
            }

            File.Copy(_lastBackupPath, sourceFile, true);
            AssetDatabase.Refresh();
            Debug.Log($"[ToolTargetAuthoring] Reverted {Path.GetFileName(sourceFile)} to backup: {_lastBackupPath}");
            _lastBackupPath = null;
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            BuildTargetList();
            BuildPartList();
            SyncAllPartMeshesToActivePose();
        }

        /// <summary>
        /// Discards ALL unsaved in-memory edits and reloads from the current machine.json on disk.
        /// </summary>
        private void RevertAllChanges()
        {
            if (string.IsNullOrEmpty(_pkgId)) return;
            if (!EditorUtility.DisplayDialog(
                "Revert All Changes",
                $"Discard all unsaved edits and reload '{_pkgId}/machine.json' from disk?",
                "Revert", "Cancel"))
                return;

            LoadPkg(_pkgId);
        }

        // ── Extract from GLB anchors ──────────────────────────────────────────

        private void ExtractFromGlbAnchors()
        {
            if (string.IsNullOrEmpty(_pkgId) || _targets == null) return;

            string pkgFolder = $"{PackageJsonUtils.AuthoringRoot}/{_pkgId}";
            string[] guids   = AssetDatabase.FindAssets("t:GameObject", new[] { pkgFolder });
            int found = 0;

            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string ext       = Path.GetExtension(assetPath);
                if (!ext.Equals(".glb",  StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".fbx",  StringComparison.OrdinalIgnoreCase))
                    continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (go == null) continue;

                for (int i = 0; i < _targets.Length; i++)
                {
                    Transform node = FindNode(go.transform, _targets[i].def.id);
                    if (node == null) continue;

                    BeginEdit();
                    _targets[i].position     = node.localPosition;
                    _targets[i].rotation     = node.localRotation;
                    _targets[i].scale        = node.localScale;
                    _targets[i].isDirty      = true;
                    _targets[i].hasPlacement = true;
                    EndEdit();
                    found++;
                }
            }

            if (found > 0) Debug.Log($"[ToolTargetAuthoring] Extracted {found} target(s) from GLB anchors.");
            else           Debug.Log("[ToolTargetAuthoring] No named anchor nodes matched any targetId.");

            SceneView.RepaintAll();
            Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Depth-first node search by exact name.</summary>
        private static Transform FindNode(Transform t, string name)
        {
            if (t.name.Equals(name, StringComparison.Ordinal)) return t;
            foreach (Transform child in t)
            {
                var found = FindNode(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static float R(float v) => Mathf.Round(v * 100000f) / 100000f;

        // ── JSON injection helpers ────────────────────────────────────────────

        private static bool TryInjectBlock(ref string json, string id, string block, string blockJson)
        {
            blockJson = PackageJsonUtils.RoundFloatsInJson(blockJson);
            string fullPattern = $"\"id\": \"{id}\"";
            int idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            if (idPos < 0)
            {
                fullPattern = $"\"id\":\"{id}\"";
                idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            }
            if (idPos < 0)
            {
                string idNeedle  = "\"id\"";
                int searchFrom = 0;
                while (searchFrom < json.Length)
                {
                    int f = json.IndexOf(idNeedle, searchFrom, StringComparison.Ordinal);
                    if (f < 0) break;
                    int afterKey = f + idNeedle.Length;
                    int colonPos = SkipWhitespace(json, afterKey);
                    if (colonPos < json.Length && json[colonPos] == ':')
                    {
                        int valuePos = SkipWhitespace(json, colonPos + 1);
                        string idValue = $"\"{id}\"";
                        if (valuePos + idValue.Length <= json.Length
                            && json.Substring(valuePos, idValue.Length) == idValue)
                        { idPos = f; break; }
                    }
                    searchFrom = f + 1;
                }
            }
            if (idPos < 0) { Debug.LogWarning($"[ToolTargetAuthoring] TryInjectBlock: id='{id}' not found"); return false; }

            int objStart = FindObjectStart(json, idPos);
            if (objStart < 0) return false;
            int objEnd = FindMatchingClose(json, objStart);
            if (objEnd < 0) return false;

            string obj     = json.Substring(objStart, objEnd - objStart + 1);
            string cleaned = RemoveKey(obj, block);

            string indent  = "            ";
            int lineStart  = json.LastIndexOf('\n', idPos);
            if (lineStart >= 0)
            {
                int firstNonSpace = lineStart + 1;
                while (firstNonSpace < json.Length && json[firstNonSpace] == ' ') firstNonSpace++;
                indent = new string(' ', firstNonSpace - lineStart - 1);
            }

            int    lastBrace = cleaned.LastIndexOf('}');
            string before    = cleaned.Substring(0, lastBrace).TrimEnd();
            string after     = cleaned.Substring(lastBrace);
            string injected  = before + ",\n" + indent + $"\"{block}\": " + blockJson + "\n"
                             + indent.Substring(0, Math.Max(0, indent.Length - 4)) + after;

            json = json.Substring(0, objStart) + injected + json.Substring(objEnd + 1);
            return true;
        }

        /// <summary>Removes a key from the JSON object identified by <paramref name="id"/>.</summary>
        private static bool TryRemoveBlock(ref string json, string id, string block)
        {
            string fullPattern = $"\"id\": \"{id}\"";
            int idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            if (idPos < 0)
            {
                fullPattern = $"\"id\":\"{id}\"";
                idPos = json.IndexOf(fullPattern, StringComparison.Ordinal);
            }
            if (idPos < 0) return false;

            int objStart = FindObjectStart(json, idPos);
            if (objStart < 0) return false;
            int objEnd = FindMatchingClose(json, objStart);
            if (objEnd < 0) return false;

            string obj = json.Substring(objStart, objEnd - objStart + 1);
            string cleaned = RemoveKey(obj, block);
            if (cleaned == obj) return false;

            json = json.Substring(0, objStart) + cleaned + json.Substring(objEnd + 1);
            return true;
        }

        private static int SkipWhitespace(string s, int from)
        {
            while (from < s.Length && char.IsWhiteSpace(s[from])) from++;
            return from;
        }

        private static int FindObjectStart(string json, int from)
        {
            int depth = 0;
            for (int i = from - 1; i >= 0; i--)
            {
                char c = json[i];
                if (c == '"')
                {
                    int slashes = 0;
                    for (int j = i - 1; j >= 0 && json[j] == '\\'; j--) slashes++;
                    if (slashes % 2 == 0)
                    {
                        i--;
                        while (i >= 0)
                        {
                            char sc = json[i];
                            if (sc == '"')
                            {
                                int sl2 = 0;
                                for (int j = i - 1; j >= 0 && json[j] == '\\'; j--) sl2++;
                                if (sl2 % 2 == 0) break;
                            }
                            i--;
                        }
                        continue;
                    }
                }
                if (c == '}') { depth++; continue; }
                if (c == '{')
                {
                    if (depth == 0) return i;
                    depth--;
                }
            }
            return -1;
        }

        private static int FindMatchingClose(string json, int openPos)
        {
            char open  = json[openPos];
            char close = open == '{' ? '}' : ']';
            int depth  = 0; bool inStr = false;
            for (int i = openPos; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open)  depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string RemoveKey(string obj, string key)
        {
            string needle = $"\"{key}\"";
            int keyIdx = obj.IndexOf(needle, StringComparison.Ordinal);
            if (keyIdx < 0) return obj;

            int colon    = obj.IndexOf(':', keyIdx + needle.Length);
            if (colon < 0) return obj;
            int valStart = SkipWhitespace(obj, colon + 1);
            if (valStart >= obj.Length) return obj;

            int valEnd;
            char first = obj[valStart];
            if (first == '{' || first == '[')
            {
                valEnd = FindMatchingClose(obj, valStart);
                if (valEnd < 0) return obj;
            }
            else if (first == '"')
            {
                valEnd = valStart;
                for (int i = valStart + 1; i < obj.Length; i++)
                {
                    if (obj[i] == '\\') { i++; continue; }
                    if (obj[i] == '"') { valEnd = i; break; }
                }
            }
            else
            {
                valEnd = valStart;
                for (int i = valStart; i < obj.Length; i++)
                {
                    char c = obj[i];
                    if (c == ',' || c == '}' || c == ']') { valEnd = i - 1; break; }
                    valEnd = i;
                }
            }

            int removeStart = keyIdx;
            int removeEnd   = valEnd + 1;
            int ls = removeStart - 1;
            while (ls >= 0 && (obj[ls] == ' ' || obj[ls] == '\t' || obj[ls] == '\r' || obj[ls] == '\n')) ls--;
            if (ls >= 0 && obj[ls] == ',')
                removeStart = ls;
            else
            {
                int ts = removeEnd;
                while (ts < obj.Length && (obj[ts] == ' ' || obj[ts] == '\t')) ts++;
                if (ts < obj.Length && obj[ts] == ',') removeEnd = ts + 1;
            }
            while (removeEnd < obj.Length && (obj[removeEnd] == ' ' || obj[removeEnd] == '\r' || obj[removeEnd] == '\n'))
                removeEnd++;

            return obj.Substring(0, removeStart) + obj.Substring(removeEnd);
        }
    }
}
