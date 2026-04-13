using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Searchable step picker. Unity's built-in <see cref="AdvancedDropdown"/>
    /// provides type-ahead search over potentially hundreds of entries. The
    /// pose-propagation UI uses it so authors can pick a span endpoint without
    /// scrolling through 300+ flat menu items.
    ///
    /// Supports:
    /// - Optional predicate filter (e.g. "only steps using this part").
    /// - A "you are here" marker on the anchor step so the author always sees
    ///   where they currently stand in the package.
    /// - Sequence-index-prefixed labels so the author can search by number
    ///   (<c>27</c>) OR by name (<c>place frame</c>).
    /// </summary>
    internal sealed class StepPickerDropdown : AdvancedDropdown
    {
        private readonly StepDefinition[] _steps;
        private readonly Action<string>   _onPicked;
        private readonly Func<StepDefinition, bool> _filter;
        private readonly string _anchorStepId;
        private readonly Dictionary<int, string> _idByItem = new();

        private StepPickerDropdown(
            AdvancedDropdownState state,
            StepDefinition[] steps,
            Func<StepDefinition, bool> filter,
            string anchorStepId,
            Action<string> onPicked)
            : base(state)
        {
            _steps        = steps ?? Array.Empty<StepDefinition>();
            _filter       = filter;
            _anchorStepId = anchorStepId;
            _onPicked     = onPicked;
            minimumSize   = new Vector2(380f, 460f);
        }

        /// <summary>
        /// Legacy 2-arg overload retained for call sites that don't need
        /// filtering or an anchor marker.
        /// </summary>
        public static void Open(StepDefinition[] steps, Action<string> onPicked)
            => Open(steps, null, null, onPicked);

        public static void Open(
            StepDefinition[] steps,
            Func<StepDefinition, bool> filter,
            string anchorStepId,
            Action<string> onPicked)
        {
            if (steps == null || steps.Length == 0 || onPicked == null) return;
            var dd = new StepPickerDropdown(new AdvancedDropdownState(), steps, filter, anchorStepId, onPicked);
            Vector2 mouse = Event.current?.mousePosition ?? Vector2.zero;
            var anchorRect = new Rect(mouse.x, mouse.y, 1f, 1f);
            if (EditorWindow.focusedWindow != null)
                anchorRect.position += EditorWindow.focusedWindow.position.position;
            dd.Show(anchorRect);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Pick a step");

            var byAssembly = new Dictionary<string, List<StepDefinition>>(StringComparer.Ordinal);
            foreach (var s in _steps)
            {
                if (s == null) continue;
                if (_filter != null && !_filter(s)) continue;
                string key = string.IsNullOrEmpty(s.assemblyId) ? "(no assembly)" : s.assemblyId;
                if (!byAssembly.TryGetValue(key, out var list))
                    byAssembly[key] = list = new List<StepDefinition>();
                list.Add(s);
            }

            if (byAssembly.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("(no steps match the current filter — turn off 'Only steps using this part')"));
                return root;
            }

            foreach (var kvp in byAssembly)
            {
                kvp.Value.Sort((a, b) => a.sequenceIndex.CompareTo(b.sequenceIndex));
                var folder = new AdvancedDropdownItem(kvp.Key);
                foreach (var s in kvp.Value)
                {
                    bool isAnchor = !string.IsNullOrEmpty(_anchorStepId)
                                    && string.Equals(s.id, _anchorStepId, StringComparison.Ordinal);
                    string suffix = isAnchor ? "   ← you are here" : "";
                    string label  = $"[{s.sequenceIndex}]  {s.GetDisplayName()}{suffix}";
                    var item = new AdvancedDropdownItem(label);
                    _idByItem[item.id] = s.id;
                    folder.AddChild(item);
                }
                root.AddChild(folder);
            }
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item == null) return;
            if (_idByItem.TryGetValue(item.id, out string stepId))
                _onPicked?.Invoke(stepId);
        }
    }
}
