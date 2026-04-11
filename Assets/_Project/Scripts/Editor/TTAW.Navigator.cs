// TTAW.Navigator.cs — Left-side UITK navigator pane.
// ──────────────────────────────────────────────────────────────────────────────
// Phase 3 of the UX redesign. Adds a left-side panel with:
//   • Search field (filters by step id and display name, case-insensitive)
//   • Tree / Flat view-mode toggle (persisted via _navigatorViewMode)
//   • Family filter chips (Place / Use / Connect / Confirm) — all-off = show all
//   • TreeView grouping steps by their parent assembly + an "(Unassigned)" bucket
//     for orphan steps; the very first item is "All Steps" (jumps to filter idx 0)
//   • ListView for flat mode showing every step in sequenceIndex order
//   • Dirty dot per step driven by _dirtyStepIds
//
// The navigator never directly mutates state — it only calls ApplyStepFilter().
// The 100 ms scheduler in TTAW.Shell.cs polls for stale data and refreshes
// selection so external step changes (toolbar, SessionDriver, undo, etc.) all
// reflect into the navigator without each call site needing to know.
//
// Part of the ToolTargetAuthoringWindow partial class split.
// See ToolTargetAuthoringWindow.cs for fields, constants, and nested types.

using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.Editor
{
    public sealed partial class ToolTargetAuthoringWindow : EditorWindow
    {
        // ── Navigator widget references & cached state ────────────────────────

        private VisualElement     _navigatorRoot;
        private ToolbarSearchField _navigatorSearchField;
        private ToolbarToggle     _navigatorTreeModeBtn;
        private ToolbarToggle     _navigatorFlatModeBtn;
        private TreeView          _navigatorTreeView;
        private ListView          _navigatorListView;

        private string _navigatorSearchText = "";
        private readonly HashSet<string> _navigatorFamilyFilters = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<NavigatorItem> _navigatorFlatItems  = new();
        private readonly Dictionary<string, int> _navStepIdToTreeId = new(StringComparer.Ordinal);

        private string _navigatorBuiltForPkgId;
        private int    _navigatorBuiltStepCount = -1;
        private int    _navigatorLastSelectedStepIdx = -2;
        private bool   _suppressNavigatorSelection;

        private sealed class NavigatorItem
        {
            public string Id;          // step id; empty for assembly nodes / All Steps
            public string Display;     // label shown in the row
            public bool   IsAssembly;
            public bool   IsAllSteps;
            public string Family;      // for filter chips
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildNavigator(VisualElement parent)
        {
            _navigatorRoot = parent;
            _navigatorRoot.style.flexDirection = FlexDirection.Column;
            _navigatorRoot.style.flexGrow      = 1;
            _navigatorRoot.style.minWidth      = 180;
            _navigatorRoot.style.borderRightWidth = 1;
            _navigatorRoot.style.borderRightColor = new Color(0f, 0f, 0f, 0.35f);
            // Clip everything that overflows the navigator pane width — without
            // this, child widgets with flex constraints can visually bleed into
            // the right (IMGUI) pane on narrow widths.
            _navigatorRoot.style.overflow = Overflow.Hidden;

            // ── Row 1: search field (full width) ──────────────────────────────
            var searchRow = new Toolbar();
            searchRow.style.flexShrink     = 0;
            searchRow.style.flexDirection  = FlexDirection.Row;
            searchRow.style.overflow       = Overflow.Hidden;

            _navigatorSearchField = new ToolbarSearchField();
            _navigatorSearchField.style.flexGrow    = 1;
            _navigatorSearchField.style.flexShrink  = 1;
            _navigatorSearchField.style.minWidth    = 0;
            _navigatorSearchField.style.marginLeft  = 2;
            _navigatorSearchField.style.marginRight = 2;
            _navigatorSearchField.value = _navigatorSearchText ?? string.Empty;
            _navigatorSearchField.RegisterValueChangedCallback(evt =>
            {
                _navigatorSearchText = evt.newValue ?? string.Empty;
                RebuildNavigatorData();
            });
            searchRow.Add(_navigatorSearchField);
            _navigatorRoot.Add(searchRow);

            // ── Row 2: Tree / Flat view-mode toggle (full width, two halves) ──
            var modeRow = new Toolbar();
            modeRow.style.flexShrink    = 0;
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.overflow      = Overflow.Hidden;

            _navigatorTreeModeBtn = new ToolbarToggle { text = "Tree", value = _navigatorViewMode == 0 };
            _navigatorTreeModeBtn.tooltip = "Group steps by assembly";
            _navigatorTreeModeBtn.style.flexGrow   = 1;
            _navigatorTreeModeBtn.style.flexShrink = 1;
            _navigatorTreeModeBtn.style.minWidth   = 0;
            _navigatorTreeModeBtn.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue) { _navigatorTreeModeBtn.SetValueWithoutNotify(true); return; }
                _navigatorViewMode = 0;
                _navigatorFlatModeBtn.SetValueWithoutNotify(false);
                ApplyNavigatorViewMode();
            });
            modeRow.Add(_navigatorTreeModeBtn);

            _navigatorFlatModeBtn = new ToolbarToggle { text = "Flat", value = _navigatorViewMode == 1 };
            _navigatorFlatModeBtn.tooltip = "Show every step in a single flat list";
            _navigatorFlatModeBtn.style.flexGrow   = 1;
            _navigatorFlatModeBtn.style.flexShrink = 1;
            _navigatorFlatModeBtn.style.minWidth   = 0;
            _navigatorFlatModeBtn.RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue) { _navigatorFlatModeBtn.SetValueWithoutNotify(true); return; }
                _navigatorViewMode = 1;
                _navigatorTreeModeBtn.SetValueWithoutNotify(false);
                ApplyNavigatorViewMode();
            });
            modeRow.Add(_navigatorFlatModeBtn);

            _navigatorRoot.Add(modeRow);

            // ── Family filter chips row ───────────────────────────────────────
            var chipsRow = new VisualElement();
            chipsRow.style.flexDirection  = FlexDirection.Row;
            chipsRow.style.flexShrink     = 0;
            chipsRow.style.paddingLeft    = 4;
            chipsRow.style.paddingRight   = 4;
            chipsRow.style.paddingTop     = 2;
            chipsRow.style.paddingBottom  = 2;
            chipsRow.style.backgroundColor = new Color(0f, 0f, 0f, 0.12f);
            chipsRow.style.overflow       = Overflow.Hidden;

            foreach (var family in new[] { "Place", "Use", "Connect", "Confirm" })
            {
                string captured = family;
                var chip = new ToolbarToggle { text = family };
                chip.style.flexGrow    = 1;
                chip.style.flexShrink  = 1;
                chip.style.minWidth    = 0;
                chip.style.marginLeft  = 1;
                chip.style.marginRight = 1;
                chip.tooltip = $"Show only {family} steps (toggle off = no filter)";
                chip.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue) _navigatorFamilyFilters.Add(captured);
                    else              _navigatorFamilyFilters.Remove(captured);
                    RebuildNavigatorData();
                });
                chipsRow.Add(chip);
            }
            _navigatorRoot.Add(chipsRow);

            // ── Tree view ─────────────────────────────────────────────────────
            _navigatorTreeView = new TreeView();
            _navigatorTreeView.style.flexGrow = 1;
            _navigatorTreeView.fixedItemHeight = 20;
            _navigatorTreeView.makeItem        = MakeNavigatorRow;
            _navigatorTreeView.bindItem        = BindNavigatorTreeRow;
            _navigatorTreeView.selectionType   = SelectionType.Single;
            _navigatorTreeView.selectionChanged += OnNavigatorTreeSelectionChanged;
            _navigatorRoot.Add(_navigatorTreeView);

            // ── Flat list view ────────────────────────────────────────────────
            _navigatorListView = new ListView();
            _navigatorListView.style.flexGrow = 1;
            _navigatorListView.fixedItemHeight = 20;
            _navigatorListView.makeItem        = MakeNavigatorRow;
            _navigatorListView.bindItem        = BindNavigatorListRow;
            _navigatorListView.selectionType   = SelectionType.Single;
            _navigatorListView.itemsSource     = _navigatorFlatItems;
            _navigatorListView.selectionChanged += OnNavigatorListSelectionChanged;
            _navigatorRoot.Add(_navigatorListView);

            // ── Validation dashboard (collapsible strip below the tree) ───────
            BuildValidationDashboard(_navigatorRoot);

            ApplyNavigatorViewMode();
            RebuildNavigatorData();
        }

        private static VisualElement MakeNavigatorRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.height        = 20;

            var dot = new VisualElement { name = "dirty-dot" };
            dot.style.width                  = 6;
            dot.style.height                 = 6;
            dot.style.marginLeft             = 2;
            dot.style.marginRight            = 4;
            dot.style.borderTopLeftRadius    = 3;
            dot.style.borderTopRightRadius   = 3;
            dot.style.borderBottomLeftRadius = 3;
            dot.style.borderBottomRightRadius = 3;
            row.Add(dot);

            var label = new Label { name = "label" };
            label.style.flexGrow         = 1;
            label.style.unityTextAlign   = TextAnchor.MiddleLeft;
            label.style.overflow         = Overflow.Hidden;
            label.style.textOverflow     = TextOverflow.Ellipsis;
            label.style.whiteSpace       = WhiteSpace.NoWrap;
            row.Add(label);

            return row;
        }

        private void BindNavigatorTreeRow(VisualElement element, int index)
        {
            var data = _navigatorTreeView.GetItemDataForIndex<NavigatorItem>(index);
            BindNavigatorRow(element, data);
        }

        private void BindNavigatorListRow(VisualElement element, int index)
        {
            if (index < 0 || index >= _navigatorFlatItems.Count) return;
            BindNavigatorRow(element, _navigatorFlatItems[index]);
        }

        private void BindNavigatorRow(VisualElement element, NavigatorItem item)
        {
            if (item == null) return;
            var dot   = element.Q<VisualElement>("dirty-dot");
            var label = element.Q<Label>("label");
            label.text = item.Display ?? string.Empty;

            if (item.IsAssembly)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color                   = new Color(0.85f, 0.85f, 0.85f);
                dot.style.backgroundColor           = new StyleColor(StyleKeyword.None);
            }
            else if (item.IsAllSteps)
            {
                label.style.unityFontStyleAndWeight = FontStyle.Italic;
                label.style.color                   = new Color(0.7f, 0.75f, 0.9f);
                dot.style.backgroundColor           = new StyleColor(StyleKeyword.None);
            }
            else
            {
                label.style.unityFontStyleAndWeight = FontStyle.Normal;
                label.style.color                   = new Color(0.78f, 0.78f, 0.78f);
                bool isDirty = !string.IsNullOrEmpty(item.Id)
                               && _dirtyStepIds != null
                               && _dirtyStepIds.Contains(item.Id);
                dot.style.backgroundColor = isDirty
                    ? new StyleColor(new Color(0.95f, 0.65f, 0.15f))
                    : new StyleColor(StyleKeyword.None);
            }
        }

        // ── View-mode swap ────────────────────────────────────────────────────

        private void ApplyNavigatorViewMode()
        {
            if (_navigatorTreeView == null || _navigatorListView == null) return;
            bool tree = _navigatorViewMode == 0;
            _navigatorTreeView.style.display = tree ? DisplayStyle.Flex : DisplayStyle.None;
            _navigatorListView.style.display = tree ? DisplayStyle.None : DisplayStyle.Flex;
            _navigatorLastSelectedStepIdx = -2; // force selection refresh
            RefreshNavigatorSelection();
        }

        // ── Filtering ─────────────────────────────────────────────────────────

        private bool MatchesNavigatorFilter(StepDefinition step)
        {
            if (step == null) return false;

            if (_navigatorFamilyFilters.Count > 0)
            {
                if (string.IsNullOrEmpty(step.family) || !_navigatorFamilyFilters.Contains(step.family))
                    return false;
            }

            if (!string.IsNullOrEmpty(_navigatorSearchText))
            {
                string needle = _navigatorSearchText.Trim();
                if (needle.Length > 0)
                {
                    string id      = step.id ?? string.Empty;
                    string display = step.GetDisplayName() ?? string.Empty;
                    if (id.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0
                        && display.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
            }
            return true;
        }

        // ── Rebuild data ──────────────────────────────────────────────────────

        private void RebuildNavigatorData()
        {
            if (_navigatorTreeView == null) return;

            _navigatorFlatItems.Clear();
            _navStepIdToTreeId.Clear();
            _navigatorLastSelectedStepIdx = -2;

            if (_pkg == null)
            {
                _navigatorTreeView.SetRootItems(new List<TreeViewItemData<NavigatorItem>>());
                _navigatorTreeView.Rebuild();
                _navigatorListView.itemsSource = _navigatorFlatItems;
                _navigatorListView.Rebuild();
                _navigatorBuiltForPkgId  = null;
                _navigatorBuiltStepCount = 0;
                return;
            }

            int idCounter = 0;
            var treeItems = new List<TreeViewItemData<NavigatorItem>>();

            // "All Steps" pseudo-item at the top of both modes
            var allItem = new NavigatorItem { Id = string.Empty, Display = "All Steps", IsAllSteps = true };
            treeItems.Add(new TreeViewItemData<NavigatorItem>(idCounter++, allItem));
            _navigatorFlatItems.Add(allItem);

            bool filtering = !string.IsNullOrEmpty(_navigatorSearchText) || _navigatorFamilyFilters.Count > 0;

            var assignedToAssembly = new HashSet<string>(StringComparer.Ordinal);
            if (_pkg.assemblies != null)
            {
                foreach (var asm in _pkg.assemblies)
                {
                    if (asm == null) continue;

                    var children = new List<TreeViewItemData<NavigatorItem>>();
                    if (asm.stepIds != null)
                    {
                        foreach (var stepId in asm.stepIds)
                        {
                            if (string.IsNullOrEmpty(stepId)) continue;
                            assignedToAssembly.Add(stepId);
                            var step = FindStep(stepId);
                            if (step == null) continue;
                            if (!MatchesNavigatorFilter(step)) continue;

                            var navItem = MakeNavigatorItemForStep(step);
                            int treeId  = idCounter++;
                            children.Add(new TreeViewItemData<NavigatorItem>(treeId, navItem));
                            _navigatorFlatItems.Add(navItem);
                            _navStepIdToTreeId[step.id] = treeId;
                        }
                    }

                    // Hide empty assemblies during search/filter; show all assemblies otherwise.
                    if (children.Count == 0 && filtering) continue;

                    string asmDisplay = string.IsNullOrEmpty(asm.name) ? asm.id : asm.name;
                    var asmItem = new NavigatorItem
                    {
                        Id         = asm.id,
                        Display    = $"{asmDisplay}  ({children.Count})",
                        IsAssembly = true,
                    };
                    treeItems.Add(new TreeViewItemData<NavigatorItem>(idCounter++, asmItem, children));
                }
            }

            // Orphan steps (not assigned to any assembly) — show in a synthetic group
            var orphans = new List<TreeViewItemData<NavigatorItem>>();
            var allSteps = _pkg.GetSteps();
            if (allSteps != null)
            {
                foreach (var step in allSteps)
                {
                    if (step == null || string.IsNullOrEmpty(step.id)) continue;
                    if (assignedToAssembly.Contains(step.id)) continue;
                    if (!MatchesNavigatorFilter(step)) continue;

                    var navItem = MakeNavigatorItemForStep(step);
                    int treeId  = idCounter++;
                    orphans.Add(new TreeViewItemData<NavigatorItem>(treeId, navItem));
                    _navigatorFlatItems.Add(navItem);
                    _navStepIdToTreeId[step.id] = treeId;
                }
            }
            if (orphans.Count > 0)
            {
                var orphanGroup = new NavigatorItem
                {
                    Id         = string.Empty,
                    Display    = $"(Unassigned)  ({orphans.Count})",
                    IsAssembly = true,
                };
                treeItems.Add(new TreeViewItemData<NavigatorItem>(idCounter++, orphanGroup, orphans));
            }

            _navigatorTreeView.SetRootItems(treeItems);
            _navigatorTreeView.Rebuild();
            _navigatorListView.itemsSource = _navigatorFlatItems;
            _navigatorListView.Rebuild();

            _navigatorBuiltForPkgId  = _pkgId;
            _navigatorBuiltStepCount = _pkg.GetSteps()?.Length ?? 0;

            RefreshNavigatorSelection();
        }

        private NavigatorItem MakeNavigatorItemForStep(StepDefinition step)
        {
            string display = step.GetDisplayName();
            if (string.IsNullOrEmpty(display)) display = step.id ?? "(unnamed)";
            string suffix;
            if (string.IsNullOrEmpty(step.profile))
                suffix = string.IsNullOrEmpty(step.family) ? string.Empty : $"  ·  {step.family}";
            else
                suffix = $"  ·  {step.family}/{step.profile}";

            return new NavigatorItem
            {
                Id      = step.id,
                Display = $"{step.sequenceIndex}.  {display}{suffix}",
                Family  = step.family,
            };
        }

        // ── Stale-data check (called from RefreshToolbar's 100ms tick) ────────

        private void RebuildNavigatorIfStale()
        {
            if (_navigatorTreeView == null) return;
            int currentStepCount = _pkg?.GetSteps()?.Length ?? 0;
            bool pkgChanged  = !string.Equals(_navigatorBuiltForPkgId, _pkgId, StringComparison.Ordinal);
            bool sizeChanged = currentStepCount != _navigatorBuiltStepCount;
            if (pkgChanged || sizeChanged)
                RebuildNavigatorData();
        }

        // ── Selection sync ────────────────────────────────────────────────────

        private void RefreshNavigatorSelection()
        {
            if (_navigatorTreeView == null) return;
            if (_stepFilterIdx == _navigatorLastSelectedStepIdx) return;
            _navigatorLastSelectedStepIdx = _stepFilterIdx;

            // No active step (or "All Steps" pseudo-mode) — clear selection
            if (_stepFilterIdx <= 0 || _stepIds == null || _stepFilterIdx >= _stepIds.Length)
            {
                _suppressNavigatorSelection = true;
                _navigatorTreeView.ClearSelection();
                _navigatorListView.ClearSelection();
                _suppressNavigatorSelection = false;
                return;
            }

            string targetId = _stepIds[_stepFilterIdx];
            if (string.IsNullOrEmpty(targetId)) return;

            // Flat list — find by linear scan
            int flatIdx = -1;
            for (int i = 0; i < _navigatorFlatItems.Count; i++)
            {
                var it = _navigatorFlatItems[i];
                if (it != null && !it.IsAssembly && !it.IsAllSteps && it.Id == targetId)
                {
                    flatIdx = i;
                    break;
                }
            }
            _suppressNavigatorSelection = true;
            try
            {
                if (flatIdx >= 0)
                {
                    _navigatorListView.SetSelectionWithoutNotify(new[] { flatIdx });
                    _navigatorListView.ScrollToItem(flatIdx);
                }
                else
                {
                    _navigatorListView.ClearSelection();
                }

                // Tree view — look up cached id. UITK TreeView only exposes
                // SetSelectionById (notify-only); the _suppressNavigatorSelection
                // guard above prevents the resulting selectionChanged callback
                // from looping back into ApplyStepFilter.
                if (_navStepIdToTreeId.TryGetValue(targetId, out int treeId))
                {
                    _navigatorTreeView.SetSelectionById(treeId);
                    _navigatorTreeView.ScrollToItemById(treeId);
                }
                else
                {
                    _navigatorTreeView.ClearSelection();
                }
            }
            finally
            {
                _suppressNavigatorSelection = false;
            }
        }

        // ── Selection event handlers ──────────────────────────────────────────

        private void OnNavigatorTreeSelectionChanged(IEnumerable<object> selected)
        {
            if (_suppressNavigatorSelection) return;
            foreach (var obj in selected)
            {
                if (obj is NavigatorItem item)
                {
                    JumpToNavigatorItem(item);
                    break;
                }
            }
        }

        private void OnNavigatorListSelectionChanged(IEnumerable<object> selected)
        {
            if (_suppressNavigatorSelection) return;
            foreach (var obj in selected)
            {
                if (obj is NavigatorItem item)
                {
                    JumpToNavigatorItem(item);
                    break;
                }
            }
        }

        private void JumpToNavigatorItem(NavigatorItem item)
        {
            if (item == null) return;

            // Assembly headers are not navigable — keep selection on whatever was active
            if (item.IsAssembly)
            {
                _navigatorLastSelectedStepIdx = -2; // force a re-sync to revert visual selection
                return;
            }

            if (item.IsAllSteps)
            {
                if (_stepFilterIdx != 0)
                {
                    ApplyStepFilter(0);
                    Repaint();
                }
                return;
            }

            if (string.IsNullOrEmpty(item.Id) || _stepIds == null) return;
            int idx = Array.IndexOf(_stepIds, item.Id);
            if (idx > 0 && idx != _stepFilterIdx)
            {
                ApplyStepFilter(idx);
                Repaint();
            }
        }
    }
}
