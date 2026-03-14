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

        // Named constants matching the harness private consts
        private const string PreviewScaffoldName = "Preview Scaffold";
        private const string SamplePartName       = "Sample Beam";
        private const string TargetMarkerName     = "Placement Target";

        private string[] _packageIds = System.Array.Empty<string>();
        private int _selectedIndex;
        private SerializedProperty _packageIdProp;
        private bool _layoutFoldout;

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

            // Draw all properties, replacing _packageId with the dropdown
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

                using (new EditorGUI.DisabledScope(iterator.name == "m_Script"))
                    EditorGUILayout.PropertyField(iterator, includeChildren: true);
            }

            serializedObject.ApplyModifiedProperties();

            // ── Preview Layout Capture ──────────────────────────────────────
            EditorGUILayout.Space(8);
            _layoutFoldout = EditorGUILayout.Foldout(_layoutFoldout, "Preview Layout Capture", true, EditorStyles.foldoutHeader);
            if (_layoutFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "Position 'Sample Beam' and 'Placement Target' in the scene, then capture their" +
                    " transforms into the active package's machine.json.\n\n" +
                    "• Capture Start — records where the part begins (before assembly step).\n" +
                    "• Capture Play  — records the assembled/final position (same as Blender layout).",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Capture Start Layout from Scene"))
                    CaptureFromScene(capturePlay: false);
                if (GUILayout.Button("Capture Play Layout from Scene"))
                    CaptureFromScene(capturePlay: true);
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
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
                }
                else if (_packageIdProp.stringValue != _packageIds[_selectedIndex])
                {
                    _selectedIndex = System.Array.IndexOf(_packageIds, _packageIdProp.stringValue);
                    if (_selectedIndex < 0) _selectedIndex = 0;
                    _packageIdProp.stringValue = _packageIds[_selectedIndex];
                }
            }

            if (GUILayout.Button(EditorGUIUtility.IconContent("Refresh"), GUILayout.Width(26), GUILayout.Height(18)))
                RefreshPackageList();

            EditorGUILayout.EndHorizontal();
        }

        // ── Capture from Scene ───────────────────────────────────────────────

        private void CaptureFromScene(bool capturePlay)
        {
            string packageId = _packageIds.Length > 0 ? _packageIds[_selectedIndex] : null;
            string jsonPath  = PackageJsonUtils.GetJsonPath(packageId);

            if (jsonPath == null)
            {
                Debug.LogError($"[SessionDriver] machine.json not found for package '{packageId}'.");
                return;
            }

            // Locate the Preview Scaffold anywhere in the scene (harness puts it as a child of itself)
            Transform previewScaffold = null;
            foreach (var t in Object.FindObjectsOfType<Transform>(includeInactive: true))
            {
                if (t.name == PreviewScaffoldName) { previewScaffold = t; break; }
            }

            if (previewScaffold == null)
            {
                Debug.LogError(
                    $"[SessionDriver] '{PreviewScaffoldName}' not found in scene. " +
                    "Open the mechanics test scene and ensure the harness has generated the preview scaffold.");
                return;
            }

            Transform samplePart   = previewScaffold.Find(SamplePartName);
            Transform targetMarker = previewScaffold.Find(TargetMarkerName);

            if (samplePart == null && targetMarker == null)
            {
                Debug.LogError($"[SessionDriver] Neither '{SamplePartName}' nor '{TargetMarkerName}' found under '{PreviewScaffoldName}'.");
                return;
            }

            var pkg = PackageJsonUtils.LoadPackage(packageId);
            if (pkg == null)
            {
                Debug.LogError($"[SessionDriver] Failed to deserialize machine.json for '{packageId}'.");
                return;
            }

            pkg.previewConfig ??= new PackagePreviewConfig();

            // ── Apply sample part transform ──────────────────────────────────
            if (samplePart != null && pkg.previewConfig.partPlacements?.Length > 0)
            {
                var placement = pkg.previewConfig.partPlacements[0];

                if (capturePlay)
                {
                    placement.playPosition = PackageJsonUtils.ToFloat3(samplePart.localPosition);
                    placement.playRotation = PackageJsonUtils.ToQuaternion(samplePart.localRotation);
                    placement.playScale    = PackageJsonUtils.ToFloat3(samplePart.localScale);
                    Debug.Log($"[SessionDriver] Captured PLAY transform for part '{placement.partId}' from scene.");
                }
                else
                {
                    placement.startPosition = PackageJsonUtils.ToFloat3(samplePart.localPosition);
                    placement.startRotation = PackageJsonUtils.ToQuaternion(samplePart.localRotation);
                    placement.startScale    = PackageJsonUtils.ToFloat3(samplePart.localScale);
                    Debug.Log($"[SessionDriver] Captured START transform for part '{placement.partId}' from scene.");
                }
            }

            // ── Apply target marker transform ────────────────────────────────
            if (targetMarker != null && pkg.previewConfig.targetPlacements?.Length > 0)
            {
                var placement = pkg.previewConfig.targetPlacements[0];
                placement.position = PackageJsonUtils.ToFloat3(targetMarker.localPosition);
                placement.rotation = PackageJsonUtils.ToQuaternion(targetMarker.localRotation);
                placement.scale    = PackageJsonUtils.ToFloat3(targetMarker.localScale);
                Debug.Log($"[SessionDriver] Captured transform for target '{placement.targetId}' from scene.");
            }

            PackageJsonUtils.WritePreviewConfig(jsonPath, pkg.previewConfig);
            AssetDatabase.Refresh();
            Debug.Log($"[SessionDriver] machine.json updated → {jsonPath}");
        }
    }
}
