using System.IO;
using System.Linq;
using OSE.Content;
using OSE.Runtime.Preview;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    [CustomEditor(typeof(SessionDriver))]
    public sealed class SessionDriverEditor : UnityEditor.Editor
    {
        private const string PackagesDataPath = "Assets/_Project/Data/Packages";

        private string[] _packageIds = System.Array.Empty<string>();
        private int _selectedIndex;
        private SerializedProperty _packageIdProp;

        // Step navigation state (edit-mode only)
        private MachinePackageDefinition _editorPkg;
        private StepDefinition[]         _orderedSteps = System.Array.Empty<StepDefinition>();
        private string[]                 _stepLabels   = System.Array.Empty<string>();
        private string                   _cachedPkgId;

        private void OnEnable()
        {
            _packageIdProp = serializedObject.FindProperty("_packageId");
            RefreshPackageList();
        }

        private void RefreshPackageList()
        {
            string fullPath = Path.Combine(Application.dataPath, "../", PackagesDataPath);
            if (Directory.Exists(fullPath))
            {
                _packageIds = Directory.GetDirectories(fullPath)
                    .Select(d => Path.GetFileName(d))
                    .OrderBy(n => n)
                    .ToArray();
            }
            else
            {
                _packageIds = System.Array.Empty<string>();
            }

            string current = _packageIdProp.stringValue;
            _selectedIndex = System.Array.IndexOf(_packageIds, current);
            if (_selectedIndex < 0 && _packageIds.Length > 0)
                _selectedIndex = 0;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.name == "_packageId")
                {
                    DrawPackageDropdown();
                    continue;
                }

                if (iterator.name == "_previewStepSequenceIndex")
                {
                    // Only replace with nav UI in edit mode when preview is enabled
                    var previewProp = serializedObject.FindProperty("_previewInEditMode");
                    if (!Application.isPlaying && (previewProp == null || previewProp.boolValue))
                    {
                        // Use FindProperty instead of the iterator — the iterator becomes
                        // invalid after the loop exits, which breaks GenericMenu callbacks
                        // that fire on the next frame.
                        DrawStepNav(serializedObject.FindProperty("_previewStepSequenceIndex"));
                        continue;
                    }
                }

                using (new EditorGUI.DisabledScope(iterator.name == "m_Script"))
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Package dropdown ─────────────────────────────────────────────────

        private void DrawPackageDropdown()
        {
            EditorGUILayout.BeginHorizontal();

            if (_packageIds.Length == 0)
            {
                EditorGUILayout.PropertyField(_packageIdProp, new GUIContent("Package Id"));
            }
            else
            {
                _selectedIndex = Mathf.Clamp(_selectedIndex, 0, _packageIds.Length - 1);
                int newIndex = EditorGUILayout.Popup(new GUIContent("Package Id"), _selectedIndex, _packageIds);
                if (newIndex != _selectedIndex)
                {
                    _selectedIndex = newIndex;
                    _packageIdProp.stringValue = _packageIds[_selectedIndex];
                    _cachedPkgId = null; // invalidate step cache on package change
                }
                else if (_packageIdProp.stringValue != _packageIds[_selectedIndex])
                {
                    _selectedIndex = System.Array.IndexOf(_packageIds, _packageIdProp.stringValue);
                    if (_selectedIndex < 0) _selectedIndex = 0;
                    _packageIdProp.stringValue = _packageIds[_selectedIndex];
                    _cachedPkgId = null;
                }
            }

            if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(26), GUILayout.Height(18)))
            {
                RefreshPackageList();
                _cachedPkgId = null;
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── Step navigation ──────────────────────────────────────────────────

        private void EnsureStepsLoaded()
        {
            string pkgId = _packageIdProp.stringValue;
            if (pkgId == _cachedPkgId) return;

            _cachedPkgId  = pkgId;
            _editorPkg    = string.IsNullOrEmpty(pkgId) ? null : PackageJsonUtils.LoadPackage(pkgId);
            _orderedSteps = _editorPkg?.GetOrderedSteps() ?? System.Array.Empty<StepDefinition>();
            _stepLabels   = BuildStepLabels(_orderedSteps, _editorPkg);
        }

        private static string[] BuildStepLabels(StepDefinition[] steps, MachinePackageDefinition pkg)
        {
            var labels = new string[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                string toolName = "(no tool)";
                if (step.relevantToolIds != null && step.relevantToolIds.Length > 0 && pkg?.tools != null)
                {
                    foreach (var td in pkg.tools)
                        if (td != null && td.id == step.relevantToolIds[0]) { toolName = td.name; break; }
                }
                string profile = string.IsNullOrEmpty(step.profile) ? "" : $"  ·  {step.profile}";
                labels[i] = $"[{step.sequenceIndex}] {step.name}  ·  {toolName}{profile}";
            }
            return labels;
        }

        private void DrawStepNav(SerializedProperty stepSeqProp)
        {
            EnsureStepsLoaded();

            if (_orderedSteps.Length == 0)
            {
                EditorGUILayout.PropertyField(stepSeqProp, new GUIContent("Preview Step Sequence"));
                return;
            }

            // Find the current step index.
            // _orderedSteps.Length is reserved for the "Fully Assembled" virtual entry.
            int currentSeq = stepSeqProp.intValue;
            int lastStepSeq = _orderedSteps[_orderedSteps.Length - 1].sequenceIndex;
            bool isFullyAssembled = currentSeq > lastStepSeq;
            int currentIdx = isFullyAssembled ? _orderedSteps.Length : 0;
            if (!isFullyAssembled)
            {
                for (int i = 0; i < _orderedSteps.Length; i++)
                {
                    if (_orderedSteps[i].sequenceIndex == currentSeq) { currentIdx = i; break; }
                }
            }

            // Total entries: all steps + 1 virtual "Fully Assembled" entry
            int totalEntries = _orderedSteps.Length + 1;

            EditorGUILayout.LabelField("Preview Step", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginDisabledGroup(currentIdx <= 0);
            if (GUILayout.Button("◄|", GUILayout.Width(28)))
                CommitStep(stepSeqProp, 0);
            if (GUILayout.Button("◄", GUILayout.Width(28)))
                CommitStep(stepSeqProp, currentIdx - 1);
            EditorGUI.EndDisabledGroup();

            string navLabel = isFullyAssembled
                ? "Fully Assembled"
                : $"Step {currentIdx + 1} / {_orderedSteps.Length}";
            if (GUILayout.Button(navLabel, EditorStyles.popup))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < _orderedSteps.Length; i++)
                {
                    int captured = i;
                    menu.AddItem(new GUIContent(_stepLabels[i]), i == currentIdx,
                        () => CommitStep(stepSeqProp, captured));
                }
                // Add "Fully Assembled" entry at the end
                menu.AddSeparator("");
                menu.AddItem(
                    new GUIContent("Fully Assembled — all parts at play positions"),
                    isFullyAssembled,
                    () => CommitStep(stepSeqProp, _orderedSteps.Length));
                menu.ShowAsContext();
            }

            EditorGUI.BeginDisabledGroup(currentIdx >= totalEntries - 1);
            if (GUILayout.Button("►", GUILayout.Width(28)))
                CommitStep(stepSeqProp, currentIdx + 1);
            if (GUILayout.Button("|►", GUILayout.Width(28)))
                CommitStep(stepSeqProp, _orderedSteps.Length);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            // Step info card
            if (isFullyAssembled)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Fully Assembled", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("All parts shown at their assembled (play) positions.", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            else
            {
                var step = _orderedSteps[currentIdx];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"[{step.sequenceIndex}] {step.name}", EditorStyles.boldLabel);
                string stepProfile = string.IsNullOrEmpty(step.profile) ? "—" : step.profile;
                int targetCount = step.targetIds?.Length ?? 0;
                EditorGUILayout.LabelField($"Profile: {stepProfile}  ·  {targetCount} target(s)", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
        }

        private void CommitStep(SerializedProperty stepSeqProp, int newIdx)
        {
            if (newIdx < 0 || newIdx > _orderedSteps.Length) return;

            int sequenceIndex;
            if (newIdx == _orderedSteps.Length)
            {
                // "Fully Assembled" virtual entry — use lastStepSeq + 1 so
                // ApplyStepAwarePositions treats it as past the final step.
                sequenceIndex = _orderedSteps[_orderedSteps.Length - 1].sequenceIndex + 1;
            }
            else
            {
                sequenceIndex = _orderedSteps[newIdx].sequenceIndex;
            }

            // SetEditModeStep first — fires EditModeStepChanged while the old value
            // is still set, so the event fires with changed=true and syncs the window.
            ((SessionDriver)target).SetEditModeStep(sequenceIndex);
            // Then update the serialized field so the inspector reflects the new step.
            stepSeqProp.intValue = sequenceIndex;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
