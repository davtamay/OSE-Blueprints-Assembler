using System.IO;
using System.Linq;
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

    }
}
