using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Searchable part picker. Uses Unity's <see cref="AdvancedDropdown"/> so authors
    /// can type-ahead filter over packages with hundreds of parts instead of scrolling
    /// a flat <c>EditorGUILayout.Popup</c>. Modeled on <see cref="StepPickerDropdown"/>
    /// — same open pattern, same state shape.
    ///
    /// Supports:
    /// - Optional grouping by category / subassembly (falls back to a flat list when unset).
    /// - A leading "(clear)" item that sets the associated part to null.
    /// - Labels include both the id and the display name so search matches either.
    /// </summary>
    internal sealed class PartPickerDropdown : AdvancedDropdown
    {
        private readonly PartDefinition[]       _parts;
        private readonly Action<string>         _onPicked;
        private readonly Dictionary<int, string> _idByItem = new();
        private readonly string _title;

        private PartPickerDropdown(
            AdvancedDropdownState state,
            PartDefinition[] parts,
            string title,
            Action<string> onPicked)
            : base(state)
        {
            _parts     = parts ?? Array.Empty<PartDefinition>();
            _title     = string.IsNullOrEmpty(title) ? "Pick a part" : title;
            _onPicked  = onPicked;
            minimumSize = new Vector2(380f, 460f);
        }

        /// <summary>
        /// Opens the picker anchored at the mouse position of the current IMGUI event.
        /// <paramref name="onPicked"/> receives the selected partId, or an empty string
        /// when the author chose the "(clear)" entry.
        /// </summary>
        public static void Open(PartDefinition[] parts, string title, Action<string> onPicked)
        {
            if (parts == null || onPicked == null) return;
            var dd = new PartPickerDropdown(new AdvancedDropdownState(), parts, title, onPicked);
            Vector2 mouse = Event.current?.mousePosition ?? Vector2.zero;
            var anchorRect = new Rect(mouse.x, mouse.y, 1f, 1f);
            if (EditorWindow.focusedWindow != null)
                anchorRect.position += EditorWindow.focusedWindow.position.position;
            dd.Show(anchorRect);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem(_title);

            var clearItem = new AdvancedDropdownItem("(clear — no part associated)");
            _idByItem[clearItem.id] = "";
            root.AddChild(clearItem);
            root.AddSeparator();

            // Group by category when available, else fall back to a flat list.
            var byCategory = new SortedDictionary<string, List<PartDefinition>>(StringComparer.Ordinal);
            foreach (var p in _parts)
            {
                if (p == null || string.IsNullOrEmpty(p.id)) continue;
                string key = string.IsNullOrEmpty(p.category) ? "uncategorized" : p.category;
                if (!byCategory.TryGetValue(key, out var list))
                    byCategory[key] = list = new List<PartDefinition>();
                list.Add(p);
            }

            if (byCategory.Count == 0)
            {
                root.AddChild(new AdvancedDropdownItem("(no parts in this package)"));
                return root;
            }

            // Single-category packages render flat for faster picking.
            if (byCategory.Count == 1)
            {
                foreach (var list in byCategory.Values)
                {
                    list.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
                    foreach (var p in list) AddPartItem(root, p);
                }
                return root;
            }

            foreach (var kvp in byCategory)
            {
                kvp.Value.Sort((a, b) => string.Compare(a.id, b.id, StringComparison.Ordinal));
                var folder = new AdvancedDropdownItem(kvp.Key);
                foreach (var p in kvp.Value) AddPartItem(folder, p);
                root.AddChild(folder);
            }
            return root;
        }

        private void AddPartItem(AdvancedDropdownItem parent, PartDefinition p)
        {
            string label = string.IsNullOrEmpty(p.name) || p.name == p.id
                ? p.id
                : $"{p.id}  —  {p.name}";
            var item = new AdvancedDropdownItem(label);
            _idByItem[item.id] = p.id;
            parent.AddChild(item);
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item == null) return;
            if (_idByItem.TryGetValue(item.id, out string partId))
                _onPicked?.Invoke(partId);
        }
    }
}
