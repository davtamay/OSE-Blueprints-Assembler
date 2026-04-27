// TTAW.Prefabs.cs — Step Configuration Prefab catalog panel.
// Lists prefabs in AgentAssistant/prefabs/. Drop-on-canvas opens the
// PrefabWizard which appends a PrefabInstance to the active package in
// memory; the normalizer's ExpandPrefabInstances pass renders the
// virtual steps. WriteJson flushes the per-assembly prefabInstances[]
// array on save — no JSON duplication, edits to the source prefab YAML
// propagate to every instance on next load.

using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // Cached catalog — refreshed when the foldout is first expanded or the
        // user clicks the refresh button. Avoids re-listing the directory on
        // every OnGUI tick (would spam the disk for an editor-only feature).
        private string[] _prefabPaths;
        private PrefabExpander.Summary[] _prefabSummaries;
        private double  _prefabCatalogScannedAt = -1;
        private const double PrefabCatalogStaleAfterSec = 5.0;

        internal void DrawPrefabsPanelCard(StepDefinition step)
        {
            EnsurePrefabCatalogFresh();
            int totalCount = _prefabPaths?.Length ?? 0;

            DrawCard("PREFABS", "canvas/prefabs", CardAccentNeutral,
                defaultExpanded: false, count: totalCount,
                body: () => DrawPrefabsPanelBody(step));
        }

        private void DrawPrefabsPanelBody(StepDefinition step)
        {
            // Toolbar — refresh + folder shortcut.
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("⟲ Refresh", EditorStyles.miniButton, GUILayout.Width(72)))
                ScanPrefabCatalog();
            if (GUILayout.Button("Open prefabs folder", EditorStyles.miniButton, GUILayout.Width(140)))
                OpenInOSExplorer(GetPrefabsDir());
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox(
                "Drag a prefab onto the canvas drop zone above the task sequence to instantiate. " +
                "Drag selected steps from the navigator into this list to capture them as a new prefab.",
                MessageType.Info);
            EditorGUILayout.Space(2);

            // ── Available prefabs (drag source + drop target for capture) ────
            EditorGUILayout.LabelField($"Prefabs · {(_prefabPaths?.Length ?? 0)}", EditorStyles.boldLabel);

            // Capture drop zone: when the navigator is dragging selected
            // steps (DragKeySelectedStepIds), this whole panel accepts the
            // drop and opens the capture wizard.
            Rect captureZoneRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            HandleCapturePrefabDropZone(captureZoneRect);

            if (_prefabPaths == null || _prefabPaths.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No prefabs found in AgentAssistant/prefabs/. Drag selected steps from the " +
                    "navigator into this section (or author *.yaml prefabs by hand) to populate.",
                    MessageType.None);
            }
            else
            {
                for (int i = 0; i < _prefabPaths.Length; i++)
                {
                    string path = _prefabPaths[i];
                    PrefabExpander.Summary summary = (_prefabSummaries != null && i < _prefabSummaries.Length)
                        ? _prefabSummaries[i] : null;

                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    Rect rowRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
                    Rect titleRect  = new Rect(rowRect.x,           rowRect.y,        rowRect.width - 84f, 16f);
                    Rect summaryRect= new Rect(rowRect.x + 18f,     rowRect.y + 18f,  rowRect.width - 100f, 14f);
                    Rect buttonRect = new Rect(rowRect.xMax - 80f,  rowRect.y + 4f,   78f, 28f);

                    string name = Path.GetFileNameWithoutExtension(path);
                    GUI.Label(titleRect, new GUIContent("📦  " + name,
                        "Drag this row onto the canvas drop zone to instantiate the prefab."),
                        new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });

                    string summaryLine = summary != null
                        ? summary.FormatSummaryLine()
                        : "(scanning…)";
                    GUI.Label(summaryRect, summaryLine, EditorStyles.miniLabel);

                    if (GUI.Button(buttonRect, new GUIContent("Open YAML",
                            "Reveal the prefab file in your OS file explorer to read or edit."),
                            EditorStyles.miniButton))
                    {
                        EditorUtility.RevealInFinder(path);
                    }
                    HandlePrefabRowDragSource(rowRect, path);
                    EditorGUILayout.EndVertical();
                }
            }
        }

        /// <summary>
        /// Drop zone covering the PREFABS section header. When the navigator
        /// is dragging selected step ids, accepts the drop and opens the
        /// capture wizard with those steps. Renders a translucent hint
        /// outline to flag the affordance during a drag.
        /// </summary>
        private void HandleCapturePrefabDropZone(Rect zoneRect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                bool dragActive = DragAndDrop.GetGenericData(DragKeySelectedStepIds) is string[];
                EditorGUI.DrawRect(zoneRect, dragActive
                    ? new Color(0.30f, 0.62f, 0.95f, 0.20f)
                    : new Color(0.18f, 0.18f, 0.22f, 0.30f));
                var style = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = dragActive ? Color.white : new Color(0.65f, 0.65f, 0.7f) },
                };
                GUI.Label(zoneRect, dragActive
                    ? "↓ Drop to capture selection as a new prefab"
                    : "Drag selected navigator steps here to capture as a prefab",
                    style);
            }

            var evt = Event.current;
            if (!zoneRect.Contains(evt.mousePosition)) return;
            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                if (DragAndDrop.GetGenericData(DragKeySelectedStepIds) is string[] ids && ids.Length > 0)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        OpenCapturePrefabModalForIds(ids);
                    }
                    evt.Use();
                }
            }
        }

        /// <summary>
        /// Captures the supplied step ids and opens
        /// <c>PrefabCaptureWindow</c>. Called by the PREFABS drop zone above
        /// AND by the canvas-banner "Capture as Prefab" button so both paths
        /// converge on the same modal.
        /// </summary>
        internal void OpenCapturePrefabModalForIds(IList<string> stepIds)
        {
            if (_pkg?.steps == null || stepIds == null || stepIds.Count == 0) return;
            var ordered = new List<StepDefinition>();
            var idSet   = new HashSet<string>(stepIds);
            foreach (var s in _pkg.steps)
                if (s != null && idSet.Contains(s.id)) ordered.Add(s);
            if (ordered.Count == 0) return;
            ordered.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));
            PrefabCaptureWindow.Open(this, ordered);
        }

        // ── Drag source ──────────────────────────────────────────────────────

        /// <summary>
        /// Generic-data key the drop handlers in <c>TTAW.UnifiedList</c> use
        /// to recognise a prefab drag (vs. the existing GameObject-only drag
        /// payload). Value is the absolute path to the prefab YAML.
        /// </summary>
        internal const string DragKeyPrefabYamlPath = "ose-prefab-yaml-path";

        /// <summary>
        /// Drag payload for the capture-as-prefab flow. The cross-step banner
        /// is the drag source; PREFABS panel is the drop target. Value is a
        /// <c>string[]</c> of step ids selected at drag-start.
        /// </summary>
        internal const string DragKeySelectedStepIds = "ose-selected-step-ids";

        /// <summary>
        /// Turns a row rect into a Unity drag source. On mouse-drag with the
        /// pointer inside the row, kicks off a DragAndDrop operation carrying
        /// the prefab's YAML path under <see cref="DragKeyPrefabYamlPath"/>.
        /// Mirrors the pattern used by the existing
        /// <c>DrawTaskSequenceDropZone</c> at <c>TTAW.UnifiedList.cs:2059</c>.
        /// </summary>
        private static void HandlePrefabRowDragSource(Rect rowRect, string prefabYamlPath)
        {
            if (Event.current.type != EventType.MouseDrag) return;
            if (!rowRect.Contains(Event.current.mousePosition)) return;
            if (Event.current.button != 0) return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragKeyPrefabYamlPath, prefabYamlPath);
            DragAndDrop.objectReferences = System.Array.Empty<UnityEngine.Object>();
            DragAndDrop.paths = System.Array.Empty<string>();
            DragAndDrop.StartDrag($"Prefab: {Path.GetFileNameWithoutExtension(prefabYamlPath)}");
            Event.current.Use();
        }

        // ── Catalog scanning ─────────────────────────────────────────────────

        private void EnsurePrefabCatalogFresh()
        {
            if (_prefabCatalogScannedAt < 0
                || EditorApplication.timeSinceStartup - _prefabCatalogScannedAt > PrefabCatalogStaleAfterSec)
            {
                ScanPrefabCatalog();
            }
        }

        private void ScanPrefabCatalog()
        {
            _prefabPaths = SafeListYaml(GetPrefabsDir());
            // Pre-compute the layer summary per prefab so PREFABS rows can
            // render `7 steps + 14 parts + 1 part group · self-contained`
            // without re-parsing on every OnGUI tick.
            int n = _prefabPaths?.Length ?? 0;
            _prefabSummaries = new PrefabExpander.Summary[n];
            for (int i = 0; i < n; i++)
                _prefabSummaries[i] = PrefabExpander.Analyze(_prefabPaths[i]);
            _prefabCatalogScannedAt = EditorApplication.timeSinceStartup;
        }

        private static string[] SafeListYaml(string dir)
        {
            if (!Directory.Exists(dir)) return System.Array.Empty<string>();
            try
            {
                var paths = new List<string>(Directory.GetFiles(dir, "*.yaml", SearchOption.TopDirectoryOnly));
                paths.Sort(System.StringComparer.OrdinalIgnoreCase);
                return paths.ToArray();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[TTAW.Prefabs] Failed to list '{dir}': {ex.Message}");
                return System.Array.Empty<string>();
            }
        }

        private static string GetRepoRoot()
        {
            // Application.dataPath = <repo>/Assets — go up one level.
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string GetPrefabsDir()
            => Path.Combine(GetRepoRoot(), "AgentAssistant", "prefabs");

        // ── Wizard entry ────────────────────────────────────────────────────

        /// <summary>Public proxy for the wizard to look up a step by id.</summary>
        internal StepDefinition FindStepPublic(string stepId) => FindStep(stepId);

        /// <summary>
        /// Public read-only accessor for the active in-memory package.
        /// Used by the capture / wizard windows to walk parts + groups +
        /// placements without re-loading from disk. Returns null between
        /// loads.
        /// </summary>
        internal MachinePackageDefinition _pkgPublic => _pkg;

        /// <summary>
        /// Returns the maximum sequenceIndex among steps belonging to the
        /// given partGroup, or -1 if none. Used by the wizard's
        /// "drop on step → instantiate at end of partGroup" default.
        /// </summary>
        internal int GetPartGroupMaxSeqPublic(string partGroupId)
        {
            if (_pkg?.steps == null || string.IsNullOrEmpty(partGroupId)) return -1;
            int max = -1;
            foreach (var s in _pkg.steps)
            {
                if (s == null) continue;
                if (!string.Equals(s.partGroupId, partGroupId, System.StringComparison.Ordinal)) continue;
                if (s.sequenceIndex > max) max = s.sequenceIndex;
            }
            return max;
        }

        /// <summary>
        /// In-memory merge entry for the wizard. Appends the supplied
        /// <see cref="PrefabInstance"/> to <c>_pkg.prefabInstances</c>, reruns
        /// <see cref="MachinePackageNormalizer.ExpandPrefabInstances"/> so the
        /// virtual steps appear in the canvas immediately, and marks the
        /// instance dirty so <c>WriteJson</c> flushes the JSON on the next
        /// "Write to machine.json" press. Nothing touches disk until then.
        /// Returns the number of virtual steps emitted (0 on failure).
        /// </summary>
        internal int MergePrefabInstancePublic(PrefabInstance instance)
        {
            if (instance == null || _pkg == null || string.IsNullOrEmpty(_pkgId)) return 0;
            if (string.IsNullOrEmpty(instance.prefabId) || string.IsNullOrEmpty(instance.instanceId))
            {
                Debug.LogError("[TTAW.Prefabs] PrefabInstance missing prefabId or instanceId.");
                return 0;
            }

            // Backfill assemblyId from the active step when the wizard
            // didn't already pin one — every emitted step needs an assembly.
            if (string.IsNullOrEmpty(instance.assemblyId))
            {
                if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                {
                    var anchor = FindStep(_stepIds[_stepFilterIdx]);
                    if (anchor != null) instance.assemblyId = anchor.assemblyId;
                }
            }
            if (string.IsNullOrEmpty(instance.assemblyId))
            {
                Debug.LogError($"[TTAW.Prefabs] PrefabInstance '{instance.instanceId}' has no assemblyId; aborting merge.");
                return 0;
            }

            int existing = _pkg.prefabInstances?.Length ?? 0;
            var merged = new PrefabInstance[existing + 1];
            if (existing > 0) System.Array.Copy(_pkg.prefabInstances, 0, merged, 0, existing);
            merged[existing] = instance;
            _pkg.prefabInstances = merged;

            // Re-run the normalizer so the new instance's virtual steps
            // appear in _pkg.steps without a full reload. Idempotent —
            // ExpandPrefabInstances strips previously-expanded entries
            // before re-emitting.
            try
            {
                MachinePackageNormalizer.Normalize(_pkg);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TTAW.Prefabs] Normalize after merge threw: {ex.Message}");
            }

            _dirtyPrefabInstanceIds.Add(instance.instanceId);

            // Refresh editor state so the canvas + navigator pick up the
            // virtual steps without going through a full LoadPkg cycle.
            BuildStepOptions();
            BuildTargetList();
            BuildPartList();
            BuildGroupList();
            RespawnScene();
            Repaint();

            int emittedCount = 0;
            if (_pkg.steps != null)
                foreach (var s in _pkg.steps)
                    if (s?.prefabRef != null
                        && string.Equals(s.prefabRef.instanceId, instance.instanceId, System.StringComparison.Ordinal))
                        emittedCount++;
            return emittedCount;
        }

        /// <summary>
        /// Removes a prefab instance from <c>_pkg.prefabInstances</c> and
        /// re-runs the normalizer so its virtual steps disappear from the
        /// canvas. Used by the linked-banner Discard button.
        /// </summary>
        internal bool DiscardPrefabInstancePublic(string instanceId)
        {
            if (_pkg == null || string.IsNullOrEmpty(instanceId) || _pkg.prefabInstances == null) return false;
            int found = -1;
            for (int i = 0; i < _pkg.prefabInstances.Length; i++)
            {
                if (_pkg.prefabInstances[i] != null
                    && string.Equals(_pkg.prefabInstances[i].instanceId, instanceId, System.StringComparison.Ordinal))
                { found = i; break; }
            }
            if (found < 0) return false;

            var trimmed = new PrefabInstance[_pkg.prefabInstances.Length - 1];
            if (found > 0) System.Array.Copy(_pkg.prefabInstances, 0, trimmed, 0, found);
            if (found < trimmed.Length) System.Array.Copy(_pkg.prefabInstances, found + 1, trimmed, found, trimmed.Length - found);
            _pkg.prefabInstances = trimmed;

            try { MachinePackageNormalizer.Normalize(_pkg); }
            catch (System.Exception ex) { Debug.LogError($"[TTAW.Prefabs] Normalize after discard threw: {ex.Message}"); }

            _dirtyPrefabInstanceIds.Add(instanceId);

            BuildStepOptions();
            BuildTargetList();
            BuildPartList();
            BuildGroupList();
            RespawnScene();
            Repaint();
            return true;
        }

        private static void OpenInOSExplorer(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[TTAW.Prefabs] Folder does not exist: {dir}");
                return;
            }
            EditorUtility.RevealInFinder(dir);
        }
    }
}
