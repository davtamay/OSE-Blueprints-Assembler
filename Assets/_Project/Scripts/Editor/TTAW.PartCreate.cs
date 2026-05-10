using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Core;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// TTAW.PartCreate.cs  —  Create-new-part flow for the Part Browser.
//
// Single path: "Browse from GLB Assets" — left scroll lists every .glb in the
// package's assets/parts folder, right pane shows the selected GLB's 3D
// preview, form fields below the preview let the author author id / name /
// category / template before committing. Selecting a GLB auto-fills the form
// via PackageAssetResolver.NormalizeToPartId + HumanizePartId; the author can
// edit any field before clicking Create.
//
// Clone-Selected was retired in Slice 1.11 — the same authoring outcome is
// reachable by selecting an existing part's GLB in this browser and editing
// the auto-suggested id (e.g. append "_copy"). Net loss for the author: zero;
// gain: one fewer toolbar tab to discover.
//
// Commit writes the new PartDefinition via PackageJsonUtils.InsertPart on the
// assembly file that owns the currently-selected step (matching the "first-
// use assembly file owns the part" convention from CLAUDE.md), or falls back
// to machine.json for monolithic packages / when no step is selected.
//
// Part of the ToolTargetAuthoringWindow partial-class split.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Types ─────────────────────────────────────────────────────────────

        internal enum PartCreateMode
        {
            None      = 0,
            FromAsset = 1,
            // Clone retired in Slice 1.11 — see header comment.
        }

        // ── State (NonSerialized — form is ephemeral) ─────────────────────────

        [NonSerialized] private PartCreateMode _createMode;
        [NonSerialized] private string _createSelectedGlbPath;       // full asset path to the GLB the author picked
        [NonSerialized] private string _createPartId        = "";
        [NonSerialized] private string _createDisplayName   = "";
        [NonSerialized] private string _createCategory      = "custom";
        [NonSerialized] private int    _createTemplateIdx;            // 0 = (none)
        [NonSerialized] private string _createGlbSearch     = "";
        [NonSerialized] private Vector2 _createGlbScroll;

        // GLB asset preview (separate from the row-selection preview in
        // DrawBrowserPreviewPane so the two don't fight for the same renderer
        // instance). Re-uses PartModelPreviewRenderer over a raw asset path.
        [NonSerialized] private PartModelPreviewRenderer _createGlbPreview;
        [NonSerialized] private string _createGlbPreviewPath;

        // ── Public-ish API (called from Browser toolbar buttons) ──────────────

        public bool IsCreatePartActive => _createMode != PartCreateMode.None;

        public void BeginCreateFromAsset()
        {
            _createMode               = PartCreateMode.FromAsset;
            _createSelectedGlbPath    = null;
            _createPartId             = "";
            _createDisplayName        = "";
            _createCategory           = "custom";
            _createTemplateIdx        = 0;
            _createGlbSearch          = "";
            _createGlbScroll          = Vector2.zero;
            DisposeCreateGlbPreview();
            PartBrowserWindow.RepaintIfOpen();
        }

        public void CancelCreatePart()
        {
            _createMode            = PartCreateMode.None;
            _createSelectedGlbPath = null;
            DisposeCreateGlbPreview();
            PartBrowserWindow.RepaintIfOpen();
        }

        private void DisposeCreateGlbPreview()
        {
            _createGlbPreview?.Dispose();
            _createGlbPreview     = null;
            _createGlbPreviewPath = null;
        }

        // ── Form rendering (called from DrawBrowserPanel right-pane branch) ──

        /// <summary>
        /// Renders the From-Asset mode body: left scroll of GLB filenames in
        /// the package's parts folder, right pane with 3D preview + form +
        /// Create/Cancel. Layout fills the panel below the toolbar.
        /// </summary>
        internal void DrawCreatePartForm(EditorWindow host)
        {
            EditorGUILayout.LabelField("New Part From GLB", EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(_pkgId))
            {
                EditorGUILayout.HelpBox("No package loaded.", MessageType.Info);
                return;
            }

            // Two-column layout: left = GLB list, right = preview + form.
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            // ── Left column — GLB list ────────────────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.Width(220f), GUILayout.ExpandHeight(true));
            DrawCreateGlbSearchField(host);
            DrawCreateGlbList(host);
            EditorGUILayout.EndVertical();

            // ── Right column — preview + form ────────────────────────────────
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            DrawCreateGlbPreviewPane(host);
            EditorGUILayout.Space(6);
            DrawCreateFormFields(host);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCreateGlbSearchField(EditorWindow host)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(18));
            string newSearch = EditorGUILayout.TextField(_createGlbSearch ?? "");
            if (!string.Equals(newSearch, _createGlbSearch, StringComparison.Ordinal))
            {
                _createGlbSearch = newSearch;
                host?.Repaint();
            }
            if (GUILayout.Button("×", EditorStyles.miniButton, GUILayout.Width(22)))
            {
                _createGlbSearch = "";
                host?.Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCreateGlbList(EditorWindow host)
        {
            string partsFolder = $"Assets/_Project/Data/Packages/{_pkgId}/assets/parts/";
            if (!Directory.Exists(partsFolder))
            {
                EditorGUILayout.HelpBox($"Folder not found:\n{partsFolder}", MessageType.Warning);
                return;
            }

            // Reverse-lookup: assetRef filename → partId already using it.
            // Lets us flag "used by 'X'" so the author sees which GLBs are
            // already adopted. Built once per repaint; small enough not to
            // need caching.
            var usedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_pkg?.parts != null)
            {
                foreach (var p in _pkg.parts)
                {
                    if (p == null || string.IsNullOrEmpty(p.assetRef)) continue;
                    if (!usedBy.ContainsKey(p.assetRef)) usedBy[p.assetRef] = p.id;
                }
            }

            string[] glbPaths;
            try { glbPaths = Directory.GetFiles(partsFolder, "*.glb"); }
            catch { glbPaths = Array.Empty<string>(); }
            Array.Sort(glbPaths, StringComparer.OrdinalIgnoreCase);

            string searchLower = (_createGlbSearch ?? "").Trim().ToLowerInvariant();

            _createGlbScroll = EditorGUILayout.BeginScrollView(_createGlbScroll,
                GUILayout.MinHeight(260f), GUILayout.ExpandHeight(true));

            int shown = 0;
            foreach (var fullPath in glbPaths)
            {
                string filename = Path.GetFileName(fullPath);
                if (!string.IsNullOrEmpty(searchLower)
                    && filename.ToLowerInvariant().IndexOf(searchLower, StringComparison.Ordinal) < 0)
                    continue;
                shown++;

                bool isSelected = string.Equals(_createSelectedGlbPath, fullPath, StringComparison.OrdinalIgnoreCase);
                bool inUse      = usedBy.TryGetValue(filename, out var ownerPartId);

                var rowStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = isSelected ? FontStyle.Bold : FontStyle.Normal,
                    normal    = { textColor = isSelected
                                    ? new Color(0.20f, 0.62f, 0.95f)
                                    : (inUse ? new Color(0.55f, 0.55f, 0.55f) : EditorStyles.label.normal.textColor) },
                    padding   = new RectOffset(6, 4, 2, 2),
                };

                var rect = GUILayoutUtility.GetRect(new GUIContent(filename), rowStyle, GUILayout.Height(20f), GUILayout.ExpandWidth(true));
                if (isSelected)
                    EditorGUI.DrawRect(rect, new Color(0.20f, 0.62f, 0.95f, 0.18f));

                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && rect.Contains(ev.mousePosition))
                {
                    SelectCreateGlb(fullPath);
                    ev.Use();
                    host?.Repaint();
                }

                GUI.Label(rect, filename, rowStyle);

                if (inUse)
                {
                    var tagStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal    = { textColor = new Color(0.55f, 0.78f, 0.95f, 0.85f) },
                    };
                    GUI.Label(rect, $"used by '{ownerPartId}' ", tagStyle);
                }
            }

            if (shown == 0)
            {
                EditorGUILayout.LabelField(
                    string.IsNullOrEmpty(searchLower)
                        ? "(no GLBs in this folder)"
                        : "(no GLBs match the filter)",
                    EditorStyles.centeredGreyMiniLabel);
            }

            EditorGUILayout.EndScrollView();
        }

        private void SelectCreateGlb(string fullPath)
        {
            _createSelectedGlbPath = fullPath;

            // Auto-fill form fields from filename — but only if the user
            // hasn't already typed anything custom (so they can pick a
            // different GLB without losing their typed id mid-edit).
            string stem = Path.GetFileNameWithoutExtension(fullPath);
            string normalizedId = PackageAssetResolver.NormalizeToPartId(stem);
            if (string.IsNullOrEmpty(_createPartId))      _createPartId      = normalizedId;
            if (string.IsNullOrEmpty(_createDisplayName)) _createDisplayName = HumanizePartId(normalizedId);

            // Refresh the preview renderer if the path changed.
            if (!string.Equals(fullPath, _createGlbPreviewPath, StringComparison.OrdinalIgnoreCase))
            {
                DisposeCreateGlbPreview();
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath) != null)
                {
                    _createGlbPreview     = new PartModelPreviewRenderer(fullPath, Vector3.zero);
                    _createGlbPreviewPath = fullPath;
                }
            }
        }

        private void DrawCreateGlbPreviewPane(EditorWindow host)
        {
            const float PreviewHeight = 200f;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_createSelectedGlbPath))
            {
                EditorGUILayout.LabelField("(pick a GLB on the left)", EditorStyles.centeredGreyMiniLabel);
                GUILayout.Space(PreviewHeight - 18f);
                EditorGUILayout.EndVertical();
                return;
            }

            if (_createGlbPreview != null)
            {
                var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(PreviewHeight), GUILayout.ExpandWidth(true));
                var ev = Event.current;
                if (ev.type == EventType.MouseDrag && rect.Contains(ev.mousePosition))
                {
                    _createGlbPreview.Orbit(ev.delta);
                    ev.Use();
                    host?.Repaint();
                }
                else if (ev.type == EventType.ScrollWheel && rect.Contains(ev.mousePosition))
                {
                    _createGlbPreview.Zoom(ev.delta.y);
                    ev.Use();
                    host?.Repaint();
                }
                if (_createGlbPreview.Draw(rect, useMm: true))
                    host?.Repaint();
            }
            else
            {
                EditorGUILayout.HelpBox($"Could not load: {_createSelectedGlbPath}", MessageType.None);
                GUILayout.Space(PreviewHeight - 40f);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawCreateFormFields(EditorWindow host)
        {
            string newId = EditorGUILayout.TextField(
                new GUIContent("Part ID", "Unique identifier. Lowercase letters, digits, underscore."),
                _createPartId ?? "");
            if (newId != _createPartId)
            {
                _createPartId = newId;
                host?.Repaint();
            }

            string idError = ValidatePartId(_createPartId);
            if (!string.IsNullOrEmpty(idError))
            {
                var errStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.95f, 0.55f, 0.45f) },
                };
                EditorGUILayout.LabelField("  " + idError, errStyle);
            }

            _createDisplayName = EditorGUILayout.TextField(
                new GUIContent("Display name", "Human-readable label. Optional."),
                _createDisplayName ?? "");
            _createCategory = EditorGUILayout.TextField(
                new GUIContent("Category", "Free-text grouping. Common values: bearing, bolt, nut, motor, custom."),
                _createCategory ?? "");

            var templates = _pkg?.partTemplates;
            if (templates != null && templates.Length > 0)
            {
                var opts = new string[templates.Length + 1];
                opts[0] = "(none)";
                for (int i = 0; i < templates.Length; i++)
                    opts[i + 1] = string.IsNullOrEmpty(templates[i]?.id) ? $"(template {i})" : templates[i].id;
                _createTemplateIdx = Mathf.Clamp(_createTemplateIdx, 0, opts.Length - 1);
                _createTemplateIdx = EditorGUILayout.Popup(
                    new GUIContent("Template", "Optional partTemplate inheritance."),
                    _createTemplateIdx, opts);
            }

            EditorGUILayout.Space(4);
            bool canCreate = string.IsNullOrEmpty(idError) && !string.IsNullOrEmpty(_createSelectedGlbPath);

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!canCreate);
            var createStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            if (GUILayout.Button("✓  Create part from this GLB", createStyle, GUILayout.Height(26)))
                CommitCreatePart();
            EditorGUI.EndDisabledGroup();
            if (GUILayout.Button("Cancel", GUILayout.Width(80), GUILayout.Height(26)))
                CancelCreatePart();
            EditorGUILayout.EndHorizontal();
        }

        // ── Commit ────────────────────────────────────────────────────────────

        private void CommitCreatePart()
        {
            if (string.IsNullOrEmpty(_pkgId) || _pkg == null) return;
            if (_createMode != PartCreateMode.FromAsset) return;
            if (!string.IsNullOrEmpty(ValidatePartId(_createPartId))) return;
            if (string.IsNullOrEmpty(_createSelectedGlbPath)) return;

            string assetRef = Path.GetFileName(_createSelectedGlbPath);
            var def = BuildNewPartDefinition(assetRef);

            string targetFile = ResolveTargetJsonForNewPart();
            if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
            {
                OseLog.Warn($"[TTAW.PartCreate] No target JSON file resolved for new part. pkgId={_pkgId}");
                return;
            }

            try
            {
                PackageJsonUtils.InsertPart(targetFile, def);
                OseLog.Info($"[TTAW.PartCreate] Inserted part '{def.id}' into {targetFile}");
            }
            catch (Exception e)
            {
                OseLog.Warn($"[TTAW.PartCreate] InsertPart failed: {e}");
                return;
            }

            string newId = def.id;
            CancelCreatePart();
            ReloadAndFocusPart(newId);
        }

        private PartDefinition BuildNewPartDefinition(string assetRef)
        {
            var def = new PartDefinition
            {
                id        = _createPartId,
                name      = string.IsNullOrEmpty(_createDisplayName) ? _createPartId : _createDisplayName,
                category  = string.IsNullOrEmpty(_createCategory) ? "custom" : _createCategory,
                assetRef  = string.IsNullOrEmpty(assetRef) ? null : assetRef,
            };

            if (_createTemplateIdx > 0
                && _pkg?.partTemplates != null
                && _createTemplateIdx - 1 < _pkg.partTemplates.Length)
            {
                var tmpl = _pkg.partTemplates[_createTemplateIdx - 1];
                if (tmpl != null && !string.IsNullOrEmpty(tmpl.id))
                    def.templateId = tmpl.id;
            }

            return def;
        }

        private string ResolveTargetJsonForNewPart()
        {
            if (_stepFilterIdx > 0 && _stepIds != null && _stepFilterIdx < _stepIds.Length)
            {
                string stepId = _stepIds[_stepFilterIdx];
                if (!string.IsNullOrEmpty(stepId))
                {
                    string fromMap = PackageJsonUtils.TryGetEntityOriginFile(_pkgId, stepId);
                    if (!string.IsNullOrEmpty(fromMap) && File.Exists(fromMap)) return fromMap;
                }
            }
            string monolithic = $"Assets/_Project/Data/Packages/{_pkgId}/machine.json";
            if (File.Exists(monolithic)) return monolithic;
            return null;
        }

        private void ReloadAndFocusPart(string newId)
        {
            try { LoadPkg(_pkgId); }
            catch (Exception e) { OseLog.Warn($"[TTAW.PartCreate] LoadPkg failed after insert: {e}"); }
            BuildPartList();
            _browserSelectedPartId = newId;
            if (IsPartBrowserOpen)
            {
                EnsureBrowserGhosts();
                PartBrowserWindow.RepaintIfOpen();
            }
            Repaint();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private string ValidatePartId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "id required";
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return "lowercase letters, digits, underscore only";
            }
            if (_pkg != null)
            {
                foreach (var p in _pkg.GetParts())
                    if (p != null && string.Equals(p.id, id, StringComparison.Ordinal))
                        return "id already exists in package";
            }
            return null;
        }

        private static string HumanizePartId(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var sb = new StringBuilder(id.Length + 4);
            bool capNext = true;
            foreach (var ch in id)
            {
                if (ch == '_' || ch == '-')
                {
                    sb.Append(' ');
                    capNext = true;
                }
                else if (capNext)
                {
                    sb.Append(char.ToUpperInvariant(ch));
                    capNext = false;
                }
                else
                {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
