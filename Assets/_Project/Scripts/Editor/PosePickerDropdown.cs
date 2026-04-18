using System;
using System.Collections.Generic;
using OSE.Content;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Searchable end-pose picker for a part's pose catalogue. Uses Unity's
    /// <see cref="AdvancedDropdown"/> for type-ahead filtering — same pattern
    /// as <see cref="StepPickerDropdown"/> and <see cref="PartPickerDropdown"/>.
    ///
    /// <para>Callers pass a <see cref="PartPreviewPlacement"/> and receive a
    /// <c>toPose</c> token string: <c>"auto"</c>, <c>"start"</c>, <c>"assembled"</c>,
    /// or <c>"step:&lt;stepId&gt;"</c>. Grammar documented on
    /// <see cref="OSE.Content.ToolPartInteraction.toPose"/>.</para>
    ///
    /// <para>Consumers (as of Phase F):</para>
    /// <list type="bullet">
    ///   <item>Tool task — Interaction Panel 🔩 Endpoints band (<c>TTAW.InteractionPanel.cs</c>).</item>
    ///   <item>Part task — <c>DrawPartDetailPanel</c> End-pose row (<c>TTAW.Layout.cs</c>).</item>
    ///   <item>Part-group task — inherits the Part-task dropdown.</item>
    /// </list>
    /// </summary>
    internal sealed class PosePickerDropdown : AdvancedDropdown
    {
        /// <summary>Sentinel token returned when the author picks the inline
        /// "+ New custom pose for this step" entry. The caller is expected to
        /// create a stepPose at the current step and apply its new token.</summary>
        public const string CreateNewToken = "__create_new__";

        private readonly PartPreviewPlacement    _placement;
        private readonly string                  _currentStepId;
        private readonly Action<string>          _onPicked;
        private readonly bool                    _offerCreateNew;
        private readonly Dictionary<int, string> _tokenByItem = new();

        private PosePickerDropdown(
            AdvancedDropdownState state,
            PartPreviewPlacement placement,
            string currentStepId,
            bool offerCreateNew,
            Action<string> onPicked)
            : base(state)
        {
            _placement      = placement;
            _currentStepId  = currentStepId;
            _offerCreateNew = offerCreateNew && !string.IsNullOrEmpty(currentStepId);
            _onPicked       = onPicked;
            minimumSize     = new Vector2(380f, 420f);
        }

        /// <summary>
        /// Opens the picker anchored at the current mouse position.
        /// <paramref name="onPicked"/> receives the authored <c>toPose</c> token
        /// (<c>"auto"</c>, <c>"start"</c>, <c>"assembled"</c>, <c>"step:&lt;id&gt;"</c>).
        /// <paramref name="currentStepId"/> is used only to flag the "auto" entry's
        /// implicit resolution target in the UI — it is not written into any token.
        /// </summary>
        public static void Open(
            PartPreviewPlacement placement,
            string currentStepId,
            Action<string> onPicked,
            bool offerCreateNew = false)
        {
            if (onPicked == null) return;
            var dd = new PosePickerDropdown(new AdvancedDropdownState(), placement, currentStepId, offerCreateNew, onPicked);
            Vector2 mouse = Event.current?.mousePosition ?? Vector2.zero;
            var anchorRect = new Rect(mouse.x, mouse.y, 1f, 1f);
            if (EditorWindow.focusedWindow != null)
                anchorRect.position += EditorWindow.focusedWindow.position.position;
            dd.Show(anchorRect);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Pick an end pose");

            // (auto) — today's implicit resolution. Shown first and distinct.
            string autoLabel = !string.IsNullOrEmpty(_currentStepId)
                ? $"(auto — stepPoses[{_currentStepId}] → assembled)"
                : "(auto — assembled)";
            var autoItem = new AdvancedDropdownItem(autoLabel);
            _tokenByItem[autoItem.id] = ToPoseTokens.Auto;
            root.AddChild(autoItem);

            root.AddSeparator();

            // Fixed poses — start, assembled.
            var startItem = new AdvancedDropdownItem("Start (staging pose)");
            _tokenByItem[startItem.id] = ToPoseTokens.Start;
            root.AddChild(startItem);

            var assembledItem = new AdvancedDropdownItem("Assembled (final pose)");
            _tokenByItem[assembledItem.id] = ToPoseTokens.Assembled;
            root.AddChild(assembledItem);

            // Custom step poses, sorted by stepId for stable ordering.
            var stepPoses = _placement?.stepPoses;
            if (stepPoses != null && stepPoses.Length > 0)
            {
                root.AddSeparator();
                var sorted = new List<StepPoseEntry>(stepPoses);
                sorted.Sort((a, b) =>
                {
                    string ax = a?.stepId ?? "";
                    string bx = b?.stepId ?? "";
                    return string.Compare(ax, bx, StringComparison.Ordinal);
                });
                foreach (var sp in sorted)
                {
                    if (sp == null || string.IsNullOrEmpty(sp.stepId)) continue;
                    string label = !string.IsNullOrEmpty(sp.label)
                        ? $"{sp.label}  —  step:{sp.stepId}"
                        : $"step:{sp.stepId}";
                    var item = new AdvancedDropdownItem(label);
                    _tokenByItem[item.id] = ToPoseTokens.StepPrefix + sp.stepId;
                    root.AddChild(item);
                }
            }

            // Inline "+ New custom pose" entry — offered to callers that want to
            // let the author create a stepPose without leaving the dropdown.
            // The caller handles the actual creation; this just returns a sentinel.
            if (_offerCreateNew)
            {
                root.AddSeparator();
                var createItem = new AdvancedDropdownItem($"+ New custom pose for step '{_currentStepId}' from current transform");
                _tokenByItem[createItem.id] = CreateNewToken;
                root.AddChild(createItem);
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item == null) return;
            if (_tokenByItem.TryGetValue(item.id, out string token))
                _onPicked?.Invoke(token);
        }

        /// <summary>
        /// Resolves a human-readable label for a <c>toPose</c> token against the given
        /// placement. Used by the Interaction Panel's Motion readout and the canvas
        /// diagnostics chip. Returns null for malformed tokens so callers can flag them.
        /// </summary>
        public static string ResolveLabel(string token, PartPreviewPlacement placement, string currentStepId)
        {
            if (ToPoseTokens.IsAuto(token))
            {
                if (placement?.stepPoses != null && !string.IsNullOrEmpty(currentStepId))
                {
                    foreach (var sp in placement.stepPoses)
                        if (sp != null && sp.stepId == currentStepId)
                            return !string.IsNullOrEmpty(sp.label) ? sp.label : $"step:{sp.stepId}";
                }
                return "Assembled (auto)";
            }
            if (token == ToPoseTokens.Start)     return "Start";
            if (token == ToPoseTokens.Assembled) return "Assembled";
            if (token.StartsWith(ToPoseTokens.StepPrefix, StringComparison.Ordinal))
            {
                string stepId = token.Substring(ToPoseTokens.StepPrefix.Length);
                if (placement?.stepPoses != null)
                {
                    foreach (var sp in placement.stepPoses)
                        if (sp != null && sp.stepId == stepId)
                            return !string.IsNullOrEmpty(sp.label) ? sp.label : $"step:{stepId}";
                }
                return null; // stale reference — caller should flag
            }
            return null;
        }

        /// <summary>True when the given token is a <c>step:&lt;id&gt;</c> reference whose
        /// <see cref="StepPoseEntry"/> is missing from the placement.</summary>
        public static bool IsStaleReference(string token, PartPreviewPlacement placement)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (!token.StartsWith(ToPoseTokens.StepPrefix, StringComparison.Ordinal)) return false;
            string stepId = token.Substring(ToPoseTokens.StepPrefix.Length);
            if (placement?.stepPoses == null) return true;
            foreach (var sp in placement.stepPoses)
                if (sp != null && sp.stepId == stepId) return false;
            return true;
        }
    }
}
