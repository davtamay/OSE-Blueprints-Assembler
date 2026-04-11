// TTAW.Shell.cs — UITK shell + native toolbar that hosts the existing IMGUI.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 2 of the UX redesign. The window is structurally a UI Toolkit
// EditorWindow: Unity calls CreateGUI() once after OnEnable, we build a
// VisualElement root, and the body of the window is a single full-width
// IMGUIContainer that calls DrawAuthoringIMGUI() each frame.
//
// Phase 2 adds a real UITK toolbar above that container with:
//   • Package dropdown (replaces DrawPkgPicker)
//   • Step nav buttons + step number field + step count label (replaces the
//     navigation row of DrawStepFilter)
//   • Step title label (truncates with ellipsis on resize)
//   • Dirty indicator showing total unsaved item count across all dirty sets
//   • + New Step toggle (still opens the IMGUI new-step form below)
//
// The toolbar polls IMGUI-side state every 100 ms via VisualElement.schedule
// so it stays in sync with mutations from anywhere (SessionDriver step
// changes, dirty marks set inside IMGUI panels, undo, etc.) without each
// mutation site needing to know about the toolbar.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using System.Linq;
using OSE.Content;
using OSE.Runtime.Preview;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── UITK toolbar widget references ────────────────────────────────────

        private VisualElement        _toolbar;                  // root container holding both rows
        private PopupField<string>   _toolbarPackageField;
        private ToolbarButton        _toolbarPackageRefreshBtn;
        private ToolbarButton        _toolbarFirstStepBtn;
        private ToolbarButton        _toolbarPrevStepBtn;
        private IntegerField         _toolbarStepField;
        private Label                _toolbarStepCountLabel;
        private ToolbarButton        _toolbarNextStepBtn;
        private ToolbarButton        _toolbarLastStepBtn;
        private Label                _toolbarStepTitleLabel;
        private Label                _toolbarDirtyLabel;
        private ToolbarButton        _toolbarNewStepBtn;
        private ToolbarToggle        _toolbarInspectorBtn;

        // ── Three-pane shell references ───────────────────────────────────────
        private TwoPaneSplitView     _outerSplit;     // navigator | (canvas + inspector)
        private TwoPaneSplitView     _innerSplit;     // canvas | inspector  (only when visible)
        private VisualElement        _canvasPane;     // middle pane (IMGUI body)
        private VisualElement        _inspectorPane;  // right pane  (IMGUI inspector)
        private VisualElement        _rightSideHost;  // wrapper around inner split / lone canvas

        // ── UITK shell ────────────────────────────────────────────────────────

        private void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.flexGrow      = 1;
            root.style.flexDirection = FlexDirection.Column;

            BuildToolbar(root);

            // ── Three-pane shell ──────────────────────────────────────────────
            // Outer split: NAVIGATOR | (CANVAS + INSPECTOR)
            // Inner split: CANVAS | INSPECTOR (the inspector can be toggled
            // off via the toolbar; when off the canvas takes the full width
            // of the right side host).
            _outerSplit = new TwoPaneSplitView(0, 240f, TwoPaneSplitViewOrientation.Horizontal);
            _outerSplit.style.flexGrow = 1;

            // Left — navigator pane
            var navPane = new VisualElement();
            navPane.style.flexGrow = 1;
            navPane.style.minWidth = 180;
            navPane.style.overflow = Overflow.Hidden;
            BuildNavigator(navPane);
            _outerSplit.Add(navPane);

            // Right side host — owns the canvas and (optionally) the inspector
            _rightSideHost = new VisualElement();
            _rightSideHost.style.flexGrow      = 1;
            _rightSideHost.style.flexDirection = FlexDirection.Row;
            _rightSideHost.style.minWidth      = 320;
            _rightSideHost.style.overflow      = Overflow.Hidden;
            _outerSplit.Add(_rightSideHost);

            // Canvas pane — the existing IMGUI body
            _canvasPane = new VisualElement();
            _canvasPane.style.flexGrow = 1;
            _canvasPane.style.minWidth = 320;
            _canvasPane.style.overflow = Overflow.Hidden;
            // Single IMGUIContainer that runs the existing OnGUI body (now
            // renamed DrawAuthoringIMGUI in TTAW.Layout.cs). All Event.current,
            // GUILayout, EditorGUILayout, position, and Repaint() calls work the
            // same inside an IMGUIContainer as they did when Unity called OnGUI
            // directly.
            var canvasHost = new IMGUIContainer(DrawAuthoringIMGUI)
            {
                name = "ttaw-imgui-canvas"
            };
            canvasHost.style.flexGrow = 1;
            _canvasPane.Add(canvasHost);

            // Inspector pane — IMGUIContainer that runs DrawInspectorIMGUI
            _inspectorPane = new VisualElement();
            _inspectorPane.style.flexGrow = 1;
            _inspectorPane.style.minWidth = 240;
            _inspectorPane.style.overflow = Overflow.Hidden;
            _inspectorPane.style.borderLeftWidth = 1;
            _inspectorPane.style.borderLeftColor = new Color(0f, 0f, 0f, 0.35f);
            var inspectorHost = new IMGUIContainer(DrawInspectorIMGUI)
            {
                name = "ttaw-imgui-inspector"
            };
            inspectorHost.style.flexGrow = 1;
            _inspectorPane.Add(inspectorHost);

            ApplyInspectorVisibility();

            root.Add(_outerSplit);

            // Poll IMGUI-side state changes (dirty flags, step changes from
            // SessionDriver, package reloads, etc.) and refresh the toolbar +
            // navigator so they stay in sync without each mutation site needing
            // to know about the UITK widgets.
            root.schedule.Execute(RefreshToolbar).Every(100);

            RefreshToolbar();
        }

        // ── Inspector visibility (toolbar toggle) ─────────────────────────────

        /// <summary>
        /// Rebuilds the right-side host to reflect <see cref="_inspectorVisible"/>.
        /// When visible, host = inner TwoPaneSplitView(canvas | inspector).
        /// When hidden, host = canvas only (full width of the right side).
        /// Called from CreateGUI and from the toolbar toggle handler.
        /// </summary>
        private void ApplyInspectorVisibility()
        {
            if (_rightSideHost == null) return;

            // Detach existing children — both panes are reusable VisualElements
            // we created in CreateGUI, so they survive the swap intact.
            _canvasPane?.RemoveFromHierarchy();
            _inspectorPane?.RemoveFromHierarchy();
            _innerSplit?.RemoveFromHierarchy();
            _innerSplit = null;
            _rightSideHost.Clear();

            if (_inspectorVisible)
            {
                _innerSplit = new TwoPaneSplitView(1, 280f, TwoPaneSplitViewOrientation.Horizontal);
                _innerSplit.style.flexGrow = 1;
                _innerSplit.Add(_canvasPane);
                _innerSplit.Add(_inspectorPane);
                _rightSideHost.Add(_innerSplit);
            }
            else
            {
                _rightSideHost.Add(_canvasPane);
            }
        }

        // ── Toolbar build ─────────────────────────────────────────────────────

        private void BuildToolbar(VisualElement root)
        {
            // Two-row toolbar so a narrow window doesn't squish controls onto a
            // single line. Row 1 = package + global status/actions. Row 2 = step
            // navigation + full-width step title. Each row is its own Toolbar so
            // it picks up Unity's native toolbar styling (background, separator).
            var toolbarRoot = new VisualElement();
            toolbarRoot.style.flexDirection = FlexDirection.Column;
            toolbarRoot.style.flexShrink    = 0;

            // ── Row 1 — package · dirty · + New Step ──────────────────────────
            var row1 = new Toolbar();
            row1.style.flexShrink = 0;

            _toolbarPackageField = new PopupField<string>(new List<string> { "(no packages)" }, 0);
            _toolbarPackageField.style.minWidth   = 160;
            _toolbarPackageField.style.flexGrow   = 0;
            _toolbarPackageField.style.marginLeft = 4;
            _toolbarPackageField.style.marginRight = 2;
            _toolbarPackageField.tooltip = "Active machine package";
            _toolbarPackageField.RegisterValueChangedCallback(OnToolbarPackageChanged);
            row1.Add(_toolbarPackageField);

            _toolbarPackageRefreshBtn = new ToolbarButton(RefreshPackageList) { text = "↺" };
            _toolbarPackageRefreshBtn.tooltip = "Refresh package list from disk";
            _toolbarPackageRefreshBtn.style.width = 24;
            row1.Add(_toolbarPackageRefreshBtn);

            // Flex spacer pushes the right-side widgets to the far edge
            var row1Spacer = new VisualElement();
            row1Spacer.style.flexGrow = 1;
            row1.Add(row1Spacer);

            // Dirty indicator (right-aligned, blank when nothing dirty)
            _toolbarDirtyLabel = new Label(string.Empty);
            _toolbarDirtyLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _toolbarDirtyLabel.style.marginLeft  = 4;
            _toolbarDirtyLabel.style.marginRight = 6;
            _toolbarDirtyLabel.style.color = new Color(0.95f, 0.65f, 0.15f);
            _toolbarDirtyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolbarDirtyLabel.tooltip = "Unsaved authoring changes";
            row1.Add(_toolbarDirtyLabel);

            _toolbarInspectorBtn = new ToolbarToggle { text = "Inspector", value = _inspectorVisible };
            _toolbarInspectorBtn.tooltip = "Show/hide the right-side inspector pane";
            _toolbarInspectorBtn.RegisterValueChangedCallback(evt =>
            {
                _inspectorVisible = evt.newValue;
                ApplyInspectorVisibility();
                Repaint();
            });
            row1.Add(_toolbarInspectorBtn);

            _toolbarNewStepBtn = new ToolbarButton(OnToolbarNewStepClicked) { text = "+ New Step" };
            _toolbarNewStepBtn.tooltip = "Create a new step in the active assembly";
            row1.Add(_toolbarNewStepBtn);

            toolbarRoot.Add(row1);

            // ── Row 2 — step navigation + step title ──────────────────────────
            var row2 = new Toolbar();
            row2.style.flexShrink = 0;

            _toolbarFirstStepBtn = new ToolbarButton(() => { ApplyStepFilter(1); Repaint(); }) { text = "◄◄" };
            _toolbarFirstStepBtn.tooltip = "Jump to first step";
            _toolbarFirstStepBtn.style.width = 30;
            _toolbarFirstStepBtn.style.marginLeft = 4;
            row2.Add(_toolbarFirstStepBtn);

            _toolbarPrevStepBtn = new ToolbarButton(() => { ApplyStepFilter(_stepFilterIdx - 1); Repaint(); }) { text = "◄" };
            _toolbarPrevStepBtn.tooltip = "Previous step";
            _toolbarPrevStepBtn.style.width = 26;
            row2.Add(_toolbarPrevStepBtn);

            _toolbarStepField = new IntegerField { isDelayed = true };
            _toolbarStepField.style.width = 48;
            _toolbarStepField.style.marginLeft  = 2;
            _toolbarStepField.style.marginRight = 0;
            _toolbarStepField.tooltip = "Step number — type to jump, scroll-wheel to scrub";
            _toolbarStepField.RegisterValueChangedCallback(OnToolbarStepFieldChanged);
            _toolbarStepField.RegisterCallback<WheelEvent>(OnToolbarStepFieldWheel);
            row2.Add(_toolbarStepField);

            _toolbarStepCountLabel = new Label("/0");
            _toolbarStepCountLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolbarStepCountLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _toolbarStepCountLabel.style.marginLeft  = 2;
            _toolbarStepCountLabel.style.marginRight = 4;
            row2.Add(_toolbarStepCountLabel);

            _toolbarNextStepBtn = new ToolbarButton(() => { ApplyStepFilter(_stepFilterIdx + 1); Repaint(); }) { text = "►" };
            _toolbarNextStepBtn.tooltip = "Next step";
            _toolbarNextStepBtn.style.width = 26;
            row2.Add(_toolbarNextStepBtn);

            _toolbarLastStepBtn = new ToolbarButton(() =>
            {
                int last = (_stepOptions?.Length ?? 1) - 1;
                if (last >= 1) { ApplyStepFilter(last); Repaint(); }
            }) { text = "►►" };
            _toolbarLastStepBtn.tooltip = "Jump to last step";
            _toolbarLastStepBtn.style.width = 30;
            row2.Add(_toolbarLastStepBtn);

            // Visual divider between nav cluster and title
            var divider = new VisualElement();
            divider.style.width = 1;
            divider.style.marginLeft  = 8;
            divider.style.marginRight = 8;
            divider.style.marginTop    = 3;
            divider.style.marginBottom = 3;
            divider.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            row2.Add(divider);

            // Step title — fills remaining space, ellipses on overflow
            _toolbarStepTitleLabel = new Label(string.Empty);
            _toolbarStepTitleLabel.style.flexGrow  = 1;
            _toolbarStepTitleLabel.style.flexShrink = 1;
            _toolbarStepTitleLabel.style.minWidth   = 0;
            _toolbarStepTitleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _toolbarStepTitleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _toolbarStepTitleLabel.style.overflow = Overflow.Hidden;
            _toolbarStepTitleLabel.style.textOverflow = TextOverflow.Ellipsis;
            _toolbarStepTitleLabel.style.whiteSpace = WhiteSpace.NoWrap;
            row2.Add(_toolbarStepTitleLabel);

            toolbarRoot.Add(row2);

            root.Add(toolbarRoot);
            _toolbar = toolbarRoot;
        }

        // ── Toolbar event handlers ────────────────────────────────────────────

        private void OnToolbarPackageChanged(ChangeEvent<string> evt)
        {
            if (_packageIds == null || _packageIds.Length == 0) return;
            int idx = Array.IndexOf(_packageIds, evt.newValue);
            if (idx < 0 || idx == _pkgIdx) return;

            _pkgIdx = idx;
            LoadPkg(_packageIds[idx]);

            // Sync EditModePreviewDriver so spawned parts match the new package.
            var driver = UnityEngine.Object.FindFirstObjectByType<EditModePreviewDriver>();
            if (driver != null) driver.SetPackage(_packageIds[idx]);

            Repaint();
            RefreshToolbar();
        }

        private void OnToolbarStepFieldChanged(ChangeEvent<int> evt)
        {
            if (_stepOptions == null || _stepOptions.Length == 0) return;
            int max = _stepOptions.Length - 1;
            int v   = Mathf.Clamp(evt.newValue, 0, max);
            if (v != _stepFilterIdx)
            {
                ApplyStepFilter(v);
                Repaint();
            }
        }

        private void OnToolbarStepFieldWheel(WheelEvent evt)
        {
            if (_stepOptions == null || _stepOptions.Length == 0) return;
            int max   = _stepOptions.Length - 1;
            int delta = evt.delta.y > 0f ? -1 : 1; // wheel down = previous
            int v     = Mathf.Clamp(_stepFilterIdx + delta, 0, max);
            if (v != _stepFilterIdx)
            {
                ApplyStepFilter(v);
                Repaint();
                evt.StopPropagation();
            }
        }

        private void OnToolbarNewStepClicked()
        {
            _showNewStepForm    = !_showNewStepForm;
            _newStepId          = string.Empty;
            _newStepName        = string.Empty;
            _newStepFamilyIdx   = 0;
            _newStepProfileIdx  = 0;
            _newStepAssemblyIdx = 0;

            int afterSeq = _stepFilterIdx > 0 && _stepSequenceIdxs != null
                ? _stepSequenceIdxs[_stepFilterIdx] + 1
                : (_pkg?.GetSteps()?.Max(s => s?.sequenceIndex ?? 0) ?? 0) + 1;
            _newStepSeqIdx = afterSeq;

            Repaint();
        }

        // ── Toolbar refresh (called periodically + after explicit mutations) ──

        private void RefreshToolbar()
        {
            if (_toolbar == null) return;

            // Package dropdown
            var pkgChoices = (_packageIds != null && _packageIds.Length > 0)
                ? new List<string>(_packageIds)
                : new List<string> { "(no packages)" };

            if (_toolbarPackageField.choices == null
                || _toolbarPackageField.choices.Count != pkgChoices.Count
                || !_toolbarPackageField.choices.SequenceEqual(pkgChoices))
            {
                _toolbarPackageField.choices = pkgChoices;
            }

            int pkgIdx     = Mathf.Clamp(_pkgIdx, 0, pkgChoices.Count - 1);
            string pkgVal  = pkgChoices[pkgIdx];
            if (_toolbarPackageField.value != pkgVal)
                _toolbarPackageField.SetValueWithoutNotify(pkgVal);

            bool hasPkg = _packageIds != null && _packageIds.Length > 0 && _pkg != null;

            // Step nav
            int stepCount = (_stepOptions?.Length ?? 1) - 1;
            if (_toolbarStepField.value != _stepFilterIdx)
                _toolbarStepField.SetValueWithoutNotify(_stepFilterIdx);
            string countText = $"/{stepCount}";
            if (_toolbarStepCountLabel.text != countText)
                _toolbarStepCountLabel.text = countText;

            bool canPrev = hasPkg && _stepFilterIdx > 1;
            bool canNext = hasPkg && _stepFilterIdx < stepCount;
            _toolbarFirstStepBtn.SetEnabled(canPrev);
            _toolbarPrevStepBtn .SetEnabled(canPrev);
            _toolbarNextStepBtn .SetEnabled(canNext);
            _toolbarLastStepBtn .SetEnabled(canNext);
            _toolbarStepField   .SetEnabled(hasPkg && stepCount > 0);
            _toolbarNewStepBtn  .SetEnabled(hasPkg);

            // Step title
            string title;
            if (!hasPkg)                           title = string.Empty;
            else if (_stepFilterIdx == 0)          title = "All Steps";
            else if (_stepIds != null && _stepFilterIdx < _stepIds.Length)
                title = FindStep(_stepIds[_stepFilterIdx])?.GetDisplayName() ?? string.Empty;
            else                                   title = string.Empty;

            if (_toolbarStepTitleLabel.text != title)
                _toolbarStepTitleLabel.text = title;

            // Dirty indicator — sum across all four dirty sets
            int dirtyCount = (_dirtyToolIds?.Count          ?? 0)
                           + (_dirtyStepIds?.Count          ?? 0)
                           + (_dirtyTaskOrderStepIds?.Count ?? 0)
                           + (_dirtyPartAssetRefIds?.Count  ?? 0);
            string dirtyText = dirtyCount > 0 ? $"● {dirtyCount} unsaved" : string.Empty;
            if (_toolbarDirtyLabel.text != dirtyText)
                _toolbarDirtyLabel.text = dirtyText;

            if (_toolbarInspectorBtn != null && _toolbarInspectorBtn.value != _inspectorVisible)
                _toolbarInspectorBtn.SetValueWithoutNotify(_inspectorVisible);

            // Navigator — rebuild on package/size changes, and re-sync the
            // selection if the active step changed via the IMGUI side or the
            // toolbar's step nav buttons.
            RebuildNavigatorIfStale();
            RefreshNavigatorSelection();
        }
    }
}
