// TTAW.Validation.cs — Validation dashboard panel + auto-run hooks.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 6 of the UX redesign. Surfaces MachinePackageValidator results as a
// first-class part of the editor instead of relying on Debug.Log spew.
//
// The dashboard lives at the bottom of the navigator pane as a collapsible
// strip showing error / warning counts and (when expanded) a click-to-jump
// list of issues. It auto-runs in two places:
//
//   • LoadPkg(...)  → on package open / domain reload
//   • WriteJson(...) → after every successful save (the user's #4 ask: "auto
//     run on every save with persistent issue list")
//
// Issues are persisted on the window via _validationIssues so they survive
// between paints; the dashboard reads from that buffer rather than re-running
// the validator on every refresh tick.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using OSE.Content;
using OSE.Content.Loading;
using OSE.Content.Validation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private MachinePackageValidationIssue[] _validationIssues = Array.Empty<MachinePackageValidationIssue>();
        private string                          _validationLastRunPkgId;
        private DateTime                        _validationLastRunUtc;

        // UITK widget references
        private VisualElement _validationRoot;
        private Foldout       _validationFoldout;
        private Label         _validationSummaryLabel;
        private ListView      _validationListView;

        // ── Auto-run hooks ────────────────────────────────────────────────────
        //
        // Callable from LoadPkg + WriteJson without either site needing to know
        // about the dashboard widgets. The widgets read _validationIssues on
        // their next paint via the existing 100 ms RefreshToolbar tick (which
        // calls RefreshValidationDashboard).

        private void RunValidation()
        {
            if (_pkg == null)
            {
                _validationIssues       = Array.Empty<MachinePackageValidationIssue>();
                _validationLastRunPkgId = null;
                _validationLastRunUtc   = DateTime.MinValue;
                return;
            }

            try
            {
                // Validator requires a normalised package; the existing
                // editor entry points (MachinePackageValidatorMenu, the pre-play
                // validator) all do this same Normalize → Validate dance.
                MachinePackageNormalizer.Normalize(_pkg);
                var result = MachinePackageValidator.Validate(_pkg);
                _validationIssues = result?.Issues ?? Array.Empty<MachinePackageValidationIssue>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TTAW.Validation] Validator threw: {ex.Message}");
                _validationIssues = Array.Empty<MachinePackageValidationIssue>();
            }

            _validationLastRunPkgId = _pkgId;
            _validationLastRunUtc   = DateTime.UtcNow;
        }

        // ── UITK build ────────────────────────────────────────────────────────

        private void BuildValidationDashboard(VisualElement parent)
        {
            _validationRoot = new VisualElement();
            _validationRoot.style.flexShrink     = 0;
            _validationRoot.style.borderTopWidth = 1;
            _validationRoot.style.borderTopColor = new Color(0f, 0f, 0f, 0.4f);
            _validationRoot.style.backgroundColor = new Color(0f, 0f, 0f, 0.18f);

            _validationFoldout = new Foldout { text = "Validation", value = false };
            _validationFoldout.style.marginLeft  = 4;
            _validationFoldout.style.marginRight = 4;
            _validationFoldout.style.marginTop   = 2;
            _validationFoldout.RegisterValueChangedCallback(evt => UpdateValidationLayout());
            _validationRoot.Add(_validationFoldout);

            _validationSummaryLabel = new Label("Validation: never run");
            _validationSummaryLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _validationSummaryLabel.style.marginLeft = 4;
            _validationSummaryLabel.style.marginTop  = 2;
            _validationFoldout.contentContainer.Add(_validationSummaryLabel);

            // Action row inside the foldout — Run Now / Clear
            var actionRow = new VisualElement();
            actionRow.style.flexDirection = FlexDirection.Row;
            actionRow.style.marginTop     = 2;
            actionRow.style.marginBottom  = 2;

            var runBtn = new Button(() => { RunValidation(); RefreshValidationDashboard(); }) { text = "Run now" };
            runBtn.style.flexGrow = 1;
            runBtn.style.marginLeft  = 2;
            runBtn.style.marginRight = 2;
            actionRow.Add(runBtn);
            _validationFoldout.contentContainer.Add(actionRow);

            // Issue list
            _validationListView = new ListView
            {
                fixedItemHeight = 22,
                selectionType   = SelectionType.Single,
                makeItem        = MakeValidationRow,
                bindItem        = BindValidationRow,
                showBoundCollectionSize = false,
            };
            _validationListView.style.flexGrow = 0;
            _validationListView.style.minHeight = 0;
            _validationListView.itemsSource = _validationIssues;
            _validationListView.selectionChanged += OnValidationIssueSelected;
            _validationFoldout.contentContainer.Add(_validationListView);

            parent.Add(_validationRoot);
            UpdateValidationLayout();
        }

        private static VisualElement MakeValidationRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.height        = 22;
            row.style.paddingLeft   = 2;
            row.style.paddingRight  = 2;

            var icon = new Label { name = "icon" };
            icon.style.width             = 14;
            icon.style.unityTextAlign    = TextAnchor.MiddleCenter;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(icon);

            var label = new Label { name = "text" };
            label.style.flexGrow         = 1;
            label.style.unityTextAlign   = TextAnchor.MiddleLeft;
            label.style.overflow         = Overflow.Hidden;
            label.style.textOverflow     = TextOverflow.Ellipsis;
            label.style.whiteSpace       = WhiteSpace.NoWrap;
            row.Add(label);

            return row;
        }

        private void BindValidationRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _validationIssues.Length) return;
            var issue = _validationIssues[index];
            var icon  = element.Q<Label>("icon");
            var text  = element.Q<Label>("text");

            bool isError = issue.Severity == MachinePackageIssueSeverity.Error;
            icon.text = isError ? "✕" : "⚠";
            icon.style.color = isError
                ? new StyleColor(new Color(0.95f, 0.30f, 0.30f))
                : new StyleColor(new Color(0.95f, 0.80f, 0.20f));

            string path = string.IsNullOrEmpty(issue.Path) ? "" : $"{issue.Path}  —  ";
            text.text   = path + issue.Message;
            text.tooltip = $"{issue.Severity}\n{issue.Path}\n{issue.Message}";
        }

        // ── Refresh & layout ──────────────────────────────────────────────────

        /// <summary>
        /// Called from the existing 100 ms RefreshToolbar tick. Cheap when the
        /// issue buffer hasn't changed (compares object reference + count).
        /// </summary>
        private void RefreshValidationDashboard()
        {
            if (_validationFoldout == null) return;

            int errors   = 0;
            int warnings = 0;
            for (int i = 0; i < _validationIssues.Length; i++)
            {
                if (_validationIssues[i].Severity == MachinePackageIssueSeverity.Error) errors++;
                else                                                                    warnings++;
            }

            string summary;
            if (_pkg == null)
            {
                summary = "(no package loaded)";
            }
            else if (_validationLastRunUtc == DateTime.MinValue)
            {
                summary = "Validation: never run";
            }
            else if (_validationIssues.Length == 0)
            {
                summary = "✓  No issues";
                _validationSummaryLabel.style.color = new Color(0.30f, 0.85f, 0.40f);
            }
            else
            {
                summary = $"✕ {errors} error{(errors == 1 ? "" : "s")}   ⚠ {warnings} warning{(warnings == 1 ? "" : "s")}";
                _validationSummaryLabel.style.color = errors > 0
                    ? new Color(0.95f, 0.40f, 0.30f)
                    : new Color(0.95f, 0.80f, 0.20f);
            }

            if (_validationSummaryLabel.text != summary)
                _validationSummaryLabel.text = summary;

            // Foldout title shows the count too so authors can read it without expanding
            string foldoutTitle = errors == 0 && warnings == 0
                ? "Validation"
                : $"Validation  ({errors + warnings})";
            if (_validationFoldout.text != foldoutTitle)
                _validationFoldout.text = foldoutTitle;

            // Re-bind the list if the buffer changed
            if (!ReferenceEquals(_validationListView.itemsSource, _validationIssues))
            {
                _validationListView.itemsSource = _validationIssues;
                _validationListView.Rebuild();
            }
            else if (_validationListView.itemsSource is Array arr && arr.Length != _validationIssues.Length)
            {
                _validationListView.itemsSource = _validationIssues;
                _validationListView.Rebuild();
            }

            UpdateValidationLayout();
        }

        private void UpdateValidationLayout()
        {
            if (_validationListView == null || _validationFoldout == null) return;
            // When the foldout is collapsed the list is hidden anyway. When
            // expanded, cap the list at ~6 rows so the dashboard doesn't push
            // the navigator tree off-screen on a short window.
            int rows  = Math.Min(_validationIssues.Length, 6);
            float h   = Math.Max(rows * 22f, 22f);
            _validationListView.style.height = h;
        }

        // ── Issue → step jump ────────────────────────────────────────────────

        private void OnValidationIssueSelected(IEnumerable<object> selected)
        {
            foreach (var obj in selected)
            {
                if (obj is MachinePackageValidationIssue issue)
                {
                    JumpToValidationIssue(issue);
                    break;
                }
            }
        }

        /// <summary>
        /// Best-effort: parse the issue's Path field for a step id and jump
        /// the navigator there. Validator paths look like
        /// "steps[step_id_here].requiredPartIds[0]" — extract the bracketed id.
        /// Falls back to no-op if the id can't be resolved against _stepIds.
        /// </summary>
        private void JumpToValidationIssue(MachinePackageValidationIssue issue)
        {
            if (string.IsNullOrEmpty(issue.Path) || _stepIds == null) return;

            const string stepsPrefix = "steps[";
            int start = issue.Path.IndexOf(stepsPrefix, StringComparison.Ordinal);
            if (start < 0) return;
            start += stepsPrefix.Length;
            int end = issue.Path.IndexOf(']', start);
            if (end <= start) return;

            string stepId = issue.Path.Substring(start, end - start);
            for (int i = 1; i < _stepIds.Length; i++)
            {
                if (string.Equals(_stepIds[i], stepId, StringComparison.Ordinal))
                {
                    ApplyStepFilter(i);
                    Repaint();
                    return;
                }
            }
        }
    }
}
