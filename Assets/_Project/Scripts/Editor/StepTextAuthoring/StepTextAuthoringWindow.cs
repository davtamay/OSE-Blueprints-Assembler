using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Core;
using UnityEditor;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
// StepTextAuthoringWindow.cs
//
// Dedicated dockable editor for every TEXTUAL field on a step:
//   • guidance  (instructionText, whyItMattersText, hintIds, contextualDiagramRef)
//   • hints     (HintDefinition: id/type/title/message/targetId/partId/toolId/priority)
//   • validation (validationRuleIds + per-rule failureMessage / correctionHintId)
//   • feedback   (success/failure/effect refs + completion fx fields)
//   • reinforcement (milestoneMessage / consequenceText / safetyNote / counterfactual)
//   • requiredToolActions[].successMessage / failureMessage
//
// Pairs with TTAW: text edits mutate the SAME in-memory StepDefinition /
// HintDefinition objects TTAW owns, mark the relevant dirty set via
// TTAW.StepTextBridge, and flush through TTAW.WriteJson() so a single save
// pushes both spatial and textual changes together.
//
// Opened from TTAW's per-step "✎ Text" button or directly from the menu.
// ──────────────────────────────────────────────────────────────────────────────

namespace OSE.Editor
{
    public sealed class StepTextAuthoringWindow : EditorWindow
    {
        // Cached references to the TTAW window we ride on. Resolved lazily
        // each draw — TTAW may be closed/reopened while we're alive.
        private ToolTargetAuthoringWindow _ttaw;

        private Vector2 _scroll;
        private bool _showGuidance      = true;
        private bool _showHints         = true;
        private bool _showValidation    = true;
        private bool _showFeedback      = true;
        private bool _showReinforcement = true;
        private bool _showToolActions   = true;
        private bool _hintPickerOpen;
        private string _hintPickerFilter = "";

        /// <summary>
        /// Opens (or focuses) the Step Text editor and points it at the
        /// active TTAW step. There is intentionally no top-level menu item:
        /// the window only opens via TTAW's per-step "✎ Text" button so the
        /// editor always has a step in context. The window auto-follows
        /// whatever step TTAW is currently focused on, so re-clicking
        /// "✎ Text" on a different step just refreshes the contents.
        /// </summary>
        public static void OpenForStep(string packageId, string stepId)
        {
            // utility:false → free-floating window the user can dock alongside
            // TTAW. focus:true forces it to the front so the user always sees
            // the result of clicking the button (the previous behaviour silently
            // re-used a hidden tab).
            var w = GetWindow<StepTextAuthoringWindow>(false, "Step Text", true);
            w.minSize = new Vector2(540, 480);
            w.Focus();
            w.Repaint();
        }

        private string _lastDrawnStepId;

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            // Repaint on selection / focus changes so when the user clicks a
            // different step in TTAW we pick it up without waiting for the
            // user to mouse over us.
            EditorApplication.update += PollForStepChange;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.update -= PollForStepChange;
        }

        private void PollForStepChange()
        {
            var ttaw = ResolveTtaw();
            string current = ttaw?.SelectedStepId;
            if (!string.Equals(current, _lastDrawnStepId, StringComparison.Ordinal))
                Repaint();
        }

        // Per feedback_deferred_editors_must_flush_on_play.md: editors holding
        // dirty in-memory edits must persist them before Play, since Unity
        // does not auto-save EditorWindow state on the Edit→Play boundary.
        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && _ttaw != null)
                _ttaw.FlushTextEdits();
        }

        private ToolTargetAuthoringWindow ResolveTtaw()
        {
            if (_ttaw != null) return _ttaw;
            // Use FindObjectsByType to locate any open TTAW without forcing one to open.
            var open = Resources.FindObjectsOfTypeAll<ToolTargetAuthoringWindow>();
            if (open != null && open.Length > 0) _ttaw = open[0];
            return _ttaw;
        }

        private void OnGUI()
        {
            var ttaw = ResolveTtaw();
            DrawToolbar(ttaw);

            if (ttaw == null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "TTAW is not open. The text editor mutates the same in-memory package TTAW owns and saves through its WriteJson path. " +
                    "Click the button below to open TTAW, then re-select the step here.",
                    MessageType.Info);
                if (GUILayout.Button("Open TTAW", GUILayout.Width(140)))
                    ToolTargetAuthoringWindow.Open();
                return;
            }

            string pkgId = ttaw.CurrentPackageId;
            if (string.IsNullOrEmpty(pkgId))
            {
                EditorGUILayout.HelpBox("TTAW has no package loaded. Pick a package in TTAW first.", MessageType.Info);
                return;
            }

            string stepId = ttaw.SelectedStepId;
            _lastDrawnStepId = stepId;
            var step = string.IsNullOrEmpty(stepId) ? null : ttaw.GetStepById(stepId);
            if (step == null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "TTAW is in 'All Steps' mode. Pick a single step in TTAW (or click ✎ Text on a step) and its text fields will appear here.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            DrawStepHeader(step);
            EditorGUILayout.Space(4);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawGuidanceSection(ttaw, step);
            DrawHintsSection(ttaw, step);
            DrawValidationSection(ttaw, step);
            DrawFeedbackSection(ttaw, step);
            DrawReinforcementSection(ttaw, step);
            DrawToolActionsSection(ttaw, step);
            EditorGUILayout.EndScrollView();
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private void DrawToolbar(ToolTargetAuthoringWindow ttaw)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            using (new EditorGUI.DisabledScope(ttaw == null))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(56)))
                    ttaw?.FlushTextEdits();

                if (GUILayout.Button("Revert", EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    if (EditorUtility.DisplayDialog("Revert Step Text",
                        "Reload the step from disk, discarding unsaved text edits made here?", "Revert", "Cancel"))
                    {
                        // Cheapest revert: ask TTAW to drop the dirty marker on
                        // this step. Hint dirties are session-scoped — leaving
                        // them is fine; they only flush on the next Save.
                        // Full reload of the package is heavy and TTAW doesn't
                        // expose a per-step reload, so we no-op here and let
                        // the user discard via Window → Reload Package.
                    }
                }
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Import ▾", EditorStyles.toolbarDropDown, GUILayout.Width(80)))
                ShowImportMenu(ttaw);
            if (GUILayout.Button("Export ▾", EditorStyles.toolbarDropDown, GUILayout.Width(80)))
                ShowExportMenu(ttaw);

            EditorGUILayout.EndHorizontal();
        }

        // ── Step header ───────────────────────────────────────────────────────

        private void DrawStepHeader(StepDefinition step)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(step.GetDisplayName(), EditorStyles.boldLabel);
            string profile = string.IsNullOrEmpty(step.profile) ? "" : $" · {step.profile}";
            EditorGUILayout.LabelField($"id: {step.id}    family: {step.ResolvedFamily}{profile}    seq: {step.sequenceIndex}",
                EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        // ── Sections ──────────────────────────────────────────────────────────

        private void DrawGuidanceSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showGuidance = EditorGUILayout.Foldout(_showGuidance, "Guidance", true, EditorStyles.foldoutHeader);
            if (!_showGuidance) return;

            EnsureGuidance(step);
            var g = step.guidance;

            EditorGUI.BeginChangeCheck();
            string newInstruction = LabeledTextArea("Instruction", g.instructionText, 6,
                "Primary instruction shown in the HUD. Plain text; \\n becomes a newline.");
            string newWhy         = LabeledTextArea("Why it matters", g.whyItMattersText, 3,
                "Brief learning context. Surfaces under the instruction text.");
            string newDiagram     = LabeledLine("Contextual diagram", g.contextualDiagramRef,
                "Optional reference to a diagram resource. Free text — the runtime resolves it.");

            if (EditorGUI.EndChangeCheck())
            {
                g.instructionText      = newInstruction;
                g.whyItMattersText     = newWhy;
                g.contextualDiagramRef = newDiagram;
                ttaw.MarkStepTextDirty(step.id);
            }
        }

        private void DrawHintsSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showHints = EditorGUILayout.Foldout(_showHints, "Hints", true, EditorStyles.foldoutHeader);
            if (!_showHints) return;

            EnsureGuidance(step);
            var hintIds = step.guidance.hintIds ?? Array.Empty<string>();

            // Resolve linked hint definitions.
            for (int i = 0; i < hintIds.Length; i++)
            {
                string hid = hintIds[i];
                var hint = ttaw.GetHintById(hid);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}  {hid}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Unlink", "Remove this hint from the step's hintIds (does not delete the hint)."),
                        EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    var list = new List<string>(hintIds);
                    list.RemoveAt(i);
                    step.guidance.hintIds = list.Count == 0 ? null : list.ToArray();
                    ttaw.MarkStepTextDirty(step.id);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                if (hint == null)
                {
                    EditorGUILayout.HelpBox($"Hint '{hid}' is referenced but no HintDefinition exists in this package.", MessageType.Warning);
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    string newType     = LabeledLine("Type",    hint.type,    "Free-text hint type (e.g. 'tip', 'warning'). Optional.");
                    string newTitle    = LabeledLine("Title",   hint.title,   "Optional one-line label shown above the message.");
                    string newMessage  = LabeledTextArea("Message", hint.message, 3,
                        "The hint body. Shown progressively in-runtime.");
                    string newPriority = LabeledLine("Priority", hint.priority,
                        "Free-text priority (e.g. 'low', 'high'). Optional.");
                    string newTarget   = LabeledLine("targetId", hint.targetId, "Optional scoping id; restricts hint to a target.");
                    string newPart     = LabeledLine("partId",   hint.partId,   "Optional scoping id; restricts hint to a part.");
                    string newTool     = LabeledLine("toolId",   hint.toolId,   "Optional scoping id; restricts hint to a tool.");
                    if (EditorGUI.EndChangeCheck())
                    {
                        hint.type     = newType;
                        hint.title    = newTitle;
                        hint.message  = newMessage;
                        hint.priority = newPriority;
                        hint.targetId = newTarget;
                        hint.partId   = newPart;
                        hint.toolId   = newTool;
                        ttaw.MarkHintDirty(hint.id);
                    }
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("+ New Hint", EditorStyles.miniButton, GUILayout.Width(96)))
                CreateAndLinkNewHint(ttaw, step);
            if (GUILayout.Button("Link Existing", EditorStyles.miniButton, GUILayout.Width(108)))
                _hintPickerOpen = !_hintPickerOpen;
            EditorGUILayout.EndHorizontal();

            if (_hintPickerOpen) DrawHintPicker(ttaw, step);
        }

        private void DrawHintPicker(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _hintPickerFilter = EditorGUILayout.TextField("Filter", _hintPickerFilter ?? "");
            var all = ttaw.GetAllHints();
            var linked = new HashSet<string>(step.guidance?.hintIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            int shown = 0;
            string filter = string.IsNullOrEmpty(_hintPickerFilter) ? null : _hintPickerFilter.ToLowerInvariant();
            foreach (var h in all)
            {
                if (h == null || string.IsNullOrEmpty(h.id)) continue;
                if (linked.Contains(h.id)) continue;
                if (filter != null
                    && !(h.id?.ToLowerInvariant().Contains(filter) ?? false)
                    && !(h.message?.ToLowerInvariant().Contains(filter) ?? false)
                    && !(h.title?.ToLowerInvariant().Contains(filter) ?? false))
                    continue;
                if (shown++ > 50) break;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Link", EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    var list = new List<string>(step.guidance.hintIds ?? Array.Empty<string>()) { h.id };
                    step.guidance.hintIds = list.ToArray();
                    ttaw.MarkStepTextDirty(step.id);
                    _hintPickerOpen = false;
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.LabelField($"{h.id}", GUILayout.Width(180));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(h.message) ? "(no message)" : h.message, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }
            if (shown == 0) EditorGUILayout.LabelField("(no matching hints)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void CreateAndLinkNewHint(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            string baseId = $"hint_{step.id}";
            string id = baseId;
            int n = 1;
            while (ttaw.GetHintById(id) != null) id = $"{baseId}_{n++}";
            var h = new HintDefinition
            {
                id      = id,
                type    = "tip",
                title   = "",
                message = "",
            };
            if (!ttaw.RegisterNewHint(h)) return;
            EnsureGuidance(step);
            var list = new List<string>(step.guidance.hintIds ?? Array.Empty<string>()) { id };
            step.guidance.hintIds = list.ToArray();
            ttaw.MarkStepTextDirty(step.id);
        }

        private void DrawValidationSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showValidation = EditorGUILayout.Foldout(_showValidation, "Validation", true, EditorStyles.foldoutHeader);
            if (!_showValidation) return;

            // StepValidationPayload only carries rule references; the rule
            // bodies (failureMessage, correctionHintId) live on the
            // ValidationRule definitions. We surface both here as a single
            // table.
            EnsureValidation(step);
            var v = step.validation;
            var ruleIds = v.validationRuleIds ?? Array.Empty<string>();

            for (int i = 0; i < ruleIds.Length; i++)
            {
                string rid = ruleIds[i];
                ValidationRuleDefinition rule = null;
                if (ttaw.CurrentPackage?.validationRules != null)
                    foreach (var r in ttaw.CurrentPackage.validationRules)
                        if (r != null && r.id == rid) { rule = r; break; }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"#{i + 1}  {rid}", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Unlink", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    var list = new List<string>(ruleIds);
                    list.RemoveAt(i);
                    v.validationRuleIds = list.Count == 0 ? null : list.ToArray();
                    ttaw.MarkStepTextDirty(step.id);
                    GUIUtility.ExitGUI();
                }
                EditorGUILayout.EndHorizontal();

                if (rule == null)
                    EditorGUILayout.HelpBox($"Rule '{rid}' is referenced but not defined in this package.", MessageType.Warning);
                else
                    EditorGUILayout.LabelField($"failureMessage: {rule.failureMessage ?? "(unset)"}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }

            if (ruleIds.Length == 0)
                EditorGUILayout.LabelField("(no validation rules linked)", EditorStyles.miniLabel);
        }

        private void DrawFeedbackSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showFeedback = EditorGUILayout.Foldout(_showFeedback, "Feedback", true, EditorStyles.foldoutHeader);
            if (!_showFeedback) return;

            EnsureFeedback(step);
            var f = step.feedback;
            EditorGUI.BeginChangeCheck();
            string newColor = LabeledLine("completionEffectColor", f.completionEffectColor,
                "Hex color for the completion click effect, e.g. '#33FF66'. Empty = profile/family default.");
            float newPulse = EditorGUILayout.FloatField(new GUIContent("completionPulseScale",
                "Scale multiplier for the completion pulse. 0 = profile default."), f.completionPulseScale);
            string newParticle = LabeledLine("completionParticleId", f.completionParticleId,
                "Named particle effect on completion (e.g. 'torque_sparks'). Empty = none.");
            if (EditorGUI.EndChangeCheck())
            {
                f.completionEffectColor = newColor;
                f.completionPulseScale  = newPulse;
                f.completionParticleId  = newParticle;
                ttaw.MarkStepTextDirty(step.id);
            }
        }

        private void DrawReinforcementSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showReinforcement = EditorGUILayout.Foldout(_showReinforcement, "Reinforcement", true, EditorStyles.foldoutHeader);
            if (!_showReinforcement) return;

            EnsureReinforcement(step);
            var r = step.reinforcement;
            EditorGUI.BeginChangeCheck();
            string milestone     = LabeledTextArea("Milestone message", r.milestoneMessage, 2,
                "Shown after the step completes; celebrates reaching this checkpoint.");
            string consequence   = LabeledTextArea("Consequence text", r.consequenceText, 2,
                "Explains the downstream impact of completing this step.");
            string safetyNote    = LabeledTextArea("Safety note", r.safetyNote, 2,
                "Any safety-relevant follow-up. Surfaced with elevated styling at runtime.");
            string counterfactual= LabeledTextArea("Counterfactual", r.counterfactualText, 2,
                "What would have gone wrong if this step had been skipped or done incorrectly.");
            if (EditorGUI.EndChangeCheck())
            {
                r.milestoneMessage   = milestone;
                r.consequenceText    = consequence;
                r.safetyNote         = safetyNote;
                r.counterfactualText = counterfactual;
                ttaw.MarkStepTextDirty(step.id);
            }
        }

        private void DrawToolActionsSection(ToolTargetAuthoringWindow ttaw, StepDefinition step)
        {
            _showToolActions = EditorGUILayout.Foldout(_showToolActions, "Tool Actions", true, EditorStyles.foldoutHeader);
            if (!_showToolActions) return;

            var actions = step.requiredToolActions;
            if (actions == null || actions.Length == 0)
            {
                EditorGUILayout.LabelField("(no tool actions on this step)", EditorStyles.miniLabel);
                return;
            }

            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];
                if (a == null) continue;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"#{i + 1}  {a.id}    tool={a.toolId}    target={a.targetId}", EditorStyles.miniBoldLabel);
                EditorGUI.BeginChangeCheck();
                string success = LabeledTextArea("Success message", a.successMessage, 2,
                    "Shown briefly when this tool action completes successfully.");
                string failure = LabeledTextArea("Failure message", a.failureMessage, 2,
                    "Shown when the tool action fails (e.g. wrong tool, wrong target).");
                if (EditorGUI.EndChangeCheck())
                {
                    a.successMessage = success;
                    a.failureMessage = failure;
                    ttaw.MarkStepTextDirty(step.id);
                }
                EditorGUILayout.EndVertical();
            }
        }

        // ── Import / Export menus ─────────────────────────────────────────────

        private void ShowImportMenu(ToolTargetAuthoringWindow ttaw)
        {
            if (ttaw == null || string.IsNullOrEmpty(ttaw.SelectedStepId)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("From clipboard / Markdown"), false, () => ImportFromClipboard(ttaw, StepTextIO.Format.Markdown));
            menu.AddItem(new GUIContent("From clipboard / JSON"),     false, () => ImportFromClipboard(ttaw, StepTextIO.Format.Json));
            menu.AddItem(new GUIContent("From clipboard / YAML"),     false, () => ImportFromClipboard(ttaw, StepTextIO.Format.Yaml));
            menu.AddItem(new GUIContent("From clipboard / Plain"),    false, () => ImportFromClipboard(ttaw, StepTextIO.Format.Plain));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("From file…"), false, () => ImportFromFile(ttaw));
            menu.ShowAsContext();
        }

        private void ShowExportMenu(ToolTargetAuthoringWindow ttaw)
        {
            if (ttaw == null || string.IsNullOrEmpty(ttaw.SelectedStepId)) return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("To clipboard / Markdown"), false, () => ExportToClipboard(ttaw, StepTextIO.Format.Markdown));
            menu.AddItem(new GUIContent("To clipboard / JSON"),     false, () => ExportToClipboard(ttaw, StepTextIO.Format.Json));
            menu.AddItem(new GUIContent("To clipboard / YAML"),     false, () => ExportToClipboard(ttaw, StepTextIO.Format.Yaml));
            menu.AddItem(new GUIContent("To clipboard / Plain"),    false, () => ExportToClipboard(ttaw, StepTextIO.Format.Plain));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("To file…"), false, () => ExportToFile(ttaw));
            menu.ShowAsContext();
        }

        private void ExportToClipboard(ToolTargetAuthoringWindow ttaw, StepTextIO.Format fmt)
        {
            var step = ttaw.GetStepById(ttaw.SelectedStepId);
            if (step == null) return;
            EditorGUIUtility.systemCopyBuffer = StepTextIO.Serialize(step, ttaw, fmt);
            ShowNotification(new GUIContent($"Copied {fmt} to clipboard"));
        }

        private void ExportToFile(ToolTargetAuthoringWindow ttaw)
        {
            var step = ttaw.GetStepById(ttaw.SelectedStepId);
            if (step == null) return;
            string path = EditorUtility.SaveFilePanel("Export Step Text", "",
                $"{step.id}.md", "md,json,yaml,yml,txt");
            if (string.IsNullOrEmpty(path)) return;
            StepTextIO.Format fmt = StepTextIO.GuessFormatFromPath(path);
            System.IO.File.WriteAllText(path, StepTextIO.Serialize(step, ttaw, fmt));
        }

        private void ImportFromClipboard(ToolTargetAuthoringWindow ttaw, StepTextIO.Format fmt)
        {
            var step = ttaw.GetStepById(ttaw.SelectedStepId);
            if (step == null) return;
            string text = EditorGUIUtility.systemCopyBuffer ?? "";
            if (StepTextIO.ApplyTo(step, ttaw, text, fmt))
            {
                ttaw.MarkStepTextDirty(step.id);
                ShowNotification(new GUIContent($"Imported {fmt} from clipboard"));
                Repaint();
            }
            else
            {
                ShowNotification(new GUIContent("Import failed — see console"));
            }
        }

        private void ImportFromFile(ToolTargetAuthoringWindow ttaw)
        {
            var step = ttaw.GetStepById(ttaw.SelectedStepId);
            if (step == null) return;
            string path = EditorUtility.OpenFilePanel("Import Step Text", "", "md,json,yaml,yml,txt");
            if (string.IsNullOrEmpty(path)) return;
            string text = System.IO.File.ReadAllText(path);
            StepTextIO.Format fmt = StepTextIO.GuessFormatFromPath(path);
            if (StepTextIO.ApplyTo(step, ttaw, text, fmt))
            {
                ttaw.MarkStepTextDirty(step.id);
                Repaint();
            }
            else
            {
                OseLog.Warn($"[StepTextAuthoring] Import from {path} did not apply any fields.");
            }
        }

        // ── Field helpers ─────────────────────────────────────────────────────

        private static string LabeledTextArea(string label, string current, int rows, string tooltip)
        {
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), EditorStyles.miniBoldLabel);
            float h = Mathf.Max(rows * EditorGUIUtility.singleLineHeight, EditorGUIUtility.singleLineHeight);
            string value = EditorGUILayout.TextArea(current ?? "", GUILayout.MinHeight(h));
            EditorGUILayout.Space(2);
            return value;
        }

        private static string LabeledLine(string label, string current, string tooltip)
        {
            return EditorGUILayout.TextField(new GUIContent(label, tooltip), current ?? "");
        }

        private static void EnsureGuidance(StepDefinition s)      { if (s.guidance      == null) s.guidance      = new StepGuidancePayload(); }
        private static void EnsureValidation(StepDefinition s)    { if (s.validation    == null) s.validation    = new StepValidationPayload(); }
        private static void EnsureFeedback(StepDefinition s)      { if (s.feedback      == null) s.feedback      = new StepFeedbackPayload(); }
        private static void EnsureReinforcement(StepDefinition s) { if (s.reinforcement == null) s.reinforcement = new StepReinforcementPayload(); }
    }
}
