using System.Collections.Generic;
using System.IO;
using OSE.Content;
using UnityEditor;
using UnityEngine;

namespace OSE.Editor
{
    /// <summary>
    /// Editor window for visualizing and refining spatial grab metadata for
    /// both tools (toolPose) and parts (grabConfig).
    ///
    /// Two preview modes:
    ///   - Embedded: self-contained 3D preview in the window (drag orbit, scroll zoom)
    ///   - SceneView: spawns into the scene with draggable handles — use standard Unity nav
    ///
    /// Open via: OSE > Grab Pose Editor
    /// </summary>
    public sealed class ToolPoseGizmoEditor : EditorWindow
    {
        private const string MenuPath = "OSE/Grab Pose Editor";
        private const string RightHandFbx =
            "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/RightHand.fbx";
        private const string LeftHandFbx =
            "Assets/Samples/XR Hands/1.7.3/HandVisualizer/Models/LeftHand.fbx";
        private const float RuntimeCursorScale = 0.16f;
        private const float EmbeddedPreviewHeight = 380f;

        private enum EditorTab { Tools, Parts }
        private enum PreviewMode { Embedded, SceneView }

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var w = GetWindow<ToolPoseGizmoEditor>("Grab Pose Editor");
            w.minSize = new Vector2(420, 500);
            w.Show();
        }

        // ── State ────────────────────────────────────────────────────────────────

        private EditorTab _tab = EditorTab.Tools;
        private PreviewMode _preview = PreviewMode.SceneView;
        private string[] _packageIds;
        private int _pkgIdx;
        private MachinePackageDefinition _pkg;
        private string _pkgId;
        private Vector2 _scroll;

        private int _toolIdx; private ToolDefinition _tool;
        private int _partIdx; private PartDefinition _part;

        // Shared model state
        private GameObject _model;
        private Bounds _bounds;
        private bool _loaded;

        // Embedded preview
        private PreviewRenderUtility _pu;
        private float _orbX = 20f, _orbY = -30f, _dist = 0.8f;
        private Vector3 _pivot;

        // Hand
        private GameObject _hand;
        private bool _showHand = true;
        private Transform[] _fingers;
        private Transform _thumbTip;

        // Scale
        private float _scale = 1f;
        private bool _runtimeScale;

        // Editable pose (model-local coordinates)
        private Vector3 _grip, _gripRot, _tip, _cursorOffset;
        private string _handedness = "right", _pose = "power_grip";
        private bool _dirty;
        private bool _showLabels = true;

        // Undo/redo for pose edits (in-editor, before writing to disk)
        private struct PoseSnapshot
        {
            public Vector3 grip, gripRot, tip, cursorOffset;
            public string handedness, pose;
        }
        private const int MaxUndoHistory = 50;
        private readonly List<PoseSnapshot> _undoStack = new();
        private readonly List<PoseSnapshot> _redoStack = new();
        private bool _snapshotPending;

        // File-level backup for machine.json writes
        private string _lastBackupPath;

        private bool IsTool => _tab == EditorTab.Tools;
        private bool IsEmbedded => _preview == PreviewMode.Embedded;

        private static readonly string[] Hands = { "right", "left", "either" };
        private static readonly string[] Poses = { "power_grip", "pinch", "precision", "two_hand" };

        private static readonly Color ColGrip   = new(0.2f, 0.5f, 1f, 1f);
        private static readonly Color ColTip    = new(1f, 0.15f, 0.15f, 1f);
        private static readonly Color ColTipA   = new(0.2f, 1f, 0.3f, 1f);
        private static readonly Color ColLine   = new(0.7f, 0.7f, 0.7f, 0.5f);
        private static readonly Color ColGrid   = new(0.35f, 0.35f, 0.35f, 0.4f);
        private static readonly Color ColCursor = new(1f, 0.85f, 0.1f, 0.9f);

        // ── Pose Undo / Redo ────────────────────────────────────────────────────

        private PoseSnapshot CaptureSnapshot() => new()
        {
            grip = _grip, gripRot = _gripRot, tip = _tip,
            cursorOffset = _cursorOffset, handedness = _handedness, pose = _pose
        };

        private void PushUndo()
        {
            _undoStack.Add(CaptureSnapshot());
            if (_undoStack.Count > MaxUndoHistory) _undoStack.RemoveAt(0);
            _redoStack.Clear();
        }

        private void UndoPose()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(CaptureSnapshot());
            var s = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            ApplySnapshot(s);
        }

        private void RedoPose()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(CaptureSnapshot());
            var s = _redoStack[_redoStack.Count - 1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            ApplySnapshot(s);
        }

        private void ApplySnapshot(PoseSnapshot s)
        {
            _grip = s.grip; _gripRot = s.gripRot; _tip = s.tip;
            _cursorOffset = s.cursorOffset; _handedness = s.handedness; _pose = s.pose;
            _dirty = true;
            UpdateHandXform(); UpdateFingerCurl();
            Repaint();
            if (!IsEmbedded) SceneView.RepaintAll();
        }

        /// <summary>Call before any edit to record current state for undo.</summary>
        private void BeginEdit()
        {
            if (!_snapshotPending)
            {
                PushUndo();
                _snapshotPending = true;
            }
        }

        /// <summary>Call after an edit sequence completes (mouse up, field commit).</summary>
        private void EndEdit() => _snapshotPending = false;

        // ── Lifecycle ────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            RefreshPackageList();
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void OnDestroy()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Cleanup();
        }

        private void Cleanup()
        {
            KillModel(); KillHand();
            if (_pu != null) { _pu.Cleanup(); _pu = null; }
        }

        private void EnsurePU()
        {
            if (_pu != null) return;
            _pu = new PreviewRenderUtility();
            _pu.camera.fieldOfView = 30f;
            _pu.camera.nearClipPlane = 0.001f;
            _pu.camera.farClipPlane = 100f;
            _pu.camera.clearFlags = CameraClearFlags.SolidColor;
            _pu.camera.backgroundColor = new Color(0.17f, 0.17f, 0.21f, 1f);
            _pu.lights[0].transform.rotation = Quaternion.Euler(30f, -45f, 0f);
            _pu.lights[0].intensity = 1.3f;
            _pu.lights[0].color = new Color(1f, 0.97f, 0.93f);
            if (_pu.lights.Length > 1)
            {
                _pu.lights[1].transform.rotation = Quaternion.Euler(-20f, 135f, 0f);
                _pu.lights[1].intensity = 0.5f;
            }
        }

        // ── Main GUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EditorGUILayout.Space(4);

            // Tab toggle
            EditorGUI.BeginChangeCheck();
            _tab = (EditorTab)GUILayout.Toolbar((int)_tab, new[] { "Tools", "Parts" });
            if (EditorGUI.EndChangeCheck())
            {
                KillModel(); KillHand();
                _tool = null; _part = null; _dirty = false;
                if (_pkg != null) PickFirst();
            }

            EditorGUILayout.Space(2);
            DrawPkgPicker();
            if (_pkg == null) return;
            DrawItemPicker();
            if (IsTool ? _tool == null : _part == null) return;

            // Preview mode toggle
            EditorGUI.BeginChangeCheck();
            _preview = (PreviewMode)GUILayout.Toolbar((int)_preview, new[] { "Embedded Preview", "SceneView Preview" });
            if (EditorGUI.EndChangeCheck()) SwitchPreviewMode();

            // Embedded preview renders inline; SceneView mode just shows a hint
            if (IsEmbedded)
                DrawEmbeddedPreview();
            else
                EditorGUILayout.HelpBox(
                    "Preview is in the SceneView. Use standard Unity navigation.\n" +
                    "Drag the blue/red handles to move grip/tip points.",
                    MessageType.Info);

            // Inspector panel
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.Space(4);
            DrawScaleUI();
            DrawBoundsInfo();
            EditorGUILayout.Space(6);
            DrawPoseFields();
            EditorGUILayout.Space(4);
            DrawHandUI();
            EditorGUILayout.Space(8);
            DrawActions();
            EditorGUILayout.Space(4);
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        private void SwitchPreviewMode()
        {
            // Tear down current model and re-load in the new mode
            string assetRef = IsTool ? _tool?.assetRef : _part?.assetRef;
            string itemId = IsTool ? _tool?.id : _part?.id;
            KillModel(); KillHand();
            if (!string.IsNullOrEmpty(assetRef))
                LoadModel(assetRef, itemId);
        }

        // ── Package / Item pickers ───────────────────────────────────────────────

        private void DrawPkgPicker()
        {
            if (_packageIds == null || _packageIds.Length == 0)
            {
                EditorGUILayout.HelpBox("No packages found in Assets/_Project/Data/Packages/", MessageType.Warning);
                if (GUILayout.Button("Refresh")) RefreshPackageList();
                return;
            }
            int i = EditorGUILayout.Popup("Package", _pkgIdx, _packageIds);
            if (i != _pkgIdx) { _pkgIdx = i; LoadPkg(_packageIds[i]); }
            if (_pkg == null && GUILayout.Button("Load")) LoadPkg(_packageIds[_pkgIdx]);
        }

        private void DrawItemPicker()
        {
            if (IsTool)
            {
                var arr = _pkg.tools;
                if (arr == null || arr.Length == 0) { EditorGUILayout.HelpBox("No tools.", MessageType.Info); return; }
                var n = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++) n[i] = $"{arr[i].id} ({arr[i].name})";
                int idx = EditorGUILayout.Popup("Tool", _toolIdx, n);
                if (idx != _toolIdx || _tool == null) { _toolIdx = idx; PickTool(arr[idx]); }
            }
            else
            {
                var arr = _pkg.parts;
                if (arr == null || arr.Length == 0) { EditorGUILayout.HelpBox("No parts.", MessageType.Info); return; }
                var n = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++) n[i] = $"{arr[i].id} ({arr[i].GetDisplayName()})";
                int idx = EditorGUILayout.Popup("Part", _partIdx, n);
                if (idx != _partIdx || _part == null) { _partIdx = idx; PickPart(arr[idx]); }
            }
        }

        // ── Embedded Preview ─────────────────────────────────────────────────────

        private void DrawEmbeddedPreview()
        {
            Rect r = GUILayoutUtility.GetRect(10, EmbeddedPreviewHeight, GUILayout.ExpandWidth(true));
            if (r.width < 10 || r.height < 10 || _pu == null || !_loaded) return;

            HandleEmbeddedInput(r);
            UpdateEmbeddedCamera();
            UpdateModelScale();
            UpdateHandXform();

            _pu.BeginPreview(r, GUIStyle.none);
            _pu.camera.Render();
            Handles.SetCamera(_pu.camera);
            DrawGrid();
            DrawMarkersVis();
            Texture tex = _pu.EndPreview();
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);

            var st = new GUIStyle(EditorStyles.miniLabel)
            { normal = { textColor = new Color(1, 1, 1, 0.35f) }, alignment = TextAnchor.LowerLeft };
            GUI.Label(new Rect(r.x + 4, r.yMax - 18, r.width, 16),
                "Drag: orbit  |  Scroll: zoom  |  Alt+Drag / Middle: pan", st);
        }

        private void HandleEmbeddedInput(Rect r)
        {
            Event e = Event.current;
            if (!r.Contains(e.mousePosition)) return;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.type)
            {
                case EventType.MouseDown when e.button is 0 or 1 or 2:
                    GUIUtility.hotControl = id; e.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    if (e.button == 2 || (e.button == 0 && e.alt))
                    {
                        float ps = _dist * 0.002f;
                        _pivot -= _pu.camera.transform.right * e.delta.x * ps
                                + _pu.camera.transform.up    * e.delta.y * ps;
                    }
                    else
                    {
                        _orbY += e.delta.x * 0.5f;
                        _orbX = Mathf.Clamp(_orbX - e.delta.y * 0.5f, -89f, 89f);
                    }
                    e.Use(); Repaint(); break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0; e.Use(); break;
                case EventType.ScrollWheel:
                    _dist = Mathf.Clamp(_dist * (1f + e.delta.y * 0.06f), 0.005f, 30f);
                    e.Use(); Repaint(); break;
            }
        }

        private void UpdateEmbeddedCamera()
        {
            float xr = _orbX * Mathf.Deg2Rad, yr = _orbY * Mathf.Deg2Rad;
            Vector3 off = new(
                _dist * Mathf.Cos(xr) * Mathf.Sin(yr),
                _dist * Mathf.Sin(xr),
                _dist * Mathf.Cos(xr) * Mathf.Cos(yr));
            _pu.camera.transform.position = _pivot + off;
            _pu.camera.transform.LookAt(_pivot);
        }

        private void DrawGrid()
        {
            float ext = Mathf.Max(_bounds.size.magnitude * _scale, 0.2f);
            float step = SnapGrid(ext);
            float y = _pivot.y - _bounds.extents.y * _scale;
            Handles.color = ColGrid;
            for (float v = -ext; v <= ext; v += step)
            {
                Handles.DrawLine(new Vector3(_pivot.x + v, y, _pivot.z - ext),
                                 new Vector3(_pivot.x + v, y, _pivot.z + ext));
                Handles.DrawLine(new Vector3(_pivot.x - ext, y, _pivot.z + v),
                                 new Vector3(_pivot.x + ext, y, _pivot.z + v));
            }
        }

        private static float SnapGrid(float ext)
        {
            float raw = ext / 5f;
            float p = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
            float n = raw / p;
            return (n < 2f ? 1f : n < 5f ? 2f : 5f) * p;
        }

        // ── SceneView Preview ────────────────────────────────────────────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (IsEmbedded || _model == null || !_loaded) return;

            Transform root = _model.transform;
            float baseSize = HandleUtility.GetHandleSize(root.position) * 0.06f;

            // Grip handle (blue, draggable position + rotation)
            Vector3 gripW = root.TransformPoint(_grip);
            Quaternion gripRotW = root.rotation * Quaternion.Euler(_gripRot);
            Handles.color = ColGrip;
            Handles.SphereHandleCap(0, gripW, Quaternion.identity, baseSize * 2.5f, EventType.Repaint);

            // Position handle
            EditorGUI.BeginChangeCheck();
            Vector3 newGrip = Handles.PositionHandle(gripW, gripRotW);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                _grip = root.InverseTransformPoint(newGrip);
                _dirty = true; UpdateHandXform(); Repaint();
            }
            else EndEdit();

            // Rotation handle (smaller disc rings around grip)
            EditorGUI.BeginChangeCheck();
            Quaternion newRotW = Handles.RotationHandle(gripRotW, gripW);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                Quaternion local = Quaternion.Inverse(root.rotation) * newRotW;
                _gripRot = local.eulerAngles;
                _dirty = true; UpdateHandXform(); Repaint();
            }
            else EndEdit();

            // Draw small coordinate frame showing grip orientation
            float axLen = baseSize * 6f;
            Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f);
            Handles.DrawLine(gripW, gripW + gripRotW * Vector3.right * axLen);
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
            Handles.DrawLine(gripW, gripW + gripRotW * Vector3.up * axLen);
            Handles.color = new Color(0.3f, 0.3f, 1f, 0.8f);
            Handles.DrawLine(gripW, gripW + gripRotW * Vector3.forward * axLen);

            if (_showLabels)
            {
                var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColGrip } };
                Handles.Label(gripW + Vector3.up * baseSize * 3f, "GRIP", s);
            }

            if (IsTool)
            {
                // Tip handle (red, draggable)
                Vector3 tipW = root.TransformPoint(_tip);
                Handles.color = ColTip;
                Handles.SphereHandleCap(0, tipW, Quaternion.identity, baseSize * 2.5f, EventType.Repaint);
                EditorGUI.BeginChangeCheck();
                Vector3 newTip = Handles.PositionHandle(tipW, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    _tip = root.InverseTransformPoint(newTip);
                    _dirty = true; Repaint();
                }
                else EndEdit();
                if (_showLabels)
                {
                    var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColTip } };
                    Handles.Label(tipW + Vector3.up * baseSize * 3f, "TIP", s);
                }

                // Grip-to-tip dashed line + direction arrow
                Handles.color = ColLine;
                Handles.DrawDottedLine(gripW, tipW, 4f);
                Vector3 dir = (tipW - gripW);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    float len = baseSize * 10f;
                    Vector3 end = gripW + dir.normalized * len;
                    Handles.color = ColTipA;
                    Handles.DrawAAPolyLine(3f, gripW, end);
                    Handles.ConeHandleCap(0, end, Quaternion.LookRotation(dir.normalized),
                        baseSize * 1.2f, EventType.Repaint);
                    if (_showLabels)
                    {
                        var s = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = ColTipA } };
                        Handles.Label(end + Vector3.up * baseSize, "tip dir", s);
                    }
                }
            }

            // Cursor offset handle (yellow, draggable from grip)
            if (IsTool)
            {
                Vector3 cursorW = root.TransformPoint(_grip + _cursorOffset);
                Handles.color = ColCursor;
                Handles.SphereHandleCap(0, cursorW, Quaternion.identity, baseSize * 2f, EventType.Repaint);
                EditorGUI.BeginChangeCheck();
                Vector3 newCursor = Handles.PositionHandle(cursorW, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginEdit();
                    _cursorOffset = root.InverseTransformPoint(newCursor) - _grip;
                    _dirty = true; Repaint();
                }
                else EndEdit();

                // Dashed line from grip to cursor offset
                Handles.color = ColCursor * 0.7f;
                Handles.DrawDottedLine(gripW, cursorW, 3f);
                if (_showLabels)
                {
                    var s = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColCursor } };
                    Handles.Label(cursorW + Vector3.up * baseSize * 3f, "CURSOR", s);
                }
            }
        }

        // ── Shared marker drawing (embedded mode, read-only) ─────────────────────

        private void DrawMarkersVis()
        {
            if (_model == null) return;
            Transform root = _model.transform;
            float sz = Mathf.Max(0.006f, _dist * 0.018f);

            Vector3 gw = root.TransformPoint(_grip);
            Quaternion gripRotW = root.rotation * Quaternion.Euler(_gripRot);
            Handles.color = ColGrip;
            Handles.SphereHandleCap(0, gw, Quaternion.identity, sz, EventType.Repaint);

            // Grip orientation frame
            float axLen = sz * 3f;
            Handles.color = new Color(1f, 0.3f, 0.3f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.right * axLen);
            Handles.color = new Color(0.3f, 1f, 0.3f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.up * axLen);
            Handles.color = new Color(0.3f, 0.3f, 1f, 0.7f);
            Handles.DrawLine(gw, gw + gripRotW * Vector3.forward * axLen);

            if (_showLabels) EmbLabel(gw, "GRIP", ColGrip);

            if (!IsTool) return;

            Vector3 tw = root.TransformPoint(_tip);
            Handles.color = ColTip;
            Handles.SphereHandleCap(0, tw, Quaternion.identity, sz, EventType.Repaint);
            if (_showLabels) EmbLabel(tw, "TIP", ColTip);

            Handles.color = ColLine;
            Handles.DrawDottedLine(gw, tw, 3f);
            Vector3 tipDir = (tw - gw);
            if (tipDir.sqrMagnitude > 0.0001f)
            {
                float len = _bounds.size.magnitude * _scale * 0.3f;
                Vector3 end = gw + tipDir.normalized * len;
                Handles.color = ColTipA;
                Handles.DrawAAPolyLine(3f, gw, end);
                float cone = Mathf.Max(len * 0.07f, 0.003f);
                Handles.ConeHandleCap(0, end, Quaternion.LookRotation(tipDir.normalized), cone, EventType.Repaint);
                if (_showLabels) EmbLabel(end, "tip dir", ColTipA);
            }

            // Cursor offset marker (yellow sphere + dashed line from grip)
            Vector3 cw = root.TransformPoint(_grip + _cursorOffset);
            Handles.color = ColCursor;
            Handles.SphereHandleCap(0, cw, Quaternion.identity, sz, EventType.Repaint);
            Handles.color = ColCursor * 0.7f;
            Handles.DrawDottedLine(gw, cw, 2f);
            if (_showLabels) EmbLabel(cw, "CURSOR", ColCursor);
        }


        private static void EmbLabel(Vector3 p, string text, Color c)
        {
            Handles.Label(p + Vector3.up * 0.006f,
                text, new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = c }, fontSize = 11 });
        }

        // ── Scale ────────────────────────────────────────────────────────────────

        private void DrawScaleUI()
        {
            EditorGUILayout.LabelField("Preview Scale", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            bool prevRts = _runtimeScale;
            _runtimeScale = EditorGUILayout.Toggle("Runtime Cursor Scale", _runtimeScale);
            if (_runtimeScale)
            {
                float so = IsTool && _tool != null ? _tool.scaleOverride : 0f;
                float eff = so > 0f ? RuntimeCursorScale * so : RuntimeCursorScale;
                EditorGUILayout.LabelField($"({eff:F3})", GUILayout.Width(70));
            }
            EditorGUILayout.EndHorizontal();
            if (_runtimeScale != prevRts) { UpdateEffectiveScale(); ResetCam(); Repaint(); }

            if (!_runtimeScale)
            {
                EditorGUI.BeginChangeCheck();
                _scale = EditorGUILayout.Slider("Scale", _scale, 0.01f, 5f);
                if (EditorGUI.EndChangeCheck()) { UpdateModelScale(); Repaint(); }
            }
        }

        private void UpdateEffectiveScale()
        {
            if (_runtimeScale)
            {
                float so = IsTool && _tool != null ? _tool.scaleOverride : 0f;
                _scale = so > 0f ? RuntimeCursorScale * so : RuntimeCursorScale;
            }
        }

        private void DrawBoundsInfo()
        {
            if (!_loaded) return;
            Vector3 s = _bounds.size;
            Vector3 sc = s * _scale;
            string info = $"Model bounds: {s.x:F3} x {s.y:F3} x {s.z:F3} m\n" +
                          $"At scale {_scale:F3}: {sc.x:F3} x {sc.y:F3} x {sc.z:F3} m";
            float maxDim = Mathf.Max(s.x, s.y, s.z);
            if (maxDim > 2f)
                info += $"\n(!) Model is {maxDim:F1}m — likely not in meter scale.";
            EditorGUILayout.HelpBox(info, MessageType.None);
        }

        private void UpdateModelScale()
        {
            if (_model != null)
                _model.transform.localScale = Vector3.one * _scale;
        }

        // ── Spatial Metadata ─────────────────────────────────────────────────────

        private void DrawPoseFields()
        {
            EditorGUILayout.LabelField("Spatial Metadata", EditorStyles.boldLabel);

            // Undo / Redo buttons
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_undoStack.Count == 0);
            if (GUILayout.Button("◄ Undo", GUILayout.Width(70))) UndoPose();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_redoStack.Count == 0);
            if (GUILayout.Button("Redo ►", GUILayout.Width(70))) RedoPose();
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"({_undoStack.Count} undo / {_redoStack.Count} redo)", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();

            _grip    = EditorGUILayout.Vector3Field("Grip Point (blue) — cursor/touch anchor", _grip);
            _gripRot = EditorGUILayout.Vector3Field("Grip Rotation (XR hand, model-local)", _gripRot);

            if (IsTool)
            {
                EditorGUILayout.Space(2);
                _tip = EditorGUILayout.Vector3Field("Tip Point (red)", _tip);
            }

            EditorGUILayout.Space(2);
            _cursorOffset = EditorGUILayout.Vector3Field("Cursor Offset (from grip)", _cursorOffset);
            EditorGUILayout.HelpBox(
                "Grip Point = XR hand grab position AND cursor base anchor.\n" +
                "Cursor Offset = additional shift for desktop/mobile cursor (0,0,0 = cursor at grip).\n" +
                "Grip Rotation = XR hand orientation only.\n" +
                "Cursor rotation uses orientationEuler or auto-detect.",
                MessageType.Info);

            EditorGUILayout.Space(2);
            int hi = System.Array.IndexOf(Hands, _handedness);
            hi = EditorGUILayout.Popup("Handedness", Mathf.Max(0, hi), Hands);
            _handedness = Hands[hi];

            int pi = System.Array.IndexOf(Poses, _pose);
            int newPi = EditorGUILayout.Popup("Pose Hint", Mathf.Max(0, pi), Poses);
            if (newPi != pi) { _pose = Poses[newPi]; UpdateFingerCurl(); }

            _showLabels = EditorGUILayout.Toggle("Show Labels", _showLabels);

            if (EditorGUI.EndChangeCheck())
            {
                PushUndo();
                _dirty = true; UpdateHandXform();
                Repaint();
                if (!IsEmbedded) SceneView.RepaintAll();
            }
        }

        // ── Hand Preview ─────────────────────────────────────────────────────────

        private void DrawHandUI()
        {
            EditorGUILayout.LabelField("Hand Preview", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            _showHand = EditorGUILayout.Toggle("Show Hand at Grip", _showHand);
            if (EditorGUI.EndChangeCheck())
            {
                if (_showHand) SpawnHand(); else KillHand();
                Repaint();
            }
            if (_showHand && _scale < 0.5f)
                EditorGUILayout.HelpBox("Hand may appear oversized at small preview scale. " +
                    "Increase scale to see realistic hand-tool proportion.", MessageType.Info);
        }

        private void SpawnHand()
        {
            KillHand();
            if (!_loaded) return;
            // Embedded mode needs PreviewRenderUtility
            if (IsEmbedded && _pu == null) return;

            string path = _handedness == "left" ? LeftHandFbx : RightHandFbx;
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pfb == null)
            {
                Debug.LogWarning($"[GrabPoseEditor] Hand not found: {path}. Import XR Hands HandVisualizer sample.");
                return;
            }

            _hand = Instantiate(pfb);
            _hand.name = "[GrabPosePreview] Hand";
            _hand.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in _hand.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            ApplyHandMat(_hand);
            CacheBones(_hand);

            if (IsEmbedded)
                _pu.AddSingleGO(_hand);
            // SceneView mode: hand is just a scene object with HideAndDontSave

            UpdateHandXform();
            UpdateFingerCurl();
        }

        private void KillHand()
        {
            if (_hand != null) DestroyImmediate(_hand);
            _hand = null; _fingers = null; _thumbTip = null;
        }

        private void UpdateHandXform()
        {
            if (_hand == null || _model == null) return;
            Transform root = _model.transform;
            _hand.transform.position = root.TransformPoint(_grip);

            if (_gripRot.sqrMagnitude > 0.001f)
            {
                // Grip rotation is authored — it controls hand orientation directly.
                // This matches runtime: XRGrabInteractable.attachTransform.rotation = gripRotation.
                _hand.transform.rotation = root.rotation * Quaternion.Euler(_gripRot);
            }
            else
            {
                // Fallback: derive hand orientation from grip→tip shaft direction.
                Vector3 tipDir = (_tip - _grip);
                Vector3 shaft = IsTool && tipDir.sqrMagnitude > 0.001f
                    ? root.TransformDirection(tipDir.normalized)
                    : root.TransformDirection(Vector3.forward);
                Vector3 palm = Vector3.Cross(shaft, Vector3.up).normalized;
                if (palm.sqrMagnitude < 0.01f) palm = Vector3.Cross(shaft, Vector3.right).normalized;
                _hand.transform.rotation = Quaternion.LookRotation(-shaft, palm);
            }
            _hand.transform.localScale = Vector3.one;
        }

        private void UpdateFingerCurl()
        {
            if (_hand == null || _fingers == null) return;
            float curl  = _pose switch { "power_grip" => 80f, "pinch" => 20f, "precision" => 45f, "two_hand" => 70f, _ => 60f };
            float thumb = _pose switch { "power_grip" => 50f, "pinch" => 40f, "precision" => 35f, "two_hand" => 45f, _ => 40f };
            for (int i = 0; i < _fingers.Length; i++)
            {
                if (_fingers[i] == null) continue;
                float c = (_pose == "pinch" && i == 0) ? 50f : curl;
                _fingers[i].localRotation = Quaternion.Euler(c, 0, 0);
            }
            if (_thumbTip?.parent != null) _thumbTip.parent.localRotation = Quaternion.Euler(0, 0, thumb);
        }

        private void CacheBones(GameObject hand)
        {
            var map = new Dictionary<string, Transform>();
            foreach (var t in hand.GetComponentsInChildren<Transform>(true))
                map[t.name.ToLowerInvariant()] = t;
            _fingers = new[]
            {
                Bone(map, "indexproximal", "index1", "b_r_index1", "b_l_index1"),
                Bone(map, "middleproximal", "middle1", "b_r_middle1", "b_l_middle1"),
                Bone(map, "ringproximal", "ring1", "b_r_ring1", "b_l_ring1"),
                Bone(map, "littleproximal", "pinky1", "b_r_pinky1", "b_l_pinky1"),
            };
            _thumbTip = Bone(map, "thumbtip", "thumb_tip", "b_r_thumb_tip", "b_l_thumb_tip", "thumb3");
        }

        private static Transform Bone(Dictionary<string, Transform> m, params string[] names)
        {
            foreach (string n in names)
                if (m.TryGetValue(n.ToLowerInvariant(), out var t)) return t;
            foreach (string n in names)
            {
                string lo = n.ToLowerInvariant();
                foreach (var kv in m) if (kv.Key.Contains(lo)) return kv.Value;
            }
            return null;
        }

        private static void ApplyHandMat(GameObject hand)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh == null) return;
            Material m = new(sh) { name = "HandPreview", hideFlags = HideFlags.HideAndDontSave };
            Color skin = new(0.85f, 0.72f, 0.6f, 0.75f);
            m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.renderQueue = 3100;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", skin);
            if (m.HasProperty("_Color")) m.SetColor("_Color", skin);
            foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = m;
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private void DrawActions()
        {
            if (GUILayout.Button("Auto-Detect (PCA from mesh)"))
            {
                PushUndo();
                AutoDetect();
                _dirty = true; Repaint();
            }

            EditorGUILayout.BeginHorizontal();
            if (IsEmbedded && GUILayout.Button("Reset Camera")) ResetCam();
            if (!IsEmbedded && GUILayout.Button("Frame in SceneView")) FrameInScene();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUI.BeginDisabledGroup(!_dirty);
            GUI.backgroundColor = _dirty ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            string lbl = IsTool ? "Write toolPose to machine.json" : "Write grabConfig to machine.json";
            if (GUILayout.Button(lbl, GUILayout.Height(28))) { WriteJson(); _dirty = false; }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();

            // Revert last file write from backup
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_lastBackupPath) || !File.Exists(_lastBackupPath));
            if (GUILayout.Button("Revert Last Write (restore backup)"))
            {
                string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
                if (jsonPath != null && !string.IsNullOrEmpty(_lastBackupPath) && File.Exists(_lastBackupPath))
                {
                    File.Copy(_lastBackupPath, jsonPath, true);
                    AssetDatabase.Refresh();
                    Debug.Log($"[GrabPoseEditor] Reverted to backup: {_lastBackupPath}");
                    string saved = IsTool ? _tool?.id : _part?.id;
                    LoadPkg(_pkgId);
                    if (IsTool && _pkg.tools != null)
                    {
                        _toolIdx = System.Array.FindIndex(_pkg.tools, t => t.id == saved);
                        if (_toolIdx >= 0) PickTool(_pkg.tools[_toolIdx]);
                    }
                    else if (!IsTool && _pkg.parts != null)
                    {
                        _partIdx = System.Array.FindIndex(_pkg.parts, p => p.id == saved);
                        if (_partIdx >= 0) PickPart(_pkg.parts[_partIdx]);
                    }
                    _lastBackupPath = null;
                }
            }
            EditorGUI.EndDisabledGroup();
        }

        private void DrawStatus()
        {
            string tier;
            if (IsTool && _tool != null)
                tier = _tool.HasToolPose ? "Tier 1 (toolPose)" : _tool.HasOrientationOverride ? "Tier 2 (orientationEuler)" : "Tier 3 (auto-detect)";
            else if (!IsTool && _part != null)
                tier = _part.HasGrabConfig ? "Authored (grabConfig)" : "None (default)";
            else return;
            string extra = IsTool ? $"  |  Grip-Tip: {(_tip - _grip).magnitude:F3}m" : "";
            EditorGUILayout.HelpBox($"Resolution: {tier}{extra}", MessageType.Info);
        }

        private void ResetCam()
        {
            _orbX = 20f; _orbY = -30f;
            _dist = Mathf.Max(_bounds.size.magnitude * 1.5f * _scale, 0.1f);
            _pivot = _bounds.center * _scale;
            Repaint();
        }

        private void FrameInScene()
        {
            if (_model == null) return;
            Selection.activeGameObject = _model;
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { sv.FrameSelected(); sv.Repaint(); }
        }

        // ── Model Loading ────────────────────────────────────────────────────────

        private void LoadModel(string assetRef, string itemId)
        {
            KillModel(); KillHand();
            if (string.IsNullOrEmpty(assetRef) || string.IsNullOrEmpty(_pkgId)) return;

            string path = $"Assets/_Project/Data/Packages/{_pkgId}/{assetRef}";
            var pfb = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (pfb == null) { Debug.LogWarning($"[GrabPoseEditor] Not found: {path}"); return; }

            _model = Instantiate(pfb);
            _model.name = $"[GrabPosePreview] {itemId}";
            _model.hideFlags = HideFlags.HideAndDontSave;
            foreach (var c in _model.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);

            _bounds = CalcBounds(_model);
            _loaded = true;

            if (IsEmbedded)
            {
                EnsurePU();
                _pu.AddSingleGO(_model);
            }
            // SceneView mode: model is just a scene object with HideAndDontSave

            UpdateEffectiveScale();
            UpdateModelScale();
            ResetCam();
            if (_showHand) SpawnHand();
        }

        private void KillModel()
        {
            if (_model != null) DestroyImmediate(_model);
            _model = null; _loaded = false;
        }

        private static Bounds CalcBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // ── Selection ────────────────────────────────────────────────────────────

        private void PickTool(ToolDefinition t)
        {
            _tool = t; _part = null;
            _undoStack.Clear(); _redoStack.Clear();

            // Set pose fields BEFORE loading the model, so SpawnHand sees correct values
            if (t.toolPose != null)
            {
                _grip         = t.toolPose.GetGripPoint();
                _gripRot      = new Vector3(t.toolPose.gripRotation.x, t.toolPose.gripRotation.y, t.toolPose.gripRotation.z);
                _tip          = t.toolPose.GetTipPoint();
                _cursorOffset = t.toolPose.GetCursorOffset();
                _handedness   = t.toolPose.handedness ?? "right";
                _pose         = t.toolPose.poseHint ?? "power_grip";
            }
            else
            {
                ResetFields();
                // Pre-populate gripRotation from legacy orientationEuler so the
                // cursor orientation is preserved when authoring a new toolPose.
                if (t.HasOrientationOverride)
                    _gripRot = t.orientationEuler;
            }

            LoadModel(t.assetRef, t.id);
            _dirty = false;
        }

        private void PickPart(PartDefinition p)
        {
            _part = p; _tool = null;
            _undoStack.Clear(); _redoStack.Clear();

            // Set pose fields BEFORE loading the model
            if (p.grabConfig != null)
            {
                _grip    = p.grabConfig.GetGripPoint();
                _gripRot = p.grabConfig.HasGripRotation
                    ? new Vector3(p.grabConfig.gripRotation.x, p.grabConfig.gripRotation.y, p.grabConfig.gripRotation.z)
                    : Vector3.zero;
                _handedness = p.grabConfig.handedness ?? "either";
                _pose       = p.grabConfig.poseHint ?? "power_grip";
            }
            else { _grip = _gripRot = _cursorOffset = Vector3.zero; _handedness = "either"; _pose = "power_grip"; }
            _tip = Vector3.zero;

            LoadModel(p.assetRef, p.id);
            _dirty = false;
        }

        private void PickFirst()
        {
            if (IsTool && _pkg.tools is { Length: > 0 }) { _toolIdx = 0; PickTool(_pkg.tools[0]); }
            else if (!IsTool && _pkg.parts is { Length: > 0 }) { _partIdx = 0; PickPart(_pkg.parts[0]); }
        }

        private void ResetFields()
        {
            _grip = _gripRot = _tip = _cursorOffset = Vector3.zero;
            _handedness = "right"; _pose = "power_grip";
        }

        // ── Auto-detect (PCA) ────────────────────────────────────────────────────

        private void AutoDetect()
        {
            if (_model == null) return;
            var filters = _model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length == 0) return;
            Transform root = _model.transform;

            var pts = new List<Vector3>();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices)
                    pts.Add(root.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (pts.Count < 3) return;

            Vector3 cent = Vector3.zero;
            for (int i = 0; i < pts.Count; i++) cent += pts[i];
            cent /= pts.Count;

            float cxx = 0, cyy = 0, czz = 0, cxy = 0, cxz = 0, cyz = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 d = pts[i] - cent;
                cxx += d.x * d.x; cyy += d.y * d.y; czz += d.z * d.z;
                cxy += d.x * d.y; cxz += d.x * d.z; cyz += d.y * d.z;
            }
            float n = pts.Count;
            cxx /= n; cyy /= n; czz /= n; cxy /= n; cxz /= n; cyz /= n;

            Vector3 ev = new Vector3(1, 1, 1).normalized;
            for (int iter = 0; iter < 50; iter++)
            {
                Vector3 mv = new(
                    cxx * ev.x + cxy * ev.y + cxz * ev.z,
                    cxy * ev.x + cyy * ev.y + cyz * ev.z,
                    cxz * ev.x + cyz * ev.y + czz * ev.z);
                float mag = mv.magnitude;
                if (mag < 1e-10f) break;
                ev = mv / mag;
            }

            float maxP = float.NegativeInfinity, minP = float.PositiveInfinity;
            Vector3 tipPt = cent, gripPt = cent;
            for (int i = 0; i < pts.Count; i++)
            {
                float proj = Vector3.Dot(pts[i] - cent, ev);
                if (proj > maxP) { maxP = proj; tipPt = pts[i]; }
                if (proj < minP) { minP = proj; gripPt = pts[i]; }
            }
            gripPt = Vector3.Lerp(gripPt, cent, 0.3f);

            _grip = gripPt; _gripRot = Vector3.zero;
            _handedness = IsTool ? "right" : "either";
            _pose = "power_grip";

            if (IsTool)
                _tip = tipPt;

            Debug.Log($"[GrabPoseEditor] PCA: grip={_grip}" + (IsTool ? $" tip={_tip}" : ""));
            UpdateHandXform();
            UpdateFingerCurl();
            if (!IsEmbedded) SceneView.RepaintAll();
        }

        // ── Package loading ──────────────────────────────────────────────────────

        private void RefreshPackageList()
        {
            string root = PackageJsonUtils.AuthoringRoot;
            if (!Directory.Exists(root)) { _packageIds = new string[0]; return; }
            var dirs = Directory.GetDirectories(root);
            var ids = new List<string>();
            foreach (var d in dirs)
                if (File.Exists(Path.Combine(d, "machine.json"))) ids.Add(Path.GetFileName(d));
            _packageIds = ids.ToArray();
        }

        private void LoadPkg(string id)
        {
            _pkg = PackageJsonUtils.LoadPackage(id);
            _pkgId = id;
            _toolIdx = _partIdx = 0;
            _tool = null; _part = null;
            KillModel(); KillHand();
            if (_pkg != null) PickFirst();
        }

        // ── Write back ───────────────────────────────────────────────────────────

        private void WriteJson()
        {
            string itemId = IsTool ? _tool?.id : _part?.id;
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(_pkgId)) return;
            string jsonPath = PackageJsonUtils.GetJsonPath(_pkgId);
            if (jsonPath == null) { Debug.LogError($"[GrabPoseEditor] machine.json not found for '{_pkgId}'"); return; }

            string blockName = IsTool ? "toolPose" : "grabConfig";
            string poseJson = BuildPoseJson();

            string json = File.ReadAllText(jsonPath);

            // Validate JSON parses before we touch it
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GrabPoseEditor] machine.json is already invalid, aborting write.\n{ex.Message}");
                return;
            }

            if (!TryInjectBlock(ref json, itemId, blockName, poseJson))
            {
                Debug.LogError($"[GrabPoseEditor] Could not find '{itemId}' in {jsonPath}");
                return;
            }

            // Validate the result parses before saving
            try { JsonUtility.FromJson<MachinePackageDefinition>(json); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[GrabPoseEditor] Write would produce invalid JSON, aborting.\n{ex.Message}");
                return;
            }

            // Backup before writing
            string backupDir = Path.Combine(Path.GetDirectoryName(jsonPath), ".pose_backups");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);
            string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(backupDir, $"machine_{ts}.json");
            File.Copy(jsonPath, backupPath, true);
            _lastBackupPath = backupPath;

            File.WriteAllText(jsonPath, json);
            AssetDatabase.Refresh();
            Debug.Log($"[GrabPoseEditor] Wrote {blockName} for '{itemId}' (backup: {backupPath})");

            // Reload the package definition so status display updates (tier info),
            // but do NOT re-pick the tool/part — that would destroy the model/hand
            // and recreate from JSON, causing visual jumps from float round-trip.
            _pkg = PackageJsonUtils.LoadPackage(_pkgId);
            if (_pkg != null)
            {
                if (IsTool && _pkg.tools != null)
                {
                    int idx = System.Array.FindIndex(_pkg.tools, t => t.id == itemId);
                    if (idx >= 0) { _toolIdx = idx; _tool = _pkg.tools[idx]; }
                }
                else if (!IsTool && _pkg.parts != null)
                {
                    int idx = System.Array.FindIndex(_pkg.parts, p => p.id == itemId);
                    if (idx >= 0) { _partIdx = idx; _part = _pkg.parts[idx]; }
                }
            }
        }

        private string BuildPoseJson()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("{\n");
            string ind = "                "; // match typical machine.json indentation

            sb.Append($"{ind}\"gripPoint\": {{ \"x\": {R(_grip.x).ToString(inv)}, \"y\": {R(_grip.y).ToString(inv)}, \"z\": {R(_grip.z).ToString(inv)} }}");

            // Always write gripRotation — the cursor ghost is camera-parented and needs
            // an explicit rotation. Zero = identity = mesh raw orientation relative to camera.
            sb.Append($",\n{ind}\"gripRotation\": {{ \"x\": {R(_gripRot.x).ToString(inv)}, \"y\": {R(_gripRot.y).ToString(inv)}, \"z\": {R(_gripRot.z).ToString(inv)} }}");

            if (IsTool)
                sb.Append($",\n{ind}\"tipPoint\": {{ \"x\": {R(_tip.x).ToString(inv)}, \"y\": {R(_tip.y).ToString(inv)}, \"z\": {R(_tip.z).ToString(inv)} }}");

            // Only write cursorOffset when non-zero (most tools leave it at 0,0,0)
            if (_cursorOffset.sqrMagnitude > 0.0001f)
                sb.Append($",\n{ind}\"cursorOffset\": {{ \"x\": {R(_cursorOffset.x).ToString(inv)}, \"y\": {R(_cursorOffset.y).ToString(inv)}, \"z\": {R(_cursorOffset.z).ToString(inv)} }}");

            sb.Append($",\n{ind}\"handedness\": \"{_handedness}\"");
            sb.Append($",\n{ind}\"poseHint\": \"{_pose}\"");
            sb.Append($"\n            }}");
            return sb.ToString();
        }

        /// <summary>
        /// Finds the JSON object with the given "id" value, removes any existing key named
        /// <paramref name="block"/>, and appends the new block. All brace/bracket matching
        /// is string-aware (ignores braces inside JSON string literals).
        /// </summary>
        private static bool TryInjectBlock(ref string json, string id, string block, string blockJson)
        {
            // Find "id": "<id>" in the tools/parts array.
            // Strategy: search for the full pattern "id": "tool_mig_torch" as a single string
            // to avoid false positives from "toolId", sub-object "id" keys, etc.
            string fullPattern = $"\"id\": \"{id}\"";
            int idPos = json.IndexOf(fullPattern, System.StringComparison.Ordinal);

            // Also try without space after colon
            if (idPos < 0)
            {
                fullPattern = $"\"id\":\"{id}\"";
                idPos = json.IndexOf(fullPattern, System.StringComparison.Ordinal);
            }

            // Try with variable whitespace: find "id" then verify value
            if (idPos < 0)
            {
                string idNeedle = $"\"id\"";
                int searchFrom = 0;
                while (searchFrom < json.Length)
                {
                    int found = json.IndexOf(idNeedle, searchFrom, System.StringComparison.Ordinal);
                    if (found < 0) break;

                    int afterKey = found + idNeedle.Length;
                    int colonPos = SkipWhitespace(json, afterKey);
                    if (colonPos < json.Length && json[colonPos] == ':')
                    {
                        int valuePos = SkipWhitespace(json, colonPos + 1);
                        string idValue = $"\"{id}\"";
                        if (valuePos + idValue.Length <= json.Length
                            && json.Substring(valuePos, idValue.Length) == idValue)
                        {
                            idPos = found;
                            break;
                        }
                    }
                    searchFrom = found + 1;
                }
            }

            if (idPos < 0)
            {
                Debug.LogWarning($"[GrabPoseEditor] TryInjectBlock: pattern not found for id='{id}'");
                return false;
            }

            // Walk backwards (string-aware) to find the opening { of this object
            int objStart = FindObjectStart(json, idPos);
            if (objStart < 0) return false;

            // Walk forward (string-aware) to find the closing } of this object
            int objEnd = FindMatchingClose(json, objStart);
            if (objEnd < 0) return false;

            // Extract the object substring
            string obj = json.Substring(objStart, objEnd - objStart + 1);

            // Remove existing block key if present
            string cleaned = RemoveKey(obj, block);

            // Detect indentation from the "id" line
            string indent = "            "; // fallback 12 spaces
            int lineStart = json.LastIndexOf('\n', idPos);
            if (lineStart >= 0)
            {
                int firstNonSpace = lineStart + 1;
                while (firstNonSpace < json.Length && json[firstNonSpace] == ' ') firstNonSpace++;
                indent = new string(' ', firstNonSpace - lineStart - 1);
            }

            // Insert the block before the closing }
            int lastBrace = cleaned.LastIndexOf('}');
            string before = cleaned.Substring(0, lastBrace).TrimEnd();
            string after = cleaned.Substring(lastBrace);

            string injected = before + ",\n" + indent + $"\"{block}\": " + blockJson + "\n"
                + indent.Substring(0, System.Math.Max(0, indent.Length - 4)) + after;

            json = json.Substring(0, objStart) + injected + json.Substring(objEnd + 1);
            return true;
        }

        private static int SkipWhitespace(string s, int from)
        {
            while (from < s.Length && char.IsWhiteSpace(s[from])) from++;
            return from;
        }

        /// <summary>
        /// From the "id" key position, walk backward to find the opening { of
        /// this object. In well-formed machine.json, "id" is always the first key
        /// so we just need to skip whitespace + newlines back to {.
        /// </summary>
        private static int FindObjectStart(string json, int from)
        {
            for (int i = from - 1; i >= 0; i--)
            {
                char c = json[i];
                if (c == '{') return i;
                if (!char.IsWhiteSpace(c)) return -1; // unexpected char before "id"
            }
            return -1;
        }

        /// <summary>
        /// From an opening { or [, find the matching close. String-aware.
        /// </summary>
        private static int FindMatchingClose(string json, int openPos)
        {
            char open = json[openPos];
            char close = open == '{' ? '}' : ']';
            int depth = 0;
            bool inStr = false;
            for (int i = openPos; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '\\' && inStr) { i++; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == open) depth++;
                if (c == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>
        /// Removes a top-level key:value pair from a JSON object string.
        /// Uses string-aware scanning for nested braces/brackets.
        /// </summary>
        private static string RemoveKey(string obj, string key)
        {
            string needle = $"\"{key}\"";
            int keyIdx = obj.IndexOf(needle, System.StringComparison.Ordinal);
            if (keyIdx < 0) return obj;

            // Find colon
            int colon = obj.IndexOf(':', keyIdx + needle.Length);
            if (colon < 0) return obj;

            // Find value start
            int valStart = SkipWhitespace(obj, colon + 1);
            if (valStart >= obj.Length) return obj;

            // Find value end
            int valEnd;
            char first = obj[valStart];
            if (first == '{' || first == '[')
            {
                valEnd = FindMatchingClose(obj, valStart);
                if (valEnd < 0) return obj;
            }
            else if (first == '"')
            {
                // String value — find unescaped closing quote
                valEnd = valStart;
                for (int i = valStart + 1; i < obj.Length; i++)
                {
                    if (obj[i] == '\\') { i++; continue; }
                    if (obj[i] == '"') { valEnd = i; break; }
                }
            }
            else
            {
                // Number/bool/null — scan to next , or } or ]
                valEnd = valStart;
                for (int i = valStart; i < obj.Length; i++)
                {
                    char c = obj[i];
                    if (c == ',' || c == '}' || c == ']') { valEnd = i - 1; break; }
                    valEnd = i;
                }
            }

            // Determine removal range including comma + surrounding whitespace
            int removeStart = keyIdx;
            int removeEnd = valEnd + 1;

            // Look for leading comma
            int ls = removeStart - 1;
            while (ls >= 0 && (obj[ls] == ' ' || obj[ls] == '\t' || obj[ls] == '\r' || obj[ls] == '\n')) ls--;
            if (ls >= 0 && obj[ls] == ',')
            {
                removeStart = ls;
            }
            else
            {
                // No leading comma — eat trailing comma
                int ts = removeEnd;
                while (ts < obj.Length && (obj[ts] == ' ' || obj[ts] == '\t')) ts++;
                if (ts < obj.Length && obj[ts] == ',')
                    removeEnd = ts + 1;
            }

            // Eat trailing newline
            while (removeEnd < obj.Length && (obj[removeEnd] == ' ' || obj[removeEnd] == '\r' || obj[removeEnd] == '\n'))
                removeEnd++;

            return obj.Substring(0, removeStart) + obj.Substring(removeEnd);
        }

        private static float R(float v) => Mathf.Round(v * 100000f) / 100000f;
    }
}
