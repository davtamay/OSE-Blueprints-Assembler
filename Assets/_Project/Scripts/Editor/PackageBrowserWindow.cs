using System.Collections.Generic;
using System.IO;
using OSE.Content;
using OSE.Content.Validation;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Read-only browser for machine packages in Assets/_Project/Data/Packages/.
    /// Shows a tree of every package with its assemblies, steps, parts, and tools.
    /// Clicking a part's asset reference pings the file in the Project window.
    /// Open via: OSE → Package Browser
    /// </summary>
    public sealed class PackageBrowserWindow : EditorWindow
    {
        private const string AuthoringRoot = "Assets/_Project/Data/Packages";
        private const string MenuPath      = "OSE/Package Browser";

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var window = GetWindow<PackageBrowserWindow>("Package Browser");
            window.minSize = new Vector2(420, 500);
            window.Show();
        }

        // ── State ────────────────────────────────────────────────────────────

        private readonly List<PackageEntry> _packages   = new List<PackageEntry>();
        private Vector2 _scroll;
        private string  _status = string.Empty;
        private bool    _loaded;

        // Which tree nodes are expanded (keyed by a stable string label)
        private readonly HashSet<string> _expanded = new HashSet<string>();

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void OnEnable()
        {
            titleContent = new GUIContent("Package Browser");
            Reload();
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!_loaded)
            {
                EditorGUILayout.HelpBox("Loading packages...", MessageType.Info);
                return;
            }

            if (_packages.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    $"No packages found in {AuthoringRoot}.\nCreate a package folder there to get started.",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var entry in _packages)
                DrawPackage(entry);
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.None);
            }
        }

        // ── Drawing ──────────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Machine Packages", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60)))
                Reload();
            if (GUILayout.Button("Sync →  StreamingAssets", EditorStyles.toolbarButton, GUILayout.Width(160)))
            {
                int n = PackageSyncTool.Sync();
                AssetDatabase.Refresh();
                _status = $"Sync complete. {n} file(s) updated.";
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPackage(PackageEntry entry)
        {
            string key = entry.FolderName;
            bool expanded = IsExpanded(key);

            Color bg = expanded
                ? new Color(0.18f, 0.22f, 0.30f)
                : new Color(0.15f, 0.18f, 0.24f);

            DrawBackground(bg);
            bool newExpanded = EditorGUILayout.Foldout(expanded,
                $"  {entry.DisplayName}  [{entry.FolderName}]",
                true, PackageHeaderStyle());
            SetExpanded(key, newExpanded);

            if (!newExpanded) return;

            EditorGUI.indentLevel++;

            if (entry.LoadError != null)
            {
                EditorGUILayout.HelpBox(entry.LoadError, MessageType.Error);
                EditorGUI.indentLevel--;
                return;
            }

            var pkg = entry.Package;

            // ── Validation badge
            var validation = MachinePackageValidator.Validate(pkg);
            if (validation.HasErrors)
                EditorGUILayout.HelpBox("Validation errors — see console after saving.", MessageType.Error);
            else if (validation.HasWarnings)
                EditorGUILayout.HelpBox("Validation warnings present.", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("Package is valid.", MessageType.Info);

            // ── Machine info
            DrawField("Machine ID",   pkg.machine?.id);
            DrawField("Display Name", pkg.machine?.GetDisplayName());
            DrawField("Difficulty",   pkg.machine?.difficulty);
            DrawField("Est. Time",    $"{pkg.machine?.estimatedBuildTimeMinutes} min");

            EditorGUILayout.Space(4);

            // ── Assemblies
            DrawSection(key + ":assemblies", $"Assemblies ({pkg.GetAssemblies().Length})", () =>
            {
                foreach (var asm in pkg.GetAssemblies())
                {
                    DrawSubSection(key + ":asm:" + asm.id, $"{asm.id}", () =>
                    {
                        DrawField("Steps", string.Join(", ", asm.stepIds ?? new string[0]));
                        DrawField("Focus", asm.learningFocus);
                    });
                }
            });

            // ── Steps
            DrawSection(key + ":steps", $"Steps ({pkg.GetOrderedSteps().Length})", () =>
            {
                foreach (var step in pkg.GetOrderedSteps())
                {
                    DrawSubSection(key + ":step:" + step.id,
                        $"[{step.sequenceIndex}] {step.GetDisplayName()}", () =>
                    {
                        DrawField("Instruction", step.instructionText);
                        DrawField("Parts",       string.Join(", ", step.requiredPartIds ?? new string[0]));
                        DrawField("Tools",       string.Join(", ", step.relevantToolIds ?? new string[0]));
                        DrawField("Completion",  step.completionMode);
                    });
                }
            });

            // ── Parts
            DrawSection(key + ":parts", $"Parts ({pkg.GetParts().Length})", () =>
            {
                foreach (var part in pkg.GetParts())
                {
                    DrawSubSection(key + ":part:" + part.id, part.GetDisplayName(), () =>
                    {
                        DrawField("ID",       part.id);
                        DrawField("Material", part.material);
                        DrawField("Function", part.function);

                        // Asset ref — ping button
                        if (!string.IsNullOrWhiteSpace(part.assetRef))
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.PrefixLabel("Asset Ref");
                            if (GUILayout.Button(part.assetRef, EditorStyles.linkLabel))
                                PingAsset(entry.FolderName, part.assetRef);
                            EditorGUILayout.EndHorizontal();
                        }
                    });
                }
            });

            // ── Tools
            DrawSection(key + ":tools", $"Tools ({pkg.GetTools().Length})", () =>
            {
                foreach (var tool in pkg.GetTools())
                {
                    DrawSubSection(key + ":tool:" + tool.id, tool.GetDisplayName(), () =>
                    {
                        DrawField("Category", tool.category);
                        DrawField("Purpose",  tool.purpose);
                    });
                }
            });

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(6);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void DrawSection(string key, string label, System.Action body)
        {
            bool expanded = IsExpanded(key);
            SetExpanded(key, EditorGUILayout.Foldout(expanded, label, true));
            if (expanded) body();
        }

        private void DrawSubSection(string key, string label, System.Action body)
        {
            EditorGUI.indentLevel++;
            bool expanded = IsExpanded(key);
            SetExpanded(key, EditorGUILayout.Foldout(expanded, label, true));
            if (expanded) body();
            EditorGUI.indentLevel--;
        }

        private static void DrawField(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            EditorGUILayout.LabelField(label, value, EditorStyles.wordWrappedLabel);
        }

        private static void DrawBackground(Color color)
        {
            Rect r = GUILayoutUtility.GetRect(0, 0);
            r.height = 20;
            EditorGUI.DrawRect(r, color);
        }

        private static GUIStyle PackageHeaderStyle()
        {
            var s = new GUIStyle(EditorStyles.foldout);
            s.fontStyle = FontStyle.Bold;
            return s;
        }

        private static void PingAsset(string packageFolder, string assetRef)
        {
            // Resolve relative to authoring folder first
            string authoringPath = Path.Combine(AuthoringRoot, packageFolder, assetRef).Replace('\\', '/');
            var obj = AssetDatabase.LoadAssetAtPath<Object>(authoringPath);
            if (obj != null)
            {
                EditorGUIUtility.PingObject(obj);
                return;
            }

            // Fallback: search project for just the filename
            string filename = Path.GetFileNameWithoutExtension(assetRef);
            string[] guids = AssetDatabase.FindAssets(filename);
            if (guids.Length > 0)
            {
                obj = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guids[0]));
                if (obj != null) EditorGUIUtility.PingObject(obj);
            }
        }

        private bool IsExpanded(string key) => _expanded.Contains(key);
        private void SetExpanded(string key, bool value)
        {
            if (value) _expanded.Add(key);
            else _expanded.Remove(key);
        }

        // ── Loading ──────────────────────────────────────────────────────────

        private void Reload()
        {
            _packages.Clear();
            _status = string.Empty;
            _loaded = false;

            string root = Path.GetFullPath(AuthoringRoot);
            if (!Directory.Exists(root))
            {
                _loaded = true;
                Repaint();
                return;
            }

            foreach (string dir in Directory.GetDirectories(root))
            {
                string jsonPath = Path.Combine(dir, "machine.json");
                string folderName = Path.GetFileName(dir);

                if (!File.Exists(jsonPath))
                    continue;

                string json = File.ReadAllText(jsonPath);
                MachinePackageDefinition pkg = null;
                string error = null;

                try
                {
                    pkg = JsonUtility.FromJson<MachinePackageDefinition>(json);
                    if (pkg?.machine == null)
                        error = "JSON parsed but machine block is missing.";
                }
                catch (System.Exception ex)
                {
                    error = $"JSON parse error: {ex.Message}";
                }

                _packages.Add(new PackageEntry(folderName, pkg, error));
            }

            _packages.Sort((a, b) => string.Compare(a.FolderName, b.FolderName,
                System.StringComparison.OrdinalIgnoreCase));

            _loaded = true;
            Repaint();
        }

        // ── Inner types ──────────────────────────────────────────────────────

        private sealed class PackageEntry
        {
            public readonly string                  FolderName;
            public readonly MachinePackageDefinition Package;
            public readonly string                  LoadError;

            public string DisplayName => Package?.machine?.GetDisplayName() ?? FolderName;

            public PackageEntry(string folderName, MachinePackageDefinition package, string loadError)
            {
                FolderName = folderName;
                Package    = package;
                LoadError  = loadError;
            }
        }
    }
}
