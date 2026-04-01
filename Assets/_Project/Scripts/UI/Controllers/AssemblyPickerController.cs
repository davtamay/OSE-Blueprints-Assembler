using System;
using OSE.Content;
using OSE.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace OSE.UI.Controllers
{
    /// <summary>
    /// Fullscreen overlay that displays all assemblies as selectable section cards.
    /// Users can pick a section to jump to, or dismiss to resume the current step.
    /// Follows the same programmatic UI Toolkit pattern as <see cref="IntroOverlayController"/>
    /// and <see cref="AssemblyTransitionController"/>.
    /// </summary>
    internal sealed class AssemblyPickerController
    {
        private readonly Func<VisualElement> _getRootElement;

        private VisualElement _overlay;
        private bool _isVisible;

        public bool IsVisible => _isVisible;

        public AssemblyPickerController(Func<VisualElement> getRootElement)
        {
            _getRootElement = getRootElement;
        }

        /// <summary>
        /// Shows the assembly picker overlay.
        /// </summary>
        /// <param name="package">Loaded package (assemblies, steps, machine).</param>
        /// <param name="completedStepCount">Global completed step count from persistence.</param>
        public void Show(MachinePackageDefinition package, int completedStepCount)
        {
            VisualElement root = _getRootElement?.Invoke();
            if (root == null || package == null)
                return;

            Dismiss();
            _isVisible = true;

            string[] assemblyOrder = package.machine?.entryAssemblyIds;
            if (assemblyOrder == null || assemblyOrder.Length == 0)
            {
                var assemblies = package.GetAssemblies();
                assemblyOrder = new string[assemblies.Length];
                for (int i = 0; i < assemblies.Length; i++)
                    assemblyOrder[i] = assemblies[i].id;
            }

            StepDefinition[] orderedSteps = package.GetOrderedSteps();

            // Precompute per-assembly step ranges
            var assemblyStepInfo = ComputeAssemblyStepInfo(assemblyOrder, orderedSteps, package, completedStepCount);

            // Build overlay
            _overlay = new VisualElement();
            _overlay.name = "ose-assembly-picker-overlay";
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0f;
            _overlay.style.right = 0f;
            _overlay.style.top = 0f;
            _overlay.style.bottom = 0f;
            _overlay.style.backgroundColor = new Color(0.06f, 0.07f, 0.09f, 0.92f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.pickingMode = PickingMode.Position;

            // Card container
            var card = new VisualElement();
            card.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 0.98f);
            card.style.borderTopLeftRadius = 16f;
            card.style.borderTopRightRadius = 16f;
            card.style.borderBottomLeftRadius = 16f;
            card.style.borderBottomRightRadius = 16f;
            card.style.paddingTop = 20f;
            card.style.paddingBottom = 20f;
            card.style.paddingLeft = 24f;
            card.style.paddingRight = 24f;
            card.style.maxWidth = 480f;
            card.style.minWidth = 340f;
            card.style.maxHeight = new Length(85f, LengthUnit.Percent);
            card.style.alignItems = Align.Center;
            card.style.borderTopWidth = 1f;
            card.style.borderBottomWidth = 1f;
            card.style.borderLeftWidth = 1f;
            card.style.borderRightWidth = 1f;
            card.style.borderTopColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderBottomColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderLeftColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);
            card.style.borderRightColor = new Color(0.25f, 0.27f, 0.32f, 0.6f);

            // Title
            var titleLabel = new Label("Sections");
            titleLabel.style.fontSize = 18f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new Color(0.95f, 0.96f, 0.98f);
            titleLabel.style.marginBottom = 4f;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(titleLabel);

            // Subtitle
            var subtitleLabel = new Label("Choose a section to jump to");
            subtitleLabel.style.fontSize = 11f;
            subtitleLabel.style.color = new Color(0.6f, 0.63f, 0.68f);
            subtitleLabel.style.marginBottom = 12f;
            subtitleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(subtitleLabel);

            // Scrollable assembly list
            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.style.flexGrow = 1f;
            scrollView.style.flexShrink = 1f;
            scrollView.style.width = new Length(100f, LengthUnit.Percent);

            for (int i = 0; i < assemblyStepInfo.Length; i++)
            {
                var info = assemblyStepInfo[i];
                var row = CreateAssemblyRow(info, i + 1);
                scrollView.Add(row);
            }

            card.Add(scrollView);

            // Resume button
            var resumeBtn = new Button();
            resumeBtn.text = "Resume";
            resumeBtn.style.height = 38f;
            resumeBtn.style.width = 180f;
            resumeBtn.style.fontSize = 14f;
            resumeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            resumeBtn.style.backgroundColor = new Color(0.2f, 0.22f, 0.28f, 1f);
            resumeBtn.style.color = new Color(0.8f, 0.82f, 0.88f);
            resumeBtn.style.borderTopLeftRadius = 12f;
            resumeBtn.style.borderTopRightRadius = 12f;
            resumeBtn.style.borderBottomLeftRadius = 12f;
            resumeBtn.style.borderBottomRightRadius = 12f;
            resumeBtn.style.borderTopWidth = 1f;
            resumeBtn.style.borderBottomWidth = 1f;
            resumeBtn.style.borderLeftWidth = 1f;
            resumeBtn.style.borderRightWidth = 1f;
            resumeBtn.style.borderTopColor = new Color(0.3f, 0.32f, 0.38f, 0.5f);
            resumeBtn.style.borderBottomColor = new Color(0.3f, 0.32f, 0.38f, 0.5f);
            resumeBtn.style.borderLeftColor = new Color(0.3f, 0.32f, 0.38f, 0.5f);
            resumeBtn.style.borderRightColor = new Color(0.3f, 0.32f, 0.38f, 0.5f);
            resumeBtn.style.marginTop = 12f;
            resumeBtn.clicked += () =>
            {
                Dismiss();
                RuntimeEventBus.Publish(new AssemblyPickerDismissed(null, -1));
            };
            card.Add(resumeBtn);

            _overlay.Add(card);
            root.Add(_overlay);
        }

        public void Dismiss()
        {
            if (!_isVisible) return;
            _isVisible = false;
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
            }
        }

        public void Teardown()
        {
            _isVisible = false;
            if (_overlay != null)
            {
                _overlay.RemoveFromHierarchy();
                _overlay = null;
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Row construction
        // ════════════════════════════════════════════════════════════════════

        private VisualElement CreateAssemblyRow(AssemblyStepInfo info, int index)
        {
            bool isCompleted = info.CompletedSteps >= info.TotalSteps && info.TotalSteps > 0;
            bool isInProgress = info.CompletedSteps > 0 && !isCompleted;

            var row = new Button();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.backgroundColor = isInProgress
                ? new Color(0.14f, 0.18f, 0.26f, 0.9f)
                : new Color(0.10f, 0.11f, 0.14f, 0.9f);
            row.style.borderTopLeftRadius = 8f;
            row.style.borderTopRightRadius = 8f;
            row.style.borderBottomLeftRadius = 8f;
            row.style.borderBottomRightRadius = 8f;
            row.style.paddingLeft = 12f;
            row.style.paddingRight = 12f;
            row.style.paddingTop = 10f;
            row.style.paddingBottom = 10f;
            row.style.marginBottom = 6f;
            row.style.borderTopWidth = 1f;
            row.style.borderBottomWidth = 1f;
            row.style.borderLeftWidth = 1f;
            row.style.borderRightWidth = 1f;

            Color borderColor = isCompleted
                ? new Color(0.3f, 0.7f, 0.45f, 0.5f)
                : isInProgress
                    ? new Color(0.35f, 0.55f, 0.85f, 0.5f)
                    : new Color(0.22f, 0.24f, 0.28f, 0.5f);
            row.style.borderTopColor = borderColor;
            row.style.borderBottomColor = borderColor;
            row.style.borderLeftColor = borderColor;
            row.style.borderRightColor = borderColor;

            // Status icon
            string icon = isCompleted ? "\u2713" : isInProgress ? "\u25B6" : "\u25CB";
            Color iconColor = isCompleted
                ? new Color(0.4f, 0.85f, 0.5f)
                : isInProgress
                    ? new Color(0.45f, 0.65f, 1f)
                    : new Color(0.45f, 0.48f, 0.55f);

            var iconLabel = new Label(icon);
            iconLabel.style.fontSize = 16f;
            iconLabel.style.color = iconColor;
            iconLabel.style.marginRight = 10f;
            iconLabel.style.minWidth = 20f;
            iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconLabel.pickingMode = PickingMode.Ignore;
            row.Add(iconLabel);

            // Text column
            var textCol = new VisualElement();
            textCol.style.flexGrow = 1f;
            textCol.style.flexShrink = 1f;
            textCol.pickingMode = PickingMode.Ignore;

            var nameLabel = new Label($"{index}. {info.Name}");
            nameLabel.style.fontSize = 13f;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.color = new Color(0.9f, 0.92f, 0.95f);
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.pickingMode = PickingMode.Ignore;
            textCol.Add(nameLabel);

            if (!string.IsNullOrWhiteSpace(info.Description))
            {
                var descLabel = new Label(info.Description);
                descLabel.style.fontSize = 10f;
                descLabel.style.color = new Color(0.6f, 0.63f, 0.68f);
                descLabel.style.whiteSpace = WhiteSpace.Normal;
                descLabel.style.marginTop = 2f;
                descLabel.pickingMode = PickingMode.Ignore;
                textCol.Add(descLabel);
            }

            row.Add(textCol);

            // Step count badge
            string badgeText = isCompleted
                ? $"{info.TotalSteps}/{info.TotalSteps}"
                : $"{info.CompletedSteps}/{info.TotalSteps}";
            var badge = new Label(badgeText);
            badge.style.fontSize = 11f;
            badge.style.color = new Color(0.55f, 0.58f, 0.65f);
            badge.style.marginLeft = 8f;
            badge.style.minWidth = 36f;
            badge.style.unityTextAlign = TextAnchor.MiddleRight;
            badge.pickingMode = PickingMode.Ignore;
            row.Add(badge);

            // Click handler
            string assemblyId = info.AssemblyId;
            int globalStepIndex = info.FirstGlobalStepIndex;
            row.clicked += () =>
            {
                Dismiss();
                RuntimeEventBus.Publish(new AssemblyPickerDismissed(assemblyId, globalStepIndex));
            };

            return row;
        }

        // ════════════════════════════════════════════════════════════════════
        // Helpers
        // ════════════════════════════════════════════════════════════════════

        private struct AssemblyStepInfo
        {
            public string AssemblyId;
            public string Name;
            public string Description;
            public int TotalSteps;
            public int CompletedSteps;
            public int FirstGlobalStepIndex;
        }

        private static AssemblyStepInfo[] ComputeAssemblyStepInfo(
            string[] assemblyOrder,
            StepDefinition[] orderedSteps,
            MachinePackageDefinition package,
            int completedStepCount)
        {
            var result = new AssemblyStepInfo[assemblyOrder.Length];

            for (int i = 0; i < assemblyOrder.Length; i++)
            {
                string asmId = assemblyOrder[i];
                package.TryGetAssembly(asmId, out AssemblyDefinition asm);

                // Derive steps from step.assemblyId — no dependency on assembly.stepIds
                StepDefinition[] asmSteps = package.GetStepsForAssembly(asmId);
                int totalSteps = asmSteps.Length;
                int firstGlobalIndex = -1;

                // Find the first global step index for this assembly
                if (asmSteps.Length > 0)
                {
                    string firstStepId = asmSteps[0].id;
                    for (int s = 0; s < orderedSteps.Length; s++)
                    {
                        if (string.Equals(orderedSteps[s].id, firstStepId, StringComparison.OrdinalIgnoreCase))
                        {
                            firstGlobalIndex = s;
                            break;
                        }
                    }
                }

                // Count completed steps for this assembly
                int completedInAssembly = 0;
                for (int s = 0; s < asmSteps.Length; s++)
                {
                    // A step is completed if its global index < completedStepCount
                    for (int g = 0; g < orderedSteps.Length && g < completedStepCount; g++)
                    {
                        if (string.Equals(orderedSteps[g].id, asmSteps[s].id, StringComparison.OrdinalIgnoreCase))
                        {
                            completedInAssembly++;
                            break;
                        }
                    }
                }

                result[i] = new AssemblyStepInfo
                {
                    AssemblyId = asmId,
                    Name = asm?.name ?? asmId,
                    Description = asm?.learningFocus,
                    TotalSteps = totalSteps,
                    CompletedSteps = completedInAssembly,
                    FirstGlobalStepIndex = firstGlobalIndex >= 0 ? firstGlobalIndex : 0,
                };
            }

            return result;
        }
    }
}
