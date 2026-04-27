// TTAW.Prefabs.cs — Step Configuration Prefab catalog panel (P3-lite).
// Lists prefabs in AgentAssistant/prefabs/ and instantiations in
// AgentAssistant/inputs/. Run button shells out to instantiate_prefab.py;
// emitted step JSON lands in AgentAssistant/outputs/ for manual merge.

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using OSE.Content;
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
                foreach (var path in _prefabPaths)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    Rect rowRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                    Rect labelRect  = new Rect(rowRect.x,            rowRect.y, rowRect.width - 84f, rowRect.height);
                    Rect buttonRect = new Rect(rowRect.xMax - 80f,   rowRect.y, 78f, rowRect.height);
                    GUI.Label(labelRect, new GUIContent("≡  " + Path.GetFileNameWithoutExtension(path),
                        "Drag this row onto the canvas drop zone to instantiate the prefab."),
                        new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold });
                    if (GUI.Button(buttonRect, new GUIContent("Open YAML",
                            "Reveal the prefab file in your OS file explorer to read or edit."),
                            EditorStyles.miniButton))
                    {
                        EditorUtility.RevealInFinder(path);
                    }
                    HandlePrefabRowDragSource(rowRect, path);
                    EditorGUILayout.EndHorizontal();
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
            // _inputPaths section was removed when the wizard superseded the
            // CLI workflow. GetInputsDir() is still used internally by the
            // wizard's temp-input writer.
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

        private static string GetInputsDir()
            => Path.Combine(GetRepoRoot(), "AgentAssistant", "inputs");

        private static string GetOutputsDir()
            => Path.Combine(GetRepoRoot(), "AgentAssistant", "outputs");

        private static string GetInstantiateScriptPath()
            => Path.Combine(GetRepoRoot(), "Tools", "instantiate_prefab.py");

        // ── Wizard entry (Slice 1c will flesh this out) ─────────────────────

        /// <summary>Public proxy for the wizard to look up a step by id.</summary>
        internal StepDefinition FindStepPublic(string stepId) => FindStep(stepId);

        /// <summary>
        /// Returns the maximum sequenceIndex among steps belonging to the
        /// given subassembly, or -1 if none. Used by the wizard's
        /// "drop on step → instantiate at end of subassembly" default.
        /// </summary>
        internal int GetSubassemblyMaxSeqPublic(string subassemblyId)
        {
            if (_pkg?.steps == null || string.IsNullOrEmpty(subassemblyId)) return -1;
            int max = -1;
            foreach (var s in _pkg.steps)
            {
                if (s == null) continue;
                if (!string.Equals(s.subassemblyId, subassemblyId, System.StringComparison.Ordinal)) continue;
                if (s.sequenceIndex > max) max = s.sequenceIndex;
            }
            return max;
        }

        /// <summary>Public proxy for the wizard to invoke the existing subprocess plumbing.</summary>
        internal void RunInstantiatePrefabPublic(string inputYamlPath) => RunInstantiatePrefab(inputYamlPath);

        /// <summary>Public proxy exposing the python-interpreter resolver for the wizard's inline subprocess.</summary>
        internal static string ResolvePythonExecutablePublic() => ResolvePythonExecutable();

        /// <summary>
        /// Auto-merge entry for the wizard. Parses the emitted step JSON
        /// array, stamps each step with a <see cref="PrefabRef"/>, appends
        /// to the active package's source assembly file via
        /// <see cref="PackageJsonUtils.InsertStep"/>, then reloads the
        /// package so the canvas + navigator pick up the new steps.
        /// Returns the number of steps merged (0 = nothing happened, e.g.
        /// JSON was empty or parse failed).
        /// </summary>
        internal int MergeInstantiatedStepsPublic(
            string emittedJson, string prefabId, string instanceId,
            PrefabRoleBinding[] bindings, string sourceMtime)
        {
            if (string.IsNullOrEmpty(emittedJson) || _pkg == null || string.IsNullOrEmpty(_pkgId))
                return 0;

            // The engine emits a bare JSON array of step objects. JsonUtility
            // can't parse a top-level array, so wrap it in a temporary object
            // for FromJson, then extract.
            var wrapper = new EmittedStepsWrapper();
            try
            {
                string wrappedJson = "{\"steps\":" + emittedJson + "}";
                JsonUtility.FromJsonOverwrite(wrappedJson, wrapper);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TTAW.Prefabs] Failed to parse emitted step JSON: {ex.Message}");
                return 0;
            }
            if (wrapper.steps == null || wrapper.steps.Length == 0) return 0;

            // The active step gives us an authoritative file path on disk —
            // reuse it for every emitted step so they all land in the same
            // assembly file as their target. Falls back to the package's
            // primary assembly file if the lookup fails.
            string fallbackPath = null;
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
                fallbackPath = PackageJsonUtils.FindEntityFilePath(_pkgId, _stepIds[_stepFilterIdx]);

            int merged = 0;
            foreach (var step in wrapper.steps)
            {
                if (step == null || string.IsNullOrEmpty(step.id)) continue;
                step.prefabRef = new PrefabRef
                {
                    prefabId    = prefabId,
                    instanceId  = instanceId,
                    bindings    = bindings,
                    sourceMtime = sourceMtime,
                };
                string filePath = !string.IsNullOrEmpty(fallbackPath)
                    ? fallbackPath
                    : PackageJsonUtils.FindEntityFilePath(_pkgId, step.assemblyId);
                if (string.IsNullOrEmpty(filePath))
                {
                    Debug.LogWarning($"[TTAW.Prefabs] No assembly file resolved for step '{step.id}' (assemblyId='{step.assemblyId}'); skipping.");
                    continue;
                }
                try { PackageJsonUtils.InsertStep(filePath, step); merged++; }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[TTAW.Prefabs] InsertStep failed for '{step.id}': {ex.Message}");
                }
            }

            if (merged > 0)
            {
                AssetDatabase.Refresh();
                LoadPkg(_pkgId, restoring: true);
                Repaint();
            }
            return merged;
        }

        [System.Serializable]
        private sealed class EmittedStepsWrapper
        {
            public StepDefinition[] steps;
        }

        // ── Subprocess: instantiate_prefab.py ───────────────────────────────

        private void RunInstantiatePrefab(string inputYamlPath)
        {
            string scriptPath = GetInstantiateScriptPath();
            if (!File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("Prefab instantiator missing",
                    $"Could not find {scriptPath}. The Step Configuration Prefab engine ships at Tools/instantiate_prefab.py.",
                    "OK");
                return;
            }

            string repoRoot = GetRepoRoot();
            string py = ResolvePythonExecutable();
            if (string.IsNullOrEmpty(py))
            {
                EditorUtility.DisplayDialog("Python not found",
                    "Could not find a Python interpreter on PATH. Install Python 3 (with PyYAML) and try again.",
                    "OK");
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName  = py,
                Arguments = $"\"{scriptPath}\" \"{inputYamlPath}\"",
                WorkingDirectory       = repoRoot,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };

            try
            {
                EditorUtility.DisplayProgressBar("Prefabs", $"Running instantiate_prefab.py on '{Path.GetFileName(inputYamlPath)}'…", 0.5f);
                using (var proc = Process.Start(psi))
                {
                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(30_000);

                    if (proc.ExitCode == 0)
                    {
                        OseLog.Info($"[TTAW.Prefabs] instantiate_prefab.py succeeded for '{Path.GetFileName(inputYamlPath)}'.\n{stdout}");
                        EditorUtility.RevealInFinder(GetOutputsDir());
                        EditorUtility.DisplayDialog("Prefab instantiated",
                            $"Step JSON written to AgentAssistant/outputs/. Review the file and merge into the appropriate assembly_*.json under Assets/_Project/Data/Packages/.",
                            "OK");
                    }
                    else
                    {
                        Debug.LogError($"[TTAW.Prefabs] instantiate_prefab.py exit={proc.ExitCode}\nstdout: {stdout}\nstderr: {stderr}");
                        EditorUtility.DisplayDialog("Prefab instantiation failed",
                            $"instantiate_prefab.py exited with code {proc.ExitCode}.\n\nSee Console for details.\n\nstderr:\n{stderr}",
                            "OK");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TTAW.Prefabs] Failed to launch python: {ex}");
                EditorUtility.DisplayDialog("Subprocess failed", ex.Message, "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static string ResolvePythonExecutable()
        {
            // Try a few common names — the user likely has at least one.
            string[] candidates = { "python", "python3", "py" };
            string pathEnv = System.Environment.GetEnvironmentVariable("PATH") ?? "";
            char sep = Path.PathSeparator;
            foreach (var dir in pathEnv.Split(sep))
            {
                if (string.IsNullOrEmpty(dir)) continue;
                foreach (var name in candidates)
                {
                    string full = Path.Combine(dir, name);
                    if (File.Exists(full) || File.Exists(full + ".exe")) return name;
                }
            }
            return null;
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
